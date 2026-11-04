using NUnit.Framework;
using UnityEngine;

/// <summary>
/// La kill cam en direct (KillCam, 2026-11-04, d'après Sniper Elite) : son déroulé, la vitesse du monde, quand elle se
/// joue, sa légende, ses plans, le décor qui s'assombrit, et la caméra qui rentre au joueur. Fonctions pures.
/// </summary>
public class KillCamTests
{
    private static KillCam.Info Kill(Health.DamageCause cause, float distance)
        => new KillCam.Info { killer = 1, victim = 2, cause = cause, distanceDm = KillCam.Decimeters(distance) };

    /// <summary>
    /// Le déroulé : le coup part à 0 ; la balle suivie pour un tir de loin, d'autant plus longtemps qu'il est long (au sniper
    /// d'un poste à l'autre, plus de deux secondes) ; de près (le gant, le pompe à bout portant), le temps d'un instant. Vu
    /// échouer, le vol de la balle de la même durée à toutes les distances.
    /// </summary>
    [Test]
    public void LeDeroule_SuitLaBalleDAutantPlusLongtempsQueLeTirEstLong()
    {
        KillCam.Timeline far = KillCam.TimelineFor(Kill(Health.DamageCause.HeadShot, 56f));
        KillCam.Timeline near = KillCam.TimelineFor(Kill(Health.DamageCause.Shot, 8f));
        Assert.AreEqual(KillCam.Style.Bullet, far.style);
        Assert.AreEqual(KillCam.Style.Bullet, near.style);
        Assert.Greater(far.impact, 2f, "Au sniper, d'un poste à l'autre : la balle suivie plus de deux secondes.");
        Assert.AreEqual(KillCam.FlightMin, near.impact, 1e-5f);
        Assert.Greater(far.duration, near.duration);
        Assert.AreEqual(KillCam.FlightMax, KillCam.FlightTime(500f), 1e-5f, "Jamais plus que FlightMax.");
        Assert.AreEqual(KillCam.FlightMin, KillCam.FlightTime(float.NaN), 1e-5f);

        foreach (KillCam.Info info in new[] { Kill(Health.DamageCause.Glove, 1.2f), Kill(Health.DamageCause.Shot, 2.5f) })
        {
            KillCam.Timeline close = KillCam.TimelineFor(info);
            Assert.AreEqual(KillCam.Style.Close, close.style, "Le gant, un tir à bout portant : de près.");
            Assert.AreEqual(KillCam.CloseLead, close.impact, 1e-5f);
        }

        foreach (KillCam.Timeline line in new[] { far, near, KillCam.TimelineFor(Kill(Health.DamageCause.Glove, 1f)) })
        {
            Assert.Less(0f, line.impact);
            Assert.Less(line.impact, line.impactEnd);
            Assert.Less(line.impactEnd, line.settled);
            Assert.Less(line.settled, line.duration);
            Assert.Less(line.duration, 6f, "Une kill cam courte : la partie reprend vite.");
        }
        Assert.AreEqual(far.duration, KillCam.PhaseExtension(Kill(Health.DamageCause.HeadShot, 56f)), 1e-5f,
                        "Elle part au coup : rien avant elle.");
    }

