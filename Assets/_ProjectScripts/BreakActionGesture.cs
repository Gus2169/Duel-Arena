using UnityEngine;

/// <summary>
/// Le rechargement d'une arme qui s'ouvre (2026-11-03) : le Big D du Golden Gun, un derringer à un coup dont le canon
/// bascule en arrière, autour de la charnière, jusqu'à l'horizontale. Il ne change pas de chargeur : le levier
/// s'abaisse, la main gauche lance le canon, la douille vide glisse de la chambre tournée vers le bas et tombe, la main
/// prend une cartouche neuve à la hanche, la présente au-dessus de la chambre et l'y pousse, puis ramène le canon, qui
/// se verrouille ; le chien s'arme. Le clip est calculé dans Blender (Tools/Blender/animations/
/// rechargement_golden_gun.py) sur les étapes de l'arme (golden_gun.ETAPES) ; Duel Arena > Animations > Importer les
/// rechargements à chargeur l'importe, le branche dans l'Animator et remplit ce geste. On réexporte de Blender et on
/// relance le menu ; on ne retouche pas ces valeurs à la main.
///
/// Pour l'Animator et la vue subjective, c'est un geste de rechargement comme les autres (ReloadGesture : sa valeur de
/// GesteRecharge, ses armes, le clip ; MagazineSwap la fait jouer et la vue subjective cadre le rechargement). Les
/// étapes du chargeur y gardent leur sens : la douille vide part (drop), la main prend la cartouche (grab), la présente
/// (approach) et la cartouche est devant la chambre (seated). Ce qui bouge sur l'arme se déduit de PoseAt
/// (BreakActionView). Catégorie C.
/// </summary>
[CreateAssetMenu(menuName = "Duel Arena/Geste de rechargement d'une arme qui s'ouvre", fileName = "Rechargement")]
public class BreakActionGesture : ReloadGesture
{
    [Header("Ce qui bouge sur le modèle (les chemins depuis sa racine)")]
    [Tooltip("Le canon : il tourne autour de son X (l'axe de la charnière) ; ouvert, la bouche monte puis passe en arrière.")]
    public string barrelPath = "Canon";
    [Tooltip("Le chien, autour de son X : armé au repos (le modèle est dessiné prêt à tirer), abattu par le tir.")]
    public string hammerPath = "Chien";
    [Tooltip("Le levier qui libère le canon, autour de son X.")]
    public string latchPath = "Levier";
    [Tooltip("La cartouche de la chambre (dans le canon) : elle en sort le long de son -Z (vers la culasse, canon fermé).")]
    public string cartridgePath = "Canon/Cartouche";
    [Tooltip("Sa douille et sa balle (une douille vide n'a pas de balle).")]
    public string casePath = "Canon/Cartouche/Douille";
    public string bulletPath = "Canon/Cartouche/Balle";

    [Header("Les étapes, en part du rechargement (golden_gun.ETAPES)")]
    [Tooltip("Le levier s'abaisse puis revient.")]
    public Vector2 latchStep = new Vector2(0.02f, 0.10f);
    [Tooltip("Le canon bascule jusqu'à l'horizontale.")]
    public Vector2 openStep = new Vector2(0.08f, 0.24f);
    [Tooltip("La chambre tournée vers le bas, la douille vide glisse dehors, puis tombe.")]
    public Vector2 ejectStep = new Vector2(0.14f, 0.18f);
    [Tooltip("La cartouche neuve, présentée devant la chambre, y entre.")]
    public Vector2 insertStep = new Vector2(0.50f, 0.62f);
    [Tooltip("Le canon revient et se verrouille.")]
    public Vector2 closeStep = new Vector2(0.68f, 0.82f);
    [Tooltip("Le chien s'arme.")]
    public Vector2 cockStep = new Vector2(0.86f, 0.93f);

