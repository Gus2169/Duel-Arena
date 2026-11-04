using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// L'arme tenue en main droite (2026-10-11) : une copie du modèle de l'arme en main, et d'elle seule.
///
/// Tout ce qui copie le robot reprend ce qu'il y a sous sa main : les bras de la vue subjective, ceux d'un
/// spectateur, et surtout l'épave, à chaque mort et sur chaque machine, dans l'image de l'impact de la kill cam. Et
/// tout ce qui bouge le robot parcourt ce qui pend à ses os. Une arme ne naît donc qu'à sa sortie, et s'en va quand
/// une autre la remplace : une arme imposée en pleine manche (le Gun Game) coûte un préfab et ses accessoires.
///
/// Elle se tient au repère « PriseArme » du préfab joueur, sous la main droite, avec le décalage calculé sur la pose
/// de visée (poignée au creux de la paume, canon dans l'axe de la main) : ses préfabs ont tous la poignée à l'origine
/// et le canon vers +Z (WeaponModel). La copie porte le nom de sa place (« Arme1 » à « Arme13 »), la finition choisie
/// à l'armurerie (PlayerWeaponLooks, 2026-10-15) et les accessoires de sa place (PlayerWeaponAttachments, 2026-10-16),
/// montés par AttachmentMount.
///
/// Catégorie C pure. Sans état, testé.
/// </summary>
public static class HeldWeapons
{
    /// <summary>Le repère du préfab joueur, sous la main droite, où se tient la poignée des armes.</summary>
    public const string GripAnchorName = "PriseArme";

    /// <summary>Le nom de la copie de l'arme d'une place de l'arsenal.</summary>
    public static string SlotName(int slot) => "Arme" + (slot + 1);

    /// <summary>
    /// Met en main le modèle de l'arme `data`, avec sa finition et ses accessoires, et ôte de la main toute autre
    /// arme. Renvoie le modèle (null si `data` n'est pas de `loadout` ou n'a pas de modèle : rien n'est montré, comme
    /// pour un spectateur). `renderLike` donne les ombres de ce qui est créé (celles du corps qui le porte : ombre seule
    /// chez le propriétaire, aucune sur les bras de la vue subjective) ; sans lui, elles gardent celles du préfab.
    /// </summary>
    public static WeaponModel Show(Transform rightHand, IReadOnlyList<WeaponData> loadout, WeaponData data, WeaponLooks looks,
                                   WeaponAttachments attachments, Renderer renderLike = null)
    {
        if (rightHand == null || loadout == null) return null;
        int slot = SlotOf(loadout, data);
        WeaponModel wanted = slot >= 0 && data.modelPrefab != null ? data.modelPrefab.GetComponent<WeaponModel>() : null;

        WeaponModel held = null;
        for (int i = rightHand.childCount - 1; i >= 0; i--)
        {
            // Une arme que porte un spectateur (ArenaItem.CreateVisual) est un modèle inerte : à lui, pas à cette main.
            if (!rightHand.GetChild(i).TryGetComponent(out WeaponModel copy) || !copy.enabled) continue;
            if (held == null && wanted != null && copy.Designation == wanted.Designation) held = copy;
            else PutAway(copy.gameObject);
        }
        if (wanted == null) return null;
        if (held == null) held = Create(wanted, rightHand, slot, renderLike);

        WeaponFinish finish = looks.Finish(slot);
        if (held.TryGetComponent(out WeaponFinishes finishes) && finishes.Shown != finish) finishes.Apply(finish);
        AttachmentMount.Apply(held.transform, data, attachments, slot, finish, renderLike);
        return held;
    }

    private static int SlotOf(IReadOnlyList<WeaponData> loadout, WeaponData data)
    {
        if (data == null) return -1;
        for (int slot = 0; slot < loadout.Count; slot++)
            if (loadout[slot] == data) return slot;
        return -1;
    }

    private static WeaponModel Create(WeaponModel prefab, Transform rightHand, int slot, Renderer renderLike)
    {
        Transform anchor = rightHand.Find(GripAnchorName);
        WeaponModel model = Object.Instantiate(prefab, rightHand, false);
        model.name = SlotName(slot);
        Transform copy = model.transform;
        copy.localPosition = anchor != null ? anchor.localPosition : Vector3.zero;
        copy.localRotation = anchor != null ? anchor.localRotation : Quaternion.identity;
        copy.localScale = Vector3.one;
        // Layer et ombres : ceux du corps qui le porte (ombre seule chez le propriétaire, layer des bras sur la copie de
        // la vue subjective).
        int layer = rightHand.gameObject.layer;
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        if (renderLike != null)
        {
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = renderLike.shadowCastingMode;
                r.receiveShadows = renderLike.receiveShadows;
            }
        }
        return model;
    }

    private static void PutAway(GameObject copy)
    {
        // Destroy est différé : sortie de la main et éteinte tout de suite, elle ne se fait plus trouver ni copier d'ici là.
        copy.SetActive(false);
        copy.transform.SetParent(null, false);
        if (Application.isPlaying) Object.Destroy(copy);
        else Object.DestroyImmediate(copy);
    }

    /// <summary>Le gant à décharge du taser (2026-10-11), posé dans le préfab joueur sur l'avant-bras
    /// gauche, la main qui frappe : origine au poignet, poing vers l'avant. Il remplace la main
    /// gauche, repliée (échelle 0) : son poing est fermé et rigide, la main du robot le traverserait.</summary>
    public const string TaserGloveName = "GantChoc";

    /// <summary>Le gant sous l'avant-bras gauche, ou null.</summary>
    public static Transform TaserGlove(Animator animator)
    {
        Transform forearm = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftLowerArm) : null;
        return forearm != null ? forearm.Find(TaserGloveName) : null;
    }

    /// <summary>Porte le gant (main gauche repliée) ou l'ôte (main gauche visible). La vue subjective
    /// ne le montre que pendant le coup, comme le couteau rapide de Call of Duty : porté en
    /// permanence, il masquait l'arme tenue à deux mains (le raygun).</summary>
    public static void WearTaserGlove(Animator animator, bool worn)
    {
        Transform glove = TaserGlove(animator);
        if (glove == null) return;
        if (glove.gameObject.activeSelf != worn) glove.gameObject.SetActive(worn);
        Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (hand != null) hand.localScale = worn ? Vector3.zero : Vector3.one;
    }

    /// <summary>Le modèle visible sous la main (le premier actif qui porte un WeaponModel), ou null.</summary>
    public static WeaponModel Visible(Transform rightHand)
    {
        if (rightHand == null) return null;
        for (int i = 0; i < rightHand.childCount; i++)
        {
            Transform child = rightHand.GetChild(i);
            if (!child.gameObject.activeSelf) continue;
            if (child.TryGetComponent(out WeaponModel model)) return model;
        }
        return null;
    }
}
