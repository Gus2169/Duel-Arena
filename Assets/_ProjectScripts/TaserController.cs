using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Le taser : un coup bref au corps à corps, au choix du joueur, qui tue d'un coup (GDD § 6,
/// tranché le 2026-10-05 et précisé le 2026-10-08 : « comme un coup de couteau rapide dans Call of
/// Duty », et il ne sort plus tout seul quand l'arme est vide). Posé à côté de WeaponController.
///
/// Même autorité que le tir : le joueur demande un coup (origine, direction, instant affiché), et
/// le SERVEUR seul décide qui est touché, avec le même rewind et les mêmes garde-fous. Rien de ce
/// que le client envoie n'est un résultat.
///
/// Le coup part au CONTACT, `contactDelay` après l'appui — le moment où le bras est tendu dans
/// l'animation — et non à l'appui. C'est ce qui laisse une chance à la cible : un attaquant tué
/// pendant ces quelques centièmes ne frappe pas, et un coup qui tuerait sans le moindre geste
/// serait illisible. Le serveur résout donc le coup en différé, à l'instant du contact tel que le
/// joueur le voyait.
///
/// Pendant tout le geste, on ne tire pas, et un rechargement en cours est annulé (comme le couteau
/// de Call of Duty) — chez le joueur tout de suite, sur le serveur à réception. Frapper en
/// sprintant arrête le sprint, et on ne sprinte pas pendant le geste (PlayerLocomotion.GateSprint).
/// On ne frappe pas en franchissant un obstacle, et on ne franchit pas pendant le geste (2026-10-11).
/// </summary>
public class TaserController : NetworkBehaviour
{
    [Header("Le coup")]
    [Tooltip("Durée du geste complet (s). On ne tire pas pendant ce temps, et c'est aussi l'intervalle minimal entre deux coups. Réglage de ressenti et d'équilibrage.")]
    [SerializeField] private float strikeDuration = 0.6f;

    [Tooltip("De l'appui au contact (s) : le moment où le bras est tendu dans l'animation. Le coup est résolu à cet instant. Plus court, le taser devient un coup sans parade possible ; plus long, il devient lent.")]
    [SerializeField] private float contactDelay = 0.18f;

    [Tooltip("Portée depuis l'œil (m). Bras tendu plus un petit pas en avant.")]
    [SerializeField] private float range = 2.0f;

    [Tooltip("Rayon du volume balayé (m). Un coup au corps à corps n'exige pas la précision d'une balle.")]
    [SerializeField] private float radius = 0.3f;

    [Tooltip("Surfaces touchables des joueurs : le layer Hitbox, comme pour le tir.")]
    [SerializeField] private LayerMask hitboxMask = 1 << 7;

    [Tooltip("Géométrie qui arrête le coup (on ne frappe pas à travers un mur).")]
    [SerializeField] private LayerMask worldMask = 1;

    [Header("Son")]
    [Tooltip("Décharge du taser, entendue par tout le monde. Vide = silencieux.")]
    [SerializeField] private AudioClip strikeSound;
    [SerializeField, Range(0f, 1f)] private float strikeVolume = 0.9f;
    [SerializeField] private AudioSource audioSource;

    [Header("Garde-fous serveur (anti-triche)")]
    [Tooltip("Distance max (m) entre l'origine annoncée par le client et la position du joueur connue du serveur. Même borne que pour le tir.")]
    [SerializeField] private float maxOriginDistanceFromPlayer = 4f;

    [Tooltip("Rewind maximal (s) accordé au coup, comme pour le tir. Doit rester inférieur à l'historique des hitbox.")]
    [SerializeField] private float maxRewindSeconds = 0.3f;

#if UNITY_EDITOR
    /// <summary>COUP AUTOMATIQUE DE TEST — Editor uniquement. Frappe dès qu'un adversaire est à
    /// portée, en le visant au torse (comme le tir automatique du banc à deux instances).</summary>
    public static bool AutopilotStrike;
#endif

