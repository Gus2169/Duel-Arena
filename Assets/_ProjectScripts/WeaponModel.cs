using UnityEngine;

/// <summary>
/// Le modèle visible d'une arme (2026-10-11), posé sur la racine de son préfab : poignée pistolet à
/// l'origine, canon vers +Z. C'est ce qui permet d'accrocher n'importe quelle arme à la main droite
/// avec le même décalage que le fusil d'assaut (HeldWeapons), et de la placer en vue subjective.
///
/// Catégorie C pure : rien de la simulation, du tir ni du hitbox ne le lit. Le tir part toujours de
/// la caméra.
/// </summary>
[DisallowMultipleComponent]
public class WeaponModel : MonoBehaviour
{
    [Tooltip("Repère de visée : sur la ligne de mire, +Z dans l'axe du canon. La vue subjective le place au centre de l'écran en visée, et le buste s'aligne sur lui (vu par les autres).")]
    [SerializeField] private Transform sight;

    [Tooltip("Bout du canon (futurs traçants partant de l'arme).")]
    [SerializeField] private Transform muzzle;

    [Tooltip("En visée, distance de l'œil à la mire (m). Courte pour un viseur à écran (on regarde l'écran de près), plus longue pour une mire métallique (on vise par-dessus l'arme).")]
    [SerializeField] private float aimEyeDistance = 0.10f;

    [Tooltip("Tenue de pistolet (raygun) : à deux mains, bras tendus, au lieu de la crosse à l'épaule. Le haut du corps joue alors les poses de pistolet (paramètre « Pistolet » de l'Animator, PlayerAnimator), pour les autres comme sur les bras de la vue subjective.")]
    [SerializeField] private bool pistolHold;

    public Transform Sight => sight;
    public Transform Muzzle => muzzle;
    public float AimEyeDistance => aimEyeDistance;
    public bool PistolHold => pistolHold;

#if UNITY_EDITOR
    /// <summary>Réglage par l'outil qui construit les préfabs d'armes (Editor seulement).</summary>
    public void EditorSetup(Transform sightPoint, Transform muzzlePoint, float eyeDistance, bool pistol = false)
    {
        sight = sightPoint;
        muzzle = muzzlePoint;
        aimEyeDistance = eyeDistance;
        pistolHold = pistol;
    }
#endif
}
