---
name: ressources-libres
description: Chercher, vérifier, télécharger et ranger des ressources libres pour Duel Arena (textures, polices, modèles 3D, HDRI, sons, paquets Unity) — licences compatibles avec un jeu vendu sur Steam, téléchargement sûr, provenance tenue à jour dans Docs/. À utiliser dès qu'il faut une texture, une police, un modèle ou un son trouvé sur internet, ou un paquet Unity à installer, même si l'utilisateur dit seulement « trouve-moi un asset » ou « télécharge ce qu'il faut ».
---

# Ressources libres pour Duel Arena

Le jeu sera **vendu** (Steam, autour de 5 €, GDD § 16) : une ressource n'entre dans le projet que si sa licence permet un usage commercial, sans contrepartie impossible à tenir. L'utilisateur a autorisé les téléchargements le 2026-10-27 (« tu peux aller sur internet pour télécharger et installer des packages Unity ») ; une ressource dont la licence est douteuse se signale, elle n'entre pas.

## D'abord : faut-il télécharger ?

La DA (bible § 10, § 16) est un **low poly propre** à usure procédurale : armes, robots et props du décor se font dans Blender, par script (skill `blender`), et prennent leur usure dans le shader. Un modèle ou une texture photographique trouvés en ligne détonnent presque toujours. Télécharger sert surtout à :
- des **polices** (pochoirs, signalétique), rastérisées en géométrie dans Blender ;
- des **sons** (voir `Docs/Sons-Provenance.md` et `Tools/Sons/`) ;
- un **détail** discret (une carte de normales de béton, un masque de crasse), retinté dans la palette ;
- des **paquets Unity** du registre (`com.unity.*`).

## Où chercher (licences sûres)

| Source | Licence | Pour |
|---|---|---|
| Google Fonts (dépôt `github.com/google/fonts`, dossier `ofl/`) | SIL OFL 1.1 | polices (pochoir : Saira Stencil One, Big Shoulders Stencil, Allerta Stencil) |
| ambientCG (`ambientcg.com`) | CC0 | textures PBR (béton, métal, grillage avec alpha) |
| Poly Haven (`polyhaven.com`) | CC0 | textures, HDRI, quelques modèles |
| Kenney (`kenney.nl`), Quaternius (`quaternius.com`, `poly.pizza`) | CC0 | modèles low poly (souvent trop colorés : à retinter, à juger sur l'image) |
| Registre d'Unity (`package_search`, `package_add`) | licence d'Unity | Splines, Polybrush, ProBuilder… |

À éviter : l'Asset Store (il faut le compte de l'utilisateur, et accepter ses conditions : c'est à lui de le faire), toute licence **NC** (non commerciale), « gratuit pour un usage personnel », une licence introuvable, un fichier rehébergé par un tiers. Une CC-BY demande une attribution dans les crédits : la noter.

## Télécharger sans risque

1. Chaque téléchargement dans **son propre dossier neuf** du scratchpad (`telechargements/<ressource>/`), par `curl -sSL`, depuis la source officielle.
2. Vérifier ce qui est arrivé (`file`, taille), lire le fichier de licence fourni, calculer le SHA-256. Ne jamais exécuter un fichier téléchargé ; un script Python qui lit ces fichiers tourne en `python -I`, depuis un autre dossier.
3. Ne copier dans le projet que ce qui sert, **avec sa licence** à côté :
   - ce qui ne sert qu'à Blender (une police) : `Tools/Blender/polices/` — rien de la police n'entre dans la build, seule la géométrie des lettres ;
   - ce qu'Unity utilise : sous `Assets/_ProjectArt/` ou `Assets/_ProjectArena/<domaine>/`.
4. Vérifier que le binaire est en LFS : `git check-attr filter -- <fichier>` doit répondre `lfs`.
5. **Tenir la provenance** dans `Docs/Decor-Provenance.md` (les sons : `Docs/Sons-Provenance.md`) : nom, auteur, licence, adresse exacte, date, SHA-256, où la ressource sert. Une ressource sans ligne de provenance est à retirer.

## Un paquet Unity

- Chercher par `package_search`, lire sa page (documentation d'Unity), installer par `package_add` : il modifie `Packages/manifest.json`, partagé par toutes les sessions et suivi par Git.
- **Prévenir les autres sessions avant** (`ListAgents`, `SendMessage`) : l'installation recompile tout l'Editor.
- Une référence d'assembly à ajouter à l'asmdef qui s'en sert (ex. `DuelArena.Editor` : `Unity.ProBuilder`, `Unity.RenderPipelines.Universal.Runtime`).
- Le noter dans `CLAUDE.md` (ce qu'il apporte, pourquoi).
