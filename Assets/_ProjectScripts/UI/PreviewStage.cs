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
/// Rien ne tourne seul (2026-10-27, demandé par l'utilisateur) : le robot reste de trois quarts face et
/// l'arme de profil, canon vers la droite — comme dans l'armurerie de Call of Duty, où l'on juge un
/// accessoire de profil —, tant qu'on ne les fait pas tourner à la souris. Lâchés en plein geste, ils
/// continuent un instant sur leur lancée et s'arrêtent en douceur (une légère inertie, StepSpin).
///
/// Posé À L'ORIGINE DU MONDE, rotation identité : les bornes d'un sujet exprimées dans le repère de
/// son pivot sont alors directement des positions du monde.
/// </summary>
public class PreviewStage : MonoBehaviour
{
    [SerializeField] private Camera previewCamera;
    [SerializeField] private Transform characterPivot;
    [SerializeField] private Transform weaponPivot;

    [Tooltip("Rotation (°) par pixel de glissement horizontal.")]
    [SerializeField] private float dragDegreesPerPixel = 0.35f;

    [Tooltip("Inertie : temps (s) en lequel la rotation lancée à la souris perd les deux tiers de sa vitesse. Petit : la rotation s'arrête presque aussitôt.")]
    [SerializeField] private float spinDecayTime = 0.16f;

    [Tooltip("Vitesse de rotation maximale (°/s) que garde un sujet lâché en plein geste.")]
    [SerializeField] private float maxSpinSpeed = 720f;

    [Tooltip("La plus petite taille d'arme que le cadrage remplit (demi-longueur, m) : une arme plus courte, un pistolet, se montre à sa taille par rapport à celle-là, plutôt que d'emplir le cadre comme un fusil.")]
    [SerializeField] private float minWeaponFramingRadius = 0.26f;

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
    private bool dragging;
    private float pendingDragPixels;   // le glissement reçu depuis la dernière image
    private float spinSpeed;           // °/s : la vitesse du geste, puis celle de la lancée

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

