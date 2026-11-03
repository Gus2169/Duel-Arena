using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Le tableau de la soirée (GDD § 14, 2026-11-03) : au salon, qui a gagné quoi depuis l'ouverture de la partie. Le
/// serveur retient chaque match fini (Evening.Match) ; chaque machine en tire une ligne par joueur et le dernier match.
/// Fonctions pures.
/// </summary>
public class EveningTests
{
    private static Evening.Match M(GameModeId mode, string[] winners, string[] losers, int winnerScore = 0, int loserScore = 0,
                                   bool forfeit = false)
        => Evening.Record(mode, winners, losers, winnerScore, loserScore, forfeit);

    /// <summary>Qui a gagné quoi : chaque joueur, ses victoires par mode et ses matchs joués, rangés par victoires puis par
    /// bilan (le moins de matchs à victoires égales) ; un forfait compte, au Tag Team chaque membre de l'équipe gagne, et un
    /// tournoi compte pour un match, gagné par son champion contre tous les autres. Vu échouer, les matchs perdus
    /// oubliés.</summary>
    [Test]
    public void LaSoiree_ChaqueJoueurSesVictoiresParModeEtSesMatchs()
    {
        var matches = new List<Evening.Match>
        {
            M(GameModeId.Classic, new[] { "Faucon" }, new[] { "Rivet" }, 2, 1),
            M(GameModeId.GunGame, new[] { "Rivet" }, new[] { "Faucon" }),
            M(GameModeId.Classic, new[] { "Faucon" }, new[] { "Rivet" }, 1, 0, forfeit: true),
            M(GameModeId.TagTeam, new[] { "Faucon", "Ombre" }, new[] { "Rivet", "Pic" }, 100, 25),
            M(GameModeId.Tournament, new[] { "Ombre" }, new[] { "Faucon", "Rivet", "Pic" }),
        };
        List<Evening.Row> rows = Evening.Tally(matches);
        CollectionAssert.AreEqual(new[] { "Faucon", "Ombre", "Rivet", "Pic" }, rows.ConvertAll(r => r.name),
            "Par victoires ; à victoires égales, le meilleur bilan.");
        Assert.AreEqual(3, rows[0].wins);
        Assert.AreEqual(5, rows[0].played);
        Assert.AreEqual(2, rows[0].WinsIn(GameModeId.Classic), "Le forfait compte.");
        Assert.AreEqual(1, rows[0].WinsIn(GameModeId.TagTeam), "Chaque membre de l'équipe gagnante.");
        Assert.AreEqual(2, rows[1].wins);
        Assert.AreEqual(2, rows[1].played);
        Assert.AreEqual(1, rows[1].WinsIn(GameModeId.Tournament), "Le champion gagne le tournoi, un match.");
        Assert.AreEqual(1, rows[2].wins);
        Assert.AreEqual(5, rows[2].played);
        Assert.AreEqual(0, rows[3].wins);
        Assert.AreEqual(2, rows[3].played, "Un vaincu du tournoi y a joué un match.");
        Assert.AreEqual("Classique 2 · Tag Team 1 · sur 5 matchs", Evening.Breakdown(rows[0]), "Dans l'ordre où il les a gagnées.");
        Assert.AreEqual("sur 2 matchs", Evening.Breakdown(rows[3]));

        var tie = Evening.Tally(new List<Evening.Match>
        {
            M(GameModeId.Classic, new[] { "Kilo" }, new[] { "Lima" }, 2, 0),
            M(GameModeId.Classic, new[] { "Lima" }, new[] { "Mike" }, 2, 1),
            M(GameModeId.Classic, new[] { "Mike" }, new[] { "Kilo" }, 2, 1),
            M(GameModeId.Classic, new[] { "Lima" }, new[] { "Kilo" }, 2, 1),
        });
        CollectionAssert.AreEqual(new[] { "Lima", "Mike", "Kilo" }, tie.ConvertAll(r => r.name),
            "Lima, 2 victoires ; puis Mike et Kilo, une chacun : Mike en deux matchs, Kilo en trois.");

        Assert.AreEqual("LA SOIRÉE · 5 MATCHS", Evening.Title(5));
        Assert.AreEqual("LA SOIRÉE · 1 MATCH", Evening.Title(1));
        Assert.AreEqual("0 VICTOIRE", Evening.WinsLabel(0));
        Assert.AreEqual("1 VICTOIRE", Evening.WinsLabel(1));
        Assert.AreEqual("3 VICTOIRES", Evening.WinsLabel(3));
        Assert.IsEmpty(Evening.Tally(new List<Evening.Match>()), "Aucun match : aucune ligne.");
    }

