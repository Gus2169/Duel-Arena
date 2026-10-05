using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Points d'apparition des joueurs, posés dans la scène. Lu UNIQUEMENT par le serveur : c'est lui
/// qui décide où chaque joueur apparaît, puis publie le résultat via networkPosition/networkYaw
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

    private void OnDrawGizmos()
    {
        if (spawnPoints == null) return;

        foreach (Transform point in spawnPoints)
        {
            if (point == null) continue;

            // Capsule debout approximative + flèche de direction, pour vérifier d'un coup d'œil
            // qu'un point n'est pas dans un mur et qu'il regarde bien vers l'arène.
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(point.position + Vector3.up * 0.35f, 0.35f);
            Gizmos.DrawWireSphere(point.position + Vector3.up * 1.45f, 0.35f);
            Gizmos.DrawLine(point.position, point.position + Vector3.up * 1.8f);
            Gizmos.DrawRay(point.position + Vector3.up * 1.6f, point.forward * 1.5f);
        }
    }
}
