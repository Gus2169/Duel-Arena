# FPS Claude — Duel Arena

FPS de **duel 1v1** en Unity 6 (6000.6.0f1), URP, multijoueur via Netcode for GameObjects 2.13.2.

*Version condensée le 2026-11-04, à la demande de l'utilisateur : cette page dit ce qui est vrai et ce qu'il faut savoir, pas comment on y est arrivé. Les enquêtes, les relevés datés et les dettes closes sont dans l'historique Git (`git show 7fb7bfc:CLAUDE.md` pour la version complète d'avant).*

## Comment ce projet se pilote

**Deux documents de design font autorité, chacun sur son terrain** (tranché le 2026-10-04) :
- **`Docs/GDD-Duel-Arena.md` pour les règles du jeu** : vision, piliers, règles non négociables, ce que le jeu refuse d'être. À consulter avant toute décision de ressenti, d'équilibrage ou de contenu. En cas de conflit sur une règle de jeu, **le GDD gagne**.
- **`Docs/Bible-Lore-DA.md` pour le monde et l'apparence** : lore, personnages, armes, arènes, ton, direction artistique.

Les deux se mettent à jour quand une décision de design est prise.

**Pour tout le reste (architecture, technique, priorités, dettes), cette page est la seule source de vérité**, et Claude en est le juge :
- Claude tranche l'architecture et le séquencement technique sans demander d'arbitrage sur ce qui n'engage pas le design.
- Claude tient cette page à jour dans la même session : incrément livré, dette découverte, décision d'archi.
- Ce qui est écrit ici a été **vérifié dans le code**. En cas de doute, le code gagne et la page se corrige. Une doc technique non vérifiée devient un piège (vécu : un ancien document décrivait comme livrés des correctifs absents).
- Remontent à l'utilisateur : les arbitrages de **ressenti** et de **contenu**, et les **actions destructives**.
- **Cette page reste une référence, pas un journal** (demandé le 2026-10-12) : on y écrit l'état, la règle, la valeur et le piège, en une ou deux phrases. Le récit d'une enquête et les relevés datés vont dans le message de commit ; une dette close sort de la liste.

## Règles de travail

- Tout en **français** : échanges, docs, commentaires du code.
- **Commit et push seulement sur demande explicite.** Le push peut être refusé par le mode automatique de Claude Code : le dire à l'utilisateur, qui le lance lui-même.
- **Rien n'est supprimé (clips, assets) sans l'accord de l'utilisateur.** Une suppression passe par `AssetDatabase.MoveAssetsToTrash` (Corbeille de Windows, récupérable).
- **Les références sont *Rainbow Six* et *Call of Duty*** ; demander en cas de doute.
- **Tout nouveau test non trivial est validé par mutation** : on le voit échouer avec le défaut, puis passer sans.
- **Mesures à deux instances AVANT les tests PlayMode** (ces derniers bloquent le joueur virtuel), et **banc de test éteint après** (`SetCloneTestBench(false, false, false)`).
- **Build de contrôle après tout ajout de code de diagnostic** : une garde `#if UNITY_EDITOR` mal placée casse la build au lieu de passer inaperçue. Dernière build vérifiée : 2026-11-04 (version 0.1.7, en Mono, D3D11 seul, `Desktop/Build/FPS V10 optimisation`, 232 Mo ; une demi-heure quand les variantes du décor sont à compiler, 8 min quand elles sont en cache ; quatre avertissements connus : la dette n° 63, le pont Pipeline sans configuration, et deux de shaders sans effet). Lancée : 237 images/s au plafond de 240 (2,1 ms de fil principal, 2,3 ms de GPU, pire image 5 ms) ; une partie entre elle et l'Editor, sans erreur. Les builds d'avant ne parlent plus le même protocole. Le code réservé aux builds (`#if !UNITY_EDITOR`, le profil de connexion par instance) ne compile que là. **Contrôle rapide des gardes**, sans la demi-heure des assets : `PlayerBuildInterface.CompilePlayerScripts` pour `StandaloneWindows64` (eval, une minute ; les erreurs vont à la console).
- **Avant un commit, vérifier que les binaires nouveaux sont couverts par LFS** (`git check-attr filter`) : un `.glb` de 22 Mo y avait échappé.

## Outils

