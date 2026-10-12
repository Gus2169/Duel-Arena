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

**La forme retenue.** Le serveur publie à chaque frame une `DisplayPose` (`networkPose`) : **date sur son horloge** (`NetworkManager.ServerTime`), position, yaw, lean, **tangage du regard** (depuis le 2026-10-09, voir « Le buste suit le regard »), drapeaux d'animation (au sol, en franchissement, en visée), et un compteur de téléportation qui vide l'historique du spectateur à chaque nouvelle manche. Le spectateur interpole à `ServerTime − interpolationDelay` (`SampleDisplayPose`, fonction pure testée) : position et lean en ligne droite, rotation par le plus court chemin, drapeaux pris au début de l'intervalle, et **jamais d'extrapolation**. Deux variables réseau (la pose, la posture) remplacent les sept d'avant.

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

- `WeaponController.FireServerRpc` rejette : un tir plus rapide que `data.shotsPerSecond` (crédit de temps, voir plus bas), un tir hors manche, **un tir à vide ou pendant un rechargement** (voir « Le chargeur »), une direction nulle, et une **origine trop éloignée de la position serveur du tireur** (`maxOriginDistanceFromPlayer`, 4 m — couvre hauteur caméra + lean + avance de prédiction sous latence. Le rewind est en place et l'historique serveur couvre aussi le tireur, mais la validation de l'origine ne s'en sert pas encore : c'est ce qui permettrait de resserrer la borne).
- `PlayerLocomotion.ApplyBufferedServerInputs` clampe le `deltaTime` de chaque input et **budgète le temps simulé sur le temps RÉEL** (token bucket rechargé de `Time.deltaTime × 1.1`, réserve plafonnée à 0,25 s). `SubmitInputServerRpc` plafonne la taille de `serverInputQueue`.

**Deux règles qui en découlent, à appliquer à toute nouvelle RPC client→serveur :**

1. Aucune RPC n'accepte de **résultat** (hit, dégâts, position). Au mieux une *suggestion*, toujours clampée côté serveur.
2. Toute borne anti-abus s'exprime **par unité de temps réel, jamais par frame** — une borne par frame se contourne en gardant la queue pleine, puisque le serveur tourne à ~60 frames/s.

**Le garde-fou de cadence est un crédit de temps** (2026-10-08, `TryAcceptShotRate`, fonction pure). Chaque tir coûte un intervalle de tir, le crédit se recharge avec le temps réel jusqu'à 1,5 intervalle, et il faut 0,85 intervalle pour tirer. La cadence moyenne ne peut donc jamais dépasser celle de l'arme, mais un tir légitime retardé jusqu'à 0,65 intervalle (93 ms au MP5) ne fait plus refuser le suivant. L'ancienne règle exigeait 85 % de l'intervalle entre deux **arrivées** : un à-coup de plus de 21 ms côté serveur refusait un tir légitime (environ un sur cent à deux instances, mesuré le jour même : invisible jusque-là, il est apparu avec le chargeur, puisque la balle refusée « revenait » dans le compteur). Elle laissait aussi un client modifié tirer en continu 17 % trop vite ; un client modifié ne gagne plus que 0,65 intervalle au début d'une rafale. Gardé par `FireRateTests` (gigue de -30 à +60 ms sans refus, client qui inonde, client 13 % trop rapide), validé par mutation : l'ancienne règle fait échouer les quatre.

### Le chargeur (2026-10-08)

GDD § 6 : « chargeur limité, rechargement ». Réglages dans `WeaponData` : `magazineSize` (**30**), `reloadDuration` (**2,2 s**), `reloadSound`. Ce sont des valeurs provisoires de Claude, à juger en jouant (ressenti **et** équilibrage : le rechargement est la fenêtre où l'adversaire peut pousser). Touche **R** (action `Reload` de `PlayerControls`).

**Le serveur tient le compte qui fait foi** (`MagazineState` dans une `NetworkVariable` en écriture serveur : balles, fin du rechargement en cours sur l'horloge du serveur, nombre de demandes traitées). Il refuse tout tir à vide ou pendant un rechargement, et décide seul du début et de la fin d'un rechargement : `ReloadServerRpc` ne transmet aucun instant. On ne tire pas en rechargeant ; le taser, la mise en course et le franchissement interrompent un rechargement (voir « Le taser », « Le sprint face au rechargement et au taser » et « La sortie de sprint »). Chargeur plein et rechargement annulé à l'apparition et à chaque début de manche (`ServerRefillMagazine`, appelé par `RoundManager.BeginRound`).

**Le tireur prédit le même compte**, pour tirer et recharger sans attendre l'aller-retour. Sa fin de rechargement est prédite sur `NetworkManager.LocalTime`, l'instant où le serveur recevra la demande : elle tombe donc au même instant que celle que le serveur calcule à réception. Le serveur accorde 50 ms de marge à un tir qui arriverait juste avant (`ReloadCompletionTolerance`, la gigue du réseau) : sans elle, le premier tir après un rechargement serait parfois refusé alors que le joueur l'a vu partir.

**Le recalage compare à demandes égales** (`ReconcileMagazine`, fonction pure) : le tireur compte ses demandes (tirs et rechargements), le serveur publie combien il en a traité, acceptées ou non. Tant qu'une demande est en route, l'état publié est en retard et ne se compare pas — c'est le piège « comparer à séquence ÉGALE », appliqué au chargeur. Une fois tout accusé, le serveur fait foi, sauf s'il recharge encore : le tireur qui a fini quelques millisecondes avant lui garde son chargeur plein, au lieu de voir son compteur clignoter à 0. Ce qui se recale ainsi : un tir refusé (cadence, hors manche), un rechargement refusé, un début de manche.

**Le rechargement s'entend** (catégorie B) : le propriétaire le joue en appuyant, les autres quand la `NetworkVariable` annonce un rechargement. Le son vient de `WeaponData.reloadSound`, comme le son de tir : il dépend de l'arme, donc il ne passe pas par `PlayerSoundBank`. **Aucun clip pour l'instant** : le rechargement est muet (dette n° 33). L'**animation**, elle, est en place depuis le 2026-10-08 (voir « Rechargement, taser et strafe visé ») : l'adversaire voit qu'on recharge.

**Le HUD provisoire** (`CrosshairUI`) affiche le compteur en bas à droite (en rouge à vide), une barre sous le réticule pendant le rechargement et « R : recharger » quand le chargeur est vide. Il est désormais réservé au joueur local : chaque joueur affiché dessinait jusqu'ici son propre réticule, superposé au centre de l'écran, donc invisible.

**Tests** : `MagazineTests` (15) couvre les règles du serveur, le recalage, et un **scénario complet sous latence** : rafale de 30, rechargement, cinq tirs de plus, avec 75 ms dans chaque sens. Aucun tir refusé, compteur qui ne remonte qu'à la fin du rechargement, comptes d'accord à la fin, y compris quand la demande de rechargement prend 30 ms de retard sur les tirs. Validé par trois mutations : recalage pendant des demandes en route (le tireur tire alors une balle de trop, refusée), marge nulle, recalage pendant que le serveur recharge.

**Vérifié à deux instances** (client en tir automatique contre un Host rendu invulnérable, 45 s) : 8 rechargements, prédiction égale au compte du serveur à chaque relevé, **aucun tir refusé pour le chargeur**. Deux recalages, tous deux au démarrage et expliqués : un tir parti au moment où la manche passait en décompte, et un refus de cadence pendant l'à-coup de démarrage du Host. En tuant le Host, la manche suivante a rempli le chargeur du client pendant le gel (`recalé de 28 à 30`).

### Le taser (2026-10-08)

GDD § 6 : le corps à corps est un **taser qui tue d'un coup**, une **action brève au choix du joueur**, comme le couteau rapide de *Call of Duty* (précisé par l'utilisateur le 2026-10-08 : il ne sort plus quand l'arme est vide). Touche **V** (action `Melee`). `TaserController`, sur l'objet `Weapon` à côté de `WeaponController`.

**Même autorité que le tir.** Le joueur demande un coup (`StrikeServerRpc` : origine, direction, instant affiché) ; le serveur seul décide qui est touché. Garde-fous : un coup au plus par geste en temps réel (`strikeDuration`, tolérance de 15 %), pas hors manche, pas d'attaquant mort, origine à moins de 4 m du joueur, rewind borné à 0,3 s.

**Le coup part au contact, pas à l'appui** : `contactDelay` (0,18 s) après, quand le bras est tendu dans l'animation. Le serveur résout donc le coup en différé, contre la pose des adversaires à l'instant du contact tel que l'attaquant le voyait (`viewServerTime + contactDelay`, même clamp que le tir), avec une origine gardée relative au joueur, qui continue de bouger. **Un attaquant tué pendant ces 0,18 s ne frappe pas** : sans ce délai, un coup mortel sans le moindre geste serait illisible et sans parade.

**La touche** (`QueryStrike`, testée par de vrais colliders) : un volume balayé de 2 m depuis l'œil et 0,3 m de rayon, par **chevauchement** (`OverlapCapsule`) et non par balayage — un `SphereCast` ignore les colliders dans lesquels il commence, donc un adversaire collé à soi serait intouchable. Le plus proche l'emporte ; rien derrière l'attaquant ; la ligne entre l'œil et le point touché ne doit traverser aucun mur. Mort instantanée : `ApplyDamage(∞)`.

**Pendant le geste (0,6 s), on ne tire pas, et un rechargement en cours est annulé** — chez le joueur tout de suite (`CancelLocalReload`), sur le serveur à réception (`ServerCancelReload`). Un rechargement à moins de 50 ms de sa fin compte comme fini (même marge que pour un tir). Le recalage du chargeur rattrape l'écart entre les deux sans qu'il faille compter le coup comme une demande : tant que le serveur recharge encore, le joueur garde sa prédiction.

**Valeurs provisoires de Claude, à juger en jouant** : `strikeDuration` 0,6 s, `contactDelay` 0,18 s, portée 2 m, rayon 0,3 m. Aucun son (`strikeSound` vide), aucun effet visuel.

**Le taser est un gant à décharge** (2026-10-11, modèle fourni par l'utilisateur, référence *Fallout*), porté sur l'avant-bras gauche, et le geste est un direct du poing gauche (`Taser Punch` de Mixamo) : voir « Le gant du taser et la tenue de pistolet ».

**Vérifié à deux instances** (client en coup automatique, Host placé à 1,1 m) : `[Serveur] Taser : Player(Clone) neutralisé (rewind 120 ms)` — 100 ms d'affichage plus le délai de contact. Et le Host voit le geste du client, bras gauche tendu, fusil gardé à la hanche.

### Le sprint face au rechargement et au taser (2026-10-10)

GDD § 7, demandé « comme les FPS connus » : sprint vers l'avant seulement (`PlayerLocomotion.IsSprintDirection`, ≤ 50° de l'avant : les diagonales du clavier passent, un stick presque de côté non) ; **la dernière action l'emporte** : recharger ou frapper en sprintant arrête le sprint, se mettre à courir pendant un rechargement l'annule, et le coup de taser va à son terme sans sprint (`GateSprint`, fonction pure testée). « Se mettre à courir », c'est l'intention de sprint qui NAÎT : une touche tenue depuis avant le rechargement ne l'annule pas.

**Décidé chez le propriétaire, hors de `Move()`** : `ReadLocalInput` envoie `sprintHeld` déjà filtré, donc `Move()` reste une fonction du seul snapshot et aucune valeur cumulative ne s'ajoute à la réconciliation. L'annulation locale passe par `WeaponController.CancelLocalReload`.

**Le serveur vérifie sans rien demander au client** : les demandes de rechargement et de taser voyagent par RPC fiables, livrées dans l'ordre d'envoi, donc après tous les inputs produits avant elles. Le serveur retient le numéro du premier input postérieur (`ServerNextInput`) et annule le rechargement, ou fait rater le coup de taser, si un input simulé depuis voulait sprinter (`ServerSprintedSince`). Un sprint d'avant la demande, même traité plus tard, ne compte pas. Le Host numérote ses inputs comme un client. Un client modifié qui sprinterait en rechargeant perd son rechargement sans rien y gagner.

**Vérifié à deux instances le 2026-10-10**, avec l'exercice scripté du banc de test (`autodrill`, voir « Banc de test ») : un cycle de 8 s où le client recule en tirant, recharge puis se met à courir (le rechargement doit être annulé), recharge en courant touche tenue (il doit aller au bout), puis frappe au taser en courant (le coup ne doit pas être annulé). Sur une minute environ :
- **Client honnête** : 18 rechargements, 9 annulés en se mettant à courir, **9 annulations chez le joueur et 9 sur le serveur**, aucun recalage du chargeur (hors le tir de démarrage connu), aucun coup de taser annulé, et le rechargement commencé en courant va au bout.
- **Client tricheur** (`cheatsprint` : il sprinte sans respecter `GateSprint` et n'annule rien) : le serveur annule **ses 15 rechargements** et **ses 8 coups de taser** (`[Serveur] Coup de taser annulé`). Le tricheur finit à 0 balle, prédiction d'accord avec le serveur : il n'y gagne rien.

L'ordre des RPC fiables tient donc entre les deux composants (`PlayerLocomotion` et `WeaponController` / `TaserController`, sur le même `NetworkObject`).

### Reculer et aller de côté, moins vite (2026-10-10)

Demandé par l'utilisateur, « comme beaucoup de FPS » (GDD § 7). `PlayerLocomotion.DirectionSpeedMultiplier`, fonction pure de l'input testée, appliquée dans `Move()` : toute la vitesse jusqu'à 45° de l'avant (les diagonales avant du clavier, sprint compris), puis décroissance jusqu'à **`strafeSpeedMultiplier` (×0,85) plein côté** et jusqu'à **`backwardSpeedMultiplier` (×0,7) en arrière**, sans saut d'une direction à sa voisine. Valeurs provisoires de Claude, à juger en jouant : ressenti **et** équilibrage (vitesse d'un peek en pas chassé, vitesse à laquelle on rompt un engagement). Écrites explicitement dans le prefab.

Pure fonction de l'input, donc identique en prédiction, sur le serveur et au rejeu : aucune valeur cumulative de plus. L'affichage suit : `PlayerAnimator` divise la vitesse par une référence elle aussi réduite selon la direction, donc reculer en marchant reste « la marche » pour l'arbre de mélange, et la cadence des jambes se cale sur la vitesse réelle. `LocomotionFootTests` et `FootSlideTests` appliquent la même réduction. Gardé par `ReculerEtAllerDeCote_SontPlusLentsQuAvancer` (vitesse atteinte dans `Move()`), validé par mutation.

### La sortie de sprint, et le rechargement face au franchissement (2026-10-11)

Demandé par l'utilisateur : « quand je cours et que je tire, le sprint s'arrête instantanément et le tir part direct après. Je voudrais que la course mette plus de temps pour s'arrêter, et ensuite qu'on ait la possibilité de tirer, pareil pour la recharge » — pour que courir engage (GDD § 7).

**Deux mécanismes, chacun réglable :**
- **Le délai de tir et de rechargement** : `WeaponData.sprintToFireTime` (**0,3 s**, propre à chaque arme) entre la fin d'un sprint et le premier tir ou le début d'un rechargement. Un rechargement demandé pendant ce temps attend (`WeaponController.ReloadPending`) et part dès que possible ; en attente, il arrête le sprint comme un rechargement en cours. Le taser n'est pas concerné (le coup part tout de suite, comme le couteau de CoD).
- **L'élan** : `PlayerLocomotion.sprintDeceleration` (**25 m/s²**) au-dessus de l'allure de marche, dans `Move()` (10 → 5 m/s en 0,2 s au lieu de 0,06). La vitesse se décompose selon la direction où le joueur VEUT aller : cette composante se règle comme d'habitude, et ce qui reste de la course s'éteint lentement sans jamais être réorienté. 🚨 Deux premières versions freinaient le long du mouvement lui-même : l'élan tournait avec lui et finissait sur le côté, **7,6 m/s de côté** en lâchant le sprint, un peek deux fois plus rapide qu'un pas de côté normal (3,74). Le test l'a attrapé. On tourne en sprintant aussi vite qu'avant (cible et vitesse égales : chemin normal).

**Le temps se compte sur les inputs, pas sur une horloge.** Le joueur somme la durée (bornée à `MaxSingleInputDeltaTime`, comme sur le serveur) des inputs qu'il a envoyés depuis le dernier qui voulait sprinter (`TimeSinceSprint`, `AdvanceInputClock`, fonction pure testée) ; le serveur fait la même somme sur les inputs qu'il a REÇUS, à la réception (`ServerTimeSinceSprint`). Une demande de tir ou de rechargement voyage par RPC fiable, après tous les inputs envoyés avant elle : au moment où le serveur la juge, les deux sommes portent sur exactement les mêmes inputs. Aucune gigue du réseau n'y entre, pas même une perte de paquet (qui rapprocherait les arrivées sans changer la somme). `FireServerRpc` et `ReloadServerRpc` refusent en deçà de `sprintToFireTime − 0,01 s` (`SprintOutTolerance`, l'arrondi).

**Vérifié à deux instances le 2026-10-11** (exercice `autodrill`, enrichi d'un tir juste après la course et d'un rechargement demandé en courant) :
- **Client honnête**, une minute : premiers tirs à **300 ms pile** de la fin du sprint, acceptés ; rechargements en attente partis à 300-305 ms ; **aucun tir ni rechargement refusé**, aucun recalage du chargeur.
- **Client tricheur** (`cheatsprint`, qui ignore aussi la sortie de sprint) : **4 tirs refusés** (partis à 4, 5 et 149 ms) et **7 rechargements refusés**, son chargeur se recale.

⚠️ **Limite connue** (dette n° 44) : un client modifié qui gonfle la durée de ses inputs raccourcit la sortie de sprint (chaque input compte au plus 0,1 s). Le budget de simulation du serveur l'empêche d'en tirer un déplacement, pas un tir plus tôt. À borner avec l'anti-triche du classé.

**On ne recharge pas en franchissant un obstacle.** Un rechargement demandé pendant un franchissement attend la réception ; franchir pendant un rechargement l'annule, chez le joueur (`CancelReloadIfVaulting`, après sa simulation, jamais dans `Move()`) comme sur le serveur (`ServerVaultedSince`, même principe que le sprint : un input simulé en franchissement après la demande annule le rechargement). Vérifié le 2026-10-11 en Host (voir « Pas de tir ni de taser en franchissant ») : le serveur annule le rechargement commencé avant un franchissement (`rechargement annulé par le franchissement (serveur)`). **On ne tire pas et on ne frappe pas non plus en franchissant** (tranché par l'utilisateur le même jour).