        // Fond transparent (2026-10-16) : le sujet se pose sur le fond de l'écran, sans cadre, comme
        // l'arme de l'armurerie de Call of Duty. Sans HDR, URP garde la couche alpha jusqu'à la texture
        // (son tampon HDR par défaut n'en a pas) ; rien ici ne dépasse 1, l'image ne change pas.
        Color background = previewCamera.backgroundColor;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(background.r, background.g, background.b, 0f);
        previewCamera.allowHDR = false;
    }

    private void OnDestroy() => ReleaseTexture();

    private void Update()
    {
        if (activePivot == null) return;
        float dt = Time.unscaledDeltaTime;
        float degrees = StepSpin(ref spinSpeed, dragging, pendingDragPixels * -dragDegreesPerPixel, dt, spinDecayTime, maxSpinSpeed);
        pendingDragPixels = 0f;
        // Angle négatif pour un glissement vers la droite : la face tournée vers la caméra suit la souris,
        // quel que soit le côté d'où on regarde le plateau.
        if (degrees != 0f) activePivot.Rotate(Vector3.up, degrees, Space.World);
    }

    /// <summary>
    /// Une image de la rotation à la souris, avec sa lancée : rend l'angle (°) dont tourner le sujet. Tenu,
    /// il suit le geste (`dragDegrees`, ce que la souris a glissé depuis l'image d'avant), et `speed` retient
    /// la vitesse du geste, lissée (une souris arrêtée avant de lâcher ne lance rien) ; lâché, il continue à
    /// cette vitesse, qui décroît de façon exponentielle (`decayTime`) jusqu'à l'arrêt. Fonction pure, testée.
    /// </summary>
    public static float StepSpin(ref float speed, bool dragging, float dragDegrees, float dt, float decayTime, float maxSpeed)
    {
        if (float.IsNaN(speed) || float.IsInfinity(speed)) speed = 0f;
        if (!(dt > 0f)) return dragging ? dragDegrees : 0f;
        if (dragging)
        {
            float measured = Mathf.Clamp(dragDegrees / dt, -maxSpeed, maxSpeed);
            // Lissée sur ~50 ms : un à-coup d'une image ne lance pas le sujet, une souris qui s'arrête l'arrête.
            speed = Mathf.Lerp(speed, measured, 1f - Mathf.Exp(-dt / 0.05f));
            return dragDegrees;
        }
        if (decayTime <= 0f) { speed = 0f; return 0f; }
        float before = speed;
        speed *= Mathf.Exp(-dt / decayTime);
        if (Mathf.Abs(speed) < 2f) speed = 0f;
        // L'angle parcouru pendant l'image, à la vitesse moyenne de la décroissance.
        return (before + speed) * 0.5f * dt;
    }

    /// <summary>Montre le robot dans `viewport`, de trois quarts face, immobile tant qu'on ne le fait pas tourner.</summary>
    public void ShowCharacter(Image viewport)
    {
        if (characterPivot == null) return;
        Activate(characterPivot, weaponPivot, viewport);

        characterPivot.localRotation = Quaternion.identity;
        Bounds bounds = ComputeWorldBounds(characterPivot);
        characterPivot.localRotation = Quaternion.Euler(0f, CharacterStartYaw, 0f);

        SetFraming(bounds.center, bounds.extents.y, HorizontalRadius(bounds), CharacterViewDirection);
    }

    /// <summary>Montre l'arme dans `viewport`, de profil (ou dans la rotation qu'on lui a donnée, sans
    /// `resetRotation` : un autre modèle de la même arme se regarde sous le même angle). `localBounds`
    /// est donné dans le repère du pivot (l'appelant y a centré l'arme) et couvre la place de TOUS ses
    /// accessoires possibles, pas seulement de ceux choisis : le cadrage ne bouge pas quand on en change
    /// (voir WeaponCustomizationController).</summary>
    public void ShowWeapon(Bounds localBounds, Image viewport, bool resetRotation = true)
    {
        if (weaponPivot == null) return;
        Quaternion rotation = weaponPivot.localRotation;
        Activate(weaponPivot, characterPivot, viewport);
        weaponPivot.localRotation = Quaternion.identity;

        // Une arme courte (un pistolet) garde sa taille par rapport aux autres : le cadrage ne la grossit pas
        // jusqu'à emplir le cadre (2026-10-27, « un peu trop proches » pour l'utilisateur).
        SetFraming(weaponPivot.TransformPoint(localBounds.center), localBounds.extents.y,
            WeaponFramingRadius(HorizontalRadius(localBounds), minWeaponFramingRadius), WeaponViewDirection);
        // Le cadrage ne dépend pas de la rotation : l'arme est centrée sur le pivot, et le rayon
        // horizontal couvre tous les angles.
        if (!resetRotation) weaponPivot.localRotation = rotation;
    }

    public void Hide()
    {
        activePivot = null;
        dragging = false;
        UpdateCameraEnabled();
        if (characterPivot != null) characterPivot.gameObject.SetActive(false);
        if (weaponPivot != null) weaponPivot.gameObject.SetActive(false);
    }

    /// <summary>Le rayon (m) sur lequel cadrer une arme de rayon horizontal `radius` : le sien, sauf une arme
    /// plus courte que `minRadius`, montrée à sa taille dans le cadre d'une arme de `minRadius`. Fonction
    /// pure, testée.</summary>
    public static float WeaponFramingRadius(float radius, float minRadius)
        => Mathf.Max(float.IsNaN(radius) ? 0f : radius, Mathf.Max(0f, minRadius));

    private void Activate(Transform pivot, Transform other, Image viewport)
    {
        activePivot = pivot;
        dragging = false;
        pendingDragPixels = 0f;
        spinSpeed = 0f;
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
                spinSpeed = 0f;   // la main reprend le sujet : sa lancée s'arrête
            });
            viewport.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (output == viewport && viewport.HasPointerCapture(evt.pointerId)) pendingDragPixels += evt.deltaPosition.x;
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
    /// Tout le squelette compte, pas seulement les os qui portent la peau (2026-10-27) : celle du
    /// prototype (le Y Bot) ne porte pas le bout de la tête, et sa vignette coupait la tête, plus
    /// grande que celles des autres châssis.
    /// Les effets (particules) sont ignorés : leurs bornes vides à l'origine fausseraient tout.</summary>
    internal static Bounds ComputeWorldBounds(Transform root)
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

        var skeletons = new HashSet<Transform>();
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (r is SkinnedMeshRenderer skinned && skinned.bones.Length > 0)
            {
                foreach (Transform bone in skinned.bones)
                {
                    if (bone != null) Add(new Bounds(bone.position, Vector3.one * (SkinMargin * 2f)));
                }
                // Le reste du squelette sous l'os racine : les bouts (de la tête, des doigts, des orteils).
                Transform skeleton = skinned.rootBone;
                if (skeleton == null || !skeletons.Add(skeleton)) continue;
                foreach (Transform bone in skeleton.GetComponentsInChildren<Transform>())
                    Add(new Bounds(bone.position, Vector3.one * (SkinMargin * 2f)));
            }
            else Add(r.bounds);
        }

        return any ? bounds : new Bounds(root.position, Vector3.one * 0.3f);
    }
}
