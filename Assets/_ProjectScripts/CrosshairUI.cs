using UnityEngine;

/// <summary>
/// Réticule minimaliste affiché en Game view. PLACEHOLDER assumé, en attendant une vraie arme
/// visible à l'écran (viewmodel 3D + animations) et un vrai HUD. Objectif immédiat : pouvoir viser
/// sans dépendre de la fenêtre Scene. 100% local, aucune dépendance réseau.
///
/// Important : le point visé est calculé via aimCamera.WorldToScreenPoint plutôt que supposé au
/// centre géométrique de la fenêtre (Screen.width/2, Screen.height/2). Les deux coïncident dans la
/// majorité des configurations, mais pas forcément si le Viewport Rect de la caméra n'est pas
/// exactement (0,0,1,1), ou si un Lens Shift est actif (Physical Camera). En passant par la caméra
/// elle-même, le réticule reste garanti aligné avec le rayon de tir de WeaponController, quel que
/// soit le réglage caméra en cause.
///
/// Contrainte à respecter : "Aim Camera" doit être EXACTEMENT la même caméra que celle assignée
/// à WeaponController.aimCamera — c'est ce qui garantit l'alignement du réticule avec les impacts.
/// "Input Reader" est optionnel (sans lui le réticule ne se resserre pas en ADS).
///
/// Affiche aussi les MUNITIONS (2026-10-08) : compteur en bas à droite, barre de rechargement
/// sous le réticule, rappel de la touche quand le chargeur est vide. Et depuis le 2026-10-11, le
/// nom de l'arme en main, aucun réticule à la hanche au sniper (comme dans Call of Duty), et la
/// lunette plein écran du sniper. Même statut de placeholder, à remplacer par le HUD UI Toolkit
/// (GDD § 15).
/// </summary>
public class CrosshairUI : MonoBehaviour
{
    [Header("Référence caméra (obligatoire)")]
    [Tooltip("Doit être la MÊME caméra que celle assignée dans WeaponController.aimCamera.")]
    [SerializeField] private Camera aimCamera;

    [Header("Référence optionnelle (resserre le viseur en ADS)")]
    [SerializeField] private PlayerInputReader inputReader;

    [Header("Apparence - à la hanche")]
    [SerializeField] private float hipGap = 6f;
    [SerializeField] private float hipLineLength = 8f;
    [SerializeField] private float lineThickness = 2f;
    [SerializeField] private Color crosshairColor = Color.white;

    [Header("Apparence - visée (ADS)")]
    [SerializeField] private float adsGap = 2f;
    [SerializeField] private float adsLineLength = 4f;

    [Header("Point central (optionnel)")]
    [SerializeField] private bool showCenterDot = true;
    [SerializeField] private float centerDotSize = 2f;

    [Header("Munitions")]
    [Tooltip("L'arme du joueur. Trouvée dans les enfants si laissée vide.")]
    [SerializeField] private WeaponController weapon;
    [SerializeField] private Color ammoEmptyColor = new Color(1f, 0.4f, 0.3f);

    [Header("Lunette du sniper")]
    [Tooltip("Diamètre de l'ouverture de la lunette, en part de la hauteur de l'écran.")]
    [SerializeField, Range(0.5f, 1f)] private float scopeDiameter = 0.92f;
    [SerializeField] private Color scopeReticleColor = new Color(0f, 0f, 0f, 0.9f);

    private Texture2D pixel;
    private Texture2D scopeMask; // disque transparent sur fond noir, construit une fois
    private FirstPersonView firstPersonView;
    private GUIStyle ammoStyle;
    private GUIStyle nameStyle;
    private GUIStyle hintStyle;

    private void Awake()
    {
        // Texture2D.whiteTexture est un pixel blanc fourni par Unity, teinté ensuite via GUI.color
        // — évite de générer/gérer une texture custom pour un simple aplat de couleur.
        pixel = Texture2D.whiteTexture;
        if (weapon == null) weapon = GetComponentInChildren<WeaponController>();
        firstPersonView = GetComponent<FirstPersonView>();

        if (aimCamera == null)
        {
            Debug.LogError("CrosshairUI : assigne 'Aim Camera' (la même que WeaponController.aimCamera) dans l'inspecteur.", this);
        }
    }

