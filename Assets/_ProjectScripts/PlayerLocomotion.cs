using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Contrôleur joueur complet et réseauté : prédiction client + réconciliation serveur +
/// interpolation spectateur, par-dessus les features gameplay (vitesses sprint/sneak/ADS,
/// accel/décel, gravité, posture complète Debout/Accroupi/Prone avec CanStandUp()).
///
/// Répartition en 3 catégories (détaillée dans CLAUDE.md, à respecter pour toute nouvelle
/// mécanique joueur) :
///
/// A. SIMULÉ / AUTORITAIRE (réseauté via le pattern 4-cas ci-dessous) :
///    position, vitesse, gravité, posture (Debout/Accroupi/Prone), vitesses de déplacement, yaw.
///    Le VAULT en fait partie depuis le 2026-09-28 : son déclenchement ET son avancement vivent
///    dans Move(), donc il est prédit chez le propriétaire, rejoué à la réconciliation et identique
///    côté serveur, comme tout le reste de cette catégorie (voir le bloc "Vault" plus bas).
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

    [Tooltip("Surface TOUCHABLE du joueur (enfant 'Hitbox'), distincte du CharacterController de mouvement : elle suit la posture ET le lean, et n'est jamais désactivée par le mouvement. Sans elle, le joueur est INTOUCHABLE — le tir serveur ne cherche que ce collider.")]
    [SerializeField] private PlayerHitbox hitbox;

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
    [Tooltip("Dip caméra dédié à l'atterrissage d'un vault (cosmétique, catégorie C).")]
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

    [Header("Vault (réseauté — voir le bloc Vault plus bas)")]
    [SerializeField] private float vaultCheckDistance = 0.8f;
    [SerializeField] private float vaultMinHeight = 0.3f;
    [SerializeField] private float vaultMaxHeight = 1.3f;
    [SerializeField] private float vaultLandingProbeDistance = 0.6f;
    [SerializeField] private float vaultDuration = 0.45f;
    [SerializeField] private float vaultArcHeight = 0.35f;
    [SerializeField] private AnimationCurve vaultHeightCurve = BuildDefaultVaultArc();
    [SerializeField] private bool debugDrawVaultRays = true;

#if UNITY_EDITOR
    // Compilé UNIQUEMENT dans l'Editor : ne peut pas se retrouver dans une build, même si la case
    // reste cochée par mégarde dans le prefab. Même raisonnement que dans WeaponController.
    [Header("Debug — triche simulée (Editor uniquement, décoche après usage)")]
    [Tooltip("SIMULE UN CLIENT MODIFIÉ qui spamme la RPC d'input pour se déplacer plus vite que le serveur ne l'autorise. Sert à vérifier que le budget de temps de ApplyBufferedServerInputs tient : attendu = le joueur N'avance PAS plus vite, il se fait ramener en arrière en permanence par la réconciliation. N'a d'effet QUE sur une instance CLIENT distante (cas 2) — en Host, ton propre perso ne passe jamais par la queue serveur, donc cocher la case ne fera rien.")]
    [SerializeField] private bool debugFloodServerInputs;

    [Tooltip("Nombre d'envois de la RPC d'input par frame quand 'Debug Flood Server Inputs' est coché (1 = comportement normal). Avant le budget de temps, une valeur de 10 suffisait à traverser la carte.")]
    [SerializeField, Range(1, 30)] private int debugFloodMultiplier = 10;
