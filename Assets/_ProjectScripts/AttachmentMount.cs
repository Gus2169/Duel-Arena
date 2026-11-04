using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Monte les accessoires d'une arme sur une copie de son modèle (2026-10-16, GDD § 6) : sur le corps
/// et les bras de la vue subjective (HeldWeapons), et dans l'armurerie du menu.
///
/// Chaque accessoire est une copie de son préfab (AttachmentCatalog), posée sans décalage sur le point
/// d'accroche de sa rubrique, enfant du groupe « Accessoires » du modèle (Viseur, Poignee, Laser,
/// Chargeur, Crosse), ou de sa bouche pour ce qui va au canon : les préfabs ont leur origine au point
/// d'accroche, dans les axes de l'arme. Un canon long ou court qui déplace l'avant de l'arme porte les
/// points d'accroche qu'il déplace (son propre groupe « Accessoires » : le laser et la rallonge du tube
/// du FAP-30 suivent sa tête). Ce qu'un accessoire remplace se cache (WeaponAttachment.Hides : la hausse
/// et le guidon sous un viseur, le chargeur d'origine sous un chargeur grande capacité, la crosse d'origine
/// sous une crosse, le bout du canon sous un canon court), sur l'arme comme dans les copies qu'en portent
/// les autres accessoires (le guidon d'un canon long sous un viseur), et revient quand il part. Il prend la
/// finition de l'arme, son layer et ses ombres.
///
/// Les effets ne sont pas ici (AttachmentCatalog, WeaponController) : un accessoire qui n'a pas de
/// modèle pour cette arme (le chargeur du RV-30, un barillet) agit quand même.
///
/// Catégorie C pure. Testé.
/// </summary>
public static class AttachmentMount
{
    // Ce qui est monté sur chaque copie d'arme, par rubrique (AttachmentCategory) : la prise des mains et le laser le relisent à
    // chaque image, et le chercher parcourt les enfants de l'arme par leur nom. Une copie faite par Instantiate (les bras de la
    // vue subjective) n'y est pas encore : elle se cherche à sa première lecture. Seul Apply monte et retire : il oublie tout, ce
    // qui en sort aussi les copies détruites.
    private static readonly Dictionary<Transform, WeaponAttachment[]> knownMounts = new Dictionary<Transform, WeaponAttachment[]>();

    /// <summary>Le groupe des points d'accroche dans le préfab d'un modèle d'arme.</summary>
    public const string SocketGroupName = "Accessoires";

    /// <summary>Le repère de la bouche d'un modèle d'arme (exporter_armes), où se monte ce qui va au canon.</summary>
    public const string MuzzleName = "Bouche";

    /// <summary>Le point d'accroche d'une rubrique (exporter_armes : les enfants de « Accessoires » ; le canon se
    /// monte à la bouche de l'arme).</summary>
    public static string SocketName(AttachmentCategory category) => category switch
    {
        AttachmentCategory.Grip => "Poignee",
        AttachmentCategory.Sight => "Viseur",
        AttachmentCategory.Laser => "Laser",
        AttachmentCategory.Stock => "Crosse",
        AttachmentCategory.Barrel => MuzzleName,
        _ => "Chargeur",
    };

    /// <summary>Le point d'accroche d'une rubrique sur une copie de modèle, ou null s'il n'en a pas : la bouche de
    /// l'arme pour le canon ; celui que porte ce qui est monté au canon s'il le déplace.</summary>
    public static Transform Socket(Transform weapon, AttachmentCategory category)
    {
        if (weapon == null) return null;
        if (category == AttachmentCategory.Barrel) return MuzzleSocket(weapon);
        Transform moved = MovedSocket(weapon, category);
        return moved != null ? moved : OwnSocket(weapon, category);
    }

    private static Transform MuzzleSocket(Transform weapon)
    {
        WeaponModel model = weapon.GetComponent<WeaponModel>();
        return model != null && model.Muzzle != null ? model.Muzzle : weapon.Find(MuzzleName);
    }

    private static Transform OwnSocket(Transform weapon, AttachmentCategory category)
    {
        Transform group = weapon.Find(SocketGroupName);
        return group != null ? group.Find(SocketName(category)) : null;
    }

    /// <summary>Le point d'accroche que porte ce qui est monté au canon (un canon court qui fait reculer la tête
    /// du FAP-30, et son laser avec elle), ou null.</summary>
    private static Transform MovedSocket(Transform weapon, AttachmentCategory category)
    {
        WeaponAttachment barrel = MountedOn(MuzzleSocket(weapon));
        return barrel != null ? barrel.transform.Find(SocketGroupName + "/" + SocketName(category)) : null;
    }

