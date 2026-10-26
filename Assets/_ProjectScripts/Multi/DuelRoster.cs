using System.Collections.Generic;
using Unity.Netcode;

/// <summary>Le rôle d'un joueur dans la partie (GDD § 4) : l'un des deux duellistes, ou un spectateur
/// de la tribune qui attend son tour. Décidé par le serveur seul (RoundManager), publié dans la fiche
/// du joueur (PlayerMatchState). Les valeurs gardent leur ordre : elles voyagent par variable réseau.</summary>
public enum PlayerRole : byte
{
    Duelist,
    Spectator,
}

/// <summary>
/// L'organisation des duels, choisie par l'hôte avant la partie (GDD § 4 : « se règle par partie »).
/// Demandé par l'utilisateur le 2026-10-13 : le roi de la colline est un mode parmi d'autres, à choisir
/// au lobby. Le tournoi et le hasard du GDD viendront s'ajouter ici. Ajouter les valeurs à la fin.
/// </summary>
public enum DuelOrganization : byte
{
    /// <summary>Le gagnant du match reste, le perdant retourne au bout de la file, le premier
    /// spectateur prend sa place. Le format par défaut du GDD.</summary>
    KingOfTheHill,
    /// <summary>Les deux premiers arrivés restent duellistes ; les autres regardent. Un spectateur
    /// n'entre que si un duelliste quitte la partie.</summary>
    FixedDuelists,
}

/// <summary>
/// Les réglages d'une partie, fixés par l'hôte avant de la lancer (écran Jouer, en attendant le lobby)
/// et publiés par le serveur (RoundManager) : la capacité n'est jamais une constante (CLAUDE.md), et
/// tout ce qui sort du duel pur se règle par partie (GDD § 3).
/// </summary>
public struct MatchRules : INetworkSerializeByMemcpy
{
    public const int MinCapacity = 2;
    /// <summary>Six au plus : deux duellistes et quatre spectateurs (GDD § 4, « peut-être jusqu'à six
    /// plus tard »). La palette de PlayerPaint en compte sept.</summary>
    public const int MaxCapacity = 6;

    /// <summary>Un match en BO3 : deux manches gagnées. Et en BO5, le format par défaut du GDD : trois.</summary>
    public const int Bo3RoundsToWin = 2, Bo5RoundsToWin = 3;

    /// <summary>Joueurs dans la partie, hôte compris.</summary>
    public byte capacity;
    public DuelOrganization organization;
    /// <summary>Les spectateurs peuvent-ils agir (cailloux, dons de matériel, bagarre) ? Le classé
    /// n'en aura aucune (GDD § 9).</summary>
    public bool spectatorInterventions;
    /// <summary>Manches à gagner pour remporter le match : un BO5 ou un BO3, au choix de l'hôte au salon
    /// (GDD § 5, 2026-10-25). Toute autre valeur (0 : des réglages d'avant ce choix) vaut un BO5.</summary>
    public byte roundsToWin;

    /// <summary>Le prototype du GDD (§ 16) : quatre places (deux duellistes, deux spectateurs), roi de
    /// la colline, interventions des spectateurs permises, en BO5.</summary>
    public static MatchRules Default => new MatchRules
    {
        capacity = 4,
        organization = DuelOrganization.KingOfTheHill,
        spectatorInterventions = true,
        roundsToWin = Bo5RoundsToWin,
    };

    /// <summary>Le format en toutes lettres : « BO5 » ou « BO3 ».</summary>
    public string Format => $"BO{Sanitized().roundsToWin * 2 - 1}";

    /// <summary>Les mêmes réglages, ramenés dans leurs bornes (un réglage enregistré peut venir d'une
    /// ancienne version). Fonction pure, testée.</summary>
    public MatchRules Sanitized()
    {
        MatchRules r = this;
        if (r.capacity < MinCapacity) r.capacity = MinCapacity;
        if (r.capacity > MaxCapacity) r.capacity = MaxCapacity;
        if (r.organization > DuelOrganization.FixedDuelists) r.organization = DuelOrganization.KingOfTheHill;
        if (r.roundsToWin != Bo3RoundsToWin && r.roundsToWin != Bo5RoundsToWin) r.roundsToWin = Bo5RoundsToWin;
        return r;
    }
}

