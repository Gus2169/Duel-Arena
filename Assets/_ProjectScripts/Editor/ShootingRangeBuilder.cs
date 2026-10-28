using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Construit le stand de tir (2026-10-16) : le mannequin (préfab) et la scène StandDeTir, et ajoute la
/// scène à la Build. Rejouable : il refait tout à l'identique, comme « Construire la passerelle ». On
/// corrige ce script, pas la scène.
///
/// Le mannequin prend au préfab joueur ce qui fait qu'il est touché comme un robot : la géométrie de
/// la surface touchable (PlayerHitbox) et la hauteur de l'œil debout. Les mannequins sont posés à 5,
/// 10, 15, 25, 35 et 50 m du poste de tir, face à lui, plus un derrière une barricade, la tête
/// seule à découvert. Un pilier près du poste sert à essayer le lean.
///
/// À droite du poste, une porte mène à la Fosse (BuildCourse) : le parcours chronométré, ses sections
/// et ses cibles (CibleParcours.prefab : le mannequin et CourseTarget).
/// </summary>
public static class ShootingRangeBuilder
{
    public const string ScenePath = "Assets/_ProjectScenes/StandDeTir.unity";
    public const string DummyPrefabPath = "Assets/_ProjectArena/Stand/Mannequin.prefab";
    public const string CourseTargetPrefabPath = "Assets/_ProjectArena/Stand/CibleParcours.prefab";
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const string IdleClipPath = "Assets/_ProjectArt/Animations/MainsNues/Y Bot@Idle.fbx";
    private const string GridMaterialGuid = "c22777d6e868e4f2fb421913386b154e";   // le quadrillage de l'arène (ProBuilder)
    private const string DarkMaterialPath = "Assets/_ProjectArena/Tribune/Materiaux/Passerelle_GardeCorps.mat";
    private const string PausePath = "Assets/_ProjectUI/Game/StandPause.uxml";
    private const string PanelSettingsPath = "Assets/_ProjectUI/Game/GamePanelSettings.asset";
    private const string BindRowPath = "Assets/_ProjectUI/MainMenu/KeyBindRow.uxml";
    private const string GameSoundsPath = "Assets/_ProjectArt/Audio/GameSounds.asset";

    /// <summary>Les mannequins à découvert : (x, distance au poste de tir).</summary>
    public static readonly Vector2[] OpenDummies =
    {
        new Vector2(-3f, 5f), new Vector2(2f, 10f), new Vector2(-2f, 15f),
        new Vector2(3f, 25f), new Vector2(-3f, 35f), new Vector2(0f, 50f),
    };

    /// <summary>Le mannequin derrière la barricade : la tête seule dépasse.</summary>
    public static readonly Vector2 CoveredDummy = new Vector2(8f, 20f);

    [MenuItem("Duel Arena/Stand de tir/Construire le stand")]
    public static void Build()
    {
        // Sans boîte de dialogue (le constructeur se lance aussi d'un outil) : une scène ouverte non
        // enregistrée arrête tout, plutôt que de perdre son travail.
        Scene previous = SceneManager.GetActiveScene();
        if (previous.isDirty)
        {
            Debug.LogWarning("[Stand] La scène ouverte n'est pas enregistrée : enregistre-la, puis reconstruis le stand.");
            return;
        }
        string previousPath = previous.path;
        GameObject dummyPrefab = BuildDummyPrefab(DummyPrefabPath, false);
        GameObject coursePrefab = BuildDummyPrefab(CourseTargetPrefabPath, true);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Material grid = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(GridMaterialGuid));
        Material dark = AssetDatabase.LoadAssetAtPath<Material>(DarkMaterialPath);

        var lightObject = new GameObject("Soleil");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

