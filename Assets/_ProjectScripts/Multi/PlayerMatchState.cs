using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// La fiche d'un joueur dans la partie (2026-10-12) : son pseudo, sa préparation avant la manche et
/// ses statistiques du match. Posée sur la racine du prefab joueur.
///
/// Tout est écrit par le SERVEUR, comme le score :
/// - le pseudo vient de la demande de connexion (SessionFlow), nettoyé et rendu unique ici. Le
///   client ne fait que le proposer ;
/// - « prêt » ne se demande que pendant la préparation (ReadyServerRpc) ; le serveur le remet à faux
///   à chaque manche (RoundManager.BeginRound) ;
/// - les statistiques sont comptées par le serveur pendant les manches (WeaponController,
///   TaserController, RoundManager), jamais déclarées par le client.
///
/// Catégorie C réseautée : la simulation n'en lit rien. Le pseudo et les statistiques ne sont
/// qu'affichés (HUD, écran de fin) ; « prêt », lui, décide du départ du décompte et ferme le choix
/// de l'arme (RoundManager.WeaponSwitchAllowed), côté serveur.
/// </summary>
[DisallowMultipleComponent]
public class PlayerMatchState : NetworkBehaviour
{
    /// <summary>Longueur maximale d'un pseudo, en caractères.</summary>
    public const int MaxNameLength = 16;

    /// <summary>Les statistiques d'un joueur sur le match (GDD § 14) : précision, tirs à la tête,
    /// dégâts infligés, manche gagnée la plus rapide.</summary>
    public struct MatchStats : INetworkSerializable, System.IEquatable<MatchStats>
    {
        /// <summary>Tirs acceptés par le serveur pendant une manche (un tir de pompe compte pour un).</summary>
        public int shotsFired;
        /// <summary>Tirs qui ont touché un adversaire (au moins un plomb).</summary>
        public int shotsHit;
        /// <summary>Tirs qui ont touché la tête.</summary>
        public int headshots;
        /// <summary>Vie réellement retirée aux adversaires (balles et taser).</summary>
        public float damageDealt;
        /// <summary>Durée de la manche gagnée la plus courte (s), 0 si aucune.</summary>
        public float fastestRoundWin;

        public float Accuracy => shotsFired > 0 ? (float)shotsHit / shotsFired : 0f;

        public MatchStats WithShot(bool hit, bool head, float damage)
        {
            MatchStats s = this;
            s.shotsFired++;
            if (hit) s.shotsHit++;
            if (head) s.headshots++;
            s.damageDealt += Mathf.Max(0f, damage);
            return s;
        }

        public MatchStats WithDamage(float damage)
        {
            MatchStats s = this;
            s.damageDealt += Mathf.Max(0f, damage);
            return s;
        }

        public MatchStats WithRoundWin(float duration)
        {
            MatchStats s = this;
            if (duration > 0f && (s.fastestRoundWin <= 0f || duration < s.fastestRoundWin)) s.fastestRoundWin = duration;
            return s;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref shotsFired);
            serializer.SerializeValue(ref shotsHit);
            serializer.SerializeValue(ref headshots);
            serializer.SerializeValue(ref damageDealt);
            serializer.SerializeValue(ref fastestRoundWin);
        }

