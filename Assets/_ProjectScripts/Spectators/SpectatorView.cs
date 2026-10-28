using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// La vue subjective d'un spectateur (2026-10-13) : ses poings et ce qu'il tient, vus de ses propres
/// yeux. La vue armée d'un duelliste (FirstPersonView) s'éteint sur la tribune ; celle-ci prend le
/// relais, sur le même layer et avec la même caméra des bras.
///
/// - Une copie du robot, à mains nues (ni arme, ni gant), accrochée à la caméra, sa tête (repliée) juste
///   derrière et sous l'œil, là où seraient ses yeux : au repos, ses bras sont sous l'écran et son buste
///   derrière la caméra. Les coups de poing y jouent les directs du corps (couche des gestes d'un
///   spectateur, 2026-10-13 ; le droit depuis le 2026-10-27, l'enchaînement), bras réorienté vers le
///   centre de l'écran, comme le taser d'un duelliste. La garde y lève les deux poings en bas de
///   l'écran, et encaisse quand elle arrête un coup.
/// - L'objet en main, en bas à droite : il recule pendant l'élan, part devant au lancer. Rangé (la
///   molette), il ne se voit pas.
///
/// Catégorie C pure, chez le propriétaire seulement : rien ne la lit.
/// </summary>
[DisallowMultipleComponent]
public class SpectatorView : NetworkBehaviour
{
    [Header("Coup de poing (ressenti)")]
    [Tooltip("Où part le poing gauche, dans le repère de la caméra (m) : un peu à gauche et sous le centre de l'écran. Le droit part à l'opposé, un peu à droite.")]
    [SerializeField] private Vector3 fistTarget = new Vector3(-0.07f, -0.11f, 0.55f);

    [Header("Placement de la copie (ressenti)")]
    [Tooltip("Où se tient la copie, par rapport à sa tête mise dans l'œil (m, repère de la caméra) : en arrière et plus bas, pour que le buste d'un châssis massif reste hors de l'écran. Tête dans l'œil, la caméra était dans le buste du CT-01 (vu le 2026-10-27 : un spectateur ne voyait plus rien).")]
    [SerializeField] private Vector3 viewOffset = new Vector3(0f, -0.08f, -0.10f);

    [Header("Garde (ressenti)")]
    [Tooltip("Temps (s) pour lever ou baisser la garde à l'écran.")]
    [SerializeField] private float guardBlendTime = 0.1f;
    [Tooltip("En garde, la copie se tient là au lieu de viewOffset (m, repère de la caméra) : les poings tiennent le bas de l'écran, en V, sans boucher le centre (7 à 12 % de l'écran au CT-01, mesuré le 2026-10-27).")]
    [SerializeField] private Vector3 guardViewOffset = new Vector3(0f, -0.15f, 0.03f);

    [Header("Objet tenu (ressenti)")]
    [Tooltip("Où se tient l'objet, dans le repère de la caméra (m) : en bas à droite.")]
    [SerializeField] private Vector3 holdOffset = new Vector3(0.24f, -0.21f, 0.45f);
    [SerializeField] private Vector3 holdEuler = new Vector3(8f, -25f, 4f);
    [Tooltip("Pendant la visée d'un lancer, l'objet recule d'autant, le bras armé derrière l'épaule (m, repère de la caméra) : il sort de l'écran et ne cache rien de la trajectoire qu'on vise (2026-10-27).")]
    [SerializeField] private Vector3 windUpOffset = new Vector3(0.1f, 0.06f, -0.62f);
    [Tooltip("Au lancer, l'objet part devant pendant ce temps (s), puis disparaît.")]
    [SerializeField] private float throwPushTime = 0.15f;

    private static readonly int PoingId = Animator.StringToHash("Poing");
    private static readonly int PoingDroitId = Animator.StringToHash("PoingDroit");
    private static readonly int GardeId = Animator.StringToHash("Garde");
    private static readonly int GardeChocId = Animator.StringToHash("GardeChoc");
    private static readonly int GesteSpeedId = Animator.StringToHash("GesteSpeed");
    private static readonly int MainsNuesId = Animator.StringToHash("MainsNues");
    private const int UpperBodyLayer = 1; // HautDuCorps : le fusil d'un duelliste, muet ici

    private SpectatorController spectator;
    private PlayerLocomotion locomotion;
    private PlayerAnimator playerAnimator;
    private FirstPersonView firstPersonView;
    private Transform cameraTransform;
    private int layer = -1;

    private GameObject arms;
    private Animator armsAnimator;
    private Transform armsHead, armsLeftUpperArm, armsFist, armsRightUpperArm, armsRightHand;

