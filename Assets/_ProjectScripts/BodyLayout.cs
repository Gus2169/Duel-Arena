using UnityEngine;

/// <summary>Zone du corps touchée par un tir. Porte le futur multiplicateur de dégâts, dont
/// l'ampleur reste une décision de design à trancher par playtest (GDD § 6).</summary>
public enum HitboxZoneType { Head, Torso, Legs }

/// <summary>
/// Géométrie du corps dans UNE posture, dans l'espace local du joueur (origine aux pieds, +Z
/// devant). Les valeurs par défaut viennent de mesures du robot (Y Bot) dans la pose d'attente de
/// chaque posture, relevées le 2026-10-05 avec le vrai Animator.
/// </summary>
[System.Serializable]
public struct StanceBody
{
    [Tooltip("Point autour duquel le buste pivote quand on se penche : la base de la colonne (l'os Spine du modèle, autour duquel l'animation fait pivoter le même buste).")]
    public Vector3 leanPivot;

    [Tooltip("Faux : le buste s'incline sur le côté (rotation autour de l'axe avant, debout et accroupi). Vrai : le buste pivote à plat vers la gauche ou la droite (rotation autour de la verticale, allongé).")]
    public bool leanSwingsSideways;

    [Tooltip("Jambes : capsule de A à B, rayon. Elles ne bougent JAMAIS avec le lean : c'est tout le principe du lean façon Rainbow Six.")]
    public Vector3 legsA;
    public Vector3 legsB;
    public float legsRadius;

    [Tooltip("Seconde jambe, quand une seule capsule ne peut pas couvrir les deux (allongé, la pose replie une jambe sur le côté). Rayon 0 = absente.")]
    public Vector3 secondLegA;
    public Vector3 secondLegB;
    public float secondLegRadius;

    [Tooltip("Torse : capsule de A à B, rayon. Pivote avec le lean.")]
    public Vector3 torsoA;
    public Vector3 torsoB;
    public float torsoRadius;

    [Tooltip("Tête : sphère centrée sur l'ŒIL du joueur (la caméra), rayon. Pivote avec le lean.")]
    public float headRadius;
}

/// <summary>Une zone mise en place : capsule de A à B, ou sphère quand A == B.</summary>
public struct ZoneShape
{
    public Vector3 a;
    public Vector3 b;
    public float radius;
}

/// <summary>
/// Calcul de la surface touchable à partir de la posture et du lean réseautés. Fonction PURE et
/// statique : elle se teste sans Editor, sans réseau et sans scène, et elle donne exactement le
/// même résultat sur toutes les machines pour les mêmes entrées.
///
/// 🚨 C'est ICI, et nulle part ailleurs, que se décide où l'on peut être touché. L'animation
/// AFFICHE le même lean en lisant le même scalaire (PlayerAnimator) ; elle ne pilote jamais ces
/// zones. Un Animator n'est pas déterministe entre machines.
///
/// LE LEAN FAÇON RAINBOW SIX (GDD § 7) : seuls le torse et la tête pivotent autour de la base de
/// la colonne ; les jambes restent derrière la couverture. Le scalaire réseauté ne change pas :
/// c'est toujours le décalage LATÉRAL de l'œil, en mètres. L'angle s'en déduit, ce qui laisse
/// intacts le réseau, l'historique du rewind et l'anti-clipping.
/// </summary>
public static class BodyLayout
{
    // Mesures du 2026-10-05 sur Y Bot, dans la pose d'attente de chaque posture jouée par le vrai
    // Animator. Les yeux vivent dans PlayerLocomotion.StanceProfile : une seule source.
    public static readonly StanceBody DefaultStanding = new StanceBody
    {
        leanPivot = new Vector3(0f, 1.08f, -0.02f),
        leanSwingsSideways = false,
        legsA = new Vector3(0f, 0.20f, 0f),
        legsB = new Vector3(0f, 0.80f, 0f),
        legsRadius = 0.20f,
        torsoA = new Vector3(0f, 1.17f, -0.02f),
        torsoB = new Vector3(0f, 1.30f, -0.03f),
        torsoRadius = 0.23f,
        headRadius = 0.14f,
    };

