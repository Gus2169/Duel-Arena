# FPS Claude — Duel Arena

FPS de **duel 1v1** en Unity 6 (6000.6.0f1), URP, multijoueur via Netcode for GameObjects 2.13.2.

## Comment ce projet se pilote

**`Docs/GDD-Duel-Arena.md` est le seul document qui fait autorité, et il ne parle que de design** — la vision, les piliers, les règles non négociables, ce que le jeu refuse d'être. C'est l'âme du jeu : à consulter avant toute décision qui touche au ressenti, à l'équilibrage ou au contenu, et à mettre à jour quand une décision de design est prise.

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
- **Git** : dépôt local, pas de remote. **Committer seulement sur demande explicite.**

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
- Le **décalage** est calculé par le serveur (`ServerAdvanceLean`, avec le même anti-clipping que le client) et publié dans `networkLeanOffset`.
- Le **propriétaire** utilise sa valeur locale prédite (`LeanOffset`), pour que sa caméra et son corps n'aient aucune latence.

Chez un spectateur, le lean d'un adversaire est affiché avec un retard d'environ un RTT — comme la position. **Le rewind le compense** : l'historique serveur porte le décalage de lean au même titre que la position (voir plus bas).

### Hit registration

Le tireur raycast en local pour son feedback visuel instantané (**aucun dégât associé**), envoie `origin`/`direction` au serveur qui refait SEUL le raycast et décide SEUL des dégâts. `Health` est un `NetworkBehaviour` dont `Current` est une `NetworkVariable<float>` en écriture serveur uniquement (avec un mode de secours non-réseauté pour une cible de test isolée) ; `ApplyDamage` refuse tout appel client direct sur un objet réseauté.