    /// <summary>
    /// La vitesse du monde : le temps réel à l'instant du coup (aucune coupure), presque à l'arrêt en un dixième de seconde,
    /// le plus lent pendant le vol, un peu moins à l'impact, puis elle remonte d'un trait jusqu'au temps réel, sans jamais
    /// s'arrêter tout à fait. De près, moins lente : le coup arrive tout de suite. Vu échouer, le temps réel dès l'impact.
    /// </summary>
    [Test]
    public void LeMonde_RalentitAuCoup_PuisRevientAuTempsReel()
    {
        foreach (KillCam.Timeline line in new[]
                 {
                     KillCam.TimelineFor(Kill(Health.DamageCause.HeadShot, 40f)),
                     KillCam.TimelineFor(Kill(Health.DamageCause.Glove, 1f)),
                 })
        {
            bool bullet = line.style == KillCam.Style.Bullet;
            float slowest = bullet ? KillCam.FlightRate : KillCam.CloseRate;
            Assert.AreEqual(1f, KillCam.WorldRate(-0.1f, line), "Avant, le temps réel.");
            Assert.AreEqual(1f, KillCam.WorldRate(0f, line), 1e-5f, "Au coup, le temps réel : le ralenti part de la partie.");
            Assert.AreEqual(slowest, KillCam.WorldRate(KillCam.SlowDownTime, line), 1e-5f);
            Assert.AreEqual(slowest, KillCam.WorldRate(line.impact - 0.001f, line), 1e-5f);
            Assert.AreEqual(KillCam.ImpactRate, KillCam.WorldRate(line.impact, line), 1e-5f);
            Assert.AreEqual(KillCam.ImpactRate, KillCam.WorldRate(line.impactEnd - 0.001f, line), 1e-5f);
            Assert.AreEqual(1f, KillCam.WorldRate(line.settled, line), 1e-5f, "Le temps est revenu.");
            Assert.AreEqual(1f, KillCam.WorldRate(line.duration, line), 1e-5f);

            float previous = KillCam.ImpactRate;
            for (float t = line.impactEnd; t < line.settled; t += 0.01f)
            {
                float rate = KillCam.WorldRate(t, line);
                Assert.GreaterOrEqual(rate, previous - 1e-5f, $"Elle remonte sans jamais redescendre (à {t:F2} s).");
                Assert.Less(rate - previous, 0.02f, $"Sans à-coup (à {t:F2} s).");
                previous = rate;
            }
            for (float t = 0f; t < line.duration; t += 0.01f)
                Assert.Greater(KillCam.WorldRate(t, line), 0f, "Le monde ne s'arrête jamais tout à fait.");
        }
        Assert.Less(KillCam.FlightRate, KillCam.ImpactRate, "Le vol, le plus lent : la balle seule semble bouger.");

        KillCam.Timeline flight = KillCam.TimelineFor(Kill(Health.DamageCause.Shot, 30f));
        Assert.AreEqual(0f, KillCam.FlightProgress(0f, flight));
        Assert.AreEqual(1f, KillCam.FlightProgress(flight.impact, flight));
        Assert.AreEqual(1f, KillCam.FlightProgress(0f, KillCam.TimelineFor(Kill(Health.DamageCause.Shot, 2.5f))), "De près, partie d'un coup.");
    }

    /// <summary>La balle : lente en sortant du canon et en arrivant, vite entre les deux, du canon à l'impact, sans jamais
    /// reculer. Vu échouer, la balle à vitesse constante.</summary>
    [Test]
    public void LaBalle_SortDuCanonEtArriveLentement()
    {
        Assert.AreEqual(0f, KillCam.BulletTravel(0f), 1e-6f);
        Assert.AreEqual(1f, KillCam.BulletTravel(1f), 1e-6f);
        float previous = 0f;
        for (float u = 0.01f; u <= 1f; u += 0.01f)
        {
            float travel = KillCam.BulletTravel(u);
            Assert.Greater(travel, previous, $"Jamais en arrière (à {u:F2}).");
            previous = travel;
        }
        float start = KillCam.BulletTravel(0.05f) / 0.05f;
        float middle = (KillCam.BulletTravel(0.55f) - KillCam.BulletTravel(0.45f)) / 0.1f;
        float end = (1f - KillCam.BulletTravel(0.95f)) / 0.05f;
        Assert.Less(start, 0.6f, "Elle sort du canon lentement.");
        Assert.Less(end, 0.6f, "Elle arrive lentement.");
        Assert.Greater(middle, 1.3f, "Entre les deux, vite.");
    }

