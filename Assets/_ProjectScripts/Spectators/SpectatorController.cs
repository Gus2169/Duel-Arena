using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ce que fait un spectateur, sur la tribune (GDD § 9, 2026-10-13) : un contrôleur à part, puisqu'il
/// ne fait pas ce que fait un duelliste, posé sur le même robot. Son déplacement (marcher, courir,
/// s'accroupir) reste celui de PlayerLocomotion, prédit et réconcilié comme celui d'un duelliste ; le
/// rôle lui retire le reste (ni arme, ni lean, ni franchissement, ni allongé).
///
/// - SE BATTRE : des coups de poing (clic gauche les poings sortis, ou V). Relancés avant la fin du
///   geste, ils s'enchaînent gauche puis droite (2026-10-27). Un coup étourdit un autre spectateur
///   quelques secondes, qui lâche ce qu'il tient : on peut le lui voler. Il fend aussi les blocs de la
///   tribune, d'où l'on arrache des cailloux.
/// - PARER (clic droit maintenu, les poings sortis, 2026-10-27) : la garde arrête de face les coups de
///   poing et les cailloux. On n'y frappe pas, on n'y court pas, et on y marche comme un duelliste qui
///   vise : pour un spectateur, la garde prend la place de la visée (PlayerLocomotion).
/// - RAMASSER (F) : un caillou ou du matériel posé devant soi. Une seule chose en main.
/// - LA MOLETTE range l'objet tenu (les poings sont sortis) ou le ressort (2026-10-27).
/// - LANCER (l'objet en main, 2026-10-27, demandé par l'utilisateur) : le clic droit maintenu vise, le bras
///   armé, et montre au seul lanceur la trajectoire de l'objet jusqu'à ce qu'il touche le décor
///   (ThrowArcPreview) ; le clic gauche lance, à pleine force. Relâcher le clic droit renonce.
/// - ÊTRE ÉTOURDI : on ne bouge plus et on ne fait plus rien pendant quelques secondes, puis on est
///   immunisé un instant, pour ne pas rester au sol sous les coups.
///
/// AUTORITÉ : comme le taser. Le joueur demande (coup, ramassage, lancer), le serveur vérifie tout et
/// décide seul ; aucune RPC n'accepte de résultat. L'étourdissement et l'objet tenu sont publiés par
/// le serveur ; l'étourdissement est lu par PlayerLocomotion.Move() (catégorie A, comme le gel de
/// manche). Les sons sont décidés par le serveur (catégorie B). Le geste, l'objet en main, les
/// étincelles et la vue subjective sont cosmétiques (catégorie C).
///
/// Le même composant reçoit, chez un DUELLISTE, ce que lui font les spectateurs : un caillou qui le
/// touche secoue sa visée (tranché par l'utilisateur le 2026-10-13 : aucun dégât), et sa vue montre d'où
/// il est parti (2026-10-27, HitIndicatorHud), comme celle d'un spectateur que sa garde n'a pas protégé.
/// </summary>
[DisallowMultipleComponent]
public class SpectatorController : NetworkBehaviour
{
    /// <summary>L'état publié par le serveur.</summary>
    public struct State : INetworkSerializeByMemcpy
    {
        /// <summary>Fin de l'étourdissement, horloge du serveur (0 : jamais étourdi).</summary>
        public double stunnedUntil;
        /// <summary>Fin de l'immunité qui suit un étourdissement.</summary>
        public double stunImmuneUntil;
        public ArenaItemKind carried;
        public byte carriedVariant;
        /// <summary>L'objet tenu est rangé, les poings sont sortis (la molette, 2026-10-27).</summary>
        public bool stowed;
    }

    [Header("Coup de poing (à juger en jouant)")]
    [Tooltip("Durée du geste (s) : on ne refrappe pas avant, et c'est aussi la cadence que le serveur accepte.")]
    [SerializeField] private float punchDuration = 0.45f;
    [Tooltip("De l'appui au contact (s), comme le taser : un spectateur étourdi pendant son élan ne frappe pas.")]
    [SerializeField] private float punchContactDelay = 0.15f;
    [Tooltip("Portée depuis l'œil (m) : le bras tendu.")]
    [SerializeField] private float punchRange = 1.5f;
    [SerializeField] private float punchRadius = 0.3f;
    [Tooltip("Étourdissement d'un coup de poing (s) : « quelques secondes » (demande de l'utilisateur).")]
    [SerializeField] private float punchStunDuration = 2.5f;
    [Tooltip("Après la fin d'un coup, le suivant part encore de l'autre poing pendant ce temps (s) ; au-delà, l'enchaînement repart du gauche.")]
    [SerializeField] private float comboWindow = 0.35f;

    [Header("Garde (à juger en jouant)")]
    [Tooltip("Demi-angle (degrés) autour de l'avant du robot dans lequel la garde arrête un coup de poing ou un caillou : ce qui vient de côté, de dans le dos ou d'au-dessus passe.")]
    [SerializeField] private float guardHalfAngle = 70f;
    [Tooltip("Secousse de la vue (degrés) d'un spectateur dont la garde arrête un coup, et sa durée (s).")]
    [SerializeField] private float guardShake = 1.2f;
    [SerializeField] private float guardShakeDuration = 0.2f;

    [Header("Étourdissement")]
    [Tooltip("Après un étourdissement, on ne peut pas être étourdi de nouveau pendant ce temps (s) : personne ne reste au sol sous les coups de deux autres.")]
    [SerializeField] private float stunImmunity = 1.5f;
    [Tooltip("Étourdissement d'un caillou reçu par un spectateur (s).")]
    [SerializeField] private float rockStunDuration = 1.5f;

    [Header("Ramasser")]
    [Tooltip("Portée (m) depuis l'œil.")]
    [SerializeField] private float pickupRange = 2.3f;
    [Tooltip("Demi-angle du cône (degrés) dans lequel on vise l'objet à ramasser.")]
    [SerializeField] private float pickupAngle = 35f;

    [Header("Lancer (à juger en jouant)")]
    [Tooltip("Le temps (s) que met le bras à s'armer quand on vise (l'élan montré) ; on peut lancer avant.")]
    [SerializeField] private float throwRaiseTime = 0.25f;
    [Tooltip("La vitesse (m/s) d'un objet lancé : un lancer part toujours à pleine force, on vise par la hauteur du regard et la trajectoire montrée.")]
    [SerializeField] private float maxThrowSpeed = 24f;
    [Tooltip("Intervalle minimal entre deux lancers (s), en temps réel côté serveur.")]
    [SerializeField] private float throwCooldown = 0.5f;

    [Header("Un duelliste touché par un caillou (ressenti)")]
    [Tooltip("La visée remonte d'autant (degrés), et reste là : le duelliste doit se recaler.")]
    [SerializeField] private float flinchPitch = 3.5f;
    [Tooltip("Secousse passagère de la vue (degrés), du côté où va le caillou.")]
    [SerializeField] private float flinchShake = 2.5f;
    [SerializeField] private float flinchShakeDuration = 0.35f;

