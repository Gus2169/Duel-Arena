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
/// </summary>
public static class ShootingRangeBuilder
{
    public const string ScenePath = "Assets/_ProjectScenes/StandDeTir.unity";
    public const string DummyPrefabPath = "Assets/_ProjectArena/Stand/Mannequin.prefab";
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const string IdleClipPath = "Assets/_ProjectArt/Animations/MainsNues/Y Bot@Idle.fbx";
    private const string GridMaterialGuid = "c22777d6e868e4f2fb421913386b154e";   // le quadrillage de l'arène (ProBuilder)
    private const string DarkMaterialPath = "Assets/_ProjectArena/Tribune/Materiaux/Passerelle_GardeCorps.mat";

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
        GameObject dummyPrefab = BuildDummyPrefab();

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
        Box("Mur droit", level, new Vector3(22f, 3f, 28f), new Vector3(1f, 6f, 76f), grid);
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
        Debug.Log($"[Stand] Construit : {ScenePath}, {OpenDummies.Length + 1} mannequins.");
        if (!string.IsNullOrEmpty(previousPath) && previousPath != ScenePath) EditorSceneManager.OpenScene(previousPath);
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

    /// <summary>Le mannequin : vie, surface touchable du préfab joueur, apparence du robot.</summary>
    private static GameObject BuildDummyPrefab()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform model = player.transform.Find("Model");
        PlayerHitbox playerHitbox = player.GetComponentInChildren<PlayerHitbox>(true);
        float eye = new SerializedObject(player.GetComponent<PlayerLocomotion>()).FindProperty("standingProfile.cameraHeight").floatValue;
        AnimationClip idle = AssetDatabase.LoadAllAssetsAtPath(IdleClipPath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        string folder = System.IO.Path.GetDirectoryName(DummyPrefabPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_ProjectArena", "Stand");

        var root = new GameObject("Mannequin") { layer = LayerMask.NameToLayer("Player") };
        root.AddComponent<NetworkObject>();
        root.AddComponent<Health>();
        var dummy = root.AddComponent<TrainingDummy>();
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
        dummySo.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, DummyPrefabPath);
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