    private void OnGUI()
    {
        if (aimCamera == null) return;

        // Un seul HUD par machine : celui du joueur local. Sans cette garde, chaque joueur affiché
        // dessinait son propre réticule — superposé au centre de l'écran, donc invisible — et
        // dessinerait aussi ses munitions par-dessus les nôtres.
        if (weapon != null && weapon.IsSpawned && !weapon.IsOwner) return;

        // Point sur l'axe de visée de la caméra, projeté en coordonnées écran par la caméra
        // elle-même : garanti cohérent avec l'origine/direction utilisées par WeaponController.Fire(),
        // peu importe le Viewport Rect ou d'autres réglages caméra.
        Vector3 aimWorldPoint = aimCamera.transform.position + aimCamera.transform.forward * 10f;
        Vector3 screenPoint = aimCamera.WorldToScreenPoint(aimWorldPoint);

        // Camera.WorldToScreenPoint place l'origine en bas à gauche (y vers le haut), alors que
        // OnGUI place l'origine en haut à gauche (y vers le bas) — d'où l'inversion du y ici.
        float cx = screenPoint.x;
        float cy = Screen.height - screenPoint.y;

        // Dans la lunette du sniper, elle couvre tout l'écran, avec sa réticule.
        if (firstPersonView != null && firstPersonView.Scoped)
        {
            DrawScope(cx, cy);
            DrawAmmo(cx, cy);
            GUI.color = Color.white;
            return;
        }

        // En visée, l'œil est dans l'axe du viseur : sa réticule marque le centre, celle-ci
        // s'efface. Les munitions restent.
        if (firstPersonView != null && firstPersonView.SightOnAxis)
        {
            DrawAmmo(cx, cy);
            GUI.color = Color.white;
            return;
        }

        bool isAiming = inputReader != null && inputReader.AimHeld;

        // Le sniper n'a pas de réticule à la hanche, comme dans Call of Duty (tranché le
        // 2026-10-11) : la balle part quand même au centre de l'écran, sans hasard (GDD § 3).
        if (!isAiming && firstPersonView != null && !firstPersonView.HipCrosshair)
        {
            DrawAmmo(cx, cy);
            GUI.color = Color.white;
            return;
        }
        float gap = isAiming ? adsGap : hipGap;
        float length = isAiming ? adsLineLength : hipLineLength;

        GUI.color = crosshairColor;

        // Haut / Bas / Gauche / Droite, décalés du centre par "gap" pour laisser un trou au milieu.
        GUI.DrawTexture(new Rect(cx - lineThickness * 0.5f, cy - gap - length, lineThickness, length), pixel);
        GUI.DrawTexture(new Rect(cx - lineThickness * 0.5f, cy + gap, lineThickness, length), pixel);
        GUI.DrawTexture(new Rect(cx - gap - length, cy - lineThickness * 0.5f, length, lineThickness), pixel);
        GUI.DrawTexture(new Rect(cx + gap, cy - lineThickness * 0.5f, length, lineThickness), pixel);

        if (showCenterDot)
        {
            GUI.DrawTexture(new Rect(cx - centerDotSize * 0.5f, cy - centerDotSize * 0.5f, centerDotSize, centerDotSize), pixel);
        }

        DrawAmmo(cx, cy);

        GUI.color = Color.white;
    }

