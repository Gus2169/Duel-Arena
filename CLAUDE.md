# FPS Claude — Duel Arena

FPS de **duel 1v1** en Unity 6 (6000.6.0f1), URP, multijoueur via Netcode for GameObjects 2.13.2.

## Comment ce projet se pilote

**Deux documents de design font autorité, chacun sur son terrain** (tranché le 2026-10-04) :
- **`Docs/GDD-Duel-Arena.md` pour les règles du jeu** — la vision, les piliers, les règles non négociables, ce que le jeu refuse d'être. C'est l'âme du jeu : à consulter avant toute décision qui touche au ressenti, à l'équilibrage ou au contenu.
- **`Docs/Bible-Lore-DA.md` pour le monde et l'apparence** — lore, personnages, armes, arènes, ton, direction artistique. C'est désormais la version de référence de la bible : le PDF d'origine de l'utilisateur en est la base, et ses écarts sont listés dans son journal.

En cas de conflit sur une règle de jeu, **le GDD gagne**. Les deux se mettent à jour quand une décision de design est prise.

**Pour tout le reste — architecture, technique, priorités, dettes — ce fichier est la seule source de vérité**, et c'est Claude qui en est le juge. Concrètement :

- Claude tranche les choix d'architecture et de séquencement technique, sans demander d'arbitrage sur des points qui n'engagent pas le design.
- Claude tient cette page à jour lui-même : un incrément livré, une dette découverte, une décision d'archi prise → ça s'écrit ici, dans la même session.
- Ce qui est écrit ici a été **vérifié dans le code**, pas recopié d'un document. En cas de doute, le code gagne et la page se corrige.
- Ce qui remonte à l'utilisateur : les arbitrages de **ressenti** (est-ce que ça se joue bien ?), de **contenu**, et les actions destructives.

Aucun autre document technique n'est maintenu. Il y en a eu un, décrivant comme livrés des correctifs absents du dépôt — la leçon est intégrée : **une doc technique qui n'est pas vérifiée contre le code devient un piège.**

## Outils

- **Piloter l'Editor : les outils MCP `unity-editor-mcp`** (vérifié le 2026-09-24). Même backend que le CLI (package Pipeline, Editor déjà ouvert sur ce projet), mais en appel direct plutôt qu'en aller-retour shell. Épinglé sur ce projet : `unity mcp --project-path "D:\Documents\Unity\FPS Claude"`, déclaré dans `~/.claude.json`.
  Le CLI `unity` reste la porte d'entrée pour tout ce que MCP n'expose pas : `unity install/open/editors`, `unity pipeline`, `unity skill`, `unity mcp configure`. Équivalence : outil MCP `<nom>` = `unity command <nom> --project-path "..."`.
  Cycle de vérification standard après une modif de script : `recompile`, puis `recompile_status` en boucle, puis `console_status`. Préférer ces outils à l'édition manuelle de `.unity`/`.prefab` (YAML) quand l'Editor est ouvert.
  ⚠️ Le buffer de `console` garde les entrées des compilations précédentes. Se fier à `groundTruth` / `recompile_status`, pas au comptage brut.
  Un second serveur MCP existait (`unity-mcp`, le pont in-Editor du package AI Assistant via `relay_win.exe`) : **supprimé le 2026-09-24**, il faisait doublon et n'était épinglé à aucun projet — il s'attachait à l'Editor qui tournait, piège assuré le jour où deux Editors tournent en parallèle. Le pont reste activable dans Project Settings > AI > Unity MCP si un besoin apparaît (son seul apport unique était la génération d'assets par IA, qui demande un abonnement Unity AI absent ici).
- **Git** : remote `origin` = dépôt GitHub **privé** `https://github.com/Gus2169/Duel-Arena.git` (ajouté le 2026-10-05, branche `master`, avec LFS). **Committer seulement sur demande explicite**, et pousser de même.

## Architecture réseau

