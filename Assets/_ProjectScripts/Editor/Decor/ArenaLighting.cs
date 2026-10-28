using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// La lumière de l'arène habillée (2026-10-27, « projecteurs, pénombre », choisi par l'utilisateur) : une salle
/// souterraine, sans soleil.
///
/// - **La rampe générale** : une lumière directionnelle, presque verticale, froide, qui porte les ombres. C'est la
///   somme des rampes de projecteurs au-dessus de l'arène : le sol reste éclairé de façon égale (personne ne campe
///   dans le noir), les robots et les murs portent leur ombre. Elle n'éclaire que le calque de rendu de l'arène
///   (le premier) : la salle et la tribune, sur le second, restent dans la pénombre.
/// - **Les rampes de projecteurs**, pendues à la charpente : leurs verres luisent, un projecteur sans ombre sous
///   chacune pose une flaque de lumière, et **un faisceau** sort de chaque verre dans la brume (un cône ajouté, qui
///   s'éteint à BeamBottom, au-dessus des robots : DuelArena/FaisceauProjecteur).
/// - **La tribune** : une lampe tiède sur un pan sur deux du mur du fond, et un liseré qui luit sous le bord de la
///   passerelle, sur tout son tour ; **les murs de la salle**, des lampes qui les éclairent en rasant.
/// - Une ambiance sombre et bleutée, une brume légère, une sonde de reflets cuite, et le post-traitement de l'arène
///   (Arene_PostTraitement.asset).
/// </summary>
public static class ArenaLighting
{
    public const string RootName = "Éclairage (généré)";
    public const string ProfilePath = "Assets/_ProjectArena/Decor/Arene_PostTraitement.asset";

    public static readonly Color Froid = new Color(0.80f, 0.88f, 1f);
    public static readonly Color Tiede = new Color(1f, 0.92f, 0.83f);

    /// <summary>Le bas des faisceaux : au-dessus des robots, même d'un spectateur debout sur la passerelle (4,5 m + 1,8 m)
    /// s'il y en avait au-dessus d'elle (il n'y en a qu'au-dessus de l'arène).</summary>
    public const float BeamBottom = 6.4f;
    public const float BeamRadius = 0.14f;
    public const float BeamHalfAngle = 11f;
    public const string BeamMaterialPath = DecorMaterials.Folder + "/Decor_FaisceauProjecteur.mat";

