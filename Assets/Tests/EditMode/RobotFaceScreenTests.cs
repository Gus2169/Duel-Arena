using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// L'écran du visage à la mort (2026-10-26) : la visière de l'épave affiche FATAL ERROR, ses lueurs s'éteignent.
///
/// Ce qui casse ici ne lève aucune erreur : l'écran posé sur la mauvaise pièce de matériau (la peinture qui
/// grésille, la visière qui reste noire), l'instant de la mort perdu (le glitch de la mort ne se joue jamais), des
/// lueurs qui restent allumées sur un robot hors service.
/// </summary>
public class RobotFaceScreenTests
{
    private readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in created) if (o != null) Object.DestroyImmediate(o);
        created.Clear();
    }

    private Material Material(string name, string shader)
    {
        var material = new Material(Shader.Find(shader)) { name = name };
        created.Add(material);
        return material;
    }

    [Test]
    public void ALaMort_LaVisiereAfficheFatalError_EtLesLueursSEteignent()
    {
        Assert.IsNotNull(Shader.Find(RobotFaceScreen.ScreenShader), "le shader de l'écran existe");
        var robot = new GameObject("Robot");
        created.Add(robot);
        var renderer = robot.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[]
        {
            Material("CT01_Peinture", "Universal Render Pipeline/Lit"),
            Material("CT01_Ecran", RobotFaceScreen.ScreenShader),
            Material("CT01_Lueur", "Universal Render Pipeline/Lit"),
        };

        RobotFaceScreen.ShowDeath(robot, 12.5f);

        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block, 1);
        Assert.AreEqual(1f, block.GetFloat("_Mort"), "la visière affiche l'écran de la mort");
        Assert.AreEqual(12.5f, block.GetFloat("_MortDepuis"), "à l'instant de la mort");
        renderer.GetPropertyBlock(block, 2);
        Assert.AreEqual(Color.black, block.GetColor("_EmissionColor"), "les lueurs s'éteignent");
        renderer.GetPropertyBlock(block, 0);
        Assert.IsTrue(block.isEmpty, "la peinture n'est pas touchée");
    }

    [Test]
    public void UnRobotSansVisiere_NAfficheRien()
    {
        var screens = new List<int>();
        var glows = new List<int>();
        RobotFaceScreen.Slots(new[] { Material("Alpha_Body_MAT", "Universal Render Pipeline/Lit") }, screens, glows);
        Assert.IsEmpty(screens);
        Assert.IsEmpty(glows);
    }
}
