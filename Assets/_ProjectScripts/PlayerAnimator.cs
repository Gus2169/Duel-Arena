using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pilote l'Animator du personnage à partir de l'état affiché du joueur.
///
/// CATÉGORIE C — purement cosmétique. Ce script LIT, il n'écrit jamais rien que la simulation
/// relise. C'est la règle centrale du projet côté animation : l'animation AFFICHE, elle ne décide
/// pas. Un Animator n'est pas déterministe entre machines (blending, vitesse de lecture,
/// LateUpdate), donc tout ce qui en dépendrait ferait diverger les instances.
///
/// Contrairement aux autres scripts cosmétiques, il tourne sur TOUTES les instances et non
/// seulement chez le propriétaire : il faut bien voir son adversaire bouger.
///
/// C'est précisément ce qui dicte d'où vient la vitesse. `currentVelocity` n'existe que là où la
/// simulation tourne (propriétaire et serveur) ; chez un spectateur elle vaudrait zéro en
/// permanence et l'adversaire glisserait sans bouger les jambes. Le DÉPLACEMENT DU TRANSFORM,
/// lui, est juste dans les quatre cas réseau — y compris l'interpolation du spectateur, qui est
/// justement ce qu'on veut montrer.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerLocomotion))]
public class PlayerAnimator : MonoBehaviour
{
    [Tooltip("Animator du personnage (enfant Model). Laissé vide, il est cherché dans les enfants.")]
    [SerializeField] private Animator animator;

    // Les vitesses de référence (celles où l'arbre de mélange vaut 1) ne sont plus recopiées ici :
    // elles sont LUES dans PlayerLocomotion.NominalSpeed. La copie s'était désaccordée en silence
    // (4,4 m/s ici contre 5 m/s réglés dans le prefab), et l'animation tournait 14 % trop vite.

    [Tooltip("Lissage des paramètres de mélange. Purement visuel : évite qu'un à-coup d'une frame ne fasse claquer l'animation.")]
    [SerializeField] private float blendSmoothing = 12f;

    [Tooltip("Durée de la partie UTILE du clip de vault, en secondes — pas sa durée totale. Le contrôleur entre dans l'état à 20 % du clip pour sauter la préparation, et la réception est coupée par la sortie d'état. Sert à lire le geste à la vitesse qui le fait tenir dans la durée du vault.")]
    [SerializeField] private float vaultClipLength = 0.70f;

    [Header("Lean et posture allongée (affichage)")]
    [Tooltip("Recul du modèle en position allongée, en mètres (Z local). Sa tête tombe ainsi à l'intérieur du CharacterController, là où la caméra et la tête touchable la placent (voir BodyLayout.DefaultProne). Doit rester accordé à ces deux valeurs. Le modèle y glisse au rythme de la transition de posture.")]
    [SerializeField] private float proneModelOffset = -0.30f;

    [Header("Transitions de posture (affichage)")]
    [Tooltip("Durée de la partie UTILE des clips genou ↔ allongé, en secondes — pas leur durée totale (1,97 s). Mesurée le 2026-10-06 : le corps descend de 0,25 s à 1,80 s du clip, et remonte de 0,35 s à 1,90 s. Sert à lire le geste à la vitesse qui le fait tenir dans la durée de la transition.")]
    [SerializeField] private float proneTransitionClipLength = 1.55f;

    [Header("Rechargement et taser (affichage)")]
    [Tooltip("Durée de la partie UTILE du clip de rechargement (Y Bot@Reloading, 3,3 s au total). Mesurée le 2026-10-08 : la main gauche quitte l'arme vers 0,4 s et y revient vers 2,5 s. Le contrôleur entre dans le clip à 0,4 s ; il est lu à la vitesse qui fait tenir ce geste dans la durée réelle du rechargement (WeaponData.reloadDuration).")]
    [SerializeField] private float reloadClipUsefulLength = 2.1f;

    [Tooltip("Durée de la partie UTILE du clip de taser (Y Bot@Taser Punch, en miroir, 1,7 s au total, depuis le 2026-10-11). Mesurée : le poing part à 0,10 s, il est tendu à 0,32 s et revenu à 0,82 s ; le contact tombe ainsi à 30 % du geste, les 0,18 s de TaserController.contactDelay sur 0,6 s. Le contrôleur entre dans le clip à 0,10 s ; il est lu à la vitesse qui fait tenir ce geste dans TaserController.StrikeDuration.")]
    [SerializeField] private float taserClipUsefulLength = 0.72f;

    [Tooltip("Temps (s) du passage d'une tenue à l'autre (fusil ↔ pistolet) en changeant d'arme.")]
    [SerializeField] private float holdBlendTime = 0.2f;

    [Header("Gestes d'un spectateur (affichage)")]
    [Tooltip("Temps (s) entre l'entrée dans le clip du coup de poing et le poing tendu. Le spectateur frappe avec le direct du gauche du taser (Taser Punch en miroir, entré à 0,10 s, poing tendu à 0,32 s) : le direct de boxe de Mixamo, en garde, hanches tournées, partait en l'air et sur le côté une fois posé sur un haut du corps aux hanches de face (2026-10-13). Le geste est lu à la vitesse qui fait tomber cet instant sur le contact (SpectatorController.PunchContactDelay).")]
    [SerializeField] private float punchClipContactTime = 0.22f;
    [Tooltip("Temps (s) de la montée et de la descente du poids de la couche des gestes.")]
    [SerializeField] private float gestureBlendTime = 0.08f;

    [Header("Cadence de lecture")]
    [Tooltip("Plancher de la cadence des animations d'attente et du haut du corps. Ne descend pas à 0 : l'attente doit continuer de respirer à l'arrêt.")]
    [SerializeField] private float minPlaybackSpeed = 0.75f;

    [Tooltip("Cadence des animations d'attente et du haut du corps, par posture — et des jambes allongé tant que la vitesse de la reptation n'est pas encore mesurée.")]
    [SerializeField] private float standingPlaybackScale = 0.85f;
    [SerializeField] private float crouchingPlaybackScale = 1f;
    [SerializeField] private float pronePlaybackScale = 1.4f;

    [Header("Cadence des jambes (pieds calés sur le sol)")]
    [Tooltip("Cadence maximale des jambes. Les clips du Y Bot avancent moins vite que le jeu (course 4,64 m/s pour une marche à 5, sprint 6,96 pour 10, marche accroupie 1,97 pour 3) : les caler demande jusqu'à ×1,52. Au-delà de ce plafond, les pieds glissent ; aucune glissade n'est tolérée (2026-10-10), donc il couvre toutes les allures du jeu. Le baisser, c'est accepter des glissades ou baisser les vitesses du jeu.")]
    [SerializeField] private float maxLegCadence = 1.6f;

    [Tooltip("Cadence minimale des jambes. En dessous, les pieds avanceraient plus vite que le corps. Une cadence basse se voit comme un ralenti : signe qu'il manque un clip de marche lente pour cette direction.")]
    [SerializeField] private float minLegCadence = 0.2f;

    [Header("Direction du buste (affichage)")]
    [Tooltip("Temps de lissage de l'alignement du buste sur le regard (s). Court, le fusil reste rivé au regard ; long, il flotte au rythme des pas.")]
    [SerializeField] private float torsoAlignSmoothing = 0.12f;

    [Tooltip("Vitesse du passage arme basse ↔ visée de cet alignement (par seconde).")]
    [SerializeField] private float torsoAimBlendSpeed = 6f;

    [Tooltip("Torsion maximale du buste pour suivre le regard, à gauche comme à droite (degrés). Viser en se déplaçant à gauche en demande plus de 100 : les jambes y tournent les hanches de 59° à gauche, et le tireur épaule le buste tourné de 27° à droite.")]
    [SerializeField] private float maxTorsoYaw = 120f;

    [Tooltip("Inclinaison maximale du buste pour suivre le regard vers le haut ou le bas (degrés), debout et accroupi.")]
    [SerializeField] private float maxTorsoPitch = 70f;

    [Tooltip("Même chose allongé : le buste est au sol, il ne peut guère se relever ni plonger.")]
    [SerializeField] private float maxProneTorsoPitch = 20f;

    private static readonly int MoveXId = Animator.StringToHash("MoveX");
    private static readonly int MoveYId = Animator.StringToHash("MoveY");
    private static readonly int StanceId = Animator.StringToHash("Stance");
    private static readonly int SpeedMultId = Animator.StringToHash("SpeedMult");
    private static readonly int AirborneId = Animator.StringToHash("Airborne");
    private static readonly int VaultingId = Animator.StringToHash("Vaulting");
    private static readonly int VaultSpeedId = Animator.StringToHash("VaultSpeed");
    private static readonly int StanceFId = Animator.StringToHash("StanceF");
    private static readonly int AimingId = Animator.StringToHash("Aiming");
    private static readonly int FireId = Animator.StringToHash("Fire");
    private static readonly int PostureSpeedId = Animator.StringToHash("PostureSpeed");
    private static readonly int ReloadingId = Animator.StringToHash("Reloading");
    private static readonly int ReloadSpeedId = Animator.StringToHash("ReloadSpeed");
    private static readonly int TaserId = Animator.StringToHash("Taser");
    private static readonly int TaserSpeedId = Animator.StringToHash("TaserSpeed");
    private static readonly int PistoletId = Animator.StringToHash("Pistolet");
    private static readonly int LocoSpeedId = Animator.StringToHash("LocoSpeed");
    private static readonly int MarcheId = Animator.StringToHash("Marche");
    private static readonly int AllureId = Animator.StringToHash("Allure");
    private static readonly int TirStateId = Animator.StringToHash("Tir");
    private static readonly int DeboutStateId = Animator.StringToHash("Debout");
    private static readonly int AccroupiStateId = Animator.StringToHash("Accroupi");
    private static readonly int ProneStateId = Animator.StringToHash("Prone");
    // Le spectateur, à mains nues (2026-10-13).
    private static readonly int MainsNuesId = Animator.StringToHash("MainsNues");
    private static readonly int PoingId = Animator.StringToHash("Poing");
    private static readonly int LancerId = Animator.StringToHash("Lancer");
    private static readonly int RamasserId = Animator.StringToHash("Ramasser");
    private static readonly int ElanId = Animator.StringToHash("Elan");
    private static readonly int ElanLancerId = Animator.StringToHash("ElanLancer");
    private static readonly int GesteSpeedId = Animator.StringToHash("GesteSpeed");
    private static readonly int ReposStateId = Animator.StringToHash("Repos");
    private static readonly int RamasserStateId = Animator.StringToHash("Ramasser");

