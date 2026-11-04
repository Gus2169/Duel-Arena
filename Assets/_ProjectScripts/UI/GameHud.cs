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
/// Au Gun Game (2026-11-02, GDD § 11, GG7) : l'échelle des deux duellistes à la place des manches, l'arme de la
/// marche de chacun, l'annonce de la dernière marche (le gant), une marche perdue au gant, et le décompte du
/// joueur mort jusqu'à sa réapparition.
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

    /// <summary>Un écran couvre le jeu ou le noir est tombé : le HUD de combat (CrosshairUI) se tait.</summary>
    public static bool HidesCrosshair { get; private set; }

    private VisualElement scoreBar;
    private Label leftName;
    private Label rightName;
    private VisualElement leftPips;
    private VisualElement rightPips;
    private Label leftWeapon;
    private Label rightWeapon;
    private Label leftPoints;
    private Label rightPoints;
    private int shownLeftPoints = -1, shownRightPoints = -1;
    private Label zoneStatus;
    private VisualElement zoneMarker;
    private VisualElement zoneMarkerIcon;
    private VisualElement zoneMarkerHalo;
    private Label zoneMarkerLabel;
    private VisualElement nextZoneMarker;
    private VisualElement nextZoneMarkerIcon;
    private VisualElement nextZoneMarkerHalo;
    private Label nextZoneMarkerLabel;
    private VisualElement zoneMarkerAnchor;
    private VisualElement nextZoneMarkerAnchor;
    private bool zoneMarkersUp;
    private VisualElement leftGauge;
    private VisualElement leftGaugeFill;
    private VisualElement rightGauge;
    private VisualElement rightGaugeFill;
    private Label scoreGoal;
    private bool pipsAreSteps;
    private bool pipsCompact;
    private int shownLeftWeapon = -1, shownRightWeapon = -1;
    private Label roundTimer;
    private VisualElement healthPanel;
    private Label healthValue;
    private VisualElement healthFill;
    private VisualElement healthTrail;
    private VisualElement centerMessage;
    private VisualElement killCamCaption;
    private Label killCamLine;
    private string shownKillCamLine;
    private Label centerBig;
    private Label centerSmall;
    private VisualElement fade;
    private VisualElement loadingBlock;
    private Label loadingTitle;
    private VisualElement loadingSpinner;
    private string shownLoadingTitle;
    private int pipCount = -1;

    // Le robot de cette machine et ses composants, cherchés quand il change, pas à chaque image.
    private NetworkObject followed;
    private PlayerMatchState localState;
    private PlayerInputReader localInput;
    private Health localHealth;

    // L'arrivée dans l'arène (2026-11-03) : le temps compté depuis que la partie est prête sur cette machine
    // (StepArrival), -1 tant qu'elle ne l'est pas. Le noir du chargement se lève à partir de là (ArrivalBlack).
    private float readyFor = -1f;
    // Cette machine a été dans la partie, dans cette arène : la perdre ensuite, c'est s'en aller (le noir reste).
    private bool wasInGame;

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
        leftWeapon = root.Q<Label>("leftWeapon");
        rightWeapon = root.Q<Label>("rightWeapon");
        shownLeftWeapon = shownRightWeapon = -1;
        leftPoints = root.Q<Label>("leftPoints");
        rightPoints = root.Q<Label>("rightPoints");
        shownLeftPoints = shownRightPoints = -1;
        zoneStatus = root.Q<Label>("zoneStatus");
        zoneMarker = root.Q<VisualElement>("zoneMarker");
        zoneMarkerIcon = root.Q<VisualElement>("zoneMarkerIcon");
        zoneMarkerHalo = root.Q<VisualElement>("zoneMarkerHalo");
        zoneMarkerLabel = root.Q<Label>("zoneMarkerLabel");
        nextZoneMarker = root.Q<VisualElement>("nextZoneMarker");
        nextZoneMarkerIcon = root.Q<VisualElement>("nextZoneMarkerIcon");
        nextZoneMarkerHalo = root.Q<VisualElement>("nextZoneMarkerHalo");
        nextZoneMarkerLabel = root.Q<Label>("nextZoneMarkerLabel");
        zoneMarkerAnchor = MarkerAnchor(zoneMarker);
        nextZoneMarkerAnchor = MarkerAnchor(nextZoneMarker);
        zoneMarkersUp = false;
        leftGauge = root.Q<VisualElement>("leftGauge");
        leftGaugeFill = root.Q<VisualElement>("leftGaugeFill");
        rightGauge = root.Q<VisualElement>("rightGauge");
        rightGaugeFill = root.Q<VisualElement>("rightGaugeFill");
        scoreGoal = root.Q<Label>("scoreGoal");
        leftGaugeShown = rightGaugeShown = default;
        shownScoreGoal = -1;
        barWritten = false;
        statusWritten = false;
        zoneMarkerShown = nextMarkerShown = default;
        roundTimer = root.Q<Label>("roundTimer");
        healthPanel = root.Q<VisualElement>("healthPanel");
        healthValue = root.Q<Label>("healthValue");
        healthFill = root.Q<VisualElement>("healthFill");
        healthTrail = root.Q<VisualElement>("healthTrail");
        centerMessage = root.Q<VisualElement>("centerMessage");
        centerBig = root.Q<Label>("centerBig");
        centerSmall = root.Q<Label>("centerSmall");
        killCamCaption = root.Q<VisualElement>("killCamCaption");
        killCamLine = root.Q<Label>("killCamLine");
        shownKillCamLine = null;
        fade = root.Q<VisualElement>("fade");
        loadingBlock = root.Q<VisualElement>("loadingBlock");
        loadingTitle = root.Q<Label>("loadingTitle");
        loadingSpinner = loadingBlock != null ? loadingBlock.Q<DuelArena.UI.Spinner>() : null;
        shownLoadingTitle = null;
        trackedMode = null;
        Follow(null);
        readyFor = -1f;
        wasInGame = false;
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

    /// <summary>La caméra du robot de cette machine, ou null. Camera.main n'est pas toujours la sienne : sous la kill cam
    /// et l'écran de fin, elle est éteinte (KillCam.CoveredCamera).</summary>
    public static Camera LocalPlayerCamera
    {
        get
        {
            NetworkObject local = LocalPlayerObject;
            return local != null && local.TryGetComponent(out FirstPersonView view) ? view.PlayerCamera : null;
        }
    }

    private void Follow(NetworkObject local)
    {
        followed = local;
        localState = local != null ? local.GetComponent<PlayerMatchState>() : null;
        localInput = local != null ? local.GetComponent<PlayerInputReader>() : null;
        localHealth = local != null ? local.GetComponent<Health>() : null;
    }

    private void LateUpdate()
    {
        // Le match du joueur de cette machine : au Tournoi, celui de son couloir, ou la salle d'attente (le RoundManager
        // de la scène, qui tient aussi le tournoi).
        RoundManager rm = RoundManager.Local;
        NetworkObject local = LocalPlayerObject;
        if (local != followed) Follow(local);
        // On s'en va (le retour au menu demandé, ou Netcode qui s'arrête : il le finit après cette image et emporte la
        // caméra du joueur avant qu'elle ne soit dessinée) : plus rien de la partie, le noir tout de suite.
        NetworkManager nm = NetworkManager.Singleton;
        bool leaving = SessionFlow.LeavingToMenu || (nm != null && nm.ShutdownInProgress);
        bool inGame = !leaving && local != null && rm != null && rm.IsSpawned;
        // L'écran de fin a toute la place : ni score, ni vie par-dessus le gagnant ; le salon non plus.
        bool endScreen = MatchEndScreen.IsShown || SalonPanel.IsShown;
        // La kill cam a l'écran (KillCam) : le HUD s'efface devant elle, comme sous l'écran de fin.
        bool killCam = inGame && KillCam.Covers;
        bool covered = endScreen || killCam;
        if (inGame) TrackMatchChange(rm);

        // Hors d'un match du Tournoi (la salle d'attente, l'attente des joueurs), pas de barre de score : le tableau du
        // tournoi en tient lieu (TournamentBoard).
        bool barShown = inGame && !covered && !rm.TournamentMode;
        SetVisible(scoreBar, barShown);
        // En jeu, UpdateHealth seul la montre ou non : la montrer ici pour la cacher là referait son style à chaque image
        // chez un spectateur ou un mort.
        if (!inGame || covered) SetVisible(healthPanel, false);
        SetVisible(centerMessage, inGame && !killCam);
        UpdateKillCam(killCam);
        if (!inGame)
        {
            // Pas encore dans la partie (le chargement : son robot ou l'arbitre de la manche pas encore là), ou plus (la
            // session s'arrête, le retour au menu) : le noir, et ce qu'on attend. Sans session (l'arène lancée seule dans
            // l'Editor), rien.
            bool session = wasInGame || leaving || (nm != null && nm.IsListening);
            readyFor = -1f;
            SetFade(session ? 1f : 0f);
            ShowLoading(session, LoadingTitle(wasInGame || leaving, false));
            HidesCrosshair = false;
            return;
        }

        wasInGame = true;
        // Prête quand l'arbitre et son robot sont là, et que les joueurs d'un salon en ligne sont tous arrivés.
        bool gathering = RoundManager.Instance != null && RoundManager.Instance.Gathering;
        bool matchComing = rm.CurrentPhase == RoundManager.Phase.WaitingForPlayers && rm.PhaseHasEnd;
        readyFor = StepArrival(readyFor, !gathering, matchComing, Time.unscaledDeltaTime);
        float arrival = ArrivalBlack(readyFor, fadeHold, fadeInTime);
        ShowLoading(arrival >= 1f, LoadingTitle(false, gathering));

        UpdateScoreBar(rm, local.OwnerClientId, barShown);
        if (!covered) UpdateHealth();
        TrackMode(rm, local.OwnerClientId);
        TrackTournament(RoundManager.Instance, rm, local.OwnerClientId);
        UpdateCenterMessage(rm, local);
        // La ligne sous la barre de score : à la Zone, au Tag Team ou dans un match du Tournoi.
        bool statusShown = UpdateZone(rm, local.OwnerClientId, local.transform.position, covered);
        statusShown |= UpdateTagTeam(rm, covered);
        statusShown |= UpdateTournamentLine(rm, covered);
        SetVisible(zoneStatus, statusShown);
        // Le noir des fins de manche, et celui de l'arrivée.
        float black = Mathf.Max(FadeAlpha(rm), arrival);
        SetFade(black);

        // L'arbitre du curseur et des commandes. Sous le noir de l'arrivée, on ne joue pas à l'aveugle.
        bool menu = PauseMenu.IsOpen || MatchEndScreen.IsShown || SalonPanel.IsShown;
        bool choosing = LoadoutPanel.IsShown;
        bool cursorFree = menu || choosing;
        bool loading = arrival > 0.5f;
        if (localInput != null)
        {
            localInput.SetMode(menu || choosing || loading ? PlayerInputReader.Mode.None : PlayerInputReader.Mode.Gameplay);
        }
        UnityEngine.Cursor.lockState = cursorFree ? CursorLockMode.None : CursorLockMode.Locked;
        UnityEngine.Cursor.visible = cursorFree;
        HidesCrosshair = cursorFree || black > 0.5f || killCam;
    }

    // Le mode de l'image d'avant (null : pas encore vu).
    private GameModeId? trackedMode;

    /// <summary>Les annonces du mode joué. Celles des autres ne tournent qu'une fois, quand le mode change : elles se
    /// remettent à zéro.</summary>
    private void TrackMode(RoundManager rm, ulong me)
    {
        GameModeId mode = rm.Mode;
        bool changed = mode != trackedMode;
        trackedMode = mode;
        if (changed || mode == GameModeId.GunGame) TrackGunGame(rm);
        if (changed || mode == GameModeId.KillConfirmed) TrackKillConfirmed(rm, me);
        if (changed || mode == GameModeId.Zone) TrackZone(rm);
        if (changed || mode == GameModeId.TagTeam) TrackTagTeam(rm);
    }

    /// <summary>La légende de la kill cam, de l'impact à la fin de l'épave (KillCam.CaptionShown).</summary>
    private void UpdateKillCam(bool killCam)
    {
        bool shown = killCam && KillCam.CaptionShown;
        SetVisible(killCamCaption, shown);
        if (!shown || killCamLine == null) return;
        string line = KillCam.CurrentCaption;
        if (line == shownKillCamLine) return;
        shownKillCamLine = line;
        killCamLine.text = line;
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

    /// <summary>Un côté de la barre de score. `score` : ses manches, ses éliminations ou ses points ; sa marche au Gun Game ;
    /// les points de son équipe au Tag Team.</summary>
    public struct ScoreSide : System.IEquatable<ScoreSide>
    {
        public bool shown;
        public ulong id;
        public int score;
        public string name;
        public Color paint;

        public bool Equals(ScoreSide o) => shown == o.shown && id == o.id && score == o.score && name == o.name && paint == o.paint;
    }

    /// <summary>Ce que montre la barre de score : elle n'est refaite que quand cela change, le chrono à part. La peinture et
    /// le pseudo en font partie : ils arrivent après l'apparition du robot.</summary>
    public struct ScoreBarKey : System.IEquatable<ScoreBarKey>
    {
        public bool tagTeam;
        public bool spectating;
        public bool gunGame;
        public bool points;
        public bool compact;
        public GunGameLadder ladder;
        public int target;
        public ScoreSide left;
        public ScoreSide right;

        public bool Equals(ScoreBarKey o) => tagTeam == o.tagTeam && spectating == o.spectating && gunGame == o.gunGame && points == o.points
                                             && compact == o.compact && ladder == o.ladder && target == o.target
                                             && left.Equals(o.left) && right.Equals(o.right);
    }

    private ScoreBarKey shownBar;
    private bool barWritten;

    private void UpdateScoreBar(RoundManager rm, ulong me, bool shown)
    {
        // Les deux côtés de la barre : moi et mon adversaire ; vu de la tribune, les deux duellistes
        // (2026-10-13), chacun à sa couleur, ses manches en cercles neutres. Au Tag Team, les deux équipes.
        // La liste sert aussi aux annonces et au message du centre, barre cachée comprise.
        rm.GetMatchDuelists(duelists);
        if (!shown) return;
        if (rm.TagTeamMode)
        {
            UpdateTagTeamScoreBar(rm);
            return;
        }
        bool spectating = localState != null && localState.IsSpectator;
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

        // Au Gun Game, une marche par arme de l'échelle et le gant, à la place des manches (GDD § 11, GG7).
        // Au Death match et à l'Élimination confirmée, une marque par élimination à faire, plus serrées que les manches.
        // À la Zone, les points de chacun, sans marques.
        bool gunGame = rm.Mode == GameModeId.GunGame;
        ulong leftId = left != null ? left.OwnerClientId : me;
        var bar = new ScoreBarKey
        {
            spectating = spectating,
            gunGame = gunGame,
            points = rm.CountsPoints,
            compact = gunGame || rm.CountsKills,
            ladder = rm.Ladder,
            target = rm.RoundsToWin,
            left = new ScoreSide
            {
                shown = left != null || !spectating,
                id = leftId,
                score = !gunGame ? rm.WinsOf(leftId) : left != null ? left.GunGameStep : 0,
                name = PlayerMatchState.NameOf(leftId),
                paint = PaintOf(leftId),
            },
        };
        if (right != null)
        {
            ulong rightId = right.OwnerClientId;
            bar.right = new ScoreSide
            {
                shown = true,
                id = rightId,
                score = gunGame ? right.GunGameStep : rm.WinsOf(rightId),
                name = right.DisplayName,
                paint = PaintOf(rightId),
            };
        }
        if (!barWritten || !bar.Equals(shownBar)) WriteScoreBar(bar);

        // Le chrono de la manche : le temps écoulé, pas un compte à rebours (GDD § 5). Hors manche,
        // « VS » sépare les cercles des deux joueurs, qui se lisaient sinon comme une seule rangée.
        // Le texte n'est refait qu'au changement de seconde (il allouait à chaque image).
        // À la Zone, le temps qui reste au match, puis la prolongation.
        bool active = rm.CurrentPhase == RoundManager.Phase.Active;
        bool overtime = active && rm.ZoneOvertime;
        SetVisible(roundTimer, bar.right.shown || active);
        roundTimer.EnableInClassList("round-timer--long", overtime);
        int seconds = !active ? -1 : bar.points ? Mathf.CeilToInt(rm.ZoneMatchTimeLeft) : Mathf.Max(0, Mathf.FloorToInt(rm.PhaseElapsed));
        if (seconds != shownTimerSeconds)
        {
            shownTimerSeconds = seconds;
            roundTimer.text = overtime ? "PROLONGATION" : active ? FormatTime(seconds) : "VS";
        }
    }

    private void WriteScoreBar(in ScoreBarKey bar)
    {
        barWritten = true;
        shownBar = bar;
        int pips = bar.gunGame ? GunGame.StepCount(bar.ladder) : bar.points ? 0 : bar.target;
        if (pipCount != pips || pipsAreSteps != bar.gunGame || pipsCompact != bar.compact) BuildPips(pips, bar.gunGame, bar.compact);
        ShowSide(bar, bar.left, leftName, leftPips, bar.spectating ? "score-pip--neutral" : "score-pip--won",
                 leftWeapon, ref shownLeftWeapon, leftPoints, ref shownLeftPoints, leftGauge, leftGaugeFill, ref leftGaugeShown);
        ShowSide(bar, bar.right, rightName, rightPips, bar.spectating ? "score-pip--neutral" : "score-pip--lost",
                 rightWeapon, ref shownRightWeapon, rightPoints, ref shownRightPoints, rightGauge, rightGaugeFill, ref rightGaugeShown);
        SetVisible(scoreGoal, bar.points);
        if (bar.points && bar.target != shownScoreGoal)
        {
            shownScoreGoal = bar.target;
            scoreGoal.text = "OBJECTIF " + ZoneMode.Points(bar.target);
        }
    }

    private static void ShowSide(in ScoreBarKey bar, in ScoreSide side, Label name, VisualElement pips, string filledClass,
                                 Label weapon, ref int shownWeapon, Label points, ref int shownPoints,
                                 VisualElement gauge, VisualElement fill, ref GaugeShown shownGauge)
    {
        SetVisible(name, side.shown);
        SetVisible(pips, side.shown);
        if (side.shown)
        {
            name.text = side.name;
            name.style.color = side.paint;
            if (bar.gunGame) FillSteps(pips, side.score, filledClass);
            else FillPips(pips, side.score, filledClass);
        }
        ShowStepWeapon(weapon, bar.gunGame && side.shown, bar.ladder, side.score, ref shownWeapon);
        ShowPoints(points, bar.points && side.shown, side.score, ref shownPoints);
        ShowGauge(gauge, fill, bar.points && side.shown, side.score, bar.target, side.paint, ref shownGauge);
    }

    private int shownTimerSeconds = int.MinValue;
    private int shownScoreGoal = -1;

    // Ce qu'affiche une barre des points (rien n'est réécrit sans changement).
    private struct GaugeShown
    {
        public int points;
        public Color color;
        public bool written;
    }

    private GaugeShown leftGaugeShown, rightGaugeShown;

    /// <summary>La barre des points d'un duelliste, à la couleur de son robot, pleine au compte à atteindre (`target`).</summary>
    private static void ShowGauge(VisualElement gauge, VisualElement fill, bool visible, int points, int target, Color color, ref GaugeShown shown)
    {
        SetVisible(gauge, visible);
        if (gauge == null || fill == null || !visible) return;
        if (!shown.written || points != shown.points)
        {
            shown.points = points;
            fill.style.width = Length.Percent(100f * GaugeFill(points, target));
        }
        if (!shown.written || color != shown.color)
        {
            shown.color = color;
            fill.style.backgroundColor = color;
        }
        shown.written = true;
    }

    /// <summary>La part remplie d'une barre : `points` sur `target`, sans jamais déborder. Fonction pure, testée.</summary>
    public static float GaugeFill(int points, int target) => target > 0 ? Mathf.Clamp01((float)points / target) : 0f;

    private void BuildPips(int count, bool steps, bool compact)
    {
        pipCount = count;
        pipsAreSteps = steps;
        pipsCompact = compact;
        leftPips.Clear();
        rightPips.Clear();
        for (int i = 0; i < count; i++)
        {
            bool glove = steps && i == count - 1;
            leftPips.Add(NewPip(compact, glove));
            rightPips.Add(NewPip(compact, glove));
        }
        // Les deux échelles montent vers le chrono, le gant au milieu.
        rightPips.EnableInClassList("score-pips--mirror", steps);
    }

    private static VisualElement NewPip(bool step, bool glove)
    {
        var pip = new VisualElement { pickingMode = PickingMode.Ignore };
        pip.AddToClassList("score-pip");
        pip.EnableInClassList("score-pip--step", step);
        pip.EnableInClassList("score-pip--glove", glove);
        return pip;
    }

    /// <summary>Une échelle du Gun Game : les marches passées remplies, celle où l'on est cernée de blanc.</summary>
    private static void FillSteps(VisualElement row, int step, string filledClass)
    {
        for (int i = 0; i < row.childCount; i++)
        {
            bool done = i < step;
            row[i].EnableInClassList("score-pip--won", filledClass == "score-pip--won" && done);
            row[i].EnableInClassList("score-pip--lost", filledClass == "score-pip--lost" && done);
            row[i].EnableInClassList("score-pip--neutral", filledClass == "score-pip--neutral" && done);
            row[i].EnableInClassList("score-pip--current", i == step);
        }
    }

    /// <summary>Les points d'un duelliste, à la Zone, à côté de son nom. Le texte n'est refait qu'au changement.</summary>
    private static void ShowPoints(Label label, bool visible, int points, ref int shown)
    {
        SetVisible(label, visible);
        if (label == null || !visible || points == shown) return;
        shown = points;
        label.text = ZoneMode.Points(points);
    }

    /// <summary>L'arme de la marche d'un duelliste, à côté de son nom. Le texte n'est refait qu'au changement.</summary>
    private static void ShowStepWeapon(Label label, bool visible, GunGameLadder ladder, int step, ref int shown)
    {
        SetVisible(label, visible);
        if (label == null || !visible) return;
        int key = (int)ladder * 256 + step;
        if (key == shown) return;
        shown = key;
        label.text = GunGame.WeaponNameAt(ladder, step).ToUpperInvariant();
    }

    private static void FillPips(VisualElement row, int wins, string filledClass)
    {
        for (int i = 0; i < row.childCount; i++)
        {
            row[i].EnableInClassList("score-pip--won", filledClass == "score-pip--won" && i < wins);
            row[i].EnableInClassList("score-pip--lost", filledClass == "score-pip--lost" && i < wins);
            row[i].EnableInClassList("score-pip--neutral", filledClass == "score-pip--neutral" && i < wins);
            row[i].EnableInClassList("score-pip--current", false);
        }
    }

    // ------------------------------------------------------------------
    // Le Gun Game : les annonces (GDD § 11, GG7)
    // ------------------------------------------------------------------

    // La marche de chaque duelliste à l'image d'avant, pour voir ce qui change.
    private readonly System.Collections.Generic.Dictionary<ulong, int> seenSteps = new System.Collections.Generic.Dictionary<ulong, int>();
    private string announceBig, announceSmall;
    private float announceUntil;
    private int announceId;

    /// <summary>La dernière marche s'annonce à tous (« Untel passe au gant »), et une marche perdue au gant aussi.
    /// Chaque machine le voit aux marches publiées par le serveur : rien de plus ne voyage.</summary>
    private void TrackGunGame(RoundManager rm)
    {
        if (rm.Mode != GameModeId.GunGame || rm.CurrentPhase != RoundManager.Phase.Active)
        {
            seenSteps.Clear();
            return;
        }
        int glove = GunGame.GloveStep(rm.Ladder);
        foreach (PlayerMatchState state in duelists)
        {
            int step = state.GunGameStep;
            if (seenSteps.TryGetValue(state.OwnerClientId, out int before) && step != before)
            {
                if (step == glove) Announce($"{state.DisplayName} passe au gant", "Dernière marche : un coup de gant, et c'est fini.", 3f);
                else if (step < before) Announce($"{state.DisplayName} redescend d'un cran", "Un coup de gant.", 2.5f);
            }
            seenSteps[state.OwnerClientId] = step;
        }
    }

    // ------------------------------------------------------------------
    // L'Élimination confirmée : ce qui s'annonce (GDD § 11)
    // ------------------------------------------------------------------

    // Les modules pris, et ceux déjà vus, à l'image d'avant.
    private int seenModulesTaken = -1;
    private readonly System.Collections.Generic.HashSet<ushort> seenModules = new System.Collections.Generic.HashSet<ushort>();

    /// <summary>Un module pris s'annonce à tous (« Untel confirme l'élimination », « Untel reprend son module ») ; au
    /// vainqueur, le module qui tombe rappelle qu'il faut aller le ramasser. Chaque machine le voit à ce que publie le
    /// serveur (la liste des modules, et chaque module pris : RoundManager.ModulesTaken).</summary>
    private void TrackKillConfirmed(RoundManager rm, ulong me)
    {
        if (rm.Mode != GameModeId.KillConfirmed || rm.CurrentPhase != RoundManager.Phase.Active)
        {
            seenModules.Clear();
            seenModulesTaken = rm.ModulesTaken;
            return;
        }
        if (seenModulesTaken >= 0 && rm.ModulesTaken != seenModulesTaken)
        {
            string taker = PlayerMatchState.NameOf(rm.LastModuleTaker);
            if (rm.LastModuleConfirmed) Announce($"{taker} confirme l'élimination", $"{rm.LastModuleScore} sur {rm.RoundsToWin}", 2.5f);
            else Announce($"{taker} reprend son module", "L'élimination ne compte pas.", 2.5f);
        }
        seenModulesTaken = rm.ModulesTaken;
        for (int i = 0; i < rm.ModuleCount; i++)
        {
            KillConfirmed.Module module = rm.ModuleAt(i);
            if (seenModules.Add(module.id) && module.killerId == me)
                Announce("Élimination à confirmer", "Ramasse son module pour qu'elle compte.", 2.5f);
        }
    }

    // ------------------------------------------------------------------
    // La Zone : ses annonces, son état, ses repères (GDD § 11)
    // ------------------------------------------------------------------

    // La zone et son annonce à l'image d'avant, et les duellistes déjà annoncés à la moitié des points.
    private int seenZone = -1;
    private bool seenZoneAnnounced;
    private bool lastMinuteAnnounced;
    private bool overtimeAnnounced;
    private readonly System.Collections.Generic.HashSet<ulong> halfAnnounced = new System.Collections.Generic.HashSet<ulong>();

    /// <summary>La suivante s'annonce (« La zone bouge dans 10 s », Z9), son changement aussi, et l'IA annonce qui passe
    /// la moitié des points (Z7), en bandeau tant qu'elle n'a pas de voix. Chaque machine le voit à ce que publie le
    /// serveur (le temps du match, les scores).</summary>
    private void TrackZone(RoundManager rm)
    {
        ZoneLayout layout = ZoneLayout.Instance;
        if (rm.Mode != GameModeId.Zone || rm.CurrentPhase != RoundManager.Phase.Active || layout == null)
        {
            seenZone = -1;
            seenZoneAnnounced = false;
            lastMinuteAnnounced = overtimeAnnounced = false;
            if (rm.CurrentPhase != RoundManager.Phase.MatchOver) halfAnnounced.Clear();
            return;
        }
        int zone = rm.ShownZone;
        int next = rm.AnnouncedZone;
        if (seenZone >= 0 && zone != seenZone) Announce("La zone change de place", layout.NameOf(zone), 2.5f);
        else if (seenZone >= 0 && next >= 0 && !seenZoneAnnounced)
            Announce($"La zone bouge dans {Mathf.CeilToInt(rm.ZoneTimeLeft)} s", "Prochaine : " + layout.NameOf(next), 2.5f);
        seenZone = zone;
        seenZoneAnnounced = next >= 0;

        if (rm.ZoneOvertime && !overtimeAnnounced)
        {
            overtimeAnnounced = true;
            Announce("Prolongation", "Égalité : le prochain point gagne.", 3f);
        }
        else if (rm.ZoneMatchTimeLeft <= LastMinute && rm.ZoneMatchTimeLeft > LastMinute - 2f && !lastMinuteAnnounced)
        {
            lastMinuteAnnounced = true;
            Announce("Plus qu'une minute", "À la fin du chrono, le plus de points gagne.", 2.5f);
        }

        int half = ZoneMode.TargetPoints / 2;
        foreach (PlayerMatchState state in duelists)
        {
            if (rm.WinsOf(state.OwnerClientId) < half || !halfAnnounced.Add(state.OwnerClientId)) continue;
            Announce($"{state.DisplayName} passe la moitié", $"{ZoneMode.Points(half)} sur {ZoneMode.Points(ZoneMode.TargetPoints)}", 2.5f);
        }
    }

    /// <summary>Ce que dit la ligne sous la barre de score, que se partagent la Zone, le Tag Team et un match du Tournoi :
    /// son texte n'est fabriqué que quand cela change, pas à chaque image pour être comparé ensuite. Les pseudos en font
    /// partie : ils peuvent arriver après coup.</summary>
    public struct StatusKey : System.IEquatable<StatusKey>
    {
        public byte source;     // ZoneLine, TeamLine ou LaneLine
        public int a, b, c, d;
        public string name, next;

        public bool Equals(StatusKey o) => source == o.source && a == o.a && b == o.b && c == o.c && d == o.d && name == o.name && next == o.next;
    }

    private const byte ZoneLine = 1, TeamLine = 2, LaneLine = 3;

    private StatusKey shownStatus;
    private bool statusWritten;
    private Color shownZoneColor;

    private bool StatusChanged(in StatusKey key)
    {
        if (statusWritten && key.Equals(shownStatus)) return false;
        statusWritten = true;
        shownStatus = key;
        return true;
    }

    /// <summary>L'état de la zone, sous la barre de score : « ZONE DISPUTÉE », « ZONE LIBRE », « TU TIENS LA ZONE »,
    /// « GUSTAVO TIENT LA ZONE », et dans combien de secondes elle bouge. `holder` : qui la tient, si ce n'est pas soi.
    /// Fonction pure, testée.</summary>
    public static string ZoneStatusLine(bool contested, bool free, bool mine, string holder, int seconds)
    {
        string state = contested ? "ZONE DISPUTÉE" : free ? "ZONE LIBRE" : mine ? "TU TIENS LA ZONE" : holder.ToUpperInvariant() + " TIENT LA ZONE";
        return $"{state} · ELLE BOUGE DANS {seconds} S";
    }

    /// <summary>L'état de la zone sous la barre de score (« Gustavo tient la zone · elle bouge dans 7 s »), à la couleur
    /// de qui la tient, et ses repères à l'écran : la zone du moment (sauf pour qui est dedans : l'état le lui dit) et,
    /// les dernières secondes, la suivante, avec leur distance, au bord de l'écran quand elles sont hors champ. Vrai si la
    /// ligne de l'état se montre.</summary>
    private bool UpdateZone(RoundManager rm, ulong me, Vector3 feet, bool endScreen)
    {
        if (rm.Mode != GameModeId.Zone)
        {
            // Rien à placer hors de la Zone : un dernier passage éteint ses repères.
            if (zoneMarkersUp)
            {
                zoneMarkersUp = false;
                SetVisible(zoneMarker, false);
                SetVisible(nextZoneMarker, false);
            }
            return false;
        }
        zoneMarkersUp = true;
        ZoneLayout layout = ZoneLayout.Instance;
        int shown = rm.ShownZone;
        bool show = !endScreen && layout != null && shown >= 0;
        bool inside = show && layout.Contains(shown, feet);
        bool active = show && rm.CurrentPhase == RoundManager.Phase.Active;
        Color color = ZoneHudColor(rm);
        if (active)
        {
            ulong holder = rm.ZoneHolder;
            bool contested = rm.ZoneContested;
            bool free = holder == RoundManager.NoWinner;
            bool mine = holder == me && (localState == null || !localState.IsSpectator);
            var key = new StatusKey
            {
                source = ZoneLine,
                a = contested ? 0 : free ? 1 : mine ? 2 : 3,
                b = Mathf.CeilToInt(rm.ZoneTimeLeft),
                name = contested || free || mine ? null : PlayerMatchState.NameOf(holder),
            };
            bool changed = StatusChanged(key);
            if (changed) zoneStatus.text = ZoneStatusLine(contested, free, mine, key.name, key.b);
            if (changed || color != shownZoneColor)
            {
                shownZoneColor = color;
                zoneStatus.style.color = color;
            }
        }
        PlaceMarker(zoneMarker, zoneMarkerAnchor, zoneMarkerIcon, zoneMarkerHalo, zoneMarkerLabel, show && !inside, show ? layout.Center(shown) : Vector3.zero, "ZONE", color, ref zoneMarkerShown);
        int next = rm.AnnouncedZone;
        PlaceMarker(nextZoneMarker, nextZoneMarkerAnchor, nextZoneMarkerIcon, nextZoneMarkerHalo, nextZoneMarkerLabel, show && next >= 0, next >= 0 ? layout.Center(next) : Vector3.zero, "SUIVANTE",
                    new Color(0.82f, 0.9f, 1f), ref nextMarkerShown);
        return active;
    }

    /// <summary>La couleur de la zone au HUD : celle de qui la tient, blanche sinon.</summary>
    private static Color ZoneHudColor(RoundManager rm)
    {
        if (rm.ZoneContested || rm.ZoneHolder == RoundManager.NoWinner) return new Color(0.94f, 0.95f, 0.96f);
        return PaintOf(rm.ZoneHolder);
    }

    /// <summary>Un repère se déplace à chaque image : par left et top, ce serait une mise en page par image. Il suit donc un
    /// parent, de la taille du sien, qui ne fait que glisser (un transform seul) ; le repère garde son centrage (translate
    /// en USS), et le même parent pour sa largeur. Fonction testée.</summary>
    public static VisualElement MarkerAnchor(VisualElement marker)
    {
        if (marker == null) return null;
        const string name = "zoneMarkerAnchor";
        if (marker.parent.name == name) return marker.parent;
        var anchor = new VisualElement { name = name, pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform };
        anchor.style.position = Position.Absolute;
        anchor.style.left = 0f;
        anchor.style.top = 0f;
        anchor.style.width = Length.Percent(100f);
        anchor.style.height = Length.Percent(100f);
        VisualElement parent = marker.parent;
        parent.Insert(parent.IndexOf(marker), anchor);
        anchor.Add(marker);
        marker.style.left = 0f;
        marker.style.top = 0f;
        return anchor;
    }

    // Ce qu'affiche un repère : sa place, sa distance, sa couleur et son opacité (rien n'est réécrit sans changement).
    private struct MarkerShown
    {
        public Vector2 at;
        public int distance;
        public Color color;
        public float opacity;
        public bool written;
    }

    private MarkerShown zoneMarkerShown, nextMarkerShown;

    /// <summary>Le repère d'une zone flotte à tant de mètres au-dessus de son centre : au-dessus de sa paroi et de la
    /// tête d'un robot qui la tient, qu'il ne cache pas.</summary>
    public const float MarkerHeight = 3f;

    /// <summary>Le dernier moment annoncé du chrono (s).</summary>
    private const float LastMinute = 60f;

    /// <summary>Un repère reste à tant de points du bord de l'écran : la moitié de sa largeur, sa distance comprise.</summary>
    private const float MarkerMargin = 76f;

    /// <summary>Pose un repère à l'écran au-dessus du centre d'une zone : un losange lumineux, son titre et sa distance (à
    /// plat) au joueur, estompé près du réticule. Il se voit à travers tout : c'est du HUD.</summary>
    private void PlaceMarker(VisualElement marker, VisualElement anchor, VisualElement icon, VisualElement halo, Label label, bool visible, Vector3 center, string title, Color color, ref MarkerShown shown)
    {
        Camera camera = visible ? Camera.main : null;
        VisualElement root = document != null ? document.rootVisualElement : null;
        float width = root != null ? root.layout.width : float.NaN;
        float height = root != null ? root.layout.height : float.NaN;
        bool ok = visible && camera != null && marker != null && label != null && width > 0f && height > 0f;
        SetVisible(marker, ok);
        if (!ok) return;
        var panel = new Vector2(width, height);
        Vector2 at = MarkerPosition(camera.WorldToViewportPoint(center + Vector3.up * MarkerHeight), panel, MarkerMargin);
        if (!shown.written || at != shown.at)
        {
            shown.at = at;
            anchor.style.translate = new Translate(at.x, at.y);
        }
        Vector3 offset = camera.transform.position - center;
        int distance = Mathf.RoundToInt(new Vector2(offset.x, offset.z).magnitude);
        if (!shown.written || distance != shown.distance)
        {
            shown.distance = distance;
            label.text = $"{title} · {distance} m";
        }
        if (!shown.written || color != shown.color)
        {
            shown.color = color;
            label.style.color = color;
            icon.style.borderTopColor = icon.style.borderRightColor = icon.style.borderBottomColor = icon.style.borderLeftColor = color;
            icon.style.backgroundColor = new Color(color.r, color.g, color.b, 0.35f);
            halo.style.backgroundColor = new Color(color.r, color.g, color.b, 0.16f);
        }
        float opacity = MarkerOpacity(at, panel);
        if (!shown.written || Mathf.Abs(opacity - shown.opacity) > 0.01f)
        {
            shown.opacity = opacity;
            marker.style.opacity = opacity;
        }
        shown.written = true;
    }

    /// <summary>
    /// L'opacité d'un repère posé en `at` sur un panneau de taille `panel` : estompé tout près du réticule, au centre de
    /// l'écran, pour ne pas gêner la visée, mais assez pour se voir quand on regarde vers la zone à travers un mur (au
    /// quart, il disparaissait, 2026-11-04), plein à partir de 15 % de la hauteur de l'écran. Fonction pure, testée.
    /// </summary>
    public static float MarkerOpacity(Vector2 at, Vector2 panel)
    {
        if (panel.y <= 0f) return 1f;
        float fromCenter = Vector2.Distance(at, panel * 0.5f) / panel.y;
        return Mathf.Lerp(0.45f, 1f, Mathf.InverseLerp(0.05f, 0.15f, fromCenter));
    }

    /// <summary>
    /// Où poser un repère à l'écran (en points d'un panneau de taille `panel`) pour un point du monde vu en `viewport`
    /// (Camera.WorldToViewportPoint) : à sa place s'il est devant ; s'il est derrière, retourné et posé au bas de
    /// l'écran ; jamais à moins de `margin` du bord. Fonction pure, testée.
    /// </summary>
    public static Vector2 MarkerPosition(Vector3 viewport, Vector2 panel, float margin)
    {
        float x = viewport.x, y = viewport.y;
        if (viewport.z < 0f)
        {
            x = 1f - x;
            y = 0f;
        }
        return new Vector2(Mathf.Clamp(x * panel.x, margin, panel.x - margin),
                           Mathf.Clamp((1f - y) * panel.y, margin, panel.y - margin));
    }

    private void Announce(string big, string small, float duration)
    {
        announceBig = big;
        announceSmall = small;
        announceUntil = Time.time + duration;
        announceId++;
    }

    /// <summary>La couleur d'un joueur au HUD : celle de son robot, éclaircie (blanche avant sa peinture).</summary>
    public static Color PaintOf(ulong clientId)
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
    private void UpdateHealth()
    {
        // Un spectateur n'a pas de vie à perdre : rien à montrer (2026-10-13).
        Health health = localHealth;
        if (health == null || (localState != null && localState.IsSpectator))
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
    // Le Tag Team : la barre des équipes, son équipe et son rôle, les annonces (GDD § 11)
    // ------------------------------------------------------------------

    /// <summary>Le combattant de `team` (sur toutes les machines : rôles et équipes sont publiés), ou null.</summary>
    private static PlayerMatchState FighterOf(PlayerTeam team)
    {
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && team != PlayerTeam.None && state.Team == team && state.IsDuelist) return state;
        }
        return null;
    }

    /// <summary>Le coéquipier qui entre au prochain relais de `team` (sa place dans le relais : 1), ou null.</summary>
    private static string NextTeammate(PlayerTeam team)
    {
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && team != PlayerTeam.None && state.Team == team && !state.IsDuelist && state.QueuePosition == 1)
                return state.DisplayName;
        }
        return null;
    }

    /// <summary>Au Tag Team, la barre oppose les équipes : à gauche la sienne (l'équipe A vue d'ailleurs), à droite
    /// l'autre ; le nom de leur combattant, à sa couleur, et leurs points, sans marques.</summary>
    private void UpdateTagTeamScoreBar(RoundManager rm)
    {
        PlayerTeam leftTeam = localState != null && localState.Team != PlayerTeam.None ? localState.Team : PlayerTeam.A;
        var bar = new ScoreBarKey { tagTeam = true, left = TeamSide(rm, leftTeam), right = TeamSide(rm, TagTeam.Other(leftTeam)) };
        if (!barWritten || !bar.Equals(shownBar))
        {
            barWritten = true;
            shownBar = bar;
            if (pipCount != 0 || pipsAreSteps || pipsCompact) BuildPips(0, false, false);
            ShowTeamSide(leftName, leftPips, leftPoints, leftWeapon, bar.left, ref shownLeftPoints, ref shownLeftWeapon);
            ShowTeamSide(rightName, rightPips, rightPoints, rightWeapon, bar.right, ref shownRightPoints, ref shownRightWeapon);
        }

        bool active = rm.CurrentPhase == RoundManager.Phase.Active;
        SetVisible(roundTimer, true);
        int seconds = active ? Mathf.Max(0, Mathf.FloorToInt(rm.PhaseElapsed)) : -1;
        if (seconds != shownTimerSeconds)
        {
            shownTimerSeconds = seconds;
            roundTimer.text = active ? FormatTime(seconds) : "VS";
        }
    }

    private static ScoreSide TeamSide(RoundManager rm, PlayerTeam team)
    {
        PlayerMatchState fighter = FighterOf(team);
        return new ScoreSide
        {
            shown = true,
            id = fighter != null ? fighter.OwnerClientId : RoundManager.NoWinner,
            score = rm.TeamPoints(team),
            name = fighter != null ? fighter.DisplayName : TagTeam.Name(team),
            paint = fighter != null ? PaintOf(fighter.OwnerClientId) : Color.white,
        };
    }

    private static void ShowTeamSide(Label name, VisualElement pips, Label points, Label weapon, in ScoreSide side,
                                     ref int shownPoints, ref int shownWeapon)
    {
        SetVisible(name, true);
        SetVisible(pips, false);
        name.text = side.name;
        name.style.color = side.paint;
        ShowStepWeapon(weapon, false, GunGameLadder.Full, 0, ref shownWeapon);
        ShowPoints(points, true, side.score, ref shownPoints);
    }

    /// <summary>Au Tag Team, sous la barre : son équipe et son rôle (TeamStatus). Le texte n'est refait qu'au changement.
    /// Vrai si la ligne se montre.</summary>
    private bool UpdateTagTeam(RoundManager rm, bool endScreen)
    {
        if (!rm.TagTeamMode) return false;
        RoundManager.Phase phase = rm.CurrentPhase;
        bool show = !endScreen && (phase == RoundManager.Phase.Active || phase == RoundManager.Phase.Starting || phase == RoundManager.Phase.Preparation);
        PlayerMatchState me = localState;
        if (!show || me == null) return show;
        PlayerMatchState fighter = FighterOf(me.Team);
        var key = new StatusKey
        {
            source = TeamLine,
            a = (int)me.Team,
            b = me.IsDuelist ? 1 : 0,
            c = me.QueuePosition,
            name = fighter != null ? fighter.DisplayName : null,
            next = NextTeammate(me.Team),
        };
        if (!StatusChanged(key)) return true;
        zoneStatus.text = TeamStatus(me.Team, me.IsDuelist, key.c, key.name, key.next);
        zoneStatus.style.color = new Color(0.94f, 0.95f, 0.96f);
        return true;
    }

    /// <summary>
    /// Le statut d'un joueur au Tag Team, sous la barre de score : « ÉQUIPE A · TU TE BATS · ENSUITE GUSTAVO 3 » pour le
    /// combattant, « ÉQUIPE A · TU ENTRES QUAND GUSTAVO TOMBE » pour le prochain, « ÉQUIPE A · TU ENTRES AU 2E RELAIS »
    /// pour le suivant, les deux équipes pour qui n'en a pas. `place` : sa place dans le relais (0 : il se bat).
    /// Fonction pure, testée.
    /// </summary>
    public static string TeamStatus(PlayerTeam team, bool fighting, int place, string fighter, string next)
    {
        if (team == PlayerTeam.None) return "ÉQUIPE A CONTRE ÉQUIPE B";
        string side = TagTeam.Name(team).ToUpperInvariant();
        if (fighting) return string.IsNullOrEmpty(next) ? $"{side} · TU TE BATS" : $"{side} · TU TE BATS · ENSUITE {next.ToUpperInvariant()}";
        if (place <= 1) return string.IsNullOrEmpty(fighter) ? $"{side} · TU ENTRES AU PROCHAIN RELAIS" : $"{side} · TU ENTRES QUAND {fighter.ToUpperInvariant()} TOMBE";
        return $"{side} · TU ENTRES AU {place}E RELAIS";
    }

    // Les éliminations et les entrées du Tag Team déjà annoncées.
    private int seenTagKills = -1, seenTagIns = -1;

    /// <summary>Au Tag Team, chaque élimination s'annonce (« Untel élimine Untel » et les points rapportés), et chaque
    /// entrée (« Untel entre dans l'arène »). Chaque machine les apprend du serveur (RoundManager.TagKills, TagIns).</summary>
    private void TrackTagTeam(RoundManager rm)
    {
        if (!rm.TagTeamMode || rm.CurrentPhase != RoundManager.Phase.Active)
        {
            seenTagKills = rm.TagKills;
            seenTagIns = rm.TagIns;
            return;
        }
        if (seenTagKills >= 0 && rm.TagKills != seenTagKills)
        {
            PlayerMatchState killer = PlayerMatchState.Of(rm.LastTagKiller);
            Announce($"{PlayerMatchState.NameOf(rm.LastTagKiller)} élimine {PlayerMatchState.NameOf(rm.LastTagVictim)}",
                     KillLine(rm.LastTagPoints, killer != null ? killer.Team : PlayerTeam.None, rm.LastTagCause), 2.5f);
        }
        if (seenTagIns >= 0 && rm.TagIns != seenTagIns)
        {
            PlayerMatchState entering = PlayerMatchState.Of(rm.LastTagIn);
            Announce($"{PlayerMatchState.NameOf(rm.LastTagIn)} entre dans l'arène",
                     entering != null && entering.Team != PlayerTeam.None ? $"Pour l'équipe {TagTeam.Letter(entering.Team)}" : "", 2.5f);
        }
        seenTagKills = rm.TagKills;
        seenTagIns = rm.TagIns;
    }

    /// <summary>Les points d'une élimination au Tag Team, en une ligne : « +15 pour l'équipe A · à la tête ». Fonction
    /// pure, testée.</summary>
    public static string KillLine(int points, PlayerTeam team, Health.DamageCause cause)
    {
        string how = cause == Health.DamageCause.HeadShot ? " · à la tête" : cause == Health.DamageCause.Glove ? " · au gant" : "";
        return $"+{points} pour l'équipe {TagTeam.Letter(team)}{how}";
    }

    // ------------------------------------------------------------------
    // Le Tournoi : d'un match à la salle d'attente, ses annonces (GDD § 11, 2026-11-03)
    // ------------------------------------------------------------------

    // Le match montré à l'image d'avant (un couloir du Tournoi, ou le RoundManager de la scène), et l'instant où le joueur
    // est remonté d'un match dans la salle d'attente (le noir s'y lève). Un couloir fini est détruit : on garde à part
    // ce qu'il était.
    private RoundManager shownMatch;
    private bool shownMatchIsLane;
    private float backInRoomAt = float.NegativeInfinity;

    /// <summary>Le joueur passe d'un match à la salle d'attente, ou l'inverse : les messages repartent de zéro (une
    /// annonce de la salle ne déborde pas sur le match), et le retour dans la salle se fait sous le noir, le temps du
    /// replacement.</summary>
    private void TrackMatchChange(RoundManager rm)
    {
        if (ReferenceEquals(rm, shownMatch)) return;
        if (shownMatchIsLane && !rm.IsLane) backInRoomAt = Time.time;
        shownMatch = rm;
        shownMatchIsLane = rm.IsLane;
        centerShownOnce = false;
        announceUntil = 0f;
    }

    // L'étape du tournoi déjà annoncée (son début, publié par le serveur).
    private double seenStageAt = -1d;

    /// <summary>
    /// Au Tournoi, dans la salle d'attente, chaque étape s'annonce : le tirage, un tour (« Tu te reposes » pour qui n'y
    /// joue pas), l'entracte et ce qui suit, la finale. Ceux qui jouent un match ne voient rien : leur match a ses
    /// messages. Chaque machine le voit à l'étape publiée par le serveur (Tournament.State).
    /// </summary>
    private void TrackTournament(RoundManager main, RoundManager rm, ulong me)
    {
        if (main == null || !main.IsSpawned || !main.TournamentMode || main.CurrentPhase != RoundManager.Phase.Tournament)
        {
            seenStageAt = -1d;
            return;
        }
        Tournament.State s = main.TournamentProgress;
        if (s.stageStartedAt == seenStageAt) return;
        // Dans son match, l'étape attend son retour dans la salle ; arrivé en plein tournoi, ou revenu longtemps après
        // le début de l'étape, rien à rattraper.
        if (!ReferenceEquals(rm, main)) return;
        seenStageAt = s.stageStartedAt;
        if (main.TournamentStageElapsed > 2.5f) return;
        TournamentBoard.CollectMatches(main, boardMatches);
        TournamentBoard.CollectPlayers(main, boardPlayers);
        TournamentBoard.CollectLeft(main, boardPlayers, boardLeft);
        if (TournamentBoard.TryGetAnnouncement(s, me, boardPlayers, boardMatches, boardLeft, Mathf.CeilToInt(main.TournamentStageTimeLeft),
                                               PlayerMatchState.NameOf, out string big, out string small))
            Announce(big, small, 3f);
    }

    private readonly System.Collections.Generic.List<Tournament.Match> boardMatches = new System.Collections.Generic.List<Tournament.Match>();
    private readonly System.Collections.Generic.List<ulong> boardPlayers = new System.Collections.Generic.List<ulong>();
    private readonly System.Collections.Generic.HashSet<ulong> boardLeft = new System.Collections.Generic.HashSet<ulong>();

    /// <summary>Pendant un match du Tournoi, sous la barre de score : où il en est (« TOUR 2 SUR 3 · BO3 », « FINALE · BO5 »).
    /// Le texte n'est refait qu'au changement. Vrai si la ligne se montre.</summary>
    private bool UpdateTournamentLine(RoundManager rm, bool endScreen)
    {
        if (!rm.IsLane || endScreen) return false;
        RoundManager main = RoundManager.Instance;
        RoundManager.LaneInfo lane = rm.Lane;
        var key = new StatusKey
        {
            source = LaneLine,
            a = lane.match,
            b = lane.roundsToWin * 2 + (lane.final ? 1 : 0),
            c = main != null ? main.TournamentProgress.tourCount : 0,
            d = main != null ? main.TournamentMatchCount : 0,
        };
        if (!StatusChanged(key)) return true;
        zoneStatus.text = TournamentBoard.LaneLine(lane, main);
        zoneStatus.style.color = new Color(0.94f, 0.95f, 0.96f);
        return true;
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
        public int announce;
        public int respawn;
        public bool ownModule;

        public bool Equals(CenterKey o) => phase == o.phase && value == o.value && round == o.round && winner == o.winner
                                           && wins == o.wins && spectator == o.spectator && endScreen == o.endScreen
                                           && forfeit == o.forfeit && announce == o.announce && respawn == o.respawn
                                           && ownModule == o.ownModule;
    }

    private CenterKey shownCenter;
    private bool centerShownOnce;

    private void UpdateCenterMessage(RoundManager rm, NetworkObject local)
    {
        RoundManager.Phase phase = rm.CurrentPhase;
        int wins = 0;
        foreach (PlayerMatchState state in duelists) wins = wins * 8 + rm.WinsOf(state.OwnerClientId);
        bool gunGame = rm.Mode == GameModeId.GunGame;
        // Le joueur mort d'un mode à réapparitions : le décompte jusqu'à son retour.
        PlayerMatchState me = localState;
        int respawnIn = me != null && me.RespawnAt > 0d
            ? Mathf.Max(1, Mathf.CeilToInt(rm.SecondsUntil(me.RespawnAt))) : 0;
        bool announcing = (phase == RoundManager.Phase.Active || phase == RoundManager.Phase.Tournament) && Time.time < announceUntil;
        // À l'Élimination confirmée, le mort dont le module attend encore son vainqueur peut aller le reprendre.
        bool ownModule = respawnIn > 0 && rm.Mode == GameModeId.KillConfirmed && rm.HasModuleOf(local.OwnerClientId);
        var key = new CenterKey
        {
            phase = phase,
            value = phase == RoundManager.Phase.Starting ? Mathf.Max(1, Mathf.CeilToInt(rm.PhaseTimeRemaining))
                  : phase == RoundManager.Phase.WaitingForPlayers ? Mathf.FloorToInt(Time.unscaledTime) : 0,
            round = rm.RoundNumber,
            winner = rm.LastRoundWinner,
            wins = wins,
            spectator = me != null && me.IsSpectator,
            endScreen = MatchEndScreen.IsShown,
            forfeit = rm.WonByForfeit,
            announce = announcing ? announceId : 0,
            respawn = respawnIn,
            ownModule = ownModule,
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
                small = rm.TagTeamMode ? "En attente des joueurs : il en faut quatre, deux par équipe."
                      : rm.TournamentMode ? "En attente des joueurs : il en faut trois au moins."
                      : key.spectator ? "En attente des duellistes..." : "En attente d'un adversaire...";
                string invite = InviteLine();
                if (invite != null) small += "\n" + invite;
                break;

            case RoundManager.Phase.Starting:
                // CeilToInt pour afficher 3, 2, 1 plutôt que 2, 1, 0 : on veut voir le dernier
                // chiffre pendant toute sa seconde.
                big = Mathf.Max(1, Mathf.CeilToInt(rm.PhaseTimeRemaining)).ToString();
                small = gunGame ? $"Gun Game · {GunGame.Phrase(rm.Ladder)}"
                      : rm.TagTeamMode ? $"Tag Team · première équipe à {TagTeam.TargetPoints} points"
                      : rm.CountsPoints ? $"Zone · premier à {ZoneMode.Points(rm.RoundsToWin)} points"
                      : rm.DeathMatch ? $"Death match · premier à {rm.RoundsToWin}"
                      : rm.Mode == GameModeId.KillConfirmed ? $"Élimination confirmée · premier à {rm.RoundsToWin}"
                      : rm.IsLane ? TournamentBoard.LaneRoundLine(rm.Lane, RoundManager.Instance, rm.RoundNumber)
                      : $"Manche {rm.RoundNumber}";
                // Les options de la manche : l'arme de l'arsenal commun, la famille de pile ou face, Tête seulement.
                small += RoundExtras(rm, local);
                break;

            case RoundManager.Phase.Active:
                // Le Gun Game : la dernière marche, une marche perdue ; l'Élimination confirmée : un module pris, ou à
                // ramasser ; et le mort, son retour (et son module à reprendre).
                if (announcing)
                {
                    sentence = true;
                    big = announceBig;
                    small = announceSmall;
                }
                if (respawnIn > 0)
                {
                    // Au Tag Team, le coéquipier qui attend prend sa place.
                    string relay = rm.TagTeamMode && me != null ? NextTeammate(me.Team) : null;
                    small = relay != null ? $"{relay} prend ta place dans {respawnIn} s"
                          : ownModule ? $"Réapparition dans {respawnIn} s\nReprends ton module pour annuler l'élimination"
                          : $"Réapparition dans {respawnIn} s";
                }
                break;

            case RoundManager.Phase.Tournament:
                // La salle d'attente : ses annonces (le tirage, un tour, l'entracte, la finale) ; le reste du temps,
                // rien au centre, on s'y entraîne sur les mannequins.
                if (announcing)
                {
                    sentence = true;
                    big = announceBig;
                    small = announceSmall;
                }
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
                if (rm.TagTeamMode)
                {
                    PlayerTeam won = rm.WinningTeam;
                    big = rm.WonByForfeit ? $"L'équipe {TagTeam.Letter(won)} remporte le match par forfait"
                                          : $"L'équipe {TagTeam.Letter(won)} remporte le match !";
                    small = rm.WonByForfeit ? "L'autre équipe a quitté la partie."
                                            : $"{rm.TeamPoints(won)} – {rm.TeamPoints(TagTeam.Other(won))}";
                    break;
                }
                if (rm.WonByForfeit)
                {
                    // L'adversaire a quitté la partie (GDD § 4, 2026-10-25).
                    big = $"{PlayerMatchState.NameOf(rm.LastRoundWinner)} remporte le match par forfait";
                    small = "Son adversaire a quitté la partie.";
                    break;
                }
                big = rm.IsLane && rm.Lane.final ? $"{PlayerMatchState.NameOf(rm.LastRoundWinner)} remporte la finale !"
                                                 : $"{PlayerMatchState.NameOf(rm.LastRoundWinner)} remporte le match !";
                small = gunGame ? Capitalized(GunGame.Phrase(rm.Ladder)) + " finie au gant" : ScoreLine(rm);
                break;
        }

        centerBig.text = big ?? "";
        centerSmall.text = small ?? "";
        SetVisible(centerBig, big != null);
        SetVisible(centerSmall, small != null);
        centerBig.EnableInClassList("center-big--sentence", sentence);
    }

    /// <summary>Ce que le décompte rappelle des options de la manche (l'arme de l'arsenal commun lue dans l'arsenal du
    /// joueur).</summary>
    private static string RoundExtras(RoundManager rm, NetworkObject local)
    {
        string common = null;
        if (rm.CommonArsenalSlot >= 0)
        {
            WeaponController weapon = local.GetComponentInChildren<WeaponController>();
            WeaponData data = weapon != null ? weapon.DataOf(rm.CommonArsenalSlot) : null;
            if (data != null) common = data.weaponName;
        }
        return RoundExtras(rm.WeaponChoiceNow, common, rm.CoinFlipNow.family, rm.Rules.headshotsOnly);
    }

    /// <summary>
    /// Les options de la manche, à la suite du décompte : « · FA-30 pour les deux » (l'arsenal commun), « · fusil de
    /// précision » ou « · le gant seul » (pile ou face), « · tête seulement ». Fonction pure, testée.
    /// </summary>
    public static string RoundExtras(WeaponChoice choice, string commonWeapon, byte family, bool headshotsOnly)
    {
        string extras = "";
        if (choice == WeaponChoice.CommonArsenal && !string.IsNullOrEmpty(commonWeapon)) extras += $" · {commonWeapon} pour les deux";
        else if (choice == WeaponChoice.CoinFlip && family != CoinFlip.NoFamily) extras += " · " + CoinFlip.FamilyLabel(family).ToLowerInvariant();
        if (headshotsOnly) extras += " · tête seulement";
        return extras;
    }

    private static string Capitalized(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

    /// <summary>Le score, du point de vue du gagnant de la manche : « 2 – 1 ». Seuls les duellistes
    /// comptent (2026-10-13).</summary>
    private static string ScoreLine(RoundManager rm)
    {
        var duelistStates = new System.Collections.Generic.List<PlayerMatchState>();
        rm.GetMatchDuelists(duelistStates);
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
        return rm.CountsPoints ? $"{ZoneMode.Points(rm.WinsOf(first))} – {ZoneMode.Points(theirs)}" : $"{rm.WinsOf(first)} – {theirs}";
    }

    /// <summary>Ce qu'il faut donner aux autres pour venir : le code de la partie en ligne (chez tout le
    /// monde, chacun peut inviter), sinon l'adresse de l'hôte en réseau local (chez l'hôte seulement).</summary>
    public static string InviteLine()
    {
        if (!TryGetInvite(out string caption, out string value)) return null;
        return caption == "CODE" ? $"Code de la partie : {value}" : $"Donne cette adresse aux autres joueurs : {value}";
    }

    /// <summary>Ce qu'on donne aux autres pour venir, au salon de l'arène : « CODE » et le code de la partie en
    /// ligne, ou « ADRESSE » et l'adresse de l'hôte en réseau local (chez l'hôte seulement). Faux s'il n'y a
    /// rien à donner.</summary>
    public static bool TryGetInvite(out string caption, out string value)
    {
        if (OnlineSession.InSession && !string.IsNullOrEmpty(OnlineSession.Code))
        {
            caption = "CODE";
            value = OnlineSession.Code;
            return true;
        }
        value = HostAddress();
        caption = value != null ? "ADRESSE" : "";
        return value != null;
    }

    /// <summary>L'adresse à donner à son adversaire, chez l'hôte seulement (le menu la montrait,
    /// mais l'arène se charge aussitôt l'hébergement lancé).</summary>
    private static string HostAddress()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsHost || !nm.TryGetComponent(out UnityTransport transport)) return null;
        string ip = transport.ConnectionData.Address;
        ushort port = transport.ConnectionData.Port;
        if (hostAddress == null || ip != hostAddressIp || port != hostAddressPort)
        {
            hostAddressIp = ip;
            hostAddressPort = port;
            hostAddress = $"{ip}:{port}";
        }
        return hostAddress;
    }

    // Gardée tant qu'elle ne change pas : le salon la relit à chaque image, pour sa clé.
    private static string hostAddress;
    private static string hostAddressIp;
    private static ushort hostAddressPort;

    // ------------------------------------------------------------------
    // Fondu au noir
    // ------------------------------------------------------------------

    /// <summary>
    /// L'opacité du noir : il tombe à la fin d'une manche (et avant l'écran de fin), reste complet
    /// pendant le replacement des joueurs, puis se lève au début de la préparation (au début du décompte,
    /// dans un mode sans préparation). Calé sur les
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
            case RoundManager.Phase.Starting when RoundManager.HasPreparation(rm.Rules):
                return 0f;
            case RoundManager.Phase.WaitingForPlayers when rm.PhaseHasEnd:
                // L'adversaire est arrivé : le match part à la fin de l'attente (RoundManager.MatchStartFade), le noir
                // tombe d'abord.
                return FadeOut(rm.PhaseTimeRemaining);
            case RoundManager.Phase.Tournament:
            {
                // Au Tournoi, la salle d'attente s'ouvre sous le noir : au début du tournoi (tous y sont replacés), et au
                // retour d'un match (le replacement arrive avec la fin du couloir).
                float fromStart = 1f - Mathf.Clamp01((rm.PhaseElapsed - fadeHold) / Mathf.Max(0.01f, fadeInTime));
                float fromMatch = 1f - Mathf.Clamp01((Time.time - backInRoomAt - fadeHold) / Mathf.Max(0.01f, fadeInTime));
                return Mathf.Max(fromStart, fromMatch);
            }
            case RoundManager.Phase.Preparation:
            case RoundManager.Phase.Salon:
            case RoundManager.Phase.Starting:
                // La lumière revient sur la préparation, sur le salon après le noir de l'écran de fin, et sur
                // le décompte d'un mode sans préparation (le Golden Gun).
                return 1f - Mathf.Clamp01((rm.PhaseElapsed - fadeHold) / Mathf.Max(0.01f, fadeInTime));
            default:
                return 0f;
        }
    }

    private float FadeOut(float untilEnd)
        => 1f - Mathf.Clamp01((untilEnd - fadeHold) / Mathf.Max(0.01f, fadeOutLead - fadeHold));

    /// <summary>
    /// Le noir de l'arrivée dans l'arène (2026-11-03) : complet tant que la partie n'est pas prête sur cette machine
    /// (`readyFor` négatif : son robot ou l'arbitre de la manche pas encore là, les joueurs du salon en route), puis il
    /// se lève comme au début d'une manche : `hold` secondes complet, puis en `fadeIn`. Sans lui, on voyait son robot
    /// l'arme en main une seconde, puis le noir de la préparation coupait la vue (l'utilisateur : « ça fait tchip »).
    /// Fonction pure, testée.
    /// </summary>
    public static float ArrivalBlack(float readyFor, float hold, float fadeIn)
        => readyFor < 0f ? 1f : 1f - Mathf.Clamp01((readyFor - hold) / Mathf.Max(0.01f, fadeIn));

    /// <summary>Une image ne compte pas plus de ce temps (s) dans l'arrivée (StepArrival).</summary>
    public const float MaxArrivalStep = 1f / 30f;

    /// <summary>
    /// Le temps compté depuis que la partie est prête sur cette machine (-1 : pas encore, `ready` faux). Chaque image
    /// n'en compte que MaxArrivalStep au plus : l'image qui charge l'arène, et la première qui la dessine, durent souvent
    /// plus d'une demi-seconde, et le noir se levait pendant qu'elles figeaient l'écran (vu au banc le 2026-11-03 : le
    /// joueur virtuel arrivait dans une arène déjà éclairée). Un match qui va partir de l'attente (`matchComing` : le
    /// noir y retombe, RoundManager.MatchStartFade) l'arrête : le noir de l'arrivée ne se lève plus pendant qu'il
    /// retombe (il se levait à moitié, puis revenait). Fonction pure, testée.
    /// </summary>
    public static float StepArrival(float readyFor, bool ready, bool matchComing, float dt)
    {
        if (!ready) return -1f;
        if (readyFor < 0f) return 0f;
        if (matchComing) return readyFor;
        return readyFor + Mathf.Clamp(dt, 0f, MaxArrivalStep);
    }

    /// <summary>Ce que dit le noir du chargement : le retour au menu, les joueurs du salon en route, ou le chargement.
    /// Fonction pure, testée.</summary>
    public static string LoadingTitle(bool leaving, bool gathering)
        => leaving ? "RETOUR AU MENU" : gathering ? "LES JOUEURS DU SALON ARRIVENT" : "CHARGEMENT";

    private void ShowLoading(bool shown, string title)
    {
        SetVisible(loadingBlock, shown);
        // Le sien aussi : il ne regarde que son propre affichage, et tournerait tout le match sous son bloc caché.
        SetVisible(loadingSpinner, shown);
        if (!shown || loadingTitle == null || title == shownLoadingTitle) return;
        shownLoadingTitle = title;
        loadingTitle.text = title;
    }

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
