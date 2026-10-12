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
/// marquer deux joueurs à chaque mort, et rien ne refusait une build d'une autre version.
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

    [Test]
    public void LaPartieAccueilleDeuxJoueurs_PasUnTroisieme()
    {
        Assert.IsTrue(SessionFlow.ShouldApprove(0, "0.1.0", "0.1.0", out _), "L'hôte lui-même.");
        Assert.IsTrue(SessionFlow.ShouldApprove(1, "0.1.0", "0.1.0", out _), "Son adversaire.");
        Assert.IsFalse(SessionFlow.ShouldApprove(2, "0.1.0", "0.1.0", out string reason), "Un troisième joueur.");
        StringAssert.Contains("pleine", reason);
    }

    [Test]
    public void UneAutreVersion_EstRefusee_AvecLesDeuxVersionsDansLaRaison()
    {
        Assert.IsFalse(SessionFlow.ShouldApprove(1, "0.1.0", "0.2.0", out string reason));
        StringAssert.Contains("0.1.0", reason);
        StringAssert.Contains("0.2.0", reason);
    }
}
