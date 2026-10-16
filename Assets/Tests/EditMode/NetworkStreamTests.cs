using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Les flux continus en non fiable (2026-10-13) : inputs du joueur, poses des autres, demandes qui
/// annoncent leur input de référence.
///
/// Mesuré à deux instances avec 2 % de paquets perdus, tout passant en fiable ordonné : un paquet
/// perdu retenait tous les messages suivants jusqu'à son renvoi. L'hôte voyait le robot du client
/// figé sur 21 à 33 % de ses images, puis bondir de 50 cm ; le client voyait l'hôte se figer de même.
/// </summary>
public class NetworkStreamTests
{
    // ------------------------------------------------------------------
    // Les demandes annoncent leur input de référence
    // ------------------------------------------------------------------

    [Test]
    public void UneDemande_EstJugeeSurLInputQuElleAnnonce_MemeSIlNEstPasEncoreArrive()
    {
        // Les inputs voyagent en non fiable : la demande (fiable) peut arriver avant eux.
        Assert.AreEqual(105, PlayerLocomotion.ReferenceInput(105, 100));
        Assert.AreEqual(100, PlayerLocomotion.ReferenceInput(100, 100));
    }

    [Test]
    public void UnClientModifie_NeFaitPasJugerSaDemandeSurUnEtatPasse()
    {
        // Annoncer un input d'avant son sprint ou son franchissement, déjà reçu par le serveur : refusé,
        // c'est le dernier reçu qui compte, aussi strict qu'avec la livraison ordonnée.
        Assert.AreEqual(100, PlayerLocomotion.ReferenceInput(40, 100));
        Assert.AreEqual(100, PlayerLocomotion.ReferenceInput(-5, 100));
        Assert.AreEqual(100, PlayerLocomotion.ReferenceInput(int.MinValue, 100));
    }

    [Test]
    public void UnInputAnnonceTropLoinDevant_EstBorne()
    {
        int lead = PlayerLocomotion.MaxReferenceLead;
        Assert.AreEqual(100 + lead, PlayerLocomotion.ReferenceInput(int.MaxValue, 100));
        Assert.AreEqual(100 + lead, PlayerLocomotion.ReferenceInput(100 + lead + 1, 100));
        Assert.AreEqual(-1 + 7, PlayerLocomotion.ReferenceInput(6, -1), "Avant le premier input reçu.");
    }

    // ------------------------------------------------------------------
    // L'envoi : redondance, jamais un input déjà confirmé
    // ------------------------------------------------------------------

    [Test]
    public void UnEnvoi_RepeteLesEnvoisPrecedentsNonConfirmes()
    {
        // Les envois précédents gardés en redondance commencent à 90 ; le serveur a confirmé jusqu'à 80.
        Assert.AreEqual(90, PlayerLocomotion.FirstInputToSend(100, 80, 90, 40));
    }

    [Test]
    public void UnEnvoi_NeRepetePasCeQueLeServeurAConfirme()
    {
        Assert.AreEqual(96, PlayerLocomotion.FirstInputToSend(100, 95, 90, 40));
        // Tout est confirmé : l'envoi ne contient que le plus récent.
        Assert.AreEqual(100, PlayerLocomotion.FirstInputToSend(100, 99, 90, 40));
    }

    [Test]
    public void UnEnvoi_NeDepassePasLaTailleDUnPaquet()
    {
        // 500 images/s pendant une coupure : la redondance remonterait trop loin.
        Assert.AreEqual(61, PlayerLocomotion.FirstInputToSend(100, -1, 0, 40));
        Assert.AreEqual(40, 100 - PlayerLocomotion.FirstInputToSend(100, -1, 0, 40) + 1);
    }

    // ------------------------------------------------------------------
    // La réception : ce qui est neuf, ce qui est perdu
    // ------------------------------------------------------------------

    private static PlayerLocomotion.InputWire Wire(float dt) => new PlayerLocomotion.InputWire { deltaTime = dt };

