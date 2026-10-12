using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Déterminisme de Move() — la garantie la plus précieuse du projet, et la seule qui restait
/// vérifiée à la main.
///
/// CE QUI EST TESTÉ, et pourquoi c'est CETTE propriété : l'équivalence entre une simulation en
/// avant et un REJEU depuis le même état. C'est exactement ce que suppose la réconciliation —
/// le client rejoue ses inputs non confirmés à partir de la position renvoyée par le serveur, et
/// doit retomber sur le même résultat. Si Move() lit quoi que ce soit qui n'est pas restauré
/// avant le rejeu, les deux divergent, et le joueur se fait recaler en boucle.
///
/// POURQUOI SANS SESSION NETCODE : monter un host réel rendrait le test asynchrone et instable
/// (connexion, ports, timing) pour prouver une propriété qui n'a rien de réseau. Move() est une
/// fonction de (état, input, dt) ; le réseau ne fait que décider QUAND on l'appelle. Le test
/// attaque donc directement l'invariant, ce qui le rend rapide et reproductible.
///
/// 🚨 LE PIÈGE QUE CE FICHIER GARDE : le projet s'est fait avoir QUATRE fois par une valeur
/// cumulative lue par Move() sans équivalent confirmé par le serveur — le yaw (dérive en double),
/// currentVelocity (désaccord permanent en accélération), la hauteur de capsule, puis l'état de
/// vault. À chaque fois le symptôme était différent et le diagnostic long. Si une cinquième
/// apparaît, PlayerLocomotion.SimulationState sera incomplète, la restauration partielle, et ces
/// tests échoueront — au lieu de laisser le bug se manifester en jeu sous forme de
/// désynchronisation aléatoire.
/// </summary>
public class MoveDeterminismTests
{
    private const float Dt = 1f / 60f;
    private const int SettleSteps = 90;

    private GameObject ground;
    private GameObject obstacle;
    private GameObject obstacle2;
    private GameObject playerObject;
    private PlayerLocomotion player;

    [SetUp]
    public void SetUp()
    {
        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "TestGround";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(60f, 1f, 60f);
        Physics.SyncTransforms();

        // Inactif pendant l'assemblage : Awake() met en cache le CharacterController et le
        // PlayerInputReader, donc ils doivent exister avant qu'il ne tourne.
        playerObject = new GameObject("TestPlayer");
        playerObject.SetActive(false);
        // Layer "Player", comme le prefab. Indispensable, et le piège est sournois : Awake() fait
        // obstacleMask &= ~(1 << gameObject.layer). Un joueur de test resté sur Default retirerait
        // donc Default du masque — c'est-à-dire l'obstacle lui-même, qui deviendrait invisible à
        // TryFindVaultTarget. Le vault ne se déclenchait jamais, silencieusement.
        playerObject.layer = LayerMask.NameToLayer("Player");
        playerObject.AddComponent<CharacterController>();
        playerObject.AddComponent<PlayerInputReader>();
        player = playerObject.AddComponent<PlayerLocomotion>();
        playerObject.transform.position = new Vector3(0f, 1.5f, 0f);
        playerObject.SetActive(true);
    }

    [TearDown]
    public void TearDown()
    {
        if (playerObject != null) Object.DestroyImmediate(playerObject);
        if (ground != null) Object.DestroyImmediate(ground);
        if (obstacle != null) Object.DestroyImmediate(obstacle);
        if (obstacle2 != null) Object.DestroyImmediate(obstacle2);
    }

    /// <summary>Laisse le joueur tomber au sol et s'y stabiliser. Indispensable : un test qui
    /// démarre en l'air mesurerait surtout de la chute libre, et le vault exige isGrounded.</summary>
    private void Settle()
    {
        for (int i = 0; i < SettleSteps; i++) player.TestMove(Vector2.zero, 0f, false, false, false, false, 0, Dt);
    }

    /// <summary>Séquence d'inputs déterministe : fonction du seul numéro de pas, sans aléatoire ni
    /// horloge. Elle mélange volontairement accélération, décélération, changements de direction,
    /// rotation et changements d'allure — ce sont les chemins où vivent les valeurs cumulatives.</summary>
    private static void InputAt(int step, out Vector2 move, out float lookX, out bool sprint, out bool sneak, out bool aim, out int lean)
    {
        float t = step * Dt;
        move = new Vector2(Mathf.Sin(t * 3.1f), Mathf.Cos(t * 2.3f));
        lookX = Mathf.Sin(t * 1.7f) * 2.5f;
        sprint = (step / 17) % 2 == 0;
        sneak = (step / 29) % 3 == 0;
        aim = (step / 23) % 2 == 1;
        lean = (step / 41) % 3 - 1;
    }

