using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Importe les props du décor exportés de Blender (2026-10-27, Tools/Blender/decor/exporter_decor.py) : Duel Arena >
/// Arène > Importer les props de Blender. Pour chaque dossier `Props/<CODE>/` : les réglages d'import des armes
/// (WeaponImporter.ConfigureModel, plus le dépliage des lightmaps : un prop est statique), un matériau DuelArena/Decor
/// par clé de la fiche, en espace « pièce de Blender », aux paramètres de kit.materiau (WeaponImporter.ApplyParams :
/// la même usure que dans Blender), puis le préfab `<CODE>.prefab`, sans collider (un prop ne change jamais le jeu).
/// On corrige le script du prop dans Blender et on réexporte, jamais le préfab ni ses matériaux.
/// </summary>
public static class DecorImporter
{
    public const string Folder = "Assets/_ProjectArena/Decor/Props";
    private const string ShaderName = "DuelArena/Decor";

    [MenuItem("Duel Arena/Arène/Importer les props de Blender")]
    public static void ImportAllFromMenu() => Debug.Log("[Décor] " + ImportAll());

    public static string ImportAll()
    {
        var report = new System.Text.StringBuilder();
        if (!AssetDatabase.IsValidFolder(Folder)) return "Pas de props à importer (" + Folder + ").";
        AssetDatabase.Refresh();
        foreach (string dir in Directory.GetDirectories(Folder))
        {
            string code = Path.GetFileName(dir);
            string sheetPath = $"{dir}/{code}_Fiche.json".Replace('\\', '/');
            if (!File.Exists(sheetPath)) continue;
            GameObject prefab = Import(dir.Replace('\\', '/'), code);
            report.AppendLine($"{code} : {AssetDatabase.GetAssetPath(prefab)}");
        }
        AssetDatabase.SaveAssets();
        return report.ToString();
    }

    public static GameObject Import(string dir, string code)
    {
        var sheet = JsonUtility.FromJson<WeaponImporter.Sheet>(File.ReadAllText($"{dir}/{code}_Fiche.json"));
        string fbxPath = $"{dir}/{code}_Modele.fbx";
        WeaponImporter.ConfigureModel(fbxPath);
        var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        importer.generateSecondaryUV = true;
        importer.SaveAndReimport();

        string aoPath = $"{dir}/{code}_Occlusion.png";
        Texture2D ao = null;
        if (File.Exists(aoPath))
        {
            WeaponImporter.ConfigureOcclusion(aoPath);
            ao = AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath);
        }

        Shader shader = Shader.Find(ShaderName);
        string matDir = $"{dir}/Materiaux";
        if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(dir, "Materiaux");
        var byName = new Dictionary<string, Material>();
        foreach (WeaponImporter.MaterialSheet entry in sheet.materiaux)
        {
            string path = $"{matDir}/{code}_{entry.cle}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            WeaponImporter.ApplyParams(material, entry.finitions[0], ao != null ? ao : Texture2D.whiteTexture);
            material.SetFloat("_Espace", 1f);
            material.SetFloat("_Motif", 0f);
            material.SetVector("_Soubassement", Vector4.zero);
            material.SetVector("_Hachures", Vector4.zero);
            material.SetVector("_Impacts", Vector4.zero);
            material.SetVector("_Crasse", new Vector4(0.45f, 0.35f, 0f, 0f));
            material.SetFloat("_Contour", 2f);
            material.SetFloat("_ContourLisse", 1f);
            material.SetShaderPassEnabled(DecorOutlineFeature.LightMode, true);   // le contour, en cel-shading et en mêlés
            material.globalIlluminationFlags = entry.finitions[0].forceEmission > 0f
                ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(material);
            byName[entry.nom] = material;
        }

        string prefabPath = $"{dir}/{code}.prefab";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = code;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null || !byName.TryGetValue(slots[i].name, out Material m))
                        throw new System.InvalidOperationException($"{code} : la pièce {renderer.name} porte un matériau inconnu de la fiche ({(slots[i] != null ? slots[i].name : "aucun")}).");
                    slots[i] = m;
                }
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            }
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true)) ArenaDecorBuilder.SetStatic(t.gameObject);
            return PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    /// <summary>Le préfab d'un prop, ou null s'il n'a pas été importé.</summary>
    public static GameObject Prefab(string code) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{code}/{code}.prefab");

    /// <summary>Un maillage d'un modèle de Blender, par son nom (un marquage de MQ1).</summary>
    public static Mesh MeshOf(string code, string name)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath($"{Folder}/{code}/{code}_Modele.fbx"))
        {
            if (o is Mesh mesh && mesh.name == name) return mesh;
        }
        return null;
    }
}
