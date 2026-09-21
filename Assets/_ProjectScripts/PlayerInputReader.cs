using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Fait le pont entre l'asset Input Actions (généré en C#) et le reste du code du joueur.
///
/// Nouveau par rapport à ta version : l'action "Fire" (clic gauche), avec à la fois
/// FireHeld (pour les armes automatiques) et FirePressedThisFrame (pour le semi-auto,
/// un appui = un coup, même si le joueur reste appuyé).
///
/// Pré-requis dans l'éditeur Unity :
/// 1. Dans PlayerControls.inputactions, Action Map "Gameplay", ajoute une action :
///    - Fire (Button) -> clic gauche de la souris (<Mouse>/leftButton)
/// 2. Reclique sur "Apply" dans l'inspecteur de l'asset pour régénérer la classe C#.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInputReader : MonoBehaviour
{
    private PlayerControls controls;

    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }
    public bool SprintHeld { get; private set; }
    public bool SneakHeld { get; private set; }
    public bool AimHeld { get; private set; }
    public bool FireHeld { get; private set; }

    public bool CrouchPressedThisFrame { get; private set; }
    public bool PronePressedThisFrame { get; private set; }
    public bool JumpPressedThisFrame { get; private set; }
    public bool LeanLeftPressedThisFrame { get; private set; }
    public bool LeanRightPressedThisFrame { get; private set; }
    public bool FirePressedThisFrame { get; private set; }

    private void Awake()
    {
        controls = new PlayerControls();
    }

    private void OnEnable()
    {
        controls.Gameplay.Enable();

        controls.Gameplay.Crouch.performed += _ => CrouchPressedThisFrame = true;
        controls.Gameplay.Prone.performed += _ => PronePressedThisFrame = true;
        controls.Gameplay.Jump.performed += _ => JumpPressedThisFrame = true;
        controls.Gameplay.LeanLeft.performed += _ => LeanLeftPressedThisFrame = true;
        controls.Gameplay.LeanRight.performed += _ => LeanRightPressedThisFrame = true;
        controls.Gameplay.Fire.performed += _ => FirePressedThisFrame = true;
    }

    private void OnDisable()
    {
        controls.Gameplay.Disable();
    }

    private void Update()
    {
        MoveInput = controls.Gameplay.Move.ReadValue<Vector2>();
        LookInput = controls.Gameplay.Look.ReadValue<Vector2>();
        SprintHeld = controls.Gameplay.Sprint.IsPressed();
        SneakHeld = controls.Gameplay.Sneak.IsPressed();
        AimHeld = controls.Gameplay.Aim.IsPressed();
        FireHeld = controls.Gameplay.Fire.IsPressed();
    }

    /// <summary>
    /// À appeler en fin de frame une fois les inputs "one-shot" consommés, pour éviter
    /// qu'un Crouch/Prone/Jump/Lean/Fire(semi-auto) ne soit lu deux fois.
    /// </summary>
    public void ConsumeFrameInputs()
    {
        CrouchPressedThisFrame = false;
        PronePressedThisFrame = false;
        JumpPressedThisFrame = false;
        LeanLeftPressedThisFrame = false;
        LeanRightPressedThisFrame = false;
        FirePressedThisFrame = false;
    }
}
