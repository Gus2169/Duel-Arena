using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Les rechargements où le chargeur quitte l'arme (2026-10-29) : pour chaque arme à chargeur qui a le sien (Gestures),
/// un clip calculé dans Blender sur le squelette du Y Bot (Tools/Blender/animations/rechargement_chargeur.py) et sa
/// fiche (les étapes, en part du rechargement, et les repères que la main touche), et leur place dans le jeu.
///
/// Duel Arena > Animations > Importer les rechargements à chargeur :
/// - règle l'import de chaque FBX (humanoïde, l'avatar du Y Bot copié, la racine comme Y Bot@Reloading, sans boucle) ;
/// - fait choisir son geste à l'état Recharge de la couche du haut du corps : son arbre devient un arbre sur
///   GesteRecharge, qui garde l'arbre d'avant à 0 (Y Bot@Reloading, Crouch Reload, Prone Reloading) et met à la valeur
///   de chaque arme son arbre à elle (son clip debout et accroupi ; allongé, le geste commun, qui garde le chargeur) ;
/// - remplit le geste de chaque arme (ReloadGesture, à côté des armes) : ses étapes, et la main gauche dans le repère
///   de l'arme quand le chargeur neuf entre, mesurée sur le clip importé, sur le Y Bot (le chargeur suit cette main) ;
/// - pose MagazineSwap sur le préfab joueur, avec ces gestes.
/// On réexporte de Blender et on relance le menu ; on ne retouche ni les clips, ni l'arbre, ni les gestes à la main.
///
/// Les armes qui s'ouvrent (2026-11-03, BreakActions : le Big D du Golden Gun, Tools/Blender/animations/
/// rechargement_golden_gun.py) passent par le même chemin : leur clip est importé et branché de même, et leur geste
/// (BreakActionGesture) reprend de leur fiche les étapes de l'arme et ce qui y bouge.
/// </summary>
public static class MagazineReloadImporter
{
    public const string ClipFolder = "Assets/_ProjectArt/Animations/Blender/";
    public const string CommonClipPath = "Assets/_ProjectArt/Animations/Y Bot@Reloading.fbx";
    public const string GestureFolder = "Assets/_ProjectArena/Weapon/";
    public const string PlayerPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    public const string GestureTreeName = "RechargeSelonGeste";

    /// <summary>
    /// Les armes qui ont leur geste (leur désignation, WeaponData.weaponName), et la valeur de GesteRecharge qui le fait
    /// jouer. Une valeur donnée ne change plus (l'Animator et les gestes la gardent) : une arme s'ajoute à la fin, avec
    /// sa place dans rechargement_chargeur.ARMES. Une arme dont le clip n'est pas encore exporté est passée.
    /// </summary>
    public static readonly (string code, int value)[] Gestures = { ("FA-07", 1), ("FA-30", 2), ("FP-12", 3), ("PA-05", 4) };

    /// <summary>
    /// Les armes qui s'ouvrent (BreakActionGesture : la désignation de leur modèle, WeaponModel.Designation), et la valeur
    /// de GesteRecharge qui fait jouer leur geste, à la suite de celles de Gestures (elles ne changent plus).
    /// </summary>
    public static readonly (string code, int value)[] BreakActions = { ("BIG-D", 5) };

    /// <summary>
    /// La vue subjective pendant l'armement (ReloadGesture.armingViewPitchMax, armingViewYaw) : la levée de plus au
    /// plus (degrés), et la rotation vers la gauche. La levée elle-même se calcule en jeu (ReloadGesture.ArmingViewPitch),
    /// elle dépend du viseur ; la règle a été cherchée hors ligne sur des relevés du stand (2026-10-29, le CT-01, chaque
    /// viseur de chaque fusil) : la pièce et la main dans le champ tout l'armement, le buste et l'épaule droite sous 5 %
    /// de l'écran. La poignée du FA-07, sous l'œil, demande de tourner un peu vers la gauche. Le pistolet a son propre
    /// cadrage (FirstPersonView.pistolReloadView…), où sa glissière se voit déjà.
    /// </summary>
    public static readonly (string code, float pitchMax, float yaw)[] ArmingViews = { ("FA-07", 35f, 6f), ("FA-30", 35f, 0f), ("FP-12", 35f, 0f) };

