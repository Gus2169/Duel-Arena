using UnityEngine;

/// <summary>
/// IK analytique à deux os (2026-10-17, tirée de FootPlanter, qui cale les pieds au sol depuis le
/// 2026-10-10) : amène un point porté par le second os sur une cible en pliant les deux os, sans changer
/// leur longueur. Elle sert aux jambes (FootPlanter) et aux bras (WeaponGrip : les mains sur l'arme).
/// Fonctions pures sur des transforms, testées.
/// </summary>
public static class TwoBoneIK
{
    /// <summary>
    /// Amène `end` sur `target` en pliant `upper` et `lower`, dans le plan où le membre est déjà plié
    /// (le genou garde sa direction). La rotation de `end` dans le monde est conservée : le pied reste à
    /// plat. Hors de portée, le membre se tend vers la cible, et la fonction renvoie faux.
    /// </summary>
    public static bool Solve(Transform upper, Transform lower, Transform end, Vector3 target)
    {
        Quaternion endRotation = end.rotation;
        bool reached = Solve(upper, lower, lower.InverseTransformPoint(end.position), target, Vector3.zero, 0f);
        end.rotation = endRotation;
        return reached;
    }

    /// <summary>
    /// La même chose pour un point quelconque que porte `lower` (`effector`, dans son repère : le bout du
    /// membre, ou le poing du gant que porte l'avant-bras). Avec un pôle (`pole`, une direction dans le
    /// monde, suivie à `poleWeight`, de 0 à 1), le membre tourne ensuite autour de l'axe qui va de sa
    /// racine au bout, ce qui ne déplace pas le bout, jusqu'à ce que le coude pointe vers lui. Les enfants
    /// de `lower` suivent : l'appelant rétablit leur rotation s'il le faut. Renvoie faux hors de portée.
    /// </summary>
    public static bool Solve(Transform upper, Transform lower, Vector3 effector, Vector3 target, Vector3 pole, float poleWeight)
    {
        Vector3 a = upper.position, b = lower.position, c = lower.TransformPoint(effector);
        float lab = Vector3.Distance(a, b);
        float lcb = Vector3.Distance(b, c);
        float wanted = Vector3.Distance(a, target);
        float lat = Mathf.Clamp(wanted, Mathf.Abs(lab - lcb) + 1e-3f, lab + lcb - 1e-3f);
        bool reached = wanted - lat <= 0.005f;

        float acab0 = AngleBetween(c - a, b - a);
        float babc0 = AngleBetween(a - b, c - b);
        float acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
        float babc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

        Vector3 axis = Vector3.Cross(c - a, b - a);
        if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(c - a, upper.forward);
        axis.Normalize();

        upper.rotation = Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, axis) * upper.rotation;
        lower.rotation = Quaternion.AngleAxis((babc1 - babc0) * Mathf.Rad2Deg, axis) * lower.rotation;

        a = upper.position;
        c = lower.TransformPoint(effector);
        upper.rotation = Quaternion.FromToRotation(c - a, target - a) * upper.rotation;

        if (poleWeight > 0f && pole.sqrMagnitude > 1e-8f)
        {
            // Le coude vers le pôle : une rotation autour de l'axe racine-bout, qui ne bouge pas le bout.
            Vector3 limb = lower.TransformPoint(effector) - a;
            Vector3 elbow = Vector3.ProjectOnPlane(lower.position - a, limb);
            Vector3 toward = Vector3.ProjectOnPlane(pole, limb);
            if (limb.sqrMagnitude > 1e-8f && elbow.sqrMagnitude > 1e-8f && toward.sqrMagnitude > 1e-8f)
                upper.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(elbow, toward, limb) * Mathf.Clamp01(poleWeight), limb)
                                 * upper.rotation;
        }
        return reached;
    }

    private static float AngleBetween(Vector3 u, Vector3 v)
    {
        return Mathf.Acos(Mathf.Clamp(Vector3.Dot(u.normalized, v.normalized), -1f, 1f));
    }
}
