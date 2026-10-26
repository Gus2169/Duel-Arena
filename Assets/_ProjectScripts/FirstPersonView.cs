using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// La vue subjective : les bras et l'arme que le joueur voit de ses propres yeux (2026-10-08).
///
/// Le corps du personnage ne peut pas servir à ça. Chez son propriétaire il est en ombre seule
/// (sa caméra est dans le crâne), et ses bras ne suivent pas le regard : en regardant en l'air,
/// l'arme sortirait de l'écran. On fait donc comme les FPS : chez le PROPRIÉTAIRE seulement, une
/// copie du personnage et de son fusil est accrochée à la caméra, sa tête et ses jambes repliées,
/// et elle rejoue les GESTES de l'arme du corps (PlayerAnimator.MirrorWeaponGestures) : tir,
/// rechargement, taser. Ni ses jambes ni sa posture (2026-10-10) : elles tournaient le buste sous
/// l'arme, qui balançait en pas chassé et passait devant l'œil en se relevant. La caméra porte déjà
/// le déplacement, la hauteur et le lean ; le pas, lui, se lit dans un léger balancement.
///
/// La tenue, elle, n'est pas celle du corps. Au repos, le soldat tient son fusil bas, en travers
/// des hanches, 55 cm sous l'œil : hors de l'écran. Les bras jouent donc TOUJOURS la pose de
/// visée, et l'arme est placée comme dans un FPS : en bas à droite sans viser, au centre en
/// visant, canon dans l'axe du réticule. L'alignement est MESURÉ chaque image sur la pose jouée,
/// puis lissé : les poses d'attente ne défilent pas (leur respiration faisait dériver la mire), le
/// lissage laisse passer les gestes rapides (le recul), et l'alignement se fige pendant le
/// rechargement, dont le geste doit se voir. Pendant le coup de taser, au contraire, il suit la pose
/// sans retard et l'arme s'abaisse : c'est le poing ganté qui doit se voir, pas l'arme qui bouge.
///
/// Elle vit sur son propre layer (Viewmodel), dessiné par une seconde caméra par-dessus le décor :
/// sans ça, le fusil s'enfoncerait dans les murs dès qu'on s'en approche.
///
/// Catégorie C pure : rien ne la lit, elle ne projette pas d'ombre (le corps s'en charge), et elle
/// n'existe sur aucune autre machine.
/// </summary>
[DisallowMultipleComponent]
public class FirstPersonView : NetworkBehaviour
{
    public const string LayerName = "Viewmodel";

    [Tooltip("La caméra du joueur (Main Camera du prefab), à laquelle les bras sont accrochés.")]
    [SerializeField] private Camera playerCamera;

    [Header("Tenue de l'arme (ressenti)")]
    [Tooltip("En visée : où tombe le centre de l'écran du viseur (repère « Mire » du fusil), dans le repère de la caméra (m). Sur l'axe, à la distance de l'œil : l'écran se regarde de face, la réticule au centre.")]
    [SerializeField] private Vector3 aimSightPosition = new Vector3(0f, 0f, 0.10f);

    [Tooltip("Sans viser : décalage de l'arme par rapport à la position de visée (m) — à droite, en bas.")]
    [SerializeField] private Vector3 hipOffset = new Vector3(0.12f, -0.18f, 0.27f);

    [Tooltip("Vitesse du passage hanche ↔ visée (par seconde).")]
    [SerializeField] private float aimTransitionSpeed = 8f;

    [Tooltip("Temps de lissage de l'alignement (s). Plus court, l'arme reste collée à l'axe mais le recul s'efface ; plus long, elle revient lentement après un geste.")]
    [SerializeField] private float holdSmoothing = 0.2f;

    [Tooltip("Champ de vision vertical des bras (degrés). Indépendant de celui du monde, comme dans la plupart des FPS : un champ très large étire et rapetisse l'arme.")]
    [SerializeField] private float armsFieldOfView = 60f;

    [Header("Balancement en marchant (ressenti)")]
    [Tooltip("Balancement de l'arme à la hanche, à l'allure de marche (m) : x sur le côté, y en hauteur. Les bras ne rejouent pas les jambes : c'est lui qui fait sentir le pas.")]
    [SerializeField] private Vector2 bobAmplitude = new Vector2(0.008f, 0.012f);

