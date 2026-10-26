using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// L'écran du visage à la mort du robot (2026-10-26, demandé par l'utilisateur : « un écran glitch avec écrit
/// FATAL ERROR sur l'écran du visage ») : sa visière grésille et affiche FATAL ERROR (le shader
/// DuelArena/EcranVisage), et ses lueurs s'éteignent (yeux, cœur, fentes : les matériaux « Lueur »), le robot
/// est hors service.
///
/// Posé sur l'épave (RobotCorpse.Spawn), sur chaque machine : catégorie C. Par MaterialPropertyBlock, pièce de
/// matériau par pièce de matériau : le robot vivant, qui partage les mêmes matériaux, garde son écran éteint et
/// ses lueurs. Un robot sans visière (le Y Bot) n'a rien à afficher.
/// </summary>
public static class RobotFaceScreen
{
    public const string ScreenShader = "DuelArena/EcranVisage";

    private static readonly int Dead = Shader.PropertyToID("_Mort");
    private static readonly int DeadSince = Shader.PropertyToID("_MortDepuis");
    private static readonly int LitEmission = Shader.PropertyToID("_EmissionColor");   // URP Lit
    private static readonly int CelEmission = Shader.PropertyToID("_Emission");        // DuelArena/RobotCelShading

    private static readonly List<int> Screens = new List<int>();
    private static readonly List<int> Glows = new List<int>();

    /// <summary>
    /// Les emplacements de `materials` à changer à la mort : ceux de la visière (le shader de l'écran) dans
    /// `screens`, ceux des lueurs (un matériau dont le nom contient « Lueur ») dans `glows`. Fonction pure, testée.
    /// </summary>
    public static void Slots(Material[] materials, List<int> screens, List<int> glows)
    {
        screens.Clear();
        glows.Clear();
        for (int i = 0; i < materials.Length; i++)
        {
            Material m = materials[i];
            if (m == null) continue;
            if (m.shader != null && m.shader.name == ScreenShader) screens.Add(i);
            else if (m.name.Contains(PlayerPaint.GlowSlot)) glows.Add(i);
        }
    }

    /// <summary>
    /// Allume l'écran de la mort sur les rendus sous `root` (le robot est mort à `since`, sur l'horloge du
    /// shader : Time.timeSinceLevelLoad), et éteint ses lueurs.
    /// </summary>
    public static void ShowDeath(GameObject root, float since)
    {
        var block = new MaterialPropertyBlock();
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            Slots(r.sharedMaterials, Screens, Glows);
            foreach (int i in Screens)
            {
                r.GetPropertyBlock(block, i);
                block.SetFloat(Dead, 1f);
                block.SetFloat(DeadSince, since);
                r.SetPropertyBlock(block, i);
            }
            foreach (int i in Glows)
            {
                r.GetPropertyBlock(block, i);
                block.SetColor(LitEmission, Color.black);
                block.SetColor(CelEmission, Color.black);
                r.SetPropertyBlock(block, i);
            }
        }
    }
}
