using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// L'épave d'un robot tué (RobotCorpse, 2026-10-12) : demandé par l'utilisateur, « au minimum qu'il
/// tombe ». Elle doit tomber au sol sans le traverser ni se disloquer, sans emporter le modèle
/// vivant, et le survivant doit pouvoir la traverser : il garde la main en fin de manche, et une
/// épave n'a pas la même place sur deux machines.
/// </summary>
public class RobotCorpseTests
{
    private GameObject ground;
    private GameObject model;
    private RobotCorpse corpse;
    private GameObject walker;
    private readonly List<RobotCorpse> others = new List<RobotCorpse>();

    [TearDown]
    public void TearDown()
    {
        if (corpse != null) Object.Destroy(corpse.gameObject);
        foreach (RobotCorpse other in others) if (other != null) Object.Destroy(other.gameObject);
        others.Clear();
        if (model != null) Object.Destroy(model);
        if (ground != null) Object.Destroy(ground);
        if (walker != null) Object.Destroy(walker);
    }

    private IEnumerator SpawnCorpse(Vector3 velocity = default, ChassisId chassis = ChassisId.CT01)
    {
        ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = Vector3.one * 4f; // 40 m, sur le layer Default
        model = TestCharacter.InstantiateModel(out Animator animator);
        model.transform.position = Vector3.zero;
        RobotChassis.Apply(model.transform, chassis);

        // La pose d'attente posée par l'Animator, comme au moment d'une mort en jeu.
        yield return null;
        yield return null;
        Physics.SyncTransforms();

        corpse = RobotCorpse.Spawn(animator, velocity);
        Assert.IsNotNull(corpse, "Le modèle du joueur est humanoïde : l'épave doit se construire.");
    }

    [UnityTest]
    public IEnumerator LEpave_TombeAuSol_SansLeTraverserNiSeDisloquer()
    {
        yield return SpawnCorpse();
        Animator liveAnimator = model.GetComponent<Animator>();
        float liveHeadHeight = liveAnimator.GetBoneTransform(HumanBodyBones.Head).position.y;
        Assert.Greater(liveHeadHeight, 1.4f, "Garde : le robot est debout au moment de mourir.");

        Assert.GreaterOrEqual(corpse.Bodies.Count, 11, "Bassin, buste, tête, deux bras, deux avant-bras, deux cuisses, deux jambes.");
        foreach (Rigidbody body in corpse.Bodies)
        {
            Assert.AreEqual(CorpseLayer.Index, body.gameObject.layer, $"{body.name} : une épave vit sur le layer Corpse.");
            Assert.IsNotNull(body.GetComponent<Collider>(), $"{body.name} : chaque pièce a un collider, sinon elle passe à travers le sol.");
        }
        Assert.IsNull(corpse.GetComponentInChildren<Animator>(), "L'épave n'a plus d'Animator : il réécrirait les os.");

        // Le coup mortel, comme un tir de sniper dans le dos.
        corpse.Fall(corpse.Center + Vector3.up * 0.5f, Vector3.forward * 60f, 0.8f, 1);

        float until = Time.time + 3f;
        float lowest = float.MaxValue;
        while (Time.time < until)
        {
            foreach (Rigidbody body in corpse.Bodies) lowest = Mathf.Min(lowest, body.worldCenterOfMass.y);
            yield return null;
        }

        Bounds spread = new Bounds(corpse.Bodies[0].worldCenterOfMass, Vector3.zero);
        float highest = float.MinValue;
        foreach (Rigidbody body in corpse.Bodies)
        {
            Vector3 c = body.worldCenterOfMass;
            Assert.IsFalse(float.IsNaN(c.x) || float.IsNaN(c.y) || float.IsNaN(c.z), $"{body.name} : position invalide.");
            spread.Encapsulate(c);
            highest = Mathf.Max(highest, c.y);
        }

        Debug.Log($"[EPAVE] pièce la plus haute {highest:F2} m, la plus basse vue {lowest:F2} m, étendue {spread.size}");
        Assert.Less(highest, 0.6f, "Au bout de 3 s, l'épave est couchée au sol.");
        Assert.Greater(lowest, -0.15f, "Elle n'a jamais traversé le sol.");
        Assert.Less(Mathf.Max(spread.size.x, spread.size.z), 2.4f, "Elle ne s'est pas disloquée : ses pièces tiennent dans la taille d'un robot.");

        Assert.AreEqual(liveHeadHeight, liveAnimator.GetBoneTransform(HumanBodyBones.Head).position.y, 0.05f,
            "Le modèle vivant n'est pas touché : il réapparaîtra intact à la manche suivante.");
    }