    private void DrawAmmo(float cx, float cy)
    {
        if (weapon == null || weapon.MagazineSize <= 0) return;
        BuildStylesIfNeeded();

        int ammo = weapon.DisplayedAmmo;
        Color textColor = ammo == 0 ? ammoEmptyColor : crosshairColor;

        GUI.color = Color.white;
        ammoStyle.normal.textColor = textColor;
        float margin = Screen.height * 0.04f;
        GUI.Label(new Rect(0f, 0f, Screen.width - margin, Screen.height - margin), $"{ammo} / {weapon.MagazineSize}", ammoStyle);
        if (weapon.Data != null)
        {
            nameStyle.normal.textColor = crosshairColor;
            GUI.Label(new Rect(0f, 0f, Screen.width - margin, Screen.height - margin - ammoStyle.fontSize * 1.3f),
                      $"{weapon.DisplayedSlot + 1}  {weapon.Data.weaponName}", nameStyle);
        }

        float below = cy + hipGap + hipLineLength + 14f;
        if (weapon.IsReloading)
        {
            // Barre sous le réticule : c'est là que le regard se trouve pendant un duel. Fond sombre
            // et débordant, pour qu'elle se lise aussi sur un mur clair.
            const float width = 64f;
            const float height = 4f;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(cx - width * 0.5f - 1f, below - 1f, width + 2f, height + 2f), pixel);
            GUI.color = crosshairColor;
            GUI.DrawTexture(new Rect(cx - width * 0.5f, below, width * weapon.ReloadProgress, height), pixel);
        }
        else if (ammo == 0)
        {
            hintStyle.normal.textColor = textColor;
            GUI.Label(new Rect(cx - 150f, below - 4f, 300f, 30f), "R : recharger", hintStyle);
        }
    }

    /// <summary>La lunette plein écran : un disque ouvert au centre, du noir tout autour, et une
    /// réticule fine qui se croise au centre exact de l'écran, là où partent les balles.</summary>
    private void DrawScope(float cx, float cy)
    {
        if (scopeMask == null) scopeMask = BuildScopeMask(256);
        float diameter = Screen.height * scopeDiameter;
        float left = cx - diameter * 0.5f, top = cy - diameter * 0.5f;

        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, top), pixel);
        GUI.DrawTexture(new Rect(0f, top + diameter, Screen.width, Screen.height - top - diameter), pixel);
        GUI.DrawTexture(new Rect(0f, top, left, diameter), pixel);
        GUI.DrawTexture(new Rect(left + diameter, top, Screen.width - left - diameter, diameter), pixel);
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(left, top, diameter, diameter), scopeMask);

        // Réticule : deux traits fins, plus épais vers le bord, interrompus au centre.
        GUI.color = scopeReticleColor;
        float r = diameter * 0.5f;
        GUI.DrawTexture(new Rect(left, cy - 1f, r - 6f, 2f), pixel);
        GUI.DrawTexture(new Rect(cx + 6f, cy - 1f, r - 6f, 2f), pixel);
        GUI.DrawTexture(new Rect(cx - 1f, cy + 6f, 2f, r - 6f), pixel);
        GUI.DrawTexture(new Rect(cx - 1f, top, 2f, r - 6f), pixel);
        GUI.DrawTexture(new Rect(left, cy - 3f, r * 0.45f, 6f), pixel);
        GUI.DrawTexture(new Rect(cx + r * 0.55f, cy - 3f, r * 0.45f, 6f), pixel);
        GUI.DrawTexture(new Rect(cx - 3f, cy + r * 0.55f, 6f, r * 0.45f), pixel);
        GUI.DrawTexture(new Rect(cx - 1.5f, cy - 1.5f, 3f, 3f), pixel);
    }

    /// <summary>Un disque transparent, bord adouci et ombré, sur fond noir.</summary>
    private static Texture2D BuildScopeMask(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Lunette" };
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half)) / half;
            float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 1f, d)); // vignette vers le bord
            texture.SetPixel(x, y, new Color(0f, 0f, 0f, d >= 1f ? 1f : edge * 0.85f));
        }
        texture.Apply();
        return texture;
    }

    // Styles construits une seule fois : OnGUI tourne à chaque frame, y créer des GUIStyle
    // allouerait en continu.
    private void BuildStylesIfNeeded()
    {
        if (ammoStyle != null) return;

        ammoStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.LowerRight,
            fontSize = Mathf.Max(22, Screen.height / 18),
            fontStyle = FontStyle.Bold,
        };
        nameStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.LowerRight,
            fontSize = Mathf.Max(14, Screen.height / 40),
            fontStyle = FontStyle.Bold,
        };
        hintStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = Mathf.Max(14, Screen.height / 40),
            fontStyle = FontStyle.Bold,
        };
    }
}
