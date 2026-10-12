#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Se baisser puis se relever à l'arrêt, sur le vrai contrôleur (2026-10-10). Accroupi, le robot a
/// le pied droit loin en arrière, talon levé : le fondu entre l'attente debout et l'attente
/// accroupie le faisait glisser de 35 cm au sol, aller comme retour. Le calage des pieds reste
/// désormais actif pendant ce fondu (PlayerAnimator.LegsOnFeet) : le pied tient, puis fait un petit
/// pas levé, comme pendant le pivot sur place.
/// </summary>
public class CrouchFeetTests
{
    private const float Dt = 1f / 120f;
    private const int Frames = 600;

    /// <summary>Debout jusqu'à 1 s, accroupi jusqu'à 3 s, debout jusqu'à 5 s.</summary>
    private static int StanceAt(int frame) => frame >= 120 && frame < 360 ? 1 : 0;

    /// <summary>Joue le scénario et renvoie le chemin (m) des pointes posées au sol, c'est-à-dire
    /// sous leur seuil de contact, et la hauteur la plus basse d'une pointe.</summary>
    private static float Run(bool plantedThroughTransitions, float[] contact, out float lowest)
    {
        lowest = float.MaxValue;
        GameObject instance = TestCharacter.InstantiateModel(out Animator animator);
        try
        {
            var planter = new FootPlanter(animator);
            var toes = new[] { animator.GetBoneTransform(HumanBodyBones.LeftToes), animator.GetBoneTransform(HumanBodyBones.RightToes) };
            var previous = new Vector3[2];
            float path = 0f;
            for (int i = 0; i < Frames; i++)
            {
                int stance = StanceAt(i);
                animator.SetInteger("Stance", stance);
                animator.SetFloat("StanceF", stance);
                animator.SetFloat("Marche", 0f);
                animator.Update(Dt);
                // Une demi-seconde pour prendre la pose de départ, comme en jeu au lancement.
                if (plantedThroughTransitions) planter.Apply(i >= 60 && PlayerAnimator.LegsOnFeet(animator), Dt, animator.IsInTransition(0));
                else planter.Apply(i >= 60 && !animator.IsInTransition(0), Dt); // avant : coupé pendant le fondu

                for (int k = 0; k < 2; k++)
                {
                    Vector3 p = toes[k].position;
                    if (i >= 60) lowest = Mathf.Min(lowest, p.y);
                    if (i >= 60 && p.y < contact[k] && previous[k].y < contact[k])
                    {
                        Vector3 d = p - previous[k]; d.y = 0f;
                        path += d.magnitude;
                    }
                    previous[k] = p;
                }
            }
            return path;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void SeBaisserEtSeRelever_LesPiedsNeGlissentPas()
    {
        // Le seuil de contact de chaque pointe : son plus bas dans la plus haute des deux attentes
        // (le pied droit accroupi repose sur une pointe à 5 cm du sol, contre 3 debout), plus 5 mm.
        var standing = new[] { float.MaxValue, float.MaxValue };
        var crouched = new[] { float.MaxValue, float.MaxValue };
        MeasureLows(standing, crouched);
        var contact = new float[2];
        for (int k = 0; k < 2; k++) contact[k] = Mathf.Max(standing[k], crouched[k]) + 0.005f;

        float before = Run(false, contact, out float lowestBefore);
        float after = Run(true, contact, out float lowestAfter);
        Debug.Log($"[ACCROUPI] pointes posées : {after * 100f:F1} cm au sol, contre {before * 100f:F1} cm sans calage pendant le fondu ; " +
                  $"pointe la plus basse {lowestAfter * 100f:F1} cm, contre {lowestBefore * 100f:F1} (seuils {contact[0]:F3} / {contact[1]:F3} m)");
        Assert.Greater(before, 0.2f, "Garde anti-vacuité : sans calage pendant le fondu, le pied droit devrait glisser.");
        // Mesuré le 2026-10-10 : 2,3 cm en tout, contre 131 cm quand le calage était coupé pendant
        // le fondu (le pied droit glissait de 38 cm à l'aller comme au retour).
        Assert.Less(after, 0.04f, $"Les pointes posées ont glissé de {after * 100f:F1} cm (sans calage pendant le fondu : {before * 100f:F1} cm).");
        // Le mélange des deux poses passait la pointe 5 cm sous le sol ; posée à plat, elle est à 3 cm.
        Assert.Greater(lowestAfter, 0.02f, $"Une pointe est descendue à {lowestAfter * 100f:F1} cm : elle traverse le sol.");
    }

    /// <summary>Le plus bas de chaque pointe, animation seule, dans l'attente debout et dans l'attente accroupie.</summary>
    private static void MeasureLows(float[] standing, float[] crouched)
    {
        GameObject instance = TestCharacter.InstantiateModel(out Animator animator);
        try
        {
            var toes = new[] { animator.GetBoneTransform(HumanBodyBones.LeftToes), animator.GetBoneTransform(HumanBodyBones.RightToes) };
            for (int i = 0; i < 360; i++)
            {
                int stance = StanceAt(i);
                animator.SetInteger("Stance", stance);
                animator.SetFloat("StanceF", stance);
                animator.SetFloat("Marche", 0f);
                animator.Update(Dt);
                for (int k = 0; k < 2; k++)
                {
                    float y = toes[k].position.y;
                    if (i >= 60 && i < 120) standing[k] = Mathf.Min(standing[k], y);
                    if (i >= 300) crouched[k] = Mathf.Min(crouched[k], y);
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }
}
#endif