**Résolution en DEUX traces** (`WeaponController.ResolveShot`, partagée entre le feedback local et la décision serveur, pour qu'elles ne puissent pas diverger) :

1. **Le monde** (`WeaponData.worldMask` = `Default`), qui arrête les balles. **Sans les joueurs** : leur `CharacterController` ne suit pas le lean, donc le laisser bloquer les tirs ferait réapparaître par la bande le trou que le hitbox ferme.
2. **Les hitbox** (`WeaponData.hitboxMask` = layer `Hitbox`), limitée à la distance du mur touché — c'est ce qui fait qu'un adversaire derrière une caisse ne prend rien.

Le tireur retire son propre hitbox de la requête le temps du tir (`try/finally` — un hitbox laissé désactivé rendrait le tireur invulnérable pour le reste de la partie).

### Compensation de latence — le rewind (2026-09-24)

Le serveur valide chaque tir contre l'état des adversaires **au moment où le tireur a réellement visé**, pas au moment où la RPC arrive. Sans ça, un tireur qui vise juste rate une cible en mouvement : l'écart vaut à peu près `(RTT/2 + délai d'interpolation) × vitesse`, soit plus d'un mètre à 150 ms de ping et 8 m/s — largement la largeur d'un joueur.

**Mesure du délai, sans horloge partagée.** Le RTT est chronométré **gratuitement**, en mesurant l'aller-retour prédiction/réconciliation qui existe déjà : un timestamp dans `PendingInput`, relu quand la correction de cette séquence revient, puis lissé. Aucune RPC de ping dédiée, aucune dépendance à la synchronisation d'horloge de Netcode. Le délai suggéré est `RTT/2 + interpolationDelay` (`EstimatedRewindSeconds`), et vaut **0 pour le Host**, qui voit les autres joueurs à la position qu'il simule lui-même (cas 3) — il n'a rien à compenser.

Le RTT ainsi mesuré inclut l'attente de l'input dans la queue serveur, donc surestime légèrement le RTT réseau pur. **C'est le bon biais** : ce qu'on cherche n'est pas la latence théorique, mais l'âge réel de ce que le tireur voit.

**Le client suggère, le serveur clampe.** `rewindSeconds` est le seul paramètre « libre » accepté par `FireServerRpc`, et il est borné par `maxRewindSeconds` (0,3 s — couvre ~400 ms de ping légitime). Sans ce clamp, un client modifié annoncerait un ping énorme pour tuer ses adversaires là où ils étaient il y a une éternité. Au-delà de cette borne, c'est la **victime** qui subit l'injustice, en mourant à couvert.

**L'historique porte la pose COMPLÈTE** : position, yaw, décalage de lean et posture (`HitboxPose`, fenêtre glissante d'une seconde, alimentée à chaque frame serveur — même sans input traité, sinon l'historique aurait des trous pendant les micro-coupures, précisément quand le rewind sert le plus). Historiser la seule position ne corrigerait qu'une part du décalage : un adversaire penché ou accroupi au moment du tir serait rewind avec la géométrie qu'il a *maintenant*. La posture étant discrète, l'échantillonnage garde **celle d'avant** plutôt que d'inventer un état intermédiaire — le choix conservateur du point de vue de la cible.

**Seuls les hitbox voyagent dans le passé**, jamais les joueurs : la simulation continue sur les vraies positions, et tout se déroule de façon synchrone dans le handler de la RPC, donc invisible pour le reste du jeu. C'est exactement ce que le hitbox séparé rend possible.

🚨 **`Physics.SyncTransforms()` est obligatoire** avant ET après le déplacement des hitbox. `Physics.autoSyncTransforms` vaut **false** par défaut (vérifié dans ce projet) : déplacer un transform ne met pas à jour la scène physique utilisée par les requêtes. Sans cette synchronisation, le raycast verrait les hitbox à leur position actuelle et **tout le rewind serait silencieusement sans effet** — le pire des échecs, puisqu'il ne se voit pas.

Contrainte de configuration : `maxRewindSeconds` (sur `WeaponController`) doit rester **inférieur** à `hitboxHistoryDuration` (sur `PlayerLocomotion`), sinon on demande à l'historique une pose qu'il a déjà jetée.

### Le hitbox (2026-09-24)

`PlayerHitbox` (enfant `Hitbox` du prefab joueur) porte un `CapsuleCollider` **trigger** sur le layer **`Hitbox` (7)**. Trigger et layer sont forcés dans `Awake()`, pas laissés à l'inspecteur : ce sont des invariants, et les deux se règlent silencieusement mal en un clic (un hitbox non-trigger repousserait physiquement les joueurs, un mauvais layer le rendrait invisible au tir ou bloquerait les balles comme un mur).

Ses dimensions et sa position sont une **fonction pure de (posture réseau, décalage de lean)** — aucune interpolation locale, donc une surface identique sur toutes les machines. La **capsule visible utilise exactement les mêmes valeurs** : on touche ce qu'on voit.

Ce que ça débloque, au-delà du lean : le `CharacterController` peut être désactivé (vault) sans que le joueur cesse d'être touchable, et le rewind aura une surface dédiée à déplacer dans le passé sans toucher à la simulation.

⚠️ **Le corps ne s'interpole plus entre postures** — il claque. Seule la hauteur caméra garde son lissage. C'est assumé : un lissage local des dimensions ferait diverger la silhouette d'un écran à l'autre, donc viser un corps que le serveur n'a pas au même endroit. Un vrai personnage animé rendra la transition fluide visuellement.

### Passer à un lean humanoïde plus tard — ce qui change et ce qui ne change pas

Le lean actuel fait *glisser toute la capsule* sur le côté. Avec un vrai personnage, on voudra que **seul le haut du corps se penche**, les jambes restant en place.

**Ce qui ne bouge pas** : la grandeur réseautée reste **un seul scalaire**. Qu'il pilote un glissement latéral ou une rotation du buste est une question de géométrie et d'affichage, pas de réseau. Le travail difficile (faire connaître le lean au serveur) ne sera pas à refaire, et le rewind n'aura pas plus d'état à historiser.

**La couture** est `PlayerHitbox.Apply(...)`. Elle prendra une posture + un scalaire de lean et produira *plusieurs* capsules (jambes fixes, buste/tête pivotant) au lieu d'une. `PlayerLocomotion` et `WeaponController` n'ont pas à changer.

🚨 **Règle à ne pas enfreindre : ne JAMAIS dériver le hitbox des os animés.** Un Animator n'est pas déterministe entre machines (blending, vitesse d'animation, `LateUpdate`, root motion), donc un hitbox accroché aux os ferait diverger la surface touchable d'un écran à l'autre — exactement le problème qu'on vient de fermer, réintroduit par la porte de derrière. **L'animation AFFICHE le lean ; le hitbox se CALCULE à partir du même scalaire réseauté.** Les deux lisent la même source, aucun ne lit l'autre.

