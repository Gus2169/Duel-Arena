using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>La poignée avant d'une arme (GDD § 6, 2026-10-16), comme dans Rainbow Six : une seule.
/// 🚨 Réseautée par sa valeur entière : ajouter les nouvelles à la fin.</summary>
public enum GripKind : byte
{
    None = 0,
    Vertical = 1,   // retient le recul
    Angled = 2,     // fait viser plus vite
}

/// <summary>Le chargeur d'une arme (GDD § 6) : le sien, le grand (2026-10-16 : la moitié de balles en plus) ou
/// le rapide (2026-10-29 : rechargé plus vite, moins de balles) ; sur une arme laser, sa cellule. Un seul à la
/// fois. 🚨 Réseauté par sa valeur entière : ajouter les nouveaux à la fin.</summary>
public enum MagazineKind : byte
{
    Standard = 0,
    Extended = 1,
    Fast = 2,
}

/// <summary>La crosse d'une arme (GDD § 6, 2026-10-29) : la sienne, une légère (on épaule plus vite, l'arme
/// recule plus) ou une lourde (l'inverse). 🚨 Réseautée par sa valeur entière : ajouter les nouvelles à la fin.</summary>
public enum StockKind : byte
{
    Standard = 0,
    Light = 1,
    Heavy = 2,
}

/// <summary>Ce qui se monte au canon (GDD § 6, 2026-10-29 : un seul emplacement, comme Rainbow Six) : un
/// compensateur, un frein de bouche, un canon long ou court ; sur une arme laser, leurs émetteurs à elles
/// (stabilisateur, dissipateur, focaliseurs). Le codage garde trois places libres (WeaponAttachments).
/// 🚨 Réseauté par sa valeur entière : ajouter les nouveaux à la fin.</summary>
public enum BarrelKind : byte
{
    Standard = 0,
    Compensator = 1,
    MuzzleBrake = 2,
    Long = 3,
    Short = 4,
}

/// <summary>Les rubriques qu'une arme prend à l'armurerie (WeaponData.acceptedAttachments).
/// 🚨 Enregistrées par leur valeur entière dans les WeaponData : ajouter les nouvelles à la fin.</summary>
[Flags]
public enum AttachmentSlots : byte
{
    None = 0,
    Sight = 1,
    Grip = 2,
    Laser = 4,
    Magazine = 8,
    Stock = 16,
    Barrel = 32,
    All = Sight | Grip | Laser | Magazine | Stock | Barrel,
}

/// <summary>Les familles des viseurs (WeaponData.acceptedSights) : les points rouges (viseurs 1 à 3), les
/// holographiques (4 à 7), les réflex (8 à 10), le réflex à grand cadre (11, le « Vintage », trop large pour
/// la glissière d'un pistolet) et les lunettes (12 à 15, grossissantes : les armes de précision).</summary>
[Flags]
public enum SightFamilies : byte
{
    None = 0,
    RedDot = 1,
    Holographic = 2,
    Reflex = 4,
    Scope = 8,
    WideReflex = 16,
    /// <summary>Les viseurs sans grossissement.</summary>
    NonMagnified = RedDot | Holographic | Reflex | WideReflex,
    All = NonMagnified | Scope,
}

/// <summary>Les chargeurs qu'une arme prend (WeaponData.acceptedMagazines) : une arme qui recharge cartouche
/// par cartouche ne prend pas le rapide, qui ne lui laisserait que sa contrepartie (2026-10-29).</summary>
[Flags]
public enum MagazineKinds : byte
{
    None = 0,
    Extended = 1,
    Fast = 2,
    All = Extended | Fast,
}

/// <summary>Ce qu'une arme prend au canon (WeaponData.acceptedBarrels) : les canons long et court seulement si
/// ses dégâts baissent avec la distance, sinon l'un n'aurait que sa contrepartie et l'autre que son avantage
/// (l'utilisateur, 2026-10-29).</summary>
[Flags]
public enum BarrelKinds : byte
{
    None = 0,
    Compensator = 1,
    MuzzleBrake = 2,
    Long = 4,
    Short = 8,
    MuzzleDevices = Compensator | MuzzleBrake,
    Barrels = Long | Short,
    All = MuzzleDevices | Barrels,
}

