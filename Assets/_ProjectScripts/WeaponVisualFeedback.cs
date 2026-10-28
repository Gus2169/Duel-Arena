using UnityEngine;

/// <summary>
/// Feedback visuel de tir en Game view : la flamme de bouche d'une arme à balles, ou la trace
/// lumineuse d'un rayon (le raygun), et un marqueur d'impact temporaire. PLACEHOLDER assumé, appelé
/// par WeaponController.Fire() (feedback local du tireur) et par BroadcastShotClientRpc (ce que voient
/// les autres joueurs). Depuis le 2026-10-16, les armes à balles n'ont plus de traçant (demandé par
/// l'utilisateur) : la flamme de bouche (WeaponData.shotIsBeam à faux).
///
/// Entièrement procédural : pas de prefab à préparer, pas de matériau à créer à la main. Le shader
/// utilisé est détecté automatiquement pour rester compatible Built-in RP / URP. Pensé pour être
/// remplacé plus tard par de vrais VFX (Particle System, tracer avec traînée, décal d'impact
/// texturé...) sans changer l'API publique (SpawnTracer / SpawnImpact) — les appelants n'auront
/// rien à changer le jour où le visuel sera amélioré.
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

    [Header("Flamme de bouche (armes à balles, 2026-10-16)")]
    [Tooltip("Matériau de la flamme (DuelArena/FlammeBouche). Sans lui, pas de flamme.")]
    [SerializeField] private Material muzzleFlashMaterial;
    [Tooltip("Durée de la flamme (s) : deux ou trois images.")]
    [SerializeField] private float muzzleFlashDuration = 0.05f;
    [Tooltip("Diamètre de l'étoile vue de face (m).")]
    [SerializeField] private float muzzleFlashSize = 0.22f;
    [Tooltip("Longueur des langues le long du canon (m).")]
    [SerializeField] private float muzzleFlashLength = 0.36f;
    [SerializeField] private Color muzzleLightColor = new Color(1f, 0.62f, 0.25f);
    [SerializeField] private float muzzleLightIntensity = 3f;
    [SerializeField] private float muzzleLightRange = 4f;

    [Header("Charge du tir (le FX-2, 2026-10-26)")]
    [Tooltip("Diamètre (m) de l'éclat au canon d'une arme chargée à fond : le point du laser tactique, en gros. Il grandit avec la charge et bat, pleine.")]
    [SerializeField] private float chargeGlowSize = 0.09f;
    [Tooltip("Sa taille apparente au moins (radians, pour la caméra de qui regarde), chargée à fond : comme le point du laser tactique, il se voit de loin.")]
    [SerializeField] private float chargeGlowAngularSize = 0.015f;
    [Tooltip("Sa lumière, chargée à fond : elle se voit sur les murs proches, l'adversaire voit venir le tir.")]
    [SerializeField] private float chargeLightIntensity = 2f;
    [SerializeField] private float chargeLightRange = 3f;

    private static Mesh flashMesh;
    private static Mesh glowQuad;
    private int flashCount;
    private MeshRenderer chargeGlow;
    private Light chargeLight;

    /// <summary>L'éclat de la charge est-il montré ? (tests)</summary>
    public bool ChargeGlowShown => chargeGlow != null && chargeGlow.enabled;
    public Transform ChargeGlowTransform => chargeGlow != null ? chargeGlow.transform : null;

    /// <summary>
    /// L'éclat d'une arme qui se charge (le FX-2) à sa bouche `muzzle` : d'autant plus gros et lumineux
    /// que la charge (0 à 1) est haute, battant une fois pleine ; caché sans charge. Le matériau est celui
    /// du point du laser tactique (DuelArena/FaisceauLaser), face à la caméra. Cosmétique, sur l'arme que
    /// chaque machine montre (les bras du tireur, le corps chez les autres) : l'adversaire voit venir le
    /// tir (GDD § 6).
    /// </summary>
    public void ShowCharge(Transform muzzle, float charge, Material material, Color color)
    {
        if (muzzle == null || material == null || !(charge > 0f))
        {
            if (chargeGlow != null && chargeGlow.enabled)
            {
                chargeGlow.enabled = false;
                if (chargeLight != null) chargeLight.enabled = false;
            }
            return;
        }
        if (chargeGlow == null)
        {
            var go = new GameObject("EclatCharge");
            go.AddComponent<MeshFilter>().sharedMesh = GlowQuad();
            chargeGlow = go.AddComponent<MeshRenderer>();
            chargeGlow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            chargeGlow.receiveShadows = false;
            chargeGlow.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            chargeGlow.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var lightObject = new GameObject("Lumiere");
            lightObject.transform.SetParent(go.transform, false);
            chargeLight = lightObject.AddComponent<Light>();
            chargeLight.type = LightType.Point;
            chargeLight.shadows = LightShadows.None;
        }
        // Enfant de la bouche, il suit l'arme ; sur son layer, il est dessiné avec elle. Une arme refaite
        // (HeldWeapons) l'emporte avec elle : il renaît à l'image suivante.
        Transform t = chargeGlow.transform;
        if (t.parent != muzzle)
        {
            t.SetParent(muzzle, false);
            chargeGlow.gameObject.layer = muzzle.gameObject.layer;
        }
        float pulse = charge >= 1f ? 1f + 0.15f * Mathf.Sin(Time.time * 18f) : 1f;
        float size = chargeGlowSize;
        Camera viewer = Camera.main;
        if (viewer != null)
        {
            Vector3 away = muzzle.position - viewer.transform.position;
            if (away.sqrMagnitude > 1e-8f) t.rotation = Quaternion.LookRotation(away, viewer.transform.up);
            size = Mathf.Max(size, away.magnitude * chargeGlowAngularSize);
        }
        size *= (0.3f + 0.7f * charge) * pulse;
        t.position = muzzle.position;
        Vector3 parentScale = muzzle.lossyScale;
        t.localScale = new Vector3(size / Mathf.Max(1e-4f, parentScale.x), size / Mathf.Max(1e-4f, parentScale.y),
                                   size / Mathf.Max(1e-4f, parentScale.z));
        if (chargeGlow.sharedMaterial != material) chargeGlow.sharedMaterial = material;
        chargeGlow.enabled = true;
        chargeLight.color = color;
        chargeLight.intensity = chargeLightIntensity * charge * pulse;
        chargeLight.range = chargeLightRange;
        chargeLight.enabled = true;
    }

    /// <summary>Un carré d'un mètre, centré, tourné vers -Z (la caméra), ses UV de 0 à 1 et ses couleurs
    /// de sommet blanches (le shader du point du laser les lit).</summary>
    private static Mesh GlowQuad()
    {
        if (glowQuad != null) return glowQuad;
        glowQuad = new Mesh { name = "Eclat de charge" };
        glowQuad.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
        };
        glowQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        glowQuad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        glowQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        glowQuad.RecalculateBounds();
        return glowQuad;
    }

    /// <summary>La flamme de bouche est-elle prête (matériau branché) ? (tests)</summary>
    public bool HasMuzzleFlash => muzzleFlashMaterial != null;
    public float MuzzleFlashDuration => muzzleFlashDuration;

    /// <summary>
    /// La flamme d'un coup à la bouche `muzzle` d'une arme à balles, vers `forward` (l'axe du canon) :
    /// une étoile et deux langues (DuelArena/FlammeBouche) et un éclair de lumière, quelques
    /// centièmes de seconde. Enfant de la bouche, elle suit l'arme ; sur son layer, elle est dessinée
    /// avec elle (les bras du tireur, le corps chez les autres). Cosmétique : chaque machine la place
    /// sur l'arme qu'elle montre. Elle tourne d'un coup à l'autre (un angle d'or, pas de hasard).
    /// </summary>
    public GameObject SpawnMuzzleFlash(Transform muzzle, Vector3 forward)
    {
        if (muzzle == null || muzzleFlashMaterial == null || forward.sqrMagnitude < 1e-6f) return null;
        flashCount++;
        float roll = flashCount * 137.5f;
        float scale = 0.85f + 0.3f * (0.5f + 0.5f * Mathf.Sin(flashCount * 2.3f));

        var go = new GameObject("FlammeBouche");
        go.layer = muzzle.gameObject.layer;
        go.transform.SetParent(muzzle, false);
        go.transform.position = muzzle.position;
        go.transform.rotation = Quaternion.LookRotation(forward, muzzle.up) * Quaternion.AngleAxis(roll, Vector3.forward);
        Vector3 parentScale = muzzle.lossyScale;
        go.transform.localScale = new Vector3(muzzleFlashSize * scale / Mathf.Max(1e-4f, parentScale.x),
                                              muzzleFlashSize * scale / Mathf.Max(1e-4f, parentScale.y),
                                              muzzleFlashLength * scale / Mathf.Max(1e-4f, parentScale.z));
        go.AddComponent<MeshFilter>().sharedMesh = FlashMesh();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = muzzleFlashMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        if (muzzleLightIntensity > 0f)
        {
            var lightObject = new GameObject("Eclair");
            lightObject.transform.SetParent(go.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = muzzleLightColor;
            light.intensity = muzzleLightIntensity;
            light.range = muzzleLightRange;
            light.shadows = LightShadows.None;
        }

        Destroy(go, muzzleFlashDuration);
        return go;
    }

    /// <summary>Le maillage de la flamme, dans le repère de la bouche (+Z le long du canon) : une
    /// étoile d'une unité de diamètre face au canon, et deux langues en croix, d'une unité de long.</summary>
    private static Mesh FlashMesh()
    {
        if (flashMesh != null) return flashMesh;
        var star = new Color(1f, 0f, 1f, 1f);
        var tongue = new Color(1f, 1f, 1f, 1f);
        flashMesh = new Mesh { name = "Flamme de bouche" };
        flashMesh.vertices = new[]
        {
            // l'étoile, face au canon
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            // la langue horizontale
            new Vector3(-0.25f, 0f, 0f), new Vector3(0.25f, 0f, 0f), new Vector3(0.25f, 0f, 1f), new Vector3(-0.25f, 0f, 1f),
            // la langue verticale
            new Vector3(0f, -0.25f, 0f), new Vector3(0f, 0.25f, 0f), new Vector3(0f, 0.25f, 1f), new Vector3(0f, -0.25f, 1f),
        };
        flashMesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
        };
        flashMesh.colors = new[] { star, star, star, star, tongue, tongue, tongue, tongue, tongue, tongue, tongue, tongue };
        flashMesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6, 8, 10, 9, 8, 11, 10 };
        flashMesh.RecalculateBounds();
        return flashMesh;
    }

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

    // Un matériau par couleur (traçants et impacts), partagé : en créer un par trace (huit par coup
    // de pompe) en laissait un derrière chaque trace détruite.
    private static readonly System.Collections.Generic.Dictionary<Color, Material> tracerMaterials =
        new System.Collections.Generic.Dictionary<Color, Material>();

    private static Material TracerMaterial(Color color)
    {
        if (tracerMaterials.TryGetValue(color, out Material material) && material != null) return material;
        material = new Material(GetUnlitShader()) { color = color, name = "Traçant" };
        tracerMaterials[color] = material;
        return material;
    }

    /// <summary>Trace visuelle instantanée entre le point de départ et le point d'arrivée du tir.</summary>
    public void SpawnTracer(Vector3 start, Vector3 end) => SpawnTracer(start, end, tracerColor);

    /// <summary>Même trace, à la couleur de l'arme (WeaponData.tracerColor : le rayon des armes laser),
    /// `weight` fois plus épaisse et plus durable (le tir chargé du FX-2).</summary>
    public void SpawnTracer(Vector3 start, Vector3 end, Color color, float weight = 1f)
    {
        var go = new GameObject("ShotTracer");
        var lr = go.AddComponent<LineRenderer>();

        lr.sharedMaterial = TracerMaterial(color);
        lr.startColor = color;
        lr.endColor = color;
        lr.startWidth = tracerWidth * weight;
        lr.endWidth = tracerWidth * weight;
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        Destroy(go, tracerDuration * weight);
    }

    /// <summary>Petit marqueur temporaire à l'endroit précis où le tir a touché quelque chose.</summary>
    public void SpawnImpact(Vector3 point, Vector3 normal)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "ImpactMarker";

        // CreatePrimitive livre TOUJOURS un collider. Il faut le neutraliser, et surtout le faire
        // IMMÉDIATEMENT : Destroy() est différé à la fin de la frame, donc le collider reste bien
        // vivant pour la physique entre-temps. C'est enabled = false qui prend effet tout de
        // suite ; Destroy ne fait que le ménage ensuite.
        //
        // Ce marqueur apparaît pile à l'endroit touché, donc à la surface du corps de la cible.
        // Un collider actif ne serait pas qu'un détail cosmétique :
        //   - le CharacterController de la cible se pousserait hors de la sphère à son prochain
        //     Move() — d'où un adversaire qui RECULE visiblement à chaque balle encaissée ;
        //   - la sphère naît sur le layer Default, celui de worldMask, donc elle bloquerait les
        //     tirs suivants comme un mur ;
        //   - elle compterait aussi comme obstacle pour CanStandUp() et l'anti-clipping du lean.
        var col = go.GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
            Destroy(col);
        }

        go.transform.position = point + normal * 0.01f; // léger décalage pour éviter le z-fighting
        go.transform.localScale = Vector3.one * impactSize;

        // Matériau partagé, comme celui des traçants : en créer un par impact en laissait un derrière
        // chaque marqueur détruit, des milliers au fil d'une soirée (relecture du 2026-10-12).
        go.GetComponent<Renderer>().sharedMaterial = TracerMaterial(impactColor);

        Destroy(go, impactLifetime);
    }
}
