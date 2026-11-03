using NUnit.Framework;
using UnityEngine;

/// <summary>
/// La kill cam, le replay du coup mortel (KillReplay, 2026-11-03, refaite le même jour d'après Sniper Elite et Mortal
/// Kombat) : son déroulé et son temps, quand elle se joue, sa légende, ses plans, le décor qui s'assombrit, et
/// l'enregistrement des robots. Fonctions pures, et de vrais Transform pour l'enregistrement.
/// </summary>
public class KillReplayTests
{
    private static KillReplay.Info Kill(Health.DamageCause cause, float distance)
        => new KillReplay.Info { killer = 1, victim = 2, cause = cause, distanceDm = KillReplay.Decimeters(distance) };

    /// <summary>
    /// Le déroulé : la balle suivie pour un tir de loin, d'autant plus longtemps qu'il est long (au sniper d'un poste à
    /// l'autre, plus de deux secondes) ; de près (le gant, le pompe à bout portant), le coup et l'impact ne font qu'un. Vu
    /// échouer, le vol de la balle de la même durée à toutes les distances.
    /// </summary>
    [Test]
    public void LeDeroule_SuitLaBalleDAutantPlusLongtempsQueLeTirEstLong()
    {
        KillReplay.Timeline far = KillReplay.TimelineFor(Kill(Health.DamageCause.HeadShot, 56f));
        KillReplay.Timeline near = KillReplay.TimelineFor(Kill(Health.DamageCause.Shot, 8f));
        Assert.AreEqual(KillReplay.Style.Bullet, far.style);
        Assert.AreEqual(KillReplay.Style.Bullet, near.style);
        Assert.Greater(far.impact - far.shot, 2f, "Au sniper, d'un poste à l'autre : la balle suivie plus de deux secondes.");
        Assert.AreEqual(KillReplay.FlightMin, near.impact - near.shot, 1e-5f);
        Assert.Greater(far.duration, near.duration);
        Assert.AreEqual(KillReplay.FlightMax, KillReplay.FlightTime(500f), 1e-5f, "Jamais plus que FlightMax.");
        Assert.AreEqual(KillReplay.FlightMin, KillReplay.FlightTime(float.NaN), 1e-5f);

        foreach (KillReplay.Info info in new[] { Kill(Health.DamageCause.Glove, 1.2f), Kill(Health.DamageCause.Shot, 2.5f) })
        {
            KillReplay.Timeline close = KillReplay.TimelineFor(info);
            Assert.AreEqual(KillReplay.Style.Close, close.style, "Le gant, un tir à bout portant : de près.");
            Assert.AreEqual(close.shot, close.impact, "De près, le coup et l'impact ne font qu'un.");
        }

        foreach (KillReplay.Timeline line in new[] { far, near, KillReplay.TimelineFor(Kill(Health.DamageCause.Glove, 1f)) })
        {
            Assert.Less(0f, line.shot);
            Assert.LessOrEqual(line.shot, line.impact);
            Assert.Less(line.impact, line.impactEnd);
            Assert.Less(line.impactEnd, line.duration);
            Assert.Less(line.duration, 6f, "Une kill cam courte : la partie reprend vite.");
        }
        Assert.AreEqual(KillReplay.LeadIn + far.duration, KillReplay.PhaseExtension(Kill(Health.DamageCause.HeadShot, 56f)), 1e-5f);
    }

