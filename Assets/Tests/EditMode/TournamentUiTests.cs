using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Ce que le Tournoi montre (2026-11-03, GDD § 11) : le salon (ses réglages, sa phrase), les règles de la partie, les
/// textes du tableau de la salle d'attente et de ses annonces (l'étape, ce qui attend le joueur, les matchs, le
/// classement), l'écran du champion, et le cadrage des caméras du mur d'écrans. Fonctions pures.
///
/// Trois joueurs (Faucon 1, Rivet 2, Ombre 3), trois tours d'un match, un joueur au repos à chacun :
/// tour 1, Faucon contre Rivet (Ombre se repose) ; tour 2, Rivet contre Ombre ; tour 3, Ombre contre Faucon.
/// </summary>
public class TournamentUiTests
{
    private const ulong Faucon = 1, Rivet = 2, Ombre = 3, Visiteur = 9;
    private static readonly List<ulong> Players = new List<ulong> { Faucon, Rivet, Ombre };

    private static string Name(ulong id) => id switch { Faucon => "Faucon", Rivet => "Rivet", Ombre => "Ombre", _ => "Joueur" };

    private static Tournament.Match M(ulong a, ulong b, int tour, Tournament.Status status = Tournament.Status.Pending,
                                      ulong winner = Tournament.Nobody, int roundsA = 0, int roundsB = 0, bool forfeit = false, int arena = 0)
        => new Tournament.Match
        {
            a = a, b = b, tour = (byte)tour, status = status, winner = winner, roundsA = (byte)roundsA, roundsB = (byte)roundsB,
            forfeit = forfeit, arena = (byte)arena,
        };

    private static Tournament.State S(Tournament.Stage stage, int tour) => new Tournament.State { stage = stage, tour = (byte)tour, tourCount = 3 };

    /// <summary>Le calendrier, aucun match joué.</summary>
    private static List<Tournament.Match> Pool() => new List<Tournament.Match>
    {
        M(Faucon, Rivet, 0), M(Rivet, Ombre, 1), M(Ombre, Faucon, 2),
    };

    /// <summary>La poule finie : Faucon gagne ses deux matchs, Rivet un, Ombre aucun.</summary>
    private static List<Tournament.Match> PoolDone() => new List<Tournament.Match>
    {
        M(Faucon, Rivet, 0, Tournament.Status.Done, Faucon, 2, 0),
        M(Rivet, Ombre, 1, Tournament.Status.Done, Rivet, 2, 1),
        M(Ombre, Faucon, 2, Tournament.Status.Done, Faucon, 1, 2),
    };

    // ------------------------------------------------------------------ Le salon et les règles

    /// <summary>Le salon du Tournoi : son but et ses matchs, fixés, puis l'arme ; sous « Plus de réglages », les places
    /// seules (ni tirage des duellistes, ni tribune), jamais moins de trois. Vu échouer, la tribune rendue au Tournoi.</summary>
    [Test]
    public void LeSalonDuTournoi_DitSonButEtSesMatchs_EtNaNiTirageNiTribune()
    {
        MatchRules rules = GameModeCatalog.RulesFor(GameModeId.Tournament);
        List<SalonView.Row> essentials = SalonView.EssentialRows(rules);
        CollectionAssert.AreEqual(new[] { "Le but", "Les matchs", "L'arme" }, essentials.ConvertAll(r => r.label));
        Assert.IsTrue(essentials[0].isFixed && essentials[1].isFixed, "Le but et les matchs ne se changent pas.");
        StringAssert.Contains("finale", essentials[0].hint);
        StringAssert.Contains("BO3", essentials[1].hint);
        StringAssert.Contains("BO5", essentials[1].hint);

        List<SalonView.Row> advanced = SalonView.AdvancedRows(rules, playerCount: 2);
        CollectionAssert.AreEqual(new[] { "Tête seulement", "Places" }, advanced.ConvertAll(r => r.label), "Tout le monde se bat : ni tirage, ni tribune.");
        foreach (SalonView.Choice place in advanced[1].choices)
            Assert.AreEqual(int.Parse(place.label) < Tournament.MinPlayers, place.unavailable, $"{place.label} places : trois au moins.");

        Assert.AreEqual("DANS LE TOURNOI · HÔTE · TOI", SalonView.TournamentRole(true, true));
        Assert.AreEqual("DANS LE TOURNOI", SalonView.TournamentRole(false, false));
        Assert.AreEqual("Il faut trois joueurs au moins : chacun affronte tous les autres.", SalonView.TournamentPhrase(2));
        Assert.AreEqual("3 joueurs : 3 matchs de poule en 3 tours, un joueur au repos à chaque tour, puis la finale entre les deux premiers.",
                        SalonView.TournamentPhrase(3));
        Assert.AreEqual("4 joueurs : 6 matchs de poule en 3 tours, 2 à la fois, puis la finale entre les deux premiers.",
                        SalonView.TournamentPhrase(4));
        Assert.AreEqual("6 joueurs : 15 matchs de poule en 5 tours, 3 à la fois, puis la finale entre les deux premiers.",
                        SalonView.TournamentPhrase(6));
    }

