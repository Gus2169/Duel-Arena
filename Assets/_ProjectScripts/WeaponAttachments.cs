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

/// <summary>Ce qu'une arme prend à l'armurerie (WeaponData.acceptedAttachments).</summary>
[Flags]
public enum AttachmentSlots : byte
{
    None = 0,
    Sight = 1,
    Grip = 2,
    Laser = 4,
    Magazine = 8,
    All = Sight | Grip | Laser | Magazine,
}

/// <summary>Les familles des viseurs de Rainbow Six (WeaponData.acceptedSights) : les points rouges
/// (viseurs 1 à 3), les holographiques (4 à 7), les réflex (8 à 10).</summary>
[Flags]
public enum SightFamilies : byte
{
    None = 0,
    RedDot = 1,
    Holographic = 2,
    Reflex = 4,
    All = RedDot | Holographic | Reflex,
}

/// <summary>
/// Les accessoires des armes d'un joueur (2026-10-16, GDD § 6) : pour chaque place de la panoplie
/// (touches 1 à 4), le viseur (0 : la mire de l'arme ; 1 à 10 : les dix viseurs de Rainbow Six, dans
/// l'ordre du catalogue), la poignée, le laser tactique et le chargeur grande capacité.
///
/// Contrairement à l'apparence (WeaponLooks), ils CHANGENT l'arme : le chargeur, le recul, le temps
/// d'épaulement (AttachmentCatalog en porte les valeurs). Le joueur les annonce en se connectant
/// (SessionFlow), le serveur les borne (Sanitized : ce que l'arme ne prend pas tombe) et les publie à
/// l'apparition du robot (PlayerWeaponAttachments) ; le joueur et le serveur comptent ensuite le même
/// chargeur. Tous ouverts à tous dès le départ : un choix de configuration, jamais un avantage
/// débloqué (GDD § 14).
///
/// Tient sur 32 bits, 8 par arme : 4 pour le viseur, 2 pour la poignée, 1 pour le laser, 1 pour le
/// chargeur. Fonctions pures, testées.
/// </summary>
[Serializable]
public struct WeaponAttachments : IEquatable<WeaponAttachments>
{
    public const int Slots = 4;
    /// <summary>Les viseurs du catalogue (la mire de l'arme, 0, en plus).</summary>
    public const int SightCount = 10;
    private const int BitsPerSlot = 8;

    [SerializeField] private uint packed;

    public WeaponAttachments(uint packed) => this.packed = packed;

    /// <summary>Aucun accessoire : la mire de l'arme, ni poignée, ni laser, le chargeur d'origine.</summary>
    public static WeaponAttachments None => default;

    public uint Packed => packed;

    private uint Bits(int slot) => slot < 0 || slot >= Slots ? 0u : (packed >> (BitsPerSlot * slot)) & 0xFFu;

    /// <summary>Le viseur de la place `slot` : 0 la mire de l'arme, 1 à 10 un viseur du catalogue.</summary>
    public int Sight(int slot) => (int)(Bits(slot) & 0xFu);

    public GripKind Grip(int slot) => (GripKind)((Bits(slot) >> 4) & 0x3u);

    public bool Laser(int slot) => ((Bits(slot) >> 6) & 1u) != 0;

    public bool ExtendedMagazine(int slot) => ((Bits(slot) >> 7) & 1u) != 0;

    /// <summary>Les mêmes accessoires, avec ceux-ci pour la place `slot` (bornés à ce qui se code).</summary>
    public WeaponAttachments With(int slot, int sight, GripKind grip, bool laser, bool extendedMagazine)
    {
        if (slot < 0 || slot >= Slots) return this;
        uint bits = (uint)Mathf.Clamp(sight, 0, 15) | ((uint)grip & 0x3u) << 4 | (laser ? 1u << 6 : 0u)
                    | (extendedMagazine ? 1u << 7 : 0u);
        int shift = BitsPerSlot * slot;
        return new WeaponAttachments((packed & ~(0xFFu << shift)) | (bits << shift));
    }

    public WeaponAttachments WithSight(int slot, int sight) => With(slot, sight, Grip(slot), Laser(slot), ExtendedMagazine(slot));
    public WeaponAttachments WithGrip(int slot, GripKind grip) => With(slot, Sight(slot), grip, Laser(slot), ExtendedMagazine(slot));
    public WeaponAttachments WithLaser(int slot, bool laser) => With(slot, Sight(slot), Grip(slot), laser, ExtendedMagazine(slot));
    public WeaponAttachments WithMagazine(int slot, bool extended) => With(slot, Sight(slot), Grip(slot), Laser(slot), extended);