    /// <summary>Couche du haut du corps (torse, bras, tête au-dessus des hanches).</summary>
    private const int UpperBodyLayer = 1;

    /// <summary>Couche du taser : buste et bras GAUCHE seulement (MasqueTaser). L'estocade est jouée
    /// en miroir, pour que la main gauche frappe pendant que la droite garde le fusil. Son poids
    /// est piloté ici, à 0 hors du geste.</summary>
    private const int TaserLayer = 2;

    /// <summary>Couche des gestes d'un spectateur (MasqueHautDuCorps) : coup de poing, élan et lancer.
    /// Son poids est piloté ici, à 0 hors d'un geste.</summary>
    public const int SpectatorGesturesLayer = 3;

    /// <summary>Couche de tout le corps d'un spectateur : il titube, étourdi, et se baisse jusqu'au sol pour
    /// ramasser (la flexion vient des hanches et des genoux, qu'un masque du haut du corps exclut).</summary>
    private const int FullBodyLayer = 4;

    /// <summary>Allure (part de l'allure nominale) au-delà de laquelle on ne se baisse pas pour ramasser :
    /// des jambes figées dans le geste glisseraient sous un corps qui avance.</summary>
    private const float MaxPickUpGait = 0.25f;

    private PlayerLocomotion locomotion;
    private WeaponController weapon;
    private TaserController taser;
    private SpectatorController spectator; // les gestes d'un spectateur (2026-10-13)
    private FirstPersonView firstPersonView;
    private float lastStrikeTime = float.NegativeInfinity;
    // Durée du geste joué sur la couche du taser.
    private float strikeGestureDuration = 0.6f;
    private bool shownSpectator;
    private float gestureWeight;
    private float pickUpWeight;
    private Vector3 previousPosition;
    private Vector2 smoothedMove;
    private AnimatorRootMotionProbe rootMotion;
    private GaitState gait;
    private FootPlanter planter; // les pieds calés au sol pendant l'appui (2026-10-10)
    private LegsTurnState legsTurn; // le bas du corps qui garde son cap à l'arrêt (2026-10-10)

    // Direction du buste : la mire du fusil, les os qui tournent (du bas au haut de la colonne), et
    // la correction lissée.
    private Transform sight;
    private Transform rightHand;
    private WeaponData shownWeapon; // l'arme dont le modèle est en main
    private PlayerWeaponLooks weaponLooks; // les modèles et finitions choisis à l'armurerie (2026-10-15)
    private PlayerWeaponAttachments weaponAttachments; // et les accessoires (2026-10-16)
    // Tenue du haut du corps : 0 fusil (deux mains, crosse à l'épaule), 1 pistolet (WeaponModel.PistolHold).
    private float pistolHold, pistolBlend;
    private bool pistolHoldShown;
    private Transform[] spineChain;
    // Durée du rechargement en cours, relevée à son début : le geste y est étalé, même quand le
    // pompe glisse ses cartouches une à une.
    private float reloadAnimationDuration = 1f;
    private bool wasReloading;
    private float torsoAimWeight;
    private float torsoYaw;
    private float torsoPitch;

    private void Awake()
    {
        locomotion = GetComponent<PlayerLocomotion>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        weapon = GetComponentInChildren<WeaponController>();
        taser = GetComponentInChildren<TaserController>();
        spectator = GetComponent<SpectatorController>();
        firstPersonView = GetComponent<FirstPersonView>();
        weaponLooks = GetComponent<PlayerWeaponLooks>();
        weaponAttachments = GetComponent<PlayerWeaponAttachments>();
        previousPosition = transform.position;
        if (taser != null) strikeGestureDuration = taser.StrikeDuration;

        if (animator != null)
        {
            // Avant que FirstPersonView ne copie le modèle : la copie hérite du composant, ce qui
            // est sans effet (ses bras n'ont pas de jambes à caler).
            rootMotion = animator.GetComponent<AnimatorRootMotionProbe>();
            if (rootMotion == null) rootMotion = animator.gameObject.AddComponent<AnimatorRootMotionProbe>();

            // Les armes de la panoplie, accrochées à la main, avant que la vue subjective ne copie le
            // modèle (2026-10-11) : le modèle par défaut de chaque classe, sans accessoire, jusqu'à ce
            // que le serveur publie ce que le joueur a choisi (HandleWeaponLooks, HandleWeaponAttachments).
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (weapon != null) HeldWeapons.Build(rightHand, weapon.Loadout, WeaponLooks.Default);
            ShowHeldWeapon();
            spineChain = SpineChain(animator);
            planter = new FootPlanter(animator);
        }
    }

    private void OnEnable()
    {
        if (weaponLooks != null)
        {
            weaponLooks.Changed += HandleWeaponLooks;
            if (weaponLooks.Published) HandleWeaponLooks(weaponLooks.Looks);
        }
        if (weaponAttachments != null)
        {
            weaponAttachments.Changed += HandleWeaponAttachments;
            if (weaponAttachments.Published) HandleWeaponAttachments(weaponAttachments.Attachments);
        }
        if (weapon != null) weapon.OnShotFired += DeclencherAnimationDeTir;
        if (taser != null) taser.OnStrike += DeclencherCoupDeTaser;
        if (spectator != null)
        {
            spectator.OnPunch += DeclencherCoupDePoing;
            spectator.OnThrow += DeclencherLancer;
            spectator.OnPickUp += DeclencherRamasser;
        }
    }

    private void OnDisable()
    {
        if (weaponLooks != null) weaponLooks.Changed -= HandleWeaponLooks;
        if (weaponAttachments != null) weaponAttachments.Changed -= HandleWeaponAttachments;
        if (weapon != null) weapon.OnShotFired -= DeclencherAnimationDeTir;
        if (taser != null) taser.OnStrike -= DeclencherCoupDeTaser;
        if (spectator != null)
        {
            spectator.OnPunch -= DeclencherCoupDePoing;
            spectator.OnThrow -= DeclencherLancer;
            spectator.OnPickUp -= DeclencherRamasser;
        }
    }

    // Les gestes d'un spectateur (2026-10-13, sur la couche des gestes) : le direct du gauche (celui du
    // taser), un lancer du bras droit et se pencher pour ramasser (clips Mixamo choisis ce jour-là). Par
    // événement, comme le tir : chez le joueur à l'appui, chez les autres à la diffusion du serveur.

    /// <summary>Le coup de poing : le poing gauche est tendu à l'instant du contact.</summary>
    private void DeclencherCoupDePoing(float duration)
    {
        if (animator == null) return;
        animator.SetFloat(GesteSpeedId, PunchPlaybackSpeed);
        animator.SetTrigger(PoingId);
    }

    /// <summary>Vitesse de lecture du coup de poing : le poing tendu tombe sur le contact.</summary>
    public float PunchPlaybackSpeed => spectator != null
        ? punchClipContactTime / Mathf.Max(0.05f, spectator.PunchContactDelay)
        : 1f;

    /// <summary>Le lancer : chez le joueur, il suit l'élan (ElanLancer) ; chez les autres, qui ne voient
    /// pas l'élan, il part du bras armé.</summary>
    private void DeclencherLancer()
    {
        if (animator != null) animator.SetTrigger(LancerId);
    }

    /// <summary>Ramasser : se baisser jusqu'au sol, tout le corps, seulement à l'arrêt ou presque.</summary>
    private void DeclencherRamasser()
    {
        if (animator != null && smoothedMove.magnitude < MaxPickUpGait) animator.SetTrigger(RamasserId);
    }

    /// <summary>L'Animator des bras de la vue subjective, chez le propriétaire seulement.</summary>
    private Animator ViewAnimator => firstPersonView != null ? firstPersonView.ViewAnimator : null;

    /// <summary>
    /// Les bras de la vue subjective ne reprennent du corps que les GESTES de l'arme : tir et taser
    /// (par leurs événements), rechargement, et leurs vitesses. Le reste est figé : debout, immobile,
    /// en visée (2026-10-10). Rapporté par l'utilisateur : en visant, un pas chassé gauche-droite
    /// faisait balancer l'arme au point de ne plus pouvoir viser, et se relever faisait passer la
    /// caméra à travers le buste. Les bras rejouaient alors tout le corps : les jambes tournent les
    /// hanches de près de 60° d'un pas chassé à l'autre, le buste suivait, et l'alignement de l'arme,
    /// lissé, courait derrière ; les clips de changement de posture balayaient le buste devant
    /// l'œil. Comme dans les FPS, les bras ne dépendent ni des jambes ni de la posture : la caméra
    /// porte déjà le déplacement, la hauteur et le lean.
    /// </summary>
    private void MirrorToView()
    {
        Animator view = ViewAnimator;
        if (view == null || !view.isActiveAndEnabled) return;
        // Debout, le taser a toujours son geste, même quand le corps est allongé (où il n'en a pas).
        MirrorWeaponGestures(animator, view, TaserLayerWeight(PlayerLocomotion.Stance.Standing));
    }

    /// <summary>Recopie vers les bras de la vue subjective ce qu'ils reprennent du corps (voir
    /// MirrorToView). Sans état, testée.</summary>
    public static void MirrorWeaponGestures(Animator body, Animator view, float taserWeight)
    {
        view.SetBool(ReloadingId, body.GetBool(ReloadingId));
        view.SetFloat(ReloadSpeedId, body.GetFloat(ReloadSpeedId));
        view.SetFloat(TaserSpeedId, body.GetFloat(TaserSpeedId));
        // La tenue (fusil ou pistolet) : les bras tiennent l'arme comme le corps.
        view.SetFloat(PistoletId, body.GetFloat(PistoletId));
        // Les poses d'attente ne défilent pas : leur respiration, lente, faisait dériver la mire de
        // ±45 pixels en visée (mesuré le 2026-10-10), le placement de l'arme la rattrapant en
        // retard. Les gestes (tir, rechargement, taser) ont leur propre vitesse et jouent.
        view.SetFloat(SpeedMultId, 0f);

        // Toujours la même tenue : la pose de visée, debout, jambes à l'arrêt. Au repos, le robot
        // tient son fusil en travers des hanches, hors de l'écran : la différence entre la hanche
        // et la visée est un déplacement de l'arme, géré par FirstPersonView.
        view.SetBool(AimingId, true);
        view.SetInteger(StanceId, 0);
        view.SetFloat(StanceFId, 0f);
        SetLocomotion(view, Vector2.zero);
        view.SetFloat(MarcheId, 0f);
        view.SetFloat(LocoSpeedId, 0f);
        view.SetBool(AirborneId, false);
        view.SetBool(VaultingId, false);
        if (view.layerCount > UpperBodyLayer) view.SetLayerWeight(UpperBodyLayer, 1f);
        if (view.layerCount > TaserLayer) view.SetLayerWeight(TaserLayer, taserWeight);
    }

