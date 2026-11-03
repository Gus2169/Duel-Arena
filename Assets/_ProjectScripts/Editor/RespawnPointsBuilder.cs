using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Les points de réapparition d'une arène (2026-11-02, GDD § 11, T8 : chaque arène reçoit ses repères pour les
/// modes) : Duel Arena > Arène > Poser les points de réapparition, la scène de l'arène ouverte.
///
/// Calculés sur son sol : les cases où un robot tient debout, jointes à pied depuis le poste A (une case derrière
/// un mur d'enceinte ne s'atteint pas), et assez loin de tout (Clearance) pour qu'il n'apparaisse pas collé à une
/// caisse. Puis, à partir des postes A et B, chaque fois la case la plus éloignée de celles déjà prises, jusqu'à
/// Count points ou à un écart de MinSpacing : des points répartis dans toute l'arène. Le serveur choisit parmi eux
/// (et les postes) celui que l'adversaire ne voit pas, le plus loin de lui (PlayerSpawnPoints.TryGetRespawnPoint).
/// Chaque point regarde vers le milieu de l'arène.
///
/// Le blocking reste la vérité du jeu : seul le décor qui arrête un robot compte (le layer Default). On corrige ce
/// script et on relance le menu après une retouche du blocking ; les points se retouchent aussi à la main.
/// </summary>
public static class RespawnPointsBuilder
{
    /// <summary>Le pas de la grille du sol (m).</summary>
    private const float Cell = 0.5f;
    /// <summary>Le rayon d'un robot qui passe (m), un peu plus que sa capsule.</summary>
    private const float PassRadius = 0.4f;
    /// <summary>Le rayon libre autour d'un point (m) : ni caisse ni mur à portée de main.</summary>
    private const float Clearance = 1f;
    /// <summary>La hauteur d'un robot debout (m).</summary>
    private const float Height = 1.85f;
    /// <summary>Au plus autant de points, en plus des postes A et B.</summary>
    private const int Count = 16;
    /// <summary>Pas deux points plus près que ça (m).</summary>
    private const float MinSpacing = 7f;
    /// <summary>Jusqu'où chercher le sol autour du poste A (m) : bien plus que la plus grande arène.</summary>
    private const float SearchRadius = 90f;
    private const int WorldMask = 1;   // Default : le décor qui arrête un robot
    public const string GroupName = "Réapparitions";

    [MenuItem("Duel Arena/Arène/Poser les points de réapparition")]
    public static void BuildMenu()
    {
        string report = Build();
        Debug.Log("[Réapparitions] " + report);
    }

    /// <summary>Pose les points dans la scène ouverte et l'enregistre. Renvoie ce qui a été fait.</summary>
    public static string Build()
    {
        PlayerSpawnPoints points = Object.FindAnyObjectByType<PlayerSpawnPoints>();
        if (points == null) return "Pas de PlayerSpawnPoints dans la scène ouverte.";
        var so = new SerializedObject(points);
        SerializedProperty spawnArray = so.FindProperty("spawnPoints");
        var posts = new List<Vector3>();
        for (int i = 0; i < spawnArray.arraySize; i++)
        {
            if (spawnArray.GetArrayElementAtIndex(i).objectReferenceValue is Transform t) posts.Add(t.position);
        }
        if (posts.Count == 0) return "Les points d'apparition (postes A et B) manquent : rien à partir d'eux.";

        Physics.SyncTransforms();
        List<Vector3> floor = ReachableFloor(posts[0]);
        if (floor.Count == 0) return "Aucun sol où tient un robot autour du poste A.";
        Vector3 middle = Vector3.zero;
        foreach (Vector3 p in floor) middle += p;
        middle /= floor.Count;

        var clear = new List<Vector3>();
        foreach (Vector3 p in floor)
        {
            if (!Physics.CheckCapsule(p + Vector3.up * (Clearance + 0.1f), p + Vector3.up * Mathf.Max(Clearance + 0.1f, Height - Clearance),
                                      Clearance, WorldMask, QueryTriggerInteraction.Ignore))
                clear.Add(p);
        }
        List<Vector3> chosen = Spread(clear, posts, Count, MinSpacing);

        // Le groupe d'avant (posé par ce menu) laisse la place au nouveau.
        Transform old = points.transform.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject(GroupName).transform;
        group.SetParent(points.transform, false);
        var made = new Transform[chosen.Count];
        for (int i = 0; i < chosen.Count; i++)
        {
            var point = new GameObject($"Réapparition {i + 1:00}").transform;
            point.SetParent(group, true);
            point.position = chosen[i];
            Vector3 toMiddle = middle - chosen[i];
            toMiddle.y = 0f;
            point.rotation = toMiddle.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toMiddle) : Quaternion.identity;
            made[i] = point;
        }