**Conséquence de gameplay, à arbitrer côté GDD** : aujourd'hui, pencher met le corps *entier* à l'abri — les jambes se téléportent derrière la couverture avec le reste, ce qui rend le peek un peu trop généreux. Un lean humanoïde exposerait la tête et l'épaule en laissant les jambes vulnérables : plus honnête, et plus proche du peek des FPS compétitifs.

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

- `WeaponController.FireServerRpc` rejette : un tir plus rapide que `data.shotsPerSecond` (tolérance 15 %), une direction nulle, et une **origine trop éloignée de la position serveur du tireur** (`maxOriginDistanceFromPlayer`, 4 m — couvre hauteur caméra + lean + avance de prédiction sous latence ; à resserrer quand le rewind sera en place).
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
- **Ressenti « saccadé » en build** : vérifier d'abord le Packet Delay Ms du Debug Simulator avant de soupçonner le réseau ou le code. Contrainte réelle séparée : 2 instances complètes sur une machine coûtent cher en perf.
- **`obstacleMask`** (utilisé par `CheckCapsule`/`CheckSphere`, pas seulement des raycasts) doit exclure le layer du joueur : `obstacleMask &= ~(1 << gameObject.layer)` dans `Awake`.
- **`Destroy()` est DIFFÉRÉ à la fin de la frame — la physique, elle, voit encore l'objet.** `GameObject.CreatePrimitive` livre toujours un collider ; le détruire ne suffit pas, il faut le **désactiver immédiatement** (`col.enabled = false`), ce qui prend effet tout de suite. Symptôme vécu le 2026-09-24 : l'adversaire **reculait visiblement à chaque balle encaissée**, parce que le marqueur d'impact naissait à la surface de son corps avec un `SphereCollider` vivant une frame, dont son `CharacterController` se poussait au `Move()` suivant. Conséquences plus sournoises encore : la sphère naissant sur le layer `Default` (celui de `worldMask`), elle **bloquait les tirs suivants comme un mur**, et comptait comme obstacle pour `CanStandUp()` et l'anti-clipping du lean. **Tout objet purement visuel créé pendant le jeu doit naître sans collider actif.**
- **Ordre recul/raycast dans `Fire()`** : le recul s'applique APRÈS avoir déterminé où le tir atterrit, sinon chaque tir est décalé par son propre recul.
- **Near Clip Plane** gardé petit (~0.03-0.05), sinon un mur très proche disparaît du rendu.
- **Enum sérialisé** : Unity le sérialise par sa valeur entière, pas par son nom — préserver l'ordre existant en ajoutant des valeurs (cf. `PlayerSoundEvent`).

## Structure du projet

**Prefab joueur unique** : `Assets/_ProjectArena/PlayerPrefab/Player.prefab` — `NetworkObject` + `PlayerLocomotion` + `WeaponController` + `PlayerInputReader` + `PlayerCameraLook` + `CrosshairUI` + `WeaponVisualFeedback` + `PlayerSoundEmitter`. Référencé par GUID dans `NetworkManager.PlayerPrefab` et `Assets/DefaultNetworkPrefabs.asset`.

**Scènes** :
- `Assets/_ProjectScenes/MultiTestScene.unity` — seule scène avec un `NetworkManager` (UnityTransport, 127.0.0.1:7777, local uniquement). Scène de test multijoueur.
- `Assets/_ProjectScenes/Arena.unity` — scène de jeu d'origine, **pas encore migrée au multijoueur**.

Les deux référencent un asset Terrain à la racine d'`Assets/` (`New Terrain.asset`, `New Terrain 1.asset`) — ils ont l'air de traîner mais **ils sont utilisés**, ne pas les supprimer sans vérifier.

**Placeholders assumés** (à remplacer avec le vrai système d'armes / le vrai HUD, pas des bugs) : `CrosshairUI` (réticule OnGUI), `WeaponVisualFeedback` (tracer/impact procéduraux), `NetworkBootstrapUI` (boutons Host/Server/Client en OnGUI).

`CubeTest` (dans `MultiTestScene`) est un `NetworkObject` in-scene **désactivé**, donc `TestSpin.cs` ne tourne jamais. Il n'est pas impliqué dans le bug du joueur fantôme (il n'est pas le `PlayerPrefab`), mais c'est la même famille de piège : à supprimer au prochain nettoyage, ce qui rendra `TestSpin.cs` mort à son tour.

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

