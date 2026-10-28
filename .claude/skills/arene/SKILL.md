---
name: arene
description: Habiller, éclairer et juger une arène de Duel Arena dans Unity — le décor posé sur le blocking sans toucher au jeu (shader DuelArena/Decor, matériaux par recette, salle souterraine, signalétique, props de Blender), la lumière (projecteurs, pénombre, cuisson), le post-traitement, l'essai des rendus (low poly propre, cel-shading, mêlés) et les captures de contrôle. À utiliser dès qu'il est question de la map, du décor, de l'habillage, du level art, de l'éclairage d'une scène, d'ambiance, de signalétique, de marquages au sol, de matériaux de murs ou de sol, d'une nouvelle arène ou du rendu du décor, même si l'utilisateur ne dit pas « arène ».
---

# Habiller une arène de Duel Arena

Tout se fait par script, dans l'Editor ouvert (outils MCP `unity-editor-mcp`) : **on corrige le script, jamais la scène**. Le premier habillage date du 2026-10-27 (MultiTestScene, « salle de test balistique », « projecteurs, pénombre », choisis par l'utilisateur).

Avant tout : relire la bible (`Docs/Bible-Lore-DA.md`, § 10, § 13, § 14, § 17) et le GDD (§ 10). Les arbitrages de **ressenti et de contenu** (type de salle, ambiance, rendu, textes peints) remontent à l'utilisateur ; poser la question avec `AskUserQuestion`, une recommandation en premier.

## Les règles qui ne bougent pas

- **Le blocking est la vérité du jeu.** Ses blocs ProBuilder gardent leurs colliders (MeshCollider, layer `Default`) ; l'habillage n'éteint que leur rendu et pose à côté un bloc de la même enveloppe. Un layout validé par les tests ne change pas sans l'accord de l'utilisateur.
- **Aucune surface ne laisse passer les balles** (bible § 13) : dans l'espace de jeu, rien de visible sans collider, rien qui dépasse d'un collider de plus de quelques centimètres. Ce qui n'est pas plat contre un mur ou le sol va hors de portée (la salle, le plafond, la tribune) ou remplace l'apparence d'un bloc en tenant exactement dans sa boîte (les caisses de Blender).
- **Rien du décor ne change le jeu** : pas de collider ajouté dans l'arène, les props n'ont pas de collider (l'import le garantit). Seuls exceptions voulues : le sol (une dalle, `BoxCollider` dont le dessus est à y = 0), les murs et le plafond de la salle (rien de lancé n'en sort).
- **La palette** (bible § 10) : décor sombre et terne (noir, gris acier, béton, blanc cassé, presque pas de jaune), jamais une couleur de robot (orange, bleu, magenta, vert, cyan, violet, jaune). Le rouge est l'énergie et le danger (voyants « TIR EN COURS »).
- **Le sol de l'arène reste éclairé de façon égale** : personne ne campe dans le noir. La pénombre est pour la salle et la tribune.
- **Lisibilité d'abord** (bible § 17) : un motif qui scintille de loin s'efface (`DecorNettete`), un effet lumineux ne masque pas un adversaire, un faisceau ne descend pas jusqu'aux robots (ceux des projecteurs s'éteignent à `ArenaLighting.BeamBottom`, 6,4 m : `ArenaDecorTests` le vérifie).

## Les outils (menu Duel Arena > Arène)

| Menu | Code | Ce qu'il fait |
|---|---|---|
| Habiller l'arène | `ArenaDecorBuilder.Build()` | refait tout ce qu'il a construit sur la scène active : blocs habillés, sol, salle, tribune, signalétique, lumière. 2 s. |
| Cuire la lumière | `ArenaDecorBuilder.BakeFromMenu()` | la lumière indirecte (lightmapper GPU, en tâche de fond), puis la sonde de reflets. Quelques minutes. |
| Importer les props de Blender | `DecorImporter.ImportAll()` | préfabs et matériaux des props exportés (`Assets/_ProjectArena/Decor/Props/<CODE>/`). |
| Montrer le blocking / le décor | | rallume le gris pour retoucher le layout, puis revient au décor. |
| (Essais) Captures des trois rendus de l'arène | `ArenaStyleTrial.Capture(dossier)` | les mêmes vues, robots compris, en low poly propre, cel-shading et mêlés. |