    private GameObject held;
    private ArenaItemKind heldKind;
    private int heldVariant = -1;

    private float punchStartedAt = float.NegativeInfinity;
    private float punchDuration = 0.45f;
    private bool punchRight;
    private float guardWeight;
    private float throwStartedAt = float.NegativeInfinity;
    private PlayerChassis chassis;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        spectator = GetComponent<SpectatorController>();
        locomotion = GetComponent<PlayerLocomotion>();
        playerAnimator = GetComponent<PlayerAnimator>();
        firstPersonView = GetComponent<FirstPersonView>();
        layer = LayerMask.NameToLayer(FirstPersonView.LayerName);
        if (spectator != null)
        {
            spectator.OnPunch += HandlePunch;
            spectator.OnThrow += HandleThrow;
            spectator.OnGuardHit += HandleGuardHit;
        }
        BuildArms();
        chassis = GetComponent<PlayerChassis>();
        if (chassis != null)
        {
            chassis.Changed += HandleChassis;
            HandleChassis(chassis.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (spectator != null)
        {
            spectator.OnPunch -= HandlePunch;
            spectator.OnThrow -= HandleThrow;
            spectator.OnGuardHit -= HandleGuardHit;
        }
        if (chassis != null) chassis.Changed -= HandleChassis;
        if (arms != null) Destroy(arms);
        if (held != null) Destroy(held);
        arms = null;
        held = null;
    }

    // Les bras à mains nues dans le châssis publié (2026-10-26), comme ceux de la vue armée.
    private void HandleChassis(ChassisId id)
    {
        if (arms != null) RobotChassis.Apply(arms.transform, id);
    }

    private void HandlePunch(float duration, bool right)
    {
        punchStartedAt = Time.time;
        punchDuration = Mathf.Max(0.1f, duration);
        punchRight = right;
        if (armsAnimator == null || !armsAnimator.isActiveAndEnabled) return;
        armsAnimator.SetFloat(GesteSpeedId, playerAnimator != null ? playerAnimator.PunchPlaybackSpeed : 1f);
        armsAnimator.SetTrigger(right ? PoingDroitId : PoingId);
    }

    private void HandleThrow() => throwStartedAt = Time.time;

    private void HandleGuardHit()
    {
        if (armsAnimator != null && armsAnimator.isActiveAndEnabled) armsAnimator.SetTrigger(GardeChocId);
    }

    /// <summary>La copie du robot, à mains nues, accrochée à la caméra (comme la vue armée).</summary>
    private void BuildArms()
    {
        Transform model = transform.Find("Model");
        Camera cam = firstPersonView != null ? firstPersonView.PlayerCamera : null;
        if (model == null || cam == null || layer < 0) return;
        cameraTransform = cam.transform;

        arms = FirstPersonView.BuildView(model.gameObject, cameraTransform, layer, out armsAnimator, out _);
        arms.name = "VueSpectateur";
        // À mains nues : ni arme, ni gant, rien de ce qu'il tenait à la copie.
        foreach (WeaponModel weapon in arms.GetComponentsInChildren<WeaponModel>(true)) weapon.gameObject.SetActive(false);
        HeldWeapons.WearTaserGlove(armsAnimator, false);
        armsHead = armsAnimator.GetBoneTransform(HumanBodyBones.Head);
        armsLeftUpperArm = armsAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        armsRightUpperArm = armsAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        armsRightHand = armsAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform glove = HeldWeapons.TaserGlove(armsAnimator);
        armsFist = glove != null ? glove.Find("Poing") : null;

        PlayerPaint paint = GetComponent<PlayerPaint>();
        if (paint != null)
        {
            foreach (Renderer original in paint.PaintedRenderers)
            {
                if (original == null) continue;
                Transform twin = arms.transform.Find(RelativePath(original.transform, model));
                if (twin != null && twin.TryGetComponent(out Renderer r)) paint.AddRenderer(r);
            }
        }
        arms.SetActive(false);
    }

    private void LateUpdate()
    {
        if (!IsOwner || spectator == null || cameraTransform == null) return;

        bool active = locomotion != null && locomotion.IsSpectator && firstPersonView != null && firstPersonView.ArmsCamera != null
                      && firstPersonView.ArmsCamera.enabled;
        if (arms != null && arms.activeSelf != active) arms.SetActive(active);
        if (!active)
        {
            ShowHeld(ArenaItemKind.None, -1);
            return;
        }

        UpdatePunch();
        UpdateHeld();
    }

    /// <summary>La tête de la copie dans l'œil ; pendant un coup, son poing vers l'écran ; en garde, ses
    /// deux poings levés devant les yeux.</summary>
    private void UpdatePunch()
    {
        if (arms == null || armsAnimator == null) return;
        float t = Time.time - punchStartedAt;
        float weight = 0f;
        bool punching = t >= 0f && t <= punchDuration;
        if (punching)
        {
            float rise = Mathf.Clamp01(t / 0.05f);
            float fall = Mathf.Clamp01((punchDuration - t) / 0.12f);
            weight = Mathf.Min(rise, fall);
        }
        // La garde de la simulation (celle qui part au serveur), après la fin d'un coup.
        bool guard = locomotion != null && locomotion.DisplayGuard && !punching;
        guardWeight = Mathf.MoveTowards(guardWeight, guard ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, guardBlendTime));

        // À mains nues au repos (l'arbre debout des spectateurs, pas de fusil), les directs et la garde sur
        // la couche des gestes.
        armsAnimator.SetFloat(MainsNuesId, 1f);
        armsAnimator.SetBool(GardeId, guard);
        if (armsAnimator.layerCount > PlayerAnimator.SpectatorGesturesLayer)
        {
            armsAnimator.SetLayerWeight(UpperBodyLayer, 0f);
            armsAnimator.SetLayerWeight(PlayerAnimator.SpectatorGesturesLayer, Mathf.Max(weight, guardWeight));
        }

        // La copie est enfant de la caméra : sa tête (repliée) près de l'œil, un peu en avant de lui pour
        // que le buste reste hors de l'écran ; en garde, plus bas, pour que les poings tiennent le bas de
        // l'écran.
        if (armsHead != null)
            Place(arms.transform, armsHead, Vector3.Lerp(viewOffset, guardViewOffset, Mathf.SmoothStep(0f, 1f, guardWeight)));

        // Le poing qui frappe vers le centre de l'écran : le gauche un peu à gauche, le droit un peu à droite.
        float dip = Mathf.SmoothStep(0f, 1f, weight);
        if (dip <= 0.01f) return;
        if (punchRight)
        {
            if (armsRightUpperArm != null && armsRightHand != null)
            {
                Vector3 target = new Vector3(-fistTarget.x, fistTarget.y, fistTarget.z);
                FirstPersonView.AimStrike(armsRightUpperArm, armsRightHand, cameraTransform.TransformPoint(target), dip);
            }
        }
        else if (armsLeftUpperArm != null && armsFist != null)
        {
            FirstPersonView.AimStrike(armsLeftUpperArm, armsFist, cameraTransform.TransformPoint(fistTarget), dip);
        }
    }