    /// <summary>Le cadrage de l'armement d'une arme (rien si la table n'en a pas).</summary>
    public static (float pitchMax, float yaw) ArmingViewOf(string code)
    {
        foreach ((string c, float pitchMax, float yaw) in ArmingViews)
            if (c == code) return (pitchMax, yaw);
        return (0f, 0f);
    }

    public static string ClipName(string code) => "Rechargement " + code;
    public static string ClipPath(string code) => ClipFolder + "Y Bot@Rechargement " + code + ".fbx";
    public static string SheetPath(string code) => ClipFolder + "Y Bot@Rechargement " + code + ".json";
    public static string GesturePath(string code) => GestureFolder + "Rechargement_" + code.Replace("-", "") + ".asset";
    public static string TreeName(string code) => "Recharge" + code.Replace("-", "");

    /// <summary>La valeur de GesteRecharge du geste d'une arme, ou 0 si elle n'en a pas.</summary>
    public static int ValueOf(string code)
    {
        foreach ((string c, int v) in Gestures)
            if (c == code) return v;
        return 0;
    }

    /// <summary>Les armes de la table dont le clip est exporté.</summary>
    public static List<string> Exported()
    {
        var codes = new List<string>();
        foreach ((string code, int _) in Gestures)
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(SheetPath(code)) != null) codes.Add(code);
        return codes;
    }

    /// <summary>Les armes qui s'ouvrent dont le clip est exporté.</summary>
    public static List<string> ExportedBreakActions()
    {
        var codes = new List<string>();
        foreach ((string code, int _) in BreakActions)
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(SheetPath(code)) != null) codes.Add(code);
        return codes;
    }

    /// <summary>La valeur de GesteRecharge du geste d'une arme qui s'ouvre, ou 0.</summary>
    public static int BreakActionValueOf(string code)
    {
        foreach ((string c, int v) in BreakActions)
            if (c == code) return v;
        return 0;
    }

    [System.Serializable]
    private class Sheet
    {
        public string clip;
        public float duree;
        public float[] utile;
        public Steps etapes;
        public float[] paumeConception;
        public Arming pieceArmement;
        public Mark levier;
    }

    /// <summary>Un repère de la fiche : la paume (point, dans le repère de l'arme d'Unity).</summary>
    [System.Serializable]
    private class Mark
    {
        public float[] paume;
    }

    [System.Serializable]
    private class Steps
    {
        public float sortie, saisie, approche, insertion, frappe, priseLevier, armement, retour;
    }

    /// <summary>Les pièces d'armement que la main fait reculer, leur course et ses instants (rechargement_chargeur.fiche).</summary>
    [System.Serializable]
    private class Arming
    {
        public string[] pieces;
        public float course, prise, tiree, lachee, revenue;
    }

    /// <summary>La fiche d'une arme qui s'ouvre (rechargement_golden_gun.fiche).</summary>
    [System.Serializable]
    private class BreakSheet
    {
        public string clip;
        public float duree;
        public float[] utile;
        public BreakSteps etapes;
        public float[] paumeConception;
        public float angleCanon, angleChien, angleLevier, sortieDouille, approcheCartouche, dureeRechargement;
    }

    [System.Serializable]
    private class BreakSteps
    {
        public float[] levier, ouverture, ejection, insertion, fermeture, armement;
        public float saisie, retour;
    }

    private sealed class Built
    {
        public string code;
        public int value;
        public Sheet sheet;
        public BreakSheet breakSheet;   // une arme qui s'ouvre
        public AnimationClip clip;
        public ReloadGesture gesture;
    }

    [MenuItem("Duel Arena/Animations/Importer les rechargements à chargeur")]
    public static void Build()
    {
        var built = new List<Built>();
        foreach (string code in Exported())
        {
            Sheet sheet = ReadSheet(code);
            if (sheet == null || !ConfigureImport(code)) return;
            AnimationClip clip = ProneSideCrawl.Clip(ClipPath(code), ClipName(code));
            if (clip == null)
            {
                Debug.LogError("[Rechargement] Le clip « " + ClipName(code) + " » manque dans " + ClipPath(code) + ".");
                return;
            }
            built.Add(new Built { code = code, value = ValueOf(code), sheet = sheet, clip = clip });
        }
        foreach (string code in ExportedBreakActions())
        {
            BreakSheet sheet = ReadBreakSheet(code);
            if (sheet == null || !ConfigureImport(code)) return;
            AnimationClip clip = ProneSideCrawl.Clip(ClipPath(code), ClipName(code));
            if (clip == null)
            {
                Debug.LogError("[Rechargement] Le clip « " + ClipName(code) + " » manque dans " + ClipPath(code) + ".");
                return;
            }
            built.Add(new Built { code = code, value = BreakActionValueOf(code), breakSheet = sheet, clip = clip });
        }
        if (built.Count == 0)
        {
            Debug.LogError("[Rechargement] Aucun clip exporté dans " + ClipFolder + ". Exporter d'abord depuis Blender.");
            return;
        }
        if (!WireAnimator(built)) return;
        foreach (Built b in built)
        {
            b.gesture = b.breakSheet != null ? FillBreakAction(b) : FillGesture(b);
            if (b.gesture == null) return;
        }
        PutOnPlayer(built);
        AssetDatabase.SaveAssets();
        foreach (Built b in built)
        {
            ReloadGesture g = b.gesture;
            if (g is BreakActionGesture o)
                Debug.Log($"[Rechargement] Le {b.code} recharge en s'ouvrant (geste {b.value}) : canon ouvert de {o.openStep.x * 100f:F0} à {o.openStep.y * 100f:F0} %, douille partie à {o.drop * 100f:F0} %, cartouche en main à {o.grab * 100f:F0} %, devant la chambre à {o.seated * 100f:F0} %, canon refermé à {o.closeStep.y * 100f:F0} % ; le repère de l'arme à {g.weaponInHandPosition:F3} de la main gauche.");
            else
                Debug.Log($"[Rechargement] Le {b.code} recharge en changeant de chargeur (geste {b.value}) : chute à {g.drop * 100f:F0} %, saisie à {g.grab * 100f:F0} %, en place à {g.seated * 100f:F0} % ; le repère de l'arme à {g.weaponInHandPosition:F3} de la main gauche.");
        }
    }

    private static BreakSheet ReadBreakSheet(string code)
    {
        var text = AssetDatabase.LoadAssetAtPath<TextAsset>(SheetPath(code));
        BreakSheet sheet = text != null ? JsonUtility.FromJson<BreakSheet>(text.text) : null;
        BreakSteps e = sheet?.etapes;
        if (sheet == null || sheet.utile == null || sheet.utile.Length != 2 || e == null || !Pair(e.levier) || !Pair(e.ouverture)
            || !Pair(e.ejection) || !Pair(e.insertion) || !Pair(e.fermeture) || !Pair(e.armement))
        {
            Debug.LogError("[Rechargement] La fiche " + SheetPath(code) + " est introuvable ou incomplète. Exporter d'abord depuis Blender.");
            return null;
        }
        return sheet;
    }

    private static bool Pair(float[] step) => step != null && step.Length == 2;

    private static Vector2 Step(float[] step) => new Vector2(step[0], step[1]);

    private static Sheet ReadSheet(string code)
    {
        var text = AssetDatabase.LoadAssetAtPath<TextAsset>(SheetPath(code));
        if (text == null)
        {
            Debug.LogError("[Rechargement] Introuvable : " + SheetPath(code) + ". Exporter d'abord depuis Blender.");
            return null;
        }
        var sheet = JsonUtility.FromJson<Sheet>(text.text);
        if (sheet == null || sheet.etapes == null || sheet.utile == null || sheet.utile.Length != 2)
        {
            Debug.LogError("[Rechargement] La fiche " + SheetPath(code) + " est incomplète.");
            return null;
        }
        return sheet;
    }

    /// <summary>Humanoïde sur l'avatar du Y Bot, toute la prise, sans boucle, la racine comme Y Bot@Reloading (seul le
    /// haut du corps s'en sert : les hanches sont celles de la couche de déplacement).</summary>
    private static bool ConfigureImport(string code)
    {
        var importer = AssetImporter.GetAtPath(ClipPath(code)) as ModelImporter;
        var common = AssetImporter.GetAtPath(CommonClipPath) as ModelImporter;
        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ProneSideCrawl.AvatarPath);
        if (importer == null || common == null || avatar == null)
        {
            Debug.LogError("[Rechargement] Introuvable : " + (importer == null ? ClipPath(code) : common == null ? CommonClipPath : ProneSideCrawl.AvatarPath) + ".");
            return false;
        }
        ModelImporterClipAnimation model = common.clipAnimations[0];
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        ModelImporterClipAnimation take = importer.defaultClipAnimations[0];
        importer.clipAnimations = new[]
        {
            new ModelImporterClipAnimation
            {
                name = ClipName(code), takeName = take.takeName, firstFrame = take.firstFrame, lastFrame = take.lastFrame,
                loopTime = false, loopPose = false,
                lockRootRotation = model.lockRootRotation, keepOriginalOrientation = model.keepOriginalOrientation,
                lockRootHeightY = model.lockRootHeightY, keepOriginalPositionY = model.keepOriginalPositionY,
                heightFromFeet = model.heightFromFeet,
                lockRootPositionXZ = model.lockRootPositionXZ, keepOriginalPositionXZ = model.keepOriginalPositionXZ,
                mirror = false,
            },
        };
        importer.SaveAndReimport();
        return true;
    }

    /// <summary>L'état Recharge de la couche du haut du corps.</summary>
    public static AnimatorState ReloadState(AnimatorController controller)
    {
        if (controller == null || controller.layers.Length <= MagazineSwap.UpperBodyLayer) return null;
        foreach (ChildAnimatorState s in controller.layers[MagazineSwap.UpperBodyLayer].stateMachine.states)
            if (s.state.name == MagazineSwap.ReloadStateName) return s.state;
        return null;
    }

    private static bool WireAnimator(List<Built> built)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ProneSideCrawl.ControllerPath);
        AnimatorState state = ReloadState(controller);
        if (state == null || !(state.motion is BlendTree motion))
        {
            Debug.LogError("[Rechargement] L'état « " + MagazineSwap.ReloadStateName + " » de la couche du haut du corps, ou son arbre, manque.");
            return false;
        }
        bool known = false;
        foreach (AnimatorControllerParameter p in controller.parameters) known |= p.name == MagazineSwap.GestureParameter;
        if (!known) controller.AddParameter(MagazineSwap.GestureParameter, AnimatorControllerParameterType.Float);

        // L'arbre du geste commun : celui de l'état, ou, déjà branché, son enfant à 0.
        BlendTree gestures = motion.name == GestureTreeName ? motion : null;
        BlendTree common = gestures != null ? ChildAt(gestures, 0f) : motion;
        if (common == null)
        {
            Debug.LogError("[Rechargement] L'arbre du geste commun manque dans l'état Recharge.");
            return false;
        }
        Motion prone = null;
        foreach (ChildMotion c in common.children)
            if (Mathf.Approximately(c.threshold, 2f)) prone = c.motion;
        if (gestures == null) gestures = NewTree(controller, GestureTreeName);

        var children = new List<ChildMotion> { new ChildMotion { motion = common, threshold = 0f, timeScale = 1f } };
        built.Sort((a, b) => a.value.CompareTo(b.value));
        foreach (Built b in built)
        {
            // Debout et accroupi, le geste de l'arme ; allongé, le geste commun (il garde le chargeur en place).
            BlendTree tree = ChildAt(gestures, b.value);
            if (tree == null) tree = NewTree(controller, TreeName(b.code));
            tree.name = TreeName(b.code);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "StanceF";
            tree.useAutomaticThresholds = false;
            tree.children = new[]
            {
                new ChildMotion { motion = b.clip, threshold = 0f, timeScale = 1f },
                new ChildMotion { motion = b.clip, threshold = 1f, timeScale = 1f },
                new ChildMotion { motion = prone, threshold = 2f, timeScale = 1f },
            };
            EditorUtility.SetDirty(tree);
            children.Add(new ChildMotion { motion = tree, threshold = b.value, timeScale = 1f });
        }
        foreach (ChildMotion c in gestures.children)
            if (c.threshold > 0.5f && !children.Exists(k => Mathf.Approximately(k.threshold, c.threshold)))
                Debug.LogWarning("[Rechargement] Le geste à " + c.threshold + " de l'état Recharge n'a plus de clip : il sort de l'arbre.");
        gestures.blendType = BlendTreeType.Simple1D;
        gestures.blendParameter = MagazineSwap.GestureParameter;
        gestures.useAutomaticThresholds = false;
        gestures.children = children.ToArray();
        state.motion = gestures;
        EditorUtility.SetDirty(gestures);
        EditorUtility.SetDirty(controller);
        return true;
    }

    private static BlendTree ChildAt(BlendTree tree, float threshold)
    {
        foreach (ChildMotion c in tree.children)
            if (Mathf.Approximately(c.threshold, threshold)) return c.motion as BlendTree;
        return null;
    }

    private static BlendTree NewTree(AnimatorController controller, string name)
    {
        var tree = new BlendTree { name = name, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    /// <summary>L'arme du préfab joueur, de l'arsenal ou d'un mode (WeaponController.AllWeapons), dont le modèle porte
    /// cette désignation (WeaponModel.Designation), ou null : le Big D est le modèle du Golden Gun.</summary>
    public static WeaponData WeaponOfModel(string designation)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        WeaponController controller = prefab != null ? prefab.GetComponentInChildren<WeaponController>(true) : null;
        if (controller == null) return null;
        foreach (WeaponData w in controller.AllWeapons)
            if (w != null && w.modelPrefab != null && w.modelPrefab.TryGetComponent(out WeaponModel m) && m.Designation == designation)
                return w;
        return null;
    }

    /// <summary>
    /// Le geste d'une arme qui s'ouvre : les étapes de l'arme et ce qui y bouge (sa fiche), les étapes du chargeur dans
    /// leur sens ici (la douille vide part, la main prend la cartouche, la présente, elle est devant la chambre), le clip,
    /// et la main gauche quand la cartouche se présente, dans le repère de l'arme, mesurée sur le clip importé.
    /// </summary>
    private static ReloadGesture FillBreakAction(Built b)
    {
        WeaponData weapon = WeaponOfModel(b.code);
        if (weapon == null)
        {
            Debug.LogError("[Rechargement] Aucune arme du préfab joueur n'a le modèle " + b.code + " (WeaponData.modelPrefab).");
            return null;
        }
        string path = GesturePath(b.code);
        var gesture = AssetDatabase.LoadAssetAtPath<BreakActionGesture>(path);
        if (gesture == null)
        {
            gesture = ScriptableObject.CreateInstance<BreakActionGesture>();
            AssetDatabase.CreateAsset(gesture, path);
        }
        Undo.RecordObject(gesture, "Geste de rechargement du " + b.code);
        BreakSheet sheet = b.breakSheet;
        BreakSteps e = sheet.etapes;
        gesture.animatorValue = b.value;
        gesture.weapons = new[] { weapon };
        gesture.latchStep = Step(e.levier);
        gesture.openStep = Step(e.ouverture);
        gesture.ejectStep = Step(e.ejection);
        gesture.insertStep = Step(e.insertion);
        gesture.closeStep = Step(e.fermeture);
        gesture.cockStep = Step(e.armement);
        gesture.barrelAngle = sheet.angleCanon;
        gesture.hammerAngle = sheet.angleChien;
        gesture.latchAngle = sheet.angleLevier;
        gesture.caseTravel = sheet.sortieDouille;
        gesture.cartridgeApproach = sheet.approcheCartouche;
        gesture.reloadSeconds = sheet.dureeRechargement;
        gesture.drop = e.ejection[1];
        gesture.grab = e.saisie;
        gesture.seated = e.insertion[0];
        gesture.approach = Mathf.Max(gesture.grab, gesture.seated - BreakActionGesture.PresentLead);
        gesture.clipLength = b.clip.length;
        gesture.usefulStart = sheet.utile[0];
        gesture.usefulEnd = sheet.utile[1];
        if (sheet.paumeConception != null && sheet.paumeConception.Length == 3)
            gesture.designPalm = new Vector3(sheet.paumeConception[0], sheet.paumeConception[1], sheet.paumeConception[2]);
        gesture.armingParts = new string[0];
        gesture.armingTravel = 0f;
        gesture.armingViewPitchMax = 0f;
        if (!MeasureHand(b.clip, gesture.usefulStart + gesture.seated * (gesture.usefulEnd - gesture.usefulStart),
                         out gesture.weaponInHandPosition, out gesture.weaponInHandRotation))
            return null;
        EditorUtility.SetDirty(gesture);
        return gesture;
    }

    /// <summary>L'arme de l'arsenal du préfab joueur qui porte cette désignation, ou null.</summary>
    public static WeaponData WeaponOf(string code)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        WeaponController controller = prefab != null ? prefab.GetComponentInChildren<WeaponController>(true) : null;
        if (controller == null) return null;
        foreach (WeaponData w in controller.Loadout)
            if (w != null && w.weaponName == code) return w;
        return null;
    }

    /// <summary>Le geste d'une arme : ses étapes (la fiche), le clip (sa durée et sa partie utile), et la main gauche
    /// quand le chargeur neuf entre, dans le repère de l'arme, mesurée sur le clip importé.</summary>
    private static ReloadGesture FillGesture(Built b)
    {
        WeaponData weapon = WeaponOf(b.code);
        if (weapon == null)
        {
            Debug.LogError("[Rechargement] Le " + b.code + " n'est pas dans l'arsenal du préfab joueur.");
            return null;
        }
        string path = GesturePath(b.code);
        var gesture = AssetDatabase.LoadAssetAtPath<ReloadGesture>(path);
        if (gesture == null)
        {
            gesture = ScriptableObject.CreateInstance<ReloadGesture>();
            AssetDatabase.CreateAsset(gesture, path);
        }
        Undo.RecordObject(gesture, "Geste de rechargement du " + b.code);
        Sheet sheet = b.sheet;
        gesture.animatorValue = b.value;
        gesture.weapons = new[] { weapon };
        gesture.drop = sheet.etapes.sortie;
        gesture.grab = sheet.etapes.saisie;
        gesture.approach = sheet.etapes.approche;
        gesture.seated = sheet.etapes.insertion;
        gesture.clipLength = b.clip.length;
        gesture.usefulStart = sheet.utile[0];
        gesture.usefulEnd = sheet.utile[1];
        if (sheet.paumeConception != null && sheet.paumeConception.Length == 3)
            gesture.designPalm = new Vector3(sheet.paumeConception[0], sheet.paumeConception[1], sheet.paumeConception[2]);
        Arming arming = sheet.pieceArmement;
        gesture.armingParts = arming?.pieces ?? new string[0];
        gesture.armingTravel = arming != null ? arming.course : 0f;
        gesture.armingGrab = arming != null ? arming.prise : 0f;
        gesture.armingPulled = arming != null ? arming.tiree : 0f;
        gesture.armingRelease = arming != null ? arming.lachee : 0f;
        gesture.armingReturned = arming != null ? arming.revenue : 0f;
        (gesture.armingViewPitchMax, gesture.armingViewYaw) = ArmingViewOf(b.code);
        float[] palm = sheet.levier?.paume;
        gesture.armingPalm = palm != null && palm.Length == 3 ? new Vector3(palm[0], palm[1], palm[2]) : Vector3.zero;
        if (!MeasureHand(b.clip, gesture.usefulStart + gesture.seated * (gesture.usefulEnd - gesture.usefulStart),
                         out gesture.weaponInHandPosition, out gesture.weaponInHandRotation))
            return null;
        EditorUtility.SetDirty(gesture);
        return gesture;
    }

    /// <summary>
    /// Le repère de l'arme (PriseArme, sous la main droite) vu de l'os de la main gauche, le clip échantillonné sur le
    /// robot du préfab à l'instant `time`. La main gauche y porte le gant (échelle nulle) : on compose positions et
    /// rotations, sans ses matrices.
    /// </summary>
    public static bool MeasureHand(AnimationClip clip, float time, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            Transform model = root.transform.Find("Model");
            Animator animator = model != null ? model.GetComponent<Animator>() : null;
            Transform right = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            Transform left = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            Transform anchor = right != null ? right.Find(HeldWeapons.GripAnchorName) : null;
            if (anchor == null || left == null)
            {
                Debug.LogError("[Rechargement] Le robot du préfab joueur n'a pas ses mains ou sa prise d'arme.");
                return false;
            }
            model.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clip.SampleAnimation(model.gameObject, time);
            Quaternion inverse = Quaternion.Inverse(left.rotation);
            position = inverse * (anchor.position - left.position);
            rotation = inverse * anchor.rotation;
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>MagazineSwap sur le préfab joueur, avec ces gestes (par valeur), puis ceux qu'il avait déjà.</summary>
    private static void PutOnPlayer(List<Built> built)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            MagazineSwap swap = root.GetComponent<MagazineSwap>();
            if (swap == null) swap = root.AddComponent<MagazineSwap>();
            var list = new List<ReloadGesture>();
            foreach (Built b in built) list.Add(b.gesture);
            foreach (ReloadGesture g in swap.Gestures)
                if (g != null && !list.Contains(g)) list.Add(g);
            swap.EditorSetGestures(list.ToArray());
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