**La vue subjective le montre** : en sprint, l'arme est portée en travers (`FirstPersonView.sprintOffset`, `sprintEuler`) et revient exactement pendant la sortie de sprint (`WeaponController.SprintRecovery`), le temps où l'on ne peut pas tirer. Vérifié à l'image.

### La panoplie : quatre armes, touches 1 à 4 (2026-10-11)

Demandé par l'utilisateur, qui a fourni les modèles : « intègre-les et fais des WeaponData cohérents », en prenant *Rainbow Six* et *Call of Duty* pour références (mémoire « Références R6 et CoD »). Arbitrages de l'utilisateur : chaque joueur choisit librement son arme (touches 1 à 4) en attendant la règle du pile ou face du GDD ; le raygun est un pistolet à tir instantané ; headshots façon CoD, arme par arme ; le sniper vise en lunette plein écran et tire juste à la hanche, mais sans réticule ; les armes pèsent légèrement sur la vitesse.

**Les armes** (`WeaponController.loadout`, quatre `WeaponData` ; le fichier `MP5.asset` garde son nom) :

| Arme | Dégâts | Zones | Tir | Chargeur | Portée, baisse | Sortie, poids |
|---|---|---|---|---|---|---|
| Fusil d'assaut | 17 | tête ×1,4 | auto, 7/s | 30, 2,2 s | 100 m, aucune | 0,6 s, ×0,95 |
| Sniper | 100 | tête ×1,5, jambes ×0,6 | coup par coup, 0,8/s | 5, 3 s | 150 m, aucune | 0,8 s, ×0,9 |
| Fusil à pompe | 18 × 8 plombs | aucun bonus | coup par coup, 1,1/s | 6, cartouche par cartouche (0,55 s) | 35 m, ×0,3 de 8 à 20 m | 0,65 s, ×0,95 |
| Raygun | 26 | tête ×1,5 | coup par coup, 4,5/s | 12, 1,6 s | 60 m, ×0,75 de 15 à 35 m | 0,4 s, ×1 |

Valeurs provisoires de Claude, à juger en jouant. Le sniper sort aussi plus lentement d'un sprint (0,45 s), le raygun plus vite (0,2 s).

**Ce que `WeaponData` porte en plus** : `modelPrefab`, multiplicateurs de zone, baisse linéaire avec la distance (`DamageAt`, `RangeMultiplier`), **gerbe à motif fixe** (`pelletPattern` en degrés, resserrée en visée par `aimPatternScale`, GDD § 3 : aucun hasard), rechargement cartouche par cartouche (`reloadPerRound` : tirer l'interrompt), temps de sortie (`raiseTime`), poids (`moveSpeedMultiplier`), temps d'épaulement (`aimTime`), grossissement de lunette, réticule à la hanche, couleur du traçant. Les dégâts d'un tir s'additionnent par cible et par plomb, puis s'appliquent une fois (`AddDamage`).

**Le chargeur, par arme** : `MagazineState` porte une balle par place (`ammo0..3`, `ammo` désignant l'arme en main), l'arme en main (`equipped`) et l'instant où elle est sortie (`readyAt`). Changer d'arme (`TrySwitchWeapon`, prédit, puis `SwitchWeaponServerRpc`) annule le rechargement, garde les balles de chacune et interdit de tirer avant la sortie. Le recalage reprend arme et chargeurs une fois tout accusé.

**Le tir au coup par coup** lit l'appui dans l'Input System (`FireWasPressedThisFrame`, avec 0,1 s de mémoire) : c'est ce qui referme la dette n° 32.

**Le poids des armes** est simulé (catégorie A) : l'arme de l'input (`MovementInputSnapshot.weaponSlot`) multiplie la vitesse visée dans `Move()`. Le serveur ne croit pas celle qu'annonce le client : il fixe lui-même l'arme de chaque input, à partir de l'input qui suit la demande de changement (`ServerScheduleWeaponSlot`, décidé à la réception de la RPC). Gardé par `ChaqueArme_RalentitLeJoueurDeSonPoids`, validé par mutation.

**L'affichage** : chaque arme a son préfab (`FusilSniper`, `Pompe`, `Raygun`, et `FusilAssaut`), avec un `WeaponModel` (mire, bouche, distance de l'œil en visée, tenue de pistolet), poignée à l'origine et canon vers +Z. `HeldWeapons.Build` les crée dans la main droite au lancement, avec le décalage du fusil de référence, avant que la vue subjective ne copie le modèle ; une seule est visible (`HeldWeapons.Show`). L'arme qu'on vient de prendre remonte à l'écran pendant sa sortie (`FirstPersonView.raiseOffset`, `raiseEuler`). Le HUD affiche sa place et son nom au-dessus des munitions. **La lunette du sniper** : passé 95 % de l'épaulement, le champ se resserre de ×4 (`PlayerCameraLook.ZoomedFov`), la sensibilité baisse d'autant (`FirstPersonView.LookScale`, appliquée au regard avant qu'il n'entre dans l'input), l'arme disparaît et le HUD dessine la lunette (`CrosshairUI.DrawScope`). Gardé par `WeaponLoadoutTests` (12) et `HeldWeaponsTests`.

**Les modèles fournis** n'ont pas été modifiés : le sniper avait des pièces détachées posées à côté (retirées dans `Sniper_Nettoye.asset`) et pas de lunette (une lunette simple, en cylindres, lui a été ajoutée) ; le pompe utilisait un matériau d'un ancien moteur de rendu (converti pour URP) ; le raygun n'avait pas de texture (reprises de sa version glTF).

**Vérifié à deux instances le 2026-10-11** (client en tir automatique visé qui fait tourner sa panoplie toutes les 6 s, `autoswitch` ; Host en va-et-vient dans le champ, rendu quasi invulnérable) : 131 tirs, 113 touches, dégâts conformes (fusil 17, raygun 26 et 25 au-delà de 15 m, sniper 100, pompe 17 à 22 à 16 m, tête et buste), prédiction du déplacement à **0 % de recalage** malgré le poids qui change avec l'arme, chargeurs d'accord. 🚨 **La mesure a trouvé un défaut** : le serveur gardait le crédit de cadence de l'arme précédente, et le premier tir du sniper, parti 0,8 s après une rafale de fusil (à la fin de sa sortie), était refusé (intervalle de 1,25 s). Changer d'arme remet désormais le crédit à plein (`ResetShotRateOnSwitch`), comme le joueur remet son délai à zéro : c'est la sortie de l'arme qui borne le premier tir. Changer d'arme coûte au moins 0,4 s, on n'en tire pas de cadence. Gardé par `ChangerDArme_LePremierTirDeLaNouvelle_NAttendQueSaSortie`, validé par mutation ; après correction, plus aucun refus en changeant d'arme. Le seul refus restant tombe pendant l'à-coup de démarrage du Host, cas déjà connu.

### La file ordonnée des demandes du joueur, côté serveur (2026-10-11)

Tirs, rechargements, changements d'arme et annulations par le taser passent par une file, traitée dans l'ordre (`WeaponController.ServerProcessArmsActions`). Chaque demande retient, à son arrivée, le dernier input reçu (`inputSequence`) et tout ce qui dépend de l'instant d'arrivée (horloge du chargeur, temps réel de la cadence, sortie de sprint) ; elle attend que cet input ait été **simulé** (`PlayerLocomotion.ServerLastSimulatedInput`) avant d'être jugée, 0,3 s au plus. Pourquoi : le serveur juge « franchissait-il ? » sur l'état qui suit cet input, relevé à chaque simulation dans un anneau de 256 inputs (`ServerRecordSimulatedInput`, `ServerInputFlags`). La file est vidée à l'arrivée de chaque demande, après chaque simulation d'input et à chaque image.

**Mesuré à deux instances** : **tous** les tirs attendent leur input (131 sur 131 : les RPC arrivent avant la simulation de l'image), mais aucun n'attend une image de plus, sauf pendant l'à-coup de démarrage du Host (9 tirs, 49 ms en moyenne). Le rewind et la cadence utilisant les instants d'arrivée, cette attente ne change aucun résultat. `[DIAG-SERVEUR]` le rapporte (`en_attente_de_l_input`, `differes_d_une_image`, `attente_moy`).

### Pas de tir ni de taser en franchissant un obstacle (2026-10-11)

Tranché par l'utilisateur : « bloque le tir pendant le vault, pareil pour le taser ». Chez le joueur, ni tir ni coup pendant un franchissement (`locomotion.IsVaulting`). Sur le serveur, un tir dont l'input de référence franchissait est rejeté (`[Serveur] Tir rejeté : le joueur franchissait un obstacle.`), et un coup de taser rate si le joueur a franchi entre sa demande et sa résolution (`ServerVaultedSince`, même principe que le sprint).

**Vérifié en Host** avec l'exercice du franchissement (`PlayerInputReader.AutopilotVaultDrill` : avance, saute toutes les 0,3 s, tire, face à `Barricade 1.25`) : en tricheur (`PlayerLocomotion.DebugVaultIgnoresRules`), **4 tirs rejetés** par le serveur pendant le franchissement et le chargeur recalé ; en honnête, 43 tirs acceptés, **aucun rejet**. Le Host passe par la même file que les clients ; l'ordre des RPC à deux instances, lui, est vérifié par la mesure de la file ci-dessus. Le taser n'a pas été vérifié ainsi (il faut un adversaire à portée).

### Tester les garde-fous

Quatre interrupteurs de triche simulée, entourés de `#if UNITY_EDITOR` (impossible à embarquer dans une build). Les deux premiers sont dans l'inspecteur, sous le header `Debug — triche simulée` :

- `WeaponController.debugFakeShotOrigin` — décale l'origine envoyée au serveur de 50 m. Attendu : aucun dégât + un warning `[Serveur] Tir rejeté` par tir.
- `PlayerLocomotion.debugFloodServerInputs` — renvoie le même input 10× par frame. Attendu : le joueur n'avance PAS plus vite, il se fait ramener en arrière.
- `PlayerLocomotion.DebugSprintIgnoresRules` (statique, réglé par le banc de test, `cheatsprint`) — le client sprinte en rechargeant et en frappant sans rien annuler, et tire ou recharge sans attendre la sortie de sprint. Attendu : `[DIAG-CHARGEUR] rechargement annulé par le sprint (serveur)`, `[Serveur] Coup de taser annulé`, `[Serveur] Tir rejeté : sortie de sprint` et `[Serveur] Rechargement refusé : sortie de sprint`.
- `PlayerLocomotion.DebugVaultIgnoresRules` (statique, `cheatvault`, 2026-10-11) — le joueur tire et frappe en franchissant un obstacle. Attendu : `[Serveur] Tir rejeté : le joueur franchissait un obstacle.` Marche aussi en Host.

**Ils ne marchent que depuis une instance CLIENT distante**, jamais le Host : en Host, le perso local passe par le cas 1 et ne traverse ni la queue d'inputs ni la RPC de tir.

## Pièges déjà rencontrés (ne pas les re-découvrir)

- **Valeur RELATIVE/cumulative dans `Move()`** → doit avoir un équivalent « valeur confirmée » renvoyé par le serveur et appliqué AVANT le rejeu des inputs non confirmés, sinon dérive à chaque correction. Déjà arrivé deux fois : le yaw (dérive en double) et `currentVelocity` (désaccord permanent en accel/décel). Vaut pour toute future extension de `Move()`.
- **Comparer une prédiction, c'est comparer à séquence ÉGALE.** Le seuil de réconciliation compare ce que le serveur confirme pour une séquence donnée à ce que le client avait prédit **pour cette même séquence** (`PendingInput.predictedPosition`). Comparer à la position *actuelle* du client n'aurait aucun sens : elle est en avance de tout le RTT, donc l'écart y est toujours grand. Corollaire : après un rejeu, les prédictions stockées doivent être **réécrites** avec les nouvelles valeurs, sinon la correction suivante se base sur des données périmées.
- **Tout script qui lit clavier/souris doit avoir une garde `IsOwner`** en tête d'`Update()`/`LateUpdate()` — sinon chaque instance locale réagit à la souris physique (caméras qui bougent sur 2 écrans, `AudioListener` en surnombre, tirs pour tout le monde). `PlayerInputReader` ne connaît volontairement pas la notion de propriétaire : c'est aux consommateurs de se garder.
- **JAMAIS d'instance du PlayerPrefab posée dans une scène réseau**, même désactivée. Netcode ré-ACTIVE de force les `NetworkObject` in-scene désactivés côté client — c'est écrit noir sur blanc dans `NetworkSpawnManager.cs` (« if it is disabled then enable it so NetworkBehaviours will have their OnNetworkSpawn method invoked »). Symptôme vécu le 2026-09-22 : un joueur fantôme apparaissait sur le CLIENT au moment de sa connexion, au point de spawn, invisible pour le Host et absent de la hiérarchie de l'Editor — avec sa caméra et son `AudioListener`, d'où le warning des 2 audio listeners. **Un joueur se spawne par `NetworkManager.PlayerPrefab`, jamais en le posant dans la scène.** Même méfiance pour tout autre `NetworkObject` in-scene désactivé.
- **La caméra de secours de la scène doit être éteinte au démarrage d'une session.** Deux caméras plein écran actives à la même `depth` = la scène rendue DEUX FOIS par frame (et un `AudioListener` de trop). Depuis le 2026-10-12, c'est `ArenaFallbackCamera` qui s'en charge, posée DIRECTEMENT sur la caméra de secours de `MultiTestScene` — pas `NetworkBootstrapUI` (qui a quitté cette scène avec le `NetworkManager`, voir « Le menu principal », et ne peut plus retrouver une caméra qui vit dans une autre scène après un changement de scène), ni `PlayerLocomotion` (sa garde ne voit que les enfants du Player, et cette caméra n'en est pas un). Cause identifiée le 2026-09-22 d'un Host qui ramait par rapport au Client.
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
- 🚨 **Changer la valeur par défaut d'un champ NOUVEAU ne change rien si l'Editor a rechargé le code entre-temps** (vécu le 2026-10-09). Un champ `[SerializeField]` ajouté prend sa valeur par défaut dans le prefab **chargé en mémoire** au premier rechargement de domaine, et ce prefab la garde aux rechargements suivants, sans que le fichier change. `maxTorsoYaw` passé de 80 à 120 dans le code restait à 80 en jeu, alors que le fichier du prefab ne contenait même pas le champ. Dès qu'un réglage nouveau compte, **l'écrire explicitement dans le prefab** (`LoadPrefabContents` → `SerializedObject` → `SaveAsPrefabAsset`) et vérifier qu'il y figure. Même famille que « les valeurs du prefab priment sur les valeurs par défaut du code ».
- ⚠️ **Après une chute rapide, le `CharacterController` peut s'arrêter DANS sa marge de peau** au lieu de s'y poser : dans un test, le même placement donnait 0 cm après une chute de 1,5 m et 8 cm après une pose douce. Un test de hauteur doit partir d'une pose douce, comme une apparition.

## Structure du projet

**Prefab joueur unique** : `Assets/_ProjectArena/PlayerPrefab/Player.prefab`. Sur la racine : `CharacterController`, `NetworkObject`, `PlayerLocomotion`, `PlayerInputReader`, `CrosshairUI`, `WeaponVisualFeedback`, `PlayerSoundEmitter`, `Health`, `AudioSource`, `PlayerAnimator`, `PlayerPaint`, `FirstPersonView`. Enfants : `CameraPivot` (`PlayerCameraLook`) → `Leanpivot` → `Main Camera` ; `Weapon` (`WeaponController`, `TaserController`) ; `Hitbox` (`PlayerHitbox`, layer 7) ; `Model` (le personnage et son `Animator` ; le fusil visible est accroché à sa main droite, avec son `WeaponSight`, voir « L'arme visible » et « Le viseur intégré », et les autres armes de la panoplie y sont créées au lancement, voir « La panoplie » ; le gant du taser est posé sur l'avant-bras gauche, main gauche repliée, voir « Le gant du taser » ; `PlayerAnimator` y ajoute au lancement un `AnimatorRootMotionProbe`, voir « Les jambes », et cale ses pieds au sol par `FootPlanter`, voir « Aucune glissade »). Chez le propriétaire seulement, `FirstPersonView` en accroche une copie sous la caméra (voir « La vue subjective »). Référencé par GUID dans `NetworkManager.PlayerPrefab` et `Assets/DefaultNetworkPrefabs.asset`.

**Deux scènes depuis le 2026-10-12** (voir « Le menu principal ») : `Assets/_ProjectScenes/MainMenu.unity` (démarrage, index 0 des Build Settings, porte désormais le `NetworkManager`) et `Assets/_ProjectScenes/MultiTestScene.unity` (jeu, index 1, chargée par le serveur au moment d'Héberger — ce n'est plus elle qui porte le `NetworkManager`). UnityTransport, port 7777 par défaut ; l'adresse (écoute et connexion) est configurée au runtime par le menu, 127.0.0.1 n'étant plus qu'une valeur de repli dans l'UI. `MultiTestScene` utilise le terrain `Assets/New Terrain 1.asset` — malgré son nom, **il est utilisé**.

`Arena.unity` (la scène d'origine, gardée pour mémoire), son terrain `New Terrain.asset` et `CubeTest` + `TestSpin.cs` ont été **supprimés le 2026-10-05**, avec l'accord de l'utilisateur. L'historique Git les garde.

**Placeholders assumés** (à remplacer avec le vrai système d'armes / le vrai HUD, pas des bugs) : `CrosshairUI` (réticule OnGUI), `WeaponVisualFeedback` (tracer/impact procéduraux). `RoundManager` garde aussi son `OnGUI` de test (score/décompte), voir sa section. `PreviewAttachmentFactory` (accessoires de l'armurerie du menu, en primitives). `NetworkBootstrapUI` n'a plus de boutons depuis le 2026-10-12 (remplacés par le vrai menu, voir « Le menu principal ») : il ne reste plus que les logs de connexion et le banc de test à deux instances.

**Le jeu n'utilise que `PlayerControls.inputactions`** (classe C# générée, dans `DuelArena.Input`). L'asset d'actions *globales* du gabarit Unity (`InputSystem_Actions`) a été retiré le 2026-10-05 : il était chargé dans la build sans servir à rien. Les touches actuelles (sprint sur Alt, sneak sur Shift) sont la configuration personnelle de l'utilisateur ; le GDD (§ 15) prévoit des touches personnalisables.

⚠️ **Piège de nommage** : `/Spawn` et `/Spawn Enemy` dans `MultiTestScene` **ne sont PAS des points d'apparition** — ce sont des groupes de géométrie ProBuilder (caisses, cubes) situés *dans* les zones de spawn. Les vrais points d'apparition sont sous `/SpawnPoints`.

### Points de spawn

`PlayerSpawnPoints` (sur `/SpawnPoints`) liste un `Transform` par point : sa **position** donne les pieds du joueur, sa **rotation Y** la direction du regard à l'apparition. Un `OnDrawGizmos` dessine une capsule debout et une flèche, pour vérifier d'un coup d'œil qu'un point n'est pas dans un mur.

Deux points posés dans l'arène : `Spawn A` en (0, 0, 2) face au +Z, `Spawn B` en (0, 0, 58) face au −Z — les deux extrémités, à ~56 m l'une de l'autre, validés au sol et dégagés pour une capsule debout.

`PlayerLocomotion.ServerMoveToSpawnPoint()` est **serveur uniquement** (aucun client ne choisit son point, même principe que pour les dégâts) : il retient le point le plus **éloigné des joueurs déjà présents**, remet l'inertie à zéro, puis publie position et yaw. Appelé depuis `OnNetworkSpawn`, il est **public exprès** : la boucle de round (BO5) devra replacer les deux joueurs à chaque manche et pourra l'appeler tel quel.

Sans composant dans la scène ou sans point renseigné, les joueurs apparaissent à la position du prefab — le comportement d'avant, pas une erreur.

## Le menu principal (2026-10-12)

Demandé par l'utilisateur : lancement de partie (Héberger/Rejoindre, sélection de mode), personnalisation (robot, armurerie), options (graphismes, remap des touches, sensibilité), quitter — « rajoute tout ce que le projet a besoin si j'ai oublié quelque chose ».

### Deux scènes, un NetworkManager qui voyage

Le jeu démarre maintenant sur `MainMenu.unity` (index 0 des Build Settings), qui porte le `NetworkManager` (`UnityTransport`, `NetworkSimulator`, `NetworkBootstrapUI`) — déplacé depuis `MultiTestScene` (index 1, désormais une scène de jeu pure, sans `NetworkManager`). Ce déplacement s'appuie sur un comportement de Netcode vérifié dans le code du package : `NetworkManager.Awake()` s'applique `DontDestroyOnLoad(gameObject)` tant qu'il est à la **racine** de sa scène (`NetworkManagerCheckForParent()`), donc il survit de lui-même à n'importe quel changement de scène — aucun script maison n'a été nécessaire pour ça.

**Héberger** : `NetworkManager.StartHost()`, puis **immédiatement** `NetworkManager.SceneManager.LoadScene("MultiTestScene", LoadSceneMode.Single)` (`MainMenuController.OnHostClicked`). C'est le patron standard de Netcode pour une scène de bootstrap/menu — le joueur du Host, déjà spawné dans `MainMenu` par `StartHost()`, est migré par Netcode dans la nouvelle scène active (mécanisme documenté dans son propre changelog : les `NetworkObject` dynamiquement spawnés pendant un chargement `Single` sont basculés en DontDestroyOnLoad le temps du chargement, puis remigrés dans la scène qui vient de charger). **Vérifié en Play Mode** : transition propre, aucune erreur console, joueur armé (fusil d'assaut) et HUD de `RoundManager` fonctionnel (« En attente d'un adversaire... ») une fois dans `MultiTestScene`.

🚨 **Le joueur du Host naît dans la scène du MENU**, avant que l'arène ne soit chargée : sans sol ni point d'apparition, il y tombait dans le vide pendant le chargement, et arrivait dans l'arène à **−2 519 m** sous le terrain, où il continuait de tomber jusqu'à ce qu'un adversaire arrive (le début de manche replace tout le monde). La vérification ci-dessus ne pouvait pas le voir : le HUD et l'arme s'affichent normalement, seul le décor manquait. Corrigé le 2026-10-12 : `RoundManager.OnNetworkSpawn` (serveur), qui s'exécute quand l'arène est chargée, place les joueurs déjà présents à un point d'apparition. Vérifié : le Host arrive à `Spawn A` (0 ; 0 ; 2), au sol. Un client, lui, naît directement dans l'arène.

**Rejoindre** n'a besoin d'aucun appel à `LoadScene` : `EnableSceneManagement` est actif sur le `NetworkManager` (déjà vrai avant ce changement), donc une fois connecté, le client reçoit et charge automatiquement la scène que le serveur a chargée.

**Adresse et port** : le Host écoute sur `0.0.0.0` (toutes les interfaces, via `UnityTransport.SetConnectionData(adresseLocale, port, "0.0.0.0")`) et affiche son IP locale (`Dns.GetHostEntry`) à donner aux amis sur le même réseau — c'est ce qui permet de jouer en LAN sans Relay, en attendant l'étape 6 de l'ordre de travail. Le champ Rejoindre prend IP + port, `127.0.0.1:7777` par défaut (comportement du prototype, inchangé si rien n'est saisi). Pas de résolution d'erreur réseau poussée : un bouton « Annuler » pendant une tentative (`NetworkManager.Shutdown()`), un message si la connexion échoue (`OnClientDisconnectCallback` avant tout succès).

### UI Toolkit — premier usage du projet

Le projet n'avait jusqu'ici que de l'OnGUI, explicitement en placeholder (voir plus haut). Le menu, lui, est durable : `Assets/_ProjectUI/MainMenu/` (`MainMenu.uxml`, `MainMenu.uss`, `KeyBindRow.uxml`, `MainMenuPanelSettings.asset` — `ScaleWithScreenSize`, référence 1920×1080) et `Assets/_ProjectScripts/UI/` (`GameSettings.cs`, `MainMenuController.cs`, `ArmoryController.cs`, `OptionsController.cs`, et pour l'aperçu 3D `CharacterPreviewController.cs`, `WeaponCustomizationController.cs`, `PreviewStage.cs`, `PreviewAttachmentFactory.cs`, `AttachmentCategory.cs`, `PreviewLayer.cs`), tous sur le GameObject unique `MainMenuUI` de la scène, sauf `PreviewStage` (voir « Le robot en 3D et l'armurerie avancée »). Navigation par classe USS `.hidden`, aucune référence Inspector à câbler entre écrans : chaque contrôleur retrouve ses éléments par nom (`root.Q<T>("nom")`) dans son `OnEnable`. Posera les conventions du futur HUD UI Toolkit (voir plus haut).

🚨 **`:last-child` est interdit en USS** (même famille que `:first-child`/`:nth-child`) et **`-unity-font-definition: none` est invalide** — les deux ont été écrits par réflexe CSS puis corrigés après un passage en Console (classe explicite `.host-join-column--last` pour le premier, ligne retirée pour le second).

🚨 **`UnityTransport.SetConnectionData` force une référence à `Unity.Networking.Transport`**, même quand l'overload utilisé (`string, ushort, string`) ne touche jamais à `NetworkEndpoint` : un AUTRE overload de la même méthode en dépend, et le compilateur doit pouvoir résoudre TOUS les overloads pour choisir le bon (`CS0012`). Ajouté aux références de `DuelArena.Runtime.asmdef`.

### Personnalisation : une couleur qui reste une préférence, une arme qui s'applique vraiment

**La couleur n'est PAS branchée au serveur**, volontairement : `PlayerPaint` reste seul maître de l'attribution (voir sa section), conformément à la décision déjà actée de reporter la personnalisation après le prototype. `ArmoryController` stocke le choix dans `GameSettings.PreferredColorIndex` (PlayerPrefs), affiché comme sélection, avec un texte expliquant que ce n'est pas encore pris en compte.

**La palette du menu est celle de `PlayerPaint`, lue sur `Player.prefab`** (`CharacterPreviewController.Palette`, qui lit `PlayerPaint.Palette` sur l'asset sans l'instancier). Elle était d'abord recopiée dans `ArmoryController`, en dette : une copie divergeait en silence le jour où la palette changeait. Corrigé le 2026-10-12, même principe que les pièces à peindre de l'aperçu.

**L'arme de départ, elle, s'applique réellement.** `GameSettings.PreferredWeaponSlot` est consommé UNE SEULE FOIS, à l'apparition du joueur, par `WeaponController.HandleSwitchInput` — exactement le même chemin authentifié que les touches 1 à 4 (prédiction locale + `SwitchWeaponServerRpc`, mêmes garde-fous serveur). Aucune confiance nouvelle n'est demandée : c'est un déclenchement différé du geste existant, pas un raccourci qui contourne le serveur.

### Le robot en 3D et l'armurerie avancée (2026-10-12)

Demandé par l'utilisateur : « un visuel 3D sur le personnage avec les changements en temps réel (on peut le faire tourner sur lui-même) », et pour les armes « une fenêtre avec des rubriques accessoires, comme une création de classe façon *Call of Duty Cold War* : poignée, viseur, laser, grand chargeur, avec changements en temps réel sur le visuel de l'arme ».

**Le plateau** (`PreviewStage`, GameObject `/PreviewStage` de `MainMenu`, à l'origine du monde) : une caméra dédiée qui ne voit que le layer **`Preview` (9)** (la caméra du menu l'exclut), deux pivots (robot, arme), deux lumières. Une seule caméra pour les deux écrans, jamais visibles ensemble. Elle dessine dans une texture **créée à l'exécution aux dimensions en pixels du cadre UI** qui l'affiche (`GeometryChangedEvent`, `panel.scaledPixelsPerPoint`), recadrée à chaque changement de taille : une texture carrée fixe laissait des bandes noires dans des cadres en portrait ou en paysage. La caméra ne tourne que quand un sujet est montré ET que sa texture existe (sans texture, une caméra active dessinerait par-dessus le menu). La rotation à la souris est câblée une fois par le plateau sur chaque cadre ; le robot tourne seul au repos (14°/s), l'arme reste de profil, canon à droite, comme dans l'armurerie de CoD.

**Le cadrage** fait tenir le sujet en hauteur ET en largeur quelle que soit sa rotation (rayon horizontal, pas la seule largeur de face). 🚨 **Les bornes d'un `SkinnedMeshRenderer` ne mesurent pas le robot** (boîte fixe de 2 m × 1,8 m, prévue pour toutes les poses : le robot apparaissait deux fois trop petit) : il se mesure par ses os (de `HeadTop_End` aux orteils, + 8 cm), après un `Animator.Update(0)`, sinon on mesurerait la pose de repos bras en croix. Les particules sont ignorées (bornes vides à l'origine).

**Le robot** (`CharacterPreviewController`) : instancie l'enfant `Model` de `Player.prefab` (jamais la racine : un `NetworkObject` détruit hors session fait lever une exception dans Netcode), peint ses pièces par `MaterialPropertyBlock`, aux mêmes renderers que `PlayerPaint.PaintedRenderers` lus sur l'asset. La couleur suit le choix en temps réel. Elle est retenue si elle arrive avant la construction du modèle : l'ordre des `OnEnable` entre `ArmoryController` (qui envoie la couleur initiale) et ce composant n'est pas garanti, et le robot restait gris au premier affichage. Il ne joue que l'état par défaut de l'Animator, sans les corrections de `PlayerAnimator` (buste, pieds) : un aperçu, pas une simulation.

**L'armurerie avancée** (`WeaponCustomizationController`, écran `screenWeaponCustomize`, ouvert par « Personnaliser les armes » sur l'arme de départ) : les 4 armes, 4 rubriques (Poignée : courte, verticale ; Viseur : point rouge, holographique ; Laser ; Chargeur étendu), le choix appliqué à l'arme affichée au clic. L'arme est centrée sur son milieu (bornes dans SON repère via `Renderer.localBounds`, indépendantes de la rotation du plateau), et le cadrage couvre la place de tous ses accessoires possibles, laser excepté : il ne bouge pas quand on en change, et un chargeur étendu ne sort pas du cadre.

