using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// La mise en scène de l'écran de fin (2026-10-12) : le robot du gagnant, à sa couleur, célèbre dans
/// l'arène, filmé en plein écran de trois quarts et un peu par en dessous. L'écran de fin
/// (MatchEndScreen) pose le tableau des scores à droite ; le robot tient la gauche. GDD § 5 : « un
/// écran de victoire mis en scène pour le gagnant ».
///
/// Refaite le même jour sur retour de l'utilisateur : dans l'arène (et non plus sur un fond neutre),
/// SANS l'arme, en jouant au hasard les célébrations qu'il a fournies (Mixamo, sur le Y Bot).
///
/// - Le robot est une COPIE de l'enfant « Model » du préfab joueur (jamais la racine et son
///   NetworkObject), dépouillée de ses scripts, de son arme et du gant du taser, sur le layer Preview (9) que la caméra
///   du joueur ne dessine pas. Il se tient sur un point choisi pour le cadrage (`/VictorySpot` de
///   l'arène) : comme les « meilleurs joueurs » de Call of Duty, un endroit fixe de la carte.
/// - Les vrais robots sont masqués pour cette caméra seulement, le temps de son rendu (le gagnant a
///   pu finir son match à cet endroit), et les épaves ne sont pas sur ses layers.
/// - Les célébrations s'enchaînent au hasard, jamais deux fois la même de suite (CelebrationAt),
///   tirées d'une graine et d'une horloge que les deux machines partagent : les deux joueurs voient
///   la même chose au même moment.
///
/// Catégorie C pure.
/// </summary>
public class VictoryStage : MonoBehaviour
{
    /// <summary>Le champ vertical de la caméra (degrés) : un portrait, peu déformé.</summary>
    public const float FieldOfView = 32f;
    /// <summary>Où se tient le robot sur l'écran, en fraction de sa largeur : au milieu de la colonne
    /// de gauche de l'écran de fin.</summary>
    public const float ScreenX = 0.25f;
    /// <summary>Le cadre, en hauteur au-dessus des pieds (m) : des genoux aux mains levées d'une
    /// célébration. Fixe : la caméra ne suit pas les bras, elle ne tremble pas.</summary>
    public const float FrameBottom = 0.35f;
    public const float FrameTop = 2.3f;
    /// <summary>Le fondu d'une célébration à la suivante (s).</summary>
    public const float CrossFade = 0.35f;

    private const int WorldMask = 1; // Default : le décor qui pourrait masquer le robot

    private Camera stageCamera;
    private Transform spot;
    private GameObject model;
    private Animator modelAnimator;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private readonly List<AnimationClipPlayable> clipPlayables = new List<AnimationClipPlayable>();
    private readonly List<float> clipLengths = new List<float>();
    private CelebrationPlan plan;
    private int seed;
    private float time;
    private HumanPoseHandler poseHandler;
    private HumanPose pose;
    private bool proceduralPose;

    // Les vrais robots, masqués le temps du rendu de la caméra du plateau.
    private readonly List<Renderer> liveRenderers = new List<Renderer>();
    private readonly List<Renderer> hiddenNow = new List<Renderer>();

    // La caméra du joueur, éteinte sous celle du plateau.
    private readonly KillCam.CoveredCamera playerView = new KillCam.CoveredCamera();

    /// <summary>Le plateau (sa caméra), créé au premier usage dans la scène courante.</summary>
    public static VictoryStage Create()
    {
        var root = new GameObject("PlateauVictoire");
        var stage = root.AddComponent<VictoryStage>();
        stage.Build();
        return stage;
    }

    private void Build()
    {
        var cameraObject = new GameObject("CameraVictoire") { tag = "MainCamera" };
        cameraObject.transform.SetParent(transform, false);
        stageCamera = cameraObject.AddComponent<Camera>();
        // Le décor et le robot du plateau ; ni les bras de la vue subjective, ni les surfaces
        // touchables, ni les épaves.
        int hidden = LayerBit("Viewmodel") | LayerBit("Hitbox") | LayerBit("Player") | CorpseLayer.Mask;
        stageCamera.cullingMask = ~hidden | (PreviewLayer.Index >= 0 ? 1 << PreviewLayer.Index : 0);
        stageCamera.clearFlags = CameraClearFlags.Skybox;
        stageCamera.fieldOfView = FieldOfView;
        stageCamera.nearClipPlane = 0.1f;
        stageCamera.farClipPlane = 1000f;
        // Celle du joueur s'éteint sous elle (KillCam.CoveredCamera) ; celle de la kill cam passe au-dessus.
        stageCamera.depth = 50f;
        stageCamera.enabled = false;
        UniversalAdditionalCameraData data = stageCamera.GetUniversalAdditionalCameraData();
        data.renderShadows = true;
        data.renderPostProcessing = true;   // comme la caméra du joueur et la kill cam
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
    }

