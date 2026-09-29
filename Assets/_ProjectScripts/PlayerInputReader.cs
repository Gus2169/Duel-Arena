using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Fait le pont entre l'asset Input Actions (généré en C#) et le reste du code du joueur.
///
/// Deux formes pour le tir : FireHeld (armes automatiques) et FirePressedThisFrame (semi-auto,
/// un appui = un coup même en restant appuyé).
///
/// Les actions "one-shot" (Crouch, Prone, Jump, Lean, Fire) sont mises à true par un callback
/// de l'Input System puis remises à false par ConsumeFrameInputs() en fin de frame. Ce script
/// ne connaît PAS la notion de propriétaire réseau : il lit le clavier/souris physique de la
/// machine. C'est aux consommateurs (PlayerLocomotion, WeaponController) de se garder avec
/// IsOwner — sans quoi chaque instance de joueur affichée localement réagirait à ces inputs.
///
/// NOTE : JumpPressedThisFrame n'est PAS lu depuis Update() mais recopié dans le snapshot d'input
/// envoyé au serveur — le vault, son seul consommateur, vit dans Move() pour rester déterministe.
/// Il faut donc le consommer APRÈS la construction du snapshot, pas avant.
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

#if UNITY_EDITOR
    /// <summary>
    /// AUTOPILOTE DE TEST — Editor uniquement, jamais embarqué en build.
    ///
    /// Fait faire au joueur un va-et-vient latéral déterministe à la place de l'input clavier.
    /// Sert à tester le rewind : une seule personne ne peut pas à la fois se déplacer sur une
    /// instance et viser sur l'autre, or c'est exactement la situation que la compensation de
    /// latence existe pour couvrir. L'autopilote tient le rôle de la cible mouvante.
    ///
    /// Déterministe (fonction du temps, sans aléatoire) pour que deux essais soient comparables.
    /// </summary>
    public static bool AutopilotStrafe;

    [Tooltip("Période (s) d'un aller-retour complet de l'autopilote de test.")]
    public static float AutopilotPeriod = 2f;
#endif

    private void Update()
    {
#if UNITY_EDITOR
        if (AutopilotStrafe)
        {
            // Créneau plutôt que sinusoïde : on veut une vitesse latérale CONSTANTE, donc un
            // décalage bien franc entre ce que voit le tireur et ce que le serveur connaît —
            // c'est ce décalage que le rewind doit annuler. Une sinusoïde ralentirait aux
            // extrémités, là où l'erreur est justement la plus faible.
            float phase = Mathf.Repeat(Time.time / Mathf.Max(0.1f, AutopilotPeriod), 1f);
            MoveInput = new Vector2(phase < 0.5f ? 1f : -1f, 0f);
            LookInput = Vector2.zero;
            SprintHeld = false;
            SneakHeld = false;
            AimHeld = false;
            FireHeld = false;
            return;
        }
#endif

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
