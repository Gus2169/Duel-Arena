using UnityEngine;

/// <summary>
/// Le rechargement d'une arme qui s'ouvre (2026-11-03) : le Big D du Golden Gun, un derringer à un coup dont le canon
/// bascule en arrière, autour de la charnière, jusqu'à l'horizontale. Il ne change pas de chargeur. Refait le même jour
/// d'après le Wingman d'Apex Legends (« fluide, aisé et nonchalant », demandé par l'utilisateur) : la main droite fait
/// tout au poignet. Le pouce abaisse le levier pendant l'élan, un coup de poignet vers le haut lance le canon, qui
/// bascule seul et rebondit sur sa butée, et l'éjecteur chasse la douille vide ; la main gauche prend une cartouche
/// neuve debout dans sa boucle sur la hanche, entre le pouce et l'index, et la glisse dans la chambre ; un coup de
/// poignet sec vers le bas lance le canon, qui se rabat et claque ; le pouce arme le chien. Le clip est calculé dans
/// Blender (Tools/Blender/animations/rechargement_golden_gun.py) sur les étapes de l'arme (golden_gun.ETAPES) ;
/// Duel Arena > Animations > Importer les rechargements à chargeur l'importe, le branche dans l'Animator et remplit ce
/// geste. On réexporte de Blender et on relance le menu ; on ne retouche pas ces valeurs à la main.
///
/// Pour l'Animator et la vue subjective, c'est un geste de rechargement comme les autres (ReloadGesture : sa valeur de
/// GesteRecharge, ses armes, le clip ; MagazineSwap la fait jouer et la vue subjective cadre le rechargement). Les
/// étapes du chargeur y gardent leur sens : la douille vide part (drop), la main prend la cartouche (grab), la présente
/// (approach) et la cartouche est devant la chambre (seated). Ce qui bouge sur l'arme se déduit de PoseAt
/// (BreakActionView), le pouce droit de ThumbAt : ses poses tordent ses phalanges, ce qu'un clip humanoïde perd (le
/// pouce y tombait à 3 à 7 cm du levier et du chien). Catégorie C.
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
    [Tooltip("Le pouce abaisse le levier, le tient, puis le relâche.")]
    public Vector2 latchStep = new Vector2(0.03f, 0.17f);
    [Tooltip("Le coup de poignet vers le haut arrêté net, le canon bascule seul jusqu'à l'horizontale.")]
    public Vector2 openStep = new Vector2(0.12f, 0.17f);
    [Tooltip("Il rebondit sur sa butée.")]
    public Vector2 reboundStep = new Vector2(0.17f, 0.21f);
    [Tooltip("L'éjecteur chasse la douille vide, qui glisse dehors, puis s'envole et tombe.")]
    public Vector2 ejectStep = new Vector2(0.17f, 0.215f);
    [Tooltip("La cartouche neuve, présentée devant la chambre, y entre.")]
    public Vector2 insertStep = new Vector2(0.49f, 0.57f);
    [Tooltip("Le coup de poignet vers le bas arrêté net, le canon se rabat et claque.")]
    public Vector2 closeStep = new Vector2(0.672f, 0.705f);
    [Tooltip("Le chien s'arme.")]
    public Vector2 cockStep = new Vector2(0.80f, 0.88f);

    [Header("De combien elles bougent (golden_gun.pose)")]
    [Tooltip("Degrés : le canon ouvert.")]
    public float barrelAngle = 180f;
    [Tooltip("Degrés : le chien abattu, depuis l'armé.")]
    public float hammerAngle = 40f;
    [Tooltip("Degrés : le levier abaissé.")]
    public float latchAngle = 12f;
    [Tooltip("Parts de l'étape du levier : le pouce l'enfonce, le tient, puis le relâche (golden_gun.appui).")]
    public float latchPress = 0.30f, latchRelease = 0.25f;
    [Tooltip("Le canon lancé part à cette part de sa vitesse moyenne, puis accélère jusqu'à sa butée (golden_gun.bascule) : en s'ouvrant, puis en se rabattant (le coup vers le bas est sec).")]
    public float openKick = 0.30f, closeKick = 0.80f;
    [Tooltip("Degrés : le canon ouvert rebondit de tant sur sa butée.")]
    public float reboundAngle = 10f;
    [Tooltip("Mètres : la douille vide sortie de la chambre quand elle la quitte.")]
    public float caseTravel = 0.09f;
    [Tooltip("Mètres : la cartouche neuve devant la chambre quand elle s'y présente.")]
    public float cartridgeApproach = 0.12f;
    [Tooltip("Secondes : la durée du rechargement pour lequel le geste est calculé (Arme_GoldenGun.reloadDuration) ; la douille vide part à la vitesse qu'elle avait en glissant.")]
    public float reloadSeconds = 2.5f;

    [Header("Le pouce droit (rechargement_golden_gun.pouce_droit_unity)")]
    [Tooltip("Les rotations locales de ses trois phalanges (de la base au bout, par rapport à leur parent), à des parts régulières du geste, de 0 à 1 : le jeu les repose lui-même (BreakActionView), l'humanoïde ne rejouant pas leur torsion.")]
    public Quaternion[] rightThumb = new Quaternion[0];

    [Header("La main droite qui tient l'arme (tenue_golden_gun.py)")]
    [Tooltip("Les rotations locales des phalanges de la main droite (dans l'ordre de HoldOrder, par rapport à leur parent) quand elle tient l'arme : la prise des pistolets du clip referme la main sur deux centimètres, et ses doigts entraient de 14 à 20 mm dans la crosse. Le jeu les repose lui-même tant que l'arme est en main (BreakActionView) ; pendant le rechargement, le pouce passe de là aux poses du geste.")]
    public Quaternion[] rightHold = new Quaternion[0];

    /// <summary>Les phalanges du pouce relevées à chaque part.</summary>
    public const int ThumbBones = 3;

    /// <summary>Les phalanges de la main droite que la tenue repose, de la base au bout de chaque doigt : le pouce
    /// d'abord (celles de rightThumb), puis l'index, le majeur, l'annulaire et l'auriculaire.</summary>
    public static readonly HumanBodyBones[] HoldOrder =
    {
        HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
        HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
    };

    /// <summary>Le geste a sa tenue de la main droite.</summary>
    public bool HasHold => rightHold != null && rightHold.Length == HoldOrder.Length;

    /// <summary>Le pouce passe de la pose de l'Animator à la sienne sur cette part du rechargement, au début, et en revient
    /// à la fin.</summary>
    public const float ThumbBlend = 0.03f;

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

    /// <summary>La course du canon lancé (golden_gun.bascule) : il part à `kick` de sa vitesse moyenne et accélère
    /// jusqu'à sa butée.</summary>
    public static float Swing(float x, float kick)
    {
        x = Mathf.Clamp01(x);
        return x * (kick + (1f - kick) * x);
    }

    /// <summary>L'appui du pouce sur le levier (golden_gun.appui) : 0, puis 1, puis 0, sans à-coup.</summary>
    public static float Press(float x, float press, float release)
    {
        x = Mathf.Clamp01(x);
        if (x < press) return Smooth(x / press);
        if (x > 1f - release) return Smooth((1f - x) / release);
        return 1f;
    }

    /// <summary>
    /// Les pièces mobiles à la part `f` du rechargement (golden_gun.pose, traduite telle quelle) : le canon, lancé,
    /// s'ouvre, rebondit sur sa butée, puis se rabat ; le chien, abattu par le tir, s'arme à la fin ; le levier s'abaisse,
    /// tenu, puis revient ; la douille vide glisse de plus en plus vite hors de la chambre, puis part ; la cartouche neuve
    /// y entre. Fonction pure, comparée aux relevés de Blender.
    /// </summary>
    public Pose PoseAt(float f)
    {
        float ejected = Part(f, ejectStep);
        return new Pose
        {
            barrel = barrelAngle * (Swing(Part(f, openStep), openKick) - Swing(Part(f, closeStep), closeKick))
                     - reboundAngle * Mathf.Sin(Mathf.PI * Part(f, reboundStep)),
            hammer = hammerAngle * (1f - Smooth(Part(f, cockStep))),
            latch = latchAngle * Press(Part(f, latchStep), latchPress, latchRelease),
            caseSlide = ejected < 1f ? caseTravel * ejected * ejected : float.NaN,
            cartridgeSlide = f >= insertStep.x ? cartridgeApproach * (1f - Smooth(Part(f, insertStep))) : float.NaN,
        };
    }

    /// <summary>
    /// Les rotations locales des phalanges du pouce droit à la part `f` du rechargement (entre deux relevés, au plus
    /// court), dans `into` (ThumbBones rotations) : il abaisse le levier, puis arme le chien. Faux si le geste n'en a pas.
    /// </summary>
    public bool ThumbAt(float f, Quaternion[] into)
    {
        int samples = rightThumb != null ? rightThumb.Length / ThumbBones : 0;
        if (samples < 2 || into == null || into.Length < ThumbBones) return false;
        float x = Mathf.Clamp01(f) * (samples - 1);
        int i = Mathf.Min(samples - 2, Mathf.FloorToInt(x));
        for (int b = 0; b < ThumbBones; b++)
            into[b] = Quaternion.Slerp(rightThumb[i * ThumbBones + b], rightThumb[(i + 1) * ThumbBones + b], x - i);
        return true;
    }

    /// <summary>Le poids de la pose du pouce sur celle de l'Animator à la part `f` : 0 au début et à la fin du geste, 1
    /// entre les deux.</summary>
    public static float ThumbWeight(float f) => Smooth(Mathf.Min(f, 1f - f) / ThumbBlend);

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
