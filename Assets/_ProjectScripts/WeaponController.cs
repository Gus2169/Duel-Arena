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
/// LIMITE CONNUE, pas un oubli : pas encore de compensation de latence (rewind) — le serveur
/// valide contre la position ACTUELLE des adversaires, donc légèrement en retard sur ce que le
/// tireur voyait, d'autant plus sous forte latence. Voir le TODO détaillé sur FireServerRpc.
/// </summary>
[RequireComponent(typeof(Transform))]
public class WeaponController : NetworkBehaviour
{
    [SerializeField] private WeaponData data;
    [SerializeField] private Camera aimCamera;
    [SerializeField] private PlayerCameraLook cameraLook;
    [SerializeField] private WeaponVisualFeedback visualFeedback;
    [SerializeField] private AudioSource audioSource;

    [Header("Recul selon la posture / le mouvement")]
    [Tooltip("Multiplicateurs (%) appliqués au recul (data.recoilPerShotDegrees) selon la posture et si le joueur se déplace ou non. Vit ici plutôt que dans WeaponData : ça s'applique automatiquement à n'importe quelle arme équipée, sans réglage à dupliquer par arme. Le déplacement en sneak (Ctrl/molette maintenu) est plus stable que la marche normale, quelle que soit la posture, sans être aussi stable qu'à l'arrêt complet.")]
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
    [Tooltip("Distance max (m) tolérée entre l'origine de tir annoncée par le client et la position du joueur connue du SERVEUR. Au-delà, le tir est rejeté : c'est ce qui empêche un client modifié de tirer depuis n'importe où sur la carte. Doit couvrir la hauteur caméra + le lean + l'avance de prédiction du tireur sous latence — voir le détail dans FireServerRpc. Si tu vois des tirs légitimes rejetés en Console pendant un test à fort ping, augmente cette valeur plutôt que de retirer le garde-fou.")]
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
#endif

    private PlayerInputReader input;
    private PlayerLocomotion locomotion;

    private float cooldown;
    private float timeSinceLastShot;
    private int shotIndexInBurst;

    // Garde-fou anti-triche (voir FireServerRpc) : dernier tir ACCEPTÉ par le serveur, pour
    // faire respecter data.shotsPerSecond indépendamment de ce que le client prétend faire.
    private float serverLastAcceptedFireTime = -Mathf.Infinity;

    private float verticalRecoilAccumulated;
    private float horizontalRecoilAccumulated;

#if UNITY_EDITOR
    // Diagnostics de session (Editor uniquement). Comptés côté SERVEUR, seul endroit qui décide.
    // Ce qu'on cherche : des tirs rejetés qui ne devraient pas l'être (garde-fous trop serrés) et
    // la part réelle du rewind, invisibles autrement.
    private static int diagAccepted, diagRejectedRate, diagRejectedOrigin, diagRejectedPhase, diagHits;
    private static float diagRewindSum, diagRewindMax;
    private static float diagNextReportAt;

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
        if (Time.time < diagNextReportAt) return;
        diagNextReportAt = Time.time + 3f;

        int total = diagAccepted + diagRejectedRate + diagRejectedOrigin + diagRejectedPhase;
        if (total == 0) return;

        float avgRewind = diagAccepted > 0 ? diagRewindSum / diagAccepted : 0f;
        int compared = diagRewindGained + diagRewindLost + diagBothHit + diagNeitherHit;

        Debug.Log($"[DIAG-SERVEUR] tirs={total} acceptes={diagAccepted} touches={diagHits} " +
                  $"rejets(cadence={diagRejectedRate} origine={diagRejectedOrigin} hors_manche={diagRejectedPhase}) " +
                  $"rewind_moy={avgRewind * 1000f:F0}ms rewind_max={diagRewindMax * 1000f:F0}ms | " +
                  $"DIFFERENTIEL sur {compared} tirs : gagnes_par_rewind={diagRewindGained} " +
                  $"perdus_par_rewind={diagRewindLost} touches_dans_les_2_cas={diagBothHit} " +
                  $"rates_dans_les_2_cas={diagNeitherHit}" +
                  (diagDistSamples > 0
                      ? $" | VISEE : distance_au_corps_PASSE={diagDistPastSum / diagDistSamples:F2}m " +
                        $"distance_au_corps_ACTUEL={diagDistNowSum / diagDistSamples:F2}m"
                      : ""));

