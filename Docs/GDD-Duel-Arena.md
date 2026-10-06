# Duel Arena — Game Design Document

*L'âme du jeu. Ce document dit ce que Duel Arena **est** et ce qu'il refuse d'être — pas où en est le code. L'état technique, les priorités et les dettes vivent dans `CLAUDE.md`, à la racine du projet.*

*Refondu le 2026-10-02 à partir du questionnaire de conception. Les réponses brutes, avec leurs hésitations, sont archivées dans `Docs/Questionnaire-GDD.md`.*

*Le monde et l'apparence du jeu (lore, personnages, armes, arènes, ton, direction artistique) vivent dans la bible, `Docs/Bible-Lore-DA.md`. **Ce document-ci fait foi pour les règles du jeu, la bible pour le monde et l'apparence.** En cas de conflit sur une règle de jeu, c'est le GDD qui gagne (tranché le 2026-10-04).*

*« Duel Arena » est un nom **provisoire**.*

---

## 1. Vision

**Un jeu de duel 1v1 nerveux, lisible, compétitif et fun.** Deux joueurs s'affrontent dans une arène fermée, sous les yeux d'un public qui attend son tour, depuis une tribune qui surplombe le combat.

Le modèle est un **combat clandestin**, façon boxe ou gladiateurs : le duel est le spectacle, et ceux qui regardent font partie de la soirée. Ils peuvent même, selon les réglages, s'en mêler. La référence de format est le goulag de Call of Duty (mode de jeu et arène fermée) ; la référence de contrôles est Rainbow Six Siege.

**Le monde** : des prototypes de soldats robotisés, rafistolés, qui s'affrontent en duels clandestins dans un complexe militaire souterrain, oublié après la disparition de l'humanité. L'ancienne IA de supervision commente les matchs. Le détail est dans la bible.

**Ton** : sérieux dans le gameplay, le ton et le thème, avec une bizarrerie assumée (7/10). Drôle quand même, jamais enfantin.

**Le jeu qu'on relance.** Une session dure une heure ou plus. L'objectif est qu'à la fin d'un match, on ait envie d'en relancer un autre.

## 2. Les quatre piliers

1. **Lisibilité** — aucun chaos visuel. On doit toujours comprendre ce qui vient de se passer.
2. **Compétition** — skill pur. Hit registration fiable, aucun avantage structurel.
3. **Fun** — spectateurs, touches visuelles, rythme, célébrations.
4. **Rapidité** — boucle courte, addictive.

Ce sont les piliers du jeu. La bible a ses propres piliers, qui guident la direction artistique et le lore (tranché le 2026-10-04).

## 3. Règles non négociables

Ce sont les contraintes qui arbitrent tous les arbitrages. Si une décision les contredit, c'est la décision qui change.

- **Le skill prime sur tout.**
- **Personne ne commence avec un avantage.** Ni en début de partie, ni en début de manche. Seule exception : un mode ou un réglage de partie qui l'annonce explicitement.
- **Pas de pay-to-win.** Ce qui s'achète ou se débloque est cosmétique.
- **Pas de RNG dans la précision.** Deux joueurs qui tirent la même rafale dans les mêmes conditions subissent exactement le même recul. Le recul est une courbe apprenable, jamais une dispersion aléatoire. Les armes à gerbe (fusil à pompe) tirent selon un **motif fixe**, identique à chaque tir : large et lisible au jugé, mais jamais aléatoire (tranché le 2026-10-02).
- **En classé, aucun avantage de latence structurel.** Le classé arrivera **après la sortie**, sur serveur dédié. D'ici là, toutes les parties tournent en mode hôte, qui donne un léger avantage à celui qui héberge : accepté entre amis.
- **Le son est une information de gameplay**, au même titre que le visuel. Jamais un habillage.
- **En classé, le duel est pur** : pas de spectateurs, pas d'interventions. Ailleurs, les spectateurs peuvent peser sur le duel (voir § 9), mais toujours par un **geste physique imparfait** : courir, ramasser, viser, lancer. Jamais par un simple bouton qui donnerait un avantage à coup sûr.
- **Tout ce qui sort du duel pur se règle par partie** : spectateurs, power-ups, chat vocal, événements. Le classé les désactive.

## 4. Structure d'une session

**Un lobby de plusieurs joueurs, deux duellistes à la fois.** Les autres sont spectateurs, dans la tribune, et attendent leur tour. Le nombre de spectateurs dépend du mode : **deux pour le prototype** (4 joueurs au total), peut-être jusqu'à six plus tard.

