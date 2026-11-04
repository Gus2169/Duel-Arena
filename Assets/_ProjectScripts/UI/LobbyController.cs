using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Le salon d'une partie en ligne (2026-10-13, demandé par l'utilisateur ; GDD § 12 : « choix du mode, avec
/// ses réglages à côté → lobby → duel »), refait le 2026-11-02 d'après la maquette qu'il a retenue (« B ·
/// Salon ») : les joueurs en liste, les réglages (les essentiels, puis « Plus de réglages »), l'image de
/// l'arène ; l'écran est SalonView, le même que le salon de l'arène (SalonPanel). Sur MainMenuUI, à côté de
/// MainMenuController ; ouvert depuis l'écran Jouer (une carte de mode, ou Rejoindre avec un code).
///
/// - Les duellistes sont l'hôte et le deuxième arrivé au salon (LobbyLineup ; l'arène suit cet ordre, voir
///   SessionFlow.LaunchOrder), puis la tribune dans l'ordre d'arrivée. Avec les duellistes tirés au sort,
///   tous sont « dans le tirage » : il se fait au lancement.
/// - Chacun a la couleur qu'il aura vraiment : celle qu'il a choisie si personne avant lui ne l'a prise,
///   sinon la première libre (PlayerPaint.PickPaint, comme le serveur).
/// - Les réglages, que seul l'hôte change et que tous voient changer, puis le code à donner.
/// - Au Tag Team, les équipes que l'hôte compose (un clic sur un joueur, ou au hasard), publiées dans le salon
///   (OnlineSession.Teams) et emmenées dans l'arène au lancement (SessionFlow.LaunchTeams) ; tant qu'il n'en a pas
///   composé, A et B en alternance dans l'ordre du salon (TagTeam.AutoTeams).
///
/// Ce composant ne décide rien du réseau : il appelle OnlineSession et affiche ce que le salon publie.
/// </summary>
[DisallowMultipleComponent]
public class LobbyController : MonoBehaviour
{
    [SerializeField] private UIDocument document;
    [SerializeField] private MainMenuController mainMenu;

    [Tooltip("La palette des robots, lue sur le préfab joueur (sur le même GameObject si vide).")]
    [SerializeField] private CharacterPreviewController characterPreview;

    [Tooltip("Nom de la scène de jeu à charger au lancement. Doit être dans les Build Settings.")]
    [SerializeField] private string gameplaySceneName = "MultiTestScene";

    /// <summary>Un joueur du salon, tel que l'écran le montre.</summary>
    public readonly struct Entry
    {
        public readonly string id;
        public readonly string name;
        public readonly Color32 paint;
        public readonly bool host, local;

        public Entry(string id, string name, Color32 paint, bool host, bool local)
        {
            this.id = id;
            this.name = name;
            this.paint = paint;
            this.host = host;
            this.local = local;
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
        /// <summary>Au Tag Team, les équipes composées par l'hôte, par identifiant de joueur (vides : celles par défaut).</summary>
        public Dictionary<string, PlayerTeam> teams;
    }

    private VisualElement root;
    private VisualElement lobbyScreen;
    private SalonView salon;

    private bool busy;        // une demande au service en cours (créer, rejoindre, lancer, quitter)
    private bool launching;   // l'hôte a lancé : on attend l'arène
    private string statusText = "";
    private bool statusIsError;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        if (mainMenu == null) mainMenu = GetComponent<MainMenuController>();
        if (characterPreview == null) characterPreview = GetComponent<CharacterPreviewController>();
        root = document.rootVisualElement;
        lobbyScreen = root.Q<VisualElement>("screenLobby");

        salon = new SalonView(lobbyScreen);
        salon.RulesChanged += ChangeRules;
        salon.LaunchClicked += OnStartClicked;
        salon.LeaveClicked += OnLeaveClicked;
        salon.PlayerClicked += ChangeTeam;
        salon.ShuffleClicked += ShuffleTeams;
        MenuArtwork artwork = mainMenu != null ? mainMenu.Artwork : null;
        salon.SetArena(artwork != null ? artwork.arenaOverview : null, artwork != null ? artwork.arenaName : "ARÈNE DE TEST");

        OnlineSession.Changed += Render;
        OnlineSession.Ended += OnSessionEnded;
    }

    private void OnDisable()
    {
        salon?.Dispose();
        salon = null;
        OnlineSession.Changed -= Render;
        OnlineSession.Ended -= OnSessionEnded;
    }

    private void Update() => salon?.Tick(Time.unscaledTime);

