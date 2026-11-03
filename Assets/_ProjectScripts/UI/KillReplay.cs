using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// La kill cam : le replay du coup mortel (GDD § 6). Refaite le 2026-11-03 d'après les kill cams de Sniper Elite et les
/// brutalities de Mortal Kombat (l'utilisateur : le déclenchement en jeu, la balle suivie par la caméra, « surtout si
/// c'est un sniper », le robot qui explose avec de l'huile moteur à la place du sang et des étincelles, le ralenti, le
/// fond sombre ; pas les rayons X). Quand une manche, ou le match (dans un mode à réapparitions, le coup qui gagne), se
/// finit sur une élimination, le noir tombe LeadIn après le coup et tous le revoient au ralenti ; puis le gagnant garde
/// la main, le temps de narguer, avant le noir de la fin de manche.
///
/// Deux déroulés (TimelineFor, sur ce que publie le serveur : la même durée partout) :
/// - la balle, pour un tir de ShortShot et plus : par-dessus l'épaule du tireur jusqu'au coup, puis la balle suivie de la
///   bouche à la cible, le monde presque à l'arrêt, d'autant plus longtemps que le tir est long (FlightTime) ;
/// - de près, pour le gant et un tir à bout portant : les deux robots de côté jusqu'au coup.
/// Puis l'impact, de côté, au plus lent : le robot vole en pièces (RobotCorpse.Burst, tel qu'il est tombé sur cette
/// machine), l'huile gicle, les étincelles partent (RobotBurst) ; puis la caméra tourne autour de l'épave pendant que le
/// temps revient à la normale. Le décor s'assombrit autour des robots, presque noir à l'impact (DimAt : le global
/// _ReplayDim, que lisent les shaders du décor le temps du rendu de la caméra du replay seulement).
///
/// Catégorie C. Le serveur ne décide que QUAND (RoundManager publie l'instant, le tireur, la victime, l'arme, la zone et la
/// distance : la même légende partout), et gèle les joueurs le temps du replay. Chaque machine rejoue ce qu'ELLE a vu :
/// elle enregistre en continu la pose des robots des duellistes de son match (les os du modèle, 60 fois par seconde, sur
/// 12 s ; l'épave une fois tombé, ses pièces arrachées comprises) et les tirs qu'elle a montrés
/// (WeaponController.ShotShown) ; le moment venu, elle pose ces poses sur des copies des robots (des marionnettes, sur le
/// layer Preview) que filme sa propre caméra, par-dessus celle du joueur. Les vrais robots sont masqués pour cette caméra
/// le temps de son rendu, comme à l'écran de fin (VictoryStage), et les épaves ne sont pas sur ses layers. L'enregistrement
/// continue pendant le replay : l'épave finit de tomber dans la partie pendant qu'on en revoit le début.
/// </summary>
[DefaultExecutionOrder(1000)]
public class KillReplay : MonoBehaviour
{
    // ------------------------------------------------------------------
    // Le déroulé (temps réel, en secondes depuis le début du replay)
    // ------------------------------------------------------------------

    /// <summary>La kill cam part ce temps après l'élimination (s, horloge du serveur) : on voit le coup porter, et le noir
    /// tombe aussitôt.</summary>
    public const float LeadIn = 0.3f;
    /// <summary>Le passage au noir, de part et d'autre du début et de la fin du replay (s).</summary>
    public const float CutTime = 0.18f;
    /// <summary>Une élimination finit-elle la manche ? Seulement si elle date de moins que ça (s, horloge du serveur) :
    /// la mort se voit et la manche se finit dans la même image ; une zone tenue ou un module ramassé plus tard, non.</summary>
    public const float FreshKill = 0.25f;
    /// <summary>Un replay pas encore commencé à cet instant ne commence plus (une machine arrivée en retard).</summary>
    public const float LateStart = 0.6f;
    /// <summary>En deçà de cette distance d'un robot à l'autre (m), pas de balle suivie (elle partirait du bras du
    /// tireur) : le coup se voit de côté, comme un coup de gant.</summary>
    public const float ShortShot = 4f;

    /// <summary>À la balle : par-dessus l'épaule du tireur, jusqu'au coup (s).</summary>
    public const float BulletApproach = 0.8f;
    /// <summary>À la balle : le temps de jeu montré avant le coup (s).</summary>
    public const float BulletLead = 0.35f;
    /// <summary>On voit la flamme partir de derrière le tireur, puis la balle (s).</summary>
    public const float FlashCut = 0.12f;
    /// <summary>Le vol de la balle (s) : FlightBase plus FlightPerMeter par mètre, de FlightMin à FlightMax (d'un poste à
    /// l'autre, 56 m : 2,2 s).</summary>
    public const float FlightMin = 0.9f;
    public const float FlightMax = 2.4f;
    public const float FlightBase = 0.55f;
    public const float FlightPerMeter = 0.03f;
    /// <summary>La caméra sur la balle s'arrête à cette distance de l'impact (m), et regarde la balle arriver.</summary>
    public const float BulletCamStop = 1.3f;
    /// <summary>La caméra suit la balle à cette distance (m) ; le rayon d'une arme laser, qui se voit en entier, de plus
    /// loin.</summary>
    public const float BulletCamBehind = 0.8f;
    public const float BeamCamBehind = 1.1f;
    /// <summary>La traînée de la balle et son sillage, une lueur qui s'efface derrière elle (m) : plus courts que l'écart
    /// de la caméra. Plus longs, ils passaient derrière l'objectif et barraient l'image (la traînée, une barre jaune ; le
    /// sillage de 3 m, une bande fauve ; vus à deux instances le 2026-11-03).</summary>
    public const float StreakLength = 0.22f;
    public const float WakeLength = 0.55f;

    /// <summary>De près : les deux robots de côté, jusqu'au coup (s).</summary>
    public const float CloseApproach = 1f;
    /// <summary>De près : le temps de jeu montré avant le coup (s) ; l'élan du gant s'y voit.</summary>
    public const float CloseLead = 0.55f;

    /// <summary>L'impact, au plus lent : ImpactReal secondes pour ImpactGame de jeu.</summary>
    public const float ImpactReal = 0.75f;
    public const float ImpactGame = 0.1f;
    /// <summary>L'épave : AftermathReal secondes pour AftermathGame de jeu, le temps revenant peu à peu à la normale.</summary>
    public const float AftermathReal = 1.4f;
    public const float AftermathGame = 0.9f;

    /// <summary>Le décor assombri (0 à 1, DimAt) : pendant l'approche, au bout du vol, à l'impact (atteint en DimRise),
    /// et à la fin.</summary>
    public const float DimApproach = 0.3f;
    public const float DimFlight = 0.45f;
    public const float DimImpact = 0.9f;
    public const float DimAftermath = 0.6f;
    public const float DimRise = 0.15f;

    /// <summary>La hauteur du son du coup rejoué : le ralenti.</summary>
    public const float SlowPitch = 0.6f;

    /// <summary>Ce que l'enregistrement garde (s).</summary>
    public const float RecordSeconds = 12f;
    /// <summary>Les images enregistrées par seconde, au plus.</summary>
    public const float SampleRate = 60f;

