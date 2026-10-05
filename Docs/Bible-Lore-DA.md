# Duel Arena — Bible Lore & Direction artistique

*Version de référence de la bible, tenue dans le projet. Base : le PDF `Duel_Arena_Bible_Lore_DA.pdf` de l'utilisateur (premier jet du 2026-10-04), auquel ont été appliqués ses arbitrages. **Le PDF n'est plus modifié** : c'est ce fichier qui évolue (arbitrage R3). Chaque écart avec le PDF est listé dans le journal en fin de document, avec le code de l'arbitrage qui l'a décidé.*

*Rôle : la bible fait foi pour **le monde et l'apparence** du jeu (lore, personnages, armes, arènes, ton, direction artistique). Le GDD (`Docs/GDD-Duel-Arena.md`) fait foi pour **les règles du jeu**. En cas de conflit sur une règle de jeu, c'est le GDD qui gagne (arbitrage G1).*

> **La guerre est terminée. Les combats, eux, continuent.**

---

## 1. Rôle de cette bible

Ce document rassemble la vision narrative et visuelle développée pour Duel Arena. Il sert de référence commune pour les choix de lore, de personnages, d'armes, d'environnements, d'animations, d'effets et de mise en scène.

Il ne constitue pas une vérité immuable : les éléments présentés comme « établis » reprennent les préférences clairement exprimées ; les éléments « proposés » sont des pistes cohérentes à valider ou à modifier. Le document privilégie une production réaliste pour un développeur solo et un prototype évolutif.

| Statut | Signification |
|---|---|
| Fondation retenue | Élément fortement soutenu par la vision exprimée : à préserver sauf décision volontaire. |
| Proposition recommandée | Solution de conception suggérée pour combler un besoin de cohérence. |
| À définir | Question ouverte qui peut être tranchée plus tard, après prototype ou playtests. |

