using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// L'essai des trois rendus sur l'arène habillée (2026-10-27, demandé par l'utilisateur avant de choisir le rendu du
/// jeu) : les mêmes vues, avec des robots, en low poly propre, en cel-shading et dans les deux mêlés. Duel Arena >
/// Essais > Captures des trois rendus de l'arène, sur l'arène habillée et ouverte. Rien n'est enregistré dans la scène.
///
/// Le décor change de rendu par le réglage global de DuelArena/Decor (_DecorStyle) ; les robots prennent les matériaux
/// de l'essai du CT-01 (RenderTrialBuilder : CT01_Cel_*, DuelArena/RobotCelShading), peints comme en jeu.
/// </summary>
public static class ArenaStyleTrial
{
    public static readonly string[] Styles = { "1_low_poly_propre", "2_cel_shading", "3_meles" };
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const int Orange = 0, Blue = 1, Magenta = 2, Green = 3, Cyan = 4, Violet = 5, Yellow = 6;

    /// <summary>Un robot de l'essai : où il est, où il regarde, sa couleur, sa pose et l'instant de sa pose.</summary>
    private readonly struct Figure
    {
        public readonly Vector3 position, lookAt;
        public readonly int paint;
        public readonly string pose;
        public readonly float time;

        public Figure(Vector3 position, Vector3 lookAt, int paint, string pose, float time)
        {
            this.position = position;
            this.lookAt = lookAt;
            this.paint = paint;
            this.pose = pose;
            this.time = time;
        }
    }

    private static readonly Figure[] Figures =
    {
        new Figure(new Vector3(-3.5f, 0f, 15.5f), new Vector3(8f, 0f, 21f), Orange, "idle", 0.4f),
        new Figure(new Vector3(9f, 0f, 22.5f), new Vector3(-3f, 0f, 16f), Blue, "punch", 0.15f),
        new Figure(new Vector3(-6.2f, 0f, 25.4f), new Vector3(-9f, 0f, 21f), Violet, "idle", 1.3f),
        new Figure(new Vector3(-35.2f, 4.5f, 26f), new Vector3(0f, 4.5f, 31f), Yellow, "idle", 1.1f),
        new Figure(new Vector3(-34.7f, 4.5f, 31.5f), new Vector3(0f, 4.5f, 31f), Cyan, "celebration", 1.6f),
        new Figure(new Vector3(-35.4f, 4.5f, 37f), new Vector3(0f, 4.5f, 31f), Magenta, "idle", 0.9f),
    };

    public static readonly DecorPreview.View[] Views =
    {
        new DecorPreview.View("A_duel", new Vector3(-9.5f, 1.65f, 10.5f), new Vector3(3f, 1.1f, 21f)),
        new DecorPreview.View("B_tunnel", new Vector3(-10f, 1.65f, 17f), new Vector3(-1f, 2.0f, 26f)),
        new DecorPreview.View("C_tribune", new Vector3(-35.3f, 6.15f, 18f), new Vector3(-16f, 2.2f, 37f)),
        new DecorPreview.View("D_ensemble", new Vector3(14f, 13f, -6f), new Vector3(-3f, 0f, 34f), 62f),
        new DecorPreview.View("E_caisses", new Vector3(-7.5f, 1.65f, 23.5f), new Vector3(-3.5f, 0.9f, 27f)),
    };

