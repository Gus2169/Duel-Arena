using UnityEngine;
using UnityEngine.UIElements;

namespace DuelArena.UI
{
    /// <summary>
    /// Un maillage à couleurs de sommets pour les éléments du HUD qui se dessinent eux-mêmes (le réticule, les
    /// parasites, la lunette) : on y pose des triangles et des quadrilatères, puis on l'écrit d'un coup. Chaque
    /// triangle est remis dans le sens qu'attend UI Toolkit, celui des aiguilles d'une montre à l'écran (l'axe y
    /// descend), comme ceux de HitIndicatorOverlay. Ses tableaux grandissent une fois, puis servent à chaque
    /// image : rien n'est alloué en jouant.
    /// </summary>
    internal sealed class UiMesh
    {
        private Vector2[] positions = new Vector2[128];
        private Color32[] tints = new Color32[128];
        private ushort[] indices = new ushort[192];
        private int vertexCount;
        private int indexCount;

        public void Clear()
        {
            vertexCount = 0;
            indexCount = 0;
        }

        public int AddVertex(Vector2 position, Color32 tint)
        {
            if (vertexCount == positions.Length)
            {
                System.Array.Resize(ref positions, positions.Length * 2);
                System.Array.Resize(ref tints, tints.Length * 2);
            }
            positions[vertexCount] = position;
            tints[vertexCount] = tint;
            return vertexCount++;
        }

        /// <summary>Un triangle, posé dans le sens des aiguilles d'une montre à l'écran quel que soit l'ordre donné.</summary>
        public void Triangle(int a, int b, int c)
        {
            if (indexCount + 3 > indices.Length) System.Array.Resize(ref indices, indices.Length * 2);
            Vector2 pa = positions[a], pb = positions[b], pc = positions[c];
            float cross = (pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x);
            indices[indexCount++] = (ushort)a;
            indices[indexCount++] = (ushort)(cross >= 0f ? b : c);
            indices[indexCount++] = (ushort)(cross >= 0f ? c : b);
        }

        /// <summary>Le quadrilatère a, b, c, d (dans l'ordre de son tour), en deux triangles.</summary>
        public void Quad(int a, int b, int c, int d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }

        /// <summary>Un quadrilatère d'une seule couleur, ses coins dans l'ordre de son tour.</summary>
        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
        {
            int i = AddVertex(a, tint);
            AddVertex(b, tint);
            AddVertex(c, tint);
            AddVertex(d, tint);
            Quad(i, i + 1, i + 2, i + 3);
        }

        /// <summary>Un rectangle droit, de (x0, y0) à (x1, y1).</summary>
        public void Rect(float x0, float y0, float x1, float y1, Color32 tint)
            => Quad(new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1), tint);

        /// <summary>Un trait de `from` à `to`, épais de `width`.</summary>
        public void Segment(Vector2 from, Vector2 to, float width, Color32 tint)
        {
            Vector2 along = to - from;
            if (along.sqrMagnitude < 1e-8f) return;
            Vector2 side = new Vector2(-along.y, along.x).normalized * (width * 0.5f);
            Quad(from + side, to + side, to - side, from - side, tint);
        }

        public void Commit(MeshGenerationContext ctx)
        {
            if (vertexCount == 0 || indexCount == 0) return;
            MeshWriteData mesh = ctx.Allocate(vertexCount, indexCount);
            for (int i = 0; i < vertexCount; i++)
                mesh.SetNextVertex(new Vertex { position = new Vector3(positions[i].x, positions[i].y, Vertex.nearZ), tint = tints[i] });
            for (int i = 0; i < indexCount; i++) mesh.SetNextIndex(indices[i]);
        }
    }
}