    /// <summary>Les règles du Tournoi : trois places au moins, la poule en BO3 (les manches ne se changent pas), six places
    /// d'emblée, la préparation de l'arme, sans réapparitions ; la partie en une ligne. Vu échouer, le plancher de trois
    /// places retiré.</summary>
    [Test]
    public void LesReglesDuTournoi_TroisPlacesAuMoins_LaPouleEnBO3()
    {
        MatchRules r = new MatchRules { mode = GameModeId.Tournament, capacity = 2, roundsToWin = 3 }.Sanitized();
        Assert.AreEqual(GameModeId.Tournament, r.mode, "Le Tournoi se joue.");
        Assert.AreEqual(Tournament.MinPlayers, r.capacity, "Trois places au moins.");
        Assert.AreEqual(Tournament.PoolRoundsToWin, r.roundsToWin, "La poule en BO3 : ses manches ne se choisissent pas.");
        Assert.AreEqual(Tournament.PoolRoundsToWin, r.WinTarget);
        Assert.AreEqual("poule en BO3, finale en BO5", r.Format);
        Assert.IsTrue(OnlineSession.ParseRules(OnlineSession.FormatRules(r)).IsTournament, "Le mode voyage dans les réglages du salon.");

        MatchRules fresh = GameModeCatalog.RulesFor(GameModeId.Tournament);
        Assert.AreEqual(Tournament.MaxPlayers, fresh.capacity, "Six places d'emblée.");
        Assert.AreEqual("Tournoi · poule en BO3, finale en BO5 · 6 joueurs au plus · arme libre", GameModeCatalog.Summary(GameModeId.Tournament, fresh));
        Assert.IsTrue(RoundManager.HasPreparation(GameModeId.Tournament), "Chacun choisit son arme avant chaque manche.");
        Assert.IsFalse(RoundManager.HasRespawns(fresh), "Des manches du classique : pas de réapparitions.");
        Assert.AreEqual(-1, RoundManager.ImposedWeaponSlot(fresh, 0), "Aucune arme imposée.");
    }

    // ------------------------------------------------------------------ Le tableau de la salle d'attente

    /// <summary>L'étape, en tête du tableau. Vu échouer, l'entracte annonçant le tour d'après de travers.</summary>
    [Test]
    public void LEtape_DitLeTirage_LeTour_LEntracte_EtLaFinale()
    {
        Assert.AreEqual("LE TIRAGE · LE TOUR 1 DANS 9 S", TournamentBoard.StageLine(S(Tournament.Stage.Draw, 0), 9));
        Assert.AreEqual("LE TIRAGE · LE TOUR 1 DANS 0 S", TournamentBoard.StageLine(S(Tournament.Stage.Draw, 0), -2), "Jamais négatif.");
        Assert.AreEqual("TOUR 2 SUR 3", TournamentBoard.StageLine(S(Tournament.Stage.Tour, 1), 0));
        Assert.AreEqual("ENTRACTE · LE TOUR 2 DANS 8 S", TournamentBoard.StageLine(S(Tournament.Stage.Break, 0), 8));
        Assert.AreEqual("ENTRACTE · LA FINALE DANS 8 S", TournamentBoard.StageLine(S(Tournament.Stage.Break, 2), 8), "Après le dernier tour.");
        Assert.AreEqual("LA FINALE", TournamentBoard.StageLine(S(Tournament.Stage.Final, 2), 0));
        Assert.AreEqual("LE TOURNOI EST FINI", TournamentBoard.StageLine(S(Tournament.Stage.Done, 2), 0));
    }

