using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Phase = RoundManager.Phase;

/// <summary>
/// Le déroulé d'un match, tel que demandé par l'utilisateur le 2026-10-12 :
/// - une préparation avant chaque manche, où l'on choisit son arme ; plus de changement ensuite ;
/// - en fin de manche, le survivant garde la main (il bouge, tire) pour narguer le perdant ;
/// - un pseudo, annoncé à l'hôte en se connectant ;
/// - les statistiques de l'écran de fin.
/// Et l'épave d'un robot tué, qui ne doit toucher que le décor.
/// </summary>
public class MatchFlowTests
{
    [Test]
    public void LeSurvivant_GardeLaMain_EnFinDeManche_EtLeMortJamais()
    {
        Assert.IsTrue(RoundManager.PlayAllowed(Phase.Active, true), "Pendant la manche.");
        Assert.IsTrue(RoundManager.PlayAllowed(Phase.WaitingForPlayers, true), "Seul, pour s'échauffer.");
        Assert.IsTrue(RoundManager.PlayAllowed(Phase.RoundOver, true), "Le survivant, après la mort de son adversaire.");
        Assert.IsTrue(RoundManager.PlayAllowed(Phase.MatchOver, true), "Le gagnant du match, avant l'écran de fin.");

        Assert.IsFalse(RoundManager.PlayAllowed(Phase.Preparation, true), "Préparation : tout le monde est gelé.");
        Assert.IsFalse(RoundManager.PlayAllowed(Phase.Starting, true), "Décompte : les deux partent ensemble.");

        foreach (Phase phase in System.Enum.GetValues(typeof(Phase)))
            Assert.IsFalse(RoundManager.PlayAllowed(phase, false), $"Un robot mort ne joue jamais ({phase}).");
    }

    [Test]
    public void OnChoisitSonArme_AvantLaManche_JamaisPendant()
    {
        Assert.IsTrue(RoundManager.WeaponSwitchAllowed(Phase.Preparation, false), "Pendant la préparation.");
        Assert.IsFalse(RoundManager.WeaponSwitchAllowed(Phase.Preparation, true), "Validé : le choix est fermé.");
        Assert.IsTrue(RoundManager.WeaponSwitchAllowed(Phase.WaitingForPlayers, false), "Seul, pour essayer les armes.");

        foreach (Phase phase in new[] { Phase.Starting, Phase.Active, Phase.RoundOver, Phase.MatchOver })
        {
            Assert.IsFalse(RoundManager.WeaponSwitchAllowed(phase, false), $"Plus de changement d'arme une fois la manche lancée ({phase}).");
        }
    }

    [Test]
    public void LePseudo_EstNettoye_EtBorne()
    {
        Assert.AreEqual("Gus", PlayerMatchState.SanitizeName("  Gus  "));
        Assert.AreEqual("Le Robot", PlayerMatchState.SanitizeName("Le \t  Robot"), "Les espaces multiples se réduisent.");
        Assert.AreEqual("AB", PlayerMatchState.SanitizeName("A\nB"), "Pas de saut de ligne : il séparerait la demande de connexion.");
        Assert.AreEqual("Héloïse", PlayerMatchState.SanitizeName("Héloïse\u0007"), "Accents gardés, contrôle retiré.");
        Assert.AreEqual("ok", PlayerMatchState.SanitizeName("ok😀"), "Les emoji sont retirés.");
        Assert.AreEqual(PlayerMatchState.MaxNameLength, PlayerMatchState.SanitizeName(new string('x', 40)).Length);
        Assert.AreEqual("", PlayerMatchState.SanitizeName("   "));
        Assert.AreEqual("", PlayerMatchState.SanitizeName(null));
    }

    [Test]
    public void DeuxJoueursDuMemeNom_SontDistingues()
    {
        Assert.AreEqual("Gus", PlayerMatchState.UniqueName("Gus", new string[0], 0));
        Assert.AreEqual("Gus 2", PlayerMatchState.UniqueName("Gus", new[] { "gus" }, 1), "Sans tenir compte des majuscules.");
        Assert.AreEqual("Gus 3", PlayerMatchState.UniqueName("Gus", new[] { "Gus", "Gus 2" }, 2));
        Assert.AreEqual("Joueur 2", PlayerMatchState.UniqueName("", new[] { "Gus" }, 1), "Sans pseudo : Joueur N.");

        string longName = new string('a', PlayerMatchState.MaxNameLength);
        string unique = PlayerMatchState.UniqueName(longName, new[] { longName }, 1);
        Assert.LessOrEqual(unique.Length, PlayerMatchState.MaxNameLength, "Le numéro tient dans les 16 caractères.");
        Assert.AreNotEqual(longName, unique);
    }