    [Header("De combien elles bougent (golden_gun.pose)")]
    [Tooltip("Degrés : le canon ouvert.")]
    public float barrelAngle = 180f;
    [Tooltip("Degrés : le chien abattu, depuis l'armé.")]
    public float hammerAngle = 40f;
    [Tooltip("Degrés : le levier abaissé.")]
    public float latchAngle = 14f;
    [Tooltip("Mètres : la douille vide sortie de la chambre quand elle la quitte.")]
    public float caseTravel = 0.09f;
    [Tooltip("Mètres : la cartouche neuve devant la chambre quand elle s'y présente.")]
    public float cartridgeApproach = 0.12f;
    [Tooltip("Secondes : la durée du rechargement pour lequel le geste est calculé (Arme_GoldenGun.reloadDuration) ; la douille vide part à la vitesse qu'elle avait en glissant.")]
    public float reloadSeconds = 2.5f;

    /// <summary>La cartouche neuve passe de la main à sa place devant la chambre sur cette part du rechargement, avant que
    /// la chambre la prenne (ReloadGesture.approach, seated) : sur les bras, la paume du châssis montré n'est pas celle
    /// du geste, et la cartouche n'y saute pas.</summary>
    public const float PresentLead = 0.05f;

    /// <summary>Les pièces mobiles à une part du rechargement.</summary>
    public struct Pose
    {
        /// <summary>Degrés : le canon ouvert de tant.</summary>
        public float barrel;
        /// <summary>Degrés : le chien abattu de tant depuis l'armé.</summary>
        public float hammer;
        /// <summary>Degrés : le levier abaissé de tant.</summary>
        public float latch;
        /// <summary>Mètres : la douille vide déjà sortie de la chambre ; NaN une fois partie.</summary>
        public float caseSlide;
        /// <summary>Mètres : la cartouche neuve encore devant la chambre ; NaN tant qu'elle n'y est pas présentée.</summary>
        public float cartridgeSlide;
    }

    /// <summary>Où en est une étape à la part `f` du rechargement : 0 avant, 1 après.</summary>
    public static float Part(float f, Vector2 step) => Mathf.Clamp01((f - step.x) / Mathf.Max(1e-6f, step.y - step.x));

    /// <summary>La courbe en S de golden_gun.doux.</summary>
    public static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    /// <summary>
    /// Les pièces mobiles à la part `f` du rechargement (golden_gun.pose, traduite telle quelle) : le canon s'ouvre puis
    /// se referme, le chien, abattu par le tir, s'arme à la fin, le levier s'abaisse et revient ; la douille vide glisse de
    /// plus en plus vite hors de la chambre, puis part ; la cartouche neuve y entre. Fonction pure, comparée aux relevés
    /// de Blender.
    /// </summary>
    public Pose PoseAt(float f)
    {
        float ejected = Part(f, ejectStep);
        return new Pose
        {
            barrel = barrelAngle * (Smooth(Part(f, openStep)) - Smooth(Part(f, closeStep))),
            hammer = hammerAngle * (1f - Smooth(Part(f, cockStep))),
            latch = latchAngle * Mathf.Sin(Mathf.PI * Part(f, latchStep)),
            caseSlide = ejected < 1f ? caseTravel * ejected * ejected : float.NaN,
            cartridgeSlide = f >= insertStep.x ? cartridgeApproach * (1f - Smooth(Part(f, insertStep))) : float.NaN,
        };
    }

    /// <summary>La vitesse (m/s) de la douille vide quand elle quitte la chambre : elle glisse de plus en plus vite
    /// (caseTravel × la part de l'éjection au carré).</summary>
    public float CaseExitSpeed => 2f * caseTravel / Mathf.Max(1e-3f, (ejectStep.y - ejectStep.x) * reloadSeconds);

    /// <summary>
    /// La rotation locale d'une pièce qui tourne autour de son X de `degrees` dans le sens de Blender (le canon s'ouvre
    /// d'un angle négatif, le chien s'abat d'un angle positif, le levier s'abaisse d'un angle négatif) : l'export
    /// (exporter_armes.vers_axes_unity) retourne l'axe X et passe d'un repère direct à un repère indirect, les deux
    /// retournements s'annulent. Mesuré sur le modèle importé (BreakActionReloadTests).
    /// </summary>
    public static Quaternion Turn(float degrees) => Quaternion.AngleAxis(degrees, Vector3.right);
}
