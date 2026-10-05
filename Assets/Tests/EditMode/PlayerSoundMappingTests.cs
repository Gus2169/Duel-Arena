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

    // ------------------------------------------------------------------
    // Niveaux de bruit (2026-10-05). Le sneak et le ramper étaient classés Silent, que
    // PlayerSoundEmitter filtre : ils ne faisaient AUCUN bruit, alors que le GDD (§ 8) les veut
    // faibles mais audibles. Un test vert ici ne dit rien du volume réel, seulement qu'aucune
    // allure de déplacement n'est classée muette.
    // ------------------------------------------------------------------

    [Test]
    public void SneakEtRamper_SontFaibles_JamaisMuets()
    {
        Assert.AreEqual(PlayerLocomotion.NoiseLevel.Faint,
            PlayerLocomotion.GetNoiseLevel(moving: true, PlayerLocomotion.Stance.Standing, sprinting: false, sneaking: true));

        Assert.AreEqual(PlayerLocomotion.NoiseLevel.Faint,
            PlayerLocomotion.GetNoiseLevel(moving: true, PlayerLocomotion.Stance.Prone, sprinting: false, sneaking: false));
    }

    [Test]
    public void SeuleLImmobilite_EstSilencieuse()
    {
        Assert.AreEqual(PlayerLocomotion.NoiseLevel.Silent,
            PlayerLocomotion.GetNoiseLevel(moving: false, PlayerLocomotion.Stance.Standing, sprinting: false, sneaking: false));

        // Toutes les combinaisons EN MOUVEMENT doivent faire du bruit.
        foreach (PlayerLocomotion.Stance stance in System.Enum.GetValues(typeof(PlayerLocomotion.Stance)))
        {
            foreach (bool sprint in new[] { false, true })
            {
                foreach (bool sneak in new[] { false, true })
                {
                    Assert.AreNotEqual(PlayerLocomotion.NoiseLevel.Silent,
                        PlayerLocomotion.GetNoiseLevel(moving: true, stance, sprint, sneak),
                        $"Muet en mouvement : {stance}, sprint={sprint}, sneak={sneak}");
                }
            }
        }
    }

    [Test]
    public void LaCourse_EstPlusBruyanteQueLaMarche()
    {
        Assert.AreEqual(PlayerLocomotion.NoiseLevel.Loud,
            PlayerLocomotion.GetNoiseLevel(moving: true, PlayerLocomotion.Stance.Standing, sprinting: true, sneaking: false));

        Assert.AreEqual(PlayerLocomotion.NoiseLevel.Quiet,
            PlayerLocomotion.GetNoiseLevel(moving: true, PlayerLocomotion.Stance.Standing, sprinting: false, sneaking: false));
    }

    // ------------------------------------------------------------------
    // Sons du lean, décidés par le serveur depuis l'état reçu dans chaque input (2026-10-05).
    // ------------------------------------------------------------------

    [Test]
    public void SePencher_JoueLeSonDeDebut_SeRedresser_LeSonDeFin()
    {
        Assert.AreEqual(PlayerSoundEvent.LeanStart, PlayerLocomotion.GetLeanSound(0, -1));
        Assert.AreEqual(PlayerSoundEvent.LeanStart, PlayerLocomotion.GetLeanSound(0, 1));
        Assert.AreEqual(PlayerSoundEvent.LeanEnd, PlayerLocomotion.GetLeanSound(-1, 0));
        Assert.AreEqual(PlayerSoundEvent.LeanEnd, PlayerLocomotion.GetLeanSound(1, 0));
    }

    [Test]
    public void ChangerDeCote_SEntend()
    {
        // Passer directement de gauche à droite est un nouveau peek : il ne doit pas être muet.
        Assert.AreEqual(PlayerSoundEvent.LeanStart, PlayerLocomotion.GetLeanSound(-1, 1));
        Assert.AreEqual(PlayerSoundEvent.LeanStart, PlayerLocomotion.GetLeanSound(1, -1));
    }

    [Test]
    public void LeanInchange_AucunSon()
    {
        Assert.IsNull(PlayerLocomotion.GetLeanSound(0, 0));
        Assert.IsNull(PlayerLocomotion.GetLeanSound(1, 1));
    }
}
