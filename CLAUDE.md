# FPS Claude — Duel Arena

FPS multijoueur en Unity 6 (6000.6.0f1), URP. Jeu de **duel 1v1** nerveux, lisible, compétitif, avec des spectateurs qui peuvent influencer légèrement le match. Transition solo → multijoueur en cours (Netcode for GameObjects 2.13.2).

## Documents de référence (`Docs/`)

Ce sont les documents de cadrage du projet. **Les consulter avant toute décision de design ou de priorisation**, et les tenir à jour quand une décision importante est prise ou qu'un incrément est livré.

- `Docs/GDD-Duel-Arena.md` — Game Design Document à jour (vision, gameplay core, TTK, mouvement, son, arène, spectateurs, état d'avancement, prochaines étapes design).
- `Docs/feuille-de-route-technique.md` — feuille de route technique : décisions prises, pile technique, phases 0→8, risques prioritaires, notes de conception et pièges Unity déjà rencontrés. **Le plus détaillé des trois, c'est la source principale.**
- `Docs/Projet FPS Unity.pdf` — GDD initial d'origine. **Historique uniquement** : remplacé par `GDD-Duel-Arena.md`, ne pas s'y référer pour l'état actuel.

## Principes de design non négociables

- **Le skill prime sur tout.** Tous les choix techniques en découlent.
- **Pas de RNG dans la précision** → le recul suit une `AnimationCurve` déterministe (montée rapide, plafonnement, pattern horizontal en "S"), jamais de dispersion aléatoire. Deux joueurs qui tirent la même rafale dans les mêmes conditions subissent exactement le même recul.
- **TTK cible ≈ 0.7 s** de tir soutenu (6 impacts à 17 dégâts, 7 tirs/s ≈ 420 cpm). Valeur de départ validée, ajustable dans l'inspecteur (`WeaponData` est un ScriptableObject, pas de constante en dur).
- **Pas d'avantage de latence structurel** : le mode hôte-joueur est un compromis de développement, la cible en production est un serveur dédié.
- **Le son est une information de gameplay**, pas un habillage : pas, ramper, changements de posture, lean et tirs doivent être audibles en 3D spatial par un adversaire proche. Le sneak reste volontairement silencieux (contrepartie de sa lenteur).
- **Trois paliers de vitesse** : sneak (lent, silencieux) < marche < course (rapide, bruyant) — vrai choix tactique, pas un simple confort.
- **Pas de mouvement à momentum** (bhop, slide, air-strafe) tant que les fondations réseau de la Phase 1 ne sont pas bouclées.
- Ton : sérieux dans le gameplay, avec une touche de bizarrerie assumée (7/10) dans le décor, les réactions du public et certaines animations.
- Rounds de 15–30 s, respawn instantané dans le lobby, format 1v1 uniquement.

## Outils

- **Piloter l'Editor : le CLI `unity`, pas MCP.** L'Editor tourne en général déjà avec ce projet ouvert (port 7800) :
  `unity command <nom> --project-path "D:\Documents\Unity\FPS Claude"` — `unity list` liste toutes les commandes disponibles (scène, GameObjects, prefabs, build, tests, console...). Préférer ça à l'édition manuelle de fichiers `.unity`/`.prefab` (YAML) quand l'Editor est ouvert : plus sûr, validé en direct par l'Editor plutôt que par du texte à l'aveugle.
- **Git** : dépôt local initialisé (pas de remote). Committer seulement sur demande explicite.

## Architecture réseau (Netcode for GameObjects)

Topologie **client-serveur autoritaire** (pas la "distributed authority" d'Unity 6 : on veut une source de vérité unique pour les dégâts).

Le mouvement, la posture et le tir suivent tous le même pattern à 4 cas selon `IsOwner`/`IsServer`, appliqué de façon cohérente dans `PlayerLocomotion.cs` et `WeaponController.cs` :

1. `IsOwner && IsServer` (Host sur son propre perso) → autorité directe, pas de prédiction (aucune latence avec soi-même).
2. `IsOwner && !IsServer` (client distant sur son propre perso) → prédiction locale + `ServerRpc` + réconciliation par rejeu. `Move()` doit rester STRICTEMENT déterministe entre prédiction, traitement serveur et rejeu de réconciliation — toute divergence casse la réconciliation.
3. `!IsOwner && IsServer` (serveur sur le perso d'un autre joueur) → seule source de vérité, applique les inputs bufferisés (`serverInputQueue`).
4. `!IsOwner && !IsServer` (spectateur) → interpolation par historique de positions (`remoteSnapshots`), jamais de logique de mouvement.

Position, yaw ET posture sont gérés à la main via `NetworkVariable` plutôt que `NetworkTransform`, précisément pour permettre cette prédiction/interpolation. La posture est **confirm-only, pas prédite** (compromis assumé, à revoir si perçu comme mou).

Répartition du code joueur en 3 catégories (à respecter pour toute nouvelle mécanique) :
- **(A) État simulé/autoritaire, réseauté via le pattern à 4 cas** : position, yaw, vitesses, gravité, sol, posture.
- **(B) Événements sonores de gameplay** (`OnPlayerSound`), diffusés à tous par ClientRpc — décidés côté serveur (pas, ramper, posture, relevé auto) ou demandés par le client avec garde-fou (lean start/end uniquement, la RPC rejette tout autre type).
- **(C) Cosmétique pur, ne tourne que si `IsOwner`** : head bob, kick caméra à l'atterrissage, offset visuel de caméra du lean.

Le lean est **hybride** : effet caméra en C (local), son en B (les adversaires doivent l'entendre). Le yaw est en A et non en C : il affecte la direction de déplacement ET de tir, donc c'est du gameplay, pas de la caméra.

Hit registration : le tireur raycast en local pour son feedback visuel instantané (aucun dégât associé), envoie origin/direction au serveur qui refait SEUL le raycast et décide SEUL des dégâts. `Health` est un `NetworkBehaviour` dont `Current` est une `NetworkVariable<float>` en écriture serveur uniquement (avec un mode de secours non-réseauté pour une cible de test isolée) ; `ApplyDamage` refuse tout appel client direct sur un objet réseauté.

Garde-fous serveur en place :
- `WeaponController.FireServerRpc` rejette tout tir plus rapide que `data.shotsPerSecond` (tolérance 15%), une direction nulle, et **une origine trop éloignée de la position serveur du tireur** (`maxOriginDistanceFromPlayer`, 4 m par défaut — couvre hauteur caméra + lean + avance de prédiction sous latence ; à resserrer quand le rewind sera en place).
- `PlayerLocomotion.ApplyBufferedServerInputs` clampe le `deltaTime` de chaque input reçu et **budgète le temps simulé sur le temps RÉEL** (token bucket rechargé de `Time.deltaTime × 1.1`, réserve plafonnée à 0,25 s) au lieu de borner par frame. `SubmitInputServerRpc` plafonne aussi la taille de `serverInputQueue` (`MaxQueuedInputs`).

**Règle à retenir** : toute borne anti-abus doit être exprimée **par unité de temps réel**, jamais par frame — une borne par frame se contourne en gardant la queue pleine, puisque le serveur tourne à ~60 frames/s.

**Tester ces garde-fous** : deux interrupteurs de triche simulée existent dans l'inspecteur, sous un header `Debug — triche simulée`, tous deux entourés de `#if UNITY_EDITOR` (ils ne peuvent donc pas se retrouver dans une build, même cochés par mégarde).
- `WeaponController.debugFakeShotOrigin` — décale l'origine envoyée au serveur de 50 m. Attendu : aucun dégât + un warning `[Serveur] Tir rejeté` par tir. Le feedback visuel local reste honnête, donc on voit bien le décalage entre ce que le tireur voit et ce que le serveur accepte.
- `PlayerLocomotion.debugFloodServerInputs` — renvoie le même input 10× par frame avec des séquences distinctes. Attendu : le joueur n'avance PAS plus vite, il se fait ramener en arrière en permanence.

**Les deux ne fonctionnent que depuis une instance CLIENT distante**, jamais le Host : en Host, le perso local passe par le cas 1 (autorité directe) et ne traverse ni la queue d'inputs ni la RPC de tir.

Règle anti-triche à reproduire pour toute nouvelle RPC client→serveur : aucune RPC n'accepte de **résultat** (hit, dégâts, position). Elle accepte au mieux une *suggestion* (ex. un délai de rewind), toujours clampée côté serveur.

## Pièges déjà rencontrés (ne pas les re-découvrir)

- **Rotation/position appliquée de façon RELATIVE/cumulative dans `Move()`** → doit avoir un équivalent "valeur confirmée" renvoyé par le serveur et appliqué AVANT le rejeu des inputs non confirmés, sinon dérive à chaque correction (bug du yaw en double, corrigé). À retenir pour toute future extension de `Move()` (vault, nouvelles mécaniques de mouvement).
- **Tout script qui lit clavier/souris doit avoir une garde `IsOwner`** en tête d'`Update()`/`LateUpdate()` — sinon chaque instance locale réagit à la souris physique (caméras qui bougent sur 2 écrans, `AudioListener` en surnombre, tirs pour tout le monde).
- **Un champ `[SerializeField]` assigné dans l'inspecteur PENDANT le Play Mode n'est jamais sauvegardé** — Unity annule ce changement à l'arrêt. Symptôme typique : un fix qui marche dans un contexte/machine mais pas l'autre alors que le code est identique. Toujours assigner en mode Édition puis sauvegarder la scène.
- **Ressenti "saccadé" en build** : vérifier d'abord le Packet Delay Ms du Debug Simulator (Multiplayer Play Mode) avant de soupçonner le réseau ou le code. Contrainte réelle séparée : 2 instances complètes sur une seule machine coûtent cher en perf.
- **`obstacleMask`** (utilisé par `CheckCapsule`/`CheckSphere`, pas seulement des raycasts) doit exclure le layer du joueur lui-même : `obstacleMask &= ~(1 << gameObject.layer)` dans `Awake`. À reproduire pour tout futur système réutilisant ce mask.
- **Ordre recul/raycast dans `Fire()`** : le recul s'applique APRÈS avoir déterminé où le tir atterrit, sinon chaque tir est décalé par son propre recul.
- **Near Clip Plane** gardé petit (~0.03-0.05), sinon un mur très proche disparaît entièrement du rendu.
- **Enum sérialisé** : Unity sérialise un enum par sa valeur entière, pas par son nom — préserver l'ordre existant en ajoutant des valeurs (cf. `PlayerSoundEvent`).

## Prefabs joueur

Un seul prefab joueur : `Assets/_ProjectArena/PlayerPrefab/Player.prefab` — `NetworkObject` + `PlayerLocomotion` + `WeaponController` + `PlayerInputReader` + `PlayerCameraLook` + `CrosshairUI` + `WeaponVisualFeedback` + `PlayerSoundEmitter`. Référencé par GUID dans `NetworkManager.PlayerPrefab` et `Assets/DefaultNetworkPrefabs.asset`.

Historique (2026-09) : il y avait 3 prefabs quasi-doublons (`Player`, `PlayerMulti`, `Player Test`) issus du prototypage incrémental de la couche réseau — nettoyés. `Player.prefab` actuel est en fait l'ancien `Player Test.prefab` renommé (GUID conservé).

`Assets/_ProjectScripts/Multi/NetworkPlayerMovement.cs` et `TestSpin.cs` : le pattern de `NetworkPlayerMovement` a été fusionné dans `PlayerLocomotion.cs`, le fichier lui-même n'a pas été supprimé (gardé en référence, plus utilisé par aucun prefab). `TestSpin` reste utilisé par un objet de test (`CubeTest`) dans la scène.

## Scènes

- `Assets/_ProjectScenes/MultiTestScene.unity` — seule scène avec un `NetworkManager` configuré (UnityTransport, 127.0.0.1:7777, local uniquement, pas de Relay/lobby). Scène de test multijoueur actuelle.
- `Assets/_ProjectScenes/Arena.unity` — scène de jeu d'origine, **pas encore migrée au multijoueur** (pas de `NetworkManager`).

## Feuille de route (résumé — détail dans `Docs/feuille-de-route-technique.md`)

L'ordre est volontairement contraignant : le réseau n'arrive qu'après un gunplay solo satisfaisant.

0. **Vertical slice solo** — *close.* Contrôleur complet, arme hitscan, blocking d'arène validé sur les 3 distances, audio de contexte 3D, feedback visuel de tir (placeholder procédural : réticule OnGUI, tracer, marqueur d'impact).
1. **Netcode & duel fonctionnel** — *en cours.* Mouvement/posture/son/tir réseautés validés à 2 joueurs réels (Host + Client). Hit registration serveur-autoritaire livré.
2. Arène & level design compétitif (layers "bloque la vue" vs "bloque le tir", passage à l'asset final une fois le layout figé).
3. Armes & identité insolite (roster étendu, vrai viewmodel 3D + animations remplaçant le placeholder, munitions/rechargement — hooks son `ReloadStart`/`ReloadEnd` déjà réservés).
4. Pouvoirs / power-ups (commencer par des power-ups d'arène plutôt que des personnages dédiés).
5. Système spectateurs (actions validées ET rate-limitées côté serveur, activables par lobby).
6. Modes alternatifs — arène dans le noir (canal "sons forts" séparé : **c'est là que le son de tir devra être rebranché**), Hack & Defend.
7. UX/UI & polish — flow lobby → file d'attente → duel → résultats, vrai HUD UI Toolkit (remplace le réticule OnGUI).
8. Playtests, équilibrage, durcissement (continu).

## Dettes techniques identifiées (audit de code du 2026-09-21)

Constats issus d'une lecture des scripts eux-mêmes, pas des docs. Classés par gravité.

1. ~~**Speedhack par flood d'inputs.**~~ **✅ Corrigé le 2026-09-21.** `ApplyBufferedServerInputs` bornait à 0,5 s de `deltaTime` **par frame serveur**, soit ~30 s de mouvement simulé par seconde réelle à 60 FPS (speedhack ×30 en gardant la queue pleine). Remplacé par un budget rechargé sur le temps réel + un plafond de queue dans `SubmitInputServerRpc`.
2. ~~**`origin` jamais validé dans `WeaponController.FireServerRpc`.**~~ **✅ Corrigé le 2026-09-21.** Un client modifié pouvait raycaster depuis n'importe quel point de la carte tout en respectant la cadence. Rejeté désormais au-delà de `maxOriginDistanceFromPlayer`.
3. **Réconciliation sans seuil d'erreur.** `ApplyBufferedServerInputs` envoie une correction à *chaque* frame serveur où des inputs ont été traités. Côté client, chaque correction fait un `controller.enabled = false/true`, un repositionnement, puis rejoue **tous** les inputs non confirmés — à 100 ms de RTT, ~600 `CharacterController.Move()` (sweeps physiques) par seconde et par joueur juste pour réconcilier. Le toggle de `controller.enabled` remet en plus `isGrounded` à `false` à chaque correction, ce qui fait clignoter la détection de sol. **Fix : ne réconcilier que si `Vector3.Distance(predicted, confirmed) > ~0.05f`.**
4. **Deux états échappent encore à la réconciliation — même famille que le bug de yaw déjà corrigé.**
   - `currentVelocity` (cumulative via `MoveTowards`) n'est jamais recalée sur une valeur confirmée par le serveur avant le rejeu. Elle reconverge seule (bornée par `targetSpeed`) donc ça ne diverge pas sans fin, mais ça garantit un désaccord permanent pendant chaque phase d'accel/décel.
   - **La hauteur/le rayon du `CharacterController`** : `UpdateStanceTransition()` lerp avec `Time.deltaTime` indépendamment sur chaque instance. Pendant une transition de posture, client et serveur n'ont donc pas la même capsule → pas la même collision → `Move()` n'est plus déterministe, ce qui viole le contrat écrit en en-tête de la fonction. **À corriger avant le hitbox rewindable**, qui sinon se construit sur une base non déterministe.
5. **Bug de spawn latent.** `PlayerLocomotion.OnNetworkSpawn` fait `transform.position = networkPosition.Value`, mais au spawn la `NetworkVariable` vaut encore `default` = **(0,0,0)**. Le commentaire dit "évite un téléport visuel au spawn" ; en pratique ça *provoque* un téléport à l'origine du monde, serveur compris. Invisible aujourd'hui (scène de test près de l'origine, pas de points de spawn) — ça cassera dès la boucle BO5 avec des spawns opposés. Fix : ne repositionner que si `!IsServer`, et initialiser `networkPosition.Value = transform.position` côté serveur avant tout.

**Écarts doc ↔ code à ne pas oublier** (la feuille de route décrit comme livrés des correctifs absents du dépôt) : le rewind (voir priorités), le champ `bootstrapCamera` de `NetworkBootstrapUI` (n'existe pas dans le fichier), et le diagnostic `LogSceneAudioListeners()` marqué "TEMPORAIRE — à retirer" qui tourne toujours à chaque spawn (`FindObjectsByType` + `StringBuilder`). Ces deux derniers vont ensemble : le bug des `AudioListener` est documenté comme résolu alors que le correctif n'est pas dans le code.

**Hygiène de projet :** pas de `.gitattributes`, donc **ni Git LFS ni Smart Merge** alors que la feuille de route les liste comme acquis (à faire maintenant : rétroactivement c'est une réécriture d'historique). Aucun `.asmdef` → tout dans `Assembly-CSharp`, recompilation complète à chaque modif. **Aucun test**, alors que `Move()` est une fonction pure paramétrée par un snapshot : c'est exactement le genre de code qui se teste en EditMode ("même snapshot × 2 → même position" attraperait les régressions de déterminisme du point 4 automatiquement).

## Priorités immédiates (Phase 1, dans cet ordre)

*(Les deux premiers points — budget de temps serveur et validation d'`origin` — ont été livrés le 2026-09-21, voir les dettes n°1 et 2. **Pas encore re-testés à 2 joueurs réels** : à valider au prochain test Host+Client, en surveillant la Console pour d'éventuels tirs légitimes rejetés sous forte latence.)*

1. **Seuil de réconciliation + `currentVelocity` confirmée** (dettes n°3 et 4) — améliore le feel *et* la perf, et évite de chasser des micro-désyncs plus tard.
2. **Déterminisme de la hauteur de capsule** (dette n°4) — à faire **avant** le hitbox séparé.
3. **Hitbox de tir séparé du `CharacterController` de mouvement.** Aujourd'hui le raycast serveur touche le `CharacterController`, qui suit la posture mais **jamais le lean** (le lean ne déplace que la caméra/`leanPivot`) : un joueur qui penche pour peek est partiellement intouchable sur son flanc exposé. Le hitbox doit suivre posture ET lean, être synchronisé et **rewindable**. Le `CharacterController` de mouvement, lui, ne doit jamais bouger avec le lean (ça casserait la collision monde).
4. **Rewind / compensation de latence sur le hit registration** — à traiter avec le point 3 (même cause racine). État réel du code : **pas encore implémenté**, TODO explicite dans `WeaponController.FireServerRpc`. ⚠️ `Docs/feuille-de-route-technique.md` le décrit comme "livré" : le doc est en avance sur le dépôt, il décrit l'implémentation visée (RTT mesuré en piggybackant sur le round-trip prédiction/réconciliation existant plutôt qu'avec une RPC de ping dédiée, historique de position glissant de 1,5 s par joueur côté serveur, délai de rewind suggéré par le client mais clampé à 1 s, restauration des positions en `try/finally`). Mettre le doc en cohérence une fois le code livré.
5. **Boucle de round / conditions de victoire (BO5)** — rien ne termine la partie aujourd'hui, seule la vie baisse. C'est aussi ce qui fera remonter la dette n°5 (spawns).
6. **Vault réseauté** — voir ci-dessous.
7. Migrer le multijoueur vers `Arena.unity`.
8. Lobby / Relay (Unity Services) — Phase 7.

## Vault : désactivé, pas cassé

`HandleVaultInput()` et `ProcessVault()` existent toujours dans `PlayerLocomotion.cs` mais **ne sont appelés depuis nulle part** — ils ont été débranchés d'`Update()` pendant la fusion réseau (décision assumée, documentée en en-tête de classe et dans la feuille de route). `IsVaulting` reste donc toujours `false`, et comme le vault était la seule chose branchée sur l'action Jump, **la touche de saut ne fait plus rien du tout** : `PlayerInputReader.JumpPressedThisFrame` est positionné puis consommé par `ConsumeFrameInputs()` sans que personne ne le lise.

Ce n'est donc pas une régression à débugger. La raison du débranchement : le vault est un mouvement scripté à durée fixe qui fait `controller.enabled = false` pendant un lerp de position — un état "hors contrôle" fondamentalement incompatible avec le `Move()` par frame que la prédiction/réconciliation rejoue. Le réactiver demande un vrai incrément réseau (état de vault réseauté, ou vault prédit côté propriétaire + confirmé serveur), pas juste de rappeler la fonction.