    /// <summary>Le préfab de l'accessoire choisi (`variant`, celui de WeaponAttachments.Variant) dans
    /// une rubrique, pour un modèle d'arme : null pour « aucun », ou quand il n'a pas de modèle pour
    /// cette arme. Le laser d'un pistolet est le laser de poing, sous le canon ; un chargeur, une crosse, un
    /// canon sont faits pour leur modèle d'arme.</summary>
    public static GameObject PrefabFor(AttachmentCatalog catalog, WeaponModel model, AttachmentCategory category, int variant)
    {
        if (catalog == null || variant <= 0) return null;
        string designation = model != null ? model.Designation : null;
        return category switch
        {
            AttachmentCategory.Sight => catalog.SightPrefab(variant),
            AttachmentCategory.Grip => catalog.GripPrefab((GripKind)variant),
            AttachmentCategory.Laser => catalog.LaserPrefab(model != null && model.PistolHold),
            AttachmentCategory.Magazine => catalog.MagazinePrefab(designation, (MagazineKind)variant),
            AttachmentCategory.Stock => catalog.StockPrefab((StockKind)variant, designation),
            AttachmentCategory.Barrel => catalog.BarrelPrefab((BarrelKind)variant, designation),
            _ => null,
        };
    }

    /// <summary>
    /// La bouche d'où part le tir qu'on voit (la flamme, l'éclat du tir chargé) : celle de ce qui est monté au
    /// canon s'il en a une (un compensateur, un canon long ou court la déplacent), sinon celle du modèle.
    /// </summary>
    public static Transform MuzzleOf(WeaponModel model)
    {
        if (model == null) return null;
        WeaponAttachment barrel = Mounted(model.transform, AttachmentCategory.Barrel);
        return barrel != null && barrel.Muzzle != null ? barrel.Muzzle : model.Muzzle;
    }

    /// <summary>
    /// Monte sur `weapon` (une copie du modèle de `data`) les accessoires de la place `slot`, dans la
    /// finition `finish` : crée ce qui manque, remplace ce qui n'est plus le bon, retire ce qui n'est
    /// plus choisi, et cache ou remontre les pièces qu'ils remplacent. Ce que l'arme ne prend pas
    /// (WeaponAttachments.Accepts) n'est pas monté. `renderLike` donne les ombres des nouvelles pièces
    /// (sans lui, celles de l'arme).
    /// </summary>
    public static void Apply(Transform weapon, WeaponData data, WeaponAttachments attachments, int slot, WeaponFinish finish,
                             Renderer renderLike = null)
    {
        if (weapon == null) return;
        WeaponModel model = weapon.GetComponent<WeaponModel>();
        AttachmentCatalog catalog = data != null ? data.attachmentCatalog : null;
        var mountedAll = new List<WeaponAttachment>();
        var touched = new HashSet<string>();

        foreach (AttachmentCategory category in MountOrder)
        {
            Transform socket = Socket(weapon, category);
            // Un point d'accroche déplacé par le canon (ou rendu à l'arme) : ce qui était monté à l'autre place part.
            Transform own = category == AttachmentCategory.Barrel ? null : OwnSocket(weapon, category);
            WeaponAttachment stale = own != null && own != socket ? MountedOn(own) : null;
            if (stale != null)
            {
                touched.UnionWith(stale.Hides);
                Remove(stale.gameObject);
            }
            if (socket == null) continue;
            int variant = attachments.Variant(slot, category);
            GameObject prefab = WeaponAttachments.Accepts(data, category, variant) ? PrefabFor(catalog, model, category, variant) : null;

            WeaponAttachment mounted = MountedOn(socket);
            if (mounted != null) touched.UnionWith(mounted.Hides);
            if (mounted != null && (prefab == null || !SameAttachment(mounted, prefab)))
            {
                Remove(mounted.gameObject);
                mounted = null;
            }
            if (mounted == null && prefab != null) mounted = Mount(prefab, socket, weapon, renderLike, model != null && model.PistolHold);
            if (mounted == null) continue;

            if (mounted.TryGetComponent(out WeaponFinishes finishes) && finishes.Shown != finish) finishes.Apply(finish);
            mountedAll.Add(mounted);
            touched.UnionWith(mounted.Hides);
        }

        // Ce qu'un accessoire remplace se cache sur l'arme, et dans les autres accessoires qui en portent une
        // copie (le guidon d'un canon long sous un viseur) ; jamais dans le sien, qui le montre à sa place.
        foreach (string part in touched)
        {
            SetShown(weapon.Find(part), !HiddenByAnother(mountedAll, part, null));
            foreach (WeaponAttachment owner in mountedAll)
                SetShown(owner.transform.Find(part), !HiddenByAnother(mountedAll, part, owner));
        }
        knownMounts.Clear();
    }

    // Ce qui va au canon d'abord : il peut déplacer les points d'accroche des autres (Socket).
    private static readonly AttachmentCategory[] MountOrder =
    {
        AttachmentCategory.Barrel, AttachmentCategory.Sight, AttachmentCategory.Grip, AttachmentCategory.Laser,
        AttachmentCategory.Magazine, AttachmentCategory.Stock,
    };

    private static bool HiddenByAnother(List<WeaponAttachment> mounted, string part, WeaponAttachment owner)
    {
        foreach (WeaponAttachment a in mounted)
        {
            if (a == owner) continue;
            foreach (string hidden in a.Hides)
                if (hidden == part) return true;
        }
        return false;
    }

