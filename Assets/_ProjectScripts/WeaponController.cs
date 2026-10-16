using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Tir hitscan + recul déterministe. Posé sur un GameObject "Weapon" enfant du Player.
///
/// NetworkBehaviour (et non MonoBehaviour) : Fire() ne s'exécute que sur la machine du tireur
/// (garde IsOwner en tête d'Update, nécessaire pour éviter qu'une même touche physique fasse
/// tirer toutes les copies de WeaponController présentes sur l'écran).
///
/// HIT REGISTRATION SERVEUR-AUTORITAIRE : le tireur raycast localement pour son propre feedback
/// visuel INSTANTANÉ (tracer/impact, aucun dégât associé), puis envoie juste origin/direction au
/// serveur, qui refait SEUL son propre raycast et décide SEUL du hit et des dégâts
/// (Health.ApplyDamage n'est jamais appelée directement par un client sur un objet réseauté).
/// Les autres joueurs reçoivent ensuite le tracer/impact calculé par le serveur via
/// BroadcastShotClientRpc.
///
/// Le serveur valide chaque tir contre la pose PASSÉE des adversaires, celle que le tireur avait
/// à l'écran (compensation de latence, voir ServerFire).
///
/// LA PANOPLIE (2026-10-11) : une arme par touche, de 1 à 4 (fusil d'assaut, sniper, pompe,
/// raygun), chacune avec son chargeur, comme un loadout de Call of Duty. Le choix d'arme est libre
/// pour chaque joueur en attendant le pile ou face du GDD (§ 6). Changer d'arme prend le temps de la
/// sortir (WeaponData.raiseTime) et annule un rechargement.
///
/// Le CHARGEUR et l'arme en main sont tenus par le serveur (MagazineState) et prédits par le
/// tireur, qui tire, recharge et change d'arme sans attendre de réponse (voir « Chargeur » plus bas).
/// </summary>
[RequireComponent(typeof(Transform))]
public class WeaponController : NetworkBehaviour
{
    /// <summary>Places de la panoplie (touches 1 à 4). Le chargeur réseauté en garde une par arme.</summary>
    public const int MaxSlots = 4;

    [Tooltip("La panoplie du joueur, une arme par touche (1 à 4, dans cet ordre). Chacune garde son chargeur quand on en change, comme dans Call of Duty. Au plus 4.")]
    [SerializeField] private WeaponData[] loadout = new WeaponData[0];
    [SerializeField] private Camera aimCamera;
    [SerializeField] private PlayerCameraLook cameraLook;
    [SerializeField] private WeaponVisualFeedback visualFeedback;
    [SerializeField] private AudioSource audioSource;

    [Header("Recul selon la posture / le mouvement")]
    [Tooltip("Multiplicateurs (%) appliqués au recul (data.recoilPerShotDegrees) selon la posture et si le joueur se déplace ou non. Vit ici plutôt que dans WeaponData : ça s'applique automatiquement à n'importe quelle arme équipée, sans réglage à dupliquer par arme. Le déplacement en sneak (action Sneak maintenue) est plus stable que la marche normale, quelle que soit la posture, sans être aussi stable qu'à l'arrêt complet.")]
    [SerializeField, Range(0f, 100f)] private float recoilPercentStandingStatic = 85f;
    [SerializeField, Range(0f, 100f)] private float recoilPercentStandingSneaking = 92f;
    [SerializeField, Range(0f, 100f)] private float recoilPercentStandingMoving = 100f;
    [SerializeField, Range(0f, 100f)] private float recoilPercentCrouchingStatic = 45f;
    [SerializeField, Range(0f, 100f)] private float recoilPercentCrouchingSneaking = 55f;
    [SerializeField, Range(0f, 100f)] private float recoilPercentCrouchingMoving = 65f;
    [SerializeField, Range(0f, 100f)] private float recoilPercentProneStatic = 10f;
    [SerializeField, Range(0f, 100f)] private float recoilPercentProneSneaking = 20f;
    [SerializeField, Range(0f, 100f)] private float recoilPercentProneMoving = 30f;

    [Tooltip("Multiplicateur (%) additionnel appliqué par-dessus le pourcentage ci-dessus quand le joueur vise (input Aim maintenu). S'applique quelle que soit la posture/le déplacement, comme le sneak : viser stabilise toujours l'arme en plus du reste.")]
    [SerializeField, Range(0f, 100f)] private float recoilPercentWhileAiming = 50f;

    [Header("Garde-fous serveur (anti-triche)")]
    [Tooltip("Distance max (m) tolérée entre l'origine de tir annoncée par le client et la position du joueur connue du SERVEUR. Au-delà, le tir est rejeté : c'est ce qui empêche un client modifié de tirer depuis n'importe où sur la carte. Doit couvrir la hauteur caméra + le lean + l'avance de prédiction du tireur sous latence — voir le détail dans ServerFire. Si tu vois des tirs légitimes rejetés en Console pendant un test à fort ping, augmente cette valeur plutôt que de retirer le garde-fou.")]
    [SerializeField] private float maxOriginDistanceFromPlayer = 4f;

    [Tooltip("Rewind maximum (s) que le serveur accepte d'appliquer, quelle que soit la latence annoncée par le client. Couvre un ping légitime élevé (0,3 s ≈ 400 ms de ping avec le délai d'interpolation) sans permettre de tuer quelqu'un là où il était il y a une éternité — au-delà, c'est la VICTIME qui subit l'injustice, en mourant à couvert. Doit rester inférieur à 'Hitbox History Duration' sur PlayerLocomotion.")]
    [SerializeField] private float maxRewindSeconds = 0.3f;

#if UNITY_EDITOR
    // Tout ce bloc est compilé UNIQUEMENT dans l'Editor (#if UNITY_EDITOR) : il ne peut donc
    // physiquement pas se retrouver dans une build, même si la case reste cochée par mégarde dans
    // le prefab. C'est volontaire — du code de triche laissé traîner est exactement le genre de
    // chose qu'on retrouve dans une build six mois plus tard.
    [Header("Debug — triche simulée (Editor uniquement, décoche après usage)")]
    [Tooltip("SIMULE UN CLIENT MODIFIÉ. Décale l'origine de tir envoyée au serveur de N mètres vers l'avant, comme le ferait un tricheur voulant tirer depuis ailleurs (à travers un mur, derrière l'adversaire). Sert à vérifier que le garde-fou 'Max Origin Distance From Player' se déclenche bien : attendu = aucun dégât appliqué + un warning '[Serveur] Tir rejeté' en Console à chaque tir. À tester depuis l'instance CLIENT, pas le Host.")]
    [SerializeField] private bool debugFakeShotOrigin;

    [Tooltip("Décalage (m) appliqué quand 'Debug Fake Shot Origin' est coché. 50 m place l'origine largement au-delà de la tolérance serveur et de la plupart des murs de l'arène.")]
    [SerializeField] private float debugFakeShotOriginOffset = 50f;

    /// <summary>
    /// VISÉE AUTOMATIQUE DE TEST — Editor uniquement. Chaque tir part vers le centre du torse de
    /// l'adversaire TEL QUE CETTE MACHINE L'AFFICHE, au centimètre près.
    ///
    /// C'est la mesure qui juge le rewind sans biais humain : un joueur anticipe instinctivement
    /// une cible mobile, ce qui masque l'effet de la compensation (leçon du 2026-09-28). Ici, le
    /// tireur vise exactement ce qu'il voit ; si le rewind replace la cible à l'instant affiché,
    /// [DIAG-SERVEUR] doit trouver le rayon au centre du corps PASSÉ.
    /// </summary>
    public static bool AutopilotAimAtOpponent;

    /// <summary>Arme imposée au joueur de test (banc à deux instances), -1 = son choix. Editor
    /// uniquement.</summary>
    public static int AutopilotWeaponSlot = -1;

    /// <summary>Le joueur de test fait tourner sa panoplie (banc à deux instances, « autoswitch ») :
    /// une arme toutes les `AutopilotWeaponCyclePeriod` secondes. Editor uniquement.</summary>
    public static bool AutopilotWeaponCycle;
    public static float AutopilotWeaponCyclePeriod = 6f;

    private bool TryAimAtOpponent(Vector3 origin, out Vector3 direction)
    {
        direction = default;
        foreach (PlayerLocomotion other in PlayerLocomotion.SpawnedPlayers)
        {
            if (other == null || other == locomotion || other.Hitbox == null) continue;
            // La position du TRANSFORM, pas bounds.center : avec autoSyncTransforms à false, les
            // bounds d'un collider ne suivent son transform qu'au prochain pas de physique. La
            // visée tombait alors 5 à 6 cm à côté, ce qui se lisait comme une erreur du rewind.
            Vector3 target = other.Hitbox.TorsoCollider.transform.position;
            direction = (target - origin).normalized;
            return direction.sqrMagnitude > 0.5f;
        }
        return false;
    }
#endif

    private PlayerInputReader input;
    private PlayerLocomotion locomotion;
    private TaserController taser;
    private Health ownHealth; // un robot mort ne tire plus (voir ServerFire)
    private PlayerMatchState matchState; // prêt pour la manche (choix de l'arme fermé), statistiques

    // Arme demandée par le panneau de préparation (GameHud), prise comme un appui sur 1 à 4.
    private int requestedSlot = -1;

    private float cooldown;
    private float timeSinceLastShot;
    private int shotIndexInBurst;

    /// <summary>Une arme au coup par coup garde un appui de gâchette un court instant : cliquer un
    /// peu avant la fin de l'intervalle de tir tire dès qu'il est écoulé, au lieu d'être perdu.</summary>
    private const float FirePressBuffer = 0.1f;
    private float firePressBufferedUntil = float.NegativeInfinity;

    // Garde-fou anti-triche (voir ServerFire et TryAcceptShotRate) : crédit de temps du tireur,
    // côté serveur, pour faire respecter data.shotsPerSecond quoi que le client prétende faire.
    private double serverFireCredit;
    private double serverLastFireCheck = double.NegativeInfinity;

    private float verticalRecoilAccumulated;
    private float horizontalRecoilAccumulated;

    // ------------------------------------------------------------------
    // Chargeur et arme en main (2026-10-08, panoplie le 2026-10-11)
    //
    // Le SERVEUR tient le compte qui fait foi : il refuse tout tir à vide, pendant un rechargement
    // ou avant que l'arme soit sortie, et décide seul de la fin d'un rechargement, sur sa propre
    // horloge. Le PROPRIÉTAIRE prédit le même compte pour tirer, recharger et changer d'arme sans
    // attendre l'aller-retour, et se recale sur le serveur dès que plus aucune de ses demandes n'est
    // en route.
    // ------------------------------------------------------------------

    /// <summary>
    /// État du chargeur et de l'arme en main, écrit par le serveur seul et publié à tous (les
    /// spectateurs voient l'arme et entendent le rechargement, le futur HUD de tribune pourra montrer
    /// les munitions des duellistes). Un chargeur par place de la panoplie : changer d'arme garde
    /// les balles de l'autre.
    ///
    /// `actionsProcessed` compte les demandes du propriétaire (tirs, rechargements, changements
    /// d'arme) que le serveur a traitées, acceptées ou refusées : quand il égale le nombre de
    /// demandes envoyées, plus rien n'est en route, et l'état publié est directement comparable à la
    /// prédiction.
    /// </summary>
    public struct MagazineState : INetworkSerializeByMemcpy
    {
        /// <summary>Instant serveur de la fin du rechargement en cours — ou de la prochaine
        /// cartouche, pour une arme qui recharge cartouche par cartouche ; 0 = aucun.</summary>
        public double reloadEndsAt;
        /// <summary>Instant serveur où l'arme en main est sortie (changement d'arme) : on ne tire
        /// pas avant.</summary>
        public double readyAt;
        public ushort actionsProcessed;
        /// <summary>Place de l'arme en main (0 à 3).</summary>
        public byte equipped;
        public byte ammo0, ammo1, ammo2, ammo3;

        /// <summary>Balles de l'arme en main.</summary>
        public byte ammo
        {
            get => AmmoOf(equipped);
            set => SetAmmo(equipped, value);
        }

        public byte AmmoOf(int slot) => slot switch { 0 => ammo0, 1 => ammo1, 2 => ammo2, 3 => ammo3, _ => (byte)0 };

        public void SetAmmo(int slot, byte value)
        {
            switch (slot)
            {
                case 0: ammo0 = value; break;
                case 1: ammo1 = value; break;
                case 2: ammo2 = value; break;
                case 3: ammo3 = value; break;
            }
        }
    }

