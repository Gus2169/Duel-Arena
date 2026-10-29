using UnityEngine;
using UnityEngine.UIElements;
using HitKind = WeaponController.HitKind;

namespace DuelArena.UI
{
    /// <summary>
    /// Le marqueur de touche (2026-10-29, GDD § 6 : « hitmarker, un son de touche, et un son différent pour la
    /// tête ») : une croix en X autour du point visé, à chaque tir que le serveur a compté. Blanche pour une
    /// touche, plus grande et plus épaisse à la tête, rouge (la couleur de l'énergie) et plus longue à l'écran
    /// pour le coup qui tue, comme dans Call of Duty. Elle apparaît un peu plus grande et se pose en 50 ms.
    /// Fonctions pures, testées ; les valeurs sont à juger en jouant.
    /// </summary>
    public static class HitMarker
    {
        /// <summary>Du point visé au début de chaque branche (unités du panneau, 1080 de haut).</summary>
        public const float Inner = 7f;

        /// <summary>Le temps d'apparition, pendant lequel la croix se pose (s).</summary>
        public const float PopTime = 0.05f;

        /// <summary>Le temps qu'elle reste à l'écran (s) : un coup qui tue s'attarde.</summary>
        public static float Duration(HitKind kind) => kind switch
        {
            HitKind.Kill => 0.45f,
            HitKind.Head => 0.3f,
            _ => 0.22f,
        };

        /// <summary>Le temps qu'elle reste pleine avant de s'effacer (s).</summary>
        public static float Hold(HitKind kind) => kind == HitKind.Kill ? 0.18f : 0.06f;

        /// <summary>Le bout de ses branches, du point visé (unités du panneau).</summary>
        public static float Reach(HitKind kind) => kind switch
        {
            HitKind.Kill => 19f,
            HitKind.Head => 18f,
            _ => 15f,
        };

        /// <summary>L'épaisseur de ses branches (unités du panneau).</summary>
        public static float Thickness(HitKind kind) => kind switch
        {
            HitKind.Kill => 3f,
            HitKind.Head => 2.6f,
            _ => 2f,
        };

        /// <summary>Son opacité à `age` secondes : pleine, puis elle s'efface ; rien avant ni après.</summary>
        public static float Alpha(HitKind kind, float age)
        {
            float duration = Duration(kind), hold = Hold(kind);
            if (!(age >= 0f) || age >= duration) return 0f;
            if (age <= hold) return 1f;
            return 1f - (age - hold) / (duration - hold);
        }

        /// <summary>Sa taille à `age` secondes, en part de sa taille au repos : un peu plus grande au coup.</summary>
        public static float Pop(float age) => 1f + 0.25f * (1f - Mathf.Clamp01(age / PopTime));

        /// <summary>Une touche nouvelle remplace la croix montrée, sauf celle d'un coup qui tue encore à
        /// l'écran : elle reste jusqu'au bout.</summary>
        public static bool Replaces(HitKind shown, float shownAge, HitKind incoming)
            => incoming == HitKind.Kill || shown != HitKind.Kill || !(shownAge < Duration(HitKind.Kill));
    }

    /// <summary>
    /// Le réticule du HUD de combat (2026-10-29, en UI Toolkit à la place de l'OnGUI provisoire, GDD § 15) : la
    /// croix de la hanche, dont les branches s'écartent jusqu'au bord du cône, comme celle de Rainbow Six ; le
    /// point d'un spectateur ; la barre du rechargement ou de la charge sous le point visé ; et le marqueur de
    /// touche (HitMarker), qui se voit aussi en visée et dans la lunette. Un liseré sombre le fait lire sur un mur
    /// clair. Tout se dessine autour de `center`, le point visé à l'écran.
    ///
    /// L'élément ne fait que dessiner ce que lui donne CrosshairUI.
    /// </summary>
    [UxmlElement]
    public partial class CombatReticle : VisualElement
    {
        /// <summary>Le point visé, dans l'élément (unités du panneau).</summary>
        public Vector2 center;

        /// <summary>La croix de la hanche, et l'écart de ses branches au point visé (unités du panneau).</summary>
        public bool crosshair;
        public float gap = 6f;
        public float lineLength = 8f;
        public float thickness = 2f;
        public bool centerDot = true;
        public float dotSize = 2f;

        /// <summary>Le point d'un spectateur, sans arme : pour viser ses lancers et ses coups.</summary>
        public bool spectatorDot;

