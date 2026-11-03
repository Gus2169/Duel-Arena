using System.Collections.Generic;

/// <summary>Les échelles du Gun Game, au choix de l'hôte (GDD § 11, GG2). La valeur voyage dans les réglages de
/// la partie (MatchRules) : ajouter les nouvelles à la fin.</summary>
public enum GunGameLadder : byte
{
    /// <summary>Les douze armes, dans l'ordre de l'utilisateur (GG8).</summary>
    Full,
    /// <summary>Les armes de stock (proposition de Claude, GG8).</summary>
    Stock,
    /// <summary>La série 30.</summary>
    Series30,
    /// <summary>Les armes laser.</summary>
    Laser,
}

/// <summary>
/// Le Gun Game (GDD § 11, 2026-11-02) : chaque élimination fait passer à l'arme suivante de l'échelle, des plus
/// fortes aux plus faibles (GG3), et la dernière marche est le gant : le premier qui l'y finit d'un coup de taser
/// gagne (GG4). Un coup de gant en cours de partie fait redescendre le tué d'un cran, sans faire monter celui qui
/// frappe (GG5). Chacun garde ses accessoires (GG6) : les marches sont des armes de l'arsenal, avec ceux du joueur.
///
/// Les marches se comptent de 0 (la première arme) à GloveStep (le gant). Le serveur seul les tient
/// (RoundManager, PlayerMatchState.GunGameStep). Fonctions pures, testées.
/// </summary>
public static class GunGame
{
    // Les places de l'arsenal (WeaponController.loadout, qui ne se réordonne pas) : FP-30, FP-12, FX-2, FAP-30, FX-3,
    // FAP-03, FA-07, FX-1, FA-30, RV-30, FX-4, PA-05 pour la grande échelle ; chaque échelle courte prend les armes
    // d'un rang, dans le même ordre.
    private static readonly int[] FullLadder = { 6, 1, 7, 8, 9, 2, 0, 5, 4, 11, 3, 10 };
    private static readonly int[] StockLadder = { 1, 2, 0, 10 };
    private static readonly int[] Series30Ladder = { 6, 8, 4, 11 };
    private static readonly int[] LaserLadder = { 7, 9, 5, 3 };

    /// <summary>La désignation de chaque arme de l'arsenal, par sa place : ce que le salon annonce sans charger les
    /// armes (un test vérifie qu'elles sont celles du préfab joueur).</summary>
    public static readonly IReadOnlyList<string> Designations = new[]
    {
        "FA-07", "FP-12", "FAP-03", "FX-4", "FA-30", "FX-1", "FP-30", "FX-2", "FAP-30", "FX-3", "PA-05", "RV-30",
    };

    /// <summary>Le nom de la dernière marche.</summary>
    public const string GloveName = "Gant";

    /// <summary>Les échelles, dans l'ordre du salon.</summary>
    public static readonly IReadOnlyList<GunGameLadder> Ladders = new[]
    {
        GunGameLadder.Full, GunGameLadder.Stock, GunGameLadder.Series30, GunGameLadder.Laser,
    };

    /// <summary>Les armes d'une échelle (des places de l'arsenal), dans l'ordre où on les monte. Le gant suit.</summary>
    public static IReadOnlyList<int> Weapons(GunGameLadder ladder) => ladder switch
    {
        GunGameLadder.Stock => StockLadder,
        GunGameLadder.Series30 => Series30Ladder,
        GunGameLadder.Laser => LaserLadder,
        _ => FullLadder,
    };

    /// <summary>La marche du gant : la dernière.</summary>
    public static int GloveStep(GunGameLadder ladder) => Weapons(ladder).Count;

    /// <summary>Le nombre de marches, gant compris.</summary>
    public static int StepCount(GunGameLadder ladder) => GloveStep(ladder) + 1;

    /// <summary>La place de l'arme d'une marche : une arme de l'arsenal, ou le gant (WeaponController.GloveSlot).</summary>
    public static int SlotAt(GunGameLadder ladder, int step)
    {
        IReadOnlyList<int> weapons = Weapons(ladder);
        if (step < 0) step = 0;
        return step < weapons.Count ? weapons[step] : WeaponController.GloveSlot;
    }

    /// <summary>La désignation de l'arme d'une marche (« FX-3 », ou « Gant »).</summary>
    public static string WeaponNameAt(GunGameLadder ladder, int step)
    {
        int slot = SlotAt(ladder, step);
        return slot >= 0 && slot < Designations.Count ? Designations[slot] : GloveName;
    }

    /// <summary>Le nom court d'une échelle, sur son bouton au salon.</summary>
    public static string Name(GunGameLadder ladder) => ladder switch
    {
        GunGameLadder.Stock => "Stock",
        GunGameLadder.Series30 => "Série 30",
        GunGameLadder.Laser => "Laser",
        _ => "Grande",
    };

    /// <summary>L'échelle dans une phrase (« grande échelle », « échelle laser ») : le résumé de la partie.</summary>
    public static string Phrase(GunGameLadder ladder) => ladder switch
    {
        GunGameLadder.Stock => "échelle stock",
        GunGameLadder.Series30 => "échelle série 30",
        GunGameLadder.Laser => "échelle laser",
        _ => "grande échelle",
    };

    /// <summary>Les armes d'une échelle en toutes lettres (« FP-12, FAP-03, FA-07, PA-05, puis le gant »).</summary>
    public static string Describe(GunGameLadder ladder)
    {
        var names = new List<string>();
        foreach (int slot in Weapons(ladder)) names.Add(Designations[slot]);
        return string.Join(", ", names) + ", puis le gant";
    }

    /// <summary>Ce que change une élimination : les marches du vainqueur et du tué, et si le vainqueur gagne.</summary>
    public readonly struct KillOutcome
    {
        public readonly int killerStep, victimStep;
        public readonly bool killerWins;

        public KillOutcome(int killerStep, int victimStep, bool killerWins)
        {
            this.killerStep = killerStep;
            this.victimStep = victimStep;
            this.killerWins = killerWins;
        }
    }

    /// <summary>
    /// Une élimination dans le Gun Game : à l'arme, le vainqueur passe à la marche suivante ; au gant, sur la
    /// dernière marche, il gagne (GG4) ; au gant avant, le tué redescend d'un cran, au plus bas la première arme,
    /// et le vainqueur ne monte pas (GG5). Fonction pure, testée.
    /// </summary>
    public static KillOutcome AfterKill(GunGameLadder ladder, int killerStep, int victimStep, bool byGlove)
    {
        int glove = GloveStep(ladder);
        killerStep = System.Math.Clamp(killerStep, 0, glove);
        victimStep = System.Math.Clamp(victimStep, 0, glove);
        if (byGlove)
        {
            if (killerStep == glove) return new KillOutcome(killerStep, victimStep, true);
            return new KillOutcome(killerStep, System.Math.Max(0, victimStep - 1), false);
        }
        return new KillOutcome(System.Math.Min(glove, killerStep + 1), victimStep, false);
    }
}
