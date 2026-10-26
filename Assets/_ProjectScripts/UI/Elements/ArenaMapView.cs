using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DuelArena.UI
{
    /// <summary>
    /// La carte de l'arène du salon (2026-10-17, demandée par l'utilisateur : « un visuel de la map avec les
    /// éléments clés ») : l'arène vue de dessus (MenuArtwork), le tour de la tribune en pointillés, et les
    /// repères posés dessus, les points d'apparition (A, B) et les blocs d'où les spectateurs tirent leurs
    /// cailloux. L'image garde ses proportions (bandes sur les côtés si le cadre n'a pas les mêmes) ; les
    /// repères suivent l'image affichée.
    /// </summary>
    [UxmlElement]
    public partial class ArenaMapView : VisualElement
    {
        /// <summary>Le jaune des points d'apparition, le cyan de la tribune, l'orange des blocs : les
        /// couleurs de la légende (LobbyController).</summary>
        public static readonly Color SpawnColor = new Color(0.957f, 0.745f, 0.11f);
        public static readonly Color WalkwayColor = new Color(0.36f, 0.84f, 0.95f);
        public static readonly Color RockColor = new Color(0.95f, 0.5f, 0.2f);

        private readonly VisualElement image;
        private readonly VisualElement overlay;
        private readonly List<(MapMarker marker, VisualElement element)> markers = new();
        private Texture2D texture;
        private Rect worldRect = new Rect(0f, 0f, 1f, 1f);
        private Vector2[] rail = System.Array.Empty<Vector2>();
        private Rect shown;

        public ArenaMapView()
        {
            AddToClassList("arena-map");
            image = new VisualElement { pickingMode = PickingMode.Ignore };
            image.AddToClassList("arena-map__image");
            Add(image);
            overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.AddToClassList("arena-map__overlay");
            overlay.generateVisualContent += DrawRail;
            Add(overlay);
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        /// <summary>Montre la carte de `artwork` (rien sans elle).</summary>
        public void SetMap(MenuArtwork artwork)
        {
            texture = artwork != null ? artwork.arenaTopDown : null;
            worldRect = artwork != null ? artwork.topDownWorldRect : new Rect(0f, 0f, 1f, 1f);
            rail = artwork != null && artwork.walkwayRail != null ? artwork.walkwayRail : System.Array.Empty<Vector2>();
            image.style.backgroundImage = texture != null ? new StyleBackground(texture) : new StyleBackground(StyleKeyword.None);

            foreach ((MapMarker _, VisualElement element) in markers) element.RemoveFromHierarchy();
            markers.Clear();
            if (artwork != null && artwork.mapMarkers != null)
            {
                foreach (MapMarker marker in artwork.mapMarkers) markers.Add((marker, NewMarker(marker)));
            }
            Layout();
        }

        private VisualElement NewMarker(MapMarker marker)
        {
            // Le repère est centré sur son point (marges négatives en USS) ; sa forme tourne, pas son libellé.
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList("map-marker");
            element.AddToClassList(marker.kind == MapMarkerKind.Spawn ? "map-marker--spawn" : "map-marker--rock");
            var shape = new VisualElement { pickingMode = PickingMode.Ignore };
            shape.AddToClassList("map-marker__shape");
            element.Add(shape);
            if (marker.kind == MapMarkerKind.Spawn && !string.IsNullOrEmpty(marker.label))
            {
                var label = new Label(marker.label) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("map-marker__label");
                element.Add(label);
            }
            Add(element);
            return element;
        }

        private void Layout()
        {
            Rect box = contentRect;
            if (float.IsNaN(box.width) || box.width < 1f || box.height < 1f) return;

            // L'image entière, à ses proportions, centrée.
            float aspect = texture != null && texture.height > 0 ? (float)texture.width / texture.height
                : worldRect.height > 0f ? worldRect.width / worldRect.height : 1f;
            float width = box.width, height = box.width / aspect;
            if (height > box.height) { height = box.height; width = height * aspect; }
            shown = new Rect((box.width - width) * 0.5f, (box.height - height) * 0.5f, width, height);
            image.style.left = shown.x;
            image.style.top = shown.y;
            image.style.width = shown.width;
            image.style.height = shown.height;

            foreach ((MapMarker marker, VisualElement element) in markers)
            {
                Vector2 p = ToLocal(marker.position);
                element.style.left = p.x;
                element.style.top = p.y;
            }
            overlay.MarkDirtyRepaint();
        }

        private Vector2 ToLocal(Vector2 ground)
        {
            Vector2 uv = MenuArtwork.MapUV(worldRect, ground);
            return new Vector2(shown.x + uv.x * shown.width, shown.y + uv.y * shown.height);
        }

        private void DrawRail(MeshGenerationContext ctx)
        {
            if (rail.Length < 2 || shown.width < 1f) return;
            Painter2D p = ctx.painter2D;
            p.strokeColor = new Color(WalkwayColor.r, WalkwayColor.g, WalkwayColor.b, 0.9f);
            p.lineWidth = 2f;
            p.lineJoin = LineJoin.Round;
            p.SetDashPattern(7f, 5f);
            p.BeginPath();
            p.MoveTo(ToLocal(rail[0]));
            for (int i = 1; i < rail.Length; i++) p.LineTo(ToLocal(rail[i]));
            p.ClosePath();
            p.Stroke();
        }
    }
}
