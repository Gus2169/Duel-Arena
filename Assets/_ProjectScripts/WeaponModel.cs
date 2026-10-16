using UnityEngine;

/// <summary>
/// Le modèle visible d'une arme (2026-10-11), posé sur la racine de son préfab : poignée pistolet à
/// l'origine, canon vers +Z. C'est ce qui permet d'accrocher n'importe quelle arme à la main droite
/// avec le même décalage que les autres (HeldWeapons, repère « PriseArme »), et de la placer en vue
/// subjective.
///
/// Depuis le 2026-10-15, chaque classe d'arme a trois modèles au choix de l'armurerie (le stock, la
/// série 30, la version laser : GDD § 6), tous construits dans Blender (Tools/Blender) et importés par
/// Duel Arena > Armes > Importer les armes de Blender, avec leurs finitions (WeaponFinishes).
///
/// Catégorie C pure : rien de la simulation, du tir ni du hitbox ne le lit. Le tir part toujours de
/// la caméra.
/// </summary>
[DisallowMultipleComponent]
public class WeaponModel : MonoBehaviour
{
    /// <summary>Les familles de modèles. WeaponData.ModelPrefabFor commence par le modèle par défaut de
    /// la classe, puis suit cet ordre : la famille d'un modèle se lit ici, jamais sur son indice.</summary>
    public enum Family : byte { Stock = 0, Serie30 = 1, Laser = 2 }

    [Tooltip("La désignation de l'arme (« FA-07 »), affichée par l'armurerie. Deux modèles différents n'ont jamais la même : HeldWeapons s'en sert pour savoir si le modèle en main est le bon.")]
    [SerializeField] private string designation = "";

    [SerializeField] private Family family = Family.Stock;

    [Tooltip("Repère de visée : sur la ligne de mire, +Z dans l'axe du canon. La vue subjective le place au centre de l'écran en visée, et le buste s'aligne sur lui (vu par les autres).")]
    [SerializeField] private Transform sight;

    [Tooltip("Bout du canon (futurs traçants partant de l'arme).")]
    [SerializeField] private Transform muzzle;

    [Tooltip("En visée, distance de l'œil à la mire (m). Courte pour un viseur à écran ou une hausse à anneau (on regarde de près), plus longue pour un pistolet tenu à bout de bras.")]
    [SerializeField] private float aimEyeDistance = 0.10f;

    [Tooltip("Tenue de pistolet (raygun) : à deux mains, bras tendus, au lieu de la crosse à l'épaule. Le haut du corps joue alors les poses de pistolet (paramètre « Pistolet » de l'Animator, PlayerAnimator), pour les autres comme sur les bras de la vue subjective.")]
    [SerializeField] private bool pistolHold;

    [Tooltip("Sous le garde-main, là où la paume gauche tient l'arme (repère « MainGauche » de Blender) : la cible d'une future IK de la main gauche. Absent d'un pistolet.")]
    [SerializeField] private Transform leftHand;

    [Tooltip("Sur un poignet de crosse droit (FP-30, FAP-30), là où la main droite tient l'arme (repère « MainDroite ») ; absent quand elle tient une poignée pistolet, à l'origine.")]
    [SerializeField] private Transform rightHand;

    public string Designation => designation;
    public Family ModelFamily => family;
    public Transform Sight => sight;
    public Transform Muzzle => muzzle;
    public float AimEyeDistance => aimEyeDistance;
    public bool PistolHold => pistolHold;
    public Transform LeftHand => leftHand;
    public Transform RightHand => rightHand;

    /// <summary>Le nom d'une famille, tel que l'armurerie l'affiche.</summary>
    public static string FamilyLabel(Family f) => f switch
    {
        Family.Stock => "stock",
        Family.Serie30 => "série 30",
        Family.Laser => "laser",
        _ => f.ToString(),
    };

    /// <summary>Ce que l'armurerie dit d'une famille (Bible § 12).</summary>
    public static string FamilyDescription(Family f) => f switch
    {
        Family.Stock => "Une arme du stock militaire du complexe, sobre et éprouvée.",
        Family.Serie30 => "La dernière dotation du complexe, livrée peu avant la disparition des humains et retrouvée dans ses caisses.",
        Family.Laser => "Un prototype du programme d'essai, à part des armes à balles.",
        _ => "",
    };

#if UNITY_EDITOR
    /// <summary>Réglage par l'outil qui construit les préfabs d'armes (Editor seulement).</summary>
    public void EditorSetup(Transform sightPoint, Transform muzzlePoint, float eyeDistance, bool pistol = false)
    {
        sight = sightPoint;
        muzzle = muzzlePoint;
        aimEyeDistance = eyeDistance;
        pistolHold = pistol;
    }

    /// <summary>Réglage par l'importeur des armes de Blender (Editor seulement).</summary>
    public void EditorSetup(string code, Family modelFamily, Transform sightPoint, Transform muzzlePoint, float eyeDistance,
                            bool pistol, Transform leftHandPoint, Transform rightHandPoint)
    {
        designation = code;
        family = modelFamily;
        EditorSetup(sightPoint, muzzlePoint, eyeDistance, pistol);
        leftHand = leftHandPoint;
        rightHand = rightHandPoint;
    }
#endif
}
