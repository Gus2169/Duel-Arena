using UnityEngine;
using UnityEngine.UIElements;

namespace DuelArena.UI
{
    /// <summary>
    /// Ce que voit un robot qui prend une balle (2026-10-29, GDD § 6 : « des parasites et des interférences à
    /// l'écran », un robot ne saigne pas) : sa vue grésille un instant. Les bords de l'image s'assombrissent comme
    /// un signal qui faiblit, des bandes d'interférence claires la traversent, frangées de rouge et de cyan, et une
    /// neige fine pique les bords. Le tout est plus fort sur les côtés qu'au milieu, où l'on vise, et s'éteint en
    /// moins d'une demi-seconde : rien d'exagéré, le sursaut punit déjà le joueur touché.
    ///
    /// L'élément ne fait que dessiner ce que lui donne CrosshairUI (l'intensité, l'âge du coup, une graine). La
    /// forme change toutes les 35 ms, comme les éclairs du gant.
    /// </summary>
    [UxmlElement]
    public partial class DamageStaticOverlay : VisualElement
    {
        /// <summary>De 0 (rien) à 1 (un gros coup, à l'instant).</summary>
        public float intensity;
        /// <summary>Depuis le coup (s).</summary>
        public float age;
        public uint seed;

        /// <summary>Une forme toutes les 35 ms (HitIndicatorOverlay.ArcShapeInterval).</summary>
        public const float ShapeInterval = 0.035f;

        private static readonly Color Dark = new Color(0.02f, 0.03f, 0.05f);
        private static readonly Color Light = new Color(0.86f, 0.92f, 1f);
        private static readonly Color Cyan = new Color(0.35f, 0.95f, 1f);
        private static readonly Color Red = new Color(1f, 0.3f, 0.25f);
        // Le long d'une bande, de gauche à droite : sa part d'opacité (plus forte aux bords, presque rien au milieu).
        private static readonly float[] BandColumns = { 0f, 0.2f, 0.5f, 0.8f, 1f };
        private static readonly float[] BandLevels = { 1f, 0.55f, 0.12f, 0.55f, 1f };

        private readonly UiMesh mesh = new UiMesh();

        public DamageStaticOverlay()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("da-damage-static");
            generateVisualContent += Draw;
        }

        // ------------------------------------------------------------------
        // Ce qui se calcule (fonctions pures, testées)
        // ------------------------------------------------------------------

        /// <summary>La force des parasites pour un coup de `damage` PV : `minimum` pour le plus petit coup, pleine
        /// à `fullDamage` et au-delà. Un plomb de pompe ne grésille pas comme une balle de revolver.</summary>
        public static float Strength(float damage, float fullDamage, float minimum)
        {
            if (!(damage > 0f)) return 0f; // NaN compris
            return Mathf.Lerp(minimum, 1f, Mathf.Clamp01(damage / Mathf.Max(1f, fullDamage)));
        }

        /// <summary>Leur part qui reste `age` secondes après le coup : pleine, puis elle s'éteint vite, éteinte au
        /// bout de `duration`.</summary>
        public static float Intensity(float age, float duration)
        {
            if (!(duration > 0f) || !(age >= 0f) || age >= duration) return 0f;
            float rest = 1f - age / duration;
            return rest * rest;
        }

        // ------------------------------------------------------------------
        // Le dessin
        // ------------------------------------------------------------------

        private void Draw(MeshGenerationContext ctx)
        {
            float w = layout.width, h = layout.height;
            float strength = Mathf.Clamp01(intensity);
            if (strength <= 0f || float.IsNaN(w) || float.IsNaN(h) || w < 1f || h < 1f) return;
            mesh.Clear();
            uint s = ElectricArc.Hash(seed + (uint)Mathf.FloorToInt(Mathf.Max(0f, age) / ShapeInterval) * 0x632be5abU);

            // Le signal qui faiblit : les bords de l'image s'assombrissent.
            Frame(w, h, 0.16f * h, 0.3f * strength);

            // Les bandes d'interférence, sur toute la largeur, surtout fines ; une sur deux frangée de rouge et de cyan.
            int bands = Mathf.RoundToInt(3f + 7f * strength);
            for (int i = 0; i < bands; i++)
            {
                float y = ElectricArc.Next01(ref s) * h;
                float thick = 1.5f + 9f * ElectricArc.Next01(ref s) * ElectricArc.Next01(ref s);
                float alpha = (0.06f + 0.14f * ElectricArc.Next01(ref s)) * strength;
                Band(y, thick, w, Light, alpha);
                if (ElectricArc.Next01(ref s) < 0.5f)
                {
                    Band(y - 2f, 1f, w, Cyan, alpha * 0.9f);
                    Band(y + thick + 1f, 1f, w, Red, alpha * 0.9f);
                }
            }

            // La neige : de petits traits près des bords gauche et droit.
            int flakes = Mathf.RoundToInt(40f * strength);
            for (int i = 0; i < flakes; i++)
            {
                float side = ElectricArc.Next01(ref s);
                float depth = ElectricArc.Next01(ref s);
                float x = side < 0.5f ? depth * depth * 0.24f * w : w - depth * depth * 0.24f * w;
                float y = ElectricArc.Next01(ref s) * h;
                float length = 2f + 8f * ElectricArc.Next01(ref s);
                float height = ElectricArc.Next01(ref s) < 0.7f ? 1f : 2f;
                float grey = 0.6f + 0.4f * ElectricArc.Next01(ref s);
                float alpha = (0.15f + 0.3f * ElectricArc.Next01(ref s)) * strength;
                mesh.Rect(x, y, x + length, y + height, new Color(grey, grey, grey, alpha));
            }
            mesh.Commit(ctx);
        }

        /// <summary>Une bande horizontale de toute la largeur, plus marquée aux bords qu'au milieu.</summary>
        private void Band(float y, float thick, float w, Color tint, float alpha)
        {
            int first = -1;
            for (int i = 0; i < BandColumns.Length; i++)
            {
                Color32 c = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(alpha * BandLevels[i]));
                int top = mesh.AddVertex(new Vector2(BandColumns[i] * w, y), c);
                mesh.AddVertex(new Vector2(BandColumns[i] * w, y + thick), c);
                if (first < 0) first = top;
                else mesh.Quad(top - 2, top, top + 1, top - 1);
            }
        }

        /// <summary>Le cadre sombre : opaque de `alpha` sur le bord de l'élément, transparent `margin` plus loin.</summary>
        private void Frame(float w, float h, float margin, float alpha)
        {
            Color32 edge = new Color(Dark.r, Dark.g, Dark.b, Mathf.Clamp01(alpha));
            Color32 clear = new Color(Dark.r, Dark.g, Dark.b, 0f);
            int o0 = mesh.AddVertex(new Vector2(0f, 0f), edge);
            int o1 = mesh.AddVertex(new Vector2(w, 0f), edge);
            int o2 = mesh.AddVertex(new Vector2(w, h), edge);
            int o3 = mesh.AddVertex(new Vector2(0f, h), edge);
            int i0 = mesh.AddVertex(new Vector2(margin, margin), clear);
            int i1 = mesh.AddVertex(new Vector2(w - margin, margin), clear);
            int i2 = mesh.AddVertex(new Vector2(w - margin, h - margin), clear);
            int i3 = mesh.AddVertex(new Vector2(margin, h - margin), clear);
            mesh.Quad(o0, o1, i1, i0);
            mesh.Quad(o1, o2, i2, i1);
            mesh.Quad(o2, o3, i3, i2);
            mesh.Quad(o3, o0, i0, i3);
        }
    }
}
