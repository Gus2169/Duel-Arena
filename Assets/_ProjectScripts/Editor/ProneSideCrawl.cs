using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// La reptation de côté (2026-10-29) : deux clips calculés dans Blender sur le squelette du Y Bot
/// (Tools/Blender/animations/reptation_laterale.py), un par côté, et leur place dans l'Animator.
///
/// Mixamo n'a pas de reptation latérale (seulement une roulade, refusée par l'utilisateur) : allongé, le
/// robot ne pouvait pas aller de côté (dette n° 41). Les clips ne servent qu'au bassin et aux jambes : la
/// couche du haut du corps pose l'attente couchée sur le buste et les bras.
///
/// Duel Arena > Animations > Importer la reptation de côté : règle l'import des deux FBX (humanoïdes,
/// l'avatar du Y Bot copié, la racine comme la reptation arrière de Mixamo), puis fait de l'arbre de la
/// reptation un arbre directionnel sur MoveX et MoveY : l'arrière, l'avant (l'arrière joué à l'envers), la
/// gauche, la droite. On réexporte de Blender et on relance le menu ; on ne retouche ni les clips ni
/// l'arbre à la main.
/// </summary>
public static class ProneSideCrawl
{
    public const string LeftPath = "Assets/_ProjectArt/Animations/Blender/Y Bot@Reptation Gauche.fbx";
    public const string RightPath = "Assets/_ProjectArt/Animations/Blender/Y Bot@Reptation Droite.fbx";
    public const string LeftClip = "Reptation Gauche";
    public const string RightClip = "Reptation Droite";
    public const string AvatarPath = "Assets/_ProjectArt/Mesh/Personnages/Y Bot.fbx";
    public const string BackwardPath = "Assets/_ProjectArt/Animations/New/Y Bot@Moving Backward In Prone Position.fbx";
    public const string ControllerPath = "Assets/_ProjectArt/PlayerAnimator.controller";
    public const string TreeName = "ProneMarche";

    /// <summary>Où l'arbre place ses clips : tout près du centre, comme les seuils de l'arbre d'avant (±0,05).
    /// L'allure n'y joue aucun rôle (la cadence s'en charge, PlayerAnimator.DriveGait) : seule la direction
    /// choisit le mélange.</summary>
    public const float Radius = 0.05f;

    [MenuItem("Duel Arena/Animations/Importer la reptation de côté")]
    public static void Build()
    {
        if (!ConfigureImport(LeftPath, LeftClip) || !ConfigureImport(RightPath, RightClip)) return;
        AnimationClip backward = Clip(BackwardPath, "Moving Backward In Prone Position");
        AnimationClip left = Clip(LeftPath, LeftClip);
        AnimationClip right = Clip(RightPath, RightClip);
        BlendTree tree = FindTree();
        if (backward == null || left == null || right == null || tree == null)
        {
            Debug.LogError("[Reptation] Il manque un clip (reptation arrière, gauche ou droite) ou l'arbre « " + TreeName + " ».");
            return;
        }
        tree.blendType = BlendTreeType.SimpleDirectional2D;
        tree.blendParameter = "MoveX";
        tree.blendParameterY = "MoveY";
        tree.useAutomaticThresholds = false;
        tree.children = new[]
        {
            new ChildMotion { motion = backward, position = new Vector2(0f, -Radius), timeScale = 1f },
            new ChildMotion { motion = backward, position = new Vector2(0f, Radius), timeScale = -1f },
            new ChildMotion { motion = left, position = new Vector2(-Radius, 0f), timeScale = 1f },
            new ChildMotion { motion = right, position = new Vector2(Radius, 0f), timeScale = 1f },
        };
        EditorUtility.SetDirty(tree);
        EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath));
        AssetDatabase.SaveAssets();
        Debug.Log("[Reptation] Clips de la reptation de côté importés, arbre « " + TreeName + " » sur quatre directions.");
    }

    /// <summary>Humanoïde sur l'avatar du Y Bot, un clip qui boucle sur toute la prise, la racine comme la
    /// reptation arrière : rotation et hauteur gardées dans la pose (« Based Upon Original »), déplacement
    /// horizontal en root motion (que le jeu jette ; PlayerAnimator s'en sert pour mesurer la cadence).</summary>
    private static bool ConfigureImport(string path, string clipName)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
        if (importer == null || avatar == null)
        {
            Debug.LogError("[Reptation] Introuvable : " + (importer == null ? path : AvatarPath) + ". Exporter d'abord depuis Blender.");
            return false;
        }
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        ModelImporterClipAnimation take = importer.defaultClipAnimations[0];
        importer.clipAnimations = new[]
        {
            new ModelImporterClipAnimation
            {
                name = clipName, takeName = take.takeName, firstFrame = take.firstFrame, lastFrame = take.lastFrame,
                loopTime = true, loopPose = false,
                lockRootRotation = true, keepOriginalOrientation = true,
                lockRootHeightY = false, keepOriginalPositionY = true, heightFromFeet = false,
                lockRootPositionXZ = false, keepOriginalPositionXZ = false,
                mirror = false,
            },
        };
        importer.SaveAndReimport();
        return true;
    }

    /// <summary>L'arbre de la reptation : dans l'état « Prone » de la couche de base, la branche du déplacement.</summary>
    public static BlendTree FindTree()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) return null;
        foreach (ChildAnimatorState state in controller.layers[0].stateMachine.states)
        {
            if (state.state.name != "Prone" || !(state.state.motion is BlendTree prone)) continue;
            foreach (ChildMotion child in prone.children)
            {
                if (child.motion is BlendTree tree && tree.name == TreeName) return tree;
            }
        }
        return null;
    }

    public static AnimationClip Clip(string path, string name)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (o is AnimationClip c && c.name == name) return c;
        }
        return null;
    }
}
