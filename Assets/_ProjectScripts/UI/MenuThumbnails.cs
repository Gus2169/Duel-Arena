using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

/// <summary>
/// Les vignettes du menu (2026-10-17, pour les maquettes de l'utilisateur, toutes faites d'images) : le robot
/// dans chaque couleur, dans une pose, en pied ou en buste ; chaque arme et chaque accessoire, de profil ; et
/// ce qu'on voit en visée (2026-10-27) : l'arme vue de l'œil qui vise, le réticule de son viseur allumé.
///
/// Rendues dans le menu, à partir des vrais préfabs (le robot du préfab joueur, les armes de la panoplie,
/// les accessoires du catalogue), jamais cuites à la main : elles suivent le jour où un modèle change. Une
/// caméra à elle, sur le layer `Preview`, à 1 000 m sous le plateau d'aperçu (PreviewStage), dont la
/// caméra ne voit pas si loin ; les deux lumières directionnelles du plateau les éclairent pareil.
///
/// Rendues à la demande, deux par image au plus (le menu ne saccade pas quand un écran en demande vingt),
/// puis gardées : une même vignette demandée deux fois ne se rend qu'une fois. Chaque rendu passe par une
/// texture à anticrénelage, recopiée dans une texture simple, la seule gardée.
///
/// Les portraits des armes (WeaponPortraits, 2026-10-27), eux, sont rendus tous d'un coup à l'ouverture du
/// menu, et lui survivent : la préparation d'une manche et la fenêtre du stand les montrent.
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

    /// <summary>Le champ de la caméra des vignettes de profil (degrés, vertical).</summary>
    private const float ShotFieldOfView = 22f;
    /// <summary>En visée, la part du cadre que prend la vitre du viseur : assez de son corps autour pour le
    /// reconnaître, assez grande pour lire le réticule.</summary>
    private const float LensFill = 0.55f;
    /// <summary>Sans viseur, la part du cadre que prennent la hausse et le guidon.</summary>
    private const float IronSightsFill = 0.8f;
    /// <summary>Sans viseur, le demi-champ au plus (tangente) : la hausse d'un fusil est si près de l'œil que sa
    /// boîte ouvrirait le cadre à 55°, le guidon s'y perdrait.</summary>
    private const float IronSightsMaxTangent = 0.21f;

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
        ReleaseWork();
    }

    private void ReleaseWork()
    {
        if (work == null) return;
        work.Release();
        if (Application.isPlaying) Destroy(work);
        else DestroyImmediate(work);
        work = null;
    }

    private void Start() => PrepareWeaponPortraits();

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
        // Le rendu (RenderStyle) est dans la clé : une vignette faite avant un changement de rendu ne sert plus.
        string key = $"robot/{(int)RenderStyle.Current}/{(int)shot.chassis}/{paint.r},{paint.g},{paint.b}/{(pose != null ? pose.name : "-")}/{shot.time:0.00}/{shot.yaw:0}/{shot.framing}/{shot.width}x{shot.height}";
        Request(key, () => RenderRobot(shot, pose, paint), ready);
    }

    /// <summary>L'arme `data`, dans sa finition, avec les accessoires choisis pour la place `slot` (aucun
    /// si `attachments` vaut WeaponAttachments.None), de profil.</summary>
    public void Weapon(WeaponData data, WeaponFinish finish, WeaponAttachments attachments, int slot, int width, int height, Action<Texture> ready)
    {
        if (data == null) { ready?.Invoke(null); return; }
        string fitted = $"{attachments.Sight(slot)}.{(int)attachments.Grip(slot)}.{(attachments.Laser(slot) ? 1 : 0)}.{(attachments.ExtendedMagazine(slot) ? 1 : 0)}";
        string key = $"arme/{data.name}/{(int)finish}/{fitted}/{width}x{height}";
        Request(key, () => RenderWeapon(data, finish, attachments, slot, width, height), ready);
    }

    /// <summary>Un accessoire seul, dans la finition de l'arme qui le porte, de profil.</summary>
    public void Attachment(GameObject prefab, WeaponFinish finish, int width, int height, Action<Texture> ready)
    {
        if (prefab == null) { ready?.Invoke(null); return; }
        string key = $"accessoire/{prefab.name}/{(int)finish}/{width}x{height}";
        Request(key, () => RenderProp(prefab, finish, width, height), ready);
    }

    /// <summary>
    /// Ce qu'on voit en visée (2026-10-27, demandé par l'utilisateur, « comme dans Rainbow Six ») : l'arme
    /// `data` dans sa finition, avec le viseur choisi pour la place `slot` (sa mire à elle sans viseur), vue de
    /// l'œil qui vise. L'œil est là où la vue subjective le met (AttachmentMount.SightOf : derrière la mire du
    /// viseur, à sa distance de l'œil, ou derrière la hausse ; un pistolet à bout de bras), et regarde dans l'axe.
    /// Le réticule est celui du jeu, dessiné à l'infini par la vitre (DuelArena/ViseurReticule) : il tombe au
    /// centre, à sa vraie taille par rapport à la fenêtre. Seuls le viseur et le laser (sur le flanc) peuvent se
    /// voir de l'œil : la poignée et le chargeur, sous l'arme, ne comptent pas.
    ///
    /// Rendue sur `background`, le fond de ce qui la montre : la vitre, teintée et à peine opaque, s'y compose
    /// comme en jeu. Sur un fond transparent, l'interface (qui ne lit pas l'alpha prémultiplié) perdait la
    /// teinte des vitres et amincissait le bord du réticule.
    /// </summary>
    public void SightPicture(WeaponData data, WeaponFinish finish, WeaponAttachments attachments, int slot, int width, int height,
                             Color32 background, Action<Texture> ready)
    {
        if (data == null) { ready?.Invoke(null); return; }
        WeaponAttachments seen = SeenFromTheEye(attachments, slot);
        string key = $"visee/{data.name}/{(int)finish}/{seen.Sight(slot)}.{(seen.Laser(slot) ? 1 : 0)}"
                     + $"/{background.r},{background.g},{background.b},{background.a}/{width}x{height}";
        Request(key, () => RenderSightPicture(data, finish, seen, slot, width, height, background), ready);
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Tests : ce qu'on voit en visée, rendu tout de suite sur un fond transparent, hors de la file et du
    /// cache. La texture rendue est à détruire par l'appelant.</summary>
    public RenderTexture RenderSightPictureNow(WeaponData data, WeaponFinish finish, WeaponAttachments attachments, int slot, int width, int height)
    {
        try { return data != null ? RenderSightPicture(data, finish, SeenFromTheEye(attachments, slot), slot, width, height, Color.clear) : null; }
        finally { ReleaseWork(); }
    }
#endif

    private static WeaponAttachments SeenFromTheEye(WeaponAttachments attachments, int slot)
        => WeaponAttachments.None.WithSight(slot, attachments.Sight(slot)).WithLaser(slot, attachments.Laser(slot));

    /// <summary>
    /// Les portraits des armes de la panoplie (WeaponPortraits, 2026-10-27, demandés par l'utilisateur) :
    /// chacune de profil, sans accessoire, dans sa finition d'origine. Rendus tout de suite, hors de la file : le
    /// stand ouvert dans l'Editor quitte le menu dès sa première image (ShootingRangeBootstrap). Une arme déjà
    /// portraiturée ne se rend plus : une fois par lancement du jeu.
    /// </summary>
    public void PrepareWeaponPortraits()
    {
        WeaponController arms = playerPrefab != null ? playerPrefab.GetComponentInChildren<WeaponController>(true) : null;
        if (arms == null) return;
        try
        {
            IReadOnlyList<WeaponData> loadout = arms.Loadout;
            for (int slot = 0; slot < loadout.Count; slot++)
            {
                WeaponData data = loadout[slot];
                if (data == null || WeaponPortraits.Of(data) != null) continue;
                RenderTexture rendered = null;
                try
                {
                    rendered = RenderWeapon(data, WeaponFinish.Origine, WeaponAttachments.None, slot, WeaponPortraits.Width, WeaponPortraits.Height, whole: true);
                    if (rendered != null) WeaponPortraits.Keep(data, ToTexture(rendered, $"Portrait {data.weaponName}"));
                }
                catch (Exception e) { Debug.LogWarning($"[Menu] Portrait de {data.weaponName} : {e}", this); }
                finally
                {
                    if (rendered != null)
                    {
                        // Graphics.Blit (Capture) l'a laissée active.
                        if (RenderTexture.active == rendered) RenderTexture.active = null;
                        rendered.Release();
                        if (Application.isPlaying) Destroy(rendered);
                        else DestroyImmediate(rendered);
                    }
                }
            }
        }
        finally { ReleaseWork(); }
    }

    /// <summary>Une copie de `rendered` en mémoire : elle passe d'une scène à l'autre, et se dessine aussi bien
    /// dans l'interface que dans OnGUI.</summary>
    private static Texture2D ToTexture(RenderTexture rendered, string name)
    {
        var texture = new Texture2D(rendered.width, rendered.height, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = rendered;
            texture.ReadPixels(new Rect(0, 0, rendered.width, rendered.height), 0, 0, false);
            texture.Apply(false);
        }
        finally
        {
            RenderTexture.active = previous;
        }
        return texture;
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

    /// <summary>L'arme de profil. Les vignettes de l'armurerie la serrent (ses bouts peuvent toucher le cadre) ;
    /// `whole` la montre entière, avec sa marge : les portraits (WeaponPortraits).</summary>
    private RenderTexture RenderWeapon(WeaponData data, WeaponFinish finish, WeaponAttachments attachments, int slot, int width, int height,
                                       bool whole = false)
    {
        GameObject prefab = data.modelPrefab;
        if (prefab == null) return null;
        EnsureRig();
        GameObject weapon = Instantiate(prefab, stage, false);
        try
        {
            Prepare(weapon);
            if (weapon.TryGetComponent(out WeaponFinishes finishes)) finishes.Apply(finish);
            AttachmentMount.Apply(weapon.transform, data, attachments, slot, finish);
            Bounds bounds = RendererBounds(weapon);
            // Vue de côté, la longueur de l'arme (le long du canon, +Z) est sa largeur à l'écran.
            float radius = whole ? bounds.extents.z : new Vector2(bounds.extents.x, bounds.extents.z).magnitude * 0.82f;
            return Shoot(bounds.center, bounds.extents.y, radius, new Vector3(1f, 0.08f, 0f), width, height, whole ? 1.06f : 1.04f);
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

    private RenderTexture RenderSightPicture(WeaponData data, WeaponFinish finish, WeaponAttachments attachments, int slot, int width, int height,
                                             Color background)
    {
        GameObject prefab = data.modelPrefab;
        if (prefab == null) return null;
        ClampSize(ref width, ref height);
        // Sans viseur, une arme de précision se vise par sa lunette d'origine, que le HUD dessine.
        if (attachments.Sight(slot) == 0 && data.scopeMagnification > 1f) return DrawBuiltInScope(width, height);
        EnsureRig();
        GameObject weapon = Instantiate(prefab, stage, false);
        try
        {
            Prepare(weapon);
            if (weapon.TryGetComponent(out WeaponFinishes finishes)) finishes.Apply(finish);
            AttachmentMount.Apply(weapon.transform, data, attachments, slot, finish);

            Transform sight = AttachmentMount.SightOf(weapon.GetComponent<WeaponModel>(), out float eyeDistance);
            if (sight == null) return null;
            Transform eye = shotCamera.transform;
            eye.SetPositionAndRotation(sight.position - sight.forward * eyeDistance, Quaternion.LookRotation(sight.forward, sight.up));

            // Cadrée sur la fenêtre du viseur ; sans viseur, sur ce qu'un viseur cache : la hausse et le guidon.
            float aspect = (float)width / height;
            WeaponAttachment mounted = AttachmentMount.Mounted(weapon.transform, AttachmentCategory.Sight);
            float tangent;
            if (mounted != null && mounted.Lens != null)
            {
                tangent = FrameTangent(new[] { mounted.Lens }, eye.worldToLocalMatrix, aspect, LensFill);
                // Une lunette se voit comme dans sa vue grossie (FirstPersonView.EyepieceScoped) : son seul
                // œilleton autour de la vitre. Le grossissement ne change rien d'autre à l'image : la vitre et
                // le réticule, dans la même caméra, grossissent ensemble.
                if (mounted.Magnification > 1f)
                    foreach (Renderer r in mounted.GetComponentsInChildren<Renderer>(true))
                        if (!mounted.ShownWhenScoped(r)) r.forceRenderingOff = true;
            }
            else
            {
                tangent = FrameTangent(IronSights(weapon.transform, data), eye.worldToLocalMatrix, aspect, IronSightsFill);
                tangent = tangent > 0f ? Mathf.Min(tangent, IronSightsMaxTangent) : IronSightsMaxTangent;
            }
            if (tangent <= 0f) return null;

            shotCamera.fieldOfView = 2f * Mathf.Atan(tangent) * Mathf.Rad2Deg;
            shotCamera.aspect = aspect;
            shotCamera.nearClipPlane = 0.005f;
            shotCamera.farClipPlane = 4f;
            shotCamera.backgroundColor = background;
            return Capture(width, height);
        }
        finally
        {
            shotCamera.backgroundColor = Color.clear;
            DestroyImmediate(weapon);
        }
    }

    /// <summary>
    /// La lunette d'origine d'une arme de précision telle que le HUD la dessine en jeu (ScopeOverlay) :
    /// du noir autour d'un disque de 92 % de la hauteur, et une croix noire fine, aux branches épaissies vers le
    /// bord, sauf celle du haut. Dessinée, pas rendue : en jeu, on ne regarde pas son modèle. Le disque montre un
    /// ciel clair au-dessus d'un sol sombre, pour que la croix se lise comme sur l'arène. Traits élargis à
    /// l'échelle d'une vignette (2 et 6 pixels en 1080p y seraient invisibles).
    /// </summary>
    private static RenderTexture DrawBuiltInScope(int width, int height)
    {
        var pixels = new Color32[width * height];
        float cx = width * 0.5f, cy = height * 0.5f;
        float r = height * 0.46f;
        float thin = Mathf.Max(1.5f, height * 2f / 1080f), thick = Mathf.Max(4f, height * 6f / 1080f), gap = thin * 3f;
        var sky = new Color(0.62f, 0.68f, 0.74f);
        var horizon = new Color(0.78f, 0.80f, 0.80f);
        var ground = new Color(0.34f, 0.33f, 0.31f);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
            Color scene = dy >= 0f ? Color.Lerp(horizon, sky, dy / r) : Color.Lerp(horizon, ground, Mathf.Clamp01(-dy / (r * 0.25f)));
            // La croix : fine jusqu'à 55 % du rayon, épaisse au-delà (pas en haut), coupée au centre.
            float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
            bool horizontal = ax > gap && (ay <= thin * 0.5f || (ax >= r * 0.55f && ay <= thick * 0.5f));
            bool vertical = ay > gap && (ax <= thin * 0.5f || (dy < 0f && ay >= r * 0.55f && ax <= thick * 0.5f));
            bool dot = ax <= thin && ay <= thin;
            if (horizontal || vertical || dot) scene = Color.Lerp(scene, Color.black, 0.9f);
            // Le bord du disque s'assombrit (le masque du HUD), puis le noir.
            float shade = d >= 1f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 1f, d)) * 0.85f;
            float edge = Mathf.Clamp01((d - 1f) * r + 0.5f);   // un pixel de fondu sur le bord
            shade = Mathf.Lerp(shade, 1f, edge);
            pixels[y * width + x] = (Color32)Color.Lerp(scene, Color.black, shade);
        }
        var image = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Lunette d'origine" };
        try
        {
            image.SetPixels32(pixels);
            image.Apply();
            var result = new RenderTexture(width, height, 0) { name = "Vignette" };
            result.Create();
            Graphics.Blit(image, result);
            return result;
        }
        finally { DestroyImmediate(image); }
    }

    /// <summary>Les pièces de l'arme qu'un viseur cache (WeaponAttachment.Hides) : sa hausse et son guidon.</summary>
    private static List<Renderer> IronSights(Transform weapon, WeaponData data)
    {
        var renderers = new List<Renderer>();
        GameObject anySight = data.attachmentCatalog != null ? data.attachmentCatalog.SightPrefab(1) : null;
        if (anySight == null || !anySight.TryGetComponent(out WeaponAttachment sight)) return renderers;
        foreach (string part in sight.Hides)
        {
            Transform piece = weapon.Find(part);
            if (piece != null) renderers.AddRange(piece.GetComponentsInChildren<Renderer>());
        }
        return renderers;
    }

    /// <summary>
    /// La tangente du demi-champ vertical d'une caméra (de repère `worldToView`, de rapport `aspect`) qui fait
    /// tenir `framed` sur la part `fill` du cadre, l'axe au centre : la plus forte pente, verticale ou
    /// horizontale, des coins de leurs boîtes devant elle. 0 s'il n'y a rien devant elle.
    /// </summary>
    private static float FrameTangent(IEnumerable<Renderer> framed, Matrix4x4 worldToView, float aspect, float fill)
    {
        float right = 0f, up = 0f;
        foreach (Renderer r in framed)
        {
            Bounds local = r.localBounds;
            Matrix4x4 toView = worldToView * r.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = toView.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f)));
                if (p.z < 0.01f) continue;
                right = Mathf.Max(right, Mathf.Abs(p.x / p.z));
                up = Mathf.Max(up, Mathf.Abs(p.y / p.z));
            }
        }
        return Mathf.Max(up, right / aspect) / fill;
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
        ClampSize(ref width, ref height);
        halfHeight = Mathf.Max(halfHeight, 0.01f);
        radius = Mathf.Max(radius, 0.01f);

        float aspect = (float)width / height;
        shotCamera.fieldOfView = ShotFieldOfView;
        float tanV = Mathf.Tan(ShotFieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanH = tanV * aspect;
        float distance = Mathf.Max(halfHeight / tanV, radius / tanH) * margin + radius * 0.3f;
        Vector3 dir = direction.normalized;
        shotCamera.transform.position = center + dir * distance;
        shotCamera.transform.LookAt(center, Vector3.up);
        shotCamera.aspect = aspect;
        shotCamera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 3f);
        shotCamera.farClipPlane = distance + radius * 3f + 1f;
        return Capture(width, height);
    }

    private static void ClampSize(ref int width, ref int height)
    {
        width = Mathf.Clamp(width, 16, 2048);
        height = Mathf.Clamp(height, 16, 2048);
    }

    /// <summary>Rend la caméra des vignettes, telle qu'elle est posée, dans la texture de travail, et en garde
    /// une copie.</summary>
    private RenderTexture Capture(int width, int height)
    {
        if (work == null || work.width != width || work.height != height)
        {
            ReleaseWork();
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
        shotCamera.backgroundColor = Color.clear;
        // Sans HDR, URP garde la couche alpha jusqu'à la texture (voir PreviewStage).
        shotCamera.allowHDR = false;
        shotCamera.allowMSAA = true;
        shotCamera.fieldOfView = ShotFieldOfView;
    }
}
