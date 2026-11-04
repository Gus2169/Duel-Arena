using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// L'écran de fin de match (2026-10-12, demandé par l'utilisateur) : à gauche le robot du gagnant
/// qui célèbre dans l'arène (VictoryStage), à droite « Untel a gagné ! » et le tableau des scores des
/// deux joueurs (manches, précision, tirs à la tête, dégâts, manche gagnée la plus rapide : les
/// statistiques du GDD § 14, comptées par le serveur dans PlayerMatchState).
///
/// Il s'ouvre une fois que le gagnant a eu le même temps qu'entre deux manches pour narguer son
/// adversaire (RoundManager.EndScreenDelay), après un fondu au noir (GameHud), et se ferme quand le
/// match suivant commence, ou au salon.
///
/// Le vote (2026-10-26, règles de l'utilisateur) : chaque duelliste choisit la revanche ou le salon ; la
/// revanche demande les deux voix, sinon tout le monde revient au salon (au plus tard à la fin de
/// l'écran). Les voix de chacun s'affichent chez tous. Le serveur compte (RoundManager) : cet écran ne
/// fait que demander et montrer. Pas de vote après un forfait.
/// </summary>
[DisallowMultipleComponent]
public class MatchEndScreen : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    [Tooltip("Où le gagnant célèbre, dans l'arène : la position de ses pieds et la direction de son regard. La caméra se place devant lui, de trois quarts.")]
    [SerializeField] private Transform victorySpot;

    [Tooltip("Les célébrations du gagnant (clips Humanoid, Mixamo sur le Y Bot), jouées au hasard, jamais deux fois la même de suite. Vide : une pose provisoire, le poing levé.")]
    [SerializeField] private AnimationClip[] celebrationClips = System.Array.Empty<AnimationClip>();

    /// <summary>L'écran de fin est-il affiché ? (le curseur est alors libre, voir GameHud)</summary>
    public static bool IsShown { get; private set; }

    private VisualElement screen;
    private Label titleLabel;
    private Label subtitleLabel;
    private VisualElement table;
    private Label countdownLabel;
    private Button quitButton;
    private VisualElement voteBlock;
    private VisualElement voteStatus;
    private VisualElement voteButtons;
    private Button rematchButton;
    private Button salonButton;
    private VictoryStage stage;
    private bool shown;

    // Ce que montrent le vote et le compte à rebours : refaits seulement quand cela change, chacun à son rythme (une
    // seconde de moins ne recrée pas les pastilles du vote).
    private int shownVoteKey = int.MinValue;
    private int shownSeconds = int.MinValue;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;
        screen = root.Q<VisualElement>("endScreen");
        titleLabel = root.Q<Label>("endTitle");
        subtitleLabel = root.Q<Label>("endSubtitle");
        table = root.Q<VisualElement>("scoreTable");
        countdownLabel = root.Q<Label>("endCountdown");
        quitButton = root.Q<Button>("endQuitButton");
        voteBlock = root.Q<VisualElement>("endVote");
        voteStatus = root.Q<VisualElement>("endVoteStatus");
        voteButtons = root.Q<VisualElement>("endVoteButtons");
        rematchButton = root.Q<Button>("endRematchButton");
        salonButton = root.Q<Button>("endSalonButton");
        quitButton.clicked += QuitMatch;
        rematchButton.clicked += VoteRematch;
        salonButton.clicked += VoteSalon;
        shown = false;
        shownVoteKey = shownSeconds = int.MinValue;
    }

    private void OnDisable()
    {
        if (quitButton != null) quitButton.clicked -= QuitMatch;
        if (rematchButton != null) rematchButton.clicked -= VoteRematch;
        if (salonButton != null) salonButton.clicked -= VoteSalon;
        IsShown = false;
        if (stage != null) stage.Hide();
    }

    private void OnDestroy()
    {
        if (stage != null) Destroy(stage.gameObject);
    }

    private void Update()
    {
        RoundManager rm = RoundManager.Instance;
        bool show = rm != null && rm.IsSpawned && GameHud.LocalPlayerObject != null
                    && rm.CurrentPhase == RoundManager.Phase.MatchOver && rm.PhaseElapsed >= rm.EndScreenDelay;

        if (show != shown)
        {
            shown = show;
            screen.EnableInClassList("hidden", !show);
            shownVoteKey = shownSeconds = int.MinValue;
            if (show) Fill(rm);
            else if (stage != null) stage.Hide();
        }
        IsShown = show;
        if (!show) return;

        UpdateVote(rm);
        // La célébration suit l'horloge du serveur : la même image chez les deux joueurs.
        if (stage != null) stage.SetTime(rm.PhaseElapsed - rm.EndScreenDelay);
    }

    /// <summary>Les voix des duellistes, les boutons du joueur local et le compte à rebours.</summary>
    private void UpdateVote(RoundManager rm)
    {
        PlayerMatchState local = LocalState();
        int seconds = Mathf.CeilToInt(rm.PhaseTimeRemaining);
        int key = (int)rm.Decision;
        key = key * 31 + (rm.WonByForfeit ? 1 : 0);
        key = key * 31 + (rm.VoteOpen ? 1 : 0);
        key = key * 31 + (rm.NextMatchRotates ? 1 : 0);
        key = key * 31 + (local != null && local.IsMatchPlayer ? 1 : 0);
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state == null || !state.IsMatchPlayer) continue;
            key = key * 31 + (int)state.OwnerClientId * 4 + (int)state.Vote;
            key = key * 31 + state.DisplayName.GetHashCode();
        }
        bool voteChanged = key != shownVoteKey;
        if (!voteChanged && seconds == shownSeconds) return;
        shownVoteKey = key;
        shownSeconds = seconds;

        // Au Tournoi, pas de vote : le salon vient à la fin de l'écran du champion.
        bool vote = !rm.WonByForfeit && !rm.TournamentMode;
        voteBlock.EnableInClassList("hidden", !vote);
        string rematch = rm.NextMatchRotates ? "Match suivant" : "Revanche";
        if (vote && voteChanged)
        {
            voteStatus.Clear();
            foreach (PlayerMatchState state in PlayerMatchState.Spawned)
            {
                if (state == null || !state.IsMatchPlayer) continue;
                voteStatus.Add(VoteChip(state.DisplayName, state.Vote, rematch));
            }
            bool canVote = rm.VoteOpen && local != null && local.IsMatchPlayer;
            voteButtons.EnableInClassList("hidden", !canVote);
            rematchButton.text = rematch;
            PlayerMatchState.MatchVote mine = local != null ? local.Vote : PlayerMatchState.MatchVote.None;
            rematchButton.EnableInClassList("end-vote-button--chosen", mine == PlayerMatchState.MatchVote.Rematch);
            salonButton.EnableInClassList("end-vote-button--chosen", mine == PlayerMatchState.MatchVote.Salon);
        }

        countdownLabel.text = rm.TournamentMode ? $"Retour au salon dans {seconds} s"
                                                : CountdownText(rm.WonByForfeit, rm.Decision, rematch, seconds, rm.TagTeamMode);
    }

    /// <summary>Le texte sous le tableau : ce qui vient, et quand. Au Tag Team (`teams`), la revanche demande la voix de
    /// tous, et un forfait ramène au salon, où l'hôte refait les équipes. Fonction pure.</summary>
    public static string CountdownText(bool forfeit, RoundManager.MatchDecision decided, string rematch, int seconds, bool teams = false)
    {
        if (forfeit) return teams ? $"Retour au salon dans {seconds} s" : $"Nouvelle partie dans {seconds} s";
        return decided switch
        {
            RoundManager.MatchDecision.Rematch => $"{rematch} !",
            RoundManager.MatchDecision.Salon => "Retour au salon…",
            _ => teams ? $"{rematch} si tout le monde la choisit, sinon retour au salon dans {seconds} s"
                       : $"{rematch} si les deux duellistes la choisissent, sinon retour au salon dans {seconds} s",
        };
    }

    private static VisualElement VoteChip(string name, PlayerMatchState.MatchVote vote, string rematch)
    {
        var chip = new VisualElement();
        chip.AddToClassList("end-vote-chip");
        if (vote == PlayerMatchState.MatchVote.Rematch) chip.AddToClassList("end-vote-chip--rematch");
        if (vote == PlayerMatchState.MatchVote.Salon) chip.AddToClassList("end-vote-chip--salon");
        var nameLabel = new Label(name);
        nameLabel.AddToClassList("end-vote-chip__name");
        chip.Add(nameLabel);
        var choice = new Label(vote == PlayerMatchState.MatchVote.Rematch ? rematch
                             : vote == PlayerMatchState.MatchVote.Salon ? "Retour au salon" : "réfléchit…");
        choice.AddToClassList("end-vote-chip__choice");
        chip.Add(choice);
        return chip;
    }

    private static PlayerMatchState LocalState()
    {
        NetworkObject local = GameHud.LocalPlayerObject;
        return local != null && local.TryGetComponent(out PlayerMatchState state) ? state : null;
    }

    private void VoteRematch() => LocalState()?.RequestVote(PlayerMatchState.MatchVote.Rematch);

    private void VoteSalon() => LocalState()?.RequestVote(PlayerMatchState.MatchVote.Salon);

    private void Fill(RoundManager rm)
    {
        if (rm.TournamentMode)
        {
            FillTournament(rm);
            return;
        }
        if (rm.TagTeamMode)
        {
            FillTeams(rm);
            return;
        }
        ulong winner = rm.LastRoundWinner;
        string winnerName = PlayerMatchState.NameOf(winner);
        titleLabel.text = rm.WonByForfeit ? $"{winnerName} a gagné par forfait" : $"{winnerName} a gagné !";

        // Le gagnant d'abord, puis son adversaire : les deux duellistes du match (les spectateurs
        // n'ont pas joué ce match ; les rôles ne tournent qu'à la fin de l'écran).
        var rows = new List<PlayerMatchState>();
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state == null || !state.IsDuelist) continue;
            if (state.OwnerClientId == winner) rows.Insert(0, state);
            else rows.Add(state);
        }

        int loserWins = 0;
        foreach (PlayerMatchState state in rows)
        {
            if (state.OwnerClientId != winner) loserWins = Mathf.Max(loserWins, rm.WinsOf(state.OwnerClientId));
        }
        // Le Gun Game (2026-11-02) n'a pas de manches : la marche de chacun sur l'échelle, et le temps du match.
        bool gunGame = rm.Mode == GameModeId.GunGame;
        bool deathMatch = rm.DeathMatch;
        bool killConfirmed = rm.Mode == GameModeId.KillConfirmed;
        bool zone = rm.CountsPoints;
        int steps = GunGame.StepCount(rm.Ladder);
        subtitleLabel.text = rm.WonByForfeit
            ? "Son adversaire a quitté la partie"
            : gunGame ? "L'échelle finie au gant"
            : zone ? $"Victoire {ZoneMode.Points(rm.WinsOf(winner))} – {ZoneMode.Points(loserWins)}"
            : $"Victoire {rm.WinsOf(winner)} – {loserWins}";

        table.Clear();
        // Au Death match, les éliminations de chacun et le temps du match ; à l'Élimination confirmée, ses éliminations
        // confirmées.
        // À la Zone, les points de chacun.
        string score = gunGame ? "MARCHE" : deathMatch ? "ÉLIMINATIONS" : killConfirmed ? "CONFIRMÉES" : zone ? "POINTS" : "MANCHES";
        table.Add(Row(true, false, "JOUEUR", score, "PRÉCISION", "TÊTE", "DÉGÂTS",
                      gunGame || deathMatch || killConfirmed || zone ? "TEMPS" : "MANCHE LA PLUS RAPIDE"));
        foreach (PlayerMatchState state in rows)
        {
            PlayerMatchState.MatchStats s = state.Stats;
            table.Add(Row(false, state.OwnerClientId == winner,
                state.DisplayName,
                gunGame ? $"{state.GunGameStep + 1}/{steps}" : zone ? ZoneMode.Points(rm.WinsOf(state.OwnerClientId)) : rm.WinsOf(state.OwnerClientId).ToString(),
                s.shotsFired > 0 ? $"{Mathf.RoundToInt(s.Accuracy * 100f)} %" : "—",
                s.headshots.ToString(),
                Mathf.RoundToInt(s.damageDealt).ToString(),
                s.fastestRoundWin > 0f ? GameHud.FormatTime(s.fastestRoundWin) : "—"));
        }

        ShowWinner(winner, CelebrationSeed(rm.PhaseStartedAt));
    }

    /// <summary>Le Tag Team (GDD § 11) : l'équipe gagnante, ses points contre ceux de l'autre, et tous les joueurs des
    /// équipes, la gagnante d'abord, chacun avec ses points et son équipe. Le robot qui a porté le dernier coup célèbre.</summary>
    private void FillTeams(RoundManager rm)
    {
        PlayerTeam won = rm.WinningTeam;
        PlayerTeam lost = TagTeam.Other(won);
        titleLabel.text = rm.WonByForfeit ? $"L'équipe {TagTeam.Letter(won)} a gagné par forfait" : $"L'équipe {TagTeam.Letter(won)} a gagné !";
        subtitleLabel.text = rm.WonByForfeit ? "L'autre équipe a quitté la partie" : $"Victoire {rm.TeamPoints(won)} – {rm.TeamPoints(lost)}";

        var rows = new List<PlayerMatchState>();
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && state.Team != PlayerTeam.None) rows.Add(state);
        }
        rows.Sort((a, b) => a.Team != b.Team ? (a.Team == won ? -1 : b.Team == won ? 1 : 0)
                                             : rm.WinsOf(b.OwnerClientId).CompareTo(rm.WinsOf(a.OwnerClientId)));
        table.Clear();
        table.Add(Row(true, false, "JOUEUR", "POINTS", "PRÉCISION", "TÊTE", "DÉGÂTS", "ÉQUIPE"));
        foreach (PlayerMatchState state in rows)
        {
            PlayerMatchState.MatchStats s = state.Stats;
            table.Add(Row(false, state.Team == won,
                state.DisplayName,
                rm.WinsOf(state.OwnerClientId).ToString(),
                s.shotsFired > 0 ? $"{Mathf.RoundToInt(s.Accuracy * 100f)} %" : "—",
                s.headshots.ToString(),
                Mathf.RoundToInt(s.damageDealt).ToString(),
                TagTeam.Letter(state.Team)));
        }
        ShowWinner(rm.LastRoundWinner, CelebrationSeed(rm.PhaseStartedAt));
    }

    /// <summary>Le Tournoi (GDD § 11, TO6) : le champion, sa finale, et tous les joueurs du tournoi, le champion puis son
    /// adversaire de la finale en tête, les autres dans l'ordre de la poule, chacun avec ses matchs, ses manches (gagnées
    /// moins perdues) et ses statistiques de tout le tournoi. Le champion célèbre.</summary>
    private void FillTournament(RoundManager rm)
    {
        ulong champion = rm.LastRoundWinner;
        titleLabel.text = $"{TournamentBoard.NameOf(champion)} remporte le tournoi !";
        var matches = new List<Tournament.Match>();
        var players = new List<ulong>();
        var left = new HashSet<ulong>();
        TournamentBoard.CollectMatches(rm, matches);
        TournamentBoard.CollectPlayers(rm, players);
        TournamentBoard.CollectLeft(rm, players, left);
        subtitleLabel.text = TournamentBoard.ChampionLine(champion, matches, rm.WonByForfeit, TournamentBoard.NameOf);

        List<Tournament.Standing> standings = Tournament.Standings(players, matches, left);
        TournamentBoard.ChampionOrder(champion, matches, standings);
        table.Clear();
        table.Add(Row(true, false, "JOUEUR", "MATCHS", "MANCHES", "PRÉCISION", "TÊTE", "DÉGÂTS"));
        foreach (Tournament.Standing row in standings)
        {
            PlayerMatchState state = PlayerMatchState.Of(row.id);
            PlayerMatchState.MatchStats s = state != null ? state.Stats : default;
            bool known = state != null;
            table.Add(Row(false, row.id == champion,
                TournamentBoard.NameOf(row.id),
                TournamentBoard.Record(row),
                TournamentBoard.RoundDifference(row.RoundDifference),
                known && s.shotsFired > 0 ? $"{Mathf.RoundToInt(s.Accuracy * 100f)} %" : "—",
                known ? s.headshots.ToString() : "—",
                known ? Mathf.RoundToInt(s.damageDealt).ToString() : "—"));
        }
        ShowWinner(champion, CelebrationSeed(rm.PhaseStartedAt));
    }

    /// <summary>La graine des célébrations : l'instant où le match s'est fini, publié par le serveur,
    /// donc le même sur les deux machines, et différent à chaque match.</summary>
    public static int CelebrationSeed(double matchOverAt)
    {
        long bits = System.BitConverter.DoubleToInt64Bits(matchOverAt);
        return unchecked((int)bits ^ (int)(bits >> 32));
    }

    private static VisualElement Row(bool header, bool winner, string name, params string[] cells)
    {
        var row = new VisualElement();
        row.AddToClassList("score-row");
        if (header) row.AddToClassList("score-row--header");
        if (winner) row.AddToClassList("score-row--winner");

        var nameLabel = new Label(name);
        nameLabel.AddToClassList("score-cell");
        nameLabel.AddToClassList("score-cell--name");
        row.Add(nameLabel);
        foreach (string cell in cells)
        {
            var label = new Label(cell);
            label.AddToClassList("score-cell");
            row.Add(label);
        }
        return row;
    }

    /// <summary>Le robot du gagnant, à sa couleur, sur son point de l'arène.</summary>
    private void ShowWinner(ulong winner, int seed)
    {
        NetworkManager nm = NetworkManager.Singleton;
        GameObject playerPrefab = nm != null ? nm.NetworkConfig.PlayerPrefab : null;
        Color32 paint = default;
        ChassisId chassis = ChassisId.CT01;
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player == null || player.OwnerClientId != winner) continue;
            if (player.TryGetComponent(out PlayerPaint playerPaint)) paint = playerPaint.Paint;
            if (player.TryGetComponent(out PlayerChassis playerChassis)) chassis = playerChassis.Value;
        }

        if (playerPrefab == null || victorySpot == null)
        {
            if (stage != null) stage.Hide();
            return;
        }
        if (stage == null) stage = VictoryStage.Create();
        stage.Show(victorySpot, playerPrefab, chassis, paint, celebrationClips, seed, GameHud.LocalPlayerCamera);
    }

    private void QuitMatch()
    {
        SessionFlow.ReturnToMenu("");
    }
}
