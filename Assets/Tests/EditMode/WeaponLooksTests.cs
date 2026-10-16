using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// L'apparence des armes (2026-10-15, GDD § 6) : pour chacune des quatre armes, le modèle (le stock,
/// la série 30, la version laser) et la finition choisis à l'armurerie, annoncés en se connectant,
/// bornés par le serveur et vus par tous.
///
/// Ce qui casse ici ne lève aucune erreur : une arme qui prend la finition de sa voisine, un client
/// modifié qui demande un modèle qui n'existe pas (et un serveur qui le publie tel quel), une ancienne
/// build qui n'envoie pas d'apparence et qu'on lit de travers, deux instances du jeu sur un PC qui
/// échangent leurs armes, une finition « dépareillée » qui ne change aucune pièce.
/// </summary>
public class WeaponLooksTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    private static IReadOnlyList<WeaponData> Loadout() =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<WeaponController>(true).Loadout;

    [Test]
    public void LApparence_GardeUnModeleEtUneFinitionParArme_SansToucherAuxAutres()
    {
        var looks = WeaponLooks.Default;
        for (int slot = 0; slot < WeaponLooks.Slots; slot++)
        {
            Assert.AreEqual(0, looks.Model(slot));
            Assert.AreEqual(WeaponFinish.Origine, looks.Finish(slot), "Sans choix : le modèle par défaut, d'origine.");
        }

        looks = looks.With(0, 2, WeaponFinish.Rouillee).With(1, 1, WeaponFinish.Encrassee).With(3, 2, WeaponFinish.Polie);
        Assert.AreEqual(2, looks.Model(0));
        Assert.AreEqual(WeaponFinish.Rouillee, looks.Finish(0));
        Assert.AreEqual(1, looks.Model(1));
        Assert.AreEqual(WeaponFinish.Encrassee, looks.Finish(1));
        Assert.AreEqual(0, looks.Model(2), "La place 3 n'a rien reçu.");
        Assert.AreEqual(WeaponFinish.Origine, looks.Finish(2));
        Assert.AreEqual(2, looks.Model(3));
        Assert.AreEqual(WeaponFinish.Polie, looks.Finish(3));

        // Changer une arme ne touche ni le modèle ni la finition des autres.
        WeaponLooks changed = looks.WithFinish(1, WeaponFinish.Repeinte).WithModel(0, 1);
        Assert.AreEqual(1, changed.Model(0));
        Assert.AreEqual(WeaponFinish.Rouillee, changed.Finish(0), "Changer de modèle garde la finition.");
        Assert.AreEqual(1, changed.Model(1), "Changer de finition garde le modèle.");
        Assert.AreEqual(WeaponFinish.Repeinte, changed.Finish(1));
        Assert.AreEqual(looks.Model(3), changed.Model(3));
        Assert.AreEqual(looks.Finish(3), changed.Finish(3));

        // Hors de la panoplie : rien ne bouge.
        Assert.AreEqual(looks, looks.With(4, 1, WeaponFinish.Polie));
        Assert.AreEqual(0, looks.Model(-1));
    }

    [Test]
    public void LeServeur_RameneAuChoixParDefaut_CeQuiNExistePas()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        // Un client modifié : tous les bits levés (des modèles et des finitions qui n'existent pas).
        WeaponLooks sanitized = new WeaponLooks(0xFFFFFFFFu).Sanitized(loadout);
        for (int slot = 0; slot < WeaponLooks.Slots; slot++)
        {
            Assert.AreEqual(0, sanitized.Model(slot), $"Place {slot + 1} : un modèle inexistant redevient celui par défaut.");
            Assert.AreEqual(WeaponFinish.Origine, sanitized.Finish(slot), $"Place {slot + 1} : une finition inexistante redevient l'origine.");
        }

        // Un choix honnête passe tel quel, jusqu'au dernier modèle et à la dernière finition.
        var honest = WeaponLooks.Default;
        for (int slot = 0; slot < WeaponLooks.Slots; slot++)
            honest = honest.With(slot, loadout[slot].ModelCount - 1, (WeaponFinish)(WeaponLooks.FinishCount - 1 - slot));
        Assert.AreEqual(honest, honest.Sanitized(loadout), "Le serveur ne doit pas toucher à un choix valable.");

        // Juste au-delà du dernier modèle et de la dernière finition.
        WeaponLooks beyond = WeaponLooks.Default.With(1, loadout[1].ModelCount, (WeaponFinish)WeaponLooks.FinishCount).Sanitized(loadout);
        Assert.AreEqual(0, beyond.Model(1));
        Assert.AreEqual(WeaponFinish.Origine, beyond.Finish(1));
    }

    [Test]
    public void LaDemandeDeConnexion_PorteLApparenceDesArmes()
    {
        WeaponLooks looks = WeaponLooks.Default.With(0, 1, WeaponFinish.Polie).With(3, 2, WeaponFinish.Depareillee);
        Assert.IsTrue(SessionFlow.ParsePayload(SessionFlow.BuildPayload("0.3.0", "Héloïse", looks), out string version, out string name, out WeaponLooks read));
        Assert.AreEqual("0.3.0", version);
        Assert.AreEqual("Héloïse", name, "Le pseudo se lit toujours.");
        Assert.AreEqual(looks, read, "L'apparence arrive telle qu'elle est partie.");

        // Une ancienne build : sa version seule, ou sa version et son pseudo. Les armes par défaut.
        Assert.IsTrue(SessionFlow.ParsePayload(System.Text.Encoding.UTF8.GetBytes("0.1.2\nUntel"), out _, out name, out read));
        Assert.AreEqual("Untel", name);
        Assert.AreEqual(WeaponLooks.Default, read);
        Assert.IsTrue(SessionFlow.ParsePayload(System.Text.Encoding.UTF8.GetBytes("0.1.0"), out _, out _, out read));
        Assert.AreEqual(WeaponLooks.Default, read);

        // Un client modifié qui envoie n'importe quoi à la place de l'apparence : les armes par défaut.
        Assert.IsTrue(SessionFlow.ParsePayload(System.Text.Encoding.UTF8.GetBytes("0.3.0\nUntel\nZZ-pas-hexa"), out _, out _, out read));
        Assert.AreEqual(WeaponLooks.Default, read);
    }

    /// <summary>Comme le pseudo : deux instances du jeu sur un PC (l'Editor et son clone, deux builds)
    /// partagent la clé du registre ; chacune garde ses armes.</summary>
    [Test]
    public void LApparence_ResteCelleDeCetteInstance_QuandUneAutreEnregistreLaSienne()
    {
        WeaponLooks before = GameSettings.WeaponLooks;
        int stored = PlayerPrefs.GetInt(GameSettings.WeaponLooksKey, 0);
        try
        {
            WeaponLooks mine = WeaponLooks.Default.With(2, 1, WeaponFinish.Rouillee);
            GameSettings.WeaponLooks = mine;
            PlayerPrefs.SetInt(GameSettings.WeaponLooksKey, unchecked((int)WeaponLooks.Default.With(0, 2, WeaponFinish.Polie).Packed));
            Assert.AreEqual(mine, GameSettings.WeaponLooks, "L'autre instance a écrit la sienne : celle-ci garde ses armes.");
        }
        finally
        {
            GameSettings.WeaponLooks = before;
            PlayerPrefs.SetInt(GameSettings.WeaponLooksKey, stored);
            PlayerPrefs.Save();
        }
    }

    /// <summary>« Dépareillée » : les pièces qu'une réparation a prises sur une autre arme portent sa
    /// finition (exporter_armes, REMPLACEMENTS_USURE) ; les autres gardent la leur.</summary>
    [Test]
    public void LaFinitionDepareillee_DonneAuxPiecesRapporteesLaFinitionDUneAutreArme()
    {
        var rifle = Loadout()[0].modelPrefab;
        var finishes = rifle.GetComponent<WeaponFinishes>();
        int handguard = -1, receiver = -1;
        for (int i = 0; i < finishes.PieceCount; i++)
        {
            if (finishes.RendererOf(i).name == "Garde_Main") handguard = i;
            if (finishes.RendererOf(i).name == "Carcasse_Haute") receiver = i;
        }
        Assert.GreaterOrEqual(handguard, 0, $"{rifle.name} n'a plus de garde-main.");
        Assert.GreaterOrEqual(receiver, 0, $"{rifle.name} n'a plus de carcasse haute.");

        Material originHandguard = finishes.MaterialsOf(handguard, WeaponFinish.Origine)[0];
        Material mismatched = finishes.MaterialsOf(handguard, WeaponFinish.Depareillee)[0];
        Material originReceiver = finishes.MaterialsOf(receiver, WeaponFinish.Origine)[0];
        Material receiverMismatched = finishes.MaterialsOf(receiver, WeaponFinish.Depareillee)[0];
        Assert.AreSame(originHandguard, originReceiver, "Le garde-main et la carcasse ont la même peinture d'origine.");
        Assert.AreNotEqual(originHandguard.GetColor("_Base"), mismatched.GetColor("_Base"),
            "Dépareillée : le garde-main vient d'une autre arme, d'une autre couleur.");
        Assert.AreEqual(originReceiver.GetColor("_Base"), receiverMismatched.GetColor("_Base"),
            "Dépareillée : la carcasse haute reste de sa couleur (seules ses marques changent).");
    }
}
