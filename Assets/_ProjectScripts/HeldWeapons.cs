using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Les armes tenues en main droite (2026-10-11) : une copie du modèle de chaque arme de l'arsenal,
/// toutes accrochées au même point, une seule visible à la fois.
///
/// Le point est le repère « PriseArme » posé dans le préfab joueur sous la main droite, avec le
/// décalage calculé sur la pose de visée (poignée au creux de la paume, canon dans l'axe de la main).
/// Les modèles sont créés au lancement à partir de lui : leurs préfabs ont tous la poignée à l'origine
/// et le canon vers +Z (WeaponModel). Fait AVANT que la vue subjective ne copie le modèle, pour que
/// ses bras aient les mêmes armes, puis refait sur le corps et sur les bras quand l'apparence choisie
/// à l'armurerie arrive du serveur (PlayerWeaponLooks, 2026-10-15) : la finition de chaque arme.
///
/// Chaque copie porte le nom de sa place (« Arme1 » à « Arme12 ») : c'est par lui qu'on la retrouve. Elle porte les accessoires choisis pour sa place
/// (PlayerWeaponAttachments, 2026-10-16), montés par AttachmentMount.
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
    /// Met sous la main le modèle de chaque arme, avec sa finition : crée ce qui manque, remplace un
    /// modèle qui n'est plus le bon (en gardant sa visibilité), et ne touche pas aux autres. `renderLike` donne les ombres des nouvelles pièces (celles du corps qui les porte :
    /// ombre seule chez le propriétaire, aucune sur les bras de la vue subjective) ; sans lui, elles
    /// gardent celles du préfab.
    /// </summary>
    public static void Build(Transform rightHand, IReadOnlyList<WeaponData> loadout, WeaponLooks looks, Renderer renderLike = null)
        => Build(rightHand, loadout, looks, WeaponAttachments.None, renderLike);

    /// <summary>La même chose, avec les accessoires de chaque place (AttachmentMount.Apply).</summary>
    public static void Build(Transform rightHand, IReadOnlyList<WeaponData> loadout, WeaponLooks looks, WeaponAttachments attachments,
                             Renderer renderLike = null)
    {
        if (rightHand == null || loadout == null) return;
        Transform anchor = rightHand.Find(GripAnchorName);
        Vector3 position = anchor != null ? anchor.localPosition : Vector3.zero;
        Quaternion rotation = anchor != null ? anchor.localRotation : Quaternion.identity;

        for (int slot = 0; slot < loadout.Count; slot++)
        {
            WeaponData data = loadout[slot];
            GameObject prefab = data != null ? data.modelPrefab : null;
            if (prefab == null) continue;

            Transform existing = rightHand.Find(SlotName(slot));
            bool visible = existing != null && existing.gameObject.activeSelf;
            if (existing != null && !SameModel(existing.gameObject, prefab))
            {
                // Destroy est différé : renommé et éteint, l'ancien ne se fait plus trouver d'ici là.
                existing.name += " (remplacée)";
                existing.gameObject.SetActive(false);
                if (Application.isPlaying) Object.Destroy(existing.gameObject);
                else Object.DestroyImmediate(existing.gameObject);
                existing = null;
            }

            if (existing == null)
            {
                GameObject model = Object.Instantiate(prefab, rightHand, false);
                model.name = SlotName(slot);
                model.transform.localPosition = position;
                model.transform.localRotation = rotation;
                model.transform.localScale = Vector3.one;
                // Layer et ombres : ceux du corps qui le porte (ombre seule chez le propriétaire, layer
                // des bras sur la copie de la vue subjective).
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
                model.SetActive(visible);
                existing = model.transform;
            }

            if (existing.TryGetComponent(out WeaponFinishes finishes) && finishes.Shown != looks.Finish(slot))
                finishes.Apply(looks.Finish(slot));
            AttachmentMount.Apply(existing, data, attachments, slot, looks.Finish(slot), renderLike);
        }
    }

    /// <summary>La copie sous la main est-elle ce préfab ? (même désignation de modèle)</summary>
    private static bool SameModel(GameObject copy, GameObject prefab)
    {
        var a = copy.GetComponent<WeaponModel>();
        var b = prefab.GetComponent<WeaponModel>();
        if (a == null || b == null) return a == b;
        return a.Designation == b.Designation;
    }

    /// <summary>Montre l'arme de `data` et cache les autres armes de l'arsenal. Renvoie le modèle
    /// montré (null s'il n'existe pas sous cette main, ou si `data` n'est pas de l'arsenal : rien
    /// n'est montré).</summary>
    public static WeaponModel Show(Transform rightHand, IReadOnlyList<WeaponData> loadout, WeaponData data)
    {
        if (rightHand == null || loadout == null) return null;
        WeaponModel shown = null;
        for (int slot = 0; slot < loadout.Count; slot++)
        {
            Transform model = rightHand.Find(SlotName(slot));
            if (model == null) continue;
            bool visible = data != null && loadout[slot] == data;
            if (model.gameObject.activeSelf != visible) model.gameObject.SetActive(visible);
            if (visible) shown = model.GetComponent<WeaponModel>();
        }
        return shown;
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