**Les points d'accroche** sont des enfants vides du préfab de chaque arme, sous `Accessoires` : `Poignee`, `Viseur`, `Laser`, `Chargeur`. Leur position place l'accessoire, leur rotation l'oriente (le chargeur du raygun suit sa crosse inclinée de 16°), leur échelle le dimensionne (chargeur de pistolet court, chargeur du sniper large). **Une arme sans point pour une rubrique ne l'a pas** : l'onglet est grisé (le raygun, un pistolet, n'a pas de poignée avant). Posés le 2026-10-12 à partir du profil mesuré des maillages (rendus orthographiques quadrillés), vérifiés à l'image de profil et de trois quarts ; réglables à la main dans le préfab, sans code. Vides, ils ne coûtent rien aux armes tenues en jeu. Le fusil d'assaut a déjà une poignée et une lunette modélisées : la poignée ajoutée se place en avant, le viseur sur le rail avant.

⚠️ **Purement cosmétique et limité au menu** : les accessoires (`GameSettings.GetAttachment`, PlayerPrefs, par place de la panoplie et par rubrique) ne changent rien à l'arme tenue en match, ni pour soi ni pour les autres. Les y appliquer demanderait de réseauter le choix (catégorie C réseautée, comme la peinture) et de toucher `HeldWeapons` et `FirstPersonView` : chantier à part. Aucun accessoire n'a d'effet de jeu non plus (le GDD n'en prévoit pas pour l'instant). La géométrie des accessoires est un **placeholder** (`PreviewAttachmentFactory` : des primitives, un matériau par couleur mis en cache), à remplacer par de vrais modèles. Le viseur ajouté ne masque pas la lunette d'origine du sniper.

### Options : sensibilité, graphismes, remappage des touches

`GameSettings` (statique, PlayerPrefs) centralise tout : sensibilité, résolution/plein écran/vsync/plafond d'images, et les overrides de bindings de l'Input System (`SaveBindingOverridesAsJson` / `LoadBindingOverridesFromJson`, sur `InputActionAsset`). Catégorie C stricte — rien de tout ça n'est lu par la simulation ou le serveur.

