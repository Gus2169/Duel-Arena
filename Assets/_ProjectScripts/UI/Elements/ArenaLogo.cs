using UnityEngine;
using UnityEngine.UIElements;

namespace DuelArena.UI
{
    /// <summary>
    /// Le logo de Duel Arena (2026-10-17, d'après les maquettes) : la cible des mannequins de crash-test,
    /// un disque en quatre quartiers alternés et son anneau. Couleurs en USS : `--logo-color` (l'anneau et
    /// deux quartiers, le jaune du menu, ou la couleur du robot dans la carte du joueur) et `--logo-dark`
    /// (les deux autres quartiers).
    /// </summary>
    [UxmlElement]
    public partial class ArenaLogo : VisualElement
    {
        private static readonly CustomStyleProperty<Color> LogoColorProperty = new("--logo-color");
        private static readonly CustomStyleProperty<Color> LogoDarkProperty = new("--logo-dark");

        private Color light = new Color(0.957f, 0.745f, 0.11f);
        private Color dark = new Color(0.07f, 0.075f, 0.085f);

        public ArenaLogo()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("da-logo");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(evt =>
            {
                if (evt.customStyle.TryGetValue(LogoColorProperty, out Color c)) light = c;
                if (evt.customStyle.TryGetValue(LogoDarkProperty, out Color d)) dark = d;
                MarkDirtyRepaint();
            });
        }

        /// <summary>La couleur de l'anneau et des quartiers clairs (la carte du joueur y met celle de son robot).</summary>
        public Color LogoColor
        {
            get => light;
            set { light = value; MarkDirtyRepaint(); }
        }

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            float size = Mathf.Min(r.width, r.height);
            if (size < 2f) return;
            var center = new Vector2(r.width * 0.5f, r.height * 0.5f);
            float outer = size * 0.5f;
            float ring = size * 0.09f;
            float inner = outer - ring * 1.55f;
            Painter2D p = ctx.painter2D;

            p.fillColor = light;
            p.BeginPath();
            p.Arc(center, outer, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
            p.fillColor = dark;
            p.BeginPath();
            p.Arc(center, outer - ring, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();

            // Quatre quartiers : clairs en haut à gauche et en bas à droite (l'axe Y de l'écran descend).
            for (int q = 0; q < 4; q++)
            {
                p.fillColor = q % 2 == 0 ? light : dark;
                float start = 180f + q * 90f;
                p.BeginPath();
                p.MoveTo(center);
                p.Arc(center, inner, Angle.Degrees(start), Angle.Degrees(start + 90f));
                p.ClosePath();
                p.Fill();
            }
        }
    }
}
