using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Réglages du joueur, persistés en PlayerPrefs : sensibilité souris, graphismes, remappage des
/// touches, et ce que le joueur choisit dans Personnalisation (le châssis et la couleur de son robot,
/// l'apparence et les accessoires de ses armes).
///
/// Catégorie C, au même titre que le lean ou le head bob : rien ici n'est lu par la simulation ni le
/// hitbox. La sensibilité ne touche qu'à la lecture de la souris (PlayerCameraLook). Les choix de
/// Personnalisation partent dans la demande de connexion (SessionFlow), et c'est le serveur qui les
/// borne et les applique : la couleur, il la donne si aucun autre joueur ne la porte (PlayerPaint).
/// </summary>
public static class GameSettings
{
    private const string SensitivityKey = "DuelArena.Sensitivity";
    private const string InvertYKey = "DuelArena.InvertY";
    /// <summary>La clé de la couleur choisie dans PlayerPrefs (publique pour les tests).</summary>
    public const string PreferredColorKey = "DuelArena.PreferredColorIndex";
    private const string FullscreenKey = "DuelArena.Fullscreen";
    private const string ResolutionIndexKey = "DuelArena.ResolutionIndex";
    private const string VSyncKey = "DuelArena.VSync";
    private const string FrameRateKey = "DuelArena.FrameRateCap";
    private const string BindingsKey = "DuelArena.Bindings";
    private const string HostCapacityKey = "DuelArena.Host.Capacity";
    private const string HostOrganizationKey = "DuelArena.Host.Organization";
    private const string HostInterventionsKey = "DuelArena.Host.SpectatorInterventions";
    private const string HostRoundsKey = "DuelArena.Host.RoundsToWin";
    private const string MasterVolumeKey = "DuelArena.Volume.Master";
    private const string MusicVolumeKey = "DuelArena.Volume.Music";

