using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Points d'apparition des joueurs, posés dans la scène. Lu UNIQUEMENT par le serveur : c'est lui
/// qui décide où chaque joueur apparaît, puis publie le résultat dans sa pose réseau
/// (voir PlayerLocomotion.ServerMoveToSpawnPoint). Aucun client ne choisit son point de spawn —
/// même principe que pour les dégâts.
///
/// Sélection : le point le plus ÉLOIGNÉ des joueurs déjà présents. Pour un duel 1v1 avec deux
/// points opposés, ça donne naturellement un joueur à chaque bout de l'arène, et ça évite qu'un
/// nouveau venu apparaisse dans les pieds de l'autre. Le même comportement servira tel quel à la
/// boucle de round (BO5), qui devra replacer les deux joueurs à chaque manche.
///
/// Mise en place : poser ce composant sur un GameObject de la scène et renseigner un Transform par
/// point de spawn. La POSITION du Transform donne les pieds du joueur, sa ROTATION Y donne la
/// direction dans laquelle il regarde en apparaissant (faire pointer le gizmo vers l'arène).
/// Sans composant dans la scène, ou sans point renseigné, les joueurs apparaissent simplement à la
/// position du prefab — comportement d'avant, pas d'erreur.
/// </summary>
public class PlayerSpawnPoints : MonoBehaviour
{
    [Tooltip("Un Transform par point d'apparition. Position = pieds du joueur, rotation Y = direction du regard au spawn.")]
    [SerializeField] private Transform[] spawnPoints;

    [Tooltip("Les points de réapparition des modes qui en ont (le Gun Game, GDD § 11), en plus des points d'apparition. Posés par Duel Arena > Arène > Poser les points de réapparition, à refaire pour chaque arène.")]
    [SerializeField] private Transform[] respawnPoints = System.Array.Empty<Transform>();

    /// <summary>Le décor arrête le regard (Default) : les robots, les épaves et les triggers non.</summary>
    private const int SightMask = 1;

    /// <summary>D'où se voit un robot qui réapparaît : sa tête et son buste, au-dessus de ses pieds (m).</summary>
    private static readonly float[] SeenHeights = { 1.6f, 1.1f };

    private static PlayerSpawnPoints cached;

    /// <summary>Instance présente dans la scène, ou null s'il n'y en a pas. Le test `cached == null`
    /// s'appuie sur la surcharge Unity de l'opérateur, qui renvoie vrai aussi pour un objet détruit :
    /// la référence se réévalue donc correctement après un changement de scène.</summary>
    public static PlayerSpawnPoints Instance
    {
        get
        {
            if (cached == null) cached = FindAnyObjectByType<PlayerSpawnPoints>();
            return cached;
        }
    }

    /// <summary>Le poste `post` de l'arène, dans l'ordre des points d'apparition (0 : le poste A, 1 : le poste B ; GDD § 5,
    /// T3). Faux s'il n'existe pas.</summary>
    public bool TryGetPost(int post, out Vector3 position, out float yaw)
    {
        position = default;
        yaw = 0f;
        if (spawnPoints == null || post < 0 || post >= spawnPoints.Length || spawnPoints[post] == null) return false;
        position = spawnPoints[post].position;
        yaw = spawnPoints[post].eulerAngles.y;
        return true;
    }

    /// <summary>
    /// Choisit le point de spawn le plus éloigné des positions déjà occupées.
    /// Renvoie false s'il n'y a aucun point utilisable — l'appelant garde alors sa position.
    /// </summary>
    public bool TryGetSpawnPointFarthestFrom(List<Vector3> occupied, out Vector3 position, out float yaw)
    {
        position = default;
        yaw = 0f;

        if (spawnPoints == null || spawnPoints.Length == 0) return false;

        Transform best = null;
        float bestDistance = float.NegativeInfinity;

        foreach (Transform point in spawnPoints)
        {
            if (point == null) continue;

            // Distance au joueur le PLUS PROCHE de ce point : c'est elle qu'on veut maximiser.
            // Aucun joueur présent => reste à +Infinity, et le premier point valide l'emporte
            // (comparaison stricte), ce qui rend le choix déterministe sur un spawn isolé.
            float distanceToNearest = float.PositiveInfinity;
            if (occupied != null)
            {
                foreach (Vector3 other in occupied)
                {
                    distanceToNearest = Mathf.Min(distanceToNearest, (point.position - other).sqrMagnitude);
                }
            }

            if (distanceToNearest > bestDistance)
            {
                bestDistance = distanceToNearest;
                best = point;
            }
        }

        if (best == null) return false;

        position = best.position;
        yaw = best.eulerAngles.y;
        return true;
    }

    /// <summary>
    /// La réapparition d'un mode qui en a (GDD § 11, T2) : le point le plus éloigné des adversaires parmi ceux
    /// qu'aucun d'eux ne voit, choisi par le serveur ; si tous sont en vue, le plus éloigné. `opponentEyes` : d'où
    /// regardent les adversaires. Les points de réapparition et ceux d'apparition comptent. Faux s'il n'y en a pas.
    /// </summary>
    public bool TryGetRespawnPoint(IReadOnlyList<Vector3> opponentEyes, out Vector3 position, out float yaw)
        => TryGetRespawnPoint(opponentEyes, null, 0f, out position, out yaw);

