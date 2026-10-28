using NUnit.Framework;
using UnityEngine;
using Run = ChargeTracker.Run;

/// <summary>
/// Le tir chargé du FX-2 (2026-10-26, GDD § 6) : on tient la gâchette pour charger, on la lâche pour
/// tirer ; chargé à fond, il tue au buste, relâché tôt, il blesse moins, sans aucun hasard. La charge se
/// compte sur les inputs, chez le tireur sur ceux qu'il envoie et sur le serveur sur ceux qu'il reçoit,
/// comme l'épaulement : le client n'annonce jamais sa charge (ChargeTracker).
///
/// Ce qui casse ici ne lève aucune erreur : un tir plein qui ne fait que 99,99 dégâts (le robot survit),
/// une charge qui ne compte que le dernier input, une charge qui ne repart pas de zéro à l'appui suivant,
/// un tir qui ne l'emporte pas (un client modifié tiendrait la gâchette et tirerait des coups pleins à la
/// cadence de l'arme), une demande de tir retardée jugée sur la charge suivante, ou une charge mise de
/// côté pour plus tard.
/// </summary>
public class WeaponChargeTests
{
    private const string FX2Path = "Assets/_ProjectArena/Weapon/Arme_FX2.asset";

    private static WeaponData FX2() => UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>(FX2Path);

    [Test]
    public void LaCharge_MeneLesDegats_DeTrenteACent_EtUnTirPleinTue()
    {
        WeaponData fx2 = FX2();
        Assert.AreEqual(30f, fx2.DamageAt(HitboxZoneType.Torso, 50f, 0f), 1e-4f, "Sans charge : 30.");
        Assert.AreEqual(65f, fx2.DamageAt(HitboxZoneType.Torso, 50f, 0.5f), 1e-4f, "À mi-charge : à mi-chemin.");
        Assert.AreEqual(100f, fx2.DamageAt(HitboxZoneType.Torso, 50f, 1f), "Pleine : exactement 100, le robot tombe.");

        // La charge vient du temps tenu : pleine en chargeTime, et elle se garde au-delà.
        Assert.AreEqual(0.5f, fx2.ChargeFraction(0.5f * fx2.chargeTime), 1e-6f);
        Assert.AreEqual(1f, fx2.ChargeFraction(3f * fx2.chargeTime), "Tenue plus longtemps, elle reste pleine.");

        // À la tête (×1,5), chargé à un peu plus de la moitié.
        Assert.Less(fx2.DamageAt(HitboxZoneType.Head, 50f, 0.5f), 100f);
        Assert.GreaterOrEqual(fx2.DamageAt(HitboxZoneType.Head, 50f, 0.53f), 100f);

        // Une arme qui ne se charge pas ignore la charge.
        var plain = ScriptableObject.CreateInstance<WeaponData>();
        try
        {
            plain.damagePerHit = 17f;
            Assert.AreEqual(17f, plain.DamageAt(HitboxZoneType.Torso, 10f, 0f), "Le FA-07 n'a pas de charge.");
            Assert.AreEqual(1f, plain.ChargeFraction(0f));
        }
        finally
        {
            Object.DestroyImmediate(plain);
        }
    }

    [Test]
    public void LaCharge_SeCompteSurLesInputsTenus_EtSeGardeQuandOnLache()
    {
        var tracker = new ChargeTracker();
        int sequence = 0;
        double clock = 0.0;
        void Input(float dt, bool charging) => tracker.Record(sequence++, clock, clock += dt, charging);

        Input(0.02f, false);
        Assert.AreEqual(0f, tracker.Seconds(), "Rien de tenu, rien de chargé.");

        // 0,3 s tenue, en inputs de durées diverses : toute leur durée compte, le premier compris.
        float held = 0f;
        for (int i = 0; i < 30; i++)
        {
            float dt = 0.005f + 0.001f * (i % 7);
            held += dt;
            Input(dt, true);
        }
        Assert.IsTrue(tracker.Current.active);
        Assert.AreEqual(held, tracker.Seconds(), 1e-5f, "La somme des inputs tenus.");
        Assert.AreEqual(held / 0.6f, tracker.Displayed(0.6f), 1e-5f, "Montrée en part du temps de charge.");

        // Lâchée : la charge reste telle quelle, pour le tir qui la lâche ; plus rien à montrer.
        for (int i = 0; i < 5; i++) Input(0.01f, false);
        Assert.IsFalse(tracker.Current.active);
        Assert.AreEqual(held, tracker.Seconds(), 1e-5f, "Elle ne fond pas après la lâche.");
        Assert.AreEqual(0f, tracker.Displayed(0.6f));

        // L'appui suivant repart de zéro.
        Input(0.01f, true);
        Assert.AreEqual(0.01f, tracker.Seconds(), 1e-6f, "Une nouvelle charge.");
    }

