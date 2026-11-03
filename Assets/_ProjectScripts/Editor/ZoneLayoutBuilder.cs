using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Pose les emplacements de la Zone dans l'arène de test (GDD § 11, T8) : Duel Arena > Arène > Poser les zones. Quatre
/// emplacements de la salle B-01, dans leur ordre (proposition de Claude) : le premier à égale distance des deux postes
/// (le flanc ouest), puis devant le poste B, le flanc est, devant le poste A ; chacun posé sur le sol, sur une place
/// dégagée (vérifiée par GameModesTests). Une autre arène aura les siens. On corrige ce script et on relance le menu,
/// jamais la scène.
/// </summary>
public static class ZoneLayoutBuilder
{
    public const string GroupName = "Zones";
    public const string ScenePath = "Assets/_ProjectScenes/MultiTestScene.unity";
    private const string MaterialPath = "Assets/_ProjectArena/Modes/Zone_Lueur.mat";
    private const string ArcMaterialPath = "Assets/_ProjectArena/Weapon/Gant/ArcElectrique.mat";
    private const string SoundsPath = "Assets/_ProjectArt/Audio/GameSounds.asset";
    private const int WorldMask = 1;

    /// <summary>Les emplacements de la salle B-01, dans l'ordre où la zone les parcourt (x, z).</summary>
    public static readonly (string name, Vector2 place)[] Places =
    {
        ("Flanc ouest", new Vector2(-18f, 30f)),
        ("Devant le poste B", new Vector2(0f, 44f)),
        ("Flanc est", new Vector2(18f, 30f)),
        ("Devant le poste A", new Vector2(0f, 16f)),
    };

    [MenuItem("Duel Arena/Arène/Poser les zones")]
    public static void Build()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            Debug.Log("[Modes] " + BuildIn(scene));
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    public static string BuildIn(Scene scene)
    {
        Material zoneMaterial = ZoneMaterial();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == GroupName) Object.DestroyImmediate(root);
        }

        var group = new GameObject(GroupName);
        SceneManager.MoveGameObjectToScene(group, scene);
        ZoneLayout layout = group.AddComponent<ZoneLayout>();
        Physics.SyncTransforms();
        var made = new Transform[Places.Length];
        for (int i = 0; i < Places.Length; i++)
        {
            Vector2 place = Places[i].place;
            var top = new Vector3(place.x, 5f, place.y);
            float floor = Physics.Raycast(top, Vector3.down, out RaycastHit hit, 20f, WorldMask, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
            var zone = new GameObject($"{i + 1}. {Places[i].name}").transform;
            zone.SetParent(group.transform, false);
            zone.position = new Vector3(place.x, floor, place.y);
            made[i] = zone;
        }

        var so = new SerializedObject(layout);
        SerializedProperty zones = so.FindProperty("zones");
        SerializedProperty names = so.FindProperty("zoneNames");
        zones.arraySize = made.Length;
        names.arraySize = made.Length;
        for (int i = 0; i < made.Length; i++)
        {
            zones.GetArrayElementAtIndex(i).objectReferenceValue = made[i];
            names.GetArrayElementAtIndex(i).stringValue = Places[i].name;
        }
        so.FindProperty("zoneMaterial").objectReferenceValue = zoneMaterial;
        so.FindProperty("arcMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ArcMaterialPath);
        so.FindProperty("sounds").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameSounds>(SoundsPath);
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"{made.Length} zones posées dans {scene.name}.";
    }

    /// <summary>La lumière de la zone : URP Particles/Unlit, transparente, ajoutée, des deux faces ; sa couleur se pose
    /// en jeu (ZoneLayout), l'alpha des sommets efface la paroi vers le haut.</summary>
    private static Material ZoneMaterial()
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 2f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.SetColor("_BaseColor", Color.white);
        material.renderQueue = (int)RenderQueue.Transparent;
        return RobotChassisBuilder.SaveMaterial(MaterialPath, material);
    }
}