    [Tooltip("Allers-retours du balancement par seconde à l'allure de marche (un aller-retour = deux pas). Il ne suit la vitesse que de loin (puissance 0,2) : comme un vrai pas, il allonge sa foulée plus qu'il n'accélère. Avant le 2026-10-11, il était proportionnel à la distance parcourue, et l'arme partait de gauche à droite quatre fois par seconde en sprint.")]
    [SerializeField] private float bobFrequency = 1.4f;

    [Tooltip("Part du balancement gardée en visée (0 : arme immobile).")]
    [SerializeField, Range(0f, 1f)] private float aimBobScale = 0.1f;

    [Header("Sprint (ressenti, 2026-10-11)")]
    [Tooltip("En sprint, l'arme est portée en travers : décalage (m) dans le repère de la caméra, ajouté à la pose de hanche. Elle revient pendant la sortie de sprint (WeaponData.sprintToFireTime), le temps exact pendant lequel on ne peut pas tirer.")]
    [SerializeField] private Vector3 sprintOffset = new Vector3(-0.02f, 0.04f, -0.02f);

    [Tooltip("En sprint, l'arme pivote autour de sa mire (degrés, repère de la caméra) : x la baisse, y la tourne vers la gauche, z la couche. Plus de 10° vers le bas, elle sort de l'écran.")]
    [SerializeField] private Vector3 sprintEuler = new Vector3(6f, -22f, 28f);

    [Tooltip("Temps (s) pour passer en pose de sprint quand on se met à courir.")]
    [SerializeField] private float sprintPoseInTime = 0.15f;

    [Header("Changement d'arme (ressenti, 2026-10-11)")]
    [Tooltip("L'arme qu'on vient de prendre remonte de là (m, repère de la caméra) pendant qu'elle sort (WeaponData.raiseTime), le temps où l'on ne peut pas tirer.")]
    [SerializeField] private Vector3 raiseOffset = new Vector3(0.02f, -0.22f, -0.04f);

    [Tooltip("Et pivote de là autour de sa mire (degrés) : x la baisse, z la couche.")]
    [SerializeField] private Vector3 raiseEuler = new Vector3(35f, 0f, 15f);

    [Header("Coup de taser (ressenti, 2026-10-11)")]
    [Tooltip("Pendant le coup de taser, l'arme s'abaisse de là (m, repère de la caméra) pour laisser passer le poing ganté, comme l'arme pendant le couteau rapide de Call of Duty.")]
    [SerializeField] private Vector3 strikeOffset = new Vector3(0.03f, -0.05f, 0f);

    [Tooltip("Et pivote de là autour de sa mire (degrés) : x la baisse, z la couche vers la droite.")]
    [SerializeField] private Vector3 strikeEuler = new Vector3(10f, 0f, -8f);

    [Tooltip("Pendant le coup, le poing ganté part de l'épaule vers ce point (m, repère de la caméra). Le Taser Punch frappe à hauteur de poitrine, 40 cm sous l'œil : sans ce réalignement du bras, le poing passait sous l'écran. Vu par les autres, le coup reste celui du clip.")]
    [SerializeField] private Vector3 strikeFistTarget = new Vector3(-0.06f, -0.10f, 0.6f);

    [Header("Tenue de pistolet (raygun, 2026-10-11)")]
    [Tooltip("Sans viser, le pistolet est décalé de là (m) par rapport à sa position de visée, à la place de hipOffset : à droite, en bas, sans l'avancer. Les bras jouent la pose de pistolet, tendus : avancer l'arme comme un fusil ferait entrer les épaules dans l'écran.")]
    [SerializeField] private Vector3 pistolHipOffset = new Vector3(0.10f, -0.10f, -0.10f);

    [Header("Lunette plein écran (sniper, façon Call of Duty, 2026-10-11)")]
    [Tooltip("Part de l'épaulement à partir de laquelle on est dans la lunette : la vue zoome, l'arme disparaît, la lunette couvre l'écran (CrosshairUI).")]
    [SerializeField, Range(0.5f, 1f)] private float scopeInThreshold = 0.95f;

