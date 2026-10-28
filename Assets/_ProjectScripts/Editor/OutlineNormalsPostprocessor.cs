using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Le contour du cel-shading (DuelArena/RobotCelShading) gonfle la coque le long des normales. Sur un robot à
/// facettes, les normales sont coupées à chaque arête vive : la coque s'y ouvrirait et le contour aurait des trous
/// aux coins. À l'import des modèles listés ici, chaque sommet reçoit dans ses tangentes une normale lissée (la
/// moyenne des normales de tous les sommets au même endroit) ; Unity déforme les tangentes avec le squelette comme
/// les normales, et le contour les lit quand son matériau le demande (_ContourLisse). Ces modèles n'ont pas de
/// carte de normales : leurs tangentes ne servent à rien d'autre (2026-10-26).
/// </summary>
public class OutlineNormalsPostprocessor : AssetPostprocessor
{
    public static readonly string[] Models = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(
        new[] { RenderTrialBuilder.Ct01Path, RenderTrialBuilder.Os02Path },
        System.Array.ConvertAll(RobotChassisBuilder.Robots, r => r.FbxPath)));

    /// <summary>Les robots de Blender, et les props du décor (2026-10-27 : le contour de leur essai de cel-shading).</summary>
    private bool Concerned => System.Array.IndexOf(Models, assetPath) >= 0 || assetPath.StartsWith(DecorImporter.Folder + "/");

    private void OnPreprocessModel()
    {
        if (!Concerned) return;
        ((ModelImporter)assetImporter).importTangents = ModelImporterTangents.None;
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (!Concerned) return;
        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (renderer.sharedMesh != null) StoreSmoothNormals(renderer.sharedMesh);
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh != null) StoreSmoothNormals(filter.sharedMesh);
    }

    /// <summary>Range dans les tangentes de chaque sommet la normale moyenne des sommets au même endroit.</summary>
    public static void StoreSmoothNormals(Mesh mesh)
    {
        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        float step = Mathf.Max(mesh.bounds.size.magnitude * 1e-5f, 1e-7f);
        var sums = new Dictionary<Vector3Int, Vector3>();
        var keys = new Vector3Int[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 p = positions[i] / step;
            keys[i] = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
            sums.TryGetValue(keys[i], out Vector3 sum);
            sums[keys[i]] = sum + normals[i];
        }
        var tangents = new Vector4[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 n = sums[keys[i]];
            n = n.sqrMagnitude > 1e-12f ? n.normalized : normals[i];
            tangents[i] = new Vector4(n.x, n.y, n.z, 1f);
        }
        mesh.tangents = tangents;
    }
}