/// <summary>
/// Les accessoires des armes d'un joueur (2026-10-16, GDD § 6) : pour chaque arme de l'arsenal, le
/// viseur (0 : la mire de l'arme, ou la lunette d'origine d'une arme de précision ; 1 à 15 : les quinze
/// viseurs, dans l'ordre du catalogue : SightCount les prend tous), la poignée, le laser tactique, le
/// chargeur (grand ou rapide ; la cellule d'une arme laser), la crosse et le canon (2026-10-29). Chaque arme
/// garde les siens (2026-10-26) : en match, elle arrive configurée.
///
/// Contrairement à l'apparence (WeaponLooks), ils CHANGENT l'arme (AttachmentCatalog en porte les effets).
/// Le joueur les annonce en se connectant (SessionFlow), le serveur les borne (Sanitized : ce que l'arme ne
/// prend pas tombe) et les publie à l'apparition du robot (PlayerWeaponAttachments) ; le joueur et le serveur
/// comptent ensuite le même chargeur. Tous ouverts à tous dès le départ : un choix de configuration, jamais un
/// avantage débloqué (GDD § 14).
///
/// Tient sur 256 bits, 16 par arme et seize armes au plus (MaxSlots peut grandir jusque-là sans changer le
/// codage) : 4 pour le viseur, 2 pour la poignée, 1 pour le laser, 2 pour le chargeur, 2 pour la crosse, 3 pour
/// le canon, 2 libres. Les huit premiers bits d'une arme sont ceux d'avant le 2026-10-29 (8 bits par arme) :
/// une chaîne de cette époque se relit telle quelle (TryParseHex). Fonctions pures, testées.
/// </summary>
[Serializable]
public struct WeaponAttachments : IEquatable<WeaponAttachments>, Unity.Netcode.INetworkSerializeByMemcpy
{
    public const int Slots = WeaponController.MaxSlots;
    /// <summary>Les places que le codage peut tenir : seize armes.</summary>
    public const int Capacity = 16;
    /// <summary>Les viseurs du catalogue (la mire de l'arme, 0, en plus). 🚨 Quatre bits : quinze au plus, tous
    /// pris depuis la refonte des viseurs (2026-10-28) ; un seizième demandera un bit de plus (il y en a deux
    /// de libres).</summary>
    public const int SightCount = 15;
    private const int BitsPerSlot = 16;
    private const int SlotsPerWord = 4;
    /// <summary>Les chiffres hexadécimaux du codage (ToHex), et ceux du codage d'avant le 2026-10-29.</summary>
    public const int HexLength = 64;
    public const int LegacyHexLength = 24;

    private const int SightShift = 0, SightWidth = 4;
    private const int GripShift = 4, GripWidth = 2;
    private const int LaserShift = 6, LaserWidth = 1;
    private const int MagazineShift = 7, MagazineWidth = 2;
    private const int StockShift = 9, StockWidth = 2;
    private const int BarrelShift = 11, BarrelWidth = 3;

    /// <summary>Les rubriques, dans l'ordre de l'armurerie.</summary>
    public static readonly AttachmentCategory[] Categories =
    {
        AttachmentCategory.Sight, AttachmentCategory.Barrel, AttachmentCategory.Grip, AttachmentCategory.Laser,
        AttachmentCategory.Magazine, AttachmentCategory.Stock,
    };

    [SerializeField] private ulong w0, w1, w2, w3;   // les places 0 à 3, 4 à 7, 8 à 11, 12 à 15

    public WeaponAttachments(ulong w0, ulong w1, ulong w2, ulong w3)
    {
        this.w0 = w0;
        this.w1 = w1;
        this.w2 = w2;
        this.w3 = w3;
    }

    /// <summary>Aucun accessoire : la mire de l'arme, ni poignée, ni laser, le chargeur, la crosse et le canon
    /// d'origine.</summary>
    public static WeaponAttachments None => default;

