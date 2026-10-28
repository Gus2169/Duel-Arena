using UnityEditor;
using UnityEngine;

/// <summary>
/// Les clips de la garde du spectateur (2026-10-27) : les bras de « Standing Block Idle » et de sa
/// réaction au choc, « Standing Block React Large » (Mixamo), les poings fermés du direct du taser.
///
/// Les seules gardes de Mixamo qui gardent les hanches presque de face (8 à 18°) ont les mains ouvertes,
/// paumes en avant ; les gardes de boxe, poings fermés, ont les hanches tournées de 35 à 57° et partent
/// de travers sur un haut du corps de face (mesuré le 2026-10-27). D'où ces clips : les courbes des
/// premières, sauf les doigts, figés sur le poing serré de « Taser Punch » au moment du direct.
///
/// Duel Arena > Animations > Construire la garde du spectateur : on relance le menu, on ne retouche pas
/// les clips. Le relancer garde leur identifiant (l'Animator les référence).
/// </summary>
public static class SpectatorGuardClips
{
    private const string Folder = "Assets/_ProjectArt/Animations/MainsNues/";
    public const string GuardPath = Folder + "Garde Poings.anim";
    public const string GuardHitPath = Folder + "Garde Poings Choc.anim";

    /// <summary>L'instant du direct du taser où le poing est serré (doigts à -1,47 en moyenne).</summary>
    private const float FistTime = 0.4f;

    [MenuItem("Duel Arena/Animations/Construire la garde du spectateur")]
    public static void Build()
    {
        AnimationClip fist = Clip("Assets/_ProjectArt/Animations/New/Y Bot@Taser Punch.fbx", "Taser Punch");
        AnimationClip guard = Clip(Folder + "Y Bot@Standing Block Idle.fbx", "Garde");
        AnimationClip hit = Clip(Folder + "Y Bot@Standing Block React Large.fbx", "Garde Choc");
        if (fist == null || guard == null || hit == null)
        {
            Debug.LogError("[Garde] Il manque un clip source (Taser Punch, Standing Block Idle ou Standing Block React Large).");
            return;
        }
        Write(WithFists(guard, fist, "Garde Poings"), GuardPath);
        Write(WithFists(hit, fist, "Garde Poings Choc"), GuardHitPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[Garde] Clips de la garde du spectateur reconstruits.");
    }

    /// <summary>Les courbes de `arms`, sauf celles des doigts, figées sur le poing de `fist` à FistTime.
    /// Les réglages du clip (boucle, racine gardée dans la pose) sont ceux de `arms`.</summary>
    public static AnimationClip WithFists(AnimationClip arms, AnimationClip fist, string name)
    {
        var clip = new AnimationClip { name = name, frameRate = arms.frameRate };
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(arms))
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(arms, binding);
            if (IsFinger(binding.propertyName))
            {
                AnimationCurve source = AnimationUtility.GetEditorCurve(fist, binding);
                float value = source != null ? source.Evaluate(FistTime) : curve.Evaluate(0f);
                curve = AnimationCurve.Constant(0f, arms.length, value);
            }
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
        AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(arms));
        return clip;
    }

    /// <summary>Un muscle de doigt (« LeftHand.Index.1 Stretched », « RightHand.Thumb.Spread »...).</summary>
    public static bool IsFinger(string property)
        => property.Contains("Thumb") || property.Contains("Index") || property.Contains("Middle")
           || property.Contains("Ring") || property.Contains("Little");

    private static AnimationClip Clip(string path, string name)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (o is AnimationClip c && c.name == name) return c;
        }
        return null;
    }

    private static void Write(AnimationClip clip, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(clip, path);
            return;
        }
        EditorUtility.CopySerialized(clip, existing);
        Object.DestroyImmediate(clip);
        EditorUtility.SetDirty(existing);
    }
}