        diagAccepted = diagRejectedRate = diagRejectedOrigin = diagRejectedPhase = diagHits = 0;
        diagRewindSum = diagRewindMax = 0f;
        diagRewindGained = diagRewindLost = diagBothHit = diagNeitherHit = 0;
        diagDistPastSum = diagDistNowSum = 0f;
        diagDistSamples = 0;
    }
#endif

    private void Awake()
    {
        input = GetComponentInParent<PlayerInputReader>();
        locomotion = GetComponentInParent<PlayerLocomotion>();

        if (input == null || locomotion == null)
        {
            Debug.LogError("WeaponController doit être un enfant du Player (PlayerInputReader/PlayerLocomotion introuvables).", this);
        }

        if (aimCamera == null || visualFeedback == null)
        {
            Debug.LogError("WeaponController : assigne 'Aim Camera' et 'Visual Feedback' dans l'inspecteur.", this);
        }
    }

    private void Update()
    {
        // Maintenant que PlayerLocomotion est réseauté, il y a potentiellement plusieurs
        // instances de Player dans la scène en même temps. PlayerInputReader lit le clavier/la
        // souris PHYSIQUE de la machine sans savoir à qui elle appartient — sans ce garde-fou,
        // CHAQUE copie de WeaponController (y compris celle des autres joueurs affichés sur ton
        // écran) tirerait à chaque clic. On ne traite l'input que pour SON PROPRE joueur.
        // (NetworkObject == null : composant testé hors contexte réseau, on laisse passer.)
        if (NetworkObject != null && !IsOwner) return;

        timeSinceLastShot += Time.deltaTime;
        if (timeSinceLastShot > data.recoilResetDelay)
        {
            shotIndexInBurst = 0;
        }

        cooldown -= Time.deltaTime;
        ApplyRecoilRecovery();
        HandleFireInput();
    }

    private void HandleFireInput()
    {
        bool wantsToFire = data.isAutomatic ? input.FireHeld : input.FirePressedThisFrame;

        // Le tir n'est possible qu'en déplacement normal/sneak/accroupi/allongé (lean ou non),
        // jamais en sprint — PlayerLocomotion coupe déjà le sprint dès que Fire est appuyé, donc
        // ce garde-fou ne bloque en pratique que la frame où IsSprinting n'a pas encore été
        // remise à jour ce tick (WeaponController tourne avant PlayerLocomotion.Update()).
        // Confort local : on ne déclenche même pas le recul ni le son hors manche. Le vrai
        // verrou est côté serveur dans FireServerRpc — celui-ci n'est qu'une convention client.
        if (wantsToFire && cooldown <= 0f && !locomotion.IsSprinting && RoundManager.FiringAllowed)
        {
            // Le cooldown doit être posé AVANT Fire() : si Fire() lève une exception (ex.
            // référence non assignée dans l'inspecteur), on ne veut surtout pas rester avec
            // cooldown <= 0, sinon HandleFireInput retire au tir (et donc au recul) à chaque
            // frame suivante au lieu de respecter shotsPerSecond.
            cooldown = 1f / Mathf.Max(0.01f, data.shotsPerSecond);
            Fire();
        }
    }

    private void Fire()
    {
        timeSinceLastShot = 0f;
        OnShotFired?.Invoke();
        PlayFireSound();

        // IMPORTANT : on lit origin/direction AVANT d'appliquer le recul de CE tir. Sinon
        // (comme c'était le cas avant ce fix), la caméra a déjà tourné au moment où on lit
        // aimCamera.transform.forward, et le tir atterrit décalé par son propre recul au lieu
        // de partir exactement là où le crosshair pointait au moment de la pression. C'est ce
        // qui expliquait le décalage variable observé selon la posture (quasi nul en prone,
        // léger en crouch, marqué debout) : l'ampleur suit exactement GetRecoilMultiplier().
        Vector3 origin = aimCamera.transform.position;
        Vector3 direction = aimCamera.transform.forward;

        // Raycast LOCAL : ne sert qu'au feedback visuel instantané du tireur (tracer/impact), pas
        // aux dégâts ni à ce qui est envoyé au serveur — voir FireServerRpc. Il utilise la MÊME
        // résolution que le serveur (ResolveShot) pour que ce que le tireur voit corresponde à ce
        // que le serveur décide, aux différences de latence près.
        bool hitSomething = ResolveShot(origin, direction, out Vector3 end, out Vector3 localNormal, out _);

        Debug.DrawLine(origin, end, hitSomething ? Color.red : Color.gray, 0.5f);
        if (visualFeedback != null)
        {
            visualFeedback.SpawnTracer(origin, end);
            if (hitSomething) visualFeedback.SpawnImpact(end, localNormal);
        }

        if (NetworkObject != null)
        {
            // Contexte réseau normal : seuls origin/direction partent au serveur, qui refait SEUL
            // son propre raycast et décide SEUL du résultat (voir FireServerRpc) — un client modifié
            // ne peut donc plus mentir sur ce qu'il prétend avoir touché.
            Vector3 sentOrigin = origin;
#if UNITY_EDITOR
            // Triche simulée : on ment au serveur sur l'origine du tir, exactement comme le ferait
            // un client modifié. Le feedback visuel local ci-dessus reste, lui, honnête — on voit
            // donc bien à l'écran que le tir part d'où il devrait, alors que le serveur le rejette.
            if (debugFakeShotOrigin) sentOrigin = origin + direction * debugFakeShotOriginOffset;
#endif
            // Le tireur annonce de combien il estime que sa vue est en retard. Le serveur clampe
            // cette valeur : c'est une suggestion, pas une donnée de confiance.
            FireServerRpc(sentOrigin, direction, locomotion.EstimatedRewindSeconds);
        }
        else if (hitSomething)
        {
            // Composant testé hors contexte réseau (pas de NetworkObject) : pas de serveur à qui
            // déléguer, on applique les dégâts directement en local comme avant le passage réseau.
            ResolveShot(origin, direction, out _, out _, out Collider localBody);
            Health targetHealth = localBody != null ? localBody.GetComponentInParent<Health>() : null;
            if (targetHealth != null) targetHealth.ApplyDamage(data.damagePerHit, transform.root.gameObject);
        }

        // Le recul de CE tir est appliqué maintenant, après avoir déterminé où il a atterri —
        // il influence donc la visée du PROCHAIN tir (comportement voulu pour une rafale
        // progressive), jamais celui qui vient d'être tiré.
        ApplyRecoilKick();

        shotIndexInBurst++;
    }

    /// <summary>Joue le son de tir de l'arme équipée (data.fireSounds). Appelé localement par le
    /// tireur dans Fire(), et rediffusé aux autres clients par BroadcastShotClientRpc — sinon,
    /// comme pour le visuel avant l'ajout du ClientRpc, seul le tireur entendrait ses propres tirs.</summary>
    /// <summary>Leve a chaque tir VU depuis cette machine : chez le tireur depuis Fire(), chez
    /// tous les autres depuis BroadcastShotClientRpc. Purement cosmetique — il sert a declencher
    /// l'animation de tir, qui doit etre visible sur l'adversaire comme sur soi.
    ///
    /// Le tir etait deja diffuse a tout le monde pour le son et le tracer : aucun reseau
    /// supplementaire n'a ete necessaire, seulement un point d'accroche.</summary>
    public event System.Action OnShotFired;

    private void PlayFireSound()
    {
        if (audioSource == null || data.fireSounds == null || data.fireSounds.Length == 0) return;

        AudioClip clip = data.fireSounds[Random.Range(0, data.fireSounds.Length)];
        audioSource.pitch = Random.Range(data.firePitchRange.x, data.firePitchRange.y);
        audioSource.PlayOneShot(clip, data.fireVolume);
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
    /// `rewindSeconds` est une SUGGESTION du client (voir PlayerLocomotion.EstimatedRewindSeconds),
    /// jamais une donnée de confiance : le serveur la clampe. Sans ce clamp, un client modifié
    /// annoncerait un ping énorme pour rewind ses adversaires très loin en arrière et les toucher
    /// là où ils n'ont plus été depuis longtemps. C'est le seul paramètre "libre" accepté par cette
    /// RPC — tout le reste (hit, dégâts, position des cibles) est recalculé ici.
    ///
    /// Le rewind ne déplace QUE les hitbox, jamais les joueurs eux-mêmes : la simulation en cours
    /// continue sur les vraies positions, et tout se déroule de façon synchrone dans ce handler,
    /// donc invisible pour le reste du jeu.
    /// </summary>
    [ServerRpc]
    private void FireServerRpc(Vector3 origin, Vector3 direction, float rewindSeconds)
    {
        // Garde-fou anti-triche : cooldown/HandleFireInput ne sont que des CONVENTIONS côté
        // client — rien n'empêche un client modifié d'appeler cette RPC aussi vite qu'il veut.
        // Le serveur doit donc lui-même refuser tout tir arrivant plus vite que shotsPerSecond
        // ne l'autorise, sans quoi la cadence de tir n'est jamais réellement protégée malgré
        // tout le soin apporté au hit registration ci-dessous. Tolérance de 15% pour absorber
        // la gigue réseau/frame sans pénaliser un tir légitime tombé pile à la limite.
        float minInterval = 1f / Mathf.Max(0.01f, data.shotsPerSecond);
        float now = Time.time;
        if (now - serverLastAcceptedFireTime < minInterval * 0.85f)
        {
#if UNITY_EDITOR
            diagRejectedRate++;
            DiagReportIfDue();
#endif
            return;
        }
        serverLastAcceptedFireTime = now;

        // Garde-fou anti-triche : hors manche, aucun tir n'est accepté. Sans ce test serveur, un
        // client modifié tirerait pendant le décompte ou après la mort de son adversaire — le test
        // dans HandleFireInput n'est qu'une convention côté client.
        if (!RoundManager.FiringAllowed)
        {
#if UNITY_EDITOR
            diagRejectedPhase++;
            DiagReportIfDue();
#endif
            return;
        }

        // Garde-fou anti-triche : une direction nulle (ou non fournie) ferait un Raycast dans une
        // direction indéfinie — .normalized d'un vecteur nul renvoie déjà Vector3.zero sans lever
        // d'exception, mais autant refuser explicitement plutôt que de raycaster pour rien.
        if (direction.sqrMagnitude < 0.0001f) return;
        direction = direction.normalized;

        // Garde-fou anti-triche : `origin` est fourni par le CLIENT et servait jusqu'ici tel quel.
        // Sans cette vérification, un client modifié peut raycaster depuis N'IMPORTE QUEL point de
        // la carte — derrière l'adversaire, de l'autre côté d'un mur, depuis le spawn d'en face —
        // tout en respectant parfaitement la cadence de tir. C'était le trou le plus grave restant
        // dans le hit registration, plus grave que l'absence de rewind.
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
        // le tir "depuis ailleurs", pas de chipoter sur quelques dizaines de centimètres. Elle
        // pourra être resserrée une fois le rewind en place (le serveur saura alors où le tireur
        // se trouvait vraiment au moment du tir, et non seulement où il est maintenant).
        if ((origin - transform.root.position).sqrMagnitude > maxOriginDistanceFromPlayer * maxOriginDistanceFromPlayer)
        {
            Debug.LogWarning($"[Serveur] Tir rejeté : origine invalide (à {Vector3.Distance(origin, transform.root.position):F1} m du joueur, max {maxOriginDistanceFromPlayer} m). Soit un client modifié, soit la tolérance est trop serrée pour la latence testée.", this);
#if UNITY_EDITOR
            diagRejectedOrigin++;
            DiagReportIfDue();
#endif
            return;
        }

        // Le tireur ne doit pas se tirer dessus : son propre hitbox entoure sa caméra, et en lean
        // celle-ci peut carrément en sortir, ce qui mettrait son propre collider en travers du
        // rayon. On le retire de la requête le temps du tir. Le try/finally garantit qu'il revient
        // même si le raycast ou l'application des dégâts lève — un hitbox laissé désactivé rendrait
        // le tireur invulnérable pour le reste de la partie. C'est aussi le patron que réutilisera
        // le rewind, qui devra déplacer puis restaurer les hitbox des autres joueurs.
        Collider ownHitbox = locomotion != null ? locomotion.HitboxCollider : null;
        bool hitboxWasEnabled = ownHitbox != null && ownHitbox.enabled;
        if (hitboxWasEnabled) ownHitbox.enabled = false;

        // Garde-fou anti-triche : la suggestion du client est bornée. maxRewindSeconds doit couvrir
        // un ping élevé légitime sans permettre de toucher quelqu'un là où il était il y a une
        // éternité — au-delà, c'est la VICTIME qui subirait l'injustice, en mourant à couvert.
        float clampedRewind = Mathf.Clamp(rewindSeconds, 0f, maxRewindSeconds);

#if UNITY_EDITOR
        diagAccepted++;
        diagRewindSum += clampedRewind;
        if (clampedRewind > diagRewindMax) diagRewindMax = clampedRewind;
        DiagReportIfDue();
#endif
        float targetTime = Time.time - clampedRewind;

        var rewoundPlayers = new List<PlayerLocomotion>();
#if UNITY_EDITOR
        bool diagHitWithRewind = false;
        Vector3 diagPastCenter = Vector3.zero;
#endif
        try
        {
            if (clampedRewind > 0f)
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
                    Collider c = other != null ? other.HitboxCollider : null;
                    if (c != null) diagPastCenter = c.bounds.center;
                }
#endif
            }

            bool hitSomething = ResolveShot(origin, direction, out Vector3 end, out Vector3 hitNormal, out Collider bodyHit);
#if UNITY_EDITOR
            diagHitWithRewind = bodyHit != null;
#endif

            if (bodyHit != null)
            {
                Health targetHealth = bodyHit.GetComponentInParent<Health>();
                if (targetHealth != null)
                {
                    targetHealth.ApplyDamage(data.damagePerHit, transform.root.gameObject);
#if UNITY_EDITOR
                    diagHits++;
#endif
                    Debug.Log($"[Serveur] {data.weaponName} : {bodyHit.transform.root.name} touché pour {data.damagePerHit} dégâts (vie restante : {targetHealth.Current}).");
                }
                else
                {
                    // Un collider sur le layer Hitbox sans Health au-dessus : configuration
                    // incohérente, silencieuse autrement, et qui se traduirait par des tirs qui
                    // "ne font rien" sans la moindre trace.
                    Debug.LogWarning($"[Serveur] {bodyHit.name} est sur le layer Hitbox mais n'a aucun Health parent — aucun dégât appliqué.", bodyHit);
                }
            }

            BroadcastShotClientRpc(origin, end, hitSomething, end, hitNormal);
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
            // la même trace, et on compare au résultat rewind. Ça répond sans ambiguïté à « à quoi
            // sert le rewind », là où comparer des taux de touche entre deux sessions humaines ne
            // répond à rien — un joueur vise instinctivement devant une cible mobile et compense
            // donc l'absence de compensation, ce qui masque l'effet qu'on cherche à mesurer.
            //
            // Faite AVANT de réactiver le hitbox du tireur, pour que les deux traces excluent les
            // mêmes colliders et restent comparables.
            if (rewoundPlayers.Count > 0)
            {
                ResolveShot(origin, direction, out _, out _, out Collider bodyHitLive);
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
                    Collider c = other != null ? other.HitboxCollider : null;
                    if (c == null) continue;

                    float dPast = Vector3.Cross(direction, diagPastCenter - origin).magnitude;
                    float dNow = Vector3.Cross(direction, c.bounds.center - origin).magnitude;

                    diagDistPastSum += dPast;
                    diagDistNowSum += dNow;
                    diagDistSamples++;
                }
            }
