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

    private static readonly int MoveXId = Animator.StringToHash("MoveX");
    private static readonly int MoveYId = Animator.StringToHash("MoveY");
    private static readonly int StanceId = Animator.StringToHash("Stance");

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

        float reference = ReferenceSpeed(locomotion.CurrentStance);
        Vector2 target = new Vector2(local.x, local.z) / Mathf.Max(0.01f, reference);

        // Borne volontaire. Une resynchronisation de réconciliation TÉLÉPORTE le propriétaire de
        // quelques centimètres en une frame : dérivée telle quelle, cette téléportation vaut
        // plusieurs mètres par seconde et ferait sursauter l'animation à chaque recalage. On
        // plafonne donc à un peu plus que le sprint, ce qui laisse passer tout mouvement réel.
        target = Vector2.ClampMagnitude(target, 2.5f);

        smoothedMove = Vector2.Lerp(smoothedMove, target, 1f - Mathf.Exp(-blendSmoothing * dt));

        animator.SetFloat(MoveXId, smoothedMove.x);
        animator.SetFloat(MoveYId, smoothedMove.y);

        // La posture vient d'une NetworkVariable, donc elle est juste sur toutes les instances
        // sans traitement particulier.
        animator.SetInteger(StanceId, (int)locomotion.CurrentStance);
    }

    private float ReferenceSpeed(PlayerLocomotion.Stance stance)
    {
        if (stance == PlayerLocomotion.Stance.Crouching) return crouchingReference;
        if (stance == PlayerLocomotion.Stance.Prone) return proneReference;
        return standingReference;
    }
}
