using NUnit.Framework;
using UnityEngine;

/// <summary>
/// La cible de la salle de précision du stand (AimRange, 2026-10-28) : les points de chaque anneau, ce qui compte comme un
/// impact sur sa face, le chariot qui l'emmène à la distance demandée, le pupitre et ses boutons, la cible imprimée, les
/// impacts qui s'y peignent et le bouton qui les efface, la ligne des comptes du HUD. Ce qui casse ici ne lève aucune
/// erreur : un anneau qui compte un point de trop, un chariot qui dépasse sa place et revient, deux boutons qui se
/// chevauchent, une cible « effacée » qui garde ses impacts.
/// </summary>
public class AimRangeTests
{
    [Test]
    public void LesPoints_ValentDixAuCentre_UnDeMoinsParAnneau_ZeroAuDela()
    {
        Assert.AreEqual(10, AimRange.RingScore(Vector2.zero));
        for (int k = 1; k <= AimRange.Rings; k++)
        {
            float edge = AimRange.RingStep * (AimRange.Rings + 1 - k);
            Assert.AreEqual(k, AimRange.RingScore(new Vector2(edge - 0.001f, 0f)), $"Juste dans l'anneau {k}.");
            Assert.AreEqual(k - 1, AimRange.RingScore(new Vector2(0f, -(edge + 0.001f))), $"Juste au-delà de l'anneau {k}.");
        }
        // Le noir (du 7 au 10), dans une direction quelconque ; le liseré du bord, hors des anneaux.
        Assert.AreEqual(7, AimRange.RingScore(new Vector2(-1f, 0.25f).normalized * (AimRange.RingStep * 3.75f)));
        Assert.AreEqual(6, AimRange.RingScore(new Vector2(1f, -1f).normalized * (AimRange.RingStep * 4.1f)));
        Assert.AreEqual(0, AimRange.RingScore(new Vector2(0f, AimRange.BoardRadius - 0.02f)));
    }

    [Test]
    public void UnImpact_NeCompteQueSurLaFaceDeLaCible()
    {
        var go = new GameObject("Cible (test)");
        try
        {
            Transform board = go.transform;
            float front = AimRange.BoardThickness * 0.5f, c = AimRange.BoardCenterHeight;
            board.SetPositionAndRotation(new Vector3(2f, c, 20f + front), Quaternion.identity);
            Assert.IsTrue(AimRange.TryBoardPoint(board, new Vector3(2.1f, c + 0.15f, 20f), Vector3.back, out Vector2 local), "Sur la face.");
            Assert.AreEqual(0.1f, local.x, 1e-4f, "x vers la droite du tireur.");
            Assert.AreEqual(0.15f, local.y, 1e-4f, "y vers le haut.");
            Assert.IsTrue(AimRange.TryBoardPoint(board, new Vector3(2f + 1.3f, c + 1.3f, 20f), Vector3.back, out _), "Au bord du disque, dessus.");
            Assert.IsFalse(AimRange.TryBoardPoint(board, new Vector3(2.1f, c + 0.15f, 20f + 2f * front), Vector3.forward, out _), "Le dos.");
            Assert.IsFalse(AimRange.TryBoardPoint(board, new Vector3(2f + AimRange.BoardRadius, c, 20f + front), Vector3.right, out _), "Le chant.");
            Assert.IsFalse(AimRange.TryBoardPoint(board, new Vector3(2f + 1.5f, c + 1.5f, 20f), Vector3.back, out _), "Dans le coin du carré, hors du disque.");
            Assert.IsFalse(AimRange.TryBoardPoint(board, new Vector3(2.1f, c + 0.15f, 20f), Vector3.forward, out _), "Une normale qui ne regarde pas le tireur.");

            // Tournée, la cible garde ses axes : la place sur la face suit la cible.
            board.rotation = Quaternion.Euler(0f, 30f, 0f);
            Vector3 point = board.TransformPoint(new Vector3(-0.2f, 0.3f, -front));
            Assert.IsTrue(AimRange.TryBoardPoint(board, point, -board.forward, out local));
            Assert.AreEqual(-0.2f, local.x, 1e-4f);
            Assert.AreEqual(0.3f, local.y, 1e-4f);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void LeChariot_VaALaDistanceDemandee_SansJamaisLaDepasser_EtFaitDemiTour()
    {
        foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 144f })
        {
            float position = 5f, velocity = 0f, time = 0f, fastest = 0f;
            while (position != 50f && time < 20f)
            {
                float before = position;
                position = AimRange.StepCarrier(position, 50f, dt, ref velocity);
                Assert.LessOrEqual(position, 50f, "Il ne dépasse jamais sa place.");
                Assert.GreaterOrEqual(position, before - 1e-5f, "Il ne recule pas en allant au fond.");
                fastest = Mathf.Max(fastest, (position - before) / dt);
                time += dt;
            }
            Assert.AreEqual(50f, position, $"Il arrive (pas de {dt:0.000} s).");
            Assert.AreEqual(0f, velocity, "Arrivé, il s'arrête.");
            Assert.LessOrEqual(fastest, AimRange.CarrierSpeed + 0.01f, "Jamais plus vite que sa vitesse de croisière.");
            Assert.That(time, Is.InRange(45f / AimRange.CarrierSpeed, 8f), "Ni téléporté, ni traînant : de 5 à 50 m en 4,5 à 8 s.");

            // À mi-chemin, on le rappelle à 10 m : il freine, fait demi-tour, et s'arrête sur sa nouvelle place, sans la passer.
            position = 5f;
            velocity = 0f;
            for (time = 0f; time < 2f; time += dt) position = AimRange.StepCarrier(position, 50f, dt, ref velocity);
            Assert.Greater(position, 10f, "Garde : il est parti au-delà de 10 m.");
            float farthest = position;
            for (time = 0f; position != 10f && time < 20f; time += dt)
            {
                position = AimRange.StepCarrier(position, 10f, dt, ref velocity);
                farthest = Mathf.Max(farthest, position);
                Assert.GreaterOrEqual(position, 10f, "Revenu, il ne passe pas sa nouvelle place.");
            }
            Assert.AreEqual(10f, position, "Il revient à 10 m.");
            Assert.Greater(farthest - 10f, 0f, "Garde : il a dû freiner avant de revenir.");
        }
        Assert.AreEqual(25, AimRange.Nearest(23), "Une distance hors du pupitre va à la plus proche.");
    }

