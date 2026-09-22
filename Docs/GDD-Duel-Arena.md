# Duel Arena — Game Design Document

*L'âme du jeu. Ce document dit ce que Duel Arena **est** et ce qu'il refuse d'être — pas où en est le code. L'état technique, les priorités et les dettes vivent dans `CLAUDE.md`, à la racine du projet.*

---

## 1. Vision

**Un jeu de duel 1v1 nerveux, lisible, compétitif et fun**, où chaque match est rapide, intense, et influencé — optionnellement — par les spectateurs présents dans le lobby.

À la manière d'un « goulag » de Call of Duty ou d'un CS 1v1 : une boucle simple à comprendre, profonde à maîtriser. Mais avec sa propre identité.

**Ton** : sérieux dans le gameplay, avec une bizarrerie assumée (7/10). La compétition ne plaisante pas ; l'univers, si.

## 2. Les quatre piliers

1. **Lisibilité** — aucun chaos visuel. On doit toujours comprendre ce qui vient de se passer.
2. **Compétition** — skill pur. Pas de RNG, hit registration fiable, aucun avantage structurel.
3. **Fun** — spectateurs, touches visuelles, rythme.
4. **Rapidité** — boucle courte, addictive.

## 3. Règles non négociables

Ce sont les contraintes qui arbitrent tous les arbitrages. Si une décision les contredit, c'est la décision qui change.

- **Le skill prime sur tout.**
- **Pas de RNG dans la précision.** Deux joueurs qui tirent la même rafale dans les mêmes conditions subissent exactement le même recul. Le recul est une courbe apprenable, jamais une dispersion aléatoire.
- **Pas d'avantage de latence structurel.** Personne ne gagne parce qu'il héberge la partie.
- **Le son est une information de gameplay**, au même titre que le visuel. Jamais un habillage.
- **Les spectateurs ne décident jamais d'un duel.** Ils l'assaisonnent.

## 4. Gameplay core

**Format** — 1v1 uniquement. Rounds de 15 à 30 secondes. Respawn instantané dans le lobby après la mort.

**Ce que fait le joueur** — éliminer l'adversaire le plus vite possible, lire ses intentions (mindgames), gérer les distances close / medium / long que l'arène impose.

**TTK** — cible ≈ **0,7 seconde** de tir soutenu. Ni trop long (ça devient une guerre d'usure), ni trop court (ça devient une loterie du premier coup d'œil). C'est une valeur de départ validée en jeu, pas un dogme : elle vit dans l'inspecteur, pas en dur dans le code.

**Mouvement** — trois paliers de vitesse : **sneak** (lent, silencieux) < **marche** < **course** (rapide, bruyante). Le compromis vitesse/discrétion est un vrai choix tactique, pas un confort.

Postures complètes Debout / Accroupi / Prone, chacune avec sa vitesse, sa hauteur de vue et son empreinte. Se relever est bloqué s'il n'y a pas la place. Lean gauche/droite avec anti-clipping. Vault pour franchir un obstacle bas.

**Pas de mouvement à momentum** (bhop, slide, air-strafe) : ça favorise l'exécution mécanique sur la lecture, et ça rend le hit registration beaucoup plus dur à rendre honnête. Le jour où ce sera envisagé, ce sera une décision de design consciente, pas une dérive.

**Armes** — une à deux au début. Recul stylisé et lisible : montée rapide sur les premiers tirs puis plafonnement, avec un pattern horizontal en « S » quand la rafale s'allonge. L'arme doit se sentir **domptable** après quelques balles. Le recul est modulé par la posture, le déplacement et la visée — une arme est bien plus stable en prone statique qu'en sprint-stop debout, et l'ADS stabilise toujours.

## 5. Le son comme information

Un adversaire proche doit pouvoir **entendre** les pas, le ramper, les changements de posture, le lean et les tirs — en 3D, localisable. Ce n'est pas du décor : c'est la moitié de l'information disponible dans un duel où on ne voit pas son adversaire la plupart du temps.

Le sneak est volontairement **silencieux**. C'est la contrepartie de sa lenteur, et ce qui rend le triangle vitesse / bruit / discrétion réellement tactique.

Les sons forts (tirs, explosions) formeront plus tard un **canal séparé** des sons de contexte — c'est ce canal qui alimentera le mode « arène dans le noir ».

## 6. Arène & level design

Arène de taille intermédiaire, comparable au goulag CoD. Des zones pour les trois distances de combat : close, medium, long.

Pas de destruction totale — la lisibilité prime. En revanche, certains éléments traversables par les balles mais pas par la vue, ou l'inverse : c'est ce qui crée des angles où tirer sans voir, et voir sans pouvoir tirer.

Le layout se valide par playtest avant de recevoir son habillage visuel définitif. Un beau niveau injouable ne se rattrape pas.

## 7. Spectateurs

Optionnel, mais c'est une partie de l'identité du jeu : **personne n'attend sans rien faire**.

Leurs actions doivent améliorer l'expérience sans altérer la compétition : mini-influences, effets temporaires, interactions légères. Petits bonus environnementaux, buffs légers, effets visuels amusants. Jamais un renversement de match, jamais un handicap frustrant pour les duellistes.

Activable / désactivable par lobby.

## 8. Tonalité & humour

Le jeu reste nerveux, cadré, sérieux dans son gameplay. La bizarrerie vit ailleurs : dans les réactions du public, certains éléments de décor, quelques animations stylisées.

L'objectif est une identité unique qui ne coûte rien à la compétition.

## 9. Identité artistique

**Visuel** — stylisé réaliste : proportions humaines, mouvement lisible, silhouettes claires. Des touches d'exagération pour accentuer la lisibilité et la personnalité. Un monde cohérent, avec des détails légèrement étranges.

**Animations** — stylisées et fluides, avec la réactivité comme priorité absolue (peu d'anticipation, gameplay first). Impacts et feedbacks visuels exagérés pour renforcer la sensation de skill.

## 10. Public cible

Joueurs compétitifs. Amateurs de FPS skill-based. Fans de duels rapides.

## 11. Questions de design encore ouvertes

Ce qui n'est pas tranché, et qui mérite de l'être avant de construire par-dessus.

- **Deuxième arme** : laquelle, et son identité « insolite ».
- **Pré-round vs pickups** : comment le joueur obtient son arme.
- **Interactions spectateurs** : la liste précise, pas seulement le principe.
- **Power-ups d'arène vs pouvoirs de personnage** : préférence actuelle pour des power-ups d'arène, moins risqués pour l'équilibre d'un 1v1.
- **Modes alternatifs** : « arène dans le noir » (écho/sonar) et « Hack & Defend » sont des pistes, pas des engagements.
- **Boucle UX complète** : lobby → file d'attente → duel → résultats.
