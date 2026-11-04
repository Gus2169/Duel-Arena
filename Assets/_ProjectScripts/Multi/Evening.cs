using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// Le tableau de la soirée (GDD § 14, tranché le 2026-10-25) : au salon, qui a gagné quoi depuis l'ouverture de la
/// partie. Pas de classement mondial. Le serveur retient chaque match fini (RoundManager, sa liste `evening`, publiée) ;
/// chaque machine en tire le tableau (Tally) et ses textes. Un tournoi compte pour un match, gagné par son champion ;
/// au Tag Team, tous les membres de l'équipe gagnante le gagnent. Les joueurs y sont désignés par leur pseudo (unique
/// dans la partie) : qui part et revient sous le même pseudo garde ses victoires. Fonctions pures, testées.
/// </summary>
public static class Evening
{
    /// <summary>Les matchs retenus, au plus : au-delà, le plus ancien sort (une soirée en joue rarement autant).</summary>
    public const int MaxMatches = 40;

    /// <summary>Entre deux pseudos d'un même camp (aucun pseudo n'en contient : PlayerMatchState.SanitizeName).</summary>
    public const char Separator = '\n';

    /// <summary>Un match fini : son mode, ses vainqueurs et ses vaincus (pseudos), son score, et s'il a été gagné par
    /// forfait.</summary>
    public struct Match : INetworkSerializeByMemcpy, IEquatable<Match>
    {
        public GameModeId mode;
        public bool forfeit;
        public ushort winnerScore;
        public ushort loserScore;
        public FixedString128Bytes winners;
        public FixedString128Bytes losers;

        public bool Equals(Match other) => mode == other.mode && forfeit == other.forfeit && winnerScore == other.winnerScore
                                           && loserScore == other.loserScore && winners.Equals(other.winners)
                                           && losers.Equals(other.losers);
    }

    /// <summary>Un match : `winners` et `losers` (des pseudos), le score du vainqueur et celui du meilleur vaincu.</summary>
    public static Match Record(GameModeId mode, IEnumerable<string> winners, IEnumerable<string> losers, int winnerScore,
                               int loserScore, bool forfeit)
    {
        return new Match
        {
            mode = mode,
            forfeit = forfeit,
            winnerScore = (ushort)Math.Clamp(winnerScore, 0, ushort.MaxValue),
            loserScore = (ushort)Math.Clamp(loserScore, 0, ushort.MaxValue),
            winners = Join(winners),
            losers = Join(losers),
        };
    }

    /// <summary>Des pseudos, mis bout à bout (ceux qui n'y tiendraient plus restent dehors).</summary>
    public static FixedString128Bytes Join(IEnumerable<string> names)
    {
        var joined = new FixedString128Bytes();
        if (names == null) return joined;
        foreach (string name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            var next = new FixedString128Bytes();
            next.Append(joined);
            if (next.Length > 0) next.Append(Separator);
            if (next.Append(name) != FormatError.None) break;
            joined = next;
        }
        return joined;
    }

    /// <summary>Les pseudos d'un camp.</summary>
    public static List<string> Split(FixedString128Bytes names)
    {
        var result = new List<string>();
        string text = names.ToString();
        if (text.Length == 0) return result;
        foreach (string name in text.Split(Separator))
        {
            if (name.Length > 0) result.Add(name);
        }
        return result;
    }

    /// <summary>Une ligne du tableau : un joueur, ses victoires, ses matchs joués, et ses victoires par mode, dans
    /// l'ordre où il les a gagnées.</summary>
    public sealed class Row
    {
        public string name;
        public int wins;
        public int played;
        public readonly List<KeyValuePair<GameModeId, int>> winsByMode = new List<KeyValuePair<GameModeId, int>>();
        internal int firstSeen;

        internal void AddWin(GameModeId mode)
        {
            wins++;
            for (int i = 0; i < winsByMode.Count; i++)
            {
                if (winsByMode[i].Key != mode) continue;
                winsByMode[i] = new KeyValuePair<GameModeId, int>(mode, winsByMode[i].Value + 1);
                return;
            }
            winsByMode.Add(new KeyValuePair<GameModeId, int>(mode, 1));
        }
    }