    [Test]
    public void UnTirEmporteSaCharge_TenirLaGachetteNeRechargePasUnTirPlein()
    {
        var tracker = new ChargeTracker();
        int sequence = 0;
        double clock = 0.0;
        void Input(float dt, bool charging) => tracker.Record(sequence++, clock, clock += dt, charging);

        for (int i = 0; i < 300; i++) Input(0.005f, true); // 1,5 s
        Assert.AreEqual(1.5f, tracker.SecondsFor(sequence - 1, sequence - 1), 1e-4f);

        // Un client modifié tire sans lâcher : le tir emporte la charge.
        tracker.Consume(sequence - 1, sequence - 1);
        Assert.AreEqual(0f, tracker.SecondsFor(sequence - 1, sequence - 1), "Plus rien à tirer.");
        for (int i = 0; i < 40; i++) Input(0.005f, true);   // 0,2 s de plus, toujours tenue
        Assert.AreEqual(0.2f, tracker.SecondsFor(sequence - 1, sequence - 1), 1e-4f, "Seulement ce qui a été tenu depuis le tir.");

        // Le joueur honnête lâche pour tirer : son tir n'entame pas sa charge suivante.
        Input(0.01f, false);
        int release = sequence - 1;
        tracker.Consume(release, release);
        for (int i = 0; i < 20; i++) Input(0.01f, true);
        Assert.AreEqual(0.2f, tracker.SecondsFor(sequence - 1, sequence - 1), 1e-4f, "Une charge entière après la lâche.");

        // Chez le tireur, de même, sur son dernier input.
        tracker.ConsumeLatest();
        Assert.AreEqual(0f, tracker.Seconds());
    }

    [Test]
    public void LeServeur_JugeLeTirSurLaChargeAnnoncee_MemeArriveeEnRetard()
    {
        // Le tireur tire à chaque lâche ; le serveur reçoit ses inputs par paquets, et sa demande de tir
        // (fiable) parfois après quelques inputs plus récents : la borne la juge alors sur le dernier reçu
        // (PlayerLocomotion.ReferenceInput), et le joueur a pu recommencer à charger entre-temps.
        var rng = new System.Random(11);
        var shooter = new ChargeTracker();
        var server = new ChargeTracker();
        const int count = 4000;
        var dts = new float[count];
        var held = new bool[count];
        bool pressing = false;
        for (int i = 0, left = 0; i < count; i++)
        {
            if (left-- <= 0)
            {
                pressing = !pressing;
                left = pressing ? 20 + rng.Next(220) : 2 + rng.Next(12);   // des relances rapides
            }
            dts[i] = 0.003f + (float)rng.NextDouble() * 0.009f;
            held[i] = pressing;
        }

        var shots = new System.Collections.Generic.List<(int input, float charge)>();
        double shooterClock = 0.0;
        for (int i = 0; i < count; i++)
        {
            shooter.Record(i, shooterClock, shooterClock += dts[i], held[i]);
            if (i > 0 && held[i - 1] && !held[i])
            {
                shots.Add((i, shooter.Seconds()));   // la lâche tire, sur l'input qu'elle vient d'envoyer
                shooter.ConsumeLatest();
            }
        }
        Assert.Greater(shots.Count, 20, "Garde : des tirs.");

        double serverClock = 0.0;
        int received = -1, shot = 0, judgedAfterNewCharge = 0;
        while (shot < shots.Count)
        {
            // La demande arrive après un retard de 0 à 15 inputs.
            int arrivesAfter = Mathf.Min(count - 1, shots[shot].input + rng.Next(16));
            while (received < arrivesAfter)
            {
                received++;
                server.Record(received, serverClock, serverClock += dts[received], held[received]);
            }
            int reference = PlayerLocomotion.ReferenceInput(shots[shot].input, received);
            if (server.Current.active) judgedAfterNewCharge++;
            Assert.AreEqual(shots[shot].charge, server.SecondsFor(shots[shot].input, reference), 1e-6f, $"Tir {shot}.");
            server.Consume(shots[shot].input, reference);
            shot++;
        }
        Assert.Greater(judgedAfterNewCharge, 3, "Garde : des demandes jugées alors que la charge suivante avait commencé.");
    }

    [Test]
    public void UneChargeLacheeSansTirer_NeSeMetPasDeCote()
    {
        var tracker = new ChargeTracker();
        int sequence = 0;
        double clock = 0.0;
        void Input(float dt, bool charging) => tracker.Record(sequence++, clock, clock += dt, charging);

        for (int i = 0; i < 100; i++) Input(0.01f, true);   // 1 s
        Input(0.01f, false);
        int release = sequence - 1;
        for (int i = 0; i < 30; i++) Input(0.01f, false);    // 0,3 s plus tard : une demande retardée
        Assert.AreEqual(1f, tracker.SecondsFor(release, sequence - 1), 1e-4f, "Une demande en retard tire sa charge.");
        for (int i = 0; i < 30; i++) Input(0.01f, false);    // 0,6 s : une charge gardée de côté
        Assert.AreEqual(0f, tracker.SecondsFor(release, sequence - 1), "Trop vieille, elle ne compte plus.");
    }
}
