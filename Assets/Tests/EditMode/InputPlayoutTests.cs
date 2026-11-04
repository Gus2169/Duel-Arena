using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

/// <summary>
/// Le tampon de lecture des inputs (la dette n° 30 : l'hôte voyait le robot d'un client par à-coups) et l'envoi de Netcode
/// avant le rendu. On rejoue ce que fait ApplyBufferedServerInputs : des inputs qui arrivent par paquets, à des instants
/// irréguliers, et une image qui en prend ce que son crédit permet.
/// </summary>
public class InputPlayoutTests
{
    /// <summary>Un client et un serveur à cadence fixe : ce qu'une partie donne image après image.</summary>
    private sealed class Run
    {
        private readonly InputPlayout playout = new InputPlayout();
        private readonly Queue<float> queue = new Queue<float>();
        private float queued;

        public int EmptyFrames, Frames, MaxPerFrame;
        public float Played, MaxHeld;

        public void Arrive(float dt, int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                queue.Enqueue(dt);
                queued += dt;
            }
        }

        public void Frame(float elapsed)
        {
            playout.Begin(elapsed, queued);
            int played = 0;
            while (queue.Count > 0)
            {
                float dt = queue.Peek();
                if (!playout.CanPlay(dt)) break;
                playout.Spend(dt);
                queue.Dequeue();
                queued = queue.Count > 0 ? Mathf.Max(0f, queued - dt) : 0f;
                Played += dt;
                played++;
            }
            playout.End(elapsed, queued);
            Frames++;
            if (played == 0) EmptyFrames++;
            MaxPerFrame = Mathf.Max(MaxPerFrame, played);
            MaxHeld = Mathf.Max(MaxHeld, queued);
        }