**L'organisation des duels se règle par partie** :
- **Roi de la colline** — le format par défaut. Le gagnant reste, le perdant retourne dans la file, le suivant prend sa place.
- **Tournoi** — arbre à élimination dans le lobby.
- **Hasard** — l'adversaire suivant est tiré au sort.

**Un match** se joue en BO5 ou en BO3, selon le mode. Une manche dure rarement plus d'une minute, donc un BO5 reste court même pour ceux qui attendent.

**Tout le monde repart à 100 PV** à chaque manche, gagnant compris. Exception : le mode *Until Death* (§ 11), où les blessures restent.

**Abandon en plein match** : forfait automatique.

**Matchmaking** : à la sortie, **uniquement des lobbys privés avec un code**, sans file publique : sans serveur dédié, une file publique mettrait des inconnus face à un hôte qui a l'avantage de la latence (tranché le 2026-10-04). **Après la sortie** : la file classée (sans spectateurs, sur serveur dédié), avec un matchmaking par région pour limiter le ping.

**Le réglage des parties doit être complet sans être pénible.** Il y aura beaucoup d'options, et le risque est un écran de configuration indigeste. Piste : des **préréglages** nommés (classique, arcade…) qui couvrent 90 % des cas, avec un onglet avancé pour le reste.

## 5. La manche

**Pas de limite de temps.** Une manche se termine par une mort. Le chrono actuel du code était un outil de test, il disparaît.

*Le risque connu : deux joueurs passifs peuvent faire durer une manche indéfiniment. Ce n'est pas un problème tant que les playtests ne le montrent pas. Si ça arrive, la piste privilégiée est la **révélation** : au bout d'un moment, chaque joueur émet un son de sa position. La règle devient une mécanique sonore plutôt qu'une sanction.*

**Spawns** : plusieurs points différents dans l'arène, avec échange de côté d'une manche à l'autre. Leur disposition dépendra surtout des modes de jeu, donc elle sera fixée plus tard ; d'ici là, le prototype garde ses deux points opposés (2026-10-05).

**Décompte** : 3 à 5 secondes. Le regard et le changement de posture sont libres, le déplacement non.

**À 2-2**, la manche décisive est une manche normale.

**Le kill** déclenche une **mini-célébration** : une phrase écrite à l'écran pour le gagnant, et une autre pour le perdant.

**Entre deux manches** : fondu au noir. Toutes les lumières de l'arène s'éteignent, les deux joueurs sont replacés dans l'obscurité, puis la lumière revient et la manche suivante démarre. Le replacement n'est jamais vu, pas même par les spectateurs. Dans le noir, **on entend la reconstruction des châssis** (perceuses, servomoteurs). Une killcam pour les deux joueurs est envisagée, à condition de ne pas casser le rythme.

**Fin de match** : un écran de victoire mis en scène pour le gagnant (mouvement de caméra, musique, animation, dans l'esprit d'un montage « aura farming »), puis un tableau récapitulatif pour les deux. Drôle, jamais enfantin (confirmé le 2026-10-04).

## 6. Combat et armes

**TTK** — la cible de départ est ≈ **0,7 seconde** de tir soutenu, mais elle est **à retravailler**, notamment avec l'arrivée des headshots. Elle vit dans l'inspecteur, pas en dur dans le code.

**Le MP5 actuel est une arme de test.** L'apparence des armes est décrite dans la bible (§ 12 de la bible) : conventionnelles, prototypes, bricolées, absurdes.

**L'arsenal du prototype** (tranché le 2026-10-05) : une arme automatique à chargeur, le MP5 ou une arme d'apparence plus robotique ou futuriste, et le **taser** de corps à corps (voir *Mêlée*). Le choix de l'arme (pile ou face, classes) attend la suite.

**L'identité « insolite »** : des armes qui jouent sur le décalage entre leur apparence et leur effet. Exemples cités : un énorme pistolet bionique démesuré qui tire un minuscule rayon paralysant (ralentit beaucoup, blesse peu), ou à l'inverse un petit pistolet qui tire un énorme trou noir.

**Les armes bricolées sont un style visuel**, pas une mécanique : elles fonctionnent comme les autres, sans défaut ni enrayement (tranché le 2026-10-04).

