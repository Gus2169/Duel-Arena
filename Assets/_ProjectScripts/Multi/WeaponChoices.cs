using System;
using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// L'arsenal commun (GDD § 11, I1 ; 2026-11-03), d'après le Gunfight de Modern Warfare (2019) : les deux duellistes
/// reçoivent la même arme, avec les mêmes accessoires, tirée au sort, qui change toutes les deux manches, sans
/// préparation. Fonctions pures, testées ; RoundManager les applique (BeginRound).
/// </summary>
public static class CommonArsenal
{
    /// <summary>Aucune arme tirée.</summary>
    public const byte NoSlot = byte.MaxValue;

    /// <summary>Ce que le serveur publie : l'arme tirée (sa place dans l'arsenal), ses accessoires (ceux de cette place
    /// seulement comptent), et la manche du tirage.</summary>
    public struct State : INetworkSerializeByMemcpy, IEquatable<State>
    {
        public byte slot;
        public byte drawnRound;
        public WeaponAttachments attachments;

        public bool Equals(State other) => slot == other.slot && drawnRound == other.drawnRound && attachments == other.attachments;
    }

    /// <summary>Une nouvelle arme se tire au début des manches 1, 3, 5… : chacune sert deux manches.</summary>
    public static bool DrawsAt(int round) => round % 2 == 1;

    /// <summary>L'arme tirée au sort parmi celles de l'arsenal, jamais deux fois de suite la même (s'il y en a d'autres).</summary>
    public static int DrawSlot(IReadOnlyList<WeaponData> loadout, int previous, Random random)
    {
        var slots = new List<int>();
        if (loadout != null)
        {
            for (int slot = 0; slot < loadout.Count; slot++)
            {
                if (loadout[slot] != null && slot != previous) slots.Add(slot);
            }
        }
        if (slots.Count == 0) return previous;
        return slots[random.Next(slots.Count)];
    }

    /// <summary>Les accessoires de l'arme tirée, au sort dans chaque rubrique parmi ce qu'elle prend (rien compris) ;
    /// seuls ceux de sa place sont posés.</summary>
    public static WeaponAttachments DrawAttachments(WeaponData data, int slot, Random random)
    {
        WeaponAttachments drawn = WeaponAttachments.None;
        if (data == null) return drawn;
        var variants = new List<int>();
        foreach (AttachmentCategory category in WeaponAttachments.Categories)
        {
            variants.Clear();
            for (int variant = 0; variant < WeaponAttachments.VariantCount(category); variant++)
            {
                if (variant == 0 || WeaponAttachments.Accepts(data, category, variant)) variants.Add(variant);
            }
            drawn = drawn.WithVariant(slot, category, variants[random.Next(variants.Count)]);
        }
        return drawn;
    }

    /// <summary>Les accessoires d'un joueur, ceux de la place `slot` remplacés par ceux du tirage : les autres armes
    /// gardent les siens.</summary>
    public static WeaponAttachments WithSlotFrom(WeaponAttachments mine, WeaponAttachments drawn, int slot)
    {
        foreach (AttachmentCategory category in WeaponAttachments.Categories)
            mine = mine.WithVariant(slot, category, drawn.Variant(slot, category));
        return mine;
    }
}

/// <summary>
/// Pile ou face (GDD § 6 et § 11, P1 à P4 ; 2026-11-03) : au début de chaque préparation, un joueur choisit une famille
/// d'armes (fusil d'assaut, précision, pompe, pistolet) ou le gant seul, et l'impose aux deux ; chacun choisit ensuite son
/// arme dedans. À la première manche, une pièce lancée à l'écran par l'IA désigne celui qui choisit ; ensuite, le perdant
/// de la manche. 10 s pour la famille, le reste de la préparation pour l'arme ; jamais deux fois de suite la même famille.
/// Sans choix au bout des 10 s, une famille tirée au sort (sans le gant). Fonctions pures, testées ; RoundManager les
/// applique.
/// </summary>
public static class CoinFlip
{
    /// <summary>Pas encore de famille ; le gant seul, le cinquième choix (P4).</summary>
    public const byte NoFamily = byte.MaxValue;
    public const byte GloveFamily = Arsenal.FamilyCount;

    /// <summary>Le temps du choix de la famille, au début de la préparation (P2).</summary>
    public const float FamilyChoiceSeconds = 10f;

    /// <summary>Le temps que la pièce tourne à l'écran avant de tomber (P1).</summary>
    public const float TossSeconds = 1.6f;

    /// <summary>Ce que le serveur publie : qui choisit, la famille choisie (ou pas encore), celle de la manche d'avant
    /// (interdite), l'instant où la pièce a été lancée (la première manche, 0 sinon) et la fin des 10 s.</summary>
    public struct State : INetworkSerializeByMemcpy, IEquatable<State>
    {
        public ulong chooser;
        public byte family;
        public byte previous;
        public double tossedAt;
        public double familyDeadline;

        public bool Pending => family == NoFamily;

        public bool Equals(State other) => chooser == other.chooser && family == other.family && previous == other.previous
                                           && tossedAt == other.tossedAt && familyDeadline == other.familyDeadline;
    }

    /// <summary>La fin du choix de la famille : 10 s, comptées quand la pièce est tombée si elle a été lancée.</summary>
    public static double FamilyDeadline(double now, bool tossed) => now + (tossed ? TossSeconds : 0f) + FamilyChoiceSeconds;

    /// <summary>Une famille (ou le gant) peut-elle être choisie ? Une valeur connue, et pas celle de la manche d'avant (P3).</summary>
    public static bool Allowed(byte family, byte previous) => family <= GloveFamily && family != previous;

    /// <summary>La famille tirée au sort quand personne n'a choisi : une des quatre, pas celle d'avant, jamais le gant.</summary>
    public static byte RandomFamily(byte previous, Random random)
    {
        var families = new List<byte>();
        for (byte family = 0; family < Arsenal.FamilyCount; family++)
        {
            if (family != previous) families.Add(family);
        }
        return families[random.Next(families.Count)];
    }

    /// <summary>Qui choisit la famille de la manche : à la première, ou après une manche sans vainqueur, la pièce
    /// (`tossed`) ; sinon, le perdant de la manche d'avant (`lastWinner` étant l'un des deux).</summary>
    public static ulong Chooser(ulong a, ulong b, int round, ulong lastWinner, Random random, out bool tossed)
    {
        tossed = round <= 1 || (lastWinner != a && lastWinner != b);
        if (tossed) return random.Next(2) == 0 ? a : b;
        return lastWinner == a ? b : a;
    }

    /// <summary>Une arme se choisit-elle dans la famille de la manche ? Pas avant que la famille soit choisie, ni quand
    /// c'est le gant seul.</summary>
    public static bool SlotAllowed(WeaponData data, byte family)
        => data != null && family < Arsenal.FamilyCount && (byte)data.family == family;

    /// <summary>Le nom d'un choix : la famille, ou « Le gant seul ».</summary>
    public static string FamilyLabel(byte family)
        => family == GloveFamily ? "Le gant seul" : family < Arsenal.FamilyCount ? Arsenal.FamilyLabel((WeaponFamily)family) : "";
}