    private readonly NetworkVariable<MagazineState> networkMagazine = new NetworkVariable<MagazineState>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Marge (s) accordée à un tir qui arrive juste avant la fin du rechargement (ou de la
    /// sortie de l'arme) côté serveur. Le propriétaire prédit cette fin sur son horloge, et la gigue
    /// du réseau peut faire arriver son premier tir quelques millisecondes trop tôt : sans marge, ce
    /// tir serait refusé alors que le joueur l'a vu partir. Borne en temps serveur : même un client
    /// modifié ne raccourcit pas un rechargement de plus de 50 ms.</summary>
    public const double ReloadCompletionTolerance = 0.05;

    // Le chargeur PRÉDIT, chez le propriétaire (et seul chargeur hors réseau), sur l'horloge
    // MagazineClock.
    private MagazineState localMagazine;
    // Le dernier état du chargeur prédit qu'on a fait entendre (WeaponAudio.Hear), chez le
    // propriétaire : ses rechargements s'entendent sans attendre le serveur.
    private MagazineState heardMagazine;
    private WeaponAudio weaponAudio;
    // La gâchette pressée chargeur vide a déjà claqué pour cet appui.
    private bool emptyTriggerHeard;
    // Une demande de rechargement qui attend son moment : la sortie d'un sprint, ou la fin d'un
    // franchissement (2026-10-11). Chez le propriétaire seulement.
    private bool reloadPending;
    private ushort actionsSent;

    private bool IsNetworked => NetworkObject != null && IsSpawned;
    private bool UsesLocalMagazine => !IsNetworked || IsOwner;

    /// <summary>L'horloge du chargeur prédit : chez un client, NetworkManager.LocalTime, l'instant
    /// où le serveur recevra ce qu'on envoie maintenant. Une fin de rechargement prédite sur cette
    /// horloge tombe donc au même instant que celle que le serveur calculera à réception.</summary>
    private double MagazineClock => IsNetworked ? NetworkManager.LocalTime.Time : Time.timeAsDouble;

    /// <summary>L'état et l'horloge que cette machine doit montrer : la prédiction chez le
    /// propriétaire, le serveur ailleurs.</summary>
    private MagazineState ShownMagazine => UsesLocalMagazine ? localMagazine : networkMagazine.Value;
    private double ShownClock => UsesLocalMagazine ? MagazineClock : NetworkManager.ServerTime.Time;

    // ------------------------------------------------------------------
    // La panoplie
    // ------------------------------------------------------------------

    public IReadOnlyList<WeaponData> Loadout => loadout;
    public int SlotCount => loadout == null ? 0 : Mathf.Min(loadout.Length, MaxSlots);
    public WeaponData DataOf(int slot) => slot >= 0 && slot < SlotCount ? loadout[slot] : null;

    /// <summary>Place de l'arme en main, telle que cette machine doit la montrer.</summary>
    public int DisplayedSlot => ShownMagazine.equipped;

    /// <summary>L'arme en main, telle que cette machine doit la montrer.</summary>
    public WeaponData Data => DataOf(DisplayedSlot) ?? DataOf(0);

    /// <summary>Chez le propriétaire : la place de l'arme en main, jointe à chaque input (le poids de
    /// l'arme pèse sur la vitesse dans Move()).</summary>
    public byte LocalSlot => localMagazine.equipped;

    private WeaponData LocalData => DataOf(localMagazine.equipped) ?? DataOf(0);
    private WeaponData ServerData => DataOf(networkMagazine.Value.equipped) ?? DataOf(0);

    /// <summary>Le poids d'une arme de la panoplie : sa vitesse de déplacement relative. Lu par
    /// Move(), donc identique partout (les WeaponData ne changent pas en jeu).</summary>
    public float SpeedMultiplierOf(int slot)
    {
        WeaponData data = DataOf(slot);
        return data != null ? data.moveSpeedMultiplier : 1f;
    }

    /// <summary>Balles dans le chargeur de l'arme en main, telles que cette machine doit les montrer.</summary>
    public int DisplayedAmmo => ShownMagazine.ammo;
    public int MagazineSize => MagazineSizeOf(DataSlot(DisplayedSlot));
    public float ReloadDuration => ReloadDurationOf(DataSlot(DisplayedSlot));
    /// <summary>Le temps d'épaulement de l'arme en main, accessoires compris (la vue subjective).</summary>
    public float AimTime => AimTimeOf(DataSlot(DisplayedSlot));

    private PlayerWeaponAttachments attachmentsSource;

    /// <summary>Les accessoires des armes de ce joueur, publiés par le serveur (PlayerWeaponAttachments,
    /// GDD § 6) : aucun hors session.</summary>
    public WeaponAttachments Attachments => attachmentsSource != null ? attachmentsSource.Attachments : WeaponAttachments.None;

    /// <summary>La taille du chargeur de la place `slot`, accessoires compris : le chargeur grande
    /// capacité y met la moitié de balles en plus (GDD § 6). Le joueur et le serveur la lisent sur les
    /// mêmes accessoires publiés : ils comptent le même chargeur.</summary>
    public int MagazineSizeOf(int slot)
    {
        WeaponData data = DataOf(slot);
        return AttachmentCatalog.MagazineSize(data, Attachments.ExtendedMagazine(slot), data != null ? data.attachmentCatalog : null);
    }

    /// <summary>La durée d'un rechargement de la place `slot` (d'une cartouche, à une arme qui recharge
    /// cartouche par cartouche), accessoires compris.</summary>
    public float ReloadDurationOf(int slot)
    {
        WeaponData data = DataOf(slot);
        return AttachmentCatalog.ReloadDuration(data, Attachments.ExtendedMagazine(slot), data != null ? data.attachmentCatalog : null);
    }

    /// <summary>Le temps d'épaulement de la place `slot`, accessoires compris (la poignée inclinée).</summary>
    public float AimTimeOf(int slot)
    {
        WeaponData data = DataOf(slot);
        return AttachmentCatalog.AimTime(data, Attachments.Grip(slot), data != null ? data.attachmentCatalog : null);
    }

    // La place que désignent Data, LocalData et ServerData : la leur, ou la première si elle est vide.
    private int DataSlot(int slot) => DataOf(slot) != null ? slot : 0;

    /// <summary>La caméra d'où partent les tirs — et les coups de taser, qui la partagent.</summary>
    public Camera AimCamera => aimCamera;
    public bool IsReloading => ShownMagazine.reloadEndsAt > 0.0;

    /// <summary>Temps restant (s) jusqu'au chargeur plein, rechargement en cours (animation).</summary>
    public float ReloadTimeRemaining
    {
        get
        {
            MagazineState m = ShownMagazine;
            WeaponData data = Data;
            if (m.reloadEndsAt <= 0.0 || data == null) return 0f;
            float first = Mathf.Max(0f, (float)(m.reloadEndsAt - ShownClock));
            if (!data.reloadPerRound) return first;
            int slot = DataSlot(DisplayedSlot);
            return first + Mathf.Max(0, MagazineSizeOf(slot) - m.ammo - 1) * ReloadDurationOf(slot);
        }
    }

    /// <summary>Sortie de l'arme en main, de 0 (on vient d'en changer) à 1 (prête à tirer).</summary>
    public float RaiseProgress
    {
        get
        {
            WeaponData data = Data;
            if (data == null || data.raiseTime <= 0f) return 1f;
            return Mathf.Clamp01(1f - (float)(ShownMagazine.readyAt - ShownClock) / data.raiseTime);
        }
    }

    /// <summary>Chez le propriétaire : un rechargement demandé attend la fin d'un sprint ou d'un
    /// franchissement. Il arrête le sprint comme un rechargement en cours (PlayerLocomotion.GateSprint).</summary>
    public bool ReloadPending => reloadPending;

    /// <summary>Marge (s) du serveur sur la sortie de sprint : les deux côtés comptent exactement
    /// les mêmes inputs (PlayerLocomotion.TimeSinceSprint), il ne reste que l'arrondi.</summary>
    public const float SprintOutTolerance = 0.01f;

    /// <summary>Chez le propriétaire : l'arme est-elle remontée depuis le dernier sprint ? On ne tire
    /// ni ne commence de rechargement avant (WeaponData.sprintToFireTime).</summary>
    public bool ReadyAfterSprint
    {
        get
        {
#if UNITY_EDITOR
            if (PlayerLocomotion.DebugSprintIgnoresRules) return true; // client tricheur simulé
#endif
            WeaponData data = LocalData;
            return locomotion == null || data == null || locomotion.TimeSinceSprint >= data.sprintToFireTime;
        }
    }

    /// <summary>De 0 (en plein sprint) à 1 (arme remontée), pour la vue subjective.</summary>
    public float SprintRecovery
    {
        get
        {
            WeaponData data = LocalData;
            return locomotion == null || data == null || data.sprintToFireTime <= 0f
                ? 1f : Mathf.Clamp01(locomotion.TimeSinceSprint / data.sprintToFireTime);
        }
    }

    /// <summary>Avancement du rechargement en cours, de 0 à 1 (HUD). Cartouche par cartouche : la
    /// part du chargeur déjà remplie.</summary>
    public float ReloadProgress
    {
        get
        {
            WeaponData data = Data;
            MagazineState m = ShownMagazine;
            int slot = DataSlot(DisplayedSlot);
            float duration = ReloadDurationOf(slot);
            if (m.reloadEndsAt <= 0.0 || data == null || duration <= 0f) return 0f;
            if (data.reloadPerRound) return Mathf.Clamp01((float)m.ammo / Mathf.Max(1, MagazineSizeOf(slot)));
            return Mathf.Clamp01(1f - (float)((m.reloadEndsAt - ShownClock) / duration));
        }
    }

#if UNITY_EDITOR
    // Diagnostics de session (Editor uniquement). Comptés côté SERVEUR, seul endroit qui décide.
    // Ce qu'on cherche : des tirs rejetés qui ne devraient pas l'être (garde-fous trop serrés) et
    // la part réelle du rewind, invisibles autrement.
    private static int diagAccepted, diagRejectedRate, diagRejectedOrigin, diagRejectedPhase, diagRejectedAmmo, diagRejectedVault, diagRejectedDead, diagHits;
    private static float diagRewindSum, diagRewindMax;
    private static float diagNextReportAt;
    private static int diagDeferred;
    private static float diagDeferWaitSum;
    // Tirs qui, à leur arrivée, attendaient que leur input soit simulé (traités plus tard dans la
    // même image, ou à une image suivante : ces derniers sont aussi comptés dans diagDeferred).
    private static int diagQueuedAtArrival;

    // Mesure différentielle : combien de tirs le rewind fait BASCULER, et dans quel sens.
    // diagRewindLost devrait rester à 0 — le rewind ne doit jamais faire rater un tir qui
    // touchait sans lui.
    private static int diagRewindGained, diagRewindLost, diagBothHit, diagNeitherHit;

    // Mesure CONTINUE : à quelle distance du rayon se trouve la cible, à sa position PASSÉE
    // (celle que le tireur voyait) et à sa position ACTUELLE. Bien plus informative que le
    // binaire touché/raté, qui demande des centaines de tirs pour sortir du bruit.
    //
    // Si le tireur vise ce qu'il VOIT — l'hypothèse sur laquelle tout le rewind repose — la
    // distance à la position passée doit être systématiquement plus petite. Si c'est l'inverse,
    // c'est qu'il anticipe le déplacement, et le rewind travaille alors contre lui.
    private static float diagDistPastSum, diagDistNowSum;
    private static int diagDistSamples;

    private static void DiagReportIfDue()
    {
        // Static, et le rechargement de domaine est désactivé à l'entrée en Play Mode : une
        // échéance héritée d'une session précédente, où Time.time était plus grand, faisait taire
        // ce rapport pendant des minutes. Une échéance à plus de 3 s dans le futur est périmée.
        if (Time.time < diagNextReportAt && diagNextReportAt - Time.time <= 3f) return;
        diagNextReportAt = Time.time + 3f;

        int total = diagAccepted + diagRejectedRate + diagRejectedOrigin + diagRejectedPhase + diagRejectedAmmo + diagRejectedVault + diagRejectedDead;
        if (total == 0) return;

        float avgRewind = diagAccepted > 0 ? diagRewindSum / diagAccepted : 0f;
        int compared = diagRewindGained + diagRewindLost + diagBothHit + diagNeitherHit;

        Debug.Log($"[DIAG-SERVEUR] tirs={total} acceptes={diagAccepted} touches={diagHits} " +
                  $"rejets(cadence={diagRejectedRate} origine={diagRejectedOrigin} hors_manche={diagRejectedPhase} chargeur={diagRejectedAmmo} franchissement={diagRejectedVault} tireur_mort={diagRejectedDead}) " +
                  $"rewind_moy={avgRewind * 1000f:F0}ms rewind_max={diagRewindMax * 1000f:F0}ms " +
                  $"en_attente_de_l_input={diagQueuedAtArrival} differes_d_une_image={diagDeferred} attente_moy={(diagDeferred > 0 ? diagDeferWaitSum / diagDeferred * 1000f : 0f):F0}ms | " +
                  $"DIFFERENTIEL sur {compared} tirs : gagnes_par_rewind={diagRewindGained} " +
                  $"perdus_par_rewind={diagRewindLost} touches_dans_les_2_cas={diagBothHit} " +
                  $"rates_dans_les_2_cas={diagNeitherHit}" +
                  (diagDistSamples > 0
                      ? $" | VISEE : distance_au_corps_PASSE={diagDistPastSum / diagDistSamples:F2}m " +
                        $"distance_au_corps_ACTUEL={diagDistNowSum / diagDistSamples:F2}m"
                      : ""));

        diagAccepted = diagRejectedRate = diagRejectedOrigin = diagRejectedPhase = diagRejectedAmmo = diagRejectedVault = diagRejectedDead = diagHits = 0;
        diagRewindSum = diagRewindMax = 0f;
        diagDeferred = 0;
        diagDeferWaitSum = 0f;
        diagQueuedAtArrival = 0;
        diagRewindGained = diagRewindLost = diagBothHit = diagNeitherHit = 0;
        diagDistPastSum = diagDistNowSum = 0f;
        diagDistSamples = 0;
    }
#endif

    private void Awake()
    {
        input = GetComponentInParent<PlayerInputReader>();
        locomotion = GetComponentInParent<PlayerLocomotion>();
        taser = GetComponent<TaserController>();
        weaponAudio = GetComponent<WeaponAudio>();
        ownHealth = GetComponentInParent<Health>();
        matchState = GetComponentInParent<PlayerMatchState>();
        attachmentsSource = GetComponentInParent<PlayerWeaponAttachments>();

        if (input == null || locomotion == null)
        {
            Debug.LogError("WeaponController doit être un enfant du Player (PlayerInputReader/PlayerLocomotion introuvables).", this);
        }

        if (aimCamera == null || visualFeedback == null)
        {
            Debug.LogError("WeaponController : assigne 'Aim Camera' et 'Visual Feedback' dans l'inspecteur.", this);
        }

        if (SlotCount == 0) Debug.LogError("WeaponController : la panoplie est vide.", this);
        RefillAll(ref localMagazine);
        heardMagazine = localMagazine;
    }

    /// <summary>Arme choisie à l'armurerie du menu principal (GameSettings.PreferredWeaponSlot),
    /// à appliquer une seule fois dès que possible — consommée par HandleSwitchInput, qui passe
    /// par le MÊME changement d'arme authentifié que les touches 1 à 4 (aucune confiance nouvelle
    /// demandée au serveur). -1 = rien en attente.</summary>
    private int pendingStartupSlot = -1;

    public override void OnNetworkSpawn()
    {
        if (IsServer) ServerRefillMagazine();

        localMagazine = networkMagazine.Value;
        heardMagazine = localMagazine;
        actionsSent = localMagazine.actionsProcessed;
        networkMagazine.OnValueChanged += HandleMagazineChanged;

        if (IsOwner)
        {
            int preferred = GameSettings.PreferredWeaponSlot;
            if (preferred > 0 && preferred < MaxSlots) pendingStartupSlot = preferred;
        }
    }

    public override void OnNetworkDespawn()
    {
        networkMagazine.OnValueChanged -= HandleMagazineChanged;
    }

    private void Update()
    {
        // Le serveur traite les demandes et termine les rechargements de TOUS les joueurs, donc
        // avant la garde IsOwner.
        if (IsNetworked && IsServer)
        {
            ServerProcessArmsActions();
            ServerUpdateMagazine();
        }

        // Maintenant que PlayerLocomotion est réseauté, il y a potentiellement plusieurs
        // instances de Player dans la scène en même temps. PlayerInputReader lit le clavier/la
        // souris PHYSIQUE de la machine sans savoir à qui elle appartient — sans ce garde-fou,
        // CHAQUE copie de WeaponController (y compris celle des autres joueurs affichés sur ton
        // écran) tirerait à chaque clic. On ne traite l'input que pour SON PROPRE joueur.
        // (NetworkObject == null : composant testé hors contexte réseau, on laisse passer.)
        if (NetworkObject != null && !IsOwner) return;
        // Un spectateur n'a pas d'arme (2026-10-13) : son clic gauche frappe ou lance (SpectatorController).
        if (IsSpectatorRole) return;
        WeaponData data = LocalData;
        if (data == null || input == null) return;

        timeSinceLastShot += Time.deltaTime;
        if (timeSinceLastShot > data.recoilResetDelay)
        {
            shotIndexInBurst = 0;
        }

        cooldown -= Time.deltaTime;
        ApplyRecoilRecovery(data);
        UpdateLocalMagazine();
        HandleSwitchInput();
        HandlePowerUpInput();
        HandleReloadInput();
        HandleFireInput();
        HearLocalMagazine();
    }

    /// <summary>Chez le propriétaire : fait entendre ce que ses actions et le recalage ont changé au
    /// chargeur prédit depuis la dernière fois (rechargement commencé, interrompu, cartouche glissée).
    /// Les autres l'entendent par le chargeur publié (HandleMagazineChanged).</summary>
    private void HearLocalMagazine()
    {
        if (weaponAudio != null) weaponAudio.Hear(heardMagazine, localMagazine, DataOf(heardMagazine.equipped), DataOf(localMagazine.equipped));
        heardMagazine = localMagazine;
    }

    /// <summary>Ce joueur est-il spectateur (rôle publié par le serveur) ? Il n'a alors pas d'arme.</summary>
    private bool IsSpectatorRole => matchState != null && matchState.IsSpectator;

    // ------------------------------------------------------------------
    // Prendre une arme lancée par un spectateur (2026-10-13, GDD § 9)
    // ------------------------------------------------------------------

    [Header("Power-ups lancés par les spectateurs")]
    [Tooltip("Portée (m) à laquelle on prend une arme posée au sol, mesurée à plat depuis les pieds.")]
    [SerializeField] private float powerUpReach = 1.8f;

    /// <summary>Chez le joueur : l'arme posée qu'il peut prendre (touche F), ou null. Lue par le HUD.</summary>
    public ArenaItem PowerUpCandidate { get; private set; }

    /// <summary>
    /// Une arme lancée dans l'arène par un spectateur se prend avec la touche F, à ses pieds. Tranché par
    /// l'utilisateur le 2026-10-13 : le duelliste la prend en main avec un chargeur plein, même en pleine
    /// manche (l'exception explicite à « on ne change plus d'arme », GDD § 6). Prédit comme un
    /// changement d'arme, puis vérifié par le serveur, qui peut le refuser (l'arme a déjà été prise).
    /// </summary>
#if UNITY_EDITOR
    /// <summary>BANC DE TEST — Editor uniquement : le duelliste prend seul l'arme lancée à ses pieds.</summary>
    public static bool AutopilotPickUpPowerUps;
#endif

    private void HandlePowerUpInput()
    {
        PowerUpCandidate = FindPowerUp(transform.root.position, powerUpReach);
        bool wants = input.InteractPressedThisFrame;
#if UNITY_EDITOR
        if (AutopilotPickUpPowerUps) wants = true;
#endif
        if (PowerUpCandidate == null || !wants) return;
        if (ownHealth != null && ownHealth.IsDead) return;
        if (!RoundManager.FiringAllowedFor(true)) return;
        int slot = SlotOfPowerUp(PowerUpCandidate);
        if (slot < 0) return;

        WeaponData current = LocalData;
        int currentSlot = DataSlot(localMagazine.equipped);
        CompleteReloadIfDue(ref localMagazine, MagazineClock, MagazineSizeOf(currentSlot), current.reloadPerRound,
                            ReloadDurationOf(currentSlot));
        ApplyPowerUp(ref localMagazine, slot, MagazineClock, DataOf(slot).raiseTime, MagazineSizeOf(slot));
        reloadPending = false;
        cooldown = 0f;
        shotIndexInBurst = 0;
        firePressBufferedUntil = float.NegativeInfinity;

        if (IsNetworked)
        {
            actionsSent++; // AVANT l'appel : chez le Host, la RPC s'exécute immédiatement
            PickUpPowerUpServerRpc(new NetworkObjectReference(PowerUpCandidate.NetworkObject), ReferenceInputForRequest());
        }
        PowerUpCandidate = null;
    }

    /// <summary>L'arme posée la plus proche de `feet`, à plat, à moins de `reach` mètres (et pas plus de
    /// deux mètres au-dessus ou au-dessous), ou null.</summary>
    public static ArenaItem FindPowerUp(Vector3 feet, float reach)
    {
        ArenaItem best = null;
        float bestDistance = reach;
        foreach (ArenaItem item in ArenaItem.Spawned)
        {
            if (item == null || item.Kind != ArenaItemKind.PowerUp || !item.IsResting) continue;
            Vector3 to = item.RestPosition - feet;
            if (Mathf.Abs(to.y) > 2f) continue;
            float distance = new Vector2(to.x, to.z).magnitude;
            if (distance > bestDistance) continue;
            best = item;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>La place de la panoplie de l'arme d'un power-up, ou -1 si elle n'en fait pas partie.</summary>
    private int SlotOfPowerUp(ArenaItem item)
    {
        PowerUpCatalog.Entry entry = item != null && item.Catalog != null ? item.Catalog.Get(item.Variant) : null;
        if (entry == null || entry.weapon == null) return -1;
        for (int slot = 0; slot < SlotCount; slot++)
        {
            if (loadout[slot] == entry.weapon) return slot;
        }
        return -1;
    }

    /// <summary>
    /// L'arme d'un power-up en main, chargeur plein : on la sort (raiseTime) si ce n'était pas celle
    /// qu'on tenait, et le rechargement en cours est abandonné. Fonction pure, testée.
    /// </summary>
    public static void ApplyPowerUp(ref MagazineState magazine, int slot, double now, float raiseTime, int magazineSize)
    {
        if (slot < 0 || slot >= MaxSlots) return;
        if (!TrySwitchWeapon(ref magazine, slot, now, raiseTime)) magazine.reloadEndsAt = 0.0;
        magazine.SetAmmo(slot, ClampAmmo(magazineSize));
    }

    /// <summary>Le duelliste prend l'arme posée `itemReference`. Jugé à l'arrivée (le premier arrivé la
    /// prend), puis appliqué dans l'ordre de ses demandes (la file), comme un changement d'arme.</summary>
    [ServerRpc]
    private void PickUpPowerUpServerRpc(NetworkObjectReference itemReference, int inputSequence)
    {
        if (serverArmsActions.Count >= MaxQueuedArmsActions) return;
        ArmsAction action = Received(ArmsActionKind.PowerUp, inputSequence);
        action.refused = true;

        bool alive = ownHealth == null || !ownHealth.IsDead;
        if (!IsSpectatorRole && alive && RoundManager.FiringAllowedFor(true)
            && itemReference.TryGet(out NetworkObject itemObject) && itemObject.TryGetComponent(out ArenaItem item)
            && item.ServerIsPickable && item.Kind == ArenaItemKind.PowerUp)
        {
            Vector3 to = item.RestPosition - transform.root.position;
            int slot = SlotOfPowerUp(item);
            // Marge pour l'avance de la prédiction du joueur sur le serveur.
            if (slot >= 0 && Mathf.Abs(to.y) <= 2.5f && new Vector2(to.x, to.z).magnitude <= powerUpReach + 1f)
            {
                action.refused = false;
                action.slot = (byte)slot;
                item.ServerConsume();
                if (locomotion != null) locomotion.ServerScheduleWeaponSlot(action.inputSequence + 1, (byte)slot);
                PowerUpTakenClientRpc();
                Debug.Log($"[Spectateurs] {PlayerMatchState.NameOf(OwnerClientId)} prend {item.DisplayName}.");
            }
        }
        serverArmsActions.Enqueue(action);
        ServerProcessArmsActions();
    }

    [ClientRpc]
    private void PowerUpTakenClientRpc()
    {
        Tribune tribune = Tribune.Instance;
        if (tribune != null && tribune.Sounds != null) tribune.Sounds.powerUpTaken.PlayOn(audioSource);
    }

    /// <summary>Serveur : l'arme du power-up en main, chargeur plein. Comptée comme une demande,
    /// acceptée ou refusée : le joueur l'a prédite.</summary>
    private void ServerPowerUp(ArmsAction action)
    {
        MagazineState magazine = networkMagazine.Value;
        magazine.actionsProcessed++;
        WeaponData next = DataOf(action.slot);
        WeaponData current = ServerData;
        if (!action.refused && next != null && current != null)
        {
            int currentSlot = DataSlot(magazine.equipped);
            CompleteReloadIfDue(ref magazine, action.serverTime + ReloadCompletionTolerance, MagazineSizeOf(currentSlot),
                                current.reloadPerRound, ReloadDurationOf(currentSlot));
            bool switching = action.slot != magazine.equipped;
            ApplyPowerUp(ref magazine, action.slot, action.serverTime, next.raiseTime, MagazineSizeOf(action.slot));
            if (switching) ResetShotRateOnSwitch(ref serverLastFireCheck);
        }
        networkMagazine.Value = magazine;
    }

    // ------------------------------------------------------------------
    // Changer d'arme (touches 1 à 4, 2026-10-11)
    // ------------------------------------------------------------------

    /// <summary>
    /// Le joueur peut-il encore changer d'arme ? Seulement avant la manche (2026-10-12) : pendant la
    /// préparation tant qu'il n'a pas validé, et en attendant un adversaire. Le serveur applique la
    /// même règle à chaque demande (SwitchWeaponServerRpc).
    /// </summary>
    public bool CanChooseWeapon => !IsSpectatorRole && RoundManager.WeaponSwitchAllowedFor(matchState != null && matchState.ReadyOrRequested);

    /// <summary>Le panneau de préparation demande l'arme de la place `slot`, comme un appui sur les
    /// touches 1 à 4 (même chemin, mêmes contrôles du serveur).</summary>
    public void RequestWeapon(int slot)
    {
        if (slot >= 0 && slot < SlotCount) requestedSlot = slot;
    }

    private void HandleSwitchInput()
    {
        int wanted = input.WeaponSlotPressedThisFrame;
        if (wanted < 0 && requestedSlot >= 0) wanted = requestedSlot;
        if (wanted < 0 && pendingStartupSlot >= 0) wanted = pendingStartupSlot;
        requestedSlot = -1;
        pendingStartupSlot = -1; // un seul essai, qu'il réussisse ou non (place déjà équipée, etc.)
#if UNITY_EDITOR
        if (AutopilotWeaponSlot >= 0 && AutopilotWeaponSlot != localMagazine.equipped) wanted = AutopilotWeaponSlot;
        if (AutopilotWeaponCycle && SlotCount > 0)
        {
            int cycled = (int)(Time.time / Mathf.Max(0.5f, AutopilotWeaponCyclePeriod)) % SlotCount;
            if (cycled != localMagazine.equipped) wanted = cycled;
        }
#endif
        if (wanted < 0 || wanted == localMagazine.equipped || DataOf(wanted) == null) return;
        if (!CanChooseWeapon) return;

        // Prédit : l'arme change tout de suite chez le joueur, et le serveur la changera à réception,
        // à l'instant que MagazineClock annonce. Un rechargement en cours ou en attente est annulé.
        WeaponData current = LocalData;
        int currentSlot = DataSlot(localMagazine.equipped);
        CompleteReloadIfDue(ref localMagazine, MagazineClock, MagazineSizeOf(currentSlot), current.reloadPerRound,
                            ReloadDurationOf(currentSlot));
        TrySwitchWeapon(ref localMagazine, wanted, MagazineClock, DataOf(wanted).raiseTime);
        reloadPending = false;
        cooldown = 0f;
        shotIndexInBurst = 0;
        firePressBufferedUntil = float.NegativeInfinity;

        if (IsNetworked)
        {
            actionsSent++; // AVANT l'appel : chez le Host, la RPC s'exécute immédiatement
            SwitchWeaponServerRpc((byte)wanted, ReferenceInputForRequest());
        }
    }

    // ------------------------------------------------------------------
    // Tir
    // ------------------------------------------------------------------

    private void HandleFireInput()
    {
        WeaponData data = LocalData;
        bool wantsToFire;
        if (data.isAutomatic)
        {
            wantsToFire = input.FireHeld;
        }
        else
        {
            if (input.FireWasPressedThisFrame) firePressBufferedUntil = Time.time + FirePressBuffer;
            wantsToFire = Time.time <= firePressBufferedUntil;
#if UNITY_EDITOR
            if (PlayerInputReader.AutopilotFire) wantsToFire = input.FireHeld;
#endif
        }

        // La gâchette pressée chargeur vide claque une fois par appui (2026-10-13) : le tireur sait
        // qu'il doit recharger. Rien n'est envoyé au serveur.
        bool emptyTrigger = input.FireHeld && localMagazine.ammo == 0 && localMagazine.reloadEndsAt <= 0.0
                            && RoundManager.FiringAllowedFor(ownHealth == null || !ownHealth.IsDead);
        if (emptyTrigger && !emptyTriggerHeard && weaponAudio != null) weaponAudio.PlayDryFire(data);
        emptyTriggerHeard = emptyTrigger;

        // Le tir n'est possible qu'en déplacement normal/sneak/accroupi/allongé (lean ou non),
        // jamais en sprint ni en franchissant un obstacle (2026-10-11), ni avant que l'arme soit
        // sortie. Confort local : on ne déclenche même pas le recul ni le son hors manche. Le vrai
        // verrou est côté serveur dans ServerFire — celui-ci n'est qu'une convention client.
        bool armed = MagazineClock >= localMagazine.readyAt && localMagazine.ammo > 0
                     && (localMagazine.reloadEndsAt <= 0.0 || data.reloadPerRound);
        bool vaulting = locomotion.IsVaulting;
#if UNITY_EDITOR
        if (PlayerLocomotion.DebugVaultIgnoresRules) vaulting = false; // client tricheur simulé
#endif
        if (wantsToFire && cooldown <= 0f && armed && !locomotion.IsSprinting && !vaulting
            && ReadyAfterSprint && RoundManager.FiringAllowedFor(ownHealth == null || !ownHealth.IsDead)
            && (taser == null || !taser.IsStriking))
        {
            // Le cooldown doit être posé AVANT Fire() : si Fire() lève une exception (ex.
            // référence non assignée dans l'inspecteur), on ne veut surtout pas rester avec
            // cooldown <= 0, sinon HandleFireInput retire au tir (et donc au recul) à chaque
            // frame suivante au lieu de respecter shotsPerSecond.
            cooldown = 1f / Mathf.Max(0.01f, data.shotsPerSecond);
            firePressBufferedUntil = float.NegativeInfinity;
            Fire(data);
        }
    }

    private void Fire(WeaponData data)
    {
#if UNITY_EDITOR
        if (locomotion != null && locomotion.TimeSinceSprint < 1f)
            Debug.Log($"[DIAG-SPRINT] tir à {locomotion.TimeSinceSprint * 1000f:F0} ms de la fin du sprint");
#endif
        // Prédit, sans la marge du serveur : le joueur ne tire jamais avant ce qu'il voit.
        int firedSlot = DataSlot(localMagazine.equipped);
        TryConsumeRound(ref localMagazine, MagazineClock, MagazineSizeOf(firedSlot), data.reloadPerRound, ReloadDurationOf(firedSlot), 0.0);
        timeSinceLastShot = 0f;
        OnShotFired?.Invoke();
        PlayFireSound(data);

        // IMPORTANT : on lit origin/direction AVANT d'appliquer le recul de CE tir. Sinon
        // (comme c'était le cas avant ce fix), la caméra a déjà tourné au moment où on lit
        // aimCamera.transform.forward, et le tir atterrit décalé par son propre recul au lieu
        // de partir exactement là où le crosshair pointait au moment de la pression. C'est ce
        // qui expliquait le décalage variable observé selon la posture (quasi nul en prone,
        // léger en crouch, marqué debout) : l'ampleur suit exactement GetRecoilMultiplier().
        Vector3 origin = aimCamera.transform.position;
        Vector3 direction = aimCamera.transform.forward;
#if UNITY_EDITOR
        if (AutopilotAimAtOpponent && TryAimAtOpponent(origin, out Vector3 autoDirection)) direction = autoDirection;
#endif
        // La gerbe se resserre en visée : celle du dernier input envoyé, que le serveur relira.
        bool aiming = locomotion != null && locomotion.IsAiming;

        // Raycast LOCAL : ne sert qu'au feedback visuel instantané du tireur (tracer/impact), pas
        // aux dégâts ni à ce qui est envoyé au serveur — voir ServerFire. Il utilise la MÊME
        // résolution que le serveur (ResolveShot, PelletDirection) pour que ce que le tireur voit
        // corresponde à ce que le serveur décide, aux différences de latence près.
        var impacts = new ShotImpactSounds(data);
        for (int i = 0; i < data.PelletCount; i++)
        {
            Vector3 pellet = PelletDirection(direction, data.PelletOffset(i, aiming));
            bool hitSomething = ResolveShot(data, origin, pellet, out Vector3 end, out Vector3 localNormal, out Collider bodyHit, out _);
            bool onBody = bodyHit != null;
            // Une épave sur le chemin encaisse le tir et l'arrête (cosmétique, voir RobotCorpse).
            if (RobotCorpse.TryHit(origin, end, CorpseImpulse(data), out Vector3 wreckPoint, out Vector3 wreckNormal))
            {
                end = wreckPoint;
                localNormal = wreckNormal;
                hitSomething = true;
                onBody = true;
            }
            Debug.DrawLine(origin, end, hitSomething ? Color.red : Color.gray, 0.5f);
            if (visualFeedback != null)
            {
                visualFeedback.SpawnTracer(origin, end, data.tracerColor);
                if (hitSomething) visualFeedback.SpawnImpact(end, localNormal);
            }
            if (hitSomething) impacts.Add(end, onBody);
        }

        if (NetworkObject != null)
        {
            // Contexte réseau normal : seuls origin/direction partent au serveur, qui refait SEUL
            // son propre raycast et décide SEUL du résultat (voir ServerFire) — un client modifié
            // ne peut donc plus mentir sur ce qu'il prétend avoir touché.
            Vector3 sentOrigin = origin;
#if UNITY_EDITOR
            // Triche simulée : on ment au serveur sur l'origine du tir, exactement comme le ferait
            // un client modifié. Le feedback visuel local ci-dessus reste, lui, honnête — on voit
            // donc bien à l'écran que le tir part d'où il devrait, alors que le serveur le rejette.
            if (debugFakeShotOrigin) sentOrigin = origin + direction * debugFakeShotOriginOffset;
#endif
            // Le tireur annonce l'instant, sur l'horloge du serveur, de ce qu'il voit des autres.
            // Le serveur borne le recul qui en découle : c'est une suggestion, pas une donnée de
            // confiance. La place de l'arme sert à repérer un désaccord, jamais à choisir l'arme.
            actionsSent++; // AVANT l'appel : chez le Host, la RPC s'exécute immédiatement
            FireServerRpc(sentOrigin, direction, locomotion.ViewServerTime, localMagazine.equipped, ReferenceInputForRequest());
        }
        else
        {
            // Composant testé hors contexte réseau (pas de NetworkObject) : pas de serveur à qui
            // déléguer, on applique les dégâts directement en local comme avant le passage réseau.
            var damage = new Dictionary<Health, float>();
            for (int i = 0; i < data.PelletCount; i++)
            {
                Vector3 pellet = PelletDirection(direction, data.PelletOffset(i, aiming));
                ResolveShot(data, origin, pellet, out _, out _, out Collider body, out float distance);
                AddDamage(damage, data, body, distance);
            }
            foreach (var pair in damage) pair.Key.ApplyDamage(pair.Value, transform.root.gameObject);
        }

        // Le recul de CE tir est appliqué maintenant, après avoir déterminé où il a atterri —
        // il influence donc la visée du PROCHAIN tir (comportement voulu pour une rafale
        // progressive), jamais celui qui vient d'être tiré.
        ApplyRecoilKick(data);

        shotIndexInBurst++;
    }

    // Portée des impacts entendus (m) : plein volume jusqu'au minimum, puis atténués.
    private const float ImpactMinDistance = 2f;
    private const float BodyImpactMinDistance = 3f;
    private const float ImpactMaxDistance = 60f;

    /// <summary>Les impacts qu'un tir fait entendre, là où il arrive (2026-10-13) : un sur le décor,
    /// un sur un robot, au plus. Les huit plombs du pompe ne font pas huit bruits.</summary>
    private struct ShotImpactSounds
    {
        private readonly WeaponData data;
        private bool world;
        private bool body;

        public ShotImpactSounds(WeaponData data)
        {
            this.data = data;
            world = false;
            body = false;
        }

        public void Add(Vector3 point, bool onBody)
        {
            if (data == null) return;
            if (onBody)
            {
                if (body) return;
                body = true;
                data.bodyImpactSounds?.PlayAt(point, BodyImpactMinDistance, ImpactMaxDistance);
            }
            else
            {
                if (world) return;
                world = true;
                data.impactSounds?.PlayAt(point, ImpactMinDistance, ImpactMaxDistance);
            }
        }
    }

    /// <summary>Levé à chaque tir VU depuis cette machine : chez le tireur depuis Fire(), chez
    /// tous les autres depuis BroadcastShotClientRpc. Purement cosmétique — il sert à déclencher
    /// l'animation de tir, qui doit être visible sur l'adversaire comme sur soi.
    ///
    /// Le tir était déjà diffusé à tout le monde pour le son et le tracer : aucun réseau
    /// supplémentaire n'a été nécessaire, seulement un point d'accroche.</summary>
    public event System.Action OnShotFired;

    /// <summary>Joue le son de tir de l'arme (coup, queue de réverbération, mécanique : WeaponAudio).
    /// Appelé localement par le tireur dans Fire(), et rediffusé aux autres clients par
    /// BroadcastShotClientRpc — sinon, comme pour le visuel avant l'ajout du ClientRpc, seul le
    /// tireur entendrait ses propres tirs.</summary>
    private void PlayFireSound(WeaponData data)
    {
        if (weaponAudio != null) weaponAudio.PlayShot(data);
    }

    /// <summary>
    /// Direction d'un plomb : l'axe du regard tourné de `offsetDegrees` (x à droite, y en haut),
    /// dans un repère construit sur la verticale du monde. Fonction pure, testée.
    ///
    /// Le repère ne dépend QUE de la direction : le tireur et le serveur, qui ne connaît pas le
    /// roulis de la caméra (lean), calculent donc exactement la même gerbe. C'est ce qui la rend
    /// « motif fixe » (GDD § 3) : deux tirs identiques touchent exactement les mêmes points.
    /// </summary>
    public static Vector3 PelletDirection(Vector3 forward, Vector2 offsetDegrees)
    {
        forward = forward.normalized;
        if (offsetDegrees == Vector2.zero) return forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 1e-6f) right = Vector3.right; // regard à la verticale
        right.Normalize();
        Vector3 up = Vector3.Cross(forward, right);
        return (Quaternion.AngleAxis(offsetDegrees.x, up) * Quaternion.AngleAxis(-offsetDegrees.y, right) * forward).normalized;
    }

    // ------------------------------------------------------------------
    // Les demandes du joueur, côté serveur : une file ORDONNÉE (2026-10-11)
    //
    // Pourquoi une file. On ne tire pas en franchissant un obstacle, et le serveur doit le vérifier
    // sans croire le client. Il sait QUAND le joueur a tiré : la demande annonce le dernier input
    // produit avant elle, que le serveur borne (PlayerLocomotion.ServerReferenceInput : jamais avant
    // le dernier input déjà reçu), donc l'état qui compte est celui qui suit cet input
    // (inputSequence). Depuis le 2026-10-13, les inputs voyagent en non fiable et les demandes en
    // fiable : l'input de référence peut n'être ni reçu ni SIMULÉ à l'arrivée de la demande (les
    // inputs attendent aussi dans la file de PlayerLocomotion) : la demande attend donc qu'il le
    // soit. Et toutes les demandes attendent dans l'ordre, sinon un rechargement pourrait doubler le
    // tir qui le précédait.
    //
    // Ce qui dépend de l'instant d'ARRIVÉE (horloge du chargeur, cadence, recul du rewind) est relevé
    // à l'arrivée et voyage avec la demande : la traiter une image plus tard ne change rien au
    // résultat. La sortie de sprint, elle, se lit sur l'input de référence, au traitement.
    // ------------------------------------------------------------------

    private enum ArmsActionKind : byte { Fire, Reload, Switch, CancelReload, PowerUp }

    private struct ArmsAction
    {
        public ArmsActionKind kind;
        /// <summary>Dernier input reçu avant la demande : l'état du joueur qui compte.</summary>
        public int inputSequence;
        /// <summary>Horloge du serveur à l'arrivée (chargeur, rewind).</summary>
        public double serverTime;
        /// <summary>Temps réel à l'arrivée (cadence de tir).</summary>
        public double realTime;
        public Vector3 origin;
        public Vector3 direction;
        public double viewServerTime;
        public byte slot;
        /// <summary>Changement d'arme refusé dès l'arrivée : la manche était lancée, ou le joueur
        /// avait déjà validé son arme.</summary>
        public bool refused;
    }

    private readonly Queue<ArmsAction> serverArmsActions = new Queue<ArmsAction>();

    /// <summary>Demandes en attente au plus (2026-10-12). Un joueur honnête n'en a jamais plus de
    /// quelques-unes : elles attendent au plus l'input qui les précède. Sans borne, un client modifié
    /// qui inonderait le rechargement ou le changement d'arme faisait grandir la file sans limite
    /// (règle 2 des garde-fous). Au-delà, la demande est ignorée.</summary>
    private const int MaxQueuedArmsActions = 64;

    /// <summary>Attente maximale (s, temps réel) d'une demande dont l'input n'est jamais simulé
    /// (inputs jetés par le plafond de la file, client qui n'envoie plus rien) : elle est alors
    /// traitée sans l'état de cet input.</summary>
    private const double MaxArmsActionWait = 0.3;

    /// <summary>Une demande arrivée maintenant. `claimedInput` : le dernier input que le joueur dit
    /// avoir produit avant elle, borné par le serveur (PlayerLocomotion.ServerReferenceInput).</summary>
    private ArmsAction Received(ArmsActionKind kind, int claimedInput) => new ArmsAction
    {
        kind = kind,
        inputSequence = locomotion != null ? locomotion.ServerReferenceInput(claimedInput) : -1,
        serverTime = NetworkManager.ServerTime.Time,
        realTime = Time.timeAsDouble,
    };

    /// <summary>Chez le joueur : le dernier input produit, annoncé avec chaque demande. Les inputs pas
    /// encore partis partent avec elle : le serveur n'attend pas l'envoi suivant pour la juger.</summary>
    private int ReferenceInputForRequest()
    {
        if (locomotion == null) return -1;
        locomotion.SendPendingInputs();
        return locomotion.LastInputSequence;
    }

    /// <summary>Serveur : la sortie de sprint telle que le joueur l'avait au moment de sa demande,
    /// comptée sur ses inputs jusqu'à l'input de référence.</summary>
    private float ServerTimeSinceSprint(ArmsAction action)
        => locomotion != null ? locomotion.ServerTimeSinceSprintAt(action.inputSequence) : float.PositiveInfinity;

    [ServerRpc]
    private void FireServerRpc(Vector3 origin, Vector3 direction, double viewServerTime, byte slot, int inputSequence)
    {
        if (serverArmsActions.Count >= MaxQueuedArmsActions) return;
        ArmsAction action = Received(ArmsActionKind.Fire, inputSequence);
        action.origin = origin;
        action.direction = direction;
        action.viewServerTime = viewServerTime;
        action.slot = slot;
        serverArmsActions.Enqueue(action);
        ServerProcessArmsActions();
#if UNITY_EDITOR
        if (serverArmsActions.Count > 0) diagQueuedAtArrival++;
#endif
    }

    /// <summary>Le serveur décide seul du début d'un rechargement, sur sa propre horloge : le
    /// client ne transmet aucun instant.</summary>
    [ServerRpc]
    private void ReloadServerRpc(int inputSequence)
    {
        if (serverArmsActions.Count >= MaxQueuedArmsActions) return;
        serverArmsActions.Enqueue(Received(ArmsActionKind.Reload, inputSequence));
        ServerProcessArmsActions();
    }

    /// <summary>Changement d'arme. Le poids de la nouvelle arme pèse sur le déplacement à partir de
    /// l'input qui suit la demande — décidé ICI, à l'arrivée, avant que cet input ne soit simulé
    /// (PlayerLocomotion.ServerScheduleWeaponSlot).</summary>
    [ServerRpc]
    private void SwitchWeaponServerRpc(byte slot, int inputSequence)
    {
        if (serverArmsActions.Count >= MaxQueuedArmsActions) return;
        ArmsAction action = Received(ArmsActionKind.Switch, inputSequence);
        action.slot = slot;
        // On ne change d'arme qu'avant la manche (2026-10-12) : jugé À L'ARRIVÉE. La demande de
        // validation du joueur (PlayerMatchState) voyage après ses changements d'arme : un choix fait
        // avant de valider est donc toujours accepté, même s'il n'est appliqué qu'au début du
        // décompte (la file attend l'input qui le précède).
        action.refused = IsSpectatorRole || !RoundManager.WeaponSwitchAllowedFor(matchState != null && matchState.Ready);
        if (!action.refused && slot < SlotCount && DataOf(slot) != null && locomotion != null)
            locomotion.ServerScheduleWeaponSlot(action.inputSequence + 1, slot);
        serverArmsActions.Enqueue(action);
        ServerProcessArmsActions();
    }

    /// <summary>Le taser annule le rechargement en cours, dans l'ordre des demandes du joueur
    /// (TaserController), après l'input `referenceInput` (déjà borné par le taser). Ne compte pas
    /// comme une demande : le recalage du chargeur rattrape l'écart.</summary>
    public void ServerQueueCancelReload(int referenceInput)
    {
        if (!IsServer) return;
        serverArmsActions.Enqueue(Received(ArmsActionKind.CancelReload, referenceInput));
        ServerProcessArmsActions();
    }

    /// <summary>Traite, dans l'ordre, les demandes dont l'input de référence a été simulé. Appelé à
    /// l'arrivée de chaque demande, après chaque simulation des inputs (PlayerLocomotion), et à
    /// chaque image.</summary>
    public void ServerProcessArmsActions()
    {
        if (!IsServer) return;
        while (serverArmsActions.Count > 0)
        {
            ArmsAction action = serverArmsActions.Peek();
            bool simulated = locomotion == null || locomotion.ServerLastSimulatedInput >= action.inputSequence;
            if (!simulated && Time.timeAsDouble - action.realTime < MaxArmsActionWait) return;
            serverArmsActions.Dequeue();
#if UNITY_EDITOR
            if (Time.timeAsDouble > action.realTime)
            {
                diagDeferred++;
                diagDeferWaitSum += (float)(Time.timeAsDouble - action.realTime);
            }
#endif
            switch (action.kind)
            {
                case ArmsActionKind.Fire: ServerFire(action); break;
                case ArmsActionKind.Reload: ServerReload(action); break;
                case ArmsActionKind.Switch: ServerSwitch(action); break;
                case ArmsActionKind.CancelReload: ServerCancelReload(); break;
                case ArmsActionKind.PowerUp: ServerPowerUp(action); break;
            }
        }
    }

    /// <summary>
    /// Autorité serveur du tir, AVEC compensation de latence. Le serveur refait seul son raycast
    /// depuis l'origin/direction fournis, mais contre l'état des adversaires tel qu'il était au
    /// moment où le tireur a réellement visé — pas au moment où la RPC arrive.
    ///
    /// Sans ça, un tireur qui vise juste rate un adversaire en mouvement, parce que le serveur le
    /// valide contre une position plus récente que celle affichée sur son écran. L'écart vaut à peu
    /// près (RTT/2 + délai d'interpolation) × vitesse de la cible : à 150 ms de ping et 8 m/s, plus
    /// d'un mètre, soit largement la largeur d'un joueur.
    ///
    /// `viewServerTime` est l'instant, sur l'horloge du serveur, de ce que le tireur avait à
    /// l'écran (PlayerLocomotion.ViewServerTime) : ses adversaires y sont interpolés à partir de
    /// poses datées par le serveur lui-même, donc l'instant est EXACT — plus besoin d'estimer la
    /// latence (2026-10-06). C'est malgré tout une SUGGESTION du client, jamais une donnée de
    /// confiance : le recul qui en découle est borné. Sans cette borne, un client modifié
    /// annoncerait un instant très ancien pour toucher ses adversaires là où ils n'ont plus été
    /// depuis longtemps. C'est le seul paramètre "libre" accepté — tout le reste (hit, dégâts,
    /// position des cibles, arme en main, gerbe) est recalculé ici.
    ///
    /// Le rewind ne déplace QUE les hitbox, jamais les joueurs eux-mêmes : la simulation en cours
    /// continue sur les vraies positions, et tout se déroule de façon synchrone ici, donc invisible
    /// pour le reste du jeu.
    /// </summary>
    private void ServerFire(ArmsAction action)
    {
        // Chaque demande compte, acceptée ou refusée : c'est ce qui dit au propriétaire que plus
        // rien n'est en route, donc qu'il peut se recaler sur le chargeur publié (ReconcileMagazine).
        MagazineState magazine = networkMagazine.Value;
        magazine.actionsProcessed++;
        networkMagazine.Value = magazine;

        WeaponData data = ServerData;
        if (data == null) return;

        // Un spectateur n'a pas d'arme (2026-10-13) : seul un client modifié en demande un tir.
        if (IsSpectatorRole) return;

        // Un robot mort ne tire plus (2026-10-12, tranché par l'utilisateur : quand deux joueurs se
        // tirent dessus au même instant, le premier tir reçu gagne). Sans ce test, le tir d'un joueur
        // tué plus tôt dans la même image passait encore : les deux tombaient, et la manche allait au
        // « survivant », mort lui aussi, toujours le même. Le taser vérifiait déjà son attaquant.
        if (ownHealth != null && ownHealth.IsDead)
        {
#if UNITY_EDITOR
            diagRejectedDead++;
            DiagReportIfDue();
#endif
            return;
        }

        // Une valeur invalide (NaN, infini) ne vient que d'un client modifié. Refusée ici, avant les
        // comparaisons : avec NaN, elles sont toutes fausses, et l'origine passait le test de distance.
        if (!IsFinite(action.origin) || !IsFinite(action.direction) || !IsFinite(action.viewServerTime)) return;

        // L'arme du tir est celle que le serveur sait en main : la place annoncée ne sert qu'à
        // repérer un désaccord (client modifié, ou changement d'arme refusé).
        if (action.slot != magazine.equipped)
        {
            Debug.LogWarning($"[Serveur] Tir rejeté : le joueur annonce l'arme {action.slot + 1}, le serveur lui sait la {magazine.equipped + 1} en main.", this);
#if UNITY_EDITOR
            diagRejectedAmmo++;
            DiagReportIfDue();
#endif
            return;
        }

        // Garde-fou anti-triche : cooldown/HandleFireInput ne sont que des CONVENTIONS côté
        // client — rien n'empêche un client modifié d'appeler la RPC aussi vite qu'il veut. Le
        // serveur doit donc lui-même refuser tout tir arrivant plus vite que shotsPerSecond ne
        // l'autorise, sans quoi la cadence de tir n'est jamais réellement protégée malgré tout le
        // soin apporté au hit registration ci-dessous. Voir TryAcceptShotRate pour la tolérance à
        // la gigue. Le temps est celui de l'ARRIVÉE.
        float minInterval = 1f / Mathf.Max(0.01f, data.shotsPerSecond);
        if (!TryAcceptShotRate(ref serverFireCredit, ref serverLastFireCheck, action.realTime, minInterval))
        {
#if UNITY_EDITOR
            diagRejectedRate++;
            DiagReportIfDue();
#endif
            return;
        }

        // Garde-fou anti-triche : hors manche, aucun tir n'est accepté. Sans ce test serveur, un
        // client modifié tirerait pendant le décompte ou après la mort de son adversaire — le test
        // dans HandleFireInput n'est qu'une convention côté client.
        if (!RoundManager.FiringAllowedFor(true)) // la mort du tireur est refusée plus haut
        {
#if UNITY_EDITOR
            diagRejectedPhase++;
            DiagReportIfDue();
#endif
            return;
        }

        // Garde-fou anti-triche : pas de tir en sortant d'un sprint (2026-10-11). Le temps se compte
        // sur les inputs jusqu'à l'input de référence, exactement ceux que le tireur avait envoyés
        // quand il a tiré (PlayerLocomotion.ServerTimeSinceSprintAt) : un tir honnête n'est jamais
        // refusé.
        float timeSinceSprint = ServerTimeSinceSprint(action);
        if (locomotion != null && timeSinceSprint < data.sprintToFireTime - SprintOutTolerance)
        {
            Debug.LogWarning($"[Serveur] Tir rejeté : sortie de sprint ({timeSinceSprint * 1000f:F0} ms sur {data.sprintToFireTime * 1000f:F0}).", this);
            return;
        }

        // Garde-fou anti-triche : pas de tir en franchissant un obstacle (2026-10-11). L'état qui
        // compte est celui qui suit le dernier input reçu avant le tir, que la file a attendu.
        if (locomotion != null && locomotion.ServerInputFlags(action.inputSequence, out bool vaulting, out _) && vaulting)
        {
            Debug.LogWarning("[Serveur] Tir rejeté : le joueur franchissait un obstacle.", this);
#if UNITY_EDITOR
            diagRejectedVault++;
            DiagReportIfDue();
#endif
            return;
        }

        // Garde-fou anti-triche : pas de tir à vide, pendant un rechargement ou avant que l'arme
        // soit sortie. Le chargeur prédit du client n'est qu'une convention ; c'est ce compte-ci
        // qui fait foi, à l'instant d'arrivée.
        magazine = networkMagazine.Value;
        int firedSlot = DataSlot(magazine.equipped);
        bool roundConsumed = TryConsumeRound(ref magazine, action.serverTime, MagazineSizeOf(firedSlot), data.reloadPerRound,
                                             ReloadDurationOf(firedSlot), ReloadCompletionTolerance);
        networkMagazine.Value = magazine; // même refusé : le tir a pu terminer un rechargement
        if (!roundConsumed)
        {
#if UNITY_EDITOR
            diagRejectedAmmo++;
            DiagReportIfDue();
#endif
            return;
        }

        // Garde-fou anti-triche : une direction nulle (ou non fournie) ferait un Raycast dans une
        // direction indéfinie — .normalized d'un vecteur nul renvoie déjà Vector3.zero sans lever
        // d'exception, mais autant refuser explicitement plutôt que de raycaster pour rien.
        Vector3 origin = action.origin;
        Vector3 direction = action.direction;
        if (direction.sqrMagnitude < 0.0001f) return;
        direction = direction.normalized;

        // Garde-fou anti-triche : `origin` est fourni par le CLIENT. Sans cette vérification, un
        // client modifié peut raycaster depuis N'IMPORTE QUEL point de la carte — derrière
        // l'adversaire, de l'autre côté d'un mur, depuis le spawn d'en face — tout en respectant
        // parfaitement la cadence de tir.
        //
        // On compare à la racine du joueur côté SERVEUR (aux pieds), pas à la caméra : le
        // cameraPivot n'est mis à jour que sur l'instance du propriétaire (catégorie C), donc sa
        // position est périmée côté serveur pour un client distant et ne peut pas servir de
        // référence. D'où une tolérance en distance plutôt qu'une égalité — elle doit couvrir :
        //   - la hauteur caméra debout (~1,7 m au-dessus des pieds),
        //   - le décalage latéral du lean (~0,4 m),
        //   - et surtout l'AVANCE de prédiction du tireur : son client simule en avance sur le
        //     serveur, donc sa caméra est légitimement devant la position que le serveur lui
        //     connaît, d'autant plus que le ping est élevé (~1,2 m à 6 m/s et 200 ms de RTT).
        // La valeur par défaut est donc volontairement large : l'objectif est de rendre impossible
        // le tir "depuis ailleurs", pas de chipoter sur quelques dizaines de centimètres. Pour la
        // resserrer, il faudrait comparer l'origine à la pose PASSÉE du tireur, que l'historique
        // du rewind connaît déjà mais que cette validation n'utilise pas encore.
        if ((origin - transform.root.position).sqrMagnitude > maxOriginDistanceFromPlayer * maxOriginDistanceFromPlayer)
        {
            Debug.LogWarning($"[Serveur] Tir rejeté : origine invalide (à {Vector3.Distance(origin, transform.root.position):F1} m du joueur, max {maxOriginDistanceFromPlayer} m). Soit un client modifié, soit la tolérance est trop serrée pour la latence testée.", this);
#if UNITY_EDITOR
            diagRejectedOrigin++;
            DiagReportIfDue();
#endif
            return;
        }

        // La gerbe se resserre en visée : la visée du serveur, celle de l'input de référence, jamais
        // ce que le client prétend.
        bool aiming = locomotion != null && locomotion.ServerInputFlags(action.inputSequence, out _, out bool aimHeld) && aimHeld;

        // Le tireur ne doit pas se tirer dessus : son propre hitbox entoure sa caméra, et en lean
        // celle-ci peut carrément en sortir, ce qui mettrait son propre collider en travers du
        // rayon. On le retire de la requête le temps du tir. Le try/finally garantit qu'il revient
        // même si le raycast ou l'application des dégâts lève — un hitbox laissé désactivé rendrait
        // le tireur invulnérable pour le reste de la partie. Le rewind ci-dessous suit le même
        // patron pour les hitbox des autres joueurs. Les TROIS zones sont retirées : la tête
        // entoure la caméra, donc le rayon en part toujours.
        PlayerHitbox ownHitbox = locomotion != null ? locomotion.Hitbox : null;
        bool hitboxWasEnabled = ownHitbox != null && ownHitbox.IsQueryable;
        if (hitboxWasEnabled) ownHitbox.SetQueryable(false);

        // Garde-fou anti-triche : la suggestion du client est bornée. maxRewindSeconds doit couvrir
        // un ping élevé légitime sans permettre de toucher quelqu'un là où il était il y a une
        // éternité — au-delà, c'est la VICTIME qui subirait l'injustice, en mourant à couvert.
        // Un instant dans le futur est ramené au présent. Borné depuis l'ARRIVÉE du tir : l'attente
        // dans la file n'entame pas la marge.
        float clampedRewind = Mathf.Clamp((float)(action.serverTime - action.viewServerTime), 0f, maxRewindSeconds);
        double targetTime = action.serverTime - clampedRewind;

#if UNITY_EDITOR
        diagAccepted++;
        diagRewindSum += clampedRewind;
        if (clampedRewind > diagRewindMax) diagRewindMax = clampedRewind;
        DiagReportIfDue();
#endif

        var rewoundPlayers = new List<PlayerLocomotion>();
#if UNITY_EDITOR
        bool diagHitWithRewind = false;
        Vector3 diagPastCenter = Vector3.zero;
#endif
        try
        {
            if (NetworkManager.ServerTime.Time - targetTime > 1e-4)
            {
                foreach (PlayerLocomotion other in PlayerLocomotion.SpawnedPlayers)
                {
                    if (other == null || other == locomotion) continue;
                    other.ServerBeginRewind(targetTime);
                    rewoundPlayers.Add(other);
                }

                // Indispensable : Physics.autoSyncTransforms vaut false par défaut depuis Unity
                // 2018, donc déplacer un transform ne met PAS à jour la scène physique utilisée par
                // les requêtes. Sans cette synchronisation, le raycast ci-dessous verrait encore les
                // hitbox à leur position actuelle et tout le rewind serait silencieusement sans
                // effet — le pire des échecs, parce qu'il ne se voit pas.
                Physics.SyncTransforms();

#if UNITY_EDITOR
                // Centre du hitbox à sa position PASSÉE, capturé pendant que le rewind est appliqué.
                foreach (PlayerLocomotion other in rewoundPlayers)
                {
                    Collider c = other != null && other.Hitbox != null ? other.Hitbox.TorsoCollider : null;
                    if (c != null) diagPastCenter = c.bounds.center;
                }
#endif
            }

            // Chaque plomb (ou la balle) est résolu seul ; les dégâts s'additionnent par cible et
            // tombent en une fois.
            int count = data.PelletCount;
            var ends = new Vector3[count];
            var normals = new Vector3[count];
            int hitMask = 0;
            int bodyMask = 0; // les plombs arrêtés par un robot (le son de l'impact, 2026-10-13)
            var damage = new Dictionary<Health, float>();
            var firstHitPoint = new Dictionary<Health, Vector3>();
            bool headHit = false;
            bool killed = false;
            var zones = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                Vector3 pellet = PelletDirection(direction, data.PelletOffset(i, aiming));
                bool hitSomething = ResolveShot(data, origin, pellet, out ends[i], out normals[i], out Collider bodyHit, out float distance);
                if (hitSomething && i < 31) hitMask |= 1 << i;
                if (bodyHit != null && i < 31) bodyMask |= 1 << i;
#if UNITY_EDITOR
                if (i == 0) diagHitWithRewind = bodyHit != null;
#endif
                if (bodyHit == null) continue;
                Health target = bodyHit.GetComponentInParent<Health>();
                if (target == null)
                {
                    // Un collider sur le layer Hitbox sans Health au-dessus : configuration
                    // incohérente, silencieuse autrement, et qui se traduirait par des tirs qui
                    // "ne font rien" sans la moindre trace.
                    Debug.LogWarning($"[Serveur] {bodyHit.name} est sur le layer Hitbox mais n'a aucun Health parent — aucun dégât appliqué.", bodyHit);
                    continue;
                }
                AddDamage(damage, data, bodyHit, distance);
                if (!firstHitPoint.ContainsKey(target)) firstHitPoint[target] = ends[i];
                PlayerHitboxZone zone = bodyHit.GetComponent<PlayerHitboxZone>();
                if (zone != null && zone.Zone == HitboxZoneType.Head) headHit = true;
                zones.Append(zone != null ? zone.Zone.ToString() : "?").Append(' ');
            }

            float dealt = 0f;
            foreach (var pair in damage)
            {
                float before = pair.Key.Current;
                pair.Key.ApplyDamage(pair.Value, transform.root.gameObject);
                dealt += Mathf.Max(0f, before - pair.Key.Current);
#if UNITY_EDITOR
                diagHits++;
#endif
                Debug.Log($"[Serveur] {data.weaponName} : {pair.Key.transform.root.name} touché ({zones.ToString().TrimEnd()}) pour {pair.Value:F0} dégâts (vie restante : {pair.Key.Current}).");

                // Le coup qui tue : l'épave part dans le sens du tir, d'autant plus fort que le tir
                // était lourd (PlayerDeath, cosmétique).
                if (before > 0f && pair.Key.IsDead) killed = true;
                if (before > 0f && pair.Key.IsDead && pair.Key.TryGetComponent(out PlayerDeath death))
                {
                    Vector3 point = firstHitPoint.TryGetValue(pair.Key, out Vector3 p) ? p : pair.Key.transform.position + Vector3.up;
                    death.ServerNotifyKilled(point, direction * DeathImpulse(pair.Value));
                }
            }

            // Statistiques du match (GDD § 14), comptées ici, par le serveur, et seulement pendant la
            // manche : ni l'échauffement, ni les tirs sur l'épave de l'adversaire. Un tir de pompe
            // compte pour un tir, touché si un plomb a touché.
            if (matchState != null && RoundManager.StatsRecording) matchState.ServerRecordShot(damage.Count > 0, headHit, dealt);

            // Le retour de touche (GDD § 6) : décidé ici, entendu par le tireur seul.
            if (damage.Count > 0) ServerConfirmHit(ClassifyHit(headHit, killed));

            BroadcastShotClientRpc(origin, ends, normals, hitMask, bodyMask, magazine.equipped);
        }
        finally
        {
            // Restauration garantie même si le raycast ou l'application des dégâts lève : un hitbox
            // laissé dans le passé rendrait ce joueur touchable au mauvais endroit pour le reste de
            // la partie, et un hitbox de tireur laissé désactivé le rendrait invulnérable.
            foreach (PlayerLocomotion other in rewoundPlayers)
            {
                if (other != null) other.ServerEndRewind();
            }
            if (rewoundPlayers.Count > 0) Physics.SyncTransforms();

#if UNITY_EDITOR
            // MESURE DIFFÉRENTIELLE DU REWIND (Editor uniquement, sans aucun effet sur le jeu).
            //
            // Les hitbox viennent d'être remis à leur position ACTUELLE : on refait donc exactement
            // la même trace (la balle, ou le plomb central), et on compare au résultat rewind. Ça
            // répond sans ambiguïté à « à quoi sert le rewind », là où comparer des taux de touche
            // entre deux sessions humaines ne répond à rien — un joueur vise instinctivement devant
            // une cible mobile et compense donc l'absence de compensation, ce qui masque l'effet
            // qu'on cherche à mesurer.
            //
            // Faite AVANT de réactiver le hitbox du tireur, pour que les deux traces excluent les
            // mêmes colliders et restent comparables.
            if (rewoundPlayers.Count > 0)
            {
                ResolveShot(data, origin, PelletDirection(direction, data.PelletOffset(0, aiming)), out _, out _, out Collider bodyHitLive, out _);
                bool hitWithoutRewind = bodyHitLive != null;

                if (diagHitWithRewind && !hitWithoutRewind) diagRewindGained++;
                else if (!diagHitWithRewind && hitWithoutRewind) diagRewindLost++;
                else if (diagHitWithRewind) diagBothHit++;
                else diagNeitherHit++;

                // Distance perpendiculaire du rayon au centre de la cible, passée vs actuelle.
                // `direction` est normalisée, donc la norme du produit vectoriel donne
                // directement cette distance.
                foreach (PlayerLocomotion other in rewoundPlayers)
                {
                    Collider c = other != null && other.Hitbox != null ? other.Hitbox.TorsoCollider : null;
                    if (c == null) continue;

                    float dPast = Vector3.Cross(direction, diagPastCenter - origin).magnitude;
                    float dNow = Vector3.Cross(direction, c.bounds.center - origin).magnitude;

                    diagDistPastSum += dPast;
                    diagDistNowSum += dNow;
                    diagDistSamples++;
                }
            }
#endif

            if (hitboxWasEnabled) ownHitbox.SetQueryable(true);
        }
    }

    /// <summary>Ajoute les dégâts d'un impact à sa cible : zone touchée (tête, buste, jambes) et
    /// distance (WeaponData.DamageAt).</summary>
    private static void AddDamage(Dictionary<Health, float> damage, WeaponData data, Collider body, float distance)
    {
        if (body == null) return;
        Health target = body.GetComponentInParent<Health>();
        if (target == null) return;
        PlayerHitboxZone zone = body.GetComponent<PlayerHitboxZone>();
        float amount = data.DamageAt(zone != null ? zone.Zone : HitboxZoneType.Torso, distance);
        damage[target] = (damage.TryGetValue(target, out float sum) ? sum : 0f) + amount;
    }

    // ------------------------------------------------------------------
    // Chargeur
    // ------------------------------------------------------------------

    private void HandleReloadInput()
    {
        WeaponData data = LocalData;
        bool wantsReload = input.ReloadPressedThisFrame;
#if UNITY_EDITOR
        // Le banc de tir automatique garde la gâchette enfoncée : il recharge à vide.
        if (PlayerInputReader.AutopilotFire && localMagazine.ammo == 0) wantsReload = true;
        if (input.DrillReloadPressed) wantsReload = true;
#endif
        if (wantsReload) reloadPending = true;
        if (!reloadPending) return;
        if (ownHealth != null && ownHealth.IsDead)
        {
            reloadPending = false;
            return;
        }
        int reloadSlot = DataSlot(localMagazine.equipped);
        if (localMagazine.reloadEndsAt > 0.0 || localMagazine.ammo >= MagazineSizeOf(reloadSlot))
        {
            reloadPending = false;
            return;
        }
        // On ne commence pas un rechargement en sortant d'un sprint, en franchissant un obstacle
        // (2026-10-11) ou avant que l'arme soit sortie : la demande attend, et part dès que c'est
        // possible.
        if (!ReadyAfterSprint || (locomotion != null && locomotion.IsVaulting) || MagazineClock < localMagazine.readyAt) return;
        reloadPending = false;

        // Prédit : le rechargement commence tout de suite chez le tireur (il l'entend à la fin de
        // l'image, HearLocalMagazine), et le serveur le commencera à réception, à l'instant que
        // MagazineClock annonce.
        TryStartReload(ref localMagazine, MagazineClock, MagazineSizeOf(reloadSlot), ReloadDurationOf(reloadSlot));
#if UNITY_EDITOR
        Debug.Log($"[DIAG-CHARGEUR] rechargement à {localMagazine.ammo} balles ({data.weaponName})" +
                  (locomotion != null && locomotion.TimeSinceSprint < 1f ? $", {locomotion.TimeSinceSprint * 1000f:F0} ms après le sprint" : ""));
#endif

        if (IsNetworked)
        {
            actionsSent++;
            ReloadServerRpc(ReferenceInputForRequest());
        }
    }

    private void UpdateLocalMagazine()
    {
        WeaponData data = LocalData;
        int slot = DataSlot(localMagazine.equipped);
        CompleteReloadIfDue(ref localMagazine, MagazineClock, MagazineSizeOf(slot), data.reloadPerRound, ReloadDurationOf(slot));

        if (!IsNetworked) return;
#if UNITY_EDITOR
        int predicted = localMagazine.ammo;
        int predictedSlot = localMagazine.equipped;
#endif
        ReconcileMagazine(networkMagazine.Value, actionsSent, ref localMagazine);
#if UNITY_EDITOR
        // Un recalage hors début de manche signale un tir refusé par le serveur : la prédiction et
        // le serveur n'étaient pas d'accord. Doit rester rare.
        if (localMagazine.ammo != predicted || localMagazine.equipped != predictedSlot)
            Debug.Log($"[DIAG-CHARGEUR] recalé de {predicted} à {localMagazine.ammo} balles" +
                      (localMagazine.equipped != predictedSlot ? $", arme {predictedSlot + 1} → {localMagazine.equipped + 1}" : ""));

        // État complet toutes les 3 s, chez un client distant seulement (comme [DIAG-CLIENT]) :
        // c'est lui qui a révélé une gâchette automatique restée éteinte sur le clone.
        if (!IsServer && (Time.time >= diagMagazineNextReportAt || diagMagazineNextReportAt - Time.time > 3f))
        {
            diagMagazineNextReportAt = Time.time + 3f;
            MagazineState s = networkMagazine.Value;
            Debug.Log($"[DIAG-CHARGEUR] arme={localMagazine.equipped + 1} predit={localMagazine.ammo} recharge={localMagazine.reloadEndsAt > 0.0} envoyees={actionsSent} | " +
                      $"serveur: arme={s.equipped + 1} balles={s.ammo} traitees={s.actionsProcessed} fin_recharge={s.reloadEndsAt:F2} | " +
                      $"gachette={input.FireHeld} tir_permis={RoundManager.FiringAllowedFor(ownHealth == null || !ownHealth.IsDead)} sprint={locomotion.IsSprinting} cooldown={cooldown:F2}");
        }
#endif
    }

#if UNITY_EDITOR
    private float diagMagazineNextReportAt;
#endif

    /// <summary>Refusé sans bruit s'il recharge déjà ou si le chargeur est plein ; l'accusé de
    /// réception, lui, compte toujours.</summary>
    private void ServerReload(ArmsAction action)
    {
        MagazineState magazine = networkMagazine.Value;
        magazine.actionsProcessed++;
        WeaponData data = ServerData;
        if (IsSpectatorRole)
        {
            networkMagazine.Value = magazine;
            return;
        }
        // On ne commence pas un rechargement en sortant d'un sprint ni en franchissant un obstacle
        // (2026-10-11). Le joueur honnête attend ; la demande d'un client modifié est refusée, et
        // son chargeur se recale.
        float timeSinceSprint = ServerTimeSinceSprint(action);
        if (locomotion != null && data != null && timeSinceSprint < data.sprintToFireTime - SprintOutTolerance)
        {
            networkMagazine.Value = magazine;
            Debug.LogWarning($"[Serveur] Rechargement refusé : sortie de sprint ({timeSinceSprint * 1000f:F0} ms sur {data.sprintToFireTime * 1000f:F0}).", this);
            return;
        }
        if (locomotion != null && locomotion.ServerInputFlags(action.inputSequence, out bool vaulting, out _) && vaulting)
        {
            networkMagazine.Value = magazine;
            Debug.LogWarning("[Serveur] Rechargement refusé : le joueur franchissait un obstacle.", this);
            return;
        }
        if (data != null && action.serverTime + ReloadCompletionTolerance >= magazine.readyAt
            && TryStartReload(ref magazine, action.serverTime, MagazineSizeOf(DataSlot(magazine.equipped)),
                              ReloadDurationOf(DataSlot(magazine.equipped))))
        {
            serverReloadFirstInput = action.inputSequence + 1;
        }
        networkMagazine.Value = magazine;
    }

    /// <summary>Changement d'arme : la nouvelle arme sort en WeaponData.raiseTime, le rechargement en
    /// cours est annulé (les cartouches déjà glissées restent).</summary>
    private void ServerSwitch(ArmsAction action)
    {
        MagazineState magazine = networkMagazine.Value;
        magazine.actionsProcessed++;
        if (action.refused)
        {
            // Compté quand même : c'est ce qui laisse le joueur se recaler sur l'arme qu'il a en main.
            networkMagazine.Value = magazine;
            Debug.LogWarning($"[Serveur] Changement d'arme refusé : l'arme se choisit avant la manche.", this);
            return;
        }
        WeaponData next = DataOf(action.slot);
        WeaponData current = ServerData;
        if (next != null && current != null)
        {
            int currentSlot = DataSlot(magazine.equipped);
            CompleteReloadIfDue(ref magazine, action.serverTime + ReloadCompletionTolerance, MagazineSizeOf(currentSlot),
                                current.reloadPerRound, ReloadDurationOf(currentSlot));
            if (TrySwitchWeapon(ref magazine, action.slot, action.serverTime, next.raiseTime))
                ResetShotRateOnSwitch(ref serverLastFireCheck);
        }
        else
        {
            Debug.LogWarning($"[Serveur] Changement d'arme refusé : place {action.slot + 1} vide.", this);
        }
        networkMagazine.Value = magazine;
    }

    /// <summary>Serveur : premier input du joueur produit après sa demande de rechargement. Un
    /// sprint dans l'un d'eux annule le rechargement (PlayerLocomotion.GateSprint) ; ceux d'avant,
    /// même traités plus tard, ne comptent pas.</summary>
    private int serverReloadFirstInput = int.MaxValue;

    private void ServerUpdateMagazine()
    {
        MagazineState magazine = networkMagazine.Value;
        WeaponData data = ServerData;
        if (data == null) return;
        int slot = DataSlot(magazine.equipped);
        if (CompleteReloadIfDue(ref magazine, NetworkManager.ServerTime.Time, MagazineSizeOf(slot), data.reloadPerRound,
                                ReloadDurationOf(slot)))
        {
            networkMagazine.Value = magazine;
            if (magazine.reloadEndsAt <= 0.0) return;
        }

        // On ne recharge jamais en courant (2026-10-10). Le joueur honnête a déjà annulé chez lui
        // en se mettant à courir ; un client modifié qui sprinterait sans annuler perd ici son
        // rechargement, sans rien y gagner.
        if (magazine.reloadEndsAt > 0.0 && locomotion != null && locomotion.ServerSprintedSince(serverReloadFirstInput))
        {
            ServerCancelReload();
#if UNITY_EDITOR
            Debug.Log("[DIAG-CHARGEUR] rechargement annulé par le sprint (serveur)");
#endif
        }
        // On ne recharge pas en franchissant un obstacle (2026-10-11) : un franchissement après la
        // demande annule le rechargement, comme le sprint.
        else if (magazine.reloadEndsAt > 0.0 && locomotion != null && locomotion.ServerVaultedSince(serverReloadFirstInput))
        {
            ServerCancelReload();
#if UNITY_EDITOR
            Debug.Log("[DIAG-CHARGEUR] rechargement annulé par le franchissement (serveur)");
#endif
        }
    }

    /// <summary>Tous les chargeurs pleins et rechargement annulé. Serveur uniquement : à
    /// l'apparition, et au début de chaque manche (RoundManager.BeginRound) — on ne reprend pas une
    /// manche avec les chargeurs de la précédente. L'arme en main ne change pas.</summary>
    public void ServerRefillMagazine()
    {
        if (!IsServer) return;

        MagazineState magazine = networkMagazine.Value;
        RefillAll(ref magazine);
        networkMagazine.Value = magazine;
    }

    private void RefillAll(ref MagazineState magazine)
    {
        for (int slot = 0; slot < SlotCount; slot++)
        {
            if (loadout[slot] != null) magazine.SetAmmo(slot, ClampAmmo(MagazineSizeOf(slot)));
        }
        magazine.reloadEndsAt = 0.0;
        magazine.readyAt = 0.0;
    }

    /// <summary>Le taser annule le rechargement en cours, comme le couteau de Call of Duty, et se
    /// mettre à courir ou franchir un obstacle aussi. Chez le joueur, tout de suite ; le serveur
    /// fera de même à réception, et le recalage du chargeur rattrape l'écart d'ici là. Les
    /// cartouches déjà glissées restent.</summary>
    public void CancelLocalReload()
    {
        WeaponData data = LocalData;
        if (data != null)
        {
            int slot = DataSlot(localMagazine.equipped);
            CompleteReloadIfDue(ref localMagazine, MagazineClock, MagazineSizeOf(slot), data.reloadPerRound, ReloadDurationOf(slot));
        }
        localMagazine.reloadEndsAt = 0.0;
        reloadPending = false;
    }

    /// <summary>Annulation côté serveur. Un rechargement à moins de ReloadCompletionTolerance de sa
    /// fin est considéré comme fini, avec la même marge que pour un tir : le joueur l'a vu se
    /// terminer avant d'appuyer, il ne doit pas perdre son chargeur.</summary>
    public void ServerCancelReload()
    {
        if (!IsServer) return;
        MagazineState magazine = networkMagazine.Value;
        WeaponData data = ServerData;
        if (magazine.reloadEndsAt <= 0.0 || data == null) return;
        int slot = DataSlot(magazine.equipped);
        CompleteReloadIfDue(ref magazine, NetworkManager.ServerTime.Time + ReloadCompletionTolerance, MagazineSizeOf(slot),
                            data.reloadPerRound, ReloadDurationOf(slot));
        magazine.reloadEndsAt = 0.0;
        networkMagazine.Value = magazine;
    }

    private void HandleMagazineChanged(MagazineState previous, MagazineState current)
    {
        // Le rechargement s'entend : l'adversaire sait qu'on ne peut plus tirer (catégorie B,
        // décidée par le serveur), et qu'on le peut de nouveau quand il se tait. Le propriétaire
        // l'entend sur sa prédiction (HearLocalMagazine).
        if (!IsOwner && weaponAudio != null) weaponAudio.Hear(previous, current, DataOf(previous.equipped), DataOf(current.equipped));
    }

    private static byte ClampAmmo(int ammo) => (byte)Mathf.Clamp(ammo, 0, 255);

    /// <summary>Réserve maximale du crédit de tir, en intervalles de tir. Avec le seuil ci-dessous,
    /// un tir retardé jusqu'à 0,65 intervalle (93 ms au MP5) ne fait pas refuser le suivant, arrivé
    /// à l'heure ; en échange, un client modifié gagne au plus 0,65 intervalle au début d'une
    /// rafale, jamais en continu.</summary>
    public const double FireCreditCapacity = 1.5;

    /// <summary>Part d'un intervalle de crédit qu'il faut avoir pour tirer : le crédit peut
    /// passer légèrement sous zéro, ce qui garde la cadence MOYENNE exacte.</summary>
    public const double FireCreditThreshold = 0.85;

    /// <summary>
    /// Garde-fou de cadence côté serveur, en temps réel (2026-10-08). Fonction pure, testée.
    ///
    /// Chaque tir coûte un intervalle de crédit ; le crédit se recharge avec le temps qui passe,
    /// jusqu'à `FireCreditCapacity` intervalles. La cadence moyenne ne peut donc jamais dépasser
    /// celle de l'arme, mais deux tirs légitimes qui ARRIVENT rapprochés passent : le premier,
    /// retardé par la gigue du réseau ou un à-coup du serveur, a laissé du crédit au second.
    ///
    /// L'ancienne règle exigeait 85 % de l'intervalle entre deux arrivées : un à-coup de plus de
    /// 21 ms suffisait à refuser un tir légitime (environ un sur cent à deux instances, mesuré le
    /// 2026-10-08), et elle laissait un client modifié tirer en continu 17 % trop vite.
    /// </summary>
    /// <summary>
    /// Changer d'arme remet le crédit de cadence à plein (2026-10-11), comme le joueur remet son
    /// délai entre deux tirs à zéro : c'est la sortie de l'arme (readyAt) qui borne son premier tir.
    /// Sinon le crédit de l'arme précédente restait : mesuré à deux instances, le premier tir du
    /// sniper (intervalle 1,25 s) parti 0,8 s après une rafale de fusil, à la fin de sa sortie, était
    /// refusé. Changer d'arme coûte au moins sa sortie (0,4 s et plus) : on n'en tire pas une cadence
    /// plus rapide.
    /// </summary>
    public static void ResetShotRateOnSwitch(ref double lastCheck) => lastCheck = double.NegativeInfinity;

    /// <summary>Aucune composante NaN ni infinie. Ce qu'un client envoie passe par là avant toute
    /// comparaison : avec NaN, elles sont toutes fausses, donc un garde-fou écrit « refuser si trop
    /// loin » laissait passer (relecture du 2026-10-12). Sans état, testée.</summary>
    public static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    public static bool IsFinite(Vector2 v) => IsFinite(v.x) && IsFinite(v.y);
    public static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);

