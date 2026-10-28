using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Le HUD de la partie, en UI Toolkit (2026-10-12), sur /GameUI de MultiTestScene : la barre de
/// score (pseudos, manches en cercles, chrono de la manche, GDD § 15), la vie (le chiffre et une
/// jauge façon Rainbow Six), les messages de manche (décompte, « Untel a gagné la manche ») et le
/// fondu au noir entre deux manches et autour de l'écran de fin (GDD § 5). Il remplace l'OnGUI
/// provisoire de RoundManager.
///
/// Il est aussi l'ARBITRE du curseur et des commandes : quand un écran a la main (préparation,
/// menu d'Échap, écran de fin), le curseur est libre et les commandes du joueur sont coupées
/// (seules les touches d'arme restent pendant la préparation).
///
/// Catégorie C pure : il lit l'état publié par le serveur (RoundManager, PlayerMatchState, Health)
/// et ne décide rien.
/// </summary>
[DisallowMultipleComponent]
public class GameHud : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    [Header("Fondu au noir (ressenti)")]
    [Tooltip("Le noir commence à tomber ce temps (s) avant la fin de la manche.")]
    [SerializeField] private float fadeOutLead = 0.9f;
    [Tooltip("Noir complet pendant les dernières secondes de la manche et les premières de la préparation : le replacement des joueurs n'est jamais vu (GDD § 5), même avec un peu de latence.")]
    [SerializeField] private float fadeHold = 0.3f;
    [Tooltip("Retour de la lumière au début de la préparation (s).")]
    [SerializeField] private float fadeInTime = 0.45f;

    [Header("Jauge de vie (ressenti)")]
    [Tooltip("Temps (s) pendant lequel la part de vie perdue reste rouge avant de se vider.")]
    [SerializeField] private float healthTrailHold = 0.45f;
    [Tooltip("Vitesse (PV/s) à laquelle la part rouge rejoint la vie restante.")]
    [SerializeField] private float healthTrailSpeed = 90f;

    /// <summary>Un écran couvre le jeu ou le noir est tombé : le réticule (CrosshairUI, OnGUI) se tait.</summary>
    public static bool HidesCrosshair { get; private set; }

    private VisualElement scoreBar;
    private Label leftName;
    private Label rightName;
    private VisualElement leftPips;
    private VisualElement rightPips;
    private Label roundTimer;
    private VisualElement healthPanel;
    private Label healthValue;
    private VisualElement healthFill;
    private VisualElement healthTrail;
    private VisualElement centerMessage;
    private Label centerBig;
    private Label centerSmall;
    private VisualElement fade;
    private int pipCount = -1;

    // La jauge : la vie montrée par la part rouge, et l'instant du dernier coup encaissé.
    private float shownTrail = -1f;
    private float lastHealth = -1f;
    private float lastDamageAt;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;
        scoreBar = root.Q<VisualElement>("scoreBar");
        leftName = root.Q<Label>("leftName");
        rightName = root.Q<Label>("rightName");
        leftPips = root.Q<VisualElement>("leftPips");
        rightPips = root.Q<VisualElement>("rightPips");
        roundTimer = root.Q<Label>("roundTimer");
        healthPanel = root.Q<VisualElement>("healthPanel");
        healthValue = root.Q<Label>("healthValue");
        healthFill = root.Q<VisualElement>("healthFill");
        healthTrail = root.Q<VisualElement>("healthTrail");
        centerMessage = root.Q<VisualElement>("centerMessage");
        centerBig = root.Q<Label>("centerBig");
        centerSmall = root.Q<Label>("centerSmall");
        fade = root.Q<VisualElement>("fade");
        pipCount = -1;
        // Les éléments viennent d'être retrouvés : leurs textes sont à refaire.
        shownTimerSeconds = int.MinValue;
        shownHealth = int.MinValue;
        shownCenter = default;
        centerShownOnce = false;
    }

    private void OnDisable()
    {
        HidesCrosshair = false;
    }

    /// <summary>Le robot de cette machine, ou null (pas de session, pas encore apparu).</summary>
    public static NetworkObject LocalPlayerObject
    {
        get
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || !nm.IsClient || nm.LocalClient == null) return null;
            return nm.LocalClient.PlayerObject;
        }
    }

    private void LateUpdate()
    {
        RoundManager rm = RoundManager.Instance;
        NetworkObject local = LocalPlayerObject;
        bool inGame = local != null && rm != null && rm.IsSpawned;
        // L'écran de fin a toute la place : ni score, ni vie par-dessus le gagnant ; le salon non plus.
        bool endScreen = MatchEndScreen.IsShown || SalonPanel.IsShown;

        SetVisible(scoreBar, inGame && !endScreen);
        SetVisible(healthPanel, inGame && !endScreen);
        SetVisible(centerMessage, inGame);
        if (!inGame)
        {
            SetFade(0f);
            HidesCrosshair = false;
            return;
        }

        UpdateScoreBar(rm, local.OwnerClientId);
        if (!endScreen) UpdateHealth(local);
        UpdateCenterMessage(rm);
        float black = FadeAlpha(rm);
        SetFade(black);

        // L'arbitre du curseur et des commandes.
        bool menu = PauseMenu.IsOpen || MatchEndScreen.IsShown || SalonPanel.IsShown;
        bool choosing = LoadoutPanel.IsShown;
        bool cursorFree = menu || choosing;
        if (local.TryGetComponent(out PlayerInputReader input))
        {
            input.SetMode(menu || choosing ? PlayerInputReader.Mode.None : PlayerInputReader.Mode.Gameplay);
        }
        UnityEngine.Cursor.lockState = cursorFree ? CursorLockMode.None : CursorLockMode.Locked;
        UnityEngine.Cursor.visible = cursorFree;
        HidesCrosshair = cursorFree || black > 0.5f;
    }

    // ------------------------------------------------------------------
    // Barre de score
    // ------------------------------------------------------------------

    private readonly System.Collections.Generic.List<PlayerMatchState> duelists = new System.Collections.Generic.List<PlayerMatchState>();

    /// <summary>Le joueur local est-il spectateur (sur la tribune) ?</summary>
    public static bool LocalIsSpectator
    {
        get
        {
            NetworkObject local = LocalPlayerObject;
            return local != null && local.TryGetComponent(out PlayerMatchState state) && state.IsSpectator;
        }
    }

    private void UpdateScoreBar(RoundManager rm, ulong me)
    {
        // Les deux côtés de la barre : moi et mon adversaire ; vu de la tribune, les deux duellistes
        // (2026-10-13), chacun à sa couleur, ses manches en cercles neutres.
        RoundManager.GetDuelists(duelists);
        bool spectating = LocalIsSpectator;
        PlayerMatchState left = null, right = null;
        if (spectating)
        {
            if (duelists.Count > 0) left = duelists[0];
            if (duelists.Count > 1) right = duelists[1];
        }
        else
        {
            foreach (PlayerMatchState state in duelists)
            {
                if (state.OwnerClientId == me) left = state;
                else if (right == null) right = state;
            }
        }

        if (pipCount != rm.RoundsToWin) BuildPips(rm.RoundsToWin);

        ulong leftId = left != null ? left.OwnerClientId : me;
        SetVisible(leftName, left != null || !spectating);
        SetVisible(leftPips, left != null || !spectating);
        leftName.text = PlayerMatchState.NameOf(leftId);
        leftName.style.color = PaintOf(leftId);
        FillPips(leftPips, rm.WinsOf(leftId), spectating ? "score-pip--neutral" : "score-pip--won");

        PlayerMatchState opponent = right;
        bool hasOpponent = opponent != null;
        SetVisible(rightName, hasOpponent);
        SetVisible(rightPips, hasOpponent);
        if (hasOpponent)
        {
            rightName.text = opponent.DisplayName;
            rightName.style.color = PaintOf(opponent.OwnerClientId);
            FillPips(rightPips, rm.WinsOf(opponent.OwnerClientId), spectating ? "score-pip--neutral" : "score-pip--lost");
        }

        // Le chrono de la manche : le temps écoulé, pas un compte à rebours (GDD § 5). Hors manche,
        // « VS » sépare les cercles des deux joueurs, qui se lisaient sinon comme une seule rangée.
        // Le texte n'est refait qu'au changement de seconde (il allouait à chaque image).
        bool active = rm.CurrentPhase == RoundManager.Phase.Active;
        SetVisible(roundTimer, hasOpponent || active);
        int seconds = active ? Mathf.Max(0, Mathf.FloorToInt(rm.PhaseElapsed)) : -1;
        if (seconds != shownTimerSeconds)
        {
            shownTimerSeconds = seconds;
            roundTimer.text = active ? FormatTime(seconds) : "VS";
        }
    }

    private int shownTimerSeconds = int.MinValue;

    private void BuildPips(int count)
    {
        pipCount = count;
        leftPips.Clear();
        rightPips.Clear();
        for (int i = 0; i < count; i++)
        {
            leftPips.Add(NewPip());
            rightPips.Add(NewPip());
        }
    }

    private static VisualElement NewPip()
    {
        var pip = new VisualElement { pickingMode = PickingMode.Ignore };
        pip.AddToClassList("score-pip");
        return pip;
    }

    private static void FillPips(VisualElement row, int wins, string filledClass)
    {
        for (int i = 0; i < row.childCount; i++)
        {
            row[i].EnableInClassList("score-pip--won", filledClass == "score-pip--won" && i < wins);
            row[i].EnableInClassList("score-pip--lost", filledClass == "score-pip--lost" && i < wins);
            row[i].EnableInClassList("score-pip--neutral", filledClass == "score-pip--neutral" && i < wins);
        }
    }

    private static Color PaintOf(ulong clientId)
    {
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player == null || player.OwnerClientId != clientId) continue;
            if (player.TryGetComponent(out PlayerPaint paint) && paint.Paint.a > 0)
                return Color.Lerp(paint.Paint, Color.white, 0.25f);
        }
        return new Color(0.94f, 0.95f, 0.96f);
    }

    // ------------------------------------------------------------------
    // Vie
    // ------------------------------------------------------------------

    /// <summary>
    /// La vie, comme Rainbow Six (demandé par l'utilisateur, 2026-10-12) : le chiffre, et une jauge
    /// dont la part perdue reste rouge un instant avant de se vider, pour voir ce que le coup a coûté.
    /// Rouge à 30 PV et moins. Cachée une fois mort.
    /// </summary>
    private void UpdateHealth(NetworkObject local)
    {
        // Un spectateur n'a pas de vie à perdre : rien à montrer (2026-10-13).
        if (!local.TryGetComponent(out Health health) || LocalIsSpectator)
        {
            SetVisible(healthPanel, false);
            return;
        }
        bool alive = !health.IsDead;
        SetVisible(healthPanel, alive);
        float current = Mathf.Max(0f, health.Current);
        if (current < lastHealth) lastDamageAt = Time.time;
        lastHealth = current;
        if (!alive) return;

        float max = Mathf.Max(1f, health.Max);
        shownTrail = shownTrail < 0f ? current
            : StepHealthTrail(shownTrail, current, Time.time - lastDamageAt, Time.deltaTime, healthTrailHold, healthTrailSpeed);
        int hp = Mathf.CeilToInt(current);
        if (hp != shownHealth)
        {
            shownHealth = hp;
            healthValue.text = hp.ToString();
        }
        healthFill.style.width = Length.Percent(100f * Mathf.Clamp01(current / max));
        healthTrail.style.width = Length.Percent(100f * Mathf.Clamp01(shownTrail / max));
        healthPanel.EnableInClassList("health-panel--low", hp <= 30);
    }

    private int shownHealth = int.MinValue;

    /// <summary>
    /// La part rouge de la jauge : elle garde la vie d'avant le coup pendant `hold` secondes, puis
    /// rejoint la vie restante à `speed` PV/s ; un soin (nouvelle manche) la rattrape aussitôt.
    /// Fonction pure, testée.
    /// </summary>
    public static float StepHealthTrail(float trail, float current, float sinceDamage, float deltaTime, float hold, float speed)
    {
        if (current >= trail) return current;
        if (sinceDamage < hold) return trail;
        return Mathf.MoveTowards(trail, current, speed * deltaTime);
    }

    // ------------------------------------------------------------------
    // Messages au centre
    // ------------------------------------------------------------------

    /// <summary>Ce dont dépend le message du centre : il n'est refait que quand cela change (ses
    /// chaînes allouaient à chaque image). En attente, une fois par seconde (le code de la partie).</summary>
    private struct CenterKey : System.IEquatable<CenterKey>
    {
        public RoundManager.Phase phase;
        public int value;
        public int round;
        public ulong winner;
        public int wins;
        public bool spectator;
        public bool endScreen;
        public bool forfeit;

        public bool Equals(CenterKey o) => phase == o.phase && value == o.value && round == o.round && winner == o.winner
                                           && wins == o.wins && spectator == o.spectator && endScreen == o.endScreen
                                           && forfeit == o.forfeit;
    }

    private CenterKey shownCenter;
    private bool centerShownOnce;

    private void UpdateCenterMessage(RoundManager rm)
    {
        RoundManager.Phase phase = rm.CurrentPhase;
        int wins = 0;
        foreach (PlayerMatchState state in duelists) wins = wins * 8 + rm.WinsOf(state.OwnerClientId);
        var key = new CenterKey
        {
            phase = phase,
            value = phase == RoundManager.Phase.Starting ? Mathf.Max(1, Mathf.CeilToInt(rm.PhaseTimeRemaining))
                  : phase == RoundManager.Phase.WaitingForPlayers ? Mathf.FloorToInt(Time.unscaledTime) : 0,
            round = rm.RoundNumber,
            winner = rm.LastRoundWinner,
            wins = wins,
            spectator = LocalIsSpectator,
            endScreen = MatchEndScreen.IsShown,
            forfeit = rm.WonByForfeit,
        };
        if (centerShownOnce && key.Equals(shownCenter)) return;
        centerShownOnce = true;
        shownCenter = key;

        string big = null;
        string small = null;
        bool sentence = false;

        switch (rm.CurrentPhase)
        {
            case RoundManager.Phase.WaitingForPlayers:
                small = LocalIsSpectator ? "En attente des duellistes..." : "En attente d'un adversaire...";
                string invite = InviteLine();
                if (invite != null) small += "\n" + invite;
                break;

            case RoundManager.Phase.Starting:
                // CeilToInt pour afficher 3, 2, 1 plutôt que 2, 1, 0 : on veut voir le dernier
                // chiffre pendant toute sa seconde.
                big = Mathf.Max(1, Mathf.CeilToInt(rm.PhaseTimeRemaining)).ToString();
                small = $"Manche {rm.RoundNumber}";
                break;

            case RoundManager.Phase.RoundOver:
                sentence = true;
                big = rm.LastRoundWinner == RoundManager.NoWinner
                    ? "Manche nulle"
                    : $"{PlayerMatchState.NameOf(rm.LastRoundWinner)} a gagné la manche";
                small = ScoreLine(rm);
                break;

            case RoundManager.Phase.MatchOver:
                if (MatchEndScreen.IsShown) break;
                sentence = true;
                if (rm.WonByForfeit)
                {
                    // L'adversaire a quitté la partie (GDD § 4, 2026-10-25).
                    big = $"{PlayerMatchState.NameOf(rm.LastRoundWinner)} remporte le match par forfait";
                    small = "Son adversaire a quitté la partie.";
                    break;
                }
                big = $"{PlayerMatchState.NameOf(rm.LastRoundWinner)} remporte le match !";
                small = ScoreLine(rm);
                break;
        }

        centerBig.text = big ?? "";
        centerSmall.text = small ?? "";
        SetVisible(centerBig, big != null);
        SetVisible(centerSmall, small != null);
        centerBig.EnableInClassList("center-big--sentence", sentence);
    }

    /// <summary>Le score, du point de vue du gagnant de la manche : « 2 – 1 ». Seuls les duellistes
    /// comptent (2026-10-13).</summary>
    private static string ScoreLine(RoundManager rm)
    {
        var duelistStates = new System.Collections.Generic.List<PlayerMatchState>();
        RoundManager.GetDuelists(duelistStates);
        ulong first = rm.LastRoundWinner;
        if (first == RoundManager.NoWinner)
        {
            NetworkObject local = LocalPlayerObject;
            if (local == null) return null;
            first = !LocalIsSpectator ? local.OwnerClientId : duelistStates.Count > 0 ? duelistStates[0].OwnerClientId : local.OwnerClientId;
        }
        int theirs = 0;
        foreach (PlayerMatchState state in duelistStates)
        {
            if (state.OwnerClientId != first) theirs = rm.WinsOf(state.OwnerClientId);
        }
        return $"{rm.WinsOf(first)} – {theirs}";
    }

    /// <summary>Ce qu'il faut donner aux autres pour venir : le code de la partie en ligne (chez tout le
    /// monde, chacun peut inviter), sinon l'adresse de l'hôte en réseau local (chez l'hôte seulement).</summary>
    public static string InviteLine()
    {
        if (OnlineSession.InSession && !string.IsNullOrEmpty(OnlineSession.Code)) return $"Code de la partie : {OnlineSession.Code}";
        string address = HostAddress();
        return address != null ? $"Donne cette adresse aux autres joueurs : {address}" : null;
    }

    /// <summary>L'adresse à donner à son adversaire, chez l'hôte seulement (le menu la montrait,
    /// mais l'arène se charge aussitôt l'hébergement lancé).</summary>
    private static string HostAddress()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsHost || !nm.TryGetComponent(out UnityTransport transport)) return null;
        return $"{transport.ConnectionData.Address}:{transport.ConnectionData.Port}";
    }

    // ------------------------------------------------------------------
    // Fondu au noir
    // ------------------------------------------------------------------

    /// <summary>
    /// L'opacité du noir : il tombe à la fin d'une manche (et avant l'écran de fin), reste complet
    /// pendant le replacement des joueurs, puis se lève au début de la préparation. Calé sur les
    /// instants que publie le serveur, pas sur l'arrivée des messages : chez un client, l'horloge du
    /// serveur retarde un peu sur ce qu'il reçoit, le noir est donc complet avant que le replacement
    /// n'arrive à l'écran.
    /// </summary>
    private float FadeAlpha(RoundManager rm)
    {
        switch (rm.CurrentPhase)
        {
            case RoundManager.Phase.RoundOver:
                return FadeOut(rm.PhaseTimeRemaining);
            case RoundManager.Phase.MatchOver:
                // Le noir tombe sur le gagnant qui nargue, se lève sur l'écran de fin, et retombe
                // avant le match suivant.
                float untilEndScreen = rm.EndScreenDelay - rm.PhaseElapsed;
                if (untilEndScreen > 0f) return FadeOut(untilEndScreen);
                float sinceEndScreen = -untilEndScreen;
                float lightComing = 1f - Mathf.Clamp01((sinceEndScreen - fadeHold) / Mathf.Max(0.01f, fadeInTime));
                return Mathf.Max(lightComing, FadeOut(rm.PhaseTimeRemaining));
            case RoundManager.Phase.Preparation:
            case RoundManager.Phase.Salon:
                // La lumière revient sur la préparation, et sur le salon après le noir de l'écran de fin.
                return 1f - Mathf.Clamp01((rm.PhaseElapsed - fadeHold) / Mathf.Max(0.01f, fadeInTime));
            default:
                return 0f;
        }
    }

    private float FadeOut(float untilEnd)
        => 1f - Mathf.Clamp01((untilEnd - fadeHold) / Mathf.Max(0.01f, fadeOutLead - fadeHold));

    private void SetFade(float alpha)
    {
        if (fade == null) return;
        fade.style.opacity = alpha;
        fade.style.display = alpha > 0.001f ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ------------------------------------------------------------------

    public static string FormatTime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{total / 60}:{total % 60:00}";
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
