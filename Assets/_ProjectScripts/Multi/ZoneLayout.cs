using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Les emplacements de la Zone dans une arène (GDD § 11, T8 : « chaque arène reçoit ses repères »), dans leur ordre, et
/// ce que chaque machine en montre (catégorie C) : la zone du moment, un anneau au sol et une paroi de lumière (presque
/// effacée vue de dedans, pour ne pas teindre l'arène), à la couleur de qui la tient (pâle libre, blanche et battante
/// disputée), qui grésille quand on la tient (arcs et sons,
/// Z7) ; la suivante, un anneau qui bat, les dernières secondes avant le changement. Le serveur y lit aussi qui est
/// dedans (RoundManager). Posé par Duel Arena > Arène > Poser les zones (ZoneLayoutBuilder) : on corrige le script,
/// pas la scène.
/// </summary>
public class ZoneLayout : MonoBehaviour
{
    [Tooltip("Les emplacements, dans l'ordre où la zone les parcourt (le premier à égale distance des deux postes).")]
    [SerializeField] private Transform[] zones = System.Array.Empty<Transform>();
    [Tooltip("Le nom de chaque emplacement, dans les annonces (« Prochaine : Flanc est »).")]
    [SerializeField] private string[] zoneNames = System.Array.Empty<string>();
    [Tooltip("Le rayon (m) de la zone.")]
    [SerializeField] private float radius = 4f;
    [Tooltip("L'anneau et la paroi : une lumière ajoutée (URP Particles/Unlit, additif, les deux faces).")]
    [SerializeField] private Material zoneMaterial;
    [Tooltip("Les arcs quand on la tient : ceux du gant (DuelArena/ArcElectrique).")]
    [SerializeField] private Material arcMaterial;
    [SerializeField] private GameSounds sounds;
    [Tooltip("Hauteur (m) de la paroi, qui s'efface vers le haut.")]
    [SerializeField] private float wallHeight = 2.2f;
    [Tooltip("Éclat de la zone libre, tenue, disputée (au plus fort de son battement), et de la suivante.")]
    [SerializeField] private float freeIntensity = 0.45f, heldIntensity = 1.1f, contestedIntensity = 1.3f, nextIntensity = 0.35f;
    [Tooltip("Vue de dedans, la paroi entoure tout le champ et teindrait l'arène (et l'adversaire) : elle n'y garde que " +
             "cette part de son éclat, et la retrouve en entier à tant de mètres du bord (m). Le jeu se rend en espace " +
             "linéaire : sur un sol sombre, une lueur ajoutée se voit bien plus que son chiffre.")]
    [SerializeField] private float wallInsideFade = 0.1f, wallFadeDistance = 6f;

    /// <summary>L'opacité du pied de la paroi (elle s'efface vers le haut) : assez pour se voir de loin, pas assez pour
    /// voiler un robot dedans.</summary>
    private const float WallOpacity = 0.3f;

    private static readonly Color Pale = new Color(0.82f, 0.9f, 1f);
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private static ZoneLayout instance;

