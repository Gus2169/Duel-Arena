using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Un chargeur vide tombé d'une arme pendant un rechargement (2026-10-29, MagazineSwap). Catégorie C.
///
/// Sur le corps, c'est un corps rigide sur le layer Corpse, comme les débris (CorpseLayer) : il ne touche que le
/// décor, ni les joueurs, ni les tirs (worldMask), ni rien de la simulation, puis disparaît au bout de son temps.
/// Chez le joueur, dont le corps n'est qu'une ombre, il ne se montre qu'une fois tombé : celui de ses bras passe
/// d'abord hors de l'écran. Sur les bras de la vue subjective, il ne fait que tomber, sans physique, et s'efface
/// aussitôt : les bras sont dessinés par-dessus le décor, et posé au sol il s'y dessinerait à travers les murs.
/// Les plus anciens disparaissent quand il y en a trop.
/// </summary>
public class DroppedMagazine : MonoBehaviour
{
    /// <summary>Chargeurs tombés gardés au plus, tous joueurs confondus : au-delà, les plus anciens partent.</summary>
    public const int MaxFallen = 12;

    private static readonly Queue<DroppedMagazine> fallen = new Queue<DroppedMagazine>();

    private float dieAt;
    private float revealAt = -1f;
    private bool drawn;            // sans physique : il tombe de lui-même
    private Vector3 velocity, spin;

    /// <summary>Le rechargement de domaine est coupé à l'entrée en Play Mode : la file ne garde rien d'une session à
    /// l'autre.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => fallen.Clear();

    /// <summary>
    /// Fait tomber `copy` (une copie du chargeur, à sa place) à `velocity` (m/s), en tournant à `angularVelocity`
    /// (rad/s, monde). `physical` : un corps rigide sur le layer Corpse ; sinon il tombe sans physique. Il disparaît
    /// au bout de `lifetime` ; `revealAfter` > 0 : ses pièces, qui ne sont qu'une ombre, se montrent après ce temps.
    /// </summary>
    public static DroppedMagazine Spawn(GameObject copy, Vector3 velocity, Vector3 angularVelocity, float lifetime,
                                        bool physical, float revealAfter)
    {
        var dropped = copy.AddComponent<DroppedMagazine>();
        dropped.dieAt = Time.time + Mathf.Max(0.01f, lifetime);
        dropped.revealAt = revealAfter > 0f ? Time.time + revealAfter : -1f;
        if (physical)
        {
            int layer = CorpseLayer.Index;
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            var box = copy.AddComponent<BoxCollider>();
            Bounds bounds = LocalBounds(copy.transform);
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.01f);
            var body = copy.AddComponent<Rigidbody>();
            body.mass = 0.45f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.linearVelocity = velocity;
            body.angularVelocity = angularVelocity;
        }
        else
        {
            dropped.drawn = true;
            dropped.velocity = velocity;
            dropped.spin = angularVelocity;
        }
        fallen.Enqueue(dropped);
        while (fallen.Count > MaxFallen)
        {
            DroppedMagazine oldest = fallen.Dequeue();
            if (oldest != null) Destroy(oldest.gameObject);
        }
        return dropped;
    }

    /// <summary>La boîte de ses pièces, dans son propre repère.</summary>
    private static Bounds LocalBounds(Transform root)
    {
        bool any = false;
        var bounds = new Bounds();
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds b = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 p = root.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        return bounds;
    }

    /// <summary>Tombe-t-il sans physique (les bras de la vue subjective) ?</summary>
    public bool Drawn => drawn;

    private void LateUpdate()
    {
        if (Time.time >= dieAt)
        {
            Destroy(gameObject);
            return;
        }
        if (revealAt >= 0f && Time.time >= revealAt)
        {
            revealAt = -1f;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) r.shadowCastingMode = ShadowCastingMode.On;
        }
        if (!drawn) return;
        float dt = Time.deltaTime;
        velocity += Physics.gravity * dt;
        transform.position += velocity * dt;
        if (spin.sqrMagnitude > 1e-8f) transform.rotation = Quaternion.AngleAxis(spin.magnitude * Mathf.Rad2Deg * dt, spin) * transform.rotation;
    }
}
