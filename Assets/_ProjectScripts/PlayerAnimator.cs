using UnityEngine;

/// <summary>
/// Pilote l'Animator du personnage à partir de l'état affiché du joueur.
///
/// CATÉGORIE C — purement cosmétique. Ce script LIT, il n'écrit jamais rien que la simulation
/// relise. C'est la règle centrale du projet côté animation : l'animation AFFICHE, elle ne décide
/// pas. Un Animator n'est pas déterministe entre machines (blending, vitesse de lecture,
/// LateUpdate), donc tout ce qui en dépendrait ferait diverger les instances.
///
/// Contrairement aux autres scripts cosmétiques, il tourne sur TOUTES les instances et non
/// seulement chez le propriétaire : il faut bien voir son adversaire bouger.
///
/// C'est précisément ce qui dicte d'où vient la vitesse. `currentVelocity` n'existe que là où la
/// simulation tourne (propriétaire et serveur) ; chez un spectateur elle vaudrait zéro en
/// permanence et l'adversaire glisserait sans bouger les jambes. Le DÉPLACEMENT DU TRANSFORM,
/// lui, est juste dans les quatre cas réseau — y compris l'interpolation du spectateur, qui est
/// justement ce qu'on veut montrer.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerLocomotion))]
public class PlayerAnimator : MonoBehaviour
{
    [Tooltip("Animator du personnage (enfant Model). Laissé vide, il est cherché dans les enfants.")]
    [SerializeField] private Animator animator;

    [Header("Vitesses de référence par posture")]
    [Tooltip("Vitesse à laquelle l'arbre de mélange vaut 1 (allure nominale). Doit suivre walkSpeed de PlayerLocomotion.")]
    [SerializeField] private float standingReference = 4.4f;
    [SerializeField] private float crouchingReference = 2.64f;
    [SerializeField] private float proneReference = 1.1f;

    [Tooltip("Lissage des paramètres de mélange. Purement visuel : évite qu'un à-coup d'une frame ne fasse claquer l'animation.")]
    [SerializeField] private float blendSmoothing = 12f;

    [Header("Cadence de lecture")]
    [Tooltip("Plancher du multiplicateur de vitesse de lecture. Ne descend pas à 0 : il s'applique aussi à l'animation d'attente, qui doit continuer de respirer à l'arrêt.")]
    [SerializeField] private float minPlaybackSpeed = 0.75f;

    [Tooltip("Cadence à l'allure nominale, par posture. Les clips Mixamo ne sont pas authorés à la même échelle que les vitesses du jeu : c'est le réglage à toucher si les pieds patinent ou si la foulée paraît frénétique.")]
    [SerializeField] private float standingPlaybackScale = 0.85f;
    [SerializeField] private float crouchingPlaybackScale = 1f;
    [SerializeField] private float pronePlaybackScale = 1.4f;

    private static readonly int MoveXId = Animator.StringToHash("MoveX");
    private static readonly int MoveYId = Animator.StringToHash("MoveY");
    private static readonly int StanceId = Animator.StringToHash("Stance");
    private static readonly int SpeedMultId = Animator.StringToHash("SpeedMult");

    private PlayerLocomotion locomotion;
    private Vector3 previousPosition;
    private Vector2 smoothedMove;

    private void Awake()
    {
        locomotion = GetComponent<PlayerLocomotion>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        previousPosition = transform.position;
    }

    /// <summary>
    /// LateUpdate et pas Update : Move() est appelée depuis Update(), donc la position n'est
    /// définitive qu'après. Lire trop tôt donnerait la vitesse de la frame précédente.
    /// </summary>
    private void LateUpdate()
    {
        if (animator == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 delta = transform.position - previousPosition;
        previousPosition = transform.position;
        delta.y = 0f;

        Vector3 local = transform.InverseTransformDirection(delta / dt);

        // NetworkedStance et non CurrentStance : cette dernière n'est mise à jour que dans Move(),
        // qu'un spectateur n'appelle jamais. Elle y resterait bloquée sur Standing, et l'adversaire
        // n'aurait jamais d'animation accroupie ni allongée. Symptôme vécu le 2026-09-29.
        PlayerLocomotion.Stance stance = locomotion.NetworkedStance;
        float reference = ReferenceSpeed(stance);
        Vector2 target = new Vector2(local.x, local.z) / Mathf.Max(0.01f, reference);

        // Borne volontaire. Une resynchronisation de réconciliation TÉLÉPORTE le propriétaire de
        // quelques centimètres en une frame : dérivée telle quelle, cette téléportation vaut
        // plusieurs mètres par seconde et ferait sursauter l'animation à chaque recalage. On
        // plafonne donc à un peu plus que le sprint, ce qui laisse passer tout mouvement réel.
        target = Vector2.ClampMagnitude(target, 2.5f);

        smoothedMove = Vector2.Lerp(smoothedMove, target, 1f - Mathf.Exp(-blendSmoothing * dt));

        animator.SetFloat(MoveXId, smoothedMove.x);
        animator.SetFloat(MoveYId, smoothedMove.y);

        animator.SetInteger(StanceId, (int)stance);

        // Un arbre de mélange NE modifie PAS la cadence de ses clips. À mi-vitesse il mélange
        // l'attente et la course, mais la course joue à 100 % de sa cadence pendant que le corps
        // n'avance qu'à moitié : les jambes s'agitent sans que le personnage suive. On accorde
        // donc la vitesse de LECTURE au déplacement réel.
        //
        // 🚨 PLAFONNÉ À 1, et c'est le point qui n'était pas évident. Au-delà de l'allure
        // nominale, ce n'est pas la cadence qui doit monter mais le CLIP qui change : à MoveY=2
        // l'arbre joue Sprint Forward, déjà authorée pour sprinter. Une première version plafonnait
        // à 1,5 et accélérait donc de 50 % une animation qui n'en avait aucun besoin — le sprint
        // paraissait frénétique.
        //
        // Le plancher, lui, ne descend pas à zéro : ce multiplicateur pilote l'état entier,
        // animation d'attente comprise, qui doit continuer de respirer à l'arrêt.
        float cadence = Mathf.Clamp(smoothedMove.magnitude, minPlaybackSpeed, 1f)
                        * PlaybackScale(stance);
        animator.SetFloat(SpeedMultId, cadence);
    }

    /// <summary>Accorde la cadence d'une posture à ses clips. Mixamo n'authore pas ses animations
    /// à l'échelle des vitesses de ce jeu : le ramper est plus lent que la vitesse prone réelle,
    /// la course debout plus rapide que la marche. C'est un réglage de RESSENTI, à ajuster en
    /// jouant et non par calcul.</summary>
    private float PlaybackScale(PlayerLocomotion.Stance stance)
    {
        if (stance == PlayerLocomotion.Stance.Crouching) return crouchingPlaybackScale;
        if (stance == PlayerLocomotion.Stance.Prone) return pronePlaybackScale;
        return standingPlaybackScale;
    }

    private float ReferenceSpeed(PlayerLocomotion.Stance stance)
    {
        if (stance == PlayerLocomotion.Stance.Crouching) return crouchingReference;
        if (stance == PlayerLocomotion.Stance.Prone) return proneReference;
        return standingReference;
    }
}
