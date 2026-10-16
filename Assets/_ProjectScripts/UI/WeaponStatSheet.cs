using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// La fiche d'une arme à l'armurerie (2026-10-16) : ses caractéristiques en barres, ce que les
/// accessoires y changent, et ce qu'en dit l'IA de supervision. Tout est lu dans les données de l'arme
/// (WeaponData) et dans le catalogue des accessoires (AttachmentCatalog), jamais recopié : une valeur
/// réglée en jouant se voit ici sans rien toucher.
///
/// Chaque barre est la part du meilleur de la panoplie, accessoires compris : une barre pleine est le
/// mieux qu'on puisse avoir, et un accessoire qui aide la remplit. Seuls les dégâts se lisent
/// autrement : la part de la vie d'un robot qu'un tir au buste lui retire, à courte portée (le sniper
/// et une décharge complète de pompe la remplissent).
///
/// Bible § 9 : l'IA ne se trompe jamais. Les nombres qu'elle annonce (coups pour neutraliser un robot)
/// sont calculés comme le serveur compte les dégâts (WeaponData.DamageAt).
///
/// Fonctions pures, testées (WeaponStatSheetTests).
/// </summary>
public static class WeaponStatSheet
{
    public enum Stat : byte
    {
        Damage,
        FireRate,
        Control,        // le recul d'un coup, épaulé
        HipPrecision,   // la dispersion à la hanche, à l'arrêt (le cône, ou la gerbe d'un pompe)
        Handling,       // le temps d'épaulement
        Mobility,
        Capacity,
    }

    /// <summary>Les barres de la fiche, dans l'ordre de l'écran.</summary>
    public static readonly Stat[] Bars =
        { Stat.Damage, Stat.FireRate, Stat.Control, Stat.HipPrecision, Stat.Handling, Stat.Mobility, Stat.Capacity };

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static string Label(Stat stat) => stat switch
    {
        Stat.Damage => "Dégâts",
        Stat.FireRate => "Cadence de tir",
        Stat.Control => "Contrôle",
        Stat.HipPrecision => "Précision à la hanche",
        Stat.Handling => "Épaulement",
        Stat.Mobility => "Mobilité",
        _ => "Capacité",
    };

    /// <summary>Plus haut vaut mieux pour les dégâts, la cadence, la mobilité et la capacité ; plus bas
    /// pour le recul, la dispersion à la hanche et le temps d'épaulement.</summary>
    public static bool HigherIsBetter(Stat stat) => stat != Stat.Control && stat != Stat.HipPrecision && stat != Stat.Handling;

    /// <summary>
    /// La valeur d'une caractéristique, avec les accessoires de la place `slot` : les dégâts d'un tir au
    /// buste (tous ses plombs), les coups par seconde, le recul vertical d'un coup en degrés, la
    /// dispersion à la hanche en degrés (debout à l'arrêt : le cône, ou l'écart moyen des plombs d'une
    /// gerbe), le temps d'épaulement en secondes, la vitesse relative, les balles du chargeur.
    /// </summary>
    public static float Value(Stat stat, WeaponData data, WeaponAttachments attachments, int slot)
    {
        if (data == null) return 0f;
        AttachmentCatalog catalog = data.attachmentCatalog;
        switch (stat)
        {
            case Stat.Damage: return data.damagePerHit * data.PelletCount;
            case Stat.FireRate: return data.shotsPerSecond;
            case Stat.Control:
                AttachmentCatalog.RecoilFactors(catalog, attachments, slot, out float vertical, out _);
                return data.recoilPerShotDegrees * vertical;
            case Stat.HipPrecision:
                return HipDispersion(data) * AttachmentCatalog.HipSpreadFactor(catalog, attachments, slot);
            case Stat.Handling: return AttachmentCatalog.AimTime(data, attachments.Grip(slot), catalog);
            case Stat.Mobility: return data.moveSpeedMultiplier;
            default: return AttachmentCatalog.MagazineSize(data, attachments.ExtendedMagazine(slot), catalog);
        }
    }

