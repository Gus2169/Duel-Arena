using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Le Tournoi (2026-11-03, GDD § 11, TO1 à TO9) : la poule, chacun contre chacun, tirée au sort ; à nombre impair, un
/// joueur se repose à chaque tour ; les matchs d'un tour dans des arènes distinctes ; le classement (les victoires, puis
/// le match entre deux joueurs à égalité, puis les manches) ; les finalistes ; le forfait de celui qui part.
/// </summary>
public class TournamentTests
{
    private static List<ulong> Players(int count)
    {
        var players = new List<ulong>();
        for (int i = 0; i < count; i++) players.Add((ulong)(10 + i));
        return players;
    }

    [Test]
    public void LaPoule_FaitSeRencontrerChacunUneFois_ParTours_EtUnSeReposeANombreImpair()
    {
        for (int count = Tournament.MinPlayers; count <= Tournament.MaxPlayers; count++)
        {
            List<ulong> players = Players(count);
            List<Tournament.Match> schedule = Tournament.Schedule(players, new System.Random(count));
            Assert.AreEqual(count * (count - 1) / 2, schedule.Count, $"À {count}, chacun contre chacun : tous les matchs, pas un de plus.");

            var pairs = new HashSet<(ulong, ulong)>();
            foreach (Tournament.Match m in schedule)
            {
                Assert.AreNotEqual(m.a, m.b, "Personne ne joue contre lui-même.");
                Assert.IsTrue(pairs.Add(m.a < m.b ? (m.a, m.b) : (m.b, m.a)), $"À {count}, deux joueurs se rencontrent une seule fois.");
                Assert.AreEqual(Tournament.Status.Pending, m.status);
                Assert.AreEqual(Tournament.Nobody, m.winner);
            }

            int tours = Tournament.TourCount(count);
            Assert.AreEqual(count % 2 == 0 ? count - 1 : count, tours);
            var rests = new Dictionary<ulong, int>();
            foreach (ulong id in players) rests[id] = 0;
            var resting = new List<ulong>();
            int lastTour = -1;
            for (int tour = 0; tour < tours; tour++)
            {
                var seen = new HashSet<ulong>();
                int matches = 0;
                foreach (Tournament.Match m in schedule)
                {
                    if (m.tour != tour) continue;
                    matches++;
                    Assert.IsTrue(seen.Add(m.a) && seen.Add(m.b), $"À {count}, un joueur joue un seul match par tour.");
                }
                Assert.AreEqual(Tournament.MatchesPerTour(count), matches, $"À {count}, autant de matchs à chaque tour (tour {tour}).");
                Tournament.Resting(players, schedule, tour, null, resting);
                Assert.AreEqual(count % 2, resting.Count, $"À {count}, un joueur se repose à chaque tour à nombre impair, aucun sinon.");
                foreach (ulong id in resting) rests[id]++;
            }
            foreach (Tournament.Match m in schedule)
            {
                Assert.GreaterOrEqual(m.tour, lastTour, "Le calendrier va du premier tour au dernier.");
                lastTour = m.tour;
            }
            if (count % 2 == 1)
            {
                foreach (ulong id in players) Assert.AreEqual(1, rests[id], $"À {count}, chacun se repose une fois.");
            }
        }
    }

    [Test]
    public void LeTirage_ChangeAvecLeHasard_EtSeRejoueALIdentique()
    {
        List<ulong> players = Players(6);
        string Order(int seed)
        {
            var text = new System.Text.StringBuilder();
            foreach (Tournament.Match m in Tournament.Schedule(players, new System.Random(seed))) text.Append($"{m.tour}:{m.a}-{m.b} ");
            return text.ToString();
        }
        Assert.AreEqual(Order(7), Order(7), "Le même hasard donne le même tirage.");
        var different = new HashSet<string>();
        for (int seed = 0; seed < 8; seed++) different.Add(Order(seed));
        Assert.Greater(different.Count, 4, "L'ordre des matchs se tire au sort (TO8) : il change d'un tirage à l'autre.");
    }

