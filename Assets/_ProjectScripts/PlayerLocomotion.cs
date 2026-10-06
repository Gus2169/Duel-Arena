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
///    ENTENDRE (primauté du son selon le GDD) — donc jamais purement locaux. TOUS sont décidés par
///    le serveur à partir de sa propre simulation, sans aucune RPC de son entrante : le lean y
///    compris depuis le 2026-10-05, puisque son état voyage déjà dans chaque input.
///
/// C. COSMÉTIQUE PUREMENT LOCAL (jamais réseauté, tourne uniquement si IsOwner) :
///    head bob, kick caméra d'atterrissage, inclinaison caméra du lean. Concerne uniquement la
///    caméra du propriétaire, invisible et non pertinent pour quiconque d'autre. Le DÉCALAGE du
///    lean, lui, n'est plus cosmétique : il déplace la surface touchable (catégorie A).
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
    /// <summary>Bruit d'un déplacement. Faint = sneak et ramper : faibles mais jamais muets.
    /// Jamais sérialisé dans un asset, seulement transmis par RPC entre deux instances du même
    /// build : l'ordre peut donc suivre la logique plutôt que l'historique.</summary>
    public enum NoiseLevel { Silent, Faint, Quiet, Loud }

    /// <summary>
    /// Posture publiée par le serveur, AVEC sa transition : la posture d'arrivée, celle de départ,
    /// et l'instant du changement sur l'horloge du serveur (NetworkManager.ServerTime).
    ///
    /// Cet instant suffit à reconstituer la transition partout sans rien réseauter de plus : chaque
    /// machine calcule la progression pour l'instant qu'ELLE considère — le présent pour le
    /// serveur, le passé interpolé pour un spectateur, un instant passé pour le rewind. C'est ce
    /// qui garde d'accord, pendant une transition, le corps visible, la surface touchable et le
    /// rewind (2026-10-06).
    /// </summary>
    public struct StanceState : INetworkSerializeByMemcpy
    {
        public Stance current;
        public Stance previous;
        public double changedAt;
    }

    /// <summary>
    /// Pose d'AFFICHAGE publiée par le serveur à chaque frame, DATÉE sur son horloge.
    ///
    /// Les spectateurs datent chaque pose de l'instant où le serveur l'a produite, et non plus de
    /// son arrivée chez eux. C'est ce qui rend leur interpolation régulière : mesuré le 2026-10-06
    /// avec l'ancienne datation à l'arrivée, l'adversaire avançait par bonds (jusqu'à 14 cm en une
    /// frame pour 2,5 cm en moyenne), parce que la gigue du réseau se lisait comme des variations
    /// de vitesse. La rotation, le lean et les drapeaux d'animation voyagent dans la même pose :
    /// ils étaient appliqués dès réception, 100 ms avant la position qu'ils accompagnent, et la
    /// rotation n'avançait que par crans de 33 ms.
    /// </summary>
    public struct DisplayPose : INetworkSerializeByMemcpy
    {
        public double time;
        public Vector3 position;
        public float yaw;
        public float leanOffset;
        public byte flags;       // DisplayFlags
        public byte teleportId;  // change à chaque téléportation : l'interpolation repart de zéro
    }

    [System.Flags]
    private enum DisplayFlags : byte
    {
        Grounded = 1,
        Vaulting = 2,
        Aiming = 4,
    }

    [System.Serializable]
    public struct StanceProfile
    {
        public float controllerHeight;
        public float controllerRadius;

        [Tooltip("Hauteur de l'ŒIL (la caméra) au-dessus des pieds. La tête touchable est centrée dessus : on voit depuis l'endroit où l'on peut être touché.")]
        public float cameraHeight;

        [Tooltip("Avancée de l'œil devant la racine, en mètres. Calée sur la tête du modèle dans cette posture (accroupi, la tête est en avant ; allongé, bien plus). L'œil doit rester à l'intérieur du rayon du CharacterController, sinon la caméra passerait à travers un mur face auquel on se tient.")]
        public float cameraForward;

        [Tooltip("Décalage latéral de l'œil, en mètres (+ = droite). Nul debout et accroupi ; allongé, la pose penche la tête sur la crosse, et l'œil suit la tête visible.")]
        public float cameraSide;

        public float moveSpeedMultiplier;
    }

    [Header("Références")]
    [SerializeField] private Transform cameraPivot;

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
    // Yeux calés sur la tête du robot le 2026-10-05 (voir BodyLayout) : accroupi et allongé, la
    // caméra était 10 à 50 cm à côté de la tête visible, donc on pouvait être touché à la tête
    // là où l'on ne voyait rien.
    [SerializeField] private StanceProfile standingProfile = new StanceProfile
    {
        controllerHeight = 1.8f, controllerRadius = 0.35f, cameraHeight = 1.65f, cameraForward = 0f, moveSpeedMultiplier = 1f
    };
    [SerializeField] private StanceProfile crouchingProfile = new StanceProfile
    {
        controllerHeight = 1.1f, controllerRadius = 0.35f, cameraHeight = 1.05f, cameraForward = 0.10f, moveSpeedMultiplier = 0.6f
    };
    [SerializeField] private StanceProfile proneProfile = new StanceProfile
    {
        controllerHeight = 0.5f, controllerRadius = 0.4f, cameraHeight = 0.36f, cameraForward = 0.25f, cameraSide = -0.15f, moveSpeedMultiplier = 0.25f
    };

    [Header("Transitions de posture (ressenti, à régler en jouant)")]
    [Tooltip("Durée (s) entre debout et accroupi, dans les deux sens. Le corps visible, la caméra, la surface touchable et la vitesse de déplacement passent ensemble d'une posture à l'autre sur cette durée.")]
    [SerializeField] private float standCrouchTransition = 0.3f;
    [Tooltip("Durée (s) entre accroupi et allongé. L'animation de transition (genou à terre ↔ allongé) est lue à la vitesse qui la fait tenir dans cette durée.")]
    [SerializeField] private float crouchProneTransition = 0.65f;
    [Tooltip("Durée (s) entre debout et allongé. L'animation y ajoute le temps de poser un genou (0,25 s) à la précédente : garder à peu près cet écart, sinon le geste paraîtra pressé ou en retard.")]
    [SerializeField] private float standProneTransition = 0.9f;

    [Header("Lean (façon Rainbow Six : seul le buste se penche)")]
    [Tooltip("Décalage latéral maximal de l'œil, en mètres. 0,35 m, plus proche de R6 que l'ancien 0,5 m (tranché le 2026-10-05). Réglage de ressenti.")]
    [SerializeField] private float maxLeanOffset = 0.35f;
    [Tooltip("Angle maximal dont le buste s'incline. Accroupi, le buste est plus court : sans cette borne, il faudrait s'y plier à près de 50° pour sortir la tête d'autant que debout.")]
    [SerializeField] private float maxLeanAngle = 40f;
    [SerializeField] private float maxLeanTilt = 12f;      // roulis de la CAMÉRA en degrés, pas celui du buste
    [Tooltip("Temps d'amortissement (s) du buste qui s'incline ou se redresse : départ et arrivée en douceur, sans rebond. Le buste arrive à 95 % en environ 2,4 fois cette valeur (0,06 → ~0,14 s). L'ancien mouvement à vitesse constante (7 m/s, 35 cm en 50 ms) partait et s'arrêtait net : « brut et sec » (2026-10-06). Réglage de ressenti ; il règle aussi la vitesse à laquelle on s'expose en peekant.")]
    [SerializeField] private float leanSmoothTime = 0.06f;

    /// <summary>Vitesse courante du lean (m/s), état de l'amortissement. Propre à chaque instance,
    /// comme le décalage lui-même : chez le propriétaire pour sa caméra, sur le serveur pour la
    /// surface touchable.</summary>
    private float leanVelocity;

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
    [Tooltip("Chute maximale acceptée à l'arrivée d'un franchissement. Borne volontaire : sans elle, vaulter une barricade au bord d'un vide téléporterait au fond.")]
    [SerializeField] private float vaultMaxLandingDrop = 1.5f;
    [SerializeField] private float vaultDuration = 0.45f;
    [SerializeField] private float vaultArcHeight = 0.35f;
    [Tooltip("Marge au-dessus du sommet de l'obstacle au point haut de l'arc. L'arc est elargi si besoin pour l'atteindre : sans ca, un franchissement dont le depart ET l'arrivee sont au sol traverserait l'obstacle au lieu de passer dessus.")]
    [SerializeField] private float vaultObstacleClearance = 0.15f;
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
    [Tooltip("Délai volontaire (s) auquel un spectateur affiche un joueur distant, pour toujours avoir 2 poses connues entre lesquelles interpoler. Il s'AJOUTE aux 50 ms de marge que Netcode garde déjà sur son horloge serveur côté client : 0,05 s donne 100 ms de réserve derrière la pose la plus récente, trois poses à 30 par seconde. Si [DIAG-ANIM] compte des frames « affamées », c'est trop court.")]
    [SerializeField] private float interpolationDelay = 0.05f;

    public Stance CurrentStance { get; private set; } = Stance.Standing;

    /// <summary>Posture d'ARRIVÉE telle que le SERVEUR la publie, celle que la simulation utilise.
    /// Contrairement à CurrentStance, elle est juste sur les quatre cas réseau, y compris chez un
    /// spectateur qui n'appelle jamais Move(). Pour l'AFFICHAGE, préférer GetDisplayedStance, qui
    /// tient compte de la transition en cours et de l'instant affiché.</summary>
    public Stance NetworkedStance => networkStance.Value.current;

    /// <summary>
    /// Posture AFFICHÉE pour ce joueur sur cette machine : posture de départ, posture d'arrivée et
    /// progression adoucie de l'une à l'autre (1 = arrivé). Calculée pour l'instant que cette
    /// machine affiche : le présent du serveur pour le serveur, le passé interpolé pour un
    /// spectateur — exactement le même que celui de la position, donc l'adversaire se couche là
    /// où on le voit, au moment où on le voit.
    /// </summary>
    public void GetDisplayedStance(out Stance from, out Stance to, out float progress)
    {
        StanceState state = networkStance.Value;
        ResolveStance(state, DisplayTime, TransitionDuration(state.previous, state.current), out from, out to, out progress);
    }

    /// <summary>Durée de la transition entre accroupi et allongé : l'animation de transition y
    /// accorde sa vitesse de lecture, comme le vault le fait avec sa propre durée.</summary>
    public float CrouchProneTransitionDuration => crouchProneTransition;

    /// <summary>Vrai là où ce joueur est SIMULÉ (propriétaire, serveur, ou hors session dans les
    /// tests) ; faux chez un pur spectateur, qui ne fait qu'afficher ce que le serveur publie.</summary>
    private bool Simulates => !IsSpawned || IsOwner || IsServer;

    private bool HasDisplayFlag(DisplayFlags flag) => (displayedPose.flags & (byte)flag) != 0;

    /// <summary>Le joueur est-il en train de franchir, du point de vue de l'AFFICHAGE ?
    ///
    /// Même patron que LeanOffset : celui qui SIMULE utilise sa valeur locale, donc sans latence,
    /// et seul le spectateur lit la valeur réseautée. Le propriétaire voit ainsi son propre
    /// franchissement instantanément — ce qui compte pour son ombre, la seule partie de son
    /// modèle qu'il voit.</summary>
    public bool DisplayVaulting => Simulates ? IsVaulting : HasDisplayFlag(DisplayFlags.Vaulting);

    /// <summary>Durée d'un franchissement, en secondes. Exposée pour que l'ANIMATION puisse s'y
    /// accorder : son clip est lu à la vitesse qu'il faut pour tenir exactement dans cet
    /// intervalle. Sans ça, retoucher la durée du vault désaccorderait silencieusement le geste.</summary>
    public float VaultDuration => vaultDuration;

    /// <summary>Vitesse de marche nominale d'une posture, en m/s : celle à laquelle l'animation de
    /// déplacement doit valoir 1. Exposée pour que PlayerAnimator la LISE au lieu de la recopier :
    /// sa copie s'était déjà désaccordée en silence (4,4 m/s dans l'animateur, 5 m/s réglés dans
    /// le prefab, constaté le 2026-10-05).</summary>
    public float NominalSpeed(Stance stance) => walkSpeed * GetStanceProfile(stance).moveSpeedMultiplier;

    /// <summary>Position de l'œil dans une posture, dans l'espace du joueur, SANS lean ni head
    /// bob. C'est la valeur autoritaire, identique sur toutes les machines : elle sert au calcul
    /// des zones touchables et à l'anti-clipping du lean. La caméra du propriétaire, elle, y
    /// glisse en douceur.</summary>
    public Vector3 EyeLocal(Stance stance)
    {
        StanceProfile p = GetStanceProfile(stance);
        return new Vector3(p.cameraSide, p.cameraHeight, p.cameraForward);
    }

    /// <summary>Rayon du CharacterController dans une posture. L'œil doit rester à l'intérieur,
    /// sinon la caméra traverserait un mur face auquel on se tient (vérifié par les tests).</summary>
    public float ControllerRadius(Stance stance) => GetStanceProfile(stance).controllerRadius;

    /// <summary>Décalage latéral de lean à AFFICHER pour ce joueur sur cette machine : prédit
    /// chez le propriétaire, publié par le serveur partout ailleurs. Lu par l'animation.</summary>
    public float DisplayLeanOffset => LeanOffset;

    /// <summary>Surface touchable, ou null si le joueur n'en a pas (joueur de test).</summary>
    public PlayerHitbox Hitbox => hitbox;

    /// <summary>Déplacement de la caméra dû au lean, dans l'espace du JOUEUR (latéral, plus la
    /// descente de la tête quand le buste s'incline). Propriétaire uniquement ; appliqué par
    /// PlayerCameraLook APRÈS le pitch, pour pouvoir le ramener dans le repère de la caméra.</summary>
    public Vector3 LeanCameraOffset { get; private set; }

    /// <summary>Roulis de la caméra dû au lean, en degrés : proportionnel à l'angle du buste, donc le
    /// même à fond de lean dans toutes les postures. Allongé, c'est l'essentiel de l'effet : le
    /// buste roule sur lui-même et la tête sort peu.</summary>
    public float LeanCameraTilt { get; private set; }

    /// <summary>Le joueur est-il en visee, du point de vue de l'AFFICHAGE ?
    ///
    /// IsAiming est affectee DANS Move(), qu'un spectateur n'appelle jamais : lue telle quelle
    /// elle resterait false en permanence et l'adversaire n'epaulerait jamais son arme. Meme
    /// aiguillage que pour la posture, la chute et le franchissement.</summary>
    public bool DisplayAiming => Simulates ? IsAiming : HasDisplayFlag(DisplayFlags.Aiming);

    /// <summary>Le joueur est-il en l'air, du point de vue de l'AFFICHAGE ?
    ///
    /// Le franchissement en est EXCLU explicitement : il coupe le CharacterController, donc
    /// isGrounded y vaut false et un vault passerait pour une chute. S'en remettre à l'ordre des
    /// transitions de l'Animator marcherait aujourd'hui et casserait au premier réagencement.</summary>
    public bool DisplayAirborne
    {
        get
        {
            if (DisplayVaulting) return false;
            bool grounded = Simulates ? controller.isGrounded : HasDisplayFlag(DisplayFlags.Grounded);
            return !grounded;
        }
    }
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
    private float currentCameraForward;   // avancée de base liée à la posture (tête en avant accroupi/allongé)
    private float currentCameraSide;      // décalage latéral de base (tête penchée allongé)

    private float landingDipOffset;       // décalage négatif temporaire appliqué par-dessus, qui remonte à 0

    private float bobPhase;
    private float bobBlend;
    private float headBobOffset;
    private float headBobLateralOffset;

    private float landingBobPhase;
    private float landingBobEnvelope;

    private float footstepDistanceAccumulator;

    // ------------------------------------------------------------------
    // Réseau — ce que le serveur publie (2026-10-06)
    //
    // Deux variables seulement. La POSE d'affichage, datée sur l'horloge du serveur et publiée à
    // chaque frame : position, rotation, lean et drapeaux d'animation (au sol, en franchissement,
    // en visée). Et la POSTURE avec sa transition. Elles remplacent sept variables, dont trois
    // drapeaux qui n'existaient que pour l'animation d'un spectateur — lequel n'appelle jamais
    // Move() et n'a donc aucun autre moyen de savoir qu'un adversaire tombe, franchit ou épaule.
    //
    // La règle reste celle apprise à ses dépens : tout ce qu'un spectateur doit VOIR doit venir
    // d'une source réseautée, jamais d'un champ mis à jour par la simulation.
    // ------------------------------------------------------------------

    private readonly NetworkVariable<DisplayPose> networkPose = new NetworkVariable<DisplayPose>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<StanceState> networkStance = new NetworkVariable<StanceState>(
        new StanceState { current = Stance.Standing, previous = Stance.Standing, changedAt = double.NegativeInfinity },
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Côté serveur : change à chaque téléportation (apparition, nouvelle manche), pour que
    /// les spectateurs vident leur historique au lieu d'interpoler un glissement à travers la carte.</summary>
    private byte serverTeleportId;

    /// <summary>Décalage de lean de CE joueur sur CETTE machine : la valeur simulée chez le
    /// propriétaire et sur le serveur, la valeur interpolée — au même instant que la position —
    /// chez un spectateur.</summary>
    private float LeanOffset => Simulates ? currentLeanOffset : displayedPose.leanOffset;

    /// <summary>Horloge du serveur (NetworkManager.ServerTime). Sur le serveur, son présent ; sur un
    /// client, son estimation, en retard d'environ RTT/2 + 50 ms — soit l'âge des données qui lui
    /// arrivent. Hors session (tests), le temps local.</summary>
    private double ServerClock => IsSpawned && NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

    /// <summary>
    /// Instant, sur l'horloge du serveur, de ce que CETTE machine affiche des AUTRES joueurs. Le
    /// tireur l'envoie avec chaque tir, et le serveur replace ses adversaires exactement à cet
    /// instant : plus d'estimation de latence à deviner. Le Host voit les autres au présent de sa
    /// propre simulation, donc il n'a rien à compenser.
    /// </summary>
    public double ViewServerTime => IsServer ? ServerClock : (shownViewTime > 0.0 ? shownViewTime : InterpolationTime);

    /// <summary>Instant auquel un spectateur interpole les autres joueurs, calculé maintenant.</summary>
    private double InterpolationTime => ServerClock - interpolationDelay;

    /// <summary>
    /// Instant RÉELLEMENT affiché des autres joueurs sur cette machine : celui de leur dernière
    /// interpolation. C'est lui que le tir doit annoncer, et non l'instant calculé à la volée
    /// (2026-10-06) : selon l'ordre d'exécution des scripts, le tir part avant ou après que les
    /// adversaires aient été replacés pour cette frame. Annoncer l'instant de la frame en cours
    /// alors qu'ils sont encore à celui de la précédente décalait le rewind d'une frame — jusqu'à
    /// 8 cm à 60 images/s sur une cible qui court.
    /// </summary>
    private static double shownViewTime;

    /// <summary>Instant affiché pour CE joueur sur cette machine : le présent pour qui le simule,
    /// l'instant interpolé pour un spectateur.</summary>
    private double DisplayTime => Simulates ? ServerClock : InterpolationTime;

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
        /// de ping dédiée. Diagnostic seulement depuis que le tir envoie l'instant qu'il voit.</summary>
        public float sentAt;

        /// <summary>Instant de simulation de cet input sur l'horloge du serveur : NetworkManager.
        /// LocalTime chez le client, qui vaut à peu près l'instant où le serveur le traitera. Sert
        /// à la progression d'une transition de posture dans Move(), et doit être REJOUÉ tel quel
        /// à la réconciliation, sinon le rejeu ne referait pas le même calcul.</summary>
        public double simTime;
    }

    // Côté propriétaire distant (cas 2) : inputs envoyés au serveur mais pas encore confirmés.
    private readonly List<PendingInput> unconfirmedInputs = new List<PendingInput>();

    // ------------------------------------------------------------------
    // Compensation de latence (rewind) — voir WeaponController.FireServerRpc
    // ------------------------------------------------------------------

    [Header("Réseau — compensation de latence")]
    [Tooltip("Durée (s) de l'historique de pose conservé par le SERVEUR pour chaque joueur, afin de pouvoir le replacer dans le passé au moment de valider un tir. Doit couvrir le rewind maximum autorisé côté arme, avec de la marge.")]
    [SerializeField] private float hitboxHistoryDuration = 1f;

    /// <summary>RTT lissé du propriétaire distant, en secondes — DIAGNOSTIC seulement. Le rewind
    /// s'en servait pour estimer l'âge de ce que le tireur voit ; depuis le 2026-10-06, le tireur
    /// envoie directement l'instant qu'il affiche (ViewServerTime), ce qui est exact. L'estimation
    /// oubliait d'ailleurs les 50 ms de marge que Netcode garde sur son horloge serveur.</summary>
    private float smoothedRtt;

    /// <summary>Pose passée d'un joueur, telle que le serveur l'a simulée. Contient TOUT ce dont
    /// dépend la surface touchable — position, orientation, lean et posture. Historiser la seule
    /// position ne corrigerait qu'une part du décalage : un adversaire penché ou accroupi au moment
    /// du tir serait rewind avec la géométrie qu'il a MAINTENANT.</summary>
    public struct HitboxPose
    {
        public double time;      // horloge du serveur
        public Vector3 position;
        public float yaw;
        public float leanOffset;
        public StanceState stance; // avec sa transition : la forme exacte se recalcule à l'instant visé
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

    // Côté spectateur (cas 4) : petit historique récent des poses reçues, et la pose affichée.
    private readonly List<DisplayPose> remoteSnapshots = new List<DisplayPose>();
    private DisplayPose displayedPose;
    private byte lastTeleportId;

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

            // Le SERVEUR publie la pose où il vient de faire apparaître ce joueur. Sans ça, elle
            // vaut encore default = (0,0,0) à cet instant, et la branche cliente ci-dessous
            // téléportait tout le monde à l'origine du monde. Invisible tant que la scène de test
            // spawne près de l'origine, et fatal dès qu'il y a de vrais points de spawn opposés.
            ServerPublishPose();
        }
        else
        {
            // Client/spectateur : on s'aligne sur ce que le serveur a publié (livré avec le
            // message de spawn), pour éviter une frame affichée à la position du prefab.
            DisplayPose pose = networkPose.Value;
            controller.enabled = false;
            transform.SetPositionAndRotation(pose.position, Quaternion.Euler(0f, pose.yaw, 0f));
            controller.enabled = true;
            displayedPose = pose;
            lastTeleportId = pose.teleportId;
            remoteSnapshots.Clear();
            remoteSnapshots.Add(pose);
        }

        networkPose.OnValueChanged += HandleNetworkPoseChanged;
        if (!spawnedPlayers.Contains(this)) spawnedPlayers.Add(this);

        // Static, et le rechargement de domaine est désactivé à l'entrée en Play Mode dans ce
        // projet : sans cette remise à zéro, le premier tir d'une session annoncerait l'instant de
        // la session précédente.
        if (IsOwner) shownViewTime = 0.0;
        ApplyStanceImmediate(networkStance.Value.current);

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
        networkPose.OnValueChanged -= HandleNetworkPoseChanged;
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
                  $"rtt={smoothedRtt * 1000f:F0}ms " +
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
        double now = ServerClock;
        serverHitboxHistory.Add(new HitboxPose
        {
            time = now,
            position = transform.position,
            yaw = transform.eulerAngles.y,
            leanOffset = LeanOffset,
            stance = networkStance.Value,
        });

        double cutoff = now - hitboxHistoryDuration;
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
    public void ServerBeginRewind(double targetTime)
    {
        if (!IsServer || isRewound || hitbox == null) return;
        if (serverHitboxHistory.Count == 0) return;

        if (!TrySampleHistory(targetTime, out HitboxPose pose)) return;

        Transform t = hitbox.transform;
        rewindSavedLocalPosition = t.localPosition;
        rewindSavedLocalRotation = t.localRotation;
        isRewound = true;

        // Position et orientation MONDE : le hitbox est un enfant, mais on le sort volontairement
        // de la pose de son parent le temps du tir. Ses trois zones suivent.
        t.SetPositionAndRotation(pose.position, Quaternion.Euler(0f, pose.yaw, 0f));

        // La posture est recalculée pour l'instant VISÉ, transition comprise : un adversaire
        // surpris à mi-chemin d'un plongeon au sol est replacé à mi-chemin, comme on le voyait.
        ApplyHitboxAt(pose.stance, targetTime, pose.leanOffset);
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

        // Les zones sont recalculées au prochain UpdateStanceVisuals (fonction pure de la posture
        // courante), mais on les remet tout de suite pour qu'une requête intermédiaire ne voie
        // pas la géométrie du passé.
        ApplyHitboxAt(networkStance.Value, ServerClock, LeanOffset);
    }

    private bool TrySampleHistory(double targetTime, out HitboxPose result)
        => SampleHitboxHistory(serverHitboxHistory, targetTime, out result);

    /// <summary>
    /// Interpole la pose historique à l'instant demandé. Renvoie false si l'historique est vide ;
    /// se rabat sur la pose la plus ancienne/récente si l'instant sort de sa fenêtre.
    ///
    /// Fonction PURE et statique, séparée de l'état du composant exprès : c'est le cœur du rewind,
    /// donc l'endroit où une régression coûterait le plus cher, et sous cette forme elle se teste
    /// sans Editor, sans réseau et sans scène (voir les tests EditMode).
    /// </summary>
    public static bool SampleHitboxHistory(IReadOnlyList<HitboxPose> history, double targetTime, out HitboxPose result)
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

            double span = b.time - a.time;
            float t = span > 0.0001 ? (float)((targetTime - a.time) / span) : 0f;

            result = new HitboxPose
            {
                time = targetTime,
                position = Vector3.Lerp(a.position, b.position, t),
                yaw = Mathf.LerpAngle(a.yaw, b.yaw, t),
                leanOffset = Mathf.Lerp(a.leanOffset, b.leanOffset, t),
                // La posture ne s'interpole pas : elle porte l'instant exact de son changement, et
                // la transition se recalcule à l'instant visé (ApplyHitboxAt). On prend donc celle
                // qui était en vigueur à cet instant : la nouvelle si le changement le précède.
                stance = b.stance.changedAt <= targetTime ? b.stance : a.stance,
            };
            return true;
        }

        result = history[history.Count - 1];
        return true;
    }

    /// <summary>
    /// Replace ce joueur sur un point de spawn, côté SERVEUR uniquement, et publie le résultat.
    /// Appelée au spawn, puis à chaque manche par RoundManager.BeginRound().
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
        foreach (PlayerLocomotion other in spawnedPlayers)
        {
            if (other == null || other == this) continue;
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
        // milieu. La posture vient d'une NetworkVariable, donc la remettre ici suffit — sans
        // transition : on réapparaît debout, on ne se relève pas.
        ServerSetStanceImmediate(Stance.Standing);
        ApplyStanceImmediate(Stance.Standing);

        currentLeanOffset = 0f;
        leanVelocity = 0f;
        leanState = 0;
        serverLastLeanState = 0; // sinon le premier input de la manche jouerait un faux LeanEnd

        // Le LEAN demande en plus une RPC vers le propriétaire, contrairement à tout le reste.
        // Son ÉTAT (-1/0/+1) est une bascule qui vit sur le client : remettre le décalage à zéro
        // côté serveur ne suffirait pas, le propriétaire se repencherait dès la frame suivante
        // puisque sa touche est toujours considérée comme enclenchée.
        ResetLeanClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        });

        serverTeleportId++;
        ServerPublishPose();
    }

    /// <summary>Annule le lean chez le propriétaire — état, décalage et effet caméra. Envoyée par
    /// le serveur au début de chaque manche (voir ServerMoveToSpawnPoint).</summary>
    [ClientRpc]
    private void ResetLeanClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;

        leanState = 0;
        currentLeanOffset = 0f;
        leanVelocity = 0f;
        LeanCameraOffset = Vector3.zero;
        LeanCameraTilt = 0f;
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

        // Une demande de posture arrivée pendant une transition s'applique à la fin de celle-ci.
        if (IsServer) ServerUpdateStance();

        if (IsOwner && IsServer)
        {
            // Cas 1 : Host sur son propre perso — autorité directe, comme en solo.
            MovementInputSnapshot snap = ReadLocalInput();
            float dt = Time.deltaTime;
            bool wasGrounded = controller.isGrounded;

            ServerCheckAutoStand(snap);
            Move(snap, dt, ServerClock);
            ServerTrackLeanSound(snap.leanState);

            if (controller.isGrounded && !wasGrounded) TriggerLandingKick(Mathf.Abs(verticalVelocity));
            ServerAdvanceFootsteps(dt);
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
        }

        // Publication serveur, à CHAQUE frame et même sans input traité : sinon l'historique du
        // rewind aurait des trous pendant les micro-coupures réseau, précisément quand il sert le
        // plus, et la pose des spectateurs resterait figée (un adversaire garderait son animation
        // de chute après avoir atterri). Placée APRÈS le bloc du propriétaire, pour que le Host
        // publie le lean de cette frame et non celui de la précédente.
        if (IsServer && IsSpawned)
        {
            ServerPublishPose();
            ServerRecordHitboxPose();
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

        // LocalTime et non ServerTime : c'est l'instant, sur l'horloge du serveur, où celui-ci
        // TRAITERA cet input. Une transition de posture y progresse donc d'autant des deux côtés.
        double simTime = NetworkManager.LocalTime.Time;

        bool wasGrounded = controller.isGrounded;
        Move(snap, dt, simTime);
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
            simTime = simTime,
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
            // L'horloge du serveur, jamais un instant fourni par le client : il pourrait sinon
            // sauter la fin d'une transition (se relever d'un coup) en annonçant un instant futur.
            Move(next.snapshot, dt, ServerClock);
            ServerAdvanceFootsteps(dt);
            ServerAdvanceLean(next.snapshot.leanState, dt);
            ServerTrackLeanSound(next.snapshot.leanState);
            lastProcessedSequence = next.sequence;
        }

        // Le reste éventuel de la queue (flood ou vrai gros rattrapage) attend le prochain
        // Update() serveur plutôt que d'être traité d'un coup — voir commentaire ci-dessus.

        if (lastProcessedSequence >= 0)
        {
            SendCorrectionToOwner(lastProcessedSequence, transform.position, verticalVelocity, transform.eulerAngles.y, currentVelocity, IsVaulting, vaultTimer, vaultStart, vaultEnd, vaultPeakY);
        }

        // La pose et l'historique sont publiés par Update(), à chaque frame, inputs ou non.
    }

    private void SendCorrectionToOwner(int confirmedSequence, Vector3 confirmedPosition, float confirmedVerticalVelocity, float confirmedYaw, Vector3 confirmedHorizontalVelocity, bool confirmedVaulting, float confirmedVaultTimer, Vector3 confirmedVaultStart, Vector3 confirmedVaultEnd, float confirmedVaultPeakY)
    {
        var targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        ReceiveCorrectionClientRpc(confirmedSequence, confirmedPosition, confirmedVerticalVelocity, confirmedYaw, confirmedHorizontalVelocity, confirmedVaulting, confirmedVaultTimer, confirmedVaultStart, confirmedVaultEnd, confirmedVaultPeakY, targetParams);
    }

    [ClientRpc]
    private void ReceiveCorrectionClientRpc(int confirmedSequence, Vector3 confirmedPosition, float confirmedVerticalVelocity, float confirmedYaw, Vector3 confirmedHorizontalVelocity, bool confirmedVaulting, float confirmedVaultTimer, Vector3 confirmedVaultStart, Vector3 confirmedVaultEnd, float confirmedVaultPeakY, ClientRpcParams clientRpcParams = default)
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
        RestoreVaultState(confirmedVaulting, confirmedVaultTimer, confirmedVaultStart, confirmedVaultEnd, confirmedVaultPeakY);

        isReplayingInputs = true;
        try
        {
            for (int i = 0; i < unconfirmedInputs.Count; i++)
            {
                PendingInput pending = unconfirmedInputs[i];
                Move(pending.snapshot, pending.deltaTime, pending.simTime);

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

    private void HandleNetworkPoseChanged(DisplayPose previous, DisplayPose current)
    {
        if (IsOwner || IsServer) return;

        // Une téléportation (nouvelle manche) vide l'historique : interpoler entre l'ancienne
        // position et le point de spawn ferait traverser la carte au personnage.
        if (current.teleportId != lastTeleportId)
        {
            remoteSnapshots.Clear();
            lastTeleportId = current.teleportId;
        }

        // Les poses arrivent dans l'ordre (livraison fiable et ordonnée), mais rien ne coûte de
        // s'en garder : une pose plus ancienne que la dernière casserait la recherche par intervalle.
        if (remoteSnapshots.Count > 0 && current.time <= remoteSnapshots[remoteSnapshots.Count - 1].time) return;

        remoteSnapshots.Add(current);
        double cutoff = current.time - 1.0;
        remoteSnapshots.RemoveAll(snapshot => snapshot.time < cutoff);
    }

    /// <summary>
    /// Publie la pose d'affichage, datée sur l'horloge du serveur. Appelée à CHAQUE frame serveur.
    /// La date change à chaque frame, donc la variable part à chaque tick réseau (30 par seconde,
    /// environ 1 Ko/s par joueur) : c'est voulu, un spectateur a besoin de poses régulières même
    /// quand le joueur est immobile, sinon il interpolerait un départ sur plusieurs secondes.
    /// </summary>
    private void ServerPublishPose()
    {
        DisplayFlags flags = 0;
        if (controller.enabled && controller.isGrounded) flags |= DisplayFlags.Grounded;
        if (IsVaulting) flags |= DisplayFlags.Vaulting;
        if (IsAiming) flags |= DisplayFlags.Aiming;

        networkPose.Value = new DisplayPose
        {
            time = ServerClock,
            position = transform.position,
            yaw = transform.eulerAngles.y,
            leanOffset = LeanOffset,
            flags = (byte)flags,
            teleportId = serverTeleportId,
        };
    }

    private void UpdateRemoteInterpolation()
    {
        double viewTime = InterpolationTime;
        shownViewTime = viewTime;
        displayedPose = remoteSnapshots.Count > 0
            ? SampleDisplayPose(remoteSnapshots, viewTime)
            : networkPose.Value;

#if UNITY_EDITOR
        if (remoteSnapshots.Count > 0)
        {
            double reserve = remoteSnapshots[remoteSnapshots.Count - 1].time - viewTime;
            if (reserve < 0.0) DiagStarvedFrames++;
            DiagReserveSum += reserve;
            DiagReserveFrames++;
        }
#endif
        transform.SetPositionAndRotation(displayedPose.position, Quaternion.Euler(0f, displayedPose.yaw, 0f));
    }

#if UNITY_EDITOR
    /// <summary>Frames où l'instant affiché dépassait la pose la plus récente reçue : le
    /// spectateur n'avait plus rien entre quoi interpoler et figeait l'adversaire. Doit rester à
    /// zéro hors coupure réseau ; sinon interpolationDelay est trop court. Lu par [DIAG-ANIM].</summary>
    public int DiagStarvedFrames { get; set; }

    /// <summary>Somme, sur la fenêtre, de l'avance de la pose la plus récente reçue sur l'instant
    /// affiché : la RÉSERVE réellement disponible pour absorber la gigue. Ce qui la dépasse est du
    /// retard d'affichage payé pour rien. Lu par [DIAG-ANIM].</summary>
    public double DiagReserveSum { get; set; }
    public int DiagReserveFrames { get; set; }
#endif

    /// <summary>
    /// Pose interpolée à un instant donné de l'horloge du serveur. Fonction PURE et statique,
    /// testée sans réseau comme SampleHitboxHistory. Position et lean en ligne droite, rotation par
    /// le plus court chemin, drapeaux d'animation (discrets) pris à la pose de début d'intervalle.
    /// Hors de l'historique, la pose la plus proche, sans extrapolation : deviner où va un
    /// adversaire serait pire que le montrer un instant immobile.
    /// </summary>
    public static DisplayPose SampleDisplayPose(IReadOnlyList<DisplayPose> snapshots, double time)
    {
        if (time <= snapshots[0].time) return snapshots[0];

        for (int i = 0; i < snapshots.Count - 1; i++)
        {
            DisplayPose a = snapshots[i];
            DisplayPose b = snapshots[i + 1];
            if (time > b.time) continue;

            double span = b.time - a.time;
            float t = span > 1e-6 ? (float)((time - a.time) / span) : 1f;
            return new DisplayPose
            {
                time = time,
                position = Vector3.Lerp(a.position, b.position, t),
                yaw = Mathf.LerpAngle(a.yaw, b.yaw, t),
                leanOffset = Mathf.Lerp(a.leanOffset, b.leanOffset, t),
                flags = a.flags,
                teleportId = b.teleportId,
            };
        }

        return snapshots[snapshots.Count - 1];
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

    /// <param name="simTime">Instant de simulation de cet input sur l'horloge du serveur (voir
    /// PendingInput.simTime). Ne sert qu'à la progression d'une transition de posture.</param>
    private void Move(MovementInputSnapshot snap, float dt, double simTime)
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

        // TRANSITION DE POSTURE (2026-10-06) : la vitesse glisse de celle de la posture quittée à
        // celle de la posture d'arrivée. Sans ça, se relever d'un plongeon au sol rendait la
        // vitesse de marche d'un coup, pendant que le corps visible était encore à genoux : il
        // glissait. La progression vient de l'instant de simulation de l'input, à peu près le
        // même chez le client qui prédit et chez le serveur qui tranche ; le petit écart restant
        // est absorbé par le seuil de réconciliation, comme celui que la posture confirmée par le
        // serveur crée déjà.
        StanceState stanceState = networkStance.Value;
        Stance stance = stanceState.current;
        float stanceProgress = TransitionProgress(stanceState.changedAt, simTime,
                                                  TransitionDuration(stanceState.previous, stance));

        // Un vault ne démarre que pendant une manche, et jamais en pleine transition de posture :
        // on ne franchit pas un obstacle à moitié relevé.
        if (snap.jumpPressed && RoundManager.MovementAllowed && stanceProgress >= 1f
            && TryFindVaultTarget(out Vector3 vaultLanding, out float vaultTop, false))
        {
            BeginVault(vaultLanding, vaultTop);
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
        float stanceMultiplier = Mathf.Lerp(GetStanceProfile(stanceState.previous).moveSpeedMultiplier,
                                            profile.moveSpeedMultiplier, stanceProgress);
        float targetSpeed = baseSpeed * stanceMultiplier;

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
            Stance desired = networkStance.Value.current == Stance.Crouching ? Stance.Standing : Stance.Crouching;
            RequestStanceChangeServerRpc(desired);
        }

        if (input.PronePressedThisFrame)
        {
            Stance desired = networkStance.Value.current == Stance.Prone ? Stance.Standing : Stance.Prone;
            RequestStanceChangeServerRpc(desired);
        }

        // Le relevé automatique en sprint (Crouch/Prone + Sprint + avancer = se relève tout
        // seul) dépend d'un état MAINTENU, pas d'un appui ponctuel — il est donc évalué côté
        // serveur, à même la simulation du mouvement (ServerCheckAutoStand), plutôt qu'ici.
    }

    [ServerRpc]
    private void RequestStanceChangeServerRpc(Stance desired)
    {
        ServerRequestStance(desired);
    }

    /// <summary>Relevé automatique en sprint, évalué côté serveur à partir de l'input de
    /// mouvement de CE tick (déjà soumis pour le déplacement) — pas de RPC dédiée nécessaire.</summary>
    private void ServerCheckAutoStand(MovementInputSnapshot snap)
    {
        if (networkStance.Value.current == Stance.Standing) return;

        if (snap.sprintHeld && snap.move.y > 0.1f)
        {
            ServerRequestStance(Stance.Standing);
        }
    }

    // Côté serveur : demande de posture arrivée PENDANT une transition, appliquée à sa fin.
    private Stance? serverPendingStance;

    /// <summary>
    /// Une demande de posture, d'où qu'elle vienne (touche, relevé automatique). Pendant une
    /// transition, elle est mise en attente et s'applique quand la transition se termine : une
    /// seule transition à la fois.
    ///
    /// C'est aussi ce qui borne le débit de RequestStanceChangeServerRpc, ouvert depuis la relecture
    /// du 2026-10-05 : un client modifié qui alternerait ses postures à chaque frame n'obtient plus
    /// qu'un changement par transition, au moins 0,3 s, mesuré en temps réel (règle 2 des
    /// garde-fous).
    /// </summary>
    private void ServerRequestStance(Stance desired)
    {
        if (ServerInStanceTransition())
        {
            serverPendingStance = desired;
            return;
        }
        ServerTryChangeStance(desired);
    }

    private bool ServerInStanceTransition()
    {
        StanceState state = networkStance.Value;
        return ServerClock < state.changedAt + TransitionDuration(state.previous, state.current);
    }

    /// <summary>Applique la demande en attente dès que la transition en cours est finie.</summary>
    private void ServerUpdateStance()
    {
        if (!serverPendingStance.HasValue || ServerInStanceTransition()) return;

        Stance desired = serverPendingStance.Value;
        serverPendingStance = null;
        ServerTryChangeStance(desired);
    }

    private void ServerTryChangeStance(Stance desired)
    {
        Stance current = networkStance.Value.current;
        if (desired == current) return;
        if (desired == Stance.Standing && !CanStandUp()) return;

        networkStance.Value = new StanceState { current = desired, previous = current, changedAt = ServerClock };
        PlayerSoundEvent? sound = GetStanceSound(current, desired);
        if (sound.HasValue) BroadcastPlayerSoundClientRpc(sound.Value, ComputeNoiseLevel());
    }

    /// <summary>Change de posture SANS transition : apparition, début de manche.</summary>
    private void ServerSetStanceImmediate(Stance stance)
    {
        serverPendingStance = null;
        networkStance.Value = new StanceState { current = stance, previous = stance, changedAt = double.NegativeInfinity };
    }

    /// <summary>Durée de la transition entre deux postures (0 si elles sont identiques).</summary>
    private float TransitionDuration(Stance from, Stance to)
    {
        if (from == to) return 0f;
        bool prone = from == Stance.Prone || to == Stance.Prone;
        bool standing = from == Stance.Standing || to == Stance.Standing;
        if (prone && standing) return standProneTransition;
        return prone ? crouchProneTransition : standCrouchTransition;
    }

    /// <summary>
    /// Progression ADOUCIE d'une transition commencée à `changedAt`, à l'instant `time` : 0 au
    /// départ, 1 à l'arrivée, avec un départ et une arrivée en douceur (smoothstep) — un corps ne
    /// passe pas d'une posture à l'autre à vitesse constante. Fonction pure, testée.
    /// </summary>
    public static float TransitionProgress(double changedAt, double time, float duration)
    {
        if (duration <= 0f) return 1f;
        float p = Mathf.Clamp01((float)((time - changedAt) / duration));
        return p * p * (3f - 2f * p);
    }

    /// <summary>
    /// Posture à afficher à un instant donné : avant le changement, l'ancienne posture, établie ;
    /// après, la transition de l'ancienne vers la nouvelle. Fonction pure, testée. Le serveur
    /// n'accepte un nouveau changement qu'une fois la transition précédente terminée, donc
    /// l'ancienne posture était forcément établie au moment du changement.
    /// </summary>
    public static void ResolveStance(StanceState state, double time, float duration,
                                     out Stance from, out Stance to, out float progress)
    {
        if (time < state.changedAt)
        {
            from = state.previous;
            to = state.previous;
            progress = 1f;
            return;
        }

        from = state.previous;
        to = state.current;
        progress = TransitionProgress(state.changedAt, time, duration);
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

        // Même placement que la vraie capsule debout : elle commence à la marge de peau au-dessus
        // des pieds (voir CapsuleCenterHeight). Partir des pieds mêmes ferait toucher le sol au test.
        float radius = standingProfile.controllerRadius * 0.95f;
        float bottom = controller.skinWidth;
        Vector3 feet = transform.position + Vector3.up * (bottom + radius);
        Vector3 head = transform.position + Vector3.up * (bottom + standingProfile.controllerHeight - radius);

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

        float centerY = CapsuleCenterHeight(profile);

        // Court-circuit : écrire height/radius force PhysX à reconstruire le collider, inutile de
        // le faire à chaque frame alors que la posture ne change que rarement.
        if (Mathf.Approximately(controller.height, profile.controllerHeight)
            && Mathf.Approximately(controller.radius, profile.controllerRadius)
            && Mathf.Approximately(controller.center.y, centerY))
        {
            return;
        }

        controller.height = profile.controllerHeight;
        controller.radius = profile.controllerRadius;
        controller.center = new Vector3(0f, centerY, 0f);
    }

    /// <summary>
    /// Hauteur du centre de la capsule au-dessus de la RACINE du joueur, choisie pour que la
    /// racine soit exactement au SOL. Tout ce qui se mesure « depuis les pieds » — l'œil, les zones
    /// touchables, le modèle — en dépend.
    ///
    /// Corrigé le 2026-10-06 : le robot flottait de 8,6 cm debout et de 18,8 cm allongé. Deux
    /// causes, toutes deux silencieuses. (1) Le CharacterController garde sa marge de peau
    /// (skinWidth, 8 cm) entre la capsule et le sol : capsule posée sur la racine, la racine
    /// restait 8 cm en l'air. (2) Allongé, la hauteur (0,5 m) est inférieure au diamètre (0,8 m) :
    /// Unity en fait une sphère de 0,8 m, centrée à 0,25 m, qui dépassait de 15 cm sous les pieds.
    /// La capsule commence donc maintenant à la marge de peau au-dessus de la racine. Son volume
    /// dans le MONDE n'a pas bougé d'un millimètre : seule la racine, et tout ce qui s'y accroche,
    /// est descendue au sol.
    /// </summary>
    private float CapsuleCenterHeight(StanceProfile profile)
        => Mathf.Max(profile.controllerHeight, 2f * profile.controllerRadius) * 0.5f + controller.skinWidth;

    /// <summary>
    /// Habillage de la posture — œil de la caméra et surface touchable. Tourne sur TOUTES les
    /// instances (y compris les spectateurs, qui n'appellent jamais Move()).
    ///
    /// Les deux suivent la MÊME transition, calculée pour l'instant que cette machine affiche
    /// (GetDisplayedStance) : le corps visible, la caméra et la surface touchable passent ensemble
    /// d'une posture à l'autre. Jusqu'au 2026-10-06, la caméra glissait en moins de 0,1 s et la
    /// surface touchable changeait d'un coup : « trop instantané », et on visait un corps à
    /// mi-hauteur que le serveur croyait déjà couché.
    /// </summary>
    private void UpdateStanceVisuals()
    {
        GetDisplayedStance(out Stance from, out Stance to, out float progress);

        Vector3 eye = Vector3.Lerp(EyeLocal(from), EyeLocal(to), progress);
        currentCameraSide = eye.x;
        currentCameraHeight = eye.y;
        currentCameraForward = eye.z;

        // La surface TOUCHABLE ne dépend que de données réseautées et de l'instant affiché : sur le
        // serveur, seul à décider des dégâts, c'est son présent, identique à ce que le rewind
        // recalculera plus tard pour le même instant.
        //
        // 🚨 Ne JAMAIS dériver le hitbox des os animés : un Animator n'est pas déterministe entre
        // machines. L'animation AFFICHE la transition, la surface touchable la CALCULE ; les deux
        // lisent la même source réseautée, aucune ne lit l'autre.
        ApplyHitboxBlended(from, to, progress, LeanOffset);

        // Les spectateurs n'appellent jamais Move() : sans ça, leur CharacterController local
        // garderait la capsule de la posture précédente.
        ApplySimulationCapsule(networkStance.Value.current);
    }

    /// <summary>Met la surface touchable en accord avec une posture (en transition ou non) et le
    /// lean. Tourne sur toutes les instances, mais seule celle du SERVEUR décide des dégâts.</summary>
    private void ApplyHitboxBlended(Stance from, Stance to, float progress, float lateralOffset)
    {
        if (hitbox == null) return;
        hitbox.ApplyBlended(from, EyeLocal(from), to, EyeLocal(to), progress, lateralOffset);
    }

    /// <summary>Surface touchable telle qu'elle était à un instant donné de l'horloge du serveur,
    /// transition de posture comprise. Sert au rewind et à sa restauration.</summary>
    private void ApplyHitboxAt(StanceState state, double time, float lateralOffset)
    {
        ResolveStance(state, time, TransitionDuration(state.previous, state.current),
                      out Stance from, out Stance to, out float progress);
        ApplyHitboxBlended(from, to, progress, lateralOffset);
    }

    private void ApplyStanceImmediate(Stance stance)
    {
        StanceProfile profile = GetStanceProfile(stance);
        ApplySimulationCapsule(stance);
        currentCameraHeight = profile.cameraHeight;
        currentCameraForward = profile.cameraForward;
        currentCameraSide = profile.cameraSide;
        landingDipOffset = 0f;

        if (cameraPivot != null)
        {
            cameraPivot.localPosition = new Vector3(currentCameraSide, currentCameraHeight, currentCameraForward);
        }

        ApplyHitboxBlended(stance, stance, 1f, currentLeanOffset);
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

        Stance stance = networkStance.Value.current;
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
        NoiseLevel level = GetNoiseLevel(IsMoving, CurrentStance, IsSprinting, IsSneaking);
        CurrentNoise = level;
        return level;
    }

    /// <summary>Niveau de bruit d'un déplacement. Fonction pure, testée.
    ///
    /// Le sneak et le ramper sont FAIBLES, jamais muets (GDD § 8, confirmé le 2026-10-05). Ils
    /// étaient classés Silent, que PlayerSoundEmitter filtre : ramper et marcher en sneak ne
    /// faisaient donc aucun bruit, ce qui retirait une information au joueur adverse.</summary>
    public static NoiseLevel GetNoiseLevel(bool moving, Stance stance, bool sprinting, bool sneaking)
    {
        if (!moving) return NoiseLevel.Silent;
        if (stance == Stance.Prone) return NoiseLevel.Faint;
        if (sprinting) return NoiseLevel.Loud;
        if (sneaking) return NoiseLevel.Faint;
        return NoiseLevel.Quiet;
    }

    [ClientRpc]
    private void BroadcastPlayerSoundClientRpc(PlayerSoundEvent soundEvent, NoiseLevel noise)
    {
        OnPlayerSound?.Invoke(soundEvent, noise);
    }

    // Son du lean, décidé par le SERVEUR (2026-10-05). Il passait par une RPC où le client
    // demandait lui-même le son, sans aucune borne de débit. Elle était devenue inutile : l'état
    // de lean voyage dans chaque input, donc le serveur voit les transitions lui-même — comme
    // pour la posture, sans rien accepter du client qu'il ne sache déjà.
    private int serverLastLeanState;
    private float serverLastLeanSoundTime = -Mathf.Infinity;

    /// <summary>Intervalle minimal entre deux sons de lean, en temps RÉEL (règle des garde-fous) :
    /// un client modifié qui alternerait son état à chaque input ne doit pas pouvoir saturer les
    /// autres de sons. Largement sous le rythme d'un vrai joueur qui peek.</summary>
    private const float MinLeanSoundInterval = 0.1f;

    private void ServerTrackLeanSound(int state)
    {
        int previous = serverLastLeanState;
        serverLastLeanState = state;

        PlayerSoundEvent? sound = GetLeanSound(previous, state);
        if (!sound.HasValue) return;

        if (Time.time - serverLastLeanSoundTime < MinLeanSoundInterval) return;
        serverLastLeanSoundTime = Time.time;
        BroadcastPlayerSoundClientRpc(sound.Value, ComputeNoiseLevel());
    }

    /// <summary>Son d'une transition de lean (-1 gauche / 0 / +1 droite). Passer directement d'un
    /// côté à l'autre est un nouveau lean, donc il s'entend aussi.</summary>
    public static PlayerSoundEvent? GetLeanSound(int from, int to)
    {
        if (from == to) return null;
        if (to == 0) return PlayerSoundEvent.LeanEnd;
        return PlayerSoundEvent.LeanStart;
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
        if (IsSprinting)
        {
            leanState = 0;
            return;
        }

        if (input.LeanLeftPressedThisFrame) leanState = leanState == -1 ? 0 : -1;
        if (input.LeanRightPressedThisFrame) leanState = leanState == 1 ? 0 : 1;
    }

    /// <summary>
    /// Décalage de lean effectivement autorisé pour un état donné, une fois l'anti-clipping
    /// appliqué : se pencher contre un mur ne doit pas passer la tête à travers.
    /// Partagée entre le propriétaire (prédiction locale, pour le feel caméra) et le serveur
    /// (valeur autoritaire, celle qui déplace le hitbox).
    /// </summary>
    private float ComputeAllowedLeanOffset(int state)
    {
        Stance stance = networkStance.Value.current;
        float targetOffset = state * MaxLeanOffset(stance, state);
        if (Mathf.Abs(targetOffset) <= 0.01f) return targetOffset;

        // Origine calculée, PAS lue sur le CameraPivot (corrigé le 2026-10-05). Seul le
        // propriétaire met ce pivot à jour : sur le serveur, celui d'un client distant restait à
        // hauteur debout même accroupi ou allongé, et près d'un obstacle bas les deux côtés
        // calculaient un lean différent. L'œil de la posture RÉSEAU est le même partout, et
        // ignore le head bob, qui n'a rien à faire dans un calcul autoritaire.
        Vector3 origin = transform.TransformPoint(EyeLocal(stance));
        Vector3 dir = transform.right * Mathf.Sign(targetOffset);
        float desiredDistance = Mathf.Abs(targetOffset);

        if (Physics.Raycast(origin, dir, out RaycastHit hit, desiredDistance + 0.1f, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            float allowed = Mathf.Max(0f, hit.distance - 0.15f);
            targetOffset = Mathf.Sign(targetOffset) * allowed;
        }

        return targetOffset;
    }

    /// <summary>Décalage latéral maximal d'un côté (+1 droite, -1 gauche) dans une posture : le plus
    /// petit de `maxLeanOffset` et de ce que permet l'angle maximal du buste. Allongé, la pose est
    /// asymétrique, donc les deux côtés diffèrent un peu. Ne dépend que de la posture et de données
    /// du prefab, donc identique sur le client et le serveur.</summary>
    private float MaxLeanOffset(Stance stance, int side)
    {
        if (hitbox == null) return maxLeanOffset;
        return Mathf.Min(maxLeanOffset, hitbox.MaxLateralOffset(stance, EyeLocal(stance), maxLeanAngle, side));
    }

    /// <summary>Effet caméra du lean — catégorie C, propriétaire uniquement. Le décalage résultant
    /// sert AUSSI à positionner la surface touchable du propriétaire, prédite sans latence.
    ///
    /// La caméra suit l'ŒIL penché calculé par BodyLayout, pas un simple glissement latéral : le
    /// buste pivotant autour de la colonne, la tête descend un peu en sortant. La caméra est donc
    /// exactement au centre de la tête touchable — on voit depuis l'endroit où l'on est exposé.</summary>
    private void UpdateLeanVisual()
    {
        float targetOffset = ComputeAllowedLeanOffset(leanState);
        currentLeanOffset = StepLean(currentLeanOffset, targetOffset, ref leanVelocity, leanSmoothTime, Time.deltaTime);

        if (hitbox != null)
        {
            // Pendant une transition de posture, l'effet du lean glisse d'une posture à l'autre
            // comme l'œil lui-même : la caméra reste au centre de la tête touchable.
            GetDisplayedStance(out Stance from, out Stance to, out float progress);
            Vector3 eyeFrom = EyeLocal(from);
            Vector3 eyeTo = EyeLocal(to);
            LeanCameraOffset = Vector3.Lerp(hitbox.LeanedEye(from, eyeFrom, currentLeanOffset) - eyeFrom,
                                            hitbox.LeanedEye(to, eyeTo, currentLeanOffset) - eyeTo, progress);

            // Roulis de la caméra proportionnel à l'angle du BUSTE, et non plus au décalage : allongé,
            // le buste roule de 40° pour 15 cm de décalage seulement, et la vue doit le suivre.
            float bustAngle = Mathf.Lerp(hitbox.LeanAngle(from, eyeFrom, currentLeanOffset),
                                         hitbox.LeanAngle(to, eyeTo, currentLeanOffset), progress);
            LeanCameraTilt = -(bustAngle / Mathf.Max(1f, maxLeanAngle)) * maxLeanTilt;
        }
        else
        {
            LeanCameraOffset = new Vector3(currentLeanOffset, 0f, 0f);
            LeanCameraTilt = (currentLeanOffset / Mathf.Max(0.01f, maxLeanOffset)) * -maxLeanTilt;
        }
    }

    /// <summary>
    /// Avance le lean AUTORITAIRE pour un client distant (cas 3), à partir de l'état reçu dans son
    /// snapshot d'input et du dt de ce même input. C'est cette valeur qui déplace son hitbox côté
    /// serveur, donc celle qui décide s'il est touché quand il peek.
    ///
    /// Il est publié avec la pose (ServerPublishPose) et enregistré dans l'historique du rewind
    /// (HitboxPose) au même titre que la position : le tireur voit ce lean en retard, comme le
    /// reste, et le rewind le replace à l'instant que le tireur voyait.
    /// </summary>
    private void ServerAdvanceLean(int state, float dt)
    {
        float targetOffset = ComputeAllowedLeanOffset(state);
        currentLeanOffset = StepLean(currentLeanOffset, targetOffset, ref leanVelocity, leanSmoothTime, dt);
    }

    /// <summary>
    /// Un pas du lean vers sa cible : amortissement critique (SmoothDamp), donc un départ en
    /// douceur, une arrivée en douceur et aucun dépassement — ni en s'inclinant, ni en se
    /// redressant, ni en passant directement d'un côté à l'autre. Fonction pure, testée.
    ///
    /// Partagée par le propriétaire (sa caméra, à son propre rythme d'images) et le serveur (la
    /// surface touchable, au rythme des inputs reçus) : les deux suivent la même courbe, aux
    /// petites différences de pas près, comme avant avec MoveTowards.
    /// </summary>
    public static float StepLean(float current, float target, ref float velocity, float smoothTime, float dt)
    {
        if (dt <= 0f) return current;
        return Mathf.SmoothDamp(current, target, ref velocity, Mathf.Max(0.001f, smoothTime), Mathf.Infinity, dt);
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
        pos.x = currentCameraSide + headBobLateralOffset;
        pos.y = currentCameraHeight + landingDipOffset + headBobOffset;
        pos.z = currentCameraForward;
        cameraPivot.localPosition = pos;
    }

    // ------------------------------------------------------------------
    // Vault — réseauté depuis le 2026-09-28. Le déclenchement et l'avancement vivent dans Move()
    // (fonction déterministe), pas dans Update() : c'est ce qui le rend prédit, rejouable et
    // identique côté serveur. Ne restent ici que la RECHERCHE de cible et les données de tuning.
    // ------------------------------------------------------------------

    private bool TryFindVaultTarget(out Vector3 landingPoint, out float obstacleTopY, bool drawDebug)
    {
        landingPoint = default;
        obstacleTopY = 0f;

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

        // Le point d'arrivée horizontal, au-delà de l'obstacle.
        Vector3 horizontalLanding = topHit.point + forward * vaultLandingProbeDistance;

        // SONDE DE SOL. Sans elle, on atterrissait à l'ALTITUDE DU SOMMET de l'obstacle : le
        // joueur restait perché dessus dès que sa capsule débordait encore un peu au-dessus.
        // Avec une barricade de 0,50 m de profondeur et un rayon de capsule de 0,35 m, arriver
        // 0,25 m derrière la laisse mordre de 10 cm sur le dessus — assez pour que le
        // CharacterController y trouve du sol et s'y arrête. La variable s'appelait pourtant
        // déjà "LandingProbeDistance" : la sonde était prévue, elle n'avait jamais été écrite.
        //
        // La chute est BORNÉE : sans plafond, franchir une barricade au bord d'un vide
        // téléporterait au fond. Au-delà de la borne on garde l'arrivée haute et c'est la
        // gravité qui fait le reste, ce qui est le comportement sûr.
        Vector3 probeOrigin = horizontalLanding + Vector3.up * 0.1f;
        if (Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit groundHit,
                            vaultMaxLandingDrop + 0.1f, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            landingPoint = groundHit.point;
        }
        else
        {
            landingPoint = horizontalLanding;
        }

        obstacleTopY = topHit.point.y;

        if (drawDebug)
        {
            Debug.DrawLine(topHit.point, horizontalLanding, Color.green);
            Debug.DrawLine(horizontalLanding, landingPoint, Color.magenta);
        }
        return true;
    }

    private float vaultTimer;
    private Vector3 vaultStart;
    private Vector3 vaultEnd;

    /// <summary>Altitude que le point haut de l'arc doit atteindre : le sommet de l'obstacle
    /// plus une marge. CINQUIEME valeur cumulative lue par Move(), donc soumise a la regle du
    /// projet : elle doit etre confirmee par le serveur et restauree avant tout rejeu, au meme
    /// titre que vaultStart et vaultEnd.</summary>
    private float vaultPeakY;

    /// <summary>Levé par AdvanceVault à l'atterrissage, consommé par Update() chez le propriétaire.
    /// Le kick caméra est cosmétique : le déclencher depuis Move() le rejouerait à chaque
    /// réconciliation.</summary>
    private bool vaultJustLanded;

    /// <summary>Vrai pendant le rejeu de réconciliation. Un rejeu RE-SIMULE des inputs déjà
    /// prédits : tout effet cosmétique déclenché depuis Move() doit s'y taire, sinon il se
    /// rejoue autant de fois que le client se resynchronise.</summary>
    private bool isReplayingInputs;

    private void BeginVault(Vector3 landingPoint, float obstacleTopY)
    {
#if UNITY_EDITOR
        if (!isReplayingInputs) diagVaultsTotal++;
#endif
        IsVaulting = true;
        vaultTimer = 0f;
        vaultStart = transform.position;
        vaultEnd = landingPoint;
        vaultPeakY = obstacleTopY + vaultObstacleClearance;
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

        // L'amplitude de l'arc n'est PAS une constante : elle est elargie autant qu'il faut
        // pour que le point haut depasse le sommet de l'obstacle.
        //
        // Tant que l'arrivee etait posee AU SOMMET de l'obstacle, c'etait l'interpolation
        // elle-meme qui faisait monter le joueur, et un arc fixe de 0,35 m suffisait a
        // l'habiller. Depuis que la sonde de sol pose l'arrivee AU SOL derriere l'obstacle,
        // les deux bouts sont bas : 0,35 m ne franchit plus rien et le joueur TRAVERSAIT la
        // barricade.
        //
        // La reference est le point BAS des deux extremites, pas leur moyenne : c'est le choix
        // conservateur, il garantit le franchissement meme quand depart et arrivee sont a des
        // hauteurs differentes.
        float basDeLArc = Mathf.Min(vaultStart.y, vaultEnd.y);
        float amplitude = Mathf.Max(vaultArcHeight, vaultPeakY - basDeLArc);
        float arc = vaultHeightCurve.Evaluate(t) * amplitude;

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
    private void RestoreVaultState(bool vaulting, float timer, Vector3 start, Vector3 end, float peakY)
    {
        IsVaulting = vaulting;
        vaultTimer = timer;
        vaultStart = start;
        vaultEnd = end;
        vaultPeakY = peakY;
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
    /// fait piéger CINQ fois sur cette règle (yaw, currentVelocity, hauteur de capsule, état de
    /// vault, vaultPeakY). Seul EtatDeSimulation_SeRestaureEntierement garde réellement cet
    /// invariant : les tests de rejeu partent d'un état pris AVANT le vault et ne voient pas une
    /// restauration incomplète. Toute nouvelle valeur doit y être ajoutée aussi.
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
        public float vaultPeakY;
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
            vaultPeakY = vaultPeakY,
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
        RestoreVaultState(state.vaulting, state.vaultTimer, state.vaultStart, state.vaultEnd, state.vaultPeakY);

        // La scène physique doit voir la nouvelle position AVANT le prochain Move(), sinon le
        // premier sweep part de l'ancienne (autoSyncTransforms vaut false dans ce projet).
        Physics.SyncTransforms();
    }

    /// <summary>Impose une posture, transition comprise, sans passer par le serveur.</summary>
    public void TestSetStance(StanceState state)
    {
        networkStance.Value = state;
    }

    /// <summary>Appelle Move() avec un input construit de toutes pièces, sans passer par le
    /// clavier ni par le réseau. Sans session, aucune transition de posture n'est en cours :
    /// l'instant de simulation n'a donc aucun effet et vaut 0 par défaut.</summary>
    public void TestMove(Vector2 move, float lookX, bool sprintHeld, bool sneakHeld, bool aimHeld, bool jumpPressed, int leanState, float dt, double simTime = 0)
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
        }, dt, simTime);
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
