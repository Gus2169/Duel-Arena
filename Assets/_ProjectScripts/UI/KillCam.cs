using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// La kill cam (GDD § 6) : le coup mortel, EN DIRECT et au ralenti. Refaite le 2026-11-04, l'utilisateur : « ne doit pas
/// être un replay », « l'action à l'instant de la partie jouée en direct et au ralenti, avec la caméra qui suit la balle
/// comme dans Sniper Elite », fluide et dans la continuité du jeu. Ni noir, ni bandes, ni copies des robots : elle filme
/// la vraie partie. Quand une manche, ou le match (dans un mode à réapparitions, le coup qui gagne), se finit sur une
/// élimination, tout ralentit à l'instant du tir, la caméra quitte les yeux du tireur et suit la balle jusqu'à la cible,
/// le robot vole en pièces (RobotCorpse.Burst), la caméra tourne autour de l'épave pendant que le temps revient à la
/// normale, puis elle rentre au joueur ; le gagnant garde ensuite la main, le temps de narguer.
///
/// Comme dans Sniper Elite, où le jeu connaît le résultat du tir dès la pression sur la détente : ici le tir est instantané
/// et jugé par le serveur à son arrivée, donc le serveur sait aussitôt que ce coup tue et finit la manche. Il le juge sur
/// place (RoundManager.ServerJudgeKill) et le dit dans le message de la mort (PlayerDeath.ServerNotifyKilled). La
/// simulation, elle, ne ralentit jamais : le robot est mort sur le serveur et les joueurs sont gelés le temps de la kill
/// cam (Freezes), comme en fin de manche. Ce qui ralentit n'est que ce que CHAQUE machine montre, sans jamais toucher à
/// Time.timeScale (l'hôte simule les autres, et d'autres matchs au Tournoi) :
/// - la mort se voit à l'impact : la victime reste debout à l'écran (PlayerDeath retient sa mort) jusqu'à ce que la balle
///   arrive, et son épave naît alors dans sa pose du moment ;
/// - les deux robots ralentissent (RateOf, lu par PlayerAnimator : l'Animator et ses lissages) ;
/// - la physique avance à la main, au ralenti (Physics.Simulate), et les gerbes de l'épave aussi.
///
/// Deux déroulés (TimelineFor, sur ce que publie le serveur : la même durée partout) :
/// - la balle, pour un tir de ShortShot et plus : du canon à la cible, le monde presque à l'arrêt, d'autant plus longtemps
///   que le tir est long (FlightTime) ;
/// - de près, pour le gant et un tir à bout portant : les deux robots de côté, le temps d'un instant.
/// Puis l'impact, de côté, au plus lent ; puis l'épave ; puis le retour au joueur. Le décor s'assombrit autour des robots,
/// presque noir à l'impact (DimAt : le global _ReplayDim, que lisent les shaders du décor le temps du rendu de cette caméra
/// seulement). Catégorie C : le serveur ne décide que QUAND et la légende.
/// </summary>
[DefaultExecutionOrder(1000)]
public class KillCam : MonoBehaviour
{
    // ------------------------------------------------------------------
    // Le déroulé (temps réel, en secondes depuis le début de la kill cam)
    // ------------------------------------------------------------------

    /// <summary>Une élimination finit-elle la manche ? Seulement si elle date de moins que ça (s, horloge du serveur) :
    /// la mort se voit et la manche se finit dans la même image ; une zone tenue ou un module ramassé plus tard, non.</summary>
    public const float FreshKill = 0.25f;
    /// <summary>Une kill cam pas encore commencée à cet instant ne commence plus (une machine arrivée en retard).</summary>
    public const float LateStart = 0.6f;
    /// <summary>En deçà de cette distance d'un robot à l'autre (m), pas de balle suivie (elle partirait du bras du
    /// tireur) : le coup se voit de côté, comme un coup de gant.</summary>
    public const float ShortShot = 4f;
    /// <summary>Le tir diffusé est attendu ce temps (s) : la kill cam s'annonce à peu près avec lui.</summary>
    public const float ShotWait = 0.15f;
    /// <summary>Le coup mortel est le dernier tir montré du tireur depuis ce temps (s) : chez lui, il date d'un
    /// aller-retour.</summary>
    public const float ShotMemory = 1f;

    /// <summary>Le vol de la balle (s) : FlightBase plus FlightPerMeter par mètre, de FlightMin à FlightMax (d'un poste à
    /// l'autre, 56 m : 2,2 s).</summary>
    public const float FlightMin = 0.9f;
    public const float FlightMax = 2.4f;
    public const float FlightBase = 0.55f;
    public const float FlightPerMeter = 0.03f;
    /// <summary>De près : le temps entre le coup et l'impact (s).</summary>
    public const float CloseLead = 0.4f;
    /// <summary>L'impact, au plus lent (s).</summary>
    public const float ImpactReal = 0.75f;
    /// <summary>L'épave, le temps revenant à la normale (s).</summary>
    public const float AftermathReal = 1.4f;
    /// <summary>La caméra rentre au joueur (s).</summary>
    public const float ReturnTime = 0.5f;
    /// <summary>La caméra quitte les yeux du tireur (s).</summary>
    public const float TakeOffTime = 0.3f;

    /// <summary>Le monde passe du temps réel à son plus lent en ce temps (s) : il ne s'arrête pas d'un coup.</summary>
    public const float SlowDownTime = 0.12f;
    /// <summary>La vitesse du monde (1 : le temps réel) pendant le vol de la balle, de près, à l'impact.</summary>
    public const float FlightRate = 0.04f;
    public const float CloseRate = 0.15f;
    public const float ImpactRate = 0.13f;

    /// <summary>La caméra sur la balle s'arrête à cette distance de l'impact (m), et regarde la balle arriver.</summary>
    public const float BulletCamStop = 1.3f;
    /// <summary>La caméra suit la balle à cette distance (m) ; le rayon d'une arme laser, qui se voit en entier, de plus
    /// loin.</summary>
    public const float BulletCamBehind = 0.8f;
    public const float BeamCamBehind = 1.1f;
    /// <summary>La caméra part par-dessus l'épaule du tireur, à cette distance derrière la bouche (m), et rejoint la course de
    /// la balle sur LaunchDistance (m).</summary>
    public const float LaunchBehind = 1.6f;
    public const float LaunchDistance = 2.5f;
    /// <summary>La place qu'il faut à la caméra de côté (m) : sans elle, de l'autre côté, sinon le long du coup, dégagé.</summary>
    public const float SideRoom = 2.4f;
    /// <summary>La traînée de la balle et son sillage, une lueur qui s'efface derrière elle (m) : plus courts que l'écart
    /// de la caméra. Plus longs, ils passaient derrière l'objectif et barraient l'image (la traînée, une barre jaune ; le
    /// sillage de 3 m, une bande fauve ; vus à deux instances le 2026-11-03).</summary>
    public const float StreakLength = 0.22f;
    public const float WakeLength = 0.55f;
    /// <summary>L'arrivée de la balle reste sur le robot que cette machine montre, à cette distance au plus de son axe (m) :
    /// le serveur a jugé le tir sur la pose que voyait le tireur, et ce robot a pu continuer un instant.</summary>
    public const float BodyRadius = 0.35f;

