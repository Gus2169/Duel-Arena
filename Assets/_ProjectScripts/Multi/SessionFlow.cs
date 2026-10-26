using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Les règles de la session, posées sur le GameObject du NetworkManager (scène MainMenu, qui
/// voyage d'une scène à l'autre avec lui). Ajouté à la relecture complète du 2026-10-12 :
///
/// - LA CAPACITÉ de la partie, réglée par l'hôte (2026-10-13 : quatre places par défaut, deux
///   duellistes et deux spectateurs, GDD § 4). Au-delà de deux, les joueurs attendent leur tour sur la
///   tribune (RoundManager, DuelRoster).
/// - LA MÊME VERSION des deux côtés (Application.version, « Version » des Player Settings) : un ami
///   resté sur une ancienne build est refusé avec un message clair, au lieu d'entrer dans une
///   partie dont le code diffère. Monter la version à chaque build envoyée.
/// - LE RETOUR AU MENU quand la session s'arrête : l'hôte quitte, la connexion tombe, ou le joueur
///   quitte depuis le menu d'Échap (PauseMenu). Avant, le client restait dans une arène vide, sans
///   robot ni caméra, curseur bloqué, jusqu'à Alt+F4.
///
/// - LE PSEUDO (2026-10-12) : le joueur l'annonce dans sa demande de connexion, avec sa version.
///   Le serveur le retient à l'approbation et le donne à sa fiche quand son robot apparaît
///   (PlayerMatchState), nettoyé et rendu unique. Aucune RPC de plus.
/// - L'APPARENCE DE SES ARMES (2026-10-15) : le modèle et la finition de chacune, choisis à
///   l'armurerie, par le même chemin (PlayerWeaponLooks les borne et les publie).
/// - SES ACCESSOIRES (2026-10-16) : le viseur, la poignée, le laser et le chargeur de chacune, par
///   le même chemin encore (PlayerWeaponAttachments les borne et les publie).
/// - LA COULEUR DE SON ROBOT (2026-10-16) : son rang dans la palette, choisi dans Personnalisation ;
///   PlayerPaint la donne si aucun autre joueur ne la porte, sinon la première libre.
///
/// Le refus est décidé par le serveur seul (ConnectionApprovalCallback) ; le client ne fait
/// qu'annoncer sa version et son pseudo.
/// </summary>
[DisallowMultipleComponent]
public class SessionFlow : MonoBehaviour
{
    /// <summary>Taille maximale d'une demande de connexion acceptée (octets) : une version et un
    /// pseudo en tiennent bien moins. Au-delà, elle ne vient que d'un client modifié.</summary>
    public const int MaxPayloadBytes = 256;

    // Côté serveur : le pseudo et l'apparence des armes annoncés par chaque client approuvé, en
    // attendant que son robot apparaisse. L'hôte passe par la même approbation (Netcode l'appelle
    // aussi pour lui).
    private static readonly Dictionary<ulong, string> approvedNames = new Dictionary<ulong, string>();
    private static readonly Dictionary<ulong, WeaponLooks> approvedLooks = new Dictionary<ulong, WeaponLooks>();
    private static readonly Dictionary<ulong, WeaponAttachments> approvedAttachments = new Dictionary<ulong, WeaponAttachments>();
    private static readonly Dictionary<ulong, int> approvedPaints = new Dictionary<ulong, int>();
    private static readonly Dictionary<ulong, int> approvedChassis = new Dictionary<ulong, int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        approvedNames.Clear();
        approvedLooks.Clear();
        approvedAttachments.Clear();
        approvedPaints.Clear();
        approvedChassis.Clear();
    }

    /// <summary>
    /// Les réglages de la partie que cette machine héberge : capacité, organisation des duels,
    /// interventions des spectateurs (2026-10-13). Choisis par l'hôte dans le salon (LobbyController), ou
    /// ceux de sa dernière partie en réseau local (GameSettings.HostRules), fixés au lancement, publiés par
    /// RoundManager.
    /// La capacité n'est jamais une constante (CLAUDE.md) : quatre places pour le prototype du GDD.
    /// </summary>
    public static MatchRules ActiveRules { get; set; } = MatchRules.Default;

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
        // sa version et son pseudo dans sa demande de connexion.
        approvedNames.Clear();
        approvedLooks.Clear();
        approvedAttachments.Clear();
        approvedPaints.Clear();
        approvedChassis.Clear();
        networkManager.NetworkConfig.ConnectionApproval = true;
        PrepareConnectionData(networkManager);
        networkManager.ConnectionApprovalCallback = ApproveConnection;
    }

    /// <summary>La demande de connexion de cette machine : sa version, le pseudo des Options, l'apparence de
    /// ses armes, sa couleur et son châssis. À refaire juste avant de démarrer : ils ont pu changer depuis le
    /// lancement.</summary>
    public static void PrepareConnectionData(NetworkManager nm)
    {
        if (nm != null)
            nm.NetworkConfig.ConnectionData = BuildPayload(Application.version, GameSettings.PlayerName, GameSettings.WeaponLooks,
                                                           GameSettings.WeaponAttachments, GameSettings.PreferredColorIndex,
                                                           GameSettings.PreferredChassis);
    }

    /// <summary>Le pseudo annoncé par `clientId` à son approbation, retiré de l'attente (vide s'il
    /// n'en a pas annoncé, ou hors d'une session lancée par le menu).</summary>
    public static string TakeApprovedName(ulong clientId)
    {
        if (!approvedNames.TryGetValue(clientId, out string name)) return "";
        approvedNames.Remove(clientId);
        return name;
    }

    /// <summary>L'apparence des armes annoncée par `clientId` à son approbation, retirée de l'attente
    /// (les armes par défaut s'il n'en a pas annoncé). Pas encore bornée : PlayerWeaponLooks s'en charge,
    /// lui qui connaît la panoplie.</summary>
    public static WeaponLooks TakeApprovedLooks(ulong clientId)
    {
        if (!approvedLooks.TryGetValue(clientId, out WeaponLooks looks)) return WeaponLooks.Default;
        approvedLooks.Remove(clientId);
        return looks;
    }

    /// <summary>Les accessoires annoncés par `clientId` à son approbation, retirés de l'attente (aucun
    /// s'il n'en a pas annoncé). Pas encore bornés : PlayerWeaponAttachments s'en charge.</summary>
    public static WeaponAttachments TakeApprovedAttachments(ulong clientId)
    {
        if (!approvedAttachments.TryGetValue(clientId, out WeaponAttachments attachments)) return WeaponAttachments.None;
        approvedAttachments.Remove(clientId);
        return attachments;
    }

    /// <summary>La couleur choisie par `clientId` (son rang dans la palette), retirée de l'attente : -1
    /// s'il n'en a pas annoncé. Pas encore bornée : PlayerPaint s'en charge, lui qui connaît la palette et
    /// les couleurs déjà portées.</summary>
    public static int TakeApprovedPaint(ulong clientId)
    {
        if (!approvedPaints.TryGetValue(clientId, out int paint)) return -1;
        approvedPaints.Remove(clientId);
        return paint;
    }

    /// <summary>Le châssis choisi par `clientId` (son rang, ChassisId), retiré de l'attente : -1 s'il n'en a pas
    /// annoncé. Pas encore borné : PlayerChassis s'en charge.</summary>
    public static int TakeApprovedChassis(ulong clientId)
    {
        if (!approvedChassis.TryGetValue(clientId, out int chassis)) return -1;
        approvedChassis.Remove(clientId);
        return chassis;
    }

    /// <summary>La demande de connexion : la version, le pseudo, puis l'apparence des armes et leurs
    /// accessoires (2026-10-16) en hexadécimal, le rang de la couleur choisie (-1 : aucune) et celui du châssis
    /// (2026-10-26), séparés par des sauts de ligne (que le pseudo, nettoyé, ne peut pas contenir). Fonction pure,
    /// testée.</summary>
    public static byte[] BuildPayload(string version, string name, WeaponLooks looks, WeaponAttachments attachments, int paint,
                                      ChassisId chassis)
        => Encoding.UTF8.GetBytes((version ?? "") + "\n" + PlayerMatchState.SanitizeName(name) + "\n" + looks.Packed.ToString("X8")
                                  + "\n" + attachments.Packed.ToString("X8")
                                  + "\n" + paint.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                  + "\n" + ((int)chassis).ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static byte[] BuildPayload(string version, string name, WeaponLooks looks, WeaponAttachments attachments, int paint)
        => BuildPayload(version, name, looks, attachments, paint, ChassisId.CT01);

    public static byte[] BuildPayload(string version, string name, WeaponLooks looks, WeaponAttachments attachments)
        => BuildPayload(version, name, looks, attachments, -1);

    public static byte[] BuildPayload(string version, string name, WeaponLooks looks)
        => BuildPayload(version, name, looks, WeaponAttachments.None);

    public static byte[] BuildPayload(string version, string name) => BuildPayload(version, name, WeaponLooks.Default);

    /// <summary>Lit une demande de connexion. Faux si elle est absente ou trop longue : elle ne vient
    /// alors que d'un client modifié. Une ancienne build n'envoie que sa version, ou sa version et son
    /// pseudo : ce qui manque ne vaut rien (pas de pseudo, les armes par défaut, aucune couleur ni aucun
    /// châssis choisis), comme une valeur illisible. Fonction pure, testée.</summary>
    public static bool ParsePayload(byte[] payload, out string version, out string name, out WeaponLooks looks,
                                    out WeaponAttachments attachments, out int paint, out int chassis)
    {
        version = "";
        name = "";
        looks = WeaponLooks.Default;
        attachments = WeaponAttachments.None;
        paint = -1;
        chassis = -1;
        if (payload == null || payload.Length == 0 || payload.Length > MaxPayloadBytes) return false;
        string[] lines = Encoding.UTF8.GetString(payload).Split('\n');
        version = lines[0];
        if (lines.Length > 1) name = PlayerMatchState.SanitizeName(lines[1]);
        if (lines.Length > 2 && uint.TryParse(lines[2], System.Globalization.NumberStyles.AllowHexSpecifier,
                                               System.Globalization.CultureInfo.InvariantCulture, out uint packed))
            looks = new WeaponLooks(packed);
        if (lines.Length > 3 && uint.TryParse(lines[3], System.Globalization.NumberStyles.AllowHexSpecifier,
                                               System.Globalization.CultureInfo.InvariantCulture, out uint chosen))
            attachments = new WeaponAttachments(chosen);
        if (lines.Length > 4 && int.TryParse(lines[4], System.Globalization.NumberStyles.AllowLeadingSign,
                                              System.Globalization.CultureInfo.InvariantCulture, out int color))
            paint = color;
        if (lines.Length > 5 && int.TryParse(lines[5], System.Globalization.NumberStyles.AllowLeadingSign,
                                              System.Globalization.CultureInfo.InvariantCulture, out int body))
            chassis = body;
        return true;
    }

    public static bool ParsePayload(byte[] payload, out string version, out string name, out WeaponLooks looks,
                                    out WeaponAttachments attachments, out int paint)
        => ParsePayload(payload, out version, out name, out looks, out attachments, out paint, out _);

    public static bool ParsePayload(byte[] payload, out string version, out string name, out WeaponLooks looks,
                                    out WeaponAttachments attachments)
        => ParsePayload(payload, out version, out name, out looks, out attachments, out _);

    public static bool ParsePayload(byte[] payload, out string version, out string name, out WeaponLooks looks)
        => ParsePayload(payload, out version, out name, out looks, out _);

    public static bool ParsePayload(byte[] payload, out string version, out string name)
        => ParsePayload(payload, out version, out name, out _);

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
        string reason;
        if (!ParsePayload(request.Payload, out string clientVersion, out string clientName, out WeaponLooks clientLooks,
                          out WeaponAttachments clientAttachments, out int clientPaint, out int clientChassis))
        {
            response.Approved = false;
            reason = "Demande de connexion invalide.";
        }
        else
        {
            response.Approved = ShouldApprove(networkManager.ConnectedClientsIds.Count, ActiveRules.Sanitized().capacity,
                                              clientVersion, Application.version, out reason);
        }
        response.Reason = reason;
        response.CreatePlayerObject = response.Approved;
        if (response.Approved)
        {
            approvedNames[request.ClientNetworkId] = clientName;
            approvedLooks[request.ClientNetworkId] = clientLooks;
            approvedAttachments[request.ClientNetworkId] = clientAttachments;
            approvedPaints[request.ClientNetworkId] = clientPaint;
            approvedChassis[request.ClientNetworkId] = clientChassis;
        }
        else Debug.Log($"[Session] Connexion refusée : {reason}");

        // Un joueur qui arrive pendant que l'hôte charge encore l'arène (vécu au banc à deux instances,
        // 2026-10-13 : le client réessayait déjà quand l'hôte a démarré) naissait dans la scène du menu,
        // et son robot disparaissait avec elle : connecté, mais sans corps. Sa demande attend que l'arène
        // soit chargée (Netcode garde la réponse en attente tant que Pending est vrai). L'hôte lui-même
        // est accepté à son démarrage, avant le chargement : il n'est pas concerné.
        if (response.Approved && request.ClientNetworkId != networkManager.LocalClientId && SceneManager.GetActiveScene().name == MenuSceneName)
        {
            response.Pending = true;
            pendingApprovals.Add(response);
        }
    }

    // Les connexions acceptées qui attendent que l'hôte soit dans l'arène (voir ApproveConnection).
    private readonly List<NetworkManager.ConnectionApprovalResponse> pendingApprovals = new List<NetworkManager.ConnectionApprovalResponse>();

    private void Update()
    {
        if (pendingApprovals.Count == 0 || SceneManager.GetActiveScene().name == MenuSceneName) return;
        foreach (NetworkManager.ConnectionApprovalResponse response in pendingApprovals) response.Pending = false;
        pendingApprovals.Clear();
    }

    /// <summary>
    /// Accepte-t-on un joueur de plus ? Refusé si la partie est pleine (`capacity` joueurs, hôte compris)
    /// ou si sa version diffère de celle de l'hôte, avec la raison à lui montrer. Au-delà de deux, les
    /// joueurs attendent leur tour sur la tribune (GDD § 4). Fonction pure, testée.
    /// </summary>
    public static bool ShouldApprove(int connectedPlayers, int capacity, string clientVersion, string hostVersion, out string reason)
    {
        if (connectedPlayers >= capacity)
        {
            reason = $"La partie est pleine : {capacity} joueurs au plus.";
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
        // Départ volontaire (menu d'Échap, écran de fin) : le retour est déjà en route, sans message.
        if (returningToMenu) return;
        ReturnToMenu(MenuMessageFor(networkManager.DisconnectReason, wasHost));
    }

    /// <summary>
    /// Ce qu'on dit au joueur ramené au menu par la fin de sa session. Netcode donne ses raisons en
    /// anglais, parfois techniques (« [Disconnect Event]... the transport was shutdown ») : on les
    /// traduit ; les refus de l'hôte (ShouldApprove), déjà en français, passent tels quels. Fonction
    /// pure, testée. Corrigé le 2026-10-12 : l'hôte qui quittait de lui-même voyait la raison
    /// technique de son propre arrêt, en rouge, et le client un message anglais.
    /// </summary>
    public static string MenuMessageFor(string disconnectReason, bool wasHost)
    {
        string reason = disconnectReason ?? "";
        if (reason.IndexOf("host shutting down", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return "L'hôte a quitté la partie.";
        if (reason.Length == 0 || reason.StartsWith("[Disconnect Event]", System.StringComparison.Ordinal))
            return wasHost ? "" : "La connexion avec l'hôte a été perdue, ou il a quitté la partie.";
        return reason;
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
            _ = OnlineSession.LeaveAsync();
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
    ///
    /// En ligne (2026-10-13), on quitte d'abord le salon (OnlineSession) : c'est lui qui arrête Netcode
    /// (l'hôte le ferme, et tout le monde en sort), et son pilote doit être libéré avant que le
    /// NetworkManager ne disparaisse. Le service peut tarder : on ne l'attend que quelques secondes.
    /// </summary>
    private IEnumerator LeaveSession()
    {
        if (OnlineSession.InSession)
        {
            Task leaving = OnlineSession.LeaveAsync();
            float leaveDeadline = Time.realtimeSinceStartup + 4f;
            while (!leaving.IsCompleted && Time.realtimeSinceStartup < leaveDeadline) yield return null;
        }

        if (networkManager.IsListening && !networkManager.ShutdownInProgress) networkManager.Shutdown();
        float deadline = Time.realtimeSinceStartup + 2f;
        while ((networkManager.IsListening || networkManager.ShutdownInProgress) && Time.realtimeSinceStartup < deadline)
            yield return null;

        Destroy(networkManager.gameObject);
        SceneManager.LoadSceneAsync(MenuSceneName);
    }
}
