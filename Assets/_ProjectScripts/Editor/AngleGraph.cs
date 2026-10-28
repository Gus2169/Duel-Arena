using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// La toile de l'éditeur des armes où se dessinent le motif de recul et la gerbe (2026-10-26) : des
/// angles en degrés, x à droite, y en haut, à la même échelle sur les deux axes (un motif ne se
/// déforme pas). Le cadre s'ajuste à ce qu'il montre, sauf pendant qu'on y fait glisser un point.
///
/// IMGUI : les traits passent par Handles, qui dessine en coordonnées de la fenêtre. La toile ne doit
/// donc jamais être dans une zone qui défile (BeginScrollView).
/// </summary>
internal sealed class AngleGraph
{
    private static readonly float[] Steps = { 0.05f, 0.1f, 0.2f, 0.25f, 0.5f, 1f, 2f, 2.5f, 5f, 10f, 20f, 25f, 50f };

    public Rect Rect { get; private set; }
    private Vector2 center;
    private float pixelsPerDegree = 20f;
    private bool frozen;

    /// <summary>Le cadre reste où il est (pendant qu'on fait glisser un point) : sans ça, il
    /// s'ajusterait sous la souris.</summary>
    public bool Frozen { get => frozen; set => frozen = value; }

    public float PixelsPerDegree => pixelsPerDegree;

    /// <summary>Place la toile dans `rect` et cadre `points` (degrés), l'axe compris, avec une marge et
    /// au moins `minSpan` degrés de côté.</summary>
    public void Fit(Rect rect, List<Vector2> points, float minSpan)
    {
        Rect = rect;
        if (frozen) return;
        Vector2 min = Vector2.zero, max = Vector2.zero;
        foreach (Vector2 p in points)
        {
            if (!float.IsFinite(p.x) || !float.IsFinite(p.y)) continue;
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        Vector2 span = Vector2.Max(max - min, new Vector2(minSpan, minSpan)) * 1.18f;
        center = (min + max) * 0.5f;
        float width = Mathf.Max(10f, rect.width - 36f), height = Mathf.Max(10f, rect.height - 28f);
        pixelsPerDegree = Mathf.Min(width / span.x, height / span.y);
    }

    public Vector2 ToPixel(Vector2 degrees)
        => new Vector2(Rect.center.x + (degrees.x - center.x) * pixelsPerDegree, Rect.center.y - (degrees.y - center.y) * pixelsPerDegree);

    public Vector2 ToDegrees(Vector2 pixel)
        => new Vector2(center.x + (pixel.x - Rect.center.x) / pixelsPerDegree, center.y - (pixel.y - Rect.center.y) / pixelsPerDegree);

    /// <summary>Le fond, la grille (un trait par pas, choisi pour rester lisible) et les axes du premier
    /// coup, avec leurs graduations.</summary>
    public void DrawBackground()
    {
        if (Event.current.type != EventType.Repaint) return;
        bool dark = EditorGUIUtility.isProSkin;
        EditorGUI.DrawRect(Rect, dark ? new Color(0.13f, 0.13f, 0.14f) : new Color(0.93f, 0.93f, 0.93f));
        Color minor = dark ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.07f);
        Color axis = dark ? new Color(1f, 1f, 1f, 0.28f) : new Color(0f, 0f, 0f, 0.3f);

        float step = Steps[Steps.Length - 1];
        foreach (float candidate in Steps)
            if (candidate * pixelsPerDegree >= 26f) { step = candidate; break; }
        int labelEvery = step * pixelsPerDegree >= 44f ? 1 : 2;

        Vector2 low = ToDegrees(new Vector2(Rect.xMin, Rect.yMax)), high = ToDegrees(new Vector2(Rect.xMax, Rect.yMin));
        GUIStyle label = Styles.Tick;
        for (int i = Mathf.CeilToInt(low.x / step); i * step <= high.x; i++)
        {
            float x = ToPixel(new Vector2(i * step, 0f)).x;
            EditorGUI.DrawRect(new Rect(x, Rect.yMin, 1f, Rect.height), i == 0 ? axis : minor);
            if (i != 0 && i % labelEvery == 0)
                label.Draw(new Rect(x + 2f, Rect.yMax - 15f, 50f, 14f), Number(i * step) + "°", false, false, false, false);
        }
        for (int i = Mathf.CeilToInt(low.y / step); i * step <= high.y; i++)
        {
            float y = ToPixel(new Vector2(0f, i * step)).y;
            EditorGUI.DrawRect(new Rect(Rect.xMin, y, Rect.width, 1f), i == 0 ? axis : minor);
            if (i != 0 && i % labelEvery == 0)
                label.Draw(new Rect(Rect.xMin + 3f, y - 14f, 50f, 14f), Number(i * step) + "°", false, false, false, false);
        }
    }