    [Test]
    public void LaDemandeDeConnexion_PorteVersionEtPseudo()
    {
        byte[] payload = SessionFlow.BuildPayload("0.3.0", "  Héloïse ");
        Assert.IsTrue(SessionFlow.ParsePayload(payload, out string version, out string name));
        Assert.AreEqual("0.3.0", version);
        Assert.AreEqual("Héloïse", name);

        // Une build d'avant le pseudo n'envoie que sa version : elle est lue, et sera refusée par
        // la version, avec un message clair.
        Assert.IsTrue(SessionFlow.ParsePayload(System.Text.Encoding.UTF8.GetBytes("0.1.0"), out version, out name));
        Assert.AreEqual("0.1.0", version);
        Assert.AreEqual("", name);

        Assert.IsFalse(SessionFlow.ParsePayload(null, out _, out _), "Pas de demande.");
        Assert.IsFalse(SessionFlow.ParsePayload(new byte[SessionFlow.MaxPayloadBytes + 1], out _, out _), "Trop longue : client modifié.");

        // Un pseudo qui contiendrait un saut de ligne ne peut pas se faire passer pour une version.
        Assert.IsTrue(SessionFlow.ParsePayload(SessionFlow.BuildPayload("0.3.0", "a\n0.9.9"), out version, out name));
        Assert.AreEqual("0.3.0", version);
        Assert.AreEqual("a0.9.9", name);

        // Le serveur nettoie lui-même ce qu'il reçoit : un client modifié ne passe pas par BuildPayload.
        Assert.IsTrue(SessionFlow.ParsePayload(System.Text.Encoding.UTF8.GetBytes("0.3.0\n   Bad\u0007Name\t "), out _, out name));
        Assert.AreEqual("BadName", name);
    }

    /// <summary>
    /// Deux instances du jeu sur un même PC partagent leurs PlayerPrefs (le registre de Windows, relu
    /// à chaque appel) : le dernier pseudo tapé valait pour les deux, et la partie montrait « Untel »
    /// et « Untel 2 » (retour de l'utilisateur, 2026-10-12). Chaque instance annonce le sien.
    /// </summary>
    [Test]
    public void LePseudo_ResteCeluiDeCetteInstance_QuandUneAutreEnregistreLeSien()
    {
        string saved = PlayerPrefs.GetString(GameSettings.PlayerNameKey, string.Empty);
        string mine = GameSettings.PlayerName;
        try
        {
            GameSettings.PlayerName = "Alpha";
            Assert.AreEqual("Alpha", PlayerPrefs.GetString(GameSettings.PlayerNameKey), "Garde : le pseudo est retenu pour le prochain lancement.");

            // L'autre instance tape le sien, dans la même clé du registre.
            PlayerPrefs.SetString(GameSettings.PlayerNameKey, "Beta");
            Assert.AreEqual("Alpha", GameSettings.PlayerName, "Cette instance annonce toujours le pseudo de son champ.");
            Assert.AreEqual("Alpha", PlayerName(SessionFlow.BuildPayload("0.3.0", GameSettings.PlayerName)), "Et c'est lui qui part dans la demande de connexion.");
        }
        finally
        {
            GameSettings.PlayerName = mine;
            PlayerPrefs.SetString(GameSettings.PlayerNameKey, saved);
            PlayerPrefs.Save();
        }
    }

    private static string PlayerName(byte[] payload)
    {
        Assert.IsTrue(SessionFlow.ParsePayload(payload, out _, out string name));
        return name;
    }