    /// <summary>Échap : ferme la fenêtre « Changer de mode » si elle est ouverte (MainMenuController).</summary>
    public bool CloseWindow() => salon != null && salon.CloseWindow();

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
        if (this == null || root == null || salon == null) return;
        ISession session = OnlineSession.Current;
        if (session == null) return;
        Show(new View
        {
            host = session.IsHost,
            started = OnlineSession.GameStarted || launching,
            rules = OnlineSession.Rules,
            code = session.Code,
            players = Entries(session),
            teams = OnlineSession.Teams,
        });
    }

#if UNITY_EDITOR
    /// <summary>Montre l'écran du salon avec ces données, sans le service : pour voir l'écran dans tous ses
    /// états (seul, à deux, à six) lors des captures.</summary>
    public void ShowPreview(View view)
    {
        mainMenu.ShowScreenByName(MainMenuController.LobbyScreen);
        Show(view);
    }
#endif

    private void Show(View view)
    {
        if (salon == null) return;
        List<Entry> players = view.players ?? new List<Entry>();
        MatchRules rules = view.rules.Sanitized();
        shownView = view;
        if (rules.IsTagTeam)
        {
            ShowTeams(view, players, rules);
            return;
        }
        if (rules.IsTournament)
        {
            ShowTournament(view, players, rules);
            return;
        }
        var lineup = LobbyLineup<Entry>.Arrange(players, rules.capacity, rules.randomDuelists);

        // Les duellistes d'abord (l'hôte et le deuxième arrivé), puis la tribune dans l'ordre d'arrivée.
        var shown = new List<SalonView.Player>();
        if (lineup.hasFirst) shown.Add(Shown(lineup.first, true, 0, false));
        if (lineup.secondKnown) shown.Add(Shown(lineup.second, true, 0, false));
        for (int i = 0; i < lineup.stands.Count; i++) shown.Add(Shown(lineup.stands[i], false, i + 1, lineup.drawn));

        string hostName = "";
        foreach (Entry entry in players)
        {
            if (entry.host) hostName = entry.name;
        }

        string status = statusText;
        bool error = statusIsError;
        if (view.started)
        {
            status = view.host ? "Lancement de la partie…" : "La partie démarre…";
            error = false;
        }
        else if (!statusIsError)
        {
            status = !view.host ? "En attente du lancement par l'hôte…"
                : players.Count < DuelRoster.DuelistSlots ? "Tu peux lancer seul : les autres te rejoindront dans l'arène."
                : "Lance la partie quand tu veux.";
        }

        salon.Show(new SalonView.Model
        {
            eyebrow = string.IsNullOrEmpty(hostName) ? "SALON · EN LIGNE" : $"SALON DE {hostName.ToUpperInvariant()} · EN LIGNE",
            mode = GameModeCatalog.ModeOf(rules),
            rules = rules,
            host = view.host,
            editable = view.host && !view.started,
            inviteCaption = "CODE",
            inviteValue = string.IsNullOrEmpty(view.code) ? "······" : view.code,
            players = shown,
            phrase = SalonView.Phrase(players.Count,
                lineup.hasFirst ? lineup.first.name.ToUpperInvariant() : null,
                lineup.secondKnown ? lineup.second.name.ToUpperInvariant() : null,
                lineup.drawn, rules.organization, rules.capacity > DuelRoster.DuelistSlots, "le code du salon"),
            status = status,
            statusError = error,
            canLaunch = view.host && !busy && !view.started,
            leaveLabel = "QUITTER LE SALON",
        });
    }

    // Ce que le salon montre, pour savoir qui l'hôte a cliqué (le Tag Team).
    private View shownView;

    /// <summary>Le salon du Tag Team : les joueurs dans l'ordre du salon, rangés par équipe par SalonView ; chacun sa
    /// place dans le relais de son équipe.</summary>
    private void ShowTeams(View view, List<Entry> players, MatchRules rules)
    {
        PlayerTeam[] teams = TeamsOf(players, view.teams);
        var shown = new List<SalonView.Player>();
        int teamA = 0, teamB = 0;
        string hostName = "", firstA = null, firstB = null;
        for (int i = 0; i < players.Count; i++)
        {
            Entry entry = players[i];
            if (entry.host) hostName = entry.name;
            int place = teams[i] == PlayerTeam.A ? teamA++ : teams[i] == PlayerTeam.B ? teamB++ : 0;
            if (teams[i] == PlayerTeam.A && place == 0) firstA = entry.name.ToUpperInvariant();
            if (teams[i] == PlayerTeam.B && place == 0) firstB = entry.name.ToUpperInvariant();
            shown.Add(new SalonView.Player
            {
                name = entry.name,
                paint = entry.paint,
                role = SalonView.TeamRole(teams[i], place, entry.host, entry.local),
                local = entry.local,
                team = teams[i],
            });
        }

        bool ready = TagTeam.CanPlay(teamA, teamB);
        string status = statusText;
        bool error = statusIsError;
        if (view.started)
        {
            status = view.host ? "Lancement de la partie…" : "La partie démarre…";
            error = false;
        }
        else if (!statusIsError)
        {
            status = !view.host ? "En attente du lancement par l'hôte…"
                : ready ? "Lance la partie quand tu veux."
                : "Il faut deux équipes égales, de deux ou trois joueurs : clique un joueur pour le changer d'équipe.";
        }

        salon.Show(new SalonView.Model
        {
            eyebrow = string.IsNullOrEmpty(hostName) ? "SALON · EN LIGNE" : $"SALON DE {hostName.ToUpperInvariant()} · EN LIGNE",
            mode = GameModeCatalog.ModeOf(rules),
            rules = rules,
            host = view.host,
            editable = view.host && !view.started,
            inviteCaption = "CODE",
            inviteValue = string.IsNullOrEmpty(view.code) ? "······" : view.code,
            players = shown,
            phrase = SalonView.TeamPhrase(teamA, teamB, firstA, firstB),
            status = status,
            statusError = error,
            canLaunch = view.host && !busy && !view.started && ready,
            leaveLabel = "QUITTER LE SALON",
            teams = true,
            teamsEditable = view.host && !view.started,
        });
    }

    /// <summary>L'équipe de chaque joueur (dans l'ordre du salon) : celle que l'hôte lui a donnée, aucune pour un
    /// nouveau venu ; sans composition de l'hôte, A et B en alternance (TagTeam.AutoTeams). Fonction pure, testée.</summary>
    public static PlayerTeam[] TeamsOf(IReadOnlyList<Entry> players, IReadOnlyDictionary<string, PlayerTeam> composed)
    {
        int count = players != null ? players.Count : 0;
        if (composed == null || composed.Count == 0) return TagTeam.AutoTeams(count);
        var teams = new PlayerTeam[count];
        for (int i = 0; i < count; i++)
            teams[i] = composed.TryGetValue(players[i].id ?? "", out PlayerTeam team) ? team : PlayerTeam.None;
        return teams;
    }

    /// <summary>La composition des équipes à publier : chaque joueur du salon et son équipe.</summary>
    private static List<(string id, PlayerTeam team)> Composition(IReadOnlyList<Entry> players, PlayerTeam[] teams)
    {
        var composition = new List<(string id, PlayerTeam team)>();
        for (int i = 0; i < players.Count; i++) composition.Add((players[i].id, teams[i]));
        return composition;
    }

    /// <summary>L'hôte clique un joueur du Tag Team : il passe dans l'autre équipe, puis hors équipe (TagTeam.NextTeam).</summary>
    private void ChangeTeam(int index)
    {
        if (!OnlineSession.IsHost || OnlineSession.GameStarted || launching) return;
        List<Entry> players = shownView.players;
        if (players == null || index < 0 || index >= players.Count) return;
        PlayerTeam[] teams = TeamsOf(players, shownView.teams);
        int teamA = 0, teamB = 0;
        foreach (PlayerTeam team in teams)
        {
            if (team == PlayerTeam.A) teamA++;
            else if (team == PlayerTeam.B) teamB++;
        }
        teams[index] = TagTeam.NextTeam(teams[index], teamA, teamB);
        OnlineSession.SetTeams(Composition(players, teams));
    }

    /// <summary>L'hôte tire les équipes au sort (« Équipes au hasard », GDD § 11, TT1).</summary>
    private void ShuffleTeams()
    {
        if (!OnlineSession.IsHost || OnlineSession.GameStarted || launching) return;
        List<Entry> players = shownView.players;
        if (players == null) return;
        OnlineSession.SetTeams(Composition(players, TagTeam.Shuffled(players.Count, new System.Random())));
    }

    /// <summary>Le salon du Tournoi : tous y jouent, dans l'ordre d'arrivée ; on le lance à trois au moins.</summary>
    private void ShowTournament(View view, List<Entry> players, MatchRules rules)
    {
        var shown = new List<SalonView.Player>();
        string hostName = "";
        foreach (Entry entry in players)
        {
            if (entry.host) hostName = entry.name;
            shown.Add(new SalonView.Player
            {
                name = entry.name,
                paint = entry.paint,
                role = SalonView.TournamentRole(entry.host, entry.local),
                local = entry.local,
            });
        }
        bool enough = players.Count >= Tournament.MinPlayers;
        string status = statusText;
        bool error = statusIsError;
        if (view.started)
        {
            status = view.host ? "Lancement du tournoi…" : "Le tournoi démarre…";
            error = false;
        }
        else if (!statusIsError)
        {
            status = !view.host ? "En attente du lancement par l'hôte…"
                : enough ? "Lance le tournoi quand tout le monde est là."
                : "Il faut trois joueurs au moins pour lancer le tournoi.";
        }
        salon.Show(new SalonView.Model
        {
            eyebrow = string.IsNullOrEmpty(hostName) ? "SALON · EN LIGNE" : $"SALON DE {hostName.ToUpperInvariant()} · EN LIGNE",
            mode = GameModeCatalog.ModeOf(rules),
            rules = rules,
            host = view.host,
            editable = view.host && !view.started,
            inviteCaption = "CODE",
            inviteValue = string.IsNullOrEmpty(view.code) ? "······" : view.code,
            players = shown,
            phrase = SalonView.TournamentPhrase(players.Count),
            status = status,
            statusError = error,
            canLaunch = view.host && !busy && !view.started && enough,
            leaveLabel = "QUITTER LE SALON",
        });
    }

    private static SalonView.Player Shown(Entry entry, bool duelist, int queuePosition, bool drawn) => new SalonView.Player
    {
        name = entry.name,
        paint = entry.paint,
        role = SalonView.Role(duelist, queuePosition, drawn, entry.host, entry.local),
        local = entry.local,
    };

