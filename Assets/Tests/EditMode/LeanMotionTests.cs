using NUnit.Framework;

/// <summary>
/// Le mouvement du buste quand on se penche et qu'on se redresse (2026-10-06).
///
/// Le lean avançait à vitesse constante, 35 cm en 50 ms : il partait et s'arrêtait net, « brut et
/// sec » selon l'utilisateur. Ces tests gardent un départ en douceur, une arrivée franche et
/// l'absence de rebond — trois propriétés qu'on ne voit qu'en jouant, et qu'une retouche de
/// réglage peut défaire sans qu'aucune erreur ne s'affiche.
/// </summary>
public class LeanMotionTests
{
    private const float Dt = 1f / 60f;
    private const float SmoothTime = 0.06f; // valeur par défaut de PlayerLocomotion.leanSmoothTime

    private static float Simuler(float depart, float cible, float secondes, ref float vitesse)
    {
        float lean = depart;
        for (float t = 0f; t < secondes; t += Dt)
        {
            lean = PlayerLocomotion.StepLean(lean, cible, ref vitesse, SmoothTime, Dt);
        }
        return lean;
    }

    [Test]
    public void SePencher_DemarreEnDouceur()
    {
        // Sur la première image, le buste ne parcourt qu'une petite part du chemin. L'ancien
        // mouvement à vitesse constante en faisait déjà un tiers (7 m/s, 11,7 cm à 60 images/s).
        float vitesse = 0f;
        float lean = PlayerLocomotion.StepLean(0f, 0.35f, ref vitesse, SmoothTime, Dt);
        Assert.Less(lean, 0.35f * 0.2f, $"Après une image, le buste est déjà à {lean * 100f:F1} cm sur 35 : le départ est sec.");
    }

    [Test]
    public void SePencherPuisSeRedresser_ArriveSansTrainer()
    {
        // Garde l'autre bout : un lean trop mou serait aussi mauvais en duel qu'un lean sec.
        float vitesse = 0f;
        float penche = Simuler(0f, 0.35f, 0.3f, ref vitesse);
        Assert.AreEqual(0.35f, penche, 0.01f, "Au bout de 0,3 s, le buste doit être penché.");

        float redresse = Simuler(penche, 0f, 0.3f, ref vitesse);
        Assert.AreEqual(0f, redresse, 0.01f, "Au bout de 0,3 s, le buste doit être redressé.");
    }

    [Test]
    public void PasserDUnCoteALAutre_NeDepassePasLaCible()
    {
        // D'un lean à droite à un lean à gauche (plus court allongé) : le buste ne doit jamais
        // aller plus loin que sa cible, sinon la tête touchable sortirait plus que prévu.
        float vitesse = 0f;
        float lean = Simuler(0f, 0.35f, 0.5f, ref vitesse);
        const float cible = -0.094f;
        float plusLoin = 0f;
        for (float t = 0f; t < 0.6f; t += Dt)
        {
            lean = PlayerLocomotion.StepLean(lean, cible, ref vitesse, SmoothTime, Dt);
            if (lean < plusLoin) plusLoin = lean;
        }
        Assert.GreaterOrEqual(plusLoin, cible - 1e-4f, $"Le buste est allé jusqu'à {plusLoin * 100f:F1} cm pour une cible à {cible * 100f:F1} cm.");
        Assert.AreEqual(cible, lean, 0.005f);
    }

    [Test]
    public void SansTempsEcoule_RienNeBouge()
    {
        float vitesse = 0f;
        Assert.AreEqual(0.1f, PlayerLocomotion.StepLean(0.1f, 0.35f, ref vitesse, SmoothTime, 0f));
    }
}