#endif

    [Header("Réseau — réconciliation (client distant)")]
    [Tooltip("Écart de position (m) au-delà duquel le client se recale sur le serveur et rejoue ses inputs non confirmés. En dessous, la prédiction est jugée bonne et RIEN n'est fait — c'est ce qui évite un recalage + rejeu à chaque frame. Trop bas : corrections permanentes, saccades et coût CPU. Trop haut : le joueur peut dériver visiblement de sa position réelle côté serveur avant d'être ramené, donc se faire toucher là où il ne se voit pas. 5 cm est un bon compromis de départ.")]
    [SerializeField] private float positionReconciliationThreshold = 0.05f;

    [Tooltip("Même principe que le seuil de position, mais pour le yaw (degrés). Le yaw est piloté directement par la souris, donc client et serveur convergent très bien : une tolérance de l'ordre du degré suffit.")]
    [SerializeField] private float yawReconciliationThreshold = 1f;

    [Header("Réseau — spectateur")]
    [Tooltip("Délai volontaire (s) auquel un spectateur affiche un joueur distant, pour toujours avoir 2 points d'historique connus entre lesquels interpoler.")]
    [SerializeField] private float interpolationDelay = 0.1f;

    public Stance CurrentStance { get; private set; } = Stance.Standing;

    /// <summary>Posture telle que le SERVEUR la publie. Contrairement à CurrentStance, elle est
    /// juste sur les quatre cas réseau, y compris chez un spectateur qui n'appelle jamais Move().
    /// C'est la source à utiliser pour tout affichage (animation, capsule visible, hitbox).</summary>
    public Stance NetworkedStance => networkStance.Value;
    public NoiseLevel CurrentNoise { get; private set; } = NoiseLevel.Silent;
    public bool IsAiming { get; private set; }
    public bool IsVaulting { get; private set; } // état simulé : confirmé par le serveur, restauré avant rejeu
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

    /// <summary>Décalage latéral du lean, en mètres — écrit par le serveur. Le lean n'est plus
    /// purement cosmétique depuis le hitbox séparé : il déplace la surface touchable, donc tout le
    /// monde doit voir où penche un adversaire. Le PROPRIÉTAIRE, lui, utilise sa valeur locale
    /// prédite (voir LeanOffset) pour que sa caméra et son corps ne subissent aucune latence.</summary>
    private readonly NetworkVariable<float> networkLeanOffset = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Décalage de lean à utiliser pour positionner la surface visible et la surface
    /// touchable de CE joueur, sur CETTE machine : la valeur locale prédite chez le propriétaire,
    /// la valeur publiée par le serveur partout ailleurs (serveur compris, puisqu'il est l'auteur
    /// de cette valeur pour un client distant).</summary>
    private float LeanOffset => IsOwner ? currentLeanOffset : networkLeanOffset.Value;

    /// <summary>Collider de la surface touchable, pour que le tir serveur puisse exclure celle du
    /// tireur — et, plus tard, pour que le rewind puisse la déplacer dans le passé.</summary>
    public Collider HitboxCollider => hitbox != null ? hitbox.Collider : null;

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
        public int leanState; // -1 gauche / 0 / +1 droite — voir UpdateLeanState
        public bool jumpPressed; // déclenche un vault si un obstacle franchissable est devant
    }

    private struct PendingInput
    {
        public int sequence;
        public MovementInputSnapshot snapshot;
        public float deltaTime;

        // État que le CLIENT avait prédit juste APRÈS avoir simulé cet input. Rempli uniquement
        // côté propriétaire distant (cas 2), inutilisé côté serveur.
        //
        // C'est la clé du seuil de réconciliation : pour savoir si une prédiction était juste, il
        // faut comparer ce que le serveur confirme pour une séquence DONNÉE avec ce que le client
        // avait prédit POUR CETTE MÊME SÉQUENCE. Comparer la position ACTUELLE du client (déjà en
        // avance de plusieurs inputs) à la position confirmée (en retard d'un RTT) n'a aucun sens :
        // l'écart y est toujours grand, et un seuil dessus ne déclencherait jamais correctement.
        public Vector3 predictedPosition;
        public float predictedYaw;

        /// <summary>IsVaulting tel que le client l'avait prédit POUR CETTE SÉQUENCE. Comparer
        /// l'IsVaulting courant à la confirmation serveur était un bug : le client est en avance
        /// d'un RTT, donc il a déjà fini son franchissement quand arrivent les confirmations des
        /// séquences du milieu de l'arc. Le désaccord était alors systématique et faux.</summary>
        public bool predictedVaulting;

        /// <summary>Time.time au moment de l'envoi au serveur. Sert à mesurer le RTT GRATUITEMENT,
        /// en chronométrant l'aller-retour prédiction/réconciliation qui existe déjà : aucune RPC
        /// de ping dédiée, et aucune dépendance à la synchronisation d'horloge de Netcode.</summary>
        public float sentAt;
    }

    // Côté propriétaire distant (cas 2) : inputs envoyés au serveur mais pas encore confirmés.
    private readonly List<PendingInput> unconfirmedInputs = new List<PendingInput>();

    // ------------------------------------------------------------------
    // Compensation de latence (rewind) — voir WeaponController.FireServerRpc
    // ------------------------------------------------------------------

    [Header("Réseau — compensation de latence")]
    [Tooltip("Durée (s) de l'historique de pose conservé par le SERVEUR pour chaque joueur, afin de pouvoir le replacer dans le passé au moment de valider un tir. Doit couvrir le rewind maximum autorisé côté arme, avec de la marge.")]
    [SerializeField] private float hitboxHistoryDuration = 1f;

    /// <summary>RTT lissé du propriétaire distant, en secondes. Reste à 0 pour le Host, qui ne
    /// passe jamais par la boucle prédiction/réconciliation — cohérent avec le fait qu'il n'a
    /// aucune latence avec lui-même.</summary>
    private float smoothedRtt;

    /// <summary>
    /// Délai de rewind que CE tireur suggère au serveur. Deux termes, qui approximent ensemble
    /// l'âge de ce qu'il voit réellement à l'écran au moment où il vise :
    ///   - la moitié du RTT : le temps que son tir mette à atteindre le serveur ;
    ///   - le délai d'interpolation : les adversaires lui sont affichés volontairement en retard,
    ///     pour toujours avoir deux points d'historique entre lesquels interpoler.
    /// Vaut 0 pour le Host : il voit les autres joueurs à la position que LUI-MÊME simule (cas 3),
    /// donc sans interpolation ni latence — il n'y a rien à compenser.
    /// Le serveur ne fait jamais confiance à cette valeur : il la clampe (voir FireServerRpc).
    /// </summary>
    public float EstimatedRewindSeconds => IsServer ? 0f : smoothedRtt * 0.5f + interpolationDelay;

    /// <summary>Pose passée d'un joueur, telle que le serveur l'a simulée. Contient TOUT ce dont
    /// dépend la surface touchable — position, orientation, lean et posture. Historiser la seule
    /// position ne corrigerait qu'une part du décalage : un adversaire penché ou accroupi au moment
    /// du tir serait rewind avec la géométrie qu'il a MAINTENANT.</summary>
    public struct HitboxPose
    {
        public float time;
        public Vector3 position;
        public float yaw;
        public float leanOffset;
        public Stance stance;
    }

    // Côté serveur : historique glissant, alimenté à chaque tick où le serveur fait autorité sur
    // ce joueur (cas 1 et 3). Distinct de remoteSnapshots, qui n'existe que côté spectateur pour
    // l'affichage.
    private readonly List<HitboxPose> serverHitboxHistory = new List<HitboxPose>();

    // État du hitbox sauvegardé avant un rewind, pour pouvoir le restaurer à l'identique.
    private bool isRewound;
    private Vector3 rewindSavedLocalPosition;
    private Quaternion rewindSavedLocalRotation;

    /// <summary>Tous les joueurs actuellement spawnés. Maintenu à l'inscription/désinscription
    /// plutôt que recalculé par FindObjectsByType à chaque tir : le rewind parcourt cette liste
    /// plusieurs fois par seconde et par tireur.</summary>
    private static readonly List<PlayerLocomotion> spawnedPlayers = new List<PlayerLocomotion>();
    public static IReadOnlyList<PlayerLocomotion> SpawnedPlayers => spawnedPlayers;

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
        if (IsServer)
        {
            // Place le joueur sur un point de spawn AVANT de publier sa position : sans ça, tout
            // le monde apparaît à la position du prefab, donc les uns sur les autres.
            ServerMoveToSpawnPoint();

            // Le SERVEUR publie la position où il vient de faire apparaître ce joueur. Sans ça,
            // networkPosition vaut encore default = (0,0,0) à cet instant, et la branche cliente
            // ci-dessous téléportait tout le monde à l'origine du monde — y compris le serveur
            // lui-même, dans la version précédente qui appliquait ce recalage inconditionnellement.
            // Le commentaire d'origine disait "évite un téléport visuel au spawn" ; en pratique
            // c'est lui qui le PROVOQUAIT. Invisible tant que la scène de test spawne près de
            // l'origine, et fatal dès qu'il y aura de vrais points de spawn opposés.
            networkPosition.Value = transform.position;
            networkYaw.Value = transform.eulerAngles.y;
        }
        else
        {
            // Client/spectateur : on s'aligne sur ce que le serveur a publié (livré avec le
            // message de spawn), pour éviter une frame affichée à la position du prefab.
            controller.enabled = false;
            transform.position = networkPosition.Value;
            transform.rotation = Quaternion.Euler(0f, networkYaw.Value, 0f);
            controller.enabled = true;
        }

        networkPosition.OnValueChanged += HandleNetworkPositionChanged;
        if (!spawnedPlayers.Contains(this)) spawnedPlayers.Add(this);
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

        // Le propriétaire est À L'INTÉRIEUR de son personnage : sa caméra est à hauteur de tête,
        // donc il verrait l'intérieur du crâne. Ses renderers passent en ShadowsOnly plutôt que
        // d'être désactivés — il continue ainsi de projeter une ombre, qui est une INFORMATION DE
        // JEU et pas de la décoration : voir sa propre ombre dépasser d'un angle renseigne sur ce
        // que l'adversaire peut voir de soi. Le GDD fait de l'information un pilier.
        //
        // Ciblé sur le seul sous-arbre "Model" : une arme en vue première personne, elle, doit
        // rester visible pour son propriétaire.
        Transform modelRoot = transform.Find("Model");
        if (modelRoot != null)
        {
            var mode = IsOwner
                ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                : UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (Renderer r in modelRoot.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = mode;
            }
        }

        // Si un jour le warning "There are N audio listeners in the scene" revient : la boucle
        // ci-dessus ne voit QUE les enfants de ce GameObject. Un AudioListener orphelin posé
        // ailleurs dans la scène (typiquement une Main Camera de secours affichée avant le spawn
        // du joueur) ne sera jamais désactivé par ce code — c'est là qu'il faut chercher en
        // premier, avant de soupçonner un bug d'ownership.
    }

    public override void OnNetworkDespawn()
    {
        networkPosition.OnValueChanged -= HandleNetworkPositionChanged;
        spawnedPlayers.Remove(this);
    }

