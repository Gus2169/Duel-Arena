using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Les accessoires des armes d'un joueur (2026-10-16, GDD § 6) : le viseur, la poignée, le laser et le
/// chargeur choisis à l'armurerie pour chacune des quatre armes (WeaponAttachments).
///
/// Comme l'apparence (PlayerWeaponLooks), le joueur les annonce dans sa demande de connexion
/// (SessionFlow), le serveur les borne (WeaponAttachments.Sanitized) et les publie à l'apparition du
/// robot, dans le message même qui le fait apparaître. Mais eux changent l'arme : WeaponController en
/// lit la taille du chargeur et la durée du rechargement, chez le joueur comme sur le serveur, qui
/// comptent ainsi le même chargeur ; le recul et l'épaulement se jouent chez le joueur. Le corps
/// (PlayerAnimator) et les bras de la vue subjective (FirstPersonView) y montent les accessoires, et le
/// faisceau du laser se voit de tous (TacticalLaser).
/// </summary>
[DisallowMultipleComponent]
public class PlayerWeaponAttachments : NetworkBehaviour
{
    private readonly NetworkVariable<uint> attachments = new NetworkVariable<uint>(
        0u, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private WeaponController weapon;
    private bool published;
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    private WeaponAttachments? offline;   // hors session (les tests) : ce que le test a posé
#endif

    /// <summary>Les accessoires publiés par le serveur (aucun tant qu'il n'a rien dit).</summary>
    public WeaponAttachments Attachments
    {
        get
        {
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
            if (offline.HasValue && !IsSpawned) return offline.Value;
#endif
            return new WeaponAttachments(attachments.Value);
        }
    }

    /// <summary>Le serveur a publié les accessoires de ce joueur : ils sont sûrs.</summary>
    public bool Published => published;

    /// <summary>Levé à la publication (l'apparition du robot), puis à chaque changement.</summary>
    public event Action<WeaponAttachments> Changed;

    private void Awake() => weapon = GetComponentInChildren<WeaponController>(true);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            WeaponAttachments wanted = SessionFlow.TakeApprovedAttachments(OwnerClientId);
            attachments.Value = wanted.Sanitized(weapon != null ? weapon.Loadout : null).Packed;
        }
        attachments.OnValueChanged += HandleChanged;
        published = true;
        Changed?.Invoke(Attachments);
    }

    public override void OnNetworkDespawn()
    {
        attachments.OnValueChanged -= HandleChanged;
        published = false;
    }

    private void HandleChanged(uint previous, uint current) => Changed?.Invoke(new WeaponAttachments(current));

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Hors session (tests) : les accessoires de ce joueur, sans serveur.</summary>
    public void SetOfflineForTests(WeaponAttachments value)
    {
        offline = value;
        published = true;
        Changed?.Invoke(value);
    }
#endif
}
