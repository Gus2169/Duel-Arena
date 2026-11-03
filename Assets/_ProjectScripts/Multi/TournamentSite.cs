using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Le site du Tournoi dans la scène de l'arène (2026-11-03, GDD § 11, TO5, TO9) : les copies de l'arène, une par match joué
/// en même temps, loin les unes des autres (rien ne s'entend ni ne se voit de l'une à l'autre : les tirs portent à 500 m),
/// et la salle d'attente, loin d'elles aussi, d'où l'on regarde les duels en cours sur ses écrans. Posé par Duel Arena >
/// Arène > Poser le site du tournoi (TournamentSiteBuilder) : on corrige le script, jamais la scène.
///
/// Lu par le serveur pour placer les joueurs (PlayerLocomotion.ServerMoveToSpawnPoint : les points d'apparition de l'arène,
/// décalés vers sa copie ; ceux de la salle d'attente), et par chaque machine pour les écrans (TournamentFeeds).
/// </summary>
[DisallowMultipleComponent]
public class TournamentSite : MonoBehaviour
{
    [Tooltip("Le décalage de chaque copie de l'arène depuis l'arène de la scène (la première : aucun). Une copie par match joué en même temps : trois à six joueurs.")]
    [SerializeField] private Vector3[] arenaOffsets = { Vector3.zero, new Vector3(1000f, 0f, 0f), new Vector3(-1000f, 0f, 0f) };

    [Tooltip("Les points d'apparition de la salle d'attente : la position donne les pieds, la rotation Y le regard (vers les écrans).")]
    [SerializeField] private Transform[] waitingSpawns = System.Array.Empty<Transform>();

    [Tooltip("La salle d'attente, dans le monde : qui s'y trouve regarde les écrans (TournamentFeeds les allume).")]
    [SerializeField] private Bounds waitingRoom = new Bounds(new Vector3(0f, 2.5f, -1000f), new Vector3(30f, 8f, 20f));

    [Tooltip("Les écrans de la salle d'attente, un par copie de l'arène, dans leur ordre.")]
    [SerializeField] private Renderer[] screens = System.Array.Empty<Renderer>();

    private static TournamentSite cached;

    /// <summary>Le site de la scène, ou null (le stand, le menu, une arène sans tournoi).</summary>
    public static TournamentSite Instance
    {
        get
        {
            if (cached == null) cached = FindAnyObjectByType<TournamentSite>();
            return cached;
        }
    }

    /// <summary>Les copies de l'arène, la première comprise.</summary>
    public int ArenaCount => arenaOffsets.Length;

    /// <summary>Le décalage d'une copie de l'arène depuis celle de la scène (aucun pour la première, ou un numéro inconnu).</summary>
    public Vector3 ArenaOffset(int arena) => arena >= 0 && arena < arenaOffsets.Length ? arenaOffsets[arena] : Vector3.zero;

    /// <summary>La salle d'attente, dans le monde.</summary>
    public Bounds WaitingRoom => waitingRoom;

    /// <summary>Les écrans de la salle d'attente, un par copie de l'arène.</summary>
    public IReadOnlyList<Renderer> Screens => screens;

    /// <summary>La copie de l'arène qui contient ce point (son numéro), ou -1 : la salle d'attente, ou ailleurs.</summary>
    public int ArenaAt(Vector3 position)
    {
        int best = -1;
        float bestDistance = 400f * 400f;
        for (int i = 0; i < arenaOffsets.Length; i++)
        {
            Vector3 d = position - arenaOffsets[i];
            float flat = d.x * d.x + d.z * d.z;
            if (flat < bestDistance)
            {
                bestDistance = flat;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Serveur : le point de la salle d'attente le plus éloigné de ceux qui y sont déjà (comme les points de
    /// l'arène). Faux s'il n'y en a pas.</summary>
    public bool TryGetWaitingSpawn(List<Vector3> occupied, out Vector3 position, out float yaw)
    {
        position = default;
        yaw = 0f;
        Transform best = null;
        float bestDistance = float.NegativeInfinity;
        foreach (Transform point in waitingSpawns)
        {
            if (point == null) continue;
            float nearest = float.PositiveInfinity;
            if (occupied != null)
            {
                foreach (Vector3 other in occupied) nearest = Mathf.Min(nearest, (point.position - other).sqrMagnitude);
            }
            if (nearest > bestDistance)
            {
                bestDistance = nearest;
                best = point;
            }
        }
        if (best == null) return false;
        position = best.position;
        yaw = best.eulerAngles.y;
        return true;
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Le constructeur (TournamentSiteBuilder) et les tests : les copies, les points, la salle et ses écrans.</summary>
    public void EditorSet(Vector3[] offsets, Transform[] spawns, Bounds room, Renderer[] screenRenderers)
    {
        arenaOffsets = offsets;
        waitingSpawns = spawns;
        waitingRoom = room;
        screens = screenRenderers;
    }

    public IReadOnlyList<Transform> WaitingSpawns => waitingSpawns;
#endif
}
