using UnityEngine;

/// <summary>
/// Les éclairs du jeu (2026-10-16) : des lignes brisées d'un point à un autre, comme un arc électrique
/// qui cherche son chemin dans l'air. La décharge du gant s'en sert (TaserSparkFX), et le grésillement
/// d'un spectateur étourdi (SpectatorController). Fonctions pures et déterministes : la même graine
/// donne le même éclair, ce qui lui laisse suivre le poing d'une image à l'autre sans changer de forme.
///
/// Un éclair se construit par déplacements du milieu : le milieu du segment qui va du départ à
/// l'arrivée est décalé de côté, puis le milieu de chaque moitié, et ainsi de suite, chaque fois d'une
/// fraction de la longueur du segment (`jaggedness`). Peu de générations donnent des zigzags francs,
/// dans la manière low poly du projet, plutôt qu'un bruit fin.
/// </summary>
public static class ElectricArc
{
    /// <summary>Le nombre de points d'un éclair de `generations` générations.</summary>
    public static int PointCount(int generations) => (1 << generations) + 1;

    /// <summary>
    /// Écrit dans `points` l'éclair qui va de `start` à `end` : PointCount(generations) points, le
    /// premier au départ, le dernier à l'arrivée. `bow` courbe l'éclair : son milieu en est d'abord
    /// décalé (m), puis il se brise. La graine avance.
    /// </summary>
    public static void Build(Vector3 start, Vector3 end, int generations, float jaggedness, Vector3 bow,
                             ref uint seed, Vector3[] points)
    {
        int last = PointCount(generations) - 1;
        points[0] = start;
        points[last] = end;
        for (int step = last; step > 1; step >>= 1)
        {
            int half = step >> 1;
            for (int i = half; i < last; i += step)
            {
                Vector3 a = points[i - half], b = points[i + half];
                Vector3 mid = (a + b) * 0.5f;
                if (step == last) mid += bow;
                float length = Vector3.Distance(a, b);
                if (length > 1e-6f)
                {
                    // Jamais presque droit : un arc se brise à chaque génération.
                    float reach = length * jaggedness * (0.4f + 0.6f * Next01(ref seed));
                    mid += RandomPerpendicular((b - a) / length, ref seed) * reach;
                }
                points[i] = mid;
            }
        }
    }

    /// <summary>Un vecteur unitaire perpendiculaire à `direction` (unitaire), au hasard de la graine.</summary>
    public static Vector3 RandomPerpendicular(Vector3 direction, ref uint seed)
    {
        Vector3 helper = Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right;
        Vector3 u = Vector3.Cross(direction, helper).normalized;
        Vector3 v = Vector3.Cross(direction, u);
        float angle = Next01(ref seed) * (2f * Mathf.PI);
        return u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
    }

    /// <summary>`direction` (unitaire) penchée de `degrees` degrés, d'un côté tiré au hasard.</summary>
    public static Vector3 Tilt(Vector3 direction, float degrees, ref uint seed)
    {
        Vector3 axis = RandomPerpendicular(direction, ref seed);
        return Quaternion.AngleAxis(degrees, axis) * direction;
    }

    /// <summary>Un point de la sphère unité, au hasard de la graine (uniforme).</summary>
    public static Vector3 OnSphere(ref uint seed)
    {
        float z = Next01(ref seed) * 2f - 1f;
        float angle = Next01(ref seed) * (2f * Mathf.PI);
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        return new Vector3(r * Mathf.Cos(angle), z, r * Mathf.Sin(angle));
    }

    /// <summary>Mélange un entier (lowbias32, de Chris Wellons) : une graine par éclair et par forme.</summary>
    public static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352dU;
        x ^= x >> 15;
        x *= 0x846ca68bU;
        x ^= x >> 16;
        return x;
    }

    /// <summary>Un nombre de [0 ; 1[, et la graine avance.</summary>
    public static float Next01(ref uint seed)
    {
        seed = Hash(seed + 0x9e3779b9U);
        return (seed >> 8) * (1f / 16777216f);
    }

    /// <summary>
    /// Le point que la caméra des bras (FirstPersonView : empilée sur celle du joueur, au même endroit)
    /// montre à l'écran là où la caméra du joueur montre `world`. Elles ne diffèrent que par leur champ :
    /// la profondeur est gardée et l'écart à l'axe ramené dans le rapport des tangentes de leurs
    /// demi-champs. Un point sur l'axe ne bouge pas (c'est ce qui suffisait au laser, TacticalLaser).
    /// </summary>
    public static Vector3 WorldToArms(Vector3 world, Transform eye, float playerFieldOfView, float armsFieldOfView)
    {
        Vector3 local = eye.InverseTransformPoint(world);
        float k = Mathf.Tan(armsFieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(playerFieldOfView * 0.5f * Mathf.Deg2Rad);
        return eye.TransformPoint(new Vector3(local.x * k, local.y * k, local.z));
    }
}
