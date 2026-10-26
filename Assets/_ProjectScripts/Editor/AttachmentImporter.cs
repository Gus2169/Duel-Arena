using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// L'importeur des accessoires des armes (2026-10-16, GDD § 6) : ce qu'exporter_accessoires.py dépose
/// dans Assets/_ProjectArena/Weapon/Accessoires/&lt;CODE&gt;/ (le FBX, l'occlusion cuite, la fiche)
/// devient le préfab &lt;CODE&gt;.prefab : WeaponAttachment (rubrique, repères, vitre), WeaponFinishes
/// (les matériaux de ses pièces dans les six finitions, comme une arme : il prend celle de son arme),
/// et la vitre d'un viseur au shader du réticule (DuelArena/ViseurReticule), avec la forme, la couleur
/// et la taille de son réticule.
///
/// Puis le catalogue (Accessoires.asset) : les préfabs, et les matériaux du faisceau et du point du
/// laser tactique (DuelArena/FaisceauLaser). Il est branché sur les WeaponData de la panoplie.
///
/// Réimporter refait tout à partir de l'export, sauf ce qui se règle au ressenti dans le préfab ou le
/// catalogue : la distance de l'œil d'un viseur (WeaponAttachment.aimEyeDistance), les valeurs des
/// effets et du faisceau. La taille et l'éclat des réticules se règlent ici, pour tous.
/// </summary>
public static class AttachmentImporter
{
    public const string Root = "Assets/_ProjectArena/Weapon/Accessoires";
    public const string CatalogPath = Root + "/Accessoires.asset";
    public const string LensShaderName = "DuelArena/ViseurReticule";
    public const string BeamShaderName = "DuelArena/FaisceauLaser";
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    /// <summary>Les viseurs, dans l'ordre de WeaponAttachments (1 à 10) : celui de Rainbow Six.</summary>
    public static readonly string[] SightOrder = { "PR-A", "PR-B", "PR-C", "HO-A", "HO-B", "HO-C", "HO-D", "RX-A", "RX-B", "RX-C" };

    /// <summary>Les formes du réticule, dans l'ordre de commun_accessoires.FORMES_RETICULE : la
    /// propriété _Forme du shader de la vitre.</summary>
    public static readonly string[] ReticleShapes =
        { "point", "cercle_point", "cercle_chevron", "losange_point", "tirets_point", "cercle_croix", "triangle", "tirets_chevron" };

    // La taille des réticules à l'écran, en milliradians par mètre du rayon dessiné dans Blender : le
    // cercle de HO-A (10,5 mm) fait 63 mrad, 34 px de rayon en 1080p sous les 90° du champ, comme les
    // holographiques de Rainbow Six ; le point de PR-A, 4,5 mrad. Ressenti.
    private const float ReticleMilliradiansPerMetre = 6000f;
    // L'éclat du réticule : un rouge franc sous le tonemapping neutre de l'arène, qui déborde un peu
    // en halo (bloom au-dessus de 1).
    private const float ReticleIntensity = 1.6f;
    private const float LensOpacity = 0.12f;
    // En visée, la distance de l'œil à la mire d'un viseur, au premier import (ressenti, dans le préfab).
    private const float SightEyeDistance = 0.08f;

    // Les couleurs de commun_accessoires (sRGB), celles de Blender.
    private static readonly Color RedReticle = new Color(255f / 255f, 34f / 255f, 24f / 255f);
    private static readonly Color GreenReticle = new Color(70f / 255f, 255f / 255f, 96f / 255f);
    private static readonly Dictionary<string, Color> GlassTints = new Dictionary<string, Color>
    {
        { "ambre", new Color(222f / 255f, 176f / 255f, 104f / 255f) },   // points rouges
        { "bleu", new Color(132f / 255f, 172f / 255f, 214f / 255f) },    // holographiques
        { "neutre", new Color(184f / 255f, 196f / 255f, 200f / 255f) },  // réflex
    };

    [MenuItem("Duel Arena/Armes/Importer les accessoires de Blender")]
    public static void ImportAllFromMenu()
    {
        ImportAll();
        AssetDatabase.SaveAssets();
    }