    /// <summary>Tout ce que la file rend maintenant, dans l'ordre : (numéro, perdus juste avant).</summary>
    private static List<(int sequence, int lost)> Drain(PlayerLocomotion.InputReorderBuffer buffer, double now)
    {
        var taken = new List<(int, int)>();
        while (buffer.TryTake(now, PlayerLocomotion.InputReorderWait, out int sequence, out _, out int lost)) taken.Add((sequence, lost));
        return taken;
    }

    [Test]
    public void DesInputsDansLOrdre_PassentAussitot()
    {
        var buffer = new PlayerLocomotion.InputReorderBuffer();
        buffer.Store(0, Wire(0.01f));
        buffer.Store(1, Wire(0.02f));
        CollectionAssert.AreEqual(new[] { (0, 0), (1, 0) }, Drain(buffer, 0.0));
        Assert.IsTrue(buffer.Store(2, Wire(0.03f)));
        Assert.IsTrue(buffer.TryTake(0.0, PlayerLocomotion.InputReorderWait, out int sequence, out PlayerLocomotion.InputWire wire, out _));
        Assert.AreEqual(2, sequence);
        Assert.AreEqual(0.03f, wire.deltaTime);
    }

    [Test]
    public void UnInputRepete_NEstRendQuUneFois()
    {
        // La redondance : chaque envoi répète les précédents.
        var buffer = new PlayerLocomotion.InputReorderBuffer();
        Assert.IsTrue(buffer.Store(0, Wire(0.01f)));
        Assert.IsFalse(buffer.Store(0, Wire(0.01f)), "Encore en attente.");
        Drain(buffer, 0.0);
        Assert.IsFalse(buffer.Store(0, Wire(0.01f)), "Déjà passé.");
        CollectionAssert.IsEmpty(Drain(buffer, 0.0));
    }

    [Test]
    public void UnPaquetDouble_AttendLesInputsQuIlPrecede()
    {
        // Le paquet portant 2..3 arrive avant celui portant 0..1 : rien n'est sauté.
        var buffer = new PlayerLocomotion.InputReorderBuffer();
        buffer.Store(2, Wire(0.01f));
        buffer.Store(3, Wire(0.01f));
        CollectionAssert.IsEmpty(Drain(buffer, 1.00), "0 et 1 manquent : on les attend.");
        CollectionAssert.IsEmpty(Drain(buffer, 1.04));
        buffer.Store(0, Wire(0.01f));
        buffer.Store(1, Wire(0.01f));
        CollectionAssert.AreEqual(new[] { (0, 0), (1, 0), (2, 0), (3, 0) }, Drain(buffer, 1.045));
    }

    [Test]
    public void DesInputsDontTousLesEnvoisSePerdent_SontSautesApresLAttente()
    {
        var buffer = new PlayerLocomotion.InputReorderBuffer();
        buffer.Store(0, Wire(0.01f));
        Drain(buffer, 0.0);
        buffer.Store(3, Wire(0.01f)); // 1 et 2 ne viendront jamais
        CollectionAssert.IsEmpty(Drain(buffer, 2.00));
        CollectionAssert.IsEmpty(Drain(buffer, 2.00 + PlayerLocomotion.InputReorderWait * 0.9));
        CollectionAssert.AreEqual(new[] { (3, 2) }, Drain(buffer, 2.00 + PlayerLocomotion.InputReorderWait * 1.1));
        Assert.IsFalse(buffer.Store(1, Wire(0.01f)), "Arrivé trop tard : déjà sauté.");
    }

    [Test]
    public void UnJoueurQuiNEnvoieRien_NeFaitRienSauter()
    {
        // Rien de plus récent n'attend : il ne manque rien, et l'attente ne court pas.
        var buffer = new PlayerLocomotion.InputReorderBuffer();
        CollectionAssert.IsEmpty(Drain(buffer, 0.0));
        CollectionAssert.IsEmpty(Drain(buffer, 10.0));
        buffer.Store(0, Wire(0.01f));
        CollectionAssert.AreEqual(new[] { (0, 0) }, Drain(buffer, 10.0));
    }

