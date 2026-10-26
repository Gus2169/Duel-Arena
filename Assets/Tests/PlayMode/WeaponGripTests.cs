#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Les mains sur l'arme (WeaponGrip, 2026-10-17, dette n° 71), avec le vrai Animator : en visée, la main
/// gauche tient le garde-main de chacun des douze modèles (sur le corps, le poing du gant du taser ; sur
/// les bras de la vue subjective, la paume), ou la poignée montée ; la droite tient le poignet d'une
/// crosse droite (FP-30, FAP-30) sans que l'arme bouge ; au rechargement, la main gauche suit le clip.
///
/// Ce qui casse ici ne lève aucune erreur : une main qui flotte sous l'arme, un poing qui traverse le
/// garde-main, une arme qu'emporte la main droite, une main restée collée au garde-main pendant que le
/// clip va chercher le chargeur. On mesure ce qui se voit, sur les maillages : le milieu de la paume du
/// robot, déformé comme Unity le déforme ; le poing du gant, qui doit être sous le repère (du côté où
/// regarde la paume de la prise) et l'effleurer sans traverser l'arme. Le seul contact d'une surface ne
/// suffit pas : sans prise, le gros poing du gant frôle déjà le repère, ou le traverse.
///
/// Ces maillages ne sont pas lisibles en Play Mode (Read/Write coupé à l'import, ce qui convient au jeu) :
/// Surfaces les relève avant (IPrebuildSetup, en mode Édition).
/// </summary>
[PrebuildSetup(typeof(WeaponGripTests.Surfaces))]
public class WeaponGripTests
{
    /// <summary>La main tient le repère à 2 cm près.</summary>
    private const float Touch = 0.02f;

    /// <summary>Le poing du gant : ce qui dépasse de 10 cm le poignet (les jointures sont à 12,8 cm, le
    /// bout des électrodes à 19,2 cm ; en deçà, la manchette).</summary>
    private const float FistStart = 0.10f;

    private GameObject instance;
    private GameObject cameraGo;
    private Animator body, arms;
    private WeaponGrip bodyGrip, armsGrip;

    /// <summary>Ce que le test relit des maillages du gant et du robot, relevé en mode Édition.</summary>
    [System.Serializable]
    private class SurfaceData
    {
        public Vector3[] fist;          // les sommets du poing du gant, dans le repère du gant
        public Vector3 fistCenter;      // le milieu de la boîte qu'ils occupent
        public Vector3 palm;            // le milieu de la paume gauche, sommet de la peau (repère du maillage)
        public string[] palmBones;      // ses os, par leur chemin sous « Model »
        public Matrix4x4[] palmBindPoses;
        public float[] palmWeights;
    }

    private static string SurfacePath => System.IO.Path.Combine(Application.dataPath, "..", "Temp", "WeaponGripTests.json");
    private static SurfaceData surfaces;

