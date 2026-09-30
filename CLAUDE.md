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
- 🚨 **Le `DebugSimulator` d'`UnityTransport` NE FAIT RIEN** en Netcode 2.13.2 — il est marqué `[Obsolete("no longer supported and has no effect")]`. Régler son `Packet Delay Ms` n'a aucun effet : mesuré le 2026-09-28, RTT de 5 ms avec le champ à 75 ms. **Conséquence historique : tous les « tests sous latence » de ce projet antérieurs à cette date se sont en réalité déroulés à ~5 ms de RTT.** Et l'ancien piège documenté ici (« un Packet Delay oublié à 100-150 ms explique un ressenti saccadé ») était faux — ce réglage ne peut rien causer. Voir « Tester sous latence » pour la méthode qui marche.
- **Ressenti « saccadé » en build** : 2 instances complètes sur une machine coûtent cher en perf, c'est la piste réelle à examiner avant de soupçonner le réseau ou le code.
- **`obstacleMask`** (utilisé par `CheckCapsule`/`CheckSphere`, pas seulement des raycasts) doit exclure le layer du joueur : `obstacleMask &= ~(1 << gameObject.layer)` dans `Awake`.
- **`Destroy()` est DIFFÉRÉ à la fin de la frame — la physique, elle, voit encore l'objet.** `GameObject.CreatePrimitive` livre toujours un collider ; le détruire ne suffit pas, il faut le **désactiver immédiatement** (`col.enabled = false`), ce qui prend effet tout de suite. Symptôme vécu le 2026-09-24 : l'adversaire **reculait visiblement à chaque balle encaissée**, parce que le marqueur d'impact naissait à la surface de son corps avec un `SphereCollider` vivant une frame, dont son `CharacterController` se poussait au `Move()` suivant. Conséquences plus sournoises encore : la sphère naissant sur le layer `Default` (celui de `worldMask`), elle **bloquait les tirs suivants comme un mur**, et comptait comme obstacle pour `CanStandUp()` et l'anti-clipping du lean. **Tout objet purement visuel créé pendant le jeu doit naître sans collider actif.**
- **Ordre recul/raycast dans `Fire()`** : le recul s'applique APRÈS avoir déterminé où le tir atterrit, sinon chaque tir est décalé par son propre recul.
- **Near Clip Plane** gardé petit (~0.03-0.05), sinon un mur très proche disparaît du rendu.
- **Enum sérialisé** : Unity le sérialise par sa valeur entière, pas par son nom — préserver l'ordre existant en ajoutant des valeurs (cf. `PlayerSoundEvent`).

## Structure du projet

**Prefab joueur unique** : `Assets/_ProjectArena/PlayerPrefab/Player.prefab` — `NetworkObject` + `PlayerLocomotion` + `WeaponController` + `PlayerInputReader` + `PlayerCameraLook` + `CrosshairUI` + `WeaponVisualFeedback` + `PlayerSoundEmitter`. Référencé par GUID dans `NetworkManager.PlayerPrefab` et `Assets/DefaultNetworkPrefabs.asset`.

**Scènes** :
- `Assets/_ProjectScenes/MultiTestScene.unity` — seule scène avec un `NetworkManager` (UnityTransport, 127.0.0.1:7777, local uniquement). Scène de test multijoueur.
- `Assets/_ProjectScenes/Arena.unity` — scène de jeu d'origine, **conservée pour mémoire seulement**. Sa géométrie d'arène a été reportée dans `MultiTestScene`, qui est donc la scène de travail unique. Aucune migration à prévoir (tranché le 2026-09-29) : il n'y a rien dans `Arena.unity` qui n'existe déjà ailleurs.

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

## Tester sous latence, et l'outillage de diagnostic (2026-09-28)

