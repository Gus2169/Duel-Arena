using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Là où arrive un tir (2026-11-04, à la place des boules rouges, demandé par l'utilisateur) : sur le décor, une marque
/// qui reste (un trou de balle, une brûlure noire pour une arme laser) et quelques étincelles ; sur un robot, un éclat et des
/// étincelles (rouges pour une arme laser), sans marque : sa surface touchable dépasse un peu de lui, et ses blessures
/// viendront à part (GDD § 6).
///
/// Catégorie C : chaque machine montre ce qu'elle voit. Toutes les marques tiennent dans un maillage, une seule image à
/// dessiner (DuelArena/MarqueImpact, MarquesImpact dans Resources : Duel Arena > Armes > Préparer les impacts). Elles
/// restent jusqu'à la manche suivante ; au-delà de MaxMarks, les plus anciennes s'effacent puis cèdent leur place. Une
/// marque ne se pose pas sur ce qui bouge. Les étincelles sont deux systèmes partagés, sans rien créer à chaque tir.
/// </summary>
public class ShotImpacts : MonoBehaviour
{
    /// <summary>Le matériau des marques, dans Resources.</summary>
    public const string MaterialName = "MarquesImpact";

    /// <summary>Les marques qu'on garde.</summary>
    public const int MaxMarks = 128;

    /// <summary>Les plus anciennes, à partir de ce nombre, s'effacent peu à peu avant de céder leur place.</summary>
    public const int FadingMarks = 16;

    /// <summary>Les cases de la texture : quatre colonnes de variantes ; les trous en haut, les brûlures en bas.</summary>
    public const int TileColumns = 4, TileRows = 2;

    /// <summary>Une marque flotte à tant de mètres de sa surface.</summary>
    private const float Lift = 0.004f;

    /// <summary>La sonde qui retrouve la surface sous une marque.</summary>
    private const float Probe = 0.06f;

    private static ShotImpacts instance;

    private static ShotImpacts Instance
    {
        get
        {
            if (instance == null) instance = new GameObject("Impacts des tirs").AddComponent<ShotImpacts>();
            return instance;
        }
    }

    /// <summary>Montre sur cette machine un tir de `data` arrivé en `point`, sur une surface de normale `normal` : un robot
    /// ou une épave (`onBody`), ou le décor.</summary>
    public static void Show(Vector3 point, Vector3 normal, bool onBody, WeaponData data)
    {
        if (data == null || normal.sqrMagnitude < 1e-6f) return;
        ShotImpacts impacts = Instance;
        bool beam = data.shotIsBeam;
        if (onBody)
        {
            impacts.Emit(point, normal, beam ? data.tracerColor : BulletSpark, beam ? 7 : 10, beam ? 3f : 5.5f, beam ? 0.5f : 0.32f);
            return;
        }
        impacts.Emit(point, normal, beam ? data.tracerColor : BulletSpark, 4, 3.5f, beam ? 0.26f : 0.16f);
        impacts.AddMark(point, normal, beam, MarkSize(data), data.worldMask);
    }

    /// <summary>La taille d'une marque (m), selon la force de l'arme : un plomb, un trou de fusil, une brûlure plus large.
    /// Fonction pure, testée.</summary>
    public static float MarkSize(WeaponData data)
    {
        float power = Mathf.Clamp01(data.damagePerHit / 100f);
        float size = data.shotIsBeam ? Mathf.Lerp(0.26f, 0.5f, power) : Mathf.Lerp(0.16f, 0.28f, power);
        return data.PelletCount > 1 ? size * 0.7f : size;
    }

    /// <summary>
    /// Où poser une marque de `size` mètres sur le décor touché en `point` : à sa surface, un peu devant, et jamais sur ce
    /// qui bouge (`surface` : sa normale). Près d'une arête, elle rétrécit pour ne pas pendre dans le vide. Faux s'il n'y a
    /// pas de décor fixe ici. Avec de vrais colliders, testée.
    /// </summary>
    public static bool TryPlace(Vector3 point, Vector3 normal, int mask, ref float size, out Vector3 center, out Vector3 surface)
    {
        center = point;
        surface = normal;
        if (!Physics.Raycast(point + normal * Probe, -normal, out RaycastHit hit, 2f * Probe, mask, QueryTriggerInteraction.Ignore)) return false;
        if (hit.collider.attachedRigidbody != null) return false;
        surface = hit.normal;
        center = hit.point + surface * Lift;
        for (int i = 0; i < 3 && !CornersOnSurface(hit.point, surface, size * 0.35f, mask); i++) size *= 0.65f;
        return true;
    }

    // Les quatre coins d'une marque sont-ils sur la même surface ?
    private static bool CornersOnSurface(Vector3 point, Vector3 surface, float reach, int mask)
    {
        Axes(surface, out Vector3 right, out Vector3 up);
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = point + ((i & 1) == 0 ? right : -right) * reach + ((i & 2) == 0 ? up : -up) * reach;
            if (!Physics.Raycast(corner + surface * Probe, -surface, out RaycastHit hit, 2f * Probe, mask, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(Vector3.Dot(hit.point - point, surface)) > 0.02f) return false;
        }
        return true;
    }

    private static void Axes(Vector3 surface, out Vector3 right, out Vector3 up)
    {
        right = Vector3.Cross(surface, Mathf.Abs(surface.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
        up = Vector3.Cross(right, surface);
    }

    // ------------------------------------------------------------------
    // Les marques
    // ------------------------------------------------------------------

    private readonly Vector3[] vertices = new Vector3[MaxMarks * 4];
    private readonly Vector2[] uvs = new Vector2[MaxMarks * 4];
    private readonly Color32[] colors = new Color32[MaxMarks * 4];
    private Mesh mesh;
    private int next, count;
    private bool dirty;
    private double seenPhaseStart = -1.0;

    /// <summary>Les marques posées.</summary>
    public static int MarkCount => instance != null ? instance.count : 0;

    private void Awake()
    {
        var material = Resources.Load<Material>(MaterialName);
        if (material != null)
        {
            mesh = new Mesh { name = "Marques des tirs" };
            mesh.MarkDynamic();
            var triangles = new int[MaxMarks * 6];
            for (int i = 0; i < MaxMarks; i++)
            {
                int v = i * 4, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rendered = gameObject.AddComponent<MeshRenderer>();
            rendered.sharedMaterial = material;
            rendered.shadowCastingMode = ShadowCastingMode.Off;
            rendered.receiveShadows = false;
            rendered.lightProbeUsage = LightProbeUsage.Off;
            rendered.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        sparks = MakeParticles("Etincelles", true);
        glows = MakeParticles("Eclats", false);
    }

    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (instance == this) instance = null;
    }

    private void AddMark(Vector3 point, Vector3 normal, bool burn, float size, int mask)
    {
        if (mesh == null || !TryPlace(point, normal, mask, ref size, out Vector3 center, out Vector3 surface)) return;
        Axes(surface, out Vector3 right, out Vector3 up);
        Quaternion spin = Quaternion.AngleAxis(Random.value * 360f, surface);
        right = spin * right * (size * 0.5f);
        up = spin * up * (size * 0.5f);
        int v = next * 4;
        vertices[v] = center - right - up;
        vertices[v + 1] = center - right + up;
        vertices[v + 2] = center + right + up;
        vertices[v + 3] = center + right - up;
        TileUvs(Random.Range(0, TileColumns), burn ? 1 : 0, uvs, v);
        next = (next + 1) % MaxMarks;
        count = Mathf.Min(count + 1, MaxMarks);
        dirty = true;
    }

    /// <summary>Les coordonnées de la case (`column`, `row`) de la texture, à partir du sommet `at`.</summary>
    public static void TileUvs(int column, int row, Vector2[] uvs, int at)
    {
        float w = 1f / TileColumns, h = 1f / TileRows;
        float x = column * w, y = 1f - (row + 1) * h;
        uvs[at] = new Vector2(x, y);
        uvs[at + 1] = new Vector2(x, y + h);
        uvs[at + 2] = new Vector2(x + w, y + h);
        uvs[at + 3] = new Vector2(x + w, y);
    }

    /// <summary>La force d'une marque, de 1 à 0 : `age` est son rang à partir de la plus récente, sur `count` marques posées.
    /// Les plus anciennes, quand elles approchent de MaxMarks, s'effacent. Fonction pure, testée.</summary>
    public static float MarkStrength(int age)
    {
        int left = MaxMarks - age;
        return left >= FadingMarks ? 1f : Mathf.Clamp01((float)left / FadingMarks);
    }

    private void Upload()
    {
        for (int age = 0; age < count; age++)
        {
            int v = ((next - 1 - age + MaxMarks * 2) % MaxMarks) * 4;
            var color = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * MarkStrength(age)));
            colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = color;
        }
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.colors32 = colors;
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
    }

    private void Clear()
    {
        count = next = 0;
        System.Array.Clear(vertices, 0, vertices.Length);
        dirty = true;
    }

    private void LateUpdate()
    {
        // À chaque nouvelle manche, l'arène repart propre : le noir de la fin de manche couvre le changement.
        RoundManager round = RoundManager.Local;
        if (round != null && round.IsSpawned
            && (round.CurrentPhase == RoundManager.Phase.Preparation || round.CurrentPhase == RoundManager.Phase.Starting)
            && round.PhaseStartedAt != seenPhaseStart)
        {
            seenPhaseStart = round.PhaseStartedAt;
            if (count > 0) Clear();
        }
        if (!dirty || mesh == null) return;
        dirty = false;
        Upload();
    }

    // ------------------------------------------------------------------
    // Les étincelles et l'éclat
    // ------------------------------------------------------------------

    private static readonly Color BulletSpark = new Color(1f, 0.74f, 0.36f);

    private ParticleSystem sparks, glows;

    private ParticleSystem MakeParticles(string partName, bool stretched)
    {
        var part = new GameObject(partName);
        part.transform.SetParent(transform, false);
        var system = part.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 256;
        main.gravityModifier = stretched ? 0.7f : 0f;
        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;
        ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var rendered = part.GetComponent<ParticleSystemRenderer>();
        rendered.sharedMaterial = RobotBurst.SparkParticles;
        rendered.shadowCastingMode = ShadowCastingMode.Off;
        rendered.receiveShadows = false;
        rendered.renderMode = stretched ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        rendered.velocityScale = stretched ? 0.03f : 0f;
        rendered.lengthScale = stretched ? 2f : 1f;
        system.Play();
        return system;
    }

    private void Emit(Vector3 point, Vector3 normal, Color color, int count, float speed, float glow)
    {
        var spark = new ParticleSystem.EmitParams { startColor = color };
        for (int i = 0; i < count; i++)
        {
            spark.position = point + normal * 0.02f;
            spark.velocity = (normal + Random.insideUnitSphere * 0.75f).normalized * (speed * Random.Range(0.4f, 1f));
            spark.startSize = Random.Range(0.008f, 0.02f);
            spark.startLifetime = Random.Range(0.12f, 0.32f);
            sparks.Emit(spark, 1);
        }
        var flash = new ParticleSystem.EmitParams
        {
            position = point + normal * 0.03f,
            velocity = Vector3.zero,
            startColor = new Color(color.r, color.g, color.b, 0.85f),
            startSize = glow,
            startLifetime = 0.09f,
        };
        glows.Emit(flash, 1);
    }
}