Code : `Assets/_ProjectScripts/Editor/Decor/` (assembly `DuelArena.Editor`, qui référence ProBuilder et URP). Les maillages générés vont dans `<scène>_Decor.asset` à côté de la scène (en LFS : `*_Decor.asset`).

- `ArenaDecorBuilder` : l'ordre de la construction, le **rôle de chaque bloc** (`Classify`, lu dans son nom et celui de ses parents : mur d'enceinte, tunnel, barricade, acier peint, plateforme, tour, caisse, escalier), la salle (`HallGeometry`, lue sur la tribune : le garde-corps, le bord extérieur de la passerelle, le pied des murs), la tribune.
- `DecorMeshes` : boîte chanfreinée (couleur de sommet rouge sur les chanfreins = `bord`), boîte orientée, poutre en I, prisme sur un polygone, décalage de polygone, banque de maillages. Chaque maillage reçoit la normale lissée dans ses tangentes (le contour du cel-shading) et son dépliage de lightmap.
- `DecorMaterials` : **les recettes des matériaux** (`Recette` : couleur, motif, panneau, trous de banche, soubassement et liseré, hachures, crasse, impacts, éclats, émission). Un matériau se règle là, jamais dans l'inspecteur : le prochain habillage l'écraserait.
- `ArenaMarkings` : lignes de tir peintes, textes au sol et aux murs (les maillages de `MQ1`), voyants `GY1` et leur lueur rouge.
- `ArenaLighting` : lumière, ambiance, brume, sondes, sonde de reflets, volume de post-traitement (`Assets/_ProjectArena/Decor/Arene_PostTraitement.asset`), réglages de cuisson (`<scène>_Lumiere.lighting`).
- `DecorPreview` : rend des vues de contrôle (caméra temporaire, post-traitement compris) dans un dossier du scratchpad ; `ArenaViews` en donne six. **On ne juge le décor que sur l'image** (outil Read sur les PNG).

## Le shader DuelArena/Decor (`Assets/_ProjectArt/Shaders/Decor.shader`, `Decor.hlsl`)