    /// <summary>Le décor assombri (0 à 1, DimAt) : au bout du vol, à l'impact (atteint en DimRise), et à la fin de
    /// l'épave.</summary>
    public const float DimFlight = 0.45f;
    public const float DimImpact = 0.9f;
    public const float DimAftermath = 0.6f;
    public const float DimRise = 0.15f;

    /// <summary>La hauteur des sons de l'impact : le ralenti.</summary>
    public const float SlowPitch = 0.6f;

    private const float BulletRoll = 7f;            // degrés : la caméra s'incline pendant le vol, portée par la balle
    private const float BulletEaseDepth = 0.6f;     // la balle : lente en sortant du canon et en arrivant (BulletTravel)
    private const float FlybyPitch = 0.5f;
    private const float CameraSmoothing = 0.09f;    // s, deux fois de suite : de la balle à l'impact et à l'épave, sans coupe
    private const float WreckSmoothing = 0.2f;      // s : autour de l'épave, sans suivre chaque pièce qui part
    private const float OwnBodyDistance = 1.2f;     // m : plus près de ses yeux, le joueur ne voit pas son propre corps
    private const float MaxPhysicsStep = 1f / 30f;
    private const float BulletWidth = 0.008f;
    private const float WakeWidth = 0.012f;
    private const float BeamWidth = 0.012f;
    private const float BeamLinger = 0.25f;         // s : le rayon d'une arme laser reste un instant après l'impact
    private const float FlashHold = 0.45f;          // s : la flamme de bouche, au ralenti
    private const int WorldMask = 1;                // Default : le décor qui pourrait se mettre entre la caméra et les robots
    private const int MaxStreaks = 16;
    private static readonly Color BulletColor = new Color(1f, 0.86f, 0.55f);
    private static readonly Color WakeColor = new Color(0.42f, 0.34f, 0.22f);
    private static readonly int ReplayDimId = Shader.PropertyToID("_ReplayDim");

    // ------------------------------------------------------------------
    // Ce que publie le serveur
    // ------------------------------------------------------------------

    /// <summary>La kill cam d'une fin de manche (RoundManager, variable réseau) : quand elle commence (horloge du serveur ;
    /// 0 : aucune), qui a tiré, qui est tombé, comment (le gant, la tête ou ailleurs), avec quelle arme (sa place) et de
    /// combien (décimètres, d'un robot à l'autre).</summary>
    public struct Info : INetworkSerializeByMemcpy, System.IEquatable<Info>
    {
        public double startsAt;
        public ulong killer;
        public ulong victim;
        public Health.DamageCause cause;
        public byte weaponSlot;
        public ushort distanceDm;

        public bool Scheduled => startsAt > 0d;
        public float Distance => distanceDm * 0.1f;

        public bool Equals(Info other) => startsAt.Equals(other.startsAt) && killer == other.killer && victim == other.victim
                                          && cause == other.cause && weaponSlot == other.weaponSlot && distanceDm == other.distanceDm;
    }

    /// <summary>Une distance (m) en décimètres pour Info, bornée.</summary>
    public static ushort Decimeters(float meters)
        => float.IsFinite(meters) ? (ushort)Mathf.RoundToInt(Mathf.Clamp(meters * 10f, 0f, ushort.MaxValue)) : (ushort)0;

    // ------------------------------------------------------------------
    // Le déroulé (fonctions pures, testées)
    // ------------------------------------------------------------------

    /// <summary>La balle suivie, ou le coup de près.</summary>
    public enum Style : byte { Bullet, Close }

    /// <summary>Le déroulé d'une kill cam, en temps réel depuis son début (le coup part à 0) : le coup arrive (`impact`),
    /// l'impact au plus lent se finit (`impactEnd`), le temps est revenu à la normale (`settled`), la caméra est rentrée
    /// (`duration`).</summary>
    public struct Timeline
    {
        public Style style;
        public float impact;
        public float impactEnd;
        public float settled;
        public float duration;
    }

    /// <summary>La balle suivie pour un tir de ShortShot et plus ; de près pour le gant et un tir à bout portant.</summary>
    public static Style StyleOf(Info info)
        => info.cause != Health.DamageCause.Glove && info.Distance >= ShortShot ? Style.Bullet : Style.Close;

    /// <summary>Le vol de la balle (s) pour un tir de `distance` mètres.</summary>
    public static float FlightTime(float distance)
        => Mathf.Clamp(FlightBase + FlightPerMeter * (float.IsFinite(distance) ? Mathf.Max(0f, distance) : 0f), FlightMin, FlightMax);

    /// <summary>Le déroulé de la kill cam de `info`.</summary>
    public static Timeline TimelineFor(Info info)
    {
        bool bullet = StyleOf(info) == Style.Bullet;
        var line = new Timeline
        {
            style = bullet ? Style.Bullet : Style.Close,
            impact = bullet ? FlightTime(info.Distance) : CloseLead,
        };
        line.impactEnd = line.impact + ImpactReal;
        line.settled = line.impactEnd + AftermathReal;
        line.duration = line.settled + ReturnTime;
        return line;
    }

    /// <summary>La durée de la kill cam de `info` (s).</summary>
    public static float DurationOf(Info info) => TimelineFor(info).duration;

    /// <summary>Ce que la kill cam de `info` ajoute à la fin de manche (s).</summary>
    public static float PhaseExtension(Info info) => DurationOf(info);

    /// <summary>L'élimination vue à `killAt` finit-elle la manche à `now` ? (FreshKill)</summary>
    public static bool IsFresh(double killAt, double now) => killAt > 0d && now >= killAt && now - killAt <= FreshKill;

    /// <summary>Cette kill cam est-elle celle de la phase commencée à `phaseStartedAt` ? (une d'avant n'est plus rien)</summary>
    public static bool BelongsTo(Info info, double phaseStartedAt) => info.Scheduled && info.startsAt >= phaseStartedAt;

    /// <summary>Les joueurs sont-ils gelés ? Le temps de la kill cam : rien ne bouge ni ne tire sous elle (ni pas, ni coups
    /// de feu entendus pendant qu'on la regarde). Après (le gagnant qui nargue), si.</summary>
    public static bool Freezes(Info info, double phaseStartedAt, double now)
        => BelongsTo(info, phaseStartedAt) && now >= info.startsAt && now < info.startsAt + DurationOf(info);

    /// <summary>L'écran de fin vient après la kill cam quand il y en a une, puis le temps du gagnant. Au Tournoi, aucun : le
    /// champion a gardé la main dans la finale, son écran vient tout de suite.</summary>
    public static float EndScreenDelay(bool tournament, float roundOverDuration, Info killCam)
        => tournament ? 0f : roundOverDuration + (killCam.Scheduled ? PhaseExtension(killCam) : 0f);

