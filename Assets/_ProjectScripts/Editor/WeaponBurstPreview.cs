using System.Collections.Generic;
using UnityEngine;

/// <summary>Le déplacement du tireur dans l'aperçu de l'éditeur des armes.</summary>
public enum PreviewMovement { Still, Sneak, Moving }

/// <summary>
/// La silhouette d'un robot debout, vue de face, telle que le serveur la touche (BodyLayout, sur les
/// mesures du préfab joueur) : en mètres, x vers la droite du tireur, y depuis les pieds.
/// </summary>
public struct TargetShape
{
    public ZoneShape head, torso, legs;

    /// <summary>La surface touchable d'un robot dans la posture `body`, œil en `eye`, sans lean, tournée
    /// vers le tireur (sa droite est la gauche du tireur).</summary>
    public static TargetShape Facing(in StanceBody body, Vector3 eye)
    {
        BodyLayout.Compute(body, eye, 0f, out ZoneShape head, out ZoneShape torso, out ZoneShape legs, out _);
        return new TargetShape { head = Mirror(head), torso = Mirror(torso), legs = Mirror(legs) };
    }

    private static ZoneShape Mirror(ZoneShape zone)
    {
        zone.a.x = -zone.a.x;
        zone.b.x = -zone.b.x;
        return zone;
    }

    /// <summary>Le point visé au premier coup : le milieu du buste, ou l'œil.</summary>
    public Vector2 AimPoint(bool atHead) => atHead ? (Vector2)head.a : (Vector2)(torso.a + torso.b) * 0.5f;

    /// <summary>La zone sous `point` (mètres, dans le plan de la cible), la tête d'abord ; null à côté.</summary>
    public HitboxZoneType? ZoneAt(Vector2 point)
    {
        if (Inside(head, point)) return HitboxZoneType.Head;
        if (Inside(torso, point)) return HitboxZoneType.Torso;
        if (Inside(legs, point)) return HitboxZoneType.Legs;
        return null;
    }

    private static bool Inside(in ZoneShape zone, Vector2 point)
    {
        if (zone.radius <= 0f) return false;
        Vector2 a = zone.a, b = zone.b, ab = b - a;
        float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
        return (point - (a + ab * t)).sqrMagnitude <= zone.radius * zone.radius;
    }
}

/// <summary>Ce qu'il faut pour rejouer une rafale : le tireur, sa cible, et ce que le jeu en dit.</summary>
public struct BurstSetup
{
    public int shots;
    /// <summary>Ce que la posture, le déplacement, la visée et la poignée verticale laissent de la
    /// poussée du motif (x horizontal, y vertical).</summary>
    public Vector2 recoilScale;
    public bool aiming;
    public PlayerLocomotion.Stance stance;
    /// <summary>L'allure du tireur (m/s), qui ouvre le cône à la hanche.</summary>
    public float speed;
    /// <summary>Ce que le laser tactique laisse de la dispersion à la hanche (1 sans laser).</summary>
    public float laserFactor;
    public TargetShape target;
    public bool aimAtHead;
    public float distance;
    public float maxHealth;
}

/// <summary>
/// L'aperçu d'une rafale dans l'éditeur des armes (2026-10-26) : où part chaque coup tiré au plus vite
/// sans compenser, avec le motif de recul et son retour (RecoilPattern), le cône à la hanche et son
/// hasard (HipFireSpread : celui du premier joueur, coup par coup), la gerbe, et ce que la rafale fait à
/// un robot debout à `distance` mètres (WeaponData.DamageAt, comme le serveur). Les mêmes fonctions que
/// le jeu : l'aperçu ne ment pas, aux images près. Pur, testé (RecoilPatternTests).
/// </summary>
public sealed class BurstPreview
{
    public struct Bullet
    {
        public int shot;
        /// <summary>Où part la balle (ou le plomb), en degrés depuis l'axe du premier coup.</summary>
        public Vector2 degrees;
        public HitboxZoneType? zone;
    }

