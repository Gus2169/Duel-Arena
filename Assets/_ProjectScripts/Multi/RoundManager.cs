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
    private static readonly System.Random draw = new System.Random();

    // ------------------------------------------------------------------
    // Accès global
    // ------------------------------------------------------------------

    private static RoundManager cached;

    public static RoundManager Instance
    {
        get
        {
            if (cached == null) cached = FindAnyObjectByType<RoundManager>();
            return cached;
        }
    }

    public Phase CurrentPhase => phase.Value;
    public int RoundNumber => roundNumber.Value;
    /// <summary>Manches à gagner pour remporter le match : 3 en BO5, 2 en BO3 (MatchRules).</summary>
    public int RoundsToWin => rules.Value.Sanitized().roundsToWin;
    public ulong LastRoundWinner => lastRoundWinner.Value;
    /// <summary>Le match vient-il d'être gagné par forfait (en MatchOver) ?</summary>
    public bool WonByForfeit => wonByForfeit.Value;
    /// <summary>Ce que le vote de fin de match a décidé (en MatchOver).</summary>
    public MatchDecision Decision => decision.Value;

    /// <summary>Le vote est-il ouvert ? Pendant l'écran de fin d'un match gagné (pas d'un forfait), tant
    /// qu'il n'est pas tranché.</summary>
    public bool VoteOpen => phase.Value == Phase.MatchOver && !wonByForfeit.Value && decision.Value == MatchDecision.Pending
                            && PhaseElapsed >= EndScreenDelay;

    /// <summary>Le match suivant oppose-t-il les mêmes duellistes ? Non au roi de la colline quand quelqu'un
    /// attend : le perdant cède sa place (l'écran de fin dit alors « Match suivant » plutôt que « Revanche »).</summary>
    public bool NextMatchRotates => rules.Value.organization == DuelOrganization.KingOfTheHill && spectatorQueueCount.Value > 0;

    // Le nombre de spectateurs en file, publié pour l'écran de fin (la file elle-même reste au serveur).
    private readonly NetworkVariable<byte> spectatorQueueCount = new NetworkVariable<byte>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public MatchRules Rules => rules.Value;

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

    /// <summary>Le temps pendant lequel le gagnant garde la main après la dernière mort du match,
    /// avant l'écran de fin (le même qu'entre deux manches).</summary>
    public float EndScreenDelay => roundOverDuration;

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
            || current == Phase.RoundOver || current == Phase.MatchOver;
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

    /// <summary>PlayAllowed pour la phase en cours. Sans RoundManager (tests, scène sans boucle de
    /// manches), seul le fait d'être en vie compte.</summary>
    public static bool MovementAllowedFor(bool alive)
    {
        RoundManager rm = Instance;
        return rm == null ? alive : PlayAllowed(rm.phase.Value, alive);
    }

    /// <summary>Même règle que le déplacement : le tir et le taser sont permis exactement quand
    /// on peut bouger. Vérifié côté client pour le confort, et surtout côté SERVEUR dans
    /// `WeaponController.ServerFire` et `TaserController` : c'est là que ça compte.</summary>
    public static bool FiringAllowedFor(bool alive) => MovementAllowedFor(alive);

    /// <summary>WeaponSwitchAllowed pour la phase en cours (libre sans RoundManager).</summary>
    public static bool WeaponSwitchAllowedFor(bool ready)
    {
        RoundManager rm = Instance;
        return rm == null || WeaponSwitchAllowed(rm.phase.Value, ready);
    }

    /// <summary>Les statistiques du match (PlayerMatchState) ne comptent que pendant la manche : pas
    /// les tirs d'échauffement, ni ceux sur l'épave de l'adversaire.</summary>
    public static bool StatsRecording
    {
        get
        {
            RoundManager rm = Instance;
            return rm != null && rm.phase.Value == Phase.Active;
        }
    }

    // ------------------------------------------------------------------
    // Boucle serveur
    // ------------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        cached = this;
        if (!IsServer) return;
        rules.Value = SessionFlow.ActiveRules.Sanitized();
        // Lancée d'un salon en ligne : tous ses joueurs se connectent à la fois, dans un ordre que rien ne
        // prédit. On les attend un peu pour donner les places de duelliste dans l'ordre du salon (2026-10-27).
        gathering = SessionFlow.LaunchOrder.Count > 0;
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
        if (!IsServer) return;

        // Qui est là, qui se bat, qui attend : par sondage, comme la mort (voir la doc de classe).
        bool duelistLeft = ServerUpdateRoster();
        if (duelistLeft) ServerOnDuelistLeft();

        switch (phase.Value)
        {
            case Phase.WaitingForPlayers:
                if (!gathering && HasEnoughPlayers()) StartMatch();
                break;

            case Phase.Preparation:
                if (!HasEnoughPlayers()) { EnterWaiting(); break; }
                if (AllPlayersReady() || PhaseHasEnded) SetPhase(Phase.Starting, startingDuration);
                break;

            case Phase.Starting:
                if (!HasEnoughPlayers()) { EnterWaiting(); break; }
                if (PhaseHasEnded) SetPhase(Phase.Active, roundTimeLimit);
                break;

            case Phase.Active:
                if (!HasEnoughPlayers()) { EnterWaiting(); break; }
                TickActiveRound();
                break;

            case Phase.RoundOver:
                if (PhaseHasEnded) BeginRound();
                break;

            case Phase.MatchOver:
                if (VoteOpen) ServerTallyVotes();
                if (PhaseHasEnded)
                {
                    if (wonByForfeit.Value)
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
        }
    }

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
    private static int DuelistSlotsForSession => DebugNoDuel ? 0 : DebugSoloDuel ? 1 : DuelRoster.DuelistSlots;

    // Remis à zéro à chaque entrée en Play Mode (le rechargement de domaine est désactivé) : un banc
    // oublié ne doit pas faire du second joueur de l'utilisateur un spectateur. À régler une fois en
    // Play Mode, avant d'héberger.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDebug()
    {
        DebugSoloDuel = false;
        DebugNoDuel = false;
    }
