using UnityEngine;

/// <summary>
/// Le catalogue des accessoires des armes (2026-10-16, GDD § 6) : leurs préfabs, construits dans
/// Blender (Tools/Blender : viseurs.py, laser_tactique.py, poignees.py, chargeurs.py) et importés par
/// Duel Arena > Armes > Importer les armes de Blender, et leurs effets.
///
/// Une seule instance, Assets/_ProjectArena/Weapon/Accessoires/Accessoires.asset, que portent
/// WeaponController (les effets), le préfab joueur (l'arme tenue) et l'armurerie du menu.
/// Les effets se lisent ici, pas dans l'accessoire : un seul endroit à régler en jouant. Depuis le
/// 2026-10-29, ce sont des données (`effects`) : pour chaque accessoire, une liste de caractéristiques
/// et de facteurs, que l'éditeur des armes ajoute, retire et modifie.
/// </summary>
[CreateAssetMenu(fileName = "Accessoires", menuName = "Duel Arena/Catalogue des accessoires")]
public class AttachmentCatalog : ScriptableObject
{
    [Tooltip("Les quinze viseurs, dans l'ordre de WeaponAttachments (1 à 15) : points rouges A à C, holographiques A à D, réflex A à C, le réflex à grand cadre D, puis les lunettes ×2, ×2,5 (A et B) et ×3.")]
    public GameObject[] sights = new GameObject[0];

    [Tooltip("Le laser tactique des armes d'épaule (LT-1), sur le rail du flanc droit.")]
    public GameObject laser;

    [Tooltip("Le laser des pistolets (LT-P), sous le canon.")]
    public GameObject pistolLaser;

    public GameObject verticalGrip;
    public GameObject angledGrip;

    [Tooltip("Les chargeurs grande capacité et rapides (les cellules d'une arme laser), un par modèle d'arme et par sorte (WeaponAttachment.WeaponDesignation, Variant). Le RV-30 n'en a pas : un barillet ne s'allonge pas, l'effet vaut quand même.")]
    public GameObject[] magazines = new GameObject[0];

    [Tooltip("Les crosses légères et lourdes (2026-10-29), une par modèle d'arme et par sorte : chacune remplace la crosse de son arme.")]
    public GameObject[] stocks = new GameObject[0];

    [Tooltip("Ce qui se monte au canon (2026-10-29), un par modèle d'arme et par sorte : compensateur, frein de bouche, canons long et court ; sur une arme laser, ses émetteurs.")]
    public GameObject[] barrels = new GameObject[0];

    [Header("Effets (valeurs provisoires, à juger en jouant)")]
    [Tooltip("Ce que chaque accessoire change à l'arme qui le porte : des caractéristiques et leurs facteurs (0,8 : 20 % de moins). L'éditeur des armes les ajoute, les retire et les modifie (Réglages communs). Un accessoire absent n'a aucun effet.")]
    public AttachmentEffect[] effects = DefaultEffects();

    [Tooltip("Les premiers coups d'une rafale, ceux que change « Recul des premiers coups » (le frein de bouche) : 3, les trois premiers.")]
    [Min(1)] public int firstShots = 3;