    private static int LayerBit(string name)
    {
        int layer = LayerMask.NameToLayer(name);
        return layer >= 0 ? 1 << layer : 0;
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += HideLiveRobots;
        RenderPipelineManager.endCameraRendering += ShowLiveRobots;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= HideLiveRobots;
        RenderPipelineManager.endCameraRendering -= ShowLiveRobots;
        RestoreLiveRobots();
    }

    private void OnDestroy() => Clear();

    // ------------------------------------------------------------------
    // Montrer, cacher
    // ------------------------------------------------------------------

    /// <summary>
    /// Montre le robot du gagnant sur `victorySpot` : modèle du préfab joueur, dans son châssis, peint de
    /// `paint`, sans arme, qui enchaîne les célébrations `clips` au hasard de `celebrationSeed`. Sans clip,
    /// une pose de victoire provisoire (le poing levé). `playerCamera`, la caméra du joueur, s'éteint dessous.
    /// </summary>
    public void Show(Transform victorySpot, GameObject playerPrefab, ChassisId chassis, Color32 paint, IReadOnlyList<AnimationClip> clips,
                     int celebrationSeed, Camera playerCamera)
    {
        Clear();
        Transform assetModel = playerPrefab != null ? playerPrefab.transform.Find("Model") : null;
        if (victorySpot == null || assetModel == null) return;
        spot = victorySpot;
        seed = celebrationSeed;
        time = 0f;

        // Sans arme : ni le fusil, ni le gant du taser, qui remplace la main gauche (elle réapparaît).
        model = RobotAppearance.Copy(assetModel.gameObject, transform, "RobotVictoire");
        model.transform.SetPositionAndRotation(spot.position, Quaternion.Euler(0f, spot.eulerAngles.y, 0f));
        modelAnimator = model.GetComponent<Animator>();
        RobotChassis.Apply(model.transform, chassis);
        Paint(playerPrefab, assetModel, paint);

        if (modelAnimator != null)
        {
            modelAnimator.applyRootMotion = false;
            modelAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (!BuildCelebrations(clips))
            {
                // La pose d'attente du contrôleur du jeu, retouchée à chaque image.
                proceduralPose = modelAnimator.avatar != null && modelAnimator.avatar.isHuman;
                if (proceduralPose) poseHandler = new HumanPoseHandler(modelAnimator.avatar, model.transform);
            }
        }

        CollectLiveRobots();
        stageCamera.enabled = true;
        playerView.Follow(playerCamera);
        playerView.Hold();
        Evaluate();
        Frame();
    }

    public void Hide()
    {
        Clear();
        if (stageCamera != null) stageCamera.enabled = false;
    }

    /// <summary>L'instant de l'écran de fin (s depuis son ouverture, sur l'horloge du serveur) : la
    /// célébration qu'il montre en découle, la même sur les deux machines.</summary>
    public void SetTime(float seconds)
    {
        time = Mathf.Max(0f, seconds);
        Evaluate();
    }

    private void Clear()
    {
        playerView.Release();
        RestoreLiveRobots();
        liveRenderers.Clear();
        if (graph.IsValid()) graph.Destroy();
        plan = null;
        clipPlayables.Clear();
        clipLengths.Clear();
        poseHandler?.Dispose();
        poseHandler = null;
        proceduralPose = false;
        if (model != null) Discard(model);
        model = null;
        modelAnimator = null;
    }

