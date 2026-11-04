using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Les matériaux des gerbes d'un robot qui vole en pièces (RobotBurst, 2026-11-03) : Duel Arena > Mort > Préparer les
/// éclats du robot. L'huile (des gouttes rondes qui luisent, ambre sombre par la couleur des particules), les étincelles
/// (une lueur ajoutée), les éclats de blindage (de petites plaques éclairées, grises ou à la couleur du robot), et le
/// sillage de la balle de la kill cam (une lueur ajoutée sans texture). Dans Resources, que RobotBurst charge par leur nom :
/// ils vont dans la build avec leurs shaders (URP Particles). Les deux textures (la goutte, la lueur) sont dessinées ici.
/// On corrige ce script et on relance le menu, jamais les matériaux.
/// </summary>
public static class RobotBurstMaterials
{
    private const string Parent = "Assets/_ProjectArena";
    private const string Folder = Parent + "/Mort";
    private const string ResourcesFolder = Folder + "/Resources";
    private const int TextureSize = 64;

    [MenuItem("Duel Arena/Mort/Préparer les éclats du robot")]
    public static void Build()
    {
        EnsureFolder(Parent, "Mort");
        EnsureFolder(Folder, "Resources");
        Texture2D drop = SaveTexture(Folder + "/Goutte.png", Drop);
        Texture2D glow = SaveTexture(Folder + "/Lueur.png", Glow);

        Save(RobotBurst.OilMaterialName, Particles("Unlit", transparent: true, additive: false, drop));
        Save(RobotBurst.SparkMaterialName, Particles("Unlit", transparent: true, additive: true, glow));
        Save(RobotBurst.WakeMaterialName, Particles("Unlit", transparent: true, additive: true, null));
        Material debris = Particles("Lit", transparent: false, additive: false, null);
        debris.SetFloat("_Metallic", 0.6f);
        debris.SetFloat("_Smoothness", 0.45f);
        Save(RobotBurst.DebrisMaterialName, debris);
        AssetDatabase.SaveAssets();
        Debug.Log("[Mort] Éclats du robot préparés : " + ResourcesFolder);
    }

    // Un matériau de particules d'URP : la couleur vient des particules (blanc ici), transparente (mêlée ou ajoutée) ou
    // opaque, des deux faces.
    private static Material Particles(string kind, bool transparent, bool additive, Texture2D texture)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Particles/" + kind));
        material.SetColor("_BaseColor", Color.white);
        if (texture != null) material.SetTexture("_BaseMap", texture);
        material.SetFloat("_Surface", transparent ? 1f : 0f);
        material.SetFloat("_Blend", additive ? 2f : 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        if (transparent) material.renderQueue = (int)RenderQueue.Transparent;
        return material;
    }

    private static void Save(string name, Material material)
        => RobotChassisBuilder.SaveMaterial(ResourcesFolder + "/" + name + ".mat", material);

    // La goutte : un disque au bord doux, plus sombre vers le bord, et un reflet en haut à gauche (l'huile luit).
    private static Color Drop(float x, float y)
    {
        float r = Mathf.Sqrt(x * x + y * y);
        float alpha = 1f - Mathf.SmoothStep(0.75f, 1f, r);
        float highlight = 1f - Mathf.SmoothStep(0f, 0.32f, Vector2.Distance(new Vector2(x, y), new Vector2(-0.32f, 0.34f)));
        float shade = Mathf.Lerp(0.62f, 0.4f, r * r) + 0.5f * highlight;
        return new Color(shade, shade, shade, alpha);
    }

    // La lueur : blanche, en cloche, nulle au bord (une étincelle étirée par sa vitesse en garde les bouts doux).
    private static Color Glow(float x, float y)
    {
        float r2 = x * x + y * y;
        float edge = Mathf.Exp(-5f);
        float alpha = Mathf.Clamp01((Mathf.Exp(-5f * r2) - edge) / (1f - edge));
        return new Color(1f, 1f, 1f, alpha);
    }

    // Une texture carrée dessinée par `paint` (x et y de -1 à 1), écrite en PNG et importée lisse, sans répétition.
    private static Texture2D SaveTexture(string path, System.Func<float, float, Color> paint)
    {
        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        var pixels = new Color[TextureSize * TextureSize];
        for (int j = 0; j < TextureSize; j++)
        {
            for (int i = 0; i < TextureSize; i++)
            {
                float x = (i + 0.5f) / TextureSize * 2f - 1f;
                float y = (j + 0.5f) / TextureSize * 2f - 1f;
                pixels[j * TextureSize + i] = paint(x, y);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }
}
