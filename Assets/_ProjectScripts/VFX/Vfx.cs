using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Sur quoi une balle est tombée : le décor (poussière, trace) ou un robot (étincelles
/// de métal, crépitement électrique — un robot ne saigne pas, GDD § 6).</summary>
public enum ImpactSurface { World, Chassis }

/// <summary>
/// Point d'entrée unique des effets visuels du jeu. Tout est construit en code au premier usage,
/// puis recyclé : aucun prefab d'effet à préparer à la main, aucune allocation par tir.
///
/// Les effets sont de catégorie C (CLAUDE.md) : purement locaux, sans collider, jamais relus par
/// la simulation. Chaque machine les déclenche elle-même à partir d'événements qui existent déjà
/// (le tir local, BroadcastShotClientRpc, la vie réseautée).
///
/// Textures : sélection du Kenney Particle Pack (CC0), converties en blanc + alpha pour être
/// teintées par la couleur de particule (voir Docs/Credits-Assets.md). Réglages dans
/// <see cref="VfxLibrary"/>.
/// </summary>
public static class Vfx
{
    // ================================================================== API

    /// <summary>Impact de balle : étincelles, poussière et trace sur le décor ; gerbe d'étincelles
    /// et crépitement sur un robot. Joue aussi le son d'impact au point touché.
    /// <paramref name="decal"/> : poser une trace (seulement si une surface du décor est vraiment là).</summary>
    public static void BulletImpact(Vector3 point, Vector3 normal, ImpactSurface surface, bool decal = true)
    {
        var lib = VfxLibrary.Instance;
        if (lib == null) return;
        Quaternion facing = Facing(normal);

        if (surface == ImpactSurface.Chassis)
        {
            Spawn(Kind.SparksMetal, point, facing);
            Spawn(Kind.ImpactFlash, point + normal * 0.02f, facing);
            if (Random.value < 0.6f) Spawn(Kind.CrackleSmall, point, facing);
            Sfx.PlayAt(lib.impactChassis, point, lib.impactVolume, lib.impactMinDistance, lib.impactMaxDistance);
        }
        else
        {
            Spawn(Kind.Dust, point, facing);
            Spawn(Kind.SparksConcrete, point, facing);
            if (decal) VfxRunner.Instance.ScorchMark(point, normal, Random.Range(0.09f, 0.14f), lib.scorch, lib.scorchColor, lib.scorchLifetime, lib.maxScorchMarks);
            Sfx.PlayAt(lib.impactWorld, point, lib.impactVolume * 0.85f, lib.impactMinDistance, lib.impactMaxDistance);
        }
    }

    /// <summary>Flash de bouche. En vue à la première personne, seule la lumière est émise : le
    /// sprite apparaîtrait dans l'oeil du tireur, qui n'a pas encore d'arme visible.</summary>
    public static void MuzzleFlash(Vector3 position, Vector3 direction, bool firstPerson)
    {
        var lib = VfxLibrary.Instance;
        if (lib == null) return;
        if (!firstPerson) Spawn(Kind.Muzzle, position, Facing(direction));
        LightFlash(position, lib.muzzleColor, firstPerson ? 1.4f : 2.6f, firstPerson ? 4f : 5.5f, 0.05f, 0.3f);
    }

    /// <summary>Balle traçante qui part de <paramref name="start"/> et file jusqu'à <paramref name="end"/>.</summary>
    public static void Tracer(Vector3 start, Vector3 end)
    {
        var lib = VfxLibrary.Instance;
        if (lib == null || lib.beam == null) return;
        if ((end - start).sqrMagnitude < 0.04f) return;
        VfxRunner.Instance.Tracer(start, end, lib.beam, lib.tracerColor);
    }

    /// <summary>Décharge de taser sur un châssis : gerbe d'arcs qui rampent sur la surface,
    /// étincelles bleu-blanc, éclair lumineux, filet de fumée, et le son de décharge.</summary>
    public static void ElectricHit(Vector3 point, Vector3 normal, bool playSound = true)
    {
        var lib = VfxLibrary.Instance;
        if (lib == null) return;
        Quaternion facing = Facing(normal);
        Spawn(Kind.ElectricBurst, point, facing);
        VfxRunner.Instance.ArcBursts(point, normal, 0.28f, 0.35f, 3, lib.beam, lib.electricCore, 0.012f);
        LightFlash(point + normal * 0.1f, lib.electricGlow, 5f, 4.5f, 0.28f, 0.7f);
        if (playSound) Sfx.PlayAt(lib.electricHit, point, lib.electricVolume, 3f, 40f, 0.04f);
    }

    /// <summary>Petite décharge à vide (taser qui frappe dans l'air, électrodes qui crépitent).</summary>
    public static void ElectricSpark(Vector3 point, Vector3 normal, bool playSound = false)
    {
        var lib = VfxLibrary.Instance;
        if (lib == null) return;
        Spawn(Kind.CrackleSmall, point, Facing(normal));
        LightFlash(point, lib.electricGlow, 1.6f, 2.5f, 0.08f, 0.8f);
        if (playSound) Sfx.PlayAt(lib.electricZap, point, lib.electricVolume * 0.8f, 2f, 25f, 0.06f);
    }

    /// <summary>Destruction d'un robot : court-circuit, gerbe d'étincelles, fumée sombre et son
    /// signature (GDD § 6). <paramref name="center"/> = le torse.</summary>
    public static void DeathBurst(Vector3 center)
    {
        var lib = VfxLibrary.Instance;
        if (lib == null) return;
        Spawn(Kind.DeathSparks, center, Quaternion.identity);
        Spawn(Kind.ElectricBurst, center, Quaternion.identity);
        Spawn(Kind.DeathSmoke, center, Quaternion.identity);
        VfxRunner.Instance.ArcBursts(center, Vector3.up, 0.55f, 0.6f, 4, lib.beam, lib.electricCore, 0.016f);
        LightFlash(center, lib.electricGlow, 6f, 6f, 0.4f, 0.6f);
        Sfx.PlayAt(lib.deathShutdown, center, lib.deathVolume, 4f, 60f, 0.02f);
    }

    public static void LightFlash(Vector3 position, Color color, float intensity, float range, float duration, float flicker = 0f)
    {
        var lib = VfxLibrary.Instance;
        int max = lib != null ? lib.maxFlashLights : 4;
        VfxRunner.Instance.LightFlash(position, color, intensity, range, duration, flicker, max);
    }

    /// <summary>Efface les traces d'impact. À appeler pendant le fondu au noir entre deux manches.</summary>
    public static void ClearPersistent()
    {
        VfxRunner.Instance.ClearMarks();
    }

    /// <summary>Vrai si <paramref name="origin"/> est l'oeil de la caméra active de cette machine :
    /// c'est alors un tir du joueur local, vu à la première personne.</summary>
    public static bool IsLocalViewOrigin(Vector3 origin)
    {
        Camera cam = Camera.main;
        return cam != null && (cam.transform.position - origin).sqrMagnitude < 0.3f * 0.3f;
    }

    // ================================================================== pool

    public enum Kind
    {
        SparksMetal,
        SparksConcrete,
        Dust,
        ImpactFlash,
        CrackleSmall,
        ElectricBurst,
        Muzzle,
        DeathSparks,
        DeathSmoke,
    }

    public class PooledEffect
    {
        public Kind kind;
        public GameObject root;
        public ParticleSystem[] systems;
        public float lifetime;
    }

    private static Dictionary<Kind, Stack<PooledEffect>> pools;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        pools = null;
        quadMesh = null;
    }

    private static void Spawn(Kind kind, Vector3 position, Quaternion rotation)
    {
        var lib = VfxLibrary.Instance;
        if (lib == null) return;
        pools ??= new Dictionary<Kind, Stack<PooledEffect>>();
        if (!pools.TryGetValue(kind, out var stack))
        {
            stack = new Stack<PooledEffect>();
            pools[kind] = stack;
        }

        PooledEffect fx = null;
        while (stack.Count > 0 && fx == null)
        {
            fx = stack.Pop();
            if (fx.root == null) fx = null; // détruit hors de notre contrôle (sortie de Play Mode)
        }
        fx ??= Build(kind, lib);

        fx.root.transform.SetPositionAndRotation(position, rotation);
        fx.root.SetActive(true);
        foreach (var ps in fx.systems)
        {
            ps.Clear(false);
            ps.Play(false);
        }
        VfxRunner.Instance.Release(fx, fx.lifetime);
    }

    public static void Return(PooledEffect fx)
    {
        if (fx == null || fx.root == null) return;
        foreach (var ps in fx.systems) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        fx.root.SetActive(false);
        pools ??= new Dictionary<Kind, Stack<PooledEffect>>();
        if (!pools.TryGetValue(fx.kind, out var stack))
        {
            stack = new Stack<PooledEffect>();
            pools[fx.kind] = stack;
        }
        stack.Push(fx);
    }

    // ================================================================== recettes

    private static PooledEffect Build(Kind kind, VfxLibrary lib)
    {
        var root = new GameObject("Vfx_" + kind);
        root.transform.SetParent(VfxRunner.Root, false);
        var systems = new List<ParticleSystem>();
        float life;

        switch (kind)
        {
            case Kind.SparksMetal:
                // Balle sur un châssis : gerbe vive, orange-blanc, qui retombe.
                systems.Add(Sparks(root, lib, 10, 16, 3.5f, 9f, 0.12f, 0.32f, 45f, 1.3f, lib.sparkHot, lib.sparkCool));
                life = 0.5f;
                break;

            case Kind.SparksConcrete:
                // Sur le béton, quelques étincelles seulement : la poussière domine.
                systems.Add(Sparks(root, lib, 2, 6, 2.5f, 6f, 0.06f, 0.18f, 55f, 1.1f, lib.sparkHot, lib.sparkCool));
                life = 0.3f;
                break;

            case Kind.Dust:
                systems.Add(Dust(root, lib));
                life = 1.3f;
                break;

            case Kind.ImpactFlash:
                systems.Add(Flash(root, lib.star, 0.16f, 0.26f, 0.04f, lib.sparkHot));
                life = 0.1f;
                break;

            case Kind.CrackleSmall:
                systems.Add(Crackle(root, lib, 1, 3, 0.08f, 0.2f, 0.04f, 0.1f, 0f));
                life = 0.2f;
                break;

            case Kind.ElectricBurst:
            {
                // Plusieurs salves de craquelures sur 0,25 s : c'est le clignotement qui fait
                // l'électricité, pas la taille.
                var crackle = Crackle(root, lib, 2, 3, 0.18f, 0.5f, 0.05f, 0.14f, 0.08f);
                var em = crackle.emission;
                em.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0f, 3, 4),
                    new ParticleSystem.Burst(0.06f, 2, 3),
                    new ParticleSystem.Burst(0.13f, 2, 3),
                    new ParticleSystem.Burst(0.21f, 1, 2),
                });
                systems.Add(crackle);
                systems.Add(Sparks(root, lib, 14, 22, 2f, 7f, 0.15f, 0.4f, 70f, 0.6f, lib.electricCore, lib.electricGlow));
                systems.Add(Flash(root, lib.glow, 0.7f, 0.9f, 0.08f, lib.electricGlow));
                systems.Add(Smoke(root, lib, 1, 2, 0.15f, 0.45f, 0.8f, 1.3f, -0.04f, lib.smokeColor));
                life = 1.4f;
                break;
            }

            case Kind.Muzzle:
            {
                var flash = Flash(root, lib.muzzle, 0.22f, 0.34f, 0.045f, lib.muzzleColor);
                var tsa = flash.textureSheetAnimation;
                SetupAtlas(tsa);
                systems.Add(flash);
                systems.Add(Flash(root, lib.star, 0.3f, 0.4f, 0.03f, lib.sparkHot));
                systems.Add(Sparks(root, lib, 2, 5, 6f, 12f, 0.04f, 0.09f, 10f, 0.2f, lib.sparkHot, lib.sparkCool));
                systems.Add(Smoke(root, lib, 1, 1, 0.06f, 0.25f, 0.35f, 0.55f, -0.02f, new Color(lib.smokeColor.r, lib.smokeColor.g, lib.smokeColor.b, 0.25f)));
                life = 0.6f;
                break;
            }

            case Kind.DeathSparks:
            {
                var s = Sparks(root, lib, 28, 40, 2f, 8f, 0.25f, 0.7f, 180f, 1.5f, lib.sparkHot, lib.sparkCool);
                var shape = s.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.25f;
                systems.Add(s);
                systems.Add(Flash(root, lib.glow, 1.4f, 1.8f, 0.12f, lib.electricCore));
                life = 1f;
                break;
            }

            case Kind.DeathSmoke:
            {
                var s = Smoke(root, lib, 6, 8, 0.35f, 1.1f, 1.6f, 2.6f, -0.08f, lib.smokeColor);
                var shape = s.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.3f;
                systems.Add(s);
                life = 2.8f;
                break;
            }

            default:
                life = 0.1f;
                break;
        }

        return new PooledEffect { kind = kind, root = root, systems = systems.ToArray(), lifetime = life };
    }

    // ------------------------------------------------------------------ briques

    private static ParticleSystem NewSystem(GameObject parent, string name, Material material, int maxParticles)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        // Un ParticleSystem tout neuf démarre avec ses réglages par défaut : on l'arrête et on le
        // vide AVANT de le configurer (certains réglages refusent d'être changés en cours de lecture).
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.3f;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startRotation = 0f;
        // Même horloge que VfxRunner, qui recycle l'effet : sans ça, un ralenti (killcam)
        // couperait les particules avant la fin de leur vie.
        main.useUnscaledTime = true;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 30f;
        shape.radius = 0.01f;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = material;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.sortMode = ParticleSystemSortMode.None;
        // Une particule ne doit jamais remplir l'écran, même tout contre la caméra : c'est la
        // règle « aucun effet ne masque l'adversaire » de la bible (§ 17).
        r.maxParticleSize = 0.25f;
        return ps;
    }

    private static void Burst(ParticleSystem ps, int min, int max)
    {
        var em = ps.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)min, (short)max) });
    }

    private static ParticleSystem.MinMaxGradient FadeOut(Color from, Color to)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        return new ParticleSystem.MinMaxGradient(g);
    }

    private static void SetupAtlas(ParticleSystem.TextureSheetAnimationModule tsa)
    {
        tsa.enabled = true;
        tsa.mode = ParticleSystemAnimationMode.Grid;
        tsa.numTilesX = 2;
        tsa.numTilesY = 2;
        tsa.animation = ParticleSystemAnimationType.WholeSheet;
        // Deux constantes = une case tirée au hasard par particule, gardée toute sa vie.
        tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);
        tsa.cycleCount = 1;
    }

    /// <summary>Étincelles : le point lumineux doux, étiré par sa vitesse, fait une traînée.</summary>
    private static ParticleSystem Sparks(GameObject parent, VfxLibrary lib, int min, int max, float speedMin, float speedMax,
        float lifeMin, float lifeMax, float coneAngle, float gravity, Color hot, Color cool)
    {
        var ps = NewSystem(parent, "Sparks", lib.glow, 64);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
        main.startColor = new ParticleSystem.MinMaxGradient(hot, Color.Lerp(hot, cool, 0.5f));
        main.gravityModifier = gravity;
        Burst(ps, min, max);

        var shape = ps.shape;
        shape.angle = coneAngle;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeOut(Color.white, cool);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.35f));

        // Freinage léger : sans lui, les étincelles partent en ligne droite comme des balles.
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 30f;
        limit.dampen = 0.08f;

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.022f;
        r.lengthScale = 1.2f;
        return ps;
    }

    private static ParticleSystem Crackle(GameObject parent, VfxLibrary lib, int min, int max, float sizeMin, float sizeMax,
        float lifeMin, float lifeMax, float radius)
    {
        var ps = NewSystem(parent, "Crackle", lib.crackle, 24);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(lib.electricCore, lib.electricGlow);
        Burst(ps, min, max);

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Mathf.Max(0.01f, radius);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeOut(Color.white, lib.electricGlow);

        SetupAtlas(ps.textureSheetAnimation);
        ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 0.35f;
        return ps;
    }

    private static ParticleSystem Flash(GameObject parent, Material material, float sizeMin, float sizeMax, float life, Color color)
    {
        var ps = NewSystem(parent, "Flash", material, 4);
        var main = ps.main;
        main.startLifetime = life;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = color;
        Burst(ps, 1, 1);

        var shape = ps.shape;
        shape.enabled = false;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeOut(Color.white, Color.white);
        ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 0.4f;
        return ps;
    }

    private static ParticleSystem Dust(GameObject parent, VfxLibrary lib)
    {
        var ps = NewSystem(parent, "Dust", lib.smoke, 12);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = lib.dustColor;
        main.gravityModifier = 0.06f;
        Burst(ps, 3, 5);

        var shape = ps.shape;
        shape.angle = 25f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeOut(Color.white, Color.white);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 2.6f));

        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0.25f;
        limit.dampen = 0.2f;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);

        SetupAtlas(ps.textureSheetAnimation);
        ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 0.3f;
        return ps;
    }

    private static ParticleSystem Smoke(GameObject parent, VfxLibrary lib, int min, int max, float sizeMin, float sizeMax,
        float lifeMin, float lifeMax, float gravity, Color color)
    {
        var ps = NewSystem(parent, "Smoke", lib.smoke, 16);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = color;
        main.gravityModifier = gravity;
        Burst(ps, min, max);

        var shape = ps.shape;
        shape.angle = 20f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeOut(Color.white, Color.white);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 2.2f));

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

        SetupAtlas(ps.textureSheetAnimation);
        ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 0.35f;
        return ps;
    }

    // ------------------------------------------------------------------ partagé

    private static Quaternion Facing(Vector3 direction)
    {
        return direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
    }

    public static LineRenderer NewLine(GameObject go, Material material, int points)
    {
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.sharedMaterial = material;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 0;
        lr.positionCount = points;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.lightProbeUsage = LightProbeUsage.Off;
        lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        lr.enabled = false;
        return lr;
    }

    /// <summary>Écrit une ligne brisée de <paramref name="a"/> à <paramref name="b"/> : écarts
    /// aléatoires perpendiculaires, nuls aux extrémités, maximaux au milieu.
    /// <paramref name="jitter"/> est relatif à la longueur.</summary>
    public static void WriteJaggedLine(LineRenderer line, Vector3 a, Vector3 b, int points, float jitter, Vector3 hint)
    {
        if (line.positionCount != points) line.positionCount = points;
        Vector3 axis = b - a;
        float length = axis.magnitude;
        Vector3 dir = length > 0.0001f ? axis / length : Vector3.forward;
        Vector3 side = Vector3.Cross(dir, hint.sqrMagnitude > 0.0001f ? hint : Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(dir, Vector3.right);
        side.Normalize();
        Vector3 up = Vector3.Cross(side, dir);

        for (int i = 0; i < points; i++)
        {
            float t = points > 1 ? i / (float)(points - 1) : 0f;
            float envelope = Mathf.Sin(t * Mathf.PI);
            Vector2 offset = Random.insideUnitCircle * (jitter * length * envelope);
            line.SetPosition(i, a + axis * t + side * offset.x + up * offset.y);
        }
    }

    private static Mesh quadMesh;

    /// <summary>Quad de 1 m centré, avec couleurs de sommet blanches (le shader de particules les
    /// multiplie). Construit en code : CreatePrimitive livrerait un collider (voir CLAUDE.md).</summary>
    public static Mesh QuadMesh
    {
        get
        {
            if (quadMesh != null) return quadMesh;
            quadMesh = new Mesh { name = "VfxQuad" };
            quadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
            };
            quadMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            quadMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            quadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quadMesh.RecalculateNormals();
            quadMesh.RecalculateBounds();
            return quadMesh;
        }
    }
}
