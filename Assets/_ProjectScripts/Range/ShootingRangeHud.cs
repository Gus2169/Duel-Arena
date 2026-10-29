using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ce que le stand de tir affiche par-dessus les mannequins (2026-10-16) : leur vie (une barre qui
/// garde un instant la part perdue, comme la jauge du jeu), la distance, les dégâts de chaque balle ou
/// plomb qui touche (la zone, la baisse avec la distance), et, quand un mannequin tombe, le temps et le
/// nombre d'impacts qu'il a fallu. De quoi juger une arme et ses accessoires en chiffres.
///
/// Les dégâts viennent du serveur (WeaponController.ServerHitRegistered) : le stand tourne chez l'hôte,
/// seul. HUD OnGUI, placeholder assumé. Rien ne relit ces chiffres.
/// </summary>
[DisallowMultipleComponent]
public class ShootingRangeHud : MonoBehaviour
{
    [Tooltip("Durée (s) d'un chiffre de dégâts à l'écran.")]
    [SerializeField] private float numberLifetime = 1.1f;
    [Tooltip("Durée (s) du bilan d'un mannequin neutralisé.")]
    [SerializeField] private float killReportLifetime = 4f;

    private struct HitNumber
    {
        public TrainingDummy dummy;
        public HitboxZoneType zone;
        public float damage;
        public float distance;
        public float at;
        public int order;
        public string text;
    }

    // Par mannequin : la vie montrée (la part perdue reste un instant), et la série de touches en cours.
    private class DummyState
    {
        public float shown = -1f;
        public float trail = -1f;
        public float trailHoldUntil;
        public bool hadFullHealth = true;
        public float seriesStart;
        public int seriesHits;
        public float seriesDamage;
        public string report;
        public float reportUntil;
        public float lastHealth = -1f;
        public float lastHitAt = -100f;
        // Le texte « 100 PV · 12 m », refait seulement quand la vie ou la distance montrées changent.
        public string label;
        public int labelHealth = -1;
        public int labelDistance = -1;
    }

    // Un mannequin à l'écran, le temps d'une image : où, à quelle distance, et dans quel ordre.
    private struct Entry
    {
        public TrainingDummy dummy;
        public Vector2 point;
        public float distance;
        public int rank;
    }

    private readonly List<HitNumber> numbers = new List<HitNumber>();
    private readonly Dictionary<TrainingDummy, DummyState> states = new Dictionary<TrainingDummy, DummyState>();
    private readonly Dictionary<Health, TrainingDummy> byHealth = new Dictionary<Health, TrainingDummy>();
    private readonly List<Entry> entries = new List<Entry>();
    private readonly List<Rect> placed = new List<Rect>();
    private static readonly System.Comparison<Entry> ByPriority =
        (a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.distance.CompareTo(b.distance);
    private Texture2D pixel;
    private int hitOrder;
    [System.NonSerialized] private GUIStyle numberStyle;
    [System.NonSerialized] private GUIStyle labelStyle;
    [System.NonSerialized] private GUIStyle reportStyle;
    [System.NonSerialized] private GUIStyle helpStyle;

    private void OnEnable() => WeaponController.ServerHitRegistered += HandleHit;
    private void OnDisable() => WeaponController.ServerHitRegistered -= HandleHit;

    private void Awake()
    {
        pixel = Texture2D.whiteTexture;
        useGUILayout = false;
    }

    private TrainingDummy DummyOf(Health health)
    {
        if (health == null) return null;
        if (byHealth.TryGetValue(health, out TrainingDummy dummy)) return dummy;
        dummy = health.GetComponent<TrainingDummy>();
        byHealth[health] = dummy;
        return dummy;
    }

    private DummyState StateOf(TrainingDummy dummy)
    {
        if (!states.TryGetValue(dummy, out DummyState state))
        {
            state = new DummyState();
            states[dummy] = state;
        }
        return state;
    }

    private void HandleHit(Health target, HitboxZoneType zone, float damage, float distance)
    {
        TrainingDummy dummy = DummyOf(target);
        if (dummy == null) return;
        numbers.Add(new HitNumber { dummy = dummy, zone = zone, damage = damage, distance = distance, at = Time.time, order = hitOrder++,
                                    text = $"{Mathf.RoundToInt(damage)} {ZoneLabel(zone)}" });
        DummyState state = StateOf(dummy);
        state.lastHitAt = Time.time;
        if (state.hadFullHealth)
        {
            state.hadFullHealth = false;
            state.seriesStart = Time.time;
            state.seriesHits = 0;
            state.seriesDamage = 0f;
        }
        state.seriesHits++;
        state.seriesDamage += damage;
    }

    private void Update()
    {
        ShootingRange range = ShootingRange.Instance;
        if (range == null) return;
        foreach (TrainingDummy dummy in range.Dummies)
        {
            if (dummy == null || dummy.Health == null || !dummy.Regenerates) continue;
            DummyState state = StateOf(dummy);
            float current = dummy.Health.Current;
            float max = dummy.Health.Max;
            if (state.shown < 0f) state.shown = state.trail = current;

            // Neutralisé : le bilan de la série (le temps depuis la première touche, les impacts).
            if (state.lastHealth > 0f && current <= 0f && !state.hadFullHealth)
            {
                float duration = Time.time - state.seriesStart;
                state.report = SeriesReport(duration, state.seriesHits);
                state.reportUntil = Time.time + killReportLifetime;
            }
            if (current >= max) state.hadFullHealth = true;
            state.lastHealth = current;

            // La vie perdue tombe d'un coup, la part perdue reste rouge un instant puis se vide ; elle
            // remonte en douceur quand le mannequin se relève.
            if (current < state.shown)
            {
                if (state.trail < state.shown) state.trail = state.shown;
                state.trailHoldUntil = Time.time + 0.45f;
                state.shown = current;
            }
            else state.shown = Mathf.MoveTowards(state.shown, current, max * 1.8f * Time.deltaTime);
            if (Time.time >= state.trailHoldUntil) state.trail = Mathf.MoveTowards(state.trail, state.shown, 90f * Time.deltaTime);
            if (state.trail < state.shown) state.trail = state.shown;
        }
        for (int i = numbers.Count - 1; i >= 0; i--)
            if (Time.time - numbers[i].at > numberLifetime || numbers[i].dummy == null) numbers.RemoveAt(i);
    }

    /// <summary>Le bilan d'un mannequin neutralisé : « Neutralisé en 0,71 s · 6 impacts » (un impact
    /// = une balle ou un plomb qui touche). Fonction pure, testée.</summary>
    public static string SeriesReport(float seconds, int hits)
    {
        string time = seconds < 0.005f ? "d'un coup" : $"en {seconds.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} s";
        return $"Neutralisé {time} · {hits} impact{(hits > 1 ? "s" : "")}";
    }

    /// <summary>Le nom d'une zone, tel que le stand l'écrit.</summary>
    public static string ZoneLabel(HitboxZoneType zone) => zone switch
    {
        HitboxZoneType.Head => "tête",
        HitboxZoneType.Legs => "jambes",
        _ => "buste",
    };

    private void BuildStyles()
    {
        if (numberStyle != null) return;
        numberStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        reportStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        helpStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
    }

    private void OnGUI()
    {
        ShootingRange range = ShootingRange.Instance;
        Camera cam = Camera.main;
        if (range == null || cam == null) return;
        BuildStyles();
        // La fenêtre des armes ouverte, rien ne se dessine par-dessus.
        if (ShootingRangePanel.IsOpen || PauseMenu.IsOpen) return;

        Shadowed(new Rect(16f, 16f, 1400f, 24f), "STAND DE TIR   ·   B : armes et accessoires   ·   Échap : menu du stand   ·   la Fosse : porte à droite du poste   ·   la salle de précision : porte à gauche", helpStyle, Color.white);

        // Les mannequins à l'écran, par priorité : celui qu'on vise, puis ceux qu'on vient de toucher,
        // puis les autres, du plus proche au plus loin. Alignés dans l'axe du stand, leurs étiquettes se
        // chevauchaient : chacune ne s'écrit que si elle ne recouvre pas une étiquette plus prioritaire ;
        // la barre de vie, elle, se dessine toujours.
        Vector3 eye = cam.transform.position;
        Vector3 forward = cam.transform.forward;
        // Dans la lunette d'origine d'un fusil de précision, le HUD noircit tout autour du disque (ScopeOverlay) :
        // une étiquette hors du disque y flottait sur le noir.
        NetworkObject local = GameHud.LocalPlayerObject;
        bool scoped = local != null && local.TryGetComponent(out FirstPersonView view) && view.Scoped;
        var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float scopeRadius = ScopeRadius * Screen.height;
        entries.Clear();
        int aimed = -1;
        float aimedAngle = AimedAngle;
        foreach (TrainingDummy dummy in range.Dummies)
        {
            // Les cibles de la Fosse n'ont ni barre ni étiquette (le parcours a son chrono) ; leurs
            // chiffres de dégâts restent.
            if (dummy == null || dummy.Health == null || !dummy.Regenerates) continue;
            if (!OnScreen(cam, dummy.LabelPoint, out Vector2 p)) continue;
            if (scoped && (p - screenCenter).sqrMagnitude > scopeRadius * scopeRadius) continue;
            // L'étiquette laisse au-dessus de la tête la place du marqueur de touche (HitMarker, jusqu'à 25 unités
            // au-dessus du point visé) : de loin, elle tombait sur ses branches du haut quand on visait la tête.
            if (OnScreen(cam, dummy.HeadPoint, out Vector2 head)) p.y = Mathf.Min(p.y, head.y - MarkerClearance * Screen.height / 1080f);
            // Un mannequin caché par un mur n'a pas d'étiquette : depuis la Fosse, on voyait celles du
            // stand à travers la cloison.
            if (Physics.Linecast(eye, dummy.HeadPoint, 1, QueryTriggerInteraction.Ignore)) continue;
            DummyState state = StateOf(dummy);
            float angle = Vector3.Angle(forward, dummy.HeadPoint - Vector3.up * 0.4f - eye);
            if (angle < aimedAngle) { aimedAngle = angle; aimed = entries.Count; }
            bool recent = Time.time - state.lastHitAt < 3f || (state.report != null && Time.time < state.reportUntil);
            entries.Add(new Entry { dummy = dummy, point = p, distance = Vector3.Distance(eye, dummy.HeadPoint), rank = recent ? 1 : 2 });
        }
        if (aimed >= 0)
        {
            Entry e = entries[aimed];
            e.rank = 0;
            entries[aimed] = e;
        }
        entries.Sort(ByPriority);

        // Les barres : d'abord celles du second plan, du plus loin au plus proche, puis les prioritaires
        // par-dessus.
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].rank == 2) DrawBar(entries[i], StateOf(entries[i].dummy));
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].rank < 2) DrawBar(entries[i], StateOf(entries[i].dummy));

        // Les barres prioritaires, puis les textes un à un : un texte qui recouvrirait l'un d'eux se tait.
        placed.Clear();
        foreach (Entry e in entries)
            if (e.rank < 2) placed.Add(BarRect(e));
        foreach (Entry e in entries)
        {
            DummyState state = StateOf(e.dummy);
            Rect bar = BarRect(e);
            int health = e.dummy.Health.IsDead ? 0 : Mathf.CeilToInt(e.dummy.Health.Current);
            int meters = Mathf.RoundToInt(e.distance);
            if (state.label == null || health != state.labelHealth || meters != state.labelDistance)
            {
                state.labelHealth = health;
                state.labelDistance = meters;
                state.label = (e.dummy.Health.IsDead ? "NEUTRALISÉ" : $"{health} PV") + $"   ·   {meters} m";
            }
            var text = new Rect(e.point.x - 80f, bar.y - 20f, 160f, 18f);
            bool showReport = state.report != null && Time.time < state.reportUntil;
            var report = new Rect(e.point.x - 160f, bar.y - 40f, 320f, 20f);
            if (Overlaps(text) || (showReport && Overlaps(report))) continue;
            placed.Add(text);
            Shadowed(text, state.label, labelStyle, Color.white);
            if (!showReport) continue;
            placed.Add(report);
            Shadowed(report, state.report, reportStyle, new Color(1f, 0.85f, 0.35f));
        }

        // Les chiffres des touches, à droite du buste, qui montent et s'effacent sans atteindre
        // l'étiquette ni le bilan ; ceux d'un même instant (les plombs d'un pompe) s'écartent.
        foreach (HitNumber n in numbers)
        {
            Vector3 anchor = n.dummy.HeadPoint - Vector3.up * 0.35f;
            if (!OnScreen(cam, anchor, out Vector2 p)) continue;
            float age = (Time.time - n.at) / numberLifetime;
            float spread = (n.order % 5) * 14f;
            Color color = n.zone == HitboxZoneType.Head ? new Color(1f, 0.82f, 0.2f)
                        : n.zone == HitboxZoneType.Legs ? new Color(0.75f, 0.78f, 0.82f) : Color.white;
            color.a = 1f - age * age;
            Shadowed(new Rect(p.x + 24f + spread, p.y - 12f - age * 32f - (n.order % 5) * 6f, 140f, 24f), n.text, numberStyle, color);
        }
        GUI.color = Color.white;
    }

    /// <summary>Un mannequin est « visé » à moins de tant de degrés du réticule (son buste).</summary>
    private const float AimedAngle = 4f;

    /// <summary>Entre la tête et le bas de son étiquette, au moins tant de pixels en 1080p : la place des
    /// branches du haut du marqueur de touche quand on vise la tête.</summary>
    private const float MarkerClearance = 30f;

    /// <summary>Le rayon de l'ouverture de la lunette d'origine, en part de la hauteur de l'écran (CrosshairUI :
    /// 92 % de diamètre).</summary>
    private const float ScopeRadius = 0.46f;

    private static Rect BarRect(Entry e)
    {
        float scale = Mathf.Clamp(14f / Mathf.Max(1f, e.distance), 0.75f, 1.2f);
        float width = 96f * scale, height = 9f * scale;
        return new Rect(e.point.x - width * 0.5f, e.point.y - height, width, height);
    }

    private void DrawBar(Entry e, DummyState state)
    {
        Rect bar = BarRect(e);
        float max = Mathf.Max(1f, e.dummy.Health.Max);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(bar.x - 2f, bar.y - 2f, bar.width + 4f, bar.height + 4f), pixel);
        GUI.color = new Color(0.85f, 0.15f, 0.12f, 0.95f);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(state.trail / max), bar.height), pixel);
        float fraction = Mathf.Clamp01(state.shown / max);
        GUI.color = fraction <= 0.3f ? new Color(1f, 0.35f, 0.25f, 1f) : new Color(0.95f, 0.95f, 0.95f, 1f);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * fraction, bar.height), pixel);
        GUI.color = Color.white;
    }

    private bool Overlaps(Rect r)
    {
        foreach (Rect other in placed)
            if (other.Overlaps(r)) return true;
        return false;
    }

    private static bool OnScreen(Camera cam, Vector3 world, out Vector2 gui)
    {
        Vector3 s = cam.WorldToScreenPoint(world);
        gui = new Vector2(s.x, Screen.height - s.y);
        return s.z > 0.1f;
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