    public static bool TryAcceptShotRate(ref double credit, ref double lastCheck, double now, float interval)
    {
        double elapsed = now - lastCheck; // +infini au premier tir : crédit plein
        credit = System.Math.Min(interval * FireCreditCapacity, credit + System.Math.Max(0.0, elapsed));
        lastCheck = now;
        if (credit < interval * FireCreditThreshold) return false;
        credit -= interval;
        return true;
    }

    /// <summary>Termine le rechargement en cours si son heure est passée. Fonction pure, testée.</summary>
    public static bool CompleteReloadIfDue(ref MagazineState magazine, double now, int magazineSize)
        => CompleteReloadIfDue(ref magazine, now, magazineSize, false, 0f);

    /// <summary>
    /// Termine le rechargement en cours si son heure est passée — ou, cartouche par cartouche,
    /// glisse toutes celles dont l'heure est passée, jusqu'au plein. Fonction pure, testée.
    /// </summary>
    public static bool CompleteReloadIfDue(ref MagazineState magazine, double now, int magazineSize, bool perRound, float roundTime)
    {
        if (magazine.reloadEndsAt <= 0.0 || now < magazine.reloadEndsAt) return false;
        if (!perRound)
        {
            magazine.ammo = ClampAmmo(magazineSize);
            magazine.reloadEndsAt = 0.0;
            return true;
        }
        int ammo = magazine.ammo;
        while (magazine.reloadEndsAt > 0.0 && now >= magazine.reloadEndsAt)
        {
            ammo = Mathf.Min(ammo + 1, magazineSize);
            magazine.reloadEndsAt = ammo >= magazineSize ? 0.0 : magazine.reloadEndsAt + Mathf.Max(0.01f, roundTime);
        }
        magazine.ammo = ClampAmmo(ammo);
        return true;
    }

