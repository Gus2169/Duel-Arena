using System.Collections.Generic;
using UnityEngine;

/// <summary>L'arsenal rangé comme le joueur le voit (2026-10-26) : par famille (fusil d'assaut,
/// précision, pompe, pistolet), puis le stock, la série 30 et le prototype laser. L'ordre de l'arsenal
/// lui-même (les places) ne change jamais : il voyage sur le réseau. Fonctions pures, testées.</summary>
public static class Arsenal
{
    public const int FamilyCount = 4;

    /// <summary>Le nom d'une famille, tel que l'armurerie et la préparation l'affichent.</summary>
    public static string FamilyLabel(WeaponFamily family) => family switch
    {
        WeaponFamily.AssaultRifle => "Fusil d'assaut",
        WeaponFamily.Precision => "Fusil de précision",
        WeaponFamily.Shotgun => "Fusil à pompe",
        WeaponFamily.Pistol => "Pistolet",
        _ => family.ToString(),
    };

    /// <summary>Les places des armes d'une famille, du stock au prototype laser.</summary>
    public static List<int> SlotsOf(IReadOnlyList<WeaponData> loadout, WeaponFamily family)
    {
        var slots = new List<int>();
        if (loadout == null) return slots;
        for (int slot = 0; slot < loadout.Count; slot++)
            if (loadout[slot] != null && loadout[slot].family == family) slots.Add(slot);
        slots.Sort((a, b) => loadout[a].Tier != loadout[b].Tier ? loadout[a].Tier.CompareTo(loadout[b].Tier) : a.CompareTo(b));
        return slots;
    }

    /// <summary>Toutes les places, famille par famille.</summary>
    public static List<int> DisplayOrder(IReadOnlyList<WeaponData> loadout)
    {
        var order = new List<int>();
        for (int family = 0; family < FamilyCount; family++) order.AddRange(SlotsOf(loadout, (WeaponFamily)family));
        return order;
    }
}

/// <summary>La famille d'une arme (GDD § 6, 2026-10-26) : ses trois armes (le stock, la série 30, le
/// prototype laser) ont les mêmes points de prise, donc les mêmes gestes, et un son reconnaissable ; le
/// mode Pile ou face en impose une. 🚨 Sérialisée par sa valeur entière : ajouter les nouvelles à la fin.</summary>
public enum WeaponFamily : byte
{
    AssaultRifle = 0,
    Precision = 1,
    Shotgun = 2,
    Pistol = 3,
}

