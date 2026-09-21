using UnityEngine;

/// <summary>
/// Données d'une arme (Phase 3 de la feuille de route, en avance ici pour tester le TTK
/// dès la Phase 0). Tout est data-driven : ajouter une arme = créer un nouvel asset,
/// jamais toucher au code de WeaponController.
///
/// Créer un asset : clic droit dans le Project > Create > Duel Arena > Weapon Data.
///
/// Valeurs de départ calées pour un TTK ≈ 0.7s (confirmé) : 6 impacts à 17 dégâts
/// (17 x 6 = 102, donc le 6e tir tue toujours), à une cadence de 7 tirs/seconde
/// (~420 coups/minute) : TTK = 5 intervalles x (1/7)s ≈ 0.71s. Ajuste librement dans
/// l'inspecteur — c'est fait pour ça.
/// </summary>
[CreateAssetMenu(fileName = "NewWeapon", menuName = "Duel Arena/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Identité")]
    public string weaponName = "Arme sans nom";

    [Header("Dégâts & cadence")]
    public float damagePerHit = 17f;
    public float shotsPerSecond = 7f;
    public bool isAutomatic = true; // maintenir Fire pour tirer en rafale, sinon un appui = un coup
    public float maxRange = 60f;
    public LayerMask hittableMask = ~0;

    [Header("Recul (déterministe, pas de RNG)")]
    [Tooltip("Multiplicateur du kick vertical à chaque tir. L'axe X de la courbe = numéro du tir dans la rafale (0, 1, 2...). Monte vite sur les premiers tirs puis plafonne, comme une vraie arme.")]
    public AnimationCurve verticalRecoilPattern = BuildDefaultVerticalPattern();
    [Tooltip("Même principe pour le kick horizontal, en zigzag (valeurs alternées + / -) une fois que le vertical a bien grimpé — le classique pattern en 'S' des FPS compétitifs.")]
    public AnimationCurve horizontalRecoilPattern = BuildDefaultHorizontalPattern();
    public float recoilPerShotDegrees = 1.2f;
    public float recoilRecoverySpeed = 6f; // degrés/seconde de récupération
    public float recoilResetDelay = 0.35f; // pause sans tirer avant que le pattern reparte de 0

    [Header("Audio")]
    [Tooltip("Son(s) de tir propres à cette arme. Un clip est tiré au hasard à chaque coup (variation contre l'effet 'disque rayé'). Vit ici plutôt que dans un bank global : chaque arme a un son différent, WeaponController n'a rien à connaître de l'arme équipée.")]
    public AudioClip[] fireSounds;
    [Range(0f, 1f)] public float fireVolume = 0.9f;
    public Vector2 firePitchRange = new Vector2(0.97f, 1.03f);

    // Pattern par défaut : les 4 premiers tirs grimpent presque à la verticale (comme un vrai
    // canon qui monte sous la pression des gaz), puis le kick vertical plafonne pendant que
    // l'horizontal part en zigzag — c'est ce qui donne l'impression de devoir "dompter" l'arme
    // après quelques balles plutôt que de subir un recul purement aléatoire.
    private static AnimationCurve BuildDefaultVerticalPattern()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 0.6f),
            new Keyframe(1f, 0.85f),
            new Keyframe(2f, 1.05f),
            new Keyframe(3f, 1.2f),
            new Keyframe(4f, 1.3f),
            new Keyframe(6f, 1.3f),
            new Keyframe(9f, 1.15f),
            new Keyframe(12f, 1.0f)
        );
        for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
        return curve;
    }

    private static AnimationCurve BuildDefaultHorizontalPattern()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(1f, 0f),
            new Keyframe(2f, 0.15f),
            new Keyframe(3f, -0.25f),
            new Keyframe(4f, 0.4f),
            new Keyframe(5f, -0.5f),
            new Keyframe(6f, 0.55f),
            new Keyframe(7f, -0.5f),
            new Keyframe(8f, 0.45f),
            new Keyframe(9f, -0.4f),
            new Keyframe(10f, 0.35f),
            new Keyframe(12f, -0.3f)
        );
        for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
        return curve;
    }
}
