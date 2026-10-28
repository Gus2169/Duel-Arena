using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Les meilleurs temps de la Fosse (2026-10-16), par arme : la place de la panoplie (0 à 11) avec
/// laquelle le parcours a été couru, ou -1 pour une course où l'on a changé d'arme. Gardés sur la machine
/// (PlayerPrefs), comme les réglages : le stand tourne chez le joueur, seul.
///
/// Chaque record n'est lu qu'une fois par lancement puis gardé en mémoire : PlayerPrefs relit le
/// registre de Windows à chaque appel (voir les pièges de CLAUDE.md).
/// </summary>
public static class CourseRecords
{
    /// <summary>La clé d'une course où l'on a changé d'arme.</summary>
    public const int MixedLoadout = -1;

    // La Fosse refaite le 2026-10-28 (douze sections, plus longue) a ses propres records : ceux de la première, sous
    // « LaFosse.Record. », ne se battraient plus et ne sont plus lus.
    private const string Prefix = "LaFosse2.Record.";
    private static readonly Dictionary<int, float> cache = new Dictionary<int, float>();

    /// <summary>Un record vient de tomber (le tableau du départ se réécrit).</summary>
    public static event System.Action Changed;

    /// <summary>Le meilleur temps final (s) pour cette arme, NaN s'il n'y en a pas.</summary>
    public static float Best(int weaponKey)
    {
        if (cache.TryGetValue(weaponKey, out float best)) return best;
        best = PlayerPrefs.GetFloat(Prefix + KeyName(weaponKey), float.NaN);
        cache[weaponKey] = best;
        return best;
    }

    /// <summary>Retient `finalTime` s'il bat le record de cette arme. Rend vrai si c'est un record ;
    /// `previous` est l'ancien (NaN s'il n'y en avait pas).</summary>
    public static bool TryRecord(int weaponKey, float finalTime, out float previous)
    {
        previous = Best(weaponKey);
        if (!IsImprovement(previous, finalTime)) return false;
        cache[weaponKey] = finalTime;
        PlayerPrefs.SetFloat(Prefix + KeyName(weaponKey), finalTime);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Un temps bat-il le précédent ? Un temps invalide ne bat rien ; sans précédent, tout
    /// temps valide est un record. Fonction pure, testée.</summary>
    public static bool IsImprovement(float previous, float candidate)
    {
        if (float.IsNaN(candidate) || float.IsInfinity(candidate) || candidate < 0f) return false;
        return float.IsNaN(previous) || candidate < previous;
    }

    private static string KeyName(int weaponKey) => weaponKey < 0 ? "mixte" : weaponKey.ToString();
}
