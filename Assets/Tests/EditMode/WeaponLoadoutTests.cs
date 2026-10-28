using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MagazineState = WeaponController.MagazineState;

/// <summary>
/// L'arsenal : les douze armes de la refonte du 2026-10-26 (GDD § 6), en quatre familles de trois (le
/// stock, la série 30, le prototype laser), réglées sur Call of Duty et Rainbow Six.
///
/// Ce qui casse ici ne lève aucune erreur : un sniper qui ne tue plus d'une balle au buste, une gerbe
/// qui change d'un tir à l'autre (le GDD § 3 interdit le hasard), un pompe qui recharge tout d'un
/// coup, une arme qu'on tire avant de l'avoir sortie, deux armes qui portent le même modèle. Les seuils
/// de dégâts sont lus dans les VRAIS assets : ce sont eux qui tournent en jeu, pas des valeurs de test.
/// </summary>
public class WeaponLoadoutTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const float Health = 100f;

    // Les places de l'arsenal (WeaponController.loadout) : les quatre d'avant la refonte d'abord.
    private const int FA07 = 0, FP12 = 1, FAP03 = 2, FX4 = 3, PA05 = 10;

    private static WeaponController PrefabWeapon()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(prefab, "Prefab joueur introuvable : " + PlayerPrefabPath);
        var weapon = prefab.GetComponentInChildren<WeaponController>(true);
        Assert.IsNotNull(weapon, "Le prefab joueur n'a pas de WeaponController.");
        return weapon;
    }

    private static WeaponData Slot(int slot)
    {
        WeaponData data = PrefabWeapon().DataOf(slot);
        Assert.IsNotNull(data, $"La place {slot + 1} de l'arsenal est vide.");
        return data;
    }

    private static int ShotsToKill(float damagePerShot) => Mathf.CeilToInt(Health / damagePerShot - 1e-4f);

    // ------------------------------------------------------------------
    // L'arsenal du prefab
    // ------------------------------------------------------------------

    /// <summary>Le modèle de chaque arme de l'arsenal.</summary>
    private static IEnumerable<GameObject> Models()
    {
        WeaponController weapon = PrefabWeapon();
        for (int slot = 0; slot < weapon.SlotCount; slot++) yield return weapon.DataOf(slot).modelPrefab;
    }

    [Test]
    public void LArsenal_DouzeArmes_TroisParFamille_ChacuneSonModele_AvecMireEtBouche()
    {
        WeaponController weapon = PrefabWeapon();
        Assert.AreEqual(WeaponController.MaxSlots, weapon.SlotCount, "Douze armes (GDD § 6, 2026-10-26).");

        // Les quatre premières places sont les armes d'avant la refonte : les réglages enregistrés et le
        // réseau les désignent par leur rang (GameSettings.ArmsBeforeOverhaul).
        string[] firstFour = { "FA-07", "FP-12", "FAP-03", "FX-4" };
        for (int slot = 0; slot < firstFour.Length; slot++)
            Assert.AreEqual(firstFour[slot], weapon.DataOf(slot).Designation, $"Place {slot + 1}.");

        var designations = new HashSet<string>();
        for (int slot = 0; slot < weapon.SlotCount; slot++)
        {
            WeaponData data = weapon.DataOf(slot);
            GameObject prefab = data.modelPrefab;
            Assert.IsNotNull(prefab, $"{data.name} n'a pas de modèle.");
            var model = prefab.GetComponent<WeaponModel>();
            Assert.IsNotNull(model, $"{prefab.name} n'a pas de WeaponModel.");
            Assert.IsFalse(string.IsNullOrEmpty(model.Designation), $"{prefab.name} n'a pas de désignation.");
            Assert.IsTrue(designations.Add(model.Designation), $"Deux armes portent le modèle {model.Designation} : HeldWeapons les confondrait.");
            Assert.AreEqual(model.Designation, data.weaponName, $"{data.name} : une arme porte le nom de son modèle (l'importeur s'y fie).");
            Assert.IsFalse(string.IsNullOrEmpty(data.role), $"{data.weaponName} n'a pas de rôle (GDD § 6).");
            Assert.IsNotNull(model.Sight, $"{prefab.name} n'a pas de mire.");
            Assert.IsNotNull(model.Muzzle, $"{prefab.name} n'a pas de bouche.");
            Assert.Less(Quaternion.Angle(Quaternion.identity, model.Sight.localRotation), 0.01f, $"La mire de {prefab.name} doit regarder dans l'axe du canon (+Z).");
            Assert.Greater(model.Muzzle.localPosition.z, model.Sight.localPosition.z, $"La bouche de {prefab.name} doit être devant la mire.");
            Assert.Greater(model.Muzzle.localPosition.z, 0.1f, $"La bouche de {prefab.name} doit être devant la poignée (origine).");
            Assert.AreEqual(Vector3.one, prefab.transform.localScale, $"{prefab.name} : à l'échelle 1, la poignée au point de prise.");
        }

        // Quatre familles de trois : le stock, la série 30, le prototype laser, chacune avec les gestes
        // de sa famille (une arme de poing se tient à bout de bras).
        for (int family = 0; family < Arsenal.FamilyCount; family++)
        {
            List<int> slots = Arsenal.SlotsOf(weapon.Loadout, (WeaponFamily)family);
            Assert.AreEqual(3, slots.Count, $"{Arsenal.FamilyLabel((WeaponFamily)family)} : trois armes.");
            for (int tier = 0; tier < slots.Count; tier++)
            {
                WeaponData data = weapon.DataOf(slots[tier]);
                Assert.AreEqual((WeaponModel.Family)tier, data.Tier, $"{data.weaponName} : son rang dans la famille.");
                Assert.AreEqual(family == (int)WeaponFamily.Pistol, data.modelPrefab.GetComponent<WeaponModel>().PistolHold,
                    $"{data.weaponName} : tenu comme les armes de sa famille.");
                Assert.AreEqual(data.Tier == WeaponModel.Family.Laser, data.UsesHeat, $"{data.weaponName} : seules les armes laser chauffent.");
                Assert.AreEqual(data.Tier == WeaponModel.Family.Laser, data.shotIsBeam, $"{data.weaponName} : une arme laser tire un rayon, une arme à balles une flamme.");
            }
        }

        // Les armes se créent en main au lancement, au point de prise du préfab (HeldWeapons) : aucune
        // n'y est posée d'avance, la vue subjective en aurait une de trop.
        var player = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform hand = null;
        foreach (Transform t in player.GetComponentsInChildren<Transform>(true)) if (t.name == "mixamorig:RightHand") hand = t;
        Assert.IsNotNull(hand.Find(HeldWeapons.GripAnchorName), "Le point de prise des armes manque sous la main droite.");
        Assert.AreEqual(0, hand.GetComponentsInChildren<WeaponModel>(true).Length, "Une arme est posée dans le préfab joueur.");
        Assert.IsNotNull(player.GetComponent<PlayerWeaponLooks>(), "Sans PlayerWeaponLooks, personne ne verrait les armes choisies.");
    }

    /// <summary>Les six finitions de chaque modèle (GDD § 6) : chaque pièce a ses matériaux dans
    /// chacune, tous du shader de l'usure, et une finition change vraiment quelque chose.</summary>
    [Test]
    public void ChaqueModele_ASesSixFinitions_DuShaderDeLUsure()
    {
        {
            foreach (GameObject prefab in Models())
            {
                var finishes = prefab.GetComponent<WeaponFinishes>();
                Assert.IsNotNull(finishes, $"{prefab.name} n'a pas de finitions.");
                Assert.AreEqual(prefab.GetComponentsInChildren<MeshRenderer>(true).Length, finishes.PieceCount,
                    $"{prefab.name} : une pièce n'a pas ses finitions.");
                var distinct = new HashSet<Material>();
                for (int i = 0; i < finishes.PieceCount; i++)
                {
                    Renderer r = finishes.RendererOf(i);
                    Assert.IsNotNull(r, $"{prefab.name} : une pièce a perdu son rendu.");
                    CollectionAssert.AreEqual(r.sharedMaterials, finishes.MaterialsOf(i, WeaponFinish.Origine),
                        $"{prefab.name}/{r.name} : le préfab doit porter la finition d'origine.");
                    for (int f = 0; f < WeaponLooks.FinishCount; f++)
                    {
                        Material[] mats = finishes.MaterialsOf(i, (WeaponFinish)f);
                        Assert.AreEqual(r.sharedMaterials.Length, mats.Length, $"{prefab.name}/{r.name} : un matériau par sous-maillage.");
                        foreach (Material m in mats)
                        {
                            Assert.IsNotNull(m, $"{prefab.name}/{r.name} : un matériau manque à la finition {(WeaponFinish)f}.");
                            Assert.AreEqual("DuelArena/ArmeUsee", m.shader.name, $"{m.name} n'est pas du shader de l'usure.");
                            Assert.IsNotNull(m.GetTexture("_Occlusion"), $"{m.name} a perdu l'occlusion cuite dans Blender.");
                            distinct.Add(m);
                        }
                    }
                }
                Assert.Greater(distinct.Count, prefab.GetComponentsInChildren<MeshRenderer>(true)[0].sharedMaterials.Length,
                    $"{prefab.name} : les finitions ne changent aucun matériau.");
            }
        }
    }

    /// <summary>Les accessoires (2026-10-16) se montent aux points posés sous `Accessoires` dans le
    /// préfab de chaque arme (AttachmentMount) ; une arme sans point pour une rubrique ne l'a pas. Un
    /// modèle remplacé sans ses points perdrait ses accessoires en silence.</summary>
    [Test]
    public void ChaqueArme_ASesPointsDAccroche_SaufLaPoigneeDuPistolet()
    {
        {
            foreach (GameObject prefab in Models())
            {
                Transform group = prefab.transform.Find(AttachmentMount.SocketGroupName);
                Assert.IsNotNull(group, $"{prefab.name} n'a pas de groupe « {AttachmentMount.SocketGroupName} ».");

                bool pistol = prefab.GetComponent<WeaponModel>().PistolHold;
                foreach (AttachmentCategory category in System.Enum.GetValues(typeof(AttachmentCategory)))
                {
                    Transform socket = group.Find(AttachmentMount.SocketName(category));
                    if (category == AttachmentCategory.Grip && pistol)
                    {
                        Assert.IsNull(socket, $"{prefab.name} est un pistolet : pas de poignée avant.");
                        continue;
                    }
                    Assert.IsNotNull(socket, $"{prefab.name} n'a pas de point d'accroche pour {category}.");
                    Assert.AreEqual(0, socket.childCount, $"Le point {socket.name} de {prefab.name} doit rester vide : l'accessoire y est monté au lancement.");
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Dégâts : zones et distance (façon Call of Duty)
    // ------------------------------------------------------------------

    /// <summary>Le gant à décharge du taser : le GD-1 (2026-10-16), construit dans Blender dans la DA du projet
    /// (Tools/Blender/gant_choc.py), à la place du modèle généré par IA de 28 404 triangles. Porté sur
    /// l'avant-bras gauche, la main qui frappe.</summary>
    [Test]
    public void LeGant_EstSurLAvantBrasGauche_LegerEtDansLaDA()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform glove = null, hand = null;
        foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == HeldWeapons.TaserGloveName) glove = t;
            if (t.name == "mixamorig:LeftHand") hand = t;
        }
        Assert.IsNotNull(glove, "Le préfab joueur ne porte pas le gant du taser.");
        Assert.AreEqual("mixamorig:LeftForeArm", glove.parent.name, "Le gant se porte sur l'avant-bras gauche : c'est la main gauche qui frappe.");
        Assert.AreEqual(hand.localPosition, glove.localPosition, "L'origine du gant est au poignet.");
        Assert.AreEqual(Vector3.zero, hand.localScale, "La main du robot traverserait le poing du gant.");

        Transform fist = glove.Find("Poing");
        Assert.IsNotNull(fist, "Le gant n'a pas de repère « Poing ».");
        Assert.Greater(fist.localPosition.z, 0.12f, "Le poing est devant les jointures (12,8 cm du poignet).");
        Assert.Less(fist.localPosition.z, 0.25f, "Le poing est au bout de la main, pas au-delà.");
        Assert.IsNotNull(fist.GetComponentInChildren<TaserSparkFX>(true), "Les arcs du coup partent du poing (ElectricArcTests).");

        int triangles = 0;
        MeshRenderer[] pieces = glove.GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer piece in pieces) triangles += piece.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
        Assert.Greater(triangles, 500, "Le gant n'a plus de modèle.");
        Assert.Less(triangles, 5000, $"Le gant pèse {triangles} triangles : le budget d'une arme du projet (l'ancien en avait 28 404).");
        foreach (MeshRenderer piece in pieces)
            foreach (Material m in piece.sharedMaterials)
                Assert.AreEqual(WeaponImporter.ShaderName, m.shader.name, $"{piece.name} : le gant s'use comme les armes (ArmeUsee).");
        var finishes = glove.GetComponent<WeaponFinishes>();
        Assert.IsNotNull(finishes, "Le gant n'a pas ses finitions.");
        Assert.AreEqual(pieces.Length, finishes.PieceCount, "Chaque pièce du gant a ses six finitions.");
    }

    [Test]
    public void LeFusilDAssaut_GardeSesSixBallesAuBuste_EtLaTeteAccelere()
    {
        WeaponData ar = Slot(FA07);
        Assert.AreEqual(6, ShotsToKill(ar.DamageAt(HitboxZoneType.Torso, 10f)), "TTK du GDD : 6 balles au buste.");
        Assert.Less(ShotsToKill(ar.DamageAt(HitboxZoneType.Head, 10f)), 6, "Viser la tête doit payer.");
        Assert.AreEqual(ar.DamageAt(HitboxZoneType.Torso, 1f), ar.DamageAt(HitboxZoneType.Torso, 90f), 1e-4f, "Pas de baisse de dégâts avec la distance.");
    }

    [Test]
    public void LeSniper_TueDUneBalleAuBusteEtALaTete_PasAuxJambes()
    {
        WeaponData sniper = Slot(FP12);
        Assert.AreEqual(1, ShotsToKill(sniper.DamageAt(HitboxZoneType.Torso, 60f)), "Au buste, à toute distance.");
        Assert.AreEqual(1, ShotsToKill(sniper.DamageAt(HitboxZoneType.Head, 60f)));
        Assert.AreEqual(2, ShotsToKill(sniper.DamageAt(HitboxZoneType.Legs, 60f)), "Aux jambes, il en faut deux.");
        Assert.IsFalse(sniper.isAutomatic, "Un sniper à verrou tire coup par coup.");
        Assert.Greater(sniper.scopeMagnification, 1f, "Le sniper a sa lunette plein écran.");
        Assert.IsFalse(sniper.hipCrosshair, "Pas de réticule à la hanche, comme dans CoD.");
    }

    [Test]
    public void LePompe_TueDePres_PasDeLoin_SansBonusALaTete()
    {
        WeaponData pompe = Slot(FAP03);
        Assert.Greater(pompe.PelletCount, 1, "Le pompe tire des plombs.");
        float pointBlank = pompe.PelletCount * pompe.DamageAt(HitboxZoneType.Torso, 3f);
        float far = pompe.PelletCount * pompe.DamageAt(HitboxZoneType.Torso, 25f);
        Assert.GreaterOrEqual(pointBlank, Health, "Tous les plombs au buste, à bout portant, tuent.");
        Assert.Less(far, Health, "À 25 m, même tous les plombs ne tuent pas.");
        Assert.AreEqual(pompe.DamageAt(HitboxZoneType.Torso, 3f), pompe.DamageAt(HitboxZoneType.Head, 3f), 1e-4f,
            "Les plombs ne comptent pas la tête (CoD).");
        Assert.IsTrue(pompe.reloadPerRound, "Le pompe recharge cartouche par cartouche.");
    }

    /// <summary>Le PA-05 (2026-10-26) : semi-automatique, 15 coups, le plus mobile, il sort le plus vite
    /// de tout l'arsenal.</summary>
    [Test]
    public void LePA05_EstLePlusMobile_EtSortLePlusVite()
    {
        WeaponController weapon = PrefabWeapon();
        WeaponData pistol = Slot(PA05);
        Assert.IsFalse(pistol.isAutomatic, "Semi-automatique.");
        Assert.AreEqual(1, pistol.PelletCount, "Une balle, pas une gerbe.");
        Assert.AreEqual(15, pistol.magazineSize, "15 coups (GDD § 6).");
        for (int slot = 0; slot < weapon.SlotCount; slot++)
        {
            if (slot == PA05) continue;
            Assert.Less(pistol.raiseTime, Slot(slot).raiseTime, $"Le PA-05 se sort plus vite que le {Slot(slot).weaponName}.");
            Assert.GreaterOrEqual(pistol.moveSpeedMultiplier, Slot(slot).moveSpeedMultiplier, $"Le PA-05 est au moins aussi mobile que le {Slot(slot).weaponName}.");
        }
    }

    [Test]
    public void LaBaisseDeDegatsAvecLaDistance_EstContinue()
    {
        var data = ScriptableObject.CreateInstance<WeaponData>();
        data.damagePerHit = 20f;
        data.falloffStart = 10f;
        data.falloffEnd = 20f;
        data.minDamageMultiplier = 0.5f;
        Assert.AreEqual(20f, data.DamageAt(HitboxZoneType.Torso, 5f), 1e-4f);
        Assert.AreEqual(20f, data.DamageAt(HitboxZoneType.Torso, 10f), 1e-4f);
        Assert.AreEqual(15f, data.DamageAt(HitboxZoneType.Torso, 15f), 1e-4f);
        Assert.AreEqual(10f, data.DamageAt(HitboxZoneType.Torso, 20f), 1e-4f);
        Assert.AreEqual(10f, data.DamageAt(HitboxZoneType.Torso, 80f), 1e-4f);
        Object.DestroyImmediate(data);
    }

    // ------------------------------------------------------------------
    // La gerbe : un motif fixe (GDD § 3)
    // ------------------------------------------------------------------

    [Test]
    public void LaGerbe_EstUnMotifFixe_DansLeRepereDuRegard()
    {
        WeaponData pompe = Slot(FAP03);
        Vector3 forward = new Vector3(0.3f, -0.2f, 1f).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 up = Vector3.Cross(forward, right);
        for (int i = 0; i < pompe.PelletCount; i++)
        {
            Vector2 offset = pompe.PelletOffset(i, false);
            Vector3 a = WeaponController.PelletDirection(forward, offset);
            Vector3 b = WeaponController.PelletDirection(forward, offset);
            Assert.AreEqual(a, b, $"Plomb {i} : deux tirs identiques doivent partir exactement pareil.");
            Assert.AreEqual(offset.magnitude, Vector3.Angle(forward, a), 0.05f, $"Plomb {i} : écart angulaire.");
            if (Mathf.Abs(offset.x) > 0.1f) Assert.AreEqual(Mathf.Sign(offset.x), Mathf.Sign(Vector3.Dot(a, right)), $"Plomb {i} : côté.");
            if (Mathf.Abs(offset.y) > 0.1f) Assert.AreEqual(Mathf.Sign(offset.y), Mathf.Sign(Vector3.Dot(a, up)), $"Plomb {i} : hauteur.");
        }
        // En visée, la gerbe se resserre.
        float hip = 0f, aim = 0f;
        for (int i = 0; i < pompe.PelletCount; i++)
        {
            hip = Mathf.Max(hip, pompe.PelletOffset(i, false).magnitude);
            aim = Mathf.Max(aim, pompe.PelletOffset(i, true).magnitude);
        }
        Assert.Less(aim, hip, "La gerbe ne se resserre pas en visée.");

        // Regard à la verticale : pas de NaN.
        Vector3 vertical = WeaponController.PelletDirection(Vector3.up, new Vector2(2f, 1f));
        Assert.IsFalse(float.IsNaN(vertical.x) || float.IsNaN(vertical.y) || float.IsNaN(vertical.z));
        Assert.AreEqual(Vector3.forward, WeaponController.PelletDirection(Vector3.forward, Vector2.zero), "Une balle seule part dans l'axe.");
    }

    // ------------------------------------------------------------------
    // Chargeur : cartouche par cartouche, changement d'arme
    // ------------------------------------------------------------------

    [Test]
    public void LePompe_GlisseSesCartouchesUneAUne_EtTirerInterromptLeRechargement()
    {
        const int size = 6;
        const float shell = 0.5f;
        var m = new MagazineState { ammo = 1 };
        Assert.IsTrue(WeaponController.TryStartReload(ref m, 10.0, size, shell));

        Assert.IsFalse(WeaponController.CompleteReloadIfDue(ref m, 10.4, size, true, shell));
        Assert.AreEqual(1, m.ammo);
        Assert.IsTrue(WeaponController.CompleteReloadIfDue(ref m, 10.5, size, true, shell));
        Assert.AreEqual(2, m.ammo, "Une cartouche à 0,5 s.");
        Assert.Greater(m.reloadEndsAt, 0.0, "Le rechargement continue.");
        WeaponController.CompleteReloadIfDue(ref m, 11.6, size, true, shell);
        Assert.AreEqual(4, m.ammo, "Deux de plus à 1,5 s (et pas trois à 1,6 s).");

        // Tirer pendant le rechargement : il s'arrête, le coup part avec ce qui est chargé.
        Assert.IsTrue(WeaponController.TryConsumeRound(ref m, 11.7, size, true, shell, 0.0));
        Assert.AreEqual(3, m.ammo);
        Assert.AreEqual(0.0, m.reloadEndsAt, "Le tir n'a pas interrompu le rechargement.");

        // Jusqu'au plein, puis plus rien.
        var full = new MagazineState { ammo = 4 };
        WeaponController.TryStartReload(ref full, 0.0, size, shell);
        WeaponController.CompleteReloadIfDue(ref full, 10.0, size, true, shell);
        Assert.AreEqual(size, full.ammo);
        Assert.AreEqual(0.0, full.reloadEndsAt);

        // Un chargeur complet, lui, ne se tire pas en rechargeant.
        var rifle = new MagazineState { ammo = 3 };
        WeaponController.TryStartReload(ref rifle, 0.0, 30, 2f);
        Assert.IsFalse(WeaponController.TryConsumeRound(ref rifle, 1.0, 30, false, 0f, 0.0));
        // Et un pompe vide en rechargement attend sa première cartouche.
        var empty = new MagazineState { ammo = 0 };
        WeaponController.TryStartReload(ref empty, 0.0, size, shell);
        Assert.IsFalse(WeaponController.TryConsumeRound(ref empty, 0.2, size, true, shell, 0.0));
        Assert.IsTrue(WeaponController.TryConsumeRound(ref empty, 0.47, size, true, shell, WeaponController.ReloadCompletionTolerance),
            "La première cartouche à 50 ms près compte (gigue du réseau), comme pour un rechargement complet.");
    }

    /// <summary>Une seule arme dans l'inventaire (2026-10-25) : la nouvelle arrive pleine (et froide, une
    /// arme laser), le rechargement en cours s'arrête, et elle se sort avant de tirer.</summary>
    [Test]
    public void ChangerDArme_DonneLaNouvellePleine_QuiSeSortAvantDeTirer()
    {
        var m = new MagazineState { equipped = 0, ammo = 12, heat = 0.6f, heatAt = 0.5, overheated = true };
        WeaponController.TryStartReload(ref m, 0.0, 30, 2.2f);

        Assert.IsTrue(WeaponController.TrySwitchWeapon(ref m, 11, 1.0, 0.8f, 6));
        Assert.AreEqual(11, m.equipped, "La douzième arme : l'arme en main tient sur un octet.");
        Assert.AreEqual(6, m.ammo, "La nouvelle arme arrive pleine : l'inventaire ne garde pas les balles de l'ancienne.");
        Assert.AreEqual(0f, m.heat, "Et froide.");
        Assert.IsFalse(m.overheated);
        Assert.AreEqual(0.0, m.reloadEndsAt, "Changer d'arme annule le rechargement (CoD).");

        Assert.IsFalse(WeaponController.TryConsumeRound(ref m, 1.5, 6, false, 0f, 0.0), "L'arme n'est pas encore sortie.");
        Assert.IsTrue(WeaponController.TryConsumeRound(ref m, 1.81, 6, false, 0f, 0.0));
        Assert.AreEqual(5, m.ammo);

        Assert.IsFalse(WeaponController.TrySwitchWeapon(ref m, 11, 2.0, 0.8f, 6), "Déjà en main.");
        Assert.IsFalse(WeaponController.TrySwitchWeapon(ref m, WeaponController.MaxSlots, 2.0, 0.8f, 6), "Hors de l'arsenal.");
        Assert.AreEqual(5, m.ammo, "Un changement refusé ne remplit rien.");
    }

    [Test]
    public void LeRecalage_ReprendArmeChargeurEtChaleur_UneFoisToutAccuse()
    {
        var local = new MagazineState { equipped = 5, actionsProcessed = 0, ammo = 1, heat = 0.4f, heatAt = 3.0 };
        var server = local;
        server.actionsProcessed = 3;
        server.equipped = 0; // le serveur a refusé le changement
        server.ammo = 24;
        server.heat = 0f;

        MagazineState before = local;
        WeaponController.ReconcileMagazine(server, 4, ref local);
        Assert.AreEqual(before.equipped, local.equipped, "Une demande est encore en route : pas de recalage.");

        WeaponController.ReconcileMagazine(server, 3, ref local);
        Assert.AreEqual(0, local.equipped, "Tout est accusé : le serveur fait foi, arme en main comprise.");
        Assert.AreEqual(24, local.ammo, "Et pour le chargeur.");
        Assert.AreEqual(0f, local.heat, "Et pour la chaleur d'une arme laser.");
    }

    [Test]
    public void LaLunette_GrossitLeChamp_Exactement()
    {
        float fov = PlayerCameraLook.ZoomedFov(90f, 4f);
        Assert.AreEqual(0.25f, Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(45f * Mathf.Deg2Rad), 1e-4f);
        Assert.AreEqual(90f, PlayerCameraLook.ZoomedFov(90f, 1f), 1e-4f);
    }

    /// <summary>Rapporté par l'utilisateur le 2026-10-12 : viser au sniper touche de sprint tenue,
    /// puis lâcher la visée, laissait la vue zoomée jusqu'à ce qu'on lâche le sprint. Le champ de la
    /// lunette ne revenait d'un coup qu'à un joueur qui ne sprinte pas.</summary>
    [Test]
    public void SortirDeLaLunette_DezoomeAussitot_MemeEnCourant()
    {
        const float Base = 90f, Sprint = 96f, Speed = 8f, Dt = 1f / 60f;
        float scoped = PlayerCameraLook.NextFov(Base, Base, Sprint, 4f, false, Speed, Dt);
        Assert.AreEqual(PlayerCameraLook.ZoomedFov(Base, 4f), scoped, 1e-4f, "Dans la lunette : le champ du grossissement.");

        foreach (bool sprinting in new[] { false, true })
        {
            float next = PlayerCameraLook.NextFov(scoped, Base, Sprint, 1f, sprinting, Speed, Dt);
            Assert.GreaterOrEqual(next, Base - 0.01f,
                $"En sortant de la lunette ({(sprinting ? "en courant" : "à l'arrêt")}), la vue doit revenir au champ normal dès l'image suivante.");
        }

        // Le passage au champ de sprint, lui, reste progressif.
        float sprintStart = PlayerCameraLook.NextFov(Base, Base, Sprint, 1f, true, Speed, Dt);
        Assert.Less(sprintStart, Sprint, "Le champ de sprint s'installe en douceur.");
        Assert.Greater(sprintStart, Base);
    }
}
