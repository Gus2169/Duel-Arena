using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Écran titre + navigation + écran « Jouer » (choix du mode, Héberger/Rejoindre) du menu
/// principal. Posé sur le même GameObject que l'UIDocument, dans la scène MainMenu — qui
/// remplace les boutons Host/Server/Client de NetworkBootstrapUI (voir CLAUDE.md « Le menu
/// principal »).
///
/// Les écrans Personnalisation et Options sont gérés par des composants séparés
/// (ArmoryController, OptionsController) sur le même GameObject : celui-ci ne fait QUE la
/// navigation entre écrans (via la classe USS "hidden") et la logique propre à l'écran Jouer.
///
/// Le NetworkManager vit sur un GameObject séparé de cette scène (persistant, voir
/// NetworkBootstrapUI) : Héberger démarre le host puis charge IMMÉDIATEMENT la scène de jeu en
/// mode Single — c'est le patron standard de Netcode pour une scène de bootstrap/menu (le
/// NetworkManager survit au changement de scène, lui, parce qu'il s'auto-applique
/// DontDestroyOnLoad tant qu'il est à la racine). Rejoindre se contente de démarrer le client :
/// la synchronisation de scène est automatique (EnableSceneManagement est actif sur le
/// NetworkManager), aucun appel à LoadScene n'est nécessaire côté client.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Tooltip("Nom de la scène de jeu à charger une fois l'hébergement démarré. Doit être dans les Build Settings.")]
    [SerializeField] private string gameplaySceneName = "MultiTestScene";

    [SerializeField] private UIDocument document;

    [Tooltip("Pour couper la caméra d'aperçu 3D quand on quitte l'écran Personnalisation (voir ShowScreenByName).")]
    [SerializeField] private CharacterPreviewController characterPreview;

    private static readonly string[] ScreenNames = { "screenTitle", "screenPlay", "screenArmory", "screenOptions", "screenWeaponCustomize" };

    private VisualElement root;
    private VisualElement screenTitle;
    private VisualElement screenPlay;

    private Button[] modeCards;
    private int activeModeCardIndex;

    private TextField hostPortField;
    private TextField joinIpField;
    private TextField joinPortField;
    private Button hostButton;
    private Button joinButton;
    private Label localIpLabel;
    private Label playStatusLabel;

    private bool joinAttemptInProgress;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        screenTitle = root.Q<VisualElement>("screenTitle");
        screenPlay = root.Q<VisualElement>("screenPlay");

        WireTitleScreen();
        WirePlayScreen();

        ShowScreenByName("screenTitle");

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

    // ------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------

    private void WireTitleScreen()
    {
        root.Q<Button>("playButton").clicked += () => ShowScreenByName("screenPlay");
        root.Q<Button>("armoryButton").clicked += () => ShowScreenByName("screenArmory");
        root.Q<Button>("optionsButton").clicked += () => ShowScreenByName("screenOptions");
        root.Q<Button>("quitButton").clicked += QuitGame;

        root.Q<Button>("playBackButton").clicked += () => ShowScreenByName("screenTitle");
        root.Q<Button>("armoryBackButton").clicked += () => ShowScreenByName("screenTitle");
        root.Q<Button>("optionsBackButton").clicked += () => ShowScreenByName("screenTitle");
    }

    /// <summary>Affiche l'écran demandé, masque les autres. Pas d'animation : un menu de duel n'a
    /// pas besoin de fioritures, et ça reste trivial à maintenir si un écran s'ajoute. Public : la
    /// fenêtre d'armurerie avancée (WeaponCustomizationController) revient à Personnalisation par
    /// ce même chemin, pas par un retour au titre.</summary>
    public void ShowScreenByName(string screenName)
    {
        foreach (string name in ScreenNames)
        {
            VisualElement s = root.Q<VisualElement>(name);
            if (s != null) s.EnableInClassList("hidden", name != screenName);
        }

        // La caméra d'aperçu du personnage ne tourne que pendant qu'on la regarde — coupée dès
        // qu'on quitte Personnalisation, y compris en passant par l'armurerie avancée (qui prend
        // elle-même la main sur le même PreviewStage, voir WeaponCustomizationController).
        if (characterPreview != null)
        {
            if (screenName == "screenArmory") characterPreview.Show();
            else if (screenName != "screenWeaponCustomize") characterPreview.Hide();
        }
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------
    // Écran Jouer : mode + Héberger/Rejoindre
    // ------------------------------------------------------------------

    private void WirePlayScreen()
    {
        modeCards = new[]
        {
            root.Q<Button>("modeBo5"),
            root.Q<Button>("modeRanked"),
            root.Q<Button>("modeCapture"),
            root.Q<Button>("modeZone"),
        };

        // Un seul mode existe pour l'instant (le BO5 de RoundManager) : les trois autres sont
        // visibles pour montrer où le jeu va, mais désactivés — pas des boutons morts qu'on
        // découvre en cliquant, un état lisible d'un coup d'œil (voir la classe .mode-card:disabled).
        for (int i = 1; i < modeCards.Length; i++)
        {
            modeCards[i].SetEnabled(false);
        }
        activeModeCardIndex = 0;
        modeCards[0].AddToClassList("mode-card--active");
        modeCards[0].clicked += () => SelectModeCard(0);

        hostPortField = root.Q<TextField>("hostPortField");
        joinIpField = root.Q<TextField>("joinIpField");
        joinPortField = root.Q<TextField>("joinPortField");
        hostButton = root.Q<Button>("hostButton");
        joinButton = root.Q<Button>("joinButton");
        localIpLabel = root.Q<Label>("localIpLabel");
        playStatusLabel = root.Q<Label>("playStatusLabel");

        hostButton.clicked += OnHostClicked;
        joinButton.clicked += OnJoinClicked;
    }

    /// <summary>Un seul mode est sélectionnable aujourd'hui, mais la sélection reste générique :
    /// le jour où un second mode existe, il suffit de le réactiver au-dessus.</summary>
    private void SelectModeCard(int index)
    {
        if (index == activeModeCardIndex) return;
        modeCards[activeModeCardIndex].RemoveFromClassList("mode-card--active");
        activeModeCardIndex = index;
        modeCards[activeModeCardIndex].AddToClassList("mode-card--active");
    }

    private void OnHostClicked()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) { SetStatus("NetworkManager introuvable dans la scène.", true); return; }
        if (nm.IsClient || nm.IsServer) { SetStatus("Déjà connecté.", true); return; }

        ushort port = ParsePort(hostPortField.value, 7777);
        UnityTransport transport = nm.GetComponent<UnityTransport>();
        if (transport == null) { SetStatus("UnityTransport introuvable.", true); return; }

        // Écoute sur toutes les interfaces (0.0.0.0) pour accepter un client du même réseau local,
        // pas seulement 127.0.0.1 : c'est ce qui permet de jouer entre amis avant que Relay existe.
        string localIp = GetLocalIPv4();
        transport.SetConnectionData(localIp, port, "0.0.0.0");

        if (!nm.StartHost())
        {
            SetStatus("Impossible de démarrer l'hébergement.", true);
            return;
        }

        SceneEventProgressStatus status = nm.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            SetStatus($"Hébergement démarré, mais le chargement de la scène a échoué ({status}).", true);
            return;
        }

        localIpLabel.text = $"Donne cette adresse à tes amis sur le même réseau : {localIp}:{port}";
        SetStatus("Hébergement en cours...", false);
    }

    private void OnJoinClicked()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) { SetStatus("NetworkManager introuvable dans la scène.", true); return; }

        if (joinAttemptInProgress)
        {
            // Le bouton sert de bascule pendant une tentative : un second clic annule.
            nm.Shutdown();
            EndJoinAttempt("Connexion annulée.", false);
            return;
        }

        if (nm.IsClient || nm.IsServer) { SetStatus("Déjà connecté.", true); return; }

        string ip = string.IsNullOrWhiteSpace(joinIpField.value) ? "127.0.0.1" : joinIpField.value.Trim();
        ushort port = ParsePort(joinPortField.value, 7777);

        UnityTransport transport = nm.GetComponent<UnityTransport>();
        if (transport == null) { SetStatus("UnityTransport introuvable.", true); return; }
        transport.SetConnectionData(ip, port);

        if (!nm.StartClient())
        {
            SetStatus("Impossible de démarrer la connexion.", true);
            return;
        }

        joinAttemptInProgress = true;
        joinButton.text = "Annuler";
        SetStatus($"Connexion à {ip}:{port}...", false);
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId && joinAttemptInProgress)
        {
            EndJoinAttempt("Connecté.", false);
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (joinAttemptInProgress)
        {
            EndJoinAttempt("Connexion échouée : vérifie l'adresse, le port, et que l'hôte a bien cliqué sur Héberger.", true);
        }
    }

    private void EndJoinAttempt(string message, bool isError)
    {
        joinAttemptInProgress = false;
        joinButton.text = "Rejoindre";
        SetStatus(message, isError);
    }

    private void SetStatus(string message, bool isError)
    {
        if (playStatusLabel == null) return;
        playStatusLabel.text = message;
        playStatusLabel.EnableInClassList("status-label--error", isError);
        playStatusLabel.EnableInClassList("status-label--ok", !isError && message == "Connecté.");
    }

    private static ushort ParsePort(string text, ushort fallback)
        => ushort.TryParse(text, out ushort value) && value > 0 ? value : fallback;

    private static string GetLocalIPv4()
    {
        try
        {
            foreach (IPAddress address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork) return address.ToString();
            }
        }
        catch (System.Exception)
        {
            // Pas de réseau résolvable (machine hors ligne, pare-feu) : on retombe sur la boucle
            // locale plutôt que de planter le menu pour un simple affichage informatif.
        }
        return "127.0.0.1";
    }
}
