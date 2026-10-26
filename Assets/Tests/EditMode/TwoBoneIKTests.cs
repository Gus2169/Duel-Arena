using NUnit.Framework;
using UnityEngine;

/// <summary>
/// L'IK à deux os des bras (TwoBoneIK, 2026-10-17) : un point porté par l'avant-bras (le poing du gant du
/// taser, au-delà du poignet) amené sur sa cible, et le coude plié vers un pôle. Un signe faux ne lève
/// aucune erreur : le coude se retourne vers le haut, ou le poing manque l'arme de quelques centimètres.
/// Les jambes, sans pôle, sont couvertes par FootPlanterTests.
/// </summary>
public class TwoBoneIKTests
{
    private GameObject shoulder;

    [TearDown]
    public void TearDown()
    {
        if (shoulder != null) Object.DestroyImmediate(shoulder);
    }

    /// <summary>Un bras gauche : épaule à 1,45 m, coude plié en arrière et vers l'extérieur, poignet devant.</summary>
    private void BuildArm(out Transform upper, out Transform lower, out Transform hand)
    {
        shoulder = new GameObject("Epaule");
        shoulder.transform.position = new Vector3(-0.18f, 1.45f, 0f);
        shoulder.transform.rotation = Quaternion.Euler(10f, -20f, 5f);
        upper = shoulder.transform;
        lower = new GameObject("Coude").transform;
        lower.SetParent(upper, false);
        lower.position = new Vector3(-0.30f, 1.22f, 0.12f);
        hand = new GameObject("Poignet").transform;
        hand.SetParent(lower, false);
        hand.position = new Vector3(-0.08f, 1.20f, 0.36f);
    }

    [Test]
    public void LePoingPorteParLAvantBras_AtteintSaCible_EtLeCoudePlieVersLePole()
    {
        BuildArm(out Transform upper, out Transform lower, out Transform hand);
        float arm = Vector3.Distance(upper.position, lower.position);
        float forearm = Vector3.Distance(lower.position, hand.position);
        // Le poing du gant : 14 cm au-delà du poignet, un peu de côté (WeaponGrip.Settings.gloveFistCenter).
        Vector3 fist = lower.InverseTransformPoint(hand.position + (hand.position - lower.position).normalized * 0.14f + Vector3.up * 0.01f);
        float reach = Vector3.Distance(lower.position, lower.TransformPoint(fist));
        Vector3 target = new Vector3(-0.02f, 1.36f, 0.52f);
        Vector3 pole = new Vector3(-0.5f, -1f, 0f);

        bool reached = TwoBoneIK.Solve(upper, lower, fist, target, pole, 1f);

        Vector3 end = lower.TransformPoint(fist);
        Assert.IsTrue(reached, "La cible est à portée.");
        Assert.Less(Vector3.Distance(end, target), 1e-3f, $"Le poing est à {Vector3.Distance(end, target) * 100f:F1} cm de sa cible.");
        Assert.AreEqual(arm, Vector3.Distance(upper.position, lower.position), 1e-4f, "Le bras a changé de longueur.");
        Assert.AreEqual(forearm, Vector3.Distance(lower.position, hand.position), 1e-4f, "L'avant-bras a changé de longueur.");
        Assert.AreEqual(reach, Vector3.Distance(lower.position, end), 1e-4f, "Le poing a bougé sur l'avant-bras.");

        Vector3 axis = target - upper.position;
        Vector3 elbow = Vector3.ProjectOnPlane(lower.position - upper.position, axis);
        Vector3 wanted = Vector3.ProjectOnPlane(pole, axis);
        float angle = Vector3.Angle(elbow, wanted);
        Assert.Less(angle, 0.5f, $"Le coude plie à {angle:F1}° du pôle (vers le bas et l'extérieur).");
        Assert.Less(lower.position.y, (upper.position.y + target.y) * 0.5f, "Garde anti-vacuité : le coude est sous la ligne épaule-poing.");
    }

    [Test]
    public void SansPole_LeCoudeGardeSonPlan_EtHorsDePortee_LeBrasSeTend()
    {
        BuildArm(out Transform upper, out Transform lower, out Transform hand);
        Vector3 bendBefore = Vector3.Cross(hand.position - upper.position, lower.position - upper.position).normalized;
        Vector3 target = new Vector3(-0.06f, 1.30f, 0.45f);

        Assert.IsTrue(TwoBoneIK.Solve(upper, lower, lower.InverseTransformPoint(hand.position), target, Vector3.zero, 0f));
        Assert.Less(Vector3.Distance(hand.position, target), 1e-3f);
        Vector3 bendAfter = Vector3.Cross(hand.position - upper.position, lower.position - upper.position).normalized;
        Assert.Less(Vector3.Angle(bendBefore, bendAfter), 25f, "Sans pôle, le plan du coude ne doit tourner que du peu qu'il faut pour suivre la cible.");

        float length = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
        Vector3 far = upper.position + new Vector3(0.2f, -0.1f, 1f).normalized * (length + 0.2f);
        Assert.IsFalse(TwoBoneIK.Solve(upper, lower, lower.InverseTransformPoint(hand.position), far, Vector3.zero, 0f), "Hors de portée, la fonction le dit.");
        Assert.Less(Vector3.Angle(hand.position - upper.position, far - upper.position), 0.5f, "Hors de portée, le bras pointe vers la cible.");
        Assert.Greater(Vector3.Distance(upper.position, hand.position), length - 0.01f, "Hors de portée, le bras se tend.");
    }
}
