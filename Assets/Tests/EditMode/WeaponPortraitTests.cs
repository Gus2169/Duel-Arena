using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Le portrait de chaque arme (2026-10-27, demandé par l'utilisateur) : de profil, sans accessoire, dans sa
/// finition d'origine, rendu par les vignettes du menu (MenuThumbnails.PrepareWeaponPortraits) et gardé pour
/// les boutons qui choisissent une arme (WeaponPortraits : la préparation d'une manche, la fenêtre du stand).
///
/// Ce qui casse ici ne lève aucune erreur, le bouton est seulement vide ou faux : une texture lue avant le
/// rendu, une arme coupée par le bord du cadre ou perdue au milieu, une arme oubliée, ou des portraits rendus
/// à nouveau à chaque retour au menu.
/// </summary>
public class WeaponPortraitTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    private GameObject host;
    private MenuThumbnails thumbnails;

    [SetUp]
    public void SetUp()
    {
        WeaponPortraits.Clear();
        host = new GameObject("VignettesDeTest");
        thumbnails = host.AddComponent<MenuThumbnails>();
        var serialized = new SerializedObject(thumbnails);
        serialized.FindProperty("playerPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        WeaponPortraits.Clear();
        if (host != null) Object.DestroyImmediate(host);
    }

    private static IReadOnlyList<WeaponData> Loadout() =>
        AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<WeaponController>(true).Loadout;

    [Test]
    public void ChaqueArme_ASonPortrait_EntiereEtCadree()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        Assert.GreaterOrEqual(loadout.Count, 12, "La panoplie a perdu des armes : le test ne voit plus grand-chose.");
        thumbnails.PrepareWeaponPortraits();

        foreach (WeaponData data in loadout)
        {
            Texture2D portrait = WeaponPortraits.Of(data);
            Assert.IsNotNull(portrait, $"{data.weaponName} : pas de portrait.");
            Assert.AreEqual(WeaponPortraits.Width, portrait.width, $"{data.weaponName} : largeur du portrait.");
            Assert.AreEqual(WeaponPortraits.Height, portrait.height, $"{data.weaponName} : hauteur du portrait.");

            int width = portrait.width, height = portrait.height;
            Color32[] pixels = portrait.GetPixels32();
            int covered = 0, minX = width, maxX = -1, minY = height, maxY = -1;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a < 128) continue;
                covered++;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            Assert.Greater(covered, width * height * 0.04f, $"{data.weaponName} : le portrait est presque vide.");
            // Entière : ni le canon ni la crosse ne touchent le bord.
            Assert.IsTrue(minX > 0 && maxX < width - 1 && minY > 0 && maxY < height - 1,
                $"{data.weaponName} : l'arme touche le bord du portrait (x {minX} à {maxX}, y {minY} à {maxY} sur {width} × {height}).");
            // Cadrée : elle remplit le cadre en largeur ou en hauteur.
            Assert.IsTrue(maxX - minX + 1 >= width * 0.7f || maxY - minY + 1 >= height * 0.7f,
                $"{data.weaponName} : l'arme est perdue au milieu du portrait ({maxX - minX + 1} × {maxY - minY + 1} sur {width} × {height}).");
        }
    }

    [Test]
    public void UnPortrait_NeSeRendQuUneFois()
    {
        WeaponData rifle = Loadout()[0];
        thumbnails.PrepareWeaponPortraits();
        Texture2D first = WeaponPortraits.Of(rifle);
        Assert.IsNotNull(first, "Pas de portrait du FA-07.");

        // Le menu rouvert (retour d'une partie) garde les portraits déjà faits.
        thumbnails.PrepareWeaponPortraits();
        Assert.AreSame(first, WeaponPortraits.Of(rifle), "Le portrait a été rendu une seconde fois.");
    }
}
