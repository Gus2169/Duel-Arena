using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Boucle de manches d'un duel 1v1, en BO5 (premier à `roundsToWin` manches gagnées).
///
/// ENTIÈREMENT SERVEUR-AUTORITAIRE : la phase, le score et les transitions sont décidés par le
/// serveur et publiés en `NetworkVariable`. Aucun client n'a de RPC pour influencer le déroulement
/// — même principe que pour les dégâts. Les clients ne font que lire et afficher.
///
/// La détection de mort se fait par SONDAGE de `Health.IsDead` plutôt que par abonnement à
/// `Health.OnDeath`. À deux joueurs, le coût est nul, et ça évite toute la classe de bugs de cycle
/// de vie des abonnements (joueur qui se déconnecte en pleine manche, objet détruit avant le
/// désabonnement, double abonnement au respawn).
///
/// Ce composant ne connaît rien du HUD : il expose un état, et affiche un OnGUI minimal de test
/// (même statut de placeholder que `NetworkBootstrapUI`, à retirer avec le vrai HUD UI Toolkit).
/// </summary>
public class RoundManager : NetworkBehaviour
{
    public enum Phase : byte
    {
        WaitingForPlayers,
        Starting,   // décompte, déplacement bloqué, tir interdit
        Active,     // la manche est en cours
        RoundOver,  // quelqu'un est mort, court temps d'arrêt
        MatchOver,  // un joueur a atteint roundsToWin
    }

    [Header("Format")]
    [Tooltip("Manches à gagner pour remporter le match. 3 = BO5, conformément au GDD.")]
    [SerializeField] private int roundsToWin = 3;

    [Header("Durées (s)")]
    [Tooltip("Décompte avant le début d'une manche. Pendant ce temps le déplacement est bloqué et le tir interdit, pour que les deux joueurs partent exactement en même temps.")]
    [SerializeField] private float startingDuration = 3f;

    [Tooltip("Temps d'arrêt après une mort, avant la manche suivante. Laisse voir ce qui vient de se passer.")]
    [SerializeField] private float roundOverDuration = 2.5f;

    [Tooltip("Temps d'arrêt après la fin du match, avant qu'un nouveau match reparte automatiquement (pratique pour tester en continu).")]
    [SerializeField] private float matchOverDuration = 5f;

    [Tooltip("Durée max d'une manche, en secondes. 0 = AUCUNE limite, conformément au GDD (§ 5, tranché le 2026-10-02) : une manche se termine par une mort. Ne sert qu'à un test ponctuel ; si des manches passives apparaissent en playtest, la piste retenue est la révélation sonore, pas le retour d'un chrono.")]
    [SerializeField] private float roundTimeLimit = 0f;

    // ------------------------------------------------------------------
    // État réseauté
    // ------------------------------------------------------------------

    private readonly NetworkVariable<Phase> phase = new NetworkVariable<Phase>(
        Phase.WaitingForPlayers, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Début et fin de la phase courante, en temps SERVEUR (NetworkManager.ServerTime).
    /// Publiés une fois par changement de phase, au lieu d'un temps restant réécrit à chaque frame
    /// et donc réémis à chaque tick. Chaque client en déduit le décompte et le temps écoulé — le
    /// « chrono » du futur HUD (GDD § 15). Une fin à 0 signifie : pas de fin.
    ///
    /// Le temps serveur de Netcode n'est qu'une estimation chez le client : assez pour un
    /// affichage, jamais pour une décision, qui reste au serveur.</summary>
    private readonly NetworkVariable<double> phaseStartedAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<double> phaseEndsAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> roundNumber = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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

    private double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

    /// <summary>Secondes restantes dans la phase courante, 0 si elle n'a pas de fin.</summary>
    public float PhaseTimeRemaining =>
        phaseEndsAt.Value <= 0d ? 0f : (float)System.Math.Max(0d, phaseEndsAt.Value - Now);

    /// <summary>Secondes écoulées depuis le début de la phase courante. En phase Active, c'est le
    /// temps de la manche, que le HUD affichera (GDD § 15).</summary>
    public float PhaseElapsed => (float)System.Math.Max(0d, Now - phaseStartedAt.Value);

    private bool PhaseHasEnded => phaseEndsAt.Value > 0d && Now >= phaseEndsAt.Value;

    /// <summary>Le déplacement est libre pendant une manche, et pendant l'attente d'un adversaire
    /// (sinon le premier joueur connecté restait figé, ce qui empêchait aussi de tester seul).
    /// Bloqué pendant le décompte pour que les deux joueurs démarrent ensemble, et après une mort
    /// pour figer la scène.
    ///
    /// Lu depuis `PlayerLocomotion.Move()`, donc depuis une fonction rejouée par la réconciliation :
    /// la valeur utilisée lors d'un rejeu est celle de MAINTENANT, pas celle de l'input rejoué.
    /// C'est le même compromis que pour la posture, et il est acceptable pour la même raison — la
    /// transition arrive une fois par manche, alors que les joueurs sont immobiles, et l'éventuel
    /// écart est rattrapé par le seuil de réconciliation. Un client modifié ne gagne rien à ignorer
    /// ce blocage : le serveur applique le même test sur sa propre simulation et le recalera.</summary>
    public static bool MovementAllowed
    {
        get
        {
            RoundManager rm = Instance;
            return rm == null || rm.phase.Value == Phase.Active || rm.phase.Value == Phase.WaitingForPlayers;
        }
    }

    /// <summary>Le tir est autorisé pendant une manche, et pendant l'attente d'un adversaire pour
    /// s'échauffer (personne à toucher : la phase s'arrête dès que le second joueur arrive).
    /// Vérifié côté client pour le confort, et surtout côté SERVEUR dans
    /// `WeaponController.FireServerRpc` — c'est là que ça compte.</summary>
    public static bool FiringAllowed
    {
        get
        {
            RoundManager rm = Instance;
            return rm == null || rm.phase.Value == Phase.Active || rm.phase.Value == Phase.WaitingForPlayers;
        }
    }

    // ------------------------------------------------------------------
    // Boucle serveur
    // ------------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        cached = this;
        if (IsServer) SetPhase(Phase.WaitingForPlayers, 0f);
    }

