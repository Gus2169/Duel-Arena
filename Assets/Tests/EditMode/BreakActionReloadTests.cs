using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Le rechargement du Big D, l'arme du Golden Gun (2026-11-03) : une arme qui s'ouvre (BreakActionGesture). Son modèle
/// (Tools/Blender/golden_gun.py, exporté par exporter_armes.py) et son clip (rechargement_golden_gun.py), importés et
/// branchés par Duel Arena > Armes > Importer les armes de Blender et Duel Arena > Animations > Importer les rechargements
/// à chargeur.
///
/// Ce qu'il faut garder : le Big D est le modèle du Golden Gun, tenu en pistolet, ses pièces mobiles là où le geste les
/// cherche, et elles tournent dans le bon sens ; le geste est branché dans l'Animator et sur le joueur ; les pièces
/// bougent comme dans Blender (les relevés de la fiche) ; les sons du rechargement tombent sur le geste ; et, le clip
/// échantillonné sur le robot du jeu, la paume gauche est sous le canon qu'elle lance, sur la cartouche quand elle se
/// présente et quand elle entre, sur le flanc du canon qu'elle ramène, et dans la tenue des pistolets au début et à la fin.
/// </summary>
public class BreakActionReloadTests
{
    private const string Code = "BIG-D";
    private const string GoldenGunPath = "Assets/_ProjectArena/Weapon/Arme_GoldenGun.asset";

    /// <summary>La tenue d'un pistolet hors visée (l'état Arme, ArmePistolet) : c'est elle qui pose la main gauche quand
    /// on ne recharge pas (un pistolet n'a pas de repère MainGauche).</summary>
    private const string PistolIdlePath = "Assets/_ProjectArt/Animations/New/Y Bot@Pistol NoAim Idle.fbx";

    private static BreakActionGesture Gesture()
    {
        var g = AssetDatabase.LoadAssetAtPath<BreakActionGesture>(MagazineReloadImporter.GesturePath(Code));
        Assert.IsNotNull(g, "Le geste du Big D manque : " + MagazineReloadImporter.GesturePath(Code)
                            + " (Duel Arena > Animations > Importer les rechargements à chargeur).");
        return g;
    }

    private static WeaponData GoldenGun()
    {
        var w = AssetDatabase.LoadAssetAtPath<WeaponData>(GoldenGunPath);
        Assert.IsNotNull(w, "Garde : l'arme du Golden Gun : " + GoldenGunPath);
        return w;
    }

    private static AnimationClip Clip()
    {
        AnimationClip clip = ProneSideCrawl.Clip(MagazineReloadImporter.ClipPath(Code), MagazineReloadImporter.ClipName(Code));
        Assert.IsNotNull(clip, "Le clip « " + MagazineReloadImporter.ClipName(Code) + " » manque.");
        return clip;
    }

    [System.Serializable]
    private class Sheet
    {
        public string arme;
        public Steps etapes;
        public Landmark sous, flanc, pince;
        public Record[] releves;
    }

    [System.Serializable]
    private class Steps
    {
        public float saisie, retour, lacher;
    }

    [System.Serializable]
    private class Landmark
    {
        public float[] paume;
        public Vector3 Point => new Vector3(paume[0], paume[1], paume[2]);
    }

    /// <summary>Les pièces relevées dans Blender à une part du rechargement (golden_gun.pose ; -1 : rien).</summary>
    [System.Serializable]
    private class Record
    {
        public float f, canon, chien, levier, douille, cartouche;
    }

    private static Sheet SheetOf()
    {
        string path = MagazineReloadImporter.SheetPath(Code);
        var text = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        Assert.IsNotNull(text, "La fiche du clip manque : " + path);
        Sheet sheet = JsonUtility.FromJson<Sheet>(text.text);
        Assert.AreEqual(Code, sheet.arme, "La fiche n'est pas celle du Big D.");
        Assert.IsNotNull(sheet.pince?.paume, "La fiche du Big D n'a pas ses repères : réexporter de Blender.");
        return sheet;
    }