    /// <summary>
    /// Le temps montré : l'approche un peu ralentie, le vol de la balle le monde presque figé (figé quand la mort se voit
    /// dans l'image du coup), l'impact au plus lent, puis l'épave dont le temps revient à la normale ; jamais de retour
    /// en arrière ni de saut, de près comme à la balle ; sa vitesse (qui règle le ralenti des gerbes) est bien sa dérivée.
    /// Vu échouer, l'impact joué à la vitesse de l'épave.
    /// </summary>
    [Test]
    public void LeTempsMontre_RalentitSurLImpact_PuisRevientALaNormale()
    {
        const double shot = 100.0;
        const double death = 100.08;
        KillReplay.Timeline line = KillReplay.TimelineFor(Kill(Health.DamageCause.Shot, 30f));
        Assert.AreEqual(shot - line.lead, KillReplay.SourceTime(0f, line, shot, death), 1e-9);
        Assert.AreEqual(shot, KillReplay.SourceTime(line.shot, line, shot, death), 1e-6);
        Assert.AreEqual(death, KillReplay.SourceTime(line.impact, line, shot, death), 1e-6);
        Assert.AreEqual(death + KillReplay.ImpactGame, KillReplay.SourceTime(line.impactEnd, line, shot, death), 1e-6);
        Assert.AreEqual(death + KillReplay.ImpactGame + KillReplay.AftermathGame, KillReplay.SourceTime(line.duration, line, shot, death), 1e-6);
        Assert.AreEqual(shot - line.lead, KillReplay.SourceTime(-1f, line, shot, death), 1e-9, "Avant le début, le début.");
        Assert.AreEqual(KillReplay.SourceTime(line.duration, line, shot, death), KillReplay.SourceTime(line.duration + 2f, line, shot, death), 1e-9,
                        "Après la fin, la fin.");
        Assert.AreEqual(shot, KillReplay.SourceTime((line.shot + line.impact) * 0.5f, line, shot, shot), 1e-9,
                        "La mort vue dans l'image du coup : le monde figé pendant le vol.");

        Assert.Less(KillReplay.WarpRate((line.shot + line.impact) * 0.5f, line, shot, death), 0.1f, "Le vol, presque à l'arrêt.");
        Assert.Less(KillReplay.WarpRate((line.impact + line.impactEnd) * 0.5f, line, shot, death), 0.2f, "L'impact, au plus lent.");
        Assert.That(KillReplay.WarpRate(line.shot * 0.5f, line, shot, death), Is.InRange(0.3f, 0.8f), "L'approche, un peu ralentie.");
        Assert.Greater(KillReplay.WarpRate(line.duration - 0.01f, line, shot, death), 0.9f, "À la fin, le temps est revenu.");

        KillReplay.Timeline close = KillReplay.TimelineFor(Kill(Health.DamageCause.Shot, 2.5f));
        foreach ((KillReplay.Timeline l, double s, double d) in new[] { (line, shot, death), (close, shot, death), (close, shot, shot) })
        {
            double previous = double.NaN;
            for (float t = 0f; t <= l.duration + 0.005f; t += 0.01f)
            {
                double source = KillReplay.SourceTime(t, l, s, d);
                if (!double.IsNaN(previous))
                {
                    Assert.GreaterOrEqual(source, previous, $"Jamais de retour en arrière (à {t:F2} s).");
                    Assert.Less(source - previous, 0.0125, $"Jamais de saut (à {t:F2} s).");
                }
                previous = source;
            }
            // La vitesse est la dérivée du temps montré, loin des changements de plan.
            for (float t = 0.05f; t < l.duration - 0.05f; t += 0.05f)
            {
                if (Mathf.Abs(t - l.shot) < 0.02f || Mathf.Abs(t - l.impact) < 0.02f || Mathf.Abs(t - l.impactEnd) < 0.02f) continue;
                float slope = (float)((KillReplay.SourceTime(t + 0.005f, l, s, d) - KillReplay.SourceTime(t - 0.005f, l, s, d)) / 0.01);
                Assert.AreEqual(slope, KillReplay.WarpRate(t, l, s, d), 0.02f, $"La vitesse du temps montré (à {t:F2} s).");
            }
        }
        Assert.AreEqual(death, KillReplay.SourceTime(close.shot, close, shot, death), 1e-9, "De près, l'approche finit sur l'impact.");

        Assert.AreEqual(0f, KillReplay.FlightProgress(line.shot, line));
        Assert.AreEqual(1f, KillReplay.FlightProgress(line.impact, line));
        Assert.AreEqual(0f, KillReplay.FlightProgress(close.shot - 0.01f, close));
        Assert.AreEqual(1f, KillReplay.FlightProgress(close.shot, close));
    }