    /// <summary>Ce qui attend le joueur : son adversaire au tour qui vient, son repos, son match fini (ou gagné par forfait),
    /// la finale qu'il joue ou qu'il regarde (les deux premiers encore là) ; un visiteur arrivé après le tirage jouera le
    /// suivant. Vu échouer, le repos du tour qui vient pris pour un match.</summary>
    [Test]
    public void LaLigneDuJoueur_DitSonProchainAdversaire_SonRepos_EtLaFinale()
    {
        List<Tournament.Match> pool = Pool();
        Assert.AreEqual("TU REGARDES : TU JOUERAS LE PROCHAIN TOURNOI",
                        TournamentBoard.MineLine(S(Tournament.Stage.Draw, 0), Visiteur, Players, pool, null, Name));
        Assert.AreEqual("TU AFFRONTES RIVET AU TOUR 1", TournamentBoard.MineLine(S(Tournament.Stage.Draw, 0), Faucon, Players, pool, null, Name));
        Assert.AreEqual("TU TE REPOSES AU TOUR 1 · PUIS RIVET", TournamentBoard.MineLine(S(Tournament.Stage.Draw, 0), Ombre, Players, pool, null, Name));
        Assert.AreEqual("TU TE REPOSES CE TOUR · PUIS RIVET", TournamentBoard.MineLine(S(Tournament.Stage.Tour, 0), Ombre, Players, pool, null, Name));

        List<Tournament.Match> afterFirst = Pool();
        afterFirst[0] = M(Faucon, Rivet, 0, Tournament.Status.Done, Faucon, 2, 1);
        Assert.AreEqual("TON MATCH EST FINI · PUIS OMBRE", TournamentBoard.MineLine(S(Tournament.Stage.Tour, 0), Faucon, Players, afterFirst, null, Name));
        Assert.AreEqual("TU AFFRONTES OMBRE AU TOUR 2", TournamentBoard.MineLine(S(Tournament.Stage.Break, 0), Rivet, Players, afterFirst, null, Name));
        Assert.AreEqual("TU TE REPOSES AU TOUR 2 · PUIS OMBRE", TournamentBoard.MineLine(S(Tournament.Stage.Break, 0), Faucon, Players, afterFirst, null, Name));

        List<Tournament.Match> forfeit = Pool();
        forfeit[0] = M(Faucon, Rivet, 0, Tournament.Status.Done, Faucon, 2, 0, forfeit: true);
        Assert.AreEqual("TON ADVERSAIRE EST PARTI : MATCH GAGNÉ · PUIS OMBRE",
                        TournamentBoard.MineLine(S(Tournament.Stage.Tour, 0), Faucon, Players, forfeit, null, Name));

        List<Tournament.Match> done = PoolDone();
        Assert.AreEqual("TU JOUES LA FINALE", TournamentBoard.MineLine(S(Tournament.Stage.Break, 2), Rivet, Players, done, null, Name));
        Assert.AreEqual("TU REGARDES LA FINALE", TournamentBoard.MineLine(S(Tournament.Stage.Break, 2), Ombre, Players, done, null, Name));
        Assert.AreEqual("TU JOUES LA FINALE", TournamentBoard.MineLine(S(Tournament.Stage.Break, 2), Ombre, Players, done, new HashSet<ulong> { Rivet }, Name),
                        "Rivet est parti : le troisième joue la finale.");

        done.Add(M(Faucon, Rivet, Tournament.FinalTour, Tournament.Status.Playing));
        Assert.AreEqual("TU JOUES LA FINALE", TournamentBoard.MineLine(S(Tournament.Stage.Final, 2), Faucon, Players, done, null, Name));
        Assert.AreEqual("TU REGARDES LA FINALE", TournamentBoard.MineLine(S(Tournament.Stage.Final, 2), Ombre, Players, done, null, Name));
    }