    /// <summary>Commence un rechargement, sauf s'il y en a déjà un ou si le chargeur est plein.
    /// `duration` : le rechargement complet, ou la première cartouche. Fonction pure, testée.</summary>
    public static bool TryStartReload(ref MagazineState magazine, double now, int magazineSize, float duration)
    {
        if (magazine.reloadEndsAt > 0.0 || magazine.ammo >= magazineSize) return false;
        magazine.reloadEndsAt = now + Mathf.Max(0.01f, duration);
        return true;
    }

    /// <summary>Consomme une balle pour un tir arrivé à `now`. Refusé à vide ou pendant un
    /// rechargement, sauf dans les derniers `ReloadCompletionTolerance` d'un rechargement, qui se
    /// termine alors un peu en avance. Fonction pure, testée.</summary>
    public static bool TryConsumeRound(ref MagazineState magazine, double now, int magazineSize)
        => TryConsumeRound(ref magazine, now, magazineSize, false, 0f, ReloadCompletionTolerance);

    /// <summary>
    /// Consomme une balle pour un tir arrivé à `now`, avec `tolerance` de marge (celle du serveur,
    /// ou 0 chez le tireur). Refusé à vide, avant que l'arme soit sortie, et pendant un rechargement
    /// complet. Cartouche par cartouche (pompe), tirer interrompt le rechargement et tire avec ce
    /// qui est chargé, comme dans Call of Duty et Rainbow Six. Fonction pure, testée.
    /// </summary>
    public static bool TryConsumeRound(ref MagazineState magazine, double now, int magazineSize, bool perRound, float roundTime, double tolerance)
    {
        CompleteReloadIfDue(ref magazine, now + tolerance, magazineSize, perRound, roundTime);
        if (now + tolerance < magazine.readyAt) return false;
        if (magazine.reloadEndsAt > 0.0)
        {
            if (!perRound || magazine.ammo == 0) return false;
            magazine.reloadEndsAt = 0.0;
        }
        if (magazine.ammo == 0) return false;
        magazine.ammo--;
        return true;
    }

