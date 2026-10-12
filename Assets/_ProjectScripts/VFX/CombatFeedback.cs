using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Les retours de combat du joueur LOCAL (GDD § 6) :
/// - toucher : hitmarker et son de touche, un son différent pour la tête, un autre pour le coup
///   fatal ;
/// - être touché : parasites et interférences à l'écran (un robot ne saigne pas), son
///   d'interférence, et un très léger sursaut de caméra qui revient tout seul ;
/// - la mort d'un robot, vue par tous : court-circuit, étincelles, fumée et son signature.
///
/// Posé sur la racine du Player ; ne fait rien sur les joueurs que cette machine ne possède pas,
/// donc il tourne exactement une fois par machine.
///
/// 🚨 Les touches sont CONFIRMÉES PAR LE SERVEUR, pas prédites. Le hitmarker part quand la vie
/// réseautée de l'adversaire baisse (sondage, même choix que RoundManager pour la mort : pas de
/// cycle de vie d'abonnement à gérer). Il arrive donc un aller-retour après le tir, mais il ne
/// ment jamais : un tir que le serveur a refusé (rewind dépassé, mur) n'affiche rien. Seule la
/// ZONE vient du tir local (<see cref="ReportLocalHit"/>), le serveur ne la publiant pas.
///
/// En 1v1, toute baisse de vie de l'adversaire vient du joueur local. Le jour où des spectateurs
/// pourront blesser (cailloux, GDD § 9), il faudra que le serveur dise qui a touché.
/// </summary>
[DisallowMultipleComponent]
public class CombatFeedback : MonoBehaviour
{
    [Header("Sons (2D, joueur local uniquement)")]
    [SerializeField] private AudioClip[] hitBodySounds;
    [SerializeField] private AudioClip[] hitHeadSounds;
    [SerializeField] private AudioClip[] killSounds;
    [SerializeField] private AudioClip[] damageTakenSounds;
    [SerializeField, Range(0f, 1f)] private float hitVolume = 0.75f;
    [SerializeField, Range(0f, 1f)] private float damageVolume = 0.8f;

    [Header("Hitmarker")]
    [SerializeField] private float hitmarkerDuration = 0.22f;
    [SerializeField] private float hitmarkerGap = 7f;
    [SerializeField] private float hitmarkerLength = 7f;
    [SerializeField] private float hitmarkerThickness = 2f;
    [SerializeField] private Color hitmarkerColor = Color.white;
    [SerializeField] private Color headshotColor = new Color(1f, 0.32f, 0.22f);
    [Tooltip("Fenêtre (s) pendant laquelle la zone d'un tir local est rapprochée d'une baisse de vie confirmée. Doit couvrir l'aller-retour réseau.")]
    [SerializeField] private float zoneMatchWindow = 0.6f;

    [Header("Interférences (dégâts reçus)")]
    [SerializeField] private float interferenceDuration = 0.35f;
    [SerializeField, Range(0f, 1f)] private float interferenceStrength = 0.55f;
    [SerializeField, Range(4, 40)] private int interferenceBands = 14;

    [Header("Sursaut de caméra (dégâts reçus)")]
    [Tooltip("Degrés. Très léger exprès : le sursaut punit déjà le joueur touché en premier (GDD § 6). 0 pour le couper.")]
    [SerializeField] private float cameraJoltDegrees = 0.5f;
    [SerializeField] private float cameraJoltReturnTime = 0.12f;

    private NetworkObject networkObject;
    private Health ownHealth;
    private PlayerCameraLook cameraLook;
    private readonly Dictionary<PlayerLocomotion, Tracked> tracked = new Dictionary<PlayerLocomotion, Tracked>();
    private readonly List<PlayerLocomotion> stale = new List<PlayerLocomotion>();

    private float hitmarkerUntil;
    private bool hitmarkerHead, hitmarkerKill;
    private float interferenceStart = -10f, interferenceScale;
    private float joltRemainingPitch, joltRemainingYaw, joltReturnUntil;
    private Texture2D pixel;

    private class Tracked
    {
        public Health health;
        public PlayerHitboxZone torso;
        public float lastValue;
    }

