using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Le salon d'une partie en ligne (2026-10-13, demandé par l'utilisateur ; GDD § 12 : « choix du mode, avec
/// ses réglages à côté → lobby → duel »). Sur MainMenuUI, à côté de MainMenuController, qui l'ouvre depuis
/// l'écran Jouer (Créer une partie, ou Rejoindre avec un code).
///
/// On y voit le code à donner, les joueurs arrivés (l'hôte, soi, les places libres), le mode et les
/// réglages de la partie : places, organisation des duels, interventions des spectateurs. Seul l'hôte les
/// change, et tous les voient changer ; il lance la partie quand il veut, même seul (pour s'échauffer).
/// Les réglages sont aussi retenus pour sa prochaine partie (GameSettings.HostRules), y compris en réseau
/// local.
///
/// Ce composant ne décide rien du réseau : il appelle OnlineSession et affiche ce que le salon publie.
/// </summary>
[DisallowMultipleComponent]
public class LobbyController : MonoBehaviour
{
    [SerializeField] private UIDocument document;
    [SerializeField] private MainMenuController mainMenu;

    [Tooltip("Nom de la scène de jeu à charger au lancement. Doit être dans les Build Settings.")]
    [SerializeField] private string gameplaySceneName = "MultiTestScene";

    private static readonly (DuelOrganization value, string label)[] Organizations =
    {
        (DuelOrganization.KingOfTheHill, "Roi de la colline"),
        (DuelOrganization.FixedDuelists, "Duellistes fixes"),
    };

    // Un seul mode existe (le BO5 de RoundManager) ; les autres se montrent, grisés, pour dire où le jeu va.
    private static readonly string[] ComingModes = { "Classé", "Capture", "Zone" };

    private VisualElement root;
    private VisualElement lobbyScreen;
    private Label roleLabel;
    private Label codeLabel;
    private Label playersTitle;
    private Label rulesHint;
    private Label statusLabel;
    private Button copyButton;
    private Button startButton;
    private Button leaveButton;
    private VisualElement playerList;
    private VisualElement modeRow;
    private VisualElement capacityRow;
    private VisualElement organizationRow;
    private Toggle interventionsToggle;

    private bool busy;        // une demande au service en cours (créer, rejoindre, lancer, quitter)
    private bool launching;   // l'hôte a lancé : on attend l'arène
    private bool statusIsError;
    private float copiedUntil;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        if (mainMenu == null) mainMenu = GetComponent<MainMenuController>();
        root = document.rootVisualElement;

        lobbyScreen = root.Q<VisualElement>("screenLobby");
        roleLabel = root.Q<Label>("lobbyRoleLabel");
        codeLabel = root.Q<Label>("lobbyCodeLabel");
        playersTitle = root.Q<Label>("lobbyPlayersTitle");
        rulesHint = root.Q<Label>("lobbyRulesHint");
        statusLabel = root.Q<Label>("lobbyStatusLabel");
        copyButton = root.Q<Button>("lobbyCopyButton");
        startButton = root.Q<Button>("lobbyStartButton");
        leaveButton = root.Q<Button>("lobbyLeaveButton");
        playerList = root.Q<VisualElement>("lobbyPlayerList");
        modeRow = root.Q<VisualElement>("lobbyModeRow");
        capacityRow = root.Q<VisualElement>("lobbyCapacityRow");
        organizationRow = root.Q<VisualElement>("lobbyOrganizationRow");
        interventionsToggle = root.Q<Toggle>("lobbyInterventionsToggle");

        BuildRuleControls();
        copyButton.clicked += CopyCode;
        startButton.clicked += OnStartClicked;
        leaveButton.clicked += OnLeaveClicked;
        interventionsToggle.RegisterValueChangedCallback(OnInterventionsChanged);