    /// <summary>
    /// Le coup qui tue fait voler l'épave en pièces (2026-11-03, avec la kill cam) : brutal (le pompe, le gant), la tête,
    /// les bras, les jambes et le buste s'en détachent, sans articulation, et s'éloignent ; l'huile, les étincelles et les
    /// éclats partent, sur le layer de l'épave ; une balle légère à la tête n'arrache que la tête ; le prototype, à la peau
    /// continue, ne perd rien mais crache sa gerbe. Vu échouer, les articulations gardées.
    /// </summary>
    [UnityTest]
    public IEnumerator LeCoupQuiTue_FaitVolerLEpaveEnPieces()
    {
        yield return SpawnCorpse();
        Vector3 chest = corpse.Center + Vector3.up * 0.45f;
        corpse.Burst(chest, Vector3.forward * 200f, new Color(1f, 0.42f, 0.07f));
        Assert.IsTrue(corpse.HasBurst);
        Assert.AreEqual(RobotCorpse.BurstSeverity.Brutal, corpse.Severity);
        Assert.AreEqual(6, corpse.Detached.Count, "Brutal : la tête, les deux bras, les deux cuisses et le buste.");
        var rest = new Dictionary<Transform, Vector3>();
        foreach (Transform piece in corpse.Detached) rest[piece] = piece.localPosition;
        ParticleSystem[] systems = corpse.GetComponentsInChildren<ParticleSystem>(true);
        Assert.GreaterOrEqual(systems.Length, 3 + 6 * 2, "La gerbe de l'impact, l'huile de chaque articulation rompue, la traînée de chaque pièce.");
        foreach (ParticleSystem system in systems)
            Assert.AreEqual(CorpseLayer.Index, system.gameObject.layer, $"{system.name} : sur le layer de l'épave.");

        // Une articulation détruite disparaît à la fin de l'image.
        yield return null;
        foreach (Transform piece in corpse.Detached)
            Assert.IsNull(piece.GetComponent<CharacterJoint>(), $"{piece.name} : son articulation est rompue.");
        yield return new WaitForSeconds(0.6f);
        int away = 0;
        foreach (KeyValuePair<Transform, Vector3> pair in rest)
        {
            if (Vector3.Distance(pair.Key.localPosition, pair.Value) > 0.25f) away++;
        }
        Assert.GreaterOrEqual(away, 5, "Les pièces arrachées s'éloignent de leur place.");
        int alive = 0;
        foreach (ParticleSystem system in systems)
        {
            if (system != null && system.particleCount > 0) alive++;
        }
        Assert.Greater(alive, 0, "La gerbe est partie.");

        // Une balle légère à la tête.
        TearDown();
        yield return null;
        yield return SpawnCorpse();
        Transform head = model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
        corpse.Burst(head.position + Vector3.up * 0.1f, Vector3.forward * WeaponController.DeathImpulse(17f), Color.clear);
        Assert.AreEqual(RobotCorpse.BurstSeverity.Light, corpse.Severity);
        Assert.AreEqual(1, corpse.Detached.Count, "Une balle légère à la tête : la tête seule.");
        Assert.AreEqual(head.name, corpse.Detached[0].name);

        // Un tir de précision au buste, de face : le robot coupé à la taille, et sa tête. Jamais un bras touché (la surface
        // touchable n'a pas de bras) : celui qui tient l'arme devant le buste ne le cache pas.
        TearDown();
        yield return null;
        yield return SpawnCorpse();
        Transform chestBone = model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Chest);
        Vector3 front = model.transform.forward;
        for (float x = -0.6f; x <= 0.61f; x += 0.1f)
            for (float y = -0.5f; y <= 0.51f; y += 0.1f)
                for (float z = -0.3f; z <= 0.61f; z += 0.1f)
                {
                    Vector3 point = chestBone.position + model.transform.right * x + Vector3.up * y + front * z;
                    Assert.IsFalse(RobotCorpse.IsArm(corpse.TestNearestPart(point)), $"Un coup en {point} touche un bras.");
                }
        corpse.Burst(chestBone.position + front * 0.2f, -front * WeaponController.DeathImpulse(100f), Color.clear);
        Assert.AreEqual(RobotCorpse.BurstSeverity.Heavy, corpse.Severity);
        var cut = new List<string>();
        foreach (Transform piece in corpse.Detached) cut.Add(piece.name);
        Assert.AreEqual(2, cut.Count, "Lourd au buste : le buste et la tête (" + string.Join(", ", cut) + ").");
        Assert.IsTrue(cut.Exists(n => n.EndsWith("Spine")) && cut.Exists(n => n.EndsWith("Head")), string.Join(", ", cut));