    [Header("Garde-fous serveur")]
    [Tooltip("Distance max (m) entre l'œil que le serveur connaît et l'origine annoncée d'un coup ou d'un lancer. Couvre l'avance de la prédiction en courant.")]
    [SerializeField] private float maxOriginDistance = 3f;
    [Tooltip("Rewind maximal (s) accordé au coup de poing, comme au tir et au taser.")]
    [SerializeField] private float maxRewindSeconds = 0.3f;
    [Tooltip("Marge (m) accordée au ramassage : le joueur se voit un peu en avance sur le serveur.")]
    [SerializeField] private float pickupTolerance = 0.9f;
    [SerializeField] private LayerMask worldMask = 1;

    private readonly NetworkVariable<State> state = new NetworkVariable<State>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private static readonly List<SpectatorController> spawned = new List<SpectatorController>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        spawned.Clear();
#if UNITY_EDITOR
        AutopilotBrawl = AutopilotBrawlMode.None;
        AutopilotSwapRequested = false;
#endif
    }

    public static IReadOnlyList<SpectatorController> Spawned => spawned;

    private PlayerInputReader input;
    private PlayerLocomotion locomotion;
    private PlayerMatchState matchState;
    private PlayerCameraLook cameraLook;
    private Camera playerCamera;
    private AudioSource audioSource;
    private Animator modelAnimator;

    // Chez le joueur.
    private float localPunchEndsAt = -1f;
    private bool punchQueued;          // un appui pendant un coup : le suivant part à la fin du geste
    private bool lastPunchRight;
    private float lastPunchStartedAt = float.NegativeInfinity;
    private bool fistsSelected = true; // le choix de la molette, tout de suite : les poings, ou l'objet tenu
    private bool guarding;
    private bool aimingThrow;
    private float aimStartedAt;
    private float throwHiddenUntil = -1f;
    private ThrowArcPreview throwArc;
    private ArenaItem pickupCandidate;
    private readonly List<Vector3> candidatePositions = new List<Vector3>();
    private readonly List<ArenaItem> candidateItems = new List<ArenaItem>();
    private int rockHitCount;
    private Vector3 lastRockHitFrom;

    // Au serveur.
    private double serverLastPunchAt = double.NegativeInfinity;
    private double serverLastThrowAt = double.NegativeInfinity;
    private bool serverPunchPending;
    private double serverPunchResolveAt;
    private Vector3 serverPunchOffset;
    private Vector3 serverPunchDirection;
    private double serverPunchViewTime;
    private int serverPunchReferenceInput = -1;

    // L'objet tenu, vu sur le corps (toutes les machines).
    private GameObject handVisual;
    private ArenaItemKind handKind;
    private int handVariant = -1;

    // L'étourdissement, vu sur le corps : des arcs autour de la tête (ElectricArc).
    private ArcLines stunArcs;
    private float stunVisual;

    /// <summary>Levé à chaque coup de poing VU depuis cette machine (durée du geste, poing droit ?) : chez
    /// le joueur à l'appui, chez les autres à la diffusion du serveur. Cosmétique (animation, vue
    /// subjective).</summary>
    public event System.Action<float, bool> OnPunch;

    /// <summary>Levé quand la garde de ce spectateur arrête un coup ou un caillou, sur toutes les
    /// machines (le serveur l'a décidé). Cosmétique : les bras encaissent.</summary>
    public event System.Action OnGuardHit;

    /// <summary>Levé à chaque lancer vu depuis cette machine. Cosmétique.</summary>
    public event System.Action OnThrow;

    /// <summary>Levé quand le joueur se retrouve un objet en main (le serveur a accepté le ramassage),
    /// sur toutes les machines. Cosmétique : le geste de ramasser.</summary>
    public event System.Action OnPickUp;

    // ------------------------------------------------------------------
    // Ce que lisent les autres
    // ------------------------------------------------------------------

    /// <summary>Ce joueur est-il spectateur ? (rôle publié par le serveur)</summary>
    public bool IsSpectatorRole => matchState != null && matchState.IsSpectator;

    /// <summary>Hauteur du corps (m), selon la posture publiée : la capsule que touchent les poings et
    /// les cailloux.</summary>
    public float BodyHeight => locomotion != null ? locomotion.ControllerHeight(locomotion.NetworkedStance) : 1.8f;

    public ArenaItemKind Carried => state.Value.carried;
    public int CarriedVariant => state.Value.carriedVariant;

    /// <summary>L'objet tenu tel que cette machine le montre, rangé ou non : chez le joueur qui vient de
    /// lancer, il n'a déjà plus rien, le temps que le serveur le confirme.</summary>
    public ArenaItemKind ShownCarried => IsOwner && Time.time < throwHiddenUntil ? ArenaItemKind.None : state.Value.carried;

    /// <summary>Les poings sont-ils sortis (rien de tenu, ou l'objet rangé) ? Chez le joueur, son choix à
    /// la molette, tout de suite ; ailleurs, l'état publié par le serveur.</summary>
    public bool FistsOut => IsOwner
        ? fistsSelected || ShownCarried == ArenaItemKind.None
        : SpectatorRules.FistsOut(state.Value.carried, state.Value.stowed);

    /// <summary>Ce que ce spectateur a en main, tel que cette machine le montre : rien, poings sortis.</summary>
    public ArenaItemKind ShownInHand => FistsOut ? ArenaItemKind.None : ShownCarried;

    /// <summary>Chez le joueur : tient-il sa garde (clic droit, poings sortis, ni coup ni étourdi) ? Elle
    /// part au serveur à la place de la visée, qu'un spectateur n'a pas (PlayerLocomotion) : prédite,
    /// simulée, publiée et rembobinée comme elle.</summary>
    public bool LocalGuardHeld => guarding;

    /// <summary>L'horloge de cette machine pour l'étourdissement : celle de la prédiction chez le joueur
    /// (NetworkManager.LocalTime, comme Move()), celle du serveur ailleurs.</summary>
    private double Clock
    {
        get
        {
            if (!IsSpawned || NetworkManager == null) return Time.timeAsDouble;
            return IsOwner && !IsServer ? NetworkManager.LocalTime.Time : NetworkManager.ServerTime.Time;
        }
    }

    /// <summary>Étourdi à l'instant `time` de l'horloge du serveur ? Lu par PlayerLocomotion.Move().</summary>
    public bool IsStunnedAt(double time) => SpectatorRules.IsStunned(state.Value.stunnedUntil, time);

    public bool IsStunnedNow => IsStunnedAt(Clock);

    /// <summary>Secondes d'étourdissement restantes, sur cette machine.</summary>
    public float StunRemaining => (float)System.Math.Max(0.0, state.Value.stunnedUntil - Clock);

    /// <summary>De 0 à 1, l'étourdissement montré (fondu à l'entrée et à la sortie), pour l'animation.</summary>
    public float StunVisualWeight => stunVisual;

    public bool IsPunching => Time.time < localPunchEndsAt;
    /// <summary>De l'appui au contact du coup de poing (s) : l'animation y tend le poing.</summary>
    public float PunchContactDelay => punchContactDelay;

    /// <summary>Chez le joueur : vise-t-il un lancer (clic droit, l'objet en main) ? Sa trajectoire se montre.</summary>
    public bool IsAimingThrow => aimingThrow;

    /// <summary>De 0 à 1, le bras qui s'arme pendant la visée d'un lancer (l'élan montré par l'animation).</summary>
    public float ThrowAimProgress => aimingThrow ? Mathf.Clamp01((Time.time - aimStartedAt) / Mathf.Max(0.01f, throwRaiseTime)) : 0f;

    /// <summary>La vitesse d'un objet lancé dans la direction `direction` : celle que le serveur lui donnera.</summary>
    public Vector3 ThrowVelocityAlong(Vector3 direction) => SpectatorRules.ThrowVelocity(direction, 1f, maxThrowSpeed, maxThrowSpeed);

    /// <summary>Chez le joueur : le sprint s'efface devant le coup, la visée d'un lancer et l'étourdissement
    /// (PlayerLocomotion.GateSprint).</summary>
    public bool LocalBlocksSprint => IsPunching || aimingThrow || IsStunnedNow;

    /// <summary>Chez le joueur : l'objet qu'il vise pour le ramasser, ou null.</summary>
    public ArenaItem PickupCandidate => pickupCandidate;

    /// <summary>Chez le joueur : les cailloux qui l'ont touché depuis qu'il est apparu (2026-10-27). La vue du
    /// robot touché les montre (HitIndicatorHud, qui sonde ce compte).</summary>
    public int RockHitCount => rockHitCount;

    /// <summary>Chez le joueur : d'où partait le dernier caillou qui l'a touché (le point du lancer).</summary>
    public Vector3 LastRockHitFrom => lastRockHitFrom;

    /// <summary>Les spectateurs peuvent-ils agir dans cette partie ? (réglage de l'hôte)</summary>
    public static bool InterventionsAllowed
    {
        get
        {
            RoundManager rm = RoundManager.Instance;
            return rm == null || !rm.IsSpawned || rm.Rules.spectatorInterventions;
        }
    }

    /// <summary>Peut-on lancer dans la phase en cours ?</summary>
    public static bool ThrowPhaseAllowed
    {
        get
        {
            RoundManager rm = RoundManager.Instance;
            return rm == null || !rm.IsSpawned || SpectatorRules.ThrowAllowed(rm.CurrentPhase);
        }
    }

    private SpectatorSounds Sounds => Tribune.Instance != null ? Tribune.Instance.Sounds : null;

    // ------------------------------------------------------------------
    // Cycle de vie
    // ------------------------------------------------------------------

    private void Awake()
    {
        input = GetComponent<PlayerInputReader>();
        locomotion = GetComponent<PlayerLocomotion>();
        matchState = GetComponent<PlayerMatchState>();
        cameraLook = GetComponentInChildren<PlayerCameraLook>(true);
        audioSource = GetComponent<AudioSource>();
        Transform model = transform.Find("Model");
        modelAnimator = model != null ? model.GetComponent<Animator>() : null;
        foreach (Camera c in GetComponentsInChildren<Camera>(true))
        {
            if (c.name == "Main Camera") { playerCamera = c; break; }
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!spawned.Contains(this)) spawned.Add(this);
        state.OnValueChanged += HandleStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        state.OnValueChanged -= HandleStateChanged;
        spawned.Remove(this);
        // Un spectateur qui part lâche ce qu'il tenait, là où il était.
        if (IsServer) ServerDropCarried(Vector3.zero);
        ClearHandVisual();
    }

    private void HandleStateChanged(State previous, State current)
    {
        bool pickedUp = previous.carried == ArenaItemKind.None && current.carried != ArenaItemKind.None;
        if (IsOwner)
        {
            // Ce qu'on ramasse arrive en main ; les mains vides, ce sont les poings.
            if (pickedUp) fistsSelected = false;
            if (current.carried == ArenaItemKind.None) fistsSelected = true;
        }
        if (pickedUp) OnPickUp?.Invoke();
        SpectatorSounds sounds = Sounds;
        if (sounds == null) return;
        // Catégorie B : le serveur a décidé ; chacun l'entend sur ce robot.
        if (current.stunnedUntil > previous.stunnedUntil) sounds.stunned.PlayOn(audioSource);
        if (pickedUp) sounds.pickUp.PlayOn(audioSource);
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (IsServer && serverPunchPending && Time.timeAsDouble >= serverPunchResolveAt)
        {
            serverPunchPending = false;
            ServerResolvePunch();
        }

        if (IsOwner) OwnerUpdate();
    }

    private void LateUpdate()
    {
        if (!IsSpawned) return;
        UpdateHandVisual();
        UpdateStunVisual();
        if (IsOwner) UpdateThrowArc();
    }

    // ------------------------------------------------------------------
    // Chez le joueur
    // ------------------------------------------------------------------

    private void OwnerUpdate()
    {
        bool spectating = IsSpectatorRole;
        if (!spectating || input == null || !InterventionsAllowed || IsStunnedNow || input.CurrentMode != PlayerInputReader.Mode.Gameplay)
        {
            aimingThrow = false;
            guarding = false;
            punchQueued = false;
            pickupCandidate = null;
            return;
        }

        pickupCandidate = FindPickupCandidate();
        bool holding = ShownCarried != ArenaItemKind.None;

        // La molette range l'objet tenu (les poings sortent) ou le ressort ; sans objet, les poings
        // restent (2026-10-27). Pas pendant la visée d'un lancer : on lance ou on renonce d'abord.
        bool swap = input.SwapItemPressedThisFrame;
#if UNITY_EDITOR
        if (AutopilotSwapRequested) swap = true;
        AutopilotSwapRequested = false;
#endif
        if (swap && holding && !aimingThrow)
        {
            fistsSelected = !fistsSelected;
            SelectFistsServerRpc(fistsSelected);
        }
        bool fists = FistsOut;

        bool wantsPunch = input.MeleePressedThisFrame || (fists && input.FireWasPressedThisFrame);
        bool wantsPickUp = input.InteractPressedThisFrame;
        bool firePressed = input.FireWasPressedThisFrame;
        bool aimHeld = input.AimHeld;
#if UNITY_EDITOR
        AutopilotOverride(ref wantsPunch, ref wantsPickUp, ref firePressed, ref aimHeld, holding);
#endif

        // Le lancer (2026-10-27) : le clic droit maintenu vise, et montre la trajectoire ; le clic gauche
        // lance. Relâcher le clic droit renonce.
        bool aiming = SpectatorRules.ThrowAimHeld(holding && !fists, aimHeld, ThrowPhaseAllowed, IsPunching);
        if (aiming && !aimingThrow) aimStartedAt = Time.time;
        aimingThrow = aiming;
        if (aimingThrow && firePressed)
        {
            Throw();
            aimingThrow = false;
        }

        // La garde : clic droit, les poings sortis. On n'y frappe pas ; un coup déjà parti va à son
        // terme, et la garde monte après.
        guarding = fists && aimHeld && !IsPunching;
        // Un appui pendant un coup est retenu : le suivant part à la fin du geste, de l'autre poing. Qui
        // martèle le clic enchaîne gauche, droite, gauche... En garde ou en visant un lancer, il est perdu.
        punchQueued = SpectatorRules.PunchStillWanted(punchQueued, wantsPunch, guarding, aimingThrow);
        if (punchQueued && !IsPunching)
        {
            punchQueued = false;
            Punch(fists);
        }

        if (wantsPickUp && !holding && pickupCandidate != null)
            PickUpServerRpc(new NetworkObjectReference(pickupCandidate.NetworkObject));
    }

    /// <summary>L'objet posé que vise le joueur : à portée de son œil, dans le cône du regard, sans
    /// mur entre les deux. Sur la tribune seulement : l'arène est hors de portée de toute façon.</summary>
    private ArenaItem FindPickupCandidate()
    {
        if (playerCamera == null) return null;
        candidatePositions.Clear();
        candidateItems.Clear();
        foreach (ArenaItem item in ArenaItem.Spawned)
        {
            if (item == null || !item.IsResting) continue;
            candidatePositions.Add(item.RestPosition);
            candidateItems.Add(item);
        }
        Transform eye = playerCamera.transform;
        int best = SpectatorRules.BestPickup(eye.position, eye.forward, candidatePositions, pickupRange, pickupAngle);
        if (best < 0) return null;
        ArenaItem candidate = candidateItems[best];
        return CanSee(eye.position, candidate.RestPosition, worldMask) ? candidate : null;
    }

    /// <summary>
    /// L'œil voit-il l'objet posé en `item` ? Son centre, ou un peu au-dessus : un objet tombé contre un
    /// bloc fissuré ne doit pas devenir impossible à ramasser parce que le coin du bloc cache son
    /// centre (vécu au banc à deux instances, 2026-10-13). Le même test chez le joueur et au serveur.
    /// </summary>
    public static bool CanSee(Vector3 eye, Vector3 item, LayerMask worldMask)
    {
        return !Physics.Linecast(eye, item, worldMask, QueryTriggerInteraction.Ignore)
            || !Physics.Linecast(eye, item + Vector3.up * 0.25f, worldMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>Un coup de poing : l'autre poing si le joueur relance avant la fin du geste précédent
    /// (l'enchaînement) ; un objet en main droite, il ne frappe que du gauche.</summary>
    private void Punch(bool fists)
    {
        bool right = fists && SpectatorRules.NextPunchIsRight(lastPunchRight, Time.time - lastPunchStartedAt, punchDuration, comboWindow);
        lastPunchRight = right;
        lastPunchStartedAt = Time.time;
        localPunchEndsAt = Time.time + punchDuration;
        OnPunch?.Invoke(punchDuration, right);
        Sounds?.punchSwing.PlayOn(audioSource);
        if (playerCamera == null || locomotion == null) return;
        Transform eye = playerCamera.transform;
        // Le dernier input produit avant le coup, et ceux pas encore partis avec lui : le serveur y lit
        // la garde du frappeur, qui ne frappe pas en garde (PlayerLocomotion.ServerReferenceInput).
        locomotion.SendPendingInputs();
        PunchServerRpc(eye.position, eye.forward, locomotion.ViewServerTime, right, locomotion.LastInputSequence);
    }

    private void Throw()
    {
        throwHiddenUntil = Time.time + 0.6f;
        OnThrow?.Invoke();
        Sounds?.throwSwing.PlayOn(audioSource);
        if (playerCamera == null) return;
        Transform eye = playerCamera.transform;
        ThrowServerRpc(ThrowOrigin(eye), eye.forward);
    }

    /// <summary>D'où part un objet lancé : un peu devant l'œil, du côté de la main droite.</summary>
    public static Vector3 ThrowOrigin(Transform eye)
        => eye.position + eye.forward * 0.4f + eye.right * 0.15f - eye.up * 0.1f;

    /// <summary>Chez le joueur : d'où partira son objet s'il le lance maintenant, comme le serveur le décidera
    /// (la main, ou l'œil si un mur les sépare), et dans quelle direction. Faux sans caméra.</summary>
    public bool TryGetThrowStart(out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = default;
        if (playerCamera == null) return false;
        Transform eye = playerCamera.transform;
        origin = ThrowOrigin(eye);
        direction = eye.forward;
        // La main dans un mur ou derrière le garde-corps : l'objet part de l'œil (ThrowServerRpc).
        if (Physics.Linecast(eye.position, origin, worldMask, QueryTriggerInteraction.Ignore)) origin = eye.position;
        return true;
    }

    /// <summary>Chez le joueur : la trajectoire de son lancer, qu'il voit seul pendant qu'il vise.</summary>
    private void UpdateThrowArc()
    {
        if (!aimingThrow && throwArc == null) return;
        if (throwArc == null)
        {
            var go = new GameObject("Trajectoire du lancer");
            go.transform.SetParent(transform, false);
            throwArc = go.AddComponent<ThrowArcPreview>();
            throwArc.Bind(this);
        }
    }

    // ------------------------------------------------------------------
    // Au serveur : les demandes du joueur
    // ------------------------------------------------------------------

    /// <summary>Un spectateur peut-il agir maintenant ? Spectateur, interventions permises, pas étourdi.</summary>
    private bool ServerCanAct()
        => IsSpectatorRole && InterventionsAllowed && !IsStunnedAt(NetworkManager.ServerTime.Time);

    /// <summary>L'œil du joueur tel que le serveur le connaît (posture publiée, sans lean).</summary>
    private Vector3 ServerEye => transform.position + Vector3.up * (locomotion != null ? locomotion.EyeLocal(locomotion.NetworkedStance).y : 1.65f);

    /// <summary>La molette (2026-10-27) : ranger l'objet tenu (les poings sortent) ou le ressortir. Sans
    /// objet, rien à ranger.</summary>
    [ServerRpc]
    private void SelectFistsServerRpc(bool fists)
    {
        State s = state.Value;
        if (s.carried == ArenaItemKind.None || s.stowed == fists) return;
        s.stowed = fists;
        state.Value = s;
    }

    [ServerRpc]
    private void PunchServerRpc(Vector3 origin, Vector3 direction, double viewServerTime, bool right, int inputSequence)
    {
        // Une valeur non finie ne vient que d'un client modifié : avec NaN, les tests de distance
        // seraient faux, donc passés (CLAUDE.md).
        if (!WeaponController.IsFinite(origin) || !WeaponController.IsFinite(direction) || !WeaponController.IsFinite(viewServerTime)) return;
        double now = Time.timeAsDouble;
        // Un coup par geste, en temps réel (règle des garde-fous), 15 % de marge pour la gigue.
        if (now - serverLastPunchAt < punchDuration * 0.85) return;
        if (!ServerCanAct() || direction.sqrMagnitude < 1e-4f) return;
        Vector3 offset = origin - transform.position;
        if ((origin - ServerEye).sqrMagnitude > maxOriginDistance * maxOriginDistance)
        {
            Debug.LogWarning($"[Serveur] Coup de poing rejeté : origine à {Vector3.Distance(origin, ServerEye):F1} m de l'œil (max {maxOriginDistance} m).", this);
            return;
        }
        serverLastPunchAt = now;
        // Le poing qui frappe n'est qu'une suggestion du joueur : il ne change que le geste montré.
        PunchClientRpc(right);

        serverPunchPending = true;
        serverPunchResolveAt = now + punchContactDelay;
        serverPunchOffset = offset;
        serverPunchDirection = direction.normalized;
        serverPunchViewTime = viewServerTime + punchContactDelay;
        // Borné comme celui de toute demande : jamais avant le dernier input reçu.
        serverPunchReferenceInput = locomotion != null ? locomotion.ServerReferenceInput(inputSequence) : -1;
    }

    [ClientRpc]
    private void PunchClientRpc(bool right)
    {
        if (IsOwner) return; // déjà joué à l'appui
        OnPunch?.Invoke(punchDuration, right);
        Sounds?.punchSwing.PlayOn(audioSource);
    }

    /// <summary>
    /// Le coup arrive au contact. Il touche ce qui est le plus proche devant le poing : un autre
    /// spectateur, là où le frappeur le voyait (compensation de latence, comme le taser), ou un bloc
    /// fissuré de la tribune. Rien à travers un mur.
    /// </summary>
    private void ServerResolvePunch()
    {
        if (!ServerCanAct()) return; // étourdi pendant son élan : le coup ne part pas
        // On ne frappe pas en garde : celle du frappeur à l'input qui précède son coup, que le serveur
        // connaît une fois cet input simulé (un client modifié qui frapperait garde levée ne frappe pas).
        if (locomotion != null && locomotion.ServerInputFlags(serverPunchReferenceInput, out _, out bool attackerGuard) && attackerGuard) return;

        Vector3 origin = transform.position + serverPunchOffset;
        Vector3 direction = serverPunchDirection;
        double serverNow = NetworkManager.ServerTime.Time;
        float rewind = Mathf.Clamp((float)(serverNow - serverPunchViewTime), 0f, maxRewindSeconds);
        double targetTime = serverNow - rewind;

        SpectatorController victim = null;
        float bestDistance = float.MaxValue;
        foreach (SpectatorController other in spawned)
        {
            if (other == null || other == this || !other.IsSpectatorRole || other.locomotion == null) continue;
            Vector3 feet = other.transform.position;
            float height = other.BodyHeight;
            if (other.locomotion.ServerTrySamplePose(targetTime, out PlayerLocomotion.HitboxPose pose))
            {
                feet = pose.position;
                height = other.locomotion.ControllerHeight(pose.stance.current);
            }
            if (!SpectatorRules.PunchReaches(origin, direction, punchRange, punchRadius, feet, height,
                                             SpectatorRules.BodyRadius, out float distance, out Vector3 point)) continue;
            if (distance >= bestDistance) continue;
            if (distance > 0.05f && Physics.Linecast(origin, point, worldMask, QueryTriggerInteraction.Ignore)) continue;
            victim = other;
            bestDistance = distance;
        }

        Tribune tribune = Tribune.Instance;
        float sourceDistance = float.MaxValue;
        RockSource source = tribune != null
            ? tribune.QueryRockSource(origin, direction, punchRange, punchRadius, worldMask, out sourceDistance, out _)
            : null;
        if (source != null && sourceDistance < bestDistance)
        {
            tribune.ServerHitSource(source);
            return;
        }

        if (victim == null) return;
        // Sa garde, telle que la voyait le frappeur : de face, elle arrête le coup.
        if (victim.ServerGuardsAgainst(targetTime, origin))
        {
            victim.GuardedClientRpc(direction);
            Debug.Log($"[Spectateurs] {PlayerMatchState.NameOf(victim.OwnerClientId)} pare le coup de {PlayerMatchState.NameOf(OwnerClientId)} (rewind {rewind * 1000f:F0} ms).");
            return;
        }
        victim.PunchedClientRpc();
        victim.ServerStun(punchStunDuration, direction);
        Debug.Log($"[Spectateurs] {PlayerMatchState.NameOf(OwnerClientId)} frappe {PlayerMatchState.NameOf(victim.OwnerClientId)} (rewind {rewind * 1000f:F0} ms).");
    }

    [ClientRpc]
    private void PunchedClientRpc() => Sounds?.punchHit.PlayOn(audioSource);

    /// <summary>
    /// Serveur : la garde de ce spectateur arrête-t-elle ce qui vient de `threat` (l'œil du frappeur, ou
    /// un point d'où arrive un caillou) à l'instant `time` ? Sa garde et son cap à cet instant, tirés de
    /// l'historique du rewind (ce que voyait le frappeur, comme sa position), et ses poings sortis : un
    /// objet en main, ou étourdi, il ne pare rien (SpectatorRules.GuardStops).
    /// </summary>
    private bool ServerGuardsAgainst(double time, Vector3 threat)
    {
        if (locomotion == null) return false;
        bool guard = locomotion.IsAiming;
        float yaw = transform.eulerAngles.y;
        Vector3 feet = transform.position;
        PlayerLocomotion.Stance stance = locomotion.NetworkedStance;
        if (locomotion.ServerTrySamplePose(time, out PlayerLocomotion.HitboxPose pose))
        {
            guard = pose.guard;
            yaw = pose.yaw;
            feet = pose.position;
            stance = pose.stance.current;
        }
        Vector3 eye = feet + Vector3.up * locomotion.EyeLocal(stance).y;
        return SpectatorRules.GuardStops(guard, SpectatorRules.FistsOut(state.Value.carried, state.Value.stowed),
                                         IsStunnedAt(NetworkManager.ServerTime.Time), yaw, threat - eye, guardHalfAngle);
    }

    /// <summary>La garde a arrêté un coup ou un caillou (décidé par le serveur) : chacun entend le choc
    /// sur les poings (catégorie B), le joueur sent sa vue secouée du côté du coup (C).</summary>
    [ClientRpc]
    private void GuardedClientRpc(Vector3 direction)
    {
        Sounds?.punchBlocked.PlayOn(audioSource);
        OnGuardHit?.Invoke();
        if (IsOwner && cameraLook != null)
            cameraLook.Shake(guardShake * SpectatorRules.FlinchSide(direction, transform.right), guardShakeDuration);
    }

    [ServerRpc]
    private void PickUpServerRpc(NetworkObjectReference itemReference)
    {
        if (!ServerCanAct() || state.Value.carried != ArenaItemKind.None) return;
        if (!itemReference.TryGet(out NetworkObject itemObject) || !itemObject.TryGetComponent(out ArenaItem item)) return;
        if (!item.ServerIsPickable) return;

        Vector3 eye = ServerEye;
        float reach = pickupRange + pickupTolerance;
        if ((item.RestPosition - eye).sqrMagnitude > reach * reach) return;
        if (!CanSee(eye, item.RestPosition, worldMask)) return;

        State s = state.Value;
        s.carried = item.Kind;
        s.carriedVariant = (byte)Mathf.Clamp(item.Variant, 0, 255);
        s.stowed = false; // ce qu'on ramasse arrive en main
        state.Value = s;
        item.ServerConsume();
    }

    [ServerRpc]
    private void ThrowServerRpc(Vector3 origin, Vector3 direction)
    {
        if (!WeaponController.IsFinite(origin) || !WeaponController.IsFinite(direction)) return;
        State s = state.Value;
        // Un objet rangé ne se lance pas : il faut le ressortir (la molette).
        if (!ServerCanAct() || s.carried == ArenaItemKind.None || s.stowed || !ThrowPhaseAllowed) return;
        double now = Time.timeAsDouble;
        if (now - serverLastThrowAt < throwCooldown * 0.85) return;
        // Toujours à pleine force (2026-10-27) : le joueur vise par la trajectoire montrée.
        Vector3 velocity = ThrowVelocityAlong(direction);
        if (velocity == Vector3.zero) return;

        Vector3 eye = ServerEye;
        if ((origin - eye).sqrMagnitude > maxOriginDistance * maxOriginDistance)
        {
            Debug.LogWarning($"[Serveur] Lancer rejeté : origine à {Vector3.Distance(origin, eye):F1} m de l'œil (max {maxOriginDistance} m).", this);
            return;
        }
        // La main dans un mur ou derrière le garde-corps : l'objet part de l'œil.
        if (Physics.Linecast(eye, origin, worldMask, QueryTriggerInteraction.Ignore)) origin = eye;

        Tribune tribune = Tribune.Instance;
        if (tribune == null || tribune.ItemPrefab == null) return;
        serverLastThrowAt = now;
        ArenaItem.ServerSpawn(tribune.ItemPrefab, s.carried, s.carriedVariant, origin, velocity, OwnerClientId, false);
        s.carried = ArenaItemKind.None;
        s.carriedVariant = 0;
        s.stowed = false;
        state.Value = s;
        ThrowClientRpc();
    }

    [ClientRpc]
    private void ThrowClientRpc()
    {
        if (IsOwner) return;
        OnThrow?.Invoke();
        Sounds?.throwSwing.PlayOn(audioSource);
    }

    // ------------------------------------------------------------------
    // Au serveur : ce qui arrive à ce joueur
    // ------------------------------------------------------------------

    /// <summary>Serveur : étourdit ce spectateur `duration` secondes (sauf pendant son immunité). Il
    /// lâche ce qu'il tient, poussé dans le sens du coup. Renvoie vrai s'il est étourdi.</summary>
    public bool ServerStun(float duration, Vector3 push)
    {
        if (!IsServer || !IsSpectatorRole) return false;
        State s = state.Value;
        if (!SpectatorRules.TryStun(ref s.stunnedUntil, ref s.stunImmuneUntil, NetworkManager.ServerTime.Time, duration, stunImmunity)) return false;
        state.Value = s;
        serverPunchPending = false;
        ServerDropCarried(push);
        return true;
    }

    /// <summary>Serveur : un caillou lancé de `origin` touche ce spectateur. De face, sa garde l'arrête (il se
    /// brise sur ses poings) ; sinon, il l'étourdit, et le spectateur voit d'où il est parti. Le caillou est
    /// simulé au présent : la garde aussi.</summary>
    public void ServerRockHitSpectator(Vector3 rockVelocity, Vector3 origin)
    {
        if (!IsServer || !IsSpectatorRole) return;
        if (ServerGuardsAgainst(NetworkManager.ServerTime.Time, ServerEye - rockVelocity))
        {
            GuardedClientRpc(rockVelocity);
            Debug.Log($"[Spectateurs] {PlayerMatchState.NameOf(OwnerClientId)} pare un caillou.");
            return;
        }
        // Touché : il le voit, même pendant son répit, où le caillou ne l'étourdit pas.
        RockHitClientRpc(rockVelocity, origin, false, OwnerOnly);
        if (ServerStun(rockStunDuration, rockVelocity.normalized))
            Debug.Log($"[Spectateurs] {PlayerMatchState.NameOf(OwnerClientId)} étourdi par un caillou.");
    }

    /// <summary>Serveur : un caillou lancé de `origin` touche ce DUELLISTE. Aucun dégât : sa visée est
    /// secouée, et il voit d'où le caillou est parti, chez lui seulement (catégorie C, déclenchée par le
    /// serveur).</summary>
    public void ServerRockHitDuelist(Vector3 rockVelocity, Vector3 point, Vector3 origin)
    {
        if (!IsServer || IsSpectatorRole) return;
        RockHitClientRpc(rockVelocity, origin, true, OwnerOnly);
        Debug.Log($"[Spectateurs] Caillou sur le duelliste {PlayerMatchState.NameOf(OwnerClientId)}.");
    }

    /// <summary>Une RPC pour le seul propriétaire de ce robot.</summary>
    private ClientRpcParams OwnerOnly => new ClientRpcParams
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
    };

    /// <summary>Un caillou parti de `origin` a touché ce robot (décidé par le serveur, chez le propriétaire) :
    /// sa vue le montre (HitIndicatorHud), et un duelliste (`flinch`) en a la visée secouée.</summary>
    [ClientRpc]
    private void RockHitClientRpc(Vector3 rockVelocity, Vector3 origin, bool flinch, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;
        rockHitCount++;
        lastRockHitFrom = origin;
        if (!flinch || cameraLook == null) return;
        // La visée remonte et y reste ; la vue tremble un instant du côté où va le caillou. Aucun hasard :
        // le même caillou donne toujours la même secousse (GDD § 3).
        float side = SpectatorRules.FlinchSide(rockVelocity, transform.right);
        cameraLook.AddInstantRotation(flinchPitch, 0f);
        cameraLook.Shake(flinchShake * side, flinchShakeDuration);
    }

    /// <summary>Serveur : lâche ce que tient le joueur, qui tombe devant lui (poussé de `push`), et que
    /// n'importe qui peut ramasser : c'est le vol du GDD (§ 9).</summary>
    public void ServerDropCarried(Vector3 push)
    {
        if (!IsServer) return;
        State s = state.Value;
        if (s.carried == ArenaItemKind.None) return;
        Tribune tribune = Tribune.Instance;
        if (tribune != null && tribune.ItemPrefab != null)
        {
            Vector3 start = transform.position + Vector3.up * 1.2f;
            Vector3 flat = new Vector3(push.x, 0f, push.z);
            Vector3 velocity = (flat.sqrMagnitude > 1e-4f ? flat.normalized : transform.forward) * 2f + Vector3.up * 2f;
            ArenaItem.ServerSpawn(tribune.ItemPrefab, s.carried, s.carriedVariant, start, velocity, OwnerClientId, true);
        }
        s.carried = ArenaItemKind.None;
        s.carriedVariant = 0;
        s.stowed = false;
        if (IsSpawned) state.Value = s;
    }

    /// <summary>Serveur : le joueur change de rôle. Un spectateur qui entre dans l'arène lâche ce qu'il
    /// tenait sur la tribune et n'est plus étourdi.</summary>
    public void ServerOnRoleChanged(PlayerRole role)
    {
        if (!IsServer) return;
        ServerDropCarried(Vector3.zero);
        serverPunchPending = false;
        State s = state.Value;
        if (s.stunnedUntil == 0.0 && s.stunImmuneUntil == 0.0) return;
        s.stunnedUntil = 0.0;
        s.stunImmuneUntil = 0.0;
        state.Value = s;
    }

    // ------------------------------------------------------------------
    // Ce que voient les autres (catégorie C)
    // ------------------------------------------------------------------

    /// <summary>L'objet en main droite, sur le corps du robot : les autres voient qui tient quoi. Rangé
    /// (les poings sortis), il ne se voit pas.</summary>
    private void UpdateHandVisual()
    {
        ArenaItemKind kind = ShownInHand;
        int variant = kind == ArenaItemKind.None ? -1 : CarriedVariant;
        if (kind == handKind && variant == handVariant) return;
        ClearHandVisual();
        handKind = kind;
        handVariant = variant;
        if (kind == ArenaItemKind.None || modelAnimator == null || Tribune.Instance == null) return;
        Transform hand = modelAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        handVisual = Tribune.Instance.ItemPrefab != null ? Tribune.Instance.ItemPrefab.CreateVisual(kind, variant, hand) : null;
        if (handVisual == null) return;
        handVisual.transform.localPosition = new Vector3(0f, 0.08f, 0.05f);
        handVisual.transform.localRotation = Quaternion.identity;
        // Chez le joueur, son corps n'est qu'une ombre (sa caméra est dans le crâne) : l'objet aussi.
        if (IsOwner)
        {
            foreach (Renderer r in handVisual.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }
    }

    private void ClearHandVisual()
    {
        if (handVisual != null) Destroy(handVisual);
        handVisual = null;
        handKind = ArenaItemKind.None;
        handVariant = -1;
    }

    /// <summary>Un robot étourdi grésille : des arcs autour de la tête, et il titube sur place (la
    /// couche Etourdi de l'Animator, que PlayerAnimator règle sur StunVisualWeight).</summary>
    private void UpdateStunVisual()
    {
        bool stunned = IsSpectatorRole && IsStunnedNow;
        stunVisual = Mathf.MoveTowards(stunVisual, stunned ? 1f : 0f, Time.deltaTime / 0.2f);
        // Le joueur étourdi ne voit pas les siens : sa tête est sa caméra (il a le voile de SpectatorHud).
        DrawStunArcs(IsOwner ? 0f : stunVisual);
        if (!stunned) return;
        if (IsOwner && cameraLook != null) cameraLook.Wobble(stunVisual);
    }

    /// <summary>La même décharge que le gant du taser, vue de l'autre côté (2026-10-16) : trois petits arcs
    /// sautent autour de la tête, du matériau des arcs du gant, et changent de forme quinze fois par
    /// seconde. `strength` : la part de l'étourdissement montrée (0 les cache).</summary>
    private void DrawStunArcs(float strength)
    {
        if (strength <= 0.01f || modelAnimator == null)
        {
            if (stunArcs != null) stunArcs.HideAll();
            return;
        }
        Transform head = modelAnimator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return;
        if (stunArcs == null)
        {
            Transform glove = HeldWeapons.TaserGlove(modelAnimator);
            TaserSparkFX source = glove != null ? glove.GetComponentInChildren<TaserSparkFX>(true) : null;
            if (source == null || source.ArcMaterial == null) return;
            stunArcs = new ArcLines(transform, "Arcs d'étourdissement", source.ArcMaterial, 0, new[] { 3, 3, 3 }, null);
        }

        Vector3 center = head.position + Vector3.up * 0.1f;
        uint seed = ElectricArc.Hash((uint)NetworkObjectId * 0x9e3779b9U ^ (uint)Mathf.FloorToInt(Time.time / 0.065f));
        Camera viewer = Camera.main;
        float width = Mathf.Max(0.008f, viewer != null ? Vector3.Distance(viewer.transform.position, center) * 0.0025f : 0f);
        for (int i = 0; i < stunArcs.Count; i++)
        {
            uint s = ElectricArc.Hash(seed + (uint)(i + 1) * 0x632be5abU);
            Vector3 from = ElectricArc.OnSphere(ref s);
            Vector3 to = ElectricArc.Tilt(from, 60f + 50f * ElectricArc.Next01(ref s), ref s);
            Vector3 a = center + from * 0.13f, b = center + to * 0.13f;
            // L'arc passe au-dessus du crâne, pas à travers.
            Vector3 bow = ((from + to) * 0.5f).normalized * 0.05f;
            ElectricArc.Build(a, b, 3, 0.3f, bow, ref s, stunArcs.Points(i));
            float force = strength * (0.55f + 0.45f * ElectricArc.Next01(ref s));
            stunArcs.Show(i, width, force, force);
        }
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Surface de test : étourdi jusqu'à `until` (horloge du serveur, ou temps local hors session).</summary>
    public void TestSetStunnedUntil(double until)
    {
        State s = state.Value;
        s.stunnedUntil = until;
        state.Value = s;
    }
#endif

#if UNITY_EDITOR
    // ------------------------------------------------------------------
    // BANC DE TEST — Editor uniquement : un spectateur que personne ne pilote (le clone de Multiplayer
    // Play Mode). Il marche jusqu'au bloc fissuré le plus proche, le frappe jusqu'à ce qu'il cède,
    // ramasse ce qui tombe (ou le matériel qu'il voit), et lance tout sur le duelliste, à pleine force.
    // ------------------------------------------------------------------

    public static bool AutopilotSpectator;
    private float autopilotAimUntil;

    /// <summary>La bagarre de test (2026-10-27) : Frapper, il va au contact du spectateur le plus proche et
    /// martèle le clic (l'enchaînement) ; Garder, il lui fait face, garde levée, sans bouger.</summary>
    public enum AutopilotBrawlMode { None, Punch, Guard }
    public static AutopilotBrawlMode AutopilotBrawl;

    /// <summary>Un coup de molette de test, consommé à l'image suivante.</summary>
    public static bool AutopilotSwapRequested;

    private void AutopilotOverride(ref bool punch, ref bool pickUp, ref bool firePressed, ref bool aimHeld, bool holding)
    {
        if (AutopilotBrawl != AutopilotBrawlMode.None)
        {
            AutopilotFight(ref punch, ref aimHeld);
            return;
        }
        if (!AutopilotSpectator) return;
        punch = false;
        pickUp = false;
        firePressed = false;
        aimHeld = false;
        input.AutopilotMove(Vector2.zero);
        if (!holding && pickupCandidate == null && AutopilotGoToItem()) return;
        if (holding)
        {
            // Vise le duelliste le plus proche (tir tendu), le clic droit maintenu, puis lance. Sans duelliste
            // (le banc sans duel), le spectateur le plus proche.
            PlayerLocomotion duelist = null;
            foreach (PlayerLocomotion p in PlayerLocomotion.SpawnedPlayers)
            {
                if (p != null && !p.IsSpectator && p.IsAlive) { duelist = p; break; }
            }
            if (duelist == null)
            {
                SpectatorController other = NearestOtherSpectator(out _);
                if (other != null) duelist = other.locomotion;
            }
            // Un caillou au buste ; une arme à ses pieds, pour qu'il la ramasse.
            float aimHeight = ShownCarried == ArenaItemKind.Rock ? 1.2f : 0.2f;
            if (duelist != null) AutopilotLookAt(duelist.transform.position + Vector3.up * aimHeight, maxThrowSpeed);
            // Une visée de 0,4 s, le temps que le bras s'arme et que la vue se pose, puis le lancer.
            if (!aimingThrow) autopilotAimUntil = Time.time + 0.4f;
            aimHeld = true;
            firePressed = aimingThrow && Time.time >= autopilotAimUntil;
            return;
        }
        if (pickupCandidate != null)
        {
            pickUp = true;
            return;
        }

        // Le bloc fissuré le plus proche qui peut encore donner un caillou : on y va, on le frappe.
        Tribune tribune = Tribune.Instance;
        if (tribune == null || playerCamera == null) return;
        RockSource target = null;
        float best = float.MaxValue;
        foreach (RockSource source in tribune.RockSources)
        {
            if (source == null || !tribune.SourceAvailable(source.Index)) continue;
            float d = Vector3.Distance(transform.position, source.transform.position);
            if (d < best) { best = d; target = source; }
        }
        if (target == null) return;
        Vector3 eye = playerCamera.transform.position;
        Vector3 aim = target.ClosestPoint(eye + Vector3.down * 0.9f);
        AutopilotLookAtPoint(aim);
        if (Vector3.Distance(eye, aim) > punchRange * 0.8f) input.AutopilotMove(Vector2.up);
        else punch = !IsPunching;
    }

    /// <summary>La bagarre de test : face au spectateur le plus proche, au contact pour frapper (le clic
    /// martelé, que la file des coups enchaîne), ou garde levée sur place.</summary>
    private void AutopilotFight(ref bool punch, ref bool aimHeld)
    {
        punch = false;
        aimHeld = false;
        input.AutopilotMove(Vector2.zero);
        SpectatorController target = NearestOtherSpectator(out float distance);
        if (target == null || playerCamera == null) return;
        AutopilotLookAtPoint(target.transform.position + Vector3.up * 1.4f);
        if (AutopilotBrawl == AutopilotBrawlMode.Guard)
        {
            aimHeld = true;
            return;
        }
        if (distance > 1.1f) input.AutopilotMove(Vector2.up);
        else punch = true;
    }

    private SpectatorController NearestOtherSpectator(out float distance)
    {
        SpectatorController nearest = null;
        distance = float.MaxValue;
        foreach (SpectatorController other in spawned)
        {
            if (other == null || other == this || !other.IsSpectatorRole) continue;
            float d = Vector3.Distance(transform.position, other.transform.position);
            if (d < distance) { distance = d; nearest = other; }
        }
        return nearest;
    }

    /// <summary>Le matériel ou le caillou posé le plus proche sur la tribune : on y va, et on le
    /// regarde pour le ramasser. Faux s'il n'y en a pas.</summary>
    private bool AutopilotGoToItem()
    {
        Tribune tribune = Tribune.Instance;
        if (tribune == null || playerCamera == null) return false;
        ArenaItem nearest = null;
        float best = 12f;
        foreach (ArenaItem item in ArenaItem.Spawned)
        {
            if (item == null || !item.IsResting || !tribune.IsOnTribune(item.RestPosition)) continue;
            float d = Vector3.Distance(transform.position, item.RestPosition);
            if (d < best) { best = d; nearest = item; }
        }
        if (nearest == null) return false;
        AutopilotLookAtPoint(nearest.RestPosition);
        // Jusqu'à le voir à portée ; s'il reste caché, tout contre lui.
        if (best > 1.2f || (pickupCandidate == null && best > 0.5f)) input.AutopilotMove(Vector2.up);
        return true;
    }

    private void AutopilotLookAtPoint(Vector3 point)
    {
        Vector3 to = point - playerCamera.transform.position;
        float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
        input.AutopilotLookTarget(yaw, pitch);
    }

    /// <summary>Tourne la vue du joueur de test vers `target` (avec la hausse d'un lancer à `speed`).</summary>
    private void AutopilotLookAt(Vector3 target, float speed)
    {
        if (playerCamera == null || input == null) return;
        Vector3 eye = playerCamera.transform.position;
        Vector3 to = target - eye;
        float flat = new Vector2(to.x, to.z).magnitude;
        float g = 14f;
        // Hausse du tir tendu : tan θ = (v² - √(v⁴ - g(gx² + 2yv²))) / (gx).
        float v2 = speed * speed;
        float disc = v2 * v2 - g * (g * flat * flat + 2f * to.y * v2);
        float elevation = disc >= 0f ? Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * Mathf.Max(0.1f, flat))) * Mathf.Rad2Deg : 45f;
        float wantedYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        input.AutopilotLookTarget(wantedYaw, -elevation);
    }
#endif
}