- **Sensibilité** : `PlayerCameraLook` multiplie son `mouseSensitivity` (réglage de base, dans l'inspecteur) par `GameSettings.SensitivityMultiplier` — un ajout en bout de chaîne, après le calcul existant, qui ne touche à rien d'autre.
- **Graphismes** : résolution (liste de `Screen.resolutions`), plein écran, vsync, plafond d'images/s (Illimité/60/120/144/240), appliqués immédiatement ET persistés. **Pas de sélecteur de qualité** : une seule existe (`PC`, voir dette n°19), l'exposer n'aurait aucun effet.
- **Remappage** : liste GÉNÉRIQUE construite depuis l'action map `Gameplay` (19 lignes : les 4 directions de `Move`, qui est une composite, + 15 actions simples) — aucune ligne à ajouter à la main si une action s'ajoute à `PlayerControls.inputactions`, à condition de l'ajouter aussi au dictionnaire de libellés (`ActionLabels`/`MovePartLabels` dans `OptionsController`), sinon elle est volontairement absente (cas de `Look`, dont le remappage n'aurait aucun sens). Remappage par `InputActionRebindingExtensions.PerformInteractiveRebinding`, sur une instance LOCALE et jamais activée de `PlayerControls` (on ne fait qu'inspecter/modifier ses bindings). `PlayerInputReader.Awake()` recharge les mêmes overrides sur SA PROPRE instance (`GameSettings.ApplyBindingOverrides(controls.asset)`) — nécessaire puisque chaque `new PlayerControls()` déserialise une copie indépendante de l'asset généré.

⚠️ **Aucune détection de conflit** (deux actions sur la même touche sont acceptées silencieusement) et **aucun menu pause en cours de partie** (ces réglages ne sont accessibles qu'avant de rejoindre une manche) : limites connues, à lever si le besoin se confirme en playtest.

### Vérifié le 2026-10-12

**109 tests EditMode et 58 tests PlayMode toujours verts** après les trois modifications de scripts de gameplay (`PlayerCameraLook`, `PlayerInputReader`, `WeaponController`) — aucune régression, cohérent avec le fait qu'aucun des ajouts ne touche `Move()` ou un chemin réseauté existant.

**En Play Mode (Host, une instance)** : écran titre, « Jouer » (cartes de mode, BO5 actif et seul cliquable, trois grisées), Héberger → transition vers `MultiTestScene`, joueur armé, HUD de manche actif, console vierge. Personnalisation (couleurs + 4 armes nommées depuis les vrais assets `WeaponData` de la panoplie) et Options (Graphismes avec la résolution détectée, Commandes avec 19 lignes de remap et le clavier AZERTY affiché correctement — `Z`/`Q`/`S`/`D` pour `Move`, l'Input System résout la touche physique selon la disposition) capturés à l'écran et conformes.

**Le robot 3D et l'armurerie avancée** (même jour, reprise de la session) : **110 tests EditMode** (dont `ChaqueArme_ASesPointsDAccroche_SaufLaPoigneeDuPistolet`, validé par mutation : un point retiré du sniper le fait échouer en le désignant) et **58 PlayMode** verts. Capturés en Play Mode : le robot remplit son cadre, peint à la couleur initiale puis au clic ; les 4 armes de profil avec tous leurs accessoires, la rubrique Poignée grisée sur le raygun, l'arme tournée de trois quarts qui reste dans le cadre ; la transition Héberger → arène avec le plateau dans la scène du menu (texture libérée, console propre).

**Non testé** : le glissé à la souris par un vrai pointeur (la rotation a été vérifiée en appelant la même méthode que le glissé) ; le flux Rejoindre à deux VRAIES instances (le banc de test à deux instances existant continue de fonctionner sans changement, voir « Tester sous latence » — son `autoClient` appelle `StartClient()` directement, sans passer par le menu) ; un remappage fait au clavier par un vrai appui (la chaîne `PerformInteractiveRebinding` a été vérifiée démarrer sans exception, pas le clic + appui bout en bout par un humain).

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

L'affichage `OnGUI` du `RoundManager` est un **placeholder** au même titre que `CrosshairUI`, à remplacer par le HUD UI Toolkit — le menu principal, lui, est déjà en UI Toolkit (voir « Le menu principal »), ce futur HUD pourra en reprendre les conventions (dossier, `PanelSettings`, patron de navigation par classes `hidden`).

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
11. 🟡 **Largement corrigé le 2026-10-12** (voir « Les sons de tir, de pas et le taser »). `PlayerSounds.asset` a désormais des clips pour `FootstepRun`, `FootstepCrouch`, `LeanStart`, `LeanEnd`, `CrouchDown`, `CrouchUp`, `ProneDown` et `ProneUp` (tous muets avant) ; le fusil d'assaut ne tire plus au hasard parmi des sons sans rapport. **Restent, à la charge de l'utilisateur et sans urgence** : `Crawl` toujours muet (aucun bon candidat libre de droits trouvé pour une reptation), la portée par allure, et la seule `AudioSource` à 20 m qui sert à toutes les allures dans une arène de 66 × 74 m (« la course s'entend de partout » reste impossible). Les tests de son vérifient le *mapping*, pas l'audibilité : ils restent verts. Historique du diagnostic d'origine (2026-10-05) : le sneak et le ramper sont classés `Faint` (nouvelle valeur de `NoiseLevel`) et joués à volume réduit (`PlayerSoundEmitter.faintVolumeScale`, 0,35) au lieu d'être coupés ; gardé par trois tests, validés par mutation.
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

**Découverts en posant le chargeur (2026-10-08)** :

32. ✅ **Corrigé le 2026-10-11** — le tir au coup par coup lit l'appui dans l'Input System (`FireWasPressedThisFrame`), voir « La panoplie ». Le diagnostic : 🟡 **Un appui « one-shot » peut être perdu selon l'ordre d'exécution.** `PlayerInputReader` pose `FirePressedThisFrame` (et les autres appuis) par callback, et c'est `PlayerLocomotion` qui les remet à zéro en fin d'`Update`. Or l'ordre entre `PlayerLocomotion` et `WeaponController` n'est pas fixé : si la locomotion passe d'abord, une arme **semi-automatique** ne verrait jamais l'appui. Latent, le MP5 étant automatique (`FireHeld`). La touche de rechargement évite déjà le piège (`WasPressedThisFrame`, lu dans l'Input System) ; à faire de même pour le tir au coup par coup avant d'ajouter une telle arme.
33. ✅ **Corrigé le 2026-10-12** — les quatre armes ont désormais un `reloadSound` (voir « Les sons de tir, de pas et le taser »). Historique : l'animation de rechargement était en place depuis le 2026-10-08 (clip fourni par l'utilisateur, voir « Rechargement, taser et strafe visé »), l'adversaire voyait donc qu'on recharge, debout, accroupi et, depuis le 2026-10-10, allongé (chacun son geste), mais le son manquait.

**Découverts en posant l'arme visible et le taser (2026-10-08)** :

34. 🟡 **Son et effet ajoutés le 2026-10-12** (voir « Les sons de tir, de pas et le taser ») : `TaserController.strikeSound` est câblé, et `GantChoc.prefab` porte désormais des étincelles électriques (`TaserSparkFX`, catégorie C pure) jouées au contact. **Reste** : allongé, le taser n'a toujours pas de geste (la couche du taser reste à 0 : l'estocade est authorée debout, son bras partirait dans le sol). Sa portée de 2 m dépasse aussi le bras tendu (70 cm) : le coup touche avant que le poing n'arrive, comme le couteau rapide de CoD avec sa fente — à juger en jouant.
35. ✅ **Corrigé le 2026-10-08** — le joueur voit ses bras et son arme (voir « La vue subjective »). Le diagnostic : le fusil est sous `Model`, donc en `ShadowsOnly` chez le propriétaire comme le reste du corps.
36. ⚪ **Le fusil pèse 63 600 sommets** (205 pièces fusionnées en un mesh de 9 matériaux). Sans conséquence à deux joueurs ; à alléger (LOD, décimation) avant d'en afficher davantage.

**Découverts en calant les pieds et le buste (2026-10-09)** :

37. ✅ **Résolu le 2026-10-11** : l'anneau lent debout joue les huit marches du Y Bot (voir plus bas). Historique : **Il manquait des clips de marche lente.** La liste complète des gestes, avec ce que le projet utilise et ce qui manque, est la page [Animations Duel Arena](https://claude.ai/artifact/VHunU2FqQ8XUoMHxSbZRM3) (2026-10-09, 61 gestes) : l'utilisateur y note le clip Mixamo retenu pour chacun, que Claude relit par `ArtifactData` (collection `choix`, un document par geste : `etat`, `clip`). Le sneak et la visée debout jouent, vers l'avant, la course visée (`Rifle Run Aim`, 2,8 m/s) lue à ×0,36 à ×0,84, et vers l'arrière la course arrière (4,5 m/s) lue à ×0,23 à ×0,68 : pieds calés, mais au ralenti. Une marche visée avant et arrière (Mixamo, sur le Y Bot) le corrigerait sans code : la cadence se mesure seule. **Allongé, rien ne joue de côté** (seulement la reptation avant/arrière) : le corps glisse en pose d'attente.
    **Premier lot reçu le 2026-10-10** : 31 clips sur le Y Bot, dans `Animations/New`, passés aux règles d'import (Humanoid, avatar du Y Bot, bouclage). **Tous téléchargés « In Place »** (aucun mouvement des hanches à l'import) : ceux qui se déplacent sont à retélécharger avant de rejoindre l'Animator (voir « Conformité d'import »). La base est le pack « 8 directions » de Mixamo, déjà source des clips du soldat : vitesses de racine standard (marche accroupie 2,00 m/s, course 4,72, sprint 7,07, mesurées sur ces derniers), marche estimée à ~2 m/s par les pieds, soit le sneak debout. Ses gauches et droites sont de **vraies prises, pas des miroirs** : en tireur droitier, les hanches restent tournées vers la droite en allant à gauche (+52° en course, +75° en marche accroupie). Les marches « Rifle Sneak » (~0,6 m/s, cycle de 2 s) sont trois fois trop lentes pour le sneak du jeu (2 m/s). Introuvables : la reptation avant et latérale, la course accroupie, les transitions debout → accroupi et debout ↔ allongé, le tir accroupi, le taser allongé.
    **Le 2026-10-10**, lot retéléchargé avec son déplacement (« In Place » décoché) et intégré, voir « Aucune glissade ». **Manquent toujours Walk Forward, Walk Backward, Walk Left et Walk Right** du même pack. L'utilisateur les croyait ajoutés le 2026-10-10 au soir ; ils ne sont nulle part sur le disque (cherché dans le projet, Documents, Bureau, Téléchargements), redemandés. **Les quatre diagonales de marche, elles, sont branchées depuis ce soir-là** dans l'anneau lent (`Walk Forward Right`, `Walk Backward Right` et leurs miroirs à l'import) : sneak et visée en diagonale glissent de 6 à 16 % bruts au lieu de 67 à 89 %, 0 à 2 % une fois calés, et `FootSlideTests` les exige. Restaient droit devant et droit derrière (30 à 80 % bruts). **Le 2026-10-11, Walk Forward, Backward, Left et Right sont arrivés** (importés en « Generic » par défaut, passés aux règles d'import ; la gauche est `Walk Left Miroir`, miroir à l'import de `Walk Right`) : l'anneau lent est entièrement fait de marches du Y Bot, glissade brute de 7 à 21 %, **0 à 2 % calée dans toutes les directions**, toutes exigées par `FootSlideTests`. Les « Rifle Sneak » n'ont pas été retéléchargés (toujours sur place) et vont à 0,6 m/s : trois fois trop lents pour le sneak du jeu (2 m/s), même à la cadence maximale (×1,6 → 0,96 m/s). Les utiliser demanderait de les retélécharger avec leur déplacement ET de ralentir le sneak vers 1 m/s : décision de jeu, proposée à l'utilisateur. **Essayé le 2026-10-11** (sneak à 1,3 m/s sur les Rifle Sneak retéléchargés avec leur déplacement), **puis écarté par l'utilisateur le même soir** (« je préfère l'ancien ») : le sneak debout reste à 2 m/s sur les marches du Y Bot. Voir « Les Rifle Sneak : essayés, puis écartés ».

38. ✅ **Corrigé le 2026-10-10** — plafond de cadence à ×1,6, qui couvre toutes les allures du jeu (voir « Aucune glissade »). Le diagnostic : au plafond de ×1,3, sprint à 88 % et marche accroupie à 3 m/s à 87 %.
39. ⚪ **Viser en se déplaçant à gauche tord le buste de près de 100°** au-dessus des hanches (les jambes du pas chassé tournent les hanches de 59° à gauche, et le tireur épaule le buste tourné de 27° à droite). Le fusil pointe juste ; reste à juger si la torsion se voit.

**Découverts en posant la sortie de sprint (2026-10-11)** :

44. 🟡 **Un client modifié peut raccourcir la sortie de sprint** en gonflant la durée de ses inputs (chacune bornée à 0,1 s). Voir « La sortie de sprint ».

**Découverts en chassant les glissades (2026-10-10)** :

40. ✅ **Vérifié à deux instances le 2026-10-10** — les règles serveur du sprint face au rechargement et au taser, qui reposent sur l'ordre de livraison des RPC fiables : client honnête et client tricheur, voir « Le sprint face au rechargement et au taser ».
41. 🟡 **Allongé, on ne se déplace plus de côté, et on rampe à 0,43 m/s** (décision de jeu, conséquence de « aucune glissade ») ; la reptation avant reste la reptation arrière jouée à l'envers. À rouvrir avec une reptation latérale et une reptation avant (introuvables sur Mixamo pour l'instant).
42. ⚪ **Glissades résiduelles mesurées** : course de côté 4,2 % en médiane d'appui à 5 m/s, **5,5 % depuis qu'on va de côté à ×0,85** (le pied roule sur sa tranche pendant l'appui : une glissade par pas presque inchangée, sur un chemin plus court ; exigée à 6 % pour ce seul cas), course arrière gauche 12 à 14 % au total (jambe déjà tendue à la poussée, l'IK ne peut plus atteindre le pied). Quelques centimètres par pas. Essayé sans effet le 2026-10-10 : figer l'axe du pied quand seule la pointe tient, talon au ras du sol.
43. ✅ **Supprimés le 2026-10-12, avec l'accord de l'utilisateur** (relecture complète) : les 43 clips importés mais inutilisés, envoyés à la Corbeille de Windows. Ceux du soldat (`Rifle Run`, `Rifle Run Aim`, `Run Right`, `Run Left`, `Run Backward`, `Sprint Forward`, `Walk Crouching` ×4, `Moving Backward In Prone Position`, `Jump Up`, `Jump Down`, `Jumping Over Into Combat`, `Rifle Aim To Down`, `Rifle Down To Aim`, `Rifle Kneel Idle`, `Rifle Kneel To Stand`, `Rifle Stand To Kneel`) et ceux du Y Bot (les vraies gauches debout, `Strafe Right` et son miroir, `Stabbing`, les quatre pivots `Turn 90`, `Crouch To Standing With Rifle`, `Jump Down`, les « Rifle Sneak », les six clips de pistolet en mouvement). Le contrôleur n'en référençait aucun ; tests verts après coup. L'historique Git garde ceux qui avaient été commités ; ceux des lots du 2026-10-10 et 2026-10-11 ne l'avaient jamais été (l'utilisateur garde ses téléchargements Mixamo).

**Découverts en posant la panoplie et le gant (2026-10-11)** :