    private PlayerInputReader input;
    private PlayerLocomotion locomotion;
    private WeaponController weapon;
    private Health ownHealth;

    private float localStrikeEndsAt = -1f;

    // Côté serveur : dernier coup accepté (borne de débit en temps réel) et coup en attente de
    // son contact. Un seul à la fois : la borne de débit l'impose.
    private double serverLastStrikeAt = double.NegativeInfinity;
    private bool serverStrikePending;
    private double serverStrikeResolveAt;
    private Vector3 serverStrikeOriginOffset;
    private Vector3 serverStrikeDirection;
    private double serverStrikeViewTime;
    private int serverStrikeFirstInput = int.MaxValue; // premier input produit après l'appui

    private readonly Collider[] overlapBuffer = new Collider[16];

    public float StrikeDuration => strikeDuration;
    public float ContactDelay => contactDelay;

    /// <summary>Le joueur local est-il en plein geste ? (on ne tire pas pendant ce temps)</summary>
    public bool IsStriking => Time.time < localStrikeEndsAt;

    /// <summary>Levé à chaque coup VU depuis cette machine : chez le joueur à l'appui, chez les
    /// autres à la diffusion du serveur. Purement cosmétique (animation).</summary>
    public event Action OnStrike;

    private bool IsNetworked => NetworkObject != null && IsSpawned;

    private void Awake()
    {
        input = GetComponentInParent<PlayerInputReader>();
        locomotion = GetComponentInParent<PlayerLocomotion>();
        weapon = GetComponent<WeaponController>();
        ownHealth = GetComponentInParent<Health>();
    }

    private void Update()
    {
        // Le serveur résout les coups de TOUS les joueurs, donc avant la garde IsOwner.
        if (IsNetworked && IsServer && serverStrikePending && Time.timeAsDouble >= serverStrikeResolveAt)
        {
            serverStrikePending = false;
            ServerResolveStrike();
        }

        if (NetworkObject != null && !IsOwner) return;
        if (input == null) return;

        bool wantsStrike = input.MeleePressedThisFrame;
#if UNITY_EDITOR
        if (AutopilotStrike && !IsStriking && TryFindOpponentInReach(out _)) wantsStrike = true;
        if (input.DrillMeleePressed) wantsStrike = true;
#endif
        // On ne frappe pas en franchissant un obstacle (2026-10-11), comme on n'y tire pas.
        bool vaulting = locomotion != null && locomotion.IsVaulting;
#if UNITY_EDITOR
        if (PlayerLocomotion.DebugVaultIgnoresRules) vaulting = false; // client tricheur simulé
#endif
        if (wantsStrike && !IsStriking && RoundManager.FiringAllowed && (ownHealth == null || !ownHealth.IsDead) && !vaulting)
        {
            BeginStrike();
        }
    }

    private void BeginStrike()
    {
        localStrikeEndsAt = Time.time + strikeDuration;
        if (weapon != null) weapon.CancelLocalReload();
        OnStrike?.Invoke();
        PlayStrikeSound();

        Camera cam = weapon != null ? weapon.AimCamera : null;
        if (cam == null) return;
        Vector3 origin = cam.transform.position;
        Vector3 direction = cam.transform.forward;
#if UNITY_EDITOR
        if (AutopilotStrike && TryFindOpponentInReach(out Vector3 target)) direction = (target - origin).normalized;
#endif
        if (IsNetworked) StrikeServerRpc(origin, direction, locomotion.ViewServerTime);
    }

