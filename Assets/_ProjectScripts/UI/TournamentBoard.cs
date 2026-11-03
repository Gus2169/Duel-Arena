using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Le tableau du Tournoi (2026-11-03, GDD § 11), sur /GameUI de l'arène, en haut à droite : dans la salle d'attente, où en
/// est le tournoi (le tirage, le tour, l'entracte, la finale), ce qui attend le joueur (son prochain adversaire, son
/// repos), le classement de la poule et les matchs du tour, en cours (leurs manches, leur arène) ou à venir. Pendant son
/// propre match, le joueur ne le voit pas : la barre de score et sa ligne en tiennent lieu (GameHud).
///
/// Catégorie C pure : il lit ce que publie le RoundManager de la scène (le calendrier, l'avancée) et ses couloirs (les
/// manches en cours), et ne décide rien. Ses textes ne sont refaits qu'au changement. Les fonctions qui les écrivent sont
/// pures, testées ; GameHud s'en sert aussi pour ses annonces et la ligne d'un match.
/// </summary>
[DisallowMultipleComponent]
public class TournamentBoard : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    private VisualElement board;
    private Label stageLabel;
    private Label mineLabel;
    private Label matchesHeading;
    private VisualElement standingsRows;
    private VisualElement matchesRows;
    private int shownStageKey = int.MinValue;
    private int shownDataKey = int.MinValue;

    private readonly List<Tournament.Match> matches = new List<Tournament.Match>();
    private readonly List<ulong> players = new List<ulong>();
    private readonly HashSet<ulong> left = new HashSet<ulong>();
    private readonly List<Tournament.Match> shownMatches = new List<Tournament.Match>();
    private readonly List<int> shownIndices = new List<int>();

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;
        board = root.Q<VisualElement>("tournamentBoard");
        stageLabel = root.Q<Label>("tournamentStage");
        mineLabel = root.Q<Label>("tournamentMine");
        matchesHeading = root.Q<Label>("tournamentMatchesHeading");
        standingsRows = root.Q<VisualElement>("tournamentStandings");
        matchesRows = root.Q<VisualElement>("tournamentMatches");
        // Les éléments viennent d'être retrouvés : leurs textes sont à refaire.
        shownStageKey = shownDataKey = int.MinValue;
    }

    private void LateUpdate()
    {
        if (board == null) return;
        RoundManager main = RoundManager.Instance;
        NetworkObject local = GameHud.LocalPlayerObject;
        bool show = local != null && main != null && main.IsSpawned && main.TournamentMode
                    && main.CurrentPhase == RoundManager.Phase.Tournament
                    && ReferenceEquals(RoundManager.Local, main)
                    && !MatchEndScreen.IsShown && !SalonPanel.IsShown;
        board.EnableInClassList("hidden", !show);
        if (!show) return;

        ulong me = local.OwnerClientId;
        Tournament.State s = main.TournamentProgress;
        int seconds = Mathf.CeilToInt(main.TournamentStageTimeLeft);
        int stageKey = (((int)s.stage * 256 + s.tour) * 256 + s.tourCount) * 1024 + Mathf.Clamp(seconds, 0, 1023);
        if (stageKey != shownStageKey)
        {
            shownStageKey = stageKey;
            stageLabel.text = StageLine(s, seconds);
        }
        int dataKey = DataKey(main, me);
        if (dataKey == shownDataKey) return;
        // Un match « en cours » sans son couloir : celui-ci vient de disparaître, et le résultat, publié avant par le
        // serveur, arrive une image plus tard (la disparition voyage plus vite). On garde ce qui est montré plutôt qu'un
        // instant de « 0 – 0 » ; au plus une seconde.
        if (LaneMissing(main))
        {
            if (missingSince < 0f) missingSince = Time.unscaledTime;
            if (Time.unscaledTime - missingSince < 1f) return;
        }
        missingSince = -1f;
        shownDataKey = dataKey;
        Rebuild(main, me, s);
    }

    private float missingSince = -1f;

    /// <summary>Un match du calendrier est-il en cours sans que son couloir soit là (il vient de partir, ou d'arriver) ?</summary>
    private static bool LaneMissing(RoundManager main)
    {
        for (int i = 0; i < main.TournamentMatchCount; i++)
        {
            if (main.TournamentMatchAt(i).status == Tournament.Status.Playing && LaneOf(i) == null) return true;
        }
        return false;
    }

    /// <summary>Ce dont dépendent le classement, les matchs et la ligne du joueur : le calendrier publié, l'avancée, les
    /// manches des couloirs en cours. Calculé à chaque image sans rien allouer.</summary>
    private static int DataKey(RoundManager main, ulong me)
    {
        Tournament.State s = main.TournamentProgress;
        int key = (int)s.stage * 31 + s.tour;
        key = key * 31 + s.leftMask;
        key = key * 31 + (int)me;
        for (int i = 0; i < main.TournamentPlayerCount; i++) key = key * 31 + (int)main.TournamentPlayerAt(i);
        for (int i = 0; i < main.TournamentMatchCount; i++)
        {
            Tournament.Match m = main.TournamentMatchAt(i);
            key = key * 31 + ((int)m.status * 16 + m.roundsA) * 16 + m.roundsB;
            key = key * 31 + (int)m.winner + m.arena;
        }
        IReadOnlyList<RoundManager> lanes = RoundManager.Lanes;
        for (int i = 0; i < lanes.Count; i++)
        {
            RoundManager lane = lanes[i];
            if (lane == null || !lane.IsSpawned) continue;
            RoundManager.LaneInfo info = lane.Lane;
            key = key * 31 + info.match;
            key = key * 31 + lane.WinsOf(info.a) * 8 + lane.WinsOf(info.b);
        }
        return key;
    }

    private void Rebuild(RoundManager main, ulong me, Tournament.State s)
    {
        CollectMatches(main, matches);
        CollectPlayers(main, players);
        CollectLeft(main, players, left);
        List<Tournament.Standing> table = Tournament.Standings(players, matches, left);

        mineLabel.text = MineLine(s, me, players, matches, left, NameOf);

        standingsRows.Clear();
        for (int i = 0; i < table.Count; i++)
        {
            Tournament.Standing row = table[i];
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("tournament-row");
            line.EnableInClassList("tournament-row--me", row.id == me);
            line.EnableInClassList("tournament-row--left", row.left);
            line.Add(Cell((i + 1).ToString(), "tournament-row__rank"));
            Label name = Cell(NameOf(row.id), "tournament-row__name");
            name.style.color = GameHud.PaintOf(row.id);
            line.Add(name);
            line.Add(Cell(Record(row), "tournament-row__record"));
            line.Add(Cell(row.left ? "" : RoundDifference(row.RoundDifference), "tournament-row__diff"));
            standingsRows.Add(line);
        }

        string heading = MatchesHeading(s);
        matchesHeading.text = heading ?? "";
        matchesHeading.EnableInClassList("hidden", heading == null);
        matchesRows.Clear();
        BoardMatches(s, matches, table, shownMatches, shownIndices);
        for (int i = 0; i < shownMatches.Count; i++)
        {
            Tournament.Match m = shownMatches[i];
            int liveA = 0, liveB = 0;
            RoundManager lane = shownIndices[i] >= 0 ? LaneOf(shownIndices[i]) : null;
            if (lane != null)
            {
                liveA = lane.WinsOf(m.a);
                liveB = lane.WinsOf(m.b);
            }
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("tournament-match");
            line.EnableInClassList("tournament-match--mine", m.Involves(me));
            Label a = Cell(NameOf(m.a), "tournament-match__name");
            a.style.color = GameHud.PaintOf(m.a);
            line.Add(a);
            line.Add(Cell(MatchScore(m, liveA, liveB), "tournament-match__score"));
            Label b = Cell(NameOf(m.b), "tournament-match__name");
            b.style.color = GameHud.PaintOf(m.b);
            b.AddToClassList("tournament-match__name--right");
            line.Add(b);
            line.Add(Cell(MatchWhere(m), "tournament-match__where"));
            matchesRows.Add(line);
        }
    }

    private static RoundManager LaneOf(int matchIndex)
    {
        IReadOnlyList<RoundManager> lanes = RoundManager.Lanes;
        for (int i = 0; i < lanes.Count; i++)
        {
            RoundManager lane = lanes[i];
            if (lane != null && lane.IsSpawned && lane.Lane.match == matchIndex) return lane;
        }
        return null;
    }

    // Chaque machine : le pseudo de chaque joueur du tournoi, gardé pour quand il sera parti.
    private static readonly Dictionary<ulong, string> knownNames = new Dictionary<ulong, string>();

    /// <summary>Le pseudo d'un joueur du tournoi : le sien, même parti (gardé tant qu'il était là), sinon « Joueur N ».</summary>
    public static string NameOf(ulong clientId)
    {
        PlayerMatchState state = PlayerMatchState.Of(clientId);
        if (state != null)
        {
            string name = state.DisplayName;
            knownNames[clientId] = name;
            return name;
        }
        return knownNames.TryGetValue(clientId, out string known) ? known : PlayerMatchState.NameOf(clientId);
    }

    private static Label Cell(string text, string className)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(className);
        return label;
    }

    /// <summary>Le calendrier publié (dans l'ordre : la poule, tour par tour, puis la finale).</summary>
    public static void CollectMatches(RoundManager main, List<Tournament.Match> result)
    {
        result.Clear();
        if (main == null) return;
        for (int i = 0; i < main.TournamentMatchCount; i++) result.Add(main.TournamentMatchAt(i));
    }

    /// <summary>Les joueurs du tournoi, dans l'ordre du tirage.</summary>
    public static void CollectPlayers(RoundManager main, List<ulong> result)
    {
        result.Clear();
        if (main == null) return;
        for (int i = 0; i < main.TournamentPlayerCount; i++) result.Add(main.TournamentPlayerAt(i));
    }

    /// <summary>Ceux des joueurs du tournoi qui l'ont quitté (TO7).</summary>
    public static void CollectLeft(RoundManager main, IReadOnlyList<ulong> players, HashSet<ulong> result)
    {
        result.Clear();
        if (main == null) return;
        for (int i = 0; i < players.Count; i++)
        {
            if (main.TournamentPlayerLeft(players[i])) result.Add(players[i]);
        }
    }

    // ------------------------------------------------------------------
    // Les textes (fonctions pures, testées)
    // ------------------------------------------------------------------

    /// <summary>L'étape du tournoi, en tête du tableau : « LE TIRAGE · LE TOUR 1 DANS 9 S », « TOUR 2 SUR 3 »,
    /// « ENTRACTE · LA FINALE DANS 6 S », « LA FINALE ». `seconds` : ce qui reste de l'étape (le tirage, l'entracte).</summary>
    public static string StageLine(Tournament.State s, int seconds)
    {
        seconds = Mathf.Max(0, seconds);
        switch (s.stage)
        {
            case Tournament.Stage.Draw:
                return $"LE TIRAGE · LE TOUR 1 DANS {seconds} S";
            case Tournament.Stage.Tour:
                return $"TOUR {s.tour + 1} SUR {s.tourCount}";
            case Tournament.Stage.Break:
                return s.tour + 1 < s.tourCount ? $"ENTRACTE · LE TOUR {s.tour + 2} DANS {seconds} S"
                                                : $"ENTRACTE · LA FINALE DANS {seconds} S";
            case Tournament.Stage.Final:
                return "LA FINALE";
            case Tournament.Stage.Done:
                return "LE TOURNOI EST FINI";
            default:
                return "LE TOURNOI";
        }
    }

    /// <summary>
    /// Ce qui attend le joueur, sous l'étape : « TU AFFRONTES GUSTAVO 2 AU TOUR 2 », « TU TE REPOSES CE TOUR · PUIS
    /// GUSTAVO 3 », « TON MATCH EST FINI », « TU REGARDES LA FINALE »… ; pour qui n'est pas du tournoi (arrivé après le
    /// tirage), qu'il jouera le suivant.
    /// </summary>
    public static string MineLine(Tournament.State s, ulong me, IReadOnlyList<ulong> players, IReadOnlyList<Tournament.Match> matches,
                                  ICollection<ulong> left, Func<ulong, string> nameOf)
    {
        if (!Contains(players, me)) return "TU REGARDES : TU JOUERAS LE PROCHAIN TOURNOI";
        if (s.stage == Tournament.Stage.Final || s.stage == Tournament.Stage.Done)
        {
            foreach (Tournament.Match m in matches)
            {
                if (m.IsFinal) return m.Involves(me) ? "TU JOUES LA FINALE" : "TU REGARDES LA FINALE";
            }
            return "";
        }

        bool hasNext = TryGetNextPoolMatch(matches, me, out Tournament.Match next);
        string then = hasNext ? $" · PUIS {Upper(nameOf(next.Opponent(me)))}" : "";
        if (s.stage == Tournament.Stage.Tour)
        {
            if (hasNext && next.tour == s.tour) return $"TON MATCH CONTRE {Upper(nameOf(next.Opponent(me)))} COMMENCE";
            if (!TryGetTourMatch(matches, me, s.tour, out Tournament.Match mine)) return "TU TE REPOSES CE TOUR" + then;
            return (mine.forfeit && mine.winner == me ? "TON ADVERSAIRE EST PARTI : MATCH GAGNÉ" : "TON MATCH EST FINI") + then;
        }

        // Le tirage ou l'entracte : le tour qui vient.
        int upcoming = s.stage == Tournament.Stage.Draw ? 0 : s.tour + 1;
        if (upcoming >= s.tourCount && s.stage != Tournament.Stage.Draw)
        {
            // La poule est finie : la finale, entre les deux premiers encore là.
            List<Tournament.Standing> table = Tournament.Standings(players, matches, left);
            if (!Tournament.TryGetFinalists(table, out ulong first, out ulong second)) return "";
            return first == me || second == me ? "TU JOUES LA FINALE" : "TU REGARDES LA FINALE";
        }
        if (!hasNext) return "TES MATCHS DE POULE SONT FINIS";
        if (next.tour == upcoming) return $"TU AFFRONTES {Upper(nameOf(next.Opponent(me)))} AU TOUR {upcoming + 1}";
        return $"TU TE REPOSES AU TOUR {upcoming + 1}" + then;
    }

    /// <summary>
    /// L'annonce d'une étape, au centre de l'écran de qui est dans la salle d'attente : le tirage (« Le tournoi
    /// commence »), un tour (« Tour 2 sur 3 », et le repos), l'entracte et ce qui suit (son adversaire, ou la finale), la
    /// finale. Faux s'il n'y a rien à annoncer (la fin : l'écran du champion). `seconds` : la durée de l'étape.
    /// </summary>
    public static bool TryGetAnnouncement(Tournament.State s, ulong me, IReadOnlyList<ulong> players, IReadOnlyList<Tournament.Match> matches,
                                          ICollection<ulong> left, int seconds, Func<ulong, string> nameOf, out string big, out string small)
    {
        big = small = null;
        bool entrant = Contains(players, me);
        seconds = Mathf.Max(0, seconds);
        switch (s.stage)
        {
            case Tournament.Stage.Draw:
            {
                int pool = 0;
                foreach (Tournament.Match m in matches)
                {
                    if (!m.IsFinal) pool++;
                }
                big = "Le tournoi commence";
                small = $"{players.Count} joueurs · {pool} matchs de poule en {s.tourCount} tours, puis la finale";
                return true;
            }
            case Tournament.Stage.Tour:
                big = $"Tour {s.tour + 1} sur {s.tourCount}";
                small = !entrant ? "Les matchs passent sur les écrans."
                      : !TryGetTourMatch(matches, me, s.tour, out Tournament.Match mine) ? "Tu te reposes : les matchs passent sur les écrans."
                      : mine.status != Tournament.Status.Done ? "Ton match commence."
                      : mine.forfeit && mine.winner == me ? "Ton adversaire est parti : tu gagnes ce match par forfait."
                      : "Tu te reposes : les matchs passent sur les écrans.";
                return true;
            case Tournament.Stage.Break:
            {
                big = $"Tour {s.tour + 1} fini";
                if (s.tour + 1 >= s.tourCount)
                {
                    List<Tournament.Standing> table = Tournament.Standings(players, matches, left);
                    small = Tournament.TryGetFinalists(table, out ulong first, out ulong second)
                        ? $"La finale dans {seconds} s : {nameOf(first)} contre {nameOf(second)}"
                        : $"La finale dans {seconds} s";
                    return true;
                }
                small = $"Le tour {s.tour + 2} dans {seconds} s";
                if (entrant && TryGetNextPoolMatch(matches, me, out Tournament.Match next))
                    small += next.tour == s.tour + 1 ? $" : tu affrontes {nameOf(next.Opponent(me))}" : " : tu te reposes";
                return true;
            }
            case Tournament.Stage.Final:
                foreach (Tournament.Match m in matches)
                {
                    if (!m.IsFinal) continue;
                    big = "La finale";
                    small = $"{nameOf(m.a)} contre {nameOf(m.b)}, en BO{Tournament.FinalRoundsToWin * 2 - 1}";
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    /// <summary>Le titre des matchs du tableau, ou null : « TOUR 1 » au tirage, « LES MATCHS DU TOUR » pendant un tour,
    /// « TOUR 3 · À VENIR » ou « LA FINALE · À VENIR » à l'entracte, « LA FINALE ».</summary>
    public static string MatchesHeading(Tournament.State s)
    {
        switch (s.stage)
        {
            case Tournament.Stage.Draw: return "TOUR 1 · À VENIR";
            case Tournament.Stage.Tour: return "LES MATCHS DU TOUR";
            case Tournament.Stage.Break: return s.tour + 1 < s.tourCount ? $"TOUR {s.tour + 2} · À VENIR" : "LA FINALE · À VENIR";
            case Tournament.Stage.Final: return "LA FINALE";
            default: return null;
        }
    }

    /// <summary>
    /// Les matchs que montre le tableau : ceux du premier tour au tirage, ceux du tour en cours, ceux du tour suivant à
    /// l'entracte (la finale entre les deux premiers, si la poule est finie), la finale. `indices` : la place de chacun
    /// dans le calendrier (-1 pour la finale annoncée, pas encore au calendrier).
    /// </summary>
    public static void BoardMatches(Tournament.State s, IReadOnlyList<Tournament.Match> matches, IReadOnlyList<Tournament.Standing> table,
                                    List<Tournament.Match> result, List<int> indices)
    {
        result.Clear();
        indices.Clear();
        int tour;
        switch (s.stage)
        {
            case Tournament.Stage.Draw:
                tour = 0;
                break;
            case Tournament.Stage.Tour:
                tour = s.tour;
                break;
            case Tournament.Stage.Break:
                tour = s.tour + 1;
                if (tour >= s.tourCount)
                {
                    if (Tournament.TryGetFinalists(table, out ulong first, out ulong second))
                    {
                        result.Add(new Tournament.Match { a = first, b = second, tour = Tournament.FinalTour, winner = Tournament.Nobody });
                        indices.Add(-1);
                    }
                    return;
                }
                break;
            case Tournament.Stage.Final:
                tour = Tournament.FinalTour;
                break;
            default:
                return;
        }
        for (int i = 0; i < matches.Count; i++)
        {
            if (matches[i].tour != tour) continue;
            result.Add(matches[i]);
            indices.Add(i);
        }
    }

    /// <summary>Le score d'un match au tableau : « 1 – 0 » en cours (les manches de son couloir), le résultat une fois
    /// fini, un tiret à venir.</summary>
    public static string MatchScore(Tournament.Match m, int liveA, int liveB)
    {
        switch (m.status)
        {
            case Tournament.Status.Playing: return $"{liveA} – {liveB}";
            case Tournament.Status.Done: return $"{m.roundsA} – {m.roundsB}";
            default: return "–";
        }
    }

    /// <summary>Où en est un match : son arène en cours, fini, gagné par forfait.</summary>
    public static string MatchWhere(Tournament.Match m)
    {
        switch (m.status)
        {
            case Tournament.Status.Playing: return $"ARÈNE {m.arena + 1}";
            case Tournament.Status.Done: return m.forfeit ? "FORFAIT" : "FINI";
            default: return "";
        }
    }

    /// <summary>Sous le titre de l'écran du champion : sa finale (« Finale gagnée 3 – 1 contre Gustavo 2 »), ou le forfait
    /// qui l'a fait champion.</summary>
    public static string ChampionLine(ulong champion, IReadOnlyList<Tournament.Match> matches, bool forfeit, Func<ulong, string> nameOf)
    {
        foreach (Tournament.Match m in matches)
        {
            if (!m.IsFinal || m.status != Tournament.Status.Done || m.winner != champion) continue;
            ulong other = m.Opponent(champion);
            if (m.forfeit) return $"Son adversaire de la finale, {nameOf(other)}, a quitté la partie";
            return $"Finale gagnée {m.RoundsOf(champion)} – {m.RoundsOf(other)} contre {nameOf(other)}";
        }
        return forfeit ? "Les autres joueurs ont quitté le tournoi" : "";
    }

    /// <summary>
    /// L'ordre du tableau de l'écran du champion : le champion, son adversaire de la finale, puis les autres dans l'ordre
    /// du classement de la poule.
    /// </summary>
    public static void ChampionOrder(ulong champion, IReadOnlyList<Tournament.Match> matches, List<Tournament.Standing> table)
    {
        ulong finalist = Tournament.Nobody;
        foreach (Tournament.Match m in matches)
        {
            if (m.IsFinal && m.Involves(champion)) finalist = m.Opponent(champion);
        }
        MoveToFront(table, finalist);
        MoveToFront(table, champion);
    }

    private static void MoveToFront(List<Tournament.Standing> table, ulong id)
    {
        for (int i = 0; i < table.Count; i++)
        {
            if (table[i].id != id) continue;
            Tournament.Standing row = table[i];
            table.RemoveAt(i);
            table.Insert(0, row);
            return;
        }
    }

    /// <summary>Le bilan d'un joueur au classement : « 2 V · 1 D », ou « PARTI ».</summary>
    public static string Record(Tournament.Standing s) => s.left ? "PARTI" : $"{s.wins} V · {s.losses} D";

    /// <summary>Les manches gagnées moins les perdues : « +3 », « 0 », « −2 ».</summary>
    public static string RoundDifference(int difference) => difference > 0 ? $"+{difference}" : difference < 0 ? $"−{-difference}" : "0";

    /// <summary>Pendant un match du Tournoi, sous la barre de score : « TOUR 2 SUR 3 · BO3 », « FINALE · BO5 ».</summary>
    public static string LaneLine(RoundManager.LaneInfo lane, RoundManager main)
    {
        string bo = $"BO{Mathf.Max(1, lane.roundsToWin * 2 - 1)}";
        if (lane.final) return $"FINALE · {bo}";
        int tour = TourOf(lane, main);
        int count = main != null ? main.TournamentProgress.tourCount : 0;
        return tour >= 0 && count > 0 ? $"TOUR {tour + 1} SUR {count} · {bo}" : $"TOURNOI · {bo}";
    }

    /// <summary>Au décompte d'une manche d'un match du Tournoi : « Tour 2 · manche 1 », « Finale · manche 3 ».</summary>
    public static string LaneRoundLine(RoundManager.LaneInfo lane, RoundManager main, int round)
    {
        if (lane.final) return $"Finale · manche {round}";
        int tour = TourOf(lane, main);
        return tour >= 0 ? $"Tour {tour + 1} · manche {round}" : $"Manche {round}";
    }

    private static int TourOf(RoundManager.LaneInfo lane, RoundManager main)
    {
        if (main == null || lane.match >= main.TournamentMatchCount) return -1;
        Tournament.Match m = main.TournamentMatchAt(lane.match);
        return m.IsFinal ? -1 : m.tour;
    }

    private static bool TryGetNextPoolMatch(IReadOnlyList<Tournament.Match> matches, ulong me, out Tournament.Match next)
        => Tournament.TryGetNextMatch(matches, me, out next) && !next.IsFinal;

    /// <summary>Le match de `me` dans ce tour de la poule (en cours, à venir, fini ou gagné par forfait), ou faux : il se
    /// repose.</summary>
    private static bool TryGetTourMatch(IReadOnlyList<Tournament.Match> matches, ulong me, int tour, out Tournament.Match found)
    {
        foreach (Tournament.Match m in matches)
        {
            if (m.tour != tour || m.IsFinal || !m.Involves(me)) continue;
            found = m;
            return true;
        }
        found = default;
        return false;
    }

    private static bool Contains(IReadOnlyList<ulong> ids, ulong id)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id) return true;
        }
        return false;
    }

    private static string Upper(string text) => string.IsNullOrEmpty(text) ? "" : text.ToUpperInvariant();
}