**Pas de pouvoirs propres aux personnages.** Seulement des power-ups, ramassés dans l'arène (tranché le 2026-10-04).

**Obtenir son arme — pas encore tranché.** La piste la plus avancée : avant le match, un tirage à pile ou face désigne un joueur, qui choisit une **classe d'arme** (sniper, fusil d'assaut, pompe, pistolet…) et l'**impose aussi à l'adversaire**. À la manche suivante, c'est le perdant qui choisit. Le risque à étudier : la frustration de jouer une arme qu'on n'a pas choisie. Les spectateurs peuvent aussi, selon les réglages, voter l'arme ou en lancer une dans l'arène.

**Headshots** : nécessaires pour certaines armes. Punitifs sur le papier, mais très satisfaisants en jeu. Multiplicateur à trancher par playtest.

**Munitions** : chargeur limité, rechargement.

**Visée (ADS)** : ralentit le déplacement. Viser en courant **coupe le sprint** et lance la visée. On ne tire pas en sprintant.

**Tir à la hanche** : possible, et c'est là que les armes à gerbe brillent. Leur gerbe suit un motif fixe (§ 3) : un tir au jugé au fusil à pompe couvre un grand angle, de façon lisible, et deux tirs identiques donnent toujours le même résultat.

**Dégâts selon la distance** : constants par défaut, à définir arme par arme.

**Matériaux traversables** : aucun pour l'instant, à voir selon les arènes.

**Utilitaires** : des grenades, soit cachées dans l'arène, soit données par les spectateurs.

**Mêlée** : un **taser** plutôt qu'un couteau, plus logique contre des robots (tranché le 2026-10-05). **Mort instantanée**, pour l'instant. Il sort **automatiquement** quand l'arme est vide.

**Recul** — référence de sensation : la R-301 d'Apex. **Les rafales longues doivent être viables et agréables** : pas de jeu qui force les rafales courtes, sauf pour une DMR au coup par coup. Le recul est stylisé et lisible : montée rapide sur les premiers tirs puis plafonnement, avec un pattern horizontal en « S » quand la rafale s'allonge. Il est modulé par la posture, le déplacement et la visée.

**Retour de touche** : hitmarker, un son de touche, et un son différent pour la tête.

**Recevoir une balle** : des **parasites et des interférences** à l'écran (un robot ne saigne pas), un son d'impact, et un **très léger** sursaut de caméra. Rien d'exagéré : le sursaut punit déjà le joueur touché en premier.

**La mort** : le châssis vole en pièces (ragdoll, pas de gore), un son signature, et un **replay instantané exagéré au ralenti**, dans l'esprit de la killcam de *Sniper Elite* ou des finish de *Mortal Kombat*.

## 7. Mouvement

**Trois paliers de vitesse** : **sneak** (lent, très discret) < **marche** < **course** (rapide, bruyante). Le compromis vitesse/discrétion est un vrai choix tactique. La sensation visée : nerveux et rapide, sans excès, à régler en jouant.

**Sprint illimité**, pas d'endurance.

**Postures** Debout / Accroupi / Prone, chacune avec sa vitesse, sa hauteur de vue et son empreinte. Se relever est bloqué s'il n'y a pas la place. **On peut tirer en rampant.**

**Changer de posture prend du temps**, même pour un robot : environ 0,3 s entre debout et accroupi, 0,65 s entre accroupi et allongé, 0,9 s entre debout et allongé. Pendant ce temps, le corps, la vue, la vitesse et l'endroit où l'on peut être touché passent progressivement d'une posture à l'autre ; on ne franchit pas d'obstacle à moitié relevé. Un plongeon au sol n'est donc pas une esquive instantanée. Valeurs de départ, à régler en jouant (2026-10-06).

**Le lean façon Rainbow Six** : seul le **buste** se penche. Derrière un mur, en visant et en se penchant, on n'expose que le haut du corps ; les jambes restent à couvert. Son amplitude se rapproche de celle de R6 : la tête sort d'environ 35 cm, valeur de départ à régler en jouant (tranché le 2026-10-05 ; amplitude et vue accroupie jugées bonnes en jouant le 2026-10-06).

Contrairement à Rainbow Six, **le lean reste possible en position allongée**, mais il y prend une autre forme : le buste **roule sur lui-même** et la vue bascule, la tête ne sortant que d'une dizaine de centimètres (demandé le 2026-10-06 ; un premier essai faisait glisser le haut du corps sur le côté).

