using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// La trajectoire d'un lancer, vue du seul lanceur (2026-10-27, demandé par l'utilisateur, « pour mieux viser
/// sa cible ») : pendant qu'un spectateur vise (clic droit, l'objet en main, SpectatorController), la parabole
/// que suivra l'objet, de sa main jusqu'au premier obstacle du décor, et un cercle là où il le touchera.
///
/// Calculée comme le serveur simulera l'objet (ArenaItem : le même départ, la même vitesse, la même gravité,
/// la même sphère contre le décor, au pas près) ; elle traverse les robots, que le serveur juge seul. Un objet
/// local, créé chez le propriétaire : personne d'autre ne la voit. Catégorie C.
///
/// Après la caméra du joueur (ordre 600, comme le laser tactique) : elle part de la main là où l'œil la voit.
/// Ses traits gardent la même largeur apparente de près comme de loin.
/// </summary>
[DefaultExecutionOrder(600)]
[DisallowMultipleComponent]
public class ThrowArcPreview : MonoBehaviour
{
    /// <summary>Le pas (s) entre deux points de la trajectoire : son écart à la parabole reste sous 2 mm.</summary>
    public const float TraceStep = 1f / 30f;
    /// <summary>Quatre secondes de vol au plus : au-delà, l'objet est sorti de l'arène.</summary>
    public const int MaxPoints = 121;
    public const int RingSegments = 32;
    /// <summary>Le rayon (m) du cercle du point de chute.</summary>
    public const float RingRadius = 0.3f;
    /// <summary>La largeur apparente (rad) des traits, à toute distance : 4 à 5 pixels en 1080p.</summary>
    public const float ApparentWidth = 0.005f;

    private static readonly Color ArcColor = new Color(1f, 1f, 1f, 0.9f);
    private static readonly Color ArcEndColor = new Color(1f, 1f, 1f, 0.55f);

    private SpectatorController spectator;
    private LineRenderer arc;
    private LineRenderer ring;
    private readonly Vector3[] points = new Vector3[MaxPoints];
    private readonly Vector3[] ringPoints = new Vector3[RingSegments];

    /// <summary>La trajectoire est-elle montrée ? (tests)</summary>
    public bool Shown => arc != null && arc.enabled;

    /// <summary>Le nombre de points de la trajectoire montrée, et son dernier point (tests).</summary>
    public int ShownPointCount { get; private set; }
    public Vector3 ShownEnd => ShownPointCount > 0 ? points[ShownPointCount - 1] : transform.position;

    /// <summary>Le spectateur dont on montre le lancer.</summary>
    public void Bind(SpectatorController owner) => spectator = owner;

    private void LateUpdate()
    {
        Tribune tribune = Tribune.Instance;
        ArenaItem item = tribune != null ? tribune.ItemPrefab : null;
        if (spectator == null || item == null || !spectator.IsAimingThrow
            || !spectator.TryGetThrowStart(out Vector3 origin, out Vector3 direction))
        {
            Hide();
            return;
        }
        Show(origin, spectator.ThrowVelocityAlong(direction), item.Gravity, item.RadiusFor(spectator.ShownCarried), item.WorldMask,
             item.TrajectoryMaterial, Camera.main);
    }

    /// <summary>Montre la trajectoire d'un objet parti de `origin` à `velocity`, telle que `viewer` la voit.</summary>
    public void Show(Vector3 origin, Vector3 velocity, float gravity, float radius, LayerMask mask, Material material, Camera viewer)
    {
        EnsureLines(material);
        int count = Trace(origin, velocity, gravity, radius, mask, points, out bool hit, out Vector3 hitPoint, out Vector3 hitNormal);
        ShownPointCount = count;
        if (count < 2)
        {
            Hide();
            return;
        }

        Vector3 eye = viewer != null ? viewer.transform.position : origin;
        arc.positionCount = count;
        arc.SetPositions(points);
        // La largeur suit la distance à l'œil, du départ au bout : la même largeur apparente partout.
        arc.startWidth = ApparentWidth * Mathf.Max(0.3f, Vector3.Distance(eye, points[0]));
        arc.endWidth = ApparentWidth * Mathf.Max(0.3f, Vector3.Distance(eye, points[count - 1]));
        arc.startColor = ArcColor;
        arc.endColor = ArcEndColor;
        if (!arc.enabled) arc.enabled = true;

        if (!hit)
        {
            if (ring.enabled) ring.enabled = false;
            return;
        }
        // Le cercle du point de chute, posé sur la surface touchée (un peu au-dessus, pour ne pas s'y enfoncer).
        Vector3 normal = hitNormal.sqrMagnitude > 1e-6f ? hitNormal.normalized : Vector3.up;
        Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.95f ? Vector3.up : Vector3.forward).normalized;
        Vector3 bitangent = Vector3.Cross(normal, tangent);
        Vector3 center = hitPoint + normal * 0.02f;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i * (Mathf.PI * 2f / RingSegments);
            ringPoints[i] = center + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * RingRadius;
        }
        ring.SetPositions(ringPoints);
        ring.widthMultiplier = ApparentWidth * 1.4f * Mathf.Max(0.3f, Vector3.Distance(eye, center));
        ring.startColor = ring.endColor = ArcColor;
        if (!ring.enabled) ring.enabled = true;
    }

    public void Hide()
    {
        ShownPointCount = 0;
        if (arc != null && arc.enabled) arc.enabled = false;
        if (ring != null && ring.enabled) ring.enabled = false;
    }

    /// <summary>
    /// Les points de la trajectoire d'un objet lancé de `origin` à `velocity` sous `gravity` (vers le bas, m/s²),
    /// au pas TraceStep, jusqu'au premier obstacle de `mask` que touche une sphère de `radius` (le dernier point
    /// est alors le centre de la sphère au contact, `hitPoint` le point touché et `hitNormal` sa normale), ou
    /// jusqu'à la place de `points`. Rend le nombre de points écrits. La parabole d'ArenaItem.Evaluate et le même
    /// test que la simulation du serveur (ArenaItem.FindHit, contre le décor). Testée, avec de vrais colliders.
    /// </summary>
    public static int Trace(Vector3 origin, Vector3 velocity, float gravity, float radius, LayerMask mask, Vector3[] points,
                            out bool hit, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hit = false;
        hitPoint = default;
        hitNormal = Vector3.up;
        if (points == null || points.Length == 0) return 0;
        var motion = new ArenaItem.Motion { time = 0.0, position = origin, velocity = velocity, phase = ArenaItem.Phase.Flying };
        points[0] = origin;
        int count = 1;
        Vector3 a = origin;
        for (int i = 1; i < points.Length; i++)
        {
            Vector3 b = ArenaItem.Evaluate(motion, i * TraceStep, gravity);
            Vector3 delta = b - a;
            float length = delta.magnitude;
            if (length > 1e-6f)
            {
                Vector3 direction = delta / length;
                if (Physics.SphereCast(a, radius, direction, out RaycastHit h, length, mask, QueryTriggerInteraction.Ignore))
                {
                    hit = true;
                    hitPoint = h.point;
                    hitNormal = h.normal;
                    points[count++] = a + direction * h.distance;
                    return count;
                }
            }
            points[count++] = b;
            a = b;
        }
        return count;
    }

    private void EnsureLines(Material material)
    {
        if (arc != null) return;
        arc = NewLine("Trajectoire", material, false);
        ring = NewLine("Point de chute", material, true);
        ring.positionCount = RingSegments;
    }

    private LineRenderer NewLine(string name, Material material, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.loop = loop;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        line.allowOcclusionWhenDynamic = false;
        line.enabled = false;
        return line;
    }
}