    [Test]
    public void LePupitre_ASesBoutonsDansSaPlaque_SansQuIlsSeChevauchent()
    {
        var plate = new Rect(-AimRange.ConsoleSize * 0.5f, AimRange.ConsoleSize);
        for (int i = 0; i < AimRange.ButtonCount; i++)
        {
            Rect r = AimRange.ButtonRect(i);
            Assert.IsTrue(plate.Contains(r.min) && plate.Contains(r.max), $"Le bouton {i} est sur la plaque.");
            Assert.That(r.width, Is.GreaterThanOrEqualTo(0.15f), $"Le bouton {i} se vise (assez large).");
            Assert.That(r.height, Is.GreaterThanOrEqualTo(0.12f), $"Le bouton {i} se vise (assez profond).");
            Assert.AreEqual(i, AimRange.ButtonAt(r.center), $"Le milieu du bouton {i} est le bouton {i}.");
            for (int j = i + 1; j < AimRange.ButtonCount; j++)
                Assert.IsFalse(r.Overlaps(AimRange.ButtonRect(j)), $"Les boutons {i} et {j} se chevauchent.");
        }
        Assert.AreEqual(-1, AimRange.ButtonAt(Vector2.zero), "Entre deux rangées, pas de bouton.");
        CollectionAssert.AreEqual(new[] { 5, 10, 15, 20, 25, 30, 40, 50 }, AimRange.Distances, "Les distances du pupitre.");
    }

