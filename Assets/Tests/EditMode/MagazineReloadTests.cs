using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Les rechargements où le chargeur quitte l'arme (2026-10-29), arme par arme : un clip calculé dans Blender
/// (rechargement_chargeur.py), importé et branché par Duel Arena > Animations > Importer les rechargements à chargeur
/// (MagazineReloadImporter), et ce que MagazineSwap en montre.
///
/// Ce qu'il faut garder, pour chaque arme : son geste tombe sur ses étapes sonores (le chargeur tombe quand on l'entend
/// sortir, entre quand on l'entend entrer, la poignée part quand on entend l'armement) ; il s'importe comme le
/// rechargement du jeu, dont il a la forme (l'état Recharge y entre et le lit pareil) ; l'Animator le joue pour cette
/// arme seule, debout et accroupi ; les repères de sa fiche sont sur le modèle (la paume contre le flanc du chargeur,
/// sous sa semelle) ; et, le clip échantillonné sur le robot du jeu, la paume gauche tient le chargeur neuf contre son
/// flanc quand il entre, frappe sa semelle, prend la poignée d'armement, et finit là où la prise du jeu (WeaponGrip)
/// la reprend.
/// </summary>
public class MagazineReloadTests
{
    /// <summary>Les armes dont le geste est exporté.</summary>
    private static IEnumerable<string> Codes() => MagazineReloadImporter.Exported();

    private static ReloadGesture Gesture(string code)
    {
        string path = MagazineReloadImporter.GesturePath(code);
        var g = AssetDatabase.LoadAssetAtPath<ReloadGesture>(path);
        Assert.IsNotNull(g, "Le geste du " + code + " manque : " + path + " (Duel Arena > Animations > Importer les rechargements à chargeur).");
        return g;
    }

    private static WeaponData Weapon(string code)
    {
        WeaponData w = MagazineReloadImporter.WeaponOf(code);
        Assert.IsNotNull(w, "Garde : le " + code + " est dans l'arsenal du préfab joueur.");
        return w;
    }

    private static AnimationClip Clip(string code)
    {
        AnimationClip clip = ProneSideCrawl.Clip(MagazineReloadImporter.ClipPath(code), MagazineReloadImporter.ClipName(code));
        Assert.IsNotNull(clip, "Le clip « " + MagazineReloadImporter.ClipName(code) + " » manque.");
        return clip;
    }

    [System.Serializable]
    private class Sheet
    {
        public string arme;
        public Steps etapes;
        public string[] sons;        // les étapes que marquent, dans l'ordre, les étapes sonores de l'arme
        public bool culasse;         // la poignée est une culasse : elle recule puis revient avec la main
        public Landmark tenue, semelle, levier, gardeMain;
        public float course;
        public float[] axePuits;
    }

    [System.Serializable]
    private class Steps
    {
        public float sortie, saisie, approche, insertion, frappe, priseLevier, recul, avance, armement, retour;

        public float this[string name] => name switch
        {
            "sortie" => sortie, "saisie" => saisie, "approche" => approche, "insertion" => insertion, "frappe" => frappe,
            "priseLevier" => priseLevier, "recul" => recul, "avance" => avance, "armement" => armement, "retour" => retour,
            _ => float.NaN,
        };
    }

    /// <summary>Un repère de la fiche, dans le repère de l'arme : le point de la paume, sa normale, ses doigts.</summary>
    [System.Serializable]
    private class Landmark
    {
        public float[] paume, normale, doigts;
        public Vector3 Point => new Vector3(paume[0], paume[1], paume[2]);
        public Vector3 Normal => new Vector3(normale[0], normale[1], normale[2]).normalized;
    }

    private static Sheet SheetOf(string code)
    {
        string path = MagazineReloadImporter.SheetPath(code);
        var text = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        Assert.IsNotNull(text, "La fiche du clip manque : " + path);
        Sheet sheet = JsonUtility.FromJson<Sheet>(text.text);
        Assert.IsNotNull(sheet.tenue?.paume, "La fiche du " + code + " n'a pas ses repères : réexporter de Blender.");
        Assert.AreEqual(code, sheet.arme, "La fiche n'est pas celle de son arme.");
        return sheet;
    }

    [Test]
    public void LesArmesAChargeur_OntLeurGeste()
    {
        List<string> codes = MagazineReloadImporter.Exported();
        foreach (string code in new[] { "FA-07", "FA-30", "FP-12", "PA-05" })
            Assert.Contains(code, codes, "Le " + code + " n'a plus son geste exporté.");
        foreach (string code in codes)
            Assert.Greater(MagazineReloadImporter.ValueOf(code), 0, code + " n'a pas de valeur de GesteRecharge.");
    }

