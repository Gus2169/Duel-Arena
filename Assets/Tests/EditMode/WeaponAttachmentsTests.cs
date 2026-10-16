using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Les accessoires des armes (2026-10-16, GDD § 6) : les dix viseurs de Rainbow Six, la poignée
/// verticale ou inclinée, le laser tactique et le chargeur grande capacité, choisis à l'armurerie,
/// annoncés en se connectant, bornés par le serveur, et qui CHANGENT l'arme.
///
/// Ce qui casse ici ne lève aucune erreur : un serveur qui laisse un client modifié poser un viseur
/// sur le sniper ou une poignée sur un pistolet, un chargeur que le joueur et le serveur ne comptent
/// pas pareil, une ancienne build lue de travers, une poignée qui retient le mauvais recul, un viseur
/// rangé à la place d'un autre, une hausse qui reste dans la fenêtre du viseur, un chargeur d'origine
/// qui dépasse sous le grand.
/// </summary>
public class WeaponAttachmentsTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const int Rifle = 0, Sniper = 1, Shotgun = 2, Pistol = 3;
    private const int RedDotA = 1, HoloA = 4, ReflexA = 8, ReflexC = 10;

    private readonly List<GameObject> created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created) if (go != null) Object.DestroyImmediate(go);
        created.Clear();
    }

    private static IReadOnlyList<WeaponData> Loadout() =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<WeaponController>(true).Loadout;

    private static AttachmentCatalog Catalog() =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<AttachmentCatalog>(AttachmentImporter.CatalogPath);

    private static GameObject ModelPrefab(string code) =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"{WeaponImporter.Root}/{code}/{code}.prefab");

    private Transform Instantiate(string code)
    {
        GameObject prefab = ModelPrefab(code);
        Assert.IsNotNull(prefab, $"Le modèle {code} n'est plus là.");
        var go = Object.Instantiate(prefab);
        created.Add(go);
        return go.transform;
    }

    private static bool Shown(Transform weapon, string part)
    {
        Transform t = weapon.Find(part);
        Assert.IsNotNull(t, $"{weapon.name} n'a plus de pièce « {part} ».");
        return t.gameObject.activeSelf;
    }

    // ------------------------------------------------------------------
    // Le choix, le serveur, la connexion
    // ------------------------------------------------------------------

    [Test]
    public void LesAccessoires_GardentUnChoixParRubriqueEtParArme_SansToucherAuxAutres()
    {
        WeaponAttachments a = WeaponAttachments.None;
        for (int slot = 0; slot < WeaponAttachments.Slots; slot++)
        {
            Assert.AreEqual(0, a.Sight(slot), "Sans choix : la mire de l'arme.");
            Assert.AreEqual(GripKind.None, a.Grip(slot));
            Assert.IsFalse(a.Laser(slot));
            Assert.IsFalse(a.ExtendedMagazine(slot));
        }

        a = a.With(Rifle, HoloA, GripKind.Vertical, true, true).With(Pistol, ReflexC, GripKind.None, false, true)
             .WithGrip(Sniper, GripKind.Angled);
        Assert.AreEqual(HoloA, a.Sight(Rifle));
        Assert.AreEqual(GripKind.Vertical, a.Grip(Rifle));
        Assert.IsTrue(a.Laser(Rifle));
        Assert.IsTrue(a.ExtendedMagazine(Rifle));
        Assert.AreEqual(ReflexC, a.Sight(Pistol), "Le dixième viseur tient dans ses quatre bits.");
        Assert.IsFalse(a.Laser(Pistol));
        Assert.IsTrue(a.ExtendedMagazine(Pistol));
        Assert.AreEqual(GripKind.Angled, a.Grip(Sniper));
        Assert.AreEqual(0, a.Sight(Sniper), "Une rubrique change sans toucher aux autres.");
        Assert.AreEqual(WeaponAttachments.None.Packed, (a.Packed >> 16) & 0xFFu, "La place 3 n'a rien reçu.");

        // Une rubrique à la fois, par les choix de l'armurerie : les autres restent.
        WeaponAttachments b = a.WithVariant(Rifle, AttachmentCategory.Sight, RedDotA).WithVariant(Rifle, AttachmentCategory.Laser, 0);
        Assert.AreEqual(RedDotA, b.Variant(Rifle, AttachmentCategory.Sight));
        Assert.AreEqual(0, b.Variant(Rifle, AttachmentCategory.Laser));
        Assert.AreEqual((int)GripKind.Vertical, b.Variant(Rifle, AttachmentCategory.Grip));
        Assert.AreEqual(1, b.Variant(Rifle, AttachmentCategory.Magazine));
        Assert.AreEqual(a.Packed & 0xFFFFFF00u, b.Packed & 0xFFFFFF00u, "Les autres armes n'ont pas bougé.");

        // Hors de la panoplie : rien ne bouge.
        Assert.AreEqual(a, a.With(4, HoloA, GripKind.Angled, true, true));
        Assert.AreEqual(0, a.Sight(-1));
        Assert.AreEqual(WeaponAttachments.SightCount + 1, WeaponAttachments.VariantCount(AttachmentCategory.Sight),
            "Dix viseurs, plus la mire de l'arme.");
    }

    /// <summary>Le serveur ne publie que ce que l'arme prend : un client modifié qui demande un viseur
    /// sur le sniper, un point rouge sur un pistolet, une poignée sur un pistolet ou un accessoire qui
    /// n'existe pas n'a que ce qu'il pouvait avoir.</summary>
    [Test]
    public void LeServeur_FaitTomberCeQueLArmeNePrendPas()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        var asked = WeaponAttachments.None
            .With(Rifle, HoloA, GripKind.Angled, true, true)
            .With(Sniper, RedDotA, GripKind.Vertical, true, true)
            .With(Shotgun, ReflexA, GripKind.Vertical, true, true)
            .With(Pistol, RedDotA, GripKind.Vertical, true, true);
        WeaponAttachments kept = asked.Sanitized(loadout);

        Assert.AreEqual(asked.Packed & 0xFFu, kept.Packed & 0xFFu, "Le fusil d'assaut prend tout.");
        Assert.AreEqual(0, kept.Sight(Sniper), "Le sniper garde sa lunette : pas de viseur.");
        Assert.AreEqual(GripKind.Vertical, kept.Grip(Sniper), "Mais il prend une poignée...");
        Assert.IsTrue(kept.Laser(Sniper), "... un laser...");
        Assert.IsTrue(kept.ExtendedMagazine(Sniper), "... et le chargeur.");
        Assert.AreEqual(asked.Packed & 0xFF0000u, kept.Packed & 0xFF0000u, "Le fusil à pompe prend tout.");
        Assert.AreEqual(0, kept.Sight(Pistol), "Un point rouge ne tient pas sur une glissière de pistolet.");
        Assert.AreEqual(GripKind.None, kept.Grip(Pistol), "Un pistolet n'a pas de poignée avant.");
        Assert.IsTrue(kept.Laser(Pistol));
        Assert.IsTrue(kept.ExtendedMagazine(Pistol));

        var reflex = WeaponAttachments.None.WithSight(Pistol, ReflexA);
        Assert.AreEqual(ReflexA, reflex.Sanitized(loadout).Sight(Pistol), "Un réflex, si.");
        Assert.AreEqual(ReflexC, reflex.WithSight(Pistol, ReflexC).Sanitized(loadout).Sight(Pistol));

        // Tous les bits levés : des viseurs et des poignées qui n'existent pas.
        WeaponAttachments all = new WeaponAttachments(0xFFFFFFFFu).Sanitized(loadout);
        for (int slot = 0; slot < WeaponAttachments.Slots; slot++)
        {
            Assert.AreEqual(0, all.Sight(slot), $"Place {slot + 1} : le viseur 15 n'existe pas.");
            Assert.AreEqual(GripKind.None, all.Grip(slot), $"Place {slot + 1} : la poignée 3 n'existe pas.");
        }
        Assert.AreEqual(kept, kept.Sanitized(loadout), "Le serveur ne touche pas à un choix valable.");
        Assert.AreEqual(WeaponAttachments.None, asked.Sanitized(null), "Sans panoplie, rien.");
    }

    [Test]
    public void LaDemandeDeConnexion_PorteLesAccessoires()
    {
        WeaponLooks looks = WeaponLooks.Default.With(0, 1, WeaponFinish.Polie);
        WeaponAttachments attachments = WeaponAttachments.None.With(Rifle, HoloA, GripKind.Vertical, true, false)
                                                              .With(Pistol, ReflexC, GripKind.None, true, true);
        Assert.IsTrue(SessionFlow.ParsePayload(SessionFlow.BuildPayload("0.1.4", "Héloïse", looks, attachments),
                                               out string version, out string name, out WeaponLooks readLooks, out WeaponAttachments read));
        Assert.AreEqual("0.1.4", version);
        Assert.AreEqual("Héloïse", name);
        Assert.AreEqual(looks, readLooks, "L'apparence se lit toujours.");
        Assert.AreEqual(attachments, read, "Les accessoires arrivent tels qu'ils sont partis.");

        // Une build d'avant les accessoires (0.1.3) : la version, le pseudo et l'apparence. Aucun accessoire.
        Assert.IsTrue(SessionFlow.ParsePayload(SessionFlow.BuildPayload("0.1.3", "Untel", looks), out _, out _, out readLooks, out read));
        Assert.AreEqual(looks, readLooks);
        Assert.AreEqual(WeaponAttachments.None, read);
        Assert.IsTrue(SessionFlow.ParsePayload(System.Text.Encoding.UTF8.GetBytes("0.1.4\nUntel\n00000000\nZZ-pas-hexa"),
                                               out _, out _, out _, out read));
        Assert.AreEqual(WeaponAttachments.None, read, "N'importe quoi à la place des accessoires : aucun.");
    }

    /// <summary>Comme le pseudo et l'apparence : deux instances du jeu sur un PC partagent la clé du
    /// registre ; chacune garde ses accessoires.</summary>
    [Test]
    public void LesAccessoires_RestentCeuxDeCetteInstance_QuandUneAutreEnregistreLesSiens()
    {
        WeaponAttachments before = GameSettings.WeaponAttachments;
        int stored = PlayerPrefs.GetInt(GameSettings.WeaponAttachmentsKey, 0);
        try
        {
            WeaponAttachments mine = WeaponAttachments.None.With(Rifle, HoloA, GripKind.Vertical, true, true);
            GameSettings.WeaponAttachments = mine;
            PlayerPrefs.SetInt(GameSettings.WeaponAttachmentsKey, unchecked((int)WeaponAttachments.None.WithSight(Rifle, RedDotA).Packed));
            Assert.AreEqual(mine, GameSettings.WeaponAttachments, "L'autre instance a écrit les siens : celle-ci garde les siens.");
        }
        finally
        {
            GameSettings.WeaponAttachments = before;
            PlayerPrefs.SetInt(GameSettings.WeaponAttachmentsKey, stored);
            PlayerPrefs.Save();
        }
    }

    // ------------------------------------------------------------------
    // Les effets
    // ------------------------------------------------------------------

    [Test]
    public void LeChargeurGrandeCapacite_AjouteLaMoitieDeBalles_EtAllongeUnPeuLeRechargement()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        AttachmentCatalog catalog = Catalog();
        int[] expected = { 45, 8, 9, 18 };   // 30, 5, 6 et 12 balles d'origine
        for (int slot = 0; slot < loadout.Count; slot++)
        {
            WeaponData data = loadout[slot];
            Assert.AreEqual(data.magazineSize, AttachmentCatalog.MagazineSize(data, false, catalog), $"{data.weaponName} : le chargeur d'origine.");
            Assert.AreEqual(expected[slot], AttachmentCatalog.MagazineSize(data, true, catalog), $"{data.weaponName} : le grand chargeur.");
            Assert.AreEqual(data.magazineSize, AttachmentCatalog.MagazineSize(data, true, null), "Sans catalogue, aucun effet.");
            Assert.AreEqual(data.reloadDuration, AttachmentCatalog.ReloadDuration(data, false, catalog), 1e-6f);
        }

        WeaponData rifle = loadout[Rifle];
        Assert.AreEqual(rifle.reloadDuration * catalog.extendedMagazineReload, AttachmentCatalog.ReloadDuration(rifle, true, catalog), 1e-5f,
            "Un chargeur plus long se change moins vite.");
        Assert.Greater(catalog.extendedMagazineReload, 1f);
        WeaponData shotgun = loadout[Shotgun];
        Assert.IsTrue(shotgun.reloadPerRound, "Le fusil à pompe recharge cartouche par cartouche.");
        Assert.AreEqual(shotgun.reloadDuration, AttachmentCatalog.ReloadDuration(shotgun, true, catalog), 1e-6f,
            "Une cartouche se glisse aussi vite : le tube en prend seulement plus.");
    }

    [Test]
    public void LaPoigneeVerticale_RetientLeReculVertical_EtLeLaserLeReculALaHanche()
    {
        AttachmentCatalog catalog = Catalog();
        var grip = WeaponAttachments.None.WithGrip(Rifle, GripKind.Vertical);
        AttachmentCatalog.RecoilFactors(catalog, grip, Rifle, true, out float vertical, out float horizontal);
        Assert.AreEqual(catalog.verticalGripRecoil, vertical, 1e-6f, "La poignée verticale retient le recul vertical...");
        Assert.AreEqual(1f, horizontal, 1e-6f, "... et rien d'autre.");
        Assert.Less(catalog.verticalGripRecoil, 1f);

        var laser = WeaponAttachments.None.WithLaser(Rifle, true);
        AttachmentCatalog.RecoilFactors(catalog, laser, Rifle, false, out vertical, out horizontal);
        Assert.AreEqual(catalog.laserHipRecoil, vertical, 1e-6f, "À la hanche, le laser retient tout le recul.");
        Assert.AreEqual(catalog.laserHipRecoil, horizontal, 1e-6f);
        AttachmentCatalog.RecoilFactors(catalog, laser, Rifle, true, out vertical, out horizontal);
        Assert.AreEqual(1f, vertical, 1e-6f, "En visée, le laser n'aide plus.");
        Assert.AreEqual(1f, horizontal, 1e-6f);

        AttachmentCatalog.RecoilFactors(catalog, grip.WithLaser(Rifle, true), Rifle, false, out vertical, out horizontal);
        Assert.AreEqual(catalog.verticalGripRecoil * catalog.laserHipRecoil, vertical, 1e-6f, "Les deux s'ajoutent.");

        AttachmentCatalog.RecoilFactors(catalog, grip, Sniper, true, out vertical, out _);
        Assert.AreEqual(1f, vertical, 1e-6f, "La poignée d'une arme ne retient pas le recul d'une autre.");
        AttachmentCatalog.RecoilFactors(null, grip, Rifle, true, out vertical, out _);
        Assert.AreEqual(1f, vertical, 1e-6f, "Sans catalogue, aucun effet.");
    }

    [Test]
    public void LaPoigneeInclinee_FaitEpaulerPlusVite()
    {
        AttachmentCatalog catalog = Catalog();
        WeaponData rifle = Loadout()[Rifle];
        Assert.AreEqual(rifle.aimTime, AttachmentCatalog.AimTime(rifle, GripKind.None, catalog), 1e-6f);
        Assert.AreEqual(rifle.aimTime, AttachmentCatalog.AimTime(rifle, GripKind.Vertical, catalog), 1e-6f, "La poignée verticale n'y change rien.");
        Assert.AreEqual(rifle.aimTime * catalog.angledGripAimTime, AttachmentCatalog.AimTime(rifle, GripKind.Angled, catalog), 1e-6f);
        Assert.Less(catalog.angledGripAimTime, 1f);
    }

    // ------------------------------------------------------------------
    // Le catalogue et la panoplie
    // ------------------------------------------------------------------

    /// <summary>Le catalogue range les viseurs dans l'ordre de WeaponAttachments, celui de Rainbow Six,
    /// et chaque viseur dessine le réticule de sa fiche ; chaque arme de la panoplie le porte et ne
    /// prend que ce que dit le GDD (§ 6).</summary>
    [Test]
    public void LeCatalogue_RangeLesViseursDansLOrdre_EtChaqueArmeLePorte()
    {
        AttachmentCatalog catalog = Catalog();
        Assert.IsNotNull(catalog, $"Pas de catalogue en {AttachmentImporter.CatalogPath}.");
        Assert.AreEqual(WeaponAttachments.SightCount, catalog.sights.Length);
        for (int sight = 1; sight <= WeaponAttachments.SightCount; sight++)
        {
            WeaponAttachment a = catalog.SightPrefab(sight).GetComponent<WeaponAttachment>();
            Assert.AreEqual(AttachmentImporter.SightOrder[sight - 1], a.Code, $"Le viseur {sight} n'est pas à sa place.");
            Assert.AreEqual(AttachmentCategory.Sight, a.Category);
            Assert.IsNotNull(a.Sight, $"{a.Code} n'a pas de mire.");
            Assert.IsNotNull(a.Lens, $"{a.Code} n'a pas de vitre.");
            Material lens = a.Lens.sharedMaterial;
            Assert.AreEqual(AttachmentImporter.LensShaderName, lens.shader.name, $"La vitre de {a.Code} ne dessine pas de réticule.");
            Assert.Greater(lens.GetFloat("_Rayon"), 0f);
            CollectionAssert.IsSubsetOf(new[] { "Hausse", "Guidon" }, new List<string>(a.Hides), $"{a.Code} doit cacher la hausse et le guidon.");
        }
        Assert.AreEqual("PR-A", catalog.SightPrefab(RedDotA).GetComponent<WeaponAttachment>().Code);
        Assert.AreEqual("RX-A", catalog.SightPrefab(ReflexA).GetComponent<WeaponAttachment>().Code);
        Assert.AreEqual(SightFamilies.RedDot, WeaponAttachments.FamilyOf(RedDotA));
        Assert.AreEqual(SightFamilies.Holographic, WeaponAttachments.FamilyOf(HoloA));
        Assert.AreEqual(SightFamilies.Reflex, WeaponAttachments.FamilyOf(ReflexA));
        Assert.AreEqual("Mire de l'arme", catalog.SightTitle(0));

        Assert.AreEqual(AttachmentCategory.Laser, catalog.laser.GetComponent<WeaponAttachment>().Category);
        Assert.IsNotNull(catalog.laser.GetComponent<WeaponAttachment>().Beam, "Le laser n'a pas d'émetteur.");
        Assert.IsNotNull(catalog.pistolLaser.GetComponent<WeaponAttachment>().Beam, "Le laser de poing n'a pas d'émetteur.");
        Assert.IsNotNull(catalog.verticalGrip.GetComponent<WeaponAttachment>().LeftHand, "La poignée verticale n'a pas de prise.");
        Assert.IsNotNull(catalog.angledGrip.GetComponent<WeaponAttachment>().LeftHand, "La poignée inclinée n'a pas de prise.");
        Assert.IsNotNull(catalog.beamMaterial);
        Assert.IsNotNull(catalog.dotMaterial);

        IReadOnlyList<WeaponData> loadout = Loadout();
        foreach (WeaponData data in loadout)
        {
            Assert.AreSame(catalog, data.attachmentCatalog, $"{data.weaponName} ne porte pas le catalogue : ses accessoires n'auraient aucun effet.");
            // Chaque modèle qui a un chargeur a son grand chargeur, sauf le barillet du RV-30.
            for (int model = 0; model < data.ModelCount; model++)
            {
                WeaponModel m = data.ModelPrefabFor(model).GetComponent<WeaponModel>();
                GameObject magazine = catalog.MagazinePrefab(m.Designation);
                if (m.Designation == "RV-30") Assert.IsNull(magazine, "Un barillet ne s'allonge pas.");
                else Assert.IsNotNull(magazine, $"{m.Designation} n'a pas de chargeur grande capacité.");
            }
        }
        Assert.AreEqual(AttachmentSlots.All, loadout[Rifle].acceptedAttachments);
        Assert.AreEqual(AttachmentSlots.Grip | AttachmentSlots.Laser | AttachmentSlots.Magazine, loadout[Sniper].acceptedAttachments);
        Assert.AreEqual(AttachmentSlots.All, loadout[Shotgun].acceptedAttachments);
        Assert.AreEqual(AttachmentSlots.Sight | AttachmentSlots.Laser | AttachmentSlots.Magazine, loadout[Pistol].acceptedAttachments);
        Assert.AreEqual(SightFamilies.Reflex, loadout[Pistol].acceptedSights);
    }

    /// <summary>La mire de chaque viseur passe par le milieu de sa vitre, devant elle : en visée, on
    /// regarde au travers, et le réticule (à l'infini, sur l'axe) tombe au centre de la fenêtre.</summary>
    [Test]
    public void LaMireDeChaqueViseur_PasseParLeMilieuDeSaVitre()
    {
        AttachmentCatalog catalog = Catalog();
        for (int sight = 1; sight <= WeaponAttachments.SightCount; sight++)
        {
            GameObject prefab = catalog.SightPrefab(sight);
            WeaponAttachment a = prefab.GetComponent<WeaponAttachment>();
            Mesh mesh = a.Lens.GetComponent<MeshFilter>().sharedMesh;
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix * a.Lens.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            var pane = new Bounds(toRoot.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            foreach (Vector3 v in vertices) pane.Encapsulate(toRoot.MultiplyPoint3x4(v));
            Vector3 mire = prefab.transform.InverseTransformPoint(a.Sight.position);

            Assert.Less(Mathf.Abs(mire.x - pane.center.x), 0.001f, $"{a.Code} : la mire n'est pas au milieu de la vitre (horizontalement).");
            Assert.Less(Mathf.Abs(mire.y - pane.center.y), 0.001f, $"{a.Code} : la mire n'est pas au milieu de la vitre (verticalement).");
            Assert.Greater(pane.size.x, 0.012f, $"{a.Code} : une vitre de moins de 12 mm.");
            Assert.Less(mire.z, pane.center.z, $"{a.Code} : la vitre doit être devant la mire.");
            Assert.Less(Quaternion.Angle(Quaternion.identity, a.Sight.localRotation), 0.01f, $"{a.Code} : la mire doit regarder dans l'axe du canon.");
        }
    }

    // ------------------------------------------------------------------
    // Le montage sur l'arme (AttachmentMount)
    // ------------------------------------------------------------------

    [Test]
    public void UnViseurMonte_CacheLesOrganesDeVisee_EtLesRendQuandIlPart()
    {
        WeaponData rifle = Loadout()[Rifle];
        Transform weapon = Instantiate("FA07");
        WeaponModel model = weapon.GetComponent<WeaponModel>();

        AttachmentMount.Apply(weapon, rifle, WeaponAttachments.None.WithSight(Rifle, HoloA), Rifle, WeaponFinish.Origine);
        WeaponAttachment sight = AttachmentMount.Mounted(weapon, AttachmentCategory.Sight);
        Assert.IsNotNull(sight, "Le holographique A n'est pas monté.");
        Assert.AreEqual("HO-A", sight.Code);
        Assert.AreSame(AttachmentMount.Socket(weapon, AttachmentCategory.Sight), sight.transform.parent, "Monté ailleurs que sur le rail.");
        Assert.Less(sight.transform.localPosition.magnitude, 1e-6f, "Le préfab a son origine au point d'accroche.");
        Assert.IsFalse(Shown(weapon, "Hausse"), "La hausse resterait dans la fenêtre du viseur.");
        Assert.IsFalse(Shown(weapon, "Guidon"));
        Assert.IsFalse(Shown(weapon, "Guidon_Base"), "La tour du guidon se voyait sous le réticule.");
        Assert.IsTrue(Shown(weapon, "Chargeur_Standard"), "Un viseur ne touche pas au chargeur.");
        Assert.AreSame(sight.Sight, AttachmentMount.SightOf(model, out float eye), "En visée, l'œil va derrière le viseur...");
        Assert.AreEqual(sight.AimEyeDistance, eye, 1e-6f, "... à sa distance.");

        // Un autre viseur prend sa place, sans doublon.
        AttachmentMount.Apply(weapon, rifle, WeaponAttachments.None.WithSight(Rifle, RedDotA), Rifle, WeaponFinish.Origine);
        Assert.AreEqual(1, weapon.GetComponentsInChildren<WeaponAttachment>().Length, "Deux viseurs sur le même rail.");
        Assert.AreEqual("PR-A", AttachmentMount.Mounted(weapon, AttachmentCategory.Sight).Code);

        // Plus de viseur : la mire de l'arme revient.
        AttachmentMount.Apply(weapon, rifle, WeaponAttachments.None, Rifle, WeaponFinish.Origine);
        Assert.IsNull(AttachmentMount.Mounted(weapon, AttachmentCategory.Sight));
        Assert.AreEqual(0, weapon.GetComponentsInChildren<WeaponAttachment>(true).Length);
        Assert.IsTrue(Shown(weapon, "Hausse"), "La hausse doit revenir quand le viseur part.");
        Assert.IsTrue(Shown(weapon, "Guidon"));
        Assert.IsTrue(Shown(weapon, "Guidon_Base"));
        Assert.AreSame(model.Sight, AttachmentMount.SightOf(model, out eye));
        Assert.AreEqual(model.AimEyeDistance, eye, 1e-6f);
    }

    [Test]
    public void LeGrandChargeur_RemplaceCeluiDOrigine_SaufLaRallongeDUnTube()
    {
        Transform rifle = Instantiate("FA07");
        AttachmentMount.Apply(rifle, Loadout()[Rifle], WeaponAttachments.None.WithMagazine(Rifle, true), Rifle, WeaponFinish.Origine);
        Assert.AreEqual("CG-FA07", AttachmentMount.Mounted(rifle, AttachmentCategory.Magazine).Code, "Le FA-07 a son grand chargeur.");
        Assert.IsFalse(Shown(rifle, "Chargeur_Standard"), "Le chargeur d'origine dépasserait sous le grand.");
        Assert.IsTrue(Shown(rifle, "Hausse"));

        Transform shotgun = Instantiate("FAP03");
        AttachmentMount.Apply(shotgun, Loadout()[Shotgun], WeaponAttachments.None.WithMagazine(Shotgun, true), Shotgun, WeaponFinish.Origine);
        Assert.AreEqual("CG-FAP03", AttachmentMount.Mounted(shotgun, AttachmentCategory.Magazine).Code);
        Assert.IsTrue(Shown(shotgun, "Tube_Magasin"), "La rallonge s'ajoute au tube, elle ne le remplace pas.");

        // Le chargeur d'un autre modèle ne se monte pas : le FA-30 a le sien.
        Transform other = Instantiate("FA30");
        AttachmentMount.Apply(other, Loadout()[Rifle], WeaponAttachments.None.WithMagazine(Rifle, true), Rifle, WeaponFinish.Origine);
        Assert.AreEqual("CG-FA30", AttachmentMount.Mounted(other, AttachmentCategory.Magazine).Code);
    }

    [Test]
    public void UnPistolet_PrendLeLaserDePoingEtUnReflex_JamaisDePoignee()
    {
        WeaponData pistol = Loadout()[Pistol];
        Transform weapon = Instantiate("FX4");
        // Ce que le serveur n'aurait pas publié (une poignée, un point rouge) ne se monte pas non plus.
        var asked = WeaponAttachments.None.With(Pistol, RedDotA, GripKind.Vertical, true, false);
        AttachmentMount.Apply(weapon, pistol, asked, Pistol, WeaponFinish.Origine);
        Assert.AreEqual("LT-P", AttachmentMount.Mounted(weapon, AttachmentCategory.Laser).Code, "Sous le canon d'un pistolet, le laser de poing.");
        Assert.IsNull(AttachmentMount.Mounted(weapon, AttachmentCategory.Sight), "Un point rouge ne tient pas sur la glissière.");
        Assert.IsNull(AttachmentMount.Mounted(weapon, AttachmentCategory.Grip));
        Assert.IsTrue(Shown(weapon, "Hausse"));

        AttachmentMount.Apply(weapon, pistol, asked.WithSight(Pistol, ReflexA), Pistol, WeaponFinish.Origine);
        WeaponAttachment reflex = AttachmentMount.Mounted(weapon, AttachmentCategory.Sight);
        Assert.AreEqual("RX-A", reflex.Code);
        Assert.IsFalse(Shown(weapon, "Hausse"));
        Assert.AreSame(reflex.Sight, AttachmentMount.SightOf(weapon.GetComponent<WeaponModel>(), out float eye));
        Assert.AreEqual(weapon.GetComponent<WeaponModel>().AimEyeDistance, eye, 1e-6f, "Un pistolet se vise toujours à bout de bras.");

        // Le barillet du RV-30 n'a pas de modèle de grand chargeur : rien n'est monté, l'effet vaut.
        Transform revolver = Instantiate("RV30");
        AttachmentMount.Apply(revolver, pistol, WeaponAttachments.None.WithMagazine(Pistol, true), Pistol, WeaponFinish.Origine);
        Assert.IsNull(AttachmentMount.Mounted(revolver, AttachmentCategory.Magazine));
        Assert.AreEqual(18, AttachmentCatalog.MagazineSize(pistol, true, pistol.attachmentCatalog));
    }

    [Test]
    public void LeSniper_GardeSaLunette_EtLesAccessoiresPrennentLaFinitionDeLArme()
    {
        WeaponData sniper = Loadout()[Sniper];
        Transform weapon = Instantiate("FP12");
        var asked = WeaponAttachments.None.With(Sniper, HoloA, GripKind.Angled, true, true);
        AttachmentMount.Apply(weapon, sniper, asked, Sniper, WeaponFinish.Rouillee);
        Assert.IsNull(AttachmentMount.Mounted(weapon, AttachmentCategory.Sight), "Le sniper garde sa lunette.");
        Assert.IsTrue(Shown(weapon, "Lunette_Standard"));
        Assert.AreSame(weapon.GetComponent<WeaponModel>().Sight, AttachmentMount.SightOf(weapon.GetComponent<WeaponModel>(), out _));

        foreach (AttachmentCategory category in new[] { AttachmentCategory.Grip, AttachmentCategory.Laser, AttachmentCategory.Magazine })
        {
            WeaponAttachment a = AttachmentMount.Mounted(weapon, category);
            Assert.IsNotNull(a, $"Le sniper prend {category}.");
            Assert.AreEqual(WeaponFinish.Rouillee, a.GetComponent<WeaponFinishes>().Shown, $"{a.Code} : la finition de l'arme.");
        }
        AttachmentMount.Apply(weapon, sniper, asked, Sniper, WeaponFinish.Polie);
        Assert.AreEqual(WeaponFinish.Polie, AttachmentMount.Mounted(weapon, AttachmentCategory.Laser).GetComponent<WeaponFinishes>().Shown,
            "Une autre finition passe aux accessoires déjà montés.");
        Assert.AreEqual(3, weapon.GetComponentsInChildren<WeaponAttachment>().Length, "Rien n'est monté en double.");
    }
}
