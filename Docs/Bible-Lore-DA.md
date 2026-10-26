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

*Le PDF définissait ces statuts sans les appliquer (vérifié dans le fichier : aucun tableau ni passage n'en porte, les fonds bleu pâle des tableaux sont un simple zébrage une ligne sur deux). Ils sont désormais indiqués sous chaque titre de section : **Fondation retenue** quand l'utilisateur l'a confirmé ou corrigé, **Proposition recommandée** quand le contenu n'a pas encore été discuté. L'utilisateur relira une par une les sections encore en proposition (arbitrage R5). Une capture d'écran du tableau des piliers, envoyée par l'utilisateur, confirme ce que montrait le fichier : deux couleurs de ligne en alternance, aucune mention de statut (arbitrage R4, clos). Relecture faite le 2026-10-25 : § 6, § 8 et § 13 validés, § 12 à reprendre, § 15 retiré.*

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

*Statut : Fondation retenue (arbitrages H3, L1, L2, L3, L4 ; revue du 2026-10-25, C5 et C7).*

Les protagonistes sont des **prototypes de soldats robotisés**, conçus pour être évalués : précision, réflexes, résistance. Leur apparence emprunte aux mannequins de crash-test et d'entraînement. Cette origine fonctionnelle justifie des proportions standardisées, des articulations visibles et des animations moins organiques que celles d'un humain.

**Apparence générale**

- Châssis humanoïde, proportions maîtrisées et **même silhouette pour tous**. Plusieurs designs de robot sont possibles, à condition de tenir dans cette silhouette : les zones touchables, calculées à partir de la posture et du lean, restent les mêmes pour tous (souhaité par l'utilisateur le 2026-10-25). Le robot du jeu est le **CT-01** (2026-10-26), un mannequin d'essai blindé à visière (inspiré de l'archétype de KAY/O) ; le robot des premiers essais (le Y Bot) reste au choix du joueur, le châssis « prototype », comme l'**OS-02**, l'« Ossature », un endosquelette d'acier au crâne d'homme compact, sans peinture (inspiré de l'archétype du T-800), aux mêmes tailles, et épaissi pour montrer autant de sa silhouette que le CT-01 (68 % de sa surface touchable de face, 62 % de profil) : un robot plus fin serait plus dur à voir et à viser. Tous portent la couleur du joueur sur leurs plaques.
- **Hors service** : à la mort d'un robot à visière, l'écran de son visage grésille et affiche FATAL ERROR sous un triangle d'alerte rouge, et ses lueurs s'éteignent (demandé par l'utilisateur le 2026-10-26, d'après une image de référence).
- Articulations mécaniques lisibles, pièces segmentées et surfaces simples.
- Marquages de test, motifs géométriques, numéros d'identification et aplats de couleurs.
- **Une couleur vive, qui tranche avec un décor sombre et terne. Chaque robot a la sienne**, pour que les spectateurs reconnaissent vite chaque duelliste, même après l'avoir perdu de vue. C'est la peinture qui identifie chaque combattant. Chaque joueur choisit la sienne parmi les couleurs vives de la palette, pour que personne ne se camoufle dans le décor, et deux robots n'ont jamais la même (2026-10-16).
- Aspect usé, réparé et rafistolé : plaques rapportées, boulons, soudures, câbles visibles, peinture écaillée, impacts et pièces dépareillées. Les réparations peuvent être de couleur terne, **sur de petites pièces seulement** (2026-10-25).
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

*Statut : Fondation retenue (validée à la relecture du 2026-10-25).*

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

*Statut : Fondation retenue (arbitrages H1, H6 ; revue du 2026-10-25, L1).*

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

La défaite est présentée comme une destruction du châssis : pièces mécaniques dispersées, ragdoll et effets d'impact non gore. La reconstruction entre les manches permet de conserver le format de match et d'expliquer le retour des combattants. **On l'entend pendant le fondu au noir** qui sépare deux manches : perceuses, servomoteurs. **Un robot reconstruit se souvient de tout** : la défaite ne lui coûte que des pièces (2026-10-25). La nature exacte de la restauration reste ouverte.

## 8. Spectateurs et interventions

*Statut : Fondation retenue (arbitrages T1, T2 ; justifications fictionnelles validées à la relecture du 2026-10-25).*

Le nombre de spectateurs dépend du mode : **quatre au plus pour l'instant** (six places), peut-être davantage plus tard (2026-10-25). Il ne s'agit donc pas d'un grand stade ou d'un événement de masse, mais d'une communauté restreinte, clandestine et familière. Les spectateurs peuvent être des combattants en attente, des proches, des rivaux ou de simples observateurs.

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

*Dans le prototype (2026-10-13) : une passerelle à 4,5 m au-dessus de l'arène, qui en épouse le contour, avec un garde-corps d'un mètre côté arène et un mur côté extérieur. Les cailloux viennent de **blocs fissurés** posés le long de ce mur, qu'on casse à coups de poing ; le matériel que lancent les spectateurs, ce sont des armes (GDD § 9). Le tout est en gris, à habiller.*

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
- **Elle écrit toujours à l'écran ; sa voix ne sert qu'aux moments forts** (2026-10-25).
- Ses répliques s'écrivent à deux, l'utilisateur et Claude. Dans l'armurerie, elle reste factuelle : des chiffres, sans pique (2026-10-25).

L'IA ne doit pas devenir un personnage comique qui plaisante constamment. Son humour naît surtout du décalage entre ses protocoles rigides et les usages désormais absurdes de l'installation.

## 10. Direction artistique générale

*Statut : Fondation retenue (arbitrages A3, L1, M2). Le style de rendu final reste à définir (section 18) : l'utilisateur veut voir un essai des deux rendus, bien distincts, sur le robot (2026-10-25). L'essai est prêt depuis le 2026-10-26, sur le robot actuel (le Y Bot, lisse, à côté de ses versions low poly propre et cel-shading) et sur le CT-01, un robot inspiré de KAY/O de *Valorant* demandé par l'utilisateur, en trois styles (low poly propre, cel-shading, les deux mêlés). Le choix du style revient à l'utilisateur ; le robot est choisi : le CT-01, avec le prototype et l'OS-02 au choix du joueur (section 5).*

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

*Statut : **À reprendre** (relecture du 2026-10-25). L'utilisateur ne ressent pas encore cette vision dans le prototype, et veut l'accentuer. Elle sera revue avec la refonte des armes, où les modèles de chaque classe deviendront des armes distinctes (GDD § 6). Restent acquis : les armes bricolées sont un style visuel (A1), les équipements spéciaux se ramassent dans l'arène (A4), les armes absurdes ne s'obtiennent que des spectateurs ou en power-up (2026-10-25), et l'énergie est rouge.*

Les armes peuvent être issues du programme militaire, de prototypes jamais finalisés ou de modifications bricolées par les robots. Elles doivent être visuellement insolites tout en conservant une fonction compréhensible et une lecture claire en combat.

| Famille | Traitement visuel | Principe de lisibilité |
|---|---|---|
| Armes conventionnelles | Fusils, pistolets et armes de base issus du stock militaire. | Silhouette immédiatement reconnaissable, effets sobres. |
| Prototypes | Équipements expérimentaux, capteurs, modules d'énergie ou systèmes de lancement. | Un effet principal identifiable, couleur ou animation distincte. |
| Armes bricolées | Pièces récupérées, assemblages asymétriques, réparations apparentes. | Garder la zone de tir et la direction du projectile évidentes. |
| Armes absurdes | Objets inattendus ou détournés, avec humour noir et décalage. | L'apparence peut être drôle, mais l'effet doit être annoncé et compréhensible. |
| Équipements spéciaux | Power-ups ou modificateurs d'arène, ramassés dans l'arène. | Feedback visuel et sonore clair, durée et portée prévisibles. |

**Les armes bricolées sont un style visuel** : elles fonctionnent exactement comme les autres, sans défaut ni enrayement.

**Les armes absurdes** (le pistolet bionique géant au minuscule rayon paralysant, le petit pistolet qui tire un trou noir) **ne s'obtiennent que des spectateurs**, ou en power-up d'arène dans une partie sans spectateurs (2026-10-25). Elles restent hors du duel pur.

**L'énergie est rouge, partout** (2026-10-25) : la lueur des armes laser, les arcs du gant et le rayon du pistolet (appelé « raygun » jusque-là), qui était vert, une couleur de robot.

**Le programme d'essai a ses armes laser** (demandé par l'utilisateur, 2026-10-15) : quatre prototypes, un par classe d'arme (fusil FX-1, précision FX-2, dispersion FX-3, pistolet FX-4), qui sont des armes à part, aux formes franchement futuristes, dans l'esprit des armes à énergie d'*Apex Legends*. Les armes à balles du stock restent telles quelles. Une arme laser garde les points de prise de sa classe (la poignée, le chargeur devenu cellule d'énergie, le levier), donc ses gestes. *Propositions de Claude, appliquées aux modèles :*
- des volumes en coin, épais et anguleux, en deux tons : peinture anthracite et céramique blanc cassé ;
- l'énergie visible à travers l'arme (un cœur cerclé de bobines derrière une fenêtre, un accélérateur entre deux poutres, une charge engagée) et un émetteur sculpté qui fait la silhouette (tête carrée, fourche, coupe, anneau focaliseur) ; les huit lentilles du FX-3 dessinent la gerbe du jeu ;
- une lueur rouge (fenêtres, évents, lentilles, compteur allumé, à l'arrière de l'arme, face au tireur), puisque ni le rouge ni le blanc ne sont des couleurs de robot ; le marquage « PROTOTYPE » et un pictogramme laser en noir et blanc cassé.