    /// <summary>Les zones de l'arène ouverte, ou null (une scène sans Zone).</summary>
    public static ZoneLayout Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<ZoneLayout>();
            return instance;
        }
    }

    public int Count => zones.Length;
    public float Radius => radius;
    public Vector3 Center(int index) => zones[index].position;

    /// <summary>Le nom d'un emplacement, dans les annonces.</summary>
    public string NameOf(int index) => index >= 0 && index < zoneNames.Length && !string.IsNullOrEmpty(zoneNames[index]) ? zoneNames[index] : $"Zone {index + 1}";

    /// <summary>Un robot dont les pieds sont en `feet` est-il dans la zone `index` ?</summary>
    public bool Contains(int index, Vector3 feet) => index >= 0 && index < zones.Length && zones[index] != null
                                                     && ZoneMode.Contains(zones[index].position, radius, feet);

    // ------------------------------------------------------------------
    // Ce que chaque machine montre
    // ------------------------------------------------------------------

    private Renderer[] rings;
    private Renderer[] walls;
    private ArcLines arcs;
    [System.NonSerialized] private MaterialPropertyBlock block;
    private float nextCrackle;

    private void Awake()
    {
        if (!Application.isPlaying || zoneMaterial == null) return;
        Mesh ring = RingMesh(radius - 0.12f, radius, 72);
        Mesh wall = WallMesh(radius, wallHeight, 72);
        rings = new Renderer[zones.Length];
        walls = new Renderer[zones.Length];
        for (int i = 0; i < zones.Length; i++)
        {
            rings[i] = Part(zones[i], "Anneau", ring);
            walls[i] = Part(zones[i], "Paroi", wall);
        }
        if (arcMaterial != null) arcs = new ArcLines(transform, "Arcs de la zone", arcMaterial, 0, new[] { 3, 3, 3, 3 }, null);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        arcs?.Destroy();
    }

    private void LateUpdate()
    {
        if (rings == null) return;
        RoundManager rm = RoundManager.Instance;
        bool live = rm != null && rm.IsSpawned;
        int shown = live ? rm.ShownZone : -1;
        int next = live ? rm.AnnouncedZone : -1;
        Camera viewer = Camera.main;
        for (int i = 0; i < zones.Length; i++)
        {
            bool active = i == shown;
            bool announced = i == next && !active;
            rings[i].enabled = active || announced;
            walls[i].enabled = active;
            if (active)
            {
                float fade = 1f;
                if (viewer != null)
                {
                    Vector3 offset = viewer.transform.position - zones[i].position;
                    fade = WallFade(new Vector2(offset.x, offset.z).magnitude, radius, wallInsideFade, wallFadeDistance);
                }
                Tint(i, ZoneColor(rm), fade);
            }
            else if (announced) Tint(i, Pale * (nextIntensity * (0.6f + 0.4f * Mathf.Sin(Time.time * 2f * Mathf.PI))), 1f);
        }
        Crackle(rm, shown);
    }

    /// <summary>
    /// La part de son éclat que garde la paroi pour qui la regarde à `distance` mètres (à plat) du centre d'une zone de
    /// rayon `radius` : `insideFade` de dedans, puis de plus en plus jusqu'à tout son éclat à `fadeDistance` mètres du
    /// bord. Fonction pure, testée.
    /// </summary>
    public static float WallFade(float distance, float radius, float insideFade, float fadeDistance)
    {
        return Mathf.Lerp(insideFade, 1f, Mathf.InverseLerp(radius, radius + Mathf.Max(0.01f, fadeDistance), distance));
    }

    /// <summary>La couleur de la zone du moment : celle de qui la tient, pâle libre, blanche et battante disputée.</summary>
    private Color ZoneColor(RoundManager rm)
    {
        if (rm.ZoneContested) return Color.white * (contestedIntensity * (0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.time * 2f * Mathf.PI * 1.5f))));
        if (rm.ZoneHolder != RoundManager.NoWinner) return PaintOf(rm.ZoneHolder) * heldIntensity;
        return Pale * freeIntensity;
    }

    /// <summary>L'anneau à `color`, la paroi aussi, n'en gardant que `wallFade` (une lumière ajoutée : son alpha la
    /// dose).</summary>
    private void Tint(int index, Color color, float wallFade)
    {
        block ??= new MaterialPropertyBlock();
        block.SetColor(BaseColorId, color);
        rings[index].SetPropertyBlock(block);
        color.a *= wallFade;
        block.SetColor(BaseColorId, color);
        walls[index].SetPropertyBlock(block);
    }

    /// <summary>Tenue, la zone grésille : quatre arcs qui sautent le long de l'anneau, quinze fois par seconde, et le
    /// bruit d'une décharge de temps en temps, là où ils sautent (entendu de tous : quelqu'un tient la zone).</summary>
    private void Crackle(RoundManager rm, int shown)
    {
        bool held = shown >= 0 && rm.CurrentPhase == RoundManager.Phase.Active && rm.ZoneHolder != RoundManager.NoWinner && !rm.ZoneContested;
        if (!held)
        {
            arcs?.HideAll();
            return;
        }
        Vector3 center = zones[shown].position;
        Camera viewer = Camera.main;
        uint seed = ElectricArc.Hash((uint)(shown + 1) * 0x9e3779b9U ^ (uint)Mathf.FloorToInt(Time.time / 0.065f));
        Vector3 spark = center;
        for (int i = 0; arcs != null && i < arcs.Count; i++)
        {
            uint s = ElectricArc.Hash(seed + (uint)(i + 1) * 0x632be5abU);
            float from = ElectricArc.Next01(ref s) * 2f * Mathf.PI;
            float to = from + (0.15f + 0.35f * ElectricArc.Next01(ref s)) * (ElectricArc.Next01(ref s) < 0.5f ? -1f : 1f);
            Vector3 a = center + new Vector3(Mathf.Cos(from) * radius, 0.05f, Mathf.Sin(from) * radius);
            Vector3 b = center + new Vector3(Mathf.Cos(to) * radius, 0.05f, Mathf.Sin(to) * radius);
            Vector3 bow = Vector3.up * (0.2f + 0.35f * ElectricArc.Next01(ref s));
            ElectricArc.Build(a, b, 3, 0.3f, bow, ref s, arcs.Points(i));
            float width = Mathf.Max(0.01f, viewer != null ? Vector3.Distance(viewer.transform.position, a) * 0.0022f : 0.02f);
            float force = 0.6f + 0.4f * ElectricArc.Next01(ref s);
            arcs.Show(i, width, force, force);
            if (i == 0) spark = a;
        }
        if (sounds == null || Time.time < nextCrackle) return;
        nextCrackle = Time.time + Random.Range(0.45f, 0.9f);
        sounds.zoneCrackle?.PlayAt(spark, sounds.zoneCrackleMinDistance, sounds.zoneCrackleMaxDistance);
    }

    private static Color PaintOf(ulong clientId)
    {
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player == null || player.OwnerClientId != clientId) continue;
            if (player.TryGetComponent(out PlayerPaint paint) && paint.Paint.a > 0) return paint.Paint;
        }
        return Pale;
    }

    private Renderer Part(Transform zone, string name, Mesh mesh)
    {
        var part = new GameObject(name);
        part.transform.SetParent(zone, false);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = zoneMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.enabled = false;
        return renderer;
    }

    /// <summary>L'anneau au sol : une couronne de `inner` à `outer` mètres, à 3 cm du sol.</summary>
    private static Mesh RingMesh(float inner, float outer, int segments)
    {
        var vertices = new Vector3[segments * 2];
        var colors = new Color[segments * 2];
        var triangles = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 2f * Mathf.PI / segments;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = direction * inner + Vector3.up * 0.03f;
            vertices[i * 2 + 1] = direction * outer + Vector3.up * 0.03f;
            colors[i * 2] = colors[i * 2 + 1] = Color.white;
            int next = (i + 1) % segments;
            int t = i * 6;
            triangles[t] = i * 2; triangles[t + 1] = next * 2; triangles[t + 2] = i * 2 + 1;
            triangles[t + 3] = i * 2 + 1; triangles[t + 4] = next * 2; triangles[t + 5] = next * 2 + 1;
        }
        var mesh = new Mesh { name = "Zone_Anneau", vertices = vertices, colors = colors, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>La paroi : un cylindre ouvert de `height` mètres, qui s'efface vers le haut (l'alpha des sommets, en
    /// (1 - h)² sur cinq rangs : un fondu linéaire laissait une ligne nette en haut, une lueur ajoutée se voyant fort
    /// près du noir).</summary>
    private static Mesh WallMesh(float radius, float height, int segments)
    {
        const int rows = 5;
        var vertices = new Vector3[segments * rows];
        var colors = new Color[segments * rows];
        var triangles = new int[segments * (rows - 1) * 6];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 2f * Mathf.PI / segments;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            for (int r = 0; r < rows; r++)
            {
                float up = (float)r / (rows - 1);
                vertices[i * rows + r] = direction + Vector3.up * Mathf.Lerp(0.03f, height, up);
                colors[i * rows + r] = new Color(1f, 1f, 1f, WallOpacity * (1f - up) * (1f - up));
            }
            int next = (i + 1) % segments;
            for (int r = 0; r < rows - 1; r++)
            {
                int t = (i * (rows - 1) + r) * 6;
                int a = i * rows + r, b = next * rows + r;
                triangles[t] = a; triangles[t + 1] = a + 1; triangles[t + 2] = b;
                triangles[t + 3] = b; triangles[t + 4] = a + 1; triangles[t + 5] = b + 1;
            }
        }
        var mesh = new Mesh { name = "Zone_Paroi", vertices = vertices, colors = colors, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDrawGizmos()
    {
        for (int i = 0; i < zones.Length; i++)
        {
            if (zones[i] == null) continue;
            Gizmos.color = i == 0 ? new Color(1f, 0.85f, 0.2f) : new Color(0.4f, 0.8f, 1f);
            Vector3 c = zones[i].position + Vector3.up * 0.05f;
            for (int k = 0; k < 48; k++)
            {
                float a0 = k * 2f * Mathf.PI / 48f, a1 = (k + 1) * 2f * Mathf.PI / 48f;
                Gizmos.DrawLine(c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius, c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius);
            }
        }
    }
}
