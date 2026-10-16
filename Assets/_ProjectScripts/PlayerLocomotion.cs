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
        public float aimPitch;   // tangage du regard, en degrés (+ = vers le bas, comme la caméra)
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
    [Tooltip("Approche furtive : plus lente et plus discrète que la marche. Les autres postures en prennent leur part (×0,6 accroupi). Debout, elle joue les marches du Y Bot ; un sneak à 1,3 m/s sur les Rifle Sneak a été essayé le 2026-10-11, puis écarté par l'utilisateur.")]
    [SerializeField] private float sneakSpeed = 2f;
    [SerializeField] private float runSpeed = 8.8f;    // course : rapide mais bruyante
    [SerializeField] private float adsSpeedMultiplier = 0.55f;
    [Tooltip("Vitesse de côté, rapportée à celle vers l'avant (2026-10-10, « comme beaucoup de FPS »). Les diagonales avant gardent toute leur vitesse ; de là, elle décroît jusqu'à cette valeur plein côté. Ressenti ET équilibrage : c'est la vitesse d'un peek en pas chassé.")]
    [SerializeField, Range(0.3f, 1f)] private float strafeSpeedMultiplier = 0.85f;
    [Tooltip("Vitesse à reculer, rapportée à celle vers l'avant. Du côté vers l'arrière, elle décroît jusqu'à cette valeur. Ressenti ET équilibrage : c'est la vitesse à laquelle on rompt un engagement.")]
    [SerializeField, Range(0.3f, 1f)] private float backwardSpeedMultiplier = 0.7f;
    [SerializeField] private float gravity = -18f;

    [Header("Accélération")]
    [Tooltip("m/s² quand on accélère vers une vitesse cible plus élevée.")]
    [SerializeField] private float acceleration = 60f;
    [Tooltip("m/s² quand on freine vers une vitesse cible plus basse (plus rapide que l'accélération pour rester réactif en duel).")]
    [SerializeField] private float deceleration = 80f;
    [Tooltip("m/s² : au-dessus de l'allure de marche, la vitesse ne retombe qu'à ce rythme (2026-10-11). On met du temps à casser un sprint : 10 → 5 m/s en 0,2 s à 25. Ressenti ET équilibrage, avec WeaponData.sprintToFireTime. Seul l'élan dans la direction de course est freiné ainsi : on tourne et on part de côté comme avant, sans jamais aller de côté plus vite qu'un pas de côté normal.")]
    [SerializeField] private float sprintDeceleration = 25f;

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
    // Allongé, on rampe à la vitesse de l'animation (2026-10-10) : la reptation avance à 0,28 m/s,
    // et ×0,085 donne 0,43 m/s à l'allure de marche, soit une cadence de ×1,5. À 1,25 m/s (×0,25
    // jusque-là), le corps glissait trois fois plus vite que ses mains et ses genoux.
    [SerializeField] private StanceProfile proneProfile = new StanceProfile
    {
        controllerHeight = 0.5f, controllerRadius = 0.4f, cameraHeight = 0.36f, cameraForward = 0.25f, cameraSide = -0.15f, moveSpeedMultiplier = 0.085f
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

    /// <summary>SIMULE UN CLIENT MODIFIÉ qui sprinte sans respecter GateSprint : il court en
    /// rechargeant et en frappant, sans annuler son rechargement. Attendu : le serveur annule le
    /// rechargement et fait rater le coup (ServerSprintedSince). Statique, pour le banc de test à
    /// deux instances (NetworkBootstrapUI, « cheatsprint ») ; sans effet en Host.</summary>
    public static bool DebugSprintIgnoresRules;

    /// <summary>SIMULE UN CLIENT MODIFIÉ qui tire et frappe en franchissant un obstacle (2026-10-11).
    /// Attendu : « [Serveur] Tir rejeté : le joueur franchissait un obstacle » et « Coup de taser
    /// annulé ». Statique, pour le banc de test (« cheatvault ») ; marche aussi en Host, dont les
    /// demandes passent par la même file du serveur.</summary>
    public static bool DebugVaultIgnoresRules;
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

    /// <summary>Ce que la direction retire à la vitesse (DirectionSpeedMultiplier), avec les
    /// réglages de ce joueur. Lu aussi par l'affichage, pour que l'allure jouée suive.</summary>
    public float DirectionMultiplier(Vector2 move) => DirectionSpeedMultiplier(move, strafeSpeedMultiplier, backwardSpeedMultiplier);

    /// <summary>
    /// On recule et on va de côté moins vite qu'en avançant (2026-10-10, demandé par l'utilisateur,
    /// comme dans beaucoup de FPS). Jusqu'à 45° de l'avant (les diagonales avant du clavier), toute
    /// la vitesse ; de 45° au plein côté, elle décroît jusqu'à `strafe` ; du côté à l'arrière,
    /// jusqu'à `backward`. Fonction pure de l'input, donc déterministe pour Move(), testée.
    /// </summary>
    public static float DirectionSpeedMultiplier(Vector2 move, float strafe, float backward)
    {
        if (move.sqrMagnitude < 1e-6f) return 1f;
        float angle = Vector2.Angle(Vector2.up, move);
        if (angle <= 45f) return 1f;
        if (angle <= 90f) return Mathf.Lerp(1f, strafe, (angle - 45f) / 45f);
        return Mathf.Lerp(strafe, backward, (angle - 90f) / 90f);
    }

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

    /// <summary>Hauteur du CharacterController dans une posture : le corps d'un spectateur, que
    /// touchent les poings et les cailloux (SpectatorRules), en est la capsule.</summary>
    public float ControllerHeight(Stance stance) => GetStanceProfile(stance).controllerHeight;

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

    /// <summary>Tangage du regard à AFFICHER pour ce joueur sur cette machine, en degrés (+ = vers
    /// le bas, la convention de la caméra). Même aiguillage que la visée : la valeur de la
    /// simulation chez qui la fait tourner, la pose interpolée chez un spectateur. Le buste s'en
    /// sert pour pointer le fusil là où le joueur regarde (PlayerAnimator.ApplyAimDirection).
    ///
    /// Catégorie C réseautée : rien ne la relit dans la simulation, le hitbox ou le tir. Un client
    /// modifié peut mentir sur la sienne, comme sur son lean : il ne change que l'endroit où les
    /// autres VOIENT pointer son fusil, jamais où partent ses balles.</summary>
    public float DisplayAimPitch => Simulates ? AimPitch : displayedPose.aimPitch;

    /// <summary>L'œil de ce joueur tel que cette machine l'AFFICHE, dans le monde : la posture affichée
    /// (transition comprise) et le lean publié, comme la tête touchable. D'où les autres voient partir
    /// son regard : le point du laser tactique (TacticalLaser, 2026-10-16). Catégorie C.</summary>
    public Vector3 DisplayEyePosition
    {
        get
        {
            GetDisplayedStance(out Stance from, out Stance to, out float progress);
            Vector3 eyeFrom = EyeLocal(from);
            Vector3 eyeTo = EyeLocal(to);
            float lean = DisplayLeanOffset;
            if (hitbox != null)
            {
                eyeFrom = hitbox.LeanedEye(from, eyeFrom, lean);
                eyeTo = hitbox.LeanedEye(to, eyeTo, lean);
            }
            else
            {
                eyeFrom.x += lean;
                eyeTo.x += lean;
            }
            return transform.TransformPoint(Vector3.Lerp(eyeFrom, eyeTo, progress));
        }
    }

    /// <summary>La direction du regard de ce joueur telle que cette machine l'AFFICHE : son cap et son
    /// tangage affiché (DisplayAimPitch). Catégorie C.</summary>
    public Vector3 DisplayLookDirection => Quaternion.Euler(DisplayAimPitch, transform.eulerAngles.y, 0f) * Vector3.forward;

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

    /// <summary>Tangage du regard de l'input en cours (degrés, + = vers le bas). Affecté dans
    /// Move() comme IsAiming, donc juste chez le propriétaire et sur le serveur.</summary>
    public float AimPitch { get; private set; }
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
    private PlayerCameraLook cameraLook; // le tangage du regard, chez le propriétaire
    private WeaponController weapon;     // le sprint s'efface devant le rechargement (GateSprint)
    private TaserController taser;       // et devant le coup de taser
    private FirstPersonView firstPersonView; // la lunette du sniper réduit la sensibilité

    /// <summary>Chez le propriétaire : facteur de sensibilité de la souris, réduit dans la lunette
    /// du sniper comme le champ de vision (2026-10-11). Appliqué au mouvement de souris AVANT qu'il
    /// n'entre dans le snapshot : Move() reste une fonction du seul snapshot.</summary>
    private float LookScale => firstPersonView != null ? firstPersonView.LookScale : 1f;

    /// <summary>Chez le propriétaire : voulait-il sprinter à l'image précédente ? Sert à repérer
    /// le moment où il SE MET à courir (GateSprint). Hors simulation : rien dans Move() ne le lit.</summary>
    private bool sprintIntentLastFrame;

    /// <summary>Serveur : numéro du dernier input reçu de ce joueur, et du dernier input simulé où il
    /// voulait sprinter. Chaque demande (tir, rechargement, taser, changement d'arme) annonce le numéro
    /// du dernier input produit avant elle (ServerReferenceInput) : un input de numéro supérieur a été
    /// produit APRÈS l'appui. C'est ce qui permet au serveur d'annuler un rechargement ou un coup
    /// suivis d'un sprint, sans jamais punir le sprint qui les précédait.</summary>
    public int ServerLastReceivedInput { get; private set; } = -1;
    public int ServerLastSprintInput { get; private set; } = -1;

    /// <summary>Chez le joueur : numéro du dernier input produit, que chaque demande annonce au
    /// serveur (voir ServerReferenceInput).</summary>
    public int LastInputSequence => nextInputSequence - 1;

    /// <summary>Avance maximale (en inputs) d'un input de référence annoncé sur le dernier reçu.</summary>
    public const int MaxReferenceLead = 1024;

    /// <summary>
    /// L'input de référence d'une demande : le dernier produit avant elle, celui dont l'état compte
    /// (sortie de sprint, franchissement, visée). Fonction pure, testée.
    ///
    /// Depuis le 2026-10-13, les inputs voyagent en NON FIABLE (voir SubmitInputsServerRpc) et les
    /// demandes en fiable : une demande peut donc arriver avant les inputs qui la précèdent, ou après
    /// d'autres qui la suivent. Elle annonce donc son input de référence. C'est une suggestion bornée :
    /// jamais AVANT le dernier input déjà reçu (un client modifié ne fait pas juger son tir sur un état
    /// passé, d'avant son sprint ou son franchissement : le serveur reste aussi strict qu'avec la
    /// livraison ordonnée), et jamais très loin devant (la demande attend son input au plus 0,3 s).
    /// </summary>
    public static int ReferenceInput(int claimed, int lastReceived)
    {
        if (claimed < lastReceived) return lastReceived;
        return claimed - lastReceived > MaxReferenceLead ? lastReceived + MaxReferenceLead : claimed;
    }

    /// <summary>Serveur : l'input de référence d'une demande arrivée maintenant (ReferenceInput).</summary>
    public int ServerReferenceInput(int claimed) => ReferenceInput(claimed, ServerLastReceivedInput);

    /// <summary>Serveur : le temps (s) depuis le dernier input qui voulait sprinter, tel qu'il était à
    /// la réception de l'input `sequence` : ce que le joueur avait envoyé quand il a tiré ou rechargé.
    /// L'input inconnu (perdu, ou trop ancien), le temps d'après le dernier input reçu.</summary>
    public float ServerTimeSinceSprintAt(int sequence)
    {
        int index = sequence & (ServerInputRecordSize - 1);
        if (sequence >= 0 && serverReceivedSequence[index] == sequence)
            return (float)(serverReceivedClock[index] - serverReceivedLastSprint[index]);
        return ServerTimeSinceSprint;
    }

    /// <summary>Serveur, à la réception de l'input `sequence` : on retient l'horloge des inputs.</summary>
    private void ServerRecordReceivedInput(int sequence)
    {
        int index = sequence & (ServerInputRecordSize - 1);
        serverReceivedSequence[index] = sequence;
        serverReceivedClock[index] = serverInputClock;
        serverReceivedLastSprint[index] = serverLastSprintClock;
    }

    /// <summary>Serveur : le joueur a-t-il voulu sprinter dans un input simulé depuis `firstInput` ?</summary>
    public bool ServerSprintedSince(int firstInput) => ServerLastSprintInput >= firstInput;

    /// <summary>Serveur : numéro du dernier input simulé pendant un franchissement. On ne recharge
    /// pas en franchissant (2026-10-11) : un rechargement suivi d'un input en franchissement est
    /// annulé, comme pour le sprint.</summary>
    public int ServerLastVaultInput { get; private set; } = -1;
    public bool ServerVaultedSince(int firstInput) => ServerLastVaultInput >= firstInput;

    /// <summary>Serveur : numéro du dernier input de ce joueur que le serveur a SIMULÉ. Une demande
    /// de tir, de rechargement ou de changement d'arme attend que l'input qui la précède le soit
    /// (WeaponController.ServerProcessArmsActions) : c'est l'état qui en découle qui compte.</summary>
    public int ServerLastSimulatedInput { get; private set; } = -1;

    // Serveur : ce que chaque input simulé récemment a donné — franchissement après sa simulation,
    // visée demandée. Un anneau de taille fixe, indexé par numéro d'input. 1024 inputs : plus d'une
    // seconde même à 800 images/s, quand une demande n'attend jamais plus de 0,3 s son input.
    private const int ServerInputRecordSize = 1024;
    private readonly int[] serverInputRecordSequence = InitRecordSequences();
    private readonly byte[] serverInputRecordFlags = new byte[ServerInputRecordSize];
    private const byte RecordVaulting = 1, RecordAiming = 2;

    // Serveur : l'horloge des inputs à la RÉCEPTION de chacun (sortie de sprint, voir
    // ServerTimeSinceSprintAt). Même anneau, rempli à l'arrivée et non à la simulation.
    private readonly int[] serverReceivedSequence = InitRecordSequences();
    private readonly double[] serverReceivedClock = new double[ServerInputRecordSize];
    private readonly double[] serverReceivedLastSprint = new double[ServerInputRecordSize];

    private static int[] InitRecordSequences()
    {
        var sequences = new int[ServerInputRecordSize];
        for (int i = 0; i < sequences.Length; i++) sequences[i] = -1;
        return sequences;
    }

    /// <summary>Serveur, après la simulation d'un input : on retient ce qu'il a donné.</summary>
    private void ServerRecordSimulatedInput(int sequence, bool aiming)
    {
        int index = sequence & (ServerInputRecordSize - 1);
        serverInputRecordSequence[index] = sequence;
        serverInputRecordFlags[index] = (byte)((IsVaulting ? RecordVaulting : 0) | (aiming ? RecordAiming : 0));
        ServerLastSimulatedInput = Mathf.Max(ServerLastSimulatedInput, sequence);
        if (weapon != null) weapon.ServerProcessArmsActions();
    }

    /// <summary>Serveur : l'état qui suivait la simulation de l'input `sequence` — franchissait-il,
    /// visait-il ? Faux s'il n'est pas (ou plus) connu.</summary>
    public bool ServerInputFlags(int sequence, out bool vaulting, out bool aiming)
    {
        int index = sequence & (ServerInputRecordSize - 1);
        bool known = sequence >= 0 && serverInputRecordSequence[index] == sequence;
        byte flags = known ? serverInputRecordFlags[index] : (byte)0;
        vaulting = (flags & RecordVaulting) != 0;
        aiming = (flags & RecordAiming) != 0;
        return known;
    }

    // Serveur : l'arme en main input par input (2026-10-11). Son poids pèse sur la vitesse dans
    // Move() ; le serveur ne croit pas le client, il applique les changements d'arme qu'il a reçus,
    // à partir de l'input qui suit chacun.
    private readonly Queue<(int fromInput, byte slot)> serverWeaponSlotChanges = new Queue<(int, byte)>();
    private byte serverWeaponSlot;

    /// <summary>Serveur, à l'arrivée d'un changement d'arme : l'arme `slot` est en main à partir de
    /// l'input `fromInput` (le premier produit après la demande).</summary>
    public void ServerScheduleWeaponSlot(int fromInput, byte slot)
    {
        // Borne de la file (2026-10-12) : elle ne se vide qu'au fil des inputs simulés, donc un client
        // modifié qui changerait d'arme sans envoyer d'input la ferait grandir sans limite. Un joueur
        // honnête n'a jamais plus d'un ou deux changements en attente.
        if (serverWeaponSlotChanges.Count >= 64) return;
        serverWeaponSlotChanges.Enqueue((fromInput, slot));
    }

    /// <summary>Serveur : l'arme en main pour l'input `sequence`, changements reçus appliqués.</summary>
    private byte ServerWeaponSlotFor(int sequence)
    {
        while (serverWeaponSlotChanges.Count > 0 && serverWeaponSlotChanges.Peek().fromInput <= sequence)
            serverWeaponSlot = serverWeaponSlotChanges.Dequeue().slot;
        return serverWeaponSlot;
    }

    /// <summary>Le poids de l'arme `slot` : sa vitesse de déplacement relative (1 sans arme, et pour un
    /// spectateur, qui n'en porte pas).</summary>
    private float WeaponSpeedMultiplier(byte slot) => weapon != null && !IsSpectator ? weapon.SpeedMultiplierOf(slot) : 1f;

    /// <summary>Le poids de l'arme affichée en main : la vitesse de référence de l'animation le suit.</summary>
    public float DisplayedWeaponSpeedMultiplier => weapon != null && !IsSpectator ? weapon.SpeedMultiplierOf(weapon.DisplayedSlot) : 1f;

    // ------------------------------------------------------------------
    // Le rôle (2026-10-13) : duelliste ou spectateur de la tribune (GDD § 4 et § 9)
    // ------------------------------------------------------------------

    private PlayerMatchState matchState;      // le rôle, écrit par le serveur
    private SpectatorController spectator;    // l'étourdissement d'un spectateur fige son déplacement

    /// <summary>
    /// Ce joueur est-il spectateur ? Variable réseau écrite par le serveur : juste partout. Un spectateur
    /// se déplace comme un duelliste (même prédiction, même réconciliation), à mains nues : ni visée,
    /// ni tir, ni lean, ni franchissement, ni position allongée (filtrés chez le joueur ET par le
    /// serveur, SpectatorInputFilter). Les phases de manche ne le gèlent pas ; seul un étourdissement
    /// l'arrête (SpectatorController).
    /// </summary>
    public bool IsSpectator => matchState != null && matchState.IsSpectator;

    /// <summary>Ce qu'un spectateur envoie de ses commandes : ni visée, ni tir, ni lean, ni
    /// franchissement. Appliquée chez le joueur avant le snapshot, et par le serveur à l'arrivée : un
    /// client modifié n'y gagne rien. Fonction pure, testée.</summary>
    public static void SpectatorInputFilter(ref bool aimHeld, ref bool fireHeld, ref bool firePressed, ref int leanState, ref bool jumpPressed)
    {
        aimHeld = false;
        fireHeld = false;
        firePressed = false;
        leanState = 0;
        jumpPressed = false;
    }

    private static MovementInputSnapshot FilterForSpectator(MovementInputSnapshot snap)
    {
        SpectatorInputFilter(ref snap.aimHeld, ref snap.fireHeld, ref snap.firePressedThisFrame, ref snap.leanState, ref snap.jumpPressed);
        snap.weaponSlot = 0;
        return snap;
    }

    /// <summary>Un spectateur se déplace-t-il à l'instant de simulation `simTime` ? Toujours, sauf
    /// étourdi : il n'est gelé ni par la préparation ni par le décompte des duellistes.</summary>
    private bool SpectatorMovementAllowed(double simTime) => spectator == null || !spectator.IsStunnedAt(simTime);

    // Sortie de sprint (2026-10-11) : le temps écoulé depuis le dernier input qui voulait sprinter,
    // compté sur les inputs eux-mêmes (la somme de leurs durées, bornées comme sur le serveur). Le
    // joueur compte les inputs qu'il a envoyés, le serveur ceux qu'il a reçus : une demande (tir,
    // rechargement) voyage par RPC fiable, livrée après tous les inputs envoyés avant elle, donc les
    // deux comptes sont identiques au moment où le serveur la juge. Aucune gigue du réseau n'y entre.
    private double localInputClock, localLastSprintClock = double.NegativeInfinity;
    private double serverInputClock, serverLastSprintClock = double.NegativeInfinity;

    /// <summary>Chez le propriétaire : temps (s) depuis le dernier input envoyé qui voulait sprinter.</summary>
    public float TimeSinceSprint => (float)(localInputClock - localLastSprintClock);

    /// <summary>Serveur : le même temps, sur les inputs reçus de ce joueur.</summary>
    public float ServerTimeSinceSprint => (float)(serverInputClock - serverLastSprintClock);

    /// <summary>Avance une horloge d'inputs d'un input de durée `dt` (bornée), et la marque si cet
    /// input voulait sprinter. Fonction pure, testée.</summary>
    public static void AdvanceInputClock(ref double clock, ref double lastSprint, float dt, bool sprint)
    {
        clock += Mathf.Clamp(dt, 0f, MaxSingleInputDeltaTime);
        if (sprint) lastSprint = clock;
    }

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
    // Deux variables seulement. La POSE d'affichage, datée sur l'horloge du serveur : position,
    // rotation, lean et drapeaux d'animation (au sol, en franchissement, en visée). La variable ne
    // change qu'à l'apparition et aux replacements ; le reste du temps, la pose part en non fiable à
    // chaque tick (PoseClientRpc, 2026-10-13). Et la POSTURE avec sa transition. Elles remplacent sept
    // variables, dont trois drapeaux qui n'existaient que pour l'animation d'un spectateur — lequel
    // n'appelle jamais Move() et n'a donc aucun autre moyen de savoir qu'un adversaire tombe, franchit
    // ou épaule.
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
        public float aimPitch; // tangage du regard, affichage seulement — voir DisplayAimPitch
        public bool fireHeld;
        public bool firePressedThisFrame;
        public int leanState; // -1 gauche / 0 / +1 droite — voir UpdateLeanState
        public bool jumpPressed; // déclenche un vault si un obstacle franchissable est devant
        public byte weaponSlot;  // l'arme en main : son poids pèse sur la vitesse (le serveur la fixe lui-même)
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
    // Les inputs en route vers le serveur : un flux NON FIABLE et redondant (2026-10-13)
    //
    // Jusque-là, chaque image envoyait son input par une RPC fiable et ordonnée. Mesuré à deux
    // instances avec 2 % de paquets perdus (le quotidien d'une partie par internet) : un seul paquet
    // perdu retenait tous les messages suivants jusqu'à son renvoi, inputs, tirs et sons compris.
    // L'hôte voyait le robot du client figé sur 21 à 33 % de ses images, puis bondir de 50 cm. Et à
    // une RPC par image, sans plafond d'images, ces paquets se comptaient par centaines par seconde.
    //
    // Désormais les inputs partent en non fiable, au plus InputSendRate fois par seconde, et chaque
    // envoi répète ceux des InputRedundantSends envois précédents que le serveur n'a pas encore
    // confirmés : un paquet perdu est couvert par le suivant, sans rien bloquer. Le serveur ignore ce
    // qu'il a déjà reçu. Les demandes (tir, rechargement...) restent fiables et annoncent leur input
    // de référence (ServerReferenceInput), puisqu'elles ne voyagent plus dans le même flux.
    // ------------------------------------------------------------------

    /// <summary>Envois d'inputs par seconde, au plus : en pratique un par image, sauf à un nombre
    /// d'images démesuré. Mesuré à deux instances : plafonné à 120, l'hôte (104 images/s) recevait
    /// moins souvent du neuf d'un client à 177 images/s, et voyait son robot immobile sur 25 % de ses
    /// images au lieu de 17 % (dette n° 30). Le débit reste celui d'avant le flux non fiable.</summary>
    public const float InputSendRate = 250f;

    /// <summary>Envois précédents dont chaque envoi répète les inputs non confirmés : jusqu'à ce
    /// nombre de paquets perdus d'affilée, rien n'est perdu.</summary>
    public const int InputRedundantSends = 2;

    /// <summary>Inputs par paquet, au plus (21 octets chacun : le paquet tient dans une trame).</summary>
    public const int MaxInputsPerPacket = 40;

    /// <summary>Un input tel qu'il voyage : le snapshot de Move() et sa durée, les booléens et le lean
    /// rangés dans un octet. Les flottants voyagent tels quels : le serveur doit simuler exactement ce
    /// que le joueur a prédit.</summary>
    public struct InputWire
    {
        public Vector2 move;
        public float lookX;
        public float aimPitch;
        public float deltaTime;
        public byte buttons;
    }

    private const byte WireSprint = 1, WireSneak = 2, WireAim = 4, WireFireHeld = 8, WireFirePressed = 16, WireJump = 32;
    private const int WireLeanShift = 6; // 2 bits : 0 aucun, 1 gauche, 2 droite

    private static InputWire Pack(MovementInputSnapshot snap, float dt)
    {
        int buttons = (snap.sprintHeld ? WireSprint : 0) | (snap.sneakHeld ? WireSneak : 0) | (snap.aimHeld ? WireAim : 0)
                      | (snap.fireHeld ? WireFireHeld : 0) | (snap.firePressedThisFrame ? WireFirePressed : 0)
                      | (snap.jumpPressed ? WireJump : 0);
        int lean = snap.leanState < 0 ? 1 : snap.leanState > 0 ? 2 : 0;
        return new InputWire
        {
            move = snap.move,
            lookX = snap.lookX,
            aimPitch = snap.aimPitch,
            deltaTime = dt,
            buttons = (byte)(buttons | (lean << WireLeanShift)),
        };
    }

    private static MovementInputSnapshot Unpack(InputWire wire)
    {
        int lean = (wire.buttons >> WireLeanShift) & 3;
        return new MovementInputSnapshot
        {
            move = wire.move,
            lookX = wire.lookX,
            aimPitch = wire.aimPitch,
            sprintHeld = (wire.buttons & WireSprint) != 0,
            sneakHeld = (wire.buttons & WireSneak) != 0,
            aimHeld = (wire.buttons & WireAim) != 0,
            fireHeld = (wire.buttons & WireFireHeld) != 0,
            firePressedThisFrame = (wire.buttons & WireFirePressed) != 0,
            jumpPressed = (wire.buttons & WireJump) != 0,
            leanState = lean == 1 ? -1 : lean == 2 ? 1 : 0, // 3 n'existe pas : un client modifié, sans lean
        };
    }

    /// <summary>Un envoi d'inputs : des numéros consécutifs à partir de `firstSequence`. Reçu, il est
    /// relu dans un tampon partagé (le serveur traite chaque RPC aussitôt reçue) : rien n'est alloué.</summary>
    public struct InputPacket : INetworkSerializable
    {
        public int firstSequence;
        public int count;
        public InputWire[] inputs;

        private static readonly InputWire[] received = new InputWire[MaxInputsPerPacket];

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref firstSequence);
            byte n = (byte)count;
            serializer.SerializeValue(ref n);
            count = n;
            if (serializer.IsReader)
            {
                inputs = received;
                // Plus que la borne : un client modifié, le paquet est ignoré.
                if (count > MaxInputsPerPacket) { count = 0; return; }
            }
            for (int i = 0; i < count; i++)
            {
                serializer.SerializeValue(ref inputs[i].move);
                serializer.SerializeValue(ref inputs[i].lookX);
                serializer.SerializeValue(ref inputs[i].aimPitch);
                serializer.SerializeValue(ref inputs[i].deltaTime);
                serializer.SerializeValue(ref inputs[i].buttons);
            }
        }
    }

    /// <summary>
    /// Le premier input d'un envoi : le plus ancien des envois gardés en redondance (`windowStart`),
    /// sans jamais repartir d'un input déjà confirmé par le serveur, ni dépasser `maxCount` inputs.
    /// Fonction pure, testée.
    /// </summary>
    public static int FirstInputToSend(int newest, int lastAcked, int windowStart, int maxCount)
        => Mathf.Max(Mathf.Max(lastAcked + 1, windowStart), newest - maxCount + 1);

    /// <summary>Attente maximale (s) d'un input manquant, quand de plus récents sont déjà arrivés.</summary>
    public const double InputReorderWait = 0.05;

    /// <summary>
    /// Serveur : remet dans l'ordre les inputs d'un joueur. Un paquet non fiable peut en doubler un
    /// autre : mesuré à deux instances avec 8 ms de gigue, à 180 envois par seconde, un paquet était
    /// doublé par deux ou trois suivants, et le serveur sautait les inputs qu'il portait seul (2 à 6
    /// toutes les 3 s, sans aucune perte, et des recalages). Les inputs arrivés en avance attendent
    /// donc les manquants, au plus InputReorderWait ; passé ce délai, les manquants sont perdus pour de
    /// bon (tous leurs envois perdus) et sautés, le recalage du joueur rattrape l'écart. Sans état
    /// réseau, testée.
    /// </summary>
    public sealed class InputReorderBuffer
    {
        private const int Size = 1024;
        private readonly int[] sequences = new int[Size];
        private readonly InputWire[] wires = new InputWire[Size];
        private int highestStored = -1;
        private double gapSince = double.NaN;
        private int pendingLost;

        public InputReorderBuffer() => Reset(0);

        /// <summary>Le prochain input attendu.</summary>
        public int NextExpected { get; private set; }

        public void Reset(int nextExpected)
        {
            for (int i = 0; i < Size; i++) sequences[i] = -1;
            NextExpected = nextExpected;
            highestStored = nextExpected - 1;
            gapSince = double.NaN;
        }

        /// <summary>Range un input reçu ; ignoré s'il est déjà passé. Un input plus loin devant que
        /// le tampon ne porte (après une longue coupure à un grand nombre d'images par seconde) : tout
        /// ce qui le précède est perdu, on repart de lui.</summary>
        public bool Store(int sequence, InputWire wire)
        {
            if (sequence < NextExpected) return false;
            if (sequence - NextExpected >= Size)
            {
                pendingLost += sequence - NextExpected;
                Reset(sequence);
            }
            int index = sequence & (Size - 1);
            if (sequences[index] == sequence) return false; // déjà reçu (redondance)
            sequences[index] = sequence;
            wires[index] = wire;
            if (sequence > highestStored) highestStored = sequence;
            return true;
        }

        /// <summary>Le prochain input dans l'ordre, s'il est là. `lost` : les inputs sautés juste
        /// avant lui, après une attente de plus de `maxWait` secondes.</summary>
        public bool TryTake(double now, double maxWait, out int sequence, out InputWire wire, out int lost)
        {
            lost = pendingLost;
            pendingLost = 0;
            sequence = NextExpected;
            wire = default;
            int index = NextExpected & (Size - 1);
            if (sequences[index] != NextExpected)
            {
                // Rien de plus récent n'attend : il ne manque rien, le joueur n'a simplement rien envoyé.
                if (highestStored < NextExpected)
                {
                    gapSince = double.NaN;
                    return false;
                }
                if (double.IsNaN(gapSince)) gapSince = now;
                if (now - gapSince < maxWait) return false;
                // Les manquants ne viendront plus : on saute jusqu'au premier arrivé.
                while (sequences[NextExpected & (Size - 1)] != NextExpected)
                {
                    NextExpected++;
                    lost++;
                }
                index = NextExpected & (Size - 1);
            }
            sequence = NextExpected;
            wire = wires[index];
            sequences[index] = -1;
            NextExpected++;
            gapSince = double.NaN;
            return true;
        }
    }

    private readonly InputReorderBuffer serverReorder = new InputReorderBuffer();

    private struct OutgoingInput
    {
        public int sequence;
        public InputWire wire;
    }

    // Chez le joueur : les inputs envoyés ou à envoyer, pas encore confirmés, par numéro croissant.
    private readonly List<OutgoingInput> outgoingInputs = new List<OutgoingInput>();
    private readonly InputWire[] packetBuffer = new InputWire[MaxInputsPerPacket];
    private readonly int[] sendWindowStarts = new int[InputRedundantSends + 1];
    private int sendCount;
    private int lastSentInput = -1;
    private int lastAckedInput = -1;
    private float inputSendTimer;

    /// <summary>Chez le joueur : dernière correction appliquée. Elles voyagent en non fiable : une plus
    /// ancienne qui arrive après est périmée.</summary>
    private int lastCorrectionSequence = -1;

    private void QueueInputForServer(int sequence, MovementInputSnapshot snap, float dt)
    {
        outgoingInputs.Add(new OutgoingInput { sequence = sequence, wire = Pack(snap, dt) });
        // Borne de mémoire : un serveur qui ne confirme plus rien (coupure) n'en fait pas grossir la liste.
        if (outgoingInputs.Count > 4 * MaxInputsPerPacket) outgoingInputs.RemoveRange(0, outgoingInputs.Count - 4 * MaxInputsPerPacket);
    }

    /// <summary>Chez le joueur : envoie au serveur les inputs pas encore partis, avec les précédents
    /// gardés en redondance. Appelé au rythme d'InputSendRate, et juste avant chaque demande, qui
    /// voyage ainsi avec l'input dont elle dépend.</summary>
    public void SendPendingInputs()
    {
        if (!IsSpawned || !IsOwner || IsServer || outgoingInputs.Count == 0) return;
        int newest = outgoingInputs[outgoingInputs.Count - 1].sequence;
        if (newest <= lastSentInput) return;

        sendWindowStarts[sendCount % sendWindowStarts.Length] = lastSentInput + 1;
        int windowStart = sendWindowStarts[Mathf.Max(0, sendCount - InputRedundantSends) % sendWindowStarts.Length];
        sendCount++;
        int first = FirstInputToSend(newest, lastAckedInput, windowStart, MaxInputsPerPacket);

        // Ce qui précède le premier input envoyé ne repartira plus.
        int stale = 0;
        while (stale < outgoingInputs.Count && outgoingInputs[stale].sequence < first) stale++;
        if (stale > 0) outgoingInputs.RemoveRange(0, stale);

        int count = 0;
        for (int i = 0; i < outgoingInputs.Count && count < MaxInputsPerPacket; i++) packetBuffer[count++] = outgoingInputs[i].wire;
        SubmitInputsServerRpc(new InputPacket { firstSequence = outgoingInputs[0].sequence, count = count, inputs = packetBuffer });
        lastSentInput = newest;
        NetStats.InputPacketsSent++;
        NetStats.InputsSent += count;
#if UNITY_EDITOR
        diagPacketsSent++;
        diagInputsSent += count;
#endif
    }

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
    private double lastReceivedPoseTime = double.NegativeInfinity;

    // Serveur : la pose de cette image, et le dernier tick réseau où elle est partie.
    private int lastPoseTick = -1;

    // Serveur : la correction à envoyer au propriétaire (voir ApplyBufferedServerInputs).
    private bool serverCorrectionPending;
    private int serverCorrectionSequence = -1;
    private float serverCorrectionSentAt = float.NegativeInfinity;
    private ClientRpcParams ownerRpcParams;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = GetComponent<PlayerInputReader>();
        if (cameraPivot != null) cameraLook = cameraPivot.GetComponent<PlayerCameraLook>();
        weapon = GetComponentInChildren<WeaponController>();
        taser = GetComponentInChildren<TaserController>();
        firstPersonView = GetComponent<FirstPersonView>();
        health = GetComponent<Health>();
        matchState = GetComponent<PlayerMatchState>();
        spectator = GetComponent<SpectatorController>();
        aliveLayer = gameObject.layer;
        ApplyStanceImmediate(Stance.Standing);

        // Voir commentaire équivalent de l'ancienne version : évite que le joueur ne se bloque
        // lui-même sur ses propres tests de chevauchement (CheckCapsule pour CanStandUp). Les épaves
        // (layer Corpse) n'en font pas partie non plus : elles ne tombent pas pareil sur deux
        // machines, et Move() doit rester déterministe (pas de franchissement d'une épave, pas de
        // lean qui s'y cogne).
        obstacleMask &= ~(1 << gameObject.layer);
        if (CorpseLayer.Index >= 0) obstacleMask &= ~CorpseLayer.Mask;
    }

    // ------------------------------------------------------------------
    // La mort, côté simulation (2026-10-12)
    // ------------------------------------------------------------------

    private Health health;
    private int aliveLayer;
    private bool lifeStateDead;
    private bool hitboxShown = true;

    /// <summary>Ce joueur est-il en vie ? La vie est une variable réseau : juste partout.</summary>
    public bool IsAlive => health == null || !health.IsDead;

    /// <summary>
    /// Un robot mort n'est plus qu'une épave (PlayerDeath, cosmétique). Ce qui en reste dans la
    /// simulation s'efface aussi, sur toutes les machines : sa capsule de mouvement passe sur le layer
    /// Corpse, qui ne touche que le décor (le survivant, qui garde la main en fin de manche, traverse
    /// la place où il est tombé au lieu de buter sur un mur invisible), et sa surface touchable
    /// s'éteint (les tirs du survivant vont au sol ou à l'épave, pas dans le vide où il se tenait).
    /// Rendu en vie au début de la manche suivante.
    ///
    /// Un spectateur n'a pas de surface touchable non plus (2026-10-13) : il est hors du duel, « à mains
    /// nues, sans arme ni hitbox de duel » (CLAUDE.md). Les poings et les cailloux le touchent par sa
    /// capsule calculée (SpectatorRules), jamais par ces zones.
    /// </summary>
    private void ApplyLifeState()
    {
        bool dead = !IsAlive;
        if (dead != lifeStateDead)
        {
            lifeStateDead = dead;
            int corpseLayer = CorpseLayer.Index;
            if (corpseLayer >= 0) gameObject.layer = dead ? corpseLayer : aliveLayer;
        }

        bool queryable = !dead && !IsSpectator;
        if (queryable == hitboxShown) return;
        hitboxShown = queryable;
        if (hitbox != null) hitbox.SetQueryable(queryable);
    }

    public override void OnNetworkSpawn()
    {
        // Les corrections ne vont qu'au propriétaire : la cible est fixée une fois pour toutes.
        ownerRpcParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } } };
        lastCorrectionSequence = -1;
        lastAckedInput = -1;
        lastSentInput = -1;
        outgoingInputs.Clear();

        if (IsServer)
        {
            // Place le joueur sur un point de spawn AVANT de publier sa position : sans ça, tout
            // le monde apparaît à la position du prefab, donc les uns sur les autres.
            ServerMoveToSpawnPoint();

            // Le SERVEUR publie la pose où il vient de faire apparaître ce joueur. Sans ça, elle
            // vaut encore default = (0,0,0) à cet instant, et la branche cliente ci-dessous
            // téléportait tout le monde à l'origine du monde. Invisible tant que la scène de test
            // spawne près de l'origine, et fatal dès qu'il y a de vrais points de spawn opposés.
            ServerPublishPoseReliably();
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
            lastReceivedPoseTime = pose.time;
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

    // Le flux d'inputs (2026-10-13) : paquets envoyés et inputs qu'ils portaient (redondance comprise)
    // chez le joueur ; inputs perdus pour de bon (tous leurs envois perdus) et paquets qui
    // n'apportaient rien de neuf, sur le serveur.
    private int diagPacketsSent;
    private int diagInputsSent;
    private static int diagServerInputsLost;
    private static int diagServerPacketsUseless;
    private int diagCorrectionsStale;

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
                  $"paquets_inputs={diagPacketsSent / 3f:F0}/s inputs_par_paquet={(diagPacketsSent > 0 ? (float)diagInputsSent / diagPacketsSent : 0f):F1} " +
                  $"corrections_perimees={diagCorrectionsStale}");

        diagCorrections = 0;
        diagResyncs = 0;
        diagErrorSum = 0f;
        diagMaxError = 0f;
        diagPacketsSent = 0;
        diagInputsSent = 0;
        diagCorrectionsStale = 0;
    }

    private static float diagServerNextReportAt;

    /// <summary>[DIAG-INPUTS], sur le serveur : ce que le flux non fiable a perdu pour de bon. Doit
    /// rester à 0 hors pertes en rafale (plus de InputRedundantSends paquets d'affilée).</summary>
    private void DiagServerReportIfDue()
    {
        if (!IsServer || IsOwner) return;
        if (Time.time < diagServerNextReportAt && diagServerNextReportAt - Time.time <= 3f) return;
        diagServerNextReportAt = Time.time + 3f;
        Debug.Log($"[DIAG-INPUTS] inputs_perdus={diagServerInputsLost} paquets_sans_neuf={diagServerPacketsUseless} file={serverInputQueue.Count}");
        diagServerInputsLost = 0;
        diagServerPacketsUseless = 0;
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

        // Une liste plutôt qu'une file : on a besoin d'un accès indexé pour interpoler entre deux poses.
        // Rangée par date : les poses périmées sont en tête (une lambda à chaque image allouait).
        RemoveOlderThan(serverHitboxHistory, now - hitboxHistoryDuration);
    }

    /// <summary>Retire de la tête d'un historique rangé par date les poses antérieures à `cutoff`.</summary>
    private static void RemoveOlderThan(List<HitboxPose> history, double cutoff)
    {
        int stale = 0;
        while (stale < history.Count && history[stale].time < cutoff) stale++;
        if (stale > 0) history.RemoveRange(0, stale);
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

    /// <summary>Serveur : la pose de ce joueur à un instant passé (position, orientation, posture), tirée
    /// de l'historique du rewind. Le coup de poing d'un spectateur s'en sert pour frapper un autre
    /// spectateur là où il le voyait, comme le taser (SpectatorController).</summary>
    public bool ServerTrySamplePose(double targetTime, out HitboxPose pose)
    {
        pose = default;
        return IsServer && TrySampleHistory(targetTime, out pose);
    }

    /// <summary>L'instant (horloge du serveur) auquel CETTE machine affiche les autres joueurs : celui
    /// de leur dernière interpolation, 0 s'il n'y en a pas (sur le serveur, qui les montre au présent).
    /// Les objets lancés par les spectateurs s'affichent au même instant (ArenaItem) : on voit un
    /// caillou arriver sur un robot au moment où l'on voit ce robot.</summary>
    public static double RemoteViewTime => shownViewTime;

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

        // Positions des autres joueurs DÉJÀ présents, pour ne pas faire apparaître le nouveau venu
        // dans les pieds de son adversaire. Le serveur spawne les joueurs séquentiellement, donc le
        // second voit bien le premier. Seuls comptent ceux du même rôle (2026-10-13) : un duelliste
        // apparaît dans l'arène, loin de l'autre duelliste ; un spectateur sur la tribune, loin des
        // autres spectateurs.
        bool spectating = IsSpectator;
        var occupied = new List<Vector3>();
        foreach (PlayerLocomotion other in spawnedPlayers)
        {
            if (other == null || other == this || other.IsSpectator != spectating) continue;
            occupied.Add(other.transform.position);
        }

        Vector3 spawnPosition;
        float spawnYaw;
        if (spectating)
        {
            Tribune tribune = Tribune.Instance;
            if (tribune == null || !tribune.TryGetSpectatorSpawn(occupied, out spawnPosition, out spawnYaw)) return;
        }
        else
        {
            PlayerSpawnPoints points = PlayerSpawnPoints.Instance;
            if (points == null || !points.TryGetSpawnPointFarthestFrom(occupied, out spawnPosition, out spawnYaw)) return;
        }

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
        serverVaultLandingPending = false; // ni une réception au point d'apparition

        // Le LEAN demande en plus une RPC vers le propriétaire, contrairement à tout le reste.
        // Son ÉTAT (-1/0/+1) est une bascule qui vit sur le client : remettre le décalage à zéro
        // côté serveur ne suffirait pas, le propriétaire se repencherait dès la frame suivante
        // puisque sa touche est toujours considérée comme enclenchée.
        ResetLeanClientRpc(ownerRpcParams);

        serverTeleportId++;
        ServerPublishPoseReliably();
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
        ApplyLifeState();

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

            // Numérotés comme ceux d'un client distant, pour que le rechargement et le taser du
            // Host suivent la même règle face au sprint (ServerSprintedSince).
            int sequence = nextInputSequence++;
            ServerLastReceivedInput = sequence;
            bool sprintInput = WantsToSprint(snap.sprintHeld, snap.move, snap.aimHeld, snap.fireHeld);
            AdvanceInputClock(ref localInputClock, ref localLastSprintClock, dt, sprintInput);
            AdvanceInputClock(ref serverInputClock, ref serverLastSprintClock, dt, sprintInput);
            ServerRecordReceivedInput(sequence);

            ServerCheckAutoStand(snap);
            bool wasVaulting = IsVaulting;
            float fallSpeed = -verticalVelocity;
            Move(snap, dt, ServerClock);
            CancelReloadIfVaulting();
            if (sprintInput) ServerLastSprintInput = sequence;
            if (IsVaulting) ServerLastVaultInput = sequence;
            ServerRecordSimulatedInput(sequence, snap.aimHeld);
            ServerTrackLeanSound(snap.leanState);
            ServerTrackVaultSound(wasVaulting, wasGrounded, fallSpeed);

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

        // Publication serveur, même sans input traité : l'historique du rewind à CHAQUE frame, sinon
        // il aurait des trous pendant les micro-coupures réseau, précisément quand il sert le plus ;
        // la pose des spectateurs à chaque tick, sinon elle resterait figée (un adversaire garderait
        // son animation de chute après avoir atterri). Placée APRÈS le bloc du propriétaire, pour que
        // le Host publie le lean de cette frame et non celui de la précédente.
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
        DiagServerReportIfDue();
#endif
    }

    /// <summary>Chez le propriétaire, après sa simulation (jamais dans Move(), qu'un rejeu
    /// rappellerait) : on ne recharge pas en franchissant un obstacle (2026-10-11), le
    /// franchissement annule le rechargement en cours. Le serveur fait de même de son côté
    /// (ServerVaultedSince).</summary>
    private void CancelReloadIfVaulting()
    {
        if (IsVaulting && weapon != null && weapon.IsReloading) weapon.CancelLocalReload();
    }

    private MovementInputSnapshot ReadLocalInput()
    {
        // Le sprint s'efface devant le rechargement et le taser, et se mettre à courir annule un
        // rechargement (GateSprint). Décidé ici, chez le propriétaire, et envoyé tel quel : Move()
        // reste une fonction du seul snapshot. Le serveur vérifie de son côté (ServerSprintedSince).
        // Un spectateur n'a ni arme ni taser : son clic gauche frappe ou lance (SpectatorController), et
        // c'est le coup, l'élan d'un lancer ou l'étourdissement qui arrêtent son sprint.
        bool spectating = IsSpectator;
        bool intent = spectating
            ? WantsToSprint(input.SprintHeld, input.MoveInput, false, false)
            : WantsToSprint(input.SprintHeld, input.MoveInput, input.AimHeld, input.FireHeld);
        // Un rechargement en attente (sortie de sprint, franchissement) arrête le sprint comme un
        // rechargement en cours : il partira dès que l'arme sera remontée.
        bool reloading = !spectating && weapon != null && (weapon.IsReloading || weapon.ReloadPending);
        bool striking = spectating ? spectator != null && spectator.LocalBlocksSprint : taser != null && taser.IsStriking;
        bool sprintAllowed = GateSprint(intent, ref sprintIntentLastFrame, reloading, striking, out bool cancelReload);
#if UNITY_EDITOR
        if (DebugSprintIgnoresRules)
        {
            sprintAllowed = true;
            cancelReload = false;
        }
        if (cancelReload) Debug.Log("[DIAG-CHARGEUR] rechargement annulé par le sprint (joueur)");
#endif
        if (cancelReload) weapon.CancelLocalReload();

        // Un robot mort n'envoie plus que l'écoulement du temps : sa souris fait tourner la caméra
        // qui regarde son épave (PlayerDeath), pas son corps.
        if (!IsAlive)
        {
            return new MovementInputSnapshot
            {
                aimPitch = cameraLook != null ? cameraLook.Pitch : 0f,
                leanState = leanState,
                weaponSlot = weapon != null ? weapon.LocalSlot : (byte)0,
            };
        }

        var snapshot = new MovementInputSnapshot
        {
            move = input.MoveInput,
            // La sensibilité du menu Options vaut pour les deux axes : PlayerCameraLook l'applique au
            // tangage, et ici au lacet, avant le snapshot (Move() reste une fonction du seul
            // snapshot). Oubliée jusqu'au 2026-10-12 : régler la sensibilité ne changeait que l'axe
            // vertical.
            lookX = input.LookInput.x * LookScale * GameSettings.SensitivityMultiplier,
            sprintHeld = input.SprintHeld && sprintAllowed,
            sneakHeld = input.SneakHeld,
            aimHeld = input.AimHeld,
            aimPitch = cameraLook != null ? cameraLook.Pitch : 0f,
            fireHeld = input.FireHeld,
            firePressedThisFrame = input.FirePressedThisFrame,
            leanState = leanState,
            // On ne franchit pas d'obstacle pendant un coup de taser (2026-10-11) : le geste va à son
            // terme, comme pour le sprint. Filtré ici, hors de Move().
            jumpPressed = input.JumpPressedThisFrame && (taser == null || !taser.IsStriking),
            weaponSlot = weapon != null ? weapon.LocalSlot : (byte)0,
        };
        return spectating ? FilterForSpectator(snapshot) : snapshot;
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
        CancelReloadIfVaulting();
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

        QueueInputForServer(sequence, snap, dt);
        AdvanceInputClock(ref localInputClock, ref localLastSprintClock, dt,
                          WantsToSprint(snap.sprintHeld, snap.move, snap.aimHeld, snap.fireHeld));

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
            for (int i = 1; i < debugFloodMultiplier; i++) QueueInputForServer(nextInputSequence++, snap, dt);
        }
