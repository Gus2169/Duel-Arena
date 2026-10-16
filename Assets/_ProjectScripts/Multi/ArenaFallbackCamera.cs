using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Caméra de secours de la scène de jeu : affiche quelque chose entre le chargement de la scène
/// et l'apparition du joueur local (ce délai existe surtout côté CLIENT distant — le Host, lui,
/// a déjà son joueur avant même que la scène ne finisse de charger, voir MainMenuController).
///
/// Posée directement sur la caméra qu'elle éteint (contrairement à l'ancienne version de ce
/// comportement, qui vivait sur le NetworkManager — devenu persistant d'une scène à l'autre
/// depuis que le menu principal existe, donc incapable de retrouver la caméra de CETTE scène
/// après un changement de scène). Une caméra par scène gère désormais la sienne.
///
/// DOIT être éteinte dès qu'une session réseau est active, sinon elle continue de rendre la scène
/// en parallèle de la caméra du joueur une fois celui-ci apparu — deux caméras plein écran à la
/// même depth, donc la scène dessinée DEUX FOIS par frame, et un AudioListener de trop.
/// </summary>
public class ArenaFallbackCamera : MonoBehaviour
{
    [Tooltip("Garder l'image et n'éteindre que l'écoute : sur la caméra du menu (2026-10-13), dont le fond reste affiché pendant que le robot de l'hôte apparaît, avant le chargement de l'arène.")]
    [SerializeField] private bool keepCamera;

    private Camera cam;
    private AudioListener listener;
    private bool disabled;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        listener = GetComponent<AudioListener>();
    }

    private void Update()
    {
        if (disabled) return;
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer) return;

        if (cam != null && !keepCamera) cam.enabled = false;
        if (listener != null) listener.enabled = false;
        disabled = true;
    }
}