    public static void Build(Scene scene, ArenaDecorBuilder.HallGeometry hall, Dictionary<string, Material> materials,
                             DecorMeshes.Bank bank, StringBuilder report)
    {
        foreach (GameObject old in scene.GetRootGameObjects())
        {
            if (old.name == RootName) Undo.DestroyObjectImmediate(old);
        }
        var root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        Undo.RegisterCreatedObjectUndo(root, "Éclairer l'arène");

        // L'ancien soleil du blocking : éteint (pas supprimé).
        int suns = 0;
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
        {
            if (light.gameObject.scene != scene || light.type != LightType.Directional || light.transform.IsChildOf(root.transform)) continue;
            Undo.RecordObject(light.gameObject, "Éclairer l'arène");
            light.gameObject.SetActive(false);
            suns++;
        }

        // La rampe générale.
        Light key = NewLight(root.transform, "Rampe générale", LightType.Directional);
        key.transform.rotation = Quaternion.Euler(64f, 38f, 0f);
        key.color = Froid;
        key.intensity = 0.9f;
        key.lightmapBakeType = LightmapBakeType.Mixed;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.92f;
        SetLayers(key, ArenaDecorBuilder.ArenaLayerMask);
        RenderSettings.sun = key;

        // Les rampes de projecteurs : six autour de la couronne, douze sur l'anneau du milieu. Le modèle de Blender
        // (PR1) s'il est importé, sinon des boîtes ; leurs suspentes jusqu'à la charpente.
        var rigs = new DecorMeshes.Builder();
        var rods = new DecorMeshes.Builder();
        GameObject rigPrefab = DecorImporter.Prefab("PR1");
        var rigRoot = new GameObject("Rampes de projecteurs").transform;
        rigRoot.SetParent(root.transform, false);
        int spots = 0;
        var lenses = new List<Vector3>();
        Vector3[] ring = ArenaDecorBuilder.HallRing, middle = ArenaDecorBuilder.HallMiddle;
        foreach ((Vector3 anchor, float height, float angle, float intensity) in RigPlaces(ring, middle))
        {
            Vector3 radial = anchor - new Vector3(hall.center.x, anchor.y, hall.center.y);
            if (rigPrefab != null) PlaceRig(rigRoot, rods, rigPrefab, anchor, height, radial, lenses);
            else AddRig(rigs, anchor, height, radial, lenses);
            AddSpot(root.transform, new Vector3(anchor.x, height - 0.55f, anchor.z), angle, 30f, intensity);
            spots++;
        }
        if (rigs.VertexCount > 0)
        {
            Mesh rigMesh = bank.Add(rigs.ToMesh("Rampes_Projecteurs"));
            GameObject rigObject = ArenaDecorBuilder.Part(rigRoot, "Rampes (boîtes)", rigMesh, materials["Decor_Charpente"], ArenaDecorBuilder.HallLayerMask);
            var rigRenderer = rigObject.GetComponent<MeshRenderer>();
            rigRenderer.sharedMaterials = new[] { materials["Decor_Charpente"], materials["Decor_LampeFroide"] };
            rigRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        if (rods.VertexCount > 0)
        {
            GameObject rodObject = ArenaDecorBuilder.Part(rigRoot, "Suspentes", bank.Add(rods.ToMesh("Suspentes")), materials["Decor_Charpente"], ArenaDecorBuilder.HallLayerMask);
            rodObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        int beams = AddBeams(rigRoot, lenses, hall, bank);

        // La tribune : une lampe tiède sur un pan sur deux du mur du fond, et son applique.
        var sconces = new DecorMeshes.Builder();
        int lamps = 0;
        Vector2[] outer = DecorMeshes.Offset(hall.rail, hall.walkwayWidth);
        for (int i = 0; i < outer.Length; i += 2)
        {
            Vector2 a = outer[i], c = outer[(i + 1) % outer.Length];
            if (Vector2.Distance(a, c) < 6f) continue;
            Vector2 o = DecorMeshes.OutwardOf(outer, i);
            Vector2 mid = (a + c) * 0.5f - o * 0.32f;
            var position = new Vector3(mid.x, hall.floorTop + 2.25f, mid.y);
            Light lamp = NewLight(root.transform, "Lampe de tribune", LightType.Point);
            lamp.transform.position = position - new Vector3(o.x, 0f, o.y) * 0.25f + Vector3.down * 0.15f;
            lamp.color = Tiede;
            lamp.intensity = 5.5f;
            lamp.lightmapBakeType = LightmapBakeType.Mixed;
            lamp.range = 8.5f;
            lamp.shadows = LightShadows.None;
            SetLayers(lamp, (ArenaDecorBuilder.HallLayerMask | ArenaDecorBuilder.ArenaLayerMask));
            AddSconce(sconces, position, new Vector3(-o.x, 0f, -o.y));
            lamps++;
        }

        // Les murs de la salle : des lampes rasantes au-dessus de la tribune, qui les sortent du noir.
        int washers = 0;
        for (int i = 0; i < outer.Length; i++)
        {
            Vector2 a = outer[i], c = outer[(i + 1) % outer.Length];
            if (Vector2.Distance(a, c) < 6f) continue;
            Vector2 o = DecorMeshes.OutwardOf(outer, i);
            Vector2 mid = (a + c) * 0.5f - o * 0.5f;
            var position = new Vector3(mid.x, ArenaDecorBuilder.CeilingHeight - 1.6f, mid.y);
            Light washer = NewLight(root.transform, "Lampe rasante", LightType.Spot);
            washer.transform.position = position;
            washer.transform.rotation = Quaternion.LookRotation((Vector3.down * 2f + new Vector3(o.x, 0f, o.y)).normalized, Vector3.up);
            washer.spotAngle = 95f;
            washer.innerSpotAngle = 40f;
            washer.range = 14f;
            washer.intensity = 50f;
            washer.color = Froid;
            washer.shadows = LightShadows.None;
            washer.lightmapBakeType = LightmapBakeType.Mixed;
            SetLayers(washer, ArenaDecorBuilder.HallLayerMask);
            AddSconce(sconces, position + Vector3.up * 0.2f, new Vector3(-o.x, 0f, -o.y));
            washers++;
        }
        Mesh sconceMesh = bank.Add(sconces.ToMesh("Appliques"));
        GameObject sconceObject = ArenaDecorBuilder.Part(root.transform, "Appliques", sconceMesh, materials["Decor_Charpente"], ArenaDecorBuilder.HallLayerMask);
        sconceObject.GetComponent<MeshRenderer>().sharedMaterials = new[] { materials["Decor_Charpente"], materials["Decor_LampeTiede"] };
        AddRailStrip(root.transform, hall, materials["Decor_LedTribune"], bank);

        // Le haut de la salle : ce que le sol éclairé renverrait vers le plafond et la charpente (en attendant une
        // lumière cuite), sur le seul calque de la salle.
        int fills = 0;
        foreach (Vector2 offset in new[] { Vector2.zero, new Vector2(-18f, -16f), new Vector2(18f, -16f), new Vector2(-18f, 16f), new Vector2(18f, 16f), new Vector2(0f, -30f), new Vector2(0f, 30f) })
        {
            Light fill = NewLight(root.transform, "Reflet du sol", LightType.Point);
            fill.transform.position = new Vector3(hall.center.x + offset.x, 9.5f, hall.center.y + offset.y);
            fill.range = 22f;
            fill.intensity = 2.5f;
            fill.color = Froid;
            fill.shadows = LightShadows.None;
            SetLayers(fill, ArenaDecorBuilder.HallLayerMask);
            fills++;
        }

        // L'ambiance : sombre et bleutée, sans ciel ; une brume légère ; les reflets d'une sonde cuite.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.085f, 0.095f, 0.115f);
        RenderSettings.ambientEquatorColor = new Color(0.06f, 0.065f, 0.075f);
        RenderSettings.ambientGroundColor = new Color(0.045f, 0.045f, 0.05f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.0065f;
        RenderSettings.fogColor = new Color(0.07f, 0.078f, 0.092f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;

        var probeObject = new GameObject("Sonde de reflets");
        probeObject.transform.SetParent(root.transform, false);
        probeObject.transform.position = new Vector3(hall.center.x, 2.5f, hall.center.y);
        var probe = probeObject.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Baked;
        probe.boxProjection = true;
        probe.size = new Vector3(90f, ArenaDecorBuilder.CeilingHeight + 2f, 100f);
        probe.center = new Vector3(0f, ArenaDecorBuilder.CeilingHeight * 0.5f - 2.5f, 0f);
        probe.resolution = 128;
        probe.importance = 1;

        // Le post-traitement.
        var volumeObject = new GameObject("Post-traitement");
        volumeObject.transform.SetParent(root.transform, false);
        var volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = PostProfile();

        // La caméra de secours de l'arène (avant la session) : sous le plafond, de la tribune sud vers le milieu.
        foreach (ArenaFallbackCamera fallback in Object.FindObjectsByType<ArenaFallbackCamera>(FindObjectsInactive.Include))
        {
            if (fallback.gameObject.scene != scene) continue;
            Undo.RecordObject(fallback.transform, "Éclairer l'arène");
            fallback.transform.position = new Vector3(hall.center.x, 9f, hall.center.y - 40f);
            fallback.transform.LookAt(new Vector3(hall.center.x, 0f, hall.center.y));
        }

        int probes = BuildLightProbes(root.transform, hall);
        Lightmapping.lightingSettings = LightingSettingsAsset(scene);
        Lightmapping.BakeReflectionProbe(probe, ReflectionPath(scene));
        report.AppendLine($"Lumière : {suns} soleil éteint, la rampe générale, {spots} projecteurs et {beams} faisceaux, {lamps} lampes de tribune et son liseré, {washers} lampes rasantes, {fills} reflets du sol, {probes} sondes de lumière ; brume, sonde de reflets, post-traitement. Lumière indirecte : Duel Arena > Arène > Cuire la lumière.");
    }

    /// <summary>Les sondes de lumière : de quoi éclairer les robots (qui bougent) par la lumière cuite du décor. Une
    /// grille de 4 m sur l'arène à trois hauteurs, et la passerelle ; aucune dans un bloc (elle y serait noire).</summary>
    private static int BuildLightProbes(Transform root, ArenaDecorBuilder.HallGeometry hall)
    {
        Physics.SyncTransforms();
        int world = LayerMask.GetMask("Default");
        var positions = new List<Vector3>();
        Rect bounds = BoundsOf(hall.rail);
        for (float x = bounds.xMin; x <= bounds.xMax + 0.01f; x += 4f)
        {
            for (float z = bounds.yMin; z <= bounds.yMax + 0.01f; z += 4f)
            {
                foreach (float y in new[] { 0.6f, 1.9f, 3.6f })
                {
                    var p = new Vector3(x, y, z);
                    if (!Physics.CheckSphere(p, 0.15f, world, QueryTriggerInteraction.Ignore)) positions.Add(p);
                }
            }
        }
        Vector2[] center = DecorMeshes.Offset(hall.rail, hall.walkwayWidth * 0.5f);
        foreach (Vector2 c in center)
        {
            foreach (float y in new[] { hall.floorTop + 0.6f, hall.floorTop + 1.9f })
            {
                var p = new Vector3(c.x, y, c.y);
                if (!Physics.CheckSphere(p, 0.15f, world, QueryTriggerInteraction.Ignore)) positions.Add(p);
            }
        }
        var go = new GameObject("Sondes de lumière");
        go.transform.SetParent(root, false);
        go.AddComponent<LightProbeGroup>().probePositions = positions.ToArray();
        return positions.Count;
    }

    private static Rect BoundsOf(Vector2[] points)
    {
        Vector2 min = points[0], max = points[0];
        foreach (Vector2 p in points)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>Les réglages de la cuisson de la scène (un asset à côté d'elle) : la lumière indirecte seulement (les
    /// lumières restent en temps réel pour l'éclairage direct et les ombres), le lightmapper du GPU, 3 texels par mètre.</summary>
    internal static LightingSettings LightingSettingsAsset(Scene scene)
    {
        string folder = string.IsNullOrEmpty(scene.path) ? "Assets" : System.IO.Path.GetDirectoryName(scene.path).Replace('\\', '/');
        string path = $"{folder}/{scene.name}_Lumiere.lighting";
        var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
        if (settings == null)
        {
            settings = new LightingSettings { name = scene.name + "_Lumiere" };
            AssetDatabase.CreateAsset(settings, path);
        }
        settings.bakedGI = true;
        settings.realtimeGI = false;
        settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
        settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        settings.lightmapResolution = 3f;
        settings.lightmapPadding = 4;
        settings.lightmapMaxSize = 2048;
        settings.directionalityMode = LightmapsMode.NonDirectional;
        settings.ao = true;
        settings.aoMaxDistance = 1.2f;
        settings.aoExponentIndirect = 1.2f;
        settings.maxBounces = 3;
        settings.indirectScale = 1.15f;
        settings.directSampleCount = 32;
        settings.indirectSampleCount = 256;
        settings.environmentSampleCount = 128;
        settings.lightmapCompression = LightmapCompression.NormalQuality;
        settings.filteringMode = LightingSettings.FilterMode.Auto;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return settings;
    }

    /// <summary>Là où la cuisson de la lumière range la sonde de reflets (le dossier de la scène) : un seul fichier, que
    /// l'habillage et la cuisson réécrivent tous les deux.</summary>
    private static string ReflectionPath(Scene scene)
    {
        string folder = string.IsNullOrEmpty(scene.path) ? "Assets" : System.IO.Path.GetDirectoryName(scene.path).Replace('\\', '/');
        string dir = $"{folder}/{scene.name}";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(folder, scene.name);
        return $"{dir}/ReflectionProbe-0.exr";
    }

    internal static Light NewLight(Transform parent, string name, LightType type)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var light = go.AddComponent<Light>();
        light.type = type;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        if (!go.TryGetComponent(out UniversalAdditionalLightData _)) go.AddComponent<UniversalAdditionalLightData>();
        return light;
    }

    /// <summary>Les calques de rendu qu'une lumière éclaire. URP les lit dans sa UniversalAdditionalLightData (pas dans
    /// Light.renderingLayerMask, qu'il réécrit pour les ombres) : une lumière réglée seulement sur la seconde n'éclairait
    /// rien d'autre que le calque par défaut.</summary>
    internal static void SetLayers(Light light, uint mask)
    {
        var data = light.GetComponent<UniversalAdditionalLightData>();
        data.renderingLayers = mask;
        light.renderingLayerMask = (int)mask;
    }

    internal static void AddSpot(Transform parent, Vector3 position, float angle, float range, float intensity)
    {
        Light spot = NewLight(parent, "Projecteur", LightType.Spot);
        spot.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.down, Vector3.forward));
        spot.spotAngle = angle;
        spot.innerSpotAngle = angle * 0.3f;
        spot.range = range;
        spot.intensity = intensity;
        spot.color = Froid;
        spot.shadows = LightShadows.None;
        spot.lightmapBakeType = LightmapBakeType.Mixed;
        SetLayers(spot, (ArenaDecorBuilder.HallLayerMask | ArenaDecorBuilder.ArenaLayerMask));
    }

    /// <summary>Où pendent les rampes : une sur deux autour de la couronne, à 12,4 m ; toutes celles de l'anneau du milieu,
    /// à 11,6 m. Rend (point d'accroche sur la charpente, hauteur de la rampe, angle et intensité de son projecteur).
    /// Des cônes de 84° : chaque flaque se voit, et elles se recouvrent assez pour que le sol n'ait pas de coin sombre
    /// (la rampe générale en porte le fond).</summary>
    private static IEnumerable<(Vector3, float, float, float)> RigPlaces(Vector3[] ring, Vector3[] middle)
    {
        for (int i = 0; i < ring.Length; i += 2) yield return (ring[i], 12.4f, 84f, 135f);
        foreach (Vector3 m in middle) yield return (m + Vector3.down * 0.9f, 11.6f, 82f, 125f);
    }

    /// <summary>La rampe de Blender (PR1), tangente à la couronne, et ses deux suspentes jusqu'au point d'accroche.</summary>
    internal static void PlaceRig(Transform parent, DecorMeshes.Builder rods, GameObject prefab, Vector3 anchor, float height, Vector3 radial,
                                 List<Vector3> lenses)
    {
        radial.y = 0f;
        Vector3 along = radial.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, radial.normalized) : Vector3.right;
        Vector3 forward = Vector3.Cross(along, Vector3.up);
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        // Le long de la rampe est son X : son Z local est donc le rayon (perpendiculaire à la rampe).
        rig.transform.SetPositionAndRotation(new Vector3(anchor.x, height, anchor.z), Quaternion.LookRotation(forward, Vector3.up));
        foreach (MeshRenderer r in rig.GetComponentsInChildren<MeshRenderer>(true))
        {
            r.renderingLayerMask = ArenaDecorBuilder.HallLayerMask;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
        AddLenses(rig.transform, lenses);
        foreach (float s in new[] { -1.3f, 1.3f })
        {
            Vector3 bottom = new Vector3(anchor.x, height, anchor.z) + along * s;
            Vector3 top = new Vector3(bottom.x, anchor.y - 0.3f, bottom.z);
            if (top.y - bottom.y < 0.05f) continue;
            DecorMeshes.OrientedBox(rods, (top + bottom) * 0.5f, along, Vector3.up, forward, new Vector3(0.02f, (top.y - bottom.y) * 0.5f, 0.02f));
        }
    }

    /// <summary>Une rampe de projecteurs (en attendant son modèle de Blender) : une barre de 3,2 m pendue à la charpente
    /// par deux tiges, trois boîtiers dessous, leurs faces qui luisent (sous-maillage 1).</summary>
    private static void AddRig(DecorMeshes.Builder b, Vector3 anchor, float height, Vector3 radial, List<Vector3> lenses)
    {
        radial.y = 0f;
        Vector3 along = radial.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, radial.normalized) : Vector3.right;
        Vector3 forward = Vector3.Cross(along, Vector3.up);
        var center = new Vector3(anchor.x, height, anchor.z);
        DecorMeshes.OrientedBox(b, center, along, Vector3.up, forward, new Vector3(1.6f, 0.08f, 0.08f));
        foreach (float s in new[] { -1.3f, 1.3f })
        {
            Vector3 top = new Vector3(anchor.x, anchor.y - 0.45f, anchor.z) + along * s;
            Vector3 bottom = center + along * s;
            DecorMeshes.OrientedBox(b, (top + bottom) * 0.5f, along, Vector3.up, forward, new Vector3(0.025f, (top.y - bottom.y) * 0.5f, 0.025f));
        }
        foreach (float s in new[] { -1.05f, 0f, 1.05f })
        {
            Vector3 housing = center + along * s + Vector3.down * 0.26f;
            DecorMeshes.OrientedBox(b, housing, along, Vector3.up, forward, new Vector3(0.32f, 0.17f, 0.24f));
            Vector3 face = housing + Vector3.down * 0.175f;
            b.Quad(face + along * -0.28f + forward * -0.2f, face + along * 0.28f + forward * -0.2f,
                   face + along * 0.28f + forward * 0.2f, face + along * -0.28f + forward * 0.2f, Vector3.down, 0f, 1);
            lenses.Add(face);
        }
    }

    /// <summary>Le milieu de chaque verre d'une rampe de Blender (sa pièce « Verres ») : ses sommets groupés le long de
    /// la rampe (un verre fait 30 cm de long, deux verres sont à 50 cm l'un de l'autre).</summary>
    private static void AddLenses(Transform rig, List<Vector3> lenses)
    {
        foreach (MeshFilter filter in rig.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.name.EndsWith("Verres") || filter.sharedMesh == null) continue;
            var local = new List<Vector3>();
            foreach (Vector3 v in filter.sharedMesh.vertices) local.Add(rig.InverseTransformPoint(filter.transform.TransformPoint(v)));
            local.Sort((a, b) => a.x.CompareTo(b.x));
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < local.Count; i++)
            {
                sum += local[i];
                count++;
                if (i < local.Count - 1 && local[i + 1].x - local[i].x < 0.35f) continue;
                lenses.Add(rig.TransformPoint(sum / count));
                sum = Vector3.zero;
                count = 0;
            }
        }
    }