#endif

        // Au plus InputSendRate envois par seconde, quel que soit le nombre d'images : le temps
        // restant est reporté, pour garder la cadence moyenne.
        float interval = 1f / InputSendRate;
        inputSendTimer += Time.unscaledDeltaTime;
        if (inputSendTimer >= interval)
        {
            inputSendTimer = Mathf.Min(inputSendTimer - interval, interval);
            SendPendingInputs();
        }
    }

    /// <summary>
    /// Les inputs du joueur, en NON FIABLE (2026-10-13) : un paquet perdu ne retient plus rien, le
    /// suivant le couvre (voir « Les inputs en route vers le serveur »). Le serveur ne garde que ce
    /// qu'il n'a pas encore reçu, dans l'ordre des numéros.
    /// </summary>
    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void SubmitInputsServerRpc(InputPacket packet)
    {
        // Des numéros qui déborderaient ne viennent que d'un client modifié.
        if (packet.count <= 0 || packet.firstSequence < 0 || packet.firstSequence > int.MaxValue - MaxInputsPerPacket) return;

        bool anyNew = false;
        for (int i = 0; i < packet.count; i++) anyNew |= serverReorder.Store(packet.firstSequence + i, packet.inputs[i]);
        NetStats.InputPacketsReceived++;
        if (!anyNew) NetStats.UselessPackets++;
#if UNITY_EDITOR
        if (!anyNew) diagServerPacketsUseless++;
#endif
        ServerDrainReceivedInputs();
    }

    /// <summary>Serveur : passe à la simulation les inputs reçus dans l'ordre (InputReorderBuffer).
    /// Appelé à chaque paquet, et à chaque image pour qu'un input manquant finisse par être sauté.</summary>
    private void ServerDrainReceivedInputs()
    {
        double now = Time.unscaledTimeAsDouble;
        while (true)
        {
            bool taken = serverReorder.TryTake(now, InputReorderWait, out int sequence, out InputWire wire, out int lost);
            NetStats.InputsLost += lost;
#if UNITY_EDITOR
            diagServerInputsLost += lost;
#endif
            if (!taken) return;
            ServerReceiveInput(sequence, wire);
        }
    }

    /// <summary>Serveur : un input reçu pour la première fois, dans l'ordre des numéros.</summary>
    private void ServerReceiveInput(int sequence, InputWire wire)
    {
        MovementInputSnapshot snap = Unpack(wire);
        // Un input invalide ne vient que d'un client modifié : il est ignoré, comme s'il n'avait
        // jamais été envoyé (voir IsValidInput). Le lean est ramené à ses trois états à la lecture
        // (Unpack) : au-delà, le buste se couchait presque à l'horizontale, emportant la surface
        // touchable avec lui.
        if (!IsValidInput(snap.move, snap.lookX, wire.deltaTime)) return;
        // Un spectateur n'a ni visée, ni tir, ni lean, ni franchissement : le serveur l'impose, quoi que
        // le client envoie (2026-10-13). Le joueur honnête a déjà filtré de même (ReadLocalInput).
        if (IsSpectator) snap = FilterForSpectator(snap);
        snap.aimPitch = SanitizeAimPitch(snap.aimPitch);

        // Compté AVANT le plafond : même jeté, cet input a été produit avant toute demande qui
        // annoncera un input plus récent (voir ServerReferenceInput).
        ServerLastReceivedInput = Mathf.Max(ServerLastReceivedInput, sequence);
        // L'horloge de la sortie de sprint avance à la RÉCEPTION, dans l'ordre des numéros : c'est
        // ce que le joueur avait envoyé quand il a tiré ou rechargé (voir TimeSinceSprint).
        AdvanceInputClock(ref serverInputClock, ref serverLastSprintClock, wire.deltaTime,
                          WantsToSprint(snap.sprintHeld, snap.move, snap.aimHeld, snap.fireHeld));
        ServerRecordReceivedInput(sequence);

        // Garde-fou anti-flood (mémoire) : sans plafond, un client qui inonde le serveur d'inputs fait
        // grossir la file indéfiniment, et le retard de simulation de CE joueur s'allonge sans jamais
        // se résorber (chaque Update() serveur n'en consomme qu'une part, voir le budget dans
        // ApplyBufferedServerInputs). Les inputs excédentaires sont jetés : un client légitime
        // n'atteint jamais ces bornes (une seconde de jeu en attente, à n'importe quel nombre
        // d'images), et celui qui les dépasse se pénalise lui-même par un recalage plus brutal.
        float dt = Mathf.Clamp(wire.deltaTime, 0f, MaxSingleInputDeltaTime);
        if (serverInputQueue.Count >= MaxQueuedInputs || serverQueuedInputTime + dt > MaxQueuedInputTime) return;

        serverQueuedInputTime += dt;
        serverInputQueue.Enqueue(new PendingInput
        {
            sequence = sequence,
            snapshot = snap,
            deltaTime = wire.deltaTime,
        });
        NetStats.InputsReceived++;
        if (serverInputQueue.Count > NetStats.QueueMax) NetStats.QueueMax = serverInputQueue.Count;
    }

    // ------------------------------------------------------------------
    // Cas 3 — serveur : seule source de vérité pour le perso d'un client distant
    // ------------------------------------------------------------------

    // Garde-fous anti-triche sur les inputs reçus du client (voir ApplyBufferedServerInputs) :
    // rien ne garantit que le deltaTime envoyé par SubmitInputsServerRpc reflète le temps
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

    // Bornes de la file d'attente (voir ServerReceiveInput). Exprimées aussi en TEMPS de jeu depuis le
    // 2026-10-13 : 120 inputs ne faisaient que 0,24 s à 500 images/s, et un client honnête au
    // nombre d'images non plafonné perdait des inputs (donc se recalait) après un simple à-coup.
    private const int MaxQueuedInputs = 512;
    private const float MaxQueuedInputTime = 1f;
    private float serverQueuedInputTime;

    /// <summary>Intervalle minimal (s) entre deux corrections envoyées au propriétaire : un hôte à
    /// plusieurs centaines d'images par seconde ne les multiplie pas. Une correction retardée de
    /// quelques millisecondes ne se voit pas : elle ne fait que confirmer.</summary>
    private const float MinCorrectionInterval = 1f / 120f;

    /// <summary>
    /// Un input reçu d'un client est-il recevable ? Toutes ses valeurs finies et une durée positive.
    /// Fonction pure, testée (relecture du 2026-10-12). Sans ce filtre, une seule durée NaN
    /// empoisonnait pour toujours l'horloge de sortie de sprint (serverInputClock) : avec NaN,
    /// « trop tôt après un sprint » est toujours faux, et le serveur ne refusait plus jamais un tir
    /// ni un rechargement en sortant d'un sprint. Un NaN dans la direction ou la souris, lui,
    /// rendait la position du joueur NaN, publiée à tout le monde.
    /// </summary>
    public static bool IsValidInput(Vector2 move, float lookX, float deltaTime)
        => WeaponController.IsFinite(move) && WeaponController.IsFinite(lookX)
           && WeaponController.IsFinite(deltaTime) && deltaTime >= 0f;

    private float serverInputTimeBudget;

    private void ApplyBufferedServerInputs()
    {
        ServerDrainReceivedInputs();
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
            serverQueuedInputTime = serverInputQueue.Count > 0 ? Mathf.Max(0f, serverQueuedInputTime - dt) : 0f;

            ServerCheckAutoStand(next.snapshot);
            // L'arme en main est celle que le serveur sait, jamais celle que le client annonce : son
            // poids pèse sur la vitesse (2026-10-11).
            next.snapshot.weaponSlot = ServerWeaponSlotFor(next.sequence);
            bool wasVaulting = IsVaulting;
            bool wasGrounded = controller.isGrounded;
            float fallSpeed = -verticalVelocity;
            // L'horloge du serveur, jamais un instant fourni par le client : il pourrait sinon
            // sauter la fin d'une transition (se relever d'un coup) en annonçant un instant futur.
            Move(next.snapshot, dt, ServerClock);
            ServerTrackVaultSound(wasVaulting, wasGrounded, fallSpeed);
            if (WantsToSprint(next.snapshot.sprintHeld, next.snapshot.move, next.snapshot.aimHeld, next.snapshot.fireHeld))
                ServerLastSprintInput = next.sequence;
            if (IsVaulting) ServerLastVaultInput = next.sequence;
            ServerRecordSimulatedInput(next.sequence, next.snapshot.aimHeld);
            ServerAdvanceFootsteps(dt);
            ServerAdvanceLean(next.snapshot.leanState, dt);
            ServerTrackLeanSound(next.snapshot.leanState);
            lastProcessedSequence = next.sequence;
        }

        // Le reste éventuel de la queue (flood ou vrai gros rattrapage) attend le prochain
        // Update() serveur plutôt que d'être traité d'un coup — voir commentaire ci-dessus.

        // La correction confirme le dernier input simulé, au plus toutes les MinCorrectionInterval : un
        // hôte à plusieurs centaines d'images par seconde n'en envoie pas une par image. Elle porte
        // l'état ACTUEL : rien d'autre que Move() ne le change entre deux, hormis un replacement en
        // début de manche, que le joueur doit justement recevoir.
        if (lastProcessedSequence >= 0)
        {
            serverCorrectionSequence = lastProcessedSequence;
            serverCorrectionPending = true;
        }
        if (serverCorrectionPending && Time.unscaledTime - serverCorrectionSentAt >= MinCorrectionInterval)
        {
            serverCorrectionPending = false;
            serverCorrectionSentAt = Time.unscaledTime;
            ReceiveCorrectionClientRpc(serverCorrectionSequence, transform.position, verticalVelocity, transform.eulerAngles.y, currentVelocity,
                                       IsVaulting, vaultTimer, vaultStart, vaultEnd, vaultPeakY, ownerRpcParams);
        }

        // La pose et l'historique sont publiés par Update(), à chaque frame, inputs ou non.
    }

    /// <summary>
    /// La correction du serveur : l'état qui suit l'input `confirmedSequence`. En NON FIABLE depuis le
    /// 2026-10-13 : la suivante remplace celle qui se perd, et une correction qui arrive après une
    /// plus récente est périmée (elle ferait rejouer des inputs déjà confirmés), donc ignorée.
    /// </summary>
    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void ReceiveCorrectionClientRpc(int confirmedSequence, Vector3 confirmedPosition, float confirmedVerticalVelocity, float confirmedYaw, Vector3 confirmedHorizontalVelocity, bool confirmedVaulting, float confirmedVaultTimer, Vector3 confirmedVaultStart, Vector3 confirmedVaultEnd, float confirmedVaultPeakY, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;
        if (confirmedSequence <= lastCorrectionSequence)
        {
            NetStats.StaleCorrections++;
#if UNITY_EDITOR
            diagCorrectionsStale++;
#endif
            return;
        }
        lastCorrectionSequence = confirmedSequence;
        lastAckedInput = Mathf.Max(lastAckedInput, confirmedSequence);

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
        // Les inputs non confirmés sont rangés par numéro croissant : ceux que la correction confirme
        // sont en tête (une boucle plutôt qu'une lambda, qui allouait à chaque correction).
        int confirmedCount = 0;
        while (confirmedCount < unconfirmedInputs.Count && unconfirmedInputs[confirmedCount].sequence <= confirmedSequence) confirmedCount++;
        int predictionIndex = confirmedCount > 0 && unconfirmedInputs[confirmedCount - 1].sequence == confirmedSequence ? confirmedCount - 1 : -1;
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
            NetStats.Corrections++;
            if (!predictionWasCorrect) NetStats.Resyncs++;
            if (positionError > NetStats.ErrorMax) NetStats.ErrorMax = positionError;
            NetStats.RttSum += measuredRtt;
            NetStats.RttSamples++;

#if UNITY_EDITOR
            DiagRecordCorrection(!predictionWasCorrect, positionError);
#endif
        }
        // Si la séquence est introuvable (correction périmée, ou input jeté par le plafond de
        // serverInputQueue), on ne peut rien conclure : on retombe sur le recalage systématique,
        // qui reste le comportement SÛR. Mieux vaut un à-coup qu'une désynchronisation silencieuse.

        if (confirmedCount > 0) unconfirmedInputs.RemoveRange(0, confirmedCount);

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

    /// <summary>La variable de pose ne change qu'à l'apparition et aux replacements (fiable) ; elle
    /// rejoint le flux des poses non fiables (PoseClientRpc).</summary>
    private void HandleNetworkPoseChanged(DisplayPose previous, DisplayPose current) => ReceivePose(current);

    /// <summary>Écart (s) entre deux poses reçues au-delà duquel on n'interpole plus de l'une à
    /// l'autre : la seconde est montrée telle quelle. Une pose ancienne (celle d'apparition, reçue en
    /// rejoignant une partie en cours) glissait sinon vers la première pose à jour.</summary>
    public const double MaxPoseGap = 0.5;

    private void ReceivePose(DisplayPose pose)
    {
        if (IsOwner || IsServer) return;
        NetStats.PosesReceived++;
        AddRemotePose(remoteSnapshots, ref lastReceivedPoseTime, ref lastTeleportId, pose, MaxPoseGap);
    }

    /// <summary>
    /// Range une pose reçue dans l'historique d'interpolation d'un spectateur. Fonction pure, testée.
    ///
    /// Les poses voyagent en NON FIABLE depuis le 2026-10-13 : elles peuvent se perdre (l'interpolation
    /// enjambe le trou, 100 ms de réserve) ou arriver dans le désordre. Une pose plus ancienne que la
    /// plus récente reçue est donc ignorée, AVANT tout le reste : une pose d'avant un replacement qui
    /// arriverait après lui viderait sinon l'historique pour y remettre l'ancienne position. Un
    /// replacement (nouvelle manche) ou un trou de plus de `maxGap` vide l'historique : interpoler entre
    /// l'ancienne position et la nouvelle ferait traverser la carte au personnage. On garde une seconde.
    /// </summary>
    public static bool AddRemotePose(List<DisplayPose> history, ref double lastTime, ref byte lastTeleport, DisplayPose pose, double maxGap)
    {
        if (pose.time <= lastTime) return false;
        bool teleported = pose.teleportId != lastTeleport;
        bool gap = history.Count > 0 && pose.time - history[history.Count - 1].time > maxGap;
        if (teleported || gap) history.Clear();
        lastTeleport = pose.teleportId;
        lastTime = pose.time;
        history.Add(pose);

        double cutoff = pose.time - 1.0;
        int stale = 0;
        while (stale < history.Count - 1 && history[stale].time < cutoff) stale++;
        if (stale > 0) history.RemoveRange(0, stale);
        return true;
    }

    /// <summary>
    /// Les poses d'affichage, en NON FIABLE, une par tick réseau (30 par seconde, environ 1 Ko/s par
    /// joueur), même quand le joueur est immobile : un spectateur a besoin de poses régulières, sinon il
    /// interpolerait un départ sur plusieurs secondes. Elles passaient par une variable réseau, donc en
    /// fiable : un paquet perdu figeait les autres joueurs jusqu'à son renvoi (mesuré le 2026-10-13 à
    /// 2 % de pertes : 19 à 68 images « affamées » toutes les 3 s chez le client). Une pose perdue n'est
    /// plus qu'un trou que l'interpolation enjambe.
    /// </summary>
    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void PoseClientRpc(DisplayPose pose) => ReceivePose(pose);

    /// <summary>La pose d'affichage de cette image, datée sur l'horloge du serveur.</summary>
    private DisplayPose ServerCurrentPose()
    {
        DisplayFlags flags = 0;
        if (controller.enabled && controller.isGrounded) flags |= DisplayFlags.Grounded;
        if (IsVaulting) flags |= DisplayFlags.Vaulting;
        if (IsAiming) flags |= DisplayFlags.Aiming;

        return new DisplayPose
        {
            time = ServerClock,
            position = transform.position,
            yaw = transform.eulerAngles.y,
            leanOffset = LeanOffset,
            aimPitch = AimPitch,
            flags = (byte)flags,
            teleportId = serverTeleportId,
        };
    }

    /// <summary>Serveur, à chaque image : la pose part au premier passage de chaque tick réseau.</summary>
    private void ServerPublishPose()
    {
        int tick = NetworkManager.ServerTime.Tick;
        if (tick == lastPoseTick) return;
        lastPoseTick = tick;
        PoseClientRpc(ServerCurrentPose());
    }

    /// <summary>Serveur : la pose par la variable réseau, FIABLE, à l'apparition et à chaque
    /// replacement. Un joueur qui arrive la reçoit avec le robot, et aucun spectateur ne manque un
    /// replacement.</summary>
    private void ServerPublishPoseReliably() => networkPose.Value = ServerCurrentPose();

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
    /// <summary>Tangage reçu d'un client : borné à la verticale, et un NaN (client modifié) ramené à
    /// l'horizontale, pour qu'il ne se propage pas jusqu'à l'animation des autres joueurs.</summary>
    public static float SanitizeAimPitch(float pitch) => float.IsNaN(pitch) ? 0f : Mathf.Clamp(pitch, -90f, 90f);

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
                aimPitch = Mathf.Lerp(a.aimPitch, b.aimPitch, t),
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
        // on ne franchit pas un obstacle à moitié relevé. (Un spectateur ne franchit jamais : son
        // saut est filtré, SpectatorInputFilter.)
        bool spectating = IsSpectator;
        bool movementAllowed = spectating ? SpectatorMovementAllowed(simTime) : RoundManager.MovementAllowedFor(IsAlive);
        if (snap.jumpPressed && !spectating && movementAllowed && stanceProgress >= 1f
            && TryFindVaultTarget(out Vector3 vaultLanding, out float vaultTop, false))
        {
            BeginVault(vaultLanding, vaultTop);
            AdvanceVault(dt);
            return;
        }

        // Pendant la préparation et le décompte, le déplacement est gelé pour que les deux joueurs
        // repartent exactement en même temps ; un robot mort ne bouge plus. On ne coupe QUE le
        // déplacement : regarder autour de soi et se pré-positionner visuellement reste permis. En
        // fin de manche, le survivant garde la main (RoundManager.PlayAllowed).
        //
        // Appliqué ici, dans la fonction déterministe, donc identiquement côté client et serveur :
        // un client modifié qui ignorerait le gel se ferait recaler, puisque le serveur applique le
        // même test sur sa propre simulation.
        //
        // Un spectateur, lui, n'est gelé par aucune phase : seul un étourdissement l'arrête, lu à
        // l'instant de simulation de l'input (comme une transition de posture) sur l'état publié par
        // le serveur. Le joueur l'apprend avec un peu de retard : le recalage rattrape l'écart.
        Vector2 moveInput = movementAllowed ? snap.move : Vector2.zero;

        // Allongé, on ne se déplace pas de côté (2026-10-10) : aucune animation ne le montre, et le
        // corps glissait en pose d'attente. Aucune glissade n'est tolérée. À rouvrir avec un clip
        // de reptation latérale.
        if (stance == Stance.Prone) moveInput.x = 0f;

        Vector3 inputDir = new Vector3(moveInput.x, 0f, moveInput.y);
        inputDir = Vector3.ClampMagnitude(inputDir, 1f);
        Vector3 worldDir = transform.TransformDirection(inputDir);

        IsAiming = snap.aimHeld; // TODO (avec le vrai système d'armes) : brancher FOV/sway ici.
        AimPitch = snap.aimPitch; // affichage seulement : rien dans Move() ne le relit

        StanceProfile profile = GetStanceProfile(stance);

        // Capsule de collision appliquée ICI, dans la fonction déterministe, et pas seulement
        // dans Update() : un rejeu de réconciliation tourne en dehors d'Update (depuis la
        // ClientRpc de correction), et doit donc reconstituer lui-même la bonne capsule avant
        // d'appeler controller.Move(). Voir ApplySimulationCapsule.
        ApplySimulationCapsule(stance);

        // Vers l'avant seulement, diagonales avant comprises : ni de côté, ni en arrière (2026-10-10).
        bool wantsSprint = WantsToSprint(snap.sprintHeld, moveInput, snap.aimHeld, snap.fireHeld)
            && stance == Stance.Standing && !snap.firePressedThisFrame;
        bool wantsSneak = snap.sneakHeld && !wantsSprint;
        float baseSpeed = wantsSprint ? runSpeed : (wantsSneak ? sneakSpeed : walkSpeed);
        float stanceMultiplier = Mathf.Lerp(GetStanceProfile(stanceState.previous).moveSpeedMultiplier,
                                            profile.moveSpeedMultiplier, stanceProgress);
        float targetSpeed = baseSpeed * stanceMultiplier * DirectionMultiplier(moveInput) * WeaponSpeedMultiplier(snap.weaponSlot);

        if (snap.aimHeld)
        {
            targetSpeed *= adsSpeedMultiplier;
        }

        Vector3 targetVelocity = worldDir * targetSpeed;
        // On met du temps à casser un sprint (2026-10-11) : au-dessus de l'allure de marche, l'élan
        // de la course ne retombe qu'à sprintDeceleration. La vitesse se décompose selon la
        // direction où le joueur VEUT aller (ou celle où il va, s'il s'arrête) : cette composante
        // se règle comme d'habitude, et ce qui reste de la course s'éteint lentement, sans jamais
        // être réorienté. Partir de côté en sortant d'un sprint garde donc l'élan vers l'avant,
        // sans jamais aller de côté plus vite qu'un pas de côté normal. (Deux premières versions
        // freinaient le long du mouvement lui-même : l'élan tournait avec lui et finissait de côté,
        // à 7,6 m/s, soit un peek deux fois plus rapide en sortant d'un sprint.)
        Vector3 horizontal = new Vector3(currentVelocity.x, 0f, currentVelocity.z);
        float speedNow = horizontal.magnitude;
        if (speedNow > walkSpeed && targetSpeed < speedNow)
        {
            Vector3 axis = targetVelocity.sqrMagnitude > 1e-6f ? targetVelocity.normalized : horizontal / speedNow;
            float along = Vector3.Dot(horizontal, axis);
            float wanted = Vector3.Dot(targetVelocity, axis);
            along = along > wanted
                ? Mathf.Max(wanted, along - sprintDeceleration * dt)
                : Mathf.Min(wanted, along + acceleration * dt);
            Vector3 carried = horizontal - axis * Vector3.Dot(horizontal, axis);
            float carriedSpeed = carried.magnitude;
            if (carriedSpeed > 1e-6f) carried *= Mathf.Max(0f, carriedSpeed - sprintDeceleration * dt) / carriedSpeed;
            currentVelocity = axis * along + carried;
        }
        else
        {
            float rate = targetVelocity.sqrMagnitude > currentVelocity.sqrMagnitude ? acceleration : deceleration;
            currentVelocity = Vector3.MoveTowards(currentVelocity, targetVelocity, rate * dt);
        }

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

        // Un spectateur marche, court et s'accroupit, mais ne s'allonge pas (2026-10-13, demande de
        // l'utilisateur) : le serveur refuse de même (RequestStanceChangeServerRpc).
        if (input.PronePressedThisFrame && !IsSpectator)
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
        // Une posture hors de l'énumération ne vient que d'un client modifié (2026-10-12).
        if (desired < Stance.Standing || desired > Stance.Prone) return;
        if (desired == Stance.Prone && IsSpectator) return;
        ServerRequestStance(desired);
    }

    /// <summary>Relevé automatique en sprint, évalué côté serveur à partir de l'input de
    /// mouvement de CE tick (déjà soumis pour le déplacement) — pas de RPC dédiée nécessaire.</summary>
    private void ServerCheckAutoStand(MovementInputSnapshot snap)
    {
        if (networkStance.Value.current == Stance.Standing) return;

        if (snap.sprintHeld && IsSprintDirection(snap.move))
        {
            ServerRequestStance(Stance.Standing);
        }
    }

    // ------------------------------------------------------------------
    // Le sprint : vers l'avant seulement, et jamais en rechargeant ni en frappant (2026-10-10)
    // ------------------------------------------------------------------

    /// <summary>Écart maximal (degrés) entre la direction demandée et l'avant pour sprinter. Les
    /// diagonales avant du clavier (45°) passent ; le côté et l'arrière, non, comme dans Call of
    /// Duty ou Rainbow Six. Les 5° de marge couvrent un stick de manette tenu à peu près en
    /// diagonale.</summary>
    public const float SprintMaxAngle = 50f;

    /// <summary>La direction demandée permet-elle de sprinter ? Fonction pure, testée.</summary>
    public static bool IsSprintDirection(Vector2 move)
    {
        return move.sqrMagnitude > 0.01f && Vector2.Angle(Vector2.up, move) <= SprintMaxAngle;
    }

    /// <summary>Le joueur veut-il sprinter : touche tenue, direction permise, ni visée ni tir ?
    /// La posture n'en fait pas partie : sprinter accroupi ou allongé relève d'abord le joueur.
    /// Fonction pure, testée.</summary>
    public static bool WantsToSprint(bool sprintHeld, Vector2 move, bool aimHeld, bool fireHeld)
    {
        return sprintHeld && IsSprintDirection(move) && !aimHeld && !fireHeld;
    }

    /// <summary>
    /// Le sprint face au rechargement et au taser, chez le joueur. Fonction pure, testée.
    ///
    /// Comme dans Call of Duty ou Rainbow Six, c'est la DERNIÈRE action qui l'emporte :
    /// - recharger ou frapper en sprintant arrête le sprint : on ne recharge jamais en courant ;
    /// - se mettre à courir pendant un rechargement l'annule ;
    /// - le coup de taser va à son terme : pas de sprint pendant le geste.
    ///
    /// « Se mettre à courir », c'est l'intention de sprinter qui NAÎT (touche enfoncée, ou poussée
    /// vers l'avant touche tenue). Une touche tenue depuis avant le rechargement ne l'annule pas :
    /// sinon, appuyer sur recharger en sprintant n'aurait aucun effet.
    /// </summary>
    /// <param name="intent">Le joueur veut-il sprinter (WantsToSprint) ?</param>
    /// <param name="previousIntent">L'intention de l'image précédente, mise à jour ici.</param>
    /// <param name="cancelReload">Vrai quand le rechargement en cours doit être annulé.</param>
    /// <returns>Le sprint est-il permis à cette image ?</returns>
    public static bool GateSprint(bool intent, ref bool previousIntent, bool reloading, bool striking, out bool cancelReload)
    {
        bool startsRunning = intent && !previousIntent;
        previousIntent = intent;
        cancelReload = false;
        if (striking) return false;
        if (reloading)
        {
            if (!startsRunning) return false;
            cancelReload = true;
        }
        return true;
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

    /// <summary>Vitesse de chute (m/s) à partir de laquelle une réception s'entend : une marche
    /// descendue n'est pas une chute (2026-10-13).</summary>
    public const float LandingSoundFallSpeed = 3f;

    // Un franchissement fini : sa réception attend le premier contact avec le sol (serveur seul).
    private bool serverVaultLandingPending;

    /// <summary>Franchir un obstacle et retomber s'entendent (2026-10-13, catégorie B, comme les pas) :
    /// le serveur le voit dans sa propre simulation, rien n'est demandé au client.</summary>
    private void ServerTrackVaultSound(bool wasVaulting, bool wasGrounded, float fallSpeed)
    {
        PlayerSoundEvent? sound = GetVaultSound(wasVaulting, IsVaulting, wasGrounded, controller.isGrounded, fallSpeed,
                                                ref serverVaultLandingPending);
        if (sound.HasValue) BroadcastPlayerSoundClientRpc(sound.Value, ComputeNoiseLevel());
    }

    /// <summary>
    /// Le son d'un franchissement qui commence, ou d'une réception : au premier contact avec le sol après
    /// un franchissement (une seule fois), ou après une vraie chute. À la fin de l'arc, le contrôleur,
    /// coupé pendant le franchissement, n'a pas encore remis à jour son « au sol » : la réception attend
    /// le contact (`landingPending`). Au banc (2026-10-13), elle s'entendait deux fois, à la fin de l'arc
    /// puis au contact 0,14 s plus tard. Fonction pure, testée.
    /// </summary>
    public static PlayerSoundEvent? GetVaultSound(bool wasVaulting, bool isVaulting, bool wasGrounded, bool isGrounded,
                                                  float fallSpeed, ref bool landingPending)
    {
        if (isVaulting)
        {
            if (wasVaulting) return null;
            landingPending = false;
            return PlayerSoundEvent.Vault;
        }
        if (wasVaulting)
        {
            landingPending = true;
            return null;
        }
        if (!isGrounded) return null;
        if (landingPending)
        {
            landingPending = false;
            return PlayerSoundEvent.Land;
        }
        return !wasGrounded && fallSpeed >= LandingSoundFallSpeed ? PlayerSoundEvent.Land : (PlayerSoundEvent?)null;
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
        // Un spectateur ne se penche pas (2026-10-13) : le lean est un geste du duel.
        if (IsSprinting || IsSpectator)
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

        // La capsule ENTIÈRE doit arriver derrière l'obstacle. À 0,25 m derrière une barricade de
        // 0,50 m de profondeur, une capsule de 0,35 m de rayon mordait encore 10 cm sur son arête, et
        // le CharacterController l'en sortait au pas suivant en « montant la marche » (stepOffset,
        // 0,30 m) : un bond de 30 cm, une chute, et une seconde réception qu'on entendait (mesuré au
        // banc le 2026-10-13). On cherche la face arrière, par un rayon tiré de l'autre côté vers
        // l'obstacle juste sous son sommet, et l'arrivée est repoussée d'un rayon de capsule (plus la
        // peau) au-delà. Géométrie statique : le même calcul chez le client qui prédit et le serveur.
        float backClearance = controller.radius + controller.skinWidth;
        float backProbeReach = vaultLandingProbeDistance + backClearance + 0.5f;
        Vector3 backProbeOrigin = topHit.point + forward * backProbeReach + Vector3.down * 0.05f;
        if (Physics.Raycast(backProbeOrigin, -forward, out RaycastHit backHit, backProbeReach, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            float needed = Vector3.Dot(backHit.point - topHit.point, forward) + backClearance;
            if (needed > vaultLandingProbeDistance) horizontalLanding = topHit.point + forward * needed;
        }

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

    /// <summary>Un input tel qu'il voyage (Pack), pour vérifier qu'il arrive tel qu'il est parti.</summary>
    public static InputWire TestPack(Vector2 move, float lookX, float aimPitch, float dt, bool sprint, bool sneak, bool aim,
                                     bool fireHeld, bool firePressed, bool jump, int lean)
        => Pack(new MovementInputSnapshot
        {
            move = move, lookX = lookX, aimPitch = aimPitch, sprintHeld = sprint, sneakHeld = sneak, aimHeld = aim,
            fireHeld = fireHeld, firePressedThisFrame = firePressed, jumpPressed = jump, leanState = lean,
        }, dt);

    /// <summary>Ce que le serveur relit d'un input reçu (Unpack) : les booléens dans l'ordre sprint,
    /// sneak, visée, gâchette tenue, gâchette pressée, saut.</summary>
    public static bool[] TestUnpack(InputWire wire, out Vector2 move, out float lookX, out float aimPitch, out int lean)
    {
        MovementInputSnapshot s = Unpack(wire);
        move = s.move;
        lookX = s.lookX;
        aimPitch = s.aimPitch;
        lean = s.leanState;
        return new[] { s.sprintHeld, s.sneakHeld, s.aimHeld, s.fireHeld, s.firePressedThisFrame, s.jumpPressed };
    }

    /// <summary>Impose une posture, transition comprise, sans passer par le serveur.</summary>
    public void TestSetStance(StanceState state)
    {
        networkStance.Value = state;
    }

    /// <summary>Appelle Move() avec un input construit de toutes pièces, sans passer par le
    /// clavier ni par le réseau. Sans session, aucune transition de posture n'est en cours :
    /// l'instant de simulation n'a donc aucun effet et vaut 0 par défaut. `weaponSlot` : l'arme en
    /// main, dont le poids pèse sur la vitesse.</summary>
    public void TestMove(Vector2 move, float lookX, bool sprintHeld, bool sneakHeld, bool aimHeld, bool jumpPressed, int leanState, float dt, double simTime = 0, byte weaponSlot = 0)
    {
        Move(new MovementInputSnapshot
        {
            weaponSlot = weaponSlot,
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