    /// <summary>
    /// La barre d'une caractéristique, de 0 à 1 : la part du meilleur de la panoplie, chaque arme avec
    /// les accessoires qu'elle prend et qui servent le mieux cette caractéristique. Les dégâts : la part
    /// de `maxHealth` qu'un tir au buste retire, à courte portée.
    /// </summary>
    public static float Fraction(Stat stat, IReadOnlyList<WeaponData> loadout, int slot, WeaponAttachments attachments, float maxHealth)
    {
        WeaponData data = loadout != null && slot >= 0 && slot < loadout.Count ? loadout[slot] : null;
        if (data == null) return 0f;
        float value = Value(stat, data, attachments, slot);
        if (stat == Stat.Damage) return maxHealth > 0f ? Mathf.Clamp01(value / maxHealth) : 0f;

        bool higher = HigherIsBetter(stat);
        float best = higher ? 0f : float.PositiveInfinity;
        for (int i = 0; i < loadout.Count; i++)
        {
            if (loadout[i] == null) continue;
            float candidate = Value(stat, loadout[i], BestFor(stat, loadout[i], i), i);
            best = higher ? Mathf.Max(best, candidate) : Mathf.Min(best, candidate);
        }

        if (higher) return best > 0f ? Mathf.Clamp01(value / best) : 0f;
        if (value <= 0f) return 1f;   // ni recul, ni temps d'épaulement : on ne fait pas mieux
        return float.IsInfinity(best) ? 0f : Mathf.Clamp01(best / value);
    }

    /// <summary>La dispersion à la hanche d'une arme sans accessoire, debout à l'arrêt (degrés) : son
    /// cône (HipFireSpread), et pour une arme à plombs l'écart moyen de sa gerbe.</summary>
    public static float HipDispersion(WeaponData data)
    {
        if (data == null) return 0f;
        float pellets = 0f;
        if (data.PelletCount > 1)
        {
            for (int i = 0; i < data.PelletCount; i++) pellets += data.PelletOffset(i, false).magnitude;
            pellets /= data.PelletCount;
        }
        return data.hipSpread + pellets;
    }

    /// <summary>Les accessoires que l'arme prend et qui servent le mieux une caractéristique (l'échelle
    /// des barres) : la poignée verticale pour le recul, le laser à la hanche, la poignée inclinée pour
    /// l'épaulement, le chargeur grande capacité.</summary>
    private static WeaponAttachments BestFor(Stat stat, WeaponData data, int slot)
    {
        WeaponAttachments best = WeaponAttachments.None;
        bool vertical = WeaponAttachments.Accepts(data, AttachmentCategory.Grip, (int)GripKind.Vertical);
        switch (stat)
        {
            case Stat.Control:
                if (vertical) best = best.WithGrip(slot, GripKind.Vertical);
                break;
            case Stat.HipPrecision:
                if (WeaponAttachments.Accepts(data, AttachmentCategory.Laser, 1)) best = best.WithLaser(slot, true);
                break;
            case Stat.Handling:
                if (WeaponAttachments.Accepts(data, AttachmentCategory.Grip, (int)GripKind.Angled)) best = best.WithGrip(slot, GripKind.Angled);
                break;
            case Stat.Capacity:
                if (WeaponAttachments.Accepts(data, AttachmentCategory.Magazine, 1)) best = best.WithMagazine(slot, true);
                break;
        }
        return best;
    }

    /// <summary>Une valeur telle que la fiche l'écrit : « 18 × 8 », « 420 coups/min », « 1,2° par
    /// coup », « 1,5° d'écart », « 125 ms », « 95 % », « 45 ».</summary>
    public static string Format(Stat stat, WeaponData data, float value) => stat switch
    {
        Stat.Damage => data != null && data.PelletCount > 1 ? $"{Number(data.damagePerHit)} × {data.PelletCount}" : Number(value),
        Stat.FireRate => $"{Mathf.RoundToInt(value * 60f)} coups/min",
        Stat.Control => $"{Number(value)}° par coup",
        Stat.HipPrecision => $"{Number(value)}° d'écart",
        Stat.Handling => $"{Mathf.RoundToInt(value * 1000f)} ms",
        Stat.Mobility => $"{Mathf.RoundToInt(value * 100f)} %",
        _ => Number(value),
    };

    /// <summary>Une valeur qui change pendant un aperçu, l'unité écrite une fois : « 30 → 45 »,
    /// « 1,2° → 0,96° », « 125 → 100 ms ».</summary>
    public static string FormatChange(Stat stat, WeaponData data, float from, float to) => stat switch
    {
        Stat.Control or Stat.HipPrecision => $"{Number(from)}° → {Number(to)}°",
        Stat.Handling => $"{Mathf.RoundToInt(from * 1000f)} → {Mathf.RoundToInt(to * 1000f)} ms",
        Stat.Mobility => $"{Mathf.RoundToInt(from * 100f)} → {Mathf.RoundToInt(to * 100f)} %",
        Stat.FireRate => $"{Mathf.RoundToInt(from * 60f)} → {Mathf.RoundToInt(to * 60f)} coups/min",
        _ => $"{Format(stat, data, from)} → {Format(stat, data, to)}",
    };

    /// <summary>Un nombre à la française, deux décimales au plus (« 1,2 », « 0,96 », « 17 »).</summary>
    public static string Number(float value) => value.ToString("0.##", French);

    // ------------------------------------------------------------------
    // Les lignes d'information, sous les barres
    // ------------------------------------------------------------------

