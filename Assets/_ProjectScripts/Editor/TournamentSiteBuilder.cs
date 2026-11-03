using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Look = StandDecorBuilder.Look;

/// <summary>
/// Pose le site du Tournoi dans l'arène de test (GDD § 11, TO5 et TO9 ; 2026-11-03) : Duel Arena > Arène > Poser le site du
/// tournoi.
/// - Deux copies de l'arène, à 2 km de part et d'autre de celle de la scène : trois matchs à la fois, à six joueurs. Rien ne
///   s'entend ni ne se voit d'une copie à l'autre (les tirs portent à 500 m, la vue à 1 km). Une copie reprend la salle
///   telle qu'elle est (le blocking et ses colliders, l'habillage, la tribune, les lumières sauf la rampe générale, qui
///   éclaire tout le monde), sans rien de ce qui fait le jeu (ni objet réseau, ni script) ; ses colliders sont ceux du
///   blocking, les mêmes maillages.
/// - La salle d'attente, à 2 km au sud, une salle du même complexe : son mur d'écrans, où passe en direct chaque match en
///   cours (TournamentFeeds ; au milieu, l'arène de la scène, celle de la finale), le coin d'attente et ses bancs, et au
///   fond, derrière un comptoir, un pas de tir et ses mannequins (on ne se blesse pas dans la salle : TO5).
/// - Le RoundManager de la scène reçoit le préfab de ses couloirs (MatchTournoi), et l'interface le tableau du tournoi.
///
/// On corrige ce script et on relance le menu, jamais la scène. À refaire après une retouche du blocking ou de
/// l'habillage de l'arène (les copies la reprennent), puis Duel Arena > Arène > Cuire la lumière (les copies et la salle
/// d'attente ont leur lumière cuite, leurs sondes).
/// </summary>
public static class TournamentSiteBuilder
{
    public const string ScenePath = "Assets/_ProjectScenes/MultiTestScene.unity";
    public const string RootName = "Site du tournoi (généré)";
    public const string LanePrefabPath = "Assets/_ProjectArena/Modes/MatchTournoi.prefab";
    private const string ScreenMaterialPath = "Assets/_ProjectArena/Modes/Tournoi_Ecran.mat";
    private const string BankPath = "Assets/_ProjectScenes/MultiTestScene_SalleAttente_Decor.asset";
    private const string GameHudPath = "Assets/_ProjectUI/Game/GameHud.uxml";

    /// <summary>Les copies de l'arène : la première est celle de la scène.</summary>
    public static readonly Vector3[] ArenaOffsets = { Vector3.zero, new Vector3(2000f, 0f, 0f), new Vector3(-2000f, 0f, 0f) };

    /// <summary>Le centre de la salle d'attente, au sol.</summary>
    public static readonly Vector3 WaitingRoomCenter = new Vector3(0f, 0f, -2000f);

    /// <summary>La salle d'attente : son intérieur (x, z autour de son centre) et sa hauteur sous plafond.</summary>
    public static readonly Rect WaitingRoomInside = Rect.MinMaxRect(-15f, -16f, 15f, 16f);
    public const float WaitingRoomHeight = 7f;

    /// <summary>Les écrans, sur le mur nord (x de leur centre, dans l'ordre des arènes : celle de la scène au milieu), leur
    /// taille (16:9) et la hauteur de leur centre.</summary>
    public static readonly float[] ScreenCenters = { 0f, -9.4f, 9.4f };
    public static readonly Vector2 ScreenSize = new Vector2(7.2f, 4.05f);
    public const float ScreenHeight = 3.45f;

    /// <summary>Le comptoir du pas de tir (z, autour du centre de la salle) et les mannequins derrière lui (x, z).</summary>
    public const float CounterFront = -0.4f;
    public const float CounterBack = -1.2f;
    public static readonly Vector2[] Dummies = { new(-9f, -6f), new(-3f, -8.5f), new(4f, -7f), new(10f, -10f), new(-6f, -12.5f), new(1.5f, -13.5f) };