    /// <summary>
    /// Les effets de départ (GDD § 6, valeurs provisoires) : la poignée verticale retient le recul vertical
    /// (×0,8) ; la poignée inclinée fait épauler plus vite (×0,8) ; le laser resserre la dispersion à la
    /// hanche (×0,75, comme dans Rainbow Six jusqu'en Y9S1) ; le chargeur grande capacité tient la moitié de
    /// balles en plus (×1,5) et se recharge 15 % plus lentement (proposition de Claude). Sur une arme laser,
    /// c'est la cellule grande capacité : la moitié de chaleur en plus, des vidanges 15 % plus longues.
    ///
    /// Les sept du 2026-10-25 (propositions de Claude choisies par l'utilisateur, chacun avec sa contrepartie) :
    /// la crosse légère (épaulement −15 %, recul +10 %), la lourde (recul −15 %, épaulement +15 %), le
    /// compensateur (recul horizontal −30 %, vertical +10 %), le frein de bouche (premiers coups −25 %, flamme
    /// +50 %), le canon long (portée des dégâts +30 %, sortie et épaulement +10 %), le court (l'inverse, portée
    /// −30 %), le chargeur rapide (rechargement −25 %, balles −20 %). Sur une arme laser, leurs émetteurs et sa
    /// cellule font de même.
    /// </summary>
    public static AttachmentEffect[] DefaultEffects() => new[]
    {
        new AttachmentEffect
        {
            category = AttachmentCategory.Grip, variant = (int)GripKind.Vertical,
            modifiers = new[] { new StatModifier(WeaponStat.VerticalRecoil, 0.8f) },
        },
        new AttachmentEffect
        {
            category = AttachmentCategory.Grip, variant = (int)GripKind.Angled,
            modifiers = new[] { new StatModifier(WeaponStat.AimTime, 0.8f) },
        },
        new AttachmentEffect
        {
            category = AttachmentCategory.Laser, variant = 1,
            modifiers = new[] { new StatModifier(WeaponStat.HipSpread, 0.75f) },
        },
        new AttachmentEffect
        {
            category = AttachmentCategory.Magazine, variant = 1,
            modifiers = new[] { new StatModifier(WeaponStat.MagazineSize, 1.5f), new StatModifier(WeaponStat.ReloadTime, 1.15f) },
        },
        new AttachmentEffect
        {
            category = AttachmentCategory.Stock, variant = (int)StockKind.Light,
            modifiers = new[]
            {
                new StatModifier(WeaponStat.AimTime, 0.85f), new StatModifier(WeaponStat.VerticalRecoil, 1.1f),
                new StatModifier(WeaponStat.HorizontalRecoil, 1.1f),
            },
        },
        new AttachmentEffect
        {
            category = AttachmentCategory.Stock, variant = (int)StockKind.Heavy,
            modifiers = new[]
            {
                new StatModifier(WeaponStat.VerticalRecoil, 0.85f), new StatModifier(WeaponStat.HorizontalRecoil, 0.85f),
                new StatModifier(WeaponStat.AimTime, 1.15f),
            },
        },
        new AttachmentEffect
        {
            category = AttachmentCategory.Barrel, variant = (int)BarrelKind.Compensator,
            modifiers = new[] { new StatModifier(WeaponStat.HorizontalRecoil, 0.7f), new StatModifier(WeaponStat.VerticalRecoil, 1.1f) },
        },
        new AttachmentEffect
        {
            category = AttachmentCategory.Barrel, variant = (int)BarrelKind.MuzzleBrake,
            modifiers = new[] { new StatModifier(WeaponStat.FirstShotsRecoil, 0.75f), new StatModifier(WeaponStat.MuzzleFlash, 1.5f) },
        },
        new AttachmentEffect
        {
            category = AttachmentCategory.Barrel, variant = (int)BarrelKind.Long,
            modifiers = new[]
            {
                new StatModifier(WeaponStat.FalloffRange, 1.3f), new StatModifier(WeaponStat.RaiseTime, 1.1f),
                new StatModifier(WeaponStat.AimTime, 1.1f),
            },
        },
        new AttachmentEffect
        {
            category = AttachmentCategory.Barrel, variant = (int)BarrelKind.Short,
            modifiers = new[]
            {
                new StatModifier(WeaponStat.RaiseTime, 0.9f), new StatModifier(WeaponStat.AimTime, 0.9f),
                new StatModifier(WeaponStat.FalloffRange, 0.7f),
            },
        },
        new AttachmentEffect
        {
            category = AttachmentCategory.Magazine, variant = (int)MagazineKind.Fast,
            modifiers = new[] { new StatModifier(WeaponStat.ReloadTime, 0.75f), new StatModifier(WeaponStat.MagazineSize, 0.8f) },
        },
    };

    [Header("Le faisceau du laser, vu de tous (catégorie C réseautée)")]
    public Material beamMaterial;
    public Material dotMaterial;
    [Tooltip("Largeur du faisceau (m).")]
    public float beamWidth = 0.0035f;
    [Tooltip("Taille apparente du point, halo compris (rapport à sa distance à la caméra qui le regarde) : il reste visible de loin. 0,02 : une dizaine de pixels en 1080p sous 90°.")]
    public float dotAngularSize = 0.02f;
    [Tooltip("Portée du faisceau (m).")]
    public float beamRange = 150f;

    public GameObject SightPrefab(int sight) => sights != null && sight >= 1 && sight <= sights.Length ? sights[sight - 1] : null;

    public GameObject GripPrefab(GripKind grip) => grip switch
    {
        GripKind.Vertical => verticalGrip,
        GripKind.Angled => angledGrip,
        _ => null,
    };

    public GameObject LaserPrefab(bool pistol) => pistol && pistolLaser != null ? pistolLaser : laser;

    /// <summary>Le chargeur grande capacité (ou rapide) d'un modèle d'arme (sa désignation), ou null.</summary>
    public GameObject MagazinePrefab(string designation, MagazineKind kind = MagazineKind.Extended)
        => PerWeapon(magazines, designation, (int)kind);

    /// <summary>La crosse légère ou lourde d'un modèle d'arme, ou null.</summary>
    public GameObject StockPrefab(StockKind kind, string designation) => PerWeapon(stocks, designation, (int)kind);