#if UNITY_EDITOR
    // ------------------------------------------------------------------
    // Diagnostics de session — Editor uniquement, jamais embarqué en build.
    //
    // Mesure la SANTÉ DE LA PRÉDICTION, qui ne se voit pas à l'œil nu : un client dont la
    // prédiction est juste ne déclenche presque aucun resync. Un taux élevé signale une divergence
    // entre la simulation locale et celle du serveur — exactement la classe de bug qui a coûté le
    // plus cher ici (yaw, currentVelocity, capsule).
    //
    // Tourne uniquement chez le PROPRIÉTAIRE DISTANT : c'est le seul qui prédit.
    // ------------------------------------------------------------------
    private int diagCorrections;
    private int diagResyncs;

    // CUMULATIFS sur toute la session, volontairement PAS remis à zéro à chaque fenêtre : un
    // franchissement et son kick d'atterrissage peuvent tomber de part et d'autre d'une bordure
    // de fenêtre, ce qui rendrait le rapprochement des deux illisible.
    //
    // Ces deux compteurs existent parce que le test du 2026-09-29 a révélé un angle mort : un
    // taux de resync à 0 % pendant un vault est indiscernable d'un vault qui n'a jamais eu lieu.
    // Les lire ensemble tranche les deux questions d'un coup — vaults > 0 prouve que le
    // franchissement s'est produit, et kicks == vaults prouve que le kick caméra ne se rejoue
    // plus (c'est lui qui faisait saccader la caméra, jusqu'à une vingtaine de fois par vault).
    private int diagVaultsTotal;
    private int diagLandingKicksTotal;
    private float diagErrorSum;
    private float diagMaxError;
    private float diagNextReportAt;

    private void DiagRecordCorrection(bool resynced, float positionError)
    {
        diagCorrections++;
        if (resynced) diagResyncs++;
        diagErrorSum += positionError;
        if (positionError > diagMaxError) diagMaxError = positionError;
    }

    private void DiagReportIfDue()
    {
        if (!IsOwner || IsServer) return;
        if (Time.time < diagNextReportAt) return;

        diagNextReportAt = Time.time + 3f;
        if (diagCorrections == 0) return;

        float resyncRate = 100f * diagResyncs / diagCorrections;
        float avgError = diagErrorSum / diagCorrections;

        Debug.Log($"[DIAG-CLIENT] corrections={diagCorrections} resync={diagResyncs} ({resyncRate:F1}%) " +
                  $"erreur_moy={avgError * 100f:F1}cm erreur_max={diagMaxError * 100f:F1}cm " +
                  $"vaults={diagVaultsTotal} kicks={diagLandingKicksTotal} " +
                  $"rtt={smoothedRtt * 1000f:F0}ms rewind={EstimatedRewindSeconds * 1000f:F0}ms " +
                  $"file_serveur={serverInputQueue.Count}");

        diagCorrections = 0;
        diagResyncs = 0;
        diagErrorSum = 0f;
        diagMaxError = 0f;
    }
#endif

    // ------------------------------------------------------------------
    // Rewind — historique serveur et replacement temporaire du hitbox
    // ------------------------------------------------------------------

    /// <summary>Enregistre la pose courante dans l'historique serveur. Appelée à chaque tick où le
    /// serveur fait autorité sur ce joueur (cas 1 et 3).</summary>
    private void ServerRecordHitboxPose()
    {
        serverHitboxHistory.Add(new HitboxPose
        {
            time = Time.time,
            position = transform.position,
            yaw = transform.eulerAngles.y,
            leanOffset = LeanOffset,
            stance = networkStance.Value,
        });

        float cutoff = Time.time - hitboxHistoryDuration;
        // RemoveAll plutôt qu'une file : l'historique est court (~60 entrées) et on a besoin d'un
        // accès indexé pour interpoler entre deux poses.
        serverHitboxHistory.RemoveAll(p => p.time < cutoff);
    }

    /// <summary>
    /// Replace TEMPORAIREMENT la surface touchable de ce joueur là où elle était à l'instant
    /// demandé. Ne touche ni au transform du joueur ni à son CharacterController : la simulation
    /// en cours continue sur les vraies positions, seul le collider de tir voyage dans le passé.
    /// C'est précisément ce que le hitbox séparé rend possible.
    /// </summary>
    public void ServerBeginRewind(float targetTime)
    {
        if (!IsServer || isRewound || hitbox == null) return;
        if (serverHitboxHistory.Count == 0) return;

        if (!TrySampleHistory(targetTime, out HitboxPose pose)) return;

        Transform t = hitbox.transform;
        rewindSavedLocalPosition = t.localPosition;
        rewindSavedLocalRotation = t.localRotation;
        isRewound = true;

        // Position et orientation MONDE : le hitbox est un enfant, mais on le sort volontairement
        // de la pose de son parent le temps du tir.
        t.SetPositionAndRotation(pose.position, Quaternion.Euler(0f, pose.yaw, 0f));

        StanceProfile profile = GetStanceProfile(pose.stance);
        hitbox.Apply(profile.controllerHeight, profile.controllerRadius, pose.leanOffset);
    }

    /// <summary>Remet la surface touchable dans sa pose courante. TOUJOURS appelée en finally par
    /// l'appelant : un hitbox laissé dans le passé rendrait le joueur intouchable, ou touchable au
    /// mauvais endroit, pour le reste de la partie.</summary>
    public void ServerEndRewind()
    {
        if (!isRewound || hitbox == null) return;

        Transform t = hitbox.transform;
        t.localPosition = rewindSavedLocalPosition;
        t.localRotation = rewindSavedLocalRotation;
        isRewound = false;

        // Les dimensions sont recalculées au prochain UpdateStanceVisuals (fonction pure de la
        // posture courante), mais on les remet tout de suite pour qu'une requête intermédiaire ne
        // voie pas la géométrie du passé.
        StanceProfile profile = GetStanceProfile(networkStance.Value);
        hitbox.Apply(profile.controllerHeight, profile.controllerRadius, LeanOffset);
    }

    private bool TrySampleHistory(float targetTime, out HitboxPose result)
        => SampleHitboxHistory(serverHitboxHistory, targetTime, out result);

    /// <summary>
    /// Interpole la pose historique à l'instant demandé. Renvoie false si l'historique est vide ;
    /// se rabat sur la pose la plus ancienne/récente si l'instant sort de sa fenêtre.
    ///
    /// Fonction PURE et statique, séparée de l'état du composant exprès : c'est le cœur du rewind,
    /// donc l'endroit où une régression coûterait le plus cher, et sous cette forme elle se teste
    /// sans Editor, sans réseau et sans scène (voir les tests EditMode).
    /// </summary>
    public static bool SampleHitboxHistory(IReadOnlyList<HitboxPose> history, float targetTime, out HitboxPose result)
    {
        result = default;
        if (history == null || history.Count == 0) return false;

        if (targetTime <= history[0].time)
        {
            result = history[0];
            return true;
        }

        for (int i = 0; i < history.Count - 1; i++)
        {
            HitboxPose a = history[i];
            HitboxPose b = history[i + 1];
            if (a.time > targetTime || targetTime > b.time) continue;

            float span = b.time - a.time;
            float t = span > 0.0001f ? (targetTime - a.time) / span : 0f;

            result = new HitboxPose
            {
                time = targetTime,
                position = Vector3.Lerp(a.position, b.position, t),
                yaw = Mathf.LerpAngle(a.yaw, b.yaw, t),
                leanOffset = Mathf.Lerp(a.leanOffset, b.leanOffset, t),
                // La posture est discrète : on garde celle d'AVANT plutôt que d'inventer un état
                // intermédiaire. Pendant une transition, le joueur est donc rewind avec la capsule
                // qu'il quittait — le choix conservateur du point de vue de la cible.
                stance = a.stance,
            };
            return true;
        }

        result = history[history.Count - 1];
        return true;
    }

    /// <summary>
    /// Replace ce joueur sur un point de spawn, côté SERVEUR uniquement, et publie le résultat.
    /// Appelée au spawn, et prévue pour l'être aussi à chaque manche par la future boucle de round.
    /// Sans `PlayerSpawnPoints` dans la scène, ne fait rien : le joueur garde la position que lui a
    /// donnée le NetworkManager.
    /// </summary>
    public void ServerMoveToSpawnPoint()
    {
        if (!IsServer) return;

        PlayerSpawnPoints points = PlayerSpawnPoints.Instance;
        if (points == null) return;

        // Positions des autres joueurs DÉJÀ présents, pour ne pas faire apparaître le nouveau venu
        // dans les pieds de son adversaire. Le serveur spawne les joueurs séquentiellement, donc le
        // second voit bien le premier.
        var occupied = new List<Vector3>();
        foreach (PlayerLocomotion other in FindObjectsByType<PlayerLocomotion>(FindObjectsSortMode.None))
        {
            if (other == this) continue;
            occupied.Add(other.transform.position);
        }

        if (!points.TryGetSpawnPointFarthestFrom(occupied, out Vector3 spawnPosition, out float spawnYaw)) return;

        // Le CharacterController doit être désactivé pour un repositionnement direct, sinon il
        // écrase la nouvelle position au Move() suivant (même raison que dans la réconciliation).
        controller.enabled = false;
        transform.position = spawnPosition;
        transform.rotation = Quaternion.Euler(0f, spawnYaw, 0f);
        controller.enabled = true;

        // Remise à zéro de l'inertie : réapparaître avec la vitesse accumulée avant la mort ferait
        // glisser le joueur au début de la manche suivante.
        currentVelocity = Vector3.zero;
        verticalVelocity = 0f;

        // Remise à zéro de la POSTURE et du LEAN. Sans ça, on reprend la manche suivante dans
        // l'état où on est mort : penché derrière un angle qui n'existe plus, ou allongé en plein
        // milieu. La posture vient d'une NetworkVariable, donc la remettre ici suffit.
        networkStance.Value = Stance.Standing;
        ApplyStanceImmediate(Stance.Standing);

        currentLeanOffset = 0f;
        networkLeanOffset.Value = 0f;
        leanState = 0;

        // Le LEAN demande en plus une RPC vers le propriétaire, contrairement à tout le reste.
        // Son ÉTAT (-1/0/+1) est une bascule qui vit sur le client : remettre le décalage à zéro
        // côté serveur ne suffirait pas, le propriétaire se repencherait dès la frame suivante
        // puisque sa touche est toujours considérée comme enclenchée.
        ResetLeanClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        });

        networkPosition.Value = transform.position;
        networkYaw.Value = transform.eulerAngles.y;
    }

    /// <summary>Annule le lean chez le propriétaire — état, décalage et effet caméra. Envoyée par
    /// le serveur au début de chaque manche (voir ServerMoveToSpawnPoint).</summary>
    [ClientRpc]
    private void ResetLeanClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;

        leanState = 0;
        currentLeanOffset = 0f;

        if (leanPivot != null)
        {
            leanPivot.localPosition = Vector3.zero;
            leanPivot.localRotation = Quaternion.identity;
        }
    }

    // ------------------------------------------------------------------
    // Update — dispatch selon les 4 cas (voir en-tête de classe), puis catégorie C (owner-only),
    // puis catégorie A visuelle (posture, tourne sur toutes les instances).
    // ------------------------------------------------------------------

    private void Update()
    {
        // L'état de lean (-1/0/+1) est lu AVANT le dispatch : il fait maintenant partie du
        // snapshot d'input envoyé au serveur, puisque le hitbox doit suivre le lean. Le reste du
        // lean (décalage caméra, anti-clipping) reste cosmétique et local, plus bas.
        if (IsOwner) UpdateLeanState();

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
            ServerRecordHitboxPose();
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
            // Catégorie C (feel caméra local) : lue AVANT ConsumeFrameInputs, comme tout input
            // "one-shot" du frame.
            HandleStanceInput();
            UpdateLeanVisual();
            UpdateHeadBob();
            UpdateLandingFeedback();

            if (vaultJustLanded)
            {
                vaultJustLanded = false;
#if UNITY_EDITOR
                diagLandingKicksTotal++;
#endif
                landingDipOffset -= vaultLandingKick;
                StartLandingBob(1f);
            }

            ApplyCameraPivotPosition();

            // Le Host publie son propre décalage de lean : il est à la fois propriétaire et
            // serveur, donc sa valeur locale EST la valeur autoritaire.
            if (IsServer) networkLeanOffset.Value = currentLeanOffset;
        }

        // Habillage de la posture (hauteur caméra) + surface visible + surface touchable — tourne
        // sur TOUTES les instances, y compris les spectateurs qui n'appellent jamais Move().
        // La capsule de COLLISION, elle, est appliquée dans Move() : c'est de la simulation.
        UpdateStanceVisuals();

        if (IsOwner)
        {
            input.ConsumeFrameInputs();
        }

