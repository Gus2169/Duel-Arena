using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 🚨 Ne sert plus depuis le 2026-10-16 : l'armurerie monte les vrais accessoires (AttachmentMount).
/// Gardé jusqu'à l'accord de l'utilisateur pour le mettre à la Corbeille (dette n° 74).
///
/// Géométrie PLACEHOLDER des accessoires de l'armurerie avancée — des primitives composées, pas
/// des modèles 3D : aucun art final n'existe pour ça, et c'est un aperçu de menu, pas la véritable
/// arme tenue en jeu (voir WeaponCustomizationController). Même esprit que les autres placeholders
/// assumés du projet (CrosshairUI, WeaponVisualFeedback) : à remplacer, pas un bug.
///
/// Chaque variante est un petit sous-arbre de primitives Unity, enfant du POINT D'ACCROCHE de sa
/// rubrique dans le préfab de l'arme et construit dans son repère : une poignée et un chargeur
/// pendent le long de -Y, un viseur se pose sur +Y, un laser tire le long de +Z. La rotation et
/// l'échelle du point d'accroche orientent et dimensionnent l'accessoire, arme par arme.
/// </summary>
public static class PreviewAttachmentFactory
{
    /// <summary>Le faisceau du laser, que le cadrage de l'armurerie ignore (un mètre de long).</summary>
    public const string BeamName = "Faisceau";

    public static GameObject Build(AttachmentCategory category, int variant, Transform parent)
    {
        return category switch
        {
            AttachmentCategory.Grip => BuildGrip(variant, parent),
            AttachmentCategory.Sight => BuildSight(variant, parent),
            AttachmentCategory.Laser => BuildLaser(parent),
            AttachmentCategory.Magazine => BuildMagazine(parent),
            _ => NewRoot("Accessoire", parent),
        };
    }

    private static GameObject BuildGrip(int variant, Transform parent)
    {
        GameObject root = NewRoot("PoigneeAvant", parent);
        float height = variant >= 2 ? 0.09f : 0.05f;
        GameObject box = Primitive(PrimitiveType.Cube, root.transform, DarkPlastic);
        box.transform.localScale = new Vector3(0.025f, height, 0.03f);
        box.transform.localPosition = new Vector3(0f, -height * 0.5f, 0f);
        return root;
    }

    private static GameObject BuildSight(int variant, Transform parent)
    {
        GameObject root = NewRoot("Viseur", parent);

        GameObject rail = Primitive(PrimitiveType.Cube, root.transform, DarkPlastic);
        rail.transform.localScale = new Vector3(0.03f, 0.008f, 0.05f);

        if (variant >= 2) // holographique : boîtier large + fenêtre teintée
        {
            GameObject body = Primitive(PrimitiveType.Cube, root.transform, DarkPlastic);
            body.transform.localScale = new Vector3(0.035f, 0.035f, 0.06f);
            body.transform.localPosition = new Vector3(0f, 0.022f, 0f);

            GameObject window = Primitive(PrimitiveType.Cube, root.transform, new Color(0.25f, 0.65f, 0.95f, 0.65f));
            window.transform.localScale = new Vector3(0.028f, 0.022f, 0.004f);
            window.transform.localPosition = new Vector3(0f, 0.022f, 0.031f);
        }
        else // point rouge : petit boîtier + lentille
        {
            GameObject body = Primitive(PrimitiveType.Cube, root.transform, DarkPlastic);
            body.transform.localScale = new Vector3(0.022f, 0.028f, 0.038f);
            body.transform.localPosition = new Vector3(0f, 0.018f, 0f);

            GameObject lens = Primitive(PrimitiveType.Cylinder, root.transform, new Color(0.85f, 0.15f, 0.15f));
            lens.transform.localScale = new Vector3(0.015f, 0.01f, 0.015f);
            lens.transform.localPosition = new Vector3(0f, 0.018f, 0.019f);
            lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        return root;
    }

    private static GameObject BuildLaser(Transform parent)
    {
        GameObject root = NewRoot("Laser", parent);

        GameObject body = Primitive(PrimitiveType.Cylinder, root.transform, new Color(0.08f, 0.08f, 0.08f));
        body.transform.localScale = new Vector3(0.012f, 0.025f, 0.012f);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // Le faisceau : un fin cylindre rouge projeté vers l'avant, posé en continu (pas une
        // vraie simulation de tir, purement décoratif) — pour vendre l'idée du laser d'un coup
        // d'œil dans la vitrine. Cylindre Unity par défaut : 2 unités de haut (-1 à 1) avant mise
        // à l'échelle ; demi-échelle 0,5 => longueur 1 m, décalée de sa moitié pour partir du
        // boîtier plutôt que d'être centrée dessus.
        const float halfLength = 0.5f;
        GameObject beam = Primitive(PrimitiveType.Cylinder, root.transform, new Color(1f, 0.15f, 0.15f));
        beam.name = BeamName;
        beam.transform.localScale = new Vector3(0.0035f, halfLength, 0.0035f);
        beam.transform.localPosition = new Vector3(0f, 0f, halfLength);
        beam.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        return root;
    }

    private static GameObject BuildMagazine(Transform parent)
    {
        GameObject root = NewRoot("ChargeurEtendu", parent);
        const float length = 0.16f;
        GameObject box = Primitive(PrimitiveType.Cube, root.transform, DarkPlastic);
        box.transform.localScale = new Vector3(0.03f, length, 0.05f);
        box.transform.localPosition = new Vector3(0f, -length * 0.5f, 0f);
        return root;
    }

    private static readonly Color DarkPlastic = new Color(0.22f, 0.23f, 0.25f);

    private static GameObject NewRoot(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static GameObject Primitive(PrimitiveType type, Transform parent, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);

        // Un CreatePrimitive livre toujours un collider actif ; purement cosmétique ici, et sans
        // physique dans le menu, mais on le désactive quand même tout de suite — même réflexe que
        // pour les marqueurs d'impact (voir CLAUDE.md, piège du 2026-09-24).
        Collider col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        go.transform.SetParent(parent, false);

        go.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
        return go;
    }

    // Un matériau par couleur, partagé par toutes les armes : sans ce cache, chaque changement
    // d'arme en créait une quinzaine que rien ne détruisait.
    private static readonly Dictionary<Color, Material> Materials = new();

    private static Material MaterialFor(Color color)
    {
        if (Materials.TryGetValue(color, out Material cached) && cached != null) return cached;
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
        Materials[color] = material;
        return material;
    }
}