    /// <summary>Le milieu des maillages sous `t`, dans le repère de `root`.</summary>
    private static Vector3 Center(Transform root, Transform t)
    {
        Vector3 sum = Vector3.zero;
        int n = 0;
        foreach (MeshFilter m in t.GetComponentsInChildren<MeshFilter>(true))
        {
            sum += root.InverseTransformPoint(m.transform.TransformPoint(m.sharedMesh.bounds.center));
            n++;
        }
        Assert.Greater(n, 0, "Garde : " + t.name + " a ses maillages.");
        return sum / n;
    }

    [Test]
    public void LeBigD_EstLeModeleDuGoldenGun_AvecSesPiecesMobiles()
    {
        WeaponData w = GoldenGun();
        Assert.IsNotNull(w.modelPrefab, "Le Golden Gun n'a pas de modèle.");
        var model = w.modelPrefab.GetComponent<WeaponModel>();
        Assert.IsNotNull(model);
        Assert.AreEqual(Code, model.Designation, "Le Golden Gun n'a pas le modèle du Big D.");
        Assert.IsTrue(model.PistolHold, "Le Big D ne se tient pas comme un pistolet.");
        BreakActionGesture g = Gesture();
        Transform root = w.modelPrefab.transform;
        foreach (string path in new[] { g.barrelPath, g.hammerPath, g.latchPath, g.cartridgePath, g.casePath, g.bulletPath })
            Assert.IsNotNull(root.Find(path), "Le Big D n'a pas sa pièce « " + path + " ».");
        // la visée et la bouche sont celles de l'arme fermée : elles ne tournent pas avec le canon
        Assert.AreEqual(root, model.Sight.parent, "La mire tournerait avec le canon.");
        Assert.AreEqual(root, model.Muzzle.parent, "La bouche tournerait avec le canon.");
        // les pièces mobiles sont à leur place au repos : le canon fermé, la cartouche dans la chambre
        Assert.AreEqual(Quaternion.identity, root.Find(g.barrelPath).localRotation, "Le canon n'est pas fermé au repos.");
        Assert.AreEqual(Quaternion.identity, root.Find(g.hammerPath).localRotation, "Le chien n'est pas armé au repos.");
    }