    /// <summary>Prend l'arme de la place `slot` : elle sort en `raiseTime` à partir de `now`, et le
    /// rechargement en cours est abandonné. Refusé pour l'arme déjà en main ou une place hors de la
    /// panoplie. Fonction pure, testée.</summary>
    public static bool TrySwitchWeapon(ref MagazineState magazine, int slot, double now, float raiseTime)
    {
        if (slot < 0 || slot >= MaxSlots || slot == magazine.equipped) return false;
        magazine.equipped = (byte)slot;
        magazine.reloadEndsAt = 0.0;
        magazine.readyAt = now + Mathf.Max(0f, raiseTime);
        return true;
    }

    /// <summary>
    /// Recale le chargeur prédit sur celui du serveur, quand c'est possible. Fonction pure, testée.
    ///
    /// Tant qu'une demande est en route, l'état publié est en retard sur la prédiction : le
    /// comparer serait aussi faux que de comparer une position prédite à une position confirmée
    /// d'une autre séquence (voir « Comparer une prédiction, c'est comparer à séquence ÉGALE »).
    /// Une fois tout accusé, le serveur fait foi — sauf s'il recharge encore : sa fin arrive, et
    /// recopier son chargeur vide ferait clignoter le compte d'un tireur qui a déjà fini.
    ///
    /// Ce qui se recale ainsi : un tir refusé par le serveur (cadence, hors manche), un
    /// rechargement refusé ou annulé par un début de manche, une fin de rechargement arrivée
    /// quelques millisecondes plus tôt sur le serveur, un changement d'arme refusé.
    /// </summary>
    public static void ReconcileMagazine(MagazineState server, ushort actionsSent, ref MagazineState local)
    {
        if (server.actionsProcessed != actionsSent) return;
        if (server.reloadEndsAt > 0.0) return;
        local = server;
    }

