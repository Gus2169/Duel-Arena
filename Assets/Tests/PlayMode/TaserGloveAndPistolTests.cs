#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Le gant à décharge du taser et la tenue de pistolet (2026-10-11).
///
/// Ce qui casse ici ne lève aucune erreur : un raygun tenu crosse à l'épaule (contre le visage), des
/// bras de vue subjective qui tiennent un fusil pendant que le corps tient un pistolet, une main de
/// robot qui traverse le poing du gant, un gant qui masque l'arme en permanence, un poing qui frappe
/// sous l'écran.
/// </summary>
public class TaserGloveAndPistolTests
{
    private GameObject instance;
    private GameObject cameraGo;
    private Animator animator;

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
        if (cameraGo != null) Object.DestroyImmediate(cameraGo);
    }

    private string DominantClip(Animator a, int layer)
    {
        var infos = a.GetCurrentAnimatorClipInfo(layer);
        Assert.Greater(infos.Length, 0, "Aucun clip joué sur la couche " + layer + ".");
        string best = null;
        float weight = -1f;
        foreach (var info in infos)
        {
            if (info.weight > weight)
            {
                weight = info.weight;
                best = info.clip.name;
            }
        }
        return best;
    }

    [UnityTest]
    public IEnumerator LeRaygun_SeTientADeuxMains_EtLesBrasDeLaVueLeTiennentPareil()
    {
        instance = TestCharacter.InstantiateModel(out animator);
        yield return null;

        animator.SetFloat("SpeedMult", 1f);
        animator.SetFloat("StanceF", 0f);
        animator.SetFloat("Pistolet", 1f);
        animator.SetBool("Aiming", true);
        yield return new WaitForSeconds(0.5f);
        Assert.AreEqual("Pistol Aim Idle", DominantClip(animator, 1), "En visée, un pistolet ne se tient pas comme un fusil.");

        Transform root = animator.transform;
        Vector3 left = root.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
        Vector3 right = root.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightHand).position);
        Debug.Log($"[PISTOLET] mains : gauche {left:F2}, droite {right:F2}, écart {Vector3.Distance(left, right) * 100f:F0} cm");
        // Mesuré le 2026-10-11 : mains à 25 cm devant le corps, à quelques centimètres l'une de
        // l'autre. Le fusil, lui, les écarte de 33 cm (poignée et garde-main).
        Assert.Less(Vector3.Distance(left, right), 0.15f, "Le pistolet se tient à deux mains, jointes.");
        Assert.Greater(right.z, 0.2f, "Les bras sont tendus devant soi.");

        animator.SetBool("Aiming", false);
        yield return new WaitForSeconds(0.5f);
        Assert.AreEqual("Pistol NoAim Idle", DominantClip(animator, 1));

        animator.SetFloat("Pistolet", 0f);
        animator.SetBool("Aiming", true);
        yield return new WaitForSeconds(0.5f);
        Assert.AreEqual("Rifle Aiming Idle", DominantClip(animator, 1), "Le fusil garde sa tenue.");

        // Les bras de la vue subjective prennent la tenue du corps.
        cameraGo = new GameObject("CameraTest");
        int layer = LayerMask.NameToLayer(FirstPersonView.LayerName);
        GameObject view = FirstPersonView.BuildView(instance, cameraGo.transform, layer, out Animator arms, out _);
        animator.SetFloat("Pistolet", 1f);
        PlayerAnimator.MirrorWeaponGestures(animator, arms, 0f);
        Assert.AreEqual(1f, arms.GetFloat("Pistolet"), "Les bras de la vue subjective tiennent le pistolet comme un fusil.");
        Object.DestroyImmediate(view);
    }

    [UnityTest]
    public IEnumerator LeGant_RemplaceLaMainGauche_EtSOteSurDemande()
    {
        instance = TestCharacter.InstantiateModel(out animator);
        yield return null;

        Transform glove = HeldWeapons.TaserGlove(animator);
        Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Assert.IsNotNull(glove, "Le robot ne porte pas le gant du taser sur l'avant-bras gauche.");
        Assert.IsTrue(glove.gameObject.activeSelf, "Vu par les autres, le gant est toujours porté.");
        Assert.AreEqual(Vector3.zero, hand.localScale, "La main du robot traverserait le poing du gant.");

        HeldWeapons.WearTaserGlove(animator, false);
        Assert.IsFalse(glove.gameObject.activeSelf);
        Assert.AreEqual(Vector3.one, hand.localScale, "Gant ôté, la main doit réapparaître.");

        HeldWeapons.WearTaserGlove(animator, true);
        Assert.IsTrue(glove.gameObject.activeSelf);
        Assert.AreEqual(Vector3.zero, hand.localScale);
    }

    /// <summary>Le Taser Punch frappe à hauteur de poitrine, 40 cm sous l'œil : en vue subjective, le
    /// bras est réorienté à l'épaule pour que le poing parte vers l'écran, à la distance que lui donne
    /// le clip.</summary>
    [UnityTest]
    public IEnumerator AuContact_LePoingDeLaVueSubjectivePartVersLaCible_SansChangerSonAllonge()
    {
        instance = TestCharacter.InstantiateModel(out animator);
        yield return null;
        animator.SetFloat("SpeedMult", 1f);
        animator.SetFloat("TaserSpeed", 0.72f / 0.6f);
        animator.SetLayerWeight(2, 1f);
        yield return new WaitForSeconds(0.3f);
        animator.SetTrigger("Taser");
        yield return new WaitForSeconds(0.18f);

        Transform upperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform fist = HeldWeapons.TaserGlove(animator).Find("Poing");
        Assert.IsNotNull(fist, "Le gant n'a pas de repère « Poing ».");
        Vector3 shoulder = upperArm.position;
        float reach = Vector3.Distance(shoulder, fist.position);
        Vector3 target = shoulder + Quaternion.Euler(-40f, 25f, 0f) * animator.transform.forward;

        FirstPersonView.AimStrike(upperArm, fist, target, 1f);
        float angle = Vector3.Angle(fist.position - shoulder, target - shoulder);
        float newReach = Vector3.Distance(shoulder, fist.position);
        Debug.Log($"[TASER-VUE] poing à {angle:F2}° de la cible, allonge {reach * 100f:F1} → {newReach * 100f:F1} cm");
        Assert.Less(angle, 0.5f, "Le poing ne part pas vers la cible.");
        Assert.Less(Mathf.Abs(newReach - reach), 0.002f, "Le bras a changé d'allonge : seule sa direction doit changer.");
        Assert.Greater(reach, 0.4f, "Garde anti-vacuité : au contact, le bras devrait être tendu.");
    }
}
#endif