    [Test]
    public void LesPieces_TournentDansLeSensDuGeste()
    {
        BreakActionGesture g = Gesture();
        GameObject go = Object.Instantiate(GoldenGun().modelPrefab);
        try
        {
            Transform root = go.transform, barrel = root.Find(g.barrelPath);
            Assert.IsNotNull(barrel, "Garde : le modèle du Golden Gun a le canon du Big D.");
            Vector3 pivot = barrel.localPosition, closed = Center(root, barrel);
            // Ouvert à 90°, la bouche monte ; à 180°, le canon est couché en arrière, au-dessus de la carcasse.
            barrel.localRotation = BreakActionGesture.Turn(-90f);
            Vector3 up = Center(root, barrel);
            Assert.Greater(up.y - closed.y, 0.05f, "Le canon ne bascule pas vers le haut : il passerait dans la carcasse.");
            barrel.localRotation = BreakActionGesture.Turn(-g.barrelAngle);
            Vector3 open = Center(root, barrel);
            Assert.Less(open.z, pivot.z - 0.05f, "Ouvert, le canon n'est pas couché en arrière.");
            Assert.Greater(open.y, pivot.y, "Ouvert, le canon n'est pas au-dessus de la charnière.");
            Assert.Less(Vector3.Dot(root.InverseTransformDirection(barrel.forward), Vector3.forward), -0.99f,
                        "Ouvert, le canon ne pointe pas vers l'arrière.");
            // La cartouche, sortie de la chambre canon ouvert, s'avance devant elle (la chambre regarde vers l'avant).
            Transform cartridge = root.Find(g.cartridgePath);
            Vector3 seated = root.InverseTransformPoint(cartridge.position);
            cartridge.localPosition += Vector3.back * g.cartridgeApproach;
            Vector3 presented = root.InverseTransformPoint(cartridge.position);
            Assert.AreEqual(g.cartridgeApproach, presented.z - seated.z, 1e-4f, "La cartouche ne sort pas de la chambre vers l'avant.");

            // Le chien s'abat vers l'avant : sa crête (le haut de l'arrière) avance.
            Transform hammer = root.Find(g.hammerPath);
            MeshFilter crestMesh = hammer.GetComponentInChildren<MeshFilter>();
            Vector3[] v = crestMesh.sharedMesh.vertices;
            int crest = 0;
            for (int i = 1; i < v.Length; i++)
                if (v[i].y - v[i].z > v[crest].y - v[crest].z) crest = i;
            Vector3 cocked = root.InverseTransformPoint(crestMesh.transform.TransformPoint(v[crest]));
            hammer.localRotation = BreakActionGesture.Turn(g.hammerAngle);
            Vector3 fired = root.InverseTransformPoint(crestMesh.transform.TransformPoint(v[crest]));
            Assert.Greater(fired.z - cocked.z, 0.004f, "Le chien ne s'abat pas vers l'avant.");

            // Le levier s'abaisse : son bout arrière, sous le pouce, descend.
            Transform latch = root.Find(g.latchPath);
            MeshFilter latchMesh = latch.GetComponentInChildren<MeshFilter>();
            Vector3[] w = latchMesh.sharedMesh.vertices;
            int back = 0;
            for (int i = 1; i < w.Length; i++)
                if (w[i].z < w[back].z) back = i;
            Vector3 rest = root.InverseTransformPoint(latchMesh.transform.TransformPoint(w[back]));
            latch.localRotation = BreakActionGesture.Turn(-g.latchAngle);
            Vector3 pressed = root.InverseTransformPoint(latchMesh.transform.TransformPoint(w[back]));
            Assert.Less(pressed.y - rest.y, -0.003f, "Le levier ne s'abaisse pas sous le pouce.");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void LesPiecesBougent_CommeDansBlender()
    {
        BreakActionGesture g = Gesture();
        Sheet sheet = SheetOf();
        Assert.Greater(sheet.releves.Length, 100, "Garde : la fiche a ses relevés.");
        var failures = new List<string>();
        void Same(string what, float f, float got, float wanted, float tolerance)
        {
            bool none = wanted < 0f;
            if (none ? !float.IsNaN(got) : float.IsNaN(got) || Mathf.Abs(got - wanted) > tolerance)
                failures.Add($"f={f:F3} : {what} vaut {got:F4} dans le jeu, {(none ? "rien" : wanted.ToString("F4"))} dans Blender");
        }
        foreach (Record r in sheet.releves)
        {
            BreakActionGesture.Pose p = g.PoseAt(r.f);
            Same("le canon", r.f, p.barrel, r.canon, 0.01f);
            Same("le chien", r.f, p.hammer, r.chien, 0.01f);
            Same("le levier", r.f, p.latch, r.levier, 0.01f);
            Same("la douille vide", r.f, p.caseSlide, r.douille, 1e-4f);
            Same("la cartouche neuve", r.f, p.cartridgeSlide, r.cartouche, 1e-4f);
        }
        Assert.IsEmpty(failures, string.Join("\n", failures));
        // Garde : le geste se déroule (le canon ouvert pour charger, refermé et armé à la fin).
        Assert.AreEqual(g.barrelAngle, g.PoseAt(0.4f).barrel, 1e-3f);
        Assert.AreEqual(0f, g.PoseAt(1f).barrel, 1e-4f);
        Assert.AreEqual(g.hammerAngle, g.PoseAt(0f).hammer, 1e-4f, "Au début, le chien n'est pas abattu (le coup est parti).");
        Assert.AreEqual(0f, g.PoseAt(1f).hammer, 1e-4f, "À la fin, le chien n'est pas armé.");
    }

    [Test]
    public void LeGeste_EstBrancheDansLAnimatorEtSurLeJoueur()
    {
        BreakActionGesture g = Gesture();
        Assert.AreEqual(MagazineReloadImporter.BreakActionValueOf(Code), g.animatorValue, "Le geste ne choisit pas sa branche de l'arbre.");
        Assert.Greater(g.animatorValue, 0);
        foreach ((string code, int value) in MagazineReloadImporter.Gestures)
            Assert.AreNotEqual(value, g.animatorValue, "Le Big D prend la valeur de GesteRecharge du " + code + ".");
        CollectionAssert.AreEqual(new[] { GoldenGun() }, g.weapons, "Le geste n'est pas celui du Golden Gun.");

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ProneSideCrawl.ControllerPath);
        var tree = MagazineReloadImporter.ReloadState(controller)?.motion as BlendTree;
        Assert.IsNotNull(tree, "L'état Recharge n'a pas d'arbre.");
        BlendTree branch = null;
        foreach (ChildMotion c in tree.children)
            if (Mathf.Approximately(c.threshold, g.animatorValue)) branch = c.motion as BlendTree;
        Assert.IsNotNull(branch, "L'arbre de l'état Recharge n'a pas de branche pour le Big D.");
        Assert.AreEqual("StanceF", branch.blendParameter);
        var clips = new Dictionary<float, string>();
        foreach (ChildMotion c in branch.children) clips[c.threshold] = c.motion != null ? c.motion.name : null;
        Assert.AreEqual(MagazineReloadImporter.ClipName(Code), clips[0f], "Debout, le Big D ne joue pas son geste.");
        Assert.AreEqual(MagazineReloadImporter.ClipName(Code), clips[1f], "Accroupi, le Big D ne joue pas son geste.");
        Assert.AreEqual("Prone Reloading", clips[2f], "Allongé, le Big D garde le geste commun.");

        var player = AssetDatabase.LoadAssetAtPath<GameObject>(MagazineReloadImporter.PlayerPath);
        Assert.AreEqual(g, player.GetComponent<MagazineSwap>().GestureFor(GoldenGun()), "Le joueur ne joue pas le geste du Big D au Golden Gun.");

        var importer = (ModelImporter)AssetImporter.GetAtPath(MagazineReloadImporter.ClipPath(Code));
        Assert.AreEqual(ModelImporterAnimationType.Human, importer.animationType, "Le clip n'est pas humanoïde.");
        Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Avatar>(ProneSideCrawl.AvatarPath), importer.sourceAvatar, "Le clip ne copie pas l'avatar du Y Bot.");
        Assert.IsFalse(importer.clipAnimations[0].loopTime, "Un rechargement ne boucle pas.");
        Assert.AreEqual(Clip().length, g.clipLength, 1e-4f, "Le geste n'a pas la durée du clip : relancer l'import.");
    }

