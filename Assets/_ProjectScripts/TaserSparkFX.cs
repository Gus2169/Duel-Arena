using UnityEngine;

/// <summary>
/// Étincelles électriques au poing du gant, au moment du CONTACT (catégorie C pure, purement
/// cosmétique — rien ici n'est lu par la simulation, le hitbox ou le tir).
///
/// Ce composant vit sur <c>GantChoc.prefab</c>, donc il existe sur CHAQUE instance du gant :
/// celle posée sur le Model (vue par les autres joueurs) et celle clonée par FirstPersonView
/// sous la caméra (vue du propriétaire pendant son propre coup). Les deux retrouvent le MÊME
/// TaserController en remontant jusqu'à la racine du joueur (transform.root), puisque la copie
/// de FirstPersonView reste un descendant de cette racine même une fois accrochée à la caméra.
///
/// Le coup part à l'appui (TaserController.OnStrike), mais le poing ne touche qu'au CONTACT,
/// ContactDelay plus tard (le moment que le serveur utilise pour résoudre le coup) : l'étincelle
/// attend ce même délai pour rester synchronisée avec le geste, jamais avec l'appui.
/// </summary>
public class TaserSparkFX : MonoBehaviour
{
    [SerializeField] private ParticleSystem sparks;
    [SerializeField] private Light flash;
    [SerializeField] private float flashDuration = 0.08f;

    private TaserController taser;

    private void Awake()
    {
        taser = GetComponentInParent<TaserController>();
        if (taser == null) taser = transform.root.GetComponentInChildren<TaserController>();
        if (flash != null) flash.enabled = false;
    }

    private void OnEnable()
    {
        if (taser != null) taser.OnStrike += HandleStrike;
    }

    private void OnDisable()
    {
        if (taser != null) taser.OnStrike -= HandleStrike;
        CancelInvoke();
    }

    private void HandleStrike()
    {
        Invoke(nameof(PlaySpark), taser != null ? taser.ContactDelay : 0f);
    }

    private void PlaySpark()
    {
        if (sparks != null) sparks.Play();
        if (flash == null) return;
        flash.enabled = true;
        Invoke(nameof(HideFlash), flashDuration);
    }

    private void HideFlash()
    {
        if (flash != null) flash.enabled = false;
    }
}
