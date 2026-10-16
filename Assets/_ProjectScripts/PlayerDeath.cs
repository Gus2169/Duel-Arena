using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ce qu'on voit d'un robot tué (2026-10-12, demandé par l'utilisateur : « au minimum qu'il
/// tombe ») : son épave tombe en ragdoll (RobotCorpse), poussée par le coup qui l'a tué, et le
/// modèle vivant est masqué jusqu'à la manche suivante. Le joueur mort, lui, quitte ses yeux : ses
/// bras disparaissent et sa caméra recule pour regarder son épave, qu'il fait tourner à la souris.
///
/// La mort se lit sur Health.IsDead, par SONDAGE à chaque image comme RoundManager : la vie est
/// une variable réseau, juste sur toutes les machines. Seule l'impulsion du coup voyage à part
/// (KilledClientRpc) : elle peut arriver juste avant ou juste après la mort, et les deux ordres sont
/// gérés.
///
/// Catégorie C pure : rien de la simulation ne lit l'épave ni la caméra du mort. Ce qui, de la
/// mort, touche la simulation (le survivant traverse la place du mort, ses tirs ne s'arrêtent plus
/// sur une surface touchable invisible) est dans PlayerLocomotion.ApplyLifeState.
/// </summary>
[DefaultExecutionOrder(500)] // après PlayerCameraLook et FirstPersonView : la caméra du mort a le dernier mot
[DisallowMultipleComponent]
public class PlayerDeath : NetworkBehaviour
{
    [Header("Épave (ressenti)")]
    [Tooltip("Multiplie l'impulsion du coup mortel envoyée par le serveur (N·s). Le GDD veut des morts exagérées.")]
    [SerializeField] private float impulseScale = 1f;

    [Header("Son (2026-10-13)")]
    [Tooltip("Le son signature de la mort (GDD § 6), entendu de tous à l'épave, et le bruit de ses pièces qui heurtent le sol.")]
    [SerializeField] private GameSounds sounds;

    [Header("Vue du joueur mort (ressenti)")]
    [Tooltip("La caméra du joueur (Main Camera du prefab). Trouvée par son nom si laissée vide.")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Distance de la caméra à l'épave (m).")]
    [SerializeField] private float cameraDistance = 3.2f;
    [Tooltip("Hauteur de départ de la caméra au-dessus de l'épave (degrés).")]
    [SerializeField] private float cameraStartPitch = 28f;
    [Tooltip("Temps (s) pour passer des yeux du robot à la vue extérieure.")]
    [SerializeField] private float cameraBlendTime = 0.7f;
    [Tooltip("Rotation (degrés) par unité de souris, avant la sensibilité des Options.")]
    [SerializeField] private float orbitSensitivity = 0.12f;

    private Health health;
    private Animator modelAnimator;
    private Transform modelRoot;
    private Renderer[] hiddenRenderers = System.Array.Empty<Renderer>();
    private PlayerPaint paint;
    private FirstPersonView firstPersonView;
    private PlayerInputReader input;
    private RobotCorpse corpse;
    private bool shownDead;

    private readonly MotionWindow motion = new MotionWindow();

    // L'impulsion arrivée avant la mort (la RPC a doublé la variable de vie).
    private bool pendingImpulse;
    private Vector3 pendingPoint;
    private Vector3 pendingForce;
    private float pendingAt;

    // La vue du mort.
    private bool deathCamera;
    private float deathCameraTime;
    private Vector3 cameraStartPosition;
    private Quaternion cameraStartRotation;
    private Vector3 cameraSavedLocalPosition;
    private Quaternion cameraSavedLocalRotation;
    private float orbitYaw;
    private float orbitPitch;

    private static readonly int WorldMask = 1; // Default : le décor arrête la caméra, pas les épaves

    /// <summary>L'épave affichée, null tant que le joueur est en vie.</summary>
    public RobotCorpse Corpse => corpse;

    /// <summary>
    /// La vitesse d'un robot sur sa dernière fraction de seconde, que son épave hérite. Mesurée sur
    /// une seule image, elle s'emballait chez l'hôte : le robot d'un client y avance au rythme
    /// d'arrivée de ses inputs (dette n° 30), par à-coups, et une image de rattrapage valait deux ou
    /// trois fois la vraie vitesse (l'épave partait projetée, 2026-10-12). Une téléportation (manche,
    /// recalage) n'est pas une vitesse : elle remet la mesure à zéro. Sans état Unity, testée.
    /// </summary>
    public class MotionWindow
    {
        /// <summary>La durée sur laquelle la vitesse se mesure (s).</summary>
        public const float Window = 0.25f;
        /// <summary>Au-delà de ce saut entre deux images (m), c'est une téléportation.</summary>
        public const float TeleportDistance = 1.5f;

        private const int Capacity = 128;
        private readonly float[] times = new float[Capacity];
        private readonly Vector3[] positions = new Vector3[Capacity];
        private int newest = -1;
        private int count;

        public void Add(float time, Vector3 position)
        {
            if (count > 0 && (position - positions[newest]).sqrMagnitude > TeleportDistance * TeleportDistance) count = 0;
            newest = (newest + 1) % Capacity;
            times[newest] = time;
            positions[newest] = position;
            count = Mathf.Min(count + 1, Capacity);
        }

        /// <summary>Le déplacement moyen sur les `Window` dernières secondes (zéro sans recul).</summary>
        public Vector3 Velocity
        {
            get
            {
                if (count < 2) return Vector3.zero;
                float now = times[newest];
                int oldest = newest;
                for (int i = 1; i < count; i++)
                {
                    int index = (newest - i + Capacity) % Capacity;
                    oldest = index;
                    if (now - times[index] >= Window) break;
                }
                float span = now - times[oldest];
                return span > 1e-3f ? (positions[newest] - positions[oldest]) / span : Vector3.zero;
            }
        }
    }

    private void Awake()
    {
        health = GetComponent<Health>();
        paint = GetComponent<PlayerPaint>();
        firstPersonView = GetComponent<FirstPersonView>();
        input = GetComponent<PlayerInputReader>();
        modelRoot = transform.Find("Model");
        modelAnimator = modelRoot != null ? modelRoot.GetComponent<Animator>() : null;
        if (playerCamera == null)
        {
            foreach (Camera c in GetComponentsInChildren<Camera>(true))
            {
                if (c.name == "Main Camera") { playerCamera = c; break; }
            }
        }
    }

    private void Update()
    {
        motion.Add(Time.time, transform.position);

        bool dead = IsSpawned && health != null && health.IsDead;
        if (dead != shownDead)
        {
            if (dead) ShowDeath();
            else ShowAlive();
        }
    }

    private void LateUpdate()
    {
        if (deathCamera) UpdateDeathCamera();
    }

    public override void OnNetworkDespawn()
    {
        if (shownDead) ShowAlive();
    }

    public override void OnDestroy()
    {
        if (corpse != null) Destroy(corpse.gameObject);
        base.OnDestroy();
    }

    // ------------------------------------------------------------------
    // Mort et retour en jeu
    // ------------------------------------------------------------------

    private void ShowDeath()
    {
        shownDead = true;
        corpse = RobotCorpse.Spawn(modelAnimator, motion.Velocity);
        if (corpse != null && paint != null)
        {
            foreach (Renderer live in paint.PaintedRenderers) paint.PaintOnce(corpse.TwinOf(live));
        }

        // Le modèle vivant disparaît, armes et gant compris (y compris ceux créés au lancement).
        hiddenRenderers = modelRoot != null ? modelRoot.GetComponentsInChildren<Renderer>(true) : System.Array.Empty<Renderer>();
        foreach (Renderer r in hiddenRenderers) r.forceRenderingOff = true;

        if (corpse != null && pendingImpulse && Time.time - pendingAt < 1f) corpse.ApplyImpulse(pendingPoint, pendingForce);
        pendingImpulse = false;
        PlayDeathSound();

        if (IsOwner)
        {
            if (firstPersonView != null) firstPersonView.SetHidden(true);
            StartDeathCamera();
        }
    }

    private void ShowAlive()
    {
        shownDead = false;
        if (corpse != null) Destroy(corpse.gameObject);
        corpse = null;
        foreach (Renderer r in hiddenRenderers)
        {
            if (r != null) r.forceRenderingOff = false;
        }
        hiddenRenderers = System.Array.Empty<Renderer>();
        pendingImpulse = false;

        if (firstPersonView != null) firstPersonView.SetHidden(false);
        StopDeathCamera();
    }

    /// <summary>Le son signature de la mort (GDD § 6) : le robot s'éteint, dans un court-circuit,
    /// là où il tombe ; puis ses pièces qui heurtent le sol. Chaque machine le joue quand elle voit la
    /// mort (catégorie B : la vie est publiée par le serveur).</summary>
    private void PlayDeathSound()
    {
        if (sounds == null) return;
        Vector3 at = corpse != null ? corpse.Center : transform.position + Vector3.up;
        float far = 300f;
        sounds.deathShutdown?.PlayAt(at, sounds.deathMinDistance, far);
        sounds.deathCrackle?.PlayAt(at, sounds.deathMinDistance, far);
        if (corpse != null) corpse.EnableImpactSounds(sounds.corpseImpact);
    }

    /// <summary>Serveur : ce joueur vient d'être tué par un coup reçu en `point`, poussé de
    /// `impulse` (N·s). Diffusé à tous pour que l'épave parte dans le bon sens.</summary>
    public void ServerNotifyKilled(Vector3 point, Vector3 impulse)
    {
        if (!IsServer || !IsSpawned) return;
        KilledClientRpc(point, impulse);
    }

    [ClientRpc]
    private void KilledClientRpc(Vector3 point, Vector3 impulse)
    {
        Vector3 force = impulse * impulseScale;
        if (shownDead && corpse != null)
        {
            corpse.ApplyImpulse(point, force);
            return;
        }
        pendingImpulse = true;
        pendingPoint = point;
        pendingForce = force;
        pendingAt = Time.time;
    }

    // ------------------------------------------------------------------
    // La vue du joueur mort
    // ------------------------------------------------------------------

    private void StartDeathCamera()
    {
        if (playerCamera == null) return;
        Transform cam = playerCamera.transform;
        cameraSavedLocalPosition = cam.localPosition;
        cameraSavedLocalRotation = cam.localRotation;
        cameraStartPosition = cam.position;
        cameraStartRotation = cam.rotation;
        // Derrière le robot, comme il regardait : on voit son épave et, au-delà, celui qui l'a eu.
        orbitYaw = cam.eulerAngles.y;
        orbitPitch = cameraStartPitch;
        deathCameraTime = 0f;
        deathCamera = true;
    }

    private void StopDeathCamera()
    {
        if (!deathCamera) return;
        deathCamera = false;
        if (playerCamera == null) return;
        playerCamera.transform.localPosition = cameraSavedLocalPosition;
        playerCamera.transform.localRotation = cameraSavedLocalRotation;
    }

    private void UpdateDeathCamera()
    {
        if (playerCamera == null) return;
        deathCameraTime += Time.deltaTime;

        Vector2 look = input != null ? input.LookInput : Vector2.zero;
        float sensitivity = orbitSensitivity * GameSettings.SensitivityMultiplier;
        orbitYaw += look.x * sensitivity;
        orbitPitch = Mathf.Clamp(orbitPitch - look.y * sensitivity, 5f, 75f);

        Vector3 target = (corpse != null ? corpse.Center : transform.position + Vector3.up * 0.9f) + Vector3.up * 0.3f;
        Vector3 direction = Quaternion.Euler(orbitPitch, orbitYaw, 0f) * Vector3.back;
        float distance = cameraDistance;
        // Le décor arrête la caméra : elle ne passe jamais derrière un mur.
        if (Physics.SphereCast(target, 0.2f, direction, out RaycastHit hit, cameraDistance, WorldMask, QueryTriggerInteraction.Ignore))
            distance = Mathf.Max(0.4f, hit.distance - 0.05f);

        Vector3 position = target + direction * distance;
        Quaternion rotation = Quaternion.LookRotation(target - position, Vector3.up);
        float t = Mathf.SmoothStep(0f, 1f, deathCameraTime / Mathf.Max(0.01f, cameraBlendTime));
        playerCamera.transform.SetPositionAndRotation(Vector3.Lerp(cameraStartPosition, position, t),
                                                      Quaternion.Slerp(cameraStartRotation, rotation, t));
    }
}