- `_Espace` 0, **l'architecture** : aucun dépliage, les motifs se lisent sur la position du monde tournée dans les axes de l'objet (un mur en biais garde ses panneaux droits). `_Motif` : 1 béton banché (panneaux, joints, trous de banche), 2 bardage riveté, 3 dalle sciée (taches d'huile), 4 tôle striée (dessus seulement). Puis ce que la hauteur du monde décide : soubassement peint et liseré, hachures, crasse au pied, coulures, poussière des dessus ; impacts de balles ; éclats des arêtes (`bord`).
- `_Espace` 1, **une pièce de Blender** : l'usure des armes (`UsureBlender.hlsl`, la fonction `ArmeUsure`, partagée avec `ArmeUsee`), aux paramètres de la fiche de l'export (`WeaponImporter.ApplyParams`).
- `_Decoupe` > 0 : **un marquage peint** (la peinture découpée par plaques, `_UsureMarquage`) ; ses passes d'ombre et de profondeur sont coupées par matériau.
- `_DecorStyle` (global, `Shader.SetGlobalFloat`) : 0 low poly propre (PBR d'URP), 1 cel-shading (seule la lumière principale posée en paliers d'1,25 diaphragme, son ombre tranchée ; les lumières d'appoint, la lumière cuite et l'occlusion d'écran ajoutées en douceur ; sans usure, contour), 2 mêlés (paliers et usure). Le contour est une coque retournée (passe `DecorContour`), qu'URP ne dessine pas de lui-même : `DecorOutlineFeature` (une fonction du renderer d'URP) la dessine en cel-shading et en mêlés seulement ; coupée sur les matériaux sans contour. **Le rendu du jeu n'est pas encore choisi** (bible § 18) : le joueur le règle dans les Options (Vidéo > Rendu, `RenderStyle`), les deux mêlés par défaut pendant la phase de test ; dans l'Editor hors Play Mode, le décor est en 0.
- Passes d'URP 17.6 : Forward+ (`_CLUSTER_LIGHT_LOOP` : la boucle des lumières se copie de `UniversalFragmentPBR`), ombres, profondeur, normales (SSAO), **Meta** (la cuisson lit couleur et lueur).

## La lumière (« projecteurs, pénombre »)

- **Calques de rendu** : 1 (`ArenaLayerMask`) l'arène et les robots, 2 (`HallLayerMask`) la salle et la tribune. La rampe générale (directionnelle, presque verticale, froide, ombres) n'éclaire que le 1 : la salle reste sombre, les spectateurs (des robots, calque 1) restent visibles.
- 🚨 **URP lit les calques d'une lumière dans `UniversalAdditionalLightData.renderingLayers`**, pas dans `Light.renderingLayerMask` (qu'il réécrit pour les ombres) : `ArenaLighting.SetLayers` règle les deux. Sans ça, une lumière du seul calque 2 n'éclairait rien.
- Projecteurs sous les rampes (spots sans ombre de 84°, des flaques qui se recouvrent), lampes tièdes de la tribune, lampes rasantes des murs de la salle, « reflets du sol » (faibles points du calque 2, le temps que la lumière soit cuite).
- **Les faisceaux** (`ArenaLighting.AddBeams`, shader `DuelArena/FaisceauProjecteur`) : un cône ajouté sous chaque verre des rampes (la pièce « Verres » de `PR1`, ses sommets groupés le long de la rampe), au-dessus de l'arène seulement, jusqu'à 6,4 m ; normales radiales (le cœur plus dense que les bords), `uv.y` de 0 au verre à 1 au bout, effacés de près et dans la brume. Ni ombre, ni sonde, ni statique (la cuisson les prendrait pour des écrans). **Le liseré de la tribune** : une bande qui luit sous le bord de la passerelle (`Decor_LedTribune`), sans lumière. Les lumières sont **mixtes** (indirecte cuite, directe en temps réel) : couper une lumière en jeu garde son indirecte, le fondu au noir le cache.
- Ambiance Trilight sombre et bleutée, brume exponentielle (0,0065), pas de ciel, reflets d'une sonde cuite (boîte de la salle), 980 sondes de lumière pour les robots (aucune dans un bloc).
- Post-traitement : tonemapping neutre (les peintures des robots gardent leur teinte), contraste +10, halo sur les lampes (seuil 1,25, au-dessus des marquages blancs éclairés ; intensité 0,6), vignettage 0,22. Le volume est dans la scène : le menu et le stand gardent le leur.

## Le stand de tir, la Fosse et la salle de précision (2026-10-28)

La scène `StandDeTir` est construite d'un bout à l'autre par `ShootingRangeBuilder` (Duel Arena > Stand de tir > Construire le stand, puis Cuire la lumière du stand) : pas de blocking à part. Chaque bloc du jeu est un `BoxCollider` sans rendu (`Span`) qui dit ce qu'il devient (`StandDecorBuilder.Look` : béton banché, béton sombre, module d'acier, barricade, acier, plateforme et marche à dessus de tôle striée, garde-corps, caisse ou conteneur de Blender, prop de Blender par son code, mur de salle, plafond, pare-balles, cloison de poste, auvent). `StandDecorBuilder` les habille, ajoute ce qui est hors de portée sous le plafond (poutres, déflecteurs) et les marquages ; `StandLighting` éclaire. Les outils de l'arène servent tels quels (`DecorMaterials`, `DecorMeshes`, `ArenaMarkings`, `ArenaLighting` : `PlaceRig`, `AddSpot`, `AddBeams`, `SetLayers`).

- Un mur de salle (`Look.Mur`) : béton banché jusqu'à 6 m sur le calque de l'arène, le béton de la pénombre au-dessus sur celui de la salle.
- Un prop (`Look.Prop`, l'établi `ET1`, l'étagère `RA1`) prend un bloc à ses dimensions exactes, sa longueur le long du grand côté, son dos contre le mur qui touche le bloc.
- Le tracé de la Fosse se vérifie par le tour à pied (`CourseRoute`, `ShootingCourseTests`) ; l'habillage par `StandDecorTests`.
- La salle de précision : ses blocs sous `Salle de précision (décor)` ; le chariot de la cible et le pupitre sont des objets du jeu (`AimRange`, posés par `BuildAimRoom`), que `StandDecorBuilder.DressAimRange` habille : les pièces du chariot ne sont pas statiques (elles roulent ; les sondes de lumière les éclairent, d'où une grille plus serrée dans la salle), le disque de la cible a les sommets de son collider (un prisme convexe, fait par le constructeur), sa face des uv en mètres que le pavage du matériau ramène sur la texture (`CiblePrecision`, URP Lit) ; les boutons sont des quadrilatères d'Unity (`PupitreBouton`, URP Unlit). **L'écran holographique** (`BuildHologram`) : le shader `DuelArena/Hologramme`, additif (ce qui est noir ne se voit pas : une image s'y prépare noire là où il doit être vide), ni statique ni collider, hors de la cuisson ; sa lueur est une lumière en temps réel.
- **Placer des cibles par le calcul**, pas à l'œil : pour chaque poste de tir, la ligne de visée de l'œil (1,65 m) vers chaque cible, et sa distance à plat aux autres cibles qu'elle croise sous 1,85 m. La première disposition des mannequins « dispersés » faite à l'œil en cachait deux sur quatorze depuis le poste du milieu ; celle du stand vient d'une recherche par recuit (côtés équilibrés, 5 m au moins entre deux mannequins) retouchée à la main, et `ShootingRangeTests` la vérifie sur la scène.
- Les textes de distance de `MQ1` vont de 5 à 60 m, un par mètre : une cible se pose à n'importe quelle distance sans réexporter.
- Le pied d'un bloc posé au sol descend sous le sol **en coordonnées du monde**, avant de recentrer la boîte : réglé en coordonnées locales, il coupait la moitié basse des murs (vu le 2026-10-28).

## Recettes

- **Un nouveau matériau** : une `Recette` dans `DecorMaterials`, un rôle dans `Classify` (ou une pièce de la salle), puis Habiller l'arène et des captures.
- **Un nouveau prop** : voir le skill `blender` (« Le décor ») ; exporter, Importer les props de Blender, le poser dans le builder (`DecorImporter.Prefab(code)`), calque de rendu et ombres réglés à la pose.
- **Un nouveau texte peint** : l'ajouter à `TEXTES` de `Tools/Blender/decor/marquages.py`, réexporter `MQ1`, le poser par `FloorText` / `WallText` (hauteur de capitale en mètres). Les textes sont des propositions : l'utilisateur les relit (humour sec, jamais enfantin).
- **Une nouvelle arène** : construire sa tribune (`Tribune.EditorBuildWalkway`), nommer ses blocs pour `Classify`, puis Habiller l'arène ; les positions écrites en dur (tunnel, murs d'enceinte, voyants, textes) sont celles de MultiTestScene : à reprendre.

