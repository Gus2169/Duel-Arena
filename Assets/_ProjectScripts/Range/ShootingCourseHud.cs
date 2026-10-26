using System.Collections.Generic;
using System.Globalization;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ce que la Fosse affiche (2026-10-16) : au départ, la consigne et le record de l'arme en main ; en
/// course, le chrono et les hostiles tombés ; à l'arrivée, le bilan à la manière de Modern Warfare 2
/// (temps, pénalités, bonus de précision, temps final, record). Il lit l'état publié par le serveur
/// (ShootingCourse) et ne décide rien. HUD OnGUI, placeholder assumé comme celui du stand.
/// </summary>
[DisallowMultipleComponent]
public class ShootingCourseHud : MonoBehaviour
{
    [Tooltip("Durée (s) du bilan à l'écran.")]
    [SerializeField] private float resultLifetime = 12f;
    [Tooltip("Durée (s) du message d'abandon.")]
    [SerializeField] private float abortLifetime = 4f;

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private int shownRun;
    private float resultShownAt = -100f;
    private Texture2D pixel;
    [System.NonSerialized] private GUIStyle chronoStyle;
    [System.NonSerialized] private GUIStyle lineStyle;
    [System.NonSerialized] private GUIStyle titleStyle;
    [System.NonSerialized] private GUIStyle bigStyle;

    // Les textes ne se refont qu'au changement (rien n'alloue à chaque image).
    private int chronoCentis = -1;
    private string chronoText = "";
    private int countsKey = -1;
    private string countsText = "";
    private int hintKey = int.MinValue;
    private string hintText = "";
    private int resultRun = -1;
    private string[] resultLines = new string[0];

    private void Awake()
    {
        pixel = Texture2D.whiteTexture;
        useGUILayout = false;
    }

    private void Update()
    {
        ShootingCourse course = ShootingCourse.Instance;
        if (course == null) return;
        CourseResult r = course.LastResult;
        if (r.run != shownRun)
        {
            shownRun = r.run;
            if (r.run > 0) resultShownAt = Time.time;
        }
        if (course.Running) resultShownAt = -100f;
    }