**Pas de saut libre.** La touche de saut sert au **vault** : un obstacle assez bas, du sol derrière, et on passe par-dessus. C'est un choix assumé.

**Pas de dégâts de chute**, pas de mort hors-arène.

**Pas de mouvement à momentum** (bhop, slide, air-strafe) : ça favorise l'exécution mécanique sur la lecture, et ça rend le hit registration beaucoup plus dur à rendre honnête.

## 8. Le son

Un adversaire proche doit pouvoir **entendre** les pas, le ramper, les changements de posture, le lean et les tirs — en 3D, localisable. C'est la moitié de l'information disponible dans un duel.

**Portée par allure** (ordre d'idée, à régler en jouant) : la course s'entend de partout, la marche à peu près à moitié de cette distance, le sneak à peine. Le sneak et le ramper sont **très faibles, mais audibles** : jamais totalement muets (confirmé le 2026-10-05).

**Pas de musique pendant la manche.** La musique vit dans les menus, entre les manches et sur l'écran de victoire. Pendant le combat : quelques sons d'ambiance, et ceux des spectateurs.

**Les spectateurs font du bruit, et c'est voulu.** Leur chat vocal parvient faiblement aux duellistes (si la partie l'active). Leurs cailloux font du bruit en tombant, et **peuvent tromper** les duellistes : un caillou est un leurre sonore. Le son reste une information, mais une information qu'on peut falsifier.

**Chat vocal entre duellistes** : possible, si la partie l'active.

**La voix de l'IA** : pendant une manche, elle peut parler, mais **rarement, brièvement, et jamais fort** : juste assez pour que les deux joueurs l'entendent (tranché le 2026-10-04). Les deux l'entendent donc au même volume, quelle que soit leur position. Le reste (présentation, résultats, piques) vit avant et après la manche.

**Tous les robots font exactement les mêmes bruits.** Les cosmétiques changent la peinture et la forme, jamais le son : un robot plus discret qu'un autre serait un avantage (tranché le 2026-10-04).

**Les sons de reconstruction** du fondu au noir (§ 5) s'arrêtent avant que la manche démarre, pour qu'elle commence dans un silence où l'on entend les pas.

**Accessibilité** : pas d'indicateur visuel des sons dans le prototype. À reconsidérer plus tard.

Les sons forts (tirs, explosions) formeront un **canal séparé** des sons de contexte. C'est ce canal qui alimentera le mode **Arène dans le noir**, une envie forte (§ 11).

## 9. Les spectateurs

C'est le système qui fait l'identité du jeu : **personne n'attend sans rien faire.**

**Qui** : les joueurs du lobby qui attendent leur tour. Leur nombre dépend du mode, deux pour le prototype (§ 4). **Toutes leurs actions se règlent avant la partie**, et le classé n'en a aucune. L'intégration Twitch est abandonnée pour l'instant : un bonus possible, pas un besoin (tranché le 2026-10-04).

**Où** : une **tribune physique**, une plateforme circulaire fermée et surélevée qui fait le tour de l'arène, comme au goulag de CoD (confirmé le 2026-10-04). Chaque spectateur y est **incarné** par un robot, **à mains nues**.

**Ce qu'ils font** — un petit jeu à eux, conçu pour les occuper sans dicter le duel. Ils agissent **pendant les manches**, si le mode et les réglages de la partie le permettent :
- **Dons de matériel.** Du matériel (armes, grenades, accessoires) tombe au hasard sur leurs plateformes. Il faut **courir** pour le ramasser, puis le **lancer** à un duelliste. Un mauvais lancer peut l'envoyer au mauvais duelliste. On peut aussi le garder, sans pouvoir s'en servir, pour en priver les autres.
- **Jets de cailloux.** Il faut d'abord **casser** un morceau de la plateforme pour en récupérer un, ce qui prend du temps. Un caillou se lance sur un duelliste, pour le déstabiliser, ou sur un autre spectateur.
- **Bagarre.** Les spectateurs peuvent se frapper à coups de poing (ou se lancer des cailloux) pour s'étourdir, et voler le matériel que tient un autre.
- **Chat vocal**, faiblement audible des duellistes.
- **Votes entre les manches**, si la partie le permet : l'arme, les power-ups de la manche suivante.

**Favoriser un camp est permis**, hors classé. Ce qui garde l'équilibre, c'est la friction : il faut courir, ramasser, viser, et les autres spectateurs peuvent intercepter, frapper ou voler. Le geste peut rater, et il peut profiter à l'adversaire.

