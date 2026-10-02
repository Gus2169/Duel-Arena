# Questionnaire GDD — les réponses (2026-10-02)

*Archive. Ce sont les réponses brutes, recopiées telles quelles depuis le questionnaire interactif (https://claude.ai/artifact/KNPnbjR4KPc9ZkcTbwi5GL), où elles restent aussi stockées. Ce qui a été tranché est passé dans `GDD-Duel-Arena.md`, qui fait autorité. Ce fichier n’est pas tenu à jour : il garde la trace de ce qui a été dit, avec ses hésitations, pour qu’on puisse y revenir.*

## Vision et ambition

**0.1 — Quelle est l'ambition du projet ?**
- Choix : Sortie Steam payante
> Sortie stem payante (5 €)

**0.2 — Qui travaille dessus ?**
> Je suis seul

**0.3 — Y a-t-il une échéance ?**
- Choix : Aucune

**0.4 — Quels jeux sont tes références, et qu'est-ce que tu leur prends exactement ?**
- Choix : CoD goulag ; Rainbow Six Siege
> R6 pour le contrôleur, COD pour le goulag  (mode de jeu et map fermé), je cherche d'autre mode de jeux, donc a voire ou prendre d'autre ref

**0.5 — Décris une manche parfaite, du décompte au kill, en 5 ou 6 phrases.**
> alors ça dépend du mode de jeu. mais si on parle de jeu classique en 1v1. je voudrais un joueur de chaque coté de la map. Décompte, lancement du match, un kill avec mini célébration pour le gagnant (phrase écrite à l'écran) même chose coté perdant et on relance jusqu'à un gagnant. Ecran de fin pour les 2 joueurs avec le joueur gagnant mouve caméra et musique et animation (style édit tiktok aura farming) et tableau de récap.
>
> Je ne sais pas encore comment ils choisissent leurs armes, pareil pour les power up.
>
> par contre je viens d'avoir une idée, peu être un futur mode de jeu ou une variante du 1V1 classique. Au lieu de stoppé le jeu et démarrer un nouveau round, le perdant réapparait quelques pars dans la map et le jeu continue. Cela évite les coupures trop fréquente et où ça casse le rythme, et donc frustrant pour les joueurs. (point faible: spawn points, si exploité horrible pour le joueur qui réapparait et se fait spawn kill)

**0.6 — Qu'est-ce que le jeu refuse d'être ?**
- Choix : Pas de pay-to-win
> pas d'avantage à un autre joueurs en début de partie ou round, sauf exceptions de mode ou paramétrages parties

**0.7 — Le nom « Duel Arena » est-il définitif ou provisoire ?**
- Choix : Provisoire

## Structure d'une session

**1.1 — Combien de personnes dans un lobby, et comment s'organisent les duels ?**
- Choix : B. Roi de la colline : le gagnant reste, le perdant retourne dans la file
> Tes propositions sont toutes bien et mérite d'être étudié. je pense que ça se règlera dans les réglages de la partie ou en mode de jeux. sois tournois, sois plus classique le gagnant reste et un autre prend sa place, sois le hasard qui choisis.
>
> Info importantes que je vois venir. Je voudrais un paramétrage de partie complet mais pas pénible à faire. Parce que ça va faire pas mal d'option de jeu déjà et je pense que ça peut vitre être le bazar lors du setup.

**1.2 — Si le gagnant reste : garde-t-il ses PV ? Y a-t-il un plafond de victoires consécutives ?**
> tout le monde recommence à 100 de PV sauf un mode ("plus réaliste qui pourrait être mort continue ou Until Death")

**1.3 — Qu'est-ce qu'un « match » ?**
> ça dépend de le mode de jeu mais le classique seras effectivement un bo5 ou bo3 avoir. je me met à la place des spectateurs qui regarde le duel comme un combat de boxe ou un combat de gladiateur, c'est divertissent, de plus ils peuvent parfois selon les paramètres jouer un rôle dans le combat (parler avc le chat vocale, lancer des power up dans l'arène, lancé des cailloux pour déstabiliser les joueurs) donc ne pas s'ennuyer. et je me dit un round de 15-30 sec fois 5 c'est pas si long, si je me trompe on pourras toujours changer ça

**1.4 — Matchmaking public ou parties privées ?**
- Choix : Lobby privé entre amis avec un code ; File publique non classée ; File publique classée

**1.5 — Combien de temps dure une session typique, de l'ouverture du jeu à la fermeture ?**
- Choix : Plus
> 1 heures voir plus ça dépend s'il y'a du monde sur le jeu. je voudrais un jeu on on veut relancer une partie derrière.

**1.6 — Abandon en plein match ?**
- Choix : Forfait automatique

## La manche

**2.1 — Que se passe-t-il quand le temps s'écoule sans mort ?**
> je ne sais pas s'il faut vraiment faire quelque chose. En sois théoriquement, la manche dure rarement plus de 1 min mais je ne sais pas s'il faut inclure une règle de design pour forcer les joueurs à ne pas dépasser 1 min. A éclaircir je ne suis pas pour mettre de chrono en tout cas. ton avis sur le campeur est très pertinent je la garde dans un coin de ma tête

**2.2 — Les 15-30 s du GDD, c'est le temps de contact ou le temps total ?**
> je ne sais pas pourquoi à la base il y a un chrono, je pensais l'enlevé, c'était surtout pour des test mais je vais l'enlever.

**2.3 — Spawns : toujours les mêmes extrémités, ou variables ?**
- Choix : Échange de côté à chaque manche ; Variables
> je pensais en faire plusieurs diffèrent dans l'arène

**2.4 — Le décompte avant la manche**
- Choix : Regard libre seulement (actuel)
> 5 ou 3 secondes me paraissent suffisant, Regard libre et changement de position

**2.5 — À 2-2 dans un BO5 : manche décisive normale ou règle spéciale ?**
- Choix : Manche normale

**2.6 — Entre deux manches, que voit le perdant ?**
- Choix : Killcam
> les 2 joueurs je pense, a voir je ne voudrais pas casser le rythmes du combat. Ou un fade to blake pour les 2 joueurs, l'arène est plongé dans le noir, les 2 joueurs respawn (comme ça même les spectateur ne voit pas le changement brutal) ensuite l'arène rallume ces lumières et les 2 joueurs retrouve leurs places, début du nouveau round

## Combat et armes

**3.1 — Comment le joueur obtient-il son arme ?**
- Choix : Loadout choisi avant la manche
- *Je ne sais pas, on testera*
> a voir, je ne sais pas encore quelle armes faire encore. peut être un chois multiples. avant le début du match aléatoirement (comme un pile ou face) un des joueurs choisis sa classe (snipe, ar, pompes, pistolet etc...) et l'impose aussi à l'autres, le perdant chois au round d'après. Peut être frustrant pour les joueurs. Donc à étudier.
>
> Les spectateurs pourrait aussi avoir l'occasion d'en lancer sur le terrain pour avantagé un joueur.

**3.2 — Quelle deuxième arme ? Que veut dire « insolite » pour toi ?**
> Le MP5 n'est qu'un test. à la base je ne sais pas quelle DA emprunter pour mon jeu. Je ne sais pas si je veux partir sur quelque chose de réaliste (comme cod ou r6)  ou sur du cartoon voir fantasy. j'imaginais un truc facile à lire mais comme il y a des pouvoirs est des power up, j'imaginais quelque chose de pas réaliste. comme un énorme pistolet bionique démesuré mais qui tire un rayon un tout petit rayon paralysant qui ralenti l'ennemis qui fait peu de dégâts, ou a l'inverse un petit pistolet qui tire un énorme trou de noir (c'est un exemple)

**3.3 — Faut-il une zone tête, et avec quel multiplicateur ?**
- *Je ne sais pas, on testera*
> très bonne question je pense même retravailler le TTK, un head shot pour moi, me semble punitif, mais en jeu ça marche super bien et c'est satisfaisant qui il passe. don à tester mais un headsot me parais nécessaire pour certaine armes

**3.4 — Munitions et rechargement**
- Choix : Chargeur limité + rechargement

**3.5 — La visée (ADS)**
- Choix : Ralentit le déplacement
> pourquoi pas autoriser le RNG uniquement pour le tire épaulé. Et voir si il n'y a pas une autre option. Cela me semble indispensable surtout pour les armes de type pompes ou le tire au jugé est plus lisible et fluide avec des tirs projection grand angles

**3.6 — Tirer en sprintant**
- Choix : Impossible
> si le joueur vise en courant, cela arrête le sprint et comment à viser

**3.7 — Dégâts selon la distance**
- Choix : Dégâts constants
> avoir selon les armes

**3.8 — Matériaux traversables par les balles**
- Choix : Aucun
> pas pour l'instant avoir selon la map

**3.9 — Utilitaires**
- Choix : Grenades
> pourquoi pas, sois caché dans la map sois les spectateur les donner aux joueurs

**3.10 — Mêlée**
- Choix : Couteau ; Instakill
> Si plus de munition couteau automatique

**3.11 — Retour de touche**
- Choix : Hitmarker ; Son distinct pour la tête
> et son en général si ça touche et un diffèrent si c'est la tête

## Mouvement

**4.1 — Le lean est-il trop généreux ?**
- Choix : Lean réaliste (buste seulement, jambes exposées)
> je travail dessus en ce moment, le lean seras comme sur R6. Derrières un mur en train de viser et de lean en même temps, uniquement le buste seras penché sur le coté et seras exposé, le reste du corps seras protéger derrière le mur.

**4.2 — Faut-il un vrai saut ?**
- Choix : Vault seulement, choix assumé
> il faut juste un obstacle asser petit et du sol derrière, et le joueur pourras sauter par dessus

**4.3 — Dégâts de chute / mort hors-arène ?**
- Choix : Non

**4.4 — Le sprint est-il illimité ?**
- Choix : Illimité

**4.5 — Prone : peut-on tirer en rampant ? Passage debout ↔ couché instantané ou lent ?**
- Choix : Tir en rampant possible

**4.6 — Les vitesses : as-tu une sensation cible ?**
> asser nerveux et rapide, mais pas trop il faudra tester tout ça

## Le son

**5.1 — Portée d'écoute par allure**
- Choix : Sneak très faible
> le sprint on l'entend de partout, marche casi de moitié et le sneak vraiment peu c'est un ordre d'idée, il faudra tester également

**5.2 — Un indicateur visuel des sons pour les joueurs malentendants ?**
- Choix : Non
> pourquoi pas plus tard mais pas dans cette version de proto

**5.3 — Le public fait-il du bruit pendant la manche ?**
> Selon le mode de jeu et le réglage de la partie: chat vocale, (pas trop fort) pour les joueurs dans l'arène. et jet de caillou qui fait du brui et peu trompé les joueurs dans l'arène.

**5.4 — Communication entre duellistes**
- Choix : Chat vocal
> S'il est activé

**5.5 — Le mode « arène dans le noir » : envie forte ou idée lointaine ?**
- Choix : Envie forte
> et d'autre mode de jeu son à venir

## Les spectateurs

**6.1 — Qui sont les spectateurs ?**
- Choix : Les joueurs qui attendent leur tour ; Des viewers externes (Twitch)
> il y auras un nombres max de spectateur (et toujours leurs actions seront réglables avant les parties)

**6.2 — Un effet de spectateur doit-il toujours affecter les deux joueurs à égalité ?**
- Choix : On peut favoriser un camp

**6.3 — Quand agissent-ils ?**
- Choix : Seulement entre les manches (vote du modificateur suivant)
> et peuvent avoir un choix sur la suites des manches si les réglages de la parties le permettent. chois de l'armes, des power upp, don d'armes ou d'accessoires jet de cailloux etc...

**6.4 — L'anti-ghosting : que voient les spectateurs ?**
- Choix : Les deux joueurs en temps réel
> pour les rankes (ave un enjeu) je pense faire uniquement un 1v1 sans spectateur. Pour le reste. il faudra trouver une solution

**6.5 — Comment limiter les actions des spectateurs ?**
> les dons de matériels, drop au hasard sur leurs plateformes et ils devrons courir dessus pour le récupéré et le jeter aux duelliste (un mauvais lancer du spectateur peux le donner au mauvais duelliste) ou le garder sur eux (mais ne peux pas l'utiliser). Jet de cailloux qui devrons casser de leurs plateformes pour la récupérer (prend du temps),  pour pouvoir la jeter sur un duelliste ou sur un autres spectateurs. Les Spectateurs sont mains nu et peuvent se taper entre eux a coup de poings (ou se jeter des cailloux) pour s'étourdir et pour récupérer le matériel si un autre spectateur l'a en main. Avec tout ces éléments je pense que les spectateur ont de quoi s'occuper et ne pas trop déranger le duels

**6.6 — Donne-moi tes 5 à 10 idées d'interactions, même farfelues.**
> les dons de matériels, drop au hasard sur leurs plateformes et ils devrons courir dessus pour le récupéré et le jeter aux duelliste (un mauvais lancer du spectateur peux le donner au mauvais duelliste) ou le garder sur eux (mais ne peux pas l'utiliser). Jet de cailloux qui devrons casser de leurs plateformes pour la récupérer (prend du temps),  pour pouvoir la jeter sur un duelliste ou sur un autres spectateurs. Les Spectateurs sont mains nu et peuvent se taper entre eux a coup de poings (ou se jeter des cailloux) pour s'étourdir et pour récupérer le matériel si un autre spectateur l'a en main. Avec tout ces éléments je pense que les spectateur ont de quoi s'occuper et ne pas trop déranger le duels

**6.7 — Paris / pronostics sur le vainqueur avec une monnaie fictive ?**
- Choix : Non

**6.8 — Comment se déplacent les spectateurs ?**
- Choix : Tribune physique avec un avatar
> je voyais une plateformes circulaire fermé tout autour de l'arène et sur élevé comme le goulag de cod

## Arène et level design

**7.1 — Une ou plusieurs arènes ?**
- Choix : Une seule
> plusieurs map seront disponible mais une seule par game

**7.2 — Symétrique ou asymétrique ?**
- Choix : Asymétrique
> il y auras les 2 ça dépend des maps

**7.3 — Verticalité**
- Choix : 1 niveau ; Avantage de la hauteur voulu

**7.4 — Les détails « étranges » : donne des exemples concrets.**
> il s'agit de la DA je veux un truc spécial mais lisible qui sort de l'ordinaire. Unique et mémorable

**7.5 — Éléments dynamiques**
- Choix : Lumières
> je voulais faire un élément surprise et légendaire qui ne tombe quasiment jamais dans aucune game. Comme la legend de Herobrine dans Minecraft pendant un round, au hasard un monstre/créature mystérieuse débarque dans l'arène pour faire peur au joueurs. toutes les lumières s'éteignes et une musique bizarre se lance avec des bruit terrifiants (uniquement dans les mode "simple type arcade" , pas les ranked)

## Univers et ton

**8.1 — Pourquoi ces gens se battent-ils ?**
- *Je ne sais pas, on testera*
> peut être des prisonniers (mais trop classique), combattant clandestin, combat de gladiateur modernes,

**8.2 — Les personnages**
- Choix : Personnages différents
> Même gabaries et surtout bien distinctif (changement de couleur), pour que les spectateurs ne se trompent pas. C'est pour ça en plus que le cartoon ou fantaisie pourrais m'aider et pour la beauté du jeu et pour le game design et faire en sorte que les joueurs ne sois pas ressemblant

**8.3 — Un présentateur / commentateur en voix off ?**
> Pourquoi pas oui c'est possible, à voir quand le Lore du jeu sera établi.
>
> J'ai bien aimé la voix off du présentateur du jeux the finals qui présente les games comme un show télévisé

## Progression et méta

**9.1 — Y a-t-il une progression ?**
- Choix : Rangs uniquement ; Cosmétiques déblocables ; Niveaux

**9.2 — Statistiques après le match**
- Choix : Précision ; Headshots
> Points de manches perdu et gagné, manches la plus courte avec le chrono, avec un nom adapté au style de jeu (exemple: sniper s'il a tiré beaucoup de fois dans la tête, bourrin s'il a fait beaucoup de kill au fusil à pompes)

**9.3 — Replays de duels ?**
- Choix : Oui

**9.4 — Classements / leaderboards ?**
- Choix : Oui
> Si tournois ou mode de jeu qui fais participer les spectateurs et prennent la places de duelliste

## UX, HUD et onboarding

**10.1 — Qu'affiche le HUD ?**
- Choix : PV en chiffre ; Munitions ; Chrono
> Round remporté, cercle de couleur vert pour gagner et rouge pour perdu

**10.2 — Comment un nouveau joueur apprend ?**
- Choix : Tutoriel ; Stand de tir
> mode arène avec pancarte d'ennemies et étape scriptés

**10.3 — Réglages importants pour toi**
- Choix : FOV ; Sensibilité ; Réticule personnalisable ; Raccourcis
> touches personnalisables, sensibilité, graphisme, son, FOV, les paramètre classique d'un FPS

**10.4 — Le flow complet des écrans**
> Ecran de titre -> menu avec jouer, paramètres etc -> modes de jeux, avec réglages et paramètres du mode de jeux a coté de chaque modes -> lobby -> duel -> résultats avec écran finish -> retour au lobby au ou men

## Technique

**11.1 — Serveur dédié et budget**
- Choix : Mode hôte (Relay) pour les parties entre amis, dédié pour le classé
> à voir les tarrifs au niveau des serveurs on sais jamais

**11.2 — Plateforme**
- Choix : PC uniquement, clavier-souris
> les aides à la visée seront minimum comme les sur R6

**11.3 — Ping maximum acceptable**
- Choix : Matchmaking par région

**11.4 — Framerate visé**
- Choix : 60 fps

**11.5 — Anti-triche**
- Choix : Anti-triche client

**11.6 — Assets**
- *Je ne sais pas, on testera*
> À voir en fonction de la direction artistique (DA). Je cherche justement une identité visuelle, à mi-chemin entre le cartoon et la fantasy, qui me permettrait de simplifier au maximum la conception du jeu, sans que cela soit perçu comme un défaut par les joueurs.
>
> L'objectif serait de trouver un style cohérent, à la fois pour les personnages, les armes et les décors, qui soit **simple à produire, peu coûteux et peu exigeant en termes d'animation et de modélisation**. L'idée n'est pas de pousser les détails à l'extrême, mais plutôt de miser sur une identité visuelle forte et reconnaissable, capable de rendre les imperfections techniques acceptables, voire de les intégrer naturellement à l'univers du jeu.
>
> Par exemple, si les personnages sont des robots futuristes, certaines limitations d'animation pourraient devenir des choix artistiques cohérents avec leur nature. Pour le *lean* (inclinaison du personnage), il ne serait pas forcément nécessaire d'avoir une animation réaliste impliquant tout le corps. Il pourrait simplement s'agir d'une inclinaison du haut du corps, d'une rotation du bassin ou même d'un mouvement légèrement saccadé, sans que cela paraisse anormal ou dérangeant pour les joueurs.
>
> **L'idée est donc de faire de ces contraintes techniques des éléments à part entière de la direction artistique et du lore du jeu**, plutôt que de chercher à reproduire des animations réalistes qui demanderaient beaucoup plus de temps, de compétences et de ressources.
>
> Cela permettrait de conserver une expérience de jeu fluide et lisible, sans compromettre le gameplay, tout en réduisant considérablement le temps et les coûts de production. Le but serait de créer un univers dans lequel les choix artistiques justifient naturellement la simplicité des modèles, des animations et des environnements.

## Playtest

**12.1 — Qui teste, et à quelle fréquence ?**
> Je voulais d'abord avoir un mini proto finis avec au moins le mode classique et faire tester à mon groupe d'amis qui travail tous dans le jeux vidéo il y auras 5 ou 6 personnes. Pour ensuite voire les bugs technique et aussi avoir d'autres idées de règles.
>
> Quand ce seras plus poussé pourquoi pas faire un discord

**12.2 — Comment recueillir leur avis ?**
- Choix : Discussion libre
> pour mes amis discussion libres avec prise de note. et pour plus tard avec plus de monde un questionnaire

## Power-ups et modes

**13.1 — Power-ups d'arène : lesquels, et où apparaissent-ils ?**
- Choix : Emplacement fixe et annoncé
> dans l'arène il y a des points prédéfinis, pas de RNG les PU seront annoncé avant le debut du round avec un timer avant apparitions.

**13.2 — « Hack & Defend » : qu'est-ce que c'est dans ta tête ?**
> Pour ce mode je voulais un point dans la map, qui doit être pris en plantant un spike dans la zone comme dans valorant. Cette zone doit être défendu par le joueur qui a planté jusqu'à la fin du timer. L'autre joueur lui dois désamorcer le spike et planté le sien à son tour. Respawn illimité mais avec un cooldown. Sois la fin du timer arrivé signifie la fin d'une manche en BO5. Sois la partie dure genre 3 ou 5 min, la zone change de place plusieurs fois pendant la partie. Plus un joueur plante longtemps plus il gagne de points et la fin du timer on regarde qui a le plus de point pour gagner. A voir

**13.3 — Autres modes envisagés**
> Tag team : comme au catch un genre de 2v2 si un joueur meurt l'autre prend sa place, avec sois communication ouverte sois fermé pour les duos (mais facile a contré avec discord). Temps donné pendant la game, la team avec le plus de kill remporte la game. Ou objectif de point le premier à 100
>
> Golden Gun : une seul arme unique, une balle un mort (munition illimité), Temps donné ou objectif de point
>
> Gun Game : Un kill, et le joueur passe à l'armes suivante, dans l'ordres, s'il se fait cut par un couteau, il descend d'une classe. Le premier qui fait un kill avec la dernière armes gagne le combat.
>
> Mode tournois Until death: 2 duellistes des spectateurs, le gagnant reste, un spectateur prend la place
>
> (A voir car rien à voir avec le jeu de base ce n'est pas du duel) Coop Zombie; 2 joueur, des vagues de zombie qui arrive essayer de survivre (comme call of duty)

## Sensation et direction artistique

**13.4 — La mort : jusqu'où va l'exagération ?**
- Choix : Ragdoll projeté ; Effet sonore signature
> replay instantané exagéré avec slow motion, type sniper Elite 4 ou mortal kombat

**13.5 — Le recul : quelle arme de référence pour la sensation ?**
- Choix : La R-301 d'Apex
> a voir, mais je suis vraiment pas fan des courtes rafales, je veux un bon feeling avec les armes. Sauf s'il s'agit d'une DMR coup par coup.

**13.6 — Recevoir une balle**
- Choix : Flinch (la visée sursaute)
> je ne pensais pas m'être d'effet mais ce n'est pas si inintéressant, pourquoi pas l'écran qui devient rouge de sang avec un son de hit, et un très léger sursaut caméra mais pas exagéré

**13.7 — Références visuelles, palette, époque ou lieu**
> justement je n'ai aucune idée pour ça et je ne sais pas trop ou chercher à vrai dire, je suis ouvert à toute proposition du moment que ça répond aussi à la question 11.6 du questionnaire
>
> Concevoir un univers visuel cohérent qui transforme les contraintes techniques, budgétaires et d'animation en choix artistiques assumés.

**13.8 — La lisibilité des joueurs**
- *Je ne sais pas, on testera*
> A voir avec la DA choisis

**13.9 — Musique**
- Choix : Menus et entre les manches seulement
> quelques bruit environnementaux, et bruits lié aux spectateurs s'il y en a.

## Produit

**13.10 — Modèle économique, si sortie**
- Choix : Payant une fois
> 5 euros je pense pour l'instant ça peux changer

**13.11 — Langues**
- Choix : Les deux
> Le plus possibles

**13.12 — Le jeu doit-il être pensé pour être regardé ?**
- Choix : Intégration Twitch pour les spectateurs
> oui en mode arcade plus léger, également pour les modes plus sérieux comme la ranked

**13.13 — Le prototype « blocking du goulag CoD » est-il terminé ?**
> C'est premier jet, mais il est bien avancé, au moins pour tester les mécaniques principales. Esthétiquement et visuellement rien est fait