    /// <summary>
    /// Demande de coup. Le serveur la borne (débit en temps réel, phase de manche, origine), annule
    /// le rechargement du joueur, montre le geste aux autres, puis résout le coup au contact.
    /// </summary>
    [ServerRpc]
    private void StrikeServerRpc(Vector3 origin, Vector3 direction, double viewServerTime)
    {
        // Une valeur invalide (NaN, infini) ne vient que d'un client modifié : avec NaN, le test de
        // distance de l'origine ci-dessous serait toujours faux, donc passé (2026-10-12).
        if (!WeaponController.IsFinite(origin) || !WeaponController.IsFinite(direction)
            || !WeaponController.IsFinite(viewServerTime)) return;

        // Garde-fou : au plus un coup par geste, en temps RÉEL (règle des garde-fous). Tolérance de
        // 15 % pour la gigue : à 0,6 s d'intervalle, elle couvre 90 ms.
        double now = Time.timeAsDouble;
        if (now - serverLastStrikeAt < strikeDuration * 0.85) return;
        if (!RoundManager.FiringAllowed) return;
        if (ownHealth != null && ownHealth.IsDead) return;
        if (direction.sqrMagnitude < 0.0001f) return;

        Vector3 offset = origin - transform.root.position;
        if (offset.sqrMagnitude > maxOriginDistanceFromPlayer * maxOriginDistanceFromPlayer)
        {
            Debug.LogWarning($"[Serveur] Coup de taser rejeté : origine à {offset.magnitude:F1} m du joueur (max {maxOriginDistanceFromPlayer} m).", this);
            return;
        }
        serverLastStrikeAt = now;

        // Dans l'ordre des demandes du joueur : un rechargement demandé juste avant le coup est
        // commencé, puis annulé (WeaponController.ServerProcessArmsActions).
        if (weapon != null) weapon.ServerQueueCancelReload();
        serverStrikeFirstInput = locomotion != null ? locomotion.ServerNextInput : int.MaxValue;
        StrikeClientRpc();

        // L'origine est gardée RELATIVE au joueur : il continue de bouger jusqu'au contact, et son
        // bras le suit.
        serverStrikePending = true;
        serverStrikeResolveAt = now + contactDelay;
        serverStrikeOriginOffset = offset;
        serverStrikeDirection = direction.normalized;
        serverStrikeViewTime = viewServerTime + contactDelay;
    }

    [ClientRpc]
    private void StrikeClientRpc()
    {
        if (IsOwner) return; // déjà joué à l'appui
        OnStrike?.Invoke();
        PlayStrikeSound();
    }

    private void ServerResolveStrike()
    {
        // Un attaquant tué pendant le geste ne frappe pas : c'est tout l'intérêt du délai.
        if (ownHealth != null && ownHealth.IsDead) return;
        if (!RoundManager.FiringAllowed) return;

        // On ne frappe pas en courant (2026-10-10) : le joueur honnête cesse de sprinter dès
        // l'appui (PlayerLocomotion.GateSprint). Un client qui sprinterait pendant le geste ne
        // touche rien.
        if (locomotion != null && locomotion.ServerSprintedSince(serverStrikeFirstInput))
        {
            Debug.LogWarning("[Serveur] Coup de taser annulé : le joueur sprintait pendant le geste.", this);
            return;
        }

        // On ne frappe pas en franchissant un obstacle (2026-10-11) : ni s'il franchissait à l'appui
        // (l'input qui précède la demande), ni s'il s'est mis à franchir pendant le geste. Le joueur
        // honnête ne peut faire ni l'un ni l'autre (TaserController.Update, PlayerLocomotion.ReadLocalInput).
        if (locomotion != null && locomotion.ServerVaultedSince(serverStrikeFirstInput - 1))
        {
            Debug.LogWarning("[Serveur] Coup de taser annulé : le joueur franchissait un obstacle.", this);
            return;
        }

        Vector3 origin = transform.root.position + serverStrikeOriginOffset;
        double serverNow = NetworkManager.ServerTime.Time;
        float rewind = Mathf.Clamp((float)(serverNow - serverStrikeViewTime), 0f, maxRewindSeconds);
        double targetTime = serverNow - rewind;

        PlayerHitbox ownHitbox = locomotion != null ? locomotion.Hitbox : null;
        bool ownWasQueryable = ownHitbox != null && ownHitbox.IsQueryable;
        if (ownWasQueryable) ownHitbox.SetQueryable(false);

        var rewound = new List<PlayerLocomotion>();
        try
        {
            if (rewind > 0f)
            {
                foreach (PlayerLocomotion other in PlayerLocomotion.SpawnedPlayers)
                {
                    if (other == null || other == locomotion) continue;
                    other.ServerBeginRewind(targetTime);
                    rewound.Add(other);
                }
            }
            // Sans cette synchronisation, la requête verrait les hitbox à leur place actuelle et
            // le rewind serait silencieusement sans effet (voir le tir).
            Physics.SyncTransforms();

            Collider hit = QueryStrike(origin, serverStrikeDirection, range, radius, hitboxMask, worldMask, overlapBuffer);
            Health target = hit != null ? hit.GetComponentInParent<Health>() : null;
            if (target != null && target != ownHealth && !target.IsDead)
            {
                // Mort instantanée (GDD § 6) : quelle que soit la vie restante.
                target.ApplyDamage(float.PositiveInfinity, transform.root.gameObject);
                Debug.Log($"[Serveur] Taser : {hit.transform.root.name} neutralisé (rewind {rewind * 1000f:F0} ms).");
            }
        }
        finally
        {
            foreach (PlayerLocomotion other in rewound)
            {
                if (other != null) other.ServerEndRewind();
            }
            if (ownWasQueryable) ownHitbox.SetQueryable(true);
            Physics.SyncTransforms();
        }
    }