#if UNITY_EDITOR
    /// <summary>Un joueur pour un aperçu de l'écran (ShowPreview).</summary>
    public static Entry PreviewEntry(string name, Color32 paint, bool host, bool local) => new Entry(name, name, paint, host, local);
#endif

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
            entries.Add(new Entry(player.Id, string.IsNullOrEmpty(name) ? $"Joueur {i + 1}" : name, paint, player.Id == hostId, player.Id == localId));
        }
        return entries;
    }

    private IReadOnlyList<Color32> Palette => characterPreview != null ? characterPreview.Palette : Array.Empty<Color32>();

    // ------------------------------------------------------------------
    // Les réglages
    // ------------------------------------------------------------------

    private void ChangeRules(MatchRules rules)
    {
        if (!OnlineSession.IsHost || OnlineSession.GameStarted || launching) return;
        rules = rules.Sanitized();
        GameSettings.HostRules = rules;  // retenus pour la prochaine partie, en ligne ou en réseau local
        OnlineSession.SetRules(rules);   // publiés aux autres (envoi regroupé)
    }

    /// <summary>Les réglages en une phrase, pour l'écran Jouer (réseau local). Fonction pure.</summary>
    public static string DescribeRules(MatchRules rules)
    {
        MatchRules r = rules.Sanitized();
        string organization = r.organization == DuelOrganization.FixedDuelists ? "duellistes fixes" : "roi de la colline";
        string interventions = r.spectatorInterventions ? "spectateurs actifs" : "spectateurs sans interventions";
        string draw = r.randomDuelists ? ", duellistes tirés au sort" : "";
        // Le classique dit « Duel BO5 » ; un autre mode se nomme (« Gun Game, grande échelle »).
        string mode = r.mode == GameModeId.Classic ? $"Duel {r.Format}" : $"{GameModeCatalog.Name(r.mode)}, {r.Format}";
        return $"{mode}, {r.capacity} places, {organization}, {interventions}{draw}.";
    }

    // ------------------------------------------------------------------
    // Lancer, quitter
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
        // L'ordre du salon (les duellistes en tête, tirés au sort avec le tirage), que l'arène suivra : tous
        // s'y connectent à la fois, et l'ordre d'arrivée n'y dit plus rien (2026-10-27).
        SessionFlow.LaunchOrder.Clear();
        MatchRules launched = OnlineSession.Rules.Sanitized();
        SessionFlow.LaunchOrder.AddRange(OnlineSession.LaunchOrder(launched.randomDuelists && !launched.IsTagTeam, new System.Random()));
        // Au Tag Team, les équipes du salon : l'arène les reprend (RoundManager), chacune dans l'ordre du salon.
        SessionFlow.LaunchTeams.Clear();
        if (launched.IsTagTeam && shownView.players != null)
        {
            PlayerTeam[] teams = TeamsOf(shownView.players, OnlineSession.Teams);
            for (int i = 0; i < shownView.players.Count; i++) SessionFlow.LaunchTeams[shownView.players[i].id] = teams[i];
        }
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
            SessionFlow.LaunchOrder.Clear();
            SessionFlow.LaunchTeams.Clear();
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
        // Pendant une demande au service, on ne quitte pas, sauf pour renoncer au lancement.
        if (busy && !launching) return;
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

    private void SetBusy(bool value)
    {
        busy = value;
        mainMenu.SetOnlineBusy(value);
        Render();
    }

    private void SetStatus(string message, bool isError)
    {
        statusIsError = isError && !string.IsNullOrEmpty(message);
        statusText = message ?? "";
        Render();
    }
}
