using NUnit.Framework;

/// <summary>
/// Le garde-fou de cadence du serveur (2026-10-08, WeaponController.TryAcceptShotRate).
///
/// Deux échecs possibles, aussi silencieux l'un que l'autre : refuser un tir légitime arrivé un
/// peu tard (le tireur perd des dégâts qu'il a vus partir ; mesuré environ un sur cent avec
/// l'ancienne règle), ou laisser un client modifié tirer plus vite que l'arme (le TTK baisse).
/// </summary>
public class FireRateTests
{
    private const float Interval = 1f / 7f; // MP5

    private static int CountAccepted(System.Func<int, double> arrivalOfShot, int shots)
    {
        double credit = 0.0;
        double lastCheck = double.NegativeInfinity;
        int accepted = 0;
        for (int k = 0; k < shots; k++)
        {
            if (WeaponController.TryAcceptShotRate(ref credit, ref lastCheck, arrivalOfShot(k), Interval)) accepted++;
        }
        return accepted;
    }

    [Test]
    public void TirsLegitimes_AvecDeLaGigue_AucunNEstRefuse()
    {
        // Chaque tir part à la cadence exacte et arrive avec un retard variable entre -30 et
        // +60 ms. Les arrivées restent dans l'ordre, comme des RPC fiables.
        const int shots = 300;
        int accepted = CountAccepted(k => 10.0 + k * Interval + 0.015 + 0.045 * System.Math.Sin(k * 1.7), shots);
        Assert.AreEqual(shots, accepted, $"{shots - accepted} tirs légitimes refusés pour de la gigue.");
    }

    [Test]
    public void UnTirRetardeDe60ms_LeSuivantPasseQuandMeme()
    {
        // Le tir n° 5 a pris 60 ms de retard (à-coup du serveur) : le n° 6 arrive 83 ms après lui.
        int accepted = CountAccepted(k => 10.0 + k * Interval + (k == 5 ? 0.06 : 0.0), 10);
        Assert.AreEqual(10, accepted);
    }

    [Test]
    public void ClientQuiInonde_LaCadenceMoyenneResteCelleDeLArme()
    {
        // Un tir demandé à chaque image, à 120 images/s pendant 10 s.
        int accepted = CountAccepted(k => 10.0 + k / 120.0, 1200);
        Assert.LessOrEqual(accepted, 70 + 1, "Plus de tirs acceptés que l'arme ne peut en tirer en 10 s.");
        Assert.GreaterOrEqual(accepted, 70 - 1, "Garde anti-vacuité : la cadence de l'arme doit rester atteignable.");
    }

    [Test]
    public void ClientQuiTire13PourcentTropVite_ResteBorneALaCadenceDeLArme()
    {
        // Juste au-dessus de l'ancienne tolérance par tir (85 % de l'intervalle), qui laissait passer
        // ce rythme en continu : 80 tirs en 10 s au lieu de 70.
        int accepted = CountAccepted(k => 10.0 + k * Interval * 0.87, 80);
        Assert.LessOrEqual(accepted, 70 + 1, $"{accepted} tirs acceptés en 10 s pour une arme à 7 coups par seconde.");
    }
    /// <summary>Mesuré à deux instances le 2026-10-11 : le premier tir du sniper, parti dès la fin
    /// de sa sortie (0,8 s) après une rafale de fusil d'assaut, était refusé, le serveur gardant le
    /// crédit du fusil. Changer d'arme remet le crédit à plein, comme le joueur son délai.</summary>
    [Test]
    public void ChangerDArme_LePremierTirDeLaNouvelle_NAttendQueSaSortie()
    {
        const float rifle = 1f / 7f, sniper = 1.25f, sniperRaise = 0.8f;
        double credit = 0.0, lastCheck = double.NegativeInfinity, t = 10.0;
        for (int k = 0; k < 10; k++, t += rifle)
            Assert.IsTrue(WeaponController.TryAcceptShotRate(ref credit, ref lastCheck, t, rifle), "Garde anti-vacuité : la rafale de fusil passe.");
        double firstSniperShot = t - rifle + sniperRaise;

        double keptCredit = credit, keptCheck = lastCheck;
        Assert.IsFalse(WeaponController.TryAcceptShotRate(ref keptCredit, ref keptCheck, firstSniperShot, sniper),
                       "Garde anti-vacuité : avec le crédit du fusil, le premier tir du sniper est refusé (le défaut mesuré).");

        WeaponController.ResetShotRateOnSwitch(ref lastCheck);
        Assert.IsTrue(WeaponController.TryAcceptShotRate(ref credit, ref lastCheck, firstSniperShot, sniper),
                      "Le premier tir du sniper, une fois sorti, est refusé.");
        double tooSoonCredit = credit, tooSoonCheck = lastCheck;
        Assert.IsFalse(WeaponController.TryAcceptShotRate(ref tooSoonCredit, ref tooSoonCheck, firstSniperShot + 0.3, sniper),
                       "Après la remise à plein, la cadence du sniper doit reprendre.");
        Assert.IsTrue(WeaponController.TryAcceptShotRate(ref credit, ref lastCheck, firstSniperShot + sniper, sniper));
    }
}