#if UNITY_EDITOR
        DiagReportIfDue();
#endif
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
            leanState = leanState,
            jumpPressed = input.JumpPressedThisFrame,
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

        bool wasGrounded = controller.isGrounded;
        Move(snap, dt);
        if (controller.isGrounded && !wasGrounded) TriggerLandingKick(Mathf.Abs(verticalVelocity));

        // L'input est enregistré APRÈS Move() : on a besoin de l'état prédit qui en résulte pour
        // pouvoir le comparer plus tard à ce que le serveur confirmera pour cette même séquence
        // (voir PendingInput et ReceiveCorrectionClientRpc).
        unconfirmedInputs.Add(new PendingInput
        {
            sequence = sequence,
            snapshot = snap,
            deltaTime = dt,
            predictedPosition = transform.position,
            predictedYaw = transform.eulerAngles.y,
            predictedVaulting = IsVaulting,
            sentAt = Time.time,
        });

        SubmitInputServerRpc(sequence, snap.move, snap.lookX, snap.sprintHeld, snap.sneakHeld, snap.aimHeld, snap.fireHeld, snap.firePressedThisFrame, snap.leanState, snap.jumpPressed, dt);

#if UNITY_EDITOR
        // Triche simulée : on renvoie le MÊME input plusieurs fois, avec des numéros de séquence
        // distincts pour qu'il passe pour du trafic légitime. Chaque copie est individuellement
        // plausible (même dt qu'un vrai frame) — c'est précisément ce qui rendait cette attaque
        // invisible pour l'ancien garde-fou, qui ne bornait que le dt d'un input isolé et le total
        // par FRAME serveur (contournable en gardant la queue pleine, voir ApplyBufferedServerInputs).
        //
        // Note : les copies ne sont volontairement PAS ajoutées à unconfirmedInputs — le client ne
        // les a pas prédites. La réconciliation devient donc franchement brutale dès que le serveur
        // confirme une de ces séquences, ce qui est exactement l'effet observable recherché.
        if (debugFloodServerInputs)
        {
            for (int i = 1; i < debugFloodMultiplier; i++)
            {
                SubmitInputServerRpc(nextInputSequence++, snap.move, snap.lookX, snap.sprintHeld, snap.sneakHeld, snap.aimHeld, snap.fireHeld, snap.firePressedThisFrame, snap.leanState, snap.jumpPressed, dt);
            }
        }
