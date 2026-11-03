using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Boucle de manches d'un duel 1v1, en BO5 ou en BO3 au choix de l'hôte (premier à
/// `MatchRules.roundsToWin` manches gagnées, 2026-10-25).
///
/// ENTIÈREMENT SERVEUR-AUTORITAIRE : la phase, le score et les transitions sont décidés par le
/// serveur et publiés en `NetworkVariable`. Les clients ne font que lire et afficher (GameHud). La
/// seule demande qu'un client puisse faire est « je suis prêt » pendant la préparation
/// (PlayerMatchState), qui ne fait qu'avancer le décompte.
///
/// Une manche (2026-10-12) : PRÉPARATION (choix de l'arme, 30 s au plus, finie dès que les deux
/// joueurs ont validé) → DÉCOMPTE → MANCHE → FIN DE MANCHE (le survivant garde la main quelques
/// secondes pour narguer son adversaire, puis fondu au noir) → préparation suivante. Le match fini,
/// même temps pour le gagnant, puis l'écran de fin, puis un nouveau match.
///
/// La détection de mort se fait par SONDAGE de `Health.IsDead` plutôt que par abonnement à
/// `Health.OnDeath`. À deux joueurs, le coût est nul, et ça évite toute la classe de bugs de cycle
/// de vie des abonnements (joueur qui se déconnecte en pleine manche, objet détruit avant le
/// désabonnement, double abonnement au respawn).
///
/// LES RÔLES (2026-10-13, GDD § 4) : une partie accueille plusieurs joueurs (la capacité est un réglage
/// de l'hôte), mais deux seulement se battent. Les autres sont spectateurs, sur la tribune, dans une
/// file d'attente. Le serveur range les joueurs à chaque image (DuelRoster) et publie le rôle de chacun
/// dans sa fiche (PlayerMatchState). La manche ne regarde que les duellistes : eux seuls se préparent,
/// se replacent, marquent. En fin de match, au roi de la colline, le gagnant reste et le perdant cède sa
/// place au premier de la file ; en duellistes fixes, rien ne tourne.
///
/// L'ABANDON (2026-10-25, GDD § 4, dette n° 58) : un duelliste qui quitte la partie en plein match perd
/// par forfait. L'autre remporte le match (même temps pour lui, puis l'écran de fin) ; ensuite, le
/// premier spectateur de la file prend la place libre et un nouveau match commence contre lui ; sans
/// spectateur, le gagnant attend un adversaire.
///
/// LA FIN DU MATCH (2026-10-26, règles de l'utilisateur) : pendant l'écran de fin, les deux duellistes
/// votent (PlayerMatchState.RequestVote). La revanche demande leurs deux voix ; l'un qui choisit le
/// salon, ou les 15 s écoulées sans les deux voix, ramènent tout le monde au SALON de la partie : la
/// phase Salon, dans l'arène, où l'hôte change les réglages (manches, places, duels, interventions) et
/// relance, et où les autres l'attendent (SalonPanel). Pas de vote après un forfait.
///
/// LES MODES (2026-11-02, GDD § 11) : le mode est un réglage de la partie (MatchRules.mode). Le classique
/// suit la boucle ci-dessus. Le Golden Gun aussi, en plus de manches (le premier à 5 ou à 7), mais sans
/// préparation : chaque manche commence au décompte, l'arme du mode en main (ServerForceWeapon).
///
/// LES RÉAPPARITIONS (le Gun Game, l'Élimination confirmée, la Zone et le Death match du classique, 2026-11-02) : le match se joue d'une traite. Un duelliste tué revient
/// `respawnDelay` secondes plus tard (3 s, GDD § 11, T2), sans fondu, au point le plus éloigné de l'adversaire
/// qu'il ne voit pas (PlayerLocomotion.ServerRespawnAwayFromOpponents), sans protection. Le mort se voit par
/// sondage, comme ailleurs ; qui l'a tué et avec quoi, la vie le retient (Health.ServerKillerId). Au Gun Game,
/// l'élimination fait monter le vainqueur d'une marche, et l'arme suivante lui arrive en main aussitôt ; le coup
/// de gant de la dernière marche gagne le match (GunGame.AfterKill). Au Death match, chaque élimination compte
/// un point au vainqueur (le score des manches compte alors des éliminations) : le premier à `killsToWin` gagne. À
/// l'Élimination confirmée, le robot détruit lâche son module mémoire (KillConfirmed) : le point ne va au vainqueur
/// que quand il le ramasse, et la victime revenue l'annule en le reprenant. À la Zone, on gagne des points seul dans la
/// zone du moment (ZoneMode, ZoneLayout), qui change de place à intervalles fixes, et moins en éliminant ; le score
/// compte alors des points.
///
/// LE TAG TEAM (2026-11-02, GDD § 11, TagTeam) : deux équipes, composées par l'hôte au salon ; dans l'arène, le premier
/// de chaque équipe combat (le duelliste), les autres attendent sur la tribune (spectateurs de leur équipe, qui aident
/// par ses gestes, sans toucher leurs coéquipiers). Quand un combattant tombe, son équipe passe le relais : 3 s plus
/// tard, il monte sur la tribune et le coéquipier qui attend depuis le plus longtemps entre, au point le plus éloigné
/// du survivant, qui garde ses PV. Chaque élimination rapporte des points à l'équipe du vainqueur (TagTeam.PointsFor) ;
/// la première à TagTeam.TargetPoints gagne. Une équipe que tous ses joueurs quittent perd par forfait.
///
/// LE TOURNOI (2026-11-03, GDD § 11, Tournament) : une poule, chacun contre chacun, tirée au sort, puis une finale entre les
/// deux premiers. Les matchs d'un tour se jouent en même temps, chacun dans sa copie de l'arène (TournamentSite) : chacun a
/// son RoundManager, un « couloir » (le préfab MatchTournoi, IsLane), qui arbitre ce seul match en classique, BO3 en poule et
/// BO5 en finale, entre ses deux joueurs, et rend son résultat au RoundManager de la scène, qui mène le tournoi (la phase
/// Tournament : le tirage, les tours, l'entracte, la finale, puis l'écran du champion). Hors de leur match, les joueurs
/// attendent dans la salle d'attente, où ils regardent les duels en cours sur ses écrans et tirent sur des mannequins, sans
/// pouvoir se toucher entre eux (MayHurt, HitboxLiveFor). Chaque joueur est arbitré par le RoundManager de son match (For) :
/// le HUD, les sons et les panneaux montrent celui du joueur de leur machine (Local).
/// </summary>
public class RoundManager : NetworkBehaviour
{
    public enum Phase : byte
    {
        WaitingForPlayers,
        Starting,    // décompte, déplacement bloqué, tir interdit
        Active,      // la manche est en cours
        RoundOver,   // quelqu'un est mort : le survivant garde la main, puis fondu au noir
        MatchOver,   // un joueur a atteint roundsToWin : même temps, puis l'écran de fin
        Preparation, // choix de l'arme avant la manche (2026-10-12, ajoutée à la fin de l'énumération)
        Salon,       // le salon de la partie, après un match sans revanche : l'hôte règle et relance (2026-10-26)
        Tournament,  // le Tournoi (2026-11-03) : le tirage, les tours, la finale ; hors de leur match, les joueurs attendent
    }

    /// <summary>Ce que le vote de fin de match a décidé (publié : l'écran de fin l'annonce).</summary>
    public enum MatchDecision : byte
    {
        Pending,
        Rematch,
        Salon,
    }

    /// <summary>Aucun vainqueur (manche nulle, ou pas encore de manche jouée).</summary>
    public const ulong NoWinner = ulong.MaxValue;

    [Header("Durées (s)")]
    [Tooltip("Préparation avant chaque manche : chaque joueur choisit son arme dans le panneau du centre de l'écran. Le décompte part dès que les deux ont validé, au plus tard à la fin de ce temps (2026-10-12, demandé par l'utilisateur ; 30 s à chaque manche depuis le 2026-10-26).")]
    [SerializeField] private float preparationDuration = 30f;

    [Tooltip("Décompte avant le début d'une manche. Pendant ce temps le déplacement est bloqué et le tir interdit, pour que les deux joueurs partent exactement en même temps.")]
    [SerializeField] private float startingDuration = 3f;

    [Tooltip("Après une mort : le survivant garde la main (il bouge, tire, se penche) pour narguer son adversaire, puis l'écran passe au noir et les joueurs sont replacés (GDD § 5). Les dernières secondes servent au fondu (GameHud).")]
    [SerializeField] private float roundOverDuration = 5f;

    [Tooltip("Durée de l'écran de fin, après le même temps que roundOverDuration pour le gagnant : le temps du vote des deux duellistes. Sans leurs deux voix pour la revanche, retour au salon (2026-10-26, demandé par l'utilisateur : 15 s).")]
    [SerializeField] private float endScreenDuration = 15f;

    [Tooltip("Le vote tranché, l'écran de fin dure encore ce temps (s) : le noir tombe, puis vient la revanche ou le salon.")]
    [SerializeField] private float decisionTail = 1f;

    [Tooltip("Dans les modes à réapparitions (le Gun Game), le temps (s) entre la mort d'un duelliste et son retour dans l'arène (GDD § 11, T2 : 3 s).")]
    [SerializeField] private float respawnDelay = 3f;

    [Header("Tournoi (2026-11-03)")]
    [Tooltip("Le tirage du Tournoi, montré à tous dans la salle d'attente avant le premier tour (s).")]
    [SerializeField] private float drawDuration = 12f;

    [Tooltip("L'entracte entre deux tours du Tournoi, le classement à l'écran (s).")]
    [SerializeField] private float breakDuration = 8f;

    [Tooltip("L'écran du champion, plus long que celui d'un match (GDD § 11, TO6) (s).")]
    [SerializeField] private float championScreenDuration = 25f;

    [Tooltip("Le préfab d'un couloir du Tournoi (MatchTournoi : un RoundManager qui arbitre un seul match), que celui de la scène fait apparaître pour chaque match d'un tour.")]
    [SerializeField] private RoundManager lanePrefabAsset;

    [Tooltip("Ce RoundManager est un couloir du Tournoi (coché sur le préfab MatchTournoi, jamais dans la scène) : il arbitre un seul match, entre deux joueurs, dans sa copie de l'arène.")]
    [SerializeField] private bool lanePrefab;

    [Tooltip("Durée max d'une manche, en secondes. 0 = AUCUNE limite, conformément au GDD (§ 5, tranché le 2026-10-02) : une manche se termine par une mort. Ne sert qu'à un test ponctuel ; si des manches passives apparaissent en playtest, la piste retenue est la révélation sonore, pas le retour d'un chrono.")]
    [SerializeField] private float roundTimeLimit = 0f;

    // ------------------------------------------------------------------
    // État réseauté
    // ------------------------------------------------------------------

    private readonly NetworkVariable<Phase> phase = new NetworkVariable<Phase>(
        Phase.WaitingForPlayers, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Début et fin de la phase courante, en temps SERVEUR (NetworkManager.ServerTime).
    /// Publiés une fois par changement de phase, au lieu d'un temps restant réécrit à chaque frame
    /// et donc réémis à chaque tick. Chaque client en déduit le décompte, le temps écoulé (le chrono
    /// du HUD, GDD § 15) et le moment du fondu au noir. Une fin à 0 signifie : pas de fin.
    ///
    /// Le temps serveur de Netcode n'est qu'une estimation chez le client : assez pour un
    /// affichage, jamais pour une décision, qui reste au serveur.</summary>
    private readonly NetworkVariable<double> phaseStartedAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<double> phaseEndsAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> roundNumber = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Le gagnant de la dernière manche (du match, en MatchOver), ou NoWinner.</summary>
    private readonly NetworkVariable<ulong> lastRoundWinner = new NetworkVariable<ulong>(
        NoWinner, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Le replay du coup mortel de la fin de manche en cours (KillReplay, 2026-11-03) : quand il commence, qui,
    /// comment, avec quoi, de combien. Effacé à chaque changement de phase.</summary>
    private readonly NetworkVariable<KillReplay.Info> killReplay = new NetworkVariable<KillReplay.Info>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Serveur : la dernière élimination vue (son auteur, sa victime, comment), et quand.
    private KillReplay.Info lastKill;
    private double lastKillAt;

    /// <summary>Le match en cours (en MatchOver) a-t-il été gagné par forfait ?</summary>
    private readonly NetworkVariable<bool> wonByForfeit = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Ce que le vote de fin de match a décidé (en MatchOver).</summary>
    private readonly NetworkVariable<MatchDecision> decision = new NetworkVariable<MatchDecision>(
        MatchDecision.Pending, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public struct Score : INetworkSerializable, System.IEquatable<Score>
    {
        public ulong clientId;
        public int wins;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref wins);
        }

        public bool Equals(Score other) => clientId == other.clientId && wins == other.wins;
    }

    private readonly NetworkList<Score> scores = new NetworkList<Score>();

    /// <summary>L'Élimination confirmée : les modules des robots détruits, que leur vainqueur doit ramasser
    /// (KillConfirmed). Publiés à tous ; chaque machine les montre (MemoryModule).</summary>
    private readonly NetworkList<KillConfirmed.Module> modules = new NetworkList<KillConfirmed.Module>();
    private ushort nextModuleId;

    /// <summary>Le tableau de la soirée (GDD § 14) : les matchs finis depuis l'ouverture de la partie (Evening), publiés à
    /// tous ; le salon en tire qui a gagné quoi. Rempli sur le RoundManager de la scène seulement : un couloir du Tournoi
    /// n'est qu'un match du tournoi, qui compte pour un.</summary>
    private readonly NetworkList<Evening.Match> evening = new NetworkList<Evening.Match>();

    // Serveur : les joueurs du match en cours (les duellistes ; au Tag Team, les membres des deux équipes), retenus à
    // son départ, et le pseudo de chaque joueur d'un match ou d'un tournoi : celui qui part avant la fin garde le sien.
    private readonly List<(ulong id, PlayerTeam team)> matchRoster = new List<(ulong, PlayerTeam)>();
    private readonly Dictionary<ulong, string> knownNames = new Dictionary<ulong, string>();
    private readonly MemoryModuleViews moduleViews = new MemoryModuleViews();

    // Chaque machine : les modules pris depuis le début de la partie, et le dernier (ModuleTakenClientRpc).
    private int modulesTaken;
    private Vector3 lastModuleAt;
    private ulong lastModuleTaker;
    private bool lastModuleConfirmed;
    private int lastModuleScore;

    /// <summary>La Zone : qui la tient (NoWinner : personne, ou disputée), et si elle est disputée. Publiés ; les
    /// fractions de point en cours restent au serveur.</summary>
    private readonly NetworkVariable<ulong> zoneHolder = new NetworkVariable<ulong>(
        NoWinner, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> zoneContested = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly Dictionary<ulong, float> zoneCarry = new Dictionary<ulong, float>();

    /// <summary>Les points des deux équipes du Tag Team.</summary>
    public struct TeamScore : INetworkSerializeByMemcpy, System.IEquatable<TeamScore>
    {
        public int a, b;

        public int Of(PlayerTeam team) => team == PlayerTeam.A ? a : team == PlayerTeam.B ? b : 0;

        public bool Equals(TeamScore other) => a == other.a && b == other.b;
    }

    /// <summary>Le Tag Team : les points de chaque équipe, et l'équipe qui a gagné le match (en MatchOver).</summary>
    private readonly NetworkVariable<TeamScore> teamScore = new NetworkVariable<TeamScore>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<PlayerTeam> winningTeam = new NetworkVariable<PlayerTeam>(
        PlayerTeam.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Serveur : les deux équipes du Tag Team, chacune dans l'ordre de son relais ([0] combat, les autres attendent).
    private readonly List<ulong> teamA = new List<ulong>();
    private readonly List<ulong> teamB = new List<ulong>();

    // Chaque machine : les éliminations du Tag Team et les entrées depuis le début de la partie, et les dernières
    // (TagKillClientRpc, TagInClientRpc) : le HUD et les sons les sondent.
    private int tagKills;
    private ulong lastTagKiller, lastTagVictim;
    private int lastTagPoints;
    private Health.DamageCause lastTagCause;
    private int tagIns;
    private ulong lastTagIn;

    /// <summary>Les réglages de la partie (capacité, organisation des duels, interventions des
    /// spectateurs), fixés par l'hôte avant de la lancer (SessionFlow.ActiveRules) et publiés à tous.</summary>
    private readonly NetworkVariable<MatchRules> rules = new NetworkVariable<MatchRules>(
        MatchRules.Default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Côté serveur : qui se bat, et la file des spectateurs, par identifiant de client (DuelRoster).
    private readonly List<ulong> duelistIds = new List<ulong>();
    private readonly List<ulong> spectatorQueue = new List<ulong>();
    private readonly List<ulong> presentIds = new List<ulong>();
    private readonly List<int> presentRanks = new List<int>();

    [Tooltip("Une partie lancée d'un salon en ligne attend ses joueurs dans l'arène au plus ce temps (s), pour faire duellistes ceux que le salon désigne : au-delà, un joueur qui n'arrive pas cède sa place.")]
    [SerializeField] private float gatheringTimeout = 15f;

    // Côté serveur : les joueurs du salon arrivent encore dans l'arène (SessionFlow.LaunchOrder), et jusqu'à
    // quand on les attend.
    private bool gathering;
    private double gatheringEndsAt;
    // Côté serveur : la partie a été lancée d'un salon en ligne (le Tournoi y part tout seul ; en réseau local, l'hôte le
    // lance du salon de l'arène).
    private bool launchedOnline;
    private static readonly System.Random draw = new System.Random();

    /// <summary>Un couloir du Tournoi : sa copie de l'arène, sa place dans le calendrier, ses manches à gagner, et ses deux
    /// joueurs. Publié par le serveur à l'apparition du couloir ; des manches à 0 : pas encore connu.</summary>
    public struct LaneInfo : INetworkSerializeByMemcpy, System.IEquatable<LaneInfo>
    {
        public byte arena;
        public byte match;
        public byte roundsToWin;
        public bool final;
        public ulong a, b;

        public bool Equals(LaneInfo other) => arena == other.arena && match == other.match && roundsToWin == other.roundsToWin
                                              && final == other.final && a == other.a && b == other.b;
    }

    private readonly NetworkVariable<LaneInfo> laneInfo = new NetworkVariable<LaneInfo>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // Serveur : ce que le RoundManager de la scène donne au couloir avant de le faire apparaître ; le résultat rendu.
    private LaneInfo pendingLane;
    private bool laneReported;
    /// <summary>L'arsenal commun (GDD § 11, I1) : l'arme tirée au sort pour les deux duellistes, et ses accessoires.</summary>
    private readonly NetworkVariable<CommonArsenal.State> commonArsenal = new NetworkVariable<CommonArsenal.State>(
        new CommonArsenal.State { slot = CommonArsenal.NoSlot }, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    /// <summary>Pile ou face (GDD § 11) : qui choisit la famille de la manche, et laquelle.</summary>
    private readonly NetworkVariable<CoinFlip.State> coinFlip = new NetworkVariable<CoinFlip.State>(
        new CoinFlip.State { family = CoinFlip.NoFamily, previous = CoinFlip.NoFamily }, NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    // Serveur : les accessoires de chaque joueur d'avant l'arsenal commun, rendus à la fin.
    private readonly Dictionary<ulong, WeaponAttachments> savedAttachments = new Dictionary<ulong, WeaponAttachments>();

    // Chaque machine : les couloirs apparus.
    private static readonly List<RoundManager> lanes = new List<RoundManager>();

    /// <summary>Le Tournoi, publié par le RoundManager de la scène : son avancée, ses matchs (le calendrier tiré au sort,
    /// leurs résultats) et ses joueurs, dans l'ordre du tirage.</summary>
    private readonly NetworkVariable<Tournament.State> tournament = new NetworkVariable<Tournament.State>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkList<Tournament.Match> tournamentMatches = new NetworkList<Tournament.Match>();
    private readonly NetworkList<ulong> tournamentPlayers = new NetworkList<ulong>();
    // Serveur : la copie de travail du calendrier et des joueurs, ceux qui sont partis, les couloirs en cours.
    private readonly List<Tournament.Match> schedule = new List<Tournament.Match>();
    private readonly List<ulong> entrants = new List<ulong>();
    private readonly HashSet<ulong> leftEntrants = new HashSet<ulong>();
    private readonly List<RoundManager> runningLanes = new List<RoundManager>();

    // ------------------------------------------------------------------
    // Accès global
    // ------------------------------------------------------------------

    private static RoundManager cached;

    public static RoundManager Instance
    {
        get
        {
            if (cached == null)
            {
                // Le RoundManager de la scène, jamais un couloir du Tournoi.
                foreach (RoundManager rm in FindObjectsByType<RoundManager>(FindObjectsInactive.Exclude))
                {
                    if (rm.lanePrefab) continue;
                    cached = rm;
                    break;
                }
            }
            return cached;
        }
    }

    /// <summary>Le RoundManager qui arbitre ce joueur : au Tournoi, le couloir de son match en cours ; sinon celui de la
    /// partie (dans la salle d'attente du Tournoi, il est en phase Tournament). Null sans RoundManager.</summary>
    public static RoundManager For(ulong clientId)
    {
        for (int i = 0; i < lanes.Count; i++)
        {
            RoundManager lane = lanes[i];
            if (lane == null || !lane.IsSpawned) continue;
            LaneInfo info = lane.laneInfo.Value;
            if (info.roundsToWin > 0 && (info.a == clientId || info.b == clientId)) return lane;
        }
        return Instance;
    }

    /// <summary>Le RoundManager du joueur de cette machine : ce que le HUD, les sons et les panneaux montrent.</summary>
    public static RoundManager Local
    {
        get
        {
            NetworkManager nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening ? For(nm.LocalClientId) : Instance;
        }
    }

    /// <summary>Ce RoundManager est-il un couloir du Tournoi (un match de la poule, ou la finale) ?</summary>
    public bool IsLane => lanePrefab;

    /// <summary>Le match de ce couloir : sa copie de l'arène, sa place dans le calendrier, ses deux joueurs.</summary>
    public LaneInfo Lane => laneInfo.Value;

    /// <summary>Les couloirs du Tournoi apparus sur cette machine.</summary>
    public static IReadOnlyList<RoundManager> Lanes => lanes;

    /// <summary>Le Tournoi se joue-t-il ? (le RoundManager de la scène ; un couloir joue un match du classique)</summary>
    public bool TournamentMode => !lanePrefab && rules.Value.Sanitized().IsTournament;

    /// <summary>L'avancée du Tournoi, ses matchs et ses joueurs (dans l'ordre du tirage), sur toutes les machines.</summary>
    public Tournament.State TournamentProgress => tournament.Value;
    public int TournamentMatchCount => tournamentMatches.Count;
    public Tournament.Match TournamentMatchAt(int index) => tournamentMatches[index];
    public int TournamentPlayerCount => tournamentPlayers.Count;

    /// <summary>Le tableau de la soirée : les matchs finis depuis l'ouverture de la partie, du plus ancien au plus récent.</summary>
    public int EveningCount => evening.Count;
    public Evening.Match EveningAt(int index) => evening[index];

    /// <summary>Secondes avant la fin de l'étape du tournoi (le tirage, l'entracte), 0 sans fin.</summary>
    public float TournamentStageTimeLeft => RemainingIn(tournament.Value.stageStartedAt, tournament.Value.stageEndsAt, Now);

    /// <summary>Secondes depuis le début de l'étape du tournoi.</summary>
    public float TournamentStageElapsed => (float)System.Math.Max(0d, Now - tournament.Value.stageStartedAt);
    public ulong TournamentPlayerAt(int index) => tournamentPlayers[index];

    /// <summary>Un joueur du Tournoi l'a-t-il quitté (TO7) ?</summary>
    public bool TournamentPlayerLeft(ulong clientId)
    {
        for (int i = 0; i < tournamentPlayers.Count && i < 8; i++)
        {
            if (tournamentPlayers[i] == clientId) return (tournament.Value.leftMask & (1 << i)) != 0;
        }
        return false;
    }

    /// <summary>Où un joueur doit être : dans l'arène de son match (`arena`, le numéro de sa copie ; 0 hors du Tournoi), ou,
    /// au Tournoi hors d'un match, dans la salle d'attente (faux).</summary>
    public static bool TryGetArenaOf(ulong clientId, out int arena)
    {
        arena = 0;
        RoundManager rm = For(clientId);
        if (rm != null && rm.IsLane)
        {
            arena = rm.laneInfo.Value.arena;
            return true;
        }
        return rm == null || !rm.TournamentMode;
    }

    /// <summary>La surface touchable d'un joueur est-elle en jeu ? Pas dans la salle d'attente du Tournoi : on y tire sur
    /// les mannequins, pas les uns sur les autres (TO5).</summary>
    public static bool HitboxLiveFor(ulong clientId) => TryGetArenaOf(clientId, out _);

    /// <summary>Un joueur peut-il en blesser un autre ? Au Tournoi, seulement son adversaire, dans leur couloir ; ailleurs,
    /// toujours (le serveur le vérifie avant les dégâts, en plus des surfaces touchables).</summary>
    public static bool MayHurt(ulong attackerId, Health target)
    {
        if (target == null) return false;
        PlayerMatchState victim = target.GetComponent<PlayerMatchState>();
        return victim == null || MayHurt(attackerId, victim.OwnerClientId);
    }

    public static bool MayHurt(ulong attackerId, ulong victimId)
    {
        RoundManager attacker = For(attackerId), victim = For(victimId);
        return attacker == victim && (attacker == null || !attacker.TournamentMode);
    }

    public Phase CurrentPhase => phase.Value;
    public int RoundNumber => roundNumber.Value;
    /// <summary>Ce qu'il faut gagner pour remporter le match : 3 manches en BO5, 2 en BO3, ou les éliminations du Death
    /// match (MatchRules.WinTarget) ; le score (WinsOf) les compte.</summary>
    public int RoundsToWin => rules.Value.WinTarget;

    /// <summary>Le Death match du classique se joue-t-il ?</summary>
    public bool DeathMatch => rules.Value.Sanitized().IsDeathMatch;

    /// <summary>Le match se gagne-t-il aux éliminations (le Death match, l'Élimination confirmée) ?</summary>
    public bool CountsKills => rules.Value.Sanitized().CountsKills;

    /// <summary>Le match se gagne-t-il aux points (la Zone) ? Le score compte alors des points.</summary>
    public bool CountsPoints => rules.Value.Sanitized().CountsPoints;

    /// <summary>Les morts reviennent-ils dans ce match ? (voir HasRespawns)</summary>
    public bool Respawns => HasRespawns(rules.Value);
    public ulong LastRoundWinner => lastRoundWinner.Value;
    /// <summary>Le match vient-il d'être gagné par forfait (en MatchOver) ?</summary>
    public bool WonByForfeit => wonByForfeit.Value;
    /// <summary>Ce que le vote de fin de match a décidé (en MatchOver).</summary>
    public MatchDecision Decision => decision.Value;

    /// <summary>Le vote est-il ouvert ? Pendant l'écran de fin d'un match gagné (pas d'un forfait), tant
    /// qu'il n'est pas tranché.</summary>
    public bool VoteOpen => phase.Value == Phase.MatchOver && !wonByForfeit.Value && decision.Value == MatchDecision.Pending
                            && !lanePrefab && !TournamentMode
                            && PhaseElapsed >= EndScreenDelay;

    /// <summary>Le match suivant oppose-t-il les mêmes duellistes ? Non au roi de la colline quand quelqu'un
    /// attend : le perdant cède sa place (l'écran de fin dit alors « Match suivant » plutôt que « Revanche »). Au Tag
    /// Team, les équipes restent.</summary>
    public bool NextMatchRotates => rules.Value.organization == DuelOrganization.KingOfTheHill && !TagTeamMode && spectatorQueueCount.Value > 0;

    // Le nombre de spectateurs en file, publié pour l'écran de fin (la file elle-même reste au serveur).
    private readonly NetworkVariable<byte> spectatorQueueCount = new NetworkVariable<byte>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public MatchRules Rules => rules.Value;

    /// <summary>Le mode de la partie (le classique, le Golden Gun...).</summary>
    public GameModeId Mode => rules.Value.Sanitized().mode;

    /// <summary>Les manches de ce mode commencent-elles par la préparation (le choix de l'arme) ? Non quand le
    /// mode impose l'arme : le décompte vient tout de suite (le Golden Gun, GDD § 11, G4). Fonction pure.</summary>
    public static bool HasPreparation(GameModeId mode) => !GameModeCatalog.ImposesWeapon(mode);

    /// <summary>Les manches de ces réglages commencent-elles par la préparation ? Non quand le mode impose l'arme, ni à
    /// l'arsenal commun : l'arme tirée au sort arrive en main avec le décompte (GDD § 11, I1). Fonction pure, testée.</summary>
    public static bool HasPreparation(MatchRules rules)
    {
        MatchRules r = rules.Sanitized();
        return HasPreparation(r.mode) && r.EffectiveWeaponChoice != WeaponChoice.CommonArsenal;
    }

    /// <summary>Le choix de l'arme de ce match : libre, pile ou face, ou arsenal commun.</summary>
    public WeaponChoice WeaponChoiceNow => rules.Value.Sanitized().EffectiveWeaponChoice;

    /// <summary>L'arsenal commun : l'arme des deux (sa place), ou -1.</summary>
    public int CommonArsenalSlot => WeaponChoiceNow == WeaponChoice.CommonArsenal && commonArsenal.Value.slot != CommonArsenal.NoSlot
        ? commonArsenal.Value.slot : -1;

    /// <summary>Pile ou face : qui choisit la famille de la manche, laquelle, et celle d'avant.</summary>
    public CoinFlip.State CoinFlipNow => coinFlip.Value;

    /// <summary>Pile ou face : la famille de la manche reste-t-elle à choisir ? (en préparation)</summary>
    public bool CoinFlipFamilyPending => WeaponChoiceNow == WeaponChoice.CoinFlip && phase.Value == Phase.Preparation && coinFlip.Value.Pending;

    /// <summary>Pile ou face : secondes avant la fin du choix de la famille.</summary>
    public float CoinFlipTimeLeft => RemainingIn(phaseStartedAt.Value, coinFlip.Value.familyDeadline, Now);

    /// <summary>Pile ou face : secondes depuis le lancer de la pièce (la première manche), ou -1 sans lancer.</summary>
    public float CoinFlipSinceToss => coinFlip.Value.tossedAt > 0d ? (float)(Now - coinFlip.Value.tossedAt) : -1f;

    /// <summary>L'arme que le mode met en main d'un duelliste, ou -1 : chacun garde la sienne (GDD § 11). Le Golden
    /// Gun, la même pour tous ; le Gun Game, celle de sa marche (`gunGameStep`) sur l'échelle de la partie.
    /// Fonction pure, testée.</summary>
    public static int ImposedWeaponSlot(MatchRules rules, int gunGameStep)
    {
        MatchRules r = rules.Sanitized();
        return r.mode switch
        {
            GameModeId.GoldenGun => WeaponController.GoldenGunSlot,
            GameModeId.GunGame => GunGame.SlotAt(r.ladder, gunGameStep),
            _ => -1,
        };
    }

    /// <summary>Les réglages font-ils revenir les morts dans le match, au lieu de finir la manche ? Le Gun Game,
    /// l'Élimination confirmée, la Zone, le Tag Team (un coéquipier entre à la place du mort), et le Death match du
    /// classique (GDD § 11). Fonction pure, testée.</summary>
    public static bool HasRespawns(MatchRules rules)
    {
        MatchRules r = rules.Sanitized();
        return r.mode == GameModeId.GunGame || r.mode == GameModeId.KillConfirmed || r.mode == GameModeId.Zone
            || r.mode == GameModeId.TagTeam || r.IsDeathMatch;
    }

    /// <summary>Le Tag Team se joue-t-il ?</summary>
    public bool TagTeamMode => rules.Value.Sanitized().IsTagTeam;

    /// <summary>Les points d'une équipe du Tag Team.</summary>
    public int TeamPoints(PlayerTeam team) => teamScore.Value.Of(team);

    /// <summary>L'équipe qui a gagné le match du Tag Team (en MatchOver), ou None.</summary>
    public PlayerTeam WinningTeam => winningTeam.Value;

    /// <summary>Chaque machine : les éliminations du Tag Team depuis le début de la partie, et la dernière : son auteur,
    /// le robot détruit, les points rapportés à l'équipe, le coup (à la tête, au gant). Le HUD les sonde.</summary>
    public int TagKills => tagKills;
    public ulong LastTagKiller => lastTagKiller;
    public ulong LastTagVictim => lastTagVictim;
    public int LastTagPoints => lastTagPoints;
    public Health.DamageCause LastTagCause => lastTagCause;

    /// <summary>Chaque machine : les entrées d'un coéquipier dans l'arène depuis le début de la partie, et la dernière.</summary>
    public int TagIns => tagIns;
    public ulong LastTagIn => lastTagIn;

    /// <summary>Deux joueurs sont-ils de la même équipe du Tag Team ? Ni les cailloux ni les coups de la tribune ne
    /// touchent un coéquipier (GDD § 11, TT5 : on y aide son équipe).</summary>
    public static bool Teammates(ulong a, ulong b)
    {
        PlayerMatchState x = PlayerMatchState.Of(a), y = PlayerMatchState.Of(b);
        return x != null && y != null && x.Team != PlayerTeam.None && x.Team == y.Team;
    }

    /// <summary>Les joueurs d'une équipe (sur toutes les machines : l'équipe est publiée dans chaque fiche).</summary>
    public static int TeamCount(PlayerTeam team)
    {
        int count = 0;
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && state.IsSpawned && team != PlayerTeam.None && state.Team == team) count++;
        }
        return count;
    }

    /// <summary>Des équipes de ces tailles peuvent-elles commencer un match ? Autant de chaque côté, deux ou trois
    /// (TagTeam.CanPlay) ; une seule de chaque côté au banc de test (DebugTagTeamUneven).</summary>
    public static bool TagTeamCanStart(int teamA, int teamB)
    {
#if UNITY_EDITOR
        if (DebugTagTeamUneven) return teamA >= 1 && teamB >= 1 && teamA <= TagTeam.MaxTeamSize && teamB <= TagTeam.MaxTeamSize;
#endif
        return TagTeam.CanPlay(teamA, teamB);
    }

    /// <summary>Une élimination compte-t-elle pour son auteur ? Il faut un auteur connu, qui n'est pas le tué.
    /// Fonction pure, testée.</summary>
    public static bool KillScores(ulong killerId, ulong victimId) => killerId != Health.NoKiller && killerId != victimId;

    /// <summary>L'échelle du Gun Game de la partie.</summary>
    public GunGameLadder Ladder => rules.Value.Sanitized().ladder;

    /// <summary>Les modules mémoire à ramasser (l'Élimination confirmée), sur toutes les machines.</summary>
    public int ModuleCount => modules.Count;

    public KillConfirmed.Module ModuleAt(int index) => modules[index];

    /// <summary>Un module de `victimId` attend-il encore son vainqueur ? Le mort qui revient peut le reprendre.</summary>
    public bool HasModuleOf(ulong victimId)
    {
        for (int i = 0; i < modules.Count; i++)
        {
            if (modules[i].victimId == victimId) return true;
        }
        return false;
    }

    /// <summary>Chaque machine : les modules pris depuis le début de la partie. Le HUD et les sons le sondent, et
    /// lisent le dernier : où, par qui, confirmé par son vainqueur (et son compte alors) ou repris par sa victime.</summary>
    public int ModulesTaken => modulesTaken;
    public Vector3 LastModuleAt => lastModuleAt;
    public ulong LastModuleTaker => lastModuleTaker;
    public bool LastModuleConfirmed => lastModuleConfirmed;
    public int LastModuleScore => lastModuleScore;

    /// <summary>La Zone : qui la tient (NoWinner : personne, ou disputée).</summary>
    public ulong ZoneHolder => zoneHolder.Value;

    /// <summary>La Zone : à deux dedans, plus rien n'avance.</summary>
    public bool ZoneContested => zoneContested.Value;

    /// <summary>La zone que chaque machine montre (sa place dans ZoneLayout), ou -1 hors de la Zone : la première dès la
    /// préparation, puis celle du moment pendant le match (ZoneMode.ZoneAt, sur le temps du match publié).</summary>
    public int ShownZone
    {
        get
        {
            if (Mode != GameModeId.Zone) return -1;
            ZoneLayout layout = ZoneLayout.Instance;
            if (layout == null || layout.Count == 0) return -1;
            Phase current = phase.Value;
            if (current == Phase.Active) return ZoneMode.ZoneAt(PhaseElapsed, layout.Count);
            return current == Phase.Preparation || current == Phase.Starting ? 0 : -1;
        }
    }

    /// <summary>La zone suivante, annoncée les dernières secondes avant le changement (ZoneMode.AnnounceLead), ou -1.</summary>
    public int AnnouncedZone
    {
        get
        {
            int shown = ShownZone;
            if (shown < 0 || phase.Value != Phase.Active || !ZoneMode.Announcing(PhaseElapsed)) return -1;
            return (shown + 1) % ZoneLayout.Instance.Count;
        }
    }

    /// <summary>Secondes avant que la zone ne change de place (pendant le match).</summary>
    public float ZoneTimeLeft => ZoneMode.TimeUntilMove(PhaseElapsed);

    /// <summary>Un duelliste peut-il prendre une arme posée (lancée par la tribune) ? Pas quand le mode impose
    /// l'arme (GDD § 11, T7). Sans RoundManager, oui.</summary>
    public static bool WeaponPickupAllowed
    {
        get
        {
            RoundManager rm = Instance;
            return rm == null || !rm.IsSpawned || (!GameModeCatalog.ImposesWeapon(rm.Mode) && rm.WeaponChoiceNow == WeaponChoice.Free);
        }
    }

    /// <summary>Les fiches des joueurs du match que ce RoundManager arbitre : les deux de son couloir, ou les duellistes de
    /// la partie ; personne dans la salle d'attente du Tournoi.</summary>
    public void GetMatchDuelists(List<PlayerMatchState> result)
    {
        result.Clear();
        if (!lanePrefab)
        {
            if (!TournamentMode) GetDuelists(result);
            return;
        }
        LaneInfo info = laneInfo.Value;
        PlayerMatchState a = PlayerMatchState.Of(info.a), b = PlayerMatchState.Of(info.b);
        if (a != null) result.Add(a);
        if (b != null) result.Add(b);
    }

    /// <summary>Les fiches des duellistes présents, dans l'ordre d'apparition (sur toutes les
    /// machines : le rôle est publié dans chaque fiche).</summary>
    public static void GetDuelists(List<PlayerMatchState> result)
    {
        result.Clear();
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && state.IsDuelist) result.Add(state);
        }
    }

    /// <summary>Le temps qui sépare la dernière mort du match de l'écran de fin : la kill cam quand il y en a une, puis
    /// le temps pendant lequel le gagnant garde la main (le même qu'entre deux manches). Au Tournoi, aucun : le champion a
    /// déjà gardé la main dans la finale, son écran vient tout de suite.</summary>
    public float EndScreenDelay => KillReplay.EndScreenDelay(TournamentMode, roundOverDuration, ReplayNow);

    // Le replay publié est-il celui de cette phase ?
    private bool ReplayInPhase => KillReplay.BelongsTo(killReplay.Value, phaseStartedAt.Value);

    /// <summary>Le replay du coup mortel de cette fin de manche (KillReplay), ou rien.</summary>
    public KillReplay.Info ReplayNow => ReplayInPhase ? killReplay.Value : default;

    /// <summary>Le temps écoulé depuis le début du replay (s, horloge du serveur ; négatif avant), ou -∞ sans replay.</summary>
    public float ReplayElapsed => ReplayInPhase ? (float)(Now - killReplay.Value.startsAt) : float.NegativeInfinity;

    /// <summary>Les joueurs sont-ils gelés par le replay ? (KillReplay.Freezes)</summary>
    public bool ReplayFreezes => KillReplay.Freezes(killReplay.Value, phaseStartedAt.Value, Now);

    private double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

    /// <summary>Secondes restantes dans la phase courante, 0 si elle n'a pas de fin.</summary>
    public float PhaseTimeRemaining => RemainingIn(phaseStartedAt.Value, phaseEndsAt.Value, Now);

    /// <summary>
    /// Le temps qui reste d'une phase, jamais plus que sa durée : chez un client, l'horloge du serveur
    /// estimée retarde un peu, et la phase qui vient d'arriver y durait plus longtemps qu'elle ne dure
    /// (un « 4 » fugitif au décompte, et un bip de trop, vus au banc le 2026-10-13). Fonction pure, testée.
    /// </summary>
    public static float RemainingIn(double startedAt, double endsAt, double now)
    {
        if (endsAt <= 0d) return 0f;
        return (float)System.Math.Max(0d, System.Math.Min(endsAt - now, endsAt - startedAt));
    }

    /// <summary>Secondes d'ici l'instant `at` (horloge du serveur), jamais négatives ni plus que l'attente d'une
    /// réapparition : chez un client, l'horloge du serveur estimée retarde un peu (le décompte du joueur mort).</summary>
    public float SecondsUntil(double at) => (float)System.Math.Max(0d, System.Math.Min(respawnDelay, at - Now));

    /// <summary>Secondes écoulées depuis le début de la phase courante. En phase Active, c'est le
    /// temps de la manche, que le HUD affiche (GDD § 15).</summary>
    public float PhaseElapsed => (float)System.Math.Max(0d, Now - phaseStartedAt.Value);

    /// <summary>La phase a-t-elle une fin programmée ?</summary>
    public bool PhaseHasEnd => phaseEndsAt.Value > 0d;

    /// <summary>L'instant (horloge du serveur) où la phase courante a commencé, le même sur toutes
    /// les machines : de quoi y tirer un hasard partagé (les célébrations de l'écran de fin).</summary>
    public double PhaseStartedAt => phaseStartedAt.Value;

    private bool PhaseHasEnded => phaseEndsAt.Value > 0d && Now >= phaseEndsAt.Value;

    /// <summary>Manches gagnées par `clientId` dans le match en cours.</summary>
    public int WinsOf(ulong clientId)
    {
        for (int i = 0; i < scores.Count; i++)
        {
            if (scores[i].clientId == clientId) return scores[i].wins;
        }
        return 0;
    }

    // ------------------------------------------------------------------
    // Ce que chaque phase permet (fonctions pures, testées)
    // ------------------------------------------------------------------

    /// <summary>
    /// Un joueur peut-il bouger, tirer, frapper ? Seulement vivant, et :
    /// - pendant la manche ;
    /// - pendant l'attente d'un adversaire (sinon le premier connecté restait figé, et on ne pouvait
    ///   pas s'échauffer seul) ;
    /// - après une mort, en fin de manche comme en fin de match (2026-10-12, demandé par l'utilisateur :
    ///   le gagnant garde la main pour narguer le perdant). L'écran de fin, lui, coupe les commandes.
    /// Gelé pendant la préparation et le décompte, pour que les deux joueurs partent ensemble, et au
    /// salon, où l'on attend l'hôte.
    ///
    /// Lu depuis `PlayerLocomotion.Move()`, donc depuis une fonction rejouée par la réconciliation :
    /// la valeur utilisée lors d'un rejeu est celle de MAINTENANT, pas celle de l'input rejoué.
    /// C'est le même compromis que pour la posture, et il est acceptable pour la même raison : la
    /// transition arrive une fois par manche, et l'éventuel écart est rattrapé par le seuil de
    /// réconciliation. Un client modifié ne gagne rien à ignorer ce blocage : le serveur applique le
    /// même test sur sa propre simulation, et sur chaque tir.
    /// </summary>
    public static bool PlayAllowed(Phase current, bool alive)
    {
        if (!alive) return false;
        return current == Phase.Active || current == Phase.WaitingForPlayers
            || current == Phase.RoundOver || current == Phase.MatchOver || current == Phase.Tournament;
    }

    /// <summary>
    /// Un joueur peut-il changer d'arme ? Seulement avant la manche (2026-10-12, demandé par
    /// l'utilisateur) : pendant la préparation tant qu'il n'a pas validé, et pendant l'attente d'un
    /// adversaire, pour essayer les armes seul. Plus jamais une fois la manche lancée.
    /// </summary>
    public static bool WeaponSwitchAllowed(Phase current, bool ready)
    {
        return current == Phase.WaitingForPlayers || (current == Phase.Preparation && !ready);
    }

    /// <summary>PlayAllowed pour la phase du match de ce joueur (son couloir au Tournoi). Sans RoundManager (tests, scène
    /// sans boucle de manches), seul le fait d'être en vie compte.</summary>
    public static bool MovementAllowedFor(ulong clientId, bool alive)
    {
        RoundManager rm = For(clientId);
        // Pendant le replay du coup mortel, rien ne bouge sous lui (KillReplay.Freezes).
        return rm == null ? alive : PlayAllowed(rm.phase.Value, alive) && !rm.ReplayFreezes;
    }

    /// <summary>Même règle que le déplacement : le tir et le taser sont permis exactement quand
    /// on peut bouger. Vérifié côté client pour le confort, et surtout côté SERVEUR dans
    /// `WeaponController.ServerFire` et `TaserController` : c'est là que ça compte.</summary>
    public static bool FiringAllowedFor(ulong clientId, bool alive) => MovementAllowedFor(clientId, alive);

    /// <summary>WeaponSwitchAllowed pour la phase en cours (libre sans RoundManager).</summary>
    public static bool WeaponSwitchAllowedFor(ulong clientId, bool ready)
    {
        RoundManager rm = For(clientId);
        // À pile ou face, pas avant que la famille de la manche soit choisie.
        return rm == null || (WeaponSwitchAllowed(rm.phase.Value, ready) && !rm.CoinFlipFamilyPending);
    }

    /// <summary>Ce joueur peut-il prendre cette arme ? Toujours, sauf en préparation à pile ou face : seulement dans la
    /// famille de la manche (chez le joueur et sur le serveur).</summary>
    public static bool WeaponAllowedFor(ulong clientId, WeaponData data)
    {
        RoundManager rm = For(clientId);
        if (rm == null || rm.WeaponChoiceNow != WeaponChoice.CoinFlip || rm.phase.Value != Phase.Preparation) return true;
        return CoinFlip.SlotAllowed(data, rm.coinFlip.Value.family);
    }

    /// <summary>Tête seulement (GDD § 11, I3) vaut-il pour le match de ce joueur ? Seuls ses tirs à la tête blessent.</summary>
    public static bool HeadshotsOnlyFor(ulong clientId)
    {
        RoundManager rm = For(clientId);
        return rm != null && rm.rules.Value.headshotsOnly;
    }

    /// <summary>Les statistiques du match (PlayerMatchState) ne comptent que pendant la manche : pas
    /// les tirs d'échauffement, ni ceux sur l'épave de l'adversaire.</summary>
    public static bool StatsRecordingFor(ulong clientId)
    {
        RoundManager rm = For(clientId);
        return rm != null && rm.phase.Value == Phase.Active;
    }

    // ------------------------------------------------------------------
    // Boucle serveur
    // ------------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        if (lanePrefab)
        {
            if (!lanes.Contains(this)) lanes.Add(this);
            if (IsServer) ServerStartLane();
            return;
        }
        cached = this;
        KillReplay.Ensure(gameObject.scene);
        if (!IsServer) return;
        rules.Value = SessionFlow.ActiveRules.Sanitized();
        // Lancée d'un salon en ligne : tous ses joueurs se connectent à la fois, dans un ordre que rien ne
        // prédit. On les attend un peu pour donner les places de duelliste dans l'ordre du salon (2026-10-27).
        gathering = SessionFlow.LaunchOrder.Count > 0;
        launchedOnline = gathering;
        gatheringEndsAt = Now + gatheringTimeout;
        EnterWaiting();

        // Le joueur du Host naît dans la scène du menu, AVANT que l'arène ne soit chargée (voir
        // « Le menu principal ») : sans point d'apparition ni sol, il y tombait dans le vide pendant
        // le chargement, et l'arène le trouvait des centaines de mètres sous le terrain. L'arène
        // prête, on range ceux qui sont déjà là (duellistes ou spectateurs), puis on les place. Un
        // joueur qui arrive ensuite naît dans l'arène, ou sur la tribune.
        ServerRegisterPlayers();
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player != null) player.ServerMoveToSpawnPoint();
        }
    }

    private void Update()
    {
        // Chaque machine montre les modules publiés (catégorie C).
        if (IsSpawned) moduleViews.Sync(modules, transform);
        if (!IsServer) return;
        if (lanePrefab)
        {
            ServerTickLane();
            return;
        }

        // Qui est là, qui se bat, qui attend : par sondage, comme la mort (voir la doc de classe).
        bool duelistLeft = ServerUpdateRoster();
        if (duelistLeft) ServerOnDuelistLeft();

        switch (phase.Value)
        {
            case Phase.WaitingForPlayers:
                if (TournamentMode)
                {
                    // Le Tournoi part du salon de l'arène, quand l'hôte le lance ; lancé d'un salon en ligne, dès que ses
                    // joueurs sont arrivés.
                    if (gathering) break;
                    if (launchedOnline && presentIds.Count >= MinTournamentPlayers) ServerStartTournament();
                    else EnterSalon();
                    break;
                }
                if (!gathering && HasEnoughPlayers()) StartMatch();
                break;

            case Phase.Preparation:
                if (!HasEnoughPlayers()) { EnterWaiting(); break; }
                ServerTickCoinFlip();
                if (AllPlayersReady() || PhaseHasEnded) SetPhase(Phase.Starting, startingDuration);
                break;

            case Phase.Starting:
                if (!HasEnoughPlayers()) { EnterWaiting(); break; }
                if (PhaseHasEnded) SetPhase(Phase.Active, roundTimeLimit);
                break;

            case Phase.Active:
                if (!HasEnoughPlayers()) { EnterWaiting(); break; }
                if (Respawns) TickRespawnRound();
                else TickActiveRound();
                break;

            case Phase.RoundOver:
                if (PhaseHasEnded) BeginRound();
                break;

            case Phase.MatchOver:
                if (VoteOpen) ServerTallyVotes();
                if (PhaseHasEnded)
                {
                    if (TournamentMode)
                    {
                        // L'écran du champion fini : le salon, sans vote (un tournoi se relance de là).
                        EnterSalon();
                    }
                    else if (wonByForfeit.Value && TagTeamMode)
                    {
                        // Une équipe partie : l'hôte refait les équipes au salon.
                        EnterSalon();
                    }
                    else if (wonByForfeit.Value)
                    {
                        // Après un forfait, pas de vote : la place libre va au premier de la file ; sans
                        // lui, le gagnant attend un adversaire.
                        if (DuelRoster.FillFromQueue(duelistIds, spectatorQueue, DuelistSlotsForSession)) ServerApplyRoles();
                        if (HasEnoughPlayers()) StartMatch();
                        else EnterWaiting();
                    }
                    else if (decision.Value == MatchDecision.Rematch)
                    {
                        // Le roi de la colline : le gagnant reste, le perdant passe sur la tribune, le
                        // premier de la file descend. Le noir est tombé : personne ne voit l'échange.
                        RotateAndFill();
                        if (HasEnoughPlayers()) StartMatch();
                        else EnterSalon();
                    }
                    else
                    {
                        EnterSalon();
                    }
                }
                break;

            case Phase.Salon:
                // On attend l'hôte (ServerLaunchFromSalon).
                break;

            case Phase.Tournament:
                ServerTickTournament();
                break;
        }
    }

    public override void OnNetworkDespawn()
    {
        lanes.Remove(this);
        moduleViews.Clear();
    }

    // Les couloirs d'une partie précédente ne survivent pas à l'entrée en Play Mode (le rechargement de domaine est
    // désactivé dans l'Editor).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetLanes() => lanes.Clear();

    // ------------------------------------------------------------------
    // Qui se bat, qui attend (2026-10-13)
    // ------------------------------------------------------------------

    /// <summary>Serveur : range les joueurs présents (duellistes, file des spectateurs) et publie le
    /// rôle de chacun. Appelé à chaque image, et dès qu'un joueur apparaît (PlayerMatchState), pour
    /// qu'un spectateur ne passe jamais par l'arène.</summary>
    public void ServerRegisterPlayers()
    {
        if (!IsServer) return;
        ServerUpdateRoster();
    }

    /// <summary>Met la composition à jour avec les joueurs présents. Renvoie vrai si un duelliste est
    /// parti.</summary>
    private bool ServerUpdateRoster()
    {
        presentIds.Clear();
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && state.IsSpawned) presentIds.Add(state.OwnerClientId);
        }

        // Au Tournoi, tout le monde joue, armé ; ceux qui partent déclarent forfait.
        if (TournamentMode)
        {
            ServerUpdateTournamentRoster();
            return false;
        }

        // Au Tag Team, les équipes font la composition (et leur combattant qui part, le relais ou le forfait).
        if (TagTeamMode)
        {
            ServerUpdateTagTeamRoster();
            return false;
        }

        int slots = DuelistSlotsForSession;
        bool duelistLeft = false;
        if (gathering)
        {
            // Les joueurs du salon arrivent : les places suivent l'ordre du salon, refaites de zéro à chaque
            // arrivée. Les duellistes qu'il désigne gardent leur place même s'ils arrivent après les autres.
            // Rangés AVANT de clore l'attente : le dernier arrivé prend sa place à l'image même de son arrivée
            // (vu à trois instances le 2026-10-27 : clos d'abord, l'attente le laissait au bout de la file).
            presentRanks.Clear();
            foreach (ulong id in presentIds) presentRanks.Add(SessionFlow.LaunchRankOf(id));
            DuelRoster.ArrangeByRank(presentIds, presentRanks, slots, duelistIds, spectatorQueue);
            UpdateGathering();
        }
        else
        {
            bool noMatch = phase.Value == Phase.WaitingForPlayers || phase.Value == Phase.Salon;
            DuelRoster.Sync(presentIds, duelistIds, spectatorQueue, slots, out duelistLeft, admitNewcomers: noMatch);
            // Une place libre va au premier de la file quand aucun match ne se joue ; celle d'un duelliste
            // parti en plein match, après l'écran de fin du forfait (Update, MatchOver).
            if (noMatch) DuelRoster.FillFromQueue(duelistIds, spectatorQueue, slots);
        }
        ServerApplyRoles();
        byte waiting = (byte)Mathf.Min(spectatorQueue.Count, 255);
        if (spectatorQueueCount.Value != waiting) spectatorQueueCount.Value = waiting;
        return duelistLeft;
    }

    /// <summary>L'attente des joueurs du salon s'arrête quand tous ceux qui y sont encore sont dans l'arène,
    /// ou au bout de `gatheringTimeout` : la composition reprend alors son cours ordinaire (une place de
    /// duelliste restée vide va au premier de la file).</summary>
    private void UpdateGathering()
    {
        bool everyoneHere = true;
        foreach (string playerId in SessionFlow.LaunchOrder)
        {
            // Parti du salon pendant le lancement : on ne l'attend plus.
            if (!OnlineSession.HasPlayer(playerId)) continue;
            if (SessionFlow.LaunchPlayerConnected(playerId, presentIds)) continue;
            everyoneHere = false;
            break;
        }
        if (!everyoneHere && Now < gatheringEndsAt) return;
        gathering = false;
        SessionFlow.LaunchOrder.Clear();
        SessionFlow.LaunchTeams.Clear();
        Debug.Log(everyoneHere ? "[Round] Les joueurs du salon sont tous dans l'arène." : "[Round] Des joueurs du salon ne sont pas arrivés : la partie commence sans eux.");
    }

    /// <summary>Le tirage au sort des duellistes (MatchRules.randomDuelists), au lancement depuis le salon de
    /// l'arène : deux des joueurs présents, au hasard ; les autres attendent dans l'ordre où ils étaient.</summary>
    private void DrawDuelistsAmongPresent()
    {
        var order = new List<ulong>(duelistIds);
        order.AddRange(spectatorQueue);
        List<ulong> drawn = DuelRoster.DrawDuelists(order, DuelistSlotsForSession, draw);
        duelistIds.Clear();
        spectatorQueue.Clear();
        for (int i = 0; i < drawn.Count; i++)
        {
            if (i < DuelistSlotsForSession) duelistIds.Add(drawn[i]);
            else spectatorQueue.Add(drawn[i]);
        }
        ServerApplyRoles();
    }

    /// <summary>
    /// Un match se joue-t-il, qu'un départ ferait perdre par forfait ? De la préparation de la première
    /// manche à la fin de la dernière ; pas en attente, ni une fois le match gagné. Fonction pure, testée.
    /// </summary>
    public static bool ForfeitApplies(Phase current)
        => current == Phase.Preparation || current == Phase.Starting || current == Phase.Active || current == Phase.RoundOver;

    /// <summary>Serveur : un duelliste a quitté la partie.</summary>
    private void ServerOnDuelistLeft()
    {
        CollectDuelists(roundPlayers);
        if (ForfeitApplies(phase.Value) && roundPlayers.Count > 0)
        {
            // Forfait (GDD § 4) : celui qui reste remporte le match. Il garde la main le même temps qu'à
            // une victoire, puis vient l'écran de fin ; un mort au dernier coup se relève pour le voir.
            PlayerLocomotion survivor = roundPlayers[0];
            Health health = survivor.GetComponent<Health>();
            if (health != null && health.IsDead) health.ResetHealth();
            lastRoundWinner.Value = survivor.OwnerClientId;
            wonByForfeit.Value = true;
            Debug.Log($"[Round] Un duelliste a quitté la partie : {PlayerMatchState.NameOf(survivor.OwnerClientId)} gagne le match par forfait.");
            SetPhase(Phase.MatchOver, roundOverDuration + endScreenDuration);
            return;
        }
        // Le perdant d'un match fini qui s'en va : l'écran de fin continue (le vote ne peut plus donner de
        // revanche). Au salon, la place va au premier de la file, et l'on attend toujours l'hôte.
        if (phase.Value == Phase.MatchOver && duelistIds.Contains(lastRoundWinner.Value)) return;
        if (phase.Value == Phase.Salon)
        {
            if (DuelRoster.FillFromQueue(duelistIds, spectatorQueue, DuelistSlotsForSession)) ServerApplyRoles();
            return;
        }

        // Les deux sont partis, ou le gagnant d'un match fini : il n'y a plus rien à montrer.
        if (phase.Value != Phase.WaitingForPlayers)
        {
            Debug.Log("[Round] Un duelliste a quitté la partie : le match s'arrête.");
            DuelRoster.FillFromQueue(duelistIds, spectatorQueue, DuelistSlotsForSession);
            ServerApplyRoles();
            EnterWaiting();
        }
    }

