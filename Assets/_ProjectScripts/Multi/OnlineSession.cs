using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// La partie en ligne (2026-10-13, GDD § 4 : « uniquement des lobbys privés avec un code ») : un salon privé
/// qu'on rejoint par un code, puis la connexion de jeu par les relais d'Unity, qui évitent d'ouvrir des ports
/// et ne montrent l'adresse de personne. Paquet com.unity.services.multiplayer (les sessions).
///
/// Le déroulé :
/// - CRÉER : l'hôte ouvre un salon, sans réseau de jeu, avec la version du jeu et les réglages de la partie
///   en propriétés partagées. Le service lui donne un code.
/// - REJOINDRE : un ami tape le code et entre dans le salon ; sa version est comparée à celle de l'hôte.
/// - LANCER : l'hôte démarre le réseau de jeu (StartRelayNetworkAsync). Le paquet démarre Netcode chez lui
///   (StartHost), publie de quoi se connecter dans le salon, et chaque membre se connecte tout seul
///   (StartClient). L'hôte charge l'arène, Netcode y emmène les autres.
/// - Le code reste valable pendant la partie : qui arrive ensuite entre directement dans l'arène, et attend
///   son tour sur la tribune comme en réseau local.
///
/// Netcode garde l'approbation de chaque connexion (SessionFlow : capacité, version, pseudo). Pour partir,
/// on quitte LE SALON (LeaveAsync, DeleteAsync pour l'hôte), qui arrête Netcode proprement : jamais
/// NetworkManager.Shutdown tant qu'un salon existe (le paquet le signale, et son pilote ne serait pas libéré).
///
/// La capacité : le nombre de places d'un salon est fixé à sa création. Il en compte donc six
/// (MatchRules.MaxCapacity), et la capacité choisie par l'hôte est tenue par le verrou du salon (fermé dès
/// qu'il est complet) et, si deux joueurs entrent à la même seconde, par le retrait du dernier arrivé.
///
/// Tout est statique : le salon survit aux changements de scène, et le NetworkManager qui voyage est détruit
/// au retour au menu (SessionFlow).
/// </summary>
public static class OnlineSession
{
    /// <summary>Propriétés du salon : la version du jeu de l'hôte, et les réglages de la partie.</summary>
    public const string VersionKey = "version";
    public const string RulesKey = "regles";

    /// <summary>Propriété de chaque joueur : son pseudo, tel qu'il l'a tapé (nettoyé).</summary>
    public const string NameKey = "pseudo";

    /// <summary>Propriété de chaque joueur : la couleur choisie pour son robot (son rang dans la palette, -1
    /// sans choix), pour les portraits du salon (2026-10-17).</summary>
    public const string ColorKey = "couleur";
    /// <summary>Le châssis choisi par chaque joueur (son rang, ChassisId), pour les portraits du salon (2026-10-26).</summary>
    public const string ChassisKey = "chassis";

    /// <summary>Le salon de cette machine, null hors d'une partie en ligne.</summary>
    public static ISession Current { get; private set; }

    public static bool InSession => Current != null;
    public static bool IsHost => Current != null && Current.IsHost;

    /// <summary>Le code à donner aux autres, vide hors d'un salon.</summary>
    public static string Code => Current != null ? Current.Code ?? "" : "";

    /// <summary>Le réseau de jeu de ce salon est-il lancé (ou en train de l'être) ?</summary>
    public static bool GameStarted => Current != null && Current.Network != null && Current.Network.State != NetworkState.Stopped;

    /// <summary>Le salon a changé : un joueur est arrivé ou parti, un réglage a bougé, la partie démarre.</summary>
    public static event Action Changed;

    /// <summary>Le salon s'est fermé sans qu'on le quitte (l'hôte l'a fermé, on en a été retiré, la
    /// connexion à la partie a échoué), avec ce qu'on dit au joueur.</summary>
    public static event Action<string> Ended;

