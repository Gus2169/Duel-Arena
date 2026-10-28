using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Habille l'arène (2026-10-27, demandé par l'utilisateur) : Duel Arena > Arène > Habiller l'arène, sur la scène ouverte.
/// On corrige ce script, pas la scène : il refait tout ce qu'il a construit.
///
/// Ce qu'il fait, sans toucher au jeu :
/// - **le blocking reste la vérité du jeu** (tranché par l'utilisateur : on garde le layout). Chaque bloc ProBuilder
///   garde son collider ; seul son rendu s'éteint, remplacé par un bloc habillé de la même enveloppe (arêtes
///   chanfreinées de quelques centimètres, matériau selon son rôle : béton banché, tunnel, barricade d'essai, acier
///   peint, caisse). Les escaliers gardent leur maillage, sous la tôle striée.
/// - le sol : une dalle de béton à la place du terrain (désactivé, pas supprimé), et son collider ;
/// - la salle souterraine (bible § 3 et § 13 : une salle de test balistique, choisie le 2026-10-27) : ses murs sur le
///   tracé extérieur de la tribune, ses pilastres, son plafond et sa charpente rayonnante ;
/// - la tribune : ses pièces prennent les matériaux du décor ;
/// - la lumière (« projecteurs, pénombre », choisi le 2026-10-27) : voir ArenaLighting.
///
/// Les maillages construits vont dans `<scène>_Decor.asset`, à côté de la scène (en LFS).
/// </summary>
public static class ArenaDecorBuilder
{
    public const string DecorRootName = "Décor (généré)";
    /// <summary>Hauteur du plafond de la salle (m).</summary>
    public const float CeilingHeight = 15f;
    private const float HallWallThickness = 0.6f;

    /// <summary>Le masque des calques de rendu du décor de la salle et de la tribune : la rampe générale ne les éclaire
    /// pas, ils restent dans la pénombre (ArenaLighting).</summary>
    public const uint HallLayerMask = 1u << 1;
    public const uint ArenaLayerMask = 1u;