    [TestCaseSource(nameof(Codes))]
    public void LeGeste_TombeSurLesEtapesSonoresDeSonArme(string code)
    {
        ReloadGesture g = Gesture(code);
        WeaponData w = Weapon(code);
        Sheet sheet = SheetOf(code);
        Steps steps = sheet.etapes;
        string note = "les étapes sonores du " + code + " (" + w.name + ".asset, reloadCues)";
        Assert.AreEqual(3, w.reloadCues.Length, "Garde : " + note + " sont trois.");
        Assert.AreEqual("Rech_Sortie", w.reloadCues[0].clips[0].name, "Garde : la première étape sonore du " + code + " a changé de son.");
        Assert.AreEqual("Rech_Insertion", w.reloadCues[1].clips[0].name, "Garde : la deuxième étape sonore du " + code + " a changé de son.");
        Assert.That(w.reloadCues[2].clips[0].name, Is.EqualTo("Rech_Armement").Or.EqualTo("Rech_Culasse"),
                    "Garde : la troisième étape sonore du " + code + " n'est plus l'armement.");
        // Le chargeur tombe quand on l'entend sortir, entre quand on l'entend entrer ; l'armement s'entend quand la
        // poignée est lâchée (elle revient seule), ou, une culasse, quand la main la prend (le son la fait reculer
        // puis revenir).
        Assert.AreEqual(new[] { "sortie", "insertion", sheet.culasse ? "priseLevier" : "armement" }, sheet.sons,
                        "La fiche ne dit pas les étapes que marquent les sons de l'arme.");
        string[] what = { "Le chargeur vide ne tombe pas quand on l'entend sortir", "Le chargeur neuf n'entre pas quand on l'entend entrer",
                          sheet.culasse ? "La main ne prend pas la culasse quand on l'entend partir" : "La poignée d'armement ne part pas quand on entend l'armement" };
        for (int i = 0; i < 3; i++)
            Assert.AreEqual(w.reloadCues[i].at, steps[sheet.sons[i]], 1e-4f, what[i] + " (" + note + ").");
        Assert.AreEqual(w.reloadCues[0].at, g.drop, 1e-4f, "Le chargeur vide ne tombe pas quand on l'entend sortir (" + note + ").");
        Assert.AreEqual(w.reloadCues[1].at, g.seated, 1e-4f, "Le chargeur neuf n'entre pas quand on l'entend entrer (" + note + ").");
        Assert.AreEqual(steps.sortie, g.drop, 1e-5f, "Le geste n'a pas les étapes de sa fiche : relancer l'import.");
        Assert.AreEqual(steps.saisie, g.grab, 1e-5f, "Le geste n'a pas les étapes de sa fiche : relancer l'import.");
        Assert.AreEqual(steps.approche, g.approach, 1e-5f, "Le geste n'a pas les étapes de sa fiche : relancer l'import.");
        Assert.AreEqual(steps.insertion, g.seated, 1e-5f, "Le geste n'a pas les étapes de sa fiche : relancer l'import.");
        bool bolt = !sheet.culasse || (steps.priseLevier < steps.recul && steps.recul < steps.avance && steps.avance < steps.armement);
        Assert.IsTrue(g.drop < g.grab && g.grab < g.approach && g.approach < g.seated && g.seated < steps.frappe
                      && steps.frappe < steps.priseLevier && steps.priseLevier < steps.armement && steps.armement < steps.retour && bolt,
                      "Les étapes ne se suivent pas.");
    }