    [Test]
    public void LesSonsDuRechargement_TombentSurLeGeste()
    {
        BreakActionGesture g = Gesture();
        WeaponData w = GoldenGun();
        Assert.AreEqual(3, w.reloadCues.Length, "Garde : les trois étapes sonores du rechargement.");
        Assert.AreEqual(g.openStep.x, w.reloadCues[0].at, 1e-4f, "On n'entend pas le canon s'ouvrir quand il s'ouvre.");
        Assert.AreEqual(g.insertStep.y, w.reloadCues[1].at, 1e-4f, "On n'entend pas la cartouche entrer quand elle entre.");
        Assert.AreEqual(g.closeStep.y, w.reloadCues[2].at, 1e-4f, "On n'entend pas le canon se verrouiller quand il se ferme.");
        Assert.AreEqual(w.reloadDuration, g.reloadSeconds, 1e-4f, "Le geste n'est pas calculé pour la durée du rechargement du Golden Gun.");
        Assert.IsTrue(w.reloadsAfterEachShot, "Garde : le Golden Gun se recharge après chaque tir.");
    }

    // ---------------------------------------------------------------------------------------------------------
    // Le clip, échantillonné sur le robot du jeu
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>La paume du CT-01 (WeaponGrip, repère de l'os de la main gauche) dans le repère de l'arme, et la
    /// rotation de la main dans ce repère, le clip échantillonné à l'instant `time` (s).</summary>
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

    private static void PalmAt(AnimationClip clip, ReloadGesture g, float f, out Vector3 palm, out Quaternion hand)
        => PalmAtTime(clip, g.usefulStart + f * (g.usefulEnd - g.usefulStart), out palm, out hand);

    [Test]
    public void LeClip_PoseLaPaumeSurLeCanonEtLaCartouche_AuxEtapesQuiComptent()
    {
        AnimationClip clip = Clip();
        BreakActionGesture g = Gesture();
        Sheet sheet = SheetOf();
        GameObject go = Object.Instantiate(GoldenGun().modelPrefab);   // le modèle, pour y poser ses pièces
        try
        {
            Transform root = go.transform, barrel = root.Find(g.barrelPath), cartridge = root.Find(g.cartridgePath);
            Assert.IsTrue(barrel != null && cartridge != null, "Garde : le modèle du Golden Gun a le canon et la cartouche du Big D.");
            Vector3 pivot = barrel.localPosition, cartridgeRest = cartridge.localPosition;
            // un repère de la fiche sur le canon (dans le repère de l'arme fermée), le canon où le geste le met à f
            Vector3 OnBarrel(Vector3 p, float f)
            {
                barrel.localRotation = BreakActionGesture.Turn(-g.PoseAt(f).barrel);
                return root.InverseTransformPoint(barrel.TransformPoint(p - pivot));
            }
            // un repère de la fiche sur la cartouche (son culot à l'origine), sortie de la chambre de `slide`
            Vector3 OnCartridge(Vector3 p, float f, float slide)
            {
                barrel.localRotation = BreakActionGesture.Turn(-g.PoseAt(f).barrel);
                cartridge.localPosition = cartridgeRest + Vector3.back * slide;
                return root.InverseTransformPoint(cartridge.TransformPoint(p));
            }
            var failures = new List<string>();
            void Expect(string what, float f, Vector3 wanted)
            {
                PalmAt(clip, g, f, out Vector3 palm, out _);
                float off = Vector3.Distance(palm, wanted);
                Debug.Log($"[RECHARGEMENT] Big D, {what} (f={f:F3}) : paume à {off * 1000f:F1} mm de sa place");
                if (off > 0.008f) failures.Add($"{what} (f={f:F3}) : la paume est à {off * 1000f:F0} mm de sa place {wanted:F3}, à {palm:F3}");
            }
            Expect("sous le canon, qu'elle va lancer", g.openStep.x, OnBarrel(sheet.sous.Point, g.openStep.x));
            Expect("sous le canon, qu'elle lâche", sheet.etapes.lacher, OnBarrel(sheet.sous.Point, sheet.etapes.lacher));
            Expect("la cartouche présentée devant la chambre", g.seated, OnCartridge(sheet.pince.Point, g.seated, g.cartridgeApproach));
            Expect("la cartouche enfoncée", g.insertStep.y, OnCartridge(sheet.pince.Point, g.insertStep.y, 0f));
            Expect("le canon pris pour le refermer", g.closeStep.x - 0.01f, OnBarrel(sheet.flanc.Point, g.closeStep.x - 0.01f));
            Expect("le canon refermé", g.closeStep.y, OnBarrel(sheet.flanc.Point, g.closeStep.y));
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void LeClip_CommenceEtFinitDansLaTenueDesPistolets()
    {
        AnimationClip clip = Clip(), idle = ProneSideCrawl.Clip(PistolIdlePath, "Pistol NoAim Idle");
        Assert.IsNotNull(idle, "Garde : la tenue du pistolet hors visée manque : " + PistolIdlePath);
        BreakActionGesture g = Gesture();
        Sheet sheet = SheetOf();
        PalmAtTime(idle, 0f, out Vector3 expected, out Quaternion hold);
        var failures = new List<string>();
        foreach (float f in new[] { 0f, sheet.etapes.retour, 1f })
        {
            PalmAt(clip, g, f, out Vector3 palm, out Quaternion hand);
            float off = Vector3.Distance(palm, expected), turn = Quaternion.Angle(hand, hold);
            Debug.Log($"[RECHARGEMENT] Big D, tenue des pistolets (f={f:F2}) : paume à {off * 1000f:F1} mm, main tournée de {turn:F1}°");
            if (off > 0.008f) failures.Add($"f={f:F2} : la paume est à {off * 1000f:F0} mm de la tenue des pistolets");
            if (turn > 6f) failures.Add($"f={f:F2} : la main est tournée de {turn:F0}° de la tenue des pistolets");
        }
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }
}