        SerializedProperty respawnArray = so.FindProperty("respawnPoints");
        respawnArray.arraySize = made.Length;
        for (int i = 0; i < made.Length; i++) respawnArray.GetArrayElementAtIndex(i).objectReferenceValue = made[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(points.gameObject.scene);
        EditorSceneManager.SaveScene(points.gameObject.scene);
        return $"{made.Length} points posés ({floor.Count} cases de sol jointes à pied, {clear.Count} dégagées).";
    }

    /// <summary>Les cases du sol où un robot tient debout, jointes à pied depuis `start` (à 0,5 m près, de proche en
    /// proche, sans sauter ni grimper : au niveau du sol de départ).</summary>
    public static List<Vector3> ReachableFloor(Vector3 start)
    {
        var result = new List<Vector3>();
        var seen = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        Vector2Int origin = Vector2Int.zero;
        if (!Stands(CellPosition(start, origin))) return result;
        queue.Enqueue(origin);
        seen.Add(origin);
        int limit = Mathf.CeilToInt(SearchRadius / Cell);
        var steps = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            result.Add(CellPosition(start, cell));
            foreach (Vector2Int step in steps)
            {
                Vector2Int next = cell + step;
                if (Mathf.Abs(next.x) > limit || Mathf.Abs(next.y) > limit || !seen.Add(next)) continue;
                if (Stands(CellPosition(start, next))) queue.Enqueue(next);
            }
        }
        return result;
    }

    private static Vector3 CellPosition(Vector3 start, Vector2Int cell) => start + new Vector3(cell.x * Cell, 0f, cell.y * Cell);

    /// <summary>Un robot tient-il debout là : du sol sous ses pieds, rien dans sa capsule.</summary>
    private static bool Stands(Vector3 feet)
    {
        if (!Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 0.8f, WorldMask, QueryTriggerInteraction.Ignore)
            || ground.normal.y < 0.7f || Mathf.Abs(ground.point.y - feet.y) > 0.1f)
            return false;
        return !Physics.CheckCapsule(feet + Vector3.up * (PassRadius + 0.05f), feet + Vector3.up * (Height - PassRadius),
                                     PassRadius, WorldMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// Des points répartis : à partir de `fixedPoints` (les postes), chaque fois le candidat le plus éloigné de tous
    /// ceux déjà pris, tant qu'il en reste et qu'il est à `minSpacing` au moins. Fonction pure, déterministe.
    /// </summary>
    public static List<Vector3> Spread(IReadOnlyList<Vector3> candidates, IReadOnlyList<Vector3> fixedPoints, int count, float minSpacing)
    {
        var chosen = new List<Vector3>();
        var nearest = new float[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            nearest[i] = float.PositiveInfinity;
            foreach (Vector3 f in fixedPoints) nearest[i] = Mathf.Min(nearest[i], Flat(candidates[i], f));
        }
        while (chosen.Count < count)
        {
            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (best < 0 || nearest[i] > nearest[best]) best = i;
            }
            if (best < 0 || nearest[best] < minSpacing) break;
            Vector3 pick = candidates[best];
            chosen.Add(pick);
            for (int i = 0; i < candidates.Count; i++) nearest[i] = Mathf.Min(nearest[i], Flat(candidates[i], pick));
        }
        return chosen;
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
}