#if UNITY_EDITOR
    /// <summary>BANC DE TEST — Editor uniquement, sur l'hôte : une seule place de duelliste, pour qu'à
    /// deux instances le second joueur soit spectateur. Le duelliste reste seul, en attente (il
    /// s'échauffe), et le spectateur peut tout faire, lancer compris. À éteindre après.</summary>
    public static bool DebugSoloDuel;

    /// <summary>BANC DE TEST — Editor uniquement, sur l'hôte : aucune place de duelliste, tous les joueurs
    /// sont spectateurs (2026-10-27) : à deux instances, ils se battent entre eux sur la tribune (coups,
    /// garde, cailloux). Personne ne joue de match. À éteindre après.</summary>
    public static bool DebugNoDuel;

    /// <summary>BANC DE TEST — Editor uniquement, sur l'hôte : au Tag Team, des équipes inégales, d'un joueur au moins
    /// (2026-11-02) : à trois instances, deux contre un ; le dernier d'un nombre impair joue aussi. À éteindre après.</summary>
    public static bool DebugTagTeamUneven;
    private static int DuelistSlotsForSession => DebugNoDuel ? 0 : DebugSoloDuel ? 1 : DuelRoster.DuelistSlots;

    // Remis à zéro à chaque entrée en Play Mode (le rechargement de domaine est désactivé) : un banc
    // oublié ne doit pas faire du second joueur de l'utilisateur un spectateur. À régler une fois en
    // Play Mode, avant d'héberger.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDebug()
    {
        DebugSoloDuel = false;
        DebugNoDuel = false;
        DebugTagTeamUneven = false;
    }

    private static bool DebugTagTeamUnevenForSession => DebugTagTeamUneven;
