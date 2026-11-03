using System.Collections.Generic;
using System.Text;

/// <summary>L'équipe d'un joueur au Tag Team (GDD § 11, 2026-11-02) : A ou B, ou aucune (sur la tribune, sans combattre).
/// Composée par l'hôte au salon, publiée par le serveur (PlayerMatchState). Les valeurs gardent leur ordre : elles
/// voyagent par variable réseau et dans les propriétés du salon en ligne.</summary>
public enum PlayerTeam : byte
{
    None,
    A,
    B,
}

/// <summary>
/// Le Tag Team (GDD § 11, TT1 à TT9 ; 2026-11-02), un deux contre deux façon catch : deux équipes, un combattant de
/// chacune dans l'arène à la fois ; les autres attendent sur la tribune et aident leur équipe par ses gestes (TT5).
/// Quand le combattant d'une équipe tombe, le coéquipier qui attend depuis le plus longtemps entre après les 3 s des
/// réapparitions, au point le plus éloigné du survivant, qui garde ses PV (TT3, TT4, TT8). Une élimination rapporte
/// des points à l'équipe du vainqueur ; la première à TargetPoints gagne (TT6). Les équipes ont toujours autant de
/// joueurs l'une que l'autre, deux ou trois (TT2, TT9), composées par l'hôte au salon ou au hasard (TT1).
///
/// Fonctions pures, testées ; RoundManager les applique. Les points d'une élimination sont les propositions de Claude
/// du GDD, à régler en jouant.
/// </summary>
public static class TagTeam
{
    /// <summary>La première équipe à tant de points gagne (TT6).</summary>
    public const int TargetPoints = 100;

    /// <summary>Une élimination, à la tête, au gant (propositions de Claude du GDD).</summary>
    public const int KillPoints = 10, HeadshotPoints = 15, GlovePoints = 20;

    /// <summary>Deux contre deux ou trois contre trois (TT2) : la partie a six places au plus.</summary>
    public const int MinTeamSize = 2, MaxTeamSize = 3;

    /// <summary>Il faut quatre joueurs pour jouer : deux contre deux.</summary>
    public const int MinPlayers = MinTeamSize * 2;

    /// <summary>Les points d'une élimination, selon le coup qui l'a portée.</summary>
    public static int PointsFor(Health.DamageCause cause) => cause switch
    {
        Health.DamageCause.Glove => GlovePoints,
        Health.DamageCause.HeadShot => HeadshotPoints,
        _ => KillPoints,
    };

    /// <summary>Deux équipes de ces tailles peuvent-elles jouer ? Autant de joueurs de chaque côté (jamais à nombre
    /// impair, TT9), de `minSize` (deux) à trois.</summary>
    public static bool CanPlay(int teamA, int teamB, int minSize = MinTeamSize)
        => teamA == teamB && teamA >= minSize && teamA <= MaxTeamSize;

    /// <summary>L'équipe d'en face.</summary>
    public static PlayerTeam Other(PlayerTeam team) => team == PlayerTeam.A ? PlayerTeam.B : team == PlayerTeam.B ? PlayerTeam.A : PlayerTeam.None;

    /// <summary>« A », « B » (vide hors équipe).</summary>
    public static string Letter(PlayerTeam team) => team == PlayerTeam.A ? "A" : team == PlayerTeam.B ? "B" : "";

    /// <summary>« Équipe A », « Équipe B ».</summary>
    public static string Name(PlayerTeam team) => team switch
    {
        PlayerTeam.A => "Équipe A",
        PlayerTeam.B => "Équipe B",
        _ => "Hors équipe",
    };

    /// <summary>Le clic de l'hôte sur un joueur du salon : A, puis B, puis hors équipe, puis A.</summary>
    public static PlayerTeam Cycle(PlayerTeam team) => team switch
    {
        PlayerTeam.A => PlayerTeam.B,
        PlayerTeam.B => PlayerTeam.None,
        _ => PlayerTeam.A,
    };

