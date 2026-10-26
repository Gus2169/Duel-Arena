using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

/// <summary>
/// Les vignettes du menu (2026-10-17, pour les maquettes de l'utilisateur, toutes faites d'images) : le robot
/// dans chaque couleur, dans une pose, en pied ou en buste ; chaque arme et chaque accessoire, de profil.
///
/// Rendues dans le menu, à partir des vrais préfabs (le robot du préfab joueur, les armes de la panoplie,
/// les accessoires du catalogue), jamais cuites à la main : elles suivent le jour où un modèle change. Une
/// caméra à elle, sur le layer `Preview`, à 1 000 m sous le plateau d'aperçu (PreviewStage), dont la
/// caméra ne voit pas si loin ; les deux lumières directionnelles du plateau les éclairent pareil.
///
/// Rendues à la demande, deux par image au plus (le menu ne saccade pas quand un écran en demande vingt),
/// puis gardées : une même vignette demandée deux fois ne se rend qu'une fois. Chaque rendu passe par une
/// texture à anticrénelage, recopiée dans une texture simple, la seule gardée.
/// </summary>
public class MenuThumbnails : MonoBehaviour
{
    public enum Framing
    {
        /// <summary>Le robot en pied.</summary>
        FullBody,
        /// <summary>La tête et le buste : les portraits du salon.</summary>
        Bust,
    }

    /// <summary>Une vignette du robot.</summary>
    public struct RobotShot
    {
        public Color32 paint;
        /// <summary>La pose (l'attente si vide), prise à `time` secondes.</summary>
        public AnimationClip pose;
        public float time;
        /// <summary>Le robot tourné de tant de degrés (0 : de face).</summary>
        public float yaw;
        public Framing framing;
        public int width;
        public int height;
        /// <summary>Un robot sans couleur, sombre : le duelliste qu'on ne connaît pas encore.</summary>
        public bool silhouette;
        /// <summary>Son châssis (RobotChassis ; le CT-01 par défaut).</summary>
        public ChassisId chassis;
    }

    [Tooltip("Le préfab joueur : son enfant « Model » est copié, jamais la racine (voir CharacterPreviewController).")]
    [SerializeField] private GameObject playerPrefab;

    [Tooltip("La pose par défaut des vignettes du robot : l'attente à mains nues (Y Bot@Idle).")]
    [SerializeField] private AnimationClip idleClip;

    [Tooltip("Vignettes rendues par image, au plus.")]
    [SerializeField, Min(1)] private int rendersPerFrame = 2;

    private static readonly Vector3 StageOrigin = new Vector3(0f, -1000f, 0f);
    private static readonly Color32 SilhouettePaint = new Color32(46, 52, 60, 255);
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private sealed class Job
    {
        public string key;
        public Func<RenderTexture> render;
        public readonly List<Action<Texture>> callbacks = new();
    }

    private readonly Dictionary<string, RenderTexture> cache = new();
    private readonly Dictionary<string, Job> pending = new();
    private readonly Queue<Job> queue = new();
    private Camera shotCamera;
    private Transform stage;
    private RenderTexture work;

    public GameObject PlayerPrefab => playerPrefab;

    private void OnDestroy()
    {
        foreach (RenderTexture texture in cache.Values)
        {
            if (texture == null) continue;
            texture.Release();
            Destroy(texture);
        }
        cache.Clear();
        if (work != null)
        {
            work.Release();
            Destroy(work);
        }
    }

    private void Update()
    {
        for (int i = 0; i < rendersPerFrame && queue.Count > 0; i++)
        {
            Job job = queue.Dequeue();
            pending.Remove(job.key);
            RenderTexture texture = null;
            try { texture = job.render(); }
            catch (Exception e) { Debug.LogWarning($"[Menu] Vignette « {job.key} » : {e}", this); }
            if (texture != null) cache[job.key] = texture;
            foreach (Action<Texture> callback in job.callbacks) callback?.Invoke(texture);
        }
    }

    // ------------------------------------------------------------------
    // Les demandes
    // ------------------------------------------------------------------

    /// <summary>Le robot de `shot` ; `ready` reçoit la vignette (tout de suite si elle est déjà rendue).</summary>
    public void Robot(RobotShot shot, Action<Texture> ready)
    {
        AnimationClip pose = shot.pose != null ? shot.pose : idleClip;
        Color32 paint = shot.silhouette ? SilhouettePaint : shot.paint;
        string key = $"robot/{(int)shot.chassis}/{paint.r},{paint.g},{paint.b}/{(pose != null ? pose.name : "-")}/{shot.time:0.00}/{shot.yaw:0}/{shot.framing}/{shot.width}x{shot.height}";
        Request(key, () => RenderRobot(shot, pose, paint), ready);
    }

