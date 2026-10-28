using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// La lumière du stand de tir, de la Fosse et de la salle de précision (2026-10-28) : celle de l'arène, « projecteurs,
/// pénombre » (ArenaLighting), dans des salles plus basses (10 m ; 5 m pour la salle de précision).
///
/// - **La rampe générale** (directionnelle, presque verticale, froide, qui porte les ombres) sur le calque de l'arène :
///   les sols, les blocs, les robots et les mannequins ; la salle (le haut des murs, le plafond, les poutres) reste dans la
///   pénombre, sur son propre calque.
/// - **Les rampes de projecteurs** pendues au plafond, un projecteur sans ombre sous chacune, et **leurs faisceaux** dans
///   la brume, qui s'arrêtent à BeamBottom (au-dessus d'un robot debout sur le toit des bureaux).
/// - **L'auvent du pas de tir** et **les bureaux** ont leurs lampes ; **la salle de précision**, une lampe au plafond tous
///   les 5 m, devant chaque place de la cible ; le haut des murs, des lampes rasantes ; une ambiance sombre et bleutée, la
///   brume de l'arène, une sonde de reflets par salle, et le post-traitement de l'arène.
/// </summary>
public static class StandLighting
{
    /// <summary>Le bas des faisceaux : au-dessus d'un robot debout sur le toit des bureaux de la Fosse (3 m + 1,8 m).</summary>
    public const float BeamBottom = 5.2f;
    /// <summary>La hauteur des rampes de projecteurs, sous les poutres.</summary>
    public const float RigHeight = 8.4f;

