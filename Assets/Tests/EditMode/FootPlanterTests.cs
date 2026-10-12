using NUnit.Framework;
using UnityEngine;

/// <summary>
/// L'IK à deux os qui retient les pieds au sol (FootPlanter, 2026-10-10). Un signe faux dans le
/// pliage du genou ne lève aucune erreur : la jambe se replie à l'envers, ou le pied manque sa cible
/// de quelques centimètres, et le pied glisse quand même.
/// </summary>
public class FootPlanterTests
{
    private GameObject hip;

    [TearDown]
    public void TearDown()
    {
        if (hip != null) Object.DestroyImmediate(hip);
    }

    /// <summary>Une jambe pliée : hanche à 0,95 m, genou en avant, cheville au sol.</summary>
    private void BuildLeg(out Transform upper, out Transform lower, out Transform foot)
    {
        hip = new GameObject("Hanche");
        hip.transform.position = new Vector3(0.1f, 0.95f, 0f);
        upper = hip.transform;
        lower = new GameObject("Genou").transform;
        lower.SetParent(upper, false);
        lower.position = new Vector3(0.11f, 0.52f, 0.08f);
        foot = new GameObject("Cheville").transform;
        foot.SetParent(lower, false);
        foot.position = new Vector3(0.1f, 0.09f, -0.02f);
        foot.rotation = Quaternion.Euler(-10f, 20f, 3f);
    }

    [Test]
    public void LaChevilleAtteintSaCible_SansAllongerLesOs_EtLePiedGardeSonOrientation()
    {
        BuildLeg(out Transform upper, out Transform lower, out Transform foot);
        float thigh = Vector3.Distance(upper.position, lower.position);
        float shin = Vector3.Distance(lower.position, foot.position);
        Quaternion footRotation = foot.rotation;
        Vector3 kneeBefore = lower.position;

        // Le pied retenu 12 cm derrière et 4 cm de côté : ce qu'il faut pour annuler une glissade.
        Vector3 target = foot.position + new Vector3(0.04f, 0f, -0.12f);
        FootPlanter.SolveTwoBone(upper, lower, foot, target);

        Assert.Less(Vector3.Distance(foot.position, target), 1e-3f, $"La cheville est à {Vector3.Distance(foot.position, target) * 100f:F1} cm de sa cible.");
        Assert.AreEqual(thigh, Vector3.Distance(upper.position, lower.position), 1e-4f, "La cuisse a changé de longueur.");
        Assert.AreEqual(shin, Vector3.Distance(lower.position, foot.position), 1e-4f, "Le mollet a changé de longueur.");
        Assert.Less(Quaternion.Angle(footRotation, foot.rotation), 0.01f, "Le pied a tourné : il ne serait plus à plat.");
        // Le genou plie toujours vers l'avant : une IK au signe faux le retournerait vers l'arrière.
        Assert.Greater(lower.position.z - (upper.position.z + foot.position.z) * 0.5f, 0.02f,
            $"Le genou plie vers l'arrière (avant : z = {kneeBefore.z:F2}, après : {lower.position.z:F2}).");
    }

    [Test]
    public void HorsDePortee_LaJambeSeTendVersLaCible()
    {
        BuildLeg(out Transform upper, out Transform lower, out Transform foot);
        float reach = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, foot.position);
        Vector3 target = upper.position + new Vector3(0f, -1f, 0.6f).normalized * (reach + 0.3f);

        FootPlanter.SolveTwoBone(upper, lower, foot, target);

        Vector3 toFoot = foot.position - upper.position;
        Assert.Less(Vector3.Angle(toFoot, target - upper.position), 0.5f, "La jambe ne pointe pas vers la cible.");
        Assert.Greater(toFoot.magnitude, reach - 0.01f, "La jambe ne s'est pas tendue.");
    }
}
