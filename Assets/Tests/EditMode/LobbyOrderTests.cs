using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// L'ordre du salon jusque dans l'arène (2026-10-27, demandé par l'utilisateur) : la deuxième personne arrivée
/// au salon est le second duelliste, la troisième un spectateur, même si elle arrive dans l'arène avant les
/// autres (tous s'y connectent à la fois) ; et le tirage au sort des duellistes. Chaque joueur annonce à l'hôte
/// son identifiant de joueur des services, qui dit sa place au salon.
/// </summary>
public class LobbyOrderTests
{
    [Test]
    public void LOrdreDuSalon_LHoteDAbord_PuisParArrivee()
    {
        var t = new DateTime(2026, 10, 27, 20, 0, 0, DateTimeKind.Utc);
        var players = new List<(string id, DateTime joined)>
        {
            ("troisieme", t.AddSeconds(30)),
            ("hote", t.AddSeconds(50)),   // un nouvel hôte peut être arrivé après les autres
            ("deuxieme", t.AddSeconds(10)),
        };
        CollectionAssert.AreEqual(new[] { "hote", "deuxieme", "troisieme" }, OnlineSession.SalonOrder(players, "hote"));
        CollectionAssert.AreEqual(new[] { "troisieme" }, OnlineSession.PlayersBeyondCapacity(players, "hote", 2),
            "Au-delà des places, le dernier arrivé repart, jamais l'hôte.");
    }

    [Test]
    public void DansLArene_LesDeuxPremiersDuSalon_SontDuellistes_MemeArrivesApresLesAutres()
    {
        // Le troisième du salon (client 3) se connecte avant le deuxième (client 2).
        var duelists = new List<ulong>();
        var queue = new List<ulong>();
        DuelRoster.ArrangeByRank(new ulong[] { 0, 3 }, new[] { 0, 2 }, DuelRoster.DuelistSlots, duelists, queue);
        CollectionAssert.AreEqual(new ulong[] { 0 }, duelists, "La place du deuxième du salon l'attend.");
        CollectionAssert.AreEqual(new ulong[] { 3 }, queue, "Le troisième va sur la tribune dès son arrivée.");

        DuelRoster.ArrangeByRank(new ulong[] { 0, 3, 2, 7 }, new[] { 0, 2, 1, -1 }, DuelRoster.DuelistSlots, duelists, queue);
        CollectionAssert.AreEqual(new ulong[] { 0, 2 }, duelists, "Le deuxième arrivé : il prend sa place.");
        CollectionAssert.AreEqual(new ulong[] { 3, 7 }, queue, "La file dans l'ordre du salon, puis ceux venus sans y être passés.");
    }

    [Test]
    public void LeTirage_MetDeuxJoueursAuHasardEnTete_EtGardeLOrdreDesAutres()
    {
        var order = new[] { "a", "b", "c", "d", "e" };
        var seen = new HashSet<string>();
        for (int seed = 0; seed < 200; seed++)
        {
            List<string> drawn = DuelRoster.DrawDuelists(order, DuelRoster.DuelistSlots, new Random(seed));
            CollectionAssert.AreEquivalent(order, drawn, "Tous les joueurs, une fois chacun.");
            var rest = drawn.GetRange(2, drawn.Count - 2);
            var expectedRest = new List<string>(order);
            expectedRest.Remove(drawn[0]);
            expectedRest.Remove(drawn[1]);
            CollectionAssert.AreEqual(expectedRest, rest, "Les autres gardent leur ordre derrière les tirés au sort.");
            seen.Add(drawn[0]);
            seen.Add(drawn[1]);
        }
        Assert.AreEqual(order.Length, seen.Count, "Chacun peut être tiré, l'hôte compris.");
        CollectionAssert.AreEqual(new[] { "x" }, DuelRoster.DrawDuelists(new[] { "x" }, 2, new Random(1)), "Seul : il reste.");
    }

    [Test]
    public void LaDemandeDeConnexion_PorteLIdentifiantDuJoueur_Nettoye()
    {
        byte[] payload = SessionFlow.BuildPayload("1.2.3", "Untel", WeaponLooks.Default, WeaponAttachments.None, 2, ChassisId.CT01,
                                                  "AbC_12-xy\nz;!");
        Assert.IsTrue(SessionFlow.ParsePayload(payload, out string version, out string name, out _, out _, out int paint, out _, out string id));
        Assert.AreEqual("1.2.3", version);
        Assert.AreEqual("Untel", name);
        Assert.AreEqual(2, paint);
        Assert.AreEqual("AbC_12-xyz", id, "Lettres, chiffres, « _ » et « - » seulement.");

        byte[] old = SessionFlow.BuildPayload("1.2.3", "Untel", WeaponLooks.Default, WeaponAttachments.None, 2, ChassisId.CT01);
        Assert.IsTrue(SessionFlow.ParsePayload(old, out _, out _, out _, out _, out _, out _, out string none));
        Assert.AreEqual("", none, "Sans identifiant (réseau local) : rien.");
        Assert.AreEqual(64, SessionFlow.SanitizePlayerId(new string('a', 200)).Length, "64 caractères au plus.");
    }
}