    public static void Build(Scene scene, Dictionary<string, Material> materials, DecorMeshes.Bank bank, StringBuilder report)
    {
        foreach (GameObject old in scene.GetRootGameObjects())
        {
            if (old.name == ArenaLighting.RootName) Object.DestroyImmediate(old);
        }
        var root = new GameObject(ArenaLighting.RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        float ceiling = ShootingRangeBuilder.CeilingHeight;

        // La rampe générale.
        Light key = ArenaLighting.NewLight(root.transform, "Rampe générale", LightType.Directional);
        key.transform.rotation = Quaternion.Euler(64f, 38f, 0f);
        key.color = ArenaLighting.Froid;
        key.intensity = 0.9f;
        key.lightmapBakeType = LightmapBakeType.Mixed;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.92f;
        ArenaLighting.SetLayers(key, ArenaDecorBuilder.ArenaLayerMask);
        RenderSettings.sun = key;

        // Les rampes de projecteurs : en travers du stand, trois par rangée, entre les déflecteurs ; une grille au-dessus de
        // la Fosse.
        GameObject rigPrefab = DecorImporter.Prefab("PR1");
        var rigRoot = new GameObject("Rampes de projecteurs").transform;
        rigRoot.SetParent(root.transform, false);
        var rods = new DecorMeshes.Builder();
        var lenses = new List<Vector3>();
        int spots = 0;
        foreach ((Vector2 at, float intensity) in RigPlaces())
        {
            if (rigPrefab != null)
                ArenaLighting.PlaceRig(rigRoot, rods, rigPrefab, new Vector3(at.x, ceiling + 0.3f, at.y), RigHeight, Vector3.forward, lenses);
            ArenaLighting.AddSpot(root.transform, new Vector3(at.x, RigHeight - 0.55f, at.y), 84f, 22f, intensity);
            spots++;
        }
        if (rods.VertexCount > 0)
        {
            GameObject rodObject = ArenaDecorBuilder.Part(rigRoot, "Suspentes", bank.Add(rods.ToMesh("Stand_Suspentes")), materials["Decor_Charpente"], ArenaDecorBuilder.HallLayerMask);
            rodObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        int beams = ArenaLighting.AddBeams(rigRoot, lenses, p => Above(p), BeamBottom, bank);

        // L'auvent du pas de tir : ses dalles lumineuses (StandDecorBuilder) et leurs lampes, deux rangées (il cache la rampe
        // générale : sans elles, les postes étaient dans le noir).
        int lamps = 0;
        foreach (float z in new[] { -2.4f, -7f })
        {
            for (int i = 0; i < 6; i++)
            {
                float x = StandDecorBuilder.StandRoom.center.x + (i - 2.5f) * 3.8f;
                Light lamp = ArenaLighting.NewLight(root.transform, "Lampe de l'auvent", LightType.Point);
                lamp.transform.position = new Vector3(x, 3.3f, z);
                lamp.color = ArenaLighting.Froid;
                lamp.intensity = 7f;
                lamp.range = 8f;
                lamp.shadows = LightShadows.None;
                lamp.lightmapBakeType = LightmapBakeType.Mixed;
                ArenaLighting.SetLayers(lamp, ArenaDecorBuilder.ArenaLayerMask | ArenaDecorBuilder.HallLayerMask);
                lamps++;
            }
        }

        // Les bureaux de la Fosse : des dalles tièdes au plafond, et leurs lampes.
        var panels = new DecorMeshes.Builder();
        foreach (Vector3 p in ShootingRangeBuilder.OfficeLamps)
        {
            Vector3 at = new Vector3(p.x, 2.695f, p.z);
            panels.Quad(at + new Vector3(-0.6f, 0f, -0.3f), at + new Vector3(0.6f, 0f, -0.3f), at + new Vector3(0.6f, 0f, 0.3f),
                        at + new Vector3(-0.6f, 0f, 0.3f), Vector3.down);
            Light lamp = ArenaLighting.NewLight(root.transform, "Lampe des bureaux", LightType.Point);
            lamp.transform.position = new Vector3(p.x, 2.4f, p.z);
            lamp.color = ArenaLighting.Tiede;
            lamp.intensity = 3.6f;
            lamp.range = 7.5f;
            lamp.shadows = LightShadows.None;
            lamp.lightmapBakeType = LightmapBakeType.Mixed;
            ArenaLighting.SetLayers(lamp, ArenaDecorBuilder.ArenaLayerMask);
            lamps++;
        }
        GameObject panelObject = ArenaDecorBuilder.Part(root.transform, "Dalles lumineuses des bureaux", bank.Add(panels.ToMesh("Bureaux_Dalles")),
                                                        materials["Decor_LampeTiede"], ArenaDecorBuilder.ArenaLayerMask);
        panelObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // La salle de précision : une dalle froide au plafond tous les 5 m, 2,5 m devant chaque place de la cible (sa face
        // est éclairée de face), et deux au-dessus du coin d'attente ; elles éclairent aussi le plafond, tout proche.
        var aimPanels = new DecorMeshes.Builder();
        float aimCeiling = ShootingRangeBuilder.AimRoomHeight;
        foreach (float z in AimLampRows())
        {
            Vector3 at = new Vector3(ShootingRangeBuilder.AimLaneX, aimCeiling - 0.005f, z);
            aimPanels.Quad(at + new Vector3(-0.6f, 0f, -0.15f), at + new Vector3(0.6f, 0f, -0.15f), at + new Vector3(0.6f, 0f, 0.15f),
                           at + new Vector3(-0.6f, 0f, 0.15f), Vector3.down);
            Light lamp = ArenaLighting.NewLight(root.transform, "Lampe de la salle de précision", LightType.Point);
            lamp.transform.position = new Vector3(ShootingRangeBuilder.AimLaneX, aimCeiling - 0.35f, z);
            lamp.color = ArenaLighting.Froid;
            lamp.intensity = 4.2f;
            lamp.range = 8f;
            lamp.shadows = LightShadows.None;
            lamp.lightmapBakeType = LightmapBakeType.Mixed;
            ArenaLighting.SetLayers(lamp, ArenaDecorBuilder.ArenaLayerMask | ArenaDecorBuilder.HallLayerMask);
            lamps++;
        }
        GameObject aimPanelObject = ArenaDecorBuilder.Part(root.transform, "Dalles lumineuses de la salle de précision", bank.Add(aimPanels.ToMesh("Precision_Dalles")),
                                                           materials["Decor_LampeFroide"], ArenaDecorBuilder.ArenaLayerMask | ArenaDecorBuilder.HallLayerMask);
        aimPanelObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // Le haut des murs : des lampes rasantes qui les sortent du noir, et leurs appliques.
        var sconces = new DecorMeshes.Builder();
        int washers = 0;
        foreach ((Vector3 at, Vector3 inward) in WasherPlaces())
        {
            Light washer = ArenaLighting.NewLight(root.transform, "Lampe rasante", LightType.Spot);
            washer.transform.position = at;
            washer.transform.rotation = Quaternion.LookRotation((Vector3.down * 2f - inward).normalized, Vector3.up);
            washer.spotAngle = 95f;
            washer.innerSpotAngle = 40f;
            washer.range = 10f;
            washer.intensity = 26f;
            washer.color = ArenaLighting.Froid;
            washer.shadows = LightShadows.None;
            washer.lightmapBakeType = LightmapBakeType.Mixed;
            ArenaLighting.SetLayers(washer, ArenaDecorBuilder.HallLayerMask);
            ArenaLighting.AddSconce(sconces, at + Vector3.up * 0.2f, inward);
            washers++;
        }
        GameObject sconceObject = ArenaDecorBuilder.Part(root.transform, "Appliques", bank.Add(sconces.ToMesh("Stand_Appliques")), materials["Decor_Charpente"], ArenaDecorBuilder.HallLayerMask);
        sconceObject.GetComponent<MeshRenderer>().sharedMaterials = new[] { materials["Decor_Charpente"], materials["Decor_LampeTiede"] };

        // Ce que le sol éclairé renverrait vers le plafond (en attendant la lumière cuite), sur le seul calque de la salle.
        int fills = 0;
        float middle = StandDecorBuilder.StandRoom.center.x;
        foreach (Vector2 p in new[] { new Vector2(middle, 8f), new Vector2(middle, 30f), new Vector2(middle, 52f), new Vector2(32f, 5f), new Vector2(52f, 5f), new Vector2(32f, 36f), new Vector2(52f, 36f) })
        {
            Light fill = ArenaLighting.NewLight(root.transform, "Reflet du sol", LightType.Point);
            fill.transform.position = new Vector3(p.x, 7.5f, p.y);
            fill.range = 18f;
            fill.intensity = 2f;
            fill.color = ArenaLighting.Froid;
            fill.shadows = LightShadows.None;
            ArenaLighting.SetLayers(fill, ArenaDecorBuilder.HallLayerMask);
            fills++;
        }

        // L'ambiance de l'arène : sombre et bleutée, sans ciel ; la même brume.
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

        // Une sonde de reflets par salle (en boîte), cuite.
        string folder = System.IO.Path.GetDirectoryName(scene.path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder($"{folder}/{scene.name}")) AssetDatabase.CreateFolder(folder, scene.name);
        int index = 0;
        foreach ((Rect room, float height) in new[] { (StandDecorBuilder.StandRoom, ceiling), (StandDecorBuilder.CourseRoom, ceiling),
                                                       (StandDecorBuilder.AimRoom, ShootingRangeBuilder.AimRoomHeight) })
        {
            var probeObject = new GameObject("Sonde de reflets");
            probeObject.transform.SetParent(root.transform, false);
            probeObject.transform.position = new Vector3(room.center.x, Mathf.Min(2.5f, height * 0.5f), room.center.y);
            var probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.boxProjection = true;
            probe.size = new Vector3(room.width, height + 1f, room.height);
            probe.center = new Vector3(0f, height * 0.5f - probeObject.transform.position.y, 0f);
            probe.resolution = 128;
            probe.importance = 1;
            Lightmapping.BakeReflectionProbe(probe, $"{folder}/{scene.name}/ReflectionProbe-{index++}.exr");
        }

        // Le post-traitement de l'arène (le même profil : la même image).
        var volumeObject = new GameObject("Post-traitement");
        volumeObject.transform.SetParent(root.transform, false);
        var volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ArenaLighting.ProfilePath);

        int probes = BuildLightProbes(root.transform);
        Lightmapping.lightingSettings = ArenaLighting.LightingSettingsAsset(scene);
        report.AppendLine($"Lumière : la rampe générale, {spots} projecteurs et {beams} faisceaux, {lamps} lampes (auvent, bureaux, salle de précision), {washers} lampes rasantes, {fills} reflets du sol, {probes} sondes de lumière ; brume, trois sondes de reflets, post-traitement. Lumière indirecte : Duel Arena > Stand de tir > Cuire la lumière du stand.");
    }

    /// <summary>Où pendent les rampes de projecteurs (x, z), et la force de leur projecteur.</summary>
    private static IEnumerable<(Vector2, float)> RigPlaces()
    {
        float middle = StandDecorBuilder.StandRoom.center.x;
        foreach (float z in new[] { 9f, 21f, 33f, 45f, 57f })
            foreach (float dx in new[] { -6.5f, 0f, 6.5f })
                yield return (new Vector2(middle + dx, z), 46f);
        foreach (float z in new[] { -3.5f, 8.5f, 20.5f, 32.5f, 44.5f })
            foreach (float x in new[] { 27.5f, 37.5f, 47.5f, 57f })
                yield return (new Vector2(x, z), 46f);
    }

    /// <summary>Les rangées des lampes de la salle de précision (z) : deux au-dessus du coin d'attente, puis tous les 5 m,
    /// 2,5 m devant chaque place de la cible.</summary>
    private static IEnumerable<float> AimLampRows()
    {
        yield return -7f;
        yield return -3f;
        for (float z = 2.5f; z < StandDecorBuilder.AimRoom.yMax - 2f; z += 5f) yield return z;
    }

    /// <summary>Un faisceau sous un verre qui donne sur le pas de tir du stand (pas au-dessus de l'auvent) ou sur la Fosse.</summary>
    private static bool Above(Vector2 p)
    {
        Rect stand = StandDecorBuilder.StandRoom, course = StandDecorBuilder.CourseRoom;
        return (stand.Contains(p) && p.y > 0.5f) || course.Contains(p);
    }

    /// <summary>Les lampes rasantes : sous le plafond, le long des grands murs des deux salles, tournées vers le mur.</summary>
    private static IEnumerable<(Vector3, Vector3)> WasherPlaces()
    {
        float y = ShootingRangeBuilder.CeilingHeight - 1.1f;
        Rect s = StandDecorBuilder.StandRoom, c = StandDecorBuilder.CourseRoom;
        foreach (float z in new[] { 2f, 18f, 34f, 50f })
        {
            yield return (new Vector3(s.xMin + 0.5f, y, z), Vector3.right);
            yield return (new Vector3(s.xMax - 0.5f, y, z), Vector3.left);
        }
        yield return (new Vector3(s.center.x - 6f, y, s.yMax - 0.5f), Vector3.back);
        yield return (new Vector3(s.center.x + 6f, y, s.yMax - 0.5f), Vector3.back);
        foreach (float z in new[] { 4f, 20f, 36f })
            yield return (new Vector3(c.xMax - 0.5f, y, z), Vector3.left);
        foreach (float x in new[] { 32f, 51f })
        {
            yield return (new Vector3(x, y, c.yMin + 0.5f), Vector3.forward);
            yield return (new Vector3(x, y, c.yMax - 0.5f), Vector3.back);
        }
    }

    /// <summary>Les sondes de lumière : une grille de 4 m dans le stand et la Fosse, de 2,5 m sur 4 dans la salle de
    /// précision (le chariot de la cible en tire sa lumière), à hauteur de robot (et du toit des bureaux, de la passerelle,
    /// dans la Fosse) ; aucune dans un bloc.</summary>
    private static int BuildLightProbes(Transform root)
    {
        Physics.SyncTransforms();
        int world = LayerMask.GetMask("Default");
        var positions = new List<Vector3>();
        void Grid(Rect room, float[] heights, float step = 4f)
        {
            for (float x = room.xMin + step * 0.5f; x <= room.xMax - 1f; x += step)
            for (float z = room.yMin + 1.5f; z <= room.yMax - 1f; z += 4f)
            foreach (float y in heights)
            {
                var p = new Vector3(x, y, z);
                if (!Physics.CheckSphere(p, 0.15f, world, QueryTriggerInteraction.Ignore)) positions.Add(p);
            }
        }
        Grid(StandDecorBuilder.StandRoom, new[] { 0.6f, 1.9f, 3.4f });
        Grid(StandDecorBuilder.CourseRoom, new[] { 0.6f, 1.9f, 3.6f, 4.9f });
        Grid(StandDecorBuilder.AimRoom, new[] { 0.6f, 1.6f, 2.6f, 4f }, 2.5f);
        var go = new GameObject("Sondes de lumière");
        go.transform.SetParent(root, false);
        go.AddComponent<LightProbeGroup>().probePositions = positions.ToArray();
        return positions.Count;
    }
}