#endif
    }

    [ServerRpc]
    private void SubmitInputServerRpc(int sequence, Vector2 move, float lookX, bool sprintHeld, bool sneakHeld, bool aimHeld, bool fireHeld, bool firePressedThisFrame, int leanState, bool jumpPressed, float deltaTime)
    {
        // Garde-fou anti-flood (mémoire) : les ServerRpc sont livrées en Reliable, donc TOUT ce
        // qu'un client envoie finit par arriver. Sans plafond, un client qui spamme cette RPC fait
        // grossir la queue indéfiniment — la mémoire du serveur monte, et le retard de simulation
        // de CE joueur s'allonge sans jamais se résorber (chaque Update() serveur n'en consomme
        // qu'une fraction, voir le budget dans ApplyBufferedServerInputs). On jette donc les inputs
        // excédentaires plutôt que de les accumuler : un client légitime n'atteint jamais ce
        // plafond (MaxQueuedInputs ≈ 2 s de jeu à 60 FPS), et un client qui le dépasse se pénalise
        // lui-même par une réconciliation plus brutale — ce qui est exactement le comportement voulu.
        if (serverInputQueue.Count >= MaxQueuedInputs) return;

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
                leanState = leanState,
                jumpPressed = jumpPressed,
            },
            deltaTime = deltaTime,
        });
    }

    // ------------------------------------------------------------------
    // Cas 3 — serveur : seule source de vérité pour le perso d'un client distant
    // ------------------------------------------------------------------

    // Garde-fous anti-triche sur les inputs reçus du client (voir ApplyBufferedServerInputs) :
    // rien ne garantit que le deltaTime envoyé par SubmitInputServerRpc reflète le temps
    // RÉELLEMENT écoulé côté client — un client modifié pourrait en envoyer un artificiellement
    // grand pour parcourir une distance disproportionnée en un seul Move(), ou en spammer des
    // milliers avec un deltaTime individuellement plausible pour obtenir le même résultat par
    // accumulation.
    //
    // MaxSingleInputDeltaTime traite le PREMIER cas (un dt aberrant isolé). Le SECOND cas (le
    // flood) demande une borne sur le temps simulé accordé par unité de temps RÉEL, pas par
    // frame : une borne par frame se contourne trivialement en gardant la queue pleine, puisque
    // le serveur tourne à ~60 frames par seconde (l'ancienne borne de 0,5 s/frame autorisait
    // ainsi ~30 s de mouvement simulé par seconde réelle, soit un speedhack ×30). D'où le budget
    // ci-dessous, rechargé au rythme du temps réel.
    private const float MaxSingleInputDeltaTime = 0.1f; // ~3 ticks à 30Hz : couvre un vrai freeze client sans laisser passer un dt aberrant.

    // Budget de temps simulé (en secondes) accordé au client, rechargé de Time.deltaTime à chaque
    // Update() serveur. Le point clé : pour un client LÉGITIME, la somme des deltaTime envoyés par
    // seconde vaut toujours ≈ 1 seconde, quel que soit son framerate (60 FPS = 60 inputs de 16 ms,
    // 144 FPS = 144 inputs de 7 ms — même total). Le budget ne pénalise donc jamais un gros
    // framerate, seulement un client qui prétend avoir vécu plus de temps qu'il n'en est passé.
    private const float InputTimeBudgetRefillFactor = 1.1f;  // 10% de marge pour la gigue réseau/frame.
    private const float MaxInputTimeBudget = 0.25f;          // réserve max : permet un vrai rattrapage après un à-coup réseau, sans plus.
    private const int MaxQueuedInputs = 120;                 // ≈ 2 s de jeu à 60 FPS — voir SubmitInputServerRpc.

    private float serverInputTimeBudget;

    private void ApplyBufferedServerInputs()
    {
        int lastProcessedSequence = -1;

        serverInputTimeBudget = Mathf.Min(
            serverInputTimeBudget + Time.deltaTime * InputTimeBudgetRefillFactor,
            MaxInputTimeBudget);

        while (serverInputQueue.Count > 0)
        {
            // Peek plutôt que Dequeue : si le budget ne couvre pas cet input EN ENTIER, on le
            // laisse dans la queue pour le prochain Update() au lieu de le tronquer. Simuler un
            // input avec un dt partiel casserait la réconciliation (le client, lui, a prédit avec
            // le dt complet) — exactement le genre de divergence que Move() doit éviter.
            PendingInput next = serverInputQueue.Peek();
            float dt = Mathf.Clamp(next.deltaTime, 0f, MaxSingleInputDeltaTime);
            if (dt > serverInputTimeBudget) break;

            serverInputQueue.Dequeue();
            serverInputTimeBudget -= dt;

            ServerCheckAutoStand(next.snapshot);
            Move(next.snapshot, dt);
            ServerAdvanceFootsteps(dt);
            ServerAdvanceLean(next.snapshot.leanState, dt);
            lastProcessedSequence = next.sequence;
        }

        // Le reste éventuel de la queue (flood ou vrai gros rattrapage) attend le prochain
        // Update() serveur plutôt que d'être traité d'un coup — voir commentaire ci-dessus.

        if (lastProcessedSequence >= 0)
        {
            networkPosition.Value = transform.position;
            networkYaw.Value = transform.eulerAngles.y;
            SendCorrectionToOwner(lastProcessedSequence, transform.position, verticalVelocity, transform.eulerAngles.y, currentVelocity, IsVaulting, vaultTimer, vaultStart, vaultEnd);
        }

        // Enregistré à CHAQUE frame serveur, même sans input traité : sinon l'historique aurait des
        // trous pendant les micro-coupures réseau, précisément quand le rewind sert le plus.
        ServerRecordHitboxPose();
    }

    private void SendCorrectionToOwner(int confirmedSequence, Vector3 confirmedPosition, float confirmedVerticalVelocity, float confirmedYaw, Vector3 confirmedHorizontalVelocity, bool confirmedVaulting, float confirmedVaultTimer, Vector3 confirmedVaultStart, Vector3 confirmedVaultEnd)
    {
        var targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        ReceiveCorrectionClientRpc(confirmedSequence, confirmedPosition, confirmedVerticalVelocity, confirmedYaw, confirmedHorizontalVelocity, confirmedVaulting, confirmedVaultTimer, confirmedVaultStart, confirmedVaultEnd, targetParams);
    }

    [ClientRpc]
    private void ReceiveCorrectionClientRpc(int confirmedSequence, Vector3 confirmedPosition, float confirmedVerticalVelocity, float confirmedYaw, Vector3 confirmedHorizontalVelocity, bool confirmedVaulting, float confirmedVaultTimer, Vector3 confirmedVaultStart, Vector3 confirmedVaultEnd, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;

        // Le serveur envoie une correction à CHAQUE frame où il a traité des inputs, c'est-à-dire
        // en permanence. Sans le seuil ci-dessous, chaque correction déclenchait un recalage + le
        // rejeu de TOUS les inputs non confirmés : à 100 ms de RTT, ça représentait ~10 Move()
        // supplémentaires par frame, soit plusieurs centaines de sweeps physiques par seconde,
        // uniquement pour aboutir au même résultat. Et le toggle controller.enabled ci-dessous
        // remettait isGrounded à false à chaque fois, faisant clignoter la détection de sol.
        //
        // On ne se recale donc QUE si la prédiction était réellement fausse. Pour le savoir, il
        // faut comparer ce que le serveur confirme avec ce que le client avait prédit POUR LA MÊME
        // SÉQUENCE — d'où predictedPosition/predictedYaw stockés dans PendingInput. Comparer avec
        // la position actuelle du client n'aurait aucun sens : elle est en avance de tout le RTT.
        int predictionIndex = unconfirmedInputs.FindIndex(p => p.sequence == confirmedSequence);
        bool predictionWasCorrect = false;

        if (predictionIndex >= 0)
        {
            PendingInput predicted = unconfirmedInputs[predictionIndex];
            float positionError = Vector3.Distance(predicted.predictedPosition, confirmedPosition);
            float yawError = Mathf.Abs(Mathf.DeltaAngle(predicted.predictedYaw, confirmedYaw));
            // Un désaccord sur le FAIT d'être en train de franchir n'est jamais tolérable, même
            // si les positions coïncident : les deux côtés ne simuleraient plus la même chose au
            // pas suivant.
            predictionWasCorrect = positionError <= positionReconciliationThreshold
                && yawError <= yawReconciliationThreshold
                && predicted.predictedVaulting == confirmedVaulting;

            // RTT mesuré GRATUITEMENT : cet input est parti à `sentAt` et sa confirmation arrive
            // maintenant. Aucune RPC de ping dédiée, aucune dépendance à la synchronisation
            // d'horloge de Netcode — juste le chronométrage d'un aller-retour déjà présent dans
            // l'architecture. Lissé pour qu'un pic isolé ne fasse pas sauter le rewind d'un coup.
            //
            // Inclut le temps d'attente de l'input dans la queue serveur, donc surestime un peu le
            // RTT pur. C'est le bon biais ici : ce qu'on cherche à mesurer n'est pas la latence
            // réseau théorique, mais l'âge réel de ce que le tireur voit à l'écran.
            float measuredRtt = Time.time - predicted.sentAt;
            smoothedRtt = smoothedRtt <= 0f ? measuredRtt : Mathf.Lerp(smoothedRtt, measuredRtt, 0.1f);

#if UNITY_EDITOR
            DiagRecordCorrection(!predictionWasCorrect, positionError);
#endif
        }
        // Si la séquence est introuvable (correction périmée, ou input jeté par le plafond de
        // serverInputQueue), on ne peut rien conclure : on retombe sur le recalage systématique,
        // qui reste le comportement SÛR. Mieux vaut un à-coup qu'une désynchronisation silencieuse.

        unconfirmedInputs.RemoveAll(p => p.sequence <= confirmedSequence);

        if (predictionWasCorrect)
        {
            return;
        }

        // IMPORTANT : le yaw doit être recalé sur la valeur confirmée AVANT le rejeu, exactement
        // comme la position. Move() applique le yaw via transform.Rotate — une rotation
        // RELATIVE/cumulative. Sans ce recalage, les inputs encore non confirmés se voient rejouer
        // leur delta de yaw une SECONDE fois : une fois lors de la prédiction initiale, une fois
        // ici. Le yaw dérivait donc en double à chaque correction serveur — c'est ce qui donnait
        // cette sensation de contrôle "en diagonale" et des tirs partant n'importe où.
        //
        // currentVelocity suit la MÊME règle et pour la MÊME raison : c'est une valeur cumulative
        // (MoveTowards depuis sa propre valeur précédente). Sans recalage, le rejeu repartait de la
        // vitesse lissée du client au lieu de celle du serveur, ce qui garantissait un désaccord
        // permanent pendant chaque phase d'accélération/décélération — donc des corrections en
        // boucle. Règle générale : toute valeur cumulative lue par Move() doit avoir son équivalent
        // confirmé, renvoyé par le serveur et appliqué ici avant le rejeu.
        controller.enabled = false;
        transform.position = confirmedPosition;
        transform.rotation = Quaternion.Euler(0f, confirmedYaw, 0f);
        controller.enabled = true;
        verticalVelocity = confirmedVerticalVelocity;
        currentVelocity = confirmedHorizontalVelocity;
        RestoreVaultState(confirmedVaulting, confirmedVaultTimer, confirmedVaultStart, confirmedVaultEnd);

        isReplayingInputs = true;
        try
        {
            for (int i = 0; i < unconfirmedInputs.Count; i++)
            {
                PendingInput pending = unconfirmedInputs[i];
                Move(pending.snapshot, pending.deltaTime);

                // Le rejeu vient de produire un NOUVEL état prédit pour cet input, différent de celui
                // calculé lors de la prédiction initiale puisqu'on est reparti d'une base corrigée.
                // Il faut le réenregistrer, sinon la prochaine correction comparerait la confirmation
                // du serveur à une prédiction périmée — et le seuil déclencherait n'importe comment.
                // (PendingInput est une struct : il faut réécrire l'élément dans la liste.)
                pending.predictedPosition = transform.position;
                pending.predictedYaw = transform.eulerAngles.y;
                pending.predictedVaulting = IsVaulting;
                unconfirmedInputs[i] = pending;
            }
        }
        finally
        {
            // Sans finally, une exception en plein rejeu laisserait le drapeau levé et le kick
            // d'atterrissage serait muet pour le reste de la partie.
            isReplayingInputs = false;
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

        // VAULT — traité ICI, dans la fonction déterministe, et pas dans Update() comme avant la
        // passe réseau. C'est ce qui le rend prédictible et rejouable comme le reste.
        //
        // Le franchissement est un mouvement scripté à durée fixe : tant qu'il dure, il REMPLACE
        // le déplacement normal (d'où le return). Ses points de départ et d'arrivée ne sont PAS
        // réseautés : ils sont recalculés de part et d'autre par TryFindVaultTarget, qui ne dépend
        // que de la position, de l'orientation, de la posture et de la géométrie STATIQUE du
        // monde — tout ce qui est déjà déterministe. Réseauter ce que les deux côtés savent
        // calculer serait du poids inutile dans la RPC de correction.
        if (IsVaulting)
        {
            AdvanceVault(dt);
            return;
        }

        // Un vault ne démarre que pendant une manche : sinon on franchirait un obstacle pendant
        // le décompte, alors que le déplacement normal est gelé.
        if (snap.jumpPressed && RoundManager.MovementAllowed && TryFindVaultTarget(out Vector3 vaultLanding, false))
        {
            BeginVault(vaultLanding);
            AdvanceVault(dt);
            return;
        }

        // Hors manche (décompte de départ, mort, fin de match), le déplacement est gelé pour que
        // les deux joueurs repartent exactement en même temps. On ne coupe QUE le déplacement :
        // regarder autour de soi et se pré-positionner visuellement reste permis.
        //
        // Appliqué ici, dans la fonction déterministe, donc identiquement côté client et serveur :
        // un client modifié qui ignorerait le gel se ferait recaler, puisque le serveur applique le
        // même test sur sa propre simulation.
        Vector2 moveInput = RoundManager.MovementAllowed ? snap.move : Vector2.zero;

        Vector3 inputDir = new Vector3(moveInput.x, 0f, moveInput.y);
        inputDir = Vector3.ClampMagnitude(inputDir, 1f);
        Vector3 worldDir = transform.TransformDirection(inputDir);

        IsAiming = snap.aimHeld; // TODO (avec le vrai système d'armes) : brancher FOV/sway ici.

        Stance stance = networkStance.Value;
        StanceProfile profile = GetStanceProfile(stance);

        // Capsule de collision appliquée ICI, dans la fonction déterministe, et pas seulement
        // dans Update() : un rejeu de réconciliation tourne en dehors d'Update (depuis la
        // ClientRpc de correction), et doit donc reconstituer lui-même la bonne capsule avant
        // d'appeler controller.Move(). Voir ApplySimulationCapsule.
        ApplySimulationCapsule(stance);

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

        // 🚨 CurrentStance n'est JUSTE que là où Move() tourne, c'est-à-dire chez le propriétaire
        // et sur le serveur. Un spectateur n'appelle jamais Move() : la valeur y reste figée sur
        // celle de l'Awake. Tout ce qui doit connaître la posture sur TOUTES les instances doit
        // lire NetworkedStance ci-dessous, pas cette propriété.
    }

    // ------------------------------------------------------------------
    // Posture (catégorie A) — requête propriétaire → validation/confirmation serveur → effet
    // visuel partout. Volontairement PAS prédit (compromis assumé) : les
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
    public static PlayerSoundEvent? GetStanceSound(Stance from, Stance to)
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

    /// <summary>
    /// CAPSULE DE COLLISION — catégorie A (simulation). Fonction PURE de networkStance : aucune
    /// interpolation, aucun état cumulatif, donc strictement identique partout dès que la
    /// NetworkVariable de posture a la même valeur.
    ///
    /// C'était le dernier trou de déterminisme de Move(). Avant, les dimensions du
    /// CharacterController étaient interpolées dans Update() avec le Time.deltaTime LOCAL de
    /// chaque instance : pendant une transition de posture, le client et le serveur ne simulaient
    /// donc pas avec la même capsule, donc pas avec la même collision — ce qui violait le contrat
    /// "Move() doit rester strictement déterministe" écrit en en-tête de classe, et produisait des
    /// recalages que le seuil de réconciliation ne pouvait pas absorber (la divergence était
    /// RÉELLE, pas du bruit).
    ///
    /// Le choix retenu est de rendre la collision INSTANTANÉE plutôt que de réseauter une hauteur
    /// interpolée : ça supprime l'état cumulatif au lieu d'en ajouter un de plus à confirmer dans
    /// la RPC de correction. Ce que le joueur RESSENT (hauteur de caméra) et ce qu'il VOIT
    /// (capsule visuelle) restent interpolés en douceur, eux — voir UpdateStanceVisuals().
    /// Se relever reste protégé par CanStandUp(), donc le passage instantané à la capsule debout
    /// ne peut pas faire traverser un plafond.
    /// </summary>
    private void ApplySimulationCapsule(Stance stance)
    {
        StanceProfile profile = GetStanceProfile(stance);

        // Court-circuit : écrire height/radius force PhysX à reconstruire le collider, inutile de
        // le faire à chaque frame alors que la posture ne change que rarement.
        if (Mathf.Approximately(controller.height, profile.controllerHeight)
            && Mathf.Approximately(controller.radius, profile.controllerRadius))
        {
            return;
        }

        controller.height = profile.controllerHeight;
        controller.radius = profile.controllerRadius;
        controller.center = new Vector3(0f, profile.controllerHeight / 2f, 0f);
    }

    /// <summary>
    /// Habillage de la posture — catégorie C/visuel. Tourne sur TOUTES les instances (y compris
    /// les spectateurs, qui n'appellent jamais Move()) et peut donc utiliser Time.deltaTime sans
    /// risque : rien ici n'influence la simulation.
    ///
    /// La capsule visuelle a désormais ses propres dimensions, distinctes de celles du
    /// CharacterController : ce dernier passe instantanément d'une posture à l'autre, le mesh
    /// continue de glisser. Bref décalage assumé entre ce qu'on voit et ce qui entre en collision
    /// pendant une transition (~0,1 s) — invisible en pratique, et le prix d'une simulation
    /// réellement déterministe.
    /// </summary>
    private void UpdateStanceVisuals()
    {
        Stance stance = networkStance.Value;
        StanceProfile profile = GetStanceProfile(stance);

        // SEULE la hauteur caméra est interpolée : c'est le point de vue du propriétaire, et c'est
        // ce lissage-là qui donne le confort de s'accroupir. Purement local, sans conséquence.
        currentCameraHeight = Mathf.MoveTowards(currentCameraHeight, profile.cameraHeight, stanceTransitionSpeed * Time.deltaTime);

        // Le CORPS, lui, ne s'interpole plus : surface visible et surface touchable sont toutes
        // deux une fonction PURE de (posture réseau, décalage de lean), donc identiques sur toutes
        // les machines. C'est ce qui garantit qu'on touche ce qu'on voit.
        //
        // Un lissage local des dimensions du corps ferait diverger la silhouette d'un écran à
        // l'autre pendant chaque transition : le tireur viserait un corps que le serveur n'a pas
        // au même endroit. Le corps qui "claque" d'une posture à l'autre est le prix assumé tant
        // que la capsule est un placeholder ; un vrai personnage animé réglera ça par l'animation,
        // avec un hitbox qui suivra les os.
        float lean = LeanOffset;
        ApplyVisualCapsule(profile.controllerHeight, profile.controllerRadius, lean);
        ApplyHitbox(profile.controllerHeight, profile.controllerRadius, lean);

        // Les spectateurs n'appellent jamais Move() : sans ça, leur CharacterController local
        // garderait la capsule de la posture précédente.
        ApplySimulationCapsule(stance);
    }

    /// <summary>Met la surface touchable en accord avec la posture et le lean. Tourne sur toutes
    /// les instances, mais seule celle du SERVEUR décide des dégâts.</summary>
    private void ApplyHitbox(float height, float radius, float lateralOffset)
    {
        if (hitbox == null) return;
        hitbox.Apply(height, radius, lateralOffset);
    }

    private void ApplyStanceImmediate(Stance stance)
    {
        StanceProfile profile = GetStanceProfile(stance);
        ApplySimulationCapsule(stance);
        currentCameraHeight = profile.cameraHeight;
        landingDipOffset = 0f;

        if (cameraPivot != null)
        {
            Vector3 pos = cameraPivot.localPosition;
            pos.y = currentCameraHeight;
            cameraPivot.localPosition = pos;
        }

        ApplyVisualCapsule(profile.controllerHeight, profile.controllerRadius, currentLeanOffset);
        ApplyHitbox(profile.controllerHeight, profile.controllerRadius, currentLeanOffset);
    }

    private void ApplyVisualCapsule(float height, float radius, float lateralOffset)
    {
        if (visualCapsule == null) return;

        // Capsule primitive par défaut d'Unity : 2 unités de haut / 0.5 de rayon à l'échelle 1,
        // pivot au centre — d'où les facteurs /2 et *2.
        visualCapsule.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);

        // Le corps visible se décale avec le lean, exactement comme le hitbox : sans ça, un
        // adversaire penché serait touchable à un endroit où on ne le voit pas, ce qui serait un
        // trou d'exactitude symétrique de celui qu'on vient de fermer.
        visualCapsule.localPosition = new Vector3(lateralOffset, height / 2f, 0f);
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
    public static PlayerSoundEvent GetFootstepSoundEvent(Stance stance, bool sprinting)
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

    /// <summary>
    /// Bascule l'ÉTAT de lean (-1 gauche / 0 / +1 droite) à partir de l'input. Propriétaire
    /// uniquement, appelée en tête d'Update pour que l'état parte dans le snapshot du frame.
    ///
    /// Depuis le hitbox séparé, le lean n'est plus purement cosmétique : il déplace la surface
    /// touchable, donc le serveur doit le connaître. L'état voyage dans le snapshot d'input
    /// existant — aucune RPC supplémentaire, et un client ne peut mentir que sur son propre lean,
    /// ce qui ne lui donne aucun avantage (se pencher expose son flanc).
    /// </summary>
    private void UpdateLeanState()
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
    }

    /// <summary>
    /// Décalage de lean effectivement autorisé pour un état donné, une fois l'anti-clipping
    /// appliqué : se pencher contre un mur ne doit pas passer la tête à travers.
    /// Partagée entre le propriétaire (prédiction locale, pour le feel caméra) et le serveur
    /// (valeur autoritaire, celle qui déplace le hitbox).
    /// </summary>
    private float ComputeAllowedLeanOffset(int state)
    {
        float targetOffset = state * maxLeanOffset;
        if (Mathf.Abs(targetOffset) <= 0.01f || cameraPivot == null) return targetOffset;

        Vector3 origin = cameraPivot.position;
        Vector3 dir = transform.right * Mathf.Sign(targetOffset);
        float desiredDistance = Mathf.Abs(targetOffset);

        if (Physics.Raycast(origin, dir, out RaycastHit hit, desiredDistance + 0.1f, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            float allowed = Mathf.Max(0f, hit.distance - 0.15f);
            targetOffset = Mathf.Sign(targetOffset) * allowed;
        }

        return targetOffset;
    }

    /// <summary>Effet caméra du lean — catégorie C, propriétaire uniquement. Le décalage résultant
    /// sert AUSSI à positionner la surface visible et la surface touchable du propriétaire, pour
    /// qu'il voie son propre corps là où les autres le voient.</summary>
    private void UpdateLeanVisual()
    {
        float targetOffset = ComputeAllowedLeanOffset(leanState);
        currentLeanOffset = Mathf.MoveTowards(currentLeanOffset, targetOffset, leanSpeed * Time.deltaTime);

        if (leanPivot != null)
        {
            leanPivot.localPosition = new Vector3(currentLeanOffset, 0f, 0f);
            float tilt = (currentLeanOffset / maxLeanOffset) * -maxLeanTilt;
            leanPivot.localRotation = Quaternion.Euler(0f, 0f, tilt);
        }
    }

    /// <summary>
    /// Avance le lean AUTORITAIRE pour un client distant (cas 3), à partir de l'état reçu dans son
    /// snapshot d'input et du dt de ce même input. C'est cette valeur qui déplace son hitbox côté
    /// serveur, donc celle qui décide s'il est touché quand il peek.
    ///
    /// Limite connue : ce décalage est en retard d'environ un RTT sur ce que le tireur voit à
    /// l'écran, comme l'était la position avant le rewind. Le rewind devra donc historiser le lean
    /// en même temps que la position, sous peine de ne corriger qu'une moitié du problème.
    /// </summary>
    private void ServerAdvanceLean(int state, float dt)
    {
        float targetOffset = ComputeAllowedLeanOffset(state);
        currentLeanOffset = Mathf.MoveTowards(currentLeanOffset, targetOffset, leanSpeed * dt);
        networkLeanOffset.Value = currentLeanOffset;
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
    // Vault — réseauté depuis le 2026-09-28. Le déclenchement et l'avancement vivent dans Move()
    // (fonction déterministe), pas dans Update() : c'est ce qui le rend prédit, rejouable et
    // identique côté serveur. Ne restent ici que la RECHERCHE de cible et les données de tuning.
    // ------------------------------------------------------------------

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

    /// <summary>Levé par AdvanceVault à l'atterrissage, consommé par Update() chez le propriétaire.
    /// Le kick caméra est cosmétique : le déclencher depuis Move() le rejouerait à chaque
    /// réconciliation.</summary>
    private bool vaultJustLanded;

    /// <summary>Vrai pendant le rejeu de réconciliation. Un rejeu RE-SIMULE des inputs déjà
    /// prédits : tout effet cosmétique déclenché depuis Move() doit s'y taire, sinon il se
    /// rejoue autant de fois que le client se resynchronise.</summary>
    private bool isReplayingInputs;

    private void BeginVault(Vector3 landingPoint)
    {
#if UNITY_EDITOR
        if (!isReplayingInputs) diagVaultsTotal++;
#endif
        IsVaulting = true;
        vaultTimer = 0f;
        vaultStart = transform.position;
        vaultEnd = landingPoint;
        verticalVelocity = 0f;
        currentVelocity = Vector3.zero;
        footstepDistanceAccumulator = 0f;
    }

    /// <summary>
    /// Avance le franchissement d'un pas de simulation. Appelée UNIQUEMENT depuis Move(), donc avec
    /// le dt de l'input traité et non Time.deltaTime : c'est ce qui permet à un rejeu de
    /// réconciliation de reproduire exactement la même trajectoire.
    ///
    /// Le CharacterController est coupé pendant l'arc — c'est tout l'intérêt du vault, franchir un
    /// obstacle que la collision refuserait. Son état est piloté ici comme une fonction de
    /// IsVaulting, jamais laissé à la charge d'un appelant : un controller resté désactivé
    /// figerait le joueur pour le reste de la partie.
    ///
    /// Depuis le hitbox séparé, couper ce collider n'a plus aucun effet sur la capacité à être
    /// touché — c'était la condition qui bloquait la réactivation du vault.
    /// </summary>
    private void AdvanceVault(float dt)
    {
        vaultTimer += dt;
        float t = Mathf.Clamp01(vaultTimer / vaultDuration);

        Vector3 horizontal = Vector3.Lerp(vaultStart, vaultEnd, t);
        float arc = vaultHeightCurve.Evaluate(t) * vaultArcHeight;

        controller.enabled = false;
        transform.position = horizontal + Vector3.up * arc;

        if (t < 1f) return;

        transform.position = vaultEnd;
        controller.enabled = true;
        IsVaulting = false;
        verticalVelocity = 0f;

        // Le kick caméra d'atterrissage est de la catégorie C : il ne doit surtout pas être
        // appliqué depuis Move(), qui est rejouée. On lève un drapeau que Update() consommera,
        // côté propriétaire uniquement.
        //
        // Le drapeau NE SUFFIT PAS à lui seul : il est levé depuis Move(), donc un rejeu qui
        // retraverse l'atterrissage le relève. Mesuré le 2026-09-29 à 318 ms de RTT : jusqu'à
        // une vingtaine de kicks empilés pour un seul franchissement, d'où une caméra qui
        // saccade violemment à la réception. Le rejeu re-simule du DÉJÀ prédit, donc du déjà
        // ressenti : il doit rester muet.
        if (!isReplayingInputs) vaultJustLanded = true;
    }

    /// <summary>Remet le vault à zéro depuis une valeur confirmée par le serveur, avant un rejeu.
    /// Même règle que le yaw et currentVelocity : tout état cumulatif lu par Move() doit pouvoir
    /// être recalé, sinon il dérive à chaque correction.</summary>
    private void RestoreVaultState(bool vaulting, float timer, Vector3 start, Vector3 end)
    {
        IsVaulting = vaulting;
        vaultTimer = timer;
        vaultStart = start;
        vaultEnd = end;
        controller.enabled = !vaulting;
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    // ------------------------------------------------------------------
    // SURFACE DE TEST — jamais embarquée dans une build de jeu.
    //
    // La garde couvre UNITY_INCLUDE_TESTS en plus d'UNITY_EDITOR : l'assembly de test PlayMode
    // ne peut pas être Editor-only (Unity classerait ses tests en EditMode et ne les
    // découvrirait jamais), donc elle compile aussi dans une build "avec tests". Sans ce second
    // symbole, cette build casserait à la compilation.
    //
    // Ouverte pour une seule raison : vérifier automatiquement que Move() est déterministe, ce
    // qui était jusqu'ici la garantie la plus précieuse du projet ET la seule vérifiée à la main.
    // Même principe que SampleHitboxHistory, extraite en fonction pure pour être testable : on
    // ouvre le strict nécessaire, et on dit pourquoi.
    // ------------------------------------------------------------------

    /// <summary>
    /// Photo de TOUT l'état cumulatif lu par Move().
    ///
    /// 🚨 Cette structure est un INVARIANT, pas un utilitaire de test. Toute valeur cumulative
    /// lue par Move() doit y figurer — c'est la même liste que celle des valeurs qui doivent
    /// avoir un équivalent confirmé par le serveur dans la RPC de correction. Le projet s'est
    /// fait piéger QUATRE fois sur cette règle (yaw, currentVelocity, hauteur de capsule, état
    /// de vault) ; le test de déterminisme échoue désormais si une cinquième est oubliée, parce
    /// qu'une restauration incomplète fait diverger le rejeu.
    /// </summary>
    public struct SimulationState
    {
        public Vector3 position;
        public Quaternion rotation;
        public float verticalVelocity;
        public Vector3 currentVelocity;
        public bool vaulting;
        public float vaultTimer;
        public Vector3 vaultStart;
        public Vector3 vaultEnd;
    }

    public SimulationState TestCaptureState()
    {
        return new SimulationState
        {
            position = transform.position,
            rotation = transform.rotation,
            verticalVelocity = verticalVelocity,
            currentVelocity = currentVelocity,
            vaulting = IsVaulting,
            vaultTimer = vaultTimer,
            vaultStart = vaultStart,
            vaultEnd = vaultEnd,
        };
    }

    public void TestRestoreState(SimulationState state)
    {
        // Le controller doit être coupé pour téléporter : sinon il réapplique sa propre
        // résolution de collision et la position restaurée n'est plus exactement celle demandée.
        bool wasEnabled = controller.enabled;
        controller.enabled = false;
        transform.position = state.position;
        transform.rotation = state.rotation;
        controller.enabled = wasEnabled;

        verticalVelocity = state.verticalVelocity;
        currentVelocity = state.currentVelocity;
        RestoreVaultState(state.vaulting, state.vaultTimer, state.vaultStart, state.vaultEnd);

        // La scène physique doit voir la nouvelle position AVANT le prochain Move(), sinon le
        // premier sweep part de l'ancienne (autoSyncTransforms vaut false dans ce projet).
        Physics.SyncTransforms();
    }

    /// <summary>Appelle Move() avec un input construit de toutes pièces, sans passer par le
    /// clavier ni par le réseau.</summary>
    public void TestMove(Vector2 move, float lookX, bool sprintHeld, bool sneakHeld, bool aimHeld, bool jumpPressed, int leanState, float dt)
    {
        Move(new MovementInputSnapshot
        {
            move = move,
            lookX = lookX,
            sprintHeld = sprintHeld,
            sneakHeld = sneakHeld,
            aimHeld = aimHeld,
            fireHeld = false,
            firePressedThisFrame = false,
            leanState = leanState,
            jumpPressed = jumpPressed,
        }, dt);
    }
#endif

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