    private GameObject view;
    private Animator viewAnimator;
    private Transform sight;
    private Camera armsCamera;
    private WeaponSight viewSight;
    private PlayerLocomotion locomotion;
    private float aimBlend;
    private Vector3 lastPosition;
    private bool hasLastPosition;
    private float bobPhase, bobWeight;
    private WeaponController weaponController;
    private float sprintPose;
    private WeaponData shownWeapon;   // l'arme dont le modèle est en main sur la copie
    private WeaponModel shownModel;   // … et ce modèle
    private WeaponAttachment shownSight; // … et le viseur qui y est monté
    private PlayerWeaponLooks weaponLooks;
    private PlayerChassis chassis;
    private PlayerWeaponAttachments weaponAttachments;
    private MaterialPropertyBlock lensBlock;
    private static readonly int AxeCameraId = Shader.PropertyToID("_AxeCamera");
    private Transform viewRightHand;
    private Transform viewLeftUpperArm, viewFist;
    private WeaponGrip viewGrip;      // les mains des bras sur l'arme (2026-10-17)
    private PlayerAnimator body;      // ses réglages, ceux du corps
    private float aimEyeDistance = 0.10f;
    private bool pistolHold; // WeaponModel.PistolHold : les bras jouent la pose de pistolet (PlayerAnimator)

    // Tenue lissée : la rotation qui met le canon dans l'axe du regard, et la mire, dans le
    // repère de la copie.
    private bool holdInitialized;
    private Quaternion holdRotation = Quaternion.identity;
    private Vector3 holdPivot;
    private int taserLayer = -1;
    private static readonly int ReloadingId = Animator.StringToHash("Reloading");

    /// <summary>L'Animator des bras, auquel PlayerAnimator recopie ses paramètres. Null hors du
    /// propriétaire.</summary>
    public Animator ViewAnimator => viewAnimator;

    /// <summary>L'œil est dans l'axe du viseur : c'est sa réticule qui marque le centre, le
    /// réticule du HUD s'efface (CrosshairUI). Seulement pour une arme à viseur : un viseur monté
    /// (2026-10-16, AttachmentMount) ou l'écran numérique de l'ancien fusil d'assaut ; une mire
    /// métallique n'a pas de réticule, le HUD garde la sienne.</summary>
    public bool SightOnAxis => view != null && !hidden && !spectatorMode && aimBlend > 0.9f && shownModel != null
                               && (shownSight != null || (viewSight != null && viewSight.gameObject == shownModel.gameObject));

    /// <summary>Dans la lunette plein écran du sniper (2026-10-11) : la vue zoome du grossissement de
    /// l'arme, l'arme disparaît, la lunette couvre l'écran (CrosshairUI). Comme dans Call of Duty,
    /// seulement une fois l'arme épaulée.</summary>
    public bool Scoped => view != null && !hidden && !spectatorMode && shownWeapon != null && shownWeapon.scopeMagnification > 1f
                          && aimBlend >= scopeInThreshold && weaponController != null && !weaponController.IsReloading;

    /// <summary>Grossissement de la vue : celui de la lunette quand on y est, 1 sinon.</summary>
    public float ScopeZoom => Scoped ? shownWeapon.scopeMagnification : 1f;

    /// <summary>Sensibilité de la souris : réduite dans la lunette comme le champ de vision, pour que
    /// le réticule se déplace à l'écran comme sans zoom.</summary>
    public float LookScale => 1f / ScopeZoom;

    /// <summary>Le réticule du HUD s'affiche-t-il à la hanche avec l'arme en main ? (pas au sniper)</summary>
    public bool HipCrosshair => shownWeapon == null || shownWeapon.hipCrosshair;

    /// <summary>L'arme des bras, quand on les voit (ni mort, ni spectateur, ni dans la lunette) : la
    /// flamme de bouche part de sa bouche (WeaponController), le laser de son émetteur (TacticalLaser).</summary>
    public WeaponModel ViewWeapon => view != null && !hidden && !spectatorMode && !Scoped ? shownModel : null;

