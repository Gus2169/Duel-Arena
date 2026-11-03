#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Le chargeur qui quitte l'arme (MagazineSwap, 2026-10-29), avec le vrai Animator, au rechargement de chaque arme qui a
/// son geste (le FA-07 pour ce qui ne dépend pas de l'arme) : sur le corps (vu des autres) et sur les bras de la vue
/// subjective, le chargeur vide tombe à son étape, le neuf apparaît dans la main gauche, la suit, rejoint sa place sans
/// à-coup et l'arme retrouve le sien ; interrompu, le rechargement rend l'arme intacte ; allongé, le corps garde son
/// chargeur (le geste commun).
///
/// Ce qui casse ici ne lève aucune erreur : un chargeur qui flotte à côté de la main, qui saute en entrant, qui ne
/// revient pas, ou une arme qui reste sans chargeur après un rechargement coupé. On mesure les copies et les
/// pièces de l'arme à chaque image, juste après que l'Animator a posé le corps.
/// </summary>
public class MagazineSwapTests
{
    /// <summary>Les armes qui ont leur geste à chargeur, sur le préfab joueur (MagazineSwap ; une arme qui s'ouvre, le
    /// Big D, a ses tests : BreakActionReloadTests).</summary>
    private static IEnumerable<string> Codes()
    {
        foreach (ReloadGesture g in Prefab.GetComponent<MagazineSwap>().Gestures)
            if (g != null && !(g is BreakActionGesture) && g.weapons.Length > 0) yield return g.weapons[0].weaponName;
    }

    /// <summary>Où la paume tient le chargeur neuf, contre son flanc gauche, sous le puits, dans le repère de l'arme :
    /// la « tenue » de la fiche du clip (rechargement_chargeur.py).</summary>
    private static Vector3 HeldPalmOf(string code)
    {
        string path = "Assets/_ProjectArt/Animations/Blender/Y Bot@Rechargement " + code + ".json";
        var text = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        Assert.IsNotNull(text, "La fiche du clip manque : " + path);
        float[] p = JsonUtility.FromJson<Sheet>(text.text).tenue.paume;
        return new Vector3(p[0], p[1], p[2]);
    }

    [System.Serializable]
    private class Sheet
    {
        public Landmark tenue;
    }

    [System.Serializable]
    private class Landmark
    {
        public float[] paume;
    }

    private float reloadDuration;   // celle de l'arme, sans accessoire

    private static readonly ChassisId[] AllChassis = { ChassisId.CT01, ChassisId.Prototype, ChassisId.OS02 };

    private GameObject instance, cameraGo, stepper;
    private Animator body, arms;
    private ReloadGesture gesture;
    private MagazineSwap.View bodyView, armsView;
    private readonly MagazineSwap.Drop drop = new MagazineSwap.Drop();
    private Vector3 armsShift;
    private float bodyStance;

    [TearDown]
    public void TearDown()
    {
        Time.captureDeltaTime = 0f;
        record = null;
        place = null;
        if (stepper != null) Object.DestroyImmediate(stepper);
        if (instance != null) Object.DestroyImmediate(instance);
        if (cameraGo != null) Object.DestroyImmediate(cameraGo);
        foreach (DroppedMagazine d in Object.FindObjectsByType<DroppedMagazine>())
            Object.DestroyImmediate(d.gameObject);
    }

