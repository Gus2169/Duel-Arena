using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Tir hitscan + recul déterministe. Pose ce script sur un GameObject "Weapon" enfant
/// du Player (peu importe sa position exacte pour l'instant, aucune arme visible encore).
///
/// À assigner dans l'inspecteur :
/// - data          : un asset WeaponData
/// - aimCamera     : la Camera sous LeanPivot (celle qui existe déjà pour le look)
/// - cameraLook    : le composant PlayerCameraLook posé sur CameraPivot
/// - visualFeedback: le composant WeaponVisualFeedback (pose-le sur le Player par exemple)
///
/// Ce script lit PlayerInputReader.FireHeld / FirePressedThisFrame — assure-toi d'avoir
/// ajouté l'action "Fire" dans PlayerControls.inputactions (voir PlayerInputReader.cs).
///
/// NetworkBehaviour (et non plus MonoBehaviour) depuis le fix multijoueur : Fire() ne s'exécute
/// que sur la machine du tireur (garde IsOwner ci-dessous, nécessaire pour éviter qu'une même
/// touche physique fasse tirer toutes les copies de WeaponController présentes sur l'écran).
///
/// HIT REGISTRATION SERVEUR (increment 1, SANS compensation de latence/rewind — voir TODO sur
/// FireServerRpc) : le tireur raycast localement pour son propre feedback visuel INSTANTANÉ
/// (tracer/impact, aucun dégât associé), puis envoie juste origin/direction au serveur, qui
/// refait SEUL son propre raycast et décide SEUL du hit et des dégâts (Health.ApplyDamage n'est
/// plus jamais appelée directement par un client sur un objet réseauté). Les autres joueurs
/// reçoivent ensuite le tracer/impact calculé par le serveur via BroadcastShotClientRpc — donc
/// potentiellement légèrement différent de ce que le tireur a vu localement sous latence, c'est
/// attendu pour cet incrément (le rewind, plus tard, réduira cet écart).
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

    private PlayerInputReader input;
    private PlayerLocomotion locomotion;

    private float cooldown;
    private float timeSinceLastShot;
    private int shotIndexInBurst;

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

        // Raycast LOCAL : ne sert plus qu'au feedback visuel instantané du tireur (tracer/impact),
        // plus du tout aux dégâts ni même à ce qui est envoyé au serveur — voir FireServerRpc.
        // Le feedback visuel sort volontairement du "if (targetHealth ...)" d'avant : on veut voir
        // où le tir a atterri même sur un mur/une caisse sans Health, sinon les tirs ratés
        // resteraient invisibles en jeu.
        bool hitSomething = Physics.Raycast(origin, direction, out RaycastHit localHit, data.maxRange, data.hittableMask);
        Vector3 end = hitSomething ? localHit.point : origin + direction * data.maxRange;

        Debug.DrawLine(origin, end, hitSomething ? Color.red : Color.gray, 0.5f);
        if (visualFeedback != null)
        {
            visualFeedback.SpawnTracer(origin, end);
            if (hitSomething) visualFeedback.SpawnImpact(localHit.point, localHit.normal);
        }

        if (NetworkObject != null)
        {
            // Contexte réseau normal : seuls origin/direction partent au serveur, qui refait SEUL
            // son propre raycast et décide SEUL du résultat (voir FireServerRpc) — un client modifié
            // ne peut donc plus mentir sur ce qu'il prétend avoir touché.
            FireServerRpc(origin, direction);
        }
        else if (hitSomething)
        {
            // Composant testé hors contexte réseau (pas de NetworkObject) : pas de serveur à qui
            // déléguer, on applique les dégâts directement en local comme avant le passage réseau.
            Health targetHealth = localHit.collider.GetComponentInParent<Health>();
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

    /// <summary>Autorité serveur du tir (increment 1, SANS compensation de latence/rewind) : re-raycast
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
        bool hitSomething = Physics.Raycast(origin, direction, out RaycastHit hit, data.maxRange, data.hittableMask);
        Vector3 end = hitSomething ? hit.point : origin + direction * data.maxRange;
        Vector3 hitNormal = hitSomething ? hit.normal : Vector3.zero;

        if (hitSomething)
        {
            Health targetHealth = hit.collider.GetComponentInParent<Health>();
            if (targetHealth != null)
            {
                targetHealth.ApplyDamage(data.damagePerHit, transform.root.gameObject);
                Debug.Log($"[Serveur] {data.weaponName} : {hit.collider.name} touché pour {data.damagePerHit} dégâts (vie restante : {targetHealth.Current}).");
            }
        }

        BroadcastShotClientRpc(origin, end, hitSomething, end, hitNormal);
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
