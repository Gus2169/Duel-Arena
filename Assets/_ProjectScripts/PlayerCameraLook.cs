using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Look caméra à la souris/stick, posé sur "CameraPivot". Gère aussi le FOV kick au sprint
/// et AddInstantRotation(), utilisée par WeaponController pour le recul.
///
/// Le YAW (rotation du corps du joueur) n'est PLUS géré ici : il est réseauté et appliqué de
/// façon déterministe dans PlayerLocomotion.Move() (voir commentaire là-bas). Ce script ne gère
/// donc plus que le PITCH (inclinaison caméra locale, purement cosmétique) et le FOV — et
/// uniquement pour le propriétaire local (voir garde IsOwner ci-dessous) : sans ça, chaque
/// instance de joueur présente sur une machine (y compris celle des AUTRES joueurs, affichée
/// localement) réagissait à la souris PHYSIQUE de cette machine, d'où la caméra qui semblait
/// "bouger toute seule" côté spectateur dès qu'on avait 2 joueurs dans la scène.
/// </summary>
public class PlayerCameraLook : MonoBehaviour
{
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float minPitch = -85f;
    [SerializeField] private float maxPitch = 85f;

    [Header("FOV")]
    [Tooltip("La Camera réelle (sous LeanPivot). Laisse vide pour désactiver le FOV kick.")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float baseFov = 90f;
    [SerializeField] private float sprintFov = 96f;
    [SerializeField] private float fovTransitionSpeed = 8f;

    private PlayerInputReader input;
    private PlayerLocomotion locomotion;
    private NetworkObject networkObject;
    private float pitch;
    private bool initializedForOwner;

    private void Awake()
    {
        input = GetComponentInParent<PlayerInputReader>();
        locomotion = GetComponentInParent<PlayerLocomotion>();
        networkObject = GetComponentInParent<NetworkObject>();
    }

    private void LateUpdate()
    {
        // Non-owner (perso d'un autre joueur affiché localement) : on ne touche à rien ici, ni au
        // curseur ni à la rotation — sa caméra est de toute façon désactivée par PlayerLocomotion,
        // et son yaw suit désormais networkYaw (voir PlayerLocomotion.UpdateRemoteInterpolation).
        if (networkObject != null && !networkObject.IsOwner) return;

        // Fait une fois, ici plutôt que dans Awake() : au moment d'Awake, IsOwner n'est pas
        // encore garanti fiable tant que l'objet n'a pas fini son spawn réseau.
        if (!initializedForOwner)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (targetCamera != null) targetCamera.fieldOfView = baseFov;
            initializedForOwner = true;
        }

        if (input == null) return;

        Vector2 look = input.LookInput * mouseSensitivity;

        pitch = Mathf.Clamp(pitch - look.y, minPitch, maxPitch);
        transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        UpdateFov();
    }

    private void UpdateFov()
    {
        if (targetCamera == null || locomotion == null) return;

        float target = locomotion.IsSprinting ? sprintFov : baseFov;
        targetCamera.fieldOfView = Mathf.MoveTowards(targetCamera.fieldOfView, target, fovTransitionSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Applique une rotation instantanée à la caméra (pitch) et au corps du joueur (yaw),
    /// indépendamment de l'input souris. Utilisé pour le kick de recul et sa récupération.
    /// Le signe suit la même convention que le look souris : pitchDelta positif = caméra
    /// monte, yawDelta positif = rotation vers la droite.
    /// </summary>
    public void AddInstantRotation(float pitchDelta, float yawDelta)
    {
        pitch = Mathf.Clamp(pitch - pitchDelta, minPitch, maxPitch);
        transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        if (locomotion != null && Mathf.Abs(yawDelta) > 0f)
        {
            locomotion.transform.Rotate(Vector3.up * yawDelta);
        }
    }
}