    /// <summary>« 100 m » pour une arme qui ne perd rien avec la distance ; sinon la distance des dégâts
    /// pleins, puis ce qu'il en reste et où (« 8 m, puis ×0,3 à 20 m »).</summary>
    public static string EffectiveRange(WeaponData data)
    {
        if (data == null) return "";
        if (data.falloffEnd <= data.falloffStart) return $"{Number(data.maxRange)} m";
        return $"{Number(data.falloffStart)} m, puis ×{Number(data.minDamageMultiplier)} à {Number(data.falloffEnd)} m";
    }

    /// <summary>La durée d'un rechargement avec les accessoires de la place `slot` (une cartouche, pour
    /// une arme qui recharge cartouche par cartouche).</summary>
    public static string Reload(WeaponData data, WeaponAttachments attachments, int slot)
    {
        if (data == null) return "";
        float duration = AttachmentCatalog.ReloadDuration(data, attachments.ExtendedMagazine(slot), data.attachmentCatalog);
        return data.reloadPerRound ? $"{Number(duration)} s par cartouche" : $"{Number(duration)} s";
    }

    /// <summary>« Automatique », « Coup par coup », « Coup par coup, 8 plombs ».</summary>
    public static string FireMode(WeaponData data)
    {
        if (data == null) return "";
        string mode = data.isAutomatic ? "Automatique" : "Coup par coup";
        return data.PelletCount > 1 ? $"{mode}, {data.PelletCount} plombs" : mode;
    }

    /// <summary>Les zones qui comptent autrement que le buste : « Tête ×1,4 », « Tête ×1,5, jambes ×0,6 »,
    /// « Aucun bonus ».</summary>
    public static string Zones(WeaponData data)
    {
        if (data == null) return "";
        var parts = new List<string>();
        if (!Mathf.Approximately(data.headMultiplier, 1f)) parts.Add($"tête ×{Number(data.headMultiplier)}");
        if (!Mathf.Approximately(data.legsMultiplier, 1f)) parts.Add($"jambes ×{Number(data.legsMultiplier)}");
        if (parts.Count == 0) return "Aucun bonus";
        string text = string.Join(", ", parts);
        return char.ToUpper(text[0], French) + text.Substring(1);
    }

    // ------------------------------------------------------------------
    // Ce que dit l'IA de supervision
    // ------------------------------------------------------------------

    /// <summary>Le nombre de tirs qui neutralisent un robot à pleine vie (`maxHealth`) dans une zone
    /// (`zoneMultiplier`), à pleins dégâts, tous les plombs d'un tir touchant.</summary>
    public static int ShotsToKill(WeaponData data, float zoneMultiplier, float maxHealth)
    {
        if (data == null) return 0;
        float perShot = data.damagePerHit * data.PelletCount * zoneMultiplier;
        // La marge absorbe l'erreur des flottants sur une division juste (100 / 100).
        return perShot > 0f ? Mathf.CeilToInt(maxHealth / perShot - 1e-4f) : 0;
    }

    /// <summary>
    /// Ce que l'IA dit d'une classe d'arme en l'ouvrant : son mode de tir, sa cadence, et ce qu'il faut
    /// pour neutraliser un robot à pleine vie, à pleins dégâts (« Fusil d'assaut. Automatique, 420 coups
    /// par minute. Il faut 6 impacts au buste pour neutraliser un robot, 5 à la tête. »).
    /// </summary>
    public static string Brief(WeaponData data, float maxHealth)
    {
        if (data == null) return "";
        bool pellets = data.PelletCount > 1;
        string Unit(int n) => pellets ? (n > 1 ? "décharges complètes" : "décharge complète") : (n > 1 ? "impacts" : "impact");

        int body = ShotsToKill(data, 1f, maxHealth);
        int head = ShotsToKill(data, data.headMultiplier, maxHealth);
        int legs = ShotsToKill(data, data.legsMultiplier, maxHealth);
        bool headCounts = !Mathf.Approximately(data.headMultiplier, 1f);
        bool legsCount = !Mathf.Approximately(data.legsMultiplier, 1f);

        string where = headCounts && head == body ? "au buste ou à la tête" : "au buste";
        string kill = $"il faut {body} {Unit(body)} {where} pour neutraliser un robot";
        if (headCounts && head != body) kill += $", {head} à la tête";
        if (legsCount && legs != body) kill += $", {legs} aux jambes";
        kill = data.falloffEnd > data.falloffStart
            ? $"Jusqu'à {Number(data.falloffStart)} m, {kill}."
            : char.ToUpper(kill[0], French) + kill.Substring(1) + ".";

        return $"{data.weaponName}. {FireMode(data)}, {Mathf.RoundToInt(data.shotsPerSecond * 60f)} coups par minute. {kill}";
    }
}
