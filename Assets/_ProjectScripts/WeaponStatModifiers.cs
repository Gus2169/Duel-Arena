using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Une caractéristique de l'arme qu'un accessoire change (2026-10-29, GDD § 6 : l'utilisateur veut ajouter,
/// retirer et modifier les effets dans l'éditeur des armes, sans code). Chacune est lue à un seul endroit, le
/// même chez le joueur et sur le serveur (AttachmentCatalog.Factor). N'y figurent que celles que le jeu lit :
/// une caractéristique proposée dans l'éditeur doit changer quelque chose.
/// 🚨 Enregistrée par sa valeur entière (Accessoires.asset) : ajouter les nouvelles à la fin.
/// </summary>
public enum WeaponStat
{
    /// <summary>La poussée verticale de chaque coup (RecoilPattern), épaulé comme à la hanche.</summary>
    VerticalRecoil = 0,
    /// <summary>La poussée horizontale de chaque coup.</summary>
    HorizontalRecoil = 1,
    /// <summary>La dispersion à la hanche : le cône (HipFireSpread), la gerbe d'un pompe.</summary>
    HipSpread = 2,
    /// <summary>Le temps d'épaulement.</summary>
    AimTime = 3,
    /// <summary>Les balles du chargeur, ou la chaleur que tient une arme laser avant la surchauffe.</summary>
    MagazineSize = 4,
    /// <summary>Le rechargement d'un chargeur, ou les vidanges d'une arme laser ; pas une cartouche glissée
    /// une à une, qui se glisse toujours aussi vite.</summary>
    ReloadTime = 5,
    /// <summary>Le temps pour sortir l'arme (WeaponData.raiseTime : on ne tire pas avant), 2026-10-29.</summary>
    RaiseTime = 6,
    /// <summary>Les distances où les dégâts commencent et finissent de baisser (WeaponData.falloffStart,
    /// falloffEnd) ; rien sur une arme dont les dégâts ne baissent pas. 2026-10-29.</summary>
    FalloffRange = 7,
    /// <summary>La poussée des premiers coups d'une rafale (AttachmentCatalog.firstShots), sur les deux axes.
    /// 2026-10-29.</summary>
    FirstShotsRecoil = 8,
    /// <summary>La flamme de bouche que voient tous les joueurs ; sur une arme laser, l'épaisseur du rayon.
    /// Catégorie C réseautée : elle ne change que ce qu'on voit du tireur. 2026-10-29.</summary>
    MuzzleFlash = 9,
}

/// <summary>Un effet : une caractéristique et le facteur qui la multiplie (0,8 : 20 % de moins ; 1,5 : la
/// moitié en plus).</summary>
[Serializable]
public struct StatModifier
{
    public WeaponStat stat;
    public float factor;

    public StatModifier(WeaponStat stat, float factor)
    {
        this.stat = stat;
        this.factor = factor;
    }
}

/// <summary>Les effets d'un accessoire, désigné par sa rubrique et son choix (WeaponAttachments.Variant).</summary>
[Serializable]
public class AttachmentEffect
{
    public AttachmentCategory category;
    public int variant;
    public StatModifier[] modifiers = new StatModifier[0];
}

/// <summary>
/// Ce que disent les effets, en français : le nom de chaque caractéristique (dans l'éditeur, et pour une arme
/// donnée dans l'armurerie : des « cartouches » au pompe, la « vidange » d'une arme laser), et ce qu'un effet
/// y change (« Recul vertical −20 % »). Fonctions pures, testées.
/// </summary>
public static class WeaponStats
{
    /// <summary>Toutes les caractéristiques, dans l'ordre de l'éditeur.</summary>
    public static readonly WeaponStat[] All = (WeaponStat[])Enum.GetValues(typeof(WeaponStat));

    /// <summary>Le nom d'une caractéristique, pour toutes les armes (l'éditeur).</summary>
    public static string Label(WeaponStat stat) => stat switch
    {
        WeaponStat.VerticalRecoil => "Recul vertical",
        WeaponStat.HorizontalRecoil => "Recul horizontal",
        WeaponStat.HipSpread => "Dispersion à la hanche",
        WeaponStat.AimTime => "Épaulement",
        WeaponStat.MagazineSize => "Chargeur",
        WeaponStat.ReloadTime => "Rechargement",
        WeaponStat.RaiseTime => "Sortie de l'arme",
        WeaponStat.FalloffRange => "Portée des dégâts",
        WeaponStat.FirstShotsRecoil => "Recul des premiers coups",
        WeaponStat.MuzzleFlash => "Flamme de bouche",
        _ => stat.ToString(),
    };

    /// <summary>Ce que couvre une caractéristique (l'aide de l'éditeur).</summary>
    public static string Help(WeaponStat stat) => stat switch
    {
        WeaponStat.VerticalRecoil => "La poussée verticale de chaque coup, épaulé comme à la hanche.",
        WeaponStat.HorizontalRecoil => "La poussée horizontale de chaque coup.",
        WeaponStat.HipSpread => "Le cône du tir à la hanche, et la gerbe d'un pompe à la hanche.",
        WeaponStat.AimTime => "Le temps pour épauler.",
        WeaponStat.MagazineSize => "Les balles du chargeur ; pour une arme laser, la chaleur qu'elle tient avant la surchauffe.",
        WeaponStat.ReloadTime => "Le rechargement d'un chargeur, ou les vidanges d'une arme laser. Pas une cartouche glissée une à une.",
        WeaponStat.RaiseTime => "Le temps pour sortir l'arme : on ne tire pas avant (une arme ramassée, un changement d'arme).",
        WeaponStat.FalloffRange => "Les distances où les dégâts commencent et finissent de baisser. Rien sur une arme dont les dégâts ne baissent pas.",
        WeaponStat.FirstShotsRecoil => "La poussée des premiers coups d'une rafale, sur les deux axes (combien de coups : ci-dessous). Une arme plus lente que la remise à zéro de son motif n'a que des premiers coups.",
        WeaponStat.MuzzleFlash => "La flamme de bouche, que voient tous les joueurs ; sur une arme laser, l'épaisseur du rayon. Elle ne change que ce qu'on voit du tireur.",
        _ => "",
    };

    /// <summary>La caractéristique change-t-elle quelque chose à cette arme ? Une cartouche glissée une à une
    /// se glisse toujours aussi vite : le rechargement d'un pompe ne change pas ; une arme dont les dégâts ne
    /// baissent pas n'a pas de portée des dégâts à changer.</summary>
    public static bool Applies(WeaponStat stat, WeaponData data) => stat switch
    {
        WeaponStat.ReloadTime => data == null || !data.reloadPerRound,
        WeaponStat.FalloffRange => data == null || data.HasFalloff,
        _ => true,
    };

    /// <summary>Le nom d'une caractéristique pour une arme donnée, en minuscules (l'armurerie) : les coups d'un
    /// chargeur, les cartouches d'un tube, les tirs avant la surchauffe d'une arme laser, sa vidange.</summary>
    public static string LabelFor(WeaponStat stat, WeaponData data)
    {
        bool heat = data != null && data.UsesHeat;
        return stat switch
        {
            WeaponStat.MagazineSize => heat ? "tirs avant surchauffe" : data != null && data.reloadPerRound ? "cartouches" : "coups",
            WeaponStat.ReloadTime => heat ? "vidange" : "recharge",
            WeaponStat.MuzzleFlash => data != null && data.shotIsBeam ? "épaisseur du rayon" : "flamme de bouche",
            WeaponStat.RaiseTime => "sortie de l'arme",
            WeaponStat.FalloffRange => "portée des dégâts",
            WeaponStat.FirstShotsRecoil => "recul des premiers coups",
            _ => Label(stat).ToLowerInvariant(),
        };
    }

    /// <summary>Un facteur dit en pourcentage : « −20 % », « +50 % ».</summary>
    public static string Percent(float factor)
        => factor < 1f ? $"−{Mathf.RoundToInt((1f - factor) * 100f)} %" : $"+{Mathf.RoundToInt((factor - 1f) * 100f)} %";

    /// <summary>
    /// Les effets d'un accessoire sur une arme, en une ligne : « Coups +50 %, recharge +15 % ». Ceux qui ne
    /// changent rien à cette arme (un facteur de 1, le rechargement d'un pompe) se taisent ; « Aucun effet »
    /// s'il n'en reste pas.
    /// </summary>
    public static string Describe(AttachmentEffect effect, WeaponData data)
    {
        var parts = new List<string>();
        if (effect != null && effect.modifiers != null)
        {
            foreach (StatModifier modifier in effect.modifiers)
            {
                if (modifier.factor == 1f || !Applies(modifier.stat, data)) continue;
                parts.Add($"{LabelFor(modifier.stat, data)} {Percent(modifier.factor)}");
            }
        }
        if (parts.Count == 0) return "Aucun effet";
        string line = string.Join(", ", parts);
        return char.ToUpperInvariant(line[0]) + line.Substring(1);
    }
}