    // Masqués quand le joueur est mort (PlayerDeath) : sa caméra quitte ses yeux pour regarder son
    // épave. La copie continue d'être animée (ses paramètres viennent de PlayerAnimator), seule sa
    // caméra s'éteint : les bras ne vivent que sur son layer.
    private bool hidden;

    // Un spectateur n'a pas d'arme (2026-10-13) : la copie armée s'éteint, mais la caméra des bras
    // reste, pour la vue subjective du spectateur (SpectatorView : ses poings, ce qu'il tient).
    private bool spectatorMode;

    /// <summary>Masque (ou rend) les bras et l'arme de la vue subjective, et la lunette.</summary>
    public void SetHidden(bool value)
    {
        hidden = value;
        if (armsCamera != null) armsCamera.enabled = !value && !Scoped;
        if (value && viewSight != null) viewSight.UpdateScreen(playerCamera, armsCamera, 0f);
    }

    /// <summary>La caméra des bras (layer Viewmodel), que la vue subjective du spectateur partage.</summary>
    public Camera ArmsCamera => armsCamera;

    /// <summary>La caméra du joueur, à laquelle les bras sont accrochés.</summary>
    public Camera PlayerCamera => playerCamera;

    /// <summary>Le joueur est-il en mode spectateur (copie armée éteinte) ?</summary>
    public bool SpectatorMode => spectatorMode;

    private void ApplySpectatorMode(bool value)
    {
        spectatorMode = value;
        if (view != null && view.activeSelf == value) view.SetActive(!value);
        if (value)
        {
            aimBlend = 0f;
            if (viewSight != null) viewSight.UpdateScreen(playerCamera, armsCamera, 0f);
            if (armsCamera != null) armsCamera.enabled = !hidden;
        }
        holdInitialized = false;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        locomotion = GetComponent<PlayerLocomotion>();
        weaponController = GetComponentInChildren<WeaponController>();
        Build();

        // Le châssis du robot (2026-10-26) : la copie a pris celui que montrait le corps ; elle suit le publié.
        chassis = GetComponent<PlayerChassis>();
        if (chassis != null)
        {
            chassis.Changed += HandleChassis;
            HandleChassis(chassis.Value);
        }

        // Les modèles et finitions choisis par le joueur (2026-10-15) : la copie a pris les armes du
        // corps telles qu'elles étaient, les modèles par défaut tant que le serveur n'a rien publié.
        weaponLooks = GetComponent<PlayerWeaponLooks>();
        if (weaponLooks != null)
        {
            weaponLooks.Changed += HandleWeaponLooks;
            if (weaponLooks.Published) HandleWeaponLooks(weaponLooks.Looks);
        }
        // Et les accessoires (2026-10-16), publiés de même.
        weaponAttachments = GetComponent<PlayerWeaponAttachments>();
        if (weaponAttachments != null)
        {
            weaponAttachments.Changed += HandleWeaponAttachments;
            if (weaponAttachments.Published) HandleWeaponAttachments(weaponAttachments.Attachments);
        }
    }

    private void HandleChassis(ChassisId id)
    {
        if (view != null) RobotChassis.Apply(view.transform, id);
    }

    private void HandleWeaponLooks(WeaponLooks looks)
        => BuildWeapons(looks, weaponAttachments != null && weaponAttachments.Published ? weaponAttachments.Attachments : WeaponAttachments.None);

    private void HandleWeaponAttachments(WeaponAttachments attachments)
        => BuildWeapons(weaponLooks != null && weaponLooks.Published ? weaponLooks.Looks : WeaponLooks.Default, attachments);

    private void BuildWeapons(WeaponLooks looks, WeaponAttachments attachments)
    {
        if (view == null || viewRightHand == null || weaponController == null) return;
        // les nouvelles pièces prennent les réglages de la copie : ni ombre portée, ni ombre reçue
        Renderer like = view.GetComponentInChildren<SkinnedMeshRenderer>(true);
        HeldWeapons.Build(viewRightHand, weaponController.Loadout, looks, attachments, like);
        AimLensesAtCamera();
        ShowWeapon(weaponController.Data);
    }

