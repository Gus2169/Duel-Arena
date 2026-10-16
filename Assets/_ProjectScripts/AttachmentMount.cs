using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Monte les accessoires d'une arme sur une copie de son modèle (2026-10-16, GDD § 6) : sur le corps
/// et les bras de la vue subjective (HeldWeapons), et dans l'armurerie du menu.
///
/// Chaque accessoire est une copie de son préfab (AttachmentCatalog), posée sans décalage sur le point
/// d'accroche de sa rubrique, enfant du groupe « Accessoires » du modèle (Viseur, Poignee, Laser,
/// Chargeur) : les préfabs ont leur origine au point d'accroche, dans les axes de l'arme. Ce qu'il
/// remplace se cache (WeaponAttachment.Hides : la hausse et le guidon sous un viseur, le chargeur
/// d'origine sous un chargeur grande capacité) et revient quand il part. Il prend la finition de
/// l'arme, son layer et ses ombres.
///
/// Les effets ne sont pas ici (AttachmentCatalog, WeaponController) : un accessoire qui n'a pas de
/// modèle pour cette arme (le chargeur du RV-30, un barillet) agit quand même.
///
/// Catégorie C pure. Sans état, testé.
/// </summary>
public static class AttachmentMount
{
    /// <summary>Le groupe des points d'accroche dans le préfab d'un modèle d'arme.</summary>
    public const string SocketGroupName = "Accessoires";

    private static readonly AttachmentCategory[] Categories =
        { AttachmentCategory.Sight, AttachmentCategory.Grip, AttachmentCategory.Laser, AttachmentCategory.Magazine };

    /// <summary>Le point d'accroche d'une rubrique (exporter_armes : les enfants de « Accessoires »).</summary>
    public static string SocketName(AttachmentCategory category) => category switch
    {
        AttachmentCategory.Grip => "Poignee",
        AttachmentCategory.Sight => "Viseur",
        AttachmentCategory.Laser => "Laser",
        _ => "Chargeur",
    };

    /// <summary>Le point d'accroche d'une rubrique sur une copie de modèle, ou null s'il n'en a pas.</summary>
    public static Transform Socket(Transform weapon, AttachmentCategory category)
    {
        Transform group = weapon != null ? weapon.Find(SocketGroupName) : null;
        return group != null ? group.Find(SocketName(category)) : null;
    }

    /// <summary>Le préfab de l'accessoire choisi (`variant`, celui de WeaponAttachments.Variant) dans
    /// une rubrique, pour un modèle d'arme : null pour « aucun », ou quand il n'a pas de modèle pour
    /// cette arme. Le laser d'un pistolet est le laser de poing, sous le canon.</summary>
    public static GameObject PrefabFor(AttachmentCatalog catalog, WeaponModel model, AttachmentCategory category, int variant)
    {
        if (catalog == null || variant <= 0) return null;
        return category switch
        {
            AttachmentCategory.Sight => catalog.SightPrefab(variant),
            AttachmentCategory.Grip => catalog.GripPrefab((GripKind)variant),
            AttachmentCategory.Laser => catalog.LaserPrefab(model != null && model.PistolHold),
            _ => catalog.MagazinePrefab(model != null ? model.Designation : null),
        };
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
        var hidden = new HashSet<string>();
        var touched = new HashSet<string>();

        foreach (AttachmentCategory category in Categories)
        {
            Transform socket = Socket(weapon, category);
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
            if (mounted == null && prefab != null) mounted = Mount(prefab, socket, weapon, renderLike);
            if (mounted == null) continue;

            if (mounted.TryGetComponent(out WeaponFinishes finishes) && finishes.Shown != finish) finishes.Apply(finish);
            hidden.UnionWith(mounted.Hides);
        }

        touched.UnionWith(hidden);
        foreach (string part in touched)
        {
            Transform piece = weapon.Find(part);
            bool visible = !hidden.Contains(part);
            if (piece != null && piece.gameObject.activeSelf != visible) piece.gameObject.SetActive(visible);
        }
    }

    /// <summary>L'accessoire monté dans une rubrique, ou null.</summary>
    public static WeaponAttachment Mounted(Transform weapon, AttachmentCategory category) => MountedOn(Socket(weapon, category));

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

    private static WeaponAttachment Mount(GameObject prefab, Transform socket, Transform weapon, Renderer renderLike)
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
        if (attachment != null && attachment.Lens != null) SetUpLens(attachment.Lens, like);
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
