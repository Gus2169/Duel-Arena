using UnityEngine;

/// <summary>
/// Le matériel que les spectateurs se disputent sur la tribune et lancent dans l'arène (GDD § 9, « dons
/// de matériel »). Data-driven, comme WeaponData : ajouter un power-up, c'est ajouter une entrée ici.
///
/// Tranché par l'utilisateur le 2026-10-13 : un power-up est une ARME, que le duelliste qui la ramasse
/// prend en main avec un chargeur plein, même en pleine manche (exception explicite à « on ne change
/// plus d'arme pendant la manche », GDD § 6) ; « surtout une meilleure arme potentiellement ». Pour
/// l'instant ce sont les quatre armes de la panoplie. Une arme réservée aux power-ups (hors panoplie)
/// demandera une place de plus dans le chargeur réseauté (WeaponController.MagazineState).
///
/// L'index d'une entrée voyage par le réseau (ArenaItem, SpectatorController) : on ajoute à la fin.
/// </summary>
[CreateAssetMenu(fileName = "PowerUpCatalog", menuName = "Duel Arena/Power-up Catalog")]
public class PowerUpCatalog : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("Ce qu'affichent le HUD et les invites (« F : prendre le Sniper »).")]
        public string displayName = "Arme";
        [Tooltip("L'arme que le duelliste prend en main. Elle doit faire partie de sa panoplie (WeaponController.loadout).")]
        public WeaponData weapon;
        [Tooltip("Couleur de la balise lumineuse de l'objet posé au sol : on le voit de loin.")]
        public Color beaconColor = new Color(1f, 0.75f, 0.2f);
        [Tooltip("Poids dans le tirage du matériel qui tombe sur la tribune (0 = jamais).")]
        [Min(0f)] public float dropWeight = 1f;
    }

    [SerializeField] private Entry[] entries = System.Array.Empty<Entry>();

    public int Count => entries == null ? 0 : entries.Length;

    /// <summary>L'entrée `variant`, ou null hors du catalogue.</summary>
    public Entry Get(int variant) => entries != null && variant >= 0 && variant < entries.Length ? entries[variant] : null;

    public string NameOf(int variant) => Get(variant)?.displayName ?? "objet";

    /// <summary>Tire une entrée selon les poids, avec `roll` dans [0, 1[ (le serveur seul tire : GDD §
    /// 9, « tombe au hasard »). -1 si rien ne peut tomber. Fonction pure, testée.</summary>
    public int PickWeighted(float roll)
    {
        if (entries == null) return -1;
        float total = 0f;
        foreach (Entry e in entries) total += e != null ? Mathf.Max(0f, e.dropWeight) : 0f;
        if (total <= 0f) return -1;
        float target = Mathf.Clamp01(roll) * total;
        int last = -1;
        for (int i = 0; i < entries.Length; i++)
        {
            float w = entries[i] != null ? Mathf.Max(0f, entries[i].dropWeight) : 0f;
            if (w <= 0f) continue;
            last = i;
            if (target < w) return i;
            target -= w;
        }
        return last;
    }
}
