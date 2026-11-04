using UnityEngine;

/// <summary>
/// La Zone (GDD § 11, 2026-11-02, à la façon du Hardpoint de Call of Duty) : une partie aux points, avec réapparitions.
/// Une zone change de place à intervalles fixes, parmi les emplacements de l'arène (ZoneLayout), dans un ordre fixe,
/// annoncée un peu avant ; elle s'ouvre dès le départ. Seul dedans, on gagne des points, par paliers ; à deux dedans, plus
/// rien n'avance ; en sortant, on garde ce qu'on a pris. Une élimination rapporte moins. Le premier au compte gagne, ou le
/// plus de points au bout du chrono ; à égalité, la prolongation : le prochain point gagne.
///
/// Le serveur juge qui tient la zone et compte (RoundManager) ; chaque machine montre la zone (ZoneLayout) et ce qui
/// s'y passe (GameHud). Les valeurs sont les propositions de Claude du GDD, à régler en jouant. Fonctions pures, testées.
/// </summary>
public static class ZoneMode
{
    /// <summary>La zone change de place toutes les tant de secondes.</summary>
    public const float RotationPeriod = 60f;

    /// <summary>La suivante s'annonce tant de secondes avant (GDD § 11, Z9).</summary>
    public const float AnnounceLead = 10f;

    /// <summary>Seul dans la zone, tant de points d'un coup, à chaque intervalle plein (2026-11-04, l'utilisateur :
    /// « augmenté de X points toutes les X secondes » ; le rythme des 100 toutes les 5 s de Z1).</summary>
    public const int PointsPerTick = 20;
    public const float TickInterval = 1f;

    /// <summary>Le chrono du match (2026-11-04, l'utilisateur : « pour pas que ça soit trop long ») : à sa fin, le plus de
    /// points gagne.</summary>
    public const float MatchDuration = 600f;

    /// <summary>Points d'une élimination : moins qu'un temps passé dans la zone (Z1, Z4).</summary>
    public const int PointsPerKill = 100;

    /// <summary>Le premier à tant de points gagne.</summary>
    public const int TargetPoints = 3000;

    /// <summary>On est dans la zone les pieds de tant sous son centre (une marche, un sol qui penche) à tant au-dessus
    /// (debout sur une caisse basse dedans) ; à plat, dans son rayon.</summary>
    public const float FeetBelow = 0.5f, FeetAbove = 1.6f;

    /// <summary>Un mort ne réapparaît pas à moins de tant de mètres du bord de la zone : il y gagnerait des points sans
    /// s'être battu pour y entrer.</summary>
    public const float RespawnClearance = 8f;

    /// <summary>Qui tient la zone.</summary>
    public enum Hold : byte
    {
        /// <summary>Personne dedans.</summary>
        Free,
        /// <summary>Un seul dedans : il gagne des points.</summary>
        Held,
        /// <summary>À deux dedans : plus rien n'avance.</summary>
        Contested,
    }

    /// <summary>La place de la zone (dans l'ordre de l'arène) `elapsed` secondes après le début du match, parmi
    /// `count` emplacements ; -1 sans emplacement.</summary>
    public static int ZoneAt(float elapsed, int count)
    {
        if (count <= 0) return -1;
        return (int)(Mathf.Max(0f, elapsed) / RotationPeriod) % count;
    }

    /// <summary>Secondes avant que la zone ne change de place, `elapsed` secondes après le début du match.</summary>
    public static float TimeUntilMove(float elapsed) => RotationPeriod - Mathf.Repeat(Mathf.Max(0f, elapsed), RotationPeriod);

    /// <summary>La suivante est-elle annoncée ? Les AnnounceLead dernières secondes de chaque emplacement.</summary>
    public static bool Announcing(float elapsed) => TimeUntilMove(elapsed) <= AnnounceLead;

    /// <summary>Qui tient la zone, selon combien de robots en vie y sont.</summary>
    public static Hold HoldOf(int insideCount) => insideCount <= 0 ? Hold.Free : insideCount == 1 ? Hold.Held : Hold.Contested;

    /// <summary>Un robot dont les pieds sont en `feet` est-il dans la zone de centre `center` (au sol) et de rayon
    /// `radius` ?</summary>
    public static bool Contains(Vector3 center, float radius, Vector3 feet)
    {
        float rise = feet.y - center.y;
        if (rise < -FeetBelow || rise > FeetAbove) return false;
        float dx = feet.x - center.x, dz = feet.z - center.z;
        return dx * dx + dz * dz <= radius * radius;
    }

    /// <summary>Les points qu'on prend en `seconds` secondes seul dans la zone, ajoutées au temps déjà tenu depuis le dernier
    /// palier (`held`) : un palier par intervalle plein, et ce qui reste pour le suivant.</summary>
    public static int Earn(float seconds, ref float held)
    {
        held += Mathf.Max(0f, seconds);
        int ticks = Mathf.FloorToInt(held / TickInterval);
        held -= ticks * TickInterval;
        return ticks * PointsPerTick;
    }

    /// <summary>Secondes avant la fin du chrono, `elapsed` secondes après le début du match.</summary>
    public static float TimeLeft(float elapsed) => Mathf.Max(0f, MatchDuration - elapsed);

    /// <summary>Le chrono est-il fini ? À égalité, le match continue : c'est la prolongation.</summary>
    public static bool TimeUp(float elapsed) => elapsed >= MatchDuration;

    /// <summary>Qui mène ces `points` (sa place dans la liste), ou -1 à égalité en tête, et sans personne.</summary>
    public static int LeaderOf(System.Collections.Generic.IReadOnlyList<int> points)
    {
        int leader = -1, best = int.MinValue;
        bool tied = false;
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] > best)
            {
                best = points[i];
                leader = i;
                tied = false;
            }
            else if (points[i] == best) tied = true;
        }
        return tied ? -1 : leader;
    }

    /// <summary>Des points à la française : « 3 000 » (un espace insécable entre les milliers).</summary>
    public static string Points(int points)
    {
        string digits = Mathf.Abs(points).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var text = new System.Text.StringBuilder(digits.Length + 4);
        if (points < 0) text.Append('-');
        for (int i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) text.Append(' ');
            text.Append(digits[i]);
        }
        return text.ToString();
    }
}
