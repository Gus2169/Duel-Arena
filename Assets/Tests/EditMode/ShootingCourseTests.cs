using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// La Fosse (2026-10-16), le parcours chronométré du stand de tir, sur le modèle de « The Pit » de
/// Modern Warfare 2 : quand la course part, finit ou s'abandonne ; le temps final et ses pénalités ;
/// la pose d'une cible (lever, chute, rail) ; les records ; le bilan ; et la scène construite, où chaque
/// cible doit tenir debout sur un sol, hors des murs.
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
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
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
