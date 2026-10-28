using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Le recul coup par coup (2026-10-26, demandé par l'utilisateur, dessiné dans l'éditeur des armes) :
/// chaque coup d'une rafale pousse le regard selon le motif de l'arme (WeaponData.recoilPattern, en
/// multiples de recoilPerShotDegrees), puis le regard revient vers l'axe à recoilRecoverySpeed degrés
/// par seconde. Sans hasard (GDD § 3) : la même rafale monte toujours de la même façon, et se dompte en
/// apprenant le motif, comme dans Rainbow Six et Call of Duty.
///
/// Le recul ne touche que la caméra du tireur (PlayerCameraLook.AddInstantRotation) : le tir part de la
/// caméra, et le serveur ne reçoit que l'axe. Fonctions pures, testées (RecoilPatternTests), lues par le
/// tireur (WeaponController) et par l'éditeur des armes, qui montre la rafale telle qu'elle partira.
/// </summary>
public static class RecoilPattern
{
    /// <summary>La poussée (degrés : x à droite, y en haut) du coup `shot` d'une rafale (0 pour le
    /// premier), avant la posture, la visée et les accessoires. Au-delà du dernier coup du motif, le
    /// dernier se répète.</summary>
    public static Vector2 Kick(WeaponData data, int shot)
    {
        if (data == null || data.recoilPattern == null || data.recoilPattern.Length == 0) return Vector2.zero;
        return data.recoilPattern[Mathf.Clamp(shot, 0, data.recoilPattern.Length - 1)] * data.recoilPerShotDegrees;
    }

    /// <summary>Ce que le retour du regard rend, en `seconds` secondes, du recul accumulé
    /// `accumulated` : chaque axe revient vers l'axe à `speed` degrés par seconde, sans le dépasser. Un
    /// recul vers le bas revient aussi (jusqu'au 2026-10-26, seul le recul vers le haut revenait).</summary>
    public static Vector2 Recovery(Vector2 accumulated, float speed, float seconds)
    {
        float step = speed > 0f && seconds > 0f ? speed * seconds : 0f;
        return new Vector2(Mathf.Clamp(accumulated.x, -step, step), Mathf.Clamp(accumulated.y, -step, step));
    }

    /// <summary>Le temps entre deux coups tirés au plus vite (s) : la cadence, ou la charge d'un tir
    /// chargé à fond (le FX-2).</summary>
    public static float ShotInterval(WeaponData data)
    {
        if (data == null || data.shotsPerSecond <= 0f) return 0f;
        return Mathf.Max(1f / data.shotsPerSecond, data.UsesCharge ? data.chargeTime : 0f);
    }

    /// <summary>Les coups s'enchaînent-ils dans le motif ? Une arme qui tire moins vite que la remise à
    /// zéro du motif (recoilResetDelay) le recommence à chaque coup : seul son premier coup compte.</summary>
    public static bool Chains(WeaponData data) => data != null && ShotInterval(data) > 0f && ShotInterval(data) < data.recoilResetDelay;

    /// <summary>Les coups d'une rafale au plus long : le chargeur, ou les tirs d'affilée avant la
    /// surchauffe d'une arme laser, avec ou sans le grand chargeur (la cellule grande capacité).</summary>
    public static int BurstLength(WeaponData data, bool extended)
    {
        if (data == null) return 0;
        if (data.UsesHeat)
            return Mathf.Max(1, WeaponController.ShotsBeforeOverheat(WeaponController.HeatRulesFor(data, extended), data.shotsPerSecond));
        return Mathf.Max(1, AttachmentCatalog.MagazineSize(data, extended, data.attachmentCatalog));
    }

    /// <summary>
    /// Où part chaque coup d'une rafale de `shots` coups, tirée au plus vite sans compenser : l'écart du
    /// regard (degrés, depuis l'axe du premier coup) au moment de chaque tir. `scale` : ce que la
    /// posture, la visée et les accessoires laissent de la poussée (x horizontal, y vertical). Sans
    /// `recovery`, le motif seul : le regard ne revient pas entre deux coups. Comme le tireur : la
    /// poussée d'un coup s'applique après son départ (elle déplace le suivant), et une arme qui ne
    /// s'enchaîne pas (Chains) reprend son motif au premier coup.
    /// </summary>
    public static void Burst(WeaponData data, int shots, Vector2 scale, bool recovery, List<Vector2> points)
    {
        points.Clear();
        if (data == null) return;
        float interval = ShotInterval(data);
        bool chains = Chains(data);
        Vector2 aim = Vector2.zero, accumulated = Vector2.zero;
        for (int shot = 0; shot < shots; shot++)
        {
            if (shot > 0 && recovery)
            {
                Vector2 back = Recovery(accumulated, data.recoilRecoverySpeed, interval);
                aim -= back;
                accumulated -= back;
            }
            points.Add(aim);
            Vector2 kick = Vector2.Scale(Kick(data, chains ? shot : 0), scale);
            aim += kick;
            accumulated += kick;
        }
    }
}