    /// <summary>Même recalage, réduit au chargeur de l'arme en main (tests du chargeur seul).</summary>
    public static void ReconcileMagazine(MagazineState server, ushort actionsSent, ref int localAmmo, ref bool localReloading)
    {
        if (server.actionsProcessed != actionsSent) return;
        if (server.reloadEndsAt > 0.0) return;
        localReloading = false;
        localAmmo = server.ammo;
    }

    /// <summary>
    /// Résout un tir en DEUX traces distinctes, au lieu d'un raycast unique contre « tout ».
    ///
    /// 1. Le MONDE, qui arrête les balles. Volontairement SANS les joueurs : leur
    ///    `CharacterController` ne suit pas le lean, donc le laisser bloquer les tirs ferait
    ///    réapparaître par la bande le trou que le hitbox vient de fermer — un joueur penché
    ///    arrêterait des balles avec un collider resté droit.
    /// 2. Les HITBOX, limitée à la distance du mur touché : c'est ce qui fait qu'un adversaire
    ///    derrière une caisse ne prend rien.
    ///
    /// Les hitbox sont des triggers, pour ne perturber aucune collision physique — d'où
    /// `QueryTriggerInteraction.Collide` sur la seconde trace seulement.
    ///
    /// Partagée entre le feedback visuel local du tireur et la décision serveur : une seule
    /// logique, donc pas de dérive possible entre ce qu'on voit et ce qui compte.
    /// </summary>
    private static bool ResolveShot(WeaponData data, Vector3 origin, Vector3 direction, out Vector3 end, out Vector3 normal,
                                    out Collider bodyHit, out float distance)
    {
        bool worldHit = Physics.Raycast(origin, direction, out RaycastHit world, data.maxRange, data.worldMask, QueryTriggerInteraction.Ignore);
        float blockingDistance = worldHit ? world.distance : data.maxRange;

        bool playerHit = Physics.Raycast(origin, direction, out RaycastHit body, blockingDistance, data.hitboxMask, QueryTriggerInteraction.Collide);

        bodyHit = playerHit ? body.collider : null;
        end = playerHit ? body.point : (worldHit ? world.point : origin + direction * data.maxRange);
        normal = playerHit ? body.normal : (worldHit ? world.normal : Vector3.zero);
        distance = playerHit ? body.distance : blockingDistance;
        return worldHit || playerHit;
    }

