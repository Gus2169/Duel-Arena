using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// La tribune (GDD § 9, bible § 8 et § 13) : la passerelle surélevée et fermée qui fait le tour de
/// l'arène, d'où les spectateurs regardent le duel, se battent et lancent ce qu'ils trouvent
/// (2026-10-13). Un NetworkObject de scène, comme le RoundManager.
///
/// Ce qu'elle tient, côté serveur :
/// - les points d'apparition des spectateurs ;
/// - les blocs fissurés (RockSource) : les coups reçus et l'épuisement de chacun, publiés à tous dans
///   une liste réseau ; au dernier coup, un caillou tombe devant le bloc ;
/// - les dons de matériel : pendant une manche, une arme tombe de temps en temps, au hasard, sur la
///   passerelle (GDD § 9 : « tombe au hasard sur leurs plateformes ») ;
/// - le ménage : au début de chaque manche, les objets restés dans l'arène disparaissent (personne ne
///   commence une manche avec un avantage, GDD § 3).
///
/// La passerelle elle-même (sol, garde-corps, mur extérieur, piliers, blocs, points d'apparition) se
/// construit dans l'éditeur à partir du tracé de son garde-corps (menu contextuel du composant) : à
/// refaire pour chaque arène.
/// </summary>
[DisallowMultipleComponent]
public class Tribune : NetworkBehaviour
{
    /// <summary>Un bloc épuisé, dans la liste réseau : il se refend.</summary>
    public const byte Depleted = 255;

    [Header("Objets")]
    [SerializeField] private ArenaItem itemPrefab;
    [SerializeField] private PowerUpCatalog catalog;
    [SerializeField] private SpectatorSounds sounds;
    [SerializeField] private AudioSource blockAudioPrefab;

    [Header("Places")]
    [Tooltip("Où apparaissent les spectateurs : position des pieds, regard vers l'arène.")]
    [SerializeField] private Transform[] spectatorSpawns = System.Array.Empty<Transform>();
    [SerializeField] private RockSource[] rockSources = System.Array.Empty<RockSource>();
    [Tooltip("Le milieu de la passerelle, en boucle fermée : le matériel tombe quelque part dessus.")]
    [SerializeField] private Vector3[] walkwayCenterline = System.Array.Empty<Vector3>();

    [Header("Blocs fissurés (à juger en jouant)")]
    [Tooltip("Coups de poing pour arracher un caillou : « ce qui prend du temps » (GDD § 9).")]
    [SerializeField, Min(1)] private int hitsToBreak = 4;
    [Tooltip("Temps (s) avant qu'un bloc épuisé ne puisse redonner un caillou.")]
    [SerializeField] private float regrowTime = 8f;

    [Header("Dons de matériel (à juger en jouant)")]
    [SerializeField] private Vector2 dropInterval = new Vector2(12f, 20f);
    [Tooltip("Matériel présent au plus sur la tribune (posé ou tenu par un spectateur).")]
    [SerializeField] private int maxPowerUpsOnTribune = 2;
    [Tooltip("Hauteur (m) d'où tombe le matériel, au-dessus de la passerelle.")]
    [SerializeField] private float dropHeight = 6f;

