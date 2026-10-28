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
/// L'apparence des armes d'un joueur (2026-10-15, GDD § 6) : la finition choisie à l'armurerie pour
/// chaque arme de l'arsenal. Depuis la refonte du 2026-10-26, chaque modèle est une arme : il n'y a
/// plus de modèle à choisir, et chaque arme garde sa finition.
///
/// Catégorie C réseautée : le joueur l'annonce en se connectant (SessionFlow, avec son pseudo), le
/// serveur la borne et la publie (PlayerWeaponLooks), chaque machine l'affiche ; rien de la
/// simulation ne la lit. Tient sur 64 bits, 4 par arme.
/// </summary>
[Serializable]
public struct WeaponLooks : IEquatable<WeaponLooks>
{
    public const int Slots = WeaponController.MaxSlots;
    public const int FinishCount = 6;
    private const int BitsPerSlot = 4;

    [SerializeField] private ulong packed;

    public WeaponLooks(ulong packed) => this.packed = packed;

    /// <summary>Toutes les armes d'origine.</summary>
    public static WeaponLooks Default => default;

    public ulong Packed => packed;

    /// <summary>La finition choisie pour l'arme de la place `slot`.</summary>
    public WeaponFinish Finish(int slot) => slot < 0 || slot >= Slots ? WeaponFinish.Origine
        : (WeaponFinish)((packed >> (BitsPerSlot * slot)) & 0xFUL);

    /// <summary>La même apparence, avec cette finition pour la place `slot` (bornée).</summary>
    public WeaponLooks WithFinish(int slot, WeaponFinish finish)
    {
        if (slot < 0 || slot >= Slots) return this;
        int shift = BitsPerSlot * slot;
        ulong value = (ulong)Mathf.Clamp((int)finish, 0, 15);
        return new WeaponLooks((packed & ~(0xFUL << shift)) | (value << shift));
    }

    /// <summary>
    /// Ce qu'un serveur accepte d'un client qui ne fait pas foi : une finition qui n'existe pas
    /// redevient l'origine ; les bits sans place sont effacés.
    /// Plus tard, les finitions se débloqueront en jouant (GDD § 14) : c'est ici qu'on les vérifiera.
    /// Fonction pure, testée.
    /// </summary>
    public WeaponLooks Sanitized(IReadOnlyList<WeaponData> loadout)
    {
        var result = Default;
        for (int slot = 0; slot < Slots; slot++)
        {
            WeaponFinish finish = (int)Finish(slot) < FinishCount ? Finish(slot) : WeaponFinish.Origine;
            result = result.WithFinish(slot, finish);
        }
        return result;
    }

    /// <summary>
    /// L'apparence enregistrée avant la refonte (2026-10-15 au 2026-10-26) : 8 bits par classe, le
    /// modèle puis la finition. La finition de chaque classe passe à l'arme du modèle qu'elle montrait
    /// (`armsOfModels[classe][modèle]`, la place de cette arme dans l'arsenal) ; les autres armes sont
    /// d'origine. Fonction pure, testée.
    /// </summary>
    public static WeaponLooks FromBeforeOverhaul(uint old, int[][] armsOfModels)
    {
        var result = Default;
        for (int weaponClass = 0; weaponClass < 4; weaponClass++)
        {
            uint bits = (old >> (8 * weaponClass)) & 0xFFu;
            int model = (int)(bits & 0xFu);
            var finish = (WeaponFinish)((bits >> 4) & 0xFu);
            if ((int)finish >= FinishCount || armsOfModels == null || weaponClass >= armsOfModels.Length) continue;
            int[] arms = armsOfModels[weaponClass];
            if (arms == null || model >= arms.Length) continue;
            result = result.WithFinish(arms[model], finish);
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

    /// <summary>L'abrégé d'une finition, dans la case de sa tuile à l'armurerie.</summary>
    public static string FinishCode(WeaponFinish finish) => finish switch
    {
        WeaponFinish.Origine => "ORI",
        WeaponFinish.Repeinte => "REP",
        WeaponFinish.Rouillee => "ROU",
        WeaponFinish.Depareillee => "DÉP",
        WeaponFinish.Polie => "POL",
        WeaponFinish.Encrassee => "ENC",
        _ => "?",
    };

    /// <summary>Ce que l'armurerie dit d'une finition (Bible § 12).</summary>
    public static string FinishDescription(WeaponFinish finish) => finish switch
    {
        WeaponFinish.Origine => "L'usure de tous les jours.",
        WeaponFinish.Repeinte => "Repeinte au pinceau, couche après couche, sans décaper.",
        WeaponFinish.Rouillee => "Sortie de ses caisses sans être décapée : cloques, creux et coulures.",
        WeaponFinish.Depareillee => "Des pièces prises sur d'autres armes.",
        WeaponFinish.Polie => "La peinture partie là où on la tient, le métal poli.",
        WeaponFinish.Encrassee => "Jamais nettoyée : huile, poussière et suie.",
        _ => "",
    };

    public bool Equals(WeaponLooks other) => packed == other.packed;
    public override bool Equals(object obj) => obj is WeaponLooks other && Equals(other);
    public override int GetHashCode() => packed.GetHashCode();
    public static bool operator ==(WeaponLooks a, WeaponLooks b) => a.packed == b.packed;
    public static bool operator !=(WeaponLooks a, WeaponLooks b) => a.packed != b.packed;
    public override string ToString() => packed.ToString("X12");
}