    /// <summary>La balle : lente en sortant du canon et en arrivant, vite entre les deux, du canon à l'impact, sans jamais
    /// reculer. Vu échouer, la balle à vitesse constante.</summary>
    [Test]
    public void LaBalle_SortDuCanonEtArriveLentement()
    {
        Assert.AreEqual(0f, KillReplay.BulletTravel(0f), 1e-6f);
        Assert.AreEqual(1f, KillReplay.BulletTravel(1f), 1e-6f);
        float previous = 0f;
        for (float u = 0.01f; u <= 1f; u += 0.01f)
        {
            float travel = KillReplay.BulletTravel(u);
            Assert.Greater(travel, previous, $"Jamais en arrière (à {u:F2}).");
            previous = travel;
        }
        float start = KillReplay.BulletTravel(0.05f) / 0.05f;
        float middle = (KillReplay.BulletTravel(0.55f) - KillReplay.BulletTravel(0.45f)) / 0.1f;
        float end = (1f - KillReplay.BulletTravel(0.95f)) / 0.05f;
        Assert.Less(start, 0.6f, "Elle sort du canon lentement.");
        Assert.Less(end, 0.6f, "Elle arrive lentement.");
        Assert.Greater(middle, 1.3f, "Entre les deux, vite.");
    }

    /// <summary>
    /// Quand la jouer : sur l'élimination de l'image (pas une zone tenue ni un module ramassé plus tard), aussitôt (le noir
    /// commence après le coup) ; les joueurs gelés le temps du replay, puis le gagnant reprend la main, et pas par le replay
    /// d'une phase d'avant ; l'écran de fin après elle et le temps du gagnant, au Tournoi tout de suite ; le noir à
    /// l'entrée et à la sortie. Vu échouer, le gagnant gelé jusqu'à la fin de la manche.
    /// </summary>
    [Test]
    public void LaKillCam_PartAussitot_GeleLesJoueurs_PuisRendLaMain()
    {
        Assert.IsTrue(KillReplay.IsFresh(10.0, 10.0));
        Assert.IsTrue(KillReplay.IsFresh(10.0, 10.2));
        Assert.IsFalse(KillReplay.IsFresh(10.0, 10.5), "Un module ramassé, une zone tenue après l'élimination : pas de replay.");
        Assert.IsFalse(KillReplay.IsFresh(0.0, 0.1), "Aucune élimination.");
        Assert.IsFalse(KillReplay.IsFresh(10.0, 9.9));
        Assert.Less(KillReplay.LeadIn, 0.5f, "Le déclenchement en jeu, pas des secondes après.");
        Assert.GreaterOrEqual(KillReplay.LeadIn, KillReplay.CutTime, "Le noir commence après le coup.");

        KillReplay.Info info = Kill(Health.DamageCause.Shot, 30f);
        info.startsAt = 25.0;
        float duration = KillReplay.DurationOf(info);
        Assert.IsTrue(KillReplay.BelongsTo(info, 20.0));
        Assert.IsFalse(KillReplay.Freezes(info, 20.0, 24.9), "Le coup porte, en jeu.");
        Assert.IsTrue(KillReplay.Freezes(info, 20.0, 25.0));
        Assert.IsTrue(KillReplay.Freezes(info, 20.0, 25.0 + duration - 0.05));
        Assert.IsFalse(KillReplay.Freezes(info, 20.0, 25.0 + duration + 0.05), "Après le replay, le gagnant reprend la main.");
        Assert.IsFalse(KillReplay.Freezes(info, 30.0, 31.0), "Le replay d'une phase d'avant ne gèle rien.");
        Assert.IsFalse(KillReplay.Freezes(default, 20.0, 25.0), "Sans replay, rien.");

        Assert.AreEqual(5f + KillReplay.LeadIn + duration, KillReplay.EndScreenDelay(false, 5f, info), 1e-5f);
        Assert.AreEqual(5f, KillReplay.EndScreenDelay(false, 5f, default), 1e-5f);
        Assert.AreEqual(0f, KillReplay.EndScreenDelay(true, 5f, info), "Au Tournoi, l'écran du champion tout de suite.");

        Assert.AreEqual(1f, KillReplay.CutDip(0f, duration), 1e-5f, "Le noir, au passage au replay.");
        Assert.AreEqual(0.5f, KillReplay.CutDip(-KillReplay.CutTime * 0.5f, duration), 1e-4f);
        Assert.AreEqual(0.5f, KillReplay.CutDip(KillReplay.CutTime * 0.5f, duration), 1e-4f);
        Assert.AreEqual(0f, KillReplay.CutDip(-KillReplay.CutTime - 0.01f, duration));
        Assert.AreEqual(0f, KillReplay.CutDip(duration * 0.5f, duration), "Pendant le replay, pas de noir.");
        Assert.AreEqual(1f, KillReplay.CutDip(duration, duration), 1e-5f, "Le noir, au retour à la partie.");
        Assert.AreEqual(0.5f, KillReplay.CutDip(duration + KillReplay.CutTime * 0.5f, duration), 1e-4f);
        Assert.AreEqual(0f, KillReplay.CutDip(duration + KillReplay.CutTime + 0.01f, duration));
    }

