using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Construit le module mémoire de l'Élimination confirmée (GDD § 11, 2026-11-02) : Duel Arena > Modes > Construire le
/// module mémoire. Un objet du mode, sans plus (il n'entre pas dans le lore, I11) : un boîtier sombre de 22 × 30 cm,
/// un cœur et un liseré qui luisent à la couleur du robot détruit (posée en jeu par MemoryModule), une barrette de
/// contacts, une lumière, aucun collider. En primitives, en attendant un modèle de Blender s'il en faut un (dette
/// n° 86). On corrige ce script et on relance le menu, jamais le préfab.
/// </summary>
public static class MemoryModuleBuilder
{
    private const string Folder = "Assets/_ProjectArena/Modes";
    private const string ResourcesFolder = Folder + "/Resources";
    private const string PrefabPath = ResourcesFolder + "/" + MemoryModule.ResourceName + ".prefab";
    private const string CaseMaterialPath = Folder + "/ModuleMemoire_Boitier.mat";
    private const string GlowMaterialPath = Folder + "/ModuleMemoire_Lueur.mat";

    // Le boîtier, debout : il tourne sur lui-même autour de la verticale (MemoryModule).
    private static readonly Vector3 CaseSize = new Vector3(0.22f, 0.30f, 0.05f);

    [MenuItem("Duel Arena/Modes/Construire le module mémoire")]
    public static void Build()
    {
        EnsureFolder("Assets/_ProjectArena", "Modes");
        EnsureFolder(Folder, "Resources");
        Material caseMaterial = RobotChassisBuilder.SaveMaterial(CaseMaterialPath,
            RobotChassisBuilder.LitMaterial(new Color(0.16f, 0.17f, 0.19f), 0.55f, 0.7f, 0f));
        // Blanche ici : la couleur du robot détruit se pose en jeu (MemoryModule.Show).
        Material glowMaterial = RobotChassisBuilder.SaveMaterial(GlowMaterialPath,
            RobotChassisBuilder.LitMaterial(Color.white, 0.6f, 0f, 3f));

        var root = new GameObject(MemoryModule.ResourceName);
        try
        {
            Part(root, "Boitier", Vector3.zero, CaseSize, caseMaterial, shadows: true);
            // Le cœur dépasse un peu des deux faces : il luit des deux côtés.
            Renderer core = Part(root, "Coeur", new Vector3(0f, 0.02f, 0f), new Vector3(0.12f, 0.16f, CaseSize.z + 0.008f), glowMaterial);
            // Le liseré du haut et des côtés, qui dessine le contour du boîtier de loin.
            float rim = 0.012f, depth = CaseSize.z + 0.004f;
            Renderer top = Part(root, "Lisere_Haut", new Vector3(0f, CaseSize.y / 2f - rim / 2f, 0f), new Vector3(CaseSize.x, rim, depth), glowMaterial);
            Renderer left = Part(root, "Lisere_Gauche", new Vector3(-CaseSize.x / 2f + rim / 2f, 0f, 0f), new Vector3(rim, CaseSize.y, depth), glowMaterial);
            Renderer right = Part(root, "Lisere_Droite", new Vector3(CaseSize.x / 2f - rim / 2f, 0f, 0f), new Vector3(rim, CaseSize.y, depth), glowMaterial);
            // La barrette de contacts, en bas : ce qui dit « module qu'on enfiche ».
            Part(root, "Contacts", new Vector3(0f, -CaseSize.y / 2f - 0.012f, 0f), new Vector3(0.15f, 0.024f, 0.03f), caseMaterial);

            var lightObject = new GameObject("Lumiere");
            lightObject.transform.SetParent(root.transform, false);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 3f;
            light.intensity = 2f;
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            MemoryModule module = root.AddComponent<MemoryModule>();
            var serialized = new SerializedObject(module);
            SerializedProperty glows = serialized.FindProperty("glowRenderers");
            Renderer[] glowing = { core, top, left, right };
            glows.arraySize = glowing.Length;
            for (int i = 0; i < glowing.Length; i++) glows.GetArrayElementAtIndex(i).objectReferenceValue = glowing[i];
            serialized.FindProperty("glowLight").objectReferenceValue = light;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Modes] Module mémoire construit : {PrefabPath}.");
    }

    /// <summary>Une pièce en cube, sans collider (rien ne s'arrête sur un module), sans lumière cuite.</summary>
    private static Renderer Part(GameObject root, string name, Vector3 position, Vector3 size, Material material, bool shadows = false)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(root.transform, false);
        part.transform.localPosition = position;
        part.transform.localScale = size;
        var renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.receiveGI = ReceiveGI.LightProbes;
        GameObjectUtility.SetStaticEditorFlags(part, 0);
        return renderer;
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }
}
