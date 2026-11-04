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
/// <summary>
/// Le choix de l'arme, un réglage du salon, l'un ou l'autre (GDD § 11, I9 ; 2026-11-03) : libre, chacun la sienne en
/// préparation ; pile ou face, une famille imposée aux deux par celui que la pièce, puis la défaite de la manche,
/// désigne (CoinFlip) ; arsenal commun, la même arme et les mêmes accessoires pour les deux, tirés au sort toutes les deux
/// manches, sans préparation (CommonArsenal). 🚨 Sérialisé par sa valeur : ajouter à la fin.
/// </summary>
public enum WeaponChoice : byte
{
    Free,
    CoinFlip,
    CommonArsenal,
}

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

    /// <summary>Le Golden Gun se joue en plus de manches : le premier à 5, ou à 7 (GDD § 11, G4).</summary>
    public const int GoldenGunShortRounds = 5, GoldenGunLongRounds = 7;

    /// <summary>Le Gun Game se joue d'une traite, avec réapparitions : une seule « manche », gagnée en finissant
    /// l'échelle (GDD § 11, GG1). L'Élimination confirmée aussi, gagnée aux éliminations confirmées.</summary>
    public const int GunGameRounds = 1;

    /// <summary>Les éliminations à faire au Death match du classique, et les éliminations confirmées de
    /// l'Élimination confirmée, au choix de l'hôte (GDD § 11, N8 ; 10 par défaut, la proposition de Claude pour le
    /// Death match, la règle du GDD pour l'Élimination confirmée).</summary>
    public static readonly byte[] KillChoices = { 5, 10, 15 };
    public const byte DefaultKillsToWin = 10;

    /// <summary>Joueurs dans la partie, hôte compris.</summary>
    public byte capacity;
    public DuelOrganization organization;
    /// <summary>Les spectateurs peuvent-ils agir (cailloux, dons de matériel, bagarre) ? Le classé
    /// n'en aura aucune (GDD § 9).</summary>
    public bool spectatorInterventions;
    /// <summary>Manches à gagner pour remporter le match : un BO5 ou un BO3, au choix de l'hôte au salon
    /// (GDD § 5, 2026-10-25). Toute autre valeur (0 : des réglages d'avant ce choix) vaut un BO5.</summary>
    public byte roundsToWin;
    /// <summary>Les deux duellistes du premier match sont tirés au sort parmi tous les joueurs, au lancement
    /// (2026-10-27, demandé par l'utilisateur : qu'on ne sache pas d'avance qui commence) ; sinon ce sont
    /// l'hôte et le deuxième arrivé au salon.</summary>
    public bool randomDuelists;
    /// <summary>Le mode de jeu (GDD § 11, 2026-11-02) : un réglage de la partie comme les autres, que l'hôte
    /// change au salon sans recréer la partie. Seul un mode jouable reste (Sanitized) ; 0 : le classique.</summary>
    public GameModeId mode;
    /// <summary>L'échelle du Gun Game (GDD § 11, GG2), retenue même dans un autre mode : elle revient quand
    /// l'hôte y repasse.</summary>
    public GunGameLadder ladder;
    /// <summary>Le Death match du classique (GDD § 11, N8, M5) : le premier à `killsToWin` éliminations, avec
    /// réapparitions, à la place des manches. Une case du classique : retenue dans un autre mode, sans effet
    /// (IsDeathMatch).</summary>
    public bool deathMatch;
    /// <summary>Les éliminations à faire au Death match, ou les éliminations confirmées à l'Élimination confirmée :
    /// 5, 10 ou 15 (toute autre valeur vaut 10). Un seul réglage, gardé d'un mode à l'autre.</summary>
    public byte killsToWin;
    /// <summary>Le choix de l'arme (GDD § 11, I9) : libre, pile ou face, ou arsenal commun. Retenu dans un mode où il n'a
    /// pas de sens, sans effet (EffectiveWeaponChoice).</summary>
    public WeaponChoice weaponChoice;
    /// <summary>Tête seulement (GDD § 11, I3) : seuls les tirs à la tête blessent ; le gant tue toujours. Une case, dans
    /// tous les modes.</summary>
    public bool headshotsOnly;

    /// <summary>Une empreinte de tous les réglages, sans allocation : un écran qui les montre (le salon de l'arène) se refait
    /// quand elle change. Tout nouveau champ s'y ajoute (testé : chacun la change). Fonction pure.</summary>
    public int Fingerprint()
    {
        int key = capacity;
        key = key * 31 + (int)organization;
        key = key * 31 + (spectatorInterventions ? 1 : 0);
        key = key * 31 + roundsToWin;
        key = key * 31 + (randomDuelists ? 1 : 0);
        key = key * 31 + (int)mode;
        key = key * 31 + (int)ladder;
        key = key * 31 + (deathMatch ? 1 : 0);
        key = key * 31 + killsToWin;
        key = key * 31 + (int)weaponChoice;
        key = key * 31 + (headshotsOnly ? 1 : 0);
        return key;
    }

    /// <summary>Le choix de l'arme qui s'applique : pile ou face et l'arsenal commun ne valent que dans les modes à
    /// manches où l'arme est libre (WeaponChoiceApplies) ; ailleurs, l'arme est libre, ou imposée par le mode.</summary>
    public WeaponChoice EffectiveWeaponChoice => WeaponChoiceApplies(mode, IsDeathMatch) ? weaponChoice : WeaponChoice.Free;

    /// <summary>Pile ou face et l'arsenal commun ont-ils un sens dans ce mode ? Seulement là où l'on joue des manches avec
    /// l'arme de son choix : le classique (pas son Death match, d'une traite) et le Tournoi, dont les matchs sont des
    /// classiques. Le Golden Gun et le Gun Game imposent leur arme ; les modes à réapparitions n'ont pas de manches.
    /// Fonction pure, testée.</summary>
    public static bool WeaponChoiceApplies(GameModeId mode, bool deathMatch)
        => (mode == GameModeId.Classic && !deathMatch) || mode == GameModeId.Tournament;

    /// <summary>Le Death match se joue-t-il ? Seulement dans le classique.</summary>
    public bool IsDeathMatch => mode == GameModeId.Classic && deathMatch;

    /// <summary>Le match se gagne-t-il aux éliminations (`killsToWin`) plutôt qu'aux manches ? Au Death match du
    /// classique, et à l'Élimination confirmée (des éliminations confirmées).</summary>
    public bool CountsKills => IsDeathMatch || mode == GameModeId.KillConfirmed;

    /// <summary>Le match se gagne-t-il aux points ? À la Zone (ZoneMode).</summary>
    public bool CountsPoints => mode == GameModeId.Zone;

    /// <summary>Le Tag Team se joue-t-il ? Deux équipes, aux points d'équipe (TagTeam).</summary>
    public bool IsTagTeam => mode == GameModeId.TagTeam;

    /// <summary>Le Tournoi se joue-t-il ? Une poule, chacun contre chacun, puis une finale, plusieurs matchs à la fois
    /// (Tournament).</summary>
    public bool IsTournament => mode == GameModeId.Tournament;

    /// <summary>Ce qu'il faut gagner pour remporter le match : des manches, des éliminations au Death match et à
    /// l'Élimination confirmée, des points à la Zone, ou des points d'équipe au Tag Team. Au Tournoi, les manches d'un match
    /// de la poule (chaque match a les siennes : RoundManager en couloir).</summary>
    public int WinTarget
    {
        get
        {
            MatchRules r = Sanitized();
            if (r.IsTagTeam) return TagTeam.TargetPoints;
            if (r.IsTournament) return Tournament.PoolRoundsToWin;
            if (r.CountsPoints) return ZoneMode.TargetPoints;
            return r.CountsKills ? r.killsToWin : r.roundsToWin;
        }
    }

    /// <summary>Le prototype du GDD (§ 16) : quatre places (deux duellistes, deux spectateurs), roi de
    /// la colline, interventions des spectateurs permises, en BO5.</summary>
    public static MatchRules Default => new MatchRules
    {
        capacity = 4,
        organization = DuelOrganization.KingOfTheHill,
        spectatorInterventions = true,
        roundsToWin = Bo5RoundsToWin,
    };

    /// <summary>Le format en toutes lettres : « BO5 » ou « BO3 » au classique, « Death match à 10 » s'il est coché,
    /// « premier à 5 » au Golden Gun, qui en joue plus, l'échelle au Gun Game, qui n'a pas de manches, « premier à
    /// 10 » à l'Élimination confirmée, en éliminations confirmées, « premier à 3 000 points » à la Zone, « première
    /// équipe à 100 points » au Tag Team.</summary>
    public string Format
    {
        get
        {
            MatchRules r = Sanitized();
            if (r.IsDeathMatch) return $"Death match à {r.killsToWin}";
            return r.mode switch
            {
                GameModeId.GoldenGun => $"premier à {r.roundsToWin}",
                GameModeId.GunGame => GunGame.Phrase(r.ladder),
                GameModeId.KillConfirmed => $"premier à {r.killsToWin}",
                GameModeId.Zone => $"premier à {ZoneMode.Points(ZoneMode.TargetPoints)} points",
                GameModeId.TagTeam => $"première équipe à {TagTeam.TargetPoints} points",
                GameModeId.Tournament => $"poule en BO{Tournament.PoolRoundsToWin * 2 - 1}, finale en BO{Tournament.FinalRoundsToWin * 2 - 1}",
                _ => $"BO{r.roundsToWin * 2 - 1}",
            };
        }
    }

    /// <summary>Les nombres de manches à gagner qu'offre un mode, le premier par défaut : 3 ou 2 (BO5, BO3)
    /// au classique, 5 ou 7 au Golden Gun (GDD § 11, G4), une seule au Gun Game, à l'Élimination confirmée, à la
    /// Zone et au Tag Team, qui se jouent d'une traite ; au Tournoi, celles de la poule, fixées (BO3, BO5 en finale).</summary>
    public static (byte usual, byte other) RoundChoices(GameModeId mode) => mode switch
    {
        GameModeId.GoldenGun => ((byte)GoldenGunShortRounds, (byte)GoldenGunLongRounds),
        GameModeId.GunGame or GameModeId.KillConfirmed or GameModeId.Zone or GameModeId.TagTeam => ((byte)GunGameRounds, (byte)GunGameRounds),
        GameModeId.Tournament => ((byte)Tournament.PoolRoundsToWin, (byte)Tournament.PoolRoundsToWin),
        _ => ((byte)Bo5RoundsToWin, (byte)Bo3RoundsToWin),
    };

    /// <summary>Les mêmes réglages, ramenés dans leurs bornes (un réglage enregistré peut venir d'une
    /// ancienne version) : un mode qui ne se joue pas encore redevient le classique, les manches sont
    /// celles que son mode offre, le Tag Team a au moins ses quatre places (deux contre deux), le Tournoi ses trois. Fonction pure,
    /// testée.</summary>
    public MatchRules Sanitized()
    {
        MatchRules r = this;
        if (r.capacity < MinCapacity) r.capacity = MinCapacity;
        if (r.capacity > MaxCapacity) r.capacity = MaxCapacity;
        if (r.organization > DuelOrganization.FixedDuelists) r.organization = DuelOrganization.KingOfTheHill;
        if (!GameModeCatalog.IsPlayable(r.mode)) r.mode = GameModeId.Classic;
        if (r.mode == GameModeId.TagTeam && r.capacity < TagTeam.MinPlayers) r.capacity = TagTeam.MinPlayers;
        if (r.mode == GameModeId.Tournament && r.capacity < Tournament.MinPlayers) r.capacity = Tournament.MinPlayers;
        if (r.ladder > GunGameLadder.Laser) r.ladder = GunGameLadder.Full;
        (byte usual, byte other) = RoundChoices(r.mode);
        if (r.roundsToWin != usual && r.roundsToWin != other) r.roundsToWin = usual;
        if (System.Array.IndexOf(KillChoices, r.killsToWin) < 0) r.killsToWin = DefaultKillsToWin;
        if (r.weaponChoice > WeaponChoice.CommonArsenal) r.weaponChoice = WeaponChoice.Free;
        return r;
    }
}