    /// <summary>
    /// Quand la jouer : sur l'élimination de l'image (pas une zone tenue ni un module ramassé plus tard) ; les joueurs gelés
    /// dès le coup et le temps de la kill cam, puis le gagnant reprend la main, et pas par la kill cam d'une phase d'avant ;
    /// l'écran de fin après elle et le temps du gagnant, au Tournoi tout de suite. Vu échouer, le gagnant gelé jusqu'à la fin
    /// de la manche.
    /// </summary>
    [Test]
    public void LaKillCam_PartAuCoup_GeleLesJoueurs_PuisRendLaMain()
    {
        Assert.IsTrue(KillCam.IsFresh(10.0, 10.0));
        Assert.IsTrue(KillCam.IsFresh(10.0, 10.2));
        Assert.IsFalse(KillCam.IsFresh(10.0, 10.5), "Un module ramassé, une zone tenue après l'élimination : pas de kill cam.");
        Assert.IsFalse(KillCam.IsFresh(0.0, 0.1), "Aucune élimination.");
        Assert.IsFalse(KillCam.IsFresh(10.0, 9.9));

        KillCam.Info info = Kill(Health.DamageCause.Shot, 30f);
        info.startsAt = 25.0;
        float duration = KillCam.DurationOf(info);
        Assert.IsTrue(KillCam.BelongsTo(info, 20.0));
        Assert.IsFalse(KillCam.Freezes(info, 20.0, 24.9), "Avant le coup, en jeu.");
        Assert.IsTrue(KillCam.Freezes(info, 20.0, 25.0), "Dès le coup, tout le monde regarde.");
        Assert.IsTrue(KillCam.Freezes(info, 20.0, 25.0 + duration - 0.05));
        Assert.IsFalse(KillCam.Freezes(info, 20.0, 25.0 + duration + 0.05), "Après elle, le gagnant reprend la main.");
        Assert.IsFalse(KillCam.Freezes(info, 30.0, 31.0), "La kill cam d'une phase d'avant ne gèle rien.");
        Assert.IsFalse(KillCam.Freezes(default, 20.0, 25.0), "Sans kill cam, rien.");

        Assert.AreEqual(5f + duration, KillCam.EndScreenDelay(false, 5f, info), 1e-5f);
        Assert.AreEqual(5f, KillCam.EndScreenDelay(false, 5f, default), 1e-5f);
        Assert.AreEqual(0f, KillCam.EndScreenDelay(true, 5f, info), "Au Tournoi, l'écran du champion tout de suite.");
    }

    /// <summary>Le décor : tel quel au coup et à la fin, sans rupture (ni noir ni coupe : on reste dans la partie), presque
    /// noir à l'impact, le plus sombre de la kill cam. Vu échouer, un décor qui s'assombrit d'un coup à l'instant du tir.</summary>
    [Test]
    public void LeDecor_SAssombritDepuisLaPartie_EtYRevient()
    {
        foreach (KillCam.Timeline line in new[]
                 {
                     KillCam.TimelineFor(Kill(Health.DamageCause.HeadShot, 40f)),
                     KillCam.TimelineFor(Kill(Health.DamageCause.Glove, 1f)),
                 })
        {
            Assert.AreEqual(0f, KillCam.DimAt(-0.1f, line));
            Assert.AreEqual(0f, KillCam.DimAt(0f, line), 1e-5f, "Au coup, le décor tel quel.");
            Assert.AreEqual(0f, KillCam.DimAt(line.duration + 0.01f, line), "Après elle, le décor tel quel.");
            float darkest = 0f;
            float darkestAt = 0f;
            float previous = 0f;
            for (float t = 0f; t < line.duration; t += 0.01f)
            {
                float dim = KillCam.DimAt(t, line);
                Assert.That(dim, Is.InRange(0f, 1f));
                Assert.Less(Mathf.Abs(dim - previous), 0.04f, $"Sans rupture (à {t:F2} s).");
                previous = dim;
                if (dim <= darkest) continue;
                darkest = dim;
                darkestAt = t;
            }
            Assert.Less(previous, 0.04f, "Il rend la partie telle quelle au retour de la caméra.");
            Assert.AreEqual(KillCam.DimImpact, darkest, 1e-4f);
            Assert.That(darkestAt, Is.InRange(line.impact, line.impactEnd), "Le plus sombre, à l'impact.");
            Assert.AreEqual(KillCam.DimImpact, KillCam.DimAt(line.impact + KillCam.DimRise + 0.01f, line), 1e-4f);
        }
    }

    /// <summary>La légende : qui, avec quoi, à la tête, de combien (au mètre près, un au moins) ; au gant, le coup de gant.
    /// Vu échouer, la tête oubliée.</summary>
    [Test]
    public void LaLegende_DitQuiAvecQuoiOuEtDeCombien()
    {
        Assert.AreEqual("FAUCON · FP-12 · À LA TÊTE · 32 M", KillCam.Caption("Faucon", "FP-12", Health.DamageCause.HeadShot, 31.6f));
        Assert.AreEqual("FAUCON · FA-07 · 18 M", KillCam.Caption("Faucon", "FA-07", Health.DamageCause.Shot, 18.2f));
        Assert.AreEqual("FAUCON · COUP DE GANT", KillCam.Caption("Faucon", "FA-07", Health.DamageCause.Glove, 1.2f));
        Assert.AreEqual("ÉLOÏSE · PA-05 · 1 M", KillCam.Caption("Éloïse", "PA-05", Health.DamageCause.Shot, 0.3f));
        Assert.AreEqual("? · FA-07 · 5 M", KillCam.Caption(null, "FA-07", Health.DamageCause.Shot, 5f));
        var info = new KillCam.Info { distanceDm = KillCam.Decimeters(12.34f) };
        Assert.AreEqual(12.3f, info.Distance, 1e-4f, "La distance voyage en décimètres.");
        Assert.AreEqual(0, KillCam.Decimeters(float.NaN));
        Assert.AreEqual(ushort.MaxValue, KillCam.Decimeters(1e9f));
    }

