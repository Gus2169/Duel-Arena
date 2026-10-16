using UnityEngine;

/// <summary>
/// Le laser tactique (2026-10-16, GDD § 6) : un point rouge là où le joueur vise, et, chez les autres,
/// le faisceau qui y mène depuis l'émetteur de son laser. Il aide le tir au juger (le cône de la hanche
/// et la gerbe d'un pompe se resserrent : AttachmentCatalog.laserHipSpread ; le point marque le centre
/// du cône) et trahit qui le porte : le faisceau se voit de loin, le point sur le mur aussi. Un vrai
/// choix, comme le voulait l'utilisateur.
///
/// Le point est au bout du regard : chez le joueur, le rayon même du tir (la caméra) ; chez les
/// autres, l'œil et le regard affichés de sa pose (PlayerLocomotion.DisplayEyePosition,
/// DisplayLookDirection), arrêtés par le décor et les corps comme une balle (WeaponData.worldMask,
/// hitboxMask). Le joueur voit aussi son propre faisceau, comme dans Call of Duty : il part du laser
/// de ses bras et il est dessiné par leur caméra (layer Viewmodel, FirstPersonView) ; son bout, sur
/// l'axe de la caméra, tombe au même endroit pour les deux caméras. Pas dans la lunette du sniper.
///
/// Catégorie C réseautée : les accessoires sont publiés par le serveur (PlayerWeaponAttachments), le
/// regard voyage dans la pose ; rien ne relit le faisceau. Posé sur la racine du préfab joueur.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(600)] // après PlayerAnimator (l'arme suit le regard) et la caméra du joueur
public class TacticalLaser : MonoBehaviour
{
    [Tooltip("Largeur apparente minimale du faisceau (rad) : il reste visible de loin, une fois et demie un pixel en 1080p sous 90°.")]
    [SerializeField] private float minimumAngularWidth = 0.0028f;

    [Tooltip("La luminosité du faisceau à son bout, rapportée à celle de l'émetteur : il s'éteint en portant.")]
    [SerializeField, Range(0f, 1f)] private float endBrightness = 0.3f;

    private PlayerLocomotion locomotion;
    private WeaponController weapon;
    private Health health;
    private FirstPersonView firstPersonView;
    private Transform rightHand;
    private int worldLayer, viewLayer;
    private LineRenderer beam;
    private MeshRenderer dot;
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private static Mesh quad;

    /// <summary>Le point est-il montré cette image ? (tests)</summary>
    public bool DotShown => dot != null && dot.enabled;
    /// <summary>Le faisceau est-il montré cette image ? (tests)</summary>
    public bool BeamShown => beam != null && beam.enabled;
    /// <summary>Où est le point (tests).</summary>
    public Vector3 DotPosition => dot != null ? dot.transform.position : Vector3.zero;

    private void Awake()
    {
        locomotion = GetComponent<PlayerLocomotion>();
        weapon = GetComponentInChildren<WeaponController>(true);
        health = GetComponent<Health>();
        firstPersonView = GetComponent<FirstPersonView>();
        worldLayer = 0;   // Default : la caméra du joueur le dessine
        viewLayer = LayerMask.NameToLayer(FirstPersonView.LayerName);
        Transform model = transform.Find("Model");
        Animator animator = model != null ? model.GetComponent<Animator>() : null;
        rightHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
    }

    private void OnDestroy()
    {
        if (beam != null) Destroy(beam.gameObject);
        if (dot != null) Destroy(dot.gameObject);
    }

    private void LateUpdate()
    {
        bool on = TryAim(out Vector3 origin, out Vector3 direction, out bool owner, out WeaponData data);
        AttachmentCatalog catalog = data != null ? data.attachmentCatalog : null;
        if (!on || catalog == null || catalog.dotMaterial == null)
        {
            Show(false, false);
            return;
        }

        float range = catalog.beamRange;
        Vector3 end = origin + direction * range;
        bool hit = Cast(origin, direction, range, data, out RaycastHit stop);
        if (hit) end = stop.point - direction * 0.01f;   // posé devant la surface, pas dedans

        Camera viewer = Camera.main;
        Transform emitter = owner ? ViewEmitter() : Emitter(rightHand);
        bool beamShown = emitter != null && catalog.beamMaterial != null;
        Show(hit, beamShown);
        if (hit) PlaceDot(end, viewer, catalog);
        if (beamShown)
        {
            // Le faisceau du joueur est dessiné par la caméra de ses bras, avec eux.
            int layer = owner && viewLayer >= 0 ? viewLayer : worldLayer;
            if (beam.gameObject.layer != layer) beam.gameObject.layer = layer;
            PlaceBeam(emitter.position, end, viewer, catalog);
        }
    }

