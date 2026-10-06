using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Boutons de test temporaires pour valider une connexion réseau, avant toute vraie UI de lobby.
/// Posé sur le MÊME GameObject que le NetworkManager.
///
/// Affiche 3 boutons (Host / Server / Client) tant qu'aucune connexion n'est active, puis affiche
/// le rôle courant et le nombre de clients connectés une fois connecté. Logue aussi dans la
/// Console à chaque connexion/déconnexion, pour confirmer sans ambiguïté que ça fonctionne.
///
/// À désactiver/supprimer une fois qu'un vrai flow de connexion (lobby, Relay) existera.
///
/// Gère aussi la CAMÉRA DE SECOURS de la scène : celle qui affiche quelque chose tant qu'aucun
/// joueur n'existe (écran des boutons Host/Server/Client). Elle DOIT être éteinte dès qu'une
/// session démarre, sinon elle continue de rendre toute la scène en parallèle de la caméra du
/// joueur — deux caméras plein écran à la même depth, donc la scène dessinée DEUX FOIS par frame,
/// et un AudioListener de trop. PlayerLocomotion ne peut pas s'en charger : sa boucle de garde ne
/// voit que les enfants du Player, et cette caméra n'en est pas un.
/// </summary>
public class NetworkBootstrapUI : MonoBehaviour
{
    [Tooltip("Caméra affichée tant qu'aucune session réseau n'est démarrée (l'écran des boutons). Éteinte automatiquement dès qu'une session démarre. Si laissé vide, Camera.main est utilisée au démarrage — assigne-la explicitement si ta scène a plusieurs caméras hors joueur. À ASSIGNER EN MODE ÉDITION : une assignation faite pendant le Play Mode n'est jamais sauvegardée.")]
    [SerializeField] private Camera bootstrapCamera;

    private AudioListener bootstrapListener;
    private bool bootstrapCameraDisabled;

    private void Awake()
    {
        // Repli automatique : évite que le fix dépende silencieusement d'une case d'inspecteur
        // qu'on peut oublier de remplir (ou perdre en la remplissant pendant le Play Mode).
        if (bootstrapCamera == null) bootstrapCamera = Camera.main;
        if (bootstrapCamera != null) bootstrapListener = bootstrapCamera.GetComponent<AudioListener>();
    }

#if UNITY_EDITOR
    // ------------------------------------------------------------------
    // BANC DE TEST À DEUX INSTANCES — Editor uniquement, jamais embarqué en build.
    //
    // L'instance virtuelle de Multiplayer Play Mode ne se pilote pas depuis l'Editor principal :
    // sans ces deux interrupteurs, observer le côté CLIENT exige quelqu'un pour cliquer « Client »
    // et pour bouger. Ils sont lus dans les EditorPrefs, que les deux processus partagent (ils
    // vivent dans le registre de l'utilisateur, pas dans le projet) : l'Editor principal les
    // allume, le clone les lit au démarrage. Éteints par défaut : le flux normal ne change pas.
    // ------------------------------------------------------------------
    public const string CloneAutoClientPref = "DuelArena.Test.CloneAutoClient";
    public const string CloneAutopilotPref = "DuelArena.Test.CloneAutopilot";
    public const string CloneAutofirePref = "DuelArena.Test.CloneAutofire";

    private void Start()
    {
        if (!IsVirtualPlayerClone()) return;

        // Affecté dans les DEUX sens : le rechargement de domaine est désactivé à l'entrée en Play
        // Mode dans ce projet, donc un static allumé lors d'une session y resterait à la suivante.
        PlayerInputReader.AutopilotStrafe = UnityEditor.EditorPrefs.GetBool(CloneAutopilotPref, false);
        bool autofire = UnityEditor.EditorPrefs.GetBool(CloneAutofirePref, false);
        PlayerInputReader.AutopilotFire = autofire;
        WeaponController.AutopilotAimAtOpponent = autofire;

        NetworkManager nm = NetworkManager.Singleton;
        if (UnityEditor.EditorPrefs.GetBool(CloneAutoClientPref, false) && nm != null && !nm.IsClient && !nm.IsServer)
        {
            // Le Host n'existe peut-être pas encore : UnityTransport réessaie de se connecter
            // pendant une minute, ce qui laisse le temps de le démarrer dans l'Editor principal.
            Debug.Log("[Test] Instance virtuelle : connexion automatique en client.");
            nm.StartClient();
        }
    }

    private static bool IsVirtualPlayerClone()
        => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--virtual-project-clone") >= 0;
#endif

    private void Update()
    {
        // Sondé dans Update plutôt que sur un callback de connexion : StartHost/StartServer/
        // StartClient peuvent être appelés depuis ailleurs que les boutons ci-dessous (script de
        // test, futur lobby), et on veut que la caméra s'éteigne dans tous les cas.
        if (bootstrapCameraDisabled || bootstrapCamera == null) return;
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer) return;

        bootstrapCamera.enabled = false;
        if (bootstrapListener != null) bootstrapListener.enabled = false;
        bootstrapCameraDisabled = true;
    }

    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        Debug.Log($"[Netcode] Client connecté : id={clientId}.");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        Debug.Log($"[Netcode] Client déconnecté : id={clientId}.");
    }

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null)
        {
            GUI.Label(new Rect(10, 10, 420, 30), "NetworkManager.Singleton est null — vérifie qu'il y a bien un NetworkManager actif dans cette scène.");
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 240, 160));

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Host")) NetworkManager.Singleton.StartHost();
            if (GUILayout.Button("Server")) NetworkManager.Singleton.StartServer();
            if (GUILayout.Button("Client")) NetworkManager.Singleton.StartClient();
        }
        else
        {
            string role = NetworkManager.Singleton.IsHost ? "Host" : (NetworkManager.Singleton.IsServer ? "Server" : "Client");
            GUILayout.Label($"Connecté en tant que : {role}");
            GUILayout.Label($"Clients connectés : {NetworkManager.Singleton.ConnectedClients.Count}");
        }

        GUILayout.EndArea();
    }
}
