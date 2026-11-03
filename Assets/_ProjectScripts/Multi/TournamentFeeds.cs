using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Le mur d'écrans de la salle d'attente du Tournoi (2026-11-03, choisi par l'utilisateur : « mur d'écrans en direct »,
/// GDD § 11, TO5) : chaque match en cours passe en direct sur l'écran de son arène, filmé par une caméra qui suit ses deux
/// robots, de côté et un peu au-dessus, comme une retransmission. Un écran sans match reste sombre.
///
/// Catégorie C pure, sur chaque machine, et seulement quand son joueur est dans la salle d'attente : rien n'est rendu
/// pour celui qui joue. Les caméras se partagent les images (une seule rend à chaque image, à tour de rôle, au plus
/// `feedRate` fois par seconde chacune) : le direct coûte au plus le rendu d'une petite vue de l'arène par image.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TournamentSite))]
public class TournamentFeeds : MonoBehaviour
{
    [Tooltip("La taille de l'image de chaque écran (16:9).")]
    [SerializeField] private Vector2Int resolution = new Vector2Int(1024, 576);

    [Tooltip("Les images par seconde de chaque écran, au plus.")]
    [SerializeField] private float feedRate = 30f;

    [Tooltip("La caméra : sa hauteur au-dessus du sol (m), entre ces deux valeurs : au-dessus des caisses (3 m), sous le bas des faisceaux des projecteurs (6,4 m).")]
    [SerializeField] private Vector2 heightRange = new Vector2(4.2f, 6f);

    [Tooltip("Sa distance aux robots (m) : au moins, au plus.")]
    [SerializeField] private Vector2 distanceRange = new Vector2(9f, 30f);

    [Tooltip("Le lissage de son mouvement (s).")]
    [SerializeField] private float smoothTime = 0.6f;

    [Tooltip("Où elle peut aller, dans chaque copie de l'arène (x, z autour de son origine) : l'aire de jeu, sous la passerelle.")]
    [SerializeField] private Rect area = Rect.MinMaxRect(-31f, -3.5f, 31f, 66.5f);

    private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly Color Idle = new Color(0.035f, 0.04f, 0.05f);

    private TournamentSite site;
    private Feed[] feeds = System.Array.Empty<Feed>();
    private MaterialPropertyBlock block;
    private int turn;

    private sealed class Feed
    {
        public Camera camera;
        public RenderTexture texture;
        public Vector3 velocity;
        public bool placed;
        public bool live;
        public bool shownLive;
        public float renderedAt = float.NegativeInfinity;
    }

    private void Awake()
    {
        site = GetComponent<TournamentSite>();
        block = new MaterialPropertyBlock();
    }

    private void OnDisable()
    {
        foreach (Feed feed in feeds)
        {
            if (feed.camera != null) feed.camera.enabled = false;
        }
    }

    private void OnDestroy()
    {
        foreach (Feed feed in feeds)
        {
            if (feed.camera != null) Destroy(feed.camera.gameObject);
            if (feed.texture != null) feed.texture.Release();
        }
    }

    private void LateUpdate()
    {
        IReadOnlyList<Renderer> screens = site.Screens;
        if (screens.Count == 0) return;
        RoundManager main = RoundManager.Instance;
        bool watching = main != null && main.IsSpawned && main.TournamentMode && main.CurrentPhase == RoundManager.Phase.Tournament
                        && ReferenceEquals(RoundManager.Local, main);
        // Les caméras et leurs images ne naissent qu'au premier tournoi regardé : hors du Tournoi, rien ne coûte.
        if (feeds.Length != screens.Count)
        {
            if (!watching) return;
            CreateFeeds(screens.Count);
        }
        int liveCount = 0;
        for (int i = 0; i < feeds.Length; i++)
        {
            Feed feed = feeds[i];
            feed.live = watching && TryGetDuel(i, out Vector3 a, out Vector3 b) && Aim(feed, i, a, b);
            if (feed.live) liveCount++;
            else feed.placed = false;
            feed.camera.enabled = false;
            if (feed.live != feed.shownLive) ShowOnScreen(screens[i], feed);
        }
        if (liveCount == 0) return;

        // À tour de rôle : une caméra rend à cette image, celle qui attend depuis le plus longtemps, si son tour est venu.
        float interval = 1f / Mathf.Max(1f, feedRate);
        for (int k = 0; k < feeds.Length; k++)
        {
            int i = (turn + k) % feeds.Length;
            Feed feed = feeds[i];
            if (!feed.live || Time.unscaledTime - feed.renderedAt < interval) continue;
            feed.camera.enabled = true;
            feed.renderedAt = Time.unscaledTime;
            turn = i + 1;
            break;
        }
    }

    private void CreateFeeds(int count)
    {
        OnDestroy();
        feeds = new Feed[count];
        for (int i = 0; i < count; i++)
        {
            var texture = new RenderTexture(resolution.x, resolution.y, 24) { name = $"Tournoi_Direct_{i + 1}" };
            texture.Create();
            var go = new GameObject($"Caméra du direct {i + 1}");
            go.transform.SetParent(transform, false);
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.targetTexture = texture;
            camera.fieldOfView = 40f;
            camera.nearClipPlane = 0.2f;
            camera.farClipPlane = 250f;
            // Ni la vue subjective, ni l'aperçu des menus, ni l'interface.
            camera.cullingMask = ~((1 << 5) | (1 << 8) | (1 << 9));
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            // Le post-traitement de la vue du joueur passe déjà sur l'écran : pas deux fois.
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            feeds[i] = new Feed { camera = camera, texture = texture };
            ShowOnScreen(site.Screens[i], feeds[i]);
        }
    }

    private void ShowOnScreen(Renderer screen, Feed feed)
    {
        feed.shownLive = feed.live;
        if (screen == null) return;
        screen.GetPropertyBlock(block);
        if (feed.live)
        {
            block.SetTexture(BaseMap, feed.texture);
            block.SetColor(BaseColor, Color.white);
        }
        else
        {
            block.SetTexture(BaseMap, Texture2D.whiteTexture);
            block.SetColor(BaseColor, Idle);
        }
        screen.SetPropertyBlock(block);
    }

    /// <summary>Les deux robots du match qui se joue dans cette copie de l'arène, ou faux : aucun.</summary>
    private static bool TryGetDuel(int arena, out Vector3 a, out Vector3 b)
    {
        a = b = default;
        IReadOnlyList<RoundManager> lanes = RoundManager.Lanes;
        for (int i = 0; i < lanes.Count; i++)
        {
            RoundManager lane = lanes[i];
            if (lane == null || !lane.IsSpawned || lane.Lane.arena != arena || lane.Lane.roundsToWin == 0) continue;
            return TryGetPosition(lane.Lane.a, out a) & TryGetPosition(lane.Lane.b, out b);
        }
        return false;
    }

    private static bool TryGetPosition(ulong clientId, out Vector3 position)
    {
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player == null || player.OwnerClientId != clientId) continue;
            position = player.transform.position;
            return true;
        }
        position = default;
        return false;
    }

    /// <summary>Place la caméra d'un écran sur le duel : faux si les robots ne sont pas dans son arène (pas encore
    /// replacés).</summary>
    private bool Aim(Feed feed, int arena, Vector3 a, Vector3 b)
    {
        Vector3 origin = site.ArenaOffset(arena);
        if (site.ArenaAt(a) != arena || site.ArenaAt(b) != arena) return false;
        Transform t = feed.camera.transform;
        Vector3 goal = FeedPosition(a - origin, b - origin, feed.placed ? t.position - origin : (Vector3?)null, heightRange, distanceRange, area) + origin;
        Vector3 focus = (a + b) * 0.5f + Vector3.up * 1.1f;
        if (!feed.placed)
        {
            t.position = goal;
            feed.velocity = Vector3.zero;
            feed.placed = true;
        }
        else t.position = Vector3.SmoothDamp(t.position, goal, ref feed.velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        t.rotation = Quaternion.LookRotation(focus - t.position, Vector3.up);
        float spread = new Vector2(a.x - b.x, a.z - b.z).magnitude;
        feed.camera.fieldOfView = FeedFieldOfView(spread, Vector3.Distance(t.position, focus), feed.camera.aspect);
        return true;
    }

    /// <summary>
    /// Où se tient la caméra d'un duel, dans le repère de son arène : de côté par rapport aux deux robots (`a`, `b`), à une
    /// distance qui grandit avec leur écart, un peu au-dessus, et du côté où elle était déjà (`previous` : elle ne passe pas
    /// d'un côté à l'autre quand ils tournent l'un autour de l'autre) ; jamais hors de l'aire de jeu. Fonction pure,
    /// testée.
    /// </summary>
    public static Vector3 FeedPosition(Vector3 a, Vector3 b, Vector3? previous, Vector2 heights, Vector2 distances, Rect area)
    {
        Vector3 middle = (a + b) * 0.5f;
        var axis = new Vector3(b.x - a.x, 0f, b.z - a.z);
        float spread = axis.magnitude;
        Vector3 side = spread > 0.01f ? Vector3.Cross(Vector3.up, axis / spread) : Vector3.right;
        float distance = Mathf.Clamp(0.75f * spread + 6f, distances.x, distances.y);
        float height = Mathf.Clamp(heights.x + 0.08f * spread, heights.x, heights.y);
        Vector3 one = Clamp(middle + side * distance, area), other = Clamp(middle - side * distance, area);
        // Du côté où il y a le plus de recul ; à égalité près, celui où elle était.
        float roomOne = (one - middle).sqrMagnitude, roomOther = (other - middle).sqrMagnitude;
        bool pickOne = roomOne >= roomOther;
        if (previous.HasValue && Mathf.Abs(Mathf.Sqrt(roomOne) - Mathf.Sqrt(roomOther)) < 4f)
            pickOne = (one - previous.Value).sqrMagnitude <= (other - previous.Value).sqrMagnitude;
        Vector3 chosen = pickOne ? one : other;
        chosen.y = middle.y + height;
        return chosen;
    }

    private static Vector3 Clamp(Vector3 p, Rect area) => new Vector3(Mathf.Clamp(p.x, area.xMin, area.xMax), p.y, Mathf.Clamp(p.z, area.yMin, area.yMax));

    /// <summary>Le champ de vision (vertical) qui montre les deux robots, `spread` mètres l'un de l'autre, vus de `distance`
    /// mètres, avec de la marge ; entre 22° et 70°. Fonction pure, testée.</summary>
    public static float FeedFieldOfView(float spread, float distance, float aspect)
    {
        float horizontal = 2f * Mathf.Atan((spread * 0.5f + 2.5f) / Mathf.Max(1f, distance));
        float vertical = 2f * Mathf.Atan(Mathf.Tan(horizontal * 0.5f) / Mathf.Max(0.5f, aspect)) * Mathf.Rad2Deg;
        return Mathf.Clamp(vertical, 22f, 70f);
    }
}
