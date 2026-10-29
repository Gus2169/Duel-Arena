using UnityEngine;
using UnityEngine.UIElements;

namespace DuelArena.UI
{
    /// <summary>
    /// La lunette d'origine d'un fusil de précision, plein écran (2026-10-11, en UI Toolkit depuis le
    /// 2026-10-29) : du noir tout autour d'un disque ouvert au centre, son bord qui s'assombrit, et une croix
    /// noire fine qui se croise au point visé, là où partent les balles, ses branches épaissies vers le bord,
    /// sauf celle du haut. La vignette de l'armurerie la dessine pareil (MenuThumbnails.DrawBuiltInScope).
    ///
    /// L'élément ne fait que dessiner ce que lui donne CrosshairUI.
    /// </summary>
    [UxmlElement]
    public partial class ScopeOverlay : VisualElement
    {
        /// <summary>Le point visé, dans l'élément (unités du panneau).</summary>
        public Vector2 center;
        /// <summary>Le diamètre de l'ouverture, en part de la hauteur de l'élément.</summary>
        public float diameter = 0.92f;
        public Color reticleColor = new Color(0f, 0f, 0f, 0.9f);

        private const int Steps = 96;
        // Le bord du disque s'assombrit de 86 % du rayon jusqu'au bord (le masque d'avant, adouci en cloche).
        private static readonly float[] ShadeRings = { 0.86f, 0.9f, 0.93f, 0.96f, 0.98f, 1f };

        private readonly UiMesh mesh = new UiMesh();

        public ScopeOverlay()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("da-scope");
            generateVisualContent += Draw;
        }

        /// <summary>L'ombre du bord à `fraction` du rayon : rien jusqu'à 86 %, 85 % au bord, puis le noir.</summary>
        public static float EdgeShade(float fraction)
        {
            if (fraction >= 1f) return 1f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 1f, fraction)) * 0.85f;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            float w = layout.width, h = layout.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w < 1f || h < 1f) return;
            mesh.Clear();
            Vector2 c = center;
            float r = h * diameter * 0.5f;

            // Le noir, de l'ouverture jusqu'au-delà des coins de l'écran, puis l'ombre du bord, anneau par anneau.
            Ring(c, r, Mathf.Sqrt(w * w + h * h), Color.black, Color.black);
            for (int k = 0; k + 1 < ShadeRings.Length; k++)
            {
                Color inner = new Color(0f, 0f, 0f, EdgeShade(ShadeRings[k]));
                Color outer = new Color(0f, 0f, 0f, EdgeShade(ShadeRings[k + 1] - 1e-4f));
                Ring(c, r * ShadeRings[k], r * ShadeRings[k + 1], inner, outer);
            }

            // La croix : fine, interrompue au centre ; épaisse au-delà de 55 % du rayon, sauf en haut.
            Color32 ink = reticleColor;
            mesh.Rect(c.x - r, c.y - 1f, c.x - 6f, c.y + 1f, ink);
            mesh.Rect(c.x + 6f, c.y - 1f, c.x + r, c.y + 1f, ink);
            mesh.Rect(c.x - 1f, c.y + 6f, c.x + 1f, c.y + r, ink);
            mesh.Rect(c.x - 1f, c.y - r, c.x + 1f, c.y - 6f, ink);
            mesh.Rect(c.x - r, c.y - 3f, c.x - r * 0.55f, c.y + 3f, ink);
            mesh.Rect(c.x + r * 0.55f, c.y - 3f, c.x + r, c.y + 3f, ink);
            mesh.Rect(c.x - 3f, c.y + r * 0.55f, c.x + 3f, c.y + r, ink);
            mesh.Rect(c.x - 1.5f, c.y - 1.5f, c.x + 1.5f, c.y + 1.5f, ink);
            mesh.Commit(ctx);
        }

        /// <summary>Un anneau de `inner` à `outer` autour de `c`, chaque bord de sa couleur.</summary>
        private void Ring(Vector2 c, float inner, float outer, Color innerTint, Color outerTint)
        {
            int first = -1;
            for (int i = 0; i <= Steps; i++)
            {
                float a = i * (2f * Mathf.PI / Steps);
                var direction = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                int here = mesh.AddVertex(c + direction * inner, innerTint);
                mesh.AddVertex(c + direction * outer, outerTint);
                if (first < 0) first = here;
                else mesh.Quad(here - 2, here, here + 1, here - 1);
            }
        }
    }
}
