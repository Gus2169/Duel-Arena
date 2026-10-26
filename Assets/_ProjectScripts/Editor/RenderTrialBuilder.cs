using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// L'essai des deux rendus du robot (2026-10-26, bible § 13 : « low poly propre ou cel-shading discret, à
/// prototyper » ; l'utilisateur, le 2026-10-25 : « je ne vois pas bien la différence, fais-moi un test des
/// deux bien distincts »). Sur des COPIES du robot provisoire : le Y Bot, son préfab et ses matériaux restent
/// intacts, et tout l'essai vit dans Assets/_ProjectArt/Essais/Rendus.
///
/// Duel Arena > Essais > Construire l'essai des rendus du robot : règle l'import de la copie low poly
/// (Tools/Blender/essais/robot_lowpoly.py : le Y Bot décimé, à facettes), crée les matériaux (la même
/// peinture violette pour les trois), un Animator d'attente et la scène EssaiRendus : trois robots côte à
/// côte sous la lumière de l'arène, l'actuel, le low poly et le cel-shading (DuelArena/RobotCelShading),
/// devant un décor sombre et terne (bible § 13). On corrige ce script, pas la scène.
///
/// Duel Arena > Essais > Construire l'essai du robot CT-01 (2026-10-26, le robot inspiré de KAY/O que
/// l'utilisateur a demandé : Tools/Blender/robot_ct01.py, exporté par --fbx) : la scène EssaiRendusCT01, le
/// robot actuel pour l'échelle, le CT-01 en low poly propre (URP Lit, aplats de ses matériaux) et en cel-shading.
/// L'usure du low poly n'est que dans les rendus de Blender : elle demanderait des textures cuites. Le CT-01 y est
/// dans son skin vierge (robot_ct01.py --skin vierge, choisi par l'utilisateur le 2026-10-26), sa visière est
/// l'écran du visage (DuelArena/EcranVisage), et un quatrième robot montre l'écran de la mort (FATAL ERROR),
/// celui que l'épave affichera (RobotFaceScreen), ses lueurs éteintes. Puis l'OS-02 (2026-10-26, Tools/Blender/robot_os02.py,
/// l'« Ossature » inspirée du T-800, à la demande de l'utilisateur), dans les deux rendus, aux mêmes tailles.
/// </summary>
public static class RenderTrialBuilder
{
    public const string Folder = "Assets/_ProjectArt/Essais/Rendus";
    public const string ScenePath = Folder + "/EssaiRendus.unity";
    public const string Ct01Path = Folder + "/Robot CT-01.fbx";
    public const string Ct01ScenePath = Folder + "/EssaiRendusCT01.unity";
    public const string Os02Path = Folder + "/Robot OS-02.fbx";
    private const string SourcePath = "Assets/_ProjectArt/Mesh/Personnages/Y Bot.fbx";
    private const string LowPolyPath = Folder + "/Y Bot LowPoly.fbx";
    private const string IdlePath = "Assets/_ProjectArt/Animations/MainsNues/Y Bot@Idle.fbx";
    private const string CelShaderName = "DuelArena/RobotCelShading";

    // Le violet de la palette (PlayerPaint), la couleur de l'utilisateur ; des articulations grises.
    private static readonly Color Paint = new Color32(143, 75, 255, 255);
    private static readonly Color Joints = new Color(0.28f, 0.30f, 0.33f);

