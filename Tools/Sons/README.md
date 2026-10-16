# Outils des sons (2026-10-13)

Choisir et préparer les sons du jeu depuis la banque de l'utilisateur (`Téléchargements/Bank Sounds`,
18 000 fichiers). On ne peut pas écouter ici : on choisit sur les noms, les mesures et les spectrogrammes,
et l'utilisateur juge à l'oreille.

Dans un venv **hors du projet** (`python -m venv …`, puis `pip install numpy scipy soundfile matplotlib`),
toujours avec `python -I` (la banque est une donnée téléchargée) :

| Script | Rôle |
|---|---|
| `analyse.py <banque> <catalogue.csv>` | Mesure chaque son unique : durée, niveau, attaque, longueur utile, transitoires, timbre. |
| `familles.py <catalogue.csv> <motif>…` | Résume un ou plusieurs dossiers par familles de noms. |
| `planche.py <sortie.png> <titre> <fichier>…` | Planche d'enveloppes et de spectrogrammes, pour comparer des candidats à l'œil. |
| `rafale.py <dossier FusilAssaut> <sortie.wav> <coups> <cadence> <fondu>` | Simule une rafale comme `WeaponAudio` la joue (premier coup, coups, queue). |
| `selection.py` | Les choix : chaque famille, ses fichiers sources, ses options (début gardé, coupe, fondu, `raw` pour une boucle copiée telle quelle). |
| `preparer.py <banque> <SoundsBank> <selection.py> <Docs/Sons-Provenance.md> [mesures.json]` | Écrit les WAV mono dans le projet et le tableau de provenance (l'en-tête et les licences du document, écrits à la main, sont gardés). |

Après `preparer.py` : réglages d'import (effets en mono, PCM, préchargés, sans normalisation Unity ; une
boucle en stéréo Vorbis en mémoire), puis câblage des clips dans les assets (`WeaponData`, `PlayerSounds`,
`SpectatorSounds`, `GameSounds`, taser du prefab).

La musique (`Assets/_ProjectArt/Audio/Musique/`) ne passe pas par ces outils : des MP3 copiés tels quels,
importés en streaming Vorbis stéréo. Sa provenance (page, auteur, licence, Content ID) s'écrit à la main
dans `Docs/Sons-Provenance.md`.
