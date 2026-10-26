using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Le menu refait le 2026-10-17 d'après les maquettes de l'utilisateur : ce qui s'y décide sans écran. Les
/// modes de l'écran Jouer et les réglages qu'ils ouvrent (GameModeCatalog), qui le salon met en tête
/// d'affiche (LobbyLineup), le nom des couleurs des robots (PaletteNames), la carte de l'arène
/// (MenuArtwork.MapUV), la couleur que chaque joueur publie au salon (OnlineSession.ParseColor).
/// </summary>
public class MenuScreensTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    // ------------------------------------------------------------------ Les modes de l'écran Jouer

    [Test]
    public void LesHuitModes_SontDansLOrdreDeLaMaquette_EtToutEstRempli()
    {
        IReadOnlyList<GameMode> modes = GameModeCatalog.Modes;
        Assert.AreEqual(8, modes.Count, "Les neuf cartes de la maquette, moins l'événement légendaire (2026-10-25).");
        string theme = File.ReadAllText("Assets/_ProjectUI/Shared/Theme.uss");
        for (int i = 0; i < modes.Count; i++)
        {
            if (i > 0) Assert.Greater((int)modes[i].id, (int)modes[i - 1].id, "Dans l'ordre de l'énumération.");
            Assert.AreNotEqual(GameModeId.Legendary, modes[i].id,
                "L'événement légendaire est une surprise rare des modes arcade, pas un mode (GDD § 10, 2026-10-25).");
            Assert.IsFalse(string.IsNullOrWhiteSpace(modes[i].title), modes[i].id.ToString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(modes[i].subtitle), modes[i].id.ToString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(modes[i].description), modes[i].id.ToString());
            StringAssert.Contains("." + modes[i].icon + " ", theme, $"L'icône de {modes[i].id} doit exister dans Theme.uss.");
            Assert.AreEqual(modes[i].id, GameModeCatalog.Get(modes[i].id).id);
        }
    }

    [Test]
    public void ChaqueModeJouable_OuvreUnSalonQuiPorteSonNom()
    {
        foreach (GameModeId id in new[] { GameModeId.Classic, GameModeId.KingOfTheHill, GameModeId.Spectators })
        {
            Assert.IsTrue(GameModeCatalog.Get(id).CanCreate, id.ToString());
            Assert.AreEqual(id, GameModeCatalog.ModeOf(GameModeCatalog.RulesFor(id)), $"Le salon de {id} doit se nommer {id}.");
        }
        foreach (GameModeId id in new[] { GameModeId.WeaponGifts, GameModeId.Rocks })
        {
            Assert.IsTrue(GameModeCatalog.Get(id).CanCreate, "Les dons d'armes et les cailloux existent déjà, dans le mode Spectateurs.");
            Assert.AreEqual(GameModeId.Spectators, GameModeCatalog.ModeOf(GameModeCatalog.RulesFor(id)));
        }
        foreach (GameModeId id in new[] { GameModeId.Tournament, GameModeId.Darkness, GameModeId.Ranked })
        {
            Assert.IsFalse(GameModeCatalog.Get(id).CanCreate, $"{id} n'existe pas encore : sa carte ne crée rien.");
            Assert.IsNotEmpty(GameModeCatalog.Badge(GameModeCatalog.Get(id).availability), "Elle dit pourquoi.");
        }
        Assert.IsEmpty(GameModeCatalog.Badge(GameModeAvailability.Playable), "Un mode jouable n'a pas de badge.");
    }

    [Test]
    public void LesReglagesDesModes_SontCeuxQuOnAnnonce()
    {
        MatchRules classic = GameModeCatalog.RulesFor(GameModeId.Classic);
        Assert.AreEqual(2, classic.capacity, "Le classique : un contre un, personne d'autre.");
        Assert.AreEqual(DuelOrganization.FixedDuelists, classic.organization);
        Assert.IsFalse(classic.spectatorInterventions);

        MatchRules king = GameModeCatalog.RulesFor(GameModeId.KingOfTheHill);
        Assert.AreEqual(DuelOrganization.KingOfTheHill, king.organization);
        Assert.Greater(king.capacity, 2, "Une rotation demande une file.");
        Assert.IsFalse(king.spectatorInterventions, "La tribune regarde : c'est ce qui le distingue du mode Spectateurs.");

        MatchRules stands = GameModeCatalog.RulesFor(GameModeId.Spectators);
        Assert.AreEqual(MatchRules.MaxCapacity, stands.capacity);
        Assert.IsTrue(stands.spectatorInterventions);

        foreach (GameMode mode in GameModeCatalog.Modes)
        {
            MatchRules rules = GameModeCatalog.RulesFor(mode.id);
            Assert.AreEqual(rules.capacity, rules.Sanitized().capacity, $"{mode.id} : des réglages dans leurs bornes.");
        }
    }

    [Test]
    public void LeSalon_NommeLeModeQueJouentSesReglages_QuelsQuIlsSoient()
    {
        for (int capacity = MatchRules.MinCapacity; capacity <= MatchRules.MaxCapacity; capacity++)
        {
            var fixedDuel = new MatchRules { capacity = (byte)capacity, organization = DuelOrganization.FixedDuelists, spectatorInterventions = false };
            var rotation = new MatchRules { capacity = (byte)capacity, organization = DuelOrganization.KingOfTheHill, spectatorInterventions = false };
            var lively = new MatchRules { capacity = (byte)capacity, organization = DuelOrganization.FixedDuelists, spectatorInterventions = true };
            Assert.AreEqual("Classique", GameModeCatalog.Describe(fixedDuel));
            Assert.AreEqual("Roi de la colline", GameModeCatalog.Describe(rotation));
            Assert.AreEqual("Spectateurs", GameModeCatalog.Describe(lively), "Une tribune qui intervient, c'est le mode Spectateurs.");
        }
    }

    /// <summary>
    /// Le nombre de manches est un réglage du salon (GDD § 5, 2026-10-25) : un BO5 par défaut, ou un BO3.
    /// Il voyage dans les réglages publiés du salon, et un réglage d'avant ce choix (sans manches, ou d'une
    /// ancienne version du salon) vaut un BO5.
    /// </summary>
    [Test]
    public void LesManches_SontUnReglageDuSalon_BO5ParDefautOuBO3()
    {
        Assert.AreEqual(MatchRules.Bo5RoundsToWin, MatchRules.Default.roundsToWin, "Un BO5 par défaut.");
        Assert.AreEqual(3, MatchRules.Bo5RoundsToWin);
        Assert.AreEqual(2, MatchRules.Bo3RoundsToWin);
        foreach (GameMode mode in GameModeCatalog.Modes)
            Assert.AreEqual(MatchRules.Bo5RoundsToWin, GameModeCatalog.RulesFor(mode.id).roundsToWin, $"{mode.id} s'ouvre en BO5.");

        var bo3 = new MatchRules { capacity = 2, roundsToWin = MatchRules.Bo3RoundsToWin };
        Assert.AreEqual(MatchRules.Bo3RoundsToWin, bo3.Sanitized().roundsToWin, "Le BO3 reste un BO3.");
        Assert.AreEqual("BO3", bo3.Format);
        Assert.AreEqual("BO5", MatchRules.Default.Format);
        foreach (byte invalid in new byte[] { 0, 1, 4, 9 })
            Assert.AreEqual(MatchRules.Bo5RoundsToWin, new MatchRules { roundsToWin = invalid }.Sanitized().roundsToWin,
                $"{invalid} manches : ramené au BO5.");

        MatchRules back = OnlineSession.ParseRules(OnlineSession.FormatRules(bo3));
        Assert.AreEqual(MatchRules.Bo3RoundsToWin, back.roundsToWin, "Le BO3 voyage dans les réglages du salon.");
        Assert.AreEqual(MatchRules.Bo5RoundsToWin, OnlineSession.ParseRules("4;0;1").roundsToWin,
            "Un salon d'avant le réglage (trois champs) : un BO5.");
        StringAssert.Contains("BO3", LobbyController.DescribeRules(bo3));
    }

    // ------------------------------------------------------------------ Le salon : qui en tête d'affiche

    [Test]
    public void SeulLHote_EstEnTeteDAffiche_TantQuIlEstSeul()
    {
        LobbyLineup<string> lineup = LobbyLineup<string>.Arrange(new[] { "hôte" }, 4);
        Assert.IsTrue(lineup.hasFirst);
        Assert.AreEqual("hôte", lineup.first);
        Assert.IsFalse(lineup.secondKnown);
        Assert.IsFalse(lineup.secondAmongStands, "Personne d'autre : le second est attendu, pas parmi les spectateurs.");
        Assert.AreEqual(0, lineup.stands.Count);
        Assert.AreEqual(2, lineup.freeStands, "Quatre places : deux duellistes, deux places de tribune.");
    }

    [Test]
    public void ADeux_LesDeuxDuellistesSontConnus()
    {
        LobbyLineup<string> lineup = LobbyLineup<string>.Arrange(new[] { "hôte", "ami" }, 6);
        Assert.AreEqual("hôte", lineup.first);
        Assert.IsTrue(lineup.secondKnown);
        Assert.AreEqual("ami", lineup.second);
        Assert.AreEqual(0, lineup.stands.Count);
        Assert.AreEqual(4, lineup.freeStands);
    }

    [Test]
    public void ATroisEtPlus_LeSecondReste_ParmiLesSpectateurs()
    {
        LobbyLineup<string> lineup = LobbyLineup<string>.Arrange(new[] { "hôte", "b", "c" }, 6);
        Assert.AreEqual("hôte", lineup.first, "L'hôte est toujours le premier duelliste (son robot naît le premier).");
        Assert.IsFalse(lineup.secondKnown, "Le second sera le premier des autres à arriver dans l'arène : rien ne le prédit.");
        Assert.IsTrue(lineup.secondAmongStands);
        CollectionAssert.AreEqual(new[] { "b", "c" }, lineup.stands, "Les autres, dans leur ordre d'arrivée.");
        Assert.AreEqual(3, lineup.freeStands, "Six places, trois joueurs : trois places libres.");

        LobbyLineup<string> full = LobbyLineup<string>.Arrange(new[] { "a", "b", "c", "d", "e", "f" }, 6);
        Assert.AreEqual(5, full.stands.Count);
        Assert.AreEqual(0, full.freeStands, "Complet.");
    }

    [Test]
    public void UnSalonVide_NeMontrePersonne()
    {
        LobbyLineup<string> lineup = LobbyLineup<string>.Arrange(new string[0], 4);
        Assert.IsFalse(lineup.hasFirst);
        Assert.IsFalse(lineup.secondKnown);
        Assert.AreEqual(0, lineup.stands.Count);
        Assert.AreEqual(2, lineup.freeStands);
        Assert.AreEqual(0, LobbyLineup<string>.Arrange(null, 2).freeStands, "Deux places : un duel sans tribune.");
    }

    // ------------------------------------------------------------------ Les couleurs des robots

    [Test]
    public void ChaqueCouleurDeLaPalette_ASonNom_LuSurSaTeinte()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        IReadOnlyList<Color32> palette = prefab.GetComponent<PlayerPaint>().Palette;
        Assert.Greater(palette.Count, 1);
        var names = new HashSet<string>();
        foreach (Color32 color in palette)
        {
            string name = PaletteNames.NameOf(color);
            Assert.IsTrue(names.Add(name), $"Deux couleurs de la palette s'appellent « {name} » : les tuiles de Personnalisation les confondraient.");
        }
        CollectionAssert.IsSubsetOf(new[] { "Orange", "Bleu", "Magenta", "Vert", "Cyan", "Violet", "Jaune" }, names,
            "La palette documentée (CLAUDE.md) : orange, bleu, magenta, vert, cyan, violet, jaune.");
    }

    [Test]
    public void LesNomsDesCouleurs_SuiventLaTeinte()
    {
        Assert.AreEqual("Rouge", PaletteNames.NameOf(new Color32(230, 30, 30, 255)));
        Assert.AreEqual("Orange", PaletteNames.NameOf(new Color32(255, 106, 19, 255)));
        Assert.AreEqual("Jaune", PaletteNames.NameOf(new Color32(255, 210, 30, 255)));
        Assert.AreEqual("Vert", PaletteNames.NameOf(new Color32(46, 224, 90, 255)));
        Assert.AreEqual("Cyan", PaletteNames.NameOf(new Color32(0, 229, 229, 255)));
        Assert.AreEqual("Bleu", PaletteNames.NameOf(new Color32(30, 144, 255, 255)));
        Assert.AreEqual("Violet", PaletteNames.NameOf(new Color32(143, 75, 255, 255)));
        Assert.AreEqual("Magenta", PaletteNames.NameOf(new Color32(255, 45, 155, 255)));
        Assert.AreEqual("Gris", PaletteNames.NameOf(new Color32(120, 122, 125, 255)), "Sans couleur : gris, blanc ou noir.");
        Assert.AreEqual("Blanc", PaletteNames.NameOf(new Color32(245, 245, 245, 255)));
        Assert.AreEqual("Noir", PaletteNames.NameOf(new Color32(10, 10, 12, 255)));
    }

    // ------------------------------------------------------------------ La carte de l'arène

    [Test]
    public void LaCarte_MetLePlusZDuMondeEnHaut_EtLePlusXADroite()
    {
        var world = new Rect(-40f, -10f, 80f, 100f);   // x de -40 à 40, z de -10 à 90
        AssertNear(new Vector2(0f, 1f), MenuArtwork.MapUV(world, new Vector2(-40f, -10f)), "Le coin x et z minimaux : en bas à gauche.");
        AssertNear(new Vector2(1f, 0f), MenuArtwork.MapUV(world, new Vector2(40f, 90f)), "Le coin x et z maximaux : en haut à droite.");
        AssertNear(new Vector2(0.5f, 0.5f), MenuArtwork.MapUV(world, new Vector2(0f, 40f)), "Le milieu.");
        AssertNear(new Vector2(0.5f, 0.88f), MenuArtwork.MapUV(world, new Vector2(0f, 2f)), "Le point d'apparition A, près du bord sud : en bas.");
        AssertNear(new Vector2(0.5f, 0.5f), MenuArtwork.MapUV(new Rect(0f, 0f, 0f, 10f), new Vector2(3f, 3f)), "Un rectangle vide ne divise pas par zéro.");
    }

    private static void AssertNear(Vector2 expected, Vector2 actual, string message)
    {
        Assert.AreEqual(expected.x, actual.x, 1e-4f, message);
        Assert.AreEqual(expected.y, actual.y, 1e-4f, message);
    }

    // ------------------------------------------------------------------ La couleur publiée au salon

    [Test]
    public void LaCouleurPublieeAuSalon_SeRelitBornee()
    {
        Assert.IsTrue(OnlineSession.ParseColor("3", out int index));
        Assert.AreEqual(3, index);
        Assert.IsTrue(OnlineSession.ParseColor("-1", out index), "Sans choix.");
        Assert.AreEqual(-1, index);
        Assert.IsFalse(OnlineSession.ParseColor("", out index), "Un joueur d'une ancienne version n'en publie pas.");
        Assert.AreEqual(-1, index);
        Assert.IsFalse(OnlineSession.ParseColor("abc", out index));
        Assert.IsFalse(OnlineSession.ParseColor("9999", out index), "Un rang absurde ne sert à rien.");
        Assert.AreEqual(-1, index);
        Assert.IsFalse(OnlineSession.ParseColor(null, out index));
    }
}