    // Ces réglages ne servent qu'à la construction, dans l'éditeur : la build les croirait inutilisés
    // (CS0414). Ils restent hors du #if UNITY_EDITOR, qui changerait la disposition sérialisée de la
    // scène entre l'éditeur et la build.
#pragma warning disable 0414
    [Header("Construction de la passerelle (éditeur, voir le menu contextuel)")]
    [Tooltip("Le tracé du garde-corps, vu du dessus (x, z), en boucle fermée : le bord intérieur de la passerelle, côté arène.")]
    [SerializeField] private Vector2[] railLine = System.Array.Empty<Vector2>();
    [Tooltip("Hauteur du sol de la passerelle (m). Les murs de l'arène montent à 3 m, un franchissement à 1,3 m : aucun duelliste n'y monte.")]
    [SerializeField] private float floorTop = 4.5f;
    [SerializeField] private float walkwayWidth = 3.5f;
    [SerializeField] private float slabThickness = 0.3f;
    [Tooltip("Hauteur du garde-corps. Au-dessus de la marche d'un CharacterController (0,3 m) et sans franchissement pour les spectateurs : on ne tombe pas dans l'arène. Plus bas que les yeux d'un robot accroupi (1,05 m), qui regarde par-dessus.")]
    [SerializeField] private float railHeight = 1.0f;
    [SerializeField] private float railThickness = 0.12f;
    [SerializeField] private float outerWallHeight = 2.6f;
    [SerializeField] private float outerWallThickness = 0.3f;
    [Tooltip("Un bloc fissuré tous les tant de mètres de mur extérieur (un par côté d'au moins la moitié).")]
    [SerializeField] private float rockSourceSpacing = 20f;
    [Tooltip("Points d'apparition des spectateurs, répartis sur le tour. Au moins la capacité maximale moins deux.")]
    [SerializeField] private int spectatorSpawnCount = 8;
    [SerializeField] private Material floorMaterial;
    [SerializeField] private Material railMaterial;
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material blockMaterial;
#pragma warning restore 0414
    [SerializeField] private Material chunkMaterial;

    private static Tribune cached;

    public static Tribune Instance
    {
        get
        {
            if (cached == null) cached = FindAnyObjectByType<Tribune>();
            return cached;
        }
    }

    public ArenaItem ItemPrefab => itemPrefab;
    public PowerUpCatalog Catalog => catalog;
    public SpectatorSounds Sounds => sounds;
    public float FloorTop => floorTop;
    public int HitsToBreak => hitsToBreak;
    public IReadOnlyList<RockSource> RockSources => rockSources;

    /// <summary>Coups reçus par chaque bloc (0 à hitsToBreak - 1), ou Depleted. Écrite par le serveur.</summary>
    private NetworkList<byte> sourceStates;
    private double[] regrowAt = System.Array.Empty<double>();
    private double nextDropAt;
    private readonly Collider[] overlapBuffer = new Collider[16];
    private AudioSource[] blockAudio = System.Array.Empty<AudioSource>();

    private void Awake()
    {
        // Une liste réseau se crée avant le spawn (Netcode l'exige).
        sourceStates = new NetworkList<byte>();
        for (int i = 0; i < rockSources.Length; i++)
        {
            if (rockSources[i] != null) rockSources[i].Index = i;
        }
    }

    public override void OnNetworkSpawn()
    {
        cached = this;
        if (IsServer)
        {
            sourceStates.Clear();
            for (int i = 0; i < rockSources.Length; i++) sourceStates.Add(0);
            regrowAt = new double[rockSources.Length];
            nextDropAt = 0.0;
        }
        sourceStates.OnListChanged += HandleSourceChanged;
        for (int i = 0; i < sourceStates.Count && i < rockSources.Length; i++) ShowSource(i, false);
    }

    public override void OnNetworkDespawn()
    {
        sourceStates.OnListChanged -= HandleSourceChanged;
    }

    public override void OnDestroy()
    {
        sourceStates?.Dispose();
        base.OnDestroy();
    }

    // ------------------------------------------------------------------
    // Où l'on est
    // ------------------------------------------------------------------

    /// <summary>Ce point est-il sur la tribune (et non dans l'arène) ? Tout ce qui est au-dessus des
    /// murs de l'arène, presque au niveau de la passerelle.</summary>
    public bool IsOnTribune(Vector3 position) => position.y > floorTop - 0.8f;

    /// <summary>
    /// Le point d'apparition d'un spectateur : le plus éloigné des autres spectateurs, comme les points
    /// des duellistes (PlayerSpawnPoints). Faux s'il n'y en a aucun.
    /// </summary>
    public bool TryGetSpectatorSpawn(List<Vector3> occupied, out Vector3 position, out float yaw)
    {
        position = default;
        yaw = 0f;
        Transform best = null;
        float bestDistance = float.NegativeInfinity;
        foreach (Transform point in spectatorSpawns)
        {
            if (point == null) continue;
            float nearest = float.PositiveInfinity;
            if (occupied != null)
            {
                foreach (Vector3 other in occupied) nearest = Mathf.Min(nearest, (point.position - other).sqrMagnitude);
            }
            if (nearest > bestDistance)
            {
                bestDistance = nearest;
                best = point;
            }
        }
        if (best == null) return false;
        position = best.position;
        yaw = best.eulerAngles.y;
        return true;
    }

