using Unity.Netcode;
using UnityEngine;

/// <summary>Ce qu'est une cible de la Fosse : à neutraliser, ou à épargner (le civil de Modern Warfare 2).</summary>
public enum CourseTargetRole : byte
{
    Hostile = 0,
    Civil = 1,
}

/// <summary>Où en est une cible de la Fosse : cachée (avant son passage), debout, ou tombée.</summary>
public enum CourseTargetState : byte
{
    Hidden = 0,
    Up = 1,
    Down = 2,
}

/// <summary>L'état publié d'une cible : où elle en est, quand elle s'est levée, quand elle est tombée
/// (instants serveur). Chaque machine en déduit sa pose (CourseTarget.ApplyPose).</summary>
public struct CourseTargetMotion : INetworkSerializeByMemcpy
{
    public CourseTargetState state;
    public double raisedAt;
    public double downAt;
}

/// <summary>
/// Une cible de la Fosse (2026-10-16, le parcours chronométré du stand de tir, sur le modèle de « The
/// Pit » de Modern Warfare 2) : un mannequin (TrainingDummy, la vie et la surface touchable d'un robot)
/// qui reste caché jusqu'à ce que le joueur entre dans sa section, se lève d'un coup sur ses pieds
/// comme une cible de stand, et tombe en arrière quand il est neutralisé, jusqu'au départ suivant. Il ne
/// se relève pas seul (TrainingDummy.Regenerates est faux). Une cible peut aller et venir sur un rail.
///
/// Le serveur décide (ShootingCourse) ; la pose est une fonction de l'état publié et de l'heure du
/// serveur (PoseAngle, RailPosition), la même partout. Cachée ou tombée, ni sa surface touchable ni son
/// corps ne répondent : on ne touche que ce qui est debout.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrainingDummy))]
public class CourseTarget : NetworkBehaviour
{
    /// <summary>Durée (s) du lever : la cible bascule de couchée à debout.</summary>
    public const float RiseTime = 0.25f;
    /// <summary>Durée (s) de la chute d'une cible neutralisée.</summary>
    public const float FallTime = 0.35f;

    [SerializeField] private CourseTargetRole role = CourseTargetRole.Hostile;
    [Tooltip("La section de la Fosse dont l'entrée fait lever la cible (ShootingCourse).")]
    [SerializeField] private int section;
    [Tooltip("La cible va et vient entre sa position et railEnd.")]
    [SerializeField] private bool moves;
    [SerializeField] private Vector3 railEnd;
    [Tooltip("Vitesse (m/s) sur le rail.")]
    [SerializeField] private float railSpeed = 2.2f;

    private readonly NetworkVariable<CourseTargetMotion> motion = new NetworkVariable<CourseTargetMotion>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private TrainingDummy dummy;
    private Vector3 basePosition;
    private Quaternion baseRotation;
    private Renderer[] renderers;
    private Collider[] bodyColliders;
    private bool hasShownState;
    private CourseTargetState shownState;

    public CourseTargetRole Role => role;
    public bool IsHostile => role == CourseTargetRole.Hostile;
    public int Section => section;
    public TrainingDummy Dummy => dummy;
    public Health Health => dummy != null ? dummy.Health : null;
    public CourseTargetMotion Motion => motion.Value;
    public CourseTargetState State => motion.Value.state;

    private void Awake()
    {
        dummy = GetComponent<TrainingDummy>();
        basePosition = transform.position;
        baseRotation = transform.rotation;
        bodyColliders = GetComponents<Collider>();
    }

    /// <summary>Serveur : la cible se cache et retrouve toute sa vie (un nouveau départ).</summary>
    public void ServerHide()
    {
        if (!IsServer) return;
        motion.Value = default;
        if (dummy != null) dummy.ServerRestore();
    }