    [Test]
    public void LesStatistiques_SeComptentTirParTir()
    {
        var stats = default(PlayerMatchState.MatchStats);
        stats = stats.WithShot(true, true, 23.8f);
        stats = stats.WithShot(false, false, 0f);
        stats = stats.WithShot(true, false, 17f);
        stats = stats.WithShot(true, false, -5f); // jamais de dégâts négatifs
        Assert.AreEqual(4, stats.shotsFired);
        Assert.AreEqual(3, stats.shotsHit);
        Assert.AreEqual(1, stats.headshots);
        Assert.AreEqual(40.8f, stats.damageDealt, 1e-3f);
        Assert.AreEqual(0.75f, stats.Accuracy, 1e-4f);

        Assert.AreEqual(0f, default(PlayerMatchState.MatchStats).Accuracy, "Aucun tir : pas de division par zéro.");

        stats = stats.WithDamage(60f); // le taser
        Assert.AreEqual(100.8f, stats.damageDealt, 1e-3f);
        Assert.AreEqual(4, stats.shotsFired, "Le taser ne compte pas comme un tir.");

        stats = stats.WithRoundWin(42f).WithRoundWin(31.5f).WithRoundWin(55f);
        Assert.AreEqual(31.5f, stats.fastestRoundWin, 1e-4f, "La manche gagnée la plus rapide.");
    }

    [Test]
    public void LEpave_NeToucheQueLeDecor()
    {
        int corpse = CorpseLayer.Index;
        Assert.AreEqual(10, corpse, "Le layer Corpse (10) doit exister (Project Settings > Tags and Layers).");

        Assert.IsFalse(Physics.GetIgnoreLayerCollision(corpse, 0), "L'épave tombe sur le décor (Default).");
        for (int layer = 1; layer < 32; layer++)
        {
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(corpse, layer),
                $"L'épave ne doit pas toucher le layer {layer} « {LayerMask.LayerToName(layer)} » : ni les joueurs (le survivant la traverse), ni elle-même (sa capsule invisible).");
        }