    /// <summary>Le décor assombri : un peu pendant l'approche, presque noir à l'impact (le plus sombre du replay), puis un
    /// peu moins ; rien hors du replay. Vu échouer, l'impact pas plus sombre que l'approche.</summary>
    [Test]
    public void LeDecor_SAssombritJusquAuNoirALImpact()
    {
        foreach (KillReplay.Timeline line in new[]
                 {
                     KillReplay.TimelineFor(Kill(Health.DamageCause.HeadShot, 40f)),
                     KillReplay.TimelineFor(Kill(Health.DamageCause.Glove, 1f)),
                 })
        {
            Assert.AreEqual(0f, KillReplay.DimAt(-0.1f, line));
            Assert.AreEqual(0f, KillReplay.DimAt(line.duration + 0.01f, line), "Hors du replay, le décor tel quel.");
            float darkest = 0f;
            float darkestAt = 0f;
            for (float t = 0f; t < line.duration; t += 0.01f)
            {
                float dim = KillReplay.DimAt(t, line);
                Assert.That(dim, Is.InRange(0f, 1f));
                if (dim <= darkest) continue;
                darkest = dim;
                darkestAt = t;
            }
            Assert.AreEqual(KillReplay.DimImpact, darkest, 1e-4f);
            Assert.That(darkestAt, Is.InRange(line.impact, line.impactEnd), "Le plus sombre, à l'impact.");
            Assert.Less(KillReplay.DimAt(line.shot * 0.5f, line), 0.5f, "Pendant l'approche, on voit encore l'arène.");
            Assert.AreEqual(KillReplay.DimImpact, KillReplay.DimAt(line.impact + KillReplay.DimRise + 0.01f, line), 1e-4f);
        }
    }

    /// <summary>La légende : qui, avec quoi, à la tête, de combien (au mètre près, un au moins) ; au gant, le coup de gant.
    /// Vu échouer, la tête oubliée.</summary>
    [Test]
    public void LaLegende_DitQuiAvecQuoiOuEtDeCombien()
    {
        Assert.AreEqual("FAUCON · FP-12 · À LA TÊTE · 32 M", KillReplay.Caption("Faucon", "FP-12", Health.DamageCause.HeadShot, 31.6f));
        Assert.AreEqual("FAUCON · FA-07 · 18 M", KillReplay.Caption("Faucon", "FA-07", Health.DamageCause.Shot, 18.2f));
        Assert.AreEqual("FAUCON · COUP DE GANT", KillReplay.Caption("Faucon", "FA-07", Health.DamageCause.Glove, 1.2f));
        Assert.AreEqual("ÉLOÏSE · PA-05 · 1 M", KillReplay.Caption("Éloïse", "PA-05", Health.DamageCause.Shot, 0.3f));
        Assert.AreEqual("? · FA-07 · 5 M", KillReplay.Caption(null, "FA-07", Health.DamageCause.Shot, 5f));
        var info = new KillReplay.Info { distanceDm = KillReplay.Decimeters(12.34f) };
        Assert.AreEqual(12.3f, info.Distance, 1e-4f, "La distance voyage en décimètres.");
        Assert.AreEqual(0, KillReplay.Decimeters(float.NaN));
        Assert.AreEqual(ushort.MaxValue, KillReplay.Decimeters(1e9f));
    }

