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

    private Texture2D pixel;

    private void Awake()
    {
        // Texture2D.whiteTexture est un pixel blanc fourni par Unity, teinté ensuite via GUI.color
        // — évite de générer/gérer une texture custom pour un simple aplat de couleur.
        pixel = Texture2D.whiteTexture;

        if (aimCamera == null)
        {
            Debug.LogError("CrosshairUI : assigne 'Aim Camera' (la même que WeaponController.aimCamera) dans l'inspecteur.", this);
        }
    }

    private void OnGUI()
    {
        if (aimCamera == null) return;

        // Point sur l'axe de visée de la caméra, projeté en coordonnées écran par la caméra
        // elle-même : garanti cohérent avec l'origine/direction utilisées par WeaponController.Fire(),
        // peu importe le Viewport Rect ou d'autres réglages caméra.
        Vector3 aimWorldPoint = aimCamera.transform.position + aimCamera.transform.forward * 10f;
        Vector3 screenPoint = aimCamera.WorldToScreenPoint(aimWorldPoint);

        // Camera.WorldToScreenPoint place l'origine en bas à gauche (y vers le haut), alors que
        // OnGUI place l'origine en haut à gauche (y vers le bas) — d'où l'inversion du y ici.
        float cx = screenPoint.x;
        float cy = Screen.height - screenPoint.y;

        bool isAiming = inputReader != null && inputReader.AimHeld;
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

        GUI.color = Color.white;
    }
}
