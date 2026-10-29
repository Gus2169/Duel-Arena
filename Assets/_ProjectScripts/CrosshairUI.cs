using DuelArena.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Le HUD de combat du joueur, en UI Toolkit (2026-10-29, à la place de l'OnGUI provisoire ; GDD § 6 et § 15) :
/// - le réticule de la hanche, dont les branches s'écartent jusqu'au bord du cône où partira la prochaine balle,
///   comme dans Rainbow Six ; rien en visée (on vise avec l'arme) ni à la hanche d'un fusil de précision ; un
///   point pour un spectateur ;
/// - le marqueur de touche, à chaque tir que le serveur a compté (WeaponController.OnHitConfirmed) : une croix
///   blanche, plus grande à la tête, rouge au coup qui tue (HitMarker) ; il se voit aussi en visée et dans la
///   lunette ;
/// - quand on est touché, des parasites à l'écran (DamageStaticOverlay) et un très léger sursaut de la vue
///   (PlayerCameraLook.Flinch) ;
/// - les munitions, ou la chaleur d'une arme laser, sur un fond sombre en bas à droite, en face de la vie
///   (GameHud) ; la barre du rechargement ou de la charge (le FX-2) sous le réticule ; l'invite du rechargement
///   chargeur vide ; la lunette d'origine d'un fusil de précision (ScopeOverlay).
///
/// Le robot du joueur crée lui-même son document (CombatHud.uxml, avec le PanelSettings de la partie), rangé
/// sous ceux des scènes (sortingOrder) : le HUD est le même dans l'arène et au stand, et les menus, la
/// préparation et le fondu au noir le couvrent. Chez le propriétaire seul. Catégorie C : il lit l'état publié et
/// ne décide rien ; le sursaut tourne la caméra, comme la secousse d'un caillou.
///
/// Le point visé à l'écran est calculé par la caméra de visée plutôt que supposé au centre : le réticule reste
/// aligné avec le tir, quels que soient le Viewport Rect ou le Lens Shift. "Aim Camera" doit être la MÊME caméra
/// que WeaponController.aimCamera.
/// </summary>
[DisallowMultipleComponent]
public class CrosshairUI : MonoBehaviour
{
    [Header("Référence caméra (obligatoire)")]
    [Tooltip("Doit être la MÊME caméra que celle assignée dans WeaponController.aimCamera.")]
    [SerializeField] private Camera aimCamera;

    [Header("Référence optionnelle (la visée, à défaut de PlayerLocomotion)")]
    [SerializeField] private PlayerInputReader inputReader;

    [Header("Le document")]
    [Tooltip("Le HUD de combat (Assets/_ProjectUI/Game/CombatHud.uxml).")]
    [SerializeField] private VisualTreeAsset hudTree;
    [Tooltip("Le PanelSettings des scènes de jeu (GamePanelSettings) : le HUD se range sous leurs documents.")]
    [SerializeField] private PanelSettings panelSettings;
    [Tooltip("Sous les documents des scènes (0) : les menus, la préparation et le fondu au noir le couvrent.")]
    [SerializeField] private float sortingOrder = -10f;

    [Header("Apparence - à la hanche (unités du panneau, 1080 de haut)")]
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
    [Tooltip("La jauge de la charge (le FX-2) une fois pleine : le tir est prêt.")]
    [SerializeField] private Color chargeFullColor = new Color(1f, 0.3f, 0.22f);

    [Header("Parasites quand on est touché (ressenti)")]
    [Tooltip("Leur durée (s) : un éclat, pas un voile.")]
    [SerializeField] private float staticDuration = 0.45f;
    [Tooltip("Dégâts (PV) d'un coup qui grésille à pleine force ; un plus petit grésille moins.")]
    [SerializeField] private float staticFullDamage = 40f;
    [Tooltip("La force du plus petit coup, en part de la pleine.")]
    [Range(0f, 1f)]
    [SerializeField] private float staticMinStrength = 0.4f;

    [Header("Sursaut quand on est touché (ressenti, GDD § 6 : très léger)")]
    [Tooltip("Degrés de sursaut par point de dégâts.")]
    [SerializeField] private float flinchPerDamage = 0.025f;
    [Tooltip("Jamais plus que ce sursaut (degrés).")]
    [SerializeField] private float flinchMax = 1.2f;

    [Header("Lunette du sniper")]
    [Tooltip("Diamètre de l'ouverture de la lunette, en part de la hauteur de l'écran.")]
    [SerializeField, Range(0.5f, 1f)] private float scopeDiameter = 0.92f;
    [SerializeField] private Color scopeReticleColor = new Color(0f, 0f, 0f, 0.9f);

    /// <summary>Sous le point visé, la barre du rechargement ou de la charge et l'invite (unités du panneau) :
    /// sous les branches du réticule, là où se trouve le regard.</summary>
    private float BarOffset => hipGap + hipLineLength + 14f;

    private NetworkObject networkObject;
    private FirstPersonView firstPersonView;
    private PlayerLocomotion locomotion;
    private Health health;
    private PlayerMatchState matchState;
    private PlayerCameraLook cameraLook;

    // Le document et ses éléments, retrouvés à chaque arbre neuf (l'UIDocument le recrée en se réactivant).
    private UIDocument hudDocument;
    private VisualElement wiredRoot;
    private VisualElement hudRoot, ammoPanel, ammoRow, heatRow, heatFill;
    private Label weaponNameLabel, ammoValueLabel, ammoSizeLabel, heatLabel, emptyHint;
    private CombatReticle reticle;
    private ScopeOverlay scope;
    private DamageStaticOverlay damageStatic;

    // Ce que montrent les textes, refaits seulement quand ça change.
    private int shownAmmo, shownSize;
    private WeaponData shownData;
    private int shownHeatState;
    private float shownHeat;
    private int shownHintState;

    // Le marqueur de touche en cours, et les parasites.
    private WeaponController.HitKind markerKind;
    private float markerAge = float.PositiveInfinity;
    private float staticAge = float.PositiveInfinity;
    private float staticStrength;
    private uint staticSeed;

    /// <summary>Le robot de cette machine, en session : le seul qui a un HUD.</summary>
    private bool IsLocalOwner => networkObject != null && networkObject.IsSpawned && networkObject.IsOwner;

    private void Awake()
    {
        if (weapon == null) weapon = GetComponentInChildren<WeaponController>(true);
        networkObject = GetComponent<NetworkObject>();
        firstPersonView = GetComponent<FirstPersonView>();
        locomotion = GetComponent<PlayerLocomotion>();
        health = GetComponent<Health>();
        matchState = GetComponent<PlayerMatchState>();
        cameraLook = GetComponentInChildren<PlayerCameraLook>(true);

        if (aimCamera == null)
        {
            Debug.LogError("CrosshairUI : assigne 'Aim Camera' (la même que WeaponController.aimCamera) dans l'inspecteur.", this);
        }
    }

    private void OnEnable()
    {
        if (weapon != null) weapon.OnHitConfirmed += HandleHitConfirmed;
        if (health != null) health.OnDamaged += HandleDamaged;
    }

    private void OnDisable()
    {
        if (weapon != null) weapon.OnHitConfirmed -= HandleHitConfirmed;
        if (health != null) health.OnDamaged -= HandleDamaged;
        if (hudDocument != null)
        {
            if (Application.isPlaying) Destroy(hudDocument.gameObject);
            else DestroyImmediate(hudDocument.gameObject);
        }
        hudDocument = null;
        wiredRoot = null;
    }

    // ------------------------------------------------------------------
    // Ce qui arrive : une touche confirmée, un coup reçu
    // ------------------------------------------------------------------

    private void HandleHitConfirmed(WeaponController.HitKind kind)
    {
        if (!IsLocalOwner || !HitMarker.Replaces(markerKind, markerAge, kind)) return;
        markerKind = kind;
        markerAge = 0f;
    }

    private void HandleDamaged(float amount, GameObject source)
    {
        // Le coup qui tue n'a ni parasites ni sursaut : la vue quitte les yeux du robot (PlayerDeath).
        if (!IsLocalOwner || !(amount > 0f) || (health != null && health.IsDead)) return;
        float still = DamageStaticOverlay.Intensity(staticAge, staticDuration) * staticStrength;
        staticStrength = Mathf.Max(DamageStaticOverlay.Strength(amount, staticFullDamage, staticMinStrength), still);
        staticAge = 0f;
        staticSeed = ElectricArc.Hash(staticSeed + 0x9e3779b9U);
        if (cameraLook != null) cameraLook.Flinch(PlayerCameraLook.FlinchDegrees(amount, flinchPerDamage, flinchMax));
    }

    // ------------------------------------------------------------------
    // Chaque image
    // ------------------------------------------------------------------

    private void LateUpdate()
    {
        markerAge += Time.deltaTime;
        staticAge += Time.deltaTime;
        if (!IsLocalOwner || !EnsureHud())
        {
            if (hudRoot != null) SetVisible(hudRoot, false);
            return;
        }

        // Mort (la caméra regarde l'épave), ou un écran a la main (préparation, Échap, fin de match, fenêtre des
        // armes du stand, noir tombé) : rien.
        bool dead = health != null && health.IsDead;
        bool covered = GameHud.HidesCrosshair || ShootingRangePanel.IsOpen || PauseMenu.IsOpen;
        bool shown = !dead && !covered && aimCamera != null && reticle.panel != null;
        SetVisible(hudRoot, shown);
        if (!shown) return;

        float height = reticle.layout.height;
        if (float.IsNaN(height) || height < 1f) return;
        Vector2 center = RuntimePanelUtils.CameraTransformWorldToPanel(
            reticle.panel, aimCamera.transform.position + aimCamera.transform.forward * 10f, aimCamera);
        bool spectator = matchState != null && matchState.IsSpectator;
        bool armed = !spectator && weapon != null && weapon.MagazineSize > 0;
        bool scoped = !spectator && firstPersonView != null && firstPersonView.Scoped;

        UpdateReticle(center, height, spectator, armed, scoped);
        UpdateScope(center, scoped);
        UpdateStatic();
        UpdateAmmo(armed);
        UpdateHint(center, armed);
    }

    private void UpdateReticle(Vector2 center, float height, bool spectator, bool armed, bool scoped)
    {
        // En visée, l'œil est dans l'axe du viseur ou on vise avec l'arme : pas de réticule (2026-10-16). La
        // visée de la simulation, pas la touche : pendant un rechargement, elle est refusée (PlayerLocomotion.
        // GateAim) et le réticule de la hanche reste. Le fusil de précision n'en a pas à la hanche, comme dans
        // Call of Duty : il faut deviner le centre de son cône (HipFireSpread).
        bool aiming = locomotion != null ? locomotion.DisplayAiming : inputReader != null && inputReader.AimHeld;
        bool sightOnAxis = firstPersonView != null && firstPersonView.SightOnAxis;
        bool hipCrosshair = firstPersonView == null || firstPersonView.HipCrosshair;

        reticle.center = center;
        reticle.crosshair = armed && !scoped && !sightOnAxis && !aiming && hipCrosshair;
        reticle.spectatorDot = spectator;
        reticle.gap = Mathf.Max(hipGap, ConeFraction(weapon != null ? weapon.HipSpreadDegrees : 0f, aimCamera.fieldOfView) * height);
        reticle.lineLength = hipLineLength;
        reticle.thickness = lineThickness;
        reticle.centerDot = showCenterDot;
        reticle.dotSize = centerDotSize;
        reticle.color = crosshairColor;
        reticle.barOffset = BarOffset;

        // La barre sous le réticule : le rechargement, ou la charge du tir (le FX-2, 2026-10-26), lunette comprise.
        // Pleine, la charge change de couleur : lâcher la gâchette tire un coup qui tue au buste.
        float charge = armed ? weapon.DisplayedCharge : 0f;
        if (armed && weapon.IsReloading)
        {
            reticle.barProgress = weapon.ReloadProgress;
            reticle.barColor = crosshairColor;
        }
        else if (charge > 0f)
        {
            reticle.barProgress = charge;
            reticle.barColor = charge >= 1f ? chargeFullColor : crosshairColor;
        }
        else reticle.barProgress = -1f;

        reticle.markerKind = !spectator && HitMarker.Alpha(markerKind, markerAge) > 0f ? (int)markerKind : -1;
        reticle.markerAge = markerAge;
        reticle.MarkDirtyRepaint();
    }

    private void UpdateScope(Vector2 center, bool scoped)
    {
        SetVisible(scope, scoped);
        if (!scoped) return;
        scope.center = center;
        scope.diameter = scopeDiameter;
        scope.reticleColor = scopeReticleColor;
        scope.MarkDirtyRepaint();
    }

    private void UpdateStatic()
    {
        float intensity = DamageStaticOverlay.Intensity(staticAge, staticDuration) * staticStrength;
        SetVisible(damageStatic, intensity > 0f);
        if (intensity <= 0f) return;
        damageStatic.intensity = intensity;
        damageStatic.age = staticAge;
        damageStatic.seed = staticSeed;
        damageStatic.MarkDirtyRepaint();
    }

    /// <summary>Les munitions, ou la chaleur d'une arme laser (GDD § 15, 2026-10-26) : sur un fond sombre, en bas à
    /// droite, comme la vie en bas à gauche. Les textes ne sont refaits que quand ils changent.</summary>
    private void UpdateAmmo(bool armed)
    {
        SetVisible(ammoPanel, armed);
        if (!armed) return;

        WeaponData data = weapon.Data;
        if (data != shownData)
        {
            shownData = data;
            weaponNameLabel.text = data != null ? data.weaponName : "";
        }

        bool hot = weapon.DisplayedUsesHeat;
        SetVisible(ammoRow, !hot);
        SetVisible(heatRow, hot);
        if (hot)
        {
            bool overheated = weapon.DisplayedOverheated;
            int state = overheated ? 1 : 0;
            if (state != shownHeatState)
            {
                shownHeatState = state;
                heatLabel.text = overheated ? "SURCHAUFFE" : "CHALEUR";
                heatRow.EnableInClassList("combat-heat--overheated", overheated);
            }
            float heat = Mathf.Clamp01(weapon.DisplayedHeat);
            if (Mathf.Abs(heat - shownHeat) > 0.001f)
            {
                shownHeat = heat;
                heatFill.style.width = Length.Percent(heat * 100f);
            }
            return;
        }

        int ammo = weapon.DisplayedAmmo, size = weapon.MagazineSize;
        if (ammo != shownAmmo)
        {
            shownAmmo = ammo;
            ammoValueLabel.text = ammo.ToString();
            ammoPanel.EnableInClassList("combat-ammo--empty", ammo == 0);
        }
        if (size != shownSize)
        {
            shownSize = size;
            ammoSizeLabel.text = "/ " + size;
        }
    }

    /// <summary>Chargeur vide (ou arme laser surchauffée), sans rechargement en cours : la touche, sous le
    /// réticule. Une arme laser ne se vide d'elle-même que si rien n'interrompt sa vidange.</summary>
    private void UpdateHint(Vector2 center, bool armed)
    {
        bool hot = armed && weapon.DisplayedUsesHeat;
        bool empty = armed && (hot ? weapon.DisplayedOverheated : weapon.DisplayedAmmo == 0);
        bool show = empty && !weapon.IsReloading && !(weapon.DisplayedCharge > 0f);
        SetVisible(emptyHint, show);
        // Le texte est refait à chaque apparition : l'arme (balles ou chaleur) et la touche ont pu changer.
        int state = show ? (hot ? 2 : 1) : 0;
        if (state != shownHintState)
        {
            shownHintState = state;
            string key = inputReader != null ? inputReader.KeyFor("Reload") : "R";
            if (show) emptyHint.text = hot ? $"{key} : vider" : $"{key} : recharger";
        }
        if (!show) return;
        emptyHint.style.left = center.x - 150f;
        emptyHint.style.top = center.y + BarOffset - 4f;
    }

    // ------------------------------------------------------------------
    // Le document
    // ------------------------------------------------------------------

    /// <summary>Le document du HUD, créé au premier besoin, et ses éléments, retrouvés sur chaque arbre neuf.</summary>
    private bool EnsureHud()
    {
        if (hudDocument == null)
        {
            if (hudTree == null || panelSettings == null) return false;
            var holder = new GameObject("HUD de combat");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            hudDocument = holder.AddComponent<UIDocument>();
            hudDocument.panelSettings = panelSettings;
            hudDocument.visualTreeAsset = hudTree;
            hudDocument.sortingOrder = sortingOrder;
            holder.SetActive(true);
        }
        VisualElement root = hudDocument.rootVisualElement;
        if (root == null) return false;
        if (root != wiredRoot) Wire(root);
        return reticle != null && hudRoot != null;
    }

    private void Wire(VisualElement root)
    {
        wiredRoot = root;
        hudRoot = root.Q<VisualElement>("combatHud");
        damageStatic = root.Q<DamageStaticOverlay>("damageStatic");
        scope = root.Q<ScopeOverlay>("scope");
        reticle = root.Q<CombatReticle>("reticle");
        emptyHint = root.Q<Label>("emptyHint");
        ammoPanel = root.Q<VisualElement>("ammoPanel");
        weaponNameLabel = root.Q<Label>("weaponName");
        ammoRow = root.Q<VisualElement>("ammoRow");
        ammoValueLabel = root.Q<Label>("ammoValue");
        ammoSizeLabel = root.Q<Label>("ammoSize");
        heatRow = root.Q<VisualElement>("heatRow");
        heatLabel = root.Q<Label>("heatLabel");
        heatFill = root.Q<VisualElement>("heatFill");
        // Un arbre neuf : ses textes sont à refaire.
        shownAmmo = shownSize = int.MinValue;
        shownData = null;
        shownHeatState = -1;
        shownHeat = -1f;
        shownHintState = -1;
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        if (element != null && element.ClassListContains("hidden") == visible) element.EnableInClassList("hidden", !visible);
    }

    /// <summary>Le rayon à l'écran d'un cône de `degrees` degrés, en part de la hauteur de l'écran, vu sous un
    /// champ vertical de `fieldOfView` degrés. Fonction pure, testée.</summary>
    public static float ConeFraction(float degrees, float fieldOfView)
    {
        if (!(degrees > 0f) || !(fieldOfView > 0f)) return 0f;
        return Mathf.Tan(degrees * Mathf.Deg2Rad) / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.5f;
    }
}