    /// <summary>
    /// Le tableau de la soirée : chaque joueur d'au moins un match, ses victoires et ses matchs. Rangé par victoires,
    /// puis par matchs joués (le moins, à victoires égales : le meilleur bilan), puis par ordre d'arrivée dans la soirée.
    /// </summary>
    public static List<Row> Tally(IReadOnlyList<Match> matches)
    {
        var rows = new List<Row>();
        if (matches == null) return rows;
        Row Find(string name)
        {
            foreach (Row row in rows)
            {
                if (row.name == name) return row;
            }
            var added = new Row { name = name, firstSeen = rows.Count };
            rows.Add(added);
            return added;
        }
        foreach (Match match in matches)
        {
            foreach (string name in Split(match.winners))
            {
                Row row = Find(name);
                row.played++;
                row.AddWin(match.mode);
            }
            foreach (string name in Split(match.losers))
            {
                Find(name).played++;
            }
        }
        rows.Sort((a, b) => a.wins != b.wins ? b.wins.CompareTo(a.wins)
                          : a.played != b.played ? a.played.CompareTo(b.played)
                          : a.firstSeen.CompareTo(b.firstSeen));
        return rows;
    }

    /// <summary>Le titre de la section : « LA SOIRÉE · 3 MATCHS ».</summary>
    public static string Title(int matches) => $"LA SOIRÉE · {matches} {(matches > 1 ? "MATCHS" : "MATCH")}";

    /// <summary>Les victoires d'un joueur, en capitales : « 3 VICTOIRES », « 1 VICTOIRE », « 0 VICTOIRE ».</summary>
    public static string WinsLabel(int wins) => $"{wins} {(wins > 1 ? "VICTOIRES" : "VICTOIRE")}";

    /// <summary>Ce qu'il a gagné, et sur combien de matchs : « Classique 2 · Gun Game 1 · sur 4 matchs ».</summary>
    public static string Breakdown(Row row)
    {
        var parts = new List<string>();
        foreach (KeyValuePair<GameModeId, int> pair in row.winsByMode)
            parts.Add($"{GameModeCatalog.Name(pair.Key)} {pair.Value}");
        parts.Add($"sur {row.played} {(row.played > 1 ? "matchs" : "match")}");
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Le dernier match, en une ligne : « Classique : Faucon bat Rivet, 2 – 1 », « … par forfait », « Tag Team : Faucon
    /// et Ombre battent Rivet et Pic, 100 – 25 », « Tournoi : Faucon champion ». Sans score quand il n'en a pas (0 – 0 :
    /// le Gun Game, que l'échelle départage).
    /// </summary>
    public static string MatchLine(Match match)
    {
        List<string> winners = Split(match.winners);
        List<string> losers = Split(match.losers);
        string mode = GameModeCatalog.Name(match.mode);
        if (winners.Count == 0) return $"{mode} : personne ne gagne";
        bool several = winners.Count > 1;
        if (match.mode == GameModeId.Tournament) return $"{mode} : {Names(winners)} champion{(match.forfeit ? ", par forfait" : "")}";
        string line = losers.Count > 0
            ? $"{mode} : {Names(winners)} {(several ? "battent" : "bat")} {Names(losers)}"
            : $"{mode} : {Names(winners)} {(several ? "gagnent" : "gagne")}";
        if (match.forfeit) return line + " par forfait";
        if (match.winnerScore == 0 && match.loserScore == 0) return line;
        return $"{line}, {Score(match.mode, match.winnerScore)} – {Score(match.mode, match.loserScore)}";
    }

    /// <summary>« Faucon », « Faucon et Ombre », « Faucon, Ombre et Rivet ».</summary>
    private static string Names(List<string> names)
    {
        if (names.Count == 1) return names[0];
        return string.Join(", ", names.GetRange(0, names.Count - 1)) + " et " + names[names.Count - 1];
    }

    /// <summary>Un score : des manches, des éliminations, ou des points (la Zone, en milliers à la française).</summary>
    private static string Score(GameModeId mode, int score) => mode == GameModeId.Zone ? ZoneMode.Points(score) : score.ToString();
}
