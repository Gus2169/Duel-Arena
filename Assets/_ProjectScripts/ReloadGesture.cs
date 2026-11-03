using UnityEngine;

/// <summary>
/// Un rechargement où le chargeur quitte l'arme (2026-10-29, un par arme à chargeur) : le geste que joue l'état
/// Recharge de l'Animator pour les armes qui le prennent, et les étapes où le chargeur vide tombe, où le neuf apparaît
/// dans la main gauche et où il rentre, en part du rechargement. Le clip est calculé dans Blender
/// (Tools/Blender/animations/rechargement_chargeur.py), sur les étapes sonores de l'arme (WeaponData.reloadCues) ;
/// Duel Arena > Animations > Importer les rechargements à chargeur l'importe, le branche dans l'Animator et remplit
/// ce geste. On réexporte de Blender et on relance le menu ; on ne retouche pas ces valeurs à la main.
///
/// Le clip a la forme de Y Bot@Reloading (3,3 s, la partie utile de 0,4 à 2,5 s) : l'état Recharge y entre au même
/// instant et le lit à la même vitesse (ReloadSpeed, PlayerAnimator). Catégorie C : rien de la simulation ne le lit,
/// le chargeur montré se déduit du geste que joue l'Animator (MagazineSwap).
///
/// Une arme qui s'ouvre (le Big D du Golden Gun, 2026-11-03) a un geste de la même forme, sans chargeur :
/// BreakActionGesture.
/// </summary>
[CreateAssetMenu(menuName = "Duel Arena/Geste de rechargement", fileName = "Rechargement")]
public class ReloadGesture : ScriptableObject
{
    [Tooltip("La valeur du paramètre GesteRecharge de l'Animator qui fait jouer ce geste à l'état Recharge (0 : le geste commun, Y Bot@Reloading).")]
    public int animatorValue = 1;

    [Tooltip("Les armes qui le prennent.")]
    public WeaponData[] weapons = new WeaponData[0];

    [Header("Les étapes, en part du rechargement (la fiche du clip)")]
    [Tooltip("Le chargeur vide tombe du puits (l'étape sonore « chargeur retiré »).")]
    [Range(0f, 1f)] public float drop = 0.04f;
    [Tooltip("La main gauche prend le chargeur neuf au porte-chargeur : il apparaît dans sa main.")]
    [Range(0f, 1f)] public float grab = 0.26f;
    [Tooltip("Le chargeur neuf est sous le puits, dans son axe : il va de la main à sa place.")]
    [Range(0f, 1f)] public float approach = 0.40f;
    [Tooltip("Le chargeur neuf est en place (l'étape sonore « chargeur inséré »).")]
    [Range(0f, 1f)] public float seated = 0.45f;

    [Header("Le chargeur dans la main")]
    [Tooltip("Le repère de l'arme vu de l'os de la main gauche quand le chargeur neuf entre (le clip importé, échantillonné sur le Y Bot à l'étape « en place ») : là où la main tient le chargeur, rapporté à l'arme.")]
    public Vector3 weaponInHandPosition;
    public Quaternion weaponInHandRotation = Quaternion.identity;
    [Tooltip("Le point de la paume pour lequel le geste a été calculé (celui du CT-01, WeaponGrip.Settings.palmPoints) : une autre main tient le chargeur à sa propre paume.")]
    public Vector3 designPalm = new Vector3(0.010f, 0.050f, 0.021f);

    [Header("Les pièces d'armement (la fiche du clip)")]
    [Tooltip("Les pièces du modèle d'arme qui reculent avec la main gauche, par leur chemin depuis la racine du modèle : la poignée d'armement, le levier, la culasse, ou la glissière et ce qu'elle porte (les organes de visée, le point d'accroche du viseur). Celles du même nom dans un accessoire monté reculent avec elles (le canon court du PA-05 porte sa propre glissière).")]
    public string[] armingParts = new string[0];
    [Tooltip("De combien elles reculent (m), dans l'axe du canon.")]
    public float armingTravel;
    [Tooltip("En part du rechargement : la main les prend ; les a tirées à fond ; les lâche (une poignée revient seule) ou les ramène (une culasse) ; elles sont revenues.")]
    public float armingGrab, armingPulled, armingRelease, armingReturned;

    [Header("La vue subjective pendant l'armement (MagazineReloadImporter.ArmingViews)")]
    [Tooltip("La paume sur la pièce d'armement au repos, dans le repère du modèle d'arme (la fiche du clip, « levier ») : la vue subjective la garde dans le champ pendant l'armement.")]
    public Vector3 armingPalm;
    [Tooltip("Pendant l'armement, les bras se lèvent encore autour de l'œil, en plus du cadrage du rechargement (FirstPersonView), de quoi mettre la main sur la pièce tirée à ArmingPalmAngle au plus sous l'axe de la vue ; au plus de tant (degrés), et rien à 0. Sans ça, la pièce et la main passaient au ras du bas de l'écran ou dessous.")]
    public float armingViewPitchMax;
    [Tooltip("Et tournent de tant (degrés) vers la gauche.")]
    public float armingViewYaw;