/// <summary>
/// Qui se bat et qui attend : la composition de la partie, côté serveur (2026-10-13). Fonctions pures,
/// testées, sur des listes d'identifiants de clients ; RoundManager les applique aux joueurs présents.
///
/// - Les deux premiers arrivés sont duellistes ; les suivants entrent dans la file des spectateurs,
///   dans l'ordre d'arrivée.
/// - En fin de match, au roi de la colline, le gagnant reste, le perdant passe au bout de la file et
///   le premier de la file prend sa place (GDD § 4).
/// - Un duelliste qui part en plein match perd par forfait : l'autre gagne le match, et le premier de
///   la file prend la place libre à la fin de l'écran de fin (GDD § 4, 2026-10-25 ; RoundManager).
/// </summary>
public static class DuelRoster
{
    /// <summary>Un duel : deux duellistes.</summary>
    public const int DuelistSlots = 2;

    /// <summary>
    /// Met la composition à jour avec les joueurs présents (`present`, dans l'ordre d'arrivée) : les
    /// absents sont retirés, les nouveaux venus inscrits (duelliste s'il reste une place, sinon au
    /// bout de la file). `duelistLeft` dit si un duelliste est parti : le match en cours ne peut pas
    /// continuer. Renvoie vrai si les duellistes ont changé.
    /// </summary>
    public static bool Sync(IReadOnlyList<ulong> present, List<ulong> duelists, List<ulong> queue, out bool duelistLeft)
        => Sync(present, duelists, queue, DuelistSlots, out duelistLeft);

    /// <summary>Sync, avec `slots` places de duelliste (deux en jeu ; une seule pour le banc de test
    /// à deux instances, RoundManager.DebugSoloDuel, qui fait du second joueur un spectateur).
    /// `admitNewcomers` faux : un nouveau venu passe par la file même si une place est libre (celle d'un
    /// duelliste parti en plein match, qui attend la fin de l'écran de fin du forfait).</summary>
    public static bool Sync(IReadOnlyList<ulong> present, List<ulong> duelists, List<ulong> queue, int slots, out bool duelistLeft,
                            bool admitNewcomers = true)
    {
        duelistLeft = false;
        bool changed = false;
        for (int i = duelists.Count - 1; i >= 0; i--)
        {
            if (Contains(present, duelists[i])) continue;
            duelists.RemoveAt(i);
            duelistLeft = true;
            changed = true;
        }
        for (int i = queue.Count - 1; i >= 0; i--)
        {
            if (!Contains(present, queue[i])) queue.RemoveAt(i);
        }

        foreach (ulong id in present)
        {
            if (duelists.Contains(id) || queue.Contains(id)) continue;
            // Une place libre revient d'abord à ceux qui attendent déjà (FillFromQueue) : un nouveau
            // venu n'entre directement que si personne n'attend.
            if (admitNewcomers && duelists.Count < slots && queue.Count == 0)
            {
                duelists.Add(id);
                changed = true;
            }
            else
            {
                queue.Add(id);
            }
        }
        return changed;
    }

    /// <summary>Donne les places de duelliste libres aux premiers de la file. Renvoie vrai si
    /// quelqu'un est entré.</summary>
    public static bool FillFromQueue(List<ulong> duelists, List<ulong> queue, int slots = DuelistSlots)
    {
        bool changed = false;
        while (duelists.Count < slots && queue.Count > 0)
        {
            duelists.Add(queue[0]);
            queue.RemoveAt(0);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// La rotation de fin de match. Au roi de la colline, le perdant prend la place du premier de la
    /// file, au bout de celle-ci, et ce dernier entre à sa place : le gagnant reste. Sans file, sans
    /// gagnant connu, ou en duellistes fixes, rien ne change. Renvoie vrai si quelqu'un a tourné.
    /// </summary>
    public static bool RotateAfterMatch(DuelOrganization organization, List<ulong> duelists, List<ulong> queue, ulong winner)
    {
        if (organization != DuelOrganization.KingOfTheHill || queue.Count == 0) return false;
        if (!duelists.Contains(winner)) return false;

        int loserIndex = -1;
        for (int i = 0; i < duelists.Count; i++)
        {
            if (duelists[i] == winner) continue;
            loserIndex = i;
            break;
        }
        if (loserIndex < 0) return false;

        ulong loser = duelists[loserIndex];
        duelists[loserIndex] = queue[0];
        queue.RemoveAt(0);
        queue.Add(loser);
        return true;
    }

    /// <summary>Le rôle de `clientId`, et sa place dans la file (1 = le prochain à entrer, 0 pour un
    /// duelliste).</summary>
    public static PlayerRole RoleOf(ulong clientId, List<ulong> duelists, List<ulong> queue, out int queuePosition)
    {
        if (duelists.Contains(clientId))
        {
            queuePosition = 0;
            return PlayerRole.Duelist;
        }
        queuePosition = queue.IndexOf(clientId) + 1;
        return PlayerRole.Spectator;
    }

    private static bool Contains(IReadOnlyList<ulong> list, ulong id)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == id) return true;
        }
        return false;
    }
}