    // ------------------------------------------------------------------
    // Les blocs fissurés
    // ------------------------------------------------------------------

    /// <summary>Le bloc `index` peut-il donner un caillou (pas épuisé) ? Juste partout.</summary>
    public bool SourceAvailable(int index) => index >= 0 && index < sourceStates.Count && sourceStates[index] != Depleted;

    /// <summary>
    /// Serveur : le bloc fissuré qu'atteint un coup de poing donné depuis `origin` vers `direction`
    /// (le plus proche, devant soi, sans mur entre l'œil et lui), ou null. `distance` : de l'œil au bloc.
    /// </summary>
    public RockSource QueryRockSource(Vector3 origin, Vector3 direction, float range, float radius, LayerMask worldMask, out float distance, out Vector3 point)
    {
        distance = float.MaxValue;
        point = origin;
        RockSource best = null;
        direction.Normalize();
        int count = Physics.OverlapCapsuleNonAlloc(origin, origin + direction * range, radius, overlapBuffer, worldMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = overlapBuffer[i];
            RockSource source = c != null ? c.GetComponentInParent<RockSource>() : null;
            if (source == null || source.Index < 0) continue;
            Vector3 p = c.ClosestPoint(origin);
            Vector3 to = p - origin;
            if (Vector3.Dot(to, direction) < -0.1f) continue;
            float d = to.magnitude;
            if (d >= distance) continue;
            // Un mur entre l'œil et le bloc (arrêté juste avant sa surface, qui compte pour lui).
            if (d > 0.05f && Physics.Linecast(origin, origin + to * 0.95f, out RaycastHit wall, worldMask, QueryTriggerInteraction.Ignore)
                && wall.collider.GetComponentInParent<RockSource>() != source) continue;
            best = source;
            distance = d;
            point = p;
        }
        return best;
    }

    /// <summary>
    /// Serveur : un coup de poing atteint le bloc `source`. Il se fend un peu plus ; au dernier coup, il
    /// cède, un caillou tombe devant lui, et le bloc se refend pendant `regrowTime`. Renvoie vrai si le
    /// coup a porté (un bloc épuisé ne donne rien).
    /// </summary>
    public bool ServerHitSource(RockSource source)
    {
        if (!IsServer || source == null) return false;
        int index = source.Index;
        if (index < 0 || index >= sourceStates.Count) return false;
        byte hits = sourceStates[index];
        if (hits == Depleted) return false;

        hits++;
        if (hits < hitsToBreak)
        {
            sourceStates[index] = hits;
            return true;
        }

        sourceStates[index] = Depleted;
        regrowAt[index] = NetworkManager.ServerTime.Time + regrowTime;
        // Le morceau tombe du bloc vers la passerelle : un caillou « lâché », qui se pose sans frapper.
        Vector3 start = source.transform.position + source.transform.forward * 0.45f + Vector3.up * 0.25f;
        Vector3 velocity = (source.DropPosition - start).normalized * 1.5f + Vector3.up * 1.5f;
        ArenaItem.ServerSpawn(itemPrefab, ArenaItemKind.Rock, 0, start, velocity, ulong.MaxValue, true);
        return true;
    }

    private void HandleSourceChanged(NetworkListEvent<byte> change)
    {
        if (change.Type != NetworkListEvent<byte>.EventType.Value && change.Type != NetworkListEvent<byte>.EventType.Add) return;
        int index = change.Index;
        if (index < 0 || index >= rockSources.Length) return;
        bool struck = change.Type == NetworkListEvent<byte>.EventType.Value
                      && (change.Value == Depleted || change.Value > change.PreviousValue);
        ShowSource(index, struck);
    }

