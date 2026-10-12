using System.Collections;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Les règles de la session, posées sur le GameObject du NetworkManager (scène MainMenu, qui
/// voyage d'une scène à l'autre avec lui). Ajouté à la relecture complète du 2026-10-12 :
///
/// - DEUX JOUEURS AU PLUS tant que la tribune n'existe pas. RoundManager suppose un duel : un
///   troisième client recevait un robot armé, et deux joueurs marquaient à chaque mort.
/// - LA MÊME VERSION des deux côtés (Application.version, « Version » des Player Settings) : un ami
///   resté sur une ancienne build est refusé avec un message clair, au lieu d'entrer dans une
///   partie dont le code diffère. Monter la version à chaque build envoyée.
/// - LE RETOUR AU MENU quand la session s'arrête : l'hôte quitte, la connexion tombe, ou le joueur
///   quitte depuis le menu d'Échap (PauseMenu). Avant, le client restait dans une arène vide, sans
///   robot ni caméra, curseur bloqué, jusqu'à Alt+F4.
///
/// Le refus est décidé par le serveur seul (ConnectionApprovalCallback) ; le client ne fait
/// qu'annoncer sa version.
/// </summary>
[DisallowMultipleComponent]
public class SessionFlow : MonoBehaviour
{
    /// <summary>Joueurs dans une partie, hôte compris : un duel, en attendant la tribune (GDD § 4).
    /// À faire passer à la capacité du mode quand les spectateurs existeront.</summary>
    public const int MaxPlayers = 2;

    /// <summary>La scène du menu, où l'on revient quand la session s'arrête (index 0 des Build
    /// Settings). Elle apporte son propre NetworkManager.</summary>
    public const string MenuSceneName = "MainMenu";

    /// <summary>Message à montrer à l'arrivée dans le menu (pourquoi on y revient), vide sinon.
    /// MainMenuController le lit et le vide.</summary>
    public static string PendingMenuMessage { get; set; } = "";

    private NetworkManager networkManager;
    private bool returningToMenu;

    private void Awake()
    {
        networkManager = GetComponent<NetworkManager>();
        if (networkManager == null)
        {
            Debug.LogError("SessionFlow doit être posé sur le GameObject du NetworkManager.", this);
            return;
        }

        // Réglé ici, sur toutes les machines : le serveur juge chaque arrivée, et le client annonce
        // sa version dans sa demande de connexion.
        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Application.version);
        networkManager.ConnectionApprovalCallback = ApproveConnection;
    }

    private void OnEnable()
    {
        if (networkManager != null) networkManager.OnClientStopped += HandleClientStopped;
    }

    // Filet de sécurité au retour dans le menu : si l'ancien NetworkManager était encore là quand
    // celui-ci s'est activé, il n'a pas pris la place de singleton, et l'ancien l'a remise à null en
    // partant (vécu en le testant le 2026-10-12). Le menu ne trouvait alors plus aucun NetworkManager.
    private void Start()
    {
        if (networkManager != null && NetworkManager.Singleton == null) networkManager.SetSingleton();
    }

    private void OnDisable()
    {
        if (networkManager != null) networkManager.OnClientStopped -= HandleClientStopped;
    }

    private void OnDestroy()
    {
        if (networkManager != null && networkManager.ConnectionApprovalCallback == ApproveConnection)
            networkManager.ConnectionApprovalCallback = null;
    }

    private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
                                   NetworkManager.ConnectionApprovalResponse response)
    {
        string clientVersion = request.Payload != null ? Encoding.UTF8.GetString(request.Payload) : "";
        response.Approved = ShouldApprove(networkManager.ConnectedClientsIds.Count, clientVersion,
                                          Application.version, out string reason);
        response.Reason = reason;
        response.CreatePlayerObject = response.Approved;
        if (!response.Approved) Debug.Log($"[Session] Connexion refusée : {reason}");
    }

    /// <summary>
    /// Accepte-t-on un joueur de plus ? Refusé si la partie est pleine ou si sa version diffère de
    /// celle de l'hôte, avec la raison à lui montrer. Fonction pure, testée.
    /// </summary>
    public static bool ShouldApprove(int connectedPlayers, string clientVersion, string hostVersion, out string reason)
    {
        if (connectedPlayers >= MaxPlayers)
        {
            reason = $"La partie est pleine : {MaxPlayers} joueurs au plus pour l'instant.";
            return false;
        }
        if (clientVersion != hostVersion)
        {
            reason = $"Versions différentes : l'hôte joue la {hostVersion}, toi la {clientVersion}. Il faut la même build.";
            return false;
        }
        reason = "";
        return true;
    }

    /// <summary>
    /// La session de cette machine vient de s'arrêter (hôte ou client). Depuis l'arène, on revient au
    /// menu ; depuis le menu (une connexion qui échoue), MainMenuController affiche déjà la raison.
    /// </summary>
    private void HandleClientStopped(bool wasHost)
    {
        if (SceneManager.GetActiveScene().name == MenuSceneName) return;

        string reason = networkManager.DisconnectReason;
        if (string.IsNullOrEmpty(reason) && !wasHost && !returningToMenu)
            reason = "La connexion avec l'hôte a été perdue, ou il a quitté la partie.";
        ReturnToMenu(reason);
    }

    /// <summary>
    /// Quitte la partie et revient au menu. La session s'arrête d'abord proprement, puis le
    /// NetworkManager qui voyageait avec le joueur est détruit : la scène du menu apporte le sien, et
    /// deux NetworkManager en même temps ne se départagent pas seuls (voir LeaveSession).
    /// </summary>
    public static void ReturnToMenu(string message)
    {
        PendingMenuMessage = message ?? "";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        NetworkManager nm = NetworkManager.Singleton;
        SessionFlow flow = nm != null ? nm.GetComponent<SessionFlow>() : null;
        if (flow == null)
        {
            if (nm != null) Destroy(nm.gameObject);
            SceneManager.LoadSceneAsync(MenuSceneName);
            return;
        }

        if (flow.returningToMenu) return;
        flow.returningToMenu = true;
        Debug.Log($"[Session] Retour au menu{(string.IsNullOrEmpty(message) ? "" : " : " + message)}");
        flow.StartCoroutine(flow.LeaveSession());
    }

    /// <summary>
    /// Arrête la session par NetworkManager.Shutdown, qui prévient les autres joueurs tout de suite :
    /// détruire directement le NetworkManager coupait le transport sans les prévenir (Netcode
    /// l'avertit), et le client n'apprenait le départ de l'hôte qu'au bout du délai de déconnexion,
    /// 30 s. Puis détruit ce NetworkManager et charge le menu de façon asynchrone, donc au plus tôt à
    /// l'image suivante : l'ancien est alors détruit, et celui du menu devient le singleton en
    /// s'activant.
    /// </summary>
    private IEnumerator LeaveSession()
    {
        if (networkManager.IsListening && !networkManager.ShutdownInProgress) networkManager.Shutdown();
        float deadline = Time.realtimeSinceStartup + 2f;
        while ((networkManager.IsListening || networkManager.ShutdownInProgress) && Time.realtimeSinceStartup < deadline)
            yield return null;

        Destroy(networkManager.gameObject);
        SceneManager.LoadSceneAsync(MenuSceneName);
    }
}
