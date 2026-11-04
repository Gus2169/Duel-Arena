using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// La surface touchable réelle, interrogée par de VRAIS rayons, comme le fait le serveur.
///
/// BodyLayoutTests vérifie le calcul ; ce fichier vérifie que les colliders créés par PlayerHitbox
/// le reproduisent — layer, trigger, orientation des capsules — et que le lean façon Rainbow Six
/// fait bien ce que le GDD (§ 7) promet : le buste s'expose, les jambes restent à couvert.
/// </summary>
public class HitboxZoneTests
{
    private static readonly Vector3 EyeStanding = new Vector3(0f, 1.65f, 0f);
    private static readonly Vector3 EyeProne = new Vector3(-0.15f, 0.36f, 0.25f);

    private GameObject go;
    private PlayerHitbox hitbox;
    private int mask;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("TestHitbox");
        hitbox = go.AddComponent<PlayerHitbox>();
        mask = 1 << LayerMask.NameToLayer("Hitbox");
    }

    [TearDown]
    public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
    }

    /// <summary>Tire un rayon horizontal vers +Z depuis l'arrière, à la position (x, y) donnée, et
    /// renvoie la zone touchée — ou null.</summary>
    private PlayerHitboxZone ShootAlongZ(float x, float y)
    {
        Physics.SyncTransforms();
        var origin = new Vector3(x, y, -5f);
        if (!Physics.Raycast(origin, Vector3.forward, out RaycastHit hit, 20f, mask, QueryTriggerInteraction.Collide))
            return null;
        return hit.collider.GetComponent<PlayerHitboxZone>();
    }

    private PlayerHitboxZone ShootDown(float x, float z)
    {
        Physics.SyncTransforms();
        if (!Physics.Raycast(new Vector3(x, 5f, z), Vector3.down, out RaycastHit hit, 20f, mask, QueryTriggerInteraction.Collide))
            return null;
        return hit.collider.GetComponent<PlayerHitboxZone>();
    }

    [UnityTest]
    public IEnumerator LesZones_SontDesTriggersSurLeLayerHitbox()
    {
        yield return null;
        // Tête, torse, jambes, plus une seconde jambe utilisée allongé seulement.
        Assert.AreEqual(4, hitbox.Colliders.Count);
        var seen = new System.Collections.Generic.HashSet<HitboxZoneType>();
        foreach (Collider c in hitbox.Colliders)
        {
            Assert.IsTrue(c.isTrigger, c.name + " n'est pas un trigger : il repousserait les joueurs.");
            Assert.AreEqual(LayerMask.NameToLayer("Hitbox"), c.gameObject.layer, c.name + " n'est pas sur le layer Hitbox.");
            var zone = c.GetComponent<PlayerHitboxZone>();
            Assert.IsNotNull(zone, c.name + " n'a pas d'étiquette de zone.");
            seen.Add(zone.Zone);
        }
        Assert.AreEqual(3, seen.Count, "Il manque une zone : tête, torse ou jambes.");
    }

    [UnityTest]
    public IEnumerator Debout_ChaqueHauteurTombeDansLaBonneZone()
    {
        yield return null;
        hitbox.Apply(PlayerLocomotion.Stance.Standing, EyeStanding, 0f);

        Assert.AreEqual(HitboxZoneType.Head, ShootAlongZ(0f, 1.68f)?.Zone, "Un tir à hauteur de tête doit toucher la tête.");
        Assert.AreEqual(HitboxZoneType.Torso, ShootAlongZ(0f, 1.25f)?.Zone, "Un tir à hauteur de poitrine doit toucher le torse.");
        Assert.AreEqual(HitboxZoneType.Legs, ShootAlongZ(0f, 0.50f)?.Zone, "Un tir à hauteur de genou doit toucher les jambes.");
        Assert.IsNull(ShootAlongZ(0f, 1.90f), "Un tir au-dessus de la tête ne doit rien toucher.");
    }

    [UnityTest]
    public IEnumerator Peek_LaTeteSortSurLeCote_EtLesJambesRestentACouvert()
    {
        yield return null;
        const float lean = 0.35f;
        hitbox.Apply(PlayerLocomotion.Stance.Standing, EyeStanding, lean);
        Vector3 leanedEye = BodyLayout.LeanedEye(BodyLayout.DefaultStanding, EyeStanding, lean);

        // Garde anti-vacuité : la tête est bien sortie là où le calcul l'annonce.
        Assert.AreEqual(HitboxZoneType.Head, ShootAlongZ(leanedEye.x, leanedEye.y)?.Zone,
            "La tête penchée n'est pas touchable là où le lean la place.");

        // Là où se trouvait la tête droite, il n'y a plus rien : le peek a bien déplacé la cible.
        Assert.IsNull(ShootAlongZ(0f, EyeStanding.y), "Penché, on reste touchable à l'emplacement de la tête droite.");

        // Les jambes, elles, n'ont pas bougé : un tir à droite des jambes, à la hauteur des genoux,
        // ne doit rien toucher — c'est ce qui les garde derrière la couverture.
        Assert.IsNull(ShootAlongZ(0.30f, 0.50f), "Les jambes ont suivi le lean : le peek expose tout le corps.");
        Assert.AreEqual(HitboxZoneType.Legs, ShootAlongZ(0f, 0.50f)?.Zone);
    }

    [UnityTest]
    public IEnumerator Allonge_LeCorpsEstCoucheDansLAxe_JambesDerriere_TeteDevant()
    {
        yield return null;
        hitbox.Apply(PlayerLocomotion.Stance.Prone, EyeProne, 0f);

        // Le corps allongé fait près de 1,5 m : jambes derrière la racine, tête devant. L'ancienne
        // capsule unique n'en couvrait qu'une boule de 80 cm, et les jambes étaient intouchables.
        Assert.AreEqual(HitboxZoneType.Legs, ShootDown(0f, -0.90f)?.Zone, "Les jambes d'un joueur allongé doivent être touchables.");
        Assert.AreEqual(HitboxZoneType.Legs, ShootDown(0.38f, -0.80f)?.Zone, "La jambe repliée sur le côté doit être touchable.");
        Assert.AreEqual(HitboxZoneType.Torso, ShootDown(-0.05f, -0.15f)?.Zone);
        Assert.AreEqual(HitboxZoneType.Head, ShootDown(EyeProne.x, EyeProne.z)?.Zone, "La tête doit être là où est la caméra.");
        Assert.IsNull(ShootAlongZ(0f, 0.80f), "Allongé, rien ne doit dépasser à 80 cm de haut.");
    }

    [UnityTest]
    public IEnumerator UnLean_NeReecritPasLesJambes_MaisUneAutrePostureLesReplace()
    {
        yield return null;
        hitbox.Apply(PlayerLocomotion.Stance.Standing, EyeStanding, 0f);
        Transform head = hitbox.Colliders[0].transform;
        Transform legs = hitbox.Colliders[2].transform;

        // Une zone qu'on déplace à la main y reste si rien ne la réécrit. Transform.hasChanged ne le dirait pas : il ne se
        // lève pas pour une écriture de la valeur qu'elle a déjà.
        Vector3 legsPlace = legs.localPosition, moved = legsPlace + Vector3.up;
        legs.localPosition = moved;
        head.hasChanged = false;
        hitbox.Apply(PlayerLocomotion.Stance.Standing, EyeStanding, 0.2f);
        Assert.IsTrue(head.hasChanged, "Garde anti-vacuité : le lean déplace la tête.");
        Assert.AreEqual(moved, legs.localPosition, "Le lean ne bouge pas les jambes : les réécrire, c'est les faire recalculer pour rien.");
        legs.localPosition = legsPlace;

        Vector3 headPlace = head.localPosition;
        moved = headPlace + Vector3.up;
        head.localPosition = moved;
        hitbox.Apply(PlayerLocomotion.Stance.Standing, EyeStanding, 0.2f);
        Assert.AreEqual(moved, head.localPosition, "La même pose ne réécrit rien.");
        head.localPosition = headPlace;

        // Le rewind passe par ici avec une pose passée, puis la pose courante : chaque changement s'applique.
        legs.hasChanged = false;
        hitbox.Apply(PlayerLocomotion.Stance.Prone, EyeProne, 0.1f);
        Assert.IsTrue(legs.hasChanged, "Une autre posture replace les jambes.");
        Assert.AreEqual(HitboxZoneType.Legs, ShootDown(0.38f, -0.80f)?.Zone, "La pose passée a sa seconde jambe.");
        hitbox.Apply(PlayerLocomotion.Stance.Standing, EyeStanding, 0.2f);
        Assert.IsNull(ShootDown(0.38f, -0.80f), "De retour, la seconde jambe n'est plus là.");
        Assert.AreEqual(HitboxZoneType.Legs, ShootAlongZ(0f, 0.50f)?.Zone, "De retour, les jambes sont à leur place.");
    }

    [UnityTest]
    public IEnumerator HorsPositionAllongee_LaSecondeJambeNExistePas()
    {
        yield return null;
        hitbox.Apply(PlayerLocomotion.Stance.Prone, EyeProne, 0f);
        hitbox.Apply(PlayerLocomotion.Stance.Standing, EyeStanding, 0f);

        // La jambe repliée de la pose allongée ne doit pas traîner au sol quand on se relève.
        Assert.IsNull(ShootDown(0.38f, -0.80f), "La seconde jambe est restée active debout.");
    }

    [UnityTest]
    public IEnumerator LeTireurRetireToutesSesZones_PuisLesRetrouve()
    {
        yield return null;
        hitbox.Apply(PlayerLocomotion.Stance.Prone, EyeProne, 0f);

        hitbox.SetQueryable(false);
        Assert.IsNull(ShootDown(0f, -0.90f), "Une zone est restée active pendant le tir du propriétaire.");
        Assert.IsNull(ShootDown(0.38f, -0.80f), "La seconde jambe est restée active pendant le tir du propriétaire.");

        hitbox.SetQueryable(true);
        Assert.AreEqual(HitboxZoneType.Legs, ShootDown(0.38f, -0.80f)?.Zone, "La seconde jambe n'est pas revenue après le tir.");
    }
}
