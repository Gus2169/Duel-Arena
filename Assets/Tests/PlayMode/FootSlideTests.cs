#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Aucune glissade : pendant qu'un talon ou une pointe de pied est posé, il ne bouge pas dans le
/// MONDE (2026-10-10, « on n'accepte pas les glissades »).
///
/// C'est la mesure de ce que l'œil voit. LocomotionFootTests juge la vitesse que l'Animator
/// calcule pour le mélange joué ; elle peut être juste pendant que les pieds glissent quand même,
/// parce qu'un clip Mixamo glisse de lui-même pendant ses appuis (jusqu'à 25 % en course, mesuré),
/// et qu'un mélange pose ses pieds entre ceux de ses clips. Ici, le modèle avance réellement à la
/// vitesse du jeu, conduit comme en jeu (DriveGait, FootPlanter), et on suit ses os de pied.
///
/// Un os est posé quand il est à moins de 5 mm de son point le plus bas sur la mesure : on juge
/// le cœur de l'appui, pas l'instant où il touche ou quitte le sol. La glissade est le chemin qu'il
/// parcourt pendant ses appuis, rapporté à celui du corps pendant ce temps (médiane image par
/// image, et total). Mesurée sur des cycles de pas entiers, après trois secondes de mise en route.
/// </summary>
public class FootSlideTests
{
    private GameObject instance;

    /// <summary>Glissade tolérée pendant l'appui, en part du chemin du corps : médiane image par
    /// image, et sur tout l'appui (pose et décollage compris).</summary>
    private const float MaxMedian = 0.05f;
    private const float SideRunMaxMedian = 0.06f; // voir le cas « debout marche de côté »
    private const float MaxTotal = 0.15f; // la course arrière gauche fait 12 à 14 % (jambe tendue à la poussée)

    private const float Dt = 1f / 120f; // les appuis du sprint ne durent que quelques centièmes

    /// <summary>Écart toléré, en allers-retours, entre la vitesse des pieds et celle du corps : en
    /// moyenne, et pour 95 % des images. L'ancienne mesure se trompait d'un facteur 2 à 3.</summary>
    private const float MaxAlternatingMeanError = 0.08f;
    private const float MaxAlternatingP95Error = 0.15f;

