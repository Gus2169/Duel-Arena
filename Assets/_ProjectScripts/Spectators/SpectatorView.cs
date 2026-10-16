using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// La vue subjective d'un spectateur (2026-10-13) : ses poings et ce qu'il tient, vus de ses propres
/// yeux. La vue armée d'un duelliste (FirstPersonView) s'éteint sur la tribune ; celle-ci prend le
/// relais, sur le même layer et avec la même caméra des bras.
///
/// - Une copie du robot, à mains nues (ni arme, ni gant), accrochée à la caméra, tête dans l'œil : au
///   repos, ses bras sont sous l'écran. Le coup de poing y joue le direct du gauche du corps (couche des
///   gestes d'un spectateur, 2026-10-13), bras réorienté vers le centre de l'écran, comme le taser
///   d'un duelliste.
/// - L'objet tenu, en bas à droite : il recule pendant l'élan, part devant au lancer.
///
/// Catégorie C pure, chez le propriétaire seulement : rien ne la lit.
/// </summary>
[DisallowMultipleComponent]
public class SpectatorView : NetworkBehaviour
{
    [Header("Coup de poing (ressenti)")]
    [Tooltip("Où part le poing gauche, dans le repère de la caméra (m) : un peu à gauche et sous le centre de l'écran.")]
    [SerializeField] private Vector3 fistTarget = new Vector3(-0.07f, -0.11f, 0.55f);

    [Header("Objet tenu (ressenti)")]
    [Tooltip("Où se tient l'objet, dans le repère de la caméra (m) : en bas à droite.")]
    [SerializeField] private Vector3 holdOffset = new Vector3(0.24f, -0.21f, 0.45f);
    [SerializeField] private Vector3 holdEuler = new Vector3(8f, -25f, 4f);
    [Tooltip("Pendant l'élan, l'objet recule et monte d'autant à pleine charge (m, repère de la caméra).")]
    [SerializeField] private Vector3 windUpOffset = new Vector3(0.06f, 0.1f, -0.2f);
    [Tooltip("Au lancer, l'objet part devant pendant ce temps (s), puis disparaît.")]
    [SerializeField] private float throwPushTime = 0.15f;

    private static readonly int PoingId = Animator.StringToHash("Poing");
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
    private Transform armsHead, armsLeftUpperArm, armsFist;

    private GameObject held;
    private ArenaItemKind heldKind;
    private int heldVariant = -1;

    private float punchStartedAt = float.NegativeInfinity;
    private float punchDuration = 0.45f;
    private float throwStartedAt = float.NegativeInfinity;

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
        }
        BuildArms();
    }

    public override void OnNetworkDespawn()
    {
        if (spectator != null)
        {
            spectator.OnPunch -= HandlePunch;
            spectator.OnThrow -= HandleThrow;
        }
        if (arms != null) Destroy(arms);
        if (held != null) Destroy(held);
        arms = null;
        held = null;
    }

    private void HandlePunch(float duration)
    {
        punchStartedAt = Time.time;
        punchDuration = Mathf.Max(0.1f, duration);
        if (armsAnimator == null || !armsAnimator.isActiveAndEnabled) return;
        armsAnimator.SetFloat(GesteSpeedId, playerAnimator != null ? playerAnimator.PunchPlaybackSpeed : 1f);
        armsAnimator.SetTrigger(PoingId);
    }

    private void HandleThrow() => throwStartedAt = Time.time;

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

    /// <summary>La tête de la copie dans l'œil, son poing gauche vers l'écran pendant le coup.</summary>
    private void UpdatePunch()
    {
        if (arms == null || armsAnimator == null) return;
        float t = Time.time - punchStartedAt;
        float weight = 0f;
        if (t >= 0f && t <= punchDuration)
        {
            float rise = Mathf.Clamp01(t / 0.05f);
            float fall = Mathf.Clamp01((punchDuration - t) / 0.12f);
            weight = Mathf.Min(rise, fall);
        }
        // À mains nues au repos (l'arbre debout des spectateurs, pas de fusil), le direct sur la couche
        // des gestes.
        armsAnimator.SetFloat(MainsNuesId, 1f);
        if (armsAnimator.layerCount > PlayerAnimator.SpectatorGesturesLayer)
        {
            armsAnimator.SetLayerWeight(UpperBodyLayer, 0f);
            armsAnimator.SetLayerWeight(PlayerAnimator.SpectatorGesturesLayer, weight);
        }

        // La copie est enfant de la caméra : on la place pour que sa tête (repliée) soit dans l'œil.
        Transform root = arms.transform;
        root.localRotation = Quaternion.identity;
        if (armsHead != null)
        {
            Vector3 head = root.InverseTransformPoint(armsHead.position);
            root.localPosition = -head;
        }

        float dip = Mathf.SmoothStep(0f, 1f, weight);
        if (dip > 0.01f && armsLeftUpperArm != null && armsFist != null)
            FirstPersonView.AimStrike(armsLeftUpperArm, armsFist, cameraTransform.TransformPoint(fistTarget), dip);
    }

    /// <summary>L'objet tenu, en bas à droite : il recule pendant l'élan, part devant au lancer.</summary>
    private void UpdateHeld()
    {
        float sinceThrow = Time.time - throwStartedAt;
        bool pushing = sinceThrow >= 0f && sinceThrow < throwPushTime;
        ArenaItemKind kind = spectator.ShownCarried;
        int variant = spectator.CarriedVariant;
        if (pushing && kind == ArenaItemKind.None)
        {
            // La main vient de se vider (lancer) : l'objet finit son geste avant de disparaître.
            kind = heldKind;
            variant = heldVariant;
        }
        ShowHeld(kind, variant);
        if (held == null) return;

        Vector3 offset = holdOffset + windUpOffset * Mathf.SmoothStep(0f, 1f, spectator.ChargeProgress);
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
