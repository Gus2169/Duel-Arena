using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Les armes tenues en main droite (2026-10-11) : une copie du modèle de chaque arme de la panoplie,
/// toutes accrochées comme le fusil d'assaut du préfab joueur, une seule visible à la fois.
///
/// Le fusil d'assaut (« FusilAssaut ») est posé dans le préfab, avec le décalage calculé sur la pose
/// de visée (poignée au creux de la paume, canon dans l'axe de la main). Les autres armes sont créées
/// au lancement à côté de lui, avec le même décalage : leurs préfabs ont tous la poignée à
/// l'origine et le canon vers +Z (WeaponModel). Fait AVANT que la vue subjective ne copie le modèle,
/// pour que ses bras aient les mêmes armes.
///
/// Catégorie C pure. Sans état, testé.
/// </summary>
public static class HeldWeapons
{
    /// <summary>Le nom de l'arme posée dans le préfab joueur, qui donne le décalage de la main.</summary>
    public const string ReferenceModelName = "FusilAssaut";

    /// <summary>Crée sous la main les modèles qui manquent, avec le décalage du fusil de référence.</summary>
    public static void Build(Transform rightHand, IReadOnlyList<WeaponData> loadout)
    {
        if (rightHand == null || loadout == null) return;
        Transform reference = rightHand.Find(ReferenceModelName);
        Vector3 position = reference != null ? reference.localPosition : Vector3.zero;
        Quaternion rotation = reference != null ? reference.localRotation : Quaternion.identity;

        foreach (WeaponData data in loadout)
        {
            if (data == null || data.modelPrefab == null) continue;
            if (rightHand.Find(data.modelPrefab.name) != null) continue;
            GameObject model = Object.Instantiate(data.modelPrefab, rightHand, false);
            model.name = data.modelPrefab.name;
            model.transform.localPosition = position;
            model.transform.localRotation = rotation;
            model.transform.localScale = Vector3.one;
            // Layer et ombres : ceux du corps qui le porte, posés ensuite sur tout le modèle (ombre
            // seule chez le propriétaire, layer des bras sur la copie de la vue subjective).
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = rightHand.gameObject.layer;
            model.SetActive(false);
        }
    }

    /// <summary>Montre le modèle de `data` et cache les autres armes de la panoplie. Renvoie le
    /// modèle montré (null s'il n'existe pas sous cette main).</summary>
    public static WeaponModel Show(Transform rightHand, IReadOnlyList<WeaponData> loadout, WeaponData data)
    {
        if (rightHand == null || loadout == null) return null;
        WeaponModel shown = null;
        foreach (WeaponData candidate in loadout)
        {
            if (candidate == null || candidate.modelPrefab == null) continue;
            Transform model = rightHand.Find(candidate.modelPrefab.name);
            if (model == null) continue;
            bool visible = candidate == data;
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
