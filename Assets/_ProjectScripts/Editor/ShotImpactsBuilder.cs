using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Les marques des tirs (ShotImpacts, 2026-11-04) : Duel Arena > Armes > Préparer les impacts. Une texture de huit cases, dessinée
/// ici : en haut quatre trous de balle, en bas quatre brûlures. Elle ne se pose pas sur le décor, elle le module
/// (DuelArena/MarqueImpact, son rouge) : 0,5 ne change rien, d'où son bord neutre ; son vert ajoute un rien de lumière, pour se
/// lire sur un mur noir. Le matériau va dans la build par Resources. On corrige ce script et on relance le menu, jamais la texture.
/// </summary>
public static class ShotImpactsBuilder
{
    private const string Parent = "Assets/_ProjectArena";
    private const string Folder = Parent + "/Impacts";
    private const string TexturePath = Folder + "/Marques.png";
    private const string MaterialPath = Folder + "/Resources/" + ShotImpacts.MaterialName + ".mat";
    private const int Tile = 128;

    [MenuItem("Duel Arena/Armes/Préparer les impacts")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(Parent, "Impacts");
        if (!AssetDatabase.IsValidFolder(Folder + "/Resources")) AssetDatabase.CreateFolder(Folder, "Resources");

        int width = Tile * ShotImpacts.TileColumns, height = Tile * ShotImpacts.TileRows;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        var pixels = new Color[width * height];
        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                int column = i / Tile, row = ShotImpacts.TileRows - 1 - j / Tile;
                float x = ((i % Tile) + 0.5f) / Tile * 2f - 1f;
                float y = ((j % Tile) + 0.5f) / Tile * 2f - 1f;
                pixels[j * width + i] = row == 0 ? new Color(Hole(x, y, column), HoleLip(x, y, column), 0f, 1f)
                                                 : new Color(Burn(x, y, column), BurnAsh(x, y, column), 0f, 1f);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.anisoLevel = 4;
        importer.SaveAndReimport();

        var material = new Material(Shader.Find("DuelArena/MarqueImpact"));
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
        RobotChassisBuilder.SaveMaterial(MaterialPath, material);
        AssetDatabase.SaveAssets();
        Debug.Log("[Impacts] Marques préparées : " + MaterialPath);
    }

    /// <summary>Un trou de balle, de (x, y) dans [-1 ; 1] : un cœur noir, un cratère autour à la lèvre claire, des fissures,
    /// neutre loin du centre. Fonction pure, testée.</summary>
    public static float Hole(float x, float y, int variant)
    {
        Crater(x, y, variant, out float r, out float a, out float core, out float chip, out float lip);
        float seed = variant * 7.31f + 1.7f;

        // Le cratère : assombri vers le cœur, sans rien au-delà de son bord.
        float value = 0.5f - 0.27f * (1f - Ramp(core, chip, r));
        // Sa lèvre, où la surface est arrachée, accroche la lumière.
        value += 0.22f * lip;
        // Des fissures qui partent du cratère.
        for (int i = 0; i < 5; i++)
        {
            float angle = Noise(seed * 3f + i * 2.9f) * Mathf.PI * 2f + i * 1.25f;
            float reach = chip + 0.1f + 0.3f * Noise(seed + i * 5.1f);
            float off = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, angle * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
            float along = Mathf.InverseLerp(reach, core, r);
            if (r < reach) value -= 0.24f * along * Mathf.Exp(-Mathf.Pow(off / 0.02f, 2f));
        }
        // Le cœur.
        value = Mathf.Lerp(0.02f, value, Ramp(core * 0.75f, core * 1.3f, r));
        return Mathf.Lerp(value, 0.5f, Ramp(0.8f, 0.98f, r));
    }

    /// <summary>Ce qu'un trou ajoute à la lumière de sa surface : sa lèvre, de 0 à 1. Fonction pure, testée.</summary>
    public static float HoleLip(float x, float y, int variant)
    {
        Crater(x, y, variant, out float r, out _, out _, out _, out float lip);
        return lip * (1f - Ramp(0.8f, 0.98f, r));
    }

