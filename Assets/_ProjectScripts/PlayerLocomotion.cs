using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Version réseautée de PlayerLocomotion, fusionnant le pattern validé dans NetworkPlayerMovement.cs
/// (prédiction client + réconciliation serveur + interpolation spectateur) avec TOUTES les features
/// gameplay du script solo d'origine : vitesses sprint/sneak/ADS, accel/décel, gravité, et posture
/// complète Debout/Accroupi/Prone avec CanStandUp().
///
/// Répartition en 3 catégories (voir feuille de route technique, Phase 1) :
///
/// A. SIMULÉ / AUTORITAIRE (réseauté via le pattern 4-cas ci-dessous) :
///    position, vitesse, gravité, posture (Debout/Accroupi/Prone), vitesses de déplacement, yaw.
///    Le VAULT est volontairement EXCLU de cette passe (voir bloc "Vault" plus bas) : c'est un
///    mouvement scripté à durée fixe (controller.enabled = false pendant le lerp), fondamentalement
///    différent d'un Move() par frame — il aura son propre incrément réseau dédié.
///
/// B. ÉVÉNEMENTS SONORES DE GAMEPLAY (décidés par le serveur, diffusés à tous via ClientRpc) :
///    pas, ramper, changements de posture, lean start/end. Un adversaire proche doit pouvoir les
///    ENTENDRE (primauté du son selon le GDD) — donc jamais purement locaux. Les sons "pas/posture"
///    sont décidés directement par le serveur à partir de sa simulation (pas de RPC entrante, donc
///    pas d'exploit possible). Le son de lean est la seule exception : comme le lean n'a aucun effet
///    sur la simulation (juste un décalage caméra), le client demande via ServerRpc, mais la RPC
///    n'accepte QUE LeanStart/LeanEnd (garde-fou anti-triche minimal).
///
/// C. COSMÉTIQUE PUREMENT LOCAL (jamais réseauté, tourne uniquement si IsOwner) :
///    head bob, kick caméra d'atterrissage, décalage caméra du lean. Concerne uniquement la caméra
///    du propriétaire, invisible et non pertinent pour quiconque d'autre.
///
/// IMPORTANT — WeaponController doit être gardé en "IsOwner uniquement" : sans ça, chaque instance
/// de joueur sur une machine lit le même clavier/souris physique et tirerait pour tout le monde à
/// la fois dès qu'il y a 2 joueurs dans la scène.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInputReader))]
public class PlayerLocomotion : NetworkBehaviour
{
    public enum Stance { Standing, Crouching, Prone }
    public enum NoiseLevel { Silent, Quiet, Loud }

    [System.Serializable]
    public struct StanceProfile
    {
        public float controllerHeight;
        public float controllerRadius;
        public float cameraHeight; // hauteur locale du CameraPivot par rapport aux pieds
        public float moveSpeedMultiplier;
    }

    [Header("Références")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Transform leanPivot;
    [Tooltip("Transform de la capsule visuelle enfant (juste pour VOIR le joueur en jeu — n'affecte jamais la collision, qui reste gérée uniquement par le CharacterController). Assigne l'enfant 'Capsule'. Laisse vide si tu n'as pas de mesh visuel.")]
    [SerializeField] private Transform visualCapsule;

    [Header("Rotation (yaw)")]
    [Tooltip("Sensibilité horizontale de la souris pour le yaw du corps (doit correspondre à celle utilisée pour le pitch dans PlayerCameraLook). Réseauté via Move() : voir remarque dans HandleOwnerPrediction/Move.")]
    [SerializeField] private float yawSensitivity = 0.12f;

    [Header("Vitesses (m/s)")]
    [SerializeField] private float walkSpeed = 4.4f;   // marche normale
    [SerializeField] private float sneakSpeed = 2.2f;   // approche furtive : plus lente et plus discrète que la marche
    [SerializeField] private float runSpeed = 8.8f;    // course : rapide mais bruyante
    [SerializeField] private float adsSpeedMultiplier = 0.55f;
    [SerializeField] private float gravity = -18f;

    [Header("Accélération")]
    [Tooltip("m/s² quand on accélère vers une vitesse cible plus élevée.")]
    [SerializeField] private float acceleration = 60f;
    [Tooltip("m/s² quand on freine vers une vitesse cible plus basse (plus rapide que l'accélération pour rester réactif en duel).")]
    [SerializeField] private float deceleration = 80f;

    [Header("Postures")]
    [SerializeField] private StanceProfile standingProfile = new StanceProfile
    {
        controllerHeight = 1.8f, controllerRadius = 0.35f, cameraHeight = 1.65f, moveSpeedMultiplier = 1f
    };
    [SerializeField] private StanceProfile crouchingProfile = new StanceProfile
    {
        controllerHeight = 1.1f, controllerRadius = 0.35f, cameraHeight = 0.95f, moveSpeedMultiplier = 0.6f
    };
    [SerializeField] private StanceProfile proneProfile = new StanceProfile
    {
        controllerHeight = 0.5f, controllerRadius = 0.4f, cameraHeight = 0.35f, moveSpeedMultiplier = 0.25f
    };
    [SerializeField] private float stanceTransitionSpeed = 8f;

    [Header("Lean")]
    [SerializeField] private float maxLeanOffset = 0.5f;   // distance latérale max de la caméra
    [SerializeField] private float maxLeanTilt = 12f;      // inclinaison (roll) en degrés
    [SerializeField] private float leanSpeed = 10f;

    [Header("Head bob (marche/course)")]
    [Tooltip("Distance parcourue (m) pour un cycle complet de bob en marche.")]
    [SerializeField] private float walkBobCycleDistance = 1.8f;
    [SerializeField] private float walkBobAmplitude = 0.025f;
    [Tooltip("Amplitude latérale (gauche/droite) en marche.")]
    [SerializeField] private float walkBobLateralAmplitude = 0.018f;
    [Tooltip("Distance parcourue (m) pour un cycle complet de bob en sprint (plus court = bob plus rapide).")]
    [SerializeField] private float sprintBobCycleDistance = 1.4f;
    [SerializeField] private float sprintBobAmplitude = 0.05f;
    [SerializeField] private float sprintBobLateralAmplitude = 0.032f;
    [Tooltip("Vitesse de fondu (0→1) à l'entrée/sortie du bob, pour éviter un à-coup au premier pas.")]
    [SerializeField] private float bobBlendSpeed = 6f;

    [Header("Head bob (atterrissage)")]
    [SerializeField] private float landingBobFrequency = 7f; // Hz
    [SerializeField] private float landingBobAmplitude = 0.05f;
    [SerializeField] private float landingBobDecayRate = 9f;

    [Header("Atterrissage (kick caméra)")]
    [SerializeField] private float landingKickThreshold = 2f;
    [SerializeField] private float landingKickPerSpeed = 0.03f;
    [SerializeField] private float landingKickMax = 0.35f;
    [SerializeField] private float landingRecoverySpeed = 1.8f;
    [Tooltip("Dip caméra dédié à l'atterrissage d'un vault. Conservé pour quand le vault sera réintégré.")]
    [SerializeField] private float vaultLandingKick = 0.16f;

    [Header("Footsteps")]
    [SerializeField] private float walkStepDistance = 1.7f;
    [SerializeField] private float sneakStepDistance = 1.3f;
    [SerializeField] private float sprintStepDistance = 2.4f;
    [SerializeField] private float crouchStepDistance = 1.3f;
    [SerializeField] private float proneStepDistance = 0.5f;
    [SerializeField] private float minFootstepInterval = 0.22f;

    private float timeSinceLastFootstep = 10f;

    [Header("Obstacles (partagé lean / stand-up / vault)")]
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Header("Vault (désactivé le temps de sa passe réseau dédiée — voir feuille de route)")]
    [SerializeField] private float vaultCheckDistance = 0.8f;
    [SerializeField] private float vaultMinHeight = 0.3f;
    [SerializeField] private float vaultMaxHeight = 1.3f;
    [SerializeField] private float vaultLandingProbeDistance = 0.6f;
    [SerializeField] private float vaultDuration = 0.45f;
    [SerializeField] private float vaultArcHeight = 0.35f;
    [SerializeField] private AnimationCurve vaultHeightCurve = BuildDefaultVaultArc();
    [SerializeField] private bool debugDrawVaultRays = true;

