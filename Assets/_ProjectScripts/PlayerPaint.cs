using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// La peinture du robot : une couleur vive par joueur, choisie par le SERVEUR (2026-10-08).
///
/// Le GDD (§ 13) et la bible veulent que chaque robot ait sa propre couleur, pour que les
/// spectateurs reconnaissent chaque duelliste d'un coup d'œil, même après l'avoir perdu de vue.
/// Dans le prototype, c'est le jeu qui la donne : aucun client ne choisit la sienne, même principe
/// que pour le point de spawn. Le serveur prend dans `palette` la première couleur qu'aucun autre
/// joueur présent ne porte, et la publie.
///
/// Catégorie C réseautée : rien dans la simulation, le hitbox ou le son ne la lit (la bible exclut
/// expressément que la peinture touche au son). Le jour où la personnalisation arrivera, c'est cette
/// même variable qui portera le choix du joueur, validé par le serveur (couleurs vives seulement).
///
/// Seules les pièces listées dans `paintedRenderers` sont peintes : les articulations gardent leur
/// matériau, pour rester visibles comme le veut la bible. La couleur passe par un
/// MaterialPropertyBlock, sans dupliquer de matériau (donc rien à détruire au départ du joueur).
/// </summary>
public class PlayerPaint : NetworkBehaviour
{
    [Tooltip("Couleurs vives attribuées dans l'ordre : le premier arrivé prend la première libre. Les deux premières sont celles des deux duellistes d'une partie à deux, donc les plus faciles à distinguer (orange et bleu, que les daltoniens les plus courants séparent aussi). Pas de rouge : la bible ne veut pas que les joueurs partagent la couleur du danger (§ lisibilité), et les dégâts s'afficheront probablement en rouge. Doit compter au moins autant de couleurs que de places dans un lobby.")]
    [SerializeField] private Color32[] palette =
    {
        new Color32(255, 106, 19, 255),  // orange
        new Color32(30, 144, 255, 255),  // bleu
        new Color32(255, 45, 155, 255),  // magenta
        new Color32(46, 224, 90, 255),   // vert
        new Color32(0, 229, 229, 255),   // cyan
        new Color32(143, 75, 255, 255),  // violet
        new Color32(255, 210, 30, 255),  // jaune
    };

    [Tooltip("Les pièces du modèle qui reçoivent la peinture. Les autres (articulations) gardent leur matériau. À refaire si le modèle sous « Model » change.")]
    [SerializeField] private Renderer[] paintedRenderers;

    /// <summary>Alpha nul = pas encore de peinture : le modèle garde son matériau d'origine.
    /// Toutes les couleurs de la palette sont opaques, donc la valeur ne peut pas être confondue.</summary>
    private readonly NetworkVariable<Color32> networkPaint = new NetworkVariable<Color32>(
        new Color32(0, 0, 0, 0), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private static readonly List<PlayerPaint> spawnedPaints = new List<PlayerPaint>();
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private MaterialPropertyBlock propertyBlock;

    // Pièces peintes en plus de celles du modèle : les bras de la vue subjective (FirstPersonView).
    private readonly List<Renderer> extraRenderers = new List<Renderer>();

    public Color32 Paint => networkPaint.Value;
    public IReadOnlyList<Color32> Palette => palette;
    public IReadOnlyList<Renderer> PaintedRenderers => paintedRenderers;

    // Le rechargement de domaine est désactivé à l'entrée en Play Mode : sans cette remise à zéro,
    // une instance d'une session précédente pourrait encore « occuper » sa couleur.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => spawnedPaints.Clear();

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Les joueurs sont spawnés l'un après l'autre : chacun voit les couleurs déjà prises.
            var taken = new List<Color32>();
            foreach (PlayerPaint other in spawnedPaints)
            {
                if (other == null || other == this) continue;
                taken.Add(other.networkPaint.Value);
            }
            networkPaint.Value = PickPaint(palette, taken);
        }

        if (!spawnedPaints.Contains(this)) spawnedPaints.Add(this);
        networkPaint.OnValueChanged += HandlePaintChanged;
        ApplyPaint(networkPaint.Value);
    }

    public override void OnNetworkDespawn()
    {
        networkPaint.OnValueChanged -= HandlePaintChanged;
        spawnedPaints.Remove(this);
    }

    private void HandlePaintChanged(Color32 previous, Color32 current) => ApplyPaint(current);

    /// <summary>Peint aussi cette pièce, tout de suite et à chaque changement de couleur. Sert aux
    /// bras de la vue subjective, copiés du modèle après coup : un MaterialPropertyBlock ne se
    /// copie pas avec l'objet.</summary>
    public void AddRenderer(Renderer r)
    {
        if (r == null || extraRenderers.Contains(r)) return;
        extraRenderers.Add(r);
        ApplyPaint(networkPaint.Value);
    }

    /// <summary>Peint cette pièce de la couleur actuelle, une seule fois, sans la retenir : l'épave
    /// d'un robot tué (RobotCorpse) ne vit que le temps d'une mort, et la couleur ne change pas
    /// pendant un match.</summary>
    public void PaintOnce(Renderer r)
    {
        if (r == null || networkPaint.Value.a == 0) return;
        propertyBlock ??= new MaterialPropertyBlock();
        PaintRenderer(r, networkPaint.Value);
    }

    private void ApplyPaint(Color32 paint)
    {
        if (paint.a == 0 || paintedRenderers == null) return;

        propertyBlock ??= new MaterialPropertyBlock();
        foreach (Renderer r in paintedRenderers) PaintRenderer(r, paint);
        foreach (Renderer r in extraRenderers) PaintRenderer(r, paint);

#if UNITY_EDITOR
        Debug.Log($"[Peinture] Joueur {OwnerClientId} : #{ColorUtility.ToHtmlStringRGB(paint)}");
#endif
    }

    private void PaintRenderer(Renderer r, Color32 paint)
    {
        if (r == null) return;
        r.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, paint);
        r.SetPropertyBlock(propertyBlock);
    }

    /// <summary>
    /// La couleur d'un nouveau joueur : la première de la palette qu'aucun joueur présent ne porte.
    /// Si toutes sont prises (plus de joueurs que de couleurs), la moins portée, pour que les
    /// doublons restent aussi rares que possible. Fonction pure, testée.
    /// </summary>
    public static Color32 PickPaint(IReadOnlyList<Color32> palette, IReadOnlyList<Color32> taken)
    {
        if (palette == null || palette.Count == 0) return new Color32(0, 0, 0, 0);

        int best = 0;
        int bestCount = int.MaxValue;
        for (int i = 0; i < palette.Count; i++)
        {
            int count = 0;
            if (taken != null)
            {
                foreach (Color32 c in taken)
                {
                    if (Same(c, palette[i])) count++;
                }
            }
            if (count < bestCount)
            {
                best = i;
                bestCount = count;
                if (count == 0) break;
            }
        }
        return palette[best];
    }

    private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
}
