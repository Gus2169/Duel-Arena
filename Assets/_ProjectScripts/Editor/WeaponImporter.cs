using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// L'importeur des armes de Blender (2026-10-15) : ce qu'exporter_armes.py dépose dans
/// Assets/_ProjectArena/Weapon/Armes/&lt;CODE&gt;/ (le FBX, l'occlusion cuite, les paramètres des
/// finitions) devient le préfab &lt;CODE&gt;.prefab, prêt à tenir en main : WeaponModel (repères,
/// désignation, famille), WeaponFinishes (les matériaux de chaque pièce dans chacune des six
/// finitions), et les matériaux du shader DuelArena/ArmeUsee, un par matériau de Blender et par
/// finition, ceux d'une finition qui n'y change rien étant partagés.
///
/// Puis une arme de l'arsenal qui n'a pas encore de modèle prend celui dont la désignation est son nom :
/// depuis la refonte du 2026-10-26, chaque modèle est une arme (son WeaponData).
///
/// Réimporter refait tout à partir de l'export, sauf ce qui se règle au ressenti dans le préfab : la
/// distance de l'œil à la mire (WeaponModel.aimEyeDistance) est gardée.
/// </summary>
public static class WeaponImporter
{
    public const string Root = "Assets/_ProjectArena/Weapon/Armes";
    public const string ShaderName = "DuelArena/ArmeUsee";
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    // La distance de l'œil à la mire en visée au premier import, par classe (fusil d'assaut, précision,
    // pompe, pistolet) : une hausse à anneau se regarde de près, la lunette à son œilleton, un pistolet
    // à bout de bras. Ressenti : à régler ensuite dans le préfab.
    private static readonly float[] EyeDistanceByClass = { 0.10f, 0.08f, 0.12f, 0.40f };

    // Les décalages des bruits, dans l'ordre d'exporter_armes.CANAUX (trois nombres chacun) : les
    // propriétés _Dec… du shader.
    private static readonly string[] NoiseChannels =
    {
        "Variation", "Delavage", "Bords", "Eclats", "ZoneEclats", "ZoneRayures", "Rayures1", "Rayures2", "Retouches",
        "Rouille", "Cloques", "ZoneCloques", "Coulures", "ZoneCoulures", "Huile", "HuileDetail", "Poussiere", "Suie",
    };

