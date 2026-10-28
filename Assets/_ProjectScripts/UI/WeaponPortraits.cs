using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Le portrait de chaque arme (2026-10-27, demandé par l'utilisateur) : de profil, sans accessoire, dans sa
/// finition d'origine, à côté de son nom sur les boutons qui la choisissent (la préparation d'une manche, la
/// fenêtre du stand). Rendus par les vignettes du menu (MenuThumbnails.PrepareWeaponPortraits), sous les
/// lumières de son plateau, puis gardés jusqu'à la fermeture du jeu : l'arène et le stand n'ont pas ce
/// plateau, et toute partie commence au menu.
/// </summary>
public static class WeaponPortraits
{
    /// <summary>La taille d'un portrait, en pixels : le double de sa place dans l'interface, pour rester net.</summary>
    public const int Width = 256, Height = 96;

    private static readonly Dictionary<WeaponData, Texture2D> portraits = new();

    /// <summary>Le portrait de `data` ; null tant que le menu ne l'a pas rendu.</summary>
    public static Texture2D Of(WeaponData data)
        => data != null && portraits.TryGetValue(data, out Texture2D portrait) && portrait != null ? portrait : null;

    /// <summary>Garde le portrait de `data` (celui qu'il remplace est détruit).</summary>
    internal static void Keep(WeaponData data, Texture2D portrait)
    {
        if (data == null || portrait == null) return;
        if (portraits.TryGetValue(data, out Texture2D previous) && previous != null && previous != portrait) Release(previous);
        portraits[data] = portrait;
    }

    /// <summary>Oublie tous les portraits et détruit leurs textures.</summary>
    public static void Clear()
    {
        foreach (Texture2D portrait in portraits.Values)
            if (portrait != null) Release(portrait);
        portraits.Clear();
    }

    private static void Release(Texture2D portrait)
    {
        if (Application.isPlaying) Object.Destroy(portrait);
        else Object.DestroyImmediate(portrait);
    }

    // Le rechargement de domaine est coupé à l'entrée en Play Mode : les portraits d'une session
    // précédente sont détruits, le menu les rend à nouveau.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Clear();
}