## Vérifier

1. Des captures (`DecorPreview.Capture`) à hauteur d'œil (1,65 m), de la tribune, d'en haut ; les lire. Comparer avant et après.
2. Les colliders du blocking n'ont pas bougé (`git diff` de la scène : seuls `m_Enabled` des MeshRenderer changent sur les blocs).
3. En jeu, à deux instances si le changement touche le réseau (jamais pour du décor seul) ; sinon un Play Mode solo : se promener, franchir les barricades, monter aux tours, tirer dans un mur (l'impact s'y arrête).
4. Les performances, en Play Mode (un hôte en réseau local : `MainMenuController.OnHostClicked` par réflexion), cible 60 images/s (GDD § 16). **Comparer à vue égale** : la même image avec le décor, puis avec le blocking gris (rallumer le soleil, le terrain et le rendu des blocs le temps d'une mesure), par une fonction `async void` qui compte 200 images et écrit le résultat par `Debug.Log`. Ne pas juger les premières secondes : la compilation des variantes du shader y fait tomber l'Editor vers 30 images/s. Le 2026-10-27, à égalité avec le blocking (12,3 contre 12,2 ms en 1080p dans l'Editor).
   - Ce qui coûtait (7 ms) : **la passe du contour sur chaque objet**, même vide (elle porte maintenant sa propre étiquette, `DecorContour`, dessinée par `DecorOutlineFeature` seulement hors du low poly propre), et **le bruit procédural de loin** (`DecorProche` : le détail fin s'arrête à 26 m ; moins d'octaves partout). En HLSL, un ternaire calcule ses deux branches : un `if` pour sauter un bruit.
   - Les lumières sans ombre ne coûtent presque rien en Forward+ (53 lumières d'appoint : 0,6 ms), les ombres de la rampe générale 1,4 ms.
   - Les faisceaux : 0,01 à 0,32 ms par image en 1080p selon la vue. Le rendu mêlé (décor et contour) : 1,1 à 2,1 ms de plus que le low poly propre. Mesure d'un seul élément sans Play Mode : une caméra temporaire rendue 50 fois à vue égale (`RenderPipeline.SubmitRenderRequest`, puis `ReadPixels` d'un pixel pour attendre le GPU), avec et sans lui, trois fois en alternance.
5. Recapturer les images du menu (Duel Arena > Menu > Capturer les images du menu) quand l'arène change.

## Pièges rencontrés

- Un motif fin (rivets, tôle striée, impacts) scintille de loin : `DecorNettete` l'efface vers sa moyenne sous trois pixels.
- La tôle striée sur le dessous d'une dalle ou un flanc vertical se lit comme du bruit : seulement sur les dessus.
- Corriger un script par un heredoc Bash casse sur les apostrophes du français : écrire le correctif dans un fichier Python du scratchpad (`assert s.count(a) == 1`), puis `python -I`.
- `ProBuilderShape` est interne : lire sa forme par `SerializedObject` (`m_Shape`), pas par son type.
- Un appel MCP qui enchaîne l'habillage et des rendus peut perdre sa connexion : faire l'un, puis l'autre.
- 🚨 **Enregistrer la scène avant `run_tests`** : avec une scène modifiée, le lanceur des tests EditMode ouvre « Scene(s) Have Been Modified », une fenêtre modale qui bloque l'Editor et les outils MCP jusqu'à ce que l'utilisateur clique (vécu le 2026-10-27). Habiller l'arène laisse la scène modifiée.
- Un habillage refait détruit et recrée tous les objets statiques : la lumière cuite ne vaut plus rien, **recuire après chaque habillage**, puis enregistrer la scène (sans toucher aux réglages des autres sessions, comme la durée de préparation du `RoundManager`).
- `LightingData.asset` est binaire malgré son extension : sa règle LFS est dans `.gitattributes` (sans elle, il serait corrompu par la normalisation des fins de ligne).
- Le test `ArenaDecorTests` a trouvé dès son premier passage un bloc du blocking qui flotte de 2,5 cm (la barricade de 1,25 m) : l'habillage montrait un solide là où les balles passent. Un bloc qui ne touche pas le sol garde son vide.
- Plusieurs sessions pilotent le même Editor : prévenir (`ListAgents`, `SendMessage`) avant une recompilation, un Play Mode, des tests, et ne pas enregistrer une scène qu'une autre session modifie.
- 🚨 **En cel-shading, une lumière qui a du bruit fait des bords dentelés** : posée en paliers, la moindre variation au bord d'un palier le fait basculer. La lumière cuite et l'occlusion d'écran s'ajoutent donc sans paliers, et l'ombre portée est tranchée avant (`smoothstep(0,3 ; 0,7)`) : douce, l'acné et le crénelage de la carte d'ombres au loin dessinaient des dents de scie (vu le 2026-10-27). Pour trouver la lumière en cause : la même vue, une source à la fois coupée. Et **la somme de lumières qui se recouvrent** (les flaques des projecteurs, les halos des lampes), posée en paliers, dessine ses courbes de niveau : des vagues sur le sol et des anneaux sur les murs, « pas naturel » pour l'utilisateur. Seule la lumière principale tombe donc en aplats ; les lumières d'appoint s'ajoutent en douceur.
- Une capture lancée dans le même appel que la réimportation d'un shader le montre à moitié compilé (matériaux sombres et piquetés) : capturer dans l'appel suivant.
