using System;
using System.Collections.Generic;
using DuelArena.UI;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Le salon d'une partie en ligne (2026-10-13, demandé par l'utilisateur ; GDD § 12 : « choix du mode, avec
/// ses réglages à côté → lobby → duel »), refait le 2026-10-17 d'après ses maquettes : « les deux duellistes
/// en tête d'affiche si on sait qui c'est d'avance, et dessous la liste des spectateurs ; sur la droite, un
/// visuel de la map avec les éléments clés ». Sur MainMenuUI, à côté de MainMenuController ; ouvert depuis
/// l'écran Jouer (une carte de mode, ou Rejoindre avec un code).
///
/// - En tête d'affiche : l'hôte (toujours le premier duelliste) et le second quand on le connaît
///   (LobbyLineup) : à trois et plus, c'est le premier des autres à arriver dans l'arène.
/// - Dessous, la tribune : ceux qui attendent leur tour, puis les places libres.
/// - À droite : l'arène (sa carte, ses points d'apparition, sa tribune et ses blocs, MenuArtwork), le mode
///   et les réglages, que seul l'hôte change et que tous voient changer, puis le code à donner.
/// Les portraits sont le robot de chaque joueur, dans la couleur qu'il aura vraiment : celle qu'il a choisie
/// si personne avant lui ne l'a prise, sinon la première libre (PlayerPaint.PickPaint, comme le serveur).
///
/// Ce composant ne décide rien du réseau : il appelle OnlineSession et affiche ce que le salon publie.
/// </summary>
[DisallowMultipleComponent]
public class LobbyController : MonoBehaviour
{
    [SerializeField] private UIDocument document;
    [SerializeField] private MainMenuController mainMenu;

    [Tooltip("Les portraits des joueurs.")]
    [SerializeField] private MenuThumbnails thumbnails;

    [Tooltip("La palette des robots, lue sur le préfab joueur (sur le même GameObject si vide).")]
    [SerializeField] private CharacterPreviewController characterPreview;

    [Tooltip("Nom de la scène de jeu à charger au lancement. Doit être dans les Build Settings.")]
    [SerializeField] private string gameplaySceneName = "MultiTestScene";

    private static readonly (DuelOrganization value, string label)[] Organizations =
    {
        (DuelOrganization.KingOfTheHill, "Roi de la colline"),
        (DuelOrganization.FixedDuelists, "Duellistes fixes"),
    };

    private sealed class DuelistCard
    {
        public VisualElement card, portrait;
        public Label unknown, role, name, status;
    }

    /// <summary>Un joueur du salon, tel que l'écran le montre.</summary>
    public readonly struct Entry
    {
        public readonly string id;
        public readonly string name;
        public readonly Color32 paint;
        public readonly bool host, local;
        public readonly ChassisId chassis;

        public Entry(string id, string name, Color32 paint, bool host, bool local, ChassisId chassis = ChassisId.CT01)
        {
            this.id = id;
            this.name = name;
            this.paint = paint;
            this.host = host;
            this.local = local;
            this.chassis = chassis;
        }
    }

    /// <summary>Ce que l'écran du salon montre : de quoi le dessiner sans le service (les captures d'écran le
    /// remplissent à la main, Render le lit dans le salon).</summary>
    public struct View
    {
        public bool host;
        public bool started;
        public MatchRules rules;
        public string code;
        /// <summary>L'hôte d'abord, puis les autres dans leur ordre d'arrivée.</summary>
        public List<Entry> players;
    }

    private VisualElement root;
    private VisualElement lobbyScreen;
    private Label headline, subline;
    private VisualElement spinner;
    private readonly DuelistCard[] duelists = new DuelistCard[2];
    private Label queueTitle, queueHint;
    private ScrollView queueList;
    private Label arenaName;
    private ArenaMapView map;
    private Label modeValue, rulesHint;
    private OptionCycler roundsCycler, capacityCycler, organizationCycler, interventionsCycler;
    private Label codeLabel, copyLabel, statusLabel;
    private Button copyButton, startButton, leaveButton;

