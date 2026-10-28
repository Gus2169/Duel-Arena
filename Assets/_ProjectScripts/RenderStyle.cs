using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Le rendu du jeu. Ses valeurs sont celles de `_DecorStyle` du shader DuelArena/Decor (enregistrées dans
/// les réglages du joueur) : ajouter à la fin.</summary>
public enum RenderStyleMode
{
    LowPolyPropre = 0,
    CelShading = 1,
    Meles = 2,
}

/// <summary>
/// Le rendu de cette machine, au choix du joueur le temps de la phase de test (2026-10-27, demandé par l'utilisateur :
/// « les deux mêlés pour la phase de test, je ferai mon choix final plus tard ») ; réglé dans les Options
/// (GameSettings.PreferredRenderStyle). Catégorie C : rien du jeu ne le lit, chacun voit le sien. Il pilote :
/// - le décor : la globale `_DecorStyle` de DuelArena/Decor (0 low poly propre, 1 cel-shading, 2 mêlés) ;
/// - le contour du décor, que DecorOutlineFeature ne dessine qu'en cel-shading et en mêlés ;
/// - les robots : leurs matériaux du jeu ou ceux du cel-shading (RobotStyleMaterials), sur chaque copie suivie (le
///   robot de chaque joueur, les bras de la vue subjective, les robots des menus, de l'écran de fin et du stand).
/// Les armes et le gant gardent leur rendu (DuelArena/ArmeUsee) dans les trois.
/// </summary>
public static class RenderStyle
{
    private static readonly int DecorStyleId = Shader.PropertyToID("_DecorStyle");
    private static readonly List<GameObject> robots = new List<GameObject>();
    private static RobotStyleMaterials materials;

    public static RenderStyleMode Current { get; private set; } = RenderStyleMode.LowPolyPropre;

    /// <summary>Le contour (une coque retournée) du décor et des robots : en cel-shading et en mêlés.</summary>
    public static bool Outlines => Current != RenderStyleMode.LowPolyPropre;

    /// <summary>Les robots en cel-shading (DuelArena/RobotCelShading) : en cel-shading et en mêlés.</summary>
    public static bool CelRobots => Current != RenderStyleMode.LowPolyPropre;

    public static event Action Changed;

    // Le rechargement de domaine est désactivé à l'entrée en Play Mode : on oublie les robots d'avant, et en quittant
    // le Play Mode, l'Editor retrouve le rendu du jeu d'aujourd'hui (sinon la vue Scène et les captures du menu
    // garderaient celui de la partie).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        robots.Clear();
        Application.quitting -= BackToDefault;
        Application.quitting += BackToDefault;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySettings() => Set(GameSettings.PreferredRenderStyle);

    private static void BackToDefault() => Set(RenderStyleMode.LowPolyPropre);

    /// <summary>Passe tout ce qui se voit dans ce rendu : le décor, son contour, les robots suivis.</summary>
    public static void Set(RenderStyleMode mode)
    {
        Current = mode;
        Shader.SetGlobalFloat(DecorStyleId, (float)mode);
        for (int i = robots.Count - 1; i >= 0; i--)
        {
            if (robots[i] == null) robots.RemoveAt(i);
            else ApplyRobot(robots[i]);
        }
        Changed?.Invoke();
    }

    /// <summary>Suit un robot (la racine « Model » d'un joueur, ou une copie) : il prend tout de suite le rendu en
    /// cours, et le suivra s'il change. Un robot détruit est oublié au changement suivant.</summary>
    public static void TrackRobot(GameObject root)
    {
        if (root == null) return;
        if (!robots.Contains(root)) robots.Add(root);
        ApplyRobot(root);
    }

    private static void ApplyRobot(GameObject root)
    {
        RobotStyleMaterials map = Materials;
        if (map != null) map.Apply(root, CelRobots);
    }

    /// <summary>Les paires de matériaux des robots (Resources/RobotStyleMaterials, construites par Duel Arena > Rendu >
    /// Préparer le réglage du rendu) ; null tant qu'elles n'existent pas (les robots gardent alors leur rendu).</summary>
    public static RobotStyleMaterials Materials
    {
        get
        {
            if (materials == null) materials = Resources.Load<RobotStyleMaterials>(RobotStyleMaterials.ResourceName);
            return materials;
        }
    }
}
