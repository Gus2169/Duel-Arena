using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Le site du Tournoi dans la scène de l'arène (2026-11-03, GDD § 11, TO5, TO9) : les copies de l'arène, une par match joué
/// en même temps, loin les unes des autres (rien ne s'entend ni ne se voit de l'une à l'autre : les tirs portent à 500 m),
/// et la salle d'attente, loin d'elles aussi, d'où l'on regarde les duels en cours sur ses écrans. Posé par Duel Arena >
/// Arène > Poser le site du tournoi (TournamentSiteBuilder) : on corrige le script, jamais la scène.
///
/// Lu par le serveur pour placer les joueurs (PlayerLocomotion.ServerMoveToSpawnPoint : les points d'apparition de l'arène,
/// décalés vers sa copie ; ceux de la salle d'attente), et par chaque machine pour les écrans (TournamentFeeds).
///
/// Il pèse deux fois l'arène : chaque caméra trierait ses rendus et ses lumières à chaque image, dans toutes les parties.
/// Chaque machine l'éteint donc au lancement et ne l'allume que pour le Tournoi (SetLive), d'après les réglages publiés, les
/// mêmes partout : le Move() d'un client y voit les colliders du serveur. La racine reste allumée : on la cherche
/// (Instance), et TournamentFeeds vit dessus. Les objets réseau de scène aussi, avec leurs parents : le serveur ne fait
/// apparaître que ceux qui sont actifs. 🚨 Seulement à l'exécution : éteint dans la scène, le site ne se cuirait plus, et
/// TournamentSiteTests ne verrait plus ses colliders.
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

    // Ce que SetLive allume et éteint, retenu à son premier appel.
    [System.NonSerialized] private List<GameObject> switchable;
    [System.NonSerialized] private bool live = true;

    /// <summary>Le site de la scène, ou null (le stand, le menu, une arène sans tournoi).</summary>
    public static TournamentSite Instance
    {
        get
        {
            if (cached == null) cached = FindAnyObjectByType<TournamentSite>();
            return cached;
        }
    }

    private void Awake()
    {
        // Le rechargement de scène est désactivé à l'entrée en Play Mode : ces champs survivent d'une session à l'autre.
        switchable = null;
        live = true;
        SetLive(false);
    }

    private void Update()
    {
        RoundManager rm = RoundManager.Instance;
        bool tournament = rm != null && rm.Mode == GameModeId.Tournament;
        bool wanted = WantedLive(tournament, live, live && !tournament && AnyRobotOutsideFirstArena());
        if (wanted != live) SetLive(wanted);
    }

    /// <summary>
    /// Le site doit-il être allumé ? Pour le Tournoi ; et, s'il l'est, tant qu'un robot n'a pas regagné l'arène de la scène :
    /// le tournoi fini, les joueurs attendent le match suivant dans la salle, et l'hôte peut changer de mode au salon sans que
    /// personne n'y soit replacé (`robotOutside`) : l'éteindre leur retirerait le sol. Fonction pure, testée.
    /// </summary>
    public static bool WantedLive(bool tournament, bool live, bool robotOutside) => tournament || (live && robotOutside);

    private bool AnyRobotOutsideFirstArena()
    {
        var players = PlayerLocomotion.SpawnedPlayers;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerLocomotion player = players[i];
            if (player != null && ArenaAt(player.transform.position) != 0) return true;
        }
        return false;
    }

    /// <summary>Allume ou éteint le site. Le serveur l'allume aussi au départ du tournoi, sans attendre son Update : les
    /// joueurs y partent à cette image même.</summary>
    public void SetLive(bool value)
    {
        if (value == live) return;
        live = value;
        switchable ??= Switchable(transform);
        foreach (GameObject part in switchable)
        {
            if (part != null) part.SetActive(value);
        }
        // Physics.autoSyncTransforms vaut false : les colliders rallumés doivent répondre tout de suite.
        Physics.SyncTransforms();
    }

    /// <summary>Ce qui s'éteint du site posé sous `root` : tout, sauf les objets réseau de scène et leurs parents. Les plus
    /// hauts objets qui n'en contiennent pas ; ce que la scène a déjà éteint reste à part. Fonction sans état, testée sur
    /// la scène.</summary>
    public static List<GameObject> Switchable(Transform root)
    {
        var result = new List<GameObject>();
        CollectSwitchable(root, result);
        return result;
    }

    private static void CollectSwitchable(Transform parent, List<GameObject> result)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (!child.gameObject.activeSelf || child.TryGetComponent(out NetworkObject _)) continue;
            if (child.GetComponentInChildren<NetworkObject>(true) != null) CollectSwitchable(child, result);
            else result.Add(child.gameObject);
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