    /// <summary>
    /// La vitesse du monde montré à l'instant `t` (1 : le temps réel) : il ralentit en SlowDownTime à partir du coup, reste
    /// presque à l'arrêt pendant le vol de la balle, au plus lent à l'impact, puis revient au temps réel pendant que l'épave
    /// tombe. Elle règle les robots, la physique et les gerbes.
    /// </summary>
    public static float WorldRate(float t, Timeline line)
    {
        if (t < 0f || t >= line.settled) return 1f;
        if (t < line.impact)
            return Mathf.Lerp(1f, line.style == Style.Bullet ? FlightRate : CloseRate, Mathf.Clamp01(t / SlowDownTime));
        if (t < line.impactEnd) return ImpactRate;
        return Mathf.Lerp(ImpactRate, 1f, (t - line.impactEnd) / (line.settled - line.impactEnd));
    }

    /// <summary>Où en est le vol de la balle (0 à 1, en temps) à l'instant `t` ; de près, elle est partie d'un coup.</summary>
    public static float FlightProgress(float t, Timeline line)
        => line.style == Style.Bullet ? Mathf.Clamp01(t / line.impact) : 1f;

    /// <summary>La part du chemin faite par la balle à la part `u` de son vol : lente en sortant du canon et en arrivant
    /// (40 % de sa vitesse moyenne), vite entre les deux. Monotone, de 0 à 1.</summary>
    public static float BulletTravel(float u)
    {
        u = Mathf.Clamp01(u);
        return u - BulletEaseDepth * Mathf.Sin(2f * Mathf.PI * u) / (2f * Mathf.PI);
    }

    /// <summary>Le décor assombri (0 à 1) à l'instant `t` : il part de la partie telle quelle, s'assombrit pendant le vol,
    /// presque noir à l'impact (atteint en DimRise), un peu moins pendant que l'épave tombe, et rend la partie telle quelle
    /// quand la caméra rentre : aucune coupure.</summary>
    public static float DimAt(float t, Timeline line)
    {
        if (t < 0f || t >= line.duration) return 0f;
        if (t < line.impact) return Mathf.Lerp(0f, DimFlight, t / line.impact);
        if (t < line.impact + DimRise) return Mathf.Lerp(DimFlight, DimImpact, (t - line.impact) / DimRise);
        if (t < line.impactEnd) return DimImpact;
        if (t < line.settled) return Mathf.Lerp(DimImpact, DimAftermath, (t - line.impactEnd) / (line.settled - line.impactEnd));
        return Mathf.Lerp(DimAftermath, 0f, (t - line.settled) / (line.duration - line.settled));
    }

    /// <summary>
    /// La légende : « FAUCON · FP-12 · À LA TÊTE · 32 M », « FAUCON · FA-07 · 18 M », « FAUCON · COUP DE GANT ». La distance
    /// d'un robot à l'autre, au mètre près (1 m au moins).
    /// </summary>
    public static string Caption(string killer, string weapon, Health.DamageCause cause, float distance)
    {
        string name = string.IsNullOrEmpty(killer) ? "?" : killer.ToUpperInvariant();
        if (cause == Health.DamageCause.Glove) return name + " · COUP DE GANT";
        string line = name;
        if (!string.IsNullOrEmpty(weapon)) line += " · " + weapon.ToUpperInvariant();
        if (cause == Health.DamageCause.HeadShot) line += " · À LA TÊTE";
        int meters = float.IsFinite(distance) ? Mathf.Max(1, Mathf.RoundToInt(distance)) : 1;
        return line + " · " + meters + " M";
    }

    /// <summary>Le point d'arrivée `end` ramené sur le robot montré (de ses pieds `feet` à sa tête `head`) s'il en est à plus
    /// de BodyRadius ; sinon `end` tel quel.</summary>
    public static Vector3 OnBody(Vector3 end, Vector3 head, Vector3 feet)
    {
        Vector3 axis = head - feet;
        float squared = axis.sqrMagnitude;
        float along = squared > 1e-6f ? Mathf.Clamp01(Vector3.Dot(end - feet, axis) / squared) : 0f;
        Vector3 nearest = feet + axis * along;
        Vector3 away = end - nearest;
        float distance = away.magnitude;
        return distance <= BodyRadius ? end : nearest + away * (BodyRadius / distance);
    }

    // ------------------------------------------------------------------
    // Les plans (fonctions pures, testées)
    // ------------------------------------------------------------------

    /// <summary>Un plan : où est la caméra, où elle regarde, son champ vertical (degrés).</summary>
    public struct Shot
    {
        public Vector3 position;
        public Quaternion rotation;
        public float fieldOfView;
    }

    /// <summary>D'où filmer le coup : à droite de sa course, à gauche, ou derrière, le long de sa course.</summary>
    public enum Side : byte { Right, Left, Back }

    /// <summary>Le côté où la caméra a sa place (`right`, `left` : ce qu'elle y trouve, en mètres) : à droite, sinon à gauche,
    /// sinon le long du coup, qui est dégagé. Contre un mur, le coup restait filmé de tout près, ou du noir.</summary>
    public static Side BestSide(float right, float left, float needed)
        => right >= needed ? Side.Right : left >= needed ? Side.Left : Side.Back;

    // La droite de `along` (à plat), et le côté `side`.
    private static Vector3 RightOf(Vector3 along) => new Vector3(along.z, 0f, -along.x);
    private static Vector3 Across(Vector3 along, Side side) => side == Side.Left ? -RightOf(along) : RightOf(along);

    /// <summary>
    /// La balle, de la bouche `origin` à l'impact `end` (`travel` : la part du chemin faite, BulletTravel ; `bullet` : où
    /// elle est). La caméra part par-dessus l'épaule droite du tireur, LaunchBehind derrière la bouche, rejoint la course de
    /// la balle en LaunchDistance et la suit de près, un peu au-dessus et à droite (plus loin pour le rayon d'une arme laser,
    /// qui se voit en entier), en s'inclinant ; elle s'arrête BulletCamStop avant l'impact et regarde la balle arriver.
    /// </summary>
    public static Shot BulletShot(Vector3 origin, Vector3 end, float travel, bool beam, out Vector3 bullet)
    {
        Vector3 path = end - origin;
        float length = path.magnitude;
        Vector3 direction = length > 1e-4f ? path / length : Vector3.forward;
        float done = Mathf.Clamp01(travel);
        float travelled = length * done;
        bullet = origin + direction * travelled;
        Vector3 right = new Vector3(direction.z, 0f, -direction.x);
        right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
        Vector3 up = Vector3.Cross(direction, right);
        float joined = Mathf.SmoothStep(0f, 1f, travelled / LaunchDistance);
        float behind = Mathf.Lerp(LaunchBehind, beam ? BeamCamBehind : BulletCamBehind, joined);
        float along = Mathf.Min(travelled - behind, Mathf.Max(0f, length - BulletCamStop));
        Vector3 launch = right * 0.5f + up * 0.25f;
        Vector3 flight = beam ? right * 0.3f + up * 0.16f : right * 0.05f + up * 0.07f;
        Vector3 position = origin + direction * along + Vector3.Lerp(launch, flight, joined);
        Quaternion rotation = Look(end - position) * Quaternion.Euler(0f, 0f, BulletRoll * Mathf.Sin(Mathf.PI * done));
        return new Shot { position = position, rotation = rotation, fieldOfView = beam ? 50f : 40f };
    }

