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
    [Tooltip("Distance max (m) tolérée entre l'origine de tir annoncée par le client et la position du joueur connue du SERVEUR. Au-delà, le tir est rejeté : c'est ce qui empêche un client modifié de tirer depuis n'importe où sur la carte. Doit couvrir la hauteur caméra + le lean + l'avance de prédiction du tireur sous latence — voir le détail dans FireServerRpc. À resserrer une fois le rewind en place. Si tu vois des tirs légitimes rejetés en Console pendant un test à fort ping, augmente cette valeur plutôt que de retirer le garde-fou.")]
    [SerializeField] private float maxOriginDistanceFromPlayer = 4f;

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
        if (wantsToFire && cooldown <= 0f && !locomotion.IsSprinting)
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
            FireServerRpc(sentOrigin, direction);
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
    private void PlayFireSound()
    {
        if (audioSource == null || data.fireSounds == null || data.fireSounds.Length == 0) return;

        AudioClip clip = data.fireSounds[Random.Range(0, data.fireSounds.Length)];
        audioSource.pitch = Random.Range(data.firePitchRange.x, data.firePitchRange.y);
        audioSource.PlayOneShot(clip, data.fireVolume);
    }

    /// <summary>Autorité serveur du tir (SANS compensation de latence/rewind) : re-raycast
    /// depuis origin/direction fournis par le tireur, contre l'état ACTUEL des colliders côté
    /// serveur au moment où cette RPC est traitée — donc légèrement "en retard" par rapport à ce que
    /// le tireur voyait sur son écran, d'autant plus que la latence est élevée. Compromis volontaire
    /// et temporaire.
    ///
    /// TODO prochain incrément (rewind) : avant de raycaster, replacer temporairement les AUTRES
    /// joueurs à la position qu'ils avaient au moment exact où LE TIREUR a appuyé sur la gâchette
    /// (nécessite un historique de position par joueur tenu par le serveur pour TOUS les joueurs,
    /// façon remoteSnapshots dans PlayerLocomotion, mais côté serveur plutôt que juste pour
    /// l'affichage spectateur), puis les remettre à leur position actuelle juste après le raycast.
    ///
    /// hitSomething/hitPoint/hitNormal ne sont PLUS reçus du client (ancienne version) : seuls
    /// origin/direction le sont, tout le reste est recalculé ici — un client modifié ne peut donc
    /// plus s'auto-déclarer un hit qu'il n'a pas réellement fait, ni forcer des dégâts sur une
    /// cible qu'il n'a pas visée.</summary>
    [ServerRpc]
    private void FireServerRpc(Vector3 origin, Vector3 direction)
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
            return;
        }
        serverLastAcceptedFireTime = now;

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

        try
        {
            bool hitSomething = ResolveShot(origin, direction, out Vector3 end, out Vector3 hitNormal, out Collider bodyHit);

            if (bodyHit != null)
            {
                Health targetHealth = bodyHit.GetComponentInParent<Health>();
                if (targetHealth != null)
                {
                    targetHealth.ApplyDamage(data.damagePerHit, transform.root.gameObject);
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