    /// <summary>Le laser est-il allumé, et d'où part le regard qu'il suit ?</summary>
    private bool TryAim(out Vector3 origin, out Vector3 direction, out bool owner, out WeaponData data)
    {
        origin = direction = Vector3.zero;
        owner = false;
        data = null;
        if (weapon == null || locomotion == null || locomotion.IsSpectator) return false;
        if (health != null && health.IsDead) return false;
        int slot = weapon.DisplayedSlot;
        data = weapon.DataOf(slot);
        if (data == null || !weapon.Attachments.Laser(slot) || !WeaponAttachments.Accepts(data, AttachmentCategory.Laser, 1)) return false;

        owner = weapon.IsOwner && weapon.AimCamera != null;
        if (owner)
        {
            Transform cam = weapon.AimCamera.transform;
            origin = cam.position;
            direction = cam.forward;
        }
        else
        {
            origin = locomotion.DisplayEyePosition;
            direction = locomotion.DisplayLookDirection;
        }
        return direction.sqrMagnitude > 0.5f;
    }

    /// <summary>Le premier obstacle du regard : le décor, ou un autre corps devant lui (comme le tir,
    /// WeaponController.ResolveShot). Les zones du joueur lui-même ne l'arrêtent pas.</summary>
    private bool Cast(Vector3 origin, Vector3 direction, float range, WeaponData data, out RaycastHit stop)
    {
        bool found = Physics.Raycast(origin, direction, out stop, range, data.worldMask, QueryTriggerInteraction.Ignore);
        float limit = found ? stop.distance : range;
        int n = Physics.RaycastNonAlloc(origin, direction, hits, limit, data.hitboxMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform) || hits[i].distance >= limit) continue;
            limit = hits[i].distance;
            stop = hits[i];
            found = true;
        }
        return found;
    }

    /// <summary>L'émetteur du laser monté sur l'arme tenue par cette main, ou null.</summary>
    private static Transform Emitter(Transform hand)
    {
        WeaponModel model = HeldWeapons.Visible(hand);
        WeaponAttachment laser = model != null ? AttachmentMount.Mounted(model.transform, AttachmentCategory.Laser) : null;
        return laser != null ? laser.Beam : null;
    }

    /// <summary>Chez le joueur : l'émetteur du laser de ses bras, ou null quand on ne les voit pas
    /// (lunette du sniper, mort).</summary>
    private Transform ViewEmitter()
    {
        if (firstPersonView == null || firstPersonView.ViewAnimator == null || firstPersonView.Scoped) return null;
        return Emitter(firstPersonView.ViewAnimator.GetBoneTransform(HumanBodyBones.RightHand));
    }

    private void Show(bool dotShown, bool beamShown)
    {
        if (dotShown || beamShown) EnsureObjects();
        if (dot != null && dot.enabled != dotShown) dot.enabled = dotShown;
        if (beam != null && beam.enabled != beamShown) beam.enabled = beamShown;
    }

    private void PlaceDot(Vector3 position, Camera viewer, AttachmentCatalog catalog)
    {
        if (dot.sharedMaterial != catalog.dotMaterial) dot.sharedMaterial = catalog.dotMaterial;
        Transform t = dot.transform;
        t.position = position;
        if (viewer != null)
        {
            Vector3 toViewer = viewer.transform.position - position;
            float distance = toViewer.magnitude;
            if (distance > 1e-4f) t.rotation = Quaternion.LookRotation(-toViewer / distance, viewer.transform.up);
            t.localScale = Vector3.one * Mathf.Max(0.004f, distance * catalog.dotAngularSize);
        }
    }

    private void PlaceBeam(Vector3 start, Vector3 end, Camera viewer, AttachmentCatalog catalog)
    {
        if (beam.sharedMaterial != catalog.beamMaterial) beam.sharedMaterial = catalog.beamMaterial;
        beam.SetPosition(0, start);
        beam.SetPosition(1, end);
        float startWidth = catalog.beamWidth, endWidth = catalog.beamWidth;
        if (viewer != null)
        {
            Vector3 eye = viewer.transform.position;
            startWidth = Mathf.Max(startWidth, Vector3.Distance(eye, start) * minimumAngularWidth);
            endWidth = Mathf.Max(endWidth, Vector3.Distance(eye, end) * minimumAngularWidth);
        }
        beam.startWidth = startWidth;
        beam.endWidth = endWidth;
    }

    private void EnsureObjects()
    {
        if (beam != null && dot != null) return;
        if (beam == null)
        {
            // Enfants du joueur, pour que l'écran de fin les cache avec lui (VictoryStage) ; en
            // coordonnées du monde.
            var go = new GameObject("Faisceau laser");
            go.transform.SetParent(transform, false);
            beam = go.AddComponent<LineRenderer>();
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.numCapVertices = 0;
            beam.textureMode = LineTextureMode.Stretch;
            beam.alignment = LineAlignment.View;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            beam.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(endBrightness, 1f) });
            beam.colorGradient = gradient;
            beam.enabled = false;
        }
        if (dot == null)
        {
            var go = new GameObject("Point laser");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            dot = go.AddComponent<MeshRenderer>();
            dot.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dot.receiveShadows = false;
            dot.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            dot.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            dot.enabled = false;
        }
    }

    /// <summary>Un carré d'un mètre, centré, tourné vers -Z (la caméra), ses UV de 0 à 1.</summary>
    private static Mesh Quad()
    {
        if (quad != null) return quad;
        quad = new Mesh { name = "Point laser" };
        quad.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
        };
        quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };   // le shader les lit
        quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        quad.RecalculateBounds();
        return quad;
    }
}