    private void ShowSource(int index, bool struck)
    {
        RockSource source = rockSources[index];
        if (source == null || index >= sourceStates.Count) return;
        byte state = sourceStates[index];
        source.Show(state == Depleted ? hitsToBreak : state, hitsToBreak, state == Depleted);
        if (!struck || sounds == null) return;
        // Catégorie B : le serveur a décidé du coup ; chacun l'entend là où est le bloc.
        AudioSource audio = BlockAudio(index);
        if (state == Depleted) sounds.blockBreak.PlayOn(audio);
        else sounds.blockHit.PlayOn(audio);
    }

    private AudioSource BlockAudio(int index)
    {
        if (blockAudio.Length != rockSources.Length) blockAudio = new AudioSource[rockSources.Length];
        if (blockAudio[index] != null || blockAudioPrefab == null) return blockAudio[index];
        blockAudio[index] = Instantiate(blockAudioPrefab, rockSources[index].transform, false);
        return blockAudio[index];
    }

    // ------------------------------------------------------------------
    // Le serveur : blocs qui se refendent, matériel qui tombe
    // ------------------------------------------------------------------

    private void Update()
    {
        if (!IsServer || !IsSpawned) return;
        double now = NetworkManager.ServerTime.Time;
        for (int i = 0; i < sourceStates.Count && i < regrowAt.Length; i++)
        {
            if (sourceStates[i] == Depleted && now >= regrowAt[i]) sourceStates[i] = 0;
        }
        ServerUpdateDrops(now);
    }

    private void ServerUpdateDrops(double now)
    {
        RoundManager rm = RoundManager.Instance;
        bool active = rm != null && rm.IsSpawned && rm.Rules.spectatorInterventions
                      && SpectatorRules.DropsAllowed(rm.CurrentPhase) && AnySpectator() && catalog != null;
        if (!active)
        {
            nextDropAt = 0.0;
            return;
        }
        if (nextDropAt <= 0.0) nextDropAt = now + Random.Range(dropInterval.x, dropInterval.y);
        if (now < nextDropAt) return;
        nextDropAt = now + Random.Range(dropInterval.x, dropInterval.y);
        if (PowerUpsOnTribune() >= maxPowerUpsOnTribune) return;
        ServerDropPowerUp(Random.value, Random.value, Random.value);
    }

    /// <summary>Serveur : fait tomber du matériel sur la passerelle (tirage avec `rolls`, dans [0, 1[).
    /// Renvoie l'objet, ou null.</summary>
    public ArenaItem ServerDropPowerUp(float variantRoll, float placeRoll, float sideRoll)
    {
        if (!IsServer || catalog == null || walkwayCenterline.Length < 2) return null;
        int variant = catalog.PickWeighted(variantRoll);
        if (variant < 0) return null;
        Vector3 point = WalkwayPoint(placeRoll, sideRoll);
        return ArenaItem.ServerSpawn(itemPrefab, ArenaItemKind.PowerUp, variant, point + Vector3.up * dropHeight,
                                     Vector3.zero, ulong.MaxValue, true);
    }

    /// <summary>Un point de la passerelle : à `along` (0 à 1) de son tour, décalé sur le côté selon
    /// `side` (0 à 1), loin des bords. Le tour se mesure en longueur : chaque mètre a ses chances.</summary>
    public Vector3 WalkwayPoint(float along, float side)
    {
        int n = walkwayCenterline.Length;
        float total = 0f;
        for (int i = 0; i < n; i++) total += Vector3.Distance(walkwayCenterline[i], walkwayCenterline[(i + 1) % n]);
        float target = Mathf.Clamp01(along) * total;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = walkwayCenterline[i];
            Vector3 b = walkwayCenterline[(i + 1) % n];
            float length = Vector3.Distance(a, b);
            if (target > length && i < n - 1)
            {
                target -= length;
                continue;
            }
            Vector3 along3 = length > 1e-4f ? (b - a) / length : Vector3.forward;
            Vector3 across = Vector3.Cross(Vector3.up, along3);
            float lateral = (Mathf.Clamp01(side) * 2f - 1f) * Mathf.Max(0f, walkwayWidth * 0.5f - 0.7f);
            return a + along3 * Mathf.Clamp(target, 0f, length) + across * lateral;
        }
        return walkwayCenterline[0];
    }

    private static bool AnySpectator()
    {
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && state.IsSpectator) return true;
        }
        return false;
    }

    /// <summary>Le matériel déjà sur la tribune : posé, en train d'y tomber, ou en main d'un spectateur.</summary>
    private int PowerUpsOnTribune()
    {
        int count = 0;
        foreach (ArenaItem item in ArenaItem.Spawned)
        {
            if (item != null && item.Kind == ArenaItemKind.PowerUp && item.CurrentPhase != ArenaItem.Phase.Shattered
                && IsOnTribune(item.transform.position)) count++;
        }
        foreach (SpectatorController spectator in SpectatorController.Spawned)
        {
            if (spectator != null && spectator.Carried == ArenaItemKind.PowerUp) count++;
        }
        return count;
    }

    /// <summary>Serveur, au début de chaque manche : les objets restés dans l'arène disparaissent.
    /// Ceux de la tribune restent : le jeu des spectateurs continue d'une manche à l'autre.</summary>
    public void ServerClearArena()
    {
        if (!IsServer) return;
        var doomed = new List<ArenaItem>();
        foreach (ArenaItem item in ArenaItem.Spawned)
        {
            if (item != null && !IsOnTribune(item.transform.position)) doomed.Add(item);
        }
        foreach (ArenaItem item in doomed) item.ServerRemove();
    }