    // Côté hôte : les réglages publiés, qui font la capacité tenue par le verrou.
    private static MatchRules hostRules = MatchRules.Default;
    private static bool leaving;
    private static bool savingHostState;
    private static bool saveHostStateAgain;
    private static Task signInTask;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Le rechargement de domaine est coupé à l'entrée en Play Mode : rien ne doit survivre d'une session
        // de test à l'autre. Le paquet, lui, quitte ses salons quand on sort du Play Mode.
        Current = null;
        Changed = null;
        Ended = null;
        hostRules = MatchRules.Default;
        leaving = false;
        savingHostState = false;
        saveHostStateAgain = false;
        signInTask = null;
    }

    // ------------------------------------------------------------------
    // Connexion aux services
    // ------------------------------------------------------------------

    /// <summary>
    /// Initialise les services d'Unity et connecte le joueur, anonymement (aucun compte à créer). Une seule
    /// fois par lancement ; à refaire si la tentative précédente a échoué (pas d'internet...).
    /// </summary>
    public static Task EnsureSignedInAsync()
    {
        if (signInTask == null || signInTask.IsFaulted || signInTask.IsCanceled
            || (signInTask.IsCompleted && !AuthenticationService.Instance.IsSignedIn))
        {
            signInTask = SignInAsync();
        }
        return signInTask;
    }

    private static async Task SignInAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var options = new InitializationOptions();
            options.SetProfile(ProfileName());
            await UnityServices.InitializeAsync(options);
        }
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        Debug.Log($"[En ligne] Connecté aux services (profil {AuthenticationService.Instance.Profile}).");
    }

    /// <summary>
    /// Le profil de connexion de cette instance du jeu. Toutes les instances d'un PC partagent le registre de
    /// Windows, où le service garde le joueur anonyme de chaque profil : deux instances au même profil
    /// seraient LE MÊME joueur, et la seconde ne pourrait pas entrer dans le salon de la première (même piège
    /// que le pseudo, CLAUDE.md). L'Editor et son clone ont chacun le leur ; une build prend la première
    /// place libre du PC (verrou système par place, tenu jusqu'à la fermeture du jeu).
    /// </summary>
    public static string ProfileName()
    {
#if UNITY_EDITOR
        bool clone = Array.IndexOf(Environment.GetCommandLineArgs(), "--virtual-project-clone") >= 0;
        return ProfileFor(true, clone, clone ? StableHash(Application.dataPath) % 10000 : 0);
#else
        return ProfileFor(false, false, AcquireInstanceSlot());
#endif
    }

    /// <summary>Le nom du profil : un par instance possible du jeu sur un PC. Respecte la règle du service
    /// (lettres, chiffres, « _ » et « - », 30 caractères au plus). Fonction pure, testée.</summary>
    public static string ProfileFor(bool editor, bool clone, int slot)
    {
        if (editor) return clone ? "editeur_clone_" + Mathf.Abs(slot) : "editeur";
        return slot <= 0 ? "joueur" : "joueur_" + slot;
    }

    private static int StableHash(string text)
    {
        // FNV-1a : string.GetHashCode n'est pas garanti stable d'un lancement à l'autre.
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in text ?? "") hash = (hash ^ c) * 16777619;
            return (int)(hash & 0x7FFFFFFF);
        }
    }

#if !UNITY_EDITOR
    private static System.Threading.Mutex instanceMutex;
    private static int instanceSlot = -1;

    private static int AcquireInstanceSlot()
    {
        // Le verrou est gardé (jamais relâché) jusqu'à la fermeture du jeu : c'est lui qui tient la place.
        if (instanceMutex != null || instanceSlot >= 0) return Mathf.Max(0, instanceSlot);
        for (int slot = 0; slot < 16; slot++)
        {
            try
            {
                var mutex = new System.Threading.Mutex(true, "DuelArena.Instance." + slot, out bool created);
                if (created)
                {
                    instanceMutex = mutex;
                    instanceSlot = slot;
                    return slot;
                }
                mutex.Dispose();
            }
            catch (Exception)
            {
                // Plateforme sans verrou nommé : une seule instance par machine, le cas courant.
                break;
            }
        }
        instanceSlot = 0;
        return 0;
    }