    /// <summary>L'annonce de chaque étape, au centre de l'écran de qui est dans la salle d'attente. Vu échouer, le forfait
    /// annoncé comme un match qui commence.</summary>
    [Test]
    public void LesAnnonces_DisentChaqueEtape_EtCeQuiAttendLeJoueur()
    {
        List<Tournament.Match> pool = Pool();
        Assert.IsTrue(TournamentBoard.TryGetAnnouncement(S(Tournament.Stage.Draw, 0), Faucon, Players, pool, null, 12, Name, out string big, out string small));
        Assert.AreEqual("Le tournoi commence", big);
        Assert.AreEqual("3 joueurs · 3 matchs de poule en 3 tours, puis la finale", small);

        TournamentBoard.TryGetAnnouncement(S(Tournament.Stage.Tour, 1), Faucon, Players, pool, null, 0, Name, out big, out small);
        Assert.AreEqual("Tour 2 sur 3", big);
        Assert.AreEqual("Tu te reposes : les matchs passent sur les écrans.", small);
        TournamentBoard.TryGetAnnouncement(S(Tournament.Stage.Tour, 1), Rivet, Players, pool, null, 0, Name, out _, out small);
        Assert.AreEqual("Ton match commence.", small);
        TournamentBoard.TryGetAnnouncement(S(Tournament.Stage.Tour, 1), Visiteur, Players, pool, null, 0, Name, out _, out small);
        Assert.AreEqual("Les matchs passent sur les écrans.", small);
        List<Tournament.Match> forfeit = Pool();
        forfeit[1] = M(Rivet, Ombre, 1, Tournament.Status.Done, Ombre, 0, 2, forfeit: true);
        TournamentBoard.TryGetAnnouncement(S(Tournament.Stage.Tour, 1), Ombre, Players, forfeit, null, 0, Name, out _, out small);
        Assert.AreEqual("Ton adversaire est parti : tu gagnes ce match par forfait.", small);

        List<Tournament.Match> afterFirst = Pool();
        afterFirst[0] = M(Faucon, Rivet, 0, Tournament.Status.Done, Faucon, 2, 1);
        TournamentBoard.TryGetAnnouncement(S(Tournament.Stage.Break, 0), Rivet, Players, afterFirst, null, 8, Name, out big, out small);
        Assert.AreEqual("Tour 1 fini", big);
        Assert.AreEqual("Le tour 2 dans 8 s : tu affrontes Ombre", small);
        TournamentBoard.TryGetAnnouncement(S(Tournament.Stage.Break, 0), Faucon, Players, afterFirst, null, 8, Name, out _, out small);
        Assert.AreEqual("Le tour 2 dans 8 s : tu te reposes", small);
        TournamentBoard.TryGetAnnouncement(S(Tournament.Stage.Break, 0), Visiteur, Players, afterFirst, null, 8, Name, out _, out small);
        Assert.AreEqual("Le tour 2 dans 8 s", small);

        List<Tournament.Match> done = PoolDone();
        TournamentBoard.TryGetAnnouncement(S(Tournament.Stage.Break, 2), Ombre, Players, done, null, 8, Name, out big, out small);
        Assert.AreEqual("Tour 3 fini", big);
        Assert.AreEqual("La finale dans 8 s : Faucon contre Rivet", small);
        done.Add(M(Faucon, Rivet, Tournament.FinalTour, Tournament.Status.Playing));
        TournamentBoard.TryGetAnnouncement(S(Tournament.Stage.Final, 2), Ombre, Players, done, null, 0, Name, out big, out small);
        Assert.AreEqual("La finale", big);
        Assert.AreEqual("Faucon contre Rivet, en BO5", small);
        Assert.IsFalse(TournamentBoard.TryGetAnnouncement(S(Tournament.Stage.Done, 2), Ombre, Players, done, null, 0, Name, out _, out _),
                       "La fin : l'écran du champion, pas d'annonce.");
    }