    [Test]
    public void ApresUneLongueCoupure_LaFileRepartDuPremierInputRecu()
    {
        // 5 s de coupure à 300 images/s : le client n'envoie plus que ses derniers inputs.
        var buffer = new PlayerLocomotion.InputReorderBuffer();
        buffer.Store(0, Wire(0.01f));
        Drain(buffer, 0.0);
        buffer.Store(1501, Wire(0.01f));
        buffer.Store(1502, Wire(0.01f));
        CollectionAssert.AreEqual(new[] { (1501, 1500), (1502, 0) }, Drain(buffer, 6.0));
    }

    [Test]
    public void UnInput_ArriveTelQuIlEstParti()
    {
        PlayerLocomotion.InputWire wire = PlayerLocomotion.TestPack(new Vector2(0.7071f, -0.3f), 12.5f, -33f, 1f / 144f,
            true, false, true, false, true, true, -1);
        bool[] buttons = PlayerLocomotion.TestUnpack(wire, out Vector2 move, out float lookX, out float aimPitch, out int lean);
        Assert.AreEqual(new Vector2(0.7071f, -0.3f), move);
        Assert.AreEqual(12.5f, lookX);
        Assert.AreEqual(-33f, aimPitch);
        Assert.AreEqual(1f / 144f, wire.deltaTime, "La durée voyage telle quelle : le serveur simule ce qui a été prédit.");
        CollectionAssert.AreEqual(new[] { true, false, true, false, true, true }, buttons);
        Assert.AreEqual(-1, lean);

        PlayerLocomotion.TestUnpack(PlayerLocomotion.TestPack(Vector2.zero, 0f, 0f, 0f, false, true, false, true, false, false, 1),
                                    out _, out _, out _, out lean);
        Assert.AreEqual(1, lean);
        bool[] none = PlayerLocomotion.TestUnpack(PlayerLocomotion.TestPack(Vector2.zero, 0f, 0f, 0f, false, false, false, false, false, false, 0),
                                                  out _, out _, out _, out lean);
        Assert.AreEqual(0, lean);
        CollectionAssert.AreEqual(new bool[6], none);
    }

    [Test]
    public void UnLeanHorsDesTroisEtats_EstRamenéAuMilieu()
    {
        // Les deux bits du lean valent 3 : seul un client modifié l'envoie.
        var wire = new PlayerLocomotion.InputWire { buttons = 0xC0 };
        PlayerLocomotion.TestUnpack(wire, out _, out _, out _, out int lean);
        Assert.AreEqual(0, lean);
    }

    [Test]
    public void UnPaquet_SeRelitTelQuIlAEteEcrit()
    {
        var inputs = new PlayerLocomotion.InputWire[3];
        for (int i = 0; i < inputs.Length; i++)
            inputs[i] = PlayerLocomotion.TestPack(new Vector2(i, -i), i * 2f, i * 3f, 0.01f * (i + 1), i == 1, false, false, true, false, false, i - 1);
        var packet = new PlayerLocomotion.InputPacket { firstSequence = 1234, count = 3, inputs = inputs };

        using var writer = new FastBufferWriter(1024, Allocator.Temp);
        writer.WriteNetworkSerializable(packet);
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out PlayerLocomotion.InputPacket read);

