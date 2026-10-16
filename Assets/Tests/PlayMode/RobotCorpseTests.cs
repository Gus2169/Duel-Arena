using System.Collections;
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

    [TearDown]
    public void TearDown()
    {
        if (corpse != null) Object.Destroy(corpse.gameObject);
        if (model != null) Object.Destroy(model);
        if (ground != null) Object.Destroy(ground);
        if (walker != null) Object.Destroy(walker);
    }

    private IEnumerator SpawnCorpse(Vector3 velocity = default)
    {
        ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = Vector3.one * 4f; // 40 m, sur le layer Default
        model = TestCharacter.InstantiateModel(out Animator animator);
        model.transform.position = Vector3.zero;

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
        corpse.ApplyImpulse(corpse.Center + Vector3.up * 0.5f, Vector3.forward * 60f);

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

        var alone = new Fall();
        yield return MeasureFall("chute seule", null, alone);
        var head = new Fall();
        yield return MeasureFall("sniper en pleine tête",
            (c, face) => c.ApplyImpulse(face, WeaponController.KillImpulse(sniper, Vector3.back, sniperHead)), head);
        // Chez l'hôte, le robot d'un client avance par à-coups : sa vitesse sur une image de
        // rattrapage pouvait atteindre 12 m/s (la borne d'alors), et l'épave en héritait.
        var running = new Fall();
        yield return MeasureFall("sniper en pleine course (12 m/s hérités)",
            (c, face) => c.ApplyImpulse(face, WeaponController.KillImpulse(sniper, Vector3.back, sniperHead)), running,
            velocity: Vector3.back * 12f);

        Assert.Greater(alone.peakSpeed, 2f, "Garde : sans coup, l'épave tombe bien (sa chute fait de la vitesse).");
        Assert.Greater(head.push, alone.push + 0.3f, $"Garde : le sniper repousse vraiment l'épave ({head}).");

        Assert.Less(head.peakSpeed, alone.peakSpeed + 1.5f, $"Aucune pièce ne part plus vite qu'en tombant seule, ou presque ({head}).");
        Assert.Less(head.lift, 0.1f, $"L'épave n'est pas soulevée ({head}).");
        Assert.Less(head.backward - alone.backward, 1f, $"Elle recule d'un pas, sans glisser au loin ({head}).");
        Assert.Less(head.highestAtEnd, 0.6f, $"Elle finit couchée ({head}).");

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
            (c, face) => c.ApplyImpulse(face, WeaponController.KillImpulse(shotgun, Vector3.back, pointBlank)), shotgunFall);
        float spreadShotgun = LastSpread();
        var taser = new Fall();
        yield return MeasureFall("taser au visage",
            (c, face) => c.ApplyImpulse(face, TaserController.DeathImpulse(Vector3.back)), taser);
        float spreadTaser = LastSpread();
        var far = new Fall();
        yield return MeasureFall("pompe de loin, trois plombs à mi-dégâts",
            (c, face) => c.ApplyImpulse(face, WeaponController.KillImpulse(shotgun, Vector3.back, 3 * shotgun.damagePerHit * 0.5f)), far);

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
        yield return WaitForRest(4f);
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
        yield return WaitForRest(4f);
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
