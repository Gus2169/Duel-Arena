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

- **Piloter l'Editor : le CLI `unity`.** L'Editor tourne en général déjà avec ce projet ouvert :
  `unity command <nom> --project-path "D:\Documents\Unity\FPS Claude"` — `unity list` liste les commandes (scène, GameObjects, prefabs, build, tests, console...). Préférer ça à l'édition manuelle de `.unity`/`.prefab` (YAML) quand l'Editor est ouvert. Cycle de vérification standard après une modif de script : `recompile`, puis `recompile_status` en boucle, puis `console_status`.
  ⚠️ Le buffer de `console` garde les entrées des compilations précédentes. Se fier à `groundTruth` / `recompile_status`, pas au comptage brut.
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

Le lean est **hybride** : effet caméra en C, son en B (les adversaires doivent l'entendre). Le yaw est en A et non en C : il affecte la direction de déplacement ET de tir, donc c'est du gameplay, pas de la caméra.

### Hit registration

Le tireur raycast en local pour son feedback visuel instantané (**aucun dégât associé**), envoie `origin`/`direction` au serveur qui refait SEUL le raycast et décide SEUL des dégâts. `Health` est un `NetworkBehaviour` dont `Current` est une `NetworkVariable<float>` en écriture serveur uniquement (avec un mode de secours non-réseauté pour une cible de test isolée) ; `ApplyDamage` refuse tout appel client direct sur un objet réseauté.

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

- **Valeur RELATIVE/cumulative dans `Move()`** → doit avoir un équivalent « valeur confirmée » renvoyé par le serveur et appliqué AVANT le rejeu des inputs non confirmés, sinon dérive à chaque correction (bug du yaw en double). Vaut pour toute future extension de `Move()`.
- **Tout script qui lit clavier/souris doit avoir une garde `IsOwner`** en tête d'`Update()`/`LateUpdate()` — sinon chaque instance locale réagit à la souris physique (caméras qui bougent sur 2 écrans, `AudioListener` en surnombre, tirs pour tout le monde). `PlayerInputReader` ne connaît volontairement pas la notion de propriétaire : c'est aux consommateurs de se garder.
- **Warning « N audio listeners in the scene »** : la garde dans `OnNetworkSpawn` ne voit que les enfants du Player. Un `AudioListener` orphelin ailleurs dans la scène (typiquement une caméra de secours affichée avant le spawn) ne sera jamais désactivé par ce code — chercher là en premier.
- **Un champ `[SerializeField]` assigné dans l'inspecteur PENDANT le Play Mode n'est jamais sauvegardé.** Symptôme typique : un fix qui marche dans un contexte mais pas l'autre alors que le code est identique. Toujours assigner en mode Édition puis sauvegarder.
- **Ressenti « saccadé » en build** : vérifier d'abord le Packet Delay Ms du Debug Simulator avant de soupçonner le réseau ou le code. Contrainte réelle séparée : 2 instances complètes sur une machine coûtent cher en perf.
- **`obstacleMask`** (utilisé par `CheckCapsule`/`CheckSphere`, pas seulement des raycasts) doit exclure le layer du joueur : `obstacleMask &= ~(1 << gameObject.layer)` dans `Awake`.
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

`TestSpin.cs` reste utilisé par un objet de test (`CubeTest`) dans la scène.

## Dettes techniques (audit du 2026-09-21, vérifié dans le code)

1. ✅ **Corrigé** — speedhack par flood d'inputs (borne par frame → budget sur le temps réel).
2. ✅ **Corrigé** — `origin` de tir non validée dans `FireServerRpc`.
3. **Réconciliation sans seuil d'erreur.** `ApplyBufferedServerInputs` envoie une correction à *chaque* frame serveur où des inputs ont été traités. Côté client, chaque correction fait un `controller.enabled = false/true`, un repositionnement, puis rejoue **tous** les inputs non confirmés — à 100 ms de RTT, ~600 `CharacterController.Move()` par seconde et par joueur juste pour réconcilier. Le toggle de `controller.enabled` remet en plus `isGrounded` à `false` à chaque correction, ce qui fait clignoter la détection de sol. **Fix : ne réconcilier que si `Vector3.Distance(predicted, confirmed) > ~0.05f`.**
4. **Deux états échappent à la réconciliation — même famille que le bug de yaw.**
   - `currentVelocity` (cumulative via `MoveTowards`) n'est jamais recalée sur une valeur serveur avant le rejeu. Elle reconverge seule, mais garantit un désaccord permanent pendant chaque phase d'accel/décel.
   - **La hauteur/le rayon du `CharacterController`** : `UpdateStanceTransition()` lerp avec `Time.deltaTime` indépendamment sur chaque instance. Pendant une transition de posture, client et serveur n'ont pas la même capsule → pas la même collision → `Move()` n'est plus déterministe, ce qui viole le contrat écrit en en-tête de la fonction. **À corriger avant le hitbox rewindable**, qui sinon se construit sur une base non déterministe.
5. **Bug de spawn latent.** `PlayerLocomotion.OnNetworkSpawn` fait `transform.position = networkPosition.Value`, mais au spawn la `NetworkVariable` vaut encore `default` = **(0,0,0)**. Le commentaire dit « évite un téléport visuel » ; en pratique ça *provoque* un téléport à l'origine du monde, serveur compris. Invisible aujourd'hui (pas de points de spawn) — ça cassera dès la boucle BO5 avec des spawns opposés. Fix : ne repositionner que si `!IsServer`, et initialiser `networkPosition.Value = transform.position` côté serveur.

6. **GPU Resident Drawer incompatible avec la géométrie ProBuilder.** `PC_RPAsset.asset` a `m_GPUResidentDrawerMode: 1`. Résultat : à chaque ouverture de scène, la Console se remplit d'erreurs `BatchDrawCommand was submitted with an invalid Batch, Mesh, or Material ID` (`MaterialID: ProBuilderDefault`, `MeshID: <null>`, passe SHADOWCASTER) — **139 dans `Arena.unity`, 155 dans `MultiTestScene.unity`**. Ce n'est pas un bug de gameplay et ça ne casse rien de visible, mais ça **noie la Console**, donc ça masque les vraies erreurs. Deux options : passer `m_GPUResidentDrawerMode` à 0 (le plus simple, le gain du GPU Resident Drawer est nul à cette échelle), ou attendre de remplacer le blocking ProBuilder par les assets finaux. **Vérifié le 2026-09-22 : ces erreurs sont pré-existantes et indépendantes du code du projet.**

**Hygiène** : aucun `.asmdef` → tout dans `Assembly-CSharp`, recompilation complète à chaque modif. **Aucun test**, alors que `Move()` est une fonction pure paramétrée par un snapshot : un test EditMode « même snapshot × 2 → même position » attraperait les régressions de déterminisme de la dette n°4 automatiquement.

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

1. **Seuil de réconciliation + `currentVelocity` confirmée** (dettes 3 et 4) — améliore le feel *et* la perf.
2. **Déterminisme de la hauteur de capsule** (dette 4) — **avant** le hitbox.
3. **Hitbox de tir séparé du `CharacterController` de mouvement.** Aujourd'hui le raycast serveur touche le `CharacterController`, qui suit la posture mais **jamais le lean** (le lean ne déplace que la caméra/`leanPivot`) : un joueur qui penche pour peek est partiellement intouchable sur son flanc exposé. Le hitbox doit suivre posture ET lean, être synchronisé et **rewindable**. Le `CharacterController`, lui, ne doit jamais bouger avec le lean (ça casserait la collision monde).
4. **Rewind / compensation de latence** — à traiter avec le point 3, même cause racine. **Pas implémenté**, TODO détaillé dans `WeaponController.FireServerRpc`. Approche retenue : mesurer le RTT en piggybackant sur le round-trip prédiction/réconciliation existant (plutôt qu'une RPC de ping dédiée ou l'horloge de Netcode), historique de position glissant côté serveur, délai de rewind suggéré par le client mais **clampé serveur**, restauration en `try/finally`.
5. **Boucle de round / conditions de victoire (BO5)** — rien ne termine la partie aujourd'hui, seule la vie baisse. C'est ce qui fera remonter la dette n°5 (spawns).
6. **Vault réseauté** — voir ci-dessous.
7. **Migrer le multijoueur vers `Arena.unity`.**
8. **Lobby / Relay** (Unity Services), puis serveur dédié — le mode host-joueur donne un avantage de latence à l'hôte, inacceptable en 1v1 compétitif (règle du GDD).

## Vault : désactivé, pas cassé

`HandleVaultInput()` et `ProcessVault()` existent toujours dans `PlayerLocomotion.cs` mais **ne sont appelés depuis nulle part** — débranchés d'`Update()` pendant la fusion réseau. `IsVaulting` reste donc toujours `false`, et comme le vault était la seule chose branchée sur l'action Jump, **la touche de saut ne fait plus rien** : `JumpPressedThisFrame` est positionné puis consommé sans que personne ne le lise.

Ce n'est pas une régression. La raison : le vault est un mouvement scripté à durée fixe qui fait `controller.enabled = false` pendant un lerp de position — un état « hors contrôle » incompatible avec le `Move()` par frame que la prédiction/réconciliation rejoue. Le réactiver demande un vrai incrément réseau (vault prédit côté propriétaire + confirmé serveur), pas juste de rappeler la fonction.

**À ne pas oublier** : l'utilisateur y tient, c'est une mécanique du GDD.