Topologie **client-serveur autoritaire** (pas la « distributed authority » d'Unity 6 : on veut une source de vérité unique pour les dégâts). Position, yaw et posture sont gérés à la main via `NetworkVariable` plutôt que `NetworkTransform`, précisément pour permettre la prédiction et l'interpolation.

Le mouvement, la posture et le tir suivent tous le même **pattern à 4 cas** selon `IsOwner`/`IsServer`, dans `PlayerLocomotion.cs` et `WeaponController.cs` :

1. `IsOwner && IsServer` (Host sur son propre perso) → autorité directe, pas de prédiction.
2. `IsOwner && !IsServer` (client distant sur son perso) → prédiction locale + `ServerRpc` + réconciliation par rejeu. **`Move()` doit rester STRICTEMENT déterministe** entre prédiction, traitement serveur et rejeu — toute divergence casse la réconciliation.
3. `!IsOwner && IsServer` (serveur sur le perso d'un autre) → seule source de vérité, applique `serverInputQueue`.
4. `!IsOwner && !IsServer` (spectateur) → interpolation par historique (`remoteSnapshots`), jamais de logique de mouvement.

La posture est **confirm-only, pas prédite** (compromis assumé, à revoir si ça se sent mou).

### Les 3 catégories — à respecter pour toute nouvelle mécanique joueur

- **(A) État simulé/autoritaire**, réseauté via le pattern à 4 cas : position, yaw, vitesses, gravité, sol, posture.
- **(B) Événements sonores de gameplay** (`OnPlayerSound`), diffusés à tous par ClientRpc — décidés côté serveur (pas, ramper, posture, relevé auto) ou demandés par le client avec garde-fou (lean uniquement).
- **(C) Cosmétique pur**, ne tourne que si `IsOwner` : head bob, kick caméra à l'atterrissage, offset visuel du lean.

Le yaw est en A et non en C : il affecte la direction de déplacement ET de tir, donc c'est du gameplay, pas de la caméra.

### Le lean n'est plus cosmétique (2026-09-24)

Conséquence directe du hitbox : le lean **déplace la surface touchable**, donc le serveur doit le connaître. Il a quitté la catégorie C pure — c'est désormais **A pour son décalage, B pour son son, C pour l'inclinaison caméra**.

- L'**état** (-1/0/+1) voyage dans le snapshot d'input existant — aucune RPC ajoutée. Un client ne peut mentir que sur son propre lean, ce qui ne lui donne aucun avantage (se pencher expose son flanc).
- Le **décalage** est calculé par le serveur (`ServerAdvanceLean`, avec le même anti-clipping que le client) et publié dans la pose datée (`networkPose`, voir « L'affichage des adversaires »).
- Le **propriétaire** utilise sa valeur locale prédite (`LeanOffset`), pour que sa caméra et son corps n'aient aucune latence.

Chez un spectateur, le lean d'un adversaire est affiché en retard, **au même instant que sa position** (ils voyagent dans la même pose datée). **Le rewind le compense** : l'historique serveur porte le décalage de lean au même titre que la position (voir plus bas).

### Hit registration

Le tireur raycast en local pour son feedback visuel instantané (**aucun dégât associé**), envoie `origin`/`direction` au serveur qui refait SEUL le raycast et décide SEUL des dégâts. `Health` est un `NetworkBehaviour` dont `Current` est une `NetworkVariable<float>` en écriture serveur uniquement (avec un mode de secours non-réseauté pour une cible de test isolée) ; `ApplyDamage` refuse tout appel client direct sur un objet réseauté.

**Résolution en DEUX traces** (`WeaponController.ResolveShot`, partagée entre le feedback local et la décision serveur, pour qu'elles ne puissent pas diverger) :

1. **Le monde** (`WeaponData.worldMask` = `Default`), qui arrête les balles. **Sans les joueurs** : leur `CharacterController` ne suit pas le lean, donc le laisser bloquer les tirs ferait réapparaître par la bande le trou que le hitbox ferme.
2. **Les hitbox** (`WeaponData.hitboxMask` = layer `Hitbox`), limitée à la distance du mur touché — c'est ce qui fait qu'un adversaire derrière une caisse ne prend rien.

Le tireur retire son propre hitbox de la requête le temps du tir (`try/finally` — un hitbox laissé désactivé rendrait le tireur invulnérable pour le reste de la partie).

### Compensation de latence — le rewind (2026-09-24)

Le serveur valide chaque tir contre l'état des adversaires **au moment où le tireur a réellement visé**, pas au moment où la RPC arrive. Sans ça, un tireur qui vise juste rate une cible en mouvement : l'écart vaut à peu près `(RTT/2 + délai d'interpolation) × vitesse`, soit plus d'un mètre à 150 ms de ping et 8 m/s — largement la largeur d'un joueur.

**L'instant exact, plus d'estimation (2026-10-06).** Les adversaires sont affichés à partir de poses **datées par le serveur** sur son horloge (`NetworkManager.ServerTime`), voir « L'affichage des adversaires ». Le tireur sait donc exactement quel instant serveur il a à l'écran (`PlayerLocomotion.ViewServerTime`) et l'envoie avec son tir ; le serveur rewind à cet instant. **0 pour le Host**, qui voit les autres joueurs au présent de sa propre simulation (cas 3).

Avant, le tireur estimait l'âge de sa vue à `RTT/2 + interpolationDelay`, à partir d'un RTT chronométré sur l'aller-retour prédiction/réconciliation. L'estimation **oubliait les 50 ms de marge** (`ServerBufferSec`) que Netcode garde sur son horloge serveur côté client : une fois l'affichage passé sur cette horloge, le rewind aurait sous-compensé d'autant (25 cm sur une cible à 5 m/s). Le RTT lissé (`smoothedRtt`) ne sert plus qu'au diagnostic.

**Le client suggère, le serveur clampe.** `viewServerTime` est le seul paramètre « libre » accepté par `FireServerRpc`. Le recul qui en découle (`ServerTime − viewServerTime`) est borné à `[0 ; maxRewindSeconds]` (0,3 s — couvre ~400 ms de ping légitime) ; un instant dans le futur est ramené au présent. Sans ce clamp, un client modifié annoncerait un instant très ancien pour tuer ses adversaires là où ils étaient il y a une éternité. Au-delà de cette borne, c'est la **victime** qui subit l'injustice, en mourant à couvert.

**L'historique porte la pose COMPLÈTE**, datée sur l'horloge du serveur : position, yaw, décalage de lean et posture **avec sa transition** (`HitboxPose`, fenêtre glissante d'une seconde, alimentée à chaque frame serveur — même sans input traité, sinon l'historique aurait des trous pendant les micro-coupures, précisément quand le rewind sert le plus). Historiser la seule position ne corrigerait qu'une part du décalage : un adversaire penché ou accroupi au moment du tir serait rewind avec la géométrie qu'il a *maintenant*. La posture ne s'interpole pas : elle porte l'instant exact de son changement, l'échantillonnage prend celle qui était en vigueur à l'instant visé, et la transition se recalcule à cet instant (`ApplyHitboxAt`) — un adversaire surpris à mi-chemin d'un plongeon au sol est replacé à mi-chemin.

Vérifié en Play Mode le 2026-10-06 : sur 300 frames, la pose publiée et la dernière entrée de l'historique ont **exactement** la même date et la même position — les deux côtés interpolent les mêmes échantillons sur la même horloge.

**Validé à deux instances le 2026-10-06**, avec le banc de test (Host en va-et-vient, client qui tire en visée automatique sur le torse affiché) : distance du rayon au corps rembobiné **0,00 m**, au corps actuel **0,45 m** sans simulateur (rewind de 100 ms) et **1,10 m** sous le simulateur (rewind de 260 ms, 290 au plus). Le rewind replace exactement la cible là où le tireur la voyait.

🚨 **Le tir annonce l'instant RÉELLEMENT affiché** (`shownViewTime`, la date de la dernière interpolation), pas un instant recalculé au moment du tir. La première mesure laissait 5 à 6 cm d'écart : selon l'ordre d'exécution des scripts, le tir part avant ou après que les adversaires soient replacés pour la frame, et annoncer l'instant de la frame en cours décalait le rewind d'une frame (jusqu'à 8 cm à 60 images/s sur une cible qui court). Le reste de l'écart venait du banc lui-même, qui visait `Collider.bounds` : avec `autoSyncTransforms` à false, les bounds ne suivent le transform qu'au pas de physique suivant.

**Seuls les hitbox voyagent dans le passé**, jamais les joueurs : la simulation continue sur les vraies positions, et tout se déroule de façon synchrone dans le handler de la RPC, donc invisible pour le reste du jeu. C'est exactement ce que le hitbox séparé rend possible.

🚨 **`Physics.SyncTransforms()` est obligatoire** avant ET après le déplacement des hitbox. `Physics.autoSyncTransforms` vaut **false** par défaut (vérifié dans ce projet) : déplacer un transform ne met pas à jour la scène physique utilisée par les requêtes. Sans cette synchronisation, le raycast verrait les hitbox à leur position actuelle et **tout le rewind serait silencieusement sans effet** — le pire des échecs, puisqu'il ne se voit pas.

Contrainte de configuration : `maxRewindSeconds` (sur `WeaponController`) doit rester **inférieur** à `hitboxHistoryDuration` (sur `PlayerLocomotion`), sinon on demande à l'historique une pose qu'il a déjà jetée.

### L'affichage des adversaires : des poses datées par le serveur (2026-10-06)

**Le symptôme**, rapporté par l'utilisateur : chez le client, l'adversaire « saccade », animations comprises. **Mesuré** avec `[DIAG-ANIM]` (voir « Tester sous latence ») : aucun état d'animation relancé, aucun drapeau qui clignote — mais une **position qui avançait par bonds**, jusqu'à 14 cm en une frame pour 2,5 cm en moyenne. Les poses étaient datées **à leur arrivée** chez le spectateur : la gigue du réseau se lisait comme des variations de vitesse. La rotation, elle, n'était pas interpolée du tout (crans de 33 ms), et le lean comme les drapeaux d'animation étaient appliqués dès réception, 100 ms avant la position qu'ils accompagnent.

**La forme retenue.** Le serveur publie à chaque frame une `DisplayPose` (`networkPose`) : **date sur son horloge** (`NetworkManager.ServerTime`), position, yaw, lean, drapeaux d'animation (au sol, en franchissement, en visée), et un compteur de téléportation qui vide l'historique du spectateur à chaque nouvelle manche. Le spectateur interpole à `ServerTime − interpolationDelay` (`SampleDisplayPose`, fonction pure testée) : position et lean en ligne droite, rotation par le plus court chemin, drapeaux pris au début de l'intervalle, et **jamais d'extrapolation**. Deux variables réseau (la pose, la posture) remplacent les sept d'avant.

**Le budget de retard.** Côté client, l'horloge serveur de Netcode garde déjà 50 ms de marge (`ServerBufferSec`) derrière les données qui arrivent. `interpolationDelay` passe donc à **0,05 s** : 100 ms de réserve au total, trois poses à 30 par seconde, soit le même retard d'affichage qu'avant. `[DIAG-ANIM] affamees=` compte les frames où l'instant affiché dépassait la dernière pose reçue (doit rester à 0, sinon le délai est trop court), et `reserve=` l'avance moyenne de la dernière pose reçue sur l'instant affiché. **Mesuré le 2026-10-06 sous le simulateur réseau : 0 frame affamée, réserve de 74 ms** (les 100 ms prévues, moins l'attente moyenne d'un tick). Le retard d'affichage total vaut donc le trajet réel des données plus ~75 ms : notre interpolation n'en ajoute pas d'autre.

**La pose part à chaque tick** (sa date change à chaque frame), environ 1 Ko/s par joueur. C'est voulu : un spectateur a besoin de poses régulières même quand le joueur est immobile, sinon il interpolerait un départ sur plusieurs secondes.

⚠️ **Limite connue, non traitée** (dette n° 30) : le serveur **simule** un client distant par à-coups (cas 3), au rythme où ses inputs arrivent. Mesuré sous le simulateur réseau : chez le Host, le personnage du client était immobile une frame sur trois et sautait de 20 cm. Les poses datées n'y changent rien, puisque c'est la simulation elle-même qui avance par bonds. Aujourd'hui, seul le Host voit ce défaut ; avec un serveur dédié, **tous** les joueurs seraient en cas 3 et tous les spectateurs le verraient.

### Le hitbox par zones et le lean façon Rainbow Six (2026-10-05)

`PlayerHitbox` (enfant `Hitbox` du prefab joueur) crée dans `Awake()` **quatre colliders triggers** sur le layer **`Hitbox` (7)**, chacun étiqueté par un `PlayerHitboxZone` : `Head` (sphère), `Torso`, `Legs` et `Legs2` (capsules ; la seconde jambe ne sert qu'allongé). Trigger, layer et structure sont forcés dans le code, pas laissés à l'inspecteur : ce sont des invariants, et ils se règlent silencieusement mal en un clic.

**La géométrie est calculée par `BodyLayout`, fonction pure et statique**, à partir de (posture réseau, œil de la posture, décalage de lean). Aucune interpolation locale : surface identique sur toutes les machines. Les données par posture (`StanceBody` : pivot du lean, jambes, torse, rayon de tête) sont sérialisées sur `PlayerHitbox` ; l'œil vit dans `PlayerLocomotion.StanceProfile` (`cameraHeight`, `cameraForward`, `cameraSide`), **une seule source** pour la caméra et le centre de la tête touchable : on voit depuis l'endroit où l'on est exposé.

**Le lean R6** : le torse et la tête tournent autour de la base de la colonne (l'os `Spine`), toujours autour de l'**axe avant** du joueur ; les jambes ne bougent jamais. Debout et accroupi, le buste **s'incline** ; allongé, il **roule sur lui-même** (demandé par l'utilisateur le 2026-10-06 : la première version faisait pivoter le buste à plat, autour de la verticale, ce qui se lisait comme un glissement de côté). Le scalaire réseauté n'a **pas** changé — c'est toujours le décalage latéral de l'œil, en mètres — donc le réseau, l'historique du rewind et l'anti-clipping n'ont pas bougé. L'angle s'en déduit exactement (`BodyLayout.LeanAngle`), même quand l'œil n'est pas dans l'axe du pivot. L'amplitude est bornée par `maxLeanOffset` (0,35 m) **et** par un angle de buste maximal (`maxLeanAngle`, 40°) : accroupi, le buste est plus court et sort donc moins (31,5 cm) ; allongé, la tête est à 19 cm au-dessus de l'axe et ne sort que de **15 cm à droite et 9,4 cm à gauche**.

**Le roulis de la caméra suit l'angle du buste** (`maxLeanTilt`, 10° dans le prefab, à fond de lean dans toutes les postures), et non plus le décalage : allongé, c'est l'essentiel de l'effet.

**Le mouvement du buste est amorti** (2026-10-06, `StepLean`, `leanSmoothTime` 0,06 s) : départ et arrivée en douceur, sans rebond, en s'inclinant comme en se redressant. Mesuré en jeu : 90 % en ~110 ms, immobile vers 200 ms. Avant, il avançait à vitesse constante (7 m/s, 35 cm en 50 ms) et partait puis s'arrêtait net — « brut et sec ». C'est aussi la vitesse à laquelle on s'expose en peekant : réglage de ressenti **et** d'équilibrage. Gardé par `LeanMotionTests`, validé par mutation (l'ancien mouvement fait 11,7 cm dès la première image).

**L'affichage suit le même scalaire** : `PlayerAnimator.ApplyVisualLean` tourne l'os `Spine` de l'angle calculé par `BodyLayout`, après l'évaluation de l'Animator. La caméra du propriétaire va sur l'œil penché (`PlayerLocomotion.LeanCameraOffset`, appliqué par `PlayerCameraLook` **après** le pitch, dans le repère du joueur). Mesuré en Play Mode le 2026-10-05, écart entre le crâne visible du robot et la tête touchable penchée : **1,5 à 2,8 cm debout, 5,5 cm accroupi**, et caméra exactement au centre de la tête. Allongé en roulis (2026-10-06) : **1,2 cm à droite, 0,7 cm à gauche**, caméra au centre de la tête.

🚨 **Règle à ne pas enfreindre : ne JAMAIS dériver le hitbox des os animés.** Un Animator n'est pas déterministe entre machines. **L'animation AFFICHE le lean ; le hitbox se CALCULE à partir du même scalaire réseauté.** Les deux lisent la même source, aucun ne lit l'autre.

⚠️ **L'Animator du modèle doit rester en `AlwaysAnimate`.** La rotation du buste s'ajoute à la pose de chaque frame ; si l'Animator cessait d'évaluer (modèle hors champ), elle s'accumulerait et le buste tournerait sur lui-même.

⚠️ **Allongé, la pose de Mixamo est asymétrique** (tête penchée 15 cm à gauche sur la crosse, jambe droite repliée) et la surface touchable la suit : œil à (-0,15 ; 0,36 ; 0,25), seconde capsule de jambe, et un lean plus court à gauche (9,4 cm) qu'à droite (15 cm) : en roulant à gauche, la tête descend vers le sol. Le modèle est reculé de 0,30 m allongé (`PlayerAnimator.proneModelOffset`) pour que la tête et la caméra restent **à l'intérieur du `CharacterController`** — sinon la caméra traversait un mur face auquel on est allongé. `BodyLayoutTests.LOeil_ResteALInterieurDuColliderDeMouvement` le verrouille.

⚠️ **Les yeux ont bougé** pour coïncider avec la tête du robot : accroupi 0,95 → **1,05 m** (et 10 cm en avant), allongé 0,35 → 0,36 m, 25 cm en avant et 15 cm à gauche. Debout inchangé (1,65 m). Accroupi validé en jouant le 2026-10-06 (« c'est ok »), mais avec un robot qui flottait de 8 cm : depuis la correction des pieds au sol (voir les pièges), **toutes les vues sont 8 cm plus basses** qu'à ce test (23 cm allongé). Ce sont les hauteurs prévues, rejugées bonnes en jouant le même jour (« tout est okay »).

🚨 **Les valeurs du prefab priment sur les valeurs par défaut du code.** Une fois `PlayerHitbox` enregistré, changer `BodyLayout.DefaultProne` ne change RIEN en jeu. Vécu le 2026-10-05 : la pose allongée remesurée dans le code, le prefab gardait l'ancienne. D'où `BodyLayoutTests`, qui lit les données **du prefab**, jamais les défauts du code.

Ce que la séparation du hitbox débloquait reste vrai : le `CharacterController` peut être désactivé (vault) sans que le joueur cesse d'être touchable, et le rewind déplace la racine `Hitbox` dans le passé — ses zones suivent.

**Les zones n'influent pas encore sur les dégâts** : le serveur journalise la zone touchée (`[Serveur] ... touché (Head)`), les multiplicateurs restent une décision de design à trancher par playtest (GDD § 6).

### Les transitions de posture (2026-10-06)

Demandé par l'utilisateur : « le changement de position est trop instantané ». La caméra glissait en moins de 0,1 s et la surface touchable changeait de posture d'un coup ; le corps visible faisait un fondu de 0,2 s.

**Durées** (réglages de ressenti, sur `PlayerLocomotion`) : `standCrouchTransition` **0,3 s**, `crouchProneTransition` **0,65 s**, `standProneTransition` **0,9 s**. Le corps visible, la caméra, la surface touchable et la vitesse de déplacement passent **ensemble** d'une posture à l'autre sur cette durée, avec un départ et une arrivée adoucis (smoothstep, `TransitionProgress`).

**Une seule source : `StanceState`** (posture d'arrivée, posture de départ, instant du changement sur l'horloge du serveur), qui remplace la posture seule dans la `NetworkVariable`. Chaque machine calcule la progression pour l'instant qu'**elle** considère (`ResolveStance`, fonction pure testée) : le présent pour le serveur, l'instant interpolé pour un spectateur (l'adversaire se couche au moment où on le voit arriver là), un instant passé pour le rewind.

**Une seule transition à la fois, côté serveur.** Une demande arrivée pendant une transition est mise en attente et s'applique à sa fin (`ServerRequestStance` / `ServerUpdateStance`). C'est aussi la borne de débit qui manquait à `RequestStanceChangeServerRpc` (dette n° 14) : au plus un changement par transition, au moins 0,3 s, en temps réel.

**Ce qui suit la transition :**
- **La surface touchable** : chaque zone glisse de sa forme de départ à sa forme d'arrivée (`BodyLayout.ComputeBlended`) ; la seconde jambe naît de la première. Interpolée, elle n'épouse pas chaque image du clip, mais reste à quelques centimètres du corps au lieu d'en être à un mètre.
- **La caméra** du propriétaire : l'œil glisse de la même façon.
- **La vitesse** (dans `Move()`) : multiplicateur de posture interpolé. Sans ça, se relever rendait la vitesse de marche d'un coup, corps encore à genoux. **Instant de simulation** : `Move(snap, dt, simTime)`, avec `NetworkManager.LocalTime` chez le client qui prédit (l'instant où le serveur traitera l'input), l'horloge du serveur chez le serveur — **jamais un instant fourni par le client**, qui sauterait sinon la fin d'une transition. Rejoué tel quel à la réconciliation (`PendingInput.simTime`).
- **Le vault** est refusé pendant une transition.
- **L'animation** : vers ou depuis l'allongé, deux états de transition, `GenouVersAllonge` (clip `Rifle Kneel To Prone`) et `AllongeVersGenou` (`Rifle Prone To Kneel`), lus à la vitesse `PostureSpeed` qui fait tenir leur partie utile (1,55 s, mesurée) dans `crouchProneTransition`. Debout ↔ allongé ajoute un genou posé en fondu de 0,25 s. Le **haut du corps s'efface** pendant ces clips (sinon buste droit, arme à l'épaule, sur des jambes qui se couchent) et revient à la fin. Debout ↔ accroupi reste un fondu de 0,3 s, buste armé. Le modèle recule vers son décalage allongé au même rythme.

**Ce qui ne la suit PAS** : la capsule de collision, toujours instantanée sur la posture d'arrivée (déterminisme, voir dette n° 4).

Vérifié en Play Mode : debout → allongé, la caméra descend de 1,65 à 0,36 m en 0,9 s, le modèle recule de 0 à −0,30 m, le buste s'efface puis revient, l'Animator passe par `GenouVersAllonge` et finit dans `Prone` à 0,91 s.

🚨 **Les quatre clips de transition (genou, debout, allongé) sont importés avec la hauteur cuite dans la pose** (`lockRootHeightY = true`, `keepOriginalPositionY = true`), **contrairement aux autres clips**. Sans ça, leur descente verticale est du root motion, que `applyRootMotion = false` jette : le corps restait à mi-hauteur pendant tout le geste (mesuré : hanches à 0,88 m en fin de « debout → genou », au lieu de 0,39). C'est le pendant vertical du piège « Bake Into Pose » décrit plus bas : pour un clip **sur place**, la hauteur doit rester dans la pose.

⚠️ **Limites connues** : les clips Mixamo finissent **genou à terre**, alors que la posture accroupie est un squat ; le fondu de sortie masque l'écart. Et la posture reste **confirmée par le serveur, pas prédite** : chez le propriétaire, une transition commence un RTT après l'appui.

### Ragdoll et hitbox par zone — position tranchée (2026-09-24)

**Le ragdoll Unity n'est PAS le bon outil pour les hitbox.** Piste évaluée et écartée, pour quatre raisons :

1. Ses colliders sont **accrochés aux os animés** — exactement ce que la règle ci-dessus interdit.
2. Il ajoute **un Rigidbody par os** (une dizaine par joueur) plus des `CharacterJoint`. Du coût physique permanent pour un besoin qui n'est que de la requête de raycast.
3. Ses colliders sont **non-trigger par nature** (ils doivent heurter le monde en ragdollant), donc ils perturberaient la collision de mouvement — précisément ce que le hitbox trigger évite.
4. Le **rewind** devrait historiser une dizaine de transforms par joueur et par tick au lieu de trois scalaires.

**En revanche il a deux usages légitimes** : la mort physique (le GDD veut des impacts exagérés), et une **source de proportions** au moment d'auteur la géométrie des zones — le Ragdoll Wizard place des volumes crédibles qu'on peut relever pour dimensionner les capsules à la main.

La forme retenue : des hitbox **calculés** à partir des scalaires réseautés (posture, lean, et plus tard le pitch de visée), en triggers sur le layer `Hitbox` ; le ragdoll activé **uniquement à la mort**, hitbox désactivés à ce moment-là.

Note : les FPS AAA utilisent bien des hitbox accrochés aux os, mais au prix d'une simulation d'animation côté serveur — c'est la source d'une grande partie des bugs de hit registration de CS:GO. Pour un duel 1v1 avec une poignée de poses, calculer les zones depuis quelques scalaires est plus simple, déterministe par construction, et largement suffisant.

**Multiplicateurs de dégâts par zone (tête/torse/jambes)** : techniquement trivial une fois les zones là (un multiplicateur par collider, appliqué côté serveur, aucune surface de triche). Deux conditions de séquencement, en revanche :
- **Après le vrai modèle de personnage.** Sur une capsule lisse, le joueur ne peut pas *voir* où est la tête : viser deviendrait une devinette, ce qui contredit le pilier « lisibilité » du GDD.
- **Après le rewind.** Une petite zone à fort gain est exactement là où la compensation de latence manque le plus. Livrer les multiplicateurs avant rendrait le hit registration *moins* satisfaisant, pas plus.

L'ampleur des multiplicateurs est une **décision de design** : elle touche directement le TTK de 0,7 s (une tête à ×2 le ferait tomber à 3 balles). À trancher par playtest, pas par calcul.

### Garde-fous serveur

- `WeaponController.FireServerRpc` rejette : un tir plus rapide que `data.shotsPerSecond` (tolérance 15 %), une direction nulle, et une **origine trop éloignée de la position serveur du tireur** (`maxOriginDistanceFromPlayer`, 4 m — couvre hauteur caméra + lean + avance de prédiction sous latence. Le rewind est en place et l'historique serveur couvre aussi le tireur, mais la validation de l'origine ne s'en sert pas encore : c'est ce qui permettrait de resserrer la borne).
- `PlayerLocomotion.ApplyBufferedServerInputs` clampe le `deltaTime` de chaque input et **budgète le temps simulé sur le temps RÉEL** (token bucket rechargé de `Time.deltaTime × 1.1`, réserve plafonnée à 0,25 s). `SubmitInputServerRpc` plafonne la taille de `serverInputQueue`.

**Deux règles qui en découlent, à appliquer à toute nouvelle RPC client→serveur :**

1. Aucune RPC n'accepte de **résultat** (hit, dégâts, position). Au mieux une *suggestion*, toujours clampée côté serveur.
2. Toute borne anti-abus s'exprime **par unité de temps réel, jamais par frame** — une borne par frame se contourne en gardant la queue pleine, puisque le serveur tourne à ~60 frames/s.

### Tester les garde-fous

Deux interrupteurs de triche simulée dans l'inspecteur, sous le header `Debug — triche simulée`, entourés de `#if UNITY_EDITOR` (impossible à embarquer dans une build) :

- `WeaponController.debugFakeShotOrigin` — décale l'origine envoyée au serveur de 50 m. Attendu : aucun dégât + un warning `[Serveur] Tir rejeté` par tir.
- `PlayerLocomotion.debugFloodServerInputs` — renvoie le même input 10× par frame. Attendu : le joueur n'avance PAS plus vite, il se fait ramener en arrière.

**Les deux ne marchent que depuis une instance CLIENT distante**, jamais le Host : en Host, le perso local passe par le cas 1 et ne traverse ni la queue d'inputs ni la RPC de tir.

## Pièges déjà rencontrés (ne pas les re-découvrir)

- **Valeur RELATIVE/cumulative dans `Move()`** → doit avoir un équivalent « valeur confirmée » renvoyé par le serveur et appliqué AVANT le rejeu des inputs non confirmés, sinon dérive à chaque correction. Déjà arrivé deux fois : le yaw (dérive en double) et `currentVelocity` (désaccord permanent en accel/décel). Vaut pour toute future extension de `Move()`.
- **Comparer une prédiction, c'est comparer à séquence ÉGALE.** Le seuil de réconciliation compare ce que le serveur confirme pour une séquence donnée à ce que le client avait prédit **pour cette même séquence** (`PendingInput.predictedPosition`). Comparer à la position *actuelle* du client n'aurait aucun sens : elle est en avance de tout le RTT, donc l'écart y est toujours grand. Corollaire : après un rejeu, les prédictions stockées doivent être **réécrites** avec les nouvelles valeurs, sinon la correction suivante se base sur des données périmées.
- **Tout script qui lit clavier/souris doit avoir une garde `IsOwner`** en tête d'`Update()`/`LateUpdate()` — sinon chaque instance locale réagit à la souris physique (caméras qui bougent sur 2 écrans, `AudioListener` en surnombre, tirs pour tout le monde). `PlayerInputReader` ne connaît volontairement pas la notion de propriétaire : c'est aux consommateurs de se garder.
- **JAMAIS d'instance du PlayerPrefab posée dans une scène réseau**, même désactivée. Netcode ré-ACTIVE de force les `NetworkObject` in-scene désactivés côté client — c'est écrit noir sur blanc dans `NetworkSpawnManager.cs` (« if it is disabled then enable it so NetworkBehaviours will have their OnNetworkSpawn method invoked »). Symptôme vécu le 2026-09-22 : un joueur fantôme apparaissait sur le CLIENT au moment de sa connexion, au point de spawn, invisible pour le Host et absent de la hiérarchie de l'Editor — avec sa caméra et son `AudioListener`, d'où le warning des 2 audio listeners. **Un joueur se spawne par `NetworkManager.PlayerPrefab`, jamais en le posant dans la scène.** Même méfiance pour tout autre `NetworkObject` in-scene désactivé.
- **La caméra de secours de la scène doit être éteinte au démarrage d'une session.** Deux caméras plein écran actives à la même `depth` = la scène rendue DEUX FOIS par frame (et un `AudioListener` de trop). C'est `NetworkBootstrapUI` qui s'en charge, pas `PlayerLocomotion` : la garde de ce dernier ne voit que les enfants du Player, et cette caméra n'en est pas un. Cause identifiée le 2026-09-22 d'un Host qui ramait par rapport au Client.
- **Warning « N audio listeners in the scene »** : la garde dans `OnNetworkSpawn` ne voit que les enfants du Player. Un `AudioListener` orphelin ailleurs dans la scène ne sera jamais désactivé par ce code — voir les deux points ci-dessus, qui en sont les deux causes déjà rencontrées.
- **Un champ `[SerializeField]` assigné dans l'inspecteur PENDANT le Play Mode n'est jamais sauvegardé.** Symptôme typique : un fix qui marche dans un contexte mais pas l'autre alors que le code est identique. Toujours assigner en mode Édition puis sauvegarder.
- 🚨 **Le `DebugSimulator` d'`UnityTransport` NE FAIT RIEN** en Netcode 2.13.2 — il est marqué `[Obsolete("no longer supported and has no effect")]`. Régler son `Packet Delay Ms` n'a aucun effet : mesuré le 2026-09-28, RTT de 5 ms avec le champ à 75 ms. **Conséquence historique : tous les « tests sous latence » de ce projet antérieurs à cette date se sont en réalité déroulés à ~5 ms de RTT.** Et l'ancien piège documenté ici (« un Packet Delay oublié à 100-150 ms explique un ressenti saccadé ») était faux — ce réglage ne peut rien causer. Voir « Tester sous latence » pour la méthode qui marche.
- **Ressenti « saccadé » en build** : 2 instances complètes sur une machine coûtent cher en perf, c'est la piste réelle à examiner avant de soupçonner le réseau ou le code.
- **`obstacleMask`** (utilisé par `CheckCapsule`/`CheckSphere`, pas seulement des raycasts) doit exclure le layer du joueur : `obstacleMask &= ~(1 << gameObject.layer)` dans `Awake`.
- **`Destroy()` est DIFFÉRÉ à la fin de la frame — la physique, elle, voit encore l'objet.** `GameObject.CreatePrimitive` livre toujours un collider ; le détruire ne suffit pas, il faut le **désactiver immédiatement** (`col.enabled = false`), ce qui prend effet tout de suite. Symptôme vécu le 2026-09-24 : l'adversaire **reculait visiblement à chaque balle encaissée**, parce que le marqueur d'impact naissait à la surface de son corps avec un `SphereCollider` vivant une frame, dont son `CharacterController` se poussait au `Move()` suivant. Conséquences plus sournoises encore : la sphère naissant sur le layer `Default` (celui de `worldMask`), elle **bloquait les tirs suivants comme un mur**, et comptait comme obstacle pour `CanStandUp()` et l'anti-clipping du lean. **Tout objet purement visuel créé pendant le jeu doit naître sans collider actif.**
- **Ordre recul/raycast dans `Fire()`** : le recul s'applique APRÈS avoir déterminé où le tir atterrit, sinon chaque tir est décalé par son propre recul.
- **Near Clip Plane** gardé petit (~0.03-0.05), sinon un mur très proche disparaît du rendu.
- **Enum sérialisé** : Unity le sérialise par sa valeur entière, pas par son nom — préserver l'ordre existant en ajoutant des valeurs (cf. `PlayerSoundEvent`).
- 🚨 **Le `CharacterController` ne pose pas ses pieds au sol tout seul** (corrigé le 2026-10-06, `PlayerLocomotion.CapsuleCenterHeight`). Il garde sa marge de peau (`skinWidth`, 8 cm) entre la capsule et le sol ; et quand la hauteur demandée est inférieure au diamètre (allongé : 0,5 m pour 0,8 m), Unity en fait une sphère qui dépasse sous la racine. Avec une capsule posée sur la racine, le robot flottait de **8,6 cm debout et 18,8 cm allongé** — et rien ne le signalait, puisque modèle, caméra et surface touchable flottaient ensemble. La capsule commence donc à `skinWidth` au-dessus de la racine, en tenant compte de sa hauteur effective ; son volume dans le monde est inchangé. `CanStandUp()` suit le même placement. Gardé par `DansChaquePosture_LesPiedsReposentSurLeSol`, validé par mutation (8,0 / 8,0 / 23,0 cm avec l'ancien placement).
- ⚠️ **Après une chute rapide, le `CharacterController` peut s'arrêter DANS sa marge de peau** au lieu de s'y poser : dans un test, le même placement donnait 0 cm après une chute de 1,5 m et 8 cm après une pose douce. Un test de hauteur doit partir d'une pose douce, comme une apparition.

## Structure du projet

**Prefab joueur unique** : `Assets/_ProjectArena/PlayerPrefab/Player.prefab`. Sur la racine : `CharacterController`, `NetworkObject`, `PlayerLocomotion`, `PlayerInputReader`, `CrosshairUI`, `WeaponVisualFeedback`, `PlayerSoundEmitter`, `Health`, `AudioSource`, `PlayerAnimator`. Enfants : `CameraPivot` (`PlayerCameraLook`) → `Leanpivot` → `Main Camera` ; `Weapon` (`WeaponController`) ; `Hitbox` (`PlayerHitbox`, layer 7) ; `Model` (le personnage et son `Animator`). Référencé par GUID dans `NetworkManager.PlayerPrefab` et `Assets/DefaultNetworkPrefabs.asset`.

**Scène unique** : `Assets/_ProjectScenes/MultiTestScene.unity`, seule scène de la build et seule avec un `NetworkManager` (UnityTransport, 127.0.0.1:7777, local uniquement). Elle utilise le terrain `Assets/New Terrain 1.asset` — malgré son nom, **il est utilisé**.

`Arena.unity` (la scène d'origine, gardée pour mémoire), son terrain `New Terrain.asset` et `CubeTest` + `TestSpin.cs` ont été **supprimés le 2026-10-05**, avec l'accord de l'utilisateur. L'historique Git les garde.

**Placeholders assumés** (à remplacer avec le vrai système d'armes / le vrai HUD, pas des bugs) : `CrosshairUI` (réticule OnGUI), `WeaponVisualFeedback` (tracer/impact procéduraux), `NetworkBootstrapUI` (boutons Host/Server/Client en OnGUI).

**Le jeu n'utilise que `PlayerControls.inputactions`** (classe C# générée, dans `DuelArena.Input`). L'asset d'actions *globales* du gabarit Unity (`InputSystem_Actions`) a été retiré le 2026-10-05 : il était chargé dans la build sans servir à rien. Les touches actuelles (sprint sur Alt, sneak sur Shift) sont la configuration personnelle de l'utilisateur ; le GDD (§ 15) prévoit des touches personnalisables.

⚠️ **Piège de nommage** : `/Spawn` et `/Spawn Enemy` dans `MultiTestScene` **ne sont PAS des points d'apparition** — ce sont des groupes de géométrie ProBuilder (caisses, cubes) situés *dans* les zones de spawn. Les vrais points d'apparition sont sous `/SpawnPoints`.

### Points de spawn

`PlayerSpawnPoints` (sur `/SpawnPoints`) liste un `Transform` par point : sa **position** donne les pieds du joueur, sa **rotation Y** la direction du regard à l'apparition. Un `OnDrawGizmos` dessine une capsule debout et une flèche, pour vérifier d'un coup d'œil qu'un point n'est pas dans un mur.

Deux points posés dans l'arène : `Spawn A` en (0, 0, 2) face au +Z, `Spawn B` en (0, 0, 58) face au −Z — les deux extrémités, à ~56 m l'une de l'autre, validés au sol et dégagés pour une capsule debout.

`PlayerLocomotion.ServerMoveToSpawnPoint()` est **serveur uniquement** (aucun client ne choisit son point, même principe que pour les dégâts) : il retient le point le plus **éloigné des joueurs déjà présents**, remet l'inertie à zéro, puis publie position et yaw. Appelé depuis `OnNetworkSpawn`, il est **public exprès** : la boucle de round (BO5) devra replacer les deux joueurs à chaque manche et pourra l'appeler tel quel.

Sans composant dans la scène ou sans point renseigné, les joueurs apparaissent à la position du prefab — le comportement d'avant, pas une erreur.

## Boucle de manches — BO5 (2026-09-24)

`RoundManager` (sur `/RoundManager` dans `MultiTestScene`, `NetworkObject` in-scene **actif**) pilote le duel. **Entièrement serveur-autoritaire** : phase, score et transitions sont décidés par le serveur et publiés en `NetworkVariable`. Aucune RPC client→serveur n'existe — même principe que pour les dégâts.

Machine à états : `WaitingForPlayers` → `Starting` (décompte) → `Active` → `RoundOver` → manche suivante, jusqu'à `roundsToWin` (3 = BO5). Puis `MatchOver`, et un nouveau match repart automatiquement — pratique pour tester en continu.

**Détection de mort par SONDAGE de `Health.IsDead`**, pas par abonnement à `OnDeath`. À deux joueurs le coût est nul, et ça évite toute la classe de bugs de cycle de vie des abonnements (déconnexion en pleine manche, objet détruit avant désabonnement, double abonnement au respawn).

**Le survivant marque**, pas « celui qui a tiré » : en 1v1 c'est équivalent, et ça évite de faire remonter l'identité du tireur jusqu'ici. À revoir le jour où on pourra mourir autrement que sous les balles de l'adversaire (chute, zone).

**Gel hors manche.** Pendant le décompte et après une mort, `RoundManager.MovementAllowed` coupe le déplacement dans `Move()` (le regard reste libre) et `FiringAllowed` bloque le tir — côté client pour le confort, et surtout **côté serveur dans `FireServerRpc`**, seul endroit qui compte. Le gel étant lu dans `Move()`, un rejeu de réconciliation utilise la valeur de *maintenant* et non celle de l'input rejoué : même compromis que pour la posture, acceptable pour la même raison (la transition arrive une fois par manche, joueurs immobiles, et le seuil de réconciliation rattrape l'écart).

**`ServerMoveToSpawnPoint()` remet aussi à zéro la posture et le lean**, en plus de l'inertie. Sans ça, on reprenait la manche suivante dans l'état où on était mort : penché derrière un angle qui n'existe plus, ou allongé en plein milieu.

⚠️ **Le lean est le seul état qui exige une RPC vers le propriétaire pour être annulé** (`ResetLeanClientRpc`). Son *décalage* est serveur, mais son **ÉTAT** (-1/0/+1) est une bascule qui vit sur le client : remettre le décalage à zéro côté serveur ne suffit pas, le propriétaire se repenche dès la frame suivante puisque sa touche est toujours considérée comme enclenchée. La posture, elle, vient d'une `NetworkVariable` et se remet directement.

L'ordre dans `BeginRound()` compte : on **soigne avant de replacer**. `ServerMoveToSpawnPoint()` choisit le point le plus éloigné des autres joueurs, donc replacer le premier influence le choix du second — c'est ce qui garantit des extrémités opposées même si les deux sont morts au même endroit. Vérifié en Play Mode : (0, 2) et (0, 58).

**Tranché le 2026-10-02 : pas de limite de temps.** Une manche se termine par une mort, le chrono n'était qu'un outil de test. Le code le supporte déjà : `roundTimeLimit = 0` désactive la limite (test `roundTimeLimit > 0f` dans `TickActiveRound`). Reste à passer le champ à 0 dans `MultiTestScene`, ce dont l'utilisateur se charge. Le « chrono » du futur HUD est le **temps écoulé** dans la manche, pas un compte à rebours. Si des manches passives apparaissent en playtest, la piste retenue par le GDD est la révélation sonore, pas le retour d'une limite.

⚠️ **`RoundManager` suppose exactement deux joueurs connectés**, vérifié dans le code le 2026-10-02 : la manche démarre dès que `CountAlivePlayers() >= 2`, et à une mort `AddWin` est appelé pour **chaque** joueur non mort. À trois clients, deux joueurs marqueraient à chaque mort, et tous seraient armés dans l'arène. C'est le point qui bloque le modèle de session du GDD (voir « Ordre de travail »).

L'affichage `OnGUI` du `RoundManager` est un **placeholder** au même titre que `NetworkBootstrapUI`, à remplacer par le HUD UI Toolkit.

## Dettes techniques (audit du 2026-09-21, vérifié dans le code)

1. ✅ **Corrigé** — speedhack par flood d'inputs (borne par frame → budget sur le temps réel).
2. ✅ **Corrigé** — `origin` de tir non validée dans `FireServerRpc`.
3. ✅ **Corrigé le 2026-09-22** — réconciliation sans seuil d'erreur. Le client ne se recale plus qu'au-delà de `positionReconciliationThreshold` (5 cm) ou `yawReconciliationThreshold` (1°), au lieu de recaler + rejouer à chaque frame.
4. **Un état échappe encore à la réconciliation.**
   - ✅ **Corrigé le 2026-09-22** — `currentVelocity` est désormais renvoyée par le serveur dans la correction et appliquée avant le rejeu.
   - ✅ **Corrigé le 2026-09-22** — la hauteur/le rayon du `CharacterController` étaient interpolés dans `Update()` avec le `Time.deltaTime` local de chaque instance, donc client et serveur ne simulaient pas avec la même capsule pendant une transition de posture. La capsule de **collision** est désormais une fonction PURE de `networkStance` (instantanée, `ApplySimulationCapsule`), appliquée depuis `Move()` pour être correcte aussi pendant un rejeu. La hauteur caméra et la **capsule visuelle** gardent leur interpolation douce dans `UpdateStanceVisuals()` (catégorie C, sans effet sur la simulation). *(Depuis : la capsule visuelle a été supprimée le 2026-10-05, et la caméra suit la transition de posture depuis le 2026-10-06, voir « Les transitions de posture ».)*

   **Choix assumé** : supprimer l'état cumulatif plutôt que d'ajouter une hauteur confirmée de plus dans la RPC de correction. Conséquence : bref décalage (~0,1 s) entre la capsule visible et celle qui entre en collision pendant une transition. Se relever reste protégé par `CanStandUp()`, donc la capsule debout instantanée ne peut pas faire traverser un plafond.
5. ✅ **Corrigé le 2026-09-22** — spawn téléporté à l'origine du monde. `OnNetworkSpawn` appliquait `transform.position = networkPosition.Value` inconditionnellement, alors que la `NetworkVariable` vaut encore `default` = (0,0,0) à cet instant. Le serveur publie désormais sa position de spawn, et seuls les clients s'y alignent.

7. ✅ **Corrigé le 2026-09-22** — les deux joueurs apparaissaient au même endroit, `NetworkManager` instanciant le prefab à sa propre position pour tout le monde. Voir « Points de spawn » ci-dessous.

6. ✅ **Corrigé le 2026-09-22** — GPU Resident Drawer incompatible avec la géométrie ProBuilder. `PC_RPAsset.asset` avait `m_GPUResidentDrawerMode: 1`, ce qui noyait la Console sous ~150 erreurs `BatchDrawCommand was submitted with an invalid Batch, Mesh, or Material ID` à chaque ouverture de scène — sans effet visible en jeu, mais ça masquait les vraies erreurs. Passé à 0 ; son gain est nul à l'échelle d'une arène de duel. **À reconsidérer seulement si la géométrie finale n'est plus du ProBuilder et que le nombre d'objets explose.**

8. ✅ **Corrigé le 2026-09-24** — le collider accidentel sur l'enfant `Capsule` (reste de la primitive Unity) a été supprimé. La surface touchable est désormais le seul `PlayerHitbox`, explicite et dédié.
9. ✅ **Corrigé le 2026-09-24** — `hittableMask` valait `Everything` ; remplacé par deux masques explicites, `worldMask` (géométrie) et `hitboxMask` (layer `Hitbox`), utilisés par deux traces distinctes.

**Hygiène** : ✅ **assemblies et tests posés le 2026-09-28** — voir la section ci-dessous.

### Relecture complète du 2026-10-05 (vérifié dans le code, l'Editor et l'importeur)

État de départ : 39 tests verts (23 EditMode, 16 PlayMode), console vierge, dépôt propre.

10. ✅ **Corrigé le 2026-10-05 par le lean façon R6 et le hitbox par zones** (voir « Le hitbox par zones et le lean façon Rainbow Six »). Le diagnostic : 🔴 **le lean n'était pas affiché, mais le hitbox glissait** de 0,5 m sur le côté.
11. 🟡 **En partie corrigé le 2026-10-05** — le sneak et le ramper sont classés `Faint` (nouvelle valeur de `NoiseLevel`) et joués à volume réduit (`PlayerSoundEmitter.faintVolumeScale`, 0,35) au lieu d'être coupés ; gardé par trois tests, validés par mutation. **Restent, à la charge de l'utilisateur et sans urgence** : les clips manquants, la portée par allure (« sera réglée en temps et en heure ») et le son du MP5 (« pour s'amuser, sera changé »). Le diagnostic : 🟠 **Le son de gameplay est presque muet.** `PlayerSounds.asset` n'a de clip que pour `FootstepWalk` : course, accroupi, ramper, postures et lean ne jouent rien. En plus, `ComputeNoiseLevel` classe le sneak ET tout le prone en `Silent`, que `PlayerSoundEmitter` filtre : même avec des clips, ramper et marcher en sneak resteraient muets, alors que le GDD (§ 8) les veut faibles mais audibles. Une seule `AudioSource` à 20 m de portée sert à toutes les allures, dans une arène de 66 × 74 m : « la course s'entend de partout » est impossible. Le MP5 tire au hasard parmi cinq sons sans rapport (goutte d'eau, fusil à pompe, laser…), donc l'arme n'est pas reconnaissable à l'oreille. Les tests de son vérifient le *mapping*, pas l'audibilité : ils restent verts.
12. ✅ **Corrigé le 2026-10-05** — le recul horizontal est désormais porté par la caméra seule (`recoilYaw` dans `PlayerCameraLook`), gardé par `RecoilTests`, validé par mutation (l'ancien code fait échouer `LeReculHorizontal_NeTournePasLeCorps`). Le diagnostic : 🟠 **Le recul horizontal tournait le corps HORS de `Move()`.** `PlayerCameraLook.AddInstantRotation` fait `locomotion.transform.Rotate(yawDelta)` : un client distant modifie son yaw sans que le serveur le sache. Latent avec le MP5, dont le zigzag ±0,6° reste sous le seuil de 1° ; une arme à dérive horizontale d'un seul côté provoquerait des resynchronisations en pleine rafale. Même famille que les quatre pièges de valeurs cumulatives.
13. ✅ **Corrigé le 2026-10-05** — l'origine du rayon est calculée (position + hauteur de caméra de la posture réseau), donc identique partout. Le diagnostic : 🟡 **L'anti-clipping du lean divergeait entre propriétaire et serveur.** `ComputeAllowedLeanOffset` part de `cameraPivot.position`, que seul le propriétaire met à jour (`ApplyCameraPivotPosition`). Sur le serveur, le `CameraPivot` d'un client distant reste à 1,65 m même accroupi ou allongé : près d'un obstacle bas, le serveur calcule un autre lean que le client. À calculer depuis une fonction pure (position + hauteur de caméra de la posture réseau).
14. ✅ **Corrigé le 2026-10-05** — la RPC est supprimée ; le serveur déduit les sons de lean des transitions qu'il reçoit dans les inputs (`ServerTrackLeanSound`), avec un intervalle minimal de 0,1 s en temps réel. Changer directement de côté joue désormais `LeanStart`. ✅ **Refermé le 2026-10-06** : `RequestStanceChangeServerRpc` est borné par les transitions de posture (un changement au plus par transition, voir « Les transitions de posture »). Le diagnostic : 🟡 **`RequestPlayerSoundServerRpc` n'avait aucune borne de débit** (règle 2 des garde-fous). Elle est d'ailleurs devenue inutile : l'état de lean voyage dans chaque input, le serveur peut détecter les transitions lui-même, comme pour la posture.
15. ✅ **Corrigé le 2026-10-05** — `roundTimeLimit` vaut 0 (code et scène) ; déplacement et tir sont libres pendant `WaitingForPlayers` (vérifié en Play Mode : un Host seul se déplace) ; la phase publie son début et sa fin en temps serveur (`phaseStartedAt` / `phaseEndsAt`), une fois par changement, et expose `PhaseElapsed`, le chrono du futur HUD. Le diagnostic : `roundTimeLimit` valait 60 s alors que le GDD a supprimé la limite, le joueur seul était figé, et `phaseTimeRemaining` était réémis à chaque tick.
16. ⏸️ **Reporté (décision de l'utilisateur, 2026-10-05)** : la disposition des spawns dépendra des modes de jeu. Le diagnostic : deux points et « le plus éloigné des autres », alors que le GDD (§ 5) veut plusieurs points et un échange de côté à chaque manche.
17. ✅ **Corrigé le 2026-10-05.** Au passage, trois appels d'API devenus obsolètes en Unity 6.6 (`FindFirstObjectByType`, `FindObjectsByType` avec tri) ont été remplacés. Le diagnostic : ⚪ **Commentaires qui contredisaient les règles du projet** : `PlayerLocomotion` annonce encore « un hitbox qui suivra les os » (exactement ce qui est interdit), un lean « sans effet sur la simulation », un rewind « à venir » ; `WeaponController` dit encore n'avoir « pas de compensation de latence » ; un tooltip place le sneak sur Ctrl alors qu'il est sur Shift. Un commentaire faux finit par être cru.
18. ✅ **Corrigé le 2026-10-05** — `PlayerAnimator` lit `PlayerLocomotion.NominalSpeed(posture)` ; vérifié en Play Mode, un strafe à la marche donne `MoveX = -1,00` exactement. Le diagnostic : 🟡 `PlayerAnimator` recopiait les vitesses de référence (4,4 / 2,64 / 1,1) au lieu de les lire dans `PlayerLocomotion` — **et le désaccord a déjà eu lieu** : le prefab règle `walkSpeed` à 5 m/s (et `runSpeed` à 10, `sneakSpeed` à 2), pas à 4,4. L'arbre de mélange reçoit donc 1,14 à la marche au lieu de 1. À dériver de `PlayerLocomotion` (vitesse de marche × multiplicateur de posture).
19. ✅ **Supprimés le 2026-10-05**, avec l'accord de l'utilisateur, sauf les 11 clips inutilisés, gardés pour les futures transitions de posture. Tests et Play Mode verts après coup. Le diagnostic : ⚪ **Restes de gabarit et éléments morts** : `CubeTest` + `TestSpin.cs` ; `Arena.unity` **toujours dans la build** (et `New Terrain.asset`, qu'elle seule utilise) ; la `Capsule` désactivée du prefab, le code `visualCapsule` et les deux matériaux `M_Player` (inutilisé) / `M_Enemy` ; les dossiers vides `Resources`, `Scenes`, `TutorialInfo` ; `InputSystem_Actions.inputactions`, déclaré comme actions *globales* du projet et préchargé dans la build alors que le jeu utilise `PlayerControls` ; l'action `Vault` (doublon de `Jump`, jamais lue) ; le layer 6 nommé `obstacleMask` (aucun collider) ; le niveau de qualité et le pipeline `Mobile` ; quatre packages directs sans dépendant ni usage dans le code (`visualscripting`, `collab-proxy`, `ai.navigation`, `ai.inference`) ; 11 clips d'animation importés mais inutilisés.
20. ✅ **Y Bot en place le 2026-10-05** (voir « Le personnage et l'animation »). Le soldat SWAT reste comme avatar source des animations, sans être affiché.
21. ✅ **Remote GitHub privé ajouté le 2026-10-05** (voir « Configuration Git »).

**Découverts en posant le lean R6 (2026-10-05)** :

22. ✅ **Corrigé** — **un joueur allongé immobile rampait sur place.** L'arbre de mélange `Prone` de la couche de déplacement gardait ses seuils automatiques (0 / 0,5 / 1 au lieu de -1 / 0 / 1) : à `MoveY = 0`, il jouait la reptation arrière à plein poids, et le corps tournait en diagonale au fil du clip, jusqu'à 35° de sa surface touchable. Même piège que celui de la couche haut du corps, qui n'avait été corrigé que là. Gardé par `Allonge_Immobile_JoueLaPoseDAttente_EtRampeQuandIlBouge`, mutation vérifiée.
23. ✅ **Corrigé** — **allongé, la surface touchable ne correspondait pas au corps visible** : une boule de 80 cm autour de la racine, alors que le robot s'étend de 1,25 m en arrière (jambes intouchables) à 50 cm en avant, et la caméra était 50 cm derrière la tête visible.
24. 🟡 **Allongé, le corps dépasse du `CharacterController`.** Le collider de mouvement est une capsule verticale ; le corps allongé fait 1,5 m. Les jambes (visuelles et touchables) peuvent donc passer à travers un mur derrière soi, et pivoter allongé les fait balayer à travers ce qui est à côté. La tête, elle, est protégée (recul du modèle). Il manque une vérification « le corps tient-il ici ? » avant de s'allonger et de pivoter, déterministe et dans `Move()`.
25. ⚪ Les zones de tir ne pèsent pas encore sur les dégâts (multiplicateurs à trancher par playtest, GDD § 6).

**Découverts au premier test du robot à deux instances (2026-10-06)** :

26. ✅ **Corrigé** — **l'adversaire saccadait chez le client** : poses datées à l'arrivée, rotation non interpolée. Voir « L'affichage des adversaires ».
27. ✅ **Corrigé** — **le rewind aurait sous-compensé de 50 ms** une fois l'affichage passé sur l'horloge de Netcode. Le tir envoie maintenant l'instant exact qu'il voit.
28. ✅ **Corrigé** — **le robot flottait** de 8,6 cm debout et 18,8 cm allongé. Voir les pièges.
29. ✅ **Corrigé** — **le simulateur réseau était resté actif** dans la scène : environ 316 ms d'aller-retour, d'où « beaucoup de latence sur le client ». Désactivé par défaut, voir « Tester sous latence ».
30. 🟡 **Le Host voit le client par à-coups** (cas 3 : la simulation avance au rythme d'arrivée des inputs). Une frame sur trois immobile sous le simulateur. À traiter avant le playtest en Relay, et impératif avant un serveur dédié, où tous les joueurs seraient dans ce cas. Piste : afficher les joueurs simulés par le serveur avec un léger retard, à partir de l'historique, en datant chaque input par sa propre durée simulée plutôt que par sa frame d'arrivée.
31. ⚪ **Transitions de posture validées en jouant le 2026-10-06** (durées et clips). Restent, sans urgence : les clips finissent genou à terre au lieu du squat, et la surface touchable interpolée n'épouse pas chaque image du clip.

## Tester sous latence, et l'outillage de diagnostic (2026-09-28)

**Simulateur réseau.** `com.unity.multiplayer.tools` 2.2.12 est installé pour ça (le `DebugSimulator` d'`UnityTransport` est obsolète et sans effet, voir les pièges). Un composant `NetworkSimulator` est posé sur `/NetworkManager` avec le preset `Assets/_ProjectScenes/NetSim_Test150ms.asset` (75 ms + 10 ms de gigue par paquet).

🚨 **Il est DÉSACTIVÉ par défaut depuis le 2026-10-06.** Resté actif, il faisait jouer l'utilisateur à **~316 ms d'aller-retour** sans le savoir, d'où « beaucoup de latence sur le client ».

⚠️ **Ne pas se fier au seul RTT affiché sous ce simulateur.** Avec le même preset, le `[DIAG-CLIENT]` a mesuré tantôt ~165 ms (2026-09-28, et une session du 2026-10-06), tantôt 310 à 320 ms (toutes les sessions du 2026-09-29 au 2026-10-06 matin). Et dans une session affichée à 165 ms, les poses de l'adversaire arrivaient avec ~185 ms de retard (rewind mesuré de 260 ms, dont 75 de réserve) : le preset ne s'applique visiblement pas de façon symétrique aux deux instances, peut-être selon que le clone a repris la scène sauvegardée. Le retard qui compte pour un tireur est celui que mesure le rewind (`[DIAG-SERVEUR] rewind_moy`). Pour un test sous latence, cocher le composant, et viser plutôt ~25 ms par paquet pour une partie entre amis réaliste (~100 ms d'aller-retour).

**Diagnostics de session**, tous en `#if UNITY_EDITOR`, jamais embarqués :
- `[DIAG-CLIENT]` (dans `PlayerLocomotion`) — **santé de la prédiction** : taux de resynchronisation, erreur moyenne et max, RTT, profondeur de la file serveur. Le chiffre qui compte est le **taux de resync** : une prédiction saine est à 0 %.
- `[DIAG-SERVEUR]` (dans `WeaponController`) — tirs acceptés, touchés, **rejetés par motif**, rewind réellement appliqué, plus la mesure différentielle décrite plus bas.
- `PlayerInputReader.AutopilotStrafe` — fait faire au joueur un va-et-vient latéral déterministe à la place du clavier. Indispensable pour tester le rewind : une seule personne ne peut pas à la fois se déplacer sur une instance et viser sur l'autre.
- `[DIAG-ANIM]` (dans `PlayerAnimator`, 2026-10-06) — pour chaque joueur que l'instance ne possède pas, toutes les 3 s : images par seconde, **entrées et relances d'état** d'Animator par couche, bascules du drapeau « en l'air », frames immobiles en pleine course, plus grand pas d'une frame, frames « affamées » (instant affiché au-delà de la dernière pose reçue). C'est lui qui a tranché « les animations recommencent du début » : zéro relance, mais des bonds de position.
- **`PlayerInputReader.AutopilotFire` + `WeaponController.AutopilotAimAtOpponent`** — gâchette maintenue et visée au centimètre sur le torse de l'adversaire **tel qu'il est affiché**. Juge le rewind sans biais humain : avec un rewind exact, `[DIAG-SERVEUR]` doit trouver le rayon au centre du corps PASSÉ.

**Banc de test à deux instances (2026-10-06).** Le clone de Multiplayer Play Mode ne se pilote pas depuis l'Editor principal. `NetworkBootstrapUI` lit au démarrage du clone trois interrupteurs dans les **EditorPrefs** (partagés par les deux processus, puisqu'ils vivent dans le registre de l'utilisateur) : `DuelArena.Test.CloneAutoClient` (connexion automatique en client), `DuelArena.Test.CloneAutopilot` (va-et-vient), `DuelArena.Test.CloneAutofire` (tir automatique visé). Les allumer par `eval` (`UnityEditor.EditorPrefs.SetBool(NetworkBootstrapUI.CloneAutoClientPref, true)`), démarrer le Host dans l'Editor principal, lire `Library/VP/<clone>/Logs/Editor.log`. **Les éteindre après** : sinon la prochaine session de l'utilisateur se connecte et tire toute seule.

⚠️ **Lancer les tests PlayMode pendant que le joueur virtuel est actif le laisse bloqué** sur la scène temporaire des tests, sans recompiler (vécu le 2026-10-06) : il ne se connecte plus, son journal ne bouge plus. Le redémarrer depuis Window > Multiplayer Play Mode, ou relancer le projet. **Faire les mesures à deux instances AVANT les tests PlayMode.**

⚠️ **Les compteurs de diagnostic sont des `static`, et le rechargement de domaine est désactivé** à l'entrée en Play Mode : ils survivent d'une session à l'autre. `[DIAG-SERVEUR]` s'est tu pendant toute une session, son échéance héritée de la précédente étant loin dans le futur du nouveau `Time.time` (corrigé le 2026-10-06 : une échéance à plus de 3 s est jugée périmée). Même précaution pour tout nouveau static de diagnostic.

**Lire le journal du joueur virtuel** : `Library/VP/<clone>/Logs/Editor.log`. C'est ce qui permet d'observer le côté CLIENT sans y avoir accès autrement. Attention, il contient des octets binaires et des fins de ligne `CR` seules : `tr '\r' '\n' < log | grep -a "DIAG-CLIENT"`.

### Résultats mesurés le 2026-09-28 (RTT ~165 ms, 2 joueurs réels)

- **Prédiction : 0 % de resynchronisation**, erreur moyenne 0,0 cm, file serveur à 0. Vérifié sur des dizaines de fenêtres, en mouvement et en tirant. C'est la première validation réelle sous latence du projet.
- **Garde-fous : aucun rejet abusif** sur ~150 tirs (cadence, origine). Les seuls rejets étaient des tirs pendant une transition de manche — le gel qui fonctionne.
- **Rewind : validé, avec une réserve de méthode.** Un A/B « avec / sans rewind » sur les taux de touche s'est révélé **inconcluant** : le joueur anticipe instinctivement le déplacement d'une cible mobile et compense donc l'absence de compensation, ce qui masque l'effet. La mesure qui a tranché est **continue** : la distance perpendiculaire entre le rayon et le centre de la cible, à sa position passée et actuelle. Résultat reproductible sur trois séries : **la position PASSÉE est systématiquement plus proche du rayon, d'environ 0,4-0,5 m** — cohérent avec 4,4 m/s × 182 ms. Le tireur vise bien ce qu'il voit, et le rewind corrige le bon écart.

**Leçon de méthode** : pour juger une fonctionnalité qui corrige un décalage, mesurer le décalage lui-même, pas un taux de réussite. Le taux mélange la compétence du joueur, son adaptation et le bruit ; la mesure continue donne un signal exploitable dès une vingtaine de tirs.

⚠️ Un `gagnes_par_rewind=0 perdus_par_rewind=2` a été observé lors d'une série où le joueur anticipait encore. Ce n'est **pas** un bug : quand le rewind fonctionne, anticiper est contre-productif. Ne pas partir en chasse d'une régression sur ce signal sans d'abord vérifier la consigne de visée.

## Assemblies et tests (2026-09-28)

Le code de jeu a quitté `Assembly-CSharp` pour deux assemblies : `DuelArena.Input` (`_ProjectInput`, l'asset Input Actions généré) et `DuelArena.Runtime` (`_ProjectScripts`, qui référence la première, Netcode et l'Input System).

Ce n'était pas qu'une question de temps de compilation : **un assembly de test ne peut pas référencer `Assembly-CSharp`**, l'assembly prédéfini. Sans asmdef, aucun test ne pouvait voir le code du jeu. Vérifié après coup : toutes les références de scripts dans le prefab joueur et la scène ont survécu (les GUID de fichiers ne changent pas), et le jeu tourne.

**Tests EditMode** sous `Assets/Tests/EditMode` (`DuelArena.Tests.EditMode`). Lancer : `unity command run_tests --project-path "..." --mode EditMode`. 51 tests EditMode et 28 tests PlayMode au 2026-10-06, tous verts.

Ils couvrent volontairement la **logique pure**, là où une régression est à la fois probable et silencieuse :
- `SampleHitboxHistory` — le cœur du rewind. Un échantillonnage cassé ne lève aucune erreur, il fait juste rater des tirs qui auraient dû toucher. Couvre l'interpolation position/lean, le `LerpAngle` du yaw (un `Lerp` ferait tourner le hitbox à l'envers entre 350° et 10°), la posture non interpolée, les bornes, la division par zéro et l'historique vide/null.
- `PlayerSpawnPoints` — dont le cas réel de la boucle BO5 : deux joueurs morts au même endroit doivent repartir à deux extrémités.
- Les tables son ↔ posture/allure : le GDD fait du son une information de gameplay, donc un mauvais mapping est une information *fausse*, pas un détail cosmétique.

`SampleHitboxHistory` a été **extraite en fonction pure statique** pour cette raison : sous cette forme elle se teste sans Editor, sans réseau et sans scène.

🚨 **Le piège du test vide : asserter un ÉTAT FINAL quand le défaut est TRANSITOIRE.** Trois fois le même jour (2026-09-29/30), un test écrit pour couvrir un défaut réel s'est révélé vert AVEC et SANS ce défaut :

- Le retour de posture après une chute : attendre 0,6 s laissait l'enchaînement `EnLAir → Debout → Accroupi` se terminer, donc l'état final était juste dans les deux cas. Le défaut était le passage **fugitif** par `Debout`.
- L'arrivée d'un vault : mesurer la hauteur après 200 pas de simulation laissait le joueur, qui gardait son input avant, **descendre tout seul** de l'obstacle sur lequel il était perché.
- Le déterminisme du vault, à sa première écriture : le joueur n'était pas au sol, aucun vault ne se déclenchait, et comparer deux immobilités passait au vert.

**La règle** : quand le défaut est un état ou un passage TEMPORAIRE, il faut échantillonner pendant, pas conclure après. Et à chaque fois, c'est la mutation qui l'a dit — jamais la lecture du test.

🚨 **Un test qui passe ne prouve rien tant qu'on ne l'a pas vu échouer.** Les tests ont été validés par mutation : en remplaçant `Mathf.LerpAngle` par `Mathf.Lerp` dans l'échantillonnage du yaw, `YawInterpole_ParLePlusCourtChemin` échoue bien (22/23), puis repasse au vert une fois le code restauré. À refaire pour tout nouveau test non trivial.

### Tests PlayMode — le déterminisme de `Move()` (2026-09-29)

`Assets/Tests/PlayMode` (`DuelArena.Tests.PlayMode`) : 8 tests de déplacement (déterminisme, vitesse pendant une transition de posture, pieds au sol), 2 tests de recul (`RecoilTests`), 6 tests des zones de tir par vrais rayons (`HitboxZoneTests`), plus les tests d'animation des sections suivantes (28 en tout au 2026-10-06). Lancer : `unity command run_tests --project-path "..." --mode PlayMode --async_tests`, puis sonder `test_status`. **Le mode synchrone ne marche pas** : entrer en Play Mode déclenche un rechargement de domaine qui coupe la requête HTTP.

Ils verrouillent l'**équivalence entre une simulation en avant et un REJEU depuis le même état** — exactement ce que suppose la réconciliation. C'était jusqu'ici « la garantie la plus précieuse du projet, vérifiée à la main ».

**Volontairement SANS session Netcode**, contrairement à ce que cette page prévoyait. Monter un host réel rendrait le test asynchrone et instable (connexion, ports, timing) pour prouver une propriété qui n'a rien de réseau : `Move()` est une fonction de (état, input, dt), le réseau décide seulement *quand* on l'appelle. Attaquer l'invariant directement le rend rapide et reproductible.

🚨 **`PlayerLocomotion.SimulationState` est un INVARIANT, pas un utilitaire.** Elle énumère tout l'état cumulatif lu par `Move()` — la même liste que celle des valeurs devant avoir un équivalent confirmé dans la RPC de correction. Le projet s'est fait piéger **quatre** fois dessus (yaw, `currentVelocity`, hauteur de capsule, état de vault). Si une cinquième apparaît sans être ajoutée ici, la restauration sera partielle et les tests échoueront — au lieu de laisser le bug se manifester en jeu en désynchronisation aléatoire.

**Validés par mutation** : en retirant la restauration de `currentVelocity`, les 3 tests échouent avec des écarts de 3,9 à 7,3 cm, puis repassent au vert une fois le code restauré.

**Trois pièges rencontrés en les écrivant**, tous silencieux :
- ⚠️ **Une asmdef de test avec `includePlatforms: ["Editor"]` est classée EditMode par Unity**, quel que soit son nom. Les tests PlayMode n'étaient découverts par personne — 0 test trouvé, et un rapport « réussi ». C'est `defineConstraints: ["UNITY_INCLUDE_TESTS"]` qui les exclut des builds, pas `includePlatforms`. Conséquence : la surface de test dans `PlayerLocomotion` est gardée par `#if UNITY_EDITOR || UNITY_INCLUDE_TESTS`, sans quoi une build « avec tests » casserait.
- ⚠️ **Un joueur de test doit être sur le layer `Player` (3)**, comme le prefab. `Awake()` fait `obstacleMask &= ~(1 << gameObject.layer)` : resté sur `Default`, il retirait `Default` du masque, donc l'obstacle lui-même. Aucun vault ne se déclenchait.
- ⚠️ **`Physics.SyncTransforms()` après avoir créé ou déplacé de la géométrie de test.** Même piège que pour le rewind : `autoSyncTransforms` vaut false, les raycasts ne voyaient pas l'obstacle.

**Chaque test porte une garde anti-vacuité** (le joueur a bougé d'au moins 1 m ; un vault s'est réellement déclenché). Elles ne sont pas décoratives : la garde du vault a **effectivement échoué** à la première exécution et révélé le piège du layer. Sans elle, le test aurait été vert en ne testant rien — c'est exactement ce qui était arrivé au premier test de vault manuel.

**Ce qui reste NON couvert** : l'accord client/serveur sous vraie latence (mesuré à la main via `[DIAG-CLIENT]`), et tout ce qui dépend d'une `NetworkVariable` en écriture serveur — la posture notamment, qui reste à `Standing` dans ces tests.

## Le personnage et l'animation (2026-09-29)

**Depuis le 2026-10-05, le personnage est `Y Bot` (Mixamo)**, robot provisoire choisi par l'utilisateur, attaché au prefab joueur sous l'enfant **`Model`**, à (0,0,0) et **sans mise à l'échelle** : 1,805 m, articulations à quelques centimètres de celles du soldat SWAT d'avant. Rig Humanoid (`Create From This Model`), import d'animation désactivé.

`SwattSolider_T_Pose` **reste dans le projet, sans être affiché** : les 30 animations de `Assets/_ProjectArt/Animations` ont été produites sur son squelette (Ch35) et l'utilisent comme avatar source à l'import (`Copy From Other Avatar`). C'est le bon avatar pour elles ; le retirer imposerait de réimporter les 30 clips sans rien y gagner. Les clips Humanoid se rejouent ensuite sur n'importe quel avatar humanoïde, Y Bot compris. Non référencé par une scène, il n'entre pas dans la build.

Les tests d'animation prennent le personnage **dans le prefab** (`TestCharacter.InstantiateModel`), pas par un chemin écrit en dur : ils suivent d'eux-mêmes tout changement de modèle.

**L'origine du Player est aux PIEDS** — `CameraPivot` est à 1,65, qui est la hauteur caméra debout mesurée depuis les pieds. Un Mixamo ayant lui aussi sa racine aux pieds, il s'attache sans décalage.

⚠️ **`SkinnedMeshRenderer.bounds` n'est PAS la taille du personnage.** C'est une boîte englobante *statique*, gonflée pour couvrir n'importe quelle pose d'animation : elle annonçait 1,92 m. La vraie mesure passe par `BakeMesh`, qui tient compte de la pose courante — **1,797 m en T-pose, 1,764 m en pose d'attente**, contre 1,80 m pour la capsule debout. L'écart est d'un demi-centimètre, donc aucune mise à l'échelle n'est nécessaire. Mesurer en T-pose reste d'ailleurs trompeur : on est naturellement plus petit en garde.

### Conformité d'import — trois propriétés non négociables

Tout FBX d'animation ajouté doit avoir : **rig Humanoid** en `Copy From Other Avatar` pointant l'avatar du personnage, **`lockRootPositionXZ` à FALSE** (Bake Into Pose décoché, voir la section « Root motion » plus bas), et un **bouclage correct**. Exception verticale : un clip qui fait **monter ou descendre** le corps sur place (transitions de posture) doit avoir `lockRootHeightY` à **TRUE**, sinon sa descente est jetée avec le root motion (voir « Les transitions de posture »).

⚠️ *Corrigé le 2026-10-05 : cette ligne exigeait l'inverse (`lockRootPositionXZ` coché) et contredisait la section « Root motion », qui est la bonne. L'importeur, interrogé ce jour-là, donne bien `lockRootPositionXZ = false` sur les 30 clips. Piège à surveiller au moment d'importer les animations du robot.*

- Sans Humanoid, Mecanim ne peut pas retargeter — or les animations viennent d'un AUTRE personnage (`Ch35_nonPBR`) que le modèle. Elles ne joueraient tout simplement pas.
- Le déplacement de la racine doit rester du root motion, que `applyRootMotion = false` jette. **La position horizontale appartient exclusivement à `Move()`** ; une animation qui déplace aussi le personnage entrerait en conflit avec la simulation, et la vitesse d'un Animator n'est pas déterministe entre machines.
- Le bouclage se classe sur le bon critère : un état **continu** boucle (attente, déplacement, phase aérienne), un **événement** ponctuel non (tir, transition de posture, atterrissage, vault). Une première version déduisait « transition » du `" To "` des noms Mixamo — juste au début, faux dès l'arrivée des tirs et des sauts.

### L'Animator

`Assets/_ProjectArt/PlayerAnimator.controller`, piloté par `PlayerAnimator.cs`.

**Convention des paramètres** : `MoveX`/`MoveY` sont la vitesse **locale divisée par la vitesse de référence de la posture**. Donc 0 = immobile, 1 = allure nominale, 2 = sprint. Le sneak (0,5) tombe naturellement entre l'attente et la marche, ce qui donne une foulée ralentie **sans clip dédié**. `Stance` (0/1/2) vient de la `NetworkVariable`, donc juste partout.

🚨 **`PlayerAnimator` dérive la vitesse du DÉPLACEMENT DU TRANSFORM, pas de `currentVelocity`.** Cette dernière n'existe que là où la simulation tourne (propriétaire et serveur) : chez un spectateur elle vaudrait zéro en permanence et l'adversaire glisserait sans bouger les jambes. Le delta de transform est juste dans les **quatre** cas réseau, interpolation du spectateur comprise — ce qui est précisément ce qu'on veut montrer. Il est borné à 2,5× la référence, sinon la téléportation de quelques centimètres d'une resynchronisation se dériverait en plusieurs mètres par seconde et ferait sursauter l'animation à chaque recalage.

Le script est de **catégorie C stricte** : il LIT, il n'écrit jamais rien que la simulation relise. C'est ce qui rend inoffensif le non-déterminisme de l'Animator.

**Le propriétaire ne voit pas son propre modèle** : ses renderers passent en `ShadowsOnly` dans `OnNetworkSpawn` (sa caméra est à hauteur de tête, il verrait l'intérieur du crâne). `ShadowsOnly` plutôt que désactivés, parce qu'il continue ainsi de projeter une **ombre**, qui est une information de jeu — voir sa propre ombre dépasser d'un angle renseigne sur ce que l'adversaire voit. Ciblé sur le seul sous-arbre `Model`, pour qu'une arme en vue première personne reste visible.

### 🚨 Root motion : « Bake Into Pose » fait l'INVERSE de ce qu'on croit (2026-09-29)

Le personnage s'éloignait de sa racine pendant chaque cycle d'animation puis **claquait en arrière à la boucle**. Défaut livré DEUX fois, et deux fois déclaré corrigé par une mesure qui portait à côté.

**La sémantique.** « Bake Into Pose » (`lockRootPositionXZ = true`) ne supprime pas le déplacement : il le **garde dans la pose** et l'enlève seulement du *delta* de root motion. Le corps voyage donc à l'écran pendant que le GameObject reste immobile. Le réglage correct ici est de **NE PAS baker** : le déplacement devient du root motion, que `applyRootMotion = false` jette purement et simplement. Vaut pour la position XZ comme pour la rotation.

**Pourquoi les deux vérifications ont menti**, et c'est la vraie leçon :

- `AnimationClip.averageSpeed` mesure le **delta de root motion**. Baker le fait tomber à zéro *tout en laissant le corps dériver* : la mesure annonçait « plus de déplacement » pendant que le personnage se téléportait.
- `AnimationClip.SampleAnimation` applique les **courbes brutes**, sans la gestion de root motion de l'Animator. `Sprint Forward`, réellement *In Place*, y affichait 3,5 m de dérive.

Les deux mesurent quelque chose de vrai, mais **pas ce qui se voit à l'écran**. La séparation racine/pose est un travail que fait **l'Animator au runtime**, pas le clip : aucune mesure hors runtime ne peut trancher.

**`Assets/Tests/PlayMode/CharacterDriftTests.cs`** existe pour ça — il fait tourner le vrai Animator avec le vrai controller et relève la position des hanches par rapport à la racine, frame par frame, en course, sprint et marche accroupie. Il journalise `[DRIFT]` avec la valeur mesurée et pas seulement le verdict : un test vert dit « sous le seuil » sans dire de combien.

🚨 **Le fichier `.meta` n'est PAS une source fiable pour les réglages d'import.** Après une mutation qui bakait bel et bien `Rifle Run` — prouvé par le test qui échouait — un `grep lockRootPositionXZ: 1` dans son `.meta` ne renvoyait **rien**. La seule source qui fait autorité est `ModelImporter`, lu par script. Troisième fois dans la même session qu'une vérification par proxy a menti, après `averageSpeed` et `SampleAnimation` : **vérifier un réglage d'import, c'est interroger l'importeur, pas lire un fichier.**

**Validé par mutation le 2026-09-29** : en remettant « Bake Into Pose » sur la seule `Rifle Run`, `CourseDebout_LeCorpsResteSurSaRacine` échoue avec **1,33 m de dérive** pour un seuil de 0,50 m, pendant que les deux autres tests restent verts — il détecte le défaut ET désigne le bon clip. Marges à l'état sain : 3,6 cm en course, 15,3 cm accroupi, 8,5 cm en sprint.

⚠️ **Le lanceur de tests PlayMode se bloque parfois, et seul un redémarrage de l'Editor le débloque.** `run_tests --mode PlayMode` renvoie alors **0 test avec un statut « réussi »** — encore un échec qui se lit comme un succès. `cancel_tests` révèle une exécution fantôme mais ne suffit pas. EditMode n'est jamais affecté.

**Le déclencheur exact n'est PAS établi.** Observé quatre fois après une écriture d'asset par script (`SaveAndReimport` sur un modèle, `SaveAssets` sur un AnimatorController) — mais une cinquième écriture de contrôleur, dans les mêmes conditions apparentes, n'a rien bloqué. C'est donc **intermittent**, pas déterministe. Une première version de cette page affirmait « toute écriture d'asset bloque le lanceur » : c'était une généralisation à partir de quatre cas, contredite au cinquième.

**Conséquence pratique** : ne pas s'étonner d'un « 0 test » après avoir modifié un asset, et ne pas chercher la cause dans le code de test — redémarrer et relancer. Prévoir ce redémarrage quand on valide par mutation quelque chose qui vit dans un ASSET. Une mutation qui ne touche qu'au CODE n'a jamais posé de problème.

*Sixième occurrence le 2026-10-05*, après un `SaveAssets` sur l'AnimatorController (mutation de l'arbre `Prone`). Cette fois, **une recompilation suivie d'un nouvel essai a suffi** à débloquer le lanceur, sans redémarrage. Un seul cas : à essayer d'abord, sans en faire une règle. Pendant le blocage, la mutation a été vérifiée en reproduisant l'assertion du test en Play Mode par script — un repli acceptable, qui ne remplace pas de voir le test lui-même échouer.

### La cadence de lecture

Un arbre de mélange **ne modifie pas la cadence de ses clips**. À mi-vitesse il mélange l'attente et la course, mais la course joue à 100 % de sa cadence pendant que le corps n'avance qu'à moitié — les jambes s'agitent sans que le personnage suive. `SpeedMult` accorde donc la vitesse de lecture au déplacement réel.

🚨 **Plafonné à 1, et ce n'était pas évident.** Au-delà de l'allure nominale ce n'est pas la cadence qui doit monter mais le **clip** qui change : à `MoveY = 2` l'arbre joue `Sprint Forward`, déjà authorée pour sprinter. Une première version plafonnait à 1,5 et accélérait donc de 50 % une animation qui n'en avait aucun besoin.

Le plancher ne descend pas à zéro : le multiplicateur pilote l'état entier, animation d'attente comprise, qui doit continuer de respirer à l'arrêt.

**`standingPlaybackScale` / `crouchingPlaybackScale` / `pronePlaybackScale`** accordent chaque posture à ses clips, Mixamo n'authorant pas ses animations à l'échelle des vitesses de ce jeu. C'est un réglage de **ressenti**, à ajuster en jouant.

### Chute et franchissement chez le spectateur (2026-09-30)

`Airborne` et `Vaulting` sont désormais alimentés. Ils ne pouvaient pas l'être depuis `IsVaulting` ou `controller.isGrounded` : un **spectateur** n'appelle jamais `Move()` et son `CharacterController` n'est pas simulé, donc son `isGrounded` ne veut rien dire. Sans signal réseauté, un adversaire qui tombe ou franchit un obstacle garderait son animation de course.

Deux drapeaux, « au sol » et « en franchissement », publiés par le serveur **à chaque frame** — inputs traités ou non, exactement comme l'historique de pose. Sinon un drapeau resterait figé pendant une micro-coupure, et l'adversaire garderait son animation de chute après avoir atterri. Depuis le 2026-10-06, ils voyagent dans la **pose datée** (`DisplayPose.flags`) et non plus dans deux `NetworkVariable<bool>` : ils s'affichent ainsi au même instant que la position qu'ils accompagnent, au lieu de 100 ms avant.

**Catégorie C réseautée** : rien dans la simulation ne relit ces drapeaux, donc un client ne peut rien en tirer.

`DisplayVaulting` et `DisplayAirborne` font l'aiguillage, **même patron que `LeanOffset`** : celui qui simule (propriétaire ou serveur) utilise sa valeur locale, donc sans latence ; seul le spectateur lit la valeur réseautée. Le propriétaire voit ainsi son propre franchissement instantanément — ce qui compte pour son ombre, la seule partie de son modèle qu'il voit. `PlayerAnimator` ne connaît donc pas la notion de propriétaire et **ne peut pas se tromper de source**, ce qui est précisément l'erreur commise sur la posture.

🚨 **`DisplayAirborne` exclut explicitement le franchissement.** Un vault coupe le `CharacterController`, donc `isGrounded` y vaut false et le vault passerait pour une chute. S'en remettre à l'ordre des transitions de l'Animator marcherait aujourd'hui et casserait au premier réagencement.

**Les états `EnLAir` et `Vault` reviennent vers la posture RÉELLE**, pas vers `Debout`. La première version ne renvoyait que vers `Debout` : atterrir accroupi faisait clignoter la posture debout pendant 0,15 s. Défaut purement visuel, donc silencieux. `AnimatorStateTests` le couvre.

⚠️ **Dans un test PlayMode, attendre en TEMPS et non en FRAMES.** Atteindre `EnLAir` depuis l'état par défaut enchaîne DEUX transitions (0,20 s puis 0,15 s), une transition n'étant pas interruptible par défaut. Or les frames défilent bien plus vite qu'à 60 Hz en PlayMode : une première version attendait 40 frames, ce qui ne faisait même pas 0,35 s, et les trois tests échouaient dès leur première assertion — en accusant à tort le graphe, qui était correct.

### L'animation de vault — choisie par la mesure (2026-09-30)

Premier essai avec `Jumping Over Into Combat` : en jeu, **le personnage restait debout et passait par-dessus l'obstacle sans le moindre geste**. Ce n'était pas un état non atteint, mais un clip mal choisi.

En échantillonnant la hauteur des hanches sur toute la durée des clips candidats :

```
Jumping Over Into Combat (4,17 s)  0,00 -0,01 -0,03 -0,12 -0,18  0,39  0,20 ...
Jumping                  (1,17 s)  0,00  0,08  0,09  0,30  0,41  0,44  0,47  0,32 ...
```

Le franchissement de `Jumping Over Into Combat` culmine à **50 % du clip**, soit 2,08 s. Sur les 0,45 s d'un vault et à vitesse 1, on n'en voyait que les **11 premiers pour cent** — une zone parfaitement plate. `Jumping` porte au contraire un arc complet du début à la fin.

**La vitesse de lecture est pilotée par `VaultSpeed`**, calculé comme `vaultClipLength / PlayerLocomotion.VaultDuration`. Le geste tient donc toujours exactement dans la durée du franchissement, même si celle-ci est retouchée pour le ressenti — sans ce lien, l'animation se désaccorderait en silence.

**Leçon** : pour choisir un clip, mesurer où le geste utile s'y trouve. Un nom d'animation ne dit ni sa durée utile ni sa position dans le clip, et « le personnage ne s'anime pas » se lit à tort comme un problème de machine à états.

### La sonde de sol à l'arrivée d'un vault (2026-09-30)

Symptôme : en franchissant une barricade, le joueur **restait perché dessus** au lieu de retomber derrière.

`TryFindVaultTarget` posait l'arrivée à `topHit.point + forward * vaultLandingProbeDistance` — donc à l'**altitude du SOMMET** de l'obstacle, avancée de 60 cm. Aucune sonde de sol n'existait, alors que la variable s'appelait déjà `LandingProbeDistance` : elle était prévue, jamais écrite.

La géométrie explique tout : les barricades font **0,50 m de profondeur**, `topHit` tombe 0,15 m après la face avant, donc l'arrivée est 0,25 m derrière la face arrière. Mais la capsule a **0,35 m de rayon** : elle déborde encore de 10 cm au-dessus de l'obstacle, assez pour que le `CharacterController` y trouve du sol.

Corrigé par un raycast vers le bas depuis le point d'arrivée horizontal, **borné par `vaultMaxLandingDrop`** (1,5 m) : sans cette borne, franchir une barricade au bord d'un vide téléporterait au fond. Au-delà de la borne on garde l'arrivée haute et la gravité fait le reste, ce qui est le comportement sûr.

Le raycast ne porte que sur de la géométrie **statique**, donc `Move()` reste déterministe et les deux côtés calculent la même arrivée — la règle du vault réseauté est préservée.

**Couvert par `VaultParDessusUnObstacleFin_AtterritAuSolEtPasDessus`**, validé par mutation : sans la sonde, le joueur arrive à y = 0,80 m, exactement le sommet de l'obstacle de test.

### L'arc de vault franchit désormais l'obstacle (2026-09-30)

Le personnage **traversait la barricade** au lieu de passer dessus. `AdvanceVault` ajoutait un arc d'amplitude **fixe** (`vaultArcHeight`, 0,35 m) à une interpolation entre départ et arrivée. Tant que l'arrivée était posée au SOMMET de l'obstacle, c'était l'interpolation elle-même qui faisait monter le joueur et 0,35 m suffisait à l'habiller. Depuis que la sonde de sol pose l'arrivée AU SOL derrière l'obstacle, les deux bouts sont bas — et 0,35 m ne franchit plus rien.

**Les deux correctifs s'étaient annulés** : « perché dessus » avait été échangé contre « à travers ».

L'amplitude est désormais **élargie autant qu'il faut** pour que le point haut dépasse le sommet : `max(vaultArcHeight, vaultPeakY - min(départ.y, arrivée.y))`. La référence est le point BAS des deux extrémités et non leur moyenne — choix conservateur, qui garantit le franchissement même quand départ et arrivée sont à des hauteurs différentes.

🚨 **`vaultPeakY` est la CINQUIÈME valeur cumulative** lue par `Move()`, après le yaw, `currentVelocity`, la hauteur de capsule et l'état de vault. Elle suit donc la règle complète : confirmée par le serveur dans la RPC de correction, restaurée par `RestoreVaultState` avant tout rejeu, et présente dans `SimulationState`.

### 🚨 Correction d'une affirmation fausse de cette page

Il était écrit que « les tests de déterminisme échoueront d'eux-mêmes si la restauration est incomplète ». **C'était faux, et la mutation l'a prouvé** : en retirant la restauration de `vaultPeakY`, les 11 tests restaient verts.

La raison : `VaultRejoue_SuitExactementLeMemeArc` et ses voisins restaurent un état pris **AVANT** le franchissement. Le rejeu rappelle donc `BeginVault`, qui recalcule tout — une valeur non restaurée n'a aucune occasion de diverger. Ils ne testaient jamais une restauration **en plein vault**, qui est pourtant exactement ce que fait une réconciliation.

**`EtatDeSimulation_SeRestaureEntierement` comble le trou** : il prend une photo au milieu d'un premier franchissement, laisse un SECOND franchissement d'une hauteur différente écraser les valeurs, restaure la photo et compare champ par champ. Validé par mutation — sans la restauration de `vaultPeakY`, il échoue en annonçant 1,35 au lieu de 0,95, soit la hauteur d'arc du mauvais obstacle.

C'est lui, et lui seul, qui garde réellement l'invariant `SimulationState`. **Toute nouvelle valeur cumulative doit y être ajoutée**, sans quoi le prochain oubli passera de nouveau inaperçu.

### La couche haut du corps (2026-09-30)

Une seconde couche d'Animator, `HautDuCorps`, pilote le torse, les bras et la tête indépendamment des jambes, via `Assets/_ProjectArt/MasqueHautDuCorps.mask`.

**Ce qu'elle débloque** : tirer en strafant — les jambes jouent le déplacement pendant que le buste joue la visée ou le tir — et l'usage des animations génériques `Run Left/Right/Backward`, qui ne tiennent pas d'arme. Le masque les écrase au-dessus de la taille. Sans elle, il aurait fallu une animation par combinaison de direction, posture, visée et tir.

🚨 **La racine est EXCLUE du masque.** Le déplacement racine appartient à `Move()` ; laisser une couche d'animation y toucher rouvrirait exactement le défaut de dérive réglé la veille.

**Trois états, chacun aiguillé par la POSTURE** : `Arme` (arme basse), `Vise`, `Tir`. Un buste debout plaqué sur des jambes allongées serait grotesque, donc chaque état est un arbre 1D sur la posture, dont les seuils tombent sur 0/1/2 — un aiguillage exact, pas un mélange.

⚠️ **Un arbre de mélange n'accepte qu'un paramètre FLOAT.** `Stance` est un entier, parce que les transitions de la couche de déplacement le comparent exactement (`Equals`), ce qu'un float ne permet pas proprement. D'où **`StanceF`**, miroir flottant alimenté par le même pilote. Duplication imposée par Unity, pas un choix — et une erreur muette si on l'oublie : l'Editor refuse l'arbre avec « uses parameter which is not float type » et l'exécution des tests part en NullReference.

**Le tir passe par un ÉVÉNEMENT, pas par un sondage** (`WeaponController.OnShotFired`). Tirer est un instant, pas une condition qui dure : sonder un état raterait les coups tombant entre deux frames. L'événement est levé **des deux côtés** — chez le tireur depuis `Fire()`, chez tous les autres depuis `BroadcastShotClientRpc`, qui existait déjà pour le son et le tracer. **Aucun réseau supplémentaire n'a été nécessaire**, seulement un point d'accroche.

La transition `Tir → Tir` sur soi-même est volontaire : chaque coup relance le recul depuis le début. Sans elle, un tir arrivant pendant l'état serait ignoré et l'arme paraîtrait tirer une fois sur deux.

**La visée suit le même aiguillage que la posture** : `IsAiming` est affectée dans `Move()`, qu'un spectateur n'appelle jamais, donc l'adversaire n'épaulerait jamais. Le drapeau « en visée » de la pose datée + `DisplayAiming` referment le trou — troisième application du même patron, après la posture puis la chute et le franchissement.

🚨 **`BlendTree.useAutomaticThresholds` vaut TRUE par défaut, et RÉÉCRIT les seuils passés à `AddChild`.** Les seuils 0/1/2 d'un aiguillage par posture deviennent **0 / 0,5 / 1**, répartis uniformément. `StanceF = 1` — accroupi — désignait donc le TROISIÈME enfant, celui du prone.

Le symptôme en jeu : accroupi, le personnage avait **les bras pointés en l'air**. Un buste allongé (buste à 72° d'inclinaison) greffé sur des hanches accroupies. Et le tir accroupi jouait l'animation de tir couché.

**Rien ne le signale** : ni le compilateur, ni la console, ni les tests d'états — qui ne regardent que le nom de l'état, pas le clip réellement joué. `LeHautDuCorps_JoueLeClipDeLaPostureCourante` comble ce trou en lisant le clip DOMINANT de la couche via `GetCurrentAnimatorClipInfo`.

**Toujours poser `useAutomaticThresholds = false` avant d'écrire des seuils qui ont un sens.**

⚠️ **Le même piège dormait dans l'arbre `Prone` de la couche de DÉPLACEMENT**, corrigé seulement le 2026-10-05 (dette n° 22) : un joueur allongé immobile rampait sur place. Corriger un piège à un endroit ne dit rien des autres endroits où il peut se trouver : à chaque correction de ce genre, chercher toutes ses occurrences. Au 2026-10-05, les trois arbres 1D du contrôleur ont des seuils manuels ; ceux de `Debout` et `Accroupi` sont des arbres 2D, où seules les positions comptent.

⚠️ **Les clips accroupis de Mixamo ne sont pas tous la même posture.** Mesuré : `Rifle Kneel Idle` est un GENOU À TERRE (hanches à 0,38 m, bassin à +5°) tandis qu'`Idle Crouching Aiming` est un SQUAT (0,46 m, −14°). Le masque excluant la racine, greffer le buste de l'un sur les hanches de l'autre laisse une trentaine de degrés d'erreur. **Les deux couches doivent servir la même famille de pose** — c'est pourquoi l'attente accroupie de la couche de déplacement utilise elle aussi `Idle Crouching Aiming`.

⚠️ **Les deux couches doivent partager la même cadence** (`SpeedMult` sur `Arme` et `Vise` comme sur les états de déplacement). Quand elles jouent le MÊME clip — c'est le cas en prone, où les deux servent `Prone Idle` — une différence de vitesse les désynchronise en permanence et le personnage paraît bouger alors qu'il est immobile. Le tir garde sa cadence propre : c'est un geste ponctuel, pas une allure.

⚠️ **Limite connue, à éprouver en jeu** : le MP5 tire toutes les **143 ms** alors que les animations de tir durent 270 ms (debout), 430 ms (couché) et **1030 ms (accroupi)**. Elles sont authorées pour un tir visé isolé. Debout, relancer le geste à chaque coup devrait donner une pulsation de recul crédible ; accroupi, on ne verra jamais que les 14 premiers pour cent du clip. Si ça ne passe pas, la méthode est connue — mesurer où le recul se situe dans le clip, puis décaler l'entrée et accorder la vitesse, comme pour le vault.

## Configuration Git (posée le 2026-09-22)

`.gitattributes` couvre trois choses : normalisation des fins de ligne (`* text=auto`), **Unity Smart Merge** sur les fichiers YAML d'Unity, et **Git LFS** sur les binaires (audio, images, modèles 3D, vidéo, polices, DLL).

Configuration locale correspondante (dans `.git/config`, **pas** dans le global) :
- `git lfs install --local` — filtres LFS + hooks.
- `merge.unityyamlmerge.driver` → `UnityYAMLMerge.exe` de Unity 6000.6.0f1. **À repointer si la version d'Unity du projet change.**

Deux limites à connaître :
- **LFS ne s'applique qu'aux fichiers ajoutés ou modifiés après coup.** Les 7 `.wav` déjà commités restent des blobs normaux dans l'historique. Sans conséquence (dépôt à 1,8 Mo), mais si on veut les rapatrier : `git lfs migrate import --include="*.wav" --everything` — c'est une **réécriture d'historique**, triviale ici puisqu'il n'y a pas de remote, mais elle change tous les hashes de commit.
- `* text=auto` ne normalise que ce qui est touché ensuite. Pour l'appliquer à tout d'un coup : `git add --renormalize .` puis un commit dédié — ça touche tous les fichiers texte, donc à faire en isolation, jamais mélangé à un vrai changement.

Les `.asset` d'Unity sont du YAML texte (projet en Force Text) : ils passent par Smart Merge, pas par LFS. Si le projet basculait en Force Binary, il faudrait les déplacer côté LFS.

Le niveau de qualité `Mobile` et ses assets de rendu ont été retirés le 2026-10-05 : il ne reste que `PC`, utilisé par toutes les plateformes.

**Remote GitHub depuis le 2026-10-05** (`origin`, privé). Le CLI `gh` n'est pas installé ; l'authentification passe par Git Credential Manager (`credential.helper = manager`), qui ouvre une fenêtre de connexion chez l'utilisateur au premier push. Les objets LFS (~280 Mo au 2026-10-05) partent avec le push, via le hook installé par `git lfs install --local`.

⚠️ **Depuis qu'il y a un remote, réécrire l'historique n'est plus anodin** : la migration LFS des `.wav` évoquée plus haut demanderait un push forcé. À ne faire qu'avec l'accord de l'utilisateur.

## Ordre de travail

*(Livré le 2026-09-24 : le hitbox séparé, puis le rewind. **Avec eux, la Phase 1 n'a plus de chantier structurel ouvert** — le duel 1v1 est techniquement solide. Ce qui suit est du contenu et du flow, plus des fondations.)*

*(Livré le 2026-09-24 : la boucle de manches BO5. Voir la section dédiée plus haut.)*

*(Livré le 2026-09-28 : le vault réseauté. **Toutes les mécaniques du GDD sont désormais en place.**)*

*(Relecture complète et rectification le 2026-10-05 : voir « Relecture complète du 2026-10-05 » dans les dettes. Ordre ci-dessous refait à cette date.)*

1. ~~Poser des obstacles franchissables~~ — fait le 2026-09-29 (`Barricade 0.5` / `1.25` / `1.6`).
2. ~~Migrer le multijoueur vers `Arena.unity`~~ — **abandonné le 2026-09-29**, puis la scène a été supprimée le 2026-10-05.
3. **Rendre le duel lisible.**
   - ~~**Robot provisoire : Y Bot de Mixamo**~~ — **en place le 2026-10-05.** Le soldat SWAT reste comme avatar source des animations (voir « Le personnage et l'animation »).
   - **Couleur par joueur**, attribuée par le serveur (voir la section bible plus bas).
   - ~~**Lean façon R6 et hitbox par zones**~~ — **livrés le 2026-10-05**. Amplitude et vue accroupie validées en jouant le 2026-10-06. **Lean allongé refait en roulis** (« exactement ça ») et **transitions de posture** livrés et validés en jouant le 2026-10-06. L'inclinaison du lean et le retour droit, « encore un peu bruts et secs », sont amortis le même jour : à rejuger. Dettes ouvertes : le corps allongé qui dépasse du collider (n° 24), la vue du Host par à-coups (n° 30).
   - **Son** : clips à trouver, portée par allure (à la main de l'utilisateur, sans urgence).
4. **Compléter le duel** : chargeur et rechargement, **taser** de corps à corps qui tue en un coup et sort quand l'arme est vide (GDD § 6, tranché le 2026-10-05), marqueur et sons de touche, effet de dégâts reçus, vraie mort, HUD minimal, phrase de victoire. L'arme automatique du prototype reste le MP5 ou une arme d'apparence plus robotique (question ouverte au GDD § 17).
5. **Le modèle de session : N joueurs, deux duellistes.** Voir ci-dessous.
6. **Lobby avec code + Relay** (Unity Services), pour le playtest entre amis. C'est aussi ce qui sort : **des lobbys privés à code, et aucune file publique**. Le classé, et avec lui le serveur dédié, l'anti-triche client et le matchmaking par région, viennent après la sortie (tranché le 2026-10-04). Aucun service de matchmaking n'est donc à intégrer d'ici là.

### Ce que le questionnaire GDD change au plan (2026-10-02)

Le GDD a été refondu à partir de 86 réponses. Trois décisions ont des conséquences d'architecture :

**1. Un lobby de N joueurs, pas un duel à deux.** Deux duellistes s'affrontent pendant que les autres attendent dans une **tribune physique**, incarnés par un avatar à mains nues qui court, ramasse, lance des objets et se bat. Aujourd'hui, chaque client reçoit un joueur armé dans l'arène, et `RoundManager` suppose deux joueurs (voir sa section). Il faut une notion de **rôle** (duelliste / spectateur), attribuée par le serveur seul, et une rotation (roi de la colline par défaut). Le spectateur incarné n'est pas un duelliste désarmé : il n'a ni arme ni hitbox de duel, et ne doit jamais entrer dans l'arène. Ce chantier passe **avant** le Relay, parce que le premier playtest réunit 5 ou 6 personnes : à deux, on ne testerait ni la rotation ni la tribune.

**2. Le mode hôte est désormais acceptable hors classé.** Le GDD n'interdit plus l'avantage de l'hôte que pour le classé, qui tournera sur serveur dédié. Entre amis, le Relay suffit. Rien ne change dans le code (il ne suppose déjà pas que le serveur a un joueur, voir plus bas), mais le serveur dédié n'est plus un prérequis du premier playtest. Le Relay, lui, en devient un : les testeurs ne sont pas sur le même réseau.

**3. Les objets lancés par les spectateurs touchent les duellistes.** Cailloux et matériel seront des objets physiques **simulés par le serveur seul**, sur le modèle des dégâts. Le client demande un lancer (direction, force), le serveur le borne et le simule, jamais l'inverse. Le bruit d'un caillou qui tombe est un **événement sonore de catégorie B**, qui peut tromper un duelliste : c'est voulu par le GDD.

**Périmètre recommandé pour le premier prototype** (le jalon « tester entre amis » du GDD) : le mode classique, la rotation roi de la colline, la tribune avec des spectateurs qui regardent, et le Relay avec code. Les interactions des spectateurs (cailloux, dons, bagarre) arrivent après ce premier test : elles demandent un vrai système d'objets lancés, et le test dira d'abord si le duel lui-même tient. **Validé par l'utilisateur le 2026-10-02.**

### Ce que la bible Lore/DA implique techniquement (2026-10-04)

`Docs/Bible-Lore-DA.md` retient des **prototypes de soldats robotisés**, aux pièces rigides et articulations visibles, dans un complexe militaire souterrain désaffecté. Cinq conséquences techniques :

1. **Le travail d'animation survit au changement de modèle.** Un robot à pièces rigides reste un humanoïde : chaque pièce est parentée à un os (ou skinnée à 100 % sur un seul), le rig reste Humanoid, et les 30 clips Mixamo, les deux couches de l'Animator et `PlayerAnimator` se réutilisent tels quels. Seul le modèle sous `Model` change, et les trois conformités d'import plus haut restent obligatoires.
2. 🚨 **La tentation d'un collider par pièce sera forte : c'est exactement la règle « ne jamais dériver le hitbox des os animés ».** Un robot en segments rigides donne l'impression que chaque pièce *est* une hitbox, mais ces pièces suivent les os animés. Le hitbox reste calculé depuis les scalaires réseautés (`PlayerHitbox.Apply`) ; la correspondance visuelle se règle en dimensionnant les capsules sur les pièces, jamais en accrochant les colliders aux pièces.
3. **Les débris de mort sont cosmétiques, et doivent naître inoffensifs.** Un châssis qui vole en pièces implique des débris avec colliders, pour rebondir au sol. Ils doivent vivre sur un layer qui ne touche **ni** les `CharacterController`, **ni** `worldMask`, **ni** `obstacleMask` : sinon ils arrêtent des balles, poussent les joueurs ou faussent `CanStandUp()`, exactement comme le marqueur d'impact du 2026-09-24. Simulés localement sur chaque machine (catégorie C), ils n'ont pas la même trajectoire d'un écran à l'autre, donc rien de jouable ne doit en dépendre. Le fondu au noir entre deux manches est le moment naturel pour les nettoyer. Aucun ragdoll n'existe encore dans le code (vérifié le 2026-10-04).
4. **Aucun cosmétique ne touche au son** (tranché par l'utilisateur le 2026-10-04 : « les robots font tous le même bruit »). Tous les robots partagent exactement les mêmes sons : un futur système de cosmétiques ne doit avoir aucune prise sur `PlayerSoundEmitter` ni sur `OnPlayerSound` (catégorie B), pas même hors manche.
5. **Les formes cosmétiques restent dans le volume du hitbox.** Les cosmétiques changent la peinture et la *forme*. Une pièce qui dépasse du hitbox calculé (épaulière, antenne) se voit mais ne se touche pas, ce qui casse « on touche ce qu'on voit » ; une pièce qui le masque ment sur la cible. Toute géométrie cosmétique doit tenir dans les capsules de `PlayerHitbox` pour chaque posture et chaque lean.

**Capacité d'un lobby** (tranché le 2026-10-04) : elle dépend du mode. **4 joueurs pour le prototype** (2 duellistes + 2 spectateurs), confirmé même si le groupe de testeurs est plus grand : les autres attendent hors du jeu. Peut-être 6 spectateurs plus tard. Le modèle de session doit donc prendre la capacité comme un **réglage de partie**, jamais comme une constante : la coder en dur coûterait autant, et la faire évoluer coûterait plus.

**La couleur de chaque joueur est attribuée par le serveur dans le prototype.** Le GDD exige que chaque robot ait sa propre couleur, pour que les spectateurs reconnaissent les duellistes d'un coup d'œil, et la personnalisation n'arrive qu'après le prototype. Le serveur choisit donc une couleur vive, distincte des autres joueurs présents, et la publie dans une `NetworkVariable` en écriture serveur, sur le modèle du point de spawn : aucun client ne choisit la sienne. Purement visuelle (catégorie C), elle ne touche ni au hitbox ni au son. Quand la personnalisation arrivera, c'est cette même valeur qui portera le choix du joueur, validé par le serveur (couleurs vives seulement, d'après le GDD).

### Pourquoi le personnage passe avant le Lobby/Relay (tranché le 2026-09-29)

**Le report du réseau est peu coûteux, et c'est vérifiable.** L'autorité est déjà entièrement côté serveur : dégâts, position, posture, rewind, boucle de manches. Le Host n'est qu'un serveur qui possède en plus un joueur — le passage au serveur dédié retire un cas (le cas 1), il n'en réécrit aucun. Aucun code écrit d'ici là ne suppose que le serveur a un joueur, donc la dette n'enfle pas en attendant.

**Le report du personnage coûte cher, tout de suite.** Sur une capsule lisse, le joueur ne peut pas *voir* où se trouve une tête : les multiplicateurs par zone y seraient une devinette, ce qui contredit le pilier « lisibilité » du GDD. Or presque toutes les décisions de design encore ouvertes — TTK, générosité du lean, `roundTimeLimit` — se tranchent au ressenti, et un ressenti ne se juge pas sur des capsules. Tant que le jeu n'est pas lisible, les playtests ne peuvent pas produire de réponses fiables.

**Le placeholder suffit.** Ce qu'il faut n'est pas de l'art final mais des **proportions** (tête / torse / jambes) et une silhouette lisible. Un humanoïde générique les donne.

🚨 Rappel du garde-fou, qui devient actif à ce moment précis : **ne JAMAIS dériver le hitbox des os animés.** L'animation AFFICHE, le hitbox se CALCULE depuis les mêmes scalaires réseautés. La couture est `PlayerHitbox.Apply(...)`, qui produira plusieurs capsules au lieu d'une ; `PlayerLocomotion` et `WeaponController` n'ont pas à changer.

## Ce qu'il reste à valider (test Host + Client)

Le hitbox et le rewind ont été vérifiés en Play Mode : géométrie, layer, trigger, suivi du lean, tirs de contrôle qui touchent le corps penché et ratent l'ancien centre, et pour le rewind un déplacement de 3 m qui fait rater le tir sans compensation et le fait toucher avec — puis une restauration vérifiée dans les deux sens. Mais **le ressenti et le jeu à deux ne se vérifient qu'en jouant** — c'est un arbitrage qui revient à l'utilisateur.

Pour le rewind, le test qui compte : **preset `NetSim_Test150ms` actif (PAS le `Packet Delay` d'`UnityTransport`, qui est inerte — voir les pièges), viser un adversaire qui strafe**. Les tirs doivent toucher là où il est *affiché*, pas derrière lui. Et l'inverse à surveiller — c'est le prix du rewind, assumé par tous les FPS : en tant que cible, on peut désormais mourir *juste après* s'être mis à couvert. Si ça paraît excessif, c'est `maxRewindSeconds` qu'il faut baisser.

À tester depuis l'instance **CLIENT** :
1. **Peek en lean** — le test qui compte. Un adversaire qui penche derrière un angle doit être touchable sur le flanc qu'il expose, et *seulement* là. Tirer sur sa position « droite » (là où était l'ancien collider) ne doit plus rien faire.
2. **Tirs derrière une couverture** — un adversaire derrière une caisse ne doit rien prendre (la trace monde borne la trace hitbox).
3. **Tir sur soi-même** — impossible par construction, mais à confirmer : aucun dégât ne doit s'appliquer au tireur, même en lean appuyé.
4. **Postures** — accroupi et prone doivent être plus difficiles à toucher, proportionnellement à leur capsule. Le corps « claque » désormais entre postures au lieu de glisser : à confirmer que ça ne choque pas visuellement.
5. **Sous latence (100-150 ms)** — le lean d'un adversaire est en retard d'un RTT chez le spectateur ; le rewind historise le lean et compense ce retard pour le tir. Reste à juger si le retard *visuel* gêne en duel.
6. **Console** — aucun `[Serveur] Tir rejeté`, aucun warning `layer Hitbox mais aucun Health parent`.

Réglages si besoin : `positionReconciliationThreshold` / `yawReconciliationThreshold` sur `PlayerLocomotion`, `maxOriginDistanceFromPlayer` sur `WeaponController`.

## Vault réseauté (2026-09-28)

Le franchissement d'obstacle est **rebranché et intégré à la simulation déterministe**. Le déclenchement et l'avancement vivent dans `Move()`, pas dans `Update()` : c'est ce qui le rend prédit côté propriétaire, rejouable à la réconciliation, et identique côté serveur. Tant qu'il dure, il **remplace** le déplacement normal.

**Ce qui n'est PAS réseauté, et pourquoi.** Les points de départ et d'arrivée sont recalculés de part et d'autre par `TryFindVaultTarget`, qui ne dépend que de la position, de l'orientation, de la posture et de la géométrie **statique** du monde — tout ce qui est déjà déterministe. Les réseauter aurait alourdi la RPC de correction pour transmettre ce que les deux côtés savent calculer.

**Ce qui EST confirmé par le serveur** : `IsVaulting`, `vaultTimer`, `vaultStart`, `vaultEnd`, appliqués avant le rejeu (`RestoreVaultState`). Règle habituelle — tout état cumulatif lu par `Move()` doit pouvoir être recalé. Un **désaccord sur `IsVaulting` force un resync même si les positions coïncident** : sinon les deux côtés cesseraient de simuler la même chose au pas suivant.

Le `CharacterController` est coupé pendant l'arc — c'est tout l'intérêt, franchir ce que la collision refuserait. Son état est piloté comme une fonction de `IsVaulting`, jamais laissé à un appelant : un controller resté désactivé figerait le joueur pour la partie. **Vérifié en Play Mode : le joueur reste parfaitement touchable pendant tout l'arc**, controller coupé — c'était la condition qui bloquait cette réactivation, levée par le hitbox séparé.

Le kick caméra d'atterrissage passe par un drapeau (`vaultJustLanded`) consommé par `Update()` chez le propriétaire : le déclencher depuis `Move()` le rejouerait à chaque réconciliation.

**Déterminisme vérifié** : trois exécutions du même franchissement depuis le même état donnent une position finale identique **au bit près**.

⚠️ **Limite connue** : `TryFindVaultTarget` exige `controller.isGrounded`, qui vaut false juste après un repositionnement (réconciliation, spawn, début de manche). Un input de vault tombant exactement sur cette frame est ignoré. Le désaccord est rattrapé par le resync forcé sur `IsVaulting`, donc pas de désynchronisation — au pire un vault qui « ne passe pas » une fois de temps en temps. À revoir si ça se ressent en jeu.

Un obstacle n'est franchissable qu'entre `vaultMinHeight` (0,3 m) et `vaultMaxHeight` (1,3 m). Les caisses d'origine font 2 m et ne sont donc PAS franchissables — refus normal, pas un bug.

**Obstacles de test posés dans `MultiTestScene` (2026-09-29, par l'utilisateur)** : `Barricade 0.5`, `Barricade 1.25` et `Barricade 1.6`, nommées d'après leur hauteur. Les deux premières sont franchissables. **`Barricade 1.6` dépasse la limite de 1,3 m et est toujours refusée** : c'est un **test négatif délibéré**, confirmé en jeu le 2026-09-29. Vérifier qu'une borne refuse vaut autant que vérifier qu'elle accepte — c'est la moitié qu'on oublie, et la seule qui prouve que la borne existe vraiment. Ne pas la prendre pour un bug si le vault n'y répond pas.

**Les gardes `#if UNITY_EDITOR` sont prouvées par le build.** Une build du projet est passée sans erreur le 2026-09-29, compteurs de diagnostic et autopilote inclus. C'est la vérification qui compte pour ce fencing : une garde mal placée casse la build plutôt que de passer inaperçue. À refaire après tout ajout de code de diagnostic.

### Défauts trouvés au premier test à deux (2026-09-29, RTT ~318 ms) — corrigés

Le vault a fonctionné du premier coup côté client, mais les journaux ont révélé deux défauts que la vérification solo n'avait pas pu voir.

**1. La comparaison de `IsVaulting` ne se faisait pas à séquence égale.** Le test de prédiction comparait l'`IsVaulting` **courant** du client à la valeur confirmée par le serveur pour une séquence vieille d'un RTT. Le client étant en avance, il avait déjà fini son arc quand arrivaient les confirmations du milieu du franchissement : désaccord systématique et **faux**. Signature dans les journaux, très reconnaissable : **taux de resync à 27-36 % avec une erreur de position de 0,0 cm** — une prédiction qui se recale sans jamais se tromper.

C'est le piège « comparer à séquence ÉGALE » pris une **quatrième** fois, après le yaw, `currentVelocity` et la hauteur de capsule. `predictedVaulting` rejoint donc `predictedPosition`/`predictedYaw` dans `PendingInput`, avec la même obligation d'être **réécrit après un rejeu**.

**2. Le kick caméra d'atterrissage se rejouait.** Le drapeau `vaultJustLanded` était censé l'éviter, mais il est levé **depuis `Move()`** : chaque rejeu qui retraversait l'atterrissage le relevait. Combiné au défaut n°1, cela empilait jusqu'à une vingtaine de kicks pour un seul franchissement — **c'était la caméra qui saccadait à la réception**. Un drapeau `isReplayingInputs` (posé en `try/finally`) fait taire les effets cosmétiques pendant un rejeu.

🚨 **Règle qui en découle, pour toute mécanique future** : un rejeu de réconciliation re-simule du **déjà prédit**, donc du **déjà ressenti**. Tout effet de catégorie C déclenché depuis `Move()` doit se taire pendant un rejeu. Le drapeau seul ne protège de rien s'il est levé depuis le code rejoué.

**Observation à surveiller** : à 318 ms de RTT mesuré, le rewind suggéré atteignait 259 ms, proche du plafond `maxRewindSeconds` de 0,3 s. Au-delà, c'est la victime qui encaisse l'injustice. Si le ping de test monte encore, ce plafond devient le facteur limitant.

**Vérification après correctif (2026-09-29, tour + `Barricade 1.25` + `Barricade 0.5`, ~318 ms de RTT)** : **19 fenêtres consécutives à 0,0 % de resync**, soit ~4 000 corrections sans un seul recalage, console vierge. La signature « resync élevé / erreur nulle » a disparu. Caméra confirmée sans saccade par l'utilisateur.

Une chute depuis la tour coûte **une** resynchronisation isolée à ~8 cm d'erreur — la gravité amplifie l'écart de prédiction, le seuil de 5 cm est franchi une fois, le client se recale. Comportement normal d'un seuil qui fonctionne, déjà présent avant le correctif : ce n'est pas une régression. L'hypothèse d'un kick parasite déclenché en vol par le clignotement d'`isGrounded` lors d'un recalage est donc **écartée par la mesure**.

🚨 **Angle mort d'instrumentation, comblé.** Un taux de resync à 0 % pendant un vault est **indiscernable d'un vault qui n'a jamais eu lieu** : la validation ci-dessus reposait donc en partie sur la parole du testeur. `[DIAG-CLIENT]` porte désormais deux compteurs CUMULATIFS, `vaults=` et `kicks=`, qui tranchent les deux questions d'un coup — `vaults > 0` prouve que le franchissement a eu lieu, `kicks == vaults` prouve que le kick caméra ne se rejoue plus. Ils ne sont volontairement pas remis à zéro à chaque fenêtre, un vault et son atterrissage pouvant tomber de part et d'autre d'une bordure.

C'est la même leçon que pour le rewind, sous une autre forme : **une mesure doit pouvoir distinguer « ça marche » de « ça ne s'est pas produit »**. Sans ça, l'absence de symptôme se lit comme un succès.

## Historique : pourquoi le vault est resté désactivé six jours

**Résolu le 2026-09-28** — voir la section « Vault réseauté ». Ce qui suit est conservé pour mémoire du raisonnement, parce qu'il illustre une règle qui resservira : une mécanique qui touche au collider n'est pas qu'un problème de mouvement.

`HandleVaultInput()` et `ProcessVault()` étaient restés dans `PlayerLocomotion.cs` sans être appelés de nulle part — débranchés d'`Update()` pendant la fusion réseau. `IsVaulting` valait donc toujours `false`, et comme le vault était la seule chose branchée sur l'action Jump, **la touche de saut ne faisait rien**. Les deux fonctions ont été supprimées en réintégrant le vault ; leur logique de détection vit désormais dans `TryFindVaultTarget` / `AdvanceVault`, appelées depuis `Move()`.

Ce n'était pas une régression. La raison : le vault est un mouvement scripté à durée fixe qui fait `controller.enabled = false` pendant un lerp de position — un état « hors contrôle » incompatible avec le `Move()` par frame que la prédiction/réconciliation rejoue. Le réactiver demandait un vrai incrément réseau (vault prédit côté propriétaire + confirmé serveur), pas juste de rappeler la fonction.

### Le blocage est levé (2026-09-24)

Le hitbox séparé existe et **n'est plus lié au `CharacterController`** : vérifié en Play Mode, un joueur dont le `CharacterController` est désactivé reste parfaitement touchable. La fenêtre d'invulnérabilité de 0,45 s décrite ci-dessous n'existe donc plus.

Reste à faire pour le vault lui-même : l'intégrer à la simulation avec ses 4 états cumulatifs (`IsVaulting`, `vaultTimer`, `vaultStart`, `vaultEnd`), chacun devant avoir son équivalent confirmé renvoyé par le serveur avant rejeu — le patron est établi (yaw, `currentVelocity`). Sans rewind, toucher un joueur en plein arc restera peu fiable sous latence : c'est l'argument pour faire le rewind d'abord, mais ce n'est plus un blocage de sécurité, juste un ordre préférable.

### L'analyse qui l'avait reporté (2026-09-22, conservée pour mémoire)

Les fondations nécessaires existent désormais — `Move()` est strictement déterministe, et le pattern « valeur cumulative → équivalent confirmé renvoyé par le serveur » est établi et documenté. Le vault pourrait donc techniquement entrer dans la simulation.

Ce qui bloque n'est pas le mouvement, c'est **ce qu'il fait au collider** : `StartVault()` pose `controller.enabled = false` pendant 0,45 s. Or le `CharacterController` est aujourd'hui la surface de tir principale. Ajouter le vault maintenant reviendrait à livrer une mécanique qui, à chaque usage, retire du monde physique le collider par lequel on encaisse — en ne laissant que le `CapsuleCollider` accidentel de la dette n°8 pour rattraper le coup. Avec un TTK de 0,7 s, une fenêtre de 0,45 s déclenchable à volonté près de n'importe quel obstacle bas est un trou béant.

S'y ajoutent deux raisons de séquencement :
- Le vault introduit 4 états cumulatifs de plus dans la réconciliation (`IsVaulting`, `vaultTimer`, `vaultStart`, `vaultEnd`). Les câbler avant le hitbox/rewind, c'est les recâbler après.
- Sans compensation de latence, toucher un adversaire lancé sur un arc rapide serait très peu fiable — le vault mettrait en lumière le manque de rewind au pire endroit.

**Condition de déblocage** : un hitbox indépendant, toujours actif, qui ne dépend pas de l'état du `CharacterController`. À ce moment-là, désactiver le collider de mouvement pendant le vault n'aura plus aucun effet sur la capacité à être touché, et le vault redevient un incrément de mouvement ordinaire.
