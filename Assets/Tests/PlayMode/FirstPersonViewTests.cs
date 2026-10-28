using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

/// <summary>
/// La vue subjective (2026-10-08) : les bras et l'arme que le joueur voit de ses propres yeux.
///
/// Ce qui casse ici ne lève aucune erreur : une copie qui projette une ombre (le joueur aurait deux
/// ombres), une tête dépliée (l'intérieur du crâne en plein écran), une arme qui ne pointe pas là
/// où vont les balles.
/// </summary>
public class FirstPersonViewTests
{
    private GameObject source;
    private GameObject cameraGo;

    [TearDown]
    public void TearDown()
    {
        if (source != null) Object.DestroyImmediate(source);
        if (cameraGo != null) Object.DestroyImmediate(cameraGo);
    }

    [UnityTest]
    public IEnumerator LaCopie_VitSurLeLayerDesBras_SansOmbre_TeteCouEtJambesRepliees()
    {
        source = TestCharacter.InstantiateModel(out _);
        cameraGo = new GameObject("CameraTest");
        int layer = LayerMask.NameToLayer(FirstPersonView.LayerName);
        Assert.GreaterOrEqual(layer, 0, "Le layer des bras n'existe pas.");

        GameObject view = FirstPersonView.BuildView(source, cameraGo.transform, layer, out Animator animator, out Transform sight);
        yield return null;

        Assert.AreSame(cameraGo.transform, view.transform.parent, "Les bras doivent suivre la caméra.");
        Assert.IsNotNull(sight, "Le fusil de la copie n'a pas de repère « Mire ».");
        foreach (Renderer r in view.GetComponentsInChildren<Renderer>(true))
        {
            Assert.AreEqual(layer, r.gameObject.layer, r.name + " n'est pas sur le layer des bras : la caméra du monde le dessinerait aussi.");
            Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, r.name + " projette une ombre : le joueur en aurait deux.");
        }
        Assert.AreEqual(Vector3.zero, animator.GetBoneTransform(HumanBodyBones.Head).localScale, "La tête n'est pas repliée : la caméra est dedans.");
        Assert.AreEqual(Vector3.zero, animator.GetBoneTransform(HumanBodyBones.Neck).localScale, "Le cou n'est pas replié : au pistolet, ses pièces passent devant l'œil.");
        Assert.AreEqual(Vector3.zero, animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localScale);
        Assert.AreEqual(Vector3.zero, animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).localScale);
    }

    [UnityTest]
    public IEnumerator LePlacement_MetLaMireSurLaCible_EtLeCanonDansLAxeDuRegard()
    {
        cameraGo = new GameObject("CameraTest");
        cameraGo.transform.SetPositionAndRotation(new Vector3(3f, 1.6f, -2f), Quaternion.Euler(-20f, 35f, 0f));

        // Une « copie » réduite à l'essentiel : une racine et une mire, posée de travers, comme le
        // serait le fusil d'une animation qui ne vise pas droit devant.
        source = new GameObject("Copie");
        source.transform.SetParent(cameraGo.transform, false);
        var sight = new GameObject("Mire").transform;
        sight.SetParent(source.transform, false);
        sight.localPosition = new Vector3(-0.2f, -0.3f, 0.25f);
        sight.localRotation = Quaternion.Euler(10f, -38f, 5f);
        yield return null;

        // Même mesure que FirstPersonView.LateUpdate.
        Transform root = source.transform;
        Vector3 pivot = root.InverseTransformPoint(sight.position);
        Quaternion align = FirstPersonView.LevelSight(root.InverseTransformDirection(sight.forward), root.InverseTransformDirection(sight.up));
        var target = new Vector3(0f, 0f, 0.10f);
        FirstPersonView.Place(root, align, pivot, target);

        Transform cam = cameraGo.transform;
        Vector3 sightInCamera = cam.InverseTransformPoint(sight.position);
        Vector3 forwardInCamera = cam.InverseTransformDirection(sight.forward);
        Vector3 upInCamera = cam.InverseTransformDirection(sight.up);
        Assert.Less(Vector3.Distance(target, sightInCamera), 1e-4f, $"La mire est en {sightInCamera:F3}, pas sur la cible.");
        Assert.Less(Vector3.Angle(Vector3.forward, forwardInCamera), 0.01f, "Le canon ne pointe pas dans l'axe du regard.");
        // D'aplomb : le haut du viseur reste dans le plan vertical de l'écran (2026-10-09). La pose
        // de départ est de travers : aligner l'axe seul laisse le viseur penché de 8,5°.
        Assert.Less(Mathf.Abs(upInCamera.x), 1e-3f, $"Le viseur penche : son haut part de {Mathf.Asin(upInCamera.x) * Mathf.Rad2Deg:F1}° sur le côté.");
    }
    /// <summary>
    /// L'arme des bras ne bouge pas quand le corps fait des pas chassés gauche-droite en visant, ni
    /// quand il change de posture (2026-10-10). Rapporté par l'utilisateur : en visée, des
    /// gauche-droite faisaient « bouger l'arme au point de ne plus pouvoir viser », et se relever
    /// faisait passer la caméra à travers le buste. Les bras rejouaient tout le corps : les jambes
    /// tournaient les hanches, donc le buste et le fusil, et les clips de changement de posture
    /// balayaient le buste devant l'œil. Ils ne reprennent plus que les gestes de l'arme.
    /// </summary>
    [UnityTest]
    public IEnumerator EnVisee_PasChassesEtPostures_NeFontPasBougerLArmeDesBras()
    {
        source = TestCharacter.InstantiateModel(out Animator body);
        cameraGo = new GameObject("CameraTest");
        yield return null;

        Measure(body, (b, v) => PlayerAnimator.MirrorWeaponGestures(b, v, 0f), out float angle, out float shift);
        Measure(body, MirrorEverything, out float oldAngle, out float oldShift);
        Debug.Log($"[VUE-SUBJECTIVE] fusil des bras : {angle:F1}° et {shift * 100f:F1} cm au plus, contre {oldAngle:F1}° et {oldShift * 100f:F1} cm en rejouant tout le corps");
        Assert.Greater(oldAngle, 10f, "Garde anti-vacuité : en rejouant tout le corps, le fusil devrait tourner.");
        // Mesuré le 2026-10-10 : 0,0° et 0,0 cm (les poses d'attente des bras ne défilent pas non
        // plus), contre 98° et 184 cm quand les bras rejouaient tout le corps.
        Assert.Less(angle, 0.5f, $"Le fusil des bras tourne de {angle:F1}° : il suit les jambes, la posture ou la respiration.");
        Assert.Less(shift, 0.005f, $"Le fusil des bras se déplace de {shift * 100f:F1} cm : il suit les jambes, la posture ou la respiration.");
    }

    /// <summary>Joue le corps (pas chassés visés, puis accroupi, allongé, debout), recopie vers une
    /// copie des bras, et relève de combien la mire du fusil tourne et se déplace dans la copie.</summary>
    private void Measure(Animator body, System.Action<Animator, Animator> mirror, out float maxAngle, out float maxShift)
    {
        int layer = LayerMask.NameToLayer(FirstPersonView.LayerName);
        GameObject view = FirstPersonView.BuildView(source, cameraGo.transform, layer, out Animator arms, out Transform sight);
        body.Rebind();
        const float dt = 1f / 60f;
        Vector3 refForward = Vector3.zero, refPosition = Vector3.zero;
        maxAngle = maxShift = 0f;
        for (int i = 0; i < 600; i++)
        {
            float t = i * dt;
            int stance = t < 5f ? 0 : t < 6.5f ? 1 : t < 8.5f ? 2 : 0;
            bool strafing = t >= 1.5f && t < 5f;
            body.SetBool("Aiming", true);
            body.SetFloat("SpeedMult", 1f);
            body.SetFloat("LocoSpeed", 1f);
            body.SetFloat("Marche", strafing ? 1f : 0f);
            PlayerAnimator.SetLocomotion(body, new Vector2(strafing ? (Mathf.Repeat(t, 0.7f) < 0.35f ? 0.55f : -0.55f) : 0f, 0f));
            body.SetInteger("Stance", stance);
            body.SetFloat("StanceF", stance);
            body.Update(dt);
            mirror(body, arms);
            arms.Update(dt);

            Vector3 forward = view.transform.InverseTransformDirection(sight.forward);
            Vector3 position = view.transform.InverseTransformPoint(sight.position);
            if (i == 89) { refForward = forward; refPosition = position; }
            if (i >= 90)
            {
                maxAngle = Mathf.Max(maxAngle, Vector3.Angle(refForward, forward));
                maxShift = Mathf.Max(maxShift, Vector3.Distance(refPosition, position));
            }
        }
        Object.DestroyImmediate(view);
    }

    /// <summary>L'ancienne recopie : tous les paramètres et poids de couches du corps.</summary>
    private static void MirrorEverything(Animator body, Animator view)
    {
        foreach (AnimatorControllerParameter p in body.parameters)
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Float: view.SetFloat(p.nameHash, body.GetFloat(p.nameHash)); break;
                case AnimatorControllerParameterType.Int: view.SetInteger(p.nameHash, body.GetInteger(p.nameHash)); break;
                case AnimatorControllerParameterType.Bool: view.SetBool(p.nameHash, body.GetBool(p.nameHash)); break;
            }
        }
        for (int l = 1; l < body.layerCount && l < view.layerCount; l++) view.SetLayerWeight(l, body.GetLayerWeight(l));
        view.SetBool("Aiming", true);
    }
}
