using UnityEngine;
using UnityEngine.UIElements;

namespace DuelArena.UI
{
    /// <summary>
    /// Le fond anguleux du menu (2026-10-17, d'après les maquettes de l'utilisateur) : un polygone aux coins
    /// coupés, un liseré, une barre d'accent à gauche et des équerres aux coins, tracés par Painter2D (USS ne
    /// sait couper ni coins ni bords en biais).
    ///
    /// Tout se règle en USS, par des propriétés personnalisées que les pseudo-classes d'un parent peuvent
    /// changer (`.da-btn:hover > .da-slant { --fill-color: ... }`) :
    /// `--fill-color`, `--edge-color`, `--edge-width`, `--cut-tl`, `--cut-tr`, `--cut-br`, `--cut-bl` (px),
    /// `--accent-color` et `--accent-width` (la barre de gauche), `--bracket-color`, `--bracket-size` et
    /// `--bracket-width` (les équerres), `--bracket-corners` (lesquelles : 1 en haut à gauche, 2 en haut à
    /// droite, 4 en bas à droite, 8 en bas à gauche ; 5 par défaut).
    ///
    /// Deux usages : un panneau qui porte ses enfants (il se dessine derrière eux), ou le fond d'un bouton,
    /// posé en premier enfant avec la classe `slant-bg` (position absolue) : Painter2D ne s'utilise pas sur
    /// un Button ou un Label, dont le texte se dessinerait dessous.
    /// </summary>
    [UxmlElement]
    public partial class SlantBox : VisualElement
    {
        private static readonly CustomStyleProperty<Color> FillProperty = new("--fill-color");
        private static readonly CustomStyleProperty<Color> EdgeProperty = new("--edge-color");
        private static readonly CustomStyleProperty<float> EdgeWidthProperty = new("--edge-width");
        private static readonly CustomStyleProperty<float> CutTopLeftProperty = new("--cut-tl");
        private static readonly CustomStyleProperty<float> CutTopRightProperty = new("--cut-tr");
        private static readonly CustomStyleProperty<float> CutBottomRightProperty = new("--cut-br");
        private static readonly CustomStyleProperty<float> CutBottomLeftProperty = new("--cut-bl");
        private static readonly CustomStyleProperty<Color> AccentProperty = new("--accent-color");
        private static readonly CustomStyleProperty<float> AccentWidthProperty = new("--accent-width");
        private static readonly CustomStyleProperty<Color> BracketProperty = new("--bracket-color");
        private static readonly CustomStyleProperty<float> BracketSizeProperty = new("--bracket-size");
        private static readonly CustomStyleProperty<float> BracketWidthProperty = new("--bracket-width");
        private static readonly CustomStyleProperty<int> BracketCornersProperty = new("--bracket-corners");

        private Color fill = Color.clear;
        private Color edge = Color.clear;
        private float edgeWidth;
        private float cutTopLeft, cutTopRight, cutBottomRight, cutBottomLeft;
        private Color accent = Color.clear;
        private float accentWidth;
        private Color bracket = Color.clear;
        private float bracketSize;
        private float bracketWidth = 2f;
        private int bracketCorners = 5;

