using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Les règles de la manche et de la session corrigées à la relecture complète du 2026-10-12.
///
/// Le décompte (RoundManager.RoundScorers) : un échange de tirs faisait tomber les deux joueurs,
/// et la manche allait au « survivant », mort lui aussi, toujours le même. Le serveur refuse
/// désormais le tir d'un robot mort ; le décompte, lui, ne fait jamais marquer un mort.
///
/// L'accueil (SessionFlow.ShouldApprove) : un troisième joueur entrait dans un duel et faisait
/// marquer deux joueurs à chaque mort, et rien ne refusait une build d'une autre version. Depuis la
/// tribune (2026-10-13), la partie accueille sa capacité, réglée par l'hôte : au-delà de deux, les
/// joueurs attendent leur tour en spectateurs (RosterTests).
/// </summary>
public class SessionRulesTests
{
    private static List<int> Scorers(params bool[] dead)
    {
        var scorers = new List<int> { 42 }; // vidé par la fonction
        RoundManager.RoundScorers(dead, scorers);
        return scorers;
    }

    [Test]
    public void PersonneNEstMort_LaMancheContinue()
    {
        var scorers = new List<int>();
        Assert.IsFalse(RoundManager.RoundScorers(new[] { false, false }, scorers));
        Assert.IsEmpty(scorers);
    }

    [Test]
    public void UnJoueurTombe_LAutreMarque_QuelQueSoitLOrdre()
    {
        CollectionAssert.AreEqual(new[] { 1 }, Scorers(true, false), "Le premier est mort : le second marque.");
        CollectionAssert.AreEqual(new[] { 0 }, Scorers(false, true), "Le second est mort : le premier marque.");
    }

    [Test]
    public void LesDeuxTombent_PersonneNeMarque()
    {
        // Le cas de l'échange de tirs : avant, le second de la liste marquait, mort lui aussi.
        var scorers = new List<int>();
        Assert.IsTrue(RoundManager.RoundScorers(new[] { true, true }, scorers), "La manche est finie.");
        Assert.IsEmpty(scorers, "Un robot mort ne marque jamais.");
    }

    /// <summary>La capacité est un réglage de partie (2026-10-13) : au-delà de deux, les joueurs
    /// attendent sur la tribune ; au-delà de la capacité, ils sont refusés.</summary>
    [Test]
    public void LaPartieAccueilleSaCapacite_PasUnDePlus()
    {
        Assert.IsTrue(SessionFlow.ShouldApprove(0, 4, "0.1.0", "0.1.0", out _), "L'hôte lui-même.");
        Assert.IsTrue(SessionFlow.ShouldApprove(1, 4, "0.1.0", "0.1.0", out _), "Son adversaire.");
        Assert.IsTrue(SessionFlow.ShouldApprove(3, 4, "0.1.0", "0.1.0", out _), "Le second spectateur.");
        Assert.IsFalse(SessionFlow.ShouldApprove(4, 4, "0.1.0", "0.1.0", out string reason), "Un cinquième joueur.");
        StringAssert.Contains("pleine", reason);
        StringAssert.Contains("4", reason, "La raison dit combien de places compte la partie.");
        Assert.IsFalse(SessionFlow.ShouldApprove(2, 2, "0.1.0", "0.1.0", out _), "Une partie à deux places reste un duel sans tribune.");
    }

    [Test]
    public void LesReglagesDePartie_RestentDansLeursBornes()
    {
        Assert.AreEqual(4, MatchRules.Default.capacity, "Le prototype du GDD : quatre places.");
        Assert.AreEqual(DuelOrganization.KingOfTheHill, MatchRules.Default.organization, "Le format par défaut du GDD.");
        Assert.IsTrue(MatchRules.Default.spectatorInterventions);

        var tooSmall = new MatchRules { capacity = 1 };
        Assert.AreEqual(MatchRules.MinCapacity, tooSmall.Sanitized().capacity);
        var tooBig = new MatchRules { capacity = 40 };
        Assert.AreEqual(MatchRules.MaxCapacity, tooBig.Sanitized().capacity);
        var unknown = new MatchRules { capacity = 4, organization = (DuelOrganization)200 };
        Assert.AreEqual(DuelOrganization.KingOfTheHill, unknown.Sanitized().organization, "Une valeur inconnue (ancienne version) retombe sur le défaut.");
    }

    /// <summary>Vécu le 2026-10-12 : l'hôte qui quittait de lui-même voyait en rouge la raison
    /// technique de son propre arrêt, et le client un message anglais de Netcode.</summary>
    [Test]
    public void LeRetourAuMenu_DitPourquoi_EnFrancais_SansJargon()
    {
        const string Transport = "[Disconnect Event][Client-0][TransportClientId-0][TransportShutdown] NetworkConnectionManager was shutdown. The transport was shutdown.";
        Assert.AreEqual("", SessionFlow.MenuMessageFor(Transport, true), "L'hôte qui s'arrête n'a rien à apprendre.");
        StringAssert.Contains("perdue", SessionFlow.MenuMessageFor(Transport, false), "Le client a perdu la connexion.");
        Assert.AreEqual("L'hôte a quitté la partie.", SessionFlow.MenuMessageFor("Disconnected due to host shutting down.", false));
        StringAssert.Contains("perdue", SessionFlow.MenuMessageFor("", false));
        StringAssert.Contains("perdue", SessionFlow.MenuMessageFor(null, false));

        SessionFlow.ShouldApprove(4, 4, "0.1.0", "0.1.0", out string refusal);
        Assert.AreEqual(refusal, SessionFlow.MenuMessageFor(refusal, false), "Un refus de l'hôte, déjà en français, passe tel quel.");
    }

    [Test]
    public void UneAutreVersion_EstRefusee_AvecLesDeuxVersionsDansLaRaison()
    {
        Assert.IsFalse(SessionFlow.ShouldApprove(1, 4, "0.1.0", "0.2.0", out string reason));
        StringAssert.Contains("0.1.0", reason);
        StringAssert.Contains("0.2.0", reason);
    }
}
