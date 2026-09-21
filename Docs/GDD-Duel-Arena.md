# Duel Arena — Game Design Document

*Dernière mise à jour : 21 septembre 2026. Ce document part du GDD initial (voir `Projet FPS Unity.pdf` dans le projet) et l'actualise avec les décisions prises pendant le développement de la vertical slice et de la Phase 1 réseau.*

## 1. Vision du jeu

**Concept central :** un jeu de duel 1v1 nerveux, lisible, compétitif et fun, où chaque match est rapide, intense et influencé (optionnellement) par les spectateurs présents dans le lobby.

**Ton général :** sérieux mais assumant une touche de bizarrerie (niveau 7/10). Le gameplay reste compétitif, mais l'univers et certaines mécaniques peuvent avoir une personnalité et des surprises.

**Objectif principal :** proposer une boucle de gameplay simple à comprendre mais profonde à maîtriser, à la manière d'un "goulag" de Call of Duty ou d'un CS 1v1, mais avec sa propre identité.

## 2. Identité artistique & animations

**Style visuel**

- Stylisé réaliste : proportions humaines, mouvement lisible, silhouettes claires.
- Touches d'exagération pour accentuer la lisibilité et la personnalité.
- Monde cohérent mais avec des détails légèrement "étranges" (level design, éléments de décor, effets spéciaux).

**Animations**