    /// <summary>Le coup de près (le gant, un tir à bout portant) : de côté (`shooter`, `victim` : leurs bustes), les deux
    /// robots dans le cadre de la tête aux pieds, ou derrière le tireur ; la caméra glisse un peu (`u` de 0 à 1).</summary>
    public static Shot SideShot(Vector3 shooter, Vector3 victim, float u, Side where = Side.Right)
    {
        Vector3 middle = (shooter + victim) * 0.5f;
        Vector3 between = victim - shooter;
        between.y = 0f;
        float span = between.magnitude;
        Vector3 along = span > 1e-4f ? between / span : Vector3.forward;
        float distance = Mathf.Max(3.2f, span * 0.75f + 2f);
        float slide = Mathf.Lerp(-0.35f, 0.35f, Mathf.Clamp01(u));
        Vector3 position = where == Side.Back
            ? shooter - along * (2.4f - slide) + RightOf(along) * 0.6f + Vector3.up * 0.3f
            : middle + Across(along, where) * distance - Vector3.up * 0.15f + along * slide;
        // Le milieu des robots, pas leurs bustes : les pieds restent dans le cadre.
        return new Shot { position = position, rotation = Look(middle - Vector3.up * 0.35f - position), fieldOfView = 45f };
    }

    /// <summary>L'impact, au plus lent : de côté (`direction` : le sens du coup), à 1,5 m du point touché puis 2,1 m, un peu
    /// en arrière (on voit le coup arriver) et regardant un peu devant (là où partent l'huile et les pièces), ou derrière lui
    /// sur sa course ; `u` de 0 à 1.</summary>
    public static Shot ImpactShot(Vector3 point, Vector3 direction, float u, Side where = Side.Right)
    {
        Vector3 flat = new Vector3(direction.x, 0f, direction.z);
        Vector3 along = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
        float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
        float away = Mathf.Lerp(1.5f, 2.1f, s);
        Vector3 offset = where == Side.Back ? -along * (away + 0.35f) + RightOf(along) * 0.4f : Across(along, where) * away - along * 0.35f;
        Vector3 position = point + offset + Vector3.up * Mathf.Lerp(0.05f, 0.2f, s);
        return new Shot { position = position, rotation = Look(point + along * 0.3f - position), fieldOfView = 45f };
    }

    /// <summary>L'épave : autour d'elle (`target`, son bassin), de 3,4 à 4,3 m et de 14 à 30° au-dessus (les pièces qui
    /// volent dans le cadre). Elle part du côté où filmait l'impact, à 75° de la ligne du tireur (`shooter`), et tourne
    /// jusqu'à 25° de cette ligne, par où elle rentrera (`u` de 0 à 1) ; le long du coup, elle y reste.</summary>
    public static Shot OrbitShot(Vector3 target, Vector3 shooter, float u, Side where = Side.Right)
    {
        Vector3 away = shooter - target;
        away.y = 0f;
        float baseYaw = away.sqrMagnitude > 1e-6f ? Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg : 0f;
        float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
        float yaw = baseYaw + (where == Side.Left ? 1f : -1f) * (where == Side.Back ? 25f : Mathf.Lerp(75f, 25f, s));
        float elevation = Mathf.Lerp(14f, 30f, s);
        float distance = Mathf.Lerp(3.4f, 4.3f, s);
        Vector3 position = target + Quaternion.Euler(-elevation, yaw, 0f) * Vector3.forward * distance;
        return new Shot { position = position, rotation = Look(target + Vector3.up * 0.1f - position), fieldOfView = 50f };
    }

    /// <summary>La caméra qui rentre au joueur : de `from` à `to`, en passant près de `via` (pour le tireur, le bout de la
    /// course de la balle, qui est dégagée : elle la remonte), lente au départ et à l'arrivée ; `u` de 0 à 1.</summary>
    public static Vector3 ReturnPosition(Vector3 from, Vector3 via, Vector3 to, float u)
    {
        float s = Mathf.SmoothStep(0f, 1f, u);
        float r = 1f - s;
        return r * r * from + 2f * r * s * via + s * s * to;
    }

    private static Quaternion Look(Vector3 direction)
        => direction.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;

    // ------------------------------------------------------------------
    // Ce que l'écran montre (lu par le HUD, les robots, le laser)
    // ------------------------------------------------------------------

    public static KillCam Instance { get; private set; }

    /// <summary>La kill cam a-t-elle l'écran ? Le HUD de la partie s'efface.</summary>
    public static bool Covers => Instance != null && Instance.playing;

    /// <summary>La légende se montre-t-elle ? De l'impact à la fin de l'épave : elle ne dit pas la fin du vol.</summary>
    public static bool CaptionShown => Covers && Instance.elapsed >= Instance.timeline.impact && Instance.elapsed < Instance.timeline.settled;

    /// <summary>La légende de la kill cam en cours (Caption).</summary>
    public static string CurrentCaption => Instance != null ? Instance.caption : "";

    /// <summary>La vitesse du temps montré pour le robot de `player` (1 : le temps réel) : seuls le tireur et sa victime
    /// ralentissent ; ceux de la tribune continuent de vivre.</summary>
    public static float RateOf(PlayerLocomotion player)
    {
        KillCam cam = Instance;
        if (cam == null || !cam.playing || player == null) return 1f;
        ulong id = player.OwnerClientId;
        return id == cam.current.killer || id == cam.current.victim ? cam.rate : 1f;
    }

    /// <summary>
    /// La caméra du joueur sous une caméra qui couvre tout l'écran (la kill cam, le plateau de l'écran de fin) : elle
    /// s'éteint, sinon chaque image dessinerait l'arène deux fois, ombres comprises, pour tout recouvrir. Son composant
    /// seul : son transform continue de bouger (la kill cam en part et y revient), son AudioListener reste l'oreille du
    /// joueur, et les bras, en Overlay de sa pile, ne se dessinent plus. Camera.main est alors la caméra qui couvre (elles
    /// portent l'étiquette MainCamera) : le laser, la zone et les arcs se règlent ainsi sur ce qui rend. Hold à chaque image,
    /// car la kill cam et le plateau peuvent se passer la main : le Release de l'un précède le Hold de l'autre.
    /// </summary>
    public sealed class CoveredCamera
    {
        /// <summary>La caméra du joueur de cette machine, ou null (pas de robot, ou il a disparu).</summary>
        public Camera Player { get; private set; }

        public void Follow(Camera player) => Player = player;

        public void Hold()
        {
            if (Player != null && Player.enabled) Player.enabled = false;
        }

