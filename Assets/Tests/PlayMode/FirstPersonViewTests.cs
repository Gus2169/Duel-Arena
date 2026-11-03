using System.Collections;
using System.Collections.Generic;
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

    /// <summary>
    /// La copie ne montre que ses bras (2026-10-29, demandé par l'utilisateur) : sur chaque châssis, ses pièces du buste et
    /// des clavicules, rendues seules, ne couvrent rien, vues de face, quand celles du corps couvrent une bonne part de
    /// l'image ; ses bras, si. Levée pour un rechargement, la copie montrait le haut de son buste au bas de l'écran ; au
    /// pistolet, l'œil est dans les plaques du buste du CT-01, dont le contour se voyait de l'intérieur, un pan noir sur
    /// 13 % de l'écran. On mesure ce qui se voit : chaque partie rendue seule (les pièces des autres os repliées le temps
    /// du rendu), sur un fond transparent.
    /// </summary>
    [UnityTest]
    public IEnumerator LaCopie_NeMontreQueSesBras_SurChaqueChassis()
    {
        source = TestCharacter.InstantiateModel(out Animator body);
        cameraGo = new GameObject("CameraTest");
        int layer = LayerMask.NameToLayer(FirstPersonView.LayerName);
        GameObject view = FirstPersonView.BuildView(source, cameraGo.transform, layer, out _, out _);
        yield return null;

        // Les os des parties, sur le squelette du corps ; chaque peau de la copie a les mêmes, dans le même ordre.
        var torso = new HashSet<Transform>();
        foreach (HumanBodyBones b in FirstPersonView.TorsoBones)
            if (body.GetBoneTransform(b) != null) torso.Add(body.GetBoneTransform(b));
        var limbs = new HashSet<Transform>();
        foreach (HumanBodyBones b in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm })
            limbs.UnionWith(body.GetBoneTransform(b).GetComponentsInChildren<Transform>(true));
        var bodySkins = new List<SkinnedMeshRenderer>();
        var pairs = new List<(SkinnedMeshRenderer skin, Transform[] bones)>();
        foreach (SkinnedMeshRenderer skin in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skin.bones.Length < 20) continue;
            bodySkins.Add(skin);
            foreach (SkinnedMeshRenderer twin in view.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (twin.name == skin.name) pairs.Add((twin, skin.bones));
        }
        Assert.GreaterOrEqual(bodySkins.Count, RobotChassis.Count, "Garde : chaque châssis a sa peau.");
        Assert.AreEqual(bodySkins.Count, pairs.Count, "Garde : chaque peau du corps a la sienne sur la copie.");

        const int width = 320, height = 320;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        target.Create();
        var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var lens = new GameObject("Objectif");
        lens.transform.SetParent(cameraGo.transform, false);
        lens.transform.localPosition = new Vector3(0f, 1.3f, 1.6f);
        lens.transform.LookAt(cameraGo.transform.TransformPoint(new Vector3(0f, 1.3f, 0f)));
        Camera camera = lens.AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 10f;
        camera.aspect = 1f;
        camera.allowHDR = false;   // sans HDR, le tampon d'URP garde la couche alpha
        camera.allowMSAA = false;
        var fold = new GameObject("Repli").transform;
        fold.SetParent(cameraGo.transform, false);
        fold.localScale = Vector3.zero;

        // La part de l'image que couvrent les peaux `skins` (le corps ou la copie, layer `mask`), seules les pièces des os
        // `keep` (os du corps) gardées.
        float Cover(IEnumerable<(SkinnedMeshRenderer skin, Transform[] bones)> skins, int mask, HashSet<Transform> keep)
        {
            var saved = new List<(SkinnedMeshRenderer, Transform[])>();
            var others = new List<Renderer>();
            foreach (GameObject root in new[] { source, view })
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                    if (!(r is SkinnedMeshRenderer s && s.bones.Length >= 20) && !r.forceRenderingOff)
                    {
                        r.forceRenderingOff = true;   // l'arme, le gant : pas des pièces du robot
                        others.Add(r);
                    }
            try
            {
                foreach ((SkinnedMeshRenderer skin, Transform[] sourceBones) in skins)
                {
                    Transform[] bones = skin.bones;
                    saved.Add((skin, (Transform[])bones.Clone()));
                    for (int i = 0; i < bones.Length; i++)
                        if (!keep.Contains(sourceBones[i])) bones[i] = fold;
                    skin.bones = bones;
                    skin.forceMatrixRecalculationPerRender = true;
                }
                camera.cullingMask = mask;
                var request = new RenderPipeline.StandardRequest { destination = target };
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                RenderTexture.active = previous;
            }
            finally
            {
                foreach ((SkinnedMeshRenderer skin, Transform[] bones) in saved) skin.bones = bones;
                foreach (Renderer r in others) r.forceRenderingOff = false;
            }
            int covered = 0;
            foreach (Color32 c in pixels.GetPixels32())
                if (c.a >= 128) covered++;
            return (float)covered / (width * height);
        }

        var report = new System.Text.StringBuilder();
        try
        {
            foreach (ChassisId chassis in new[] { ChassisId.CT01, ChassisId.Prototype, ChassisId.OS02 })
            {
                RobotChassis.Apply(source.transform, chassis);
                RobotChassis.Apply(view.transform, chassis);
                yield return null;
                var bodyPairs = new List<(SkinnedMeshRenderer, Transform[])>();
                foreach (SkinnedMeshRenderer skin in bodySkins) bodyPairs.Add((skin, skin.bones));
                float bodyTorso = Cover(bodyPairs, 1 << bodySkins[0].gameObject.layer, torso);
                float viewTorso = Cover(pairs, 1 << layer, torso);
                float viewArms = Cover(pairs, 1 << layer, limbs);
                string name = RobotChassis.NameOf(chassis);
                report.AppendLine($"{name} : buste du corps {bodyTorso * 100f:F1} % de l'image, de la copie {viewTorso * 100f:F2} %, bras de la copie {viewArms * 100f:F1} %");
                Assert.Greater(bodyTorso, 0.03f, $"Garde : vu de face, le buste du {name} couvre une bonne part de l'image.");
                Assert.Greater(viewArms, 0.01f, $"Garde : les bras de la copie du {name} se dessinent.");
                // La peau du prototype est continue : rendus seuls par cette mesure (les autres os repliés en un même
                // point), les sommets que l'épaule partage avec le buste laissent des éclats (0,2 % de l'image), que le
                // jeu ne dessine pas. Un buste montré en couvre 5 à 6 %.
                Assert.Less(viewTorso, 0.005f, $"La copie du {name} montre son buste : {viewTorso * 100f:F1} % de l'image.");
            }
        }
        finally
        {
            target.Release();
            Debug.Log("[VUE-SUBJECTIVE] " + report);
        }
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

    /// <summary>
    /// Un tir au pistolet ne fait pas bouger les bras de la vue subjective (2026-10-29, signalé par
    /// l'utilisateur : les bras bougeaient un peu, le viseur avec, avant de se remettre droits et
    /// immobiles). Faute de clip de tir au pistolet, l'état « Tir » rejouait la pose de visée depuis son
    /// début, à vitesse normale, quand les bras la tiennent figée. Le geste reste sur le corps quand on
    /// tire de la hanche (il lève le pistolet, vu des autres), et à toute arme d'épaule.
    /// </summary>
    [UnityTest]
    public IEnumerator UnTirAuPistolet_NeFaitPasBougerLesBras()
    {
        source = TestCharacter.InstantiateModel(out Animator body);
        cameraGo = new GameObject("CameraTest");
        yield return null;

        float moved = MeasurePistolShots(body, (b, v) => PlayerAnimator.FireGesture(b, v, true, true));
        float before = MeasurePistolShots(body, (b, v) => { b.SetTrigger("Fire"); v.SetTrigger("Fire"); });
        Debug.Log($"[VUE-SUBJECTIVE] main droite des bras au tir du pistolet : {moved * 1000f:F2} mm au plus, contre {before * 1000f:F1} mm quand le geste partait sur les bras");
        Assert.Greater(before, 0.003f, "Garde anti-vacuité : déclenché sur les bras, le geste de tir du pistolet les faisait bouger.");
        Assert.Less(moved, 0.0005f, $"La main droite des bras bouge de {moved * 1000f:F2} mm au tir du pistolet : le viseur avec.");

        // Où le geste part : au pistolet, sur le corps seulement de la hanche ; aux armes d'épaule, partout.
        var arms = FirstPersonView.BuildView(source, cameraGo.transform, LayerMask.NameToLayer(FirstPersonView.LayerName),
                                             out Animator view, out _);
        foreach ((bool pistol, bool aiming, bool onBody, bool onView) in new[]
                 { (true, true, false, false), (true, false, true, false), (false, true, true, true), (false, false, true, true) })
        {
            body.ResetTrigger("Fire");
            view.ResetTrigger("Fire");
            PlayerAnimator.FireGesture(body, view, pistol, aiming);
            string what = (pistol ? "pistolet" : "arme d'épaule") + (aiming ? " épaulé" : " à la hanche");
            Assert.AreEqual(onBody, body.GetBool("Fire"), $"{what} : le geste sur le corps.");
            Assert.AreEqual(onView, view.GetBool("Fire"), $"{what} : le geste sur les bras.");
        }
        Object.DestroyImmediate(arms);
    }

    /// <summary>Tient le pistolet en visée, tire trois coups (fire), et relève de combien la main droite
    /// des bras se déplace dans la copie.</summary>
    private float MeasurePistolShots(Animator body, System.Action<Animator, Animator> fire)
    {
        int layer = LayerMask.NameToLayer(FirstPersonView.LayerName);
        GameObject view = FirstPersonView.BuildView(source, cameraGo.transform, layer, out Animator arms, out _);
        Transform hand = arms.GetBoneTransform(HumanBodyBones.RightHand);
        body.Rebind();
        const float dt = 1f / 60f;
        Vector3 reference = Vector3.zero;
        float max = 0f;
        for (int i = 0; i < 300; i++)
        {
            body.SetBool("Aiming", true);
            body.SetFloat("SpeedMult", 1f);
            body.SetFloat("Pistolet", 1f);
            body.Update(dt);
            PlayerAnimator.MirrorWeaponGestures(body, arms, 0f);
            if (i == 90 || i == 150 || i == 210) fire(body, arms);
            arms.Update(dt);
            Vector3 p = view.transform.InverseTransformPoint(hand.position);
            if (i == 89) reference = p;
            if (i >= 90) max = Mathf.Max(max, Vector3.Distance(reference, p));
        }
        Object.DestroyImmediate(view);
        return max;
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
