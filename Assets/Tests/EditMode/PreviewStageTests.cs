using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Le plateau d'aperçu de Personnalisation (2026-10-27, demandé par l'utilisateur) : rien ne tourne seul, on
/// fait tourner le robot et l'arme à la souris, avec une légère inertie ; un pistolet n'emplit plus le cadre
/// comme un fusil ; et le prototype (le Y Bot) se cadre comme les autres châssis, tête comprise.
/// </summary>
public class PreviewStageTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const float Dt = 1f / 60f;
    private const float Decay = 0.22f;
    private const float MaxSpeed = 720f;

    [Test]
    public void SansSouris_RienNeTourne()
    {
        float speed = 0f;
        float total = 0f;
        for (int i = 0; i < 120; i++) total += PreviewStage.StepSpin(ref speed, false, 0f, Dt, Decay, MaxSpeed);
        Assert.AreEqual(0f, total, "Au repos, le sujet ne tourne pas seul.");
    }

    [Test]
    public void LacheEnPleinGeste_IlContinueUnPeu_PuisSArrete()
    {
        float speed = 0f;
        float during = 0f;
        // Un geste de 0,3 s à 2 degrés par image (120 °/s).
        for (int i = 0; i < 18; i++) during += PreviewStage.StepSpin(ref speed, true, 2f, Dt, Decay, MaxSpeed);
        Assert.AreEqual(36f, during, 1e-3f, "Tenu, il suit la souris exactement.");
        Assert.AreEqual(120f, speed, 6f, "La vitesse du geste est retenue.");

        float after = 0f;
        int frames = 0;
        while (speed != 0f && frames < 600)
        {
            after += PreviewStage.StepSpin(ref speed, false, 0f, Dt, Decay, MaxSpeed);
            frames++;
        }
        Assert.Greater(after, 10f, "Lâché, il continue sur sa lancée.");
        Assert.Less(after, 40f, "Une légère inertie : moins que le geste lui-même.");
        Assert.Less(frames * Dt, 1.5f, "Et il s'arrête vite.");
    }

    [Test]
    public void UneSourisArreteeAvantDeLacher_NeLanceRien()
    {
        float speed = 0f;
        for (int i = 0; i < 18; i++) PreviewStage.StepSpin(ref speed, true, 3f, Dt, Decay, MaxSpeed);
        for (int i = 0; i < 15; i++) PreviewStage.StepSpin(ref speed, true, 0f, Dt, Decay, MaxSpeed);   // 0,25 s immobile
        float after = 0f;
        for (int i = 0; i < 120; i++) after += PreviewStage.StepSpin(ref speed, false, 0f, Dt, Decay, MaxSpeed);
        Assert.Less(after, 1f, "Tenu immobile avant de lâcher : il ne repart pas.");
    }

    [Test]
    public void UnPistolet_GardeSaTailleParRapportAuxFusils()
    {
        Assert.AreEqual(0.6f, PreviewStage.WeaponFramingRadius(0.6f, 0.26f), 1e-6f, "Un fusil : son propre cadrage.");
        Assert.AreEqual(0.26f, PreviewStage.WeaponFramingRadius(0.13f, 0.26f), 1e-6f,
            "Un pistolet : cadré comme une arme de 26 cm, il ne remplit que la moitié du cadre.");
    }

    [Test]
    public void LePrototype_SeCadreCommeLesAutresChassis_TeteComprise()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform model = prefab.transform.Find("Model");
        var compute = typeof(PreviewStage).GetMethod("ComputeWorldBounds",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        float? reference = null;
        foreach (ChassisId id in System.Enum.GetValues(typeof(ChassisId)))
        {
            GameObject copy = RobotAppearance.Copy(model.gameObject, null, "Mesure");
            try
            {
                RobotChassis.Apply(copy.transform, id);
                var bounds = (Bounds)compute.Invoke(null, new object[] { copy.transform });
                float top = MeshTop(copy);
                Assert.GreaterOrEqual(bounds.max.y, top, $"{id} : le cadrage couvre le haut de sa tête ({top:0.000} m).");
                reference ??= bounds.max.y;
                Assert.AreEqual(reference.Value, bounds.max.y, 0.01f, $"{id} : cadré à la même hauteur que les autres châssis.");
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }
    }

    // Le haut du robot tel qu'il se voit : ses peaux allumées, dans leur pose.
    private static float MeshTop(GameObject robot)
    {
        float top = float.NegativeInfinity;
        foreach (SkinnedMeshRenderer skin in robot.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!skin.enabled) continue;
            var mesh = new Mesh();
            skin.BakeMesh(mesh, true);
            foreach (Vector3 v in mesh.vertices) top = Mathf.Max(top, skin.transform.TransformPoint(v).y);
            Object.DestroyImmediate(mesh);
        }
        return top;
    }
}
