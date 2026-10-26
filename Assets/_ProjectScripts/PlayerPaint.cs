using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// La peinture du robot : une couleur vive par joueur, donnée par le SERVEUR (2026-10-08).
///
/// Le GDD (§ 13) et la bible veulent que chaque robot ait sa propre couleur, pour que les
/// spectateurs reconnaissent chaque duelliste d'un coup d'œil, même après l'avoir perdu de vue.
/// Depuis le 2026-10-16 (demandé par l'utilisateur), le joueur la choisit dans Personnalisation, parmi
/// les couleurs vives de `palette`, et l'annonce en se connectant (SessionFlow) : le serveur la lui
/// donne si aucun autre joueur présent ne la porte, sinon la première couleur libre, et la publie.
/// Deux robots n'ont donc jamais la même tant que la palette a assez de couleurs.
///
/// Catégorie C réseautée : rien dans la simulation, le hitbox ou le son ne la lit (la bible exclut
/// expressément que la peinture touche au son).
///
/// Seules les pièces listées dans `paintedRenderers` sont peintes : les articulations gardent leur
/// matériau, pour rester visibles comme le veut la bible. La couleur passe par un
/// MaterialPropertyBlock, sans dupliquer de matériau (donc rien à détruire au départ du joueur). Sur une
/// pièce à plusieurs matériaux (le CT-01, 2026-10-26), seuls ses emplacements de peinture la prennent, et
/// ses lueurs la rendent en émission (PaintPart). La liste porte les pièces de tous les châssis
/// (RobotChassis) : peindre une pièce éteinte ne coûte rien.
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
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    /// <summary>Le nom que porte un matériau de peinture, sur une pièce à plusieurs matériaux (CT01_Peinture).</summary>
    public const string PaintSlot = "Peinture";

    /// <summary>Le nom que porte un matériau de lueur (les yeux, les fentes : CT01_Lueur), que la couleur du
    /// joueur allume, et que l'écran de la mort éteint (RobotFaceScreen).</summary>
    public const string GlowSlot = "Lueur";

    /// <summary>L'éclat des lueurs : la couleur du joueur, multipliée (le jeu n'a pas de bloom ; la valeur des
    /// matériaux de l'essai des rendus, RobotChassisBuilder).</summary>
    public const float GlowIntensity = 2.6f;

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
            networkPaint.Value = PickPaint(palette, taken, SessionFlow.TakeApprovedPaint(OwnerClientId));
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
    /// pendant un match. Ses lueurs restent comme elles sont : celles d'une épave sont éteintes
    /// (RobotFaceScreen.ShowDeath).</summary>
    public void PaintOnce(Renderer r)
    {
        if (r == null || networkPaint.Value.a == 0) return;
        propertyBlock ??= new MaterialPropertyBlock();
        PaintPart(r, networkPaint.Value, propertyBlock, false);
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

    private void PaintRenderer(Renderer r, Color32 paint) => PaintPart(r, paint, propertyBlock, true);

    /// <summary>
    /// Peint une pièce du robot de la couleur `paint`. Sur une pièce à plusieurs matériaux (le CT-01), les seuls
    /// emplacements de peinture (un matériau dont le nom contient PaintSlot) et, si `glows`, les lueurs (GlowSlot),
    /// qui prennent la couleur en émission ; sur une pièce sans emplacement nommé (le corps du Y Bot), toute la
    /// pièce. Le reste garde son matériau. Partagée par le jeu et les copies des menus (RobotAppearance). Testée.
    /// </summary>
    public static void PaintPart(Renderer r, Color32 paint, MaterialPropertyBlock block, bool glows)
    {
        if (r == null) return;
        Material[] materials = r.sharedMaterials;
        bool slots = false;
        for (int i = 0; i < materials.Length; i++)
        {
            string name = materials[i] != null ? materials[i].name : "";
            bool painted = name.Contains(PaintSlot);
            bool glow = !painted && name.Contains(GlowSlot);
            if (!painted && !glow) continue;
            slots = true;
            if (glow && !glows) continue;
            r.GetPropertyBlock(block, i);
            if (painted) block.SetColor(BaseColorId, paint);
            else block.SetColor(EmissionColorId, (Color)paint * GlowIntensity);
            r.SetPropertyBlock(block, i);
        }
        if (slots) return;
        r.GetPropertyBlock(block);
        block.SetColor(BaseColorId, paint);
        r.SetPropertyBlock(block);
    }

    /// <summary>
    /// La couleur d'un nouveau joueur : celle qu'il a choisie (`preferred`, son rang dans la palette ;
    /// -1 : aucune) si aucun joueur présent ne la porte, sinon la première de la palette qu'aucun ne
    /// porte. Si toutes sont prises (plus de joueurs que de couleurs), la moins portée, pour que les
    /// doublons restent aussi rares que possible. Un rang hors de la palette ne vaut rien : il ne vient
    /// que d'un client modifié. Fonction pure, testée.
    /// </summary>
    public static Color32 PickPaint(IReadOnlyList<Color32> palette, IReadOnlyList<Color32> taken, int preferred = -1)
    {
        if (palette == null || palette.Count == 0) return new Color32(0, 0, 0, 0);

        if (preferred >= 0 && preferred < palette.Count && Worn(palette[preferred], taken) == 0) return palette[preferred];

        int best = 0;
        int bestCount = int.MaxValue;
        for (int i = 0; i < palette.Count; i++)
        {
            int count = Worn(palette[i], taken);
            if (count < bestCount)
            {
                best = i;
                bestCount = count;
                if (count == 0) break;
            }
        }
        return palette[best];
    }

    // Combien de joueurs portent déjà cette couleur.
    private static int Worn(Color32 color, IReadOnlyList<Color32> taken)
    {
        int count = 0;
        if (taken == null) return 0;
        foreach (Color32 c in taken)
        {
            if (Same(c, color)) count++;
        }
        return count;
    }

    private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
}