    /// <summary>Importe tous les accessoires exportés, refait le catalogue et le branche sur la panoplie.</summary>
    public static AttachmentCatalog ImportAll()
    {
        var imported = new List<(AttachmentSheet sheet, GameObject prefab)>();
        if (!AssetDatabase.IsValidFolder(Root)) return null;
        foreach (string dir in Directory.GetDirectories(Root))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith("_") || name == "Materiaux") continue;
            string sheetPath = $"{Root}/{name}/{name}_Finitions.json";
            if (!File.Exists(sheetPath)) continue;
            AttachmentSheet sheet = JsonUtility.FromJson<AttachmentSheet>(File.ReadAllText(sheetPath));
            GameObject prefab = ImportAttachment(sheet, $"{Root}/{name}");
            if (prefab != null) imported.Add((sheet, prefab));
        }
        AttachmentCatalog catalog = BuildCatalog(imported);
        AssignToLoadout(catalog);
        Debug.Log($"[Accessoires] {imported.Count} accessoires importés.");
        return catalog;
    }

    /// <summary>Importe un accessoire : réglages du FBX et de l'occlusion, matériaux, vitre, préfab.</summary>
    public static GameObject ImportAttachment(AttachmentSheet sheet, string dir)
    {
        string fbxPath = $"{dir}/{sheet.racine}_Modele.fbx";
        string aoPath = $"{dir}/{sheet.racine}_Occlusion.png";
        if (!File.Exists(fbxPath) || !File.Exists(aoPath))
        {
            Debug.LogError($"[Accessoires] {sheet.code} : il manque {fbxPath} ou {aoPath}.");
            return null;
        }
        WeaponImporter.ConfigureModel(fbxPath);
        WeaponImporter.ConfigureOcclusion(aoPath);
        var ao = AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath);
        Dictionary<(string, int), Material> materials = WeaponImporter.BuildMaterials(sheet, dir, ao);
        Material lens = sheet.vitres != null && sheet.vitres.Length > 0 ? BuildLensMaterial(sheet, dir) : null;
        GameObject prefab = BuildPrefab(sheet, dir, fbxPath, materials, lens);
        Debug.Log($"[Accessoires] {sheet.code} ({sheet.titre}) : {sheet.triangles} triangles, {sheet.pieces} pièces.");
        return prefab;
    }

    // ------------------------------------------------------------------
    // La vitre d'un viseur et son réticule
    // ------------------------------------------------------------------

    private static Material BuildLensMaterial(AttachmentSheet sheet, string dir)
    {
        Shader shader = Shader.Find(LensShaderName);
        if (shader == null) throw new InvalidOperationException($"Shader {LensShaderName} introuvable.");
        AttachmentData d = sheet.donnees ?? new AttachmentData();
        int shape = Array.IndexOf(ReticleShapes, d.reticule);
        if (shape < 0) throw new InvalidOperationException($"{sheet.code} : réticule inconnu « {d.reticule} ».");
        if (d.rayonReticule <= 0f) throw new InvalidOperationException($"{sheet.code} : la fiche n'a pas le rayon de son réticule.");

        string path = $"{dir}/Materiaux/{sheet.racine}_Vitre.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        Color tint = GlassTints.TryGetValue(d.teinte ?? "", out Color t) ? t : GlassTints["neutre"];
        tint.a = LensOpacity;
        material.SetColor("_CouleurVitre", tint);
        material.SetColor("_CouleurReticule", d.couleurReticule == "vert" ? GreenReticle : RedReticle);
        material.SetFloat("_Intensite", ReticleIntensity);
        material.SetFloat("_Forme", shape);
        material.SetFloat("_Rayon", ReticleRadius(d.rayonReticule));
        material.SetFloat("_Trait", ReticleRadius(d.traitReticule > 0f ? d.traitReticule : d.rayonReticule * 0.075f));
        material.SetFloat("_AxeCamera", 0f);   // la vue subjective le pose sur sa copie (FirstPersonView)
        material.enableInstancing = false;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Le rayon d'un réticule à l'écran (mrad), d'après celui dessiné dans Blender (m).</summary>
    public static float ReticleRadius(float blenderRadius) => blenderRadius * ReticleMilliradiansPerMetre;

    // ------------------------------------------------------------------
    // Préfab
    // ------------------------------------------------------------------

    private static GameObject BuildPrefab(AttachmentSheet sheet, string dir, string fbxPath,
                                          Dictionary<(string, int), Material> materials, Material lens)
    {
        string prefabPath = $"{dir}/{sheet.racine}.prefab";
        float eye = SightEyeDistance;
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null && existing.TryGetComponent(out WeaponAttachment previous)) eye = previous.AimEyeDistance;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = sheet.racine;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            var keyOfName = new Dictionary<string, string>();
            foreach (WeaponImporter.MaterialSheet entry in sheet.materiaux) keyOfName[entry.nom] = entry.cle;
            var glass = new HashSet<string>(sheet.vitres ?? Array.Empty<string>());

            Renderer lensRenderer = null;
            var pieces = new List<WeaponFinishes.Piece>();
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (glass.Contains(renderer.name))
                {
                    // La vitre n'a pas de finition : elle prend le shader du réticule, sans ombre.
                    var shared = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < shared.Length; i++) shared[i] = lens;
                    renderer.sharedMaterials = shared;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    lensRenderer = renderer;
                    continue;
                }
                pieces.Add(WeaponImporter.FinishPiece(sheet, renderer, keyOfName, materials));
            }
            if (glass.Count > 0 && lensRenderer == null)
                throw new InvalidOperationException($"{sheet.code} : la vitre {string.Join(", ", glass)} n'est pas dans le FBX.");

            Transform root = instance.transform;
            AttachmentCategory category = sheet.Category;
            Transform sight = root.Find("Mire");
            Transform beam = root.Find("Faisceau");
            Transform leftHand = root.Find("MainGauche");
            if (category == AttachmentCategory.Sight && (sight == null || lensRenderer == null))
                throw new InvalidOperationException($"{sheet.code} : un viseur sans mire ou sans vitre.");
            if (category == AttachmentCategory.Laser && beam == null)
                throw new InvalidOperationException($"{sheet.code} : un laser sans repère « Faisceau ».");
            if (category == AttachmentCategory.Grip && leftHand == null)
                throw new InvalidOperationException($"{sheet.code} : une poignée sans repère « MainGauche ».");

            var attachment = instance.AddComponent<WeaponAttachment>();
            attachment.EditorSetup(sheet.code, sheet.titre, category, sight, eye, lensRenderer, beam, leftHand,
                                   sheet.donnees != null ? sheet.donnees.arme : "", HiddenParts(sheet));
            var finishes = instance.AddComponent<WeaponFinishes>();
            finishes.EditorSetup(pieces.ToArray());
            return PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // Les pièces d'un modèle d'arme qu'un viseur remplace (exporter_armes : « à masquer sous un viseur »),
    // et la tour du guidon du FA-07, qu'on voyait dans la fenêtre du viseur, sous le réticule : comme
    // dans Rainbow Six, un viseur monté efface les organes de visée.
    private static readonly string[] IronSights = { "Hausse", "Guidon", "Guidon_Base", "Points_Hausse" };

    /// <summary>Ce que l'accessoire cache de l'arme : les organes de visée sous un viseur, le chargeur
    /// d'origine sous un chargeur grande capacité, sauf la rallonge d'un tube (elle s'y ajoute).</summary>
    public static string[] HiddenParts(AttachmentSheet sheet)
    {
        switch (sheet.Category)
        {
            case AttachmentCategory.Sight: return (string[])IronSights.Clone();
            case AttachmentCategory.Magazine:
                return sheet.donnees != null && sheet.donnees.genre == "tube" ? Array.Empty<string>() : new[] { "Chargeur_Standard" };
            default: return Array.Empty<string>();
        }
    }

    // ------------------------------------------------------------------
    // Le catalogue et la panoplie
    // ------------------------------------------------------------------

    private static AttachmentCatalog BuildCatalog(List<(AttachmentSheet sheet, GameObject prefab)> imported)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<AttachmentCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<AttachmentCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        GameObject Find(string code)
        {
            foreach ((AttachmentSheet sheet, GameObject prefab) in imported)
                if (sheet.code == code) return prefab;
            Debug.LogWarning($"[Accessoires] {code} n'est pas exporté : sa place du catalogue reste vide.");
            return null;
        }

        var sights = new GameObject[SightOrder.Length];
        for (int i = 0; i < sights.Length; i++) sights[i] = Find(SightOrder[i]);
        catalog.sights = sights;
        catalog.laser = Find("LT-1");
        catalog.pistolLaser = Find("LT-P");
        catalog.verticalGrip = Find("PV-1");
        catalog.angledGrip = Find("PI-1");

        var magazines = new List<(string code, GameObject prefab)>();
        foreach ((AttachmentSheet sheet, GameObject prefab) in imported)
            if (sheet.Category == AttachmentCategory.Magazine) magazines.Add((sheet.code, prefab));
        magazines.Sort((a, b) => string.CompareOrdinal(a.code, b.code));
        catalog.magazines = magazines.ConvertAll(m => m.prefab).ToArray();

        catalog.beamMaterial = BeamMaterial("Faisceau_Laser", false, 1.6f, 0.6f);
        catalog.dotMaterial = BeamMaterial("Point_Laser", true, 3f, 1f);
        EditorUtility.SetDirty(catalog);
        return catalog;
    }

    /// <summary>Le matériau du faisceau ou du point du laser (rouge, comme le faisceau de LT-1) : son
    /// éclat, et ce qu'il couvre du fond (le cœur du point, tout ; le faisceau, un peu).</summary>
    private static Material BeamMaterial(string name, bool dot, float intensity, float coverage)
    {
        Shader shader = Shader.Find(BeamShaderName);
        if (shader == null) throw new InvalidOperationException($"Shader {BeamShaderName} introuvable.");
        string dir = $"{Root}/Materiaux";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Root, "Materiaux");
        string path = $"{dir}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_Couleur", RedReticle);
        material.SetFloat("_Intensite", intensity);
        material.SetFloat("_Point", dot ? 1f : 0f);
        material.SetFloat("_Couverture", coverage);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Le catalogue sur chaque arme de la panoplie (ses effets, ses préfabs).</summary>
    private static void AssignToLoadout(AttachmentCatalog catalog)
    {
        var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        WeaponController weapon = player != null ? player.GetComponentInChildren<WeaponController>(true) : null;
        if (weapon == null || catalog == null) return;
        for (int slot = 0; slot < weapon.SlotCount; slot++)
        {
            WeaponData data = weapon.DataOf(slot);
            if (data == null || data.attachmentCatalog == catalog) continue;
            data.attachmentCatalog = catalog;
            EditorUtility.SetDirty(data);
        }
    }

    // ------------------------------------------------------------------
    // La fiche exportée par Blender (exporter_accessoires.py), lue par JsonUtility
    // ------------------------------------------------------------------

    [Serializable]
    public class AttachmentSheet : WeaponImporter.Sheet
    {
        public string categorie;
        public string[] vitres;
        public AttachmentData donnees;

        public AttachmentCategory Category => categorie switch
        {
            "Viseur" => AttachmentCategory.Sight,
            "Poignee" => AttachmentCategory.Grip,
            "Laser" => AttachmentCategory.Laser,
            "Chargeur" => AttachmentCategory.Magazine,
            _ => throw new InvalidOperationException($"{code} : rubrique inconnue « {categorie} »."),
        };
    }

    /// <summary>Ce que la fiche dit de l'accessoire (commun_accessoires.AtelierAccessoire.donnees).</summary>
    [Serializable]
    public class AttachmentData
    {
        public string teinte;            // viseur : la teinte de la vitre
        public string reticule;          // viseur : la forme du réticule (ReticleShapes)
        public string couleurReticule;   // « rouge » ou « vert »
        public float rayonReticule;      // m, tel que dessiné dans Blender
        public float traitReticule;      // m, l'épaisseur de ses traits (0 : 7,5 % du rayon, comme avant le 2026-10-26)
        public float hauteurAxe;         // m, l'axe optique au-dessus du rail
        public string couleurFaisceau;   // laser
        public string effet;             // poignée : « recul » ou « epaulement »
        public string arme;              // chargeur : la désignation du modèle d'arme
        public string genre;             // chargeur : « chargeur », « cellule », « tube »...
    }
}
