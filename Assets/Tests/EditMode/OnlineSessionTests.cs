using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Services.Core;
using Unity.Services.Multiplayer;

/// <summary>
/// La partie en ligne, son salon et son code (OnlineSession, 2026-10-13). Ce qui se joue avec le service
/// (créer, rejoindre, lancer) a été vérifié à deux instances ; ici, ce qui se décide sans lui : le code tapé
/// par le joueur, les réglages publiés par l'hôte, la capacité tenue par le verrou et le retrait du dernier
/// arrivé, les profils de connexion de deux instances sur un PC, et ce qu'on dit au joueur quand ça échoue.
/// </summary>
public class OnlineSessionTests
{
    [Test]
    public void LeCode_SeTapeCommeOnVeut_EtArriveCommeLeServiceLAttend()
    {
        Assert.AreEqual("HM8TQL", OnlineSession.NormalizeCode("hm8tql"), "Les minuscules passent.");
        Assert.AreEqual("HM8TQL", OnlineSession.NormalizeCode(" hm8-tql "), "Espaces et tiret, recopiés d'un message, disparaissent.");
        Assert.AreEqual("HM8TQL", OnlineSession.NormalizeCode("HM8 TQL\n"));
        Assert.AreEqual("", OnlineSession.NormalizeCode(null));
        Assert.AreEqual("", OnlineSession.NormalizeCode("é#!?"), "Rien qui ressemble à un code : vide, donc refusé avant d'appeler le service.");
        Assert.AreEqual(16, OnlineSession.NormalizeCode(new string('A', 40)).Length, "Un collage trop long est coupé.");
    }

    [Test]
    public void LesReglagesPublies_FontLAllerRetour_PourToutesLesCombinaisons()
    {
        for (int capacity = MatchRules.MinCapacity; capacity <= MatchRules.MaxCapacity; capacity++)
        {
            foreach (DuelOrganization organization in Enum.GetValues(typeof(DuelOrganization)))
            {
                foreach (bool interventions in new[] { false, true })
                {
                    var rules = new MatchRules { capacity = (byte)capacity, organization = organization, spectatorInterventions = interventions };
                    MatchRules back = OnlineSession.ParseRules(OnlineSession.FormatRules(rules));
                    Assert.AreEqual(rules.capacity, back.capacity, OnlineSession.FormatRules(rules));
                    Assert.AreEqual(rules.organization, back.organization, OnlineSession.FormatRules(rules));
                    Assert.AreEqual(rules.spectatorInterventions, back.spectatorInterventions, OnlineSession.FormatRules(rules));
                }
            }
        }
    }

    [Test]
    public void DesReglagesIllisibles_RestentDansLeursBornes()
    {
        MatchRules defaults = MatchRules.Default;
        MatchRules empty = OnlineSession.ParseRules("");
        Assert.AreEqual(defaults.capacity, empty.capacity, "Rien de publié : les réglages par défaut.");
        Assert.AreEqual(defaults.organization, empty.organization);

        MatchRules garbage = OnlineSession.ParseRules("abc;;x");
        Assert.AreEqual(defaults.capacity, garbage.capacity);
        Assert.AreEqual(defaults.organization, garbage.organization);

        MatchRules outOfRange = OnlineSession.ParseRules("9;7;0");
        Assert.AreEqual(MatchRules.MaxCapacity, outOfRange.capacity, "Plus de places que le jeu n'en tient : ramené au maximum.");
        Assert.AreEqual(DuelOrganization.KingOfTheHill, outOfRange.organization, "Une organisation inconnue (une autre version) : celle par défaut.");
        Assert.IsFalse(outOfRange.spectatorInterventions);

        Assert.AreEqual(MatchRules.MinCapacity, OnlineSession.ParseRules("1;0;1").capacity, "Moins de deux places : un duel reste à deux.");
    }

    [Test]
    public void LeSalon_SeFerme_DesQuIlEstComplet()
    {
        Assert.IsFalse(OnlineSession.ShouldLock(1, 4));
        Assert.IsFalse(OnlineSession.ShouldLock(3, 4));
        Assert.IsTrue(OnlineSession.ShouldLock(4, 4), "Complet : plus personne n'entre.");
        Assert.IsTrue(OnlineSession.ShouldLock(5, 4), "Au-delà (deux arrivées à la même seconde) : toujours fermé.");
        Assert.IsTrue(OnlineSession.ShouldLock(2, 2));
    }

