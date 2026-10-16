#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Les armes tenues en main droite (2026-10-11) : une copie de chaque arme de la panoplie, accrochée
/// au point de prise du préfab, une seule visible ; depuis le 2026-10-15, dans le modèle et la
/// finition choisis à l'armurerie (WeaponLooks), et depuis le 2026-10-16 avec leurs accessoires
/// (WeaponAttachments).
///
/// Ce qui casse ici ne lève aucune erreur : deux armes visibles à la fois, une arme accrochée de
/// travers, un buste qui s'aligne sur la mire d'une arme cachée, un changement d'apparence qui laisse
/// l'ancien modèle en main, ou qui fait disparaître l'arme qu'on tenait.
/// </summary>
public class HeldWeaponsTests
{
    private GameObject instance;

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
    }

    private static IReadOnlyList<WeaponData> Loadout() =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TestCharacter.PlayerPrefabPath).GetComponentInChildren<WeaponController>(true).Loadout;

    [UnityTest]
    public IEnumerator LaPanoplie_EstEnMainDroite_UneSeuleVisible_EtLaMireSuitLArme()
    {
        instance = TestCharacter.InstantiateModel(out Animator animator);   // qui y met les modèles par défaut
        yield return null;

        IReadOnlyList<WeaponData> loadout = Loadout();
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform anchor = hand.Find(HeldWeapons.GripAnchorName);
        Assert.IsNotNull(anchor, "Le point de prise des armes n'est plus dans la main droite du préfab.");

        HeldWeapons.Build(hand, loadout, WeaponLooks.Default); // une seconde fois : rien ne doit être créé en double

        for (int slot = 0; slot < loadout.Count; slot++)
        {
            var copies = new List<Transform>();
            for (int i = 0; i < hand.childCount; i++) if (hand.GetChild(i).name == HeldWeapons.SlotName(slot)) copies.Add(hand.GetChild(i));
            Assert.AreEqual(1, copies.Count, $"{loadout[slot].weaponName} : {copies.Count} modèles dans la main.");
            Assert.Less(Vector3.Distance(anchor.localPosition, copies[0].localPosition), 1e-5f, $"{loadout[slot].weaponName} n'est pas tenu au point de prise.");
            Assert.Less(Quaternion.Angle(anchor.localRotation, copies[0].localRotation), 0.01f, $"{loadout[slot].weaponName} n'est pas tenu au point de prise.");
            Assert.AreEqual(loadout[slot].modelPrefab.GetComponent<WeaponModel>().Designation, copies[0].GetComponent<WeaponModel>().Designation,
                $"{loadout[slot].weaponName} : sans choix, c'est le modèle par défaut de la classe.");
        }

        foreach (WeaponData data in loadout)
        {
            WeaponModel shown = HeldWeapons.Show(hand, loadout, data);
            Assert.IsNotNull(shown, $"{data.weaponName} n'a pas été montré.");
            int visible = 0;
            for (int i = 0; i < hand.childCount; i++)
                if (hand.GetChild(i).gameObject.activeSelf && hand.GetChild(i).GetComponent<WeaponModel>() != null) visible++;
            Assert.AreEqual(1, visible, $"Avec {data.weaponName} en main, {visible} armes sont visibles.");
            Assert.AreSame(shown, HeldWeapons.Visible(hand));
            Assert.AreSame(shown.Sight, PlayerAnimator.FindSight(animator), $"Avec {data.weaponName}, le buste s'alignerait sur la mire d'une autre arme.");
        }
    }

    /// <summary>L'apparence arrive du serveur après la création des armes (PlayerWeaponLooks) : le bon
    /// modèle remplace l'ancien, sans que l'arme en main disparaisse, et la finition est posée.</summary>
    [UnityTest]
    public IEnumerator UneAutreApparence_RemplaceLeModele_GardeLArmeEnMain_EtPoseLaFinition()
    {
        instance = TestCharacter.InstantiateModel(out Animator animator);
        yield return null;
        IReadOnlyList<WeaponData> loadout = Loadout();
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        HeldWeapons.Show(hand, loadout, loadout[2]);

        var looks = WeaponLooks.Default;
        for (int slot = 0; slot < loadout.Count; slot++)
            looks = looks.With(slot, slot % loadout[slot].ModelCount == 0 ? 2 : 1, (WeaponFinish)((slot + 1) % WeaponLooks.FinishCount));
        HeldWeapons.Build(hand, loadout, looks);
        yield return null;   // Destroy est différé : l'ancien modèle part à la fin de l'image

        for (int slot = 0; slot < loadout.Count; slot++)
        {
            Transform copy = hand.Find(HeldWeapons.SlotName(slot));
            Assert.IsNotNull(copy, $"{loadout[slot].weaponName} a disparu de la main.");
            string wanted = loadout[slot].ModelPrefabFor(looks.Model(slot)).GetComponent<WeaponModel>().Designation;
            Assert.AreEqual(wanted, copy.GetComponent<WeaponModel>().Designation, $"{loadout[slot].weaponName} : le modèle choisi n'est pas en main.");
            Assert.AreEqual(looks.Finish(slot), copy.GetComponent<WeaponFinishes>().Shown, $"{loadout[slot].weaponName} : la finition choisie n'est pas posée.");
            Assert.AreEqual(slot == 2, copy.gameObject.activeSelf, $"{loadout[slot].weaponName} : l'arme en main doit le rester, les autres cachées.");
        }
        int copies = 0;
        for (int i = 0; i < hand.childCount; i++) if (hand.GetChild(i).GetComponent<WeaponModel>() != null) copies++;
        Assert.AreEqual(loadout.Count, copies, "Les anciens modèles sont restés dans la main.");
        Assert.AreSame(hand.Find(HeldWeapons.SlotName(2)).GetComponent<WeaponModel>().Sight, PlayerAnimator.FindSight(animator),
            "Le buste doit suivre la mire du nouveau modèle.");
    }

    /// <summary>Les accessoires arrivent du serveur (PlayerWeaponAttachments, 2026-10-16) : chaque arme
    /// de la main porte ceux de sa place, le buste suit la mire du viseur monté, et ils repartent quand
    /// le joueur n'en a plus.</summary>
    [UnityTest]
    public IEnumerator LesAccessoires_SeMontentSurLArmeDeChaquePlace_EtLeBusteSuitLaMireDuViseur()
    {
        instance = TestCharacter.InstantiateModel(out Animator animator);
        yield return null;
        IReadOnlyList<WeaponData> loadout = Loadout();
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);

        var attachments = WeaponAttachments.None
            .With(0, 4, GripKind.Vertical, true, true)    // fusil d'assaut : holographique A, poignée, laser, chargeur
            .With(3, 8, GripKind.None, true, false);      // pistolet : réflex A et laser de poing
        HeldWeapons.Build(hand, loadout, WeaponLooks.Default, attachments);

        Transform rifle = hand.Find(HeldWeapons.SlotName(0));
        Transform pistol = hand.Find(HeldWeapons.SlotName(3));
        Assert.AreEqual("HO-A", AttachmentMount.Mounted(rifle, AttachmentCategory.Sight)?.Code, "Le fusil d'assaut n'a pas son viseur.");
        Assert.AreEqual("PV-1", AttachmentMount.Mounted(rifle, AttachmentCategory.Grip)?.Code);
        Assert.AreEqual("LT-1", AttachmentMount.Mounted(rifle, AttachmentCategory.Laser)?.Code);
        Assert.IsNotNull(AttachmentMount.Mounted(rifle, AttachmentCategory.Magazine), "Le fusil d'assaut n'a pas son grand chargeur.");
        Assert.AreEqual("RX-A", AttachmentMount.Mounted(pistol, AttachmentCategory.Sight)?.Code);
        Assert.AreEqual("LT-P", AttachmentMount.Mounted(pistol, AttachmentCategory.Laser)?.Code);
        Assert.AreEqual(0, hand.Find(HeldWeapons.SlotName(1)).GetComponentsInChildren<WeaponAttachment>(true).Length,
            "Le sniper n'a rien demandé.");
        int layer = hand.gameObject.layer;
        foreach (WeaponAttachment a in rifle.GetComponentsInChildren<WeaponAttachment>())
            Assert.AreEqual(layer, a.gameObject.layer, $"{a.Code} n'est pas sur le layer du corps qui le porte.");

        HeldWeapons.Show(hand, loadout, loadout[0]);
        Assert.AreSame(AttachmentMount.Mounted(rifle, AttachmentCategory.Sight).Sight, PlayerAnimator.FindSight(animator),
            "En visée, le buste doit aligner la mire du viseur, pas la hausse cachée dessous.");

        // Le même choix, une seconde fois : rien en double. Puis plus rien : tout repart.
        HeldWeapons.Build(hand, loadout, WeaponLooks.Default, attachments);
        Assert.AreEqual(4, rifle.GetComponentsInChildren<WeaponAttachment>().Length, "Des accessoires montés en double.");
        HeldWeapons.Build(hand, loadout, WeaponLooks.Default, WeaponAttachments.None);
        yield return null;   // Destroy est différé
        Assert.AreEqual(0, rifle.GetComponentsInChildren<WeaponAttachment>(true).Length, "Les accessoires sont restés sur l'arme.");
        Assert.AreSame(rifle.GetComponent<WeaponModel>().Sight, PlayerAnimator.FindSight(animator), "Sans viseur, la mire de l'arme revient.");
    }
}
#endif
