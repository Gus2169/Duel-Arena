using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// La finition d'une arme (GDD § 6, adoptée le 2026-10-15) : l'état dans lequel on la voit, selon
/// l'histoire qu'elle raconte. Purement cosmétique, comme la peinture du robot : ni la silhouette, ni
/// le hitbox, ni le son, ni le tir n'en dépendent.
///
/// 🚨 Sérialisée et réseautée par sa valeur entière, dans l'ordre d'exporter_armes.FINITIONS : ajouter
/// les nouvelles à la fin.
/// </summary>
public enum WeaponFinish : byte
{
    Origine = 0,
    Repeinte = 1,
    Rouillee = 2,
    Depareillee = 3,
    Polie = 4,
    Encrassee = 5,
}

/// <summary>
/// L'apparence des armes d'un joueur (2026-10-15, GDD § 6) : pour chaque place de la panoplie
/// (touches 1 à 4), le modèle choisi à l'armurerie (0 celui par défaut de la classe, 1 et 2 les deux
/// autres : voir WeaponData.ModelPrefabFor) et sa finition.
///
/// Catégorie C réseautée : le joueur l'annonce en se connectant (SessionFlow, avec son pseudo), le
/// serveur la borne et la publie (PlayerWeaponLooks), chaque machine l'affiche ; rien de la
/// simulation ne la lit. Tient sur 32 bits, 8 par arme : 4 pour le modèle, 4 pour la finition.
/// </summary>
[Serializable]
public struct WeaponLooks : IEquatable<WeaponLooks>
{
    public const int Slots = 4;
    public const int FinishCount = 6;
    private const int BitsPerSlot = 8;

    [SerializeField] private uint packed;

    public WeaponLooks(uint packed) => this.packed = packed;

    /// <summary>Les quatre armes dans leur modèle par défaut, d'origine.</summary>
    public static WeaponLooks Default => default;

    public uint Packed => packed;

    /// <summary>Le modèle choisi pour la place `slot` (0 : celui par défaut).</summary>
    public int Model(int slot) => slot < 0 || slot >= Slots ? 0 : (int)((packed >> (BitsPerSlot * slot)) & 0xFu);

    /// <summary>La finition choisie pour la place `slot`.</summary>
    public WeaponFinish Finish(int slot) => slot < 0 || slot >= Slots ? WeaponFinish.Origine
        : (WeaponFinish)((packed >> (BitsPerSlot * slot + 4)) & 0xFu);

    /// <summary>La même apparence, avec ce modèle et cette finition pour la place `slot` (bornés).</summary>
    public WeaponLooks With(int slot, int model, WeaponFinish finish)
    {
        if (slot < 0 || slot >= Slots) return this;
        int shift = BitsPerSlot * slot;
        uint value = (uint)Mathf.Clamp(model, 0, 15) | ((uint)Mathf.Clamp((int)finish, 0, 15) << 4);
        return new WeaponLooks((packed & ~(0xFFu << shift)) | (value << shift));
    }

    public WeaponLooks WithModel(int slot, int model) => With(slot, model, Finish(slot));

    public WeaponLooks WithFinish(int slot, WeaponFinish finish) => With(slot, Model(slot), finish);

    /// <summary>
    /// Ce qu'un serveur accepte d'un client qui ne fait pas foi : un modèle que l'arme n'a pas, une
    /// finition qui n'existe pas, redeviennent le modèle par défaut et l'origine ; les bits sans place
    /// sont effacés.
    /// Plus tard, les finitions se débloqueront en jouant (GDD § 14) : c'est ici qu'on les vérifiera.
    /// Fonction pure, testée.
    /// </summary>
    public WeaponLooks Sanitized(IReadOnlyList<WeaponData> loadout)
    {
        var result = Default;
        for (int slot = 0; slot < Slots; slot++)
        {
            WeaponData data = loadout != null && slot < loadout.Count ? loadout[slot] : null;
            int models = data != null ? data.ModelCount : 1;
            int model = Model(slot) < models ? Model(slot) : 0;
            WeaponFinish finish = (int)Finish(slot) < FinishCount ? Finish(slot) : WeaponFinish.Origine;
            result = result.With(slot, model, finish);
        }
        return result;
    }

    /// <summary>Le nom d'une finition, tel que l'armurerie l'affiche.</summary>
    public static string FinishLabel(WeaponFinish finish) => finish switch
    {
        WeaponFinish.Origine => "D'origine",
        WeaponFinish.Repeinte => "Repeinte",
        WeaponFinish.Rouillee => "Rouillée",
        WeaponFinish.Depareillee => "Dépareillée",
        WeaponFinish.Polie => "Polie par les mains",
        WeaponFinish.Encrassee => "Encrassée",
        _ => finish.ToString(),
    };

    public bool Equals(WeaponLooks other) => packed == other.packed;
    public override bool Equals(object obj) => obj is WeaponLooks other && Equals(other);
    public override int GetHashCode() => (int)packed;
    public static bool operator ==(WeaponLooks a, WeaponLooks b) => a.packed == b.packed;
    public static bool operator !=(WeaponLooks a, WeaponLooks b) => a.packed != b.packed;
    public override string ToString() => packed.ToString("X8");
}
