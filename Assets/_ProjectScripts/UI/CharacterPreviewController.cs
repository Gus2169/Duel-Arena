using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UIElements;

/// <summary>
/// Le robot tournant de la partie ROBOT de Personnalisation. Construit UNE SEULE FOIS, au premier
/// affichage, à partir du sous-arbre « Model » du VRAI `Player.prefab` — jamais une copie maintenue à
/// la main, pour que l'aperçu suive automatiquement le jour où le robot définitif remplacera le Y Bot.
///
/// 🚨 La copie prend directement l'enfant « Model » de l'ASSET, jamais la racine du prefab.
/// Instancier la racine (même pour la détruire aussitôt) crée un `NetworkObject` — et le détruire
/// hors de tout contexte réseau fait lever une NullReferenceException DANS Netcode lui-même
/// (`NetworkObject.OnDestroy` → `NetworkSpawnManager.RemoveNetworkObjectFromSceneChangedUpdates`).
///
/// Le robot seul (2026-10-16, demandé par l'utilisateur) : ni arme en main, ni gant du taser, ni
/// scripts (RobotAppearance), et l'attente à mains nues (`idleClip`, celle des spectateurs) jouée en
/// boucle par un graphe d'animation à lui : le contrôleur du jeu, qui tient une arme, n'a rien à faire
/// ici. Les armes ont leur propre partie, l'armurerie.
///
/// La couleur suit le swatch choisi dans ArmoryController EN TEMPS RÉEL, par le même mécanisme que
/// PlayerPaint (MaterialPropertyBlock sur `_BaseColor`), appliqué aux renderers dont le CHEMIN
/// relatif à « Model » correspond à ceux que `PlayerPaint.PaintedRenderers` liste SUR L'ASSET (lu
/// sans l'instancier). Une seule source pour « quoi peindre », jamais dupliquée.
/// </summary>
public class CharacterPreviewController : MonoBehaviour
{
    [Tooltip("Le prefab joueur complet (Player.prefab). Seul son enfant « Model » est copié, jamais la racine et son NetworkObject.")]
    [SerializeField] private GameObject playerPrefab;

    [Tooltip("L'attente à mains nues jouée en boucle par le robot de Personnalisation (celle des spectateurs : Y Bot@Idle). Vide : la pose de repos du contrôleur du jeu.")]
    [SerializeField] private AnimationClip idleClip;

    [SerializeField] private PreviewStage stage;
    [SerializeField] private UIDocument document;

    private Image viewport;
    private Renderer[] paintedRenderers = System.Array.Empty<Renderer>();
    private bool built;
    private Color32? paint;
    private GameObject modelInstance;
    private ChassisId chassis = ChassisId.CT01;

    private PlayableGraph idleGraph;
    private AnimationClipPlayable idlePlayable;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        if (stage == null) stage = GetComponent<PreviewStage>();
        viewport = document.rootVisualElement.Q<Image>("characterPreviewImage");
        BuildOnce();
    }

    private void OnDestroy()
    {
        if (idleGraph.IsValid()) idleGraph.Destroy();
    }

    private void Update()
    {
        // L'attente boucle, quel que soit le réglage de boucle de son import.
        if (!idlePlayable.IsValid() || idleClip == null || idleClip.length <= 0f) return;
        double time = idlePlayable.GetTime();
        if (time >= idleClip.length) idlePlayable.SetTime(time % idleClip.length);
    }

    /// <summary>La palette des robots, celle de PlayerPaint lue sur le préfab joueur : le menu
    /// propose exactement les couleurs que le serveur attribue.</summary>
    public IReadOnlyList<Color32> Palette
    {
        get
        {
            PlayerPaint assetPaint = playerPrefab != null ? playerPrefab.GetComponent<PlayerPaint>() : null;
            return assetPaint != null ? assetPaint.Palette : System.Array.Empty<Color32>();
        }
    }

    /// <summary>La vie d'un robot (Health du préfab joueur) : l'armurerie mesure les dégâts d'une arme
    /// à cette aune.</summary>
    public float RobotMaxHealth
    {
        get
        {
            Health health = playerPrefab != null ? playerPrefab.GetComponent<Health>() : null;
            return health != null ? health.Max : 100f;
        }
    }

    /// <summary>Appelé par MainMenuController quand l'écran Personnalisation s'affiche.</summary>
    public void Show()
    {
        BuildOnce();
        // Le cadrage mesure la pose du robot : celle de l'attente, pas celle de repos (bras en croix).
        if (idleGraph.IsValid()) idleGraph.Evaluate(0f);
        if (stage != null) stage.ShowCharacter(viewport);
    }

    /// <summary>Appelé quand on quitte l'écran Personnalisation — coupe la caméra d'aperçu, inutile
    /// tant qu'elle n'est pas regardée.</summary>
    public void Hide() => stage?.Hide();

    /// <summary>Retenue si le robot n'est pas encore construit : l'ordre des OnEnable entre
    /// ArmoryController (qui envoie la couleur initiale) et ce composant n'est pas garanti.</summary>
    public void SetPaint(Color32 color)
    {
        paint = color;
        RobotAppearance.Paint(paintedRenderers, color);
    }

    /// <summary>Le châssis montré (RobotChassis), retenu de même.</summary>
    public void SetChassis(ChassisId id)
    {
        chassis = id;
        if (modelInstance != null) RobotChassis.Apply(modelInstance.transform, id);
    }

    private void BuildOnce()
    {
        if (built || playerPrefab == null || stage == null || stage.CharacterPivot == null) return;
        built = true;

        Transform assetModel = playerPrefab.transform.Find("Model");
        if (assetModel == null) return;

        modelInstance = RobotAppearance.Copy(assetModel.gameObject, stage.CharacterPivot, "RobotPersonnalisation");
        RobotChassis.Apply(modelInstance.transform, chassis);
        PlayIdle(modelInstance.GetComponent<Animator>());

        paintedRenderers = RobotAppearance.PaintedParts(playerPrefab, assetModel, modelInstance);

        // La couleur initiale (préférence sauvegardée ou défaut) vient d'ArmoryController, qui
        // seul connaît la palette : arrivée avant la construction, elle a été retenue.
        if (paint.HasValue) SetPaint(paint.Value);
        stage.Hide();
    }

    /// <summary>L'attente à mains nues, en boucle, à la place du contrôleur du jeu.</summary>
    private void PlayIdle(Animator animator)
    {
        if (animator == null || idleClip == null) return;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.runtimeAnimatorController = null;

        idleGraph = PlayableGraph.Create("AttentePersonnalisation");
        idleGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(idleGraph, "Animation", animator);
        idlePlayable = AnimationClipPlayable.Create(idleGraph, idleClip);
        output.SetSourcePlayable(idlePlayable);
        idleGraph.Play();
    }

    internal static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
    }
}