        // Le prototype, à la peau continue.
        TearDown();
        yield return null;
        yield return SpawnCorpse(default, ChassisId.Prototype);
        corpse.Burst(corpse.Center + Vector3.up * 0.45f, Vector3.forward * 200f, Color.clear);
        Assert.AreEqual(0, corpse.Detached.Count, "Le prototype ne perd rien : sa peau s'étirerait.");
        Assert.GreaterOrEqual(corpse.GetComponentsInChildren<ParticleSystem>(true).Length, 3, "Mais sa gerbe part.");
    }

    /// <summary>Ce qu'on retient d'une chute : la pièce la plus rapide, la rotation la plus vive, de
    /// combien le bassin s'est soulevé, et où il a fini.</summary>
    private class Fall
    {
        public float peakSpeed;
        public float peakSpin;
        public float lift;
        public float travel;
        public float backward; // recul du bassin vers -Z, le sens des coups du test
        public float push;     // vitesse de recul du bassin 0,1 s après le coup
        public float highestAtEnd;
        public string fastest = "";
        public override string ToString() =>
            $"pointe {peakSpeed:F2} m/s ({fastest}), rotation {peakSpin:F1} rad/s, recul à 0,1 s {push:F2} m/s, bassin soulevé de {lift:F2} m, parti à {travel:F2} m (recul {backward:F2} m), plus haute pièce à la fin {highestAtEnd:F2} m";
    }

    /// <summary>Fait tomber une épave neuve (robot debout, face à +Z), frappée par `blow` (rien si
    /// null), et mesure sa chute pendant `seconds`.</summary>
    private IEnumerator MeasureFall(string label, System.Action<RobotCorpse, Vector3> blow, Fall result,
                                    float seconds = 2.5f, Vector3 velocity = default)
    {
        TearDown();
        yield return null;
        yield return SpawnCorpse(velocity);
        Vector3 face = model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head).position + Vector3.forward * 0.1f;
        yield return MeasureCorpse(label, () => blow?.Invoke(corpse, face), result, seconds);
    }

    private IEnumerator MeasureCorpse(string label, System.Action blow, Fall result, float seconds)
    {
        Rigidbody pelvis = corpse.Bodies[0];
        Vector3 start = pelvis.worldCenterOfMass;
        blow?.Invoke();
        float blowTime = Time.time;
        bool pushRead = false;
        float until = Time.time + seconds;
        while (Time.time < until)
        {
            yield return new WaitForFixedUpdate();
            if (!pushRead && Time.time - blowTime >= 0.1f)
            {
                result.push = -pelvis.linearVelocity.z;
                pushRead = true;
            }
            foreach (Rigidbody body in corpse.Bodies)
            {
                float speed = body.linearVelocity.magnitude;
                if (speed > result.peakSpeed) { result.peakSpeed = speed; result.fastest = body.name; }
                result.peakSpin = Mathf.Max(result.peakSpin, body.angularVelocity.magnitude);
            }
            result.lift = Mathf.Max(result.lift, pelvis.worldCenterOfMass.y - start.y);
        }
        Vector3 end = pelvis.worldCenterOfMass;
        result.travel = Vector2.Distance(new Vector2(start.x, start.z), new Vector2(end.x, end.z));
        result.backward = start.z - end.z;
        result.highestAtEnd = float.MinValue;
        foreach (Rigidbody body in corpse.Bodies) result.highestAtEnd = Mathf.Max(result.highestAtEnd, body.worldCenterOfMass.y);
        Debug.Log($"[EPAVE] {label} : {result}");
    }

    /// <summary>La première graine qui donne à ce coup la façon de tomber `style`.</summary>
    private static int SeedFor(RobotCorpse target, CorpseFall.Style style, Vector3 point, Vector3 impulse, float closeness)
    {
        for (int seed = 1; seed < 5000; seed++)
        {
            if (target.TestPlan(point, impulse, closeness, seed).Style == style) return seed;
        }
        Assert.Fail($"Aucune graine ne donne {style} à ce coup.");
        return 0;
    }

    private static void Blow(RobotCorpse target, CorpseFall.Style style, Vector3 point, Vector3 impulse, float closeness)
        => target.Fall(point, impulse, closeness, SeedFor(target, style, point, impulse, closeness));

    private static WeaponData Weapon(string asset)
    {
        WeaponData data = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>($"Assets/_ProjectArena/Weapon/{asset}.asset");
        Assert.IsNotNull(data, $"L'arme {asset} de la panoplie.");
        return data;
    }

    /// <summary>
    /// Le coup mortel d'une arme ordinaire pousse l'épave, il ne la projette pas (retour de
    /// l'utilisateur, 2026-10-12 : le ragdoll était « violent »). Mesuré contre la même épave tombée
    /// sans coup : le coup ajoute un peu d'élan, jamais un vol plané, une vrille ou une pièce qui
    /// fouette. Le pire cas : le sniper en pleine tête (la pièce la plus légère après les bras). Le
    /// pompe et le taser, eux, projettent (UnCoupPuissant_ProjetteLEpave_DUnBloc).
    /// </summary>
    [UnityTest]
    public IEnumerator LeCoupMortel_PousseLEpave_SansLaProjeter()
    {
        WeaponData sniper = Weapon("Arme_Sniper");
        Assert.AreEqual(0f, sniper.killKnockback, "Garde : le sniper n'est pas une arme qui projette.");
        float sniperHead = sniper.damagePerHit * sniper.headMultiplier;
        // Le sniper est la plus forte des armes ordinaires (DeathImpulse plafonne à ses dégâts) : ce qui
        // vaut pour lui vaut pour le fusil d'assaut et le raygun, qui poussent moins.
        Assert.AreEqual(WeaponController.DeathImpulse(1000f), WeaponController.KillImpulse(sniper, Vector3.back, sniperHead).magnitude, 1e-3f,
            "Garde : le sniper en pleine tête pousse autant qu'une arme ordinaire le peut.");

        Vector3 force = WeaponController.KillImpulse(sniper, Vector3.back, sniperHead);
        var alone = new Fall();
        yield return MeasureFall("chute seule", null, alone);
        var head = new Fall();
        yield return MeasureFall("sniper en pleine tête, renversé", (c, face) => Blow(c, CorpseFall.Style.Thrown, face, force, 0.8f), head);
        // Chez l'hôte, le robot d'un client avance par à-coups : sa vitesse sur une image de
        // rattrapage pouvait atteindre 12 m/s (la borne d'alors), et l'épave en héritait.
        var running = new Fall();
        yield return MeasureFall("sniper en pleine course (12 m/s hérités)", (c, face) => Blow(c, CorpseFall.Style.Thrown, face, force, 0.8f), running,
            velocity: Vector3.back * 12f);
        // Les autres chutes tiennent les mêmes promesses.
        var others = new List<(string, Fall)>();
        foreach (CorpseFall.Style style in new[] { CorpseFall.Style.Heap, CorpseFall.Style.Dive, CorpseFall.Style.Spin })
        {
            var fall = new Fall();
            yield return MeasureFall($"sniper en pleine tête, {style}", (c, face) => Blow(c, style, face, force, 0.8f), fall);
            others.Add((style.ToString(), fall));
        }

        Assert.Greater(alone.peakSpeed, 2f, "Garde : sans coup, l'épave tombe bien (sa chute fait de la vitesse).");
        Assert.Greater(head.push, alone.push + 0.3f, $"Garde : le sniper repousse vraiment l'épave ({head}).");

        others.Add(("renversé", head));
        foreach ((string name, Fall fall) in others)
        {
            Assert.Less(fall.peakSpeed, alone.peakSpeed + 3f, $"{name} : aucune pièce ne part beaucoup plus vite qu'en tombant seule ({fall}).");
            Assert.Less(fall.lift, 0.1f, $"{name} : l'épave n'est pas soulevée ({fall}).");
            Assert.Less(fall.backward - alone.backward, 1f, $"{name} : elle recule d'un pas, sans glisser au loin ({fall}).");
            Assert.Less(fall.travel, 3f, $"{name} : elle reste où elle était tombée ({fall}).");
            Assert.Less(fall.highestAtEnd, 0.6f, $"{name} : elle finit couchée ({fall}).");
        }

        // Tué en pleine course, le robot roule encore un peu : jamais à la vitesse d'un sprint.
        Assert.Less(running.peakSpeed, 10f, $"Course : aucune pièce ne file à la vitesse héritée d'une image de rattrapage ({running}).");
        Assert.Less(running.lift, 0.1f, $"Course : l'épave n'est pas soulevée ({running}).");
        Assert.Less(running.backward - alone.backward, 2.5f, $"Course : elle roule un peu, sans glisser au loin ({running}).");
        Assert.Less(running.highestAtEnd, 0.6f, $"Course : elle finit couchée ({running}).");
    }

    /// <summary>
    /// Le pompe à bout portant et le taser projettent l'épave en arrière, dans le sens du coup
    /// (demandé par l'utilisateur le 2026-10-16 : « un réel effet de puissance »). Elle décolle un peu
    /// et part de plus d'un mètre, d'un bloc : aucune pièce ne fouette, elle ne se disloque pas, ne
    /// traverse pas l'arène et finit couchée. Le pompe de loin, quelques plombs au but, projette moins.
    /// </summary>
    [UnityTest]
    public IEnumerator UnCoupPuissant_ProjetteLEpave_DUnBloc()
    {
        WeaponData shotgun = Weapon("Arme_Pompe");
        Assert.Greater(shotgun.killKnockback, 0f, "Garde : le pompe est une arme qui projette.");
        float pointBlank = shotgun.damagePerHit * shotgun.PelletCount;

        var alone = new Fall();
        yield return MeasureFall("chute seule", null, alone);
        Assert.AreEqual(RobotCorpse.RobotMass, corpse.TotalMass, 0.01f,
            "Un coup puissant se règle en vitesse, convertie par la masse d'une épave : c'est bien la sienne.");
        var shotgunFall = new Fall();
        yield return MeasureFall("pompe à bout portant, en pleine tête",
            (c, face) => Blow(c, CorpseFall.Style.Thrown, face, WeaponController.KillImpulse(shotgun, Vector3.back, pointBlank), 1f), shotgunFall);
        float spreadShotgun = LastSpread();
        var taser = new Fall();
        yield return MeasureFall("taser au visage",
            (c, face) => Blow(c, CorpseFall.Style.Thrown, face, TaserController.DeathImpulse(Vector3.back), 1f), taser);
        float spreadTaser = LastSpread();
        var far = new Fall();
        yield return MeasureFall("pompe de loin, trois plombs à mi-dégâts",
            (c, face) => Blow(c, CorpseFall.Style.Thrown, face, WeaponController.KillImpulse(shotgun, Vector3.back, 3 * shotgun.damagePerHit * 0.5f), 0.4f), far);

        foreach ((string name, Fall fall, float spread, float speed) in new[]
                 { ("pompe", shotgunFall, spreadShotgun, shotgun.killKnockback), ("taser", taser, spreadTaser, TaserController.StrikeKnockback) })
        {
            Assert.Greater(fall.push, 2.5f, $"{name} : l'épave part vite en arrière ({fall}).");
            Assert.Greater(fall.backward - alone.backward, 1.2f, $"{name} : elle est projetée de plus d'un mètre ({fall}).");
            Assert.Less(fall.backward - alone.backward, 4.5f, $"{name} : sans traverser l'arène ({fall}).");
            Assert.Greater(fall.lift, 0.03f, $"{name} : le coup la décolle ({fall}).");
            Assert.Less(fall.lift, 0.6f, $"{name} : sans l'envoyer en l'air ({fall}).");
            // Poussé d'un bloc, le corps bascule en arrière autour de ses pieds retenus par le sol : sa
            // tête va jusqu'à deux fois sa vitesse (10,3 m/s mesurés au pompe le 2026-10-16). Au-delà, une
            // pièce fouetterait seule.
            Assert.Less(fall.peakSpeed, 2f * speed + 1.5f, $"{name} : aucune pièce ne part plus vite que le corps qui bascule ({fall}).");
            Assert.Less(spread, 2.4f, $"{name} : elle ne se disloque pas.");
            Assert.Less(fall.highestAtEnd, 0.6f, $"{name} : elle finit couchée ({fall}).");
        }
        Assert.Less(far.push, shotgunFall.push, $"De loin, le pompe projette moins ({far} contre {shotgunFall}).");
        Assert.Greater(far.push, alone.push + 0.5f, $"Mais il projette encore ({far}).");
    }

    /// <summary>L'étendue à plat (m) des pièces de l'épave en ce moment.</summary>
    private float LastSpread()
    {
        Bounds bounds = new Bounds(corpse.Bodies[0].worldCenterOfMass, Vector3.zero);
        foreach (Rigidbody body in corpse.Bodies) bounds.Encapsulate(body.worldCenterOfMass);
        return Mathf.Max(bounds.size.x, bounds.size.z);
    }

    /// <summary>
    /// Tirer sur l'épave pour narguer (le survivant garde la main en fin de manche) la fait sursauter,
    /// sans la faire voler : une décharge de pompe à 2 m, tous les plombs au but.
    /// </summary>
    [UnityTest]
    public IEnumerator UnTirSurLEpave_LaSecoue_SansLaFaireVoler()
    {
        var lying = new Fall();
        yield return MeasureFall("épave couchée", null, lying, 2f);
        Assert.Less(lying.highestAtEnd, 0.6f, "Garde : l'épave est couchée avant le tir.");
        // Et immobile : sinon on mesure la fin de sa chute (un avant-bras qui retombait à 2 m/s faisait
        // passer la garde du pompe sans aucune poussée).
        yield return WaitForRest(6f);
        Assert.Less(CorpseSpeed(), 0.05f, "Garde : l'épave ne bouge plus avant le tir.");

        WeaponData shotgun = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/_ProjectArena/Weapon/Arme_Pompe.asset");
        Assert.IsNotNull(shotgun, "Le fusil à pompe de la panoplie.");
        Assert.Greater(shotgun.PelletCount, 1, "Garde : c'est bien une arme à plombs.");

        // Chaque plomb vise une pièce différente, les plus légères comprises (avant-bras, tête) : la
        // gerbe d'un pompe couvre tout le robot à quelques mètres.
        var shot = new Fall();
        int hits = 0;
        yield return MeasureCorpse("décharge de pompe sur toute l'épave", () =>
        {
            for (int i = 0; i < shotgun.PelletCount; i++)
            {
                Rigidbody part = corpse.Bodies[(i * 3 + 2) % corpse.Bodies.Count];
                if (ShootAt(part, WeaponController.CorpseImpulse(shotgun))) hits++;
            }
        }, shot, 2f);
        Assert.GreaterOrEqual(hits, shotgun.PelletCount - 2, "Garde : la décharge touche l'épave.");

        WeaponData sniper = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/_ProjectArena/Weapon/Arme_Sniper.asset");
        Assert.IsNotNull(sniper, "Le sniper de la panoplie.");
        yield return WaitForRest(6f);
        Assert.Less(CorpseSpeed(), 0.05f, "Garde : l'épave ne bouge plus avant la balle de sniper.");
        var headshot = new Fall();
        bool headHit = false;
        yield return MeasureCorpse("balle de sniper dans la tête de l'épave", () =>
        {
            headHit = ShootAt(corpse.Bodies[2], WeaponController.CorpseImpulse(sniper));
        }, headshot, 2f);
        Assert.IsTrue(headHit, "Garde : la balle touche la tête de l'épave.");

        foreach ((string name, Fall fall) in new[] { ("pompe", shot), ("sniper", headshot) })
        {
            // Garde à 0,1 m/s : une épave immobile qu'aucun tir ne pousse mesure 0,02 m/s, la décharge du
            // pompe 0,3 et la balle de sniper 0,5 (2026-10-13).
            Assert.Greater(fall.peakSpeed, 0.1f, $"{name} : garde, l'épave sursaute bien ({fall}).");
            Assert.Less(fall.peakSpeed, 3f, $"{name} : aucune pièce ne s'envole ({fall}).");
            Assert.Less(fall.travel, 0.5f, $"{name} : l'épave sursaute sur place ({fall}).");
            Assert.Less(fall.highestAtEnd, 0.6f, $"{name} : elle retombe couchée ({fall}).");
        }
    }

    /// <summary>Attend que l'épave soit immobile (dix pas de physique de suite sous 5 cm/s), au plus
    /// `seconds`.</summary>
    private IEnumerator WaitForRest(float seconds)
    {
        float until = Time.time + seconds;
        int still = 0;
        while (Time.time < until && still < 10)
        {
            yield return new WaitForFixedUpdate();
            still = CorpseSpeed() < 0.05f ? still + 1 : 0;
        }
    }

    /// <summary>La vitesse de la pièce la plus rapide de l'épave.</summary>
    private float CorpseSpeed()
    {
        float max = 0f;
        foreach (Rigidbody body in corpse.Bodies) max = Mathf.Max(max, body.linearVelocity.magnitude);
        return max;
    }

    /// <summary>Tire sur la pièce `part` de l'épave depuis 2 m de côté et 1,2 m de haut.</summary>
    private static bool ShootAt(Rigidbody part, float impulse)
    {
        Vector3 target = part.worldCenterOfMass;
        Vector3 origin = target + new Vector3(0f, 1.2f, -2f);
        Vector3 end = origin + (target - origin).normalized * 10f;
        return RobotCorpse.TryHit(origin, end, impulse, out _, out _);
    }

    /// <summary>Une épave de plus, décalée de `offset`, à partir du même robot (les épaves ne se touchent pas entre elles).</summary>
    private RobotCorpse SpawnOther(Animator animator, Vector3 offset)
    {
        RobotCorpse other = RobotCorpse.Spawn(animator, Vector3.zero);
        other.transform.position += offset;
        Physics.SyncTransforms();
        others.Add(other);
        return other;
    }

    private static Vector3 Grid(int index) => new Vector3((index % 5) * 4f - 8f, 0f, (index / 5) * 5f - 8f);

    // L'axe du corps : du bassin à la tête.
    private static Vector3 Spine(RobotCorpse target) => (target.Bodies[2].position - target.Bodies[0].position).normalized;

    /// <summary>
    /// Une même balle ne fait pas deux fois la même chute (2026-11-04, retour de l'utilisateur : « souvent la même chute en arrière ») :
    /// dix épaves frappées de la même balle de fusil d'assaut, au même endroit, à la graine près, partent en tas, en plongeon, en vrille
    /// ou renversées, finissent tournées de tous les côtés, et pas toutes sur le dos. Vu échouer, le plan rendu le même pour toutes.
    /// </summary>
    [UnityTest]
    public IEnumerator UneMemeBalle_NeFaitPasDeuxFoisLaMemeChute()
    {
        WeaponData assault = Weapon("MP5");
        Vector3 force = WeaponController.KillImpulse(assault, Vector3.back, assault.damagePerHit);
        yield return SpawnCorpse();
        Animator animator = model.GetComponent<Animator>();
        var chests = new List<Vector3>();
        var styles = new HashSet<CorpseFall.Style>();
        for (int i = 0; i < 10; i++)
        {
            RobotCorpse fallen = SpawnOther(animator, Grid(i));
            chests.Add(Quaternion.Inverse(fallen.Bodies[1].rotation) * fallen.transform.forward);
            fallen.Fall(fallen.Center + Vector3.up * 0.3f + fallen.transform.forward * 0.14f, force, 0.8f, 7919 * (i + 1));
            styles.Add(fallen.LastFall.Style);
        }
        yield return new WaitForSeconds(3f);

        var faces = new HashSet<string>();
        float widest = 0f;
        for (int i = 0; i < others.Count; i++)
        {
            float chest = (others[i].Bodies[1].rotation * chests[i]).y;
            faces.Add(chest > 0.5f ? "dos" : chest < -0.5f ? "ventre" : "côté");
            for (int j = 0; j < i; j++)
            {
                Vector3 a = Spine(others[i]), b = Spine(others[j]);
                widest = Mathf.Max(widest, Vector2.Angle(new Vector2(a.x, a.z), new Vector2(b.x, b.z)));
            }
            Assert.Less(others[i].Bodies[2].worldCenterOfMass.y, 0.6f, "Chacune finit couchée.");
        }
        Debug.Log($"[EPAVE] {styles.Count} façons de tomber, finies sur {string.Join(", ", faces)}, jusqu'à {widest:F0}° d'écart");
        Assert.GreaterOrEqual(styles.Count, 3, "Plusieurs façons de tomber.");
        Assert.GreaterOrEqual(faces.Count, 2, "Pas toutes sur le dos.");
        Assert.Greater(widest, 90f, "Elles ne finissent pas toutes tournées pareil.");
    }

    /// <summary>
    /// Tué en visant, le robot ne finit pas les bras en croix (2026-11-04, retour de l'utilisateur) : les limites de l'assistant Ragdoll,
    /// comptées depuis cette pose, retenaient ses bras à l'horizontale, à 90° du buste. Vu échouer avec elles.
    /// </summary>
    [UnityTest]
    public IEnumerator TueEnVisant_LesBrasRetombentLeLongDuCorps()
    {
        WeaponData sniper = Weapon("Arme_Sniper");
        Vector3 force = WeaponController.KillImpulse(sniper, Vector3.back, 60f);
        yield return SpawnCorpse();
        Animator animator = model.GetComponent<Animator>();
        Object.Destroy(corpse.gameObject);
        animator.SetBool("Aiming", true);
        yield return new WaitForSeconds(1f);
        float raised = Vector3.Angle(animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position - animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position, Vector3.down);
        Assert.Greater(raised, 35f, "Garde : il vise, le bras levé devant lui.");

        for (int i = 0; i < 10; i++)
        {
            RobotCorpse fallen = SpawnOther(animator, Grid(i));
            fallen.Fall(fallen.Center + Vector3.up * 0.3f + fallen.transform.forward * 0.14f, force, 0.8f, 104729 * (i + 1));
        }
        yield return new WaitForSeconds(3f);

        int crosses = 0, lowered = 0;
        foreach (RobotCorpse fallen in others)
        {
            Vector3 down = -Spine(fallen);
            float left = Vector3.Angle(fallen.Bodies[4].position - fallen.Bodies[3].position, down);
            float right = Vector3.Angle(fallen.Bodies[6].position - fallen.Bodies[5].position, down);
            if (left > 60f && left < 125f && right > 60f && right < 125f) crosses++;
            if (left < 45f) lowered++;
            if (right < 45f) lowered++;
        }
        Debug.Log($"[EPAVE] tué en visant : {crosses} croix sur {others.Count}, {lowered} bras le long du corps");
        // Mesuré : 0 croix et 16 bras le long du corps ; avec les limites de l'assistant, 2 croix et 6 bras.
        Assert.LessOrEqual(crosses, 1, "Les deux bras à l'horizontale, c'est la croix.");
        Assert.GreaterOrEqual(lowered, 11, "Les bras retombent le long du corps.");
    }

    // De combien un membre (de `upper` à `lower`, jusqu'à `tip`) est plié, dans le sens de sa charnière.
    private static float Bend(Rigidbody upper, Rigidbody lower, Vector3 tip)
    {
        Vector3 axis = lower.transform.TransformDirection(lower.GetComponent<CharacterJoint>().axis);
        return Vector3.SignedAngle(lower.position - upper.position, tip - lower.position, axis);
    }

    /// <summary>
    /// Les genoux et les coudes ne plient que dans leur sens, de tendus à pliés, quelle que soit la pose de la mort : debout, en
    /// visant, accroupi. Ils se comptent depuis cette pose : mal comptés, ils plieraient à l'envers sans que rien ne le dise.
    /// Mesuré à chaque pas de physique, pendant la chute. Vu échouer, les limites inversées.
    /// </summary>
    [UnityTest]
    public IEnumerator LesGenouxEtLesCoudes_NePlientQueDansLeurSens()
    {
        WeaponData assault = Weapon("MP5");
        Vector3 force = WeaponController.KillImpulse(assault, Vector3.back, assault.damagePerHit);
        yield return SpawnCorpse();
        Animator animator = model.GetComponent<Animator>();
        Object.Destroy(corpse.gameObject);
        int index = 0;
        foreach (bool crouched in new[] { false, true })
        {
            foreach (bool aiming in new[] { false, true })
            {
                animator.SetInteger("Stance", crouched ? 1 : 0);
                animator.SetFloat("StanceF", crouched ? 1f : 0f);
                animator.SetBool("Aiming", aiming);
                yield return new WaitForSeconds(0.7f);
                for (int i = 0; i < 3; i++, index++)
                {
                    RobotCorpse fallen = SpawnOther(animator, Grid(index));
                    fallen.Fall(fallen.Center + Vector3.up * 0.3f + fallen.transform.forward * 0.14f, force, 0.8f, 15485863 * (index + 1));
                }
            }
        }
        var tips = new Dictionary<RobotCorpse, (Transform leftHand, Transform rightHand, Transform leftFoot, Transform rightFoot)>();
        foreach (RobotCorpse fallen in others)
        {
            Transform Find(string name)
            {
                foreach (Transform t in fallen.GetComponentsInChildren<Transform>(true)) if (t.name.EndsWith(name)) return t;
                Assert.Fail(name);
                return null;
            }
            tips[fallen] = (Find("LeftHand"), Find("RightHand"), Find("LeftFoot"), Find("RightFoot"));
        }

        float lowest = float.MaxValue, highest = float.MinValue, lowestKnee = float.MaxValue, highestKnee = float.MinValue;
        float until = Time.time + 2.5f;
        while (Time.time < until)
        {
            yield return new WaitForFixedUpdate();
            foreach (RobotCorpse fallen in others)
            {
                var b = fallen.Bodies;
                var tip = tips[fallen];
                foreach (float elbow in new[] { Bend(b[3], b[4], tip.leftHand.position), Bend(b[5], b[6], tip.rightHand.position) })
                {
                    lowest = Mathf.Min(lowest, elbow);
                    highest = Mathf.Max(highest, elbow);
                }
                foreach (float knee in new[] { Bend(b[7], b[8], tip.leftFoot.position), Bend(b[9], b[10], tip.rightFoot.position) })
                {
                    lowestKnee = Mathf.Min(lowestKnee, knee);
                    highestKnee = Mathf.Max(highestKnee, knee);
                }
            }
        }
        Debug.Log($"[EPAVE] coudes de {lowest:F0}° à {highest:F0}°, genoux de {lowestKnee:F0}° à {highestKnee:F0}°");
        // À 20° près : un choc passe un instant la butée (13° mesurés au coude, le bras qui frappe le sol).
        Assert.Greater(lowest, -20f, "Un coude ne plie pas à l'envers.");
        Assert.Less(highest, 165f, "Ni au-delà de ce qu'il peut.");
        Assert.Greater(lowestKnee, -20f, "Un genou ne plie pas à l'envers.");
        Assert.Less(highestKnee, 160f, "Ni au-delà de ce qu'il peut.");
        Assert.Greater(highestKnee, 60f, "Garde : les jambes lâchent, les genoux plient vraiment.");
    }

    [UnityTest]
    public IEnumerator LeSurvivant_TraverseLEpave()
    {
        yield return SpawnCorpse();
        float until = Time.time + 2f;
        while (Time.time < until) yield return null;

        // Un joueur (CharacterController sur le layer Player) qui marche droit sur l'épave.
        walker = new GameObject("Survivant") { layer = LayerMask.NameToLayer("Player") };
        var controller = walker.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.35f;
        controller.center = new Vector3(0f, 0.98f, 0f);
        Vector3 start = corpse.Center - Vector3.forward * 2f;
        start.y = 0.05f;
        walker.transform.position = start;
        Physics.SyncTransforms();

        for (int i = 0; i < 40; i++)
        {
            controller.Move(Vector3.forward * 0.1f + Vector3.down * 0.02f);
            yield return null;
        }

        float travelled = walker.transform.position.z - start.z;
        Assert.Greater(travelled, 3.8f, "Le survivant passe à travers l'épave, il n'y bute pas (4 m demandés).");
    }
}