    public static readonly StanceBody DefaultCrouching = new StanceBody
    {
        leanPivot = new Vector3(0f, 0.56f, -0.10f),
        leanSwingsSideways = false,
        legsA = new Vector3(0f, 0.22f, 0.08f),
        legsB = new Vector3(0f, 0.30f, 0.08f),
        legsRadius = 0.28f,
        torsoA = new Vector3(0f, 0.66f, -0.06f),
        torsoB = new Vector3(0f, 0.74f, -0.02f),
        torsoRadius = 0.22f,
        headRadius = 0.14f,
    };

    /// <summary>
    /// Allongé. La pose d'attente de Mixamo est ASYMÉTRIQUE — tête penchée 15 cm à gauche sur la
    /// crosse, buste légèrement en biais, jambe droite repliée sur le côté — et la surface
    /// touchable la suit plutôt que de lui imposer une symétrie qu'on ne verrait pas.
    ///
    /// Le modèle est reculé de 0,30 m (PlayerAnimator.proneModelOffset) pour que sa tête tombe à
    /// l'intérieur du CharacterController : sans ça, l'œil dépasserait devant le collider et
    /// passerait à travers un mur face auquel on est allongé.
    /// </summary>
    public static readonly StanceBody DefaultProne = new StanceBody
    {
        leanPivot = new Vector3(-0.03f, 0.17f, -0.28f),
        leanSwingsSideways = true,
        legsA = new Vector3(-0.08f, 0.13f, -0.50f),
        legsB = new Vector3(0f, 0.13f, -1.22f),
        legsRadius = 0.13f,
        secondLegA = new Vector3(0.10f, 0.13f, -0.50f),
        secondLegB = new Vector3(0.52f, 0.13f, -0.95f),
        secondLegRadius = 0.14f,
        torsoA = new Vector3(-0.01f, 0.17f, -0.35f),
        torsoB = new Vector3(-0.12f, 0.23f, 0.06f),
        torsoRadius = 0.19f,
        headRadius = 0.13f,
    };

    /// <summary>Axe de rotation du lean, dans l'espace du joueur.</summary>
    public static Vector3 LeanAxis(in StanceBody body) => body.leanSwingsSideways ? Vector3.up : Vector3.forward;

    // ------------------------------------------------------------------
    // La géométrie du lean.
    //
    // Dans le plan perpendiculaire à l'axe, l'œil est à (dx, u) du pivot : dx le long de X,
    // u l'autre composante (la hauteur pour une inclinaison, l'avancée pour un pivot à plat). Une
    // rotation d'angle a, dans le sens qui envoie vers +X, donne :
    //     x(a) = dx·cos a + u·sin a = R·sin(a + φ),   R = √(dx² + u²),   φ = atan2(dx, u)
    // donc un décalage latéral Δ(a) = R·sin(a + φ) − dx, qu'on inverse pour trouver l'angle.
    //
    // Exact même quand l'œil n'est pas dans l'axe du pivot — c'est le cas allongé, où la tête est
    // penchée sur la crosse. Le lean y est alors un peu plus court d'un côté que de l'autre, ce
    // qui est la conséquence honnête d'une pose asymétrique.
    // ------------------------------------------------------------------

    private static void LeanPlane(in StanceBody body, Vector3 eye, out float dx, out float u)
    {
        Vector3 d = eye - body.leanPivot;
        dx = d.x;
        u = body.leanSwingsSideways ? d.z : d.y;
    }

    /// <summary>Bras de levier : distance de l'œil à l'axe de lean passant par le pivot.</summary>
    public static float LeanArm(in StanceBody body, Vector3 eye)
    {
        LeanPlane(body, eye, out float dx, out float u);
        return Mathf.Sqrt(dx * dx + u * u);
    }

    /// <summary>Décalage latéral de l'œil pour un angle signé (degrés, positif = vers la droite).</summary>
    public static float LateralOffsetForAngle(in StanceBody body, Vector3 eye, float angleDegrees)
    {
        LeanPlane(body, eye, out float dx, out float u);
        float a = angleDegrees * Mathf.Deg2Rad;
        return dx * Mathf.Cos(a) + u * Mathf.Sin(a) - dx;
    }