    [Test]
    public void LesMatchsDUnTour_OntChacunLeurArene_QuiChangeDUnTourALAutre()
    {
        for (int tour = 0; tour < 6; tour++)
        {
            var arenas = new HashSet<int>();
            for (int index = 0; index < Tournament.MaxArenas; index++)
            {
                int arena = Tournament.ArenaFor(tour, index);
                Assert.That(arena, Is.InRange(0, Tournament.MaxArenas - 1));
                Assert.IsTrue(arenas.Add(arena), "Deux matchs d'un même tour ne partagent jamais une arène.");
            }
        }
        Assert.AreNotEqual(Tournament.ArenaFor(0, 0), Tournament.ArenaFor(1, 0),
                           "À trois joueurs (un match par tour), les copies de l'arène servent aussi.");
    }

    private static Tournament.Match Played(ulong a, ulong b, ulong winner, int roundsA, int roundsB, byte tour = 0)
    {
        var m = new Tournament.Match { a = a, b = b, tour = tour, winner = Tournament.Nobody };
        return Tournament.Finished(m, winner, winner == a ? roundsA : roundsB, winner == a ? roundsB : roundsA, false, Tournament.PoolRoundsToWin);
    }

    [Test]
    public void LeClassement_CompteLesVictoires_PuisLeMatchEntreDeux_PuisLesManches()
    {
        const ulong A = 1, B = 2, C = 3, D = 4;
        var players = new List<ulong> { A, B, C, D };
        var matches = new List<Tournament.Match>
        {
            // B bat A de justesse, mais A écrase C et D : A et B à deux victoires, A meilleur aux manches.
            Played(A, B, B, 1, 2),
            Played(A, C, A, 2, 0),
            Played(A, D, A, 2, 0),
            Played(B, C, B, 2, 1),
            Played(B, D, D, 1, 2),
            Played(C, D, C, 2, 1),
        };
        List<Tournament.Standing> table = Tournament.Standings(players, matches, null);
        Assert.AreEqual(B, table[0].id, "À égalité de victoires, le match entre les deux départage (B a battu A), avant les manches.");
        Assert.AreEqual(A, table[1].id);
        Assert.AreEqual(2, table[0].wins);
        Assert.AreEqual(1, table[0].losses);
        Assert.AreEqual(3, table[0].played);
        Assert.AreEqual(5, table[0].roundsFor);
        Assert.AreEqual(4, table[0].roundsAgainst);

        // C et D : une victoire chacun, C a battu D.
        Assert.AreEqual(C, table[2].id, "Le match entre C et D les départage.");
        Assert.AreEqual(D, table[3].id);

        // Trois joueurs à égalité, chacun vainqueur d'un des deux autres : les manches gagnées moins les perdues.
        var circle = new List<Tournament.Match>
        {
            Played(A, B, A, 2, 1),
            Played(B, C, B, 2, 0),
            Played(C, A, C, 2, 1),
        };
        List<Tournament.Standing> tied = Tournament.Standings(new List<ulong> { A, B, C }, circle, null);
        Assert.AreEqual(B, tied[0].id, "Trois à égalité, une victoire entre eux chacun : la meilleure différence de manches (B : +1).");
        Assert.AreEqual(A, tied[1].id, "A : 0, C : −1.");
        Assert.AreEqual(C, tied[2].id);
    }

    [Test]
    public void LesFinalistes_SontLesDeuxPremiers_EncoreLa()
    {
        const ulong A = 1, B = 2, C = 3;
        var players = new List<ulong> { A, B, C };
        var matches = new List<Tournament.Match> { Played(A, B, A, 2, 0), Played(A, C, A, 2, 1), Played(B, C, B, 2, 1) };
        Assert.IsTrue(Tournament.TryGetFinalists(Tournament.Standings(players, matches, null), out ulong first, out ulong second));
        Assert.AreEqual(A, first);
        Assert.AreEqual(B, second, "La finale oppose les deux premiers de la poule (TO1).");

        Assert.IsTrue(Tournament.TryGetFinalists(Tournament.Standings(players, matches, new HashSet<ulong> { B }), out first, out second));
        Assert.AreEqual(C, second, "Un joueur parti ne joue pas la finale : le suivant la joue.");

        Assert.IsFalse(Tournament.TryGetFinalists(Tournament.Standings(players, matches, new HashSet<ulong> { B, C }), out first, out _),
                       "Seul, il n'y a plus de finale.");
        Assert.AreEqual(A, first, "Le dernier encore là est en tête.");
    }