**Ce que voient les spectateurs** : les deux duellistes, en temps réel.

**Le ghosting est un risque accepté.** Hors classé, un spectateur en vocal Discord peut annoncer la position de l'adversaire à son ami. Aucune parade n'a été jugée satisfaisante (un délai sur la vue des spectateurs, une vue limitée à un seul duelliste) : elles abîment toutes le spectacle, qui est la raison d'être de la tribune. En classé, le problème ne se pose pas, puisqu'il n'y a pas de spectateurs. *(Tranché le 2026-10-02.)*

**Pas de paris.**

## 10. Arène et level design

**Plusieurs arènes**, une seule par partie. Symétriques ou asymétriques selon l'arène. Ce sont d'anciennes salles du complexe souterrain ; leurs typologies (salle de test balistique, hangar de maintenance…) sont dans la bible.

Taille intermédiaire, comparable au goulag de CoD, avec des zones pour les trois distances de combat : close, medium, long. **Un niveau de hauteur** au-dessus du sol, et l'avantage de la hauteur est **voulu**.

**Power-ups d'arène** — à des **emplacements prédéfinis**, **annoncés avant la manche** avec un compte à rebours avant leur apparition. Aucun hasard : c'est un objectif de contrôle de l'arène, pas une loterie.

**Éléments dynamiques** : les lumières (déjà au cœur de la transition entre manches).

**L'événement légendaire.** Une surprise qui ne tombe presque jamais, dans l'esprit de la légende de Herobrine dans Minecraft : pendant une manche, au hasard, toutes les lumières s'éteignent, une musique étrange se lance avec des bruits terrifiants, et une créature mystérieuse traverse l'arène pour effrayer les joueurs (sa nature reste à définir, dans la bible). **Uniquement dans les modes arcade**, jamais en classé : c'est du hasard, assumé parce qu'il est rare et qu'il ne touche pas au compétitif.

Pas de destruction totale — la lisibilité prime.

Le layout se valide par playtest avant de recevoir son habillage visuel définitif. **Le blocking actuel**, inspiré du goulag de CoD, est un premier jet bien avancé : suffisant pour tester les mécaniques principales, rien n'est fait visuellement.

## 11. Modes de jeu

Le **classique** est le cœur, et le seul du premier prototype. Les autres sont des pistes, sans engagement.

**Les deux familles** : les modes **arcade** (spectateurs, power-ups, événements, réglages libres) et le **classé** (duel pur, sans spectateurs, serveur dédié), qui arrivera après la sortie.

- **Classique** — 1v1 en BO5 ou BO3, un joueur de chaque côté de l'arène.
- **Variante à respawn continu** — au lieu d'arrêter le jeu à chaque mort, le perdant réapparaît ailleurs dans l'arène et le combat continue. Évite les coupures qui cassent le rythme. *Point faible identifié : les points de réapparition. S'ils sont exploitables, le joueur qui réapparaît se fait tuer en boucle.*
- **Until Death** — tournoi roi de la colline où les blessures restent d'une manche à l'autre : le gagnant reste avec les PV qu'il lui reste, un spectateur prend la place du perdant.
- **Hack & Defend** — un point dans l'arène, à prendre en y plantant un *spike*, comme dans Valorant. Celui qui a planté défend le point jusqu'à la fin du timer ; l'autre doit désamorcer, puis planter le sien. Respawn illimité avec temps de recharge. Deux variantes à étudier : soit la fin du timer termine une manche d'un BO5 ; soit la partie dure 3 à 5 minutes, la zone change de place plusieurs fois, et le joueur qui a tenu le plus longtemps gagne aux points.
- **Arène dans le noir** — envie forte. Le son devient l'information principale.
- **Tag Team** — un 2v2 façon catch : quand un joueur meurt, son coéquipier prend sa place. Communication entre coéquipiers ouverte ou fermée (fermée se contourne facilement par Discord). Victoire au plus de kills en temps donné, ou au premier à 100 points.
- **Golden Gun** — une seule arme, une balle tue, munitions illimitées. En temps donné ou à un objectif de points.
- **Gun Game** — chaque kill fait passer à l'arme suivante, dans un ordre fixe. Se faire tuer au taser fait redescendre d'un cran. Le premier à tuer avec la dernière arme gagne.
- *Coop Zombie* — deux joueurs contre des vagues de zombies, comme dans Call of Duty. Noté pour mémoire : ce n'est pas du duel, donc hors de l'identité du jeu.

