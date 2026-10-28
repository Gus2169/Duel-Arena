using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// La Fosse (2026-10-16), le parcours chronométré du stand de tir, sur le modèle de « The Pit » de
/// Modern Warfare 2 : quand la course part, finit ou s'abandonne ; le temps final et ses pénalités ;
/// la pose d'une cible (lever, chute, rail) ; les records ; le bilan ; et la scène construite, où chaque
/// cible doit tenir debout sur un sol, hors des murs, et dont on doit pouvoir faire le tour à pied.
///
/// Ce qui casse ici ne lève aucune erreur : un chrono qui part en marchant à reculons, une arrivée
/// comptée sans avoir fait le tour, un bonus de précision oublié, une cible plantée dans un mur (on ne
/// peut plus la toucher, et chaque course la compte manquée).
/// </summary>
public class ShootingCourseTests
{
    private const string ScenePath = "Assets/_ProjectScenes/StandDeTir.unity";

    [Test]
    public void LaCourse_PartDuDepart_FinitParLeDernierCouloir_EtSAbandonneSinon()
    {
        const int final = 7;
        Assert.AreEqual(CourseEvent.Start, ShootingCourse.Evaluate(false, true, false, true, -1, final), "Quitter le départ pour l'entrée lance le chrono.");
        Assert.AreEqual(CourseEvent.None, ShootingCourse.Evaluate(false, false, false, true, -1, final), "Entrer dans l'entrée par ailleurs (à reculons) ne lance rien.");
        Assert.AreEqual(CourseEvent.None, ShootingCourse.Evaluate(false, true, true, false, -1, final), "Rester au départ ne lance rien.");
        Assert.AreEqual(CourseEvent.Finish, ShootingCourse.Evaluate(true, false, true, false, final, final), "Revenir au départ par le dernier couloir : l'arrivée.");
        Assert.AreEqual(CourseEvent.Abort, ShootingCourse.Evaluate(true, false, true, false, 3, final), "Revenir au départ avant la fin : l'abandon.");
        Assert.AreEqual(CourseEvent.None, ShootingCourse.Evaluate(true, true, false, true, 0, final), "En course, hors du départ : rien.");
    }

    [Test]
    public void LeTempsFinal_SuitLesReglesDeLaFosse()
    {
        Assert.AreEqual(30f, ShootingCourse.FinalTime(30f, 1, 1, 0.8f), 1e-4f, "+2 s par hostile manqué, +2 s par civil touché, -0,05 s par point de précision.");
        Assert.AreEqual(10.72f, ShootingCourse.FinalTime(11.47f, 1, 1, 0.95f), 1e-3f, "La course mesurée en jeu (2026-10-16).");
        Assert.AreEqual(25f, ShootingCourse.FinalTime(25f, 0, 0, 0f), 1e-4f, "Sans tirer ni manquer : le temps brut.");
        Assert.AreEqual(0f, ShootingCourse.FinalTime(1f, 0, 0, 1f), 1e-4f, "Jamais négatif.");
        Assert.AreEqual(20f, ShootingCourse.FinalTime(20f, -3, -1, 0f), 1e-4f, "Un compte négatif ne retranche rien.");
    }