    /// <summary>Ce qui se monte au canon d'un modèle d'arme, ou null.</summary>
    public GameObject BarrelPrefab(BarrelKind kind, string designation) => PerWeapon(barrels, designation, (int)kind);

    private static GameObject PerWeapon(GameObject[] prefabs, string designation, int variant)
    {
        if (prefabs == null || string.IsNullOrEmpty(designation) || variant <= 0) return null;
        foreach (GameObject prefab in prefabs)
            if (prefab != null && prefab.TryGetComponent(out WeaponAttachment a) && a.WeaponDesignation == designation && a.Variant == variant)
                return prefab;
        return null;
    }

    /// <summary>Le nom d'un viseur (« Point rouge A »), ou celui de la mire de l'arme.</summary>
    public string SightTitle(int sight)
    {
        GameObject prefab = SightPrefab(sight);
        return prefab != null && prefab.TryGetComponent(out WeaponAttachment a) ? a.Title : sight == 0 ? "Mire de l'arme" : $"Viseur {sight}";
    }

    /// <summary>Ce qu'une arme a sans viseur (le viseur 0) : sa lunette d'origine (« Lunette d'origine ×4 »)
    /// sur une arme de précision, sa mire sinon.</summary>
    public static string BaseSightTitle(WeaponData data)
        => data != null && data.scopeMagnification > 1f
            ? $"Lunette d'origine ×{data.scopeMagnification.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))}"
            : "Mire de l'arme";

    /// <summary>Les effets d'un accessoire (sa rubrique et son choix), ou null s'il n'en a pas.</summary>
    public AttachmentEffect EffectOf(AttachmentCategory category, int variant)
    {
        if (effects == null || variant <= 0) return null;
        foreach (AttachmentEffect effect in effects)
            if (effect != null && effect.category == category && effect.variant == variant) return effect;
        return null;
    }

    // ------------------------------------------------------------------
    // Les effets, lus par le joueur et par le serveur. Fonctions pures, testées.
    // ------------------------------------------------------------------

    /// <summary>Les rubriques, dans l'ordre où leurs effets se multiplient : le même partout (les nouvelles à la
    /// fin : les produits d'avant restent les mêmes, au bit près).</summary>
    private static readonly AttachmentCategory[] EffectOrder =
    {
        AttachmentCategory.Sight, AttachmentCategory.Grip, AttachmentCategory.Laser, AttachmentCategory.Magazine,
        AttachmentCategory.Stock, AttachmentCategory.Barrel,
    };

    /// <summary>
    /// Ce que les accessoires de la place `slot` font à une caractéristique : le produit des facteurs de
    /// chaque accessoire monté qui la change, 1 sans effet (ou sans catalogue). Le joueur et le serveur le
    /// calculent sur les mêmes accessoires publiés, dans le même ordre : le même nombre, au bit près.
    /// </summary>
    public static float Factor(AttachmentCatalog catalog, WeaponStat stat, WeaponAttachments attachments, int slot)
    {
        float factor = 1f;
        if (catalog == null) return factor;
        foreach (AttachmentCategory category in EffectOrder)
        {
            AttachmentEffect effect = catalog.EffectOf(category, attachments.Variant(slot, category));
            if (effect == null || effect.modifiers == null) continue;
            foreach (StatModifier modifier in effect.modifiers)
                if (modifier.stat == stat) factor *= modifier.factor;
        }
        return factor;
    }

    /// <summary>Le seul accessoire `variant` de la rubrique `category` (sur la place 0, comme le lisent les
    /// fonctions qui prennent une place) : ce qu'un accessoire fait à lui seul, pour les aperçus.</summary>
    public static WeaponAttachments Only(AttachmentCategory category, int variant)
        => WeaponAttachments.None.WithVariant(0, category, variant);

    /// <summary>La taille du chargeur d'une arme avec les accessoires de la place `slot` (le chargeur grande
    /// capacité y met la moitié de balles en plus). Sans effet, celle de l'arme.</summary>
    public static int MagazineSize(WeaponData data, WeaponAttachments attachments, int slot)
    {
        if (data == null) return 0;
        float factor = Factor(data.attachmentCatalog, WeaponStat.MagazineSize, attachments, slot);
        if (factor == 1f) return data.magazineSize;
        return Mathf.Clamp(Mathf.RoundToInt(data.magazineSize * factor), 1, 255);
    }

    /// <summary>La taille du chargeur avec ou sans le chargeur grande capacité, et rien d'autre.</summary>
    public static int MagazineSize(WeaponData data, bool extended)
        => MagazineSize(data, extended ? MagazineKind.Extended : MagazineKind.Standard);

