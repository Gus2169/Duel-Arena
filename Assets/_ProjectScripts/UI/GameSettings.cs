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
    private const string ResolutionWidthKey = "DuelArena.ResolutionWidth";
    private const string ResolutionHeightKey = "DuelArena.ResolutionHeight";
    private const string VSyncKey = "DuelArena.VSync";
    private const string RenderStyleKey = "DuelArena.RenderStyle";
    private const string FrameRateKey = "DuelArena.FrameRateCap";
    private const string BindingsKey = "DuelArena.Bindings";
    private const string HostCapacityKey = "DuelArena.Host.Capacity";
    private const string HostOrganizationKey = "DuelArena.Host.Organization";
    private const string HostInterventionsKey = "DuelArena.Host.SpectatorInterventions";
    private const string HostRoundsKey = "DuelArena.Host.RoundsToWin";
    private const string HostRandomDuelistsKey = "DuelArena.Host.RandomDuelists";
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
    /// duels (roi de la colline ou duellistes fixes), interventions des spectateurs, depuis le
    /// 2026-10-25 le nombre de manches (BO5 ou BO3), et depuis le 2026-10-27 le tirage au sort des
    /// duellistes. Choisis au salon, retenus d'une session à l'autre, appliqués par le serveur seul
    /// (SessionFlow.ActiveRules, RoundManager).
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
                randomDuelists = PlayerPrefs.GetInt(HostRandomDuelistsKey, defaults.randomDuelists ? 1 : 0) == 1,
            }.Sanitized();
        }
        set
        {
            MatchRules r = value.Sanitized();
            PlayerPrefs.SetInt(HostCapacityKey, r.capacity);
            PlayerPrefs.SetInt(HostOrganizationKey, (int)r.organization);
            PlayerPrefs.SetInt(HostInterventionsKey, r.spectatorInterventions ? 1 : 0);
            PlayerPrefs.SetInt(HostRoundsKey, r.roundsToWin);
            PlayerPrefs.SetInt(HostRandomDuelistsKey, r.randomDuelists ? 1 : 0);
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

    /// <summary>La clé de l'apparence des armes dans PlayerPrefs (publique pour les tests) : douze
    /// finitions en hexadécimal, depuis la refonte des armes du 2026-10-26.</summary>
    public const string WeaponLooksKey = "DuelArena.WeaponLooks12";

    /// <summary>Celle d'avant la refonte (un entier : le modèle et la finition de quatre classes), relue
    /// une fois pour garder les choix du joueur.</summary>
    public const string WeaponLooksKeyBeforeOverhaul = "DuelArena.WeaponLooks";

    /// <summary>Les places dans l'arsenal des trois armes de chaque classe d'avant la refonte, dans
    /// l'ordre des modèles qu'elle proposait (le modèle par défaut, puis les deux autres) : ce que
    /// devient un réglage enregistré avant le 2026-10-26.</summary>
    public static readonly int[][] ArmsBeforeOverhaul =
    {
        new[] { 0, 4, 5 },   // fusil d'assaut : FA-07, FA-30, FX-1
        new[] { 1, 6, 7 },   // sniper : FP-12, FP-30, FX-2
        new[] { 2, 8, 9 },   // pompe : FAP-03, FAP-30, FX-3
        new[] { 3, 10, 11 }, // pistolet : FX-4 (son modèle par défaut), PA-05, RV-30
    };

    /// <summary>
    /// La finition choisie à l'armurerie pour chaque arme (2026-10-15, GDD § 6), annoncée à l'hôte en
    /// se connectant (SessionFlow), qui la borne avant de la publier (PlayerWeaponLooks). Toutes les
    /// finitions sont au choix pendant le prototype (GDD § 14).
    ///
    /// Lue une fois par lancement, puis gardée en mémoire, comme le pseudo : deux instances du jeu sur
    /// un même PC partagent la clé du registre, et chacune doit garder ses armes.
    /// </summary>
    public static WeaponLooks WeaponLooks
    {
        get
        {
            if (!weaponLooks.HasValue)
            {
                string saved = PlayerPrefs.GetString(WeaponLooksKey, "");
                if (ulong.TryParse(saved, System.Globalization.NumberStyles.AllowHexSpecifier,
                                   System.Globalization.CultureInfo.InvariantCulture, out ulong packed))
                    weaponLooks = new WeaponLooks(packed);
                else if (PlayerPrefs.HasKey(WeaponLooksKeyBeforeOverhaul))
                    weaponLooks = WeaponLooks.FromBeforeOverhaul(unchecked((uint)PlayerPrefs.GetInt(WeaponLooksKeyBeforeOverhaul, 0)), ArmsBeforeOverhaul);
                else
                    weaponLooks = WeaponLooks.Default;
            }
            return weaponLooks.Value;
        }
        set
        {
            weaponLooks = value;
            PlayerPrefs.SetString(WeaponLooksKey, value.Packed.ToString("X12"));
            PlayerPrefs.Save();
            WeaponLooksChanged?.Invoke();
        }
    }

    /// <summary>Levé quand l'armurerie change un modèle ou une finition (l'aperçu du robot suit).</summary>
    public static event System.Action WeaponLooksChanged;

    // L'apparence des armes de CETTE instance, lue une fois (vide = pas encore lue).
    private static WeaponLooks? weaponLooks;

    /// <summary>La clé des accessoires des armes dans PlayerPrefs (publique pour les tests) : ceux des
    /// douze armes en hexadécimal, depuis la refonte des armes du 2026-10-26.</summary>
    public const string WeaponAttachmentsKey = "DuelArena.WeaponAttachments12";

    /// <summary>Celle d'avant la refonte (un entier : les accessoires de quatre classes), relue une fois :
    /// ceux d'une classe passent à ses trois armes.</summary>
    public const string WeaponAttachmentsKeyBeforeOverhaul = "DuelArena.WeaponAttachments";

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
            {
                if (PlayerPrefs.HasKey(WeaponAttachmentsKey)
                    && WeaponAttachments.TryParseHex(PlayerPrefs.GetString(WeaponAttachmentsKey, ""), out WeaponAttachments saved))
                    weaponAttachments = saved;
                else if (PlayerPrefs.HasKey(WeaponAttachmentsKeyBeforeOverhaul))
                    weaponAttachments = WeaponAttachments.FromBeforeOverhaul(
                        unchecked((uint)PlayerPrefs.GetInt(WeaponAttachmentsKeyBeforeOverhaul, 0)), ArmsBeforeOverhaul);
                else
                    weaponAttachments = WeaponAttachments.None;
            }
            return weaponAttachments.Value;
        }
        set
        {
            weaponAttachments = value;
            PlayerPrefs.SetString(WeaponAttachmentsKey, value.ToHex());
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

    /// <summary>Le rendu du jeu (2026-10-27, RenderStyle) : low poly propre, cel-shading ou les deux mêlés, au choix du
    /// joueur le temps de la phase de test ; les deux mêlés par défaut, le choix de l'utilisateur pour les essais. Gardé
    /// en mémoire comme l'inversion de l'axe, appliqué aussitôt.</summary>
    public static RenderStyleMode PreferredRenderStyle
    {
        get
        {
            renderStyle ??= (RenderStyleMode)Mathf.Clamp(PlayerPrefs.GetInt(RenderStyleKey, (int)RenderStyleMode.Meles), 0, 2);
            return renderStyle.Value;
        }
        set
        {
            renderStyle = value;
            PlayerPrefs.SetInt(RenderStyleKey, (int)value);
            PlayerPrefs.Save();
            RenderStyle.Set(value);
        }
    }

    // Le rendu de CETTE instance du jeu, lu une fois (vide = pas encore lu).
    private static RenderStyleMode? renderStyle;

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

    /// <summary>La résolution choisie (largeur, hauteur) ; (0, 0) = pas de préférence enregistrée (on garde
    /// la résolution courante). Depuis le 2026-10-27, la taille elle-même : un rang dans Screen.resolutions,
    /// où chaque taille revient une fois par fréquence d'écran, ne désignait plus la même d'un écran à
    /// l'autre. L'ancien rang est relu une fois.</summary>
    public static Vector2Int Resolution
    {
        get
        {
            int width = PlayerPrefs.GetInt(ResolutionWidthKey, 0);
            int height = PlayerPrefs.GetInt(ResolutionHeightKey, 0);
            if (width > 0 && height > 0) return new Vector2Int(width, height);
            int index = PlayerPrefs.GetInt(ResolutionIndexKey, -1);
            Resolution[] all = Screen.resolutions;
            return index >= 0 && index < all.Length ? new Vector2Int(all[index].width, all[index].height) : Vector2Int.zero;
        }
        set
        {
            PlayerPrefs.SetInt(ResolutionWidthKey, Mathf.Max(0, value.x));
            PlayerPrefs.SetInt(ResolutionHeightKey, Mathf.Max(0, value.y));
            PlayerPrefs.DeleteKey(ResolutionIndexKey);
            PlayerPrefs.Save();
        }
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
        Vector2Int size = Resolution;
        if (size.x > 0 && size.y > 0) Screen.SetResolution(size.x, size.y, mode);
        else Screen.fullScreenMode = mode;
    }

    /// <summary>Les remappages enregistrés, tels quels (vide sans remappage) : les Options y comparent ceux
    /// qu'on vient de faire, pour savoir s'il reste quelque chose à appliquer.</summary>
    public static string SavedBindingOverridesJson => PlayerPrefs.GetString(BindingsKey, string.Empty);

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
