using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// L'apparence des armes d'un joueur, vue par tous (2026-10-15, GDD § 6) : la finition choisie à
/// l'armurerie pour chacune des armes de l'arsenal (WeaponLooks).
///
/// Comme le pseudo, le joueur l'annonce dans sa demande de connexion (SessionFlow) : aucune RPC de
/// plus. Le serveur la borne (WeaponLooks.Sanitized : une finition qui n'existe pas redevient
/// l'origine) et la publie à l'apparition du robot, dans le message même qui le
/// fait apparaître. Le corps (PlayerAnimator) et les bras de la vue subjective (FirstPersonView) s'y
/// abonnent et mettent en main les bons modèles.
///
/// Catégorie C réseautée : rien de la simulation, du tir, du hitbox ni du son ne la lit (GDD § 6 et
/// § 8 : les finitions ne changent ni la silhouette, ni le bruit des armes).
/// </summary>
[DisallowMultipleComponent]
public class PlayerWeaponLooks : NetworkBehaviour
{
    private readonly NetworkVariable<ulong> looks = new NetworkVariable<ulong>(
        0UL, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private WeaponController weapon;
    private bool published;

    /// <summary>L'apparence publiée par le serveur (les armes par défaut tant qu'il n'a rien dit).</summary>
    public WeaponLooks Looks => new WeaponLooks(looks.Value);

    /// <summary>Le serveur a publié l'apparence de ce joueur : elle est sûre.</summary>
    public bool Published => published;

    /// <summary>Levé à la publication (l'apparition du robot), puis à chaque changement.</summary>
    public event Action<WeaponLooks> Changed;

    private void Awake() => weapon = GetComponentInChildren<WeaponController>(true);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            WeaponLooks wanted = SessionFlow.TakeApprovedLooks(OwnerClientId);
            looks.Value = wanted.Sanitized(weapon != null ? weapon.Loadout : null).Packed;
        }
        looks.OnValueChanged += HandleChanged;
        published = true;
        Changed?.Invoke(Looks);
    }

    public override void OnNetworkDespawn()
    {
        looks.OnValueChanged -= HandleChanged;
        published = false;
    }

    private void HandleChanged(ulong previous, ulong current) => Changed?.Invoke(new WeaponLooks(current));

    /// <summary>Serveur : change l'apparence en cours de partie (le stand de tir, 2026-10-16), bornée
    /// comme à l'apparition.</summary>
    public void ServerSet(WeaponLooks value)
    {
        if (!IsServer || !IsSpawned) return;
        looks.Value = value.Sanitized(weapon != null ? weapon.Loadout : null).Packed;
    }
}
