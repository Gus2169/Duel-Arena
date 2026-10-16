using UnityEngine;

/// <summary>
/// Un accessoire d'arme (2026-10-16, GDD § 6), posé sur la racine de son préfab : un viseur, le laser
/// tactique, une poignée ou un chargeur grande capacité, construit dans Blender et importé par Duel
/// Arena > Armes > Importer les armes de Blender. Son origine est le point d'accroche de l'arme (les
/// enfants « Accessoires/Viseur », « Laser », « Poignee », « Chargeur » de son préfab), dans les axes de
/// l'arme : il s'y pose sans décalage.
///
/// Ce qu'il porte : ses repères (la mire d'un viseur, d'où part le faisceau d'un laser, où tient la
/// main gauche sur une poignée), la vitre d'un viseur (le shader DuelArena/ViseurReticule y dessine
/// le réticule à l'infini) et ses finitions (WeaponFinishes : il prend celle de son arme). Ses effets
/// sont dans le catalogue (AttachmentCatalog). Catégorie C : rien de la simulation ne le lit.
/// </summary>
[DisallowMultipleComponent]
public class WeaponAttachment : MonoBehaviour
{
    [Tooltip("La désignation de l'accessoire (« PR-A », « LT-1 »...).")]
    [SerializeField] private string code = "";
    [Tooltip("Son nom, tel que l'armurerie l'affiche (« Point rouge A »).")]
    [SerializeField] private string title = "";
    [SerializeField] private AttachmentCategory category = AttachmentCategory.Sight;

    [Tooltip("Viseur : l'axe optique, au dos du viseur (repère « Mire ») ; la vue subjective y aligne l'œil.")]
    [SerializeField] private Transform sight;
    [Tooltip("Viseur : en visée, la distance de l'œil à sa mire (m). Ressenti, à régler dans le préfab.")]
    [SerializeField] private float aimEyeDistance = 0.08f;
    [Tooltip("Viseur : la vitre, où le réticule est dessiné à l'infini (DuelArena/ViseurReticule).")]
    [SerializeField] private Renderer lens;

    [Tooltip("Laser : l'émetteur, d'où part le faisceau (repère « Faisceau »).")]
    [SerializeField] private Transform beam;

    [Tooltip("Poignée : là où la paume gauche la tient (repère « MainGauche ») : la cible de l'IK de la main gauche.")]
    [SerializeField] private Transform leftHand;

    [Tooltip("Chargeur grande capacité : le modèle d'arme qui le prend (« FA-07 »).")]
    [SerializeField] private string weaponDesignation = "";

    [Tooltip("Les pièces de l'arme qu'il remplace, cachées tant qu'il est monté (AttachmentMount) : la hausse et le guidon sous un viseur, le chargeur d'origine sous un chargeur grande capacité.")]
    [SerializeField] private string[] hides = System.Array.Empty<string>();

    public string Code => code;
    public string Title => title;
    public AttachmentCategory Category => category;
    public Transform Sight => sight;
    public float AimEyeDistance => aimEyeDistance;
    public Renderer Lens => lens;
    public Transform Beam => beam;
    public Transform LeftHand => leftHand;
    public string WeaponDesignation => weaponDesignation;
    public System.Collections.Generic.IReadOnlyList<string> Hides => hides ?? System.Array.Empty<string>();

#if UNITY_EDITOR
    /// <summary>Réglage par l'importeur des accessoires (Editor seulement).</summary>
    public void EditorSetup(string designation, string name, AttachmentCategory kind, Transform sightPoint, float eyeDistance,
                            Renderer lensRenderer, Transform beamPoint, Transform leftHandPoint, string forWeapon, string[] hiddenParts)
    {
        hides = hiddenParts ?? System.Array.Empty<string>();
        code = designation;
        title = name;
        category = kind;
        sight = sightPoint;
        aimEyeDistance = eyeDistance;
        lens = lensRenderer;
        beam = beamPoint;
        leftHand = leftHandPoint;
        weaponDesignation = forWeapon ?? "";
    }
#endif
}