    private static Health lastHitTarget;
    private static HitboxZoneType lastHitZone;
    private static float lastHitTime = -10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        lastHitTarget = null;
        lastHitTime = -10f;
    }

    /// <summary>Appelée quand un tir LOCAL atteint un hitbox (prédiction du tireur) : retient la
    /// zone, que le hitmarker confirmé par le serveur viendra chercher.</summary>
    public static void ReportLocalHit(Health target, HitboxZoneType zone)
    {
        lastHitTarget = target;
        lastHitZone = zone;
        lastHitTime = Time.unscaledTime;
    }

    private bool IsLocalPlayer
    {
        get
        {
            if (networkObject == null) return true; // composant testé hors réseau
            return networkObject.IsSpawned && networkObject.IsOwner;
        }
    }

    private void Awake()
    {
        networkObject = GetComponent<NetworkObject>();
        ownHealth = GetComponent<Health>();
        cameraLook = GetComponentInChildren<PlayerCameraLook>();
        pixel = Texture2D.whiteTexture;
    }

    private void Update()
    {
        if (!IsLocalPlayer) return;
        PollHealth();
        ReturnJolt();
    }

    private void PollHealth()
    {
        var players = PlayerLocomotion.SpawnedPlayers;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerLocomotion player = players[i];
            if (player == null) continue;
            if (!tracked.TryGetValue(player, out Tracked t))
            {
                Health h = player.GetComponent<Health>();
                if (h == null) continue;
                t = new Tracked { health = h, torso = FindTorso(player), lastValue = h.Current };
                tracked[player] = t;
                continue;
            }

            float current = t.health.Current;
            if (current < t.lastValue - 0.001f)
            {
                bool died = current <= 0f && t.lastValue > 0f;
                if (t.health == ownHealth) OnOwnDamaged(t.lastValue - current);
                else OnOpponentDamaged(t.health, died);
                if (died) Vfx.DeathBurst(TorsoPosition(player, t));
            }
            t.lastValue = current;
        }

        // Joueurs déconnectés ou détruits.
        stale.Clear();
        foreach (var kv in tracked)
        {
            if (kv.Key == null || kv.Value.health == null) stale.Add(kv.Key);
        }
        foreach (var k in stale) tracked.Remove(k);
    }

    private void OnOpponentDamaged(Health target, bool died)
    {
        bool head = lastHitTarget == target && lastHitZone == HitboxZoneType.Head
            && Time.unscaledTime - lastHitTime <= zoneMatchWindow;

        hitmarkerUntil = Time.unscaledTime + hitmarkerDuration * (died ? 1.8f : 1f);
        hitmarkerHead = head;
        hitmarkerKill = died;

        AudioClip[] clips = died ? killSounds : head ? hitHeadSounds : hitBodySounds;
        if (died && (clips == null || clips.Length == 0)) clips = head ? hitHeadSounds : hitBodySounds;
        Sfx.Play2D(VfxLibrary.Pick(clips), hitVolume, 0.03f);
    }

    private void OnOwnDamaged(float amount)
    {
        float max = ownHealth != null && ownHealth.Max > 0f ? ownHealth.Max : 100f;
        // Une balle de MP5 (17) donne une interférence nette ; un coup fatal, le maximum.
        interferenceScale = Mathf.Clamp(amount / max * 4f, 0.45f, 1f);
        interferenceStart = Time.unscaledTime;
        Sfx.Play2D(VfxLibrary.Pick(damageTakenSounds), damageVolume, 0.05f);

        if (cameraLook != null && cameraJoltDegrees > 0f)
        {
            float pitch = cameraJoltDegrees * Random.Range(0.6f, 1f);
            float yaw = cameraJoltDegrees * Random.Range(-0.6f, 0.6f);
            cameraLook.AddInstantRotation(pitch, yaw);
            joltRemainingPitch += pitch;
            joltRemainingYaw += yaw;
            joltReturnUntil = Time.unscaledTime + cameraJoltReturnTime;
        }
    }

    /// <summary>Le sursaut revient à sa place : le joueur garde sa visée, il a juste été secoué.</summary>
    private void ReturnJolt()
    {
        if (cameraLook == null || (joltRemainingPitch == 0f && joltRemainingYaw == 0f)) return;
        float remaining = joltReturnUntil - Time.unscaledTime;
        float k = remaining <= Time.unscaledDeltaTime ? 1f : Mathf.Clamp01(Time.unscaledDeltaTime / remaining);
        float p = joltRemainingPitch * k;
        float y = joltRemainingYaw * k;
        cameraLook.AddInstantRotation(-p, -y);
        joltRemainingPitch -= p;
        joltRemainingYaw -= y;
        if (k >= 1f) joltRemainingPitch = joltRemainingYaw = 0f;
    }

    private static PlayerHitboxZone FindTorso(PlayerLocomotion player)
    {
        foreach (var zone in player.GetComponentsInChildren<PlayerHitboxZone>(true))
        {
            if (zone.Zone == HitboxZoneType.Torso) return zone;
        }
        return null;
    }

    private static Vector3 TorsoPosition(PlayerLocomotion player, Tracked t)
    {
        if (t.torso != null) return t.torso.transform.position;
        return player.transform.position + Vector3.up * 1.1f;
    }

    // ------------------------------------------------------------------ affichage

    private void OnGUI()
    {
        if (!IsLocalPlayer || Event.current.type != EventType.Repaint) return;
        DrawInterference();
        DrawHitmarker();
        GUI.color = Color.white;
    }

    private void DrawHitmarker()
    {
        float left = hitmarkerUntil - Time.unscaledTime;
        if (left <= 0f) return;
        float k = Mathf.Clamp01(left / hitmarkerDuration);

        Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float scale = hitmarkerKill ? 1.5f : 1f;
        float gap = hitmarkerGap * scale;
        float len = hitmarkerLength * scale;
        float th = hitmarkerThickness * (hitmarkerKill ? 1.5f : 1f);
        Color col = hitmarkerHead || hitmarkerKill ? headshotColor : hitmarkerColor;
        GUI.color = new Color(col.r, col.g, col.b, Mathf.Clamp01(k * 1.5f));

        // Une croix en X : les quatre branches du réticule, tournées de 45°.
        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, c);
        GUI.DrawTexture(new Rect(c.x - th * 0.5f, c.y - gap - len, th, len), pixel);
        GUI.DrawTexture(new Rect(c.x - th * 0.5f, c.y + gap, th, len), pixel);
        GUI.DrawTexture(new Rect(c.x - gap - len, c.y - th * 0.5f, len, th), pixel);
        GUI.DrawTexture(new Rect(c.x + gap, c.y - th * 0.5f, len, th), pixel);
        GUI.matrix = saved;
    }

    private void DrawInterference()
    {
        float t = (Time.unscaledTime - interferenceStart) / interferenceDuration;
        if (t < 0f || t >= 1f) return;
        float k = (1f - t) * (1f - t) * interferenceStrength * interferenceScale;

        // Le motif change à chaque image mais reste identique entre deux appels d'OnGUI d'une même
        // image : c'est une image de parasites, pas un scintillement aléatoire.
        var saved = Random.state;
        Random.InitState(Time.frameCount * 7919);

        float w = Screen.width, h = Screen.height;
        // Voile froid très léger sur tout l'écran, comme un capteur qui sature.
        GUI.color = new Color(0.55f, 0.75f, 0.85f, k * 0.12f);
        GUI.DrawTexture(new Rect(0f, 0f, w, h), pixel);

        for (int i = 0; i < interferenceBands; i++)
        {
            float y = Random.value * h;
            float bandH = Mathf.Lerp(1f, h * 0.035f, Random.value * Random.value);
            float x = Random.Range(-0.2f, 0.3f) * w;
            float bw = Random.Range(0.3f, 1.1f) * w;
            bool bright = Random.value < 0.35f;
            GUI.color = bright
                ? new Color(0.9f, 0.97f, 1f, k * Random.Range(0.25f, 0.6f))
                : new Color(0.05f, 0.07f, 0.09f, k * Random.Range(0.35f, 0.8f));
            GUI.DrawTexture(new Rect(x, y, bw, bandH), pixel);
        }

        // Bords qui décrochent : deux bandes latérales décalées, signature d'un signal vidéo perdu.
        float tear = k * w * 0.02f;
        GUI.color = new Color(0f, 0f, 0f, k * 0.6f);
        GUI.DrawTexture(new Rect(0f, 0f, tear, h), pixel);
        GUI.DrawTexture(new Rect(w - tear, 0f, tear, h), pixel);

        Random.state = saved;
    }
}
