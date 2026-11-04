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
/// échantillonné sur le robot du jeu (le geste refait le 2026-11-03 d'après le Wingman : la main droite fait tout au
/// poignet), la paume gauche est sur la cartouche quand elle la présente devant la chambre et quand elle la lâche, la
/// cartouche pincée entre le bout du pouce et celui de l'index ; le pouce droit est sur le levier qu'il tient abaissé,
/// puis sur le chien qu'il arme, sans entrer dans l'arme chez aucun châssis, de son départ à son retour ; et la main
/// gauche est dans la tenue des pistolets au début et à la fin.
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
        public Landmark pince;
        public Surface levierAbaisse, chien, chienArme;
        public float rayonBourrelet, epaisseurPouce;
        public Record[] releves;
    }

    [System.Serializable]
    private class Steps
    {
        public float saisie, lacher, retour;
    }

    [System.Serializable]
    private class Landmark
    {
        public float[] paume;
        public Vector3 Point => new Vector3(paume[0], paume[1], paume[2]);
    }

    /// <summary>Une surface que le pouce droit touche (repère de l'arme d'Unity) : son point et sa normale.</summary>
    [System.Serializable]
    private class Surface
    {
        public float[] point, normale;
        public Vector3 Point => new Vector3(point[0], point[1], point[2]);
        public Vector3 Normal => new Vector3(normale[0], normale[1], normale[2]).normalized;
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
        Assert.IsTrue(sheet.pince?.paume != null && sheet.levierAbaisse?.point != null && sheet.chienArme?.point != null
                      && sheet.etapes.lacher > 0f, "La fiche du Big D n'a pas ses repères : réexporter de Blender.");
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
        // (la douille et la cartouche valent -1 quand il n'y en a pas ; un angle peut être un rien négatif)
        void Same(string what, float f, float got, float wanted, float tolerance, bool optional = false)
        {
            bool none = optional && wanted < 0f;
            if (none ? !float.IsNaN(got) : float.IsNaN(got) || Mathf.Abs(got - wanted) > tolerance)
                failures.Add($"f={f:F3} : {what} vaut {got:F4} dans le jeu, {(none ? "rien" : wanted.ToString("F4"))} dans Blender");
        }
        foreach (Record r in sheet.releves)
        {
            BreakActionGesture.Pose p = g.PoseAt(r.f);
            Same("le canon", r.f, p.barrel, r.canon, 0.01f);
            Same("le chien", r.f, p.hammer, r.chien, 0.01f);
            Same("le levier", r.f, p.latch, r.levier, 0.01f);
            Same("la douille vide", r.f, p.caseSlide, r.douille, 1e-4f, true);
            Same("la cartouche neuve", r.f, p.cartridgeSlide, r.cartouche, 1e-4f, true);
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

    /// <summary>Ce que le clip met dans le repère de l'arme (la prise de la main droite) : la paume gauche du CT-01
    /// (WeaponGrip, repère de l'os de la main) et la rotation de la main gauche, les bouts du pouce et de l'index gauches
    /// et celui du pouce droit (les os du bout des doigts du Y Bot, au bout de la dernière phalange).</summary>
    private struct Hands
    {
        public Vector3 palm, leftThumb, leftIndex, rightThumb;
        public Quaternion hand;
    }

    /// <summary>Le clip échantillonné sur le robot du jeu à l'instant `time` (s), le gant ôté (il replie la main
    /// gauche, ses doigts avec) ; `thumb` : les phalanges du pouce droit reposées ensuite, comme le fait le jeu
    /// (BreakActionView), ou celles du clip.</summary>
    private static Hands HandsAtTime(AnimationClip clip, float time, Quaternion[] thumb = null)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(MagazineReloadImporter.PlayerPath);
        try
        {
            Transform model = root.transform.Find("Model");
            Animator animator = model.GetComponent<Animator>();
            HeldWeapons.WearTaserGlove(animator, false);
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform anchor = animator.GetBoneTransform(HumanBodyBones.RightHand).Find(HeldWeapons.GripAnchorName);
            Vector3 point = root.GetComponent<PlayerAnimator>().HandGrip.PalmPoint(ChassisId.CT01);
            model.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clip.SampleAnimation(model.gameObject, time);
            if (thumb != null)
            {
                animator.GetBoneTransform(HumanBodyBones.RightThumbProximal).localRotation = thumb[0];
                animator.GetBoneTransform(HumanBodyBones.RightThumbIntermediate).localRotation = thumb[1];
                animator.GetBoneTransform(HumanBodyBones.RightThumbDistal).localRotation = thumb[2];
            }
            Quaternion toWeapon = Quaternion.Inverse(anchor.rotation);
            Vector3 InWeapon(Vector3 world) => toWeapon * (world - anchor.position);
            Vector3 Tip(HumanBodyBones distal) => InWeapon(animator.GetBoneTransform(distal).GetChild(0).position);
            return new Hands
            {
                palm = InWeapon(left.position + left.rotation * point),
                hand = toWeapon * left.rotation,
                leftThumb = Tip(HumanBodyBones.LeftThumbDistal),
                leftIndex = Tip(HumanBodyBones.LeftIndexDistal),
                rightThumb = Tip(HumanBodyBones.RightThumbDistal),
            };
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Hands HandsAt(AnimationClip clip, ReloadGesture g, float f, Quaternion[] thumb = null)
        => HandsAtTime(clip, g.usefulStart + f * (g.usefulEnd - g.usefulStart), thumb);

    /// <summary>Les phalanges du pouce droit que le jeu pose à la part `f` (BreakActionGesture.ThumbAt).</summary>
    private static Quaternion[] ThumbOf(BreakActionGesture g, float f)
    {
        var q = new Quaternion[BreakActionGesture.ThumbBones];
        Assert.IsTrue(g.ThumbAt(f, q), "Le geste du Big D n'a pas les poses du pouce droit : relancer l'import.");
        return q;
    }

    /// <summary>Le modèle du Big D, ses pièces posées comme le geste les met à la part `f`, la cartouche sortie de la
    /// chambre de `slide` ; `use` reçoit sa racine et sa cartouche (son culot à l'origine).</summary>
    private static void WithModelAt(BreakActionGesture g, float f, float slide, System.Action<Transform, Transform> use)
    {
        GameObject go = Object.Instantiate(GoldenGun().modelPrefab);
        try
        {
            Transform root = go.transform, barrel = root.Find(g.barrelPath), cartridge = root.Find(g.cartridgePath);
            Assert.IsTrue(barrel != null && cartridge != null, "Garde : le modèle du Golden Gun a le canon et la cartouche du Big D.");
            barrel.localRotation = BreakActionGesture.Turn(-g.PoseAt(f).barrel);
            cartridge.localPosition += Vector3.back * slide;
            use(root, cartridge);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void LeClip_PoseLaPaumeSurLaCartouche_QuandElleLaPresenteEtQuandElleLaLache()
    {
        AnimationClip clip = Clip();
        BreakActionGesture g = Gesture();
        Sheet sheet = SheetOf();
        var failures = new List<string>();
        void Expect(string what, float f, float slide)
        {
            Hands h = HandsAt(clip, g, f);
            WithModelAt(g, f, slide, (root, cartridge) =>
            {
                Vector3 wanted = root.InverseTransformPoint(cartridge.TransformPoint(sheet.pince.Point));
                float off = Vector3.Distance(h.palm, wanted);
                Debug.Log($"[RECHARGEMENT] Big D, {what} (f={f:F3}) : paume à {off * 1000f:F1} mm de sa place");
                if (off > 0.008f) failures.Add($"{what} (f={f:F3}) : la paume est à {off * 1000f:F0} mm de sa place {wanted:F3}, à {h.palm:F3}");
            });
        }
        Assert.That(sheet.etapes.lacher, Is.InRange(g.seated + 0.01f, g.insertStep.y), "Garde : la main lâche la cartouche pendant qu'elle entre.");
        Expect("la cartouche présentée devant la chambre", g.seated, g.cartridgeApproach);
        Expect("la cartouche poussée, au moment de la lâcher", sheet.etapes.lacher, g.PoseAt(sheet.etapes.lacher).cartridgeSlide);
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    [Test]
    public void LeClip_PinceLaCartouche_EntreLeBoutDuPouceEtCeluiDeLIndex()
    {
        AnimationClip clip = Clip();
        BreakActionGesture g = Gesture();
        Sheet sheet = SheetOf();
        var failures = new List<string>();
        foreach (float f in new[] { g.seated, sheet.etapes.lacher - 0.01f })
        {
            Hands h = HandsAt(clip, g, f);
            WithModelAt(g, f, g.PoseAt(f).cartridgeSlide, (root, cartridge) =>
            {
                // dans le repère de la cartouche : son axe est z, son culot (le bourrelet) à l'origine
                Vector3 thumb = cartridge.InverseTransformPoint(root.TransformPoint(h.leftThumb));
                Vector3 index = cartridge.InverseTransformPoint(root.TransformPoint(h.leftIndex));
                Vector2 rt = new Vector2(thumb.x, thumb.y), ri = new Vector2(index.x, index.y);
                float apart = Vector2.Angle(rt, ri);
                Debug.Log($"[RECHARGEMENT] Big D, la pince (f={f:F3}) : pouce à {(rt.magnitude - sheet.rayonBourrelet) * 1000f:F1} mm du bourrelet "
                          + $"(z {thumb.z * 1000f:F1} mm), index à {(ri.magnitude - sheet.rayonBourrelet) * 1000f:F1} mm (z {index.z * 1000f:F1} mm), {apart:F0}° l'un de l'autre");
                foreach ((string who, Vector3 tip, Vector2 r) in new[] { ("le pouce", thumb, rt), ("l'index", index, ri) })
                {
                    if (r.magnitude < sheet.rayonBourrelet || r.magnitude > sheet.rayonBourrelet + 0.012f)
                        failures.Add($"f={f:F3} : le bout de {who} est à {(r.magnitude - sheet.rayonBourrelet) * 1000f:F0} mm du bourrelet (0 à 12)");
                    if (tip.z < -0.008f || tip.z > 0.012f)
                        failures.Add($"f={f:F3} : le bout de {who} n'est pas à la hauteur du bourrelet ({tip.z * 1000f:F0} mm le long de la cartouche)");
                }
                if (apart < 140f)
                    failures.Add($"f={f:F3} : le pouce et l'index ne la prennent pas de part et d'autre ({apart:F0}°)");
            });
        }
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    /// <summary>Des points sur les triangles `tri` (trois sommets par triangle), tous les `step` mètres au plus : les
    /// sommets seuls d'un bloc (les pièces du CT-01) laisseraient une face toucher le levier sans qu'aucun n'en soit près.</summary>
    private static List<Vector3> Samples(List<Vector3> tri, float step)
    {
        var points = new List<Vector3>();
        for (int i = 0; i < tri.Count; i += 3)
        {
            Vector3 a = tri[i], b = tri[i + 1], c = tri[i + 2];
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max((b - a).magnitude, (c - a).magnitude, (c - b).magnitude) / step));
            for (int u = 0; u <= n; u++)
                for (int v = 0; u + v <= n; v++)
                    points.Add(a + (b - a) * ((float)u / n) + (c - a) * ((float)v / n));
        }
        return points;
    }

    /// <summary>Le pouce droit de chaque châssis contre le Big D, aux parts `fs` du geste, posé comme le jeu le pose (la
    /// tenue, puis les poses du pouce fondues par ThumbWeight, BreakActionView), l'arme posée comme le geste la met : ce
    /// que le pouce enfonce dans l'arme (par châssis : au plus, os, pièce et part) ; et, aux parts de `touched`, à quelle
    /// distance les faces de la dernière phalange du CT-01 sont de la pièce donnée.</summary>
    private static (Dictionary<string, (float depth, string where)> depth, Dictionary<float, float> contact) ThumbAgainstBigD(
        AnimationClip clip, BreakActionGesture g, IEnumerable<float> fs, Dictionary<float, string> touched)
    {
        var depth = new Dictionary<string, (float, string)>();
        var contact = new Dictionary<float, float>();
        var thumb = new Quaternion[BreakActionGesture.ThumbBones];
        GameObject root = PrefabUtility.LoadPrefabContents(MagazineReloadImporter.PlayerPath);
        try
        {
            Transform model = root.transform.Find("Model");
            Animator animator = model.GetComponent<Animator>();
            HeldWeapons.WearTaserGlove(animator, false);
            model.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Transform anchor = animator.GetBoneTransform(HumanBodyBones.RightHand).Find(HeldWeapons.GripAnchorName);
            foreach (float f in fs)
            {
                clip.SampleAnimation(model.gameObject, g.usefulStart + f * (g.usefulEnd - g.usefulStart));
                for (int b = 0; b < BreakActionGesture.HoldOrder.Length; b++)
                    animator.GetBoneTransform(BreakActionGesture.HoldOrder[b]).localRotation = g.rightHold[b];
                if (g.ThumbAt(f, thumb))
                {
                    float w = BreakActionGesture.ThumbWeight(f);
                    for (int b = 0; b < thumb.Length; b++)
                    {
                        Transform t = animator.GetBoneTransform(BreakActionGesture.HoldOrder[b]);
                        t.localRotation = Quaternion.Slerp(t.localRotation, thumb[b], w);
                    }
                }
                GameObject gun = Object.Instantiate(GoldenGun().modelPrefab);
                try
                {
                    gun.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
                    BreakActionGesture.Pose pose = g.PoseAt(f);
                    gun.transform.Find(g.barrelPath).localRotation *= BreakActionGesture.Turn(-pose.barrel);
                    gun.transform.Find(g.hammerPath).localRotation *= BreakActionGesture.Turn(pose.hammer);
                    gun.transform.Find(g.latchPath).localRotation *= BreakActionGesture.Turn(-pose.latch);
                    List<Solid> solids = SolidsOf(gun);
                    foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (smr.name != "CT01" && smr.name != "OS02" && smr.name != "Alpha_Surface") continue;
                        var mesh = new Mesh();
                        smr.BakeMesh(mesh, false);
                        Vector3[] verts = mesh.vertices;
                        Object.DestroyImmediate(mesh);
                        BoneWeight[] weights = smr.sharedMesh.boneWeights;
                        Transform[] bones = smr.bones;
                        string Bone(int i) => bones[weights[i].boneIndex0].name.Replace("mixamorig:", "");
                        if (!depth.ContainsKey(smr.name)) depth[smr.name] = (0f, "");
                        for (int i = 0; i < verts.Length; i++)
                        {
                            if (!Bone(i).StartsWith("RightHandThumb")) continue;
                            Vector3 p = smr.transform.TransformPoint(verts[i]);
                            foreach (Solid s in solids)
                            {
                                float d = Depth(s, p);
                                if (d > depth[smr.name].Item1) depth[smr.name] = (d, $"{Bone(i)} dans {s.name}, f={f:F3}");
                            }
                        }
                        if (smr.name != "CT01" || !touched.TryGetValue(f, out string piece)) continue;
                        Solid target = solids.Find(s => s.name == piece);
                        Assert.IsNotNull(target, "Garde : le Big D a la pièce " + piece);
                        int[] tris = smr.sharedMesh.triangles;
                        var faces = new List<Vector3>();
                        for (int k = 0; k < tris.Length; k += 3)
                            if (Bone(tris[k]) == "RightHandThumb3")
                                for (int j = 0; j < 3; j++) faces.Add(smr.transform.TransformPoint(verts[tris[k + j]]));
                        Assert.Greater(faces.Count, 0, "Garde : le CT-01 a la dernière phalange de son pouce droit.");
                        float best = float.MaxValue;
                        foreach (Vector3 p in Samples(faces, 0.002f)) best = Mathf.Min(best, Depth(target, p) > 0f ? 0f : Distance(target, p));
                        contact[f] = best;
                    }
                }
                finally
                {
                    Object.DestroyImmediate(gun);
                }
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return (depth, contact);
    }

    [Test]
    public void LePouceDroit_SurLeLevierQuIlTient_PuisSurLeChienQuIlArme_SansEntrerDansLArme()
    {
        AnimationClip clip = Clip();
        BreakActionGesture g = Gesture();
        // au milieu de l'appui, le levier est tout à fait abaissé
        float held = g.latchStep.x + (g.latchPress + 0.5f * (1f - g.latchPress - g.latchRelease)) * (g.latchStep.y - g.latchStep.x);
        Assert.AreEqual(g.latchAngle, g.PoseAt(held).latch, 1e-4f, "Garde : le levier est abaissé au milieu de l'appui.");
        var touched = new Dictionary<float, string> { { held, "Levier_Corps" }, { g.cockStep.x, "Chien_Corps" }, { g.cockStep.y, "Chien_Corps" } };
        // tout le travail du pouce : du repos au levier et retour, puis du repos au chien, par le dehors, et retour
        var fs = new List<float>(touched.Keys);
        for (float f = 0f; f <= g.latchStep.y + 0.07f; f += 0.0125f) fs.Add(f);
        for (float f = g.cockStep.x - 0.10f; f <= Mathf.Min(1f, g.cockStep.y + 0.11f); f += 0.0125f) fs.Add(f);
        var (depth, contact) = ThumbAgainstBigD(clip, g, fs, touched);
        var failures = new List<string>();
        foreach (var kv in depth)
        {
            Debug.Log($"[RECHARGEMENT] Big D, le pouce droit de {kv.Key} au travail ({fs.Count} parts) : enfoncé de {kv.Value.depth * 1000f:F1} mm au plus ({kv.Value.where})");
            // la peau continue du prototype se plie aux jointures : un peu plus de jeu que les pièces rigides
            float allowed = kv.Key == "Alpha_Surface" ? 0.004f : 0.0025f;
            if (kv.Value.depth > allowed)
                failures.Add($"{kv.Key} : le pouce droit entre de {kv.Value.depth * 1000f:F1} mm dans l'arme ({kv.Value.where})");
        }
        Assert.AreEqual(3, depth.Count, "Garde : le pouce des trois châssis a été mesuré.");
        foreach ((string what, float f) in new[] { ("sur le levier qu'il tient abaissé", held), ("sur le chien, avant de l'armer", g.cockStep.x), ("sur le chien armé", g.cockStep.y) })
        {
            Assert.AreEqual(1f, BreakActionGesture.ThumbWeight(f), 1e-4f, "Garde : le jeu pose tout à fait le pouce à cette étape.");
            Debug.Log($"[RECHARGEMENT] Big D, le pouce droit du CT-01 {what} (f={f:F3}) : à {contact[f] * 1000f:F1} mm de la pièce");
            if (contact[f] > 0.002f) failures.Add($"le pouce droit du CT-01 {what} (f={f:F3}) : à {contact[f] * 1000f:F1} mm de la pièce");
        }
        // Pendant le coup de poignet qui referme l'arme, il reste couché sur la carcasse : bougeant à côté du canon, il
        // paraissait le rabattre (retour de l'utilisateur, 2026-11-03).
        Quaternion[] before = ThumbOf(g, g.closeStep.x - 0.03f), after = ThumbOf(g, g.closeStep.y);
        for (int b = 0; b < BreakActionGesture.ThumbBones; b++)
        {
            float turn = Quaternion.Angle(before[b], after[b]);
            if (turn > 0.5f) failures.Add($"pendant le coup de poignet qui referme l'arme, la phalange {b + 1} du pouce droit tourne de {turn:F1}°");
        }
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    [Test]
    public void LeClip_CommenceEtFinitDansLaTenueDesPistolets()
    {
        AnimationClip clip = Clip(), idle = ProneSideCrawl.Clip(PistolIdlePath, "Pistol NoAim Idle");
        Assert.IsNotNull(idle, "Garde : la tenue du pistolet hors visée manque : " + PistolIdlePath);
        BreakActionGesture g = Gesture();
        Sheet sheet = SheetOf();
        Hands idleHands = HandsAtTime(idle, 0f);
        Vector3 expected = idleHands.palm;
        Quaternion hold = idleHands.hand;
        var failures = new List<string>();
        foreach (float f in new[] { 0f, sheet.etapes.retour, 1f })
        {
            Hands h = HandsAt(clip, g, f);
            Vector3 palm = h.palm;
            Quaternion hand = h.hand;
            float off = Vector3.Distance(palm, expected), turn = Quaternion.Angle(hand, hold);
            Debug.Log($"[RECHARGEMENT] Big D, tenue des pistolets (f={f:F2}) : paume à {off * 1000f:F1} mm, main tournée de {turn:F1}°");
            if (off > 0.008f) failures.Add($"f={f:F2} : la paume est à {off * 1000f:F0} mm de la tenue des pistolets");
            if (turn > 6f) failures.Add($"f={f:F2} : la main est tournée de {turn:F0}° de la tenue des pistolets");
        }
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    // --- la tenue de la main droite (tenue_golden_gun.py, 2026-11-03) ---------------------------------------------

    /// <summary>La prise des pistolets en visée, celle des bras de la vue subjective.</summary>
    private const string PistolAimPath = "Assets/_ProjectArt/Animations/New/Y Bot@Pistol Aim Idle.fbx";

    /// <summary>Une pièce fermée du Big D, ses triangles dans le monde (trois sommets par triangle).</summary>
    private sealed class Solid
    {
        public string name;
        public Bounds bounds;
        public Vector3[] tri;
    }

    private static List<Solid> SolidsOf(GameObject gun)
    {
        var solids = new List<Solid>();
        foreach (MeshFilter mf in gun.GetComponentsInChildren<MeshFilter>(true))
        {
            string n = mf.name;
            if (mf.sharedMesh == null || n.Contains("Gravure") || n.Contains("Marquages") || n.Contains("Douille") || n.Contains("Balle")) continue;
            Vector3[] v = mf.sharedMesh.vertices;
            int[] t = mf.sharedMesh.triangles;
            var tri = new Vector3[t.Length];
            var b = new Bounds(mf.transform.TransformPoint(v[t[0]]), Vector3.zero);
            for (int i = 0; i < t.Length; i++)
            {
                tri[i] = mf.transform.TransformPoint(v[t[i]]);
                b.Encapsulate(tri[i]);
            }
            solids.Add(new Solid { name = n, bounds = b, tri = tri });
        }
        return solids;
    }

    private static readonly Vector3[] Rays =
    {
        new Vector3(0.31f, 0.82f, 0.48f).normalized, new Vector3(-0.66f, 0.27f, -0.70f).normalized,
        new Vector3(0.12f, -0.55f, 0.83f).normalized,
    };

    /// <summary>De combien un point est dans une pièce (m, sa distance à la surface), 0 dehors : il est dedans si deux
    /// rayons sur trois traversent la pièce un nombre impair de fois.</summary>
    private static float Depth(Solid s, Vector3 p)
    {
        if (!s.bounds.Contains(p)) return 0f;
        int votes = 0;
        foreach (Vector3 d in Rays)
        {
            int hits = 0;
            for (int i = 0; i < s.tri.Length; i += 3)
                if (RayHits(p, d, s.tri[i], s.tri[i + 1], s.tri[i + 2])) hits++;
            votes += hits & 1;
        }
        return votes < 2 ? 0f : Distance(s, p);
    }

    private static float Distance(Solid s, Vector3 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i < s.tri.Length; i += 3)
            best = Mathf.Min(best, (ClosestOnTriangle(p, s.tri[i], s.tri[i + 1], s.tri[i + 2]) - p).magnitude);
        return best;
    }

    private static bool RayHits(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 e1 = b - a, e2 = c - a, h = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, h);
        if (Mathf.Abs(det) < 1e-12f) return false;
        float f = 1f / det;
        Vector3 s = o - a;
        float u = f * Vector3.Dot(s, h);
        if (u < 0f || u > 1f) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float v = f * Vector3.Dot(d, q);
        return v >= 0f && u + v <= 1f && f * Vector3.Dot(e2, q) > 1e-7f;
    }

    private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    /// <summary>Ce que les doigts droits de chaque châssis enfoncent dans le Big D, dans la prise des pistolets en
    /// visée, le Big D en main, la tenue posée ou non ; et, la tenue posée, à quelle distance de l'arme est le bout de
    /// chacun des trois derniers doigts (son point le plus près). Par châssis : (enfoncé au plus, os qui l'enfonce,
    /// distances des bouts).</summary>
    private static Dictionary<string, (float depth, string bone, float[] tips)> FingersInBigD(bool hold)
    {
        BreakActionGesture g = Gesture();
        AnimationClip aim = ProneSideCrawl.Clip(PistolAimPath, "Pistol Aim Idle");
        Assert.IsNotNull(aim, "Garde : la prise des pistolets en visée : " + PistolAimPath);
        var result = new Dictionary<string, (float, string, float[])>();
        GameObject root = PrefabUtility.LoadPrefabContents(MagazineReloadImporter.PlayerPath);
        GameObject gun = null;
        try
        {
            Transform model = root.transform.Find("Model");
            Animator animator = model.GetComponent<Animator>();
            HeldWeapons.WearTaserGlove(animator, false);
            model.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            aim.SampleAnimation(model.gameObject, 0f);
            if (hold)
                for (int b = 0; b < BreakActionGesture.HoldOrder.Length; b++)
                    animator.GetBoneTransform(BreakActionGesture.HoldOrder[b]).localRotation = g.rightHold[b];
            Transform anchor = animator.GetBoneTransform(HumanBodyBones.RightHand).Find(HeldWeapons.GripAnchorName);
            gun = Object.Instantiate(GoldenGun().modelPrefab);
            gun.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            List<Solid> solids = SolidsOf(gun);
            string[] tipBones = { "RightHandMiddle3", "RightHandRing3", "RightHandPinky3" };
            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.name != "CT01" && smr.name != "OS02" && smr.name != "Alpha_Surface") continue;
                var mesh = new Mesh();
                smr.BakeMesh(mesh, false);
                Vector3[] verts = mesh.vertices;
                Object.DestroyImmediate(mesh);
                BoneWeight[] weights = smr.sharedMesh.boneWeights;
                Transform[] bones = smr.bones;
                float worst = 0f;
                string worstBone = "";
                var tips = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
                for (int i = 0; i < verts.Length; i++)
                {
                    string bone = bones[weights[i].boneIndex0].name.Replace("mixamorig:", "");
                    if (!bone.StartsWith("RightHand") || bone == "RightHand") continue;
                    Vector3 p = smr.transform.TransformPoint(verts[i]);
                    foreach (Solid s in solids)
                    {
                        float d = Depth(s, p);
                        if (d > worst)
                        {
                            worst = d;
                            worstBone = bone + " dans " + s.name;
                        }
                    }
                    int k = System.Array.IndexOf(tipBones, bone);
                    if (hold && k >= 0)
                        foreach (Solid s in solids)
                            if (s.bounds.SqrDistance(p) < tips[k] * tips[k] || tips[k] == float.MaxValue)
                                tips[k] = Mathf.Min(tips[k], Depth(s, p) > 0f ? 0f : Distance(s, p));
                }
                result[smr.name] = (worst, worstBone, tips);
            }
        }
        finally
        {
            if (gun != null) Object.DestroyImmediate(gun);
            PrefabUtility.UnloadPrefabContents(root);
        }
        return result;
    }

    [Test]
    public void LaTenue_EnveloppeLaCrosse_SansQueLesDoigtsYEntrent_SurChaqueChassis()
    {
        BreakActionGesture g = Gesture();
        Assert.IsTrue(g.HasHold, "Le geste du Big D n'a pas la tenue de la main droite : relancer l'import des rechargements.");
        var clip = FingersInBigD(false);
        var held = FingersInBigD(true);
        var failures = new List<string>();
        foreach (var kv in held)
        {
            var (depth, bone, tips) = kv.Value;
            var (clipDepth, clipBone, _) = clip[kv.Key];
            Debug.Log($"[RECHARGEMENT] Big D, la tenue sur {kv.Key} : les doigts enfoncés de {depth * 1000f:F1} mm au plus ({bone}), "
                      + $"le clip seul de {clipDepth * 1000f:F1} mm ({clipBone}) ; bouts du majeur, de l'annulaire et de l'auriculaire à "
                      + $"{tips[0] * 1000f:F1}, {tips[1] * 1000f:F1} et {tips[2] * 1000f:F1} mm de l'arme");
            // la peau continue du prototype se plie aux jointures : un peu plus de jeu que les pièces rigides
            float allowed = kv.Key == "Alpha_Surface" ? 0.004f : 0.0025f;
            if (depth > allowed)
                failures.Add($"{kv.Key} : la tenue enfonce encore les doigts de {depth * 1000f:F1} mm ({bone})");
            for (int k = 0; k < tips.Length; k++)
                if (kv.Key == "CT01" && tips[k] > 0.004f)
                    failures.Add($"{kv.Key} : le bout du doigt {k + 3} ne touche pas l'arme ({tips[k] * 1000f:F1} mm)");
        }
        Assert.Greater(clip["CT01"].depth, 0.010f, "Garde : dans la prise du clip, les doigts du CT-01 entrent dans la crosse (c'est ce que la tenue corrige).");
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }
}