    [MenuItem("Duel Arena/Armes/Importer les armes de Blender")]
    public static void ImportAll()
    {
        var imported = new List<(Sheet sheet, GameObject prefab)>();
        foreach (string dir in Directory.GetDirectories(Root))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith("_")) continue;
            string sheetPath = $"{Root}/{name}/{name}_Finitions.json";
            if (!File.Exists(sheetPath)) continue;
            Sheet sheet = JsonUtility.FromJson<Sheet>(File.ReadAllText(sheetPath));
            GameObject prefab = ImportWeapon(sheet, $"{Root}/{name}");
            if (prefab != null) imported.Add((sheet, prefab));
        }
        AssignToLoadout(imported);
        AttachmentImporter.ImportAll();   // les accessoires, montés sur ces armes (2026-10-16)
        GloveImporter.Import();           // et le gant à décharge (2026-10-16)
        AssetDatabase.SaveAssets();
        Debug.Log($"[Armes] {imported.Count} modèles importés.");
    }

    /// <summary>Importe une arme : réglages du FBX et de l'occlusion, matériaux, préfab.</summary>
    public static GameObject ImportWeapon(Sheet sheet, string dir)
    {
        string fbxPath = $"{dir}/{sheet.racine}_Modele.fbx";
        string aoPath = $"{dir}/{sheet.racine}_Occlusion.png";
        if (!File.Exists(fbxPath) || !File.Exists(aoPath))
        {
            Debug.LogError($"[Armes] {sheet.code} : il manque {fbxPath} ou {aoPath}.");
            return null;
        }
        ConfigureModel(fbxPath);
        ConfigureOcclusion(aoPath);
        var ao = AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath);
        Dictionary<(string, int), Material> materials = BuildMaterials(sheet, dir, ao);
        GameObject prefab = BuildPrefab(sheet, dir, fbxPath, materials);
        Debug.Log($"[Armes] {sheet.code} ({sheet.titre}, {sheet.nomFamille}) : {sheet.triangles} triangles, {sheet.pieces} pièces.");
        return prefab;
    }

    // ------------------------------------------------------------------
    // Réglages d'import
    // ------------------------------------------------------------------

    internal static void ConfigureModel(string path)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        // exporter_armes.vers_axes_unity a déjà mis l'arme dans les axes d'Unity : rien à convertir
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = false;
        importer.preserveHierarchy = false;
        importer.importBlendShapes = false;
        importer.importVisibility = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importConstraints = false;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.isReadable = false;
        importer.optimizeMeshPolygons = true;
        importer.optimizeMeshVertices = true;
        importer.weldVertices = true;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.None;   // pas de carte de normales
        importer.generateSecondaryUV = false;
        importer.addCollider = false;
        importer.keepQuads = false;
        // les matériaux importés ne servent qu'à leur nom : celui de Blender, qui dit quelle clé de la
        // fiche porte chaque sous-maillage
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        importer.SaveAndReimport();
    }

    internal static void ConfigureOcclusion(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;   // une donnée, pas une couleur
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
        standalone.overridden = true;
        standalone.format = TextureImporterFormat.BC4;   // un seul canal
        standalone.maxTextureSize = 2048;
        importer.SetPlatformTextureSettings(standalone);
        importer.SaveAndReimport();
    }

    // ------------------------------------------------------------------
    // Matériaux
    // ------------------------------------------------------------------

    /// <summary>Un matériau par clé et par finition ; une finition dont les paramètres sont ceux d'une
    /// précédente reprend son matériau.</summary>
    internal static Dictionary<(string, int), Material> BuildMaterials(Sheet sheet, string dir, Texture2D ao)
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null) throw new InvalidOperationException($"Shader {ShaderName} introuvable.");
        string matDir = $"{dir}/Materiaux";
        if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(dir, "Materiaux");

        var result = new Dictionary<(string, int), Material>();
        foreach (MaterialSheet entry in sheet.materiaux)
        {
            var seen = new List<(string json, Material material)>();
            for (int f = 0; f < WeaponLooks.FinishCount; f++)
            {
                Params p = entry.finitions[f];
                string signature = Signature(p);
                Material shared = null;
                foreach ((string json, Material material) in seen)
                    if (json == signature) shared = material;
                if (shared == null)
                {
                    string suffix = f == 0 ? "" : "_" + (WeaponFinish)f;
                    string path = $"{matDir}/{sheet.racine}_{entry.cle}{suffix}.mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        material = new Material(shader);
                        AssetDatabase.CreateAsset(material, path);
                    }
                    material.shader = shader;
                    ApplyParams(material, p, ao);
                    EditorUtility.SetDirty(material);
                    shared = material;
                    seen.Add((signature, material));
                }
                result[(entry.cle, f)] = shared;
            }
        }
        return result;
    }

    private static string Signature(Params p)
    {
        string nom = p.nom;
        p.nom = "";
        string json = JsonUtility.ToJson(p);
        p.nom = nom;
        return json;
    }

    /// <summary>Les paramètres de kit_lowpoly.materiau sur un matériau du shader ArmeUsee. Les couleurs
    /// de la fiche sont linéaires (celles que lit Blender) ; un matériau garde les siennes en sRGB et
    /// Unity les repasse en linéaire pour le shader.</summary>
    public static void ApplyParams(Material m, Params p, Texture ao)
    {
        m.SetColor("_Base", Srgb(p.couleur));
        m.SetFloat("_Rug", p.rug);
        m.SetFloat("_Metal", p.metal);
        m.SetFloat("_Variation", p.variation);
        m.SetFloat("_ADessous", p.aDessous ? 1f : 0f);
        m.SetColor("_Dessous", Srgb(p.dessous));
        m.SetFloat("_RugDessous", p.rugDessous);
        m.SetFloat("_MetalDessous", p.metalDessous);
        m.SetFloat("_Bords", p.bords);
        m.SetFloat("_Eclats", p.eclats);
        m.SetFloat("_EchelleEclats", p.echelleEclats);
        m.SetFloat("_Rayures", p.rayures);
        m.SetFloat("_Salete", p.salete);
        m.SetFloat("_ACouche", p.aCouche ? 1f : 0f);
        m.SetColor("_Couche", Srgb(p.couche));
        m.SetFloat("_RugCouche", p.rugCouche);
        m.SetFloat("_Retouches", p.retouches);
        m.SetColor("_TeinteRetouches", Srgb(p.teinteRetouches));
        m.SetFloat("_Rouille", p.rouille);
        m.SetColor("_TeinteRouille", Srgb(p.teinteRouille));
        m.SetFloat("_Huile", p.huile);
        m.SetFloat("_Poussiere", p.poussiere);
        m.SetFloat("_Delavage", p.delavage);
        Zone z0 = p.zones != null && p.zones.Length > 0 ? p.zones[0] : null;
        Zone z1 = p.zones != null && p.zones.Length > 1 ? p.zones[1] : null;
        m.SetVector("_Zone0", z0 != null ? new Vector4(z0.centre[0], z0.centre[1], z0.centre[2], z0.rayon) : Vector4.zero);
        m.SetVector("_Zone1", z1 != null ? new Vector4(z1.centre[0], z1.centre[1], z1.centre[2], z1.rayon) : Vector4.zero);
        m.SetVector("_ForcesZones", new Vector4(z0 != null ? z0.force : 0f, z1 != null ? z1.force : 0f, 0f, 0f));
        m.SetVector("_Suie", p.aSuie ? new Vector4(p.suie[0], p.suie[1], p.suie[2], p.suie[3]) : Vector4.zero);
        m.SetColor("_EmissionCouleur", Srgb(p.emission));
        m.SetFloat("_ForceEmission", p.forceEmission);
        m.SetFloat("_Coat", p.coat);
        m.SetTexture("_Occlusion", ao);
        if (p.decalages == null || p.decalages.Length != NoiseChannels.Length * 3)
            throw new InvalidOperationException($"{p.nom} : la fiche n'a pas les décalages de ses {NoiseChannels.Length} bruits.");
        for (int i = 0; i < NoiseChannels.Length; i++)
            m.SetVector("_Dec" + NoiseChannels[i], new Vector4(p.decalages[3 * i], p.decalages[3 * i + 1], p.decalages[3 * i + 2], 0f));
        // la lueur des écrans et des lentilles éclaire la scène, comme dans Blender
        m.globalIlluminationFlags = p.forceEmission > 0f ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                                                         : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        m.enableInstancing = false;
    }

    private static Color Srgb(float[] linear)
    {
        if (linear == null || linear.Length < 3) return Color.black;
        return new Color(linear[0], linear[1], linear[2], linear.Length > 3 ? linear[3] : 1f).gamma;
    }

    // ------------------------------------------------------------------
    // Préfab
    // ------------------------------------------------------------------

    private static GameObject BuildPrefab(Sheet sheet, string dir, string fbxPath, Dictionary<(string, int), Material> materials)
    {
        string prefabPath = $"{dir}/{sheet.racine}.prefab";
        float eye = EyeDistanceByClass[Mathf.Clamp(sheet.classe, 0, EyeDistanceByClass.Length - 1)];
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null && existing.TryGetComponent(out WeaponModel previous)) eye = previous.AimEyeDistance;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = sheet.racine;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            var keyOfName = new Dictionary<string, string>();
            foreach (MaterialSheet entry in sheet.materiaux) keyOfName[entry.nom] = entry.cle;

            var pieces = new List<WeaponFinishes.Piece>();
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                pieces.Add(FinishPiece(sheet, renderer, keyOfName, materials));

            Transform root = instance.transform;
            var weaponModel = instance.AddComponent<WeaponModel>();
            weaponModel.EditorSetup(sheet.code, (WeaponModel.Family)sheet.famille, root.Find("Mire"), root.Find("Bouche"), eye,
                                    sheet.pistolet, root.Find("MainGauche"), root.Find("MainDroite"));
            var finishes = instance.AddComponent<WeaponFinishes>();
            finishes.EditorSetup(pieces.ToArray());
            return PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    /// <summary>Pose sur une pièce importée les matériaux de la fiche (ceux d'origine) et rend ceux de
    /// ses six finitions, pour WeaponFinishes. Une arme et un accessoire (AttachmentImporter) passent
    /// par ici.</summary>
    internal static WeaponFinishes.Piece FinishPiece(Sheet sheet, MeshRenderer renderer, Dictionary<string, string> keyOfName,
                                                     Dictionary<(string, int), Material> materials)
    {
        Material[] imported = renderer.sharedMaterials;
        int n = imported.Length;
        var all = new Material[n * WeaponLooks.FinishCount];
        for (int f = 0; f < WeaponLooks.FinishCount; f++)
        {
            for (int i = 0; i < n; i++)
            {
                if (imported[i] == null || !keyOfName.TryGetValue(imported[i].name, out string key))
                    throw new InvalidOperationException($"{sheet.code} : la pièce {renderer.name} porte un matériau inconnu de la fiche ({(imported[i] != null ? imported[i].name : "aucun")}).");
                if (f == (int)WeaponFinish.Depareillee) key = Replaced(sheet, renderer.name, key);
                all[f * n + i] = materials[(key, f)];
            }
        }
        var origin = new Material[n];
        Array.Copy(all, origin, n);
        renderer.sharedMaterials = origin;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
        return new WeaponFinishes.Piece { renderer = renderer, materials = all };
    }

    /// <summary>La clé que porte un sous-maillage de `piece` dans la finition « dépareillée ».</summary>
    private static string Replaced(Sheet sheet, string piece, string key)
    {
        if (sheet.remplacements == null) return key;
        foreach (Replacement r in sheet.remplacements)
            if (r.piece == piece && r.de == key) return r.vers;
        return key;
    }

    /// <summary>Depuis la refonte du 2026-10-26, chaque modèle est une arme : une arme de l'arsenal sans
    /// modèle prend celui dont la désignation est son nom (« FA-30 »). Réimporter réécrit chaque préfab à
    /// sa place : les armes qui le portent déjà le gardent.</summary>
    private static void AssignToLoadout(List<(Sheet sheet, GameObject prefab)> imported)
    {
        var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        WeaponController weapon = player != null ? player.GetComponentInChildren<WeaponController>(true) : null;
        if (weapon == null) return;
        for (int slot = 0; slot < weapon.SlotCount; slot++)
        {
            WeaponData data = weapon.DataOf(slot);
            if (data == null || data.modelPrefab != null) continue;
            foreach ((Sheet _, GameObject prefab) in imported)
            {
                if (!prefab.TryGetComponent(out WeaponModel model) || model.Designation != data.weaponName) continue;
                data.modelPrefab = prefab;
                EditorUtility.SetDirty(data);
                Debug.Log($"[Armes] {data.weaponName} : son modèle {prefab.name}.");
            }
        }
    }

    // ------------------------------------------------------------------
    // La fiche exportée par Blender (exporter_armes.py), lue par JsonUtility
    // ------------------------------------------------------------------

    [Serializable]
    public class Sheet
    {
        public string code;
        public string racine;
        public string titre;
        public int classe;
        public int famille;
        public string nomFamille;
        public bool laser;
        public bool pistolet;
        public int triangles;
        public int pieces;
        public int resolution;
        public string[] finitions;
        public MaterialSheet[] materiaux;
        public Replacement[] remplacements;
    }

    [Serializable]
    public class MaterialSheet
    {
        public string cle;
        public string nom;
        public Params[] finitions;
    }

    [Serializable]
    public class Replacement
    {
        public string piece;
        public string de;
        public string vers;
    }

    [Serializable]
    public class Zone
    {
        public float[] centre;
        public float rayon;
        public float force;
    }

    [Serializable]
    public class Params
    {
        public string nom;
        public float[] couleur;
        public float rug;
        public float metal;
        public bool aDessous;
        public float[] dessous;
        public float rugDessous;
        public float metalDessous;
        public float bords;
        public float eclats;
        public float rayures;
        public float salete;
        public float variation;
        public float echelleEclats;
        public int graine;
        public float coat;
        public float[] emission;
        public float forceEmission;
        public bool aSuie;
        public float[] suie;
        public bool aCouche;
        public float[] couche;
        public float rugCouche;
        public float retouches;
        public float[] teinteRetouches;
        public float rouille;
        public float[] teinteRouille;
        public float huile;
        public float poussiere;
        public float delavage;
        public Zone[] zones;
        public float[] decalages;
    }
}