- **Piloter l'Editor : les outils MCP `unity-editor-mcp`**, épinglés sur ce projet (`unity mcp --project-path "D:\Documents\Unity\FPS Claude"`, déclaré dans `~/.claude.json`). Équivalence : outil MCP `<nom>` = `unity command <nom> --project-path "..."`. Le CLI `unity` sert au reste (`install`, `editors`, `pipeline`, `skill`, `mcp configure`, `self-update`).
- **Cycle après une modif de script** : `recompile`, attendre la fin du rechargement de domaine (`Domain Reload Profiling` dans `Logs/Editor.log`), puis `console_status` : se fier à `groundTruth`, le buffer de `console` garde les compilations précédentes.
- Préférer les outils de l'Editor à l'édition manuelle de `.unity`/`.prefab` quand il est ouvert.
- ⚠️ **Un `eval` qui tient le fil principal plus de 5 s** (construire le stand) répond « Main thread operation timed out », mais va au bout : vérifier l'état par un second `eval`, sans le relancer.
- **Voir une fenêtre de l'Editor** (les captures MCP ne montrent que les vues Game et Scene) : `InternalEditorUtility.ReadScreenPixel` sur sa `position` × `EditorGUIUtility.pixelsPerPoint`, depuis `EditorApplication.update` après quelques images (eval), la fenêtre visible à l'écran. Un évènement envoyé par `EditorWindow.SendEvent` y arrive décalé de la hauteur de l'onglet.
- ⚠️ **Le joueur virtuel (clone MPPM) est un second Editor**, dont le projet est *dans* le projet principal (`Library/VP/mppmb4fa39f3`, port 7803 ; l'Editor principal est sur le 7804). Pendant un rechargement de domaine de l'Editor principal, un appel MCP peut viser le clone et échouer : refaire l'appel une fois l'Editor principal prêt.
- ⚠️ **Plusieurs sessions Claude peuvent piloter le même Editor** : un Play Mode fait échouer les tests lancés par une autre (« This cannot be used during play mode »), le lanceur de tests coupe le Play Mode en cours, et une recompilation en plein Play Mode recharge les scripts sans quitter la partie. Se prévenir (`ListAgents`, `SendMessage`) avant un Play Mode, des tests ou une recompilation ; `test_status` dit si des tests tournent. Éditeur sans focus : le clavier simulé est ignoré (Input System, « respecter le focus de la vue Game »), et les fichiers modifiés hors de Unity ne sont importés qu'après `AssetDatabase.Refresh()`.
- ⚠️ **Mettre à jour le CLI sur Windows** : `unity self-update` télécharge la nouvelle version mais ne peut pas remplacer `unity.exe` tant que des serveurs `unity mcp` tournent (un par session Claude) ; il répond pourtant « succès ». Méthode : lire `latest-beta.json` sur le CDN, télécharger le binaire, vérifier son SHA-256, renommer le `unity.exe` en cours d'utilisation (Windows l'autorise), poser le nouveau, mettre à jour `%APPDATA%\UnityHub\cli-install.json` (UTF-8 **sans BOM**). Les anciens binaires renommés (`%LOCALAPPDATA%\Unity\bin\*-ancien.exe`, `unity.exe.previous`) se suppriment une fois les serveurs MCP redémarrés, avec l'accord de l'utilisateur.
- Le pilotage de l'Editor passe par le paquet `com.unity.pipeline`, à ne pas retirer. Le paquet AI Assistant est retiré du projet (2026-11-04), avec Timeline et les modules sans usage.
- **Blender 5.2 : la skill `blender`** (`.claude/skills/blender/`). Un pont local vers le Blender ouvert de l'utilisateur (127.0.0.1:9877, jeton ; lancé à chaque ouverture de Blender par `pont_demarrage.py`, installé dans son dossier de démarrage ; sinon, une ligne collée dans sa console Python), ou un Blender en arrière-plan pour itérer et rendre les vues de contrôle. Chaque modèle est un script qui le reconstruit en entier (`Tools/Blender/`, kit `kit_lowpoly.py`) : on corrige le script, pas le `.blend`. **Pas de MCP pour Blender** : l'addon d'ahujasid et l'extension de Blender Lab ne parlent pas le même protocole sur le même port (9876) ; le pont suffit (des outils `mcp__Blender__*` sont apparus depuis dans les sessions : à essayer avant de s'y fier).
- **Le décor des arènes : la skill `arene`** (`.claude/skills/arene/`), l'habillage, la lumière et l'essai des rendus dans Unity ; les props du décor sont dans la skill `blender` (« Le décor des arènes »). **Les ressources trouvées en ligne : la skill `ressources-libres`** (licences compatibles avec un jeu vendu, téléchargement sûr, provenance dans `Docs/Decor-Provenance.md`).

### Git

- Remote `origin` = dépôt GitHub **privé** `https://github.com/Gus2169/Duel-Arena.git`, branche `master`. Authentification par Git Credential Manager (pas de `gh`).
- `.gitattributes` : fins de ligne (`* text=auto`), **Unity Smart Merge** sur le YAML, **LFS** sur les binaires (audio, images, modèles, vidéo, polices, DLL) et sur les meshes générés par script (`*_Fusionne.asset`, `*_Allege.asset`, `*_Nettoye.asset`, `*_Monte.asset`). Les autres `.asset` sont du YAML texte (projet en Force Text).
- Config locale (`.git/config`) : `git lfs install --local`, et `merge.unityyamlmerge.driver` vers `UnityYAMLMerge.exe` de 6000.6.0f1, **à repointer si la version d'Unity change**.
- LFS et `text=auto` ne s'appliquent qu'aux fichiers touchés après coup. Rapatrier d'anciens binaires (`git lfs migrate`) réécrirait l'historique et demanderait un push forcé : seulement avec l'accord de l'utilisateur.

## Architecture réseau

Topologie **client-serveur autoritaire** (pas la « distributed authority » d'Unity 6) : une source de vérité unique pour les dégâts. Position, rotation et posture passent par des variables réseau maison plutôt que `NetworkTransform`, pour permettre prédiction et interpolation. Rien ne suppose que le serveur possède un joueur : passer au serveur dédié retirera le cas 1 sans réécrire les autres.

### Les flux : non fiable pour le continu, fiable pour les événements

- 🚨 **Un flux continu va en non fiable, un événement en fiable.** En fiable ordonné, un seul paquet perdu retient tout ce qui le suit jusqu'à son renvoi.
- **Les inputs** (`SubmitInputsServerRpc`) : en pratique un envoi par image (`InputSendRate`, 250/s au plus) ; chaque envoi répète ceux des `InputRedundantSends` (2) envois précédents non confirmés, 40 inputs au plus (`InputWire`). Le serveur les remet dans l'ordre (`InputReorderBuffer`), les manquants sont attendus 50 ms puis sautés, le recalage rattrape.
- **Les demandes** (tir, rechargement, changement d'arme, arme ramassée, taser) restent fiables et **annoncent leur input de référence** (`LastInputSequence`). Le serveur le borne (`ReferenceInput`) : **jamais avant le dernier input reçu**, jamais à plus de 1 024 devant.
- **Les corrections** (`ReceiveCorrectionRpc`, `[Rpc(SendTo.Owner)]`) : une par tick au plus (`lastCorrectionTick`), dans un `CorrectionPacket` (l'état du vault n'y voyage que pendant un vault) ; plus ancienne que la dernière appliquée, elle est ignorée ; elle confirme aussi les inputs. **Les poses** (`PoseRpc`, `[Rpc(SendTo.NotOwner)]`) : une par tick. **TickRate 60** (scène du menu).
- 🚨 **Une RPC universelle (`[Rpc]`) sans `InvokePermission = RpcInvokePermission.Server` peut être appelée par n'importe quel client.**
- **Le tampon de lecture des inputs** (`InputPlayout`, cas 3) : le serveur rejoue les inputs d'un client au rythme du temps réel au lieu de les simuler à leur arrivée, qui faisait du robot un escalier des instants de réception, chez l'hôte comme dans les poses publiées. Chaque image donne un crédit de temps ; un input ne part que s'il y tient en entier (jamais tronqué : le joueur a prédit sa durée complète). La réserve se règle d'elle-même sur la pire gigue de la dernière seconde, 60 ms au plus (`MaxHold`), et un client plus lent que l'hôte attend à peu près le temps d'une de ses images. Le journal la dit (`reserve=`).
- **L'envoi de Netcode passe avant le rendu** (`NetworkSendBeforeRender`) : il attendait la fin de `FinishFrameRendering`, le rendu entier, à chaque passage dans une machine.
- Restent fiables : les demandes, les sons de catégorie B, les tirs diffusés, et les variables réseau (posture, chargeur, vie, rôle, manche), qui ne changent qu'à l'événement.

### Le pattern à 4 cas (`PlayerLocomotion`, `WeaponController`)

1. `IsOwner && IsServer` (Host sur son perso) : autorité directe.
2. `IsOwner && !IsServer` (client sur son perso) : prédiction locale + `ServerRpc` + réconciliation par rejeu. **`Move()` doit rester STRICTEMENT déterministe** entre prédiction, serveur et rejeu.
3. `!IsOwner && IsServer` (serveur sur le perso d'un autre) : applique `serverInputQueue`, seule source de vérité.
4. `!IsOwner && !IsServer` (spectateur) : interpolation des poses publiées, jamais de logique de mouvement.

### Les 3 catégories, pour toute nouvelle mécanique joueur

- **(A) Simulé/autoritaire**, réseauté via le pattern à 4 cas : position, yaw, vitesses, gravité, sol, posture, vault, lean (son décalage déplace la surface touchable).
- **(B) Événements sonores de gameplay** (`OnPlayerSound`, diffusés par ClientRpc), décidés par le serveur à partir de sa propre simulation (pas, ramper, posture, lean).
- **(C) Cosmétique**, propriétaire seulement : head bob, kick d'atterrissage, roulis de caméra du lean. Ou « C réseauté » : visible par tous, relu par rien (peinture, tangage du regard, drapeaux d'animation).

Le yaw est en A : il oriente le déplacement et le tir. Le lean est A pour son décalage, B pour son son, C pour l'inclinaison de la caméra.

### Ce que le serveur publie : poses datées et posture

- **`DisplayPose`**, à chaque tick (60/s), datée sur `NetworkManager.ServerTime`. `networkPose` (fiable) ne change qu'à l'apparition et aux replacements. À la réception (`AddRemotePose`, pure), une pose plus ancienne que la plus récente est ignorée **avant** le test du replacement ; un trou de plus de 0,5 s vide l'historique.
- **`StanceState`** (`networkStance`) : postures d'arrivée et de départ, instant du changement ; chaque machine calcule la progression (`ResolveStance`, pure).
- **Le spectateur interpole** à `ServerTime − interpolationDelay` (0,02 s, plus les 50 ms de marge de Netcode : quatre poses de réserve), **jamais d'extrapolation** (`SampleDisplayPose`, pure).
- **Les accesseurs d'affichage** (`DisplayAiming`, `LeanOffset`…) choisissent la source : valeur locale chez qui simule, pose réseau chez le spectateur.
- 🚨 **Tout ce qu'un spectateur doit VOIR vient d'une source réseautée**, jamais d'un champ mis à jour par `Move()`, qu'il n'appelle pas (`CurrentStance` et `IsAiming` n'y sont justes que chez qui simule).

### Prédiction et réconciliation

- Le client garde, pour chaque input non confirmé, ce qu'il avait prédit **pour cette séquence** (`PendingInput`). La correction du serveur se compare **à séquence égale** ; seuils 5 cm et 1° ; un désaccord sur le franchissement force le recalage.
- Au recalage, l'état revient aux valeurs confirmées, puis les inputs non confirmés sont rejoués et **leurs prédictions réécrites**. Pendant le rejeu (`isReplayingInputs`), les effets cosmétiques déclenchés depuis `Move()` se taisent.
- La correction reçue est gardée (`StoreCorrection`) et appliquée en tête de la simulation, au plus un rejeu par image (`ApplyPendingCorrection`) ; les inputs non confirmés depuis plus d'une seconde sont oubliés (`DropOldUnconfirmedInputs`). Le joueur borne ses inputs à 0,1 s comme le serveur (`InputDuration`).
- 🚨 **`SimulationState` est un invariant** : tout l'état cumulatif lu par `Move()` (position, rotation, vitesse verticale, `currentVelocity`, vault : drapeau, minuteur, départ, arrivée, `vaultPeakY`). Toute nouvelle valeur cumulative s'y ajoute, voyage dans la correction et se restaure avant le rejeu. Seul `EtatDeSimulation_SeRestaureEntierement` garde vraiment cet invariant.
- **Instant de simulation** : `Move(snap, dt, simTime)` reçoit `NetworkManager.LocalTime` chez le client qui prédit, l'horloge du serveur sur le serveur, **jamais un instant fourni par le client**. Il est rejoué tel quel (`PendingInput.simTime`).
- Ce qui ne passe pas par `Move()` se décide chez le propriétaire avant le snapshot (`ReadLocalInput` : `GateSprint`, `GateAim`), pour que `Move()` reste une fonction du seul snapshot.
- Compromis assumés : la **posture est confirmée par le serveur, pas prédite** ; le gel de manche lu par `Move()` est celui de maintenant pendant un rejeu.

### La compensation de latence (rewind)

- Le tireur envoie l'instant serveur **réellement affiché** de ses adversaires (`ViewServerTime`, 0 pour le Host). Le serveur rewind à cet instant, recul borné à `[0 ; maxRewindSeconds]` (0,3 s).
- L'historique serveur porte la **pose complète** (`HitboxPose`, `SampleHitboxHistory`, pure), sur 1 s (`hitboxHistoryDuration`, qui doit rester **supérieur** à `maxRewindSeconds`).
- **Seules les surfaces touchables voyagent dans le passé** (`ServerBeginRewind` / `ServerEndRewind`, en `try/finally`), jamais les joueurs.
- 🚨 **`Physics.SyncTransforms()` avant ET après le déplacement des hitbox** : `autoSyncTransforms` vaut false, sans ça le rewind serait silencieusement sans effet.

### La surface touchable et le lean façon Rainbow Six

- **`PlayerHitbox`** (enfant `Hitbox`) crée quatre triggers sur le layer **`Hitbox` (7)** : tête, torse, jambes, seconde jambe (allongé seulement). Calculés par **`BodyLayout`** (pure) à partir de la posture réseau, de l'œil et du décalage de lean : identiques sur toutes les machines.
- Œil dans `PlayerLocomotion.StanceProfile` (une seule source pour la caméra et la tête touchable) : debout 1,65 m ; accroupi 1,05 m ; allongé 0,36 m. 🚨 **Les valeurs du prefab priment sur les défauts du code** : `BodyLayoutTests` lit le prefab.
- **Le lean** : torse et tête tournent autour de la base de la colonne ; **les jambes ne bougent jamais**. Borné par `maxLeanOffset` (0,35 m) et `maxLeanAngle` (40°), amorti (`StepLean`, 0,06 s). Le scalaire réseauté est le décalage latéral de l'œil, que l'affichage lit aussi (`PlayerAnimator.ApplyVisualLean`).
- 🚨 **Ne JAMAIS dériver le hitbox des os animés** : un Animator n'est pas déterministe entre machines. L'animation AFFICHE, le hitbox se CALCULE depuis les mêmes scalaires ; aucun ne lit l'autre. Même avec un robot à pièces rigides, on dimensionne les capsules sur les pièces, on n'y accroche pas de colliders.
- ⚠️ L'Animator du modèle reste en `AlwaysAnimate` (la rotation du buste s'accumulerait sinon). Allongé, le modèle est reculé de 0,30 m (`proneModelOffset`) pour que la tête reste dans le `CharacterController`.
- **Le tir se résout en deux traces** (`WeaponController.ResolveShot`, partagée entre le feedback local et le serveur) : le monde (`worldMask`), puis les hitbox (`hitboxMask`) limitées à la distance du mur. Le tireur retire ses propres zones le temps du tir, sur le serveur ET dans son feedback local. Dégâts par zone et distance : `WeaponData.DamageAt`.

### Les transitions de posture

Durées : `standCrouchTransition` 0,3 s, `crouchProneTransition` 0,65 s, `standProneTransition` 0,9 s. **Une seule transition à la fois côté serveur** : c'est aussi la borne de débit de `RequestStanceChangeServerRpc`. Se relever est refusé sans la place (`CanStandUp`). Surface touchable, caméra, vitesse et animation suivent la transition ; **la capsule de collision**, non : instantanée sur la posture d'arrivée (`ApplySimulationCapsule`, appliquée dans `Move()` pour être juste au rejeu).

### Garde-fous serveur

- **`ServerFire` refuse** : un tir trop rapide (crédit de temps `TryAcceptShotRate` : réserve 1,5 intervalle, seuil 0,85), hors des phases de jeu, **d'un robot mort** (le premier tir reçu gagne, tranché par l'utilisateur), à vide, pendant un rechargement ou une vidange ou avant que l'arme soit sortie, en sortie de sprint, en franchissant, avec une arme annoncée différente de celle que sait le serveur, une direction nulle, une origine, direction ou instant non fini, et une origine à plus de 4 m de la position serveur du tireur (dette n° 56).
- **Changer d'arme** n'est accepté qu'avant la manche (`RoundManager.WeaponSwitchAllowed`), jugé **à l'arrivée** de `SwitchWeaponServerRpc`. Refusé, il est quand même compté (`actionsProcessed`) pour que le joueur se recale.
- **Inputs** (`ApplyBufferedServerInputs`) : non finis ignorés (`IsValidInput`), paquet de plus de 40 inputs ignoré ; durée bornée à 0,1 s ; **temps simulé budgété sur le temps réel** (`Time.deltaTime × 1,1`, réserve 0,25 s, 1 ms au moins par input, `MinInputCost`) ; file plafonnée à 512 inputs **et** 1 s de jeu ; files des demandes plafonnées à 64.
- **Trois règles pour toute nouvelle RPC client→serveur** : aucune RPC n'accepte de résultat (au mieux une suggestion, toujours bornée), toute borne anti-abus s'exprime **par unité de temps réel, jamais par frame**, et une demande qui dépend de l'état du joueur **annonce son input de référence**.
- Avec NaN, toute comparaison est fausse : vérifier la finitude AVANT les tests de distance ou de délai.
- **Triche simulée**, en `#if UNITY_EDITOR` : `debugFakeShotOrigin`, `debugFloodServerInputs`, `DebugSprintIgnoresRules`, `DebugVaultIgnoresRules`, depuis une instance **client** (la dernière aussi en Host).

### Le chargeur et la file des demandes du joueur

- **`MagazineState`** (variable réseau en écriture serveur) : l'arme en main, ses balles (**une seule arme dans l'inventaire**, la nouvelle arrive pleine), sa chaleur et ses échéances. Le tireur **prédit** le même compte ; **`ReconcileMagazine` ne recale qu'une fois toutes ses demandes accusées** et jamais pendant que le serveur recharge. Marge serveur de 50 ms sur la fin d'un rechargement.
- **La chaleur des armes laser** (`WeaponData.heatPerShot` > 0) : fonctions pures (`HeatAt`, `HeatRules`…), les mêmes chez le tireur et sur le serveur. **À vide, la gâchette recharge** (`AutoReloadWanted`, pure), sauf les armes laser.
- **File ordonnée** (`ServerProcessArmsActions`) : les demandes retiennent leur input de référence, attendent qu'il soit **simulé** (0,3 s au plus), puis se jugent dans l'ordre sur l'état qui le suit (`ServerInputFlags`, anneau de 1 024 inputs).

### Le taser (gant à décharge, GDD § 6)

