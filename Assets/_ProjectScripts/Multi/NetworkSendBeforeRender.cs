using System;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

/// <summary>
/// Netcode envoie ses paquets une fois par image, et s'inscrit après PlayerSendFrameComplete, donc après
/// FinishFrameRendering, l'étape qui porte tout le travail d'URP sur le fil principal : une correction, une pose ou un
/// input décidés en début d'image attendaient le rendu entier avant de partir. Cette classe le replace juste avant le
/// rendu : le réseau gagne la durée du rendu à chaque passage dans une machine, sans un octet de plus.
/// Il reste sous PostLateUpdate, où UnregisterLoopSystems de Netcode le retrouve.
/// </summary>
public static class NetworkSendBeforeRender
{
    /// <summary>Le système de Netcode, interne à son assembly : il se reconnaît à son nom.</summary>
    public const string NetcodeSendSystem = "NetworkPostLateUpdate";

    // Après SubsystemRegistration, où Netcode s'inscrit, y compris à chaque entrée en Play Mode.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < root.subSystemList.Length; i++)
        {
            if (root.subSystemList[i].type != typeof(PostLateUpdate)) continue;
            root.subSystemList[i].subSystemList = MoveBeforeRendering(root.subSystemList[i].subSystemList);
            PlayerLoop.SetPlayerLoop(root);
            return;
        }
    }

    /// <summary>La liste de PostLateUpdate, l'envoi de Netcode replacé juste avant le rendu. Rend la liste telle quelle
    /// s'il n'y est pas, ou s'il est déjà avant.</summary>
    public static PlayerLoopSystem[] MoveBeforeRendering(PlayerLoopSystem[] systems)
    {
        int send = Array.FindIndex(systems, s => s.type != null && s.type.Name == NetcodeSendSystem);
        int rendering = Array.FindIndex(systems, s => s.type == typeof(PostLateUpdate.FinishFrameRendering));
        if (send < 0 || rendering < 0 || send < rendering) return systems;

        var moved = new PlayerLoopSystem[systems.Length];
        int next = 0;
        for (int i = 0; i < systems.Length; i++)
        {
            if (i == send) continue;
            if (i == rendering) moved[next++] = systems[send];
            moved[next++] = systems[i];
        }
        return moved;
    }
}