/// <summary>
/// Données d'une arme. Tout est data-driven : ajouter une arme = créer un nouvel asset et l'ajouter
/// à l'arsenal de WeaponController, jamais toucher au code.
///
/// Créer un asset : clic droit dans le Project > Create > Duel Arena > Weapon Data.
///
/// Douze armes depuis la refonte du 2026-10-26 (GDD § 6) : un asset par arme, chacune avec son modèle,
/// ses chiffres, son tir et son son. Les références sont Call of Duty et Rainbow Six. Le FA-07 est calé
/// sur un TTK ≈ 0,7 s : 6 impacts au buste à 17 dégâts (le 6e tue toujours), à 7 tirs/seconde. Toutes
/// les valeurs sont provisoires, à corriger par l'utilisateur dans l'éditeur des armes.
///
/// 🚨 Tout ce que lit le serveur (dégâts, cadence, chargeur, chaleur, gerbe, poids) doit être identique
/// sur toutes les machines : ces assets ne se modifient jamais en jeu.
/// </summary>
[CreateAssetMenu(fileName = "NewWeapon", menuName = "Duel Arena/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Identité")]
    public string weaponName = "Arme sans nom";

    [Tooltip("La famille de l'arme (GDD § 6) : ses gestes, son son reconnaissable, le mode Pile ou face, le rangement de l'armurerie.")]
    public WeaponFamily family = WeaponFamily.AssaultRifle;

    [Tooltip("Son rôle en quelques mots (GDD § 6, choisi par l'utilisateur le 2026-10-26), montré là où l'on choisit son arme.")]
    public string role = "";

    [Tooltip("Le modèle tenu en main droite (préfab : poignée à l'origine, canon vers +Z, repères « Mire » et « Bouche »). Le même sert au corps vu par les autres et aux bras de la vue subjective. Branché par l'importeur des armes de Blender, d'après la désignation du modèle (le FA-07 pour l'arme FA-07).")]
    public GameObject modelPrefab;

    [Tooltip("Ce que cette arme prend à l'armurerie (GDD § 6) : le serveur fait tomber le reste (WeaponAttachments.Sanitized). Le sniper garde sa lunette, un pistolet n'a pas de poignée avant (propositions de Claude, 2026-10-16).")]
    public AttachmentSlots acceptedAttachments = AttachmentSlots.All;

    [Tooltip("Les viseurs qu'elle prend, quand elle prend un viseur : une glissière de pistolet n'a la place que d'un réflex (proposition de Claude, 2026-10-16).")]
    public SightFamilies acceptedSights = SightFamilies.All;

    [Tooltip("Le catalogue des accessoires (Accessoires.asset), le même pour toutes les armes : leurs préfabs et la valeur de leurs effets. Branché par l'importeur des armes de Blender.")]
    public AttachmentCatalog attachmentCatalog;

    /// <summary>La désignation du modèle (« FA-07 »), ou le nom de l'arme sans modèle.</summary>
    public string Designation => modelPrefab != null && modelPrefab.TryGetComponent(out WeaponModel model) ? model.Designation : weaponName;

    /// <summary>Le rang de l'arme dans sa famille : le stock, la série 30, le prototype laser (lu sur son
    /// modèle). Range l'armurerie et le panneau de préparation.</summary>
    public WeaponModel.Family Tier => modelPrefab != null && modelPrefab.TryGetComponent(out WeaponModel model) ? model.ModelFamily : WeaponModel.Family.Stock;

    [Header("Dégâts & cadence")]
    [Tooltip("Dégâts d'une balle (ou d'un plomb) au BUSTE, à courte distance. Les autres zones en sont des multiples.")]
    public float damagePerHit = 17f;
    public float shotsPerSecond = 7f;
    public bool isAutomatic = true; // maintenir Fire pour tirer en rafale, sinon un appui = un coup
    public float maxRange = 60f;

    [Header("Zones de tir (2026-10-11, façon Call of Duty)")]
    [Tooltip("Multiplicateur des dégâts à la tête. Le sniper tue d'une balle au buste et à la tête ; le pompe ne compte pas la tête (×1).")]
    public float headMultiplier = 1.4f;
    [Tooltip("Multiplicateur des dégâts aux jambes. Le sniper ne tue pas d'une balle dans les jambes.")]
    public float legsMultiplier = 1f;

    [Header("Portée des dégâts (GDD § 6 : constants par défaut, à définir arme par arme)")]
    [Tooltip("Jusqu'à cette distance (m), dégâts pleins. Au-delà de falloffEnd, minDamageMultiplier. Entre les deux, en ligne droite. falloffEnd ≤ falloffStart : aucune baisse.")]
    public float falloffStart = 0f;
    public float falloffEnd = 0f;
    [Range(0f, 1f)] public float minDamageMultiplier = 1f;

    [Header("Gerbe (armes à plombs, GDD § 3 : motif fixe, jamais aléatoire)")]
    [Tooltip("Écart de chaque plomb par rapport à l'axe du regard, en degrés (x à droite, y en haut). Vide = une seule balle, dans l'axe. Le motif est le même à chaque tir : large et lisible au jugé, jamais aléatoire.")]
    public Vector2[] pelletPattern = new Vector2[0];
    [Tooltip("Resserrement de la gerbe en visée (1 = même gerbe).")]
    [Range(0.1f, 1f)] public float aimPatternScale = 1f;

    [Header("Tir à la hanche (façon Rainbow Six, 2026-10-16 : HipFireSpread)")]
    [Tooltip("Demi-angle (degrés) du cône où part la balle à la hanche, debout à l'arrêt. Épaulée, elle part dans l'axe. 0 pour une arme à plombs : sa gerbe est sa dispersion.")]
    [Min(0f)] public float hipSpread = 0f;
    [Tooltip("Le même cône à l'allure de marche (5 m/s). Il continue de s'ouvrir au-delà, jusqu'au double de l'écart à 10 m/s (l'élan d'un sprint).")]
    [Min(0f)] public float hipSpreadMoving = 0f;
    [Tooltip("Ce qu'ouvre chaque balle d'une rafale (degrés), jusqu'à hipSpreadMaxBloom.")]
    [Min(0f)] public float hipSpreadPerShot = 0f;
    [Min(0f)] public float hipSpreadMaxBloom = 0f;
    [Tooltip("Vitesse (degrés/s) à laquelle la rafale se referme sans tirer.")]
    [Min(0f)] public float hipSpreadRecovery = 4f;
    [Tooltip("Comment la précision arrive pendant qu'on épaule (Rainbow Six, Y9S1) : à la fin pour un fusil d'assaut, en ligne droite pour un fusil de précision, tôt pour un pistolet ou un pompe.")]
    public HipFireSpread.AimCurve aimCurve = HipFireSpread.AimCurve.Slow;

    [Header("Coup mortel puissant (2026-10-16)")]
    [Tooltip("Vitesse (m/s) dont le coup mortel projette l'épave dans le sens du tir, quand tout le tir a touché (le pompe à bout portant). Moins de plombs au but, moins fort (40 % au moins). 0 : la simple poussée selon les dégâts (WeaponController.DeathImpulse).")]
    [Min(0f)] public float killKnockback = 0f;
    [Tooltip("Ce qui la soulève (m/s vers le haut), dans la même proportion.")]
    [Min(0f)] public float killKnockbackLift = 0f;

    [Header("En main (2026-10-11)")]
    [Tooltip("Temps (s) pour sortir cette arme quand on la prend (préparation, arme lancée par un spectateur). On ne tire pas avant. Changer d'arme annule un rechargement, comme dans Call of Duty.")]
    public float raiseTime = 0.6f;
    [Tooltip("Vitesse de déplacement avec cette arme en main, relative à la vitesse du joueur (« le poids de l'arme », comme dans CoD et R6). Lu par Move() : le serveur fait foi.")]
    [Range(0.5f, 1.1f)] public float moveSpeedMultiplier = 1f;
    [Tooltip("Temps (s) pour épauler : passer de la hanche à la visée. La lunette du sniper n'apparaît qu'une fois l'arme épaulée (environ 0,35 s dans CoD).")]
    public float aimTime = 0.125f;
    [Tooltip("Lunette plein écran façon Call of Duty : grossissement de la vue en visée. 1 = pas de lunette (viseur ou mire de l'arme).")]
    [Min(1f)] public float scopeMagnification = 1f;
    [Tooltip("Le réticule du HUD s'affiche-t-il à la hanche ? Non pour le sniper, comme dans CoD : il faut deviner le centre de son cône (hipSpread).")]
    public bool hipCrosshair = true;
    [Tooltip("Le tir se voit comme un rayon, de la couleur tracerColor (le raygun) ; sinon, une arme à balles, par la flamme de bouche et sans traçant (2026-10-16, demandé par l'utilisateur).")]
    public bool shotIsBeam = false;
    [Tooltip("Couleur du rayon (le rayon du raygun se voit de loin).")]
    public Color tracerColor = new Color(1f, 0.85f, 0.4f, 1f);

    [Tooltip("Layers qui ARRÊTENT une balle : la géométrie du monde. NE DOIT PAS contenir le layer des joueurs ni celui des hitbox — le tir est résolu en deux traces distinctes (voir WeaponController.FireServerRpc), et mélanger les deux ferait bloquer les balles par le collider de MOUVEMENT des joueurs, qui ne suit pas le lean.")]
    public LayerMask worldMask = 1; // Default

    [Tooltip("Layers des surfaces TOUCHABLES des joueurs — en pratique le seul layer \"Hitbox\". C'est la seule chose que le serveur accepte comme cible de dégâts.")]
    public LayerMask hitboxMask = 1 << 7; // Hitbox

    [Header("Chargeur (GDD § 6 : chargeur limité, rechargement)")]
    [Tooltip("Balles par chargeur. Tenu par le serveur, qui refuse tout tir à vide. 255 au plus (il voyage sur un octet).")]
    [Range(1, 255)] public int magazineSize = 30;
    [Tooltip("Durée (s) d'un rechargement complet — ou d'UNE cartouche si reloadPerRound est coché. On ne tire pas pendant ce temps ; le taser, la course, le franchissement et un changement d'arme l'interrompent. Réglage de ressenti ET d'équilibrage : c'est la fenêtre où l'adversaire peut pousser.")]
    public float reloadDuration = 2.2f;
    [Tooltip("Rechargement cartouche par cartouche (fusil à pompe, comme dans CoD et R6) : une cartouche toutes les reloadDuration secondes, jusqu'au plein. Tirer l'interrompt et tire avec ce qui est chargé.")]
    public bool reloadPerRound = false;
    [Tooltip("Les étapes sonores d'un rechargement complet (chargeur retiré, inséré, armement), chacune à sa part de reloadDuration. Entendues par tout le monde : c'est une information de jeu (l'adversaire sait qu'on ne peut plus tirer). Un rechargement interrompu se tait (WeaponAudio). Vide = silencieux.")]
    public ReloadCue[] reloadCues = new ReloadCue[0];
    [Tooltip("Rechargement cartouche par cartouche : le bruit d'une cartouche glissée, joué à chaque cartouche entrée (une au hasard).")]
    public AudioClip[] reloadRoundSounds = new AudioClip[0];
    [Tooltip("Joué quand un rechargement va à son terme, reloadEndDelay après la dernière cartouche (la pompe qu'on arme). Vide = rien.")]
    public AudioClip[] reloadEndSounds = new AudioClip[0];
    public float reloadEndDelay = 0.3f;
    [Tooltip("Volume des cartouches glissées et de la fin du rechargement (les étapes ont chacune le leur).")]
    [Range(0f, 1f)] public float reloadVolume = 0.8f;

    [Header("Chaleur des armes laser (2026-10-26, GDD § 6)")]
    [Tooltip("Chaleur qu'ajoute un tir (1 = la surchauffe). 0 : une arme à chargeur. Une arme laser n'a pas de chargeur : elle chauffe en tirant et refroidit seule ; surchauffée, elle ne tire plus et se vide d'elle-même (le geste du rechargement, overheatVentDuration). La touche de rechargement la vide à tout moment, en reloadDuration. Elle ne change ni les dégâts ni la précision.")]
    [Range(0f, 1f)] public float heatPerShot = 0f;
    [Tooltip("Délai (s) sans tirer avant que l'arme commence à refroidir.")]
    [Min(0f)] public float heatCoolDelay = 0.3f;
    [Tooltip("Temps (s) pour refroidir de la surchauffe à froid, sans tirer : des rafales courtes tirent bien plus longtemps qu'un tir continu.")]
    [Min(0.01f)] public float heatCoolTime = 2.5f;
    [Tooltip("Durée (s) de la vidange forcée par la surchauffe, plus longue qu'une vidange voulue (reloadDuration) : la surchauffe se paie.")]
    [Min(0.01f)] public float overheatVentDuration = 2.5f;

    /// <summary>Une arme laser, qui chauffe au lieu de compter des balles.</summary>
    public bool UsesHeat => heatPerShot > 0f;

    [Header("Tir chargé (le FX-2, 2026-10-26, GDD § 6)")]
    [Tooltip("Temps (s) pour charger un tir à fond, la gâchette tenue : on la lâche pour tirer. 0 : une arme qui tire à l'appui. La charge se compte sur les inputs, chez le tireur comme sur le serveur (PlayerLocomotion.TrackCharge), ne monte que quand l'arme pourrait tirer, se garde tant qu'on tient la gâchette, et chaque tir la consomme.")]
    [Min(0f)] public float chargeTime = 0f;
    [Tooltip("Ce que vaut un tir lâché sans charge, en part des dégâts d'un tir chargé à fond (damagePerHit) ; la charge les mène en ligne droite jusqu'aux dégâts pleins. Un tir chauffe l'arme autant, chargé ou non.")]
    [Range(0f, 1f)] public float unchargedDamage = 0.3f;
    [Tooltip("La montée en charge, jouée pendant que l'arme se charge et accordée sur chargeTime (elle finit quand la charge est pleine), coupée quand on tire ou qu'on lâche. Entendue de tous : l'adversaire sait qu'un coup arrive (GDD § 6).")]
    public AudioClip chargeSound;
    [Range(0f, 1f)] public float chargeVolume = 0.7f;

    /// <summary>Une arme qui se charge gâchette tenue et tire quand on la lâche.</summary>
    public bool UsesCharge => chargeTime > 0f;

    /// <summary>La charge (0 à 1) d'un tir lâché après `chargedSeconds` secondes de charge ; 1 pour une
    /// arme qui ne se charge pas. Fonction pure, testée.</summary>
    public float ChargeFraction(float chargedSeconds) => UsesCharge ? Mathf.Clamp01(chargedSeconds / chargeTime) : 1f;

    /// <summary>Ce que la charge (0 à 1) laisse des dégâts : unchargedDamage sans charge, tout chargé à
    /// fond (exactement : un tir plein au buste du FX-2 tue). Fonction pure, testée.</summary>
    public float ChargeDamageScale(float charge)
    {
        if (!UsesCharge || charge >= 1f) return 1f;
        return Mathf.Lerp(unchargedDamage, 1f, charge);
    }

    [Header("Sortie de sprint (2026-10-11)")]
    [Tooltip("Temps (s) entre la fin d'un sprint et le premier tir ou le début d'un rechargement, le temps de remonter l'arme. Comme dans Call of Duty : se mettre à courir engage, l'adversaire qui tient l'angle tire le premier. Ressenti ET équilibrage, propre à chaque arme. Le serveur le vérifie.")]
    public float sprintToFireTime = 0.3f;

    [Header("Recul (déterministe, pas de hasard : RecoilPattern)")]
    [Tooltip("Le motif de recul, coup par coup (2026-10-26) : la poussée que chaque coup d'une rafale donne au regard (x à droite, y en haut), en multiples de recoilPerShotDegrees. Le premier élément est la poussée du premier coup, qui déplace le deuxième. Au-delà du dernier, le dernier se répète. Se dessine dans l'éditeur des armes (Duel Arena > Armes > Éditeur des armes).")]
    public Vector2[] recoilPattern = DefaultRecoilPattern();
    [Tooltip("La force du recul : de combien de degrés pousse un coup du motif qui vaut 1, debout en mouvement à la hanche. La posture, le déplacement et la visée la réduisent (WeaponController), la poignée verticale aussi.")]
    public float recoilPerShotDegrees = 1.2f;
    [Tooltip("La vitesse (degrés par seconde) à laquelle le regard revient vers l'axe, sur chaque axe, pendant et après une rafale.")]
    public float recoilRecoverySpeed = 6f;
    [Tooltip("Le temps sans tirer (s) après lequel le motif repart du premier coup. Une arme qui tire moins vite reprend son motif au premier coup à chaque tir.")]
    public float recoilResetDelay = 0.35f;

    [Header("Audio")]
    [Tooltip("Son(s) de tir propres à cette arme. Un clip est tiré au hasard à chaque coup (variation contre l'effet 'disque rayé'). Vit ici plutôt que dans un bank global : chaque arme a un son différent, WeaponController n'a rien à connaître de l'arme équipée. Pour une arme automatique, des coups SECS : la réverbération vient de fireTailSound.")]
    public AudioClip[] fireSounds;
    [Range(0f, 1f)] public float fireVolume = 0.9f;
    public Vector2 firePitchRange = new Vector2(0.97f, 1.03f);
    [Tooltip("Premier coup d'une rafale, à la place d'un fireSounds (le guide des armes automatiques du pack Gamemaster : la réverbération ne s'entend pas encore). Vide = un fireSounds comme les autres.")]
    public AudioClip fireFirstSound;
    [Tooltip("Queue de réverbération lancée avec CHAQUE coup et éteinte en fondu quand le suivant part (WeaponAudio) : une rafale garde sa netteté, et la queue ne s'entend qu'à la fin. Vide = les coups portent leur propre queue.")]
    public AudioClip fireTailSound;
    [Tooltip("Mécanique qui suit un coup : la culasse du sniper, la pompe du fusil à pompe. Une au hasard, fireMechanismDelay après le coup. Vide = rien.")]
    public AudioClip[] fireMechanismSounds = new AudioClip[0];
    public float fireMechanismDelay = 0.4f;
    [Range(0f, 1f)] public float fireMechanismVolume = 0.8f;

    [Header("Audio : où arrive le tir (2026-10-13)")]
    [Tooltip("Une balle qui frappe le décor, entendue là où elle arrive : on sait où tombent les tirs.")]
    public SoundSet impactSounds = new SoundSet();
    [Tooltip("Une balle qui frappe un robot, entendue là où elle arrive.")]
    public SoundSet bodyImpactSounds = new SoundSet();
    [Tooltip("Une balle qui passe près de soi sans toucher (vide pour un rayon, qui ne siffle pas).")]
    public SoundSet flybySounds = new SoundSet();
    [Tooltip("La gâchette pressée chargeur vide (le tireur seul).")]
    public SoundSet dryFireSounds = new SoundSet();
    [Tooltip("L'arme qu'on sort : changement d'arme, arme ramassée.")]
    public SoundSet raiseSounds = new SoundSet();

    /// <summary>Nombre de projectiles d'un tir : les plombs du motif, ou une balle.</summary>
    public int PelletCount => pelletPattern != null && pelletPattern.Length > 0 ? pelletPattern.Length : 1;

    /// <summary>Écart (degrés) du projectile `index`, gerbe resserrée en visée, ou de `hipScale` à la
    /// hanche (le laser tactique). Une balle seule part dans l'axe.</summary>
    public Vector2 PelletOffset(int index, bool aiming, float hipScale = 1f)
    {
        if (pelletPattern == null || pelletPattern.Length == 0) return Vector2.zero;
        return pelletPattern[index] * (aiming ? aimPatternScale : hipScale);
    }

    /// <summary>
    /// Dégâts d'un impact, selon la zone touchée, la distance et la charge du tir (0 à 1 ; une arme qui
    /// ne se charge pas l'ignore). Fonction pure, testée : c'est le serveur qui l'appelle, et elle ne
    /// dépend que de l'asset.
    /// </summary>
    public float DamageAt(HitboxZoneType zone, float distance, float charge = 1f)
    {
        float zoneMultiplier = zone switch
        {
            HitboxZoneType.Head => headMultiplier,
            HitboxZoneType.Legs => legsMultiplier,
            _ => 1f,
        };
        return damagePerHit * zoneMultiplier * RangeMultiplier(distance) * ChargeDamageScale(charge);
    }

    /// <summary>Multiplicateur de portée : 1 jusqu'à falloffStart, minDamageMultiplier au-delà de
    /// falloffEnd, en ligne droite entre les deux.</summary>
    public float RangeMultiplier(float distance)
    {
        if (falloffEnd <= falloffStart) return 1f;
        float t = Mathf.InverseLerp(falloffStart, falloffEnd, distance);
        return Mathf.Lerp(1f, minDamageMultiplier, t);
    }

    /// <summary>
    /// Le motif par défaut d'une nouvelle arme : les premiers coups grimpent presque à la verticale
    /// (comme un vrai canon qui monte sous la pression des gaz), puis la poussée verticale plafonne
    /// pendant que l'horizontale part en zigzag, le « S » des FPS compétitifs. On dompte l'arme après
    /// quelques balles au lieu de subir un recul au hasard. C'étaient les courbes d'avant le
    /// 2026-10-26, coup par coup.
    /// </summary>
    public static Vector2[] DefaultRecoilPattern() => new[]
    {
        new Vector2(0f, 0.6f),
        new Vector2(0f, 0.85f),
        new Vector2(0.15f, 1.05f),
        new Vector2(-0.25f, 1.2f),
        new Vector2(0.4f, 1.3f),
        new Vector2(-0.5f, 1.31875f),
        new Vector2(0.55f, 1.3f),
        new Vector2(-0.5f, 1.261111f),
        new Vector2(0.45f, 1.2055556f),
        new Vector2(-0.4f, 1.15f),
        new Vector2(0.35f, 1.1f),
        new Vector2(0.159375f, 1.05f),
        new Vector2(-0.3f, 1f),
    };
}

/// <summary>Une étape sonore d'un rechargement complet : un clip (au hasard parmi plusieurs), à sa
/// part de la durée du rechargement. Calée sur cette durée, l'étape suit l'animation quand on règle
/// reloadDuration.</summary>
[System.Serializable]
public struct ReloadCue
{
    public AudioClip[] clips;
    [Tooltip("Instant de l'étape, en part de reloadDuration (0 au début, 1 à la fin).")]
    [Range(0f, 1f)] public float at;
    [Range(0f, 1f)] public float volume;
}
