using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using DuelArena.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// La navigation du menu principal, refait le 2026-10-17 d'après les maquettes de l'utilisateur. Posé sur le
/// même GameObject que l'UIDocument, dans la scène MainMenu (voir CLAUDE.md « Le menu principal et la
/// session »).
///
/// Les écrans : le titre (Jouer, Personnalisation, Options, Quitter), Jouer (PlayScreenController : les
/// modes, les salons, l'entraînement), le salon (LobbyController), Personnalisation (ArmoryController pour
/// le robot, WeaponCustomizationController pour les armes) et Options (OptionsController). Ils se montrent
/// par la classe USS « hidden ». Ce composant tient aussi :
/// - le fond (l'arène vue de la tribune, MenuArtwork), voilé hors de l'écran titre ;
/// - la carte du joueur, en haut à droite de chaque écran : son pseudo et la couleur de son robot ; un clic
///   ouvre la fenêtre du pseudo (sauf dans le salon, où il est déjà parti chez les autres) ;
/// - la fenêtre « Quitter le jeu ? », ouverte par Quitter ou par Échap depuis l'écran titre ;
/// - Échap, qui revient d'un écran (BackTarget) ;
/// - les salons sans code (réseau local) et le message sous les cartes de l'écran Jouer.
///
/// Réseau local : Héberger démarre le host puis charge IMMÉDIATEMENT la scène de jeu en mode Single — le
/// patron standard de Netcode pour une scène de menu (le NetworkManager survit au changement de scène,
/// parce qu'il s'applique DontDestroyOnLoad tant qu'il est à la racine). Rejoindre démarre le client : la
/// synchronisation de scène est automatique (EnableSceneManagement).
/// </summary>
public class MainMenuController : MonoBehaviour
{
    public const string TitleScreen = "screenTitle";
    public const string PlayScreen = "screenPlay";
    public const string LobbyScreen = "screenLobby";
    public const string CustomizeScreen = "screenCustomize";
    public const string OptionsScreen = "screenOptions";

    /// <summary>Le plafond d'images du menu, sur l'écran le plus rapide : rien n'y bouge assez vite pour en demander plus.</summary>
    public const int MenuMaxFrameRate = 120;

    /// <summary>Le temps (ms) donné par image aux envois de textures et de maillages au GPU sous l'écran de chargement,
    /// contre 2 dans les réglages de qualité, faits pour qu'un chargement ne saccade pas en jouant.</summary>
    private const int LoadingUploadTimeSlice = 8;

    /// <summary>Le temps (s) laissé à l'écran titre avant de préparer les services en ligne : d'abord son image et les
    /// portraits des armes.</summary>
    private const float OnlineWarmUpDelay = 3f;

    [Tooltip("Nom de la scène de jeu à charger une fois l'hébergement démarré. Doit être dans les Build Settings.")]
    [SerializeField] private string gameplaySceneName = "MultiTestScene";

    [SerializeField] private UIDocument document;

    [Tooltip("Le robot de Personnalisation : la palette des robots, lue sur le préfab joueur.")]
    [SerializeField] private CharacterPreviewController characterPreview;

    [Tooltip("Le salon des parties en ligne (sur le même GameObject si vide).")]
    [SerializeField] private LobbyController lobby;

    [Tooltip("Personnalisation : le robot, et les onglets (sur le même GameObject si vide).")]
    [SerializeField] private ArmoryController armory;

    [Tooltip("L'onglet ARMES de Personnalisation (sur le même GameObject si vide).")]
    [SerializeField] private WeaponCustomizationController weaponCustomization;

    [Tooltip("L'écran Jouer : les modes, l'entraînement (sur le même GameObject si vide).")]
    [SerializeField] private PlayScreenController playScreen;

    [Tooltip("Le panneau des Options (sur le même GameObject si vide).")]
    [SerializeField] private OptionsController options;

    [Tooltip("Les images du menu (le fond de l'écran titre), cuites par Duel Arena > Menu > Capturer les images du menu.")]
    [SerializeField] private MenuArtwork artwork;

    private static readonly string[] ScreenNames = { TitleScreen, PlayScreen, LobbyScreen, CustomizeScreen, OptionsScreen };