#else
    private static int DuelistSlotsForSession => DuelRoster.DuelistSlots;
    private static bool DebugTagTeamUnevenForSession => false;
#endif

    /// <summary>Publie le rôle et la place dans la file de chaque joueur ; prépare ceux qui changent de
    /// rôle (un perdant qui monte sur la tribune, un spectateur qui descend dans l'arène).</summary>
    private void ServerApplyRoles()
    {
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state == null || !state.IsSpawned) continue;
            PlayerRole role = DuelRoster.RoleOf(state.OwnerClientId, duelistIds, spectatorQueue, out int place);
            PlayerRole before = state.Role;
            state.ServerSetRole(role, place);
            if (before != role) ServerOnRoleChanged(state, role);
        }
    }

    private void ServerOnRoleChanged(PlayerMatchState state, PlayerRole role)
    {
        Debug.Log($"[Round] {state.DisplayName} devient {(role == PlayerRole.Duelist ? "duelliste" : "spectateur")}.");
        // Un perdant mort au dernier coup monte sur la tribune en état de marche ; un spectateur qui
        // descend lâche ce qu'il tenait et entre l'arme pleine. Un joueur qui vient d'apparaître est
        // rangé pendant son apparition même (PlayerMatchState) : ses composants qui ne sont pas encore
        // apparus partent déjà de cet état, on ne leur écrit rien.
        if (state.TryGetComponent(out Health health) && health.IsSpawned) health.ResetHealth();
        state.ServerSetReady(false);
        if (state.TryGetComponent(out SpectatorController spectator) && spectator.IsSpawned) spectator.ServerOnRoleChanged(role);
        WeaponController weapon = state.GetComponentInChildren<WeaponController>();
        if (weapon != null && weapon.IsSpawned) weapon.ServerRefillMagazine();
        // Sur la tribune tout de suite ; dans l'arène, il sera placé au début de la manche.
        if (state.TryGetComponent(out PlayerLocomotion locomotion)) locomotion.ServerMoveToSpawnPoint();
    }

    /// <summary>Les duellistes présents (serveur), dans l'ordre du duel.</summary>
    private void CollectDuelists(List<PlayerLocomotion> result)
    {
        result.Clear();
        foreach (ulong id in duelistIds)
        {
            foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
            {
                if (player != null && player.OwnerClientId == id && !player.IsSpectator)
                {
                    result.Add(player);
                    break;
                }
            }
        }
    }

    // Réutilisées à chaque image, pour ne rien allouer pendant une manche.
    private readonly List<PlayerLocomotion> roundPlayers = new List<PlayerLocomotion>();
    private readonly List<bool> roundDead = new List<bool>();
    private readonly List<int> roundScorers = new List<int>();

    private void TickActiveRound()
    {
        // Sondage plutôt qu'abonnement : voir la doc de classe. Seuls les duellistes comptent.
        CollectDuelists(roundPlayers);
        roundDead.Clear();
        foreach (PlayerLocomotion player in roundPlayers)
        {
            Health health = player.GetComponent<Health>();
            roundDead.Add(health != null && health.IsDead);
        }

        if (RoundScorers(roundDead, roundScorers))
        {
            // Les SURVIVANTS marquent — pas "celui qui a tiré". En 1v1 c'est équivalent, et ça évite
            // de faire remonter l'identité du tireur jusqu'ici. À revoir le jour où un joueur pourra
            // mourir autrement que sous les balles de son adversaire (chute, zone, etc.).
            for (int i = 0; i < roundPlayers.Count; i++)
            {
                if (roundDead[i]) ServerNoteKill(roundPlayers[i], roundPlayers[i].GetComponent<Health>());
            }
            float duration = PhaseElapsed;
            ulong winner = NoWinner;
            foreach (int index in roundScorers)
            {
                ulong id = roundPlayers[index].OwnerClientId;
                AddWin(id);
                PlayerMatchState state = roundPlayers[index].GetComponent<PlayerMatchState>();
                if (state != null) state.ServerRecordRoundWin(duration);
                winner = id;
            }
            if (roundScorers.Count != 1) winner = NoWinner;
            if (roundScorers.Count == 0) Debug.Log("[Round] Les deux joueurs sont tombés : manche nulle.");

            EndRound(winner);
            return;
        }

        if (roundTimeLimit > 0f && PhaseHasEnded)
        {
            // Manche nulle : personne ne marque. N'arrive que si une limite a été réglée pour un
            // test : le GDD n'en veut pas (voir le tooltip de roundTimeLimit).
            Debug.Log("[Round] Temps écoulé, manche nulle.");
            EndRound(NoWinner);
        }
    }

    /// <summary>
    /// La manche d'un mode à réapparitions (le Gun Game) : un mort revient `respawnDelay` plus tard ; la mort
    /// compte au moment où on la voit (ServerOnKilledInRespawnMode), et peut finir le match.
    /// </summary>
    private void TickRespawnRound()
    {
        if (TagTeamMode)
        {
            TickTagTeamRound();
            return;
        }
        CollectDuelists(roundPlayers);
        double now = Now;
        foreach (PlayerLocomotion player in roundPlayers)
        {
            Health health = player.GetComponent<Health>();
            PlayerMatchState state = player.GetComponent<PlayerMatchState>();
            if (health == null || state == null) continue;
            if (!health.IsDead)
            {
                if (state.RespawnAt > 0d) state.ServerSetRespawnAt(0d);
                continue;
            }
            if (state.RespawnAt <= 0d)
            {
                ServerNoteKill(player, health);
                ServerOnKilledInRespawnMode(state, health);
                if (phase.Value != Phase.Active) return;
                state.ServerSetRespawnAt(now + respawnDelay);
            }
            else if (now >= state.RespawnAt)
            {
                ServerRespawn(player, state, health);
            }
        }
        if (Mode == GameModeId.Zone && ServerTickZone()) return;
        if (Mode == GameModeId.KillConfirmed) ServerCollectModules();
    }

    /// <summary>Serveur : un duelliste vient de tomber dans un mode à réapparitions. Au Death match, un point à son
    /// vainqueur, et le match au premier qui atteint le compte. À l'Élimination confirmée, son module tombe, que son
    /// vainqueur devra ramasser. Au Gun Game, son vainqueur monte d'une marche et reçoit l'arme suivante (le tué la
    /// sienne à son retour), ou gagne au gant ; un coup de gant avant la dernière marche fait redescendre le tué
    /// (GDD § 11, GG4, GG5).</summary>
    private void ServerOnKilledInRespawnMode(PlayerMatchState victim, Health health)
    {
        ulong killerId = health.ServerKillerId;
        PlayerMatchState killer = KillScores(killerId, victim.OwnerClientId) ? PlayerMatchState.Of(killerId) : null;
        if (killer == null || !killer.IsDuelist) return;

        if (Mode == GameModeId.KillConfirmed)
        {
            ServerDropModule(victim, killerId);
            return;
        }
        if (Mode == GameModeId.Zone)
        {
            ServerAddPoints(killerId, ZoneMode.PointsPerKill);
            return;
        }

        if (DeathMatch)
        {
            AddWin(killerId);
            Debug.Log($"[Round] Death match : {killer.DisplayName} élimine {victim.DisplayName} ({WinsOf(killerId)} sur {RoundsToWin}).");
            if (!TryGetMatchWinner(out _)) return;
            killer.ServerRecordRoundWin(PhaseElapsed);
            EndRound(killerId);
            return;
        }
        if (Mode != GameModeId.GunGame) return;

        GunGameLadder ladder = Ladder;
        bool byGlove = health.ServerKillCause == Health.DamageCause.Glove;
        GunGame.KillOutcome outcome = GunGame.AfterKill(ladder, killer.GunGameStep, victim.GunGameStep, byGlove);
        if (outcome.killerWins)
        {
            Debug.Log($"[Round] Gun Game : {killer.DisplayName} finit l'échelle au gant.");
            AddWin(killerId);
            killer.ServerRecordRoundWin(PhaseElapsed);
            EndRound(killerId);
            return;
        }
        if (outcome.victimStep != victim.GunGameStep)
            Debug.Log($"[Round] Gun Game : {victim.DisplayName} redescend à la marche {outcome.victimStep + 1}.");
        victim.ServerSetGunGameStep(outcome.victimStep);
        if (outcome.killerStep == killer.GunGameStep) return;
        killer.ServerSetGunGameStep(outcome.killerStep);
        Debug.Log($"[Round] Gun Game : {killer.DisplayName} passe à la marche {outcome.killerStep + 1} ({GunGame.WeaponNameAt(ladder, outcome.killerStep)}).");
        // L'arme suivante, tout de suite, au vainqueur vivant ; mort aussi, il la reçoit à son retour.
        if (killer.TryGetComponent(out Health killerHealth) && killerHealth.IsDead) return;
        WeaponController weapon = killer.GetComponentInChildren<WeaponController>();
        if (weapon != null) weapon.ServerForceWeapon(ImposedWeaponSlot(rules.Value, outcome.killerStep));
    }

    // ------------------------------------------------------------------
    // L'Élimination confirmée : les modules (GDD § 11, 2026-11-02)
    // ------------------------------------------------------------------

    // Le décor seul (le layer Default), sous le robot tombé : son module se pose au sol.
    private const int WorldMask = 1;

    /// <summary>Serveur : le robot de `victim` vient d'être détruit par `killerId` : il lâche son module mémoire là où
    /// il est tombé (KillConfirmed.FindDropPoint). Au-delà de MaxModules, le plus ancien disparaît.</summary>
    private void ServerDropModule(PlayerMatchState victim, ulong killerId)
    {
        if (modules.Count >= KillConfirmed.MaxModules) modules.RemoveAt(0);
        var module = new KillConfirmed.Module
        {
            id = nextModuleId++,
            position = KillConfirmed.FindDropPoint(victim.transform.position, WorldMask),
            victimId = victim.OwnerClientId,
            killerId = killerId,
        };
        modules.Add(module);
        Debug.Log($"[Round] Élimination confirmée : {PlayerMatchState.NameOf(killerId)} élimine {victim.DisplayName}, son module tombe en {module.position.ToString("F1")}.");
    }

    /// <summary>Serveur : qui prend les modules à cette image (KillConfirmed.Resolve). Le vainqueur qui ramasse le
    /// sien marque, et peut gagner le match ; la victime qui reprend le sien annule l'élimination. Chaque machine
    /// l'apprend (ModuleTakenClientRpc).</summary>
    private void ServerCollectModules()
    {
        for (int i = modules.Count - 1; i >= 0; i--)
        {
            KillConfirmed.Module module = modules[i];
            PlayerLocomotion killer = DuelistAmongRound(module.killerId);
            PlayerLocomotion victim = DuelistAmongRound(module.victimId);
            KillConfirmed.Taker taker = KillConfirmed.Resolve(module.position,
                IsAlive(killer), killer != null ? killer.transform.position : Vector3.zero,
                IsAlive(victim), victim != null ? victim.transform.position : Vector3.zero);
            if (taker == KillConfirmed.Taker.None) continue;

            modules.RemoveAt(i);
            bool confirmed = taker == KillConfirmed.Taker.Killer;
            ulong takerId = confirmed ? module.killerId : module.victimId;
            if (confirmed) AddWin(takerId);
            ModuleTakenClientRpc(module.position, takerId, confirmed, WinsOf(takerId));
            if (!confirmed)
            {
                Debug.Log($"[Round] Élimination confirmée : {PlayerMatchState.NameOf(takerId)} reprend son module.");
                continue;
            }
            Debug.Log($"[Round] Élimination confirmée : {PlayerMatchState.NameOf(takerId)} confirme ({WinsOf(takerId)} sur {RoundsToWin}).");
            if (!TryGetMatchWinner(out _)) continue;
            if (killer.TryGetComponent(out PlayerMatchState state)) state.ServerRecordRoundWin(PhaseElapsed);
            EndRound(takerId);
            return;
        }
    }

    private readonly List<Vector3> respawnAvoid = new List<Vector3>();

    /// <summary>Où le mort `clientId` ne réapparaît pas, et à quelle distance : à l'Élimination confirmée, sur ses
    /// modules qui attendent encore leur vainqueur (KillConfirmed.RespawnClearance) ; à la Zone, dans la zone du moment
    /// ou à son bord (ZoneMode.RespawnClearance au-delà du rayon).</summary>
    private List<Vector3> RespawnAvoid(ulong clientId, out float clearance)
    {
        respawnAvoid.Clear();
        clearance = 0f;
        if (Mode == GameModeId.KillConfirmed)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i].victimId == clientId) respawnAvoid.Add(modules[i].position);
            }
            clearance = KillConfirmed.RespawnClearance;
        }
        else if (Mode == GameModeId.Zone)
        {
            ZoneLayout layout = ZoneLayout.Instance;
            int zone = ShownZone;
            if (layout != null && zone >= 0)
            {
                respawnAvoid.Add(layout.Center(zone));
                clearance = layout.Radius + ZoneMode.RespawnClearance;
            }
        }
        return respawnAvoid;
    }

    /// <summary>Le duelliste `clientId` parmi ceux de la manche (roundPlayers), ou null.</summary>
    private PlayerLocomotion DuelistAmongRound(ulong clientId)
    {
        foreach (PlayerLocomotion player in roundPlayers)
        {
            if (player != null && player.OwnerClientId == clientId) return player;
        }
        return null;
    }

    private static bool IsAlive(PlayerLocomotion player)
        => player != null && player.TryGetComponent(out Health health) && !health.IsDead;

    // ------------------------------------------------------------------
    // La Zone : qui la tient, les points (GDD § 11, 2026-11-02)
    // ------------------------------------------------------------------

    /// <summary>Serveur : qui tient la zone du moment à cette image (seul dedans et en vie : ZoneMode.HoldOf), publié ;
    /// il en gagne les points (ZoneMode.Earn). Vrai si le match vient de se finir.</summary>
    private bool ServerTickZone()
    {
        ZoneLayout layout = ZoneLayout.Instance;
        int zone = layout != null ? ZoneMode.ZoneAt(PhaseElapsed, layout.Count) : -1;
        ulong holder = NoWinner;
        int inside = 0;
        if (zone >= 0)
        {
            foreach (PlayerLocomotion player in roundPlayers)
            {
                if (!IsAlive(player) || !layout.Contains(zone, player.transform.position)) continue;
                inside++;
                holder = player.OwnerClientId;
            }
        }
        ZoneMode.Hold hold = ZoneMode.HoldOf(inside);
        if (hold != ZoneMode.Hold.Held) holder = NoWinner;
        bool contested = hold == ZoneMode.Hold.Contested;
        if (zoneHolder.Value != holder) zoneHolder.Value = holder;
        if (zoneContested.Value != contested) zoneContested.Value = contested;
        if (holder == NoWinner) return false;

        zoneCarry.TryGetValue(holder, out float carry);
        int points = ZoneMode.Earn(Time.deltaTime, ref carry);
        zoneCarry[holder] = carry;
        return points > 0 && ServerAddPoints(holder, points);
    }

    /// <summary>Serveur : `points` à `clientId`, à la Zone ; le premier au compte gagne le match. Vrai s'il vient de le
    /// gagner.</summary>
    private bool ServerAddPoints(ulong clientId, int points)
    {
        AddScore(clientId, points);
        if (!TryGetMatchWinner(out ulong winner) || winner != clientId) return false;
        PlayerMatchState state = PlayerMatchState.Of(clientId);
        if (state != null) state.ServerRecordRoundWin(PhaseElapsed);
        Debug.Log($"[Round] Zone : {PlayerMatchState.NameOf(clientId)} atteint {ZoneMode.Points(WinsOf(clientId))} points.");
        EndRound(clientId);
        return true;
    }

    /// <summary>Serveur : personne ne tient la zone, rien n'est en cours (un match commence ou s'arrête).</summary>
    private void ServerResetZone()
    {
        zoneCarry.Clear();
        if (zoneHolder.Value != NoWinner) zoneHolder.Value = NoWinner;
        if (zoneContested.Value) zoneContested.Value = false;
    }

    /// <summary>Serveur : plus aucun module dans l'arène (un match commence ou s'arrête).</summary>
    private void ServerClearModules()
    {
        if (modules.Count > 0) modules.Clear();
    }

    /// <summary>Un module vient d'être pris en `position` (décidé par le serveur, chez tous) : par son vainqueur, qui
    /// en est à `score` éliminations confirmées, ou par sa victime. Le HUD l'annonce et les sons le font entendre
    /// (catégorie B), en sondant ModulesTaken.</summary>
    [ClientRpc]
    private void ModuleTakenClientRpc(Vector3 position, ulong takerId, bool confirmed, int score)
    {
        modulesTaken++;
        lastModuleAt = position;
        lastModuleTaker = takerId;
        lastModuleConfirmed = confirmed;
        lastModuleScore = score;
    }

    /// <summary>Serveur : un mort revient. Posé d'abord (ses pièces et son épave restent où il est tombé), puis
    /// soigné : les autres le voient apparaître à son point, pas à sa place d'avant. L'arme de sa marche, pleine.</summary>
    private void ServerRespawn(PlayerLocomotion player, PlayerMatchState state, Health health)
    {
        state.ServerSetRespawnAt(0d);
        player.ServerRespawnAwayFromOpponents(RespawnAvoid(player.OwnerClientId, out float clearance), clearance);
        health.ResetHealth();
        WeaponController weapon = player.GetComponentInChildren<WeaponController>();
        if (weapon == null) return;
        weapon.ServerRefillMagazine();
        int imposed = ImposedWeaponSlot(rules.Value, state.GunGameStep);
        if (imposed >= 0) weapon.ServerForceWeapon(imposed);
    }

    /// <summary>
    /// La manche est-elle finie, et qui marque ? Finie dès qu'un joueur est mort ; marquent ceux qui
    /// sont encore en vie (indices dans `dead`, écrits dans `scorers`). Fonction pure, testée.
    ///
    /// Corrigé le 2026-10-12 : le « survivant » était tout joueur autre que le premier mort trouvé,
    /// même mort lui aussi. Le serveur refuse désormais le tir d'un robot mort (le premier tir reçu
    /// gagne, tranché par l'utilisateur) ; si les deux tombaient malgré tout, personne ne marquerait.
    /// </summary>
    public static bool RoundScorers(IReadOnlyList<bool> dead, List<int> scorers)
    {
        scorers.Clear();
        bool anyDead = false;
        for (int i = 0; i < dead.Count; i++)
        {
            if (dead[i]) anyDead = true;
            else scorers.Add(i);
        }
        if (!anyDead) scorers.Clear();
        return anyDead;
    }

    private void EndRound(ulong winner)
    {
        lastRoundWinner.Value = winner;
        // Une manche (ou le match) finie sur une élimination se revoit tout de suite (la kill cam, KillReplay), puis le
        // gagnant garde la main.
        bool replay = ServerReplayWanted(winner);
        float replayTime = replay ? KillReplay.PhaseExtension(lastKill) : 0f;
        if (TryGetMatchWinner(out ulong matchWinner))
        {
            ServerClearModules();
            ServerResetZone();
            lastRoundWinner.Value = matchWinner;
            Debug.Log($"[Round] Match remporté par {PlayerMatchState.NameOf(matchWinner)}.");
            SetPhase(Phase.MatchOver, (lanePrefab ? roundOverDuration : roundOverDuration + endScreenDuration) + replayTime);
            if (replay) ServerPublishReplay();
            return;
        }

        if (winner != NoWinner) Debug.Log($"[Round] {PlayerMatchState.NameOf(winner)} gagne la manche {roundNumber.Value}.");
        SetPhase(Phase.RoundOver, roundOverDuration + replayTime);
        if (replay) ServerPublishReplay();
    }

    // ------------------------------------------------------------------
    // Le replay du coup mortel (KillReplay, 2026-11-03)
    // ------------------------------------------------------------------

    /// <summary>Serveur : un duelliste vient de tomber (vu par sondage) : son vainqueur, comment, avec quelle arme et de
    /// combien, au cas où cette mort finit la manche. Se tuer, ou tomber sans auteur connu, ne se revoit pas.</summary>
    private void ServerNoteKill(PlayerLocomotion victim, Health health)
    {
        lastKillAt = 0d;
        if (victim == null || health == null) return;
        ulong killerId = health.ServerKillerId;
        if (!KillScores(killerId, victim.OwnerClientId)) return;
        PlayerMatchState killer = PlayerMatchState.Of(killerId);
        if (killer == null) return;
        WeaponController weapon = killer.GetComponentInChildren<WeaponController>();
        lastKill = new KillReplay.Info
        {
            killer = killerId,
            victim = victim.OwnerClientId,
            cause = health.ServerKillCause,
            weaponSlot = weapon != null ? weapon.PublishedSlot : (byte)0,
            distanceDm = KillReplay.Decimeters(Vector3.Distance(killer.transform.position, victim.transform.position)),
        };
        lastKillAt = Now;
    }

    /// <summary>Serveur : la fin de manche (ou de match) gagnée par `winner` se revoit-elle ? Oui si elle tombe sur
    /// l'élimination qu'il vient de faire (KillReplay.IsFresh) ; pas une zone tenue, un module ramassé, un forfait.</summary>
    private bool ServerReplayWanted(ulong winner)
        => winner != NoWinner && KillReplay.IsFresh(lastKillAt, Now) && lastKill.killer == winner
           && PlayerMatchState.Of(lastKill.victim) != null;

    /// <summary>Serveur : la kill cam de la dernière élimination part aussitôt (KillReplay.LeadIn : le temps de voir le
    /// coup porter). Appelé juste après SetPhase, qui efface celle d'avant.</summary>
    private void ServerPublishReplay()
    {
        KillReplay.Info info = lastKill;
        info.startsAt = Now + KillReplay.LeadIn;
        killReplay.Value = info;
        lastKillAt = 0d;
        Debug.Log($"[Round] Kill cam du coup de {PlayerMatchState.NameOf(info.killer)} ({KillReplay.DurationOf(info):F1} s).");
    }

    private void StartMatch()
    {
        wonByForfeit.Value = false;
        decision.Value = MatchDecision.Pending;
        if (!lanePrefab) ClearVotes();
        scores.Clear();
        CollectDuelists(roundPlayers);
        foreach (PlayerLocomotion player in roundPlayers)
        {
            scores.Add(new Score { clientId = player.OwnerClientId, wins = 0 });
            PlayerMatchState state = player.GetComponent<PlayerMatchState>();
            if (state != null)
            {
                // Au Tournoi, elles comptent tout le tournoi (remises à zéro à son début, ServerStartTournament).
                if (!lanePrefab) state.ServerResetStats();
                // Le Gun Game repart de la première arme.
                state.ServerSetGunGameStep(0);
                state.ServerSetRespawnAt(0d);
            }
        }
        // Au Tag Team, ceux qui attendent sur la tribune jouent aussi le match : leurs points et leurs statistiques
        // repartent de zéro, et ceux des équipes aussi.
        if (TagTeamMode)
        {
            ServerForEachWaitingMember(state =>
            {
                scores.Add(new Score { clientId = state.OwnerClientId, wins = 0 });
                state.ServerResetStats();
                state.ServerSetRespawnAt(0d);
            });
        }
        teamScore.Value = default;
        winningTeam.Value = PlayerTeam.None;
        ServerCaptureRoster();

        roundNumber.Value = 0;
        ServerResetZone();
        ServerResetWeaponChoice();
        BeginRound();
    }

    /// <summary>Serveur : retient les joueurs du match qui commence, pour le tableau de la soirée (les duellistes ; au Tag
    /// Team, les membres des deux équipes), et leur pseudo.</summary>
    private void ServerCaptureRoster()
    {
        matchRoster.Clear();
        if (TagTeamMode)
        {
            foreach (ulong id in teamA) matchRoster.Add((id, PlayerTeam.A));
            foreach (ulong id in teamB) matchRoster.Add((id, PlayerTeam.B));
        }
        else
        {
            CollectDuelists(roundPlayers);
            foreach (PlayerLocomotion player in roundPlayers) matchRoster.Add((player.OwnerClientId, PlayerTeam.None));
        }
        foreach (var (id, _) in matchRoster) knownNames[id] = PlayerMatchState.NameOf(id);
    }

    private string KnownName(ulong id) => knownNames.TryGetValue(id, out string name) ? name : PlayerMatchState.NameOf(id);

    /// <summary>Serveur : le match qui finit entre au tableau de la soirée (Evening) : ses vainqueurs et ses vaincus, son
    /// score (les manches, les éliminations, les points ; rien au Gun Game, que l'échelle départage), et le forfait. Un
    /// tournoi compte pour un match, gagné par son champion contre tous les autres joueurs du tournoi.</summary>
    private void ServerRecordEvening()
    {
        MatchRules r = rules.Value.Sanitized();
        ulong winner = lastRoundWinner.Value;
        var winners = new List<string>();
        var losers = new List<string>();
        int winnerScore = 0, loserScore = 0;
        if (r.IsTournament)
        {
            if (winner == NoWinner) return;
            winners.Add(KnownName(winner));
            for (int i = 0; i < tournamentPlayers.Count; i++)
            {
                if (tournamentPlayers[i] != winner) losers.Add(KnownName(tournamentPlayers[i]));
            }
        }
        else if (r.IsTagTeam)
        {
            PlayerTeam team = winningTeam.Value;
            if (team == PlayerTeam.None) return;
            foreach (var (id, member) in matchRoster) (member == team ? winners : losers).Add(KnownName(id));
            winnerScore = teamScore.Value.Of(team);
            loserScore = teamScore.Value.Of(team == PlayerTeam.A ? PlayerTeam.B : PlayerTeam.A);
        }
        else
        {
            if (winner == NoWinner) return;
            winners.Add(KnownName(winner));
            foreach (var (id, _) in matchRoster)
            {
                if (id == winner) continue;
                losers.Add(KnownName(id));
                loserScore = Mathf.Max(loserScore, WinsOf(id));
            }
            winnerScore = WinsOf(winner);
            if (r.mode == GameModeId.GunGame) winnerScore = loserScore = 0;
        }
        evening.Add(Evening.Record(r.mode, winners, losers, winnerScore, loserScore, wonByForfeit.Value));
        while (evening.Count > Evening.MaxMatches) evening.RemoveAt(0);
    }

    /// <summary>Le poste du duelliste `duelist` (son rang parmi les duellistes) à la manche `round` : en manches, le premier
    /// au poste A (0) et le second au B (1), et l'on change de côté à chaque manche (GDD § 5, T3). Fonction pure, testée.</summary>
    public static int SidePost(int duelist, int round) => (Mathf.Max(0, duelist) + Mathf.Max(1, round) - 1) % 2;

    /// <summary>Remet les duellistes en jeu et ouvre la préparation de la manche suivante. Les
    /// spectateurs restent où ils sont, sur la tribune ; les objets restés dans l'arène disparaissent.</summary>
    private void BeginRound()
    {
        if (!HasEnoughPlayers())
        {
            // Un couloir voit ses joueurs partir dans ServerTickLane (le forfait) ; seul celui de la scène attend.
            if (!lanePrefab) EnterWaiting();
            return;
        }

        roundNumber.Value++;
        ulong previousWinner = lastRoundWinner.Value;
        lastRoundWinner.Value = NoWinner;
        // L'arsenal commun : une nouvelle arme toutes les deux manches ; sinon chacun retrouve ses accessoires.
        WeaponChoice choice = WeaponChoiceNow;
        if (choice == WeaponChoice.CommonArsenal) ServerDrawCommonArsenal();
        else ServerRestoreAttachments();
        Tribune tribune = Tribune.Instance;
        if (tribune != null && !lanePrefab) tribune.ServerClearArena();
        ServerClearModules();

        CollectDuelists(roundPlayers);
        for (int index = 0; index < roundPlayers.Count; index++)
        {
            PlayerLocomotion player = roundPlayers[index];

            // On soigne AVANT de replacer. Sans postes dans la scène, ServerMoveToSpawnPoint prend le point le plus
            // éloigné des autres joueurs : replacer le premier influence le choix du second, ce qui les met aux deux
            // bouts même s'ils sont morts au même endroit.
            Health health = player.GetComponent<Health>();
            if (health != null) health.ResetHealth();

            // Chargeur plein et rechargement annulé : on ne reprend pas une manche avec le
            // chargeur de la précédente. L'arme du mode s'il en impose une (le Golden Gun) ; sinon celle
            // que le joueur a choisie, une arme de l'arsenal (le mode a pu changer au salon).
            PlayerMatchState state = player.GetComponent<PlayerMatchState>();
            WeaponController weapon = player.GetComponentInChildren<WeaponController>();
            if (weapon != null)
            {
                weapon.ServerRefillMagazine();
                int imposed = ImposedWeaponSlot(rules.Value, state != null ? state.GunGameStep : 0);
                // L'arsenal commun : la même arme pour les deux, avec les mêmes accessoires, posés avant (le chargeur en dépend).
                if (imposed < 0 && choice == WeaponChoice.CommonArsenal && CommonArsenalSlot >= 0)
                {
                    ServerApplyCommonAttachments(player);
                    imposed = CommonArsenalSlot;
                }
                if (imposed >= 0) weapon.ServerForceWeapon(imposed);
                else weapon.ServerReturnToArsenal();
            }

            if (state != null)
            {
                state.ServerSetReady(false);
                state.ServerSetRespawnAt(0d);
            }

            // En manches, les postes A et B, et l'on change de côté à chaque manche (GDD § 5, T3).
            player.ServerMoveToSpawnPoint(SidePost(index, roundNumber.Value));
        }

        // Au Tag Team, ceux qui attendent leur tour choisissent aussi leur arme, sur la tribune : la leur, pleine.
        if (TagTeamMode)
        {
            ServerForEachWaitingMember(state =>
            {
                if (state.TryGetComponent(out Health health) && health.IsDead) health.ResetHealth();
                WeaponController weapon = state.GetComponentInChildren<WeaponController>();
                if (weapon != null)
                {
                    weapon.ServerRefillMagazine();
                    weapon.ServerReturnToArsenal();
                }
                state.ServerSetReady(false);
                state.ServerSetRespawnAt(0d);
            });
        }

        // Pile ou face : la pièce, ou le perdant de la manche d'avant, choisit la famille.
        bool tossed = choice == WeaponChoice.CoinFlip && ServerStartCoinFlip(previousWinner);

        // Sans préparation (le mode impose l'arme, ou l'arsenal commun), le décompte part dès le replacement. Quand la pièce
        // tourne, la préparation dure d'autant plus : 10 s pour la famille, puis 20 pour l'arme (GDD § 11, P2).
        if (HasPreparation(rules.Value)) SetPhase(Phase.Preparation, preparationDuration + (tossed ? CoinFlip.TossSeconds : 0f));
        else SetPhase(Phase.Starting, startingDuration);
    }

    /// <summary>Plus assez de joueurs : on attend le suivant. Ceux qui restent sont soignés (un
    /// joueur tué juste avant le départ de son adversaire resterait sinon une épave), et libres de
    /// s'échauffer.</summary>
    private void EnterWaiting()
    {
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player == null) continue;
            Health health = player.GetComponent<Health>();
            if (health != null && health.IsDead) health.ResetHealth();
            PlayerMatchState state = player.GetComponent<PlayerMatchState>();
            if (state != null)
            {
                state.ServerSetReady(false);
                state.ServerSetRespawnAt(0d);
            }
        }
        lastRoundWinner.Value = NoWinner;
        wonByForfeit.Value = false;
        decision.Value = MatchDecision.Pending;
        teamScore.Value = default;
        winningTeam.Value = PlayerTeam.None;
        ClearVotes();
        ServerClearModules();
        ServerResetZone();
        ServerRestoreAttachments();
        ServerResetWeaponChoice();
        SetPhase(Phase.WaitingForPlayers, 0f);
    }

    // ------------------------------------------------------------------
    // La fin du match : le vote, le salon (2026-10-26)
    // ------------------------------------------------------------------

    /// <summary>
    /// Le vote de fin de match, selon les règles de l'utilisateur : la revanche demande les voix des deux
    /// duellistes ; une voix pour le salon, ou le temps écoulé sans les deux voix, ramènent au salon.
    /// `votes` : celles des duellistes présents. Fonction pure, testée.
    /// </summary>
    public static MatchDecision DecideVote(IReadOnlyList<PlayerMatchState.MatchVote> votes, bool timeUp)
    {
        int rematch = 0;
        foreach (PlayerMatchState.MatchVote vote in votes)
        {
            if (vote == PlayerMatchState.MatchVote.Salon) return MatchDecision.Salon;
            if (vote == PlayerMatchState.MatchVote.Rematch) rematch++;
        }
        if (rematch >= DuelRoster.DuelistSlots && rematch == votes.Count) return MatchDecision.Rematch;
        return timeUp ? MatchDecision.Salon : MatchDecision.Pending;
    }

    private readonly List<PlayerMatchState.MatchVote> roundVotes = new List<PlayerMatchState.MatchVote>();

    /// <summary>Serveur : compte les voix des joueurs du match (les duellistes ; au Tag Team, tous les membres des
    /// équipes) ; une fois le vote tranché, l'écran de fin ne dure plus que `decisionTail` (le noir tombe, puis la
    /// revanche ou le salon).</summary>
    private void ServerTallyVotes()
    {
        roundVotes.Clear();
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && state.IsSpawned && state.IsMatchPlayer) roundVotes.Add(state.Vote);
        }
        MatchDecision decided = DecideVote(roundVotes, PhaseHasEnded);
        if (decided == MatchDecision.Pending) return;
        decision.Value = decided;
        Debug.Log($"[Round] Fin du match : {(decided == MatchDecision.Rematch ? "revanche" : "retour au salon")}.");
        if (!PhaseHasEnded) phaseEndsAt.Value = Now + decisionTail;
    }

    private void ClearVotes()
    {
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && state.IsSpawned) state.ServerClearVote();
        }
    }

    /// <summary>La rotation du roi de la colline, puis les places libres aux premiers de la file. Au Tag Team, les
    /// équipes restent telles que l'hôte les a faites.</summary>
    private void RotateAndFill()
    {
        if (TagTeamMode || TournamentMode) return;
        bool changed = DuelRoster.RotateAfterMatch(rules.Value.organization, duelistIds, spectatorQueue, lastRoundWinner.Value);
        changed |= DuelRoster.FillFromQueue(duelistIds, spectatorQueue, DuelistSlotsForSession);
        if (changed) ServerApplyRoles();
    }

    /// <summary>Le salon : le match suivant se prépare (les rôles ont tourné), tout le monde est soigné et
    /// gelé, et l'on attend que l'hôte relance, après avoir changé les réglages s'il le veut.</summary>
    private void EnterSalon()
    {
        RotateAndFill();
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player == null) continue;
            Health health = player.GetComponent<Health>();
            if (health != null && health.IsDead) health.ResetHealth();
            PlayerMatchState state = player.GetComponent<PlayerMatchState>();
            if (state != null)
            {
                state.ServerSetReady(false);
                state.ServerSetRespawnAt(0d);
            }
        }
        ClearVotes();
        ServerClearModules();
        ServerResetZone();
        lastRoundWinner.Value = NoWinner;
        wonByForfeit.Value = false;
        decision.Value = MatchDecision.Pending;
        teamScore.Value = default;
        winningTeam.Value = PlayerTeam.None;
        ServerEndTournament();
        ServerRestoreAttachments();
        ServerResetWeaponChoice();
        Debug.Log("[Round] Retour au salon de la partie.");
        SetPhase(Phase.Salon, 0f);
    }

    /// <summary>L'hôte peut-il relancer depuis le salon ? Il faut deux duellistes ; au Tag Team, deux équipes égales
    /// de deux ou trois joueurs.</summary>
    public bool CanLaunchFromSalon => phase.Value == Phase.Salon
        && (TournamentMode ? PlayerMatchState.Spawned.Count >= MinTournamentPlayers
            : TagTeamMode ? TagTeamCanStart(TeamCount(PlayerTeam.A), TeamCount(PlayerTeam.B)) : DuelistCount >= DuelRoster.DuelistSlots);

    /// <summary>Les duellistes présents (sur toutes les machines : les rôles sont publiés).</summary>
    public static int DuelistCount
    {
        get
        {
            int count = 0;
            foreach (PlayerMatchState state in PlayerMatchState.Spawned)
            {
                if (state != null && state.IsSpawned && state.IsDuelist) count++;
            }
            return count;
        }
    }

    /// <summary>L'hôte relance la partie depuis le salon (SalonPanel, sur sa machine : le serveur).</summary>
    public void ServerLaunchFromSalon()
    {
        if (!IsServer || phase.Value != Phase.Salon) return;
        if (TournamentMode)
        {
            if (presentIds.Count < MinTournamentPlayers) return;
            Debug.Log("[Round] L'hôte lance le tournoi.");
            ServerStartTournament();
            return;
        }
        if (TagTeamMode)
        {
            if (!HasEnoughPlayers()) return;
            Debug.Log("[Round] L'hôte relance la partie (Tag Team).");
            StartMatch();
            return;
        }
        if (DuelRoster.FillFromQueue(duelistIds, spectatorQueue, DuelistSlotsForSession)) ServerApplyRoles();
        if (!HasEnoughPlayers()) return;
        // Le tirage au sort : le salon n'a pas dit qui jouerait, il se décide maintenant.
        if (rules.Value.randomDuelists) DrawDuelistsAmongPresent();
        Debug.Log("[Round] L'hôte relance la partie.");
        StartMatch();
    }

    /// <summary>
    /// L'hôte change les réglages au salon : publiés à tous, appliqués aux connexions suivantes
    /// (SessionFlow.ActiveRules), retenus pour sa prochaine partie et publiés au salon en ligne. Jamais
    /// moins de places que de joueurs présents.
    /// </summary>
    public void ServerSetRules(MatchRules value)
    {
        if (!IsServer || phase.Value != Phase.Salon) return;
        MatchRules r = value.Sanitized();
        int floor = Mathf.Clamp(PlayerMatchState.Spawned.Count, MatchRules.MinCapacity, MatchRules.MaxCapacity);
        if (r.capacity < floor) r.capacity = (byte)floor;
        bool wasTagTeam = TagTeamMode;
        bool wasTournament = TournamentMode;
        rules.Value = r;
        SessionFlow.ActiveRules = r;
        GameSettings.HostRules = r;
        if (OnlineSession.IsHost) OnlineSession.SetRules(r);
        // Passer au Tag Team fait des équipes avec les joueurs présents (dans l'ordre : les duellistes, puis la file) ;
        // en sortir rend la composition ordinaire (les deux combattants duellistes, les autres dans la file).
        if (r.IsTagTeam && !wasTagTeam) ServerTeamsFromRoster();
        else if (!r.IsTagTeam && wasTagTeam) ServerRosterFromTeams();
        // Quitter le Tournoi (où tout le monde était armé) : la composition ordinaire se refait avec les joueurs présents.
        else if (!r.IsTournament && wasTournament)
        {
            duelistIds.Clear();
            spectatorQueue.Clear();
        }
    }

    /// <summary>Change de phase. Une durée nulle ou négative signifie : pas de fin programmée.</summary>
    private void SetPhase(Phase next, float duration)
    {
        double now = Now;
        // Un match qui finit entre au tableau de la soirée (pas le match d'un couloir du Tournoi : le tournoi compte).
        bool matchEnds = next == Phase.MatchOver && phase.Value != Phase.MatchOver && !lanePrefab;
        if (killReplay.Value.Scheduled) killReplay.Value = default;
        phase.Value = next;
        phaseStartedAt.Value = now;
        phaseEndsAt.Value = duration > 0f ? now + duration : 0d;
        if (matchEnds) ServerRecordEvening();
    }

    /// <summary>Deux duellistes présents : un duel peut se jouer. Au Tag Team, pour commencer un match (en attente, au
    /// salon, à la revanche), deux équipes égales (TagTeamCanStart) ; pour le continuer, quelqu'un dans chaque équipe.</summary>
    private bool HasEnoughPlayers()
    {
        if (TagTeamMode)
        {
            Phase current = phase.Value;
            bool starting = current == Phase.WaitingForPlayers || current == Phase.Salon || current == Phase.MatchOver;
            return starting ? TagTeamCanStart(teamA.Count, teamB.Count) : teamA.Count > 0 && teamB.Count > 0;
        }
        CollectDuelists(roundPlayers);
        return roundPlayers.Count >= DuelRoster.DuelistSlots;
    }

    /// <summary>Les deux duellistes ont validé leur arme (les spectateurs n'en choisissent pas) ; au Tag Team, tous
    /// les membres des équipes.</summary>
    private bool AllPlayersReady()
    {
        CollectDuelists(roundPlayers);
        foreach (PlayerLocomotion player in roundPlayers)
        {
            PlayerMatchState state = player.GetComponent<PlayerMatchState>();
            if (state == null || !state.Ready) return false;
        }
        if (TagTeamMode && !WaitingMembersReady(teamA)) return false;
        if (TagTeamMode && !WaitingMembersReady(teamB)) return false;
        return roundPlayers.Count > 0;
    }

    /// <summary>Ceux qui attendent leur tour dans `team` (tous sauf le combattant) ont-ils validé leur arme ?</summary>
    private static bool WaitingMembersReady(List<ulong> team)
    {
        for (int i = 1; i < team.Count; i++)
        {
            PlayerMatchState state = PlayerMatchState.Of(team[i]);
            if (state != null && !state.Ready) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------
    // Le Tag Team : les équipes, le relais, les points (GDD § 11, 2026-11-02)
    // ------------------------------------------------------------------

    /// <summary>
    /// Serveur : la composition au Tag Team. Les absents sortent de leur équipe. En attente d'un match (le réseau
    /// local), les équipes se font d'elles-mêmes dans l'ordre d'arrivée (TagTeam.AutoTeams) ; une partie lancée d'un
    /// salon en ligne reprend celles du salon ; pendant un match et au salon, un nouveau venu attend hors équipe, sur
    /// la tribune (l'hôte le range au salon). Un combattant parti laisse la place à son coéquipier ; une équipe vide perd
    /// par forfait.
    /// </summary>
    private void ServerUpdateTagTeamRoster()
    {
        bool matchRunning = ForfeitApplies(phase.Value);
        bool fighterLeft = RemoveAbsent(teamA) | RemoveAbsent(teamB);
        for (int i = spectatorQueue.Count - 1; i >= 0; i--)
        {
            if (!presentIds.Contains(spectatorQueue[i])) spectatorQueue.RemoveAt(i);
        }

        if (gathering)
        {
            ArrangeTeamsFromLaunch();
            UpdateGathering();
        }
        else if (phase.Value == Phase.WaitingForPlayers)
        {
            DealTeams(TagTeam.AutoTeams(presentIds.Count, DebugTagTeamUnevenForSession), presentIds);
        }
        else
        {
            foreach (ulong id in presentIds)
            {
                if (!teamA.Contains(id) && !teamB.Contains(id) && !spectatorQueue.Contains(id)) spectatorQueue.Add(id);
            }
        }

        ServerApplyTagTeamRoles();
        byte waiting = (byte)Mathf.Min(spectatorQueue.Count, 255);
        if (spectatorQueueCount.Value != waiting) spectatorQueueCount.Value = waiting;

        if (!matchRunning) return;
        if (teamA.Count == 0 || teamB.Count == 0) ServerTagTeamForfeit();
        else if (fighterLeft) Debug.Log("[Round] Tag Team : un combattant a quitté la partie, son coéquipier entre.");
    }

    /// <summary>Retire de `team` les joueurs partis ; vrai si son combattant en était.</summary>
    private bool RemoveAbsent(List<ulong> team)
    {
        bool fighter = false;
        for (int i = team.Count - 1; i >= 0; i--)
        {
            if (presentIds.Contains(team[i])) continue;
            if (i == 0) fighter = true;
            team.RemoveAt(i);
        }
        return fighter;
    }

    /// <summary>Range `order` selon `dealt` (TagTeam.AutoTeams, Shuffled) : les équipes dans cet ordre, les autres hors
    /// équipe, dans la file.</summary>
    private void DealTeams(PlayerTeam[] dealt, IReadOnlyList<ulong> order)
    {
        teamA.Clear();
        teamB.Clear();
        spectatorQueue.Clear();
        for (int i = 0; i < order.Count; i++)
        {
            PlayerTeam team = i < dealt.Length ? dealt[i] : PlayerTeam.None;
            (team == PlayerTeam.A ? teamA : team == PlayerTeam.B ? teamB : spectatorQueue).Add(order[i]);
        }
    }

    /// <summary>Une partie lancée d'un salon en ligne : les équipes que l'hôte y a faites (SessionFlow.LaunchTeamOf),
    /// dans l'ordre du salon, refaites à chaque arrivée tant que ses joueurs arrivent ; un joueur que le salon n'a pas
    /// rangé attend hors équipe.</summary>
    private void ArrangeTeamsFromLaunch()
    {
        var ranked = new List<(int rank, int arrival)>();
        for (int i = 0; i < presentIds.Count; i++)
        {
            int rank = SessionFlow.LaunchRankOf(presentIds[i]);
            ranked.Add((rank < 0 ? int.MaxValue : rank, i));
        }
        ranked.Sort((x, y) => x.rank != y.rank ? x.rank.CompareTo(y.rank) : x.arrival.CompareTo(y.arrival));
        teamA.Clear();
        teamB.Clear();
        spectatorQueue.Clear();
        foreach ((int _, int arrival) in ranked)
        {
            ulong id = presentIds[arrival];
            PlayerTeam team = SessionFlow.LaunchTeamOf(id);
            (team == PlayerTeam.A ? teamA : team == PlayerTeam.B ? teamB : spectatorQueue).Add(id);
        }
    }

    private readonly List<PlayerMatchState> enteringFighters = new List<PlayerMatchState>();

    /// <summary>Publie l'équipe, le rôle et la place de chacun au Tag Team : le premier de chaque équipe combat, les
    /// autres attendent sur la tribune (leur place dans le relais : 1, le prochain à entrer). Un coéquipier qui entre en
    /// pleine manche est posé au point le plus éloigné du survivant, et chacun l'apprend (TagInClientRpc).</summary>
    private void ServerApplyTagTeamRoles()
    {
        duelistIds.Clear();
        if (teamA.Count > 0) duelistIds.Add(teamA[0]);
        if (teamB.Count > 0) duelistIds.Add(teamB[0]);
        enteringFighters.Clear();
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state == null || !state.IsSpawned) continue;
            ulong id = state.OwnerClientId;
            int inA = teamA.IndexOf(id), inB = teamB.IndexOf(id);
            PlayerTeam team = inA >= 0 ? PlayerTeam.A : inB >= 0 ? PlayerTeam.B : PlayerTeam.None;
            int place = inA >= 0 ? inA : inB >= 0 ? inB : 0;
            PlayerRole role = team != PlayerTeam.None && place == 0 ? PlayerRole.Duelist : PlayerRole.Spectator;
            PlayerRole before = state.Role;
            state.ServerSetTeam(team);
            state.ServerSetRole(role, place);
            if (before == role) continue;
            ServerOnRoleChanged(state, role);
            if (role == PlayerRole.Duelist && phase.Value == Phase.Active) enteringFighters.Add(state);
        }
        // Posés une fois tous les rôles publiés : le combattant tombé a quitté l'arène.
        foreach (PlayerMatchState state in enteringFighters)
        {
            if (state.TryGetComponent(out PlayerLocomotion locomotion)) locomotion.ServerRespawnAwayFromOpponents();
            TagInClientRpc(state.OwnerClientId);
            Debug.Log($"[Round] Tag Team : {state.DisplayName} entre dans l'arène ({TagTeam.Name(state.Team)}).");
        }
    }

    /// <summary>Applique `action` à ceux qui attendent leur tour au Tag Team (les membres des équipes, sauf les
    /// combattants).</summary>
    private void ServerForEachWaitingMember(System.Action<PlayerMatchState> action)
    {
        for (int i = 1; i < teamA.Count; i++)
        {
            PlayerMatchState state = PlayerMatchState.Of(teamA[i]);
            if (state != null) action(state);
        }
        for (int i = 1; i < teamB.Count; i++)
        {
            PlayerMatchState state = PlayerMatchState.Of(teamB[i]);
            if (state != null) action(state);
        }
    }

    /// <summary>La manche du Tag Team : un combattant mort compte (ServerOnTagTeamKill), puis, `respawnDelay` plus tard,
    /// son équipe passe le relais (ServerRelay).</summary>
    private void TickTagTeamRound()
    {
        CollectDuelists(roundPlayers);
        double now = Now;
        foreach (PlayerLocomotion player in roundPlayers)
        {
            Health health = player.GetComponent<Health>();
            PlayerMatchState state = player.GetComponent<PlayerMatchState>();
            if (health == null || state == null) continue;
            if (!health.IsDead)
            {
                if (state.RespawnAt > 0d) state.ServerSetRespawnAt(0d);
                continue;
            }
            if (state.RespawnAt <= 0d)
            {
                ServerNoteKill(player, health);
                ServerOnTagTeamKill(state, health);
                if (phase.Value != Phase.Active) return;
                state.ServerSetRespawnAt(now + respawnDelay);
            }
            else if (now >= state.RespawnAt)
            {
                ServerRelay(player, state, health);
                return; // la composition a changé : la liste de la manche est périmée
            }
        }
    }

    /// <summary>Serveur : le combattant `victim` vient de tomber. Son vainqueur, s'il est de l'autre équipe, rapporte des
    /// points à la sienne (TagTeam.PointsFor : plus à la tête, plus au gant) ; la première équipe au compte gagne.</summary>
    private void ServerOnTagTeamKill(PlayerMatchState victim, Health health)
    {
        ulong killerId = health.ServerKillerId;
        PlayerMatchState killer = KillScores(killerId, victim.OwnerClientId) ? PlayerMatchState.Of(killerId) : null;
        if (killer == null || killer.Team == PlayerTeam.None || killer.Team == victim.Team) return;
        int points = TagTeam.PointsFor(health.ServerKillCause);
        AddScore(killerId, points);
        TeamScore score = teamScore.Value;
        if (killer.Team == PlayerTeam.A) score.a += points;
        else score.b += points;
        teamScore.Value = score;
        TagKillClientRpc(killerId, victim.OwnerClientId, (byte)points, health.ServerKillCause);
        Debug.Log($"[Round] Tag Team : {killer.DisplayName} élimine {victim.DisplayName}, +{points} ({TagTeam.Name(killer.Team)} : {score.Of(killer.Team)} sur {TagTeam.TargetPoints}).");
        if (score.Of(killer.Team) >= TagTeam.TargetPoints) ServerEndTagTeamMatch(killer.Team, killerId, false);
    }

    /// <summary>Serveur : le relais de l'équipe du combattant tombé. Il monte sur la tribune, soigné, et le coéquipier qui
    /// attend depuis le plus longtemps entre (ServerApplyTagTeamRoles le pose) ; seul dans son équipe (le banc de test),
    /// il revient lui-même, comme une réapparition.</summary>
    private void ServerRelay(PlayerLocomotion player, PlayerMatchState state, Health health)
    {
        List<ulong> team = teamA.Contains(state.OwnerClientId) ? teamA : teamB;
        state.ServerSetRespawnAt(0d);
        if (team.Count <= 1)
        {
            ServerRespawn(player, state, health);
            return;
        }
        TagTeam.Relay(team);
        ServerApplyTagTeamRoles();
    }

    /// <summary>Serveur : `team` remporte le match ; `scorer` a porté le dernier coup (son robot célèbre à l'écran de
    /// fin), ou combat encore pour elle quand l'autre équipe est partie (`forfeit`).</summary>
    private void ServerEndTagTeamMatch(PlayerTeam team, ulong scorer, bool forfeit)
    {
        winningTeam.Value = team;
        lastRoundWinner.Value = scorer;
        wonByForfeit.Value = forfeit;
        PlayerMatchState state = PlayerMatchState.Of(scorer);
        if (state != null && !forfeit) state.ServerRecordRoundWin(PhaseElapsed);
        Debug.Log(forfeit ? $"[Round] Tag Team : l'autre équipe est partie, {TagTeam.Name(team)} gagne par forfait."
                          : $"[Round] Tag Team : {TagTeam.Name(team)} remporte le match.");
        bool replay = !forfeit && ServerReplayWanted(scorer);
        SetPhase(Phase.MatchOver, roundOverDuration + endScreenDuration + (replay ? KillReplay.PhaseExtension(lastKill) : 0f));
        if (replay) ServerPublishReplay();
    }

    /// <summary>Serveur : une équipe n'a plus personne en plein match : l'autre gagne par forfait. Son combattant, mort
    /// au dernier coup, se relève pour l'écran de fin.</summary>
    private void ServerTagTeamForfeit()
    {
        List<ulong> remaining = teamA.Count > 0 ? teamA : teamB.Count > 0 ? teamB : null;
        if (remaining == null)
        {
            EnterWaiting();
            return;
        }
        PlayerMatchState state = PlayerMatchState.Of(remaining[0]);
        if (state != null && state.TryGetComponent(out Health health) && health.IsDead) health.ResetHealth();
        ServerEndTagTeamMatch(remaining == teamA ? PlayerTeam.A : PlayerTeam.B, remaining[0], true);
    }

    /// <summary>Un combattant vient d'en éliminer un autre au Tag Team (décidé par le serveur, chez tous) : le HUD
    /// l'annonce, avec les points rapportés à son équipe.</summary>
    [ClientRpc]
    private void TagKillClientRpc(ulong killerId, ulong victimId, byte points, Health.DamageCause cause)
    {
        tagKills++;
        lastTagKiller = killerId;
        lastTagVictim = victimId;
        lastTagPoints = points;
        lastTagCause = cause;
    }

    /// <summary>Un coéquipier vient d'entrer dans l'arène au Tag Team : le HUD l'annonce, les sons le font entendre.</summary>
    [ClientRpc]
    private void TagInClientRpc(ulong clientId)
    {
        tagIns++;
        lastTagIn = clientId;
    }

    /// <summary>L'hôte range un joueur au salon (SalonPanel) : dans l'équipe A, la B, ou hors équipe. Une équipe pleine
    /// (trois joueurs) le refuse.</summary>
    public void ServerSetTeam(ulong clientId, PlayerTeam team)
    {
        if (!IsServer || phase.Value != Phase.Salon || !TagTeamMode || !presentIds.Contains(clientId)) return;
        List<ulong> target = team == PlayerTeam.A ? teamA : team == PlayerTeam.B ? teamB : null;
        if (target != null && (target.Contains(clientId) || target.Count >= TagTeam.MaxTeamSize)) return;
        teamA.Remove(clientId);
        teamB.Remove(clientId);
        spectatorQueue.Remove(clientId);
        (target ?? spectatorQueue).Add(clientId);
        ServerApplyTagTeamRoles();
    }

    /// <summary>L'hôte tire les équipes au sort au salon (« Équipes au hasard », GDD § 11, TT1).</summary>
    public void ServerShuffleTeams()
    {
        if (!IsServer || phase.Value != Phase.Salon || !TagTeamMode) return;
        var order = new List<ulong>(teamA);
        order.AddRange(teamB);
        order.AddRange(spectatorQueue);
        DealTeams(TagTeam.Shuffled(order.Count, draw), order);
        ServerApplyTagTeamRoles();
    }

    /// <summary>Passer au Tag Team au salon : des équipes avec les joueurs présents, dans l'ordre (les duellistes, puis
    /// la file), comme à l'arrivée (TagTeam.AutoTeams).</summary>
    private void ServerTeamsFromRoster()
    {
        var order = new List<ulong>(duelistIds);
        order.AddRange(spectatorQueue);
        foreach (ulong id in presentIds)
        {
            if (!order.Contains(id)) order.Add(id);
        }
        DealTeams(TagTeam.AutoTeams(order.Count, DebugTagTeamUnevenForSession), order);
        ServerApplyTagTeamRoles();
    }

    /// <summary>Quitter le Tag Team au salon : les deux combattants restent duellistes, les autres attendent dans la
    /// file (l'équipe A, puis la B, puis ceux qui n'en avaient pas) ; plus personne n'a d'équipe.</summary>
    private void ServerRosterFromTeams()
    {
        var order = new List<ulong>();
        if (teamA.Count > 0) order.Add(teamA[0]);
        if (teamB.Count > 0) order.Add(teamB[0]);
        for (int i = 1; i < teamA.Count; i++) order.Add(teamA[i]);
        for (int i = 1; i < teamB.Count; i++) order.Add(teamB[i]);
        order.AddRange(spectatorQueue);
        teamA.Clear();
        teamB.Clear();
        duelistIds.Clear();
        spectatorQueue.Clear();
        foreach (ulong id in order) (duelistIds.Count < DuelistSlotsForSession ? duelistIds : spectatorQueue).Add(id);
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && state.IsSpawned) state.ServerSetTeam(PlayerTeam.None);
        }
        ServerApplyRoles();
    }

    // ------------------------------------------------------------------
    // Le Tournoi : les couloirs, le tirage, les tours, la finale (GDD § 11, 2026-11-03)
    // ------------------------------------------------------------------

    private const int MinTournamentPlayers = Tournament.MinPlayers;

    /// <summary>Serveur, un couloir : son match commence, en classique (BO3 en poule, BO5 en finale), entre ses deux joueurs.
    /// Ils viennent de la salle d'attente : la première préparation les place dans sa copie de l'arène.</summary>
    private void ServerStartLane()
    {
        laneInfo.Value = pendingLane;
        // Les options de la partie valent dans chaque match (le choix de l'arme, Tête seulement).
        MatchRules party = Instance != null ? Instance.rules.Value : default;
        rules.Value = new MatchRules
        {
            capacity = (byte)DuelRoster.DuelistSlots,
            organization = DuelOrganization.FixedDuelists,
            roundsToWin = pendingLane.roundsToWin,
            mode = GameModeId.Classic,
            weaponChoice = party.weaponChoice,
            headshotsOnly = party.headshotsOnly,
        }.Sanitized();
        duelistIds.Clear();
        duelistIds.Add(pendingLane.a);
        duelistIds.Add(pendingLane.b);
        laneReported = false;
        Debug.Log($"[Tournoi] {(pendingLane.final ? "La finale" : "Un match")} commence dans l'arène {pendingLane.arena + 1} : "
                  + $"{PlayerMatchState.NameOf(pendingLane.a)} contre {PlayerMatchState.NameOf(pendingLane.b)}.");
        StartMatch();
    }

    /// <summary>Serveur, un couloir : son match se joue comme celui de la scène, sans spectateurs ni vote ; un joueur parti
    /// donne le match à l'autre (TO7). Le match fini, il rend son résultat au RoundManager de la scène, qui le fait
    /// disparaître.</summary>
    private void ServerTickLane()
    {
        CollectDuelists(roundPlayers);
        if (roundPlayers.Count < DuelRoster.DuelistSlots && phase.Value != Phase.MatchOver)
        {
            ServerLaneForfeit(roundPlayers.Count > 0 ? roundPlayers[0] : null);
            return;
        }
        switch (phase.Value)
        {
            case Phase.Preparation:
                ServerTickCoinFlip();
                if (AllPlayersReady() || PhaseHasEnded) SetPhase(Phase.Starting, startingDuration);
                break;
            case Phase.Starting:
                if (PhaseHasEnded) SetPhase(Phase.Active, roundTimeLimit);
                break;
            case Phase.Active:
                TickActiveRound();
                break;
            case Phase.RoundOver:
                if (PhaseHasEnded) BeginRound();
                break;
            case Phase.MatchOver:
                if (!PhaseHasEnded || laneReported) break;
                laneReported = true;
                RoundManager main = Instance;
                if (main != null && main.IsSpawned) main.ServerOnLaneFinished(this);
                else NetworkObject.Despawn(true);
                break;
        }
    }

    /// <summary>Serveur, un couloir : un joueur est parti en plein match ; l'autre le gagne par forfait (TO7) et garde la
    /// main le temps d'une fin de manche. Partis tous les deux, personne ne le gagne.</summary>
    private void ServerLaneForfeit(PlayerLocomotion survivor)
    {
        ulong winner = NoWinner;
        if (survivor != null)
        {
            Health health = survivor.GetComponent<Health>();
            if (health != null && health.IsDead) health.ResetHealth();
            winner = survivor.OwnerClientId;
        }
        lastRoundWinner.Value = winner;
        wonByForfeit.Value = true;
        Debug.Log(winner != NoWinner ? $"[Tournoi] Un joueur a quitté son match : {PlayerMatchState.NameOf(winner)} le gagne par forfait."
                                     : "[Tournoi] Les deux joueurs d'un match sont partis.");
        SetPhase(Phase.MatchOver, survivor != null ? roundOverDuration : 0.1f);
    }

    /// <summary>
    /// Serveur, au Tournoi : tous les joueurs présents sont armés, dans leur match ou dans la salle d'attente (aucune
    /// tribune) ; un joueur du tournoi qui part déclare forfait (TO7) : ses matchs à venir vont à ses adversaires, celui en
    /// cours se règle dans son couloir. S'il ne reste qu'un joueur, il est champion ; plus personne, retour au salon.
    /// </summary>
    private void ServerUpdateTournamentRoster()
    {
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state == null || !state.IsSpawned) continue;
            PlayerRole before = state.Role;
            state.ServerSetRole(PlayerRole.Duelist, 0);
            if (state.Team != PlayerTeam.None) state.ServerSetTeam(PlayerTeam.None);
            if (before != PlayerRole.Duelist) ServerOnRoleChanged(state, PlayerRole.Duelist);
        }
        if (gathering) UpdateGathering();
        if (phase.Value != Phase.Tournament) return;

        bool changed = false;
        Tournament.State s = tournament.Value;
        for (int i = 0; i < entrants.Count; i++)
        {
            ulong id = entrants[i];
            if (presentIds.Contains(id) || leftEntrants.Contains(id)) continue;
            leftEntrants.Add(id);
            int forfeits = Tournament.ForfeitPending(schedule, id, leftEntrants);
            if (i < 8) s.leftMask |= (byte)(1 << i);
            changed = true;
            Debug.Log($"[Tournoi] {PlayerMatchState.NameOf(id)} quitte le tournoi : forfait, {forfeits} match(s) à venir donné(s) à ses adversaires.");
        }
        if (!changed) return;
        tournament.Value = s;
        ServerPublishSchedule();

        int remaining = entrants.Count - leftEntrants.Count;
        if (remaining >= 2) return;
        ServerDespawnLanes();
        foreach (ulong id in entrants)
        {
            if (leftEntrants.Contains(id)) continue;
            ServerToWaitingRoom(id);
            ServerCrownChampion(id, true);
            return;
        }
        EnterSalon();
    }

    /// <summary>Serveur : le Tournoi commence avec les joueurs présents. Le tirage : l'ordre des places, puis le calendrier de
    /// la poule (TO8), montrés à tous dans la salle d'attente, où chacun est replacé, soigné, son arme pleine.</summary>
    private void ServerStartTournament()
    {
        ServerDespawnLanes();
        entrants.Clear();
        entrants.AddRange(presentIds);
        leftEntrants.Clear();
        // L'ordre des places se tire au sort : il départage les égalités parfaites du classement.
        for (int i = entrants.Count - 1; i > 0; i--)
        {
            int j = draw.Next(i + 1);
            (entrants[i], entrants[j]) = (entrants[j], entrants[i]);
        }
        schedule.Clear();
        schedule.AddRange(Tournament.Schedule(entrants, draw));
        tournamentPlayers.Clear();
        foreach (ulong id in entrants) tournamentPlayers.Add(id);
        // Leur pseudo, pour le tableau de la soirée : un joueur parti en cours de tournoi y garde le sien.
        foreach (ulong id in entrants) knownNames[id] = PlayerMatchState.NameOf(id);
        ServerPublishSchedule();
        lastRoundWinner.Value = NoWinner;
        wonByForfeit.Value = false;
        decision.Value = MatchDecision.Pending;
        ClearVotes();
        SetPhase(Phase.Tournament, 0f);
        SetStage(Tournament.Stage.Draw, 0, drawDuration);
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state == null || !state.IsSpawned) continue;
            state.ServerResetStats();
            ServerToWaitingRoom(state.OwnerClientId);
        }
        Debug.Log($"[Tournoi] Le tournoi commence : {entrants.Count} joueurs, {schedule.Count} matchs de poule en "
                  + $"{Tournament.TourCount(entrants.Count)} tours.");
    }

    /// <summary>Serveur : le tirage, puis chaque tour (ses couloirs en même temps), l'entracte, la finale.</summary>
    private void ServerTickTournament()
    {
        Tournament.State s = tournament.Value;
        switch (s.stage)
        {
            case Tournament.Stage.Draw:
                if (Now >= s.stageEndsAt) ServerBeginTour(0);
                break;
            case Tournament.Stage.Tour:
                if (runningLanes.Count == 0 && Tournament.TourDone(schedule, s.tour)) SetStage(Tournament.Stage.Break, s.tour, breakDuration);
                break;
            case Tournament.Stage.Break:
                if (Now < s.stageEndsAt) break;
                if (s.tour + 1 < s.tourCount) ServerBeginTour(s.tour + 1);
                else ServerBeginFinal();
                break;
        }
    }

    /// <summary>Serveur : les matchs d'un tour commencent ensemble, chacun dans sa copie de l'arène (TO9) ; ceux qui se
    /// reposent restent dans la salle d'attente. Un match déjà réglé par forfait ne se joue pas.</summary>
    private void ServerBeginTour(int tour)
    {
        SetStage(Tournament.Stage.Tour, tour, 0f);
        int index = 0;
        for (int i = 0; i < schedule.Count; i++)
        {
            Tournament.Match m = schedule[i];
            if (m.tour != tour || m.status != Tournament.Status.Pending) continue;
            ServerSpawnLane(i, Tournament.ArenaFor(tour, index++), m.a, m.b, Tournament.PoolRoundsToWin, false);
        }
        Debug.Log($"[Tournoi] Tour {tour + 1} sur {tournament.Value.tourCount} : {index} match(s).");
    }

    /// <summary>Serveur : la poule finie, la finale oppose les deux premiers encore là (TO1), en BO5, dans la première arène ;
    /// s'il n'en reste qu'un, il est champion.</summary>
    private void ServerBeginFinal()
    {
        List<Tournament.Standing> table = Tournament.Standings(entrants, schedule, leftEntrants);
        if (!Tournament.TryGetFinalists(table, out ulong first, out ulong second))
        {
            if (first != Tournament.Nobody) ServerCrownChampion(first, true);
            else EnterSalon();
            return;
        }
        schedule.Add(new Tournament.Match { a = first, b = second, tour = Tournament.FinalTour, winner = Tournament.Nobody });
        ServerPublishSchedule();
        SetStage(Tournament.Stage.Final, tournament.Value.tour, 0f);
        Debug.Log($"[Tournoi] La finale : {PlayerMatchState.NameOf(first)} contre {PlayerMatchState.NameOf(second)}.");
        ServerSpawnLane(schedule.Count - 1, 0, first, second, Tournament.FinalRoundsToWin, true);
    }

    /// <summary>Serveur : un couloir pour un match, qui le joue de lui-même (ServerStartLane), aux durées de la partie.</summary>
    private void ServerSpawnLane(int matchIndex, int arena, ulong a, ulong b, int roundsToWin, bool final)
    {
        if (lanePrefabAsset == null)
        {
            Debug.LogError("[Tournoi] Le RoundManager de la scène n'a pas le préfab de ses couloirs (lanePrefabAsset) : aucun match ne se joue.", this);
            return;
        }
        RoundManager lane = Instantiate(lanePrefabAsset);
        lane.pendingLane = new LaneInfo
        {
            arena = (byte)arena, match = (byte)matchIndex, roundsToWin = (byte)roundsToWin, final = final, a = a, b = b,
        };
        lane.preparationDuration = preparationDuration;
        lane.startingDuration = startingDuration;
        lane.roundOverDuration = roundOverDuration;
        lane.roundTimeLimit = roundTimeLimit;
        lane.respawnDelay = respawnDelay;
        Tournament.Match m = schedule[matchIndex];
        m.status = Tournament.Status.Playing;
        m.arena = (byte)arena;
        schedule[matchIndex] = m;
        ServerPublishSchedule();
        runningLanes.Add(lane);
        lane.NetworkObject.Spawn(true);
    }

    /// <summary>Serveur : un couloir a fini son match. Son résultat va au calendrier, ses joueurs remontent dans la salle
    /// d'attente, et il disparaît ; la finale finie, son vainqueur est champion.</summary>
    public void ServerOnLaneFinished(RoundManager lane)
    {
        if (!IsServer || lane == null) return;
        LaneInfo info = lane.laneInfo.Value;
        ulong winner = lane.lastRoundWinner.Value;
        bool forfeit = lane.wonByForfeit.Value;
        runningLanes.Remove(lane);
        if (info.match < schedule.Count)
        {
            Tournament.Match m = schedule[info.match];
            ulong loser = m.Opponent(winner);
            m = Tournament.Finished(m, winner, winner == NoWinner ? 0 : lane.WinsOf(winner), lane.WinsOf(loser), forfeit, info.roundsToWin);
            if (winner == NoWinner)
            {
                m.roundsA = (byte)lane.WinsOf(m.a);
                m.roundsB = (byte)lane.WinsOf(m.b);
            }
            schedule[info.match] = m;
            ServerPublishSchedule();
            Debug.Log($"[Tournoi] {(info.final ? "Finale" : "Match")} fini : {PlayerMatchState.NameOf(m.a)} {m.roundsA} – {m.roundsB} "
                      + $"{PlayerMatchState.NameOf(m.b)}{(forfeit ? " (forfait)" : "")}.");
        }
        if (lane.IsSpawned) lane.NetworkObject.Despawn(true);
        ServerToWaitingRoom(info.a);
        ServerToWaitingRoom(info.b);
        if (!info.final || phase.Value != Phase.Tournament) return;
        if (winner != NoWinner) ServerCrownChampion(winner, forfeit);
        else EnterSalon();
    }

    /// <summary>Serveur : un joueur remonte dans la salle d'attente, soigné, son arme pleine.</summary>
    private void ServerToWaitingRoom(ulong clientId)
    {
        PlayerMatchState state = PlayerMatchState.Of(clientId);
        if (state == null) return;
        if (state.TryGetComponent(out Health health) && health.IsSpawned) health.ResetHealth();
        state.ServerSetReady(false);
        state.ServerSetRespawnAt(0d);
        WeaponController weapon = state.GetComponentInChildren<WeaponController>();
        if (weapon != null && weapon.IsSpawned) weapon.ServerRefillMagazine();
        if (state.TryGetComponent(out PlayerLocomotion locomotion)) locomotion.ServerMoveToSpawnPoint();
    }

    /// <summary>Serveur : le champion, et son écran de fin, plus long que celui d'un match (TO6) ; puis le salon.</summary>
    private void ServerCrownChampion(ulong champion, bool forfeit)
    {
        ServerDespawnLanes();
        lastRoundWinner.Value = champion;
        wonByForfeit.Value = forfeit;
        decision.Value = MatchDecision.Pending;
        SetStage(Tournament.Stage.Done, tournament.Value.tour, 0f);
        Debug.Log($"[Tournoi] {PlayerMatchState.NameOf(champion)} remporte le tournoi{(forfeit ? " (forfait)" : "")}.");
        SetPhase(Phase.MatchOver, championScreenDuration);
    }

    /// <summary>Serveur : le tournoi s'arrête (le salon) : ses couloirs disparaissent ; son calendrier reste publié.</summary>
    private void ServerEndTournament()
    {
        ServerDespawnLanes();
        if (tournament.Value.stage != Tournament.Stage.None) SetStage(Tournament.Stage.None, 0, 0f);
    }

    private void ServerDespawnLanes()
    {
        for (int i = runningLanes.Count - 1; i >= 0; i--)
        {
            RoundManager lane = runningLanes[i];
            if (lane != null && lane.IsSpawned) lane.NetworkObject.Despawn(true);
        }
        runningLanes.Clear();
    }

    private void SetStage(Tournament.Stage stage, int tour, float duration)
    {
        double now = Now;
        Tournament.State s = tournament.Value;
        s.stage = stage;
        s.tour = (byte)Mathf.Clamp(tour, 0, 255);
        s.tourCount = (byte)Tournament.TourCount(entrants.Count);
        s.stageStartedAt = now;
        s.stageEndsAt = duration > 0f ? now + duration : 0d;
        if (stage == Tournament.Stage.Draw) s.leftMask = 0;
        tournament.Value = s;
    }

    /// <summary>Serveur : le calendrier publié suit la copie de travail (seules les lignes qui changent voyagent).</summary>
    private void ServerPublishSchedule()
    {
        while (tournamentMatches.Count > schedule.Count) tournamentMatches.RemoveAt(tournamentMatches.Count - 1);
        for (int i = 0; i < schedule.Count; i++)
        {
            if (i >= tournamentMatches.Count) tournamentMatches.Add(schedule[i]);
            else if (!tournamentMatches[i].Equals(schedule[i])) tournamentMatches[i] = schedule[i];
        }
    }

    // ------------------------------------------------------------------
    // Le choix de l'arme : l'arsenal commun, pile ou face (GDD § 11, 2026-11-03)
    // ------------------------------------------------------------------

    /// <summary>Serveur : les armes de l'arsenal (celles du préfab, les mêmes pour tous).</summary>
    private IReadOnlyList<WeaponData> ServerLoadout()
    {
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            WeaponController weapon = player != null ? player.GetComponentInChildren<WeaponController>() : null;
            if (weapon != null) return weapon.Loadout;
        }
        return null;
    }

    /// <summary>Serveur, au début d'une manche de l'arsenal commun : une nouvelle arme tirée au sort aux manches 1, 3, 5…,
    /// avec ses accessoires (CommonArsenal) ; sinon celle des deux manches en cours reste.</summary>
    private void ServerDrawCommonArsenal()
    {
        CommonArsenal.State s = commonArsenal.Value;
        if (s.slot != CommonArsenal.NoSlot && !CommonArsenal.DrawsAt(roundNumber.Value)) return;
        IReadOnlyList<WeaponData> loadout = ServerLoadout();
        if (loadout == null || loadout.Count == 0) return;
        int slot = CommonArsenal.DrawSlot(loadout, s.slot == CommonArsenal.NoSlot ? -1 : s.slot, draw);
        if (slot < 0 || slot >= loadout.Count) return;
        s.slot = (byte)slot;
        s.drawnRound = (byte)Mathf.Clamp(roundNumber.Value, 0, 255);
        s.attachments = CommonArsenal.DrawAttachments(loadout[slot], slot, draw);
        commonArsenal.Value = s;
        Debug.Log($"[Round] Arsenal commun : {loadout[slot].weaponName} pour les deux, manches {roundNumber.Value} et {roundNumber.Value + 1}.");
    }

    /// <summary>Serveur : les accessoires de l'arme de l'arsenal commun, les mêmes pour ce joueur que pour l'autre (les
    /// siens sont gardés, et rendus à la fin : ServerRestoreAttachments).</summary>
    private void ServerApplyCommonAttachments(PlayerLocomotion player)
    {
        CommonArsenal.State s = commonArsenal.Value;
        if (s.slot == CommonArsenal.NoSlot || !player.TryGetComponent(out PlayerWeaponAttachments attachments)) return;
        if (!savedAttachments.ContainsKey(player.OwnerClientId)) savedAttachments[player.OwnerClientId] = attachments.Attachments;
        attachments.ServerSet(CommonArsenal.WithSlotFrom(attachments.Attachments, s.attachments, s.slot));
    }

    /// <summary>Serveur : chacun retrouve ses propres accessoires (la fin de l'arsenal commun : un autre réglage, le salon,
    /// l'attente).</summary>
    private void ServerRestoreAttachments()
    {
        if (savedAttachments.Count == 0) return;
        foreach (KeyValuePair<ulong, WeaponAttachments> pair in savedAttachments)
        {
            PlayerMatchState state = PlayerMatchState.Of(pair.Key);
            if (state != null && state.TryGetComponent(out PlayerWeaponAttachments attachments)) attachments.ServerSet(pair.Value);
        }
        savedAttachments.Clear();
    }

    /// <summary>Serveur, au début de la préparation d'une manche à pile ou face : qui choisit la famille (la pièce à la
    /// première manche ou après une manche nulle, sinon le perdant de la manche d'avant), et 10 s pour la choisir. Vrai si
    /// la pièce a été lancée.</summary>
    private bool ServerStartCoinFlip(ulong previousWinner)
    {
        CollectDuelists(roundPlayers);
        if (roundPlayers.Count < DuelRoster.DuelistSlots) return false;
        CoinFlip.State s = coinFlip.Value;
        // La famille de la manche qui vient de finir ne peut pas revenir tout de suite (P3).
        if (s.family != CoinFlip.NoFamily) s.previous = s.family;
        s.chooser = CoinFlip.Chooser(roundPlayers[0].OwnerClientId, roundPlayers[1].OwnerClientId, roundNumber.Value, previousWinner, draw, out bool tossed);
        s.family = CoinFlip.NoFamily;
        s.tossedAt = tossed ? Now : 0d;
        s.familyDeadline = CoinFlip.FamilyDeadline(Now, tossed);
        coinFlip.Value = s;
        Debug.Log($"[Round] Pile ou face : {PlayerMatchState.NameOf(s.chooser)} choisit la famille{(tossed ? " (la pièce)" : " (il a perdu la manche)")}.");
        return tossed;
    }

    /// <summary>Serveur : le joueur désigné choisit la famille de la manche, ou le gant seul (PlayerMatchState). Refusé hors
    /// du choix, d'un autre joueur, ou pour la famille de la manche d'avant.</summary>
    public void ServerChooseFamily(ulong clientId, byte family)
    {
        if (!IsServer || !CoinFlipFamilyPending || coinFlip.Value.chooser != clientId) return;
        if (!CoinFlip.Allowed(family, coinFlip.Value.previous)) return;
        ServerApplyFamily(family);
    }

    /// <summary>Serveur : la famille de la manche, imposée aux deux : chacun garde son arme si elle en est, sinon prend la
    /// première de la famille, et choisit ensuite dedans ; au gant seul, chacun n'a que le gant.</summary>
    private void ServerApplyFamily(byte family)
    {
        CoinFlip.State s = coinFlip.Value;
        s.family = family;
        coinFlip.Value = s;
        CollectDuelists(roundPlayers);
        foreach (PlayerLocomotion player in roundPlayers)
        {
            WeaponController weapon = player.GetComponentInChildren<WeaponController>();
            if (weapon == null) continue;
            if (family == CoinFlip.GloveFamily)
            {
                weapon.ServerForceWeapon(WeaponController.GloveSlot);
                continue;
            }
            if (CoinFlip.SlotAllowed(weapon.DataOf(weapon.PublishedSlot), family)) continue;
            List<int> slots = Arsenal.SlotsOf(weapon.Loadout, (WeaponFamily)family);
            if (slots.Count > 0) weapon.ServerForceWeapon(slots[0]);
        }
        Debug.Log($"[Round] Pile ou face : {CoinFlip.FamilyLabel(family)} pour la manche {roundNumber.Value}.");
    }

    /// <summary>Serveur : sans choix au bout des 10 s, une famille tirée au sort (sans le gant, ni celle d'avant).</summary>
    private void ServerTickCoinFlip()
    {
        if (CoinFlipFamilyPending && Now >= coinFlip.Value.familyDeadline)
            ServerApplyFamily(CoinFlip.RandomFamily(coinFlip.Value.previous, draw));
    }

    /// <summary>Serveur : un nouveau match repart sans arme tirée ni famille jouée.</summary>
    private void ServerResetWeaponChoice()
    {
        commonArsenal.Value = new CommonArsenal.State { slot = CommonArsenal.NoSlot };
        coinFlip.Value = new CoinFlip.State { family = CoinFlip.NoFamily, previous = CoinFlip.NoFamily };
    }

    private void AddWin(ulong clientId) => AddScore(clientId, 1);

    /// <summary>`amount` au score de `clientId` : une manche, une élimination, ou des points à la Zone.</summary>
    private void AddScore(ulong clientId, int amount)
    {
        for (int i = 0; i < scores.Count; i++)
        {
            if (scores[i].clientId != clientId) continue;
            scores[i] = new Score { clientId = clientId, wins = scores[i].wins + amount };
            return;
        }

        // Joueur arrivé après le début du match : on l'inscrit au tableau plutôt que de perdre son
        // point en silence.
        scores.Add(new Score { clientId = clientId, wins = amount });
    }

    private bool TryGetMatchWinner(out ulong winner)
    {
        winner = 0;
        for (int i = 0; i < scores.Count; i++)
        {
            if (scores[i].wins < RoundsToWin) continue;
            winner = scores[i].clientId;
            return true;
        }
        return false;
    }
}
