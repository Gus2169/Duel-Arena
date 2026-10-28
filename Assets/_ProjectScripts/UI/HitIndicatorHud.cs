using System.Collections.Generic;
using DuelArena.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// La vue d'un robot touché par un caillou (2026-10-27, demandé par l'utilisateur, d'après le sang et la zone
/// rouge circulaire de Call of Duty) : une zone rouge sur un cercle autour du réticule, tournée vers l'endroit
/// d'où le caillou est parti, et un grésillement électrique au bord de l'écran, du même côté. Sur /GameUI, à
/// côté de SpectatorHud ; HitIndicatorOverlay dessine.
///
/// Catégorie C, chez le robot touché seulement : le serveur lui envoie d'où partait le caillou
/// (SpectatorController.RockHitClientRpc), un duelliste à chaque caillou, un spectateur quand sa garde ne l'a
/// pas arrêté. L'indicateur pointe vers ce point du monde et tourne avec la vue tant qu'il dure, comme celui de
/// Call of Duty. Il sonde le compteur des cailloux reçus (SpectatorController.RockHitCount) au lieu de s'y
/// abonner : le robot du joueur peut changer sous le HUD.
/// </summary>
[DisallowMultipleComponent]
public class HitIndicatorHud : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    [Header("La zone autour du réticule (ressenti)")]
    [Tooltip("Rayon du cercle (part de la hauteur de l'écran).")]
    [SerializeField] private float ringRadius = 0.17f;
    [Tooltip("Épaisseur de la zone (part de la hauteur de l'écran).")]
    [SerializeField] private float ringThickness = 0.035f;
    [Tooltip("Demi-largeur de la zone (degrés de part et d'autre de la direction du lanceur).")]
    [SerializeField] private float ringHalfAngle = 26f;
    [Tooltip("La zone reste pleine ce temps (s), puis s'efface en ringFade secondes.")]
    [SerializeField] private float ringHold = 0.6f;
    [SerializeField] private float ringFade = 1.2f;
    [Tooltip("Opacité de la zone. Le jeu se rend en espace linéaire : sur un fond sombre, une opacité paraît bien plus forte qu'elle n'est (0,22 se mesurait 0,49 à l'écran).")]
    [Range(0f, 1f)]
    [SerializeField] private float ringOpacity = 0.55f;

    [Header("Le grésillement au bord de l'écran (ressenti)")]
    [Tooltip("Durée des arcs électriques et de la lueur du bord (s) : un éclat qui retombe vite.")]
    [SerializeField] private float flashDuration = 0.45f;
    [Tooltip("Rayon de la lueur rouge du bord (part de la hauteur de l'écran).")]
    [SerializeField] private float glowRadius = 0.15f;
    [Tooltip("Opacité de la lueur, en son centre : faible exprès (voir l'opacité de la zone).")]
    [Range(0f, 1f)]
    [SerializeField] private float glowOpacity = 0.06f;

    [Tooltip("Au plus tant de coups montrés à la fois : le plus ancien s'efface.")]
    [Min(1)]
    [SerializeField] private int maxMarks = 4;

    private HitIndicatorOverlay overlay;
    private NetworkObject shownPlayer;
    private SpectatorController spectator;
    private FirstPersonView firstPersonView;
    private int seenHits;
    private bool drawn;
    // D'où partait chaque caillou montré, dans l'ordre des marques de l'élément.
    private readonly List<Vector3> sources = new List<Vector3>();

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        overlay = document != null ? document.rootVisualElement.Q<HitIndicatorOverlay>("hitIndicator") : null;
        shownPlayer = null;
        drawn = false;
    }

    private void LateUpdate()
    {
        if (overlay == null) return;
        NetworkObject local = GameHud.LocalPlayerObject;
        if (local != shownPlayer) Follow(local);
        Camera view = firstPersonView != null ? firstPersonView.PlayerCamera : null;
        if (spectator == null || view == null)
        {
            Clear();
            return;
        }

        // Un caillou de plus : une marque de plus, la plus ancienne s'efface s'il y en a trop.
        int hits = spectator.RockHitCount;
        if (hits != seenHits)
        {
            seenHits = hits;
            if (overlay.Marks.Count >= maxMarks)
            {
                overlay.Marks.RemoveAt(0);
                sources.RemoveAt(0);
            }
            overlay.Marks.Add(new HitIndicatorOverlay.Mark { seed = ElectricArc.Hash((uint)hits * 0x9e3779b9U) });
            sources.Add(spectator.LastRockHitFrom);
        }

        // Chaque marque vieillit, et suit le regard : elle pointe toujours vers le point du lancer.
        float life = Mathf.Max(ringHold + ringFade, flashDuration);
        Transform eye = view.transform;
        for (int i = overlay.Marks.Count - 1; i >= 0; i--)
        {
            HitIndicatorOverlay.Mark mark = overlay.Marks[i];
            mark.age += Time.deltaTime;
            if (mark.age >= life)
            {
                overlay.Marks.RemoveAt(i);
                sources.RemoveAt(i);
                continue;
            }
            mark.angle = ScreenAngle(eye.forward, eye.position, sources[i]);
            overlay.Marks[i] = mark;
        }

        // Un écran par-dessus le jeu, ou le noir tombé : rien (comme le réticule).
        bool shown = overlay.Marks.Count > 0 && !GameHud.HidesCrosshair;
        overlay.EnableInClassList("hidden", !shown);
        if (shown)
        {
            Configure();
            overlay.MarkDirtyRepaint();
        }
        drawn = shown;
    }

    private void Follow(NetworkObject local)
    {
        shownPlayer = local;
        spectator = local != null ? local.GetComponent<SpectatorController>() : null;
        firstPersonView = local != null ? local.GetComponent<FirstPersonView>() : null;
        // Ce qu'il avait déjà reçu ne se montre pas : seulement ce qui arrive maintenant.
        seenHits = spectator != null ? spectator.RockHitCount : 0;
        Clear();
    }

    private void Clear()
    {
        if (overlay.Marks.Count == 0 && !drawn) return;
        overlay.Marks.Clear();
        sources.Clear();
        overlay.EnableInClassList("hidden", true);
        overlay.MarkDirtyRepaint();
        drawn = false;
    }

    /// <summary>Les réglages de l'inspecteur, à chaque image : on les règle en jouant.</summary>
    private void Configure()
    {
        overlay.ringRadius = ringRadius;
        overlay.ringThickness = ringThickness;
        overlay.ringHalfAngle = ringHalfAngle;
        overlay.ringHold = ringHold;
        overlay.ringFade = ringFade;
        overlay.ringOpacity = ringOpacity;
        overlay.flashDuration = flashDuration;
        overlay.glowRadius = glowRadius;
        overlay.glowOpacity = glowOpacity;
    }

    /// <summary>
    /// D'où vient un coup, à l'écran (degrés) : l'angle, à plat, entre le regard et la direction de `source`,
    /// positif vers la droite : 0 devant (en haut du cercle), 90 à droite, ±180 derrière (en bas). La hauteur ne
    /// compte pas : un caillou lancé de la passerelle, au-dessus du duelliste, vient de devant s'il vient de
    /// devant. Fonction pure, testée.
    /// </summary>
    public static float ScreenAngle(Vector3 viewForward, Vector3 viewPosition, Vector3 source)
    {
        Vector3 forward = new Vector3(viewForward.x, 0f, viewForward.z);
        Vector3 toward = new Vector3(source.x - viewPosition.x, 0f, source.z - viewPosition.z);
        if (forward.sqrMagnitude < 1e-8f || toward.sqrMagnitude < 1e-8f) return 0f;
        return Vector3.SignedAngle(forward, toward, Vector3.up);
    }
}
