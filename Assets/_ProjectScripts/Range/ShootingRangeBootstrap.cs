using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Le stand de tir ouvert directement dans l'Editor (2026-10-16) : la scène n'a pas de NetworkManager
/// (il vit dans le menu et voyage d'une scène à l'autre). Lancer le Play Mode depuis elle charge donc le
/// menu, qui lance le stand comme son bouton (MainMenuController.StartShootingRange). Arrivé par le
/// menu, il ne fait rien.
///
/// Le joueur virtuel de MPPM ouvre la même scène que l'Editor principal : il rejoint le menu sans
/// lancer de stand (il en ouvrait un second, et prenait le port avant l'Editor principal).
/// </summary>
public class ShootingRangeBootstrap : MonoBehaviour
{
    /// <summary>Le menu doit-il lancer le stand dès son ouverture ?</summary>
    public static bool StartRangeOnMenuLoad;

    private void Awake()
    {
        if (NetworkManager.Singleton != null) return;
        StartRangeOnMenuLoad = !IsVirtualPlayerClone();
        SceneManager.LoadScene(0);   // le menu, premier de la Build
    }

    private static bool IsVirtualPlayerClone()
        => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--virtual-project-clone") >= 0;
}