    /// <summary>L'arrivée de la balle : sur le robot montré, le point du serveur tel quel ; à côté (il a continué un instant,
    /// ou c'est un tir d'après chez le tireur), ramené à BodyRadius de son axe, du même côté. Vu échouer, le point du
    /// serveur toujours gardé.</summary>
    [Test]
    public void LaBalle_ArriveSurLeRobotMontre()
    {
        var feet = new Vector3(2f, 0f, 30f);
        var head = new Vector3(2f, 1.65f, 30f);
        var near = new Vector3(2.2f, 1.2f, 30f);
        Assert.AreEqual(near, KillCam.OnBody(near, head, feet), "Sur le robot : le point du serveur.");

        Vector3 moved = KillCam.OnBody(new Vector3(4f, 1.2f, 30f), head, feet);
        Assert.AreEqual(2f + KillCam.BodyRadius, moved.x, 1e-4f, "À côté : ramené sur lui, du même côté.");
        Assert.AreEqual(1.2f, moved.y, 1e-4f);

        Vector3 above = KillCam.OnBody(new Vector3(2f, 4f, 30f), head, feet);
        Assert.AreEqual(1.65f + KillCam.BodyRadius, above.y, 1e-4f, "Au-dessus de sa tête : ramené sur elle.");
        Vector3 flat = KillCam.OnBody(feet + Vector3.right * 2f, feet, feet);
        Assert.AreEqual(KillCam.BodyRadius, Vector3.Distance(flat, feet), 1e-4f, "Un robot sans hauteur ne fait rien diverger.");
    }