        public bool Equals(MatchStats other) =>
            shotsFired == other.shotsFired && shotsHit == other.shotsHit && headshots == other.headshots
            && damageDealt == other.damageDealt && fastestRoundWin == other.fastestRoundWin;
    }

    private readonly NetworkVariable<FixedString64Bytes> displayName = new NetworkVariable<FixedString64Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> ready = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<MatchStats> stats = new NetworkVariable<MatchStats>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Le rôle (2026-10-13) : duelliste, ou spectateur de la tribune qui attend son tour (GDD § 4). Décidé
    // par le serveur seul (RoundManager, DuelRoster). Duelliste par défaut : hors d'une partie (tests,
    // scène sans boucle de manches), tout le monde se bat, comme avant la tribune.
    private readonly NetworkVariable<PlayerRole> role = new NetworkVariable<PlayerRole>(
        PlayerRole.Duelist, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Sa place dans la file des spectateurs : 1 = le prochain à entrer, 0 pour un duelliste.
    private readonly NetworkVariable<byte> queuePosition = new NetworkVariable<byte>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Le choix d'un duelliste pendant l'écran de fin (2026-10-26) : la revanche, ou le salon.
    /// Les valeurs voyagent par variable réseau : ajouter les nouvelles à la fin.</summary>
    public enum MatchVote : byte
    {
        None,
        Rematch,
        Salon,
    }

    // Son vote de fin de match, accepté par le serveur (RoundManager le compte).
    private readonly NetworkVariable<MatchVote> vote = new NetworkVariable<MatchVote>(
        MatchVote.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private static readonly List<PlayerMatchState> spawned = new List<PlayerMatchState>();

    /// <summary>Les fiches des joueurs présents.</summary>
    public static IReadOnlyList<PlayerMatchState> Spawned => spawned;

    // Le rechargement de domaine est désactivé à l'entrée en Play Mode : sans cette remise à zéro, une
    // fiche d'une session précédente pourrait encore « porter » son pseudo.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => spawned.Clear();

    // Chez le propriétaire : il a validé, la confirmation du serveur est en route.
    private bool readyRequested;

    /// <summary>Le pseudo publié par le serveur (« Joueur N » tant qu'il n'est pas arrivé). Lu à chaque
    /// image par le HUD : la chaîne est gardée tant que le pseudo publié ne change pas (la convertir
    /// allouait à chaque lecture).</summary>
    public string DisplayName
    {
        get
        {
            FixedString64Bytes published = displayName.Value;
            if (shownName == null || !published.Equals(shownNameSource) || shownNameOwner != OwnerClientId)
            {
                string name = published.ToString();
                shownName = string.IsNullOrEmpty(name) ? FallbackName(OwnerClientId) : name;
                shownNameSource = published;
                shownNameOwner = OwnerClientId;
            }
            return shownName;
        }
    }

    private string shownName;
    private FixedString64Bytes shownNameSource;
    private ulong shownNameOwner;

    /// <summary>Le serveur sait ce joueur prêt : son arme est choisie pour la manche.</summary>
    public bool Ready => ready.Value;

    /// <summary>Prêt, ou en train de le devenir (le propriétaire vient de valider) : de quoi
    /// fermer le choix de l'arme chez lui sans attendre la réponse du serveur.</summary>
    public bool ReadyOrRequested => ready.Value || readyRequested;

    public MatchStats Stats => stats.Value;

    /// <summary>Le rôle publié par le serveur : duelliste ou spectateur.</summary>
    public PlayerRole Role => role.Value;
    public bool IsSpectator => role.Value == PlayerRole.Spectator;
    public bool IsDuelist => role.Value == PlayerRole.Duelist;

    /// <summary>Place dans la file des spectateurs (1 = le prochain duel), 0 pour un duelliste.</summary>
    public int QueuePosition => queuePosition.Value;

    /// <summary>Son vote de fin de match, tel que le serveur l'a accepté.</summary>
    public MatchVote Vote => vote.Value;

    /// <summary>Le propriétaire vote, pendant l'écran de fin (MatchEndScreen). Il peut changer d'avis tant
    /// que le vote n'est pas tranché.</summary>
    public void RequestVote(MatchVote choice)
    {
        if (!IsOwner || !IsSpawned || choice == MatchVote.None || choice == vote.Value) return;
        VoteServerRpc(choice);
    }

    /// <summary>Un vote ne compte que d'un duelliste, pendant le vote (RoundManager.VoteOpen).</summary>
    [ServerRpc]
    private void VoteServerRpc(MatchVote choice)
    {
        RoundManager rm = RoundManager.Instance;
        if (rm == null || !rm.VoteOpen || !IsDuelist) return;
        if (choice != MatchVote.Rematch && choice != MatchVote.Salon) return;
        vote.Value = choice;
    }

    /// <summary>Serveur : efface le vote (nouveau match, salon, attente).</summary>
    public void ServerClearVote()
    {
        if (IsServer && vote.Value != MatchVote.None) vote.Value = MatchVote.None;
    }

    /// <summary>Serveur : le rôle de ce joueur et sa place dans la file (RoundManager).</summary>
    public void ServerSetRole(PlayerRole value, int position)
    {
        if (!IsServer) return;
        if (role.Value != value) role.Value = value;
        byte place = (byte)Mathf.Clamp(position, 0, 255);
        if (queuePosition.Value != place) queuePosition.Value = place;
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Surface de test : impose le rôle sans session (les tests de Move() n'en ont pas).</summary>
    public void TestSetRole(PlayerRole value) => role.Value = value;
#endif

    public override void OnNetworkSpawn()
    {
        if (!spawned.Contains(this)) spawned.Add(this);
        if (!IsServer) return;

        var taken = new List<string>();
        foreach (PlayerMatchState other in spawned)
        {
            if (other != null && other != this) taken.Add(other.displayName.Value.ToString());
        }
        string wanted = SanitizeName(SessionFlow.TakeApprovedName(OwnerClientId));
        displayName.Value = new FixedString64Bytes(UniqueName(wanted, taken, OwnerClientId));

        // Le rôle dès l'apparition, dans le message même qui fait apparaître le robot : un spectateur
        // ne passe jamais par l'arène. (Le joueur de l'hôte naît avant l'arène : RoundManager le
        // range à son tour, à son apparition.)
        RoundManager rm = RoundManager.Instance;
        if (rm != null && rm.IsSpawned) rm.ServerRegisterPlayers();
    }

    public override void OnNetworkDespawn()
    {
        spawned.Remove(this);
    }

#if UNITY_EDITOR
    /// <summary>BANC DE TEST — Editor uniquement : le joueur de cette instance valide son arme seul,
    /// une seconde et demie après le début de la préparation (le clone de Multiplayer Play Mode,
    /// que personne ne pilote).</summary>
    public static bool AutopilotReady;

    /// <summary>BANC DE TEST — Editor uniquement : le vote de fin de match de cette instance, deux secondes
    /// après l'ouverture de l'écran de fin (None : elle ne vote pas). Remis à zéro à chaque lancement.</summary>
    public static MatchVote AutopilotVote;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetAutopilotVote() => AutopilotVote = MatchVote.None;
#endif

    private void Update()
    {
        // Une validation ne vaut que pour la préparation en cours.
        if (readyRequested && !IsPreparing()) readyRequested = false;
#if UNITY_EDITOR
        if (AutopilotReady && IsOwner && IsPreparing() && RoundManager.Instance.PhaseElapsed > 1.5f) RequestReady();
        RoundManager round = RoundManager.Instance;
        if (AutopilotVote != MatchVote.None && IsOwner && round != null && round.VoteOpen
            && round.PhaseElapsed > round.EndScreenDelay + 2f) RequestVote(AutopilotVote);
#endif
    }

    private static bool IsPreparing()
    {
        RoundManager rm = RoundManager.Instance;
        return rm != null && rm.CurrentPhase == RoundManager.Phase.Preparation;
    }

    /// <summary>Le propriétaire valide son arme pour la manche (panneau de préparation).</summary>
    public void RequestReady()
    {
        if (!IsOwner || !IsSpawned || ready.Value || readyRequested || !IsPreparing()) return;
        readyRequested = true;
        ReadyServerRpc();
    }

    /// <summary>Le joueur est prêt. Sans effet hors de la préparation, et sans autre effet que le
    /// premier : la répéter ne coûte rien au serveur.</summary>
    [ServerRpc]
    private void ReadyServerRpc()
    {
        if (ready.Value || !IsPreparing()) return;
        ready.Value = true;
    }

    public void ServerSetReady(bool value)
    {
        if (IsServer && ready.Value != value) ready.Value = value;
    }

    public void ServerResetStats()
    {
        if (IsServer) stats.Value = default;
    }

    public void ServerRecordShot(bool hit, bool head, float damage)
    {
        if (IsServer) stats.Value = stats.Value.WithShot(hit, head, damage);
    }

    public void ServerRecordDamage(float damage)
    {
        if (IsServer && damage > 0f) stats.Value = stats.Value.WithDamage(damage);
    }

    public void ServerRecordRoundWin(float duration)
    {
        if (IsServer) stats.Value = stats.Value.WithRoundWin(duration);
    }

    /// <summary>La fiche du joueur `clientId`, ou null.</summary>
    public static PlayerMatchState Of(ulong clientId)
    {
        foreach (PlayerMatchState state in spawned)
        {
            if (state != null && state.OwnerClientId == clientId) return state;
        }
        return null;
    }

    /// <summary>Le pseudo à afficher pour `clientId` : le sien, ou « Joueur N » s'il est parti.</summary>
    public static string NameOf(ulong clientId)
    {
        if (clientId == RoundManager.NoWinner) return "";
        PlayerMatchState state = Of(clientId);
        return state != null ? state.DisplayName : FallbackName(clientId);
    }

    public static string FallbackName(ulong clientId) => $"Joueur {clientId + 1}";

    /// <summary>
    /// Un pseudo affichable : espaces de bord retirés, espaces multiples réduits, caractères de
    /// contrôle et emoji (hors de la police, et sur deux unités) supprimés, 16 caractères au plus.
    /// Vide si rien ne reste. Fonction pure, testée.
    /// </summary>
    public static string SanitizeName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var result = new StringBuilder(MaxNameLength);
        bool lastWasSpace = true; // pas d'espace en tête
        foreach (char c in raw)
        {
            if (char.IsControl(c) || char.IsSurrogate(c)) continue;
            bool space = char.IsWhiteSpace(c);
            if (space && lastWasSpace) continue;
            result.Append(space ? ' ' : c);
            lastWasSpace = space;
            if (result.Length >= MaxNameLength) break;
        }
        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// Le pseudo final d'un joueur : « Joueur N » s'il n'en a pas, suivi d'un numéro s'il est déjà
    /// porté (sans tenir compte des majuscules), toujours en 16 caractères au plus. Fonction pure,
    /// testée.
    /// </summary>
    public static string UniqueName(string name, IReadOnlyCollection<string> taken, ulong clientId)
    {
        if (string.IsNullOrEmpty(name)) name = FallbackName(clientId);
        if (!IsTaken(name, taken)) return name;
        for (int i = 2; ; i++)
        {
            string suffix = " " + i;
            string stem = name.Length + suffix.Length > MaxNameLength ? name.Substring(0, MaxNameLength - suffix.Length).TrimEnd() : name;
            string candidate = stem + suffix;
            if (!IsTaken(candidate, taken)) return candidate;
        }
    }

    private static bool IsTaken(string name, IReadOnlyCollection<string> taken)
    {
        if (taken == null) return false;
        foreach (string other in taken)
        {
            if (string.Equals(other, name, System.StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