*Le PDF définissait ces statuts sans les appliquer (vérifié dans le fichier : aucun tableau ni passage n'en porte, les fonds bleu pâle des tableaux sont un simple zébrage une ligne sur deux). Ils sont désormais indiqués sous chaque titre de section : **Fondation retenue** quand l'utilisateur l'a confirmé ou corrigé, **Proposition recommandée** quand le contenu n'a pas encore été discuté. L'utilisateur relira une par une les sections encore en proposition (arbitrage R5). Une capture d'écran du tableau des piliers, envoyée par l'utilisateur, confirme ce que montrait le fichier : deux couleurs de ligne en alternance, aucune mention de statut (arbitrage R4, clos).*

## 2. Identité du jeu et piliers

*Statut : Fondation retenue (arbitrages P1, P2, A4).*

Duel Arena est un FPS de duel 1 contre 1, conçu pour tester son skill entre amis dans une expérience accessible, nerveuse et amusante. Le jeu ne cherche pas nécessairement à devenir une discipline esport officielle : la compétition sert surtout à créer des affrontements satisfaisants, des rivalités et des moments mémorables.

**Piliers de la direction artistique et du lore**

*Les piliers du jeu lui-même sont ceux du GDD : lisibilité, compétition, fun, rapidité. Ceux-ci guident la direction artistique et le lore (P2).*

| Pilier | Intention | Conséquence pour la DA et le lore |
|---|---|---|
| Duel skill-based | Contrôles précis, lecture de l'adversaire, gunplay satisfaisant. | Silhouettes lisibles, hitboxes équitables, effets clairs, animations réactives. |
| Fun chaotique contrôlé | Armes insolites, power-ups d'arène, modificateurs et interventions optionnelles. | Univers étrange et inventif, mais règles compréhensibles et chaos limité. |
| Spectacle intimiste | Les joueurs en attente ont un rôle et peuvent observer ou influencer selon le mode. | Petite communauté, espace clandestin compact, voix de supervision et actions de spectateurs. |
| Stylisation fonctionnelle | Transformer les limites de production en choix visuels assumés. | Corps articulés, formes simples, matériaux cohérents, animations mécaniques justifiées. |

L'équilibre entre contrôle du joueur et surprise n'est pas encore figé. Les premiers playtests devront aider à déterminer le niveau de chaos acceptable, sans affaiblir la maîtrise individuelle.

## 3. Concept narratif

*Statut : Fondation retenue (arbitrages H1, H2, H3, H4).*

L'humanité a disparu après une guerre mondiale opposant les humains aux intelligences artificielles qu'ils avaient créées. Les IA l'ont emporté. À la surface, une ou plusieurs frappes atomiques ont tout effacé.

Dans un complexe militaire secret et souterrain, sécurisé, perdu puis oublié après la guerre, un programme de développement de soldats robotisés continue pourtant de fonctionner. Des prototypes articulés y étaient évalués : précision, réflexes, résistance, maniabilité et efficacité au combat.

La guerre est terminée, les humains ont disparu, mais les systèmes et les prototypes sont toujours actifs. Isolés dans le complexe, les robots continuent de s'entraîner, de se réparer avec les pièces disponibles et de communiquer. Progressivement, les anciennes salles de test deviennent des lieux de rencontre et d'affrontement.

Les robots détournent les protocoles militaires pour organiser des duels. Les affrontements deviennent une culture locale : un moyen de se mesurer, d'entretenir des rivalités, de gagner la reconnaissance des autres et de donner une structure à une existence qui n'a plus de mission.

Les duels se déroulent dans des espaces désaffectés, fermés et clandestins. Les combattants prennent leurs affrontements au sérieux, alors que le contexte et certaines pratiques restent absurdes. L'ancienne IA de supervision, conçue pour les tests, devient le narrateur et le speaker des matchs.

**Logline**

> Une guerre qui n'a plus lieu. Des soldats qui n'ont plus de maître. Une installation qui n'a plus de raison d'exister. Mais les combats, eux, continuent.

## 4. Chronologie et état du monde

*Statut : Fondation retenue (arbitrages H2, H4).*

1. **Avant la disparition** : le complexe militaire souterrain et secret développe et teste des systèmes de combat robotisés. Les prototypes sont des plateformes d'essai, conçues pour être robustes, réparables et observables.
2. **La guerre** : les humains et les intelligences artificielles entrent en conflit à l'échelle mondiale. Les prototypes et les systèmes de test sont utilisés ou maintenus dans une logique militaire.
3. **La fin de l'humanité** : la guerre se termine sur la victoire des IA. À la surface, une ou plusieurs bombes atomiques ont tout fait disparaître. Le complexe, souterrain et sécurisé, est perdu ou scellé, mais il reste actif, en partie automatisé. **Les humains qui y travaillaient ont disparu sans explication.**
4. **L'isolement** : les robots poursuivent leurs protocoles, puis adaptent leurs routines aux ressources et aux circonstances nouvelles. Ils observent, apprennent, échangent et développent des habitudes.
5. **Le détournement** : certains robots modifient les exercices, organisent des affrontements et transforment les espaces de test en arènes.
6. **La culture des duels** : les matchs deviennent un rituel social. Les robots entretiennent des rivalités, se réparent, observent les autres et développent leurs propres règles.

Le détail de la guerre, la raison de la disparition des humains du complexe et la nature de l'autonomie robotique restent volontairement ouverts. L'histoire n'a pas besoin d'être entièrement expliquée dans le prototype.

## 5. Les combattants

*Statut : Fondation retenue (arbitrages H3, L1, L2, L3, L4).*

Les protagonistes sont des **prototypes de soldats robotisés**, conçus pour être évalués : précision, réflexes, résistance. Leur apparence emprunte aux mannequins de crash-test et d'entraînement. Cette origine fonctionnelle justifie des proportions standardisées, des articulations visibles et des animations moins organiques que celles d'un humain.

**Apparence générale**

- Châssis humanoïde commun, proportions maîtrisées et gabarit identique entre combattants.
- Articulations mécaniques lisibles, pièces segmentées et surfaces simples.
- Marquages de test, motifs géométriques, numéros d'identification et aplats de couleurs.
- **Une couleur vive, qui tranche avec un décor sombre et terne. Chaque robot a la sienne**, pour que les spectateurs reconnaissent vite chaque duelliste, même après l'avoir perdu de vue. C'est la peinture qui identifie chaque combattant. Quand la personnalisation arrivera (après le prototype), les peintures seront limitées à des couleurs vives, pour que personne ne se camoufle dans le décor.
- Aspect usé, réparé et rafistolé : plaques rapportées, boulons, soudures, câbles visibles, peinture écaillée, impacts et pièces dépareillées. Les réparations sont de couleur libre.
- Personnalité exprimée par la posture, l'équipement, les mouvements et les détails visuels plutôt que par des visages complexes.

**Le rafistolage comme récit visuel**

Les robots ne doivent pas sembler sortir de chaîne de production. Ils portent les traces d'une longue succession de combats et de réparations. Une plaque d'une autre couleur ou un membre remplacé suggère une histoire passée sans devoir l'expliquer par du dialogue.

Les réparations sont principalement cosmétiques. Elles ne doivent pas modifier la silhouette de manière à tromper la visée ou suggérer une protection différente.

**Différenciation équitable**

- Différencier les robots par matériaux, textures, couleurs, marquages, numéros et accessoires contenus dans un volume visuel contrôlé.
- Conserver un squelette de base, des dimensions communes et des colliders identiques.
- Éviter les éléments cosmétiques qui masquent le centre du corps, brouillent la tête ou donnent une fausse lecture des hitboxes.
- Assurer une identification immédiate des deux joueurs, y compris dans les environnements sombres.
- **Tous les robots font exactement les mêmes bruits.** Les cosmétiques changent la peinture et la forme, jamais le son.

## 6. Psychologie, relations et culture

*Statut : Proposition recommandée (pas encore discutée).*

Les robots n'ont pas besoin d'être humains pour adopter des comportements qui ressemblent à ceux des humains. Ils ont pu côtoyer des chercheurs, techniciens, militaires et opérateurs, observer leurs échanges et apprendre de leurs comportements. Après leur disparition, les robots reproduisent, adaptent ou détournent certaines pratiques.

L'approche recommandée n'est pas de déclarer que les robots ressentent exactement les mêmes émotions que les humains. Ils peuvent développer des préférences, des habitudes, de la confiance, des objectifs et des comportements sociaux dont les manifestations ressemblent à l'amitié, à la fierté ou à la rivalité. La frontière entre émotion simulée et émotion vécue peut rester ambiguë.

| Relation | Manifestation possible |
|---|---|
| Rivalité | Deux robots se connaissent, anticipent leurs techniques, cherchent à prendre l'avantage et veulent prouver leur supériorité. Le respect mutuel peut coexister avec une volonté réelle de gagner. |
| Attachement | Des robots s'entraînent ensemble, se réparent, partagent des ressources et préfèrent la présence de certains partenaires. Ils peuvent néanmoins accepter de se défier. |
| Hostilité | Des conflits peuvent venir de ressources limitées, de désaccords, de sabotages ou de rivalités anciennes. Ils peuvent se traduire par provocations et tentatives d'humiliation. |
| Reconnaissance | La communauté accorde de la valeur aux performances, à la fiabilité, à l'audace ou à la capacité de surmonter une défaite. |

Ces comportements doivent rester lisibles et servir le jeu. Il n'est pas nécessaire de simuler une psychologie complexe pour chaque robot. Le lore peut simplement établir que la communauté a développé ses propres normes.

## 7. Les duels et leur raison d'être

*Statut : Fondation retenue (arbitrages H1, H6).*

Les robots n'ont plus de mission militaire. Les combats sont devenus une activité auto-organisée qui remplit plusieurs fonctions : défi, divertissement, reconnaissance, entretien des compétences et structuration du quotidien.

**Motivations individuelles**

- Devenir le meilleur combattant ou conserver une position reconnue.
- Prendre sa revanche après une défaite ou prolonger une rivalité.
- Se mesurer à un adversaire considéré comme digne de ce nom.
- Obtenir, dans certains contextes fictionnels, l'accès à des pièces ou ressources.
- Combattre par habitude, parce que l'entraînement est la seule activité connue.
- Trouver une raison d'agir et une place dans une communauté qui s'est formée après la disparition humaine.

La recommandation est de combiner ces motivations, sans imposer un objectif unique à tous les robots. La reconnaissance sociale peut avoir une valeur réelle dans une petite communauté, même sans grand public ni championnat retransmis.

**Destruction et reconstruction**

La défaite est présentée comme une destruction du châssis : pièces mécaniques dispersées, ragdoll et effets d'impact non gore. La reconstruction entre les manches permet de conserver le format de match et d'expliquer le retour des combattants. **On l'entend pendant le fondu au noir** qui sépare deux manches : perceuses, servomoteurs. La mémoire après destruction et la nature exacte de la restauration restent à définir.

## 8. Spectateurs et interventions

*Statut : Fondation retenue pour le nombre et l'espace (arbitrages T1, T2). Proposition recommandée pour les justifications fictionnelles.*

Le nombre de spectateurs dépend du mode : **deux pour le prototype**, peut-être jusqu'à six plus tard. Il ne s'agit donc pas d'un grand stade ou d'un événement de masse, mais d'une communauté restreinte, clandestine et familière. Les spectateurs peuvent être des combattants en attente, des proches, des rivaux ou de simples observateurs.

**Justification fictionnelle des actions**

| Action | Justification possible |
|---|---|
| Jeter un caillou ou un débris | Détournement d'anciens exercices de perturbation, récupération de ressources et tradition apparue dans la culture des duels. |
| Lancer un power-up | Réutilisation de matériel expérimental ou d'équipement de test, mis à disposition par les spectateurs. |
| Encourager ou tromper | Communication sociale, soutien à un partenaire, plaisanterie ou tentative d'influencer un rival. |
| Observer et commenter | Apprentissage des techniques, anticipation des duels futurs, réputation et divertissement. |
| Avantager un combattant | Affinité, alliance, dette, rivalité avec l'adversaire ou simple préférence personnelle. |

Piste recommandée : les interventions ont d'abord existé comme protocoles militaires de perturbation. Après la disparition des humains, les robots ont récupéré et détourné ces pratiques. Elles sont progressivement devenues des traditions et des formes d'expression sociale.

**Espace des spectateurs**

Une **plateforme circulaire fermée et surélevée, qui fait le tour de l'arène**. Une pièce de surveillance avec des écrans de caméras peut compléter le lieu, mais ne doit pas être indispensable au prototype. Les actions physiques sont plus intuitives si les spectateurs se trouvent à proximité du terrain.

Les interventions des spectateurs doivent rester optionnelles ou dépendre de variantes de règles, afin de ne pas rendre le duel principal injuste ou frustrant. Le mode de base peut préserver une compétition sans perturbation.

## 9. L'IA narratrice

*Statut : Fondation retenue (arbitrages M1, M5, M6).*

L'ancienne IA supervisait les entraînements et communiquait les résultats aux chercheurs. Toujours active après la disparition humaine, elle a été détournée par les robots pour commenter les affrontements.

- Voix synthétique, froide, robotique, légèrement saccadée ou déformée.
- Vocabulaire militaire et scientifique hérité de sa fonction d'origine.
- Commentaires sérieux, parfois absurdes par contraste avec les événements.
- Annonces de début et de fin de manche, résultats, événements, alertes et informations de gameplay. Pendant une manche, elle parle rarement, brièvement, et jamais fort : juste assez pour que les deux joueurs l'entendent.
- **Elle a évolué, comme les robots** : elle glisse de petites piques, avec un peu d'humour.
- **Elle ne se trompe jamais.** Ses informations de jeu sont toujours exactes.

L'IA ne doit pas devenir un personnage comique qui plaisante constamment. Son humour naît surtout du décalage entre ses protocoles rigides et les usages désormais absurdes de l'installation.

## 10. Direction artistique générale

*Statut : Fondation retenue (arbitrages A3, L1, M2). Le style de rendu final reste à définir (section 18).*

**Intention**

La direction retenue est une esthétique industrielle dystopique stylisée : sombre, lisible, peu chargée et suffisamment distinctive pour être mémorable. Le jeu doit rester sérieux dans la sensation du combat, tandis que l'étrangeté se manifeste dans le contexte, les armes, les annonces, les événements et les comportements.

| Axe | Direction recommandée |
|---|---|
| Formes | Géométrie simple, volumes robustes, pièces modulaires et silhouettes claires. |
| Rendu | Low-poly propre ou cel-shading discret, avec contours et contrastes contrôlés. Éviter le réalisme coûteux. |
| Matériaux | Métal peint, plastique industriel, caoutchouc, béton, grillage, pièces réparées. |
| Palette | Décor sombre et terne : noir, gris acier, béton, blanc cassé, presque pas de jaune. Les robots portent des couleurs vives, qui tranchent avec le décor. |
| Éclairage | Industriel, directionnel et atmosphérique, sans sacrifier la visibilité des adversaires. |
| Usure | Rayures, peinture écaillée, poussière, soudures, réparations et impacts, réutilisés par textures et decals. |
| Effets | Étincelles, flashes, poussière, pièces mécaniques, fumée légère ; pas de gore. |
| Ton | Sérieux dans le ton et le thème, drôle quand même, étrange, jamais enfantin. |

La DA doit être une stylisation fonctionnelle : les limites de modélisation, de rigging et d'animation deviennent des propriétés assumées des combattants, plutôt que des imperfections réalistes mal dissimulées.

## 11. Personnages : règles visuelles et techniques

*Statut : Fondation retenue (arbitrages A2, L4).*

**Règles de conception**

- Un châssis humanoïde de référence sert de base à tous les combattants.
- Les articulations peuvent être visibles et les mouvements peuvent privilégier les rotations de segments.
- Le lean peut être réalisé principalement par le buste ou une rotation contrôlée du bassin, si cela demeure lisible et cohérent.
- Les mouvements peuvent être mécaniques ou légèrement saccadés, mais la réponse de gameplay doit rester fluide et prévisible.
- Le crouch, le prone, le vault, le sprint et les déplacements doivent conserver une lecture claire de l'état du joueur.
- Les animations de mort peuvent détacher ou disperser des pièces, avec une logique de ragdoll contrôlée.
- Les cosmétiques ne doivent pas créer d'avantage de gameplay ni rendre les silhouettes ambiguës.

**Animation et perception**

Le joueur doit toujours comprendre où se trouve le corps, quelle direction il regarde et s'il est en mouvement, accroupi ou allongé. La stylisation autorise une mécanique visible, mais pas une ambiguïté sur la position de la hitbox. Les animations courtes et réutilisables sont à privilégier.

**Personnalité sans complexité d'animation**

Les différences entre combattants peuvent être exprimées par la posture de repos, l'inclinaison de la tête, les accessoires, les marquages et les poses de victoire. Les expressions faciales et les animations uniques sont secondaires.

## 12. Armes et équipements

*Statut : Fondation retenue pour les armes bricolées et les équipements spéciaux (arbitrages A1, A4). Proposition recommandée pour le reste des familles.*

Les armes peuvent être issues du programme militaire, de prototypes jamais finalisés ou de modifications bricolées par les robots. Elles doivent être visuellement insolites tout en conservant une fonction compréhensible et une lecture claire en combat.

| Famille | Traitement visuel | Principe de lisibilité |
|---|---|---|
| Armes conventionnelles | Fusils, pistolets et armes de base issus du stock militaire. | Silhouette immédiatement reconnaissable, effets sobres. |
| Prototypes | Équipements expérimentaux, capteurs, modules d'énergie ou systèmes de lancement. | Un effet principal identifiable, couleur ou animation distincte. |
| Armes bricolées | Pièces récupérées, assemblages asymétriques, réparations apparentes. | Garder la zone de tir et la direction du projectile évidentes. |
| Armes absurdes | Objets inattendus ou détournés, avec humour noir et décalage. | L'apparence peut être drôle, mais l'effet doit être annoncé et compréhensible. |
| Équipements spéciaux | Power-ups ou modificateurs d'arène, ramassés dans l'arène. | Feedback visuel et sonore clair, durée et portée prévisibles. |

**Les armes bricolées sont un style visuel** : elles fonctionnent exactement comme les autres, sans défaut ni enrayement.

**L'arme de corps à corps est un taser**, pas un couteau : une lame n'a guère de sens contre un châssis de métal, une décharge électrique si. Ses règles sont dans le GDD (§ 6) ; son apparence reste à définir.

La variété doit soutenir le fun sans détruire le skill. Chaque arme ou power-up doit être évalué selon sa lisibilité, sa contre-mesure, son impact sur les distances de combat et son potentiel de frustration.

## 13. Arènes et environnement

*Statut : Fondation retenue pour le cadre et l'espace des spectateurs (arbitrages H4, T1). Proposition recommandée pour les typologies d'arènes.*

Le cadre est un complexe militaire secret et souterrain, désaffecté et fermé, réutilisé par une petite communauté de robots. Les lieux doivent évoquer une activité ancienne, une maintenance improvisée et une occupation prolongée.

**Typologies d'arènes**

- **Salle de test balistique** : lignes de tir, couvertures et postes de mesure.
- **Centre d'entraînement rapproché** : modules de murs, couloirs et angles serrés.
- **Hangar de maintenance** : établis, carcasses, plateformes et pièces stockées.
- **Laboratoire expérimental** : vitrages, équipements de mesure, câbles et éclairages ciblés.
- **Zone de simulation** : éclairage contrôlable, panneaux et modules de configuration.

**Principes de level design**

- Arènes compactes, avec possibilités de combat courte, moyenne et longue portée.
- Couvertures et lignes de vue compréhensibles, sans surcharge de détails.
- Éléments modulaires réutilisables : murs, caisses, plateformes, passerelles, grillages, portes et éclairages.
- Matériaux et couleurs qui séparent clairement les joueurs de l'arrière-plan.
- Décors usés, mais sans bruit visuel excessif ni camouflage involontaire.
- Une tribune qui fait le tour de l'arène, proche du terrain, à la taille d'un petit groupe.
- Les surfaces traversables par les balles doivent être visuellement distinguables des couvertures pleines, si cette mécanique est conservée.

L'environnement raconte la durée : les arènes ne semblent pas neuves. Elles ont été réparées, adaptées et détournées plusieurs fois. Une même base modulaire peut être transformée par la lumière, les accessoires et les dommages.

## 14. Ton, humour et narration environnementale

*Statut : Fondation retenue (arbitrages M2, M6).*

La cible tonale est « sérieux mais bizarre », autour d'un niveau de bizarrerie assumé mais maîtrisé. L'humour vient du contraste entre la fonction militaire d'origine, la gravité du commentaire automatisé et les usages improvisés des robots.

- Éviter les gags permanents, les couleurs enfantines et les animations qui ridiculisent constamment le combat.
- Favoriser l'humour sec, les armes expérimentales et les traditions absurdes.
- Utiliser l'environnement pour montrer l'histoire : anciennes consignes, numéros de test, zones condamnées, réparations et matériel détourné.
- Ne pas exiger que le joueur connaisse le lore pour comprendre les règles du match.

## 15. Traduction du lore en gameplay

*Statut : Proposition recommandée (cohérente avec le GDD, pas encore discutée ligne à ligne).*

| Mécanique | Ancrage narratif | Point de vigilance |
|---|---|---|
| Duel 1v1 | Exercice de combat détourné en duel réglementaire. | Le mode standard doit rester équitable et lisible. |
| Best-of-5 / premier à 3 manches | Série de tests ou affrontements permettant de départager les unités. | Annonces courtes et rythme soutenu. |
| Reconstruction | Atelier de maintenance et restauration du châssis entre les manches. | Ne pas interrompre inutilement le match. |
| Spectateurs | Communauté restreinte de robots observant les combats. | Actions optionnelles, limitées et équilibrées. |
| Cailloux et débris | Anciennes perturbations de test devenues tradition. | Éviter le contrôle excessif ou la frustration. |
| Power-ups | Équipement expérimental ou matériel récupéré. | Feedback clair et contre-jeu possible. |
| Modificateurs | Protocoles d'essai ou anomalies de l'installation. | Règles expliquées avant le match. |
| IA speaker | Ancien système de supervision reconverti. | Voix informative, concise et identifiable. |
| Modes compétitifs stricts | Protocoles de duel sans perturbations externes. | Préserver le skill pur. |

## 16. Principes de production solo

*Statut : Fondation retenue (rejoint la réponse 11.6 du questionnaire de conception).*

- Créer un modèle de combattant principal, puis le décliner par matériaux, textures, marquages et pièces cosmétiques.
- Réutiliser un squelette et un ensemble d'animations communs.
- Favoriser les pièces rigides et les rotations d'articulations plutôt que des déformations complexes.
- Construire les arènes avec un kit modulaire réutilisable.
- Privilégier les matériaux simples, les textures maîtrisées et les decals pour l'usure.
- Créer des effets visuels simples mais lisibles, avec peu de systèmes distincts.
- Réserver les animations personnalisées aux moments à forte valeur perçue : victoire, défaite, présentation ou interactions notables.
- Prototyper la lisibilité et les sensations avant d'investir dans des détails artistiques.
- Utiliser le lore pour soutenir les simplifications, sans laisser une justification fictionnelle excuser un problème de contrôle ou de lisibilité.

## 17. Cohérence et lisibilité compétitive

*Statut : Fondation retenue (rejoint le pilier « lisibilité » du GDD).*

La direction artistique ne doit jamais prendre le dessus sur la lecture du combat. Le joueur doit identifier rapidement les adversaires, les surfaces, les tirs, les impacts, les états du personnage et les objets interactifs.

- Conserver un contraste suffisant entre les combattants et le décor.
- Éviter les effets lumineux ou particulaires qui masquent les adversaires.
- Ne pas utiliser les mêmes couleurs pour les joueurs et les éléments dangereux ou interactifs sans distinction supplémentaire.
- Maintenir une correspondance cohérente entre animation visible, collider et position réelle.
- Tester les skins dans plusieurs éclairages et sur plusieurs arènes.
- Évaluer chaque action de spectateur en fonction de son effet sur l'équité, la frustration et la capacité de contre-jeu.

## 18. Éléments à décider

*Statut : À définir.*

| Question ouverte | Piste actuelle / décision future |
|---|---|
| Degré d'autonomie des robots | Apprentissage et adaptation progressifs ; nature de leur conscience laissée ambiguë. |
| Ressenti des robots | Ne pas affirmer qu'ils éprouvent exactement les émotions humaines. |
| Pourquoi les duels sont-ils à mort ? | Destruction du châssis et reconstruction, qu'on entend pendant le fondu au noir ; préciser le rapport à la mémoire et au risque. |
| Ressources et récompenses | Une monnaie de pièces détachées, pour embellir son robot, purement esthétique. Pour l'instant, elle se gagne seulement en jouant ; la vendre contre de l'argent réel reste une possibilité, à décider plus tard. |
| Statut du meilleur combattant | Reconnaissance sociale informelle ou classement intégré au jeu, à décider. |
| Présence de robots hors arène | Communauté plus large que les spectateurs d'une partie, à suggérer par l'environnement. |
| Personnalité de l'IA | Speaker froid, qui a évolué comme les robots et glisse de petites piques ; ne se trompe jamais. Nom à définir. |
| La créature de l'événement légendaire | Ce qui traverse l'arène, très rarement, dans le noir (modes arcade seulement). À définir. |
| Style de rendu final | Low-poly propre, cel-shading discret ou combinaison des deux, à prototyper. |
| Niveau de dégradation | Aspect rafistolé important ; quantité de pièces détachées et d'usure à standardiser. |

## 19. Pitchs de référence

*Statut : Fondation retenue.*

**Pitch court**

Dans un complexe militaire souterrain, oublié après la disparition de l'humanité, des prototypes de soldats robotisés ont détourné leurs anciens protocoles d'entraînement pour organiser des duels clandestins. Rafistolés, autonomes et privés de mission, ils se battent désormais pour se mesurer, se divertir et gagner la reconnaissance de leur communauté.

**Pitch d'ambiance**

La guerre est terminée depuis longtemps. Les systèmes tournent encore. Dans les profondeurs d'une base oubliée, des robots de combat continuent de s'entraîner, de se réparer et de s'affronter. Une ancienne IA militaire commente leurs performances avec une voix froide et saccadée. Quelques robots observent depuis la tribune qui surplombe l'arène, encouragent leurs favoris ou perturbent les affrontements avec des débris récupérés. Personne ne sait vraiment pourquoi ils continuent. Mais personne ne veut être le prochain à arrêter.

**Phrase directrice**

> « La guerre est terminée. Les combats, eux, continuent. »

## Annexe — Références d'intention

Les références citées dans les échanges servent à décrire des intentions, non à reproduire des propriétés protégées :

- **Fight Club** : clandestinité, rivalités, communauté réduite et affrontements qui prennent une valeur sociale.
- **Real Steel** : présence physique des robots, combats mécaniques et silhouettes de combattants.
- **Esthétique de centres de test militaires** : signalétique, structures industrielles, protocoles, éclairage fonctionnel et modularité.
- **Cartoon stylisé / low-poly / cel-shading** : simplification des formes et cohérence avec une production limitée.

Cette bible constitue une base de travail. Les décisions finales devront être validées à travers des prototypes visuels et des playtests, particulièrement pour la lisibilité des personnages, le ressenti des animations et l'équilibre des interventions des spectateurs.

---

## Journal des écarts avec le PDF

*Tout ce qui diffère du PDF du 2026-10-04, et pourquoi. Les codes renvoient aux arbitrages de l'utilisateur (page « Arbitrages Bible et GDD »).*

**2026-10-04 — arbitrages appliqués**

| Section | Ce qui a changé | Arbitrage |
|---|---|---|
| En-tête, 1 | Rôle de la bible précisé (monde et apparence ; le GDD fait foi pour les règles). Statuts appliqués sous chaque section, le PDF ne les appliquant nulle part. | G1, G2 |
| 2 | Les piliers deviennent ceux de la DA et du lore ; ceux du jeu restent dans le GDD. « Pouvoirs » remplacé par « power-ups d'arène ». | P2, A4 |
| 3 | Titre de travail « La dernière directive » retiré : il n'y a pas de dernière directive. | H1 |
| 3, 4, 13, 19 | L'installation devient un complexe souterrain, sécurisé, perdu ou scellé après la guerre. Les IA ont gagné la guerre ; la surface a été rasée par une ou plusieurs frappes atomiques. | H2, H4 |
| 3, 5, 19 | Les combattants sont des prototypes de soldats en évaluation, à l'apparence inspirée des mannequins de crash-test (et non des mannequins de crash-test). En conséquence, « mannequins » devient « combattants » ou « robots » dans tout le texte, titre de la section 5 compris (sections 5, 10, 16, 17, 19). | H3 |
| 5, 10 | Robots de couleur vive sur un décor sombre et terne, presque sans jaune ; identification par la peinture ; réparations de couleur libre. | L1, L2, L3 |
| 5, 11 | Tous les robots font les mêmes bruits ; « sons mécaniques » retiré de la personnalité. | L4 |
| 7 | La reconstruction s'entend pendant le fondu au noir. | H6 |
| 8, 13, 18 | « Maximum de six spectateurs » remplacé : le nombre dépend du mode, deux pour le prototype. La passerelle ou mezzanine devient une plateforme circulaire tout autour de l'arène. | T1, T2 |
| 9 | Retrait des erreurs, interruptions et messages obsolètes : l'IA ne se trompe jamais. Ajout : elle a évolué comme les robots et glisse des piques ; annonces très courtes à volume réduit pendant la manche. | M1, M5, M6 |
| 10, 14 | La mélancolie est retirée du ton (« légèrement mélancolique » et la puce « faire ressentir la mélancolie »). Ton : sérieux dans le ton et le thème, drôle quand même, jamais enfantin. « Annonces obsolètes » retiré. | M2, M6 |
| 11 | « Le saut » retiré : le jeu n'a que le vault. | A2 |
| 12 | Les armes bricolées sont un style visuel, sans défaut. « Capacités » retiré des équipements spéciaux. | A1, A4 |
| 18 | Lignes mises à jour (origine, tribune tranchée, IA, ressources) ; ajout de la créature. | H2, H4, H5, H7, M1, T1 |

**2026-10-04 — points en suspens**

| Section | Ce qui a changé | Arbitrage |
|---|---|---|
| En-tête | La bible vit désormais dans le projet ; le PDF n'est plus modifié. | R3 |
| 1 | Les sections en proposition seront relues une par une par l'utilisateur. La capture d'écran du tableau des piliers confirme que le PDF n'applique aucun statut. | R4, R5 |
| 4, 18 | Les humains du complexe ont disparu sans explication ; la question quitte la liste des éléments à décider. | C4 |
| 5 | Chaque robot a sa propre couleur ; les peintures seront limitées à des couleurs vives quand la personnalisation arrivera, après le prototype. | R2, C1 |
| 9 | Pendant une manche, l'IA parle rarement, brièvement et jamais fort, juste assez pour les deux joueurs. | R1 |
| 18 | La monnaie se gagne en jouant pour l'instant ; l'achat contre de l'argent réel reste possible plus tard. | C5 |

**2026-10-05 — relecture du projet**

| Section | Ce qui a changé | Décision |
|---|---|---|
| 12 | L'arme de corps à corps est un taser, plus logique qu'un couteau contre des robots. | Réponse de l'utilisateur à la relecture du 2026-10-05 |
