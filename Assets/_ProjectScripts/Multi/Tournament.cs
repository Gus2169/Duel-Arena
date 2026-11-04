using System;
using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// Le Tournoi (GDD § 11, TO1 à TO9 ; 2026-11-03), un mode à lui, ses matchs toujours en classique : une poule, chacun
/// contre chacun, dans un ordre tiré au sort devant tout le monde, puis une finale entre les deux premiers (TO1, TO8). À
/// nombre impair, un joueur se repose à chaque tour. BO3 en poule, BO5 en finale (TO4). Tous les matchs d'un tour se
/// jouent en même temps, chacun dans sa copie de l'arène (TO9 : un RoundManager « couloir » par match, TournamentSite) ;
/// ceux qui ont fini ou se reposent attendent dans la salle d'attente, d'où ils regardent les duels en cours sur ses
/// écrans, sans intervenir (TO5). Un joueur qui part déclare forfait : son adversaire passe, et ses matchs suivants aussi
/// (TO7).
///
/// Fonctions pures, testées ; RoundManager les applique. Le classement départage deux joueurs à égalité de victoires par
/// leur match, puis par les manches gagnées moins les perdues (les propositions de Claude du GDD).
/// </summary>
public static class Tournament
{
    /// <summary>De trois à six joueurs (la carte du mode) : à deux, la poule serait déjà la finale.</summary>
    public const int MinPlayers = 3;
    public const int MaxPlayers = 6;

    /// <summary>Les arènes qu'il faut au plus : un match pour deux joueurs, trois à la fois à six.</summary>
    public const int MaxArenas = MaxPlayers / 2;

    /// <summary>BO3 en poule, BO5 en finale (TO4).</summary>
    public const int PoolRoundsToWin = 2;
    public const int FinalRoundsToWin = 3;

    /// <summary>Le tour de la finale, après ceux de la poule.</summary>
    public const byte FinalTour = byte.MaxValue;

    /// <summary>Personne : pas encore de vainqueur, ou la place vide de la poule à nombre impair.</summary>
    public const ulong Nobody = ulong.MaxValue;

    public enum Status : byte
    {
        Pending,
        Playing,
        Done,
    }

    /// <summary>Où en est le tournoi : le tirage montré à tous, un tour de la poule, l'entracte entre deux tours, la
    /// finale, puis l'écran du champion (la fin du match du RoundManager principal).</summary>
    public enum Stage : byte
    {
        None,
        Draw,
        Tour,
        Break,
        Final,
        Done,
    }

    /// <summary>Un match du tournoi, publié par le serveur : ses deux joueurs, son tour (FinalTour pour la finale), sa
    /// copie de l'arène, puis son vainqueur et les manches de chacun.</summary>
    public struct Match : INetworkSerializable, IEquatable<Match>
    {
        public ulong a, b;
        public byte tour;
        public byte arena;
        public Status status;
        public ulong winner;
        public byte roundsA, roundsB;
        /// <summary>Gagné par forfait : l'autre est parti (TO7).</summary>
        public bool forfeit;