    [Header("Le clip")]
    [Tooltip("Durée du clip (s), et sa partie utile : celle que l'état Recharge étale sur le rechargement.")]
    public float clipLength = 3.3f;
    public float usefulStart = 0.4f;
    public float usefulEnd = 2.5f;

    /// <summary>L'arme `data` prend-elle ce geste ?</summary>
    public bool AppliesTo(WeaponData data)
    {
        if (data == null || weapons == null) return false;
        foreach (WeaponData w in weapons)
            if (w == data) return true;
        return false;
    }

    /// <summary>La part du rechargement que montre le clip à l'instant `clipTime` (s) : 0 quand il commence, 1 quand
    /// le chargeur est plein ; en deçà et au-delà, les attentes du début et de la fin.</summary>
    public float ProgressAt(float clipTime) => (clipTime - usefulStart) / Mathf.Max(1e-4f, usefulEnd - usefulStart);

    /// <summary>
    /// De combien les pièces d'armement ont reculé (m) à la part `f` du rechargement : rien avant que la main les
    /// prenne, toute la course une fois tirées, jusqu'à ce qu'elle les lâche ou les ramène, rien une fois revenues.
    /// Entre deux, elles vont comme la main du clip entre deux étapes où elle s'arrête (une courbe en S). Fonction
    /// pure, testée.
    /// </summary>
    public float ArmingOffset(float f)
    {
        if (armingParts == null || armingParts.Length == 0 || armingTravel <= 0f || float.IsNaN(f)) return 0f;
        if (f <= armingGrab || f >= armingReturned) return 0f;
        if (f < armingPulled) return armingTravel * Mathf.SmoothStep(0f, 1f, (f - armingGrab) / Mathf.Max(1e-4f, armingPulled - armingGrab));
        if (f < armingRelease) return armingTravel;
        return armingTravel * (1f - Mathf.SmoothStep(0f, 1f, (f - armingRelease) / Mathf.Max(1e-4f, armingReturned - armingRelease)));
    }

    /// <summary>Le cadrage de l'armement monte sur cette part du rechargement avant que la main prenne la pièce, et
    /// redescend sur celle-ci après que la pièce est revenue (au FA-07, 0,13 et 0,18 s).</summary>
    public const float ArmingViewLeadIn = 0.06f, ArmingViewLeadOut = 0.08f;

    /// <summary>La main qui tire la pièce reste à tant (degrés) au plus sous l'axe de la vue (le bord bas de l'écran
    /// est à 30°, la moitié du champ des bras).</summary>
    public const float ArmingPalmAngle = 26f;

    /// <summary>La paume sur la pièce tirée, dans le repère du modèle d'arme (la pièce recule dans l'axe du canon).</summary>
    public Vector3 ArmingPalmPulled => armingPalm + Vector3.back * armingTravel;

    /// <summary>
    /// La levée de plus pendant l'armement (degrés), d'après `palmPulled`, la paume sur la pièce tirée dans le repère de
    /// la caméra des bras, avant cette levée : après la rotation armingViewYaw, ce qu'il faut pour la mettre à
    /// ArmingPalmAngle au plus sous l'axe de la vue, borné à armingViewPitchMax. Elle dépend du viseur : un viseur
    /// avance la mire de 10 à 16 cm et rapproche l'œil, et toute la copie des bras vient 11 à 18 cm plus près. Fonction
    /// pure, testée.
    /// </summary>
    public float ArmingViewPitch(Vector3 palmPulled)
    {
        if (armingViewPitchMax <= 0f || float.IsNaN(palmPulled.x + palmPulled.y + palmPulled.z)) return 0f;
        Vector3 q = Quaternion.AngleAxis(-armingViewYaw, Vector3.up) * palmPulled;
        float below = -Mathf.Atan2(q.y, q.z) * Mathf.Rad2Deg;
        return Mathf.Clamp(below - ArmingPalmAngle, 0f, armingViewPitchMax);
    }

    /// <summary>
    /// Le poids du cadrage de l'armement (ArmingViewPitch, armingViewYaw) à la part `f` du rechargement : rien loin de
    /// l'armement, tout de la prise au retour de la pièce, et entre deux une courbe en S. Fonction pure, testée.
    /// </summary>
    public float ArmingViewWeight(float f)
    {
        if (float.IsNaN(f) || armingViewPitchMax <= 0f) return 0f;
        if (f <= armingGrab - ArmingViewLeadIn || f >= armingReturned + ArmingViewLeadOut) return 0f;
        if (f < armingGrab) return Mathf.SmoothStep(0f, 1f, (f - (armingGrab - ArmingViewLeadIn)) / ArmingViewLeadIn);
        if (f <= armingReturned) return 1f;
        return 1f - Mathf.SmoothStep(0f, 1f, (f - armingReturned) / ArmingViewLeadOut);
    }
}
