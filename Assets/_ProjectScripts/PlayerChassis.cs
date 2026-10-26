using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Le châssis du robot d'un joueur (RobotChassis, 2026-10-26) : choisi dans Personnalisation
/// (GameSettings.PreferredChassis), annoncé dans la demande de connexion (SessionFlow), borné et publié par le
/// serveur à l'apparition du robot, comme l'apparence des armes (PlayerWeaponLooks).
///
/// Chaque machine allume sous « Model » les pièces du châssis publié. Les copies du modèle faites ensuite
/// (l'épave) en héritent ; celles qui vivent avec le joueur (les bras de la vue subjective, ceux du spectateur)
/// suivent `Changed`.
/// </summary>
[DisallowMultipleComponent]
public class PlayerChassis : NetworkBehaviour
{
    private readonly NetworkVariable<byte> networkChassis = new NetworkVariable<byte>(
        (byte)ChassisId.CT01, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Le châssis publié.</summary>
    public ChassisId Value => RobotChassis.Sanitized(networkChassis.Value);

    /// <summary>Le châssis vient d'être appliqué au modèle (à l'apparition, puis à chaque changement).</summary>
    public event Action<ChassisId> Changed;

    public override void OnNetworkSpawn()
    {
        if (IsServer) networkChassis.Value = (byte)RobotChassis.Sanitized(SessionFlow.TakeApprovedChassis(OwnerClientId));
        networkChassis.OnValueChanged += HandleChanged;
        Apply();
    }

    public override void OnNetworkDespawn() => networkChassis.OnValueChanged -= HandleChanged;

    private void HandleChanged(byte previous, byte current) => Apply();

    private void Apply()
    {
        RobotChassis.Apply(transform.Find("Model"), Value);
        Changed?.Invoke(Value);
    }
}
