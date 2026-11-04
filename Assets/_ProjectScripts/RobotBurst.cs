using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Ce que crache un robot qui vole en pièces (2026-11-03, demandé par l'utilisateur d'après les kill cams de Sniper Elite et
/// les brutalities de Mortal Kombat : « le gore qui fait exploser le modèle, avec de l'huile moteur à la place du sang, et
/// des étincelles ») : à l'impact, une gerbe d'huile, d'étincelles et d'éclats de blindage ; à chaque articulation rompue,
/// l'huile qui gicle par saccades puis goutte, et des étincelles qui grésillent ; derrière chaque pièce qui vole, une
/// traînée d'huile.
///
/// Catégorie C : des systèmes de particules faits à la volée (matériaux dans Resources, construits par Duel Arena > Mort >
/// Préparer les éclats du robot, RobotBurstMaterials), sur le layer de l'épave, dont la kill cam règle la vitesse au ralenti
/// (SetSpeed).
/// </summary>
public static class RobotBurst
{
    /// <summary>La force d'une gerbe : un coup léger, lourd ou brutal (RobotCorpse.SeverityOf).</summary>
    public static float StrengthOf(RobotCorpse.BurstSeverity severity) => severity switch
    {
        RobotCorpse.BurstSeverity.Light => 0.7f,
        RobotCorpse.BurstSeverity.Heavy => 1f,
        _ => 1.4f,
    };

    public const string OilMaterialName = "EclatsHuile";
    public const string SparkMaterialName = "EclatsEtincelle";
    public const string DebrisMaterialName = "EclatsDebris";
    public const string WakeMaterialName = "SillageBalle";

    /// <summary>Une lueur ajoutée, sans texture : le sillage de la balle de la kill cam (KillCam), qui s'éteint vers le noir.</summary>
    public static Material WakeMaterial => wakeMaterial != null ? wakeMaterial : wakeMaterial = LoadOrMake(WakeMaterialName, true);

    // L'huile moteur : de l'ambre doré à l'ambre brûlé, la goutte luisante (sa texture). Plus sombre, elle disparaissait
    // dans le décor assombri de la kill cam (vu à deux instances le 2026-11-03).
    private static readonly Color OilLight = new Color(0.66f, 0.42f, 0.1f, 1f);
    private static readonly Color OilDark = new Color(0.3f, 0.17f, 0.05f, 1f);
    private static readonly Color MetalGrey = new Color(0.46f, 0.48f, 0.52f, 1f);

    private static Material oilMaterial, sparkMaterial, debrisMaterial, wakeMaterial;
    private static Mesh debrisMesh;

    /// <summary>La gerbe de l'impact, en `point`, dans le sens du coup (`direction`) : huile, étincelles, éclats de
    /// blindage gris et à la couleur du robot (`paint`), un éclair. Renvoie sa racine (détruite d'elle-même au bout de
    /// `lifetime` s si > 0).</summary>
    public static GameObject Impact(Transform parent, Vector3 point, Vector3 direction, RobotCorpse.BurstSeverity severity,
                                    Color paint, int layer, float lifetime = 10f)
    {
        float strength = StrengthOf(severity);
        Quaternion facing = Facing(direction);
        GameObject root = Root("Eclats de l'impact", parent, point, facing, layer);
        OilSpray(root.transform, layer, Mathf.RoundToInt(90 * strength), 2.5f * strength, 6f * strength, 30f);
        Sparks(root.transform, layer, Mathf.RoundToInt(60 * strength), 3f, 9f * strength, 0f, 0);
        Debris(root.transform, layer, Mathf.RoundToInt(10 * strength), paint, strength);
        Flash(root.transform, layer, 5f * strength, 3.5f);
        Finish(root, lifetime);
        return root;
    }

    /// <summary>Une articulation rompue, en `point`, du côté du corps resté (`parent` : il la suit) : l'huile gicle vers
    /// `direction` par saccades, puis goutte ; des étincelles grésillent.</summary>
    public static GameObject Stump(Transform parent, Vector3 point, Vector3 direction, RobotCorpse.BurstSeverity severity,
                                   int layer, float lifetime = 10f)
    {
        float strength = StrengthOf(severity);
        GameObject root = Root("Articulation rompue", parent, point, Facing(direction), layer);
        OilSpurts(root.transform, layer, strength);
        Drips(root.transform, layer, 3f);
        Sparks(root.transform, layer, Mathf.RoundToInt(25 * strength), 2f, 6f, 1.4f, 6);
        Finish(root, lifetime);
        return root;
    }

    /// <summary>La traînée d'huile derrière une pièce qui vole (elle la suit, l'huile reste là où elle est tombée).</summary>
    public static GameObject Trail(Transform piece, Vector3 point, int layer, float lifetime = 10f)
    {
        GameObject root = Root("Trainee d'huile", piece, point, Quaternion.identity, layer);
        ParticleSystem oil = Create(root.transform, "Trainee", layer, OilMaterial());
        var main = oil.main;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
        main.startColor = new ParticleSystem.MinMaxGradient(OilLight, OilDark);
        main.gravityModifier = 1f;
        var emission = oil.emission;
        emission.rateOverTime = 45f;
        var shape = oil.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.03f;
        Splat(oil);
        Stretch(oil, 0.04f, 1.2f);
        Finish(root, lifetime);
        return root;
    }

    /// <summary>Le ralenti de la kill cam : chaque système de `effect` avance à `speed` fois le temps (et l'éclair aussi).</summary>
    public static void SetSpeed(GameObject effect, float speed)
    {
        if (effect == null) return;
        foreach (ParticleSystem system in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = system.main;
            main.simulationSpeed = Mathf.Max(0f, speed);
        }
        foreach (BurstFlash flash in effect.GetComponentsInChildren<BurstFlash>(true)) flash.speed = Mathf.Max(0f, speed);
    }

    // ------------------------------------------------------------------
    // Les systèmes
    // ------------------------------------------------------------------

    private static void OilSpray(Transform root, int layer, int count, float minSpeed, float maxSpeed, float angle)
    {
        ParticleSystem oil = Create(root, "Huile", layer, OilMaterial());
        var main = oil.main;
        main.duration = 0.4f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
        main.startColor = new ParticleSystem.MinMaxGradient(OilLight, OilDark);
        main.gravityModifier = 1.1f;
        main.maxParticles = 400;
        var emission = oil.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)count),
            new ParticleSystem.Burst(0.06f, (short)(count / 2)),
            new ParticleSystem.Burst(0.15f, (short)(count / 4)),
        });
        var shape = oil.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = 0.04f;
        Splat(oil);
        Stretch(oil, 0.035f, 1.4f);
    }

    // L'huile qui gicle d'une articulation rompue : par saccades, de moins en moins fort.
    private static void OilSpurts(Transform root, int layer, float strength)
    {
        ParticleSystem oil = Create(root, "Huile qui gicle", layer, OilMaterial());
        var main = oil.main;
        main.duration = 1.4f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f * strength);
        main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.05f);
        main.startColor = new ParticleSystem.MinMaxGradient(OilLight, OilDark);
        main.gravityModifier = 1f;
        main.maxParticles = 400;
        var emission = oil.emission;
        emission.rateOverTime = 0f;
        var bursts = new List<ParticleSystem.Burst>();
        float[] times = { 0f, 0.22f, 0.45f, 0.7f, 1f, 1.3f };
        for (int i = 0; i < times.Length; i++)
            bursts.Add(new ParticleSystem.Burst(times[i], (short)Mathf.RoundToInt(40 * strength / (1 + i * 0.6f))));
        emission.SetBursts(bursts.ToArray());
        var shape = oil.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.025f;
        Splat(oil);
        Stretch(oil, 0.04f, 1.5f);
    }

    private static void Drips(Transform root, int layer, float seconds)
    {
        ParticleSystem drips = Create(root, "Gouttes", layer, OilMaterial());
        var main = drips.main;
        main.duration = seconds;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.03f);
        main.startColor = new ParticleSystem.MinMaxGradient(OilLight, OilDark);
        main.gravityModifier = 1f;
        var emission = drips.emission;
        emission.rateOverTime = 12f;
        var shape = drips.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.03f;
        Splat(drips);
        Stretch(drips, 0.05f, 1.2f);
    }

    // Les étincelles : une gerbe, puis (crackles > 0) des grésillements, `crackles` petites gerbes sur `crackleTime` s.
    private static void Sparks(Transform root, int layer, int count, float minSpeed, float maxSpeed, float crackleTime, int crackles)
    {
        ParticleSystem sparks = Create(root, "Etincelles", layer, SparkMaterial());
        var main = sparks.main;
        main.duration = Mathf.Max(0.3f, crackleTime);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.65f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.022f);
        main.startColor = Color.white;
        main.gravityModifier = 0.6f;
        main.maxParticles = 500;
        var emission = sparks.emission;
        emission.rateOverTime = 0f;
        var bursts = new List<ParticleSystem.Burst> { new ParticleSystem.Burst(0f, (short)count) };
        for (int i = 1; i <= crackles; i++)
            bursts.Add(new ParticleSystem.Burst(crackleTime * i / (crackles + 0.5f), (short)Mathf.Max(4, count / 4)));
        emission.SetBursts(bursts.ToArray());
        var shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 55f;
        shape.radius = 0.03f;
        var color = sparks.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.97f, 0.85f), 0f), new GradientColorKey(new Color(1f, 0.72f, 0.25f), 0.35f),
                    new GradientColorKey(new Color(0.9f, 0.3f, 0.05f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var collision = sparks.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.collidesWith = 1;
        collision.bounce = 0.35f;
        collision.dampen = 0.25f;
        collision.lifetimeLoss = 0.2f;
        collision.quality = ParticleSystemCollisionQuality.Medium;
        Stretch(sparks, 0.025f, 2f);
    }

    // Des éclats de blindage : de petites plaques qui tournent et rebondissent, grises ou à la couleur du robot.
    private static void Debris(Transform root, int layer, int count, Color paint, float strength)
    {
        ParticleSystem debris = Create(root, "Eclats", layer, DebrisMaterial());
        var main = debris.main;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5.5f * strength);
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.025f, 0.075f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
        main.startSizeZ = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        Color tint = paint.a > 0f ? new Color(paint.r, paint.g, paint.b, 1f) : MetalGrey;
        main.startColor = new ParticleSystem.MinMaxGradient(MetalGrey, tint);
        main.gravityModifier = 1f;
        var emission = debris.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1, count)) });
        var shape = debris.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 60f;
        shape.radius = 0.05f;
        var spin = debris.rotationOverLifetime;
        spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-12f, 12f);
        spin.y = new ParticleSystem.MinMaxCurve(-12f, 12f);
        spin.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
        var collision = debris.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.collidesWith = 1;
        collision.bounce = 0.25f;
        collision.dampen = 0.45f;
        collision.quality = ParticleSystemCollisionQuality.Medium;
        var renderer = debris.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = DebrisMesh();
        renderer.alignment = ParticleSystemRenderSpace.Local;
    }

    private static void Flash(Transform root, int layer, float intensity, float range)
    {
        var go = new GameObject("Eclair");
        go.layer = layer;
        go.transform.SetParent(root, false);
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.7f, 0.35f);
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
        var flash = go.AddComponent<BurstFlash>();
        flash.peak = intensity;
    }

    // ------------------------------------------------------------------
    // Les briques
    // ------------------------------------------------------------------

    private static GameObject Root(string name, Transform parent, Vector3 point, Quaternion rotation, int layer)
    {
        var root = new GameObject(name);
        root.SetActive(false);
        root.layer = layer;
        if (parent != null) root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(point, rotation);
        return root;
    }

    private static void Finish(GameObject root, float lifetime)
    {
        root.SetActive(true);
        if (lifetime > 0f && Application.isPlaying) Object.Destroy(root, lifetime);
    }

    private static ParticleSystem Create(Transform root, string name, int layer, Material material)
    {
        var go = new GameObject(name);
        go.layer = layer;
        go.transform.SetParent(root, false);
        var system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.loop = false;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        // Sur une pièce qui vole, la vitesse de son Rigidbody ferait partir les particules d'où elle aurait été une image
        // plus tôt : la gerbe du cou flottait à 15 cm du robot (vu à la kill cam le 2026-11-04).
        main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return system;
    }

    // Une goutte touche le décor : elle s'y écrase et s'efface vite, sans rebondir.
    private static void Splat(ParticleSystem system)
    {
        var collision = system.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.collidesWith = 1;
        collision.bounce = 0.05f;
        collision.dampen = 0.8f;
        collision.lifetimeLoss = 0.5f;
        collision.quality = ParticleSystemCollisionQuality.Medium;
    }

    private static void Stretch(ParticleSystem system, float velocityScale, float lengthScale)
    {
        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = velocityScale;
        renderer.lengthScale = lengthScale;
    }

    private static Quaternion Facing(Vector3 direction)
        => direction.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(direction.normalized, Vector3.up) : Quaternion.identity;

    private static Material OilMaterial() => oilMaterial != null ? oilMaterial : oilMaterial = LoadOrMake(OilMaterialName, false);
    private static Material SparkMaterial() => sparkMaterial != null ? sparkMaterial : sparkMaterial = LoadOrMake(SparkMaterialName, true);

    /// <summary>Le matériau des étincelles, pour celles d'un tir (ShotImpacts).</summary>
    public static Material SparkParticles => SparkMaterial();
    private static Material DebrisMaterial() => debrisMaterial != null ? debrisMaterial : debrisMaterial = LoadOrMake(DebrisMaterialName, false);

    // Les matériaux préparés (Resources) ; à défaut, un matériau simple, en attendant qu'on les prépare (l'Editor
    // seulement : en build, le shader cherché par son nom n'y serait pas).
    private static Material LoadOrMake(string name, bool additive)
    {
        var material = Resources.Load<Material>(name);
        if (material != null) return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        material = new Material(shader) { name = name };
        if (additive)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
        return material;
    }

    private static Mesh DebrisMesh()
    {
        if (debrisMesh == null) debrisMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        return debrisMesh;
    }
}

/// <summary>L'éclair d'une gerbe : il s'éteint en 0,12 s (à la vitesse du ralenti, sous la kill cam).</summary>
public class BurstFlash : MonoBehaviour
{
    public float peak = 5f;
    public float speed = 1f;
    private float age;
    private Light lamp;

    private void Awake() => lamp = GetComponent<Light>();

    private void Update()
    {
        age += Time.deltaTime * speed;
        if (lamp == null) return;
        lamp.intensity = peak * Mathf.Clamp01(1f - age / 0.12f);
        if (lamp.intensity <= 0f) lamp.enabled = false;
    }
}
