using UnityEngine;

/// <summary>Les châssis du robot (RobotChassis), dans l'ordre de leur rang : le choix d'un joueur voyage par ce
/// rang (la demande de connexion, une variable réseau) ; ajouter les nouveaux à la fin.</summary>
public enum ChassisId : byte
{
    /// <summary>Le CT-01, le mannequin d'essai blindé (bible § 5) : le châssis du jeu, par défaut.</summary>
    CT01 = 0,
    /// <summary>Le Y Bot de Mixamo, le robot des premiers essais, gardé au choix.</summary>
    Prototype = 1,
    /// <summary>L'OS-02, l'« Ossature » : un endosquelette d'acier (bible § 5).</summary>
    OS02 = 2,
}

/// <summary>
/// Les châssis du robot, au choix du joueur dans Personnalisation (2026-10-26, demandé par l'utilisateur :
/// « remplace le Y Bot par le CT-01, mais garde-le comme modèle choisissable », puis l'OS-02, une fois épaissi
/// pour montrer autant de sa surface touchable que le CT-01).
///
/// Tous habitent le même « Model » du préfab joueur, sur le même squelette (celui du Y Bot) : le même Animator,
/// les mêmes os, les mêmes armes en main et le même gant. Changer de châssis n'allume que d'autres pièces
/// (SkinnedMeshRenderer.enabled), ce qui laisse `forceRenderingOff` à ceux qui masquent un robot un temps (la mort,
/// l'écran de fin). Tout ce qui lit les os ou copie le modèle (vue subjective, épave, menus) sert ainsi à tous.
/// Catégorie C réseautée : la surface touchable ne dépend jamais du modèle (BodyLayout), le son non plus.
/// </summary>
public static class RobotChassis
{
    public const int Count = 3;

    // Les pièces de chaque châssis sous « Model », par leur nom, dans l'ordre de ChassisId. Celles du CT-01 et de
    // l'OS-02 sont posées par RobotChassisBuilder, celles du Y Bot viennent de son FBX.
    private static readonly string[][] Parts =
    {
        new[] { "CT01" },
        new[] { "Alpha_Surface", "Alpha_Joints" },
        new[] { "OS02" },
    };

    /// <summary>Le châssis d'un rang reçu (le choix d'un joueur, une variable réseau) : le CT-01 pour tout rang
    /// inconnu (rien de choisi, une ancienne build, un client modifié). Fonction pure, testée.</summary>
    public static ChassisId Sanitized(int value) => value >= 0 && value < Count ? (ChassisId)value : ChassisId.CT01;

    /// <summary>Le nom montré au joueur.</summary>
    public static string NameOf(ChassisId id) => id switch
    {
        ChassisId.Prototype => "PROTOTYPE",
        ChassisId.OS02 => "OS-02",
        _ => "CT-01",
    };

    /// <summary>Le rang du châssis dont `rendererName` est une pièce, -1 pour une pièce d'aucun châssis (une arme,
    /// le gant). Fonction pure, testée.</summary>
    public static int OwnerOf(string rendererName)
    {
        for (int i = 0; i < Parts.Length; i++)
        {
            foreach (string part in Parts[i])
            {
                if (part == rendererName) return i;
            }
        }
        return -1;
    }

    /// <summary>Montre le châssis `id` sous `model` (le « Model » d'un joueur, ou une copie) : ses pièces s'allument,
    /// celles des autres s'éteignent, le reste (armes, gant) n'est pas touché.</summary>
    public static void Apply(Transform model, ChassisId id)
    {
        if (model == null) return;
        foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            int owner = OwnerOf(r.name);
            if (owner >= 0) r.enabled = owner == (int)id;
        }
    }
}