    /// <summary>Volume général (0 à 1), appliqué à l'écoute (AudioListener.volume) : tous les sons
    /// du jeu (2026-10-13).</summary>
    public static float MasterVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, 1f));
        set { PlayerPrefs.SetFloat(MasterVolumeKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); ApplyAudio(); }
    }

    /// <summary>Volume de la musique (0 à 1), par-dessus le volume général. Lu par MusicPlayer.</summary>
    public static float MusicVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, 0.7f));
        set { PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); ApplyAudio(); }
    }

    /// <summary>Levé quand un volume change (Options), pour que la musique en cours suive.</summary>
    public static event System.Action AudioChanged;

    /// <summary>Applique le volume général. Appelé au lancement et à chaque réglage.</summary>
    public static void ApplyAudio()
    {
        AudioListener.volume = MasterVolume;
        AudioChanged?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyAudioAtLaunch() => ApplyAudio();

    /// <summary>
    /// Les réglages de la partie que cette machine hébergera (2026-10-13) : capacité, organisation des
    /// duels (roi de la colline ou duellistes fixes), interventions des spectateurs, et depuis le
    /// 2026-10-25 le nombre de manches (BO5 ou BO3). Choisis au salon, retenus d'une session à l'autre,
    /// appliqués par le serveur seul (SessionFlow.ActiveRules, RoundManager).
    /// </summary>
    public static MatchRules HostRules
    {
        get
        {
            MatchRules defaults = MatchRules.Default;
            return new MatchRules
            {
                capacity = (byte)Mathf.Clamp(PlayerPrefs.GetInt(HostCapacityKey, defaults.capacity), 0, 255),
                organization = (DuelOrganization)Mathf.Clamp(PlayerPrefs.GetInt(HostOrganizationKey, (int)defaults.organization), 0, 255),
                spectatorInterventions = PlayerPrefs.GetInt(HostInterventionsKey, defaults.spectatorInterventions ? 1 : 0) == 1,
                roundsToWin = (byte)Mathf.Clamp(PlayerPrefs.GetInt(HostRoundsKey, defaults.roundsToWin), 0, 255),
            }.Sanitized();
        }
        set
        {
            MatchRules r = value.Sanitized();
            PlayerPrefs.SetInt(HostCapacityKey, r.capacity);
            PlayerPrefs.SetInt(HostOrganizationKey, (int)r.organization);
            PlayerPrefs.SetInt(HostInterventionsKey, r.spectatorInterventions ? 1 : 0);
            PlayerPrefs.SetInt(HostRoundsKey, r.roundsToWin);
            PlayerPrefs.Save();
        }
    }
    /// <summary>La clé du pseudo dans PlayerPrefs (publique pour les tests).</summary>
    public const string PlayerNameKey = "DuelArena.PlayerName";

    /// <summary>Le pseudo choisi sur l'écran Jouer (2026-10-12), annoncé à l'hôte en se connectant
    /// (SessionFlow). Vide = le serveur nommera le joueur « Joueur N ». Nettoyé à l'enregistrement
    /// (PlayerMatchState.SanitizeName), et de nouveau par le serveur, qui ne fait pas confiance.
    ///
    /// Lu UNE fois par lancement, puis gardé en mémoire : sous Windows, PlayerPrefs relit le registre
    /// à chaque appel, et deux instances du jeu sur un même PC (deux builds, ou l'Editor et son
    /// joueur virtuel) y partagent la même clé. Le dernier pseudo tapé valait alors pour les deux, et
    /// la partie affichait « Untel » et « Untel 2 » (corrigé le 2026-10-12, retour de l'utilisateur).
    /// Le registre ne sert plus qu'à retrouver son pseudo au lancement suivant.</summary>
    public static string PlayerName
    {
        get
        {
            if (playerName == null) playerName = PlayerMatchState.SanitizeName(PlayerPrefs.GetString(PlayerNameKey, string.Empty));
            return playerName;
        }
        set
        {
            playerName = PlayerMatchState.SanitizeName(value);
            PlayerPrefs.SetString(PlayerNameKey, playerName);
            PlayerPrefs.Save();
        }
    }

    // Le pseudo de CETTE instance du jeu. Survit volontairement aux sessions de Play Mode (rechargement
    // de domaine désactivé) : c'est celui que montre son champ.
    private static string playerName;

    /// <summary>La clé de l'apparence des armes dans PlayerPrefs (publique pour les tests).</summary>
    public const string WeaponLooksKey = "DuelArena.WeaponLooks";

    /// <summary>
    /// Le modèle et la finition choisis à l'armurerie pour chaque arme (2026-10-15, GDD § 6), annoncés
    /// à l'hôte en se connectant (SessionFlow), qui les borne avant de les publier (PlayerWeaponLooks).
    /// Toutes les finitions sont au choix pendant le prototype (GDD § 14).
    ///
    /// Lue une fois par lancement, puis gardée en mémoire, comme le pseudo : deux instances du jeu sur
    /// un même PC partagent la clé du registre, et chacune doit garder ses armes.
    /// </summary>
    public static WeaponLooks WeaponLooks
    {
        get
        {
            if (!weaponLooks.HasValue) weaponLooks = new WeaponLooks(unchecked((uint)PlayerPrefs.GetInt(WeaponLooksKey, 0)));
            return weaponLooks.Value;
        }
        set
        {
            weaponLooks = value;
            PlayerPrefs.SetInt(WeaponLooksKey, unchecked((int)value.Packed));
            PlayerPrefs.Save();
            WeaponLooksChanged?.Invoke();
        }
    }

    /// <summary>Levé quand l'armurerie change un modèle ou une finition (l'aperçu du robot suit).</summary>
    public static event System.Action WeaponLooksChanged;

    // L'apparence des armes de CETTE instance, lue une fois (vide = pas encore lue).
    private static WeaponLooks? weaponLooks;

    /// <summary>La clé des accessoires des armes dans PlayerPrefs (publique pour les tests).</summary>
    public const string WeaponAttachmentsKey = "DuelArena.WeaponAttachments";

    /// <summary>
    /// Les accessoires choisis à l'armurerie pour chaque arme (2026-10-16, GDD § 6) : viseur, poignée,
    /// laser, chargeur. Annoncés à l'hôte en se connectant (SessionFlow), qui les borne avant de les
    /// publier (PlayerWeaponAttachments). Ils changent l'arme ; tous ouverts à tous.
    ///
    /// Lus une fois par lancement, puis gardés en mémoire, comme le pseudo : deux instances du jeu sur
    /// un même PC partagent la clé du registre, et chacune doit garder ses armes.
    /// </summary>
    public static WeaponAttachments WeaponAttachments
    {
        get
        {
            if (!weaponAttachments.HasValue)
                weaponAttachments = new WeaponAttachments(unchecked((uint)PlayerPrefs.GetInt(WeaponAttachmentsKey, 0)));
            return weaponAttachments.Value;
        }
        set
        {
            weaponAttachments = value;
            PlayerPrefs.SetInt(WeaponAttachmentsKey, unchecked((int)value.Packed));
            PlayerPrefs.Save();
            WeaponAttachmentsChanged?.Invoke();
        }
    }

    /// <summary>Levé quand l'armurerie change un accessoire (l'aperçu du robot suit).</summary>
    public static event System.Action WeaponAttachmentsChanged;

    // Les accessoires des armes de CETTE instance, lus une fois (vide = pas encore lus).
    private static WeaponAttachments? weaponAttachments;

    /// <summary>Levé quand les touches changent (Options) : le joueur en partie recharge ses
    /// remappages tout de suite (PlayerInputReader), sans attendre la manche suivante.</summary>
    public static event System.Action BindingsChanged;

    public const float MinSensitivity = 0.2f;
    public const float MaxSensitivity = 3f;
    public const float DefaultSensitivity = 1f;

    /// <summary>Multiplicateur appliqué par-dessus la sensibilité de base réglée dans l'inspecteur
    /// (PlayerCameraLook.mouseSensitivity) : 1 = cette base, inchangée.
    ///
    /// Lu à CHAQUE image, à quatre endroits (la caméra, le lacet du corps, la vue du mort, le banc de
    /// test) : il est gardé en mémoire depuis le 2026-10-13. PlayerPrefs relisait le registre de
    /// Windows à chaque appel, sur le chemin même des inputs.</summary>
    public static float SensitivityMultiplier
    {
        get
        {
            if (float.IsNaN(sensitivity)) sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, DefaultSensitivity), MinSensitivity, MaxSensitivity);
            return sensitivity;
        }
        set
        {
            sensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            PlayerPrefs.SetFloat(SensitivityKey, sensitivity);
            PlayerPrefs.Save();
        }
    }

    // La sensibilité de CETTE instance du jeu, lue une fois (NaN = pas encore lue).
    private static float sensitivity = float.NaN;

    /// <summary>L'axe vertical de la souris inversé (2026-10-17, la maquette des Options) : pousser la souris
    /// fait baisser le regard. Lu à chaque image par la caméra (PlayerCameraLook), donc gardé en mémoire
    /// comme la sensibilité.</summary>
    public static bool InvertY
    {
        get
        {
            invertY ??= PlayerPrefs.GetInt(InvertYKey, 0) == 1;
            return invertY.Value;
        }
        set
        {
            invertY = value;
            PlayerPrefs.SetInt(InvertYKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // L'inversion de CETTE instance du jeu, lue une fois (vide = pas encore lue).
    private static bool? invertY;

    /// <summary>
    /// La couleur choisie pour le robot dans Personnalisation : son rang dans la palette de PlayerPaint,
    /// -1 = aucun choix (le serveur donne alors la première couleur libre). Annoncée à l'hôte en se
    /// connectant (SessionFlow) ; le serveur la donne si aucun autre joueur ne la porte (2026-10-16,
    /// demandé par l'utilisateur).
    ///
    /// Lue une fois par lancement, puis gardée en mémoire, comme le pseudo : deux instances du jeu sur
    /// un même PC partagent la clé du registre, et chacune doit garder sa couleur.
    /// </summary>
    public static int PreferredColorIndex
    {
        get
        {
            if (!preferredColor.HasValue) preferredColor = PlayerPrefs.GetInt(PreferredColorKey, -1);
            return preferredColor.Value;
        }
        set
        {
            preferredColor = value;
            PlayerPrefs.SetInt(PreferredColorKey, value);
            PlayerPrefs.Save();
        }
    }

    // La couleur choisie par CETTE instance, lue une fois (vide = pas encore lue).
    private static int? preferredColor;

    /// <summary>La clé du châssis choisi dans PlayerPrefs (publique pour les tests).</summary>
    public const string PreferredChassisKey = "DuelArena.PreferredChassis";

    /// <summary>
    /// Le châssis choisi pour le robot dans Personnalisation (RobotChassis : le CT-01 par défaut). Annoncé à
    /// l'hôte en se connectant (SessionFlow), borné et publié par le serveur (PlayerChassis) ; 2026-10-26.
    /// Lu une fois par lancement, puis gardé en mémoire, comme la couleur.
    /// </summary>
    public static ChassisId PreferredChassis
    {
        get
        {
            if (!preferredChassis.HasValue) preferredChassis = RobotChassis.Sanitized(PlayerPrefs.GetInt(PreferredChassisKey, 0));
            return preferredChassis.Value;
        }
        set
        {
            preferredChassis = value;
            PlayerPrefs.SetInt(PreferredChassisKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    // Le châssis choisi par CETTE instance, lu une fois (vide = pas encore lu).
    private static ChassisId? preferredChassis;

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
        BindingsChanged?.Invoke();
    }

    public static void ResetBindingOverrides(InputActionAsset asset)
    {
        asset.RemoveAllBindingOverrides();
        PlayerPrefs.DeleteKey(BindingsKey);
        PlayerPrefs.Save();
        BindingsChanged?.Invoke();
    }

    /// <summary>Le choix d'une rubrique de l'armurerie (AttachmentCategory) pour une place de la
    /// panoplie : 0 = aucun accessoire (la mire de l'arme, le chargeur d'origine), sinon son rang dans
    /// la rubrique. Lu et écrit dans WeaponAttachments.</summary>
    public static int GetAttachment(int weaponSlot, int category)
        => WeaponAttachments.Variant(weaponSlot, (AttachmentCategory)category);

    public static void SetAttachment(int weaponSlot, int category, int variant)
        => WeaponAttachments = WeaponAttachments.WithVariant(weaponSlot, (AttachmentCategory)category, variant);
}