    /// <summary>
    /// Ceux d'avant le 2026-10-29 : 8 bits par arme, les huit premières dans `low`, les autres dans `high`. Les
    /// rubriques gardent leurs bits : chaque arme les retrouve dans les huit premiers des siens.
    /// </summary>
    public static WeaponAttachments FromLegacy(ulong low, uint high)
    {
        WeaponAttachments result = None;
        for (int slot = 0; slot < 12; slot++)
        {
            uint bits = slot < 8 ? (uint)((low >> (8 * slot)) & 0xFFUL) : (high >> (8 * (slot - 8))) & 0xFFu;
            result = result.WithSlotBits(slot, bits);
        }
        return result;
    }

    /// <summary>En 64 chiffres hexadécimaux (la demande de connexion, les réglages du joueur).</summary>
    public string ToHex() => w3.ToString("X16") + w2.ToString("X16") + w1.ToString("X16") + w0.ToString("X16");

    /// <summary>
    /// Relit ToHex. Une chaîne de 24 chiffres au plus est du codage d'avant le 2026-10-29 (une build plus
    /// ancienne, des réglages enregistrés avant) : elle se relit dans son codage (FromLegacy), complétée de zéros
    /// à gauche. Faux si ce n'est pas de l'hexadécimal ou si elle est trop longue.
    /// </summary>
    public static bool TryParseHex(string text, out WeaponAttachments value)
    {
        value = None;
        if (text == null) return false;
        text = text.Trim();
        if (text.Length == 0) return true;
        if (text.Length > HexLength) return false;
        if (text.Length <= LegacyHexLength)
        {
            text = text.PadLeft(LegacyHexLength, '0');
            if (!uint.TryParse(text.Substring(0, 8), System.Globalization.NumberStyles.HexNumber, null, out uint high)) return false;
            if (!ulong.TryParse(text.Substring(8), System.Globalization.NumberStyles.HexNumber, null, out ulong low)) return false;
            value = FromLegacy(low, high);
            return true;
        }
        text = text.PadLeft(HexLength, '0');
        var words = new ulong[4];
        for (int i = 0; i < 4; i++)
            if (!ulong.TryParse(text.Substring(16 * (3 - i), 16), System.Globalization.NumberStyles.HexNumber, null, out words[i]))
                return false;
        value = new WeaponAttachments(words[0], words[1], words[2], words[3]);
        return true;
    }

    // ------------------------------------------------------------------
    // Les bits d'une place
    // ------------------------------------------------------------------

    private ulong Word(int index) => index switch { 0 => w0, 1 => w1, 2 => w2, _ => w3 };

    private uint SlotBits(int slot)
    {
        if (slot < 0 || slot >= Slots) return 0u;
        return (uint)((Word(slot / SlotsPerWord) >> (BitsPerSlot * (slot % SlotsPerWord))) & 0xFFFFUL);
    }

    private WeaponAttachments WithSlotBits(int slot, uint bits)
    {
        if (slot < 0 || slot >= Slots) return this;
        int shift = BitsPerSlot * (slot % SlotsPerWord);
        ulong mask = 0xFFFFUL << shift;
        ulong word = (Word(slot / SlotsPerWord) & ~mask) | ((ulong)(bits & 0xFFFFu) << shift);
        WeaponAttachments copy = this;
        switch (slot / SlotsPerWord)
        {
            case 0: copy.w0 = word; break;
            case 1: copy.w1 = word; break;
            case 2: copy.w2 = word; break;
            default: copy.w3 = word; break;
        }
        return copy;
    }

    private int Get(int slot, int shift, int width) => (int)((SlotBits(slot) >> shift) & ((1u << width) - 1u));

    private WeaponAttachments Set(int slot, int shift, int width, int value)
    {
        uint mask = ((1u << width) - 1u) << shift;
        return WithSlotBits(slot, (SlotBits(slot) & ~mask) | (((uint)value << shift) & mask));
    }

    // ------------------------------------------------------------------
    // Chaque rubrique
    // ------------------------------------------------------------------

    /// <summary>Le viseur de la place `slot` : 0 la mire de l'arme (ou sa lunette d'origine), 1 à 15 un
    /// viseur du catalogue.</summary>
    public int Sight(int slot) => Get(slot, SightShift, SightWidth);

    public GripKind Grip(int slot) => (GripKind)Get(slot, GripShift, GripWidth);

