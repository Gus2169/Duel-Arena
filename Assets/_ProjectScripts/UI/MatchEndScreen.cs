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

    // Ce que montrent le vote et le compte à rebours : refaits seulement quand cela change.
    private int shownVoteKey = int.MinValue;

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
        shownVoteKey = int.MinValue;
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
        int key = seconds;
        key = key * 31 + (int)rm.Decision;
        key = key * 31 + (rm.WonByForfeit ? 1 : 0);
        key = key * 31 + (rm.VoteOpen ? 1 : 0);
        key = key * 31 + (rm.NextMatchRotates ? 1 : 0);
        key = key * 31 + (local != null && local.IsDuelist ? 1 : 0);
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && state.IsDuelist) key = key * 31 + (int)state.OwnerClientId * 4 + (int)state.Vote;
        }
        if (key == shownVoteKey) return;
        shownVoteKey = key;

        bool vote = !rm.WonByForfeit;
        voteBlock.EnableInClassList("hidden", !vote);
        string rematch = rm.NextMatchRotates ? "Match suivant" : "Revanche";
        if (vote)
        {
            voteStatus.Clear();
            foreach (PlayerMatchState state in PlayerMatchState.Spawned)
            {
                if (state == null || !state.IsDuelist) continue;
                voteStatus.Add(VoteChip(state.DisplayName, state.Vote, rematch));
            }
            bool canVote = rm.VoteOpen && local != null && local.IsDuelist;
            voteButtons.EnableInClassList("hidden", !canVote);
            rematchButton.text = rematch;
            PlayerMatchState.MatchVote mine = local != null ? local.Vote : PlayerMatchState.MatchVote.None;
            rematchButton.EnableInClassList("end-vote-button--chosen", mine == PlayerMatchState.MatchVote.Rematch);
            salonButton.EnableInClassList("end-vote-button--chosen", mine == PlayerMatchState.MatchVote.Salon);
        }

        countdownLabel.text = CountdownText(rm.WonByForfeit, rm.Decision, rematch, seconds);
    }

    /// <summary>Le texte sous le tableau : ce qui vient, et quand. Fonction pure.</summary>
    public static string CountdownText(bool forfeit, RoundManager.MatchDecision decided, string rematch, int seconds)
    {
        if (forfeit) return $"Nouvelle partie dans {seconds} s";
        return decided switch
        {
            RoundManager.MatchDecision.Rematch => $"{rematch} !",
            RoundManager.MatchDecision.Salon => "Retour au salon…",
            _ => $"{rematch} si les deux duellistes la choisissent, sinon retour au salon dans {seconds} s",
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
        subtitleLabel.text = rm.WonByForfeit
            ? "Son adversaire a quitté la partie"
            : $"Victoire {rm.WinsOf(winner)} – {loserWins}";

        table.Clear();
        table.Add(Row(true, false, "JOUEUR", "MANCHES", "PRÉCISION", "TÊTE", "DÉGÂTS", "MANCHE LA PLUS RAPIDE"));
        foreach (PlayerMatchState state in rows)
        {
            PlayerMatchState.MatchStats s = state.Stats;
            table.Add(Row(false, state.OwnerClientId == winner,
                state.DisplayName,
                rm.WinsOf(state.OwnerClientId).ToString(),
                s.shotsFired > 0 ? $"{Mathf.RoundToInt(s.Accuracy * 100f)} %" : "—",
                s.headshots.ToString(),
                Mathf.RoundToInt(s.damageDealt).ToString(),
                s.fastestRoundWin > 0f ? GameHud.FormatTime(s.fastestRoundWin) : "—"));
        }

        ShowWinner(winner, CelebrationSeed(rm.PhaseStartedAt));
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
        stage.Show(victorySpot, playerPrefab, chassis, paint, celebrationClips, seed);
    }

    private void QuitMatch()
    {
        SessionFlow.ReturnToMenu("");
    }
}