    private void BuildStyles()
    {
        if (chronoStyle != null) return;
        chronoStyle = new GUIStyle(GUI.skin.label) { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }

    private void OnGUI()
    {
        ShootingCourse course = ShootingCourse.Instance;
        if (course == null || ShootingRangePanel.IsOpen) return;
        BuildStyles();
        float cx = Screen.width * 0.5f;

        if (course.Running)
        {
            int centis = Mathf.FloorToInt(course.Elapsed * 100f);
            if (centis != chronoCentis)
            {
                chronoCentis = centis;
                chronoText = Chrono(centis / 100f);
            }
            int key = course.HostilesDown * 4096 + course.HostilesTotal * 16 + Mathf.Min(15, course.CiviliansHit);
            if (key != countsKey)
            {
                countsKey = key;
                countsText = $"Hostiles {course.HostilesDown}/{course.HostilesTotal}   ·   Civils touchés {course.CiviliansHit}";
            }
            Shadowed(new Rect(cx - 200f, 52f, 400f, 50f), chronoText, chronoStyle, Color.white);
            Shadowed(new Rect(cx - 300f, 100f, 600f, 24f), countsText, lineStyle, Color.white);
            return;
        }

        CourseResult r = course.LastResult;
        float age = Time.time - resultShownAt;
        if (r.run > 0 && r.aborted != 0 && age < abortLifetime)
        {
            Shadowed(new Rect(cx - 400f, 60f, 800f, 26f), "LA FOSSE · course abandonnée : l'arrivée se franchit en revenant par le dernier couloir", titleStyle, new Color(1f, 0.6f, 0.4f));
            return;
        }
        if (r.run > 0 && r.aborted == 0 && age < resultLifetime)
        {
            DrawResult(r);
            return;
        }

        PlayerLocomotion player = LocalPlayer();
        if (player != null && course.StartZone.Contains(player.transform.position)) DrawHint(player);
    }

    private void DrawResult(CourseResult r)
    {
        if (resultRun != r.run)
        {
            resultRun = r.run;
            resultLines = ResultLines(r, WeaponName(r.weaponKey));
        }
        float cx = Screen.width * 0.5f;
        float height = 70f + 26f * (resultLines.Length - 2) + 44f;
        var panel = new Rect(cx - 260f, 60f, 520f, height);
        GUI.color = new Color(0f, 0f, 0f, 0.62f);
        GUI.DrawTexture(panel, pixel);
        GUI.color = Color.white;

        float y = panel.y + 10f;
        Shadowed(new Rect(panel.x, y, panel.width, 26f), resultLines[0], titleStyle, new Color(1f, 0.85f, 0.35f));
        y += 34f;
        for (int i = 1; i < resultLines.Length - 2; i++, y += 26f)
            Shadowed(new Rect(panel.x, y, panel.width, 24f), resultLines[i], lineStyle, Color.white);
        Shadowed(new Rect(panel.x, y + 2f, panel.width, 38f), resultLines[resultLines.Length - 2], bigStyle, Color.white);
        y += 42f;
        Color last = r.record != 0 ? new Color(0.45f, 1f, 0.5f) : new Color(0.85f, 0.88f, 0.92f);
        Shadowed(new Rect(panel.x, y, panel.width, 24f), resultLines[resultLines.Length - 1], lineStyle, last);
    }

    private void DrawHint(PlayerLocomotion player)
    {
        WeaponController weapon = player.GetComponentInChildren<WeaponController>();
        PlayerInputReader input = player.GetComponent<PlayerInputReader>();
        int slot = weapon != null ? weapon.LocalSlot : 0;
        float best = CourseRecords.Best(slot);
        int key = slot * 100000 + (float.IsNaN(best) ? 99999 : Mathf.RoundToInt(best * 100f) % 99999);
        if (key != hintKey)
        {
            hintKey = key;
            string record = float.IsNaN(best) ? "pas encore de record" : $"record : {Seconds(best)}";
            hintText = $"Arme en main : {WeaponName(slot)} ({record})   ·   Franchir : {(input != null ? input.KeyFor("Jump") : "Espace")}   ·   Taser : {(input != null ? input.KeyFor("Melee") : "V")}";
        }
        float cx = Screen.width * 0.5f;
        Shadowed(new Rect(cx - 400f, 60f, 800f, 26f), "LA FOSSE · franchis la ligne de départ pour lancer le chrono", titleStyle, new Color(1f, 0.85f, 0.35f));
        Shadowed(new Rect(cx - 400f, 88f, 800f, 22f), "Neutralise les hostiles (rouges), épargne les civils (blancs), reviens par l'arrivée", lineStyle, Color.white);
        Shadowed(new Rect(cx - 500f, 112f, 1000f, 22f), hintText, lineStyle, new Color(0.85f, 0.88f, 0.92f));
    }

    /// <summary>Les lignes du bilan : titre, temps, pénalités, précision, temps final, record. Fonction
    /// pure, testée.</summary>
    public static string[] ResultLines(CourseResult r, string weaponName)
    {
        int accuracy = Mathf.RoundToInt(r.accuracy * 100f);
        float bonus = ShootingCourse.AccuracyBonusPerPercent * 100f * Mathf.Clamp01(r.accuracy);
        string record = r.record != 0
            ? (float.IsNaN(r.previousBest) ? $"Premier record · {weaponName}" : $"Nouveau record · {weaponName} (ancien : {Seconds(r.previousBest)})")
            : $"Record · {weaponName} : {Seconds(r.previousBest)}";
        return new[]
        {
            "LA FOSSE · BILAN",
            $"Temps   {Seconds(r.rawTime)}",
            $"Hostiles manqués   {r.missedHostiles}   +{Seconds(ShootingCourse.MissPenalty * r.missedHostiles)}",
            $"Civils touchés   {r.civiliansHit}   +{Seconds(ShootingCourse.CivilianPenalty * r.civiliansHit)}",
            $"Précision   {accuracy} %   −{Seconds(bonus)}",
            $"TEMPS FINAL   {Seconds(r.finalTime)}",
            record,
        };
    }

    /// <summary>« 41,20 s ». Fonction pure, testée.</summary>
    public static string Seconds(float seconds) => seconds.ToString("0.00", French) + " s";

    /// <summary>Le chrono : « 27,43 », puis « 1:02,15 » passé la minute. Fonction pure, testée.</summary>
    public static string Chrono(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int minutes = Mathf.FloorToInt(seconds / 60f);
        float rest = seconds - minutes * 60f;
        return minutes > 0 ? $"{minutes}:{rest.ToString("00.00", French)}" : rest.ToString("0.00", French);
    }

    private static PlayerLocomotion LocalPlayer()
    {
        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject own = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
        return own != null ? own.GetComponent<PlayerLocomotion>() : null;
    }

    /// <summary>Le nom d'une arme de la panoplie (« panoplie mixte » pour une course où l'on en a changé).</summary>
    public static string WeaponName(int weaponKey)
    {
        if (weaponKey < 0) return "panoplie mixte";
        PlayerLocomotion player = LocalPlayer();
        WeaponController weapon = player != null ? player.GetComponentInChildren<WeaponController>() : null;
        IReadOnlyList<WeaponData> loadout = weapon != null ? weapon.Loadout : null;
        if (loadout != null && weaponKey < loadout.Count && loadout[weaponKey] != null)
            return loadout[weaponKey].weaponName.ToLower(French);
        return $"arme {weaponKey + 1}";
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