    public bool Laser(int slot) => Get(slot, LaserShift, LaserWidth) != 0;

    public MagazineKind Magazine(int slot) => (MagazineKind)Get(slot, MagazineShift, MagazineWidth);

    public bool ExtendedMagazine(int slot) => Magazine(slot) == MagazineKind.Extended;

    public StockKind Stock(int slot) => (StockKind)Get(slot, StockShift, StockWidth);

    public BarrelKind Barrel(int slot) => (BarrelKind)Get(slot, BarrelShift, BarrelWidth);

    public WeaponAttachments WithSight(int slot, int sight) => Set(slot, SightShift, SightWidth, Mathf.Clamp(sight, 0, 15));
    public WeaponAttachments WithGrip(int slot, GripKind grip) => Set(slot, GripShift, GripWidth, (int)grip);
    public WeaponAttachments WithLaser(int slot, bool laser) => Set(slot, LaserShift, LaserWidth, laser ? 1 : 0);
    public WeaponAttachments WithMagazine(int slot, MagazineKind kind) => Set(slot, MagazineShift, MagazineWidth, (int)kind);
    public WeaponAttachments WithMagazine(int slot, bool extended) => WithMagazine(slot, extended ? MagazineKind.Extended : MagazineKind.Standard);
    public WeaponAttachments WithStock(int slot, StockKind kind) => Set(slot, StockShift, StockWidth, (int)kind);
    public WeaponAttachments WithBarrel(int slot, BarrelKind kind) => Set(slot, BarrelShift, BarrelWidth, (int)kind);

    /// <summary>Les mêmes accessoires, avec ceux-ci pour la place `slot` (bornés à ce qui se code) ; sa crosse et
    /// son canon ne changent pas.</summary>
    public WeaponAttachments With(int slot, int sight, GripKind grip, bool laser, bool extendedMagazine)
        => WithSight(slot, sight).WithGrip(slot, grip).WithLaser(slot, laser).WithMagazine(slot, extendedMagazine);

    /// <summary>
    /// Les accessoires enregistrés avant la refonte (2026-10-16 au 2026-10-26) : 8 bits par classe. Ceux
    /// d'une classe passent à ses trois armes (`armsOfClass[classe]`, leurs places dans l'arsenal) ;
    /// Sanitized fait ensuite tomber ce qu'une arme ne prend pas. Fonction pure, testée.
    /// </summary>
    public static WeaponAttachments FromBeforeOverhaul(uint old, int[][] armsOfClass)
    {
        WeaponAttachments result = None;
        for (int weaponClass = 0; weaponClass < 4 && armsOfClass != null && weaponClass < armsOfClass.Length; weaponClass++)
        {
            uint bits = (old >> (8 * weaponClass)) & 0xFFu;
            if (armsOfClass[weaponClass] == null) continue;
            foreach (int slot in armsOfClass[weaponClass]) result = result.WithSlotBits(slot, bits);
        }
        return result;
    }

    /// <summary>
    /// Ce qu'un serveur accepte d'un client qui ne fait pas foi : ce que l'arme de chaque place ne
    /// prend pas (WeaponData.acceptedAttachments et ce que chaque rubrique en précise : que les lunettes sur
    /// une arme de précision, pas de chargeur rapide sur une arme qui recharge cartouche par cartouche, pas de
    /// canon long sur une arme sans baisse des dégâts...), un choix qui n'existe pas, tombent ; les bits sans
    /// place sont effacés.
    /// </summary>
    public WeaponAttachments Sanitized(IReadOnlyList<WeaponData> loadout)
    {
        WeaponAttachments result = None;
        for (int slot = 0; slot < Slots; slot++)
        {
            WeaponData data = loadout != null && slot < loadout.Count ? loadout[slot] : null;
            foreach (AttachmentCategory category in Categories)
            {
                int variant = Variant(slot, category);
                if (variant != 0 && Accepts(data, category, variant)) result = result.WithVariant(slot, category, variant);
            }
        }
        return result;
    }