        public void Release()
        {
            if (Player != null) Player.enabled = true;
            Player = null;
        }
    }

    /// <summary>La kill cam de cette partie : créée par le RoundManager de la scène à son apparition, dans sa scène (elle
    /// disparaît avec elle).</summary>
    public static KillCam Ensure(Scene scene)
    {
        if (Instance != null) return Instance;
        var go = new GameObject("Kill cam");
        if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(go, scene);
        return go.AddComponent<KillCam>();
    }

    // ------------------------------------------------------------------
    // L'état
    // ------------------------------------------------------------------

    /// <summary>Un tir montré par cette machine : son instant, qui, où arrive chaque plomb.</summary>
    private struct ShownShot
    {
        public double time;
        public ulong shooter;
        public Vector3[] ends;
    }

    /// <summary>Un robot de la kill cam, tel que cette machine le montre.</summary>
    private sealed class Robot
    {
        public Transform root;
        public Transform head;
        public Transform chest;
        public Transform weapon;
        public Transform muzzle;
    }

    private readonly List<ShownShot> shots = new List<ShownShot>();

    private bool playing;
    private float elapsed = float.NegativeInfinity;
    private float rate = 1f;
    private double begun;
    private Info current;
    private Timeline timeline;
    private Robot killer;
    private Robot victim;
    private bool localIsKiller;
    private bool hasShot;
    private bool bulletCam;
    private Side side;
    private bool beam;
    private Vector3 muzzleStart;
    private readonly List<Vector3> pelletEnds = new List<Vector3>();
    private readonly List<Vector3> shownEnds = new List<Vector3>();
    private int mainPellet;
    private Vector3 impactPoint;
    private Vector3 impactDirection;
    private readonly List<LineRenderer> streaks = new List<LineRenderer>();
    private LineRenderer wake;
    private Material streakMaterial;
    private bool released;
    private string caption = "";
    private bool flashCue;
    private bool flybyCue;
    private bool impactCue;
    private float dim;
    private WeaponData killerWeapon;
    private WeaponController killerController;
    private WeaponVisualFeedback killerFeedback;
    private PlayerDeath victimDeath;
    private Camera killCamera;
    private readonly CoveredCamera playerView = new CoveredCamera();
    private bool framed;
    private bool returning;
    private bool returnBlocked;
    private Vector3 returnFrom;
    private Quaternion returnRotation;
    private float returnFieldOfView;
    private Vector3 returnVia;
    private Vector3 stagePosition;
    private Quaternion stageRotation;
    private float stageFieldOfView;
    private Vector3 smoothedPosition;
    private Quaternion smoothedRotation;
    private float smoothedFieldOfView;
    private Vector3 smoothedTarget;
    private bool wreckFramed;

    // La physique avance à la main le temps de la kill cam.
    private bool physicsSlowed;
    private SimulationMode savedSimulation;