## 12. Univers et ton

*Résumé. La référence est la bible ; ce qui suit n'est là que pour les règles qui en découlent.*

**Pourquoi ils se battent** : ils n'ont plus de mission militaire. Les duels sont devenus une activité auto-organisée : défi, divertissement, reconnaissance, entretien des compétences. Il n'y a pas de « dernière directive ».

**La voix des matchs** est l'ancienne IA de supervision : froide, au vocabulaire militaire, mais elle a évolué comme les robots et glisse de petites piques. **Elle ne se trompe jamais**, ses informations de jeu sont toujours exactes. Elle remplace le présentateur façon *The Finals* envisagé avant la bible.

**Les combattants** sont tous bâtis sur le **même châssis**, pour que les hitbox restent équitables. Ils se distinguent par leur peinture et leurs formes cosmétiques, jamais par le son (§ 8).

**Ton** : sérieux dans le ton et le thème, drôle quand même, jamais enfantin. La bizarrerie vit dans le contexte, les armes, les annonces de l'IA, les réactions du public et l'événement légendaire. Le gameplay, lui, reste nerveux, cadré, sérieux.

## 13. Direction artistique

*Résumé. La référence est la bible (§ 10 et suivants de la bible).*

**Industrielle dystopique stylisée**, en low-poly propre ou en cel-shading discret (à prototyper). La piste « cartoon et fantasy » est abandonnée (tranché le 2026-10-04).

**Le principe directeur** : **transformer les contraintes techniques, budgétaires et d'animation en choix artistiques assumés.** Le jeu est développé par une seule personne. Des robots aux pièces rigides rendent naturels un lean qui ne fait pivoter que le buste, ou un mouvement légèrement saccadé.

**La lisibilité des joueurs** (tranché le 2026-10-04) : un décor **sombre et terne**, presque sans jaune, et des robots de **couleur vive**. **Chaque robot a sa propre couleur**, pour que les spectateurs reconnaissent vite chaque duelliste, même après l'avoir perdu de vue. C'est la peinture qui l'identifie. Les réparations (plaques rapportées) sont de couleur libre.

**La personnalisation viendra après le prototype.** Quand elle arrivera, les peintures seront **limitées à des couleurs vives**, pour que personne ne puisse se camoufler dans le décor (tranché le 2026-10-04). Dans le prototype, c'est le jeu qui donne une couleur différente à chaque joueur.

Ce qu'on garde dans tous les cas : silhouettes claires, mouvement lisible, touches d'exagération, animations réactives (peu d'anticipation, gameplay d'abord), impacts et feedbacks exagérés.

*Les pistes proposées par Claude le 2026-10-02 (gladiateurs mécaniques, figurines, plateau télé) sont dépassées par la bible.*

## 14. Progression et méta

**Progression** : rangs, niveaux, et cosmétiques à débloquer. Jamais d'avantage de jeu.

**Une monnaie de pièces détachées** sert à embellir son robot, de façon purement esthétique. Le rafistolage devient ainsi la progression visible : un robot porte la trace de ses parties. **Pour l'instant, elle se gagne seulement en jouant.** La vendre contre de l'argent réel reste une possibilité, à décider plus tard (2026-10-04).

**Statistiques de fin de match** : précision, headshots, manches gagnées et perdues, manche la plus courte (avec son chrono). Chaque joueur reçoit un **profil de combat** établi par l'IA selon son style (par exemple un profil de tireur de précision pour celui qui a beaucoup visé la tête, un profil d'assaut pour celui qui a fait beaucoup de kills au fusil à pompe).

**Replays** de duels : oui.

**Classements** : oui, pour les tournois et les modes où des spectateurs prennent la place des duellistes.

## 15. Interface et prise en main

**HUD** : PV en chiffre, munitions, temps écoulé dans la manche, et les manches du match sous forme de cercles (vert pour une manche gagnée, rouge pour une perdue).

**Apprentissage** : un tutoriel, sous la forme d'une arène avec des cibles en carton et des étapes scriptées, plus un stand de tir.

**Réglages** : les classiques d'un FPS — touches personnalisables, sensibilité, FOV, réticule personnalisable, graphismes, son.

