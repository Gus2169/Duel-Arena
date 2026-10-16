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
/// match suivant commence. Catégorie C pure.
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
    private VictoryStage stage;
    private bool shown;

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
        quitButton.clicked += QuitMatch;
        shown = false;
    }

    private void OnDisable()
    {
        if (quitButton != null) quitButton.clicked -= QuitMatch;
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

        countdownLabel.text = $"Nouvelle partie dans {Mathf.CeilToInt(rm.PhaseTimeRemaining)} s";
        // La célébration suit l'horloge du serveur : la même image chez les deux joueurs.
        if (stage != null) stage.SetTime(rm.PhaseElapsed - rm.EndScreenDelay);
    }

    private void Fill(RoundManager rm)
    {
        ulong winner = rm.LastRoundWinner;
        string winnerName = PlayerMatchState.NameOf(winner);
        titleLabel.text = $"{winnerName} a gagné !";

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
        subtitleLabel.text = $"Victoire {rm.WinsOf(winner)} – {loserWins}";

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
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player == null || player.OwnerClientId != winner) continue;
            if (player.TryGetComponent(out PlayerPaint playerPaint)) paint = playerPaint.Paint;
        }

        if (playerPrefab == null || victorySpot == null)
        {
            if (stage != null) stage.Hide();
            return;
        }
        if (stage == null) stage = VictoryStage.Create();
        stage.Show(victorySpot, playerPrefab, paint, celebrationClips, seed);
    }

    private void QuitMatch()
    {
        SessionFlow.ReturnToMenu("");
    }
}