    /// <summary>Serveur : la cible se lève (le joueur entre dans sa section).</summary>
    public void ServerRaise(double now)
    {
        if (!IsServer || motion.Value.state != CourseTargetState.Hidden) return;
        motion.Value = new CourseTargetMotion { state = CourseTargetState.Up, raisedAt = now };
    }

    /// <summary>Serveur : une cible debout dont la vie est tombée à zéro tombe. Rend vrai si elle vient de tomber.</summary>
    public bool ServerUpdate(double now)
    {
        if (!IsServer || motion.Value.state != CourseTargetState.Up || Health == null || !Health.IsDead) return false;
        CourseTargetMotion m = motion.Value;
        m.state = CourseTargetState.Down;
        m.downAt = now;
        motion.Value = m;
        return true;
    }

    /// <summary>
    /// Pose la cible selon son état à l'instant `now` : bascule autour de ses pieds, place sur le rail,
    /// visibilité, surface touchable et corps. Rend vrai si le transform a bougé (la physique doit alors
    /// être resynchronisée : Physics.autoSyncTransforms vaut false).
    /// </summary>
    public bool ApplyPose(CourseTargetMotion m, double now)
    {
        if (dummy == null) return false;
        float angle = PoseAngle(m, now);
        Vector3 position = basePosition;
        if (moves && m.state != CourseTargetState.Hidden)
        {
            double until = m.state == CourseTargetState.Down ? m.downAt : now;
            position = RailPosition(basePosition, railEnd, railSpeed, until - m.raisedAt);
        }
        Quaternion rotation = baseRotation * Quaternion.Euler(-angle, 0f, 0f);
        bool moved = transform.position != position || transform.rotation != rotation;
        if (moved) transform.SetPositionAndRotation(position, rotation);

        if (!hasShownState || shownState != m.state)
        {
            hasShownState = true;
            shownState = m.state;
            bool up = m.state == CourseTargetState.Up;
            if (dummy.Hitbox != null) dummy.Hitbox.SetQueryable(up);
            foreach (Collider c in bodyColliders) if (c != null) c.enabled = up;
            SetVisible(m.state != CourseTargetState.Hidden);
        }
        return moved;
    }

    private void SetVisible(bool visible)
    {
        // L'apparence est créée par TrainingDummy.Awake, peut-être après le nôtre : cherchée à l'usage.
        if (renderers == null || renderers.Length == 0)
        {
            Transform look = transform.Find("Apparence");
            renderers = look != null ? look.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        }
        foreach (Renderer r in renderers) if (r != null) r.forceRenderingOff = !visible;
    }

    /// <summary>
    /// L'inclinaison (degrés, vers l'arrière, autour des pieds) d'une cible : 90 couchée (cachée), de 90
    /// à 0 pendant le lever, 0 debout, de 0 à 90 pendant la chute (qui accélère). Fonction pure, testée.
    /// </summary>
    public static float PoseAngle(CourseTargetMotion m, double now)
    {
        switch (m.state)
        {
            case CourseTargetState.Up:
            {
                float t = Mathf.Clamp01((float)((now - m.raisedAt) / RiseTime));
                return 90f * (1f - t * t * (3f - 2f * t));
            }
            case CourseTargetState.Down:
            {
                float t = Mathf.Clamp01((float)((now - m.downAt) / FallTime));
                return 90f * t * t;
            }
            default:
                return 90f;
        }
    }

    /// <summary>
    /// La position sur un rail de `a` à `b`, parcouru à `speed` m/s en aller-retour depuis `elapsed`
    /// secondes. Fonction pure, testée.
    /// </summary>
    public static Vector3 RailPosition(Vector3 a, Vector3 b, float speed, double elapsed)
    {
        float length = Vector3.Distance(a, b);
        if (length < 1e-4f || speed <= 0f || double.IsNaN(elapsed) || elapsed <= 0.0) return a;
        double travelled = speed * elapsed % (2.0 * length);
        float along = (float)(travelled <= length ? travelled : 2.0 * length - travelled);
        return Vector3.Lerp(a, b, along / length);
    }
}
