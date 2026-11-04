using UnityEngine;

/// <summary>
/// Le tampon de lecture des inputs d'un client, sur le serveur. Simulés à leur arrivée, ils faisaient de la position de
/// son robot un escalier des instants de réception : une image sans paquet il ne bougeait pas, la suivante il faisait
/// deux pas ; les poses publiées et l'historique du rewind en héritaient. Ici, le serveur les rejoue au rythme du temps
/// réel : chaque image lui donne un crédit de temps, un input ne part que s'il y tient en entier (jamais tronqué : le
/// joueur a prédit avec sa durée complète, voir Move()).
/// La réserve se règle d'elle-même. Un trou d'arrivée vide la file ; les inputs qui suivent, joués sans accélérer, gardent
/// cette avance et couvrent le trou suivant. Ce qui n'a servi à rien pendant une fenêtre (le plus bas niveau de la file) est
/// repris sur la fenêtre d'après : le retard ajouté suit la pire gigue de la dernière seconde, de l'ordre de 10 ms en réseau
/// local. Au-delà de MaxHold, la file se résorbe sans attendre.
/// Fonctions pures : ni horloge ni réseau ici, seulement les durées qu'on lui donne.
/// </summary>
public sealed class InputPlayout
{
    /// <summary>La mémoire de la réserve (s) : la plus basse file d'une fenêtre dit ce qu'on peut reprendre à la suivante.</summary>
    public const float Window = 1f;

    /// <summary>Ce qu'on garde en plus du strict nécessaire (s).</summary>
    public const float Margin = 0.004f;

    /// <summary>Le retard qu'on s'autorise à tenir (s) : au-delà, le robot serait trop loin de ce que le joueur a joué.</summary>
    public const float MaxHold = 0.06f;

    /// <summary>La constante de temps de la résorption au-delà de MaxHold (s).</summary>
    public const float CatchUpTime = 0.1f;

    private float credit;
    private float lastInput;
    private float drainRate;
    private float windowLeft = Window;
    private float lowest = float.MaxValue;

    /// <summary>Le temps que cette image peut encore simuler (s).</summary>
    public float Credit => credit;

    /// <summary>Début d'image : le temps réel écoulé, et ce qui est repris sur la file (`queued` : son temps de jeu).</summary>
    public void Begin(float elapsed, float queued)
    {
        credit += elapsed * (1f + drainRate);
        if (queued > MaxHold) credit += (queued - MaxHold) * Mathf.Min(1f, elapsed / CatchUpTime);
    }

    /// <summary>Un input de `dt` secondes tient-il en entier dans le crédit de l'image ?</summary>
    public bool CanPlay(float dt) => dt <= credit;

    /// <summary>Cet input part : il paie son temps.</summary>
    public void Spend(float dt)
    {
        credit -= dt;
        lastInput = dt;
    }

    /// <summary>Fin d'image, avec ce qui reste en file. File vide, le crédit ne garde que ce qui manquera à l'image suivante
    /// pour couvrir un input : au-delà, ce qui arriverait ensuite partirait d'un bloc, et la réserve qu'il apporte serait
    /// perdue. Il ne sert qu'après un paquet en retard, quand la file est restée vide quelques images : l'input part à son
    /// arrivée, et ceux qui le suivent aussi si l'image du client vaut un nombre entier d'images de l'hôte (sinon ils attendent
    /// seulement moins). Sans retard, rien ne l'a rempli : chaque input d'un client plus lent que l'hôte reste en file à peu
    /// près le temps de payer son image.</summary>
    public void End(float elapsed, float queued)
    {
        if (queued <= 0f) credit = Mathf.Min(credit, Mathf.Max(0f, lastInput - elapsed));

        lowest = Mathf.Min(lowest, queued);
        windowLeft -= elapsed;
        if (windowLeft > 0f) return;
        drainRate = Mathf.Max(0f, lowest - Margin) / Window;
        lowest = float.MaxValue;
        windowLeft = Window;
    }
}