    /// <summary>Le dernier match en une ligne : le score des manches, des éliminations ou des points (à la française), le
    /// forfait, une équipe, un champion, et rien quand il n'a pas de score (le Gun Game). Vu échouer, le pluriel d'une
    /// équipe oublié.</summary>
    [Test]
    public void LeDernierMatch_EnUneLigne()
    {
        Assert.AreEqual("Classique : Faucon bat Rivet, 2 – 1",
            Evening.MatchLine(M(GameModeId.Classic, new[] { "Faucon" }, new[] { "Rivet" }, 2, 1)));
        Assert.AreEqual("Classique : Faucon bat Rivet par forfait",
            Evening.MatchLine(M(GameModeId.Classic, new[] { "Faucon" }, new[] { "Rivet" }, 1, 0, forfeit: true)));
        Assert.AreEqual("Gun Game : Rivet bat Faucon",
            Evening.MatchLine(M(GameModeId.GunGame, new[] { "Rivet" }, new[] { "Faucon" })));
        Assert.AreEqual("Tag Team : Faucon et Ombre battent Rivet et Pic, 100 – 25",
            Evening.MatchLine(M(GameModeId.TagTeam, new[] { "Faucon", "Ombre" }, new[] { "Rivet", "Pic" }, 100, 25)));
        Assert.AreEqual("Tournoi : Ombre champion",
            Evening.MatchLine(M(GameModeId.Tournament, new[] { "Ombre" }, new[] { "Faucon", "Rivet", "Pic" })));
        Assert.AreEqual($"Zone : Faucon bat Rivet, {ZoneMode.Points(3000)} – {ZoneMode.Points(1510)}",
            Evening.MatchLine(M(GameModeId.Zone, new[] { "Faucon" }, new[] { "Rivet" }, 3000, 1510)));
    }

    /// <summary>Les pseudos d'un camp voyagent bout à bout, accents compris ; ceux qui n'y tiendraient plus restent dehors
    /// sans qu'on en coupe un.</summary>
    [Test]
    public void LesPseudos_VoyagentBoutABout_SansEnCouperUn()
    {
        var names = new[] { "Éloïse", "Faucon", "Rivet" };
        CollectionAssert.AreEqual(names, Evening.Split(Evening.Join(names)));
        var many = new List<string>();
        for (int i = 0; i < 12; i++) many.Add($"Joueur-Numéro{i:00}");
        List<string> back = Evening.Split(Evening.Join(many));
        Assert.Less(back.Count, many.Count, "Tous ne tiennent pas.");
        Assert.Greater(back.Count, 3);
        CollectionAssert.AreEqual(many.GetRange(0, back.Count), back, "Ceux qui tiennent sont entiers, dans l'ordre.");
    }

    /// <summary>Le salon montre toute la soirée (six lignes au plus) jusqu'à quatre joueurs, ses quatre premières lignes
    /// au-delà : la liste des joueurs prend alors la place.</summary>
    [Test]
    public void LeSalon_GardeLaPlaceDesJoueurs()
    {
        Assert.AreEqual(5, SalonView.EveningRowsShown(3, 5));
        Assert.AreEqual(6, SalonView.EveningRowsShown(4, 8));
        Assert.AreEqual(4, SalonView.EveningRowsShown(6, 6));
        Assert.AreEqual(0, SalonView.EveningRowsShown(2, 0));
    }
}