#endif

            if (hitboxWasEnabled) ownHitbox.enabled = true;
        }
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
    private bool ResolveShot(Vector3 origin, Vector3 direction, out Vector3 end, out Vector3 normal, out Collider bodyHit)
    {
        bool worldHit = Physics.Raycast(origin, direction, out RaycastHit world, data.maxRange, data.worldMask, QueryTriggerInteraction.Ignore);
        float blockingDistance = worldHit ? world.distance : data.maxRange;

        bool playerHit = Physics.Raycast(origin, direction, out RaycastHit body, blockingDistance, data.hitboxMask, QueryTriggerInteraction.Collide);

        bodyHit = playerHit ? body.collider : null;
        end = playerHit ? body.point : (worldHit ? world.point : origin + direction * data.maxRange);
        normal = playerHit ? body.normal : (worldHit ? world.normal : Vector3.zero);
        return worldHit || playerHit;
    }

    /// <summary>Rediffuse à tout le monde (sauf au tireur, déjà servi localement dans Fire()) le
    /// tracer/impact + le son de tir, tels que DÉCIDÉS PAR LE SERVEUR dans FireServerRpc.</summary>
    [ClientRpc]
    private void BroadcastShotClientRpc(Vector3 origin, Vector3 end, bool hitSomething, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (IsOwner) return; // déjà joué/affiché localement par le tireur, dans Fire()

        OnShotFired?.Invoke();

        PlayFireSound();

        if (visualFeedback == null) return;

        visualFeedback.SpawnTracer(origin, end);
        if (hitSomething) visualFeedback.SpawnImpact(hitPoint, hitNormal);
    }

    private void ApplyRecoilKick()
    {
        float recoilMultiplier = GetRecoilMultiplier();
        float verticalKick = data.verticalRecoilPattern.Evaluate(shotIndexInBurst) * data.recoilPerShotDegrees * recoilMultiplier;
        float horizontalKick = data.horizontalRecoilPattern.Evaluate(shotIndexInBurst) * data.recoilPerShotDegrees * recoilMultiplier;

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

    private void ApplyRecoilRecovery()
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