    private bool busy;        // une demande au service en cours (créer, rejoindre, lancer, quitter)
    private bool launching;   // l'hôte a lancé : on attend l'arène
    private bool statusIsError;
    private float copiedUntil;
    private int capacityFloor = MatchRules.MinCapacity;   // la plus petite capacité proposée (les joueurs déjà là)

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        if (mainMenu == null) mainMenu = GetComponent<MainMenuController>();
        if (characterPreview == null) characterPreview = GetComponent<CharacterPreviewController>();
        root = document.rootVisualElement;

        lobbyScreen = root.Q<VisualElement>("screenLobby");
        headline = root.Q<Label>("lobbyHeadline");
        subline = root.Q<Label>("lobbySubline");
        spinner = root.Q<VisualElement>("lobbySpinner");
        for (int i = 0; i < duelists.Length; i++)
        {
            duelists[i] = new DuelistCard
            {
                card = root.Q<VisualElement>($"duelistCard{i}"),
                portrait = root.Q<VisualElement>($"duelistPortrait{i}"),
                unknown = root.Q<Label>($"duelistUnknown{i}"),
                role = root.Q<Label>($"duelistRole{i}"),
                name = root.Q<Label>($"duelistName{i}"),
                status = root.Q<Label>($"duelistStatus{i}"),
            };
        }
        queueTitle = root.Q<Label>("lobbyQueueTitle");
        queueHint = root.Q<Label>("lobbyQueueHint");
        queueList = root.Q<ScrollView>("lobbyQueueList");
        arenaName = root.Q<Label>("lobbyArenaName");
        map = root.Q<ArenaMapView>("lobbyMap");
        modeValue = root.Q<Label>("lobbyModeValue");
        roundsCycler = root.Q<OptionCycler>("lobbyRoundsCycler");
        rulesHint = root.Q<Label>("lobbyRulesHint");
        capacityCycler = root.Q<OptionCycler>("lobbyCapacityCycler");
        organizationCycler = root.Q<OptionCycler>("lobbyOrganizationCycler");
        interventionsCycler = root.Q<OptionCycler>("lobbyInterventionsCycler");
        codeLabel = root.Q<Label>("lobbyCodeLabel");
        copyLabel = root.Q<Label>("lobbyCopyLabel");
        statusLabel = root.Q<Label>("lobbyStatusLabel");
        copyButton = root.Q<Button>("lobbyCopyButton");
        startButton = root.Q<Button>("lobbyStartButton");
        leaveButton = root.Q<Button>("lobbyLeaveButton");

        var organizationLabels = new string[Organizations.Length];
        for (int i = 0; i < Organizations.Length; i++) organizationLabels[i] = Organizations[i].label;
        organizationCycler.Choices = organizationLabels;
        BuildArena();

        copyButton.clicked += CopyCode;
        startButton.clicked += OnStartClicked;
        leaveButton.clicked += OnLeaveClicked;
        roundsCycler.RegisterValueChangedCallback(OnRoundsChanged);
        capacityCycler.RegisterValueChangedCallback(OnCapacityChanged);
        organizationCycler.RegisterValueChangedCallback(OnOrganizationChanged);
        interventionsCycler.RegisterValueChangedCallback(OnInterventionsChanged);