#if UNITY_EDITOR
    // ------------------------------------------------------------------
    // Construction de la passerelle dans l'éditeur
    // ------------------------------------------------------------------

    private const string GeneratedRootName = "Passerelle (générée)";

    /// <summary>
    /// Construit la passerelle à partir du tracé du garde-corps : le sol (un anneau, son maillage
    /// enregistré à côté de la scène), le garde-corps, le mur extérieur qui la ferme, des piliers, un
    /// bloc fissuré par côté (deux sur les grands côtés), et un point d'apparition par côté. Tout ce qui
    /// avait été généré avant est remplacé.
    /// </summary>
    [ContextMenu("Construire la passerelle")]
    public void EditorBuildWalkway()
    {
        if (railLine == null || railLine.Length < 3)
        {
            Debug.LogError("Tribune : il faut au moins trois points dans le tracé du garde-corps.", this);
            return;
        }

        Transform old = transform.Find(GeneratedRootName);
        if (old != null) UnityEditor.Undo.DestroyObjectImmediate(old.gameObject);
        var root = new GameObject(GeneratedRootName);
        UnityEditor.Undo.RegisterCreatedObjectUndo(root, "Construire la passerelle");
        root.transform.SetParent(transform, false);

        int n = railLine.Length;
        Vector3[] inner = new Vector3[n];
        for (int i = 0; i < n; i++) inner[i] = new Vector3(railLine[i].x, floorTop, railLine[i].y);
        Vector3[] outer = OffsetPolygon(inner, walkwayWidth);

        BuildFloor(root.transform, inner, outer);

        var sources = new List<RockSource>();
        var centerline = new Vector3[n];
        float pillarHeight = floorTop - slabThickness;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 a = inner[i], b = inner[j];
            Vector3 along = (b - a).normalized;
            Vector3 outward = Outward(inner, i);
            float length = Vector3.Distance(a, b);
            centerline[i] = (inner[i] + outer[i]) * 0.5f;

            // Le garde-corps, côté arène, et le mur extérieur. Chacun déborde d'une épaisseur à ses deux
            // bouts : les coins se referment sans laisser de jour.
            Box(root.transform, $"Garde-corps {i + 1}", Vector3.Lerp(a, b, 0.5f) + Vector3.up * (railHeight * 0.5f) + outward * (railThickness * 0.5f),
                along, length + railThickness, railHeight, railThickness, railMaterial);
            Vector3 oa = outer[i], ob = outer[j];
            float outerLength = Vector3.Distance(oa, ob);
            Box(root.transform, $"Mur extérieur {i + 1}", Vector3.Lerp(oa, ob, 0.5f) + Vector3.up * (outerWallHeight * 0.5f) - outward * (outerWallThickness * 0.5f),
                along, outerLength + outerWallThickness, outerWallHeight, outerWallThickness, wallMaterial);

            // Un pilier sous chaque grand côté : la passerelle se lit comme un balcon, pas comme une dalle
            // qui flotte.
            if (length >= 6f)
            {
                Vector3 pillar = Vector3.Lerp(oa, ob, 0.5f) - outward * 0.6f;
                Box(root.transform, $"Pilier {i + 1}", new Vector3(pillar.x, pillarHeight * 0.5f, pillar.z),
                    along, 0.6f, pillarHeight, 0.6f, wallMaterial);
            }

            // Les blocs fissurés, contre le mur extérieur : un sur chaque grand côté (au moins 10 m).
            int blockCount = Mathf.FloorToInt(length / rockSourceSpacing + 0.5f);
            for (int k = 0; k < blockCount; k++)
            {
                float t = (k + 0.5f) / blockCount;
                Vector3 basePoint = Vector3.Lerp(a, b, t) + outward * (walkwayWidth - outerWallThickness);
                sources.Add(BuildRockSource(root.transform, $"Bloc fissuré {sources.Count + 1}", basePoint, outward));
            }
        }

        // Les points d'apparition, régulièrement espacés sur le tour, au milieu de la passerelle, regard
        // vers l'arène.
        var spawns = new List<Transform>();
        float perimeter = 0f;
        for (int i = 0; i < n; i++) perimeter += Vector3.Distance(centerline[i], centerline[(i + 1) % n]);
        for (int k = 0; k < spectatorSpawnCount; k++)
        {
            float s = (k + 0.25f) * perimeter / spectatorSpawnCount;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = centerline[i], b = centerline[(i + 1) % n];
                float length = Vector3.Distance(a, b);
                if (s > length && i < n - 1)
                {
                    s -= length;
                    continue;
                }
                var spawn = new GameObject($"Apparition spectateur {k + 1}").transform;
                spawn.SetParent(root.transform, false);
                spawn.position = Vector3.Lerp(a, b, Mathf.Clamp01(s / Mathf.Max(0.01f, length)));
                spawn.rotation = Quaternion.LookRotation(-Outward(inner, i), Vector3.up);
                spawns.Add(spawn);
                break;
            }
        }

        spectatorSpawns = spawns.ToArray();
        rockSources = sources.ToArray();
        walkwayCenterline = centerline;
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        Debug.Log($"Tribune : passerelle construite ({n} côtés, {rockSources.Length} blocs fissurés, {spectatorSpawns.Length} points d'apparition).", this);
    }

    /// <summary>La normale d'un côté, vers l'extérieur de l'arène (à l'opposé du centre du tracé).</summary>
    private static Vector3 Outward(Vector3[] polygon, int i)
    {
        Vector3 center = Vector3.zero;
        foreach (Vector3 p in polygon) center += p;
        center /= polygon.Length;
        Vector3 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
        Vector3 along = (b - a).normalized;
        Vector3 normal = Vector3.Cross(Vector3.up, along);
        if (Vector3.Dot(normal, (a + b) * 0.5f - center) < 0f) normal = -normal;
        return normal;
    }

    /// <summary>Le tracé décalé de `distance` vers l'extérieur, angles en onglet.</summary>
    private static Vector3[] OffsetPolygon(Vector3[] polygon, float distance)
    {
        int n = polygon.Length;
        var result = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            int previous = (i + n - 1) % n;
            Vector3 n1 = Outward(polygon, previous);
            Vector3 n2 = Outward(polygon, i);
            Vector3 bisector = (n1 + n2).normalized;
            float cos = Mathf.Max(0.2f, Vector3.Dot(bisector, n2));
            result[i] = polygon[i] + bisector * (distance / cos);
        }
        return result;
    }

    private void BuildFloor(Transform parent, Vector3[] inner, Vector3[] outer)
    {
        int n = inner.Length;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        Vector3 down = Vector3.down * slabThickness;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 outward = Outward(inner, i);
            // Dessus, dessous, chant côté arène, chant extérieur : quatre faces par côté.
            Face(vertices, triangles, inner[i], inner[j], outer[j], outer[i], Vector3.up);
            Face(vertices, triangles, inner[i] + down, inner[j] + down, outer[j] + down, outer[i] + down, Vector3.down);
            Face(vertices, triangles, inner[i], inner[j], inner[j] + down, inner[i] + down, -outward);
            Face(vertices, triangles, outer[i], outer[j], outer[j] + down, outer[i] + down, outward);
        }

        var mesh = new Mesh { name = "Passerelle_Sol" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        string scenePath = gameObject.scene.path;
        string folder = string.IsNullOrEmpty(scenePath) ? "Assets" : System.IO.Path.GetDirectoryName(scenePath).Replace('\\', '/');
        string assetPath = $"{folder}/Passerelle_Sol_{gameObject.scene.name}.asset";
        Mesh existing = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existing != null)
        {
            existing.Clear();
            UnityEditor.EditorUtility.CopySerialized(mesh, existing);
            mesh = existing;
        }
        else
        {
            UnityEditor.AssetDatabase.CreateAsset(mesh, assetPath);
        }
        UnityEditor.AssetDatabase.SaveAssets();

        var floor = new GameObject("Sol de la passerelle");
        floor.transform.SetParent(parent, false);
        floor.AddComponent<MeshFilter>().sharedMesh = mesh;
        floor.AddComponent<MeshRenderer>().sharedMaterial = floorMaterial;
        floor.AddComponent<MeshCollider>().sharedMesh = mesh;
        floor.isStatic = true;
    }

    /// <summary>Ajoute le quadrilatère (a, b, c, d), visible du côté `visibleSide`, avec ses propres
    /// sommets pour des normales franches. Unity montre la face d'un triangle (a, b, c) du côté de
    /// Cross(b - a, c - a) : on retourne le quadrilatère s'il regarde de l'autre côté.</summary>
    private static void Face(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 visibleSide)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), visibleSide) < 0f)
            triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        else
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
    }

    /// <summary>Une boîte de `length` le long de `along`, de `height` de haut et `thickness` d'épaisseur,
    /// centrée sur `center` : un cube de Unity, avec son collider (c'est du décor).</summary>
    private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 along, float length, float height, float thickness, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.SetPositionAndRotation(center, Quaternion.LookRotation(along, Vector3.up));
        box.transform.localScale = new Vector3(thickness, height, length);
        if (material != null) box.GetComponent<MeshRenderer>().sharedMaterial = material;
        box.isStatic = true;
        return box;
    }

    private RockSource BuildRockSource(Transform parent, string name, Vector3 wallBase, Vector3 outward)
    {
        const float width = 1.1f, height = 0.9f, depth = 0.6f;
        Vector3 inward = -outward;
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = wallBase + inward * (depth * 0.5f);
        root.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);

        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "Bloc";
        block.transform.SetParent(root.transform, false);
        block.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        block.transform.localScale = new Vector3(width, height, depth);
        if (blockMaterial != null) block.GetComponent<MeshRenderer>().sharedMaterial = blockMaterial;
        block.isStatic = true;

        // Le morceau qui se détache : sans collider, il n'est qu'une apparence.
        GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chunk.name = "Morceau";
        Object.DestroyImmediate(chunk.GetComponent<Collider>());
        chunk.transform.SetParent(root.transform, false);
        chunk.transform.localPosition = new Vector3(0.12f, height * 0.62f, depth * 0.5f + 0.02f);
        chunk.transform.localRotation = Quaternion.Euler(8f, -12f, 6f);
        chunk.transform.localScale = new Vector3(0.42f, 0.3f, 0.16f);
        if (chunkMaterial != null) chunk.GetComponent<MeshRenderer>().sharedMaterial = chunkMaterial;

        var drop = new GameObject("Chute du caillou").transform;
        drop.SetParent(root.transform, false);
        drop.localPosition = new Vector3(0f, 0.15f, depth * 0.5f + 0.75f);

        var source = root.AddComponent<RockSource>();
        var so = new UnityEditor.SerializedObject(source);
        so.FindProperty("chunk").objectReferenceValue = chunk.transform;
        so.FindProperty("dropPoint").objectReferenceValue = drop;
        so.FindProperty("body").objectReferenceValue = block.GetComponent<Collider>();
        so.ApplyModifiedPropertiesWithoutUndo();
        return source;
    }
#endif
}
