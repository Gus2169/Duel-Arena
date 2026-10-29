using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Look = StandDecorBuilder.Look;

/// <summary>
/// Construit le stand de tir (2026-10-16) : le mannequin (préfab) et la scène StandDeTir, et ajoute la
/// scène à la Build. Rejouable : il refait tout à l'identique, comme « Construire la passerelle ». On
/// corrige ce script, pas la scène.
///
/// Le mannequin prend au préfab joueur ce qui fait qu'il est touché comme un robot : la géométrie de
/// la surface touchable (PlayerHitbox) et la hauteur de l'œil debout. Les mannequins sont posés de 5
/// à 58 m du poste de tir, d'un mur à l'autre, tournés vers lui, plus deux derrière une barricade, la
/// tête seule à découvert. Un pilier devant le premier poste sert à essayer le lean.
///
/// À droite du poste, une porte mène à la Fosse (BuildCourse) : le parcours chronométré, ses sections
/// et ses cibles (CibleParcours.prefab : le mannequin et CourseTarget). À gauche, une autre mène à la
/// salle de précision (BuildAimRoom) : la cible ronde qu'un pupitre envoie de 5 à 50 m (AimRange).
///
/// Depuis le 2026-10-28 (demandé par l'utilisateur : « la même ambiance que l'arène »), le jeu n'est fait que de
/// colliders (Span) : chacun dit ce qu'il devient (Look), et l'habillage (StandDecorBuilder, StandLighting) les
/// revêt comme la salle B-01, dans trois salles souterraines fermées : le stand (B-02), la Fosse (B-03) et la salle de
/// précision (B-04). Le même jour, le stand a été resserré (« il y a trop de pas de tir ») : 23 m de large, cinq postes.
/// </summary>
public static class ShootingRangeBuilder
{
    public const string ScenePath = "Assets/_ProjectScenes/StandDeTir.unity";
    public const string DummyPrefabPath = "Assets/_ProjectArena/Stand/Mannequin.prefab";
    public const string CourseTargetPrefabPath = "Assets/_ProjectArena/Stand/CibleParcours.prefab";
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const string IdleClipPath = "Assets/_ProjectArt/Animations/MainsNues/Y Bot@Idle.fbx";
    private const string PausePath = "Assets/_ProjectUI/Game/StandPause.uxml";
    private const string PanelSettingsPath = "Assets/_ProjectUI/Game/GamePanelSettings.asset";
    private const string BindRowPath = "Assets/_ProjectUI/MainMenu/KeyBindRow.uxml";
    private const string GameSoundsPath = "Assets/_ProjectArt/Audio/GameSounds.asset";

    /// <summary>La hauteur du plafond du stand et de la Fosse (m).</summary>
    public const float CeilingHeight = 10f;

    /// <summary>Le poste de tir où l'on apparaît (pieds) : le poste du milieu, face au pare-balles.</summary>
    public static readonly Vector3 FiringPost = new(10f, 0f, -2f);

    /// <summary>Les mannequins à découvert : (x, distance au poste de tir). Le 2026-10-28 (demandé par l'utilisateur : « ils
    /// sont tous au centre, un peu en ligne »), ils sont allés d'un mur à l'autre ; le 2026-10-29 (« rajoutes-en un peu plus
    /// au milieu »), ils sont dix-sept : cinq de chaque côté (à 2 m des murs, hors des pilastres et des distances peintes),
    /// sept au milieu (x 7 à 13, dont trois avant 25 m, deux couverts), chacun à sa distance, de 5 à 59 m, deux au moins
    /// par tranche de 10 m, à 3 m les uns des autres au moins. Placés par le calcul (une recherche par recuit) : depuis le
    /// poste du milieu, à la ligne de tir, aucune ligne de visée ne passe à moins d'un mètre d'un autre mannequin (24 cm au
    /// moins depuis les autres postes) ; ShootingRangeTests le vérifie sur la scène. Tous regardent le poste du milieu,
    /// d'où l'on tire.</summary>
    public static readonly Vector2[] OpenDummies =
    {
        new(19.5f, 5f), new(0.5f, 6f), new(3f, 8f), new(17.5f, 9f), new(13f, 11f), new(1f, 21f), new(19.5f, 23f),
        new(7.5f, 24f), new(12.5f, 25f), new(5f, 27f), new(1f, 35f), new(8f, 49f), new(17.5f, 53f), new(19.5f, 56f),
        new(10.5f, 59f),
    };

    /// <summary>Les mannequins derrière une barricade de 1,25 m : la tête seule dépasse. Une barricade cache aussi ce qui
    /// la suit de peu dans sa ligne (à 49 m, elle cachait le buste du mannequin de 54 m) : ShootingRangeTests le voit.</summary>
    public static readonly Vector2[] CoveredDummies = { new(11.5f, 39f), new(9f, 54f) };

    /// <summary>Les postes de tir : leur milieu (x), le long de la ligne de tir (z = 0). Le poste 3 est celui où l'on
    /// apparaît.</summary>
    public static readonly float[] Posts = { 5f, 7.5f, 10f, 12.5f, 15f };

    /// <summary>La salle de précision (B-04) : 5 m sous plafond, la cible au milieu (x), le comptoir de la ligne de tir (sa
    /// hauteur, sa profondeur : le pupitre devant, l'écran holographique derrière), le milieu du pupitre (x, z), où se tient
    /// le tireur (pieds), et les côtés du disque de la cible (son collider, un prisme convexe).</summary>
    public const float AimRoomHeight = 5f;
    public const float AimLaneX = -5f;
    public const float CounterHeight = 0.95f;
    public const float CounterDepth = 0.8f;
    public static readonly Vector2 ConsoleCenter = new(-6.1f, 0.275f);
    public static readonly Vector3 AimShootingSpot = new(-5f, 0f, -0.5f);
    public const int TargetSegments = 48;

    /// <summary>Les blocs du jeu construits (leur collider fait foi), et ce que chacun devient à l'habillage.</summary>
    private static readonly List<StandDecorBuilder.Block> blocks = new();