**Décision de design en attente** : `roundTimeLimit` (60 s) rend la manche **nulle** sans que personne ne marque. Face à deux joueurs passifs, ça peut se répéter indéfiniment. Le GDD vise des manches de 15-30 s ; à trancher par playtest (mort subite ? double défaite ? réduction de l'arène ?).

L'affichage `OnGUI` du `RoundManager` est un **placeholder** au même titre que `NetworkBootstrapUI`, à remplacer par le HUD UI Toolkit.

## Dettes techniques (audit du 2026-09-21, vérifié dans le code)

1. ✅ **Corrigé** — speedhack par flood d'inputs (borne par frame → budget sur le temps réel).
2. ✅ **Corrigé** — `origin` de tir non validée dans `FireServerRpc`.
3. ✅ **Corrigé le 2026-09-22** — réconciliation sans seuil d'erreur. Le client ne se recale plus qu'au-delà de `positionReconciliationThreshold` (5 cm) ou `yawReconciliationThreshold` (1°), au lieu de recaler + rejouer à chaque frame.
4. **Un état échappe encore à la réconciliation.**
   - ✅ **Corrigé le 2026-09-22** — `currentVelocity` est désormais renvoyée par le serveur dans la correction et appliquée avant le rejeu.
   - ✅ **Corrigé le 2026-09-22** — la hauteur/le rayon du `CharacterController` étaient interpolés dans `Update()` avec le `Time.deltaTime` local de chaque instance, donc client et serveur ne simulaient pas avec la même capsule pendant une transition de posture. La capsule de **collision** est désormais une fonction PURE de `networkStance` (instantanée, `ApplySimulationCapsule`), appliquée depuis `Move()` pour être correcte aussi pendant un rejeu. La hauteur caméra et la **capsule visuelle** gardent leur interpolation douce dans `UpdateStanceVisuals()` (catégorie C, sans effet sur la simulation).

   **Choix assumé** : supprimer l'état cumulatif plutôt que d'ajouter une hauteur confirmée de plus dans la RPC de correction. Conséquence : bref décalage (~0,1 s) entre la capsule visible et celle qui entre en collision pendant une transition. Se relever reste protégé par `CanStandUp()`, donc la capsule debout instantanée ne peut pas faire traverser un plafond.
5. ✅ **Corrigé le 2026-09-22** — spawn téléporté à l'origine du monde. `OnNetworkSpawn` appliquait `transform.position = networkPosition.Value` inconditionnellement, alors que la `NetworkVariable` vaut encore `default` = (0,0,0) à cet instant. Le serveur publie désormais sa position de spawn, et seuls les clients s'y alignent.

7. ✅ **Corrigé le 2026-09-22** — les deux joueurs apparaissaient au même endroit, `NetworkManager` instanciant le prefab à sa propre position pour tout le monde. Voir « Points de spawn » ci-dessous.

6. ✅ **Corrigé le 2026-09-22** — GPU Resident Drawer incompatible avec la géométrie ProBuilder. `PC_RPAsset.asset` avait `m_GPUResidentDrawerMode: 1`, ce qui noyait la Console sous ~150 erreurs `BatchDrawCommand was submitted with an invalid Batch, Mesh, or Material ID` à chaque ouverture de scène — sans effet visible en jeu, mais ça masquait les vraies erreurs. Passé à 0 ; son gain est nul à l'échelle d'une arène de duel. **À reconsidérer seulement si la géométrie finale n'est plus du ProBuilder et que le nombre d'objets explose.**

8. ✅ **Corrigé le 2026-09-24** — le collider accidentel sur l'enfant `Capsule` (reste de la primitive Unity) a été supprimé. La surface touchable est désormais le seul `PlayerHitbox`, explicite et dédié.
9. ✅ **Corrigé le 2026-09-24** — `hittableMask` valait `Everything` ; remplacé par deux masques explicites, `worldMask` (géométrie) et `hitboxMask` (layer `Hitbox`), utilisés par deux traces distinctes.

**Hygiène** : ✅ **assemblies et tests posés le 2026-09-28** — voir la section ci-dessous.

## Assemblies et tests (2026-09-28)

Le code de jeu a quitté `Assembly-CSharp` pour deux assemblies : `DuelArena.Input` (`_ProjectInput`, l'asset Input Actions généré) et `DuelArena.Runtime` (`_ProjectScripts`, qui référence la première, Netcode et l'Input System).

Ce n'était pas qu'une question de temps de compilation : **un assembly de test ne peut pas référencer `Assembly-CSharp`**, l'assembly prédéfini. Sans asmdef, aucun test ne pouvait voir le code du jeu. Vérifié après coup : toutes les références de scripts dans le prefab joueur et la scène ont survécu (les GUID de fichiers ne changent pas), et le jeu tourne.

**Tests EditMode** sous `Assets/Tests/EditMode` (`DuelArena.Tests.EditMode`). Lancer : `unity command run_tests --project-path "..." --mode EditMode`. 23 tests au 2026-09-28, tous verts.

Ils couvrent volontairement la **logique pure**, là où une régression est à la fois probable et silencieuse :
- `SampleHitboxHistory` — le cœur du rewind. Un échantillonnage cassé ne lève aucune erreur, il fait juste rater des tirs qui auraient dû toucher. Couvre l'interpolation position/lean, le `LerpAngle` du yaw (un `Lerp` ferait tourner le hitbox à l'envers entre 350° et 10°), la posture non interpolée, les bornes, la division par zéro et l'historique vide/null.
- `PlayerSpawnPoints` — dont le cas réel de la boucle BO5 : deux joueurs morts au même endroit doivent repartir à deux extrémités.
- Les tables son ↔ posture/allure : le GDD fait du son une information de gameplay, donc un mauvais mapping est une information *fausse*, pas un détail cosmétique.

