using UnityEngine;

/// <summary>
/// Les gestes de l'éditeur des armes sur un motif (2026-10-26) : le motif de recul
/// (WeaponData.recoilPattern, les poussées coup par coup, en multiples de la force) et la gerbe
/// (WeaponData.pelletPattern, les écarts des plombs en degrés). Chaque geste rend un nouveau tableau :
/// l'appelant l'écrit dans l'asset, sous l'Undo. Fonctions pures, testées (RecoilPatternTests).
/// </summary>
public static class RecoilPatternEditing
{
    /// <summary>Le motif d'au moins `count` coups : ceux qu'on ajoute répètent le dernier, comme le jeu
    /// le fait au-delà du motif (RecoilPattern.Kick). Rien ne change donc en jeu.</summary>
    public static Vector2[] Extended(Vector2[] pattern, int count)
    {
        int length = pattern != null ? pattern.Length : 0;
        var result = new Vector2[Mathf.Max(length, count)];
        Vector2 last = length > 0 ? pattern[length - 1] : Vector2.zero;
        for (int i = 0; i < result.Length; i++) result[i] = i < length ? pattern[i] : last;
        return result;
    }

    /// <summary>Le motif ramené à `count` coups (un au moins) : coupé, ou prolongé par le dernier.</summary>
    public static Vector2[] Resized(Vector2[] pattern, int count)
    {
        Vector2[] result = Extended(pattern, Mathf.Max(1, count));
        if (result.Length > count) System.Array.Resize(ref result, Mathf.Max(1, count));
        return result;
    }

    /// <summary>
    /// Le motif quand on déplace le coup `shot` (1 pour le deuxième : le premier part toujours dans
    /// l'axe) de `delta`, en multiples de la force (chaque axe déjà rapporté à l'échelle affichée). Le
    /// coup se trouve à la somme des poussées des coups d'avant : celle du coup précédent prend le
    /// déplacement. Seul ce coup bouge (la poussée du coup suivant rend le déplacement), ou toute la
    /// suite avec lui (`withFollowing`). Tous les coups montrés (`shownShots`) deviennent explicites
    /// avant : sinon la répétition du dernier ferait bouger la fin de la rafale.
    /// </summary>
    public static Vector2[] MoveShot(Vector2[] pattern, int shot, Vector2 delta, bool withFollowing, int shownShots)
    {
        bool keepNext = !withFollowing && shot + 1 < shownShots;
        Vector2[] result = Extended(pattern, Mathf.Max(shownShots - 1, keepNext ? shot + 1 : shot));
        if (shot < 1 || shot > result.Length) return result;
        result[shot - 1] += delta;
        if (keepNext) result[shot] -= delta;
        return result;
    }

    /// <summary>Le motif (ou la gerbe) en miroir, la gauche pour la droite.</summary>
    public static Vector2[] Mirrored(Vector2[] pattern)
    {
        var result = pattern != null ? (Vector2[])pattern.Clone() : new Vector2[0];
        for (int i = 0; i < result.Length; i++) result[i].x = -result[i].x;
        return result;
    }

    /// <summary>Le motif (ou la gerbe), chaque axe multiplié.</summary>
    public static Vector2[] Scaled(Vector2[] pattern, Vector2 factor)
    {
        var result = pattern != null ? (Vector2[])pattern.Clone() : new Vector2[0];
        for (int i = 0; i < result.Length; i++) result[i] = Vector2.Scale(result[i], factor);
        return result;
    }

    /// <summary>Les positions des coups (degrés, depuis l'axe du premier) que donne le motif à
    /// l'échelle `scale` (degrés par unité du motif, sur chaque axe), sans le retour du regard.</summary>
    public static Vector2[] Positions(Vector2[] pattern, int shots, Vector2 scale)
    {
        var positions = new Vector2[Mathf.Max(0, shots)];
        Vector2 aim = Vector2.zero;
        for (int shot = 0; shot < positions.Length; shot++)
        {
            positions[shot] = aim;
            if (pattern != null && pattern.Length > 0)
                aim += Vector2.Scale(pattern[Mathf.Min(shot, pattern.Length - 1)], scale);
        }
        return positions;
    }

    /// <summary>Le plomb `index` retiré de la gerbe.</summary>
    public static Vector2[] Without(Vector2[] pattern, int index)
    {
        if (pattern == null || index < 0 || index >= pattern.Length) return pattern != null ? (Vector2[])pattern.Clone() : new Vector2[0];
        var result = new Vector2[pattern.Length - 1];
        for (int i = 0, j = 0; i < pattern.Length; i++)
            if (i != index) result[j++] = pattern[i];
        return result;
    }

    /// <summary>La gerbe avec un plomb de plus, à `offset`.</summary>
    public static Vector2[] With(Vector2[] pattern, Vector2 offset)
    {
        int length = pattern != null ? pattern.Length : 0;
        var result = new Vector2[length + 1];
        for (int i = 0; i < length; i++) result[i] = pattern[i];
        result[length] = offset;
        return result;
    }
}