    [Header("Réseau — spectateur")]
    [Tooltip("Délai volontaire (s) auquel un spectateur affiche un joueur distant, pour toujours avoir 2 points d'historique connus entre lesquels interpoler.")]
    [SerializeField] private float interpolationDelay = 0.1f;

    public Stance CurrentStance { get; private set; } = Stance.Standing;
    public NoiseLevel CurrentNoise { get; private set; } = NoiseLevel.Silent;
    public bool IsAiming { get; private set; }
    public bool IsVaulting { get; private set; } // reste toujours false tant que le vault réseau n'existe pas
    public bool IsSprinting { get; private set; }
    public bool IsSneaking { get; private set; }
    public bool IsMoving { get; private set; }

    /// <summary>Émis localement (via la ClientRpc de diffusion) pour tout son de contexte du
    /// joueur, avec le niveau de bruit au moment de l'événement. PlayerSoundEmitter n'a besoin
    /// d'aucun changement : il consomme cet event exactement comme en solo.</summary>
    public event System.Action<PlayerSoundEvent, NoiseLevel> OnPlayerSound;

    private CharacterController controller;
    private PlayerInputReader input;
    private float verticalVelocity;
    private float currentLeanOffset;
    private int leanState; // -1 = gauche, 0 = aucun, 1 = droite

    private Vector3 currentVelocity;      // vitesse horizontale lissée (monde), pour l'accel/décel
    private float currentCameraHeight;    // hauteur de base liée à la posture, sans le kick d'atterrissage
    private float landingDipOffset;       // décalage négatif temporaire appliqué par-dessus, qui remonte à 0

    private float bobPhase;
    private float bobBlend;
    private float headBobOffset;
    private float headBobLateralOffset;

    private float landingBobPhase;
    private float landingBobEnvelope;

    private float footstepDistanceAccumulator;

    // ------------------------------------------------------------------
    // Réseau — position (catégorie A)
    // ------------------------------------------------------------------

    private readonly NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<Stance> networkStance = new NetworkVariable<Stance>(
        Stance.Standing, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Yaw (degrés, monde) — écrit par le serveur juste après avoir fait autorité sur le
    /// mouvement (cas 1 et 3), lu uniquement par les purs spectateurs (cas 4) pour orienter
    /// visuellement le joueur distant. Le propriétaire et le serveur-autoritaire n'en ont pas
    /// besoin : ils dérivent déjà leur propre rotation via Move() (voir remarque plus bas).</summary>
    private readonly NetworkVariable<float> networkYaw = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private int nextInputSequence;

    private struct MovementInputSnapshot
    {
        public Vector2 move;
        public float lookX; // delta souris horizontal brut (non multiplié par yawSensitivity) — voir Move()
        public bool sprintHeld;
        public bool sneakHeld;
        public bool aimHeld;
        public bool fireHeld;
        public bool firePressedThisFrame;
    }

    private struct PendingInput
    {
        public int sequence;
        public MovementInputSnapshot snapshot;
        public float deltaTime;
    }

    // Côté propriétaire distant (cas 2) : inputs envoyés au serveur mais pas encore confirmés.
    private readonly List<PendingInput> unconfirmedInputs = new List<PendingInput>();

    // Côté serveur (cas 3) : inputs reçus des clients, en attente de traitement.
    private readonly Queue<PendingInput> serverInputQueue = new Queue<PendingInput>();

    private struct PositionSnapshot
    {
        public float time;
        public Vector3 position;
    }

    // Côté spectateur (cas 4) : petit historique récent des positions reçues.
    private readonly List<PositionSnapshot> remoteSnapshots = new List<PositionSnapshot>();

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = GetComponent<PlayerInputReader>();
        ApplyStanceImmediate(Stance.Standing);

        // Voir commentaire équivalent de l'ancienne version : évite que le joueur ne se bloque
        // lui-même sur ses propres tests de chevauchement (CheckCapsule pour CanStandUp).
        obstacleMask &= ~(1 << gameObject.layer);
    }

