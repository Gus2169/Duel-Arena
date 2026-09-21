using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Boutons de test temporaires pour valider une connexion réseau, avant toute vraie UI de lobby
/// (prévue en Phase 7). Pose ce script sur le MÊME GameObject que ton NetworkManager.
///
/// Affiche 3 boutons (Host / Server / Client) tant qu'aucune connexion n'est active, puis affiche
/// le rôle courant et le nombre de clients connectés une fois connecté. Logue aussi dans la
/// Console à chaque connexion/déconnexion, pour confirmer sans ambiguïté que ça fonctionne.
///
/// À désactiver/supprimer une fois qu'un vrai flow de connexion (lobby, Relay) existera.
/// </summary>
public class NetworkBootstrapUI : MonoBehaviour
{
    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        Debug.Log($"[Netcode] Client connecté : id={clientId}.");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        Debug.Log($"[Netcode] Client déconnecté : id={clientId}.");
    }

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null)
        {
            GUI.Label(new Rect(10, 10, 420, 30), "NetworkManager.Singleton est null — vérifie qu'il y a bien un NetworkManager actif dans cette scène.");
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 240, 160));

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Host")) NetworkManager.Singleton.StartHost();
            if (GUILayout.Button("Server")) NetworkManager.Singleton.StartServer();
            if (GUILayout.Button("Client")) NetworkManager.Singleton.StartClient();
        }
        else
        {
            string role = NetworkManager.Singleton.IsHost ? "Host" : (NetworkManager.Singleton.IsServer ? "Server" : "Client");
            GUILayout.Label($"Connecté en tant que : {role}");
            GUILayout.Label($"Clients connectés : {NetworkManager.Singleton.ConnectedClients.Count}");
        }

        GUILayout.EndArea();
    }
}
