using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Géométrie de la surface touchable et lean façon Rainbow Six (2026-10-05).
///
/// Ce qui est verrouillé ici est ce qui ne se voit PAS en jouant et se paie cher en duel : une
/// tête touchable qui n'est pas là où l'on voit, des jambes qui se penchent avec le buste, un lean
/// qui sort plus loin d'un côté que de l'autre. Aucune de ces erreurs ne lève d'exception.
/// </summary>
public class BodyLayoutTests
{
    private const float Eps = 1e-4f;
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    /// <summary>
    /// Les géométries et les yeux RÉELS du jeu, lus dans le prefab joueur.
    ///
    /// 🚨 Pas les valeurs par défaut du code : une fois le composant enregistré, ce sont les valeurs
    /// du prefab qui font foi, et elles ne suivent plus le code. Le 2026-10-05, la pose allongée a
    /// été remesurée dans BodyLayout.DefaultProne alors que le prefab gardait l'ancienne : des tests
    /// écrits contre les défauts du code seraient restés verts sur des données périmées.
    /// </summary>
    private static (StanceBody body, Vector3 eye, string name)[] AllStances
    {
        get
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(prefab, "Prefab joueur introuvable : " + PlayerPrefabPath);
            var hitbox = prefab.GetComponentInChildren<PlayerHitbox>(true);
            var locomotion = prefab.GetComponent<PlayerLocomotion>();
            Assert.IsNotNull(hitbox, "Le prefab joueur n'a pas de PlayerHitbox.");
            Assert.IsNotNull(locomotion, "Le prefab joueur n'a pas de PlayerLocomotion.");

            return new[]
            {
                (hitbox.Body(PlayerLocomotion.Stance.Standing), locomotion.EyeLocal(PlayerLocomotion.Stance.Standing), "debout"),
                (hitbox.Body(PlayerLocomotion.Stance.Crouching), locomotion.EyeLocal(PlayerLocomotion.Stance.Crouching), "accroupi"),
                (hitbox.Body(PlayerLocomotion.Stance.Prone), locomotion.EyeLocal(PlayerLocomotion.Stance.Prone), "allongé"),
            };
        }
    }

    [Test]
    public void SansLean_LaTeteEstCentreeSurLOeil()
    {
        foreach (var (body, eye, name) in AllStances)
        {
            BodyLayout.Compute(body, eye, 0f, out ZoneShape head, out _, out _, out _);
            Assert.That(Vector3.Distance(head.a, eye), Is.LessThan(Eps), $"{name} : la tête touchable n'est pas sur l'œil.");
        }
    }

    [Test]
    public void LeanDesDeuxCotes_LOeilSortExactementDuDecalageDemande()
    {
        // Vrai même allongé, où la tête est hors de l'axe du pivot (penchée sur la crosse) :
        // le scalaire réseauté reste exactement le déplacement latéral de l'œil.
        foreach (var (body, eye, name) in AllStances)
        {
            foreach (int side in new[] { 1, -1 })
            {
                float offset = side * 0.8f * BodyLayout.MaxLateralOffset(body, eye, 40f, side);
                Vector3 leaned = BodyLayout.LeanedEye(body, eye, offset);
                Assert.AreEqual(offset, leaned.x - eye.x, Eps, $"{name}, côté {side} : l'œil ne sort pas du décalage réseauté.");
            }
        }
    }

    [Test]
    public void DeboutEtAccroupi_LeLeanGaucheEstLeMiroirExactDuDroit()
    {
        // Postures symétriques : aucun côté ne doit être favorisé. (Allongé, la pose de Mixamo est
        // asymétrique, et la surface touchable la suit volontairement.)
        foreach (var (body, eye, name) in new[] { AllStances[0], AllStances[1] })
        {
            float offset = 0.25f;
            Vector3 right = BodyLayout.LeanedEye(body, eye, offset);
            Vector3 left = BodyLayout.LeanedEye(body, eye, -offset);
            Assert.AreEqual(right.x, -left.x, Eps, $"{name} : le lean n'est pas symétrique en X.");
            Assert.AreEqual(right.y, left.y, Eps, $"{name} : le lean n'est pas symétrique en hauteur.");
            Assert.AreEqual(right.z, left.z, Eps, $"{name} : le lean n'est pas symétrique en profondeur.");
        }
    }

    [Test]
    public void LesJambesNeBougentJamais_CEstToutLeLeanR6()
    {
        foreach (var (body, eye, name) in AllStances)
        {
            BodyLayout.Compute(body, eye, 0f, out _, out _, out ZoneShape upright, out ZoneShape upright2);
            BodyLayout.Compute(body, eye, 0.3f, out _, out _, out ZoneShape leaned, out ZoneShape leaned2);
            Assert.That(Vector3.Distance(upright.a, leaned.a), Is.LessThan(Eps), $"{name} : les jambes ont suivi le lean.");
            Assert.That(Vector3.Distance(upright.b, leaned.b), Is.LessThan(Eps), $"{name} : les jambes ont suivi le lean.");
            Assert.That(Vector3.Distance(upright2.a, leaned2.a), Is.LessThan(Eps), $"{name} : la seconde jambe a suivi le lean.");
            Assert.That(Vector3.Distance(upright2.b, leaned2.b), Is.LessThan(Eps), $"{name} : la seconde jambe a suivi le lean.");
        }
    }

    [Test]
    public void LeBustePivoteSansSeDeformer()
    {
        // Rotation rigide autour du pivot : chaque point garde sa distance au pivot. Un buste qui
        // s'étirerait ou glisserait trahirait un calcul faux.
        foreach (var (body, eye, name) in AllStances)
        {
            BodyLayout.Compute(body, eye, 0.3f, out ZoneShape head, out ZoneShape torso, out _, out _);
            Assert.AreEqual(Vector3.Distance(body.torsoA, body.leanPivot), Vector3.Distance(torso.a, body.leanPivot), Eps, name);
            Assert.AreEqual(Vector3.Distance(body.torsoB, body.leanPivot), Vector3.Distance(torso.b, body.leanPivot), Eps, name);
            Assert.AreEqual(Vector3.Distance(eye, body.leanPivot), Vector3.Distance(head.a, body.leanPivot), Eps, name);
        }
    }

    [Test]
    public void DeboutEtAccroupi_LaTeteDescendUnPeuEnSortant()
    {
        // C'est ce qui distingue un buste qui s'INCLINE d'un corps qui glisse : la tête suit un arc.
        foreach (var (body, eye, name) in new[] { AllStances[0], AllStances[1] })
        {
            Vector3 leaned = BodyLayout.LeanedEye(body, eye, 0.3f);
            Assert.Less(leaned.y, eye.y - 0.01f, $"{name} : la tête ne descend pas, le buste ne s'incline pas.");
        }
    }

    [Test]
    public void Allonge_LeBusteRouleSurLuiMeme_SansEnfoncerLaTeteDansLeSol()
    {
        // Demandé le 2026-10-06 : allongé, le lean est une ROTATION du buste sur lui-même, pas un
        // glissement de côté. La tête reste donc dans son plan (ni avancée ni reculée), sort peu
        // sur le côté, et ne doit jamais passer sous le sol en roulant vers la gauche.
        var (body, eye, _) = AllStances[2];
        foreach (int side in new[] { 1, -1 })
        {
            float offset = side * BodyLayout.MaxLateralOffset(body, eye, 40f, side);
            Vector3 leaned = BodyLayout.LeanedEye(body, eye, offset);
            Assert.AreEqual(eye.z, leaned.z, Eps, $"côté {side} : la tête avance ou recule, le buste ne roule pas autour de l'axe avant.");
            Assert.Greater(leaned.y - body.headRadius, 0.02f, $"côté {side} : la tête touchable passe sous le sol.");
            Assert.Less(Mathf.Abs(offset), 0.2f, $"côté {side} : allongé, rouler ne doit pas sortir la tête comme un lean debout.");
        }
    }

    [Test]
    public void TransitionDePosture_PartDeLaPostureQuitteeEtArriveALaNouvelle()
    {
        var stances = AllStances;
        var (standing, eyeStanding, _) = stances[0];
        var (prone, eyeProne, _) = stances[2];

        BodyLayout.Compute(standing, eyeStanding, 0f, out ZoneShape h0, out ZoneShape t0, out ZoneShape l0, out _);
        BodyLayout.ComputeBlended(standing, eyeStanding, prone, eyeProne, 0f, 0f,
                                  out ZoneShape hb0, out ZoneShape tb0, out ZoneShape lb0, out ZoneShape sb0);
        Assert.That(Vector3.Distance(h0.a, hb0.a), Is.LessThan(Eps), "Au départ, la tête doit être celle de la posture quittée.");
        Assert.That(Vector3.Distance(t0.a, tb0.a), Is.LessThan(Eps));
        Assert.That(Vector3.Distance(l0.b, lb0.b), Is.LessThan(Eps));

        // La seconde jambe, propre à la pose allongée, naît confondue avec la première au lieu de
        // surgir à côté du joueur debout.
        Assert.That(Vector3.Distance(sb0.a, l0.a), Is.LessThan(Eps), "La seconde jambe doit naître de la première.");
        Assert.AreEqual(l0.radius, sb0.radius, Eps);

        BodyLayout.Compute(prone, eyeProne, 0f, out ZoneShape h1, out _, out _, out ZoneShape s1);
        BodyLayout.ComputeBlended(standing, eyeStanding, prone, eyeProne, 1f, 0f,
                                  out ZoneShape hb1, out _, out _, out ZoneShape sb1);
        Assert.That(Vector3.Distance(h1.a, hb1.a), Is.LessThan(Eps), "À l'arrivée, la tête doit être celle de la posture atteinte.");
        Assert.That(Vector3.Distance(s1.b, sb1.b), Is.LessThan(Eps));

        // À mi-chemin, la tête est à mi-hauteur : la surface touchable ne saute pas d'une posture
        // à l'autre, elle descend avec le corps.
        BodyLayout.ComputeBlended(standing, eyeStanding, prone, eyeProne, 0.5f, 0f, out ZoneShape hm, out _, out _, out _);
        Assert.AreEqual((h0.a.y + h1.a.y) * 0.5f, hm.a.y, Eps);
    }

    [Test]
    public void LeDecalageMaximal_CorrespondALAngleMaximalDuBuste()
    {
        foreach (var (body, eye, name) in AllStances)
        {
            float right = BodyLayout.MaxLateralOffset(body, eye, 40f, 1);
            float left = BodyLayout.MaxLateralOffset(body, eye, 40f, -1);
            Assert.AreEqual(40f, BodyLayout.LeanAngle(body, eye, right), 1e-2f, $"{name} : le maximum à droite ne tombe pas à 40°.");
            Assert.AreEqual(-40f, BodyLayout.LeanAngle(body, eye, -left), 1e-2f, $"{name} : le maximum à gauche ne tombe pas à 40°.");
        }
    }

    [Test]
    public void SeuleLaPostureAllongee_AUneSecondeJambe()
    {
        var stances = AllStances;
        Assert.AreEqual(0f, stances[0].body.secondLegRadius, "debout");
        Assert.AreEqual(0f, stances[1].body.secondLegRadius, "accroupi");
        Assert.Greater(stances[2].body.secondLegRadius, 0f, "La jambe repliée de la pose allongée doit être couverte.");
    }

    [Test]
    public void LOeil_ResteALInterieurDuColliderDeMouvement()
    {
        // Sinon, face à un mur, la caméra passerait à travers. Marge pour le plan de coupe proche.
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var locomotion = prefab.GetComponent<PlayerLocomotion>();
        foreach (PlayerLocomotion.Stance stance in System.Enum.GetValues(typeof(PlayerLocomotion.Stance)))
        {
            Vector3 eye = locomotion.EyeLocal(stance);
            float horizontal = new Vector2(eye.x, eye.z).magnitude;
            Assert.Less(horizontal, locomotion.ControllerRadius(stance) - 0.08f, $"{stance} : l'œil dépasse du collider de mouvement.");
        }
    }
}