        public bool IsFinal => tour == FinalTour;
        public bool Involves(ulong id) => a == id || b == id;
        public ulong Opponent(ulong id) => a == id ? b : a;
        public int RoundsOf(ulong id) => a == id ? roundsA : b == id ? roundsB : 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref a);
            serializer.SerializeValue(ref b);
            serializer.SerializeValue(ref tour);
            serializer.SerializeValue(ref arena);
            serializer.SerializeValue(ref status);
            serializer.SerializeValue(ref winner);
            serializer.SerializeValue(ref roundsA);
            serializer.SerializeValue(ref roundsB);
            serializer.SerializeValue(ref forfeit);
        }

        public bool Equals(Match other) => a == other.a && b == other.b && tour == other.tour && arena == other.arena
                                           && status == other.status && winner == other.winner && roundsA == other.roundsA
                                           && roundsB == other.roundsB && forfeit == other.forfeit;
    }

    /// <summary>Ce que le serveur publie de l'avancée du tournoi : l'étape, le tour de la poule, le nombre de tours, les
    /// joueurs partis (un bit par place du tirage), et la fin de l'étape (le tirage, l'entracte).</summary>
    public struct State : INetworkSerializable, IEquatable<State>
    {
        public Stage stage;
        public byte tour;
        public byte tourCount;
        public byte leftMask;
        public double stageStartedAt;
        public double stageEndsAt;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref stage);
            serializer.SerializeValue(ref tour);
            serializer.SerializeValue(ref tourCount);
            serializer.SerializeValue(ref leftMask);
            serializer.SerializeValue(ref stageStartedAt);
            serializer.SerializeValue(ref stageEndsAt);
        }

        public bool Equals(State other) => stage == other.stage && tour == other.tour && tourCount == other.tourCount
                                           && leftMask == other.leftMask && stageStartedAt == other.stageStartedAt
                                           && stageEndsAt == other.stageEndsAt;
    }

    /// <summary>Une ligne du classement de la poule.</summary>
    public struct Standing
    {
        public ulong id;
        public int played, wins, losses, roundsFor, roundsAgainst;
        /// <summary>Parti du tournoi : il reste au classement, mais ne joue plus la finale.</summary>
        public bool left;

        public int RoundDifference => roundsFor - roundsAgainst;
    }

    /// <summary>Les tours de la poule : chacun rencontre tous les autres une fois ; à nombre pair, n − 1 tours ; à nombre
    /// impair, n, un joueur se reposant à chacun.</summary>
    public static int TourCount(int players) => players < 2 ? 0 : players % 2 == 0 ? players - 1 : players;

    /// <summary>Les matchs joués en même temps : un pour deux joueurs.</summary>
    public static int MatchesPerTour(int players) => players / 2;

    /// <summary>
    /// La poule, tirée au sort (TO8) : chacun contre chacun, par tours (la méthode du cercle : à nombre impair, une place
    /// vide, et celui qui y tombe se repose ce tour-là). Les joueurs sont placés au hasard, l'ordre des tours et celui des
    /// matchs de chaque tour aussi ; la liste rendue va du premier tour au dernier. Fonction pure, testée.
    /// </summary>
    public static List<Match> Schedule(IReadOnlyList<ulong> players, Random random)
    {
        var result = new List<Match>();
        if (players == null || players.Count < 2) return result;
        var seats = new List<ulong>(players);
        Shuffle(seats, random);
        if (seats.Count % 2 == 1) seats.Add(Nobody);
        int n = seats.Count;
        int tours = n - 1;
        var order = new List<int>(tours);
        for (int t = 0; t < tours; t++) order.Add(t);
        Shuffle(order, random);

        var byTour = new List<Match>[tours];
        for (int round = 0; round < tours; round++)
        {
            var matches = new List<Match>();
            for (int i = 0; i < n / 2; i++)
            {
                ulong x = seats[i], y = seats[n - 1 - i];
                if (x == Nobody || y == Nobody) continue;
                if ((round + i) % 2 == 1) (x, y) = (y, x);
                matches.Add(new Match { a = x, b = y, tour = (byte)order[round], winner = Nobody });
            }
            Shuffle(matches, random);
            byTour[order[round]] = matches;
            // La méthode du cercle : le premier reste à sa place, les autres tournent d'un cran.
            ulong last = seats[n - 1];
            seats.RemoveAt(n - 1);
            seats.Insert(1, last);
        }
        foreach (List<Match> tour in byTour) result.AddRange(tour);
        return result;
    }

    /// <summary>L'arène du `index`-ième match d'un tour : distinctes dans un tour, elles changent d'un tour à l'autre (les
    /// copies servent même à trois joueurs). La finale se joue dans la première. Fonction pure, testée.</summary>
    public static int ArenaFor(int tour, int index) => (tour + index) % MaxArenas;

    /// <summary>Tous les matchs de ce tour sont-ils finis ?</summary>
    public static bool TourDone(IReadOnlyList<Match> matches, int tour)
    {
        foreach (Match m in matches)
        {
            if (m.tour == tour && m.status != Status.Done) return false;
        }
        return true;
    }

    /// <summary>Le match fini : son vainqueur et les manches de chacun. Un forfait donne au vainqueur au moins les manches
    /// d'une victoire (`roundsToWin`) : son adversaire est parti, il passe (TO7). Fonction pure, testée.</summary>
    public static Match Finished(Match m, ulong winner, int winnerRounds, int loserRounds, bool forfeit, int roundsToWin)
    {
        if (forfeit && winner != Nobody) winnerRounds = Math.Max(winnerRounds, roundsToWin);
        m.status = Status.Done;
        m.winner = winner;
        m.forfeit = forfeit;
        byte win = (byte)Math.Clamp(winnerRounds, 0, 255), lose = (byte)Math.Clamp(loserRounds, 0, 255);
        if (winner == m.a)
        {
            m.roundsA = win;
            m.roundsB = lose;
        }
        else if (winner == m.b)
        {
            m.roundsB = win;
            m.roundsA = lose;
        }
        return m;
    }

    /// <summary>Un joueur parti : ses matchs pas encore joués sont perdus par forfait, son adversaire passe (TO7) ; si
    /// l'adversaire est parti lui aussi, personne ne gagne. Rend le nombre de matchs ainsi réglés. Fonction pure, testée.</summary>
    public static int ForfeitPending(List<Match> matches, ulong leaver, ICollection<ulong> left)
    {
        int count = 0;
        for (int i = 0; i < matches.Count; i++)
        {
            Match m = matches[i];
            if (m.status != Status.Pending || !m.Involves(leaver)) continue;
            ulong opponent = m.Opponent(leaver);
            ulong winner = left != null && left.Contains(opponent) ? Nobody : opponent;
            int need = m.IsFinal ? FinalRoundsToWin : PoolRoundsToWin;
            matches[i] = Finished(m, winner, need, 0, true, need);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Le classement de la poule : les victoires ; entre joueurs à égalité, les victoires de leurs matchs entre eux (à
    /// deux, leur match) ; puis les manches gagnées moins les perdues ; puis les manches gagnées ; puis l'ordre du tirage
    /// (`players`). Ceux qui sont partis y restent. Fonction pure, testée.
    /// </summary>
    public static List<Standing> Standings(IReadOnlyList<ulong> players, IReadOnlyList<Match> matches, ICollection<ulong> left)
    {
        var table = new List<Standing>();
        if (players == null) return table;
        foreach (ulong id in players) table.Add(new Standing { id = id, left = left != null && left.Contains(id) });
        if (matches != null)
        {
            foreach (Match m in matches)
            {
                if (m.IsFinal || m.status != Status.Done) continue;
                Credit(table, m.a, m.winner, m.roundsA, m.roundsB);
                Credit(table, m.b, m.winner, m.roundsB, m.roundsA);
            }
        }

        var seat = new Dictionary<ulong, int>();
        for (int i = 0; i < players.Count; i++) seat[players[i]] = i;
        table.Sort((x, y) => y.wins != x.wins ? y.wins.CompareTo(x.wins) : seat[x.id].CompareTo(seat[y.id]));

        // Les égalités de victoires : leurs matchs entre eux, puis les manches.
        int start = 0;
        while (start < table.Count)
        {
            int end = start + 1;
            while (end < table.Count && table[end].wins == table[start].wins) end++;
            if (end - start > 1)
            {
                var tied = new HashSet<ulong>();
                for (int i = start; i < end; i++) tied.Add(table[i].id);
                var between = new Dictionary<ulong, int>();
                foreach (ulong id in tied) between[id] = 0;
                if (matches != null)
                {
                    foreach (Match m in matches)
                    {
                        if (m.IsFinal || m.status != Status.Done || !tied.Contains(m.a) || !tied.Contains(m.b)) continue;
                        if (between.ContainsKey(m.winner)) between[m.winner]++;
                    }
                }
                table.Sort(start, end - start, Comparer<Standing>.Create((x, y) =>
                {
                    if (between[x.id] != between[y.id]) return between[y.id].CompareTo(between[x.id]);
                    if (x.RoundDifference != y.RoundDifference) return y.RoundDifference.CompareTo(x.RoundDifference);
                    if (x.roundsFor != y.roundsFor) return y.roundsFor.CompareTo(x.roundsFor);
                    return seat[x.id].CompareTo(seat[y.id]);
                }));
            }
            start = end;
        }
        return table;
    }

    /// <summary>Les deux finalistes : les deux premiers du classement encore là. Faux s'il en reste moins de deux.</summary>
    public static bool TryGetFinalists(IReadOnlyList<Standing> standings, out ulong first, out ulong second)
    {
        first = second = Nobody;
        foreach (Standing s in standings)
        {
            if (s.left) continue;
            if (first == Nobody)
            {
                first = s.id;
                continue;
            }
            second = s.id;
            return true;
        }
        return false;
    }

    /// <summary>Le prochain match de `id` pas encore fini (en cours, ou à venir), ou faux.</summary>
    public static bool TryGetNextMatch(IReadOnlyList<Match> matches, ulong id, out Match next)
    {
        next = default;
        bool found = false;
        foreach (Match m in matches)
        {
            if (!m.Involves(id) || m.status == Status.Done) continue;
            if (m.status == Status.Playing)
            {
                next = m;
                return true;
            }
            if (!found || m.tour < next.tour)
            {
                next = m;
                found = true;
            }
        }
        return found;
    }

    private static void Credit(List<Standing> table, ulong id, ulong winner, int roundsFor, int roundsAgainst)
    {
        for (int i = 0; i < table.Count; i++)
        {
            if (table[i].id != id) continue;
            Standing s = table[i];
            s.played++;
            if (winner == id) s.wins++;
            else if (winner != Nobody) s.losses++;
            s.roundsFor += roundsFor;
            s.roundsAgainst += roundsAgainst;
            table[i] = s;
            return;
        }
    }

    private static void Shuffle<T>(List<T> list, Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