    /// <summary>Les points d'apparition de la salle, tournés vers les écrans (x, z).</summary>
    public static readonly Vector2[] Spawns =
    {
        new(-9f, 6f), new(-3f, 6f), new(3f, 6f), new(9f, 6f),
        new(-9f, 9.5f), new(-3f, 9.5f), new(3f, 9.5f), new(9f, 9.5f),
    };

    // Les racines de la scène qui ne sont pas la salle : le jeu lui-même.
    private static readonly HashSet<string> NotCopied = new HashSet<string>
    {
        "Main Camera", "Directional Light", "Terrain", "SpawnPoints", "RoundManager", "GameUI", "VictorySpot", ZoneLayoutBuilder.GroupName, RootName,
    };

    private static readonly List<StandDecorBuilder.Block> blocks = new List<StandDecorBuilder.Block>();

    [MenuItem("Duel Arena/Arène/Poser le site du tournoi")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Tournoi] Sortir du Play Mode d'abord.");
            return;
        }
        Scene previous = SceneManager.GetActiveScene();
        if (previous.isDirty)
        {
            Debug.LogWarning("[Tournoi] La scène ouverte n'est pas enregistrée : enregistre-la, puis relance.");
            return;
        }
        string previousPath = previous.path;
        Scene scene = previous.path == ScenePath ? previous : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string report = BuildIn(scene);
        EditorSceneManager.SaveScene(scene);

        // 🚨 Les mannequins sont des objets réseau de scène : leur identifiant (GlobalObjectIdHash) se calcule sur la scène
        // enregistrée (voir ShootingRangeBuilder). On le recalcule, et on réenregistre.
        scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        System.Reflection.MethodInfo validate = typeof(NetworkObject).GetMethod("OnValidate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (NetworkObject networkObject in root.GetComponentsInChildren<NetworkObject>(true))
        {
            validate?.Invoke(networkObject, null);
            EditorUtility.SetDirty(networkObject);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Tournoi] " + report + "\nÀ faire ensuite : Duel Arena > Arène > Cuire la lumière.");
        if (!string.IsNullOrEmpty(previousPath) && previousPath != ScenePath) EditorSceneManager.OpenScene(previousPath);
    }

    /// <summary>Pose le site dans la scène de l'arène (ouverte). Rend un compte rendu.</summary>
    public static string BuildIn(Scene scene)
    {
        var report = new StringBuilder();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == RootName) Object.DestroyImmediate(root);
        }
        var site = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(site, scene);

        // Les copies de l'arène.
        int copied = 0;
        for (int i = 1; i < ArenaOffsets.Length; i++)
        {
            var copy = new GameObject($"Arène {i + 1}").transform;
            copy.SetParent(site.transform, false);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == site || !root.activeSelf || NotCopied.Contains(root.name)) continue;
                GameObject made = CopyTree(root.transform, copy, ref copied);
                if (made != null) made.transform.position = root.transform.position + ArenaOffsets[i];
            }
        }
        report.AppendLine($"Site du tournoi : {ArenaOffsets.Length - 1} copies de l'arène ({copied} composants copiés).");

        // La salle d'attente.
        Transform room = new GameObject("Salle d'attente").transform;
        room.SetParent(site.transform, false);
        room.position = WaitingRoomCenter;
        blocks.Clear();
        BuildRoom(room);
        Renderer[] screens = BuildScreens(room);
        Transform[] spawns = BuildSpawns(room);
        PlaceDummies(room);
        Physics.SyncTransforms();
        Dictionary<string, Material> materials = DecorMaterials.CreateAll();
        var bank = new DecorMeshes.Bank(BankPath);
        var decor = new GameObject("Décor de la salle").transform;
        decor.SetParent(room, false);
        StandDecorBuilder.DressBlocks(decor, blocks, materials, bank, report);
        bank.Save();
        LightRoom(room);

        var bounds = new Bounds(WaitingRoomCenter + Vector3.up * (WaitingRoomHeight * 0.5f),
                                new Vector3(WaitingRoomInside.width, WaitingRoomHeight, WaitingRoomInside.height));
        site.AddComponent<TournamentSite>().EditorSet((Vector3[])ArenaOffsets.Clone(), spawns, bounds, screens);
        site.AddComponent<TournamentFeeds>();
        report.AppendLine($"Salle d'attente : {screens.Length} écrans, {spawns.Length} points d'apparition, {Dummies.Length} mannequins.");

        // Le RoundManager de la scène et l'interface.
        report.AppendLine(WireGame(scene));
        EditorSceneManager.MarkSceneDirty(scene);
        return report.ToString();
    }

    // ------------------------------------------------------------------
    // Les copies de l'arène
    // ------------------------------------------------------------------

    /// <summary>Ce qu'une copie garde d'un objet : sa forme, son rendu, ses colliders, ses lumières et ses sondes.</summary>
    private static bool Kept(Component c) => c is MeshFilter || c is MeshRenderer || c is Collider || c is Light
                                             || c is UniversalAdditionalLightData || c is LightProbeGroup || c is ReflectionProbe;

    /// <summary>
    /// Copie un objet de la salle et ses enfants, sans rien de ce qui fait le jeu (scripts, objets réseau, maillages
    /// ProBuilder) : de nouveaux objets, chaque composant gardé recopié tel quel (ses maillages et ses matériaux partagés).
    /// On ne duplique pas les objets eux-mêmes : retirer le ProBuilderMesh d'un double détruirait le maillage qu'il
    /// partage avec l'original. La rampe générale (la lumière directionnelle) n'est pas copiée : une seule éclaire tout.
    /// </summary>
    private static GameObject CopyTree(Transform source, Transform parent, ref int copied)
    {
        if (source.TryGetComponent(out Light sun) && sun.type == LightType.Directional) return null;
        var go = new GameObject(source.name) { layer = source.gameObject.layer };
        go.transform.SetParent(parent, false);
        go.transform.localPosition = source.localPosition;
        go.transform.localRotation = source.localRotation;
        go.transform.localScale = source.localScale;
        GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(source.gameObject));
        foreach (Component c in source.GetComponents<Component>())
        {
            if (c == null || c is Transform || !Kept(c)) continue;
            Component copy = go.GetComponent(c.GetType());
            if (copy == null) copy = go.AddComponent(c.GetType());
            EditorUtility.CopySerialized(c, copy);
            copied++;
        }
        foreach (Transform child in source) CopyTree(child, go.transform, ref copied);
        go.SetActive(source.gameObject.activeSelf);
        return go;
    }

    // ------------------------------------------------------------------
    // La salle d'attente (autour de son centre)
    // ------------------------------------------------------------------

    private static void BuildRoom(Transform room)
    {
        Rect r = WaitingRoomInside;
        const float h = WaitingRoomHeight;
        Span("Sol", room, new Vector3(r.xMin - 1f, -1f, r.yMin - 1f), new Vector3(r.xMax + 1f, 0f, r.yMax + 1f), Look.Dalle);
        Span("Mur nord (les écrans)", room, new Vector3(r.xMin - 1f, 0f, r.yMax), new Vector3(r.xMax + 1f, h, r.yMax + 1f), Look.Mur);
        Span("Mur sud", room, new Vector3(r.xMin - 1f, 0f, r.yMin - 1f), new Vector3(r.xMax + 1f, h, r.yMin), Look.Mur);
        Span("Mur ouest", room, new Vector3(r.xMin - 1f, 0f, r.yMin), new Vector3(r.xMin, h, r.yMax), Look.Mur);
        Span("Mur est", room, new Vector3(r.xMax, 0f, r.yMin), new Vector3(r.xMax + 1f, h, r.yMax), Look.Mur);
        Span("Plafond", room, new Vector3(r.xMin - 1f, h, r.yMin - 1f), new Vector3(r.xMax + 1f, h + 1f, r.yMax + 1f), Look.Plafond);

        // Des pilastres le long des murs de côté, tous les 8 m.
        for (float z = r.yMin + 4f; z < r.yMax - 1f; z += 8f)
        {
            Span("Pilastre", room, new Vector3(r.xMin, 0f, z - 0.4f), new Vector3(r.xMin + 0.4f, h, z + 0.4f), Look.Mur);
            Span("Pilastre", room, new Vector3(r.xMax - 0.4f, 0f, z - 0.4f), new Vector3(r.xMax, h, z + 0.4f), Look.Mur);
        }

        // Le cadre de chaque écran, contre le mur nord.
        foreach (float x in ScreenCenters)
        {
            Vector2 half = ScreenSize * 0.5f + new Vector2(0.25f, 0.2f);
            Span("Cadre d'écran", room, new Vector3(x - half.x, ScreenHeight - half.y, r.yMax - 0.3f), new Vector3(x + half.x, ScreenHeight + half.y, r.yMax), Look.Module);
        }

        // Le coin d'attente : deux bancs face aux écrans, des caisses dans les coins.
        Span("Banc", room, new Vector3(-8f, 0f, 2.6f), new Vector3(-4f, 0.45f, 3.1f), Look.Acier);
        Span("Banc", room, new Vector3(4f, 0f, 2.6f), new Vector3(8f, 0.45f, 3.1f), Look.Acier);
        Span("Caisse", room, new Vector3(r.xMin, 0f, r.yMax - 1f), new Vector3(r.xMin + 1f, 1f, r.yMax), Look.Caisse);
        Span("Caisse", room, new Vector3(r.xMin + 1.1f, 0f, r.yMax - 1f), new Vector3(r.xMin + 2.1f, 1f, r.yMax), Look.Caisse);
        Span("Caisse", room, new Vector3(r.xMax - 1f, 0f, r.yMax - 1f), new Vector3(r.xMax, 1f, r.yMax), Look.Caisse);

        // Le pas de tir, au sud : le comptoir, par-dessus lequel on tire, et le pare-balles au fond.
        Span("Comptoir", room, new Vector3(r.xMin, 0f, CounterBack), new Vector3(r.xMax, 0.95f, CounterFront), Look.Comptoir);
        Span("Pare-balles", room, new Vector3(r.xMin, 0f, r.yMin), new Vector3(r.xMax, 5f, r.yMin + 1.2f), Look.PareBalles);
    }

    /// <summary>Un bloc du jeu, de `min` à `max` autour du centre de la salle : un collider, et ce qu'il devient à
    /// l'habillage.</summary>
    private static void Span(string name, Transform room, Vector3 min, Vector3 max, Look look)
    {
        var go = new GameObject(name);
        go.transform.SetParent(room, false);
        go.transform.localPosition = (min + max) * 0.5f;
        go.AddComponent<BoxCollider>().size = max - min;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        blocks.Add(new StandDecorBuilder.Block(go, look));
    }

    /// <summary>Les écrans : un quadrilatère sans lumière (URP Unlit) devant chaque cadre, tourné vers la salle ; le direct y
    /// pose son image (TournamentFeeds). Dans l'ordre des arènes.</summary>
    private static Renderer[] BuildScreens(Transform room)
    {
        Material material = ScreenMaterial();
        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var screens = new Renderer[ScreenCenters.Length];
        for (int i = 0; i < ScreenCenters.Length; i++)
        {
            var go = new GameObject($"Écran {i + 1} (arène {i + 1})");
            go.transform.SetParent(room, false);
            // Le quadrilatère d'Unity se voit du côté -Z : la salle est au sud de l'écran.
            go.transform.localPosition = new Vector3(ScreenCenters[i], ScreenHeight, WaitingRoomInside.yMax - 0.31f);
            go.transform.localScale = new Vector3(ScreenSize.x, ScreenSize.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            screens[i] = renderer;
        }
        return screens;
    }

    private static Material ScreenMaterial()
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor", new Color(0.035f, 0.04f, 0.05f));
        return RobotChassisBuilder.SaveMaterial(ScreenMaterialPath, material);
    }

    private static Transform[] BuildSpawns(Transform room)
    {
        var group = new GameObject("Points d'apparition").transform;
        group.SetParent(room, false);
        var spawns = new Transform[Spawns.Length];
        for (int i = 0; i < Spawns.Length; i++)
        {
            var point = new GameObject($"Apparition {i + 1}").transform;
            point.SetParent(group, false);
            point.localPosition = new Vector3(Spawns[i].x, 0f, Spawns[i].y);
            // Tourné vers les écrans (le nord).
            point.localRotation = Quaternion.identity;
            spawns[i] = point;
        }
        return spawns;
    }

    /// <summary>Les mannequins du stand (TrainingDummy), derrière le comptoir, tournés vers la salle.</summary>
    private static void PlaceDummies(Transform room)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShootingRangeBuilder.DummyPrefabPath);
        if (prefab == null) return;
        var group = new GameObject("Mannequins").transform;
        group.SetParent(room, false);
        for (int i = 0; i < Dummies.Length; i++)
        {
            var dummy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            dummy.name = $"Mannequin {i + 1}";
            dummy.transform.SetLocalPositionAndRotation(new Vector3(Dummies[i].x, 0f, Dummies[i].y), Quaternion.identity);
        }
    }

    /// <summary>La lumière de la salle : des projecteurs sous le plafond (la rampe générale l'éclaire aussi, comme
    /// l'arène), ses sondes de lumière pour les robots, une sonde de reflets.</summary>
    private static void LightRoom(Transform room)
    {
        var lights = new GameObject("Éclairage de la salle").transform;
        lights.SetParent(room, false);
        foreach (float x in new[] { -9f, 0f, 9f })
        foreach (float z in new[] { 4f, 11f })
            ArenaLighting.AddSpot(lights, room.position + new Vector3(x, WaitingRoomHeight - 0.3f, z), 100f, 12f, 40f);
        foreach (float x in new[] { -8f, 0f, 8f })
            ArenaLighting.AddSpot(lights, room.position + new Vector3(x, WaitingRoomHeight - 0.3f, -8f), 90f, 12f, 45f);

        var probes = new GameObject("Sondes de lumière").AddComponent<LightProbeGroup>();
        probes.transform.SetParent(lights, false);
        var positions = new List<Vector3>();
        for (float x = WaitingRoomInside.xMin + 1.5f; x < WaitingRoomInside.xMax; x += 3f)
        for (float z = WaitingRoomInside.yMin + 1.5f; z < WaitingRoomInside.yMax; z += 3f)
        foreach (float y in new[] { 0.4f, 1.6f, 3.2f })
            positions.Add(new Vector3(x, y, z));
        probes.probePositions = positions.ToArray();

        var reflection = new GameObject("Sonde de reflets").AddComponent<ReflectionProbe>();
        reflection.transform.SetParent(lights, false);
        reflection.transform.localPosition = new Vector3(0f, 3f, 0f);
        reflection.mode = ReflectionProbeMode.Baked;
        reflection.size = new Vector3(WaitingRoomInside.width, WaitingRoomHeight, WaitingRoomInside.height);
        reflection.boxProjection = true;
    }

    // ------------------------------------------------------------------
    // Le jeu : le RoundManager de la scène, l'interface
    // ------------------------------------------------------------------

    private static string WireGame(Scene scene)
    {
        var lines = new StringBuilder();
        var lane = AssetDatabase.LoadAssetAtPath<GameObject>(LanePrefabPath);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.TryGetComponent(out RoundManager rm))
            {
                var so = new SerializedObject(rm);
                so.FindProperty("lanePrefabAsset").objectReferenceValue = lane != null ? lane.GetComponent<RoundManager>() : null;
                so.FindProperty("lanePrefab").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                lines.Append(lane != null ? "Le RoundManager a ses couloirs (MatchTournoi). " : "⚠ Pas de préfab MatchTournoi. ");
            }
            if (root.name == "GameUI" && root.TryGetComponent(out UnityEngine.UIElements.UIDocument document))
            {
                if (!root.TryGetComponent(out TournamentBoard board)) board = root.AddComponent<TournamentBoard>();
                var so = new SerializedObject(board);
                so.FindProperty("document").objectReferenceValue = document;
                so.ApplyModifiedPropertiesWithoutUndo();
                lines.Append("L'interface a le tableau du tournoi.");
            }
        }
        return lines.ToString();
    }
}