    /// <summary>L'arme `data`, dans son modèle `model` et sa finition, avec les accessoires choisis pour la
    /// place `slot` (aucun si `attachments` vaut WeaponAttachments.None), de profil.</summary>
    public void Weapon(WeaponData data, int model, WeaponFinish finish, WeaponAttachments attachments, int slot, int width, int height, Action<Texture> ready)
    {
        if (data == null) { ready?.Invoke(null); return; }
        string fitted = $"{attachments.Sight(slot)}.{(int)attachments.Grip(slot)}.{(attachments.Laser(slot) ? 1 : 0)}.{(attachments.ExtendedMagazine(slot) ? 1 : 0)}";
        string key = $"arme/{data.name}/{model}/{(int)finish}/{fitted}/{width}x{height}";
        Request(key, () => RenderWeapon(data, model, finish, attachments, slot, width, height), ready);
    }

    /// <summary>Un accessoire seul, dans la finition de l'arme qui le porte, de profil.</summary>
    public void Attachment(GameObject prefab, WeaponFinish finish, int width, int height, Action<Texture> ready)
    {
        if (prefab == null) { ready?.Invoke(null); return; }
        string key = $"accessoire/{prefab.name}/{(int)finish}/{width}x{height}";
        Request(key, () => RenderProp(prefab, finish, width, height), ready);
    }

    private void Request(string key, Func<RenderTexture> render, Action<Texture> ready)
    {
        if (cache.TryGetValue(key, out RenderTexture done) && done != null)
        {
            ready?.Invoke(done);
            return;
        }
        if (!pending.TryGetValue(key, out Job job))
        {
            job = new Job { key = key, render = render };
            pending[key] = job;
            queue.Enqueue(job);
        }
        if (ready != null) job.callbacks.Add(ready);
    }

    // ------------------------------------------------------------------
    // Les rendus
    // ------------------------------------------------------------------

    private RenderTexture RenderRobot(RobotShot shot, AnimationClip pose, Color32 paint)
    {
        Transform assetModel = playerPrefab != null ? playerPrefab.transform.Find("Model") : null;
        if (assetModel == null) return null;
        EnsureRig();

        GameObject copy = RobotAppearance.Copy(assetModel.gameObject, stage, "Vignette");
        PlayableGraph graph = default;
        try
        {
            copy.transform.localRotation = Quaternion.Euler(0f, shot.yaw, 0f);
            RobotChassis.Apply(copy.transform, shot.chassis);
            RobotAppearance.Paint(playerPrefab, assetModel, copy, paint);

            Animator animator = copy.GetComponent<Animator>();
            if (animator != null && pose != null)
            {
                animator.applyRootMotion = false;
                animator.runtimeAnimatorController = null;
                graph = PlayableGraph.Create("Vignette");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
                var clip = AnimationClipPlayable.Create(graph, pose);
                clip.SetTime(Mathf.Clamp(shot.time, 0f, pose.length));
                output.SetSourcePlayable(clip);
                graph.Evaluate(0f);
            }

            Bounds bounds = PreviewStage.ComputeWorldBounds(copy.transform);
            Vector3 center = bounds.center;
            float halfHeight = bounds.extents.y;
            float radius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
            if (shot.framing == Framing.Bust)
            {
                // De la tête au milieu du buste.
                float top = bounds.max.y;
                halfHeight = bounds.size.y * 0.21f;
                center = new Vector3(center.x, top - halfHeight * 1.05f, center.z);
                radius = Mathf.Min(radius, halfHeight * 1.3f);
            }
            return Shoot(center, halfHeight, radius, new Vector3(0f, 0.12f, 1f), shot.width, shot.height, 1.06f);
        }
        finally
        {
            if (graph.IsValid()) graph.Destroy();
            DestroyImmediate(copy);
        }
    }

