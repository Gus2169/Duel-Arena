using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Les LineRenderer d'un jeu d'éclairs (ElectricArc), créés une fois sous `parent` et réutilisés :
/// chaque ligne a son tableau de points, à la taille de son éclair, et rien n'est alloué ensuite. En
/// coordonnées du monde, tournées vers la caméra qui les dessine ; la force de chaque arc passe par
/// l'alpha des sommets, que lit le shader DuelArena/ArcElectrique. Catégorie C.
/// </summary>
public sealed class ArcLines
{
    private readonly LineRenderer[] lines;
    private readonly Vector3[][] points;

    /// <param name="generations">Les générations de l'éclair de chaque ligne (ElectricArc.Build).</param>
    /// <param name="widthCurve">L'amincissement le long de l'arc, ou null pour une largeur constante.</param>
    public ArcLines(Transform parent, string name, Material material, int layer, int[] generations, AnimationCurve widthCurve)
    {
        Root = new GameObject(name);
        Root.layer = layer;
        Root.transform.SetParent(parent, false);
        lines = new LineRenderer[generations.Length];
        points = new Vector3[generations.Length][];
        for (int i = 0; i < lines.Length; i++)
        {
            var go = new GameObject("Arc " + (i + 1));
            go.layer = layer;
            go.transform.SetParent(Root.transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCornerVertices = 2;
            line.numCapVertices = 0;
            if (widthCurve != null) line.widthCurve = widthCurve;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.allowOcclusionWhenDynamic = false;
            points[i] = new Vector3[ElectricArc.PointCount(generations[i])];
            line.positionCount = points[i].Length;
            line.enabled = false;
            lines[i] = line;
        }
    }

    /// <summary>L'objet qui porte les lignes.</summary>
    public GameObject Root { get; }

    public int Count => lines.Length;

    /// <summary>Les points de la ligne `i`, à remplir avant Show.</summary>
    public Vector3[] Points(int i) => points[i];

    public LineRenderer Line(int i) => lines[i];

    /// <summary>Montre la ligne `i` sur ses points : largeur (m) et force à son départ et à son bout.</summary>
    public void Show(int i, float width, float startStrength, float endStrength)
    {
        LineRenderer line = lines[i];
        line.SetPositions(points[i]);
        line.widthMultiplier = width;
        line.startColor = new Color(1f, 1f, 1f, startStrength);
        line.endColor = new Color(1f, 1f, 1f, endStrength);
        if (!line.enabled) line.enabled = true;
    }

    public void Hide(int i)
    {
        if (lines[i].enabled) lines[i].enabled = false;
    }

    public void HideAll()
    {
        for (int i = 0; i < lines.Length; i++) Hide(i);
    }

    /// <summary>Combien de lignes sont montrées (tests).</summary>
    public int ShownCount
    {
        get
        {
            int n = 0;
            foreach (LineRenderer line in lines) if (line.enabled) n++;
            return n;
        }
    }

    public void Destroy()
    {
        if (Root == null) return;
        if (Application.isPlaying) Object.Destroy(Root);
        else Object.DestroyImmediate(Root);
    }
}
