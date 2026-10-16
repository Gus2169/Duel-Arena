using NUnit.Framework;
using ReloadSound = WeaponAudio.ReloadSound;

/// <summary>
/// Les sons de l'arme (2026-10-13) : ce que le chargeur fait entendre, et le début d'une rafale.
///
/// Le son est une information de jeu (GDD § 8) : un rechargement interrompu qui continuerait de
/// s'entendre dirait à l'adversaire qu'on ne peut pas tirer, alors qu'on le peut — c'était le cas
/// avant, un seul clip partant à l'appui sans jamais se couper.
/// </summary>
public class WeaponAudioTests
{
    private const int Fusil = 30;
    private const int Pompe = 6;

    private static WeaponController.MagazineState Chargeur(byte balles, double finRechargement = 0.0, byte arme = 0)
    {
        var m = new WeaponController.MagazineState { equipped = arme, reloadEndsAt = finRechargement };
        m.ammo = balles;
        return m;
    }

    [Test]
    public void RechargementCommence_SEntend()
    {
        Assert.AreEqual(ReloadSound.Started, WeaponAudio.ClassifyReload(Chargeur(5), Chargeur(5, 12.0), Fusil, false));
    }

    [Test]
    public void RechargementMeneASonTerme_NeSeCoupePas()
    {
        Assert.AreEqual(ReloadSound.Completed, WeaponAudio.ClassifyReload(Chargeur(5, 12.0), Chargeur(30), Fusil, false));
    }

    [Test]
    public void RechargementInterrompu_SeTait()
    {
        // Sprint, taser, refus du serveur : le rechargement s'arrête sans chargeur plein.
        Assert.AreEqual(ReloadSound.Interrupted, WeaponAudio.ClassifyReload(Chargeur(5, 12.0), Chargeur(5), Fusil, false));
    }

    [Test]
    public void ChangerDArmeEnRechargeant_LeCoupe()
    {
        // Du pompe qui recharge (3 cartouches) au fusil plein : ce n'est ni une cartouche glissée ni
        // un rechargement mené à son terme, c'est un rechargement interrompu.
        Assert.AreEqual(ReloadSound.Interrupted,
            WeaponAudio.ClassifyReload(Chargeur(3, 12.0, arme: 2), Chargeur(30, 0.0, arme: 0), Pompe, true));
    }

    [Test]
    public void ChangerDArmeSansRecharger_NeFaitRien()
    {
        // Les balles de l'autre arme ne sont pas une cartouche glissée.
        Assert.AreEqual(ReloadSound.None,
            WeaponAudio.ClassifyReload(Chargeur(30, 0.0, arme: 0), Chargeur(6, 0.0, arme: 2), Fusil, true));
    }

    [Test]
    public void Tirer_NeFaitRienEntendreDuChargeur()
    {
        Assert.AreEqual(ReloadSound.None, WeaponAudio.ClassifyReload(Chargeur(30), Chargeur(29), Fusil, false));
    }

    [Test]
    public void Pompe_ChaqueCartoucheGlisseeSEntend()
    {
        // La cartouche entre et l'échéance de la suivante avance : une cartouche, pas un nouveau rechargement.
        Assert.AreEqual(ReloadSound.RoundIn, WeaponAudio.ClassifyReload(Chargeur(3, 12.0), Chargeur(4, 12.55), Pompe, true));
    }

    [Test]
    public void Pompe_LaDerniereCartoucheTermineLeRechargement()
    {
        Assert.AreEqual(ReloadSound.RoundIn | ReloadSound.Completed,
            WeaponAudio.ClassifyReload(Chargeur(5, 12.0), Chargeur(6), Pompe, true));
    }

    [Test]
    public void Pompe_TirerInterromptSansGlisserDeCartouche()
    {
        Assert.AreEqual(ReloadSound.Interrupted, WeaponAudio.ClassifyReload(Chargeur(3, 12.0), Chargeur(2), Pompe, true));
    }

    [Test]
    public void ChargeurComplet_NEstPasUneCartoucheGlissee()
    {
        // Hors pompe, la hausse des balles à la fin n'est que le chargeur complet.
        ReloadSound sound = WeaponAudio.ClassifyReload(Chargeur(5, 12.0), Chargeur(30), Fusil, false);
        Assert.AreEqual(ReloadSound.None, sound & ReloadSound.RoundIn);
    }

    [Test]
    public void RelanceEntreDeuxMisesAJour_RedemarreLeRechargement()
    {
        // Annulé puis relancé entre deux publications du serveur : l'ancien se tait, le nouveau commence.
        Assert.AreEqual(ReloadSound.Interrupted | ReloadSound.Started,
            WeaponAudio.ClassifyReload(Chargeur(5, 12.0), Chargeur(5, 13.0), Fusil, false));
    }

    [Test]
    public void Rafale_LePremierCoupSeDistingue()
    {
        // Fusil d'assaut, 7 coups/s : un coup toutes les 0,143 s continue la rafale.
        Assert.IsFalse(WeaponAudio.StartsBurst(1f / 7f, 7f));
        Assert.IsFalse(WeaponAudio.StartsBurst(0.2f, 7f), "un coup un peu en retard (gigue du réseau) continue la rafale");
        Assert.IsTrue(WeaponAudio.StartsBurst(0.3f, 7f));
        Assert.IsTrue(WeaponAudio.StartsBurst(float.PositiveInfinity, 7f), "le tout premier coup");
        Assert.IsTrue(WeaponAudio.StartsBurst(0.01f, 0f), "une arme sans cadence");
    }
}
