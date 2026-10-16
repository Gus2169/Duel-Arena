using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Les règles des spectateurs (GDD § 9, 2026-10-13), en fonctions pures et testées : ce que chaque
/// phase permet, l'étourdissement, la force d'un lancer, ce qu'atteint un coup de poing ou un caillou,
/// ce qu'on ramasse. Le serveur les applique (SpectatorController, ArenaItem, Tribune) ; le joueur s'en
/// sert pour son confort (prédiction, invites du HUD).
///
/// Le corps d'un robot y est une capsule verticale calculée, jamais lue sur un collider : au serveur,
/// celle d'un instant passé (compensation de latence du coup de poing), comme la surface touchable des
/// duellistes se calcule et ne se lit pas sur les os (CLAUDE.md).
/// </summary>
public static class SpectatorRules
{
    /// <summary>Rayon du corps d'un robot spectateur (m), celui de son CharacterController.</summary>
    public const float BodyRadius = 0.35f;

    // ------------------------------------------------------------------
    // Les phases
    // ------------------------------------------------------------------

    /// <summary>
    /// Un spectateur peut-il lancer ce qu'il tient ? Pendant la manche et ses suites (fin de manche, fin
    /// de match), et pendant l'attente d'un adversaire. Jamais pendant la préparation ni le décompte :
    /// les duellistes y sont gelés, et personne ne commence une manche avec un désavantage (GDD § 3).
    /// Se battre, casser un bloc et ramasser restent permis : on prépare ses cailloux.
    /// </summary>
    public static bool ThrowAllowed(RoundManager.Phase phase)
    {
        return phase == RoundManager.Phase.Active || phase == RoundManager.Phase.RoundOver
            || phase == RoundManager.Phase.MatchOver || phase == RoundManager.Phase.WaitingForPlayers;
    }

    /// <summary>Le matériel ne tombe sur la tribune que pendant une manche (GDD § 9).</summary>
    public static bool DropsAllowed(RoundManager.Phase phase) => phase == RoundManager.Phase.Active;

    // ------------------------------------------------------------------
    // L'étourdissement
    // ------------------------------------------------------------------

    /// <summary>
    /// Étourdit pour `duration` secondes à partir de `now`, sauf pendant l'immunité qui suit un
    /// étourdissement : sans elle, deux spectateurs qui s'acharnent sur un troisième le garderaient au
    /// sol indéfiniment. Renvoie faux si le coup n'étourdit pas. Fonction pure, testée.
    /// </summary>
    public static bool TryStun(ref double stunnedUntil, ref double immuneUntil, double now, float duration, float immunity)
    {
        if (!(duration > 0f) || now < immuneUntil) return false;
        stunnedUntil = now + duration;
        immuneUntil = stunnedUntil + Mathf.Max(0f, immunity);
        return true;
    }

    public static bool IsStunned(double stunnedUntil, double time) => time < stunnedUntil;

    // ------------------------------------------------------------------
    // Le lancer
    // ------------------------------------------------------------------

    /// <summary>
    /// La vitesse d'un objet lancé : dans la direction du regard, d'autant plus vite que l'élan a été
    /// long (`charge`, de 0 à 1). C'est une suggestion du client, toujours bornée : un client modifié
    /// lance au plus aussi fort qu'un joueur qui a pris tout son élan. Fonction pure, testée.
    /// </summary>
    public static Vector3 ThrowVelocity(Vector3 direction, float charge, float minSpeed, float maxSpeed)
    {
        if (!IsFinite(direction) || direction.sqrMagnitude < 1e-6f) return Vector3.zero;
        float c = float.IsNaN(charge) ? 0f : Mathf.Clamp01(charge);
        return direction.normalized * Mathf.Lerp(minSpeed, maxSpeed, c);
    }

    /// <summary>La secousse d'un duelliste touché par un caillou part du côté où va le caillou : +1
    /// vers sa droite, -1 vers sa gauche. Aucun hasard (GDD § 3). Fonction pure, testée.</summary>
    public static float FlinchSide(Vector3 rockVelocity, Vector3 playerRight)
        => Vector3.Dot(rockVelocity, playerRight) < 0f ? -1f : 1f;

    // ------------------------------------------------------------------
    // Les corps
    // ------------------------------------------------------------------

    /// <summary>Le segment central de la capsule d'un robot debout sur `feet`, de hauteur `height`.</summary>
    public static void BodySegment(Vector3 feet, float height, float radius, out Vector3 bottom, out Vector3 top)
    {
        float h = Mathf.Max(height, 2f * radius);
        bottom = feet + Vector3.up * radius;
        top = feet + Vector3.up * (h - radius);
    }