        OnlineSession.Changed += Render;
        OnlineSession.Ended += OnSessionEnded;
    }

    private void OnDisable()
    {
        copyButton.clicked -= CopyCode;
        startButton.clicked -= OnStartClicked;
        leaveButton.clicked -= OnLeaveClicked;
        roundsCycler.UnregisterValueChangedCallback(OnRoundsChanged);
        capacityCycler.UnregisterValueChangedCallback(OnCapacityChanged);
        organizationCycler.UnregisterValueChangedCallback(OnOrganizationChanged);
        interventionsCycler.UnregisterValueChangedCallback(OnInterventionsChanged);

        OnlineSession.Changed -= Render;
        OnlineSession.Ended -= OnSessionEnded;
    }

    private void Update()
    {
        if (copiedUntil > 0f && Time.unscaledTime > copiedUntil)
        {
            copiedUntil = 0f;
            copyLabel.text = "COPIER";
        }
    }

    // ------------------------------------------------------------------
    // Entrer dans un salon (depuis l'écran Jouer)
    // ------------------------------------------------------------------

    /// <summary>Ouvre un salon privé avec ces réglages (ceux du mode choisi), retenus pour la prochaine
    /// partie, et l'affiche.</summary>
    public void Create(MatchRules rules)
    {
        if (busy || LocalNetworkBusy()) return;
        GameSettings.HostRules = rules.Sanitized();
        Create();
    }

    /// <summary>Ouvre un salon privé avec les réglages de la dernière partie, et l'affiche.</summary>
    public async void Create()
    {
        if (busy || LocalNetworkBusy()) return;
        SetBusy(true);
        mainMenu.ShowPlayStatus("Ouverture du salon...", false);
        try
        {
            await OnlineSession.CreateAsync(GameSettings.HostRules);
            if (this == null) return;
            mainMenu.ShowPlayStatus("", false);
            Open();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[En ligne] Ouverture du salon : {e}");
            if (this == null) return;
            mainMenu.ShowPlayStatus(OnlineSession.DescribeError(e), true);
        }
        finally
        {
            if (this != null) SetBusy(false);
        }
    }

    /// <summary>
    /// Entre dans le salon de ce code. Si la partie y est déjà lancée, le joueur va directement dans
    /// l'arène, et ce menu disparaît avant la fin de la tâche.
    /// </summary>
    public async void Join(string code)
    {
        if (busy || LocalNetworkBusy()) return;
        string normalized = OnlineSession.NormalizeCode(code);
        if (normalized.Length == 0)
        {
            mainMenu.ShowPlayStatus("Tape le code que l'hôte t'a donné.", true);
            return;
        }

        SetBusy(true);
        mainMenu.ShowPlayStatus($"Connexion au salon {normalized}...", false);

        // Si la partie est lancée, Netcode se connecte pendant l'entrée dans le salon : la demande de
        // connexion doit déjà porter le pseudo et la version.
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null) SessionFlow.PrepareConnectionData(nm);

        try
        {
            await OnlineSession.JoinAsync(normalized);
            if (this == null) return; // partie déjà lancée : on est dans l'arène
            mainMenu.ShowPlayStatus("", false);
            Open();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[En ligne] Entrée dans le salon {normalized} : {e}");
            if (this == null) return;
            string reason = nm != null ? nm.DisconnectReason : "";
            mainMenu.ShowPlayStatus(OnlineSession.DescribeError(e, reason), true);
        }
        finally
        {
            if (this != null) SetBusy(false);
        }
    }

    // Une connexion en réseau local tourne déjà (une tentative en cours) : la partie en ligne
    // démarrerait Netcode par-dessus.
    private bool LocalNetworkBusy()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return false;
        mainMenu.ShowPlayStatus("Une connexion en réseau local est en cours : annule-la d'abord.", true);
        return true;
    }

    private void Open()
    {
        launching = false;
        SetStatus("", false);
        mainMenu.ShowScreenByName(MainMenuController.LobbyScreen);
        Render();
    }

    // ------------------------------------------------------------------
    // Le salon
    // ------------------------------------------------------------------

    private void Render()
    {
        if (this == null || root == null) return;
        ISession session = OnlineSession.Current;
        if (session == null) return;
        Show(new View
        {
            host = session.IsHost,
            started = OnlineSession.GameStarted || launching,
            rules = OnlineSession.Rules,
            code = session.Code,
            players = Entries(session),
        });
    }

    /// <summary>Montre l'écran du salon avec ces données, sans le service : pour voir l'écran dans tous ses
    /// états (seul, à deux, à six) lors des captures.</summary>
    public void ShowPreview(View view)
    {
        mainMenu.ShowScreenByName(MainMenuController.LobbyScreen);
        Show(view);
    }

    private void Show(View view)
    {
        List<Entry> players = view.players ?? new List<Entry>();
        var lineup = LobbyLineup<Entry>.Arrange(players, view.rules.capacity);

        codeLabel.text = string.IsNullOrEmpty(view.code) ? "······" : view.code;
        RenderHeadline(view.host, view.started, players.Count);
        RenderDuelists(lineup, players.Count);
        RenderStands(lineup, view.rules);
        RenderRules(view.rules, view.host, view.host && !view.started, players.Count);

        startButton.EnableInClassList("hidden", !view.host);
        startButton.SetEnabled(view.host && !busy && !view.started);
        leaveButton.SetEnabled(!busy || launching);

        if (view.started) SetStatus(view.host ? "Lancement de la partie..." : "La partie démarre...", false);
        else if (!statusIsError) SetStatus("", false);
    }

    /// <summary>Un joueur pour un aperçu de l'écran (ShowPreview).</summary>
    public static Entry PreviewEntry(string name, Color32 paint, bool host, bool local) => new Entry(name, name, paint, host, local);

    /// <summary>Les joueurs du salon, l'hôte d'abord puis les autres dans leur ordre d'arrivée, chacun avec
    /// la couleur qu'il aura en partie.</summary>
    private List<Entry> Entries(ISession session)
    {
        var players = new List<IReadOnlyPlayer>(session.Players);
        string hostId = session.Host;
        players.Sort((a, b) =>
        {
            bool aHost = a.Id == hostId, bHost = b.Id == hostId;
            if (aHost != bHost) return aHost ? -1 : 1;
            return a.Joined.CompareTo(b.Joined);
        });

        string localId = session.CurrentPlayer != null ? session.CurrentPlayer.Id : "";
        IReadOnlyList<Color32> palette = Palette;
        var taken = new List<Color32>();
        var entries = new List<Entry>();
        for (int i = 0; i < players.Count; i++)
        {
            IReadOnlyPlayer player = players[i];
            string name = OnlineSession.NameOf(player);
            Color32 paint = palette.Count > 0 ? PlayerPaint.PickPaint(palette, taken, OnlineSession.ColorOf(player)) : new Color32(244, 190, 28, 255);
            taken.Add(paint);
            entries.Add(new Entry(player.Id, string.IsNullOrEmpty(name) ? $"Joueur {i + 1}" : name, paint, player.Id == hostId, player.Id == localId,
                                  OnlineSession.ChassisOf(player)));
        }
        return entries;
    }

    private IReadOnlyList<Color32> Palette => characterPreview != null ? characterPreview.Palette : Array.Empty<Color32>();

    private void RenderHeadline(bool host, bool started, int playerCount)
    {
        bool waiting;
        if (started)
        {
            headline.text = "LANCEMENT";
            subline.text = "L'ARÈNE SE CHARGE…";
            waiting = true;
        }
        else if (playerCount < DuelRoster.DuelistSlots)
        {
            headline.text = "MATCH EN ATTENTE";
            subline.text = "EN ATTENTE D'UN ADVERSAIRE…";
            waiting = true;
        }
        else if (host)
        {
            headline.text = "PRÊT AU COMBAT";
            subline.text = "LANCE LA PARTIE QUAND TU VEUX";
            waiting = false;
        }
        else
        {
            headline.text = "MATCH EN ATTENTE";
            subline.text = "EN ATTENTE DU LANCEMENT PAR L'HÔTE…";
            waiting = true;
        }
        spinner.EnableInClassList("hidden", !waiting);
    }

    private void RenderDuelists(LobbyLineup<Entry> lineup, int playerCount)
    {
        ShowDuelist(duelists[0], lineup.hasFirst, lineup.first, "DUELLISTE 1", "", "");
        if (lineup.secondKnown)
            ShowDuelist(duelists[1], true, lineup.second, "DUELLISTE 2", "", "");
        else if (lineup.secondAmongStands)
            ShowDuelist(duelists[1], false, default, "DUELLISTE 2", "?", "Le premier des spectateurs arrivé dans l'arène.");
        else
            ShowDuelist(duelists[1], false, default, "DUELLISTE 2", "EN ATTENTE…",
                playerCount == 0 ? "" : "Donne le code du salon à ton adversaire.");
    }

    private void ShowDuelist(DuelistCard view, bool known, Entry entry, string role, string unknownName, string unknownStatus)
    {
        view.card.EnableInClassList("duelist-card--self", known && entry.local);
        view.unknown.EnableInClassList("hidden", known);
        view.role.text = known && entry.host ? role + " · HÔTE" : role;
        view.name.text = known ? entry.name.ToUpperInvariant() : unknownName;
        view.status.text = known ? (entry.local ? "PRÊT · TOI" : "PRÊT") : unknownStatus;
        view.status.EnableInClassList("duelist-card__status--waiting", !known);
        RequestPortrait(view.portrait, known ? entry.paint : default, !known, known ? entry.chassis : ChassisId.CT01);
    }

    private void RequestPortrait(VisualElement target, Color32 paint, bool silhouette, ChassisId chassis)
    {
        if (thumbnails == null || target == null) return;
        var shot = new MenuThumbnails.RobotShot
        {
            paint = paint,
            silhouette = silhouette,
            chassis = chassis,
            time = 0.4f,
            yaw = -18f,
            framing = MenuThumbnails.Framing.Bust,
            width = 300,
            height = 330,
        };
        // La vignette arrive plus tard : la carte a pu changer de joueur entre-temps.
        string key = silhouette ? "silhouette" : $"{(int)chassis}/{paint.r},{paint.g},{paint.b}";
        target.userData = key;
        thumbnails.Robot(shot, texture =>
        {
            if (!Equals(target.userData, key) || texture is not RenderTexture rt) return;
            target.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
        });
    }

    private void RenderStands(LobbyLineup<Entry> lineup, MatchRules rules)
    {
        queueList.Clear();
        foreach (Entry entry in lineup.stands) queueList.Add(StandRow(entry, queueList.childCount + 1));
        for (int i = 0; i < lineup.freeStands; i++) queueList.Add(FreeRow());

        int standPlaces = lineup.stands.Count + lineup.freeStands;
        queueTitle.text = standPlaces == 0 ? "SPECTATEURS" : $"SPECTATEURS  {lineup.stands.Count}/{standPlaces}";
        if (standPlaces == 0)
            queueHint.text = "Deux places : un duel sans tribune.";
        else if (lineup.secondAmongStands)
            queueHint.text = "Le premier d'entre eux arrivé dans l'arène affronte l'hôte ; les autres attendent leur tour sur la tribune.";
        else
            queueHint.text = rules.organization == DuelOrganization.KingOfTheHill
                ? "Ils attendent leur tour sur la tribune : le perdant de chaque match passe au bout de la file."
                : "Ils regardent le match depuis la tribune.";
    }

    private static VisualElement StandRow(Entry entry, int rank)
    {
        var row = new SlantBox();
        row.AddToClassList("queue-slot");
        if (entry.local) row.AddToClassList("queue-slot--self");
        var logo = new ArenaLogo();
        logo.AddToClassList("queue-slot__logo");
        logo.LogoColor = entry.paint;
        row.Add(logo);
        row.Add(NewLabel(entry.name, "queue-slot__name"));
        string status = entry.local ? $"TOI · EN FILE {rank}" : $"EN FILE {rank}";
        row.Add(NewLabel(status, "queue-slot__status"));
        return row;
    }

    private static VisualElement FreeRow()
    {
        var row = new SlantBox();
        row.AddToClassList("queue-slot");
        row.AddToClassList("queue-slot--empty");
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("queue-slot__empty-icon");
        row.Add(icon);
        row.Add(NewLabel("Emplacement libre", "queue-slot__name"));
        return row;
    }

    private static Label NewLabel(string text, string ussClass)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(ussClass);
        return label;
    }

    // ------------------------------------------------------------------
    // L'arène, le mode et les réglages
    // ------------------------------------------------------------------

    private void BuildArena()
    {
        MenuArtwork artwork = mainMenu != null ? mainMenu.Artwork : null;
        arenaName.text = artwork != null ? artwork.arenaName : "ARÈNE DE TEST";
        map?.SetMap(artwork);

        VisualElement legend = root.Q<VisualElement>("lobbyMapLegend");
        if (legend == null) return;
        legend.Clear();
        legend.Add(LegendItem("map-legend__swatch--spawn", "Points d'apparition A et B"));
        legend.Add(LegendItem("map-legend__swatch--walkway", "Tribune des spectateurs"));
        legend.Add(LegendItem("map-legend__swatch--rock", "Blocs (cailloux)"));
    }

    private static VisualElement LegendItem(string swatchClass, string text)
    {
        var item = new VisualElement();
        item.AddToClassList("map-legend__item");
        var swatch = new VisualElement();
        swatch.AddToClassList("map-legend__swatch");
        swatch.AddToClassList(swatchClass);
        item.Add(swatch);
        item.Add(NewLabel(text, "map-legend__label"));
        return item;
    }

    private void RenderRules(MatchRules rules, bool host, bool editable, int playerCount)
    {
        modeValue.text = GameModeCatalog.Describe(rules);
        // Les choix du sélecteur : BO5 (premier à 3), BO3 (premier à 2).
        roundsCycler.SetValueWithoutNotify(rules.roundsToWin == MatchRules.Bo3RoundsToWin ? 1 : 0);

        // On ne réduit pas les places sous le nombre de joueurs déjà là : les choix commencent à ce nombre.
        capacityFloor = Mathf.Clamp(playerCount, MatchRules.MinCapacity, MatchRules.MaxCapacity);
        var capacities = new List<string>();
        for (int c = capacityFloor; c <= MatchRules.MaxCapacity; c++) capacities.Add(c.ToString());
        capacityCycler.Choices = capacities;
        capacityCycler.SetValueWithoutNotify(Mathf.Max(0, rules.capacity - capacityFloor));
        organizationCycler.SetValueWithoutNotify(Array.FindIndex(Organizations, o => o.value == rules.organization));
        interventionsCycler.SetValueWithoutNotify(rules.spectatorInterventions ? 1 : 0);
        roundsCycler.SetEnabled(editable);
        capacityCycler.SetEnabled(editable);
        organizationCycler.SetEnabled(editable);
        interventionsCycler.SetEnabled(editable);

        rulesHint.text = host
            ? "Chaque changement s'affiche aussitôt chez les autres joueurs du salon."
            : "Seul l'hôte change les réglages.";
    }

    private void OnRoundsChanged(ChangeEvent<int> evt)
        => ChangeRules(r => { r.roundsToWin = (byte)(evt.newValue == 1 ? MatchRules.Bo3RoundsToWin : MatchRules.Bo5RoundsToWin); return r; });

    private void OnCapacityChanged(ChangeEvent<int> evt)
        => ChangeRules(r => { r.capacity = (byte)(capacityFloor + evt.newValue); return r; });

    private void OnOrganizationChanged(ChangeEvent<int> evt)
    {
        if (evt.newValue < 0 || evt.newValue >= Organizations.Length) return;
        DuelOrganization chosen = Organizations[evt.newValue].value;
        ChangeRules(r => { r.organization = chosen; return r; });
    }

    private void OnInterventionsChanged(ChangeEvent<int> evt)
        => ChangeRules(r => { r.spectatorInterventions = evt.newValue == 1; return r; });

    private void ChangeRules(Func<MatchRules, MatchRules> change)
    {
        if (!OnlineSession.IsHost || OnlineSession.GameStarted || launching) return;
        MatchRules rules = change(OnlineSession.Rules).Sanitized();
        GameSettings.HostRules = rules;  // retenus pour la prochaine partie, en ligne ou en réseau local
        OnlineSession.SetRules(rules);   // publiés aux autres (envoi regroupé)
    }

    /// <summary>Les réglages en une phrase, pour l'écran Jouer (réseau local). Fonction pure.</summary>
    public static string DescribeRules(MatchRules rules)
    {
        MatchRules r = rules.Sanitized();
        string organization = r.organization == DuelOrganization.FixedDuelists ? "duellistes fixes" : "roi de la colline";
        string interventions = r.spectatorInterventions ? "spectateurs actifs" : "spectateurs sans interventions";
        return $"Duel {r.Format}, {r.capacity} places, {organization}, {interventions}.";
    }

    // ------------------------------------------------------------------
    // Lancer, quitter, copier
    // ------------------------------------------------------------------

    private async void OnStartClicked()
    {
        if (busy || !OnlineSession.IsHost || OnlineSession.GameStarted) return;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null)
        {
            SetStatus("NetworkManager introuvable dans la scène.", true);
            return;
        }

        launching = true;
        SetBusy(true);
        SetStatus("Lancement de la partie...", false);
        SessionFlow.ActiveRules = OnlineSession.Rules;   // places, duels, spectateurs : figés pour la partie
        SessionFlow.PrepareConnectionData(nm);           // le pseudo a pu changer depuis le lancement

        try
        {
            await OnlineSession.StartGameAsync();
            if (this == null) return;

            // Netcode tourne : l'hôte emmène tout le monde dans l'arène (les connexions arrivées entre-temps
            // attendent qu'elle soit chargée, SessionFlow).
            SceneEventProgressStatus status = nm.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                launching = false;
                SetStatus($"La partie est lancée, mais l'arène ne se charge pas ({status}).", true);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[En ligne] Lancement de la partie : {e}");
            if (this == null) return;
            launching = false;
            SetStatus(OnlineSession.DescribeError(e), true);
        }
        finally
        {
            if (this != null) SetBusy(false);
        }
    }

    private async void OnLeaveClicked()
    {
        bool wasHost = OnlineSession.IsHost;
        launching = false;
        SetBusy(true);
        await OnlineSession.LeaveAsync();
        if (this == null) return;
        SetBusy(false);
        mainMenu.ShowScreenByName(MainMenuController.PlayScreen);
        mainMenu.ShowPlayStatus(wasHost ? "Salon fermé." : "Tu as quitté le salon.", false);
    }

    // Le salon s'est fermé sans nous (l'hôte l'a fermé, on en a été retiré, la connexion à la partie a échoué).
    private void OnSessionEnded(string message)
    {
        if (this == null) return;
        launching = false;
        if (lobbyScreen != null && lobbyScreen.ClassListContains("hidden")) return;
        mainMenu.ShowScreenByName(MainMenuController.PlayScreen);
        mainMenu.ShowPlayStatus(message, true);
    }

    private void CopyCode()
    {
        string code = OnlineSession.Code;
        if (string.IsNullOrEmpty(code)) return;
        GUIUtility.systemCopyBuffer = code;
        copyLabel.text = "COPIÉ !";
        copiedUntil = Time.unscaledTime + 1.5f;
    }

    private void SetBusy(bool value)
    {
        busy = value;
        mainMenu.SetOnlineBusy(value);
        Render();
    }

    private void SetStatus(string message, bool isError)
    {
        statusIsError = isError && !string.IsNullOrEmpty(message);
        statusLabel.text = message;
        statusLabel.EnableInClassList("da-status--error", isError);
    }
}