/// <summary>
/// Qui se bat et qui attend : la composition de la partie, côté serveur (2026-10-13). Fonctions pures,
/// testées, sur des listes d'identifiants de clients ; RoundManager les applique aux joueurs présents.
///
/// - Les deux premiers arrivés sont duellistes ; les suivants entrent dans la file des spectateurs,
///   dans l'ordre d'arrivée. Une partie lancée d'un salon en ligne suit l'ordre du salon, pas celui
///   d'arrivée dans l'arène, où tous se connectent à la fois (ArrangeByRank, 2026-10-27).
/// - Au tirage au sort (MatchRules.randomDuelists), deux joueurs au hasard (DrawDuelists).
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

        // Par indice : le serveur passe ici à chaque image, et un foreach sur cette interface allouerait son énumérateur.
        for (int i = 0; i < present.Count; i++)
        {
            ulong id = present[i];
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

    /// <summary>
    /// La composition d'une partie lancée d'un salon, refaite de zéro tant que ses joueurs arrivent dans
    /// l'arène (2026-10-27) : `ranks[i]` est le rang du joueur `present[i]` dans l'ordre du salon (tirage
    /// compris), -1 pour qui n'y était pas. Les `slots` premiers rangs sont duellistes, même s'ils
    /// arrivent après d'autres : leur place les attend. Les autres attendent dans l'ordre du salon, puis
    /// ceux venus sans y être passés, dans leur ordre d'arrivée.
    /// </summary>
    public static void ArrangeByRank(IReadOnlyList<ulong> present, IReadOnlyList<int> ranks, int slots, List<ulong> duelists, List<ulong> queue)
    {
        duelists.Clear();
        queue.Clear();
        var ranked = new List<(int rank, int arrival)>();
        for (int i = 0; i < present.Count; i++)
        {
            int rank = ranks != null && i < ranks.Count ? ranks[i] : -1;
            ranked.Add((rank < 0 ? int.MaxValue : rank, i));
        }
        ranked.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.arrival.CompareTo(b.arrival));
        foreach ((int rank, int arrival) in ranked)
        {
            if (rank < slots) duelists.Add(present[arrival]);
            else queue.Add(present[arrival]);
        }
    }

    /// <summary>
    /// Le tirage au sort des duellistes (2026-10-27) : `count` joueurs de `order`, au hasard (`random`), passent
    /// en tête, dans l'ordre du tirage ; les autres gardent leur ordre derrière eux. Avec moins de joueurs,
    /// tous restent. Fonction pure (au hasard près, que donne l'appelant), testée.
    /// </summary>
    public static List<T> DrawDuelists<T>(IReadOnlyList<T> order, int count, System.Random random)
    {
        var rest = new List<T>(order ?? System.Array.Empty<T>());
        var drawn = new List<T>();
        while (drawn.Count < count && rest.Count > 0)
        {
            int pick = random != null ? random.Next(rest.Count) : 0;
            drawn.Add(rest[pick]);
            rest.RemoveAt(pick);
        }
        drawn.AddRange(rest);
        return drawn;
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
