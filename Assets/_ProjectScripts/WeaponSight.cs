using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Un viseur à écran numérique, monté sur une arme (2026-10-09).
///
/// Le viseur intégré du fusil d'assaut est une lunette numérique fermée : un écran à l'arrière, une
/// lentille à l'avant, un boîtier plein entre les deux. On ne voit donc pas À TRAVERS : l'écran
/// AFFICHE ce que voit le viseur. En vue subjective, une caméra placée à l'œil filme le monde dans
/// l'axe du regard, et le shader de l'écran (DuelArena/ViseurEcran) montre, en chaque point de
/// l'écran, le monde qui se trouve derrière. Au grossissement 1 l'écran paraît donc transparent,
/// raccord au pixel près avec ce qui l'entoure ; au-delà, l'image grossit autour du centre, et seule
/// la caméra du viseur se resserre (le grossissement se choisira dans l'armurerie, GDD § 6).
///
/// La réticule est dessinée au centre de l'écran du joueur, là où partent les balles (le tir part de
/// la caméra), même quand le fusil balance au rythme des pas.
///
/// Vu par les autres, le fusil garde le matériau d'origine de l'écran : rien de tout ceci n'existe
/// hors de la vue subjective du propriétaire (catégorie C pure). La caméra du viseur ne tourne que
/// pendant la visée : un rendu de plus du décor, en petit, et seulement à ce moment-là.
/// </summary>
[DisallowMultipleComponent]
public class WeaponSight : MonoBehaviour
{
    [Tooltip("Le rendu qui porte l'écran du viseur (le fusil fusionné).")]
    [SerializeField] private Renderer screenRenderer;

    [Tooltip("Index du matériau de l'écran dans ce rendu.")]
    [SerializeField] private int screenMaterialIndex = 6;

    [Tooltip("Matériau de l'écran en vue subjective (shader DuelArena/ViseurEcran). Copié pour chaque joueur.")]
    [SerializeField] private Material screenMaterial;

    [Tooltip("Grossissement de l'image (1 = l'écran paraît transparent). Choix de l'armurerie, plus tard.")]
    [SerializeField, Min(1f)] private float magnification = 1f;

    [Tooltip("Résolution de l'image du viseur (pixels de côté).")]
    [SerializeField] private int resolution = 640;

    /// <summary>Étendue filmée par la caméra du viseur au grossissement 1, en tangente de l'angle
    /// depuis l'axe : 0,55 couvre l'écran entier en visée, avec de la marge. Le shader fait la même
    /// hypothèse (_Projection.z).</summary>
    public const float FilmedHalfTangent = 0.55f;

    private Camera sightCamera;
    private RenderTexture target;
    private Material screenInstance;
    private static readonly int ProjectionId = Shader.PropertyToID("_Projection");
    private static readonly int ScreenTextureId = Shader.PropertyToID("_Ecran");

    public float Magnification => magnification;

    /// <summary>
    /// Allume l'écran numérique, en vue subjective seulement : crée la caméra du viseur sous la
    /// caméra du joueur, et pose sur l'écran une copie du matériau qui affiche son image.
    /// </summary>
    public void EnableDigitalScreen(Camera playerCamera)
    {
        if (screenRenderer == null || screenMaterial == null || playerCamera == null || sightCamera != null) return;

        target = new RenderTexture(resolution, resolution, 24) { name = "ViseurEcran" };
        var go = new GameObject("CameraViseur");
        go.transform.SetParent(playerCamera.transform, false);
        sightCamera = go.AddComponent<Camera>();
        sightCamera.CopyFrom(playerCamera);
        sightCamera.targetTexture = target;
        sightCamera.aspect = 1f;
        sightCamera.depth = playerCamera.depth - 1;
        sightCamera.enabled = false;
        UniversalAdditionalCameraData data = sightCamera.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = false;
        data.cameraStack.Clear();

        screenInstance = new Material(screenMaterial) { name = screenMaterial.name + " (vue subjective)" };
        screenInstance.SetTexture(ScreenTextureId, target);
        Material[] materials = screenRenderer.sharedMaterials;
        if (screenMaterialIndex >= 0 && screenMaterialIndex < materials.Length)
        {
            materials[screenMaterialIndex] = screenInstance;
            screenRenderer.sharedMaterials = materials;
        }
    }

    /// <summary>
    /// À chaque image, en vue subjective : la caméra du viseur ne filme que pendant la visée, au
    /// champ qui correspond au grossissement ; l'écran reçoit le rapport entre le champ du monde et
    /// celui des bras (dessinés par une autre caméra, à un autre champ) pour retrouver, en chaque
    /// point, la direction du monde qu'il cache.
    /// </summary>
    public void UpdateScreen(Camera worldCamera, Camera armsCamera, float aimBlend)
    {
        if (sightCamera == null || screenInstance == null || worldCamera == null) return;

        bool active = aimBlend > 0.01f;
        sightCamera.enabled = active;
        sightCamera.fieldOfView = 2f * Mathf.Atan(FilmedHalfTangent / magnification) * Mathf.Rad2Deg;
        sightCamera.cullingMask = worldCamera.cullingMask;

        float worldTan = Mathf.Tan(worldCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float armsTan = Mathf.Tan((armsCamera != null ? armsCamera.fieldOfView : worldCamera.fieldOfView) * 0.5f * Mathf.Deg2Rad);
        // L'image s'allume pendant la seconde moitié de la montée à l'œil, quand l'écran arrive au
        // centre : avant, on y verrait surtout le bord de ce que filme le viseur.
        float lit = active ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.9f, aimBlend)) : 0f;
        screenInstance.SetVector(ProjectionId, new Vector4(worldTan / armsTan, magnification, FilmedHalfTangent, lit));
    }

    private void OnDestroy()
    {
        if (sightCamera != null) Destroy(sightCamera.gameObject);
        if (target != null) { target.Release(); Destroy(target); }
        if (screenInstance != null) Destroy(screenInstance);
    }
}