    private void RunSequence(int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            InputAt(i, out Vector2 move, out float lookX, out bool sprint, out bool sneak, out bool aim, out int lean);
            player.TestMove(move, lookX, sprint, sneak, aim, false, lean, Dt);
        }
    }

    private static void AssertBitIdentical(PlayerLocomotion.SimulationState a, PlayerLocomotion.SimulationState b)
    {
        // Comparaison EXACTE, pas approchée. Vector3 == utilise une tolérance de ~1e-5, ce qui
        // laisserait passer une dérive lente : c'est précisément ce genre d'écart qui s'accumule
        // sur des centaines de rejeux et finit par franchir le seuil de réconciliation.
        Assert.AreEqual(a.position.x, b.position.x, "position.x diverge");
        Assert.AreEqual(a.position.y, b.position.y, "position.y diverge");
        Assert.AreEqual(a.position.z, b.position.z, "position.z diverge");
        Assert.AreEqual(a.rotation.eulerAngles.y, b.rotation.eulerAngles.y, "yaw diverge");
        Assert.AreEqual(a.verticalVelocity, b.verticalVelocity, "verticalVelocity diverge");
        Assert.AreEqual(a.currentVelocity.x, b.currentVelocity.x, "currentVelocity.x diverge");
        Assert.AreEqual(a.currentVelocity.z, b.currentVelocity.z, "currentVelocity.z diverge");
        Assert.AreEqual(a.vaulting, b.vaulting, "IsVaulting diverge");
        Assert.AreEqual(a.vaultTimer, b.vaultTimer, "vaultTimer diverge");
    }

    [UnityTest]
    public IEnumerator RejeuDepuisLeMemeEtat_DonneLeMemeResultatAuBitPres()
    {
        yield return null;
        Settle();

        PlayerLocomotion.SimulationState depart = player.TestCaptureState();

        RunSequence(180);
        PlayerLocomotion.SimulationState premierPassage = player.TestCaptureState();

        player.TestRestoreState(depart);
        RunSequence(180);
        PlayerLocomotion.SimulationState rejeu = player.TestCaptureState();

        // Garde anti-test-vide : si le joueur n'a pas bougé, comparer deux immobilités ne
        // prouverait rien. La leçon a déjà été apprise sur le vault, dont le premier test de
        // déterminisme passait alors qu'aucun franchissement ne se déclenchait.
        Assert.Greater(Vector3.Distance(depart.position, premierPassage.position), 1f,
            "Le joueur n'a pas bougé : le test ne prouve rien.");

        AssertBitIdentical(premierPassage, rejeu);
    }

    /// <summary>
    /// Se relever d'un plongeon au sol : la vitesse remonte AVEC la transition de posture, au lieu
    /// de revenir d'un coup à la marche pendant que le corps est encore à genoux (2026-10-06). Et
    /// un rejeu des mêmes inputs, aux mêmes instants de simulation, retombe exactement au même
    /// endroit : l'instant de simulation fait partie de ce que le rejeu doit reproduire.
    /// </summary>
    [UnityTest]
    public IEnumerator SeRelever_LaVitesseRemonteAvecLaTransition_EtLeRejeuLaReproduit()
    {
        yield return null;
        Settle();

        const double changement = 10.0;
        player.TestSetStance(new PlayerLocomotion.StanceState
        {
            current = PlayerLocomotion.Stance.Standing,
            previous = PlayerLocomotion.Stance.Prone,
            changedAt = changement,
        });

        PlayerLocomotion.SimulationState depart = player.TestCaptureState();

        float VitesseApres(int pas)
        {
            for (int i = 0; i < pas; i++)
                player.TestMove(Vector2.up, 0f, false, false, false, false, 0, Dt, changement + i * Dt);
            Vector3 v = player.TestCaptureState().currentVelocity;
            return new Vector2(v.x, v.z).magnitude;
        }

        float auDebut = VitesseApres(6);      // 0,1 s après le changement
        player.TestRestoreState(depart);
        float aLaFin = VitesseApres(72);      // 1,2 s : la transition (0,9 s) est finie
        PlayerLocomotion.SimulationState premierPassage = player.TestCaptureState();

        Assert.Less(auDebut, aLaFin * 0.4f,
            $"0,1 s après avoir quitté l'allongé, on va déjà à {auDebut:F2} m/s sur {aLaFin:F2} : la vitesse n'attend pas le corps.");
        Assert.Greater(aLaFin, 3f, "Garde anti-vacuité : une fois relevé, on doit marcher.");

        player.TestRestoreState(depart);
        VitesseApres(72);
        AssertBitIdentical(premierPassage, player.TestCaptureState());
    }

    /// <summary>
    /// On ne sprinte que vers l'avant, diagonales avant comprises (2026-10-10, « comme beaucoup de
    /// FPS »). Vérifié dans Move() lui-même, sur la vitesse atteinte : c'est elle que le serveur
    /// applique, et elle seule qui compte. La direction à 70° est celle d'un stick de manette
    /// presque de côté, que l'ancienne règle (« une pointe d'avant suffit ») laissait sprinter.
    /// </summary>
    [UnityTest]
    public IEnumerator LeSprint_VersLAvantSeulement_JamaisDeCoteNiEnArriere()
    {
        yield return null;
        Settle();
        PlayerLocomotion.SimulationState depart = player.TestCaptureState();

        float Vitesse(Vector2 move, bool sprint)
        {
            player.TestRestoreState(depart);
            for (int i = 0; i < 90; i++) player.TestMove(move, 0f, sprint, false, false, false, 0, Dt);
            Vector3 v = player.TestCaptureState().currentVelocity;
            return new Vector2(v.x, v.z).magnitude;
        }

        float marche = Vitesse(Vector2.up, false);
        Assert.Greater(Vitesse(Vector2.up, true), marche * 1.5f, "Garde anti-vacuité : le sprint vers l'avant doit aller plus vite que la marche.");
        Assert.Greater(Vitesse(new Vector2(-1f, 1f).normalized, true), marche * 1.5f, "On doit pouvoir sprinter en diagonale avant.");

        var interdites = new[]
        {
            Vector2.right, Vector2.left, Vector2.down, new Vector2(1f, -1f).normalized,
            new Vector2(Mathf.Sin(70f * Mathf.Deg2Rad), Mathf.Cos(70f * Mathf.Deg2Rad)),
        };
        foreach (Vector2 d in interdites)
        {
            Assert.AreEqual(Vitesse(d, false), Vitesse(d, true), 0.01f, $"Touche de sprint vers {d} : on va plus vite qu'en marchant.");
        }
    }

    /// <summary>
    /// On recule et on va de côté moins vite qu'en avançant (2026-10-10, « comme beaucoup de
    /// FPS »), les diagonales avant gardant toute leur vitesse. Vérifié dans Move() lui-même, sur
    /// la vitesse atteinte, comme le sprint.
    /// </summary>
    [UnityTest]
    public IEnumerator ReculerEtAllerDeCote_SontPlusLentsQuAvancer()
    {
        yield return null;
        Settle();
        PlayerLocomotion.SimulationState depart = player.TestCaptureState();

        float Vitesse(Vector2 move)
        {
            player.TestRestoreState(depart);
            for (int i = 0; i < 90; i++) player.TestMove(move, 0f, false, false, false, false, 0, Dt);
            Vector3 v = player.TestCaptureState().currentVelocity;
            return new Vector2(v.x, v.z).magnitude;
        }

        float avant = Vitesse(Vector2.up);
        Assert.Greater(avant, 1f, "Garde anti-vacuité : le joueur doit avancer.");
        Assert.AreEqual(avant, Vitesse(new Vector2(1f, 1f).normalized), avant * 0.01f, "La diagonale avant garde toute la vitesse.");

        float cote = Vitesse(Vector2.right);
        float arriere = Vitesse(Vector2.down);
        Assert.Less(cote, avant * 0.95f, "De côté, on doit aller moins vite qu'en avançant.");
        Assert.Less(arriere, cote * 0.95f, "En reculant, on doit aller moins vite que de côté.");
        Assert.AreEqual(cote, Vitesse(Vector2.left), avant * 0.01f, "Gauche et droite vont à la même vitesse.");
        foreach (Vector2 d in new[] { Vector2.right, Vector2.down, new Vector2(-1f, -1f).normalized })
        {
            Assert.AreEqual(avant * player.DirectionMultiplier(d), Vitesse(d), avant * 0.01f, $"Vers {d}, la vitesse ne suit pas le réglage.");
        }
    }

    /// <summary>
    /// Le poids des armes (2026-10-11, façon Call of Duty) : chaque arme a sa vitesse de déplacement
    /// relative (`WeaponData.moveSpeedMultiplier`), appliquée dans `Move()` d'après l'arme de
    /// l'input. Lue dans les VRAIES armes du préfab : pistolet ×1, fusil et pompe ×0,95, sniper ×0,9.
    /// </summary>
    [UnityTest]
    public IEnumerator ChaqueArme_RalentitLeJoueurDeSonPoids()
    {
        // Un joueur qui porte la panoplie du préfab. Le WeaponController est désactivé : seul son
        // Awake tourne (pas de session réseau ici), Move() n'en lit que la panoplie.
        Object.DestroyImmediate(playerObject);
        WeaponController prefabWeapon = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_ProjectArena/PlayerPrefab/Player.prefab")
            .GetComponentInChildren<WeaponController>(true);
        playerObject = new GameObject("TestPlayer");
        playerObject.SetActive(false);
        playerObject.layer = LayerMask.NameToLayer("Player");
        playerObject.AddComponent<CharacterController>();
        playerObject.AddComponent<PlayerInputReader>();
        var weaponObject = new GameObject("Weapon");
        weaponObject.transform.SetParent(playerObject.transform, false);
        var weapon = weaponObject.AddComponent<WeaponController>();
        weapon.enabled = false;
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(WeaponController).GetField("loadout", flags).SetValue(weapon, prefabWeapon.Loadout is WeaponData[] array ? array.Clone() : null);
        typeof(WeaponController).GetField("aimCamera", flags).SetValue(weapon, weaponObject.AddComponent<Camera>());
        typeof(WeaponController).GetField("visualFeedback", flags).SetValue(weapon, weaponObject.AddComponent<WeaponVisualFeedback>());
        player = playerObject.AddComponent<PlayerLocomotion>();
        playerObject.transform.position = new Vector3(0f, 1.5f, 0f);
        playerObject.SetActive(true);
        Assert.AreEqual(WeaponController.MaxSlots, weapon.SlotCount, "La panoplie du préfab n'a pas été reprise.");

        yield return null;
        Settle();
        PlayerLocomotion.SimulationState depart = player.TestCaptureState();

        float Vitesse(byte slot)
        {
            player.TestRestoreState(depart);
            for (int i = 0; i < 90; i++) player.TestMove(Vector2.up, 0f, false, false, false, false, 0, Dt, 0, slot);
            Vector3 v = player.TestCaptureState().currentVelocity;
            return new Vector2(v.x, v.z).magnitude;
        }

        float sansPoids = 0f, plusLourde = 1f;
        for (int slot = 0; slot < weapon.SlotCount; slot++)
            if (weapon.SpeedMultiplierOf(slot) >= 0.999f) sansPoids = Vitesse((byte)slot);
        Assert.Greater(sansPoids, 1f, "Garde anti-vacuité : il faut une arme sans poids (le pistolet), et le joueur doit avancer.");
        for (int slot = 0; slot < weapon.SlotCount; slot++)
        {
            float k = weapon.SpeedMultiplierOf(slot);
            plusLourde = Mathf.Min(plusLourde, k);
            Debug.Log($"[POIDS] {weapon.DataOf(slot).weaponName} : ×{k:F2}, {Vitesse((byte)slot):F2} m/s");
            Assert.AreEqual(sansPoids * k, Vitesse((byte)slot), sansPoids * 0.005f, $"{weapon.DataOf(slot).weaponName} ne ralentit pas de son poids.");
        }
        Assert.Less(plusLourde, 0.95f, "Garde anti-vacuité : une arme au moins doit peser.");
    }

    /// <summary>
    /// On met du temps à casser un sprint (2026-10-11, demandé par l'utilisateur : « que l'animation
    /// de course mette plus de temps pour s'arrêter ») : au-dessus de l'allure de marche, l'élan dans
    /// la direction de course ne retombe qu'à `sprintDeceleration`. Mais tourner en sprintant ne
    /// dérape pas, et partir de côté en lâchant le sprint ne va jamais plus vite de côté qu'un pas de
    /// côté normal : sinon sortir d'un sprint donnerait un peek plus rapide.
    /// </summary>
    [UnityTest]
    public IEnumerator CasserUnSprint_PrendDuTemps_MaisTournerResteVif()
    {
        yield return null;
        Settle();
        PlayerLocomotion.SimulationState depart = player.TestCaptureState();

        Vector3 Vitesse()
        {
            Vector3 v = player.TestCaptureState().currentVelocity;
            return new Vector3(v.x, 0f, v.z);
        }

        player.TestRestoreState(depart);
        for (int i = 0; i < 90; i++) player.TestMove(Vector2.up, 0f, true, false, false, false, 0, Dt);
        PlayerLocomotion.SimulationState lance = player.TestCaptureState();
        float sprint = Vitesse().magnitude;

        // Lâcher le sprint en continuant d'avancer : 0,1 s plus tard, on court encore vite.
        for (int i = 0; i < 6; i++) player.TestMove(Vector2.up, 0f, false, false, false, false, 0, Dt);
        float apres01 = Vitesse().magnitude;
        for (int i = 0; i < 24; i++) player.TestMove(Vector2.up, 0f, false, false, false, false, 0, Dt);
        float apres05 = Vitesse().magnitude;

        // Tourner de 45° en sprintant (diagonale avant) : la direction suit en moins de 0,15 s.
        player.TestRestoreState(lance);
        Vector2 diagonale = new Vector2(1f, 1f).normalized;
        for (int i = 0; i < 9; i++) player.TestMove(diagonale, 0f, true, false, false, false, 0, Dt);
        Vector3 v = Vitesse();
        Vector3 voulu = player.transform.TransformDirection(new Vector3(diagonale.x, 0f, diagonale.y));
        float ecart = Vector3.Angle(v, voulu);

        // Lâcher le sprint pour partir de côté : l'élan continue vers l'avant, le pas de côté se
        // construit comme d'habitude, sans jamais le dépasser.
        player.TestRestoreState(depart);
        for (int i = 0; i < 90; i++) player.TestMove(Vector2.right, 0f, false, false, false, false, 0, Dt);
        float coteNormal = Vitesse().magnitude;
        player.TestRestoreState(lance);
        float coteMax = 0f, avant015 = 0f, cote015 = 0f;
        for (int i = 0; i < 30; i++)
        {
            player.TestMove(Vector2.right, 0f, false, false, false, false, 0, Dt);
            Vector3 w = Vitesse();
            float cote = Vector3.Dot(w, player.transform.right);
            coteMax = Mathf.Max(coteMax, cote);
            if (i == 8) { avant015 = Vector3.Dot(w, player.transform.forward); cote015 = cote; }
        }

        Debug.Log($"[SPRINT] {sprint:F2} m/s, {apres01:F2} m/s 0,1 s après l'avoir lâché, {apres05:F2} m/s à 0,5 s ; en tournant de 45°, {ecart:F1}° d'écart à 0,15 s ; " +
                  $"de côté en lâchant le sprint : {cote015:F2} m/s de côté et {avant015:F2} vers l'avant à 0,15 s, {coteMax:F2} au plus (pas de côté normal {coteNormal:F2})");
        Assert.Greater(sprint, 7f, "Garde anti-vacuité : le joueur doit sprinter.");
        Assert.Greater(apres01, sprint - 3.5f, $"Le sprint se casse trop vite : {apres01:F2} m/s 0,1 s après (avant : la marche en 0,06 s).");
        Assert.Less(apres05, sprint * 0.6f, "Le sprint doit finir par retomber à l'allure de marche.");
        Assert.Less(ecart, 5f, $"Tourner en sprintant dérape : la vitesse est encore à {ecart:F1}° de la direction voulue.");
        Assert.LessOrEqual(coteMax, coteNormal + 0.05f, $"En sortant d'un sprint, on va de côté à {coteMax:F2} m/s, plus vite qu'un pas de côté normal ({coteNormal:F2}).");
        Assert.Greater(cote015, coteNormal * 0.9f, "Le pas de côté doit se construire aussi vite qu'à l'arrêt.");
        Assert.Greater(avant015, 4.5f, "L'élan vers l'avant doit continuer en partant de côté.");
    }

    /// <summary>
    /// Dans les trois postures, les pieds du joueur — sa racine, d'où se mesurent l'œil, les zones
    /// touchables et le modèle — reposent SUR le sol.
    ///
    /// Le robot flottait de 8,6 cm debout et de 18,8 cm allongé (mesuré le 2026-10-06) : la marge
    /// de peau du CharacterController, plus, allongé, une capsule plus courte que son diamètre qui
    /// dépassait sous les pieds. Rien ne le signalait : le modèle, la caméra et la surface
    /// touchable flottaient ensemble, donc restaient d'accord entre eux.
    /// </summary>
    [UnityTest]
    public IEnumerator DansChaquePosture_LesPiedsReposentSurLeSol()
    {
        yield return null;

        // Les trois postures sont mesurées avant de conclure : un échec doit dire lesquelles sont
        // fausses, pas seulement la première.
        //
        // Chaque posture repart posée juste au-dessus du sol, comme à l'apparition. Après une chute
        // rapide, le CharacterController peut s'arrêter DANS sa marge de peau au lieu de s'y poser :
        // la première version faisait tomber le joueur de 1,5 m, et la position debout passait
        // pour juste même avec l'ancien placement.
        string ecarts = "";
        Settle();
        PlayerLocomotion.SimulationState pose = player.TestCaptureState();
        pose.position = new Vector3(0f, 0.3f, 0f);
        pose.verticalVelocity = 0f;
        pose.currentVelocity = Vector3.zero;

        foreach (PlayerLocomotion.Stance stance in System.Enum.GetValues(typeof(PlayerLocomotion.Stance)))
        {
            player.TestSetStance(new PlayerLocomotion.StanceState
            {
                current = stance,
                previous = stance,
                changedAt = double.NegativeInfinity,
            });
            player.TestRestoreState(pose);
            Settle();

            // Le sol de test a sa face supérieure à y = 0.
            float y = player.TestCaptureState().position.y;
            Debug.Log($"[PIEDS] {stance} : {y * 100f:F2} cm du sol");
            if (Mathf.Abs(y) > 0.01f) ecarts += $" {stance} à {y * 100f:F1} cm ;";
        }
        Assert.IsEmpty(ecarts, "Pieds hors du sol :" + ecarts);
    }

    [UnityTest]
    public IEnumerator TroisRejeuxSuccessifs_NeDerivePasDuTout()
    {
        yield return null;
        Settle();

        PlayerLocomotion.SimulationState depart = player.TestCaptureState();

        RunSequence(120);
        PlayerLocomotion.SimulationState reference = player.TestCaptureState();

        // Un seul rejeu peut masquer une dérive lente. Sous latence le client en enchaîne
        // plusieurs par seconde, donc on vérifie que la répétition ne décale rien.
        for (int essai = 0; essai < 3; essai++)
        {
            player.TestRestoreState(depart);
            RunSequence(120);
            AssertBitIdentical(reference, player.TestCaptureState());
        }
    }

    [UnityTest]
    public IEnumerator EtatDeSimulation_SeRestaureEntierement()
    {
        yield return null;

        // 🚨 CE TEST GARDE L'INVARIANT LE PLUS FRAGILE DU PROJET : SimulationState doit énumérer
        // TOUT l'état cumulatif lu par Move(). Le projet s'est fait piéger cinq fois par une
        // valeur oubliée (yaw, currentVelocity, hauteur de capsule, état de vault, hauteur d'arc).
        //
        // Les autres tests de déterminisme ne suffisent PAS à l'attraper : ils restaurent un état
        // d'AVANT le franchissement, si bien que le rejeu rappelle BeginVault, qui recalcule tout.
        // Vérifié par mutation — retirer la restauration de vaultPeakY les laissait tous verts.
        //
        // Ici on restaure un état pris EN PLEIN VAULT, après qu'un SECOND franchissement d'une
        // hauteur différente a écrasé les valeurs. C'est exactement ce que fait une réconciliation,
        // et le seul cas où une restauration incomplète se voit.
        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position = new Vector3(0f, 0.40f, 3f);
        obstacle.transform.localScale = new Vector3(4f, 0.80f, 0.5f);

        obstacle2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle2.transform.position = new Vector3(0f, 0.60f, 9f);
        obstacle2.transform.localScale = new Vector3(4f, 1.20f, 0.5f);

        Physics.SyncTransforms();
        Settle();

        PlayerLocomotion.SimulationState pendantPremierVault = default;
        bool premierCapture = false;
        int vaultsVus = 0;
        bool vaultaitAvant = false;

        for (int i = 0; i < 600; i++)
        {
            player.TestMove(Vector2.up, 0f, false, false, false, true, 0, Dt);

            bool vaulteMaintenant = player.IsVaulting;
            if (vaulteMaintenant && !vaultaitAvant) vaultsVus++;
            vaultaitAvant = vaulteMaintenant;

            // Photo prise au milieu du PREMIER franchissement.
            if (vaultsVus == 1 && vaulteMaintenant && !premierCapture)
            {
                pendantPremierVault = player.TestCaptureState();
                premierCapture = true;
            }

            // Dès que le SECOND est en cours, ses valeurs ont écrasé celles du premier.
            if (vaultsVus == 2 && vaulteMaintenant) break;
        }

        Assert.IsTrue(premierCapture, "Le premier vault ne s'est pas déclenché : le test est vide.");
        Assert.AreEqual(2, vaultsVus, "Le second vault ne s'est pas déclenché : le test est vide.");

        player.TestRestoreState(pendantPremierVault);
        PlayerLocomotion.SimulationState relu = player.TestCaptureState();

        Assert.AreEqual(pendantPremierVault.position.x, relu.position.x, "position.x non restaurée");
        Assert.AreEqual(pendantPremierVault.position.y, relu.position.y, "position.y non restaurée");
        Assert.AreEqual(pendantPremierVault.position.z, relu.position.z, "position.z non restaurée");
        Assert.AreEqual(pendantPremierVault.verticalVelocity, relu.verticalVelocity, "verticalVelocity non restaurée");
        Assert.AreEqual(pendantPremierVault.currentVelocity.x, relu.currentVelocity.x, "currentVelocity.x non restaurée");
        Assert.AreEqual(pendantPremierVault.currentVelocity.z, relu.currentVelocity.z, "currentVelocity.z non restaurée");
        Assert.AreEqual(pendantPremierVault.vaulting, relu.vaulting, "IsVaulting non restauré");
        Assert.AreEqual(pendantPremierVault.vaultTimer, relu.vaultTimer, "vaultTimer non restauré");
        Assert.AreEqual(pendantPremierVault.vaultStart.y, relu.vaultStart.y, "vaultStart non restauré");
        Assert.AreEqual(pendantPremierVault.vaultEnd.y, relu.vaultEnd.y, "vaultEnd non restauré");
        Assert.AreEqual(pendantPremierVault.vaultPeakY, relu.vaultPeakY, "vaultPeakY non restauré");
    }

    [UnityTest]
    public IEnumerator VaultPasseAuDessusDeLObstacle_EtPasAuTravers()
    {
        yield return null;

        const float SommetObstacle = 0.8f;

        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = "TestBarricadeFine";
        obstacle.transform.position = new Vector3(0f, SommetObstacle * 0.5f, 3f);
        obstacle.transform.localScale = new Vector3(4f, SommetObstacle, 0.5f);
        Physics.SyncTransforms();

        Settle();

        // On relève le point HAUT atteint pendant le franchissement. C'est la seule mesure qui
        // dit si le joueur passe PAR-DESSUS ou À TRAVERS : la position finale, elle, est au sol
        // dans les deux cas — ce qui avait déjà rendu un test précédent inopérant.
        bool vaultObserve = false;
        float pointHaut = float.MinValue;

        for (int i = 0; i < 200; i++)
        {
            player.TestMove(Vector2.up, 0f, false, false, false, true, 0, Dt);
            if (player.IsVaulting)
            {
                vaultObserve = true;
                float y = player.TestCaptureState().position.y;
                if (y > pointHaut) pointHaut = y;
            }
        }

        Assert.IsTrue(vaultObserve, "Aucun vault ne s'est déclenché : le test est vide.");

        Assert.Greater(pointHaut, SommetObstacle,
            "L'arc culmine à y=" + pointHaut.ToString("F2") + " m alors que l'obstacle monte à "
            + SommetObstacle.ToString("F2") + " m : le joueur le TRAVERSE au lieu de le franchir.");
    }

    [UnityTest]
    public IEnumerator VaultParDessusUnObstacleFin_AtterritAuSolEtPasDessus()
    {
        yield return null;

        // Profondeur 0,50 m : les dimensions réelles des barricades de l'arène. C'est le cas qui
        // a révélé le défaut — le joueur restait PERCHÉ sur la barricade au lieu de retomber
        // derrière, parce que l'arrivée était calculée à l'altitude du SOMMET et que sa capsule
        // (0,35 m de rayon) débordait encore assez pour y trouver du sol.
        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = "TestBarricadeFine";
        obstacle.transform.position = new Vector3(0f, 0.4f, 3f);
        obstacle.transform.localScale = new Vector3(4f, 0.8f, 0.5f);
        Physics.SyncTransforms();

        Settle();

        // 🚨 On relève la hauteur À L'INSTANT EXACT où le vault se termine, pas à la fin de la
        // boucle. Le joueur garde son input avant : si on le laisse courir, il DESCEND tout seul
        // de la barricade et la hauteur finale redevient basse, avec ou sans le défaut. Une
        // première version faisait ça et passait sous mutation — donc ne testait rien.
        bool vaultObserve = false;
        bool vaultTermine = false;
        float hauteurALArrivee = -1f;

        for (int i = 0; i < 200 && !vaultTermine; i++)
        {
            bool vaultaitAvant = player.IsVaulting;
            player.TestMove(Vector2.up, 0f, false, false, false, true, 0, Dt);

            if (player.IsVaulting) vaultObserve = true;
            if (vaultaitAvant && !player.IsVaulting)
            {
                vaultTermine = true;
                hauteurALArrivee = player.TestCaptureState().position.y;
            }
        }

        Assert.IsTrue(vaultObserve, "Aucun vault ne s'est déclenché : le test est vide.");
        Assert.IsTrue(vaultTermine, "Le vault ne s'est jamais terminé.");

        const float SommetObstacle = 0.8f;
        Assert.Less(hauteurALArrivee, SommetObstacle * 0.5f,
            "Le joueur arrive à y=" + hauteurALArrivee.ToString("F2") + " m, donc PERCHÉ sur "
            + "l'obstacle (sommet à " + SommetObstacle.ToString("F2") + " m) au lieu de retomber "
            + "au sol derrière lui.");
    }

    [UnityTest]
    public IEnumerator VaultRejoue_SuitExactementLeMemeArc()
    {
        yield return null;

        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = "TestVaultObstacle";
        obstacle.transform.position = new Vector3(0f, 0.4f, 3f);
        obstacle.transform.localScale = new Vector3(6f, 0.8f, 1f);

        // 🚨 Obligatoire : Physics.autoSyncTransforms vaut false dans ce projet, donc déplacer un
        // transform ne met PAS à jour la scène physique interrogée par les raycasts. Sans cette
        // ligne, TryFindVaultTarget ne voyait pas l'obstacle et aucun vault ne se déclenchait —
        // exactement le même piège que celui qui rendait le rewind silencieusement inopérant.
        Physics.SyncTransforms();

        Settle();
        PlayerLocomotion.SimulationState depart = player.TestCaptureState();

        bool vaultObserve = false;
        for (int i = 0; i < 150; i++)
        {
            player.TestMove(Vector2.up, 0f, false, false, false, true, 0, Dt);
            if (player.IsVaulting) vaultObserve = true;
        }
        PlayerLocomotion.SimulationState premierPassage = player.TestCaptureState();

        // La garde qui compte. Sans elle, ce test passerait au vert sans qu'aucun franchissement
        // n'ait lieu — c'est exactement ce qui s'est produit à la première écriture du test de
        // vault, quand le joueur n'était pas encore au sol et qu'aucun vault ne se déclenchait.
        Assert.IsTrue(vaultObserve, "Aucun vault ne s'est déclenché : le test est vide.");

        player.TestRestoreState(depart);
        for (int i = 0; i < 150; i++) player.TestMove(Vector2.up, 0f, false, false, false, true, 0, Dt);

        AssertBitIdentical(premierPassage, player.TestCaptureState());
    }
}
