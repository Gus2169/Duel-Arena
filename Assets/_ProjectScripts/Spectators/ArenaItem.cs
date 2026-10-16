using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Ce qu'un spectateur peut tenir en main et lancer. L'ordre voyage par le réseau : ajouter à la fin.</summary>
public enum ArenaItemKind : byte
{
    None,
    /// <summary>Un caillou arraché à un bloc fissuré de la tribune (GDD § 9).</summary>
    Rock,
    /// <summary>Du matériel tombé sur la tribune : une arme (PowerUpCatalog).</summary>
    PowerUp,
}

/// <summary>
/// Un objet de la tribune ou de l'arène (2026-10-13) : un caillou, ou du matériel (une arme). Il est
/// posé (on peut le ramasser), en vol (lancé, lâché, tombé du ciel), ou brisé (un caillou qui a touché
/// quelque chose).
///
/// SIMULÉ PAR LE SERVEUR SEUL (CLAUDE.md : « le client demande un lancer, le serveur le borne et le
/// simule »). Le vol est une suite de paraboles : le serveur publie le tronçon en cours (départ,
/// vitesse, instant sur son horloge), et chaque machine en calcule la position pour l'instant qu'elle
/// affiche — le même que celui des autres joueurs, donc on voit le caillou arriver sur un robot au
/// moment où on voit ce robot. Un rebond ou un impact publie un nouveau tronçon : deux messages par
/// lancer, au lieu d'une position à chaque image. Aucune physique de Unity : rien n'y diverge.
///
/// Ce qu'il touche, c'est le serveur qui le décide, contre la scène et les joueurs de son présent (pas
/// de compensation de latence pour un projectile lent : le geste est imparfait, GDD § 3) :
/// - un caillou lancé se brise sur ce qu'il touche ; sur un duelliste, il secoue sa visée ; sur un
///   spectateur, il l'étourdit (SpectatorController). Le bruit de l'impact est un son de catégorie B,
///   le leurre sonore du GDD (§ 8) ;
/// - le matériel, et un caillou lâché par un spectateur étourdi, rebondit sur les murs et se pose au
///   sol, où l'on peut le ramasser. Il traverse les robots : on le lance VERS un duelliste.
/// </summary>
[DisallowMultipleComponent]
public class ArenaItem : NetworkBehaviour
{
    public enum Phase : byte
    {
        Flying,
        Resting,
        Shattered,
    }

    /// <summary>Un tronçon de la trajectoire : parti de `position` à `velocity`, à l'instant `time` de
    /// l'horloge du serveur (la gravité s'ajoute), ou posé là, ou brisé là à cet instant.</summary>
    public struct Motion : INetworkSerializeByMemcpy
    {
        public double time;
        public Vector3 position;
        public Vector3 velocity;
        public Phase phase;
        /// <summary>Numéro du tronçon : chaque rebond, chaque impact en ajoute un.</summary>
        public byte segment;
    }

    /// <summary>Ce qu'est l'objet, fixé à sa création.</summary>
    public struct Info : INetworkSerializeByMemcpy
    {
        public ArenaItemKind kind;
        /// <summary>L'entrée du PowerUpCatalog, pour du matériel.</summary>
        public byte variant;
        /// <summary>Lâché par un spectateur étourdi : il tombe sans frapper personne.</summary>
        public bool harmless;
        /// <summary>Le client qui l'a lancé : il ne se touche pas lui-même, et le voit partir sans
        /// attendre (voir DisplayTime).</summary>
        public ulong thrower;
    }

    /// <summary>Pas de la simulation du serveur (s). À 24 m/s, 40 cm : moins que la largeur d'un corps.</summary>
    public const float SimStep = 1f / 60f;

    [SerializeField] private PowerUpCatalog catalog;
    [SerializeField] private SpectatorSounds sounds;
    [SerializeField] private AudioSource audioSource;

