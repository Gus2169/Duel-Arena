using System.Collections.Generic;

/// <summary>
/// Qui le salon montre en tête d'affiche, et qui dessous (2026-10-17, demandé par l'utilisateur : « les deux
/// duellistes en tête d'affiche si on sait qui c'est d'avance, et dessous la liste des spectateurs »).
///
/// Les duellistes sont l'hôte et le deuxième arrivé au salon ; les suivants attendent sur la tribune, dans
/// leur ordre d'arrivée (2026-10-27, demandé par l'utilisateur : le deuxième disparaissait de la tête
/// d'affiche dès qu'un troisième arrivait). L'arène suit cet ordre (SessionFlow.LaunchOrder, RoundManager),
/// même si les joueurs s'y connectent dans un autre. Avec le tirage au sort (MatchRules.randomDuelists),
/// personne n'est en tête d'affiche : tous sont dans le tirage, fait au lancement. Fonction pure, testée.
/// </summary>
public readonly struct LobbyLineup<T>
{
    /// <summary>Le premier duelliste (l'hôte), s'il y a quelqu'un et pas de tirage.</summary>
    public readonly T first;
    public readonly bool hasFirst;
    /// <summary>Le second duelliste (le deuxième arrivé), quand il y en a un et pas de tirage.</summary>
    public readonly T second;
    public readonly bool secondKnown;
    /// <summary>Les duellistes seront tirés au sort au lancement : la tête d'affiche reste à découvrir.</summary>
    public readonly bool drawn;
    /// <summary>Ceux qui ne sont pas en tête d'affiche, dans l'ordre d'arrivée (tous, avec le tirage).</summary>
    public readonly List<T> stands;
    /// <summary>Les places encore libres sous la tête d'affiche.</summary>
    public readonly int freeStands;

    private LobbyLineup(T first, bool hasFirst, T second, bool secondKnown, bool drawn, List<T> stands, int freeStands)
    {
        this.first = first;
        this.hasFirst = hasFirst;
        this.second = second;
        this.secondKnown = secondKnown;
        this.drawn = drawn;
        this.stands = stands;
        this.freeStands = freeStands;
    }

    /// <summary>`players` : l'hôte d'abord, puis les autres dans leur ordre d'arrivée. `capacity` : les
    /// places de la partie, hôte compris. `randomDuelists` : les duellistes seront tirés au sort.</summary>
    public static LobbyLineup<T> Arrange(IReadOnlyList<T> players, int capacity, bool randomDuelists = false)
    {
        int count = players != null ? players.Count : 0;
        var stands = new List<T>();
        if (randomDuelists)
        {
            // Tous dans le tirage : les places libres sont celles de la partie qui restent.
            for (int i = 0; i < count; i++) stands.Add(players[i]);
            return new LobbyLineup<T>(default, false, default, false, true, stands, System.Math.Max(0, capacity - count));
        }

        // Sous la tête d'affiche, les places de la tribune : celles qui restent une fois les deux duellistes placés.
        int free = System.Math.Max(0, capacity - System.Math.Max(count, DuelRoster.DuelistSlots));
        if (count == 0) return new LobbyLineup<T>(default, false, default, false, false, stands, free);
        for (int i = DuelRoster.DuelistSlots; i < count; i++) stands.Add(players[i]);
        bool second = count >= DuelRoster.DuelistSlots;
        return new LobbyLineup<T>(players[0], true, second ? players[1] : default, second, false, stands, free);
    }
}
