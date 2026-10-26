using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Ce qui arrive au parcours à cette image (ShootingCourse.Evaluate).</summary>
public enum CourseEvent
{
    None,
    Start,
    Finish,
    Abort,
}

/// <summary>Le bilan publié de la dernière course (ou de son abandon).</summary>
public struct CourseResult : INetworkSerializeByMemcpy
{
    /// <summary>Le numéro de la course (0 : aucune encore) : le HUD voit ainsi qu'il y en a une nouvelle.</summary>
    public int run;
    public byte aborted;
    public float rawTime;
    public byte missedHostiles;
    public byte civiliansHit;
    /// <summary>Les balles et plombs qui ont touché une cible hostile, sur ceux tirés (0 à 1).</summary>
    public float accuracy;
    public float finalTime;
    /// <summary>La place de l'arme de toute la course, ou CourseRecords.MixedLoadout.</summary>
    public sbyte weaponKey;
    public byte record;
    /// <summary>Le record d'avant (NaN s'il n'y en avait pas).</summary>
    public float previousBest;
}

/// <summary>
/// La Fosse (2026-10-16, demandée par l'utilisateur) : le parcours chronométré du stand de tir, sur le
/// modèle de « The Pit » de Call of Duty: Modern Warfare 2 (2009). On franchit la ligne de départ, le
/// chrono part ; chaque section où l'on entre fait lever ses cibles (CourseTarget) ; on neutralise les
/// hostiles, on épargne les civils ; la course finit quand on revient au départ par la dernière section.
///
/// Le temps final reprend les règles de la Fosse : +2 s par hostile resté debout, +2 s par civil touché,
/// et 0,05 s de moins par point de précision (80 % : -4 s ; FinalTime). Les cibles ont la vie d'un robot
/// (la Fosse est aussi un essai des armes) ; « une touche suffit » les fait tomber au premier impact,
/// comme les cibles en carton de l'original (OneHit, la fenêtre du stand).
///
/// Tout se décide sur le serveur, d'après la position du joueur (les sections sont des boîtes : aucun
/// déclencheur physique) ; l'état publié suffit au HUD (ShootingCourseHud). Le stand tourne chez l'hôte,
/// seul : le coureur est son joueur. Les records restent sur la machine (CourseRecords).
/// </summary>
[DisallowMultipleComponent]
public class ShootingCourse : NetworkBehaviour
{
    /// <summary>Une section du parcours : son nom et la boîte où se tient le joueur qui la traverse.</summary>
    [System.Serializable]
    public struct Section
    {
        public string name;
        public Bounds bounds;
    }

    /// <summary>Pénalité (s) par hostile resté debout à l'arrivée (la Fosse : 2 s).</summary>
    public const float MissPenalty = 2f;
    /// <summary>Pénalité (s) par civil touché (la Fosse : 2 s par civil abattu).</summary>
    public const float CivilianPenalty = 2f;
    /// <summary>Bonus (s) par point de précision (la Fosse : 0,05 s par %).</summary>
    public const float AccuracyBonusPerPercent = 0.05f;

    [Tooltip("La zone de départ : on lance le chrono en la quittant vers la première section, on l'arrête en y revenant par la dernière.")]
    [SerializeField] private Bounds startZone;
    [Tooltip("Les sections, dans l'ordre du parcours.")]
    [SerializeField] private Section[] sections = new Section[0];

