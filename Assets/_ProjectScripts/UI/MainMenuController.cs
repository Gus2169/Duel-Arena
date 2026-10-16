using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Écran titre + navigation + écran « Jouer » du menu principal. Posé sur le même GameObject que
/// l'UIDocument, dans la scène MainMenu (voir CLAUDE.md « Le menu principal et la session »).
///
/// L'écran Jouer (refait le 2026-10-13, demandé par l'utilisateur) : le pseudo, puis CRÉER une partie (un
/// salon privé et son code) ou la REJOINDRE en tapant son code. Le salon lui-même, avec le mode et les
/// réglages de la partie, est LobbyController ; la partie en ligne, OnlineSession. Le réseau local par
/// adresse IP reste, replié en bas de l'écran : pour jouer sans internet, et pour le banc de test à deux
/// instances (son clone se connecte à 127.0.0.1:7777).
///
/// Les écrans Personnalisation et Options sont gérés par des composants séparés (ArmoryController,
/// OptionsController) sur le même GameObject : celui-ci ne fait que la navigation entre écrans (via la
/// classe USS "hidden") et l'écran Jouer.
///
/// Réseau local : Héberger démarre le host puis charge IMMÉDIATEMENT la scène de jeu en mode Single — le
/// patron standard de Netcode pour une scène de menu (le NetworkManager survit au changement de scène,
/// parce qu'il s'applique DontDestroyOnLoad tant qu'il est à la racine). Rejoindre démarre le client : la
/// synchronisation de scène est automatique (EnableSceneManagement).
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Tooltip("Nom de la scène de jeu à charger une fois l'hébergement démarré. Doit être dans les Build Settings.")]
    [SerializeField] private string gameplaySceneName = "MultiTestScene";

    [SerializeField] private UIDocument document;

    [Tooltip("Pour couper la caméra d'aperçu 3D quand on quitte l'écran Personnalisation (voir ShowScreenByName).")]
    [SerializeField] private CharacterPreviewController characterPreview;

    [Tooltip("Le salon des parties en ligne (sur le même GameObject si vide).")]
    [SerializeField] private LobbyController lobby;

    [Tooltip("Personnalisation : le robot et les armes (sur le même GameObject si vide).")]
    [SerializeField] private ArmoryController armory;

    [Tooltip("L'armurerie (sur le même GameObject si vide).")]
    [SerializeField] private WeaponCustomizationController weaponCustomization;

    private static readonly string[] ScreenNames = { "screenTitle", "screenPlay", "screenLobby", "screenArmory", "screenOptions", "screenWeaponCustomize" };

    private VisualElement root;

    private TextField joinCodeField;
    private Button createButton;
    private Button joinCodeButton;

    private TextField hostPortField;
    private TextField joinIpField;
    private TextField joinPortField;
    private Button hostButton;
    private Button joinButton;
    private Label localIpLabel;
    private Label lanRulesLabel;
    private Label playStatusLabel;

    private bool joinAttemptInProgress;
    private bool onlineBusy;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        if (lobby == null) lobby = GetComponent<LobbyController>();
        if (armory == null) armory = GetComponent<ArmoryController>();
        if (weaponCustomization == null) weaponCustomization = GetComponent<WeaponCustomizationController>();
        root = document.rootVisualElement;

        WireTitleScreen();
        WirePlayScreen();

        ShowScreenByName("screenTitle");

        // De retour d'une partie (l'hôte est parti, la connexion est tombée...) : l'écran Jouer,
        // avec la raison (SessionFlow.ReturnToMenu).
        if (!string.IsNullOrEmpty(SessionFlow.PendingMenuMessage))
        {
            ShowScreenByName("screenPlay");
            SetStatus(SessionFlow.PendingMenuMessage, true);
            SessionFlow.PendingMenuMessage = "";
        }
    }

    // Abonnement fait au Start, une fois tous les objets de la scène réveillés : dans OnEnable, le
    // NetworkManager de la scène pouvait ne pas encore être le singleton (ordre d'activation non
    // garanti, et au retour d'une partie l'ancien vient à peine d'être détruit, 2026-10-12).
    private NetworkManager subscribedManager;

    private void Start()
    {
        subscribedManager = NetworkManager.Singleton != null ? NetworkManager.Singleton : FindAnyObjectByType<NetworkManager>();
        if (subscribedManager == null) return;
        subscribedManager.OnClientConnectedCallback += HandleClientConnected;
        subscribedManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void OnDestroy()
    {
        if (subscribedManager == null) return;
        subscribedManager.OnClientConnectedCallback -= HandleClientConnected;
        subscribedManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        subscribedManager = null;
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
    /// l'armurerie (WeaponCustomizationController) revient à Personnalisation par
    /// ce même chemin, et le salon (LobbyController) s'ouvre et se referme par lui.</summary>
    public void ShowScreenByName(string screenName)
    {
        foreach (string name in ScreenNames)
        {
            VisualElement s = root.Q<VisualElement>(name);
            if (s != null) s.EnableInClassList("hidden", name != screenName);
        }

        // La caméra d'aperçu du personnage ne tourne que pendant qu'on la regarde — coupée dès
        // qu'on quitte Personnalisation, y compris en passant par l'armurerie (qui prend
        // elle-même la main sur le même PreviewStage, voir WeaponCustomizationController).
        if (characterPreview != null)
        {
            if (screenName == "screenArmory") characterPreview.Show();
            else if (screenName != "screenWeaponCustomize") characterPreview.Hide();
        }

        if (screenName == "screenArmory" && armory != null) armory.Refresh();
        if (screenName == "screenPlay") OnPlayScreenShown();
    }

    private bool IsShown(string screenName)
    {
        VisualElement screen = root.Q<VisualElement>(screenName);
        return screen != null && !screen.ClassListContains("hidden");
    }

    /// <summary>
    /// Échap revient d'un écran de Personnalisation : de l'armurerie à Personnalisation, de
    /// Personnalisation à l'écran titre. Tenu ici seul : chaque écran qui la lirait de son côté ferait
    /// revenir de deux écrans d'un coup (le second voit l'appui dans la même image).
    /// </summary>
    private void Update()
    {
        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) GoBack();
    }

    /// <summary>Le retour d'Échap : d'un seul écran, depuis l'armurerie ou Personnalisation.</summary>
    private void GoBack()
    {
        if (root == null) return;
        if (IsShown("screenWeaponCustomize") && weaponCustomization != null) weaponCustomization.Close();
        else if (IsShown("screenArmory")) ShowScreenByName("screenTitle");
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
    // Écran Jouer
    // ------------------------------------------------------------------

    private void WirePlayScreen()
    {
        // Le pseudo (2026-10-12), retenu d'une partie à l'autre, annoncé au salon et à l'hôte.
        TextField pseudoField = root.Q<TextField>("pseudoField");
        if (pseudoField != null)
        {
            pseudoField.value = GameSettings.PlayerName;
            pseudoField.RegisterValueChangedCallback(evt => GameSettings.PlayerName = evt.newValue);
        }

        createButton = root.Q<Button>("createButton");
        joinCodeButton = root.Q<Button>("joinCodeButton");
        joinCodeField = root.Q<TextField>("joinCodeField");
        createButton.clicked += () => lobby.Create();
        joinCodeButton.clicked += () => lobby.Join(joinCodeField.value);

        // Le code s'écrit comme le service l'attend (majuscules, lettres et chiffres), et Entrée rejoint.
        joinCodeField.RegisterValueChangedCallback(evt =>
        {
            string normalized = OnlineSession.NormalizeCode(evt.newValue);
            if (normalized != evt.newValue) joinCodeField.SetValueWithoutNotify(normalized);
        });
        joinCodeField.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
            lobby.Join(joinCodeField.value);
        }, TrickleDown.TrickleDown);

        hostPortField = root.Q<TextField>("hostPortField");
        joinIpField = root.Q<TextField>("joinIpField");
        joinPortField = root.Q<TextField>("joinPortField");
        hostButton = root.Q<Button>("hostButton");
        joinButton = root.Q<Button>("joinButton");
        localIpLabel = root.Q<Label>("localIpLabel");
        lanRulesLabel = root.Q<Label>("lanRulesLabel");
        playStatusLabel = root.Q<Label>("playStatusLabel");

        hostButton.clicked += OnHostClicked;
        joinButton.clicked += OnJoinClicked;
    }

    private void OnPlayScreenShown()
    {
        if (lanRulesLabel != null) lanRulesLabel.text = "Réglages : ceux de ta dernière partie créée. " + LobbyController.DescribeRules(GameSettings.HostRules);

        // Les services en ligne se préparent pendant que le joueur lit l'écran : créer ou rejoindre
        // part ensuite sans attendre la connexion. Un échec (pas d'internet) se dira au clic.
        _ = WarmUpOnlineAsync();
    }

    private static async System.Threading.Tasks.Task WarmUpOnlineAsync()
    {
        try { await OnlineSession.EnsureSignedInAsync(); }
        catch (System.Exception e) { Debug.Log($"[En ligne] Services injoignables pour l'instant : {e.Message}"); }
    }

    /// <summary>Le message sous les cartes de l'écran Jouer (le salon y renvoie ses erreurs).</summary>
    public void ShowPlayStatus(string message, bool isError) => SetStatus(message, isError);

    /// <summary>Une demande au service en ligne est en cours : pas de seconde demande en même temps.</summary>
    public void SetOnlineBusy(bool busy)
    {
        onlineBusy = busy;
        createButton?.SetEnabled(!busy);
        joinCodeButton?.SetEnabled(!busy);
        hostButton?.SetEnabled(!busy && !joinAttemptInProgress);
        joinButton?.SetEnabled(!busy);
    }

    // ------------------------------------------------------------------
    // Réseau local (sans code)
    // ------------------------------------------------------------------

    private void OnHostClicked()
    {
        if (onlineBusy) return;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) { SetStatus("NetworkManager introuvable dans la scène.", true); return; }
        if (nm.IsClient || nm.IsServer) { SetStatus("Déjà connecté.", true); return; }

        ushort port = ParsePort(hostPortField.value, 7777);
        UnityTransport transport = nm.GetComponent<UnityTransport>();
        if (transport == null) { SetStatus("UnityTransport introuvable.", true); return; }

        // Écoute sur toutes les interfaces (0.0.0.0) pour accepter un client du même réseau local,
        // pas seulement 127.0.0.1.
        string localIp = GetLocalIPv4();
        transport.SetConnectionData(localIp, port, "0.0.0.0");
        SessionFlow.PrepareConnectionData(nm); // le pseudo a pu changer depuis le lancement
        SessionFlow.ActiveRules = GameSettings.HostRules; // places, duels, spectateurs : figés pour la partie

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
        if (onlineBusy) return;
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
        SessionFlow.PrepareConnectionData(nm);

        if (!nm.StartClient())
        {
            SetStatus("Impossible de démarrer la connexion.", true);
            return;
        }

        joinAttemptInProgress = true;
        joinButton.text = "Annuler";
        hostButton.SetEnabled(false);
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
            // Refusé par l'hôte (partie pleine, version différente : voir SessionFlow), on dit
            // pourquoi ; sinon, la connexion n'a simplement pas abouti.
            string reason = NetworkManager.Singleton != null ? NetworkManager.Singleton.DisconnectReason : "";
            EndJoinAttempt(string.IsNullOrEmpty(reason)
                ? "Connexion échouée : vérifie l'adresse, le port, et que l'hôte a bien cliqué sur Héberger."
                : "Connexion refusée : " + reason, true);
        }
    }

    private void EndJoinAttempt(string message, bool isError)
    {
        joinAttemptInProgress = false;
        joinButton.text = "Rejoindre";
        hostButton.SetEnabled(!onlineBusy);
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