- Stylisées et fluides.
- Importance donnée à la réactivité (peu d'anticipation excessive, gameplay first).
- Impacts et feedbacks visuels exagérés pour renforcer la sensation de skill.

*État actuel : le viewmodel d'arme réel (modèle 3D + animations) n'existe pas encore — la vertical slice utilise un feedback placeholder 100% procédural (réticule OnGUI, trace de tir et marqueur d'impact générés en code, sans dépendance réseau). Le vrai viewmodel est prévu en Phase 3, avec le reste du système d'armes.*

## 3. Gameplay core

**Format**

- 1v1 uniquement.
- Rounds rapides (15–30 secondes).
- Respawn instantané dans le lobby après la mort.

**Objectifs du joueur**

- Éliminer l'adversaire le plus vite possible.
- Lire les intentions de l'adversaire (mindgames).
- Gérer les distances (close, medium, long) grâce à l'arène.

**TTK & rythme**

- TTK cible **confirmé à ≈ 0.7 seconde** de tir soutenu (6 impacts à 17 dégâts, cadence 7 tirs/s, ~420 coups/minute). C'est une valeur de départ, ajustable librement dans l'inspecteur (l'arme est un ScriptableObject, pas de constante en dur) — mais elle a été validée en solo comme point de départ satisfaisant.
- Le skill doit primer sur tout : c'est le principe directeur qui a orienté tous les choix techniques (recul déterministe plutôt qu'aléatoire, hit registration serveur-autoritaire, pas de mouvement à momentum type bhop/slide tant que les fondations réseau ne sont pas validées).

**Mouvement**

- Trois paliers de vitesse : **sneak** (la plus lente, la plus silencieuse) < **marche** < **course** (rapide, bruyante). Le compromis vitesse/discrétion est un vrai choix tactique, pas un simple confort.
- Postures complètes : Debout / Accroupi / Prone, chacune avec sa propre vitesse, sa hauteur de caméra et son empreinte de collision. Le relevé est bloqué s'il n'y a pas la place (pas de "stand-up" sous un plafond bas).
- Lean (penché gauche/droite) avec anti-clipping (un mur proche limite l'amplitude du lean).
- Vault (franchissement d'obstacle) conçu et tuné, actuellement **désactivé en attendant son incrément réseau dédié** (voir doc technique).

**Armes (première itération)**

- 1 ou 2 armes seulement au début.
- **Pas de RNG dans la précision** : c'est un principe non négociable du GDD, qui a une conséquence technique directe — le recul suit une courbe déterministe (AnimationCurve) au lieu d'une dispersion aléatoire. Deux joueurs qui tirent la même rafale dans les mêmes conditions subissent exactement le même recul.
- Recul stylisé/lisible : montée rapide sur les premiers tirs puis plafonnement, avec un pattern horizontal en "S" une fois la rafale avancée — l'arme doit se sentir "domptable" après quelques balles.
- Recul modulé par la posture et le mouvement (une arme est beaucoup plus stable en prone statique qu'en sprint-stop debout), et par la visée (ADS stabilise toujours, quelle que soit la posture).

## 4. Système sonore & lisibilité

*Section ajoutée : le son de contexte n'était pas détaillé dans le GDD initial, mais il s'est révélé être un vrai pilier gameplay pendant le développement, pas un simple habillage.*

- **Primauté du son** : un adversaire proche doit pouvoir entendre les pas, le ramper, les changements de posture, le lean et les tirs d'un autre joueur, en 3D spatial (localisable). C'est une information de gameplay au même titre que le visuel, pas un décor sonore.
- Le sneak reste volontairement silencieux (pas de son de pas) : c'est la contrepartie de sa lenteur, l'option "furtive" du triangle vitesse/bruit/discrétion.
- Le son de tir est aujourd'hui purement cosmétique/local (chaque arme a ses propres clips, variés en pitch). Il devra plus tard être rebranché sur un canal "sons forts" séparé, prévu pour le mode "arène dans le noir" (voir §6 modes alternatifs, Phase 6 dans la doc technique) — sans urgence, juste à ne pas oublier à ce moment-là.

## 5. Arène & level design

**Structure générale**

- Arène de taille intermédiaire, comparable au goulag CoD.
- Zones adaptées aux trois distances de combat : close, medium, long.

**Éléments destructibles/traversables**

- Pas de destruction totale, pour garder la lisibilité.
- Certains éléments traversables par les balles mais pas par la vue (ou l'inverse) — nécessite des layers de collision séparés entre "bloque la vue" et "bloque le tir" (prévu Phase 2).

*État actuel : un blocking de l'arène (inspiré du goulag CoD, avec éléments de couverture de hauteurs variées) est posé et validé — les tests de tir close/medium/long range sont jugés satisfaisants. C'est un blocking de prototype, pas l'asset final : le passage au style visuel définitif est prévu en Phase 2, une fois le layout confirmé par des playtests plus larges.*

## 6. Système de spectateurs

**Rôle des spectateurs**

- Optionnel mais fun.
- Objectif : ne pas les laisser attendre sans rien faire.
- Leurs actions doivent être un plus, jamais un handicap frustrant pour les joueurs en duel.

**Philosophie du rôle spectateur**

- Doit améliorer l'expérience, sans altérer la compétition.
- Mini-influences, effets temporaires, interactions légères.
- Pas de changement majeur au cours du match.

**Types d'interactions (à affiner)**

- Petits bonus environnementaux.
- Buffs légers.
- Effets visuels amusants.

*État actuel : système non commencé. Prévu en Phase 5 de la feuille de route technique, une fois le duel 1v1 de base solide (netcode, hit registration, arène, armes). Les risques techniques déjà identifiés : toute action spectateur devra être validée et rate-limitée côté serveur (même principe anti-triche que les dégâts), et activable/désactivable par lobby.*

## 7. Tonalité & humour

- Le jeu reste nerveux, cadré, sérieux dans son gameplay.
- La bizarrerie apparaît dans : les réactions du public, certains éléments du décor, quelques animations stylisées.
- L'objectif est d'offrir une identité unique sans casser la compétition.

## 8. Vision produit & positionnement

**Public cible**

- Joueurs compétitifs.
- Amateurs de FPS skill-based.
- Fans de duels rapides.

**Piliers**

1. **Lisibilité** — aucun chaos visuel.
2. **Compétition** — skill pur (pas de RNG, hit registration fiable, pas d'avantage de latence structurel).
3. **Fun** — spectateurs, touches visuelles, rythme.
4. **Rapidité d'une session** — boucle courte, addictive.

## 9. État d'avancement (résumé)

**Ce qui est jouable et validé aujourd'hui, à 2 joueurs réels (Host + Client) :**

- Contrôleur complet : déplacement 3 paliers, sprint/sneak/ADS, postures Debout/Accroupi/Prone avec relevé auto en sprint, lean anti-clipping, head bob, kick d'atterrissage — le tout réseauté avec prédiction côté propriétaire et réconciliation serveur.
- Arme hitscan avec TTK confirmé (~0.7s), recul déterministe modulé par posture/mouvement/visée.
- Feedback visuel de tir (réticule, tracer, impact) diffusé à tous les joueurs, pas seulement au tireur.
- Son de contexte 3D spatial (pas différenciés par allure/posture, ramper, changements de posture, lean, tir par arme).
- **Hit registration serveur-autoritaire avec compensation de latence (rewind)** : le serveur revalide chaque tir lui-même, en replaçant temporairement les adversaires à leur position au moment où le tireur a réellement visé. Un client modifié ne peut plus s'auto-déclarer des hits ni appliquer ses propres dégâts.
- Arène de prototype validée sur les trois distances de combat.

**Ce qui manque encore pour un vrai duel compétitif jouable en continu :**

- Un hitbox de tir séparé du collider de mouvement, qui suit la posture ET le lean (aujourd'hui, se pencher expose visuellement le joueur sans que rien de touchable ne bouge avec lui — un vrai trou d'exactitude, prioritaire).
- Boucle de round et conditions de victoire (BO5) — pour l'instant, seule la vie baisse, rien ne termine la partie.
- Vault réseauté (désactivé pour l'instant).
- Vrai flow lobby → file d'attente → duel → résultats.
- Système de spectateurs (rien n'existe encore).
- Viewmodel d'arme réel (actuellement 100% placeholder).

## 10. Prochaines étapes design

- Valider le blocking de l'arène à plus grande échelle (déjà fait pour une première passe — à refaire une fois le hitbox/lean corrigé, ça change ce qui est réellement exposable en couverture).
- Définir précisément les interactions spectateurs.
- Définir la 2ᵉ arme et trancher pré-round vs pickups.
- Travailler la boucle UX du duel (match → lobby → duel → résultats).
- Décider si/quand introduire des power-ups d'arène (recommandation de la doc technique : commencer par des power-ups d'arène plutôt que des pouvoirs de personnage dédiés).

*Voir la doc technique (`feuille-de-route-technique.md`) pour le détail des phases, des risques et des décisions d'implémentation.*
