#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Ce que le Big D montre pendant son rechargement (BreakActionView, 2026-11-03), avec le vrai Animator, sur le corps
/// (vu des autres) et sur les bras de la vue subjective : le canon bascule comme le dit le geste, dans le bon sens ; la
/// chambre montre la douille vide sans sa balle, puis rien, puis la cartouche neuve ; la douille vide tombe une fois,
/// quand elle quitte la chambre ; la cartouche neuve est dans la main gauche de la saisie à la présentation, la suit,
/// puis rejoint la chambre sans à-coup ; le pouce droit prend les poses du geste ; à la fin, l'arme est intacte et
/// chargée. Interrompu, le rechargement rend
/// l'arme intacte ; vide, hors du rechargement, elle a son chien abattu et sa douille vide dans la chambre. L'arme en
/// main, au repos comme pendant le rechargement, la main droite prend la tenue du geste (ses doigts épousent la crosse).
///
/// Ce qui casse ici ne lève aucune erreur : un canon qui s'ouvre vers le bas, une cartouche qui flotte à côté de la main
/// ou saute en entrant, une douille qui ne tombe pas, ou une arme qui reste ouverte. On mesure à chaque image, juste
/// après que l'Animator a posé le corps.
/// </summary>
public class BreakActionViewTests
{
    private static GameObject Prefab => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TestCharacter.PlayerPrefabPath);

    private GameObject instance, cameraGo, stepper;
    private Animator body, arms;
    private WeaponData weapon;
    private BreakActionGesture gesture;
    private MagazineSwap.View bodyView, armsView;
    private BreakActionView bodyOpening, armsOpening;
    private readonly MagazineSwap.Drop drop = new MagazineSwap.Drop();
    private bool reloading, empty;
    private System.Action before, record;

    [TearDown]
    public void TearDown()
    {
        Time.captureDeltaTime = 0f;
        before = null;
        record = null;
        if (stepper != null) Object.DestroyImmediate(stepper);
        if (instance != null) Object.DestroyImmediate(instance);
        if (cameraGo != null) Object.DestroyImmediate(cameraGo);
        foreach (DroppedMagazine d in Object.FindObjectsByType<DroppedMagazine>())
            Object.DestroyImmediate(d.gameObject);
    }

    /// <summary>Le corps (gant porté, comme vu des autres) et les bras de la vue subjective (gant ôté), le Golden Gun
    /// en main, un pas de temps figé.</summary>
    private void Setup()
    {
        Time.captureDeltaTime = 1f / 60f;
        WeaponController controller = Prefab.GetComponentInChildren<WeaponController>(true);
        IReadOnlyList<WeaponData> all = controller.AllWeapons;
        weapon = controller.DataOf(WeaponController.GoldenGunSlot);
        Assert.IsNotNull(weapon, "Garde : le Golden Gun.");
        gesture = Prefab.GetComponent<MagazineSwap>().GestureFor(weapon) as BreakActionGesture;
        Assert.IsNotNull(gesture, "Le Golden Gun n'a pas le geste d'une arme qui s'ouvre.");
        instance = TestCharacter.InstantiateModel(out body);
        cameraGo = new GameObject("CameraTest");
        FirstPersonView.BuildView(instance, cameraGo.transform, LayerMask.NameToLayer(FirstPersonView.LayerName), out arms, out _);
        HeldWeapons.WearTaserGlove(arms, false);
        foreach (Animator a in new[] { body, arms })
        {
            HeldWeapons.Show(a.GetBoneTransform(HumanBodyBones.RightHand), all, weapon, WeaponLooks.Default, WeaponAttachments.None);
        }
        Assert.IsTrue(BodyModel != null && BodyModel.Designation == "BIG-D", "Garde : le corps tient le Big D.");
        Assert.IsTrue(ArmsModel != null && ArmsModel.Designation == "BIG-D", "Garde : les bras tiennent le Big D.");
        bodyView = new MagazineSwap.View(body, false);
        armsView = new MagazineSwap.View(arms, true);
        bodyOpening = new BreakActionView(body, false);
        armsOpening = new BreakActionView(arms, true);
        stepper = new GameObject("Pas");
        stepper.AddComponent<LateStep>().step = Step;
        empty = false;
    }

    private WeaponModel BodyModel => HeldWeapons.Visible(body.GetBoneTransform(HumanBodyBones.RightHand));
    private WeaponModel ArmsModel => HeldWeapons.Visible(arms.GetBoneTransform(HumanBodyBones.RightHand));

    /// <summary>Comme PlayerAnimator et MagazineSwap les posent : le geste du Big D, debout, tenu en pistolet, le
    /// rechargement étalé sur sa durée ; les bras reprennent les gestes de l'arme.</summary>
    private void Drive(bool reload)
    {
        reloading = reload;
        body.SetFloat("SpeedMult", 1f);
        body.SetInteger("Stance", 0);
        body.SetFloat("StanceF", 0f);
        body.SetBool("Aiming", false);
        body.SetBool("Reloading", reload);
        body.SetFloat("ReloadSpeed", (gesture.usefulEnd - gesture.usefulStart) / weapon.reloadDuration);
        body.SetLayerWeight(MagazineSwap.UpperBodyLayer, 1f);
        body.SetFloat("Pistolet", 1f);
        PlayerAnimator.MirrorWeaponGestures(body, arms, 0f);
        body.SetFloat(MagazineSwap.GestureParameter, gesture.animatorValue);
        arms.SetFloat(MagazineSwap.GestureParameter, gesture.animatorValue);
    }

    /// <summary>Ce que fait MagazineSwap après l'Animator : la part du geste montrée, puis l'arme qui s'ouvre.</summary>
    private void Step()
    {
        before?.Invoke();
        bodyView.Step(BodyModel, gesture, reloading, Vector3.zero, drop, Time.deltaTime);
        armsView.Step(ArmsModel, gesture, reloading, Vector3.zero, drop, Time.deltaTime);
        bodyOpening.Step(BodyModel, gesture, bodyView.Progress, empty, Vector3.zero, drop, Time.deltaTime);
        armsOpening.Step(ArmsModel, gesture, armsView.Progress, empty, Vector3.zero, drop, Time.deltaTime);
        record?.Invoke();
    }

    /// <summary>Joue une action dans le LateUpdate, après l'Animator, comme MagazineSwap.</summary>
    private class LateStep : MonoBehaviour
    {
        public System.Action step;
        private void LateUpdate() => step?.Invoke();
    }

    private static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
    }

    /// <summary>Une arme qui s'ouvre en main, sa vue a toujours de quoi faire (la tenue de la main droite) ; à la suivante,
    /// elle rend tout et se tait : MagazineSwap saute alors son tour.</summary>
    [UnityTest]
    public IEnumerator LeBigDEnMain_SaVueTourne_EtSeTaitALArmeSuivante()
    {
        Setup();
        Assert.IsTrue(new BreakActionView(body, false).AtRest, "Sans arme qui s'ouvre, une vue est au repos.");
        Drive(false);
        yield return Wait(0.2f);
        Assert.IsFalse(bodyOpening.AtRest, "Le Big D en main, sa vue doit tourner.");

        bodyOpening.Step(BodyModel, null, float.NaN, false, Vector3.zero, drop, Time.deltaTime);
        Assert.IsTrue(bodyOpening.AtRest, "L'arme suivante en main, la vue du Big D doit se taire.");
    }

    private Transform Part(WeaponModel model, string path) => model.transform.Find(path);

    private static bool Shown(Transform piece)
    {
        foreach (Renderer r in piece.GetComponentsInChildren<Renderer>(true))
            if (!r.enabled) return false;
        return true;
    }

    /// <summary>L'arme est-elle au repos, chargée : le canon fermé, le chien armé, le levier relevé, la cartouche
    /// entière dans la chambre ?</summary>
    private bool Intact(WeaponModel model, Vector3 cartridgeRest)
        => Quaternion.Angle(Part(model, gesture.barrelPath).localRotation, Quaternion.identity) < 0.01f
           && Quaternion.Angle(Part(model, gesture.hammerPath).localRotation, Quaternion.identity) < 0.01f
           && Quaternion.Angle(Part(model, gesture.latchPath).localRotation, Quaternion.identity) < 0.01f
           && Vector3.Distance(Part(model, gesture.cartridgePath).localPosition, cartridgeRest) < 1e-5f
           && Shown(Part(model, gesture.casePath)) && Shown(Part(model, gesture.bulletPath));

    [UnityTest]
    public IEnumerator AuRechargement_LeCanonSOuvre_LaDouilleTombe_LaCartouchePasseDeLaMainALaChambre()
    {
        Setup();
        Drive(false);
        yield return Wait(0.4f);
        Vector3 rest = Part(BodyModel, gesture.cartridgePath).localPosition;
        Assert.IsTrue(Intact(BodyModel, rest) && Intact(ArmsModel, rest), "Garde : avant le rechargement, l'arme est au repos, chargée.");

        var failures = new List<string>();
        var wantedThumb = new Quaternion[BreakActionGesture.ThumbBones];
        int frames = 0, dropsBody = 0, dropsArms = 0, thumbFrames = 0;
        float maxOpen = 0f, heldFrom = float.NaN, heldUntil = float.NaN, rigid = 0f, jump = float.NaN;
        bool haveRef = false, counted = false;
        Vector3 refPos = Vector3.zero;
        Quaternion refRot = Quaternion.identity;
        Transform bodyLeft = body.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform cartridge = Part(BodyModel, gesture.cartridgePath);
        record = () =>
        {
            foreach ((MagazineSwap.View view, WeaponModel model, string who) in new[] { (bodyView, BodyModel, "corps"), (armsView, ArmsModel, "bras") })
            {
                float g = view.Progress;
                if (float.IsNaN(g)) continue;
                BreakActionGesture.Pose p = gesture.PoseAt(g);
                // le canon, le chien et le levier : là où le geste les met, dans son sens
                Quaternion barrel = Part(model, gesture.barrelPath).localRotation;
                if (Quaternion.Angle(barrel, BreakActionGesture.Turn(-p.barrel)) > 0.5f)
                    failures.Add($"{who}, f={g:F3} : le canon n'est pas ouvert de {p.barrel:F1}° comme le dit le geste");
                if (Quaternion.Angle(Part(model, gesture.hammerPath).localRotation, BreakActionGesture.Turn(p.hammer)) > 0.5f)
                    failures.Add($"{who}, f={g:F3} : le chien n'est pas abattu de {p.hammer:F1}°");
                // la chambre : la douille vide sans sa balle, puis rien, puis la cartouche neuve
                bool wantCase = !float.IsNaN(p.caseSlide) || !float.IsNaN(p.cartridgeSlide), wantBullet = !float.IsNaN(p.cartridgeSlide);
                if (Shown(Part(model, gesture.casePath)) != wantCase || Shown(Part(model, gesture.bulletPath)) != wantBullet)
                    failures.Add($"{who}, f={g:F3} : la chambre montre {(Shown(Part(model, gesture.casePath)) ? "une douille" : "rien")}"
                                 + $"{(Shown(Part(model, gesture.bulletPath)) ? " et sa balle" : "")}");
            }
            // le pouce droit, qui abaisse le levier puis arme le chien : posé comme le dit le geste, l'Animator n'en
            // rejouant pas la torsion
            foreach ((MagazineSwap.View view, BreakActionView opening, string who) in new[] { (bodyView, bodyOpening, "corps"), (armsView, armsOpening, "bras") })
            {
                float g = view.Progress;
                if (float.IsNaN(g) || BreakActionGesture.ThumbWeight(g) < 1f || !gesture.ThumbAt(g, wantedThumb)) continue;
                thumbFrames++;
                for (int b = 0; b < BreakActionGesture.ThumbBones; b++)
                    if (Quaternion.Angle(opening.Thumb[b].localRotation, wantedThumb[b]) > 0.5f)
                        failures.Add($"{who}, f={g:F3} : la phalange {b + 1} du pouce droit n'est pas posée comme le dit le geste "
                                     + $"({Quaternion.Angle(opening.Thumb[b].localRotation, wantedThumb[b]):F1}°)");
            }
            float f = bodyView.Progress;
            if (float.IsNaN(f)) return;
            frames++;
            maxOpen = Mathf.Max(maxOpen, Quaternion.Angle(Quaternion.identity, Part(BodyModel, gesture.barrelPath).localRotation));
            // la douille vide quitte la chambre : une copie tombe, du corps et des bras
            if (!counted && f >= gesture.ejectStep.y)
            {
                counted = true;
                foreach (DroppedMagazine d in Object.FindObjectsByType<DroppedMagazine>())
                {
                    if (d.Drawn) dropsArms++;
                    else dropsBody++;
                }
            }
            // la cartouche neuve dans la main gauche
            GameObject held = bodyOpening.InHand;
            if (held != null)
            {
                if (float.IsNaN(heldFrom)) heldFrom = f;
                heldUntil = f;
                Quaternion inverse = Quaternion.Inverse(bodyLeft.rotation);
                Vector3 p = inverse * (held.transform.position - bodyLeft.position);
                Quaternion q = inverse * held.transform.rotation;
                if (f < gesture.approach)
                {
                    if (!haveRef) { refPos = p; refRot = q; haveRef = true; }
                    rigid = Mathf.Max(rigid, Vector3.Distance(p, refPos) + Quaternion.Angle(q, refRot) * 0.001f);
                }
                // l'écart à la cartouche de la chambre (cachée, devant elle) dans la même image : ce qui sautera
                jump = Vector3.Distance(held.transform.position, cartridge.position) + Quaternion.Angle(held.transform.rotation, cartridge.rotation) * 0.001f;
            }
        };
        Drive(true);
        yield return Wait(weapon.reloadDuration + 0.2f);
        record = null;
        Debug.Log($"[BIG-D] {frames} images ; ouvert au plus de {maxOpen:F1}° ; cartouche en main de f={heldFrom:F3} à f={heldUntil:F3}, tenue à {rigid * 1000f:F2} mm près, saut à la chambre {jump * 1000f:F2} mm ; douilles tombées : {dropsBody} corps, {dropsArms} bras");

        float frame = (1f / 60f) / weapon.reloadDuration + 0.002f;   // une image, en part du rechargement
        Assert.Greater(frames, 100, "Garde : le geste s'est joué.");
        Assert.Greater(thumbFrames, 100, "Garde : le pouce a été regardé pendant le geste, sur le corps et les bras.");
        Assert.AreEqual(gesture.barrelAngle, maxOpen, 1f, "Le canon ne s'ouvre pas jusqu'à l'horizontale.");
        Assert.AreEqual(1, dropsBody, "Le corps ne fait pas tomber sa douille vide (une seule).");
        Assert.AreEqual(1, dropsArms, "Les bras ne font pas tomber leur douille vide (une seule).");
        Assert.That(heldFrom, Is.InRange(gesture.grab, gesture.grab + 2f * frame), "La cartouche neuve n'arrive pas dans la main à son étape.");
        Assert.That(heldUntil, Is.InRange(gesture.seated - 2f * frame, gesture.seated), "La cartouche neuve ne quitte pas la main quand elle se présente.");
        Assert.Less(rigid, 0.001f, "La cartouche neuve ne suit pas la main gauche.");
        Assert.Less(jump, 0.002f, "La cartouche neuve saute en passant de la main à la chambre.");
        Assert.IsEmpty(failures, string.Join("\n", failures));
        Assert.IsTrue(Intact(BodyModel, rest) && Intact(ArmsModel, rest), "Après le rechargement, l'arme n'est pas au repos, chargée.");
        Assert.IsNull(bodyOpening.InHand, "Il reste une cartouche dans la main.");
    }

    [UnityTest]
    public IEnumerator UnRechargementInterrompu_RendAussitotLeBigDIntact()
    {
        Setup();
        Drive(false);
        yield return Wait(0.3f);
        Vector3 rest = Part(BodyModel, gesture.cartridgePath).localPosition;
        Drive(true);
        for (float t = 0f; t < 2f && bodyOpening.InHand == null; t += Time.deltaTime) yield return null;
        Assert.IsNotNull(bodyOpening.InHand, "Garde : la cartouche neuve est dans la main.");
        Assert.Greater(Quaternion.Angle(Quaternion.identity, Part(BodyModel, gesture.barrelPath).localRotation), 170f, "Garde : le canon est ouvert.");
        GameObject held = bodyOpening.InHand;

        Drive(false);   // un sprint, le taser, une autre arme : le rechargement s'arrête
        yield return null;
        Assert.IsTrue(Intact(BodyModel, rest) && Intact(ArmsModel, rest), "Le rechargement coupé, le Big D reste ouvert ou incomplet.");
        Assert.IsNull(bodyOpening.InHand, "Le rechargement coupé, la cartouche reste dans la main.");
        Assert.IsTrue(held == null, "La copie de la cartouche n'est pas détruite.");
    }

    [UnityTest]
    public IEnumerator Vide_HorsDuRechargement_LeChienEstAbattu()
    {
        Setup();
        Drive(false);
        yield return Wait(0.2f);
        Vector3 rest = Part(BodyModel, gesture.cartridgePath).localPosition;
        empty = true;   // le coup est parti, le rechargement n'a pas commencé
        yield return null;
        yield return null;
        foreach (WeaponModel model in new[] { BodyModel, ArmsModel })
        {
            Assert.AreEqual(0f, Quaternion.Angle(Part(model, gesture.hammerPath).localRotation, BreakActionGesture.Turn(gesture.hammerAngle)), 0.5f,
                            "Vide, le chien n'est pas abattu.");
            Assert.AreEqual(0f, Quaternion.Angle(Part(model, gesture.barrelPath).localRotation, Quaternion.identity), 0.01f, "Vide, le canon s'ouvre tout seul.");
            Assert.IsTrue(Shown(Part(model, gesture.casePath)), "Vide, la douille vide n'est plus dans la chambre.");
            Assert.IsFalse(Shown(Part(model, gesture.bulletPath)), "Vide, la chambre montre encore une balle.");
        }
        empty = false;   // rechargé
        yield return null;
        Assert.IsTrue(Intact(BodyModel, rest) && Intact(ArmsModel, rest), "Rechargée, l'arme n'est pas au repos, chargée.");
    }

    [UnityTest]
    public IEnumerator LeBigDEnMain_LaMainDroitePrendLaTenue_AuRepos_CommePendantLeRechargement()
    {
        Setup();
        Assert.IsTrue(gesture.HasHold, "Garde : le geste du Big D a la tenue de la main droite.");
        int bones = BreakActionGesture.HoldOrder.Length;
        var animated = new Quaternion[bones];
        var worst = new float[bones];
        float apart = 0f;
        int atRest = 0, reloadingFrames = 0;
        // la pose de l'Animator, avant que la vue ne pose la tenue : si elle était déjà la tenue, le test ne prouverait rien
        before = () =>
        {
            for (int b = 0; b < bones; b++) animated[b] = bodyOpening.Hand[b].localRotation;
        };
        record = () =>
        {
            for (int b = BreakActionGesture.ThumbBones; b < bones; b++)
                apart = Mathf.Max(apart, Quaternion.Angle(animated[b], gesture.rightHold[b]));
            foreach ((MagazineSwap.View view, BreakActionView opening) in new[] { (bodyView, bodyOpening), (armsView, armsOpening) })
            {
                float g = view.Progress;
                if (float.IsNaN(g)) atRest++;
                else reloadingFrames++;
                // pendant le geste, le pouce passe de la tenue à ses poses (le test du geste le regarde) : les autres doigts
                // gardent la tenue
                int first = !float.IsNaN(g) && BreakActionGesture.ThumbWeight(g) > 0f ? BreakActionGesture.ThumbBones : 0;
                for (int b = first; b < bones; b++)
                    worst[b] = Mathf.Max(worst[b], Quaternion.Angle(opening.Hand[b].localRotation, gesture.rightHold[b]));
            }
        };
        Drive(false);
        yield return Wait(0.4f);
        Drive(true);
        yield return Wait(weapon.reloadDuration + 0.2f);
        Drive(false);
        yield return Wait(0.3f);
        before = null;
        record = null;

        Assert.Greater(atRest, 40, "Garde : la main a été regardée l'arme au repos, sur le corps et les bras.");
        Assert.Greater(reloadingFrames, 200, "Garde : la main a été regardée pendant le geste, sur le corps et les bras.");
        Assert.Greater(apart, 10f, "Garde : les doigts de l'Animator sont déjà dans la tenue (rien n'aurait été reposé).");
        var failures = new List<string>();
        for (int b = 0; b < bones; b++)
            if (worst[b] > 0.1f)
                failures.Add($"{BreakActionGesture.HoldOrder[b]} n'est pas dans la tenue ({worst[b]:F1}° au pire)");
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }
}
#endif