        var level = new GameObject("Stand").transform;
        Box("Sol", level, new Vector3(0f, -0.5f, 28f), new Vector3(44f, 1f, 76f), grid);
        Box("Mur du fond", level, new Vector3(0f, 3f, 66f), new Vector3(44f, 6f, 1f), grid);
        Box("Mur gauche", level, new Vector3(-22f, 3f, 28f), new Vector3(1f, 6f, 76f), grid);
        // Le mur droit s'ouvre sur la Fosse (une porte de 3 m près du poste de tir).
        Box("Mur droit (sud)", level, new Vector3(22f, 3f, -8.75f), new Vector3(1f, 6f, 2.5f), grid);
        Box("Mur droit (nord)", level, new Vector3(22f, 3f, 30.75f), new Vector3(1f, 6f, 70.5f), grid);
        Box("Mur droit (linteau)", level, new Vector3(22f, 4.5f, -6f), new Vector3(1f, 3f, 3f), grid);
        FloorText(level, "LA FOSSE  >>>", new Vector3(18.5f, 0.012f, -6f), 90f, 0.07f);
        Box("Mur arrière", level, new Vector3(0f, 3f, -10f), new Vector3(44f, 6f, 1f), grid);
        Box("Ligne de tir", level, new Vector3(0f, 0.005f, 0f), new Vector3(30f, 0.01f, 0.12f), dark, collider: false);
        Box("Pilier (lean)", level, new Vector3(-5f, 1.3f, 1.2f), new Vector3(1f, 2.6f, 1f), grid);
        Box("Barricade", level, new Vector3(CoveredDummy.x, 0.625f, CoveredDummy.y - 0.9f), new Vector3(2.4f, 1.25f, 0.5f), grid);

        var dummies = new GameObject("Mannequins").transform;
        int index = 0;
        foreach (Vector2 p in OpenDummies) PlaceDummy(dummyPrefab, dummies, p, ++index);
        PlaceDummy(dummyPrefab, dummies, CoveredDummy, ++index);

        var spawns = new GameObject("SpawnPoints");
        var spawn = new GameObject("Poste de tir").transform;
        spawn.SetParent(spawns.transform, false);
        spawn.position = new Vector3(0f, 0f, -2f);
        var points = spawns.AddComponent<PlayerSpawnPoints>();
        var so = new SerializedObject(points);
        SerializedProperty list = so.FindProperty("spawnPoints");
        list.arraySize = 1;
        list.GetArrayElementAtIndex(0).objectReferenceValue = spawn;
        so.ApplyModifiedPropertiesWithoutUndo();

        var range = new GameObject("StandDeTir");
        range.AddComponent<NetworkObject>();
        range.AddComponent<ShootingRange>();
        range.AddComponent<ShootingRangeHud>();
        range.AddComponent<ShootingRangePanel>();
        range.AddComponent<ShootingRangeBootstrap>();
        BuildInterface();

        BuildCourse(coursePrefab, grid, dark);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        EditorSceneManager.SaveScene(scene, ScenePath);

