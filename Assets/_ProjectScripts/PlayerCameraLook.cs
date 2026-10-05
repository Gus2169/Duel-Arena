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

    [Tooltip("Enfant 'Leanpivot' qui porte la caméra. Le lean y est appliqué APRÈS le pitch. Laissé vide, il est cherché par son nom.")]
    [SerializeField] private Transform leanPivot;

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

    /// <summary>Décalage horizontal du RECUL, appliqué à la caméra seule (degrés). Le yaw du corps
    /// appartient à la simulation (PlayerLocomotion.Move) et n'en reçoit jamais : voir
    /// AddInstantRotation.</summary>
    private float recoilYaw;
    private bool initializedForOwner;

    private void Awake()
    {
        input = GetComponentInParent<PlayerInputReader>();
        locomotion = GetComponentInParent<PlayerLocomotion>();
        networkObject = GetComponentInParent<NetworkObject>();
        if (leanPivot == null) leanPivot = transform.Find("Leanpivot");
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
        ApplyLocalRotation();
        ApplyLean();

        UpdateFov();
    }

    private void ApplyLocalRotation()
    {
        transform.localRotation = Quaternion.Euler(pitch, recoilYaw, 0f);
    }

    /// <summary>Place la caméra sur l'œil penché. Le décalage est exprimé dans l'espace du JOUEUR
    /// (latéral + descente de la tête quand le buste s'incline) ; ce pivot-ci porte le pitch, donc
    /// on l'y ramène. Sans cette conversion, la descente de la tête partirait en avant ou en
    /// arrière dès qu'on regarde en haut ou en bas. Fait APRÈS le pitch, dans la même frame.</summary>
    private void ApplyLean()
    {
        if (leanPivot == null || locomotion == null) return;

        leanPivot.localPosition = Quaternion.Inverse(transform.localRotation) * locomotion.LeanCameraOffset;
        leanPivot.localRotation = Quaternion.Euler(0f, 0f, locomotion.LeanCameraTilt);
    }

    private void UpdateFov()
    {
        if (targetCamera == null || locomotion == null) return;

        float target = locomotion.IsSprinting ? sprintFov : baseFov;
        targetCamera.fieldOfView = Mathf.MoveTowards(targetCamera.fieldOfView, target, fovTransitionSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Applique une rotation instantanée à la CAMÉRA, indépendamment de l'input souris. Utilisé
    /// pour le kick de recul et sa récupération. Même convention de signe que le look souris :
    /// pitchDelta positif = caméra monte, yawDelta positif = rotation vers la droite.
    ///
    /// 🚨 Le yaw de recul ne touche JAMAIS le corps (corrigé le 2026-10-05). Il faisait
    /// `locomotion.transform.Rotate(...)`, donc modifiait le yaw — une valeur de la simulation —
    /// HORS de Move() : un client distant tournait sans que le serveur le sache. Le zigzag du MP5
    /// restait sous le seuil de réconciliation de 1°, mais une arme qui dévie d'un seul côté
    /// aurait provoqué des recalages en pleine rafale. La visée, elle, ne change pas : le tir
    /// part de la caméra, qui porte ce décalage.
    /// </summary>
    public void AddInstantRotation(float pitchDelta, float yawDelta)
    {
        pitch = Mathf.Clamp(pitch - pitchDelta, minPitch, maxPitch);
        recoilYaw += yawDelta;
        ApplyLocalRotation();
        ApplyLean();
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Surface de test : décalage horizontal de recul porté par la caméra.</summary>
    public float TestRecoilYaw => recoilYaw;
#endif
}
