using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Le plateau tournant du menu : une caméra dédiée, isolée sur le layer `Preview` (9, aucun autre
/// objet de la scène dessus — la caméra de fond du menu l'exclut de son masque de rendu), avec
/// deux pivots (personnage, arme) qu'on fait tourner à la souris. Partagé par
/// CharacterPreviewController et WeaponCustomizationController : les deux écrans ne sont jamais
/// visibles en même temps, donc une seule caméra suffit.
///
/// La caméra dessine dans une texture créée ici, AUX DIMENSIONS EN PIXELS du cadre qui l'affiche
/// (recalculées à chaque mise en page du cadre) : une texture carrée posée dans un cadre en
/// portrait ou en paysage laissait des bandes noires, et la netteté suivait mal la résolution.
/// Le cadrage se refait à chaque changement de dimensions, puisqu'il dépend du rapport largeur /
/// hauteur.
///
/// Le robot tourne seul au repos ; l'arme reste de profil, canon vers la droite, tant qu'on ne la
/// fait pas tourner — comme dans l'armurerie de Call of Duty, où l'on juge un accessoire de profil.
///
/// Posé À L'ORIGINE DU MONDE, rotation identité : les bornes d'un sujet exprimées dans le repère de
/// son pivot sont alors directement des positions du monde.
/// </summary>
public class PreviewStage : MonoBehaviour
{
    [SerializeField] private Camera previewCamera;
    [SerializeField] private Transform characterPivot;
    [SerializeField] private Transform weaponPivot;

    [Tooltip("Vitesse de rotation du robot au repos (°/s), quand on ne le fait pas tourner à la souris. L'arme ne tourne pas seule.")]
    [SerializeField] private float characterAutoRotateSpeed = 14f;

    [Tooltip("Rotation (°) par pixel de glissement horizontal.")]
    [SerializeField] private float dragDegreesPerPixel = 0.35f;

    [Tooltip("Champ de vision vertical de la caméra d'aperçu (°).")]
    [SerializeField] private float cameraFieldOfView = 30f;

    [Tooltip("Marge autour du sujet : 1 = bord à bord.")]
    [SerializeField] private float framingMargin = 1.15f;

    [Tooltip("Côté maximal de la texture d'aperçu (px).")]
    [SerializeField] private int maxTextureSize = 2048;

    // Le robot, de trois quarts face ; l'arme, de profil côté droit (canon vers la droite de
    // l'écran), un peu au-dessus et un peu en arrière pour garder du relief.
    private static readonly Vector3 CharacterViewDirection = new Vector3(0f, 0.1f, 1f);
    private static readonly Vector3 WeaponViewDirection = new Vector3(1f, 0.16f, -0.16f);
    private const float CharacterStartYaw = -25f;

    private Transform activePivot;
    private float autoRotateSpeed;
    private bool dragging;

    private RenderTexture texture;
    private Image output;
    private readonly HashSet<Image> wiredViewports = new();

    private bool hasFraming;
    private Vector3 framingCenter;
    private float framingHalfHeight;
    private float framingRadius;
    private Vector3 framingDirection;

    public Transform CharacterPivot => characterPivot;
    public Transform WeaponPivot => weaponPivot;

    private void Awake()
    {
        if (previewCamera == null) return;
        previewCamera.fieldOfView = cameraFieldOfView;
        previewCamera.targetTexture = null;
        previewCamera.enabled = false;
    }

    private void OnDestroy() => ReleaseTexture();

    private void Update()
    {
        if (activePivot == null || dragging || autoRotateSpeed == 0f) return;
        activePivot.Rotate(Vector3.up, autoRotateSpeed * Time.deltaTime, Space.World);
    }

    /// <summary>Montre le robot dans `viewport`, de trois quarts face, tournant seul.</summary>
    public void ShowCharacter(Image viewport)
    {
        if (characterPivot == null) return;
        Activate(characterPivot, weaponPivot, viewport);
        autoRotateSpeed = characterAutoRotateSpeed;

        characterPivot.localRotation = Quaternion.identity;
        Bounds bounds = ComputeWorldBounds(characterPivot);
        characterPivot.localRotation = Quaternion.Euler(0f, CharacterStartYaw, 0f);

        SetFraming(bounds.center, bounds.extents.y, HorizontalRadius(bounds), CharacterViewDirection);
    }

    /// <summary>Montre l'arme dans `viewport`, de profil. `localBounds` est donné dans le repère
    /// du pivot (l'appelant y a centré l'arme) et couvre la place de TOUS ses accessoires
    /// possibles, pas seulement de ceux choisis : le cadrage ne bouge pas quand on en change (voir
    /// WeaponCustomizationController).</summary>
    public void ShowWeapon(Bounds localBounds, Image viewport)
    {
        if (weaponPivot == null) return;
        Activate(weaponPivot, characterPivot, viewport);
        autoRotateSpeed = 0f;
        weaponPivot.localRotation = Quaternion.identity;

        SetFraming(weaponPivot.TransformPoint(localBounds.center), localBounds.extents.y,
            HorizontalRadius(localBounds), WeaponViewDirection);
    }

    public void Hide()
    {
        activePivot = null;
        dragging = false;
        UpdateCameraEnabled();
        if (characterPivot != null) characterPivot.gameObject.SetActive(false);
        if (weaponPivot != null) weaponPivot.gameObject.SetActive(false);
    }

    private void Activate(Transform pivot, Transform other, Image viewport)
    {
        activePivot = pivot;
        dragging = false;
        pivot.gameObject.SetActive(true);
        if (other != null) other.gameObject.SetActive(false);
        BindOutput(viewport);
        UpdateCameraEnabled();
    }

    /// <summary>La caméra ne tourne que si un sujet est montré ET qu'elle a sa texture : sans
    /// texture, une caméra active dessinerait à l'écran, par-dessus le menu.</summary>
    private void UpdateCameraEnabled()
    {
        if (previewCamera == null) return;
        previewCamera.enabled = activePivot != null && texture != null && previewCamera.targetTexture == texture;
    }

    // ------------------------------------------------------------------
    // Cadre UI : texture aux bonnes dimensions, rotation à la souris
    // ------------------------------------------------------------------

    private void BindOutput(Image viewport)
    {
        if (viewport == null) return;
        if (output != viewport)
        {
            if (output != null) output.image = null;
            output = viewport;
            output.scaleMode = ScaleMode.ScaleToFit;
        }

        if (wiredViewports.Add(viewport))
        {
            viewport.RegisterCallback<GeometryChangedEvent>(_ => { if (output == viewport) ResizeTexture(); });
            viewport.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (output != viewport) return;
                viewport.CapturePointer(evt.pointerId);
                dragging = true;
            });
            viewport.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (output == viewport && viewport.HasPointerCapture(evt.pointerId)) Drag(evt.deltaPosition.x);
            });
            viewport.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (viewport.HasPointerCapture(evt.pointerId)) viewport.ReleasePointer(evt.pointerId);
                dragging = false;
            });
            viewport.RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);
        }

        // Un écran qui vient d'être affiché n'est pas encore mis en page : sa taille arrivera par
        // GeometryChangedEvent. Déjà mis en page (on revient sur l'écran), on l'a tout de suite.
        ResizeTexture();
    }

    private void Drag(float deltaXPixels)
    {
        if (activePivot == null) return;
        // Angle négatif : la face tournée vers la caméra suit la souris, quel que soit le côté
        // d'où on regarde le plateau.
        activePivot.Rotate(Vector3.up, -deltaXPixels * dragDegreesPerPixel, Space.World);
    }

    private void ResizeTexture()
    {
        if (output == null || previewCamera == null) return;
        Rect rect = output.contentRect;
        if (float.IsNaN(rect.width) || float.IsNaN(rect.height) || rect.width < 1f || rect.height < 1f) return;

        float pixelsPerPoint = output.panel != null ? output.panel.scaledPixelsPerPoint : 1f;
        int width = Mathf.Clamp(Mathf.RoundToInt(rect.width * pixelsPerPoint), 16, maxTextureSize);
        int height = Mathf.Clamp(Mathf.RoundToInt(rect.height * pixelsPerPoint), 16, maxTextureSize);

        if (texture == null || texture.width != width || texture.height != height)
        {
            ReleaseTexture();
            texture = new RenderTexture(width, height, 24) { name = "PreviewStage", antiAliasing = 4 };
            texture.Create();
            previewCamera.targetTexture = texture;
        }

        previewCamera.aspect = (float)width / height;
        output.image = texture;
        ApplyFraming();
        UpdateCameraEnabled();
    }

    private void ReleaseTexture()
    {
        if (texture == null) return;
        if (previewCamera != null && previewCamera.targetTexture == texture)
        {
            previewCamera.enabled = false;
            previewCamera.targetTexture = null;
        }
        texture.Release();
        Destroy(texture);
        texture = null;
    }

    // ------------------------------------------------------------------
    // Cadrage
    // ------------------------------------------------------------------

    private void SetFraming(Vector3 center, float halfHeight, float horizontalRadius, Vector3 direction)
    {
        hasFraming = true;
        framingCenter = center;
        framingHalfHeight = Mathf.Max(halfHeight, 0.02f);
        framingRadius = Mathf.Max(horizontalRadius, 0.02f);
        framingDirection = direction.normalized;
        ApplyFraming();
    }

    /// <summary>Recule la caméra juste assez pour que le sujet tienne dans le cadre, en hauteur
    /// comme en largeur, quelle que soit sa rotation autour de la verticale (d'où le rayon
    /// horizontal, et non la seule largeur vue de face).</summary>
    private void ApplyFraming()
    {
        if (!hasFraming || previewCamera == null) return;

        float tanHalfV = Mathf.Tan(cameraFieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanHalfH = tanHalfV * Mathf.Max(previewCamera.aspect, 0.1f);
        float fit = Mathf.Max(framingHalfHeight / tanHalfV, framingRadius / tanHalfH) * framingMargin;

        // + un quart du rayon : tourné de trois quarts, le bout du sujet tourné vers la caméra est
        // plus proche, donc plus grand. De profil, l'arme occupe ainsi les trois quarts du cadre.
        float distance = fit + framingRadius * 0.25f;
        previewCamera.transform.position = framingCenter + framingDirection * distance;
        previewCamera.transform.LookAt(framingCenter, Vector3.up);
        previewCamera.nearClipPlane = Mathf.Max(0.01f, distance - framingRadius * 2f);
        previewCamera.farClipPlane = distance + framingRadius * 4f + 1f;
    }

    private static float HorizontalRadius(Bounds bounds)
        => new Vector2(bounds.extents.x, bounds.extents.z).magnitude;

    /// <summary>Bornes du sujet dans sa pose ACTUELLE. Un maillage animé se mesure par ses os :
    /// ses propres bornes sont une boîte fixe, gonflée pour couvrir toutes les poses possibles
    /// (2 m × 1,8 m pour le robot), et le cadrage le montrait deux fois trop petit. Les os de
    /// Mixamo vont du bout de la tête au bout des orteils ; la marge couvre l'épaisseur du corps.
    /// Les effets (particules) sont ignorés : leurs bornes vides à l'origine fausseraient tout.</summary>
    private static Bounds ComputeWorldBounds(Transform root)
    {
        // Un modèle tout juste réactivé n'a pas encore été évalué par son Animator : sans ça, on
        // mesurerait la pose de repos (bras en croix).
        foreach (Animator animator in root.GetComponentsInChildren<Animator>())
        {
            if (animator.isActiveAndEnabled) animator.Update(0f);
        }

        const float SkinMargin = 0.08f;
        bool any = false;
        Bounds bounds = default;

        void Add(Bounds b)
        {
            if (!any) { bounds = b; any = true; }
            else bounds.Encapsulate(b);
        }

        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (r is SkinnedMeshRenderer skinned && skinned.bones.Length > 0)
            {
                foreach (Transform bone in skinned.bones)
                {
                    if (bone != null) Add(new Bounds(bone.position, Vector3.one * (SkinMargin * 2f)));
                }
            }
            else Add(r.bounds);
        }

        return any ? bounds : new Bounds(root.position, Vector3.one * 0.3f);
    }
}
