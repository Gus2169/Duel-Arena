using NUnit.Framework;

/// <summary>
/// Tests des tables de correspondance son ↔ état du joueur.
///
/// Ces mappings sont décidés par le SERVEUR et diffusés à tous : une erreur ici ne se voit pas à
/// la compilation et s'entend à peine — on jouerait juste le mauvais bruit. Or le GDD fait du son
/// une information de gameplay, pas un habillage : entendre « il s'accroupit » quand l'adversaire
/// se relève, c'est une information fausse.
/// </summary>
public class PlayerSoundMappingTests
{
    [Test]
    public void EntrerEnPostureBasse_JoueLeSonDescendant()
    {
        Assert.AreEqual(PlayerSoundEvent.CrouchDown,
            PlayerLocomotion.GetStanceSound(PlayerLocomotion.Stance.Standing, PlayerLocomotion.Stance.Crouching));

        Assert.AreEqual(PlayerSoundEvent.ProneDown,
            PlayerLocomotion.GetStanceSound(PlayerLocomotion.Stance.Standing, PlayerLocomotion.Stance.Prone));
    }

    [Test]
    public void SeRelever_JoueLeSonMontantDeLaPostureQuittee()
    {
        Assert.AreEqual(PlayerSoundEvent.CrouchUp,
            PlayerLocomotion.GetStanceSound(PlayerLocomotion.Stance.Crouching, PlayerLocomotion.Stance.Standing));

        Assert.AreEqual(PlayerSoundEvent.ProneUp,
            PlayerLocomotion.GetStanceSound(PlayerLocomotion.Stance.Prone, PlayerLocomotion.Stance.Standing));
    }

    [Test]
    public void TransitionDirecteProneVersAccroupi_JoueLeSonDAccroupissement()
    {
        // Transition directe autorisée par le contrôleur : elle ne doit pas passer à travers les
        // mailles du filet et rester silencieuse.
        Assert.AreEqual(PlayerSoundEvent.CrouchDown,
            PlayerLocomotion.GetStanceSound(PlayerLocomotion.Stance.Prone, PlayerLocomotion.Stance.Crouching));
    }

    [Test]
    public void PostureInchangee_AucunSon()
    {
        Assert.IsNull(PlayerLocomotion.GetStanceSound(PlayerLocomotion.Stance.Standing, PlayerLocomotion.Stance.Standing));
        Assert.IsNull(PlayerLocomotion.GetStanceSound(PlayerLocomotion.Stance.Prone, PlayerLocomotion.Stance.Prone));
    }

    [Test]
    public void PasDeCourse_DistinctsDesPasDeMarche()
    {
        // Le GDD fait du triangle vitesse/bruit/discrétion un vrai choix tactique : courir doit
        // s'entendre différemment de marcher, sinon le compromis disparaît.
        Assert.AreEqual(PlayerSoundEvent.FootstepRun,
            PlayerLocomotion.GetFootstepSoundEvent(PlayerLocomotion.Stance.Standing, sprinting: true));

        Assert.AreEqual(PlayerSoundEvent.FootstepWalk,
            PlayerLocomotion.GetFootstepSoundEvent(PlayerLocomotion.Stance.Standing, sprinting: false));
    }

    [Test]
    public void AccroupiEtProne_OntLeursPropresSons()
    {
        Assert.AreEqual(PlayerSoundEvent.FootstepCrouch,
            PlayerLocomotion.GetFootstepSoundEvent(PlayerLocomotion.Stance.Crouching, sprinting: false));

        Assert.AreEqual(PlayerSoundEvent.Crawl,
            PlayerLocomotion.GetFootstepSoundEvent(PlayerLocomotion.Stance.Prone, sprinting: false));
    }

    [Test]
    public void SprintEnPostureBasse_NeDonneJamaisLeSonDeCourse()
    {
        // On ne sprinte pas accroupi ni allongé : même si l'indicateur de sprint traîne une frame,
        // le son doit rester celui de la posture.
        Assert.AreNotEqual(PlayerSoundEvent.FootstepRun,
            PlayerLocomotion.GetFootstepSoundEvent(PlayerLocomotion.Stance.Crouching, sprinting: true));

        Assert.AreNotEqual(PlayerSoundEvent.FootstepRun,
            PlayerLocomotion.GetFootstepSoundEvent(PlayerLocomotion.Stance.Prone, sprinting: true));
    }
}
