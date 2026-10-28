using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Le réglage du rendu (2026-10-27, RenderStyle) : chaque matériau d'un châssis a son double de cel-shading (sauf la
/// visière, qui garde l'écran de la mort) ; un robot passe au cel-shading et en revient, peinture comprise ; le rendu
/// pilote le décor et son contour, que chaque renderer d'URP du jeu sait dessiner ; un mannequin du stand garde ses
/// lueurs éteintes dans chaque rendu.
///
/// Vus échouer : une paire retirée (un matériau sans double), une paire qui ramène au mauvais matériau, le contour
/// éteint dans le renderer, les lueurs d'un mannequin éteintes pour URP Lit seulement (en cel-shading, ses yeux
/// restaient allumés).
/// </summary>
public class RenderStyleTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const string DummyPrefabPath = "Assets/_ProjectArena/Stand/Mannequin.prefab";
    private const string CelShader = "DuelArena/RobotCelShading";

    [TearDown]
    public void Restore() => RenderStyle.Set(RenderStyleMode.LowPolyPropre);

    [Test]
    public void ChaqueMateriauDUnChassis_ASonCelShading_SaufLaVisiere()
    {
        RobotStyleMaterials map = RenderStyle.Materials;
        Assert.IsNotNull(map, "Les matériaux des robots par rendu (Duel Arena > Rendu > Préparer le réglage du rendu).");
        Transform model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).transform.Find("Model");
        int mapped = 0, visors = 0;
        foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (RobotChassis.OwnerOf(r.name) < 0) continue;
            foreach (Material m in r.sharedMaterials)
            {
                Material cel = map.For(m, true);
                if (m.shader.name == "DuelArena/EcranVisage")
                {
                    Assert.AreSame(m, cel, $"{r.name} : la visière garde son matériau (l'écran de la mort).");
                    visors++;
                    continue;
                }
                Assert.AreNotSame(m, cel, $"{r.name} : {m.name} n'a pas de matériau de cel-shading.");
                Assert.AreEqual("DuelArena/RobotCelShading", cel.shader.name, cel.name);
                Assert.AreSame(m, map.For(cel, false), $"{cel.name} ramène à {m.name}.");
                mapped++;
            }
        }
        Assert.Greater(mapped, 15, "Les matériaux des trois châssis ont été vus.");
        Assert.Greater(visors, 0, "La visière du CT-01 a été vue.");
    }

    [Test]
    public void UnRobot_PasseAuCelShadingEtEnRevient_SansPerdreSaPeinture()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform assetModel = prefab.transform.Find("Model");
        var holder = new GameObject("Essai du réglage du rendu");
        try
        {
            GameObject copy = RobotAppearance.Copy(assetModel.gameObject, holder.transform, "Robot");
            Renderer[] painted = RobotAppearance.Paint(prefab, assetModel, copy, new Color32(40, 120, 230, 255));
            Dictionary<Renderer, Material[]> original = copy.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.sharedMaterials);
            Dictionary<(Renderer, int), Color> paint = PaintOf(painted);
            Assert.Greater(paint.Count, 0, "Des pièces peintes.");

            RenderStyle.Set(RenderStyleMode.Meles);
            int cel = original.Sum(kv => kv.Key.sharedMaterials.Where((m, i) => m != kv.Value[i]).Count());
            Assert.Greater(cel, 15, "Le robot est passé au cel-shading.");
            var block = new MaterialPropertyBlock();
            foreach (KeyValuePair<(Renderer, int), Color> kv in paint)
            {
                kv.Key.Item1.GetPropertyBlock(block, kv.Key.Item2);
                Assert.AreEqual(kv.Value, block.GetColor("_BaseColor"), $"{kv.Key.Item1.name} garde sa peinture.");
            }

            RenderStyle.Set(RenderStyleMode.LowPolyPropre);
            foreach (KeyValuePair<Renderer, Material[]> kv in original)
                CollectionAssert.AreEqual(kv.Value, kv.Key.sharedMaterials, $"{kv.Key.name} a retrouvé ses matériaux.");
        }
        finally
        {
            Object.DestroyImmediate(holder);
        }
    }

    [Test]
    public void LeRendu_PiloteLeDecorEtSonContour()
    {
        RenderStyle.Set(RenderStyleMode.Meles);
        Assert.AreEqual(2f, Shader.GetGlobalFloat("_DecorStyle"));
        Assert.IsTrue(RenderStyle.Outlines && RenderStyle.CelRobots);
        RenderStyle.Set(RenderStyleMode.LowPolyPropre);
        Assert.AreEqual(0f, Shader.GetGlobalFloat("_DecorStyle"));
        Assert.IsFalse(RenderStyle.Outlines || RenderStyle.CelRobots);

        // Le shader du décor porte son contour sous l'étiquette que dessine DecorOutlineFeature...
        Shader decor = Shader.Find("DuelArena/Decor");
        bool tagged = false;
        for (int i = 0; i < decor.passCount; i++)
            tagged |= decor.FindPassTagValue(i, new ShaderTagId("LightMode")).name == DecorOutlineFeature.LightMode;
        Assert.IsTrue(tagged, "Le contour du décor porte l'étiquette DecorContour.");

        // ... et chaque renderer d'URP du jeu l'a dans ses fonctions, allumée.
        var pipelines = new HashSet<RenderPipelineAsset> { GraphicsSettings.defaultRenderPipeline };
        for (int i = 0; i < QualitySettings.count; i++) pipelines.Add(QualitySettings.GetRenderPipelineAssetAt(i));
        int renderers = 0;
        foreach (RenderPipelineAsset pipeline in pipelines.Where(p => p != null))
        {
            SerializedProperty list = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is not ScriptableRendererData data) continue;
                Assert.IsTrue(data.rendererFeatures.Any(f => f is DecorOutlineFeature && f.isActive), $"{data.name} dessine le contour du décor.");
                renderers++;
            }
        }
        Assert.Greater(renderers, 0, "Les renderers d'URP du jeu ont été vus.");
    }

    [Test]
    public void UnMannequinDuStand_GardeSesLueursEteintes_DansChaqueRendu()
    {
        var dummy = AssetDatabase.LoadAssetAtPath<GameObject>(DummyPrefabPath).GetComponent<TrainingDummy>();
        var model = new SerializedObject(dummy).FindProperty("robotModel").objectReferenceValue as GameObject;
        Assert.IsNotNull(model, "Le modèle du robot que copie le mannequin.");
        var holder = new GameObject("Essai des lueurs du mannequin");
        try
        {
            // Teint dans un rendu (au lancement du stand), puis montré dans chacun (le réglage changé en jeu).
            foreach (RenderStyleMode built in new[] { RenderStyleMode.Meles, RenderStyleMode.LowPolyPropre })
            {
                RenderStyle.Set(built);
                GameObject copy = RobotAppearance.Copy(model, holder.transform, "Apparence");
                TrainingDummy.Tint(copy, new Color(0.86f, 0.74f, 0.42f, 1f));
                foreach (RenderStyleMode shown in new[] { RenderStyleMode.Meles, RenderStyleMode.LowPolyPropre })
                {
                    RenderStyle.Set(shown);
                    AssertGlowsOff(copy, shown == RenderStyleMode.Meles, $"teint en {built}, montré en {shown}");
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(holder);
        }
    }

    // Chaque lueur des châssis de la copie est éteinte, lue comme son shader la lit : _Emission en cel-shading,
    // _EmissionColor en URP Lit ; le bloc de propriétés de l'emplacement, sinon le matériau. Le gant garde son rendu
    // (DuelArena/ArmeUsee) dans chaque réglage : il n'est pas de ce test.
    private static void AssertGlowsOff(GameObject copy, bool cel, string context)
    {
        int glows = 0, glowingMaterials = 0;
        var block = new MaterialPropertyBlock();
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
        {
            if (RobotChassis.OwnerOf(r.name) < 0) continue;
            Material[] slots = r.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null || !slots[i].name.Contains(PlayerPaint.GlowSlot)) continue;
                Assert.AreEqual(cel, slots[i].shader.name == CelShader, $"{context} : {r.name}/{slots[i].name} est dans le bon rendu.");
                string property = cel ? "_Emission" : "_EmissionColor";
                r.GetPropertyBlock(block, i);
                Color glow = block.HasColor(property) ? block.GetColor(property) : slots[i].GetColor(property);
                Assert.AreEqual(0f, glow.maxColorComponent, 1e-4f, $"{context} : {r.name}/{slots[i].name} reste allumé.");
                if (slots[i].GetColor(property).maxColorComponent > 0f) glowingMaterials++;
                glows++;
            }
        }
        Assert.Greater(glows, 0, $"{context} : des lueurs ont été vues.");
        Assert.Greater(glowingMaterials, 0, $"{context} : des matériaux luisent d'eux-mêmes (sans quoi le test ne prouverait rien).");
    }

    // La couleur posée sur chaque emplacement « Peinture » des pièces peintes.
    private static Dictionary<(Renderer, int), Color> PaintOf(Renderer[] parts)
    {
        var result = new Dictionary<(Renderer, int), Color>();
        var block = new MaterialPropertyBlock();
        foreach (Renderer r in parts)
        {
            Material[] slots = r.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null || !slots[i].name.Contains("Peinture")) continue;
                r.GetPropertyBlock(block, i);
                result[(r, i)] = block.GetColor("_BaseColor");
            }
        }
        return result;
    }
}