    private VisualElement root;
    private VisualElement backdrop;
    private VisualElement backdropDim;
    private VisualElement quitModal;
    private VisualElement pseudoModal;
    private VisualElement sessionEndModal;
    private Label sessionEndText;
    private VisualElement loadingScreen;
    private TextField pseudoField;
    private ArenaLogo pseudoModalLogo;
    private readonly List<Button> playerCards = new();

    private TextField joinCodeField;
    private Button joinCodeButton;
    private TextField hostPortField;
    private TextField joinIpField;
    private TextField joinPortField;
    private Button hostButton;
    private Button joinButton;
    private Label joinButtonLabel;
    private Label localIpLabel;
    private Label lanRulesLabel;
    private Label playStatusLabel;

    private bool joinAttemptInProgress;
    private bool onlineBusy;
    private VisualElement wiredScreen;   // l'écran titre de l'arbre branché (voir OnEnable)

    private MenuThumbnails thumbnails;
    private bool launchUnderway;         // ce qu'Update a vu à l'image d'avant (OnLaunchChanged)
    [System.NonSerialized] private int menuFrameRate;   // le plafond posé par ce menu (CapFrameRate)
    private bool loadingSpedUp;
    private ThreadPriority loadingPriorityBefore;
    private int uploadSliceBefore;
    private float onlineWarmUpAt;

    public MenuArtwork Artwork => artwork;

    /// <summary>Une fenêtre (Quitter, le pseudo, le retour au menu) est ouverte, ou une partie se charge : les écrans
    /// dessous ne lisent pas le clavier.</summary>
    public bool ModalOpen => IsOpen(quitModal) || IsOpen(pseudoModal) || IsOpen(sessionEndModal) || IsOpen(loadingScreen);

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        if (lobby == null) lobby = GetComponent<LobbyController>();
        if (armory == null) armory = GetComponent<ArmoryController>();
        if (weaponCustomization == null) weaponCustomization = GetComponent<WeaponCustomizationController>();
        if (playScreen == null) playScreen = GetComponent<PlayScreenController>();
        if (characterPreview == null) characterPreview = GetComponent<CharacterPreviewController>();
        if (options == null) options = GetComponent<OptionsController>();
        if (thumbnails == null) thumbnails = GetComponent<MenuThumbnails>();
        root = document.rootVisualElement;

        launchUnderway = false;
        onlineWarmUpAt = Time.unscaledTime + OnlineWarmUpDelay;
        CapFrameRate();

        // De retour d'une partie, la souris était encore verrouillée au centre de l'écran, invisible
        // (GameHud la verrouille en jeu) : un clic y pressait le bouton du milieu (vu par l'utilisateur
        // le 2026-10-16). Le menu la rend toujours.
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        backdrop = root.Q<VisualElement>("backdrop");
        backdropDim = root.Q<VisualElement>("backdropDim");
        if (backdrop != null && artwork != null && artwork.titleBackdrop != null)
            backdrop.style.backgroundImage = new StyleBackground(artwork.titleBackdrop);
        Label version = root.Q<Label>("versionLabel");
        if (version != null) version.text = "v" + Application.version;

        // Un arbre ne se branche qu'une fois (un second OnEnable sur le même arbre doublerait les clics :
        // Rejoindre démarrerait puis annulerait aussitôt) ; un arbre neuf (l'UIDocument recrée le sien
        // quand il se réactive) se rebranche.
        VisualElement title = root.Q<VisualElement>(TitleScreen);
        if (title != wiredScreen)
        {
            wiredScreen = title;
            WireTitleScreen();
            WireModals();
            WirePlayerCards();
            WirePlayScreen();
        }

        ShowScreenByName(TitleScreen);
        SessionFlow.ArrivedInMenu();
        loadingScreen?.AddToClassList("hidden");

