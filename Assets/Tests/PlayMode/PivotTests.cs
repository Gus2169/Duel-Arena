#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Le pivot sur place, sur le vrai contrôleur (2026-10-10). Le joueur tourne de 100° à l'arrêt, d'un
/// mouvement de souris : les jambes gardent leur cap, puis pivotent vers le regard pendant que
/// chaque pied reste posé et fait de petits pas levés (FootPlanter). Les pointes de pied posées ne
/// doivent pas bouger au sol.
///
/// Avant, le robot pivotait d'un bloc avec le regard : chaque pied décrivait un arc de cercle au
/// sol, plus d'un mètre de chemin en tout pour 100°, debout comme accroupi.
/// </summary>
public class PivotTests
{
    private GameObject instance;
    private const float Dt = 1f / 120f;

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
    }

    /// <summary>Tourne de `angle` à l'arrêt et renvoie le chemin (m) des pointes posées au sol.</summary>
    private float Turn(int stance, float angle, bool procedural, out float finalLag)
    {
        instance = TestCharacter.InstantiateModel(out Animator animator);
        var planter = new FootPlanter(animator);
        // Les pointes : la pointe posée ne doit jamais bouger au sol (le talon, lui, peut tourner
        // autour d'elle).
        var bones = new[] { animator.GetBoneTransform(HumanBodyBones.LeftToes), animator.GetBoneTransform(HumanBodyBones.RightToes) };

        var turn = new PlayerAnimator.LegsTurnState();
        float playerYaw = 0f;
        var previous = new Vector3[2];
        var mins = new[] { float.MaxValue, float.MaxValue };
        float path = 0f;
        for (int i = 0; i < 720; i++)
        {
            // Une seconde au repos, puis le regard tourne de `angle` en 0,4 s.
            if (i >= 120 && i < 168) playerYaw += angle / 48f;

            PlayerAnimator.StepLegsTurn(ref turn, playerYaw, true, true, Dt);
            if (!procedural) turn.legsYaw = playerYaw; // l'ancien comportement : d'un bloc
            instance.transform.rotation = Quaternion.Euler(0f, turn.legsYaw, 0f);

            animator.SetInteger("Stance", stance);
            animator.SetFloat("StanceF", stance);
            animator.SetFloat("Marche", 0f);
            animator.Update(Dt);
            // Comme en jeu : le calage est coupé le temps de prendre la posture.
            if (procedural) planter.Apply(i >= 60, Dt);

            for (int k = 0; k < 2; k++)
            {
                Vector3 p = bones[k].position;
                // Une demi-seconde pour s'installer dans la posture, une demi-seconde pour relever
                // le plus bas de chaque pointe, puis la mesure (le regard tourne à partir de 1 s).
                if (i >= 120 && p.y < mins[k] + 0.005f && previous[k].y < mins[k] + 0.005f)
                {
                    Vector3 d = p - previous[k]; d.y = 0f;
                    path += d.magnitude;
                }
                else if (i >= 60 && i < 120) mins[k] = Mathf.Min(mins[k], p.y);
                previous[k] = p;
            }
        }
        finalLag = Mathf.Abs(Mathf.DeltaAngle(turn.legsYaw, playerYaw));
        Object.DestroyImmediate(instance);
        return path;
    }

    [Test]
    public void ALArret_TournerDe100Degres_LesJambesPivotentSansGlisser()
    {
        foreach (int stance in new[] { 0, 1 })
        foreach (float angle in new[] { 100f, -100f })
        {
            float rigid = Turn(stance, angle, false, out _);
            float path = Turn(stance, angle, true, out float lag);
            string what = $"{(stance == 0 ? "debout" : "accroupi")}, {angle:+0;-0}°";
            Debug.Log($"[PIVOT] {what} : écart final {lag:F1}°, pointes posées {path * 100f:F1} cm au sol ; d'un bloc {rigid * 100f:F1} cm");
            Assert.Less(lag, 1f, what + $" : les jambes restent {lag:F0}° derrière le regard.");
            Assert.Greater(rigid, 0.3f, what + " : garde anti-vacuité, le pivot d'un bloc devrait faire glisser les pieds.");
            // Mesuré le 2026-10-10 : 1,4 cm debout et 3 cm accroupi, sur les deux pieds et tout le
            // pivot, contre 108 à 170 cm d'un bloc.
            Assert.Less(path, 0.04f, what + $" : les pointes posées ont glissé de {path * 100f:F1} cm (d'un bloc : {rigid * 100f:F1} cm).");
        }
    }
}
#endif