**Simulateur réseau.** `com.unity.multiplayer.tools` 2.2.12 est installé pour ça (le `DebugSimulator` d'`UnityTransport` est obsolète et sans effet, voir les pièges). Un composant `NetworkSimulator` est posé sur `/NetworkManager` avec le preset `Assets/_ProjectScenes/NetSim_Test150ms.asset` : **75 ms par sens + 10 ms de gigue**, soit un RTT mesuré d'environ **165 ms**. Le preset s'applique aux deux instances puisqu'elles partagent la scène.

**Diagnostics de session**, tous en `#if UNITY_EDITOR`, jamais embarqués :
- `[DIAG-CLIENT]` (dans `PlayerLocomotion`) — **santé de la prédiction** : taux de resynchronisation, erreur moyenne et max, RTT, profondeur de la file serveur. Le chiffre qui compte est le **taux de resync** : une prédiction saine est à 0 %.
- `[DIAG-SERVEUR]` (dans `WeaponController`) — tirs acceptés, touchés, **rejetés par motif**, rewind réellement appliqué, plus la mesure différentielle décrite plus bas.
- `PlayerInputReader.AutopilotStrafe` — fait faire au joueur un va-et-vient latéral déterministe à la place du clavier. Indispensable pour tester le rewind : une seule personne ne peut pas à la fois se déplacer sur une instance et viser sur l'autre.

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

**Tests EditMode** sous `Assets/Tests/EditMode` (`DuelArena.Tests.EditMode`). Lancer : `unity command run_tests --project-path "..." --mode EditMode`. 23 tests au 2026-09-28, tous verts.

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

`Assets/Tests/PlayMode` (`DuelArena.Tests.PlayMode`), 3 tests. Lancer : `unity command run_tests --project-path "..." --mode PlayMode --async_tests`, puis sonder `test_status`. **Le mode synchrone ne marche pas** : entrer en Play Mode déclenche un rechargement de domaine qui coupe la requête HTTP.

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

`SwattSolider_T_Pose` (Mixamo) est attaché au prefab joueur sous un enfant **`Model`**, à (0,0,0) et **sans mise à l'échelle**. 30 animations dans `Assets/_ProjectArt/Animations`, toutes en Humanoid retargetées sur l'avatar du personnage.

**L'origine du Player est aux PIEDS** — `CameraPivot` est à 1,65, qui est la hauteur caméra debout mesurée depuis les pieds. Un Mixamo ayant lui aussi sa racine aux pieds, il s'attache sans décalage.

⚠️ **`SkinnedMeshRenderer.bounds` n'est PAS la taille du personnage.** C'est une boîte englobante *statique*, gonflée pour couvrir n'importe quelle pose d'animation : elle annonçait 1,92 m. La vraie mesure passe par `BakeMesh`, qui tient compte de la pose courante — **1,797 m en T-pose, 1,764 m en pose d'attente**, contre 1,80 m pour la capsule debout. L'écart est d'un demi-centimètre, donc aucune mise à l'échelle n'est nécessaire. Mesurer en T-pose reste d'ailleurs trompeur : on est naturellement plus petit en garde.

### Conformité d'import — trois propriétés non négociables

Tout FBX d'animation ajouté doit avoir : **rig Humanoid** en `Copy From Other Avatar` pointant l'avatar du personnage, **`lockRootPositionXZ`** (Bake Into Pose), et un **bouclage correct**.

- Sans Humanoid, Mecanim ne peut pas retargeter — or les animations viennent d'un AUTRE personnage (`Ch35_nonPBR`) que le modèle. Elles ne joueraient tout simplement pas.
- `lockRootPositionXZ` neutralise la root motion. **La position horizontale appartient exclusivement à `Move()`** ; une animation qui déplace aussi le personnage entrerait en conflit avec la simulation, et la vitesse d'un Animator n'est pas déterministe entre machines.
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

### La cadence de lecture

Un arbre de mélange **ne modifie pas la cadence de ses clips**. À mi-vitesse il mélange l'attente et la course, mais la course joue à 100 % de sa cadence pendant que le corps n'avance qu'à moitié — les jambes s'agitent sans que le personnage suive. `SpeedMult` accorde donc la vitesse de lecture au déplacement réel.

🚨 **Plafonné à 1, et ce n'était pas évident.** Au-delà de l'allure nominale ce n'est pas la cadence qui doit monter mais le **clip** qui change : à `MoveY = 2` l'arbre joue `Sprint Forward`, déjà authorée pour sprinter. Une première version plafonnait à 1,5 et accélérait donc de 50 % une animation qui n'en avait aucun besoin.

Le plancher ne descend pas à zéro : le multiplicateur pilote l'état entier, animation d'attente comprise, qui doit continuer de respirer à l'arrêt.

**`standingPlaybackScale` / `crouchingPlaybackScale` / `pronePlaybackScale`** accordent chaque posture à ses clips, Mixamo n'authorant pas ses animations à l'échelle des vitesses de ce jeu. C'est un réglage de **ressenti**, à ajuster en jouant.

### Chute et franchissement chez le spectateur (2026-09-30)

`Airborne` et `Vaulting` sont désormais alimentés. Ils ne pouvaient pas l'être depuis `IsVaulting` ou `controller.isGrounded` : un **spectateur** n'appelle jamais `Move()` et son `CharacterController` n'est pas simulé, donc son `isGrounded` ne veut rien dire. Sans signal réseauté, un adversaire qui tombe ou franchit un obstacle garderait son animation de course.

Deux `NetworkVariable<bool>` en écriture serveur, `networkGrounded` et `networkVaulting`, publiées par `ServerPublishDisplayState()` **à chaque frame serveur** — hors du bloc « si des inputs ont été traités », exactement comme l'historique de pose. Sinon un drapeau resterait figé pendant une micro-coupure, et l'adversaire garderait son animation de chute après avoir atterri. Une `NetworkVariable` n'émet que sur changement, donc écrire chaque frame ne coûte rien.

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

### ⚠️ DETTE OUVERTE — l'arc de vault ne franchit plus l'obstacle (2026-09-30)

Constaté en jeu juste après la sonde de sol : le personnage **traverse la barricade** au lieu de passer par-dessus.

`AdvanceVault` calcule `Lerp(vaultStart, vaultEnd, t) + up * courbe(t) * vaultArcHeight`, avec `vaultArcHeight` = **0,35 m**. Avant la sonde de sol, l'arrivée était au SOMMET de l'obstacle : c'est l'interpolation elle-même qui faisait monter le joueur. Maintenant que l'arrivée est au sol derrière, l'interpolation reste au sol et l'arc culmine à 35 cm — très en dessous d'une barricade de 1,25 m.

**Les deux correctifs se sont annulés** : « perché dessus » a été échangé contre « à travers ». Ce n'est pas que visuel — le GDD fait de la lisibilité un pilier, et un corps qui traverse un obstacle ne raconte pas un franchissement à l'adversaire.

**Le correctif propre** : l'arc doit passer au-dessus du sommet de l'obstacle, donc `AdvanceVault` doit CONNAÎTRE cette hauteur. C'est une **cinquième valeur cumulative** lue par `Move()`, et la règle du projet s'applique intégralement — elle doit rejoindre `SimulationState`, la RPC de correction et `RestoreVaultState`. Les tests de déterminisme échoueront d'eux-mêmes si la restauration est incomplète, ce qui est précisément leur raison d'être.

Reporté à la demande de l'utilisateur le 2026-09-30, en connaissance de cause.

### Ce qui n'est PAS encore branché

- **`Airborne` et `Vaulting`** : les paramètres existent, l'Animator a les états, mais rien ne les alimente. `IsVaulting` et `controller.isGrounded` ne sont vrais que là où la simulation tourne — un spectateur ne les voit pas. Il leur faut un signal réseauté, ce qui est l'incrément suivant.
- **La couche haute du corps** (visée + tir, via masque d'avatar). C'est elle qui permettra de tirer en strafant, les jambes jouant le déplacement pendant que le torse joue le tir — et donc d'utiliser les animations génériques `Run Left/Right/Backward`, qui ne tiennent pas d'arme.
- Note de cadence : le MP5 tire toutes les **143 ms** (7 coups/s) alors que les animations de tir durent 270 ms (debout), 430 ms (couché) et **1030 ms (accroupi)**. Elles sont authorées pour un tir visé isolé. Celle d'accroupi ne passera probablement pas telle quelle.

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

*(Livré le 2026-09-28 : le vault réseauté. **Toutes les mécaniques du GDD sont désormais en place.**)*

1. ~~Poser des obstacles franchissables~~ — fait le 2026-09-29 (`Barricade 0.5` / `1.25` / `1.6`).
2. ~~Migrer le multijoueur vers `Arena.unity`~~ — **abandonné le 2026-09-29**, sans objet : l'arène a été reportée dans `MultiTestScene`.
3. **Un vrai personnage (humanoïde placeholder), puis les hitbox par zone, puis les multiplicateurs de dégâts.** Voir l'arbitrage ci-dessous.
4. **Lobby / Relay** (Unity Services), puis serveur dédié — le mode host-joueur donne un avantage de latence à l'hôte, inacceptable en 1v1 compétitif (règle du GDD). Volontairement APRÈS le personnage.

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
5. **Sous latence (100-150 ms)** — le lean d'un adversaire est en retard d'un RTT chez le spectateur. Attendu tant que le rewind n'est pas là ; à mesurer pour savoir si c'est gênant en duel.
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