    [MenuItem("Duel Arena/Essais/Captures des trois rendus de l'arène")]
    public static void CaptureFromMenu()
    {
        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "EssaiRendusArene");
        List<string> files = Capture(folder);
        Debug.Log($"[Essai des rendus] {files.Count} images dans {folder}");
    }

    /// <summary>Rend les vues dans les trois rendus (`<rendu>_<vue>.png`), puis remet le décor en low poly propre.</summary>
    public static List<string> Capture(string folder, int width = 1600, int height = 900)
    {
        var files = new List<string>();
        var holder = new GameObject("EssaiRendus (temporaire)") { hideFlags = HideFlags.DontSave };
        var graphs = new List<PlayableGraph>();
        try
        {
            var robots = new List<Dressed>();
            foreach (Figure f in Figures)
            {
                Dressed robot = PlaceRobot(holder.transform, f, graphs);
                if (robot != null) robots.Add(robot);
            }
            Dictionary<Material, Material> cel = CelMaterials();
            for (int style = 0; style < Styles.Length; style++)
            {
                RenderStyle.Set((RenderStyleMode)style);
                foreach (Dressed robot in robots) Dress(robot, style == 0 ? null : cel);
                foreach (PlayableGraph g in graphs) g.Evaluate(0f);
                files.AddRange(DecorPreview.Capture(folder, Styles[style] + "_", Views, width, height));
            }
        }
        finally
        {
            RenderStyle.Set(RenderStyleMode.LowPolyPropre);
            foreach (PlayableGraph g in graphs)
            {
                if (g.IsValid()) g.Destroy();
            }
            Object.DestroyImmediate(holder);
        }
        return files;
    }

    // ------------------------------------------------------------------
    // Les robots
    // ------------------------------------------------------------------

    private static Dressed PlaceRobot(Transform parent, Figure f, List<PlayableGraph> graphs)
    {
        var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform assetModel = player != null ? player.transform.Find("Model") : null;
        if (assetModel == null) return null;
        var holder = new GameObject("Robot");
        holder.transform.SetParent(parent, false);
        Vector3 d = f.lookAt - f.position;
        holder.transform.SetPositionAndRotation(f.position, Quaternion.Euler(0f, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f));
        GameObject copy = RobotAppearance.Copy(assetModel.gameObject, holder.transform, "Apparence");
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        Color32 paint = Palette(player)[f.paint];
        RobotAppearance.Paint(player, assetModel, copy, paint);
        var keep = new Dressed { paint = paint };
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) keep.original[r] = r.sharedMaterials;

        Animator animator = copy.GetComponent<Animator>();
        AnimationClip clip = Clip(f.pose);
        if (animator == null || clip == null) return keep;
        animator.applyRootMotion = false;
        animator.runtimeAnimatorController = null;
        PlayableGraph graph = PlayableGraph.Create("EssaiRendus");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(false);
        playable.SetTime(Mathf.Repeat(f.time, Mathf.Max(0.01f, clip.length)));
        output.SetSourcePlayable(playable);
        graph.Evaluate(0f);
        graphs.Add(graph);
        return keep;
    }

    /// <summary>Ce qu'il faut pour rhabiller un robot entre deux rendus : sa peinture, les matériaux du jeu de ses pièces.</summary>
    private sealed class Dressed
    {
        public Color32 paint;
        public readonly Dictionary<Renderer, Material[]> original = new Dictionary<Renderer, Material[]>();
    }

    /// <summary>Les matériaux du jeu (null) ou du cel-shading, et la peinture du robot par-dessus. Sous le cel-shading,
    /// la lueur se donne par _Emission (DuelArena/RobotCelShading).</summary>
    private static void Dress(Dressed memo, Dictionary<Material, Material> cel)
    {
        var block = new MaterialPropertyBlock();
        foreach (KeyValuePair<Renderer, Material[]> kv in memo.original)
        {
            Renderer r = kv.Key;
            Material[] slots = (Material[])kv.Value.Clone();
            if (cel != null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != null && cel.TryGetValue(slots[i], out Material c)) slots[i] = c;
                }
            }
            r.sharedMaterials = slots;
            r.SetPropertyBlock(null);
            for (int i = 0; i < slots.Length; i++) r.SetPropertyBlock(null, i);
            PlayerPaint.PaintPart(r, memo.paint, block, true);
            if (cel == null) continue;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null || !slots[i].name.Contains("Lueur")) continue;
                r.GetPropertyBlock(block, i);
                block.SetColor("_Emission", (Color)memo.paint * 1.6f);
                r.SetPropertyBlock(block, i);
            }
        }
    }

    /// <summary>Le matériau de cel-shading de chaque matériau du CT-01 (CT01_Peinture → CT01_Cel_Peinture).</summary>
    private static Dictionary<Material, Material> CelMaterials()
    {
        var result = new Dictionary<Material, Material>();
        foreach (string guid in AssetDatabase.FindAssets("t:Material CT01_", new[] { "Assets/_ProjectArt/Robots/CT-01" }))
        {
            var game = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (game == null || !game.name.StartsWith("CT01_")) continue;
            var cel = AssetDatabase.LoadAssetAtPath<Material>($"{RenderTrialBuilder.Folder}/CT01_Cel_{game.name.Substring(5)}.mat");
            if (cel != null) result[game] = cel;
        }
        return result;
    }

    private static IReadOnlyList<Color32> Palette(GameObject player)
    {
        PlayerPaint paint = player != null ? player.GetComponent<PlayerPaint>() : null;
        return paint != null ? paint.Palette : new Color32[] { new Color32(244, 190, 28, 255) };
    }

    private static AnimationClip Clip(string pose)
    {
        (string path, string name) = pose switch
        {
            "celebration" => ("Assets/_ProjectArt/Animations/New/Y Bot@Celebration1.fbx", "Celebration1"),
            "punch" => ("Assets/_ProjectArt/Animations/New/Y Bot@Taser Punch.fbx", "Taser Punch"),
            _ => ("Assets/_ProjectArt/Animations/MainsNues/Y Bot@Idle.fbx", "Idle"),
        };
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (o is AnimationClip clip && clip.name == name) return clip;
        }
        return null;
    }
}