        public SlantBox()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("da-slant");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyle);
        }

        private void OnCustomStyle(CustomStyleResolvedEvent evt)
        {
            ICustomStyle s = evt.customStyle;
            fill = s.TryGetValue(FillProperty, out Color f) ? f : Color.clear;
            edge = s.TryGetValue(EdgeProperty, out Color e) ? e : Color.clear;
            edgeWidth = s.TryGetValue(EdgeWidthProperty, out float ew) ? ew : 0f;
            cutTopLeft = s.TryGetValue(CutTopLeftProperty, out float tl) ? tl : 0f;
            cutTopRight = s.TryGetValue(CutTopRightProperty, out float tr) ? tr : 0f;
            cutBottomRight = s.TryGetValue(CutBottomRightProperty, out float br) ? br : 0f;
            cutBottomLeft = s.TryGetValue(CutBottomLeftProperty, out float bl) ? bl : 0f;
            accent = s.TryGetValue(AccentProperty, out Color a) ? a : Color.clear;
            accentWidth = s.TryGetValue(AccentWidthProperty, out float aw) ? aw : 0f;
            bracket = s.TryGetValue(BracketProperty, out Color b) ? b : Color.clear;
            bracketSize = s.TryGetValue(BracketSizeProperty, out float bs) ? bs : 0f;
            bracketWidth = s.TryGetValue(BracketWidthProperty, out float bw) ? bw : 2f;
            bracketCorners = s.TryGetValue(BracketCornersProperty, out int bc) ? bc : 5;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            // Toute la boîte, marges intérieures comprises : un panneau a du padding, et le tracé suivait
            // contentRect, qui s'en retire (le cadre d'un panneau était dessiné plus petit que lui).
            float w = layout.width, h = layout.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w < 1f || h < 1f) return;
            Painter2D p = ctx.painter2D;

            if (fill.a > 0f)
            {
                p.fillColor = fill;
                p.BeginPath();
                Trace(p, 0f, 0f, w, h, 0f);
                p.Fill();
            }

            if (accent.a > 0f && accentWidth > 0f)
            {
                // La barre suit la coupe des coins de gauche, pour ne jamais dépasser du polygone.
                float top = Mathf.Max(cutTopLeft, 0f), bottom = h - Mathf.Max(cutBottomLeft, 0f);
                p.fillColor = accent;
                p.BeginPath();
                p.MoveTo(new Vector2(0f, top));
                p.LineTo(new Vector2(accentWidth, top + accentWidth * (cutTopLeft > 0f ? 1f : 0f)));
                p.LineTo(new Vector2(accentWidth, bottom - accentWidth * (cutBottomLeft > 0f ? 1f : 0f)));
                p.LineTo(new Vector2(0f, bottom));
                p.ClosePath();
                p.Fill();
            }

            if (edge.a > 0f && edgeWidth > 0f)
            {
                float half = edgeWidth * 0.5f;
                p.strokeColor = edge;
                p.lineWidth = edgeWidth;
                p.lineJoin = LineJoin.Miter;
                p.BeginPath();
                Trace(p, half, half, w - half, h - half, half);
                p.Stroke();
            }

            if (bracket.a > 0f && bracketSize > 0f && bracketWidth > 0f)
            {
                float size = Mathf.Min(bracketSize, Mathf.Min(w, h) * 0.5f);
                float half = bracketWidth * 0.5f;
                p.strokeColor = bracket;
                p.lineWidth = bracketWidth;
                p.lineJoin = LineJoin.Miter;
                p.lineCap = LineCap.Butt;
                if ((bracketCorners & 1) != 0) Bracket(p, new Vector2(half, half), 1f, 1f, size);
                if ((bracketCorners & 2) != 0) Bracket(p, new Vector2(w - half, half), -1f, 1f, size);
                if ((bracketCorners & 4) != 0) Bracket(p, new Vector2(w - half, h - half), -1f, -1f, size);
                if ((bracketCorners & 8) != 0) Bracket(p, new Vector2(half, h - half), 1f, -1f, size);
            }
        }

        // Une équerre dont le coin est `corner`, ses branches partant vers (dx, 0) et (0, dy).
        private static void Bracket(Painter2D p, Vector2 corner, float dx, float dy, float size)
        {
            p.BeginPath();
            p.MoveTo(corner + new Vector2(0f, dy * size));
            p.LineTo(corner);
            p.LineTo(corner + new Vector2(dx * size, 0f));
            p.Stroke();
        }

        // Le contour, coins coupés, rentré de `inset` (le liseré se trace au milieu de son épaisseur).
        private void Trace(Painter2D p, float x0, float y0, float x1, float y1, float inset)
        {
            float tl = Cut(cutTopLeft, inset), tr = Cut(cutTopRight, inset);
            float br = Cut(cutBottomRight, inset), bl = Cut(cutBottomLeft, inset);
            p.MoveTo(new Vector2(x0 + tl, y0));
            p.LineTo(new Vector2(x1 - tr, y0));
            if (tr > 0f) p.LineTo(new Vector2(x1, y0 + tr));
            p.LineTo(new Vector2(x1, y1 - br));
            if (br > 0f) p.LineTo(new Vector2(x1 - br, y1));
            p.LineTo(new Vector2(x0 + bl, y1));
            if (bl > 0f) p.LineTo(new Vector2(x0, y1 - bl));
            p.LineTo(new Vector2(x0, y0 + tl));
            p.ClosePath();
        }

        private static float Cut(float cut, float inset) => cut > 0f ? Mathf.Max(0f, cut - inset * 0.41f) : 0f;
    }
}