    /// <summary>Sur les bras, le réticule des viseurs marque le centre de l'écran, d'où part le tir
    /// (DuelArena/ViseurReticule, _AxeCamera) : l'arme balance au rythme des pas, pas le réticule.</summary>
    private void AimLensesAtCamera()
    {
        lensBlock ??= new MaterialPropertyBlock();
        foreach (WeaponAttachment attachment in viewRightHand.GetComponentsInChildren<WeaponAttachment>(true))
        {
            if (attachment.Lens == null) continue;
            attachment.Lens.GetPropertyBlock(lensBlock);
            lensBlock.SetFloat(AxeCameraId, 1f);
            attachment.Lens.SetPropertyBlock(lensBlock);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (chassis != null) chassis.Changed -= HandleChassis;
        if (weaponLooks != null) weaponLooks.Changed -= HandleWeaponLooks;
        if (weaponAttachments != null) weaponAttachments.Changed -= HandleWeaponAttachments;
        if (view != null) Destroy(view);
        if (armsCamera != null) Destroy(armsCamera.gameObject);
        view = null;
        viewAnimator = null;
    }

    private void Build()
    {
        Transform model = transform.Find("Model");
        if (model == null || playerCamera == null)
        {
            Debug.LogError("FirstPersonView : il faut l'enfant « Model » et la caméra du joueur.", this);
            return;
        }

        int layer = LayerMask.NameToLayer(LayerName);
        if (layer < 0)
        {
            Debug.LogError($"FirstPersonView : le layer « {LayerName} » n'existe pas (Project Settings > Tags and Layers).", this);
            return;
        }

        view = BuildView(model.gameObject, playerCamera.transform, layer, out viewAnimator, out sight);
        if (sight == null)
        {
            Debug.LogError("FirstPersonView : l'arme en main n'a pas de repère « Mire ».", this);
            return;
        }
        viewRightHand = viewAnimator.GetBoneTransform(HumanBodyBones.RightHand);

        taserLayer = viewAnimator.GetLayerIndex("Taser");
        HeldWeapons.WearTaserGlove(viewAnimator, false);
        viewLeftUpperArm = viewAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform glove = HeldWeapons.TaserGlove(viewAnimator);
        viewFist = glove != null ? glove.Find("Poing") : null;
        viewGrip = new WeaponGrip(viewAnimator);
        body = GetComponent<PlayerAnimator>();

        // La peinture du joueur sur ses propres bras.
        PlayerPaint paint = GetComponent<PlayerPaint>();
        if (paint != null)
        {
            foreach (Renderer original in paint.PaintedRenderers)
            {
                if (original == null) continue;
                Transform twin = view.transform.Find(RelativePath(original.transform, model));
                if (twin != null && twin.TryGetComponent(out Renderer r)) paint.AddRenderer(r);
            }
        }

        armsCamera = BuildArmsCamera(playerCamera, layer, armsFieldOfView);
    }

    private void LateUpdate()
    {
        if (view == null) return;
        // Avant de mesurer la mire : la main droite d'une crosse droite a déplacé l'arme à l'image d'avant
        // (WeaponGrip), et l'Animator des bras a remis la main dans sa pose animée.
        viewGrip?.RestoreWeapon();
        bool spectating = locomotion != null && locomotion.IsSpectator;
        if (spectating != spectatorMode) ApplySpectatorMode(spectating);
        if (hidden || spectatorMode) return;
        if (weaponController != null && weaponController.Data != shownWeapon) ShowWeapon(weaponController.Data);
        if (sight == null) return;

        bool aiming = locomotion != null && locomotion.DisplayAiming;
        // L'épaulement de l'arme, poignée inclinée comprise (WeaponController.AimTime, GDD § 6).
        float aimTime = weaponController != null ? weaponController.AimTime : shownWeapon != null ? shownWeapon.aimTime : 0f;
        float aimSpeed = aimTime > 0.01f ? 1f / aimTime : aimTransitionSpeed;
        aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, aimSpeed * Time.deltaTime);

        // La pose de l'image, dans le repère de la copie (indépendant de l'endroit où elle est posée).
        Transform root = view.transform;
        Vector3 pivot = root.InverseTransformPoint(sight.position);
        Quaternion align = LevelSight(root.InverseTransformDirection(sight.forward), root.InverseTransformDirection(sight.up));

        float strikeWeight = taserLayer >= 0 ? viewAnimator.GetLayerWeight(taserLayer) : 0f;
        bool striking = strikeWeight > 0.01f;
        // Le gant n'apparaît que pendant le coup de taser (voir HeldWeapons.WearTaserGlove).
        HeldWeapons.WearTaserGlove(viewAnimator, striking);
        if (!holdInitialized || striking)
        {
            // Pendant le coup, l'arme reste calée sur le regard, sans retard : le coup penche le buste
            // en avant (Taser Punch), et une arme figée sur la pose d'avant montait devant l'œil.
            // C'est le bras gauche qui bouge, par rapport à elle.
            holdRotation = align;
            holdPivot = pivot;
            holdInitialized = true;
        }
        else if (!viewAnimator.GetBool(ReloadingId))
        {
            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, holdSmoothing));
            holdRotation = Quaternion.Slerp(holdRotation, align, k);
            holdPivot = Vector3.Lerp(holdPivot, pivot, k);
        }

        Vector3 offsets = (pistolHold ? pistolHipOffset : hipOffset) * (1f - Mathf.SmoothStep(0f, 1f, aimBlend)) + WalkBob();

        // En sprint, l'arme est baissée ; elle remonte pendant toute la sortie de sprint, le temps
        // où l'on ne peut pas encore tirer (WeaponController.SprintRecovery). Elle se baisse vite
        // quand on se met à courir.
        float wantedPose = weaponController != null ? 1f - Mathf.SmoothStep(0f, 1f, weaponController.SprintRecovery) : 0f;
        sprintPose = wantedPose > sprintPose
            ? Mathf.MoveTowards(sprintPose, wantedPose, Time.deltaTime / Mathf.Max(0.01f, sprintPoseInTime))
            : wantedPose;
        offsets += sprintOffset * sprintPose;

        // L'arme qu'on vient de prendre remonte pendant qu'elle sort (WeaponController.RaiseProgress).
        float lowered = weaponController != null ? 1f - Mathf.SmoothStep(0f, 1f, weaponController.RaiseProgress) : 0f;
        offsets += raiseOffset * lowered;
        // Pendant le coup de taser, l'arme s'abaisse pour laisser passer le poing.
        float dip = Mathf.SmoothStep(0f, 1f, strikeWeight);
        offsets += strikeOffset * dip;
        Quaternion extra = Quaternion.Euler(sprintEuler * sprintPose + raiseEuler * lowered + strikeEuler * dip);
        Place(root, extra * holdRotation, holdPivot, new Vector3(aimSightPosition.x, aimSightPosition.y, aimEyeDistance) + offsets);
        // Les mains sur l'arme, une fois l'arme placée (sa mire a été mesurée sans elles) : la main gauche
        // la lâche pour le rechargement et pour le coup de taser, que AimStrike oriente ensuite ; la droite
        // lâche le poignet d'une crosse droite en visée.
        if (viewGrip != null && body != null)
            viewGrip.Apply(shownModel, shownWeapon != null ? shownWeapon.attachmentCatalog : null,
                           !viewAnimator.GetBool(ReloadingId), 1f - strikeWeight, body.HandGrip, Time.deltaTime,
                           StraightStockHold(aimBlend));
        if (striking && viewLeftUpperArm != null && viewFist != null)
            AimStrike(viewLeftUpperArm, viewFist, playerCamera.transform.TransformPoint(strikeFistTarget), dip);

        // Dans la lunette, l'arme disparaît : la lunette dessinée par le HUD prend tout l'écran.
        if (armsCamera != null)
        {
            armsCamera.fieldOfView = armsFieldOfView;
            armsCamera.enabled = !Scoped;
        }
        bool screenWeapon = viewSight != null && viewSight.gameObject.activeInHierarchy;
        if (viewSight != null) viewSight.UpdateScreen(playerCamera, armsCamera, screenWeapon ? aimBlend : 0f);
    }

    /// <summary>
    /// Le poids de la main droite sur le poignet d'une crosse droite (FP-30, FAP-30 ; WeaponGrip), selon
    /// l'épaulement : 1 à la hanche, 0 en visée, suivant la courbe du décalage de hanche. En visée, ce
    /// poignet est à 7 cm devant l'œil et sous lui : la main qui le tenait et son avant-bras couvraient
    /// 62 % du tiers droit de l'écran au FAP-30 (mesuré le 2026-10-17). Laissée à sa pose de clip, la main
    /// est sous l'arme, à 78° sous l'axe du regard : hors de l'image. Sans état, testée.
    /// </summary>
    public static float StraightStockHold(float aimBlend) => 1f - Mathf.SmoothStep(0f, 1f, aimBlend);

    /// <summary>Pendant le coup de taser : le bras tourne à l'épaule pour que le poing parte vers
    /// `target` (monde), à la distance de l'épaule que lui donne le clip (élan, frappe, retour). Sans
    /// état, testée.</summary>
    public static void AimStrike(Transform upperArm, Transform fist, Vector3 target, float weight)
    {
        Vector3 shoulder = upperArm.position;
        Vector3 current = fist.position - shoulder;
        Vector3 wanted = target - shoulder;
        if (current.sqrMagnitude < 1e-6f || wanted.sqrMagnitude < 1e-6f) return;
        Quaternion swing = Quaternion.FromToRotation(current, wanted);
        upperArm.rotation = Quaternion.Slerp(Quaternion.identity, swing, weight) * upperArm.rotation;
    }

    /// <summary>Montre sur les bras le modèle de l'arme en main, et reprend sa mire et sa distance de
    /// visée (2026-10-11). L'alignement repart de la pose de cette arme : avec un pistolet, les bras
    /// jouent la pose de pistolet (PlayerAnimator), tendus, à deux mains.</summary>
    private void ShowWeapon(WeaponData data)
    {
        shownWeapon = data;
        WeaponModel model = data != null && weaponController != null
            ? HeldWeapons.Show(viewRightHand, weaponController.Loadout, data) : null;
        shownModel = model;
        shownSight = model != null ? AttachmentMount.Mounted(model.transform, AttachmentCategory.Sight) : null;
        // la mire du viseur monté s'il y en a un, et sa distance à l'œil (AttachmentMount.SightOf)
        Transform modelSight = model != null ? AttachmentMount.SightOf(model, out aimEyeDistance) : null;
        sight = modelSight != null ? modelSight : PlayerAnimator.FindSight(viewAnimator);
        if (model == null) aimEyeDistance = aimSightPosition.z;
        pistolHold = model != null && model.PistolHold;
        holdInitialized = false;

        // L'écran d'un viseur numérique, sur la copie seulement : l'arme vue par les autres garde le
        // sien. Celui de l'arme qu'on range s'éteint.
        WeaponSight next = model != null ? model.GetComponent<WeaponSight>() : null;
        if (next != viewSight)
        {
            if (viewSight != null) viewSight.UpdateScreen(playerCamera, armsCamera, 0f);
            viewSight = next;
            if (viewSight != null) viewSight.EnableDigitalScreen(playerCamera);
        }
    }

    /// <summary>Le balancement de l'arme au rythme du pas, dans le repère de la caméra : un huit
    /// couché, deux creux par aller-retour (un par pas), proportionnel à l'allure.</summary>
    private Vector3 WalkBob()
    {
        Vector3 position = transform.position;
        Vector3 delta = hasLastPosition ? position - lastPosition : Vector3.zero;
        delta.y = 0f;
        lastPosition = position;
        hasLastPosition = true;
        float distance = delta.magnitude;
        if (distance > 1f) distance = 0f; // téléportation (manche, recalage), pas un pas

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        float reference = locomotion != null ? Mathf.Max(0.5f, locomotion.NominalSpeed(PlayerLocomotion.Stance.Standing)) : 5f;
        float ratio = Mathf.Clamp(distance / dt / reference, 0f, 2f);
        bobWeight = Mathf.Lerp(bobWeight, Mathf.Min(ratio, 1.5f), 1f - Mathf.Exp(-dt / 0.15f));
        float frequency = bobFrequency * Mathf.Pow(Mathf.Max(ratio, 0.05f), 0.2f);
        bobPhase = Mathf.Repeat(bobPhase + frequency * 2f * Mathf.PI * dt, 2f * Mathf.PI);

        float scale = bobWeight * Mathf.Lerp(1f, aimBobScale, aimBlend);
        return new Vector3(bobAmplitude.x * Mathf.Sin(bobPhase), bobAmplitude.y * 0.5f * (Mathf.Cos(2f * bobPhase) - 1f), 0f) * scale;
    }

    /// <summary>
    /// La rotation qui met l'axe du viseur dans l'axe du regard ET le viseur d'aplomb, son haut
    /// aussi près que possible du haut de l'écran. Aligner l'axe seul laissait le roulis de la pose
    /// jouée : l'écran du viseur penchait de 2 à 3°. Sans état, testée.
    /// </summary>
    /// <param name="sightForward">Axe du viseur, dans le repère de la copie.</param>
    /// <param name="sightUp">Haut du viseur, dans le repère de la copie.</param>
    public static Quaternion LevelSight(Vector3 sightForward, Vector3 sightUp)
    {
        return Quaternion.Inverse(Quaternion.LookRotation(sightForward, sightUp));
    }

    /// <summary>
    /// Copie le personnage sous la caméra et le prépare : layer des bras, aucune ombre, tête et
    /// jambes repliées. La copie garde son Animator et son contrôleur. Sans état, testée.
    /// </summary>
    public static GameObject BuildView(GameObject modelSource, Transform cameraTransform, int layer,
                                       out Animator animator, out Transform sight)
    {
        GameObject copy = Instantiate(modelSource, cameraTransform, false);
        copy.name = "VueSubjective";
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.transform.localScale = Vector3.one;

        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true; // la caméra est dans le crâne
        }

        animator = copy.GetComponent<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // Replier la tête (on est dedans) et les jambes (inutiles, et visibles en baissant les yeux
        // pendant certaines animations). L'Animator d'un humanoïde n'écrit pas l'échelle des os :
        // il suffit de la poser une fois.
        Collapse(animator.GetBoneTransform(HumanBodyBones.Head));
        Collapse(animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg));
        Collapse(animator.GetBoneTransform(HumanBodyBones.RightUpperLeg));

        sight = PlayerAnimator.FindSight(animator);
        return copy;
    }

    /// <summary>
    /// Place la copie (enfant de la caméra) : tournée de `rotation` autour de la mire, puis
    /// déplacée pour que la mire tombe sur `target`, dans le repère de la caméra. Sans état, testée.
    /// </summary>
    public static void Place(Transform viewRoot, Quaternion rotation, Vector3 pivot, Vector3 target)
    {
        // `pivot` est la mire dans le repère de la copie : un point p de la copie va en
        // rotation * (p - pivot) + target, dans le repère de la caméra.
        viewRoot.localRotation = rotation;
        viewRoot.localPosition = target - rotation * pivot;
    }

    private static string RelativePath(Transform t, Transform root)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    private static void Collapse(Transform bone)
    {
        if (bone != null) bone.localScale = Vector3.zero;
    }

    /// <summary>La caméra des bras : une caméra Overlay qui ne dessine que leur layer, par-dessus
    /// le monde (profondeur effacée), empilée sur la caméra du joueur.</summary>
    private static Camera BuildArmsCamera(Camera baseCamera, int layer, float fieldOfView)
    {
        baseCamera.cullingMask &= ~(1 << layer);

        var go = new GameObject("CameraBras");
        go.transform.SetParent(baseCamera.transform, false);
        var cam = go.AddComponent<Camera>();
        cam.cullingMask = 1 << layer;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 5f;
        cam.fieldOfView = fieldOfView;

        var data = cam.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Overlay;
        data.renderShadows = false;
        baseCamera.GetUniversalAdditionalCameraData().cameraStack.Add(cam);
        return cam;
    }
}