    public int Shots { get; private set; }
    public float Interval { get; private set; }
    public bool Chains { get; private set; }
    /// <summary>Le regard au moment de chaque coup (degrés), le retour compris.</summary>
    public readonly List<Vector2> aim = new List<Vector2>();
    /// <summary>Le demi-angle du cône à la hanche à chaque coup (degrés).</summary>
    public readonly List<float> cones = new List<float>();
    public readonly List<Bullet> bullets = new List<Bullet>();
    public int HeadHits { get; private set; }
    public int TorsoHits { get; private set; }
    public int LegHits { get; private set; }
    public int Misses { get; private set; }
    /// <summary>Les dégâts de toute la rafale, comptés comme le serveur.</summary>
    public float Damage { get; private set; }
    /// <summary>Le coup (depuis 0) qui neutralise le robot, -1 si la rafale n'y suffit pas.</summary>
    public int KillShot { get; private set; } = -1;
    /// <summary>Le temps du premier appui au coup qui neutralise (s).</summary>
    public float KillTime { get; private set; }

    public void Compute(WeaponData data, in BurstSetup setup)
    {
        aim.Clear();
        cones.Clear();
        bullets.Clear();
        HeadHits = TorsoHits = LegHits = Misses = 0;
        Damage = 0f;
        KillShot = -1;
        KillTime = 0f;
        Shots = data != null ? Mathf.Max(0, setup.shots) : 0;
        Interval = RecoilPattern.ShotInterval(data);
        Chains = RecoilPattern.Chains(data);
        if (Shots == 0) return;

        RecoilPattern.Burst(data, Shots, setup.recoilScale, true, aim);

        Vector2 aimPoint = setup.target.AimPoint(setup.aimAtHead);
        float distance = Mathf.Max(0.1f, setup.distance);
        float dealt = 0f;
        float bloomAfter = 0f;
        for (int shot = 0; shot < Shots; shot++)
        {
            float bloom = shot == 0 ? 0f : HipFireSpread.BloomAt(bloomAfter, 0.0, Interval, data.hipSpreadRecovery);
            bloomAfter = HipFireSpread.BloomAfterShot(data, bloom);
            float cone = HipFireSpread.Cone(data, setup.speed, setup.stance, setup.aiming ? 1f : 0f, bloom, setup.laserFactor);
            cones.Add(cone);
            Vector2 axis = aim[shot] + HipFireSpread.Offset(cone, HipFireSpread.Seed(0ul, shot + 1));

            float shotDamage = 0f;
            for (int pellet = 0; pellet < data.PelletCount; pellet++)
            {
                Vector2 degrees = axis + data.PelletOffset(pellet, setup.aiming, setup.laserFactor);
                Vector2 onTarget = aimPoint + new Vector2(Mathf.Tan(degrees.x * Mathf.Deg2Rad), Mathf.Tan(degrees.y * Mathf.Deg2Rad)) * distance;
                HitboxZoneType? zone = setup.target.ZoneAt(onTarget);
                bullets.Add(new Bullet { shot = shot, degrees = degrees, zone = zone });
                switch (zone)
                {
                    case HitboxZoneType.Head: HeadHits++; break;
                    case HitboxZoneType.Torso: TorsoHits++; break;
                    case HitboxZoneType.Legs: LegHits++; break;
                    default: Misses++; break;
                }
                if (zone.HasValue) shotDamage += data.DamageAt(zone.Value, distance);
            }

            dealt += shotDamage;
            Damage = dealt;
            if (KillShot < 0 && setup.maxHealth > 0f && dealt >= setup.maxHealth - 1e-4f)
            {
                KillShot = shot;
                KillTime = (data.UsesCharge ? data.chargeTime : 0f) + shot * Interval;
            }
        }
    }

    /// <summary>Un angle (degrés) vu à `distance` mètres, en mètres dans le plan de la cible.</summary>
    public static float Meters(float degrees, float distance) => Mathf.Tan(degrees * Mathf.Deg2Rad) * distance;

    /// <summary>Une longueur (mètres) dans le plan de la cible à `distance` mètres, en degrés.</summary>
    public static float Degrees(float meters, float distance) => Mathf.Atan2(meters, Mathf.Max(0.1f, distance)) * Mathf.Rad2Deg;
}