    /// <summary>
    /// Distance entre deux segments [p1, q1] et [p2, q2], et les points les plus proches de chacun.
    /// Méthode classique (Ericson, « Real-Time Collision Detection » § 5.1.9). Fonction pure, testée.
    /// </summary>
    public static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float s, t;
        const float Epsilon = 1e-8f;
        if (a <= Epsilon && e <= Epsilon)
        {
            s = t = 0f;
        }
        else if (a <= Epsilon)
        {
            s = 0f;
            t = Mathf.Clamp01(f / e);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= Epsilon)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;
                s = denom > Epsilon ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / e;
                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((b - c) / a);
                }
            }
        }
        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
        return Vector3.Distance(c1, c2);
    }

    /// <summary>
    /// Un coup de poing donné depuis `origin` vers `direction` (portée `range`, volume de rayon
    /// `radius`) atteint-il le robot debout sur `feet` ? Le corps doit être devant soi, pas dans le dos.
    /// `distance` : de l'œil au point touché ; `point` : le point du corps le plus proche du coup.
    /// Fonction pure, testée.
    /// </summary>
    public static bool PunchReaches(Vector3 origin, Vector3 direction, float range, float radius,
                                    Vector3 feet, float height, float bodyRadius, out float distance, out Vector3 point)
    {
        distance = float.MaxValue;
        point = feet;
        if (!IsFinite(direction) || direction.sqrMagnitude < 1e-6f) return false;
        direction.Normalize();
        BodySegment(feet, height, bodyRadius, out Vector3 bottom, out Vector3 top);
        float gap = SegmentDistance(origin, origin + direction * range, bottom, top, out Vector3 onPunch, out Vector3 onBody);
        if (gap > radius + bodyRadius) return false;

        // Le point du corps qui fait face au coup : sur sa surface, du côté du poing (face au frappeur
        // quand le coup traverse l'axe du corps).
        Vector3 toPunch = onPunch - onBody;
        point = toPunch.sqrMagnitude > 1e-6f ? onBody + toPunch.normalized * bodyRadius : onBody - direction * bodyRadius;
        if (Vector3.Dot(point - origin, direction) < -0.1f) return false;
        distance = Vector3.Distance(origin, point);
        return true;
    }

    /// <summary>
    /// Un objet de rayon `radius` qui va de `from` à `to` touche-t-il le robot debout sur `feet` ?
    /// `fraction` : où, le long du trajet (0 au départ, 1 à l'arrivée). Le pas de simulation est court
    /// (1/60 s, une quarantaine de centimètres au plus) : le point le plus proche du trajet suffit.
    /// Fonction pure, testée.
    /// </summary>
    public static bool SweepHitsBody(Vector3 from, Vector3 to, float radius, Vector3 feet, float height, float bodyRadius, out float fraction)
    {
        fraction = 1f;
        BodySegment(feet, height, bodyRadius, out Vector3 bottom, out Vector3 top);
        float gap = SegmentDistance(from, to, bottom, top, out Vector3 onPath, out _);
        if (gap > radius + bodyRadius) return false;
        float length = Vector3.Distance(from, to);
        fraction = length > 1e-6f ? Mathf.Clamp01(Vector3.Distance(from, onPath) / length) : 0f;
        return true;
    }

    // ------------------------------------------------------------------
    // Ramasser
    // ------------------------------------------------------------------

    /// <summary>
    /// L'objet que vise le joueur pour le ramasser : à portée de l'œil, dans un cône de `maxAngle`
    /// degrés autour du regard, le plus proche de l'axe. -1 s'il n'y en a pas. Fonction pure, testée.
    /// </summary>
    public static int BestPickup(Vector3 eye, Vector3 forward, IReadOnlyList<Vector3> positions, float range, float maxAngle)
    {
        int best = -1;
        float bestAngle = maxAngle;
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 to = positions[i] - eye;
            float distance = to.magnitude;
            if (distance > range) continue;
            // Tout près (à ses pieds, dans sa main), l'angle ne veut plus rien dire : il passe.
            float angle = distance < 0.5f ? 0f : Vector3.Angle(forward, to);
            if (angle > bestAngle || (best >= 0 && angle == bestAngle)) continue;
            best = i;
            bestAngle = angle;
        }
        return best;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
}
