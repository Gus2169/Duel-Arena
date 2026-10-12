using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Le robot tournant de l'écran Personnalisation. Construit UNE SEULE FOIS, au premier affichage,
/// en instanciant directement le sous-arbre « Model » du VRAI `Player.prefab` — jamais une copie
/// maintenue à la main, pour que l'aperçu suive automatiquement le jour où le robot définitif
/// remplacera le Y Bot (voir CLAUDE.md, « Pourquoi le personnage passe avant... »).
///
/// 🚨 `Instantiate` prend directement l'enfant « Model » de l'ASSET, jamais la racine du prefab.
/// Instancier la racine (même pour la détruire aussitôt) crée un `NetworkObject` — et le détruire
/// hors de tout contexte réseau fait lever une NullReferenceException DANS Netcode lui-même
/// (`NetworkObject.OnDestroy` → `NetworkSpawnManager.RemoveNetworkObjectFromSceneChangedUpdates`,
/// qui suppose un état d'enregistrement que l'objet n'a jamais eu). Vécu en testant cette page :
/// l'erreur apparaît au moment où l'écran Personnalisation s'affiche pour la première fois, sans
/// lien apparent avec le code qui vient de s'exécuter — raison de plus pour ne jamais faire exister
/// un `NetworkObject`, même une frame, en dehors d'une vraie session réseau.
///
/// Tout le reste de l'instance (réseau, caméra, armes, hitbox, scripts de gameplay) n'existe donc
/// JAMAIS dans l'aperçu : il ne joue que l'ANIMATION PAR DÉFAUT du contrôleur (debout, au repos),
/// sans les corrections de PlayerAnimator (inclinaison du buste, calage des pieds...), qui
/// dépendent de PlayerLocomotion et n'ont pas leur place ici. Limite connue et acceptée : un
/// aperçu de menu, pas une simulation.
///
/// La couleur suit le swatch choisi dans ArmoryController EN TEMPS RÉEL, par le même mécanisme que
/// PlayerPaint (MaterialPropertyBlock sur `_BaseColor`), appliqué aux renderers dont le CHEMIN
/// relatif à « Model » correspond à ceux que `PlayerPaint.PaintedRenderers` liste SUR L'ASSET (lu
/// sans l'instancier : un composant d'un prefab non instancié expose ses champs sérialisés comme
/// n'importe quel autre). Une seule source pour « quoi peindre », jamais dupliquée.
/// </summary>
public class CharacterPreviewController : MonoBehaviour
{
    [Tooltip("Le prefab joueur complet (Player.prefab). Seul son enfant « Model » est instancié, jamais la racine et son NetworkObject.")]
    [SerializeField] private GameObject playerPrefab;

    [SerializeField] private PreviewStage stage;
    [SerializeField] private UIDocument document;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private Image viewport;
    private MaterialPropertyBlock propertyBlock;
    private Renderer[] paintedRenderers = System.Array.Empty<Renderer>();
    private bool built;
    private Color32? paint;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        if (stage == null) stage = GetComponent<PreviewStage>();
        viewport = document.rootVisualElement.Q<Image>("characterPreviewImage");
        BuildOnce();
    }

    /// <summary>La palette des robots, celle de PlayerPaint lue sur le préfab joueur : le menu
    /// propose exactement les couleurs que le serveur attribue.</summary>
    public IReadOnlyList<Color32> Palette
    {
        get
        {
            PlayerPaint paint = playerPrefab != null ? playerPrefab.GetComponent<PlayerPaint>() : null;
            return paint != null ? paint.Palette : System.Array.Empty<Color32>();
        }
    }

    /// <summary>Appelé par MainMenuController quand l'écran Personnalisation s'affiche.</summary>
    public void Show()
    {
        BuildOnce();
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
        if (paintedRenderers.Length == 0) return;
        propertyBlock ??= new MaterialPropertyBlock();
        foreach (Renderer r in paintedRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            r.SetPropertyBlock(propertyBlock);
        }
    }

    private void BuildOnce()
    {
        if (built || playerPrefab == null || stage == null || stage.CharacterPivot == null) return;
        built = true;

        Transform assetModel = playerPrefab.transform.Find("Model");
        if (assetModel == null) return;

        GameObject modelInstance = Instantiate(assetModel.gameObject, stage.CharacterPivot, false);
        modelInstance.transform.localPosition = Vector3.zero;
        modelInstance.transform.localRotation = Quaternion.identity;
        SetLayerRecursively(modelInstance, PreviewLayer.Index);

        PlayerPaint assetPaint = playerPrefab.GetComponent<PlayerPaint>();
        if (assetPaint != null)
        {
            var found = new List<Renderer>();
            foreach (Renderer assetRenderer in assetPaint.PaintedRenderers)
            {
                if (assetRenderer == null) continue;
                Transform match = ResolveRelative(assetModel, assetRenderer.transform, modelInstance.transform);
                Renderer r = match != null ? match.GetComponent<Renderer>() : null;
                if (r != null) found.Add(r);
            }
            paintedRenderers = found.ToArray();
        }

        // La couleur initiale (préférence sauvegardée ou défaut) vient d'ArmoryController, qui
        // seul connaît la palette : arrivée avant la construction, elle a été retenue.
        if (paint.HasValue) SetPaint(paint.Value);
        stage.Hide();
    }

    /// <summary>Retrouve, sous `instanceRoot`, l'équivalent de `target` (un descendant de
    /// `assetRoot`) — les deux hiérarchies sont identiques, `instanceRoot` venant d'instancier
    /// `assetRoot` tel quel.</summary>
    private static Transform ResolveRelative(Transform assetRoot, Transform target, Transform instanceRoot)
    {
        if (target == assetRoot) return instanceRoot;

        var segments = new List<string>();
        for (Transform t = target; t != null && t != assetRoot; t = t.parent) segments.Add(t.name);
        segments.Reverse();

        return instanceRoot.Find(string.Join("/", segments));
    }

    internal static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
    }
}