    /// <summary>Rediffuse à tout le monde (sauf au tireur, déjà servi localement dans Fire()) les
    /// traçants, impacts et le son de tir, tels que DÉCIDÉS PAR LE SERVEUR dans ServerFire : un
    /// point d'arrivée par plomb (ou par balle), un bit par plomb qui a touché quelque chose, et un
    /// bit par plomb arrêté par un robot (le son de l'impact n'est pas le même, 2026-10-13).</summary>
    [ClientRpc]
    private void BroadcastShotClientRpc(Vector3 origin, Vector3[] ends, Vector3[] normals, int hitMask, int bodyMask, byte slot)
    {
        if (IsOwner) return; // déjà joué/affiché localement par le tireur, dans Fire()

        OnShotFired?.Invoke();

        WeaponData data = DataOf(slot) ?? Data;
        PlayFireSound(data);

        if (ends == null) return;

        var impacts = new ShotImpactSounds(data);
        for (int i = 0; i < ends.Length; i++)
        {
            Vector3 end = ends[i];
            bool hit = i < 31 && (hitMask & (1 << i)) != 0 && normals != null && i < normals.Length;
            bool onBody = i < 31 && (bodyMask & (1 << i)) != 0;
            Vector3 normal = hit ? normals[i] : Vector3.zero;
            // L'épave de cette machine (elle ne tombe pas pareil partout) arrête le tir à l'écran.
            if (data != null && RobotCorpse.TryHit(origin, end, CorpseImpulse(data), out Vector3 wreckPoint, out Vector3 wreckNormal))
            {
                end = wreckPoint;
                normal = wreckNormal;
                hit = true;
                onBody = true;
            }
            if (visualFeedback != null)
            {
                visualFeedback.SpawnTracer(origin, end, data != null ? data.tracerColor : Color.yellow);
                if (hit) visualFeedback.SpawnImpact(end, normal);
            }
            if (hit) impacts.Add(end, onBody);
        }

        PlayFlyby(data, origin, ends, bodyMask);
    }