        OnlineSession.Changed += Render;
        OnlineSession.Ended += OnSessionEnded;
    }

    private void OnDisable()
    {
        copyButton.clicked -= CopyCode;
        startButton.clicked -= OnStartClicked;
        leaveButton.clicked -= OnLeaveClicked;
        interventionsToggle.UnregisterValueChangedCallback(OnInterventionsChanged);

        OnlineSession.Changed -= Render;
        OnlineSession.Ended -= OnSessionEnded;
    }

    private void Update()
    {
        if (copiedUntil > 0f && Time.unscaledTime > copiedUntil)
        {
            copiedUntil = 0f;
            copyButton.text = "Copier";
        }
    }

    // ------------------------------------------------------------------
    // Entrer dans un salon (depuis l'écran Jouer)
    // ------------------------------------------------------------------

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
        mainMenu.ShowScreenByName("screenLobby");
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

        bool host = session.IsHost;
        bool started = OnlineSession.GameStarted || launching;
        MatchRules rules = OnlineSession.Rules;

        codeLabel.text = string.IsNullOrEmpty(session.Code) ? "······" : session.Code;
        roleLabel.text = host
            ? "Tu es l'hôte : tu règles la partie et tu la lances."
            : $"Salon de {HostName(session)}. L'hôte règle la partie et la lance.";

        RenderPlayers(session, rules.capacity);
        RenderRules(rules, host && !started, session.PlayerCount);

        startButton.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
        startButton.SetEnabled(host && !busy && !started);
        leaveButton.SetEnabled(!busy || launching);

        if (started) SetStatus(host ? "Lancement de la partie..." : "La partie démarre...", false);
        else if (!statusIsError) SetStatus(host ? "" : "En attente du lancement par l'hôte...", false);
    }

    private void RenderPlayers(ISession session, int capacity)
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
        playerList.Clear();
        for (int i = 0; i < players.Count; i++)
        {
            IReadOnlyPlayer player = players[i];
            playerList.Add(PlayerRow(DisplayName(player, i), player.Id == hostId, player.Id == localId));
        }
        for (int i = players.Count; i < capacity; i++) playerList.Add(EmptyRow());

        playersTitle.text = $"JOUEURS  {players.Count}/{capacity}";
    }

    private static VisualElement PlayerRow(string name, bool isHost, bool isLocal)
    {
        var row = new VisualElement();
        row.AddToClassList("lobby-player");
        var label = new Label(name);
        label.AddToClassList("lobby-player-name");
        row.Add(label);
        if (isHost) row.Add(Tag("HÔTE", true));
        if (isLocal) row.Add(Tag("TOI", false));
        return row;
    }

    private static VisualElement EmptyRow()
    {
        var row = new VisualElement();
        row.AddToClassList("lobby-player");
        row.AddToClassList("lobby-player--empty");
        var label = new Label("Place libre");
        label.AddToClassList("lobby-player-name");
        row.Add(label);
        return row;
    }

    private static Label Tag(string text, bool host)
    {
        var tag = new Label(text);
        tag.AddToClassList("lobby-tag");
        if (host) tag.AddToClassList("lobby-tag--host");
        return tag;
    }

    private static string DisplayName(IReadOnlyPlayer player, int index)
    {
        string name = OnlineSession.NameOf(player);
        return string.IsNullOrEmpty(name) ? $"Joueur {index + 1}" : name;
    }

    private static string HostName(ISession session)
    {
        var players = new List<IReadOnlyPlayer>(session.Players);
        int index = players.FindIndex(p => p.Id == session.Host);
        return index < 0 ? "l'hôte" : DisplayName(players[index], 0);
    }

    // ------------------------------------------------------------------
    // Le mode et les réglages
    // ------------------------------------------------------------------

    private void BuildRuleControls()
    {
        modeRow.Clear();
        var duel = new Button { text = "Duel BO5" };
        duel.AddToClassList("rule-chip");
        duel.AddToClassList("rule-chip--active");
        duel.tooltip = "Premier à 3 manches gagnées.";
        modeRow.Add(duel);
        foreach (string mode in ComingModes)
        {
            var chip = new Button { text = mode };
            chip.AddToClassList("rule-chip");
            chip.tooltip = "Mode à venir.";
            chip.SetEnabled(false);
            modeRow.Add(chip);
        }

        capacityRow.Clear();
        for (int capacity = MatchRules.MinCapacity; capacity <= MatchRules.MaxCapacity; capacity++)
        {
            int chosen = capacity;
            var chip = new Button(() => ChangeRules(r => { r.capacity = (byte)chosen; return r; })) { text = capacity.ToString() };
            chip.AddToClassList("rule-chip");
            capacityRow.Add(chip);
        }

        organizationRow.Clear();
        foreach ((DuelOrganization value, string label) in Organizations)
        {
            DuelOrganization chosen = value;
            var chip = new Button(() => ChangeRules(r => { r.organization = chosen; return r; })) { text = label };
            chip.AddToClassList("rule-chip");
            organizationRow.Add(chip);
        }
    }

    private void RenderRules(MatchRules rules, bool editable, int playerCount)
    {
        for (int i = 0; i < capacityRow.childCount; i++)
        {
            int capacity = i + MatchRules.MinCapacity;
            capacityRow[i].EnableInClassList("rule-chip--active", capacity == rules.capacity);
            // On ne réduit pas les places sous le nombre de joueurs déjà là.
            capacityRow[i].SetEnabled(editable && capacity >= playerCount);
        }
        for (int i = 0; i < organizationRow.childCount && i < Organizations.Length; i++)
        {
            organizationRow[i].EnableInClassList("rule-chip--active", Organizations[i].value == rules.organization);
            organizationRow[i].SetEnabled(editable);
        }
        interventionsToggle.SetValueWithoutNotify(rules.spectatorInterventions);
        interventionsToggle.SetEnabled(editable);

        rulesHint.text = OnlineSession.IsHost
            ? "Chaque changement s'affiche aussitôt chez les autres joueurs du salon."
            : "Seul l'hôte change les réglages.";
    }

    private void OnInterventionsChanged(ChangeEvent<bool> evt)
        => ChangeRules(r => { r.spectatorInterventions = evt.newValue; return r; });

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
        return $"Duel BO5, {r.capacity} places, {organization}, {interventions}.";
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
        mainMenu.ShowScreenByName("screenPlay");
        mainMenu.ShowPlayStatus(wasHost ? "Salon fermé." : "Tu as quitté le salon.", false);
    }

    // Le salon s'est fermé sans nous (l'hôte l'a fermé, on en a été retiré, la connexion à la partie a échoué).
    private void OnSessionEnded(string message)
    {
        if (this == null) return;
        launching = false;
        if (lobbyScreen != null && lobbyScreen.ClassListContains("hidden")) return;
        mainMenu.ShowScreenByName("screenPlay");
        mainMenu.ShowPlayStatus(message, true);
    }

    private void CopyCode()
    {
        string code = OnlineSession.Code;
        if (string.IsNullOrEmpty(code)) return;
        GUIUtility.systemCopyBuffer = code;
        copyButton.text = "Copié !";
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
        statusLabel.EnableInClassList("status-label--error", isError);
    }
}