    private static void SetShown(Transform piece, bool visible)
    {
        if (piece != null && piece.gameObject.activeSelf != visible) piece.gameObject.SetActive(visible);
    }

    /// <summary>L'accessoire monté dans une rubrique, ou null.</summary>
    public static WeaponAttachment Mounted(Transform weapon, AttachmentCategory category)
    {
        if (weapon == null) return null;
        if (!knownMounts.TryGetValue(weapon, out WeaponAttachment[] mounts))
        {
            mounts = new WeaponAttachment[MountOrder.Length];
            foreach (AttachmentCategory each in MountOrder) mounts[(int)each] = MountedOn(Socket(weapon, each));
            knownMounts.Add(weapon, mounts);
        }
        return mounts[(int)category];
    }

    /// <summary>
    /// La mire à aligner sur l'œil en visée et sa distance à l'œil : celles du viseur monté s'il y en a
    /// un, sinon celles du modèle (sa hausse). Un pistolet garde sa distance, tenu à bout de bras.
    /// </summary>
    public static Transform SightOf(WeaponModel model, out float aimEyeDistance)
    {
        aimEyeDistance = model != null ? model.AimEyeDistance : 0.1f;
        if (model == null) return null;
        WeaponAttachment sight = Mounted(model.transform, AttachmentCategory.Sight);
        if (sight == null || sight.Sight == null) return model.Sight;
        if (!model.PistolHold) aimEyeDistance = sight.AimEyeDistance;
        return sight.Sight;
    }

    private static WeaponAttachment MountedOn(Transform socket)
    {
        if (socket == null) return null;
        for (int i = 0; i < socket.childCount; i++)
        {
            Transform child = socket.GetChild(i);
            if (child.gameObject.activeSelf && child.TryGetComponent(out WeaponAttachment a)) return a;
        }
        return null;
    }

    private static bool SameAttachment(WeaponAttachment mounted, GameObject prefab)
        => prefab.TryGetComponent(out WeaponAttachment wanted) && wanted.Code == mounted.Code;

    private static WeaponAttachment Mount(GameObject prefab, Transform socket, Transform weapon, Renderer renderLike, bool pistol)
    {
        GameObject copy = Object.Instantiate(prefab, socket, false);
        copy.name = prefab.name;
        Transform t = copy.transform;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        // Layer et ombres : ceux de l'arme qui le porte (le layer des bras sur la vue subjective,
        // l'ombre seule sur le corps du propriétaire).
        int layer = weapon.gameObject.layer;
        foreach (Transform child in copy.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
        Renderer like = renderLike != null ? renderLike : FirstPiece(weapon, copy.transform);
        var attachment = copy.GetComponent<WeaponAttachment>();
        if (like != null)
        {
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            {
                if (attachment != null && r == attachment.Lens) continue;
                r.shadowCastingMode = like.shadowCastingMode;
                r.receiveShadows = like.receiveShadows;
            }
        }
        if (attachment != null && attachment.Lens != null)
        {
            SetUpLens(attachment.Lens, like);
            // Sur un pistolet, un petit réflex ne montre que le centre de son réticule (WeaponAttachment.PistolLens).
            if (pistol && attachment.PistolLens != null) attachment.Lens.sharedMaterial = attachment.PistolLens;
        }
        return attachment;
    }

    /// <summary>
    /// La vitre d'un viseur n'a jamais d'ombre : elle ne cache rien. Et sur une arme qui n'est qu'une
    /// ombre (le corps du joueur, vu de ses propres yeux), elle ne se dessine pas du tout : elle restait
    /// la seule pièce visible de l'arme de son corps, une tache grise qui flottait devant lui (vu par
    /// l'utilisateur le 2026-10-16). Sans état, testée.
    /// </summary>
    public static void SetUpLens(Renderer lens, Renderer weaponPiece)
    {
        lens.shadowCastingMode = ShadowCastingMode.Off;
        lens.receiveShadows = false;
        lens.forceRenderingOff = weaponPiece != null && weaponPiece.shadowCastingMode == ShadowCastingMode.ShadowsOnly;
    }

    /// <summary>Une pièce de l'arme elle-même (pas d'un accessoire) : celle dont on prend les ombres.</summary>
    private static Renderer FirstPiece(Transform weapon, Transform except)
    {
        foreach (Renderer r in weapon.GetComponentsInChildren<Renderer>(true))
            if (!r.transform.IsChildOf(except) && r.GetComponentInParent<WeaponAttachment>(true) == null) return r;
        return null;
    }

    private static void Remove(GameObject mounted)
    {
        // Destroy est différé : renommé et éteint, l'ancien ne se fait plus trouver d'ici là.
        mounted.name += " (retiré)";
        mounted.SetActive(false);
        if (Application.isPlaying) Object.Destroy(mounted);
        else Object.DestroyImmediate(mounted);
    }
}