    [Test]
    public void UnRegard_TrouveLeBoutonVise_APortee_EtPasAuDela()
    {
        var go = new GameObject("Pupitre (test)");
        try
        {
            Transform console = go.transform;
            console.SetPositionAndRotation(new Vector3(1f, 0.962f, 2f), Quaternion.LookRotation(Vector3.down, Vector3.forward));
            var eye = new Vector3(1.6f, 1.65f, 1.2f);
            for (int i = 0; i < AimRange.ButtonCount; i++)
            {
                Rect r = AimRange.ButtonRect(i);
                Vector3 target = console.TransformPoint(new Vector3(r.center.x, r.center.y, 0f));
                Assert.AreEqual(i, AimRange.AimedButton(console, eye, (target - eye).normalized, 2.2f), $"Le bouton {i}, visé.");
                Vector3 high = eye + Vector3.up * 3f;
                Assert.AreEqual(-1, AimRange.AimedButton(console, high, (target - high).normalized, 2.2f), $"Le bouton {i}, hors de portée.");
                Vector3 below = target + new Vector3(0.3f, -0.4f, -0.3f);
                Assert.AreEqual(-1, AimRange.AimedButton(console, below, (target - below).normalized, 2.2f), $"Le bouton {i}, vu par en dessous.");
            }
            Assert.AreEqual(-1, AimRange.AimedButton(console, eye, Vector3.up, 2.2f), "Un regard qui ne va pas vers le pupitre.");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>La cible imprimée montre ce que comptent les points : le noir couvre le 7 au 10, le papier le 1 au 6, un
    /// liseré sombre jusqu'au bord du disque, et le noir au-delà (l'hologramme, additif, le laisse transparent) ; les
    /// anneaux sont tracés.</summary>
    [Test]
    public void LaCibleImprimee_MontreLeNoirDu7Au10_EtLePapierAuDela()
    {
        int size = AimRange.TextureSize;
        var pixels = new Color32[size * size];
        AimRange.DrawTarget(pixels, size);
        Color32 At(float x, float y)
        {
            int px = Mathf.FloorToInt((x / AimRange.BoardDiameter + 0.5f) * size), py = Mathf.FloorToInt((y / AimRange.BoardDiameter + 0.5f) * size);
            return pixels[py * size + px];
        }
        float Luma(Color32 c) => (c.r + c.g + c.b) / 3f;
        float step = AimRange.RingStep;
        Assert.Less(Luma(At(step * 3.5f, 0f)), 60f, "Dans le 7 : du noir.");
        Assert.Less(Luma(At(0f, step * 1.5f)), 60f, "Dans le 9 : du noir.");
        Assert.Greater(Luma(At(step * 4.5f, 0f)), 180f, "Dans le 6 : du papier.");
        Assert.Greater(Luma(At(-step * 9.5f, 0f)), 180f, "Dans le 1 : du papier.");
        Assert.That(Luma(At(0f, -(step * 10f + AimRange.BoardRadius) * 0.5f)), Is.InRange(35f, 90f), "Entre le dernier anneau et le bord, le liseré.");
        Assert.Less(Luma(At(AimRange.BoardRadius * 0.95f, AimRange.BoardRadius * 0.95f)), 5f, "Hors du disque, du noir : l'hologramme y est transparent.");
        Assert.Less(Luma(At(AimRange.RingStep * 5f, 0f)), 120f, "L'anneau entre le 5 et le 6 est tracé sur le papier.");
        Assert.Greater(Luma(At(0f, -AimRange.RingStep * 3f)), 120f, "L'anneau entre le 7 et le 8 est tracé, clair, dans le noir.");
    }

    [Test]
    public void UnImpact_SePeintSurLaCible_EtEffacerLaRendNeuve()
    {
        var go = new GameObject("Salle de précision (test)");
        try
        {
            var aim = go.AddComponent<AimRange>();
            Texture2D texture = aim.Texture;
            Assert.IsNotNull(texture, "La cible a sa texture dès qu'elle est posée.");
            Color32[] fresh = texture.GetPixels32();

            var impact = new Vector2(0.9f, -0.41f);   // à 99 cm du centre : le 5
            aim.RegisterImpact(impact);
            Assert.AreEqual(1, aim.ImpactCount);
            Assert.AreEqual(5, aim.LastShotScore);
            Assert.AreEqual(5f, aim.MeanScore);
            Color32[] marked = texture.GetPixels32();
            int size = AimRange.TextureSize;
            int cx = Mathf.FloorToInt((impact.x / AimRange.BoardDiameter + 0.5f) * size), cy = Mathf.FloorToInt((impact.y / AimRange.BoardDiameter + 0.5f) * size);
            Color32 rim = marked[cy * size + cx + 4];
            Assert.IsTrue(rim.r > 150 && rim.g < 90, $"Le pourtour de l'impact est rouge ({rim}).");
            int changed = 0;
            for (int i = 0; i < fresh.Length; i++)
                if (!fresh[i].Equals(marked[i])) changed++;
            Assert.That(changed, Is.InRange(80, 320), "L'impact ne marque que sa place (un point de 7 cm).");

            aim.RegisterImpact(Vector2.zero);
            Assert.AreEqual(2, aim.ImpactCount);
            Assert.AreEqual(7.5f, aim.MeanScore, 1e-4f, "La moyenne des deux impacts (5 et 10).");

            aim.Clear();
            Color32[] cleared = texture.GetPixels32();
            int firstDifference = -1;
            for (int i = 0; i < fresh.Length && firstDifference < 0; i++)
                if (!fresh[i].Equals(cleared[i])) firstDifference = i;
            Assert.AreEqual(-1, firstDifference, "Effacée, la cible est comme neuve.");
            Assert.AreEqual(0, aim.ImpactCount);
            Assert.AreEqual(0, aim.LastShotImpacts);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>Les impacts du dernier tir ont le cœur blanc ; au tir suivant (une autre image), ils le perdent.</summary>
    [Test]
    public void LesImpactsDuDernierTir_OntLeCoeurBlanc_PuisLePerdent()
    {
        var go = new GameObject("Salle de précision (test)");
        try
        {
            var aim = go.AddComponent<AimRange>();
            int size = AimRange.TextureSize;
            Color32 Center(Vector2 local)
            {
                Color32[] p = aim.Texture.GetPixels32();
                int x = Mathf.FloorToInt((local.x / AimRange.BoardDiameter + 0.5f) * size), y = Mathf.FloorToInt((local.y / AimRange.BoardDiameter + 0.5f) * size);
                return p[y * size + x];
            }
            var first = new Vector2(-0.31f, 0.2f);
            var second = new Vector2(0.27f, 0.33f);
            aim.RegisterImpact(first);
            Assert.Greater(Center(first).g, 200, "Le dernier tir a le cœur blanc.");
            // Le tir suivant arrive à une autre image (en mode Édition, l'image ne change pas : on la fait changer).
            typeof(AimRange).GetField("volleyFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(aim, -5);
            aim.RegisterImpact(second);
            Assert.Less(Center(first).g, 60, "Le tir d'avant a perdu son cœur blanc.");
            Assert.Greater(Center(second).g, 200, "Le nouveau tir a le sien.");
            Assert.AreEqual(1, aim.LastShotImpacts, "Le dernier tir n'a qu'un impact.");
            Assert.AreEqual(2, aim.ImpactCount);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>Après un rechargement du code, une salle restée ouverte peut garder une texture et des pixels d'une autre
    /// taille (Unity y recopie les champs privés d'un type sérialisable ; vu le 2026-10-28, quand la cible est passée de 512
    /// à 768 pixels : SetPixels32 levait une exception) : la cible se refait à la bonne taille.</summary>
    [Test]
    public void UneTextureDUneAutreTaille_SeRefaitALaBonne()
    {
        var go = new GameObject("Salle de précision (test)");
        var stale = new Texture2D(16, 16);
        Texture2D built = null;
        try
        {
            var aim = go.AddComponent<AimRange>();
            built = aim.Texture;   // celle d'OnEnable : la vieille prend sa place
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(AimRange).GetField("texture", flags).SetValue(aim, stale);
            typeof(AimRange).GetField("basePixels", flags).SetValue(aim, new Color32[16 * 16]);
            typeof(AimRange).GetField("pixels", flags).SetValue(aim, new Color32[16 * 16]);
            typeof(AimRange).GetMethod("BuildTexture", flags).Invoke(aim, null);
            Assert.AreEqual(AimRange.TextureSize, aim.Texture.width, "La cible a sa taille.");
            aim.RegisterImpact(Vector2.zero);
            Assert.AreEqual(1, aim.ImpactCount, "Et se peint.");
        }
        finally
        {
            Object.DestroyImmediate(go);
            if (stale != null) Object.DestroyImmediate(stale);
            if (built != null) Object.DestroyImmediate(built);
        }
    }

    [Test]
    public void LaLigneDesComptes_SEcritALaFrancaise()
    {
        Assert.AreEqual("12 impacts   ·   moyenne 8,4   ·   dernier tir 9", AimRange.StatusLine(12, 8.4166f, 9, 1));
        Assert.AreEqual("1 impact   ·   moyenne 10,0   ·   dernier tir 10", AimRange.StatusLine(1, 10f, 10, 1));
        Assert.AreEqual("16 impacts   ·   moyenne 6,5   ·   dernier tir 52 (8 impacts)", AimRange.StatusLine(16, 6.5f, 52, 8));
        StringAssert.StartsWith("Aucun impact", AimRange.StatusLine(0, 0f, 0, 0));
    }
}