    [Test]
    public void UnJoueurQuiPart_PerdSesMatchsAVenir_ParForfait()
    {
        const ulong A = 1, B = 2, C = 3, D = 4;
        var matches = new List<Tournament.Match>
        {
            Played(A, B, A, 2, 1),
            new Tournament.Match { a = A, b = C, tour = 1, winner = Tournament.Nobody },
            new Tournament.Match { a = B, b = C, tour = 1, status = Tournament.Status.Playing, winner = Tournament.Nobody },
            new Tournament.Match { a = C, b = D, tour = 2, winner = Tournament.Nobody },
        };
        var left = new HashSet<ulong> { C };
        Assert.AreEqual(2, Tournament.ForfeitPending(matches, C, left), "Les matchs pas encore joués de C : deux.");
        Assert.AreEqual(Tournament.Status.Done, matches[1].status);
        Assert.AreEqual(A, matches[1].winner, "Son adversaire passe (TO7).");
        Assert.IsTrue(matches[1].forfeit);
        Assert.AreEqual(Tournament.PoolRoundsToWin, matches[1].RoundsOf(A), "Un forfait vaut les manches d'une victoire.");
        Assert.AreEqual(0, matches[1].RoundsOf(C));
        Assert.AreEqual(Tournament.Status.Playing, matches[2].status, "Le match en cours se règle dans son arène (le couloir le voit).");
        Assert.AreEqual(D, matches[3].winner);
        Assert.AreEqual(A, matches[0].winner, "Un match déjà joué ne change pas.");

        left.Add(D);
        var both = new List<Tournament.Match> { new Tournament.Match { a = C, b = D, winner = Tournament.Nobody } };
        Tournament.ForfeitPending(both, D, left);
        Assert.AreEqual(Tournament.Nobody, both[0].winner, "Les deux partis : personne ne gagne.");

        var leftAtCurrent = new List<Tournament.Match> { new Tournament.Match { a = A, b = B, tour = 0, winner = Tournament.Nobody } };
        Tournament.Match lost = Tournament.Finished(leftAtCurrent[0], B, 0, 1, true, Tournament.PoolRoundsToWin);
        Assert.AreEqual(Tournament.PoolRoundsToWin, lost.RoundsOf(B), "Un forfait en plein match : le vainqueur marque les manches d'une victoire.");
        Assert.AreEqual(1, lost.RoundsOf(A), "Le partant garde les manches qu'il avait gagnées.");

        List<Tournament.Standing> table = Tournament.Standings(new List<ulong> { A, B, C, D }, matches, left);
        Assert.IsTrue(table.Exists(s => s.id == C && s.left && s.losses == 2), "Celui qui part reste au classement, avec ses forfaits.");
    }

    [Test]
    public void LeProchainMatch_EstCeluiEnCours_SinonLePremierAVenir()
    {
        const ulong A = 1, B = 2, C = 3;
        var matches = new List<Tournament.Match>
        {
            Played(A, B, A, 2, 0, 0),
            new Tournament.Match { a = B, b = C, tour = 2, winner = Tournament.Nobody },
            new Tournament.Match { a = A, b = C, tour = 1, winner = Tournament.Nobody },
        };
        Assert.IsTrue(Tournament.TryGetNextMatch(matches, C, out Tournament.Match next));
        Assert.AreEqual(1, next.tour, "Le premier à venir, pas le premier de la liste.");
        Tournament.Match playing = matches[1];
        playing.status = Tournament.Status.Playing;
        matches[1] = playing;
        Assert.IsTrue(Tournament.TryGetNextMatch(matches, C, out next));
        Assert.AreEqual(2, next.tour, "Un match en cours passe avant.");
        Assert.IsFalse(Tournament.TryGetNextMatch(new List<Tournament.Match> { matches[0] }, A, out _), "Tout joué : plus de match.");
    }
}
