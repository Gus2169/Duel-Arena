using UnityEngine;

/// <summary>
/// Le catalogue des accessoires des armes (2026-10-16, GDD § 6) : leurs préfabs, construits dans
/// Blender (Tools/Blender : viseurs.py, laser_tactique.py, poignees.py, chargeurs.py) et importés par
/// Duel Arena > Armes > Importer les armes de Blender, et les valeurs de leurs effets.
///
/// Une seule instance, Assets/_ProjectArena/Weapon/Accessoires/Accessoires.asset, que portent
/// WeaponController (les effets), le préfab joueur (l'arme tenue) et l'armurerie du menu.
/// Les effets se lisent ici, pas dans l'accessoire : un seul endroit à régler en jouant.
/// </summary>
[CreateAssetMenu(fileName = "Accessoires", menuName = "Duel Arena/Catalogue des accessoires")]
public class AttachmentCatalog : ScriptableObject
{
    [Tooltip("Les dix viseurs sans grossissement de Rainbow Six, dans l'ordre de WeaponAttachments (1 à 10) : points rouges A à C, holographiques A à D, réflex A à C.")]
    public GameObject[] sights = new GameObject[0];

    [Tooltip("Le laser tactique des armes d'épaule (LT-1), sur le rail du flanc droit.")]
    public GameObject laser;

    [Tooltip("Le laser des pistolets (LT-P), sous le canon.")]
    public GameObject pistolLaser;

    public GameObject verticalGrip;
    public GameObject angledGrip;

    [Tooltip("Les chargeurs grande capacité, un par modèle d'arme (WeaponAttachment.WeaponDesignation). Le RV-30 n'en a pas : un barillet ne s'allonge pas, l'effet vaut quand même.")]
    public GameObject[] magazines = new GameObject[0];

    [Header("Effets (valeurs provisoires, à juger en jouant)")]
    [Tooltip("Le recul vertical avec la poignée verticale (0,8 : 20 % de moins).")]
    [Range(0.2f, 1f)] public float verticalGripRecoil = 0.8f;

    [Tooltip("Le recul à la hanche avec le laser tactique : il aide le tir au juger.")]
    [Range(0.2f, 1f)] public float laserHipRecoil = 0.7f;

    [Tooltip("Le temps d'épaulement avec la poignée inclinée (0,8 : on vise 20 % plus vite).")]
    [Range(0.2f, 1f)] public float angledGripAimTime = 0.8f;

    [Tooltip("La taille du chargeur grande capacité (1,5 : la moitié de balles en plus).")]
    [Range(1f, 3f)] public float extendedMagazineCapacity = 1.5f;

    [Tooltip("Le rechargement avec le chargeur grande capacité (1,15 : 15 % plus long ; proposition de Claude). Un pompe, qui charge cartouche par cartouche, a seulement plus de cartouches à glisser.")]
    [Range(1f, 2f)] public float extendedMagazineReload = 1.15f;

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

    /// <summary>Le chargeur grande capacité d'un modèle d'arme (sa désignation), ou null.</summary>
    public GameObject MagazinePrefab(string designation)
    {
        if (magazines == null || string.IsNullOrEmpty(designation)) return null;
        foreach (GameObject prefab in magazines)
            if (prefab != null && prefab.TryGetComponent(out WeaponAttachment a) && a.WeaponDesignation == designation)
                return prefab;
        return null;
    }

    /// <summary>Le nom d'un viseur (« Point rouge A »), ou celui de la mire de l'arme.</summary>
    public string SightTitle(int sight)
    {
        GameObject prefab = SightPrefab(sight);
        return prefab != null && prefab.TryGetComponent(out WeaponAttachment a) ? a.Title : sight == 0 ? "Mire de l'arme" : $"Viseur {sight}";
    }

    // ------------------------------------------------------------------
    // Les effets, lus par le joueur et par le serveur. Fonctions pures, testées.
    // ------------------------------------------------------------------

    /// <summary>La taille du chargeur d'une arme, avec ou sans le chargeur grande capacité.</summary>
    public static int MagazineSize(WeaponData data, bool extended, AttachmentCatalog catalog)
    {
        if (data == null) return 0;
        if (!extended || catalog == null) return data.magazineSize;
        return Mathf.Clamp(Mathf.RoundToInt(data.magazineSize * catalog.extendedMagazineCapacity), 1, 255);
    }

    /// <summary>La durée d'un rechargement (d'une cartouche, pour une arme qui recharge cartouche par
    /// cartouche : elle ne change pas, le chargeur a seulement plus de cartouches à glisser).</summary>
    public static float ReloadDuration(WeaponData data, bool extended, AttachmentCatalog catalog)
    {
        if (data == null) return 0f;
        if (!extended || catalog == null || data.reloadPerRound) return data.reloadDuration;
        return data.reloadDuration * catalog.extendedMagazineReload;
    }

    /// <summary>Ce que les accessoires de la place `slot` font au recul : la poignée verticale retient
    /// le recul vertical ; le laser, à la hanche, retient tout le recul.</summary>
    public static void RecoilFactors(AttachmentCatalog catalog, WeaponAttachments attachments, int slot, bool aiming,
                                     out float vertical, out float horizontal)
    {
        vertical = horizontal = 1f;
        if (catalog == null) return;
        if (attachments.Grip(slot) == GripKind.Vertical) vertical *= catalog.verticalGripRecoil;
        if (attachments.Laser(slot) && !aiming)
        {
            vertical *= catalog.laserHipRecoil;
            horizontal *= catalog.laserHipRecoil;
        }
    }

    /// <summary>Le temps d'épaulement d'une arme, avec ou sans la poignée inclinée.</summary>
    public static float AimTime(WeaponData data, GripKind grip, AttachmentCatalog catalog)
    {
        if (data == null) return 0f;
        return grip == GripKind.Angled && catalog != null ? data.aimTime * catalog.angledGripAimTime : data.aimTime;
    }
}
