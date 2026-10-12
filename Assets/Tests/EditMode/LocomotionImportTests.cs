using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Les réglages d'import dont dépend la marche (2026-10-09). Ils ne se voient pas dans le code, et
/// un clip réimporté avec les réglages par défaut les perdrait en silence.
///
/// - « Root Transform Rotation : Based Upon Original ». Par défaut (« Body Orientation »), Unity
///   tourne chaque clip pour aligner la moyenne hanches-épaules sur l'avant : la course latérale de
///   Mixamo, hanches tournées vers sa direction, partait alors 58° de travers et le robot courait
///   en diagonale en allant de côté.
/// - Aucun enfant mis en miroir dans un arbre de déplacement. Unity mêle le déplacement de racine
///   dans le repère du buste de chaque clip : un pas chassé mis en miroir dans l'arbre, mêlé à la
///   course gauche de Mixamo (buste tourné dans l'autre sens), n'avançait plus qu'à 35 % de sa
///   vitesse. La gauche est faite de clips mis en miroir À L'IMPORT, depuis ceux de droite.
///
/// On interroge l'importeur, jamais le .meta : voir le piège « le .meta n'est pas une source
/// fiable » dans CLAUDE.md.
/// </summary>
public class LocomotionImportTests
{
    private const string ControllerPath = "Assets/_ProjectArt/PlayerAnimator.controller";

    private static void CollectMovementChildren(BlendTree tree, bool inMovement, List<ChildMotion> into)
    {
        bool movement = inMovement || tree.name.EndsWith("Marche");
        foreach (ChildMotion child in tree.children)
        {
            if (child.motion is BlendTree sub) CollectMovementChildren(sub, movement, into);
            else if (movement && child.motion is AnimationClip) into.Add(child);
        }
    }

    private static List<ChildMotion> MovementChildren()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Assert.IsNotNull(controller, "Contrôleur introuvable : " + ControllerPath);
        var children = new List<ChildMotion>();
        foreach (ChildAnimatorState state in controller.layers[0].stateMachine.states)
        {
            if (state.state.motion is BlendTree tree) CollectMovementChildren(tree, false, children);
        }
        return children;
    }

    [Test]
    public void LesClipsDeDeplacement_GardentLOrientationDOrigine()
    {
        var failures = new List<string>();
        var seen = new HashSet<string>();
        foreach (ChildMotion child in MovementChildren())
        {
            var clip = (AnimationClip)child.motion;
            if (!seen.Add(clip.name)) continue;

            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as ModelImporter;
            Assert.IsNotNull(importer, clip.name + " ne vient pas d'un modèle importé.");
            ModelImporterClipAnimation settings = null;
            foreach (ModelImporterClipAnimation c in importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations)
            {
                if (c.name == clip.name) settings = c;
            }
            Assert.IsNotNull(settings, "Réglages d'import introuvables pour " + clip.name);
            if (!settings.keepOriginalOrientation) failures.Add(clip.name);
        }

        // Garde anti-vacuité : les arbres de déplacement ont bien été trouvés (debout, accroupi,
        // allongé : une douzaine de clips).
        Assert.GreaterOrEqual(seen.Count, 10, "Trop peu de clips de déplacement trouvés : les arbres « …Marche » ont-ils changé de nom ?");
        Assert.IsEmpty(failures, "Racine orientée sur le buste au lieu de l'original : " + string.Join(", ", failures));
    }

    [Test]
    public void AucunClipDeDeplacement_NEstMisEnMiroirDansLArbre()
    {
        var mirrored = new List<string>();
        foreach (ChildMotion child in MovementChildren())
        {
            if (child.mirror) mirrored.Add(child.motion.name);
        }
        Assert.IsEmpty(mirrored, "Miroir dans l'arbre (à faire à l'import) : " + string.Join(", ", mirrored));
    }
}