    // L'épave : le temps part de la vitesse de l'impact et finit un peu plus vite que la vitesse réelle (AftermathEase).
    private const float AftermathStartRate = ImpactGame / ImpactReal * AftermathReal / AftermathGame;
    // La balle : lente en sortant du canon et en arrivant, vite entre les deux (BulletTravel).
    private const float BulletEaseDepth = 0.6f;
    private const float BulletRoll = 7f;            // degrés : la caméra s'incline pendant le vol, portée par la balle
    private const float FlybyPitch = 0.5f;
    // La mort à revoir est celle qu'on vient de voir (s), pas une d'avant.
    private const float FreshDeath = 2.5f;

    private const float CameraSmoothing = 0.08f;    // s : le plan suit le robot sans trembler avec lui
    private const float WreckSmoothing = 0.2f;      // s : autour de l'épave, sans suivre chaque pièce qui part
    private const float BulletWidth = 0.008f;
    private const float WakeWidth = 0.012f;
    private const float BeamWidth = 0.02f;
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
    public struct Info : INetworkSerializeByMemcpy, IEquatable<Info>
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

    /// <summary>Le déroulé d'un replay, en temps réel depuis son début (s) : le coup part (`shot`), il arrive (`impact` ;
    /// de près, au même instant), l'impact au plus lent se finit (`impactEnd`), la fin (`duration`) ; et le temps de jeu
    /// montré avant le coup (`lead`).</summary>
    public struct Timeline
    {
        public Style style;
        public float shot;
        public float impact;
        public float impactEnd;
        public float duration;
        public float lead;
    }

    /// <summary>La balle suivie pour un tir de ShortShot et plus ; de près pour le gant et un tir à bout portant.</summary>
    public static Style StyleOf(Info info)
        => info.cause != Health.DamageCause.Glove && info.Distance >= ShortShot ? Style.Bullet : Style.Close;

    /// <summary>Le vol de la balle (s) pour un tir de `distance` mètres.</summary>
    public static float FlightTime(float distance)
        => Mathf.Clamp(FlightBase + FlightPerMeter * (float.IsFinite(distance) ? Mathf.Max(0f, distance) : 0f), FlightMin, FlightMax);

    /// <summary>Le déroulé du replay de `info`.</summary>
    public static Timeline TimelineFor(Info info)
    {
        bool bullet = StyleOf(info) == Style.Bullet;
        var line = new Timeline
        {
            style = bullet ? Style.Bullet : Style.Close,
            lead = bullet ? BulletLead : CloseLead,
            shot = bullet ? BulletApproach : CloseApproach,
        };
        line.impact = line.shot + (bullet ? FlightTime(info.Distance) : 0f);
        line.impactEnd = line.impact + ImpactReal;
        line.duration = line.impactEnd + AftermathReal;
        return line;
    }

    /// <summary>La durée du replay de `info` (s).</summary>
    public static float DurationOf(Info info) => TimelineFor(info).duration;

    /// <summary>Ce que la kill cam de `info` ajoute à la fin de manche (s) : le temps d'y passer, et elle.</summary>
    public static float PhaseExtension(Info info) => LeadIn + DurationOf(info);

    /// <summary>L'élimination vue à `killAt` finit-elle la manche à `now` ? (FreshKill)</summary>
    public static bool IsFresh(double killAt, double now) => killAt > 0d && now >= killAt && now - killAt <= FreshKill;

    /// <summary>Ce replay est-il celui de la phase commencée à `phaseStartedAt` ? (un replay d'avant n'est plus rien)</summary>
    public static bool BelongsTo(Info info, double phaseStartedAt) => info.Scheduled && info.startsAt >= phaseStartedAt;

    /// <summary>Les joueurs sont-ils gelés ? Le temps du replay : rien ne bouge ni ne tire sous lui (ni pas, ni coups de
    /// feu entendus pendant qu'on le regarde). Avant (le coup qui porte) et après (le gagnant qui nargue), si.</summary>
    public static bool Freezes(Info info, double phaseStartedAt, double now)
        => BelongsTo(info, phaseStartedAt) && now >= info.startsAt && now < info.startsAt + DurationOf(info);

    /// <summary>L'écran de fin vient après la kill cam quand il y en a une (`replay`), puis le temps du gagnant. Au
    /// Tournoi, aucun : le champion a gardé la main dans la finale, son écran vient tout de suite.</summary>
    public static float EndScreenDelay(bool tournament, float roundOverDuration, Info replay)
        => tournament ? 0f : roundOverDuration + (replay.Scheduled ? PhaseExtension(replay) : 0f);

    /// <summary>Le noir (0 à 1) à l'instant `t` d'un replay de `duration` secondes : il tombe sur la partie et se lève sur
    /// le replay, puis tombe sur le replay et se lève sur la partie.</summary>
    public static float CutDip(float t, float duration)
        => Mathf.Clamp01(Mathf.Max(1f - Mathf.Abs(t) / CutTime, 1f - Mathf.Abs(t - duration) / CutTime));

    /// <summary>
    /// L'instant de l'enregistrement montré à l'instant `t` du replay (temps réel). L'approche joue les `lead` secondes
    /// qui précèdent le coup (`shotAt`) ; le vol de la balle, du coup à la mort vue sur cette machine (`deathAt`), le
    /// monde presque figé (tout à fait figé quand la mort se voit dans l'image du coup) ; l'impact, ImpactGame ; l'épave,
    /// AftermathGame, de plus en plus vite. De près, l'approche finit sur l'impact. Monotone, sans saut.
    /// </summary>
    public static double SourceTime(float t, Timeline line, double shotAt, double deathAt)
    {
        double impact = Math.Max(shotAt, deathAt);
        double start = shotAt - line.lead;
        double approachEnd = line.impact > line.shot ? shotAt : impact;
        if (t <= line.shot) return start + (approachEnd - start) * Mathf.Clamp01(t / line.shot);
        if (t <= line.impact) return shotAt + (impact - shotAt) * ((t - line.shot) / (line.impact - line.shot));
        if (t <= line.impactEnd) return impact + ImpactGame * ((t - line.impact) / (line.impactEnd - line.impact));
        float u = Mathf.Clamp01((t - line.impactEnd) / (line.duration - line.impactEnd));
        return impact + ImpactGame + AftermathGame * AftermathEase(u);
    }

    /// <summary>La vitesse du temps montré à l'instant `t` (secondes de jeu par seconde réelle) : la dérivée de
    /// SourceTime. Elle règle le ralenti des gerbes du replay.</summary>
    public static float WarpRate(float t, Timeline line, double shotAt, double deathAt)
    {
        double impact = Math.Max(shotAt, deathAt);
        double approachEnd = line.impact > line.shot ? shotAt : impact;
        if (t < line.shot) return (float)((approachEnd - (shotAt - line.lead)) / line.shot);
        if (t < line.impact) return (float)((impact - shotAt) / (line.impact - line.shot));
        if (t < line.impactEnd) return ImpactGame / (line.impactEnd - line.impact);
        float u = Mathf.Clamp01((t - line.impactEnd) / (line.duration - line.impactEnd));
        return AftermathGame * (AftermathStartRate + 2f * (1f - AftermathStartRate) * u) / (line.duration - line.impactEnd);
    }

    // La part de l'épave montrée (0 à 1) à la part `u` de son temps : elle part à la vitesse de l'impact et accélère.
    private static float AftermathEase(float u) => AftermathStartRate * u + (1f - AftermathStartRate) * u * u;

    /// <summary>Où en est le vol de la balle (0 à 1, en temps) à l'instant `t` du replay ; de près, 0 avant le coup et 1
    /// après.</summary>
    public static float FlightProgress(float t, Timeline line)
        => line.impact > line.shot ? Mathf.Clamp01((t - line.shot) / (line.impact - line.shot)) : t >= line.shot ? 1f : 0f;

