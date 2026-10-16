using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// L'apparence des armes d'un joueur, vue par tous (2026-10-15, GDD § 6) : le modèle et la finition
/// choisis à l'armurerie pour chacune des quatre armes de la panoplie (WeaponLooks).
///
/// Comme le pseudo, le joueur l'annonce dans sa demande de connexion (SessionFlow) : aucune RPC de
/// plus. Le serveur la borne (WeaponLooks.Sanitized : un modèle ou une finition qui n'existe pas
/// redevient le choix par défaut) et la publie à l'apparition du robot, dans le message même qui le
/// fait apparaître. Le corps (PlayerAnimator) et les bras de la vue subjective (FirstPersonView) s'y
/// abonnent et mettent en main les bons modèles.
///
/// Catégorie C réseautée : rien de la simulation, du tir, du hitbox ni du son ne la lit (GDD § 6 et
/// § 8 : les finitions ne changent ni la silhouette, ni le bruit des armes).
/// </summary>
[DisallowMultipleComponent]
public class PlayerWeaponLooks : NetworkBehaviour
{
    private readonly NetworkVariable<uint> looks = new NetworkVariable<uint>(
        0u, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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

    private void HandleChanged(uint previous, uint current) => Changed?.Invoke(new WeaponLooks(current));
}
