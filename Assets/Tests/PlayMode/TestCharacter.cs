#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Le personnage RÉELLEMENT utilisé par le jeu, pris dans le prefab joueur.
///
/// Les tests d'animation chargeaient le modèle par un chemin écrit en dur : au passage du soldat
/// SWAT à Y Bot (2026-10-05), ils auraient continué de tester l'ancien modèle et resté verts
/// quoi qu'il arrive au nouveau. En lisant l'enfant `Model` du prefab, ils suivent d'eux-mêmes
/// tout changement de personnage.
/// </summary>
public static class TestCharacter
{
    public const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    public const string ControllerPath = "Assets/_ProjectArt/PlayerAnimator.controller";

    /// <summary>Instancie une copie isolée du modèle du joueur, avec le vrai controller.</summary>
    public static GameObject InstantiateModel(out Animator animator)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(prefab, "Prefab joueur introuvable : " + PlayerPrefabPath);

        Transform model = prefab.transform.Find("Model");
        Assert.IsNotNull(model, "Le prefab joueur n'a pas d'enfant « Model ».");

        var controller = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        Assert.IsNotNull(controller, "Controller introuvable : " + ControllerPath);

        GameObject instance = Object.Instantiate(model.gameObject);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        animator = instance.GetComponent<Animator>();
        Assert.IsNotNull(animator, "Pas d'Animator sur le modèle du joueur.");
        Assert.IsTrue(animator.avatar != null && animator.avatar.isHuman,
            "L'avatar du modèle n'est pas humanoïde : les animations ne peuvent pas être retargetées.");

        animator.runtimeAnimatorController = controller;

        // La position appartient à Move() : l'Animator ne doit jamais déplacer le personnage.
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // Les armes en main, comme PlayerAnimator les met au lancement (2026-10-15 : le préfab ne
        // porte plus que leur point de prise, « PriseArme ») : le modèle par défaut de chaque classe,
        // la première visible.
        WeaponController weapon = prefab.GetComponentInChildren<WeaponController>(true);
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        HeldWeapons.Build(hand, weapon.Loadout, WeaponLooks.Default);
        HeldWeapons.Show(hand, weapon.Loadout, weapon.DataOf(0));
        return instance;
    }
}
#endif
