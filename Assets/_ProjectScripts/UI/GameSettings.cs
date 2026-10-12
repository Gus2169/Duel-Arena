using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Réglages du joueur, persistés en PlayerPrefs : sensibilité souris, graphismes, remappage des
/// touches, et les deux préférences de l'armurerie (couleur souhaitée, arme de départ).
///
/// Catégorie C stricte, au même titre que le lean ou le head bob : rien ici n'est lu par la
/// simulation, le hitbox ou le serveur. La sensibilité ne touche qu'à la lecture de la souris
/// (PlayerCameraLook) ; l'arme de départ ne fait que déclencher, côté PROPRIÉTAIRE et une seule
/// fois au spawn, le même changement d'arme authentifié que les touches 1 à 4
/// (WeaponController.HandleSwitchInput) — le serveur valide exactement comme pour un appui clavier,
/// aucune confiance nouvelle n'est demandée. La couleur souhaitée n'est PAS encore branchée au
/// serveur (PlayerPaint choisit seul, voir CLAUDE.md « Personnalisation ») : elle n'est stockée
/// que pour le jour où elle le sera.
/// </summary>
public static class GameSettings
{
    private const string SensitivityKey = "DuelArena.Sensitivity";
    private const string PreferredWeaponKey = "DuelArena.PreferredWeapon";
    private const string PreferredColorKey = "DuelArena.PreferredColorIndex";
    private const string FullscreenKey = "DuelArena.Fullscreen";
    private const string ResolutionIndexKey = "DuelArena.ResolutionIndex";
    private const string VSyncKey = "DuelArena.VSync";
    private const string FrameRateKey = "DuelArena.FrameRateCap";
    private const string BindingsKey = "DuelArena.Bindings";
    private const string AttachmentKeyPrefix = "DuelArena.Attachment.";

    public const float MinSensitivity = 0.2f;
    public const float MaxSensitivity = 3f;
    public const float DefaultSensitivity = 1f;

    /// <summary>Multiplicateur appliqué par-dessus la sensibilité de base réglée dans l'inspecteur
    /// (PlayerCameraLook.mouseSensitivity) : 1 = cette base, inchangée.</summary>
    public static float SensitivityMultiplier
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, DefaultSensitivity), MinSensitivity, MaxSensitivity);
        set { PlayerPrefs.SetFloat(SensitivityKey, Mathf.Clamp(value, MinSensitivity, MaxSensitivity)); PlayerPrefs.Save(); }
    }

    /// <summary>Place de la panoplie choisie à l'armurerie (0 à 3). -1 = aucune préférence : on
    /// garde la première arme, comme avant que l'armurerie existe.</summary>
    public static int PreferredWeaponSlot
    {
        get => PlayerPrefs.GetInt(PreferredWeaponKey, -1);
        set { PlayerPrefs.SetInt(PreferredWeaponKey, value); PlayerPrefs.Save(); }
    }

    /// <summary>Index souhaité dans la palette de PlayerPaint, -1 = aucune préférence.</summary>
    public static int PreferredColorIndex
    {
        get => PlayerPrefs.GetInt(PreferredColorKey, -1);
        set { PlayerPrefs.SetInt(PreferredColorKey, value); PlayerPrefs.Save(); }
    }

    public static bool Fullscreen
    {
        get => PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;
        set { PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    /// <summary>Index dans Screen.resolutions ; -1 = pas de préférence enregistrée (on garde la
    /// résolution courante).</summary>
    public static int ResolutionIndex
    {
        get => PlayerPrefs.GetInt(ResolutionIndexKey, -1);
        set { PlayerPrefs.SetInt(ResolutionIndexKey, value); PlayerPrefs.Save(); }
    }

    public static bool VSync
    {
        get => PlayerPrefs.GetInt(VSyncKey, 1) == 1;
        set { PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    /// <summary>Plafond d'images/seconde ; 0 ou moins = illimité.</summary>
    public static int FrameRateCap
    {
        get => PlayerPrefs.GetInt(FrameRateKey, 0);
        set { PlayerPrefs.SetInt(FrameRateKey, value); PlayerPrefs.Save(); }
    }

    /// <summary>Applique résolution/plein écran/vsync/plafond sauvegardés. Appelé au lancement de
    /// CHAQUE scène (menu et arène) : un lancement direct sur la scène de jeu en test doit aussi
    /// repartir des réglages choisis, pas des valeurs par défaut du Player.</summary>
    public static void ApplyGraphics()
    {
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Application.targetFrameRate = FrameRateCap > 0 ? FrameRateCap : -1;

        FullScreenMode mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        int index = ResolutionIndex;
        Resolution[] resolutions = Screen.resolutions;
        if (index >= 0 && index < resolutions.Length)
        {
            Resolution r = resolutions[index];
            Screen.SetResolution(r.width, r.height, mode, r.refreshRateRatio);
        }
        else
        {
            Screen.fullScreenMode = mode;
        }
    }

    /// <summary>Remappages enregistrés, appliqués à un asset d'actions donné. Nécessaire car
    /// chaque `new PlayerControls()` déserialise sa PROPRE copie de l'asset généré : sans cet
    /// appel, un remappage fait au menu n'existerait que sur l'instance temporaire du menu.</summary>
    public static void ApplyBindingOverrides(InputActionAsset asset)
    {
        string json = PlayerPrefs.GetString(BindingsKey, string.Empty);
        if (!string.IsNullOrEmpty(json)) asset.LoadBindingOverridesFromJson(json);
    }

    public static void SaveBindingOverrides(InputActionAsset asset)
    {
        PlayerPrefs.SetString(BindingsKey, asset.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
    }

    public static void ResetBindingOverrides(InputActionAsset asset)
    {
        asset.RemoveAllBindingOverrides();
        PlayerPrefs.DeleteKey(BindingsKey);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Accessoires cosmétiques choisis à l'armurerie avancée (poignée, viseur, laser, chargeur
    /// étendu), par place de la panoplie (0 à 3) et par catégorie (voir
    /// WeaponCustomizationController.AttachmentCategory). 0 = aucun accessoire, sinon l'index de
    /// variante (1, 2...).
    ///
    /// PUREMENT COSMÉTIQUE, et seulement dans l'armurerie du menu pour l'instant : rien ici n'est
    /// lu par WeaponController, HeldWeapons ou FirstPersonView. Appliquer ces choix à l'arme
    /// réellement tenue en jeu (vue du propriétaire ET des autres joueurs) est un chantier à part,
    /// qui demanderait de réseauter le choix — délibérément hors scope de ce premier jet, au même
    /// titre que la couleur souhaitée (voir PreferredColorIndex).
    /// </summary>
    public static int GetAttachment(int weaponSlot, int category)
    {
        return PlayerPrefs.GetInt(AttachmentKeyPrefix + weaponSlot + "." + category, 0);
    }

    public static void SetAttachment(int weaponSlot, int category, int variant)
    {
        PlayerPrefs.SetInt(AttachmentKeyPrefix + weaponSlot + "." + category, variant);
        PlayerPrefs.Save();
    }
}
