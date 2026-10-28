using UnityEngine;

/// <summary>
/// La charge d'un tir (le FX-2, 2026-10-26, GDD § 6) : on tient la gâchette pour charger, on la lâche
/// pour tirer. Comptée sur une horloge d'inputs, comme l'épaulement : par le tireur sur ceux qu'il envoie,
/// par le serveur sur ceux qu'il reçoit, dans l'ordre des numéros (PlayerLocomotion). Le client n'annonce
/// jamais sa charge.
///
/// Elle ne monte que pendant les inputs où la gâchette est tenue ET où l'arme pourrait tirer (la règle
/// est dans WeaponController : LocalChargeAllowed, ServerChargeAllowed). Lâchée, elle reste telle quelle
/// pour le tir qui la lâche ; le tir l'emporte (le plancher), si bien qu'un client modifié qui tiendrait
/// la gâchette en demandant des tirs recommence chaque charge de zéro.
///
/// Le serveur juge un tir sur la charge de l'input que le tireur annonce, même si des inputs plus récents
/// sont déjà arrivés : la demande voyage en fiable, les inputs en non fiable, et une demande retardée par
/// une perte arrive parfois après les premiers inputs de la charge suivante. Une charge ne se met pas de
/// côté pour autant : finie depuis plus de MaxAge (horloge des inputs), elle ne compte plus.
/// </summary>
public sealed class ChargeTracker
{
    /// <summary>Une charge, en cours ou lâchée : de `start` (le début de son premier input) à `end` (la
    /// fin de son dernier). `active` : l'input le plus récent charge encore.</summary>
    public struct Run
    {
        public double start, end;
        public bool active;
    }

    /// <summary>Âge maximal (s, horloge des inputs) d'une charge lâchée que tire une demande arrivée en
    /// retard. Au-delà, elle ne compte plus : on ne garde pas un tir plein de côté.</summary>
    public const double MaxAge = 0.5;

    private const int Size = 1024; // plus d'une seconde d'inputs même à 800 images/s
    private readonly int[] sequences = new int[Size];
    private readonly Run[] runs = new Run[Size];
    private readonly double[] clocks = new double[Size];
    private Run current;
    private double clock;
    private double floor = double.NegativeInfinity;

    public ChargeTracker()
    {
        for (int i = 0; i < Size; i++) sequences[i] = -1;
    }

    /// <summary>La dernière charge, en cours ou lâchée.</summary>
    public Run Current => current;

    /// <summary>Compte un input de l'horloge `inputStart` à `inputEnd` : s'il charge, la charge en cours
    /// s'étend jusqu'à sa fin, ou une nouvelle commence avec lui ; sinon, la dernière reste telle quelle.
    /// Fonction pure, testée.</summary>
    public static void Track(ref Run run, double inputStart, double inputEnd, bool charging)
    {
        if (charging)
        {
            if (!run.active) run.start = inputStart;
            run.end = inputEnd;
        }
        run.active = charging;
    }

    /// <summary>La durée (s) d'une charge qui reste à tirer : ce qui en dépasse le plancher. Fonction
    /// pure, testée.</summary>
    public static float Seconds(in Run run, double floor)
        => (float)System.Math.Max(0.0, run.end - System.Math.Max(run.start, floor));

    /// <summary>Compte l'input `sequence`, de l'horloge `inputStart` à `inputEnd`, et retient ce que
    /// valait la charge après lui.</summary>
    public void Record(int sequence, double inputStart, double inputEnd, bool charging)
    {
        Track(ref current, inputStart, inputEnd, charging);
        clock = inputEnd;
        if (sequence < 0) return;
        int index = sequence & (Size - 1);
        sequences[index] = sequence;
        runs[index] = current;
        clocks[index] = inputEnd;
    }

    /// <summary>La charge (s) qu'emporterait un tir maintenant : celle du dernier input compté.</summary>
    public float Seconds() => Seconds(current, floor);

    /// <summary>Le tir vient de partir sur le dernier input compté (chez le tireur) : il emporte sa charge.</summary>
    public void ConsumeLatest() => floor = System.Math.Max(floor, current.end);

    /// <summary>La charge (s) qu'emporte un tir annoncé à l'input `claimed` et jugé sur l'input
    /// `reference` (le serveur, PlayerLocomotion.ReferenceInput).</summary>
    public float SecondsFor(int claimed, int reference) => Seconds(RunFor(claimed, reference), floor);

    /// <summary>Ce tir (serveur) est parti : il emporte sa charge, jamais celle qui l'a suivie.</summary>
    public void Consume(int claimed, int reference) => floor = System.Math.Max(floor, RunFor(claimed, reference).end);

    /// <summary>La charge (0 à 1) à montrer, gâchette tenue : rien sans charge en cours.</summary>
    public float Displayed(float chargeTime)
        => current.active && chargeTime > 0f ? Mathf.Clamp01(Seconds() / chargeTime) : 0f;

    private Run RunFor(int claimed, int reference)
    {
        int sequence = System.Math.Min(claimed, reference);
        Run run = Known(sequence) ? runs[sequence & (Size - 1)] : current;
        double now = Known(reference) ? clocks[reference & (Size - 1)] : clock;
        return now - run.end > MaxAge ? default : run;
    }

    private bool Known(int sequence) => sequence >= 0 && sequences[sequence & (Size - 1)] == sequence;
}