    /// <summary>Décalage latéral maximal d'un côté (`side` = +1 droite, -1 gauche), en valeur
    /// absolue, pour un angle de buste maximal. Accroupi, le bras de levier est plus court : à
    /// décalage égal, il faudrait se plier bien plus qu'en étant debout. Borner l'ANGLE garde une
    /// silhouette crédible dans toutes les postures.</summary>
    public static float MaxLateralOffset(in StanceBody body, Vector3 eye, float maxAngleDegrees, int side)
    {
        float angle = Mathf.Clamp(maxAngleDegrees, 0f, 89f) * Mathf.Sign(side == 0 ? 1 : side);
        return Mathf.Abs(LateralOffsetForAngle(body, eye, angle));
    }

    /// <summary>Angle signé (degrés) qui fait sortir l'œil de `lateralOffset` mètres sur le côté.</summary>
    public static float LeanAngle(in StanceBody body, Vector3 eye, float lateralOffset)
    {
        if (Mathf.Abs(lateralOffset) < 1e-6f) return 0f;

        LeanPlane(body, eye, out float dx, out float u);
        float r = Mathf.Sqrt(dx * dx + u * u);
        if (r < 1e-4f) return 0f;

        float phi = Mathf.Atan2(dx, u);
        float a = Mathf.Asin(Mathf.Clamp((lateralOffset + dx) / r, -1f, 1f)) - phi;
        return a * Mathf.Rad2Deg;
    }

    /// <summary>Rotation, dans l'espace du joueur, qui fait sortir l'œil de `lateralOffset` mètres
    /// sur le côté (+X = droite). Identité sans lean.</summary>
    public static Quaternion LeanRotation(in StanceBody body, Vector3 eye, float lateralOffset)
    {
        float angle = LeanAngle(body, eye, lateralOffset);
        if (Mathf.Abs(angle) < 1e-5f) return Quaternion.identity;

        // AngleAxis(+a, haut) fait tourner l'avant vers la droite, AngleAxis(-a, avant) fait
        // pencher le haut vers la droite : les deux envoient vers +X pour un angle positif.
        // Vérifié par les tests.
        return body.leanSwingsSideways
            ? Quaternion.AngleAxis(angle, Vector3.up)
            : Quaternion.AngleAxis(-angle, Vector3.forward);
    }

    /// <summary>Position de l'œil une fois penché. La caméra du propriétaire s'y place, et la tête
    /// touchable y est centrée : on voit depuis l'endroit où l'on peut être touché.</summary>
    public static Vector3 LeanedEye(in StanceBody body, Vector3 eye, float lateralOffset)
    {
        return Rotate(body, LeanRotation(body, eye, lateralOffset), eye);
    }

    /// <summary>Les zones touchables pour une posture, un œil et un lean donnés. La seconde jambe a
    /// un rayon nul quand la posture n'en a pas.</summary>
    public static void Compute(in StanceBody body, Vector3 eye, float lateralOffset,
                               out ZoneShape head, out ZoneShape torso, out ZoneShape legs, out ZoneShape secondLeg)
    {
        Quaternion rotation = LeanRotation(body, eye, lateralOffset);

        Vector3 leanedEye = Rotate(body, rotation, eye);
        head = new ZoneShape { a = leanedEye, b = leanedEye, radius = body.headRadius };

        torso = new ZoneShape
        {
            a = Rotate(body, rotation, body.torsoA),
            b = Rotate(body, rotation, body.torsoB),
            radius = body.torsoRadius,
        };

        // Les jambes ignorent le lean, par construction : c'est tout le lean façon R6.
        legs = new ZoneShape { a = body.legsA, b = body.legsB, radius = body.legsRadius };
        secondLeg = new ZoneShape { a = body.secondLegA, b = body.secondLegB, radius = body.secondLegRadius };
    }

    private static Vector3 Rotate(in StanceBody body, Quaternion rotation, Vector3 point)
    {
        return body.leanPivot + rotation * (point - body.leanPivot);
    }
}
