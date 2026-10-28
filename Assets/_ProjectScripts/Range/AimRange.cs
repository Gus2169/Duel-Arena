using System.Collections.Generic;
using System.Globalization;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// La salle de précision du stand (B-04, 2026-10-28, demandée par l'utilisateur d'après le « shooting range » de Rainbow
/// Six) : une grande cible ronde sur un chariot, presque aussi large que la salle (3,8 m, demandé par l'utilisateur), qu'un
/// pupitre posé sur le comptoir envoie à 5, 10, 15, 20, 25, 30, 40 ou 50 m, et dont il efface les impacts. On vise un bouton
/// du pupitre et on appuie sur la touche d'interaction (F).
///
/// Chaque balle ou plomb arrêté par la cible y laisse sa marque (WeaponController.ServerWorldImpact : le serveur, qui est
/// l'hôte du stand), compté en points (10 au centre, 1 au dernier anneau, 0 hors des anneaux) ; les impacts du dernier tir
/// ont le cœur blanc. Un écran holographique, debout sur le comptoir et tourné vers le tireur, montre la même cible, de près
/// (DuelArena/Hologramme : additif, le noir y est transparent).
///
/// Le stand ne reçoit que son hôte (127.0.0.1) : la salle ne vit que chez lui, rien n'est réseauté. Catégorie C : rien du
/// jeu ne la relit. La scène est construite par ShootingRangeBuilder (le chariot, ses colliders, le pupitre) et habillée par
/// StandDecorBuilder (leurs maillages).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class AimRange : MonoBehaviour
{
    /// <summary>Les distances du pupitre (m), de la ligne de tir à la face de la cible.</summary>
    public static readonly int[] Distances = { 5, 10, 15, 20, 25, 30, 40, 50 };
    /// <summary>Le bouton qui efface les impacts, après ceux des distances.</summary>
    public const int ClearButton = 8;
    public const int ButtonCount = 9;

    /// <summary>La cible : un disque de 3,8 m (entre les pilastres de la salle, à 20 cm de chacun), épais de 4 cm, au centre
    /// à 2 m du sol ; dix anneaux de 18 cm (le 10 au centre, le 1 jusqu'à 1,8 m du centre), le noir du 7 au 10, puis un
    /// liseré sombre jusqu'au bord.</summary>
    public const float BoardDiameter = 3.8f;
    public const float BoardRadius = BoardDiameter * 0.5f;
    public const float BoardThickness = 0.04f;
    public const float BoardCenterHeight = 2f;
    public const float RingStep = 0.18f;
    public const int Rings = 10;
    public const int BlackFrom = 7;
    /// <summary>Le pupitre (m) : sa plaque, à plat sur le comptoir, large de 1,33 m, profonde de 0,45 m.</summary>
    public static readonly Vector2 ConsoleSize = new(1.33f, 0.45f);
    /// <summary>Le chariot : sa vitesse de croisière (m/s) et son accélération (m/s²).</summary>
    public const float CarrierSpeed = 10f;
    public const float CarrierAcceleration = 8f;
    /// <summary>La définition de la cible (pixels de côté, sur tout le disque) et le rayon d'un impact (m).</summary>
    public const int TextureSize = 768;
    public const float ImpactRadius = 0.035f;

    [Tooltip("Le chariot : il avance le long du Z de la salle (ce composant est posé au milieu de la ligne de tir).")]
    [SerializeField] private Transform carrier;
    [Tooltip("La cible, enfant du chariot : un disque (son MeshCollider convexe arrête les balles), sa face regarde le -Z.")]
    [SerializeField] private Transform board;
    [Tooltip("La face imprimée de la cible (URP Lit, son premier matériau) et l'écran holographique : ils reçoivent la texture de la cible.")]
    [SerializeField] private Renderer boardFace;
    [SerializeField] private Renderer screen;
    [Tooltip("Le plan du pupitre : X à droite du tireur, Y vers le fond de la salle, -Z vers le haut (vers qui le regarde).")]
    [SerializeField] private Transform console;
    [Tooltip("Le fond des boutons (les distances, puis effacer), dans l'ordre de Distances.")]
    [SerializeField] private Renderer[] buttons = new Renderer[0];
    [Tooltip("La salle (le HUD ne s'affiche que là).")]
    [SerializeField] private Bounds room;
    [Tooltip("La distance de la cible au lancement (m).")]
    [SerializeField] private int distance = 10;
    [Tooltip("La portée de la main (m) : de l'œil au bouton visé.")]
    [SerializeField] private float reach = 2.2f;
    [SerializeField] private GameSounds sounds;

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private static readonly Color ButtonIdle = new(0.1f, 0.105f, 0.11f);
    private static readonly Color ButtonHover = new(0.24f, 0.25f, 0.26f);
    private static readonly Color ButtonSelected = new(0.62f, 0.09f, 0.06f);
    private static readonly Color ClearIdle = new(0.19f, 0.07f, 0.06f);

    // L'état de la salle ne survit pas à un rechargement du code : Unity y recopie les champs privés d'un type sérialisable
    // (des pixels d'une autre taille que la texture neuve, le 2026-10-28), d'où [NonSerialized] partout.
    [System.NonSerialized] private Texture2D texture;
    [System.NonSerialized] private Color32[] pixels;
    [System.NonSerialized] private Color32[] basePixels;
    [System.NonSerialized] private bool dirty;
    [System.NonSerialized] private MaterialPropertyBlock block;

    [System.NonSerialized] private float carrierPosition;
    [System.NonSerialized] private float carrierVelocity;
    [System.NonSerialized] private int impactCount;
    [System.NonSerialized] private int scoreSum;
    [System.NonSerialized] private readonly List<Vector2> volley = new();
    [System.NonSerialized] private int volleyFrame = -1;
    [System.NonSerialized] private int volleyScore;
    [System.NonSerialized] private int hovered = -1;
    [System.NonSerialized] private int shownButtonsKey = int.MinValue;

    [System.NonSerialized] private GUIStyle titleStyle;
    [System.NonSerialized] private GUIStyle lineStyle;
    [System.NonSerialized] private int statusKey = int.MinValue;
    [System.NonSerialized] private string titleText = "";
    [System.NonSerialized] private string statusText = "";
    [System.NonSerialized] private int promptKey = int.MinValue;
    [System.NonSerialized] private string promptText = "";

    /// <summary>La distance demandée (m) et celle où se trouve le chariot.</summary>
    public int TargetDistance => distance;
    public float CarrierDistance => carrierPosition;
    public bool CarrierMoving => Mathf.Abs(carrierPosition - distance) > 0.001f;
    public int ImpactCount => impactCount;
    public float MeanScore => impactCount > 0 ? scoreSum / (float)impactCount : 0f;
    /// <summary>Les points du dernier tir (la somme de ses plombs) et ses impacts sur la cible.</summary>
    public int LastShotScore => volleyScore;
    public int LastShotImpacts => volley.Count;
    public Transform Board => board;
    public Transform Console => console;
    public Renderer HologramScreen => screen;
    public float Reach => reach;
    /// <summary>La texture de la cible (pour les tests : ce qui s'y peint).</summary>
    public Texture2D Texture => texture;
    public static AimRange Instance { get; private set; }

    private void OnEnable()
    {
        Instance = this;
        block ??= new MaterialPropertyBlock();
        BuildTexture();
        carrierPosition = distance;
        carrierVelocity = 0f;
        PlaceCarrier();
        ApplyTexture();
        shownButtonsKey = int.MinValue;
        RefreshButtons();
        if (Application.isPlaying) WeaponController.ServerWorldImpact += OnWorldImpact;
    }

    private void OnDisable()
    {
        WeaponController.ServerWorldImpact -= OnWorldImpact;
        if (Instance == this) Instance = null;
        if (texture != null)
        {
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
            texture = null;
        }
    }

    private void OnValidate()
    {
        distance = Nearest(distance);
        if (!isActiveAndEnabled || Application.isPlaying) return;
        carrierPosition = distance;
        PlaceCarrier();
        shownButtonsKey = int.MinValue;
        RefreshButtons();
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        // Le chariot va à la distance demandée ; la cible bouge avec lui, et les tirs la trouvent où elle est.
        if (CarrierMoving || carrierVelocity != 0f)
        {
            carrierPosition = StepCarrier(carrierPosition, distance, Time.deltaTime, ref carrierVelocity);
            PlaceCarrier();
            Physics.SyncTransforms();
        }
        UpdateConsole();
        RefreshButtons();
    }

    private void LateUpdate()
    {
        if (!dirty || texture == null) return;
        dirty = false;
        texture.SetPixels32(pixels);
        texture.Apply(true);
    }

    // ------------------------------------------------------------------
    // Le pupitre
    // ------------------------------------------------------------------

    /// <summary>Le bouton visé (à portée de main), et l'appui sur la touche d'interaction. Seulement chez l'hôte du stand,
    /// menus fermés.</summary>
    private void UpdateConsole()
    {
        hovered = -1;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || console == null || ShootingRangePanel.IsOpen || PauseMenu.IsOpen) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        hovered = AimedButton(console, cam.transform.position, cam.transform.forward, reach);
        if (hovered < 0) return;
        PlayerInputReader input = LocalInput();
        if (input != null && input.InteractPressedThisFrame) Press(hovered);
    }

    /// <summary>Appuie sur un bouton du pupitre : une distance (le chariot y part), ou effacer.</summary>
    public void Press(int button)
    {
        if (button == ClearButton)
        {
            Clear();
            if (sounds != null) sounds.uiBack?.Play2D();
        }
        else if (button >= 0 && button < Distances.Length)
        {
            distance = Distances[button];
            if (sounds != null) sounds.uiSelect?.Play2D();
        }
    }

    /// <summary>Le bouton que vise un regard (depuis `eye`, vers `forward`), à moins de `reach` : -1 s'il n'y en a pas.</summary>
    public static int AimedButton(Transform console, Vector3 eye, Vector3 forward, float reach)
    {
        Vector3 normal = -console.forward;   // la face du pupitre regarde son -Z
        float facing = Vector3.Dot(forward, normal);
        float height = Vector3.Dot(eye - console.position, normal);
        if (height <= 0f || facing >= -1e-4f) return -1;   // derrière le pupitre, ou le regard ne va pas vers lui
        float t = -height / facing;
        if (t > reach) return -1;
        Vector3 local = console.InverseTransformPoint(eye + forward * t);
        return ButtonAt(new Vector2(local.x, local.y));
    }

    /// <summary>Le rectangle d'un bouton dans le plan du pupitre (m) : deux rangées de quatre distances (5 à 20 au fond, 25 à
    /// 50 devant), puis « effacer », sur les deux rangées, à droite.</summary>
    public static Rect ButtonRect(int button)
    {
        if (button == ClearButton) return new Rect(0.305f, -0.175f, 0.28f, 0.35f);
        int column = button % 4, row = button / 4;
        return new Rect(-0.585f + column * 0.22f, row == 0 ? 0.015f : -0.175f, 0.19f, 0.16f);
    }

    /// <summary>Le bouton sous un point du plan du pupitre, -1 s'il n'y en a pas.</summary>
    public static int ButtonAt(Vector2 point)
    {
        for (int i = 0; i < ButtonCount; i++)
        {
            if (ButtonRect(i).Contains(point)) return i;
        }
        return -1;
    }

    /// <summary>La couleur des boutons : la distance demandée en rouge (elle clignote pendant que le chariot roule), le
    /// bouton visé plus clair. Ne se refait qu'au changement.</summary>
    private void RefreshButtons()
    {
        if (buttons == null || buttons.Length == 0) return;
        int selected = System.Array.IndexOf(Distances, distance);
        bool blink = Application.isPlaying && CarrierMoving && Mathf.Repeat(Time.time * 3f, 1f) > 0.5f;
        int key = selected * 64 + (hovered + 1) * 2 + (blink ? 1 : 0);
        if (key == shownButtonsKey) return;
        shownButtonsKey = key;
        block ??= new MaterialPropertyBlock();
        for (int i = 0; i < buttons.Length; i++)
        {
            Renderer r = buttons[i];
            if (r == null) continue;
            Color c = i == selected && !blink ? ButtonSelected : i == ClearButton ? ClearIdle : ButtonIdle;
            if (i == hovered) c = Color.Lerp(c, ButtonHover, i == selected ? 0.35f : 1f);
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            r.SetPropertyBlock(block);
        }
    }

    // ------------------------------------------------------------------
    // Le chariot
    // ------------------------------------------------------------------

    private void PlaceCarrier()
    {
        if (carrier != null) carrier.localPosition = new Vector3(0f, 0f, carrierPosition);
    }

    /// <summary>Met tout de suite la cible à la distance du pupitre la plus proche de `meters` (les tests, les essais).</summary>
    public void SnapCarrier(int meters)
    {
        distance = Nearest(meters);
        carrierPosition = distance;
        carrierVelocity = 0f;
        PlaceCarrier();
        Physics.SyncTransforms();
        shownButtonsKey = int.MinValue;
        RefreshButtons();
    }

    /// <summary>
    /// Un pas du chariot vers `target` : il accélère jusqu'à sa vitesse de croisière, ralentit pour s'arrêter pile (la
    /// vitesse qui lui permet encore de freiner), fait demi-tour si la cible change de côté, et ne dépasse jamais la cible.
    /// `velocity` est signée, le long de la salle.
    /// </summary>
    public static float StepCarrier(float position, float target, float dt, ref float velocity)
    {
        float remaining = target - position;
        if (Mathf.Abs(remaining) < 0.0005f && Mathf.Abs(velocity) < 0.05f)
        {
            velocity = 0f;
            return target;
        }
        float wanted = Mathf.Sign(remaining) * Mathf.Min(CarrierSpeed, Mathf.Sqrt(2f * CarrierAcceleration * Mathf.Abs(remaining)));
        velocity = Mathf.MoveTowards(velocity, wanted, CarrierAcceleration * dt);
        float next = position + velocity * dt;
        if ((target - next) * remaining <= 0f)
        {
            velocity = 0f;
            return target;
        }
        return next;
    }

    /// <summary>La distance du pupitre la plus proche de `meters`.</summary>
    public static int Nearest(int meters)
    {
        int best = Distances[0];
        foreach (int d in Distances)
        {
            if (Mathf.Abs(d - meters) < Mathf.Abs(best - meters)) best = d;
        }
        return best;
    }

    // ------------------------------------------------------------------
    // La cible et ses impacts
    // ------------------------------------------------------------------

    /// <summary>Les points d'un impact, à `local` du centre de la cible (m) : 10 jusqu'à 5,5 cm, puis un de moins tous les
    /// 5,5 cm ; 0 au-delà du dernier anneau.</summary>
    public static int RingScore(Vector2 local)
    {
        float r = local.magnitude;
        if (r > RingStep * Rings) return 0;
        return Mathf.Clamp(Rings + 1 - Mathf.CeilToInt(r / RingStep), 1, Rings);
    }

    /// <summary>Un impact sur la face imprimée de la cible : sa place sur la face (m, depuis le centre ; x vers la droite
    /// du tireur, y vers le haut). Faux s'il est ailleurs (le chant, le dos, hors du disque, le chariot, le décor).</summary>
    public static bool TryBoardPoint(Transform board, Vector3 point, Vector3 normal, out Vector2 local)
    {
        local = default;
        if (board == null) return false;
        Vector3 p = board.InverseTransformPoint(point);
        if (Mathf.Abs(p.z + BoardThickness * 0.5f) > 0.005f || new Vector2(p.x, p.y).magnitude > BoardRadius + 0.005f) return false;
        if (Vector3.Dot(normal, -board.forward) < 0.5f) return false;
        local = new Vector2(p.x, p.y);
        return true;
    }

    private void OnWorldImpact(Vector3 point, Vector3 normal)
    {
        if (TryBoardPoint(board, point, normal, out Vector2 local)) RegisterImpact(local);
    }

    /// <summary>Marque un impact sur la cible (m, depuis son centre) et le compte. Ceux d'une même image font un tir (les
    /// plombs d'une décharge) : le tir d'avant perd son cœur blanc.</summary>
    public void RegisterImpact(Vector2 local)
    {
        if (pixels == null) BuildTexture();
        if (volleyFrame != Time.frameCount)
        {
            foreach (Vector2 old in volley) PaintImpact(pixels, TextureSize, old, false);
            volley.Clear();
            volleyScore = 0;
            volleyFrame = Time.frameCount;
        }
        int score = RingScore(local);
        volley.Add(local);
        volleyScore += score;
        impactCount++;
        scoreSum += score;
        PaintImpact(pixels, TextureSize, local, true);
        dirty = true;
        if (!Application.isPlaying) LateUpdate();
    }

    /// <summary>Efface les impacts : la cible revient comme neuve, les comptes à zéro.</summary>
    public void Clear()
    {
        if (basePixels == null) BuildTexture();
        System.Array.Copy(basePixels, pixels, pixels.Length);
        volley.Clear();
        volleyScore = 0;
        volleyFrame = -1;
        impactCount = 0;
        scoreSum = 0;
        dirty = true;
        if (!Application.isPlaying) LateUpdate();
    }

    private void BuildTexture()
    {
        // Une texture, ou des pixels, d'une autre taille que la cible d'aujourd'hui : on repart de zéro.
        int count = TextureSize * TextureSize;
        if (texture != null && texture.width != TextureSize)
        {
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
            texture = null;
        }
        if (basePixels != null && basePixels.Length != count) basePixels = null;
        if (pixels != null && pixels.Length != count) pixels = null;
        if (texture == null)
        {
            texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true, false)
            {
                name = "Cible de précision",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
        }
        if (basePixels == null)
        {
            basePixels = new Color32[TextureSize * TextureSize];
            DrawTarget(basePixels, TextureSize);
        }
        pixels ??= new Color32[basePixels.Length];
        System.Array.Copy(basePixels, pixels, pixels.Length);
        impactCount = 0;
        scoreSum = 0;
        volley.Clear();
        volleyScore = 0;
        texture.SetPixels32(pixels);
        texture.Apply(true);
        dirty = false;
    }

    /// <summary>La texture de la cible sur la face du disque et sur l'écran holographique : leur premier matériau seulement
    /// (le disque a aussi son dos et son chant d'acier).</summary>
    private void ApplyTexture()
    {
        foreach (Renderer r in new[] { boardFace, screen })
        {
            if (r == null || texture == null) continue;
            r.GetPropertyBlock(block, 0);
            block.SetTexture(BaseMapId, texture);
            r.SetPropertyBlock(block, 0);
        }
    }

    private static readonly Color32 PaperColor = new(224, 219, 204, 255);
    private static readonly Color32 BlackColor = new(24, 24, 27, 255);
    private static readonly Color32 RimColor = new(54, 55, 59, 255);
    private static readonly Color32 OutsideColor = new(0, 0, 0, 255);
    private static readonly Color32 LineOnPaper = new(32, 32, 35, 255);
    private static readonly Color32 LineOnBlack = new(206, 202, 190, 255);
    private static readonly Color32 ImpactRim = new(214, 30, 18, 255);
    private static readonly Color32 ImpactCore = new(26, 8, 6, 255);
    private static readonly Color32 NewestCore = new(250, 248, 240, 255);

    /// <summary>
    /// La cible imprimée, de côté `size` (pixels, le carré du disque de 3,8 m) : le papier blanc cassé, le noir du 7 au 10,
    /// les anneaux en traits (noirs sur le papier, clairs dans le noir), l'anneau du « X » au milieu du 10, un liseré sombre
    /// du dernier anneau au bord du disque, et le noir au-delà (le disque ne le montre pas ; l'hologramme, additif, le laisse
    /// transparent). Lissée au pixel (la couverture de chaque trait est calculée).
    /// </summary>
    public static void DrawTarget(Color32[] target, int size)
    {
        float metersPerPixel = BoardDiameter / size;
        float black = RingStep * (Rings + 1 - BlackFrom);
        float outer = RingStep * Rings;
        const float lineHalf = 1.25f;   // pixels
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float px = (x + 0.5f) * metersPerPixel - BoardRadius;
            float py = (y + 0.5f) * metersPerPixel - BoardRadius;
            float r = Mathf.Sqrt(px * px + py * py);
            float rPixels = r / metersPerPixel;
            // Le fond : le dehors, le liseré, le papier, le noir (bords lissés).
            Color c = Color.Lerp(OutsideColor, RimColor, Mathf.Clamp01(BoardRadius / metersPerPixel + 0.5f - rPixels));
            c = Color.Lerp(c, PaperColor, Mathf.Clamp01(outer / metersPerPixel + 0.5f - rPixels + lineHalf));
            c = Color.Lerp(c, BlackColor, Mathf.Clamp01(black / metersPerPixel + 0.5f - rPixels));
            // Les anneaux : chaque trait à sa couverture.
            float line = 0f;
            for (int k = 1; k <= Rings; k++)
            {
                if (k == BlackFrom) continue;   // le bord du noir est déjà un anneau
                float ring = RingStep * (Rings + 1 - k) / metersPerPixel;
                line = Mathf.Max(line, Mathf.Clamp01(lineHalf + 0.5f - Mathf.Abs(rPixels - ring)));
            }
            line = Mathf.Max(line, Mathf.Clamp01(0.9f + 0.5f - Mathf.Abs(rPixels - RingStep * 0.5f / metersPerPixel)));
            if (line > 0f) c = Color.Lerp(c, r < black ? (Color)LineOnBlack : LineOnPaper, line);
            target[y * size + x] = c;
        }
    }

    /// <summary>Peint un impact à `local` (m, depuis le centre de la cible) : un point rouge de 7 cm au cœur sombre, ou blanc
    /// pour ceux du dernier tir. Lissé au pixel.</summary>
    public static void PaintImpact(Color32[] target, int size, Vector2 local, bool newest)
    {
        float pixelsPerMeter = size / BoardDiameter;
        float radius = ImpactRadius * pixelsPerMeter, coreRadius = radius * 0.42f;
        float cx = (local.x + BoardRadius) * pixelsPerMeter;
        float cy = (local.y + BoardRadius) * pixelsPerMeter;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius - 1f)), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(cx + radius + 1f));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius - 1f)), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(cy + radius + 1f));
        Color32 core = newest ? NewestCore : ImpactCore;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float rim = Mathf.Clamp01(radius + 0.5f - d);
            if (rim <= 0f) continue;
            int i = y * size + x;
            Color c = Color.Lerp(target[i], ImpactRim, rim);
            c = Color.Lerp(c, core, Mathf.Clamp01(coreRadius + 0.5f - d));
            target[i] = c;
        }
    }

    // ------------------------------------------------------------------
    // Le HUD (OnGUI, placeholder comme celui du stand)
    // ------------------------------------------------------------------

    private void BuildStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || ShootingRangePanel.IsOpen || PauseMenu.IsOpen) return;
        Camera cam = Camera.main;
        if (cam == null || !room.Contains(cam.transform.position)) return;
        BuildStyles();
        float cx = Screen.width * 0.5f;

        int key = distance * 100000 + (CarrierMoving ? 50000 : 0) + Mathf.Min(impactCount, 9999) * 4 + Mathf.Min(volley.Count, 3);
        key = key * 31 + volleyScore;
        if (key != statusKey)
        {
            statusKey = key;
            titleText = CarrierMoving ? $"SALLE DE PRÉCISION   ·   la cible part à {distance} m" : $"SALLE DE PRÉCISION   ·   cible à {distance} m";
            statusText = StatusLine(impactCount, MeanScore, volleyScore, volley.Count);
        }
        Shadowed(new Rect(cx - 400f, 52f, 800f, 26f), titleText, titleStyle, Color.white);
        Shadowed(new Rect(cx - 400f, 80f, 800f, 24f), statusText, lineStyle, Color.white);

        if (hovered >= 0)
        {
            int prompt = hovered;
            if (prompt != promptKey)
            {
                promptKey = prompt;
                PlayerInputReader input = LocalInput();
                string keyName = input != null ? input.KeyFor("Interact") : "F";
                promptText = hovered == ClearButton ? $"{keyName} : effacer les impacts" : $"{keyName} : envoyer la cible à {Distances[hovered]} m";
            }
            Shadowed(new Rect(cx - 300f, Screen.height * 0.5f + 40f, 600f, 26f), promptText, titleStyle, Color.white);
        }
    }

    /// <summary>La ligne des comptes : les impacts, la moyenne, le dernier tir (ses points, et ses plombs s'il en a
    /// plusieurs).</summary>
    public static string StatusLine(int impacts, float mean, int lastScore, int lastImpacts)
    {
        if (impacts == 0) return "Aucun impact   ·   le pupitre du comptoir règle la distance et efface la cible";
        string last = lastImpacts > 1 ? $"{lastScore} ({lastImpacts} impacts)" : lastImpacts == 1 ? lastScore.ToString(French) : "—";
        return $"{impacts} impact{(impacts > 1 ? "s" : "")}   ·   moyenne {mean.ToString("0.0", French)}   ·   dernier tir {last}";
    }

    private static PlayerInputReader LocalInput()
    {
        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject own = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
        return own != null ? own.GetComponent<PlayerInputReader>() : null;
    }

    private static void Shadowed(Rect r, string text, GUIStyle style, Color color)
    {
        Color old = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.75f * color.a);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
        style.normal.textColor = color;
        GUI.Label(r, text, style);
        style.normal.textColor = old;
    }
}