    [MenuItem("Duel Arena/Arène/Habiller l'arène")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Décor] Sortir du Play Mode d'abord.");
            return;
        }
        Debug.Log("[Décor] " + Build());
    }

    /// <summary>Cuit la lumière indirecte de l'arène habillée (les lumières mixtes, en tâche de fond) puis la sonde de
    /// reflets. Quelques minutes ; à refaire après chaque habillage.</summary>
    [MenuItem("Duel Arena/Arène/Cuire la lumière")]
    public static void BakeFromMenu()
    {
        if (!Lightmapping.BakeAsync()) Debug.LogWarning("[Décor] La cuisson n'a pas démarré.");
        else Lightmapping.bakeCompleted += AfterBake;
    }

    private static void AfterBake()
    {
        Lightmapping.bakeCompleted -= AfterBake;
        foreach (ReflectionProbe probe in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude))
        {
            if (probe.mode == UnityEngine.Rendering.ReflectionProbeMode.Baked && probe.bakedTexture != null)
                Lightmapping.BakeReflectionProbe(probe, AssetDatabase.GetAssetPath(probe.bakedTexture));
        }
        Debug.Log("[Décor] Lumière cuite.");
    }

    [MenuItem("Duel Arena/Arène/Montrer le blocking")]
    public static void ShowBlocking() => SetBlockingVisible(true);

    [MenuItem("Duel Arena/Arène/Montrer le décor")]
    public static void ShowDecor() => SetBlockingVisible(false);

    /// <summary>Habille la scène active. Rend un compte rendu.</summary>
    public static string Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        Tribune tribune = FindInScene<Tribune>(scene);
        if (tribune == null) throw new System.InvalidOperationException("Pas de Tribune dans la scène active : ouvrir l'arène d'abord.");
        var report = new StringBuilder();
        Dictionary<string, Material> materials = DecorMaterials.CreateAll();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == DecorRootName) Undo.DestroyObjectImmediate(root);
        }
        var decor = new GameObject(DecorRootName);
        SceneManager.MoveGameObjectToScene(decor, scene);
        Undo.RegisterCreatedObjectUndo(decor, "Habiller l'arène");
        var bank = new DecorMeshes.Bank(MeshBankPath(scene));

        DressBlocking(scene, tribune, decor.transform, materials, bank, report);
        HallGeometry hall = HallGeometry.Of(tribune);
        BuildFloor(scene, decor.transform, hall, materials, bank, report);
        BuildHall(decor.transform, hall, materials, bank, report);
        DressTribune(tribune, materials, report);
        ArenaMarkings.Build(decor.transform, hall, bank, report);
        ArenaLighting.Build(scene, hall, materials, bank, report);

        bank.Save();
        EditorSceneManager.MarkSceneDirty(scene);
        report.Insert(0, $"Arène habillée : {bank.Count} maillages dans {MeshBankPath(scene)}.\n");
        return report.ToString();
    }

    public static string MeshBankPath(Scene scene)
    {
        string folder = string.IsNullOrEmpty(scene.path) ? "Assets" : System.IO.Path.GetDirectoryName(scene.path).Replace('\\', '/');
        return $"{folder}/{scene.name}_Decor.asset";
    }

    // ------------------------------------------------------------------
    // Le blocking
    // ------------------------------------------------------------------

    /// <summary>Ce qu'un bloc devient : son matériau (et celui de son dessus), son chanfrein, ou son maillage gardé.</summary>
    private sealed class Role
    {
        public string material;
        public string top;
        public float chamfer = 0.04f;
        public bool keepMesh;
        public bool crate;      // une caisse : remplacée par son modèle de Blender (CA1, CA2, CO1) s'il est importé
    }

    /// <summary>Le rôle d'un bloc, lu dans son nom et celui de ses parents (le blocking est rangé ainsi).</summary>
    private static Role Classify(Transform block)
    {
        string name = block.name;
        string path = HierarchyPath(block);
        if (name.StartsWith("Stairs")) return new Role { material = "Decor_ToleStriee", keepMesh = true };
        if (name == "Plateforme") return new Role { material = "Decor_AcierPeint", top = "Decor_ToleStriee", chamfer = 0.02f };
        if (name == "Tour") return new Role { material = "Decor_BetonBanche", top = "Decor_ToleStriee", chamfer = 0.04f };
        if (name == "Mur tour") return new Role { material = "Decor_AcierPeint", chamfer = 0.015f };
        if (name.StartsWith("Barricade")) return new Role { material = "Decor_BetonBarricade", chamfer = 0.04f };
        if (name.StartsWith("Caisse")) return new Role { material = "Decor_Caisse", chamfer = 0.03f, crate = true };
        if (name == "Mur") return new Role { material = "Decor_AcierPeint", chamfer = 0.015f };
        if (path.StartsWith("Midle/") || path.StartsWith("Spawn Enemy/")) return new Role { material = "Decor_BetonSombre" };
        return new Role { material = "Decor_BetonBanche" };
    }

    private static void DressBlocking(Scene scene, Tribune tribune, Transform decor, Dictionary<string, Material> materials,
                                      DecorMeshes.Bank bank, StringBuilder report)
    {
        var root = new GameObject("Blocking habillé").transform;
        root.SetParent(decor, false);
        int dressed = 0, kept = 0, crates = 0;
        foreach (ProBuilderMesh block in Object.FindObjectsByType<ProBuilderMesh>(FindObjectsInactive.Exclude))
        {
            if (block.gameObject.scene != scene || block.transform.IsChildOf(tribune.transform) || block.transform.IsChildOf(decor)) continue;
            var renderer = block.GetComponent<MeshRenderer>();
            if (renderer == null) continue;
            Role role = Classify(block.transform);
            if (role.keepMesh)
            {
                // Un escalier garde son maillage (ses marches) : il prend seulement la matière du décor.
                Undo.RecordObject(renderer, "Habiller l'arène");
                renderer.sharedMaterial = materials[role.material];
                renderer.enabled = true;
                renderer.renderingLayerMask = ArenaLayerMask;
                kept++;
                continue;
            }

            // L'enveloppe du bloc dans le monde : tous sont des boîtes tournées d'un quart de tour, leurs bornes sont
            // exactes. Un mur qui s'enfonce sous le sol s'arrête juste dessous : on n'en dessine que ce qui se voit.
            Bounds bounds = renderer.bounds;
            if (role.crate && PlaceCrate(root, block.transform, bounds))
            {
                Undo.RecordObject(renderer, "Habiller l'arène");
                renderer.enabled = false;
                crates++;
                continue;
            }
            Vector3 min = bounds.min, max = bounds.max;
            // Seul un bloc posé au sol (ou qui s'y enfonce) descend sous le sol ; un bloc qui flotte (la barricade de
            // 1,25 m, à 2,5 cm) garde son vide : on ne montre pas de solide là où les balles passent.
            if (min.y < 0.005f) min.y = -role.chamfer - 0.01f;
            Vector3 center = (min + max) * 0.5f;
            var b = new DecorMeshes.Builder();
            DecorMeshes.ChamferedBox(b, min - center, max - center, role.chamfer, role.top != null ? 1 : 0);
            Mesh mesh = bank.Add(b.ToMesh($"Bloc_{dressed:000}_{block.name}"));

            var go = new GameObject(block.name);
            go.transform.SetParent(root, false);
            go.transform.position = center;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = role.top != null
                ? new[] { materials[role.material], materials[role.top] }
                : new[] { materials[role.material] };
            r.renderingLayerMask = ArenaLayerMask;
            SetStatic(go);

            Undo.RecordObject(renderer, "Habiller l'arène");
            renderer.enabled = false;
            dressed++;
        }
        report.AppendLine($"Blocking : {dressed} blocs habillés, {crates} caisses de Blender (leurs colliders inchangés), {kept} escaliers gardés.");
    }

    /// <summary>
    /// Une caisse du blocking prend son modèle de Blender, qui tient exactement dans sa boîte : un mètre, la caisse de
    /// munitions (anthracite ou repeinte, au hasard de sa place) ; deux mètres, le conteneur. Son quart de tour aussi
    /// vient de sa place : le même à chaque reconstruction. Faux si la taille n'a pas de modèle (ou s'il n'est pas
    /// importé) : le bloc est alors habillé comme les autres.
    /// </summary>
    private static bool PlaceCrate(Transform parent, Transform block, Bounds bounds)
    {
        Vector3 size = bounds.size;
        bool cube = Mathf.Abs(size.x - size.y) < 0.02f && Mathf.Abs(size.x - size.z) < 0.02f;
        if (!cube) return false;
        int hash = Mathf.Abs(Mathf.RoundToInt(bounds.center.x * 73f) * 31 + Mathf.RoundToInt(bounds.center.y * 19f) * 17 + Mathf.RoundToInt(bounds.center.z * 41f));
        string code = Mathf.Abs(size.x - 1f) < 0.02f ? (hash % 3 == 0 ? "CA2" : "CA1")
            : Mathf.Abs(size.x - 2f) < 0.02f ? "CO1" : null;
        GameObject prefab = code != null ? DecorImporter.Prefab(code) : null;
        if (prefab == null) return false;
        var crate = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        crate.name = block.name;
        crate.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z),
                                               Quaternion.Euler(0f, 90f * (hash % 4), 0f));
        foreach (MeshRenderer r in crate.GetComponentsInChildren<MeshRenderer>(true)) r.renderingLayerMask = ArenaLayerMask;
        return true;
    }

    /// <summary>Montre le blocking gris (pour retoucher le layout) ou le décor.</summary>
    private static void SetBlockingVisible(bool blocking)
    {
        Scene scene = SceneManager.GetActiveScene();
        Tribune tribune = FindInScene<Tribune>(scene);
        GameObject decor = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == DecorRootName) decor = root;
        }
        if (decor == null)
        {
            Debug.LogWarning("[Décor] Pas de décor dans la scène : Duel Arena > Arène > Habiller l'arène.");
            return;
        }
        foreach (ProBuilderMesh block in Object.FindObjectsByType<ProBuilderMesh>(FindObjectsInactive.Exclude))
        {
            if (block.gameObject.scene != scene || (tribune != null && block.transform.IsChildOf(tribune.transform))) continue;
            if (block.name.StartsWith("Stairs")) continue;
            var renderer = block.GetComponent<MeshRenderer>();
            if (renderer == null) continue;
            Undo.RecordObject(renderer, "Montrer le blocking");
            renderer.enabled = blocking;
        }
        Transform dressed = decor.transform.Find("Blocking habillé");
        if (dressed != null)
        {
            Undo.RecordObject(dressed.gameObject, "Montrer le blocking");
            dressed.gameObject.SetActive(!blocking);
        }
        EditorSceneManager.MarkSceneDirty(scene);
    }

    // ------------------------------------------------------------------
    // Le sol et la salle
    // ------------------------------------------------------------------

    /// <summary>Les tracés de la salle, lus sur la tribune : le garde-corps, le bord extérieur de la passerelle (le
    /// pied des murs de la salle) et le dehors de ces murs.</summary>
    public sealed class HallGeometry
    {
        public Vector2[] rail;
        public Vector2[] inner;   // la face intérieure des murs de la salle
        public Vector2[] outer;   // leur face extérieure
        public Vector2 center;
        public float floorTop, walkwayWidth, outerWallHeight;

        public static HallGeometry Of(Tribune tribune)
        {
            var so = new SerializedObject(tribune);
            SerializedProperty line = so.FindProperty("railLine");
            var rail = new Vector2[line.arraySize];
            for (int i = 0; i < rail.Length; i++) rail[i] = line.GetArrayElementAtIndex(i).vector2Value;
            float width = so.FindProperty("walkwayWidth").floatValue;
            var g = new HallGeometry
            {
                rail = rail,
                walkwayWidth = width,
                floorTop = so.FindProperty("floorTop").floatValue,
                outerWallHeight = so.FindProperty("outerWallHeight").floatValue,
                inner = DecorMeshes.Offset(rail, width),
                outer = DecorMeshes.Offset(rail, width + HallWallThickness),
            };
            foreach (Vector2 p in rail) g.center += p;
            g.center /= rail.Length;
            return g;
        }
    }

    private static void BuildFloor(Scene scene, Transform decor, HallGeometry hall, Dictionary<string, Material> materials,
                                   DecorMeshes.Bank bank, StringBuilder report)
    {
        var b = new DecorMeshes.Builder();
        DecorMeshes.Prism(b, hall.outer, -0.3f, 0f, true, false, false);
        Mesh mesh = bank.Add(b.ToMesh("Sol_Dalle"));
        var floor = new GameObject("Sol");
        floor.transform.SetParent(decor, false);
        floor.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = floor.AddComponent<MeshRenderer>();
        r.sharedMaterial = materials["Decor_Dalle"];
        r.renderingLayerMask = ArenaLayerMask;
        // Le collider du sol : une dalle d'un mètre sous le dessus, sur toute la salle.
        Bounds bounds = mesh.bounds;
        var box = floor.AddComponent<BoxCollider>();
        box.center = new Vector3(bounds.center.x, -0.5f, bounds.center.z);
        box.size = new Vector3(bounds.size.x, 1f, bounds.size.z);
        SetStatic(floor);

        // Le terrain de l'ancien blocking (un kilomètre de damier) : désactivé, pas supprimé.
        int disabled = 0;
        foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude))
        {
            if (terrain.gameObject.scene != scene) continue;
            Undo.RecordObject(terrain.gameObject, "Habiller l'arène");
            terrain.gameObject.SetActive(false);
            disabled++;
        }
        report.AppendLine($"Sol : dalle de {bounds.size.x:0} × {bounds.size.z:0} m, {disabled} terrain désactivé.");
    }

    private static void BuildHall(Transform decor, HallGeometry hall, Dictionary<string, Material> materials,
                                  DecorMeshes.Bank bank, StringBuilder report)
    {
        var root = new GameObject("Salle").transform;
        root.SetParent(decor, false);
        Vector2[] p = hall.inner;
        int n = p.Length;

        // Les murs : leur face intérieure, du sol au plafond, et leur collider (rien de lancé ne sort de la salle).
        var walls = new DecorMeshes.Builder();
        for (int i = 0; i < n; i++)
        {
            Vector2 a = p[i], c = p[(i + 1) % n];
            Vector2 o = DecorMeshes.OutwardOf(p, i);
            walls.Quad(new Vector3(a.x, 0f, a.y), new Vector3(c.x, 0f, c.y), new Vector3(c.x, CeilingHeight, c.y),
                       new Vector3(a.x, CeilingHeight, a.y), new Vector3(-o.x, 0f, -o.y));
        }
        Mesh wallMesh = bank.Add(walls.ToMesh("Salle_Murs"));
        GameObject wallObject = Part(root, "Murs", wallMesh, materials["Decor_BetonSalle"], HallLayerMask);
        wallObject.AddComponent<MeshCollider>().sharedMesh = wallMesh;

        // Les pilastres : à chaque angle entre deux grands pans, et au milieu des pans coupés.
        var pilasters = new DecorMeshes.Builder();
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            Vector2 previous = p[(i + n - 1) % n], here = p[i], next = p[(i + 1) % n];
            float before = Vector2.Distance(previous, here), after = Vector2.Distance(here, next);
            if (after < 3f)
            {
                AddPilaster(pilasters, (here + next) * 0.5f, DecorMeshes.OutwardOf(p, i));
                count++;
            }
            else if (before >= 3f)
            {
                Vector2 outward = (DecorMeshes.OutwardOf(p, (i + n - 1) % n) + DecorMeshes.OutwardOf(p, i)).normalized;
                AddPilaster(pilasters, here, outward);
                count++;
            }
            // Un pilastre de plus au milieu des grands pans.
            if (after > 16f)
            {
                AddPilaster(pilasters, (here + next) * 0.5f, DecorMeshes.OutwardOf(p, i));
                count++;
            }
        }
        Part(root, "Pilastres", bank.Add(pilasters.ToMesh("Salle_Pilastres")), materials["Decor_BetonSalle"], HallLayerMask);

        // Le plafond, et son collider.
        var ceiling = new DecorMeshes.Builder();
        DecorMeshes.Prism(ceiling, hall.outer, CeilingHeight, CeilingHeight + 0.5f, false, true, false);
        Mesh ceilingMesh = bank.Add(ceiling.ToMesh("Salle_Plafond"));
        GameObject ceilingObject = Part(root, "Plafond", ceilingMesh, materials["Decor_Plafond"], HallLayerMask);
        var ceilingCollider = ceilingObject.AddComponent<BoxCollider>();
        ceilingCollider.center = new Vector3(ceilingMesh.bounds.center.x, CeilingHeight + 0.5f, ceilingMesh.bounds.center.z);
        ceilingCollider.size = new Vector3(ceilingMesh.bounds.size.x, 1f, ceilingMesh.bounds.size.z);
        ceilingObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // La charpente : des poutres rayonnantes, des pilastres vers une couronne au-dessus du milieu de l'arène,
        // et un anneau à mi-chemin. Elle ne porte pas d'ombre : la rampe générale éclaire à travers.
        var beams = new DecorMeshes.Builder();
        Vector3 hub = new Vector3(hall.center.x, CeilingHeight - 0.45f, hall.center.y);
        const float ringRadius = 7f;
        var ring = new List<Vector3>();
        var middle = new List<Vector3>();
        for (int i = 0; i < n; i++)
        {
            Vector2 here = p[i];
            if (Vector2.Distance(here, p[(i + 1) % n]) < 3f) continue;   // un seul rayon par angle coupé
            var foot = new Vector3(here.x, CeilingHeight - 0.45f, here.y);
            Vector3 toHub = (hub - foot);
            toHub.y = 0f;
            float length = toHub.magnitude;
            Vector3 end = hub - toHub.normalized * ringRadius;
            DecorMeshes.IBeam(beams, foot, end, 0.9f, 0.4f);
            ring.Add(end);
            middle.Add(Vector3.Lerp(foot, end, 0.5f) + Vector3.down * 0.0f);
        }
        for (int i = 0; i < ring.Count; i++)
        {
            DecorMeshes.IBeam(beams, ring[i], ring[(i + 1) % ring.Count], 0.7f, 0.35f);
            DecorMeshes.IBeam(beams, middle[i] + Vector3.down * 0.9f, middle[(i + 1) % middle.Count] + Vector3.down * 0.9f, 0.6f, 0.3f);
        }
        GameObject beamObject = Part(root, "Charpente", bank.Add(beams.ToMesh("Salle_Charpente")), materials["Decor_Charpente"], HallLayerMask);
        beamObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        HallRing = ring.ToArray();
        HallMiddle = middle.ToArray();

        report.AppendLine($"Salle : {n} pans de mur, {count} pilastres, plafond à {CeilingHeight:0} m, {ring.Count} poutres rayonnantes.");
    }

    /// <summary>Les points de la couronne et de l'anneau intermédiaire de la charpente (pour y pendre les rampes).</summary>
    public static Vector3[] HallRing = System.Array.Empty<Vector3>();
    public static Vector3[] HallMiddle = System.Array.Empty<Vector3>();

    private static void AddPilaster(DecorMeshes.Builder b, Vector2 at, Vector2 outward)
    {
        // 1 m de large, 0,5 m de saillie, du sol au plafond, collé au mur.
        Vector3 o = new Vector3(outward.x, 0f, outward.y).normalized;
        Vector3 along = Vector3.Cross(Vector3.up, o);
        Vector3 center = new Vector3(at.x, CeilingHeight * 0.5f, at.y) - o * 0.25f;
        DecorMeshes.OrientedBox(b, center, along, Vector3.up, o, new Vector3(0.5f, CeilingHeight * 0.5f, 0.25f));
    }

    // ------------------------------------------------------------------
    // La tribune
    // ------------------------------------------------------------------

    private static void DressTribune(Tribune tribune, Dictionary<string, Material> materials, StringBuilder report)
    {
        Transform built = tribune.transform.Find("Passerelle (générée)");
        if (built == null)
        {
            report.AppendLine("Tribune : pas de passerelle construite.");
            return;
        }
        int count = 0;
        foreach (MeshRenderer r in built.GetComponentsInChildren<MeshRenderer>(true))
        {
            string name = r.gameObject.name;
            string material = name.StartsWith("Sol") ? "Decor_ToleStriee"
                : name.StartsWith("Garde-corps") ? "Decor_GardeCorps"
                : name.StartsWith("Mur") ? "Decor_MurTribune"
                : name.StartsWith("Pilier") ? "Decor_AcierPeintTribune"
                : name == "Bloc" || name == "Morceau" ? "Decor_BetonFissure"
                : null;
            if (material == null) continue;
            Undo.RecordObject(r, "Habiller l'arène");
            r.sharedMaterial = materials[material];
            r.renderingLayerMask = HallLayerMask;
            count++;
        }
        // Le matériau des morceaux que le bloc lâche (RockSource), et celui que la reconstruction reposera.
        var so = new SerializedObject(tribune);
        so.FindProperty("floorMaterial").objectReferenceValue = materials["Decor_ToleStriee"];
        so.FindProperty("railMaterial").objectReferenceValue = materials["Decor_GardeCorps"];
        so.FindProperty("wallMaterial").objectReferenceValue = materials["Decor_MurTribune"];
        so.FindProperty("blockMaterial").objectReferenceValue = materials["Decor_BetonFissure"];
        so.FindProperty("chunkMaterial").objectReferenceValue = materials["Decor_BetonFissure"];
        so.ApplyModifiedProperties();
        report.AppendLine($"Tribune : {count} pièces aux matériaux du décor (et ceux de sa reconstruction).");
    }

    // ------------------------------------------------------------------
    // Outils
    // ------------------------------------------------------------------

    internal static GameObject Part(Transform parent, string name, Mesh mesh, Material material, uint layers)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.renderingLayerMask = layers;
        SetStatic(go);
        return go;
    }

    internal static void SetStatic(GameObject go)
    {
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI
            | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
    }

    internal static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (T c in Object.FindObjectsByType<T>(FindObjectsInactive.Include))
        {
            if (c.gameObject.scene == scene) return c;
        }
        return null;
    }

    private static string HierarchyPath(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }
}
