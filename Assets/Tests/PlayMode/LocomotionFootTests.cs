#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Les pieds ne glissent pas : l'animation fait avancer le corps à la vitesse ET dans la direction
/// où il avance vraiment (2026-10-09).
///
/// Le défaut, rapporté par l'utilisateur : en visée, et pire en sneak + visée, debout comme
/// accroupi, « on dirait que je glisse sur le sol ». Mesuré : les pieds avançaient à 3 à 10 % de la
/// vitesse du corps (l'attente de 8,6 s synchronisée avec la course étirait tout le cycle), et la
/// course latérale partait 58° de travers (orientation de racine mal importée).
///
/// La mesure est la vitesse de racine que l'Animator calcule pour le mélange joué — ce que voit
/// l'œil, mélange, cadence et synchronisation compris — lue par la même sonde qu'en jeu, et
/// conduite par la même fonction (PlayerAnimator.DriveGait), sur le vrai contrôleur et avec les
/// vitesses DU PREFAB. Chaque allure du jeu est jouée six secondes : la cadence et la direction se
/// mesurent sur un cycle de pas, qui dure jusqu'à trois secondes aux allures les plus lentes.
///
/// Depuis le 2026-10-10, aucune glissade n'est tolérée : le plafond de cadence couvre toutes les
/// allures du jeu, et toutes sont couvertes, sprint, courses en diagonale, marche accroupie à
/// pleine allure et reptation comprises. Restent hors du test les diagonales arrière en sneak ou en
/// visée debout, en attendant les marches lentes du pack (dette n° 37) : le pas chassé et la course
/// arrière de l'anneau lent n'ont pas la même foulée.
///
/// Ce test juge la vitesse que l'Animator CALCULE pour le mélange. Ce que l'œil voit vraiment, les
/// pieds dans le monde, est jugé par FootSlideTests.
/// </summary>
public class LocomotionFootTests
{
    private GameObject instance;

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
    }

    private const float Dt = 1f / 60f;

    private struct Tuning
    {
        public float strafe, backward; // la direction retire à la vitesse (2026-10-10)
        public float walk, run, sneak, ads, idleScale, minPlayback, minLeg, maxLeg;
        public float[] stanceMultiplier;
        public float[] playbackScale;
    }

    /// <summary>Les vitesses et la cadence telles que réglées dans le PREFAB : ce sont elles qui
    /// jouent en jeu, pas les valeurs par défaut du code.</summary>
    private static Tuning ReadPrefab()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TestCharacter.PlayerPrefabPath);
        var loco = new UnityEditor.SerializedObject(prefab.GetComponent<PlayerLocomotion>());
        var anim = new UnityEditor.SerializedObject(prefab.GetComponent<PlayerAnimator>());
        return new Tuning
        {
            walk = loco.FindProperty("walkSpeed").floatValue,
            run = loco.FindProperty("runSpeed").floatValue,
            sneak = loco.FindProperty("sneakSpeed").floatValue,
            ads = loco.FindProperty("adsSpeedMultiplier").floatValue,
            strafe = loco.FindProperty("strafeSpeedMultiplier").floatValue,
            backward = loco.FindProperty("backwardSpeedMultiplier").floatValue,
            stanceMultiplier = new[]
            {
                loco.FindProperty("standingProfile.moveSpeedMultiplier").floatValue,
                loco.FindProperty("crouchingProfile.moveSpeedMultiplier").floatValue,
                loco.FindProperty("proneProfile.moveSpeedMultiplier").floatValue,
            },
            playbackScale = new[]
            {
                anim.FindProperty("standingPlaybackScale").floatValue,
                anim.FindProperty("crouchingPlaybackScale").floatValue,
                anim.FindProperty("pronePlaybackScale").floatValue,
            },
            minPlayback = anim.FindProperty("minPlaybackSpeed").floatValue,
            minLeg = anim.FindProperty("minLegCadence").floatValue,
            maxLeg = anim.FindProperty("maxLegCadence").floatValue,
        };
    }

    /// <summary>Joue une allure et renvoie la vitesse de racine moyenne, dans le repère du joueur,
    /// sur des cycles de pas ENTIERS : un clip qui zigzague (la reptation se tortille d'un côté à
    /// l'autre) fausserait sinon la direction mesurée, jusqu'à 30° sur un demi-cycle. Trois
    /// secondes de mise en route, puis au moins deux cycles complets.</summary>
    private Vector2 Play(Animator animator, AnimatorRootMotionProbe probe, Tuning t, int stance, float speed, Vector2 direction,
                         bool mainsNues = false)
    {
        animator.Rebind();
        animator.Update(0f);
        var state = new PlayerAnimator.GaitState();
        // L'allure de référence suit la direction, comme en jeu (PlayerAnimator) ; `speed` est
        // déjà la vitesse réelle dans cette direction.
        float reference = t.walk * t.stanceMultiplier[stance] * PlayerLocomotion.DirectionSpeedMultiplier(direction, t.strafe, t.backward);
        Vector2 move = direction.normalized * (speed / reference);
        float idle = t.minPlayback * t.playbackScale[stance];
        float upper = Mathf.Clamp(move.magnitude, t.minPlayback, 1f) * t.playbackScale[stance];

        Vector2 sum = Vector2.zero, cycleSum = Vector2.zero;
        int n = 0, cycleN = 0, cycles = 0, lastCycle = -1;
        bool started = false;
        for (int i = 0; i < 3000; i++)
        {
            animator.SetInteger("Stance", stance);
            animator.SetFloat("StanceF", stance);
            animator.SetFloat("MainsNues", mainsNues ? 1f : 0f);
            Vector3 v = instance.transform.InverseTransformDirection(probe.Velocity);
            PlayerAnimator.DriveGait(animator, ref state, move, reference, (PlayerLocomotion.Stance)stance,
                                     new Vector2(v.x, v.z), idle, upper, t.minLeg, t.maxLeg, Dt);
            animator.Update(Dt);
            if (i < 180) continue;

            int cycle = Mathf.FloorToInt(animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
            if (lastCycle >= 0 && cycle != lastCycle)
            {
                // Un cycle vient de se terminer : il compte s'il a été suivi en entier.
                if (started) { sum += cycleSum; n += cycleN; cycles++; }
                started = true;
                cycleSum = Vector2.zero;
                cycleN = 0;
                if (cycles >= 2 && i >= 360) break;
            }
            lastCycle = cycle;
            Vector3 w = instance.transform.InverseTransformDirection(probe.Velocity);
            cycleSum += new Vector2(w.x, w.z);
            cycleN++;
        }
        Assert.Greater(n, 0, "Aucun cycle de pas complet : l'allure ne joue pas.");
        return sum / n;
    }

    [Test]
    public void AChaqueAllure_LesPiedsAvancentALaVitesseEtDansLaDirectionDuCorps()
    {
        Tuning t = ReadPrefab();
        instance = TestCharacter.InstantiateModel(out Animator animator);
        // Tourné, pour vérifier que tout se mesure bien dans le repère du joueur.
        instance.transform.rotation = Quaternion.Euler(0f, 130f, 0f);
        var probe = instance.AddComponent<AnimatorRootMotionProbe>();

        var cardinals = new[] { Vector2.up, Vector2.down, Vector2.right, Vector2.left };
        var all = new[]
        {
            Vector2.up, Vector2.down, Vector2.right, Vector2.left,
            new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f),
        };
        var forwardAndSides = new[]
        {
            Vector2.up, Vector2.down, Vector2.right, Vector2.left, new Vector2(1f, 1f), new Vector2(-1f, 1f),
        };

        var sprintable = new[] { Vector2.up, new Vector2(1f, 1f), new Vector2(-1f, 1f) };
        var forwardAndBack = new[] { Vector2.up, Vector2.down };
        float prone = t.stanceMultiplier[2];

        var cases = new List<(string name, int stance, float speed, Vector2[] dirs)>
        {
            ("debout sneak+visée", 0, t.sneak * t.ads, all),
            ("debout sneak", 0, t.sneak, all),
            ("debout visée", 0, t.walk * t.ads, forwardAndSides),
            ("debout marche", 0, t.walk, all),
            ("debout sprint", 0, t.run, sprintable),
            ("accroupi sneak+visée", 1, t.sneak * t.stanceMultiplier[1] * t.ads, all),
            ("accroupi sneak", 1, t.sneak * t.stanceMultiplier[1], all),
            ("accroupi visée", 1, t.walk * t.stanceMultiplier[1] * t.ads, all),
            ("accroupi marche", 1, t.walk * t.stanceMultiplier[1], all),
            ("allongé sneak+visée", 2, t.sneak * prone * t.ads, forwardAndBack),
            ("allongé sneak", 2, t.sneak * prone, forwardAndBack),
            ("allongé visée", 2, t.walk * prone * t.ads, forwardAndBack),
            ("allongé ramper", 2, t.walk * prone, forwardAndBack),
        };

        var failures = new List<string>();
        float maxMeasured = 0f;
        foreach (var c in cases)
        {
            foreach (Vector2 dir in c.dirs)
            {
                // Comme dans Move() : on recule et on va de côté moins vite (2026-10-10).
                float speed = c.speed * PlayerLocomotion.DirectionSpeedMultiplier(dir, t.strafe, t.backward);
                Vector2 v = Play(animator, probe, t, c.stance, speed, dir);
                float ratio = v.magnitude / speed;
                float heading = Vector2.Angle(v, dir);
                maxMeasured = Mathf.Max(maxMeasured, v.magnitude);
                string line = $"{c.name} {dir}: pieds {v.magnitude:F2} m/s pour {speed:F2} ({ratio * 100f:F0} %), écart de cap {heading:F0}°";
                Debug.Log("[PIEDS] " + line);
                if (ratio < 0.9f || ratio > 1.1f || heading > 8f) failures.Add(line);
            }
        }

        // Garde anti-vacuité : la sonde rapporte bien quelque chose. Sans OnAnimatorMove, la vitesse
        // vaudrait zéro partout et l'échec dirait « les pieds sont immobiles », pas « la sonde est muette ».
        Assert.Greater(maxMeasured, 1f, "La sonde de racine ne rapporte rien : OnAnimatorMove n'est pas appelé.");
        Assert.IsEmpty(failures, "Les pieds glissent :\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// Un spectateur, à mains nues (clips Mixamo choisis le 2026-10-13) : debout, il joue son propre arbre
    /// (paramètre MainsNues), aux vitesses d'un duelliste. Accroupi, ses jambes sont celles du duelliste,
    /// déjà jugées plus haut.
    /// </summary>
    [Test]
    public void UnSpectateur_AChaqueAllure_LesPiedsAvancentALaVitesseEtDansLaDirectionDuCorps()
    {
        Tuning t = ReadPrefab();
        instance = TestCharacter.InstantiateModel(out Animator animator);
        instance.transform.rotation = Quaternion.Euler(0f, 130f, 0f);
        var probe = instance.AddComponent<AnimatorRootMotionProbe>();
        var infos = new List<AnimatorClipInfo>();

        var all = new[]
        {
            Vector2.up, Vector2.down, Vector2.right, Vector2.left,
            new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f),
        };
        var sprintable = new[] { Vector2.up, new Vector2(1f, 1f), new Vector2(-1f, 1f) };
        var cases = new List<(string name, float speed, Vector2[] dirs)>
        {
            ("spectateur sneak", t.sneak, all),
            ("spectateur marche", t.walk, all),
            ("spectateur sprint", t.run, sprintable),
        };

        var failures = new List<string>();
        bool unarmedSeen = false;
        foreach (var c in cases)
        {
            foreach (Vector2 dir in c.dirs)
            {
                float speed = c.speed * PlayerLocomotion.DirectionSpeedMultiplier(dir, t.strafe, t.backward);
                Vector2 v = Play(animator, probe, t, 0, speed, dir, mainsNues: true);
                // Garde : c'est bien l'arbre à mains nues qui joue (ses clips sont dans MainsNues/).
                animator.GetCurrentAnimatorClipInfo(0, infos);
                foreach (AnimatorClipInfo info in infos)
                    if (info.weight > 0.5f && UnityEditor.AssetDatabase.GetAssetPath(info.clip).Contains("/MainsNues/")) unarmedSeen = true;
                float ratio = v.magnitude / speed;
                float heading = Vector2.Angle(v, dir);
                string line = $"{c.name} {dir}: pieds {v.magnitude:F2} m/s pour {speed:F2} ({ratio * 100f:F0} %), écart de cap {heading:F0}°";
                Debug.Log("[PIEDS] " + line);
                if (ratio < 0.9f || ratio > 1.1f || heading > 8f) failures.Add(line);
            }
        }
        Assert.IsTrue(unarmedSeen, "Garde : l'arbre à mains nues n'a jamais joué (paramètre MainsNues ?).");
        Assert.IsEmpty(failures, "Les pieds du spectateur glissent :\n" + string.Join("\n", failures));
    }
}
#endif
