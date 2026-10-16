using UnityEngine;

/// <summary>
/// Un bloc fissuré de la tribune, d'où les spectateurs arrachent leurs cailloux (GDD § 9 : « il faut
/// d'abord casser un morceau de la plateforme pour en récupérer un, ce qui prend du temps »). On le
/// frappe à coups de poing ; au dernier coup, un caillou tombe devant lui, à ramasser. Le bloc met
/// ensuite un moment à se refendre.
///
/// Son état (coups reçus, épuisé) vit sur le serveur, dans la Tribune, qui le publie ; ce composant ne
/// fait que l'AFFICHER (catégorie C) : le morceau qui se détache tremble à chaque coup, disparaît quand
/// le bloc est épuisé, et revient. Son collider fait partie du décor : il arrête les balles et les
/// objets lancés, comme un mur.
/// </summary>
[DisallowMultipleComponent]
public class RockSource : MonoBehaviour
{
    [Tooltip("Le morceau qui se détache : il tremble à chaque coup et disparaît quand le bloc est épuisé.")]
    [SerializeField] private Transform chunk;
    [Tooltip("Où tombe le caillou arraché, devant le bloc, au sol de la tribune.")]
    [SerializeField] private Transform dropPoint;
    [Tooltip("Le volume que le poing doit atteindre (le bloc lui-même).")]
    [SerializeField] private Collider body;

    private Vector3 chunkRestPosition;
    private Vector3 chunkRestScale = Vector3.one;
    private float shakeUntil;
    private int shownHits = -1;
    private bool shownDepleted;
    private float loosened; // de 0 (intact) à 1 (prêt à céder)

    public Collider Body => body;
    public Vector3 DropPosition => dropPoint != null ? dropPoint.position : transform.position + transform.forward * 0.6f;

    /// <summary>L'index du bloc dans la Tribune (l'état réseauté est rangé par index).</summary>
    public int Index { get; set; } = -1;

    private void Awake()
    {
        if (body == null) body = GetComponent<Collider>();
        if (chunk != null)
        {
            chunkRestPosition = chunk.localPosition;
            chunkRestScale = chunk.localScale;
        }
    }

    /// <summary>Le point du bloc le plus proche de `from` (pour le poing qui l'atteint).</summary>
    public Vector3 ClosestPoint(Vector3 from) => body != null ? body.ClosestPoint(from) : transform.position;

    /// <summary>Montre l'état publié par le serveur : `hits` coups reçus sur `hitsToBreak`, ou épuisé.</summary>
    public void Show(int hits, int hitsToBreak, bool depleted)
    {
        if (hits != shownHits && !depleted && hits > 0) shakeUntil = Time.time + 0.18f;
        shownHits = hits;
        shownDepleted = depleted;
        // Le morceau sort un peu plus du bloc à chaque coup : on voit qu'il va céder.
        loosened = hitsToBreak > 0 ? Mathf.Clamp01((float)hits / hitsToBreak) : 0f;
        if (chunk == null) return;
        chunk.gameObject.SetActive(!depleted);
        chunk.localScale = chunkRestScale;
        chunk.localPosition = chunkRestPosition + Vector3.forward * (0.06f * loosened);
    }

    private void Update()
    {
        if (chunk == null || shownDepleted) return;
        Vector3 shake = Time.time < shakeUntil
            ? new Vector3(Mathf.Sin(Time.time * 90f), Mathf.Sin(Time.time * 70f + 1f), 0f) * 0.015f
            : Vector3.zero;
        chunk.localPosition = chunkRestPosition + Vector3.forward * (0.06f * loosened) + shake;
    }
}