#endif

    // ------------------------------------------------------------------
    // Créer, rejoindre, quitter
    // ------------------------------------------------------------------

    /// <summary>Ouvre un salon privé, dont cette machine est l'hôte. Le réseau de jeu n'y démarre qu'au
    /// lancement (StartGameAsync).</summary>
    public static async Task<ISession> CreateAsync(MatchRules rules)
    {
        await EnsureSignedInAsync();
        await LeaveAsync();

        hostRules = rules.Sanitized();
        var options = new SessionOptions
        {
            MaxPlayers = MatchRules.MaxCapacity,
            IsPrivate = true,
            SessionProperties = new Dictionary<string, SessionProperty>
            {
                [VersionKey] = new SessionProperty(Application.version, VisibilityPropertyOptions.Member),
                [RulesKey] = new SessionProperty(FormatRules(hostRules), VisibilityPropertyOptions.Member),
            },
            PlayerProperties = LocalPlayerProperties(),
        };

        IHostSession session = await MultiplayerService.Instance.CreateSessionAsync(options);
        Attach(session);
        Debug.Log($"[En ligne] Salon créé, code {session.Code}.");
        return session;
    }

    /// <summary>
    /// Entre dans le salon de ce code. Si la partie est déjà lancée, le paquet connecte aussi Netcode, et la
    /// tâche ne se termine qu'une fois le joueur dans l'arène : l'appelant (le menu) est alors détruit.
    /// </summary>
    public static async Task<ISession> JoinAsync(string code)
    {
        string normalized = NormalizeCode(code);
        if (normalized.Length == 0) throw new OnlineSessionException("Tape le code que l'hôte t'a donné.");

        await EnsureSignedInAsync();
        await LeaveAsync();
        // La demande de connexion porte l'identifiant du joueur, connu maintenant : si la partie est lancée,
        // Netcode se connecte pendant l'entrée dans le salon, et l'hôte y retrouve sa place (SessionFlow).
        SessionFlow.PrepareConnectionData(NetworkManager.Singleton);

        var options = new JoinSessionOptions { PlayerProperties = LocalPlayerProperties() };
        ISession session = await MultiplayerService.Instance.JoinSessionByCodeAsync(normalized, options);

        // Une autre build que celle de l'hôte : refusée tout de suite, avec la même raison que Netcode
        // donnerait au lancement (SessionFlow.ShouldApprove).
        string hostVersion = PropertyValue(session.Properties, VersionKey);
        if (!SessionFlow.ShouldApprove(0, 1, Application.version, hostVersion, out string reason))
        {
            try { await session.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning($"[En ligne] Sortie du salon après refus : {e.Message}"); }
            throw new OnlineSessionException(reason);
        }

        Attach(session);
        Debug.Log($"[En ligne] Salon {session.Code} rejoint ({session.PlayerCount} joueurs).");
        return session;
    }

    /// <summary>
    /// Quitte le salon : l'hôte le ferme (tout le monde en sort), un membre le quitte. Arrête le réseau de jeu
    /// s'il tournait. Sans effet hors d'un salon ; ne lève jamais (on quitte même si le service ne répond pas).
    /// </summary>
    public static async Task LeaveAsync()
    {
        ISession session = Current;
        if (session == null) return;

        leaving = true;
        Detach(session);
        Current = null;
        try
        {
            if (session.IsHost) await session.AsHost().DeleteAsync();
            else await session.LeaveAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[En ligne] Sortie du salon : {e.Message}");
        }
        finally
        {
            leaving = false;
        }
        Changed?.Invoke();
    }

    /// <summary>Hôte : lance la partie. Démarre le réseau de jeu par les relais ; les membres du salon s'y
    /// connectent d'eux-mêmes. Lève si le service refuse (l'appelant le dit au joueur).</summary>
    public static async Task StartGameAsync()
    {
        if (!IsHost) throw new OnlineSessionException("Seul l'hôte peut lancer la partie.");
        IHostSession host = Current.AsHost();
        await host.Network.StartRelayNetworkAsync(new RelayNetworkOptions());
        Debug.Log($"[En ligne] Partie lancée, salon {host.Code}.");
    }

    // ------------------------------------------------------------------
    // Réglages et capacité (hôte)
    // ------------------------------------------------------------------

    /// <summary>Les réglages publiés par l'hôte du salon (les siens chez l'hôte).</summary>
    public static MatchRules Rules
    {
        get
        {
            if (Current == null) return GameSettings.HostRules;
            return IsHost ? hostRules : ParseRules(PropertyValue(Current.Properties, RulesKey));
        }
    }

    /// <summary>Hôte : change les réglages de la partie. L'envoi au service est regroupé : plusieurs clics
    /// rapprochés partent en un seul envoi.</summary>
    public static void SetRules(MatchRules rules)
    {
        if (!IsHost) return;
        hostRules = rules.Sanitized();
        RequestHostStateSave();
        Changed?.Invoke();
    }

    // Publie les réglages et le verrou (complet ou non), un envoi à la fois : le service refuse deux
    // modifications concurrentes du même salon.
    private static async void RequestHostStateSave()
    {
        if (savingHostState)
        {
            saveHostStateAgain = true;
            return;
        }

        savingHostState = true;
        try
        {
            do
            {
                saveHostStateAgain = false;
                ISession session = Current;
                if (session == null || !session.IsHost) break;

                IHostSession host = session.AsHost();
                host.SetProperty(RulesKey, new SessionProperty(FormatRules(hostRules), VisibilityPropertyOptions.Member));
                host.IsLocked = ShouldLock(host.PlayerCount, hostRules.capacity);
                await host.SavePropertiesAsync();
            }
            while (saveHostStateAgain);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[En ligne] Réglages du salon non enregistrés : {e.Message}");
        }
        finally
        {
            savingHostState = false;
        }
    }

    // Hôte : au-delà de la capacité (deux arrivées à la même seconde, avant que le verrou ne se ferme), le
    // dernier arrivé repart ; puis le verrou suit le nombre de joueurs.
    private static async void EnforceCapacity()
    {
        ISession session = Current;
        if (session == null || !session.IsHost) return;

        var players = new List<(string id, DateTime joined)>();
        foreach (IReadOnlyPlayer player in session.Players) players.Add((player.Id, player.Joined));
        foreach (string id in PlayersBeyondCapacity(players, session.Host, hostRules.capacity))
        {
            try
            {
                await session.AsHost().RemovePlayerAsync(id);
                Debug.Log($"[En ligne] Salon complet : {id} n'y entre pas.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[En ligne] Retrait d'un joueur en trop : {e.Message}");
            }
        }
        RequestHostStateSave();
    }

    // ------------------------------------------------------------------
    // Événements du salon
    // ------------------------------------------------------------------

    private static void Attach(ISession session)
    {
        Current = session;
        session.Changed += OnSessionChanged;
        session.PlayerJoined += OnPlayerJoined;
        session.PlayerHasLeft += OnPlayerHasLeft;
        session.SessionHostChanged += OnHostChanged;
        session.Deleted += OnDeleted;
        session.RemovedFromSession += OnRemovedFromSession;
        session.Network.StateChanged += OnNetworkStateChanged;
        session.Network.StartFailed += OnNetworkStartFailed;
        Changed?.Invoke();
    }

    private static void Detach(ISession session)
    {
        session.Changed -= OnSessionChanged;
        session.PlayerJoined -= OnPlayerJoined;
        session.PlayerHasLeft -= OnPlayerHasLeft;
        session.SessionHostChanged -= OnHostChanged;
        session.Deleted -= OnDeleted;
        session.RemovedFromSession -= OnRemovedFromSession;
        session.Network.StateChanged -= OnNetworkStateChanged;
        session.Network.StartFailed -= OnNetworkStartFailed;
    }

    private static void OnSessionChanged() => Changed?.Invoke();

    private static void OnPlayerJoined(string playerId) => EnforceCapacity();

    private static void OnPlayerHasLeft(string playerId)
    {
        if (IsHost) RequestHostStateSave();
    }

    // L'hôte du salon est parti sans le fermer (jeu fermé avant le lancement) : le service en a désigné un
    // autre. S'il s'agit de cette machine, elle reprend les réglages publiés et tient le salon.
    private static void OnHostChanged(string newHostId)
    {
        if (IsHost)
        {
            hostRules = ParseRules(PropertyValue(Current.Properties, RulesKey));
            RequestHostStateSave();
        }
        Changed?.Invoke();
    }

    private static void OnDeleted()
    {
        if (!leaving) End("L'hôte a fermé la partie.");
    }

    private static void OnRemovedFromSession()
    {
        if (!leaving) End("Tu n'es plus dans le salon : il était complet, ou la connexion au service a été perdue.");
    }

    private static void OnNetworkStateChanged(NetworkState state) => Changed?.Invoke();

    // Un membre n'a pas pu rejoindre la partie lancée (refusé par l'hôte, relais injoignable) : il sort du
    // salon, avec la raison. Il pourra revenir avec le code.
    private static async void OnNetworkStartFailed(SessionError error)
    {
        if (IsHost) return; // l'hôte reçoit l'erreur de StartGameAsync, et garde son salon
        string code = Code;
        string reason = NetworkManager.Singleton != null ? NetworkManager.Singleton.DisconnectReason : "";
        await LeaveAsync();
        Ended?.Invoke(DescribeNetworkFailure(reason, code));
    }

    private static void End(string message)
    {
        ISession session = Current;
        if (session != null) Detach(session);
        Current = null;
        Ended?.Invoke(message);
        Changed?.Invoke();
    }

    private static Dictionary<string, PlayerProperty> LocalPlayerProperties()
        => new Dictionary<string, PlayerProperty>
        {
            [NameKey] = new PlayerProperty(PlayerMatchState.SanitizeName(GameSettings.PlayerName), VisibilityPropertyOptions.Member),
            [ColorKey] = new PlayerProperty(GameSettings.PreferredColorIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), VisibilityPropertyOptions.Member),
            [ChassisKey] = new PlayerProperty(((int)GameSettings.PreferredChassis).ToString(System.Globalization.CultureInfo.InvariantCulture), VisibilityPropertyOptions.Member),
        };

    /// <summary>Le pseudo d'un joueur du salon (vide s'il n'en a pas donné).</summary>
    public static string NameOf(IReadOnlyPlayer player)
        => player != null && player.Properties != null ? PlayerMatchState.SanitizeName(PropertyValue(player.Properties, NameKey)) : "";

    /// <summary>La couleur choisie par un joueur du salon (son rang dans la palette), -1 sans choix.</summary>
    public static int ColorOf(IReadOnlyPlayer player)
        => player != null && ParseColor(PropertyValue(player.Properties, ColorKey), out int index) ? index : -1;

    /// <summary>Le châssis choisi par un joueur du salon : le CT-01 sans choix lisible.</summary>
    public static ChassisId ChassisOf(IReadOnlyPlayer player)
        => player != null && int.TryParse(PropertyValue(player.Properties, ChassisKey), System.Globalization.NumberStyles.None,
                                          System.Globalization.CultureInfo.InvariantCulture, out int value)
            ? RobotChassis.Sanitized(value)
            : ChassisId.CT01;

    /// <summary>Le rang de couleur publié par un joueur : un entier de -1 à 63, sinon rien. Fonction pure,
    /// testée.</summary>
    public static bool ParseColor(string text, out int index)
    {
        index = -1;
        if (!int.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int value)) return false;
        if (value < -1 || value > 63) return false;
        index = value;
        return true;
    }

    private static string PropertyValue(IReadOnlyDictionary<string, SessionProperty> properties, string key)
        => properties != null && properties.TryGetValue(key, out SessionProperty property) && property != null ? property.Value ?? "" : "";

    private static string PropertyValue(IReadOnlyDictionary<string, PlayerProperty> properties, string key)
        => properties != null && properties.TryGetValue(key, out PlayerProperty property) && property != null ? property.Value ?? "" : "";

    // ------------------------------------------------------------------
    // Fonctions pures, testées
    // ------------------------------------------------------------------

    /// <summary>Le code tel que le service l'attend : majuscules, lettres et chiffres seulement (un code
    /// recopié avec des espaces, un tiret ou en minuscules passe). Fonction pure, testée.</summary>
    public static string NormalizeCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return "";
        var builder = new System.Text.StringBuilder(code.Length);
        foreach (char c in code.ToUpperInvariant())
        {
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) builder.Append(c);
            if (builder.Length >= 16) break;
        }
        return builder.ToString();
    }

    /// <summary>Les réglages en propriété du salon : « places;organisation;interventions;manches;tirage »
    /// (le tirage au sort des duellistes depuis le 2026-10-27). Fonction pure, testée (aller-retour avec
    /// ParseRules).</summary>
    public static string FormatRules(MatchRules rules)
    {
        MatchRules r = rules.Sanitized();
        return $"{r.capacity};{(int)r.organization};{(r.spectatorInterventions ? 1 : 0)};{r.roundsToWin};{(r.randomDuelists ? 1 : 0)}";
    }

    /// <summary>Relit les réglages publiés par l'hôte. Ce qui manque ou ne se lit pas garde sa valeur par
    /// défaut, et le tout revient dans ses bornes. Fonction pure, testée.</summary>
    public static MatchRules ParseRules(string text)
    {
        MatchRules rules = MatchRules.Default;
        if (string.IsNullOrEmpty(text)) return rules;
        string[] parts = text.Split(';');
        if (parts.Length > 0 && byte.TryParse(parts[0], out byte capacity)) rules.capacity = capacity;
        if (parts.Length > 1 && byte.TryParse(parts[1], out byte organization)) rules.organization = (DuelOrganization)organization;
        if (parts.Length > 2) rules.spectatorInterventions = parts[2].Trim() != "0";
        if (parts.Length > 3 && byte.TryParse(parts[3], out byte rounds)) rules.roundsToWin = rounds;
        if (parts.Length > 4) rules.randomDuelists = parts[4].Trim() == "1";
        return rules.Sanitized();
    }

    /// <summary>
    /// L'ordre du salon au lancement (hôte) : l'hôte, puis les autres dans leur ordre d'arrivée au salon ;
    /// avec le tirage au sort, deux d'entre eux, au hasard, en tête. L'arène en tire ses duellistes et sa file
    /// (SessionFlow.LaunchOrder, RoundManager) : tous s'y connectent à la fois, et l'ordre d'arrivée n'y dit
    /// plus rien (2026-10-27 : le deuxième du salon disparaissait à trois joueurs).
    /// </summary>
    public static List<string> LaunchOrder(bool randomDuelists, System.Random random)
    {
        ISession session = Current;
        if (session == null) return new List<string>();
        var players = new List<(string id, DateTime joined)>();
        foreach (IReadOnlyPlayer player in session.Players) players.Add((player.Id, player.Joined));
        List<string> order = SalonOrder(players, session.Host);
        return randomDuelists ? DuelRoster.DrawDuelists(order, DuelRoster.DuelistSlots, random) : order;
    }

    /// <summary>Les joueurs d'un salon dans son ordre : l'hôte d'abord, puis par heure d'arrivée (et par
    /// identifiant, à la même heure). Fonction pure, testée.</summary>
    public static List<string> SalonOrder(IReadOnlyList<(string id, DateTime joined)> players, string hostId)
    {
        var ordered = new List<(string id, DateTime joined)>(players ?? Array.Empty<(string, DateTime)>());
        ordered.Sort((a, b) =>
        {
            bool aHost = a.id == hostId, bHost = b.id == hostId;
            if (aHost != bHost) return aHost ? -1 : 1;
            int byTime = a.joined.CompareTo(b.joined);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.id, b.id);
        });
        var ids = new List<string>(ordered.Count);
        foreach ((string id, DateTime _) in ordered) ids.Add(id);
        return ids;
    }

    /// <summary>Ce joueur est-il encore dans le salon de cette machine ?</summary>
    public static bool HasPlayer(string playerId)
    {
        ISession session = Current;
        if (session == null || string.IsNullOrEmpty(playerId)) return false;
        foreach (IReadOnlyPlayer player in session.Players)
            if (player.Id == playerId) return true;
        return false;
    }

    /// <summary>L'identifiant de cette machine auprès des services (son joueur anonyme), vide hors
    /// connexion : il part dans la demande de connexion, pour que l'hôte retrouve sa place dans le salon.</summary>
    public static string LocalPlayerId
    {
        get
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized) return "";
                return AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId ?? "" : "";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }

    /// <summary>Le salon se ferme quand il est complet. Fonction pure, testée.</summary>
    public static bool ShouldLock(int playerCount, int capacity) => playerCount >= capacity;

    /// <summary>
    /// Les joueurs en trop dans un salon de `capacity` places : l'hôte reste toujours, puis les premiers
    /// arrivés ; les derniers arrivés repartent. Fonction pure, testée.
    /// </summary>
    public static List<string> PlayersBeyondCapacity(IReadOnlyList<(string id, DateTime joined)> players, string hostId, int capacity)
    {
        List<string> ordered = SalonOrder(players, hostId);
        var beyond = new List<string>();
        for (int i = Mathf.Max(1, capacity); i < ordered.Count; i++) beyond.Add(ordered[i]);
        return beyond;
    }

    /// <summary>
    /// Ce qu'on dit au joueur quand créer, rejoindre ou lancer échoue. Le service répond en anglais, avec un
    /// code d'erreur souvent générique : un salon complet ou un code inconnu ne se reconnaissent qu'à leur
    /// message. `disconnectReason` est la raison donnée par Netcode s'il a refusé la connexion (version,
    /// partie pleine). Fonction pure, testée.
    /// </summary>
    public static string DescribeError(Exception error, string disconnectReason = "")
    {
        if (error is OnlineSessionException ours) return ours.Message;
        if (error is AggregateException aggregate && aggregate.InnerException != null) return DescribeError(aggregate.InnerException, disconnectReason);

        if (error is SessionException session)
        {
            string message = (session.Message ?? "").ToLowerInvariant();
            switch (session.Error)
            {
                case SessionError.RateLimitExceeded:
                    return "Trop de demandes d'un coup : réessaie dans quelques secondes.";
                case SessionError.SessionNotFound:
                case SessionError.InvalidSessionIdentifier:
                    return "Aucune partie ne correspond à ce code.";
                case SessionError.NotAuthorized:
                    return OfflineMessage;
                case SessionError.NetworkManagerStartFailed:
                case SessionError.NetworkSetupFailed:
                    return DescribeNetworkFailure(disconnectReason, "");
            }
            if (message.Contains("full") || message.Contains("locked")) return "Cette partie est complète.";
            if (message.Contains("not found") || message.Contains("code")) return "Aucune partie ne correspond à ce code.";
            return "Le service en ligne a refusé : " + session.Message;
        }

        if (error is AuthenticationException || error is RequestFailedException || error is ServicesInitializationException)
            return OfflineMessage;

        return "Erreur inattendue : " + (error != null ? error.Message : "inconnue");
    }

    public const string OfflineMessage =
        "Impossible de joindre les services en ligne : vérifie ta connexion à internet. Le réseau local reste possible.";

    /// <summary>La connexion à une partie lancée a échoué : la raison de l'hôte s'il en a donné une (version,
    /// partie pleine), sinon le relais. Fonction pure, testée.</summary>
    public static string DescribeNetworkFailure(string disconnectReason, string code)
    {
        if (!string.IsNullOrEmpty(disconnectReason))
        {
            string translated = SessionFlow.MenuMessageFor(disconnectReason, false);
            // Un refus de l'hôte (SessionFlow.ShouldApprove) arrive déjà en français, tel quel.
            if (translated == disconnectReason) return "Connexion refusée : " + disconnectReason;
            if (disconnectReason.IndexOf("host shutting down", StringComparison.OrdinalIgnoreCase) >= 0) return translated;
        }
        return string.IsNullOrEmpty(code)
            ? "Impossible de se connecter à la partie de l'hôte."
            : $"Impossible de se connecter à la partie de l'hôte. Réessaie avec le code {code}.";
    }
}

/// <summary>Un refus décidé par le jeu (pas par le service), avec ce qu'on dit au joueur.</summary>
public class OnlineSessionException : Exception
{
    public OnlineSessionException(string message) : base(message) { }
}