    /// <summary>
    /// Ce qu'un serveur accepte d'un client qui ne fait pas foi : ce que l'arme de chaque place ne
    /// prend pas (WeaponData.acceptedAttachments et acceptedSights : pas de viseur sur le sniper, que
    /// les réflex sur un pistolet, pas de poignée sur un pistolet), un viseur ou une poignée qui
    /// n'existe pas, tombent ; les bits sans place sont effacés.
    /// </summary>
    public WeaponAttachments Sanitized(IReadOnlyList<WeaponData> loadout)
    {
        WeaponAttachments result = None;
        for (int slot = 0; slot < Slots; slot++)
        {
            WeaponData data = loadout != null && slot < loadout.Count ? loadout[slot] : null;
            int sight = Accepts(data, AttachmentCategory.Sight, Sight(slot)) ? Sight(slot) : 0;
            GripKind grip = Accepts(data, AttachmentCategory.Grip, (int)Grip(slot)) ? Grip(slot) : GripKind.None;
            bool laser = Laser(slot) && Accepts(data, AttachmentCategory.Laser, 1);
            bool magazine = ExtendedMagazine(slot) && Accepts(data, AttachmentCategory.Magazine, 1);
            result = result.With(slot, sight, grip, laser, magazine);
        }
        return result;
    }

    /// <summary>La famille d'un viseur du catalogue (None pour la mire de l'arme ou hors catalogue).</summary>
    public static SightFamilies FamilyOf(int sight) => sight switch
    {
        >= 1 and <= 3 => SightFamilies.RedDot,
        >= 4 and <= 7 => SightFamilies.Holographic,
        >= 8 and <= SightCount => SightFamilies.Reflex,
        _ => SightFamilies.None,
    };

    /// <summary>L'arme prend-elle ce choix de cette rubrique ? « Aucun » (0) toujours ; sinon il faut
    /// que l'arme prenne la rubrique (acceptedAttachments), que le choix existe, et pour un viseur
    /// qu'elle en prenne la famille (acceptedSights). L'armurerie et le serveur lisent la même règle.</summary>
    public static bool Accepts(WeaponData data, AttachmentCategory category, int variant)
    {
        if (variant == 0) return true;
        if (data == null || variant < 0 || variant >= VariantCount(category)) return false;
        if ((data.acceptedAttachments & SlotOf(category)) == 0) return false;
        return category != AttachmentCategory.Sight || (data.acceptedSights & FamilyOf(variant)) != 0;
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
        _ => 2,
    };

    /// <summary>Ce que l'arme accepte de cette rubrique.</summary>
    public static AttachmentSlots SlotOf(AttachmentCategory category) => category switch
    {
        AttachmentCategory.Sight => AttachmentSlots.Sight,
        AttachmentCategory.Grip => AttachmentSlots.Grip,
        AttachmentCategory.Laser => AttachmentSlots.Laser,
        _ => AttachmentSlots.Magazine,
    };

    /// <summary>Le choix de la place `slot` dans une rubrique.</summary>
    public int Variant(int slot, AttachmentCategory category) => category switch
    {
        AttachmentCategory.Sight => Sight(slot),
        AttachmentCategory.Grip => (int)Grip(slot),
        AttachmentCategory.Laser => Laser(slot) ? 1 : 0,
        _ => ExtendedMagazine(slot) ? 1 : 0,
    };

    public WeaponAttachments WithVariant(int slot, AttachmentCategory category, int variant) => category switch
    {
        AttachmentCategory.Sight => WithSight(slot, Mathf.Clamp(variant, 0, SightCount)),
        AttachmentCategory.Grip => WithGrip(slot, (GripKind)Mathf.Clamp(variant, 0, 2)),
        AttachmentCategory.Laser => WithLaser(slot, variant > 0),
        _ => WithMagazine(slot, variant > 0),
    };

    public bool Equals(WeaponAttachments other) => packed == other.packed;
    public override bool Equals(object obj) => obj is WeaponAttachments other && Equals(other);
    public override int GetHashCode() => (int)packed;
    public static bool operator ==(WeaponAttachments a, WeaponAttachments b) => a.packed == b.packed;
    public static bool operator !=(WeaponAttachments a, WeaponAttachments b) => a.packed != b.packed;
    public override string ToString() => packed.ToString("X8");
}