**La série 30 : la dernière dotation du complexe** (demandée par l'utilisateur, 2026-10-15, sur des références d'*Apex Legends*) : quatre armes à balles, le fusil d'assaut FA-30, le fusil de précision à levier FP-30 (« un sniper type Winchester », refait le même jour d'après les images d'une carabine à levier tactique : sans chargeur, à crosse de chasse), le fusil à pompe FAP-30 et le revolver RV-30. *Propositions de Claude, appliquées aux modèles :*
- livrées peu avant la disparition des humains et retrouvées dans leurs caisses, ce sont les armes les plus modernes du stock ;
- des formes anguleuses, une peinture blanc cassé avec des aplats anthracite et noirs, un accent de laiton terni (ailettes, chien, détente) ; aucune lueur, ce sont des armes à balles. Le bleu des références est une couleur de robot : il devient anthracite ;
- elles ont servi depuis : crosse bloquée par un collier, boucle de levier gainée de ruban, plaque de couche taillée dans un pneu, poignet fêlé serré de ruban, plaquettes refaites, ailette perdue, bâtons de victoires au marqueur.

Chaque arme, du stock, de la série 30 ou laser, a sa propre histoire de rafistolage.

**Au choix du joueur** (2026-10-15, GDD § 6) : en partie, chaque arme se montre sous le modèle du stock, celui de la série 30 ou le prototype laser de sa classe ; seule l'apparence change, ni la façon dont elle tire, ni le son qu'elle fait. Le raygun, qui tire déjà un rayon, se montre d'abord en pistolet laser (FX-4).

