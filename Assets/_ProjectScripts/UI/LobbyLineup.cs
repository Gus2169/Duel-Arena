using System.Collections.Generic;

/// <summary>
/// Qui le salon montre en tête d'affiche, et qui dessous (2026-10-17, demandé par l'utilisateur : « les deux
/// duellistes en tête d'affiche si on sait qui c'est d'avance, et dessous la liste des spectateurs »).
///
/// L'hôte est toujours le premier duelliste : son robot naît le premier, et RoundManager donne les places de
/// duelliste dans l'ordre d'arrivée (DuelRoster). Le second n'est connu d'avance que s'il n'y a que deux
/// joueurs ; à trois et plus, c'est le premier des autres à finir de se connecter à l'arène, que rien ne
/// prédit. Fonction pure, testée.
/// </summary>
public readonly struct LobbyLineup<T>
{
    /// <summary>Le premier duelliste (l'hôte), s'il y a quelqu'un.</summary>
    public readonly T first;
    public readonly bool hasFirst;
    /// <summary>Le second duelliste, quand on le connaît.</summary>
    public readonly T second;
    public readonly bool secondKnown;
    /// <summary>Le second reste à trouver parmi les spectateurs (trois joueurs et plus).</summary>
    public readonly bool secondAmongStands;
    /// <summary>Ceux qui ne sont pas en tête d'affiche, dans l'ordre d'arrivée.</summary>
    public readonly List<T> stands;
    /// <summary>Les places de spectateur encore libres.</summary>
    public readonly int freeStands;

    private LobbyLineup(T first, bool hasFirst, T second, bool secondKnown, bool secondAmongStands, List<T> stands, int freeStands)
    {
        this.first = first;
        this.hasFirst = hasFirst;
        this.second = second;
        this.secondKnown = secondKnown;
        this.secondAmongStands = secondAmongStands;
        this.stands = stands;
        this.freeStands = freeStands;
    }

    /// <summary>`players` : l'hôte d'abord, puis les autres dans leur ordre d'arrivée. `capacity` : les
    /// places de la partie, hôte compris.</summary>
    public static LobbyLineup<T> Arrange(IReadOnlyList<T> players, int capacity)
    {
        int count = players != null ? players.Count : 0;
        var stands = new List<T>();
        if (count == 0) return new LobbyLineup<T>(default, false, default, false, false, stands, System.Math.Max(0, capacity - DuelRoster.DuelistSlots));

        if (count == DuelRoster.DuelistSlots)
            return new LobbyLineup<T>(players[0], true, players[1], true, false, stands, System.Math.Max(0, capacity - count));

        for (int i = 1; i < count; i++) stands.Add(players[i]);
        // À trois et plus, le second duelliste est l'un d'eux : la tribune compte toutes les places
        // restantes, sauf celle qu'il libérera.
        int free = count > DuelRoster.DuelistSlots ? capacity - count : capacity - DuelRoster.DuelistSlots;
        return new LobbyLineup<T>(players[0], true, default, false, count > DuelRoster.DuelistSlots, stands, System.Math.Max(0, free));
    }
}