    [Header("Visuel")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Transform rockVisual;
    [SerializeField] private Transform weaponMount;
    [SerializeField] private Light beacon;
    [SerializeField] private ParticleSystem shatterFx;
    [Tooltip("Le matériel posé flotte au-dessus du sol en tournant sur lui-même : on le repère de loin (catégorie C).")]
    [SerializeField] private float restHover = 0.3f;
    [SerializeField] private float restSpinSpeed = 90f;

    [Header("Vol (simulé par le serveur, à régler en jouant)")]
    [Tooltip("Gravité des objets lancés (m/s²). Plus faible que celle des robots (-18) : un lancer porte jusqu'au centre de l'arène.")]
    [SerializeField] private float gravity = -14f;
    [SerializeField] private float rockRadius = 0.09f;
    [SerializeField] private float powerUpRadius = 0.16f;
    [Tooltip("Ce qu'un rebond sur un mur garde de la vitesse.")]
    [SerializeField, Range(0f, 1f)] private float restitution = 0.3f;
    [SerializeField] private int maxBounces = 4;
    [Tooltip("Au-delà (s), un objet qui vole encore disparaît (sorti du monde).")]
    [SerializeField] private float maxFlightTime = 8f;
    [Tooltip("Un caillou brisé reste ce temps (s) avant d'être détruit : chaque machine doit avoir montré son impact.")]
    [SerializeField] private float shatterLinger = 1.2f;
    [SerializeField] private float killY = -30f;
    [Tooltip("Ce qui arrête un objet : le décor (Default), comme pour les balles.")]
    [SerializeField] private LayerMask worldMask = 1;
    [Tooltip("La surface touchable des duellistes (layer Hitbox).")]
    [SerializeField] private LayerMask hitboxMask = 1 << 7;

    private readonly NetworkVariable<Info> info = new NetworkVariable<Info>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<Motion> motion = new NetworkVariable<Motion>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private static readonly List<ArenaItem> spawned = new List<ArenaItem>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => spawned.Clear();

    /// <summary>Les objets présents, posés, en vol ou brisés.</summary>
    public static IReadOnlyList<ArenaItem> Spawned => spawned;

    public ArenaItemKind Kind => info.Value.kind;
    public int Variant => info.Value.variant;
    public bool IsResting => motion.Value.phase == Phase.Resting;
    public Phase CurrentPhase => motion.Value.phase;
    public ulong Thrower => info.Value.thrower;

    /// <summary>Où l'objet est posé (le centre de sa sphère), tel que le serveur l'a publié.</summary>
    public Vector3 RestPosition => motion.Value.position;

    public PowerUpCatalog Catalog => catalog;

    /// <summary>Le nom à afficher (« caillou », « Sniper »).</summary>
    public string DisplayName => Kind == ArenaItemKind.Rock ? "le caillou" : (catalog != null ? catalog.NameOf(Variant) : "l'objet");

    // Côté serveur.
    private Info pendingInfo;
    private Motion pendingMotion;
    private double simTime;
    private int bounces;
    private bool consumed;
    private double despawnAt;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    // Chaque machine garde les tronçons reçus, pour afficher l'instant qu'elle veut.
    private readonly List<Motion> segments = new List<Motion>();
    private int shownSegment = -1;
    private GameObject weaponModel;

    /// <summary>Le serveur seul : l'objet est posé et personne ne l'a encore pris.</summary>
    public bool ServerIsPickable => IsServer && IsSpawned && !consumed && IsResting;

    // ------------------------------------------------------------------
    // Création (serveur)
    // ------------------------------------------------------------------

    /// <summary>Crée un objet lancé (ou lâché, ou tombé du ciel), parti de `position` à `velocity`
    /// maintenant. Serveur uniquement.</summary>
    public static ArenaItem ServerSpawn(ArenaItem prefab, ArenaItemKind kind, int variant, Vector3 position, Vector3 velocity,
                                        ulong thrower, bool harmless)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (prefab == null || nm == null || !nm.IsServer || kind == ArenaItemKind.None) return null;

        ArenaItem item = Instantiate(prefab, position, Quaternion.identity);
        item.pendingInfo = new Info { kind = kind, variant = (byte)Mathf.Clamp(variant, 0, 255), harmless = harmless, thrower = thrower };
        item.pendingMotion = new Motion { time = nm.ServerTime.Time, position = position, velocity = velocity, phase = Phase.Flying };
        item.NetworkObject.Spawn(true);
        return item;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Écrites ici plutôt qu'avant Spawn() : Netcode n'aime pas qu'on écrive une variable
            // réseau d'un objet qui n'existe pas encore pour lui.
            info.Value = pendingInfo;
            motion.Value = pendingMotion;
            simTime = pendingMotion.time;
        }