    /// <summary>
    /// Les plans : par-dessus l'épaule droite du tireur, tourné vers la victime ; la balle suivie de derrière, au ras de
    /// sa course, sans jamais reculer ni passer l'arrêt d'avant l'impact, la balle arrivée au bout ; l'impact de côté, un
    /// peu en arrière, le point touché dans le cadre ; autour de l'épave, au-dessus, à côté de la ligne du tireur ; le coup
    /// de près vu de côté, les deux robots dans le cadre de la tête aux pieds. Vu échouer, la caméra de la balle entrée
    /// dans la victime.
    /// </summary>
    [Test]
    public void LesPlans_CadrentLeTireurLaBalleLImpactEtLEpave()
    {
        var eye = new Vector3(0f, 1.6f, 0f);
        var target = new Vector3(0f, 1.3f, 20f);
        for (float u = 0f; u <= 1f; u += 0.25f)
        {
            KillReplay.Shot shoulder = KillReplay.ShoulderShot(eye, target, u);
            Assert.Less(shoulder.position.z, eye.z - 0.5f, "Derrière le tireur.");
            Assert.Greater(shoulder.position.x, eye.x + 0.2f, "Par-dessus son épaule droite.");
            Assert.Greater(Vector3.Dot(shoulder.rotation * Vector3.forward, (target - shoulder.position).normalized), 0.999f, "Tourné vers la victime.");
        }

        var origin = new Vector3(0.3f, 1.5f, 0.5f);
        var end = new Vector3(0f, 1.4f, 30f);
        Vector3 direction = (end - origin).normalized;
        float length = Vector3.Distance(origin, end);
        float previousAlong = -1f;
        for (float p = 0f; p <= 1.0001f; p += 0.05f)
        {
            KillReplay.Shot follow = KillReplay.BulletShot(origin, end, p, false, out Vector3 bullet);
            float along = Vector3.Dot(follow.position - origin, direction);
            Assert.LessOrEqual(along, Vector3.Dot(bullet - origin, direction) + 1e-3f, "Derrière la balle.");
            Assert.LessOrEqual(along, length - KillReplay.BulletCamStop + 1e-3f, "Elle s'arrête avant l'impact.");
            Assert.GreaterOrEqual(along, previousAlong - 1e-4f, "Elle ne recule jamais.");
            previousAlong = along;
            Assert.Less(Vector3.Distance(follow.position, origin + direction * along), 0.1f, "Au ras de la course de la balle.");
            Assert.Greater(Vector3.Dot(follow.rotation * Vector3.forward, (end - follow.position).normalized), 0.999f, "Tournée vers l'impact.");
        }
        KillReplay.BulletShot(origin, end, 1f, false, out Vector3 arrived);
        Assert.Less(Vector3.Distance(arrived, end), 1e-3f, "La balle arrive à l'impact.");
        Assert.Less(KillReplay.WakeLength, KillReplay.BulletCamBehind - 0.15f, "Le sillage ne passe jamais derrière la caméra.");
        Assert.Less(KillReplay.StreakLength, KillReplay.BulletCamBehind - 0.15f, "La traînée non plus.");

        var hit = new Vector3(2f, 1.3f, 30f);
        for (float u = 0f; u <= 1f; u += 0.25f)
        {
            KillReplay.Shot impact = KillReplay.ImpactShot(hit, direction, u);
            Vector3 toCamera = impact.position - hit;
            Assert.That(toCamera.magnitude, Is.InRange(1.4f, 2.3f), "Tout près du point touché.");
            Assert.Greater(Mathf.Abs(Vector3.Dot(toCamera, new Vector3(direction.z, 0f, -direction.x).normalized)), 1.3f, "De côté.");
            Assert.Less(Vector3.Dot(toCamera, direction), 0f, "Un peu en arrière : on voit le coup arriver.");
            Assert.Less(Vector3.Angle(impact.rotation * Vector3.forward, hit - impact.position), impact.fieldOfView * 0.3f,
                        "Le point touché au milieu du cadre.");
        }

        var wreck = new Vector3(0f, 0.3f, 30f);
        Vector3 shooter = Vector3.zero;
        for (float u = 0f; u <= 1f; u += 0.25f)
        {
            KillReplay.Shot orbit = KillReplay.OrbitShot(wreck, shooter, u);
            Assert.That(Vector3.Distance(orbit.position, wreck), Is.InRange(3.3f, 4.4f));
            Assert.Greater(orbit.position.y, wreck.y + 0.5f, "Au-dessus de l'épave.");
            Vector3 toCamera = orbit.position - wreck;
            toCamera.y = 0f;
            Vector3 toShooter = shooter - wreck;
            toShooter.y = 0f;
            Assert.Greater(Vector3.Angle(toCamera, toShooter), 60f, "À côté de la ligne du tireur, pas dans son dos.");
        }

        var a = new Vector3(0f, 1.25f, 0f);
        var b = new Vector3(0f, 1.25f, 1.2f);
        for (float u = 0f; u <= 1f; u += 0.5f)
        {
            KillReplay.Shot side = KillReplay.SideShot(a, b, u);
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

    /// <summary>L'enregistrement d'un robot : entre deux images, le mélange des deux (la racine dans le monde, les os sous
    /// elle) ; au-delà de sa capacité, les plus anciennes s'en vont ; un os disparu garde sa dernière pose ; et le robot ne
    /// se fond jamais dans son épave. Vu échouer, l'épave mêlée au robot.</summary>
    [Test]
    public void LEnregistrement_RejoueLesPoses_SansFondreLeRobotDansSonEpave()
    {
        Transform root = new GameObject("Racine").transform;
        Transform bone = new GameObject("Os").transform;
        bone.SetParent(root, false);
        Transform other = new GameObject("Autre").transform;
        other.SetParent(root, false);
        Transform puppetRoot = new GameObject("Marionnette").transform;
        Transform puppetBone = new GameObject("Os").transform;
        puppetBone.SetParent(puppetRoot, false);
        Transform puppetOther = new GameObject("Autre").transform;
        puppetOther.SetParent(puppetRoot, false);
        try
        {
            var recording = new KillReplay.Recording(2, 4);
            Transform[] bones = { bone, other };
            Transform[] puppet = { puppetBone, puppetOther };
            for (int i = 0; i < 3; i++)
            {
                root.position = new Vector3(i, 0f, 0f);
                bone.localRotation = Quaternion.Euler(0f, 0f, 10f * i);
                other.localPosition = new Vector3(0f, i, 0f);
                recording.Add(i * 0.1, root, bones, false);
            }
            Assert.IsFalse(recording.Pose(0.15, puppetRoot, puppet));
            Assert.AreEqual(1.5f, puppetRoot.position.x, 1e-4f, "Entre deux images, le mélange des deux.");
            Assert.AreEqual(15f, puppetBone.localEulerAngles.z, 1e-3f);
            Assert.AreEqual(1.5f, puppetOther.localPosition.y, 1e-4f);
            recording.Pose(-5.0, puppetRoot, puppet);
            Assert.AreEqual(0f, puppetRoot.position.x, 1e-4f, "Avant la première image, la première.");
            recording.Pose(9.0, puppetRoot, puppet);
            Assert.AreEqual(2f, puppetRoot.position.x, 1e-4f, "Après la dernière, la dernière.");

            root.position = new Vector3(3f, 0f, 0f);
            recording.Add(0.3, root, new[] { bone, null }, false);
            recording.Pose(0.3, puppetRoot, puppet);
            Assert.AreEqual(2f, puppetOther.localPosition.y, 1e-4f, "Un os disparu garde sa dernière pose.");

            root.position = new Vector3(10f, 0f, 0f);
            recording.Add(0.4, root, bones, true);
            Assert.AreEqual(4, recording.Count, "Au-delà de sa capacité, la plus ancienne s'en va.");
            Assert.AreEqual(0.1, recording.Oldest, 1e-9);
            Assert.AreEqual(0.4, recording.Newest, 1e-9);
            Assert.IsFalse(recording.Pose(0.39, puppetRoot, puppet), "Juste avant l'épave, le robot.");
            Assert.AreEqual(3f, puppetRoot.position.x, 1e-4f, "Rien de l'épave mêlé au robot.");
            Assert.IsTrue(recording.Pose(0.4, puppetRoot, puppet));
            Assert.AreEqual(10f, puppetRoot.position.x, 1e-4f);

            recording.Clear();
            Assert.AreEqual(0, recording.Count);
            Assert.IsFalse(recording.Pose(0.4, puppetRoot, puppet), "Vide : rien à poser.");
        }
        finally
        {
            Object.DestroyImmediate(root.gameObject);
            Object.DestroyImmediate(puppetRoot.gameObject);
        }
    }
}