    /// <summary>Les matchs du tableau : ceux du premier tour au tirage, du tour en cours, du tour suivant à l'entracte, la
    /// finale annoncée (les deux premiers) puis jouée. Vu échouer, l'entracte montrant le tour fini.</summary>
    [Test]
    public void LesMatchsDuTableau_SontCeuxDuTourQuiSeJoueOuQuiVient()
    {
        var result = new List<Tournament.Match>();
        var indices = new List<int>();
        List<Tournament.Match> pool = Pool();
        List<Tournament.Standing> table = Tournament.Standings(Players, pool, null);

        TournamentBoard.BoardMatches(S(Tournament.Stage.Draw, 0), pool, table, result, indices);
        CollectionAssert.AreEqual(new[] { 0 }, indices);
        Assert.AreEqual("TOUR 1 · À VENIR", TournamentBoard.MatchesHeading(S(Tournament.Stage.Draw, 0)));
        TournamentBoard.BoardMatches(S(Tournament.Stage.Tour, 1), pool, table, result, indices);
        CollectionAssert.AreEqual(new[] { 1 }, indices);
        Assert.AreEqual("LES MATCHS DU TOUR", TournamentBoard.MatchesHeading(S(Tournament.Stage.Tour, 1)));
        TournamentBoard.BoardMatches(S(Tournament.Stage.Break, 0), pool, table, result, indices);
        CollectionAssert.AreEqual(new[] { 1 }, indices, "À l'entracte, le tour qui vient.");
        Assert.AreEqual("TOUR 2 · À VENIR", TournamentBoard.MatchesHeading(S(Tournament.Stage.Break, 0)));

        List<Tournament.Match> done = PoolDone();
        TournamentBoard.BoardMatches(S(Tournament.Stage.Break, 2), done, Tournament.Standings(Players, done, null), result, indices);
        Assert.AreEqual(1, result.Count, "La finale annoncée.");
        Assert.AreEqual(-1, indices[0], "Pas encore au calendrier.");
        Assert.IsTrue(result[0].Involves(Faucon) && result[0].Involves(Rivet), "Entre les deux premiers.");
        Assert.AreEqual("LA FINALE · À VENIR", TournamentBoard.MatchesHeading(S(Tournament.Stage.Break, 2)));

        done.Add(M(Faucon, Rivet, Tournament.FinalTour, Tournament.Status.Playing));
        TournamentBoard.BoardMatches(S(Tournament.Stage.Final, 2), done, table, result, indices);
        CollectionAssert.AreEqual(new[] { 3 }, indices);
        Assert.AreEqual("LA FINALE", TournamentBoard.MatchesHeading(S(Tournament.Stage.Final, 2)));
        TournamentBoard.BoardMatches(S(Tournament.Stage.Done, 2), done, table, result, indices);
        Assert.IsEmpty(result);
        Assert.IsNull(TournamentBoard.MatchesHeading(S(Tournament.Stage.Done, 2)));
    }

    /// <summary>Un match en une ligne (ses manches, son arène), un joueur au classement (son bilan, ses manches), et la
    /// ligne d'un match en cours sous la barre de score. Vu échouer, l'arène comptée à partir de zéro.</summary>
    [Test]
    public void LesLignesDuTableau_ScoreAreneBilanEtManches()
    {
        Assert.AreEqual("–", TournamentBoard.MatchScore(M(Faucon, Rivet, 0), 0, 0));
        Assert.AreEqual("1 – 0", TournamentBoard.MatchScore(M(Faucon, Rivet, 0, Tournament.Status.Playing), 1, 0), "En cours : les manches du couloir.");
        Assert.AreEqual("2 – 1", TournamentBoard.MatchScore(M(Faucon, Rivet, 0, Tournament.Status.Done, Faucon, 2, 1), 0, 0));
        Assert.AreEqual("ARÈNE 2", TournamentBoard.MatchWhere(M(Faucon, Rivet, 0, Tournament.Status.Playing, arena: 1)));
        Assert.AreEqual("FINI", TournamentBoard.MatchWhere(M(Faucon, Rivet, 0, Tournament.Status.Done, Faucon, 2, 1)));
        Assert.AreEqual("FORFAIT", TournamentBoard.MatchWhere(M(Faucon, Rivet, 0, Tournament.Status.Done, Faucon, 2, 0, forfeit: true)));
        Assert.AreEqual("", TournamentBoard.MatchWhere(M(Faucon, Rivet, 0)));

        Assert.AreEqual("2 V · 1 D", TournamentBoard.Record(new Tournament.Standing { wins = 2, losses = 1 }));
        Assert.AreEqual("PARTI", TournamentBoard.Record(new Tournament.Standing { wins = 2, losses = 1, left = true }));
        Assert.AreEqual("+3", TournamentBoard.RoundDifference(3));
        Assert.AreEqual("0", TournamentBoard.RoundDifference(0));
        Assert.AreEqual("−2", TournamentBoard.RoundDifference(-2));

        var final = new RoundManager.LaneInfo { final = true, roundsToWin = (byte)Tournament.FinalRoundsToWin };
        var pool = new RoundManager.LaneInfo { roundsToWin = (byte)Tournament.PoolRoundsToWin };
        Assert.AreEqual("FINALE · BO5", TournamentBoard.LaneLine(final, null));
        Assert.AreEqual("TOURNOI · BO3", TournamentBoard.LaneLine(pool, null), "Sans le calendrier, le format seul.");
        Assert.AreEqual("Finale · manche 3", TournamentBoard.LaneRoundLine(final, null, 3));
        Assert.AreEqual("Manche 1", TournamentBoard.LaneRoundLine(pool, null, 1));
    }