    private RenderTexture RenderWeapon(WeaponData data, int model, WeaponFinish finish, WeaponAttachments attachments, int slot, int width, int height)
    {
        GameObject prefab = data.ModelPrefabFor(model);
        if (prefab == null) return null;
        EnsureRig();
        GameObject weapon = Instantiate(prefab, stage, false);
        try
        {
            Prepare(weapon);
            if (weapon.TryGetComponent(out WeaponFinishes finishes)) finishes.Apply(finish);
            AttachmentMount.Apply(weapon.transform, data, attachments, slot, finish);
            Bounds bounds = RendererBounds(weapon);
            return Shoot(bounds.center, bounds.extents.y, new Vector2(bounds.extents.x, bounds.extents.z).magnitude * 0.82f,
                new Vector3(1f, 0.08f, 0f), width, height, 1.04f);
        }
        finally
        {
            DestroyImmediate(weapon);
        }
    }

    private RenderTexture RenderProp(GameObject prefab, WeaponFinish finish, int width, int height)
    {
        EnsureRig();
        GameObject prop = Instantiate(prefab, stage, false);
        try
        {
            Prepare(prop);
            if (prop.TryGetComponent(out WeaponFinishes finishes)) finishes.Apply(finish);
            Bounds bounds = RendererBounds(prop);
            return Shoot(bounds.center, bounds.extents.y, new Vector2(bounds.extents.x, bounds.extents.z).magnitude,
                new Vector3(1f, 0.3f, -0.35f), width, height, 1.15f);
        }
        finally
        {
            DestroyImmediate(prop);
        }
    }

    /// <summary>Un préfab d'arme ou d'accessoire posé sur le plateau : sur le layer `Preview`, ses ombres
    /// gardées, ses scripts sans effet (rien ne tourne entre sa création et sa destruction).</summary>
    private static void Prepare(GameObject go)
    {
        int layer = PreviewLayer.Index;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.On;
            r.forceRenderingOff = false;
        }
    }

    private static Bounds RendererBounds(GameObject go)
    {
        bool any = false;
        Bounds bounds = new Bounds(go.transform.position, Vector3.one * 0.1f);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }

    /// <summary>Cadre le sujet (le recul juste suffisant pour qu'il tienne, vu de `direction`), le rend
    /// dans la texture de travail, et en garde une copie.</summary>
    private RenderTexture Shoot(Vector3 center, float halfHeight, float radius, Vector3 direction, int width, int height, float margin)
    {
        width = Mathf.Clamp(width, 16, 2048);
        height = Mathf.Clamp(height, 16, 2048);
        halfHeight = Mathf.Max(halfHeight, 0.01f);
        radius = Mathf.Max(radius, 0.01f);

        float aspect = (float)width / height;
        float tanV = Mathf.Tan(shotCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanH = tanV * aspect;
        float distance = Mathf.Max(halfHeight / tanV, radius / tanH) * margin + radius * 0.3f;
        Vector3 dir = direction.normalized;
        shotCamera.transform.position = center + dir * distance;
        shotCamera.transform.LookAt(center, Vector3.up);
        shotCamera.aspect = aspect;
        shotCamera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 3f);
        shotCamera.farClipPlane = distance + radius * 3f + 1f;

        if (work == null || work.width != width || work.height != height)
        {
            if (work != null) { work.Release(); Destroy(work); }
            work = new RenderTexture(width, height, 24) { name = "Vignette (travail)", antiAliasing = 4 };
            work.Create();
        }

        var request = new RenderPipeline.StandardRequest { destination = work };
        if (RenderPipeline.SupportsRenderRequest(shotCamera, request))
        {
            RenderPipeline.SubmitRenderRequest(shotCamera, request);
        }
        else
        {
            shotCamera.targetTexture = work;
            shotCamera.Render();
            shotCamera.targetTexture = null;
        }

        var result = new RenderTexture(width, height, 0) { name = "Vignette" };
        result.Create();
        Graphics.Blit(work, result);
        return result;
    }

    private void EnsureRig()
    {
        if (stage == null)
        {
            var root = new GameObject("PlateauVignettes");
            root.transform.SetParent(transform, false);
            root.transform.position = StageOrigin;
            stage = root.transform;
        }
        if (shotCamera != null) return;
        var cameraObject = new GameObject("CameraVignettes");
        cameraObject.transform.SetParent(transform, false);
        shotCamera = cameraObject.AddComponent<Camera>();
        shotCamera.enabled = false;
        shotCamera.cullingMask = 1 << PreviewLayer.Index;
        shotCamera.clearFlags = CameraClearFlags.SolidColor;
        shotCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        // Sans HDR, URP garde la couche alpha jusqu'à la texture (voir PreviewStage).
        shotCamera.allowHDR = false;
        shotCamera.allowMSAA = true;
        shotCamera.fieldOfView = 22f;
    }
}