    /// <summary>L'équipe où passe un joueur que l'hôte clique (Cycle), en sautant une équipe déjà pleine (trois joueurs).
    /// `teamA`, `teamB` : leurs joueurs, celui-ci compris s'il y est.</summary>
    public static PlayerTeam NextTeam(PlayerTeam current, int teamA, int teamB)
    {
        PlayerTeam next = Cycle(current);
        for (int i = 0; i < 2; i++)
        {
            int size = next == PlayerTeam.A ? teamA : next == PlayerTeam.B ? teamB : 0;
            if (next == PlayerTeam.None || size < MaxTeamSize) return next;
            next = Cycle(next);
        }
        return next;
    }

    /// <summary>
    /// Les équipes par défaut de `count` joueurs dans leur ordre (l'hôte, puis par arrivée) : A, B, A, B… ; le dernier
    /// d'un nombre impair, et ceux au-delà de deux équipes de trois, restent hors équipe. `keepOdd` : le dernier d'un
    /// nombre impair joue aussi (le banc de test à trois instances, RoundManager.DebugTagTeamUneven).
    /// </summary>
    public static PlayerTeam[] AutoTeams(int count, bool keepOdd = false)
    {
        var teams = new PlayerTeam[System.Math.Max(0, count)];
        int playing = System.Math.Min(keepOdd ? count : count - count % 2, MaxTeamSize * 2);
        for (int i = 0; i < playing; i++) teams[i] = i % 2 == 0 ? PlayerTeam.A : PlayerTeam.B;
        return teams;
    }

    /// <summary>
    /// Des équipes au hasard (TT1, le bouton « Équipes au hasard ») : `count` joueurs mêlés par `random`, puis répartis
    /// comme AutoTeams ; d'un nombre impair, un joueur au hasard reste hors équipe.
    /// </summary>
    public static PlayerTeam[] Shuffled(int count, System.Random random)
    {
        var order = new List<int>();
        for (int i = 0; i < count; i++) order.Add(i);
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = random != null ? random.Next(i + 1) : i;
            (order[i], order[j]) = (order[j], order[i]);
        }
        PlayerTeam[] dealt = AutoTeams(count);
        var teams = new PlayerTeam[System.Math.Max(0, count)];
        for (int i = 0; i < order.Count; i++) teams[order[i]] = dealt[i];
        return teams;
    }

    /// <summary>
    /// Le relais d'une équipe : `order[0]` combat, les autres attendent dans l'ordre. À sa mort, il passe au bout de la
    /// file, et celui qui attend depuis le plus longtemps entre. Renvoie celui qui entre (le même, seul dans son équipe).
    /// </summary>
    public static T Relay<T>(List<T> order)
    {
        if (order == null || order.Count == 0) return default;
        T fallen = order[0];
        order.RemoveAt(0);
        order.Add(fallen);
        return order[0];
    }

    /// <summary>La composition des équipes, dans une propriété du salon en ligne : « id:A;id:B;id:- ».</summary>
    public static string Format(IReadOnlyList<(string id, PlayerTeam team)> teams)
    {
        var text = new StringBuilder();
        if (teams == null) return "";
        foreach ((string id, PlayerTeam team) in teams)
        {
            if (string.IsNullOrEmpty(id) || id.IndexOf(';') >= 0 || id.IndexOf(':') >= 0) continue;
            if (text.Length > 0) text.Append(';');
            text.Append(id).Append(':').Append(team == PlayerTeam.A ? 'A' : team == PlayerTeam.B ? 'B' : '-');
        }
        return text.ToString();
    }

    /// <summary>La composition lue dans le salon (Format) : les entrées illisibles sont ignorées.</summary>
    public static Dictionary<string, PlayerTeam> Parse(string text)
    {
        var teams = new Dictionary<string, PlayerTeam>();
        if (string.IsNullOrEmpty(text)) return teams;
        foreach (string entry in text.Split(';'))
        {
            int colon = entry.LastIndexOf(':');
            if (colon <= 0 || colon != entry.Length - 2) continue;
            char code = entry[colon + 1];
            PlayerTeam team = code == 'A' ? PlayerTeam.A : code == 'B' ? PlayerTeam.B : PlayerTeam.None;
            if (code != 'A' && code != 'B' && code != '-') continue;
            teams[entry.Substring(0, colon)] = team;
        }
        return teams;
    }
}