    /// <summary>Un coup de taser part : même principe que le tir, par événement.</summary>
    private void DeclencherCoupDeTaser()
    {
        lastStrikeTime = Time.time;
        strikeGestureDuration = taser != null ? taser.StrikeDuration : 0.6f;
        if (animator != null) animator.SetTrigger(TaserId);
        Animator view = ViewAnimator;
        if (view != null) view.SetTrigger(TaserId);
    }

    /// <summary>
    /// Poids de la couche du taser : monte vite au début du geste, retombe à la fin, nul le reste
    /// du temps. Allongé, il reste nul : l'estocade est authorée debout, et son bras partirait dans
    /// le sol (le coup reste valable, seul le geste manque).
    /// </summary>
    private float TaserLayerWeight(PlayerLocomotion.Stance stance)
    {
        if (taser == null || stance == PlayerLocomotion.Stance.Prone) return 0f;
        float t = Time.time - lastStrikeTime;
        float duration = strikeGestureDuration;
        if (t < 0f || t > duration) return 0f;
        float rise = Mathf.Clamp01(t / 0.05f);
        float fall = Mathf.Clamp01((duration - t) / 0.12f);
        return Mathf.Min(rise, fall);
    }

    /// <summary>Un coup part : on relance le geste de tir sur le haut du corps.
    ///
    /// Piloté par ÉVÉNEMENT et non par sondage d'un état, parce que tirer est un instant et
    /// non une condition qui dure — sonder raterait les coups tombant entre deux frames.
    /// L'événement est levé des deux côtés par WeaponController : chez le tireur depuis Fire(),
    /// chez tous les autres depuis la diffusion serveur qui existait déjà pour le son et le
    /// tracer. Aucun réseau supplémentaire n'a été nécessaire.</summary>
    private void DeclencherAnimationDeTir()
    {
        if (animator != null) animator.SetTrigger(FireId);
        Animator view = ViewAnimator;
        if (view != null) view.SetTrigger(FireId);
    }