    // La forme d'un trou : son cœur et son cratère, irréguliers, et sa lèvre, claire par endroits. Le bruit se lit sur un cercle :
    // lu sur l'angle, il laissait une entaille là où celui-ci saute.
    private static void Crater(float x, float y, int variant, out float r, out float a, out float core, out float chip, out float lip)
    {
        r = Mathf.Sqrt(x * x + y * y);
        a = Mathf.Atan2(y, x);
        float seed = variant * 7.31f + 1.7f;
        float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
        float jag = Noise2(cos * 1.6f + seed, sin * 1.6f - seed) * 0.6f + Noise2(cos * 5f - seed, sin * 5f + seed) * 0.4f;
        float fine = Noise2(cos * 11f + seed, sin * 11f - seed);
        core = 0.15f + 0.07f * jag + 0.03f * fine;
        chip = 0.32f + 0.26f * jag + 0.05f * fine;
        lip = Mathf.Exp(-Mathf.Pow((r - chip) / 0.045f, 2f)) * (0.4f + 0.6f * Noise2(cos * 3.3f + seed * 2f, sin * 3.3f));
    }

    /// <summary>Une brûlure, de (x, y) dans [-1 ; 1] : noire au milieu, au bord irrégulier, piquée de suie, neutre loin du
    /// centre. Fonction pure, testée.</summary>
    public static float Burn(float x, float y, int variant)
    {
        float r = Mathf.Sqrt(x * x + y * y);
        float seed = variant * 11.7f + 3.1f;
        float edge = BurnEdge(x, y, seed);
        // Le noir, de plus en plus dense vers le centre.
        float dark = 1f - Ramp(edge * 0.45f, edge, r);
        float value = Mathf.Lerp(0.5f, 0.035f, Mathf.Pow(dark, 0.8f));
        // La suie autour, en voile.
        float soot = (1f - Ramp(edge, edge + 0.3f, r)) * (0.5f + 0.5f * Noise2(x * 11f + seed, y * 11f));
        value -= 0.13f * soot * (1f - dark);
        return Mathf.Lerp(value, 0.5f, Ramp(0.82f, 0.98f, r));
    }

    /// <summary>Ce qu'une brûlure ajoute à la lumière de sa surface : une couronne de cendre autour du noir, de 0 à 1. Fonction
    /// pure, testée.</summary>
    public static float BurnAsh(float x, float y, int variant)
    {
        float r = Mathf.Sqrt(x * x + y * y);
        float seed = variant * 11.7f + 3.1f;
        float ring = Mathf.Exp(-Mathf.Pow((r - BurnEdge(x, y, seed)) / 0.16f, 2f));
        return ring * (0.3f + 0.7f * Noise2(x * 9f + seed, y * 9f - seed)) * (1f - Ramp(0.82f, 0.98f, r));
    }

    // Le bord irrégulier d'une brûlure, en distance au centre.
    private static float BurnEdge(float x, float y, float seed)
    {
        float cloud = Noise2(x * 2.6f + seed, y * 2.6f - seed) * 0.6f + Noise2(x * 6.3f - seed, y * 6.3f + seed) * 0.4f;
        return 0.5f + 0.34f * (cloud - 0.5f) * 2f;
    }

    // De 0 à 1 entre `from` et `to`, adouci (Mathf.SmoothStep, lui, interpole entre deux valeurs).
    private static float Ramp(float from, float to, float x)
    {
        float t = Mathf.Clamp01((x - from) / (to - from));
        return t * t * (3f - 2f * t);
    }

    // Un bruit de valeur lissé, de 0 à 1.
    private static float Noise(float t)
    {
        int i = Mathf.FloorToInt(t);
        float f = t - i;
        return Mathf.Lerp(Hash(i), Hash(i + 1), f * f * (3f - 2f * f));
    }

    private static float Noise2(float x, float y)
    {
        int i = Mathf.FloorToInt(x), j = Mathf.FloorToInt(y);
        float fx = x - i, fy = y - j;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        return Mathf.Lerp(Mathf.Lerp(Hash(i + j * 57), Hash(i + 1 + j * 57), fx),
                          Mathf.Lerp(Hash(i + (j + 1) * 57), Hash(i + 1 + (j + 1) * 57), fx), fy);
    }

    private static float Hash(int n)
    {
        unchecked
        {
            uint h = (uint)n * 0x9E3779B1u;
            h ^= h >> 15;
            h *= 0x85EBCA77u;
            h ^= h >> 13;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}