        public float Queued => queued;
    }

    [Test]
    public void DesInputsReguliers_PartentUnParImage_SansRetard()
    {
        var run = new Run();
        for (int i = 0; i < 600; i++)
        {
            run.Arrive(0.01f);
            run.Frame(0.01f);
        }
        Assert.AreEqual(0, run.EmptyFrames, "chaque image a son input");
        Assert.AreEqual(1, run.MaxPerFrame, "jamais deux d'un coup");
        Assert.Less(run.MaxHeld, 0.011f, "rien ne reste en file");
    }

    [Test]
    public void UnTrouDArrivee_NeFigeLeRobotQuUneFois_LaReserveCouvreLesSuivants()
    {
        // Tous les 40 cycles, un trou de 30 ms suivi de ce qu'il a retenu d'un seul bloc : ce qu'un client donne quand une
        // image longue retient ses paquets.
        var run = new Run();
        for (int cycle = 0; cycle < 6; cycle++)
        {
            for (int i = 0; i < 40; i++)
            {
                run.Arrive(0.01f);
                run.Frame(0.01f);
            }
            for (int i = 0; i < 3; i++) run.Frame(0.01f);
            run.Arrive(0.01f, 3);
        }
        Assert.Greater(run.Frames, 200, "la garde : le scénario a bien tourné");
        Assert.LessOrEqual(run.EmptyFrames, 3, "seul le premier trou se voit, la réserve couvre les suivants");
        Assert.LessOrEqual(run.MaxPerFrame, 2, "ce qui arrive d'un bloc ne part pas d'un bloc");
    }

    [Test]
    public void UnInput_NePartQueSIlTientEnEntier_DansLeCreditDeLImage()
    {
        var playout = new InputPlayout();
        playout.Begin(0.008f, 0.02f);
        Assert.IsFalse(playout.CanPlay(0.02f), "8 ms ne couvrent pas un input de 20 ms : il attend, sans être tronqué");
        playout.End(0.008f, 0.02f);
        playout.Begin(0.008f, 0.02f);
        playout.End(0.008f, 0.02f);
        playout.Begin(0.008f, 0.02f);
        Assert.IsTrue(playout.CanPlay(0.02f), "24 ms le couvrent");
    }

    [Test]
    public void LeCredit_NeSAmassePas_QuandRienNArrive()
    {
        var run = new Run();
        for (int i = 0; i < 200; i++) run.Frame(0.01f);
        run.Arrive(0.01f, 5);
        run.Frame(0.01f);
        Assert.LessOrEqual(run.MaxPerFrame, 2, "deux secondes sans input ne donnent pas le droit de tout jouer d'un coup");
    }

    [Test]
    public void UnClientPlusLent_NAttendPasUneFoisDePlusQueSonImage()
    {
        // Un client à 30 images/s sur un hôte à 120, sans retard : chaque input reste en file le temps de payer son image.
        var run = new Run();
        float clock = 0f, next = 0f;
        for (int i = 0; i < 480; i++)
        {
            if (clock >= next)
            {
                run.Arrive(1f / 30f);
                next += 1f / 30f;
            }
            run.Frame(1f / 120f);
            clock += 1f / 120f;
        }
        Assert.Less(run.MaxHeld, 2.1f / 30f, "pas plus de deux inputs en attente");
        Assert.Greater(run.Played, 3.8f, "les 4 s de jeu sont jouées");

        // Un paquet qui prend trois images de retard : ce que la file vide a gardé de crédit le couvre à son arrivée, il part
        // dans l'image, et ceux qui le suivent aussi. Sans ce crédit, chacun attendrait encore d'avoir payé toute son image.
        // Un client à 32 images/s sur un hôte à 128 : des durées exactes en binaire, aucun arrondi ne décale un input d'une image.
        const int period = 4, late = 3, lateFrom = 100;
        run = new Run();
        float held = 0f;
        for (int frame = 0; frame < 4 * 128; frame++)
        {
            if ((frame - (frame >= lateFrom ? late : 0)) % period == 0) run.Arrive(period / 128f);
            run.Frame(1f / 128f);
            if (frame >= lateFrom + late) held = Mathf.Max(held, run.Queued);
        }
        Assert.AreEqual(0f, held, "le crédit gardé couvre l'input à son arrivée : il ne reste pas en file une image de plus");
        Assert.Greater(run.Played, 3.9f, "la garde : les 4 s de jeu sont jouées");
    }

    [Test]
    public void UneReserveInutile_SeResorbe_EnDeuxFenetres()
    {
        var run = new Run();
        run.Arrive(0.01f, 5);   // un paquet de 50 ms d'un coup, puis plus jamais de trou
        for (int i = 0; i < 100 * (int)(2.5f * InputPlayout.Window); i++)
        {
            run.Arrive(0.01f);
            run.Frame(0.01f);
        }
        Assert.Less(run.Queued, 0.015f, "la réserve est revenue à ce qui sert");
    }

    [Test]
    public void AuDelaDeMaxHold_LaFileSeResorbeSansAttendre()
    {
        var run = new Run();
        run.Arrive(0.01f, 40);   // 400 ms retenues : un vrai à-coup réseau
        for (int i = 0; i < 40; i++)
        {
            run.Arrive(0.01f);
            run.Frame(0.01f);
        }
        Assert.Less(run.Queued, 0.1f, "en 0,4 s, l'essentiel du retard est repris");
        Assert.Less(run.MaxPerFrame, 15, "et sans tout jouer d'un bloc");
    }

    [Test]
    public void LenvoiDeNetcode_PasseAvantLeRendu()
    {
        var systems = new[]
        {
            new PlayerLoopSystem { type = typeof(PostLateUpdate.UpdateAllRenderers) },
            new PlayerLoopSystem { type = typeof(PostLateUpdate.FinishFrameRendering) },
            new PlayerLoopSystem { type = typeof(PostLateUpdate.PlayerSendFrameComplete) },
            new PlayerLoopSystem { type = typeof(NetworkPostLateUpdate) },
            new PlayerLoopSystem { type = typeof(PostLateUpdate.PresentAfterDraw) },
        };
        PlayerLoopSystem[] moved = NetworkSendBeforeRender.MoveBeforeRendering(systems);
        Assert.AreEqual(systems.Length, moved.Length, "rien ne se perd");
        Assert.AreEqual(typeof(NetworkPostLateUpdate), moved[1].type, "juste avant le rendu");
        Assert.AreEqual(typeof(PostLateUpdate.FinishFrameRendering), moved[2].type);
        Assert.AreEqual(typeof(PostLateUpdate.PresentAfterDraw), moved[4].type, "le reste garde son ordre");
        Assert.AreSame(moved, NetworkSendBeforeRender.MoveBeforeRendering(moved), "déjà à sa place, rien ne bouge");
    }

    /// <summary>Le système de Netcode est interne à son assembly : on le reconnaît à son nom, comme le fait le jeu.</summary>
    private struct NetworkPostLateUpdate { }
}