    /// <summary>Les faisceaux : un cône ouvert sous chaque verre au-dessus de l'arène, jusqu'à BeamBottom, dans un seul
    /// maillage (normales radiales, uv.y de 0 au verre à 1 au bout : DuelArena/FaisceauProjecteur). Ni ombre, ni sonde,
    /// ni lumière cuite (il n'est pas statique : la cuisson le prendrait pour un écran).</summary>
    private static int AddBeams(Transform parent, List<Vector3> lenses, ArenaDecorBuilder.HallGeometry hall, DecorMeshes.Bank bank)
        => AddBeams(parent, lenses, p => Inside(hall.rail, p), BeamBottom, bank);

    /// <summary>Les faisceaux sous les verres `lenses` dont le pied (x, z) est `inside`, jusqu'à `bottom` (m) : la même
    /// construction pour l'arène et le stand de tir.</summary>
    internal static int AddBeams(Transform parent, List<Vector3> lenses, System.Func<Vector2, bool> inside, float bottom, DecorMeshes.Bank bank)
    {
        const int sides = 16;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        float spread = Mathf.Tan(BeamHalfAngle * Mathf.Deg2Rad);
        int count = 0;
        foreach (Vector3 lens in lenses)
        {
            float length = lens.y - bottom;
            if (length < 1f || !inside(new Vector2(lens.x, lens.z))) continue;
            int start = vertices.Count;
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices.Add(lens + radial * BeamRadius);
                vertices.Add(new Vector3(lens.x, bottom, lens.z) + radial * (BeamRadius + length * spread));
                normals.Add(radial);
                normals.Add(radial);
                uvs.Add(new Vector2(i / (float)sides, 0f));
                uvs.Add(new Vector2(i / (float)sides, 1f));
            }
            for (int i = 0; i < sides; i++)
            {
                int a = start + i * 2, b = a + 2;
                triangles.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
            count++;
        }
        if (count == 0) return 0;
        var mesh = new Mesh { name = "Faisceaux" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        var go = new GameObject("Faisceaux");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = bank.Add(mesh);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = BeamMaterial();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.renderingLayerMask = ArenaDecorBuilder.HallLayerMask;
        return count;
    }

    private static Material BeamMaterial()
    {
        var m = new Material(Shader.Find("DuelArena/FaisceauProjecteur"));
        m.SetColor("_Couleur", Froid);
        m.SetFloat("_Intensite", 0.05f);
        m.SetFloat("_Proche", 3f);
        return RobotChassisBuilder.SaveMaterial(BeamMaterialPath, m);
    }

    /// <summary>Un point dans un polygone (règle du nombre de croisements).</summary>
    private static bool Inside(Vector2[] polygon, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Vector2 a = polygon[i], b = polygon[j];
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
    }

    /// <summary>Le liseré de la tribune : une bande qui luit sous le bord de la passerelle, côté arène, sur tout son tour.
    /// Il dessine le bord de l'arène dans la pénombre sans l'éclairer ; 1 cm de saillie sur la dalle.</summary>
    private static void AddRailStrip(Transform parent, ArenaDecorBuilder.HallGeometry hall, Material material, DecorMeshes.Bank bank)
    {
        var b = new DecorMeshes.Builder();
        Vector2[] rail = hall.rail;
        float y0 = hall.floorTop - 0.17f, y1 = hall.floorTop - 0.13f;
        for (int i = 0; i < rail.Length; i++)
        {
            Vector2 o = DecorMeshes.OutwardOf(rail, i);
            Vector2 a = rail[i] - o * 0.012f, c = rail[(i + 1) % rail.Length] - o * 0.012f;
            b.Quad(new Vector3(a.x, y0, a.y), new Vector3(c.x, y0, c.y), new Vector3(c.x, y1, c.y), new Vector3(a.x, y1, a.y),
                   new Vector3(-o.x, 0f, -o.y));
        }
        GameObject strip = ArenaDecorBuilder.Part(parent, "Liseré de la tribune", bank.Add(b.ToMesh("Tribune_Lisere")), material,
                                                  ArenaDecorBuilder.HallLayerMask);
        strip.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    /// <summary>Une applique : un boîtier contre le mur, sa face qui luit vers le bas (sous-maillage 1).</summary>
    internal static void AddSconce(DecorMeshes.Builder b, Vector3 position, Vector3 inward)
    {
        Vector3 along = Vector3.Cross(Vector3.up, inward);
        DecorMeshes.OrientedBox(b, position, along, Vector3.up, inward, new Vector3(0.3f, 0.07f, 0.16f));
        Vector3 face = position + Vector3.down * 0.075f;
        b.Quad(face + along * -0.26f + inward * -0.12f, face + along * 0.26f + inward * -0.12f,
               face + along * 0.26f + inward * 0.12f, face + along * -0.26f + inward * 0.12f, Vector3.down, 0f, 1);
    }

    /// <summary>Le profil du post-traitement de l'arène : un tonemapping neutre (les peintures des robots gardent leur
    /// teinte), un peu de contraste, un halo sur les lampes (son seuil au-dessus des marquages blancs éclairés), un
    /// vignettage léger.</summary>
    private static VolumeProfile PostProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ProfilePath));
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        foreach (VolumeComponent c in profile.components.ToArray())
        {
            profile.Remove(c.GetType());
            Object.DestroyImmediate(c, true);
        }

        var tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.Neutral);
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1.25f);
        bloom.intensity.Override(0.6f);
        bloom.scatter.Override(0.7f);
        var color = profile.Add<ColorAdjustments>(true);
        color.contrast.Override(10f);
        color.saturation.Override(-4f);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.22f);
        vignette.smoothness.Override(0.45f);
        foreach (VolumeComponent c in profile.components)
        {
            if (!AssetDatabase.Contains(c)) AssetDatabase.AddObjectToAsset(c, profile);
        }
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }
}