    // ------------------------------------------------------------------
    // La balle qui siffle (2026-10-13)
    // ------------------------------------------------------------------

    /// <summary>Distance (m) à l'oreille sous laquelle une balle qui passe s'entend siffler.</summary>
    public const float FlybyRadius = 2.5f;

    /// <summary>Une balle tirée par un autre qui passe près de ce joueur sans le toucher siffle, là où
    /// elle passe au plus près : on sait qu'on est visé, et de quel côté. Une seule par tir.</summary>
    private static void PlayFlyby(WeaponData data, Vector3 origin, Vector3[] ends, int bodyMask)
    {
        if (data == null || data.flybySounds == null || data.flybySounds.IsEmpty) return;
        AudioListener ear = OneShotAudio.ActiveListener;
        if (ear == null) return;
        if (TryPickFlyby(origin, ends, bodyMask, ear.transform.position, FlybyRadius, out Vector3 point))
            data.flybySounds.PlayAt(point, 1f, 12f);
    }

    /// <summary>
    /// Le sifflement d'un tir : parmi ses balles (ou ses plombs) qu'aucun robot n'a arrêtées, celle qui
    /// passe au plus près de l'oreille. Une balle arrêtée par un robot s'entend en impact : de près, une
    /// touche aux jambes passe sinon à plus d'un mètre de l'oreille sans s'y arrêter, et la victime
    /// l'entendrait siffler. Fonction pure, testée.
    /// </summary>
    public static bool TryPickFlyby(Vector3 origin, Vector3[] ends, int bodyMask, Vector3 ear, float radius, out Vector3 point)
    {
        point = Vector3.zero;
        if (ends == null) return false;
        bool found = false;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < ends.Length; i++)
        {
            if (i < 31 && (bodyMask & (1 << i)) != 0) continue;
            if (!TryFlybyPoint(origin, ends[i], ear, radius, out Vector3 candidate)) continue;
            float distance = Vector3.Distance(candidate, ear);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            point = candidate;
            found = true;
        }
        return found;
    }

    /// <summary>
    /// Une balle d'`origin` à `end` passe-t-elle à moins de `radius` de l'oreille, sans s'y arrêter ?
    /// `point` : là où elle en passe au plus près. Une balle qui finit sa course contre soi (on est
    /// touché) ou qui s'arrête avant d'arriver à hauteur de l'oreille ne siffle pas. Fonction pure,
    /// testée.
    /// </summary>
    public static bool TryFlybyPoint(Vector3 origin, Vector3 end, Vector3 ear, float radius, out Vector3 point)
    {
        point = origin;
        Vector3 path = end - origin;
        float length = path.magnitude;
        if (length < 1e-3f) return false;
        Vector3 along = path / length;
        float t = Vector3.Dot(ear - origin, along);
        if (t <= 0f || t >= length) return false; // arrêtée avant d'arriver à hauteur de l'oreille
        if ((end - ear).sqrMagnitude < 1f) return false; // finie contre soi : on est touché, pas frôlé
        point = origin + along * t;
        return (point - ear).sqrMagnitude < radius * radius;
    }

    // ------------------------------------------------------------------
    // Retour de touche (2026-10-13, GDD § 6 : un son de touche, un autre pour la tête)
    // ------------------------------------------------------------------

    public enum HitKind : byte
    {
        Body,
        Head,
        Kill,
    }

    /// <summary>Chez le tireur : une touche confirmée par le serveur (PlayerFeedbackAudio la fait
    /// entendre ; un marqueur à l'écran pourra s'y accrocher).</summary>
    public event System.Action<HitKind> OnHitConfirmed;

    /// <summary>Ce qu'un tir qui a touché fait entendre au tireur : le coup qui tue d'abord, la tête
    /// ensuite. Fonction pure, testée.</summary>
    public static HitKind ClassifyHit(bool head, bool killed) => killed ? HitKind.Kill : head ? HitKind.Head : HitKind.Body;

    /// <summary>Serveur : prévient le tireur, et lui seul, qu'il a touché. Aussi appelé par le taser.</summary>
    public void ServerConfirmHit(HitKind kind)
    {
        if (!IsServer || !IsSpawned) return;
        HitConfirmedClientRpc(kind, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        });
    }

    [ClientRpc]
    private void HitConfirmedClientRpc(HitKind kind, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;
        OnHitConfirmed?.Invoke(kind);
    }

    /// <summary>Ce que pousse une balle (ou un plomb) sur une épave, en N·s : de quoi la faire
    /// sursauter, un peu plus au sniper. Ressenti seulement. Réduit le 2026-10-12 (« violent » au
    /// pompe, retour de l'utilisateur), avec la répartition de RobotCorpse.Push.</summary>
    public static float CorpseImpulse(WeaponData data) => Mathf.Clamp(data.damagePerHit * 0.3f, 3f, 20f);

    /// <summary>Ce que pousse le coup mortel sur l'épave (N·s), selon les dégâts du tir : plus au
    /// sniper et au pompe qu'au fusil d'assaut. Réduit le 2026-10-12 (70 au plus avant ; « violent »
    /// au pompe, retour de l'utilisateur) : l'épave recule d'un pas, elle ne s'envole pas.</summary>
    public static float DeathImpulse(float damage) => Mathf.Clamp(damage * 0.35f, 14f, 45f);

    private void ApplyRecoilKick(WeaponData data)
    {
        float recoilMultiplier = GetRecoilMultiplier();
        // les accessoires (GDD § 6) : la poignée verticale retient le recul vertical, le laser tout le
        // recul quand on tire sans viser
        AttachmentCatalog.RecoilFactors(data.attachmentCatalog, Attachments, DataSlot(localMagazine.equipped), locomotion.IsAiming,
                                        out float verticalFactor, out float horizontalFactor);
        float verticalKick = data.verticalRecoilPattern.Evaluate(shotIndexInBurst) * data.recoilPerShotDegrees * recoilMultiplier
                             * verticalFactor;
        float horizontalKick = data.horizontalRecoilPattern.Evaluate(shotIndexInBurst) * data.recoilPerShotDegrees * recoilMultiplier
                               * horizontalFactor;

        cameraLook.AddInstantRotation(verticalKick, horizontalKick);

        verticalRecoilAccumulated += verticalKick;
        horizontalRecoilAccumulated += horizontalKick;
    }

    /// <summary>Pourcentage (0-1) du recul de base à appliquer, selon la posture actuelle du
    /// joueur et son état de déplacement (statique / sneak / marche-course) au moment du tir,
    /// puis encore réduit si le joueur vise (ADS) — comme le sneak, viser stabilise l'arme quelle
    /// que soit la posture/le déplacement en cours, donc appliqué en dernier par-dessus le reste.</summary>
    private float GetRecoilMultiplier()
    {
        float percent;
        if (!locomotion.IsMoving)
        {
            percent = locomotion.CurrentStance switch
            {
                PlayerLocomotion.Stance.Crouching => recoilPercentCrouchingStatic,
                PlayerLocomotion.Stance.Prone => recoilPercentProneStatic,
                _ => recoilPercentStandingStatic,
            };
        }
        else if (locomotion.IsSneaking)
        {
            percent = locomotion.CurrentStance switch
            {
                PlayerLocomotion.Stance.Crouching => recoilPercentCrouchingSneaking,
                PlayerLocomotion.Stance.Prone => recoilPercentProneSneaking,
                _ => recoilPercentStandingSneaking,
            };
        }
        else
        {
            percent = locomotion.CurrentStance switch
            {
                PlayerLocomotion.Stance.Crouching => recoilPercentCrouchingMoving,
                PlayerLocomotion.Stance.Prone => recoilPercentProneMoving,
                _ => recoilPercentStandingMoving,
            };
        }

        if (locomotion.IsAiming)
        {
            percent = percent * recoilPercentWhileAiming / 100f;
        }

        return percent / 100f;
    }

    private void ApplyRecoilRecovery(WeaponData data)
    {
        float step = data.recoilRecoverySpeed * Time.deltaTime;

        float verticalRecovery = Mathf.Min(step, verticalRecoilAccumulated);
        if (verticalRecovery > 0f)
        {
            cameraLook.AddInstantRotation(-verticalRecovery, 0f);
            verticalRecoilAccumulated -= verticalRecovery;
        }

        float horizontalMagnitude = Mathf.Abs(horizontalRecoilAccumulated);
        float horizontalRecovery = Mathf.Min(step, horizontalMagnitude) * Mathf.Sign(horizontalRecoilAccumulated);
        if (Mathf.Abs(horizontalRecovery) > 0f)
        {
            cameraLook.AddInstantRotation(0f, -horizontalRecovery);
            horizontalRecoilAccumulated -= horizontalRecovery;
        }
    }
}
