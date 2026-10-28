using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Les maillages du décor construits par script (2026-10-27) : des faces à plat (le low poly propre), la couleur de
/// sommet rouge sur les chanfreins (l'attribut « bord » des armes : le shader DuelArena/Decor y écaille les arêtes),
/// des uv en mètres, la normale lissée rangée dans les tangentes (le contour du cel-shading, comme
/// OutlineNormalsPostprocessor) et le dépliage des lightmaps.
/// </summary>
public static class DecorMeshes
{
    /// <summary>Un maillage en construction : des faces à plat, chacune ses sommets, sur un ou plusieurs
    /// sous-maillages (un par matériau).</summary>
    public sealed class Builder
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Color> colors = new();
        private readonly List<Vector2> uvs = new();
        private readonly List<List<int>> submeshes = new() { new List<int>() };

        public int VertexCount => vertices.Count;

        private List<int> Submesh(int index)
        {
            while (submeshes.Count <= index) submeshes.Add(new List<int>());
            return submeshes[index];
        }

        /// <summary>Le quadrilatère (a, b, c, d), visible du côté de `visible` (comme Tribune.Face) ; `bord` : 1 sur un
        /// chanfrein. Les uv sont la projection de la face sur le plan de son axe dominant, en mètres.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 visible, float bord = 0f, int submesh = 0)
        {
            Vector3 n = visible.normalized;
            int start = vertices.Count;
            foreach (Vector3 p in new[] { a, b, c, d }) Add(p, n, bord);
            List<int> t = Submesh(submesh);
            // Unity montre le triangle (a, b, c) du côté de Cross(b - a, c - a) (Tribune.Face) : on le retourne au besoin.
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0f) t.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            else t.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 visible, float bord = 0f, int submesh = 0)
        {
            Vector3 n = visible.normalized;
            int start = vertices.Count;
            Add(a, n, bord);
            Add(b, n, bord);
            Add(c, n, bord);
            List<int> t = Submesh(submesh);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0f) t.AddRange(new[] { start, start + 1, start + 2 });
            else t.AddRange(new[] { start, start + 2, start + 1 });
        }

        private void Add(Vector3 p, Vector3 n, float bord)
        {
            vertices.Add(p);
            normals.Add(n);
            colors.Add(new Color(bord, 0f, 0f, 1f));
            Vector3 a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            uvs.Add(a.y >= Mathf.Max(a.x, a.z) ? new Vector2(p.x, p.z) : a.x >= a.z ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
        }

        /// <summary>Le maillage : normales à plat, normale lissée dans les tangentes, dépliage des lightmaps.</summary>
        public Mesh ToMesh(string name, bool lightmapUVs = true)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            int count = submeshes.Count;
            while (count > 1 && submeshes[count - 1].Count == 0) count--;
            mesh.subMeshCount = count;
            for (int i = 0; i < count; i++) mesh.SetTriangles(submeshes[i], i);
            mesh.RecalculateBounds();
            OutlineNormalsPostprocessor.StoreSmoothNormals(mesh);
            if (lightmapUVs && vertices.Count > 0) Unwrapping.GenerateSecondaryUVSet(mesh, new UnwrapParam { angleError = 0.08f, areaError = 0.15f, hardAngle = 88f, packMargin = 0.02f });
            return mesh;
        }
    }

    // ------------------------------------------------------------------
    // Les formes
    // ------------------------------------------------------------------

    /// <summary>
    /// Une boîte de `min` à `max` aux arêtes chanfreinées de `c` : six faces rentrées, douze chanfreins et huit coins
    /// (couleur de sommet rouge sur les chanfreins et les coins). Les faces tournées vers le haut vont dans le
    /// sous-maillage `topSubmesh` (un dessus de tôle striée sur une plateforme peinte).
    /// </summary>
    public static void ChamferedBox(Builder b, Vector3 min, Vector3 max, float c, int topSubmesh = 0)
    {
        Vector3 size = max - min;
        c = Mathf.Clamp(c, 0f, 0.45f * Mathf.Min(size.x, Mathf.Min(size.y, size.z)));
        // Les faces
        for (int axis = 0; axis < 3; axis++)
        {
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            for (int s = -1; s <= 1; s += 2)
            {
                float e = s > 0 ? max[axis] : min[axis];
                Vector3 P(float pu, float pv)
                {
                    var p = new Vector3();
                    p[axis] = e;
                    p[u] = pu;
                    p[v] = pv;
                    return p;
                }
                float u0 = min[u] + c, u1 = max[u] - c, v0 = min[v] + c, v1 = max[v] - c;
                Vector3 normal = Vector3.zero;
                normal[axis] = s;
                int sub = axis == 1 && s > 0 ? topSubmesh : 0;
                b.Quad(P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1), normal, 0f, sub);
            }
        }
        if (c <= 1e-5f) return;
        // Les chanfreins : entre les faces des axes a et b, le long du troisième.
        for (int a = 0; a < 3; a++)
        {
            for (int bb = a + 1; bb < 3; bb++)
            {
                int w = 3 - a - bb;
                for (int sa = -1; sa <= 1; sa += 2)
                {
                    for (int sb = -1; sb <= 1; sb += 2)
                    {
                        float ea = sa > 0 ? max[a] : min[a];
                        float eb = sb > 0 ? max[bb] : min[bb];
                        Vector3 P(bool onA, float pw)
                        {
                            var p = new Vector3();
                            p[a] = onA ? ea : ea - sa * c;
                            p[bb] = onA ? eb - sb * c : eb;
                            p[w] = pw;
                            return p;
                        }
                        float w0 = min[w] + c, w1 = max[w] - c;
                        Vector3 normal = Vector3.zero;
                        normal[a] = sa;
                        normal[bb] = sb;
                        b.Quad(P(true, w0), P(true, w1), P(false, w1), P(false, w0), normal, 1f);
                    }
                }
            }
        }
        // Les coins
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float ex = sx > 0 ? max.x : min.x, ey = sy > 0 ? max.y : min.y, ez = sz > 0 ? max.z : min.z;
                    b.Triangle(new Vector3(ex, ey - sy * c, ez - sz * c), new Vector3(ex - sx * c, ey, ez - sz * c),
                               new Vector3(ex - sx * c, ey - sy * c, ez), new Vector3(sx, sy, sz), 1f);
                }
            }
        }
    }

    /// <summary>Une boîte orientée (centre, axes, demi-tailles) sans chanfrein : une poutre, une suspente.</summary>
    public static void OrientedBox(Builder b, Vector3 center, Vector3 right, Vector3 up, Vector3 forward, Vector3 half, int submesh = 0)
    {
        right.Normalize();
        up.Normalize();
        forward.Normalize();
        Vector3 C(float x, float y, float z) => center + right * (x * half.x) + up * (y * half.y) + forward * (z * half.z);
        b.Quad(C(1, -1, -1), C(1, 1, -1), C(1, 1, 1), C(1, -1, 1), right, 0f, submesh);
        b.Quad(C(-1, -1, -1), C(-1, -1, 1), C(-1, 1, 1), C(-1, 1, -1), -right, 0f, submesh);
        b.Quad(C(-1, 1, -1), C(-1, 1, 1), C(1, 1, 1), C(1, 1, -1), up, 0f, submesh);
        b.Quad(C(-1, -1, -1), C(1, -1, -1), C(1, -1, 1), C(-1, -1, 1), -up, 0f, submesh);
        b.Quad(C(-1, -1, 1), C(1, -1, 1), C(1, 1, 1), C(-1, 1, 1), forward, 0f, submesh);
        b.Quad(C(-1, -1, -1), C(-1, 1, -1), C(1, 1, -1), C(1, -1, -1), -forward, 0f, submesh);
    }

    /// <summary>Une poutre en I de `from` à `to` : deux semelles et une âme, `height` de haut, `width` de large,
    /// l'âme dans le plan vertical.</summary>
    public static void IBeam(Builder b, Vector3 from, Vector3 to, float height, float width, int submesh = 0)
    {
        Vector3 along = to - from;
        float length = along.magnitude;
        if (length < 1e-4f) return;
        Vector3 forward = along / length;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
        right.Normalize();
        Vector3 up = Vector3.Cross(forward, right).normalized;
        Vector3 mid = (from + to) * 0.5f;
        float flange = Mathf.Max(0.02f, height * 0.08f), web = Mathf.Max(0.015f, width * 0.12f);
        OrientedBox(b, mid + up * (height * 0.5f - flange * 0.5f), right, up, forward, new Vector3(width * 0.5f, flange * 0.5f, length * 0.5f), submesh);
        OrientedBox(b, mid - up * (height * 0.5f - flange * 0.5f), right, up, forward, new Vector3(width * 0.5f, flange * 0.5f, length * 0.5f), submesh);
        OrientedBox(b, mid, right, up, forward, new Vector3(web * 0.5f, height * 0.5f - flange, length * 0.5f), submesh);
    }

    /// <summary>Un prisme vertical sur le polygone `polygon` (x, z, dans le sens trigonométrique ou non) entre `bottom`
    /// et `top` : un dessus, un dessous et ses flancs (la dalle du sol, le plafond).</summary>
    public static void Prism(Builder b, IReadOnlyList<Vector2> polygon, float bottom, float top, bool topFace = true, bool bottomFace = true, bool sides = true, int submesh = 0)
    {
        int n = polygon.Count;
        Vector2 center = Vector2.zero;
        foreach (Vector2 p in polygon) center += p;
        center /= n;
        for (int i = 0; i < n; i++)
        {
            Vector2 p0 = polygon[i], p1 = polygon[(i + 1) % n];
            if (topFace) b.Triangle(new Vector3(center.x, top, center.y), new Vector3(p0.x, top, p0.y), new Vector3(p1.x, top, p1.y), Vector3.up, 0f, submesh);
            if (bottomFace) b.Triangle(new Vector3(center.x, bottom, center.y), new Vector3(p0.x, bottom, p0.y), new Vector3(p1.x, bottom, p1.y), Vector3.down, 0f, submesh);
            if (sides)
            {
                Vector2 mid = (p0 + p1) * 0.5f;
                Vector2 edge = (p1 - p0).normalized;
                var outward = new Vector3(edge.y, 0f, -edge.x);
                if (Vector2.Dot(new Vector2(outward.x, outward.z), mid - center) < 0f) outward = -outward;
                b.Quad(new Vector3(p0.x, bottom, p0.y), new Vector3(p1.x, bottom, p1.y), new Vector3(p1.x, top, p1.y), new Vector3(p0.x, top, p0.y), outward, 0f, submesh);
            }
        }
    }

    /// <summary>Le polygone `polygon` décalé de `distance` vers l'extérieur, angles en onglet (comme la tribune).</summary>
    public static Vector2[] Offset(IReadOnlyList<Vector2> polygon, float distance)
    {
        int n = polygon.Count;
        Vector2 center = Vector2.zero;
        foreach (Vector2 p in polygon) center += p;
        center /= n;
        Vector2 Outward(int i)
        {
            Vector2 a = polygon[i], c = polygon[(i + 1) % n];
            Vector2 e = (c - a).normalized;
            var normal = new Vector2(e.y, -e.x);
            if (Vector2.Dot(normal, (a + c) * 0.5f - center) < 0f) normal = -normal;
            return normal;
        }
        var result = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 n1 = Outward((i + n - 1) % n), n2 = Outward(i);
            Vector2 bisector = (n1 + n2).normalized;
            float cos = Mathf.Max(0.2f, Vector2.Dot(bisector, n2));
            result[i] = polygon[i] + bisector * (distance / cos);
        }
        return result;
    }

    /// <summary>La normale sortante du côté `i` du polygone (à l'opposé de son centre).</summary>
    public static Vector2 OutwardOf(IReadOnlyList<Vector2> polygon, int i)
    {
        int n = polygon.Count;
        Vector2 center = Vector2.zero;
        foreach (Vector2 p in polygon) center += p;
        center /= n;
        Vector2 a = polygon[i], c = polygon[(i + 1) % n];
        Vector2 e = (c - a).normalized;
        var normal = new Vector2(e.y, -e.x);
        if (Vector2.Dot(normal, (a + c) * 0.5f - center) < 0f) normal = -normal;
        return normal;
    }

    // ------------------------------------------------------------------
    // La banque : tous les maillages d'une scène dans un seul fichier
    // ------------------------------------------------------------------

    /// <summary>
    /// Les maillages générés d'une scène, rangés dans un seul fichier à côté d'elle (`<scène>_Decor.asset`, en LFS) :
    /// le premier en est l'objet principal, les autres ses sous-objets. Une reconstruction réécrit le principal en
    /// place (son GUID ne change pas) et remplace les autres.
    /// </summary>
    public sealed class Bank
    {
        private readonly string path;
        private readonly List<Mesh> meshes = new();

        public Bank(string path) => this.path = path;

        public Mesh Add(Mesh mesh)
        {
            meshes.Add(mesh);
            return mesh;
        }

        public int Count => meshes.Count;

        public void Save()
        {
            if (meshes.Count == 0) return;
            var main = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (main == null)
            {
                AssetDatabase.CreateAsset(meshes[0], path);
                main = meshes[0];
            }
            else
            {
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (o == null || o == main || AssetDatabase.IsMainAsset(o)) continue;
                    AssetDatabase.RemoveObjectFromAsset(o);
                    Object.DestroyImmediate(o, true);
                }
                // Le premier maillage prend la place du principal : les objets qui le montrent le gardent.
                Mesh first = meshes[0];
                main.Clear();
                EditorUtility.CopySerialized(first, main);
                main.name = first.name;
                foreach (MeshFilter filter in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
                {
                    if (filter.sharedMesh == first) filter.sharedMesh = main;
                }
                foreach (MeshCollider collider in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include))
                {
                    if (collider.sharedMesh == first) collider.sharedMesh = main;
                }
                Object.DestroyImmediate(first);
                meshes[0] = main;
            }
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], main);
            EditorUtility.SetDirty(main);
            AssetDatabase.SaveAssets();
        }
    }
}