    /// <summary>La taille du chargeur avec ce seul chargeur.</summary>
    public static int MagazineSize(WeaponData data, MagazineKind kind)
        => MagazineSize(data, Only(AttachmentCategory.Magazine, (int)kind), 0);

    /// <summary>La durée d'un rechargement avec les accessoires de la place `slot`. D'une cartouche, pour une
    /// arme qui recharge cartouche par cartouche : elle ne change pas, le chargeur a seulement plus de
    /// cartouches à glisser.</summary>
    public static float ReloadDuration(WeaponData data, WeaponAttachments attachments, int slot)
    {
        if (data == null) return 0f;
        if (data.reloadPerRound) return data.reloadDuration;
        return data.reloadDuration * Factor(data.attachmentCatalog, WeaponStat.ReloadTime, attachments, slot);
    }

    /// <summary>La durée d'un rechargement avec ou sans le chargeur grande capacité, et rien d'autre.</summary>
    public static float ReloadDuration(WeaponData data, bool extended)
        => ReloadDuration(data, Only(AttachmentCategory.Magazine, extended ? 1 : 0), 0);

    /// <summary>Ce que les accessoires de la place `slot` font au recul, à la hanche comme épaulé : la
    /// poussée verticale et l'horizontale de chaque coup, hors des premiers coups d'une rafale.</summary>
    public static void RecoilFactors(AttachmentCatalog catalog, WeaponAttachments attachments, int slot,
                                     out float vertical, out float horizontal)
    {
        vertical = Factor(catalog, WeaponStat.VerticalRecoil, attachments, slot);
        horizontal = Factor(catalog, WeaponStat.HorizontalRecoil, attachments, slot);
    }

    /// <summary>
    /// La même chose pour le coup `shot` d'une rafale (0 pour le premier) : les premiers coups (firstShots) ont
    /// en plus le « Recul des premiers coups » (le frein de bouche), sur les deux axes. Une arme qui ne
    /// s'enchaîne pas dans son motif (RecoilPattern.Chains) tire chaque coup comme un premier.
    /// </summary>
    public static void RecoilFactors(AttachmentCatalog catalog, WeaponAttachments attachments, int slot, int shot,
                                     out float vertical, out float horizontal)
    {
        RecoilFactors(catalog, attachments, slot, out vertical, out horizontal);
        if (catalog == null || shot < 0 || shot >= Mathf.Max(1, catalog.firstShots)) return;
        float first = Factor(catalog, WeaponStat.FirstShotsRecoil, attachments, slot);
        vertical *= first;
        horizontal *= first;
    }

    /// <summary>Ce que les accessoires de la place `slot` font à la portée des dégâts (WeaponData.DamageAt) : le
    /// canon long la repousse, le court la rapproche. 1 sans effet.</summary>
    public static float FalloffScale(AttachmentCatalog catalog, WeaponAttachments attachments, int slot)
        => Factor(catalog, WeaponStat.FalloffRange, attachments, slot);

    /// <summary>Ce que les accessoires de la place `slot` font à la flamme de bouche (au rayon d'une arme laser),
    /// que voient tous les joueurs. 1 sans effet.</summary>
    public static float MuzzleFlashScale(AttachmentCatalog catalog, WeaponAttachments attachments, int slot)
        => Factor(catalog, WeaponStat.MuzzleFlash, attachments, slot);

    /// <summary>Le temps pour sortir une arme avec les accessoires de la place `slot` (on ne tire pas avant).</summary>
    public static float RaiseTime(WeaponData data, WeaponAttachments attachments, int slot)
    {
        if (data == null) return 0f;
        return data.raiseTime * Factor(data.attachmentCatalog, WeaponStat.RaiseTime, attachments, slot);
    }

    /// <summary>Ce que les accessoires de la place `slot` font à la dispersion à la hanche (le cône de
    /// HipFireSpread, la gerbe d'un pompe) : le laser la resserre. 1 sans effet.</summary>
    public static float HipSpreadFactor(AttachmentCatalog catalog, WeaponAttachments attachments, int slot)
        => Factor(catalog, WeaponStat.HipSpread, attachments, slot);

    /// <summary>Le temps d'épaulement d'une arme avec les accessoires de la place `slot`.</summary>
    public static float AimTime(WeaponData data, WeaponAttachments attachments, int slot)
    {
        if (data == null) return 0f;
        return data.aimTime * Factor(data.attachmentCatalog, WeaponStat.AimTime, attachments, slot);
    }

    /// <summary>Le temps d'épaulement avec cette seule poignée.</summary>
    public static float AimTime(WeaponData data, GripKind grip) => AimTime(data, Only(AttachmentCategory.Grip, (int)grip), 0);
}