    /// <summary>
    /// LateUpdate et pas Update : Move() est appelée depuis Update(), donc la position n'est
    /// définitive qu'après. Lire trop tôt donnerait la vitesse de la frame précédente.
    /// </summary>
    private void LateUpdate()
    {
        if (animator == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Un spectateur est à mains nues (2026-10-13) : ni arme, ni gant. Le rôle est publié par le
        // serveur, donc juste sur toutes les machines.
        bool spectating = locomotion.IsSpectator;
        if (spectating != shownSpectator)
        {
            shownSpectator = spectating;
            HeldWeapons.WearTaserGlove(animator, !spectating);
            ShowHeldWeapon();
        }
        else if (weapon != null && !spectating && weapon.Data != shownWeapon)
        {
            ShowHeldWeapon();
        }
        // Debout, l'arbre de déplacement à mains nues ; accroupi, les jambes armées sous un haut du
        // corps à mains nues (voir ApplyPostureTransition).
        animator.SetFloat(MainsNuesId, spectating ? 1f : 0f);
        pistolBlend = Mathf.MoveTowards(pistolBlend, pistolHold, dt / Mathf.Max(0.01f, holdBlendTime));
        animator.SetFloat(PistoletId, pistolBlend);

        Vector3 delta = transform.position - previousPosition;
        previousPosition = transform.position;
        delta.y = 0f;

        Vector3 local = transform.InverseTransformDirection(delta / dt);

        // La posture AFFICHÉE, transition comprise, et non CurrentStance : cette dernière n'est
        // mise à jour que dans Move(), qu'un spectateur n'appelle jamais. Elle y resterait bloquée
        // sur Standing, et l'adversaire n'aurait jamais d'animation accroupie ni allongée.
        // Symptôme vécu le 2026-09-29. Chez un spectateur, la transition est en plus calée sur
        // l'instant interpolé : l'adversaire se couche au moment où on le voit arriver là.
        locomotion.GetDisplayedStance(out PlayerLocomotion.Stance from, out PlayerLocomotion.Stance stance, out float progress);
        // L'allure de référence suit aussi la direction (on recule et on va de côté moins vite,
        // 2026-10-10) : reculer en marchant reste « la marche » pour l'arbre de mélange, et la
        // cadence des jambes se cale sur la vitesse réelle.
        float reference = Mathf.Lerp(locomotion.NominalSpeed(from), locomotion.NominalSpeed(stance), progress)
                          * locomotion.DirectionMultiplier(new Vector2(local.x, local.z))
                          * locomotion.DisplayedWeaponSpeedMultiplier;
        Vector2 target = new Vector2(local.x, local.z) / Mathf.Max(0.01f, reference);

        // Borne volontaire. Une resynchronisation de réconciliation TÉLÉPORTE le propriétaire de
        // quelques centimètres en une frame : dérivée telle quelle, cette téléportation vaut
        // plusieurs mètres par seconde et ferait sursauter l'animation à chaque recalage. On
        // plafonne donc à un peu plus que le sprint, ce qui laisse passer tout mouvement réel.
        target = Vector2.ClampMagnitude(target, 2.5f);

        smoothedMove = Vector2.Lerp(smoothedMove, target, 1f - Mathf.Exp(-blendSmoothing * dt));

        animator.SetInteger(StanceId, (int)stance);
        ApplyPostureTransition(from, stance, progress);

        // Cadence des animations d'ATTENTE du haut du corps (Arme, Vise) : elles respirent un peu
        // plus vite en mouvement, sans jamais descendre sous le plancher. Allongé, c'est aussi la
        // cadence de la reptation (voir DriveGait).
        float upperCadence = Mathf.Clamp(smoothedMove.magnitude, minPlaybackSpeed, 1f) * PlaybackScale(stance);
        animator.SetFloat(SpeedMultId, upperCadence);

        // Debout ou accroupi, au sol, sans changement de posture en cours : là seulement les jambes
        // pivotent sur place et les pieds se calent.
        AnimatorStateInfo legs = animator.GetCurrentAnimatorStateInfo(0);
        bool onFeet = !locomotion.DisplayAirborne && !locomotion.DisplayVaulting
                      && from != PlayerLocomotion.Stance.Prone && stance != PlayerLocomotion.Stance.Prone
                      && (legs.shortNameHash == DeboutStateId || legs.shortNameHash == AccroupiStateId);

        // Le bas du corps garde son cap quand le joueur tourne sur place, et pivote vers le regard
        // au-delà de TurnThreshold. Appliqué au modèle avant le calage des pieds et la direction du
        // buste, qui travaillent sur la pose ainsi tournée.
        bool idle = smoothedMove.magnitude < 0.05f;
        StepLegsTurn(ref legsTurn, transform.eulerAngles.y, idle, onFeet && from == stance, dt);
        animator.transform.rotation = Quaternion.Euler(0f, legsTurn.legsYaw, 0f);

        // Les pieds posés sont retenus au sol, sur la pose que l'Animator vient d'écrire (avant toute
        // autre retouche des os). Y compris pendant un changement debout ↔ accroupi : le fondu entre
        // les deux attentes faisait glisser le pied droit de 35 cm, il fait maintenant un pas.
        planter?.Apply(onFeet && LegsOnFeet(animator), dt, from != stance || animator.IsInTransition(0));

        // Les jambes : quel clip, et à quelle cadence pour que les pieds ne patinent pas.
        // Tout dans le repère du MODÈLE, qui peut être tourné par rapport au joueur le temps que les
        // jambes se réalignent : c'est celui où l'arbre de mélange place ses clips.
        Vector3 rootVelocity = rootMotion != null ? animator.transform.InverseTransformDirection(rootMotion.Velocity) : Vector3.zero;
        Vector3 legsMove = animator.transform.InverseTransformDirection(transform.TransformDirection(new Vector3(smoothedMove.x, 0f, smoothedMove.y)));
        DriveGait(animator, ref gait, new Vector2(legsMove.x, legsMove.z), reference, stance, new Vector2(rootVelocity.x, rootVelocity.z),
                  minPlaybackSpeed * PlaybackScale(stance), upperCadence, minLegCadence, maxLegCadence, dt);

        // Chute et franchissement passent par des accesseurs qui choisissent eux-mêmes entre la
        // valeur locale et la valeur réseautée selon le cas réseau. Ce script n'a donc pas à
        // connaître la notion de propriétaire — et il ne peut pas se tromper de source, ce qui
        // est exactement l'erreur commise sur la posture.
        animator.SetBool(AirborneId, locomotion.DisplayAirborne);
        animator.SetBool(VaultingId, locomotion.DisplayVaulting);

        // Le clip de vault est lu à la vitesse qui fait tenir sa partie UTILE dans la durée
        // réelle du franchissement.
        //
        // Deux pièges déjà payés ici. Le premier clip avait son geste à mi-course : sur 0,45 s on
        // n'en voyait que l'élan, d'où un personnage qui restait debout en passant l'obstacle. Le
        // second, joué en entier, était comprimé 2,6x et devenait illisible — alors que 40 % de
        // sa durée sont une préparation et une réception quasi immobiles. On entre donc dans
        // l'état à 20 % du clip et on n'étale que l'arc, ce qui tombe à 1,56x sans toucher au
        // gameplay. Au-delà, c'est vaultDuration qu'il faut allonger, et ça change le jeu.
        float duree = Mathf.Max(0.05f, locomotion.VaultDuration);
        animator.SetFloat(VaultSpeedId, vaultClipLength / duree);

        // La visée passe par le même aiguillage que le reste : IsAiming est affectée dans
        // Move(), qu'un spectateur n'appelle jamais, donc l'adversaire n'épaulerait jamais.
        animator.SetBool(AimingId, locomotion.DisplayAiming);

        // Rechargement : même aiguillage que le reste — la prédiction chez le tireur, l'état du
        // serveur chez les autres (WeaponController.IsReloading choisit). Le geste est lu à la
        // vitesse qui le fait tenir dans la durée réelle du rechargement, comme le vault.
        if (weapon != null)
        {
            bool reloading = weapon.IsReloading;
            if (reloading && !wasReloading) reloadAnimationDuration = Mathf.Max(0.05f, weapon.ReloadTimeRemaining);
            wasReloading = reloading;
            animator.SetBool(ReloadingId, reloading);
            animator.SetFloat(ReloadSpeedId, reloadClipUsefulLength / reloadAnimationDuration);
        }
        if (taser != null)
        {
            animator.SetFloat(TaserSpeedId, taserClipUsefulLength / Mathf.Max(0.05f, strikeGestureDuration));
            if (animator.layerCount > TaserLayer) animator.SetLayerWeight(TaserLayer, TaserLayerWeight(stance));
        }
        UpdateSpectatorLayers(spectating, dt);

        ApplyProneModelOffset(from, stance, progress);
        ApplyAimDirection(stance, animator.layerCount > UpperBodyLayer ? animator.GetLayerWeight(UpperBodyLayer) : 0f, dt);
        ApplyVisualLean(from, stance, progress);
        MirrorToView();

#if UNITY_EDITOR
        // Rapport entre la vitesse où l'animation fait avancer les pieds et la vitesse réelle, quand
        // le déplacement joue seul : 100 % = pieds calés, moins = le corps glisse plus vite qu'eux.
        // Seulement sur un mélange stable, comme la mesure de la cadence : pendant que le mélange
        // change, l'Animator renvoie des déplacements faux (jusqu'à 20 m/s au demi-tour), qui ne se
        // voient pas mais faisaient lire 137 % là où les pieds étaient à 97 % (2026-10-11).
        float bodySpeed = smoothedMove.magnitude * reference;
        float footRatio = gait.marche >= 0.99f && gait.steadyFrames >= 2 && bodySpeed > 0.3f
            ? new Vector2(rootVelocity.x, rootVelocity.z).magnitude / bodySpeed
            : float.NaN;
        DiagRecord(delta.magnitude, locomotion.DisplayAirborne, footRatio);
#endif
    }

#if UNITY_EDITOR
    // ------------------------------------------------------------------
    // Diagnostic d'animation — Editor uniquement, jamais embarqué en build.
    //
    // Journalise [DIAG-ANIM] toutes les 3 s pour les joueurs que CETTE instance ne possède pas,
    // c'est-à-dire ceux qu'elle voit bouger. Sert à distinguer, chiffres à l'appui, les causes
    // possibles d'une animation saccadée chez un client : un état d'Animator relancé en boucle
    // (entrées, relances), un drapeau réseau qui clignote (bascules « en l'air »), ou une
    // position qui avance par à-coups (frames immobiles alors que le personnage court).
    // Lisible côté client dans Library/VP/<clone>/Logs/Editor.log.
    // ------------------------------------------------------------------
    private struct DiagLayer
    {
        public int hash;
        public float normalizedTime;
        public int entries;
        public int restarts;
    }

    private DiagLayer diagBase;
    private DiagLayer diagUpper;
    private int diagFrames;
    private int diagAirFlips;
    private int diagStalls;
    private bool diagAir;
    private float diagMaxStep;
    private float diagFootRatioSum;
    private int diagFootRatioFrames;
    private float diagWindowStart = -1f;

    private void DiagRecord(float step, bool airborne, float footRatio)
    {
        if (locomotion.IsOwner) return;

        if (diagWindowStart < 0f) diagWindowStart = Time.unscaledTime;
        diagFrames++;
        DiagLayerRecord(ref diagBase, 0);
        if (animator.layerCount > 1) DiagLayerRecord(ref diagUpper, 1);

        if (airborne != diagAir)
        {
            diagAirFlips++;
            diagAir = airborne;
        }

        // Une frame IMMOBILE alors que le personnage est animé en pleine course : sa position
        // avance par à-coups, ce que l'œil lit comme une saccade.
        if (step < 1e-5f && smoothedMove.magnitude > 0.3f) diagStalls++;
        if (step > diagMaxStep) diagMaxStep = step;
        if (!float.IsNaN(footRatio))
        {
            diagFootRatioSum += footRatio;
            diagFootRatioFrames++;
        }

        float elapsed = Time.unscaledTime - diagWindowStart;
        if (elapsed < 3f) return;

        Debug.Log($"[DIAG-ANIM] {name} fps={diagFrames / elapsed:F0} " +
                  $"base_entrees={diagBase.entries} base_relances={diagBase.restarts} " +
                  $"buste_entrees={diagUpper.entries} buste_relances={diagUpper.restarts} " +
                  $"bascules_air={diagAirFlips} frames_immobiles={diagStalls}/{diagFrames} " +
                  $"pas_max={diagMaxStep * 100f:F1}cm affamees={locomotion.DiagStarvedFrames} " +
                  $"reserve={(locomotion.DiagReserveFrames > 0 ? locomotion.DiagReserveSum / locomotion.DiagReserveFrames * 1000.0 : 0.0):F0}ms " +
                  $"posture={locomotion.NetworkedStance} cadence={gait.cadence:F2} vitesse_naturelle={gait.naturalSpeed:F2}m/s " +
                  $"pieds={(diagFootRatioFrames > 0 ? diagFootRatioSum / diagFootRatioFrames * 100f : 0f):F0}% (sur {diagFootRatioFrames} images) " +
                  $"buste={torsoYaw:F0}°/{torsoPitch:F0}° tangage_affiche={locomotion.DisplayAimPitch:F0}°");
        locomotion.DiagStarvedFrames = 0;
        locomotion.DiagReserveSum = 0.0;
        locomotion.DiagReserveFrames = 0;

        diagWindowStart = Time.unscaledTime;
        diagFrames = 0;
        diagAirFlips = 0;
        diagStalls = 0;
        diagMaxStep = 0f;
        diagFootRatioSum = 0f;
        diagFootRatioFrames = 0;
        diagBase.entries = diagBase.restarts = 0;
        diagUpper.entries = diagUpper.restarts = 0;
    }

    /// <summary>Compte les entrées dans un nouvel état ET les relances d'un même état : une
    /// relance ramène son temps normalisé en arrière, alors qu'une boucle normale le fait
    /// croître sans fin (3,2 → 3,25).</summary>
    private void DiagLayerRecord(ref DiagLayer layer, int index)
    {
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(index);
        if (info.fullPathHash != layer.hash) layer.entries++;
        else if (info.normalizedTime < layer.normalizedTime - 0.02f) layer.restarts++;
        layer.hash = info.fullPathHash;
        layer.normalizedTime = info.normalizedTime;
    }
#endif

    /// <summary>
    /// Transition de posture, VUE. La couche de déplacement passe d'une posture à l'autre par ses
    /// propres transitions, déclenchées par le paramètre Stance ; ce qui se règle ici, c'est le
    /// reste.
    ///
    /// Vers ou depuis la position allongée, le corps entier joue un clip de transition (genou à
    /// terre ↔ allongé). Le haut du corps doit alors s'effacer : sa couche garderait sinon le buste
    /// droit, arme à l'épaule, au-dessus de jambes en train de se coucher. Il s'efface au début et
    /// revient à la fin, sur la posture d'arrivée. Entre debout et accroupi, rien de tel : le buste
    /// reste armé et glisse simplement d'une posture à l'autre.
    /// </summary>
    private void ApplyPostureTransition(PlayerLocomotion.Stance from, PlayerLocomotion.Stance to, float progress)
    {
        bool viaGround = from != to && (from == PlayerLocomotion.Stance.Prone || to == PlayerLocomotion.Stance.Prone);

        float upperWeight = 1f;
        float stanceF;
        if (viaGround)
        {
            // S'efface sur les premiers 10 % de la transition, revient sur les 20 derniers.
            upperWeight = 1f - Mathf.Clamp01(Mathf.Min(progress / 0.1f, (1f - progress) / 0.2f));
            stanceF = progress < 0.5f ? (int)from : (int)to;
        }
        else
        {
            stanceF = Mathf.Lerp((int)from, (int)to, progress);
        }
        // Un spectateur (2026-10-13) : debout, tout son corps joue l'arbre à mains nues et la couche se
        // tait ; accroupi, elle pose des bras à mains nues (état MainsNues) sur les jambes accroupies,
        // faute de marches accroupies à mains nues qui conviennent. Il ne s'allonge jamais.
        if (locomotion.IsSpectator) upperWeight = Mathf.Clamp01(stanceF);

        if (animator.layerCount > UpperBodyLayer) animator.SetLayerWeight(UpperBodyLayer, upperWeight);

        // Miroir FLOTTANT de la posture, pour les arbres du haut du corps. Un arbre de mélange
        // n'accepte qu'un paramètre float, alors que les transitions de la couche de déplacement
        // ont besoin d'un entier pour comparer exactement. Duplication imposée par Unity.
        animator.SetFloat(StanceFId, stanceF);

        // Le clip de transition est lu à la vitesse qui fait tenir sa partie UTILE dans la durée
        // de la transition — même principe que le vault. Debout ↔ allongé ajoute un genou posé
        // en fondu (0,25 s) avant ou après le même clip.
        float duree = Mathf.Max(0.05f, locomotion.CrouchProneTransitionDuration);
        animator.SetFloat(PostureSpeedId, proneTransitionClipLength / duree);
    }

    /// <summary>Allongé, le modèle recule pour que sa tête soit là où sont la caméra et la tête
    /// touchable, à l'intérieur du CharacterController. Il y glisse au rythme de la transition de
    /// posture, comme la surface touchable.</summary>
    private void ApplyProneModelOffset(PlayerLocomotion.Stance from, PlayerLocomotion.Stance to, float progress)
    {
        Transform model = animator.transform;
        Vector3 p = model.localPosition;
        p.z = Mathf.Lerp(ModelOffset(from), ModelOffset(to), progress);
        model.localPosition = p;
    }

    private float ModelOffset(PlayerLocomotion.Stance stance)
        => stance == PlayerLocomotion.Stance.Prone ? proneModelOffset : 0f;

    /// <summary>
    /// Lean façon Rainbow Six, VU : le buste tourne autour de la base de la colonne — il s'incline
    /// debout et accroupi, il roule sur lui-même allongé — et les jambes ne bougent pas.
    ///
    /// La rotation vient de BodyLayout, avec le même décalage réseauté que la surface touchable :
    /// l'animation et le hitbox lisent la même source, aucun ne lit l'autre. On touche ce qu'on
    /// voit, sans que le hitbox dépende jamais d'un os animé.
    ///
    /// Appliquée ici, en LateUpdate, APRÈS l'évaluation de l'Animator : la rotation s'ajoute à la
    /// pose de la frame, que l'Animator réécrit à chaque évaluation. D'où l'obligation de garder
    /// l'Animator en AlwaysAnimate : s'il cessait d'évaluer (modèle hors champ), la rotation
    /// s'accumulerait frame après frame et le buste tournerait sur lui-même.
    /// </summary>
    private void ApplyVisualLean(PlayerLocomotion.Stance from, PlayerLocomotion.Stance to, float progress)
    {
        PlayerHitbox hitbox = locomotion.Hitbox;
        if (hitbox == null) return;

        float lean = locomotion.DisplayLeanOffset;
        if (Mathf.Abs(lean) < 0.001f) return;

        Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        if (spine == null) return;

        // Rotation exprimée dans l'espace du JOUEUR : on la convertit dans le repère monde, puis
        // on l'applique à l'os autour de sa propre position (la base de la colonne). Pendant une
        // transition de posture, elle glisse d'une posture à l'autre, comme la surface touchable.
        Quaternion local = Quaternion.Slerp(hitbox.LeanRotation(from, locomotion.EyeLocal(from), lean),
                                            hitbox.LeanRotation(to, locomotion.EyeLocal(to), lean), progress);
        Quaternion world = transform.rotation * local * Quaternion.Inverse(transform.rotation);
        spine.rotation = world * spine.rotation;
    }

    // ------------------------------------------------------------------
    // Le pivot sur place (2026-10-10)
    // ------------------------------------------------------------------

    /// <summary>Écart (degrés) entre le regard et le cap des jambes au-delà duquel, à l'arrêt, les
    /// jambes se mettent à pivoter. En deçà, seul le buste tourne.</summary>
    public const float TurnThreshold = 70f;

    /// <summary>Vitesse (degrés/s) du pivot sur place. Bornée par le calage des pieds : pendant
    /// qu'un pied fait son pas (FootPlanter.StepTime), l'autre ne doit pas être tiré au-delà de
    /// ce qu'une jambe accroupie peut atteindre. 90° prennent une seconde, comme un vrai pivot.</summary>
    public const float TurnSpeed = 90f;

    /// <summary>Écart maximal (degrés) : au-delà, les jambes sont entraînées d'un bloc. N'arrive
    /// qu'en tournant très vite, plus vite que le pivot. Sous maxTorsoYaw, pour que le buste puisse
    /// toujours faire face au regard.</summary>
    public const float MaxLegsLag = 100f;

    /// <summary>Vitesse (degrés/s) à laquelle les jambes se réalignent sur le regard dès qu'on se
    /// déplace : l'arbre de déplacement suppose un modèle aligné sur le joueur.</summary>
    public const float LegsAlignSpeed = 540f;

    /// <summary>Ce que le pivot sur place garde d'une image à l'autre.</summary>
    public struct LegsTurnState
    {
        /// <summary>Cap du bas du corps dans le monde (degrés).</summary>
        public float legsYaw;
        public bool initialised;
        /// <summary>Les jambes pivotent pour rattraper le regard.</summary>
        public bool turning;
    }

    /// <summary>
    /// La vitesse des jambes pour l'arbre de déplacement, normalisée par l'allure de référence :
    /// MoveX et MoveY (la direction, et sa norme), et Allure, cette norme seule (2026-10-10). Debout,
    /// Allure choisit l'anneau : lent (0,4, sneak et visée), marche (1), sprint (2), chacun avec ses
    /// propres directions. Dans un seul arbre directionnel, une direction présente sur un anneau et
    /// absente du voisin (la marche diagonale avant, sans course diagonale avant) attirait tout le
    /// mélange : la diagonale à 5 m/s jouait la marche lente, et glissait. Public pour les tests.
    /// </summary>
    public static void SetLocomotion(Animator animator, Vector2 move)
    {
        animator.SetFloat(MoveXId, move.x);
        animator.SetFloat(MoveYId, move.y);
        animator.SetFloat(AllureId, move.magnitude);
    }

    /// <summary>La couche des jambes joue-t-elle debout ou accroupi, fondu de l'un à l'autre
    /// compris ? Là seulement les pieds se calent (FootPlanter) : ni en l'air, ni en franchissement,
    /// ni pendant un passage par le sol, où le corps entier joue un clip. Public pour les tests.</summary>
    public static bool LegsOnFeet(Animator animator)
    {
        int current = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        if (current != DeboutStateId && current != AccroupiStateId) return false;
        if (!animator.IsInTransition(0)) return true;
        int next = animator.GetNextAnimatorStateInfo(0).shortNameHash;
        return next == DeboutStateId || next == AccroupiStateId;
    }

    /// <summary>
    /// Le bas du corps garde son cap quand le joueur tourne sur place (2026-10-10). Fonction pure,
    /// testée.
    ///
    /// Sans ça, le robot pivotait d'un bloc avec le regard, et ses pieds tournaient en glissant sur
    /// le sol. Comme dans la plupart des FPS : à l'arrêt, seul le buste suit le regard
    /// (ApplyAimDirection le tourne jusqu'à maxTorsoYaw) ; au-delà de TurnThreshold, les jambes
    /// pivotent jusqu'au regard, à TurnSpeed, pendant que FootPlanter garde chaque pied posé et lui
    /// fait faire un petit pas quand le corps l'a trop tiré. Les pieds ne bougent qu'en l'air.
    ///
    /// Les clips de pivot de Mixamo (Turn 90, debout et accroupi) ont été essayés et écartés le même
    /// jour : ils ne posent pas les pieds où nos attentes les posent (accroupi, la pointe gauche est
    /// à 14 cm du centre au lieu de 33), donc passer de l'un à l'autre les faisait traîner au sol, et
    /// ils glissent d'eux-mêmes de 5 à 25 cm.
    ///
    /// Dès qu'on se déplace, les jambes se réalignent sur le regard. Catégorie C stricte : le cap
    /// des jambes n'existe que dans l'affichage de chaque machine. Ni la simulation, ni le tir, ni la
    /// surface touchable ne le lisent.
    /// </summary>
    /// <param name="playerYaw">Cap du joueur (son regard), degrés.</param>
    /// <param name="idle">Le joueur est-il immobile ?</param>
    /// <param name="canTurn">Debout ou accroupi, au sol, sans changement de posture en cours.</param>
    public static void StepLegsTurn(ref LegsTurnState s, float playerYaw, bool idle, bool canTurn, float dt)
    {
        if (!s.initialised)
        {
            s.legsYaw = playerYaw;
            s.initialised = true;
        }

        float lag = Mathf.DeltaAngle(s.legsYaw, playerYaw); // + : le regard est à droite des jambes
        if (!idle || !canTurn)
        {
            s.legsYaw = Mathf.MoveTowardsAngle(s.legsYaw, playerYaw, LegsAlignSpeed * dt);
            s.turning = false;
        }
        else
        {
            if (!s.turning && Mathf.Abs(lag) > TurnThreshold) s.turning = true;
            if (s.turning)
            {
                s.legsYaw = Mathf.MoveTowardsAngle(s.legsYaw, playerYaw, TurnSpeed * dt);
                if (Mathf.Abs(Mathf.DeltaAngle(s.legsYaw, playerYaw)) < 0.5f) s.turning = false;
            }
        }

        float offset = Mathf.DeltaAngle(s.legsYaw, playerYaw);
        if (Mathf.Abs(offset) > MaxLegsLag) s.legsYaw = playerYaw - Mathf.Sign(offset) * MaxLegsLag;
        s.legsYaw = Mathf.Repeat(s.legsYaw, 360f);
    }

    // ------------------------------------------------------------------
    // Les jambes : des pieds calés sur le sol (2026-10-09)
    // ------------------------------------------------------------------

    /// <summary>Ce que la cadence des jambes garde d'une image à l'autre.</summary>
    public struct GaitState
    {
        /// <summary>Dernière direction de déplacement (repère du joueur), gardée à l'arrêt pour que
        /// l'arbre ne saute pas d'un clip à l'autre pendant que le personnage s'immobilise.</summary>
        public Vector2 direction;

        /// <summary>Vitesse à laquelle l'animation de déplacement en cours fait avancer le corps à
        /// cadence 1, en m/s : MESURÉE sur l'Animator, jamais recopiée des clips. C'est la norme de
        /// la moyenne des VECTEURS vitesse (naturalVelocity), pas la moyenne de leurs normes : un clip
        /// qui zigzague (la reptation se tortille d'un côté à l'autre) avance moins que ne le dit sa
        /// vitesse instantanée, et la cadence en tirait des pieds 20 % trop lents (2026-10-10).</summary>
        public float naturalSpeed;
        public Vector2 naturalVelocity;

        /// <summary>Ce que l'animation fait avancer à chaque tranche de son cycle de pas, rapporté à
        /// la vitesse qu'annoncent ses clips (2026-10-11) : la vitesse naturelle en est la moyenne,
        /// multipliée par la vitesse annoncée du mélange joué (clipSpeed). Une tranche n'est
        /// remplacée que par une mesure de la même phase : un bout de cycle ne fausse plus rien.
        /// Un profil par clip dominant (profiles) : le pas chassé du sneak avance par à-coups, la
        /// marche régulièrement, et leurs tranches ne se mélangent pas.</summary>
        public Vector2[] phaseEfficiency;
        public int phaseBin;
        public Dictionary<AnimationClip, Vector2[]> profiles;
        public AnimationClip profileClip;

        /// <summary>Vitesse qu'annoncent les clips de déplacement du mélange joué, à cadence 1 (m/s,
        /// AnimationClip.averageSpeed pondéré par le poids et la foulée de chacun).</summary>
        public float clipSpeed;

        /// <summary>Évaluations consécutives où les poids des clips joués n'ont presque pas bougé :
        /// pendant que le mélange change (demi-tour, passage d'un anneau à l'autre, attente qui se
        /// mêle), l'Animator renvoie des déplacements de racine faux, jusqu'à 10 m/s.</summary>
        public int steadyFrames;

        /// <summary>Les clips de l'évaluation précédente et leurs poids, pour mesurer ce changement.</summary>
        public AnimationClip[] mixClips;
        public float[] mixWeights;
        public int mixCount;

        /// <summary>Écart moyen (degrés) entre la direction où l'animation fait avancer le corps et
        /// la direction voulue, et la rotation qui en découle, appliquée à la direction envoyée à
        /// l'arbre (+ = vers la gauche).</summary>
        public float headingError;
        public float headingCorrection;

        /// <summary>Posture des mesures, et depuis combien de temps elles s'accumulent : changer de
        /// posture change de clips, et une mesure jeune se fait en moyenne simple.</summary>
        public int measuredStance;
        public float measureTime;

        /// <summary>Poids du déplacement, cadence et direction voulue de la dernière image : ce sont
        /// ceux de l'évaluation que l'on mesure à l'image suivante.</summary>
        public float marche;
        public float cadence;
        public Vector2 lastDesired;
    }

    /// <summary>En dessous, la vitesse de racine mesurée ne veut rien dire : clip sur place, ou rien
    /// de mesuré encore. On se rabat alors sur la cadence de la posture. Sous la reptation (0,29 m/s),
    /// qui doit se mesurer comme le reste depuis qu'aucune glissade n'est tolérée (2026-10-10).</summary>
    private const float MinNaturalSpeed = 0.15f;

    /// <summary>Fenêtre minimale de moyenne des mesures (s). La vraie fenêtre est un cycle de pas :
    /// la vitesse de racine varie d'un bout à l'autre d'un pas, seule sa moyenne sur le cycle compte.</summary>
    private const float MinMeasureWindow = 0.25f;

    /// <summary>Écart de direction toléré (degrés) avant de corriger. Un clip seul part rarement
    /// pile dans l'axe (le sprint dévie de 4°) : sans cette zone morte, la correction tournerait
    /// jusqu'à y mêler un clip voisin, et le sprint finissait à 19 % de course latérale.</summary>
    private const float HeadingDeadband = 6f;

    /// <summary>Correction de direction maximale (degrés).</summary>
    private const float MaxHeadingCorrection = 45f;

    /// <summary>Tranches du cycle de pas sur lesquelles la vitesse naturelle se mesure.</summary>
    private const int PhaseBins = 8;

    /// <summary>Part du chemin qu'un passage dans une tranche lui fait faire vers sa mesure.</summary>
    private const float PhaseLearningPerPass = 0.5f;

    /// <summary>Sous cette vitesse annoncée (m/s), un clip est sur place : l'attente.</summary>
    private const float MovingClipSpeed = 0.05f;

    /// <summary>Changement des poids des clips joués (somme des écarts, par seconde) au-delà duquel
    /// une évaluation n'est pas mesurée. Au demi-tour, il dépasse 15 par seconde.</summary>
    private const float MaxMixChangeRate = 2f;

    private const int MaxMixClips = 16;

    private static readonly List<AnimatorClipInfo> clipInfos = new List<AnimatorClipInfo>();
    private static readonly bool[] mixSeen = new bool[MaxMixClips];

    /// <summary>
    /// Vitesse qu'annoncent les clips de déplacement que joue la couche des jambes, à cadence 1
    /// (m/s) ; 0 si aucun n'avance. Un arbre synchronise ses clips sur une durée commune : chacun y
    /// pèse donc selon sa foulée (sa vitesse × sa durée), et le mélange avance de la somme des
    /// foulées sur la somme des durées. L'attente, sur place, n'y entre pas. Ce n'est qu'une
    /// estimation (Unity mêle les déplacements dans le repère du buste de chaque clip) : DriveGait
    /// MESURE l'écart, et ne s'en sert que comme échelle.
    /// </summary>
    private static float MixClipSpeed(List<AnimatorClipInfo> infos)
    {
        float distance = 0f, duration = 0f;
        foreach (AnimatorClipInfo info in infos)
        {
            if (info.clip == null || info.weight <= 0f) continue;
            float speed = info.clip.averageSpeed.magnitude;
            if (speed < MovingClipSpeed) continue;
            distance += info.weight * speed * info.clip.length;
            duration += info.weight * info.clip.length;
        }
        return duration > 1e-4f ? distance / duration : 0f;
    }

    /// <summary>Le clip qui avance et pèse le plus dans le mélange, ou null.</summary>
    private static AnimationClip DominantMovingClip(List<AnimatorClipInfo> infos)
    {
        AnimationClip dominant = null;
        float best = 0f;
        foreach (AnimatorClipInfo info in infos)
        {
            if (info.clip == null || info.weight <= best || info.clip.averageSpeed.magnitude < MovingClipSpeed) continue;
            best = info.weight;
            dominant = info.clip;
        }
        return dominant;
    }

    /// <summary>Passe au profil du clip dominant. Un clip jamais mesuré part de la vitesse qu'il
    /// annonce (rapport 1, dans la direction voulue) : les clips du jeu avancent à 1 à 4 % de leur
    /// annonce quand ils jouent seuls, et un clip qui ne domine que le temps d'un demi-tour n'est
    /// jamais mesuré. Hériter du profil qu'on quitte héritait aussi de ses erreurs.</summary>
    private static void UseProfile(ref GaitState state, AnimationClip clip)
    {
        if (state.profiles == null) state.profiles = new Dictionary<AnimationClip, Vector2[]>();
        if (!state.profiles.TryGetValue(clip, out Vector2[] profile))
        {
            profile = new Vector2[PhaseBins];
            for (int b = 0; b < PhaseBins; b++) profile[b] = state.direction;
            state.profiles[clip] = profile;
        }
        state.phaseEfficiency = profile;
        state.profileClip = clip;
        state.phaseBin = -1;
    }

    /// <summary>De combien les poids des clips joués ont changé depuis l'évaluation précédente
    /// (somme des écarts, un clip apparu ou disparu compte pour son poids), et retient ceux-ci.</summary>
    private static float MixChange(ref GaitState state, List<AnimatorClipInfo> infos)
    {
        if (state.mixClips == null)
        {
            state.mixClips = new AnimationClip[MaxMixClips];
            state.mixWeights = new float[MaxMixClips];
        }
        float change = 0f;
        int count = 0;
        bool[] seen = mixSeen;
        System.Array.Clear(seen, 0, MaxMixClips);
        foreach (AnimatorClipInfo info in infos)
        {
            if (info.clip == null || count >= MaxMixClips) continue;
            float previous = 0f;
            for (int j = 0; j < state.mixCount; j++)
            {
                if (seen[j] || !ReferenceEquals(state.mixClips[j], info.clip)) continue;
                previous = state.mixWeights[j];
                seen[j] = true;
                break;
            }
            change += Mathf.Abs(info.weight - previous);
            count++;
        }
        for (int j = 0; j < state.mixCount; j++)
            if (!seen[j]) change += state.mixWeights[j];

        state.mixCount = 0;
        foreach (AnimatorClipInfo info in infos)
        {
            if (info.clip == null || state.mixCount >= MaxMixClips) continue;
            state.mixClips[state.mixCount] = info.clip;
            state.mixWeights[state.mixCount] = info.weight;
            state.mixCount++;
        }
        return change;
    }

    /// <summary>
    /// Choisit le clip des jambes et sa cadence, pour que les pieds avancent à la vitesse et dans la
    /// direction réelles.
    ///
    /// Trois défauts mesurés le 2026-10-09, à l'origine de « je glisse sur le sol » en visée et en
    /// sneak (pieds à 3 à 10 % de la vitesse réelle) :
    /// - Un arbre de mélange SYNCHRONISE ses clips : sa durée est la moyenne pondérée des leurs.
    ///   L'attente debout dure 8,6 s ; mêlée à 45 % à une course de 0,5 s, elle étirait le cycle à
    ///   6,4 s et la course jouait dix fois trop lentement. L'attente et le déplacement sont donc
    ///   deux branches séparées (paramètre Marche), et le déplacement ne contient plus d'attente.
    /// - La cadence était un multiplicateur fixe par posture. Elle se MESURE désormais : la vitesse
    ///   de racine que l'Animator calcule pour l'animation jouée (AnimatorRootMotionProbe), ramenée
    ///   à la cadence où elle a été jouée, dit à quelle vitesse ce mélange précis fait avancer les
    ///   pieds. La cadence suivante en découle. Aucune table de vitesses à tenir à jour : un clip
    ///   remplacé ou une vitesse de jeu changée se recalent seuls.
    /// - En diagonale, le mélange ne part pas où l'arbre le place : chaque clip pèse selon la
    ///   longueur de sa FOULÉE (1 m par cycle pour le pas chassé, plus de 2 m pour les courses), donc
    ///   le mélange penche vers la course, jusqu'à 33° de travers. La direction réelle de l'animation
    ///   se mesure de la même façon, et la direction envoyée à l'arbre est tournée d'autant.
    ///
    /// Les mesures se font en moyenne sur un cycle de pas, et seulement quand le déplacement joue
    /// seul (ni attente mêlée, ni transition d'état), sinon elles seraient faussées. Tant que rien
    /// n'est mesuré, la posture garde sa cadence réglée à la main.
    ///
    /// Deux défauts trouvés à deux instances le 2026-10-11, invisibles quand on marche droit : un
    /// joueur en sneak (qui jouait alors les Rifle Sneak, essayés puis écartés le jour même) qui va
    /// de gauche à droite toutes les secondes avait, vu des autres, les pieds à 44 à 72 % de sa
    /// vitesse ; le sneak accroupi avait le même défaut.
    /// - Au demi-tour, le mélange passe par l'attente puis repart de l'autre côté, et l'évaluation
    ///   qui suit ce changement brusque renvoie un déplacement de racine aberrant (jusqu'à 10 m/s),
    ///   que la cadence, basse à cet instant, gonflait encore. On ne mesure donc qu'après deux images
    ///   où les poids des clips joués n'ont presque pas bougé (steadyFrames). Le même défaut se
    ///   montre quand l'allure traverse d'un coup les anneaux (visée de côté : marche, sneak, puis
    ///   marche, en 0,3 s, du temps de cet anneau de sneak).
    /// - Un pas chassé peut avancer par à-coups (celui des Rifle Sneak : 0,6 m/s pendant la moitié du
    ///   cycle, rien pendant l'autre). Une moyenne qui repartait presque de zéro à chaque demi-tour ne voyait
    ///   qu'un bout de cycle et trouvait jusqu'à trois fois la vraie vitesse ; la cadence baissait,
    ///   le cycle s'allongeait, et la mesure en voyait une part encore plus petite. La mesure se fait
    ///   désormais PAR TRANCHE du cycle (phaseEfficiency) : une tranche ne se remplace que par une
    ///   mesure de la même phase, et la vitesse est leur moyenne. Chaque clip dominant garde son
    ///   profil : au demi-tour, celui du clip miroir revient tel qu'on l'a laissé. Les tranches sont
    ///   rapportées à la vitesse qu'annoncent les clips joués (MixClipSpeed) : en passant de l'avant
    ///   au côté, la vitesse change d'échelle à l'image près, et les tranches n'apprennent que
    ///   l'écart entre l'annonce et le mélange réel.
    ///
    /// Statique et sans réseau : LocomotionFootTests la conduit sur le vrai contrôleur.
    /// </summary>
    /// <param name="move">Vitesse du joueur dans son repère, divisée par la vitesse de référence
    /// de la posture (1 = allure nominale).</param>
    /// <param name="referenceSpeed">Cette vitesse de référence, en m/s.</param>
    /// <param name="measuredRootVelocity">Vitesse de racine de l'évaluation qui vient d'avoir lieu,
    /// dans le repère du joueur (x à droite, y devant), en m/s.</param>
    public static void DriveGait(Animator animator, ref GaitState state, Vector2 move, float referenceSpeed,
                                 PlayerLocomotion.Stance stance, Vector2 measuredRootVelocity,
                                 float idleCadence, float fallbackCadence, float minCadence, float maxCadence, float dt)
    {
        // Allongé, l'arbre ne connaît que l'avant et l'arrière : de côté, faute de clip, le corps
        // garde la pose d'attente.
        Vector2 driven = stance == PlayerLocomotion.Stance.Prone ? new Vector2(0f, move.y) : move;
        float magnitude = driven.magnitude;
        if (magnitude > 0.05f) state.direction = driven / magnitude;
        else if (state.direction == Vector2.zero) state.direction = Vector2.up;
        if (state.phaseEfficiency == null) state.phaseEfficiency = new Vector2[PhaseBins];

        // Un changement franc de direction change de mélange : la correction de direction repart de
        // zéro. La vitesse, elle, suit le clip qui domine (voir UseProfile).
        bool sameDirection = Vector2.Angle(state.direction, state.lastDesired) <= 20f;
        if (!sameDirection)
        {
            state.headingCorrection = 0f;
            state.headingError = 0f;
            state.measureTime = Mathf.Min(state.measureTime, 0.1f);
        }

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        bool locomotionState = info.shortNameHash == DeboutStateId || info.shortNameHash == AccroupiStateId
                               || info.shortNameHash == ProneStateId;
        // Le mélange de l'évaluation qu'on mesure ; à l'arrêt (attente seule), on garde le dernier.
        // On ne la mesure qu'après deux évaluations où il n'a presque pas bougé.
        animator.GetCurrentAnimatorClipInfo(0, clipInfos);
        float clipSpeed = MixClipSpeed(clipInfos);
        if (clipSpeed > 0f) state.clipSpeed = clipSpeed;
        AnimationClip dominant = DominantMovingClip(clipInfos);
        if (dominant != null && dominant != state.profileClip) UseProfile(ref state, dominant);
        bool stable = state.marche >= 0.99f && MixChange(ref state, clipInfos) <= MaxMixChangeRate * dt;
        state.steadyFrames = stable ? state.steadyFrames + 1 : 0;
        if (state.steadyFrames >= 2 && state.cadence > 0.05f && state.clipSpeed > 0f
            && locomotionState && !animator.IsInTransition(0))
        {
            Vector2 efficiency = measuredRootVelocity / state.cadence / state.clipSpeed;
            if (state.naturalSpeed < MinNaturalSpeed)
            {
                for (int b = 0; b < PhaseBins; b++) state.phaseEfficiency[b] = efficiency;
                state.measureTime = 0f;
                state.headingError = 0f;
                state.headingCorrection = 0f;
            }
            if (state.measuredStance != (int)stance)
            {
                // Les tranches, rapportées à la vitesse annoncée, valent pour l'autre posture le temps
                // de s'y remesurer : mieux qu'une seule image, prise à une phase quelconque du pas.
                state.measureTime = 0f;
                state.headingError = 0f;
                state.headingCorrection = 0f;
            }
            state.measuredStance = (int)stance;

            // Chaque passage dans une tranche la rapproche de moitié de ce qu'il mesure : la cadence
            // reste aussi régulière qu'avec une moyenne glissante. info.length est la durée du cycle
            // à la cadence jouée.
            float window = Mathf.Max(MinMeasureWindow, info.length);
            int bin = Mathf.Min(PhaseBins - 1, (int)(Mathf.Repeat(info.normalizedTime, 1f) * PhaseBins));
            state.phaseBin = bin;
            float binWeight = Mathf.Min(1f, PhaseLearningPerPass * PhaseBins * dt / window);
            state.phaseEfficiency[bin] += (efficiency - state.phaseEfficiency[bin]) * binWeight;

            // La direction se corrige en moyenne simple tant que la mesure est plus jeune qu'un cycle
            // de pas, glissante ensuite.
            state.measureTime += dt;
            float k = dt / Mathf.Min(state.measureTime, window);

            if (measuredRootVelocity.magnitude > MinNaturalSpeed && sameDirection)
            {
                float error = Vector2.SignedAngle(measuredRootVelocity, state.lastDesired);
                state.headingError += (error - state.headingError) * k;
                float excess = Mathf.Sign(state.headingError) * Mathf.Max(0f, Mathf.Abs(state.headingError) - HeadingDeadband);
                state.headingCorrection = Mathf.Clamp(state.headingCorrection + excess * dt / window,
                                                      -MaxHeadingCorrection, MaxHeadingCorrection);
            }
        }

        // La vitesse naturelle du mélange joué : ce que mesurent les tranches, à l'échelle de ce
        // qu'annoncent ses clips.
        Vector2 mean = Vector2.zero;
        for (int b = 0; b < PhaseBins; b++) mean += state.phaseEfficiency[b];
        state.naturalVelocity = mean / PhaseBins * state.clipSpeed;
        state.naturalSpeed = state.naturalVelocity.magnitude;

        // Le déplacement remplace l'attente dès 0,05 d'allure (0,1 avant le 2026-10-11) : les allures
        // les plus lentes restent des pas, pas une attente qui glisse.
        float marche = Mathf.Clamp01((magnitude - 0.01f) / 0.04f);
        bool measured = state.naturalSpeed >= MinNaturalSpeed && state.measuredStance == (int)stance;
        float moveCadence = measured
            ? Mathf.Clamp(magnitude * referenceSpeed / state.naturalSpeed, minCadence, maxCadence)
            : fallbackCadence;
        float cadence = Mathf.Lerp(idleCadence, moveCadence, marche);

        // L'arbre de déplacement prend la vitesse telle quelle : sous l'anneau lent il joue l'anneau
        // lent, au-delà de l'anneau extérieur il joue l'extérieur (vérifié). La direction est tournée
        // de la correction mesurée, et gardée à l'arrêt.
        float a = state.headingCorrection * Mathf.Deg2Rad;
        Vector2 d = state.direction;
        Vector2 fed = new Vector2(d.x * Mathf.Cos(a) - d.y * Mathf.Sin(a), d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a))
                      * Mathf.Max(magnitude, 0.05f);
        SetLocomotion(animator, fed);
        animator.SetFloat(MarcheId, marche);
        animator.SetFloat(LocoSpeedId, cadence);

        state.marche = marche;
        state.cadence = cadence;
        state.lastDesired = state.direction;
    }