    /// <summary>Place la copie des bras (`root`, enfant de la caméra) : sa tête repliée au point de l'œil,
    /// décalée de `offset` (repère de la caméra). Sans état, testée (SpectatorViewTests).</summary>
    public static void Place(Transform root, Transform head, Vector3 offset)
    {
        root.localRotation = Quaternion.identity;
        root.localPosition = -root.InverseTransformPoint(head.position) + offset;
    }

    /// <summary>L'objet en main, en bas à droite : il recule pendant l'élan, part devant au lancer. Rangé
    /// (la molette), il ne se voit pas.</summary>
    private void UpdateHeld()
    {
        float sinceThrow = Time.time - throwStartedAt;
        bool pushing = sinceThrow >= 0f && sinceThrow < throwPushTime;
        ArenaItemKind kind = spectator.ShownInHand;
        int variant = spectator.CarriedVariant;
        if (pushing && kind == ArenaItemKind.None)
        {
            // La main vient de se vider (lancer) : l'objet finit son geste avant de disparaître.
            kind = heldKind;
            variant = heldVariant;
        }
        ShowHeld(kind, variant);
        if (held == null) return;

        Vector3 offset = holdOffset + windUpOffset * Mathf.SmoothStep(0f, 1f, spectator.ThrowAimProgress);
        if (pushing) offset += new Vector3(-0.1f, 0.05f, 0.6f) * (sinceThrow / throwPushTime);
        held.transform.localPosition = offset;
        held.transform.localRotation = Quaternion.Euler(holdEuler);
    }

    private void ShowHeld(ArenaItemKind kind, int variant)
    {
        if (kind == heldKind && variant == heldVariant) return;
        if (held != null) Destroy(held);
        held = null;
        heldKind = kind;
        heldVariant = variant;
        if (kind == ArenaItemKind.None || Tribune.Instance == null || Tribune.Instance.ItemPrefab == null) return;
        held = Tribune.Instance.ItemPrefab.CreateVisual(kind, variant, cameraTransform);
        if (held == null) return;
        foreach (Transform t in held.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        foreach (Renderer r in held.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    private static string RelativePath(Transform t, Transform root)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
        return path;
    }
}