        segments.Clear();
        segments.Add(motion.Value);
        shownSegment = -1;
        motion.OnValueChanged += HandleMotionChanged;
        if (!spawned.Contains(this)) spawned.Add(this);
        BuildVisual();
        ShowAt(DisplayTime());
    }

    public override void OnNetworkDespawn()
    {
        motion.OnValueChanged -= HandleMotionChanged;
        spawned.Remove(this);
    }

    private void HandleMotionChanged(Motion previous, Motion current)
    {
        // Les tronçons arrivent dans l'ordre ; un tronçon déjà connu (même numéro) remplace le dernier.
        if (segments.Count > 0 && segments[segments.Count - 1].segment == current.segment) segments[segments.Count - 1] = current;
        else segments.Add(current);
        if (segments.Count > 16) segments.RemoveAt(0);
    }

    // ------------------------------------------------------------------
    // La trajectoire (fonctions pures, testées)
    // ------------------------------------------------------------------

    /// <summary>Position sur un tronçon à l'instant `time` : la parabole en vol, le point fixe sinon.</summary>
    public static Vector3 Evaluate(Motion m, double time, float gravity)
    {
        if (m.phase != Phase.Flying) return m.position;
        float dt = (float)System.Math.Max(0.0, time - m.time);
        return m.position + m.velocity * dt + Vector3.up * (0.5f * gravity * dt * dt);
    }

    /// <summary>Vitesse sur un tronçon à l'instant `time`.</summary>
    public static Vector3 VelocityAt(Motion m, double time, float gravity)
    {
        if (m.phase != Phase.Flying) return Vector3.zero;
        float dt = (float)System.Math.Max(0.0, time - m.time);
        return m.velocity + Vector3.up * (gravity * dt);
    }

    /// <summary>Le tronçon en vigueur à l'instant `time` : le dernier parti à cet instant ou avant (le
    /// premier si l'instant le précède).</summary>
    public static int SegmentAt(IReadOnlyList<Motion> list, double time)
    {
        int index = 0;
        for (int i = 1; i < list.Count; i++)
        {
            if (list[i].time <= time) index = i;
            else break;
        }
        return index;
    }

    /// <summary>
    /// Après un choc contre le décor, le tronçon suivant d'un objet qui ne se brise pas : posé si la
    /// surface est un sol (ou après trop de rebonds), sinon renvoyé par le mur en perdant de sa vitesse.
    /// Fonction pure, testée.
    /// </summary>
    public static Motion AfterImpact(Vector3 center, Vector3 normal, Vector3 velocity, double time, float restitution,
                                     int bouncesSoFar, int maxBounces, byte segment)
    {
        var next = new Motion { time = time, segment = segment };
        if (IsFloor(normal))
        {
            next.position = center;
            next.phase = Phase.Resting;
            return next;
        }

        next.phase = Phase.Flying;
        next.position = center + normal * 0.01f;
        if (bouncesSoFar >= maxBounces)
        {
            // Il glisse le long du mur jusqu'au sol, sans plus rebondir.
            next.velocity = new Vector3(0f, Mathf.Min(0f, velocity.y), 0f);
            return next;
        }
        Vector3 reflected = Vector3.Reflect(velocity, normal) * restitution;
        // Jamais vers le mur qu'il vient de toucher.
        float into = Vector3.Dot(reflected, normal);
        if (into < 0f) reflected -= normal * into;
        next.velocity = reflected;
        return next;
    }

    /// <summary>Une surface assez horizontale pour qu'un objet s'y pose.</summary>
    public static bool IsFloor(Vector3 normal) => normal.y >= 0.6f;

    // ------------------------------------------------------------------
    // La simulation (serveur)
    // ------------------------------------------------------------------

    private void Update()
    {
        if (!IsSpawned) return;
        if (IsServer)
        {
            if (despawnAt > 0.0 && Time.timeAsDouble >= despawnAt)
            {
                NetworkObject.Despawn(true);
                return;
            }
            ServerSimulate();
        }
    }

    private void LateUpdate()
    {
        if (!IsSpawned) return;
        // En LateUpdate : les joueurs ont fini de placer leurs adversaires pour cette image (l'instant
        // affiché, PlayerLocomotion.RemoteViewTime, est à jour).
        ShowAt(DisplayTime());
    }

    private struct ItemHit
    {
        public float fraction;
        public Vector3 point;
        public Vector3 normal;
        public PlayerLocomotion duelist;
        public SpectatorController spectator;
    }

    private float RadiusOf(ArenaItemKind kind) => kind == ArenaItemKind.Rock ? rockRadius : powerUpRadius;

    /// <summary>Un caillou lancé se brise sur ce qu'il touche ; le reste rebondit et se pose.</summary>
    private static bool Breaks(Info i) => i.kind == ArenaItemKind.Rock && !i.harmless;

    private void ServerSimulate()
    {
        Motion m = motion.Value;
        if (m.phase != Phase.Flying || consumed) return;

        double now = NetworkManager.ServerTime.Time;
        Info i = info.Value;
        for (int guard = 0; guard < 16 && simTime < now; guard++)
        {
            if (!Advance(m, i, ref simTime, now, out ItemHit hit, out Vector3 center))
            {
                if (Evaluate(m, simTime, gravity).y < killY || simTime - m.time > maxFlightTime)
                {
                    consumed = true;
                    NetworkObject.Despawn(true);
                }
                return;
            }

            ServerImpact(hit, center, simTime, VelocityAt(m, simTime, gravity));
            m = motion.Value;
            if (m.phase != Phase.Flying || consumed) return;
        }
    }

    /// <summary>
    /// Avance un tronçon en vol de `time` jusqu'à `until`, par pas de SimStep, et s'arrête au premier
    /// obstacle : `time` vaut alors l'instant du choc, `center` la position de l'objet à cet instant.
    /// La même pour le serveur et pour les tests.
    /// </summary>
    private bool Advance(Motion m, Info i, ref double time, double until, out ItemHit hit, out Vector3 center)
    {
        float radius = RadiusOf(i.kind);
        hit = default;
        center = default;
        for (int guard = 0; time < until && guard < 4096; guard++)
        {
            double next = System.Math.Min(until, time + SimStep);
            Vector3 a = Evaluate(m, time, gravity);
            Vector3 b = Evaluate(m, next, gravity);
            if (FindHit(a, b, radius, i, out hit))
            {
                time += (next - time) * hit.fraction;
                center = Vector3.Lerp(a, b, hit.fraction);
                return true;
            }
            time = next;
        }
        return false;
    }

    /// <summary>Le premier obstacle sur le trajet de `a` à `b` : le décor, la surface touchable d'un
    /// duelliste en vie, le corps d'un spectateur. Seul un caillou lancé touche les robots.</summary>
    private bool FindHit(Vector3 a, Vector3 b, float radius, Info itemInfo, out ItemHit best)
    {
        best = new ItemHit { fraction = 2f };
        Vector3 delta = b - a;
        float length = delta.magnitude;
        if (length < 1e-6f) return false;
        Vector3 direction = delta / length;

        if (Physics.SphereCast(a, radius, direction, out RaycastHit world, length, worldMask, QueryTriggerInteraction.Ignore))
        {
            best = new ItemHit { fraction = world.distance / length, point = world.point, normal = world.normal };
        }

        if (!Breaks(itemInfo)) return best.fraction <= 1f;

        int count = Physics.SphereCastNonAlloc(a, radius, direction, hitBuffer, length, hitboxMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (h.collider == null || !h.collider.enabled) continue;
            PlayerLocomotion player = h.collider.GetComponentInParent<PlayerLocomotion>();
            if (player == null || player.IsSpectator || !player.IsAlive) continue;
            // Un chevauchement dès le départ est rendu à distance nulle, sans point.
            float fraction = h.distance <= 0f ? 0f : h.distance / length;
            if (fraction >= best.fraction) continue;
            best = new ItemHit
            {
                fraction = fraction,
                point = h.distance <= 0f ? a : h.point,
                normal = -direction,
                duelist = player,
            };
        }

        foreach (SpectatorController other in SpectatorController.Spawned)
        {
            if (other == null || !other.IsSpectatorRole || other.OwnerClientId == itemInfo.thrower) continue;
            Vector3 feet = other.transform.position;
            if (!SpectatorRules.SweepHitsBody(a, b, radius, feet, other.BodyHeight, SpectatorRules.BodyRadius, out float fraction)) continue;
            if (fraction >= best.fraction) continue;
            Vector3 center = Vector3.Lerp(a, b, fraction);
            best = new ItemHit { fraction = fraction, point = center, normal = -direction, spectator = other };
        }

        return best.fraction <= 1f;
    }

    private void ServerImpact(ItemHit hit, Vector3 center, double time, Vector3 velocity)
    {
        Motion current = motion.Value;
        if (Breaks(info.Value))
        {
            if (hit.duelist != null && hit.duelist.TryGetComponent(out SpectatorController duelist))
                duelist.ServerRockHitDuelist(velocity, hit.point);
            else if (hit.spectator != null)
                hit.spectator.ServerRockHitSpectator(velocity);
            ServerShatter(hit.point, time);
            return;
        }

        // Le matériel (et un caillou lâché) ne touche que le décor : il rebondit, ou se pose.
        if (hit.duelist != null || hit.spectator != null) return;
        bounces++;
        Motion next = AfterImpact(center, hit.normal, velocity, time, restitution, bounces, maxBounces, (byte)(current.segment + 1));
        motion.Value = next;
    }

    private void ServerShatter(Vector3 point, double time)
    {
        Motion current = motion.Value;
        motion.Value = new Motion { time = time, position = point, phase = Phase.Shattered, segment = (byte)(current.segment + 1) };
        consumed = true;
        despawnAt = Time.timeAsDouble + shatterLinger;
    }

    /// <summary>Serveur : quelqu'un vient de ramasser l'objet. Il disparaît.</summary>
    public void ServerConsume()
    {
        if (!IsServer || consumed) return;
        consumed = true;
        if (IsSpawned) NetworkObject.Despawn(true);
    }

    /// <summary>Serveur : retire l'objet sans qu'il serve (nouvelle manche, ménage).</summary>
    public void ServerRemove() => ServerConsume();

    // ------------------------------------------------------------------
    // L'affichage (toutes les machines, catégorie C)
    // ------------------------------------------------------------------

    /// <summary>
    /// L'instant que cette machine affiche pour cet objet : le présent sur le serveur ; chez un client,
    /// l'instant où il voit les autres joueurs (le caillou arrive sur un robot au moment où on voit ce
    /// robot) ; chez celui qui l'a lancé, son présent anticipé (NetworkManager.LocalTime), pour le voir
    /// partir de sa main à l'instant même où il l'a lâché.
    /// </summary>
    private double DisplayTime()
    {
        NetworkManager nm = NetworkManager;
        if (nm == null) return Time.timeAsDouble;
        if (IsServer) return nm.ServerTime.Time;
        if (info.Value.thrower == nm.LocalClientId && motion.Value.phase == Phase.Flying) return nm.LocalTime.Time;
        double remote = PlayerLocomotion.RemoteViewTime;
        return remote > 0.0 ? remote : nm.ServerTime.Time - 0.1;
    }

    private void ShowAt(double time)
    {
        if (segments.Count == 0) return;
        int index = SegmentAt(segments, time);
        Motion m = segments[index];
        bool started = time >= segments[0].time;

        if (index != shownSegment && started)
        {
            if (shownSegment >= 0 && index > shownSegment) PlaySegmentEvent(m);
            shownSegment = index;
        }

        transform.position = Evaluate(m, time, gravity);
        bool visible = started && m.phase != Phase.Shattered;
        if (visualRoot != null && visualRoot.gameObject.activeSelf != visible) visualRoot.gameObject.SetActive(visible);

        bool resting = m.phase == Phase.Resting;
        if (beacon != null) beacon.enabled = visible && resting && Kind == ArenaItemKind.PowerUp;
        if (visualRoot == null || !visible) return;

        float t = (float)(time % 3600.0);
        if (m.phase == Phase.Flying)
        {
            // Il tourne sur lui-même en vol : une rotation qui ne dépend que du temps de vol.
            float flight = (float)System.Math.Max(0.0, time - segments[0].time);
            visualRoot.localPosition = Vector3.zero;
            visualRoot.localRotation = Quaternion.Euler(flight * 540f, flight * 320f, 0f);
        }
        else if (Kind == ArenaItemKind.PowerUp)
        {
            visualRoot.localPosition = Vector3.up * (restHover + Mathf.Sin(t * 2f) * 0.05f);
            visualRoot.localRotation = Quaternion.Euler(0f, t * restSpinSpeed, 0f);
        }
        else
        {
            visualRoot.localPosition = Vector3.zero;
            visualRoot.localRotation = Quaternion.Euler(0f, (Variant * 37f + segments[0].segment * 11f) % 360f, 0f);
        }
    }

    private void PlaySegmentEvent(Motion m)
    {
        if (m.phase == Phase.Shattered)
        {
            if (sounds != null) sounds.rockImpact.PlayOn(audioSource);
            if (shatterFx != null)
            {
                shatterFx.transform.SetParent(null, true);
                shatterFx.transform.position = m.position;
                shatterFx.Play();
                Destroy(shatterFx.gameObject, 2f);
                shatterFx = null;
            }
            return;
        }
        if (sounds != null) sounds.itemLand.PlayOn(audioSource);
    }

    private void BuildVisual()
    {
        bool rock = Kind == ArenaItemKind.Rock;
        if (rockVisual != null) rockVisual.gameObject.SetActive(rock);
        if (rock || weaponMount == null || weaponModel != null) return;

        PowerUpCatalog.Entry entry = catalog != null ? catalog.Get(Variant) : null;
        if (beacon != null && entry != null) beacon.color = entry.beaconColor;
        if (entry == null || entry.weapon == null || entry.weapon.modelPrefab == null) return;

        weaponModel = Instantiate(entry.weapon.modelPrefab, weaponMount, false);
        weaponModel.name = entry.weapon.modelPrefab.name;
        MakeInert(weaponModel);
    }

    /// <summary>
    /// L'apparence d'un objet (`kind`, `variant`), sans collider ni script, sous `parent` : ce qu'un
    /// spectateur tient en main, sur son corps et dans sa vue subjective. Appelée sur le préfab, qu'elle
    /// ne modifie pas. Null si l'objet n'a pas d'apparence.
    /// </summary>
    public GameObject CreateVisual(ArenaItemKind kind, int variant, Transform parent)
    {
        GameObject visual;
        if (kind == ArenaItemKind.Rock)
        {
            if (rockVisual == null) return null;
            visual = Instantiate(rockVisual.gameObject, parent, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
        }
        else
        {
            PowerUpCatalog.Entry entry = catalog != null ? catalog.Get(variant) : null;
            if (entry == null || entry.weapon == null || entry.weapon.modelPrefab == null) return null;
            visual = Instantiate(entry.weapon.modelPrefab, parent, false);
        }
        visual.name = kind == ArenaItemKind.Rock ? "Caillou en main" : "Matériel en main";
        visual.SetActive(true);
        MakeInert(visual);
        return visual;
    }

    /// <summary>Un modèle d'arme posé dans le monde n'est qu'une apparence : ni collider (il ferait
    /// reculer un robot ou arrêterait une balle, voir les pièges de CLAUDE.md), ni script.</summary>
    public static void MakeInert(GameObject model)
    {
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true))
        {
            c.enabled = false;
            Destroy(c);
        }
        foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        foreach (Camera cam in model.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    // ------------------------------------------------------------------
    // Surface de test : la simulation du serveur, sans réseau.
    // ------------------------------------------------------------------

    /// <summary>Lance la simulation hors session, à partir de `start` à `velocity` à l'instant 0.</summary>
    public void TestBegin(ArenaItemKind kind, Vector3 start, Vector3 velocity, bool harmless = false)
    {
        testInfo = new Info { kind = kind, harmless = harmless, thrower = ulong.MaxValue };
        testMotion = new Motion { time = 0.0, position = start, velocity = velocity, phase = Phase.Flying };
        testTime = 0.0;
        bounces = 0;
        TestHitDuelist = null;
        testSegments.Clear();
        testSegments.Add(testMotion);
    }

    private Info testInfo;
    private Motion testMotion;
    private double testTime;
    private readonly List<Motion> testSegments = new List<Motion>();

    /// <summary>Les tronçons produits par la simulation de test.</summary>
    public IReadOnlyList<Motion> TestSegments => testSegments;
    public Motion TestMotion => testMotion;

    /// <summary>Le duelliste qu'a touché le caillou pendant la simulation de test (null : rien, ou le décor).</summary>
    public PlayerLocomotion TestHitDuelist { get; private set; }

    /// <summary>Avance la simulation de test jusqu'à `until` (s), par le même chemin que le serveur
    /// (Advance, FindHit, AfterImpact), sans les effets sur les joueurs ni le réseau.</summary>
    public void TestSimulate(double until)
    {
        for (int guard = 0; guard < 64 && testMotion.phase == Phase.Flying && testTime < until; guard++)
        {
            if (!Advance(testMotion, testInfo, ref testTime, until, out ItemHit hit, out Vector3 center)) return;
            Vector3 velocity = VelocityAt(testMotion, testTime, gravity);
            byte segment = (byte)(testMotion.segment + 1);
            if (Breaks(testInfo))
            {
                TestHitDuelist = hit.duelist;
                testMotion = new Motion { time = testTime, position = hit.point, phase = Phase.Shattered, segment = segment };
            }
            else
            {
                bounces++;
                testMotion = AfterImpact(center, hit.normal, velocity, testTime, restitution, bounces, maxBounces, segment);
            }
            testSegments.Add(testMotion);
        }
    }

    public float TestGravity => gravity;
#endif
}
