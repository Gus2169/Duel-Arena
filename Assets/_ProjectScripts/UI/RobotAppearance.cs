using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Une copie de l'apparence du robot, pour le montrer hors de la partie : le robot de Personnalisation
/// (CharacterPreviewController) et celui de l'écran de victoire (VictoryStage).
///
/// Copiée sous un parent inactif, puis dépouillée de ses scripts (ils se réveilleraient sinon, sans
/// joueur autour d'eux), de ses armes, caméras, lumières, sons et colliders ; posée sur le layer
/// `Preview` (9), que seules les caméras de ces écrans dessinent. Sans arme ni gant : le gant du taser
/// est ôté et la main gauche réapparaît (HeldWeapons.WearTaserGlove). Même principe que l'épave
/// (RobotCorpse).
/// </summary>
public static class RobotAppearance
{
    /// <summary>Copie `assetModel` (l'enfant « Model » du préfab joueur) sous `parent`, à l'origine de
    /// son repère.</summary>
    public static GameObject Copy(GameObject assetModel, Transform parent, string name)
    {
        var holder = new GameObject(name + " (préparation)");
        holder.SetActive(false);
        GameObject copy = Object.Instantiate(assetModel, holder.transform, false);
        copy.name = name;

        // Les armes d'abord (repérées par leur script), puis tout ce qui n'est pas de l'apparence.
        foreach (WeaponModel weapon in copy.GetComponentsInChildren<WeaponModel>(true)) Object.DestroyImmediate(weapon.gameObject);
        MonoBehaviour[] scripts = copy.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = scripts.Length - 1; i >= 0; i--) Object.DestroyImmediate(scripts[i]);
        foreach (Camera c in copy.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(c);
        foreach (Light l in copy.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
        foreach (AudioSource s in copy.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(s);
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (ParticleSystem p in copy.GetComponentsInChildren<ParticleSystem>(true)) p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        int layer = PreviewLayer.Index;
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
        {
            if (layer >= 0) t.gameObject.layer = layer;
        }
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.On;
            r.forceRenderingOff = false;
            if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        }
        HeldWeapons.WearTaserGlove(copy.GetComponent<Animator>(), false);

        copy.transform.SetParent(parent, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.transform.localScale = Vector3.one;
        if (Application.isPlaying) Object.Destroy(holder);
        else Object.DestroyImmediate(holder);
        RenderStyle.TrackRobot(copy);   // dans le rendu choisi dans les Options
        return copy;
    }

    /// <summary>Les pièces de la copie que PlayerPaint peint (`paintedRenderers`, lues sur l'asset du préfab
    /// joueur et retrouvées par leur chemin sous « Model »), une seule source pour « quoi peindre ».</summary>
    public static Renderer[] PaintedParts(GameObject playerPrefab, Transform assetModel, GameObject copy)
    {
        PlayerPaint assetPaint = playerPrefab != null ? playerPrefab.GetComponent<PlayerPaint>() : null;
        if (assetPaint == null || assetModel == null || copy == null) return System.Array.Empty<Renderer>();
        var found = new System.Collections.Generic.List<Renderer>();
        foreach (Renderer assetRenderer in assetPaint.PaintedRenderers)
        {
            if (assetRenderer == null) continue;
            string path = RelativePath(assetModel, assetRenderer.transform);
            Transform match = path == null ? null : path.Length == 0 ? copy.transform : copy.transform.Find(path);
            Renderer r = match != null ? match.GetComponent<Renderer>() : null;
            if (r != null) found.Add(r);
        }
        return found.ToArray();
    }

    /// <summary>Peint la copie comme PlayerPaint peint le robot en jeu (PlayerPaint.PaintPart, les mêmes
    /// pièces). Renvoie les pièces peintes, pour la repeindre.</summary>
    public static Renderer[] Paint(GameObject playerPrefab, Transform assetModel, GameObject copy, Color32 paint)
    {
        Renderer[] parts = PaintedParts(playerPrefab, assetModel, copy);
        Paint(parts, paint);
        return parts;
    }

    /// <summary>Repeint des pièces déjà trouvées.</summary>
    public static void Paint(Renderer[] parts, Color32 paint)
    {
        var block = new MaterialPropertyBlock();
        foreach (Renderer r in parts) PlayerPaint.PaintPart(r, paint, block, true);
    }

    // Le chemin de `target` sous `root` (« Armature/Corps »), vide pour la racine elle-même, null hors de lui.
    private static string RelativePath(Transform root, Transform target)
    {
        var segments = new System.Collections.Generic.List<string>();
        for (Transform t = target; t != null; t = t.parent)
        {
            if (t == root)
            {
                segments.Reverse();
                return string.Join("/", segments);
            }
            segments.Add(t.name);
        }
        return null;
    }
}