    // ------------------------------------------------------------------
    // Le buste suit le regard (2026-10-09)
    // ------------------------------------------------------------------

    /// <summary>
    /// Tourne le buste pour que le fusil pointe là où le joueur regarde — vu par les AUTRES.
    ///
    /// Le défaut, rapporté par l'utilisateur : en visée, l'adversaire « ne regarde pas dans la bonne
    /// direction ». Mesuré : immobile en visée, le fusil pointait 36° à gauche du regard, et de -41°
    /// à +33° selon ce que jouaient les jambes. Le buste de la visée est greffé (masque) sur des
    /// hanches qui ne sont pas tournées comme dans son propre clip : ce que le clip compensait par
    /// une torsion du buste tombe à côté. Aucun calage fixe ne tient, l'écart dépend des jambes.
    ///
    /// On MESURE donc, à chaque image, sur la pose que l'Animator vient d'écrire : en visée (et au
    /// tir), l'écart entre le fusil et le regard, en cap et en tangage ; arme basse, l'écart entre
    /// le torse et le regard, en cap seulement (le fusil y est tenu en travers, et le joueur qui
    /// court de côté garde le torse face à son regard). La correction est lissée, ce qui laisse au
    /// fusil un peu du balancement des pas, et répartie sur les trois os de la colonne, pour une
    /// torsion qui ne casse pas la taille. Elle se fige pendant le rechargement et le taser, dont le
    /// geste doit se voir tel qu'il est authoré.
    ///
    /// Le tangage vient du réseau (PlayerLocomotion.DisplayAimPitch). Catégorie C stricte : le tir
    /// part toujours de la caméra, le hitbox se calcule toujours sans les os. Appliqué AVANT le
    /// lean, qui incline ensuite le buste ainsi tourné.
    /// </summary>
    private void ApplyAimDirection(PlayerLocomotion.Stance stance, float upperWeight, float dt)
    {
        if (sight == null || spineChain == null || spineChain.Length == 0) return;

        bool gesture = (weapon != null && weapon.IsReloading)
                       || (animator.layerCount > TaserLayer && animator.GetLayerWeight(TaserLayer) > 0.01f);
        if (!gesture)
        {
            bool aimPose = locomotion.DisplayAiming || UpperBodyPlays(TirStateId);
            torsoAimWeight = Mathf.MoveTowards(torsoAimWeight, aimPose ? 1f : 0f, torsoAimBlendSpeed * dt);

            // Mesuré dans le repère du joueur, sur la pose de cette image.
            Vector3 rifle = transform.InverseTransformDirection(sight.forward);
            float rifleYaw = Mathf.Atan2(rifle.x, rifle.z) * Mathf.Rad2Deg;
            float rifleElevation = Mathf.Asin(Mathf.Clamp(rifle.y, -1f, 1f)) * Mathf.Rad2Deg;

            // Le torse, c'est le HAUT de la colonne : c'est lui qui reçoit la rotation entière, comme
            // le fusil. Allongé, il regarde le sol : son cap ne veut rien dire, on ne le redresse pas.
            float chestYaw = 0f;
            if (stance != PlayerLocomotion.Stance.Prone)
            {
                Vector3 c = transform.InverseTransformDirection(spineChain[spineChain.Length - 1].forward);
                chestYaw = Mathf.Atan2(c.x, c.z) * Mathf.Rad2Deg;
            }

            TorsoCorrection(rifleYaw, rifleElevation, chestYaw, locomotion.DisplayAimPitch, torsoAimWeight,
                            maxTorsoYaw, stance == PlayerLocomotion.Stance.Prone ? maxProneTorsoPitch : maxTorsoPitch,
                            out float targetYaw, out float targetPitch);

            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, torsoAlignSmoothing));
            torsoYaw = Mathf.Lerp(torsoYaw, targetYaw, k);
            torsoPitch = Mathf.Lerp(torsoPitch, targetPitch, k);
        }

        // S'efface avec le haut du corps, pendant les transitions vers ou depuis l'allongé.
        if (upperWeight <= 0.001f) return;
        RotateSpine(spineChain, transform.right, torsoYaw * upperWeight, torsoPitch * upperWeight);
    }

    /// <summary>
    /// La correction à appliquer au buste, en degrés : un cap (+ = vers la droite) et une élévation
    /// (+ = vers le haut). En visée (aimWeight 1), elle amène le fusil sur le regard ; arme basse
    /// (0), elle amène seulement le torse face au regard, sans tangage. Fonction pure, testée.
    /// </summary>
    /// <param name="aimPitch">Tangage du regard, convention de la caméra (+ = vers le bas).</param>
    public static void TorsoCorrection(float rifleYaw, float rifleElevation, float chestYaw, float aimPitch,
                                       float aimWeight, float maxYaw, float maxPitch,
                                       out float yaw, out float elevation)
    {
        yaw = Mathf.Clamp(Mathf.Lerp(-chestYaw, -rifleYaw, aimWeight), -maxYaw, maxYaw);
        elevation = Mathf.Clamp(aimWeight * (-aimPitch - rifleElevation), -maxPitch, maxPitch);
    }

    /// <summary>Tourne le buste d'un cap (autour de la verticale) puis d'une élévation (autour de
    /// l'axe droit du joueur), réparti à parts égales sur les os de la colonne : chaque os en prend
    /// sa part, et le haut du buste — donc les bras et le fusil — la rotation entière.</summary>
    public static void RotateSpine(Transform[] spine, Vector3 playerRight, float yaw, float elevation)
    {
        // Une rotation positive autour de l'axe droit ABAISSE l'avant : d'où le signe.
        Quaternion correction = Quaternion.AngleAxis(-elevation, playerRight) * Quaternion.AngleAxis(yaw, Vector3.up);
        Quaternion share = Quaternion.Slerp(Quaternion.identity, correction, 1f / spine.Length);
        foreach (Transform bone in spine) bone.rotation = share * bone.rotation;
    }

    /// <summary>
    /// Les couches d'un spectateur (2026-10-13). Celle des gestes ne pèse que le temps d'un geste (ou de
    /// l'élan d'un lancer, que seul le joueur voit) ; celle de tout le corps, le temps de ramasser, ou
    /// selon le fondu de l'étourdissement (SpectatorController). Chez un duelliste, toutes deux restent
    /// à 0. Catégorie C : rien ne lit ces os.
    /// </summary>
    private void UpdateSpectatorLayers(bool spectating, float dt)
    {
        if (animator.layerCount <= FullBodyLayer) return;
        bool charging = spectating && spectator != null && spectator.IsCharging;
        animator.SetBool(ElanId, charging);
        animator.SetFloat(ElanLancerId, charging ? spectator.ChargeProgress : 0f);

        bool busy = false;
        if (spectating)
        {
            // Le poids retombe dès que la couche repart vers le repos : l'état vide n'a pas de pose à
            // montrer, on ne le mêle jamais à poids plein (comme la couche du taser).
            AnimatorStateInfo gesture = animator.GetCurrentAnimatorStateInfo(SpectatorGesturesLayer);
            bool inTransition = animator.IsInTransition(SpectatorGesturesLayer);
            bool toRest = inTransition && animator.GetNextAnimatorStateInfo(SpectatorGesturesLayer).shortNameHash == ReposStateId;
            busy = charging || (!toRest && (gesture.shortNameHash != ReposStateId || inTransition));
        }
        gestureWeight = Mathf.MoveTowards(gestureWeight, busy ? 1f : 0f, dt / Mathf.Max(0.01f, gestureBlendTime));
        animator.SetLayerWeight(SpectatorGesturesLayer, gestureWeight);

        bool pickingUp = false;
        if (spectating)
        {
            AnimatorStateInfo body = animator.GetCurrentAnimatorStateInfo(FullBodyLayer);
            bool inTransition = animator.IsInTransition(FullBodyLayer);
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(FullBodyLayer);
            pickingUp = inTransition ? next.shortNameHash == RamasserStateId : body.shortNameHash == RamasserStateId;
        }
        pickUpWeight = Mathf.MoveTowards(pickUpWeight, pickingUp ? 1f : 0f, dt / Mathf.Max(0.01f, gestureBlendTime));
        float stun = spectating && spectator != null ? spectator.StunVisualWeight : 0f;
        animator.SetLayerWeight(FullBodyLayer, Mathf.Max(stun, pickUpWeight));
    }

    /// <summary>Les modèles et finitions choisis par le joueur (PlayerWeaponLooks, 2026-10-15) : en main
    /// du corps, avec ses ombres (une ombre seule chez le propriétaire, posée par PlayerLocomotion à
    /// l'apparition, avant que l'apparence ne soit publiée).</summary>
    private void HandleWeaponLooks(WeaponLooks looks) => BuildHeldWeapons(looks, PublishedAttachments);

    /// <summary>Les accessoires choisis par le joueur (PlayerWeaponAttachments, 2026-10-16), montés sur
    /// les armes du corps.</summary>
    private void HandleWeaponAttachments(WeaponAttachments attachments) => BuildHeldWeapons(PublishedLooks, attachments);

    private WeaponLooks PublishedLooks => weaponLooks != null && weaponLooks.Published ? weaponLooks.Looks : WeaponLooks.Default;
    private WeaponAttachments PublishedAttachments
        => weaponAttachments != null && weaponAttachments.Published ? weaponAttachments.Attachments : WeaponAttachments.None;

    private void BuildHeldWeapons(WeaponLooks looks, WeaponAttachments attachments)
    {
        if (weapon == null || rightHand == null) return;
        Renderer body = animator != null ? animator.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        HeldWeapons.Build(rightHand, weapon.Loadout, looks, attachments, body);
        ShowHeldWeapon();
    }

    /// <summary>Montre en main le modèle de l'arme en main, et suit sa mire (2026-10-11). Un spectateur
    /// n'en montre aucune (2026-10-13).</summary>
    private void ShowHeldWeapon()
    {
        bool spectating = locomotion != null && locomotion.IsSpectator;
        shownWeapon = weapon != null && !spectating ? weapon.Data : null;
        WeaponModel model = weapon != null ? HeldWeapons.Show(rightHand, weapon.Loadout, shownWeapon) : null;
        sight = FindSight(animator);
        // Un pistolet se tient à deux mains, bras tendus, et non crosse à l'épaule : le haut du corps
        // joue alors les poses de pistolet de Mixamo (2026-10-11). Le premier affichage ne fond pas.
        pistolHold = model != null && model.PistolHold ? 1f : 0f;
        if (!pistolHoldShown)
        {
            pistolBlend = pistolHold;
            pistolHoldShown = true;
        }
    }

    /// <summary>La mire de l'arme tenue en main droite (son +Z est l'axe du canon) : celle du viseur
    /// monté s'il y en a un (AttachmentMount.SightOf), ou null.</summary>
    public static Transform FindSight(Animator animator)
    {
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        WeaponModel model = HeldWeapons.Visible(hand);
        return model != null ? AttachmentMount.SightOf(model, out _) : null;
    }

    /// <summary>Les os de la colonne qui tournent pour suivre le regard, du bas vers le haut.</summary>
    public static Transform[] SpineChain(Animator animator)
    {
        var bones = new System.Collections.Generic.List<Transform>();
        foreach (HumanBodyBones b in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
        {
            Transform t = animator.GetBoneTransform(b);
            if (t != null) bones.Add(t);
        }
        return bones.ToArray();
    }

    /// <summary>La couche du haut du corps joue-t-elle cet état, ou y entre-t-elle ?</summary>
    private bool UpperBodyPlays(int stateHash)
    {
        if (animator.layerCount <= UpperBodyLayer) return false;
        if (animator.GetCurrentAnimatorStateInfo(UpperBodyLayer).shortNameHash == stateHash) return true;
        return animator.IsInTransition(UpperBodyLayer)
               && animator.GetNextAnimatorStateInfo(UpperBodyLayer).shortNameHash == stateHash;
    }

    /// <summary>Accorde la cadence d'une posture à ses clips. Mixamo n'authore pas ses animations
    /// à l'échelle des vitesses de ce jeu : le ramper est plus lent que la vitesse prone réelle,
    /// la course debout plus rapide que la marche. C'est un réglage de RESSENTI, à ajuster en
    /// jouant et non par calcul.</summary>
    private float PlaybackScale(PlayerLocomotion.Stance stance)
    {
        if (stance == PlayerLocomotion.Stance.Crouching) return crouchingPlaybackScale;
        if (stance == PlayerLocomotion.Stance.Prone) return pronePlaybackScale;
        return standingPlaybackScale;
    }
}