    /// <summary>La part du chemin faite par la balle à la part `u` de son vol : lente en sortant du canon et en arrivant
    /// (40 % de sa vitesse moyenne), vite entre les deux. Monotone, de 0 à 1.</summary>
    public static float BulletTravel(float u)
    {
        u = Mathf.Clamp01(u);
        return u - BulletEaseDepth * Mathf.Sin(2f * Mathf.PI * u) / (2f * Mathf.PI);
    }

    /// <summary>Le décor assombri (0 à 1) à l'instant `t` du replay : un peu pendant l'approche, plus au bout du vol, presque
    /// noir à l'impact (atteint en DimRise), puis un peu moins pendant que l'épave tombe.</summary>
    public static float DimAt(float t, Timeline line)
    {
        if (t < 0f || t >= line.duration) return 0f;
        if (t < line.shot) return DimApproach;
        if (t < line.impact) return Mathf.Lerp(DimApproach, DimFlight, (t - line.shot) / (line.impact - line.shot));
        float before = line.impact > line.shot ? DimFlight : DimApproach;
        if (t < line.impact + DimRise) return Mathf.Lerp(before, DimImpact, (t - line.impact) / DimRise);
        if (t < line.impactEnd) return DimImpact;
        return Mathf.Lerp(DimImpact, DimAftermath, (t - line.impactEnd) / (line.duration - line.impactEnd));
    }

    /// <summary>
    /// La légende du replay : « FAUCON · FP-12 · À LA TÊTE · 32 M », « FAUCON · FA-07 · 18 M », « FAUCON · COUP DE
    /// GANT ». La distance d'un robot à l'autre, au mètre près (1 m au moins).
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