    /// <summary>TryGetRespawnPoint, sans les points à moins de `avoidRadius` (à plat) de ceux de `avoid` : à
    /// l'Élimination confirmée, les modules du mort, qu'il reprendrait sans le vouloir. S'ils sont tous trop près, la
    /// règle d'avant s'applique.</summary>
    public bool TryGetRespawnPoint(IReadOnlyList<Vector3> opponentEyes, IReadOnlyList<Vector3> avoid, float avoidRadius,
                                   out Vector3 position, out float yaw)
    {
        position = default;
        yaw = 0f;
        candidates.Clear();
        AddCandidates(respawnPoints);
        AddCandidates(spawnPoints);
        if (candidates.Count == 0) return false;

        distances.Clear();
        hidden.Clear();
        allowed.Clear();
        foreach (Transform point in candidates)
        {
            distances.Add(DistanceToNearest(point.position, opponentEyes));
            hidden.Add(HiddenFrom(point.position, opponentEyes));
            allowed.Add(DistanceToNearest(point.position, avoid) > avoidRadius);
        }
        int best = PickRespawn(distances, hidden, allowed);
        position = candidates[best].position;
        yaw = candidates[best].eulerAngles.y;
        return true;
    }

    private readonly List<Transform> candidates = new List<Transform>();
    private readonly List<float> distances = new List<float>();
    private readonly List<bool> hidden = new List<bool>();
    private readonly List<bool> allowed = new List<bool>();

    private void AddCandidates(Transform[] points)
    {
        if (points == null) return;
        foreach (Transform point in points)
        {
            if (point != null) candidates.Add(point);
        }
    }

    /// <summary>La distance à plat du point à l'adversaire le plus proche (l'infini sans adversaire).</summary>
    private static float DistanceToNearest(Vector3 point, IReadOnlyList<Vector3> eyes)
    {
        float nearest = float.PositiveInfinity;
        if (eyes == null) return nearest;
        foreach (Vector3 eye in eyes)
        {
            Vector2 flat = new Vector2(point.x - eye.x, point.z - eye.z);
            nearest = Mathf.Min(nearest, flat.magnitude);
        }
        return nearest;
    }

    /// <summary>Aucun adversaire ne voit-il la tête ni le buste d'un robot posé là ? Seul le décor arrête le regard.</summary>
    private static bool HiddenFrom(Vector3 point, IReadOnlyList<Vector3> eyes)
    {
        if (eyes == null) return true;
        foreach (Vector3 eye in eyes)
        {
            foreach (float height in SeenHeights)
            {
                if (!Physics.Linecast(eye, point + Vector3.up * height, SightMask, QueryTriggerInteraction.Ignore)) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Le point de réapparition : parmi ceux qu'aucun adversaire ne voit (`hidden`), le plus éloigné d'eux
    /// (`distances`, à l'adversaire le plus proche) ; si tous sont en vue, le plus éloigné. À égalité, le premier.
    /// Renvoie son indice (-1 sans point). Fonction pure, testée.
    /// </summary>
    public static int PickRespawn(IReadOnlyList<float> distances, IReadOnlyList<bool> hidden) => PickRespawn(distances, hidden, null);

    /// <summary>PickRespawn parmi les seuls points permis (`allowed`, faux : trop près d'un module du mort, à
    /// l'Élimination confirmée) ; s'il n'y en a aucun, parmi tous. Fonction pure, testée.</summary>
    public static int PickRespawn(IReadOnlyList<float> distances, IReadOnlyList<bool> hidden, IReadOnlyList<bool> allowed)
    {
        bool anyAllowed = false;
        for (int i = 0; allowed != null && i < distances.Count && i < allowed.Count; i++) anyAllowed |= allowed[i];
        int best = -1;
        bool bestHidden = false;
        float bestDistance = float.NegativeInfinity;
        for (int i = 0; i < distances.Count; i++)
        {
            if (anyAllowed && (i >= allowed.Count || !allowed[i])) continue;
            bool isHidden = hidden != null && i < hidden.Count && hidden[i];
            if (best >= 0 && bestHidden && !isHidden) continue;
            if (best >= 0 && isHidden == bestHidden && distances[i] <= bestDistance) continue;
            best = i;
            bestHidden = isHidden;
            bestDistance = distances[i];
        }
        return best;
    }

    private void OnDrawGizmos()
    {
        DrawPoints(spawnPoints, Color.cyan);
        DrawPoints(respawnPoints, new Color(1f, 0.6f, 0.1f));
    }

    private static void DrawPoints(Transform[] points, Color color)
    {
        if (points == null) return;

        foreach (Transform point in points)
        {
            if (point == null) continue;

            // Capsule debout approximative + flèche de direction, pour vérifier d'un coup d'œil
            // qu'un point n'est pas dans un mur et qu'il regarde bien vers l'arène.
            Gizmos.color = color;
            Gizmos.DrawWireSphere(point.position + Vector3.up * 0.35f, 0.35f);
            Gizmos.DrawWireSphere(point.position + Vector3.up * 1.45f, 0.35f);
            Gizmos.DrawLine(point.position, point.position + Vector3.up * 1.8f);
            Gizmos.DrawRay(point.position + Vector3.up * 1.6f, point.forward * 1.5f);
        }
    }
}
