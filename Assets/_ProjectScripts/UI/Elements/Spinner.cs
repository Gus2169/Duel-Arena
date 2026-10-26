using UnityEngine;
using UnityEngine.UIElements;

namespace DuelArena.UI
{
    /// <summary>
    /// L'indicateur d'attente du menu (« en attente d'un adversaire »), d'après les maquettes : un arc qui
    /// tourne. Couleur en USS : `--spinner-color`. Ne tourne que tant qu'il est affiché.
    /// </summary>
    [UxmlElement]
    public partial class Spinner : VisualElement
    {
        private static readonly CustomStyleProperty<Color> ColorProperty = new("--spinner-color");

        private Color color = new Color(0.957f, 0.745f, 0.11f);
        private float angle;
        private IVisualElementScheduledItem spin;

        public Spinner()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("da-spinner");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(evt =>
            {
                if (evt.customStyle.TryGetValue(ColorProperty, out Color c)) color = c;
                MarkDirtyRepaint();
            });
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                spin ??= schedule.Execute(() =>
                {
                    if (resolvedStyle.display == DisplayStyle.None) return;
                    angle = (angle + 9f) % 360f;
                    MarkDirtyRepaint();
                }).Every(16);
                spin.Resume();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => spin?.Pause());
        }

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            float size = Mathf.Min(r.width, r.height);
            if (size < 4f) return;
            float width = Mathf.Max(2f, size * 0.12f);
            var center = new Vector2(r.width * 0.5f, r.height * 0.5f);
            Painter2D p = ctx.painter2D;

            p.lineWidth = width;
            p.lineCap = LineCap.Butt;
            p.strokeColor = new Color(color.r, color.g, color.b, color.a * 0.18f);
            p.BeginPath();
            p.Arc(center, size * 0.5f - width, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
            p.strokeColor = color;
            p.BeginPath();
            p.Arc(center, size * 0.5f - width, Angle.Degrees(angle), Angle.Degrees(angle + 100f));
            p.Stroke();
        }
    }
}
