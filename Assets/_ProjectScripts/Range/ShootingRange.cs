using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Le stand de tir (2026-10-16, demandé par l'utilisateur pour essayer toutes les armes avec leurs
/// accessoires et faire ses retours) : la scène StandDeTir, des mannequins à plusieurs distances
/// (TrainingDummy), et une fenêtre pour changer d'arme, de modèle, de finition et d'accessoires sans
/// repasser par le menu (ShootingRangePanel). On y entre par le bouton « Stand de tir » de l'écran titre
/// (MainMenuController.StartShootingRange), seul : l'hôte n'écoute que sa propre machine.
///
/// Pas de boucle de manches : sans RoundManager, on bouge, on tire et on change d'arme librement
/// (RoundManager.MovementAllowedFor, WeaponSwitchAllowedFor). Le serveur place les joueurs au poste de
/// tir (PlayerSpawnPoints de la scène), comme l'arène le fait (RoundManager.OnNetworkSpawn).
///
/// Ouvrir la scène dans l'Editor et lancer le Play Mode y mène aussi : sans NetworkManager, elle charge
/// le menu, qui lance le stand (ShootingRangeBootstrap).
/// </summary>
[DisallowMultipleComponent]
public class ShootingRange : NetworkBehaviour
{
    /// <summary>Le nom de la scène du stand (Build Settings).</summary>
    public const string SceneName = "StandDeTir";

    private static ShootingRange cached;
    private readonly List<TrainingDummy> dummies = new List<TrainingDummy>();

    public static ShootingRange Instance
    {
        get
        {
            if (cached == null) cached = FindAnyObjectByType<ShootingRange>();
            return cached;
        }
    }

    /// <summary>Les mannequins du stand.</summary>
    public IReadOnlyList<TrainingDummy> Dummies
    {
        get
        {
            if (dummies.Count == 0) dummies.AddRange(FindObjectsByType<TrainingDummy>());
            return dummies;
        }
    }

    public override void OnNetworkSpawn()
    {
        cached = this;
        if (!IsServer) return;

        // Le joueur de l'hôte naît dans la scène du menu : on le place au poste de tir une fois le stand
        // chargé. Un joueur qui arrive ensuite y est placé à son apparition.
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
            if (player != null) player.ServerMoveToSpawnPoint();
        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null) NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
    }

    private void HandleClientConnected(ulong clientId)
    {
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
            if (player != null && player.OwnerClientId == clientId) player.ServerMoveToSpawnPoint();
    }

    /// <summary>Serveur : tous les mannequins retrouvent leur vie.</summary>
    public void ServerRestoreDummies()
    {
        if (!IsServer) return;
        foreach (TrainingDummy dummy in Dummies)
            if (dummy != null) dummy.ServerRestore();
    }
}
