using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// La main droite des robots du jeu dans la prise des pistolets, pour Blender (2026-11-03, la tenue du Big D). La
/// prise du clip (« Pistol Aim Idle ») referme la main sur deux centimètres : les doigts entraient de 14 à 20 mm dans la
/// crosse du Big D (et de 14 à 15 mm dans celle des autres armes de poing). Tools/Blender/animations/tenue_golden_gun.py
/// en calcule une tenue qui épouse la crosse, sur les axes des muscles des doigts et contre les vraies pièces des
/// châssis ; le jeu la repose (BreakActionView).
///
/// Duel Arena > Animations > Relever les mains des robots : sur le robot du préfab joueur, dans la pose du clip, pour
/// chaque os de la main droite, sa place et sa rotation dans le repère de l'arme (l'ancre PriseArme) et sous son parent ;
/// pour chaque muscle des doigts, la rotation locale qu'en donne un petit pas, puis le muscle à -1 et à +1 ; pour chaque
/// châssis, les pièces (ou la peau) de chaque os, dans le repère de cet os (l'os dominant de chaque sommet). À relancer
/// si le squelette, un châssis ou le clip changent.
/// </summary>
public static class RobotHandsExport
{
    public const string OutputPath = "Tools/Blender/animations/mains_robots.json";
    public const string ClipPath = "Assets/_ProjectArt/Animations/New/Y Bot@Pistol Aim Idle.fbx";
    private static readonly string[] Chassis = { "CT01", "OS02", "Alpha_Surface" };

    private static readonly HumanBodyBones[] Bones =
    {
        HumanBodyBones.RightHand,
        HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
        HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
    };