**Les finitions** (2026-10-15, au choix de l'armurerie : GDD § 6) : la même arme peut se montrer dans six états, selon son histoire. *Propositions de Claude, appliquées aux modèles :*
- d'origine : l'usure de tous les jours ;
- repeinte au pinceau, couche après couche, sans décaper : l'apprêt rouge affleure au bord des éclats, des retouches d'une autre teinte les recouvrent (de l'anthracite sur le blanc, faute de blanc) ;
- rouillée, sortie de ses caisses sans être décapée : des cloques, des creux et des coulures ;
- dépareillée : un garde-main, une crosse, un chargeur, une carcasse pris sur d'autres armes ;
- polie par les mains : la peinture partie là où on la tient, le métal poli ;
- encrassée : jamais nettoyée, huile, poussière et suie.

Toujours dans la palette du décor : la rouille est brune et sombre, puisque l'orange est une couleur de robot.

**Les accessoires** (liste de l'utilisateur, 2026-10-16 ; leurs règles : GDD § 6) : des pièces du même stock que les armes, montées sur leurs rails, usées et rafistolées comme elles. Les viseurs reprennent les dix viseurs sans grossissement de *Rainbow Six Siege* (référence de l'utilisateur) : trois points rouges, quatre holographiques, trois réflex. *Propositions de Claude, appliquées aux modèles :*
- des boîtiers anthracite et noirs, à l'usure du stock (arêtes polies, éclats), sur des embases de rail à vis ou à levier ; des vitres teintées comme de vraies optiques, ambrées ou bleutées ;
- les réticules de *Rainbow Six* : rouges aux points rouges et aux holographiques, verts aux réflex ; on ne les voit qu'en visant, ils ne se confondent avec aucun robot ;
- le laser tactique, un boîtier carré sur le flanc droit (sous le canon au pistolet), au faisceau rouge, la couleur qu'aucun robot ne porte ; les deux poignées, verticale et inclinée ; les chargeurs grande capacité, allongés dans la forme du chargeur de chaque arme ;
- des réparations à leur échelle : un pare-soleil scotché, une vis remplacée par un écrou, un capot de pile tenu par un collier, un repère de réglage au marqueur ;
- montés, ils prennent la finition de leur arme (une arme rouillée porte un viseur rouillé), et un viseur efface la hausse et le guidon de l'arme, comme dans *Rainbow Six* : rien ne reste dans sa fenêtre (2026-10-16).

**L'arme de corps à corps est un taser**, pas un couteau : une lame n'a guère de sens contre un châssis de métal, une décharge électrique si. Ses règles sont dans le GDD (§ 6). **C'est un gant à décharge électrique**, porté sur l'avant-bras gauche, dans l'esprit des gantelets de *Fallout* : le coup est un direct du poing, qui lâche des arcs électriques au contact (modèle fourni par l'utilisateur, 2026-10-11 ; refait le 2026-10-16 dans la DA du projet, ses étincelles remplacées le même jour par de vrais arcs, à la demande de l'utilisateur). *Proposition de Claude pour les arcs :* à l'appui, le gant se charge, de petits arcs sautent d'une électrode à l'autre et courent des condensateurs au poing ; au contact, des éclairs zigzaguent des électrodes jusqu'à la cible, au cœur blanc et au halo rouge, l'énergie des armes laser et de la jauge du gant, et la lumière du poing rougit ce qu'elle touche. Un spectateur étourdi grésille des mêmes arcs, autour de la tête. *Propositions de Claude, appliquées au GD-1 :* un gantelet d'atelier détourné ; une manchette d'acier peinte anthracite en deux coques, fermée de sangles ; sur le dessus, deux condensateurs à ailettes de céramique blanc cassé, des isolateurs de ligne à haute tension, et une jauge de charge qui luit rouge, l'énergie des armes laser, face au coude, que voit le joueur quand il frappe ; deux câbles gainés jusqu'au poing ; quatre électrodes noircies par les arcs sur une barre de jointures d'acier. Il a servi : une coque fêlée sous une tôle rivetée, un câble regainé de ruban, un collier de serrage, et sur le flanc du pouce « HAUTE TENSION » et l'éclair dans son triangle ; sur l'autre, sa désignation et des bâtons de victoires.

La variété doit soutenir le fun sans détruire le skill. Chaque arme ou power-up doit être évalué selon sa lisibilité, sa contre-mesure, son impact sur les distances de combat et son potentiel de frustration.

## 13. Arènes et environnement

*Statut : Fondation retenue (arbitrages H4, T1 ; typologies validées à la relecture du 2026-10-25).*

Le cadre est un complexe militaire secret et souterrain, désaffecté et fermé, réutilisé par une petite communauté de robots. Les lieux doivent évoquer une activité ancienne, une maintenance improvisée et une occupation prolongée.

**Typologies d'arènes**

- **Salle de test balistique** : lignes de tir, couvertures et postes de mesure.
- **Centre d'entraînement rapproché** : modules de murs, couloirs et angles serrés.
- **Hangar de maintenance** : établis, carcasses, plateformes et pièces stockées.
- **Laboratoire expérimental** : vitrages, équipements de mesure, câbles et éclairages ciblés.
- **Zone de simulation** : éclairage contrôlable, panneaux et modules de configuration.

**La première vraie arène sera un centre d'entraînement rapproché** (choisi le 2026-10-25). Le vrai playtest se fera sur une arène réussie ; les cinq types serviront au jeu fini.

**La créature de l'événement légendaire** (GDD § 10) est **un prototype abandonné ou défectueux** du complexe (2026-10-25).

**Principes de level design**

- Arènes compactes, avec possibilités de combat courte, moyenne et longue portée.
- Couvertures et lignes de vue compréhensibles, sans surcharge de détails.
- Éléments modulaires réutilisables : murs, caisses, plateformes, passerelles, grillages, portes et éclairages.
- Matériaux et couleurs qui séparent clairement les joueurs de l'arrière-plan.
- Décors usés, mais sans bruit visuel excessif ni camouflage involontaire.
- Une tribune qui fait le tour de l'arène, proche du terrain, à la taille d'un petit groupe.
- Aucune surface ne laisse passer les balles (2026-10-25) : une vitre ou un grillage arrêtent un tir comme un mur.

L'environnement raconte la durée : les arènes ne semblent pas neuves. Elles ont été réparées, adaptées et détournées plusieurs fois. Une même base modulaire peut être transformée par la lumière, les accessoires et les dommages.

## 14. Ton, humour et narration environnementale

*Statut : Fondation retenue (arbitrages M2, M6).*

La cible tonale est « sérieux mais bizarre », autour d'un niveau de bizarrerie assumé mais maîtrisé. L'humour vient du contraste entre la fonction militaire d'origine, la gravité du commentaire automatisé et les usages improvisés des robots.

- Éviter les gags permanents, les couleurs enfantines et les animations qui ridiculisent constamment le combat.
- Favoriser l'humour sec, les armes expérimentales et les traditions absurdes.
- Utiliser l'environnement pour montrer l'histoire : anciennes consignes, numéros de test, zones condamnées, réparations et matériel détourné.
- Ne pas exiger que le joueur connaisse le lore pour comprendre les règles du match.

## 15. Section retirée

*La traduction du lore en gameplay a été retirée à la relecture du 2026-10-25, à la demande de l'utilisateur. Le numéro reste vide pour que les renvois vers les sections suivantes restent justes.*

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
| Ressources et récompenses | Une monnaie de pièces détachées, pour embellir son robot, purement esthétique, gagnée en jouant et à chaque niveau (2026-10-25). La vendre contre de l'argent réel reste une possibilité, à décider plus tard. |
| Présence de robots hors arène | Communauté plus large que les spectateurs d'une partie, à suggérer par l'environnement. |
| Nom de l'IA | Huit propositions de Claude le 2026-10-25 (VERDICT, SUPERVISEUR S-07, ÉTALON, ARBITRE, SENTINELLE, CONTRÔLE, PROTOCOLE, MIRE) : aucune n'a convaincu l'utilisateur, qui a coché SENTINELLE sans s'y tenir. La recherche continue. |
| Style de rendu final | Low-poly propre, cel-shading discret ou combinaison des deux : les essais sont prêts (2026-10-26), sur le Y Bot et sur le CT-01, robot inspiré de KAY/O ; l'utilisateur choisit le style (le robot est le CT-01, section 5). |
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

**2026-10-15 — modèles des armes**

| Section | Ce qui a changé | Décision |
|---|---|---|
| 12 | Chaque arme du stock a son prototype laser, au même mécanisme de rechargement ; la lueur rouge qui les distingue est une proposition de Claude. | Demande de l'utilisateur, 2026-10-15 |
| 12 | Les armes laser deviennent quatre armes à part, aux formes franchement futuristes (référence *Apex Legends*), une par classe, aux mêmes points de prise ; les armes à balles restent telles quelles. Les deux tons, la céramique et l'émetteur sculpté sont des propositions de Claude. | Précision de l'utilisateur, 2026-10-15 |
| 12 | La série 30 : quatre armes à balles modernes (fusil d'assaut, précision à levier façon Winchester, pompe, revolver), d'après des références d'*Apex Legends* ; leur place dans le lore (la dernière dotation, retrouvée en caisse) et leurs couleurs sont des propositions de Claude. | Demande de l'utilisateur, 2026-10-15 |
| 12 | Le FA-07 refait façon M4 d'après une photo (garde-main fin à quatre rails, guidon en « A », chargeur courbe) ; le FP-30 refait d'après les images d'une carabine à levier tactique, sans chargeur (ses cartouches entrent une à une par une porte sous la boîte de culasse, là où la main gauche du clip de rechargement va et vient) et à crosse de chasse, longue et fine, à poignet droit, reprise ensuite sur le FAP-30. La porte, les couleurs (boîte de culasse blanc cassé, levier d'acier brut) et les rafistolages sont des propositions de Claude. | Demande de l'utilisateur, 2026-10-15 |
| 12 | Le compteur de charge des armes laser passe du flanc à l'arrière de l'arme, face au tireur, pour se lire en vue subjective. | Demande de l'utilisateur, 2026-10-15 |
| 12 | Six finitions d'usure pour chaque arme (d'origine, repeinte, rouillée, dépareillée, polie par les mains, encrassée), au choix de l'armurerie et visibles en partie ; leur rendu est une proposition de Claude. | Demande de l'utilisateur, 2026-10-15 |
| 12 | En partie, chaque arme se montre au choix sous trois modèles : le stock, la série 30 ou le prototype laser de sa classe ; seule l'apparence change. | Choix de l'utilisateur, 2026-10-15 (« 4 classes, 3 modèles ») |
| 12 | Le raygun se montre par défaut en pistolet laser (FX-4), avec son rayon vert et ses sons de plasma ; le pistolet à balles du stock et le revolver restent au choix. | Choix de l'utilisateur, 2026-10-15 |

**2026-10-16 — accessoires**

| Section | Ce qui a changé | Décision |
|---|---|---|
| 12 | Les accessoires : les dix viseurs sans grossissement de *Rainbow Six Siege*, un laser tactique, deux poignées et des chargeurs grande capacité, du même stock que les armes. Leurs couleurs, leurs vitres, leurs réparations et le faisceau rouge sont des propositions de Claude. | Demande de l'utilisateur, 2026-10-16 |
| 12 | Le gant à décharge refait dans la DA (le GD-1), avec moins de triangles que le modèle fourni (3 729 contre 28 404) : la forme, les couleurs et les réparations sont des propositions de Claude, dans l'esprit des gantelets de *Fallout*. | Demande de l'utilisateur, 2026-10-16 |
| 12 | De vrais arcs électriques à la place des étincelles du gant : leur forme (charge, puis éclairs vers la cible) et leurs couleurs (cœur blanc, halo rouge) sont des propositions de Claude. | Demande de l'utilisateur, 2026-10-16 |

**2026-10-25 — revue du GDD et de la bible**

*Les codes renvoient à la page « Revue GDD et bible ».*

| Section | Ce qui a changé | Décision |
|---|---|---|
| 1 | La relecture des sections en proposition est faite. | B1 à B5 |
| 5 | Même silhouette pour tous, mais plusieurs designs de robot possibles dans cette silhouette ; réparations ternes sur de petites pièces seulement ; la couleur, choisie par le joueur depuis le 2026-10-16, n'attend plus la personnalisation. | C5, C7 |
| 6 | Validée. | B1 |
| 7, 18 | Un robot reconstruit se souvient de tout : la défaite ne coûte que des pièces. La question quitte la liste des éléments à décider. | L1 |
| 8 | Justifications validées ; quatre spectateurs au plus pour l'instant. | B2, S1 |
| 9 | L'IA écrit toujours et ne parle que pour les moments forts ; répliques écrites à deux ; armurerie factuelle. | I2, I3, I4 |
| 10, 18 | Essai des deux rendus sur le robot. | V2 |
| 12 | À reprendre avec la refonte des armes ; armes absurdes réservées aux spectateurs et aux power-ups ; l'énergie est rouge, rayon du pistolet compris. | B3, W1, W2, W4 |
| 13, 18 | Typologies validées ; première vraie arène : un centre d'entraînement rapproché ; aucune surface ne laisse passer les balles ; la créature est un prototype abandonné ou défectueux. | B4, V3, V4, L2 |
| 15 | Section retirée. | B5 |
| 18 | Le statut du meilleur combattant est le tableau de la soirée (GDD § 14) ; ligne retirée, comme celles de la mémoire et de la créature. La monnaie se gagne aussi à chaque niveau. | C1, C3 |

**2026-10-25 — suite de la revue**

*Les codes renvoient à la page « Suite de la revue ».*

| Section | Ce qui a changé | Décision |
|---|---|---|
| 9, 18 | Aucun des huit noms proposés pour l'IA n'a convaincu ; la recherche continue. | N1 |
| 12 | Sept accessoires de plus, choisis parmi les propositions de Claude (crosses légère et lourde, compensateur, frein de bouche, canons long et court, chargeur rapide) ; leur apparence reste à dessiner. | X1 |
| 13 | Quatre power-ups d'arène de plus (plaque de blindage, réparation, balise, servomoteurs) ; leur apparence reste à dessiner, et la plaque se voit sur le robot. | U1 |

**2026-10-26 — essais du robot**

| Section | Ce qui a changé | Décision |
|---|---|---|
| 5 | Le CT-01 devient le robot du jeu ; le Y Bot (le châssis « prototype ») et l'OS-02, l'« Ossature » (l'archétype du T-800, épaissi pour montrer autant de sa silhouette que le CT-01), restent au choix du joueur, dans Personnalisation. | Demandes de l'utilisateur, 2026-10-26 |
| 10, 18 | L'essai des rendus est prêt, sur le Y Bot et sur le CT-01, un robot inspiré de KAY/O (*Valorant*) : visage d'écran à deux yeux en barres, plaques peintes de la couleur du joueur sur une ossature sombre, articulations en tambours, unité d'énergie dans le dos, mires de crash-test aux tempes et aux épaulières, numéro 07, une soudure et une petite plaque d'apprêt en réparations. Pièces rigides dans la silhouette commune. Le style et le robot restent à choisir. | Demande de l'utilisateur, 2026-10-26 |