    [Test]
    public void UneCible_SeLeve_Tombe_EtVaEtVientSurSonRail()
    {
        var hidden = new CourseTargetMotion { state = CourseTargetState.Hidden };
        var up = new CourseTargetMotion { state = CourseTargetState.Up, raisedAt = 10.0 };
        var down = new CourseTargetMotion { state = CourseTargetState.Down, raisedAt = 10.0, downAt = 20.0 };
        Assert.AreEqual(90f, CourseTarget.PoseAngle(hidden, 50.0), 1e-3f, "Cachée : couchée.");
        Assert.AreEqual(90f, CourseTarget.PoseAngle(up, 10.0), 1e-3f, "Le lever part de couchée.");
        Assert.AreEqual(45f, CourseTarget.PoseAngle(up, 10.0 + CourseTarget.RiseTime / 2), 1e-3f, "À mi-lever, à mi-chemin.");
        Assert.AreEqual(0f, CourseTarget.PoseAngle(up, 10.0 + CourseTarget.RiseTime), 1e-3f, "Debout au bout du lever.");
        Assert.AreEqual(0f, CourseTarget.PoseAngle(down, 20.0), 1e-3f, "La chute part de debout.");
        Assert.Less(CourseTarget.PoseAngle(down, 20.0 + CourseTarget.FallTime / 2), 45f, "La chute accélère.");
        Assert.AreEqual(90f, CourseTarget.PoseAngle(down, 21.0), 1e-3f, "Tombée : couchée.");

        Vector3 a = new Vector3(55f, 0f, 21f), b = new Vector3(60f, 0f, 21f);
        Assert.AreEqual(0f, Vector3.Distance(a, CourseTarget.RailPosition(a, b, 2f, 0.0)), 1e-4f);
        Assert.AreEqual(0f, Vector3.Distance(new Vector3(57f, 0f, 21f), CourseTarget.RailPosition(a, b, 2f, 1.0)), 1e-4f, "2 m/s pendant 1 s.");
        Assert.AreEqual(0f, Vector3.Distance(b, CourseTarget.RailPosition(a, b, 2f, 2.5)), 1e-4f, "Au bout du rail.");
        Assert.AreEqual(0f, Vector3.Distance(new Vector3(57f, 0f, 21f), CourseTarget.RailPosition(a, b, 2f, 4.0)), 1e-4f, "Elle revient sur ses pas.");
        Assert.AreEqual(0f, Vector3.Distance(a, CourseTarget.RailPosition(a, b, 2f, 5.0)), 1e-4f, "Et repart.");
    }

    [Test]
    public void UnRecord_NeTombeQuePourUnMeilleurTemps()
    {
        Assert.IsTrue(CourseRecords.IsImprovement(float.NaN, 31.2f), "Le premier temps est un record.");
        Assert.IsTrue(CourseRecords.IsImprovement(31.2f, 30.9f));
        Assert.IsFalse(CourseRecords.IsImprovement(31.2f, 31.2f), "L'égalité ne bat pas le record.");
        Assert.IsFalse(CourseRecords.IsImprovement(31.2f, 35f));
        Assert.IsFalse(CourseRecords.IsImprovement(31.2f, float.NaN), "Un temps invalide ne bat rien.");
        Assert.IsFalse(CourseRecords.IsImprovement(float.NaN, -1f));
    }

    [Test]
    public void LeBilan_SEcritALaFrancaise()
    {
        Assert.AreEqual("41,20 s", ShootingCourseHud.Seconds(41.2f));
        Assert.AreEqual("27,43", ShootingCourseHud.Chrono(27.43f));
        Assert.AreEqual("1:02,15", ShootingCourseHud.Chrono(62.15f));

        var r = new CourseResult { run = 1, rawTime = 11.47f, missedHostiles = 1, civiliansHit = 1, accuracy = 0.95f, finalTime = 10.72f, record = 1, previousBest = float.NaN };
        string[] lines = ShootingCourseHud.ResultLines(r, "fusil d'assaut");
        CollectionAssert.Contains(lines, "Temps   11,47 s");
        CollectionAssert.Contains(lines, "Hostiles manqués   1   +2,00 s");
        CollectionAssert.Contains(lines, "Précision   95 %   −4,75 s");
        Assert.AreEqual("TEMPS FINAL   10,72 s", lines[lines.Length - 2]);
        Assert.AreEqual("Premier record · fusil d'assaut", lines[lines.Length - 1]);

        r.previousBest = 12f;
        Assert.AreEqual("Nouveau record · fusil d'assaut (ancien : 12,00 s)", ShootingCourseHud.ResultLines(r, "fusil d'assaut").Last());
        r.record = 0;
        r.previousBest = 9.5f;
        Assert.AreEqual("Record · fusil d'assaut : 9,50 s", ShootingCourseHud.ResultLines(r, "fusil d'assaut").Last());
    }