    public void Polyline(IReadOnlyList<Vector2> degrees, Color color, float width)
    {
        if (Event.current.type != EventType.Repaint || degrees.Count < 2) return;
        var pixels = new Vector3[degrees.Count];
        for (int i = 0; i < degrees.Count; i++) pixels[i] = ToPixel(degrees[i]);
        Handles.color = color;
        Handles.DrawAAPolyLine(width, pixels);
    }

    public void Dot(Vector2 degrees, float radius, Color color)
    {
        if (Event.current.type != EventType.Repaint) return;
        Handles.color = color;
        Handles.DrawSolidDisc(ToPixel(degrees), Vector3.forward, radius);
    }

    public void Ring(Vector2 degrees, float radius, Color color, float thickness = 1.5f)
    {
        if (Event.current.type != EventType.Repaint) return;
        Handles.color = color;
        Handles.DrawWireDisc(ToPixel(degrees), Vector3.forward, radius, thickness);
    }

    /// <summary>Une zone de la cible : une capsule de `a` à `b` (une sphère si elles se confondent), de
    /// rayon `radius`, le tout en degrés.</summary>
    public void Capsule(Vector2 a, Vector2 b, float radius, Color fill, Color outline)
    {
        if (Event.current.type != EventType.Repaint) return;
        Vector2 pa = ToPixel(a), pb = ToPixel(b);
        float r = radius * pixelsPerDegree;
        Vector2 direction = pb - pa;
        if (direction.sqrMagnitude < 1e-6f) direction = Vector2.up;
        direction.Normalize();
        var side = new Vector2(-direction.y, direction.x);
        const int arc = 12;
        var outlinePoints = new Vector3[arc * 2 + 3];
        for (int i = 0; i <= arc; i++)
        {
            float angle = Mathf.PI * i / arc;
            outlinePoints[i] = pb + (side * Mathf.Cos(angle) + direction * Mathf.Sin(angle)) * r;
            outlinePoints[arc + 1 + i] = pa + (-side * Mathf.Cos(angle) - direction * Mathf.Sin(angle)) * r;
        }
        outlinePoints[outlinePoints.Length - 1] = outlinePoints[0];
        Handles.color = fill;
        Handles.DrawAAConvexPolygon(outlinePoints);
        Handles.color = outline;
        Handles.DrawAAPolyLine(1.5f, outlinePoints);
    }

    /// <summary>Un texte posé près d'un point (décalé de `offset` pixels).</summary>
    public void Label(Vector2 degrees, string text, Vector2 offset, GUIStyle style)
    {
        if (Event.current.type != EventType.Repaint) return;
        Vector2 p = ToPixel(degrees) + offset;
        Vector2 size = style.CalcSize(new GUIContent(text));
        style.Draw(new Rect(p.x, p.y, size.x, size.y), text, false, false, false, false);
    }

    /// <summary>Le point de `points` (degrés) le plus proche de `mouse` (pixels), à moins de
    /// `radius` pixels, en commençant à `first` ; -1 sinon.</summary>
    public int Pick(IReadOnlyList<Vector2> points, Vector2 mouse, float radius, int first = 0)
    {
        int best = -1;
        float bestDistance = radius * radius;
        for (int i = first; i < points.Count; i++)
        {
            float d = (ToPixel(points[i]) - mouse).sqrMagnitude;
            if (d <= bestDistance) { best = i; bestDistance = d; }
        }
        return best;
    }

    private static string Number(float value) => WeaponStatSheet.Number(value);

    private static class Styles
    {
        private static GUIStyle tick;
        public static GUIStyle Tick => tick ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.4f) : new Color(0f, 0f, 0f, 0.45f) },
        };
    }
}