        /// <summary>La barre sous le point visé (0 à 1), négative sans barre, et sa distance au point visé.</summary>
        public float barProgress = -1f;
        public float barOffset = 28f;
        public Color barColor = Color.white;

        /// <summary>Le marqueur de touche montré (WeaponController.HitKind), négatif sans marqueur, et son âge (s).</summary>
        public int markerKind = -1;
        public float markerAge;

        public Color color = Color.white;
        public Color outline = new Color(0f, 0f, 0f, 0.45f);
        public Color killColor = new Color(1f, 0.3f, 0.22f);

        private const float BarWidth = 64f;
        private const float BarHeight = 4f;

        private readonly UiMesh mesh = new UiMesh();

        public CombatReticle()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("da-combat-reticle");
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            float w = layout.width, h = layout.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w < 1f || h < 1f) return;
            mesh.Clear();
            Vector2 c = center;
            Color32 line = color;
            Color32 shadow = outline;

            if (spectatorDot)
            {
                float d = Mathf.Max(3f, dotSize * 1.5f) * 0.5f;
                mesh.Rect(c.x - d - 1f, c.y - d - 1f, c.x + d + 1f, c.y + d + 1f, new Color(0f, 0f, 0f, 0.5f));
                mesh.Rect(c.x - d, c.y - d, c.x + d, c.y + d, line);
            }

            if (crosshair)
            {
                float t = thickness * 0.5f;
                // Le liseré d'abord, puis les branches : haut, bas, gauche, droite.
                for (int pass = 0; pass < 2; pass++)
                {
                    float o = pass == 0 ? 1f : 0f;
                    Color32 tint = pass == 0 ? shadow : line;
                    mesh.Rect(c.x - t - o, c.y - gap - lineLength - o, c.x + t + o, c.y - gap + o, tint);
                    mesh.Rect(c.x - t - o, c.y + gap - o, c.x + t + o, c.y + gap + lineLength + o, tint);
                    mesh.Rect(c.x - gap - lineLength - o, c.y - t - o, c.x - gap + o, c.y + t + o, tint);
                    mesh.Rect(c.x + gap - o, c.y - t - o, c.x + gap + lineLength + o, c.y + t + o, tint);
                    if (centerDot)
                    {
                        float d = dotSize * 0.5f;
                        mesh.Rect(c.x - d - o, c.y - d - o, c.x + d + o, c.y + d + o, tint);
                    }
                }
            }

            if (barProgress >= 0f)
            {
                // Sous le point visé, là où est le regard pendant un duel ; un fond sombre qui déborde, pour
                // qu'elle se lise aussi sur un mur clair.
                float left = c.x - BarWidth * 0.5f, top = c.y + barOffset;
                mesh.Rect(left - 1f, top - 1f, left + BarWidth + 1f, top + BarHeight + 1f, new Color(0f, 0f, 0f, 0.6f));
                float fill = BarWidth * Mathf.Clamp01(barProgress);
                if (fill > 0f) mesh.Rect(left, top, left + fill, top + BarHeight, barColor);
            }

            if (markerKind >= 0) DrawMarker((HitKind)markerKind, c);
            mesh.Commit(ctx);
        }

        /// <summary>Le marqueur de touche : quatre branches en X, leur liseré sombre dessous.</summary>
        private void DrawMarker(HitKind kind, Vector2 c)
        {
            float alpha = HitMarker.Alpha(kind, markerAge);
            if (alpha <= 0f) return;
            float pop = HitMarker.Pop(markerAge);
            float inner = HitMarker.Inner * pop, reach = HitMarker.Reach(kind) * pop;
            float width = HitMarker.Thickness(kind);
            Color tint = kind == HitKind.Kill ? killColor : color;
            Color32 dark = new Color(outline.r, outline.g, outline.b, outline.a * alpha);
            Color32 bright = new Color(tint.r, tint.g, tint.b, tint.a * alpha);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int k = 0; k < 4; k++)
                {
                    var direction = new Vector2(k < 2 ? 1f : -1f, k % 2 == 0 ? 1f : -1f) * 0.70710678f;
                    if (pass == 0) mesh.Segment(c + direction * (inner - 1f), c + direction * (reach + 1f), width + 2f, dark);
                    else mesh.Segment(c + direction * inner, c + direction * reach, width, bright);
                }
            }
        }
    }
}
