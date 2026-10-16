using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// L'importeur du gant à décharge (2026-10-16) : le GD-1, construit dans Blender (Tools/Blender/gant_choc.py)
/// et exporté par exporter_accessoires.py dans Assets/_ProjectArena/Weapon/Gant/GD1/ (le FBX, l'occlusion,
/// la fiche des finitions), devient le modèle du préfab GantChoc, que le préfab joueur porte sur l'avant-bras
/// gauche (HeldWeapons.TaserGlove).
///
/// Le préfab garde ce qui ne vient pas de Blender : sa racine (posée au poignet par le préfab joueur), le
/// repère « Poing » et ce qu'il porte (les arcs, TaserSparkFX, et la lumière), que la vue subjective et
/// le spectateur lisent ; « Poing » prend seulement la position du repère du même nom exporté par Blender
/// (le bout des électrodes), et les arcs, les pointes des électrodes du modèle (ElectrodeTips) et le
/// haut de ses condensateurs (CapacitorTops). Le
/// modèle (« Modele ») est refait : ses pièces aux matériaux du shader DuelArena/ArmeUsee, avec leurs
/// six finitions (WeaponFinishes, sur la racine), comme une arme.
/// </summary>
public static class GloveImporter
{
    public const string Dir = "Assets/_ProjectArena/Weapon/Gant/GD1";
    public const string PrefabPath = "Assets/_ProjectArena/Weapon/GantChoc.prefab";
    public const string ModelName = "Modele";
    public const string FistName = "Poing";
    public const string ElectrodesName = "Electrodes";
    public const string CapacitorsName = "Condensateurs";

    /// <summary>
    /// Le haut des condensateurs (la pièce « Condensateurs », sur la manchette), dans le repère du
    /// poing : de chaque côté de l'axe, le sommet de leurs ailettes le plus en avant. Des arcs en
    /// courent jusqu'aux électrodes (TaserSparkFX). Vide si la pièce manque.
    /// </summary>
    public static Vector3[] CapacitorTops(Transform glove, Transform fist)
    {
        Transform model = glove.Find(ModelName);
        Transform piece = model != null ? model.Find(CapacitorsName) : null;
        MeshFilter filter = piece != null ? piece.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null) return new Vector3[0];

