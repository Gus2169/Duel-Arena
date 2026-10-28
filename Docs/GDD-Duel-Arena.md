# Duel Arena — Game Design Document

*L'âme du jeu. Ce document dit ce que Duel Arena **est** et ce qu'il refuse d'être — pas où en est le code. L'état technique, les priorités et les dettes vivent dans `CLAUDE.md`, à la racine du projet.*

*Refondu le 2026-10-02 à partir du questionnaire de conception. Les réponses brutes, avec leurs hésitations, sont archivées dans `Docs/Questionnaire-GDD.md`.*

*Le monde et l'apparence du jeu (lore, personnages, armes, arènes, ton, direction artistique) vivent dans la bible, `Docs/Bible-Lore-DA.md`. **Ce document-ci fait foi pour les règles du jeu, la bible pour le monde et l'apparence.** En cas de conflit sur une règle de jeu, c'est le GDD qui gagne (tranché le 2026-10-04).*

*« Duel Arena » est un nom **provisoire**. Parmi les noms proposés par Claude le 2026-10-25, « Last Unit Standing » a retenu l'utilisateur : « il y a de l'idée mais c'est pas encore ça ». La recherche continue.*

*Revu le 2026-10-25 à partir d'une seconde revue de conception (55 questions : ce qui n'avait jamais été tranché, ce qui était resté flou, ce que le jeu contredisait, ce qui avait changé en route), puis de sa suite le même jour (15 questions : les précisions, et les listes demandées à Claude), et le 2026-10-26 à partir de la page de la refonte des armes (34 questions : les douze armes, l'armurerie, le choix en match). Chaque décision de ces revues porte sa date.*

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
- **Pas de RNG dans la précision, sauf à la hanche.** Deux joueurs qui tirent la même rafale dans les mêmes conditions subissent exactement le même recul. Le recul est une courbe apprenable, jamais une dispersion aléatoire. Le regard revient vers l'axe pendant la rafale comme après, à une vitesse propre à chaque arme, réglée dans l'éditeur des armes (tranché par l'utilisateur le 2026-10-27). **Épaulée, la balle part exactement dans l'axe.** Les armes à gerbe (fusil à pompe) tirent selon un **motif fixe**, identique à chaque tir : large et lisible au jugé, mais jamais aléatoire (tranché le 2026-10-02). **Seule exception, le tir à la hanche** (tranché par l'utilisateur le 2026-10-16, comme dans *Rainbow Six* : le tir au jugé était trop facile) : la balle part **au hasard dans un cône**, qu'on voit au réticule et qu'on maîtrise (§ 6). Épauler reste le seul moyen d'être précis.
- **En classé, aucun avantage de latence structurel.** Le classé arrivera **après la sortie**, sur serveur dédié. D'ici là, toutes les parties tournent en mode hôte, qui donne un léger avantage à celui qui héberge : accepté entre amis.
- **Le son est une information de gameplay**, au même titre que le visuel. Jamais un habillage.
- **En classé, le duel est pur** : pas de spectateurs, pas d'interventions. Ailleurs, les spectateurs peuvent peser sur le duel (voir § 9), mais toujours par un **geste physique imparfait** : courir, ramasser, viser, lancer. Jamais par un simple bouton qui donnerait un avantage à coup sûr.
- **Tout ce qui sort du duel pur se règle par partie** : spectateurs, power-ups, chat vocal, événements. Le classé les désactive.

## 4. Structure d'une session

**Un lobby de plusieurs joueurs, deux duellistes à la fois.** Les autres sont spectateurs, dans la tribune, et attendent leur tour. Le nombre de places dépend du mode et des réglages de l'hôte : **six au plus pour l'instant**, soit quatre spectateurs (2026-10-25). On verra plus tard s'il en faut davantage.

**L'organisation des duels se règle par partie** :
- **Roi de la colline** — le format par défaut. Le gagnant reste, le perdant retourne dans la file, le suivant prend sa place. **Pas de plafond de victoires** : un champion reste tant qu'il gagne (2026-10-25).
- **Duellistes fixes** — les deux premiers arrivés se battent tout le match ; les autres regardent depuis la tribune (ajouté le 2026-10-13).
- **Tournoi** — arbre à élimination dans le lobby.
- **Hasard** — l'adversaire suivant est tiré au sort.

*Le 2026-10-13 : l'hôte règle, dans le salon de sa partie (le lobby privé à code), le nombre de places (2 à 6, 4 par défaut), l'organisation (roi de la colline ou duellistes fixes) et les interventions des spectateurs (§ 9) ; tous les joueurs du salon voient ces réglages. Les premiers arrivés se battent ; les suivants attendent dans leur ordre d'arrivée, et le premier de la file remplace un duelliste qui s'en va.*

**Qui commence** (2026-10-27, demandé par l'utilisateur) : l'hôte et **le deuxième arrivé au salon** sont les duellistes ; le troisième et les suivants attendent sur la tribune, dans leur ordre d'arrivée au salon, même s'ils entrent dans l'arène avant les autres. **Option « duellistes aléatoires »** (oui ou non, au salon) : les deux duellistes du premier match sont tirés au sort parmi tous les joueurs, l'hôte compris, au lancement, pour qu'on ne sache pas d'avance qui commence ; le salon montre alors deux points d'interrogation. Le roi de la colline et les interventions sont des cases à cocher du salon : ce sont des options du classique, plus des modes (§ 11).

**Un match** se joue en BO5 ou en BO3, **au choix de l'hôte**, dans le salon (2026-10-25). Une manche dure rarement plus d'une minute, donc un BO5 reste court même pour ceux qui attendent.

**Tout le monde repart à 100 PV** à chaque manche, gagnant compris. Exception : le mode *Until Death* (§ 11), où les blessures restent.

**Abandon en plein match** (2026-10-25) : **forfait**, l'autre gagne le match. S'il y a un spectateur, un nouveau match commence ensuite contre lui ; sinon, le gagnant attend un adversaire.

**Matchmaking** : à la sortie, **uniquement des lobbys privés avec un code**, sans file publique : sans serveur dédié, une file publique mettrait des inconnus face à un hôte qui a l'avantage de la latence (tranché le 2026-10-04). **Après la sortie** : la file classée (sans spectateurs, sur serveur dédié), avec un matchmaking par région pour limiter le ping.

**Le réglage des parties doit être complet sans être pénible.** Il y aura beaucoup d'options, et le risque est un écran de configuration indigeste. Piste : des **préréglages** nommés (classique, arcade…) qui couvrent 90 % des cas, avec un onglet avancé pour le reste.

## 5. La manche

**Pas de limite de temps.** Une manche se termine par une mort. Le chrono actuel du code était un outil de test, il disparaît.

*Le risque connu : deux joueurs passifs peuvent faire durer une manche indéfiniment. Ce n'est pas un problème tant que les playtests ne le montrent pas. Si ça arrive, la piste privilégiée est la **révélation** : au bout d'un moment, chaque joueur émet un son de sa position. La règle devient une mécanique sonore plutôt qu'une sanction.*

**Spawns** : plusieurs points différents dans l'arène, avec échange de côté d'une manche à l'autre. Leur disposition dépendra surtout des modes de jeu, donc elle sera fixée plus tard ; d'ici là, le prototype garde ses deux points opposés (2026-10-05).

**Préparation** (tranché le 2026-10-12) : avant chaque manche, un panneau s'ouvre au centre de l'écran et chaque joueur y choisit son arme (§ 6, *Le choix de l'arme*), puis valide. Le décompte part dès que les deux ont validé, au plus tard au bout de **30 secondes**, à chaque manche (2026-10-26 ; 20 avant). Les joueurs sont replacés et immobiles pendant ce temps. Une fois la manche lancée, on garde son arme.

**Décompte** : 3 à 5 secondes. Le regard et le changement de posture sont libres, le déplacement non.

**À 2-2**, la manche décisive est une manche normale.

**Échange de tirs** : quand deux joueurs se tirent dessus au même instant, le premier tir reçu par le serveur gagne ; un robot mort ne tire plus (tranché le 2026-10-12). Cela donne un léger avantage à l'hôte, comme le reste du mode hôte (§ 3).

**Le kill** déclenche une **mini-célébration** : une phrase à l'écran, **la même pour tous**, « Untel a gagné la manche », avec le score (2026-10-12, confirmé le 2026-10-25 ; une phrase différente pour le gagnant et le perdant n'est plus voulue).

**Le gagnant garde la main** (tranché le 2026-10-12) : après le kill, pendant quelques secondes (5 pour l'instant), le survivant peut encore bouger, tirer, se pencher, s'accroupir : de quoi narguer le perdant, qui regarde son épave. Ses tirs ne comptent plus. Puis vient le fondu au noir.

**Entre deux manches** : fondu au noir. Toutes les lumières de l'arène s'éteignent, les deux joueurs sont replacés dans l'obscurité, puis la lumière revient et la manche suivante démarre. Le replacement n'est jamais vu, pas même par les spectateurs. Dans le noir, **on entend la reconstruction des châssis** (perceuses, servomoteurs). Pas de killcam : le replay du coup mortel en tient lieu (§ 6, 2026-10-25).

**Fin de match** : un écran de victoire mis en scène pour le gagnant (mouvement de caméra, musique, animation, dans l'esprit d'un montage « aura farming »), puis un tableau récapitulatif pour les deux. Drôle, jamais enfantin (confirmé le 2026-10-04). *Première version le 2026-10-12, demandée par l'utilisateur : le robot du gagnant en gros plan à gauche, « Untel a gagné ! » et le tableau des scores à droite (§ 14). Un nouveau match repart ensuite, ou l'on quitte la partie. Revue le même jour après essai : le robot célèbre **dans l'arène**, à un endroit fixe choisi pour le cadrage (comme les meilleurs joueurs de Call of Duty), **sans arme ni gant**, en enchaînant **au hasard** les célébrations fournies (jamais deux fois la même de suite), les mêmes au même moment sur les deux écrans. Restent la musique et le mouvement de caméra.*

**Revanche ou retour au salon** (le principe le 2026-10-25, les règles de l'utilisateur le 2026-10-26) : sur l'écran de fin, chaque duelliste choisit « Revanche » ou « Retour au salon ».
- La revanche demande les voix **des deux** duellistes.
- **Une seule voix pour le salon y ramène tout le monde aussitôt** : « c'est plus un choix qu'un vote ».
- Sans les deux voix au bout de **15 secondes**, retour au salon.
- Au salon, l'hôte retrouve les réglages de la partie (manches, places, organisation des duels, interventions) et la relance ; les autres l'attendent dans le salon.
- En roi de la colline, quand quelqu'un attend dans la file, la revanche devient « **Match suivant** » : le perdant cède sa place.
- Après un forfait (§ 4), pas de choix : le match suivant démarre à la fin de l'écran.
- « Quitter la partie » ramène toujours au menu.

## 6. Combat et armes

**TTK** — la cible de départ est ≈ **0,7 seconde** de tir soutenu, mais elle est **à retravailler**, notamment avec l'arrivée des headshots. Elle vit dans l'inspecteur, pas en dur dans le code. **La règle d'équilibre** (2026-10-26) : chaque arme tue en un temps proche à sa bonne distance, et moins bien ailleurs.

**Les armes du prototype sont des armes de test** (le « MP5 » d'origine est devenu le fusil d'assaut). L'apparence des armes est décrite dans la bible (§ 12 de la bible) : conventionnelles, prototypes, bricolées, absurdes.

**Douze armes, quatre familles** (tranché le 2026-10-26, page « Refonte des armes », après l'annonce de l'utilisateur du 2026-10-25) : les trois modèles de chaque classe, qui n'étaient que des apparences d'une même arme, deviennent **des armes uniques et différentes**, chacune avec ses caractéristiques, son tir et son son.
- **Quatre familles** de trois armes : le fusil d'assaut, le fusil de précision, le fusil à pompe et le pistolet. **D'autres familles viendront plus tard** (mitraillette, mitrailleuse…).
- **Dans chaque famille, la même règle** : l'arme du stock est la référence, celle de la série 30 prend un risque (plus rapide ou plus lourde), le prototype laser a une mécanique à lui.
- **Une famille partage ses gestes et son son** : ses trois armes ont les mêmes points de prise, donc les mêmes animations, et chaque famille garde un son reconnaissable, pour que le son dise toujours ce qui tire (§ 8).
- **Le tir et le son suivent l'arme** (2026-10-25) : une arme laser tire un rayon rouge, une arme à balles crache une flamme.
- **Toutes sont ouvertes dès le départ** (§ 14).

Les modèles (2026-10-15, bible § 12) : le stock (FA-07, FP-12, FAP-03, PA-05) ; la série 30 (FA-30, FP-30, FAP-30, RV-30), des armes à balles aux formes modernes, d'après des références d'*Apex Legends* fournies par l'utilisateur (le R-301, une carabine à levier tactique, un fusil à pompe à dissipateurs, le Wingman) ; les prototypes laser du programme d'essai (FX-1 à FX-4), aux formes franchement futuristes. Le FP-30, sans chargeur, se charge par une porte sous la boîte de culasse ; les armes laser ont une cellule d'énergie à la place du chargeur.

**Les douze armes et leurs chiffres de départ** : les rôles ont été choisis par l'utilisateur le 2026-10-26 ; les chiffres sont des propositions de Claude du même jour, réglées sur *Call of Duty* et *Rainbow Six*, que l'utilisateur corrigera dans l'éditeur des armes. Un robot a 100 PV.

| Arme | Rôle | Dégâts au buste (tête, jambes) | Tir | Chargeur | Rechargement | Baisse des dégâts (portée) | Sortie, poids | Pour tuer, visé au buste |
|---|---|---|---|---|---|---|---|---|
| **FA-07** | polyvalent, la référence | 17 (×1,4) | automatique, 7 coups/s | 30 | 2,2 s | aucune (100 m) | 0,6 s, ×0,95 | 6 balles, 0,71 s |
| **FA-30** | cadence élevée, épaule vite (×0,8) | 17 (×1,4) | automatique, 9 coups/s | 25 | 2 s | ×0,65 de 15 à 35 m (80 m) | 0,5 s, ×0,95 | 6 balles, 0,56 s ; 10 au-delà de 35 m, 1 s |
| **FX-1** | le FA-07 en rayons, recul ×0,6 | 17 (×1,4) | automatique, 7 rayons/s | chaleur : 22 tirs d'affilée (3 s) | vidange 1,5 s | aucune (100 m) | 0,6 s, ×0,95 | 6 rayons, 0,71 s |
| **FP-12** | à verrou, lunette ×4, la référence | 100 (×1,5 ; jambes ×0,6) | 0,8 coup/s | 5 | 3 s | aucune (150 m) | 0,8 s, ×0,9 | 1 balle |
| **FP-30** | à levier, plus rapide et plus mobile | 55 (×2 ; jambes ×0,8) | 1,6 coup/s | 4, une à une | 0,5 s par cartouche | aucune (120 m) | 0,6 s, ×0,95 | 2 balles, 0,63 s ; 1 à la tête |
| **FX-2** | tir chargé, lunette ×4 | de 30 à 100 selon la charge, pleine en 1 s (×1,5 ; jambes ×0,6) | à chaque relâche, 0,4 s entre deux | chaleur : 3 tirs pleins d'affilée | vidange 1,5 s | aucune (150 m) | 0,8 s, ×0,9 | 1 tir chargé à fond ; à la tête, chargé à plus de la moitié |
| **FAP-03** | à pompe, la référence | 8 plombs de 18 (aucun bonus) | 1,1 coup/s | 6, une à une | 0,55 s par cartouche | ×0,3 de 8 à 20 m (35 m) | 0,65 s, ×0,95 | 1 coup jusqu'à 5 m (7,5 m en visée) |
| **FAP-30** | slug : une seule balle lourde | 100 (×2 ; jambes ×0,6) | 1 coup/s | 5, une à une | 0,55 s par cartouche | ×0,55 de 15 à 30 m (50 m) | 0,65 s, ×0,95 | 1 slug jusqu'à 15 m, 2 au-delà ; 1 à la tête jusqu'à 30 m |
| **FX-3** | éventail : 8 rayons à plat | 20 par rayon (aucun bonus) | 1,4 coup/s | chaleur : 6 coups d'affilée | vidange 1,5 s | ×0,35 de 6 à 18 m (30 m) | 0,65 s, ×0,95 | 1 coup jusqu'à 4 m (6 m en visée), 2 jusqu'à 7 m (10 m en visée) |
| **PA-05** | semi-automatique, le plus mobile, sort le plus vite | 26 (×1,5) | 4,5 coups/s | 15 | 1,5 s | ×0,75 de 15 à 35 m (60 m) | 0,35 s, ×1 | 4 balles, 0,67 s |
| **RV-30** | revolver lourd, recul fort | 45 (×2,25 ; jambes ×0,8) | 2,2 coups/s | 6 | 2,6 s | ×0,8 de 20 à 40 m (60 m) | 0,5 s, ×0,97 | 3 balles, 0,91 s ; 1 à la tête jusqu'à 20 m |
| **FX-4** | rayon qui chauffe, cadence libre | 20 (×1,5) | au doigt, 7 rayons/s au plus | chaleur : 12 tirs d'affilée | vidange 1,5 s | ×0,7 de 15 à 35 m (60 m) | 0,4 s, ×1 | 5 rayons, 0,57 s au plus vite |

*Le FA-07, le FP-12 et le FAP-03 gardent les chiffres des armes d'aujourd'hui : ce sont les références. Les distances de la dernière colonne sont calculées pour un tir centré sur le buste d'un robot debout. « Épaule vite » : le temps pour épauler, comparé à celui du FA-07.*

**La chaleur des armes laser** (tranché le 2026-10-26) : elles n'ont pas de chargeur. **L'arme chauffe en tirant et refroidit seule ; trop chaude, il faut la vider, par le geste du rechargement.** La chaleur ne change ni les dégâts ni la précision : elle dit seulement combien on peut encore tirer. Elle se lit dans le HUD, sur une jauge, à la place du compte des balles (§ 15). *Propositions de Claude, valeurs de départ :*
- elle commence à refroidir 0,3 s après le dernier tir (1,5 s au FX-2, le temps d'une charge), et refroidit entièrement en 2,5 s (FX-1, FX-4) à 4 s (FX-2, FX-3) : des rafales courtes tirent bien plus longtemps qu'un tir continu ;
- **pleine, elle surchauffe** : l'arme ne tire plus et se vide d'elle-même, en 2,5 s ;
- **la touche de rechargement la vide à tout moment**, plus vite (1,5 s) ; comme un rechargement, la vidange s'interrompt en sprintant ou au taser, et on ne vise pas pendant.

**Le FX-1** (précision de l'utilisateur, 2026-10-26) : moins de recul que le FA-07, mais moins de tir continu : il surchauffe après 3 s de tir, quand le FA-07 vide ses 30 balles en 4 s.

**Le tir chargé du FX-2** (tranché le 2026-10-26) : on maintient la gâchette pour charger, on relâche pour tirer ; **chargé à fond, il tue au buste** ; relâché tôt, il blesse moins, sans aucun hasard. *Propositions de Claude :* la charge est pleine en 1 s, et se garde tant qu'on tient la gâchette ; elle se voit et s'entend, si bien que l'adversaire sait qu'un coup arrive ; un tir chargé à fond chauffe l'arme d'un tiers. *Dans le prototype depuis le 2026-10-26 : la charge ne monte que quand l'arme pourrait tirer (ni pendant une vidange, ni en sortant d'un sprint), elle se voit au bout du canon (un éclat rouge et sa lumière) et s'entend (une montée en charge qui finit quand elle est pleine), et le tireur la suit sur une jauge sous le réticule. Proposition de Claude : un tir chauffe autant, chargé ou non (sinon, relâcher tôt ferait tirer plus longtemps).*

**L'éventail du FX-3** (tranché le 2026-10-26) : une gerbe à plat, très large de côté, fine en hauteur, toujours la même (§ 3). Elle pardonne un adversaire qui se décale, pas un tir trop haut ou trop bas. *Proposition de Claude :* huit rayons en ligne, sur 4,2° de chaque côté et 0,3° en hauteur ; épaulée, elle se resserre (×0,7), comme la gerbe du pompe.

**Le slug du FAP-30** (tranché le 2026-10-26) : une seule balle lourde, pour la moyenne distance. N'ayant pas de gerbe, il a un cône à la hanche, comme les fusils : il faut épauler. *Le cône (3° à l'arrêt, 5° en bougeant) est une proposition de Claude.*

**La carabine à levier FP-30** (tranché le 2026-10-26) : deux balles au buste, une à la tête, plus rapide et plus mobile que le FP-12 ; quatre cartouches dans son magasin, glissées une à une par la porte de chargement (précisions de l'utilisateur). Tirer interrompt le rechargement, comme au pompe. *Sa lunette compacte ×2,5, que l'on peut remplacer par un viseur, est une proposition de Claude.*

**Le FX-4** (tranché le 2026-10-26) : un rayon qui chauffe au lieu de se recharger : on tire aussi vite qu'on presse la détente, tant qu'il ne surchauffe pas. *Proposition de Claude : 7 tirs par seconde au plus, la limite que vérifie le serveur.*

*Du 2026-10-15 à la refonte, les trois modèles de chaque classe n'étaient que des apparences de quatre armes : un FX-1 tirait comme le fusil d'assaut, avec sa flamme et son bruit de balle, et le RV-30 tirait le rayon du pistolet laser. Depuis le 2026-10-26, le prototype joue les douze armes à leurs chiffres de départ ; restent les sons propres aux nouvelles armes (empruntés à leur famille ou au pistolet laser) et l'interface définitive.*

**L'éditeur des armes** (demandé le 2026-10-25, précisé le 2026-10-26) : l'utilisateur veut un outil pour tout régler facilement, sans toucher au code : les chiffres de chaque arme, ses accessoires et leurs effets, les power-ups, et **les motifs de tir**, le recul coup par coup comme les gerbes. **L'ordre de la refonte** (2026-10-26) : les rôles et les chiffres de départ (ci-dessus), puis l'éditeur et les nouvelles fiches d'armes, puis l'interface (l'armurerie, le panneau de préparation, la jauge de chaleur).

**Une seule arme** (2026-10-25) : l'inventaire n'en compte qu'une, choisie pendant la préparation (§ 5) et gardée toute la manche ; **les touches 1 à 4 disparaissent** avec la refonte. L'utilisateur : « elles ne font pas partie des jeux », et « dans un duel, on peut n'avoir qu'une seule arme dans son inventaire ». Seule exception : ramasser une arme lancée par un spectateur (§ 9), qui remplace la sienne. L'arme met un temps à sortir (de 0,35 s au PA-05 à 0,8 s aux fusils de précision), pendant lequel on ne tire pas. *Les touches 1 à 4 ont disparu du prototype le 2026-10-26 : l'arme se choisit au clic dans le panneau de préparation (ou en attendant un adversaire), jamais pendant la manche (2026-10-12).*

**Le choix de l'arme** (tranché le 2026-10-12, précisé le 2026-10-26) : **chaque joueur choisit librement la sienne avant chaque manche**, pendant la préparation (§ 5), et la garde toute la manche.
- Le panneau montre **les favorites en grand** (trois au plus, marquées à l'armurerie : § 15), et les autres derrière « **Toutes les armes** ».
- **La dernière arme jouée est présélectionnée** : on valide, ou on change. *À la toute première manche d'un joueur, ce serait le FA-07 (proposition de Claude).*
- **Un bouton Accessoires** retouche ceux de l'arme choisie.
- **Le choix de l'adversaire reste caché toute la manche** : le son le trahira.
- **Personne n'a d'avantage au choix**, pas même le perdant de la manche : chacun choisit à égalité. Seul le mode Pile ou face en décide autrement (ci-dessous).

**L'identité « insolite »** : des armes qui jouent sur le décalage entre leur apparence et leur effet. Exemples cités : un énorme pistolet bionique démesuré qui tire un minuscule rayon paralysant (ralentit beaucoup, blesse peu), ou à l'inverse un petit pistolet qui tire un énorme trou noir. **Ce sont les armes qu'on n'obtient que des spectateurs**, ou, dans une partie sans spectateurs, des **power-ups d'arène** (tranché le 2026-10-25) : elles restent hors du duel pur.

**Les armes bricolées sont un style visuel**, pas une mécanique : elles fonctionnent comme les autres, sans défaut ni enrayement (tranché le 2026-10-04).

**Pas de pouvoirs propres aux personnages.** Seulement des power-ups, ramassés dans l'arène (tranché le 2026-10-04).

**Obtenir son arme** (tranché le 2026-10-25) : **le choix libre pendant la préparation, dans tous les modes**. **Le pile ou face a son propre mode**, nommé « Pile ou face » (§ 11) : un tirage désigne un joueur, qui choisit une **famille d'armes** (fusil d'assaut, précision, pompe, pistolet) et l'**impose aussi à l'adversaire** ; **chacun choisit ensuite son arme dans cette famille** (2026-10-26). Le perdant de la manche choisit la famille de la suivante. Les spectateurs, selon les réglages, votent l'arme (§ 9) ou lancent dans l'arène une arme insolite (§ 9, 2026-10-26).

**Headshots** : nécessaires pour certaines armes. Punitifs sur le papier, mais très satisfaisants en jeu. **Façon *Call of Duty*, propres à chaque arme** (tranché le 2026-10-11 ; pour les douze, voir le tableau) : aucun bonus aux armes à gerbe ; les fusils de précision ne tuent pas d'une balle aux jambes ; le FP-30, le FAP-30 et le RV-30 tuent d'une balle à la tête. À rejuger en jouant.

**Munitions** : chargeur limité, rechargement. **À vide, la gâchette recharge** (2026-10-27, demandé par l'utilisateur, comme dans les FPS classiques) : presser la détente chargeur vide lance le rechargement, et une arme automatique dont on tient la gâchette sur sa dernière balle recharge aussitôt. **On ne vise pas en rechargeant** (demandé par l'utilisateur le 2026-10-16, comme dans *Rainbow Six*) : l'arme reste à la hanche jusqu'à la fin du rechargement, puis remonte si la touche de visée est tenue. Aux armes chargées cartouche par cartouche (FAP-03, FAP-30, FP-30), tirer interrompt le rechargement, et ce coup part de la hanche. Les armes laser n'ont pas de chargeur : elles chauffent (ci-dessus).

**Visée (ADS)** : ralentit le déplacement. Viser en courant **coupe le sprint** et lance la visée. On ne tire pas en sprintant.

**Viseurs** : de vrais viseurs (point rouge, holographique…), dont la réticule reste sur la cible quand on vise. **Chaque joueur choisit le sien avant la partie**, dans un menu « armurerie » : son style **et** son grossissement. Tous les viseurs sont accessibles à tous, c'est ce qui garde l'équité (tranché le 2026-10-09). Le premier en jeu est le viseur intégré au fusil d'assaut ; son nouveau modèle (2026-10-15) a une mire métallique précise, et ce viseur numérique deviendra un viseur de l'armurerie. **Les fusils de précision visent en lunette plein écran**, façon *Call of Duty* (×4, sensibilité réduite d'autant, tranché le 2026-10-11) : le FP-12 et le FX-2. *Le FP-30 aurait une lunette compacte ×2,5 (proposition de Claude, 2026-10-26).* Les dix viseurs sans grossissement de *Rainbow Six* arrivent avec les accessoires (2026-10-16, ci-dessous). **Des viseurs grossissants viendront aussi**, de ×1,5 à ×2,5 comme dans *Rainbow Six*, sur certaines armes : l'utilisateur dira lesquels (confirmé le 2026-10-25).

**Les accessoires de l'armurerie** (liste de l'utilisateur du 2026-10-16, appelée à évoluer), façon création de classe de *Call of Duty* et de *Rainbow Six* : **ils changent l'arme qui les porte** (tranché par l'utilisateur le 2026-10-16 ; ils étaient cosmétiques depuis le 2026-10-12). **Tous sont ouverts à tous dès le départ**, comme les viseurs : un choix de configuration, jamais un avantage débloqué (§ 14). Ils se choisissent arme par arme, dans l'armurerie, et se voient sur l'arme tenue, par tous les joueurs et les spectateurs. **Chaque arme garde ses accessoires et sa finition** : en match, elle arrive configurée, et le bouton Accessoires de la préparation les retouche (2026-10-26). Valeurs provisoires, à juger en jouant :
- **Viseurs** : les dix viseurs sans grossissement de *Rainbow Six* (points rouges A, B et C ; holographiques A, B, C et D ; réflex A, B et C), ou la mire de l'arme. Ils ne changent que l'image de visée. Les fusils de précision gardent leur lunette ; les pistolets ne prennent que les trois réflex, les seuls qui tiennent sur une glissière (propositions de Claude, validées le 2026-10-25). *Le FP-30 pourrait remplacer sa lunette par un viseur (proposition de Claude, 2026-10-26).*
- **Laser tactique** : toujours allumé, son faisceau et son point se voient de partout, il **trahit sa position**. En échange, il **aide le tir au juger** : il resserre la dispersion à la hanche (×0,75, le cône comme la gerbe du pompe, comme dans *Rainbow Six* jusqu'en 2024), et son point montre où pointe l'arme, utile aux fusils de précision, qui n'ont pas de réticule à la hanche (2026-10-16 ; avant, il réduisait le recul à la hanche). **C'est un vrai laser** (demandé par l'utilisateur le 2026-10-16) : son faisceau sort de l'émetteur droit dans l'axe du canon et suit l'arme, pendant le rechargement, le sprint ou la sortie de l'arme comme en visée.
- **Poignées**, une seule sous le garde-main, pas sur les pistolets : la **verticale** retient le recul (20 % de recul vertical en moins), l'**inclinée** fait viser plus vite (épaulement ×0,8), comme dans *Rainbow Six*.
- **Chargeur grande capacité** : la moitié de balles en plus. En contrepartie, un rechargement plus long de 15 % (une arme chargée cartouche par cartouche a seulement plus de cartouches à glisser). Proposition de Claude, validée le 2026-10-25. *Sur une arme laser, ce serait une cellule grande capacité : la moitié de chaleur en plus avant la surchauffe, une vidange 15 % plus longue (proposition de Claude, 2026-10-26).*
- **Pas de silencieux** (tranché le 2026-10-25) : le son est une information de jeu, aucun accessoire n'y touche.

Un accessoire monté prend la finition de son arme (validé le 2026-10-25). **D'autres accessoires viendront**, choisis le 2026-10-25 parmi les propositions de Claude (valeurs de départ, réglables dans l'éditeur des armes) :
- **crosse légère** : épaulement 15 % plus rapide, recul 10 % plus fort ;
- **crosse lourde** : recul 15 % plus faible, épaulement 15 % plus lent ;
- **compensateur** : recul horizontal 30 % plus faible, recul vertical 10 % plus fort ;
- **frein de bouche** : premiers coups 25 % plus stables, flamme de bouche plus grande (on voit mieux le tireur) ;
- **canon long** : dégâts qui baissent 30 % plus loin, arme 10 % plus lente à sortir et à épauler ;
- **canon court** : arme 10 % plus rapide à sortir et à épauler, dégâts qui baissent plus tôt ;
- **chargeur rapide** : rechargement 25 % plus rapide, 20 % de balles en moins.

Écartés : le cache-flamme et la bretelle. Comme les premiers, ils sont ouverts à tous, et chacun se paie d'une contrepartie. *Chaque arme accepterait ceux de sa famille, à régler dans l'éditeur (proposition de Claude, 2026-10-26).* Les armes sont modélisées nues, avec leurs rails et leurs points d'accroche.

**Les finitions de l'armurerie** (adoptées le 2026-10-15) : chaque arme se choisit aussi dans une **finition d'usure**, qui raconte depuis combien de temps elle sert et comment on l'a réparée : *d'origine*, *repeinte* (couche après couche, l'apprêt rouge au bord des éclats), *rouillée*, *dépareillée* (des pièces prises sur d'autres armes), *polie par les mains* (la peinture partie là où on la tient), *encrassée* (huile et poussière). Comme les accessoires, elles sont **purement cosmétiques** : aucun effet de jeu, ni sur la silhouette, les hitbox ou le son (§ 8). Le choix se fait arme par arme, dans l'armurerie. **Elles se voient en partie**, sur l'arme tenue, par tous les joueurs et les spectateurs, comme la peinture du robot. Les six existent pour les trois familles d'armes (le stock, la série 30, les armes laser). Comment on les obtient : § 14.

**Tir à la hanche, façon *Rainbow Six*** (tranché par l'utilisateur le 2026-10-16) : la balle part **au hasard dans un cône**, que les branches du réticule montrent. **En visée, pas de réticule** : on vise avec l'arme (2026-10-16). Il est petit à l'arrêt, **s'ouvre en se déplaçant** (jusqu'au double à l'élan d'un sprint) et **au fil d'une rafale**, se resserre accroupi et allongé, et **se referme pendant qu'on épaule**, à la vitesse de la famille d'arme : la précision arrive tôt au pistolet et au pompe, à mi-course au fusil de précision, à la fin au fusil d'assaut (comme dans *Rainbow Six*). Épaulée, la balle part dans l'axe. C'est là que les armes à gerbe brillent : le FAP-03 et le FX-3 n'ont pas de cône, leur gerbe à motif fixe (§ 3) couvre un grand angle de façon lisible ; en visée, elle se resserre. **Les fusils de précision ont le plus large cône** et pas de réticule à la hanche (tranché le 2026-10-16 ; il tirait juste à la hanche depuis le 2026-10-11) : il faut épauler.

**Dégâts selon la distance** : propres à chaque arme (voir le tableau, 2026-10-26). C'est surtout ce qui donne à chaque arme sa distance.

**Matériaux traversables** : **aucun, jamais** (tranché le 2026-10-25). Une vitre ou un grillage arrêtent une balle comme un mur.

**Grenades** (tranché le 2026-10-25) : un **power-up**, à un emplacement de l'arène (§ 10). Ni dans la panoplie, ni chez les spectateurs.

**Mêlée** : un **taser** plutôt qu'un couteau, plus logique contre des robots (tranché le 2026-10-05). **Mort instantanée**, comme le couteau de *Call of Duty* (confirmé le 2026-10-25). C'est une **action brève, au choix du joueur**, comme le coup de couteau rapide de *Call of Duty* : on le déclenche, il frappe, on reprend son arme (tranché le 2026-10-08). Il ne sort plus automatiquement quand l'arme est vide. **C'est un gant à décharge électrique** (modèle de l'utilisateur, référence *Fallout*, 2026-10-11), porté sur l'avant-bras gauche : le coup est un direct du poing gauche. Les autres le voient toujours au bras du robot ; en vue subjective, il n'apparaît que pendant le coup, comme le couteau de *Call of Duty*, pour ne jamais masquer l'arme.

**Recul** — référence de sensation : la R-301 d'Apex. **Les rafales longues doivent être viables et agréables** : pas de jeu qui force les rafales courtes, sauf pour une DMR au coup par coup. Le recul est stylisé et lisible : montée rapide sur les premiers tirs puis plafonnement, avec un pattern horizontal en « S » quand la rafale s'allonge. Il est modulé par la posture, le déplacement et la visée. *Retour de l'utilisateur (2026-10-25) : les motifs de recul propres à chaque arme ne sont pas du tout au point dans le prototype. À refaire avec la refonte, dans l'éditeur des armes, qui réglera aussi les gerbes (2026-10-26).*

**Le tir se voit à l'arme** (demandé par l'utilisateur le 2026-10-16) : une arme à balles n'a pas de traçant, mais une flamme de bouche ; seul le rayon d'une arme laser se voit en l'air, rouge (2026-10-25).

**Retour de touche** : hitmarker, un son de touche, et un son différent pour la tête.

**Recevoir une balle** : des **parasites et des interférences** à l'écran (un robot ne saigne pas), un son d'impact, et un **très léger** sursaut de caméra. Rien d'exagéré : le sursaut punit déjà le joueur touché en premier.

**La mort** : le châssis vole en pièces (ragdoll, pas de gore), un son signature, et un **replay instantané exagéré au ralenti**, dans l'esprit de la killcam de *Sniper Elite* ou des finish de *Mortal Kombat*. *Première étape le 2026-10-12 : le robot tombe en ragdoll, poussé par le coup qui l'a tué (plus fort au sniper et au pompe), et les tirs du gagnant font encore bouger l'épave ; le joueur mort la regarde de l'extérieur. Calmée le même jour (« violent » au pompe et au taser, retour de l'utilisateur) : l'épave recule d'un pas et tombe, elle ne vole pas ; tué en courant, le robot roule un peu, sans glisser au loin. Les pièces qui volent, le son et le replay restent à faire. Le 2026-10-16, l'utilisateur veut « un réel effet de puissance » : **le pompe à bout portant et le taser projettent l'épave** dans le sens du coup, un peu soulevée (de 2,5 à 3,5 m en arrière ; le pompe de loin, moins) ; les autres armes la poussent toujours d'un pas.*

**Le replay du coup mortel** (tranché le 2026-10-25) : au ralenti, vu par tous. C'est le seul replay du jeu : ni killcam pour le mort, ni enregistrement de matchs entiers.

## 7. Mouvement

**Trois paliers de vitesse** : **sneak** (lent, très discret) < **marche** < **course** (rapide, bruyante). Le compromis vitesse/discrétion est un vrai choix tactique. La sensation visée : nerveux et rapide, sans excès, à régler en jouant.

**Sprint illimité**, pas d'endurance.

**Sprint vers l'avant seulement**, diagonales avant comprises : jamais de côté ni en arrière, comme dans la plupart des FPS (tranché le 2026-10-10). **On ne recharge pas et on ne frappe pas en courant** : recharger ou donner un coup de taser arrête le sprint, et se mettre à courir pendant un rechargement l'annule, comme dans *Call of Duty* ou *Rainbow Six*. Le coup de taser, lui, va à son terme : pas de sprint pendant le geste. **Sortir d'un sprint prend du temps** (tranché le 2026-10-11) : l'élan de la course retombe de 10 à 5 m/s en 0,2 s au lieu de 0,06, et il faut 0,3 s après la fin du sprint pour tirer ou commencer un rechargement, le temps de remonter l'arme. Se mettre à courir engage : un adversaire qui tient l'angle tire le premier. Le coup de taser, lui, part tout de suite, comme le couteau rapide de *Call of Duty*. **On ne recharge pas en franchissant un obstacle** : un rechargement demandé pendant le franchissement part à la réception, et franchir pendant un rechargement l'interrompt. **On n'y tire pas et on n'y frappe pas non plus** (tranché le 2026-10-11).

**Le poids des armes** (tranché le 2026-10-11, façon *Call of Duty*) : l'arme en main ralentit légèrement le déplacement. Propre à chaque arme (§ 6) : ×1 au PA-05 et au FX-4, ×0,9 au FP-12 et au FX-2, ×0,95 à la plupart des autres.

**On recule et on va de côté moins vite qu'en avançant** (tranché le 2026-10-10, comme dans beaucoup de FPS) : de côté ×0,85, en arrière ×0,7, les diagonales avant gardant toute la vitesse. Valeurs provisoires, à juger en jouant : c'est la vitesse d'un peek en pas chassé et celle à laquelle on rompt un engagement.

**Les pieds ne glissent jamais** (exigence de l'utilisateur, 2026-10-10) : « on n'accepte pas les glissades ». Quand une animation ne peut pas suivre une vitesse, c'est la vitesse qui s'adapte, pas l'inverse. À l'arrêt, le bas du corps garde son cap quand on tourne : seul le buste suit le regard, puis les jambes pivotent à petits pas. Se baisser et se relever font de même : le pied qui doit changer de place se lève.

**Postures** Debout / Accroupi / Prone, chacune avec sa vitesse, sa hauteur de vue et son empreinte. Se relever est bloqué s'il n'y a pas la place. **On peut tirer en rampant.**

**Allongé, on rampe lentement et seulement vers l'avant ou l'arrière** (2026-10-10, conséquence de « les pieds ne glissent jamais ») : environ 0,43 m/s à l'allure de marche, la vitesse de l'animation de reptation (1,25 m/s jusque-là, où le corps glissait trois fois plus vite que ses mains et ses genoux), et pas de déplacement de côté, faute d'animation. À rouvrir avec une reptation plus rapide et une reptation latérale. *Retour de l'utilisateur après un premier essai (2026-10-25) : la lenteur passe, mais ne pas pouvoir se déplacer de côté est frustrant. L'utilisateur cherche une animation de reptation de côté ; sur Mixamo, il n'y a qu'une roulade latérale, qui ne lui convient pas du tout. Autre piste : que Claude l'anime dans Blender.*

**Changer de posture prend du temps**, même pour un robot : environ 0,3 s entre debout et accroupi, 0,65 s entre accroupi et allongé, 0,9 s entre debout et allongé. Pendant ce temps, le corps, la vue, la vitesse et l'endroit où l'on peut être touché passent progressivement d'une posture à l'autre ; on ne franchit pas d'obstacle à moitié relevé. Un plongeon au sol n'est donc pas une esquive instantanée. Valeurs de départ, à régler en jouant (2026-10-06).

**Le lean façon Rainbow Six** : seul le **buste** se penche. Derrière un mur, en visant et en se penchant, on n'expose que le haut du corps ; les jambes restent à couvert. Son amplitude se rapproche de celle de R6 : la tête sort d'environ 35 cm, valeur de départ à régler en jouant (tranché le 2026-10-05 ; amplitude et vue accroupie jugées bonnes en jouant le 2026-10-06).

Contrairement à Rainbow Six, **le lean reste possible en position allongée**, mais il y prend une autre forme : le buste **roule sur lui-même** et la vue bascule, la tête ne sortant que d'une dizaine de centimètres (demandé le 2026-10-06 ; un premier essai faisait glisser le haut du corps sur le côté).

**Pas de saut libre.** La touche de saut sert au **vault** : un obstacle assez bas, du sol derrière, et on passe par-dessus. C'est un choix assumé.

**Pas de dégâts de chute**, pas de mort hors-arène.

**Pas de mouvement à momentum** (bhop, slide, air-strafe) : ça favorise l'exécution mécanique sur la lecture, et ça rend le hit registration beaucoup plus dur à rendre honnête.

*Le 2026-10-25, l'utilisateur envisage une **glissade** : s'accroupir en sprintant ferait glisser le robot, « pour ajouter plus de fluidité et de nervosité ». Elle contredit la règle ci-dessus, qui vient du premier questionnaire : à trancher (§ 17). D'ici là, la règle reste.*

## 8. Le son

Un adversaire proche doit pouvoir **entendre** les pas, le ramper, les changements de posture, le lean et les tirs — en 3D, localisable. C'est la moitié de l'information disponible dans un duel.

**Portée par allure** (ordre d'idée, à régler en jouant) : la course s'entend de partout, la marche à peu près à moitié de cette distance, le sneak à peine. Le sneak et le ramper sont **très faibles, mais audibles** : jamais totalement muets (confirmé le 2026-10-05).

**Pas de musique pendant la manche.** La musique vit dans les menus, entre les manches et sur l'écran de victoire. Pendant le combat : quelques sons d'ambiance, et ceux des spectateurs. *Le 2026-10-25, l'utilisateur trouve que les morceaux actuels ne collent pas au jeu, et surtout les bruitages de l'interface : à remplacer, mais « c'est vraiment secondaire ».*

**Les spectateurs font du bruit, et c'est voulu.** Leur chat vocal parvient faiblement aux duellistes (si la partie l'active). Leurs cailloux font du bruit en tombant, et **peuvent tromper** les duellistes : un caillou est un leurre sonore. Le son reste une information, mais une information qu'on peut falsifier.

**Chat vocal entre duellistes** : possible, si la partie l'active. **Voulu dans le jeu**, avec la discussion écrite du salon ; s'il faut payer un service, ce sera pour plus tard (2026-10-25).

**La voix de l'IA** : pendant une manche, elle peut parler, mais **rarement, brièvement, et jamais fort** : juste assez pour que les deux joueurs l'entendent (tranché le 2026-10-04). Les deux l'entendent donc au même volume, quelle que soit leur position. Le reste (présentation, résultats, piques) vit avant et après la manche. **Elle écrit toujours à l'écran, et ne parle à voix haute que pour les moments forts** (2026-10-25).

**Pas de silencieux** (2026-10-25) : aucun accessoire n'étouffe un tir.

**Tous les robots font exactement les mêmes bruits.** Les cosmétiques changent la peinture et la forme, jamais le son : un robot plus discret qu'un autre serait un avantage (tranché le 2026-10-04).

**Les sons de reconstruction** du fondu au noir (§ 5) s'arrêtent avant que la manche démarre, pour qu'elle commence dans un silence où l'on entend les pas.

**Accessibilité** : pas d'indicateur visuel des sons dans le prototype. À reconsidérer plus tard.

Les sons forts (tirs, explosions) formeront un **canal séparé** des sons de contexte. C'est ce canal qui alimentera le mode **Arène dans le noir**, une envie forte (§ 11).

## 9. Les spectateurs

C'est le système qui fait l'identité du jeu : **personne n'attend sans rien faire.**

**Qui** : les joueurs du lobby qui attendent leur tour. Leur nombre dépend du mode, quatre au plus pour l'instant (six places, § 4). **Toutes leurs actions se règlent avant la partie**, et le classé n'en a aucune. L'intégration Twitch est abandonnée pour l'instant : un bonus possible, pas un besoin (tranché le 2026-10-04).

**Où** : une **tribune physique**, une plateforme circulaire fermée et surélevée qui fait le tour de l'arène, comme au goulag de CoD (confirmé le 2026-10-04). Chaque spectateur y est **incarné** par un robot, **à mains nues**.

**Ce qu'ils font** — un petit jeu à eux, conçu pour les occuper sans dicter le duel. Ils agissent **pendant les manches**, si le mode et les réglages de la partie le permettent :
- **Dons de matériel.** Du matériel (des armes insolites, voir ci-dessous ; les grenades sont des power-ups d'arène, § 6) tombe au hasard sur leurs plateformes. Il faut **courir** pour le ramasser, puis le **lancer** à un duelliste. Un mauvais lancer peut l'envoyer au mauvais duelliste. On peut aussi le garder, sans pouvoir s'en servir, pour en priver les autres.
- **Jets de cailloux.** Il faut d'abord **casser** un morceau de la plateforme pour en récupérer un, ce qui prend du temps. Un caillou se lance sur un duelliste, pour le déstabiliser, ou sur un autre spectateur.
- **Bagarre.** Les spectateurs peuvent se frapper à coups de poing (ou se lancer des cailloux) pour s'étourdir, et voler le matériel que tient un autre.
- **Chat vocal**, faiblement audible des duellistes (voulu ; plus tard s'il faut payer un service, § 8).
- **Votes entre les manches**, **si l'hôte les active en créant la partie** (2026-10-25) : l'arme, les power-ups de la manche suivante.

**Les règles du prototype** (2026-10-13, à la demande de l'utilisateur ; valeurs provisoires, à juger en jouant) :
- **Sur la passerelle**, on marche, on court et on s'accroupit. On ne vise pas (le clic droit lève la garde, ci-dessous), on ne tire pas, on ne se penche pas, on ne se couche pas et on ne franchit rien : le garde-corps retient les spectateurs au-dessus de l'arène.
- **Les cailloux** viennent de blocs fissurés, le long du mur de la tribune : un bloc cède au quatrième coup de poing et lâche un caillou, puis se reforme en 8 secondes. Un caillou lancé se brise sur ce qu'il touche, et son bruit peut tromper (§ 8). **On vise, puis on lance** (2026-10-27, demandé par l'utilisateur) : l'objet en main, le clic droit maintenu arme le bras et montre **au seul lanceur** la trajectoire de l'objet jusqu'à ce qu'il touche le décor, avec un cercle à son point de chute ; le clic gauche lance. Relâcher le clic droit renonce. *Proposition de Claude, à juger en jouant :* un lancer part toujours à pleine force (on règle la distance par la hauteur du regard, la trajectoire montrée fait le reste), et la trajectoire traverse les robots : seul le décor l'arrête.
- **Un caillou sur un duelliste** : aucun dégât, une **secousse de visée**. Le regard saute de quelques degrés, du côté où va le caillou : aucun hasard (§ 3). Sur un spectateur, il l'étourdit.
- **Le robot touché voit d'où vient le caillou** (2026-10-27, demandé par l'utilisateur, comme le sang et la zone rouge circulaire de *Call of Duty*) : une **zone rouge** sur un cercle autour du réticule, tournée vers l'endroit d'où le caillou a été lancé, qui tourne avec la vue et s'efface en deux secondes ; du même côté, au bord de l'écran, de **légers arcs électriques** grésillent un instant (un robot ne saigne pas, § 6). Le duelliste à chaque caillou ; un spectateur quand sa garde ne l'a pas arrêté. *Proposition de Claude, à juger en jouant :* la zone montre le point du lancer, pas le lanceur s'il a bougé depuis.
- **La bagarre** : un coup de poing étourdit un autre spectateur 2,5 secondes (1,5 au caillou). L'étourdi ne bouge plus et lâche ce qu'il tient : c'est ainsi qu'on vole le matériel. Après un étourdissement, un court répit, pour que personne ne reste au sol.
- **Les coups s'enchaînent, la garde pare** (2026-10-27, demandé par l'utilisateur). Marteler le clic gauche frappe du gauche, puis du droit, puis du gauche. **Le clic droit lève la garde**, les deux poings levés devant soi : elle arrête les coups de poing et les cailloux qui arrivent **de face** ; ce qui vient de côté, de dans le dos ou d'au-dessus passe. *Propositions de Claude, à juger en jouant :* en garde, on ne frappe pas, on ne court pas et on marche à l'allure d'un duelliste qui vise ; un coup paré s'entend (un choc sourd, distinct du coup qui touche) ; la garde couvre 70° de part et d'autre du regard.
- **La molette** (2026-10-27, demandé par l'utilisateur) passe des poings, par défaut, à l'objet qu'on tient (le caillou, ou l'arme à lancer), et inversement. Ce qu'on ramasse arrive en main ; rangé, il ne se lance pas et ne se voit pas, et un étourdi le lâche quand même : la garde ne protège pas du vol.
- **Le matériel, ce sont des armes** (tranché le 2026-10-13). Il tombe au hasard sur la passerelle pendant les manches, une arme toutes les 12 à 20 secondes, deux au plus à la fois. Un spectateur la ramasse, ne peut pas s'en servir, et la lance dans l'arène. **Le duelliste qui ramasse une arme lancée l'a en main avec un chargeur plein, même en pleine manche.** **Ce seront seulement les armes insolites, quand elles existeront** (tranché le 2026-10-26) : les douze armes du duel ne tombent pas de la tribune. *Conséquence relevée par Claude, à confirmer : d'ici leur arrivée, aucune arme ne tombe sur la passerelle, et la variante Dons d'armes (§ 11) n'a rien à donner. Le prototype fait encore tomber les armes de l'arsenal.*
- On lance pendant la manche et pendant que le gagnant garde la main, jamais pendant la préparation ni le décompte. Chaque nouvelle manche repart d'une arène vide.
- Tout cela se coupe à l'hébergement (« interventions des spectateurs ») ; les spectateurs regardent alors sans agir.

**Favoriser un camp est permis**, hors classé. Ce qui garde l'équilibre, c'est la friction : il faut courir, ramasser, viser, et les autres spectateurs peuvent intercepter, frapper ou voler. Le geste peut rater, et il peut profiter à l'adversaire.

**Ce que voient les spectateurs** : les deux duellistes, en temps réel.

**Le ghosting est un risque accepté.** Hors classé, un spectateur en vocal Discord peut annoncer la position de l'adversaire à son ami. Aucune parade n'a été jugée satisfaisante (un délai sur la vue des spectateurs, une vue limitée à un seul duelliste) : elles abîment toutes le spectacle, qui est la raison d'être de la tribune. En classé, le problème ne se pose pas, puisqu'il n'y a pas de spectateurs. *(Tranché le 2026-10-02.)*

**Pas de paris.**

## 10. Arène et level design

**Plusieurs arènes**, une seule par partie. Symétriques ou asymétriques selon l'arène. Ce sont d'anciennes salles du complexe souterrain ; leurs typologies (salle de test balistique, hangar de maintenance…) sont dans la bible.

**La première vraie arène sera un centre d'entraînement rapproché** (bible § 13 : modules de murs, couloirs et angles serrés ; choisi le 2026-10-25). Les tests des deux prochaines semaines se font sur le blocking ; le vrai playtest viendra ensuite, sur cette nouvelle arène (2026-10-25). Les cinq types de salles de la bible serviront au jeu fini.

Taille intermédiaire, comparable au goulag de CoD, avec des zones pour les trois distances de combat : close, medium, long. **Un niveau de hauteur** au-dessus du sol, et l'avantage de la hauteur est **voulu**.

**Power-ups d'arène** — à des **emplacements prédéfinis**, **annoncés avant la manche** avec un compte à rebours avant leur apparition. Aucun hasard : c'est un objectif de contrôle de l'arène, pas une loterie. On y trouvera **les grenades** et, dans une partie sans spectateurs, **les armes insolites** (§ 6), ainsi que les power-ups choisis le 2026-10-25 parmi les propositions de Claude (valeurs de départ) :
- **plaque de blindage** : 25 points de bouclier jusqu'à la fin de la manche, visibles sur le robot ;
- **réparation** : rend 50 points de vie d'un coup (c'est elle qui change le plus le temps pour tuer : à essayer avec prudence) ;
- **balise** : l'adversaire émet un bip toutes les 2 s pendant 8 s ;
- **servomoteurs** : 20 % de vitesse en plus pendant 10 s, et des pas plus bruyants.

Écartés : les munitions et la surcharge du gant. **Un éditeur** permettra à l'utilisateur de changer facilement les power-ups et leurs caractéristiques, comme les accessoires (§ 6, demandé le 2026-10-25).

**Éléments dynamiques** : les lumières (déjà au cœur de la transition entre manches).

**L'événement légendaire.** Une surprise qui ne tombe presque jamais, dans l'esprit de la légende de Herobrine dans Minecraft : pendant une manche, au hasard, toutes les lumières s'éteignent, une musique étrange se lance avec des bruits terrifiants, et une créature mystérieuse traverse l'arène pour effrayer les joueurs : **un prototype abandonné ou défectueux** du complexe (bible § 13, 2026-10-25). **Uniquement dans les modes arcade**, jamais en classé : c'est du hasard, assumé parce qu'il est rare et qu'il ne touche pas au compétitif. **Ce n'est pas un mode** : sa carte quitte le menu (2026-10-25).

Pas de destruction totale — la lisibilité prime.

Le layout se valide par playtest avant de recevoir son habillage visuel définitif. **Le blocking actuel**, inspiré du goulag de CoD, est un premier jet bien avancé : suffisant pour tester les mécaniques principales. *Le 2026-10-27, à la demande de l'utilisateur, il est habillé en salle de test balistique (bible § 13), sous une lumière « projecteurs, pénombre », **sans que son layout change** : les collisions restent celles du blocking, et le sol de l'arène reste éclairé de façon égale, pour que personne ne campe dans le noir.*

## 11. Modes de jeu

Le **classique** est le cœur, et le seul du premier prototype.

**Les modes de la sortie** (choisis le 2026-10-25, revus le 2026-10-27) : Classique, avec ses options cochées au salon (le roi de la colline, les interventions des spectateurs, les duellistes aléatoires), et Dons d'armes ; plus Tournoi, Tag Team, Golden Gun, Gun Game, Zone et Pile ou face (§ 6). Le classé suit, après la sortie. **Gardés pour plus tard**, après la sortie (2026-10-25) : Arène dans le noir, Until Death, Hack & Defend, le respawn continu, Capture. Les autres ci-dessous restent des pistes, sans engagement.

**Les deux familles** : les modes **arcade** (spectateurs, power-ups, événements, réglages libres) et le **classé** (duel pur, sans spectateurs, serveur dédié), qui arrivera après la sortie.

- **Classique** — 1v1 en BO5 ou BO3, un joueur de chaque côté de l'arène.
- **Variante à respawn continu** — au lieu d'arrêter le jeu à chaque mort, le perdant réapparaît ailleurs dans l'arène et le combat continue. Évite les coupures qui cassent le rythme. *Point faible identifié : les points de réapparition. S'ils sont exploitables, le joueur qui réapparaît se fait tuer en boucle.*
- **Until Death** — tournoi roi de la colline où les blessures restent d'une manche à l'autre : le gagnant reste avec les PV qu'il lui reste, un spectateur prend la place du perdant.
- **Hack & Defend** — un point dans l'arène, à prendre en y plantant un *spike*, comme dans Valorant. Celui qui a planté défend le point jusqu'à la fin du timer ; l'autre doit désamorcer, puis planter le sien. Respawn illimité avec temps de recharge. Deux variantes à étudier : soit la fin du timer termine une manche d'un BO5 ; soit la partie dure 3 à 5 minutes, la zone change de place plusieurs fois, et le joueur qui a tenu le plus longtemps gagne aux points.
- **Arène dans le noir** — envie forte. Le son devient l'information principale.
- **Tag Team** — un 2v2 façon catch : quand un joueur meurt, son coéquipier prend sa place. Communication entre coéquipiers ouverte ou fermée (fermée se contourne facilement par Discord). Victoire au plus de kills en temps donné, ou au premier à 100 points.
- **Golden Gun** — une seule arme, une balle tue, munitions illimitées. En temps donné ou à un objectif de points.
- **Gun Game** — chaque kill fait passer à l'arme suivante, dans un ordre fixe. Se faire tuer au taser fait redescendre d'un cran. Le premier à tuer avec la dernière arme gagne.
- **Zone** — **capturer une zone fixe** (défini le 2026-10-25). Le reste est à préciser : comment se gagne la manche, et ce qui distingue ce mode de Capture et de Hack & Defend.
- **Capture** — annoncé dans le menu principal, adopté tel quel le 2026-10-12 et jamais défini ; gardé pour plus tard.

*Le 2026-10-27 (l'utilisateur) : **le roi de la colline n'est plus un mode, mais une option du classique** (à cocher ou non au salon), et les cartes Spectateurs et Cailloux quittent l'écran Jouer : la tribune qui intervient est une option du classique. Les modes pas encore disponibles sont assombris sous un grand cadenas et ne se choisissent plus.*
*Le 2026-10-17, l'écran Jouer du menu prend les neuf cartes de la maquette de l'utilisateur : Classique, Roi de la colline et Spectateurs jouables ; Dons d'armes et Cailloux, déjà compris dans Spectateurs ; Tournoi, Arène dans le noir, Événement légendaire et Classé verrouillés. Capture et Zone n'y figurent plus.* *Le 2026-10-25 : la carte Événement légendaire doit quitter le menu (c'est une surprise rare, § 10 ; retirée le 2026-10-26, il reste huit cartes), et Zone revient parmi les modes de la sortie.*
- *Coop Zombie* — deux joueurs contre des vagues de zombies, comme dans Call of Duty. Noté pour mémoire : ce n'est pas du duel, donc hors de l'identité du jeu.

## 12. Univers et ton

*Résumé. La référence est la bible ; ce qui suit n'est là que pour les règles qui en découlent.*

**Pourquoi ils se battent** : ils n'ont plus de mission militaire. Les duels sont devenus une activité auto-organisée : défi, divertissement, reconnaissance, entretien des compétences. Il n'y a pas de « dernière directive ».

**La voix des matchs** est l'ancienne IA de supervision : froide, au vocabulaire militaire, mais elle a évolué comme les robots et glisse de petites piques. **Elle ne se trompe jamais**, ses informations de jeu sont toujours exactes. Elle remplace le présentateur façon *The Finals* envisagé avant la bible. Son nom reste à trouver : les propositions de Claude du 2026-10-25 n'ont pas convaincu. Ses répliques s'écrivent à deux, l'utilisateur et Claude (2026-10-25).

**Les combattants** partagent tous **la même silhouette et les mêmes zones touchables**, pour que les hitbox restent équitables. **Plusieurs designs de robot sont possibles** (souhaité par l'utilisateur le 2026-10-25), à condition de tenir dans cette silhouette : c'est faisable, puisque les zones touchables se calculent à partir de la posture et du lean, pas du modèle. **Chaque design couvre autant de ses zones touchables que le CT-01** (68 % de face, 62 % de profil ; demandé par l'utilisateur le 2026-10-26) : un robot plus fin serait plus dur à voir et à viser pour la même surface touchable. Les robots se distinguent par leur design, leur peinture et leurs formes cosmétiques, jamais par le son (§ 8).

**Un robot reconstruit se souvient de tout** : la défaite ne lui coûte que des pièces (bible § 7, 2026-10-25).

**Ton** : sérieux dans le ton et le thème, drôle quand même, jamais enfantin. La bizarrerie vit dans le contexte, les armes, les annonces de l'IA, les réactions du public et l'événement légendaire. Le gameplay, lui, reste nerveux, cadré, sérieux.

## 13. Direction artistique

*Résumé. La référence est la bible (§ 10 et suivants de la bible).*

**Industrielle dystopique stylisée**, en low-poly propre ou en cel-shading discret (à prototyper). La piste « cartoon et fantasy » est abandonnée (tranché le 2026-10-04). **Un essai des deux rendus, bien distincts, sur le robot**, pour choisir en les voyant (demandé le 2026-10-25, fait le 2026-10-26 : le Y Bot actuel, lisse, à côté de ses versions low poly propre et cel-shading ; puis le **CT-01**, un robot inspiré de KAY/O de *Valorant*, demandé par l'utilisateur, en trois styles : low poly propre, cel-shading, et les deux mêlés). Le choix du rendu, et du robot, revient à l'utilisateur. Les armes, elles, sont déjà en low poly propre, avec une usure peinte dans le matériau.

**Le principe directeur** : **transformer les contraintes techniques, budgétaires et d'animation en choix artistiques assumés.** Le jeu est développé par une seule personne. Des robots aux pièces rigides rendent naturels un lean qui ne fait pivoter que le buste, ou un mouvement légèrement saccadé.

**La lisibilité des joueurs** (tranché le 2026-10-04) : un décor **sombre et terne**, presque sans jaune, et des robots de **couleur vive**. **Chaque robot a sa propre couleur**, pour que les spectateurs reconnaissent vite chaque duelliste, même après l'avoir perdu de vue. C'est la peinture qui l'identifie. Les réparations (plaques rapportées) peuvent être de couleur terne, **sur de petites pièces seulement** (tranché le 2026-10-25), pour que personne ne se camoufle.

**Les peintures sont limitées à des couleurs vives**, pour que personne ne puisse se camoufler dans le décor (tranché le 2026-10-04). **Chaque joueur choisit la sienne** dans le menu Personnalisation, parmi les couleurs de la palette (demandé par l'utilisateur le 2026-10-16) : le serveur la lui donne si aucun autre joueur présent ne la porte, sinon la première couleur libre. Deux robots n'ont donc jamais la même couleur. **Le châssis se choisit aussi** dans Personnalisation (2026-10-26, demandé par l'utilisateur) : le CT-01, le robot du jeu, le prototype (le robot des premiers essais) ou l'OS-02, l'« Ossature », tous à la même silhouette. Le reste de la personnalisation du robot viendra après le prototype : **les marquages** (numéros, bandes, logos peints) et **les accessoires** (antennes, plaques, casques, dans la silhouette). **Pas d'effets** : la rubrique du menu disparaît (2026-10-25).

Ce qu'on garde dans tous les cas : silhouettes claires, mouvement lisible, touches d'exagération, animations réactives (peu d'anticipation, gameplay d'abord), impacts et feedbacks exagérés.

*Les pistes proposées par Claude le 2026-10-02 (gladiateurs mécaniques, figurines, plateau télé) sont dépassées par la bible.*

## 14. Progression et méta

**Progression** (précisée le 2026-10-25) : **le niveau montre l'expérience et rapporte de la monnaie de pièces détachées** ; il ne débloque rien par lui-même. **Les rangs sont ceux du classé**, après la sortie. Les cosmétiques se débloquent par la monnaie et par les défis. Jamais d'avantage de jeu : **les douze armes sont ouvertes dès le départ** (2026-10-26), comme les accessoires.

**Les finitions d'arme** (§ 6, 2026-10-15) : toutes au choix pendant le prototype. Ensuite, elles se débloquent **en jouant avec l'arme**, comme les camouflages de *Call of Duty*. La finition d'origine est acquise d'emblée ; chaque défi suit l'histoire de sa finition, et **se lit sous la finition verrouillée, avec sa progression** (2026-10-26). Les défis choisis le 2026-10-25 parmi les propositions de Claude (valeurs de départ) :
- **encrassée** : 25 éliminations avec l'arme (jamais nettoyée) ;
- **rouillée** : 30 éliminations accroupi ou allongé (l'arme a traîné au sol) ;
- **repeinte** : 25 manches gagnées après avoir été touché (repeinte après chaque éraflure) ;
- **dépareillée** : 10 matchs gagnés avec l'arme (une pièce prise au vaincu à chaque victoire) ;
- **polie par les mains** : 100 éliminations (la peinture partie là où on la tient) ;
- et **un défi propre à chaque arme** pour la dernière finition : 50 tirs à la tête au sniper, 50 éliminations à moins de 5 m au pompe…

*À préciser : les cinq finitions ont déjà leur défi, et la proposition ne disait pas laquelle est « la dernière » (§ 17).*

**Une monnaie de pièces détachées** sert à embellir son robot, de façon purement esthétique. Le rafistolage devient ainsi la progression visible : un robot porte la trace de ses parties. **Pour l'instant, elle se gagne seulement en jouant.** La vendre contre de l'argent réel reste une possibilité, à décider plus tard (2026-10-04).

**Statistiques de fin de match** : précision, headshots, manches gagnées et perdues, manche la plus courte (avec son chrono). *Premier tableau le 2026-10-12 : manches gagnées, précision, tirs à la tête, dégâts infligés et manche gagnée la plus rapide de chaque joueur, comptés pendant les manches seulement.* Chaque joueur reçoit un **profil de combat** établi par l'IA selon son style (par exemple un profil de tireur de précision pour celui qui a beaucoup visé la tête, un profil d'assaut pour celui qui a fait beaucoup de kills au fusil à pompe).

**Replays** : seulement celui du coup mortel, à chaque fin de manche (§ 6, 2026-10-25). Pas d'enregistrement de matchs entiers.

**Classement** (tranché le 2026-10-25) : **le tableau de la soirée**, dans le salon : qui a gagné quoi depuis l'ouverture de la partie. Pas de classement mondial.

## 15. Interface et prise en main

**HUD** : PV en chiffre **et en jauge**, munitions (pour une arme laser, une jauge de chaleur à la place du compte des balles ; au FX-2, la charge du tir sur une jauge sous le réticule : 2026-10-26), temps écoulé dans la manche, et les manches du match sous forme de cercles (vert pour une manche gagnée, rouge pour une perdue). *En place le 2026-10-12, avec le pseudo de chaque joueur à côté de ses cercles. La jauge, demandée par l'utilisateur le même jour, est celle de Rainbow Six : en bas à gauche, la part perdue reste rouge un instant avant de se vider ; rouge à 30 PV et moins.*

**Le pseudo** (2026-10-12) : chaque joueur choisit le sien, 16 caractères au plus ; depuis le 2026-10-17, dans la carte du joueur, en haut à droite des écrans du menu. Il apparaît au score, dans les phrases de fin de manche et sur l'écran de fin. Sans pseudo, « Joueur 1 », « Joueur 2 » ; deux pseudos identiques sont numérotés.

**Le menu d'Échap** en partie : reprendre, options (les mêmes que le menu principal), quitter la partie. La partie continue pendant qu'il est ouvert (tranché le 2026-10-12 : pas de « quitter le jeu » ici). **Les Options s'appliquent avec un bouton Appliquer** (2026-10-27, l'utilisateur) : rien ne change avant ; en sortant avec des réglages en attente, une fenêtre propose de les appliquer ou de les ignorer. Ce qui se dit oui ou non se coche ; la résolution ne propose que les tailles courantes, au format de l'écran.

**Apprentissage** : un tutoriel, sous la forme d'une arène avec des cibles en carton et des étapes scriptées, plus un stand de tir.

*Le stand de tir existe depuis le 2026-10-16 (mannequins à plusieurs distances, fenêtre des armes et des accessoires), avec **la Fosse**, demandée par l'utilisateur sur le modèle de « The Pit » de Modern Warfare 2 : un parcours chronométré où des cibles se lèvent sur le trajet, hostiles à neutraliser et civils à épargner. Comme l'original : +2 s par hostile manqué, +2 s par civil touché, 0,05 s de moins par point de précision, un record par arme. Les cibles ont la vie d'un robot (le parcours est aussi un essai des armes) ; « une touche suffit » les rend fragiles comme le carton.* *Depuis le 2026-10-27, une seule entrée du menu, « Stand de tir et Fosse » (les deux sont dans le même lieu, derrière une porte) ; Échap y ouvre une pause avec les Options, B la fenêtre des armes, et un mannequin neutralisé tombe en arrière, puis se relève.* *Le 2026-10-28 (l'utilisateur : « la même ambiance que l'arène », la Fosse « plus complexe et plus habillée ») : le stand et la Fosse sont deux salles du complexe, la salle B-02 (resserrée le même jour à cinq postes de tir sous un auvent, un pare-balles au fond, quatorze mannequins de 5 à 60 m d'un mur à l'autre) et la salle B-03, un centre d'entraînement rapproché. La Fosse a douze sections, de la cour d'essai au couloir du retour, par un atelier, des bureaux, un toit et une passerelle ; une gaine à passer accroupi ; 33 hostiles et 14 civils ; ses records repartent de zéro.* *À gauche du poste de tir, la salle de précision (B-04), d'après le mode « shooting range » de Rainbow Six : une grande cible ronde, presque aussi large que la salle (3,8 m), que le pupitre du comptoir envoie de 5 à 50 m et dont il efface les impacts ; chaque impact s'y marque et compte ses points (10 au centre), et un écran holographique, posé sur le comptoir, la montre de près.*

**Réglages** : les classiques d'un FPS — touches personnalisables, sensibilité, FOV, réticule personnalisable, graphismes, son.

**Le flow des écrans** : écran titre → menu (jouer, paramètres…) → choix du mode, avec ses réglages à côté → lobby → duel → écran de victoire et résultats, où les duellistes choisissent **la revanche ou le retour au salon** (§ 5).

**Le salon** aura une discussion écrite (voulue le 2026-10-25 ; plus tard s'il faut payer un service).

**L'IA dans l'armurerie** reste factuelle : chiffres et nombre de tirs pour neutraliser un robot, sans pique (2026-10-25).

**L'armurerie** (2026-10-26, avec la refonte des armes) : à gauche, la liste des douze armes, rangée par famille (trois vignettes sous chaque nom) ; une étoile marque les favorites, trois au plus, montrées en grand pendant la préparation (§ 6) ; chaque arme garde ses accessoires et sa finition. **La fiche de l'arme** montre les barres comparées aux douze armes, la phrase de l'IA, la courbe des tirs pour tuer selon la distance, et les chiffres exacts (dégâts, cadence, chargeur, portée).

## 16. Le produit

- **Sortie Steam payante, autour de 5 €** (prix indicatif). Modèle « payé une fois » : pas de free-to-play.
- **PC, clavier-souris uniquement.** Aide à la visée minimale, comme sur Rainbow Six.
- **Cible de performance** : 60 fps.
- **Langues** : français et anglais au minimum, le plus possible à terme. L'anglais viendra plus tard (2026-10-25).
- **Réseau** : à la sortie, mode hôte (Relay), avec des lobbys privés à code seulement. Après la sortie, serveur dédié pour le classé ; budget serveur à évaluer selon les tarifs à ce moment-là.
- **Anti-triche côté client** : avec le classé, après la sortie. Entre amis, l'autorité serveur suffit.
- **Twitch** : abandonné pour l'instant, un bonus possible plus tard.
- **Équipe** : un développeur seul, sans échéance.
- **Assets** : à choisir une fois la DA fixée.

**Playtest.** Le premier jalon : un mini prototype fini, testé par un groupe de 5 ou 6 amis qui travaillent tous dans le jeu vidéo.

**Où on en est** (2026-10-25) : un premier essai de quelques minutes, en réseau local contre un vrai adversaire, aux retours très encourageants ; l'utilisateur en fera un compte rendu détaillé. Des tests plus poussés viendront d'ici une à deux semaines, **sur le blocking**, précédés d'un essai à deux par le code, pour mesurer les connexions. **Le vrai playtest viendra ensuite, sur la première vraie arène** (§ 10). Ce qu'il dira décidera de la suite.

**Le périmètre de ce prototype** (validé le 2026-10-02) : le mode classique, la rotation roi de la colline, une tribune où les spectateurs regardent, et un lobby privé avec code. Quatre joueurs à la fois : deux duellistes, deux spectateurs. Si le groupe de testeurs est plus grand, les autres attendent leur tour hors du jeu (2026-10-04). Les interactions des spectateurs (cailloux, dons, bagarre) devaient arriver après ce premier test, qui doit d'abord dire si le duel lui-même tient. *Avancées le 2026-10-13 à la demande de l'utilisateur : elles sont en place, et l'hôte peut les couper.* Objectif : remonter les bugs techniques, et faire naître d'autres idées de règles. Leurs avis seront recueillis en discussion libre, avec prise de notes. Plus tard, avec plus de monde : un Discord, et des questionnaires.

**Public cible** : des groupes d'amis qui veulent tester leur skill et passer une soirée ensemble ; amateurs de FPS skill-based et de duels rapides. Le jeu ne cherche pas nécessairement à devenir une discipline esport : le classé est une extension, après la sortie (2026-10-04).

## 17. Questions encore ouvertes

*Déjà tranché : les tensions du premier questionnaire (2026-10-02), les arbitrages entre la bible et le GDD et les points en suspens (2026-10-04), puis la revue du 2026-10-25 et sa suite, dont chaque décision porte sa date dans ce document.*

**En attente de l'utilisateur** :
- **Le compte rendu du premier essai** (§ 16).
- **Les viseurs grossissants** : sur quelles armes (§ 6). Claude propose une lunette compacte ×2,5 sur le FP-30 (2026-10-26).
- **Une animation de reptation de côté** (§ 7) : l'utilisateur la cherche ; Claude peut essayer de la faire dans Blender.

**Claude propose, l'utilisateur choisit** :
- **Le nom du jeu** : « Last Unit Standing » a de l'idée, « mais c'est pas encore ça » (2026-10-25).
- **Le nom de l'IA** : les huit propositions du 2026-10-25 n'ont pas convaincu (SENTINELLE cochée, mais « je n'aime pas trop les propositions »).
- **Le rendu** (§ 13) : les essais sont prêts (2026-10-26), sur le Y Bot et sur le CT-01 ; l'utilisateur choisit le style. Le robot est choisi : le CT-01, avec le prototype et l'OS-02 au choix du joueur (2026-10-26).

- **Les chiffres de départ des douze armes** (§ 6, proposés le 2026-10-26) : l'utilisateur les corrigera dans l'éditeur des armes.

**À confirmer** (relevé le 2026-10-26, avec la refonte des armes) :
- **Les armes de la tribune** (§ 9) : seulement les armes insolites, quand elles existeront. D'ici là, plus aucune arme ne tombe-t-elle de la passerelle ?
- **La surchauffe** (§ 6) : une arme laser surchauffée se vide-t-elle d'elle-même (proposition de Claude), ou faut-il appuyer sur la touche de rechargement ?
- **Le tir chargé du FX-2** (§ 6) : un tir chauffe-t-il autant chargé ou non (proposition de Claude) ? Sinon, des tirs lâchés tôt tireraient plus longtemps avant la surchauffe.

**À trancher** (relevé le 2026-10-25) :
- **La glissade** (§ 7) : envisagée par l'utilisateur pour la fluidité et la nervosité, elle contredit « pas de mouvement à momentum ».

**À préciser** (relevé le 2026-10-25) :
- **Le mode Zone** (§ 11) : capturer une zone fixe. Comment se gagne la manche, et qu'est-ce qui le distingue de Capture (même idée dans le nom, jamais défini) et de Hack & Defend (un point à prendre) ?
- **Le défi propre à chaque arme** (§ 14) : il devait débloquer « la dernière » finition, mais les cinq ont déjà leur défi. Remplace-t-il celui de la dernière de la série, ou débloque-t-il une septième finition, de maîtrise, comme l'or de *Call of Duty* ?

**À observer en playtest** :
- **Les manches passives**, maintenant qu'il n'y a plus de limite de temps (§ 5).

**Toujours ouvert** :
- Les multiplicateurs des headshots et le TTK qui en découle, posés pour les douze armes le 2026-10-26 (§ 6) : à juger en jouant, au stand de tir et dans l'éditeur.
- Le tir à la hanche façon *Rainbow Six* (§ 6, 2026-10-16) : la taille des cônes, l'ouverture en rafale et en mouvement, à juger en jouant.
- L'ordre dans lequel faire les modes de la sortie.
- La musique et les bruitages de l'interface, à remplacer (§ 8, secondaire).
- Ce qui relève du lore et reste ouvert est listé dans la bible (§ 18 de la bible) : le degré d'autonomie des robots, ce qu'ils ressentent, le niveau de dégradation, le style de rendu final.