Touche V, `TaserController`, tue d'un coup. Geste de `strikeDuration` (0,6 s), un coup par geste, en temps réel (`StrikeServerRpc`). **Le coup part au contact**, `contactDelay` (0,18 s) après l'appui, résolu contre la pose vue à cet instant ; un attaquant tué pendant ce délai ne frappe pas. **`QueryStrike`** : `OverlapCapsule` (1 m depuis l'œil, rayon 0,3 m ; un SphereCast ignorerait un adversaire collé), aucun mur entre l'œil et le point touché : un robot debout se touche jusqu'à 1,44 m entre les deux centres. Pendant le geste, pas de tir, rechargement annulé ; sprinter fait rater le coup.

### Sprint, vitesses et poids des armes

- **Sprint vers l'avant seulement** (≤ 50° de l'avant). **La dernière action l'emporte** (`GateSprint`, pure) ; le serveur annule tout rechargement ou coup suivi d'un input qui sprintait (`ServerSprintedSince`).
- **Sortie de sprint** : `WeaponData.sprintToFireTime` (0,2 à 0,45 s selon l'arme), **compté sur la durée des inputs** (`ServerTimeSinceSprint`).
- **Élan** : au-dessus de l'allure de marche, la vitesse ne retombe qu'à `sprintDeceleration` (25 m/s²). **Directions** (`DirectionSpeedMultiplier`, pure) : ×0,85 plein côté, ×0,7 en arrière. Allongé : reptation à 0,65 m/s.
- **Poids des armes** dans `Move()`, par l'arme du snapshot ; **le serveur fixe lui-même l'arme de chaque input** (`ServerScheduleWeaponSlot`). **On ne tire, ne frappe ni ne recharge en franchissant** (`ServerVaultedSince`).

### L'arsenal : douze armes en quatre familles

Une arme par modèle (GDD § 6). `WeaponController.loadout` (préfab joueur) et l'armurerie (`WeaponCustomizationController.loadout`, scène du menu) les rangent dans le même ordre : **la place d'une arme voyage sur le réseau et dans les réglages du joueur, ne pas réordonner, ajouter à la fin**.

| Place | Arme (asset) | Dégâts (tête, jambes) | Tir | Chargeur ou chaleur | Rechargement | Baisse (portée) | Sortie, poids |
|---|---|---|---|---|---|---|---|
| 0 | FA-07 (`MP5`) | 17 (×1,4) | auto, 7/s | 30 | 2,2 s | aucune (100 m) | 0,6 s, ×0,95 |
| 1 | FP-12 (`Arme_Sniper`) | 100 (×1,5 ; ×0,6) | 0,8/s, lunette ×4 | 5 | 3 s | aucune (150 m) | 0,8 s, ×0,9 |
| 2 | FAP-03 (`Arme_Pompe`) | 8 × 18 | 1,1/s | 6, une à une | 0,55 s | ×0,3 de 8 à 20 m (35 m) | 0,65 s, ×0,95 |
| 3 | FX-4 (`Arme_Raygun`) | 20 (×1,5) | au doigt, 7/s au plus | chaleur 1/12 (12 tirs) | vidange 1,5 s | ×0,7 de 15 à 35 m (60 m) | 0,4 s, ×1 |
| 4 | FA-30 | 17 (×1,4) | auto, 9/s, épaule en 0,1 s | 25 | 2 s | ×0,65 de 15 à 35 m (80 m) | 0,5 s, ×0,95 |
| 5 | FX-1 | 17 (×1,4), recul ×0,6 | auto, 7/s | chaleur 1/22 (22 tirs, 3 s) | vidange 1,5 s | aucune (100 m) | 0,6 s, ×0,95 |
| 6 | FP-30 | 55 (×2 ; ×0,8) | 1,6/s, lunette ×2,5 | 4, une à une | 0,5 s | aucune (120 m) | 0,6 s, ×0,95 |
| 7 | FX-2 | 30 à 100 selon la charge (×1,5 ; ×0,6) | chargé en 1 s, part à la lâche, 0,4 s entre deux | chaleur 0,36 (3 tirs), délai 1,5 s | vidange 1,5 s | aucune (150 m) | 0,8 s, ×0,9 |
| 8 | FAP-30 (slug) | 100 (×2 ; ×0,6), cône 3° → 5° | 1/s | 5, une à une | 0,55 s | ×0,55 de 15 à 30 m (50 m) | 0,65 s, ×0,95 |
| 9 | FX-3 (éventail) | 8 × 20 sur ±4,2° de côté, ±0,3° | 1,4/s | chaleur 0,26 (6 tirs), 4 s | vidange 1,5 s | ×0,35 de 6 à 18 m (30 m) | 0,65 s, ×0,95 |
| 10 | PA-05 | 26 (×1,5) | 4,5/s | 15 | 1,5 s | ×0,75 de 15 à 35 m (60 m) | 0,35 s, ×1 |
| 11 | RV-30 | 45 (×2,25 ; ×0,8), recul ×1,8 | 2,2/s | 6 | 2,6 s | ×0,8 de 20 à 40 m (60 m) | 0,5 s, ×0,97 |

Les armes laser refroidissent en 2,5 s (FX-1, FX-4) ou 4 s (FX-2, FX-3), vident leur surchauffe en 2,5 s, et tirent un rayon rouge (`shotIsBeam`). Les tests fixent les rôles, pas les valeurs (`WeaponArsenalTests`).

- **La gerbe est à motif fixe** (`pelletPattern` ; GDD § 3 : aucun hasard). Les dégâts d'un tir s'additionnent par cible et par plomb, puis s'appliquent une fois.
- **Le recul coup par coup** (`RecoilPattern`, pur ; `WeaponData.recoilPattern`) : caméra du tireur seulement, le serveur ne reçoit que l'axe. Le regard revient vers l'axe à `recoilRecoverySpeed` °/s, pendant la rafale aussi (tranché par l'utilisateur).
- Changer d'arme (`TrySwitchWeapon`, prédit) annule le rechargement, donne la nouvelle pleine (et froide), interdit de tirer avant la sortie, et remet le crédit de cadence à plein.
- **Le tir chargé du FX-2** (`WeaponData.chargeTime` > 0) : la gâchette tenue charge, la lâcher tire. 🚨 **La charge se compte sur les inputs, jamais annoncée par le client** (`ChargeTracker`, deux par joueur : le tireur à l'envoi, le serveur à la réception), seulement quand l'arme pourrait tirer. Le serveur juge la charge de **l'input que le tir annonce** (`ArmsAction.claimedInput`) ; une charge lâchée depuis plus de 0,5 s ne compte plus.

### L'éditeur des armes

**Duel Arena > Armes > Éditeur des armes** (`WeaponEditorWindow`) : il écrit dans les `WeaponData`, `Accessoires.asset` et les pourcentages de recul de `WeaponController` ; rien n'est sur le disque avant « Enregistrer ». ⚠️ En Play Mode, un changement ne vaut que dans l'instance qui le fait (pas chez le joueur virtuel) : régler hors du Play Mode avant un essai à deux.

### Le tir à la hanche façon Rainbow Six

- **Épaulée, la balle part dans l'axe ; à la hanche, au hasard dans un cône** (`HipFireSpread`, pure ; GDD § 3 amendé par l'utilisateur ; réglages par arme dans `WeaponData`). Les armes à gerbe n'ont pas de cône.
- **Le même hasard chez le tireur et sur le serveur** : graine = joueur + numéro de la balle (`MagazineState.shotsFired`). Le client n'envoie que l'axe ; le serveur calcule le cône sur son état de l'input de référence (`ServerHipFireState`).
- **On ne vise pas en rechargeant** (`GateAim`, demandé par l'utilisateur).
- **Là où il arrive** (`ShotImpacts`, catégorie C) : une marque sur le décor jusqu'à la manche suivante, 128 au plus ; sa texture par **Duel Arena > Armes > Préparer les impacts** (`ShotImpactsBuilder` : on corrige le script et on relance le menu).

### Le vault réseauté

- Déclenché et avancé **dans `Move()`** : prédit, rejoué, identique sur le serveur. Départ et arrivée ne sont pas réseautés : recalculés de part et d'autre par `TryFindVaultTarget`.
- Conditions : debout, au sol, hors transition de posture, déplacement permis ; obstacle de 0,3 à 1,3 m avec de la place au-dessus ; chute bornée à 1,5 m (`vaultMaxLandingDrop`). L'arrivée laisse la capsule entière derrière l'obstacle.
- 🚨 **Le passage se vérifie avant de partir** (`VaultPathClear`) : le contrôleur est coupé pendant l'arc (0,45 s), rien d'autre n'arrête le robot. **Sans sol à 1,5 m sous le sommet, pas de passage** (GDD § 7). `FindVault` est statique (`VaultGeometry`) : les tests l'appellent sur la vraie arène.
- **Le passage fermé, le robot se hisse sur l'obstacle** (`FindPerch`, demandé par l'utilisateur), s'il y tient debout et si ses pieds portent de tous les côtés.
- ⚠️ `TryFindVaultTarget` exige `controller.isGrounded`, faux juste après un repositionnement : un vault peut « ne pas passer » une fois de temps en temps.
- ⚠️ **Un obstacle de moins de 15 cm d'épaisseur ne se franchit pas** : la sonde cherche son dessus 15 cm derrière la face et trouve le sol.
- Dans `MultiTestScene`, **`Barricade 1.6` est refusée exprès**.

## La boucle de manches (BO5 ou BO3) et les joueurs dans l'arène

- **`RoundManager`** (`/RoundManager`, `NetworkObject` de scène), entièrement serveur : phase, score et transitions en variables réseau. Seule demande d'un client : « prêt » (`PlayerMatchState.ReadyServerRpc`).
- **Une manche** : `Preparation` (`preparationDuration` 30 s, finie dès que tous ont validé) → `Starting` (3 s) → `Active` → `RoundOver` (5 s : **le survivant garde la main** pour narguer, puis fondu au noir). Le match gagné (`MatchRules.roundsToWin` : 3 en BO5, 2 en BO3) : `MatchOver`, le même temps pour le gagnant (`EndScreenDelay`), puis l'écran de fin. Pas de limite de temps (GDD § 5).
- **L'abandon est un forfait** (`ForfeitApplies`, `ServerOnDuelistLeft`) ; tant qu'un match se joue, un nouveau venu passe par la file (`DuelRoster.Sync`). **La fin du match se vote** (`endScreenDuration` 15 s, `DecideVote`, pure) : **la revanche demande les deux voix**, sinon retour au salon.
- **Le salon de la partie est une phase de l'arène** (`Salon`, `SalonPanel`) : seul l'hôte change les réglages (`ServerSetRules`) et lance (`ServerLaunchFromSalon`). Revenir au salon du menu obligerait à arrêter Netcode (dette n° 68). Le tableau de la soirée : `RoundManager.evening`, `Evening.Tally`, pure.
- **Ce que chaque phase permet** (pures) : `PlayAllowed(phase, vivant)`, **jamais mort** ; `WeaponSwitchAllowed(phase, prêt)`. Lus dans `Move()` (`MovementAllowedFor`), au tir et au taser, chez le joueur et sur le serveur.
- **Mort détectée par sondage** de `Health.IsDead`. **Seuls les vivants marquent** (`RoundScorers`, pure).
- **`BeginRound`** soigne avant de replacer (`ServerMoveToSpawnPoint`). 🚨 Le lean demande une RPC au propriétaire (`ResetLeanClientRpc`) : son état est une bascule côté client.
- **La fiche de chaque joueur** (`PlayerMatchState`, écrite par le serveur) : pseudo, « prêt », statistiques (`MatchStats`, pendant `Active` seulement), gardées sur le serveur et publiées à la fin du match (`ServerPublishAllStats`).
- **Les miettes se ramassent sous le noir** (`RoundManager.CollectsGarbageEntering`, `GC.Collect`), jamais en pleine manche ni pour un couloir du Tournoi.
- **Points d'apparition** (`PlayerSpawnPoints`) : `Spawn A` (0, 0, 2), `Spawn B` (0, 0, 58) ; chacun change de côté à chaque manche (`RoundManager.SidePost`, pure). ⚠️ `/Spawn` et `/Spawn Enemy` sont de la géométrie, pas des points d'apparition.
- **La couleur** (`PlayerPaint`, `PickPaint`, pure) : deux robots n'ont jamais la même ; appliquée par `MaterialPropertyBlock` aux seules pièces de `paintedRenderers`.

### La mort : l'épave et la vue du joueur mort

- `PlayerLocomotion.ApplyLifeState`, sur toutes les machines, d'après la vie réseau : la capsule du mort passe sur le layer **`Corpse` (10)**, qui ne touche que le décor, sa surface touchable s'éteint ; `obstacleMask` exclut `Corpse`.
- **L'épave** (`RobotCorpse`, catégorie C) : une copie du modèle en ragdoll, ses limites comptées depuis la pose de la mort. **Sa chute** (`CorpseFall`) : le serveur envoie le point, l'impulsion, la proximité du tireur et une graine (`KilledClientRpc`) ; chaque machine en tire le même plan (`CorpseFall.For`, pure).
- 🚨 Un coup pousse tout le corps (`RobotCorpse.Push`) : tout mettre sur la pièce touchée faisait fouetter les pièces légères. La vitesse héritée se mesure sur 0,25 s, jamais sur une image.
- **Le joueur mort** : `PlayerDeath`. **L'écran du visage** : `RobotFaceScreen`, masques par `Tools/Blender/ecran_mort.py`.
- **Le robot vole en pièces** (GDD § 6, à l'essai) : `RobotCorpse.Burst`, `PartsToDetach`, pur, `RobotBurst` ; ses gerbes naissent en file, une pièce par image. 🚨 **Seuls les robots à pièces rigides perdent des pièces** (`RobotChassis.HasRigidPieces` : le CT-01 et l'OS-02). Matériaux par **Duel Arena > Mort > Préparer les éclats du robot** (`RobotBurstMaterials`).

### La kill cam : le coup mortel en direct

GDD § 6 ; l'utilisateur : « ne doit pas être un replay ». Elle filme la vraie partie au ralenti (`KillCam`), comme dans *Sniper Elite*. Catégorie C : le serveur ne décide que quand, et la légende.

- **Le serveur sait tout de suite** : le rewind fini, `ServerFire` et le gant appellent `RoundManager.ServerJudgeKill`. Une fin de manche ou de match gagnée sur ce coup (`ServerKillCamWanted`) allonge sa phase et la publie (`KillCam.Info`) ; la mort et le tir diffusé partent avec cette réponse. Le temps de la kill cam, tout le monde est gelé (`KillCamFreezes`).
- 🚨 **La simulation ne ralentit jamais, pas de `Time.timeScale`** (l'hôte simule les autres, et d'autres matchs au Tournoi). Ralentit seulement ce que montre chaque machine, à `KillCam.WorldRate` (pure) : la mort se voit à l'impact (`HoldForKillCam`), les robots (`KillCam.RateOf`), la physique (`Physics.simulationMode = Script`, rendu à la fin).
- **Le déroulé** (`TimelineFor`, pur : la même durée partout, de 3,05 à 5,05 s).
- La caméra du joueur s'éteint sous la kill cam et sous l'écran de fin (`KillCam.CoveredCamera`, son composant seul) : `Camera.main` y est la leur, l'`AudioListener` reste. La kill cam prépare sa caméra et sa balle dès son apparition.

## Les modes de jeu

- **Le mode est un réglage de la partie** (GDD § 11 ; `MatchRules.mode`, `GameModeId`), publié au salon, lu par `RoundManager.Mode`. Un mode qui devient jouable : sa carte (`GameModeAvailability.Playable`), ses rangées du salon (`SalonView.EssentialRows`), ses règles dans `RoundManager`.
- **Les armes des modes sont hors de l'arsenal** (`WeaponController.modeWeapons` ; `GoldenGunSlot` = `MaxSlots`, 12 ; `GloveSlot`, 13). **Le serveur seul les met en main** (`ServerForceWeapon`) et compte ces changements (`MagazineState.forced`) : le joueur prend l'arme imposée **dès qu'il voit ce compte changer, même avec des tirs en route** (`ReconcileMagazine`). L'arme choisie est rendue quand le mode change (`ServerReturnToArsenal`).
- **Les réapparitions** (`RoundManager.HasRespawns`) : le match se joue d'une traite (`TickRespawnRound`). Le mort revient `respawnDelay` (3 s) plus tard, sans protection, au point le plus éloigné de l'adversaire parmi ceux qu'il ne voit pas (`PickRespawn`, pure). Seize points, posés par **Duel Arena > Arène > Poser les points de réapparition** (`RespawnPointsBuilder` ; à refaire pour chaque arène et après une retouche du blocking).
- **Le Golden Gun** : sans préparation (`HasPreparation`), premier à 5 ou à 7. `Arme_GoldenGun.asset` : 100 dégâts partout, 150 m, une balle, rechargée d'elle-même en 2,5 s (`reloadsAfterEachShot`).
- **Le Gun Game** : les échelles (`GunGame`, pures ; leurs places y sont écrites, un test les compare au préfab), chacune finie au gant (`WeaponData.unarmed`). `GunGame.AfterKill` : à l'arme, le vainqueur monte ; au gant sur la dernière marche, il gagne ; au gant avant, le tué redescend d'un cran.
- **Le Death match** : une case du classique (`MatchRules.deathMatch`), `killsToWin` (5, 10 ou 15) à la place des manches ; se tuer ne compte pour personne (`KillScores`, pure).
- **L'Élimination confirmée** : le robot détruit lâche son module mémoire (`RoundManager.modules`). **L'élimination ne compte que quand son vainqueur le ramasse** ; la victime peut le reprendre (`KillConfirmed.Resolve`, pure, 1,2 m). Le mort ne réapparaît pas à moins de 10 m de ses modules. Préfab : **Duel Arena > Modes > Construire le module mémoire** (`MemoryModuleBuilder`).
- **La Zone** (`ZoneMode`, pure ; `ServerTickZone`) : elle change de place toutes les 60 s, annoncée 10 s avant. Seul dedans et vivant, 20 points à chaque seconde pleine ; à deux, rien n'avance ; une élimination en rapporte 100 ; premier à 3 000, ou le plus de points au bout de 10 minutes, prolongation à égalité. Le mort ne réapparaît ni dedans ni à moins de 8 m de son bord. Emplacements : **Duel Arena > Arène > Poser les zones** (`ZoneLayoutBuilder` : on corrige sa liste `Places`, jamais la scène ; à refaire pour chaque arène).
- **Le Tag Team** (`TagTeam`, pures) : deux équipes égales de deux ou trois. Le premier de chaque équipe se bat, les autres attendent sur la tribune. 10 points par élimination, 15 à la tête, 20 au gant, 100 pour gagner ; 3 s plus tard l'équipe du tombé passe le relais, le survivant garde ses PV. **La tribune ne frappe pas son propre combattant** (`RoundManager.Teammates`).
- **Le Tournoi** (`Tournament`, pures) : de trois à six joueurs, une poule en BO3 puis la finale en BO5. **Le RoundManager de la scène dirige** (`Phase.Tournament`). **Les matchs d'un tour se jouent en même temps, chacun dans son couloir** : un RoundManager apparu pour lui (`MatchTournoi.prefab`, dans les préfabs réseau ; `LaneInfo`), qui rend son résultat (`ServerOnLaneFinished`) et disparaît.
- 🚨 **Chaque machine sait quel RoundManager arbitre chaque joueur** : `RoundManager.For(clientId)` et `RoundManager.Local` ; `Instance` n'est jamais un couloir. Les portes du jeu se lisent pour le joueur (`MovementAllowedFor(clientId, …)`, `FiringAllowedFor`…). Personne ne se blesse hors de son match (`MayHurt`).
- **Le site** : **Duel Arena > Arène > Poser le site du tournoi** (`TournamentSiteBuilder` ; à refaire après une retouche du blocking ou de l'habillage, puis Cuire la lumière) : deux copies de l'arène à ±2 km sur X, la salle d'attente à 2 km au sud (`TournamentFeeds`, `TournamentBoard`). Hors du Tournoi, le site s'éteint à l'exécution, ses objets réseau exceptés (`TournamentSite.SetLive`), et ses copies n'ont pas de lot statique, qui se chargeait à chaque arrivée dans l'arène. Chez une machine qui regarde un autre match, ni tir ni mort ne se montrent (`PlayerDeath.ShowsEffectsOf`). 🚨 Jamais un double de l'objet : retirer le ProBuilderMesh d'un double détruirait le maillage qu'il partage avec l'original.

### Les options : le choix de l'arme et Tête seulement

Le choix de l'arme (`MatchRules.weaponChoice`) ne vaut que dans les modes à manches où l'arme est libre (`WeaponChoiceApplies`). **L'arsenal commun** (`CommonArsenal`, pures) : pas de préparation, une arme toutes les deux manches, jamais la même deux fois de suite, ses accessoires tirés au sort. **Pile ou face** (`CoinFlip`, pures) : le joueur que désigne la pièce, puis le perdant, choisit la famille en 10 s, jamais deux fois de suite la même ; seules ses armes se prennent, chez le joueur et sur le serveur (`WeaponAllowedFor`). **Tête seulement** (`WeaponController.Hurts`) : seuls les impacts à la tête blessent ; le gant tue toujours.

## Les spectateurs et la tribune

GDD § 9 : un robot à mains nues sur une passerelle au-dessus de l'arène.

- **Les rôles** (`PlayerRole`, écrits par le serveur seul). `RoundManager` tient les duellistes et la file (`DuelRoster`, pure). Réglages de partie : `MatchRules`, capacité 2 à 6, 4 par défaut.
- **Un spectateur est un joueur ordinaire** (même prefab, même `PlayerLocomotion`, même pattern à 4 cas) dont le rôle filtre les entrées, chez lui ET sur le serveur (`SpectatorInputFilter`) ; `Move()` refuse aussi le vault (`!spectating`). Seul l'étourdissement le fige (`SpectatorController.IsStunnedAt(simTime)`).
- 🚨 **Le garde-corps (1 m) retient les spectateurs parce qu'ils ne franchissent rien** (borne du vault : 1,3 m) et ne sautent pas.
- **`SpectatorController`** : geste 0,45 s, contact à 0,15 s, portée 1,5 m ; étourdi 2,5 s au poing, 1,5 s au caillou, puis 1,5 s d'immunité ; un lancer part à 24 m/s.
- **La garde** part **dans le bit de visée des inputs** (`aimHeld`), publiée dans la pose, enregistrée dans le rewind (`HitboxPose.guard`). Le serveur la juge **à l'instant que voyait le frappeur** (`GuardStops`, `guardHalfAngle` 70°) ; un caillou se juge au présent.
- **Le serveur juge tout** : le poing contre les poses passées (`PlayerLocomotion.ServerTrySamplePose`, 0,3 s) ; le corps d'un spectateur est une **capsule analytique** (`SpectatorRules`, rayon 0,35 m), pas un collider ; le lancer est une suggestion bornée (`ThrowVelocity`).
- **`Tribune`** (`NetworkObject` de scène) : la passerelle (sol à 4,5 m, garde-corps 1 m), **construite par le menu contextuel « Construire la passerelle »** (`EditorBuildWalkway`), **à reconstruire pour chaque arène**. Blocs (`RockSource`) cassés au 4ᵉ coup, reformés en 8 s. **Plus rien ne tombe** d'ici les armes insolites (`weaponDrops`).
- **`ArenaItem`** (prefab `ObjetTribune`) : **simulé par le serveur seul**, publié en paraboles par morceaux (`Motion`) que chaque machine évalue (`Evaluate`, pure). Un caillou sur un duelliste : 3,5° de regard vers le haut, aucun dégât (`ServerRockHitDuelist`) ; il voit d'où il vient (`HitIndicatorHud`). Une arme ramassée (`ApplyPowerUp`, pure) se juge dans la file ordonnée.
- **Vue subjective** (`SpectatorView`, construite au premier besoin, dès le début du match au Gun Game : le gant seul arrive en plein combat). 🚨 **Sa tête n'est pas dans l'œil** : la copie se tient en arrière et plus bas (`viewOffset`, `guardViewOffset`) ; tête dans l'œil, la caméra était dans le buste du CT-01.

## Le menu principal et la session

- **Deux scènes** : `MainMenu.unity` (index 0, porte le `NetworkManager`, `NetworkBootstrapUI` et `SessionFlow`) et `MultiTestScene.unity` (index 1, sans `NetworkManager`, avec `/GameUI`) ; plus `StandDeTir.unity` (index 2).
- **Au menu, seulement les modes ; l'organisation et les options se règlent au salon** (GDD § 11 ; `PlayScreenController`, `GameModeCatalog`). Le réseau local sans code sert à jouer sans internet et au banc de test (`127.0.0.1:7777`).
- **Réseau local** : `StartHost()`, puis `NetworkSceneManager.LoadScene("MultiTestScene", Single)`. 🚨 Le joueur du Host naît dans la scène du menu : `RoundManager.OnNetworkSpawn` le place à un point d'apparition une fois l'arène chargée, et la caméra du menu coupe son `AudioListener` dès que Netcode tourne (`ArenaFallbackCamera`).
- **`SessionFlow`** : l'hôte approuve chaque connexion (`ConnectionApproval`) : capacité, et la même `Application.version`. **Monter « Version » (Player Settings) à chaque build envoyée aux testeurs.** 🚨 **Une connexion acceptée pendant que l'hôte est encore sur le menu reste en attente** (`response.Pending`) jusqu'au chargement de l'arène : sinon le joueur naissait dans la scène du menu, détruite avec elle.
- **La demande de connexion** (`BuildPayload` / `ParsePayload`, 256 octets au plus) : version, pseudo, apparence, accessoires, couleur, châssis, identifiant de joueur. 🚨 Le pseudo est lu **une fois par lancement** et gardé en mémoire : deux instances sur un PC partagent la clé du registre.
- **Retour au menu** : `ReturnToMenu` quitte le salon en ligne s'il y en a un, sinon `NetworkManager.Shutdown()`, puis détruit l'ancien `NetworkManager` (Netcode n'en départage pas deux) et charge le menu de façon asynchrone. L'hôte prévient d'abord ses clients (`SessionFlow.HostLeftReason`).
- **Le chargement d'une partie** : le noir de l'arène tant que la partie n'est pas prête (`RoundManager.Gathering`).
- **UI Toolkit** : `Assets/_ProjectUI/` (`Shared/Theme.uss`, la charte) et `Assets/_ProjectScripts/UI/`. Chaque contrôleur retrouve ses éléments par nom. `LICENCES.md` (polices), à joindre aux mentions légales avant la sortie.
- **Les images du menu** : **Duel Arena > Menu > Capturer les images du menu** (`MenuArtworkCapture`), à relancer quand l'arène ou le stand change. Vignettes : `MenuThumbnails`, `WeaponPortraits`. `SalonPanel` ne porte que l'image et le nom de l'arène (`arenaImage`, `arenaName`, posés dans la scène) : `MenuArtwork` ne se charge pas dans l'arène.
- **Personnalisation** : `ArmoryController`, `WeaponCustomizationController`. La fiche (`WeaponStatSheet`, pure) : l'IA annonce les tirs pour neutraliser, comptés comme le serveur (Bible § 9).
- **Options** (`GameSettings`, `OptionsController`, un seul panneau, branché à sa première ouverture : `OnShown`, `Wire`). **Rien ne s'applique avant le bouton Appliquer.** Remappage depuis la carte `Gameplay`, sans détection de conflit.
- **Le VSync et le plafond d'images s'appliquent au lancement** (`ApplyFramePacing`) : VSync coupée, plafond à 240 pour qui n'a rien choisi (`DefaultFrameRateCap` : l'hôte simule un input par image de chaque client). Le menu se plafonne à la fréquence de l'écran, 120 au plus (`MenuFrameRate`). Sous l'écran de chargement, le chargement prend de plus longues images (`SpeedUpLoading`) ; les services en ligne se préparent sur l'écran titre ; l'arme de l'armurerie ne se redessine qu'à un changement (`PreviewStage.StepRedraw`).
- **Le salon** (`LobbyController`, `SalonView`, partagé avec l'arène) : choix en données (`SalonView.EssentialRows`, `AdvancedRows`, pures). Les réglages voyagent dans la propriété `regles` du salon (`FormatRules` / `ParseRules` ; un champ absent garde sa valeur par défaut).
- **Pas encore vérifié** : deux PC sur deux réseaux différents ; le refus d'un joueur de trop et d'une autre version dans le salon ; Rejoindre une adresse en réseau local par le menu.

### La partie en ligne : salon privé et code

GDD § 4 : « uniquement des lobbys privés avec un code ». Paquet `com.unity.services.multiplayer` 2.4.0, connexion **anonyme**. `OnlineSession` (statique, `Multi/`) : un salon privé de 6 places ; `StartRelayNetworkAsync` démarre Netcode, chaque membre se connecte tout seul ; Netcode garde l'approbation.

- 🚨 **On quitte par le salon, jamais par `NetworkManager.Shutdown`** tant qu'il existe : `OnlineSession.LeaveAsync`.
- `MaxPlayers` ne se modifie plus : la capacité choisie est tenue par le verrou du salon (`ShouldLock`) et le retrait du dernier arrivé. Envois regroupés (`RequestHostStateSave`) : le service refuse deux modifications concurrentes.
- L'ordre du salon va jusque dans l'arène (`SessionFlow.LaunchOrder`, `DuelRoster.ArrangeByRank`). Un profil de connexion par instance du jeu (`ProfileName`).
- 🚨 **Rejoindre une partie déjà lancée détruit le menu avant la fin de la tâche** : après chaque `await`, le contrôleur vérifie qu'il existe encore (`this == null`).

### L'interface de la partie (UI Toolkit)

- **`/GameUI`** : `GameHud`, `LoadoutPanel`, `MatchEndScreen` + `VictoryStage`, `SalonPanel`, `TournamentBoard`, `HitIndicatorHud`, `PauseMenu`, `SpectatorHud`, qui lisent l'état publié par le serveur et ne décident rien. **`GameHud` arbitre le curseur et les commandes** (`PlayerInputReader.Mode.None` quand un écran est ouvert) et le fondu au noir.
- **Le HUD de combat** (`CrosshairUI`) : le robot du joueur crée lui-même son document (`Assets/_ProjectUI/Game/CombatHud.uxml`, `sortingOrder` -10 : sous les menus), chez le propriétaire seul, le même dans l'arène et au stand. `CombatReticle`, `HitMarker` (pur), `DamageStaticOverlay`, `ScopeOverlay`, le sursaut (`PlayerCameraLook.Flinch`).

## Le stand de tir

Un hôte qui n'écoute que `127.0.0.1`, sur un port libre (`MainMenuController.StartShootingRange`) ; sans `RoundManager`, le jeu est libre. Code dans `Assets/_ProjectScripts/Range/`. **La scène se construit par Duel Arena > Stand de tir > Construire le stand** (`ShootingRangeBuilder`) : on corrige le script, pas la scène ; puis **Duel Arena > Stand de tir > Cuire la lumière du stand**. Le jeu n'est fait que de colliders, revêtus par `StandDecorBuilder`, éclairés par `StandLighting`. Mannequins : `TrainingDummy` (hors du champ, leur Animator s'arrête). ⚠️ Un mannequin droit devant à 10 m cacherait tous ceux du milieu derrière lui.

### La Fosse : le parcours chronométré

Construite avec le stand (`BuildCourse`). Règles : `ShootingCourse`, pures ; cibles : `CourseTarget` (la Fosse resynchronise la physique quand une cible bouge). Une retouche du tracé se vérifie d'abord par le tour à pied (`CourseRoute`). ⚠️ **Un essai de la Fosse écrit un record dans les PlayerPrefs**, partagés par l'Editor, son clone et les builds du PC : l'effacer après (`LaFosse2.Record.<0-11|mixte>`, et le cache de `CourseRecords`).

### La salle de précision : la cible ronde

`AimRange` (`ShootingRangeBuilder.BuildAimRoom`, `StandDecorBuilder.DressAimRange`), chez l'hôte seulement : rien n'est réseauté.

## L'arène habillée : la salle de test balistique B-01

L'utilisateur a tranché : **on garde le layout**, l'arène devient une **salle de test balistique** (bible § 13), « **projecteurs, pénombre** ». Tout est construit par script (skill `arene`) : **Duel Arena > Arène > Habiller l'arène**, puis **Cuire la lumière** ; on corrige le script (`Assets/_ProjectScripts/Editor/Decor/`), jamais la scène. Après une retouche du blocking, refaire les deux, puis la capture des images du menu.

- 🚨 **Le blocking reste la vérité du jeu** : chaque bloc ProBuilder garde son collider ; l'habillage n'éteint que son rendu (`ArenaDecorTests`). ⚠️ Une caisse coupée à mi-hauteur se lit comme un meuble à tiroirs : ce sont les verticales qui disent « caisse ».
- **Le shader `DuelArena/Decor`**, recettes dans `DecorMaterials`. ⚠️ Un cube d'Unity n'a pas de couleur de sommet : le shader y lit « bord » partout.
- **La lumière** (`ArenaLighting`) : pas de lampes de reflet, la lumière cuite fait ce travail. 🚨 Les calques d'une lumière se règlent dans `UniversalAdditionalLightData.renderingLayers` (`ArenaLighting.SetLayers`), pas seulement dans `Light.renderingLayerMask`.
- **Ce qui garde le rendu léger** (vue du poste A, Editor, 1080p : 2,8 ms de GPU, 3,7 ms de fil principal). Depth priming Auto (`PC_Renderer`). 🚨 Sous lui, un matériau opaque sans passes `DepthOnly` et `DepthNormals` disparaît sans message (`RenderStyleTests`) : les marquages, qui les coupent, sont en file 2501, le contour des robots passe sous `DecorContour`. Les bras ont leur renderer (`PC_Renderer_Bras`, sans SSAO ni priming). SSAO en demi-résolution. Dans `UsureBlender.hlsl` : hachage rapide pour le décor (`ARME_HACHAGE_RAPIDE`), octaves coupées sous le pixel (`ArmePied`), chaque bruit après son masque.
- **D3D11 seul** : mesuré sur une build (2026-11-04), D3D12 a le même GPU, plus de temps sur le fil de rendu (1 ms contre 0,6) et des pics à 18 ms contre 5.
- **Le post-traitement passe partout, une fois** (0,15 ms) : sur la dernière caméra qui dessine dans la pile, les bras, sinon le joueur (`FirstPersonView.ShowArmsCamera`), et sur la kill cam et l'écran de fin. Sur deux caméras d'une pile, il s'appliquerait deux fois.
- **Le rendu n'est pas choisi** (bible § 18) : `_DecorStyle`, réglé dans les Options (`RenderStyle` ; **les deux mêlés par défaut**). Contour : `DecorOutlineFeature`. Robots : **Duel Arena > Rendu > Préparer le réglage du rendu** (`RenderStyleSetup`, à relancer quand un châssis gagne un matériau). L'essai : **Duel Arena > Essais > Captures des trois rendus de l'arène**. 🚨 Qui règle ou éteint une lueur de robot le fait pour les deux shaders (`PlayerPaint.PaintPart`, `RobotFaceScreen`, `TrainingDummy.Tint`).

## Le personnage et l'animation

- **Le robot du jeu est le CT-01 ; le Y Bot de Mixamo et l'OS-02 restent au choix.** Tous sont des pièces de `Model` sur le même squelette (celui du Y Bot, 1,805 m) et le même Animator : `RobotChassis` allume celles d'un châssis, `PlayerChassis` le publie. Le soldat SWAT (`SwattSolider_T_Pose.fbx`, allégé de ses textures embarquées par `Tools/Animations/fbx_sans_textures.py`, à l'avatar identique) reste comme **avatar source de 12 clips**.
- **Monter les robots de Blender : Duel Arena > Robot > Monter les robots sur le préfab joueur** (`RobotChassisBuilder`), après chaque export (`robot_ct01.py -- --skin vierge --fbx "Assets/_ProjectArt/Robots/CT-01/Robot CT-01.fbx"`, de même `robot_os02.py`). 🚨 Le FBX de Blender a les os du Y Bot à la même place, mais à l'échelle 0,01 : recopier ses matrices de liaison grossirait le robot cent fois. On corrige le script de Blender et on relance le menu, jamais le maillage ni le préfab.
- 🚨 Le haut du torse touchable est une demi-sphère : des épaules hautes en sortent, et une balle y passerait sans toucher ou compterait comme un tir à la tête. 🚨 **Un nouveau robot se mesure avant d'être un châssis** (`Tools/Blender/couverture_robots.py`) : un robot plus fin se voit et se vise moins bien pour la même surface touchable. ⚠️ Un acier tout à fait métallique reflète le décor sombre et paraissait noir (`Chrome`, métallique à 0,65).
- **Les essais de rendu** (en attente du choix de l'utilisateur) : `Assets/_ProjectArt/Essais/Rendus/`, **Duel Arena > Essais > Construire l'essai des rendus du robot** (`RenderTrialBuilder`), **Duel Arena > Essais > Construire l'essai du robot CT-01**.
- `PlayerAnimator` (catégorie C stricte : il lit, n'écrit rien que la simulation relise) dérive la vitesse **du déplacement du transform**. Le propriétaire voit son modèle en `ShadowsOnly` : son ombre reste une information de jeu.
- Mixamo (autorisé par l'utilisateur) : par l'API du site, depuis le navigateur où il est connecté ; **mesurer un candidat avant de l'importer** : `python -I Tools/Animations/fbxspeed.py <fichier.fbx>`. Casting : la page [Animations Duel Arena](https://claude.ai/artifact/VHunU2FqQ8XUoMHxSbZRM3).

### Conformité d'import (non négociable)

- **Rig Humanoid**, `Copy From Other Avatar` vers l'avatar du squelette d'origine du clip.
- **`lockRootPositionXZ` à FALSE** (« Bake Into Pose » décoché) : le déplacement devient du root motion, que `applyRootMotion = false` jette. **La position horizontale appartient à `Move()`.**
- Clips de déplacement : `keepOriginalOrientation` TRUE, `lockRootRotation` TRUE ; **debout, la gauche est le miroir de la droite, fait À L'IMPORT**, jamais par la case miroir d'un arbre. Clips qui montent ou descendent sur place : `lockRootHeightY` TRUE.
- **Un clip calculé dans Blender** s'exporte comme ceux de Mixamo : le squelette seul, en centimètres (`apply_scale_options="FBX_SCALE_NONE"`), l'avatar du Y Bot copié.
- ⚠️ `rotationOffset` +θ tourne le déplacement de −θ ; le miroir le retourne.
- **Sur Mixamo, « In Place » DÉCOCHÉ** pour un clip de déplacement.
- 🚨 **Le `.meta` n'est pas une source fiable** pour les réglages d'import : interroger `ModelImporter` (`LocomotionImportTests`).

### L'Animator (`Assets/_ProjectArt/PlayerAnimator.controller`)

- `MoveX`/`MoveY` = vitesse locale ÷ référence, `Allure` leur norme, `StanceF` le miroir flottant de `Stance`. Couches : `HautDuCorps` (masque, racine EXCLUE), `Taser`, `GestesSpectateur`, `CorpsEntier`. Le tir passe par un **événement** (`OnShotFired`).
- 🚨 **Un arbre de mélange synchronise ses clips** : l'attente et le déplacement sont deux branches séparées. 🚨 **Unity mêle le déplacement de racine dans le repère du buste de chaque clip** : d'où les miroirs à l'import.
- 🚨 **`BlendTree.useAutomaticThresholds` vaut TRUE par défaut et réécrit les seuils** : toujours le passer à false avant d'écrire des seuils qui ont un sens.
- ⚠️ Les clips accroupis Mixamo mélangent genou à terre et squat : les deux couches doivent servir la même famille de pose.
- **La reptation de côté** : `Tools/Blender/animations/reptation_laterale.py`, **Duel Arena > Animations > Importer la reptation de côté**. 🚨 **Chaque côté est calculé, jamais mis en miroir** : le miroir d'Unity sur un clip couché enfonce les orteils dans le sol.
- La garde du spectateur : **Duel Arena > Animations > Construire la garde du spectateur**.

### Des pieds calés sur le sol (exigence de l'utilisateur, GDD § 7)

`DriveGait` mesure la cadence (`AnimatorRootMotionProbe`), `FootPlanter` maintient chaque pied par une IK à deux os (`TwoBoneIK`). Exigé par les tests : glissade médiane en appui ≤ 5 %, totale ≤ 15 % (`FootSlideTests`, sauf la course de côté à 6 %), pieds à 97-101 % de la vitesse du corps (`LocomotionFootTests`).

### Le buste suit le regard

`PlayerAnimator.ApplyAimDirection`, après l'Animator et avant le lean. **Le tangage voyage dans les inputs** (`SanitizeAimPitch`) : catégorie C réseautée, relue par rien.

### Les armes visibles, la vue subjective et le viseur

- **Les modèles** (`Assets/_ProjectArena/Weapon/Armes/<CODE>/`, `WeaponModel`) : `Tools/Blender/exporter_armes.py`, puis **Duel Arena > Armes > Importer les armes de Blender** (`WeaponImporter`). On corrige le script de l'arme et on réexporte, jamais le préfab ni ses matériaux (skill `blender`). Finitions : `WeaponFinishes`, `DuelArena/ArmeUsee`. En main : `HeldWeapons.Show`, au repère `PriseArme` : une seule copie sous la main, créée à sa sortie, détruite à la suivante ; un robot masqué garde son arme (`WeaponToShow`). Flammes et rayons sortent d'une réserve (`WeaponVisualFeedback`), les impacts se préparent à l'arrivée (`ShotImpacts.Prepare`).
- **Les mains sur l'arme** (`WeaponGrip`) : une IK à deux os, **après la direction du buste et le lean**. **Une poignée se monte à portée de la main** (accroche `Poignee` 3 à 9 cm devant `MainGauche`).
- **Vue subjective** (`FirstPersonView`, propriétaire seulement) : une copie du `Model` sous la caméra, layer **`Viewmodel` (8)**, qui ne reprend du corps que **les gestes de l'arme**. 🚨 Les bras sont les enfants du buste : replier son os par l'échelle les replierait avec lui ; chaque peau lie ses pièces à un repère d'échelle nulle (`FoldPieces`).
- **Le gant** : `exporter_accessoires.py --scripts gant_choc.py`, **Duel Arena > Armes > Importer le gant de Blender** (`GloveImporter`). Ses arcs (`TaserSparkFX`, `ElectricArc`) : 🚨 ils **sondent** `TaserController.LastStrikeSeenAt` au lieu de s'abonner à `OnStrike` : la copie du gant des bras ne s'active qu'après l'appui.

### Les accessoires des armes

- **Les modèles** (`viseurs.py`, `crosses.py`, `canons.py`, `chargeurs.py`) : **Duel Arena > Armes > Importer les accessoires de Blender** (`AttachmentImporter`), catalogue `Accessoires.asset` (`AttachmentCatalog`). ⚠️ Une crosse copie la crosse d'origine de son arme, un canon long ou court l'avant de son arme : retoucher le script d'une arme demande de réexporter ses crosses et ses canons.
- 🚨 Le viseur tient sur 4 bits (`WeaponAttachments.Sight`, 0 = la mire) : les quinze prennent toutes les places, un seizième demandera un bit de plus. Les onze sans grossissement ont la même fenêtre utile, 15° (demandé par l'utilisateur pour l'égalité entre les joueurs).
- **Le choix** (`WeaponAttachments`, 16 bits par arme), borné par le serveur, publié (`PlayerWeaponAttachments`) ; ce qu'une arme prend : `WeaponAttachments.Accepts`.
- 🚨 **Le chargeur se compte avec les accessoires** (`WeaponController.MagazineSizeOf`, `ReloadDurationOf`, `AimTimeOf`), chez le joueur et sur le serveur ; ils sont publiés à temps : Netcode fait apparaître les composants de la racine avant ceux des enfants. Plus rien ne lit `WeaponData.magazineSize` ni `reloadDuration` directement.
- **Les effets sont des données** (`AttachmentCatalog.effects`). `AttachmentCatalog.Factor` les multiplie toujours dans le même ordre : le tireur et le serveur tombent sur le même nombre, au bit près. 🚨 Une caractéristique nouvelle s'ajoute à la fin de `WeaponStat` (enregistrée par sa valeur), et seulement une fois lue par le jeu, chez le tireur et sur le serveur, par `Factor`.
- Le montage : `AttachmentMount`, qui retient ce qui est monté (`knownMounts`, oublié par `Apply`). Un viseur se juge depuis l'œil du jeu (skill `blender`).
- **Le laser** (`TacticalLaser`, catégorie C réseautée) : l'arme tenue, le point est là où regarde le joueur, le même sur tous les écrans ; un geste qui écarte l'arme du regard, il la suit (`FollowsGaze`, pure).

### Le rechargement à chargeur

FA-07, FA-30, FP-12, PA-05 : le chargeur quitte vraiment l'arme (dette n° 82 pour les autres). Catégorie C. Clips : `Tools/Blender/animations/rechargement_chargeur.py`, **Duel Arena > Animations > Importer les rechargements à chargeur** (`MagazineReloadImporter`) ; ni l'Animator ni les gestes ne se retouchent à la main. `MagazineSwap` suit ce que montre chaque vue ; un rechargement interrompu rend l'arme intacte dans l'image.

### Le rechargement du Big D, une arme qui s'ouvre

Le derringer du Golden Gun : `Tools/Blender/golden_gun.py`, `animations/rechargement_golden_gun.py`, `animations/tenue_golden_gun.py` (à partir de **Duel Arena > Animations > Relever les mains des robots**). Dans Unity : `BreakActionGesture`, `BreakActionView`. ⚠️ Une distance à une pièce, calculée par érosion d'une grille, ne compte pas le bord de la grille comme la pièce. 🚨 **Un clip humanoïde ne garde des doigts que leurs plis** : une pose de doigt calculée librement dans Blender arrive ailleurs dans le jeu (**Duel Arena > Animations > Relever les plis des doigts du Y Bot**).

### Les sons

Sources et licences : `Docs/Sons-Provenance.md` (les morceaux de Pixabay : **revérifier leurs pages avant la sortie** ; dette n° 97) ; préparation : `Tools/Sons/`. Choisis sans écoute possible ici : **à juger à l'oreille**. Tampon audio de 512 échantillons (le journal dit ce que le pilote accorde). `GameSounds.asset`, `OneShotAudio`, `WeaponAudio` (le rechargement **se tait s'il est interrompu**), `PlayerFeedbackAudio`, `MatchAudio`, `MusicPlayer` (jamais pendant la manche, GDD § 8). Pas fait : l'annonceur, à trancher avec l'utilisateur.

## Tests

- Assemblies : `DuelArena.Input`, `DuelArena.Runtime`, `DuelArena.Tests.EditMode`, `DuelArena.Tests.PlayMode` ; la surface de test du jeu est en `#if UNITY_EDITOR || UNITY_INCLUDE_TESTS`.
- **663 tests EditMode et 186 PlayMode au 2026-11-04, tous verts.** EditMode : `run_tests` (mode editor) ; un résultat trop long arrive dans un fichier JSON. PlayMode : `run_tests` en `async_tests`, puis `test_status` (le mode synchrone coupe la requête au rechargement de domaine). Son filtre est une sous-chaîne : une liste précise passe par `TestRunnerApi`.
- ⚠️ **Pas `unity command run_tests`** : il rend la main au bout de 30 s pendant que la série continue dans l'Editor ; une série lancée ensuite s'y superpose.
- Ce qu'ils couvrent : la logique pure ; le **déterminisme de `Move()`** sans session Netcode ; l'animation avec le vrai Animator, sur chaque châssis ; tir, épave et tribune avec de vrais colliders ; le câblage des données et des UXML, les scènes construites, de vrais rendus. Pas couvert : l'accord client/serveur sous vraie latence (mesuré à deux instances).
- Méthode : un test n'existe qu'une fois vu échouer (mutation), et vu passer d'abord sur le code juste ; **une garde anti-vacuité** par test ; **un défaut transitoire s'échantillonne PENDANT**, pas après ; attendre en **temps**, pas en frames ; **mesurer un geste rapide au pas de temps figé** (`Time.captureDeltaTime`, remis à zéro au `TearDown`) ; **le jeu se mesure avec et sans viseur** ; **une émulation se compare une fois au jeu** ; **frapper un corps physique au repos**.
- ⚠️ **Le lanceur PlayMode répond parfois « 0 test, réussi »**, ou rend le rapport de la passe précédente : `EditorUtility.RequestScriptReload()` par eval, sinon une recompilation complète, puis relancer.
- ⚠️ La garde de repos de `RobotCorpseTests.UnTirSurLEpave_LaSecoue_SansLaFaireVoler` manque parfois : relancé seul, il passe.
- ⚠️ Une asmdef de test en `includePlatforms: ["Editor"]` est classée EditMode : les tests PlayMode s'excluent des builds par `defineConstraints: ["UNITY_INCLUDE_TESTS"]`. Après avoir créé de la géométrie : `Physics.SyncTransforms()`.

## Diagnostic et banc à deux instances

- **Simulateur réseau** : `NetworkSimulator` sur le `NetworkManager`, **désactivé par défaut** (le `DebugSimulator` d'`UnityTransport` ne fait RIEN en Netcode 2.13.2). L'activer sur l'hôte (eval, avant de le lancer), puis `ChangeConnectionPreset(NetworkSimulatorPreset.Create(nom, "", délai_ms, gigue_ms, 0, pertes_%))`, **toujours avec des pertes** (2 %) ; lire `[DIAG-ANIM]` chez l'hôte. Références : à 40 ms, 8 ms de gigue et 2 % de pertes, le robot du client immobile sur 3 à 8 % des images de l'hôte à cadence égale, bonds de 6 à 10 cm, réserve de 15 à 27 ms ; à 100 ms, 30 ms et 3 %, 4 à 7 %, 8 à 12 cm, réserve de 35 ms ; dans les deux cas, 0 recalage et 0 image affamée. À deux Editors sur 6 cœurs, des pics de 20 à 40 ms sont leur concurrence, pas le jeu.
- **Le journal, builds comprises** (`NetDiagnostics`) : `persistentDataPath/Diagnostics/<profil>.log`, coupé par `-nodiag` ; `cpu=` (moyenne/pire), `rendu=`, `gpu=` (`FrameTimingManager`) disent ce qui limite les images. **Une build se mesure seule** : `-banc-stand` ou `-banc-arene` y entrent sans la piloter. ⚠️ Chez le client, le RTT du transport se fige : se fier à `rtt_inputs`. Références : en direct, 8 ms d'aller-retour ; par les relais, 300 à 420 ms.
- **Journaux** (Editor seulement) : `[DIAG-CLIENT]` (sain : 0 % de resynchronisation), `[DIAG-SERVEUR]`, `[DIAG-ANIM]`… ; `OneShotAudio.DebugJournal`, à lire par `eval`. En build, un refus du serveur fait une ligne toutes les 2 s au plus (`ServerNoteRefusal`), et le récit de partie reste dans l'Editor (`BenchLog`, `[Conditional]`).
- **Banc de test** : `NetworkBootstrapUI.SetCloneTestBench` écrit `%LOCALAPPDATA%/DuelArena/banc-de-test.txt`, relu par le clone à chaque entrée en Play Mode. Attendre qu'il ait rechargé le code, démarrer le Host, lire son journal (`unity command console --project-path <clone> --format json`). **L'éteindre après.** Les statics du clone se règlent par `unity command eval --project-path <clone>`. Deuxième joueur virtuel : `Library/VP/mppm5510903a`.
- ⚠️ `autopilot` écrase la gâchette : pour qu'un clone tire, `autofire` sans `autopilot`, et faire bouger l'hôte.
- ⚠️ **Le clone garde la scène qu'il avait ouverte** : sans `NetworkManager`, il ne se connecte pas : `unity command open_scene --project-path <clone> --path "Assets/_ProjectScenes/MainMenu.unity"` avant le Play Mode.
- ⚠️ Les joueurs virtuels n'essaient de se connecter que pendant la minute qui suit l'entrée en Play Mode ; ensuite par eval (`SessionFlow.PrepareConnectionData`, `StartClient`).
- ⚠️ Un eval envoyé au joueur virtuel met parfois plusieurs secondes à partir : lancer d'abord chez lui un guetteur, puis la séquence chez l'hôte.
- ⚠️ La suite d'un `await Awaitable.NextFrameAsync()` tourne avant le `LateUpdate` de l'image : attendre aussi `Awaitable.EndOfFrameAsync()`.
- **Mener un scénario** : une partie se lance par eval avec `SessionFlow.ActiveRules = GameModeCatalog.RulesFor(GameModeId.…)`. Une élimination : `Health.ApplyDamage(500, racine du tueur, cause)` sur le serveur (sans kill cam). Placer : `ServerTeleportTo` (réflexion). ⚠️ `TaserController.AutopilotStrike` ne frappe qu'à 0,9 m de l'œil au buste adverse, ce que les capsules de deux robots debout empêchent : `BeginStrike` (réflexion), à 1,2 m. Bancs : `RoundManager.DebugSoloDuel`, `DebugNoDuel`, `DebugTagTeamUneven`. Capturer par `ScreenCapture.CaptureScreenshot(<chemin absolu>)` : un `save_path` relatif atterrit dans `Assets/`.
- **Méthode** : une configuration de rendu par Play Mode (couper un réglage en pleine partie fausse les chiffres du GPU) ; apparier par numéro (de balle, d'input), jamais dans l'ordre d'arrivée ; une mesure doit distinguer « ça marche » de « ça ne s'est pas produit » ; une capture allonge l'image qui suit de 100 ms : une machine capture, l'autre mesure.

## Pièges déjà rencontrés (ne pas les re-découvrir)

**Réseau et simulation**
- La disparition d'un objet réseau arrive avant les variables réseau changées dans la même image : attendre que l'état publié rattrape.
- À la sortie d'une partie, le `NetworkManager` peut disparaître avant un objet encore `IsSpawned`.
- 🚨 **Une mesure réseau sans pertes ne prouve rien.**
- Un message non fiable peut en doubler d'autres : tout ce qui en reçoit se protège du désordre.
- 🚨 **Les relais d'Unity sont chez Google Cloud, et la route de l'utilisateur vers Google est engorgée** : **les performances et le code se jugent en réseau local** (dette n° 70).
- Tout script qui lit clavier/souris a une garde `IsOwner`.
- **Jamais d'instance du prefab joueur dans une scène réseau**, même désactivée : Netcode réactive de force les `NetworkObject` de scène côté client.
- Un `CharacterController` se désactive pour téléporter ; sa capsule commence à `skinWidth` au-dessus de la racine.
- `Destroy()` est différé : un objet visuel créé en jeu doit naître sans collider actif.
- Dans `Fire()`, le recul s'applique APRÈS avoir déterminé où part le tir.
- `Transform.hasChanged` ne se lève pas quand on réécrit la même valeur.

**Unity, scène et Editor**
- L'image qui charge une scène dure souvent plus d'une demi-seconde : un fondu se compte image par image (`GameHud.StepArrival`).
- Netcode finit de s'arrêter après `LateUpdate` : lire `ShutdownInProgress`.
- `FindAnyObjectByType<AudioListener>()` rend un écouteur éteint : l'oreille est `OneShotAudio.ActiveListener`.
- Chez un client, `NetworkManager.ServerTime` retarde un peu sur le serveur : borner un temps restant.
- Une clé de rafraîchissement qui énumère des champs oublie ceux qui s'ajoutent (`MatchRules.Fingerprint`).
- 🚨 **Lancer des tests EditMode quand une scène ouverte est modifiée** ouvre une fenêtre modale qui bloque l'Editor et tous les outils MCP : enregistrer la scène avant `run_tests`.
- Capturer les images du menu depuis la scène du menu seule : sinon les deux salles se superposent.
- `LightingData.asset` est binaire malgré son extension : il a sa règle LFS.
- Masquer un temps, c'est ne rallumer que ce qu'on a éteint.
- Changer la valeur par défaut d'un champ nouveau ne change rien au prefab : l'y écrire.
- Un `ParticleSystem` en espace monde sur un `Rigidbody` lancé part une image en retard (`emitterVelocityMode`).
- Un maillage importé sans Read/Write ne se lit pas en Play Mode : le relever avant (`IPrebuildSetup`).
- 🚨 **Un champ privé d'un type sérialisable n'est jamais null après une recompilation** : marquer ces caches `[System.NonSerialized]`.
- Le rechargement de domaine et de scène est désactivé à l'entrée en Play Mode : les `static` survivent d'une session à l'autre.
- Un enum sérialisé l'est par sa valeur entière : ajouter les nouvelles valeurs à la fin.
- USS : `:last-child` interdits ; `-unity-font-definition: none` invalide. `UnityTransport.SetConnectionData` impose une référence à `Unity.Networking.Transport` (CS0012).
- Un `TextMesh` avec le matériau de sa police se voit à travers les murs : `DuelArena/TexteMonde`.
- Un HUD OnGUI se dessine par-dessus les documents UI Toolkit.
- Un `Toggle` sans libellé ne montre pas sa case : `.da-check`.
- Une couleur à demi transparente paraît bien plus forte qu'on ne l'a réglée : régler une opacité sur une capture.
- Une règle `.classe:disabled` l'emporte sur une seule classe : un état qui doit primer répète la classe.
- 🚨 **Un contrôleur se branche par arbre, pas une fois pour toutes** : l'`UIDocument` recrée son arbre quand il se réactive (`wiredScreen`).
- Painter2D : le dégradé vertical se dessine plein ; il trace dans `layout`, pas dans `contentRect`.
- Le texte propre d'un `Button` n'est plus mesuré dès qu'il a des enfants.
- L'interface lit une `RenderTexture` en alpha droit, pas prémultiplié.
- 🚨 **Plusieurs `Camera.Render` dans une même image de l'Editor : un `SkinnedMeshRenderer` garde la peau de son premier rendu** : `forceMatrixRecalculationPerRender = true`.
- Shader cherché par son nom (`Shader.Find`) : absent de la build s'il n'est référencé par aucun matériau. `GetShaderMessages` ne voit pas les variantes pas encore compilées : lire `Shader error` dans `Logs/Editor.log`.
- 🚨 **Une fonction de renderer d'URP (`ScriptableRendererFeature`) ne crée rien d'Unity dans un champ statique ou un initialiseur** : plus aucune caméra de l'Editor ne dessine. La créer dans `Create()`. Ajoutée par script, elle naît éteinte : `SetActive(true)`.
- 🚨 **Un objet réseau de scène posé par script garde l'identifiant de son préfab** (`GlobalObjectIdHash`) tant que la scène n'est pas enregistrée : enregistrer, rouvrir, revalider, réenregistrer.
- Un champ sérialisé réservé à un outil : `#pragma warning disable 0414`, **jamais `#if UNITY_EDITOR`**, qui changerait la disposition sérialisée.
- **PlayerPrefs sous Windows relit le registre à chaque appel** : ce qui est propre à une instance, ou lu à chaque image, se garde en mémoire.
- **Ce qui tourne à chaque image n'alloue rien** : ni lambda qui capture, ni interpolation de chaîne.

**Les armes de Blender**
- Export FBX : `exporter_armes.vers_axes_unity` (« Apply Transform » tourne deux fois les objets imbriqués).
- Un nom de pièce peut masquer un repère (`RENOMMAGES_PIECES`).
- Le twist d'un `CharacterJoint` tourne à l'envers de son axe.
- Deux graines voisines donnent presque le même premier tirage : hacher la graine (`CorpseFall.Dice`).
- `Mathf.SmoothStep` n'est pas le `smoothstep` des shaders.
- Une lumière seulement ajoutée disparaît sur un fond clair : couleur prémultipliée.
- `Image.save()` de Blender à un chemin relatif n'enregistre rien, sans erreur.
- L'origine d'une pièce exportée est celle de l'arme : sa place se mesure au milieu de son maillage.
- Réimporter les armes (`WeaponImporter.ImportAll`) réécrit aussi `GantChoc.prefab` : s'il n'a pas changé de sens, le rendre par Git.
- HLSL : `point` est un mot réservé. Dans les propriétés d'un shader, `[Header(...)]` n'accepte que de l'ASCII.
- Les tests EditMode rechargent les assets : une mutation se pose sur le fichier, avec une copie pour le rétablir.
- `GetComponentInChildren<Animator>()` sur un joueur trouve d'abord la copie de la vue subjective : `transform.Find("Model")`.
- Le joueur virtuel a son propre cache de shaders : les images par seconde se mesurent dans l'Editor principal.
- 🚨 **Jamais `Camera.Render`, ni une caméra qui rend dans une texture, pour « préparer » des shaders dans l'Editor** : ces rendus compilent d'un bloc les variantes qui manquent, sans rendre la main.
- Le joueur virtuel de MPPM ne se relance pas seul : `MultiplayerPlaymode.PlayerTwo.Activate` (par réflexion).
- « Scene file not found: '' » chez les joueurs virtuels, après des tests EditMode : bruit de MPPM.

**Animation et import**
- **« Bake Into Pose » fait l'inverse de ce qu'on croit** : il garde le déplacement dans la pose. Seul le vrai Animator au runtime tranche.
- `SkinnedMeshRenderer.bounds` n'est pas la taille du personnage : mesurer par `BakeMesh` ou par les os.
- Un piège corrigé à un endroit dort ailleurs : chercher toutes ses occurrences.
- 🚨 **`Animator.CrossFade` compte son fondu en part de l'état qu'on quitte** : sur un état à vitesse nulle, il ne finit jamais. `CrossFadeInFixedTime`.
- Un geste authoré en garde (hanches tournées) ne se pose pas sur un haut du corps aux hanches de face.
- Le modèle de test porte le gant, qui replie la main gauche : l'ôter pour mesurer les doigts.

## Structure du projet

- **Prefab joueur unique** : `Assets/_ProjectArena/PlayerPrefab/Player.prefab`, référencé par GUID dans `NetworkManager.PlayerPrefab` et `Assets/DefaultNetworkPrefabs.asset`. Enfants : `CameraPivot`, `Weapon`, `Hitbox`, `Model` ; à la racine, le reste.
- Code : `Assets/_ProjectScripts/`. Rendu : `Assets/Settings/` (`PC_RPAsset`, `PC_Renderer`, `PC_Renderer_Bras`). Interface : `Assets/_ProjectUI/`. Entrées : `Assets/_ProjectInput/PlayerControls.inputactions`. Art : `Assets/_ProjectArt/` ; armes, modes, tribune, décor : `Assets/_ProjectArena/`.
- **Layers** : `Player` (3), `Hitbox` (7, ne touche rien dans la matrice des collisions : ses zones ne servent qu'aux requêtes), `Viewmodel` (8), `Preview` (9), `Corpse` (10, ne touche que `Default`).
- Touches : sprint sur Alt, sneak sur Shift (configuration de l'utilisateur).

## Dettes ouvertes (numérotation historique)

**Réseau et triche**
- 🟡 56. L'origine d'un tir n'est vérifiée qu'à 4 m près. Piste : la comparer à l'œil penché connu pour l'input de référence. À faire avant d'ouvrir le jeu à des inconnus.
- 🟡 44. Un client modifié peut raccourcir la sortie de sprint en gonflant la durée de ses inputs, et de même la charge du FX-2 (de 10 % au plus).
- ⚪ 75. Le hasard du tir à la hanche se prédit (la graine est connue du client).
- ⚪ 61. Une adresse fausse (réseau local) n'échoue qu'au bout de 60 s.
- ⚪ 68. Deux salons (`LobbyController`, `SalonPanel`) font la même chose.
- 🔴 70. La partie en ligne dépend de la route de chaque joueur vers Google Cloud. À trancher selon les journaux des amis : un hôte à la bonne route, sinon connexion directe (port UDP 7777), et à la sortie sur Steam, le relais de Steam.
- 🟡 69. Avant la sortie, les services en ligne : quotas gratuits, facturation, politique de confidentialité.

**Jeu et lisibilité**
- ⚪ 66. Les gestes du spectateur ont leurs limites.
- ⚪ 64. `/VictorySpot` est placé à la main : à refaire pour chaque arène.
- ⚪ 65. Le robot qui vole en pièces a ses limites. Le gore reste à juger par l'utilisateur.
- 🟡 24. Allongé, le corps dépasse du `CharacterController` : il manque un test « le corps tient-il ici ? » dans `Move()`.
- 🟡 41. Allongé, pas de vraie reptation avant : elle se calculerait dans Blender comme celle de côté.
- 🟡 34. Allongé, le taser n'a pas de geste.
- 🟡 45. Le raygun recharge et tire avec les gestes du fusil.
- ⚪ 82. Le rechargement à chargeur n'existe qu'aux armes à chargeur. Le PA-05 est tenu sous le menton ; `Armes.blend` n'a pas été reconstruit depuis la séparation des pièces.
- 🟡 95. Les mains des robots entrent dans les armes de poing : la tenue du Big D (`tenue_golden_gun.py`) servirait aux autres.
- 🟡 11. Son : pas de portée par allure (une seule `AudioSource` de 20 m pour tous les pas, GDD § 8).

**Visuel et technique**
- ⚪ 96. Se hisser sur un obstacle a ses limites.
- ⚪ 60. Le shader des traçants n'est pas dans la build : ils y passent sur `Sprites/Default`.
- ⚪ 31. Les clips de transition finissent genou à terre au lieu du squat.
- ⚪ 39. Viser en se déplaçant à gauche tord le buste de près de 100° : à juger.
- ⚪ 42. Glissades résiduelles.
- ⚪ 48. Armes et gant sans LOD.
- ⚪ 72. Le compteur des armes laser est figé.
- ⚪ 79. Les sons des huit nouvelles armes sont empruntés.
- ⚪ 73. Le Raygun d'avant la refonte (`Weapon/Raygun.prefab`) ne sert plus : gardé à la demande de l'utilisateur (2026-11-04).
- 🟡 90. Le Tournoi n'a été joué qu'à trois : plusieurs matchs à la fois ne sont vérifiés que par le code et les tests.
- ⚪ 91. Le direct du Tournoi a ses limites ; son coût n'est pas mesuré.
- ⚪ 92. Le site du Tournoi est posé pour l'arène de test.
- ⚪ 94. La kill cam a ses limites : pas jouée à deux instances dans un mode à réapparitions, au Tag Team ni au Tournoi ; pas essayée sous latence.
- ⚪ 93. La pièce de pile ou face n'est qu'un texte.
- 🟡 89. Le menu reste muet après une connexion échouée ou annulée : `ArenaFallbackCamera` ne rallume pas l'écouteur.
- ⚪ 88. Le Tag Team n'a pas été joué à deux contre deux.
- ⚪ 87. Les emplacements de la Zone sont ceux de l'arène de test.
- ⚪ 86. Le module mémoire est provisoire.
- ⚪ 85. Le gant seul du Gun Game a ses limites.
- ⚪ 84. Les sons du Big D sont empruntés.
- ⚪ 76. Ni discussion écrite dans le salon, ni chat vocal : plus tard s'ils demandent un service payant.
- ⚪ 80. Le jeu porte les trois rendus : une fois le rendu choisi, retirer le réglage et ce qui ne sert plus.
- ⚪ 81. L'habillage est écrit pour MultiTestScene (`ArenaMarkings`, `ArenaStyleTrial`).
- ⚪ 62. La build s'appelle « FPS Claude », éditée par « DefaultCompany ».
- ⚪ 63. La build avertit que 103 maillages (306 références, les trois arènes) n'ont pas de collision pré-calculée : à régler avant une future version d'Unity, sans enjeu de performance.
- 🟡 97. La musique du menu (PunchDeck) n'a ni source ni licence écrites : à régler avant la sortie (`Docs/Sons-Provenance.md`). `Menu_Welc0me`, de licence connue, reste en attendant.
- 🟡 98. IL2CPP avant la sortie : le jeu tourne en Mono, et le JIT s'ajoute aux gels de premier emploi (305 ms au premier FX-2, 98 ms à la première kill cam, relevés avant ce chantier). Il faut le module Windows Build Support (IL2CPP) et les outils C++ de Microsoft (plusieurs Go), et 10 à 20 min de plus par build : à proposer à l'utilisateur, le journal `cpu=` à l'appui.
- ⚪ 99. Rien ne préchauffe les shaders : une `ShaderVariantCollection` ou une `GraphicsStateCollection`, dont la liste se dresse en build Development (`logWhenShaderIsCompiled`), sous le noir de l'arrivée.
- ⚪ 101. La première image où un robot entre dans le champ, il n'a ni main sur l'arme ni pieds calés (`PlayerAnimator.Rendered`, qui lit l'image d'avant et retient 0,15 s pour le direct du Tournoi).
- 🟡 49. Deux `NullReferenceException` de Netcode à la sortie du Play Mode (`NetworkSceneManager.Dispose`) : pas notre code, sans effet en jeu.

## Ordre de travail

Restent, dans l'ordre (revu avec l'utilisateur le 2026-10-25, puis le 2026-11-02) :

1. **Les retours de chaque essai** de l'utilisateur : les corriger d'abord.
2. **La refonte des armes**, priorité de l'utilisateur (GDD § 6). Restent : les sons propres aux huit nouvelles armes (n° 79), les huit lentilles du FX-3 rangées comme son éventail (`laser_dispersion.py`), les power-ups, les nouvelles fiches d'armes ; **l'interface** de l'armurerie et de la préparation (trois favorites au plus). Attend la confirmation de l'utilisateur (GDD § 17) : une arme surchauffée se vide d'elle-même (ce que fait le code).
3. **Avant les tests poussés sur le blocking** : un essai à deux par le code, avec le journal de diagnostic de chaque joueur (n° 70).
4. **Les modes de jeu** (GDD § 11) : reste l'option Dons d'armes, avec trois ou quatre armes insolites. La voix de l'IA attend.
5. **Ensuite, ce que le playtest dira**, puis : les blessures du robot touché (GDD § 6 ; l'Arène dans le noir en dépend) ; la première vraie arène (bible § 13) ; le choix du rendu ; le rechargement à chargeur des autres familles (n° 82) ; les votes des spectateurs ; les power-ups d'arène (GDD § 10) ; la musique et les sons de l'interface ; le chat (n° 76) ; l'anglais.
6. **La partie en ligne** : l'essai sur deux PC et deux réseaux, le retour au salon après un match (n° 68). Le classé, le serveur dédié, l'anti-triche client et le matchmaking par région viennent après la sortie.

Ce que le GDD et la bible imposent à l'architecture :
- **Ce qu'on lance** est simulé par le serveur seul : le client demande, le serveur borne et simule. Les grenades suivront le même chemin (GDD § 6).
- **Les débris de mort sont cosmétiques et naissent inoffensifs** : un layer qui ne touche ni les `CharacterController`, ni `worldMask`, ni `obstacleMask`.
- **Les formes cosmétiques tiennent dans les capsules de `PlayerHitbox`** pour chaque posture et chaque lean : la surface touchable ne dépend jamais du modèle (GDD § 12).
- **Aucun cosmétique ne touche au son.**
- **Les effets des accessoires et des power-ups sont des données**, lues par les mêmes fonctions pures chez le joueur et sur le serveur. Un modificateur ne fait que multiplier : un effet qui ajoute demandera une opération de plus.
- **La chaleur et la charge sont de l'état de l'arme, simulé** (catégorie A).
- **Les modes de jeu passent par la boucle de manches** : mode et options sont des réglages publiés (`MatchRules`) ; rien ne doit supposer un seul match par partie. **Les blessures** (GDD § 6) sont de la catégorie C réseautée, mais se voient : chaque machine les pose au même endroit, d'après un point d'impact diffusé par le serveur (à ajouter), jamais d'après son propre rendu.
- **Un châssis est un jeu de pièces de `Model` sur le squelette du Y Bot** : un nouveau robot se mesure d'abord, puis s'ajoute à `RobotChassisBuilder.Robots`, avec son rang à la fin de `ChassisId`.

## À juger en jouant (arbitrages de l'utilisateur)

**Le test qui compte, depuis l'instance client, sous latence** (simulateur actif) : un adversaire qui penche derrière un angle est touchable sur le flanc qu'il expose, et seulement là ; derrière une caisse, il ne prend rien ; on ne se touche jamais soi-même ; aucun `[Serveur] Tir rejeté` en jouant honnêtement.

**Les valeurs provisoires** : vitesses, postures, lean ; chargeur, taser, sprint ; armes, recul, tir à la hanche ; sons et musique ; vue subjective, rechargements ; accessoires, laser ; déroulé du match, mort, écran de fin ; HUD ; stand, Fosse ; spectateurs ; modes et options ; kill cam.