    [MenuItem("Duel Arena/Animations/Relever les mains des robots")]
    public static void Export()
    {
        string json = Measure();
        if (json == null) return;
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), OutputPath), json);
        Debug.Log("[Mains] La main droite des robots écrite dans " + OutputPath + ".");
    }

    /// <summary>Le relevé, en JSON (positions en mètres, rotations en quaternions x, y, z, w d'Unity).</summary>
    public static string Measure()
    {
        AnimationClip clip = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ClipPath))
            if (o is AnimationClip c && !c.name.StartsWith("__preview")) clip = c;
        if (clip == null)
        {
            Debug.LogError("[Mains] Pas de clip dans " + ClipPath + ".");
            return null;
        }
        GameObject root = PrefabUtility.LoadPrefabContents(MagazineReloadImporter.PlayerPath);
        try
        {
            Transform model = root.transform.Find("Model");
            Animator animator = model != null ? model.GetComponent<Animator>() : null;
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError("[Mains] Le préfab joueur n'a pas son robot humanoïde (Model).");
                return null;
            }
            model.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            HeldWeapons.WearTaserGlove(animator, false);   // le gant replie la main gauche
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform anchor = hand.Find(HeldWeapons.GripAnchorName);
            clip.SampleAnimation(model.gameObject, 0f);
            Quaternion toAnchor = Quaternion.Inverse(anchor.rotation);

            var text = new StringBuilder("{\"clip\": \"").Append(clip.name).Append("\", \"os\": [");
            for (int i = 0; i < Bones.Length; i++)
            {
                Transform t = animator.GetBoneTransform(Bones[i]);
                if (i > 0) text.Append(",");
                text.Append("{\"humain\": \"").Append(Bones[i]).Append("\", \"nom\": \"").Append(Short(t.name))
                    .Append("\", \"parent\": \"").Append(Short(t.parent.name)).Append("\", \"p\": ")
                    .Append(V(toAnchor * (t.position - anchor.position))).Append(", \"q\": ").Append(Q(toAnchor * t.rotation))
                    .Append(", \"lp\": ").Append(V(t.localPosition)).Append(", \"lq\": ").Append(Q(t.localRotation)).Append("}");
            }
            text.Append("], \"muscles\": [");
            var handler = new HumanPoseHandler(animator.avatar, model);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            handler.SetHumanPose(ref pose);
            bool first = true;
            for (int i = 1; i < Bones.Length; i++)
            {
                Transform t = animator.GetBoneTransform(Bones[i]);
                Quaternion kept = t.localRotation;
                for (int dof = 0; dof < 3; dof++)
                {
                    int m = HumanTrait.MuscleFromBone((int)Bones[i], dof);
                    if (m < 0) continue;
                    float value = pose.muscles[m];
                    var steps = new List<string>();
                    foreach (float v in new[] { value + 0.05f, -1f, 1f })
                    {
                        HumanPose moved = pose;
                        moved.muscles = (float[])pose.muscles.Clone();
                        moved.muscles[m] = v;
                        handler.SetHumanPose(ref moved);
                        steps.Add(Q(t.localRotation));
                    }
                    handler.SetHumanPose(ref pose);
                    if (!first) text.Append(",");
                    first = false;
                    text.Append("{\"os\": \"").Append(Short(t.name)).Append("\", \"dof\": ").Append(dof)
                        .Append(", \"muscle\": \"").Append(HumanTrait.MuscleName[m]).Append("\", \"valeur\": ").Append(F(value))
                        .Append(", \"base\": ").Append(Q(kept)).Append(", \"petit\": ").Append(steps[0])
                        .Append(", \"moins\": ").Append(steps[1]).Append(", \"plus\": ").Append(steps[2]).Append("}");
                }
            }
            text.Append("], \"chassis\": {");
            bool firstPart = true;
            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (System.Array.IndexOf(Chassis, smr.name) < 0) continue;
                if (!firstPart) text.Append(",");
                firstPart = false;
                text.Append("\"").Append(smr.name).Append("\": ").Append(Pieces(smr));
            }
            // la main gauche, que la tenue ne bouge pas : ses faces dans le repère de l'arme, pour les contrôles
            text.Append("}, \"gauche\": {");
            firstPart = true;
            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (System.Array.IndexOf(Chassis, smr.name) < 0) continue;
                if (!firstPart) text.Append(",");
                firstPart = false;
                text.Append("\"").Append(smr.name).Append("\": ").Append(LeftHand(smr, anchor));
            }
            return text.Append("}}").ToString();
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Les pièces de la main droite d'un châssis : pour chaque os, les sommets dont il est l'os dominant (dans
    /// son repère) et les faces qui partent de l'un d'eux.</summary>
    private static string Pieces(SkinnedMeshRenderer smr)
    {
        var mesh = new Mesh();
        smr.BakeMesh(mesh, false);
        try
        {
            Vector3[] verts = mesh.vertices;
            BoneWeight[] weights = smr.sharedMesh.boneWeights;
            Transform[] bones = smr.bones;
            int[] tris = smr.sharedMesh.triangles;
            var index = new Dictionary<string, Dictionary<int, int>>();
            var points = new Dictionary<string, StringBuilder>();
            var faces = new Dictionary<string, StringBuilder>();
            for (int k = 0; k < tris.Length; k += 3)
            {
                Transform bone = bones[weights[tris[k]].boneIndex0];
                string name = Short(bone.name);
                if (!name.StartsWith("RightHand")) continue;
                if (!index.TryGetValue(name, out var idx))
                {
                    index[name] = idx = new Dictionary<int, int>();
                    points[name] = new StringBuilder();
                    faces[name] = new StringBuilder();
                }
                var f = new int[3];
                for (int j = 0; j < 3; j++)
                {
                    int vi = tris[k + j];
                    if (!idx.TryGetValue(vi, out int ni))
                    {
                        ni = idx.Count;
                        idx[vi] = ni;
                        if (ni > 0) points[name].Append(",");
                        points[name].Append(V(bone.InverseTransformPoint(smr.transform.TransformPoint(verts[vi]))));
                    }
                    f[j] = ni;
                }
                if (faces[name].Length > 0) faces[name].Append(",");
                faces[name].Append("[").Append(f[0]).Append(",").Append(f[1]).Append(",").Append(f[2]).Append("]");
            }
            var text = new StringBuilder("{");
            bool first = true;
            foreach (var kv in index)
            {
                if (!first) text.Append(",");
                first = false;
                text.Append("\"").Append(kv.Key).Append("\": {\"v\": [").Append(points[kv.Key]).Append("], \"f\": [")
                    .Append(faces[kv.Key]).Append("]}");
            }
            return text.Append("}").ToString();
        }
        finally
        {
            Object.DestroyImmediate(mesh);
        }
    }

    /// <summary>Les faces de la main gauche d'un châssis (celles qui partent d'un sommet dont l'os dominant est de la
    /// main gauche), dans le repère de l'arme.</summary>
    private static string LeftHand(SkinnedMeshRenderer smr, Transform anchor)
    {
        var mesh = new Mesh();
        smr.BakeMesh(mesh, false);
        try
        {
            Vector3[] verts = mesh.vertices;
            BoneWeight[] weights = smr.sharedMesh.boneWeights;
            Transform[] bones = smr.bones;
            int[] tris = smr.sharedMesh.triangles;
            Quaternion toAnchor = Quaternion.Inverse(anchor.rotation);
            var index = new Dictionary<int, int>();
            var points = new StringBuilder();
            var faces = new StringBuilder();
            for (int k = 0; k < tris.Length; k += 3)
            {
                if (!Short(bones[weights[tris[k]].boneIndex0].name).StartsWith("LeftHand")) continue;
                var f = new int[3];
                for (int j = 0; j < 3; j++)
                {
                    int vi = tris[k + j];
                    if (!index.TryGetValue(vi, out int ni))
                    {
                        ni = index.Count;
                        index[vi] = ni;
                        if (ni > 0) points.Append(",");
                        points.Append(V(toAnchor * (smr.transform.TransformPoint(verts[vi]) - anchor.position)));
                    }
                    f[j] = ni;
                }
                if (faces.Length > 0) faces.Append(",");
                faces.Append("[").Append(f[0]).Append(",").Append(f[1]).Append(",").Append(f[2]).Append("]");
            }
            return "{\"v\": [" + points + "], \"f\": [" + faces + "]}";
        }
        finally
        {
            Object.DestroyImmediate(mesh);
        }
    }

    private static string Short(string name) => name.Replace("mixamorig:", "");
    private static string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);
    private static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    private static string Q(Quaternion q) => $"[{F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)}]";
}