    /// <summary>
    /// Les plans. La balle : la caméra part par-dessus l'épaule droite du tireur, derrière le canon, rejoint la course de la
    /// balle, la suit sans jamais reculer ni passer l'arrêt d'avant l'impact, et la balle arrive au bout. L'impact de côté,
    /// un peu en arrière, le point touché dans le cadre ; autour de l'épave, au-dessus, à côté de la ligne du tireur ; le
    /// coup de près vu de côté, les deux robots dans le cadre de la tête aux pieds. Vu échouer, la caméra de la balle entrée
    /// dans la victime.
    /// </summary>
    [Test]
    public void LesPlans_CadrentLaBalleLImpactEtLEpave()
    {
        var origin = new Vector3(0.3f, 1.5f, 0.5f);
        var end = new Vector3(0f, 1.4f, 30f);
        Vector3 direction = (end - origin).normalized;
        float length = Vector3.Distance(origin, end);
        KillCam.Shot launch = KillCam.BulletShot(origin, end, 0f, false, out Vector3 start);
        Assert.Less(Vector3.Distance(start, origin), 1e-3f, "La balle part de la bouche.");
        Assert.Less(Vector3.Dot(launch.position - origin, direction), -0.5f, "Derrière le canon.");
        Assert.Greater((launch.position - origin).x, 0.2f, "À droite du tireur.");
        Assert.Greater(launch.position.y, origin.y + 0.1f, "Au-dessus de son épaule.");

        float previousAlong = -10f;
        for (float p = 0f; p <= 1.0001f; p += 0.05f)
        {
            KillCam.Shot follow = KillCam.BulletShot(origin, end, p, false, out Vector3 bullet);
            float along = Vector3.Dot(follow.position - origin, direction);
            Assert.LessOrEqual(along, Vector3.Dot(bullet - origin, direction) + 1e-3f, "Derrière la balle.");
            Assert.LessOrEqual(along, length - KillCam.BulletCamStop + 1e-3f, "Elle s'arrête avant l'impact.");
            Assert.GreaterOrEqual(along, previousAlong - 1e-4f, "Elle ne recule jamais.");
            previousAlong = along;
            if (p * length >= KillCam.LaunchDistance)
                Assert.Less(Vector3.Distance(follow.position, origin + direction * along), 0.1f, "Au ras de la course de la balle.");
            Assert.Greater(Vector3.Dot(follow.rotation * Vector3.forward, (end - follow.position).normalized), 0.999f, "Tournée vers l'impact.");
        }
        Assert.AreEqual(-KillCam.LaunchBehind, Vector3.Dot(launch.position - origin, direction), 0.01f);
        KillCam.BulletShot(origin, end, 1f, false, out Vector3 arrived);
        Assert.Less(Vector3.Distance(arrived, end), 1e-3f, "La balle arrive à l'impact.");
        Assert.Less(KillCam.WakeLength, KillCam.BulletCamBehind - 0.15f, "Le sillage ne passe jamais derrière la caméra.");
        Assert.Less(KillCam.StreakLength, KillCam.BulletCamBehind - 0.15f, "La traînée non plus.");

        var hit = new Vector3(2f, 1.3f, 30f);
        for (float u = 0f; u <= 1f; u += 0.25f)
        {
            KillCam.Shot impact = KillCam.ImpactShot(hit, direction, u);
            Vector3 toCamera = impact.position - hit;
            Assert.That(toCamera.magnitude, Is.InRange(1.4f, 2.3f), "Tout près du point touché.");
            Assert.Greater(Mathf.Abs(Vector3.Dot(toCamera, new Vector3(direction.z, 0f, -direction.x).normalized)), 1.3f, "De côté.");
            Assert.Less(Vector3.Dot(toCamera, direction), 0f, "Un peu en arrière : on voit le coup arriver.");
            Assert.Less(Vector3.Angle(impact.rotation * Vector3.forward, hit - impact.position), impact.fieldOfView * 0.3f,
                        "Le point touché au milieu du cadre.");
        }

        var wreck = new Vector3(0f, 0.3f, 30f);
        Vector3 shooter = Vector3.zero;
        Vector3 toShooter = shooter - wreck;
        toShooter.y = 0f;
        foreach (KillCam.Side where in new[] { KillCam.Side.Right, KillCam.Side.Left })
        {
            float sideSign = where == KillCam.Side.Right ? 1f : -1f;
            float previousAngle = 180f;
            for (float u = 0f; u <= 1f; u += 0.25f)
            {
                KillCam.Shot orbit = KillCam.OrbitShot(wreck, shooter, u, where);
                Assert.That(Vector3.Distance(orbit.position, wreck), Is.InRange(3.3f, 4.4f));
                Assert.Greater(orbit.position.y, wreck.y + 0.5f, "Au-dessus de l'épave.");
                Vector3 toCamera = orbit.position - wreck;
                Assert.Greater(toCamera.x * sideSign, 0f, "Du côté où filmait l'impact.");
                toCamera.y = 0f;
                float angle = Vector3.Angle(toCamera, toShooter);
                Assert.Less(angle, previousAngle, "Elle tourne vers la ligne du tireur, par où elle rentrera.");
                previousAngle = angle;
            }
            Vector3 orbitStart = KillCam.OrbitShot(wreck, shooter, 0f, where).position - wreck;
            orbitStart.y = 0f;
            Assert.AreEqual(75f, Vector3.Angle(orbitStart, toShooter), 0.5f);
            Assert.AreEqual(25f, previousAngle, 0.5f);

            // L'épave prend la suite de l'impact : du même côté, la caméra n'a pas à traverser le robot.
            Vector3 impactEnd = KillCam.ImpactShot(wreck, Vector3.forward, 1f, where).position - wreck;
            impactEnd.y = 0f;
            Assert.Less(Vector3.Angle(impactEnd, orbitStart), 20f, "L'impact finit là où l'épave commence.");
        }

        var a = new Vector3(0f, 1.25f, 0f);
        var b = new Vector3(0f, 1.25f, 1.2f);
        for (float u = 0f; u <= 1f; u += 0.5f)
        {
            KillCam.Shot side = KillCam.SideShot(a, b, u);
            foreach (Vector3 chest in new[] { a, b })
            {
                foreach (Vector3 point in new[] { chest + Vector3.up * 0.55f, chest, chest - Vector3.up * 1.2f })
                {
                    Assert.Less(Vector3.Angle(side.rotation * Vector3.forward, point - side.position), side.fieldOfView * 0.5f,
                        "Les deux robots dans le cadre, de la tête aux pieds.");
                }
            }
        }
    }

