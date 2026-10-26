using UnityEngine;
using UnityEngine.UIElements;

namespace DuelArena.UI
{
    /// <summary>
    /// Un dégradé linéaire (USS n'en sait pas faire) : l'ombre qui rend le menu lisible sur l'image de
    /// l'arène, le voile sous le titre d'une carte. Réglé en USS : `--gradient-from` et `--gradient-to`
    /// (couleurs), `--gradient-vertical` (0 : de gauche à droite, 1 : de haut en bas), `--gradient-start` et
    /// `--gradient-end` (entre 0 et 1, où commencent et finissent les deux couleurs).
    ///
    /// Tracé en maillage à couleurs de sommets, interpolées par la carte graphique : le dégradé vertical de
    /// Painter2D (`FillGradient`) se dessine plein dans cette version d'Unity (vu le 2026-10-17 : seul
    /// l'horizontal marche).
    /// </summary>
    [UxmlElement]
    public partial class GradientFill : VisualElement
    {
        private static readonly CustomStyleProperty<Color> FromProperty = new("--gradient-from");
        private static readonly CustomStyleProperty<Color> ToProperty = new("--gradient-to");
        private static readonly CustomStyleProperty<float> VerticalProperty = new("--gradient-vertical");
        private static readonly CustomStyleProperty<float> StartProperty = new("--gradient-start");
        private static readonly CustomStyleProperty<float> EndProperty = new("--gradient-end");

        private Color from = new Color(0f, 0f, 0f, 0.8f);
        private Color to = Color.clear;
        private bool vertical;
        private float start;
        private float end = 1f;

        public GradientFill()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("da-gradient");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(evt =>
            {
                ICustomStyle s = evt.customStyle;
                if (s.TryGetValue(FromProperty, out Color f)) from = f;
                if (s.TryGetValue(ToProperty, out Color t)) to = t;
                if (s.TryGetValue(VerticalProperty, out float v)) vertical = v > 0.5f;
                if (s.TryGetValue(StartProperty, out float a)) start = a;
                if (s.TryGetValue(EndProperty, out float b)) end = b;
                MarkDirtyRepaint();
            });
        }

        private void Draw(MeshGenerationContext ctx)
        {
            float w = layout.width, h = layout.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w < 1f || h < 1f) return;

            // Quatre lignes de sommets le long de l'axe : le bord, le début et la fin du fondu, l'autre bord.
            float a = Mathf.Clamp01(start), b = Mathf.Clamp01(Mathf.Max(end, start));
            float[] stops = { 0f, a, b, 1f };
            Color32[] colors = { from, from, to, to };
            MeshWriteData mesh = ctx.Allocate(8, 18);
            for (int i = 0; i < 4; i++)
            {
                float along = stops[i] * (vertical ? h : w);
                Vector3 p0 = vertical ? new Vector3(0f, along, Vertex.nearZ) : new Vector3(along, 0f, Vertex.nearZ);
                Vector3 p1 = vertical ? new Vector3(w, along, Vertex.nearZ) : new Vector3(along, h, Vertex.nearZ);
                mesh.SetNextVertex(new Vertex { position = p0, tint = colors[i] });
                mesh.SetNextVertex(new Vertex { position = p1, tint = colors[i] });
            }
            for (int i = 0; i < 3; i++)
            {
                ushort s0 = (ushort)(i * 2), s1 = (ushort)(i * 2 + 1), n0 = (ushort)(i * 2 + 2), n1 = (ushort)(i * 2 + 3);
                // Dans le sens des aiguilles d'une montre à l'écran (l'axe y descend), comme UI Toolkit l'attend.
                if (vertical)
                {
                    mesh.SetNextIndex(s0); mesh.SetNextIndex(s1); mesh.SetNextIndex(n1);
                    mesh.SetNextIndex(s0); mesh.SetNextIndex(n1); mesh.SetNextIndex(n0);
                }
                else
                {
                    mesh.SetNextIndex(s0); mesh.SetNextIndex(n0); mesh.SetNextIndex(n1);
                    mesh.SetNextIndex(s0); mesh.SetNextIndex(n1); mesh.SetNextIndex(s1);
                }
            }
        }
    }
}