    /// <summary>Relève, avant le Play Mode, ce que le test mesure : le poing du gant, et le sommet de la
    /// peau au milieu de la paume gauche (le plus en avant côté paume, à mi-chemin du poignet aux
    /// jointures).</summary>
    public class Surfaces : IPrebuildSetup
    {
        public void Setup()
        {
            Transform model = Prefab.transform.Find("Model");
            Animator animator = model.GetComponent<Animator>();
            var data = new SurfaceData();

            Transform glove = HeldWeapons.TaserGlove(animator);
            var fist = new List<Vector3>();
            foreach (MeshFilter filter in glove.GetComponentsInChildren<MeshFilter>(true))
                foreach (Vector3 v in filter.sharedMesh.vertices)
                {
                    Vector3 local = glove.InverseTransformPoint(filter.transform.TransformPoint(v));
                    if (local.z > FistStart) fist.Add(local);
                }
            data.fist = fist.ToArray();
            var box = new Bounds(fist[0], Vector3.zero);
            foreach (Vector3 v in fist) box.Encapsulate(v);
            data.fistCenter = box.center;

            Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            float best = float.NegativeInfinity;
            foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = skin.sharedMesh;
                int handIndex = System.Array.IndexOf(skin.bones, hand);
                if (handIndex < 0) continue;
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                Matrix4x4[] poses = mesh.bindposes;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (weights[i].boneIndex0 != handIndex) continue;
                    // Dans le repère de l'os de la main (Y Bot : +Y vers les doigts, +Z côté paume).
                    Vector3 p = poses[handIndex].MultiplyPoint3x4(vertices[i]);
                    if (Mathf.Abs(p.x) > 0.02f || p.y < 0.05f || p.y > 0.08f || p.z <= best) continue;
                    best = p.z;
                    BoneWeight w = weights[i];
                    int[] index = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                    data.palm = vertices[i];
                    data.palmWeights = new[] { w.weight0, w.weight1, w.weight2, w.weight3 };
                    data.palmBones = new string[4];
                    data.palmBindPoses = new Matrix4x4[4];
                    for (int k = 0; k < 4; k++)
                    {
                        data.palmBones[k] = Path(skin.bones[index[k]], model);
                        data.palmBindPoses[k] = poses[index[k]];
                    }
                }
            }
            System.IO.File.WriteAllText(SurfacePath, JsonUtility.ToJson(data));
            surfaces = null;
        }

        private static string Path(Transform t, Transform root)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }

    private static SurfaceData Data()
    {
        if (surfaces == null && System.IO.File.Exists(SurfacePath))
            surfaces = JsonUtility.FromJson<SurfaceData>(System.IO.File.ReadAllText(SurfacePath));
        Assert.IsNotNull(surfaces, "Le gant et la main n'ont pas été relevés avant le Play Mode (Surfaces).");
        Assert.Greater(surfaces.fist.Length, 100, "Le poing du gant n'a pas été relevé.");
        Assert.AreEqual(4, surfaces.palmBones != null ? surfaces.palmBones.Length : 0, "Le milieu de la paume n'a pas été trouvé sur la peau du robot.");
        return surfaces;
    }

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
        if (cameraGo != null) Object.DestroyImmediate(cameraGo);
    }

    private static GameObject Prefab => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TestCharacter.PlayerPrefabPath);
    private static IReadOnlyList<WeaponData> Loadout() => Prefab.GetComponentInChildren<WeaponController>(true).Loadout;
    // Les valeurs du préfab priment sur les défauts du code.
    private static WeaponGrip.Settings Settings() => Prefab.GetComponent<PlayerAnimator>().HandGrip;

    /// <summary>Le corps (gant porté, comme vu des autres) et les bras de la vue subjective (gant ôté).</summary>
    private void Setup()
    {
        instance = TestCharacter.InstantiateModel(out body);
        cameraGo = new GameObject("CameraTest");
        FirstPersonView.BuildView(instance, cameraGo.transform, LayerMask.NameToLayer(FirstPersonView.LayerName), out arms, out _);
        HeldWeapons.WearTaserGlove(arms, false);
        bodyGrip = new WeaponGrip(body);
        armsGrip = new WeaponGrip(arms);
    }

    /// <summary>Les mêmes armes en main du corps et des bras, celle de `slot` visible.</summary>
    private void Hold(IReadOnlyList<WeaponData> loadout, WeaponLooks looks, WeaponAttachments attachments, int slot,
                      out WeaponModel bodyModel, out WeaponModel armsModel)
    {
        Transform bodyHand = body.GetBoneTransform(HumanBodyBones.RightHand);
        Transform armsHand = arms.GetBoneTransform(HumanBodyBones.RightHand);
        HeldWeapons.Build(bodyHand, loadout, looks, attachments);
        HeldWeapons.Build(armsHand, loadout, looks, attachments);
        bodyModel = HeldWeapons.Show(bodyHand, loadout, loadout[slot]);
        armsModel = HeldWeapons.Show(armsHand, loadout, loadout[slot]);
    }

    /// <summary>La visée debout, comme PlayerAnimator la pose sur le corps et la recopie sur les bras.</summary>
    private void Aim(bool pistol, bool reloading = false)
    {
        body.SetFloat("SpeedMult", 1f);
        body.SetFloat("StanceF", 0f);
        body.SetBool("Aiming", true);
        body.SetFloat("Pistolet", pistol ? 1f : 0f);
        body.SetBool("Reloading", reloading);
        body.SetFloat("ReloadSpeed", 1f);
        body.SetLayerWeight(1, 1f);
        PlayerAnimator.MirrorWeaponGestures(body, arms, 0f);
    }

    /// <summary>Une image : chaque copie remet l'arme au point de prise, puis pose ses mains.</summary>
    private void Grip(WeaponModel bodyModel, WeaponModel armsModel, AttachmentCatalog catalog, bool holding, WeaponGrip.Settings settings)
    {
        bodyGrip.RestoreWeapon();
        armsGrip.RestoreWeapon();
        bodyGrip.Apply(bodyModel, catalog, holding, 1f, settings, Time.deltaTime);
        armsGrip.Apply(armsModel, catalog, holding, 1f, settings, Time.deltaTime);
    }

    /// <summary>Joue `step` à chaque image pendant `seconds`. Une mesure se prend dans la même image qu'un
    /// `step` : à l'image suivante, l'Animator a réécrit la pose (d'où le dernier `step` explicite avant
    /// chaque mesure).</summary>
    private static IEnumerator Run(float seconds, System.Action step)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            yield return null;
            step();
        }
    }

    /// <summary>Le repère que la main gauche doit tenir : la poignée montée, sinon le garde-main.</summary>
    private static Transform LeftMarker(WeaponModel model)
    {
        WeaponAttachment grip = AttachmentMount.Mounted(model.transform, AttachmentCategory.Grip);
        return grip != null && grip.LeftHand != null ? grip.LeftHand : model.LeftHand;
    }

    /// <summary>Où regarde la paume de la prise (monde) : sous le garde-main, ou contre la poignée montée.</summary>
    private static Vector3 HoldNormal(WeaponModel model, AttachmentCatalog catalog, WeaponGrip.Settings settings)
    {
        WeaponGrip.HandHold hold = settings.handguard;
        WeaponAttachment grip = AttachmentMount.Mounted(model.transform, AttachmentCategory.Grip);
        if (grip != null && grip.LeftHand != null)
        {
            bool angled = catalog != null && catalog.angledGrip != null && catalog.angledGrip.GetComponent<WeaponAttachment>().Code == grip.Code;
            hold = angled ? settings.angledGrip : settings.verticalGrip;
        }
        return model.transform.rotation * hold.palm.normalized;
    }

    /// <summary>Le milieu de la paume gauche des bras, sur leur peau déformée par la pose de l'image.</summary>
    private Vector3 PalmCenter()
    {
        SurfaceData data = Data();
        Vector3 p = Vector3.zero;
        for (int k = 0; k < 4; k++)
        {
            if (data.palmWeights[k] <= 0f) continue;
            Transform bone = arms.transform.Find(data.palmBones[k]);
            Assert.IsNotNull(bone, $"Les bras de la vue subjective n'ont pas l'os « {data.palmBones[k]} ».");
            p += (bone.localToWorldMatrix * data.palmBindPoses[k]).MultiplyPoint3x4(data.palm) * data.palmWeights[k];
        }
        return p;
    }

    /// <summary>Le poing du gant du corps par rapport au repère, `normal` étant où regarde la paume de la
    /// prise : `off`, l'écart du milieu du poing à la droite qui passe par le repère dans cette direction
    /// (0 : le poing est juste dessous) ; `reach`, de combien le poing dépasse le repère dans cette
    /// direction (0 : il l'effleure ; positif, il traverse l'arme ; négatif, il ne l'atteint pas).</summary>
    private void Fist(Vector3 marker, Vector3 normal, out float off, out float reach)
    {
        SurfaceData data = Data();
        Transform glove = HeldWeapons.TaserGlove(body);
        reach = float.NegativeInfinity;
        foreach (Vector3 v in data.fist) reach = Mathf.Max(reach, Vector3.Dot(glove.TransformPoint(v) - marker, normal));
        off = Vector3.ProjectOnPlane(marker - glove.TransformPoint(data.fistCenter), normal).magnitude;
    }

    /// <summary>
    /// En visée, pour les douze modèles (WeaponData.ModelPrefabFor) : le milieu de la paume des bras est
    /// sur le repère « MainGauche », le poing du gant du corps juste dessous, alors que le clip seul les
    /// en laisse loin ; un pistolet n'a pas de repère et sa pose n'est pas touchée ; sur une crosse
    /// droite, la main droite tient le poignet et l'arme ne bouge pas, puis revient à son point de prise.
    /// </summary>
    [UnityTest]
    public IEnumerator EnVisee_LesMainsTiennentLArme_DesDouzeModeles()
    {
        Setup();
        IReadOnlyList<WeaponData> loadout = Loadout();
        WeaponGrip.Settings settings = Settings();
        int models = 0, gripped = 0, stocks = 0, missedByClip = 0;
        var report = new System.Text.StringBuilder();

        for (int slot = 0; slot < loadout.Count; slot++)
        {
            for (int m = 0; m < loadout[slot].ModelCount; m++)
            {
                Hold(loadout, WeaponLooks.Default.With(slot, m, WeaponFinish.Origine), WeaponAttachments.None, slot,
                     out WeaponModel bodyModel, out WeaponModel armsModel);
                yield return null;   // Destroy est différé : l'ancien modèle part à la fin de l'image
                Assert.IsNotNull(bodyModel, $"{loadout[slot].weaponName}, modèle {m} : rien en main.");
                bool pistol = bodyModel.PistolHold;
                AttachmentCatalog catalog = loadout[slot].attachmentCatalog;
                Aim(pistol);
                yield return Run(0.6f, () => Grip(bodyModel, armsModel, catalog, true, settings));
                models++;

                // Une dernière image, mesurée avant et après la prise.
                yield return null;
                bodyGrip.RestoreWeapon();
                armsGrip.RestoreWeapon();
                Transform bodyMarker = LeftMarker(bodyModel), armsMarker = LeftMarker(armsModel);
                Transform bodyLeft = body.GetBoneTransform(HumanBodyBones.LeftHand);
                Vector3 leftBefore = bodyLeft.position;
                float palmClip = 0f, offClip = 0f, reachClip = 0f;
                if (bodyMarker != null)
                {
                    palmClip = Vector3.Distance(PalmCenter(), armsMarker.position);
                    Fist(bodyMarker.position, HoldNormal(bodyModel, catalog, settings), out offClip, out reachClip);
                }
                Vector3 weaponPosition = bodyModel.transform.position;
                Quaternion weaponRotation = bodyModel.transform.rotation;

                bodyGrip.Apply(bodyModel, catalog, true, 1f, settings, Time.deltaTime);
                armsGrip.Apply(armsModel, catalog, true, 1f, settings, Time.deltaTime);

                if (bodyMarker == null)
                {
                    Assert.IsTrue(pistol, $"{bodyModel.Designation} : une arme d'épaule sans repère « MainGauche ».");
                    Assert.Less(Vector3.Distance(leftBefore, bodyLeft.position), 0.001f,
                        $"{bodyModel.Designation} : la pose de pistolet tient l'arme à deux mains, la main gauche n'a rien à faire.");
                    report.AppendLine($"{bodyModel.Designation} : pistolet, main gauche laissée au clip");
                }
                else
                {
                    float palm = Vector3.Distance(PalmCenter(), armsMarker.position);
                    Fist(bodyMarker.position, HoldNormal(bodyModel, catalog, settings), out float off, out float reach);
                    Transform armsHand = arms.GetBoneTransform(HumanBodyBones.LeftHand);
                    Vector3 palmInHand = Quaternion.Inverse(armsHand.rotation) * (PalmCenter() - armsHand.position);
                    Transform shoulder = arms.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    float armLength = Vector3.Distance(shoulder.position, arms.GetBoneTransform(HumanBodyBones.LeftLowerArm).position)
                                      + Vector3.Distance(arms.GetBoneTransform(HumanBodyBones.LeftLowerArm).position, armsHand.position);
                    string line = $"{bodyModel.Designation} : paume à {palm * 100f:F1} cm du repère (clip seul {palmClip * 100f:F1}) ; " +
                                  $"poing décalé de {off * 100f:F1} cm, dépasse de {reach * 100f:F1} cm (clip seul {offClip * 100f:F1} et {reachClip * 100f:F1}) ; " +
                                  $"paume dans la main {palmInHand.x:F3} {palmInHand.y:F3} {palmInHand.z:F3}, épaule-poignet {Vector3.Distance(shoulder.position, armsHand.position):F3} sur {armLength:F3}";
                    report.AppendLine(line);
                    Debug.Log("[PRISE] " + line);
                    Assert.Less(palm, Touch, $"{bodyModel.Designation} : en vue subjective, le milieu de la paume est à {palm * 100f:F1} cm du garde-main.");
                    Assert.Less(off, Touch, $"{bodyModel.Designation} : vu des autres, le poing du gant est à {off * 100f:F1} cm du dessous du garde-main.");
                    Assert.Less(Mathf.Abs(reach), Touch, $"{bodyModel.Designation} : vu des autres, le poing du gant dépasse le garde-main de {reach * 100f:F1} cm.");
                    if (palmClip > Touch && (offClip > Touch || Mathf.Abs(reachClip) > Touch)) missedByClip++;
                    gripped++;
                }

                if (bodyModel.RightHand != null)
                {
                    Transform anchor = body.GetBoneTransform(HumanBodyBones.RightHand).Find(HeldWeapons.GripAnchorName);
                    float onStock = Vector3.Distance(anchor.position, bodyModel.RightHand.position);
                    Assert.Less(onStock, 0.01f, $"{bodyModel.Designation} : la main droite est à {onStock * 100f:F1} cm du poignet de la crosse.");
                    Assert.Less(Vector3.Distance(weaponPosition, bodyModel.transform.position), 0.001f,
                        $"{bodyModel.Designation} : la main droite a emporté l'arme.");
                    Assert.Less(Quaternion.Angle(weaponRotation, bodyModel.transform.rotation), 0.1f,
                        $"{bodyModel.Designation} : la main droite a tourné l'arme.");
                    bodyGrip.RestoreWeapon();
                    Assert.Less(Vector3.Distance(anchor.localPosition, bodyModel.transform.localPosition), 1e-5f,
                        $"{bodyModel.Designation} : l'arme n'est pas revenue à son point de prise.");
                    stocks++;
                }
            }
        }
        Debug.Log("[PRISE] " + report);
        Assert.AreEqual(12, models, "Il faut les douze modèles : quatre classes, trois modèles.");
        Assert.AreEqual(9, gripped, "Neuf armes d'épaule ont un garde-main ; les trois pistolets n'en ont pas.");
        Assert.AreEqual(2, stocks, "Le FP-30 et le FAP-30 ont une crosse droite.");
        Assert.AreEqual(9, missedByClip, "Garde anti-vacuité : sans la prise, le clip devrait manquer le garde-main de chaque arme d'épaule.");
    }

    /// <summary>
    /// La main se pose après la direction du buste : le buste tourné et incliné vers le haut ou le bas
    /// (comme PlayerAnimator.ApplyAimDirection le fait en visée), le poing tient toujours le garde-main.
    /// </summary>
    [UnityTest]
    public IEnumerator BusteTourne_LaMainGaucheTientToujoursLeGardeMain()
    {
        Setup();
        IReadOnlyList<WeaponData> loadout = Loadout();
        WeaponGrip.Settings settings = Settings();
        Hold(loadout, WeaponLooks.Default, WeaponAttachments.None, 0, out WeaponModel bodyModel, out WeaponModel armsModel);
        yield return null;
        AttachmentCatalog catalog = loadout[0].attachmentCatalog;
        Aim(false);
        Transform[] spine = PlayerAnimator.SpineChain(body);
        foreach (float pitch in new[] { 35f, -35f })
        {
            System.Action step = () =>
            {
                bodyGrip.RestoreWeapon();
                PlayerAnimator.RotateSpine(spine, body.transform.right, 25f, pitch);
                bodyGrip.Apply(bodyModel, catalog, true, 1f, settings, Time.deltaTime);
            };
            yield return Run(0.4f, step);
            yield return null;
            step();
            Fist(bodyModel.LeftHand.position, HoldNormal(bodyModel, catalog, settings), out float off, out float reach);
            Debug.Log($"[PRISE] buste tourné de 25°, regard à {pitch:F0}° : poing décalé de {off * 100f:F1} cm, dépasse de {reach * 100f:F1} cm");
            Assert.Less(off, Touch, $"Le buste incliné de {pitch:F0}°, le poing est à {off * 100f:F1} cm du dessous du garde-main.");
            Assert.Less(Mathf.Abs(reach), Touch, $"Le buste incliné de {pitch:F0}°, le poing dépasse le garde-main de {reach * 100f:F1} cm.");
        }
    }

    /// <summary>
    /// Au rechargement, la main gauche lâche le garde-main et suit le clip (elle va au chargeur), puis le
    /// reprend : pendant le geste, la prise ne touche plus au bras.
    /// </summary>
    [UnityTest]
    public IEnumerator AuRechargement_LaMainGaucheSuitLeClip_PuisReprendLArme()
    {
        Setup();
        IReadOnlyList<WeaponData> loadout = Loadout();
        WeaponGrip.Settings settings = Settings();
        Hold(loadout, WeaponLooks.Default, WeaponAttachments.None, 0, out WeaponModel bodyModel, out WeaponModel armsModel);
        yield return null;
        AttachmentCatalog catalog = loadout[0].attachmentCatalog;
        Aim(false);
        yield return Run(0.6f, () => Grip(bodyModel, armsModel, catalog, true, settings));
        yield return null;
        Grip(bodyModel, armsModel, catalog, true, settings);
        Assert.Less(Vector3.Distance(PalmCenter(), armsModel.LeftHand.position), Touch, "Garde anti-vacuité : avant le rechargement, la paume tient l'arme.");

        Aim(false, reloading: true);
        Transform left = body.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform armsLeft = arms.GetBoneTransform(HumanBodyBones.LeftHand);
        float moved = 0f, farthest = 0f;
        int sampled = 0;
        for (float t = 0f; t < 1.6f; t += Time.deltaTime)
        {
            yield return null;
            bodyGrip.RestoreWeapon();
            armsGrip.RestoreWeapon();
            Vector3 before = left.position, armsBefore = armsLeft.position;
            bodyGrip.Apply(bodyModel, catalog, false, 1f, settings, Time.deltaTime);
            armsGrip.Apply(armsModel, catalog, false, 1f, settings, Time.deltaTime);
            if (t < settings.blendTime + 0.05f) continue;   // le temps de lâcher l'arme
            moved = Mathf.Max(moved, Vector3.Distance(before, left.position), Vector3.Distance(armsBefore, armsLeft.position));
            farthest = Mathf.Max(farthest, Vector3.Distance(PalmCenter(), armsModel.LeftHand.position));
            sampled++;
        }
        Debug.Log($"[PRISE] rechargement : main déplacée de {moved * 1000f:F2} mm au plus par la prise, paume jusqu'à {farthest * 100f:F0} cm du garde-main ({sampled} images)");
        Assert.Greater(sampled, 10);
        Assert.Less(moved, 0.001f, "Pendant le rechargement, la prise tire encore la main gauche vers le garde-main.");
        Assert.Greater(farthest, 0.10f, "Garde anti-vacuité : le clip de rechargement devrait emmener la main loin du garde-main.");

        Aim(false);
        yield return Run(0.8f, () => Grip(bodyModel, armsModel, catalog, true, settings));
        yield return null;
        Grip(bodyModel, armsModel, catalog, true, settings);
        Assert.Less(Vector3.Distance(PalmCenter(), armsModel.LeftHand.position), Touch, "Le rechargement fini, la paume ne reprend pas l'arme.");
        Fist(bodyModel.LeftHand.position, HoldNormal(bodyModel, catalog, settings), out float off, out float reach);
        Assert.Less(off, Touch, "Le rechargement fini, le poing du gant ne reprend pas l'arme.");
        Assert.Less(Mathf.Abs(reach), Touch, "Le rechargement fini, le poing du gant ne reprend pas l'arme.");
    }

    /// <summary>Joue une action dans le LateUpdate, après l'Animator, comme FirstPersonView : ce qui est posé
    /// là est ce que les maillages montrent à la fin de l'image.</summary>
    private class LateStep : MonoBehaviour
    {
        public System.Action step;
        private void LateUpdate() => step?.Invoke();
    }

    /// <summary>
    /// En vue subjective, les bras épaulés ne cachent pas l'écran (2026-10-17) : pour les douze modèles, sans
    /// accessoire et avec ceux du rapport (viseur PR-A, poignée inclinée, laser, grand chargeur au fusil
    /// d'assaut), les bras placés comme FirstPersonView les place (mire dans l'axe, à la distance de l'œil)
    /// couvrent moins de 5 % du tiers droit de l'écran, hors du tiers du bas où se tient l'arme (les deux
    /// mains d'un pistolet y sont). Au FAP-30, la main droite tenait le poignet de sa crosse droite, 7 cm
    /// devant l'œil et sous lui : elle et son avant-bras en couvraient 43 %. À la hanche, où elle se voit,
    /// elle le tient toujours. On mesure ce qui se voit : les bras seuls, rendus par une caméra au champ des
    /// leurs, sur un fond transparent.
    /// </summary>
    [UnityTest]
    public IEnumerator EnVueSubjective_LesBrasEpaulesNeCachentPasLEcran()
    {
        Setup();
        IReadOnlyList<WeaponData> loadout = Loadout();
        WeaponGrip.Settings settings = Settings();
        // Les réglages de la vue subjective, ceux du préfab.
        var view = new UnityEditor.SerializedObject(Prefab.GetComponent<FirstPersonView>());
        Vector3 aimSight = view.FindProperty("aimSightPosition").vector3Value;
        Vector3 hipOffset = view.FindProperty("hipOffset").vector3Value;
        Vector3 pistolHipOffset = view.FindProperty("pistolHipOffset").vector3Value;
        float fieldOfView = view.FindProperty("armsFieldOfView").floatValue;

        const int width = 480, height = 270;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        target.Create();
        var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var lens = new GameObject("CameraDesBras");
        lens.transform.SetParent(cameraGo.transform, false);
        Camera camera = lens.AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.cullingMask = 1 << LayerMask.NameToLayer(FirstPersonView.LayerName);
        camera.fieldOfView = fieldOfView;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 5f;
        camera.aspect = (float)width / height;
        camera.allowHDR = false;   // sans HDR, le tampon d'URP garde la couche alpha
        camera.allowMSAA = false;

        WeaponModel armsModel = null;
        AttachmentCatalog catalog = null;
        float aimBlend = 1f;
        // Une image de FirstPersonView.LateUpdate : l'arme remise au point de prise, la copie placée sur sa
        // mire (mesurée sans les mains), puis les mains.
        lens.AddComponent<LateStep>().step = () =>
        {
            if (armsModel == null) return;
            armsGrip.RestoreWeapon();
            Transform root = arms.transform;
            Transform sight = AttachmentMount.SightOf(armsModel, out float eye);
            Vector3 pivot = root.InverseTransformPoint(sight.position);
            Quaternion align = FirstPersonView.LevelSight(root.InverseTransformDirection(sight.forward), root.InverseTransformDirection(sight.up));
            Vector3 offset = (armsModel.PistolHold ? pistolHipOffset : hipOffset) * (1f - Mathf.SmoothStep(0f, 1f, aimBlend));
            FirstPersonView.Place(root, align, pivot, new Vector3(aimSight.x, aimSight.y, eye) + offset);
            armsGrip.Apply(armsModel, catalog, true, 1f, settings, Time.deltaTime, FirstPersonView.StraightStockHold(aimBlend));
        };

        // Rend les bras seuls et rend la part de l'écran, du tiers droit, et du tiers droit hors du tiers du bas,
        // qu'ils couvrent.
        void Measure(out float screen, out float rightThird, out float rightAbove)
        {
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = target };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            pixels.Apply();
            RenderTexture.active = previous;
            Color32[] colors = pixels.GetPixels32();
            int covered = 0, right = 0, above = 0, third = width * 2 / 3, bottom = height / 3;
            for (int y = 0; y < height; y++)   // la première ligne est celle du bas
                for (int x = 0; x < width; x++)
                {
                    if (colors[y * width + x].a < 128) continue;
                    covered++;
                    if (x < third) continue;
                    right++;
                    if (y >= bottom) above++;
                }
            screen = (float)covered / (width * height);
            rightThird = (float)right / ((width - third) * height);
            rightAbove = (float)above / ((width - third) * (height - bottom));
        }

        var report = new System.Text.StringBuilder();
        int measured = 0, stocksAtHip = 0;
        float worst = 0f;
        string worstName = "";
        try
        {
            foreach (WeaponAttachments attachments in new[] { WeaponAttachments.None, new WeaponAttachments(0x486A60E1u) })
            {
                for (int slot = 0; slot < loadout.Count; slot++)
                {
                    for (int m = 0; m < loadout[slot].ModelCount; m++)
                    {
                        Hold(loadout, WeaponLooks.Default.With(slot, m, WeaponFinish.Origine), attachments, slot, out _, out WeaponModel shown);
                        yield return null;   // Destroy est différé : l'ancien modèle part à la fin de l'image
                        armsModel = shown;
                        catalog = loadout[slot].attachmentCatalog;
                        Aim(armsModel.PistolHold);

                        aimBlend = 1f;
                        yield return new WaitForSeconds(0.6f);
                        yield return new WaitForEndOfFrame();
                        Measure(out float screen, out float rightThird, out float rightAbove);
                        measured++;
                        string name = $"{armsModel.Designation} ({(attachments.Packed == 0 ? "sans accessoire" : "accessoires du rapport")})";
                        report.AppendLine($"{name} : en visée, bras sur {screen * 100f:F1} % de l'écran, {rightThird * 100f:F1} % du tiers droit, " +
                                          $"{rightAbove * 100f:F1} % hors du bas");
                        Assert.Greater(screen, 0.03f, $"Garde anti-vacuité : {name}, l'arme épaulée devrait se voir.");
                        if (rightAbove > worst) { worst = rightAbove; worstName = name; }

                        if (armsModel.RightHand != null)
                        {
                            // À la hanche, la main droite tient le poignet de la crosse.
                            aimBlend = 0f;
                            yield return new WaitForSeconds(0.1f);
                            yield return new WaitForEndOfFrame();
                            Transform anchor = arms.GetBoneTransform(HumanBodyBones.RightHand).Find(HeldWeapons.GripAnchorName);
                            float onStock = Vector3.Distance(anchor.position, armsModel.RightHand.position);
                            report.AppendLine($"{name} : à la hanche, main droite à {onStock * 100f:F1} cm du poignet de la crosse");
                            Assert.Less(onStock, 0.01f, $"{name} : à la hanche, la main droite des bras est à {onStock * 100f:F1} cm du poignet de la crosse.");
                            stocksAtHip++;
                        }
                    }
                }
            }
        }
        finally
        {
            armsModel = null;
            target.Release();
        }
        Debug.Log("[VUE-SUBJECTIVE] " + report);
        Assert.AreEqual(24, measured, "Il faut les douze modèles, sans accessoire et avec ceux du rapport.");
        Assert.AreEqual(4, stocksAtHip, "Le FP-30 et le FAP-30 ont une crosse droite.");
        Assert.Less(worst, 0.05f, $"{worstName} : en visée, les bras couvrent {worst * 100f:F1} % du tiers droit de l'écran, hors du bas.");
    }

    /// <summary>Une poignée montée (2026-10-16) prend la main gauche : son repère, pas le garde-main.</summary>
    [UnityTest]
    public IEnumerator UnePoigneeMontee_PrendLaMainGauche()
    {
        Setup();
        IReadOnlyList<WeaponData> loadout = Loadout();
        WeaponGrip.Settings settings = Settings();
        AttachmentCatalog catalog = loadout[0].attachmentCatalog;
        foreach (GripKind kind in new[] { GripKind.Vertical, GripKind.Angled })
        {
            Hold(loadout, WeaponLooks.Default, WeaponAttachments.None.With(0, 0, kind, false, false), 0,
                 out WeaponModel bodyModel, out WeaponModel armsModel);
            yield return null;
            WeaponAttachment mounted = AttachmentMount.Mounted(bodyModel.transform, AttachmentCategory.Grip);
            Assert.IsNotNull(mounted, $"La poignée {kind} n'est pas montée sur le fusil d'assaut.");
            Assert.IsNotNull(mounted.LeftHand, $"La poignée {mounted.Code} n'a pas de repère « MainGauche ».");
            Aim(false);
            yield return Run(0.6f, () => Grip(bodyModel, armsModel, catalog, true, settings));
            yield return null;
            Grip(bodyModel, armsModel, catalog, true, settings);
            float palm = Vector3.Distance(PalmCenter(), LeftMarker(armsModel).position);
            Fist(LeftMarker(bodyModel).position, HoldNormal(bodyModel, catalog, settings), out float off, out float reach);
            Debug.Log($"[PRISE] {mounted.Code} : paume à {palm * 100f:F1} cm, poing décalé de {off * 100f:F1} cm, dépasse de {reach * 100f:F1} cm");
            Assert.Less(palm, Touch, $"{mounted.Code} : la paume est à {palm * 100f:F1} cm de la poignée.");
            Assert.Less(off, Touch, $"{mounted.Code} : le poing du gant est à {off * 100f:F1} cm de la poignée.");
            Assert.Less(Mathf.Abs(reach), Touch, $"{mounted.Code} : le poing du gant dépasse la poignée de {reach * 100f:F1} cm.");
        }
    }
}
#endif
