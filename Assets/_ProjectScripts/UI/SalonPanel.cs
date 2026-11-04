using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Le salon de la partie (2026-10-26, demandé par l'utilisateur) : après un match sans revanche, tout le
/// monde y revient, dans l'arène (RoundManager.Phase.Salon). L'hôte y retrouve les réglages de la partie et la
/// relance ; les autres l'y attendent, avec les mêmes réglages sous les yeux et qui jouera le match suivant (au
/// tirage, personne ne le sait : il se fait au lancement). Le code de la partie (ou l'adresse en réseau local,
/// chez l'hôte) reste affiché, pour inviter.
///
/// Refait le 2026-11-02 d'après la maquette retenue par l'utilisateur (« B · Salon ») : l'écran est SalonView,
/// le même que le salon du menu (LobbyController), en plein écran par-dessus l'arène.
///
/// Chez l'hôte, qui est le serveur, les réglages et la relance passent directement par RoundManager
/// (ServerSetRules, ServerLaunchFromSalon), comme les équipes du Tag Team (ServerSetTeam, ServerShuffleTeams).
/// Catégorie C : chez les autres, il ne fait que lire l'état publié. Posé sur /GameUI, avec l'UIDocument du HUD.
/// </summary>
[DisallowMultipleComponent]
public class SalonPanel : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    [Tooltip("La vue d'ensemble de l'arène (Menu_Arene.png, comme au salon du menu). Posée ici plutôt que tout MenuArtwork : ses grandes images n'ont rien à faire dans l'arène.")]
    [SerializeField] private Texture2D arenaImage;

    [Tooltip("Le nom de l'arène (celui de MenuArtwork).")]
    [SerializeField] private string arenaName = "";

    /// <summary>Le salon est-il affiché ? (le curseur est alors libre, voir GameHud)</summary>
    public static bool IsShown { get; private set; }

    private static SalonPanel current;

    private VisualElement panel;
    private SalonView salon;
    private bool shown;
    private int shownKey = int.MinValue;
    private readonly List<PlayerMatchState> ordered = new List<PlayerMatchState>();
    private readonly List<Evening.Match> eveningRecords = new List<Evening.Match>();

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        panel = document.rootVisualElement.Q<VisualElement>("salonPanel");
        salon = new SalonView(panel);
        salon.RulesChanged += ChangeRules;
        salon.LaunchClicked += Launch;
        salon.LeaveClicked += Quit;
        salon.PlayerClicked += ChangeTeam;
        salon.ShuffleClicked += ShuffleTeams;
        salon.SetArena(arenaImage, arenaName);
        shown = false;
        shownKey = int.MinValue;
        current = this;
    }

    private void OnDisable()
    {
        salon?.Dispose();
        salon = null;
        IsShown = false;
        if (current == this) current = null;
    }

    private void Update()
    {
        RoundManager rm = RoundManager.Instance;
        bool show = rm != null && rm.IsSpawned && GameHud.LocalPlayerObject != null && rm.CurrentPhase == RoundManager.Phase.Salon;
        if (show != shown)
        {
            shown = show;
            panel.EnableInClassList("hidden", !show);
            if (!show) salon.CloseWindow();
            shownKey = int.MinValue;
        }
        IsShown = show;
        if (!show) return;
        Render(rm);
        salon.Tick(Time.unscaledTime);
    }

    /// <summary>Échap : ferme la fenêtre « Changer de mode » du salon si elle est ouverte (PauseMenu) ; vrai si
    /// elle l'était.</summary>
    public static bool CloseWindow() => IsShown && current != null && current.salon != null && current.salon.CloseWindow();

    /// <summary>Cette machine héberge-t-elle la partie ? (l'hôte est le serveur)</summary>
    private static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    /// <summary>Refait le salon quand ce qu'il montre change (ses textes allouaient sinon à chaque image).</summary>
    private void Render(RoundManager rm)
    {
        MatchRules rules = rm.Rules.Sanitized();
        bool host = IsHost;
        GameHud.TryGetInvite(out string inviteCaption, out string inviteValue);
        int key = (host ? 1 : 0) + 2 * (rm.CanLaunchFromSalon ? 1 : 0);
        // Tous les réglages : l'échelle, le Death match, les options n'y étaient pas, et le salon ne se refaisait pas quand
        // l'hôte ne changeait qu'eux (vu à deux instances le 2026-11-03).
        key = key * 31 + rules.Fingerprint();
        key = key * 31 + (inviteValue ?? "").GetHashCode();
        // Le tableau de la soirée : un match de plus (au-delà de la limite, le dernier change sans que le compte bouge).
        int eveningCount = rm.EveningCount;
        key = key * 31 + eveningCount;
        if (eveningCount > 0)
        {
            Evening.Match last = rm.EveningAt(eveningCount - 1);
            key = key * 31 + last.winners.GetHashCode();
            key = key * 31 + last.losers.GetHashCode();
        }
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state == null) continue;
            key = key * 31 + (int)state.OwnerClientId;
            key = key * 31 + (int)state.Role * 8 + state.QueuePosition;
            key = key * 31 + state.DisplayName.GetHashCode();
            key = key * 31 + (state.IsOwner ? 1 : 0);
            key = key * 31 + (int)state.Team;
            if (state.TryGetComponent(out PlayerPaint paint)) key = key * 31 + paint.Paint.GetHashCode();
        }
        if (key == shownKey) return;
        shownKey = key;

        bool tagTeam = rules.IsTagTeam;
        bool tournament = rules.IsTournament;
        bool drawn = rules.randomDuelists && !tagTeam && !tournament;
        OrderPlayers(tagTeam);
        var players = new List<SalonView.Player>(ordered.Count);
        string hostName = "", first = null, second = null, firstA = null, firstB = null;
        int teamA = 0, teamB = 0;
        foreach (PlayerMatchState state in ordered)
        {
            bool hostPlayer = state.OwnerClientId == NetworkManager.ServerClientId;
            string name = state.DisplayName.ToString();
            if (hostPlayer) hostName = name;
            if (state.IsDuelist)
            {
                if (first == null) first = name.ToUpperInvariant();
                else if (second == null) second = name.ToUpperInvariant();
            }
            if (state.Team == PlayerTeam.A && teamA++ == 0) firstA = name.ToUpperInvariant();
            if (state.Team == PlayerTeam.B && teamB++ == 0) firstB = name.ToUpperInvariant();
            players.Add(new SalonView.Player
            {
                name = name,
                paint = state.TryGetComponent(out PlayerPaint paint) ? paint.Paint : default,
                role = tagTeam ? SalonView.TeamRole(state.Team, state.QueuePosition, hostPlayer, state.IsOwner)
                     : tournament ? SalonView.TournamentRole(hostPlayer, state.IsOwner)
                     : SalonView.Role(state.IsDuelist, state.QueuePosition, drawn, hostPlayer, state.IsOwner),
                local = state.IsOwner,
                team = state.Team,
            });
        }

        bool online = OnlineSession.InSession;
        string where = online ? "EN LIGNE" : "RÉSEAU LOCAL";
        string inviteNoun = string.IsNullOrEmpty(inviteValue) ? null : online ? "le code de la partie" : "l'adresse de la partie";
        salon.Show(new SalonView.Model
        {
            eyebrow = string.IsNullOrEmpty(hostName) ? $"SALON · {where}" : $"SALON DE {hostName.ToUpperInvariant()} · {where}",
            mode = GameModeCatalog.ModeOf(rules),
            rules = rules,
            host = host,
            editable = host,
            inviteCaption = inviteCaption,
            inviteValue = inviteValue,
            players = players,
            phrase = tagTeam ? SalonView.TeamPhrase(teamA, teamB, firstA, firstB)
                : tournament ? SalonView.TournamentPhrase(players.Count)
                : SalonView.Phrase(players.Count, first, second, drawn, rules.organization,
                                   rules.capacity > DuelRoster.DuelistSlots, inviteNoun),
            status = !host ? "En attente de l'hôte…"
                : rm.CanLaunchFromSalon ? "Lance la partie quand tout le monde est prêt."
                : tagTeam ? "Il faut deux équipes égales, de deux ou trois joueurs : clique un joueur pour le changer d'équipe."
                : tournament ? "Il faut trois joueurs au moins pour lancer le tournoi."
                : "Il faut deux joueurs pour lancer la partie.",
            canLaunch = rm.CanLaunchFromSalon,
            leaveLabel = "QUITTER LA PARTIE",
            teams = tagTeam,
            teamsEditable = host,
            evening = EveningLines(rm),
            eveningMatches = eveningRecords.Count,
            eveningLast = eveningRecords.Count > 0 ? Evening.MatchLine(eveningRecords[eveningRecords.Count - 1]) : null,
        });
    }

    /// <summary>Le tableau de la soirée (GDD § 14) : les matchs publiés par le serveur (eveningRecords, rempli ici), et
    /// une ligne par joueur, à sa couleur s'il est encore là.</summary>
    private List<SalonView.EveningLine> EveningLines(RoundManager rm)
    {
        eveningRecords.Clear();
        for (int i = 0; i < rm.EveningCount; i++) eveningRecords.Add(rm.EveningAt(i));
        List<Evening.Row> rows = Evening.Tally(eveningRecords);
        var lines = new List<SalonView.EveningLine>(rows.Count);
        foreach (Evening.Row row in rows)
        {
            PlayerMatchState present = null;
            foreach (PlayerMatchState state in PlayerMatchState.Spawned)
            {
                if (state != null && state.DisplayName.ToString() == row.name) present = state;
            }
            lines.Add(new SalonView.EveningLine
            {
                name = row.name,
                paint = present != null && present.TryGetComponent(out PlayerPaint paint) ? paint.Paint : default,
                local = present != null && present.IsOwner,
                wins = Evening.WinsLabel(row.wins),
                detail = Evening.Breakdown(row),
            });
        }
        return lines;
    }

    /// <summary>Les joueurs : les duellistes du match suivant, puis la tribune dans l'ordre de la file ; au Tag Team,
    /// chaque équipe dans l'ordre de son relais (SalonView les range par équipe).</summary>
    private void OrderPlayers(bool tagTeam)
    {
        ordered.Clear();
        if (tagTeam)
        {
            foreach (PlayerMatchState state in PlayerMatchState.Spawned)
            {
                if (state != null) ordered.Add(state);
            }
            ordered.Sort(Comparer<PlayerMatchState>.Create((a, b) =>
                a.Team != b.Team ? ((int)a.Team).CompareTo((int)b.Team) : a.QueuePosition.CompareTo(b.QueuePosition)));
            return;
        }
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && state.IsDuelist) ordered.Add(state);
        }
        int duelists = ordered.Count;
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && !state.IsDuelist) ordered.Add(state);
        }
        ordered.Sort(duelists, ordered.Count - duelists, Comparer<PlayerMatchState>.Create((a, b) => a.QueuePosition.CompareTo(b.QueuePosition)));
    }

    private static void ChangeRules(MatchRules rules)
    {
        RoundManager rm = RoundManager.Instance;
        if (!IsHost || rm == null || rm.CurrentPhase != RoundManager.Phase.Salon) return;
        rm.ServerSetRules(rules.Sanitized());
    }

    /// <summary>L'hôte clique un joueur au Tag Team : il passe dans l'autre équipe, puis hors équipe (TagTeam.NextTeam).</summary>
    private void ChangeTeam(int index)
    {
        RoundManager rm = RoundManager.Instance;
        if (!IsHost || rm == null || index < 0 || index >= ordered.Count || ordered[index] == null) return;
        PlayerMatchState state = ordered[index];
        PlayerTeam next = TagTeam.NextTeam(state.Team, RoundManager.TeamCount(PlayerTeam.A), RoundManager.TeamCount(PlayerTeam.B));
        rm.ServerSetTeam(state.OwnerClientId, next);
    }

    private static void ShuffleTeams()
    {
        RoundManager rm = RoundManager.Instance;
        if (IsHost && rm != null) rm.ServerShuffleTeams();
    }

    private void Launch()
    {
        RoundManager rm = RoundManager.Instance;
        if (IsHost && rm != null) rm.ServerLaunchFromSalon();
    }

    private void Quit()
    {
        SessionFlow.ReturnToMenu("");
    }
}
