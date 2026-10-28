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
/// "Input Reader" est optionnel : la visée se lit sur PlayerLocomotion (pas de réticule en visée).
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

    [Header("Référence optionnelle (la visée, à défaut de PlayerLocomotion)")]
    [SerializeField] private PlayerInputReader inputReader;

    [Header("Apparence - à la hanche")]
    [SerializeField] private float hipGap = 6f;
    [SerializeField] private float hipLineLength = 8f;
    [SerializeField] private float lineThickness = 2f;
    [SerializeField] private Color crosshairColor = Color.white;

    [Header("Point central (optionnel)")]
    [SerializeField] private bool showCenterDot = true;
    [SerializeField] private float centerDotSize = 2f;

    [Header("Munitions")]
    [Tooltip("L'arme du joueur. Trouvée dans les enfants si laissée vide.")]
    [SerializeField] private WeaponController weapon;
    [SerializeField] private Color ammoEmptyColor = new Color(1f, 0.4f, 0.3f);
    [Tooltip("Le fond du compteur de munitions : la teinte de la jauge de vie du HUD (GameHud.uss), un peu plus sombre.")]
    [SerializeField] private Color ammoPanelColor = new Color(10f / 255f, 13f / 255f, 17f / 255f, 0.8f);
    [Tooltip("La jauge de la charge (le FX-2) une fois pleine : le tir est prêt.")]
    [SerializeField] private Color chargeFullColor = new Color(1f, 0.3f, 0.22f);

    [Header("Lunette du sniper")]
    [Tooltip("Diamètre de l'ouverture de la lunette, en part de la hauteur de l'écran.")]
    [SerializeField, Range(0.5f, 1f)] private float scopeDiameter = 0.92f;
    [SerializeField] private Color scopeReticleColor = new Color(0f, 0f, 0f, 0.9f);

    private Texture2D pixel;
    private Texture2D scopeMask; // disque transparent sur fond noir, construit une fois
    private FirstPersonView firstPersonView;
    private PlayerLocomotion locomotion;
    private Health health;
    private PlayerMatchState matchState;
    // Jamais sérialisés : voir PauseMenu (une recompilation recrée sinon des GUIStyle vides).
    [System.NonSerialized] private GUIStyle ammoStyle;
    [System.NonSerialized] private GUIStyle nameStyle;
    [System.NonSerialized] private GUIStyle hintStyle;

    // Les textes affichés, ce dont ils dépendent, et leur taille (mesurée quand ils changent).
    [System.NonSerialized] private string ammoText;
    [System.NonSerialized] private string nameText;
    [System.NonSerialized] private GUIContent measured;
    private Vector2 ammoTextSize, nameTextSize;
    private int shownAmmo, shownMagazineSize;
    private WeaponData shownWeaponData;

    private void Awake()
    {
        // Texture2D.whiteTexture est un pixel blanc fourni par Unity, teinté ensuite via GUI.color
        // — évite de générer/gérer une texture custom pour un simple aplat de couleur.
        pixel = Texture2D.whiteTexture;
        // Rien ici n'utilise GUILayout : sans la passe de mise en page, OnGUI n'est plus appelé
        // qu'une fois par image au lieu de deux.
        useGUILayout = false;
        if (weapon == null) weapon = GetComponentInChildren<WeaponController>();
        firstPersonView = GetComponent<FirstPersonView>();
        locomotion = GetComponent<PlayerLocomotion>();
        health = GetComponent<Health>();
        matchState = GetComponent<PlayerMatchState>();

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

        // Mort (la caméra regarde l'épave), ou un écran a la main (préparation, Échap, fin de
        // match) : ni réticule ni munitions.
        if (health != null && health.IsDead) return;
        if (GameHud.HidesCrosshair || ShootingRangePanel.IsOpen || PauseMenu.IsOpen) return;

        // Un spectateur n'a pas d'arme (2026-10-13) : un simple point, pour viser ses lancers et ses
        // coups. Aucune aide de plus : le geste reste imparfait (GDD § 3).
        if (matchState != null && matchState.IsSpectator)
        {
            Vector3 eye = aimCamera.WorldToScreenPoint(aimCamera.transform.position + aimCamera.transform.forward * 10f);
            float dot = Mathf.Max(3f, centerDotSize * 1.5f);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(eye.x - dot * 0.5f - 1f, Screen.height - eye.y - dot * 0.5f - 1f, dot + 2f, dot + 2f), pixel);
            GUI.color = crosshairColor;
            GUI.DrawTexture(new Rect(eye.x - dot * 0.5f, Screen.height - eye.y - dot * 0.5f, dot, dot), pixel);
            GUI.color = Color.white;
            return;
        }

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

        // En visée, pas de réticule : on vise avec l'arme (demandé par l'utilisateur le 2026-10-16). La
        // visée de la simulation, pas la touche : pendant un rechargement, elle est refusée
        // (PlayerLocomotion.GateAim) et le réticule de la hanche reste.
        bool isAiming = locomotion != null ? locomotion.DisplayAiming : inputReader != null && inputReader.AimHeld;

        // Le sniper n'a pas de réticule à la hanche, comme dans Call of Duty (tranché le
        // 2026-10-11) : il faut deviner le centre de son cône (HipFireSpread).
        if (isAiming || (firstPersonView != null && !firstPersonView.HipCrosshair))
        {
            DrawAmmo(cx, cy);
            GUI.color = Color.white;
            return;
        }
        // Les branches s'écartent jusqu'au bord du cône où partira la prochaine balle, comme dans
        // Rainbow Six (2026-10-16) : il s'ouvre en bougeant et en rafale, se referme en épaulant.
        float gap = Mathf.Max(hipGap, ConePixels(weapon != null ? weapon.HipSpreadDegrees : 0f));
        float length = hipLineLength;

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

    /// <summary>Le rayon à l'écran (pixels) d'un cône de `degrees` degrés, vu par la caméra de visée.</summary>
    private float ConePixels(float degrees)
    {
        if (!(degrees > 0f) || aimCamera == null) return 0f;
        float halfFov = aimCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        return Mathf.Tan(degrees * Mathf.Deg2Rad) / Mathf.Tan(halfFov) * Screen.height * 0.5f;
    }

    private void DrawAmmo(float cx, float cy)
    {
        if (weapon == null || weapon.MagazineSize <= 0) return;
        BuildStylesIfNeeded();

        // Une arme laser n'a pas de balles : une jauge de chaleur à leur place (GDD § 15, 2026-10-26).
        bool hot = weapon.DisplayedUsesHeat;
        bool overheated = hot && weapon.DisplayedOverheated;
        int ammo = hot ? (overheated ? 0 : 1) : weapon.DisplayedAmmo;
        Color textColor = ammo == 0 ? ammoEmptyColor : crosshairColor;

        // Les textes ne sont refaits (et mesurés) que quand ils changent : OnGUI tourne à chaque image.
        int size = hot ? -1 : weapon.MagazineSize;
        measured ??= new GUIContent();
        if (ammoText == null || ammo != shownAmmo || size != shownMagazineSize)
        {
            shownAmmo = ammo;
            shownMagazineSize = size;
            ammoText = !hot ? $"{ammo} / {size}" : overheated ? "SURCHAUFFE" : "CHALEUR";
            measured.text = ammoText;
            ammoTextSize = ammoStyle.CalcSize(measured);
        }
        bool named = weapon.Data != null;
        if (named && (nameText == null || weapon.Data != shownWeaponData))
        {
            shownWeaponData = weapon.Data;
            nameText = shownWeaponData.weaponName;
            measured.text = nameText;
            nameTextSize = nameStyle.CalcSize(measured);
        }

        // Sur un fond sombre, en bas à droite, comme la jauge de vie en bas à gauche (GameHud) : en blanc
        // sur un mur ou un sol clair, il ne se lisait pas (signalé par l'utilisateur le 2026-10-26).
        float scale = Screen.height / 1080f;
        float padding = 14f * scale;
        float nameHeight = named ? nameTextSize.y : 0f;
        float gaugeHeight = hot ? 10f * scale : 0f;
        float panelWidth = Mathf.Max(Mathf.Max(ammoTextSize.x, named ? nameTextSize.x : 0f), hot ? 180f * scale : 0f) + 2f * padding;
        float panelHeight = nameHeight + ammoTextSize.y + 8f * scale + (hot ? gaugeHeight + 8f * scale : 0f);
        var panel = new Rect(Screen.width - 28f * scale - panelWidth, Screen.height - 24f * scale - panelHeight, panelWidth, panelHeight);
        GUI.color = ammoPanelColor;
        GUI.DrawTexture(panel, pixel);
        GUI.color = Color.white;
        if (named)
        {
            nameStyle.normal.textColor = crosshairColor;
            GUI.Label(new Rect(panel.x, panel.y + 4f * scale, panelWidth - padding, nameHeight), nameText, nameStyle);
        }
        ammoStyle.normal.textColor = textColor;
        GUI.Label(new Rect(panel.x, panel.y + 4f * scale + nameHeight, panelWidth - padding, ammoTextSize.y), ammoText, ammoStyle);
        if (hot)
        {
            // La jauge se remplit en tirant et se vide en refroidissant ; rouge surchauffée.
            var gauge = new Rect(panel.x + padding, panel.yMax - 8f * scale - gaugeHeight, panelWidth - 2f * padding, gaugeHeight);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(gauge, pixel);
            GUI.color = overheated ? ammoEmptyColor : crosshairColor;
            GUI.DrawTexture(new Rect(gauge.x, gauge.y, gauge.width * Mathf.Clamp01(weapon.DisplayedHeat), gauge.height), pixel);
            GUI.color = Color.white;
        }

        float below = cy + hipGap + hipLineLength + 14f;
        float charge = weapon.DisplayedCharge;
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
        else if (charge > 0f)
        {
            // La charge du tir (le FX-2, 2026-10-26) : sous le réticule, là où est le regard, lunette
            // comprise. Pleine, elle change de couleur : lâcher la gâchette tire un coup qui tue au buste.
            const float width = 64f;
            const float height = 4f;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(cx - width * 0.5f - 1f, below - 1f, width + 2f, height + 2f), pixel);
            GUI.color = charge >= 1f ? chargeFullColor : crosshairColor;
            GUI.DrawTexture(new Rect(cx - width * 0.5f, below, width * charge, height), pixel);
        }
        else if (ammo == 0)
        {
            // Une ombre sous le texte : il se lit aussi sur un mur clair. Une arme laser ne se vide
            // d'elle-même que si rien n'interrompt sa vidange.
            string hint = hot ? "R : vider" : "R : recharger";
            hintStyle.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
            GUI.Label(new Rect(cx - 149f, below - 3f, 300f, 30f), hint, hintStyle);
            hintStyle.normal.textColor = textColor;
            GUI.Label(new Rect(cx - 150f, below - 4f, 300f, 30f), hint, hintStyle);
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