    private readonly List<Renderer> ownBody = new List<Renderer>();
    private bool ownBodyShown;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // Sous le noir du chargement : à l'instant du coup, tout ralentit, ce n'est pas le moment de perdre une image.
        BuildCamera();
        BuildStreaks();
    }

    private void OnEnable()
    {
        WeaponController.ShotShown += OnShotShown;
        RenderPipelineManager.beginCameraRendering += BeginCameraRender;
        RenderPipelineManager.endCameraRendering += EndCameraRender;
    }

    private void OnDisable()
    {
        WeaponController.ShotShown -= OnShotShown;
        RenderPipelineManager.beginCameraRendering -= BeginCameraRender;
        RenderPipelineManager.endCameraRendering -= EndCameraRender;
        Stop();
        Shader.SetGlobalFloat(ReplayDimId, 0f);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (streakMaterial != null) Destroy(streakMaterial);
    }

    // ------------------------------------------------------------------
    // Le fil
    // ------------------------------------------------------------------

    private void Update()
    {
        RoundManager rm = RoundManager.Local;
        bool spawned = rm != null && rm.IsSpawned;
        Info info = spawned ? rm.KillCamNow : default;
        elapsed = info.Scheduled ? rm.KillCamElapsed : float.NegativeInfinity;
        bool same = info.Scheduled && info.startsAt.Equals(current.startsAt);

        if (playing && (!same || elapsed >= timeline.duration)) Stop();

        // Elle part à l'instant publié. Chez un client, l'horloge du serveur estimée retarde un peu : la mort reste retenue
        // en attendant (PlayerDeath). Une machine trop en retard ne la joue pas, et montre la mort tout de suite.
        if (!playing && info.Scheduled && !info.startsAt.Equals(begun))
        {
            if (elapsed > LateStart)
            {
                begun = info.startsAt;
                ReleaseVictimOf(info);
            }
            else if (elapsed >= 0f && TryBegin(info))
            {
                begun = info.startsAt;
                playing = true;
            }
        }

        rate = playing ? WorldRate(elapsed, timeline) : 1f;
        if (playing) StepPhysics();

        // Les tirs, rangés par instant : ceux d'avant ne serviront plus.
        double now = Time.timeAsDouble;
        int old = 0;
        while (old < shots.Count && now - shots[old].time > ShotMemory) old++;
        if (old > 0) shots.RemoveRange(0, old);
    }

    private void LateUpdate()
    {
        // Après l'animation : la kill cam filme les robots tels qu'ils sont à cette image, et l'épave naît dans cette pose.
        if (playing) Play(elapsed);
    }

    private void OnShotShown(WeaponController weapon, Vector3 origin, IReadOnlyList<Vector3> ends)
    {
        if (playing || weapon == null || ends == null) return;
        var copy = new Vector3[Mathf.Min(ends.Count, MaxStreaks)];
        for (int i = 0; i < copy.Length; i++) copy[i] = ends[i];
        shots.Add(new ShownShot { time = Time.timeAsDouble, shooter = weapon.OwnerClientId, ends = copy });
    }

    // Le dernier tir montré de `shooter` : le coup mortel.
    private bool TryFindShot(ulong shooter, out ShownShot shot)
    {
        for (int i = shots.Count - 1; i >= 0; i--)
        {
            if (shots[i].shooter != shooter || shots[i].ends.Length == 0) continue;
            shot = shots[i];
            return true;
        }
        shot = default;
        return false;
    }

    // ------------------------------------------------------------------
    // Le début
    // ------------------------------------------------------------------

    private bool TryBegin(Info info)
    {
        PlayerMatchState killerState = PlayerMatchState.Of(info.killer);
        PlayerMatchState victimState = PlayerMatchState.Of(info.victim);
        Robot k = RobotOf(killerState);
        Robot v = RobotOf(victimState);
        if (k == null || v == null || k.root == v.root) return false;

        ShownShot shot = default;
        hasShot = info.cause != Health.DamageCause.Glove && TryFindShot(info.killer, out shot);
        if (info.cause != Health.DamageCause.Glove && !hasShot && elapsed < ShotWait) return false;

        current = info;
        timeline = TimelineFor(info);
        killer = k;
        victim = v;
        playerView.Follow(GameHud.LocalPlayerCamera);
        NetworkManager nm = NetworkManager.Singleton;
        localIsKiller = nm != null && nm.LocalClientId == info.killer;
        killerController = killerState.GetComponentInChildren<WeaponController>();
        killerWeapon = killerController != null ? killerController.DataOf(info.weaponSlot) : null;
        killerFeedback = killerState.GetComponent<WeaponVisualFeedback>();
        victimDeath = victimState.GetComponent<PlayerDeath>();
        caption = Caption(PlayerMatchState.NameOf(info.killer), killerWeapon != null ? killerWeapon.weaponName : null,
                          info.cause, info.Distance);
        beam = killerWeapon != null && killerWeapon.shotIsBeam;

        // Le coup : de la bouche de l'arme que montre cette machine, vers chaque point d'arrivée ; la caméra suit le plomb
        // qui a touché (le plus près de la tête, ou du buste, de la victime).
        pelletEnds.Clear();
        shownEnds.Clear();
        mainPellet = 0;
        bulletCam = false;
        Vector3 killerChest = PointOf(k.chest, k, 1.25f);
        Vector3 victimChest = PointOf(v.chest, v, 1.25f);
        if (hasShot)
        {
            muzzleStart = k.muzzle != null ? k.muzzle.position : killerChest;
            Transform aimed = info.cause == Health.DamageCause.HeadShot && v.head != null ? v.head : v.chest;
            Vector3 aim = aimed != null ? aimed.position : victimChest;
            float best = float.MaxValue;
            for (int i = 0; i < shot.ends.Length; i++)
            {
                shownEnds.Add(shot.ends[i]);
                pelletEnds.Add(shot.ends[i]);
                float d = (shot.ends[i] - aim).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                mainPellet = i;
            }
            KeepImpactOnBody();
            bulletCam = timeline.style == Style.Bullet && Vector3.Distance(muzzleStart, impactPoint) >= ShortShot;
        }
        else
        {
            impactPoint = victimChest;
            impactDirection = victimChest - killerChest;
        }
        PrepareStreaks(beam ? (killerWeapon != null ? killerWeapon.tracerColor : Color.red) : BulletColor);
        Vector3 flat = impactDirection;
        flat.y = 0f;
        flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
        side = BestSide(Room(impactPoint, RightOf(flat)), Room(impactPoint, -RightOf(flat)), SideRoom);

        SlowPhysics();
        flashCue = false;
        flybyCue = false;
        impactCue = false;
        released = false;
        framed = false;
        wreckFramed = false;
        returning = false;
#if UNITY_EDITOR
        Debug.Log($"[KillCam] {caption} : {timeline.duration:F2} s, {(bulletCam ? "la balle suivie" : "de près")}.");
#endif
        return true;
    }

    private static Robot RobotOf(PlayerMatchState state)
    {
        Transform model = state != null ? state.transform.Find("Model") : null;
        if (model == null) return null;
        var robot = new Robot { root = state.transform };
        Animator animator = model.GetComponent<Animator>();
        if (animator == null || !animator.isHuman) return robot;
        robot.head = animator.GetBoneTransform(HumanBodyBones.Head);
        robot.chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (robot.chest == null) robot.chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        WeaponModel held = HeldWeapons.Visible(animator.GetBoneTransform(HumanBodyBones.RightHand));
        if (held != null)
        {
            robot.weapon = held.transform;
            robot.muzzle = AttachmentMount.MuzzleOf(held);
        }
        return robot;
    }

    private static Vector3 PointOf(Transform bone, Robot robot, float height)
        => bone != null ? bone.position : robot.root.position + Vector3.up * height;

    // La balle arrive sur le robot que cette machine montre : le serveur a jugé le tir sur la pose que voyait le tireur, et
    // ce robot continue encore un instant, le temps de ses dernières poses.
    private void KeepImpactOnBody()
    {
        Vector3 end = OnBody(shownEnds[mainPellet], PointOf(victim.head, victim, 1.65f), victim.root.position);
        pelletEnds[mainPellet] = end;
        impactPoint = end;
        impactDirection = end - muzzleStart;
    }

    // ------------------------------------------------------------------
    // Le déroulement
    // ------------------------------------------------------------------

    private void Play(float t)
    {
        if (killer == null || victim == null || killer.root == null || victim.root == null)
        {
            Stop();
            return;
        }
        dim = DimAt(t, timeline);
        if (hasShot && t < timeline.impact) KeepImpactOnBody();
        if (!released && t >= timeline.impact)
        {
            // La balle arrive : la mort se voit enfin, le robot vole en pièces.
            released = true;
            if (victimDeath != null) victimDeath.ReleaseLiveKill();
        }
        PlayCues(t);
        RobotCorpse wreck = victimDeath != null ? victimDeath.Corpse : null;
        RobotBurst.SetSpeed(wreck, rate);
        UpdateStreaks(t);
        Frame(t, wreck);
        UpdateOwnBody();
        killCamera.enabled = true;
        playerView.Hold();
    }

    private void Frame(float t, RobotCorpse wreck)
    {
        if (t >= timeline.settled)
        {
            if (!returning) BeginReturn();
            Return((t - timeline.settled) / (timeline.duration - timeline.settled));
            return;
        }

        Shot shot;
        bool exact = false;     // la balle va vite : la caméra la suit sans retard
        if (t < timeline.impact)
        {
            if (bulletCam)
            {
                shot = BulletShot(muzzleStart, pelletEnds[mainPellet], BulletTravel(FlightProgress(t, timeline)), beam, out Vector3 bullet);
                shot.position = Unblock(bullet, shot.position);
                exact = true;
            }
            else shot = BothRobots(t / timeline.impact);
        }
        else if (t < timeline.impactEnd)
        {
            shot = ImpactShot(impactPoint, impactDirection, (t - timeline.impact) / (timeline.impactEnd - timeline.impact), side);
            shot.position = Unblock(impactPoint, shot.position);
        }
        else
        {
            Vector3 center = wreck != null ? wreck.Center : impactPoint;
            smoothedTarget = wreckFramed ? Vector3.Lerp(smoothedTarget, center, 1f - Mathf.Exp(-Time.unscaledDeltaTime / WreckSmoothing)) : center;
            wreckFramed = true;
            shot = OrbitShot(smoothedTarget, killer.root.position, (t - timeline.impactEnd) / (timeline.settled - timeline.impactEnd), side);
            shot.position = Unblock(smoothedTarget, shot.position);
        }

        // Le tireur quitte ses yeux : la caméra part de la sienne, sans coupure. Les autres arrivent sur le plan.
        Camera player = localIsKiller && t < TakeOffTime ? playerView.Player : null;
        if (player != null)
        {
            float w = Mathf.SmoothStep(0f, 1f, t / TakeOffTime);
            shot.position = Vector3.Lerp(player.transform.position, shot.position, w);
            shot.rotation = Quaternion.Slerp(player.transform.rotation, shot.rotation, w);
            shot.fieldOfView = Mathf.Lerp(player.fieldOfView, shot.fieldOfView, w);
            exact = true;
        }

        if (!framed || exact)
        {
            framed = true;
            stagePosition = smoothedPosition = shot.position;
            stageRotation = smoothedRotation = shot.rotation;
            stageFieldOfView = smoothedFieldOfView = shot.fieldOfView;
        }
        else
        {
            // De la balle à l'impact, de l'impact à l'épave : une seule caméra qui glisse, jamais de coupe. Deux lissages de
            // suite : elle part doucement, au lieu de s'arracher d'un coup à l'impact.
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / CameraSmoothing);
            stagePosition = Vector3.Lerp(stagePosition, shot.position, k);
            stageRotation = Quaternion.Slerp(stageRotation, shot.rotation, k);
            stageFieldOfView = Mathf.Lerp(stageFieldOfView, shot.fieldOfView, k);
            smoothedPosition = Vector3.Lerp(smoothedPosition, stagePosition, k);
            smoothedRotation = Quaternion.Slerp(smoothedRotation, stageRotation, k);
            smoothedFieldOfView = Mathf.Lerp(smoothedFieldOfView, stageFieldOfView, k);
        }
        killCamera.fieldOfView = smoothedFieldOfView;
        killCamera.transform.SetPositionAndRotation(smoothedPosition, smoothedRotation);
    }

    // Les deux robots de côté.
    private Shot BothRobots(float u)
    {
        Vector3 shooterChest = PointOf(killer.chest, killer, 1.25f);
        Vector3 victimChest = PointOf(victim.chest, victim, 1.25f);
        Shot shot = SideShot(shooterChest, victimChest, u, side);
        shot.position = Unblock((shooterChest + victimChest) * 0.5f, shot.position);
        return shot;
    }

    // La caméra rentre au joueur. Le tireur la voit remonter la course de la balle, dégagée. Un autre, s'il y a un mur entre
    // l'épave et lui, reste sur l'épave jusqu'au bout : il retrouve ses yeux d'un coup.
    private void BeginReturn()
    {
        returning = true;
        returnFrom = killCamera.transform.position;
        returnRotation = killCamera.transform.rotation;
        returnFieldOfView = killCamera.fieldOfView;
        Camera player = playerView.Player;
        Vector3 to = player != null ? player.transform.position : returnFrom;
        Vector3 back = to - impactPoint;
        returnVia = localIsKiller && hasShot ? impactPoint + back.normalized * Mathf.Min(BulletCamStop, back.magnitude) : (returnFrom + to) * 0.5f;
        returnBlocked = player == null || Physics.Linecast(localIsKiller ? returnVia : returnFrom, to, WorldMask, QueryTriggerInteraction.Ignore);
    }

    private void Return(float u)
    {
        Camera player = playerView.Player;
        if (player == null || returnBlocked) return;
        Vector3 to = player.transform.position;
        Vector3 position = ReturnPosition(returnFrom, returnVia, to, u);
        float s = Mathf.SmoothStep(0f, 1f, u);
        killCamera.fieldOfView = Mathf.Lerp(returnFieldOfView, player.fieldOfView, s);
        killCamera.transform.SetPositionAndRotation(position, Quaternion.Slerp(returnRotation, player.transform.rotation, s));
    }

    // La place de la caméra depuis `from` vers `direction`, jusqu'à SideRoom.
    private static float Room(Vector3 from, Vector3 direction)
        => Physics.SphereCast(from, 0.12f, direction, out RaycastHit hit, SideRoom, WorldMask, QueryTriggerInteraction.Ignore) ? hit.distance : SideRoom;

    /// <summary>Le décor ne passe jamais entre la caméra et ce qu'elle filme : elle se pose devant.</summary>
    private static Vector3 Unblock(Vector3 from, Vector3 to)
    {
        Vector3 path = to - from;
        float length = path.magnitude;
        if (length < 1e-3f) return to;
        Vector3 direction = path / length;
        if (Physics.SphereCast(from, 0.12f, direction, out RaycastHit hit, length, WorldMask, QueryTriggerInteraction.Ignore))
            return from + direction * Mathf.Max(0.25f, hit.distance - 0.05f);
        return to;
    }

    /// <summary>La flamme tenue au ralenti à la bouche de l'arme (le coup de feu, lui, s'est entendu en jeu) ; la balle qui
    /// siffle ; puis l'impact, le robot qui grésille et s'éteint, ses pièces qui cognent, plus graves. Chez ce joueur seul, à
    /// plat ; et chez le tireur, le retour du coup qui tue, que le serveur a gardé pour ce moment.</summary>
    private void PlayCues(float t)
    {
        if (!flashCue)
        {
            flashCue = true;
            if (hasShot && !beam && killerFeedback != null && killer.muzzle != null)
            {
                Vector3 forward = killer.weapon != null ? killer.weapon.forward : killer.muzzle.forward;
                killerFeedback.SpawnMuzzleFlash(killer.muzzle, forward, 1.2f, FlashHold);
            }
        }
        if (!flybyCue && bulletCam)
        {
            flybyCue = true;
            SoundSet flyby = killerWeapon != null ? killerWeapon.flybySounds : null;
            if (flyby != null && !flyby.IsEmpty) OneShotAudio.Play2D(flyby.Pick(), flyby.volume, FlybyPitch);
        }
        if (!impactCue && t >= timeline.impact)
        {
            impactCue = true;
            SoundSet impact = killerWeapon != null && current.cause != Health.DamageCause.Glove ? killerWeapon.bodyImpactSounds : null;
            if (impact != null && !impact.IsEmpty) OneShotAudio.Play2D(impact.Pick(), impact.volume, SlowPitch);
            GameSounds sounds = victimDeath != null ? victimDeath.Sounds : null;
            if (sounds != null)
            {
                Play2D(sounds.deathCrackle, 0.7f);
                Play2D(sounds.deathShutdown, 0.8f);
                Play2D(sounds.corpseImpact, 0.55f);
            }
            if (localIsKiller && killerController != null) killerController.ShowKillConfirmed();
        }
    }

    private static void Play2D(SoundSet set, float pitch)
    {
        if (set != null && !set.IsEmpty) OneShotAudio.Play2D(set.Pick(), set.volume, pitch);
    }

    // ------------------------------------------------------------------
    // La physique au ralenti
    // ------------------------------------------------------------------

    private void SlowPhysics()
    {
        if (physicsSlowed) return;
        physicsSlowed = true;
        savedSimulation = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
    }

    private void StepPhysics()
    {
        float step = Mathf.Min(Time.unscaledDeltaTime, MaxPhysicsStep) * rate;
        if (step > 0f) Physics.Simulate(step);
    }

    private void RestorePhysics()
    {
        if (!physicsSlowed) return;
        physicsSlowed = false;
        Physics.simulationMode = savedSimulation;
    }

    // ------------------------------------------------------------------
    // La balle (ou le rayon) au ralenti
    // ------------------------------------------------------------------

    // La balle (et les plombs d'une décharge), son sillage : créés d'avance, éteints.
    private void BuildStreaks()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        streakMaterial = new Material(shader) { name = "Balle de la kill cam" };
        for (int i = 0; i < MaxStreaks; i++) streaks.Add(NewLine("Balle de la kill cam", streakMaterial));
        // Une lueur ajoutée (RobotBurst.WakeMaterial) : elle s'éteint vers le noir, sans transparence.
        wake = NewLine("Sillage de la kill cam", RobotBurst.WakeMaterial);
        wake.startColor = WakeColor;
        wake.endColor = Color.black;
        wake.startWidth = WakeWidth;
        wake.endWidth = WakeWidth * 0.25f;
    }

    private LineRenderer NewLine(string name, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    private void PrepareStreaks(Color color)
    {
        streakMaterial.color = color;
        float width = beam ? BeamWidth : BulletWidth;
        foreach (LineRenderer line in streaks)
        {
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = beam ? width : width * 0.35f;
            line.enabled = false;
        }
        wake.enabled = false;
    }

    private void UpdateStreaks(float t)
    {
        // Le coup qui part au ralenti : la balle suivie, ou le rayon d'une arme laser (de près, d'un coup).
        bool flying = hasShot && t < timeline.impact + (beam ? BeamLinger : 0f) && (timeline.style == Style.Bullet || beam);
        float travel = BulletTravel(FlightProgress(t, timeline));
        for (int i = 0; i < streaks.Count; i++)
        {
            LineRenderer line = streaks[i];
            bool shown = flying && i < pelletEnds.Count;
            line.enabled = shown;
            if (!shown) continue;
            Vector3 path = pelletEnds[i] - muzzleStart;
            float length = path.magnitude;
            Vector3 direction = length > 1e-4f ? path / length : Vector3.forward;
            Vector3 front = muzzleStart + direction * (length * travel);
            Vector3 tail = beam ? muzzleStart : front - direction * Mathf.Min(StreakLength, length * travel);
            line.SetPosition(0, front);
            line.SetPosition(1, tail);
        }

        bool trailing = flying && !beam && t < timeline.impact && pelletEnds.Count > 0;
        if (wake == null) return;
        wake.enabled = trailing;
        if (!trailing) return;
        Vector3 main = pelletEnds[mainPellet] - muzzleStart;
        float distance = main.magnitude;
        Vector3 heading = distance > 1e-4f ? main / distance : Vector3.forward;
        Vector3 head = muzzleStart + heading * (distance * travel);
        wake.SetPosition(0, head - heading * Mathf.Min(StreakLength, distance * travel));
        wake.SetPosition(1, head - heading * Mathf.Min(WakeLength, distance * travel));
    }

    // ------------------------------------------------------------------
    // La fin
    // ------------------------------------------------------------------

    // Une kill cam que cette machine ne jouera pas : sa mort n'attend plus.
    private static void ReleaseVictimOf(Info info)
    {
        PlayerMatchState state = PlayerMatchState.Of(info.victim);
        PlayerDeath death = state != null ? state.GetComponent<PlayerDeath>() : null;
        if (death != null) death.ReleaseLiveKill();
    }

    private void Stop()
    {
        playing = false;
        rate = 1f;
        dim = 0f;
        if (killCamera != null) killCamera.enabled = false;
        playerView.Release();
        RestorePhysics();
        HideOwnBody();
        ownBodyShown = false;
        if (victimDeath != null)
        {
            if (!released) victimDeath.ReleaseLiveKill();
            RobotBurst.SetSpeed(victimDeath.Corpse, 1f);
        }
        foreach (LineRenderer line in streaks)
        {
            if (line != null) line.enabled = false;
        }
        if (wake != null) wake.enabled = false;
        killer = null;
        victim = null;
        killerWeapon = null;
        killerController = null;
        killerFeedback = null;
        victimDeath = null;
        shots.Clear();
    }

    // ------------------------------------------------------------------
    // La caméra
    // ------------------------------------------------------------------

    private void BuildCamera()
    {
        var go = new GameObject("Caméra de la kill cam") { tag = "MainCamera" };
        go.transform.SetParent(transform, false);
        killCamera = go.AddComponent<Camera>();
        // Tout ce qu'on voit de la partie, robots et épaves compris ; ni les bras de la vue subjective, ni les surfaces
        // touchables, ni les aperçus du menu.
        killCamera.cullingMask = ~(LayerBit("Viewmodel") | LayerBit("Hitbox") | (PreviewLayer.Index >= 0 ? 1 << PreviewLayer.Index : 0));
        killCamera.clearFlags = CameraClearFlags.Skybox;
        killCamera.nearClipPlane = 0.05f;
        killCamera.farClipPlane = 1000f;
        // Par-dessus celle de l'écran de fin ; celle du joueur s'éteint sous elle (CoveredCamera).
        killCamera.depth = 60f;
        killCamera.enabled = false;
        UniversalAdditionalCameraData data = killCamera.GetUniversalAdditionalCameraData();
        data.renderShadows = true;
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
    }

    private static int LayerBit(string name)
    {
        int layer = LayerMask.NameToLayer(name);
        return layer >= 0 ? 1 << layer : 0;
    }

    // Le corps du joueur, qu'il ne voit d'ordinaire qu'en ombre : la caméra le filme de dehors, pas de l'intérieur de sa tête.
    private void UpdateOwnBody()
    {
        Camera player = playerView.Player;
        bool outside = player == null || Vector3.Distance(killCamera.transform.position, player.transform.position) >= OwnBodyDistance;
        if (outside == ownBodyShown) return;
        ownBodyShown = outside;
        if (outside) ShowOwnBody();
        else HideOwnBody();
    }

    private void ShowOwnBody()
    {
        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject me = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
        Transform model = me != null ? me.transform.Find("Model") : null;
        if (model == null) return;
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) continue;
            r.shadowCastingMode = ShadowCastingMode.On;
            ownBody.Add(r);
        }
    }

    private void HideOwnBody()
    {
        foreach (Renderer r in ownBody)
        {
            if (r != null) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
        ownBody.Clear();
    }

    // Le temps du rendu de cette caméra : le décor assombri.
    private void BeginCameraRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera != killCamera || camera == null) return;
        Shader.SetGlobalFloat(ReplayDimId, dim);
    }

    private void EndCameraRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera != killCamera || camera == null) return;
        Shader.SetGlobalFloat(ReplayDimId, 0f);
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Surface de test : la kill cam joue-t-elle, sa caméra, son déroulé, la vitesse du monde.</summary>
    public bool TestPlaying => playing;
    public Camera TestCamera => killCamera;
    public bool TestBulletCam => bulletCam;
    public bool TestHasShot => hasShot;
    public Side TestSide => side;
    public Timeline TestTimeline => timeline;
    public float TestRate => rate;
    public Vector3 TestImpact => impactPoint;
#endif
}