        Vector3[] vertices = filter.sharedMesh.vertices;
        float top = float.MinValue;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = fist.InverseTransformPoint(piece.TransformPoint(vertices[i]));
            top = Mathf.Max(top, vertices[i].y);
        }
        // Le sommet des ailettes (à 4 mm du haut) ; de chaque côté, le point le plus en avant.
        var result = new List<Vector3>();
        foreach (int side in new[] { 1, -1 })
        {
            bool found = false;
            Vector3 best = default;
            foreach (Vector3 v in vertices)
            {
                if (v.y < top - 0.004f || Mathf.Sign(v.x) != side) continue;
                if (!found || v.z > best.z) best = v;
                found = true;
            }
            if (found) result.Add(best);
        }
        return result.ToArray();
    }

    /// <summary>
    /// Les pointes des électrodes du modèle (la pièce « Electrodes » sous « Modele »), dans le repère du
    /// poing : le centre de la face avant de chacune, du côté du pouce à l'autre, dans l'ordre où elles
    /// se suivent. Les arcs en partent (TaserSparkFX). Vide si la pièce manque.
    /// </summary>
    public static Vector3[] ElectrodeTips(Transform glove, Transform fist)
    {
        Transform model = glove.Find(ModelName);
        Transform piece = model != null ? model.Find(ElectrodesName) : null;
        MeshFilter filter = piece != null ? piece.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null) return new Vector3[0];

        Vector3[] vertices = filter.sharedMesh.vertices;
        float front = float.MinValue;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = fist.InverseTransformPoint(piece.TransformPoint(vertices[i]));
            front = Mathf.Max(front, vertices[i].z);
        }
        // Les faces avant (à 1,5 mm du bout), rangées en travers : un écart de plus de 8 mm sépare deux
        // électrodes (29 mm d'axe à axe, 15 mm de diamètre).
        var tips = new List<Vector3>();
        foreach (Vector3 v in vertices) if (v.z > front - 0.0015f) tips.Add(v);
        tips.Sort((a, b) => b.x.CompareTo(a.x));
        var result = new List<Vector3>();
        int start = 0;
        for (int i = 1; i <= tips.Count; i++)
        {
            if (i < tips.Count && tips[i - 1].x - tips[i].x < 0.008f) continue;
            Vector3 sum = Vector3.zero;
            for (int j = start; j < i; j++) sum += tips[j];
            result.Add(sum / (i - start));
            start = i;
        }
        return result.ToArray();
    }

    private static void SetPoints(SerializedProperty list, Vector3[] points)
    {
        list.arraySize = points.Length;
        for (int i = 0; i < points.Length; i++) list.GetArrayElementAtIndex(i).vector3Value = points[i];
    }

    [MenuItem("Duel Arena/Armes/Importer le gant de Blender")]
    public static void ImportFromMenu()
    {
        Import();
        AssetDatabase.SaveAssets();
    }

    /// <summary>Refait le modèle du gant à partir de l'export de Blender. Renvoie le préfab, ou null.</summary>
    public static GameObject Import()
    {
        string name = Path.GetFileName(Dir);
        string sheetPath = $"{Dir}/{name}_Finitions.json";
        string fbxPath = $"{Dir}/{name}_Modele.fbx";
        string aoPath = $"{Dir}/{name}_Occlusion.png";
        if (!File.Exists(sheetPath) || !File.Exists(fbxPath) || !File.Exists(aoPath))
        {
            Debug.LogError($"[Gant] Il manque l'export du gant dans {Dir} (fiche, FBX ou occlusion).");
            return null;
        }
        var sheet = JsonUtility.FromJson<AttachmentImporter.AttachmentSheet>(File.ReadAllText(sheetPath));
        WeaponImporter.ConfigureModel(fbxPath);
        WeaponImporter.ConfigureOcclusion(aoPath);
        var ao = AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath);
        Dictionary<(string, int), Material> materials = WeaponImporter.BuildMaterials(sheet, Dir, ao);

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform fist = root.transform.Find(FistName);
            if (fist == null) throw new InvalidOperationException($"{PrefabPath} n'a plus de repère « {FistName} ».");
            Transform old = root.transform.Find(ModelName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = ModelName;
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            model.transform.SetSiblingIndex(0);

            // le repère du poing exporté par Blender : « Poing » du préfab y va, puis il disparaît
            Transform exported = model.transform.Find(FistName);
            if (exported == null) throw new InvalidOperationException($"{fbxPath} n'a pas de repère « {FistName} ».");
            fist.localPosition = root.transform.InverseTransformPoint(exported.position);
            fist.localRotation = Quaternion.identity;
            Object.DestroyImmediate(exported.gameObject);

            // Les arcs partent des pointes des électrodes de ce modèle, et courent de ses condensateurs.
            if (fist.TryGetComponent(out TaserSparkFX arcs))
            {
                var serialized = new SerializedObject(arcs);
                SetPoints(serialized.FindProperty("electrodes"), ElectrodeTips(root.transform, fist));
                SetPoints(serialized.FindProperty("capacitors"), CapacitorTops(root.transform, fist));
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var keyOfName = new Dictionary<string, string>();
            foreach (WeaponImporter.MaterialSheet entry in sheet.materiaux) keyOfName[entry.nom] = entry.cle;
            var pieces = new List<WeaponFinishes.Piece>();
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                pieces.Add(WeaponImporter.FinishPiece(sheet, renderer, keyOfName, materials));

            if (!root.TryGetComponent(out WeaponFinishes finishes)) finishes = root.AddComponent<WeaponFinishes>();
            finishes.EditorSetup(pieces.ToArray());

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Gant] {sheet.code} ({sheet.titre}) : {sheet.triangles} triangles, {sheet.pieces} pièces ; poing à {fist.localPosition.ToString("F3")}, {ElectrodeTips(root.transform, fist).Length} électrodes.");
            return saved;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
