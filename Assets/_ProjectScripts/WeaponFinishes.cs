using System;
using UnityEngine;

/// <summary>
/// Les six finitions d'un modèle d'arme (GDD § 6, 2026-10-15), posé sur la racine de son préfab à
/// côté de WeaponModel : pour chaque pièce, les matériaux de chaque finition (shader
/// DuelArena/ArmeUsee), que l'importeur des armes de Blender (Duel Arena > Armes) a créés à partir des
/// paramètres de leurs matériaux procéduraux.
///
/// Une finition ne change que des matériaux : « dépareillée » donne à certaines pièces (garde-main,
/// capot, crosse, chargeur, carcasse basse) la finition d'une autre arme, toutes les autres ne font
/// qu'ajouter leurs marques. Les matériaux sont des assets partagés par toutes les copies du modèle :
/// rien à créer ni à détruire en jeu.
///
/// Catégorie C : rien de la simulation ne le lit.
/// </summary>
[DisallowMultipleComponent]
public class WeaponFinishes : MonoBehaviour
{
    [Serializable]
    public struct Piece
    {
        public Renderer renderer;
        [Tooltip("Les matériaux de la pièce dans chaque finition, à la suite : ceux de la première (d'origine), puis de la deuxième, etc. Autant par finition que la pièce a de sous-maillages.")]
        public Material[] materials;
    }

    [SerializeField] private Piece[] pieces = Array.Empty<Piece>();

    [Tooltip("La finition des matériaux actuellement posés (celle du préfab : d'origine).")]
    [SerializeField] private WeaponFinish shown = WeaponFinish.Origine;

    private Material[] buffer = Array.Empty<Material>();

    public WeaponFinish Shown => shown;
    public int PieceCount => pieces.Length;

    /// <summary>Pose les matériaux de `finish` sur toutes les pièces. Une finition inconnue redevient
    /// celle d'origine.</summary>
    public void Apply(WeaponFinish finish)
    {
        if ((int)finish >= WeaponLooks.FinishCount) finish = WeaponFinish.Origine;
        foreach (Piece piece in pieces)
        {
            if (piece.renderer == null || piece.materials == null) continue;
            int count = piece.materials.Length / WeaponLooks.FinishCount;
            if (count == 0) continue;
            if (buffer.Length != count) buffer = new Material[count];
            Array.Copy(piece.materials, (int)finish * count, buffer, 0, count);
            piece.renderer.sharedMaterials = buffer;
        }
        shown = finish;
    }

    /// <summary>Les matériaux d'une pièce dans une finition (tests, armurerie).</summary>
    public Material[] MaterialsOf(int piece, WeaponFinish finish)
    {
        Piece p = pieces[piece];
        int count = p.materials.Length / WeaponLooks.FinishCount;
        var result = new Material[count];
        Array.Copy(p.materials, (int)finish * count, result, 0, count);
        return result;
    }

    public Renderer RendererOf(int piece) => pieces[piece].renderer;

#if UNITY_EDITOR
    /// <summary>Réglage par l'importeur des armes (Editor seulement).</summary>
    public void EditorSetup(Piece[] value)
    {
        pieces = value;
        shown = WeaponFinish.Origine;
    }
#endif
}