    private static GameObject Prefab => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TestCharacter.PlayerPrefabPath);
    private static WeaponGrip.Settings Settings() => Prefab.GetComponent<PlayerAnimator>().HandGrip;

    /// <summary>Le corps (gant porté, comme vu des autres) et les bras de la vue subjective (gant ôté), l'arme `code` en
    /// main, un pas de temps figé (un geste rapide se mesure à un instant précis).</summary>
    private void Setup(WeaponAttachments attachments, string code = "FA-07")
    {
        Time.captureDeltaTime = 1f / 60f;
        IReadOnlyList<WeaponData> loadout = Prefab.GetComponentInChildren<WeaponController>(true).Loadout;
        WeaponData weapon = null;
        foreach (WeaponData w in loadout)
            if (w.weaponName == code) weapon = w;
        Assert.IsNotNull(weapon, "Garde : le " + code + " est dans l'arsenal.");
        gesture = Prefab.GetComponent<MagazineSwap>().GestureFor(weapon);
        Assert.IsNotNull(gesture, "Le " + code + " n'a pas de geste de rechargement à chargeur.");
        reloadDuration = weapon.reloadDuration;
        instance = TestCharacter.InstantiateModel(out body);
        cameraGo = new GameObject("CameraTest");
        FirstPersonView.BuildView(instance, cameraGo.transform, LayerMask.NameToLayer(FirstPersonView.LayerName), out arms, out _);
        HeldWeapons.WearTaserGlove(arms, false);
        foreach (Animator a in new[] { body, arms })
        {
            Transform hand = a.GetBoneTransform(HumanBodyBones.RightHand);
            HeldWeapons.Build(hand, loadout, WeaponLooks.Default, attachments);
            HeldWeapons.Show(hand, loadout, weapon);
        }
        bodyView = new MagazineSwap.View(body, false);
        armsView = new MagazineSwap.View(arms, true);
        Assert.IsTrue(bodyView.HasGestureParameter && armsView.HasGestureParameter, "L'Animator ne sait pas choisir le geste de rechargement.");
        stepper = new GameObject("Pas");
        stepper.AddComponent<LateStep>().step = Step;
        armsShift = Vector3.zero;
        bodyStance = 0f;
    }

    private WeaponModel BodyModel => HeldWeapons.Visible(body.GetBoneTransform(HumanBodyBones.RightHand));
    private WeaponModel ArmsModel => HeldWeapons.Visible(arms.GetBoneTransform(HumanBodyBones.RightHand));

    private bool reloading;

    /// <summary>Comme PlayerAnimator et MagazineSwap les posent : le geste de l'arme, debout (ou la posture du
    /// corps), le rechargement étalé sur sa durée ; les bras reprennent les gestes de l'arme, toujours debout.</summary>
    private void Drive(bool reload)
    {
        reloading = reload;
        body.SetFloat("SpeedMult", 1f);
        body.SetInteger("Stance", Mathf.RoundToInt(bodyStance));
        body.SetFloat("StanceF", bodyStance);
        body.SetBool("Aiming", false);
        body.SetBool("Reloading", reload);
        body.SetFloat("ReloadSpeed", (gesture.usefulEnd - gesture.usefulStart) / reloadDuration);
        body.SetLayerWeight(MagazineSwap.UpperBodyLayer, 1f);
        // la tenue de pistolet, comme PlayerAnimator la règle (les bras la reprennent) : sans elle, le PA-05 partait
        // de la tenue du fusil
        body.SetFloat("Pistolet", BodyModel != null && BodyModel.PistolHold ? 1f : 0f);
        PlayerAnimator.MirrorWeaponGestures(body, arms, 0f);
        body.SetFloat(MagazineSwap.GestureParameter, gesture.animatorValue);
        arms.SetFloat(MagazineSwap.GestureParameter, gesture.animatorValue);
    }

    private void Step()
    {
        place?.Invoke();
        bodyView.Step(BodyModel, gesture, reloading, Vector3.zero, drop, Time.deltaTime);
        armsView.Step(ArmsModel, gesture, reloading, armsShift, drop, Time.deltaTime);
        record?.Invoke();
    }

    /// <summary>Ce que fait FirstPersonView avant MagazineSwap (il passe avant lui) : placer la copie des bras.</summary>
    private System.Action place;

    private System.Action record;

    /// <summary>Joue une action dans le LateUpdate, après l'Animator, comme MagazineSwap.</summary>
    private class LateStep : MonoBehaviour
    {
        public System.Action step;
        private void LateUpdate() => step?.Invoke();
    }

    private static bool AllShown(Transform magazine)
    {
        foreach (Renderer r in magazine.GetComponentsInChildren<Renderer>(false))
            if (!r.enabled) return false;
        return true;
    }

    private static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
    }

    /// <summary>La pose d'une copie dans le repère d'un os (position, rotation), sans ses matrices (la main du corps
    /// a une échelle nulle).</summary>
    private static void Relative(Transform bone, Transform t, out Vector3 position, out Quaternion rotation)
    {
        Quaternion inverse = Quaternion.Inverse(bone.rotation);
        position = inverse * (t.position - bone.position);
        rotation = inverse * t.rotation;
    }

    [UnityTest]
    public IEnumerator AuRechargement_LeChargeurTombe_LeNeufArriveEnMain_PuisRentreSansAcoup([ValueSource(nameof(Codes))] string code)
    {
        Setup(WeaponAttachments.None, code);
        Drive(false);
        yield return Wait(0.4f);
        Transform bodyMagazine = MagazineSwap.MagazineOf(BodyModel), armsMagazine = MagazineSwap.MagazineOf(ArmsModel);
        Assert.IsNotNull(bodyMagazine);
        Assert.AreEqual(MagazineSwap.StandardMagazineName, bodyMagazine.name);
        Assert.IsTrue(AllShown(bodyMagazine) && AllShown(armsMagazine), "Garde : avant le rechargement, l'arme a son chargeur.");

        var failures = new List<string>();
        float hiddenAt = float.NaN, heldAt = float.NaN, backAt = float.NaN;
        int dropsBody = 0, dropsArms = 0;
        float rigid = 0f, jump = float.NaN;
        bool haveRef = false;
        Vector3 refPos = Vector3.zero;
        Quaternion refRot = Quaternion.identity;
        bool hadHeld = false;
        Transform bodyLeft = body.GetBoneTransform(HumanBodyBones.LeftHand);
        record = () =>
        {
            float f = MagazineSwap.ShownProgress(body, gesture);
            if (float.IsNaN(f)) return;
            if (float.IsNaN(hiddenAt) && bodyView.MagazineHidden)
            {
                hiddenAt = f;
                foreach (DroppedMagazine d in Object.FindObjectsByType<DroppedMagazine>())
                {
                    if (d.Drawn) dropsArms++;
                    else dropsBody++;
                }
            }
            GameObject held = bodyView.InHand;
            if (held != null)
            {
                if (float.IsNaN(heldAt)) heldAt = f;
                Relative(bodyLeft, held.transform, out Vector3 p, out Quaternion q);
                if (f < gesture.approach)
                {
                    if (!haveRef) { refPos = p; refRot = q; haveRef = true; }
                    rigid = Mathf.Max(rigid, Vector3.Distance(p, refPos) + Quaternion.Angle(q, refRot) * 0.001f);
                }
                // l'écart au chargeur de l'arme (caché, à sa place) dans la même image : ce qui sautera à l'insertion
                jump = Vector3.Distance(held.transform.position, bodyMagazine.position) + Quaternion.Angle(held.transform.rotation, bodyMagazine.rotation) * 0.001f;
                hadHeld = true;
            }
            else if (hadHeld && float.IsNaN(backAt))
            {
                backAt = f;
            }
            if (f > gesture.drop + 0.02f && f < gesture.seated - 0.01f && !armsView.MagazineHidden) failures.Add($"f={f:F2} : sur les bras, le chargeur n'a pas quitté l'arme");
            if (f > gesture.seated + 0.02f && (bodyView.MagazineHidden || !AllShown(bodyMagazine) || !AllShown(armsMagazine)))
                failures.Add($"f={f:F2} : le chargeur neuf entré, l'arme n'a pas retrouvé le sien");
        };
        Drive(true);
        yield return Wait(reloadDuration + 0.2f);
        record = null;
        Debug.Log($"[CHARGEUR] {code} : caché à f={hiddenAt:F3}, en main à f={heldAt:F3}, rentré à f={backAt:F3} ; tenu à {rigid * 1000f:F2} mm près ; saut à l'insertion {jump * 1000f:F1} mm ; chutes : {dropsBody} corps, {dropsArms} bras");

        float frame = (1f / 60f) / reloadDuration + 0.002f;   // une image, en part du rechargement
        Assert.That(hiddenAt, Is.InRange(gesture.drop, gesture.drop + 2f * frame), "Le chargeur vide ne quitte pas l'arme à son étape.");
        Assert.That(heldAt, Is.InRange(gesture.grab, gesture.grab + 2f * frame), "Le chargeur neuf n'apparaît pas dans la main à son étape.");
        Assert.That(backAt, Is.InRange(gesture.seated, gesture.seated + 2f * frame), "Le chargeur neuf n'entre pas à son étape.");
        Assert.AreEqual(1, dropsBody, "Le corps ne fait pas tomber son chargeur vide (un seul).");
        Assert.AreEqual(1, dropsArms, "Les bras ne font pas tomber leur chargeur vide (un seul).");
        Assert.Less(rigid, 0.001f, "Le chargeur neuf ne suit pas la main gauche.");
        Assert.Less(jump, 0.002f, "Le chargeur neuf saute en entrant dans le puits.");
        Assert.IsNull(bodyView.InHand, "Il reste un chargeur dans la main.");
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    [UnityTest]
    public IEnumerator LeChargeurVide_TombeHorsDeTout_PuisDisparait()
    {
        Setup(WeaponAttachments.None);
        Drive(false);
        yield return Wait(0.3f);
        Drive(true);
        DroppedMagazine onBody = null, onArms = null;
        for (float t = 0f; t < 0.6f && (onBody == null || onArms == null); t += Time.deltaTime)
        {
            yield return null;
            foreach (DroppedMagazine d in Object.FindObjectsByType<DroppedMagazine>())
            {
                if (d.Drawn) onArms = d;
                else onBody = d;
            }
        }
        Assert.IsNotNull(onBody, "Le corps ne fait pas tomber de chargeur.");
        Assert.IsNotNull(onArms, "Les bras ne font pas tomber de chargeur.");
        float startBody = onBody.transform.position.y, startArms = onArms.transform.position.y;

        // Le corps : un corps rigide sur le layer des débris, qui ne touche ni les joueurs ni leur surface touchable.
        int corpse = CorpseLayer.Index;
        Assert.AreEqual(corpse, onBody.gameObject.layer, "Le chargeur tombé n'est pas sur le layer des débris (Corpse).");
        Assert.IsNotNull(onBody.GetComponent<Rigidbody>(), "Le chargeur tombé du corps n'a pas de physique.");
        Assert.IsNotNull(onBody.GetComponent<Collider>());
        Assert.IsTrue(Physics.GetIgnoreLayerCollision(corpse, LayerMask.NameToLayer("Player")), "Le chargeur tombé heurterait les joueurs.");
        Assert.IsTrue(Physics.GetIgnoreLayerCollision(corpse, LayerMask.NameToLayer("Hitbox")), "Le chargeur tombé heurterait les surfaces touchables.");
        Assert.IsFalse(Physics.GetIgnoreLayerCollision(corpse, 0), "Garde : il tombe sur le décor.");
        // Les bras : il tombe sans physique, sur le layer des bras.
        Assert.AreEqual(LayerMask.NameToLayer(FirstPersonView.LayerName), onArms.gameObject.layer, "Le chargeur des bras n'est pas sur le layer des bras.");
        Assert.IsNull(onArms.GetComponent<Rigidbody>(), "Le chargeur des bras a une physique : posé au sol, il se dessinerait à travers les murs.");

        yield return Wait(0.3f);
        Assert.Less(onBody.transform.position.y, startBody - 0.2f, "Le chargeur vide du corps ne tombe pas.");
        Assert.IsTrue(onArms == null || onArms.transform.position.y < startArms - 0.2f, "Le chargeur vide des bras ne tombe pas.");
        yield return Wait(drop.armsLifetime + 0.1f);
        Assert.IsTrue(onArms == null, "Le chargeur des bras ne s'efface pas une fois sorti de l'écran.");
        Assert.IsTrue(onBody != null, "Le chargeur du corps disparaît trop tôt.");
    }

    [UnityTest]
    public IEnumerator UnRechargementInterrompu_RendAussitotLArmeIntacte()
    {
        Setup(WeaponAttachments.None);
        Drive(false);
        yield return Wait(0.3f);
        Drive(true);
        for (float t = 0f; t < 2f && bodyView.InHand == null; t += Time.deltaTime) yield return null;
        Assert.IsNotNull(bodyView.InHand, "Garde : le chargeur neuf est dans la main.");
        Assert.IsTrue(bodyView.MagazineHidden && armsView.MagazineHidden, "Garde : l'arme n'a plus son chargeur.");
        GameObject held = bodyView.InHand;

        Drive(false);   // un sprint, le taser, un changement d'arme : le rechargement s'arrête
        yield return null;
        Transform bodyMagazine = MagazineSwap.MagazineOf(BodyModel), armsMagazine = MagazineSwap.MagazineOf(ArmsModel);
        Assert.IsTrue(AllShown(bodyMagazine) && AllShown(armsMagazine), "Le rechargement coupé, l'arme reste sans chargeur.");
        Assert.IsFalse(bodyView.MagazineHidden || armsView.MagazineHidden);
        Assert.IsNull(bodyView.InHand, "Le rechargement coupé, le chargeur neuf reste dans la main.");
        Assert.IsNull(armsView.InHand);
        Assert.IsTrue(held == null, "La copie du chargeur neuf n'est pas détruite.");
    }

    /// <summary>
    /// La pièce d'armement (2026-10-29) : la poignée en T du FA-07, le levier du FA-30, la culasse du FP-12, la
    /// glissière du PA-05 reculent avec la main gauche qui les tire, puis reviennent. Sur les bras de la vue subjective
    /// (la main sans le gant, sa paume mesurable), dans le repère de l'arme : de la prise à la fin du recul, la paume
    /// reste à la même place sur la pièce, à 4 mm près, quand la pièce recule de toute sa course ; une fois revenue,
    /// elle est à sa place ; un rechargement coupé en plein armement la remet aussitôt.
    /// </summary>
    [UnityTest]
    public IEnumerator LaPieceDArmement_ReculeAvecLaMain_PuisRevient([ValueSource(nameof(Codes))] string code)
    {
        Setup(WeaponAttachments.None, code);
        WeaponGrip.Settings settings = Settings();
        Assert.Greater(gesture.armingParts.Length, 0, "Le geste du " + code + " ne fait rien reculer.");
        Transform part = ArmsModel.transform.Find(gesture.armingParts[0]);
        Assert.IsNotNull(part, "Garde : le modèle du " + code + " a sa pièce d'armement « " + gesture.armingParts[0] + " ».");
        Transform weapon = ArmsModel.transform, hand = arms.GetBoneTransform(HumanBodyBones.LeftHand);
        Vector3 rest = weapon.InverseTransformPoint(part.position);
        Vector3 palmPoint = settings.PalmPoint(ChassisId.CT01);
        Drive(false);
        yield return Wait(0.4f);

        float maxBack = 0f, worstSlip = 0f;
        bool haveGrip = false;
        Vector3 grip = Vector3.zero;   // la paume, vue de la pièce, quand la main la prend
        float atReturn = float.NaN;
        // la main tient la pièce jusqu'à la lâcher ; une culasse, elle la ramène aussi en avant
        float held = gesture.armingRelease > gesture.armingPulled ? gesture.armingRelease : gesture.armingReturned;
        record = () =>
        {
            float f = armsView.Progress;
            if (float.IsNaN(f)) return;
            Vector3 p = weapon.InverseTransformPoint(part.position);
            Vector3 palm = weapon.InverseTransformPoint(hand.TransformPoint(palmPoint)) - p;
            maxBack = Mathf.Max(maxBack, rest.z - p.z);
            if (f >= gesture.armingGrab && f <= held)
            {
                if (!haveGrip) { grip = palm; haveGrip = true; }
                worstSlip = Mathf.Max(worstSlip, Vector3.Distance(palm, grip));
            }
            if (f >= gesture.armingReturned + 0.02f && float.IsNaN(atReturn)) atReturn = Vector3.Distance(p, rest);
        };
        Drive(true);
        yield return Wait(reloadDuration + 0.2f);
        record = null;
        Debug.Log($"[ARMEMENT] {code} : « {gesture.armingParts[0]} » reculée de {maxBack * 100f:F1} cm (course {gesture.armingTravel * 100f:F1}), la paume glisse sur elle de {worstSlip * 1000f:F1} mm au plus, à {atReturn * 1000f:F2} mm de sa place une fois revenue");
        Assert.IsTrue(haveGrip, "Garde : la main a pris la pièce d'armement.");
        Assert.AreEqual(gesture.armingTravel, maxBack, 0.002f, "La pièce d'armement ne recule pas de sa course.");
        Assert.Less(worstSlip, 0.004f, "La pièce d'armement ne suit pas la main qui la tire.");
        Assert.Less(atReturn, 1e-4f, "La pièce d'armement ne revient pas à sa place.");
        Assert.IsEmpty(armsView.ArmingMoved, "Le rechargement fini, des pièces restent déplacées.");

        // coupé en plein armement : la pièce revient aussitôt
        Drive(false);
        yield return Wait(0.4f);
        Drive(true);
        for (float t = 0f; t < reloadDuration && !(armsView.Progress > (gesture.armingGrab + gesture.armingPulled) / 2f); t += Time.deltaTime)
            yield return null;
        Assert.Greater(rest.z - weapon.InverseTransformPoint(part.position).z, 0.005f, "Garde : la pièce a commencé à reculer.");
        Drive(false);
        yield return null;
        Assert.Less(Vector3.Distance(weapon.InverseTransformPoint(part.position), rest), 1e-4f, "Le rechargement coupé, la pièce d'armement reste reculée.");
    }

    /// <summary>
    /// La glissière du PA-05 emporte ce qui est monté dessus : le viseur (par son point d'accroche) et, sous un canon
    /// court, la glissière que porte le canon (celle de l'arme y est cachée). Tous reculent de la course de la main,
    /// puis reviennent à leur place.
    /// </summary>
    [UnityTest]
    public IEnumerator SurLePA05_LaGlissiereEmporteLeViseurEtCelleDuCanonCourt()
    {
        IReadOnlyList<WeaponData> loadout = Prefab.GetComponentInChildren<WeaponController>(true).Loadout;
        int slot = -1;
        for (int i = 0; i < loadout.Count; i++)
            if (loadout[i].weaponName == "PA-05") slot = i;
        Assert.GreaterOrEqual(slot, 0, "Garde : le PA-05 est dans l'arsenal.");
        int sight = 1;
        while (sight < 16 && !WeaponAttachments.Accepts(loadout[slot], AttachmentCategory.Sight, sight)) sight++;
        Assert.Less(sight, 16, "Garde : le PA-05 prend un viseur.");
        Assert.IsTrue(WeaponAttachments.Accepts(loadout[slot], AttachmentCategory.Barrel, (int)BarrelKind.Short), "Garde : le PA-05 prend le canon court.");
        Setup(WeaponAttachments.None.WithSight(slot, sight).WithBarrel(slot, BarrelKind.Short), "PA-05");
        Transform weapon = ArmsModel.transform;
        WeaponAttachment mountedSight = AttachmentMount.Mounted(weapon, AttachmentCategory.Sight);
        WeaponAttachment barrel = AttachmentMount.Mounted(weapon, AttachmentCategory.Barrel);
        Assert.IsNotNull(mountedSight, "Garde : le viseur est monté.");
        Assert.IsNotNull(barrel, "Garde : le canon court est monté.");
        Transform barrelSlide = barrel.transform.Find("Culasse");
        Assert.IsTrue(barrelSlide != null && barrelSlide.gameObject.activeInHierarchy, "Garde : le canon court porte sa glissière, et c'est elle qui se voit.");
        Transform[] parts = { mountedSight.transform, barrelSlide };
        var rest = new Vector3[parts.Length];
        var maxBack = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++) rest[i] = weapon.InverseTransformPoint(parts[i].position);
        Drive(false);
        yield return Wait(0.4f);
        record = () =>
        {
            for (int i = 0; i < parts.Length; i++)
                maxBack[i] = Mathf.Max(maxBack[i], rest[i].z - weapon.InverseTransformPoint(parts[i].position).z);
        };
        Drive(true);
        yield return Wait(reloadDuration + 0.2f);
        record = null;
        var failures = new List<string>();
        for (int i = 0; i < parts.Length; i++)
        {
            if (Mathf.Abs(maxBack[i] - gesture.armingTravel) > 0.002f)
                failures.Add($"« {parts[i].name} » recule de {maxBack[i] * 100f:F1} cm, pas de la course de la glissière ({gesture.armingTravel * 100f:F1} cm)");
            float gap = Vector3.Distance(weapon.InverseTransformPoint(parts[i].position), rest[i]);
            if (gap > 1e-4f) failures.Add($"« {parts[i].name} » reste à {gap * 1000f:F1} mm de sa place");
        }
        Assert.IsEmpty(failures, string.Join(" ; ", failures));
    }

    [UnityTest]
    public IEnumerator AvecUnGrandChargeur_CestLuiQuiTombe_EtLeChargeurDOrigineResteCache()
    {
        Setup(AttachmentCatalog.Only(AttachmentCategory.Magazine, (int)MagazineKind.Extended));
        Drive(false);
        yield return Wait(0.3f);
        WeaponModel model = BodyModel;
        WeaponAttachment extended = AttachmentMount.Mounted(model.transform, AttachmentCategory.Magazine);
        Assert.IsNotNull(extended, "Garde : le grand chargeur est monté.");
        Transform mounted = extended.transform, standard = model.transform.Find(MagazineSwap.StandardMagazineName);
        Assert.IsFalse(standard.gameObject.activeSelf, "Garde : le chargeur d'origine est caché sous le grand.");
        Drive(true);
        bool hidden = false;
        for (float t = 0f; t < reloadDuration + 0.2f; t += Time.deltaTime)
        {
            yield return null;
            hidden |= bodyView.MagazineHidden && !AllShown(mounted);
        }
        Assert.IsTrue(hidden, "Le grand chargeur ne quitte pas l'arme.");
        Assert.IsTrue(AllShown(mounted), "Le grand chargeur ne revient pas.");
        Assert.IsFalse(standard.gameObject.activeSelf, "Le chargeur d'origine réapparaît sous le grand.");
    }

    [UnityTest]
    public IEnumerator EnVueSubjective_LeChargeurNeufEstALaPaumeDeChaqueChassis([ValueSource(nameof(Codes))] string code)
    {
        Setup(WeaponAttachments.None, code);
        Vector3 heldPalm = HeldPalmOf(code);
        WeaponGrip.Settings settings = Settings();
        Transform armsLeft = arms.GetBoneTransform(HumanBodyBones.LeftHand);
        var failures = new List<string>();
        foreach (ChassisId chassis in AllChassis)
        {
            RobotChassis.Apply(arms.transform, chassis);
            armsShift = settings.PalmPoint(chassis) - gesture.designPalm;
            Drive(false);
            yield return Wait(0.4f);
            Drive(true);
            Transform magazine = MagazineSwap.MagazineOf(ArmsModel);
            float worst = 0f, jump = float.NaN;
            int seen = 0;
            record = () =>
            {
                float f = MagazineSwap.ShownProgress(arms, gesture);
                GameObject held = armsView.InHand;
                if (held == null || float.IsNaN(f)) return;
                // l'écart au chargeur de l'arme (caché, à sa place) dans la même image : ce qui sautera à l'insertion
                jump = Vector3.Distance(held.transform.position, magazine.position) + Quaternion.Angle(held.transform.rotation, magazine.rotation) * 0.001f;
                if (f >= gesture.approach) return;
                // la paume du châssis dans le repère du chargeur neuf (celui de l'arme, pour le chargeur d'origine)
                Vector3 palm = held.transform.InverseTransformPoint(armsLeft.TransformPoint(settings.PalmPoint(chassis)));
                worst = Mathf.Max(worst, Vector3.Distance(palm, heldPalm));
                seen++;
            };
            yield return Wait(reloadDuration * 0.5f);
            record = null;
            Drive(false);
            yield return Wait(0.3f);
            Debug.Log($"[CHARGEUR] {code}, {RobotChassis.NameOf(chassis)} : paume à {worst * 1000f:F1} mm de sa place sur le chargeur neuf ({seen} images), saut à l'insertion {jump * 1000f:F1} mm");
            if (seen < 5) failures.Add($"{RobotChassis.NameOf(chassis)} : le chargeur neuf n'a pas été vu dans la main");
            else if (worst > 0.002f) failures.Add($"{RobotChassis.NameOf(chassis)} : la paume est à {worst * 1000f:F0} mm de sa place sur le chargeur neuf");
            if (!(jump < 0.002f)) failures.Add($"{RobotChassis.NameOf(chassis)} : le chargeur neuf saute de {jump * 1000f:F0} mm en entrant dans le puits");
        }
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    /// <summary>
    /// Ce que le joueur voit de son rechargement (2026-10-29). Tenue à la hanche, l'arme gardait son puits sous le bord
    /// de l'écran : ni le chargeur vide qui tombe, ni le neuf qui entre ne se voyaient ; la copie montée par une
    /// translation, l'épaule et le bras gauches passaient devant l'œil (vus au stand). Les bras placés comme
    /// FirstPersonView les place (la tenue figée au début du rechargement, puis levée par LiftForReload au rythme du
    /// geste, avec les réglages du préfab), on regarde dans le champ de leur caméra, en 16:9 : le puits quand le
    /// chargeur vide en tombe et quand le neuf y entre, la paume et le chargeur neuf de son approche à son entrée, le
    /// haut du bras gauche jamais tout près de l'œil, l'épaule droite sur 5 % de l'écran au plus, et pendant
    /// l'armement la pièce (le milieu de son maillage : son origine est celle de l'arme) et la main qui la tire. Un
    /// pistolet se tient et se cadre à part (sa tenue de pistolet, pistolReloadView…) : levé comme un fusil, son
    /// rechargement se faisait tout entier hors du champ, et le test, qui le tenait comme un fusil, le voyait
    /// (2026-10-29). Avec et sans viseur : un viseur avance la mire et rapproche l'œil, toute la copie vient 11 à
    /// 18 cm plus près.
    /// </summary>
    [UnityTest]
    public IEnumerator EnVueSubjective_LeRechargementSeVoit_SansLeBrasDevantLOeil([ValueSource(nameof(Codes))] string code, [Values(false, true)] bool sighted)
    {
        IReadOnlyList<WeaponData> loadout = Prefab.GetComponentInChildren<WeaponController>(true).Loadout;
        int slot = -1;
        for (int i = 0; i < loadout.Count; i++)
            if (loadout[i].weaponName == code) slot = i;
        int sight = 0;
        if (sighted)
        {
            sight = 1;
            while (sight < 16 && !WeaponAttachments.Accepts(loadout[slot], AttachmentCategory.Sight, sight)) sight++;
            Assert.Less(sight, 16, "Garde : le " + code + " prend un viseur.");
        }
        Setup(WeaponAttachments.None.WithSight(slot, sight), code);
        WeaponGrip.Settings settings = Settings();
        armsShift = settings.PalmPoint(ChassisId.CT01) - gesture.designPalm;
        var view = new UnityEditor.SerializedObject(Prefab.GetComponent<FirstPersonView>());
        Vector3 aimSight = view.FindProperty("aimSightPosition").vector3Value;
        // un pistolet se tient et se lève à part (pistolHipOffset, pistolReloadView…) : tenu comme un fusil, il était
        // 37 cm plus loin de l'œil, et le test voyait un rechargement que le joueur ne voyait pas
        bool pistol = ArmsModel.PistolHold;
        Vector3 hipOffset = view.FindProperty(pistol ? "pistolHipOffset" : "hipOffset").vector3Value;
        float fieldOfView = view.FindProperty("armsFieldOfView").floatValue;
        float pitch = view.FindProperty(pistol ? "pistolReloadViewPitch" : "reloadViewPitch").floatValue;
        float yaw = pistol ? view.FindProperty("pistolReloadViewYaw").floatValue : 0f;
        Vector3 lift = view.FindProperty(pistol ? "pistolReloadViewOffset" : "reloadViewOffset").vector3Value;
        float frameTime = view.FindProperty(pistol ? "pistolReloadFrameTime" : "reloadFrameTime").floatValue;
        float tanV = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad), tanH = tanV * 16f / 9f;
        Transform cam = cameraGo.transform, root = arms.transform;

        // Une image de FirstPersonView.LateUpdate, à la hanche et à l'arrêt.
        Quaternion hold = Quaternion.identity;
        Vector3 holdPivot = Vector3.zero, holdIronPivot = Vector3.zero;
        float reloadFrame = 0f, armingWeight = 0f, armingPitch = 0f;
        place = () =>
        {
            Transform sight = AttachmentMount.SightOf(ArmsModel, out float eye);
            if (!arms.GetBool("Reloading"))
            {
                holdPivot = root.InverseTransformPoint(sight.position);
                holdIronPivot = root.InverseTransformPoint(ArmsModel.Sight.position);
                hold = FirstPersonView.LevelSight(root.InverseTransformDirection(sight.forward), root.InverseTransformDirection(sight.up));
            }
            FirstPersonView.Place(root, hold, holdPivot, new Vector3(aimSight.x, aimSight.y, eye) + hipOffset);
            reloadFrame = Mathf.MoveTowards(reloadFrame, float.IsNaN(armsView.Progress) ? 0f : 1f, Time.deltaTime / Mathf.Max(0.01f, frameTime));
            if (reloadFrame > 0f)
            {
                // le rechargement replace la copie comme sans viseur (sur la mire de l'arme), puis la lève
                float frame = Mathf.SmoothStep(0f, 1f, reloadFrame);
                root.localPosition += FirstPersonView.ReloadSightShift(hold, holdIronPivot, holdPivot, ArmsModel.AimEyeDistance, eye) * frame;
                FirstPersonView.LiftForReload(root, pitch, yaw, lift, frame);
            }
            // pendant l'armement, la levée de plus, décidée au début de sa montée d'après la main sur la pièce tirée
            float wanted = gesture.ArmingViewWeight(armsView.Progress);
            if (wanted > 0f && armingWeight <= 0f)
                armingPitch = gesture.ArmingViewPitch(cam.InverseTransformPoint(ArmsModel.transform.TransformPoint(gesture.ArmingPalmPulled)));
            armingWeight = wanted;
            if (armingWeight > 0f) FirstPersonView.LiftForReload(root, armingPitch, gesture.armingViewYaw, Vector3.zero, armingWeight);
        };
        // Où tombe un point à l'écran : (x, y) de -1 à 1 sur la largeur et la hauteur ; z, sa distance devant l'œil.
        Vector3 OnScreen(Vector3 world)
        {
            Vector3 c = cam.InverseTransformPoint(world);
            return c.z > 0.02f ? new Vector3(c.x / c.z / tanH, c.y / c.z / tanV, c.z) : new Vector3(9f, 9f, c.z);
        }
        bool Seen(Vector3 world)
        {
            Vector3 s = OnScreen(world);
            return Mathf.Abs(s.x) < 0.95f && Mathf.Abs(s.y) < 0.95f;
        }

        Drive(false);
        yield return Wait(0.4f);
        Transform seat = MagazineSwap.MagazineOf(ArmsModel);   // caché à sa place pendant le rechargement : le puits
        Vector3 center = LocalCenter(seat);
        Assert.Less(Vector3.Distance(seat.TransformPoint(center), ArmsModel.transform.position), 0.3f, "Garde : le centre du chargeur est sur l'arme.");
        Transform hand = arms.GetBoneTransform(HumanBodyBones.LeftHand);
        Vector3 palm = settings.PalmPoint(ChassisId.CT01);
        Transform[] upperArm =
        {
            arms.GetBoneTransform(HumanBodyBones.LeftShoulder), arms.GetBoneTransform(HumanBodyBones.LeftUpperArm),
            arms.GetBoneTransform(HumanBodyBones.LeftLowerArm),
        };

        // L'épaule droite (sa plaque, sur le haut du bras, et le bras, en boules) : la part de l'écran qu'elle couvre. Le
        // bras gauche ne disait rien de l'épaule droite, qu'un pistolet levé trop vers la gauche mettait dans le quart bas
        // droit. Le buste et les clavicules ne se dessinent pas (BuildView les replie, 2026-10-29). L'épaule gauche suit
        // le bras qui recharge (3 à 5 % de l'écran aux fusils, quand la main va au porte-chargeur) : elle n'est que relevée.
        Transform Bone(HumanBodyBones b) => arms.GetBoneTransform(b);
        Transform rightUpper = Bone(HumanBodyBones.RightUpperArm), rightLower = Bone(HumanBodyBones.RightLowerArm);
        Transform leftUpper = Bone(HumanBodyBones.LeftUpperArm);
        (System.Func<Vector3> at, float radius)[] rightShoulder =
        {
            (() => rightUpper.position, 0.13f), (() => Vector3.Lerp(rightUpper.position, rightLower.position, 0.5f), 0.09f),
        };
        float leftShoulderCover = 0f;
        float Covered(Vector3 world, float r)
        {
            Vector3 c = cam.InverseTransformPoint(world);
            if (c.z <= 0.05f) return 0f;
            float sx = c.x / c.z / tanH, sy = c.y / c.z / tanV, rx = r / c.z / tanH, ry = r / c.z / tanV;
            int inside = 0, all = 0;
            for (int i = -4; i <= 4; i++)
                for (int j = -4; j <= 4; j++)
                {
                    if (i * i + j * j > 16) continue;
                    all++;
                    if (Mathf.Abs(sx + i / 4f * rx) < 1f && Mathf.Abs(sy + j / 4f * ry) < 1f) inside++;
                }
            return (float)inside / all * Mathf.PI * rx * ry / 4f;
        }

        var failures = new List<string>();
        float wellAtDrop = float.NaN, wellLow = float.PositiveInfinity, wellHigh = float.NegativeInfinity;
        float nearest = float.PositiveInfinity, nearestAt = float.NaN, shoulderCover = 0f, shoulderCoverAt = float.NaN;
        var partCover = new float[rightShoulder.Length];
        int approachFrames = 0, armingFrames = 0, armingSeen = 0, armingPalmSeen = 0;
        bool dropSeen = false;
        Transform armingPart = ArmsModel.transform.Find(gesture.armingParts[0]);
        Vector3 armingPartCenter = LocalCenter(armingPart);   // son origine est celle de l'arme : le milieu de son maillage
        float armingPitchUsed = 0f;
        record = () =>
        {
            float f = armsView.Progress;
            if (float.IsNaN(f)) return;
            float cover = 0f;
            for (int t = 0; t < rightShoulder.Length; t++)
            {
                float c = Covered(rightShoulder[t].at(), rightShoulder[t].radius);
                partCover[t] = Mathf.Max(partCover[t], c);
                cover += c;
            }
            leftShoulderCover = Mathf.Max(leftShoulderCover, Covered(leftUpper.position, 0.12f));
            if (cover > shoulderCover)
            {
                shoulderCover = cover;
                shoulderCoverAt = f;
            }
            Vector3 well = seat.TransformPoint(center);
            if (f >= gesture.armingGrab && f <= gesture.armingReturned)
            {
                armingFrames++;
                armingPitchUsed = armingPitch;
                if (Seen(armingPart.TransformPoint(armingPartCenter))) armingSeen++;
                if (Seen(hand.TransformPoint(palm))) armingPalmSeen++;
            }
            if (!dropSeen && armsView.MagazineHidden)
            {
                dropSeen = true;
                wellAtDrop = OnScreen(well).y;
                if (!Seen(well)) failures.Add($"f={f:F2} : le chargeur vide tombe hors de l'écran (puits en {OnScreen(well):F2})");
            }
            GameObject held = armsView.InHand;
            if (held != null && f >= gesture.approach && f <= gesture.seated)
            {
                approachFrames++;
                wellLow = Mathf.Min(wellLow, OnScreen(well).y);
                wellHigh = Mathf.Max(wellHigh, OnScreen(well).y);
                if (!Seen(well)) failures.Add($"f={f:F2} : le puits est hors de l'écran ({OnScreen(well):F2}) quand le chargeur neuf y entre");
                Vector3 magazine = held.transform.TransformPoint(center);
                if (!Seen(magazine)) failures.Add($"f={f:F2} : le chargeur neuf est hors de l'écran ({OnScreen(magazine):F2})");
                Vector3 p = hand.TransformPoint(palm);
                if (!Seen(p)) failures.Add($"f={f:F2} : la main gauche est hors de l'écran ({OnScreen(p):F2})");
            }
            // Le haut du bras gauche, de l'épaule au coude : une pièce de 8 cm de rayon à tant de l'œil.
            for (int s = 0; s + 1 < upperArm.Length; s++)
                for (int k = 0; k <= 4; k++)
                {
                    Vector3 c = cam.InverseTransformPoint(Vector3.Lerp(upperArm[s].position, upperArm[s + 1].position, k / 4f));
                    if (c.z <= 0.02f) continue;
                    float radius = 0.08f / c.z;
                    if (Mathf.Abs(c.x / c.z) - radius < tanH && Mathf.Abs(c.y / c.z) - radius < tanV && c.magnitude < nearest)
                    {
                        nearest = c.magnitude;
                        nearestAt = f;
                    }
                }
        };
        Drive(true);
        yield return Wait(reloadDuration + 0.2f);
        record = null;
        Debug.Log($"[CHARGEUR] {code}{(sighted ? " avec le viseur " + sight : "")}, vue subjective (hauteur à l'écran, de -1 à 1) : puits à {wellAtDrop:F2} quand le chargeur vide tombe, de {wellLow:F2} à {wellHigh:F2} quand le neuf entre ; " +
                  $"armement levé de {armingPitchUsed:F1}° de plus ; " +
                  $"haut du bras gauche à {nearest * 100f:F0} cm de l'œil au plus près dans le champ (f={nearestAt:F2}) ; {approachFrames} images d'insertion ; " +
                  $"pièce d'armement dans le champ {armingSeen} images sur {armingFrames}, la main qui la tire {armingPalmSeen} ; épaule droite sur {shoulderCover * 100f:F1} % de l'écran au plus (f={shoulderCoverAt:F2} ; " +
                  $"épaule, bras : {string.Join(" ", System.Array.ConvertAll(partCover, c => (c * 100f).ToString("F1")))}) ; " +
                  $"épaule gauche {leftShoulderCover * 100f:F1} %");
        Assert.IsTrue(dropSeen, "Garde : le chargeur vide est tombé.");
        Assert.Greater(approachFrames, 3, "Garde : le chargeur neuf a été suivi jusqu'à sa place.");
        Assert.Greater(nearest, 0.35f, $"Le haut du bras gauche passe à {nearest * 100f:F0} cm de l'œil, dans le champ (f={nearestAt:F2}) : il cache la vue.");
        Assert.Less(shoulderCover, 0.05f, $"L'épaule droite couvre {shoulderCover * 100f:F0} % de l'écran (f={shoulderCoverAt:F2}) : elle cache la vue.");
        Assert.Greater(armingFrames, 3, "Garde : l'armement a été suivi.");
        Assert.GreaterOrEqual(armingSeen, 0.9f * armingFrames, $"La pièce d'armement n'est dans le champ que {armingSeen} images sur {armingFrames} : on ne la voit pas reculer.");
        Assert.GreaterOrEqual(armingPalmSeen, 0.8f * armingFrames, $"La main qui arme n'est dans le champ que {armingPalmSeen} images sur {armingFrames}.");
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    /// <summary>
    /// En vue subjective, la touche de visée tenue, la fin du rechargement amène l'arme droit en visée, au centre de
    /// l'écran, et elle y reste (2026-10-29, signalé par l'utilisateur : un léger à-coup avant de viser). La tenue
    /// relâchée dès la fin du rechargement courait après les poses du fondu de 0,2 s : l'arme dépassait le centre
    /// (jusqu'à 4,5° et 200 pixels au stand) puis repartait d'un coup en arrière ; figée sur celle d'avant le geste, elle
    /// gardait le retard du recul de la rafale qui vide le chargeur, que l'arme rattrapait ensuite ; et le cadrage du
    /// rechargement qui partait, l'épaulement et la pose qui revenaient, chacun à son rythme, la faisaient passer
    /// au-delà du centre (à la mire de l'arme, sans viseur, de 0,14 au FA-07 et de 0,30 au FA-30). On rejoue l'image de
    /// FirstPersonView, debout et à l'arrêt : sa tenue (FirstPersonView.Hold), l'épaulement, le cadrage du rechargement et
    /// celui de l'armement, puis le retour de la mire (PlaceReturning), avec les réglages du préfab.
    /// </summary>
    [UnityTest]
    public IEnumerator EnVueSubjective_ALaFinDuRechargement_LaViseeReprendSansAcoup([ValueSource(nameof(Codes))] string code)
    {
        Setup(WeaponAttachments.None, code);
        WeaponData weapon = null;
        foreach (WeaponData w in Prefab.GetComponentInChildren<WeaponController>(true).Loadout)
            if (w.weaponName == code) weapon = w;
        armsShift = Settings().PalmPoint(ChassisId.CT01) - gesture.designPalm;
        var view = new UnityEditor.SerializedObject(Prefab.GetComponent<FirstPersonView>());
        Vector3 aimSight = view.FindProperty("aimSightPosition").vector3Value;
        bool pistol = ArmsModel.PistolHold;
        Vector3 hipOffset = view.FindProperty(pistol ? "pistolHipOffset" : "hipOffset").vector3Value;
        float smoothing = view.FindProperty("holdSmoothing").floatValue;
        float fieldOfView = view.FindProperty("armsFieldOfView").floatValue;
        float pitch = view.FindProperty(pistol ? "pistolReloadViewPitch" : "reloadViewPitch").floatValue;
        float yaw = pistol ? view.FindProperty("pistolReloadViewYaw").floatValue : 0f;
        Vector3 lift = view.FindProperty(pistol ? "pistolReloadViewOffset" : "reloadViewOffset").vector3Value;
        float frameTime = view.FindProperty(pistol ? "pistolReloadFrameTime" : "reloadFrameTime").floatValue;
        float tanV = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad), tanH = tanV * 16f / 9f;
        Transform cam = cameraGo.transform, root = arms.transform;

        var hold = new FirstPersonView.Hold();
        bool aiming = false, returning = false;
        float aimBlend = 0f, reloadFrame = 0f, armingWeight = 0f, armingPitch = 0f;
        Vector3 shown = Vector3.zero, returnFrom = Vector3.zero;
        Quaternion shownRotation = Quaternion.identity, returnFromRotation = Quaternion.identity;
        place = () =>
        {
            Transform sight = AttachmentMount.SightOf(ArmsModel, out float eye);
            Vector3 pivot = root.InverseTransformPoint(sight.position);
            Quaternion align = FirstPersonView.LevelSight(root.InverseTransformDirection(sight.forward), root.InverseTransformDirection(sight.up));
            hold.Step(arms, align, pivot, root.InverseTransformPoint(ArmsModel.Sight.position), false, Time.deltaTime, smoothing);
            aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, weapon.aimTime));
            Vector3 target = new Vector3(aimSight.x, aimSight.y, eye) + hipOffset * (1f - Mathf.SmoothStep(0f, 1f, aimBlend));
            FirstPersonView.Place(root, hold.rotation, hold.pivot, target);
            bool magazineReload = !float.IsNaN(armsView.Progress);
            reloadFrame = Mathf.MoveTowards(reloadFrame, magazineReload ? 1f : 0f, Time.deltaTime / frameTime);
            if (reloadFrame > 0f)
            {
                float frame = Mathf.SmoothStep(0f, 1f, reloadFrame);
                root.localPosition += FirstPersonView.ReloadSightShift(hold.rotation, hold.ironPivot, hold.pivot, ArmsModel.AimEyeDistance, eye) * frame;
                FirstPersonView.LiftForReload(root, pitch, yaw, lift, frame);
            }
            float wanted = magazineReload ? gesture.ArmingViewWeight(armsView.Progress) : 0f;
            if (wanted > 0f)
            {
                if (armingWeight <= 0f)
                    armingPitch = gesture.ArmingViewPitch(cam.InverseTransformPoint(ArmsModel.transform.TransformPoint(gesture.ArmingPalmPulled)));
                armingWeight = wanted;
            }
            else armingWeight = Mathf.MoveTowards(armingWeight, 0f, Time.deltaTime / frameTime);
            if (armingWeight > 0f) FirstPersonView.LiftForReload(root, armingPitch, gesture.armingViewYaw, Vector3.zero, armingWeight);
            float poseLeft = FirstPersonView.ReloadPoseLeft(arms);
            if (poseLeft > 0f && poseLeft < 1f)
            {
                if (!returning)
                {
                    returning = true;
                    returnFrom = shown;
                    returnFromRotation = shownRotation;
                }
                FirstPersonView.PlaceReturning(root, align, pivot, returnFrom, returnFromRotation, target, Quaternion.identity, 1f - poseLeft);
            }
            else returning = false;
            shown = cam.InverseTransformPoint(sight.position);
            shownRotation = Quaternion.Inverse(cam.rotation) * sight.rotation;
        };

        Drive(false);
        yield return Wait(0.5f);   // la tenue se pose sur la visée au repos
        // Une rafale vide le chargeur (au pistolet, les bras ne rejouent pas le tir), et le rechargement part aussitôt :
        // la tenue est encore en retard sur le recul.
        for (int i = 0; i < 6; i++)
        {
            PlayerAnimator.FireGesture(body, arms, pistol, false);
            yield return Wait(0.12f);
        }
        Drive(true);
        yield return Wait(reloadDuration);
        // La fin du rechargement, la touche de visée tenue : l'épaulement reprend.
        aiming = true;
        Drive(false);
        var path = new List<(float time, Vector2 screen, float axis)>();
        float start = Time.time;
        record = () =>
        {
            Transform sight = AttachmentMount.SightOf(ArmsModel, out _);
            Vector3 c = cam.InverseTransformPoint(sight.position);
            path.Add((Time.time - start, new Vector2(c.x / c.z / tanH, c.y / c.z / tanV), Vector3.Angle(cam.forward, sight.forward)));
        };
        // l'épaulement et le fondu des bras vers la visée (0,2 s) finis
        float settle = Mathf.Max(weapon.aimTime, 0.2f) + 0.05f;
        yield return Wait(settle + 0.3f);
        record = null;

        // La mire s'approche du centre de l'écran sans jamais s'en éloigner de nouveau (le rebond : de combien elle s'en
        // écarte après s'en être approchée), et l'axe du canon de l'axe du regard ; une fois l'épaulement fini, elle y est.
        float nearest = float.PositiveInfinity, nearestAxis = float.PositiveInfinity, rebound = 0f, axisRebound = 0f;
        float reboundAt = float.NaN, axisReboundAt = float.NaN, settled = float.NaN, settledAxis = float.NaN;
        float jump = 0f, jumpAt = float.NaN, axisJump = 0f, axisJumpAt = float.NaN;
        Vector2 last = path[0].screen;
        float lastAxis = path[0].axis;
        foreach ((float time, Vector2 screen, float axis) in path)
        {
            // le pas d'une image à l'autre (1/60 s) : la mire va vite, mais ne saute pas, ni ne tourne d'un coup
            if ((screen - last).magnitude > jump) { jump = (screen - last).magnitude; jumpAt = time; }
            if (Mathf.Abs(axis - lastAxis) > axisJump) { axisJump = Mathf.Abs(axis - lastAxis); axisJumpAt = time; }
            last = screen;
            lastAxis = axis;
            float d = screen.magnitude;
            nearest = Mathf.Min(nearest, d);
            nearestAxis = Mathf.Min(nearestAxis, axis);
            if (d - nearest > rebound) { rebound = d - nearest; reboundAt = time; }
            if (axis - nearestAxis > axisRebound) { axisRebound = axis - nearestAxis; axisReboundAt = time; }
            if (float.IsNaN(settled) && time >= settle) { settled = d; settledAxis = axis; }
        }
        Debug.Log($"[CHARGEUR] {code}, fin du rechargement en visée : mire à {path[0].screen.magnitude:F2} du centre (écran de -1 à 1), " +
                  $"rebond de {rebound:F3} (à {reboundAt:F2} s), plus grand pas {jump:F3} (à {jumpAt:F2} s), axe du canon à {path[0].axis:F1}°, rebond de {axisRebound:F2}° (à {axisReboundAt:F2} s), plus grand pas {axisJump:F1}° (à {axisJumpAt:F2} s) ; " +
                  $"à {settle:F2} s, mire à {settled:F4} du centre, axe à {settledAxis:F3}°");
        Assert.Greater(path[0].screen.magnitude, 0.2f, "Garde : à la fin du rechargement, l'arme est loin du centre (à la hanche, cadrée pour le geste).");
        Assert.Less(axisRebound, 0.5f, $"Le canon dépasse l'axe du regard puis y revient : {axisRebound:F1}° (à {axisReboundAt:F2} s).");
        Assert.Less(rebound, 0.06f, $"La mire s'éloigne du centre de l'écran après s'en être approchée : de {rebound:F2} (à {reboundAt:F2} s).");
        Assert.Less(jump, 0.25f, $"La mire saute de {jump:F2} d'une image à l'autre (à {jumpAt:F2} s).");
        Assert.Less(axisJump, 15f, $"Le canon tourne de {axisJump:F1}° d'une image à l'autre (à {axisJumpAt:F2} s).");
        Assert.Less(settled, 0.005f, $"Une fois l'épaulement fini ({settle:F2} s), la mire est encore à {settled:F3} du centre de l'écran.");
        Assert.Less(settledAxis, 0.1f, $"Une fois l'épaulement fini ({settle:F2} s), le canon est encore à {settledAxis:F2}° de l'axe du regard.");
    }

    /// <summary>Le centre d'une pièce (la boîte de ses maillages), dans son repère.</summary>
    private static Vector3 LocalCenter(Transform piece)
    {
        var bounds = new Bounds();
        bool any = false;
        foreach (MeshFilter m in piece.GetComponentsInChildren<MeshFilter>(true))
        {
            Bounds b = m.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 p = piece.InverseTransformPoint(m.transform.TransformPoint(corner));
                if (any) bounds.Encapsulate(p);
                else { bounds = new Bounds(p, Vector3.zero); any = true; }
            }
        }
        Assert.IsTrue(any, piece.name + " n'a pas de maillage.");
        return bounds.center;
    }

    [UnityTest]
    public IEnumerator Allonge_LeCorpsGardeSonChargeur_EtLesBrasLeChangent()
    {
        Setup(WeaponAttachments.None);
        bodyStance = 2f;
        Drive(false);
        yield return Wait(1.5f);   // le temps de se coucher
        Drive(true);
        bool bodyHidden = false, armsHidden = false;
        for (float t = 0f; t < reloadDuration; t += Time.deltaTime)
        {
            yield return null;
            bodyHidden |= bodyView.MagazineHidden;
            armsHidden |= armsView.MagazineHidden;
        }
        Assert.IsFalse(bodyHidden, "Allongé, le corps joue le geste commun : son chargeur ne doit pas quitter l'arme.");
        Assert.IsTrue(armsHidden, "En vue subjective, les bras jouent toujours le geste debout : le chargeur y change.");
    }
}
#endif