    /// <summary>L'approche de la balle : par-dessus l'épaule droite du tireur (son œil `eye`), tournée vers la victime
    /// (`target`) ; elle avance un peu (`u` de 0 à 1).</summary>
    public static Shot ShoulderShot(Vector3 eye, Vector3 target, float u)
    {
        Vector3 flat = target - eye;
        flat.y = 0f;
        Vector3 forward = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);
        float back = Mathf.Lerp(1.3f, 0.9f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u)));
        Vector3 position = eye - forward * back + right * 0.45f + Vector3.up * 0.12f;
        return new Shot { position = position, rotation = Look(target - position), fieldOfView = 50f };
    }

    /// <summary>
    /// La balle, de la bouche `origin` à l'impact `end` (`travel` : la part du chemin faite, BulletTravel ; `bullet` : où
    /// elle est) : la caméra la suit de près, un peu au-dessus et à droite de sa course (plus loin pour le rayon d'une arme
    /// laser, qui se voit en entier), en s'inclinant, s'arrête BulletCamStop avant l'impact et la regarde arriver.
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
        float behind = beam ? BeamCamBehind : BulletCamBehind;
        float along = Mathf.Clamp(travelled - behind, 0f, Mathf.Max(0f, length - BulletCamStop));
        Vector3 offset = beam ? right * 0.3f + up * 0.16f : right * 0.05f + up * 0.07f;
        Vector3 position = origin + direction * along + offset;
        Quaternion rotation = Look(end - position) * Quaternion.Euler(0f, 0f, BulletRoll * Mathf.Sin(Mathf.PI * done));
        return new Shot { position = position, rotation = rotation, fieldOfView = beam ? 50f : 40f };
    }

    /// <summary>L'approche de près (le gant, un tir à bout portant) : de côté, à droite du tireur (`shooter`, `victim` :
    /// leurs bustes), les deux robots dans le cadre de la tête aux pieds ; la caméra glisse un peu (`u` de 0 à 1).</summary>
    public static Shot SideShot(Vector3 shooter, Vector3 victim, float u)
    {
        Vector3 middle = (shooter + victim) * 0.5f;
        Vector3 between = victim - shooter;
        between.y = 0f;
        float span = between.magnitude;
        Vector3 along = span > 1e-4f ? between / span : Vector3.forward;
        Vector3 side = new Vector3(along.z, 0f, -along.x);
        float distance = Mathf.Max(3.2f, span * 0.75f + 2f);
        Vector3 position = middle + side * distance - Vector3.up * 0.15f + along * Mathf.Lerp(-0.35f, 0.35f, Mathf.Clamp01(u));
        // Le milieu des robots, pas leurs bustes : les pieds restent dans le cadre.
        return new Shot { position = position, rotation = Look(middle - Vector3.up * 0.35f - position), fieldOfView = 45f };
    }

    /// <summary>L'impact, au plus lent : de côté, à droite du coup (`direction`), à 1,5 m du point touché puis 2,1 m, un peu
    /// en arrière (on voit le coup arriver) et regardant un peu devant (là où partent l'huile et les pièces) ; `u` de 0 à
    /// 1.</summary>
    public static Shot ImpactShot(Vector3 point, Vector3 direction, float u)
    {
        Vector3 flat = new Vector3(direction.x, 0f, direction.z);
        Vector3 along = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
        Vector3 side = new Vector3(along.z, 0f, -along.x);
        float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
        Vector3 position = point + side * Mathf.Lerp(1.5f, 2.1f, s) - along * 0.35f + Vector3.up * Mathf.Lerp(0.05f, 0.2f, s);
        return new Shot { position = position, rotation = Look(point + along * 0.3f - position), fieldOfView = 45f };
    }

    /// <summary>L'épave : autour d'elle (`target`, son bassin), de 3,4 à 4,3 m et de 14 à 30° au-dessus (les pièces qui
    /// volent dans le cadre), tournée de 70° depuis la ligne du tireur (`shooter`), puis de 50° de plus (`u` de 0 à
    /// 1).</summary>
    public static Shot OrbitShot(Vector3 target, Vector3 shooter, float u)
    {
        Vector3 away = shooter - target;
        away.y = 0f;
        float baseYaw = away.sqrMagnitude > 1e-6f ? Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg : 0f;
        float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
        float yaw = baseYaw + 70f + 50f * s;
        float elevation = Mathf.Lerp(14f, 30f, s);
        float distance = Mathf.Lerp(3.4f, 4.3f, s);
        Vector3 position = target + Quaternion.Euler(-elevation, yaw, 0f) * Vector3.forward * distance;
        return new Shot { position = position, rotation = Look(target + Vector3.up * 0.1f - position), fieldOfView = 50f };
    }

    private static Quaternion Look(Vector3 direction)
        => direction.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;

    // ------------------------------------------------------------------
    // L'enregistrement d'un robot (un anneau, testé)
    // ------------------------------------------------------------------

    /// <summary>
    /// La pose d'un robot, image par image, sur `capacity` images : la place de sa racine dans le monde et celle de
    /// chaque os sous elle. Une image vient du robot vivant ou de son épave (`wreck`) : on ne fond jamais l'un dans
    /// l'autre. Un os disparu (une arme refaite) garde sa dernière pose.
    /// </summary>
    public sealed class Recording
    {
        private readonly int boneCount;
        private readonly int capacity;
        private readonly double[] times;
        private readonly bool[] wrecks;
        private readonly Vector3[] rootPositions;
        private readonly Quaternion[] rootRotations;
        private readonly Vector3[] bonePositions;
        private readonly Quaternion[] boneRotations;
        private int head;   // l'image la plus ancienne
        private int count;

        public Recording(int bones, int capacity)
        {
            boneCount = Mathf.Max(0, bones);
            this.capacity = Mathf.Max(2, capacity);
            times = new double[this.capacity];
            wrecks = new bool[this.capacity];
            rootPositions = new Vector3[this.capacity];
            rootRotations = new Quaternion[this.capacity];
            bonePositions = new Vector3[this.capacity * boneCount];
            boneRotations = new Quaternion[this.capacity * boneCount];
        }

        public int Count => count;
        public int BoneCount => boneCount;
        public double Oldest => count > 0 ? times[Slot(0)] : double.NaN;
        public double Newest => count > 0 ? times[Slot(count - 1)] : double.NaN;

        public void Clear()
        {
            head = 0;
            count = 0;
        }

        /// <summary>Ajoute l'image de l'instant `time` : `root` dans le monde, `bones` sous lui (un os null garde la pose
        /// de l'image d'avant, ou `rest`).</summary>
        public void Add(double time, Transform root, Transform[] bones, bool wreck, Vector3[] restPositions = null,
                        Quaternion[] restRotations = null)
        {
            if (root == null) return;
            int previous = count > 0 ? Slot(count - 1) : -1;
            int slot;
            if (count < capacity) slot = Slot(count++);
            else
            {
                slot = head;
                head = (head + 1) % capacity;
            }
            times[slot] = time;
            wrecks[slot] = wreck;
            rootPositions[slot] = root.position;
            rootRotations[slot] = root.rotation;
            int at = slot * boneCount;
            int before = previous * boneCount;
            for (int i = 0; i < boneCount; i++)
            {
                Transform bone = bones != null && i < bones.Length ? bones[i] : null;
                if (bone != null)
                {
                    bonePositions[at + i] = bone.localPosition;
                    boneRotations[at + i] = bone.localRotation;
                }
                else if (previous >= 0)
                {
                    bonePositions[at + i] = bonePositions[before + i];
                    boneRotations[at + i] = boneRotations[before + i];
                }
                else
                {
                    bonePositions[at + i] = restPositions != null && i < restPositions.Length ? restPositions[i] : Vector3.zero;
                    boneRotations[at + i] = restRotations != null && i < restRotations.Length ? restRotations[i] : Quaternion.identity;
                }
            }
        }

        /// <summary>Pose `root` et `bones` comme à l'instant `time` (entre deux images, le mélange des deux ; avant la
        /// première ou après la dernière, celle-là). Vrai si c'est l'épave qu'on montre.</summary>
        public bool Pose(double time, Transform root, Transform[] bones)
        {
            if (count == 0 || root == null) return false;
            int index = FrameBefore(time);
            int a = Slot(index);
            int b = Slot(Mathf.Min(index + 1, count - 1));
            float w = 0f;
            if (a != b)
            {
                if (wrecks[a] != wrecks[b])
                {
                    // Le robot ou son épave, jamais les deux mêlés.
                    if (time >= times[b]) a = b;
                    b = a;
                }
                else if (times[b] > times[a]) w = Mathf.Clamp01((float)((time - times[a]) / (times[b] - times[a])));
            }
            root.SetPositionAndRotation(Vector3.Lerp(rootPositions[a], rootPositions[b], w),
                                        Quaternion.Slerp(rootRotations[a], rootRotations[b], w));
            int atA = a * boneCount;
            int atB = b * boneCount;
            int n = bones != null ? Mathf.Min(bones.Length, boneCount) : 0;
            for (int i = 0; i < n; i++)
            {
                Transform bone = bones[i];
                if (bone == null) continue;
                bone.localPosition = Vector3.Lerp(bonePositions[atA + i], bonePositions[atB + i], w);
                bone.localRotation = Quaternion.Slerp(boneRotations[atA + i], boneRotations[atB + i], w);
            }
            return wrecks[w > 0.5f ? b : a];
        }

        private int Slot(int index) => (head + index) % capacity;

        // La dernière image d'avant `time` (la première s'il n'y en a pas).
        private int FrameBefore(double time)
        {
            int lo = 0;
            int hi = count - 1;
            if (time <= times[Slot(lo)]) return lo;
            if (time >= times[Slot(hi)]) return hi;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (times[Slot(mid)] <= time) lo = mid;
                else hi = mid;
            }
            return lo;
        }
    }

    // ------------------------------------------------------------------
    // Ce que l'écran montre (lu par GameHud)
    // ------------------------------------------------------------------

    public static KillReplay Instance { get; private set; }

    /// <summary>Le replay (ou le noir qui l'ouvre) a-t-il l'écran ? Le HUD de la partie s'efface.</summary>
    public static bool Covers => Instance != null && Instance.playing && Instance.elapsed >= -CutTime
                                 && Instance.elapsed < Instance.timeline.duration;

    /// <summary>Le noir des passages au replay et du retour à la partie (0 à 1).</summary>
    public static float Dip => Instance != null && (Instance.playing || Instance.exiting)
        ? CutDip(Instance.elapsed, Instance.timeline.duration) : 0f;

    /// <summary>La légende se montre-t-elle ? Dès que le replay est à l'écran.</summary>
    public static bool CaptionShown => Covers && Instance.elapsed >= 0f;

    /// <summary>La légende du replay en cours (Caption).</summary>
    public static string CurrentCaption => Instance != null ? Instance.caption : "";

    /// <summary>Le replay de cette partie : créé par le RoundManager de la scène à son apparition, dans sa scène (il
    /// disparaît avec elle).</summary>
    public static KillReplay Ensure(Scene scene)
    {
        if (Instance != null) return Instance;
        var go = new GameObject("Replay du coup mortel");
        if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(go, scene);
        return go.AddComponent<KillReplay>();
    }

    // ------------------------------------------------------------------
    // L'état
    // ------------------------------------------------------------------

    /// <summary>Un robot suivi : le duelliste, son modèle, ses os (par chemin sous le modèle, les mêmes sous son épave et
    /// sous sa marionnette) et leur enregistrement.</summary>
    private sealed class Track
    {
        public PlayerLocomotion player;
        public Transform model;
        public Health health;
        public PlayerDeath death;
        public string[] paths;
        public Transform[] liveBones;
        public Vector3[] restPositions;
        public Quaternion[] restRotations;
        public RobotCorpse wreck;
        public Transform[] wreckBones;
        public Recording recording;
        public double deathAt = -1d;    // la dernière mort vue sur cette machine (son horloge)
        public bool wasDead;
    }

    /// <summary>Un tir montré par cette machine : son instant, d'où, où arrive chaque plomb.</summary>
    private struct ShownShot
    {
        public double time;
        public ulong shooter;
        public Vector3 origin;
        public Vector3[] ends;
    }

    /// <summary>Une marionnette : la copie d'un robot que le replay pose image par image.</summary>
    private sealed class Puppet
    {
        public GameObject root;
        public Transform[] bones;
        public Transform head;
        public Transform chest;
        public Transform hips;
        public Transform weapon;
        public Transform muzzle;
        public bool deathShown;
    }

    private readonly Dictionary<ulong, Track> tracks = new Dictionary<ulong, Track>();
    private readonly List<ulong> gone = new List<ulong>();
    private readonly List<ShownShot> shots = new List<ShownShot>();

    private bool playing;
    private bool exiting;           // fini à son heure : le noir se lève sur la partie
    private float elapsed = float.NegativeInfinity;
    private double begun;
    private Info current;
    private Timeline timeline;
    private Track killerTrack;
    private Track victimTrack;
    private Puppet killer;
    private Puppet victim;
    private double shotAt;
    private double deathAt;
    private bool hasShot;
    private bool bulletCam;
    private bool beam;
    private Vector3 muzzleStart;
    private readonly List<Vector3> pelletEnds = new List<Vector3>();
    private int mainPellet;
    private Vector3 impactPoint;
    private Vector3 impactDirection;
    private Color victimPaint;
    private readonly List<LineRenderer> streaks = new List<LineRenderer>();
    private LineRenderer wake;
    private Material streakMaterial;
    private readonly List<GameObject> bursts = new List<GameObject>();
    private bool burstShown;
    private string caption = "";
    private bool fireCue;
    private bool flybyCue;
    private bool impactCue;
    private float dim;
    private WeaponData killerWeapon;
    private TaserController killerTaser;
    private WeaponVisualFeedback killerFeedback;
    private PlayerDeath victimDeath;
    private Camera replayCamera;
    private int shownSegment = -1;
    private Vector3 smoothedPosition;
    private Quaternion smoothedRotation;
    private Vector3 smoothedTarget;

    // Les vrais robots, masqués le temps du rendu de la caméra du replay.
    private readonly List<Renderer> liveRenderers = new List<Renderer>();
    private readonly List<Renderer> hiddenNow = new List<Renderer>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        WeaponController.ShotShown += OnShotShown;
        RenderPipelineManager.beginCameraRendering += BeginReplayRender;
        RenderPipelineManager.endCameraRendering += EndReplayRender;
    }

    private void OnDisable()
    {
        WeaponController.ShotShown -= OnShotShown;
        RenderPipelineManager.beginCameraRendering -= BeginReplayRender;
        RenderPipelineManager.endCameraRendering -= EndReplayRender;
        Stop();
        exiting = false;
        Shader.SetGlobalFloat(ReplayDimId, 0f);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (streakMaterial != null) Destroy(streakMaterial);
    }

    // ------------------------------------------------------------------
    // Le fil : enregistrer, et rejouer
    // ------------------------------------------------------------------

    private void Update()
    {
        RoundManager rm = RoundManager.Local;
        bool spawned = rm != null && rm.IsSpawned;
        Info info = spawned ? rm.ReplayNow : default;
        elapsed = info.Scheduled ? rm.ReplayElapsed : float.NegativeInfinity;
        bool same = info.Scheduled && info.startsAt.Equals(current.startsAt);

        if (playing && (!same || elapsed >= timeline.duration))
        {
            exiting = same;
            Stop();
        }
        if (exiting && (!same || elapsed >= timeline.duration + CutTime)) exiting = false;

        // La mort se voit ici à peu près quand le replay s'annonce : on réessaie à chaque image jusqu'à LateStart.
        if (!playing && info.Scheduled && !info.startsAt.Equals(begun) && elapsed >= -CutTime && elapsed <= LateStart
            && TryBegin(info))
        {
            playing = true;
            begun = info.startsAt;
        }
    }

    private void LateUpdate()
    {
        // L'enregistrement continue pendant le replay : la chute qu'il montrera se joue encore dans la partie.
        Record(Time.timeAsDouble);
        if (playing) Play(elapsed);
    }

    /// <summary>Les duellistes du match de cette machine, 60 fois par seconde au plus : ce qu'on reverra.</summary>
    private void Record(double now)
    {
        RoundManager local = RoundManager.Local;
        if (local == null || !local.IsSpawned) return;
        IReadOnlyList<PlayerLocomotion> players = PlayerLocomotion.SpawnedPlayers;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerLocomotion player = players[i];
            if (player == null || !player.IsSpawned) continue;
            ulong id = player.OwnerClientId;
            if (!player.TryGetComponent(out PlayerMatchState state) || !state.IsDuelist || RoundManager.For(id) != local) continue;
            if (!tracks.TryGetValue(id, out Track track) || track.player != player)
            {
                track = CreateTrack(player);
                if (track == null) continue;
                tracks[id] = track;
            }
            Sample(track, now);
        }

        gone.Clear();
        foreach (KeyValuePair<ulong, Track> pair in tracks)
        {
            if (pair.Value.player == null) gone.Add(pair.Key);
        }
        for (int i = 0; i < gone.Count; i++) tracks.Remove(gone[i]);

        // Les tirs, rangés par instant : ceux d'avant l'enregistrement ne serviront plus.
        int old = 0;
        while (old < shots.Count && now - shots[old].time > RecordSeconds) old++;
        if (old > 0) shots.RemoveRange(0, old);
    }

    private static void Sample(Track track, double now)
    {
        Recording recording = track.recording;
        if (recording.Count > 0 && now - recording.Newest < 1d / SampleRate - 1e-4) return;
        bool dead = track.health != null && track.health.IsDead;
        if (dead && !track.wasDead) track.deathAt = now;
        track.wasDead = dead;

        RobotCorpse wreck = dead && track.death != null ? track.death.Corpse : null;
        if (wreck != null)
        {
            if (wreck != track.wreck)
            {
                track.wreck = wreck;
                track.wreckBones = Resolve(wreck.transform, track.paths);
            }
            recording.Add(now, wreck.transform, track.wreckBones, true);
        }
        else if (!dead && track.model != null)
        {
            recording.Add(now, track.model, track.liveBones, false, track.restPositions, track.restRotations);
        }
        // Mort sans épave : rien de plus à montrer, la marionnette garde sa dernière pose.
    }

    private static Track CreateTrack(PlayerLocomotion player)
    {
        Transform model = player.transform.Find("Model");
        if (model == null) return null;
        List<Transform> skeleton = SkeletonOf(model);
        var track = new Track
        {
            player = player,
            model = model,
            health = player.GetComponent<Health>(),
            death = player.GetComponent<PlayerDeath>(),
            paths = new string[skeleton.Count],
            liveBones = skeleton.ToArray(),
            restPositions = new Vector3[skeleton.Count],
            restRotations = new Quaternion[skeleton.Count],
        };
        for (int i = 0; i < skeleton.Count; i++)
        {
            track.paths[i] = RelativePath(model, skeleton[i]);
            track.restPositions[i] = skeleton[i].localPosition;
            track.restRotations[i] = skeleton[i].localRotation;
        }
        track.recording = new Recording(skeleton.Count, Mathf.CeilToInt(RecordSeconds * SampleRate));
        return track;
    }

    /// <summary>Les os du modèle (ceux de ses peaux) et les armes sous la main droite, dont la prise se règle après
    /// l'animation (WeaponGrip).</summary>
    private static List<Transform> SkeletonOf(Transform model)
    {
        var set = new HashSet<Transform>();
        foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skin.rootBone != null) set.Add(skin.rootBone);
            foreach (Transform bone in skin.bones)
            {
                if (bone != null) set.Add(bone);
            }
        }
        Animator animator = model.GetComponent<Animator>();
        Transform hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (hand != null)
        {
            for (int i = 0; i < hand.childCount; i++) set.Add(hand.GetChild(i));
        }
        var list = new List<Transform>();
        foreach (Transform t in set)
        {
            if (t != model && t.IsChildOf(model)) list.Add(t);
        }
        return list;
    }

    private void OnShotShown(WeaponController weapon, Vector3 origin, IReadOnlyList<Vector3> ends)
    {
        if (playing || weapon == null || ends == null) return;
        var copy = new Vector3[Mathf.Min(ends.Count, MaxStreaks)];
        for (int i = 0; i < copy.Length; i++) copy[i] = ends[i];
        shots.Add(new ShownShot { time = Time.timeAsDouble, shooter = weapon.OwnerClientId, origin = origin, ends = copy });
    }

    // Le dernier tir de `shooter` montré entre une seconde avant la mort et juste après : le coup mortel.
    private bool TryFindShot(ulong shooter, double death, out ShownShot shot)
    {
        for (int i = shots.Count - 1; i >= 0; i--)
        {
            ShownShot candidate = shots[i];
            if (candidate.shooter != shooter || candidate.ends.Length == 0) continue;
            if (candidate.time > death + 0.15d) continue;
            if (candidate.time < death - 1d) break;
            shot = candidate;
            return true;
        }
        shot = default;
        return false;
    }

    // ------------------------------------------------------------------
    // Rejouer
    // ------------------------------------------------------------------

    private bool TryBegin(Info info)
    {
        if (!tracks.TryGetValue(info.killer, out Track k) || !tracks.TryGetValue(info.victim, out Track v)) return false;
        if (k == v || k.model == null || v.model == null || v.deathAt < 0d || k.recording.Count == 0 || v.recording.Count == 0) return false;
        // La mort à revoir est celle qu'on vient de voir : pas une d'avant (une vie plus tôt, une manche plus tôt).
        if (Time.timeAsDouble - v.deathAt > FreshDeath) return false;

        current = info;
        timeline = TimelineFor(info);
        killerTrack = k;
        victimTrack = v;
        deathAt = v.deathAt;
        ShownShot shot = default;
        hasShot = info.cause != Health.DamageCause.Glove && TryFindShot(info.killer, deathAt, out shot);
        shotAt = hasShot ? Math.Min(shot.time, deathAt) : deathAt;

        PlayerMatchState killerState = PlayerMatchState.Of(info.killer);
        WeaponController weapon = killerState != null ? killerState.GetComponentInChildren<WeaponController>() : null;
        killerWeapon = weapon != null ? weapon.DataOf(info.weaponSlot) : null;
        killerTaser = killerState != null ? killerState.GetComponentInChildren<TaserController>() : null;
        killerFeedback = killerState != null ? killerState.GetComponent<WeaponVisualFeedback>() : null;
        victimDeath = v.death;
        PlayerPaint paint = v.player != null ? v.player.GetComponent<PlayerPaint>() : null;
        victimPaint = paint != null ? (Color)paint.Paint : Color.clear;
        caption = Caption(PlayerMatchState.NameOf(info.killer), killerWeapon != null ? killerWeapon.weaponName : null,
                          info.cause, info.Distance);
        beam = killerWeapon != null && killerWeapon.shotIsBeam;

        killer = BuildPuppet(k, "Replay du tireur");
        victim = BuildPuppet(v, "Replay de la victime");

        // Le coup : de la bouche de l'arme que montre la marionnette au tir, vers chaque point d'arrivée ; la caméra suit
        // le plomb qui a touché (le plus près de la tête, ou du buste, de la victime à sa mort).
        pelletEnds.Clear();
        mainPellet = 0;
        bulletCam = false;
        killerTrack.recording.Pose(shotAt, killer.root.transform, killer.bones);
        victimTrack.recording.Pose(deathAt, victim.root.transform, victim.bones);
        Vector3 killerChest = PointOf(killer.chest, killer, 1.25f);
        Vector3 victimChest = PointOf(victim.chest, victim, 1.25f);
        if (hasShot)
        {
            muzzleStart = killer.muzzle != null ? killer.muzzle.position : shot.origin;
            Transform aimed = info.cause == Health.DamageCause.HeadShot && victim.head != null ? victim.head : victim.chest;
            Vector3 aim = aimed != null ? aimed.position : victimChest;
            float best = float.MaxValue;
            for (int i = 0; i < shot.ends.Length; i++)
            {
                pelletEnds.Add(shot.ends[i]);
                float d = (shot.ends[i] - aim).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                mainPellet = i;
            }
            impactPoint = pelletEnds[mainPellet];
            impactDirection = impactPoint - muzzleStart;
            bulletCam = timeline.style == Style.Bullet && Vector3.Distance(muzzleStart, impactPoint) >= ShortShot;
        }
        else
        {
            impactPoint = victimChest;
            impactDirection = victimChest - killerChest;
        }
        PrepareStreaks(beam ? (killerWeapon != null ? killerWeapon.tracerColor : Color.red) : BulletColor);

        EnsureCamera();
        CollectLiveRobots();
        fireCue = false;
        flybyCue = false;
        impactCue = false;
        burstShown = false;
        shownSegment = -1;
        return true;
    }

    private void Play(float t)
    {
        if (killer == null || victim == null)
        {
            Stop();
            return;
        }
        float clamped = Mathf.Clamp(t, 0f, timeline.duration);
        double source = SourceTime(clamped, timeline, shotAt, deathAt);
        killerTrack.recording.Pose(source, killer.root.transform, killer.bones);
        bool wreck = victimTrack.recording.Pose(source, victim.root.transform, victim.bones);
        if (wreck && !victim.deathShown)
        {
            // Hors service, comme l'épave : la visière affiche FATAL ERROR, les lueurs s'éteignent.
            victim.deathShown = true;
            RobotFaceScreen.ShowDeath(victim.root, Time.timeSinceLevelLoad);
        }
        dim = DimAt(t, timeline);
        if (t < 0f)
        {
            replayCamera.enabled = false;
            return;
        }
        PlayCues(t);
        UpdateBursts(t);
        UpdateStreaks(t);
        Frame(t);
        replayCamera.enabled = true;
    }

    // Le plan : l'approche, le vol de la balle, l'impact, l'épave.
    private int Segment(float t)
    {
        if (timeline.style == Style.Bullet)
        {
            if (t < timeline.shot + FlashCut) return 0;
            if (t < timeline.impact) return 1;
        }
        else if (t < timeline.shot) return 0;
        return t < timeline.impactEnd ? 2 : 3;
    }

    private void Frame(float t)
    {
        int segment = Segment(t);
        Shot shot;
        bool follow = false;    // la balle va vite : la caméra la suit sans retard
        switch (segment)
        {
            case 0:
                if (timeline.style == Style.Bullet)
                {
                    Vector3 shooterEye = PointOf(killer.head, killer, 1.6f);
                    shot = ShoulderShot(shooterEye, PointOf(victim.chest, victim, 1.25f), t / (timeline.shot + FlashCut));
                    shot.position = Unblock(shooterEye, shot.position);
                }
                else shot = Side(t / timeline.shot);
                break;
            case 1:
                if (bulletCam)
                {
                    shot = BulletShot(muzzleStart, pelletEnds[mainPellet], BulletTravel(FlightProgress(t, timeline)), beam, out _);
                    follow = true;
                }
                else shot = Side((t - timeline.shot) / (timeline.impact - timeline.shot));
                break;
            case 2:
                shot = ImpactShot(impactPoint, impactDirection, (t - timeline.impact) / (timeline.impactEnd - timeline.impact));
                shot.position = Unblock(impactPoint, shot.position);
                break;
            default:
                Vector3 hips = PointOf(victim.hips, victim, 0.9f);
                if (shownSegment != segment) smoothedTarget = hips;
                else smoothedTarget = Vector3.Lerp(smoothedTarget, hips, 1f - Mathf.Exp(-Time.unscaledDeltaTime / WreckSmoothing));
                shot = OrbitShot(smoothedTarget, killer.root.transform.position,
                                 (t - timeline.impactEnd) / (timeline.duration - timeline.impactEnd));
                shot.position = Unblock(smoothedTarget, shot.position);
                break;
        }

        if (segment != shownSegment || follow)
        {
            // Une coupe : le nouveau plan, d'un coup.
            smoothedPosition = shot.position;
            smoothedRotation = shot.rotation;
            shownSegment = segment;
        }
        else
        {
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / CameraSmoothing);
            smoothedPosition = Vector3.Lerp(smoothedPosition, shot.position, k);
            smoothedRotation = Quaternion.Slerp(smoothedRotation, shot.rotation, k);
        }
        replayCamera.fieldOfView = shot.fieldOfView;
        replayCamera.transform.SetPositionAndRotation(smoothedPosition, smoothedRotation);
    }

    // Les deux robots de côté.
    private Shot Side(float u)
    {
        Vector3 shooterChest = PointOf(killer.chest, killer, 1.25f);
        Vector3 victimChest = PointOf(victim.chest, victim, 1.25f);
        Shot shot = SideShot(shooterChest, victimChest, u);
        shot.position = Unblock((shooterChest + victimChest) * 0.5f, shot.position);
        return shot;
    }

    private static Vector3 PointOf(Transform bone, Puppet puppet, float height)
        => bone != null ? bone.position : puppet.root.transform.position + Vector3.up * height;

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

    /// <summary>Le son du coup rejoué, plus grave (le ralenti), et la flamme à la bouche de la marionnette ; la balle qui
    /// siffle ; puis l'impact, le robot qui grésille et s'éteint, ses pièces qui cognent. Chez ce joueur seul, à plat.</summary>
    private void PlayCues(float t)
    {
        if (!fireCue && t >= timeline.shot)
        {
            fireCue = true;
            if (current.cause == Health.DamageCause.Glove)
            {
                if (killerTaser != null) OneShotAudio.Play2D(Pick(killerTaser.ZapSounds), killerTaser.ZapVolume, SlowPitch);
            }
            else if (killerWeapon != null)
            {
                AudioClip clip = killerWeapon.fireFirstSound != null ? killerWeapon.fireFirstSound : Pick(killerWeapon.fireSounds);
                OneShotAudio.Play2D(clip, killerWeapon.fireVolume, SlowPitch);
            }
            if (hasShot && !beam && killerFeedback != null && killer.muzzle != null)
            {
                Vector3 forward = killer.weapon != null ? killer.weapon.forward : killer.muzzle.forward;
                killerFeedback.SpawnMuzzleFlash(killer.muzzle, forward, 1.2f, FlashHold);
            }
        }
        if (!flybyCue && bulletCam && t >= timeline.shot + FlashCut)
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
        }
    }

    private static void Play2D(SoundSet set, float pitch)
    {
        if (set != null && !set.IsEmpty) OneShotAudio.Play2D(set.Pick(), set.volume, pitch);
    }

    private static AudioClip Pick(IReadOnlyList<AudioClip> clips)
        => clips == null || clips.Count == 0 ? null : clips[UnityEngine.Random.Range(0, clips.Count)];

    // ------------------------------------------------------------------
    // Le robot qui vole en pièces, au ralenti
    // ------------------------------------------------------------------

    private void UpdateBursts(float t)
    {
        if (!burstShown && t >= timeline.impact)
        {
            burstShown = true;
            SpawnBursts();
        }
        float rate = WarpRate(t, timeline, shotAt, deathAt);
        for (int i = 0; i < bursts.Count; i++) RobotBurst.SetSpeed(bursts[i], rate);
    }

    /// <summary>La gerbe du coup telle qu'elle est partie de l'épave sur cette machine (son point, sa force, les mêmes
    /// pièces arrachées), sur les marionnettes et le layer Preview : seule la caméra du replay la voit.</summary>
    private void SpawnBursts()
    {
        int layer = PreviewLayer.Index >= 0 ? PreviewLayer.Index : 0;
        RobotCorpse wreck = victimTrack.wreck;
        RobotCorpse.BurstSeverity severity = RobotCorpse.BurstSeverity.Light;
        Color paint = victimPaint;
        if (wreck != null && wreck.HasBurst)
        {
            impactPoint = wreck.BurstPoint;
            if (wreck.BurstDirection.sqrMagnitude > 1e-6f) impactDirection = wreck.BurstDirection;
            severity = wreck.Severity;
            paint = wreck.BurstPaint;
        }
        bursts.Add(RobotBurst.Impact(transform, impactPoint, impactDirection, severity, paint, layer, 0f));
        if (wreck == null || victimTrack.wreckBones == null) return;
        foreach (Transform piece in wreck.Detached)
        {
            int index = Array.IndexOf(victimTrack.wreckBones, piece);
            Transform twin = index >= 0 && index < victim.bones.Length ? victim.bones[index] : null;
            if (twin == null || twin.parent == null) continue;
            Vector3 stump = twin.position;
            bursts.Add(RobotBurst.Stump(twin.parent, stump, stump - twin.parent.position, severity, layer, 0f));
            bursts.Add(RobotBurst.Trail(twin, stump, layer, 0f));
        }
    }

    // ------------------------------------------------------------------
    // La balle (ou le rayon) au ralenti
    // ------------------------------------------------------------------

    private void EnsureStreakMaterial()
    {
        if (streakMaterial != null) return;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        streakMaterial = new Material(shader) { name = "Balle du replay" };
    }

    private LineRenderer NewLine(string name, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        if (PreviewLayer.Index >= 0) go.layer = PreviewLayer.Index;
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
        EnsureStreakMaterial();
        streakMaterial.color = color;
        while (streaks.Count < Mathf.Max(1, pelletEnds.Count)) streaks.Add(NewLine("Balle du replay", streakMaterial));
        float width = beam ? BeamWidth : BulletWidth;
        foreach (LineRenderer line in streaks)
        {
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = beam ? width : width * 0.35f;
            line.enabled = false;
        }
        if (wake == null)
        {
            // Une lueur ajoutée (RobotBurst.WakeMaterial) : elle s'éteint vers le noir, sans transparence.
            wake = NewLine("Sillage du replay", RobotBurst.WakeMaterial);
            wake.startColor = WakeColor;
            wake.endColor = Color.black;
            wake.startWidth = WakeWidth;
            wake.endWidth = WakeWidth * 0.25f;
        }
        wake.enabled = false;
    }

    private void UpdateStreaks(float t)
    {
        // Le coup qui part au ralenti : la balle suivie, ou le rayon d'une arme laser (de près, d'un coup).
        bool flying = hasShot && t >= timeline.shot && t < timeline.impact + (beam ? BeamLinger : 0f)
                      && (timeline.style == Style.Bullet || beam);
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
    // Les marionnettes
    // ------------------------------------------------------------------

    /// <summary>Une copie du robot de `track`, telle qu'on le voit (son châssis, sa peinture, son arme, son gant) : sans
    /// script, sans Animator ni collider, sur le layer Preview, que seule la caméra du replay dessine.</summary>
    private Puppet BuildPuppet(Track track, string name)
    {
        Transform live = track.model;
        var holder = new GameObject(name + " (préparation)");
        holder.SetActive(false);
        GameObject copy = Instantiate(live.gameObject, live.position, live.rotation, holder.transform);
        copy.name = name;
        RobotCorpse.StripToAppearance(copy);
        int layer = PreviewLayer.Index;
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
        {
            if (layer >= 0) t.gameObject.layer = layer;
        }
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
        {
            // Le corps du propriétaire n'est qu'une ombre pour lui, celui du mort est masqué : la marionnette, non.
            r.shadowCastingMode = ShadowCastingMode.On;
            r.forceRenderingOff = false;
            if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        }
        CopyLook(live, copy.transform);
        copy.transform.SetParent(transform, true);
        Destroy(holder);

        var puppet = new Puppet { root = copy, bones = Resolve(copy.transform, track.paths) };
        Animator animator = live.GetComponent<Animator>();
        if (animator != null && animator.isHuman)
        {
            puppet.head = Twin(live, animator.GetBoneTransform(HumanBodyBones.Head), copy.transform);
            Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            puppet.chest = Twin(live, chest, copy.transform);
            puppet.hips = Twin(live, animator.GetBoneTransform(HumanBodyBones.Hips), copy.transform);
            WeaponModel held = HeldWeapons.Visible(animator.GetBoneTransform(HumanBodyBones.RightHand));
            if (held != null)
            {
                puppet.weapon = Twin(live, held.transform, copy.transform);
                puppet.muzzle = Twin(live, AttachmentMount.MuzzleOf(held), copy.transform);
            }
        }
        return puppet;
    }

    /// <summary>Ce qu'un MaterialPropertyBlock pose sur le robot (sa peinture, ses lueurs) : il ne se copie pas avec
    /// l'objet.</summary>
    private static void CopyLook(Transform live, Transform copy)
    {
        var block = new MaterialPropertyBlock();
        foreach (Renderer r in live.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.HasPropertyBlock()) continue;
            Transform twin = Twin(live, r.transform, copy);
            Renderer target = twin != null ? twin.GetComponent<Renderer>() : null;
            if (target == null) continue;
            r.GetPropertyBlock(block);
            if (!block.isEmpty) target.SetPropertyBlock(block);
            int materials = r.sharedMaterials.Length;
            for (int i = 0; i < materials; i++)
            {
                r.GetPropertyBlock(block, i);
                if (!block.isEmpty) target.SetPropertyBlock(block, i);
            }
        }
    }

    private static void DestroyPuppet(ref Puppet puppet)
    {
        if (puppet != null && puppet.root != null) Destroy(puppet.root);
        puppet = null;
    }

    private void Stop()
    {
        playing = false;
        dim = 0f;
        if (replayCamera != null) replayCamera.enabled = false;
        RestoreLiveRobots();
        liveRenderers.Clear();
        DestroyPuppet(ref killer);
        DestroyPuppet(ref victim);
        for (int i = 0; i < bursts.Count; i++)
        {
            if (bursts[i] != null) Destroy(bursts[i]);
        }
        bursts.Clear();
        foreach (LineRenderer line in streaks)
        {
            if (line != null) line.enabled = false;
        }
        if (wake != null) wake.enabled = false;
        killerTrack = null;
        victimTrack = null;
        killerWeapon = null;
        killerTaser = null;
        killerFeedback = null;
        victimDeath = null;
        // L'enregistrement repart de rien : ce qui précède le replay ne servira plus, et une mort d'avant non plus.
        foreach (Track track in tracks.Values)
        {
            track.recording.Clear();
            track.deathAt = -1d;
        }
        shots.Clear();
    }

    // ------------------------------------------------------------------
    // La caméra du replay
    // ------------------------------------------------------------------

    private void EnsureCamera()
    {
        if (replayCamera != null) return;
        var go = new GameObject("Caméra du replay");
        go.transform.SetParent(transform, false);
        replayCamera = go.AddComponent<Camera>();
        // Le décor, les marionnettes, la balle et les gerbes ; ni les bras de la vue subjective, ni les surfaces
        // touchables, ni les épaves et leurs gerbes (les marionnettes et les leurs en tiennent lieu).
        int hidden = LayerBit("Viewmodel") | LayerBit("Hitbox") | LayerBit("Player") | CorpseLayer.Mask;
        replayCamera.cullingMask = ~hidden | (PreviewLayer.Index >= 0 ? 1 << PreviewLayer.Index : 0);
        replayCamera.clearFlags = CameraClearFlags.Skybox;
        replayCamera.nearClipPlane = 0.05f;
        replayCamera.farClipPlane = 1000f;
        // Par-dessus la caméra du joueur, qui reste en place (son AudioListener aussi), et celle de l'écran de fin.
        replayCamera.depth = 60f;
        replayCamera.enabled = false;
        UniversalAdditionalCameraData data = replayCamera.GetUniversalAdditionalCameraData();
        data.renderShadows = true;
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
    }

    private static int LayerBit(string name)
    {
        int layer = LayerMask.NameToLayer(name);
        return layer >= 0 ? 1 << layer : 0;
    }

    private void CollectLiveRobots()
    {
        liveRenderers.Clear();
        IReadOnlyList<PlayerLocomotion> players = PlayerLocomotion.SpawnedPlayers;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] != null) liveRenderers.AddRange(players[i].GetComponentsInChildren<Renderer>(true));
        }
    }

    // Le temps du rendu de la caméra du replay : les vrais robots masqués, le décor assombri.
    private void BeginReplayRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera != replayCamera || camera == null) return;
        Shader.SetGlobalFloat(ReplayDimId, dim);
        hiddenNow.Clear();
        foreach (Renderer r in liveRenderers)
        {
            if (r == null || r.forceRenderingOff) continue;
            r.forceRenderingOff = true;
            hiddenNow.Add(r);
        }
    }

    private void EndReplayRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera != replayCamera || camera == null) return;
        Shader.SetGlobalFloat(ReplayDimId, 0f);
        RestoreLiveRobots();
    }

    private void RestoreLiveRobots()
    {
        foreach (Renderer r in hiddenNow)
        {
            if (r != null) r.forceRenderingOff = false;
        }
        hiddenNow.Clear();
    }

    // ------------------------------------------------------------------
    // Chemins sous le modèle
    // ------------------------------------------------------------------

    private static Transform[] Resolve(Transform root, string[] paths)
    {
        var result = new Transform[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            string path = paths[i];
            result[i] = path == null ? null : path.Length == 0 ? root : root.Find(path);
        }
        return result;
    }

    // Le chemin de `target` sous `root` (« mixamorig:Hips/mixamorig:Spine »), vide pour la racine, null hors de lui.
    private static string RelativePath(Transform root, Transform target)
    {
        if (target == root) return "";
        string path = target.name;
        for (Transform p = target.parent; p != null; p = p.parent)
        {
            if (p == root) return path;
            path = p.name + "/" + path;
        }
        return null;
    }

    private static Transform Twin(Transform liveRoot, Transform target, Transform copyRoot)
    {
        if (target == null) return null;
        string path = RelativePath(liveRoot, target);
        return path == null ? null : path.Length == 0 ? copyRoot : copyRoot.Find(path);
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Surface de test : le replay est-il en train de jouer, sa caméra, son déroulé.</summary>
    public bool TestPlaying => playing;
    public Camera TestCamera => replayCamera;
    public bool TestBulletCam => bulletCam;
    public bool TestHasShot => hasShot;
    public Timeline TestTimeline => timeline;
    public int TestBursts => bursts.Count;
#endif
}