    private void Update()
    {
        if (!IsServer) return;

        switch (phase.Value)
        {
            case Phase.WaitingForPlayers:
                if (CountAlivePlayers() >= 2) StartMatch();
                break;

            case Phase.Starting:
                if (!HasEnoughPlayers()) { SetPhase(Phase.WaitingForPlayers, 0f); break; }
                if (PhaseHasEnded) SetPhase(Phase.Active, roundTimeLimit);
                break;

            case Phase.Active:
                if (!HasEnoughPlayers()) { SetPhase(Phase.WaitingForPlayers, 0f); break; }
                TickActiveRound();
                break;

            case Phase.RoundOver:
                if (PhaseHasEnded) BeginRound();
                break;

            case Phase.MatchOver:
                if (PhaseHasEnded) StartMatch();
                break;
        }
    }

    private void TickActiveRound()
    {
        // Sondage plutôt qu'abonnement : voir la doc de classe.
        PlayerLocomotion dead = null;
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player == null) continue;
            Health health = player.GetComponent<Health>();
            if (health != null && health.IsDead) { dead = player; break; }
        }

        if (dead != null)
        {
            // Le SURVIVANT marque — pas "celui qui a tiré". En 1v1 c'est équivalent, et ça évite de
            // faire remonter l'identité du tireur jusqu'ici. À revoir le jour où un joueur pourra
            // mourir autrement que sous les balles de son adversaire (chute, zone, etc.).
            foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
            {
                if (player == null || player == dead) continue;
                AddWin(player.OwnerClientId);
            }

            EndRound();
            return;
        }

        if (roundTimeLimit > 0f && PhaseHasEnded)
        {
            // Manche nulle : personne ne marque. N'arrive que si une limite a été réglée pour un
            // test : le GDD n'en veut pas (voir le tooltip de roundTimeLimit).
            Debug.Log("[Round] Temps écoulé, manche nulle.");
            EndRound();
        }
    }

    private void EndRound()
    {
        if (TryGetMatchWinner(out ulong winner))
        {
            Debug.Log($"[Round] Match remporté par le client {winner}.");
            SetPhase(Phase.MatchOver, matchOverDuration);
            return;
        }

        SetPhase(Phase.RoundOver, roundOverDuration);
    }

    private void StartMatch()
    {
        scores.Clear();
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player == null) continue;
            scores.Add(new Score { clientId = player.OwnerClientId, wins = 0 });
        }

        roundNumber.Value = 0;
        BeginRound();
    }

    /// <summary>Remet les joueurs en jeu et démarre le décompte de la manche suivante.</summary>
    private void BeginRound()
    {
        if (!HasEnoughPlayers())
        {
            SetPhase(Phase.WaitingForPlayers, 0f);
            return;
        }

        roundNumber.Value++;

        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player == null) continue;

            // L'ordre compte : on soigne AVANT de replacer. ServerMoveToSpawnPoint choisit le point
            // le plus éloigné des autres joueurs, donc replacer le premier joueur influence le choix
            // du second — c'est voulu, ça garantit qu'ils repartent à deux bouts opposés même s'ils
            // sont morts au même endroit.
            Health health = player.GetComponent<Health>();
            if (health != null) health.ResetHealth();

            player.ServerMoveToSpawnPoint();
        }

        SetPhase(Phase.Starting, startingDuration);
    }

    /// <summary>Change de phase. Une durée nulle ou négative signifie : pas de fin programmée.</summary>
    private void SetPhase(Phase next, float duration)
    {
        double now = Now;
        phase.Value = next;
        phaseStartedAt.Value = now;
        phaseEndsAt.Value = duration > 0f ? now + duration : 0d;
    }

    private bool HasEnoughPlayers() => CountAlivePlayers() >= 2;

    private int CountAlivePlayers()
    {
        int count = 0;
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player != null) count++;
        }
        return count;
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

    // ------------------------------------------------------------------
    // Affichage de test — placeholder, à remplacer par le HUD UI Toolkit
    // ------------------------------------------------------------------

    // Styles construits une seule fois : OnGUI tourne à chaque frame, y créer des GUIStyle
    // allouerait en continu.
    private GUIStyle centerBigStyle;
    private GUIStyle centerMediumStyle;

    private void BuildStylesIfNeeded()
    {
        if (centerBigStyle != null) return;

        // Tailles dérivées de la hauteur d'écran, pour rester lisibles quelle que soit la
        // résolution — y compris dans les petites fenêtres du Multiplayer Play Mode.
        centerBigStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Max(48, Screen.height / 6),
            fontStyle = FontStyle.Bold,
        };
        centerBigStyle.normal.textColor = Color.white;

        centerMediumStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Max(20, Screen.height / 20),
            fontStyle = FontStyle.Bold,
        };
        centerMediumStyle.normal.textColor = Color.white;
    }

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        BuildStylesIfNeeded();
        DrawCenterMessage();
        DrawScoreBox();
    }

    /// <summary>Message plein écran entre les manches : décompte, résultat, attente.</summary>
    private void DrawCenterMessage()
    {
        string big = null;
        string small = null;

        switch (phase.Value)
        {
            case Phase.WaitingForPlayers:
                small = "En attente d'un adversaire...";
                break;

            case Phase.Starting:
                // CeilToInt pour afficher 3, 2, 1 plutôt que 2, 1, 0 : on veut voir le dernier
                // chiffre pendant toute sa seconde.
                big = Mathf.CeilToInt(PhaseTimeRemaining).ToString();
                small = $"Manche {roundNumber.Value}";
                break;

            case Phase.RoundOver:
                small = "Manche terminée";
                break;

            case Phase.MatchOver:
                big = "MATCH TERMINÉ";
                break;
        }

        // Un peu au-dessus du centre : le réticule occupe le milieu exact, et on ne veut pas que
        // le décompte masque ce que le joueur regarde.
        float y = Screen.height * 0.28f;

        if (big != null)
        {
            // Ombre portée : sans elle, du texte blanc sur l'arène claire devient illisible.
            var rect = new Rect(0, y, Screen.width, Screen.height * 0.25f);
            DrawWithShadow(rect, big, centerBigStyle);
            y += Screen.height * 0.25f;
        }

        if (small != null)
        {
            DrawWithShadow(new Rect(0, y, Screen.width, Screen.height * 0.08f), small, centerMediumStyle);
        }
    }

    private static void DrawWithShadow(Rect rect, string text, GUIStyle style)
    {
        Color previous = style.normal.textColor;

        style.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);

        style.normal.textColor = previous;
        GUI.Label(rect, text, style);
    }

    private void DrawScoreBox()
    {
        var lines = new List<string>
        {
            $"Manche {roundNumber.Value} — {PhaseLabel(phase.Value)}",
        };

        for (int i = 0; i < scores.Count; i++)
        {
            string me = scores[i].clientId == NetworkManager.Singleton.LocalClientId ? " (toi)" : "";
            lines.Add($"Joueur {scores[i].clientId}{me} : {scores[i].wins} / {roundsToWin}");
        }

        GUILayout.BeginArea(new Rect(Screen.width - 260, 10, 250, 120), GUI.skin.box);
        foreach (string line in lines) GUILayout.Label(line);
        GUILayout.EndArea();
    }

    private static string PhaseLabel(Phase p) => p switch
    {
        Phase.WaitingForPlayers => "en attente d'un adversaire",
        Phase.Starting => "prêt...",
        Phase.Active => "en cours",
        Phase.RoundOver => "manche terminée",
        Phase.MatchOver => "MATCH TERMINÉ",
        _ => p.ToString(),
    };
}