    /// <summary>L'écran du champion : sa finale en une ligne (ou le forfait), et le tableau, le champion puis son
    /// adversaire de la finale en tête, les autres dans l'ordre de la poule. Vu échouer, le finaliste laissé à sa place.</summary>
    [Test]
    public void LEcranDuChampion_DitSaFinale_EtLeMetEnTete()
    {
        List<Tournament.Match> done = PoolDone();
        done.Add(M(Faucon, Rivet, Tournament.FinalTour, Tournament.Status.Done, Rivet, 1, 3));
        Assert.AreEqual("Finale gagnée 3 – 1 contre Faucon", TournamentBoard.ChampionLine(Rivet, done, false, Name));

        List<Tournament.Match> left = PoolDone();
        left.Add(M(Faucon, Rivet, Tournament.FinalTour, Tournament.Status.Done, Faucon, 3, 0, forfeit: true));
        Assert.AreEqual("Son adversaire de la finale, Rivet, a quitté la partie", TournamentBoard.ChampionLine(Faucon, left, true, Name));
        Assert.AreEqual("Les autres joueurs ont quitté le tournoi", TournamentBoard.ChampionLine(Ombre, Pool(), true, Name));

        List<Tournament.Standing> table = Tournament.Standings(new List<ulong> { Ombre, Faucon, Rivet }, PoolDone(), null);
        Assert.AreEqual(Faucon, table[0].id, "La poule : Faucon premier.");
        TournamentBoard.ChampionOrder(Rivet, done, table);
        CollectionAssert.AreEqual(new[] { Rivet, Faucon, Ombre }, table.ConvertAll(s => s.id), "Le champion, son adversaire de la finale, puis les autres.");

        // Faucon, premier de la poule, est parti avant la finale : Rivet et Ombre l'ont jouée ; il passe derrière eux.
        var gone = new HashSet<ulong> { Faucon };
        List<Tournament.Match> withoutFaucon = PoolDone();
        withoutFaucon.Add(M(Rivet, Ombre, Tournament.FinalTour, Tournament.Status.Done, Ombre, 2, 3));
        table = Tournament.Standings(Players, withoutFaucon, gone);
        Assert.AreEqual(Faucon, table[0].id, "Parti, il reste au classement de la poule.");
        TournamentBoard.ChampionOrder(Ombre, withoutFaucon, table);
        CollectionAssert.AreEqual(new[] { Ombre, Rivet, Faucon }, table.ConvertAll(s => s.id), "Le champion, son adversaire de la finale, puis celui qui est parti.");
    }

    // ------------------------------------------------------------------ Le mur d'écrans

    private static readonly Vector2 Heights = new Vector2(4.2f, 6f);
    private static readonly Vector2 Distances = new Vector2(9f, 30f);
    private static readonly Rect Area = Rect.MinMaxRect(-31f, -3.5f, 31f, 66.5f);

