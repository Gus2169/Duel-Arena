using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Les plis des doigts que le rig humanoïde d'Unity sait rejouer (2026-11-03, le rechargement du Big D). Un clip
/// humanoïde ne garde des doigts que leurs muscles : à la base d'un doigt, un pli et un écart ; aux deux phalanges
/// suivantes, un pli chacune ; aucune torsion. Une pose de doigt calculée librement dans Blender (une petite
/// cinématique inverse qui tord les phalanges) arrive donc ailleurs dans le jeu : le pouce droit du Big D tombait à 3 à
/// 7 cm du levier et du chien. Ce relevé donne à Blender les axes de ces plis et leurs bornes, pour qu'il ne calcule
/// que des poses que le jeu rejoue telles quelles (Tools/Blender/animations/rechargement_golden_gun.py).
///
/// Duel Arena > Animations > Relever les plis des doigts du Y Bot : sur le robot du préfab joueur (le squelette du
/// Y Bot), au repos, ramené à ce que l'humanoïde en garde (aller-retour par HumanPoseHandler), pour chaque phalange du
/// pouce et de l'index des deux mains et des autres doigts de la main droite (Blender y pose la tenue du Big D), ses
/// rotations dans le monde d'Unity au repos et ainsi ramenée, et pour chacun de
/// ses muscles, la rotation (monde) que donne un petit pas du muscle, puis le muscle à -1 et à +1. À relancer si le
/// squelette ou son avatar changent.
/// </summary>
public static class HumanFingerAxes
{
    public const string OutputPath = "Tools/Blender/animations/plis_doigts_ybot.json";

    private static readonly HumanBodyBones[] Bones =
    {
        HumanBodyBones.LeftHand, HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate,
        HumanBodyBones.LeftThumbDistal, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate,
        HumanBodyBones.LeftIndexDistal, HumanBodyBones.RightHand, HumanBodyBones.RightThumbProximal,
        HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
        // la main droite entière : Blender y pose la tenue du Big D (tenue_golden_gun.py), calculée dans le repère d'Unity
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
        HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
    };

    [MenuItem("Duel Arena/Animations/Relever les plis des doigts du Y Bot")]
    public static void Export()
    {
        string json = Measure();
        if (json == null) return;
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), OutputPath), json);
        Debug.Log("[Doigts] Plis des doigts du Y Bot écrits dans " + OutputPath + ".");
    }

    /// <summary>Le relevé, en JSON (rotations en matrices 3×3, lignes d'abord, dans le monde d'Unity).</summary>
    public static string Measure()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(MagazineReloadImporter.PlayerPath);
        try
        {
            Transform model = root.transform.Find("Model");
            Animator animator = model != null ? model.GetComponent<Animator>() : null;
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError("[Doigts] Le préfab joueur n'a pas son robot humanoïde (Model).");
                return null;
            }
            model.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            HeldWeapons.WearTaserGlove(animator, false);   // le gant replie la main gauche
            var rest = new Dictionary<HumanBodyBones, Quaternion>();
            foreach (HumanBodyBones b in Bones) rest[b] = animator.GetBoneTransform(b).rotation;
            var handler = new HumanPoseHandler(animator.avatar, model);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            handler.SetHumanPose(ref pose);
            var kept = new Dictionary<HumanBodyBones, Quaternion>();
            foreach (HumanBodyBones b in Bones) kept[b] = animator.GetBoneTransform(b).rotation;

            var text = new StringBuilder("{\"os\": [");
            for (int i = 0; i < Bones.Length; i++)
            {
                HumanBodyBones b = Bones[i];
                Transform t = animator.GetBoneTransform(b);
                if (i > 0) text.Append(",");
                text.Append("{\"humain\": \"").Append(b).Append("\", \"nom\": \"").Append(t.name).Append("\", \"repos\": ")
                    .Append(Matrix(rest[b])).Append(", \"base\": ").Append(Matrix(kept[b])).Append(", \"muscles\": [");
                bool first = true;
                for (int dof = 0; dof < 3; dof++)
                {
                    int m = HumanTrait.MuscleFromBone((int)b, dof);
                    if (m < 0) continue;
                    float value = pose.muscles[m];
                    var steps = new List<string>();
                    foreach (float v in new[] { value + 0.05f, -1f, 1f })
                    {
                        HumanPose moved = pose;
                        moved.muscles = (float[])pose.muscles.Clone();
                        moved.muscles[m] = v;
                        handler.SetHumanPose(ref moved);
                        steps.Add(Matrix(t.rotation * Quaternion.Inverse(kept[b])));
                    }
                    handler.SetHumanPose(ref pose);
                    if (!first) text.Append(",");
                    first = false;
                    text.Append("{\"dof\": ").Append(dof).Append(", \"muscle\": \"").Append(HumanTrait.MuscleName[m])
                        .Append("\", \"valeur\": ").Append(value.ToString("R", CultureInfo.InvariantCulture))
                        .Append(", \"petit\": ").Append(steps[0]).Append(", \"moins\": ").Append(steps[1])
                        .Append(", \"plus\": ").Append(steps[2]).Append("}");
                }
                text.Append("]}");
            }
            return text.Append("]}").ToString();
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static string Matrix(Quaternion q)
    {
        Matrix4x4 m = Matrix4x4.Rotate(q);
        string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);
        return $"[[{F(m.m00)},{F(m.m01)},{F(m.m02)}],[{F(m.m10)},{F(m.m11)},{F(m.m12)}],[{F(m.m20)},{F(m.m21)},{F(m.m22)}]]";
    }
}