    public override void OnNetworkSpawn()
    {
        // Évite un "téléport" visuel au spawn pour les spectateurs.
        transform.position = networkPosition.Value;
        transform.rotation = Quaternion.Euler(0f, networkYaw.Value, 0f);
        networkPosition.OnValueChanged += HandleNetworkPositionChanged;
        ApplyStanceImmediate(networkStance.Value);

        // Caméra/AudioListener : une seule instance de joueur doit "voir" et "entendre" par
        // machine — celle du propriétaire local. Fixé EXPLICITEMENT dans les deux sens (pas
        // seulement désactivé pour les non-propriétaires) : le résultat au runtime doit être
        // déterministe quel que soit l'état par défaut laissé dans le prefab, sinon on dépend
        // silencieusement d'un réglage d'éditeur qu'on peut facilement casser plus tard.
        foreach (Camera cam in GetComponentsInChildren<Camera>(true))
        {
            cam.enabled = IsOwner;
        }
        foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true))
        {
            listener.enabled = IsOwner;
        }

        // DIAGNOSTIC TEMPORAIRE — à retirer une fois le bug des AudioListener en surnombre résolu.
        // Le message "3 audio listeners in the scene" persistant malgré le code ci-dessus veut
        // dire que le problème n'est probablement PAS dans ce Player : soit un AudioListener
        // "orphelin" hors des prefabs Player (ex. une Main Camera de scène restée de la version
        // solo, jamais touchée par GetComponentsInChildren ci-dessus puisqu'elle n'est pas un
        // enfant de CE GameObject), soit IsOwner vaut true sur plus d'une instance à la fois (bug
        // de spawn/ownership, plus grave). Ce log liste TOUS les AudioListener de la scène avec
        // leur chemin hiérarchique complet — colle-moi la sortie Console après le prochain test.
        LogSceneAudioListeners();
    }

    private void LogSceneAudioListeners()
    {
        var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[PlayerLocomotion] Spawn de {gameObject.name} (OwnerClientId={OwnerClientId}, IsOwner={IsOwner}, IsServer={IsServer}) — {listeners.Length} AudioListener(s) dans la scène :");
        foreach (AudioListener l in listeners)
        {
            sb.AppendLine($"  - {GetHierarchyPath(l.transform)} | enabled={l.enabled} | activeInHierarchy={l.gameObject.activeInHierarchy}");
        }
        Debug.Log(sb.ToString());
    }

    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    public override void OnNetworkDespawn()
    {
        networkPosition.OnValueChanged -= HandleNetworkPositionChanged;
    }

    // ------------------------------------------------------------------
    // Update — dispatch selon les 4 cas (voir en-tête de classe), puis catégorie C (owner-only),
    // puis catégorie A visuelle (posture, tourne sur toutes les instances).
    // ------------------------------------------------------------------

    private void Update()
    {
        if (IsOwner && IsServer)
        {
            // Cas 1 : Host sur son propre perso — autorité directe, comme en solo.
            MovementInputSnapshot snap = ReadLocalInput();
            float dt = Time.deltaTime;
            bool wasGrounded = controller.isGrounded;

            ServerCheckAutoStand(snap);
            Move(snap, dt);

            if (controller.isGrounded && !wasGrounded) TriggerLandingKick(Mathf.Abs(verticalVelocity));
            ServerAdvanceFootsteps(dt);
            networkPosition.Value = transform.position;
            networkYaw.Value = transform.eulerAngles.y;
        }
        else if (IsOwner)
        {
            // Cas 2 : vrai client distant sur son propre perso — prédiction + envoi au serveur.
            HandleOwnerPrediction();
        }
        else if (IsServer)
        {
            // Cas 3 : le serveur fait autorité sur le perso d'un client distant.
            ApplyBufferedServerInputs();
        }
        else
        {
            // Cas 4 : simple spectateur — interpolation visuelle uniquement.
            UpdateRemoteInterpolation();
        }

        if (IsOwner)
        {
            // Catégories B (requête de son lean) et C (feel caméra local) : lues AVANT
            // ConsumeFrameInputs, comme pour tout input "one-shot" du frame.
            HandleStanceInput();
            HandleLean();
            UpdateHeadBob();
            UpdateLandingFeedback();
            ApplyCameraPivotPosition();
        }

        // Catégorie A (posture) — effet purement dérivé de networkStance, tourne sur TOUTES les
        // instances (propriétaire, spectateurs, serveur), exactement comme validé dans le script
        // de test : chaque instance a son propre CharacterController local à faire correspondre
        // visuellement à la posture réseau.
        UpdateStanceTransition();

        if (IsOwner)
        {
            input.ConsumeFrameInputs();
        }
    }

    private MovementInputSnapshot ReadLocalInput()
    {
        return new MovementInputSnapshot
        {
            move = input.MoveInput,
            lookX = input.LookInput.x,
            sprintHeld = input.SprintHeld,
            sneakHeld = input.SneakHeld,
            aimHeld = input.AimHeld,
            fireHeld = input.FireHeld,
            firePressedThisFrame = input.FirePressedThisFrame,
        };
    }

    // ------------------------------------------------------------------
    // Cas 2 — propriétaire distant : prédiction locale + envoi au serveur
    // ------------------------------------------------------------------

    private void HandleOwnerPrediction()
    {
        MovementInputSnapshot snap = ReadLocalInput();
        float dt = Time.deltaTime;

        int sequence = nextInputSequence++;
        unconfirmedInputs.Add(new PendingInput { sequence = sequence, snapshot = snap, deltaTime = dt });

        bool wasGrounded = controller.isGrounded;
        Move(snap, dt);
        if (controller.isGrounded && !wasGrounded) TriggerLandingKick(Mathf.Abs(verticalVelocity));

        SubmitInputServerRpc(sequence, snap.move, snap.lookX, snap.sprintHeld, snap.sneakHeld, snap.aimHeld, snap.fireHeld, snap.firePressedThisFrame, dt);
    }

    [ServerRpc]
    private void SubmitInputServerRpc(int sequence, Vector2 move, float lookX, bool sprintHeld, bool sneakHeld, bool aimHeld, bool fireHeld, bool firePressedThisFrame, float deltaTime)
    {
        serverInputQueue.Enqueue(new PendingInput
        {
            sequence = sequence,
            snapshot = new MovementInputSnapshot
            {
                move = move,
                lookX = lookX,
                sprintHeld = sprintHeld,
                sneakHeld = sneakHeld,
                aimHeld = aimHeld,
                fireHeld = fireHeld,
                firePressedThisFrame = firePressedThisFrame,
            },
            deltaTime = deltaTime,
        });
    }

    // ------------------------------------------------------------------
    // Cas 3 — serveur : seule source de vérité pour le perso d'un client distant
    // ------------------------------------------------------------------

    private void ApplyBufferedServerInputs()
    {
        int lastProcessedSequence = -1;

        while (serverInputQueue.Count > 0)
        {
            PendingInput next = serverInputQueue.Dequeue();
            ServerCheckAutoStand(next.snapshot);
            Move(next.snapshot, next.deltaTime);
            ServerAdvanceFootsteps(next.deltaTime);
            lastProcessedSequence = next.sequence;
        }

        if (lastProcessedSequence >= 0)
        {
            networkPosition.Value = transform.position;
            networkYaw.Value = transform.eulerAngles.y;
            SendCorrectionToOwner(lastProcessedSequence, transform.position, verticalVelocity, transform.eulerAngles.y);
        }
    }

    private void SendCorrectionToOwner(int confirmedSequence, Vector3 confirmedPosition, float confirmedVerticalVelocity, float confirmedYaw)
    {
        var targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        ReceiveCorrectionClientRpc(confirmedSequence, confirmedPosition, confirmedVerticalVelocity, confirmedYaw, targetParams);
    }

    [ClientRpc]
    private void ReceiveCorrectionClientRpc(int confirmedSequence, Vector3 confirmedPosition, float confirmedVerticalVelocity, float confirmedYaw, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;

        unconfirmedInputs.RemoveAll(p => p.sequence <= confirmedSequence);

        // IMPORTANT : le yaw doit être recalé sur la valeur confirmée AVANT le rejeu, exactement
        // comme la position juste en dessous. Move() applique le yaw via transform.Rotate — une
        // rotation RELATIVE/cumulative. Sans ce recalage, les inputs encore non confirmés (ceux
        // qui restent dans la liste après le RemoveAll ci-dessus) se voient rejouer leur delta de
        // yaw une SECONDE fois : une fois lors de la prédiction initiale (frame par frame), une
        // fois ici. Le yaw dérivait donc en double à chaque correction serveur (fréquent sous
        // latence), uniquement pour un vrai client distant (cas 2, jamais le Host qui ne passe
        // jamais par ce chemin) — c'est ce qui donnait cette sensation de contrôle "en diagonale"
        // et des tirs partant n'importe où (aimCamera hérite de cette rotation corrompue).
        controller.enabled = false;
        transform.position = confirmedPosition;
        transform.rotation = Quaternion.Euler(0f, confirmedYaw, 0f);
        controller.enabled = true;
        verticalVelocity = confirmedVerticalVelocity;

        foreach (PendingInput pending in unconfirmedInputs)
        {
            Move(pending.snapshot, pending.deltaTime);
        }
    }

    // ------------------------------------------------------------------
    // Cas 4 — spectateur : interpolation par historique avec délai volontaire
    // ------------------------------------------------------------------

    private void HandleNetworkPositionChanged(Vector3 previous, Vector3 current)
    {
        if (IsOwner || IsServer) return;

        remoteSnapshots.Add(new PositionSnapshot { time = Time.time, position = current });

        float cutoff = Time.time - 1f;
        remoteSnapshots.RemoveAll(s => s.time < cutoff);
    }

    private void UpdateRemoteInterpolation()
    {
        // Yaw : pas d'historique nécessaire ici (contrairement à la position) — un léger à-coup
        // de rotation est bien moins perceptible qu'un à-coup de position, donc on applique
        // directement la dernière valeur connue plutôt que d'interpoler.
        transform.rotation = Quaternion.Euler(0f, networkYaw.Value, 0f);

        if (remoteSnapshots.Count == 0)
        {
            transform.position = networkPosition.Value;
            return;
        }

        float renderTime = Time.time - interpolationDelay;

        for (int i = 0; i < remoteSnapshots.Count - 1; i++)
        {
            if (remoteSnapshots[i].time <= renderTime && renderTime <= remoteSnapshots[i + 1].time)
            {
                float span = remoteSnapshots[i + 1].time - remoteSnapshots[i].time;
                float t = span > 0.0001f ? (renderTime - remoteSnapshots[i].time) / span : 0f;
                transform.position = Vector3.Lerp(remoteSnapshots[i].position, remoteSnapshots[i + 1].position, t);
                return;
            }
        }

        PositionSnapshot fallback = renderTime < remoteSnapshots[0].time
            ? remoteSnapshots[0]
            : remoteSnapshots[remoteSnapshots.Count - 1];
        transform.position = fallback.position;
    }

    // ------------------------------------------------------------------
    // Fonction de mouvement partagée — DOIT rester strictement identique entre la prédiction
    // locale, le traitement serveur et le rejeu de réconciliation. Reprend le calcul de vitesse
    // (sprint/sneak/ADS + accel/décel) de l'ancienne HandleMovement(), paramétré par un snapshot
    // d'input au lieu de lire "input" en direct, pour rester rejouable à l'identique.
    //
    // Ne déclenche AUCUN effet de bord caméra (kick d'atterrissage) ni son : ça reste catégorie C
    // / catégorie B, gérées séparément par l'appelant selon son rôle (voir Update()).
    // ------------------------------------------------------------------

    private void Move(MovementInputSnapshot snap, float dt)
    {
        // Rotation (yaw) : appliquée ICI plutôt que dans PlayerCameraLook, pour qu'elle fasse
        // partie intégrante de la fonction déterministe rejouée par la prédiction/réconciliation.
        // Rotation RELATIVE (transform.Rotate) : voir ReceiveCorrectionClientRpc pour pourquoi le
        // recalage de la rotation au yaw confirmé, avant rejeu, est indispensable avec ce choix.
        transform.Rotate(0f, snap.lookX * yawSensitivity, 0f, Space.World);

        Vector3 inputDir = new Vector3(snap.move.x, 0f, snap.move.y);
        inputDir = Vector3.ClampMagnitude(inputDir, 1f);
        Vector3 worldDir = transform.TransformDirection(inputDir);

        IsAiming = snap.aimHeld; // TODO (Phase 3, système d'armes) : brancher FOV/sway ici.

        Stance stance = networkStance.Value;
        StanceProfile profile = GetStanceProfile(stance);

        bool wantsSprint = snap.sprintHeld && stance == Stance.Standing && inputDir.z > 0.1f
            && !snap.aimHeld && !snap.fireHeld && !snap.firePressedThisFrame;
        bool wantsSneak = snap.sneakHeld && !wantsSprint;
        float baseSpeed = wantsSprint ? runSpeed : (wantsSneak ? sneakSpeed : walkSpeed);
        float targetSpeed = baseSpeed * profile.moveSpeedMultiplier;

        if (snap.aimHeld)
        {
            targetSpeed *= adsSpeedMultiplier;
        }

        Vector3 targetVelocity = worldDir * targetSpeed;
        float rate = targetVelocity.sqrMagnitude > currentVelocity.sqrMagnitude ? acceleration : deceleration;
        currentVelocity = Vector3.MoveTowards(currentVelocity, targetVelocity, rate * dt);

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -1f;
        }
        verticalVelocity += gravity * dt;

        Vector3 motion = currentVelocity;
        motion.y = verticalVelocity;
        controller.Move(motion * dt);

        IsSprinting = wantsSprint;
        IsSneaking = wantsSneak;
        IsMoving = currentVelocity.sqrMagnitude > 0.04f;
        CurrentStance = stance; // lu par WeaponController sur l'instance du propriétaire
    }

    // ------------------------------------------------------------------
    // Posture (catégorie A) — requête propriétaire → validation/confirmation serveur → effet
    // visuel partout. Volontairement PAS prédit (comme validé dans le script de test) : les
    // changements de posture sont peu fréquents, attendre la confirmation serveur reste un
    // compromis acceptable. À revoir avec prédiction si ça se sent mou en pratique.
    // ------------------------------------------------------------------

    private void HandleStanceInput()
    {
        if (input.CrouchPressedThisFrame)
        {
            Stance desired = networkStance.Value == Stance.Crouching ? Stance.Standing : Stance.Crouching;
            RequestStanceChangeServerRpc(desired);
        }

        if (input.PronePressedThisFrame)
        {
            Stance desired = networkStance.Value == Stance.Prone ? Stance.Standing : Stance.Prone;
            RequestStanceChangeServerRpc(desired);
        }

        // Le relevé automatique en sprint (Crouch/Prone + Sprint + avancer = se relève tout
        // seul) dépend d'un état MAINTENU, pas d'un appui ponctuel — il est donc évalué côté
        // serveur, à même la simulation du mouvement (ServerCheckAutoStand), plutôt qu'ici.
    }

    [ServerRpc]
    private void RequestStanceChangeServerRpc(Stance desired)
    {
        Stance current = networkStance.Value;
        if (desired == current) return;

        if (desired != Stance.Standing || CanStandUp())
        {
            networkStance.Value = desired;
            PlayerSoundEvent? sound = GetStanceSound(current, desired);
            if (sound.HasValue) BroadcastPlayerSoundClientRpc(sound.Value, ComputeNoiseLevel());
        }
    }

    /// <summary>Relevé automatique en sprint, évalué côté serveur à partir de l'input de
    /// mouvement de CE tick (déjà soumis pour le déplacement) — pas de RPC dédiée nécessaire.</summary>
    private void ServerCheckAutoStand(MovementInputSnapshot snap)
    {
        Stance current = networkStance.Value;
        if (current == Stance.Standing) return;

        if (snap.sprintHeld && snap.move.y > 0.1f && CanStandUp())
        {
            networkStance.Value = Stance.Standing;
            PlayerSoundEvent? sound = GetStanceSound(current, Stance.Standing);
            if (sound.HasValue) BroadcastPlayerSoundClientRpc(sound.Value, ComputeNoiseLevel());
        }
    }

    /// <summary>Son à jouer pour une transition de posture donnée, quelle que soit la touche qui
    /// l'a déclenchée (Crouch, Prone, ou relevé auto en sprint) : entrer dans une posture basse
    /// joue son "Down", en sortir vers Standing joue le "Up" de la posture quittée.</summary>
    private static PlayerSoundEvent? GetStanceSound(Stance from, Stance to)
    {
        if (from == to) return null;
        if (to == Stance.Crouching) return PlayerSoundEvent.CrouchDown;
        if (to == Stance.Prone) return PlayerSoundEvent.ProneDown;
        if (to == Stance.Standing && from == Stance.Crouching) return PlayerSoundEvent.CrouchUp;
        if (to == Stance.Standing && from == Stance.Prone) return PlayerSoundEvent.ProneUp;
        return null;
    }

    /// <summary>
    /// Vérifie qu'il y a assez de place au-dessus du joueur pour repasser en position debout.
    /// Utilise Physics.CheckCapsule (chevauchement statique, pas SphereCast) exactement comme
    /// avant : un SphereCast qui démarre déjà en contact avec un collider ne le détecte pas.
    /// </summary>
    private bool CanStandUp()
    {
        if (Mathf.Approximately(controller.height, standingProfile.controllerHeight)) return true;

        float radius = standingProfile.controllerRadius * 0.95f;
        Vector3 feet = transform.position + Vector3.up * radius;
        Vector3 head = transform.position + Vector3.up * (standingProfile.controllerHeight - radius);

        bool blocked = Physics.CheckCapsule(feet, head, radius, obstacleMask, QueryTriggerInteraction.Ignore);
        if (debugDrawVaultRays)
        {
            Debug.DrawLine(feet, head, blocked ? Color.red : Color.green, 0.2f);
        }
        return !blocked;
    }

    private StanceProfile GetStanceProfile(Stance stance) => stance switch
    {
        Stance.Crouching => crouchingProfile,
        Stance.Prone => proneProfile,
        _ => standingProfile,
    };

    /// <summary>Tourne sur TOUTES les instances (propriétaire, spectateurs, serveur) : lerp du
    /// CharacterController local + de la hauteur caméra + de la capsule visuelle vers le profil
    /// cible dérivé de networkStance. Chaque instance a son propre CharacterController/mesh à
    /// faire correspondre, exactement comme validé dans le script de test.</summary>
    private void UpdateStanceTransition()
    {
        StanceProfile targetProfile = GetStanceProfile(networkStance.Value);

        controller.height = Mathf.MoveTowards(controller.height, targetProfile.controllerHeight, stanceTransitionSpeed * Time.deltaTime);
        controller.radius = Mathf.MoveTowards(controller.radius, targetProfile.controllerRadius, stanceTransitionSpeed * Time.deltaTime);
        controller.center = new Vector3(0f, controller.height / 2f, 0f);

        currentCameraHeight = Mathf.MoveTowards(currentCameraHeight, targetProfile.cameraHeight, stanceTransitionSpeed * Time.deltaTime);

        ApplyVisualCapsule(controller.height, controller.radius);
    }

    private void ApplyStanceImmediate(Stance stance)
    {
        StanceProfile profile = GetStanceProfile(stance);
        controller.height = profile.controllerHeight;
        controller.radius = profile.controllerRadius;
        controller.center = new Vector3(0f, profile.controllerHeight / 2f, 0f);
        currentCameraHeight = profile.cameraHeight;
        landingDipOffset = 0f;

        if (cameraPivot != null)
        {
            Vector3 pos = cameraPivot.localPosition;
            pos.y = currentCameraHeight;
            cameraPivot.localPosition = pos;
        }

        ApplyVisualCapsule(controller.height, controller.radius);
    }

    private void ApplyVisualCapsule(float height, float radius)
    {
        if (visualCapsule == null) return;

        // Capsule primitive par défaut d'Unity : 2 unités de haut / 0.5 de rayon à l'échelle 1,
        // pivot au centre — d'où les facteurs /2 et *2. Purement cosmétique.
        visualCapsule.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
        visualCapsule.localPosition = new Vector3(0f, height / 2f, 0f);
    }

    // ------------------------------------------------------------------
    // Catégorie B — pas / ramper : décidés directement par le serveur à partir de sa propre
    // simulation (pas de RPC entrante, donc pas d'exploit possible), diffusés à tous.
    // ------------------------------------------------------------------

    private void ServerAdvanceFootsteps(float dt)
    {
        timeSinceLastFootstep += dt;

        bool eligible = controller.isGrounded && IsMoving;
        if (!eligible)
        {
            footstepDistanceAccumulator = 0f;
            return;
        }

        Stance stance = networkStance.Value;
        bool isCrawling = stance == Stance.Prone;
        float stepDistance;
        if (isCrawling) stepDistance = proneStepDistance;
        else if (IsSprinting) stepDistance = sprintStepDistance;
        else if (IsSneaking) stepDistance = sneakStepDistance;
        else if (stance == Stance.Crouching) stepDistance = crouchStepDistance;
        else stepDistance = walkStepDistance;

        float horizontalSpeed = new Vector2(currentVelocity.x, currentVelocity.z).magnitude;
        footstepDistanceAccumulator += horizontalSpeed * dt;
        if (footstepDistanceAccumulator >= stepDistance)
        {
            footstepDistanceAccumulator -= stepDistance;

            if (timeSinceLastFootstep >= minFootstepInterval)
            {
                timeSinceLastFootstep = 0f;
                BroadcastPlayerSoundClientRpc(GetFootstepSoundEvent(stance, IsSprinting), ComputeNoiseLevel());
            }
        }
    }

    /// <summary>Choisit le bon type de pas selon la posture/l'allure : PlayerSoundEvent n'a plus
    /// de type "Footstep" générique depuis son éclatement en Walk/Run/Crouch (+ Crawl en prone,
    /// déjà existant) pour permettre des clips distincts par cadence.</summary>
    private static PlayerSoundEvent GetFootstepSoundEvent(Stance stance, bool sprinting)
    {
        if (stance == Stance.Prone) return PlayerSoundEvent.Crawl;
        if (stance == Stance.Crouching) return PlayerSoundEvent.FootstepCrouch;
        return sprinting ? PlayerSoundEvent.FootstepRun : PlayerSoundEvent.FootstepWalk;
    }

    private NoiseLevel ComputeNoiseLevel()
    {
        NoiseLevel level;
        if (!IsMoving || CurrentStance == Stance.Prone) level = NoiseLevel.Silent;
        else if (IsSprinting) level = NoiseLevel.Loud;
        else if (IsSneaking) level = NoiseLevel.Silent;
        else level = NoiseLevel.Quiet;

        CurrentNoise = level;
        return level;
    }

    [ClientRpc]
    private void BroadcastPlayerSoundClientRpc(PlayerSoundEvent soundEvent, NoiseLevel noise)
    {
        OnPlayerSound?.Invoke(soundEvent, noise);
    }

    [ServerRpc]
    private void RequestPlayerSoundServerRpc(PlayerSoundEvent soundEvent)
    {
        // Seul le lean passe par ce chemin "client demande, serveur diffuse" : c'est un son
        // purement cosmétique, qui ne dérive d'aucun état simulé côté serveur (contrairement aux
        // pas/postures). Garde-fou anti-triche minimal : un client modifié ne doit pas pouvoir
        // déclencher n'importe quel son (ex. spammer de faux pas) via cette RPC.
        if (soundEvent != PlayerSoundEvent.LeanStart && soundEvent != PlayerSoundEvent.LeanEnd) return;
        BroadcastPlayerSoundClientRpc(soundEvent, ComputeNoiseLevel());
    }

    // ------------------------------------------------------------------
    // Catégorie C — cosmétique purement local, ne tourne QUE pour le propriétaire (voir Update()).
    // ------------------------------------------------------------------

    private void HandleLean()
    {
        int previousLeanState = leanState;

        if (IsSprinting)
        {
            leanState = 0;
        }
        else
        {
            if (input.LeanLeftPressedThisFrame) leanState = leanState == -1 ? 0 : -1;
            if (input.LeanRightPressedThisFrame) leanState = leanState == 1 ? 0 : 1;
        }

        if (leanState != previousLeanState)
        {
            if (leanState != 0 && previousLeanState == 0) RequestPlayerSoundServerRpc(PlayerSoundEvent.LeanStart);
            else if (leanState == 0 && previousLeanState != 0) RequestPlayerSoundServerRpc(PlayerSoundEvent.LeanEnd);
        }

        float targetOffset = leanState * maxLeanOffset;

        if (Mathf.Abs(targetOffset) > 0.01f && leanPivot != null)
        {
            Vector3 origin = cameraPivot.position;
            Vector3 dir = transform.right * Mathf.Sign(targetOffset);
            float desiredDistance = Mathf.Abs(targetOffset);

            if (Physics.Raycast(origin, dir, out RaycastHit hit, desiredDistance + 0.1f, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                float allowed = Mathf.Max(0f, hit.distance - 0.15f);
                targetOffset = Mathf.Sign(targetOffset) * allowed;
            }
        }

        currentLeanOffset = Mathf.MoveTowards(currentLeanOffset, targetOffset, leanSpeed * Time.deltaTime);

        if (leanPivot != null)
        {
            leanPivot.localPosition = new Vector3(currentLeanOffset, 0f, 0f);
            float tilt = (currentLeanOffset / maxLeanOffset) * -maxLeanTilt;
            leanPivot.localRotation = Quaternion.Euler(0f, 0f, tilt);
        }
    }

    private void TriggerLandingKick(float fallSpeed)
    {
        if (fallSpeed < landingKickThreshold) return;

        float kick = Mathf.Min((fallSpeed - landingKickThreshold) * landingKickPerSpeed, landingKickMax);
        landingDipOffset -= kick;
        StartLandingBob(Mathf.Clamp01(kick / landingKickMax));
    }

    private void UpdateLandingFeedback()
    {
        landingDipOffset = Mathf.MoveTowards(landingDipOffset, 0f, landingRecoverySpeed * Time.deltaTime);
    }

    private void StartLandingBob(float intensity)
    {
        landingBobEnvelope = landingBobAmplitude * Mathf.Clamp01(intensity);
        landingBobPhase = 0f;
    }

    private float UpdateLandingBob()
    {
        if (landingBobEnvelope <= 0.0005f)
        {
            landingBobEnvelope = 0f;
            return 0f;
        }

        landingBobPhase += landingBobFrequency * Mathf.PI * 2f * Time.deltaTime;
        landingBobEnvelope *= Mathf.Exp(-landingBobDecayRate * Time.deltaTime);
        return Mathf.Sin(landingBobPhase) * landingBobEnvelope;
    }

    private void UpdateHeadBob()
    {
        bool grounded = controller.isGrounded;
        float horizontalSpeed = new Vector2(currentVelocity.x, currentVelocity.z).magnitude;
        bool walking = grounded && IsMoving && horizontalSpeed > 0.05f && !IsVaulting && CurrentStance != Stance.Prone;

        float phaseSpeed = 0f;
        float verticalAmplitude = 0f;
        float lateralAmplitude = 0f;
        float targetBlend = 0f;

        if (walking)
        {
            float cycleDistance = IsSprinting ? sprintBobCycleDistance : walkBobCycleDistance;
            phaseSpeed = (horizontalSpeed / cycleDistance) * (Mathf.PI * 2f);
            verticalAmplitude = IsSprinting ? sprintBobAmplitude : walkBobAmplitude;
            lateralAmplitude = IsSprinting ? sprintBobLateralAmplitude : walkBobLateralAmplitude;
            targetBlend = 1f;
        }

        bobPhase += phaseSpeed * Time.deltaTime;
        bobBlend = Mathf.MoveTowards(bobBlend, targetBlend, bobBlendSpeed * Time.deltaTime);

        headBobOffset = Mathf.Sin(bobPhase) * verticalAmplitude * bobBlend + UpdateLandingBob();
        headBobLateralOffset = Mathf.Sin(bobPhase * 0.5f) * lateralAmplitude * bobBlend;
    }

    private void ApplyCameraPivotPosition()
    {
        if (cameraPivot == null) return;

        Vector3 pos = cameraPivot.localPosition;
        pos.x = headBobLateralOffset;
        pos.y = currentCameraHeight + landingDipOffset + headBobOffset;
        cameraPivot.localPosition = pos;
    }

    // ------------------------------------------------------------------
    // Vault — DÉSACTIVÉ pour cette passe réseau (voir en-tête de classe). Le code est conservé
    // tel quel (tuning inclus) pour minimiser le travail lors de son incrément réseau dédié, mais
    // HandleVaultInput/ProcessVault ne sont plus appelés depuis Update() : IsVaulting reste donc
    // toujours false. Prédire/réconcilier un vault demanderait de répliquer un état "hors
    // contrôle" (controller.enabled = false pendant le lerp), différent du pattern Move() ci-dessus.
    // ------------------------------------------------------------------

    private void HandleVaultInput()
    {
        bool canVault = TryFindVaultTarget(out Vector3 landingPoint, debugDrawVaultRays);
        if (input.JumpPressedThisFrame && canVault)
        {
            StartVault(landingPoint);
        }
    }

    private bool TryFindVaultTarget(out Vector3 landingPoint, bool drawDebug)
    {
        landingPoint = default;

        if (CurrentStance != Stance.Standing || !controller.isGrounded)
        {
            return false;
        }

        float frontProbeHeight = vaultMinHeight * 0.5f;
        Vector3 origin = transform.position + Vector3.up * frontProbeHeight;
        Vector3 forward = transform.forward;

        bool hasFrontHit = Physics.Raycast(origin, forward, out RaycastHit frontHit, vaultCheckDistance, obstacleMask, QueryTriggerInteraction.Ignore);
        if (drawDebug) Debug.DrawRay(origin, forward * vaultCheckDistance, hasFrontHit ? Color.yellow : Color.red);
        if (!hasFrontHit) return false;

        Vector3 topProbeOrigin = frontHit.point + forward * 0.15f + Vector3.up * (vaultMaxHeight + 0.3f);
        bool hasTopHit = Physics.Raycast(topProbeOrigin, Vector3.down, out RaycastHit topHit, vaultMaxHeight + 0.5f, obstacleMask, QueryTriggerInteraction.Ignore);
        if (drawDebug) Debug.DrawRay(topProbeOrigin, Vector3.down * (vaultMaxHeight + 0.5f), hasTopHit ? Color.yellow : Color.red);
        if (!hasTopHit) return false;

        float obstacleHeight = topHit.point.y - transform.position.y;
        if (obstacleHeight < vaultMinHeight || obstacleHeight > vaultMaxHeight)
        {
            if (drawDebug) Debug.DrawLine(topHit.point, topHit.point + Vector3.up * 0.3f, Color.red);
            return false;
        }

        float clearanceRadius = controller.radius * 0.9f;
        Vector3 clearancePoint = topHit.point + Vector3.up * (clearanceRadius + 0.05f);
        bool clearanceBlocked = Physics.CheckSphere(clearancePoint, clearanceRadius, obstacleMask, QueryTriggerInteraction.Ignore);
        if (drawDebug)
        {
            Debug.DrawLine(clearancePoint - Vector3.right * clearanceRadius, clearancePoint + Vector3.right * clearanceRadius, clearanceBlocked ? Color.red : Color.cyan);
            Debug.DrawLine(clearancePoint - Vector3.forward * clearanceRadius, clearancePoint + Vector3.forward * clearanceRadius, clearanceBlocked ? Color.red : Color.cyan);
        }
        if (clearanceBlocked)
        {
            return false;
        }

        landingPoint = topHit.point + forward * vaultLandingProbeDistance;
        if (drawDebug) Debug.DrawLine(topHit.point, landingPoint, Color.green);
        return true;
    }

    private float vaultTimer;
    private Vector3 vaultStart;
    private Vector3 vaultEnd;

    private void StartVault(Vector3 landingPoint)
    {
        IsVaulting = true;
        vaultTimer = 0f;
        vaultStart = transform.position;
        vaultEnd = landingPoint;
        verticalVelocity = 0f;
        currentVelocity = Vector3.zero;
        footstepDistanceAccumulator = 0f;
        controller.enabled = false;
    }

    private void ProcessVault()
    {
        vaultTimer += Time.deltaTime;
        float t = Mathf.Clamp01(vaultTimer / vaultDuration);

        Vector3 horizontal = Vector3.Lerp(vaultStart, vaultEnd, t);
        float arc = vaultHeightCurve.Evaluate(t) * vaultArcHeight;
        transform.position = horizontal + Vector3.up * arc;

        if (t >= 1f)
        {
            transform.position = vaultEnd;
            controller.enabled = true;
            IsVaulting = false;
            verticalVelocity = 0f;

            bool grounded = controller.isGrounded;
            if (grounded)
            {
                landingDipOffset -= vaultLandingKick;
                StartLandingBob(1f);
            }
        }
    }

    private static AnimationCurve BuildDefaultVaultArc()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0f)
        );
        for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
        return curve;
    }
}