    /// <summary>La caméra d'un duel se tient de côté, d'autant plus loin que les robots sont écartés, un peu au-dessus
    /// d'eux, du côté où il y a du recul (pas contre un mur), sans changer de côté quand ils tournent l'un autour de
    /// l'autre, et jamais hors de l'aire de jeu. Vu échouer, le côté pris au hasard du calcul.</summary>
    [Test]
    public void LaCameraDUnDuel_SeTientDeCote_LoinDesMurs_EtGardeSonCote()
    {
        var a = new Vector3(0f, 0f, 10f);
        var b = new Vector3(0f, 0f, 20f);
        Vector3 p = TournamentFeeds.FeedPosition(a, b, null, Heights, Distances, Area);
        Assert.AreEqual(15f, p.z, 1e-4f, "En face du milieu du duel.");
        Assert.AreEqual(13.5f, Mathf.Abs(p.x), 1e-4f, "De côté, à 0,75 × l'écart + 6 m.");
        Assert.AreEqual(5f, p.y, 1e-4f, "Un peu au-dessus des robots.");

        Vector3 kept = TournamentFeeds.FeedPosition(a, b, new Vector3(-10f, 5f, 15f), Heights, Distances, Area);
        Assert.AreEqual(-13.5f, kept.x, 1e-4f, "Elle reste du côté où elle était.");
        kept = TournamentFeeds.FeedPosition(a, b, new Vector3(10f, 5f, 15f), Heights, Distances, Area);
        Assert.AreEqual(13.5f, kept.x, 1e-4f);

        Vector3 wall = TournamentFeeds.FeedPosition(new Vector3(25f, 0f, 10f), new Vector3(25f, 0f, 20f), new Vector3(31f, 5f, 15f), Heights, Distances, Area);
        Assert.AreEqual(11.5f, wall.x, 1e-4f, "Contre un mur, elle passe de l'autre côté : là où il y a du recul.");

        Vector3 far = TournamentFeeds.FeedPosition(new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, 55f), null, Heights, Distances, Area);
        Assert.AreEqual(30f, Mathf.Abs(far.x), 1e-4f, "Écartés de 50 m : au plus loin (30 m).");
        Assert.AreEqual(6f, far.y, 1e-4f, "Pas plus haut que 6 m : sous le bas des faisceaux des projecteurs.");

        for (int i = 0; i < 50; i++)
        {
            var x = new Vector3(Random.Range(-31f, 31f), 0f, Random.Range(-3.5f, 66.5f));
            var y = new Vector3(Random.Range(-31f, 31f), 0f, Random.Range(-3.5f, 66.5f));
            Vector3 q = TournamentFeeds.FeedPosition(x, y, null, Heights, Distances, Area);
            // Bords compris (Rect.Contains les exclut à droite et en haut : la caméra s'y arrête, contre la limite).
            Assert.IsTrue(q.x >= Area.xMin && q.x <= Area.xMax && q.z >= Area.yMin && q.z <= Area.yMax, $"{q} : dans l'aire de jeu.");
            Assert.IsTrue(q.y >= Heights.x - 1e-4f && q.y <= Heights.y + 1e-4f, $"{q} : à sa hauteur.");
        }
    }

    /// <summary>Le champ de vision montre les deux robots : plus large quand ils s'écartent, plus serré de plus loin, entre
    /// 22 et 70°. Vu échouer, le champ calculé sans l'écart.</summary>
    [Test]
    public void LeChampDeLaCamera_MontreLesDeuxRobots()
    {
        float aspect = 16f / 9f;
        float near = TournamentFeeds.FeedFieldOfView(10f, 13.5f, aspect);
        Assert.AreEqual(34.7f, near, 0.2f);
        Assert.Greater(TournamentFeeds.FeedFieldOfView(20f, 13.5f, aspect), near, "Plus écartés : plus large.");
        Assert.Less(TournamentFeeds.FeedFieldOfView(10f, 25f, aspect), near, "De plus loin : plus serré.");
        Assert.AreEqual(22f, TournamentFeeds.FeedFieldOfView(0f, 100f, aspect), 1e-4f);
        Assert.AreEqual(70f, TournamentFeeds.FeedFieldOfView(200f, 1f, aspect), 1e-4f);
    }
}
