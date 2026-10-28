using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DuelArena.UI
{
    /// <summary>
    /// Ce que voit un robot touché par un caillou (2026-10-27, demandé par l'utilisateur : « comme le sang et la
    /// zone rouge circulaire de Call of Duty ») : une zone rouge sur un cercle autour du réticule, tournée vers
    /// l'endroit d'où le caillou est parti et parcourue d'un arc électrique, et du même côté, au bord de
    /// l'écran, une lueur rouge et quelques arcs qui grésillent un instant. Un robot ne saigne pas, il grésille
    /// (GDD § 6). Le tout reste léger.
    ///
    /// L'élément ne fait que dessiner les coups que lui donne HitIndicatorHud (l'angle de chacun à l'écran, son
    /// âge) : la zone et la lueur en maillages à couleurs de sommets (comme GradientFill), les arcs en traits de
    /// Painter2D, cœur blanc et halo rouge comme ceux du gant (ElectricArc). Ils changent de forme toutes les
    /// 35 ms, comme ceux du gant.
    /// </summary>
    [UxmlElement]
    public partial class HitIndicatorOverlay : VisualElement
    {
        /// <summary>Un coup reçu, tel que l'écran le montre.</summary>
        public struct Mark
        {
            /// <summary>D'où vient le coup, à l'écran (degrés) : 0 devant (en haut), 90 à droite, ±180 derrière (en bas).</summary>
            public float angle;
            /// <summary>Depuis le coup (s).</summary>
            public float age;
            /// <summary>La graine de ses éclairs.</summary>
            public uint seed;
        }

        /// <summary>Les coups à montrer, tenus par HitIndicatorHud.</summary>
        public readonly List<Mark> Marks = new List<Mark>();

        // Les réglages (ressenti), posés par HitIndicatorHud ; les tailles en part de la hauteur de l'écran.
        public float ringRadius = 0.17f;
        public float ringThickness = 0.035f;
        public float ringHalfAngle = 26f;
        public float ringHold = 0.6f;
        public float ringFade = 1.2f;
        public float ringOpacity = 0.55f;
        public float flashDuration = 0.45f;
        public float glowRadius = 0.15f;
        public float glowOpacity = 0.06f;
        public Color energy = new Color(1f, 0.18f, 0.12f);

        /// <summary>Une forme d'éclair toutes les 35 ms, comme ceux du gant (TaserSparkFX).</summary>
        public const float ArcShapeInterval = 0.035f;

        private const int RingSteps = 24;
        private const int GlowSteps = 24;
        private const int EdgeArcs = 3;
        // Les rangs de la lueur, du centre au bord (part du rayon), et sa part d'opacité sur chacun.
        private static readonly float[] GlowRings = { 0.3f, 0.6f, 1f };
        private static readonly float[] GlowLevels = { 0.55f, 0.18f, 0f };

        private readonly Vector2[] ringArc = new Vector2[ElectricArc.PointCount(4)];
        private readonly Vector2[] edgeArc = new Vector2[ElectricArc.PointCount(3)];
        // Les sommets du maillage en cours, pour poser chaque triangle dans le sens qu'attend UI Toolkit.
        private readonly Vector2[] vertices = new Vector2[Mathf.Max((RingSteps + 1) * 3, 1 + GlowSteps * GlowRings.Length)];

        public HitIndicatorOverlay()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("da-hit-indicator");
            generateVisualContent += Draw;
        }

        // ------------------------------------------------------------------
        // Ce qui se calcule (fonctions pures, testées)
        // ------------------------------------------------------------------

        /// <summary>La zone : pleine pendant `hold` secondes, puis elle s'efface en `fade` secondes.</summary>
        public static float RingAlpha(float age, float hold, float fade)
        {
            if (age < 0f || age >= hold + Mathf.Max(0f, fade)) return 0f;
            if (age <= hold) return 1f;
            return Mathf.Clamp01(1f - (age - hold) / fade);
        }

        /// <summary>Les arcs et la lueur du bord : un éclat qui retombe vite, éteint au bout de `duration`.</summary>
        public static float FlashAlpha(float age, float duration)
        {
            if (age < 0f || duration <= 0f || age >= duration) return 0f;
            float rest = 1f - age / duration;
            return rest * rest;
        }

        /// <summary>
        /// Le point du bord de l'écran (`width` × `height`, l'axe y vers le bas) que vise, depuis le centre, la
        /// direction `angle` : 0 le haut, 90 la droite, 180 le bas.
        /// </summary>
        public static Vector2 BorderPoint(float angle, float width, float height)
        {
            Vector2 direction = Direction(angle);
            float toSide = Mathf.Abs(direction.x) > 1e-6f ? width * 0.5f / Mathf.Abs(direction.x) : float.PositiveInfinity;
            float toTop = Mathf.Abs(direction.y) > 1e-6f ? height * 0.5f / Mathf.Abs(direction.y) : float.PositiveInfinity;
            return new Vector2(width * 0.5f, height * 0.5f) + direction * Mathf.Min(toSide, toTop);
        }

        /// <summary>La direction `angle` à l'écran (unitaire, l'axe y vers le bas) : 0 vers le haut, 90 vers la droite.</summary>
        public static Vector2 Direction(float angle)
        {
            float rad = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
        }

        // ------------------------------------------------------------------
        // Le dessin
        // ------------------------------------------------------------------

        private void Draw(MeshGenerationContext ctx)
        {
            float w = layout.width, h = layout.height;
            if (Marks.Count == 0 || float.IsNaN(w) || float.IsNaN(h) || w < 1f || h < 1f) return;
            Vector2 center = new Vector2(w * 0.5f, h * 0.5f);
            Painter2D painter = ctx.painter2D;
            foreach (Mark mark in Marks)
            {
                float ring = RingAlpha(mark.age, ringHold, ringFade) * ringOpacity;
                float flash = FlashAlpha(mark.age, flashDuration);
                if (flash > 0f) DrawGlow(ctx, BorderPoint(mark.angle, w, h), h * glowRadius, glowOpacity * flash);
                if (ring > 0f) DrawRing(ctx, center, h * ringRadius, h * ringThickness, mark.angle, ring);
                if (flash <= 0f) continue;

                // Les éclairs : une forme toutes les 35 ms, d'une intensité tirée avec elle (ils grésillent).
                uint shape = ElectricArc.Hash(mark.seed + (uint)Mathf.FloorToInt(mark.age / ArcShapeInterval) * 0x632be5abU);
                DrawRingArc(painter, center, h, mark.angle, flash, ref shape);
                DrawEdgeArcs(painter, center, w, h, mark.angle, flash, ref shape);
            }
        }

        /// <summary>La zone rouge : un morceau d'anneau autour du réticule, sans bord franc. Trois rangs de
        /// sommets le long de l'arc (le bord intérieur et l'extérieur transparents, le milieu rouge), et les deux
        /// bouts qui s'effacent aussi.</summary>
        private void DrawRing(MeshGenerationContext ctx, Vector2 center, float radius, float thickness, float angle, float alpha)
        {
            int columns = RingSteps + 1;
            MeshWriteData mesh = ctx.Allocate(columns * 3, RingSteps * 12);
            Color32 clear = Tint(0f);
            for (int i = 0; i < columns; i++)
            {
                float t = i / (float)RingSteps;
                Vector2 direction = Direction(angle + (t * 2f - 1f) * ringHalfAngle);
                Color32 middle = Tint(alpha * Mathf.Sin(t * Mathf.PI));
                PutVertex(mesh, i * 3, center + direction * (radius - thickness * 0.5f), clear);
                PutVertex(mesh, i * 3 + 1, center + direction * radius, middle);
                PutVertex(mesh, i * 3 + 2, center + direction * (radius + thickness * 0.5f), clear);
            }
            for (int i = 0; i < RingSteps; i++)
            {
                int here = i * 3, next = here + 3;
                Quad(mesh, here, next, next + 1, here + 1);
                Quad(mesh, here + 1, next + 1, next + 2, here + 2);
            }
        }

        /// <summary>La lueur du bord : une tache rouge centrée sur le bord de l'écran (la moitié en dehors), qui
        /// s'efface en cloche plutôt qu'en cône : une pente droite dessinait un disque (vu le 2026-10-27).</summary>
        private void DrawGlow(MeshGenerationContext ctx, Vector2 at, float radius, float alpha)
        {
            MeshWriteData mesh = ctx.Allocate(1 + GlowSteps * GlowRings.Length, GlowSteps * (3 + (GlowRings.Length - 1) * 6));
            PutVertex(mesh, 0, at, Tint(alpha));
            for (int i = 0; i < GlowSteps; i++)
            {
                float a = i * (2f * Mathf.PI / GlowSteps);
                Vector2 direction = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                for (int r = 0; r < GlowRings.Length; r++)
                    PutVertex(mesh, 1 + i * GlowRings.Length + r, at + direction * (radius * GlowRings[r]), Tint(alpha * GlowLevels[r]));
            }
            for (int i = 0; i < GlowSteps; i++)
            {
                int here = 1 + i * GlowRings.Length, next = 1 + (i + 1) % GlowSteps * GlowRings.Length;
                Triangle(mesh, 0, here, next);
                for (int r = 0; r + 1 < GlowRings.Length; r++)
                    Quad(mesh, here + r, next + r, next + r + 1, here + r + 1);
            }
        }

        /// <summary>L'arc électrique qui parcourt la zone : un éclair plat tracé d'un bout à l'autre, couché
        /// sur l'anneau.</summary>
        private void DrawRingArc(Painter2D painter, Vector2 center, float h, float angle, float flash, ref uint seed)
        {
            float radius = h * ringRadius;
            float half = ringHalfAngle * 0.85f;
            float length = 2f * half * Mathf.Deg2Rad * radius;
            ElectricArc.Build2D(Vector2.zero, Vector2.right, 4, 0.15f, Vector2.zero, ref seed, ringArc);
            for (int i = 0; i < ringArc.Length; i++)
            {
                Vector2 p = ringArc[i];
                ringArc[i] = center + Direction(angle + (p.x * 2f - 1f) * half) * (radius + p.y * length);
            }
            Stroke(painter, ringArc, h, flash * (0.6f + 0.4f * ElectricArc.Next01(ref seed)));
        }

        /// <summary>Les arcs du bord : partis du bord de l'écran du côté du coup, ils entrent un peu dans l'image.</summary>
        private void DrawEdgeArcs(Painter2D painter, Vector2 center, float w, float h, float angle, float flash, ref uint seed)
        {
            Vector2 border = BorderPoint(angle, w, h);
            Vector2 inward = (center - border).normalized;
            Vector2 along = new Vector2(-inward.y, inward.x);
            for (int k = 0; k < EdgeArcs; k++)
            {
                float spread = (ElectricArc.Next01(ref seed) * 2f - 1f) * 0.12f * h;
                float reach = (0.05f + 0.07f * ElectricArc.Next01(ref seed)) * h;
                float tilt = (ElectricArc.Next01(ref seed) * 2f - 1f) * 0.7f;
                Vector2 start = border + along * spread + inward * (0.004f * h);
                Vector2 end = start + (inward + along * tilt).normalized * reach;
                ElectricArc.Build2D(start, end, 3, 0.35f, Vector2.zero, ref seed, edgeArc);
                Stroke(painter, edgeArc, h, flash * (0.55f + 0.45f * ElectricArc.Next01(ref seed)));
            }
        }

        /// <summary>Un éclair : un halo rouge large et transparent, puis un cœur blanc fin, comme ceux du gant.</summary>
        private void Stroke(Painter2D painter, Vector2[] points, float h, float alpha)
        {
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = new Color(energy.r, energy.g, energy.b, 0.4f * alpha);
            painter.lineWidth = Mathf.Max(2f, 0.0065f * h);
            Trace(painter, points);
            painter.Stroke();
            painter.strokeColor = new Color(1f, 0.93f, 0.9f, 0.9f * alpha);
            painter.lineWidth = Mathf.Max(1f, 0.0016f * h);
            Trace(painter, points);
            painter.Stroke();
        }

        private static void Trace(Painter2D painter, Vector2[] points)
        {
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) painter.LineTo(points[i]);
        }

        private Color32 Tint(float alpha) => new Color(energy.r, energy.g, energy.b, Mathf.Clamp01(alpha));

        private void PutVertex(MeshWriteData mesh, int index, Vector2 position, Color32 tint)
        {
            vertices[index] = position;
            mesh.SetNextVertex(new Vertex { position = new Vector3(position.x, position.y, Vertex.nearZ), tint = tint });
        }

        /// <summary>Le quadrilatère a, b, c, d (dans l'ordre de son tour), en deux triangles.</summary>
        private void Quad(MeshWriteData mesh, int a, int b, int c, int d)
        {
            Triangle(mesh, a, b, c);
            Triangle(mesh, a, c, d);
        }

        /// <summary>Un triangle, posé dans le sens des aiguilles d'une montre à l'écran (l'axe y descend), comme
        /// UI Toolkit l'attend (GradientFill).</summary>
        private void Triangle(MeshWriteData mesh, int a, int b, int c)
        {
            Vector2 pa = vertices[a], pb = vertices[b], pc = vertices[c];
            float cross = (pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x);
            mesh.SetNextIndex((ushort)a);
            mesh.SetNextIndex((ushort)(cross >= 0f ? b : c));
            mesh.SetNextIndex((ushort)(cross >= 0f ? c : b));
        }
    }
}