    /// <summary>Le côté de la caméra : à droite du coup ; contre un mur, à gauche ; entre deux murs, le long du coup, derrière
    /// le point touché, et derrière le tireur pour un coup de près. Chaque plan regarde le point touché. Vu échouer, le coup
    /// filmé à droite quoi qu'il y ait.</summary>
    [Test]
    public void LaCamera_PrendLeCoteOuElleALaPlace()
    {
        Assert.AreEqual(KillCam.Side.Right, KillCam.BestSide(KillCam.SideRoom, KillCam.SideRoom, KillCam.SideRoom));
        Assert.AreEqual(KillCam.Side.Left, KillCam.BestSide(0.6f, KillCam.SideRoom, KillCam.SideRoom));
        Assert.AreEqual(KillCam.Side.Right, KillCam.BestSide(KillCam.SideRoom, 0.6f, KillCam.SideRoom));
        Assert.AreEqual(KillCam.Side.Back, KillCam.BestSide(0.6f, 1f, KillCam.SideRoom));

        var hit = new Vector3(0f, 1.3f, 30f);
        Assert.Greater(KillCam.ImpactShot(hit, Vector3.forward, 0.5f, KillCam.Side.Right).position.x, 1f);
        Assert.Less(KillCam.ImpactShot(hit, Vector3.forward, 0.5f, KillCam.Side.Left).position.x, -1f);
        KillCam.Shot back = KillCam.ImpactShot(hit, Vector3.forward, 0.5f, KillCam.Side.Back);
        Assert.Less(back.position.z, hit.z - 1.5f, "Le long du coup, derrière le point touché.");
        Assert.Less(Mathf.Abs(back.position.x), 0.5f);
        Assert.Less(Vector3.Angle(back.rotation * Vector3.forward, hit - back.position), back.fieldOfView * 0.3f);

        var shooter = new Vector3(0f, 1.25f, 0f);
        var victim = new Vector3(0f, 1.25f, 1.2f);
        Assert.Greater(KillCam.SideShot(shooter, victim, 0.5f, KillCam.Side.Right).position.x, 2f);
        Assert.Less(KillCam.SideShot(shooter, victim, 0.5f, KillCam.Side.Left).position.x, -2f);
        KillCam.Shot behind = KillCam.SideShot(shooter, victim, 0.5f, KillCam.Side.Back);
        Assert.Less(behind.position.z, shooter.z - 1.5f, "Derrière le tireur.");
        Assert.Less(Vector3.Angle(behind.rotation * Vector3.forward, victim - behind.position), behind.fieldOfView * 0.5f, "La victime dans le cadre.");
    }

    /// <summary>La caméra qui rentre : de l'épave aux yeux du tireur, sans saut, lente au départ et à l'arrivée, et près de
    /// la course de la balle (dégagée, elle) sur presque tout le trajet : elle ne coupe pas à travers le décor. Vu échouer,
    /// la ligne droite de l'épave aux yeux, à côté de la course.</summary>
    [Test]
    public void LaCamera_RentreAuJoueur_ParLaCourseDeLaBalle()
    {
        var eye = new Vector3(0f, 1.6f, 0f);
        var impact = new Vector3(0f, 1.3f, 30f);
        Vector3 from = impact + new Vector3(3.5f, 1.5f, 1f);
        Vector3 back = (eye - impact).normalized;
        Vector3 via = impact + back * KillCam.BulletCamStop;

        Assert.AreEqual(from, KillCam.ReturnPosition(from, via, eye, 0f));
        Assert.AreEqual(eye, KillCam.ReturnPosition(from, via, eye, 1f));
        Vector3 previous = from;
        float firstStep = Vector3.Distance(KillCam.ReturnPosition(from, via, eye, 0.02f), from);
        float largest = 0f;
        for (float u = 0.02f; u <= 1.0001f; u += 0.02f)
        {
            Vector3 position = KillCam.ReturnPosition(from, via, eye, u);
            largest = Mathf.Max(largest, Vector3.Distance(position, previous));
            previous = position;
            float along = Mathf.Clamp(Vector3.Dot(position - eye, -back), 0f, Vector3.Distance(eye, impact));
            float off = Vector3.Distance(position, eye - back * along);
            if (Vector3.Distance(position, from) > 6f) Assert.Less(off, 2.2f, $"Près de la course de la balle (à {u:F2}).");
        }
        Assert.Less(firstStep, largest * 0.1f, "Elle part doucement.");
        float lastStep = Vector3.Distance(KillCam.ReturnPosition(from, via, eye, 0.98f), eye);
        Assert.Less(lastStep, largest * 0.1f, "Elle arrive doucement.");
    }
}