    [MenuItem("Duel Arena/Essais/Construire l'essai des rendus du robot")]
    public static void Build()
    {
        ConfigureLowPoly();
        Material bodyLit = SourceMaterial("Alpha_Body_MAT");
        Material jointsLit = SourceMaterial("Alpha_Joints_MAT");

        Material currentBody = SaveMaterial("Actuel_Coque", Copy(bodyLit, Paint));
        // Une peinture mate : chaque facette se lit comme un plan, d'une seule teinte.
        Material lowPolyBody = SaveMaterial("LowPoly_Coque", Matte(Copy(bodyLit, Paint)));
        Material lowPolyJoints = SaveMaterial("LowPoly_Articulations", Matte(Copy(jointsLit, jointsLit.GetColor("_BaseColor"))));
        Material celBody = SaveMaterial("Cel_Coque", Cel(Paint));
        Material celJoints = SaveMaterial("Cel_Articulations", Cel(Joints));
        AnimatorController idle = IdleController();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildStage(new Vector3(0.9f, 1.55f, -5.2f));
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        var lowPoly = AssetDatabase.LoadAssetAtPath<GameObject>(LowPolyPath);
        PlaceRobot(source, "Actuel", "ACTUEL", new Vector3(-1.6f, 0f, 0f), idle, r => BodyAndJoints(r, currentBody, jointsLit));
        PlaceRobot(lowPoly, "Low poly", "LOW POLY PROPRE", new Vector3(0f, 0f, 0f), idle, r => BodyAndJoints(r, lowPolyBody, lowPolyJoints));
        PlaceRobot(source, "Cel-shading", "CEL-SHADING", new Vector3(1.6f, 0f, 0f), idle, r => BodyAndJoints(r, celBody, celJoints));

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Essai des rendus] Scène construite : {ScenePath}. Lancer le Play Mode pour voir les robots s'animer.");
    }

    [MenuItem("Duel Arena/Essais/Construire l'essai du robot CT-01")]
    public static void BuildCt01()
    {
        ConfigureRobot(Ct01Path);
        ConfigureRobot(Os02Path);
        Texture2D screenImage = RobotChassisBuilder.ConfigureScreenImage();
        var (lit, cel) = RobotMaterialSets("CT01");
        var (os02Lit, os02Cel) = RobotMaterialSets("OS02");
        // La visière est un écran, le même dans les deux rendus ; le robot mort l'allume et éteint ses lueurs.
        lit["Ecran"] = cel["Ecran"] = SaveMaterial("CT01_Ecran_Visage", RobotChassisBuilder.ScreenMaterial(screenImage, false));
        var dead = new Dictionary<string, Material>(lit)
        {
            ["Ecran"] = SaveMaterial("CT01_Ecran_Mort", RobotChassisBuilder.ScreenMaterial(screenImage, true)),
            ["Lueur"] = SaveMaterial("CT01_Lueur_Eteinte", RobotChassisBuilder.LitMaterial(new Color32(26, 20, 34, 255), 0.4f, 0f, 0f)),
        };
        Material currentBody = SaveMaterial("Actuel_Coque", Copy(SourceMaterial("Alpha_Body_MAT"), Paint));
        Material jointsLit = SourceMaterial("Alpha_Joints_MAT");
        AnimatorController idle = IdleController();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildStage(new Vector3(0f, 1.6f, -7.2f));   // six robots
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        var ct01 = AssetDatabase.LoadAssetAtPath<GameObject>(Ct01Path);
        var os02 = AssetDatabase.LoadAssetAtPath<GameObject>(Os02Path);
        PlaceRobot(source, "Actuel", "ACTUEL", new Vector3(-3.5f, 0f, 0f), idle, r => BodyAndJoints(r, currentBody, jointsLit));
        PlaceRobot(ct01, "CT-01 low poly", "CT-01 · LOW POLY", new Vector3(-2.1f, 0f, 0f), idle, r => ByName(r, lit));
        PlaceRobot(ct01, "CT-01 cel-shading", "CT-01 · CEL", new Vector3(-0.7f, 0f, 0f), idle, r => ByName(r, cel));
        PlaceRobot(ct01, "CT-01 mort", "CT-01 · MORT", new Vector3(0.7f, 0f, 0f), idle, r => ByName(r, dead));
        PlaceRobot(os02, "OS-02 low poly", "OS-02 · LOW POLY", new Vector3(2.1f, 0f, 0f), idle, r => ByName(r, os02Lit));
        PlaceRobot(os02, "OS-02 cel-shading", "OS-02 · CEL", new Vector3(3.5f, 0f, 0f), idle, r => ByName(r, os02Cel));

        EditorSceneManager.SaveScene(scene, Ct01ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Essai des rendus] Scène construite : {Ct01ScenePath}. Lancer le Play Mode pour voir les robots s'animer.");
    }

    /// <summary>Un robot de Blender (le CT-01, l'OS-02) : l'avatar humanoïde de son squelette (celui du Y Bot), ses
    /// normales (les facettes), ses matériaux nommés comme dans Blender, que la scène remplace ; la normale lissée du
    /// contour vient d'OutlineNormalsPostprocessor.</summary>
    private static void ConfigureRobot(string path)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new System.InvalidOperationException($"{path} introuvable : lancer d'abord son script de Blender avec --fbx.");
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        importer.SaveAndReimport();
    }

    /// <summary>Les matériaux de l'essai d'un robot (code : CT01, OS02), en low poly propre et en cel-shading, rangés
    /// par nom de matériau de Blender.</summary>
    private static (Dictionary<string, Material> lit, Dictionary<string, Material> cel) RobotMaterialSets(string code)
    {
        var lit = new Dictionary<string, Material>();
        var cel = new Dictionary<string, Material>();
        foreach (var m in RobotChassisBuilder.RobotMaterials)
        {
            lit[m.name] = SaveMaterial($"{code}_LowPoly_{m.name}", RobotChassisBuilder.LitMaterial(m.color, m.smoothness, m.metallic, m.glow));
            cel[m.name] = SaveMaterial($"{code}_Cel_{m.name}", Ct01Cel(m.color, m.metallic, m.glow));
        }
        return (lit, cel);
    }

    private static Material Ct01Cel(Color color, float metallic, float glow)
    {
        Material material = Cel(color);
        material.SetFloat("_ContourLisse", 1f);
        if (metallic > 0.5f) material.SetColor("_CouleurReflet", new Color(1f, 1f, 1f, 0.7f));
        if (glow > 0f) material.SetColor("_Emission", color * glow * 0.6f);
        return material;
    }

    private static Material[] BodyAndJoints(SkinnedMeshRenderer r, Material body, Material joints)
        => new[] { r.name.Contains("Joints") ? joints : body };

    /// <summary>Chaque emplacement reçoit le matériau de l'essai dont le nom commence comme le sien, après le préfixe de
    /// son robot (CT01_Peinture_violet : Peinture).</summary>
    private static Material[] ByName(SkinnedMeshRenderer r, Dictionary<string, Material> materials)
    {
        Material[] slots = r.sharedMaterials;
        for (int i = 0; i < slots.Length; i++)
        {
            Material source = slots[i];
            string name = source != null ? source.name.Substring(source.name.IndexOf('_') + 1) : "";
            var match = materials.FirstOrDefault(kv => source != null && name.StartsWith(kv.Key));
            if (match.Value != null) slots[i] = match.Value;
            else Debug.LogWarning($"[Essai des rendus] Pas de matériau d'essai pour « {source?.name} ».");
        }
        return slots;
    }

    /// <summary>La copie low poly : l'avatar humanoïde de son squelette (celui de Mixamo), sans matériaux ni
    /// animations, ses normales d'origine (une par face : les facettes).</summary>
    private static void ConfigureLowPoly()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(LowPolyPath);
        if (importer == null) throw new System.InvalidOperationException($"{LowPolyPath} introuvable : lancer d'abord robot_lowpoly.py.");
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport();
    }

    private static Material SourceMaterial(string name)
        => AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<Material>().First(m => m.name == name);

    private static Material Copy(Material source, Color color)
    {
        var material = new Material(source);
        material.SetColor("_BaseColor", color);
        return material;
    }

    private static Material Matte(Material material)
    {
        material.SetFloat("_Smoothness", 0.12f);
        return material;
    }

    private static Material Cel(Color color)
    {
        Shader shader = Shader.Find(CelShaderName);
        if (shader == null) throw new System.InvalidOperationException($"Shader {CelShaderName} introuvable.");
        var material = new Material(shader);
        material.SetColor("_BaseColor", color);
        return material;
    }

    private static Material SaveMaterial(string name, Material material) =>
        RobotChassisBuilder.SaveMaterial($"{Folder}/{name}.mat", material);

    /// <summary>Un Animator qui ne joue que l'attente à mains nues, en boucle (partagé par les deux scènes).</summary>
    private static AnimatorController IdleController()
    {
        string path = $"{Folder}/EssaiRendus_Attente.controller";
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (existing != null) return existing;
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(IdlePath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        controller.layers[0].stateMachine.AddState("Attente").motion = clip;
        return controller;
    }

    /// <summary>La lumière de l'arène (MultiTestScene : soleil à 50° de haut, ombres douces) et un décor sombre et
    /// terne, presque sans jaune (bible § 13) : un sol, un mur, deux blocs.</summary>
    private static void BuildStage(Vector3 cameraPosition)
    {
        var sun = new GameObject("Soleil (celui de l'arène)").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.957f, 0.839f);
        sun.intensity = 1f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(50f, 330f, 0f);

        Material floor = SaveMaterial("Decor_Sol", Plain(new Color(0.16f, 0.17f, 0.18f), 0.25f));
        Material wall = SaveMaterial("Decor_Mur", Plain(new Color(0.24f, 0.25f, 0.27f), 0.15f));
        Block("Sol", new Vector3(0f, -0.05f, 1f), new Vector3(14f, 0.1f, 10f), floor);
        Block("Mur", new Vector3(0f, 3f, 3.2f), new Vector3(16f, 6f, 0.3f), wall);   // assez haut pour la caméra reculée
        Block("Bloc gauche", new Vector3(-4.2f, 0.6f, 1.6f), new Vector3(1.2f, 1.2f, 1.2f), wall);
        Block("Bloc droit", new Vector3(4.4f, 0.9f, 1.2f), new Vector3(1.4f, 1.8f, 1.0f), wall);

        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.fieldOfView = 38f;
        camera.nearClipPlane = 0.05f;
        camera.transform.position = cameraPosition;
        camera.transform.LookAt(new Vector3(0f, 0.95f, 0f));
        camera.gameObject.AddComponent<AudioListener>();
    }

    private static Material Plain(Color color, float smoothness)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    private static void Block(string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.position = position;
        block.transform.localScale = size;
        block.GetComponent<Renderer>().sharedMaterial = material;
    }

    /// <summary>Une copie du robot, ses matériaux, l'Animator d'attente et son étiquette au sol.</summary>
    private static void PlaceRobot(GameObject model, string name, string label, Vector3 position, AnimatorController idle,
                                   System.Func<SkinnedMeshRenderer, Material[]> materials)
    {
        var robot = (GameObject)PrefabUtility.InstantiatePrefab(model);
        robot.name = name;
        robot.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));
        foreach (SkinnedMeshRenderer r in robot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            r.sharedMaterials = materials(r);
            r.updateWhenOffscreen = true;
        }
        Animator animator = robot.GetComponent<Animator>();
        if (animator == null) animator = robot.AddComponent<Animator>();
        animator.runtimeAnimatorController = idle;
        animator.applyRootMotion = false;

        var text = new GameObject($"Étiquette {name}").AddComponent<TextMesh>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
        text.text = label;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.characterSize = 0.016f;
        text.fontSize = 64;
        text.color = new Color(0.85f, 0.86f, 0.88f);
        text.transform.SetPositionAndRotation(position + new Vector3(0f, 0.02f, -0.75f), Quaternion.Euler(90f, 0f, 0f));
    }
}
