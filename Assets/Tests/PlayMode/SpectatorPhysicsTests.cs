using NUnit.Framework;
using UnityEngine;

/// <summary>
/// La tribune contre de vrais colliders (2026-10-13) : ce qu'un objet lancé touche (le décor, la surface
/// touchable d'un duelliste), où se pose le matériel, ce qu'atteint un coup de poing sur un bloc
/// fissuré, et ce qui retient un spectateur sur la passerelle (le garde-corps, que lui seul ne franchit
/// pas ; l'étourdissement, qui le fige).
///
/// La simulation des objets est celle du serveur (ArenaItem.TestSimulate passe par Advance, FindHit et
/// AfterImpact), sans session : les objets ne dépendent que de la scène physique.
/// </summary>
public class SpectatorPhysicsTests
{
    private const float Dt = 1f / 60f;
    private readonly System.Collections.Generic.List<GameObject> created = new System.Collections.Generic.List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created)
        {
            if (go != null) Object.DestroyImmediate(go);
        }
        created.Clear();
    }

    private GameObject Box(string name, Vector3 center, Vector3 size)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = center;
        box.transform.localScale = size;
        created.Add(box);
        return box;
    }

    private ArenaItem NewItem()
    {
        var go = new GameObject("ObjetDeTest");
        go.SetActive(false);
        go.AddComponent<Unity.Netcode.NetworkObject>();
        ArenaItem item = go.AddComponent<ArenaItem>();
        created.Add(go);
        return item;
    }

    /// <summary>Un duelliste de test : un joueur (sans rôle, donc duelliste) dont le buste est une zone
    /// touchable sur le layer Hitbox, comme celles de PlayerHitbox.</summary>
    private PlayerLocomotion NewDuelist(Vector3 feet)
    {
        var go = new GameObject("DuellisteDeTest");
        go.SetActive(false);
        go.layer = LayerMask.NameToLayer("Player");
        go.AddComponent<CharacterController>();
        go.AddComponent<PlayerInputReader>();
        PlayerLocomotion locomotion = go.AddComponent<PlayerLocomotion>();
        go.transform.position = feet;
        var zone = new GameObject("Torse");
        zone.layer = LayerMask.NameToLayer("Hitbox");
        zone.transform.SetParent(go.transform, false);
        zone.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        var capsule = zone.AddComponent<CapsuleCollider>();
        capsule.isTrigger = true;
        capsule.radius = 0.25f;
        capsule.height = 0.9f;
        go.SetActive(true);
        created.Add(go);
        return locomotion;
    }

    // ------------------------------------------------------------------
    // Les objets lancés
    // ------------------------------------------------------------------

    /// <summary>La trajectoire que voit le lanceur (2026-10-27) suit celle que le serveur simulera : par-dessus un
    /// muret, elle s'arrête là où le caillou se brise, et son point de chute est sur la surface touchée.</summary>
    [Test]
    public void LaTrajectoireMontree_SArreteLaOuLeServeurBriseLeCaillou()
    {
        Box("Sol", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
        Box("Muret", new Vector3(0f, 0.5f, 5f), new Vector3(10f, 1f, 0.4f));
        Box("Mur du fond", new Vector3(0f, 3f, 22f), new Vector3(10f, 6f, 1f));
        Physics.SyncTransforms();

        ArenaItem rock = NewItem();
        foreach (Vector3 velocity in new[] { new Vector3(0f, 6f, 12f), new Vector3(0f, 9f, 16f), new Vector3(1.5f, 2f, 20f) })
        {
            Vector3 origin = new Vector3(0f, 1.6f, 0f);
            var points = new Vector3[ThrowArcPreview.MaxPoints];
            int count = ThrowArcPreview.Trace(origin, velocity, rock.TestGravity, 0.09f, 1, points, out bool hit, out Vector3 hitPoint, out _);
            Assert.IsTrue(hit, $"À {velocity} : le caillou touche le décor.");
            Assert.Greater(count, 2, "Une parabole, pas un trait.");

            rock.TestBegin(ArenaItemKind.Rock, origin, velocity);
            rock.TestSimulate(8.0);
            Assert.AreEqual(ArenaItem.Phase.Shattered, rock.TestMotion.phase);
            Assert.Less(Vector3.Distance(rock.TestMotion.position, hitPoint), 0.06f,
                $"À {velocity} : la trajectoire montrée finit là où le serveur brise le caillou ({rock.TestMotion.position} contre {hitPoint}).");
        }
    }

    [Test]
    public void UnCaillouLance_SeBriseSurLeMur()
    {
        Box("Sol", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
        Box("Mur", new Vector3(0f, 2f, 8f), new Vector3(10f, 4f, 1f));
        Physics.SyncTransforms();

        ArenaItem rock = NewItem();
        rock.TestBegin(ArenaItemKind.Rock, new Vector3(0f, 2f, 0f), new Vector3(0f, 2f, 18f));
        rock.TestSimulate(3.0);

        Assert.AreEqual(ArenaItem.Phase.Shattered, rock.TestMotion.phase, "Un caillou lancé se brise sur ce qu'il touche.");
        Assert.AreEqual(7.5f, rock.TestMotion.position.z, 0.05f, "Contre la face du mur.");
        Assert.IsNull(rock.TestHitDuelist);
        Assert.AreEqual(2, rock.TestSegments.Count, "Le vol, puis l'impact : deux tronçons.");
    }

    [Test]
    public void UnCaillouLance_TouleLaSurfaceTouchableDUnDuelliste_PasLeDecorDerriere()
    {
        Box("Sol", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
        Box("Mur", new Vector3(0f, 2f, 9f), new Vector3(10f, 4f, 1f));
        PlayerLocomotion duelist = NewDuelist(new Vector3(0f, 0f, 5f));
        Physics.SyncTransforms();

        ArenaItem rock = NewItem();
        rock.TestBegin(ArenaItemKind.Rock, new Vector3(0f, 1.2f, 0f), new Vector3(0f, 0.5f, 20f));
        rock.TestSimulate(3.0);

        Assert.AreSame(duelist, rock.TestHitDuelist, "Le caillou touche le duelliste (sa surface touchable).");
        Assert.AreEqual(ArenaItem.Phase.Shattered, rock.TestMotion.phase);
        Assert.Less(rock.TestMotion.position.z, 5.3f, "Il se brise sur lui, pas sur le mur derrière.");
    }

    [Test]
    public void UnCaillouLache_NeFrappePersonne_EtSePose()
    {
        Box("Sol", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
        PlayerLocomotion duelist = NewDuelist(new Vector3(0f, 0f, 1.5f));
        Physics.SyncTransforms();

        // Lâché par un spectateur étourdi : il tombe à travers le robot, et se pose au sol.
        ArenaItem rock = NewItem();
        rock.TestBegin(ArenaItemKind.Rock, new Vector3(0f, 1.6f, 1f), new Vector3(0f, 1f, 1f), harmless: true);
        rock.TestSimulate(3.0);

        Assert.IsNull(rock.TestHitDuelist, "Un caillou lâché ne frappe personne.");
        Assert.AreEqual(ArenaItem.Phase.Resting, rock.TestMotion.phase, "Il se pose : on peut le ramasser (le voler).");
        Assert.AreEqual(0.09f, rock.TestMotion.position.y, 0.02f, "Posé au sol.");
        Assert.IsNotNull(duelist);
    }

    [Test]
    public void LeMateriel_RebonditSurLeMur_PuisSePoseAuSol()
    {
        Box("Sol", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
        Box("Mur", new Vector3(0f, 2f, 6f), new Vector3(10f, 4f, 1f));
        PlayerLocomotion duelist = NewDuelist(new Vector3(0f, 0f, 3f));
        Physics.SyncTransforms();

        ArenaItem weapon = NewItem();
        weapon.TestBegin(ArenaItemKind.PowerUp, new Vector3(0f, 2.5f, 0f), new Vector3(0f, 1f, 14f));
        weapon.TestSimulate(5.0);

        Assert.IsNull(weapon.TestHitDuelist, "Le matériel traverse les robots : on le lance VERS un duelliste.");
        Assert.AreEqual(ArenaItem.Phase.Resting, weapon.TestMotion.phase, "Il finit posé.");
        Assert.Less(weapon.TestMotion.position.z, 5.5f - 0.15f, "Renvoyé par le mur, devant lui.");
        Assert.AreEqual(0.16f, weapon.TestMotion.position.y, 0.02f, "Au sol.");
        Assert.GreaterOrEqual(weapon.TestSegments.Count, 3, "Le vol, le rebond, la pose.");
        Assert.IsNotNull(duelist);
    }

    // ------------------------------------------------------------------
    // Les blocs fissurés
    // ------------------------------------------------------------------

    [Test]
    public void LePoingAtteintLeBlocFissure_PasATraversUnMur()
    {
        GameObject block = Box("Bloc", new Vector3(0f, 0.45f, 1.2f), new Vector3(1.1f, 0.9f, 0.6f));
        RockSource source = block.AddComponent<RockSource>();
        source.Index = 0;
        var tribuneGo = new GameObject("TribuneDeTest");
        created.Add(tribuneGo);
        Tribune tribune = tribuneGo.AddComponent<Tribune>();
        Physics.SyncTransforms();

        Vector3 eye = new Vector3(0f, 1.65f, 0f);
        Vector3 down = (new Vector3(0f, 0.6f, 1.2f) - eye).normalized;
        Assert.AreSame(source, tribune.QueryRockSource(eye, down, 1.5f, 0.3f, 1, out float distance, out _), "Le bloc devant soi.");
        Assert.Less(distance, 1.5f);
        Assert.IsNull(tribune.QueryRockSource(eye, Vector3.back, 1.5f, 0.3f, 1, out _, out _), "Dans le dos.");

        Box("Cloison", new Vector3(0f, 1f, 0.6f), new Vector3(2f, 2f, 0.1f));
        Physics.SyncTransforms();
        Assert.IsNull(tribune.QueryRockSource(eye, down, 1.5f, 0.3f, 1, out _, out _), "Rien à travers une cloison.");
    }

    // ------------------------------------------------------------------
    // Ce qui retient un spectateur sur la passerelle
    // ------------------------------------------------------------------

    private PlayerLocomotion NewSpectator(Vector3 feet, out PlayerMatchState state, out SpectatorController spectator)
    {
        var go = new GameObject("SpectateurDeTest");
        go.SetActive(false);
        go.layer = LayerMask.NameToLayer("Player");
        go.AddComponent<CharacterController>();
        go.AddComponent<PlayerInputReader>();
        state = go.AddComponent<PlayerMatchState>();
        spectator = go.AddComponent<SpectatorController>();
        PlayerLocomotion locomotion = go.AddComponent<PlayerLocomotion>();
        go.transform.position = feet;
        go.SetActive(true);
        created.Add(go);
        return locomotion;
    }

    // Des [Test] synchrones, et non des [UnityTest] : hors session, l'Update d'un PlayerLocomotion le
    // traite en pur spectateur (cas 4) et le replacerait à l'origine à chaque image.
    [Test]
    public void LeGardeCorps_RetientLeSpectateur_QuandUnDuellisteLeFranchirait()
    {
        Box("Sol", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
        // Un garde-corps de 1 m de haut, sous la borne d'un franchissement (1,3 m). Plus épais que celui
        // de la tribune (0,12 m) : la sonde du vault cherche le dessus 15 cm derrière la face, si bien
        // qu'un garde-corps fin ne se franchit pas, quel que soit le rôle. Ici, seul le rôle retient.
        Box("GardeCorps", new Vector3(0f, 0.5f, 2f), new Vector3(6f, 1f, 0.3f));
        Physics.SyncTransforms();

        PlayerLocomotion spectator = NewSpectator(new Vector3(0f, 0.1f, 0f), out PlayerMatchState state, out _);
        // « NetworkVariable is written to, but doesn't know its NetworkBehaviour yet » : attendu hors session.
        state.TestSetRole(PlayerRole.Spectator);
        Assert.IsTrue(spectator.IsSpectator, "Garde : le joueur de test est bien spectateur.");

        for (int i = 0; i < 60; i++) spectator.TestMove(Vector2.zero, 0f, false, false, false, false, 0, Dt);
        for (int i = 0; i < 180; i++) spectator.TestMove(Vector2.up, 0f, false, false, false, i % 10 == 0, 0, Dt);
        Assert.Less(spectator.transform.position.z, 1.85f, "Le spectateur bute contre le garde-corps, saut compris : il ne tombe jamais dans l'arène.");
        Assert.Greater(spectator.transform.position.z, 1f, "Garde : il a bien marché jusqu'au garde-corps.");

        // Le même joueur, duelliste : le garde-corps est assez bas pour qu'il le franchisse. C'est donc
        // bien son rôle qui retient le spectateur, pas la hauteur seule.
        state.TestSetRole(PlayerRole.Duelist);
        for (int i = 0; i < 120 && spectator.transform.position.z < 2.3f; i++)
            spectator.TestMove(Vector2.up, 0f, false, false, false, true, 0, Dt);
        Assert.Greater(spectator.transform.position.z, 2.3f, "Un duelliste franchit un obstacle de 1 m (franchissement jusqu'à 1,3 m).");
    }

    [Test]
    public void UnSpectateurEtourdi_NeBougePlus_PuisRepart()
    {
        Box("Sol", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
        Physics.SyncTransforms();
        PlayerLocomotion locomotion = NewSpectator(new Vector3(0f, 0.1f, 0f), out PlayerMatchState state, out SpectatorController spectator);
        state.TestSetRole(PlayerRole.Spectator);
        for (int i = 0; i < 60; i++) locomotion.TestMove(Vector2.zero, 0f, false, false, false, false, 0, Dt);

        // Hors session, l'instant de simulation des tests est 0 : étourdi jusqu'à 10, il l'est.
        spectator.TestSetStunnedUntil(10.0);
        Vector3 before = locomotion.transform.position;
        for (int i = 0; i < 60; i++) locomotion.TestMove(Vector2.up, 5f, true, false, false, false, 0, Dt);
        Assert.Less(Vector3.Distance(before, locomotion.transform.position), 0.01f, "Étourdi : il ne se déplace plus.");
        Assert.AreNotEqual(0f, Quaternion.Angle(Quaternion.identity, locomotion.transform.rotation), "Il peut toujours regarder autour de lui.");

        spectator.TestSetStunnedUntil(0.0);
        for (int i = 0; i < 60; i++) locomotion.TestMove(Vector2.up, 0f, false, false, false, false, 0, Dt);
        Assert.Greater(Vector3.Distance(before, locomotion.transform.position), 1f, "L'étourdissement fini, il repart.");
    }
}