        // 🚨 Netcode calcule l'identifiant d'un objet réseau de scène (GlobalObjectIdHash) d'après la
        // scène ENREGISTRÉE : calculé avant, il valait celui du préfab pour tous les mannequins, et le
        // chargement du stand échouait (« ScenePlacedObjects already contains the same
        // GlobalObjectIdHash »). On le recalcule une fois la scène sur le disque, et on réenregistre.
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
        AddToBuild(ScenePath);
        Debug.Log($"[Stand] Construit : {ScenePath}, {OpenDummies.Length + 1} mannequins, la Fosse et ses {CourseTargets.Length} cibles.");
        if (!string.IsNullOrEmpty(previousPath) && previousPath != ScenePath) EditorSceneManager.OpenScene(previousPath);
    }

    // ------------------------------------------------------------------------------------------------
    // La Fosse (2026-10-16) : le parcours chronométré, sur le modèle de « The Pit » de Modern Warfare 2.
    // Une pièce de 39 × 59 m à droite du stand (x 22,5 à 61,5 ; z -9,5 à 49,5), dont on fait le tour :
    // départ (la porte du stand) → la cour et sa barricade à franchir → l'allée, et sa cible qui va et
    // vient → le bâtiment (deux pièces) → l'escalier, et sa cible à neutraliser au taser → le toit, d'où
    // l'on tire dans la cour → le saut dans la cour → le couloir du retour → l'arrivée, au départ.
    // ------------------------------------------------------------------------------------------------

    private static readonly Color HostileTint = new Color(0.78f, 0.2f, 0.16f, 1f);
    private static readonly Color CivilTint = new Color(0.93f, 0.94f, 0.97f, 1f);

    /// <summary>La zone de départ (et d'arrivée) : entre la porte du stand et la ligne de départ.</summary>
    public static readonly Bounds CourseStartZone = Box3(new Vector3(21f, -1f, -9.5f), new Vector3(30f, 2.5f, -3f));

    /// <summary>Les sections, dans l'ordre du parcours : entrer dans l'une lève ses cibles.</summary>
    public static readonly (string name, Bounds bounds)[] CourseSections =
    {
        ("Entrée", Box3(new Vector3(30f, -1f, -9.5f), new Vector3(42f, 2.5f, 8f))),
        ("Après la barricade", Box3(new Vector3(42.6f, -1f, -9.5f), new Vector3(61.5f, 2.5f, 8f))),
        ("Allée", Box3(new Vector3(54f, -1f, 8f), new Vector3(61.5f, 2.5f, 30f))),
        ("Bâtiment", Box3(new Vector3(34f, -1f, 22f), new Vector3(54f, 2.5f, 40f))),
        ("Escalier", Box3(new Vector3(44f, -1f, 40f), new Vector3(61.5f, 5f, 49.5f))),
        ("Toit", Box3(new Vector3(34f, 2.5f, 22f), new Vector3(54f, 6f, 45f))),
        ("Cour", Box3(new Vector3(22.5f, -1f, 22f), new Vector3(34f, 2.5f, 49.5f))),
        ("Couloir du retour", Box3(new Vector3(22.5f, -1f, -3f), new Vector3(30f, 2.5f, 22f))),
    };

    /// <summary>Une cible : sa section, sa place (pieds), le point qu'elle regarde (x, z), civile ou
    /// non, et le bout de son rail si elle bouge.</summary>
    public struct CourseTargetSpec
    {
        public int section;
        public Vector3 position;
        public Vector2 lookAt;
        public bool civil;
        public bool moves;
        public Vector3 railEnd;
    }

    private static CourseTargetSpec H(int section, float x, float y, float z, float lx, float lz)
        => new CourseTargetSpec { section = section, position = new Vector3(x, y, z), lookAt = new Vector2(lx, lz) };

    private static CourseTargetSpec C(int section, float x, float y, float z, float lx, float lz)
        => new CourseTargetSpec { section = section, position = new Vector3(x, y, z), lookAt = new Vector2(lx, lz), civil = true };

    private static CourseTargetSpec Rail(CourseTargetSpec spec, Vector3 end)
    {
        spec.moves = true;
        spec.railEnd = end;
        return spec;
    }

    /// <summary>Les cibles de la Fosse : 20 hostiles et 8 civils (la Fosse en a 24 et 11, en carton :
    /// les nôtres ont la vie d'un robot).</summary>
    public static readonly CourseTargetSpec[] CourseTargets =
    {
        // 0. L'entrée : trois hostiles dès la ligne franchie.
        H(0, 36.5f, 0f, 3.5f, 30.5f, -6f),
        H(0, 39.5f, 0f, -8f, 30.5f, -6f),
        H(0, 40.5f, 0f, 5.5f, 31f, -6f),
        // 1. Après la barricade : deux hostiles, un civil.
        H(1, 53f, 0f, -7.5f, 43f, 0f),
        H(1, 59.5f, 0f, 4.5f, 43f, 0f),
        C(1, 56f, 0f, -2f, 43f, 0f),
        // 2. L'allée : un hostile qui va et vient en travers, deux fixes, un civil.
        H(2, 56f, 0f, 14.5f, 58f, 8f),
        Rail(H(2, 55.2f, 0f, 21f, 58f, 8f), new Vector3(60.8f, 0f, 21f)),
        H(2, 59.5f, 0f, 28f, 58f, 9f),
        C(2, 60.5f, 0f, 24.5f, 58f, 9f),
        // 3. Le bâtiment : la pièce de la porte, puis la grande pièce vue de son ouverture.
        H(3, 45.5f, 0f, 24f, 53f, 27f),
        C(3, 46f, 0f, 31.5f, 53f, 27f),
        H(3, 35.5f, 0f, 38.5f, 44.5f, 37f),
        H(3, 37.5f, 0f, 28.5f, 44.5f, 37f),
        C(3, 35.5f, 0f, 33f, 44.5f, 37f),
        // 4. Le haut de l'escalier : un hostile au contact, pour le taser (le couteau de la Fosse).
        H(4, 48.3f, 3f, 43.25f, 52f, 43.25f),
        // 5. Le toit : deux hostiles au fond, et dans la cour, en contrebas, un hostile et un civil.
        H(5, 35.5f, 3f, 23.5f, 47f, 40f),
        H(5, 52.5f, 3f, 23.5f, 47f, 40f),
        H(5, 25f, 0f, 44f, 34f, 31f),
        C(5, 27.5f, 0f, 36.5f, 34f, 31f),
        // 6. La cour, une fois sauté : trois hostiles, deux civils.
        H(6, 24f, 0f, 25.5f, 33f, 31f),
        H(6, 29.5f, 0f, 48f, 33f, 31f),
        H(6, 23.5f, 0f, 31f, 33f, 31f),
        C(6, 24f, 0f, 38.5f, 33f, 31f),
        C(6, 31.5f, 0f, 46f, 33f, 31f),
        // 7. Le couloir du retour : deux hostiles, un civil, et l'arrivée.
        H(7, 24f, 0f, 6f, 26f, 21f),
        H(7, 28.5f, 0f, 0.5f, 26f, 21f),
        C(7, 26.5f, 0f, 12f, 26f, 21f),
    };

    private static Bounds Box3(Vector3 min, Vector3 max)
    {
        var b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    /// <summary>Une boîte donnée par ses coins (plus lisible pour les murs d'un plan).</summary>
    private static GameObject Span(string name, Transform parent, Vector3 min, Vector3 max, Material material, bool collider = true)
        => Box(name, parent, (min + max) * 0.5f, max - min, material, collider);

    private static void BuildCourse(GameObject targetPrefab, Material grid, Material dark)
    {
        var level = new GameObject("La Fosse (décor)").transform;
        const float wall = 0.4f, inner = 3.5f, house = 2.7f, roof = 3f;

        // Le sol et l'enceinte (le mur ouest est celui du stand, percé de la porte).
        Span("Sol", level, new Vector3(22f, -1f, -10f), new Vector3(62f, 0f, 50f), grid);
        Span("Mur sud", level, new Vector3(22.5f, 0f, -10.5f), new Vector3(62.5f, 6f, -9.5f), grid);
        Span("Mur est", level, new Vector3(61.5f, 0f, -10.5f), new Vector3(62.5f, 6f, 50.5f), grid);
        Span("Mur nord", level, new Vector3(22.5f, 0f, 49.5f), new Vector3(62.5f, 6f, 50.5f), grid);

        // Le départ et le couloir du retour, séparés de la cour par un mur ; la ligne de départ et
        // l'arrivée au sol.
        Span("Mur du couloir", level, new Vector3(30f - wall / 2, 0f, -3f), new Vector3(30f + wall / 2, inner, 22f), grid);
        Span("Ligne de départ", level, new Vector3(29.9f, 0f, -9.5f), new Vector3(30.1f, 0.012f, -3f), dark, collider: false);
        Span("Ligne d'arrivée", level, new Vector3(22.5f, 0f, -3.1f), new Vector3(30f, 0.012f, -2.9f), dark, collider: false);
        FloorText(level, "DÉPART  >>>", new Vector3(28.4f, 0.013f, -6.25f), 90f, 0.06f);
        FloorText(level, "ARRIVÉE", new Vector3(26.25f, 0.013f, -2.2f), 180f, 0.06f);

        // 0-1. La cour et sa barricade (1 m : on la franchit).
        Span("Barricade", level, new Vector3(42f, 0f, -9.5f), new Vector3(42.6f, 1f, 8f - wall / 2), dark);
        FloorText(level, "FRANCHIR", new Vector3(40.6f, 0.013f, 0f), 90f, 0.05f);
        Span("Mur de la cour", level, new Vector3(30f, 0f, 8f - wall / 2), new Vector3(54f, inner, 8f + wall / 2), grid);

        // 2. L'allée, fermée au nord : on entre dans le bâtiment.
        Span("Mur de l'allée", level, new Vector3(54f - wall / 2, 0f, 8f), new Vector3(54f + wall / 2, inner, 22f), grid);
        Span("Fond de l'allée", level, new Vector3(54f, 0f, 30f - wall / 2), new Vector3(61.5f, inner, 30f + wall / 2), grid);
        Span("Rail", level, new Vector3(55.2f, 0f, 20.9f), new Vector3(60.8f, 0.012f, 21.1f), dark, collider: false);
        Span("Mur sud de la cour", level, new Vector3(30f, 0f, 22f - wall / 2), new Vector3(34f, inner, 22f + wall / 2), grid);

        // 3. Le bâtiment (x 34 à 54, z 22 à 40) : un rez-de-chaussée de deux pièces sous un toit.
        Span("Bâtiment, mur sud", level, new Vector3(34f, 0f, 22f - wall / 2), new Vector3(54f, house, 22f + wall / 2), grid);
        Span("Bâtiment, mur est (sud)", level, new Vector3(54f - wall / 2, 0f, 22f), new Vector3(54f + wall / 2, house, 26f), grid);
        Span("Bâtiment, mur est (nord)", level, new Vector3(54f - wall / 2, 0f, 28f), new Vector3(54f + wall / 2, house, 40f), grid);
        Span("Bâtiment, linteau est", level, new Vector3(54f - wall / 2, 2.2f, 26f), new Vector3(54f + wall / 2, house, 28f), grid);
        Span("Bâtiment, mur ouest", level, new Vector3(34f - wall / 2, 0f, 22f), new Vector3(34f + wall / 2, house, 40f), grid);
        Span("Bâtiment, mur nord", level, new Vector3(34f, 0f, 40f - wall / 2), new Vector3(52f, house, 40f + wall / 2), grid);
        Span("Bâtiment, mur nord (est)", level, new Vector3(53.6f, 0f, 40f - wall / 2), new Vector3(54f, house, 40f + wall / 2), grid);
        Span("Bâtiment, linteau nord", level, new Vector3(52f, 2.2f, 40f - wall / 2), new Vector3(53.6f, house, 40f + wall / 2), grid);
        Span("Bâtiment, cloison", level, new Vector3(44f - wall / 2, 0f, 22f), new Vector3(44f + wall / 2, house, 34f), grid);
        Span("Toit", level, new Vector3(34f - wall / 2, house, 22f - wall / 2), new Vector3(54f + wall / 2, roof, 40f + wall / 2), grid);
        PointLight(level, new Vector3(39f, 2.3f, 31f));
        PointLight(level, new Vector3(49f, 2.3f, 27f));
        PointLight(level, new Vector3(49f, 2.3f, 37f));

        // 4. L'escalier, le long du mur nord, monte vers l'ouest jusqu'au palier, à la hauteur du toit. On
        // sort du bâtiment par la porte nord, on longe le pied de l'escalier vers l'est (1,8 m de passage),
        // et on le monte en revenant vers l'ouest.
        const int steps = 15;
        const float stairBottom = 55.5f, stepDepth = 0.4f;
        for (int i = 1; i <= steps; i++)
        {
            float x1 = stairBottom - stepDepth * (i - 1), x0 = x1 - stepDepth;
            Span($"Marche {i}", level, new Vector3(x0, 0f, 42f), new Vector3(x1, roof * i / steps, 44.5f), grid);
        }
        float landingEnd = stairBottom - stepDepth * steps;   // 49,5
        Span("Palier", level, new Vector3(44f, 0f, 40f + wall / 2), new Vector3(landingEnd, roof, 44.5f), grid);

        // 5. Le parapet du toit (1 m), ouvert sur le palier et, à l'ouest, là où l'on saute dans la cour.
        const float parapet = 1f, rim = 0.3f;
        Span("Parapet sud", level, new Vector3(34f, roof, 22f - rim / 2), new Vector3(54f, roof + parapet, 22f + rim / 2), grid);
        Span("Parapet est", level, new Vector3(54f - rim / 2, roof, 22f), new Vector3(54f + rim / 2, roof + parapet, 40f), grid);
        Span("Parapet ouest (sud)", level, new Vector3(34f - rim / 2, roof, 22f), new Vector3(34f + rim / 2, roof + parapet, 29f), grid);
        Span("Parapet ouest (nord)", level, new Vector3(34f - rim / 2, roof, 33f), new Vector3(34f + rim / 2, roof + parapet, 40f), grid);
        Span("Parapet nord (ouest)", level, new Vector3(34f, roof, 40f - rim / 2), new Vector3(44f, roof + parapet, 40f + rim / 2), grid);
        Span("Parapet nord (est)", level, new Vector3(landingEnd, roof, 40f - rim / 2), new Vector3(54f, roof + parapet, 40f + rim / 2), grid);
        FloorText(level, "SAUTER  >>>", new Vector3(35.6f, roof + 0.013f, 31f), 270f, 0.05f);

        // 6. La cour : deux caisses, hors des lignes de tir.
        Span("Caisse", level, new Vector3(32.4f, 0f, 24.5f), new Vector3(33.8f, 1.2f, 25.9f), dark);
        Span("Caisse", level, new Vector3(22.6f, 0f, 45.5f), new Vector3(24f, 1.2f, 46.9f), dark);

        // Les cibles.
        var targets = new GameObject("La Fosse (cibles)").transform;
        int hostiles = 0, civils = 0;
        foreach (CourseTargetSpec spec in CourseTargets)
        {
            string label = spec.civil ? $"Civil {++civils}" : $"Hostile {++hostiles}";
            PlaceCourseTarget(targetPrefab, targets, spec, $"{label} ({CourseSections[spec.section].name})");
        }

        // Le parcours : zones, sections, chrono, bilan (ShootingCourse, ShootingCourseHud).
        var course = new GameObject("La Fosse");
        course.AddComponent<NetworkObject>();
        var logic = course.AddComponent<ShootingCourse>();
        course.AddComponent<ShootingCourseHud>();
        var so = new SerializedObject(logic);
        so.FindProperty("startZone").boundsValue = CourseStartZone;
        SerializedProperty list = so.FindProperty("sections");
        list.arraySize = CourseSections.Length;
        for (int i = 0; i < CourseSections.Length; i++)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("name").stringValue = CourseSections[i].name;
            element.FindPropertyRelative("bounds").boundsValue = CourseSections[i].bounds;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        // Le tableau des meilleurs temps, au mur du départ.
        Span("Tableau", level, new Vector3(24.25f, 0.9f, -9.5f), new Vector3(28.25f, 3.3f, -9.4f), dark);
        var board = new GameObject("Tableau des temps");
        board.transform.SetPositionAndRotation(new Vector3(26.25f, 2.1f, -9.37f), Quaternion.Euler(0f, 180f, 0f));
        var boardText = board.AddComponent<TextMesh>();
        boardText.fontSize = 64;
        boardText.characterSize = 0.032f;
        boardText.anchor = TextAnchor.MiddleCenter;
        boardText.alignment = TextAlignment.Center;
        boardText.color = new Color(0.95f, 0.9f, 0.75f);
        boardText.text = "LA FOSSE";
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        boardText.font = font;
        board.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        board.AddComponent<CourseBoard>();
    }

    private static void PlaceCourseTarget(GameObject prefab, Transform parent, CourseTargetSpec spec, string name)
    {
        var target = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        target.name = name;
        Vector3 look = new Vector3(spec.lookAt.x, spec.position.y, spec.lookAt.y) - spec.position;
        target.transform.SetPositionAndRotation(spec.position, Quaternion.LookRotation(look.sqrMagnitude > 1e-4f ? look : Vector3.back, Vector3.up));

        var dummy = new SerializedObject(target.GetComponent<TrainingDummy>());
        dummy.FindProperty("tint").colorValue = spec.civil ? CivilTint : HostileTint;
        dummy.ApplyModifiedPropertiesWithoutUndo();

        var course = new SerializedObject(target.GetComponent<CourseTarget>());
        course.FindProperty("role").intValue = spec.civil ? (int)CourseTargetRole.Civil : (int)CourseTargetRole.Hostile;
        course.FindProperty("section").intValue = spec.section;
        course.FindProperty("moves").boolValue = spec.moves;
        course.FindProperty("railEnd").vector3Value = spec.railEnd;
        course.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Un texte peint au sol, lisible par qui marche vers `towards` (degrés, autour de Y).</summary>
    private static void FloorText(Transform parent, string text, Vector3 position, float towards, float size)
    {
        var label = new GameObject($"Au sol : {text}");
        label.transform.SetParent(parent, false);
        label.transform.SetPositionAndRotation(position, Quaternion.Euler(90f, towards, 0f));
        var mesh = label.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.fontSize = 64;
        mesh.characterSize = size;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = new Color(0.25f, 0.27f, 0.3f);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mesh.font = font;
        label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }

    private static void PointLight(Transform parent, Vector3 position)
    {
        var lamp = new GameObject("Lampe");
        lamp.transform.SetParent(parent, false);
        lamp.transform.position = position;
        var light = lamp.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 11f;
        light.intensity = 1.6f;
        light.shadows = LightShadows.None;
    }

    /// <summary>
    /// Le menu de pause du stand (2026-10-27, demandé par l'utilisateur pour changer ses réglages au stand) :
    /// Échap l'ouvre (PauseMenu), avec les Options du jeu (OptionsController) et la fenêtre des armes. Le
    /// document est posé avant ses contrôleurs : ils retrouvent ses éléments en s'activant.
    /// </summary>
    private static void BuildInterface()
    {
        var ui = new GameObject("Interface");
        var document = ui.AddComponent<UnityEngine.UIElements.UIDocument>();
        document.panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(PanelSettingsPath);
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(PausePath);

        var options = new SerializedObject(ui.AddComponent<OptionsController>());
        options.FindProperty("document").objectReferenceValue = document;
        options.FindProperty("bindRowTemplate").objectReferenceValue = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(BindRowPath);
        options.ApplyModifiedPropertiesWithoutUndo();

        var pause = new SerializedObject(ui.AddComponent<PauseMenu>());
        pause.FindProperty("document").objectReferenceValue = document;
        pause.ApplyModifiedPropertiesWithoutUndo();

        var sounds = new SerializedObject(ui.AddComponent<UiSounds>());
        sounds.FindProperty("sounds").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameSounds>(GameSoundsPath);
        sounds.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void PlaceDummy(GameObject prefab, Transform parent, Vector2 p, int index)
    {
        var dummy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        dummy.name = $"Mannequin {index} ({p.y:0} m)";
        dummy.transform.SetPositionAndRotation(new Vector3(p.x, 0f, p.y), Quaternion.Euler(0f, 180f, 0f));

        // La distance, au sol, devant lui.
        var label = new GameObject("Distance");
        label.transform.SetParent(dummy.transform, false);
        label.transform.localPosition = new Vector3(0f, 0.01f, 1.1f);
        label.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
        var text = label.AddComponent<TextMesh>();
        text.text = $"{p.y:0} m";
        text.fontSize = 64;
        text.characterSize = 0.05f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = new Color(0.25f, 0.27f, 0.3f);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.font = font;
        label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }

    /// <summary>Le mannequin : vie, surface touchable du préfab joueur, apparence du robot. Une cible de
    /// la Fosse y ajoute CourseTarget et ne se relève pas seule.</summary>
    private static GameObject BuildDummyPrefab(string path, bool courseTarget)
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform model = player.transform.Find("Model");
        PlayerHitbox playerHitbox = player.GetComponentInChildren<PlayerHitbox>(true);
        float eye = new SerializedObject(player.GetComponent<PlayerLocomotion>()).FindProperty("standingProfile.cameraHeight").floatValue;
        AnimationClip idle = AssetDatabase.LoadAllAssetsAtPath(IdleClipPath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_ProjectArena", "Stand");

        var root = new GameObject(courseTarget ? "CibleParcours" : "Mannequin") { layer = LayerMask.NameToLayer("Player") };
        root.AddComponent<NetworkObject>();
        root.AddComponent<Health>();
        var dummy = root.AddComponent<TrainingDummy>();
        if (courseTarget) root.AddComponent<CourseTarget>();
        // Le corps : on ne le traverse pas, mais les balles passent (layer Player, hors de worldMask) et
        // ne s'arrêtent que sur la surface touchable.
        var body = root.AddComponent<CapsuleCollider>();
        body.center = new Vector3(0f, 0.9f, 0f);
        body.height = 1.8f;
        body.radius = 0.3f;

        var hitboxObject = new GameObject("Hitbox");
        hitboxObject.transform.SetParent(root.transform, false);
        var hitbox = hitboxObject.AddComponent<PlayerHitbox>();
        EditorUtility.CopySerialized(playerHitbox, hitbox);

        var dummySo = new SerializedObject(dummy);
        dummySo.FindProperty("robotModel").objectReferenceValue = model != null ? model.gameObject : null;
        dummySo.FindProperty("idleClip").objectReferenceValue = idle;
        dummySo.FindProperty("eyeHeight").floatValue = eye;
        dummySo.FindProperty("regenerates").boolValue = !courseTarget;
        dummySo.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material material, bool collider = true)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.position = center;
        box.transform.localScale = size;
        if (material != null) box.GetComponent<MeshRenderer>().sharedMaterial = material;
        if (!collider) Object.DestroyImmediate(box.GetComponent<Collider>());
        box.isStatic = true;
        return box;
    }

    private static void AddToBuild(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Any(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