45. 🟡 **Le raygun recharge et tire avec les gestes du fusil** : aucun clip de pistolet pour le rechargement (le pistolet bascule canon en l'air) ni pour le tir (pas de recul au bras), et allongé il est tenu comme un fusil. Des clips Mixamo « Pistol Reload » et « Pistol Shoot » le corrigeraient. Accroupi, `Pistol Kneeling Idle` est un genou à terre sur des jambes en squat (même piège que les clips du fusil, voir « La couche haut du corps »).
46. ✅ **Corrigé le 2026-10-12** (voir « Les sons de tir, de pas et le taser ») : le sniper a son propre tir, et les quatre armes ont un son de rechargement (dette n° 33 refermée). Historique : le pompe et le sniper partageaient le même bruit de fusil à pompe (le sniper plus grave), le raygun avait deux sons de laser, aucune arme n'avait de son de rechargement.
47. ⚪ **Restes des modèles fournis**, triés le 2026-10-12 avec l'accord de l'utilisateur : la scène de démo du pack `SF Weapons MLX` (et son composant manquant), ses munitions et douilles, les copies du raygun (glb, stl, dae, obj, et les textures « arm » du gltf), le FBX source du gant (55 Mo) et `Assets/New Material.mat` sont partis à la Corbeille. Restent : le modèle du sniper sans vraie lunette (faite de cylindres), et `Sniper.fbx`, source de `Sniper_Nettoye.asset`, gardé faute d'avoir été listé au tri.
48. ⚪ **Le gant n'a qu'un niveau de détail** (28 400 triangles) : sans conséquence à deux joueurs ; le générateur de LOD d'Unity pourrait en ajouter pour la distance.

**Découverts en finissant le menu (2026-10-12)** :

49. 🟡 **Deux `NullReferenceException` de Netcode à la sortie du Play Mode** (`NetworkSceneManager.Dispose`, ligne 609, via `OnApplicationQuit` puis `OnDestroy`), **seulement lors du premier Play qui suit une recompilation, et seulement si aucune session n'a été lancée** (après une session Host, rien). Au lancement de ce Play, le `NetworkManager` porte déjà un `NetworkSceneManager` (et un `SpawnManager`) alors qu'il n'écoute pas, et ce gestionnaire a déjà été libéré : Netcode le libère une seconde fois. **Pas notre code** : vérifié en désactivant l'aperçu 3D, l'erreur reste ; `NetworkBootstrapUI` ne démarre rien hors du clone ; en mode Édition, après la recompilation, le `NetworkManager` n'a pas encore ces gestionnaires. Vraisemblablement lié au rechargement de domaine désactivé et au `NetworkManager` désormais dans la scène de démarrage (jamais vu avant le menu, dans cinq sessions de travail). Netcode attrape l'exception : sans effet en jeu, et a priori impossible en build (pas de recompilation). Gêne réelle : la console n'est plus vierge pendant l'itération. Qui initialise ces gestionnaires au lancement du Play n'est pas trouvé.

## Tester sous latence, et l'outillage de diagnostic (2026-09-28)

**Simulateur réseau.** `com.unity.multiplayer.tools` 2.2.12 est installé pour ça (le `DebugSimulator` d'`UnityTransport` est obsolète et sans effet, voir les pièges). Un composant `NetworkSimulator` est posé sur `/NetworkManager` avec le preset `Assets/_ProjectScenes/NetSim_Test150ms.asset` (75 ms + 10 ms de gigue par paquet).

🚨 **Il est DÉSACTIVÉ par défaut depuis le 2026-10-06.** Resté actif, il faisait jouer l'utilisateur à **~316 ms d'aller-retour** sans le savoir, d'où « beaucoup de latence sur le client ».

⚠️ **Ne pas se fier au seul RTT affiché sous ce simulateur.** Avec le même preset, le `[DIAG-CLIENT]` a mesuré tantôt ~165 ms (2026-09-28, et une session du 2026-10-06), tantôt 310 à 320 ms (toutes les sessions du 2026-09-29 au 2026-10-06 matin). Et dans une session affichée à 165 ms, les poses de l'adversaire arrivaient avec ~185 ms de retard (rewind mesuré de 260 ms, dont 75 de réserve) : le preset ne s'applique visiblement pas de façon symétrique aux deux instances, peut-être selon que le clone a repris la scène sauvegardée. Le retard qui compte pour un tireur est celui que mesure le rewind (`[DIAG-SERVEUR] rewind_moy`). Pour un test sous latence, cocher le composant, et viser plutôt ~25 ms par paquet pour une partie entre amis réaliste (~100 ms d'aller-retour).

**Diagnostics de session**, tous en `#if UNITY_EDITOR`, jamais embarqués :
- `[DIAG-CLIENT]` (dans `PlayerLocomotion`) — **santé de la prédiction** : taux de resynchronisation, erreur moyenne et max, RTT, profondeur de la file serveur. Le chiffre qui compte est le **taux de resync** : une prédiction saine est à 0 %.
- `[DIAG-SERVEUR]` (dans `WeaponController`) — tirs acceptés, touchés, **rejetés par motif**, rewind réellement appliqué, plus la mesure différentielle décrite plus bas.
- `PlayerInputReader.AutopilotStrafe` — fait faire au joueur un va-et-vient latéral déterministe à la place du clavier. Indispensable pour tester le rewind : une seule personne ne peut pas à la fois se déplacer sur une instance et viser sur l'autre.
- `[DIAG-ANIM]` (dans `PlayerAnimator`, 2026-10-06) — pour chaque joueur que l'instance ne possède pas, toutes les 3 s : images par seconde, **entrées et relances d'état** d'Animator par couche, bascules du drapeau « en l'air », frames immobiles en pleine course, plus grand pas d'une frame, frames « affamées » (instant affiché au-delà de la dernière pose reçue). C'est lui qui a tranché « les animations recommencent du début » : zéro relance, mais des bonds de position.
- `[DIAG-ANIM]` porte aussi, depuis le 2026-10-09, la cadence des jambes, la vitesse naturelle mesurée, **`pieds=`** (vitesse moyenne où l'animation fait avancer les pieds, rapportée à celle du corps : 100 % = calés), la correction du buste et le tangage affiché. Mesuré à deux instances le jour même, client en va-et-vient sneak + visée vu du Host : **97 à 106 %**, contre environ 6 % avant. Le 2026-10-11, client en sneak qui change de côté toutes les secondes, vu du Host : **44 à 72 %** sur les Rifle Sneak essayés ce jour-là (la mesure de la cadence se trompait en allers-retours, voir « Les jambes »), **99 à 102 %** après correction, le sneak revenu sur les marches. `pieds=` ne compte plus que les images où le mélange est stable : pendant qu'il change, l'Animator renvoie des déplacements faux (jusqu'à 20 m/s au demi-tour), qui ne se voient pas mais faisaient lire 137 % pour des pieds à 97 %.
- `[DIAG-CHARGEUR]` (dans `WeaponController`, 2026-10-08) — chaque rechargement, chaque recalage du chargeur prédit sur le serveur (doit rester rare : un recalage hors début de manche signale un tir refusé), et chez un client distant l'état complet toutes les 3 s.
- **`PlayerInputReader.AutopilotFire` + `WeaponController.AutopilotAimAtOpponent`** — gâchette maintenue et visée au centimètre sur le torse de l'adversaire **tel qu'il est affiché**. Juge le rewind sans biais humain : avec un rewind exact, `[DIAG-SERVEUR]` doit trouver le rayon au centre du corps PASSÉ.

**Banc de test à deux instances (2026-10-06).** Le clone de Multiplayer Play Mode ne se pilote pas depuis l'Editor principal. `NetworkBootstrapUI` relit à chaque entrée en Play Mode du clone trois interrupteurs dans un fichier, `%LOCALAPPDATA%/DuelArena/banc-de-test.txt` : connexion automatique en client, va-et-vient, tir automatique visé (qui recharge aussi à vide depuis le 2026-10-08), coup de taser automatique dès qu'un adversaire est à portée (`TaserController.AutopilotStrike`), depuis le 2026-10-09 sneak et visée maintenus (`autosneak`, `autoaim` : le va-et-vient se fait alors à l'allure qui glissait), et depuis le 2026-10-10 l'exercice du sprint face au rechargement et au taser (`autodrill`, `PlayerInputReader.UpdateSprintDrill` ; depuis le 2026-10-11, il tire aussi juste après une course et recharge en courant, pour la sortie de sprint), honnête ou tricheur (`cheatsprint`), et depuis le 2026-10-11 la panoplie qui tourne (`autoswitch`, une arme toutes les 6 s) et l'exercice du franchissement (`autovault`, honnête ou tricheur : `cheatvault`). `[DIAG-SPRINT]` (chez le joueur) date chaque tir et rechargement partis moins d'une seconde après un sprint. Les régler par `eval` (`NetworkBootstrapUI.SetCloneTestBench(autoClient, autopilot, autofire, autotaser, autosneak, autoaim, autodrill, cheatsprint, autoswitch, autovault, cheatvault)`), attendre que le clone ait rechargé le code (`Reloading assemblies after forced synchronous recompile` dans son journal), démarrer le Host dans l'Editor principal, lire `Library/VP/<clone>/Logs/Editor.log`. **Les éteindre après** (`SetCloneTestBench(false, false, false)`) : sinon la prochaine session de l'utilisateur se connecte et tire toute seule.

🚨 **Pas dans les EditorPrefs** (2026-10-08). Les interrupteurs y étaient d'abord, puisque les deux processus partagent le même registre. Mais le clone ne voyait pas les changements : registre à 1, clone qui lisait 0. Il tournait depuis le matin, et il a vraisemblablement gardé en mémoire les valeurs lues à son démarrage. Le 2026-10-06, le banc n'avait marché que parce que le clone venait de redémarrer. C'est le rapport `[DIAG-CHARGEUR]` (`gachette=False`) qui l'a montré.

⚠️ **Immobile à son point d'apparition, le Host est caché du client par le mur de caisses** devant `Spawn A` : un client en tir automatique ne le touche pas (`touches=0` dans `[DIAG-SERVEUR]`, distance au corps 0,00 m). Pour mesurer des touches, faire bouger le Host (va-et-vient) ou le déplacer dans le champ.

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

**Tests EditMode** sous `Assets/Tests/EditMode` (`DuelArena.Tests.EditMode`). Lancer : `unity command run_tests --project-path "..." --mode EditMode`. 110 tests EditMode et 58 tests PlayMode au 2026-10-12, tous verts.

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

`Assets/Tests/PlayMode` (`DuelArena.Tests.PlayMode`) : 12 tests de déplacement (déterminisme, vitesse pendant une transition de posture, pieds au sol, sprint vers l'avant seulement, recul et côté plus lents, élan du sprint, poids des armes), 2 tests de recul (`RecoilTests`), 6 tests des zones de tir par vrais rayons (`HitboxZoneTests`), 6 tests du coup de taser par vrais colliders (`TaserStrikeTests`), 3 tests de la vue subjective (`FirstPersonViewTests`), 1 test des pieds sur toutes les allures (`LocomotionFootTests`), 2 tests du buste (`TorsoAimTests`), 2 tests du viseur (`WeaponSightTests`), 2 tests des pieds dans le monde (`FootSlideTests` : en ligne droite, et en allers-retours de côté), 1 test du pivot sur place (`PivotTests`), 1 test des pieds quand on se baisse et se relève (`CrouchFeetTests`), 1 test des armes tenues en main (`HeldWeaponsTests`), 3 tests du gant et de la tenue de pistolet (`TaserGloveAndPistolTests`), plus les tests d'animation des sections suivantes (58 en tout au 2026-10-11 dans la nuit). Lancer : `unity command run_tests --project-path "..." --mode PlayMode --async_tests`, puis sonder `test_status`. **Le mode synchrone ne marche pas** : entrer en Play Mode déclenche un rechargement de domaine qui coupe la requête HTTP.

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

`SwattSolider_T_Pose` **reste dans le projet, sans être affiché** : les 11 animations du soldat encore utilisées (`Ch35_nonPBR@…`) ont été produites sur son squelette et l'utilisent comme avatar source à l'import (`Copy From Other Avatar`). C'est le bon avatar pour elles ; le retirer imposerait de les réimporter sur un autre squelette. Ses textures (140 Mo), inutiles pour un avatar, ont été supprimées le 2026-10-12 : il s'affiche désormais blanc, ce qui n'a aucune importance. Les clips Humanoid se rejouent ensuite sur n'importe quel avatar humanoïde, Y Bot compris. Non référencé par une scène, il n'entre pas dans la build.

Les tests d'animation prennent le personnage **dans le prefab** (`TestCharacter.InstantiateModel`), pas par un chemin écrit en dur : ils suivent d'eux-mêmes tout changement de modèle.

**L'origine du Player est aux PIEDS** — `CameraPivot` est à 1,65, qui est la hauteur caméra debout mesurée depuis les pieds. Un Mixamo ayant lui aussi sa racine aux pieds, il s'attache sans décalage.

⚠️ **`SkinnedMeshRenderer.bounds` n'est PAS la taille du personnage.** C'est une boîte englobante *statique*, gonflée pour couvrir n'importe quelle pose d'animation : elle annonçait 1,92 m. La vraie mesure passe par `BakeMesh`, qui tient compte de la pose courante — **1,797 m en T-pose, 1,764 m en pose d'attente**, contre 1,80 m pour la capsule debout. L'écart est d'un demi-centimètre, donc aucune mise à l'échelle n'est nécessaire. Mesurer en T-pose reste d'ailleurs trompeur : on est naturellement plus petit en garde.

### Conformité d'import — propriétés non négociables

Tout FBX d'animation ajouté doit avoir : **rig Humanoid** en `Copy From Other Avatar` pointant l'avatar du personnage, **`lockRootPositionXZ` à FALSE** (Bake Into Pose décoché, voir la section « Root motion » plus bas), et un **bouclage correct**. **Les clips de déplacement** ont en plus `keepOriginalOrientation` à **TRUE** (« Root Transform Rotation : Based Upon Original ») et **`lockRootRotation` à TRUE** (rotation gardée dans la pose, voir « Aucune glissade »), et **debout, la gauche se fait en clip miroir À L'IMPORT**, jamais par la case miroir d'un arbre (voir « Les jambes : des pieds calés sur le sol ») ; de vraies gauches ne sont acceptées que si `LocomotionFootTests` et `FootSlideTests` passent avec elles (c'est le cas de la marche accroupie). `LocomotionImportTests` interroge l'importeur pour les deux. **Au téléchargement sur Mixamo, un clip de déplacement se prend « In Place » DÉCOCHÉ** : sur place, il n'a plus aucun déplacement de racine, et `DriveGait` ne peut plus mesurer ni la cadence ni la direction des jambes (vécu le 2026-10-10 : un lot entier de 31 clips à retélécharger ; se vérifie en lisant la courbe de position des hanches du clip importé, qui ne doit pas revenir à son point de départ). Exception verticale : un clip qui fait **monter ou descendre** le corps sur place (transitions de posture) doit avoir `lockRootHeightY` à **TRUE**, sinon sa descente est jetée avec le root motion (voir « Les transitions de posture »).

⚠️ *Corrigé le 2026-10-05 : cette ligne exigeait l'inverse (`lockRootPositionXZ` coché) et contredisait la section « Root motion », qui est la bonne. L'importeur, interrogé ce jour-là, donne bien `lockRootPositionXZ = false` sur les 30 clips. Piège à surveiller au moment d'importer les animations du robot.*

- Sans Humanoid, Mecanim ne peut pas retargeter — or les animations viennent d'un AUTRE personnage (`Ch35_nonPBR`) que le modèle. Elles ne joueraient tout simplement pas.
- Le déplacement de la racine doit rester du root motion, que `applyRootMotion = false` jette. **La position horizontale appartient exclusivement à `Move()`** ; une animation qui déplace aussi le personnage entrerait en conflit avec la simulation, et la vitesse d'un Animator n'est pas déterministe entre machines.
- Le bouclage se classe sur le bon critère : un état **continu** boucle (attente, déplacement, phase aérienne), un **événement** ponctuel non (tir, transition de posture, atterrissage, vault). Une première version déduisait « transition » du `" To "` des noms Mixamo — juste au début, faux dès l'arrivée des tirs et des sauts.

### L'Animator

`Assets/_ProjectArt/PlayerAnimator.controller`, piloté par `PlayerAnimator.cs`.

**Convention des paramètres** : `MoveX`/`MoveY` sont la vitesse **locale divisée par la vitesse de référence de la posture**, et `Allure` leur norme (posés ensemble par `PlayerAnimator.SetLocomotion`, y compris dans les tests). Donc 0 = immobile, 1 = allure nominale, 2 = sprint. La référence suit aussi la direction (on recule et on va de côté moins vite, voir « Reculer et aller de côté ») : reculer en marchant vaut 1, comme avancer. Le sneak (0,4) joue l'anneau lent de l'arbre de déplacement, et la visée (0,55) le mêle pour un quart à la course ; l'attente n'est plus dans cet arbre, c'est `Marche` qui passe de l'une à l'autre (voir « Les jambes : des pieds calés sur le sol » : le mélange avec l'attente faisait glisser les pieds). `Stance` (0/1/2) vient de la `NetworkVariable`, donc juste partout.

🚨 **`PlayerAnimator` dérive la vitesse du DÉPLACEMENT DU TRANSFORM, pas de `currentVelocity`.** Cette dernière n'existe que là où la simulation tourne (propriétaire et serveur) : chez un spectateur elle vaudrait zéro en permanence et l'adversaire glisserait sans bouger les jambes. Le delta de transform est juste dans les **quatre** cas réseau, interpolation du spectateur comprise — ce qui est précisément ce qu'on veut montrer. Il est borné à 2,5× la référence, sinon la téléportation de quelques centimètres d'une resynchronisation se dériverait en plusieurs mètres par seconde et ferait sursauter l'animation à chaque recalage.

Le script est de **catégorie C stricte** : il LIT, il n'écrit jamais rien que la simulation relise. C'est ce qui rend inoffensif le non-déterminisme de l'Animator.

**Le propriétaire ne voit pas son propre modèle** : ses renderers passent en `ShadowsOnly` dans `OnNetworkSpawn` (sa caméra est à hauteur de tête, il verrait l'intérieur du crâne). `ShadowsOnly` plutôt que désactivés, parce qu'il continue ainsi de projeter une **ombre**, qui est une information de jeu — voir sa propre ombre dépasser d'un angle renseigne sur ce que l'adversaire voit. Ciblé sur le seul sous-arbre `Model`. Ce que le propriétaire voit de lui-même, ses bras et son fusil, est une copie à part : voir « La vue subjective ».

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

*Septième occurrence le 2026-10-08*, après un `SaveAssets` sur l'AnimatorController (restauration d'une mutation du miroir du taser). Une recompilation simple répondait « rien à recompiler », et le lanceur restait à 0 test. **Une recompilation complète** (`CompilationPipeline.RequestScriptCompilation(CleanBuildCache)`) l'a débloqué, sans redémarrage.

*Huitième et neuvième occurrences le 2026-10-09*, **sans aucune écriture d'asset** : pendant une validation par mutation de code, la DEUXIÈME exécution PlayMode d'affilée (filtrée sur une autre classe) répondait 0 test, deux fois de suite. La recompilation complète l'a débloquée à chaque fois. L'écriture d'asset n'est donc pas le seul déclencheur ; une exécution PlayMode juste avant en est un autre. **Réflexe : au premier « 0 test », recompilation complète, puis relancer.**

### Les jambes : des pieds calés sur le sol (2026-10-09)

Rapporté par l'utilisateur : en visée, « problème de vitesse d'animation », et en sneak + visée, debout comme accroupi, « on dirait que je glisse sur le sol ». **Mesuré** (vitesse de racine de l'animation jouée, rapportée à celle du corps) : **3 à 16 %** en visée et en sneak, 52 % même à la marche latérale nominale. Trois causes, toutes silencieuses :

1. 🚨 **Un arbre de mélange SYNCHRONISE ses clips** : la durée du mélange est la moyenne pondérée des leurs. L'attente debout (`Rifle Idle`) dure **8,6 s** ; mêlée à 45 % à une course de 0,5 s, elle étirait le cycle à 6,4 s, et la course jouait dix fois trop lentement. C'est ce que la « cadence de lecture » d'avant ne pouvait pas voir. **L'attente et le déplacement sont désormais deux branches** d'un arbre 1D sur le paramètre **`Marche`** (0 à l'arrêt, 1 dès 0,05 de vitesse normalisée depuis le 2026-10-11, 0,1 avant), et les arbres de déplacement (`DeboutMarche`, `AccroupiMarche`, `ProneMarche`) ne contiennent plus d'attente. Les noms d'états (`Debout`, `Accroupi`, `Prone`) et toutes les transitions sont inchangés.
2. 🚨 **« Root Transform Rotation : Based Upon Body Orientation »** (le réglage par défaut) tourne chaque clip pour aligner la moyenne hanches-épaules sur l'avant. Dans la course latérale de Mixamo, les hanches regardent 58° vers la direction de course : le robot courait en diagonale vers l'avant en allant à droite. **Les clips de déplacement sont importés en « Based Upon Original »** (`keepOriginalOrientation`) : chacun part dans sa direction, à 4° près.
3. 🚨 **Unity mêle le déplacement de racine dans le repère du BUSTE de chaque clip**, pas linéairement. Pas chassé gauche et droit à 50/50 avancent tout droit à 1,86 m/s ; un pas chassé mis en miroir dans l'arbre, mêlé à la course gauche de Mixamo (dont le buste est tourné de l'autre côté), n'avançait plus qu'à 35 % de sa vitesse. **La gauche est donc le miroir exact de la droite, fait À L'IMPORT** : `Strafe Left` (de `Strafe Right`), `Run Left Miroir` (de `Run Right` ; la `Run Left` d'origine n'est plus jouée), `Run Backward Miroir`. L'arrière est coupé en deux moitiés à ±10° (`Run Backward` / `Run Backward Miroir`), pour que chaque diagonale arrière mêle des clips tournés du même côté. La case « miroir » d'un enfant d'arbre n'est **jamais** utilisée pour le déplacement.

**`DeboutMarche`** (refait le 2026-10-10 au soir) : un arbre 1D sur **`Allure`**, à deux branches (un anneau de Rifle Sneak a été essayé dessous le 2026-10-11, puis retiré). **`AnneauLent`** (seuil 0,4, directionnel libre) : les **huit marches du Y Bot** depuis le 2026-10-11 (`Walk Forward`, `Walk Backward`, `Walk Right`, `Walk Forward Right`, `Walk Backward Right`, et à gauche leurs miroirs à l'import), au rayon 1. **`CourseEtSprint`** (seuil 1, directionnel libre, l'ancien arbre sans son anneau lent) : les courses du Y Bot au rayon 1 (avant, droite, arrière, arrière droite, et à gauche les **miroirs à l'import** de la droite : `Run Left Miroir`, `Run Backward Left Miroir`), le sprint au rayon 2 (avant, avant droite, et `Sprint Forward Left Miroir`).
🚨 **Pourquoi deux branches** : dans un seul arbre directionnel, une direction présente sur un anneau et absente du voisin attire tout le mélange. La marche diagonale avant, ajoutée à l'anneau lent, captait la diagonale de la course (il n'y a pas de course diagonale avant) : à 5 m/s, elle jouait la marche lente, et les pieds glissaient de 6 à 13 %. Course et sprint restent ensemble : la diagonale de la course y emprunte au sprint diagonal, et c'est ce qui la fait tenir (séparer aussi le sprint la faisait glisser de 13 %). **`AccroupiMarche`** : les huit marches accroupies du Y Bot, gauches comprises (vraies prises du pack, cohérentes entre elles : les tests passent). Sous l'anneau le plus lent, Unity joue cet anneau ; au-delà du plus grand, il joue le plus grand (vérifié) : `PlayerAnimator` donne donc la vitesse telle quelle.

🚨 **Debout, la gauche reste le miroir de la droite** (vérifié le 2026-10-10). Les vraies gauches du pack (`Run Left`, `Run Backward Left`, `Sprint Forward Left`) ont les hanches tournées à droite (+52° en course), le pas chassé miroir à gauche (−58°) : mêlées, la correction de cap partait vers ce mélange de 121° et la course arrière gauche tombait à 21 % de sa vitesse. Un arbre entièrement symétrique ne peut pas le faire. Elles ont été supprimées le 2026-10-12 (dette n° 43).

**La cadence et la direction se MESURENT** (`PlayerAnimator.DriveGait`, statique). La simple présence d'un `OnAnimatorMove` (`AnimatorRootMotionProbe`, ajouté sur `Model` par `PlayerAnimator`) fait calculer le déplacement de racine à l'Animator **sans l'appliquer** ; sans lui, `Animator.velocity` vaut zéro quand `applyRootMotion` est faux. Cette vitesse, ramenée à la cadence où elle a été jouée, dit à quelle vitesse **ce mélange précis** fait avancer les pieds : la cadence suivante (`LocoSpeed`) en découle. Sa direction, comparée à celle du corps, fait tourner la direction envoyée à l'arbre : en diagonale, chaque clip pèse selon la longueur de sa **foulée** (1 m par cycle pour le pas chassé, plus de 2 m pour les courses), et le mélange penchait jusqu'à 33° vers la course. Zone morte de 6° (un clip seul part rarement pile dans l'axe : sans elle, le sprint finissait mêlé à 19 % de course latérale), correction remise à zéro à chaque changement franc de direction. Les mesures se font en moyenne sur un **cycle de pas**, et seulement quand le déplacement joue seul (ni attente mêlée, ni transition d'état). **Aucune table de vitesses à tenir à jour** : un clip remplacé ou une vitesse de jeu changée se recalent seuls.

**En allers-retours, la mesure se fait par tranche du cycle, clip par clip** (2026-10-11). Mesuré à deux instances (banc de test, client en sneak qui change de côté toutes les secondes, vu du Host, quand le sneak jouait les Rifle Sneak) : pieds à **44 à 72 %** de la vitesse du corps, alors que tous les tests en ligne droite passaient. Le défaut n'était pas propre à ces clips : le sneak sur les marches et le sneak accroupi l'avaient aussi. Deux défauts se nourrissaient l'un l'autre :
- 🚨 **Pendant que le mélange change, l'Animator renvoie des déplacements de racine faux**, jusqu'à 10 m/s : au demi-tour (le mélange passe par l'attente et repart de l'autre côté), et quand l'allure traverse d'un coup les anneaux (visée de côté, le temps où un anneau de sneak existait : marche, sneak, puis marche en 0,3 s). Divisés par une cadence basse à cet instant, ils entraient dans la mesure. On ne mesure plus qu'après deux évaluations où les poids des clips joués n'ont presque pas bougé (moins de 2 par seconde en somme d'écarts, `MixChange`, `steadyFrames`).
- 🚨 **Un pas chassé peut avancer par à-coups** (celui des Rifle Sneak : 0,6 m/s pendant la moitié du cycle, rien pendant l'autre). La moyenne repartait presque de zéro à chaque demi-tour et ne voyait qu'un bout de cycle : jusqu'à trois fois la vraie vitesse. La cadence baissait, le cycle s'allongeait (jusqu'à 16 s), et la mesure en voyait une part encore plus petite : un cercle vicieux. La vitesse naturelle est désormais la moyenne de **huit tranches du cycle de pas** (`phaseEfficiency`) ; une tranche ne se rapproche (de moitié par passage) que des mesures faites à sa phase.
- **Chaque clip dominant garde son profil** (`profiles`) : partager les tranches entre clips mêlait le profil d'un clip à celui d'un autre (le pas chassé par à-coups des Rifle Sneak, joué 0,1 s à chaque demi-tour de la visée, à la marche, régulière).
- **Les tranches sont rapportées à la vitesse qu'annoncent les clips joués** (`MixClipSpeed` : `AnimationClip.averageSpeed` de chaque clip, pondéré par son poids et sa durée, puisqu'un arbre synchronise ses clips). En passant d'un clip à l'autre, la vitesse change d'échelle à l'image près ; les tranches n'apprennent que l'écart entre l'annonce et le mélange réel, 1 à 4 % pour un clip seul. Un clip jamais mesuré part de son annonce.

Gardé par `FootSlideTests.EnAllersRetoursDeCote_LaCadenceResteCelleDuPas` (sneak debout et accroupi, un côté pendant 1 s et 0,4 s ; visée, 1 s) : la vitesse naturelle retenue en allers-retours, quand l'Animator joue le même mélange de clips qu'en ligne droite (somme des écarts de poids sous 0,1), comparée à celle de la ligne droite. **0,6 à 2,3 % d'écart moyen**, 3 % au plus pour 95 % des images. Validé par mutation : l'ancienne mesure fait **58 à 70 %** d'écart moyen dans les cinq cas. Une première version qui remplaçait une tranche à chaque passage faisait passer le sprint de 9,6 à 16,6 % de glissade totale (la cadence ondulait au fil du cycle) : elle n'en fait plus que la moitié du chemin. Vérifié à deux instances : **99 à 102 %**, vitesse naturelle 1,84 à 1,87 m/s (la marche de côté).

⚠️ En mode Édition, Unity n'appelle pas `OnAnimatorMove` : une mesure par `eval` hors Play Mode doit lire `Animator.velocity` avec `applyRootMotion` vrai, en replaçant le modèle à chaque pas.

**Résultat** (`LocomotionFootTests`, 50 allures, vitesses du prefab) : pieds à **97 à 101 %** de la vitesse du corps et **5° au plus** de travers, debout et accroupi, en sneak, en visée et en sneak + visée, dans les huit directions. Validé par mutation : cadence fixe (pieds de 53 à 235 %), correction de direction coupée (diagonales de 12 à 32° de travers). À deux instances : `pieds=97 à 106 %` (voir `[DIAG-ANIM]`).

**Réglages** : `maxLegCadence` (**×1,6** depuis le 2026-10-10, voir « Aucune glissade » ; ×2,4 le temps des Rifle Sneak, le 2026-10-11) ; `minLegCadence` (×0,2) — en dessous, l'animation paraît au ralenti, signe qu'un clip de marche lente manque (dette n° 37). `SpeedMult` ne pilote plus que les attentes du haut du corps ; `standingPlaybackScale` et ses voisins règlent l'attente. La reptation se mesure comme le reste depuis que `MinNaturalSpeed` est descendu à 0,15 m/s (elle avance à 0,28 m/s ; cette page la disait jusqu'au 2026-10-10 « authorée sur place », ce qui était faux).

**La vitesse naturelle est la norme de la moyenne des VECTEURS vitesse** (`GaitState.naturalVelocity`, 2026-10-10), pas la moyenne de leurs normes : la reptation se tortille d'un côté à l'autre, et la moyenne des normes surestimait son avance de 20 %.

*Historique* : la première « cadence de lecture » (2026-09-29) multipliait la vitesse de lecture par la vitesse normalisée, plafonnée à 1 au motif qu'un sprint lu ×1,5 « paraissait frénétique ». Elle ignorait la synchronisation des arbres et l'orientation des clips, ce qui la rendait fausse partout sauf à l'allure nominale en ligne droite.

### Aucune glissade : clips natifs, pieds calés, pivot sur place (2026-10-10)

Exigence de l'utilisateur : « je veux que les animations soient parfaites, on n'accepte pas les glissades » (GDD § 7). Ce qui juge désormais, c'est **`FootSlideTests`** : le modèle avance réellement à la vitesse du jeu, conduit comme en jeu, et on mesure de combien bouge **dans le monde** chaque talon ou pointe posé (à 5 mm de son plus bas), rapporté au chemin du corps. 🚨 `LocomotionFootTests` ne suffisait pas : il juge la vitesse que l'Animator CALCULE, qui peut être juste pendant que les pieds glissent (sprint : racine à 98 %, pieds qui glissaient de 56 % en plein appui). Le déplacement de racine d'un clip n'est pas toujours celui de ses pieds.

Ce qui a été fait, chaque point mesuré :
1. **Clips natifs du Y Bot** (pack « 8 directions » de Mixamo, lot du 2026-10-10). Les clips du soldat recalés sur le robot glissent d'eux-mêmes : marche accroupie avant 29 % contre 6,5 % en natif, `Rifle Run Aim` 60 %.
2. 🚨 **La rotation de racine des clips de déplacement est gardée dans la pose** (`lockRootRotation`, « Bake Into Pose » de la rotation). Le jeu jette cette rotation (c'est lui qui oriente le joueur) ; le déplacement, exprimé dans un repère qui oscillait en lacet, dérivait alors de côté : 0,3 à 7° pour les marches et courses, **12,6° pour la reptation**, qui se tortille. Dans la pose, le corps garde son balancement et chaque clip part exactement dans sa direction. Pas pour les clips dont la rotation sert.
3. **Plafond de cadence à ×1,6** : la marche accroupie à 3 m/s demande ×1,52 (clip à 1,97 m/s), le sprint ×1,44 (6,96 m/s pour 10).
4. **Allongé** : reptation ralentie à la vitesse de son animation (`proneProfile.moveSpeedMultiplier` 0,25 → **0,085**, 0,43 m/s à l'allure de marche), **pas de déplacement de côté** (`Move()` annule l'input latéral allongé), seuils de `ProneMarche` à ±0,05 (à ±0,4, une reptation lente mêlait l'avant et l'arrière, qui s'annulaient). Décisions de jeu, inscrites au GDD.
5. **`FootPlanter`** (classe simple, possédée par `PlayerAnimator`, appliquée en `LateUpdate` sur la pose que l'Animator vient d'écrire, avant le buste et le lean) : pendant qu'un pied porte, on retient où il s'est posé **dans le monde** et on l'y maintient par une **IK analytique à deux os** (`SolveTwoBone`, testée), pied à son orientation animée. Le talon tient à l'attaque, la pointe dès qu'elle touche ; pied à plat, son **axe** aussi est figé (sinon il pivote sur la pointe et le talon balaie le sol : course de côté 6,9 % → 4,2 %). Un pied « porte » quand il est à 2 cm de son plus bas récent sans avancer plus vite que 1,2 fois le corps le long de la marche (une pointe qui frôle le sol en balancier n'est pas un appui). Relâché au-delà de 25 cm d'écart ou 45° de rotation ; l'écart se résorbe en l'air en 0,07 s. **Coupé allongé, en l'air, en franchissement et pendant les passages par le sol**, et ses plus bas retenus sont alors oubliés.
6. **Se baisser et se relever** (`PlayerAnimator.LegsOnFeet`, 2026-10-10) : le calage reste actif pendant le fondu debout ↔ accroupi. Accroupi, le robot a le pied droit 35 cm plus en arrière, sur la pointe : le fondu le faisait glisser d'autant, aller comme retour, et le mélange des deux poses de jambe passait la pointe **5 cm sous le sol**. Pendant le fondu (`Apply(…, changingPose)`), les plus bas retenus sont gelés puis réappris dans la pose d'arrivée (sinon ce passage sous le sol devenait la hauteur de contact, et le pied ne se recalait plus avant une seconde), et le pied est relevé pour ne jamais descendre sous le sol, vu des deux pieds (la plus basse des pointes, le plus bas des talons : le pied droit accroupi est talon levé). Le pied qui doit changer de place fait un pas.
7. **Le pivot sur place** (`PlayerAnimator.StepLegsTurn`, fonction pure testée). À l'arrêt, le bas du corps garde son cap quand le joueur tourne ; le buste suit le regard (`ApplyAimDirection`, jusqu'à 120°). Au-delà de **70°** (`TurnThreshold`), les jambes pivotent vers le regard à **90°/s** (`TurnSpeed`) ; jamais plus de 100° de retard (`MaxLegsLag`) ; en marche, elles se réalignent à 540°/s. Le modèle est tourné (`animator.transform`), et `DriveGait` reçoit la vitesse dans le repère du MODÈLE. Pendant le pivot, `FootPlanter` garde chaque pied posé et lui fait faire **un petit pas levé** (6 cm, 0,2 s) quand le corps l'a tiré de plus de 10 cm ou 22°, un pied à la fois (le second part quand le premier est à mi-pas). Le pas se lève au-dessus de la pose ANIMÉE, n'avance qu'une fois décollé (de 15 à 85 % de sa durée), fige sa destination en redescendant et se pose aussitôt (2026-10-10 : sinon la pointe frottait le sol au départ et à l'arrivée, de 0,5 à 1,5 cm à chaque fois, en suivant un fondu de posture qui bougeait encore). Catégorie C stricte : rien de la simulation, du tir ni du hitbox ne lit le cap des jambes.

**Essayé et écarté** :
- **Les clips de pivot Mixamo** (`Turn 90`, debout et accroupi, fournis par l'utilisateur). Ils ne posent pas les pieds où nos attentes les posent : accroupi, pointe gauche à 14 cm du centre au lieu de 33. Passer de l'un à l'autre les faisait donc traîner au sol, et ils glissent d'eux-mêmes de 5 à 25 cm. Mesuré avec calage : 15 à 32 cm de pointes au sol par pivot, contre 1,4 à 3 cm en procédural ce jour-là (0 à 0,1 cm depuis). Supprimés le 2026-10-12.
- **Une cadence réglée sur la glissade mesurée par le calage** (au lieu de la racine) : instable, le sprint passait de 5 à 16 %.
- **Le clip accroupi → debout** (`Crouch To Standing With Rifle`, fourni par l'utilisateur, 1,1 s). Même défaut que les pivots : il commence les pieds à 25 cm de ceux de l'attente accroupie, traîne lui-même la pointe gauche de 21 cm, et finit à 16 cm de l'attente debout. Le fondu calé fait mieux (voir le point 6), et en 0,3 s le haut du corps, qui garde l'arme, couvre de toute façon ce que le clip ajoute au-dessus de la taille. Supprimé le 2026-10-12.
- **La réception** (`Jump Down`, fourni par l'utilisateur ; même clip que celui du soldat). Le clip est bon, à condition de garder sa hauteur dans la pose (`lockRootHeightY`) : contact à 0,3 s, genoux fléchis jusqu'à 0,45 s. Mais le jeu n'a pas de saut : on ne tombe que d'un rebord, en marchant, donc on atterrit en mouvement, et ce clip sur place ferait glisser. Non branché, puis supprimé le 2026-10-12 ; la sortie de `EnLAir` reste un fondu de 0,15 s.

**Résultats** :
- `FootSlideTests` : médiane de glissade en appui ≤ 5 % et total ≤ 15 % sur toutes les allures exigées. Exemples : sprint 56 % → 5 %, sneak latéral 17 % → 1 %, accroupi 6-9 % → 1-4 %.
- `PivotTests` (100°, debout et accroupi, gauche et droite) : pointes posées **0,0 à 0,1 cm** au sol (1,4 à 3 cm avant que le pas ne se lève avant d'avancer), contre 108 à 170 cm d'un bloc ; jambes à 0,3° du regard à la fin.
- `CrouchFeetTests` (debout → accroupi → debout, à l'arrêt) : pointes posées **2,3 cm** au sol en tout, contre 131 cm quand le calage était coupé pendant le fondu ; la pointe ne descend plus sous 3,1 cm (−5,3 cm avant).
- Validé par mutation : sans axe figé (course de côté 6,9 %, sprint 14 %), sans calage (tout échoue, de 6 à 56 %), genou plié à l'envers (`FootPlanterTests`), sans petits pas (48,8 cm), sans relever le pied au-dessus du sol pendant le fondu (61 cm), sans geler les plus bas pendant le fondu (71 cm).
- Restes connus : course de côté 4,2 % en médiane ; course arrière gauche 12 à 14 % au total, quand la jambe déjà tendue à la poussée ne peut plus atteindre son pied (`FootPlanter.OutOfReach`) .

### Les Rifle Sneak : essayés, puis écartés (2026-10-11)

Le sneak debout a joué, le temps d'une soirée, les « Rifle Sneak » de Mixamo (marche furtive, fusil épaulé, jambes fléchies), retéléchargés avec leur déplacement : sneak ralenti de 2 à 1,3 m/s, moitié moins de côté et en arrière, un anneau de sneak sous celui des marches, lu à ×2,3. **Écarté par l'utilisateur le même soir** : « je n'aime pas le sneak actuel (animation et vitesse), je préfère l'ancien ». Tout est revenu comme avant (2 m/s, les marches du Y Bot, `maxLegCadence` ×1,6, `minLegCadence` ×0,2) ; les clips ont été supprimés le 2026-10-12 (dette n° 43). Rien n'a été commité entre-temps : le code de l'essai n'existe plus que décrit ici.

Ce qu'on en garde, si l'idée revient :
- **Sans clip diagonal**, l'avant et le pas chassé mêlés n'avançaient qu'à 0,27 m/s à 45° (il aurait fallu les lire à ×4,8) : les jambes se tournaient vers la marche, de 45° au plus, vers le clip le plus proche, le buste restant face au regard.
- **Des jambes fléchies baissent la tête visible** sous la tête touchable (12 cm ici). Le bassin était remonté par l'IK à deux os du calage des pieds ; 🚨 cette remontée doit être **bornée par la portée des jambes**, sinon une jambe déjà tendue ne rejoint plus son pied (1,8 cm d'écart mesuré).
- **La mesure de la cadence se trompait en allers-retours** : trouvé à deux instances avec ces clips, corrigé pour tous (voir « Les jambes »). C'est ce qui reste de l'essai dans le code.

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

⚠️ **Les clips accroupis de Mixamo ne sont pas tous la même posture.** Mesuré : `Rifle Kneel Idle` est un GENOU À TERRE (hanches à 0,38 m, bassin à +5°) tandis qu'`Idle Crouching Aiming` est un SQUAT (0,46 m, −14°). Le masque excluant la racine, greffer le buste de l'un sur les hanches de l'autre laisse une trentaine de degrés d'erreur. **Les deux couches doivent servir la même famille de pose** — c'est pourquoi l'attente accroupie de la couche de déplacement est elle aussi un squat : `Idle Crouching` du Y Bot depuis le 2026-10-10 (hanches à 0,46 m, comme `Idle Crouching Aiming`), qui sert aussi d'arme basse accroupi sur le haut du corps.

⚠️ **Les deux couches doivent partager la même cadence** (`SpeedMult` sur `Arme` et `Vise` comme sur les états de déplacement). Quand elles jouent le MÊME clip — c'est le cas en prone, où les deux servent `Prone Idle` — une différence de vitesse les désynchronise en permanence et le personnage paraît bouger alors qu'il est immobile. Le tir garde sa cadence propre : c'est un geste ponctuel, pas une allure.

⚠️ **Limite connue, à éprouver en jeu** : le MP5 tire toutes les **143 ms** alors que les animations de tir durent 270 ms (debout), 430 ms (couché) et **1030 ms (accroupi)**. Elles sont authorées pour un tir visé isolé. Debout, relancer le geste à chaque coup devrait donner une pulsation de recul crédible ; accroupi, on ne verra jamais que les 14 premiers pour cent du clip. Si ça ne passe pas, la méthode est connue — mesurer où le recul se situe dans le clip, puis décaler l'entrée et accorder la vitesse, comme pour le vault.

### Rechargement, taser et strafe visé (2026-10-08)

Trois clips fournis par l'utilisateur, faits sur le squelette du **Y Bot** (et non du soldat) : `Y Bot@Reloading`, `Y Bot@Stabbing`, `Y Bot@Strafe Right`. Arrivés en `Generic`, ils sont passés en **Humanoid, `Copy From Other Avatar` → `Y BotAvatar`**, avec les règles d'import habituelles (déplacement de la racine non « baked »). Les clips humanoïdes se mélangent entre squelettes : ils cohabitent avec ceux du soldat dans le même contrôleur.

**Le strafe visé** est un pas chassé accroupi, fusil en joue, à **0,70 m/s** (mesuré sur la courbe de racine ; `Run Right` court à 4,55 m/s). Du 2026-10-09 au 2026-10-11, il formait les côtés de l'anneau lent de `DeboutMarche`, lu ×2,86 ; la marche de côté du Y Bot l'a remplacé (`PasDeCoteLent_JoueLaMarche_EtLaMarcheGardeLaCourse`). Supprimé le 2026-10-12, avec l'estocade `Stabbing` (dette n° 43).

**Le rechargement** est un état `Recharge` de la couche haut du corps (aiguillé par `StanceF` : un geste debout, accroupi et, depuis le 2026-10-10, allongé), piloté par le paramètre `Reloading` (`WeaponController.IsReloading` : la prédiction chez le tireur, le serveur chez les autres). Le geste utile du clip va de **0,4 à 2,5 s** (la main gauche quitte l'arme puis y revient, mesuré) : on y entre à 0,4 s et il est lu à `ReloadSpeed = 2,1 / reloadDuration`, pour tenir exactement dans le rechargement réel, même si sa durée change. Le fusil y bascule canon en l'air pendant le changement de chargeur. **Accroupi** (`Crouch Reload` du Y Bot, un squat comme l'attente) : son changement de chargeur va de 1,1 à 3,3 s sur 6,9 s. Le clip est découpé à l'import (images 20 à 124) pour que ce geste tombe aux mêmes instants relatifs que celui du rechargement debout, et lu ×1,05 dans l'arbre pour durer comme lui : la même entrée (12 %) et la même `ReloadSpeed` servent aux deux. La longue manipulation qui suit (3,3 à 5,5 s) est laissée de côté.

**Allongé** (`Prone Reloading` du Y Bot, 2026-10-10), joué par le **seul haut du corps**, comme les deux autres. Sur le corps entier, ce clip roule le robot sur le flanc : tête 33 cm sur le côté et 12 cm plus haut, donc hors de sa tête touchable, qui ne bouge pas (règle « on touche ce qu'on voit »). Sur le haut du corps, les hanches restent à plat : le fusil bascule canon en l'air, la main gauche va chercher le chargeur au sol devant, et la tête, coudes relevés, s'écarte de **11,6 cm au plus**, dans sa sphère touchable de 13 cm. Le changement de chargeur va de 0,4 à 2,5 s, comme debout : le clip est découpé à l'import aux images 0 à 99 (3,3 s) et lu ×1, même entrée et même `ReloadSpeed`. Gardé par `Allonge_RechargerChangeLeChargeur_EtLaTeteResteDansSaZone`, validé par mutation (l'ancienne pose couchée dans l'arbre le fait échouer).

**Le taser** a sa propre couche, `Taser`, qui ne pilote que le **buste et le bras gauche** (`MasqueTaser.mask`). Depuis le 2026-10-11, elle joue le **direct du poing** `Taser Punch` (fourni par l'utilisateur avec le gant, à la place de l'estocade `Stabbing`). Le coup de Mixamo est donné du bras **droit** — celui qui tient l'arme — donc il est joué **en miroir** : la main gauche, gantée, frappe, la droite garde l'arme. Mesuré image par image : le poing part à 0,10 s, il est tendu à 0,32 s et revenu à 0,82 s. Entrée à 0,10 s, geste utile de 0,72 s (`taserClipUsefulLength`), lu à `TaserSpeed = 0,72 / strikeDuration` : le contact tombe à 30 % du geste, les 0,18 s de `contactDelay`. Le coup frappe à hauteur de poitrine (l'estocade visait la tête). Le poids de la couche est piloté par `PlayerAnimator` (monte en 50 ms, retombe sur les 120 dernières), nul hors du geste. Gardé par `LeTaser_FrappeDeLaMainGauche_PuisRevientAuRepos`, qui mesure les mains au contact (aux valeurs du jeu depuis le 2026-10-11) et vérifie le clip joué ; validé par mutation avec l'estocade (sans miroir, la main gauche recule de 19 cm au lieu de frapper).

### L'arme visible (2026-10-08)

`Assets/_ProjectArt/Mesh/Armes/AssaultRifle.fbx`, fourni par l'utilisateur : un fusil futuriste de type bullpup. ⚠️ **Le fichier est une scène Cinema 4D entière** : une caméra, un ciel, un sol de 100 m, et le fusil (`p_06`, 205 pièces, 7,8 unités de long). On n'en garde que le fusil.

**`Assets/_ProjectArena/Weapon/FusilAssaut.prefab`** : un seul `MeshRenderer` sur un mesh fusionné (`FusilAssaut_Fusionne.asset`, un sous-mesh par matériau, 9 matériaux), à l'échelle **0,13** (1,01 m). Cette échelle met la poignée pistolet et la poignée avant à 33 cm l'une de l'autre, l'écart mesuré entre les mains dans les poses de tir. **Origine sur la poignée pistolet, canon vers +Z**, et un enfant `Bouche` au bout du canon (pour de futurs traçants partant de l'arme). Le fichier source n'est pas modifié : refaire la fusion si l'utilisateur le remplace.

**Accroché à la main droite** (`mixamorig:RightHand/FusilAssaut`), avec un décalage calculé sur la pose `Rifle Aiming Idle` : poignée au creux de la paume, canon dans l'axe de la main. Vérifié à l'image en visée, arme basse, course, accroupi, allongé et rechargement ; dans les poses de tir du soldat, la main gauche tombe à 4 à 19° de l'axe du canon (quelques centimètres). Une tenue à deux mains recalculée à chaque image a été envisagée puis écartée : l'écart ne la justifie pas. **Animation seule (catégorie C)** : le tir part toujours de la caméra, jamais du fusil.

L'asset `MP5` garde son nom de fichier ; c'est désormais le fusil d'assaut de la panoplie (voir « La panoplie »).

Le fusil porte un second repère, **`Mire`**, au centre de l'écran de son viseur (0 ; 0,2245 ; 0,092 depuis le 2026-10-09 ; avant, le haut du boîtier), +Z dans l'axe du canon : c'est sur lui que la vue subjective cale l'arme et que le buste s'aligne sur le regard.

### La vue subjective (2026-10-08)

Rapporté par l'utilisateur : « tu ne vois pas les bras du personnage quand il vise et tire, et aussi quand il met un coup de taser ». Le corps du propriétaire est en ombre seule (sa caméra est dans le crâne), et ses bras ne suivent pas le regard.

**`FirstPersonView`** (racine du prefab, propriétaire seulement) : au spawn, une **copie du `Model`** est accrochée à la caméra, sur le layer **`Viewmodel` (8)**, sans ombre, **tête et cuisses repliées** (échelle 0 : un Animator humanoïde n'écrit pas l'échelle des os). Elle est peinte de la couleur du joueur (`PlayerPaint.AddRenderer` : un `MaterialPropertyBlock` ne se copie pas avec l'objet). Une **seconde caméra, en Overlay URP** (`CameraBras`, 60° de champ, near 1 cm), ne dessine que ce layer par-dessus le monde, que la caméra principale ne dessine plus : le fusil ne s'enfonce pas dans les murs. D'où la référence d'assembly à `Unity.RenderPipelines.Universal.Runtime` dans `DuelArena.Runtime`.

**La copie ne reprend du corps que les GESTES de l'arme** (`PlayerAnimator.MirrorWeaponGestures`, depuis le 2026-10-10) : tir et taser par leurs événements, rechargement, et leurs vitesses. Elle joue **toujours la pose de visée, debout, jambes à l'arrêt, poses d'attente figées** : au repos, le robot tient son fusil en travers des hanches, hors de l'écran. L'arme est placée comme dans un FPS : la mire (`Mire`) en `aimSightPosition` en visée, décalée de `hipOffset` sans viser, canon dans l'axe du regard (valeurs et viseur : voir « Le viseur intégré »). L'alignement se MESURE à chaque image sur la pose jouée (`FirstPersonView.Place`), lissé sur 0,2 s (le recul du geste de tir passe), et **figé pendant le rechargement et le taser**, dont le geste doit se voir. Le pas se lit dans un **balancement procédural** de l'arme (`bobAmplitude` 8 × 12 mm à la hanche à l'allure de marche, 10 % en visée), à **`bobFrequency` 1,4 aller-retour par seconde** à la marche, qui ne suit la vitesse que de loin (puissance 0,2 : 1,6 en sprint). Jusqu'au 2026-10-11, il était proportionnel à la distance parcourue, et l'arme partait de gauche à droite quatre fois par seconde en sprint (« des gauche-droite trop rapides »). En sprint, l'arme est portée en travers (voir « La sortie de sprint »). Catégorie C pure : elle n'existe sur aucune autre machine.

🚨 **Rapporté par l'utilisateur le 2026-10-10** : « si je fais des gauche-droite en visant, l'arme ne fait que bouger, c'est impossible de viser », et « quand je me relève, la caméra passe à travers le buste ». La copie rejouait alors **tout le corps** : les jambes du pas chassé tournent les hanches de près de 60° d'un côté à l'autre, le buste suivait, et l'alignement lissé courait derrière ; les clips de changement de posture balayaient le buste devant l'œil. Mesuré (`EnVisee_PasChassesEtPostures_NeFontPasBougerLArmeDesBras`) : le fusil des bras tournait de 98° et se déplaçait de 184 cm sur un pas chassé visé suivi d'un passage accroupi, allongé, debout ; **0,0° et 0,0 cm** désormais. En jeu, en pas chassé visé, la mire dérivait encore de ±45 à ±60 pixels (en 1080p) à cause de la respiration de la pose de visée, que le lissage rattrapait en retard : poses d'attente figées, **±6 pixels**, axe du fusil à 0,00° du regard. Validé par mutation (les bras qui reprennent jambes et posture : 98°).

Vérifié à l'image : hanche, visée, tir, rechargement, coup de taser, changement de posture. Avec une arme à lunette (sniper), la caméra des bras s'éteint dans la lunette. Gardé par `FirstPersonViewTests` (layer, ombres, tête repliée ; placement exact de la mire ; arme immobile en pas chassé et en changeant de posture), validé par mutation (placement sans la rotation, ombres laissées, tête dépliée, bras qui rejouent le corps). Réglages de ressenti : `aimSightPosition`, `hipOffset`, `armsFieldOfView`, `bobAmplitude`, `bobFrequency`, `aimBobScale`, `sprintOffset`, `sprintEuler`, `sprintPoseInTime`.

### Le gant du taser et la tenue de pistolet (2026-10-11)

Fournis par l'utilisateur : un gant à décharge électrique (« Glover Schock », référence *Fallout*), le direct `Taser Punch`, et des animations de pistolet de Mixamo, « surtout pour les bras, au repos et en visée ».

**Le gant** est un modèle généré par IA : 1,4 million de triangles et ses textures embarquées dans un FBX de 55 Mo. Les textures sont extraites (`Gant/Gant_Couleur.jpg`, `Gant_Normale.png`), le maillage allégé à **28 400 triangles** par le générateur de LOD d'Unity (`MeshLodUtility.GenerateMeshLods`, dont on garde un niveau en compactant ses sommets : `Gant/Gant_Allege.asset`), matériau URP `Gant.mat`. Le fichier source n'a pas été modifié, puis a été supprimé le 2026-10-12 : le maillage allégé et les textures extraites suffisent. `GantChoc.prefab` : origine au poignet, +Z vers le poing, +Y vers le dos de la main, 44 cm de long (du coude au-delà des jointures), repère `Poing` au bout. **Il est posé dans le préfab joueur sous l'avant-bras gauche** (`mixamorig:LeftForeArm/GantChoc`), pas sous la main : rigide, il suivrait mal un poignet qui plie, et le poing du gant est fermé. La main gauche du robot est repliée (échelle 0, `HeldWeapons.WearTaserGlove`) : elle traverserait le poing. Le gant est symétrique, il n'a pas de côté.

**Les autres voient toujours le gant** au bras du robot. **En vue subjective, il n'apparaît que pendant le coup**, comme le couteau rapide de *Call of Duty* : porté en permanence, il masquait presque tout le raygun en visée (20 cm d'épaisseur sur l'avant-bras qui soutient l'arme). Hors du coup, la copie montre la main du robot. Pendant le coup, en vue subjective seulement :
- l'arme reste calée sur le regard **sans retard**, au lieu d'être figée comme pendant le rechargement. Le Taser Punch penche le buste en avant, et l'arme, figée sur la pose d'avant, montait devant l'œil ;
- elle s'abaisse un peu pour laisser passer le poing (`strikeOffset`, `strikeEuler`) ;
- le bras gauche tourne à l'épaule pour que le poing parte vers un point de l'écran (`strikeFistTarget`, en bas à gauche du centre), à la distance que lui donne le clip (`FirstPersonView.AimStrike`, fonction pure testée). Le coup frappe à hauteur de poitrine, 40 cm sous l'œil : sans ce réalignement, le poing passait sous l'écran.

Vu par les autres, le coup reste celui du clip. Vérifié à l'image : élan, contact (poing ganté qui jaillit du bas à gauche vers le centre, fusil abaissé en bas à droite), retour.

**La tenue de pistolet** : un paramètre float `Pistolet` (0 fusil, 1 pistolet) aiguille les états `Arme`, `Vise` et `Tir` du haut du corps entre leur arbre par posture habituel et un arbre de pistolet (`ArmeTenue` → `Arme` / `ArmePistolet`, etc., seuils manuels 0 et 1). Debout : `Pistol NoAim Idle` arme basse, `Pistol Aim Idle` en visée et au tir ; accroupi : `Pistol NoAim Idle` et `Pistol Kneeling Idle` ; allongé, la pose du fusil (aucun clip de pistolet couché). Tous ces clips tiennent l'arme **à deux mains**. `PlayerAnimator` le pose d'après le `WeaponModel` de l'arme montrée (`PistolHold`, le raygun), avec un fondu de 0,2 s (`holdBlendTime`) ; les bras de la vue subjective le reprennent (`MirrorWeaponGestures`). La vue subjective n'a donc plus besoin de l'IK du bras droit ni du bras gauche replié posés le matin même : elle place le pistolet par sa mire, comme les autres armes, avec un décalage de hanche propre (`pistolHipOffset`, sans avancer l'arme : les bras sont déjà tendus). La mire du raygun est remontée de 1,5 cm, la ligne de visée rasait son bouton arrière.

Les autres clips de pistolet (`Pistol Walk`, `Pistol Walk Backward`, `Pistol Strafe`, `Pistol Sprint`, `Pistol Stand To Kneel`, `Pistol Kneel To Stand`) ont été supprimés le 2026-10-12 sans avoir servi : les jambes jouent le déplacement du Y Bot quelle que soit l'arme, et le haut du corps de ces clips est la même tenue à deux mains (le sprint garde même le pistolet pointé devant soi).

Gardé par `TaserGloveAndPistolTests` (3 : tenue à deux mains et reprise par les bras ; gant porté, ôté, main repliée ; poing de la vue subjective orienté vers sa cible sans changer d'allonge) et `WeaponLoadoutTests.LeGant_EstSurLAvantBrasGauche_LegerEtTexture`. Validé par mutation (bras de la vue qui ne reprennent pas la tenue, main qui ne réapparaît pas, bras non réorienté).

### Les sons de tir, de pas et le taser (2026-10-12)

Demandé par l'utilisateur, carte blanche : « améliorer le projet en ajoutant des sound effects de tir, de bruits de déplacement etc. cohérents avec les jeux », et si possible des particules électriques sur le taser. Referme les dettes n° 11 (son de gameplay presque muet), n° 33 (rechargement muet) et n° 46 (sons d'armes provisoires et incohérents).

**Sources, deux licences libres sans attribution** (même famille que les fichiers déjà présents dans `SoundsBank`, au nommage `auteur-titre-id`) : **Freesound.org, filtré sur la licence CC0** (aucune attribution requise), et **Mixkit** (licence « Mixkit Free License », gratuite y compris en usage commercial, aucune attribution requise). Les pages de recherche de Pixabay (la source des fichiers déjà en place) sont protégées par Cloudflare et ne répondent pas à une requête directe (`curl` reçoit un défi JS, « Just a moment... ») ; Freesound et Mixkit, eux, répondent en HTML brut et exposent directement l'URL de prévisualisation (`cdn.freesound.org/previews/...-lq.mp3`, `assets.mixkit.co/.../*-preview.mp3`), ce qui a permis de les choisir par le TITRE plutôt qu'à l'oreille (aucun outil de lecture audio ici) — la durée du fichier (estimée par sa taille) a servi à écarter les enregistrements de RAFALE ou de séquence de plusieurs pas, impropres à un son joué en boucle par `PlayOneShot`.

**Les quatre armes** (`WeaponController.ResolveShot`, un clip tiré au hasard dans `fireSounds` à chaque coup) :
- **Fusil d'assaut** (`MP5.asset`) : ne tire plus au hasard parmi cinq sons sans rapport (goutte d'eau, pompe, deux lasers) — désormais deux tirs d'AK-47 / fusil d'assaut (`freesound-ak47-shot-855841`, `freesound-assault-rifle-burst-160590`). `reloadSound` : `freesound-machine-gun-reload-386777`.
- **Sniper** (`Arme_Sniper.asset`) : ne tire plus le bruit du pompe — deux tirs secs « FPS Sniper Shot » (`freesound-fps-sniper-shot-816114` et son `-alt-816113`, du même auteur, pensés pour un jeu). `reloadSound` : `freesound-gun-reload-647557`.
- **Fusil à pompe** (`Arme_Pompe.asset`) : le tir (`universfield-shotgun-blast-352038`, déjà en place) était déjà cohérent, inchangé. `reloadSound` (rechargement cartouche par cartouche) : une pompe de fusil à pompe (`mixkit-shotgun-pump-1659`).
- **Raygun** (`Arme_Raygun.asset`) : les deux sons de laser déjà en place restent, plus une troisième variation (`mixkit-laser-cannon-shot-1678`). `reloadSound` : une montée en puissance sci-fi (`mixkit-plasma-gun-powerup-1679`).

**Le taser** (`TaserController.strikeSound`) : un son d'électrocution dédié (`freesound-taser-zap-540012`), remplace le silence.

**Les sons de contexte du joueur** (`PlayerSoundBank`, `PlayerSounds.asset`) : quatre entrées étaient muettes (aucun clip), quatre autres n'existaient pas du tout.
- `FootstepWalk` (existant) : `Walk2.wav` était déjà dans le projet mais jamais câblé — ajouté en variation, avec un pas métallique de robot (`freesound-robot-footstep-815039`, série « Big robot footstep » d'AudioPapkin — la même série que la source Pixabay déjà utilisée ailleurs dans le projet, retrouvée sur Freesound sous licence CC0).
- `FootstepRun` (nouveau) : un pas de robot plus lourd de la même série (`freesound-robot-footstep-813053`).
- `FootstepCrouch` (nouveau) : un pas métallique dédié à l'accroupi (`freesound-footstep-metal-crouch-421151`).
- `LeanStart` / `LeanEnd` (nouveaux) : le même tic de servo (`mixkit-sci-fi-gear-turn-885`), en engageant au pitch plus haut et en relâchant au pitch plus bas.
- `CrouchDown` / `CrouchUp` (nouveaux) : le même verrou mécanique (`mixkit-gear-metallic-lock-2858`), même principe de pitch.
- `ProneDown` (nouveau) : un mouvement mécanique plus lourd (`mixkit-mechanical-movement-3117`).
- `ProneUp` (nouveau) : un verrou rapide (`mixkit-gear-fast-lock-tap-2857`).
- `Crawl` reste muet : aucun candidat libre de droits trouvé pour une reptation métallique. Pas forcé pour ne pas mettre un son qui sonnerait faux.

**Réglages d'import** : les 17 nouveaux fichiers sont passés en `forceToMono` (les deux `AudioSource` qui les jouent, sur `Player` et sur `Weapon`, sont en 3D plein, `spatialBlend = 1` — un clip stéréo n'y ferait entendre qu'un canal) et `Decompress On Load` (cohérent avec des sons courts rejoués souvent, y compris en rafale à 7 tirs/seconde).

**Les étincelles électriques du taser** (`TaserSparkFX.cs`, demande explicite de l'utilisateur, aucun asset externe — Unity suffit) : un `ParticleSystem` (rafale de 18 à 26 particules, 80 à 180 ms de vie, teinte cyan-blanc, module Noise pour un mouvement erratique) et une `Light` ponctuelle brève (80 ms), posés sous le repère `Poing` de `GantChoc.prefab`. **Catégorie C stricte** : rien ici n'est lu par la simulation, le hitbox ou le tir — le script écoute `TaserController.OnStrike` et joue l'effet `ContactDelay` plus tard, au moment du CONTACT plutôt qu'à l'appui (même principe que le son et l'animation du coup).

🚨 **Un seul script sert aux deux vues, sans code dupliqué.** `GantChoc.prefab` est instancié deux fois par joueur : une fois sous le `Model` (vue des autres joueurs) et une fois clonée par `FirstPersonView` sous la caméra (vue du propriétaire pendant son propre coup). `TaserSparkFX.Awake()` retrouve le `TaserController` du joueur par `transform.root.GetComponentInChildren<TaserController>()` — ça fonctionne pour les deux copies, parce que la copie de `FirstPersonView` reste un descendant de la racine du joueur même une fois accrochée à la caméra. Chaque copie joue donc l'effet au bon moment, sans RPC ni code spécifique au propriétaire.

**Mis en place par édition directe des champs sérialisés** (`set_serialized_field` du MCP Unity, pas d'édition manuelle de YAML) : les tableaux `fireSounds`/`entries` sont redimensionnés puis remplis élément par élément. Pour `TaserController.strikeSound`, qui vit sur un enfant (`Weapon`) du prefab joueur, la seule façon propre d'atteindre un champ niché dans un prefab est d'instancier temporairement le prefab dans la scène, éditer, `apply_prefab_overrides`, puis **détruire l'instance immédiatement** — jamais la laisser dans la scène réseau (voir le piège « JAMAIS d'instance du PlayerPrefab posée dans une scène réseau »).

**Vérifié** : 109 tests EditMode et 58 tests PlayMode toujours verts après coup (aucune régression — il s'agissait de données d'assets et d'un script cosmétique autonome, rien dans `Move()`, le hitbox ou le réseau n'a changé), console vierge.

⚪ **Valeurs et choix provisoires de Claude, à juger en jouant** : aucune des sources n'a pu être écoutée avant intégration (pas d'outil de lecture audio dans cet environnement) — le choix s'est fait sur le titre, l'auteur et la durée estimée. À réévaluer à l'oreille, en particulier le taser et les sons mécaniques de posture.

### Le buste suit le regard (2026-10-09)

Rapporté par l'utilisateur : « je vise dans la direction d'un ennemi, mais sur la vue de l'ennemi l'autre joueur ne regarde pas dans la bonne direction (quand je ne vise pas, si) ». **Mesuré** : immobile en visée, le fusil pointait **36° à gauche** du regard, et de −100° à +33° selon ce que jouaient les jambes. Le buste de visée est greffé par le masque sur des hanches qui ne sont pas tournées comme dans son propre clip ; ce que le clip compensait par une torsion du buste tombe à côté. Et le tangage n'existait pas chez les autres : un joueur qui regardait en l'air gardait son fusil à l'horizontale.

**La correction se mesure, elle aussi** (`PlayerAnimator.ApplyAimDirection`), sur la pose que l'Animator vient d'écrire, avant le lean : **en visée et au tir**, l'écart entre la mire du fusil et le regard, en cap et en tangage ; **arme basse**, l'écart entre le haut de la colonne et le regard, en cap seulement (le fusil reste tenu en travers, et un joueur qui court de côté garde le torse face à son regard). La rotation est lissée (0,12 s, le fusil garde un peu du balancement des pas), répartie sur les trois os de la colonne, bornée (`maxTorsoYaw` 120°, `maxTorsoPitch` 70°, 20° allongé), effacée avec le haut du corps pendant les transitions vers l'allongé, et **figée pendant le rechargement et le taser**. Fonctions pures testées : `TorsoCorrection`, `RotateSpine`.

**Le tangage voyage sur le réseau, catégorie C réseautée** : le propriétaire le joint à chaque input (`SubmitInputServerRpc`, borné à ±90°, un NaN ramené à 0 : `SanitizeAimPitch`), `Move()` le recopie sans s'en servir (`AimPitch`), le serveur le publie dans la pose datée, le spectateur l'interpole (`DisplayAimPitch`, même aiguillage que la visée). **Rien ne le relit** dans la simulation, le hitbox ou le tir : un client modifié peut mentir sur le sien comme sur son lean, et ne change alors que l'endroit où les autres VOIENT pointer son fusil. ⚠️ C'est une tromperie visuelle possible ; le serveur ne compare pas encore la direction d'un tir au regard publié.

Vérifié : `TorsoAimTests` (fusil à 0,1° du regard dans sept poses debout et accroupies, regard horizontal, 30° en haut, 25° en bas ; torse face au regard arme basse), validé par mutation (signe du tangage, visée et arme basse inversées). Capturé en jeu vu du dessus et de trois quarts : visée immobile, visée en pas chassé à gauche, regard à 30° en haut. À deux instances : le Host regardant 30° en haut, le clone affiche `tangage_affiche=-30°` et un buste incliné de 31°.

### Le viseur intégré : un écran numérique (2026-10-09)

Demandé par l'utilisateur : « de vrais viseurs dans le jeu », comme les points rouges et holographiques de *Call of Duty*, en commençant par celui du fusil fourni. GDD § 6 : chaque joueur choisira son viseur (style **et** grossissement) dans une armurerie, avant la partie.

**Le viseur du fusil est une lunette numérique fermée** : un écran plat à l'arrière (5,5 × 4,4 cm, sous-maillage `Mat.6`), une lentille ronde à l'avant, un boîtier plein entre les deux, avec un intérieur modélisé. Rendre l'écran transparent montrait l'intérieur gris du boîtier ; retirer les faces derrière l'écran (essayé en mémoire, jusqu'à 1 285 triangles) laissait voir tout un fouillis de pièces internes. **L'écran affiche donc ce que voit le viseur.**

**`WeaponSight`** (sur la racine de `FusilAssaut.prefab`) : en vue subjective seulement, `FirstPersonView` lui fait créer une **caméra du viseur** (`CameraViseur`) sous la caméra du joueur, au même endroit et dans le même axe, qui filme le décor dans une texture de 640 × 640, et pose sur l'écran **de la copie** une instance du matériau `ViseurEcran` (shader `DuelArena/ViseurEcran`). Le fusil vu par les autres garde son écran turquoise d'origine. **La caméra du viseur ne tourne que pendant la visée** : un rendu du décor de plus, en petit, à ce moment-là seulement.

**Le shader retrouve, en chaque point de l'écran, la direction du monde qu'il cache** : la direction réelle du point (repère de vue) multipliée par le rapport des champs du monde (90°) et des bras (60°, une autre caméra), puis lue dans l'image du viseur. Au grossissement 1, l'écran paraît transparent, raccord au pixel près avec le décor autour (vérifié à l'image) ; au grossissement M, la caméra du viseur se resserre (`2·atan(0,55 / M)`) et l'image grossit autour du centre (×2 vérifié à l'image). L'image s'allume pendant la seconde moitié de la montée à l'œil et se fond dans la veille près de ses bords : sinon, un rectangle d'image à bord net apparaissait pendant la montée. Lignes de balayage légères : on lit un écran, pas une vitre.

**La réticule marque le centre de l'écran du joueur, là où partent les balles** (le tir part de la caméra), même quand le fusil balance au rythme des pas. Elle se calcule dans le shader à partir de la même direction, en milliradians du champ du monde : à 90° de champ, un pixel vaut 2 à 3 mrad, d'où un point de **6 mrad** de rayon et un cercle de **30 mrad** (les premières valeurs, celles d'un vrai viseur, ne faisaient pas un pixel). Le **réticule du HUD s'efface** quand l'œil est dans l'axe (`FirstPersonView.SightOnAxis`, `CrosshairUI`) ; les munitions restent.

**La visée regarde au centre de l'écran** : la `Mire` est descendue au centre de l'écran du viseur, (0 ; 0,2245 ; 0,092) dans le repère du fusil (avant : le haut du boîtier). `aimSightPosition` vaut (0 ; 0 ; 0,10) : l'écran est centré sur le regard, à 10 cm de l'œil, et occupe environ 40 % de la hauteur de l'écran. `hipOffset` (0,12 ; −0,18 ; 0,27) garde l'arme à la hanche là où elle était. **Le viseur est mis d'aplomb** (`FirstPersonView.LevelSight`) : aligner l'axe seul laissait le roulis de la pose jouée, et l'écran du viseur penchait de 2 à 3° en jeu.

**Gardé par** `WeaponSightTests` (la `Mire` au centre de l'écran à 2 mm près et dans l'axe du canon, la visée centrée ; l'écran numérique posé sur la copie seulement, sa caméra arrêtée à la hanche et au champ du grossissement en visée) et `FirstPersonViewTests` (placement d'aplomb). Validé par mutation : sans remise d'aplomb, le viseur penche de 8,5° dans le test ; une caméra qui tourne à la hanche ; un écran qui n'affiche pas l'image du viseur.

**Réglages de ressenti** : `aimSightPosition` (distance de l'œil), `armsFieldOfView`, et sur le matériau `ViseurEcran` la réticule (taille, couleur, cercle), la teinte et les lignes. Le grossissement (`WeaponSight.magnification`) attend l'armurerie.

**Pour les futurs viseurs à vitre** (point rouge, holographique) : `DuelArena/ViseurReticule` est prêt et inutilisé. Vitre teintée, réticule « à l'infini » dans l'axe du fusil ou de la caméra (`_AxeCamera`), vérifiée sur un rendu : elle reste sur la cible quand l'œil se décale. Les tailles par défaut de sa réticule sont celles d'un vrai viseur, donc trop petites : reprendre celles de l'écran numérique.

⚠️ La caméra du viseur rend sans post-traitement. Le jour où le jeu en aura un (étalonnage, bloom), l'image de l'écran s'en distinguera.

## Configuration Git (posée le 2026-09-22)

`.gitattributes` couvre trois choses : normalisation des fins de ligne (`* text=auto`), **Unity Smart Merge** sur les fichiers YAML d'Unity, et **Git LFS** sur les binaires (audio, images, modèles 3D, vidéo, polices, DLL).

Configuration locale correspondante (dans `.git/config`, **pas** dans le global) :
- `git lfs install --local` — filtres LFS + hooks.
- `merge.unityyamlmerge.driver` → `UnityYAMLMerge.exe` de Unity 6000.6.0f1. **À repointer si la version d'Unity du projet change.**

Deux limites à connaître :
- **LFS ne s'applique qu'aux fichiers ajoutés ou modifiés après coup.** Les 7 `.wav` déjà commités restent des blobs normaux dans l'historique. Sans conséquence (dépôt à 1,8 Mo), mais si on veut les rapatrier : `git lfs migrate import --include="*.wav" --everything` — c'est une **réécriture d'historique**, triviale ici puisqu'il n'y a pas de remote, mais elle change tous les hashes de commit.
- `* text=auto` ne normalise que ce qui est touché ensuite. Pour l'appliquer à tout d'un coup : `git add --renormalize .` puis un commit dédié — ça touche tous les fichiers texte, donc à faire en isolation, jamais mélangé à un vrai changement.

Les `.asset` d'Unity sont du YAML texte (projet en Force Text) : ils passent par Smart Merge, pas par LFS. **Exception depuis le 2026-10-08** : les meshes générés par fusion (`*_Fusionne.asset`, comme le fusil, 8 Mo de sommets encodés) vont en LFS — illisibles, sans intérêt à fusionner, et chaque régénération alourdirait l'historique d'autant. Si le projet basculait en Force Binary, il faudrait les déplacer côté LFS.

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
   - ~~**Couleur par joueur**, attribuée par le serveur~~ — **livrée le 2026-10-08** (voir la section bible plus bas). Palette à juger en jouant.
   - ~~**Lean façon R6 et hitbox par zones**~~ — **livrés le 2026-10-05**. Amplitude et vue accroupie validées en jouant le 2026-10-06. **Lean allongé refait en roulis** (« exactement ça ») et **transitions de posture** livrés et validés en jouant le 2026-10-06. L'inclinaison du lean et le retour droit, « encore un peu bruts et secs », sont amortis le même jour : à rejuger. Dettes ouvertes : le corps allongé qui dépasse du collider (n° 24), la vue du Host par à-coups (n° 30).
   - ~~**Son**~~ — tirs, pas, rechargement et taser pourvus le 2026-10-12 (voir « Les sons de tir, de pas et le taser »), à juger en jouant. Restent, à la main de l'utilisateur et sans urgence : `Crawl` muet, et la portée par allure.
4. **Compléter le duel** : ~~chargeur et rechargement~~ (**livrés le 2026-10-08**, 30 balles et 2,2 s à juger en jouant), ~~**taser** de corps à corps qui tue en un coup, action brève au choix du joueur comme le couteau rapide de *Call of Duty*~~ (**livré le 2026-10-08**, réglages à juger en jouant ; GDD § 6), ~~arme visible~~ (fusil fourni par l'utilisateur, en place le 2026-10-08), ~~viseur intégré~~ (écran numérique, livré le 2026-10-09 ; l'armurerie, où chacun choisira style et grossissement, est à faire), ~~sprint vers l'avant, rechargement et taser qui l'arrêtent~~ (2026-10-10), ~~animations sans glissade~~ (2026-10-10, anneau lent debout complété le 2026-10-11), ~~sortie de sprint et élan~~ (2026-10-11, 0,3 s et 25 m/s² à juger en jouant), ~~quatre armes, touches 1 à 4, et le gant du taser~~ (2026-10-11, valeurs à juger en jouant ; le choix de l'arme attend la règle du GDD § 6), marqueur et sons de touche, effet de dégâts reçus, vraie mort, HUD minimal, phrase de victoire.
5. **Le modèle de session : N joueurs, deux duellistes.** Voir ci-dessous.
6. **Lobby avec code + Relay** (Unity Services), pour le playtest entre amis. C'est aussi ce qui sort : **des lobbys privés à code, et aucune file publique**. Le classé, et avec lui le serveur dédié, l'anti-triche client et le matchmaking par région, viennent après la sortie (tranché le 2026-10-04). Aucun service de matchmaking n'est donc à intégrer d'ici là. *(Palier intermédiaire livré le 2026-10-12 : un menu principal avec Héberger/Rejoindre par IP+port sur le réseau local, voir « Le menu principal ». Suffisant pour jouer entre amis sur le même réseau ; Relay reste nécessaire pour des amis sur des réseaux différents.)*

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

**Livré le 2026-10-08** (`PlayerPaint`, sur la racine du prefab joueur). Le serveur prend dans `palette` la **première couleur qu'aucun joueur présent ne porte** (`PickPaint`, fonction pure ; au-delà de la palette, la moins portée) et la publie dans une `NetworkVariable<Color32>`. Un alpha nul veut dire « pas encore de peinture ». Chaque machine l'applique par `MaterialPropertyBlock` (`_BaseColor`) aux seules pièces de `paintedRenderers` : le corps (`Alpha_Surface`) prend la couleur, les **articulations gardent leur gris**, pour rester visibles comme le veut la bible. Aucun matériau dupliqué, donc rien à détruire au départ d'un joueur.

**Palette provisoire** (choix de Claude, à juger en jouant) : orange, bleu, magenta, vert, cyan, violet, jaune, dans cet ordre. Les deux premières, celles des deux duellistes d'une partie à deux, sont opposées sur le cercle des couleurs, et restent distinctes pour les daltonismes les plus courants. **Pas de rouge** : la bible ne veut pas que les joueurs partagent la couleur du danger, et les dégâts s'afficheront probablement en rouge. `PlayerPaintTests` lit la palette **du prefab** et exige des couleurs vives (saturation ≥ 0,6, luminosité ≥ 0,7), au moins 20° de teinte entre deux couleurs, au moins quatre couleurs (les places du prototype) et deux premières à plus de 120° l'une de l'autre. Validé par mutation (une couleur grise, une couleur en double, des articulations peintes, un serveur qui ignore les couleurs prises).

Vérifié à deux instances : orange pour le Host, bleu pour le client, identiques sur les deux machines (`[Peinture]` dans le journal du joueur virtuel). Une couleur se libère au départ de son joueur et revient au suivant.

⚠️ `paintedRenderers` référence une pièce **du modèle** : à refaire quand le robot définitif remplacera Y Bot. `LePrefab_PeintLeCorpsDuModele_PasLesArticulations` échouera si la référence est perdue.

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