    [TestCaseSource(nameof(Codes))]
    public void LeClip_SImporteCommeLeRechargementDuJeu(string code)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(MagazineReloadImporter.ClipPath(code));
        var common = (ModelImporter)AssetImporter.GetAtPath(MagazineReloadImporter.CommonClipPath);
        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ProneSideCrawl.AvatarPath);
        Assert.AreEqual(ModelImporterAnimationType.Human, importer.animationType, "Le clip n'est pas humanoïde.");
        Assert.AreEqual(ModelImporterAvatarSetup.CopyFromOther, importer.avatarSetup);
        Assert.AreEqual(avatar, importer.sourceAvatar, "Le clip ne copie pas l'avatar du Y Bot.");
        Assert.AreEqual(1, importer.clipAnimations.Length);
        ModelImporterClipAnimation c = importer.clipAnimations[0], reference = common.clipAnimations[0];
        Assert.AreEqual(MagazineReloadImporter.ClipName(code), c.name);
        Assert.IsFalse(c.loopTime, "Un rechargement ne boucle pas.");
        Assert.IsFalse(c.mirror);
        Assert.IsTrue(c.lockRootRotation == reference.lockRootRotation && c.keepOriginalOrientation == reference.keepOriginalOrientation
                      && c.lockRootHeightY == reference.lockRootHeightY && c.keepOriginalPositionY == reference.keepOriginalPositionY
                      && c.lockRootPositionXZ == reference.lockRootPositionXZ && c.keepOriginalPositionXZ == reference.keepOriginalPositionXZ
                      && c.heightFromFeet == reference.heightFromFeet,
                      "Le clip n'a pas la racine de Y Bot@Reloading.");
        AnimationClip clip = Clip(code), old = ProneSideCrawl.Clip(MagazineReloadImporter.CommonClipPath, "Reloading");
        Assert.IsTrue(clip.isHumanMotion);
        // La forme de Y Bot@Reloading : l'état Recharge y entre au même instant (12 % du clip) et le lit à la même vitesse.
        Assert.AreEqual(old.length, clip.length, 0.5f / 30f, "Le clip n'a pas la durée de Y Bot@Reloading.");
        Assert.AreEqual(clip.length, Gesture(code).clipLength, 1e-4f, "Le geste n'a pas la durée du clip : relancer l'import.");
    }

    [Test]
    public void LAnimator_JoueLeGesteDeChaqueArme_DeboutEtAccroupi_EtLeGesteCommunAllonge()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ProneSideCrawl.ControllerPath);
        AnimatorControllerParameter parameter = null;
        foreach (AnimatorControllerParameter p in controller.parameters)
            if (p.name == MagazineSwap.GestureParameter) parameter = p;
        Assert.IsNotNull(parameter, "L'Animator n'a pas le paramètre " + MagazineSwap.GestureParameter + ".");
        Assert.AreEqual(AnimatorControllerParameterType.Float, parameter.type);
        Assert.AreEqual(0f, parameter.defaultFloat, "Par défaut, le geste commun.");

        AnimatorState state = MagazineReloadImporter.ReloadState(controller);
        Assert.IsNotNull(state);
        var gestures = state.motion as BlendTree;
        Assert.IsNotNull(gestures, "L'état Recharge n'a pas d'arbre.");
        Assert.AreEqual(MagazineSwap.GestureParameter, gestures.blendParameter, "L'état Recharge ne choisit pas son geste.");
        Assert.IsFalse(gestures.useAutomaticThresholds);
        List<string> codes = MagazineReloadImporter.Exported();
        // (les armes qui s'ouvrent, le Big D, ont aussi la leur : BreakActionReloadTests)
        Assert.AreEqual(1 + codes.Count + MagazineReloadImporter.ExportedBreakActions().Count, gestures.children.Length,
                        "L'arbre n'a pas une branche par geste, plus le geste commun.");

        Dictionary<float, string> Clips(BlendTree tree, string what)
        {
            Assert.IsNotNull(tree, what + " : sa branche de l'arbre manque.");
            Assert.AreEqual("StanceF", tree.blendParameter);
            Assert.IsFalse(tree.useAutomaticThresholds);
            var clips = new Dictionary<float, string>();
            foreach (ChildMotion c in tree.children) clips[c.threshold] = c.motion != null ? c.motion.name : null;
            return clips;
        }
        BlendTree Branch(float value)
        {
            foreach (ChildMotion c in gestures.children)
                if (Mathf.Approximately(c.threshold, value)) return c.motion as BlendTree;
            return null;
        }
        Dictionary<float, string> before = Clips(Branch(0f), "Le geste commun");
        Assert.AreEqual("Reloading", before[0f], "Le geste commun, debout, n'est plus Y Bot@Reloading.");
        Assert.AreEqual("Crouch Reload", before[1f], "Le geste commun, accroupi, n'est plus le sien.");
        Assert.AreEqual("Prone Reloading", before[2f], "Le geste commun, allongé, n'est plus le sien.");
        foreach (string code in codes)
        {
            int value = MagazineReloadImporter.ValueOf(code);
            Assert.AreEqual(value, Gesture(code).animatorValue, "Le geste du " + code + " ne choisit pas sa branche de l'arbre.");
            Dictionary<float, string> now = Clips(Branch(value), code);
            Assert.AreEqual(MagazineReloadImporter.ClipName(code), now[0f], "Debout, le " + code + " ne joue pas son geste.");
            Assert.AreEqual(MagazineReloadImporter.ClipName(code), now[1f], "Accroupi, le " + code + " ne joue pas son geste.");
            Assert.AreEqual("Prone Reloading", now[2f], "Allongé, le " + code + " garde le geste commun (son chargeur reste en place).");
        }
    }

    [Test]
    public void LeJoueur_PorteLeGesteDeChaqueArme_EtLesAutresLeGesteCommun()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MagazineReloadImporter.PlayerPath);
        var swap = prefab.GetComponent<MagazineSwap>();
        Assert.IsNotNull(swap, "Le préfab joueur n'a pas MagazineSwap.");
        List<string> codes = MagazineReloadImporter.Exported();
        int with = 0, without = 0;
        foreach (WeaponData w in prefab.GetComponentInChildren<WeaponController>(true).Loadout)
        {
            if (codes.Contains(w.weaponName))
            {
                with++;
                Assert.AreEqual(Gesture(w.weaponName), swap.GestureFor(w), "Le " + w.weaponName + " ne change pas de chargeur.");
            }
            else
            {
                without++;
                Assert.IsNull(swap.GestureFor(w), w.weaponName + " prend un geste qui n'est pas fait pour lui.");
            }
        }
        Assert.AreEqual(codes.Count, with, "Garde : chaque geste a son arme dans l'arsenal.");
        Assert.AreEqual(12 - codes.Count, without, "Garde : les autres armes de l'arsenal.");
    }

    [Test]
    public void AChaquePartDuRechargement_LeChargeurEstALaBonnePlace()
    {
        var g = ScriptableObject.CreateInstance<ReloadGesture>();
        try
        {
            g.drop = 0.04f;
            g.grab = 0.26f;
            g.approach = 0.40f;
            g.seated = 0.45f;
            Assert.AreEqual(MagazineSwap.Phase.InWeapon, MagazineSwap.PhaseAt(0f, g));
            Assert.AreEqual(MagazineSwap.Phase.InWeapon, MagazineSwap.PhaseAt(0.039f, g));
            Assert.AreEqual(MagazineSwap.Phase.Empty, MagazineSwap.PhaseAt(0.04f, g), "Le chargeur vide ne tombe pas à son étape.");
            Assert.AreEqual(MagazineSwap.Phase.Empty, MagazineSwap.PhaseAt(0.2f, g));
            Assert.AreEqual(MagazineSwap.Phase.InHand, MagazineSwap.PhaseAt(0.26f, g), "Le chargeur neuf n'apparaît pas dans la main à son étape.");
            Assert.AreEqual(MagazineSwap.Phase.InHand, MagazineSwap.PhaseAt(0.449f, g));
            Assert.AreEqual(MagazineSwap.Phase.InWeapon, MagazineSwap.PhaseAt(0.45f, g), "Le chargeur neuf n'est pas en place à son étape.");
            Assert.AreEqual(MagazineSwap.Phase.InWeapon, MagazineSwap.PhaseAt(1f, g));
            Assert.AreEqual(MagazineSwap.Phase.InWeapon, MagazineSwap.PhaseAt(float.NaN, g), "Hors du geste, l'arme garde son chargeur.");
            Assert.AreEqual(0f, MagazineSwap.SeatBlend(0.3f, g), 1e-6f);
            Assert.AreEqual(0f, MagazineSwap.SeatBlend(0.40f, g), 1e-6f, "Le chargeur quitte la main avant l'approche.");
            Assert.AreEqual(0.5f, MagazineSwap.SeatBlend(0.425f, g), 1e-6f);
            Assert.AreEqual(1f, MagazineSwap.SeatBlend(0.45f, g), 1e-6f, "Le chargeur n'est pas à sa place à l'insertion.");
            Assert.AreEqual(0f, g.ProgressAt(g.usefulStart), 1e-6f);
            Assert.AreEqual(1f, g.ProgressAt(g.usefulEnd), 1e-6f);
        }
        finally
        {
            Object.DestroyImmediate(g);
        }
    }

    /// <summary>
    /// La vue subjective pendant l'armement (ReloadGesture.ArmingViewWeight, ArmingViewPitch) : le cadrage monte avant
    /// que la main prenne la pièce, tient jusqu'à son retour, puis redescend, en S ; la levée met la main sur la pièce
    /// tirée à ArmingPalmAngle sous l'axe de la vue, après la rotation du geste, sans dépasser son maximum, et rien
    /// quand elle y est déjà ou que le geste n'en veut pas.
    /// </summary>
    [Test]
    public void LeCadrageDeLArmement_MonteAvantLaPrise_EtMetLaMainAuBordDuChamp()
    {
        var g = ScriptableObject.CreateInstance<ReloadGesture>();
        try
        {
            g.armingGrab = 0.66f;
            g.armingReturned = 0.735f;
            g.armingViewPitchMax = 35f;
            float lead = ReloadGesture.ArmingViewLeadIn, tail = ReloadGesture.ArmingViewLeadOut;
            Assert.AreEqual(0f, g.ArmingViewWeight(0.66f - lead - 0.01f), 1e-6f, "Le cadrage monte trop tôt.");
            Assert.AreEqual(0.5f, g.ArmingViewWeight(0.66f - lead / 2f), 1e-4f, "À mi-montée, le cadrage n'est pas à moitié (une courbe en S).");
            Assert.AreEqual(1f, g.ArmingViewWeight(0.66f), 1e-6f, "La main prend la pièce avant que le cadrage soit monté.");
            Assert.AreEqual(1f, g.ArmingViewWeight(0.70f), 1e-6f);
            Assert.AreEqual(1f, g.ArmingViewWeight(0.735f), 1e-6f, "Le cadrage redescend avant que la pièce soit revenue.");
            Assert.AreEqual(0.5f, g.ArmingViewWeight(0.735f + tail / 2f), 1e-4f);
            Assert.AreEqual(0f, g.ArmingViewWeight(0.735f + tail + 0.01f), 1e-6f, "Le cadrage reste levé après l'armement.");
            Assert.AreEqual(0f, g.ArmingViewWeight(float.NaN), 1e-6f, "Hors du rechargement, le cadrage se lève.");

            // la main droit devant, 40° sous l'axe : 14° de plus pour la mettre à 26°
            Vector3 Below(float degrees, float distance) => Quaternion.AngleAxis(degrees, Vector3.right) * Vector3.forward * distance;
            Assert.AreEqual(40f - ReloadGesture.ArmingPalmAngle, g.ArmingViewPitch(Below(40f, 0.5f)), 1e-3f, "La levée ne met pas la main au bord du champ.");
            Assert.AreEqual(40f - ReloadGesture.ArmingPalmAngle, g.ArmingViewPitch(Below(40f, 0.3f)), 1e-3f, "La levée dépend de la distance de la main.");
            Assert.AreEqual(0f, g.ArmingViewPitch(Below(15f, 0.5f)), 1e-6f, "La main déjà dans le champ fait lever les bras.");
            Assert.AreEqual(35f, g.ArmingViewPitch(Below(80f, 0.5f)), 1e-6f, "La levée dépasse son maximum.");
            // la rotation du geste se fait d'abord : une main sur le côté, que la rotation met devant
            g.armingViewYaw = 30f;
            Vector3 side = Quaternion.AngleAxis(30f, Vector3.up) * Below(40f, 0.5f);
            Assert.AreEqual(40f - ReloadGesture.ArmingPalmAngle, g.ArmingViewPitch(side), 1e-3f, "La levée ne compte pas la rotation du geste.");
            g.armingViewPitchMax = 0f;
            Assert.AreEqual(0f, g.ArmingViewPitch(Below(40f, 0.5f)), 1e-6f, "Un geste sans cadrage de l'armement lève les bras.");
            Assert.AreEqual(0f, g.ArmingViewWeight(0.70f), 1e-6f);
        }
        finally
        {
            Object.DestroyImmediate(g);
        }
    }

    /// <summary>
    /// Pendant un rechargement, la copie des bras reprend la place qu'elle aurait sans viseur
    /// (FirstPersonView.ReloadSightShift) : posée sur la mire du viseur puis décalée, elle met la mire de l'arme là où la
    /// poserait Place, à la distance de l'œil de l'arme. Rien sans viseur.
    /// </summary>
    [Test]
    public void SousUnViseur_LeRechargementReplaceLaCopieCommeSansViseur()
    {
        var root = new GameObject("Copie").transform;
        try
        {
            Quaternion hold = Quaternion.Euler(3f, -8f, 1f);
            Vector3 iron = new Vector3(0f, 0.207f, -0.048f), sight = new Vector3(0f, 0.216f, 0.060f);
            Vector3 target = new Vector3(0.12f, -0.18f, 0f);
            FirstPersonView.Place(root, hold, sight, target + new Vector3(0f, 0f, 0.082f));
            root.localPosition += FirstPersonView.ReloadSightShift(hold, iron, sight, 0.100f, 0.082f);
            Vector3 placed = root.localPosition;
            FirstPersonView.Place(root, hold, iron, target + new Vector3(0f, 0f, 0.100f));
            Assert.Less(Vector3.Distance(placed, root.localPosition), 1e-6f, "La copie n'est pas là où la mettrait la mire de l'arme.");
            Assert.Greater((placed - (target + new Vector3(0f, 0f, 0.082f) - hold * sight)).magnitude, 0.1f, "Garde : le viseur déplace bien la copie de plus de 10 cm.");
            Assert.Less(FirstPersonView.ReloadSightShift(hold, iron, iron, 0.1f, 0.1f).magnitude, 1e-6f, "Sans viseur, le rechargement déplace la copie.");
        }
        finally
        {
            Object.DestroyImmediate(root.gameObject);
        }
    }

    /// <summary>
    /// À la fin d'un rechargement, pendant que les bras reviennent à la visée, la copie placée par
    /// FirstPersonView.PlaceReturning met sa mire sur la droite qui va de là où le geste la montrait à sa place, sans
    /// jamais s'en éloigner, et la tourne de l'une à l'autre : au départ là où elle était, à la fin à sa place, dans
    /// l'axe voulu. Quelle que soit la pose, que le fondu change à chaque image.
    /// </summary>
    [Test]
    public void ALaFinDuRechargement_LaMireGlisseDroitVersSaPlace()
    {
        var cam = new GameObject("Camera").transform;
        cam.SetPositionAndRotation(new Vector3(2f, 1.6f, -1f), Quaternion.Euler(-10f, 40f, 0f));
        var root = new GameObject("Copie").transform;
        root.SetParent(cam, false);
        var sight = new GameObject("Mire").transform;
        sight.SetParent(root, false);
        try
        {
            Vector3 from = new Vector3(0.18f, -0.25f, 0.30f), to = new Vector3(0f, 0f, 0.10f);
            Quaternion fromRotation = Quaternion.Euler(-35f, 8f, 12f), toRotation = Quaternion.identity;
            Vector3 along = to - from;
            float previous = float.PositiveInfinity;
            for (int i = 0; i <= 10; i++)
            {
                sight.localPosition = new Vector3(0.01f * i, 0.2f - 0.005f * i, 0.05f * Mathf.Sin(i));
                sight.localRotation = Quaternion.Euler(3f * i, -2f * i, i);
                Quaternion align = FirstPersonView.LevelSight(root.InverseTransformDirection(sight.forward), root.InverseTransformDirection(sight.up));
                FirstPersonView.PlaceReturning(root, align, root.InverseTransformPoint(sight.position), from, fromRotation, to, toRotation, i / 10f);
                Vector3 at = cam.InverseTransformPoint(sight.position);
                Quaternion facing = Quaternion.Inverse(cam.rotation) * sight.rotation;
                float s = Vector3.Dot(at - from, along) / along.sqrMagnitude;
                Assert.Less(Vector3.Distance(at, from + along * s), 1e-5f, $"Image {i} : la mire quitte la droite.");
                Assert.LessOrEqual(Vector3.Distance(at, to), previous + 1e-6f, $"Image {i} : la mire s'éloigne de sa place.");
                previous = Vector3.Distance(at, to);
                if (i == 0)
                {
                    Assert.Less(Vector3.Distance(at, from), 1e-5f, "Au départ, la mire saute.");
                    Assert.Less(Quaternion.Angle(facing, fromRotation), 0.01f, "Au départ, la mire tourne d'un coup.");
                }
            }
            Assert.Less(previous, 1e-5f, "À la fin, la mire n'est pas à sa place.");
            Assert.Less(Quaternion.Angle(Quaternion.Inverse(cam.rotation) * sight.rotation, toRotation), 0.01f, "À la fin, la mire n'est pas dans l'axe voulu.");
        }
        finally
        {
            Object.DestroyImmediate(cam.gameObject);
        }
    }

    [Test]
    public void LaPieceDArmement_ReculeEnS_ResteTiree_PuisRevient()
    {
        var g = ScriptableObject.CreateInstance<ReloadGesture>();
        try
        {
            g.armingParts = new[] { "Levier_Armement" };
            g.armingTravel = 0.07f;
            g.armingGrab = 0.66f;
            g.armingPulled = 0.71f;
            g.armingRelease = 0.72f;
            g.armingReturned = 0.735f;
            Assert.AreEqual(0f, g.ArmingOffset(0.5f), 1e-6f, "La pièce recule avant que la main la prenne.");
            Assert.AreEqual(0f, g.ArmingOffset(0.66f), 1e-6f);
            Assert.AreEqual(0.035f, g.ArmingOffset(0.685f), 1e-5f, "À mi-chemin, la pièce n'est pas à mi-course (la main du clip y va en S).");
            Assert.Less(g.ArmingOffset(0.665f), 0.035f * 0.1f, "La pièce part d'un coup : la main du clip démarre en douceur.");
            Assert.AreEqual(0.07f, g.ArmingOffset(0.71f), 1e-6f, "Tirée à fond, la pièce n'a pas reculé de sa course.");
            Assert.AreEqual(0.07f, g.ArmingOffset(0.715f), 1e-6f, "La pièce revient avant que la main la lâche.");
            Assert.AreEqual(0.035f, g.ArmingOffset(0.7275f), 1e-5f);
            Assert.AreEqual(0f, g.ArmingOffset(0.735f), 1e-6f, "Lâchée, la pièce ne revient pas à sa place.");
            Assert.AreEqual(0f, g.ArmingOffset(float.NaN), 1e-6f, "Hors du geste, la pièce recule.");
            g.armingParts = new string[0];
            Assert.AreEqual(0f, g.ArmingOffset(0.71f), 1e-6f, "Un geste sans pièce d'armement fait reculer quelque chose.");
        }
        finally
        {
            Object.DestroyImmediate(g);
        }
    }

    [TestCaseSource(nameof(Codes))]
    public void LesPiecesDArmement_SontSurLeModele_EtBougentAuxEtapesDeLaFiche(string code)
    {
        ReloadGesture g = Gesture(code);
        Sheet sheet = SheetOf(code);
        Transform model = Weapon(code).modelPrefab.transform;
        Assert.Greater(g.armingParts.Length, 0, "Le " + code + " n'a pas de pièce d'armement.");
        foreach (string path in g.armingParts)
            Assert.IsNotNull(model.Find(path), "La pièce d'armement « " + path + " » n'est pas sur le modèle du " + code + " : réexporter l'arme, ou corriger sa fiche (rechargement_chargeur.py).");
        Assert.IsNotNull(model.Find(g.armingParts[0]).GetComponent<MeshRenderer>(), "Garde : la première pièce d'armement se voit.");
        Assert.AreEqual(sheet.course, g.armingTravel, 1e-5f, "La pièce ne recule pas de la course de la main.");
        Assert.AreEqual(sheet.etapes.priseLevier, g.armingGrab, 1e-5f, "La pièce bouge avant que la main la prenne.");
        if (sheet.culasse)
        {
            Assert.AreEqual(sheet.etapes.recul, g.armingPulled, 1e-5f, "La culasse n'est pas tirée à fond quand la main l'est.");
            Assert.AreEqual(sheet.etapes.avance, g.armingReturned, 1e-5f, "La culasse ne revient pas avec la main.");
        }
        else
        {
            Assert.AreEqual(sheet.etapes.armement, g.armingRelease, 1e-5f, "La poignée ne revient pas quand la main la lâche.");
            Assert.IsTrue(g.armingPulled < g.armingRelease && g.armingRelease < g.armingReturned, "Les instants de la poignée ne se suivent pas.");
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Les repères, sur le modèle de l'arme
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// La paume de la tenue est contre le flanc gauche du chargeur, celle de la frappe sous sa semelle : un rayon tiré
    /// de la paume le long de sa normale (vers le chargeur) le touche à 0 à 4 mm, dans le repère de l'arme. Un repère
    /// mal relevé tenait le chargeur dans le vide, ou la main le traversait (rien ne le montre dans les tests du clip,
    /// qui ne comparent la main qu'à la fiche).
    /// </summary>
    [TestCaseSource(nameof(Codes))]
    public void LesReperes_SontSurLeChargeurDuModele(string code)
    {
        WeaponData w = Weapon(code);
        Sheet sheet = SheetOf(code);
        Transform root = w.modelPrefab.transform;
        Transform magazine = root.Find(MagazineSwap.StandardMagazineName);
        Assert.IsNotNull(magazine, "Garde : le modèle du " + code + " a son chargeur d'origine.");
        var probe = new GameObject("Chargeur_Sonde");
        try
        {
            // Une copie des maillages du chargeur, en colliders, dans le repère de l'arme.
            foreach (MeshFilter m in magazine.GetComponentsInChildren<MeshFilter>(true))
            {
                var part = new GameObject(m.name);
                part.transform.SetParent(probe.transform, false);
                part.transform.localPosition = root.InverseTransformPoint(m.transform.position);
                part.transform.localRotation = Quaternion.Inverse(root.rotation) * m.transform.rotation;
                part.transform.localScale = m.transform.lossyScale;
                part.AddComponent<MeshCollider>().sharedMesh = m.sharedMesh;
            }
            Physics.SyncTransforms();
            var failures = new List<string>();
            void Expect(string what, Landmark landmark)
            {
                Vector3 n = landmark.Normal;
                Vector3 from = landmark.Point - n * 0.05f;
                float gap = float.NaN;
                foreach (RaycastHit hit in Physics.RaycastAll(from, n, 0.10f))
                    if (hit.collider.transform.IsChildOf(probe.transform)) gap = float.IsNaN(gap) ? hit.distance - 0.05f : Mathf.Min(gap, hit.distance - 0.05f);
                Debug.Log($"[RECHARGEMENT] {code}, {what} : la paume à {gap * 1000f:F1} mm du chargeur");
                if (float.IsNaN(gap)) failures.Add($"{what} : la paume, en {landmark.Point:F3}, ne fait pas face au chargeur");
                else if (gap < 0f || gap > 0.004f) failures.Add($"{what} : la paume est à {gap * 1000f:F1} mm du chargeur (de 0 à 4 attendus)");
            }
            Expect("tenue (son flanc gauche)", sheet.tenue);
            Expect("frappe (sous sa semelle)", sheet.semelle);
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Le clip, échantillonné sur le robot du jeu
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>La paume du CT-01 (WeaponGrip, repère de l'os de la main gauche) dans le repère de l'arme, et la
    /// rotation de la main dans ce repère, le clip échantillonné à la part `f` du rechargement.</summary>
    private static void PalmAt(AnimationClip clip, ReloadGesture g, float f, out Vector3 palm, out Quaternion hand)
        => PalmAtTime(clip, g.usefulStart + f * (g.usefulEnd - g.usefulStart), out palm, out hand);

    /// <summary>La même chose, le clip échantillonné à l'instant `time` (s).</summary>
    private static void PalmAtTime(AnimationClip clip, float time, out Vector3 palm, out Quaternion hand)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(MagazineReloadImporter.PlayerPath);
        try
        {
            Transform model = root.transform.Find("Model");
            Animator animator = model.GetComponent<Animator>();
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform anchor = animator.GetBoneTransform(HumanBodyBones.RightHand).Find(HeldWeapons.GripAnchorName);
            Vector3 point = root.GetComponent<PlayerAnimator>().HandGrip.PalmPoint(ChassisId.CT01);
            model.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clip.SampleAnimation(model.gameObject, time);
            // la main porte le gant (échelle nulle) : positions et rotations, sans ses matrices
            Quaternion toWeapon = Quaternion.Inverse(anchor.rotation);
            palm = toWeapon * (left.position + left.rotation * point - anchor.position);
            hand = toWeapon * left.rotation;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [TestCaseSource(nameof(Codes))]
    public void LeClip_PoseLaPaumeSurLeChargeurLaSemelleEtLaPoignee_AuxEtapesQuiComptent(string code)
    {
        AnimationClip clip = Clip(code);
        ReloadGesture g = Gesture(code);
        Sheet sheet = SheetOf(code);
        var failures = new List<string>();
        void Expect(string what, float f, Vector3 wanted, float tolerance)
        {
            PalmAt(clip, g, f, out Vector3 palm, out _);
            float off = Vector3.Distance(palm, wanted);
            Debug.Log($"[RECHARGEMENT] {code}, {what} (f={f:F2}) : paume à {off * 1000f:F1} mm de sa place");
            if (off > tolerance) failures.Add($"{what} (f={f:F2}) : la paume est à {off * 1000f:F0} mm de sa place {wanted:F3}, à {palm:F3}");
        }
        Expect("chargeur neuf en place", g.seated, sheet.tenue.Point, 0.008f);
        Expect("semelle frappée", sheet.etapes.frappe, sheet.semelle.Point, 0.008f);
        Expect("poignée d'armement prise", sheet.etapes.priseLevier, sheet.levier.Point, 0.010f);
        if (sheet.culasse)
        {
            // La culasse recule de sa course dans l'axe du canon, puis revient avec la main.
            Expect("culasse tirée", sheet.etapes.recul, sheet.levier.Point + Vector3.back * sheet.course, 0.010f);
            Expect("culasse revenue", sheet.etapes.avance, sheet.levier.Point, 0.010f);
        }
        // La main ne quitte pas le puits pendant que le chargeur y entre : 4 cm, droit dans l'axe du puits.
        var axis = new Vector3(sheet.axePuits[0], sheet.axePuits[1], sheet.axePuits[2]).normalized;
        PalmAt(clip, g, g.approach, out Vector3 approach, out _);
        Vector3 rise = sheet.tenue.Point - approach;
        float along = Vector3.Dot(rise, axis);
        float across = (rise - axis * along).magnitude;
        if (Mathf.Abs(along - 0.04f) > 0.008f || across > 0.008f)
            failures.Add($"à l'approche, la paume n'est pas 4 cm sous sa place, dans l'axe du puits : {along * 100f:F1} cm le long, {across * 100f:F1} cm de travers");
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    /// <summary>La tenue d'un pistolet hors visée (l'état Arme, ArmePistolet) : sa main gauche n'est pas prise par
    /// WeaponGrip (un pistolet n'a pas de repère MainGauche), c'est ce clip qui la pose.</summary>
    private const string PistolIdlePath = "Assets/_ProjectArt/Animations/New/Y Bot@Pistol NoAim Idle.fbx";

    [TestCaseSource(nameof(Codes))]
    public void LeClip_CommenceEtFinitLaOuLaPriseDuJeuTientLeGardeMain(string code)
    {
        AnimationClip clip = Clip(code);
        ReloadGesture g = Gesture(code);
        Sheet sheet = SheetOf(code);
        WeaponModel model = Weapon(code).modelPrefab.GetComponent<WeaponModel>();
        Vector3 expected;
        Quaternion hold;
        string where;
        if (model.PistolHold)
        {
            AnimationClip idle = ProneSideCrawl.Clip(PistolIdlePath, "Pistol NoAim Idle");
            Assert.IsNotNull(idle, "Garde : la tenue du pistolet hors visée manque : " + PistolIdlePath);
            PalmAtTime(idle, 0f, out expected, out hold);
            where = "la tenue du pistolet (Pistol NoAim Idle)";
        }
        else
        {
            Transform marker = model.LeftHand;
            Assert.IsNotNull(marker, "Garde : le " + code + " a son repère MainGauche.");
            expected = marker.localPosition;
            hold = AssetDatabase.LoadAssetAtPath<GameObject>(MagazineReloadImporter.PlayerPath)
                .GetComponent<PlayerAnimator>().HandGrip.handguard.Rotation;
            where = "le repère MainGauche (WeaponGrip)";
        }
        var failures = new List<string>();
        foreach (float f in new[] { 0f, sheet.etapes.retour, 1f })
        {
            PalmAt(clip, g, f, out Vector3 palm, out Quaternion hand);
            float off = Vector3.Distance(palm, expected);
            float turn = Quaternion.Angle(hand, hold);
            Debug.Log($"[RECHARGEMENT] {code}, prise du jeu (f={f:F2}) : paume à {off * 1000f:F1} mm de {where}, main tournée de {turn:F1}°");
            if (off > 0.008f) failures.Add($"f={f:F2} : la paume est à {off * 1000f:F0} mm de {where}");
            if (turn > 6f) failures.Add($"f={f:F2} : la main est tournée de {turn:F0}° de {where}");
        }
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }
}
