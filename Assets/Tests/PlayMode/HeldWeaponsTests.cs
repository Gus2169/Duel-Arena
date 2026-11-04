#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

/// <summary>
/// L'arme tenue en main droite (2026-10-11) : une copie de l'arme en main, et d'elle seule, créée à sa sortie, au point
/// de prise du préfab ; dans la finition choisie à l'armurerie (WeaponLooks), avec les accessoires de sa place
/// (WeaponAttachments).
///
/// Ce qui casse ici ne lève aucune erreur : deux armes en main, une arme accrochée de travers, un buste qui s'aligne sur
/// la mire d'une arme rangée, un changement d'apparence qui fait disparaître l'arme qu'on tenait, ou tout l'arsenal qui
/// repart dans chaque copie du robot (les bras, l'épave).
/// </summary>
public class HeldWeaponsTests
{
    private GameObject instance;
    private GameObject cameraGo;

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
        if (cameraGo != null) Object.DestroyImmediate(cameraGo);
    }

    private static IReadOnlyList<WeaponData> Loadout() =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TestCharacter.PlayerPrefabPath).GetComponentInChildren<WeaponController>(true).Loadout;

    /// <summary>Les armes sous la main, actives ou non.</summary>
    private static int Held(Transform hand)
    {
        int count = 0;
        for (int i = 0; i < hand.childCount; i++)
            if (hand.GetChild(i).GetComponent<WeaponModel>() != null) count++;
        return count;
    }

    [UnityTest]
    public IEnumerator LaMain_NeTientQueLArmeMontree_AuPointDePrise_EtLaMireLaSuit()
    {
        instance = TestCharacter.InstantiateModel(out Animator animator);   // qui y met la première arme
        yield return null;

        IReadOnlyList<WeaponData> loadout = Loadout();
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform anchor = hand.Find(HeldWeapons.GripAnchorName);
        Assert.IsNotNull(anchor, "Le point de prise des armes n'est plus dans la main droite du préfab.");
        Assert.AreEqual(1, Held(hand), "La première arme est en main, et elle seule.");

        for (int slot = 0; slot < loadout.Count; slot++)
        {
            WeaponData data = loadout[slot];
            WeaponModel shown = HeldWeapons.Show(hand, loadout, data, WeaponLooks.Default, WeaponAttachments.None);
            Assert.IsNotNull(shown, $"{data.weaponName} n'a pas été montré.");
            Assert.AreEqual(1, Held(hand), $"Avec {data.weaponName} en main, {Held(hand)} armes sous la main.");
            Assert.AreEqual(HeldWeapons.SlotName(slot), shown.name);
            Assert.Less(Vector3.Distance(anchor.localPosition, shown.transform.localPosition), 1e-5f, $"{data.weaponName} n'est pas tenu au point de prise.");
            Assert.Less(Quaternion.Angle(anchor.localRotation, shown.transform.localRotation), 0.01f, $"{data.weaponName} n'est pas tenu au point de prise.");
            Assert.AreEqual(data.modelPrefab.GetComponent<WeaponModel>().Designation, shown.Designation, $"{data.weaponName} : ce n'est pas son modèle.");
            Assert.AreSame(shown, HeldWeapons.Visible(hand));
            Assert.AreSame(shown.Sight, PlayerAnimator.FindSight(animator), $"Avec {data.weaponName}, le buste s'alignerait sur la mire d'une autre arme.");
            Assert.AreSame(shown, HeldWeapons.Show(hand, loadout, data, WeaponLooks.Default, WeaponAttachments.None),
                           "La même arme, une seconde fois : la même copie, rien en double.");
        }

        // Rien à montrer : un spectateur, une arme qui n'a pas de modèle (le gant seul).
        Assert.IsNull(HeldWeapons.Show(hand, loadout, null, WeaponLooks.Default, WeaponAttachments.None));
        Assert.AreEqual(0, Held(hand), "À mains nues, il reste une arme sous la main.");
        Assert.IsNull(HeldWeapons.Visible(hand));
    }

    /// <summary>L'apparence arrive du serveur après la première arme (PlayerWeaponLooks) : la finition se pose sur celle
    /// qu'on tient, sans qu'elle disparaisse, et sur celles qui sortent ensuite.</summary>
    [UnityTest]
    public IEnumerator UneAutreApparence_PoseLaFinition_EtGardeLArmeEnMain()
    {
        instance = TestCharacter.InstantiateModel(out Animator animator);
        yield return null;
        IReadOnlyList<WeaponData> loadout = Loadout();
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        WeaponModel held = HeldWeapons.Show(hand, loadout, loadout[2], WeaponLooks.Default, WeaponAttachments.None);

        var looks = WeaponLooks.Default;
        for (int slot = 0; slot < loadout.Count; slot++)
            looks = looks.WithFinish(slot, (WeaponFinish)((slot + 1) % WeaponLooks.FinishCount));
        Assert.AreSame(held, HeldWeapons.Show(hand, loadout, loadout[2], looks, WeaponAttachments.None),
                       "L'arme en main doit le rester.");
        Assert.AreEqual(looks.Finish(2), held.GetComponent<WeaponFinishes>().Shown, "La finition choisie n'est pas posée sur l'arme en main.");

        WeaponModel next = HeldWeapons.Show(hand, loadout, loadout[3], looks, WeaponAttachments.None);
        Assert.AreEqual(looks.Finish(3), next.GetComponent<WeaponFinishes>().Shown, "Une arme sort dans la finition choisie pour sa place.");
        Assert.AreEqual(1, Held(hand), "Une arme en main, pas une de plus.");
        Assert.AreSame(next.Sight, PlayerAnimator.FindSight(animator), "Le buste doit suivre la mire de l'arme en main.");
    }

    /// <summary>Les accessoires arrivent du serveur (PlayerWeaponAttachments, 2026-10-16) : chaque arme sort avec ceux de
    /// sa place, le buste suit la mire du viseur monté, et ils repartent quand le joueur n'en a plus.</summary>
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

        Transform pistol = HeldWeapons.Show(hand, loadout, loadout[3], WeaponLooks.Default, attachments).transform;
        Assert.AreEqual("RX-A", AttachmentMount.Mounted(pistol, AttachmentCategory.Sight)?.Code);
        Assert.AreEqual("LT-P", AttachmentMount.Mounted(pistol, AttachmentCategory.Laser)?.Code);
        Assert.AreEqual(0, HeldWeapons.Show(hand, loadout, loadout[1], WeaponLooks.Default, attachments).GetComponentsInChildren<WeaponAttachment>(true).Length,
            "Le sniper n'a rien demandé.");

        Transform rifle = HeldWeapons.Show(hand, loadout, loadout[0], WeaponLooks.Default, attachments).transform;
        Assert.AreEqual("HO-A", AttachmentMount.Mounted(rifle, AttachmentCategory.Sight)?.Code, "Le fusil d'assaut n'a pas son viseur.");
        Assert.AreEqual("PV-1", AttachmentMount.Mounted(rifle, AttachmentCategory.Grip)?.Code);
        Assert.AreEqual("LT-1", AttachmentMount.Mounted(rifle, AttachmentCategory.Laser)?.Code);
        Assert.IsNotNull(AttachmentMount.Mounted(rifle, AttachmentCategory.Magazine), "Le fusil d'assaut n'a pas son grand chargeur.");
        int layer = hand.gameObject.layer;
        foreach (WeaponAttachment a in rifle.GetComponentsInChildren<WeaponAttachment>())
            Assert.AreEqual(layer, a.gameObject.layer, $"{a.Code} n'est pas sur le layer du corps qui le porte.");
        Assert.AreSame(AttachmentMount.Mounted(rifle, AttachmentCategory.Sight).Sight, PlayerAnimator.FindSight(animator),
            "En visée, le buste doit aligner la mire du viseur, pas la hausse cachée dessous.");

        // Le même choix, une seconde fois : rien en double. Puis plus rien : tout repart.
        HeldWeapons.Show(hand, loadout, loadout[0], WeaponLooks.Default, attachments);
        Assert.AreEqual(4, rifle.GetComponentsInChildren<WeaponAttachment>().Length, "Des accessoires montés en double.");
        HeldWeapons.Show(hand, loadout, loadout[0], WeaponLooks.Default, WeaponAttachments.None);
        yield return null;   // Destroy est différé
        Assert.AreEqual(0, rifle.GetComponentsInChildren<WeaponAttachment>(true).Length, "Les accessoires sont restés sur l'arme.");
        Assert.AreSame(rifle.GetComponent<WeaponModel>().Sight, PlayerAnimator.FindSight(animator), "Sans viseur, la mire de l'arme revient.");
    }

    /// <summary>Une arme qui sort en pleine partie prend le layer et les ombres du corps qui la porte : sur celui du
    /// propriétaire, une ombre seule, et la vitre de son viseur ne se dessine pas (une tache grise devant lui).</summary>
    [UnityTest]
    public IEnumerator UneArmeQuiSort_PrendLeLayerEtLesOmbresDuCorps()
    {
        instance = TestCharacter.InstantiateModel(out Animator animator);
        yield return null;
        IReadOnlyList<WeaponData> loadout = Loadout();
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var skin = animator.GetComponentInChildren<SkinnedMeshRenderer>(true);
        skin.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

        const int slot = 4;
        WeaponAttachments attachments = WeaponAttachments.None.With(slot, 4, GripKind.None, false, false);
        WeaponModel shown = HeldWeapons.Show(hand, loadout, loadout[slot], WeaponLooks.Default, attachments, skin);
        WeaponAttachment sight = AttachmentMount.Mounted(shown.transform, AttachmentCategory.Sight);
        Assert.IsTrue(sight != null && sight.Lens != null, "Garde : l'arme sort avec un viseur à vitre.");

        foreach (Renderer r in shown.GetComponentsInChildren<Renderer>(true))
        {
            Assert.AreEqual(hand.gameObject.layer, r.gameObject.layer, $"{r.name} n'est pas sur le layer du corps qui le porte.");
            if (r == sight.Lens)
            {
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, "La vitre d'un viseur n'a jamais d'ombre.");
                Assert.IsTrue(r.forceRenderingOff, "Sur une arme qui n'est qu'une ombre, la vitre ne se dessine pas.");
                continue;
            }
            Assert.AreEqual(ShadowCastingMode.ShadowsOnly, r.shadowCastingMode, $"{r.name} se dessine sur le corps du propriétaire.");
        }
    }

    /// <summary>Tout ce qui copie le robot reprend ce qu'il y a sous sa main : l'arme en main, et rien d'autre, même juste
    /// après avoir changé d'arme (Destroy est différé). L'arsenal entier partait dans chaque copie.</summary>
    [UnityTest]
    public IEnumerator UneCopieDuRobot_NEmporteQueLArmeEnMain()
    {
        instance = TestCharacter.InstantiateModel(out Animator animator);
        yield return null;
        IReadOnlyList<WeaponData> loadout = Loadout();
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        foreach (WeaponData data in loadout) HeldWeapons.Show(hand, loadout, data, WeaponLooks.Default, WeaponAttachments.None);
        string last = loadout[loadout.Count - 1].modelPrefab.GetComponent<WeaponModel>().Designation;

        cameraGo = new GameObject("CameraTest");
        GameObject view = FirstPersonView.BuildView(instance, cameraGo.transform, LayerMask.NameToLayer(FirstPersonView.LayerName),
                                                    out Animator arms, out Transform sight);
        WeaponModel[] copied = view.GetComponentsInChildren<WeaponModel>(true);
        Assert.AreEqual(1, copied.Length, "La copie des bras emporte d'autres armes que celle en main.");
        Assert.AreEqual(last, copied[0].Designation, "Garde : c'est bien l'arme en main.");
        Assert.AreSame(copied[0].Sight, sight, "Les bras s'alignent sur la mire de l'arme qu'ils tiennent.");
        Assert.AreSame(copied[0], HeldWeapons.Show(arms.GetBoneTransform(HumanBodyBones.RightHand), loadout, loadout[loadout.Count - 1],
                                                   WeaponLooks.Default, WeaponAttachments.None),
                       "Les bras reprennent l'arme qu'ils ont héritée, sans en refaire une.");
    }

    /// <summary>Ce qu'un spectateur porte de la tribune pend à la même main, et c'est aussi un modèle d'arme : il n'est ni
    /// pris pour l'arme en main, ni rangé avec elle.</summary>
    [UnityTest]
    public IEnumerator LArmeQuePorteUnSpectateur_NEstPasUneArmeEnMain()
    {
        instance = TestCharacter.InstantiateModel(out Animator animator);
        yield return null;
        IReadOnlyList<WeaponData> loadout = Loadout();
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        GameObject carried = Object.Instantiate(loadout[1].modelPrefab, hand, false);
        ArenaItem.MakeInert(carried);

        Assert.IsNull(HeldWeapons.Show(hand, loadout, null, WeaponLooks.Default, WeaponAttachments.None), "Garde : à mains nues.");
        Assert.AreSame(hand, carried.transform.parent, "Ce qu'il porte a été rangé avec les armes.");
        WeaponModel shown = HeldWeapons.Show(hand, loadout, loadout[1], WeaponLooks.Default, WeaponAttachments.None);
        Assert.AreNotSame(carried, shown.gameObject, "Ce qu'il porte a été pris pour l'arme en main.");
        Assert.AreSame(hand, carried.transform.parent);
    }

    /// <summary>La mort masque le robot en éteignant ce qu'il porte à cet instant : une arme sortie ensuite ne le serait
    /// pas. Il garde celle qu'il tenait, et celle qui lui revient sort à son retour.</summary>
    [Test]
    public void UnRobotMasque_GardeLArmeQuIlTenait()
    {
        var held = ScriptableObject.CreateInstance<WeaponData>();
        var next = ScriptableObject.CreateInstance<WeaponData>();
        Assert.AreSame(next, PlayerAnimator.WeaponToShow(next, held, false));
        Assert.AreSame(held, PlayerAnimator.WeaponToShow(next, held, true), "Une arme sortie sur un robot masqué flotterait à sa place.");
        Assert.AreSame(held, PlayerAnimator.WeaponToShow(null, held, true), "Masqué, il ne lâche rien non plus.");
        Assert.IsNull(PlayerAnimator.WeaponToShow(null, held, false), "Un spectateur n'a rien en main.");
        Object.DestroyImmediate(held);
        Object.DestroyImmediate(next);
    }
}
#endif
