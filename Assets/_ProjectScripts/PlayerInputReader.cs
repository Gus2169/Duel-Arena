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

    /// <summary>
    /// Appui sur Recharger pendant cette frame. Lu directement dans l'Input System plutôt que posé
    /// par un callback et remis à zéro par ConsumeFrameInputs() : c'est PlayerLocomotion qui
    /// consomme les appuis en fin d'Update, et l'ordre d'exécution entre lui et WeaponController
    /// n'est pas fixé. Un drapeau consommé avant que l'arme ne le lise perdrait l'appui.
    /// </summary>
    public bool ReloadPressedThisFrame => controls != null && controls.Gameplay.Reload.WasPressedThisFrame();

    /// <summary>Appui sur le taser (corps à corps) pendant cette frame. Même lecture directe que le
    /// rechargement, pour la même raison.</summary>
    public bool MeleePressedThisFrame => controls != null && controls.Gameplay.Melee.WasPressedThisFrame();

    /// <summary>Appui sur la gâchette pendant cette frame, lu directement dans l'Input System : c'est
    /// lui que lit une arme au coup par coup (2026-10-11). FirePressedThisFrame, posé par callback et
    /// remis à zéro par PlayerLocomotion en fin d'Update, pouvait être consommé avant que l'arme ne
    /// le lise (dette n° 32) ; il ne sert plus qu'au snapshot de déplacement.</summary>
    public bool FireWasPressedThisFrame => controls != null && controls.Gameplay.Fire.WasPressedThisFrame();

    /// <summary>Arme demandée pendant cette frame (touches 1 à 4, 2026-10-11) : 0 à 3, ou -1. Même
    /// lecture directe que le rechargement.</summary>
    public int WeaponSlotPressedThisFrame
    {
        get
        {
            if (controls == null) return -1;
            if (controls.Gameplay.Weapon1.WasPressedThisFrame()) return 0;
            if (controls.Gameplay.Weapon2.WasPressedThisFrame()) return 1;
            if (controls.Gameplay.Weapon3.WasPressedThisFrame()) return 2;
            if (controls.Gameplay.Weapon4.WasPressedThisFrame()) return 3;
            return -1;
        }
    }

    private void Awake()
    {
        controls = new PlayerControls();
        // Remappages faits au menu Options : chaque `new PlayerControls()` déserialise sa PROPRE
        // copie de l'asset généré, sans eux cette instance resterait sur les touches par défaut.
        GameSettings.ApplyBindingOverrides(controls.asset);
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

    /// <summary>Le va-et-vient de test se fait en sneak (Editor uniquement).</summary>
    public static bool AutopilotSneak;

    [Tooltip("Période (s) d'un aller-retour complet de l'autopilote de test.")]
    public static float AutopilotPeriod = 2f;

    /// <summary>Tir automatique de test, gâchette maintenue — Editor uniquement. Couplé à
    /// WeaponController.AutopilotAimAtOpponent, il fait tenir le rôle du tireur à une instance
    /// que personne ne pilote (le clone de Multiplayer Play Mode).</summary>
    public static bool AutopilotFire;

    /// <summary>Visée maintenue de test — Editor uniquement (vérifier la vue subjective en visée, et
    /// la direction du buste, va-et-vient compris).</summary>
    public static bool AutopilotAim;

    /// <summary>Exercice de test du sprint face au rechargement et au taser (2026-10-10, Editor
    /// uniquement) : un cycle scripté de DrillPeriod secondes, voir UpdateSprintDrill. Sert à
    /// vérifier à deux instances que le serveur juge le sprint dans le bon ordre par rapport aux
    /// demandes de rechargement et de coup.</summary>
    public static bool AutopilotSprintDrill;

    private const float DrillPeriod = 8f;
    private float drillPreviousTime = -1f;

    /// <summary>Appuis scriptés de l'exercice, vrais d'un Update de ce lecteur au suivant : l'arme
    /// et le taser les voient donc une fois, quel que soit l'ordre d'exécution.</summary>
    public bool DrillReloadPressed { get; private set; }
    public bool DrillMeleePressed { get; private set; }

    /// <summary>Un cycle de 8 s. Recule en tirant (0 à 1,2 s), recharge (1,2 s) puis se met à courir
    /// (1,6 s) : le rechargement doit être annulé. Court (3 s), recharge en courant (3,2 s) touche
    /// tenue : le sprint s'arrête, le rechargement part à la fin de la sortie de sprint et va au
    /// bout. Court (5,8 s), frappe en courant (6 s) touche tenue : le sprint s'arrête pendant le
    /// geste, le coup n'est pas annulé. Lâche la course pour tirer (6,8 s) : le premier tir attend
    /// la fin de la sortie de sprint (2026-10-11).</summary>
    private void UpdateSprintDrill()
    {
        float t = Mathf.Repeat(Time.time, DrillPeriod);
        float previous = drillPreviousTime < 0f ? t : drillPreviousTime;
        drillPreviousTime = t;

        bool sprint = (t >= 1.6f && t < 2.4f) || (t >= 3.0f && t < 4.6f) || (t >= 5.8f && t < 6.8f);
        // En avant pour courir, en arrière le reste du temps : le joueur reste à peu près sur place.
        MoveInput = sprint ? Vector2.up : Vector2.down;
        LookInput = Vector2.zero;
        SprintHeld = sprint;
        SneakHeld = false;
        AimHeld = false;
        FireHeld = t < 1.2f || (t >= 6.8f && t < 7.6f);
        DrillReloadPressed = DrillCrossed(previous, t, 1.2f) || DrillCrossed(previous, t, 3.2f);
        DrillMeleePressed = DrillCrossed(previous, t, 6.0f);
    }

    /// <summary>Exercice du franchissement (2026-10-11, Editor uniquement) : avance, appuie sur saut
    /// toutes les 0,3 s et tire en continu. Placé face à un obstacle franchissable, le joueur le
    /// franchit en tirant : un joueur honnête ne tire pas pendant le franchissement, un tricheur
    /// (PlayerLocomotion.DebugVaultIgnoresRules) voit ces tirs rejetés par le serveur.</summary>
    public static bool AutopilotVaultDrill;

    private void UpdateVaultDrill()
    {
        float t = Mathf.Repeat(Time.time, 0.3f);
        float previous = drillPreviousTime < 0f ? t : drillPreviousTime;
        drillPreviousTime = t;
        MoveInput = Vector2.up;
        LookInput = Vector2.zero;
        SprintHeld = false;
        SneakHeld = false;
        AimHeld = false;
        FireHeld = true;
        if (t < previous) JumpPressedThisFrame = true;
    }

    private static bool DrillCrossed(float from, float to, float mark)
        => from <= to ? from < mark && mark <= to : from < mark || mark <= to;
#endif

    private void Update()
    {
#if UNITY_EDITOR
        if (AutopilotSprintDrill)
        {
            UpdateSprintDrill();
            return;
        }

        if (AutopilotVaultDrill)
        {
            UpdateVaultDrill();
            return;
        }

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
            SneakHeld = AutopilotSneak;
            AimHeld = AutopilotAim;
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
#if UNITY_EDITOR
        if (AutopilotFire) FireHeld = true;
        if (AutopilotAim) AimHeld = true;
#endif
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