        // Les balles ne la voient pas : elle n'est dans le masque d'aucune arme.
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:WeaponData");
        Assert.IsNotEmpty(guids, "Aucune arme trouvée : le test ne vérifierait rien.");
        foreach (string guid in guids)
        {
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            Assert.AreEqual(0, data.worldMask.value & CorpseLayer.Mask, $"{data.weaponName} : les balles doivent traverser l'épave.");
        }
    }

    /// <summary>
    /// L'épave hérite de la vitesse du robot. Chez l'hôte, le robot d'un client avance par à-coups
    /// (au rythme d'arrivée de ses inputs) : mesurée sur une image, sa vitesse valait trois fois la
    /// vraie sur une image de rattrapage, et l'épave partait projetée (retour de l'utilisateur,
    /// 2026-10-12 : « ragdoll violent »).
    /// </summary>
    [Test]
    public void LaVitesseHeritee_NeSEmballePas_QuandLeRobotAvanceParACoups()
    {
        const float dt = 1f / 60f;
        const float speed = 5f; // un pas chassé
        var window = new PlayerDeath.MotionWindow();
        Vector3 position = Vector3.zero;
        float time = 0f;
        Vector3 worst = Vector3.zero;
        for (int frame = 0; frame < 120; frame++)
        {
            // Trois images de mouvement arrivent ensemble, une image sur trois.
            if (frame % 3 == 2) position += Vector3.right * speed * dt * 3f;
            time += dt;
            window.Add(time, position);
            if (frame >= 30 && window.Velocity.magnitude > worst.magnitude) worst = window.Velocity;
        }
        Assert.Greater(worst.magnitude, speed * 0.8f, "Garde : la vitesse mesurée est bien celle du robot.");
        Assert.Less(worst.magnitude, speed * 1.25f, $"Jamais loin de la vraie vitesse, quelle que soit l'image (pire : {worst.magnitude:F1} m/s).");

        // Une téléportation (replacement de manche) n'est pas une vitesse.
        window.Add(time + dt, position + Vector3.forward * 40f);
        Assert.AreEqual(Vector3.zero, window.Velocity);

        // Et l'épave n'en garde qu'une course raisonnable, sans s'envoler.
        Vector3 limited = RobotCorpse.LimitInheritedVelocity(new Vector3(9f, 6f, 0f));
        Assert.AreEqual(RobotCorpse.MaxInheritedSpeed, new Vector3(limited.x, 0f, limited.z).magnitude, 1e-4f);
        Assert.LessOrEqual(limited.y, 1f);
        Assert.AreEqual(Vector3.zero, RobotCorpse.LimitInheritedVelocity(new Vector3(float.NaN, 0f, 0f)));
    }

    /// <summary>
    /// Les célébrations de l'écran de fin (retour de l'utilisateur, 2026-10-12 : « jouées au
    /// hasard ») : jamais deux fois la même de suite, chacune jouée en entier et fondue dans la
    /// suivante sans saut, et la même image sur les deux machines (même graine, même instant).
    /// </summary>
    [Test]
    public void LesCelebrations_SEnchainentAuHasard_PareilChezLesDeuxJoueurs()
    {
        float[] lengths = { 3.77f, 8.53f, 4.5f }; // les trois clips fournis
        const float fade = VictoryStage.CrossFade;
        const float step = 0.02f;
        var openers = new HashSet<int>();
        for (int seed = 0; seed < 40; seed++)
        {
            VictoryStage.CelebrationFrame previous = VictoryStage.CelebrationAt(seed, 0f, lengths, fade);
            openers.Add(previous.clip);
            int changes = 0;
            for (int i = 1; i < 2000; i++)
            {
                float t = i * step;
                VictoryStage.CelebrationFrame f = VictoryStage.CelebrationAt(seed, t, lengths, fade);
                VictoryStage.CelebrationFrame again = VictoryStage.CelebrationAt(seed, t, lengths, fade);
                Assert.IsTrue(f.clip == again.clip && f.time == again.time && f.nextClip == again.nextClip && f.nextWeight == again.nextWeight,
                    "Même graine, même instant : même image chez les deux joueurs.");

                Assert.That(f.time, Is.InRange(0f, lengths[f.clip] + 1e-3f), "Chaque clip se joue dans sa durée.");
                Assert.That(f.nextWeight, Is.InRange(0f, 1f));
                if (f.nextWeight > 0f) Assert.AreNotEqual(f.clip, f.nextClip, $"Jamais deux fois la même de suite (graine {seed}, {t:F2} s).");

                if (f.clip != previous.clip)
                {
                    changes++;
                    Assert.AreEqual(previous.nextClip, f.clip, "Le clip qui prend la main est celui qui montait.");
                    Assert.Greater(previous.time, lengths[previous.clip] - 2f * step, "Le clip précédent a été joué jusqu'au bout.");
                    Assert.AreEqual(previous.nextTime + step, f.time, 2e-3f, "Pas de saut de pose au passage de l'un à l'autre.");
                }
                previous = f;
            }
            Assert.GreaterOrEqual(changes, 4, "Sur 40 s, les célébrations se succèdent.");
        }
        Assert.AreEqual(lengths.Length, openers.Count, "Selon le match, chacune peut ouvrir l'écran de fin.");
        Assert.AreNotEqual(MatchEndScreen.CelebrationSeed(1234.5), MatchEndScreen.CelebrationSeed(1239.75), "Chaque match tire les siennes.");
    }

    /// <summary>
    /// La jauge de vie façon Rainbow Six (demandée par l'utilisateur, 2026-10-12) : la part perdue
    /// reste rouge un instant, pour voir ce que le coup a coûté, puis se vide ; une nouvelle manche
    /// la remplit aussitôt.
    /// </summary>
    [Test]
    public void LaJaugeDeVie_GardeLeCoupEnRouge_PuisSeVide()
    {
        const float hold = 0.45f, speed = 90f, dt = 0.02f;
        float trail = 100f;
        float since = 0f;
        for (; since < hold - dt; since += dt) trail = GameHud.StepHealthTrail(trail, 60f, since, dt, hold, speed);
        Assert.AreEqual(100f, trail, "Touché (100 → 60) : la part perdue reste visible.");

        for (; since < hold + 0.2f; since += dt) trail = GameHud.StepHealthTrail(trail, 60f, since, dt, hold, speed);
        Assert.That(trail, Is.InRange(65f, 95f), "Puis elle se vide, sans sauter d'un coup.");

        for (; since < 3f; since += dt) trail = GameHud.StepHealthTrail(trail, 60f, since, dt, hold, speed);
        Assert.AreEqual(60f, trail, 1e-3f, "Jusqu'à la vie restante.");

        Assert.AreEqual(100f, GameHud.StepHealthTrail(60f, 100f, 0f, dt, hold, speed), "Une nouvelle manche la remplit aussitôt.");
    }
}
