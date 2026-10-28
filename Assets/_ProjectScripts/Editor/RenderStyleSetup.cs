using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Prépare le réglage du rendu (2026-10-27, RenderStyle) : Duel Arena > Rendu > Préparer le réglage du rendu.
/// - Les paires de matériaux des robots (Resources/RobotStyleMaterials) : chaque matériau des pièces des châssis du
///   préfab joueur, en face de son double de l'essai des rendus (RenderTrialBuilder) : « CT01_Peinture » →
///   « CT01_Cel_Peinture », de même pour l'OS-02 ; le prototype (le Y Bot) : sa coque et ses articulations. La visière
///   garde le sien (DuelArena/EcranVisage : l'écran de la mort).
/// - Le contour du décor (DecorOutlineFeature), ajouté au renderer d'URP de chaque qualité s'il n'y est pas.
/// Le relancer ne change rien ; à relancer quand un châssis gagne un matériau.
/// </summary>
public static class RenderStyleSetup
{
    public const string AssetPath = "Assets/_ProjectArt/Robots/Resources/" + RobotStyleMaterials.ResourceName + ".asset";
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const string VisorShader = "DuelArena/EcranVisage";

    [MenuItem("Duel Arena/Rendu/Préparer le réglage du rendu")]
    public static void SetupFromMenu() => Debug.Log("[Rendu] " + Setup());

    public static string Setup()
    {
        var report = new StringBuilder();
        report.AppendLine(BuildRobotMaterials());
        report.AppendLine(InstallOutlineFeature());
        AssetDatabase.SaveAssets();
        return report.ToString();
    }

    /// <summary>Les paires de matériaux des robots, écrites dans l'asset de Resources.</summary>
    public static string BuildRobotMaterials()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform model = prefab != null ? prefab.transform.Find("Model") : null;
        if (model == null) return "Matériaux des robots : préfab joueur ou « Model » introuvable.";

        var pairs = new List<RobotStyleMaterials.Pair>();
        var missing = new List<string>();
        var seen = new HashSet<Material>();
        int kept = 0;
        foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (RobotChassis.OwnerOf(r.name) < 0) continue;   // seulement les pièces des châssis
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null || !seen.Add(m)) continue;
                if (m.shader != null && m.shader.name == VisorShader)
                {
                    kept++;
                    continue;
                }
                Material cel = CelOf(r.name, m);
                if (cel != null) pairs.Add(new RobotStyleMaterials.Pair { game = m, cel = cel });
                else missing.Add($"{r.name}/{m.name}");
            }
        }

        string folder = System.IO.Path.GetDirectoryName(AssetPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), "Resources");
        var asset = AssetDatabase.LoadAssetAtPath<RobotStyleMaterials>(AssetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<RobotStyleMaterials>();
            AssetDatabase.CreateAsset(asset, AssetPath);
        }
        asset.SetPairs(pairs.ToArray());
        EditorUtility.SetDirty(asset);
        string sans = missing.Count > 0 ? $" ; SANS DOUBLE : {string.Join(", ", missing)}" : "";
        return $"Matériaux des robots : {pairs.Count} paires, {kept} gardé (la visière){sans}.";
    }

    // Le double de cel-shading d'un matériau du jeu : « <CT01|OS02>_X » → « <CT01|OS02>_Cel_X » ; le prototype, par pièce.
    private static Material CelOf(string piece, Material m)
    {
        string path = null;
        if (m.name.StartsWith("CT01_") || m.name.StartsWith("OS02_")) path = $"{m.name.Substring(0, 5)}Cel_{m.name.Substring(5)}";
        else if (piece == "Alpha_Surface") path = "Cel_Coque";
        else if (piece == "Alpha_Joints") path = "Cel_Articulations";
        return path == null ? null : AssetDatabase.LoadAssetAtPath<Material>($"{RenderTrialBuilder.Folder}/{path}.mat");
    }

    /// <summary>Ajoute DecorOutlineFeature aux renderers d'URP du jeu (celui de chaque qualité et celui par défaut).</summary>
    public static string InstallOutlineFeature()
    {
        var assets = new HashSet<UniversalRenderPipelineAsset>();
        if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset main) assets.Add(main);
        for (int i = 0; i < QualitySettings.count; i++)
        {
            if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset quality) assets.Add(quality);
        }

        int added = 0, present = 0, revived = 0;
        foreach (UniversalRenderPipelineAsset pipeline in assets)
        {
            SerializedProperty list = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is not ScriptableRendererData data) continue;
                ScriptableRendererFeature existing = data.rendererFeatures.Find(f => f is DecorOutlineFeature);
                if (existing != null)
                {
                    present++;
                    if (existing.isActive) continue;
                    existing.SetActive(true);   // une fonction éteinte ne dessine rien
                    EditorUtility.SetDirty(existing);
                    revived++;
                    continue;
                }
                AddFeature(data);
                added++;
            }
        }
        return $"Contour du décor : ajouté à {added} renderer(s), déjà présent dans {present} (dont {revived} rallumé).";
    }

    // Comme le bouton « Add Renderer Feature » de l'inspecteur : la fonction en sous-asset, sa place dans la liste et
    // dans la table des identifiants.
    private static void AddFeature(ScriptableRendererData data)
    {
        var feature = ScriptableObject.CreateInstance<DecorOutlineFeature>();
        feature.name = "Contour du décor";
        feature.SetActive(true);   // créée par script, elle naît éteinte
        AssetDatabase.AddObjectToAsset(feature, data);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

        var so = new SerializedObject(data);
        SerializedProperty features = so.FindProperty("m_RendererFeatures");
        SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
        map.arraySize++;
        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
        so.ApplyModifiedProperties();
        data.SetDirty();
        EditorUtility.SetDirty(data);
    }
}