    /// <summary>La famille d'un viseur du catalogue (None pour la mire de l'arme ou hors catalogue). Le rang
    /// d'un viseur est enregistré chez les joueurs : un nouveau prend une place à la fin.</summary>
    public static SightFamilies FamilyOf(int sight) => sight switch
    {
        >= 1 and <= 3 => SightFamilies.RedDot,
        >= 4 and <= 7 => SightFamilies.Holographic,
        >= 8 and <= 10 => SightFamilies.Reflex,
        11 => SightFamilies.WideReflex,
        >= 12 and <= SightCount => SightFamilies.Scope,
        _ => SightFamilies.None,
    };

    /// <summary>Le drapeau d'un chargeur dans WeaponData.acceptedMagazines (None pour celui de l'arme).</summary>
    public static MagazineKinds FlagOf(MagazineKind kind)
        => kind == MagazineKind.Standard ? MagazineKinds.None : (MagazineKinds)(1 << ((int)kind - 1));

    /// <summary>Le drapeau d'un accessoire de canon dans WeaponData.acceptedBarrels (None pour celui de l'arme).</summary>
    public static BarrelKinds FlagOf(BarrelKind kind)
        => kind == BarrelKind.Standard ? BarrelKinds.None : (BarrelKinds)(1 << ((int)kind - 1));

    /// <summary>L'arme prend-elle ce choix de cette rubrique ? « Aucun » (0) toujours ; sinon il faut
    /// que l'arme prenne la rubrique (acceptedAttachments), que le choix existe, et pour un viseur, un chargeur
    /// ou un accessoire de canon, qu'elle prenne ce choix-là (acceptedSights, acceptedMagazines,
    /// acceptedBarrels). L'armurerie et le serveur lisent la même règle.</summary>
    public static bool Accepts(WeaponData data, AttachmentCategory category, int variant)
    {
        if (variant == 0) return true;
        if (data == null || variant < 0 || variant >= VariantCount(category)) return false;
        if ((data.acceptedAttachments & SlotOf(category)) == 0) return false;
        return category switch
        {
            AttachmentCategory.Sight => (data.acceptedSights & FamilyOf(variant)) != 0,
            AttachmentCategory.Magazine => (data.acceptedMagazines & FlagOf((MagazineKind)variant)) != 0,
            AttachmentCategory.Barrel => (data.acceptedBarrels & FlagOf((BarrelKind)variant)) != 0,
            _ => true,
        };
    }

    /// <summary>L'arme prend-elle au moins un choix de cette rubrique ? (l'armurerie grise les autres)</summary>
    public static bool AcceptsAny(WeaponData data, AttachmentCategory category)
    {
        for (int variant = 1; variant < VariantCount(category); variant++)
            if (Accepts(data, category, variant)) return true;
        return false;
    }

    // ------------------------------------------------------------------
    // Les rubriques de l'armurerie (AttachmentCategory) : un choix par rubrique, 0 = aucun
    // ------------------------------------------------------------------

    /// <summary>Le nombre de choix d'une rubrique, « aucun » compris.</summary>
    public static int VariantCount(AttachmentCategory category) => category switch
    {
        AttachmentCategory.Sight => SightCount + 1,
        AttachmentCategory.Grip => 3,
        AttachmentCategory.Laser => 2,
        AttachmentCategory.Magazine => 3,
        AttachmentCategory.Stock => 3,
        AttachmentCategory.Barrel => 5,
        _ => 1,
    };

    /// <summary>Ce que l'arme accepte de cette rubrique.</summary>
    public static AttachmentSlots SlotOf(AttachmentCategory category) => category switch
    {
        AttachmentCategory.Sight => AttachmentSlots.Sight,
        AttachmentCategory.Grip => AttachmentSlots.Grip,
        AttachmentCategory.Laser => AttachmentSlots.Laser,
        AttachmentCategory.Magazine => AttachmentSlots.Magazine,
        AttachmentCategory.Stock => AttachmentSlots.Stock,
        AttachmentCategory.Barrel => AttachmentSlots.Barrel,
        _ => AttachmentSlots.None,
    };