    /// <summary>Écart de mélange (somme des écarts de poids des clips) sous lequel l'Animator joue
    /// « le mélange de la ligne droite ».</summary>
    private const float SameMix = 0.1f;

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
    }

    private struct Tuning
    {
        public float strafe, backward; // la direction retire à la vitesse (2026-10-10)
        public float walk, run, sneak, ads, minPlayback, minLeg, maxLeg;
        public float[] stanceMultiplier, playbackScale;
    }

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
            },
            playbackScale = new[]
            {
                anim.FindProperty("standingPlaybackScale").floatValue,
                anim.FindProperty("crouchingPlaybackScale").floatValue,
            },
            minPlayback = anim.FindProperty("minPlaybackSpeed").floatValue,
            minLeg = anim.FindProperty("minLegCadence").floatValue,
            maxLeg = anim.FindProperty("maxLegCadence").floatValue,
        };
    }

    private struct Slide { public float median, total, unplanted; public int outOfReach; }

    private Slide Play(Animator animator, AnimatorRootMotionProbe probe, FootPlanter planter, Tuning t, int stance, float speed, Vector2 direction)
    {
        animator.Rebind();
        animator.Update(0f);
        planter?.Reset();
        instance.transform.position = Vector3.zero;
        var state = new PlayerAnimator.GaitState();
        // Comme en jeu : on recule et on va de côté moins vite (Move), et l'allure de référence suit
        // la direction (PlayerAnimator) : reculer en marchant reste « la marche » pour l'arbre.
        speed *= PlayerLocomotion.DirectionSpeedMultiplier(direction, t.strafe, t.backward);
        float reference = t.walk * t.stanceMultiplier[stance] * PlayerLocomotion.DirectionSpeedMultiplier(direction, t.strafe, t.backward);
        Vector2 move = direction.normalized * (speed / reference);
        Vector3 velocity = instance.transform.rotation * new Vector3(direction.normalized.x, 0f, direction.normalized.y) * speed;
        float idle = t.minPlayback * t.playbackScale[stance];
        float upper = Mathf.Clamp(move.magnitude, t.minPlayback, 1f) * t.playbackScale[stance];

        var bones = new[]
        {
            animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.LeftToes),
            animator.GetBoneTransform(HumanBodyBones.RightFoot), animator.GetBoneTransform(HumanBodyBones.RightToes),
        };
        var frames = new List<Vector3[]>();
        var roots = new List<Vector3>();
        var planted = new List<bool[]>();
        int reachBefore = FootPlanter.OutOfReach;
        int lastCycle = -1, cycles = 0;
        bool recording = false;
        for (int i = 0; i < 6000; i++)
        {
            instance.transform.position += velocity * Dt;
            animator.SetInteger("Stance", stance);
            animator.SetFloat("StanceF", stance);
            Vector3 v = instance.transform.InverseTransformDirection(probe.Velocity);
            PlayerAnimator.DriveGait(animator, ref state, move, reference, (PlayerLocomotion.Stance)stance,
                                     new Vector2(v.x, v.z), idle, upper, t.minLeg, t.maxLeg, Dt);
            animator.Update(Dt);
            planter?.Apply(true, Dt);
            if (i < 360) continue;

            int cycle = Mathf.FloorToInt(animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
            if (lastCycle >= 0 && cycle != lastCycle)
            {
                if (recording) cycles++;
                recording = true;
                if (cycles >= 2 && i >= 720) break;
            }
            lastCycle = cycle;
            if (!recording) continue;
            var p = new Vector3[bones.Length];
            for (int k = 0; k < bones.Length; k++) p[k] = bones[k].position;
            frames.Add(p);
            roots.Add(instance.transform.position);
            planted.Add(planter == null ? new bool[2] : new[] { planter.IsPlanted(true), planter.IsPlanted(false) });
        }
        Assert.Greater(frames.Count, 10, "Aucun cycle de pas complet : l'allure ne joue pas.");

        var mins = new float[bones.Length];
        for (int k = 0; k < bones.Length; k++)
        {
            mins[k] = float.MaxValue;
            for (int i = 0; i < frames.Count; i++) mins[k] = Mathf.Min(mins[k], frames[i][k].y - roots[i].y);
        }
        var ratios = new List<float>();
        float footPath = 0f, bodyPath = 0f;
        int contacts = 0, unplanted = 0;
        for (int i = 1; i < frames.Count; i++)
        {
            for (int k = 0; k < bones.Length; k++)
            {
                if (frames[i][k].y - roots[i].y > mins[k] + 0.005f) continue;
                if (frames[i - 1][k].y - roots[i - 1].y > mins[k] + 0.005f) continue;
                Vector3 df = frames[i][k] - frames[i - 1][k]; df.y = 0f;
                Vector3 db = roots[i] - roots[i - 1]; db.y = 0f;
                // Un pied qui avance plus vite que le corps est en balancier, même s'il frôle le sol
                // (la pointe de la marche accroupie avant droite le fait) : ce n'est pas un appui.
                if (Vector3.Dot(df - db, db) > 0f) continue;
                footPath += df.magnitude;
                bodyPath += db.magnitude;
                ratios.Add(df.magnitude / Mathf.Max(1e-6f, db.magnitude));
                contacts++;
                if (!planted[i][k / 2]) unplanted++;
            }
        }
        ratios.Sort();
        return new Slide
        {
            median = ratios.Count > 0 ? ratios[ratios.Count / 2] : 1f,
            total = bodyPath > 0f ? footPath / bodyPath : 1f,
            unplanted = contacts > 0 ? (float)unplanted / contacts : 0f,
            outOfReach = FootPlanter.OutOfReach - reachBefore,
        };
    }

    [Test]
    public void PendantLAppui_LesPiedsNeGlissentPas()
    {
        Tuning t = ReadPrefab();
        instance = TestCharacter.InstantiateModel(out Animator animator);
        instance.transform.rotation = Quaternion.Euler(0f, 130f, 0f);
        var probe = instance.AddComponent<AnimatorRootMotionProbe>();
        var planter = new FootPlanter(animator);

        Vector2 F = Vector2.up, B = Vector2.down, R = Vector2.right, L = Vector2.left;
        Vector2 FR = new Vector2(1f, 1f), FL = new Vector2(-1f, 1f), BR = new Vector2(1f, -1f), BL = new Vector2(-1f, -1f);
        var cases = new List<(string name, int stance, float speed, Vector2[] dirs, bool required)>
        {
            // Le sneak debout et le sneak + visée jouent l'anneau lent : les huit marches du Y Bot.
            ("debout sneak", 0, t.sneak, new[] { F, B, R, L, FR, FL, BR, BL }, true),
            ("debout sneak+visée", 0, t.sneak * t.ads, new[] { F, B, R, L, FR, BL }, true),
            ("debout visée", 0, t.walk * t.ads, new[] { F, B, R, BL }, true),
            ("debout marche", 0, t.walk, new[] { F, B, FR, BL }, true),
            // La course de côté (dette n° 42) : le pied roule sur sa tranche pendant l'appui. Mesuré
            // le 2026-10-10 : 4,2 % à 5 m/s, 5,5 % depuis qu'on va de côté à ×0,85 (une glissade
            // par pas à peine changée, sur un chemin plus court). Exigée à 6 %, en attendant mieux.
            ("debout marche de côté", 0, t.walk, new[] { R, L }, true),
            ("debout sprint", 0, t.run, new[] { F, FR, FL }, true),
            ("accroupi sneak", 1, t.sneak * t.stanceMultiplier[1], new[] { F, B, R, L, FL, BR }, true),
            ("accroupi marche", 1, t.walk * t.stanceMultiplier[1], new[] { F, B, R, FR, BL }, true),
        };

        var failures = new List<string>();
        foreach (var c in cases)
        {
            foreach (Vector2 dir in c.dirs)
            {
                Slide sans = Play(animator, probe, null, t, c.stance, c.speed, dir);
                Slide avec = Play(animator, probe, planter, t, c.stance, c.speed, dir);
                string line = $"{c.name} {dir}: glissade médiane {sans.median * 100f:F1} % → {avec.median * 100f:F1} %, totale {sans.total * 100f:F1} % → {avec.total * 100f:F1} % (appuis non retenus {avec.unplanted * 100f:F0} %, hors de portée {avec.outOfReach})";
                Debug.Log("[GLISSADE] " + (c.required ? "" : "(en attente) ") + line);
                float maxMedian = c.name == "debout marche de côté" ? SideRunMaxMedian : MaxMedian;
                if (c.required && (avec.median > maxMedian || avec.total > MaxTotal)) failures.Add(line);
            }
        }
        Assert.IsEmpty(failures, "Les pieds glissent pendant l'appui :\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// Des allers-retours de côté, comme pour peeker (2026-10-11). Mesuré à deux instances : un
    /// joueur en sneak qui changeait de côté toutes les secondes avait, vu des autres, les pieds à
    /// 44 à 72 % de sa vitesse (il jouait alors les Rifle Sneak, essayés puis écartés le jour même ;
    /// le sneak accroupi avait le même défaut). Au demi-tour, l'évaluation de l'Animator renvoyait un
    /// déplacement aberrant, et la mesure de la cadence, qui repartait presque de zéro, ne voyait
    /// qu'un bout de cycle. En ligne droite, rien ne le montrait.
    ///
    /// Conduit comme en jeu : le corps change de côté d'un coup (accélération de 60 m/s²), les jambes
    /// suivent la vitesse lissée de PlayerAnimator. On juge la vitesse naturelle que la cadence
    /// retient, rapportée à celle qu'elle trouve en allant longtemps du même côté, quand l'Animator
    /// joue le même mélange de clips qu'en ligne droite (au demi-tour, l'allure traverse les
    /// anneaux : c'est un autre mélange) : les pieds avancent à ce rapport près de la vitesse du
    /// corps. Une moyenne des vitesses de pied n'aurait pas de sens ici : un aller de 0,4 s ne couvre
    /// qu'une partie d'un pas, et l'Animator renvoie des déplacements faux pendant que son mélange
    /// change.
    /// </summary>
    [Test]
    public void EnAllersRetoursDeCote_LaCadenceResteCelleDuPas()
    {
        Tuning t = ReadPrefab();
        float smoothing = new UnityEditor.SerializedObject(UnityEditor.AssetDatabase
            .LoadAssetAtPath<GameObject>(TestCharacter.PlayerPrefabPath).GetComponent<PlayerAnimator>())
            .FindProperty("blendSmoothing").floatValue;
        instance = TestCharacter.InstantiateModel(out Animator animator);
        var probe = instance.AddComponent<AnimatorRootMotionProbe>();
        const float dt = 1f / 100f;

        // Un côté pendant 1 s (le banc de test à deux instances) et 0,4 s. Pas 0,4 s en visée : l'allure
        // n'y a pas le temps de revenir au mélange de la ligne droite.
        var cases = new (string name, int stance, float speed, float[] halves)[]
        {
            ("debout sneak", 0, t.sneak, new[] { 1f, 0.4f }),
            ("debout visée", 0, t.walk * t.ads, new[] { 1f }),
            ("accroupi sneak", 1, t.sneak * t.stanceMultiplier[1], new[] { 1f, 0.4f }),
        };
        var infos = new List<AnimatorClipInfo>();
        Dictionary<AnimationClip, float> Mix()
        {
            animator.GetCurrentAnimatorClipInfo(0, infos);
            var mix = new Dictionary<AnimationClip, float>();
            foreach (AnimatorClipInfo info in infos)
                if (info.weight > 0f) mix[info.clip] = (mix.TryGetValue(info.clip, out float w) ? w : 0f) + info.weight;
            return mix;
        }
        float Distance(Dictionary<AnimationClip, float> a, Dictionary<AnimationClip, float> b)
        {
            float d = 0f;
            foreach (var kv in a) d += Mathf.Abs(kv.Value - (b.TryGetValue(kv.Key, out float w) ? w : 0f));
            foreach (var kv in b) if (!a.ContainsKey(kv.Key)) d += kv.Value;
            return d;
        }
        string Describe(Dictionary<AnimationClip, float> mix)
        {
            var parts = new List<string>();
            foreach (var kv in mix) if (kv.Value >= 0.05f) parts.Add($"{kv.Key.name} {kv.Value * 100f:F0} %");
            return string.Join(", ", parts);
        }

        var failures = new List<string>();
        foreach (var c in cases)
        {
            float speed = c.speed * PlayerLocomotion.DirectionSpeedMultiplier(Vector2.right, t.strafe, t.backward);
            float reference = t.walk * t.stanceMultiplier[c.stance] * PlayerLocomotion.DirectionSpeedMultiplier(Vector2.right, t.strafe, t.backward);
            float idle = t.minPlayback * t.playbackScale[c.stance];

            // half = 0 : toujours du même côté, la référence ; sinon, un côté pendant `half` secondes.
            Dictionary<AnimationClip, float> rightMix = null, leftMix = null;
            float right = 0f, left = 0f;
            float Run(float half, int sideOfSteady, List<float> errors)
            {
                animator.Rebind();
                animator.Update(0f);
                var state = new PlayerAnimator.GaitState();
                Vector2 smoothed = Vector2.zero;
                for (int i = 0; i < 1200; i++)
                {
                    float time = i * dt;
                    float side = half <= 0f ? sideOfSteady : Mathf.Repeat(time / (2f * half), 1f) < 0.5f ? 1f : -1f;
                    smoothed = Vector2.Lerp(smoothed, new Vector2(side * speed / reference, 0f), 1f - Mathf.Exp(-smoothing * dt));
                    animator.SetInteger("Stance", c.stance);
                    animator.SetFloat("StanceF", c.stance);
                    Vector3 v = instance.transform.InverseTransformDirection(probe.Velocity);
                    float upper = Mathf.Clamp(smoothed.magnitude, t.minPlayback, 1f) * t.playbackScale[c.stance];
                    PlayerAnimator.DriveGait(animator, ref state, smoothed, reference, (PlayerLocomotion.Stance)c.stance,
                                             new Vector2(v.x, v.z), idle, upper, t.minLeg, t.maxLeg, dt);
                    animator.Update(dt);
                    if (half <= 0f || time < 4f || state.marche < 0.99f) continue;

                    // Après la mise en route, quand l'Animator joue le mélange de la ligne droite : les
                    // pieds y avancent à ligne droite / estimation de la vitesse du corps.
                    var mix = Mix();
                    if (Distance(mix, rightMix) <= SameMix) errors.Add(Mathf.Abs(right / state.naturalSpeed - 1f));
                    else if (Distance(mix, leftMix) <= SameMix) errors.Add(Mathf.Abs(left / state.naturalSpeed - 1f));
                }
                return state.naturalSpeed;
            }

            right = Run(0f, 1, null);
            rightMix = Mix();
            left = Run(0f, -1, null);
            leftMix = Mix();
            Assert.Greater(Mathf.Min(right, left), 0.15f, $"{c.name} : rien de mesuré en ligne droite.");
            foreach (float half in c.halves)
            {
                var errors = new List<float>();
                Run(half, 0, errors);
                Assert.Greater(errors.Count, 20, $"{c.name} : les allers-retours ne jouent jamais le mélange de la ligne droite.");
                float sum = 0f;
                foreach (float e in errors) sum += e;
                errors.Sort();
                float mean = sum / errors.Count, p95 = errors[(int)(errors.Count * 0.95f)], worst = errors[errors.Count - 1];
                string line = $"{c.name}, un côté pendant {half:F1} s : vitesse naturelle en ligne droite {right:F3} m/s à droite "
                              + $"({Describe(rightMix)}), {left:F3} à gauche ({Describe(leftMix)}) ; en allers-retours, pieds à "
                              + $"{mean * 100f:F1} % près en moyenne, {p95 * 100f:F1} % pour 95 % des images, {worst * 100f:F0} % au pire "
                              + $"(sur {errors.Count} images)";
                Debug.Log("[ALLERS-RETOURS] " + line);
                if (mean > MaxAlternatingMeanError || p95 > MaxAlternatingP95Error) failures.Add(line);
            }
        }
        Assert.IsEmpty(failures, "En allers-retours, la cadence ne suit plus le pas :\n" + string.Join("\n", failures));
    }
}
#endif