    [MenuItem("Duel Arena/Stand de tir/Construire le stand")]
    public static void Build()
    {
        // Sans boîte de dialogue (le constructeur se lance aussi d'un outil) : une scène ouverte non
        // enregistrée arrête tout, plutôt que de perdre son travail.
        Scene previous = SceneManager.GetActiveScene();
        if (previous.isDirty)
        {
            Debug.LogWarning("[Stand] La scène ouverte n'est pas enregistrée : enregistre-la, puis reconstruis le stand.");
            return;
        }
        string previousPath = previous.path;
        GameObject dummyPrefab = BuildDummyPrefab(DummyPrefabPath, false);
        GameObject coursePrefab = BuildDummyPrefab(CourseTargetPrefabPath, true);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // La scène a son chemin dès le début : l'habillage range ses maillages, sa lumière et ses sondes à côté d'elle.
        EditorSceneManager.SaveScene(scene, ScenePath);
        blocks.Clear();
        BuildRange();

        var dummies = new GameObject("Mannequins").transform;
        int index = 0;
        foreach (Vector2 p in OpenDummies) PlaceDummy(dummyPrefab, dummies, p, ++index);
        foreach (Vector2 p in CoveredDummies) PlaceDummy(dummyPrefab, dummies, p, ++index);

        var spawns = new GameObject("SpawnPoints");
        var spawn = new GameObject("Poste de tir").transform;
        spawn.SetParent(spawns.transform, false);
        spawn.position = FiringPost;
        var points = spawns.AddComponent<PlayerSpawnPoints>();
        var so = new SerializedObject(points);
        SerializedProperty list = so.FindProperty("spawnPoints");
        list.arraySize = 1;
        list.GetArrayElementAtIndex(0).objectReferenceValue = spawn;
        so.ApplyModifiedPropertiesWithoutUndo();

        var range = new GameObject("StandDeTir");
        range.AddComponent<NetworkObject>();
        range.AddComponent<ShootingRange>();
        range.AddComponent<ShootingRangeHud>();
        range.AddComponent<ShootingRangePanel>();
        range.AddComponent<ShootingRangeBootstrap>();
        BuildInterface();

        BuildCourse(coursePrefab);
        BuildAimRoom();

        // L'habillage et la lumière (la salle B-02 et la Fosse) ; la lumière indirecte se cuit à part (Cuire la lumière).
        Physics.SyncTransforms();
        string report = StandDecorBuilder.Dress(scene, blocks, DecorBankPath);
        EditorSceneManager.SaveScene(scene, ScenePath);

        // 🚨 Netcode calcule l'identifiant d'un objet réseau de scène (GlobalObjectIdHash) d'après la
        // scène ENREGISTRÉE : calculé avant, il valait celui du préfab pour tous les mannequins, et le
        // chargement du stand échouait (« ScenePlacedObjects already contains the same
        // GlobalObjectIdHash »). On le recalcule une fois la scène sur le disque, et on réenregistre.
        scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        System.Reflection.MethodInfo validate = typeof(NetworkObject).GetMethod("OnValidate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (NetworkObject networkObject in root.GetComponentsInChildren<NetworkObject>(true))
        {
            validate?.Invoke(networkObject, null);
            EditorUtility.SetDirty(networkObject);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AddToBuild(ScenePath);
        Debug.Log($"[Stand] Construit : {ScenePath}, {OpenDummies.Length + CoveredDummies.Length} mannequins, la Fosse et ses {CourseTargets.Length} cibles, la salle de précision.\n{report}");
        if (!string.IsNullOrEmpty(previousPath) && previousPath != ScenePath) EditorSceneManager.OpenScene(previousPath);
    }

    /// <summary>Cuit la lumière indirecte du stand (quelques minutes, en tâche de fond), puis ses sondes de reflets, et
    /// enregistre la scène. À refaire après chaque construction.</summary>
    [MenuItem("Duel Arena/Stand de tir/Cuire la lumière du stand")]
    public static void Bake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Stand] Sortir du Play Mode d'abord.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (scene.isDirty)
            {
                Debug.LogWarning("[Stand] La scène ouverte n'est pas enregistrée : enregistre-la d'abord.");
                return;
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        if (!Lightmapping.BakeAsync()) Debug.LogWarning("[Stand] La cuisson n'a pas démarré.");
        else Lightmapping.bakeCompleted += AfterBake;
    }

    private static void AfterBake()
    {
        Lightmapping.bakeCompleted -= AfterBake;
        foreach (ReflectionProbe probe in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude))
        {
            if (probe.mode == UnityEngine.Rendering.ReflectionProbeMode.Baked && probe.bakedTexture != null)
                Lightmapping.BakeReflectionProbe(probe, AssetDatabase.GetAssetPath(probe.bakedTexture));
        }
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path == ScenePath) EditorSceneManager.SaveScene(scene);
        Debug.Log("[Stand] Lumière cuite.");
    }

    /// <summary>Les maillages de l'habillage, à côté de la scène (en LFS : *_Decor.asset).</summary>
    public const string DecorBankPath = "Assets/_ProjectScenes/StandDeTir_Decor.asset";

    // ------------------------------------------------------------------------------------------------
    // Le stand (la salle B-02) : 23 × 75 m (x -1,5 à 21,5), cinq postes de tir sous un auvent, le pare-balles au fond.
    // ------------------------------------------------------------------------------------------------

    private static void BuildRange()
    {
        var level = new GameObject("Stand").transform;
        const float h = CeilingHeight;
        Rect room = StandDecorBuilder.StandRoom;
        float x0 = room.xMin, x1 = room.xMax;
        Span("Sol", level, new Vector3(x0 - 1f, -1f, -10f), new Vector3(x1 + 0.5f, 0f, 66f), Look.Dalle);
        Span("Mur du fond", level, new Vector3(x0, 0f, 65.5f), new Vector3(x1, h, 66.5f), Look.Mur);
        Span("Mur arrière", level, new Vector3(x0, 0f, -10.5f), new Vector3(x1, h, -9.5f), Look.Mur);
        // Les murs de côté s'ouvrent près du poste de tir : à gauche sur la salle de précision, à droite sur la Fosse (deux
        // portes de 3 m de large et de haut). Celui de gauche est aussi le mur de droite de la salle de précision.
        foreach ((float a, float b, string side) in new[] { (x0 - 1f, x0, "gauche"), (x1, x1 + 1f, "droit") })
        {
            Span($"Mur {side} (sud)", level, new Vector3(a, 0f, -10.5f), new Vector3(b, h, -7.5f), Look.Mur);
            Span($"Mur {side} (nord)", level, new Vector3(a, 0f, -4.5f), new Vector3(b, h, 66.5f), Look.Mur);
            Span($"Mur {side} (linteau)", level, new Vector3(a, 3f, -7.5f), new Vector3(b, h, -4.5f), Look.Mur);
        }
        Span("Plafond", level, new Vector3(x0 - 1f, h, -10.5f), new Vector3(x1 + 1f, h + 1f, 66.5f), Look.Plafond);

        // Des pilastres le long des grands murs, tous les 8 m.
        for (float z = 6f; z <= 62f; z += 8f)
        {
            Span("Pilastre", level, new Vector3(x0, 0f, z - 0.5f), new Vector3(x0 + 0.5f, h, z + 0.5f), Look.Mur);
            Span("Pilastre", level, new Vector3(x1 - 0.5f, 0f, z - 0.5f), new Vector3(x1, h, z + 0.5f), Look.Mur);
        }

        // Le pare-balles, au fond : ses lames commencent à 64 m de la ligne de tir.
        Span("Pare-balles", level, new Vector3(x0, 0f, 64f), new Vector3(x1, 6f, 65.5f), Look.PareBalles);

        // Les postes de tir : six cloisons de part et d'autre des cinq postes, et l'auvent au-dessus d'eux.
        foreach (float x in Posts.Select(p => p - 1.25f).Append(Posts[Posts.Length - 1] + 1.25f))
            Span("Cloison de poste", level, new Vector3(x - 0.05f, 0f, -1.5f), new Vector3(x + 0.05f, 1.9f, 0f), Look.Cloison);
        Span("Auvent", level, new Vector3(x0, 3.6f, -9.5f), new Vector3(x1, 4f, -0.5f), Look.Auvent);

        // Un pilier devant le premier poste, pour le lean ; une barricade devant chaque mannequin couvert.
        Span("Pilier (lean)", level, new Vector3(Posts[0] - 0.5f, 0f, 0.7f), new Vector3(Posts[0] + 0.5f, 2.6f, 1.7f), Look.Barricade);
        foreach (Vector2 d in CoveredDummies)
            Span("Barricade", level, new Vector3(d.x - 1.2f, 0f, d.y - 1.15f), new Vector3(d.x + 1.2f, 1.25f, d.y - 0.65f), Look.Barricade);

        // Le coin d'attente, derrière les postes : des caisses de munitions dans le coin, l'établi, l'étagère.
        Span("Caisse", level, new Vector3(x0, 0f, -9.5f), new Vector3(x0 + 1f, 1f, -8.5f), Look.Caisse);
        Span("Caisse", level, new Vector3(x0 + 1.1f, 0f, -9.5f), new Vector3(x0 + 2.1f, 1f, -8.5f), Look.Caisse);
        Span("Caisse", level, new Vector3(x0, 1f, -9.5f), new Vector3(x0 + 1f, 2f, -8.5f), Look.Caisse);
        Span("Établi", level, new Vector3(3f, 0f, -9.5f), new Vector3(5f, 1.05f, -8.7f), Look.Etabli);
        Span("Étagère", level, new Vector3(15.5f, 0f, -9.5f), new Vector3(18.5f, 2f, -8.9f), Look.Etagere);
    }

    // ------------------------------------------------------------------------------------------------
    // La salle de précision (la salle B-04, 2026-10-28, demandée par l'utilisateur d'après le « shooting range » de
    // Rainbow Six) : 5 × 65 m à gauche du stand (x -7,5 à -2,5), 5 m sous plafond ; à la ligne de tir, un comptoir et son
    // pupitre ; au fond, le pare-balles ; la cible ronde roule sur son chariot jusqu'à 50 m (AimRange).
    // ------------------------------------------------------------------------------------------------

    private static void BuildAimRoom()
    {
        var level = new GameObject("Salle de précision (décor)").transform;
        Rect room = StandDecorBuilder.AimRoom;
        const float h = AimRoomHeight;
        Span("Sol", level, new Vector3(room.xMin - 1f, -1f, -10f), new Vector3(room.xMax, 0f, room.yMax + 1f), Look.Dalle);
        Span("Mur gauche", level, new Vector3(room.xMin - 1f, 0f, room.yMin - 1f), new Vector3(room.xMin, h, room.yMax + 1f), Look.Mur);
        Span("Mur arrière", level, new Vector3(room.xMin, 0f, room.yMin - 1f), new Vector3(room.xMax, h, room.yMin), Look.Mur);
        Span("Mur du fond", level, new Vector3(room.xMin, 0f, room.yMax), new Vector3(room.xMax, h, room.yMax + 1f), Look.Mur);
        Span("Plafond", level, new Vector3(room.xMin - 1f, h, room.yMin - 1f), new Vector3(room.xMax, h + 1f, room.yMax + 1f), Look.Plafond);
        for (float z = 6f; z <= 46f; z += 8f)
        {
            Span("Pilastre", level, new Vector3(room.xMin, 0f, z - 0.4f), new Vector3(room.xMin + 0.4f, h, z + 0.4f), Look.Mur);
            Span("Pilastre", level, new Vector3(room.xMax - 0.4f, 0f, z - 0.4f), new Vector3(room.xMax, h, z + 0.4f), Look.Mur);
        }
        Span("Pare-balles", level, new Vector3(room.xMin, 0f, room.yMax - 1.5f), new Vector3(room.xMax, 4f, room.yMax), Look.PareBalles);
        // Le comptoir de la ligne de tir (z = 0) : on tire par-dessus, debout comme accroupi (l'œil à 1,05 m). Le pupitre est
        // devant, l'écran holographique derrière lui.
        Span("Comptoir", level, new Vector3(room.xMin, 0f, 0f), new Vector3(room.xMax, CounterHeight, CounterDepth), Look.Comptoir);
        // Le coin d'attente : une étagère contre le mur, une caisse dans le coin, un banc.
        Span("Étagère", level, new Vector3(-6.4f, 0f, room.yMin), new Vector3(-3.4f, 2f, room.yMin + 0.6f), Look.Etagere);
        Span("Caisse", level, new Vector3(room.xMin, 0f, room.yMin), new Vector3(room.xMin + 1f, 1f, room.yMin + 1f), Look.Caisse);
        Span("Banc", level, new Vector3(room.xMin, 0f, -6.4f), new Vector3(room.xMin + 0.45f, 0.45f, -4.4f), Look.Acier);

        // La cible, son chariot et son pupitre (AimRange) ; StandDecorBuilder les habille.
        var range = new GameObject("Salle de précision");
        range.transform.position = new Vector3(AimLaneX, 0f, 0f);
        var aim = range.AddComponent<AimRange>();
        var carrier = new GameObject("Chariot");
        carrier.transform.SetParent(range.transform, false);
        var body = carrier.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        // Ses pièces arrêtent les balles comme le décor (layer Default) ; la face de la cible seule en garde la marque. Le
        // socle et le mât sont derrière le disque.
        CarrierPart("Socle", carrier.transform, new Vector3(-0.22f, 0f, 0.05f), new Vector3(0.22f, 0.16f, 0.55f));
        CarrierPart("Mât", carrier.transform, new Vector3(-0.04f, 0.16f, AimRange.BoardThickness + 0.005f),
                    new Vector3(0.04f, AimRange.BoardCenterHeight, AimRange.BoardThickness + 0.085f));
        // Le disque : un prisme convexe inscrit dans le cercle (un BoxCollider arrêterait les balles dans ses coins, hors du
        // disque), sa face au z = 0 du chariot.
        var board = new GameObject("Cible").transform;
        board.SetParent(carrier.transform, false);
        board.localPosition = new Vector3(0f, AimRange.BoardCenterHeight, AimRange.BoardThickness * 0.5f);
        var disc = board.gameObject.AddComponent<MeshCollider>();
        disc.sharedMesh = DiscColliderMesh(AimRange.BoardRadius, AimRange.BoardThickness, TargetSegments);
        disc.convex = true;
        var console = new GameObject("Pupitre").transform;
        console.SetParent(range.transform, false);
        // La plaque du pupitre (1 cm) sur le comptoir ; son plan est celui des boutons, 2 mm au-dessus.
        console.SetPositionAndRotation(new Vector3(ConsoleCenter.x, CounterHeight + 0.012f, ConsoleCenter.y), Quaternion.LookRotation(Vector3.down, Vector3.forward));

        var so = new SerializedObject(aim);
        so.FindProperty("carrier").objectReferenceValue = carrier.transform;
        so.FindProperty("board").objectReferenceValue = board;
        so.FindProperty("console").objectReferenceValue = console;
        so.FindProperty("room").boundsValue = Box3(new Vector3(room.xMin, -1f, room.yMin), new Vector3(room.xMax, h, room.yMax));
        so.FindProperty("distance").intValue = 10;
        so.FindProperty("sounds").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameSounds>(GameSoundsPath);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Le collider du disque de la cible : un prisme à `segments` côtés inscrit dans le cercle de `radius`, épais de
    /// `thickness`, centré (sa face au -Z). Enregistré avec la scène.</summary>
    public static Mesh DiscColliderMesh(float radius, float thickness, int segments)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        float h = thickness * 0.5f;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            var p = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
            vertices.Add(p + Vector3.back * h);
            vertices.Add(p + Vector3.forward * h);
        }
        for (int i = 0; i < segments; i++)
        {
            int j = (i + 1) % segments;
            triangles.AddRange(new[] { 2 * i, 2 * j, 2 * j + 1, 2 * i, 2 * j + 1, 2 * i + 1 });
            if (i > 0 && j > 0)
            {
                triangles.AddRange(new[] { 0, 2 * j, 2 * i });
                triangles.AddRange(new[] { 1, 2 * i + 1, 2 * j + 1 });
            }
        }
        var mesh = new Mesh { name = "Cible de précision (collider)" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Une pièce du chariot de la cible, de `min` à `max` (dans le repère du chariot) : un BoxCollider sur le layer
    /// Default, qui bouge avec lui (ni statique, ni bloc du décor).</summary>
    private static GameObject CarrierPart(string name, Transform carrier, Vector3 min, Vector3 max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(carrier, false);
        go.transform.localPosition = (min + max) * 0.5f;
        go.AddComponent<BoxCollider>().size = max - min;
        return go;
    }

    // ------------------------------------------------------------------------------------------------
    // La Fosse (2026-10-16, refaite le 2026-10-28, « plus complexe et plus habillée », demandé par l'utilisateur) : la
    // salle B-03, 39 × 59 m à droite du stand (x 22,5 à 61,5 ; z -9,5 à 49,5), un centre d'entraînement rapproché
    // (bible § 13). On en fait le tour, du départ (la porte du stand) :
    //   la cour d'essai et sa barricade à franchir → le quai et ses conteneurs → l'allée, sa gaine à passer accroupi et
    //   sa cible qui va et vient → l'atelier (deux pièces, en U autour d'une cloison) → la réserve → les bureaux (un
    //   couloir, deux pièces) → la cour de service → l'escalier, et sa cible au taser → le toit → la passerelle, au-dessus
    //   de la cour ouest, et son escalier → la cour ouest → le couloir du retour, en chicane, et sa dernière barricade →
    //   l'arrivée, au départ.
    // ------------------------------------------------------------------------------------------------

    private static readonly Color HostileTint = new Color(0.78f, 0.2f, 0.16f, 1f);
    private static readonly Color CivilTint = new Color(0.93f, 0.94f, 0.97f, 1f);

    /// <summary>La zone de départ (et d'arrivée) : entre la porte du stand et la ligne de départ.</summary>
    public static readonly Bounds CourseStartZone = Box3(new Vector3(21f, -1f, -9.5f), new Vector3(30f, 2.5f, -3f));

    /// <summary>Les sections, dans l'ordre du parcours : entrer dans l'une lève ses cibles. Une boîte sur la position des
    /// pieds : au sol, jusqu'à 2,5 m (ou 2,2 m sous la passerelle) ; sur le toit et la passerelle, au-dessus.</summary>
    public static readonly (string name, Bounds bounds)[] CourseSections =
    {
        ("Cour d'essai", Box3(new Vector3(30f, -1f, -9.5f), new Vector3(42f, 2.5f, 8f))),
        ("Quai", Box3(new Vector3(42.6f, -1f, -9.5f), new Vector3(61.5f, 2.5f, 8f))),
        ("Allée", Box3(new Vector3(54f, -1f, 8f), new Vector3(61.5f, 2.5f, 22f))),
        ("Atelier", Box3(new Vector3(38f, -1f, 8f), new Vector3(54f, 2.5f, 22f))),
        ("Réserve", Box3(new Vector3(30f, -1f, 8f), new Vector3(38f, 2.5f, 22f))),
        ("Bureaux", Box3(new Vector3(34f, -1f, 22f), new Vector3(54f, 2.5f, 40f))),
        ("Cour de service", Box3(new Vector3(54f, -1f, 22f), new Vector3(61.5f, 2.5f, 40f))),
        ("Escalier", Box3(new Vector3(44f, -1f, 40f), new Vector3(61.5f, 5f, 49.5f))),
        ("Toit", Box3(new Vector3(34f, 2.5f, 22f), new Vector3(54f, 6.5f, 40f))),
        ("Passerelle", Box3(new Vector3(22.5f, 2.2f, 28.5f), new Vector3(34f, 6.5f, 40f))),
        ("Cour ouest", Box3(new Vector3(22.5f, -1f, 22f), new Vector3(34f, 2.2f, 49.5f))),
        ("Couloir du retour", Box3(new Vector3(22.5f, -1f, -3f), new Vector3(30f, 2.5f, 22f))),
    };

    /// <summary>Une cible : sa section, sa place (pieds), le point qu'elle regarde (x, z), civile ou
    /// non, et le bout de son rail si elle bouge.</summary>
    public struct CourseTargetSpec
    {
        public int section;
        public Vector3 position;
        public Vector2 lookAt;
        public bool civil;
        public bool moves;
        public Vector3 railEnd;
    }

    private static CourseTargetSpec H(int section, float x, float y, float z, float lx, float lz)
        => new CourseTargetSpec { section = section, position = new Vector3(x, y, z), lookAt = new Vector2(lx, lz) };

    private static CourseTargetSpec C(int section, float x, float y, float z, float lx, float lz)
        => new CourseTargetSpec { section = section, position = new Vector3(x, y, z), lookAt = new Vector2(lx, lz), civil = true };

    private static CourseTargetSpec Rail(CourseTargetSpec spec, Vector3 end)
    {
        spec.moves = true;
        spec.railEnd = end;
        return spec;
    }

    /// <summary>Les cibles de la Fosse : 33 hostiles et 14 civils, aux couleurs de la Fosse de Modern Warfare 2 (rouges
    /// et blancs), qui ont la vie d'un robot. Plusieurs se voient d'en haut : celles de la cour ouest se lèvent quand on
    /// arrive sur le toit, puis sur la passerelle.</summary>
    public static readonly CourseTargetSpec[] CourseTargets =
    {
        // 0. La cour d'essai : trois hostiles dès la ligne franchie, un civil.
        H(0, 36.0f, 0f, 5.6f, 30.5f, -6f),
        H(0, 40.4f, 0f, -7.6f, 30.5f, -6f),
        H(0, 40.9f, 0f, 1.8f, 31f, -6f),
        C(0, 37.2f, 0f, 6.8f, 30.5f, -6f),
        // 1. Le quai : un hostile qui va et vient le long du mur, un derrière un conteneur, un perché sur la pile du fond.
        Rail(H(1, 45.6f, 0f, 6.6f, 43f, 0f), new Vector3(51.6f, 0f, 6.6f)),
        H(1, 53.4f, 0f, -8.6f, 43f, 0f),
        H(1, 59.0f, 4f, -8.5f, 45f, 2f),
        C(1, 56.8f, 0f, -2.0f, 43f, 0f),
        // 2. L'allée : un hostile avant la gaine, un qui va et vient au fond, un après la gaine, un civil.
        H(2, 56.0f, 0f, 10.8f, 58f, 8f),
        Rail(H(2, 55.2f, 0f, 21.0f, 58f, 9f), new Vector3(60.8f, 0f, 21.0f)),
        H(2, 60.6f, 0f, 17.2f, 58f, 9f),
        C(2, 60.4f, 0f, 10.4f, 58f, 8f),
        // 3. L'atelier : deux hostiles et un civil dans la première pièce, deux hostiles et un civil autour de la cloison.
        H(3, 47.4f, 0f, 20.6f, 53f, 19f),
        H(3, 47.4f, 0f, 13.4f, 53f, 19f),
        C(3, 52.6f, 0f, 9.4f, 53f, 19f),
        H(3, 44.6f, 0f, 20.6f, 44f, 9f),
        H(3, 39.4f, 0f, 20.6f, 44f, 19f),
        C(3, 40.6f, 0f, 12.6f, 40f, 19f),
        // 4. La réserve : deux hostiles, un civil.
        H(4, 31.6f, 0f, 9.4f, 37f, 10f),
        H(4, 33.2f, 0f, 20.8f, 37f, 10f),
        C(4, 32.0f, 0f, 18.4f, 37f, 10f),
        // 5. Les bureaux : la pièce ouest, le bout du couloir, la pièce est.
        H(5, 42.8f, 0f, 31.6f, 36f, 23f),
        H(5, 35.0f, 0f, 33.0f, 38f, 23f),
        C(5, 40.2f, 0f, 24.2f, 36f, 23f),
        H(5, 53.0f, 0f, 37.2f, 42f, 37f),
        H(5, 45.0f, 0f, 23.0f, 49.2f, 33f),
        C(5, 52.8f, 0f, 31.0f, 49.2f, 33f),
        // 6. La cour de service : deux hostiles, un civil.
        H(6, 60.6f, 0f, 24.0f, 55f, 27f),
        H(6, 60.4f, 0f, 38.2f, 56f, 28f),
        C(6, 58.0f, 0f, 28.2f, 55f, 27f),
        // 7. Le haut de l'escalier : un hostile au contact, pour le taser (le couteau de la Fosse).
        H(7, 48.6f, 3f, 43.4f, 52f, 43.4f),
        // 8. Le toit : deux hostiles aux coins, et dans la cour ouest, en contrebas, un hostile et un civil.
        H(8, 35.4f, 3f, 23.4f, 47f, 40f),
        H(8, 52.6f, 3f, 23.4f, 47f, 40f),
        H(8, 25.4f, 0f, 46.4f, 34f, 31f),
        C(8, 28.2f, 0f, 37.6f, 34f, 31f),
        // 9. La passerelle : sous elle, deux hostiles au sud de la cour, un civil au nord.
        H(9, 30.6f, 0f, 25.0f, 29f, 31f),
        H(9, 24.0f, 0f, 26.0f, 29f, 31f),
        C(9, 31.6f, 0f, 45.6f, 28f, 31f),
        // 10. La cour ouest, au pied de l'escalier : trois hostiles, deux civils.
        H(10, 29.4f, 0f, 47.6f, 24f, 40f),
        H(10, 33.0f, 0f, 38.0f, 24f, 40f),
        H(10, 26.4f, 0f, 23.0f, 28f, 35f),
        C(10, 25.6f, 0f, 44.6f, 24f, 40f),
        C(10, 32.6f, 0f, 23.6f, 28f, 35f),
        // 11. Le couloir du retour : un hostile par passage de la chicane, un civil, et l'arrivée.
        H(11, 23.4f, 0f, 19.4f, 27f, 22f),
        H(11, 29.0f, 0f, 9.6f, 24f, 13f),
        C(11, 23.4f, 0f, 9.6f, 27f, 13f),
        H(11, 24.0f, 0f, -0.2f, 28f, 2f),
    };

    /// <summary>Comment on passe d'un point du parcours au suivant : en marchant, accroupi, ou en franchissant.</summary>
    public enum RouteMove
    {
        Walk,
        Crouch,
        Vault,
    }

    /// <summary>Un pas du parcours : le point à atteindre (pieds), et comment.</summary>
    public readonly struct RouteStep
    {
        public readonly Vector3 point;
        public readonly RouteMove move;

        public RouteStep(float x, float y, float z, RouteMove move = RouteMove.Walk)
        {
            point = new Vector3(x, y, z);
            this.move = move;
        }
    }

    /// <summary>Le tour de la Fosse à pied, du départ à l'arrivée : les tests le suivent avec la capsule d'un robot
    /// (ShootingCourseTests), qui doit passer partout et traverser les sections dans l'ordre.</summary>
    public static readonly RouteStep[] CourseRoute =
    {
        new RouteStep(26.0f, 0f, -6.5f),
        new RouteStep(31.0f, 0f, -6.0f),
        new RouteStep(36.0f, 0f, -2.0f),
        new RouteStep(41.2f, 0f, 0.0f),
        new RouteStep(43.4f, 0f, 0.0f, RouteMove.Vault),
        new RouteStep(44.5f, 0f, 0.0f),
        new RouteStep(49.0f, 0f, -1.5f),
        new RouteStep(53.0f, 0f, 0.0f),
        new RouteStep(57.6f, 0f, 2.0f),
        new RouteStep(57.75f, 0f, 9.5f),
        new RouteStep(57.75f, 0f, 12.2f),
        new RouteStep(57.75f, 0f, 14.4f, RouteMove.Crouch),
        new RouteStep(57.0f, 0f, 19.2f),
        new RouteStep(55.2f, 0f, 19.2f),
        new RouteStep(52.8f, 0f, 19.2f),
        new RouteStep(48.0f, 0f, 16.0f),
        new RouteStep(47.2f, 0f, 10.0f),
        new RouteStep(44.8f, 0f, 10.0f),
        new RouteStep(44.0f, 0f, 19.2f),
        new RouteStep(40.0f, 0f, 19.2f),
        new RouteStep(40.0f, 0f, 10.0f),
        new RouteStep(36.6f, 0f, 10.0f),
        new RouteStep(35.8f, 0f, 16.0f),
        new RouteStep(35.8f, 0f, 20.6f),
        new RouteStep(35.8f, 0f, 23.4f),
        new RouteStep(35.6f, 0f, 29.0f),
        new RouteStep(42.4f, 0f, 32.4f),
        new RouteStep(42.4f, 0f, 36.6f),
        new RouteStep(49.2f, 0f, 36.6f),
        new RouteStep(49.2f, 0f, 31.6f),
        new RouteStep(52.2f, 0f, 27.0f),
        new RouteStep(55.6f, 0f, 27.0f),
        new RouteStep(57.2f, 0f, 33.8f),
        new RouteStep(57.2f, 0f, 39.0f),
        new RouteStep(58.0f, 0f, 42.4f),
        new RouteStep(56.0f, 0f, 42.4f),
        new RouteStep(49.0f, 3f, 42.4f),
        new RouteStep(46.6f, 3f, 41.6f),
        new RouteStep(46.6f, 3f, 38.6f),
        new RouteStep(36.0f, 3f, 30.8f),
        new RouteStep(35.0f, 3f, 30.8f),
        new RouteStep(33.0f, 3f, 30.8f),
        new RouteStep(26.0f, 3f, 30.8f),
        new RouteStep(23.8f, 3f, 31.2f),
        new RouteStep(23.75f, 3f, 33.2f),
        new RouteStep(23.75f, 0f, 40.7f),
        new RouteStep(26.0f, 0f, 40.7f),
        new RouteStep(26.2f, 0f, 33.0f),
        new RouteStep(26.2f, 0f, 24.0f),
        new RouteStep(26.2f, 0f, 20.6f),
        new RouteStep(28.2f, 0f, 15.0f),
        new RouteStep(24.2f, 0f, 8.0f),
        new RouteStep(28.2f, 0f, 1.0f),
        new RouteStep(26.0f, 0f, -0.6f),
        new RouteStep(26.0f, 0f, -2.5f, RouteMove.Vault),
        new RouteStep(26.0f, 0f, -5.5f),
    };

    /// <summary>Les lampes des bureaux (sous le toit) : StandLighting y met une lumière tiède et sa dalle lumineuse.</summary>
    public static readonly Vector3[] OfficeLamps =
    {
        new Vector3(38.5f, 2.62f, 27.5f), new Vector3(38.5f, 2.62f, 37f), new Vector3(48.5f, 2.62f, 37f), new Vector3(48.5f, 2.62f, 27.5f),
    };

    private static Bounds Box3(Vector3 min, Vector3 max)
    {
        var b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    /// <summary>Un mur de 40 cm sur la ligne x = `x`, de z0 à z1, haut de `h`.</summary>
    private static void WallX(string name, Transform parent, float x, float z0, float z1, float h, Look look)
        => Span(name, parent, new Vector3(x - 0.2f, 0f, z0), new Vector3(x + 0.2f, h, z1), look);

    /// <summary>Un mur de 40 cm sur la ligne z = `z`, de x0 à x1, haut de `h`.</summary>
    private static void WallZ(string name, Transform parent, float z, float x0, float x1, float h, Look look)
        => Span(name, parent, new Vector3(x0, 0f, z - 0.2f), new Vector3(x1, h, z + 0.2f), look);

    private static void BuildCourse(GameObject targetPrefab)
    {
        var level = new GameObject("La Fosse (décor)").transform;
        const float h = CeilingHeight, module = 3f, house = 2.7f, roof = 3f;

        // La salle (le mur ouest est celui du stand, percé de la porte), son sol, son plafond.
        Span("Sol", level, new Vector3(22f, -1f, -10f), new Vector3(62f, 0f, 50f), Look.Dalle);
        Span("Mur sud", level, new Vector3(22.5f, 0f, -10.5f), new Vector3(62.5f, h, -9.5f), Look.Mur);
        Span("Mur est", level, new Vector3(61.5f, 0f, -10.5f), new Vector3(62.5f, h, 50.5f), Look.Mur);
        Span("Mur nord", level, new Vector3(22.5f, 0f, 49.5f), new Vector3(61.5f, h, 50.5f), Look.Mur);
        Span("Plafond", level, new Vector3(22.5f, h, -10.5f), new Vector3(62.5f, h + 1f, 50.5f), Look.Plafond);

        // Le départ et le couloir du retour, séparés de la cour d'essai et de la réserve par un mur.
        WallX("Mur du couloir", level, 30f, -3f, 22f, module, Look.BetonSombre);

        // 0. La cour d'essai : un muret, des caisses, un conteneur, et la barricade de 1 m qu'on franchit.
        Span("Muret", level, new Vector3(33.4f, 0f, 0f), new Vector3(34f, 0.9f, 2.4f), Look.Barricade);
        Span("Caisse", level, new Vector3(37.5f, 0f, -9.5f), new Vector3(38.5f, 1f, -8.5f), Look.Caisse);
        Span("Caisse", level, new Vector3(38.6f, 0f, -9.5f), new Vector3(39.6f, 1f, -8.5f), Look.Caisse);
        Span("Conteneur", level, new Vector3(38f, 0f, 3.4f), new Vector3(40f, 2f, 5.4f), Look.Caisse);
        Span("Barricade à franchir", level, new Vector3(42f, 0f, -9.5f), new Vector3(42.6f, 1f, 7.8f), Look.Barricade);

        // L'atelier (x 30 à 54, z 8 à 22), sans toit : ses murs de modules, au sud la cour et le quai, à l'est l'allée.
        WallZ("Atelier, mur sud", level, 8f, 30.2f, 54.2f, module, Look.Module);
        WallX("Atelier, mur est (sud)", level, 54f, 8.2f, 18.4f, module, Look.Module);
        WallX("Atelier, mur est (nord)", level, 54f, 20f, 21.8f, module, Look.Module);
        Span("Atelier, linteau est", level, new Vector3(53.8f, 2.3f, 18.4f), new Vector3(54.2f, module, 20f), Look.Module);

        // 1. Le quai : des conteneurs (une pile de deux au fond) et des caisses.
        Span("Conteneur", level, new Vector3(46f, 0f, 1f), new Vector3(48f, 2f, 3f), Look.Caisse);
        Span("Conteneur", level, new Vector3(50f, 0f, -7.5f), new Vector3(52f, 2f, -5.5f), Look.Caisse);
        Span("Conteneur", level, new Vector3(58f, 0f, -9.5f), new Vector3(60f, 2f, -7.5f), Look.Caisse);
        Span("Conteneur", level, new Vector3(58f, 2f, -9.5f), new Vector3(60f, 4f, -7.5f), Look.Caisse);
        Span("Caisse", level, new Vector3(54.4f, 0f, 3f), new Vector3(55.4f, 1f, 4f), Look.Caisse);
        Span("Caisse", level, new Vector3(55.5f, 0f, 3f), new Vector3(56.5f, 1f, 4f), Look.Caisse);

        // 2. L'allée, fermée au nord : la gaine en travers, qu'on passe accroupi (1,45 m de passage : debout, la tête
        // d'un robot passe à 1,88 m ; accroupi, à 1,18 m), puis la porte de l'atelier.
        WallZ("Allée, mur nord", level, 22f, 54.2f, 61.5f, module, Look.Module);
        Span("Gaine", level, new Vector3(54.2f, 1.45f, 13f), new Vector3(61.5f, 2.05f, 13.6f), Look.Gaine);

        // 3. L'atelier : la première pièce (x 46 à 54), son établi et son étagère ; la porte du sud-ouest ; la seconde
        // pièce (x 38 à 46) et sa cloison, qu'on contourne par le nord ; la porte du sud-ouest vers la réserve.
        Span("Établi", level, new Vector3(49f, 0f, 13f), new Vector3(51f, 1.05f, 13.8f), Look.Etabli);
        Span("Étagère", level, new Vector3(53.2f, 0f, 14.5f), new Vector3(53.8f, 2f, 17.5f), Look.Etagere);
        WallX("Atelier, mur 1-2 (sud)", level, 46f, 8.2f, 9.2f, module, Look.Module);
        WallX("Atelier, mur 1-2 (nord)", level, 46f, 10.8f, 21.8f, module, Look.Module);
        Span("Atelier, linteau 1-2", level, new Vector3(45.8f, 2.3f, 9.2f), new Vector3(46.2f, module, 10.8f), Look.Module);
        WallX("Atelier, cloison", level, 42f, 8.2f, 17f, module, Look.Module);
        Span("Caisse", level, new Vector3(44.6f, 0f, 8.2f), new Vector3(45.6f, 1f, 9.2f), Look.Caisse);
        WallX("Atelier, mur 2-3 (sud)", level, 38f, 8.2f, 9.2f, module, Look.Module);
        WallX("Atelier, mur 2-3 (nord)", level, 38f, 10.8f, 21.8f, module, Look.Module);
        Span("Atelier, linteau 2-3", level, new Vector3(37.8f, 2.3f, 9.2f), new Vector3(38.2f, module, 10.8f), Look.Module);

        // 4. La réserve (x 30 à 38) : des étagères, une caisse ; au nord, le mur qui la sépare de la cour ouest, et la
        // porte des bureaux.
        Span("Étagère", level, new Vector3(30.2f, 0f, 12.5f), new Vector3(30.8f, 2f, 15.5f), Look.Etagere);
        Span("Caisse", level, new Vector3(36.8f, 0f, 14.4f), new Vector3(37.8f, 1f, 15.4f), Look.Caisse);
        WallZ("Mur sud de la cour", level, 22f, 30.2f, 33.8f, module, Look.Module);

        // 5. Les bureaux (x 34 à 54, z 22 à 40) : sous un toit, la pièce ouest, le couloir du nord, la pièce est ; on
        // entre par la porte sud, on sort par la porte est.
        WallZ("Bureaux, mur sud (ouest)", level, 22f, 33.8f, 35f, house, Look.Beton);
        WallZ("Bureaux, mur sud (est)", level, 22f, 36.6f, 54.2f, house, Look.Beton);
        Span("Bureaux, linteau sud", level, new Vector3(35f, 2.2f, 21.8f), new Vector3(36.6f, house, 22.2f), Look.Beton);
        WallZ("Bureaux, mur nord", level, 40f, 33.8f, 54.2f, house, Look.Beton);
        WallX("Bureaux, mur ouest", level, 34f, 22.2f, 39.8f, house, Look.Beton);
        WallX("Bureaux, mur est (sud)", level, 54f, 22.2f, 26f, house, Look.Beton);
        WallX("Bureaux, mur est (nord)", level, 54f, 28f, 39.8f, house, Look.Beton);
        Span("Bureaux, linteau est", level, new Vector3(53.8f, 2.2f, 26f), new Vector3(54.2f, house, 28f), Look.Beton);
        WallZ("Bureaux, cloison du couloir (ouest)", level, 34f, 34.2f, 41f, house, Look.Beton);
        WallX("Bureaux, cloison", level, 44f, 22.2f, 34.2f, house, Look.Beton);
        WallZ("Bureaux, cloison du couloir (est, 1)", level, 34f, 44.2f, 48.4f, house, Look.Beton);
        WallZ("Bureaux, cloison du couloir (est, 2)", level, 34f, 50f, 53.8f, house, Look.Beton);
        Span("Bureaux, linteau du couloir", level, new Vector3(48.4f, 2.2f, 33.8f), new Vector3(50f, house, 34.2f), Look.Beton);
        Span("Bureau", level, new Vector3(37f, 0f, 26.6f), new Vector3(39f, 0.75f, 27.6f), Look.Acier);
        Span("Armoire", level, new Vector3(34.2f, 0f, 30f), new Vector3(34.8f, 1.3f, 31.2f), Look.Acier);
        Span("Bureau", level, new Vector3(46f, 0f, 29f), new Vector3(48f, 0.75f, 30f), Look.Acier);
        Span("Armoire", level, new Vector3(53.2f, 0f, 32f), new Vector3(53.8f, 1.3f, 33.4f), Look.Acier);
        Span("Toit", level, new Vector3(33.8f, house, 21.8f), new Vector3(54.2f, roof, 40.2f), Look.Toit);

        // 6. La cour de service : un conteneur, des caisses.
        Span("Conteneur", level, new Vector3(58.6f, 0f, 30f), new Vector3(60.6f, 2f, 32f), Look.Caisse);
        Span("Caisse", level, new Vector3(55f, 0f, 35.2f), new Vector3(56f, 1f, 36.2f), Look.Caisse);
        Span("Caisse", level, new Vector3(55f, 0f, 36.3f), new Vector3(56f, 1f, 37.3f), Look.Caisse);

        // 7. L'escalier, le long du mur nord des bureaux, monte vers l'ouest jusqu'au palier, à la hauteur du toit ; un mur
        // ferme le nord de la salle derrière le palier (on ne revient pas à la cour ouest par là).
        const int steps = 15;
        const float stairBottom = 55.5f, stepDepth = 0.4f;
        for (int i = 1; i <= steps; i++)
        {
            float x1 = stairBottom - stepDepth * (i - 1), x0 = x1 - stepDepth;
            Span($"Marche {i}", level, new Vector3(x0, 0f, 40.2f), new Vector3(x1, roof * i / steps, 44.5f), Look.Marche);
        }
        float landingEnd = stairBottom - stepDepth * steps;   // 49,5
        Span("Palier", level, new Vector3(44f, 0f, 40.2f), new Vector3(landingEnd, roof, 44.5f), Look.Plateforme);
        Railing("Garde-corps du palier (nord)", level, new Vector3(44f, roof, 44.5f), new Vector3(landingEnd, roof + 1f, 44.55f));
        Railing("Garde-corps du palier (ouest)", level, new Vector3(43.95f, roof, 40.2f), new Vector3(44f, roof + 1f, 44.55f));
        SlopedRailing("Garde-corps de l'escalier", level, new Vector3(stairBottom, 0f, 44.525f), new Vector3(landingEnd, roof, 44.525f));
        WallX("Mur du fond de l'escalier", level, 44f, 44.55f, 49.5f, module, Look.BetonSombre);

        // 8. Le toit : un parapet d'un mètre, ouvert sur le palier et, à l'ouest, sur la passerelle ; un bloc de
        // ventilation au milieu.
        const float parapet = 1f, rim = 0.3f;
        Span("Parapet sud", level, new Vector3(33.8f, roof, 21.8f), new Vector3(54.2f, roof + parapet, 21.8f + rim), Look.Beton);
        Span("Parapet est", level, new Vector3(54.2f - rim, roof, 21.8f + rim), new Vector3(54.2f, roof + parapet, 40.2f - rim), Look.Beton);
        Span("Parapet nord (ouest)", level, new Vector3(33.8f, roof, 40.2f - rim), new Vector3(44f, roof + parapet, 40.2f), Look.Beton);
        Span("Parapet nord (est)", level, new Vector3(landingEnd, roof, 40.2f - rim), new Vector3(54.2f, roof + parapet, 40.2f), Look.Beton);
        Span("Parapet ouest (sud)", level, new Vector3(33.8f, roof, 21.8f + rim), new Vector3(33.8f + rim, roof + parapet, 30f), Look.Beton);
        Span("Parapet ouest (nord)", level, new Vector3(33.8f, roof, 31.6f), new Vector3(33.8f + rim, roof + parapet, 40.2f - rim), Look.Beton);
        Span("Ventilation", level, new Vector3(40f, roof, 26f), new Vector3(42.4f, roof + 1.4f, 28f), Look.Acier);

        // 9. La passerelle : du toit vers l'ouest, au-dessus de la cour, jusqu'à une plateforme contre le mur du stand,
        // puis son escalier qui descend vers le nord.
        Span("Passerelle", level, new Vector3(25f, roof - 0.2f, 30f), new Vector3(33.8f, roof, 31.6f), Look.Plateforme);
        Span("Plateforme", level, new Vector3(22.5f, roof - 0.2f, 29f), new Vector3(25f, roof, 33.6f), Look.Plateforme);
        foreach (Vector2 post in new[] { new Vector2(29f, 30.1f), new Vector2(29f, 31.5f), new Vector2(24.9f, 29.1f), new Vector2(24.9f, 33.5f) })
            Span("Poteau", level, new Vector3(post.x - 0.1f, 0f, post.y - 0.1f), new Vector3(post.x + 0.1f, roof - 0.2f, post.y + 0.1f), Look.Poteau);
        Railing("Garde-corps de la passerelle (sud)", level, new Vector3(25f, roof, 29.95f), new Vector3(33.8f, roof + 1f, 30f));
        Railing("Garde-corps de la passerelle (nord)", level, new Vector3(25f, roof, 31.6f), new Vector3(33.8f, roof + 1f, 31.65f));
        Railing("Garde-corps de la plateforme (sud)", level, new Vector3(22.5f, roof, 28.95f), new Vector3(25.05f, roof + 1f, 29f));
        Railing("Garde-corps de la plateforme (est, sud)", level, new Vector3(25f, roof, 29f), new Vector3(25.05f, roof + 1f, 30f));
        Railing("Garde-corps de la plateforme (est, nord)", level, new Vector3(25f, roof, 31.6f), new Vector3(25.05f, roof + 1f, 33.6f));
        const int walkSteps = 14;
        for (int i = 1; i <= walkSteps; i++)
        {
            float z0 = 33.6f + stepDepth * (i - 1);
            Span($"Marche de la passerelle {i}", level, new Vector3(22.5f, 0f, z0), new Vector3(25f, roof - 0.2f * i, z0 + stepDepth), Look.Marche);
        }
        SlopedRailing("Garde-corps de l'escalier de la passerelle", level, new Vector3(25.025f, roof, 33.6f), new Vector3(25.025f, 0f, 39.6f));

        // 10. La cour ouest : des conteneurs, une pile de caisses.
        Span("Conteneur", level, new Vector3(27f, 0f, 41f), new Vector3(29f, 2f, 43f), Look.Caisse);
        Span("Conteneur", level, new Vector3(30.6f, 0f, 26.6f), new Vector3(32.6f, 2f, 28.6f), Look.Caisse);
        Span("Caisse", level, new Vector3(22.5f, 0f, 44f), new Vector3(23.5f, 1f, 45f), Look.Caisse);
        Span("Caisse", level, new Vector3(22.5f, 0f, 45.1f), new Vector3(23.5f, 1f, 46.1f), Look.Caisse);
        Span("Caisse", level, new Vector3(22.5f, 1f, 44f), new Vector3(23.5f, 2f, 45f), Look.Caisse);
        Span("Caisse", level, new Vector3(32.6f, 0f, 46.8f), new Vector3(33.6f, 1f, 47.8f), Look.Caisse);

        // 11. Le couloir du retour : une chicane de trois murs, puis la dernière barricade avant l'arrivée.
        WallZ("Chicane 1", level, 15f, 22.5f, 26.6f, module, Look.BetonSombre);
        WallZ("Chicane 2", level, 8f, 25.9f, 29.8f, module, Look.BetonSombre);
        WallZ("Chicane 3", level, 1f, 22.5f, 26.6f, module, Look.BetonSombre);
        Span("Dernière barricade", level, new Vector3(22.5f, 0f, -1.8f), new Vector3(29.8f, 0.9f, -1.2f), Look.Barricade);

        // Les cibles.
        var targets = new GameObject("La Fosse (cibles)").transform;
        int hostiles = 0, civils = 0;
        foreach (CourseTargetSpec spec in CourseTargets)
        {
            string label = spec.civil ? $"Civil {++civils}" : $"Hostile {++hostiles}";
            PlaceCourseTarget(targetPrefab, targets, spec, $"{label} ({CourseSections[spec.section].name})");
        }

        // Le parcours : zones, sections, chrono, bilan (ShootingCourse, ShootingCourseHud).
        var course = new GameObject("La Fosse");
        course.AddComponent<NetworkObject>();
        var logic = course.AddComponent<ShootingCourse>();
        course.AddComponent<ShootingCourseHud>();
        var so = new SerializedObject(logic);
        so.FindProperty("startZone").boundsValue = CourseStartZone;
        SerializedProperty list = so.FindProperty("sections");
        list.arraySize = CourseSections.Length;
        for (int i = 0; i < CourseSections.Length; i++)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("name").stringValue = CourseSections[i].name;
            element.FindPropertyRelative("bounds").boundsValue = CourseSections[i].bounds;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        // Le tableau des meilleurs temps, au mur du départ : son titre, et les temps des douze armes en deux colonnes.
        Span("Tableau", level, new Vector3(24.25f, 0.9f, -9.5f), new Vector3(28.25f, 3.3f, -9.4f), Look.Tableau);
        Material textMaterial = BoardTextMaterial();
        TextMesh boardText = BoardText("Tableau des temps", null, new Vector3(26.25f, 2.95f, -9.37f), 0.03f, TextAnchor.MiddleCenter, textMaterial);
        boardText.text = "LA FOSSE · MEILLEURS TEMPS";
        // Vu du départ (on regarde vers le sud), la gauche du lecteur est à l'est : la première colonne est au x le plus grand.
        TextMesh left = BoardText("Colonne gauche", boardText.transform, new Vector3(27.35f, 2.6f, -9.37f), 0.024f, TextAnchor.UpperCenter, textMaterial);
        TextMesh right = BoardText("Colonne droite", boardText.transform, new Vector3(25.15f, 2.6f, -9.37f), 0.024f, TextAnchor.UpperCenter, textMaterial);
        var boardLogic = new SerializedObject(boardText.gameObject.AddComponent<CourseBoard>());
        boardLogic.FindProperty("left").objectReferenceValue = left;
        boardLogic.FindProperty("right").objectReferenceValue = right;
        boardLogic.ApplyModifiedPropertiesWithoutUndo();
    }

    private const string BoardTextMaterialPath = "Assets/_ProjectArena/Stand/TexteTableau.mat";

    /// <summary>Le matériau des textes du tableau : DuelArena/TexteMonde, qui teste la profondeur (CourseBoard y pose la
    /// texture de la police en jeu).</summary>
    private static Material BoardTextMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(BoardTextMaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("DuelArena/TexteMonde"));
            AssetDatabase.CreateAsset(material, BoardTextMaterialPath);
        }
        material.shader = Shader.Find("DuelArena/TexteMonde");
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Un texte du tableau, tourné vers le départ (un TextMesh se lit depuis son -Z).</summary>
    private static TextMesh BoardText(string name, Transform parent, Vector3 position, float size, TextAnchor anchor, Material material)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));
        var text = go.AddComponent<TextMesh>();
        text.fontSize = 64;
        text.characterSize = size;
        text.anchor = anchor;
        text.alignment = TextAlignment.Center;
        text.color = new Color(0.95f, 0.9f, 0.75f);
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return text;
    }

    private static void PlaceCourseTarget(GameObject prefab, Transform parent, CourseTargetSpec spec, string name)
    {
        var target = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        target.name = name;
        Vector3 look = new Vector3(spec.lookAt.x, spec.position.y, spec.lookAt.y) - spec.position;
        target.transform.SetPositionAndRotation(spec.position, Quaternion.LookRotation(look.sqrMagnitude > 1e-4f ? look : Vector3.back, Vector3.up));

        var dummy = new SerializedObject(target.GetComponent<TrainingDummy>());
        dummy.FindProperty("tint").colorValue = spec.civil ? CivilTint : HostileTint;
        dummy.ApplyModifiedPropertiesWithoutUndo();

        var course = new SerializedObject(target.GetComponent<CourseTarget>());
        course.FindProperty("role").intValue = spec.civil ? (int)CourseTargetRole.Civil : (int)CourseTargetRole.Hostile;
        course.FindProperty("section").intValue = spec.section;
        course.FindProperty("moves").boolValue = spec.moves;
        course.FindProperty("railEnd").vector3Value = spec.railEnd;
        course.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Un bloc du jeu, de `min` à `max` : un collider (le layer Default arrête les balles et les robots), et ce
    /// qu'il devient à l'habillage.</summary>
    private static GameObject Span(string name, Transform parent, Vector3 min, Vector3 max, Look look)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = (min + max) * 0.5f;
        go.AddComponent<BoxCollider>().size = max - min;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        blocks.Add(new StandDecorBuilder.Block(go, look));
        return go;
    }

    /// <summary>Un garde-corps de tôle (plein : une balle s'y arrête comme sur un mur, bible § 13).</summary>
    private static void Railing(string name, Transform parent, Vector3 min, Vector3 max) => Span(name, parent, min, max, Look.GardeCorps);

    /// <summary>Le garde-corps d'un escalier : une tôle d'un mètre au-dessus du nez des marches, de `bottom` (le pied de
    /// l'escalier) à `top` (son sommet), d'une épaisseur de 5 cm.</summary>
    private static void SlopedRailing(string name, Transform parent, Vector3 bottom, Vector3 top)
    {
        Vector3 along = top - bottom;
        float length = along.magnitude;
        Vector3 flat = new Vector3(along.x, 0f, along.z).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, flat);
        Vector3 up = Vector3.Cross(along / length, side).normalized;
        if (up.y < 0f) up = -up;
        float height = 1f / Mathf.Max(0.3f, up.y);   // 1 m à la verticale
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation((bottom + top) * 0.5f + up * (height * 0.5f), Quaternion.LookRotation(side, up));
        // Le long de l'escalier est le X local ; la hauteur, le Y ; l'épaisseur, le Z (vers `side`).
        go.AddComponent<BoxCollider>().size = new Vector3(length, height, 0.05f);
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        blocks.Add(new StandDecorBuilder.Block(go, Look.GardeCorps));
    }

    /// <summary>
    /// Le menu de pause du stand (2026-10-27, demandé par l'utilisateur pour changer ses réglages au stand) :
    /// Échap l'ouvre (PauseMenu), avec les Options du jeu (OptionsController) et la fenêtre des armes. Le
    /// document est posé avant ses contrôleurs : ils retrouvent ses éléments en s'activant.
    /// </summary>
    private static void BuildInterface()
    {
        var ui = new GameObject("Interface");
        var document = ui.AddComponent<UnityEngine.UIElements.UIDocument>();
        document.panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(PanelSettingsPath);
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(PausePath);

        var options = new SerializedObject(ui.AddComponent<OptionsController>());
        options.FindProperty("document").objectReferenceValue = document;
        options.FindProperty("bindRowTemplate").objectReferenceValue = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(BindRowPath);
        options.ApplyModifiedPropertiesWithoutUndo();

        var pause = new SerializedObject(ui.AddComponent<PauseMenu>());
        pause.FindProperty("document").objectReferenceValue = document;
        pause.ApplyModifiedPropertiesWithoutUndo();

        var sounds = new SerializedObject(ui.AddComponent<UiSounds>());
        sounds.FindProperty("sounds").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameSounds>(GameSoundsPath);
        sounds.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Un mannequin à (x, distance), tourné vers le poste de tir.</summary>
    private static void PlaceDummy(GameObject prefab, Transform parent, Vector2 p, int index)
    {
        var dummy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        dummy.name = $"Mannequin {index} ({p.y:0} m)";
        var at = new Vector3(p.x, 0f, p.y);
        Vector3 toPost = FiringPost - at;
        toPost.y = 0f;
        dummy.transform.SetPositionAndRotation(at, Quaternion.LookRotation(toPost.normalized, Vector3.up));
    }

    /// <summary>Le mannequin : vie, surface touchable du préfab joueur, apparence du robot. Une cible de
    /// la Fosse y ajoute CourseTarget et ne se relève pas seule.</summary>
    private static GameObject BuildDummyPrefab(string path, bool courseTarget)
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform model = player.transform.Find("Model");
        PlayerHitbox playerHitbox = player.GetComponentInChildren<PlayerHitbox>(true);
        float eye = new SerializedObject(player.GetComponent<PlayerLocomotion>()).FindProperty("standingProfile.cameraHeight").floatValue;
        AnimationClip idle = AssetDatabase.LoadAllAssetsAtPath(IdleClipPath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_ProjectArena", "Stand");

        var root = new GameObject(courseTarget ? "CibleParcours" : "Mannequin") { layer = LayerMask.NameToLayer("Player") };
        root.AddComponent<NetworkObject>();
        root.AddComponent<Health>();
        var dummy = root.AddComponent<TrainingDummy>();
        if (courseTarget) root.AddComponent<CourseTarget>();
        // Le corps : on ne le traverse pas, mais les balles passent (layer Player, hors de worldMask) et
        // ne s'arrêtent que sur la surface touchable.
        var body = root.AddComponent<CapsuleCollider>();
        body.center = new Vector3(0f, 0.9f, 0f);
        body.height = 1.8f;
        body.radius = 0.3f;

        var hitboxObject = new GameObject("Hitbox");
        hitboxObject.transform.SetParent(root.transform, false);
        var hitbox = hitboxObject.AddComponent<PlayerHitbox>();
        EditorUtility.CopySerialized(playerHitbox, hitbox);

        var dummySo = new SerializedObject(dummy);
        dummySo.FindProperty("robotModel").objectReferenceValue = model != null ? model.gameObject : null;
        dummySo.FindProperty("idleClip").objectReferenceValue = idle;
        dummySo.FindProperty("eyeHeight").floatValue = eye;
        dummySo.FindProperty("regenerates").boolValue = !courseTarget;
        dummySo.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void AddToBuild(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Any(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