    /// <summary>Le choix de la place `slot` dans une rubrique.</summary>
    public int Variant(int slot, AttachmentCategory category) => category switch
    {
        AttachmentCategory.Sight => Sight(slot),
        AttachmentCategory.Grip => (int)Grip(slot),
        AttachmentCategory.Laser => Laser(slot) ? 1 : 0,
        AttachmentCategory.Magazine => (int)Magazine(slot),
        AttachmentCategory.Stock => (int)Stock(slot),
        AttachmentCategory.Barrel => (int)Barrel(slot),
        _ => 0,
    };

    public WeaponAttachments WithVariant(int slot, AttachmentCategory category, int variant)
    {
        variant = Mathf.Clamp(variant, 0, VariantCount(category) - 1);
        return category switch
        {
            AttachmentCategory.Sight => WithSight(slot, variant),
            AttachmentCategory.Grip => WithGrip(slot, (GripKind)variant),
            AttachmentCategory.Laser => WithLaser(slot, variant > 0),
            AttachmentCategory.Magazine => WithMagazine(slot, (MagazineKind)variant),
            AttachmentCategory.Stock => WithStock(slot, (StockKind)variant),
            AttachmentCategory.Barrel => WithBarrel(slot, (BarrelKind)variant),
            _ => this,
        };
    }

    /// <summary>
    /// Le nom d'un choix, tel que l'armurerie et le stand l'écrivent, quand l'accessoire n'a pas de modèle qui
    /// le dise : « Crosse légère », « Canon long » ; sur une arme laser, sa cellule et ses émetteurs
    /// (« Cellule rapide », « Focaliseur long »). Le choix 0 : ce que l'arme a d'origine. Fonction pure, testée.
    /// </summary>
    public static string ChoiceName(AttachmentCategory category, int variant, WeaponData data)
    {
        bool laser = data != null && data.UsesHeat;
        if (variant == 0)
            return category switch
            {
                AttachmentCategory.Sight => AttachmentCatalog.BaseSightTitle(data),
                AttachmentCategory.Grip => "Aucune poignée",
                AttachmentCategory.Laser => "Aucun laser",
                AttachmentCategory.Magazine => laser ? "Cellule d'origine" : "Chargeur d'origine",
                AttachmentCategory.Stock => "Crosse d'origine",
                _ => laser ? "Émetteur d'origine" : "Canon d'origine",
            };
        return category switch
        {
            AttachmentCategory.Sight => $"Viseur {variant}",
            AttachmentCategory.Grip => (GripKind)variant == GripKind.Vertical ? "Poignée verticale" : "Poignée inclinée",
            AttachmentCategory.Laser => "Laser tactique",
            AttachmentCategory.Magazine => (MagazineKind)variant == MagazineKind.Fast
                ? (laser ? "Cellule rapide" : "Chargeur rapide")
                : (laser ? "Cellule grande capacité" : "Chargeur grande capacité"),
            AttachmentCategory.Stock => (StockKind)variant == StockKind.Light ? "Crosse légère" : "Crosse lourde",
            _ => (BarrelKind)variant switch
            {
                BarrelKind.Compensator => laser ? "Stabilisateur" : "Compensateur",
                BarrelKind.MuzzleBrake => laser ? "Dissipateur" : "Frein de bouche",
                BarrelKind.Long => laser ? "Focaliseur long" : "Canon long",
                _ => laser ? "Focaliseur court" : "Canon court",
            },
        };
    }

    /// <summary>Le nom d'une rubrique (« Canon », « Crosse »).</summary>
    public static string CategoryName(AttachmentCategory category) => category switch
    {
        AttachmentCategory.Sight => "Viseur",
        AttachmentCategory.Grip => "Poignée",
        AttachmentCategory.Laser => "Laser",
        AttachmentCategory.Magazine => "Chargeur",
        AttachmentCategory.Stock => "Crosse",
        _ => "Canon",
    };

    public bool Equals(WeaponAttachments other) => w0 == other.w0 && w1 == other.w1 && w2 == other.w2 && w3 == other.w3;
    public override bool Equals(object obj) => obj is WeaponAttachments other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(w0, w1, w2, w3);
    public static bool operator ==(WeaponAttachments a, WeaponAttachments b) => a.Equals(b);
    public static bool operator !=(WeaponAttachments a, WeaponAttachments b) => !a.Equals(b);
    public override string ToString() => ToHex();
}