        // De retour d'une partie (l'hôte est parti, la connexion est tombée...) : l'écran Jouer, et une fenêtre
        // qui dit pourquoi (SessionFlow.ReturnToMenu ; 2026-11-03 : la seule ligne au bas de l'écran Jouer passait
        // inaperçue, et les clients revenaient au menu sans comprendre).
        if (!string.IsNullOrEmpty(SessionFlow.PendingMenuMessage))
        {
            ShowScreenByName(PlayScreen);
            SetStatus(SessionFlow.PendingMenuMessage, true);
            OpenSessionEnd(SessionFlow.PendingMenuMessage);
            SessionFlow.PendingMenuMessage = "";
        }
    }

    // Le plafond et les réglages du chargement survivent à la scène, et d'une session de Play Mode à l'autre.
    private void OnDisable()
    {
        ReleaseFrameRate();
        SpeedUpLoading(false);
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

        // Le stand ouvert dans l'Editor a chargé le menu pour qu'il le lance (ShootingRangeBootstrap).
        if (ShootingRangeBootstrap.StartRangeOnMenuLoad)
        {
            ShootingRangeBootstrap.StartRangeOnMenuLoad = false;
            StartShootingRange();
        }

        // Mesurer une build sans la piloter : elle entre seule dans la scène, une fois par lancement, et le journal de
        // diagnostic (cpu=, rendu=, gpu=) dit le reste. Sert à comparer deux réglages de lancement (-force-d3d12).
        if (benchLaunched) return;
        string[] arguments = System.Environment.GetCommandLineArgs();
        bool stand = System.Array.IndexOf(arguments, "-banc-stand") >= 0, arena = System.Array.IndexOf(arguments, "-banc-arene") >= 0;
        if (!stand && !arena) return;
        benchLaunched = true;
        if (stand) StartShootingRange();
        else OnHostClicked();
    }

    private static bool benchLaunched;

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
        root.Q<Button>("playButton").clicked += () => ShowScreenByName(PlayScreen);
        root.Q<Button>("armoryButton").clicked += () => ShowScreenByName(CustomizeScreen);
        root.Q<Button>("optionsButton").clicked += () => ShowScreenByName(OptionsScreen);
        root.Q<Button>("quitButton").clicked += OpenQuit;

        root.Q<Button>("playBackButton").clicked += () => ShowScreenByName(TitleScreen);
        root.Q<Button>("customBackButton").clicked += GoBack;
        root.Q<Button>("optionsBackButton").clicked += LeaveOptions;
    }

    /// <summary>Quitte les Options pour l'écran titre ; des réglages non appliqués font d'abord proposer de les
    /// appliquer (OptionsController.RequestLeave, 2026-10-27).</summary>
    private void LeaveOptions()
    {
        if (options != null) options.RequestLeave(() => ShowScreenByName(TitleScreen));
        else ShowScreenByName(TitleScreen);
    }

    /// <summary>Affiche l'écran demandé, masque les autres. Public : le salon (LobbyController) s'ouvre et
    /// se referme par lui.</summary>
    public void ShowScreenByName(string screenName)
    {
        foreach (string name in ScreenNames)
        {
            VisualElement s = root.Q<VisualElement>(name);
            if (s != null) s.EnableInClassList("hidden", name != screenName);
        }
        // L'arène se voit en grand derrière l'écran titre ; les autres écrans la voilent.
        backdropDim?.EnableInClassList("hidden", screenName == TitleScreen);

        // Le plateau d'aperçu (robot, arme) ne tourne que sur Personnalisation.
        if (armory != null)
        {
            if (screenName == CustomizeScreen) armory.OnShown();
            else armory.OnHidden();
        }
        if (screenName == PlayScreen) OnPlayScreenShown();
        if (screenName == OptionsScreen && options != null) options.OnShown();
        RefreshPlayerCards();
    }

    /// <summary>L'écran affiché, ou null.</summary>
    public string CurrentScreen
    {
        get
        {
            foreach (string name in ScreenNames)
            {
                VisualElement screen = root?.Q<VisualElement>(name);
                if (screen != null && !screen.ClassListContains("hidden")) return name;
            }
            return null;
        }
    }

    /// <summary>
    /// Échap revient d'un écran (BackTarget). Tenu ici seul : chaque écran qui la lirait de son côté
    /// ferait revenir de deux écrans d'un coup (le second voit l'appui dans la même image).
    /// </summary>
    private void Update()
    {
        // Une partie se charge (2026-11-03) : l'écran de chargement, du lancement jusqu'à l'arène, qui garde le même
        // dans son noir (GameHud). Le menu dessous ne bouge plus.
        NetworkManager nm = NetworkManager.Singleton;
        bool launching = nm != null && LaunchUnderway(nm.IsListening, nm.IsServer, nm.IsConnectedClient, nm.IsClient && OnlineSession.InSession);
        if (launching != launchUnderway) OnLaunchChanged(launching);
        if (loadingScreen != null && launching == loadingScreen.ClassListContains("hidden"))
        {
            loadingScreen.EnableInClassList("hidden", !launching);
            if (launching) root.panel?.focusController?.focusedElement?.Blur();
        }
        if (launching) return;

        // Appliquer, dans les Options, écrit le plafond des réglages (GameSettings.ApplyGraphics) à la place du sien.
        if (Application.targetFrameRate != menuFrameRate) CapFrameRate();
        WarmUpOnlineOnTitle();

        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) GoBack();
    }

    /// <summary>Une partie part de ce menu, ou n'en part plus (une connexion qui tombe avant l'arène) : le plafond du
    /// menu tombe AVANT le chargement, compté par image, et ce qui reste des portraits se rend ici, sous les lumières du
    /// plateau, que l'arène n'a pas (la préparation et le stand les montrent).</summary>
    private void OnLaunchChanged(bool launching)
    {
        launchUnderway = launching;
        SpeedUpLoading(launching);
        if (launching)
        {
            ReleaseFrameRate();
            thumbnails?.PrepareWeaponPortraits();
        }
        else CapFrameRate();
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Tests : ce que fait Update quand une partie part, ou n'en part plus.</summary>
    public void TestLaunchChanged(bool launching) => OnLaunchChanged(launching);
#endif

    /// <summary>
    /// Le plafond d'images du menu : la fréquence de l'écran (`screenRate`, Hz ; 0 si on ne la connaît pas), jamais plus
    /// de MenuMaxFrameRate, ni plus que le plafond des réglages (`settingsCap`, 0 sans plafond). Sans lui, un écran fixe
    /// tournait à plus de 2 000 images/s, et l'instance qui attend au menu prenait le GPU à celle qui joue. Fonction
    /// pure, testée.
    /// </summary>
    public static int MenuFrameRate(double screenRate, int settingsCap)
    {
        int rate = screenRate >= 1.0 && screenRate < MenuMaxFrameRate ? (int)System.Math.Round(screenRate) : MenuMaxFrameRate;
        return settingsCap > 0 ? Mathf.Min(rate, settingsCap) : rate;
    }

    private void CapFrameRate()
    {
        menuFrameRate = MenuFrameRate(Screen.currentResolution.refreshRateRatio.value, GameSettings.FrameRateCap);
        Application.targetFrameRate = menuFrameRate;
    }

    /// <summary>Hors du menu, le plafond des réglages, comme GameSettings.ApplyGraphics le pose.</summary>
    private static void ReleaseFrameRate()
    {
        int cap = GameSettings.FrameRateCap;
        Application.targetFrameRate = cap > 0 ? cap : -1;
    }

    /// <summary>
    /// Sous l'écran de chargement, personne ne joue : le chargement peut prendre de longues images, ce que ses réglages
    /// habituels évitent. Rendus quand la partie ne part plus, ou que le menu laisse la place à la scène chargée : tout
    /// est alors lu et intégré, et ils ne valent jamais pendant une manche.
    /// </summary>
    private void SpeedUpLoading(bool on)
    {
        if (on == loadingSpedUp) return;
        loadingSpedUp = on;
        if (on)
        {
            loadingPriorityBefore = Application.backgroundLoadingPriority;
            uploadSliceBefore = QualitySettings.asyncUploadTimeSlice;
            Application.backgroundLoadingPriority = ThreadPriority.High;
            QualitySettings.asyncUploadTimeSlice = LoadingUploadTimeSlice;
        }
        else
        {
            Application.backgroundLoadingPriority = loadingPriorityBefore;
            QualitySettings.asyncUploadTimeSlice = uploadSliceBefore;
        }
    }

    /// <summary>
    /// Une partie part de ce menu : Netcode tourne en hôte, ou connecté à un hôte, ou lancé par le salon en ligne
    /// (`onlineLaunch` : ses membres se connectent d'eux-mêmes), et l'arène (ou le stand) va remplacer le menu. Une
    /// connexion en réseau local qui se cherche encore n'en est pas une : le menu reste, avec son bouton Annuler.
    /// Fonction pure, testée.
    /// </summary>
    public static bool LaunchUnderway(bool listening, bool server, bool connectedClient, bool onlineLaunch)
        => listening && (server || connectedClient || onlineLaunch);

    /// <summary>
    /// L'écran où Échap ramène depuis `shown`, ou null. Jouer, Personnalisation et Options reviennent à
    /// l'écran titre. Rien depuis le salon, qu'on quitte par son bouton (le quitter le ferme pour tous quand
    /// on l'héberge), ni depuis l'écran titre (Échap y ouvre la fenêtre « Quitter le jeu ? »), ni pendant un
    /// remappage, qu'Échap annule seulement. Fonction pure, testée.
    /// </summary>
    public static string BackTarget(string shown, bool rebinding)
    {
        if (rebinding) return null;
        return shown switch
        {
            PlayScreen or CustomizeScreen or OptionsScreen => TitleScreen,
            _ => null,
        };
    }

    /// <summary>Le retour d'Échap, d'un seul pas : une fenêtre ouverte se ferme, la bande des choix de
    /// l'armurerie se replie, puis l'écran revient.</summary>
    private void GoBack()
    {
        if (root == null) return;
        if (IsOpen(sessionEndModal)) { CloseSessionEnd(); return; }
        if (IsOpen(quitModal)) { CloseQuit(); return; }
        if (IsOpen(pseudoModal)) { ClosePseudo(); return; }
        if (OptionsController.AnyRebinding) return;

        string shown = CurrentScreen;
        if (shown == CustomizeScreen && weaponCustomization != null && weaponCustomization.CloseStrip()) return;
        if (shown == LobbyScreen && lobby != null && lobby.CloseWindow()) return;
        if (shown == TitleScreen) { OpenQuit(); return; }
        if (shown == OptionsScreen)
        {
            // La fenêtre « Appliquer les réglages ? » se referme d'abord : on reste dans les Options.
            if (options != null && options.CloseLeaveModal()) return;
            LeaveOptions();
            return;
        }
        string target = BackTarget(shown, false);
        if (target != null) ShowScreenByName(target);
    }

    private static bool IsOpen(VisualElement modal) => modal != null && !modal.ClassListContains("hidden");

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------
    // Les fenêtres : quitter le jeu, le pseudo
    // ------------------------------------------------------------------

    private void WireModals()
    {
        quitModal = root.Q<VisualElement>("quitModal");
        pseudoModal = root.Q<VisualElement>("pseudoModal");
        sessionEndModal = root.Q<VisualElement>("sessionEndModal");
        sessionEndText = root.Q<Label>("sessionEndText");
        loadingScreen = root.Q<VisualElement>("loadingScreen");
        root.Q<Button>("sessionEndOkButton").clicked += CloseSessionEnd;
        pseudoField = root.Q<TextField>("pseudoField");
        pseudoModalLogo = root.Q<ArenaLogo>("pseudoModalLogo");

        root.Q<Button>("quitCancelButton").clicked += CloseQuit;
        root.Q<Button>("quitBackHint").clicked += CloseQuit;
        root.Q<Button>("quitConfirmButton").clicked += QuitGame;

        // Le pseudo (2026-10-12), retenu d'une partie à l'autre, annoncé au salon et à l'hôte.
        pseudoField.RegisterValueChangedCallback(evt =>
        {
            GameSettings.PlayerName = evt.newValue;
            RefreshPlayerCards();
        });
        pseudoField.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) ClosePseudo();
        }, TrickleDown.TrickleDown);
        root.Q<Button>("pseudoOkButton").clicked += ClosePseudo;
    }

    private void OpenQuit()
    {
        root.panel?.focusController?.focusedElement?.Blur();
        quitModal?.RemoveFromClassList("hidden");
    }

    private void CloseQuit() => quitModal?.AddToClassList("hidden");

    private void OpenSessionEnd(string message)
    {
        if (sessionEndModal == null) return;
        if (sessionEndText != null) sessionEndText.text = message;
        root.panel?.focusController?.focusedElement?.Blur();
        sessionEndModal.RemoveFromClassList("hidden");
    }

    private void CloseSessionEnd() => sessionEndModal?.AddToClassList("hidden");

    private void OpenPseudo()
    {
        if (pseudoModal == null) return;
        pseudoField.SetValueWithoutNotify(GameSettings.PlayerName);
        pseudoModal.RemoveFromClassList("hidden");
        pseudoField.schedule.Execute(() =>
        {
            pseudoField.Focus();
            pseudoField.SelectAll();
        });
    }

    private void ClosePseudo()
    {
        if (pseudoModal == null) return;
        pseudoModal.AddToClassList("hidden");
        pseudoField.Blur();
        RefreshPlayerCards();
        armory?.RefreshPlayerName();
    }

    // ------------------------------------------------------------------
    // La carte du joueur
    // ------------------------------------------------------------------

    private void WirePlayerCards()
    {
        playerCards.Clear();
        root.Query<Button>(className: "player-card").ForEach(card =>
        {
            playerCards.Add(card);
            // Dans le salon, le pseudo est déjà parti chez les autres (OnlineSession) : la carte ne s'y
            // change plus.
            bool inLobby = ScreenOf(card) == LobbyScreen;
            card.EnableInClassList("player-card--static", inLobby);
            if (!inLobby) card.clicked += OpenPseudo;
        });
    }

    // Le nom de l'écran qui contient `element`, ou null.
    private static string ScreenOf(VisualElement element)
    {
        for (VisualElement v = element; v != null; v = v.parent)
        {
            if (System.Array.IndexOf(ScreenNames, v.name) >= 0) return v.name;
        }
        return null;
    }

    /// <summary>Le pseudo et la couleur du robot, sur toutes les cartes du joueur (après un changement de
    /// pseudo ou de couleur).</summary>
    public void RefreshPlayerCards()
    {
        string name = PlayerMatchState.SanitizeName(GameSettings.PlayerName);
        Color32 color = PlayerColor;
        foreach (Button card in playerCards)
        {
            bool editable = !card.ClassListContains("player-card--static");
            Label label = card.Q<Label>(className: "player-card__name");
            if (label != null)
                label.text = !string.IsNullOrEmpty(name) ? name.ToUpperInvariant() : editable ? "CHOISIS TON PSEUDO" : "JOUEUR";
            ArenaLogo logo = card.Q<ArenaLogo>();
            if (logo != null) logo.LogoColor = color;
        }
        if (pseudoModalLogo != null) pseudoModalLogo.LogoColor = color;
    }

    /// <summary>La couleur du robot du joueur : celle qu'il a choisie, sinon la première de la palette (celle
    /// que reçoit le premier arrivé).</summary>
    public Color32 PlayerColor
    {
        get
        {
            IReadOnlyList<Color32> palette = characterPreview != null ? characterPreview.Palette : System.Array.Empty<Color32>();
            if (palette.Count == 0) return new Color32(244, 190, 28, 255);
            int index = GameSettings.PreferredColorIndex;
            return palette[index >= 0 && index < palette.Count ? index : 0];
        }
    }

    // ------------------------------------------------------------------
    // Écran Jouer : rejoindre par le code, le réseau local, le message
    // ------------------------------------------------------------------

    private void WirePlayScreen()
    {
        joinCodeButton = root.Q<Button>("joinCodeButton");
        joinCodeField = root.Q<TextField>("joinCodeField");
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
        joinButtonLabel = root.Q<Label>("joinButtonLabel");
        localIpLabel = root.Q<Label>("localIpLabel");
        lanRulesLabel = root.Q<Label>("lanRulesLabel");
        playStatusLabel = root.Q<Label>("playStatusLabel");

        hostButton.clicked += OnHostClicked;
        joinButton.clicked += OnJoinClicked;
    }

    private void OnPlayScreenShown()
    {
        if (lanRulesLabel != null) lanRulesLabel.text = "Réglages : " + LobbyController.DescribeRules(GameSettings.HostRules);
        playScreen?.OnShown();

        // Les services en ligne se préparent pendant que le joueur lit l'écran : créer ou rejoindre
        // part ensuite sans attendre la connexion. Un échec (pas d'internet) se dira au clic. Déjà prêts
        // (WarmUpOnlineOnTitle), cet appel ne fait rien.
        _ = WarmUpOnlineAsync();
    }

    /// <summary>La connexion aux services gèle une image : elle se fait sur l'écran titre, qui ne bouge pas, plutôt
    /// qu'à l'arrivée sur l'écran Jouer. Une fois par ouverture du menu, et jamais sous les yeux d'un joueur parti sur
    /// un autre écran (l'écran Jouer s'en charge alors, comme avant).</summary>
    private void WarmUpOnlineOnTitle()
    {
        if (Time.unscaledTime < onlineWarmUpAt) return;
        onlineWarmUpAt = float.PositiveInfinity;
        if (CurrentScreen == TitleScreen) _ = WarmUpOnlineAsync();
    }

    private static async System.Threading.Tasks.Task WarmUpOnlineAsync()
    {
        try { await OnlineSession.EnsureSignedInAsync(); }
        catch (System.Exception e) { Debug.Log($"[En ligne] Services injoignables pour l'instant : {e.Message}"); }
    }

    /// <summary>Le message de l'écran Jouer (le salon y renvoie ses erreurs).</summary>
    public void ShowPlayStatus(string message, bool isError) => SetStatus(message, isError);

    /// <summary>Une demande au service en ligne est en cours : pas de seconde demande en même temps.</summary>
    public void SetOnlineBusy(bool busy)
    {
        onlineBusy = busy;
        joinCodeButton?.SetEnabled(!busy);
        hostButton?.SetEnabled(!busy && !joinAttemptInProgress);
        joinButton?.SetEnabled(!busy);
        playScreen?.SetBusy(busy);
    }

    /// <summary>Une demande en ligne ou une connexion en réseau local est en cours.</summary>
    public bool Busy => onlineBusy || joinAttemptInProgress;

    // ------------------------------------------------------------------
    // Le stand de tir, le réseau local (sans code)
    // ------------------------------------------------------------------

    /// <summary>
    /// Le stand de tir (2026-10-16) : un hôte qui n'écoute que cette machine, et la scène du stand au
    /// lieu de l'arène (ShootingRange). Lancé par l'onglet Entraînement de l'écran Jouer, ou tout seul quand
    /// on a ouvert le stand dans l'Editor (ShootingRangeBootstrap).
    /// </summary>
    public void StartShootingRange()
    {
        if (onlineBusy) return;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.IsClient || nm.IsServer) return;
        UnityTransport transport = nm.GetComponent<UnityTransport>();
        if (transport == null) return;

        // Personne ne rejoint le stand : un port libre choisi par le système (0) plutôt que 7777, qu'une
        // autre instance du jeu (le joueur virtuel, une build ouverte) peut déjà tenir.
        transport.SetConnectionData("127.0.0.1", 0, "127.0.0.1");
        SessionFlow.PrepareConnectionData(nm);
        SessionFlow.ActiveRules = GameSettings.HostRules;
        if (!nm.StartHost())
        {
            Debug.LogWarning("Stand de tir : impossible de démarrer l'hôte.", this);
            return;
        }
        SceneEventProgressStatus status = nm.SceneManager.LoadScene(ShootingRange.SceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
            Debug.LogWarning($"Stand de tir : le chargement de la scène a échoué ({status}). Est-elle dans la Build ?", this);
    }

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
        joinButtonLabel.text = "ANNULER";
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
        joinButtonLabel.text = "REJOINDRE";
        hostButton.SetEnabled(!onlineBusy);
        SetStatus(message, isError);
    }

    private void SetStatus(string message, bool isError)
    {
        if (playStatusLabel == null) return;
        playStatusLabel.text = message;
        playStatusLabel.EnableInClassList("da-status--error", isError);
        playStatusLabel.EnableInClassList("da-status--ok", !isError && message == "Connecté.");
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
