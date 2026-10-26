using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Monte les robots de Blender sur le préfab joueur (2026-10-26, demandés par l'utilisateur : le CT-01 remplace le
/// Y Bot, qui reste au choix, puis l'OS-02) : Duel Arena > Robot > Monter les robots sur le préfab joueur.
///
/// Chaque robot vient de Blender (Tools/Blender/robot_ct01.py, robot_os02.py, --skin vierge --fbx), bâti sur le
/// squelette du Y Bot : les mêmes os à la même place, mais exportés à une autre échelle (0,01). Ses sommets sont donc
/// remontés sur les os du « Model » du préfab : chacun pris là où il est au repos (le skinning de son FBX), puis lié au
/// même os du Y Bot, dont la matrice de liaison est recalculée. Le maillage obtenu (`<préfixe>_Corps_Monte.asset`) est
/// la pièce `<préfixe>` de « Model », à côté de celles du Y Bot, sur le même Animator ; RobotChassis allume celles du
/// châssis choisi, le CT-01 par défaut. On corrige le script de Blender, on réexporte et on relance ce menu, jamais le
/// maillage ni le préfab à la main.
///
/// Les matériaux du jeu sont ceux du rendu « low poly propre » de l'essai des rendus (URP Lit, sans usure : il faudrait
/// des textures cuites), une visière est l'écran du visage (DuelArena/EcranVisage, l'écran de la mort).
/// </summary>
public static class RobotChassisBuilder
{
    /// <summary>Un robot de Blender monté sur le préfab : son code (CT-01) et son préfixe, qui nomme sa pièce sous
    /// « Model » (RobotChassis la connaît), son maillage et ses matériaux (CT01).</summary>
    public readonly struct Robot
    {
        public readonly string code, prefix;

        public Robot(string code, string prefix)
        {
            this.code = code;
            this.prefix = prefix;
        }

        public string Folder => $"Assets/_ProjectArt/Robots/{code}";
        public string FbxPath => $"{Folder}/Robot {code}.fbx";
        public string MeshPath => $"{Folder}/{prefix}_Corps_Monte.asset";
        public string MaterialFolder => $"{Folder}/Materiaux";
    }

    /// <summary>Les robots que le jeu monte, chacun un châssis de RobotChassis.</summary>
    public static readonly Robot[] Robots = { new Robot("CT-01", "CT01"), new Robot("OS-02", "OS02") };

    public const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    public const string ScreenImagePath = "Assets/_ProjectArt/Textures/EcranMort.png";

    // Les matériaux des robots de Blender (commun_robots.materiaux), reconnus à leur nom après le préfixe de leur
    // robot (CT01_, OS02_) : couleur (sRGB), lissé, métal, lueur. La peinture est le violet de la palette, la couleur de
    // l'utilisateur ; les lueurs (yeux, cœur, fentes) la reprennent, saturée. En jeu, PlayerPaint les repeint de la
    // couleur de chaque joueur.
    internal static readonly (string name, Color32 color, float smoothness, float metallic, float glow)[] RobotMaterials =
    {
        ("Peinture", new Color32(143, 75, 255, 255), 0.35f, 0f, 0f),
        ("Blindage", new Color32(62, 66, 72, 255), 0.45f, 0.35f, 0f),
        ("Chassis", new Color32(34, 36, 40, 255), 0.35f, 0f, 0f),
        ("Metal", new Color32(112, 114, 118, 255), 0.55f, 1f, 0f),
        ("Chrome", new Color32(178, 182, 188, 255), 0.65f, 0.65f, 0f),
        ("Interieur", new Color32(16, 16, 17, 255), 0.2f, 0.3f, 0f),
        ("Caoutchouc", new Color32(24, 24, 24, 255), 0.08f, 0f, 0f),
        ("Ecran", new Color32(9, 11, 13, 255), 0.4f, 0f, 0f),
        ("Lueur", new Color32(120, 40, 255, 255), 0.4f, 0f, PlayerPaint.GlowIntensity),
        ("Lentille", new Color32(6, 9, 16, 255), 0.9f, 0f, 0f),
        ("Marquage_Blanc", new Color32(214, 210, 198, 255), 0.3f, 0f, 0f),
        ("Marquage_Noir", new Color32(20, 20, 21, 255), 0.3f, 0f, 0f),
        ("Appret", new Color32(118, 50, 38, 255), 0.2f, 0f, 0f),
        ("Soudure", new Color32(92, 88, 84, 255), 0.4f, 1f, 0f),
    };

    [MenuItem("Duel Arena/Robot/Monter les robots sur le préfab joueur")]
    public static void Build()
    {
        var sources = new (Robot robot, GameObject fbx, SkinnedMeshRenderer source, Material[] materials)[Robots.Length];
        for (int r = 0; r < Robots.Length; r++)
        {
            Robot robot = Robots[r];
            ConfigureFbx(robot.FbxPath);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(robot.FbxPath);
            SkinnedMeshRenderer source = fbx != null ? fbx.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (source == null) throw new System.InvalidOperationException($"{robot.FbxPath} : pas de maillage lié à un squelette.");
            sources[r] = (robot, fbx, source, GameMaterials(source.sharedMaterials, robot));
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Transform model = root.transform.Find("Model");
            if (model == null) throw new System.InvalidOperationException("Le préfab joueur n'a pas d'enfant « Model ».");
            var paint = root.GetComponent<PlayerPaint>();
            var serialized = new SerializedObject(paint);
            SerializedProperty painted = serialized.FindProperty("paintedRenderers");
            foreach ((Robot robot, GameObject fbx, SkinnedMeshRenderer source, Material[] materials) in sources)
            {
                Mesh mounted = Mount(source, fbx.transform, model, out Transform[] bones);
                mounted.name = $"{robot.prefix}_Corps_Monte";
                SkinnedMeshRenderer part = Part(model, SaveMesh(mounted, robot.MeshPath), bones, materials, robot.prefix);
                bool listed = false;
                for (int i = 0; i < painted.arraySize; i++) listed |= painted.GetArrayElementAtIndex(i).objectReferenceValue == part;
                if (listed) continue;
                painted.InsertArrayElementAtIndex(painted.arraySize);
                painted.GetArrayElementAtIndex(painted.arraySize - 1).objectReferenceValue = part;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (root.GetComponent<PlayerChassis>() == null) root.AddComponent<PlayerChassis>();
            RobotChassis.Apply(model, ChassisId.CT01);
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Robot] {Robots.Length} robots montés sur le préfab joueur.");
    }

    /// <summary>
    /// Le maillage de `source` (un robot de son FBX, dont la racine est `sourceRoot`) remonté sur les os de `model` :
    /// chaque sommet à sa place au repos dans le repère de `model` (les deux racines coïncident : mêmes os, même pose),
    /// lié aux os de `model` du même nom (`bones`, dans l'ordre des os de `source` : les poids ne changent pas), avec
    /// leurs matrices de liaison. La main gauche du préfab est repliée (le gant du taser) : on la déplie le temps du
    /// calcul.
    /// </summary>
    public static Mesh Mount(SkinnedMeshRenderer source, Transform sourceRoot, Transform model, out Transform[] bones)
    {
        Mesh mesh = source.sharedMesh;
        Transform[] sourceBones = source.bones;
        Matrix4x4[] sourceBindposes = mesh.bindposes;
        var skin = new Matrix4x4[sourceBones.Length];
        for (int b = 0; b < sourceBones.Length; b++)
            skin[b] = sourceRoot.worldToLocalMatrix * sourceBones[b].localToWorldMatrix * sourceBindposes[b];

        var byName = new Dictionary<string, Transform>();
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
        {
            if (!byName.ContainsKey(t.name)) byName.Add(t.name, t);
        }
        bones = new Transform[sourceBones.Length];
        for (int b = 0; b < sourceBones.Length; b++)
        {
            if (!byName.TryGetValue(sourceBones[b].name, out bones[b]))
                throw new System.InvalidOperationException($"Le squelette du préfab n'a pas l'os « {sourceBones[b].name} ».");
        }

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector4[] tangents = mesh.tangents;
        BoneWeight[] weights = mesh.boneWeights;
        var mountedVertices = new Vector3[vertices.Length];
        var mountedNormals = new Vector3[normals.Length];
        var mountedTangents = new Vector4[tangents.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Matrix4x4 m = Blend(skin, weights[i]);
            mountedVertices[i] = m.MultiplyPoint3x4(vertices[i]);
            if (i < normals.Length) mountedNormals[i] = m.inverse.transpose.MultiplyVector(normals[i]).normalized;
            if (i < tangents.Length)
            {
                Vector3 t = m.MultiplyVector(tangents[i]).normalized;
                mountedTangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
            }
        }

        Transform leftHand = byName.TryGetValue("mixamorig:LeftHand", out Transform hand) ? hand : null;
        Vector3 handScale = leftHand != null ? leftHand.localScale : Vector3.one;
        if (leftHand != null) leftHand.localScale = Vector3.one;
        var bindposes = new Matrix4x4[bones.Length];
        for (int b = 0; b < bones.Length; b++) bindposes[b] = bones[b].worldToLocalMatrix * model.localToWorldMatrix;
        if (leftHand != null) leftHand.localScale = handScale;

        var mounted = new Mesh();
        mounted.indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mounted.vertices = mountedVertices;
        if (normals.Length == vertices.Length) mounted.normals = mountedNormals;
        if (tangents.Length == vertices.Length) mounted.tangents = mountedTangents;
        var uvs = new List<Vector4>();
        for (int channel = 0; channel < 8; channel++)
        {
            mesh.GetUVs(channel, uvs);
            if (uvs.Count == vertices.Length) mounted.SetUVs(channel, uvs);
        }
        Color32[] colors = mesh.colors32;
        if (colors.Length == vertices.Length) mounted.colors32 = colors;
        mounted.subMeshCount = mesh.subMeshCount;
        for (int s = 0; s < mesh.subMeshCount; s++) mounted.SetTriangles(mesh.GetTriangles(s), s);
        mounted.boneWeights = weights;
        mounted.bindposes = bindposes;
        mounted.RecalculateBounds();
        return mounted;
    }

    // La matrice d'un sommet lié à plusieurs os (nos robots n'en ont qu'un : des pièces rigides).
    private static Matrix4x4 Blend(Matrix4x4[] skin, BoneWeight w)
    {
        if (w.weight0 >= 0.9999f) return skin[w.boneIndex0];
        Matrix4x4 m = Scaled(skin[w.boneIndex0], w.weight0);
        m = Add(m, Scaled(skin[w.boneIndex1], w.weight1));
        m = Add(m, Scaled(skin[w.boneIndex2], w.weight2));
        return Add(m, Scaled(skin[w.boneIndex3], w.weight3));
    }

    private static Matrix4x4 Scaled(Matrix4x4 m, float k)
    {
        for (int i = 0; i < 16; i++) m[i] *= k;
        return m;
    }

    private static Matrix4x4 Add(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++) a[i] += b[i];
        return a;
    }

    private static Mesh SaveMesh(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        existing.name = mesh.name;
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    /// <summary>La pièce `name` sous `model`, posée sur ses os, à côté de celles du Y Bot (dont elle reprend les
    /// réglages de rendu et les bornes, plus les siennes).</summary>
    private static SkinnedMeshRenderer Part(Transform model, Mesh mesh, Transform[] bones, Material[] materials, string name)
    {
        Transform part = model.Find(name);
        if (part == null)
        {
            part = new GameObject(name).transform;
            part.SetParent(model, false);
        }
        part.localPosition = Vector3.zero;
        part.localRotation = Quaternion.identity;
        part.localScale = Vector3.one;
        SkinnedMeshRenderer like = model.Find("Alpha_Surface")?.GetComponent<SkinnedMeshRenderer>();
        part.gameObject.layer = like != null ? like.gameObject.layer : model.gameObject.layer;

        var smr = part.GetComponent<SkinnedMeshRenderer>();
        if (smr == null) smr = part.gameObject.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.bones = bones;
        Transform hips = System.Array.Find(bones, b => b.name == "mixamorig:Hips");
        smr.rootBone = hips;
        smr.sharedMaterials = materials;
        smr.updateWhenOffscreen = false;
        smr.shadowCastingMode = ShadowCastingMode.On;
        smr.receiveShadows = true;
        if (like != null)
        {
            smr.lightProbeUsage = like.lightProbeUsage;
            smr.reflectionProbeUsage = like.reflectionProbeUsage;
            smr.skinnedMotionVectors = like.skinnedMotionVectors;
            smr.quality = like.quality;
        }

        // Les bornes, dans le repère du bassin : celles du Y Bot (qui couvrent ses gestes), et le robot au repos.
        Bounds bounds = like != null ? like.localBounds : new Bounds(Vector3.zero, Vector3.one);
        Matrix4x4 toHips = hips.worldToLocalMatrix * model.localToWorldMatrix;
        Bounds rest = mesh.bounds;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? rest.min.x : rest.max.x, (i & 2) == 0 ? rest.min.y : rest.max.y,
                                     (i & 4) == 0 ? rest.min.z : rest.max.z);
            bounds.Encapsulate(toHips.MultiplyPoint3x4(corner));
        }
        smr.localBounds = bounds;
        return smr;
    }

    /// <summary>Un robot tel que Blender l'exporte : sans avatar (il prend les os du préfab), ses normales (les
    /// facettes), ses matériaux nommés comme dans Blender, que GameMaterials remplace.</summary>
    private static void ConfigureFbx(string path)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (importer == null)
            throw new System.InvalidOperationException($"{path} introuvable : lancer le script du robot dans Blender, -- --skin vierge --fbx \"{path}\".");
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.importAnimation = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.optimizeGameObjects = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        importer.SaveAndReimport();
    }

    /// <summary>Les matériaux du jeu, un par emplacement du FBX, reconnus au nom de son matériau de Blender après
    /// le préfixe du robot (CT01_Peinture_violet : Peinture) ; une visière (Ecran) est l'écran du visage.</summary>
    private static Material[] GameMaterials(Material[] slots, Robot robot)
    {
        if (!AssetDatabase.IsValidFolder(robot.MaterialFolder)) AssetDatabase.CreateFolder(robot.Folder, "Materiaux");
        var result = new Material[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            string name = slots[i] != null ? slots[i].name.Substring(slots[i].name.IndexOf('_') + 1) : "";
            int found = System.Array.FindIndex(RobotMaterials, m => name.StartsWith(m.name));
            if (found < 0) throw new System.InvalidOperationException($"Pas de matériau du jeu pour « {slots[i]?.name} ».");
            var m = RobotMaterials[found];
            Material material = m.name == "Ecran" ? ScreenMaterial(ConfigureScreenImage(), false)
                                                  : LitMaterial(m.color, m.smoothness, m.metallic, m.glow);
            result[i] = SaveMaterial($"{robot.MaterialFolder}/{robot.prefix}_{m.name}.mat", material);
        }
        return result;
    }

    // Le fond d'une lueur, comme dans Blender (commun_robots.lueur) : éteinte (l'épave), elle est sombre, pas colorée.
    private static readonly Color GlowBase = new Color32(26, 20, 34, 255);

    /// <summary>Un matériau URP Lit de robot ; une lueur (glow &gt; 0) émet sa couleur, multipliée, sur un fond sombre.</summary>
    internal static Material LitMaterial(Color color, float smoothness, float metallic, float glow)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", glow > 0f ? GlowBase : color);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        if (glow > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * glow);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return material;
    }

    /// <summary>L'écran du visage (DuelArena/EcranVisage) : éteint, ou montrant l'écran de la mort.</summary>
    internal static Material ScreenMaterial(Texture2D image, bool dead)
    {
        var material = new Material(Shader.Find(RobotFaceScreen.ScreenShader));
        material.SetTexture("_Image", image);
        material.SetFloat("_Mort", dead ? 1f : 0f);
        return material;
    }

    /// <summary>Les masques de l'écran de la mort (Tools/Blender/ecran_mort.py) : des masques, pas des couleurs.</summary>
    internal static Texture2D ConfigureScreenImage()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(ScreenImagePath);
        if (importer == null) throw new System.InvalidOperationException($"{ScreenImagePath} introuvable : lancer d'abord ecran_mort.py.");
        if (importer.sRGBTexture || importer.wrapMode != TextureWrapMode.Clamp || importer.alphaSource != TextureImporterAlphaSource.None)
        {
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ScreenImagePath);
    }

    /// <summary>`material` enregistré à `path`, ou recopié dans le matériau qui s'y trouve (les références restent
    /// bonnes). Validé comme URP le fait à la création d'un matériau (étiquette RenderType, passes, `_Color`) :
    /// la recopie perdait cette initialisation, et chaque montage réécrivait autrement les fichiers.</summary>
    internal static Material SaveMaterial(string path, Material material)
    {
        material.name = System.IO.Path.GetFileNameWithoutExtension(path);
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing == null)
        {
            Validate(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        existing.shader = material.shader;
        existing.CopyPropertiesFromMaterial(material);
        Validate(existing);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(material);
        return existing;
    }

    // Le ShaderGUI du shader (LitShader pour URP Lit) met le matériau dans l'état que montre l'inspecteur.
    private static void Validate(Material material)
    {
        var editor = (MaterialEditor)UnityEditor.Editor.CreateEditor(material);
        try { editor.customShaderGUI?.ValidateMaterial(material); }
        finally { Object.DestroyImmediate(editor); }
    }
}
