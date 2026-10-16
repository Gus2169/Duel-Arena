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
        return copy;
    }
}