    /// <summary>
    /// Le corps touché par un coup donné depuis `origin` vers `direction`, ou null. Fonction sans
    /// état (seulement la scène physique), testée par de vrais colliders.
    ///
    /// Un volume balayé plutôt qu'un rayon : au corps à corps, viser au centimètre n'a pas de sens.
    /// Un chevauchement plutôt qu'un balayage (SphereCast) : un SphereCast ignore les colliders dans
    /// lesquels il commence, donc un adversaire collé à soi serait intouchable. Le plus proche
    /// l'emporte, et la ligne entre l'œil et le point touché ne doit traverser aucun mur.
    /// </summary>
    public static Collider QueryStrike(Vector3 origin, Vector3 direction, float range, float radius,
                                       LayerMask hitboxMask, LayerMask worldMask, Collider[] buffer)
    {
        direction = direction.normalized;
        int count = Physics.OverlapCapsuleNonAlloc(origin, origin + direction * range, radius, buffer,
                                                   hitboxMask, QueryTriggerInteraction.Collide);
        Collider best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider c = buffer[i];
            if (c == null || !c.enabled) continue;

            Vector3 point = c.ClosestPoint(origin);
            Vector3 toPoint = point - origin;
            if (Vector3.Dot(toPoint, direction) < -0.1f) continue; // derrière soi

            float distance = toPoint.magnitude;
            if (distance >= bestDistance) continue;
            if (distance > 0.001f && Physics.Linecast(origin, point, worldMask, QueryTriggerInteraction.Ignore)) continue;

            best = c;
            bestDistance = distance;
        }
        return best;
    }

    private void PlayStrikeSound()
    {
        if (audioSource == null || strikeSound == null) return;
        audioSource.pitch = 1f;
        audioSource.PlayOneShot(strikeSound, strikeVolume);
    }

#if UNITY_EDITOR
    private bool TryFindOpponentInReach(out Vector3 target)
    {
        target = default;
        Camera cam = weapon != null ? weapon.AimCamera : null;
        if (cam == null) return false;
        foreach (PlayerLocomotion other in PlayerLocomotion.SpawnedPlayers)
        {
            if (other == null || other == locomotion || other.Hitbox == null) continue;
            target = other.Hitbox.TorsoCollider.transform.position;
            if (Vector3.Distance(cam.transform.position, target) <= range * 0.9f) return true;
        }
        return false;
    }
#endif
}