    /// <summary>La scène construite : le parcours, ses sections dans l'ordre, et des cibles qui tiennent
    /// debout sur un sol sans toucher un mur (vrais colliders, scène ouverte en plus de la courante).</summary>
    [Test]
    public void LaFosse_SesCiblesTiennentDebout_SurUnSol_HorsDesMurs()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            Physics.SyncTransforms();
            ShootingCourse course = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ShootingCourse>(true)).SingleOrDefault();
            Assert.IsNotNull(course, "La Fosse est dans le stand (Duel Arena > Stand de tir > Construire le stand).");
            Assert.AreEqual(ShootingRangeBuilder.CourseSections.Length, course.Sections.Count, "Ses sections, dans l'ordre.");
            // Le départ ne fait que border l'entrée et le dernier couloir : s'il débordait sur une section,
            // la course finirait (ou s'abandonnerait) en pleine section.
            foreach (ShootingCourse.Section section in course.Sections)
                Assert.IsFalse(section.bounds.Intersects(Shrink(course.StartZone)), $"La zone de départ déborde sur « {section.name} ».");
            Assert.IsTrue(Grow(course.StartZone).Intersects(course.Sections[0].bounds), "Le départ mène à l'entrée.");
            Assert.IsTrue(Grow(course.StartZone).Intersects(course.Sections[course.Sections.Count - 1].bounds), "Le dernier couloir ramène au départ.");

            CourseTarget[] targets = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CourseTarget>(true)).ToArray();
            Assert.GreaterOrEqual(targets.Count(t => t.IsHostile), 20, "Les hostiles de la Fosse.");
            Assert.GreaterOrEqual(targets.Count(t => !t.IsHostile), 8, "Les civils.");
            for (int s = 0; s < course.Sections.Count; s++)
                Assert.IsTrue(targets.Any(t => t.Section == s && t.IsHostile), $"La section « {course.Sections[s].name} » a ses hostiles.");

            int world = LayerMask.GetMask("Default");
            foreach (CourseTarget t in targets)
            {
                Vector3 feet = t.transform.position;
                Assert.IsTrue(Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 0.6f, world, QueryTriggerInteraction.Ignore),
                              $"{t.name} tient sur un sol.");
                Assert.AreEqual(feet.y, ground.point.y, 0.02f, $"{t.name} a les pieds au sol.");
                Assert.IsFalse(Physics.CheckCapsule(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.5f, 0.3f, world, QueryTriggerInteraction.Ignore),
                               $"{t.name} n'est dans aucun mur.");
                Assert.Less(t.Section, course.Sections.Count, $"{t.name} appartient à une section du parcours.");
            }

            // Une cible qui va et vient reste sur un sol et hors des murs tout le long de son rail.
            foreach (ShootingRangeBuilder.CourseTargetSpec spec in ShootingRangeBuilder.CourseTargets.Where(s => s.moves))
            {
                for (int i = 0; i <= 6; i++)
                {
                    Vector3 feet = Vector3.Lerp(spec.position, spec.railEnd, i / 6f);
                    Assert.IsTrue(Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, 0.6f, world, QueryTriggerInteraction.Ignore),
                                  $"La cible du rail {spec.position} → {spec.railEnd} tient sur un sol à {feet}.");
                    Assert.IsFalse(Physics.CheckCapsule(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.5f, 0.3f, world, QueryTriggerInteraction.Ignore),
                                   $"La cible du rail {spec.position} → {spec.railEnd} entre dans un mur à {feet}.");
                }
            }
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// Le tour de la Fosse à pied (2026-10-28, la Fosse refaite « plus complexe ») : la capsule d'un robot (celle du préfab
    /// joueur : 1,8 m de haut, 35 cm de rayon, des marches de 30 cm au plus) suit le parcours (CourseRoute) avec les vrais
    /// colliders, en marchant, accroupie sous la gaine (1,1 m de haut), en franchissant les barricades selon les règles du
    /// vault (un obstacle de 0,3 à 1,3 m, 15 cm d'épaisseur au moins, une réception 1,5 m plus bas au plus). Elle doit
    /// passer partout sans rester coincée, monter au toit et en redescendre, et entrer dans les sections dans leur ordre,
    /// du départ à l'arrivée. Ce qui le casse ne se voit pas ailleurs : une porte trop étroite, une marche trop haute, un
    /// mur posé en travers du chemin, deux sections dans le désordre (la course finirait sans le tour complet).
    /// </summary>
    [Test]
    public void LaFosse_SeParcourtAPied_EnEntrantDansLesSectionsDansLOrdre()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        // Sur le layer des épaves, qui ne touche que le décor : ni les cibles ni les mannequins (le joueur, lui, les écarte).
        var walker = new GameObject("Robot qui fait le tour (test)") { layer = LayerMask.NameToLayer("Corpse") };
        try
        {
            Physics.SyncTransforms();
            ShootingCourse course = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ShootingCourse>(true)).SingleOrDefault();
            Assert.IsNotNull(course, "La Fosse est dans le stand (Duel Arena > Stand de tir > Construire le stand).");
            var controller = walker.AddComponent<CharacterController>();
            controller.radius = 0.35f;
            controller.skinWidth = 0.08f;
            controller.stepOffset = 0.3f;
            controller.slopeLimit = 45f;
            SetHeight(controller, 1.8f);

            ShootingRangeBuilder.RouteStep[] route = ShootingRangeBuilder.CourseRoute;
            Teleport(controller, route[0].point);
            Assert.IsTrue(course.StartZone.Contains(walker.transform.position), "Le tour part du départ.");
            var order = new List<int>();
            void Track()
            {
                Vector3 feet = walker.transform.position;
                for (int s = 0; s < course.Sections.Count; s++)
                    if (!order.Contains(s) && course.Sections[s].bounds.Contains(feet)) order.Add(s);
            }

            for (int i = 1; i < route.Length; i++)
            {
                ShootingRangeBuilder.RouteStep step = route[i];
                switch (step.move)
                {
                    case ShootingRangeBuilder.RouteMove.Vault:
                        Vault(controller, step.point, i);
                        break;
                    case ShootingRangeBuilder.RouteMove.Crouch:
                        // Le passage demande vraiment de s'accroupir (le marquage « ACCROUPI » ne ment pas) : debout, la
                        // capsule d'un robot n'y tient pas.
                        Vector3 under = (walker.transform.position + step.point) * 0.5f;
                        Assert.IsTrue(Physics.CheckCapsule(under + Vector3.up * 0.43f, under + Vector3.up * 1.53f, 0.35f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore),
                                      $"Au pas {i}, on ne passe pas debout : il faut s'accroupir.");
                        SetHeight(controller, 1.1f);
                        Walk(controller, step.point, i, Track);
                        Vector3 feet = walker.transform.position;
                        Assert.IsFalse(Physics.CheckCapsule(feet + Vector3.up * 0.43f, feet + Vector3.up * 1.53f, 0.35f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore),
                                       $"Après le passage accroupi (pas {i}), le robot peut se relever.");
                        SetHeight(controller, 1.8f);
                        break;
                    default:
                        Walk(controller, step.point, i, Track);
                        break;
                }
                Track();
                Assert.AreEqual(step.point.y, walker.transform.position.y, 0.35f, $"Au pas {i}, le robot est à la hauteur attendue ({step.point}).");
            }

            Assert.IsTrue(course.StartZone.Contains(walker.transform.position), "Le tour finit au départ, l'arrivée.");
            CollectionAssert.AreEqual(Enumerable.Range(0, course.Sections.Count).ToList(), order,
                                      "Le robot entre dans les sections dans leur ordre, sans en sauter : " +
                                      string.Join(" → ", order.Select(s => course.Sections[s].name)));
        }
        finally
        {
            Object.DestroyImmediate(walker);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// La salle de précision (2026-10-28) se rejoint à pied depuis le poste de tir du stand, par la porte à sa gauche ; du
    /// comptoir, debout comme accroupi (l'œil à 1,65 et 1,05 m), la cible se voit et se touche en plein centre à chacune
    /// des distances du pupitre ; chaque bouton du pupitre se vise d'où l'on tire, à portée de main, sans rien devant lui.
    /// Ce qui le casse ne lève aucune erreur : un comptoir trop haut, un pilastre ou le pupitre dans la ligne de mire, un
    /// pupitre hors de portée, une porte trop étroite, une cible posée à une autre distance que son bouton.
    /// </summary>
    [Test]
    public void LaSalleDePrecision_SeRejointAPied_SaCibleSeVoitDuComptoir_SonPupitreEstAPortee()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var walker = new GameObject("Robot qui va à la salle de précision (test)") { layer = LayerMask.NameToLayer("Corpse") };
        AimRange aim = null;
        int before = 0;
        try
        {
            Physics.SyncTransforms();
            aim = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<AimRange>(true)).SingleOrDefault();
            Assert.IsNotNull(aim, "La salle de précision est dans le stand (Duel Arena > Stand de tir > Construire le stand).");
            before = aim.TargetDistance;
            var controller = walker.AddComponent<CharacterController>();
            controller.radius = 0.35f;
            controller.skinWidth = 0.08f;
            controller.stepOffset = 0.3f;
            controller.slopeLimit = 45f;
            SetHeight(controller, 1.8f);

            // Du poste de tir du stand à la porte de gauche, puis au comptoir.
            Teleport(controller, ShootingRangeBuilder.FiringPost);
            Vector3[] route = { new Vector3(1f, 0f, -6f), new Vector3(-4.5f, 0f, -6f), ShootingRangeBuilder.AimShootingSpot };
            for (int i = 0; i < route.Length; i++) Walk(controller, route[i], i + 1, () => { });
            Vector3 feet = walker.transform.position;
            Assert.Less(Vector2.Distance(new Vector2(feet.x, feet.z), new Vector2(ShootingRangeBuilder.AimShootingSpot.x, ShootingRangeBuilder.AimShootingSpot.z)), 0.15f,
                        "Le robot est au comptoir.");
            Assert.AreEqual(0f, feet.y, 0.1f, "Garde : il est au sol.");

            int world = LayerMask.GetMask("Default");
            foreach (int d in AimRange.Distances)
            {
                aim.SnapCarrier(d);
                Vector3 center = aim.Board.position - aim.Board.forward * (AimRange.BoardThickness * 0.5f);
                Assert.AreEqual(d, center.z, 0.01f, $"À {d} m, la face de la cible est à {d} m de la ligne de tir.");
                foreach (float eyeHeight in new[] { 1.65f, 1.05f })
                {
                    Vector3 eye = feet + Vector3.up * eyeHeight;
                    Assert.IsTrue(Physics.Raycast(eye, (center - eye).normalized, out RaycastHit hit, 80f, world, QueryTriggerInteraction.Ignore),
                                  $"À {d} m, l'œil à {eyeHeight} m voit quelque chose.");
                    Assert.AreEqual(aim.Board, hit.collider.transform, $"À {d} m, l'œil à {eyeHeight} m voit la cible, pas {hit.collider.name}.");
                    Assert.IsTrue(AimRange.TryBoardPoint(aim.Board, hit.point, hit.normal, out Vector2 local), $"À {d} m, le tir arrive sur la face.");
                    Assert.AreEqual(10, AimRange.RingScore(local), $"À {d} m, viser le centre fait 10.");
                }
            }

            Vector3 standing = feet + Vector3.up * 1.65f;
            for (int i = 0; i < AimRange.ButtonCount; i++)
            {
                Rect r = AimRange.ButtonRect(i);
                Vector3 target = aim.Console.TransformPoint(new Vector3(r.center.x, r.center.y, 0f));
                Assert.AreEqual(i, AimRange.AimedButton(aim.Console, standing, (target - standing).normalized, aim.Reach), $"Le bouton {i} se vise du comptoir.");
                Assert.IsFalse(Physics.Linecast(standing, target - aim.Console.forward * 0.004f, world, QueryTriggerInteraction.Ignore), $"Rien ne cache le bouton {i}.");
            }
        }
        finally
        {
            if (aim != null) aim.SnapCarrier(before);
            Object.DestroyImmediate(walker);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// L'écran holographique de la salle de précision (2026-10-28, demandé par l'utilisateur : « un peu comme une télé sur la
    /// table ») se voit tout entier du comptoir, l'œil dans l'axe de la salle (le champ du jeu en 16:9 : 91° de large, 60° de
    /// haut), tourné vers le tireur, et ne cache rien de la cible, même à 5 m, où elle est la plus large ; rien de lui
    /// n'arrête les balles. Ce qui le casse ne lève aucune erreur : un écran poussé devant la cible, ou hors du champ.
    /// </summary>
    [Test]
    public void LEcranHolographique_SeVoitDuComptoir_SansCacherLaCible()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        AimRange aim = null;
        int before = 0;
        try
        {
            aim = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<AimRange>(true)).SingleOrDefault();
            Assert.IsNotNull(aim, "La salle de précision est dans le stand.");
            Renderer screen = aim.HologramScreen;
            Assert.IsNotNull(screen, "L'écran holographique (Duel Arena > Stand de tir > Construire le stand).");
            before = aim.TargetDistance;
            aim.SnapCarrier(5);

            Vector3 eye = ShootingRangeBuilder.AimShootingSpot + Vector3.up * 1.65f;
            float Yaw(Vector3 p) => Mathf.Atan2(p.x - eye.x, p.z - eye.z) * Mathf.Rad2Deg;
            float Pitch(Vector3 p) => Mathf.Atan2(p.y - eye.y, new Vector2(p.x - eye.x, p.z - eye.z).magnitude) * Mathf.Rad2Deg;
            Transform t = screen.transform;
            Assert.Greater(Vector3.Dot(-t.forward, (eye - t.position).normalized), 0.99f, "L'écran regarde l'œil du tireur.");
            float screenRight = float.MinValue;
            foreach (Vector2 c in new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) })
            {
                Vector3 corner = t.TransformPoint(c);
                Assert.Less(Mathf.Abs(Yaw(corner)), 45.5f, $"Le coin {c} de l'écran est dans le champ (cap {Yaw(corner):0.0}°).");
                Assert.Less(Mathf.Abs(Pitch(corner)), 30f, $"Le coin {c} de l'écran est dans le champ (hauteur {Pitch(corner):0.0}°).");
                screenRight = Mathf.Max(screenRight, Yaw(corner));
            }
            Assert.Greater(t.lossyScale.x, 0.4f, "Garde : l'écran a sa taille.");

            // La cible à 5 m : son bord le plus à gauche, vu de l'œil.
            Mesh disc = aim.Board.GetComponent<MeshCollider>().sharedMesh;
            float targetLeft = disc.vertices.Min(v => Yaw(aim.Board.TransformPoint(v)));
            Assert.Less(targetLeft, -15f, "Garde : la cible à 5 m est large.");
            Assert.Less(screenRight, targetLeft, $"L'écran (jusqu'à {screenRight:0.0}°) reste à gauche de la cible à 5 m (dès {targetLeft:0.0}°).");

            Assert.IsEmpty(t.parent.GetComponentsInChildren<Collider>(true), "L'hologramme est de la lumière : rien n'y arrête les balles.");
        }
        finally
        {
            if (aim != null) aim.SnapCarrier(before);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void SetHeight(CharacterController controller, float height)
    {
        controller.height = height;
        controller.center = new Vector3(0f, controller.skinWidth + height * 0.5f, 0f);
    }

    private static void Teleport(CharacterController controller, Vector3 feet)
    {
        controller.enabled = false;
        controller.transform.position = feet;
        controller.enabled = true;
        Physics.SyncTransforms();
    }

    /// <summary>Marche (5 m/s, la gravité comprise) jusqu'au point (x, z) ; échoue si le robot cesse d'avancer.</summary>
    private static void Walk(CharacterController controller, Vector3 target, int step, System.Action track)
    {
        const float stride = 0.05f, dt = 0.01f;
        float vy = 0f, best = float.MaxValue;
        int sinceProgress = 0;
        for (int n = 0; n < 20000; n++)
        {
            Vector3 p = controller.transform.position;
            Vector3 flat = new Vector3(target.x - p.x, 0f, target.z - p.z);
            float distance = flat.magnitude;
            if (distance < 0.12f) return;
            if (distance < best - 0.05f)
            {
                best = distance;
                sinceProgress = 0;
            }
            else if (++sinceProgress > 60)
            {
                Assert.Fail($"Le robot reste coincé en allant au pas {step} ({target}), à {p}.");
            }
            vy = controller.isGrounded ? -1f : vy - 9.81f * dt;
            controller.Move(flat / distance * Mathf.Min(stride, distance) + Vector3.up * (vy * dt));
            track();
        }
        Assert.Fail($"Le robot n'arrive pas au pas {step} ({target}).");
    }

    /// <summary>Franchit l'obstacle devant le robot vers `landing`, s'il suit les règles du vault du jeu
    /// (PlayerLocomotion : 0,3 à 1,3 m, 15 cm d'épaisseur au moins, réception à 1,5 m plus bas au plus).</summary>
    private static void Vault(CharacterController controller, Vector3 landing, int step)
    {
        int world = LayerMask.GetMask("Default");
        Vector3 p = controller.transform.position;
        Vector3 dir = new Vector3(landing.x - p.x, 0f, landing.z - p.z).normalized;
        Assert.IsTrue(Physics.Raycast(p + Vector3.up * 0.15f, dir, out RaycastHit front, 1.5f, world, QueryTriggerInteraction.Ignore),
                      $"Au pas {step}, un obstacle à franchir devant le robot.");
        Assert.IsTrue(Physics.Raycast(front.point + dir * 0.15f + Vector3.up * 1.6f, Vector3.down, out RaycastHit top, 1.8f, world, QueryTriggerInteraction.Ignore),
                      $"Au pas {step}, l'obstacle a un dessus.");
        float height = top.point.y - p.y;
        Assert.That(height, Is.InRange(0.3f, 1.3f), $"Au pas {step}, l'obstacle se franchit (0,3 à 1,3 m) : {height:0.00} m.");
        Assert.IsTrue(Physics.Raycast(front.point + dir * 3f + Vector3.up * 0.15f, -dir, out RaycastHit back, 3f, world, QueryTriggerInteraction.Ignore),
                      $"Au pas {step}, l'obstacle a un autre côté.");
        float thickness = Vector3.Distance(front.point, back.point);
        Assert.GreaterOrEqual(thickness, 0.15f, $"Au pas {step}, l'obstacle a au moins 15 cm d'épaisseur ({thickness:0.00} m).");
        Assert.IsTrue(Physics.Raycast(landing + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 2.1f, world, QueryTriggerInteraction.Ignore),
                      $"Au pas {step}, un sol où se recevoir.");
        Assert.LessOrEqual(p.y - ground.point.y, 1.5f, $"Au pas {step}, la réception n'est pas plus de 1,5 m plus bas.");
        Assert.IsFalse(Physics.CheckCapsule(ground.point + Vector3.up * 0.43f, ground.point + Vector3.up * 1.53f, 0.35f, world, QueryTriggerInteraction.Ignore),
                       $"Au pas {step}, le robot tient à la réception.");
        Teleport(controller, ground.point);
    }

    private static Bounds Shrink(Bounds b)
    {
        b.Expand(-0.2f);
        return b;
    }

    private static Bounds Grow(Bounds b)
    {
        b.Expand(0.2f);
        return b;
    }
}