`SampleHitboxHistory` a été **extraite en fonction pure statique** pour cette raison : sous cette forme elle se teste sans Editor, sans réseau et sans scène.

🚨 **Un test qui passe ne prouve rien tant qu'on ne l'a pas vu échouer.** Les tests ont été validés par mutation : en remplaçant `Mathf.LerpAngle` par `Mathf.Lerp` dans l'échantillonnage du yaw, `YawInterpole_ParLePlusCourtChemin` échoue bien (22/23), puis repasse au vert une fois le code restauré. À refaire pour tout nouveau test non trivial.

**Ce qui n'est PAS couvert** : le déterminisme de `Move()` lui-même, qui demanderait un test PlayMode avec une session Netcode. C'est la garantie la plus précieuse du projet et elle reste vérifiée à la main.

## Configuration Git (posée le 2026-09-22)

`.gitattributes` couvre trois choses : normalisation des fins de ligne (`* text=auto`), **Unity Smart Merge** sur les fichiers YAML d'Unity, et **Git LFS** sur les binaires (audio, images, modèles 3D, vidéo, polices, DLL).

Configuration locale correspondante (dans `.git/config`, **pas** dans le global) :
- `git lfs install --local` — filtres LFS + hooks.
- `merge.unityyamlmerge.driver` → `UnityYAMLMerge.exe` de Unity 6000.6.0f1. **À repointer si la version d'Unity du projet change.**

Deux limites à connaître :
- **LFS ne s'applique qu'aux fichiers ajoutés ou modifiés après coup.** Les 7 `.wav` déjà commités restent des blobs normaux dans l'historique. Sans conséquence (dépôt à 1,8 Mo), mais si on veut les rapatrier : `git lfs migrate import --include="*.wav" --everything` — c'est une **réécriture d'historique**, triviale ici puisqu'il n'y a pas de remote, mais elle change tous les hashes de commit.
- `* text=auto` ne normalise que ce qui est touché ensuite. Pour l'appliquer à tout d'un coup : `git add --renormalize .` puis un commit dédié — ça touche tous les fichiers texte, donc à faire en isolation, jamais mélangé à un vrai changement.

Les `.asset` d'Unity sont du YAML texte (projet en Force Text) : ils passent par Smart Merge, pas par LFS. Si le projet basculait en Force Binary, il faudrait les déplacer côté LFS.