        Assert.AreEqual(1234, read.firstSequence);
        Assert.AreEqual(3, read.count);
        for (int i = 0; i < 3; i++)
        {
            Assert.AreEqual(inputs[i].move, read.inputs[i].move);
            Assert.AreEqual(inputs[i].lookX, read.inputs[i].lookX);
            Assert.AreEqual(inputs[i].aimPitch, read.inputs[i].aimPitch);
            Assert.AreEqual(inputs[i].deltaTime, read.inputs[i].deltaTime);
            Assert.AreEqual(inputs[i].buttons, read.inputs[i].buttons);
        }
        Assert.LessOrEqual(writer.Length, 5 + 3 * 21, "21 octets par input, plus l'en-tête.");
    }

    [Test]
    public void UnPaquetQuiAnnonceTropDInputs_EstIgnore()
    {
        using var writer = new FastBufferWriter(64, Allocator.Temp);
        writer.WriteValueSafe(7);                                         // premier numéro
        writer.WriteValueSafe((byte)(PlayerLocomotion.MaxInputsPerPacket + 1)); // nombre annoncé
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out PlayerLocomotion.InputPacket read);
        Assert.AreEqual(0, read.count);
    }

    // ------------------------------------------------------------------
    // Les poses des autres joueurs
    // ------------------------------------------------------------------

    private static PlayerLocomotion.DisplayPose Pose(double time, float x, byte teleport = 0)
        => new PlayerLocomotion.DisplayPose { time = time, position = new Vector3(x, 0f, 0f), teleportId = teleport };

    [Test]
    public void UnePoseArriveeDansLeDesordre_EstIgnoree()
    {
        var history = new List<PlayerLocomotion.DisplayPose>();
        double last = double.NegativeInfinity;
        byte teleport = 0;
        Assert.IsTrue(PlayerLocomotion.AddRemotePose(history, ref last, ref teleport, Pose(1.00, 0f), 0.5));
        Assert.IsTrue(PlayerLocomotion.AddRemotePose(history, ref last, ref teleport, Pose(1.066, 2f), 0.5));
        Assert.IsFalse(PlayerLocomotion.AddRemotePose(history, ref last, ref teleport, Pose(1.033, 1f), 0.5));
        Assert.IsFalse(PlayerLocomotion.AddRemotePose(history, ref last, ref teleport, Pose(1.066, 2f), 0.5), "Doublon.");
        Assert.AreEqual(2, history.Count);
        Assert.AreEqual(1.066, history[1].time);
    }

    [Test]
    public void UnePoseDAvantUnReplacement_ArriveeApres_NeRemetPasLAnciennePosition()
    {
        var history = new List<PlayerLocomotion.DisplayPose>();
        double last = double.NegativeInfinity;
        byte teleport = 0;
        PlayerLocomotion.AddRemotePose(history, ref last, ref teleport, Pose(1.00, 0f, 0), 0.5);
        PlayerLocomotion.AddRemotePose(history, ref last, ref teleport, Pose(1.10, 50f, 1), 0.5); // replacement
        Assert.AreEqual(1, history.Count, "Le replacement vide l'historique.");
        Assert.IsFalse(PlayerLocomotion.AddRemotePose(history, ref last, ref teleport, Pose(1.05, 1f, 0), 0.5));
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual(50f, history[0].position.x);
        Assert.AreEqual(1, teleport);
    }

    [Test]
    public void ApresUnLongTrou_LaPoseEstMontreeSansGlisser()
    {
        // La pose d'apparition, reçue en rejoignant une partie en cours, puis la première pose à jour,
        // moins d'une seconde plus tard (au-delà, l'historique d'une seconde l'oublierait de toute façon).
        var history = new List<PlayerLocomotion.DisplayPose>();
        double last = double.NegativeInfinity;
        byte teleport = 0;
        PlayerLocomotion.AddRemotePose(history, ref last, ref teleport, Pose(10.0, 0f), 0.5);
        PlayerLocomotion.AddRemotePose(history, ref last, ref teleport, Pose(10.8, 30f), 0.5);
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual(30f, history[0].position.x);

        // Un trou ordinaire (une pose perdue) s'interpole.
        PlayerLocomotion.AddRemotePose(history, ref last, ref teleport, Pose(10.866, 31f), 0.5);
        Assert.AreEqual(2, history.Count);
    }

    [Test]
    public void LHistorique_NeGardeQuUneSeconde()
    {
        var history = new List<PlayerLocomotion.DisplayPose>();
        double last = double.NegativeInfinity;
        byte teleport = 0;
        for (int i = 0; i <= 60; i++) PlayerLocomotion.AddRemotePose(history, ref last, ref teleport, Pose(i / 30.0, i), 0.5);
        Assert.AreEqual(31, history.Count);
        Assert.GreaterOrEqual(history[0].time, 1.0 - 1e-9);
    }
}
