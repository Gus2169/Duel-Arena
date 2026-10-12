using UnityEngine;

/// <summary>
/// Recueille le déplacement de racine que l'Animator CALCULE, sans jamais l'appliquer (2026-10-09).
///
/// La position appartient à Move() : l'Animator ne déplace jamais le personnage. Mais la vitesse à
/// laquelle ses clips font avancer le corps est exactement ce qu'il faut connaître pour que les
/// pieds ne patinent pas : PlayerAnimator s'en sert pour accorder la cadence des jambes à la vitesse
/// réelle. La seule présence d'OnAnimatorMove fait calculer ce déplacement à l'Animator et le lui
/// remet au lieu de l'appliquer : on le lit, on le garde, et rien ne bouge.
///
/// Sans ce composant, Animator.velocity vaut zéro dès que applyRootMotion est faux (vérifié le
/// 2026-10-09). Il doit vivre sur l'objet de l'Animator, seul endroit où Unity appelle
/// OnAnimatorMove ; PlayerAnimator l'y ajoute lui-même. Catégorie C : rien ne le relit dans la
/// simulation.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class AnimatorRootMotionProbe : MonoBehaviour
{
    private Animator animator;

    /// <summary>Vitesse de racine de la dernière évaluation, dans le repère du monde (m/s) : à quelle
    /// vitesse, et dans quelle direction, l'animation faisait avancer les pieds sur le sol.</summary>
    public Vector3 Velocity { get; private set; }

    private void Awake() => animator = GetComponent<Animator>();

    private void OnAnimatorMove()
    {
        // Lu, jamais appliqué : ni position, ni rotation.
        Velocity = animator.velocity;
    }
}