Point mineur laissé tel quel : `QualitySettings` référence encore un pipeline de rendu Mobile, inutile pour un FPS compétitif PC. Sans impact, à nettoyer si on touche aux settings de rendu.

## Ordre de travail

*(Livré le 2026-09-24 : le hitbox séparé, puis le rewind. **Avec eux, la Phase 1 n'a plus de chantier structurel ouvert** — le duel 1v1 est techniquement solide. Ce qui suit est du contenu et du flow, plus des fondations.)*

*(Livré le 2026-09-24 : la boucle de manches BO5. Voir la section dédiée plus haut.)*

1. **Vault réseauté** — le hitbox le débloque, voir ci-dessous.
2. **Migrer le multijoueur vers `Arena.unity`.**
3. **Lobby / Relay** (Unity Services), puis serveur dédié — le mode host-joueur donne un avantage de latence à l'hôte, inacceptable en 1v1 compétitif (règle du GDD).

## Ce qu'il reste à valider (test Host + Client)

Le hitbox et le rewind ont été vérifiés en Play Mode : géométrie, layer, trigger, suivi du lean, tirs de contrôle qui touchent le corps penché et ratent l'ancien centre, et pour le rewind un déplacement de 3 m qui fait rater le tir sans compensation et le fait toucher avec — puis une restauration vérifiée dans les deux sens. Mais **le ressenti et le jeu à deux ne se vérifient qu'en jouant** — c'est un arbitrage qui revient à l'utilisateur.

Pour le rewind, le test qui compte : **Packet Delay à 100-150 ms, viser un adversaire qui strafe**. Les tirs doivent toucher là où il est *affiché*, pas derrière lui. Et l'inverse à surveiller — c'est le prix du rewind, assumé par tous les FPS : en tant que cible, on peut désormais mourir *juste après* s'être mis à couvert. Si ça paraît excessif, c'est `maxRewindSeconds` qu'il faut baisser.

À tester depuis l'instance **CLIENT** :
1. **Peek en lean** — le test qui compte. Un adversaire qui penche derrière un angle doit être touchable sur le flanc qu'il expose, et *seulement* là. Tirer sur sa position « droite » (là où était l'ancien collider) ne doit plus rien faire.
2. **Tirs derrière une couverture** — un adversaire derrière une caisse ne doit rien prendre (la trace monde borne la trace hitbox).
3. **Tir sur soi-même** — impossible par construction, mais à confirmer : aucun dégât ne doit s'appliquer au tireur, même en lean appuyé.
4. **Postures** — accroupi et prone doivent être plus difficiles à toucher, proportionnellement à leur capsule. Le corps « claque » désormais entre postures au lieu de glisser : à confirmer que ça ne choque pas visuellement.
5. **Sous latence (100-150 ms)** — le lean d'un adversaire est en retard d'un RTT chez le spectateur. Attendu tant que le rewind n'est pas là ; à mesurer pour savoir si c'est gênant en duel.
6. **Console** — aucun `[Serveur] Tir rejeté`, aucun warning `layer Hitbox mais aucun Health parent`.

Réglages si besoin : `positionReconciliationThreshold` / `yawReconciliationThreshold` sur `PlayerLocomotion`, `maxOriginDistanceFromPlayer` sur `WeaponController`.

## Vault : désactivé, pas cassé

`HandleVaultInput()` et `ProcessVault()` existent toujours dans `PlayerLocomotion.cs` mais **ne sont appelés depuis nulle part** — débranchés d'`Update()` pendant la fusion réseau. `IsVaulting` reste donc toujours `false`, et comme le vault était la seule chose branchée sur l'action Jump, **la touche de saut ne fait plus rien** : `JumpPressedThisFrame` est positionné puis consommé sans que personne ne le lise.

Ce n'est pas une régression. La raison : le vault est un mouvement scripté à durée fixe qui fait `controller.enabled = false` pendant un lerp de position — un état « hors contrôle » incompatible avec le `Move()` par frame que la prédiction/réconciliation rejoue. Le réactiver demande un vrai incrément réseau (vault prédit côté propriétaire + confirmé serveur), pas juste de rappeler la fonction.

**À ne pas oublier** : l'utilisateur y tient, c'est une mécanique du GDD.

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
