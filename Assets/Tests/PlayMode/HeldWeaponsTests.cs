#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Les armes tenues en main droite (2026-10-11) : une copie de chaque arme de la panoplie, accrochée
/// comme le fusil d'assaut du préfab, une seule visible.
///
/// Ce qui casse ici ne lève aucune erreur : deux armes visibles à la fois, une arme accrochée de
/// travers, un buste qui s'aligne sur la mire d'une arme cachée.
/// </summary>
public class HeldWeaponsTests
{
    private GameObject instance;

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
    }

    [UnityTest]
    public IEnumerator LaPanoplie_EstEnMainDroite_UneSeuleVisible_EtLaMireSuitLArme()
    {
        instance = TestCharacter.InstantiateModel(out Animator animator);
        yield return null;

        IReadOnlyList<WeaponData> loadout = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TestCharacter.PlayerPrefabPath)
            .GetComponentInChildren<WeaponController>(true).Loadout;
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform reference = hand.Find(HeldWeapons.ReferenceModelName);
        Assert.IsNotNull(reference, "Le fusil de référence n'est plus dans la main droite du préfab.");

        HeldWeapons.Build(hand, loadout);
        HeldWeapons.Build(hand, loadout); // une seconde fois : rien ne doit être créé en double

        foreach (WeaponData data in loadout)
        {
            var copies = new List<Transform>();
            for (int i = 0; i < hand.childCount; i++) if (hand.GetChild(i).name == data.modelPrefab.name) copies.Add(hand.GetChild(i));
            Assert.AreEqual(1, copies.Count, $"{data.weaponName} : {copies.Count} modèles dans la main.");
            Assert.Less(Vector3.Distance(reference.localPosition, copies[0].localPosition), 1e-5f, $"{data.weaponName} n'est pas tenu comme le fusil de référence.");
            Assert.Less(Quaternion.Angle(reference.localRotation, copies[0].localRotation), 0.01f, $"{data.weaponName} n'est pas tenu comme le fusil de référence.");
        }

        foreach (WeaponData data in loadout)
        {
            WeaponModel shown = HeldWeapons.Show(hand, loadout, data);
            Assert.IsNotNull(shown, $"{data.weaponName} n'a pas été montré.");
            Assert.AreEqual(data.modelPrefab.name, shown.name);
            int visible = 0;
            for (int i = 0; i < hand.childCount; i++)
                if (hand.GetChild(i).gameObject.activeSelf && hand.GetChild(i).GetComponent<WeaponModel>() != null) visible++;
            Assert.AreEqual(1, visible, $"Avec {data.weaponName} en main, {visible} armes sont visibles.");
            Assert.AreSame(shown, HeldWeapons.Visible(hand));
            Assert.AreSame(shown.Sight, PlayerAnimator.FindSight(animator), $"Avec {data.weaponName}, le buste s'alignerait sur la mire d'une autre arme.");
        }
    }
}
#endif
