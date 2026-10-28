using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Cuit les images du menu dans les vraies scènes (2026-10-17, pour les maquettes de l'utilisateur) :
/// Duel Arena > Menu > Capturer les images du menu. On relance l'outil quand l'arène ou le stand change ; on
/// ne retouche jamais les images à la main (MenuArtwork).
///
/// - L'arène (MultiTestScene), ouverte à côté de la scène courante puis refermée sans rien y enregistrer :
///   le fond de l'écran titre (vu de la tribune, des robots sur la passerelle et deux duellistes face à face),
///   la vue d'ensemble (salon, détail des modes), et la carte vue de dessus avec ses éléments clés, lus dans
///   la scène : les points d'apparition (PlayerSpawnPoints), le garde-corps de la tribune et ses blocs
///   (Tribune).
/// - Le stand de tir (StandDeTir) : la ligne de tir et ses mannequins, et la Fosse vue d'en haut.
///
/// Les robots sont des copies du modèle du préfab joueur (RobotAppearance), peintes aux couleurs de la
/// palette et posées par leurs clips, sur le layer Default : seule la lumière de la scène les éclaire.
///
/// L'arène habillée (2026-10-27, ArenaDecorBuilder) a sa propre lumière, sa salle et son plafond : la capture la
/// garde telle quelle (pas de nuit posée par-dessus), et cache le haut de la salle (charpente, rampes, appliques)
/// pour les vues d'en haut, qui regardent à travers le plafond.
/// </summary>
public static class MenuArtworkCapture
{
    public const string ArtworkPath = "Assets/_ProjectUI/Images/MenuArtwork.asset";
    private const string ImageFolder = "Assets/_ProjectUI/Images";
    private const string ArenaScenePath = "Assets/_ProjectScenes/MultiTestScene.unity";
    private const string RangeScenePath = "Assets/_ProjectScenes/StandDeTir.unity";
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    // Rangs de la palette des robots (PlayerPaint, préfab joueur) : orange, bleu, magenta, vert, cyan, violet, jaune.
    private const int Orange = 0, Blue = 1, Magenta = 2, Green = 3, Cyan = 4, Violet = 5, Yellow = 6;

    /// <summary>Marge autour du garde-corps sur la carte : la passerelle (3,5 m) et son mur.</summary>
    private const float MapMargin = 5f;

    [MenuItem("Duel Arena/Menu/Capturer les images du menu")]
    public static void CaptureFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Menu] Capture des images : sortir du Play Mode d'abord.");
            return;
        }
        Capture();
    }

    /// <summary>Capture toutes les images et met MenuArtwork à jour. Rend un compte rendu.</summary>
    public static string Capture()
    {
        MenuArtwork artwork = LoadOrCreateArtwork();
        var report = new System.Text.StringBuilder();
        try
        {
            EditorUtility.DisplayProgressBar("Images du menu", "L'arène…", 0.2f);
            CaptureArena(artwork, report);
            EditorUtility.DisplayProgressBar("Images du menu", "Le stand de tir…", 0.7f);
            CaptureRange(artwork, report);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        EditorUtility.SetDirty(artwork);
        AssetDatabase.SaveAssets();
        Debug.Log("[Menu] Images du menu capturées.\n" + report);
        return report.ToString();
    }

    private static MenuArtwork LoadOrCreateArtwork()
    {
        var artwork = AssetDatabase.LoadAssetAtPath<MenuArtwork>(ArtworkPath);
        if (artwork != null) return artwork;
        Directory.CreateDirectory(ImageFolder);
        artwork = ScriptableObject.CreateInstance<MenuArtwork>();
        AssetDatabase.CreateAsset(artwork, ArtworkPath);
        return artwork;
    }

    // ------------------------------------------------------------------
    // L'arène
    // ------------------------------------------------------------------

    private static void CaptureArena(MenuArtwork artwork, System.Text.StringBuilder report)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene arena = OpenAdditive(ArenaScenePath, out bool openedHere);
        var temp = new GameObject("CaptureMenu (temporaire)");
        SceneManager.MoveGameObjectToScene(temp, arena);
        var robots = new List<PlayableGraph>();
        NightMood mood = null;
        var hidden = new List<Renderer>();
        try
        {
            SceneManager.SetActiveScene(arena);
            Physics.SyncTransforms();
            bool dressed = HasRoot(arena, ArenaLighting.RootName);
            if (dressed) hidden = HideHallTop(arena);

            var points = Object.FindObjectsByType<PlayerSpawnPoints>(FindObjectsInactive.Include)
                .Find(p => p.gameObject.scene == arena);
            var tribune = Object.FindObjectsByType<Tribune>(FindObjectsInactive.Include)
                .Find(t => t.gameObject.scene == arena);
            if (points == null || tribune == null)
            {
                report.AppendLine("Arène : PlayerSpawnPoints ou Tribune introuvable, rien capturé.");
                return;
            }
            Transform[] spawns = TransformArray(points, "spawnPoints");
            Transform[] standSpawns = TransformArray(tribune, "spectatorSpawns");
            Vector2[] rail = Vector2Array(tribune, "railLine");
            float floorTop = FloatField(tribune, "floorTop", 4.5f);
            Rect railRect = RectOf(rail);
            var center = new Vector3(railRect.center.x, 0f, railRect.center.y);
            IReadOnlyList<Color32> palette = Palette();
            Clips clips = LoadClips();

            // ---- La carte d'abord, à la lumière de la scène : vue de dessus, le +z du monde en haut.
            var mapRect = new Rect(railRect.xMin - MapMargin, railRect.yMin - MapMargin, railRect.width + MapMargin * 2f, railRect.height + MapMargin * 2f);
            int mapWidth = 1024, mapHeight = Mathf.RoundToInt(mapWidth * mapRect.height / mapRect.width);
            Camera cam = NewCamera(temp.transform);
            cam.orthographic = true;
            cam.orthographicSize = mapRect.height * 0.5f;
            cam.transform.SetPositionAndRotation(new Vector3(mapRect.center.x, 120f, mapRect.center.y),
                Quaternion.LookRotation(Vector3.down, Vector3.forward));
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 240f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 1f);
            artwork.arenaTopDown = SaveRender(cam, mapWidth, mapHeight, "Menu_Carte.png", report);
            artwork.topDownWorldRect = mapRect;

            var markers = new List<MapMarker>();
            string[] labels = { "A", "B", "C", "D" };
            for (int i = 0; i < spawns.Length; i++)
            {
                if (spawns[i] == null) continue;
                markers.Add(new MapMarker { kind = MapMarkerKind.Spawn, position = Ground(spawns[i].position), label = i < labels.Length ? labels[i] : (i + 1).ToString() });
            }
            foreach (RockSource source in tribune.RockSources)
            {
                if (source != null) markers.Add(new MapMarker { kind = MapMarkerKind.RockSource, position = Ground(source.transform.position), label = "" });
            }
            artwork.mapMarkers = markers.ToArray();
            artwork.walkwayRail = rail;
            report.AppendLine($"Carte : {mapRect.width:0} × {mapRect.height:0} m, {spawns.Length} points d'apparition, {tribune.RockSources.Count} blocs.");

            // ---- Puis l'arène de nuit, comme une salle de spectacle : projecteurs, lampes de la tribune, brume. L'arène
            // habillée a déjà les siens.
            if (!dressed)
            {
                mood = new NightMood(arena);
                for (int i = -1; i <= 1; i++)
                {
                    // Trois projecteurs le long du grand axe de l'arène.
                    mood.AddSpot(temp.transform, center + new Vector3(0f, 26f, i * railRect.height * 0.3f),
                        new Vector3(0f, -1f, -i * 0.12f), 68f, 70f, 520f, i == 0);
                }
            }
            cam.orthographic = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = NightMood.Sky;

            // Le fond de l'écran titre : sur la passerelle d'un pan en biais, le garde-corps devant, vers le
            // milieu de l'arène.
            int edge = EdgeFacing(rail, center, new Vector2(-1f, -0.55f));
            Vector2 a = rail[edge], b = rail[(edge + 1) % rail.Length];
            Vector2 middle = (a + b) * 0.5f;
            Vector2 outward = (middle - new Vector2(center.x, center.z)).normalized;
            Vector3 eye = new Vector3(middle.x + outward.x * 2.2f, floorTop + 1.7f, middle.y + outward.y * 2.2f);
            Vector3 target = center + new Vector3(0f, -2.5f, railRect.height * 0.08f);
            Quaternion look = Quaternion.LookRotation((target - eye).normalized, Vector3.up);
            look = Quaternion.AngleAxis(-11f, Vector3.up) * look;   // l'arène passe à droite du cadre : le menu occupe la gauche
            Vector3 forward = look * Vector3.forward, right = look * Vector3.right;
            Vector3 flatForward = new Vector3(forward.x, 0f, forward.z).normalized, flatRight = new Vector3(right.x, 0f, right.z).normalized;

            for (int i = 0; i < standSpawns.Length; i += 2)
            {
                // Des lampes jaunes sur la passerelle, une place de spectateur sur deux, loin de l'objectif.
                if (Vector3.Distance(standSpawns[i].position, eye) < 14f) continue;
                mood?.AddLamp(temp.transform, standSpawns[i].position + Vector3.up * 2.4f, 9f, 28f);
            }

            // Deux duellistes face à face, là où la caméra les voit : ni derrière un mur, ni derrière le robot du
            // premier plan (à droite du cadre), ni sous le menu (à gauche).
            Vector3 groundEye = new Vector3(eye.x, 0f, eye.z);
            if (!FindDuelSpots(eye, groundEye, flatForward, out Vector3 duelA, out Vector3 duelB))
            {
                duelA = Vector3.Lerp(center, spawns.Length > 0 ? spawns[0].position : center, 0.28f);
                duelB = Vector3.Lerp(center, spawns.Length > 1 ? spawns[1].position : center, 0.28f);
                duelA.y = duelB.y = 0f;
                report.AppendLine("Duellistes : aucune place dégagée en vue, placés au milieu.");
            }
            robots.Add(PlaceRobot(temp.transform, duelA, YawTowards(duelA, duelB), palette, Orange, clips.punch, 0.12f));
            robots.Add(PlaceRobot(temp.transform, duelB, YawTowards(duelB, duelA), palette, Blue, clips.punch, 0.12f));

            // Les spectateurs sur la passerelle, tournés vers l'arène, pas trop près de l'objectif.
            int[] standColors = { Yellow, Cyan, Magenta, Green, Violet, Orange, Yellow, Blue };
            for (int i = 0; i < standSpawns.Length; i++)
            {
                Vector3 p = standSpawns[i].position;
                if (Vector3.Distance(p, eye) < 9f) continue;
                bool cheering = i % 3 == 1;
                robots.Add(PlaceRobot(temp.transform, p, YawTowards(p, center), palette, standColors[i % standColors.Length],
                    cheering ? clips.celebration : clips.idle, cheering ? 1.1f + i * 0.3f : 0.7f * i));
            }
            // Au premier plan, à droite, contre le garde-corps : un robot jaune qui regarde l'arène. Jamais au-delà
            // du garde-corps : il resterait suspendu au-dessus du terrain.
            Vector3 front = eye + flatForward * 2.3f + flatRight * 1.15f;
            Vector2 offset = new Vector2(front.x, front.z) - middle;
            float behindRail = Vector2.Dot(offset, outward);
            if (behindRail < 0.4f) front += new Vector3(outward.x, 0f, outward.y) * (0.4f - behindRail);
            front.y = floorTop;
            robots.Add(PlaceRobot(temp.transform, front, YawTowards(front, front + flatForward) + 18f, palette, Yellow, clips.idle, 2.2f));
            // Une lumière douce sur ses épaules, venue de derrière l'objectif.
            mood?.AddLamp(temp.transform, front - flatForward * 1.6f + flatRight * 0.4f + Vector3.up * 2.6f, 4.5f, 2.2f, new Color(1f, 0.9f, 0.75f));

            // Le titre se voit de la passerelle, sous le plafond : le haut de la salle y reparaît.
            foreach (Renderer r in hidden) if (r != null) r.enabled = true;

            cam.fieldOfView = 54f;
            cam.transform.SetPositionAndRotation(eye, look);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            artwork.titleBackdrop = SaveRender(cam, 2560, 1440, "Menu_Titre.png", report);

            // La vue d'ensemble : de haut, en biais, l'arène et sa tribune (à travers le plafond de l'arène habillée).
            foreach (Renderer r in hidden) if (r != null) r.enabled = false;
            Vector3 overviewEye = center + new Vector3(-railRect.width * 0.44f, railRect.height * 0.34f, -railRect.height * 0.44f);
            Vector3 overviewTarget = center + new Vector3(0f, -3f, railRect.height * 0.06f);
            cam.fieldOfView = 50f;
            cam.transform.SetPositionAndRotation(overviewEye, Quaternion.LookRotation((overviewTarget - overviewEye).normalized, Vector3.up));
            artwork.arenaOverview = SaveRender(cam, 1280, 720, "Menu_Arene.png", report);
        }
        finally
        {
            foreach (PlayableGraph graph in robots)
            {
                if (graph.IsValid()) graph.Destroy();
            }
            mood?.Restore();
            foreach (Renderer r in hidden) if (r != null) r.enabled = true;
            Object.DestroyImmediate(temp);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (openedHere) EditorSceneManager.CloseScene(arena, true);
        }
    }

    private static bool HasRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == name) return true;
        return false;
    }

    /// <summary>Éteint ce qui pend sous le plafond de l'arène habillée (charpente, rampes de projecteurs, suspentes,
    /// appliques) : vu d'en haut, il cacherait l'arène. Rend ce qui a été éteint, à rallumer.</summary>
    private static List<Renderer> HideHallTop(Scene scene)
    {
        var hidden = new List<Renderer>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform[] parts =
            {
                root.name == ArenaDecorBuilder.DecorRootName ? root.transform.Find("Salle/Charpente") : null,
                // Le stand de tir habillé (2026-10-28) : ses poutres et les déflecteurs au-dessus du pas de tir.
                root.name == ArenaDecorBuilder.DecorRootName ? root.transform.Find("Salle/Poutres") : null,
                root.name == ArenaDecorBuilder.DecorRootName ? root.transform.Find("Salle/Déflecteurs") : null,
                root.name == ArenaLighting.RootName ? root.transform.Find("Rampes de projecteurs") : null,
                root.name == ArenaLighting.RootName ? root.transform.Find("Appliques") : null,
            };
            foreach (Transform part in parts)
            {
                if (part == null) continue;
                foreach (Renderer r in part.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled) continue;
                    r.enabled = false;
                    hidden.Add(r);
                }
            }
        }
        return hidden;
    }

    /// <summary>Le pan du garde-corps dont la normale sortante regarde le plus vers `direction` (x, z).</summary>
    private static int EdgeFacing(Vector2[] rail, Vector3 center, Vector2 direction)
    {
        int best = 0;
        float bestScore = float.MinValue;
        var c = new Vector2(center.x, center.z);
        for (int i = 0; i < rail.Length; i++)
        {
            Vector2 middle = (rail[i] + rail[(i + 1) % rail.Length]) * 0.5f;
            float score = Vector2.Dot((middle - c).normalized, direction.normalized);
            if (score <= bestScore) continue;
            bestScore = score;
            best = i;
        }
        return best;
    }

    /// <summary>Deux places au sol, dégagées et vues de l'objectif, à quelques mètres l'une de l'autre, un peu à
    /// droite de l'axe de la caméra (entre le menu, à gauche, et le robot du premier plan, à droite).</summary>
    private static bool FindDuelSpots(Vector3 eye, Vector3 groundEye, Vector3 forward, out Vector3 first, out Vector3 second)
    {
        int world = LayerMask.GetMask("Default");
        bool Free(Vector3 p) => !Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.5f, 0.45f, world, QueryTriggerInteraction.Ignore)
                                && Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, 1f, world, QueryTriggerInteraction.Ignore)
                                && !Physics.Linecast(eye, p + Vector3.up * 1.1f, world, QueryTriggerInteraction.Ignore);
        bool InFrame(Vector3 p)
        {
            Vector3 d = p - groundEye;
            float angle = Vector3.SignedAngle(forward, new Vector3(d.x, 0f, d.z), Vector3.up);
            return angle > 1f && angle < 22f;
        }
        first = second = default;
        for (float d = 16f; d <= 44f; d += 1.5f)
        {
            foreach (float bearing in new[] { 10f, 6f, 14f, 3f, 18f })
            {
                Vector3 p = groundEye + Quaternion.AngleAxis(bearing, Vector3.up) * forward * d;
                if (!Free(p)) continue;
                foreach (float gap in new[] { 7f, 8.5f, 6f, 10f })
                {
                    foreach (float turn in new[] { 40f, -40f, 60f, -60f, 20f, -20f })
                    {
                        Vector3 q = p + Quaternion.AngleAxis(turn, Vector3.up) * forward * gap;
                        if (!InFrame(q) || !Free(q)) continue;
                        first = p;
                        second = q;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>
    /// La nuit, le temps des rendus : ciel noir, soleil tamisé et bleuté, ambiance sombre, brume ; l'appelant y
    /// ajoute ses projecteurs et ses lampes (le moteur n'éclaire un objet que par quatre lumières d'appoint).
    /// Restore rend tout à l'identique : la scène n'est jamais enregistrée, mais elle peut avoir été ouverte
    /// avant la capture.
    /// </summary>
    private sealed class NightMood
    {
        public static readonly Color Sky = new Color(0.025f, 0.03f, 0.045f, 1f);

        private readonly AmbientMode ambientMode;
        private readonly Color ambientLight;
        private readonly float ambientIntensity;
        private readonly bool fog;
        private readonly Color fogColor;
        private readonly FogMode fogMode;
        private readonly float fogDensity;
        private readonly List<(Light light, float intensity, Color color)> suns = new();
        private readonly List<Light> spots = new();

        public NightMood(Scene scene)
        {
            ambientMode = RenderSettings.ambientMode;
            ambientLight = RenderSettings.ambientLight;
            ambientIntensity = RenderSettings.ambientIntensity;
            fog = RenderSettings.fog;
            fogColor = RenderSettings.fogColor;
            fogMode = RenderSettings.fogMode;
            fogDensity = RenderSettings.fogDensity;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.08f, 0.11f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.009f;
            RenderSettings.fogColor = new Color(0.035f, 0.04f, 0.06f);

            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (light.gameObject.scene != scene || light.type != LightType.Directional) continue;
                suns.Add((light, light.intensity, light.color));
                light.intensity *= 0.16f;
                light.color = new Color(0.55f, 0.65f, 0.9f);
            }
        }

        /// <summary>Un projecteur blanc chaud qui éclaire vers `direction`.</summary>
        public void AddSpot(Transform parent, Vector3 position, Vector3 direction, float angle, float range, float intensity, bool shadows)
        {
            Light spot = NewLight(parent, "Projecteur", LightType.Spot);
            spot.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction.normalized, Vector3.forward));
            spot.spotAngle = angle;
            spot.innerSpotAngle = angle * 0.6f;
            spot.range = range;
            spot.intensity = intensity;
            spot.color = new Color(1f, 0.95f, 0.86f);
            spot.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            spots.Add(spot);
        }

        /// <summary>Une lampe, jaune comme celles de la maquette sauf couleur donnée.</summary>
        public void AddLamp(Transform parent, Vector3 position, float range, float intensity, Color? color = null)
        {
            Light lamp = NewLight(parent, "Lampe", LightType.Point);
            lamp.transform.position = position;
            lamp.range = range;
            lamp.intensity = intensity;
            lamp.color = color ?? new Color(1f, 0.7f, 0.25f);
        }

        /// <summary>Éteint les projecteurs ajoutés (un autre cadrage en pose d'autres).</summary>
        public void ClearSpots()
        {
            foreach (Light spot in spots)
            {
                if (spot != null) Object.DestroyImmediate(spot.gameObject);
            }
            spots.Clear();
        }

        private static Light NewLight(Transform parent, string name, LightType type)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light>();
            light.type = type;
            light.renderMode = LightRenderMode.ForcePixel;
            return light;
        }

        public void Restore()
        {
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.fog = fog;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogDensity = fogDensity;
            foreach ((Light light, float intensity, Color color) in suns)
            {
                if (light == null) continue;
                light.intensity = intensity;
                light.color = color;
            }
        }
    }

    // ------------------------------------------------------------------
    // Le stand de tir et la Fosse
    // ------------------------------------------------------------------

    private static void CaptureRange(MenuArtwork artwork, System.Text.StringBuilder report)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene range = OpenAdditive(RangeScenePath, out bool openedHere);
        var temp = new GameObject("CaptureMenu (temporaire)");
        SceneManager.MoveGameObjectToScene(temp, range);
        var graphs = new List<PlayableGraph>();
        NightMood mood = null;
        var hidden = new List<Renderer>();
        try
        {
            SceneManager.SetActiveScene(range);
            Physics.SyncTransforms();
            // Le stand habillé (2026-10-28) a sa lumière, comme l'arène : la nuit de secours n'est que pour le blocking.
            bool dressed = HasRoot(range, ArenaLighting.RootName);

            // Les mannequins ne se montrent qu'en jeu (TrainingDummy.Awake) : on construit leur apparence ici.
            var buildAppearance = typeof(TrainingDummy).GetMethod("BuildAppearance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var graphField = typeof(TrainingDummy).GetField("idleGraph", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (TrainingDummy dummy in Object.FindObjectsByType<TrainingDummy>(FindObjectsInactive.Exclude))
            {
                if (dummy.gameObject.scene != range || buildAppearance == null) continue;
                buildAppearance.Invoke(dummy, null);
                if (graphField != null && graphField.GetValue(dummy) is PlayableGraph graph && graph.IsValid())
                {
                    graph.Evaluate(0f);
                    graphs.Add(graph);
                }
            }

            var points = Object.FindObjectsByType<PlayerSpawnPoints>(FindObjectsInactive.Include)
                .Find(p => p.gameObject.scene == range);
            Transform[] posts = points != null ? TransformArray(points, "spawnPoints") : new Transform[0];
            Camera cam = NewCamera(temp.transform);
            if (!dressed) mood = new NightMood(range);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = NightMood.Sky;
            if (posts.Length > 0 && posts[0] != null)
            {
                Transform post = posts[0];
                if (mood != null)
                    foreach (float along in new[] { 7f, 22f, 40f })
                        mood.AddSpot(temp.transform, post.position + post.forward * along + Vector3.up * 13f, Vector3.down + post.forward * 0.25f, 72f, 40f, 240f, along < 10f);
                // À la ligne de tir, entre les cloisons du poste (2026-10-28) : de derrière le poste, elles prenaient le tiers
                // de l'image ; d'ici, on voit les mannequins d'un mur à l'autre.
                Vector3 eye = post.position + Vector3.up * 1.75f + post.forward * 1.6f + post.right * 0.6f;
                cam.fieldOfView = 50f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((post.forward * 30f - Vector3.up * 1.2f).normalized, Vector3.up));
                artwork.rangeImage = SaveRender(cam, 1280, 720, "Menu_Stand.png", report);
            }
            else report.AppendLine("Stand : pas de poste de tir (PlayerSpawnPoints), image non refaite.");

            ShootingCourse course = Object.FindObjectsByType<ShootingCourse>(FindObjectsInactive.Include)
                .Find(c => c.gameObject.scene == range);
            if (course != null && course.Sections.Count > 0)
            {
                Bounds bounds = course.StartZone;
                foreach (ShootingCourse.Section section in course.Sections) bounds.Encapsulate(section.bounds);
                Vector3 c = bounds.center;
                float size = Mathf.Max(bounds.size.x, bounds.size.z);
                if (mood != null)
                {
                    mood.ClearSpots();
                    mood.AddSpot(temp.transform, c + new Vector3(0f, 24f, 0f), Vector3.down, 80f, 60f, 520f, true);
                    mood.AddSpot(temp.transform, c + new Vector3(-bounds.size.x * 0.3f, 20f, -bounds.size.z * 0.3f), Vector3.down, 70f, 50f, 300f, false);
                    mood.AddSpot(temp.transform, c + new Vector3(bounds.size.x * 0.3f, 20f, bounds.size.z * 0.3f), Vector3.down, 70f, 50f, 300f, false);
                }
                // Vue d'en haut, au-dessus du plafond (qu'on ne voit que d'en dessous) : ce qui pend dessous cacherait la Fosse.
                if (dressed) hidden = HideHallTop(range);
                Vector3 eye = c + new Vector3(-size * 0.45f, size * 0.55f, -size * 0.55f);
                cam.fieldOfView = 44f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((c - eye).normalized, Vector3.up));
                artwork.courseImage = SaveRender(cam, 1280, 720, "Menu_Fosse.png", report);
            }
            else report.AppendLine("Fosse : ShootingCourse introuvable, image non refaite.");
        }
        finally
        {
            foreach (Renderer r in hidden) if (r != null) r.enabled = true;
            foreach (PlayableGraph graph in graphs)
            {
                if (graph.IsValid()) graph.Destroy();
            }
            mood?.Restore();
            Object.DestroyImmediate(temp);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (openedHere) EditorSceneManager.CloseScene(range, true);
        }
    }

    // ------------------------------------------------------------------
    // Outils
    // ------------------------------------------------------------------

    private static Scene OpenAdditive(string path, out bool openedHere)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        if (scene.IsValid() && scene.isLoaded)
        {
            openedHere = false;
            return scene;
        }
        openedHere = true;
        return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
    }

    private sealed class Clips
    {
        public AnimationClip idle, celebration, punch;
    }

    private static Clips LoadClips() => new Clips
    {
        idle = Clip("Assets/_ProjectArt/Animations/MainsNues/Y Bot@Idle.fbx", "Idle"),
        celebration = Clip("Assets/_ProjectArt/Animations/New/Y Bot@Celebration1.fbx", "Celebration1"),
        punch = Clip("Assets/_ProjectArt/Animations/New/Y Bot@Taser Punch.fbx", "Taser Punch"),
    };

    private static AnimationClip Clip(string path, string name)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (o is AnimationClip clip && clip.name == name) return clip;
        }
        return null;
    }

    private static IReadOnlyList<Color32> Palette()
    {
        var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        PlayerPaint paint = player != null ? player.GetComponent<PlayerPaint>() : null;
        return paint != null ? paint.Palette : new Color32[] { new Color32(244, 190, 28, 255) };
    }

    /// <summary>Un robot posé : copie du modèle du préfab joueur, peinte, dans sa pose, sur le layer Default.
    /// Rend le graphe de sa pose, à détruire après les rendus.</summary>
    private static PlayableGraph PlaceRobot(Transform parent, Vector3 position, float yaw, IReadOnlyList<Color32> palette, int paint, AnimationClip pose, float time)
    {
        var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform assetModel = player != null ? player.transform.Find("Model") : null;
        if (assetModel == null) return default;

        var holder = new GameObject("Robot");
        holder.transform.SetParent(parent, false);
        holder.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        GameObject copy = RobotAppearance.Copy(assetModel.gameObject, holder.transform, "Apparence");
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        RobotAppearance.Paint(player, assetModel, copy, palette[Mathf.Clamp(paint, 0, palette.Count - 1)]);

        Animator animator = copy.GetComponent<Animator>();
        if (animator == null || pose == null) return default;
        animator.applyRootMotion = false;
        animator.runtimeAnimatorController = null;
        PlayableGraph graph = PlayableGraph.Create("CaptureMenu");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
        var playable = AnimationClipPlayable.Create(graph, pose);
        playable.SetApplyFootIK(false);
        playable.SetTime(Mathf.Repeat(time, Mathf.Max(0.01f, pose.length)));
        output.SetSourcePlayable(playable);
        graph.Evaluate(0f);
        return graph;
    }

    private static float YawTowards(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }

    /// <summary>Le point de la passerelle (sa ligne médiane, ou à défaut le garde-corps) le plus proche de
    /// `target`, au niveau de son sol.</summary>
    private static Vector3 WalkwayPointNear(Tribune tribune, Vector3 target, float floorTop)
    {
        Vector3[] centerline = Vector3Array(tribune, "walkwayCenterline");
        Vector3 best = target;
        float bestDistance = float.MaxValue;
        foreach (Vector3 p in centerline)
        {
            float d = new Vector2(p.x - target.x, p.z - target.z).sqrMagnitude;
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = p;
        }
        best.y = floorTop;
        return best;
    }

    private static Camera NewCamera(Transform parent)
    {
        var go = new GameObject("CameraCapture");
        go.transform.SetParent(parent, false);
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.cullingMask = ~(1 << PreviewLayer.Index);
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        cam.allowMSAA = true;
        return cam;
    }

    /// <summary>Rend la caméra dans une image de `width` × `height`, l'enregistre en PNG dans le dossier des
    /// images du menu, la règle pour l'interface et la rend.</summary>
    private static Texture2D SaveRender(Camera cam, int width, int height, string fileName, System.Text.StringBuilder report)
    {
        cam.aspect = (float)width / height;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 8 };
        var resolved = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        target.Create();
        resolved.Create();
        try
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else
            {
                cam.targetTexture = target;
                cam.Render();
                cam.targetTexture = null;
            }
            Graphics.Blit(target, resolved);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = resolved;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            string path = $"{ImageFolder}/{fileName}";
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            report.AppendLine($"{fileName} : {width} × {height}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        finally
        {
            target.Release();
            resolved.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(resolved);
        }
    }

    private static Vector2 Ground(Vector3 p) => new Vector2(p.x, p.z);

    private static Rect RectOf(Vector2[] points)
    {
        if (points == null || points.Length == 0) return new Rect(-30f, 0f, 60f, 60f);
        Vector2 min = points[0], max = points[0];
        foreach (Vector2 p in points)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Transform[] TransformArray(Object component, string field)
    {
        var property = new SerializedObject(component).FindProperty(field);
        if (property == null || !property.isArray) return new Transform[0];
        var list = new List<Transform>();
        for (int i = 0; i < property.arraySize; i++)
        {
            Object value = property.GetArrayElementAtIndex(i).objectReferenceValue;
            if (value is Transform t) list.Add(t);
            else if (value is Component c) list.Add(c.transform);
        }
        return list.ToArray();
    }

    private static Vector2[] Vector2Array(Object component, string field)
    {
        var property = new SerializedObject(component).FindProperty(field);
        if (property == null || !property.isArray) return new Vector2[0];
        var values = new Vector2[property.arraySize];
        for (int i = 0; i < values.Length; i++) values[i] = property.GetArrayElementAtIndex(i).vector2Value;
        return values;
    }

    private static Vector3[] Vector3Array(Object component, string field)
    {
        var property = new SerializedObject(component).FindProperty(field);
        if (property == null || !property.isArray) return new Vector3[0];
        var values = new Vector3[property.arraySize];
        for (int i = 0; i < values.Length; i++) values[i] = property.GetArrayElementAtIndex(i).vector3Value;
        return values;
    }

    private static float FloatField(Object component, string field, float fallback)
    {
        var property = new SerializedObject(component).FindProperty(field);
        return property != null ? property.floatValue : fallback;
    }
}

internal static class SceneObjectSearch
{
    /// <summary>Le premier élément qui satisfait `match`, ou null.</summary>
    public static T Find<T>(this T[] items, System.Predicate<T> match) where T : class
    {
        foreach (T item in items)
        {
            if (item != null && match(item)) return item;
        }
        return null;
    }
}