    // En jeu, Destroy ; en mode Édition (rendus de contrôle), DestroyImmediate.
    private static void Discard(Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    private void LateUpdate()
    {
        if (model == null) return;
        playerView.Hold();
        if (proceduralPose) ApplyVictoryPose();
        Frame();
    }

    // ------------------------------------------------------------------
    // Le robot
    // ------------------------------------------------------------------

    /// <summary>La peinture du gagnant, sur les mêmes pièces que PlayerPaint (lues sur l'asset).</summary>
    private void Paint(GameObject playerPrefab, Transform assetModel, Color32 paint)
    {
        PlayerPaint assetPaint = playerPrefab.GetComponent<PlayerPaint>();
        if (assetPaint == null || paint.a == 0) return;
        var block = new MaterialPropertyBlock();
        foreach (Renderer assetRenderer in assetPaint.PaintedRenderers)
        {
            Transform twin = Twin(assetModel, assetRenderer != null ? assetRenderer.transform : null, model.transform);
            if (twin == null || !twin.TryGetComponent(out Renderer r)) continue;
            PlayerPaint.PaintPart(r, paint, block, true);
        }
    }

    // ------------------------------------------------------------------
    // Les célébrations
    // ------------------------------------------------------------------

    private bool BuildCelebrations(IReadOnlyList<AnimationClip> clips)
    {
        if (clips == null) return false;
        var usable = new List<AnimationClip>();
        foreach (AnimationClip clip in clips)
        {
            if (clip != null && clip.length > 0.1f) usable.Add(clip);
        }
        if (usable.Count == 0) return false;

        // Le contrôleur du jeu n'a rien à faire ici : le graphe des célébrations a la main.
        modelAnimator.runtimeAnimatorController = null;
        graph = PlayableGraph.Create("CelebrationsVictoire");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(graph, "Animation", modelAnimator);
        mixer = AnimationMixerPlayable.Create(graph, usable.Count);
        output.SetSourcePlayable(mixer);
        for (int i = 0; i < usable.Count; i++)
        {
            var playable = AnimationClipPlayable.Create(graph, usable[i]);
            graph.Connect(playable, 0, mixer, i);
            mixer.SetInputWeight(i, 0f);
            clipPlayables.Add(playable);
            clipLengths.Add(usable[i].length);
        }
        plan = new CelebrationPlan(seed, clipLengths, CrossFade);
        return true;
    }

    /// <summary>Pose le robot à l'instant `time` de l'enchaînement des célébrations.</summary>
    private void Evaluate()
    {
        if (!graph.IsValid() || clipPlayables.Count == 0) return;
        CelebrationFrame frame = plan.At(time);
        for (int i = 0; i < clipPlayables.Count; i++) mixer.SetInputWeight(i, 0f);
        mixer.SetInputWeight(frame.clip, 1f - frame.nextWeight);
        clipPlayables[frame.clip].SetTime(frame.time);
        if (frame.nextWeight > 0f && frame.nextClip != frame.clip)
        {
            mixer.SetInputWeight(frame.nextClip, frame.nextWeight);
            clipPlayables[frame.nextClip].SetTime(frame.nextTime);
        }
        graph.Evaluate(0f);
    }

    /// <summary>Ce que joue le robot à un instant de l'écran de fin : un clip, et le suivant pendant
    /// le fondu de l'un à l'autre.</summary>
    public struct CelebrationFrame
    {
        public int clip;
        public float time;
        public int nextClip;
        public float nextTime;
        /// <summary>Le poids du clip suivant (0 hors des fondus).</summary>
        public float nextWeight;
    }

    /// <summary>
    /// La célébration à l'instant `t` (s) : les clips (de durées `lengths`) s'enchaînent au hasard
    /// de `seed`, jamais deux fois le même de suite, chacun joué en entier et fondu dans le suivant
    /// sur ses `fade` dernières secondes. Même graine, même instant : même image, sur toutes les
    /// machines (aucun Random d'Unity). Fonction pure, testée.
    /// </summary>
    public static CelebrationFrame CelebrationAt(int seed, float t, IReadOnlyList<float> lengths, float fade)
        => new CelebrationPlan(seed, lengths, fade).At(t);

    /// <summary>
    /// L'enchaînement des célébrations d'une graine, tiré au fur et à mesure et retenu : l'écran de fin le lit à chaque image,
    /// et rejouerait sinon tout le tirage depuis son ouverture. Lire un instant ne change rien au tirage : les mêmes images
    /// que CelebrationAt, dans n'importe quel ordre.
    /// </summary>
    public sealed class CelebrationPlan
    {
        private const int MaxClips = 10000;

        private struct Entry
        {
            public int clip;
            public int next;
            public float start;
            public float nextStart;
            public float end;
        }

        private readonly IReadOnlyList<float> lengths;
        private readonly float fade;
        private readonly int count;
        private readonly System.Random random;
        private readonly List<Entry> entries = new List<Entry>();

        public CelebrationPlan(int seed, IReadOnlyList<float> lengths, float fade)
        {
            this.lengths = lengths;
            this.fade = fade;
            count = lengths != null ? lengths.Count : 0;
            random = new System.Random(seed);
        }

        public CelebrationFrame At(float t)
        {
            if (count == 0) return default;
            t = Mathf.Max(0f, t);
            if (count == 1)
            {
                float only = Mathf.Max(lengths[0], 0.1f);
                return new CelebrationFrame { clip = 0, time = t % only };
            }

            for (int i = 0; i < MaxClips; i++)
            {
                if (i == entries.Count) DrawNext();
                Entry entry = entries[i];
                if (t < entry.nextStart) return new CelebrationFrame { clip = entry.clip, time = t - entry.start, nextClip = entry.next };
                if (t < entry.end)
                {
                    return new CelebrationFrame
                    {
                        clip = entry.clip,
                        time = t - entry.start,
                        nextClip = entry.next,
                        nextTime = t - entry.nextStart,
                        nextWeight = Mathf.Clamp01((t - entry.nextStart) / Mathf.Max(fade, 1e-3f)),
                    };
                }
            }
            return new CelebrationFrame { clip = entries[entries.Count - 1].next };
        }

        private void DrawNext()
        {
            bool first = entries.Count == 0;
            Entry previous = first ? default : entries[entries.Count - 1];
            int clip = first ? random.Next(count) : previous.next;
            float start = first ? 0f : previous.nextStart;
            float end = start + Mathf.Max(lengths[clip], fade * 2f + 0.1f);
            entries.Add(new Entry { clip = clip, next = (clip + 1 + random.Next(count - 1)) % count, start = start, nextStart = end - fade, end = end });
        }
    }

    // ------------------------------------------------------------------
    // La pose provisoire, sans clip : le poing levé
    // ------------------------------------------------------------------

    private static readonly (string muscle, float value)[] VictoryMuscles =
    {
        ("Right Shoulder Down-Up", 0.6f),
        ("Right Arm Down-Up", 1.0f),
        ("Right Arm Front-Back", 0.25f),
        ("Right Arm Twist In-Out", 0.0f),
        ("Right Forearm Stretch", 0.85f),
        ("Right Forearm Twist In-Out", 0.0f),
        ("Left Shoulder Down-Up", 0.0f),
        ("Left Arm Down-Up", -0.75f),
        ("Left Arm Front-Back", 0.15f),
        ("Left Forearm Stretch", 0.75f),
        ("Spine Front-Back", -0.1f),
        ("Chest Front-Back", -0.2f),
        ("Head Nod Down-Up", 0.2f),
    };

    private static int[] victoryMuscleIndices;

    private void ApplyVictoryPose()
    {
        if (poseHandler == null) return;
        if (victoryMuscleIndices == null)
        {
            victoryMuscleIndices = new int[VictoryMuscles.Length];
            string[] names = HumanTrait.MuscleName;
            for (int i = 0; i < VictoryMuscles.Length; i++)
                victoryMuscleIndices[i] = System.Array.IndexOf(names, VictoryMuscles[i].muscle);
        }

        poseHandler.GetHumanPose(ref pose);
        for (int i = 0; i < VictoryMuscles.Length; i++)
        {
            int index = victoryMuscleIndices[i];
            if (index >= 0 && index < pose.muscles.Length) pose.muscles[index] = VictoryMuscles[i].value;
        }
        poseHandler.SetHumanPose(ref pose);
        // SetHumanPose replace aussi le corps : on le remet sur son point.
        model.transform.SetPositionAndRotation(spot.position, Quaternion.Euler(0f, spot.eulerAngles.y, 0f));
    }

    // ------------------------------------------------------------------
    // La caméra
    // ------------------------------------------------------------------

    /// <summary>La durée de l'ouverture du mouvement de caméra (s) : de près, de profil et par en dessous, jusqu'au
    /// trois-quarts.</summary>
    public const float IntroTime = 2.6f;

    /// <summary>Où se tient la caméra, autour du robot : son angle depuis l'avant du robot vers sa droite (degrés), sa
    /// hauteur angulaire (négative : par en dessous), sa distance (1 : le cadre des genoux aux mains levées) et la
    /// hauteur visée au-dessus des pieds (m).</summary>
    public struct Shot
    {
        public float azimuth;
        public float elevation;
        public float distanceScale;
        public float centerHeight;
    }

    /// <summary>
    /// Le mouvement de caméra de l'écran de fin à l'instant `t` (s depuis son ouverture, l'horloge du serveur : le même
    /// sur les deux machines ; GDD § 5, « mouvement de caméra »). L'ouverture part de près, presque de profil et par en
    /// dessous, sur le haut du corps, et recule en tournant jusqu'au trois-quarts du cadre (le robot regarde vers le
    /// tableau des scores) ; puis la caméra tourne lentement vers sa face en avançant un peu. Toujours du côté droit du
    /// robot. Fonction pure, testée.
    /// </summary>
    public static Shot ShotAt(float t)
    {
        float intro = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / IntroTime), 3f);
        float drift = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - IntroTime) / 12f));
        return new Shot
        {
            azimuth = Mathf.Lerp(62f, 24f, intro) - 14f * drift,
            elevation = Mathf.Lerp(-20f, -5f, intro),
            distanceScale = Mathf.Lerp(0.5f, 1f, intro) * (1f - 0.06f * drift),
            centerHeight = Mathf.Lerp(1.55f, (FrameBottom + FrameTop) * 0.5f, intro),
        };
    }

    /// <summary>
    /// Des genoux aux mains levées, de trois quarts du côté droit du robot (il regarde vers le
    /// tableau des scores) et un peu par en dessous, le robot sur la gauche de l'écran (`ScreenX`),
    /// au rythme du mouvement de caméra (ShotAt). Le décor ne passe jamais entre elle et le robot.
    /// </summary>
    private void Frame()
    {
        if (stageCamera == null || spot == null) return;
        Shot shot = ShotAt(time);
        Vector3 feet = spot.position;
        Quaternion facing = Quaternion.Euler(0f, spot.eulerAngles.y, 0f);
        Vector3 center = feet + Vector3.up * shot.centerHeight;
        float halfHeight = (FrameTop - FrameBottom) * 0.5f * 1.04f;
        float distance = halfHeight / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) * shot.distanceScale;

        Vector3 direction = facing * Quaternion.Euler(-shot.elevation, shot.azimuth, 0f) * Vector3.forward;
        if (Physics.SphereCast(center, 0.25f, direction, out RaycastHit hit, distance, WorldMask, QueryTriggerInteraction.Ignore))
            distance = Mathf.Max(1.2f, hit.distance - 0.1f);
        Vector3 position = center + direction * distance;

        // Tournée vers la droite pour que le robot se tienne à ScreenX.
        float tanHalfWidth = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(0.5f, stageCamera.aspect);
        float yaw = Mathf.Atan((0.5f - ScreenX) * 2f * tanHalfWidth) * Mathf.Rad2Deg;
        Quaternion look = Quaternion.LookRotation(center - position, Vector3.up);
        stageCamera.fieldOfView = FieldOfView;
        stageCamera.transform.SetPositionAndRotation(position, Quaternion.AngleAxis(yaw, Vector3.up) * look);
    }

    // ------------------------------------------------------------------
    // Les vrais robots, masqués pour cette caméra seulement
    // ------------------------------------------------------------------

    // Ce que cette caméra dessinerait, pas plus : les armes rangées et les bras de la vue subjective, qu'elle ne voit pas,
    // feraient basculer des centaines de pièces par robot, deux fois par image.
    private void CollectLiveRobots()
    {
        liveRenderers.Clear();
        int drawn = stageCamera.cullingMask;
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player == null) continue;
            foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
            {
                if ((drawn & (1 << r.gameObject.layer)) != 0) liveRenderers.Add(r);
            }
        }
    }

    private void HideLiveRobots(ScriptableRenderContext context, Camera camera)
    {
        if (camera != stageCamera) return;
        hiddenNow.Clear();
        foreach (Renderer r in liveRenderers)
        {
            if (r == null || r.forceRenderingOff) continue;
            r.forceRenderingOff = true;
            hiddenNow.Add(r);
        }
    }

    private void ShowLiveRobots(ScriptableRenderContext context, Camera camera)
    {
        if (camera == stageCamera) RestoreLiveRobots();
    }

    private void RestoreLiveRobots()
    {
        foreach (Renderer r in hiddenNow)
        {
            if (r != null) r.forceRenderingOff = false;
        }
        hiddenNow.Clear();
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    public Camera TestCamera => stageCamera;
#endif

    private static Transform Twin(Transform assetRoot, Transform target, Transform instanceRoot)
    {
        if (target == null) return null;
        if (target == assetRoot) return instanceRoot;
        string path = target.name;
        for (Transform p = target.parent; p != null && p != assetRoot; p = p.parent) path = p.name + "/" + path;
        return instanceRoot.Find(path);
    }
}