**Le flow des écrans** : écran titre → menu (jouer, paramètres…) → choix du mode, avec ses réglages à côté → lobby → duel → écran de victoire et résultats → retour au lobby ou au menu.

## 16. Le produit

- **Sortie Steam payante, autour de 5 €** (prix indicatif). Modèle « payé une fois » : pas de free-to-play.
- **PC, clavier-souris uniquement.** Aide à la visée minimale, comme sur Rainbow Six.
- **Cible de performance** : 60 fps.
- **Langues** : français et anglais au minimum, le plus possible à terme.
- **Réseau** : à la sortie, mode hôte (Relay), avec des lobbys privés à code seulement. Après la sortie, serveur dédié pour le classé ; budget serveur à évaluer selon les tarifs à ce moment-là.
- **Anti-triche côté client** : avec le classé, après la sortie. Entre amis, l'autorité serveur suffit.
- **Twitch** : abandonné pour l'instant, un bonus possible plus tard.
- **Équipe** : un développeur seul, sans échéance.
- **Assets** : à choisir une fois la DA fixée.

**Playtest.** Le premier jalon : un mini prototype fini, testé par un groupe de 5 ou 6 amis qui travaillent tous dans le jeu vidéo.

**Le périmètre de ce prototype** (validé le 2026-10-02) : le mode classique, la rotation roi de la colline, une tribune où les spectateurs regardent, et un lobby privé avec code. Quatre joueurs à la fois : deux duellistes, deux spectateurs. Si le groupe de testeurs est plus grand, les autres attendent leur tour hors du jeu (2026-10-04). Les interactions des spectateurs (cailloux, dons, bagarre) arrivent après ce premier test : il doit d'abord dire si le duel lui-même tient. Objectif : remonter les bugs techniques, et faire naître d'autres idées de règles. Leurs avis seront recueillis en discussion libre, avec prise de notes. Plus tard, avec plus de monde : un Discord, et des questionnaires.

**Public cible** : des groupes d'amis qui veulent tester leur skill et passer une soirée ensemble ; amateurs de FPS skill-based et de duels rapides. Le jeu ne cherche pas nécessairement à devenir une discipline esport : le classé est une extension, après la sortie (2026-10-04).

## 17. Questions encore ouvertes

*Les trois tensions relevées dans le questionnaire ont été tranchées le 2026-10-02 : la gerbe à motif fixe (§ 3), les spectateurs qui agissent pendant les manches (§ 9), le ghosting accepté comme un risque (§ 9).*

**À observer en playtest** :
- **Les manches passives**, maintenant qu'il n'y a plus de limite de temps (§ 5).

*Les 29 arbitrages entre la bible et le GDD ont été tranchés le 2026-10-04 ; les deux documents en tiennent compte.*

*Les points en suspens relevés ensuite ont été tranchés le même jour : peintures limitées à des couleurs vives (§ 13), lobbys privés seulement à la sortie (§ 4), quatre places pour le prototype (§ 16), monnaie gagnée en jouant (§ 14), voix de l'IA rare et discrète pendant la manche (§ 8).*

**À garder en tête pour la personnalisation** (après le prototype) :
- **Deux duellistes de la même couleur.** Limiter les peintures aux couleurs vives empêche le camouflage, mais pas que deux joueurs choisissent le même rouge, alors que chaque duelliste doit garder sa propre couleur (§ 13).
- **Les réparations de couleur libre** : suivent-elles la même limite que les peintures ? Un robot couvert de plaques grises se camouflerait autant qu'un robot peint en gris.

**Toujours ouvert** :
- Comment le joueur obtient son arme (§ 6).
- L'apparence de l'arme automatique du prototype : le MP5, ou une arme plus robotique ou futuriste (§ 6).
- Ce que lancent les spectateurs : la bible parle de *power-ups* (§ 8 et § 15 de la bible), alors que ce document réserve les power-ups à des emplacements fixes de l'arène (§ 10) et fait lancer du *matériel* aux spectateurs (§ 9). Un objet lancé par un spectateur est-il un power-up ? Relevé le 2026-10-05, sans urgence avant les interactions des spectateurs.
- Le multiplicateur des headshots, et le TTK qui en découle.
- Les power-ups concrets : lesquels, et leurs effets.
- L'ordre de priorité entre les modes, au-delà du classique.
- Ce qui relève du lore et reste ouvert est listé dans la bible (§ 18 de la bible) : la créature de l'événement légendaire, le nom de l'IA, le style de rendu final.