    [Test]
    public void AuDelaDeLaCapacite_LesDerniersArrivesRepartent_JamaisLHote()
    {
        var t0 = new DateTime(2026, 10, 13, 15, 0, 0, DateTimeKind.Utc);
        var players = new List<(string id, DateTime joined)>
        {
            ("c", t0.AddSeconds(3)),
            ("hote", t0.AddSeconds(10)), // l'hôte d'un salon repris (l'ancien est parti) peut être arrivé tard
            ("a", t0.AddSeconds(1)),
            ("d", t0.AddSeconds(4)),
            ("b", t0.AddSeconds(2)),
        };

        CollectionAssert.AreEqual(new[] { "c", "d" }, OnlineSession.PlayersBeyondCapacity(players, "hote", 3),
            "L'hôte reste, puis les deux premiers arrivés ; les autres repartent.");
        CollectionAssert.IsEmpty(OnlineSession.PlayersBeyondCapacity(players, "hote", 6), "Assez de places : personne ne repart.");
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, OnlineSession.PlayersBeyondCapacity(players, "hote", 0),
            "Une capacité absurde garde au moins l'hôte.");

        var sameSecond = new List<(string id, DateTime joined)> { ("hote", t0), ("y", t0.AddSeconds(1)), ("x", t0.AddSeconds(1)) };
        CollectionAssert.AreEqual(new[] { "y" }, OnlineSession.PlayersBeyondCapacity(sameSecond, "hote", 2),
            "Deux arrivées à la même seconde : départagées toujours de la même façon.");
    }

    [Test]
    public void ChaqueInstanceDuJeuSurUnPC_ASonPropreProfil_ValidePourLeService()
    {
        var names = new HashSet<string>
        {
            OnlineSession.ProfileFor(true, false, 0),
            OnlineSession.ProfileFor(true, true, 7048),
            OnlineSession.ProfileFor(false, false, 0),
            OnlineSession.ProfileFor(false, false, 1),
            OnlineSession.ProfileFor(false, false, 15),
        };
        Assert.AreEqual(5, names.Count, "L'Editor, son clone et chaque build d'un même PC : des joueurs différents.");

        var rule = new Regex(@"^[a-zA-Z0-9_-]{1,30}$"); // la règle du service d'authentification
        foreach (string name in names) Assert.IsTrue(rule.IsMatch(name), $"« {name} » serait refusé par le service.");
        Assert.IsTrue(rule.IsMatch(OnlineSession.ProfileFor(true, true, int.MaxValue)));
    }

    [Test]
    public void LesEchecs_SeDisentEnFrancais_SansJargon()
    {
        StringAssert.Contains("complète", OnlineSession.DescribeError(new SessionException("session is full", SessionError.Unknown, null)));
        StringAssert.Contains("complète", OnlineSession.DescribeError(new SessionException("session is locked", SessionError.Unknown, null)),
            "Le salon ne se ferme que plein : fermé veut dire complet.");
        StringAssert.Contains("code", OnlineSession.DescribeError(new SessionException("session not found", SessionError.SessionNotFound, null)));
        StringAssert.Contains("code", OnlineSession.DescribeError(new SessionException("session code 'ZZ' contains an invalid character", SessionError.Unknown, null)));
        StringAssert.Contains("réessaie", OnlineSession.DescribeError(new SessionException("too many requests", SessionError.RateLimitExceeded, null)));
        StringAssert.Contains("internet", OnlineSession.DescribeError(new RequestFailedException(0, "Cannot resolve destination host")));
        Assert.AreEqual("Tape le code que l'hôte t'a donné.", OnlineSession.DescribeError(new OnlineSessionException("Tape le code que l'hôte t'a donné.")),
            "Un refus du jeu passe tel quel.");

        // Netcode a refusé la connexion à la partie lancée : la raison de l'hôte, déjà en français.
        string full = "La partie est pleine : 2 joueurs au plus.";
        Assert.AreEqual("Connexion refusée : " + full,
            OnlineSession.DescribeError(new SessionException("Joining network failed.", SessionError.NetworkSetupFailed, null), full));
    }

    [Test]
    public void UneConnexionALaPartieQuiEchoue_DitPourquoi()
    {
        Assert.AreEqual("L'hôte a quitté la partie.", OnlineSession.DescribeNetworkFailure("Disconnected due to host shutting down.", "HM8TQL"));
        StringAssert.StartsWith("Connexion refusée : Versions différentes",
            OnlineSession.DescribeNetworkFailure("Versions différentes : l'hôte joue la 0.2.0, toi la 0.1.0. Il faut la même build.", "HM8TQL"));
        StringAssert.Contains("HM8TQL", OnlineSession.DescribeNetworkFailure("", "HM8TQL"), "Sans raison : le relais. On redonne le code pour réessayer.");
        StringAssert.Contains("HM8TQL", OnlineSession.DescribeNetworkFailure("[Disconnect Event] Transport shutdown", "HM8TQL"));
    }

    [Test]
    public void LesReglagesDuReseauLocal_SeLisentEnUnePhrase()
    {
        var rules = new MatchRules { capacity = 3, organization = DuelOrganization.FixedDuelists, spectatorInterventions = false };
        string text = LobbyController.DescribeRules(rules);
        StringAssert.Contains("3 places", text);
        StringAssert.Contains("duellistes fixes", text);
        StringAssert.Contains("sans interventions", text);
    }
}
