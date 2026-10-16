using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Boucle de manches d'un duel 1v1, en BO5 (premier à `roundsToWin` manches gagnées).
///
/// ENTIÈREMENT SERVEUR-AUTORITAIRE : la phase, le score et les transitions sont décidés par le
/// serveur et publiés en `NetworkVariable`. Les clients ne font que lire et afficher (GameHud). La
/// seule demande qu'un client puisse faire est « je suis prêt » pendant la préparation
/// (PlayerMatchState), qui ne fait qu'avancer le décompte.
///
/// Une manche (2026-10-12) : PRÉPARATION (choix de l'arme, 20 s au plus, finie dès que les deux
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
    }

    /// <summary>Aucun vainqueur (manche nulle, ou pas encore de manche jouée).</summary>
    public const ulong NoWinner = ulong.MaxValue;

    [Header("Format")]
    [Tooltip("Manches à gagner pour remporter le match. 3 = BO5, conformément au GDD.")]
    [SerializeField] private int roundsToWin = 3;

    [Header("Durées (s)")]
    [Tooltip("Préparation avant chaque manche : chaque joueur choisit son arme dans le panneau du centre de l'écran. Le décompte part dès que les deux ont validé, au plus tard à la fin de ce temps (2026-10-12, demandé par l'utilisateur : 20 s).")]
    [SerializeField] private float preparationDuration = 20f;

    [Tooltip("Décompte avant le début d'une manche. Pendant ce temps le déplacement est bloqué et le tir interdit, pour que les deux joueurs partent exactement en même temps.")]
    [SerializeField] private float startingDuration = 3f;

    [Tooltip("Après une mort : le survivant garde la main (il bouge, tire, se penche) pour narguer son adversaire, puis l'écran passe au noir et les joueurs sont replacés (GDD § 5). Les dernières secondes servent au fondu (GameHud).")]
    [SerializeField] private float roundOverDuration = 5f;

    [Tooltip("Durée de l'écran de fin, après le même temps que roundOverDuration pour le gagnant. Un nouveau match repart ensuite automatiquement.")]
    [SerializeField] private float endScreenDuration = 12f;

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
    public int RoundsToWin => roundsToWin;
    public ulong LastRoundWinner => lastRoundWinner.Value;
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
    /// Gelé pendant la préparation et le décompte, pour que les deux joueurs partent ensemble.
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
        if (duelistLeft && phase.Value != Phase.WaitingForPlayers)
        {
            // Un duelliste est parti : le match s'arrête. Le premier de la file prend sa place, et un
            // nouveau match commence. (Le forfait du GDD, dette n° 58, attend encore son écran.)
            Debug.Log("[Round] Un duelliste a quitté la partie : le match s'arrête.");
            EnterWaiting();
        }

        switch (phase.Value)
        {
            case Phase.WaitingForPlayers:
                if (HasEnoughPlayers()) StartMatch();
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
                if (PhaseHasEnded)
                {
                    // Le roi de la colline : le gagnant reste, le perdant passe sur la tribune, le
                    // premier de la file descend. Le noir est tombé : personne ne voit l'échange.
                    if (DuelRoster.RotateAfterMatch(rules.Value.organization, duelistIds, spectatorQueue, lastRoundWinner.Value))
                        ServerApplyRoles();
                    StartMatch();
                }
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
        DuelRoster.Sync(presentIds, duelistIds, spectatorQueue, slots, out bool duelistLeft);
        // Une place libre (un duelliste parti, ou la partie qui commence) va au premier de la file.
        if (phase.Value == Phase.WaitingForPlayers || duelistLeft) DuelRoster.FillFromQueue(duelistIds, spectatorQueue, slots);
        ServerApplyRoles();
        return duelistLeft;
    }

#if UNITY_EDITOR
    /// <summary>BANC DE TEST — Editor uniquement, sur l'hôte : une seule place de duelliste, pour qu'à
    /// deux instances le second joueur soit spectateur. Le duelliste reste seul, en attente (il
    /// s'échauffe), et le spectateur peut tout faire, lancer compris. À éteindre après.</summary>
    public static bool DebugSoloDuel;
    private static int DuelistSlotsForSession => DebugSoloDuel ? 1 : DuelRoster.DuelistSlots;

    // Remis à zéro à chaque entrée en Play Mode (le rechargement de domaine est désactivé) : un banc
    // oublié ne doit pas faire du second joueur de l'utilisateur un spectateur. À régler une fois en
    // Play Mode, avant d'héberger.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDebug() => DebugSoloDuel = false;
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
        SetPhase(Phase.WaitingForPlayers, 0f);
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
            if (scores[i].wins < roundsToWin) continue;
            winner = scores[i].clientId;
            return true;
        }
        return false;
    }
}
