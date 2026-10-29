using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// La reptation de côté (2026-10-29) : deux clips calculés dans Blender (reptation_laterale.py), importés et
/// branchés par Duel Arena > Animations > Importer la reptation de côté (ProneSideCrawl).
///
/// Ce qu'il faut garder : un import humanoïde sur l'avatar du Y Bot, avec la racine de la reptation arrière
/// (une racine à l'échelle 0,01, celle d'un export de Blender en FBX_SCALE_ALL, faisait déjà avertir Unity :
/// « Root scale mis-match »), un arbre qui va dans les quatre directions, et des clips qui vont de leur côté,
/// pieds plantés et orteils au sol, une fois dans Unity. Le clip est échantillonné sur le Y Bot comme
/// l'Animator le joue (humanoïde, avatar du Y Bot) : ce sont ses os qu'on mesure, pas les courbes du FBX.
/// </summary>
public class ProneSideCrawlTests
{
    private static ModelImporterClipAnimation ClipSettings(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        Assert.IsNotNull(importer, "Pas de modèle importé : " + path);
        ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
        Assert.AreEqual(1, clips.Length, "Un seul clip attendu dans " + path);
        return clips[0];
    }

    [Test]
    public void LaReptationDeCote_SImporteCommeLaReptationArriere()
    {
        ModelImporterClipAnimation reference = ClipSettings(ProneSideCrawl.BackwardPath);
        AnimationClip backward = ProneSideCrawl.Clip(ProneSideCrawl.BackwardPath, "Moving Backward In Prone Position");
        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ProneSideCrawl.AvatarPath);
        var failures = new List<string>();
        foreach ((string path, string name) in new[] { (ProneSideCrawl.LeftPath, ProneSideCrawl.LeftClip), (ProneSideCrawl.RightPath, ProneSideCrawl.RightClip) })
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer.animationType != ModelImporterAnimationType.Human) failures.Add(name + " n'est pas humanoïde");
            if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || importer.sourceAvatar != avatar)
                failures.Add(name + " ne copie pas l'avatar du Y Bot");
            ModelImporterClipAnimation c = ClipSettings(path);
            if (c.name != name) failures.Add($"le clip de {path} s'appelle « {c.name} »");
            if (!c.loopTime) failures.Add(name + " ne boucle pas");
            if (c.mirror) failures.Add(name + " est mis en miroir (chaque côté est calculé dans Blender)");
            if (c.lockRootRotation != reference.lockRootRotation || c.keepOriginalOrientation != reference.keepOriginalOrientation
                || c.lockRootHeightY != reference.lockRootHeightY || c.keepOriginalPositionY != reference.keepOriginalPositionY
                || c.lockRootPositionXZ != reference.lockRootPositionXZ || c.keepOriginalPositionXZ != reference.keepOriginalPositionXZ
                || c.heightFromFeet != reference.heightFromFeet)
                failures.Add(name + " n'a pas la racine de la reptation arrière");
            AnimationClip clip = ProneSideCrawl.Clip(path, name);
            if (clip == null) failures.Add(name + " introuvable");
            // Même durée que la reptation arrière : l'arbre mêle les deux en diagonale sans les étirer.
            else if (Mathf.Abs(clip.length - backward.length) > 0.5f / 30f)
                failures.Add($"{name} dure {clip.length:F3} s, la reptation arrière {backward.length:F3} s");
        }
        Assert.IsTrue(reference.keepOriginalOrientation, "Garde : la reptation arrière garde l'orientation d'origine.");
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    [Test]
    public void LArbreDeLaReptation_VaDansLesQuatreDirections()
    {
        BlendTree tree = ProneSideCrawl.FindTree();
        Assert.IsNotNull(tree, "Arbre « " + ProneSideCrawl.TreeName + " » introuvable dans l'état Prone.");
        Assert.AreEqual(BlendTreeType.SimpleDirectional2D, tree.blendType, "L'arbre doit choisir le clip selon la direction.");
        Assert.AreEqual("MoveX", tree.blendParameter);
        Assert.AreEqual("MoveY", tree.blendParameterY);

        string At(Vector2 direction, float timeScale)
        {
            foreach (ChildMotion child in tree.children)
            {
                if (child.motion != null && Vector2.Angle(child.position, direction) < 1f && Mathf.Approximately(child.timeScale, timeScale))
                    return child.motion.name;
            }
            return null;
        }
        Assert.AreEqual(4, tree.children.Length, "Quatre directions : l'arrière, l'avant, la gauche, la droite.");
        Assert.AreEqual("Moving Backward In Prone Position", At(Vector2.down, 1f), "En arrière : la reptation de Mixamo.");
        Assert.AreEqual("Moving Backward In Prone Position", At(Vector2.up, -1f), "En avant : la même, à l'envers.");
        Assert.AreEqual(ProneSideCrawl.LeftClip, At(Vector2.left, 1f), "À gauche : la reptation de côté gauche.");
        Assert.AreEqual(ProneSideCrawl.RightClip, At(Vector2.right, 1f), "À droite : la reptation de côté droite.");
    }

    /// <summary>
    /// Chaque clip, joué sur le Y Bot, va de son côté (gauche = -X dans Unity) d'environ 0,7 m par cycle, sans
    /// avancer ni reculer ; un pied posé ne glisse pas de côté (le bassin avance à vitesse constante, comme le
    /// robot dans le jeu) ; les orteils touchent le sol sans s'y enfoncer ; les genoux restent au-dessus.
    /// </summary>
    [Test]
    public void ChaqueClip_VaDeSonCote_PiedsPlantes_OrteilsAuSol()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ProneSideCrawl.AvatarPath);
        var instance = (GameObject)Object.Instantiate(model);
        try
        {
            var animator = instance.GetComponent<Animator>();
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var failures = new List<string>();
            foreach ((string path, string name, float side) in new[] { (ProneSideCrawl.LeftPath, ProneSideCrawl.LeftClip, -1f), (ProneSideCrawl.RightPath, ProneSideCrawl.RightClip, 1f) })
            {
                AnimationClip clip = ProneSideCrawl.Clip(path, name);
                Assert.IsNotNull(clip, name + " introuvable");
                int frames = Mathf.RoundToInt(clip.length * clip.frameRate);
                var feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
                var knees = new[] { animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), animator.GetBoneTransform(HumanBodyBones.RightLowerLeg) };
                var toes = new[] { FindDeep(instance.transform, "mixamorig:LeftToe_End"), FindDeep(instance.transform, "mixamorig:RightToe_End") };
                var ankles = new List<Vector3>[] { new List<Vector3>(), new List<Vector3>() };
                float lowestToe = float.MaxValue, lowestKnee = float.MaxValue;
                Vector3 start = Vector3.zero;
                for (int f = 0; f <= frames; f++)
                {
                    clip.SampleAnimation(instance, f / clip.frameRate);
                    if (f == 0) start = hips.position;
                    for (int s = 0; s < 2; s++)
                    {
                        ankles[s].Add(feet[s].position);
                        lowestToe = Mathf.Min(lowestToe, toes[s].position.y);
                        lowestKnee = Mathf.Min(lowestKnee, knees[s].position.y);
                    }
                }
                Vector3 travel = hips.position - start;
                Debug.Log($"[REPTATION] {name} : bassin {travel.x:+0.000;-0.000} m de côté, {travel.z:+0.000;-0.000} m en avant par cycle ; orteils au plus bas {lowestToe * 100f:F1} cm, genoux {lowestKnee * 100f:F1} cm");
                if (travel.x * side < 0.6f || travel.x * side > 0.8f) failures.Add($"{name} : le bassin va de {travel.x:F3} m de côté par cycle (0,6 à 0,8 m vers {(side < 0f ? "-X" : "+X")} attendus)");
                if (Mathf.Abs(travel.z) > 0.02f || Mathf.Abs(travel.y) > 0.01f) failures.Add($"{name} : le bassin dérive de {travel.z:F3} m en avant, {travel.y:F3} m en hauteur");
                if (lowestToe < -0.01f || lowestToe > 0.03f) failures.Add($"{name} : orteils au plus bas à {lowestToe * 100f:F1} cm du sol");
                if (lowestKnee < 0.03f) failures.Add($"{name} : un genou descend à {lowestKnee * 100f:F1} cm du sol");
                for (int s = 0; s < 2; s++)
                {
                    // Un pied est posé quand sa cheville est à sa hauteur la plus basse, à 8 mm près (le
                    // reciblage humanoïde la fait varier de 6 mm en appui), et ses deux voisines aussi : la
                    // dernière image d'un passage, à 4 mm du sol, n'est pas un appui. Pendant tout un appui,
                    // le pied ne glisse pas de côté.
                    List<Vector3> a = ankles[s];
                    float ground = float.MaxValue;
                    foreach (Vector3 p in a) ground = Mathf.Min(ground, p.y);
                    bool Low(int f) => a[Mathf.Clamp(f, 0, a.Count - 1)].y <= ground + 0.008f;
                    int planted = 0;
                    float drift = 0f, swept = 0f, low = 0f, high = 0f;
                    bool inStance = false;
                    for (int f = 0; f < a.Count; f++)
                    {
                        swept = Mathf.Max(swept, Mathf.Abs(a[f].x - a[0].x));
                        bool core = Low(f - 1) && Low(f) && Low(f + 1);
                        if (!core)
                        {
                            inStance = false;
                            continue;
                        }
                        planted++;
                        if (!inStance) { low = high = a[f].x; inStance = true; }
                        low = Mathf.Min(low, a[f].x);
                        high = Mathf.Max(high, a[f].x);
                        drift = Mathf.Max(drift, high - low);
                    }
                    string foot = s == 0 ? "gauche" : "droit";
                    // Garde : le pied est posé une bonne part du cycle, et il avance avec le robot.
                    if (planted < 15 || swept < 0.3f) failures.Add($"{name}, pied {foot} : posé {planted} images, déplacé de {swept:F2} m (garde)");
                    if (drift > 0.005f) failures.Add($"{name}, pied {foot} : glisse de {drift * 1000f:F1} mm de côté pendant un appui");
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
