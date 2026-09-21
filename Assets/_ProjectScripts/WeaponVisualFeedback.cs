using UnityEngine;

/// <summary>
/// Feedback visuel de tir en Game view : trace lumineuse du tir + marqueur d'impact temporaire.
/// Remplace les Debug.DrawLine existants dans WeaponController (invisibles en dehors de la fenêtre
/// Scene, donc inutiles en Play normal) par quelque chose de réellement visible à l'écran pendant
/// que tu joues.
///
/// Entièrement procédural : pas de prefab à préparer, pas de matériau à créer à la main. Le shader
/// utilisé est détecté automatiquement pour rester compatible que le projet soit en Built-in RP ou
/// en URP (le cas le plus probable pour un projet Unity 6 récent). Pensé pour être remplacé plus
/// tard par de vrais VFX (Particle System, tracer avec traînée, décal d'impact texturé...) sans
/// changer l'API publique (SpawnTracer / SpawnImpact) — ce qui appelle ces méthodes n'aura rien à
/// changer le jour où tu amélioreras le visuel.
///
/// Mise en place :
/// 1. Pose ce script sur le Player (ou un enfant dédié à l'arme).
/// 2. Assigne-le au champ "Visual Feedback" de WeaponController dans l'inspecteur.
/// 3. Dans WeaponController.Fire() (ou équivalent), appelle SpawnTracer/SpawnImpact juste après le
///    Physics.Raycast, à la place (ou en plus) des Debug.DrawLine actuels.
/// </summary>
public class WeaponVisualFeedback : MonoBehaviour
{
    [Header("Trace de tir")]
    [SerializeField] private Color tracerColor = new Color(1f, 0.85f, 0.3f);
    [SerializeField] private float tracerWidth = 0.015f;
    [SerializeField] private float tracerDuration = 0.05f;

    [Header("Marqueur d'impact")]
    [SerializeField] private Color impactColor = new Color(1f, 0.3f, 0.1f);
    [SerializeField] private float impactSize = 0.12f;
    [SerializeField] private float impactLifetime = 1.2f;

    private static Shader cachedUnlitShader;

    private static Shader GetUnlitShader()
    {
        if (cachedUnlitShader != null) return cachedUnlitShader;

        // On essaie plusieurs shaders unlit connus dans l'ordre, pour que ça marche que le projet
        // soit en URP (cas probable) ou en Built-in Render Pipeline, sans configuration manuelle.
        cachedUnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (cachedUnlitShader == null) cachedUnlitShader = Shader.Find("Sprites/Default");
        if (cachedUnlitShader == null) cachedUnlitShader = Shader.Find("Unlit/Color");
        return cachedUnlitShader;
    }

    /// <summary>Trace visuelle instantanée entre le point de départ et le point d'arrivée du tir.</summary>
    public void SpawnTracer(Vector3 start, Vector3 end)
    {
        var go = new GameObject("ShotTracer");
        var lr = go.AddComponent<LineRenderer>();

        var mat = new Material(GetUnlitShader());
        mat.color = tracerColor;
        lr.material = mat;
        lr.startColor = tracerColor;
        lr.endColor = tracerColor;
        lr.startWidth = tracerWidth;
        lr.endWidth = tracerWidth;
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        Destroy(go, tracerDuration);
    }

    /// <summary>Petit marqueur temporaire à l'endroit précis où le tir a touché quelque chose.</summary>
    public void SpawnImpact(Vector3 point, Vector3 normal)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "ImpactMarker";

        // Pas besoin de collider sur un marqueur purement visuel — et ça éviterait de fausser
        // d'autres raycasts (dont les tiens) si on le laissait.
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        go.transform.position = point + normal * 0.01f; // léger décalage pour éviter le z-fighting
        go.transform.localScale = Vector3.one * impactSize;

        var renderer = go.GetComponent<Renderer>();
        var mat = new Material(GetUnlitShader());
        mat.color = impactColor;
        renderer.material = mat;

        Destroy(go, impactLifetime);
    }
}
