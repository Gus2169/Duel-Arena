using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ce que le joueur entend de ses propres coups et de ceux qu'il prend (2026-10-13, GDD § 6) : un son
/// de touche, un autre pour la tête, un autre pour le coup qui tue ; et quand il est touché, l'impact
/// sur son châssis et des parasites. Chez le propriétaire seul, à plat.
///
/// Les touches sont confirmées par le serveur (WeaponController.OnHitConfirmed) : on n'entend jamais
/// une touche que le serveur n'a pas comptée. Les dégâts se lisent sur la vie publiée (Health).
/// Catégorie C : rien ne relit ces sons.
/// </summary>
[DisallowMultipleComponent]
public class PlayerFeedbackAudio : MonoBehaviour
{
    [SerializeField] private GameSounds sounds;
    [Tooltip("Dégâts (PV) d'un coup qui sonne à plein volume ; un plus petit sonne moins fort.")]
    [SerializeField] private float fullDamage = 40f;

    private NetworkObject networkObject;
    private WeaponController weapon;
    private Health health;

    private bool IsLocalPlayer => networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner;

    private void Awake()
    {
        networkObject = GetComponent<NetworkObject>();
        weapon = GetComponentInChildren<WeaponController>(true);
        health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        if (weapon != null) weapon.OnHitConfirmed += HandleHitConfirmed;
        if (health != null) health.OnDamaged += HandleDamaged;
    }

    private void OnDisable()
    {
        if (weapon != null) weapon.OnHitConfirmed -= HandleHitConfirmed;
        if (health != null) health.OnDamaged -= HandleDamaged;
    }

    private void HandleHitConfirmed(WeaponController.HitKind kind)
    {
        if (sounds == null || !IsLocalPlayer) return;
        SoundSet sound = kind switch
        {
            WeaponController.HitKind.Kill => sounds.kill,
            WeaponController.HitKind.Head => sounds.headshot,
            _ => sounds.hit,
        };
        sound?.Play2D();
    }

    private void HandleDamaged(float amount, GameObject source)
    {
        if (sounds == null || !IsLocalPlayer || amount <= 0f) return;
        float level = DamageLevel(amount, fullDamage);
        sounds.damage?.Play2D(level);
        sounds.damageStatic?.Play2D(level);
    }

    /// <summary>Le volume (0,5 à 1) du retour d'un coup de `amount` PV : un plomb de pompe ne sonne
    /// pas comme une balle de sniper. Fonction pure, testée.</summary>
    public static float DamageLevel(float amount, float fullDamage)
    {
        if (!(amount > 0f)) return 0f; // NaN compris
        return Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(amount / Mathf.Max(1f, fullDamage)));
    }
}
