using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ce que le tir montre et ce qui l'entoure (2026-10-16, retours de l'utilisateur) : le laser droit dans
/// l'axe du canon, la flamme de bouche à la place du traçant des armes à balles, pas de visée pendant
/// un rechargement, Échap dans le menu.
///
/// Ce qui casse ici ne lève aucune erreur : un faisceau qui part des bras mais s'arrête ailleurs que là
/// où le joueur le voit (les bras sont dessinés à un autre champ que le monde), une arme à balles qui
/// retrouve son traçant ou perd sa flamme, une visée qui fait entrer la caméra dans l'arme pendant le
/// rechargement, un menu dont on ne sort pas.
/// </summary>
public class ShotFeedbackTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const int Rifle = 0, Sniper = 1, Shotgun = 2, Pistol = 3;

    private readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in created) if (o != null) Object.DestroyImmediate(o);
        created.Clear();
    }

    private static GameObject PlayerPrefab => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

    private static IReadOnlyList<WeaponData> Loadout() => PlayerPrefab.GetComponentInChildren<WeaponController>(true).Loadout;

    /// <summary>
    /// Le joueur voit son faisceau partir de ses bras, dessinés par une caméra au champ plus étroit que le
    /// monde. Le rayon cherché dans le monde est celui qu'il voit : chacun de ses points tombe, ramené
    /// dans le champ des bras (ElectricArc.WorldToArms), sur le faisceau des bras.
    /// </summary>
    [Test]
    public void LeFaisceauDuJoueur_SeChercheDansLeMondeLaOuIlLeVoit()
    {
        var eyeObject = new GameObject("Oeil");
        created.Add(eyeObject);
        Transform eye = eyeObject.transform;
        eye.SetPositionAndRotation(new Vector3(3f, 1.6f, -2f), Quaternion.Euler(8f, 35f, 0f));
        const float playerFov = 74f, armsFov = 60f;

        Vector3 armsOrigin = eye.TransformPoint(new Vector3(0.14f, -0.11f, 0.38f));
        Vector3 armsDirection = eye.TransformDirection(new Vector3(-0.03f, 0.02f, 1f).normalized);
        TacticalLaser.ArmsRayToWorld(armsOrigin, armsDirection, eye, playerFov, armsFov, out Vector3 origin, out Vector3 direction);

        Assert.AreEqual(1f, direction.magnitude, 1e-4f);
        Assert.Greater(Vector3.Angle(armsDirection, direction), 0.01f, "Garde : les deux champs diffèrent, le rayon aussi.");
        Assert.Less(Vector3.Distance(armsOrigin, ElectricArc.WorldToArms(origin, eye, playerFov, armsFov)), 1e-4f, "L'émetteur se voit là où le faisceau part.");
        foreach (float distance in new[] { 0.5f, 4f, 30f, 120f })
        {
            Vector3 seen = ElectricArc.WorldToArms(origin + direction * distance, eye, playerFov, armsFov);
            Vector3 fromArms = seen - armsOrigin;
            float offLine = Vector3.Cross(fromArms, armsDirection).magnitude;
            Assert.Less(offLine, 1e-3f * Mathf.Max(1f, distance), $"À {distance} m, le point du monde se voit sur le faisceau des bras.");
            Assert.Greater(Vector3.Dot(fromArms, armsDirection), 0f, "Devant l'émetteur, pas derrière.");
        }

        TacticalLaser.ArmsRayToWorld(armsOrigin, armsDirection, eye, armsFov, armsFov, out origin, out direction);
        Assert.Less(Vector3.Distance(armsOrigin, origin), 1e-4f, "Au même champ, rien ne change.");
        Assert.AreEqual(0f, Vector3.Angle(armsDirection, direction), 1e-3f);
    }

    /// <summary>
    /// Le laser est réglé sur la ligne de visée (2026-10-26) : son faisceau, parti de l'émetteur sous le
    /// viseur et à droite, passe par le point que vise la mire, à la distance de ce qu'elle touche. En
    /// visée, le point tombe donc au centre du réticule, de près comme de loin ; parallèle au canon, il
    /// s'en écartait de l'écart de l'émetteur à la mire.
    /// </summary>
    [Test]
    public void LeLaser_SeRegleSurLaLigneDeVisee()
    {
        var weaponObject = new GameObject("Arme");
        created.Add(weaponObject);
        Transform weapon = weaponObject.transform;
        weapon.SetPositionAndRotation(new Vector3(-2f, 1.4f, 5f), Quaternion.Euler(-4f, 120f, 3f));
        Vector3 sightOrigin = weapon.TransformPoint(new Vector3(0f, 0.07f, -0.06f));
        Vector3 emitter = weapon.TransformPoint(new Vector3(0.031f, -0.004f, 0.18f));   // rail de droite, sous la mire
        Vector3 sightDirection = weapon.forward;

        foreach (float distance in new[] { 1f, 3f, 7.5f, 40f })
        {
            Vector3 direction = TacticalLaser.ZeroedDirection(emitter, sightOrigin, sightDirection, distance);
            Vector3 target = sightOrigin + sightDirection * distance;
            Vector3 toTarget = target - emitter;
            Assert.AreEqual(1f, direction.magnitude, 1e-4f);
            Assert.Less(Vector3.Cross(toTarget, direction).magnitude, 1e-4f, $"À {distance} m, le faisceau passe par le point visé.");
            Assert.Greater(Vector3.Dot(toTarget, direction), 0f, "Vers l'avant.");
            // Garde : l'émetteur est bien décalé, un faisceau parallèle au canon passerait à côté.
            Assert.Greater(Vector3.Cross(toTarget, sightDirection).magnitude, 0.02f);
        }
        Assert.AreEqual(sightDirection, TacticalLaser.ZeroedDirection(sightOrigin, sightOrigin, sightDirection, 0f),
                        "Sur le point visé lui-même, il reste dans l'axe.");
    }

    [Test]
    public void LeLaser_VaLaOuRegardeLeJoueur_SaufPendantUnGesteQuiEcarteLArme()
    {
        Assert.IsTrue(TacticalLaser.FollowsGaze(false, false, false, false, false), "L'arme tenue, à la hanche comme en visée.");
        Assert.IsFalse(TacticalLaser.FollowsGaze(true, false, false, false, false), "Il recharge.");
        Assert.IsFalse(TacticalLaser.FollowsGaze(false, true, false, false, false), "Il sort son arme.");
        Assert.IsFalse(TacticalLaser.FollowsGaze(false, false, true, false, false), "Il sprinte.");
        Assert.IsFalse(TacticalLaser.FollowsGaze(false, false, false, true, false), "Il franchit.");
        Assert.IsFalse(TacticalLaser.FollowsGaze(false, false, false, false, true), "Il frappe au gant.");
    }

    [Test]
    public void DOuQuePartLeFaisceau_IlVaAuPointQueRegardeLeJoueur()
    {
        // La même pose vue de deux machines : l'arme des bras à hauteur d'épaule, celle du corps baissée vers le sol.
        Vector3 gazePoint = new Vector3(-4f, 1.65f, 25.5f);
        var emitters = new[] { new Vector3(-3.8f, 1.35f, 12.4f), new Vector3(-3.6f, 0.95f, 12.3f) };
        Vector3 lowered = new Vector3(0.3f, -0.8f, 0.5f).normalized;
        foreach (Vector3 emitter in emitters)
        {
            Vector3 direction = TacticalLaser.TowardGaze(emitter, lowered, gazePoint, 1f);
            Vector3 toPoint = gazePoint - emitter;
            Assert.AreEqual(1f, direction.magnitude, 1e-4f);
            Assert.Less(Vector3.Cross(toPoint, direction).magnitude, 1e-3f, "Le faisceau passe par le point.");
            Assert.Greater(Vector3.Dot(toPoint, direction), 0f, "Vers lui.");
        }

        Assert.AreEqual(lowered, TacticalLaser.TowardGaze(emitters[0], lowered, gazePoint, 0f), "Pendant un geste, il suit l'arme.");
        Vector3 toward = (gazePoint - emitters[0]).normalized;
        Vector3 half = TacticalLaser.TowardGaze(emitters[0], lowered, gazePoint, 0.5f);
        Assert.AreEqual(Vector3.Angle(lowered, toward) * 0.5f, Vector3.Angle(lowered, half), 0.1f, "Il passe de l'un à l'autre sans saut.");
        Assert.AreEqual(lowered, TacticalLaser.TowardGaze(gazePoint, lowered, gazePoint, 1f), "Sur le point lui-même, il garde sa direction.");
    }

    [Test]
    public void EnRechargeant_OnNeVisePas()
    {
        Assert.IsTrue(PlayerLocomotion.GateAim(true, false), "Hors rechargement, la visée passe.");
        Assert.IsFalse(PlayerLocomotion.GateAim(true, true), "Pendant un rechargement, non.");
        Assert.IsFalse(PlayerLocomotion.GateAim(false, false));
        Assert.IsFalse(PlayerLocomotion.GateAim(false, true));
    }

    /// <summary>Les armes à balles n'ont plus de traçant : leur tir se voit à la flamme de bouche ; le
    /// raygun garde son rayon. Le préfab du joueur a la flamme et son matériau.</summary>
    [Test]
    public void LesArmesABalles_OntUneFlammeDeBouche_LeRaygunSonRayon()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        foreach (int slot in new[] { Rifle, Sniper, Shotgun })
            Assert.IsFalse(loadout[slot].shotIsBeam, $"{loadout[slot].weaponName} : une arme à balles n'a pas de traçant.");
        Assert.IsTrue(loadout[Pistol].shotIsBeam, "Le raygun tire un rayon, qui se voit.");

        var feedback = PlayerPrefab.GetComponent<WeaponVisualFeedback>();
        Assert.IsNotNull(feedback, "Le joueur montre ses tirs.");
        Assert.IsTrue(feedback.HasMuzzleFlash, "La flamme de bouche a son matériau.");
        var material = (Material)typeof(WeaponVisualFeedback)
            .GetField("muzzleFlashMaterial", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(feedback);
        Assert.AreEqual("DuelArena/FlammeBouche", material.shader.name, "Le matériau de la flamme (couleur prémultipliée).");
        Assert.Greater(feedback.MuzzleFlashDuration, 0f);
        Assert.Less(feedback.MuzzleFlashDuration, 0.2f, "Une flamme, pas une torche.");

        foreach (WeaponData data in loadout)
        foreach (GameObject prefab in Models(data))
            Assert.IsNotNull(prefab.GetComponent<WeaponModel>().Muzzle, $"{prefab.name} : la flamme part de sa bouche.");
    }

    /// <summary>
    /// Le pistolet laser (le FX-4, à la place de l'ancien « Raygun ») et toutes les armes laser tirent un
    /// rayon rouge, la couleur de l'énergie (la lueur des armes laser, les arcs du gant) : il était vert, une
    /// couleur de robot. Aucune couleur de la palette des robots ne doit s'en approcher.
    /// </summary>
    [Test]
    public void LePistolet_TireUnRayonRouge_QuAucunRobotNePorte()
    {
        WeaponData pistol = Loadout()[Pistol];
        Assert.AreEqual("FX-4", pistol.weaponName, "La place du pistolet d'avant la refonte : le pistolet laser.");
        // Toutes les armes laser tirent ce rayon rouge (2026-10-26 : le tir suit l'arme).
        foreach (WeaponData data in Loadout())
            if (data.shotIsBeam) Assert.AreEqual(pistol.tracerColor, data.tracerColor, $"{data.weaponName} : le rouge de l'énergie.");
        Color.RGBToHSV(pistol.tracerColor, out float hue, out float saturation, out float value);
        Assert.That(Mathf.Min(hue, 1f - hue), Is.LessThan(0.03f), "Un rouge.");
        Assert.Greater(saturation, 0.8f, "Franc, pas rose.");
        Assert.Greater(value, 0.8f, "Lumineux : il se voit de loin.");

        IReadOnlyList<Color32> palette = PlayerPrefab.GetComponent<PlayerPaint>().Palette;
        Assert.Greater(palette.Count, 0, "Garde : la palette du préfab.");
        foreach (Color32 paint in palette)
        {
            Color.RGBToHSV(paint, out float paintHue, out _, out _);
            float gap = Mathf.Abs(paintHue - hue);
            Assert.Greater(Mathf.Min(gap, 1f - gap), 0.05f, $"Le rayon se confondrait avec un robot ({paint}).");
        }
    }

    private static IEnumerable<GameObject> Models(WeaponData data)
    {
        if (data.modelPrefab != null) yield return data.modelPrefab;
    }

    /// <summary>Le tir diffusé aux autres : les tampons du serveur servent d'un tir à l'autre, ils peuvent être plus longs
    /// que celui-ci, et seuls ses plombs partent.</summary>
    [Test]
    public void UnTirDiffuse_SeRelitTelQuIlAEteEcrit_SansCeQuiSuitSesPlombs()
    {
        var ends = new[] { new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f), new Vector3(7f, 8f, 9f) };
        var normals = new[] { Vector3.up, Vector3.right, Vector3.forward };
        var packet = new WeaponController.ShotPacket { count = 2, ends = ends, normals = normals };

        using var writer = new FastBufferWriter(256, Allocator.Temp);
        writer.WriteNetworkSerializable(packet);
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out WeaponController.ShotPacket read);

        Assert.AreEqual(2, read.count);
        for (int i = 0; i < 2; i++)
        {
            Assert.AreEqual(ends[i], read.ends[i]);
            Assert.AreEqual(normals[i], read.normals[i]);
        }
        Assert.AreEqual(1 + 2 * 24, writer.Length, "Un octet pour le compte, puis 24 octets par plomb.");
    }

    /// <summary>Le tampon partagé de la réception grandit au besoin : l'éditeur des armes peut donner plus de plombs à une gerbe.</summary>
    [Test]
    public void UnTirDePlusDePlombsQueLeTamponDeReception_SeRelitEnEntier()
    {
        const int count = 40;
        var ends = new Vector3[count];
        var normals = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            ends[i] = new Vector3(i, 0f, 1f);
            normals[i] = new Vector3(0f, i, 1f);
        }

        using var writer = new FastBufferWriter(2048, Allocator.Temp);
        writer.WriteNetworkSerializable(new WeaponController.ShotPacket { count = count, ends = ends, normals = normals });
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out WeaponController.ShotPacket read);

        Assert.AreEqual(count, read.count);
        Assert.AreEqual(ends[count - 1], read.ends[count - 1]);
        Assert.AreEqual(normals[count - 1], read.normals[count - 1]);
    }

    /// <summary>Échap revient d'un seul écran, de partout où c'est sans risque.</summary>
    [Test]
    public void DansLeMenu_EchapRevientDUnEcran()
    {
        Assert.AreEqual("screenTitle", MainMenuController.BackTarget("screenPlay", false), "De l'écran Jouer, à l'écran titre.");
        Assert.AreEqual("screenTitle", MainMenuController.BackTarget("screenOptions", false), "Des Options aussi.");
        Assert.AreEqual("screenTitle", MainMenuController.BackTarget("screenCustomize", false), "De Personnalisation aussi, de ses deux onglets.");
        Assert.IsNull(MainMenuController.BackTarget("screenTitle", false), "L'écran titre ne revient nulle part (Échap y ouvre « Quitter le jeu ? »).");
        Assert.IsNull(MainMenuController.BackTarget("screenLobby", false), "Le salon se quitte par son bouton.");
        Assert.IsNull(MainMenuController.BackTarget("screenOptions", true), "Pendant un remappage, Échap ne fait que l'annuler.");
        Assert.IsNull(MainMenuController.BackTarget(null, false));
    }
}