#else
    private static int DuelistSlotsForSession => DuelRoster.DuelistSlots;
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
        if (TryGetMatchWinner(out ulong matchWinner))
        {
            lastRoundWinner.Value = matchWinner;
            Debug.Log($"[Round] Match remporté par {PlayerMatchState.NameOf(matchWinner)}.");
            SetPhase(Phase.MatchOver, roundOverDuration + endScreenDuration);
            return;
        }

        if (winner != NoWinner) Debug.Log($"[Round] {PlayerMatchState.NameOf(winner)} gagne la manche {roundNumber.Value}.");
        SetPhase(Phase.RoundOver, roundOverDuration);
    }

    private void StartMatch()
    {
        wonByForfeit.Value = false;
        decision.Value = MatchDecision.Pending;
        ClearVotes();
        scores.Clear();
        CollectDuelists(roundPlayers);
        foreach (PlayerLocomotion player in roundPlayers)
        {
            scores.Add(new Score { clientId = player.OwnerClientId, wins = 0 });
            PlayerMatchState state = player.GetComponent<PlayerMatchState>();
            if (state != null) state.ServerResetStats();
        }

        roundNumber.Value = 0;
        BeginRound();
    }

    /// <summary>Remet les duellistes en jeu et ouvre la préparation de la manche suivante. Les
    /// spectateurs restent où ils sont, sur la tribune ; les objets restés dans l'arène disparaissent.</summary>
    private void BeginRound()
    {
        if (!HasEnoughPlayers())
        {
            EnterWaiting();
            return;
        }

        roundNumber.Value++;
        lastRoundWinner.Value = NoWinner;
        Tribune tribune = Tribune.Instance;
        if (tribune != null) tribune.ServerClearArena();

        CollectDuelists(roundPlayers);
        foreach (PlayerLocomotion player in roundPlayers)
        {

            // L'ordre compte : on soigne AVANT de replacer. ServerMoveToSpawnPoint choisit le point
            // le plus éloigné des autres joueurs, donc replacer le premier joueur influence le choix
            // du second — c'est voulu, ça garantit qu'ils repartent à deux bouts opposés même s'ils
            // sont morts au même endroit.
            Health health = player.GetComponent<Health>();
            if (health != null) health.ResetHealth();

            // Chargeur plein et rechargement annulé : on ne reprend pas une manche avec le
            // chargeur de la précédente.
            WeaponController weapon = player.GetComponentInChildren<WeaponController>();
            if (weapon != null) weapon.ServerRefillMagazine();

            PlayerMatchState state = player.GetComponent<PlayerMatchState>();
            if (state != null) state.ServerSetReady(false);

            player.ServerMoveToSpawnPoint();
        }

        SetPhase(Phase.Preparation, preparationDuration);
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
            if (state != null) state.ServerSetReady(false);
        }
        lastRoundWinner.Value = NoWinner;
        wonByForfeit.Value = false;
        decision.Value = MatchDecision.Pending;
        ClearVotes();
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

    /// <summary>Serveur : compte les voix des duellistes ; une fois le vote tranché, l'écran de fin ne dure
    /// plus que `decisionTail` (le noir tombe, puis la revanche ou le salon).</summary>
    private void ServerTallyVotes()
    {
        roundVotes.Clear();
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && state.IsSpawned && state.IsDuelist) roundVotes.Add(state.Vote);
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

    /// <summary>La rotation du roi de la colline, puis les places libres aux premiers de la file.</summary>
    private void RotateAndFill()
    {
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
            if (state != null) state.ServerSetReady(false);
        }
        ClearVotes();
        lastRoundWinner.Value = NoWinner;
        wonByForfeit.Value = false;
        decision.Value = MatchDecision.Pending;
        Debug.Log("[Round] Retour au salon de la partie.");
        SetPhase(Phase.Salon, 0f);
    }

    /// <summary>L'hôte peut-il relancer depuis le salon ? Il faut deux duellistes.</summary>
    public bool CanLaunchFromSalon => phase.Value == Phase.Salon && DuelistCount >= DuelRoster.DuelistSlots;

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
        rules.Value = r;
        SessionFlow.ActiveRules = r;
        GameSettings.HostRules = r;
        if (OnlineSession.IsHost) OnlineSession.SetRules(r);
    }

    /// <summary>Change de phase. Une durée nulle ou négative signifie : pas de fin programmée.</summary>
    private void SetPhase(Phase next, float duration)
    {
        double now = Now;
        phase.Value = next;
        phaseStartedAt.Value = now;
        phaseEndsAt.Value = duration > 0f ? now + duration : 0d;
    }

    /// <summary>Deux duellistes présents : un duel peut se jouer.</summary>
    private bool HasEnoughPlayers()
    {
        CollectDuelists(roundPlayers);
        return roundPlayers.Count >= DuelRoster.DuelistSlots;
    }

    /// <summary>Les deux duellistes ont validé leur arme (les spectateurs n'en choisissent pas).</summary>
    private bool AllPlayersReady()
    {
        CollectDuelists(roundPlayers);
        foreach (PlayerLocomotion player in roundPlayers)
        {
            PlayerMatchState state = player.GetComponent<PlayerMatchState>();
            if (state == null || !state.Ready) return false;
        }
        return roundPlayers.Count > 0;
    }

    private void AddWin(ulong clientId)
    {
        for (int i = 0; i < scores.Count; i++)
        {
            if (scores[i].clientId != clientId) continue;
            scores[i] = new Score { clientId = clientId, wins = scores[i].wins + 1 };
            return;
        }

        // Joueur arrivé après le début du match : on l'inscrit au tableau plutôt que de perdre son
        // point en silence.
        scores.Add(new Score { clientId = clientId, wins = 1 });
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