    private readonly NetworkVariable<bool> running = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<double> startedAt = new NetworkVariable<double>(
        0.0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<CourseResult> result = new NetworkVariable<CourseResult>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> oneHit = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<byte> civiliansHit = new NetworkVariable<byte>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private static ShootingCourse cached;
    private readonly List<CourseTarget> targets = new List<CourseTarget>();
    private readonly Dictionary<Health, CourseTarget> byHealth = new Dictionary<Health, CourseTarget>();

    // Serveur : la course en cours.
    private bool wasInStart;
    private int lastSection = -1;
    private uint raisedSections;
    private int pelletsFired;
    private int hostileImpacts;
    private int weaponAtStart;
    private bool mixedLoadout;
    private readonly HashSet<CourseTarget> touchedCivilians = new HashSet<CourseTarget>();

    public static ShootingCourse Instance
    {
        get
        {
            if (cached == null) cached = FindAnyObjectByType<ShootingCourse>();
            return cached;
        }
    }

    public bool Running => running.Value;
    public bool OneHit => oneHit.Value;
    public int CiviliansHit => civiliansHit.Value;
    public CourseResult LastResult => result.Value;
    public Bounds StartZone => startZone;
    public IReadOnlyList<Section> Sections => sections;
    public IReadOnlyList<CourseTarget> Targets => targets;
    /// <summary>Les hostiles du parcours, et ceux qui sont tombés (calculés à chaque image).</summary>
    public int HostilesTotal { get; private set; }
    public int HostilesDown { get; private set; }

    /// <summary>L'heure du parcours : celle du serveur.</summary>
    public double Now => IsSpawned ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

    /// <summary>Le chrono de la course en cours (s), 0 hors course.</summary>
    public float Elapsed => running.Value ? (float)(Now - startedAt.Value) : 0f;

    private void Awake()
    {
        targets.AddRange(FindObjectsByType<CourseTarget>());
    }

    public override void OnNetworkSpawn()
    {
        cached = this;
        if (!IsServer) return;
        byHealth.Clear();
        foreach (CourseTarget t in targets)
            if (t != null && t.Health != null) byHealth[t.Health] = t;
        WeaponController.ServerHitRegistered += HandleHit;
        WeaponController.ServerShotFired += HandleShot;
    }

    public override void OnNetworkDespawn()
    {
        WeaponController.ServerHitRegistered -= HandleHit;
        WeaponController.ServerShotFired -= HandleShot;
    }

    private void Update()
    {
        double now = Now;
        bool moved = false;
        int total = 0, down = 0;
        foreach (CourseTarget t in targets)
        {
            if (t == null) continue;
            moved |= t.ApplyPose(t.Motion, now);
            if (!t.IsHostile) continue;
            total++;
            if (t.State == CourseTargetState.Down) down++;
        }
        HostilesTotal = total;
        HostilesDown = down;
        // Physics.autoSyncTransforms vaut false : une cible qui bascule ou glisse doit être resynchronisée
        // pour que les tirs (qui ne rembobinent pas les mannequins) la trouvent là où on la voit.
        if (moved) Physics.SyncTransforms();

        if (IsServer && IsSpawned) ServerStep(now);
    }

    private void ServerStep(double now)
    {
        PlayerLocomotion runner = Runner();
        if (runner == null) return;
        Vector3 p = runner.transform.position;
        bool inStart = startZone.Contains(p);
        bool inFirst = sections.Length > 0 && sections[0].bounds.Contains(p);
        CourseEvent e = Evaluate(running.Value, wasInStart, inStart, inFirst, lastSection, sections.Length - 1);
        wasInStart = inStart;
        switch (e)
        {
            case CourseEvent.Start: ServerStart(runner, now); break;
            case CourseEvent.Finish: ServerFinish(now); break;
            case CourseEvent.Abort: ServerAbort(); break;
        }
        if (!running.Value) return;

        for (int i = 0; i < sections.Length && i < 32; i++)
        {
            if (!sections[i].bounds.Contains(p)) continue;
            if ((raisedSections & (1u << i)) == 0) RaiseSection(i, now);
            if (i > lastSection) lastSection = i;
        }

        foreach (CourseTarget t in targets)
        {
            if (t == null) continue;
            t.ServerUpdate(now);
            if (t.IsHostile || t.State == CourseTargetState.Hidden || t.Health == null) continue;
            if (t.Health.Current < t.Health.Max && touchedCivilians.Add(t))
                civiliansHit.Value = (byte)Mathf.Min(255, touchedCivilians.Count);
        }

        WeaponController weapon = runner.GetComponentInChildren<WeaponController>();
        if (weapon != null && weapon.DisplayedSlot != weaponAtStart) mixedLoadout = true;
    }

    private void ServerStart(PlayerLocomotion runner, double now)
    {
        foreach (CourseTarget t in targets) if (t != null) t.ServerHide();
        touchedCivilians.Clear();
        civiliansHit.Value = 0;
        pelletsFired = 0;
        hostileImpacts = 0;
        raisedSections = 0;
        lastSection = 0;
        WeaponController weapon = runner.GetComponentInChildren<WeaponController>();
        weaponAtStart = weapon != null ? weapon.DisplayedSlot : 0;
        mixedLoadout = false;
        startedAt.Value = now;
        running.Value = true;
        RaiseSection(0, now);
    }

    private void ServerFinish(double now)
    {
        float raw = (float)(now - startedAt.Value);
        int missed = 0;
        foreach (CourseTarget t in targets)
            if (t != null && t.IsHostile && t.State != CourseTargetState.Down) missed++;
        float accuracy = pelletsFired > 0 ? Mathf.Clamp01((float)hostileImpacts / pelletsFired) : 0f;
        float final = FinalTime(raw, missed, civiliansHit.Value, accuracy);
        int key = mixedLoadout ? CourseRecords.MixedLoadout : weaponAtStart;
        bool record = CourseRecords.TryRecord(key, final, out float previous);
        result.Value = new CourseResult
        {
            run = result.Value.run + 1,
            rawTime = raw,
            missedHostiles = (byte)Mathf.Min(255, missed),
            civiliansHit = civiliansHit.Value,
            accuracy = accuracy,
            finalTime = final,
            weaponKey = (sbyte)key,
            record = (byte)(record ? 1 : 0),
            previousBest = previous,
        };
        running.Value = false;
        Debug.Log($"[Fosse] Course {result.Value.run} : {raw:F2} s, {missed} hostile(s) manqué(s), {civiliansHit.Value} civil(s) touché(s), " +
                  $"précision {accuracy * 100f:F0} % ({hostileImpacts}/{pelletsFired}), temps final {final:F2} s{(record ? ", record" : "")}.");
    }

    private void ServerAbort()
    {
        running.Value = false;
        result.Value = new CourseResult { run = result.Value.run + 1, aborted = 1, previousBest = float.NaN };
    }

    private void RaiseSection(int index, double now)
    {
        raisedSections |= 1u << index;
        foreach (CourseTarget t in targets)
            if (t != null && t.Section == index) t.ServerRaise(now);
    }

    /// <summary>Serveur : la Fosse revient à son état de départ (cibles cachées, aucune course).</summary>
    public void ServerReset()
    {
        if (!IsServer) return;
        foreach (CourseTarget t in targets) if (t != null) t.ServerHide();
        running.Value = false;
        lastSection = -1;
        raisedSections = 0;
    }

    /// <summary>Serveur : les cibles tombent-elles au premier impact ?</summary>
    public void ServerSetOneHit(bool value)
    {
        if (IsServer) oneHit.Value = value;
    }

    private PlayerLocomotion Runner()
    {
        NetworkObject own = NetworkManager.LocalClient != null ? NetworkManager.LocalClient.PlayerObject : null;
        if (own != null) return own.GetComponent<PlayerLocomotion>();
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
            if (player != null) return player;
        return null;
    }

    private void HandleHit(Health target, HitboxZoneType zone, float damage, float distance)
    {
        if (target == null || !byHealth.TryGetValue(target, out CourseTarget t)) return;
        if (running.Value && t.IsHostile) hostileImpacts++;
        // Une touche suffit : on ne laisse qu'un souffle de vie, que les dégâts du tir, appliqués juste
        // après (WeaponController.ServerFire), emportent. Le coup qui tue reste celui de l'arme.
        if (oneHit.Value && t.State == CourseTargetState.Up && target.Current > 0.01f)
            target.ApplyDamage(target.Current - 0.01f);
    }

    private void HandleShot(WeaponController weapon, int pellets)
    {
        if (!running.Value || weapon == null) return;
        PlayerLocomotion runner = Runner();
        if (runner != null && weapon.transform.root == runner.transform.root) pelletsFired += pellets;
    }

    /// <summary>
    /// Ce qui arrive au parcours : hors course, quitter la zone de départ pour la première section lance
    /// le chrono ; en course, revenir dans la zone de départ arrête le chrono si l'on vient de la dernière
    /// section, et abandonne la course sinon (un retour en arrière). Fonction pure, testée.
    /// </summary>
    public static CourseEvent Evaluate(bool running, bool wasInStart, bool inStart, bool inFirstSection, int lastSection, int finalSection)
    {
        if (!running) return wasInStart && inFirstSection ? CourseEvent.Start : CourseEvent.None;
        if (!inStart) return CourseEvent.None;
        return lastSection == finalSection ? CourseEvent.Finish : CourseEvent.Abort;
    }

    /// <summary>
    /// Le temps final, avec les règles de la Fosse : +2 s par hostile manqué, +2 s par civil touché,
    /// 0,05 s de moins par point de précision. Jamais négatif. Fonction pure, testée.
    /// </summary>
    public static float FinalTime(float rawTime, int missedHostiles, int civiliansHit, float accuracy)
    {
        float penalties = MissPenalty * Mathf.Max(0, missedHostiles) + CivilianPenalty * Mathf.Max(0, civiliansHit);
        float bonus = AccuracyBonusPerPercent * 100f * Mathf.Clamp01(accuracy);
        return Mathf.Max(0f, rawTime + penalties - bonus);
    }
}
