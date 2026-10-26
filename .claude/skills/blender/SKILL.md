---
name: blender
description: Piloter Blender pour Duel Arena, le Blender ouvert de l'utilisateur (par un pont local) ou un Blender en arrière-plan, pour modéliser, retoucher, rendre ou exporter des modèles 3D low poly conformes à la bible DA (armes à balles, série 30 et armes laser futuristes, accessoires, décor), avec usure et rafistolage. À utiliser dès qu'il est question de Blender, d'un fichier .blend, de modéliser ou retoucher un objet en 3D, d'une arme ou d'un accessoire à dessiner en volume, d'un rendu de contrôle ou d'un export FBX vers Unity, même si l'utilisateur ne prononce pas le mot « Blender ».
---

# Blender pour Duel Arena

Deux façons de faire tourner du code Blender :

- **Dans la fenêtre de l'utilisateur**, par le pont (`scripts/pont_blender.py`, lancé une fois par session de Blender). Il voit le travail se faire, et c'est ce qu'il demande quand il dit « connecte-toi à mon Blender ».
- **Dans un Blender en arrière-plan**, sans toucher à sa fenêtre : c'est là qu'on itère et qu'on rend les vues de contrôle (une vue EEVEE prend environ une seconde).

Blender 5.2 LTS : `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`.

## Se brancher au Blender ouvert

1. `python -I .claude/skills/blender/scripts/blender_exec.py --ping` : le pont répond avec la version de Blender et le fichier ouvert.
2. S'il ne répond pas, demander à l'utilisateur de coller cette ligne dans la **console Python** de Blender (onglet *Scripting*), puis Entrée. La lui donner dans un bloc `python` :

   ```python
   exec(open(r"D:\Documents\Unity\FPS Claude\.claude\skills\blender\scripts\pont_blender.py", encoding="utf-8").read())
   ```

   La console répond « [Pont Claude] à l'écoute sur 127.0.0.1:9877 ». C'est à refaire à chaque lancement de Blender ; la recoller remplace l'ancien pont.
3. Sans contrôle de l'écran (computer use) dans la session, c'est l'utilisateur qui colle la ligne. Ne pas la taper dans sa fenêtre par un détour (SendKeys, AutoHotkey…) : des frappes qui atterrissent dans la mauvaise fenêtre peuvent faire des dégâts, et ce serait contourner une permission qu'il n'a pas donnée.
4. En attendant qu'il le fasse, travailler en arrière-plan (ci-dessous), puis rejouer le même script dans sa fenêtre.

Le pont n'écoute que 127.0.0.1 et n'exécute que les requêtes portant le jeton tiré à son lancement (écrit dans `%LOCALAPPDATA%\ClaudeBlender\pont.json`, que lit le client). Arrêt : `bpy.app.driver_namespace["pont_claude"].arreter()`.

## Exécuter du code

```bash
python -I .claude/skills/blender/scripts/blender_exec.py -c "len(bpy.data.objects)"
python -I .claude/skills/blender/scripts/blender_exec.py -f script.py --delai 300 -- --un-argument
```

- Une expression seule renvoie sa valeur ; un script reçoit ses arguments dans `ARGS` et son chemin dans `__file__`. Les variables survivent d'un appel à l'autre.
- Le code s'exécute dans une minuterie du fil principal de Blender : le contexte n'a ni fenêtre ni zone. Pour un opérateur qui en exige une : `with bpy.context.temp_override(**contexte_3d()): …` (fonction fournie par le pont). Mieux : passer par `bpy.data` et `bmesh`, qui n'en demandent pas, et évaluer par `scene.view_layers[0].depsgraph`.
- Pendant un appel, l'interface de l'utilisateur est figée : les rendus et les cuissons se font en arrière-plan.
- Le fichier ouvert est le sien : travailler dans **une scène à soi**, ne rien supprimer qui ne vienne pas de nous, ne pas l'enregistrer à sa place sans le lui dire.

En arrière-plan :

```bash
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python-exit-code 1 -P script.py -- --rendus <dossier>
```

Lire les PNG rendus (outil Read) pour juger le modèle : on ne voit le résultat qu'ainsi.

## Le pipeline des modèles du projet

`Tools/Blender/` :
- `kit_lowpoly.py` : les primitives (voir plus bas) ;
- `commun_armes.py` : ce que partagent les armes (matériaux, rails, mires, poignées, chargeur et cellule, formes futuristes, marquages, studio, lancement `principal()`) ;
- un script par arme : les armes à balles du stock `fusil_assaut.py` (FA-07), `fusil_precision.py` (FP-12), `fusil_pompe.py` (FAP-03), `pistolet.py` (PA-05) ; la série 30 `serie30_fusil.py` (FA-30), `serie30_precision.py` (FP-30), `serie30_pompe.py` (FAP-30), `serie30_revolver.py` (RV-30) ; les armes laser `laser_fusil.py` (FX-1), `laser_precision.py` (FX-2), `laser_dispersion.py` (FX-3), `laser_pistolet.py` (FX-4) ;
- `construire_armes.py` : toutes les armes dans `Armes.blend` (une scène par arme, plus une planche : une classe par ligne ; le stock à gauche, la série 30 au milieu, le laser à droite) ; `--empreinte` imprime l'empreinte de la forme de chaque arme ;
- `variantes_usure.py` : les planches des variantes d'usure (le FA-07 et le FA-30 dans chaque variante, de profil et en gros plan), rendues dans le dossier donné ;
- `exporter_armes.py` : l'export vers Unity (voir « L'export vers Unity ») ; `--armes a.py,b.py` pour n'en refaire que quelques-unes ;
- les accessoires (voir « Les accessoires ») : `commun_accessoires.py` (atelier, embases de rail, vitres, réticules de contrôle, montage sur une arme pour les rendus), `viseurs.py` (les dix viseurs de Rainbow Six), `laser_tactique.py` (LT-1 et LT-P), `poignees.py` (PV-1, PI-1), `chargeurs.py` (un grand chargeur par modèle d'arme), et leur export `exporter_accessoires.py` ;
- `gant_choc.py` : le gant à décharge GD-1, l'arme de corps à corps (voir « Le gant à décharge »), exporté par le même `exporter_accessoires.py` (`--scripts gant_choc.py --sortie Assets/_ProjectArena/Weapon/Gant --resolution 1024`) ;
- `robot_ct01.py` : le robot CT-01, le robot du jeu (voir « Le robot ») ; `--rendus` (vues `comparaison`, `bustes`, `planche`, `couleurs`, `heros`, `dos`), `--fbx` pour Unity ;
- `robot_os02.py` : l'OS-02, l'« Ossature », un autre châssis au choix du joueur ; mêmes options ;
- `FusilAssaut_v1_viseur_numerique.blend` : la première version du fusil, avec son viseur numérique, gardée pour en faire un accessoire.
- `archives/` : les versions remplacées d'un modèle (`fusil_assaut_FA07_v2.py`, `serie30_precision_FP30_v1.py`), gardées pour mémoire ; `construire_armes` ne les lit pas, et pour en relancer une, la recopier à côté des autres.

- **Un modèle est un script qui le reconstruit en entier**, dans sa propre scène. On corrige le script, pas le `.blend` : la reconstruction suivante écraserait une retouche à la main. Si l'utilisateur a retouché le modèle à la main dans Blender, le prévenir avant de relancer le script.
- Options de chaque script d'arme : `--sortie f.blend`, `--rendus dossier` (dans le scratchpad, pas dans le projet), `--vues a,b`. Le script garde son atelier dans `ATELIER` (le lit `construire_armes`).
- Vues du studio (cadrées sur les bornes) : `profil_gauche`, `profil_droit`, `trois_quarts`, `arriere` (à peu près ce que voit le joueur), `dessus`, les gros plans propres à l'arme (`VUES` du script : `visee` dans l'axe de la mire, `detail_*`) et `profil_argile` (les volumes seuls, rendu quand on ne choisit pas de vues).
- Itérer : rendre, regarder, corriger, rendre. Un défaut se juge sur l'image, pas sur le code.

## Conventions des armes (pour Unity)

- Mètres. **Origine au point de prise de la main droite**, canon vers **-Y** (+Z dans Unity par `exporter_armes.py`), haut +Z, droite du tireur -X. Les positions s'écrivent `P(av, h, x)` : vers la bouche, hauteur, côté (+ = gauche).
- **Trois familles d'armes** : les armes à balles du stock (FA-07, FP-12, FAP-03, PA-05) ; la série 30 (FA-30, FP-30, FAP-30, RV-30), des armes à balles aux formes modernes, d'après les références d'*Apex Legends* de l'utilisateur (R-301, un pompe à dissipateurs, Wingman ; pour le FP-30, le 30-30 Repeater puis l'image d'une carabine à levier tactique) ; les armes laser du programme d'essai (FX-1 à FX-4), des modèles à part aux formes franchement futuristes (demandé par l'utilisateur le 2026-10-15 : « en t'inspirant plus d'armes comme Apex Legends »), pas des variantes des premières. Toute arme garde **les points de prise de sa classe**, pour que les animations lui servent : poignée à l'origine (même T, même inclinaison ; seules exceptions, voulues par l'utilisateur : le poignet droit de la crosse de chasse du FP-30 et du FAP-30, voir `MainDroite`), puits du chargeur (une cellule d'énergie sur les lasers, qu'on saisit au même endroit), fenêtre de chargement du pompe ou porte de chargement sous la boîte de culasse (le FP-30, sans chargeur), levier de charge en haut à gauche, dessous du garde-main sous la main gauche, crosse à l'épaule.
- **Le clip de rechargement du jeu (`Y Bot@Reloading`, le seul, pour toutes les armes)**, mesuré dans le repère de l'arme Unity (x droite, y haut, z avant) : la main gauche saisit le chargeur vers z 0,19 à 0,22 et y -0,14 à -0,16, le remet au même endroit, arme un levier **en haut à gauche, à l'aplomb de la poignée** (z ≈ 0,02, y ≈ 0,20), puis revient vers z 0,25 à 0,27. D'où, pour les armes d'épaule : puits du chargeur, fenêtre ou porte de chargement sous la carcasse entre av 0,16 et 0,27 ; levier d'armement ou manette en haut à gauche, au-dessus de la poignée. Le pistolet garde son chargeur dans la poignée (comme le raygun ; le clip n'a pas de variante pistolet, dette n° 45).
- Objets vides **`<code>_Mire`** (là où se place l'œil en visée : centre de l'anneau de la hausse, du cran, de l'oculaire) et **`<code>_Bouche`** : `WeaponModel` les lit.
- **`<code>_MainGauche`** (armes longues, pas les pistolets) : sous le garde-main, là où la paume gauche tient l'arme (sur les dents du rail du dessous s'il y en a un). Les animations placent la main gauche pour un garde-main profond (son dessous vers h 0,06 à 0,075) ; un garde-main fin passe au-dessus (FA-07), d'où une IK de la main gauche à l'intégration. Contrôle : un rayon tiré vers le haut depuis 5 cm plus bas touche l'arme à quelques millimètres du point.
- **`<code>_MainDroite`**, seulement quand la main droite ne tient pas une poignée pistolet à l'origine (la crosse de chasse à poignet droit du FP-30 et du FAP-30) : le centre du poignet, cible d'une IK de la main droite ; l'arme reste placée par l'origine, qui tombe alors dans la boucle du levier (FP-30) ou derrière le pontet (FAP-30). La détente reste à la place de sa classe, et la carcasse s'arrête 4 à 5 cm derrière elle, là où commence le poignet. Contrôle : un rayon tiré depuis la gauche touche le flanc de la crosse.
- **Armes nues, prêtes pour les accessoires** : rails (dessus pour les viseurs, dessous pour les poignées, flanc droit pour les lasers ; plaques à encoches, `plaque_montage`, sur les armes laser), et des points d'accroche sous `<code>_Accessoires` : `Viseur`, `Laser`, `Poignee`, `Chargeur`, `Crosse` (les quatre premiers sont les rubriques de `AttachmentCategory`). Ce qu'un accessoire remplace est un objet ou un groupe à part : `Chargeur_Standard`, `Crosse_Standard` (groupe), `Lunette_Standard` (groupe, sniper), `Hausse` et `Guidon` (à masquer sous un viseur).
- **Noms préfixés par le code de l'arme** (`FA07_`, `FX1_`) : toutes tiennent dans un fichier ; l'export retire le préfixe. Une pièce ne porte jamais le nom d'un repère de la racine (`Mire`, `Bouche`, `MainGauche`, `MainDroite`, `Accessoires`) : l'export la renomme (`RENOMMAGES_PIECES`) ou s'arrête.
- **Une mire ne se perce pas d'un trou** : un jeu n'a pas de profondeur de champ, et une tour percée d'un œilleton cache l'écran en visée. Hausse = anneau fin sur une tige (`commun_armes.hausse`) ; guidon = lame fine à pointe claire, centrée dans l'anneau.
- **L'écran d'un viseur numérique est un objet et un matériau à part** : `WeaponSight` remplace ce matériau par le shader `DuelArena/ViseurEcran`.
- **Le flanc gauche (+X) et le dessus sont ce que voit le joueur** en vue subjective : c'est là que vont les détails qui racontent quelque chose ; le flanc droit est pour les autres.
- Budgets relevés (2026-10-15) : FA-07 6 100 triangles, FP-12 4 900, FAP-03 2 800, PA-05 1 500 ; FA-30 4 600, FP-30 5 700, FAP-30 4 200, RV-30 2 300 ; FX-1 3 900, FX-2 5 300, FX-3 5 300, FX-4 2 200 (l'ancien fusil en faisait 81 600). Le script imprime le compte par pièce : un détail qui en coûte des centaines (un cordon de soudure au pas fin, des chanfreins sur des réglettes) se simplifie.
## L'export vers Unity (`exporter_armes.py`, 2026-10-15)

```bash
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python-exit-code 1 -P Tools/Blender/exporter_armes.py -- --sortie "Assets/_ProjectArena/Weapon/Armes" --resolution 1024 --echantillons 64
```

Une trentaine de secondes par arme (la cuisson de l'occlusion), six minutes pour les douze. Puis, dans Unity : **Duel Arena > Armes > Importer les armes de Blender** (préfabs, matériaux, branchement sur les `WeaponData`). Pour chaque arme, dans `<sortie>/<CODE>/` :
- `<CODE>_Modele.fbx` : les pièces (noms sans préfixe), les repères et les points d'accroche (`Manchon` à la place de `Poignee` sur un pistolet). Chaque pièce reçoit un dépliage UV commun à toute l'arme (Smart UV Project, marge 0,006), qui ne sert qu'à l'occlusion, et l'attribut de face `bord` recopié en couleur de sommet (le rouge, par coin de face).
- `<CODE>_Occlusion.png` : le nœud Ambient Occlusion des matériaux (1,8 cm), cuit par Cycles (émission, 64 échantillons, marge de 8 pixels), en gris linéaire.
- `<CODE>_Finitions.json` : pour chaque matériau de l'arme et chaque finition, les paramètres complets de `kit.materiau` (défauts compris), les **décalages tirés pour chacun de ses bruits** (`CANAUX`, dans l'ordre où `materiau` les crée ; l'export les vérifie sur les nœuds Mapping des matériaux qu'il construit vraiment), et les pièces que la « dépareillée » remplace. Positions dans le repère de l'arme de Blender.
- **Axes** : l'export fait pivoter l'arme de -90° autour de X (géométrie et positions, jamais les axes des repères) puis l'exporte sans transformation d'espace (`use_space_transform=False`, `bake_space_transform=False`, `axis_forward="-Z"`, `axis_up="Y"`) ; Unity ne fait que retourner X, avec `bakeAxisConversion` à faux. Résultat : racine et objets sans rotation ni échelle, canon +Z, haut +Y, gauche du tireur -X, et Unity = (-x, z, -y) de Blender. « Apply Transform » tournait deux fois les objets imbriqués ; l'export par défaut laissait -90° sur la racine.
- **Le shader `DuelArena/ArmeUsee` refait l'usure de `kit.materiau`** à partir de ces paramètres : une nouvelle marque d'usure dans le kit demande la même dans `Assets/_ProjectArt/Shaders/ArmeUsee.hlsl` (et son canal dans `CANAUX` et dans `WeaponImporter.NoiseChannels`), sinon Unity ne la montre pas. Le bruit y est celui de Blender (Perlin amélioré ×0,982 haché par lookup3 de Bob Jenkins, fBm normalisé ; Voronoï F1 haché par PCG 3D en entiers signés) : vérifier un changement par une cuisson de Cycles comparée pixel par pixel à sa réimplémentation numpy.

## Les accessoires (2026-10-16)

Les règles et les effets sont dans le GDD (§ 6), les choix d'apparence dans la bible (§ 12). Chaque script construit une famille d'accessoires (`ATELIERS`) avec `commun_accessoires.AtelierAccessoire` ; options : `--rendus`, `--vues`, `--seuls PR-A,HO-B`, `--sur-arme fusil_assaut.py` (le rendu monté sur une arme : `monter`, `rendre_sur_arme`), `--usure`.

- **L'origine est le point d'accroche de l'arme** (`Viseur`, `Laser`, `Poignee`, `Chargeur`, sous `<code>_Accessoires`), dans les axes de l'arme : Unity le pose sans décalage (`AttachmentMount`). Un grand chargeur est construit sur l'arme puis ramené à son point (`a.transformation`). Chercher un point d'accroche sous le groupe `Accessoires` (`point_de`) : la pièce « Poignee » d'une arme porte le même nom que le point.
- Les repères : `Mire` (l'axe optique d'un viseur, à `hauteurAxe` au-dessus du rail, au dos du viseur, au milieu de sa vitre : un test le vérifie), `Faisceau` (l'émetteur d'un laser, +Z), `MainGauche` (la paume sur une poignée).
- **La vitre est un objet à part** (`a.vitres`, matériau `DA_Vitre_<teinte>`), sans dépliage ni occlusion : Unity y met le shader du réticule (`DuelArena/ViseurReticule`). Le réticule dessiné dans Blender (`reticule`, dans `a.controle`) ne sert qu'aux rendus de contrôle et ne part pas ; la fiche en garde la forme (`FORMES_RETICULE`, même ordre que `_Forme` du shader), la couleur et le rayon (`rayonReticule`), dont Unity tire la taille à l'écran (`AttachmentImporter.ReticleRadius`, 6 000 mrad par mètre), et l'épaisseur des traits (`traitReticule`, `trait_=` ; 7,5 % du rayon par défaut), fixe quelle que soit la taille. Une nouvelle forme demande la même dans le shader.
- **Un réticule qui vise par une pointe l'a au centre exact** (2026-10-26, « pas précis » pour l'utilisateur) : le chevron (`chevron`, proportions `CHEVRONS`, face extérieure des branches par la pointe) et le triangle (plein, sa pointe en haut, `rayon` = sa demi-largeur). Traits fins (0,4 mm dessinés, 2,4 mrad) ; le triangle des réflex fait 11 mrad de large.
- **Juger un viseur depuis l'œil du jeu**, pas depuis le rendu `visee` de `sur_arme` : l'œil est 8 cm derrière la mire du viseur, champ vertical de 60° (celui des bras). Ce qui se trouve près de l'œil paraît énorme : un dos de réflex qui monte au-dessus du bas de la fenêtre se met sous le réticule, un corps latéral à 3 cm de l'œil mange un côté de l'écran, un cadre profond fait un tunnel autour de la vitre, une embase qui dépasse devant la fenêtre remonte dans son bas (tout vu le 2026-10-26). Les joues du dos restent de part et d'autre de la ligne de visée.
- Ce qu'un accessoire cache de l'arme se décide dans Unity (`AttachmentImporter.HiddenParts`) : `Hausse`, `Guidon`, `Guidon_Base`, `Points_Hausse` sous un viseur ; `Chargeur_Standard` sous un grand chargeur, sauf la rallonge d'un tube (`genre` « tube »).

```bash
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python-exit-code 1 -P Tools/Blender/exporter_accessoires.py -- --sortie "Assets/_ProjectArena/Weapon/Accessoires"
```

Une dizaine de secondes par accessoire, cinq minutes pour les vingt-cinq (`--scripts viseurs.py`, `--seuls CG-FA07` pour en refaire quelques-uns). Même dépôt que les armes (`<CODE>_Modele.fbx`, `_Occlusion.png` en 512, `_Finitions.json`, plus la rubrique, la vitre et les données de l'accessoire), puis **Duel Arena > Armes > Importer les accessoires de Blender** (ou l'import des armes, qui le fait aussi) : préfabs, vitres, catalogue `Accessoires.asset`.

## Le gant à décharge (2026-10-16)

`gant_choc.py` construit le GD-1 dans le **repère de l'avant-bras gauche du robot** (le préfab `GantChoc`, posé au poignet sur l'os de l'avant-bras) : `P(av, h, x)` avec av vers les doigts, h vers le dos de la main, x vers l'auriculaire (le pouce à -x, +X dans Unity). Origine au poignet ; le repère `Poing` au bout des électrodes, là où partent les étincelles. La main du robot est repliée quand il le porte : le poing du gant est la main.

- La manchette suit l'avant-bras du Y Bot (8 cm de large au poignet, 11,6 cm près du coude ; jointures à 12,8 cm du poignet) avec 6 à 14 mm de jeu, par stations (`STATIONS`, interpolées par `station(av)`). Contrôle dans Unity : aucun sommet de l'avant-bras, pose de référence, hors de la manchette.
- Ses flancs s'évasent vers le coude : un décalque posé sur un plan vertical s'y enfonce par un bout (vu sur « GD-1 »). Le donner dans l'axe du flanc (`axe_x` entre deux stations).
- Les arcs du jeu (TaserSparkFX) partent des pièces « Electrodes » (le centre de la face avant de chacune) et « Condensateurs » (le haut de leurs ailettes) : l'importeur les relève sur le modèle (GloveImporter). Garder ces deux pièces sous ces noms.
- La suie des arcs vient de la « bouche » de l'atelier, posée au bout des électrodes (`a.bouche`, que l'export garde) : seules les pièces au matériau `Bouche` noircissent (la plaque des électrodes, leurs pointes).
- Le studio est celui des armes (`ca.studio`) : celui des accessoires, réglé pour 5 cm, surexpose un objet de 40 cm.

## Le robot (2026-10-26)

Les robots partagent `commun_robots.py` (le repère du Y Bot, l'atelier, les primitives, les matériaux dont `Chrome`, l'assemblage, les contrôles, les rendus, l'export) ; chaque robot est un script qui décrit son corps et le confie à `cr.principal(cr.Modele(code, préfixe, corps, variantes, skins, cadre de l'écran, écarts voulus))` : `robot_ct01.py` (le CT-01, le robot du jeu) et `robot_os02.py` (l'OS-02, l'« Ossature », un endosquelette inspiré du T-800, aux mêmes tailles, un autre châssis au choix). Leur FBX (`--skin vierge --fbx`) va dans `Assets/_ProjectArt/Robots/<code>/Robot <code>.fbx`, puis **Duel Arena > Robot > Monter les robots sur le préfab joueur** les remonte sur le squelette du préfab. `plaque_radiale` pose une plaque qui fait le tour d'une pièce bombée (un crâne), projetée depuis son cœur. `couverture_robots.py` mesure la part de la surface touchable que chaque robot couvre à l'écran (face et profil) et la rend ; `--seuls CT-01,OS-02 --sans-rendu` donne l'écart au CT-01 zone par zone. **Un nouveau robot doit couvrir autant que le CT-01** (à un point près par zone) : un robot plus fin se voit et se vise moins bien pour la même surface touchable. `ecran_mort.py` fait les masques de l'écran FATAL ERROR de la visière (rouge : le triangle, vert : le texte, sur fond transparent : le monde par défaut est gris), dans le cadre de ses UV (`uv_ecran`).

`robot_ct01.py` construit le CT-01 (inspiré de l'archétype de KAY/O, sans en reprendre le dessin ; la v2, « plus menaçante », à pans et en V ; la v1 dans `archives/`) **sur le squelette du Y Bot**, importé de son FBX tel quel (objet Armature tourné de 90° et à l'échelle 0,01 : les actions des clips Mixamo s'y appliquent sans conversion).

- Repère du monde du Y Bot : mètres, haut +Z, le robot regarde vers -Y, sa gauche en +X. Pose de repos en T : les bras le long de ±X, la paume vers le bas.
- **Une pièce = un os** (`rb.piece(nom, os, mat)`) : poids 1 sur cet os, rien d'autre, comme un robot. Les articulations sont des tambours (`tambour`) qui cachent le jeu quand les pièces tournent ; une plaque qui déborde sur l'os voisin s'y enfonce en mouvement.
- `Os(arm, nom, u, v)` donne un repère qui suit l'os : `p(s, a, b)` le long de l'os, puis selon u et v (bras : u = haut, v = avant ; jambes : u = dehors, v = avant). `coque_profil` (une plaque à pans autour de l'os : un profil d'angles et de facteurs, une arête là où le facteur culmine ; trois points font une lame en V), `section_lame` (une lame à arête posée devant ou sur le flanc d'un os, lofée le long de l'os : celles des jambes) et `tube_profil` (fermé) s'y construisent, comme `coque` et `tube` (ronds) ; un côté droit sort en miroir du gauche, les lofts refont leurs normales.
- **Un seul langage pour tout le corps** (demandé par l'utilisateur) : une poutre à plat, des vérins à nu, une lame peinte à arête devant chaque segment de membre, des plaques blindées sur les flancs ; rien de rond. Une nouvelle pièce suit ce langage. L'arrière s'habille et se peint comme l'avant (une lame peinte derrière chaque segment, autant de couleur de dos que de face), et le dos répond à la face pièce pour pièce (omoplates et pectoraux, réacteur et cœur, colonne et sternum) : nu, il paraissait fragile ; sombre, noir ; un caisson posé dessus, rapporté.
- **Une plaque posée sur une pièce** (`plaque_sur`, `face_avant`) en épouse la courbure : son contour, des anneaux vers son centre et le centre sont projetés sur la surface. Tendue entre ses seuls bords, une plaque sur une surface bombée se faisait traverser en son milieu (des pectoraux sombres cerclés de peinture, des fentes lumineuses coupées en deux).
- **Le lissage à 35° arrondit les pans plus doux que 35°** : un bloc à pans larges paraissait gonflé. Une pièce peut le baisser (`pc.angle_lisse`, lu par `assembler`).
- Un bassin d'un bloc peint, large aux hanches, a l'air d'un slip (refusé deux fois, même au dessous plat) : l'utilisateur a gardé celui de la v4, ceinture à boucle, coquille sombre, tassettes peintes, plaque sombre derrière.
- Une plaque qui enveloppe une pièce doit rester plus large qu'elle à chaque hauteur : la bande la plus large de l'abdomen perçait le coin arrière de la plaque du bas du dos (un petit rectangle sombre, qu'on ne voit que de près).
- Pour faire choisir l'utilisateur : `VARIANTES` et `--variantes A,B,C,D` (chaque variante construite en entier sur son squelette, rendue côte à côte de dos, de trois quarts et de profil, puis en gros plan) ; aujourd'hui, des bas du dos.
- **Les skins** (`SKINS`, `--skins a,b`) : une usure par famille de matériau (`materiaux(couleur, skin)` ; les matériaux portent le nom du skin, sinon le dernier construit écrase les autres), les genres de pièces gardés (`rb.piece(..., genre="marquage")` ou `"rafistolage"`, filtrés par `assembler`), des pièces refaites dans un autre matériau (`remplacements`), une livrée posée une fois le corps construit (`rb.surface(nom)` rend la surface d'une pièce ; `ruban` y trace une bande point par point : une `plaque_sur` longue et fine se creuse sous une surface bombée). Une pièce physique ne va jamais dans une pièce de marquage (le capteur de la tempe en est sorti).
- `rendus_cote_a_cote` (variantes, skins) : une rangée sous des lampes reculées (`lumieres(recul=3)` ; sinon le premier de la rangée prend deux fois plus de lumière que le dernier), puis chacun au centre pour son portrait. Le grain des dessus sombres vient du matériau, pas de l'échantillonnage (160 échantillons ne l'enlèvent pas).
- Les marquages se posent **par un rayon** sur la pièce déjà construite (`Surface(pc).toucher`) : le point et la normale de sa face, puis `axes_decalque` (lisible de l'extérieur).
- `assembler` ramène chaque pièce dans le repère du squelette (en cm), en fait un enfant sans transformation, puis réunit tout en un maillage : un seul SkinnedMeshRenderer dans Unity. Les matériaux du kit lisent la position de l'objet : un repère neutre à l'origine du monde (`repere_usure`, `ancrer`) leur rend des mètres pour les rendus.
- **Il tient dans la surface touchable du jeu** : `verifier_surface_touchable` mesure, partie par partie, ce qui sort des capsules debout du `Player.prefab` (tête, torse, jambes ; le Y Bot : 1,8 cm à la tête, 2,6 au buste), et `verifier_volumes` compare, os par os, son rayon maximal à celui de la peau du Y Bot (`VOLUMES_Y_BOT`, `ECARTS_VOULUS` pour les épaulières). Le haut du torse touchable est une demi-sphère : des épaules hautes, une crête, des cheminées en sortent vite. L'avant-bras gauche doit en plus tenir sous la manchette du gant.
- Les trois rendus (`rendus`) : low poly propre (les matériaux usés du kit), cel-shading (`aplats` : la lumière reçue par Shader to RGB en trois tons, un reflet franc, un liseré ; contour par coque inversée, `ajouter_contour`), les deux mêlés (`aplats` avec l'usure). Les copies sont posées par l'action d'un clip du même squelette (`charger_attente` : l'attente à mains nues, ou celle au fusil des clips du soldat, qui se pose bien sur le squelette du Y Bot). La vue `heros` arme le robot (`armer` : la collection de l'arme prise dans `Armes.blend`, en instance, son origine au creux de la main droite et son repère MainGauche à celui de la gauche). Une lueur forte de la couleur exacte blanchit sous AgX : `sature` pousse sa teinte.
- Un acier poli tout à fait métallique reflète le décor sombre des rendus (et de l'arène) et paraît noir : `Chrome` est métallique à 0,65, rugueux à 0,30.
- Un écran, une vitre : mats (`rug` haut, sans vernis). Vernis, il renvoyait la lumière de remplissage et le visage devenait gris.

## La direction artistique (bible § 5, 10, 12, 14, 17)

Relire `Docs/Bible-Lore-DA.md` avant un nouveau modèle. Ce qui en découle pour un objet :

- **Low poly propre** : volumes simples, profils extrudés, chanfreins de 1 à 3 mm sur les arêtes vives (ils accrochent la lumière et portent l'usure), lissage par angle à 35°.
- **Palette** : une arme reste dans les tons du décor (anthracite, noir, acier, blanc cassé), presque sans jaune. Jamais les couleurs vives des robots (orange, bleu, magenta, vert, cyan, violet, jaune) : la peinture identifie le joueur. Une pièce rapportée peut avoir une autre couleur, sourde (les plaquettes à l'apprêt rouge du RV-30).
- **Usure dans le matériau** (`kit.materiau`) : arêtes usées jusqu'au métal, éclats, rayures courtes, crasse dans les creux, suie à la bouche. Pas de géométrie pour ça.
- **Le rafistolage raconte** (bible § 5) : ruban, colliers, soudures, plaque rivetée, pièce dépareillée. Il ne change ni la silhouette ni le fonctionnement (bible § 12 : une arme bricolée marche comme les autres).
- **Marquages** : le programme d'essai (désignation, numéros, mire de crash-test noir et blanc) et la culture des duels (encoches de victoires). En français, police Bahnschrift, en décalque.
- Humour sec, jamais enfantin (bible § 14) : la croix de ruban sur le compteur de munitions mort, oui ; un gag par pièce, non.
- **Les armes laser** (bible § 12), dans l'esprit des armes à énergie d'*Apex Legends* :
  - des volumes en coin, épais et anguleux : sections à huit pans (`section8`), lofts qui s'évasent ou s'effilent, pas de tube nu ;
  - des aplats de deux tons : peinture anthracite et céramique blanc cassé (`mats8` donne un matériau par pan d'un loft : chanfreins du dessus, flancs, dessous). Le dessus d'un capot reste anthracite, la céramique sur les chanfreins : un grand aplat clair en haut de l'arme envahirait la vue subjective ;
  - **l'énergie se voit** : une fenêtre de part en part sur un cœur cerclé de bobines (`coeur`), un accélérateur entre deux poutres, une charge engagée ; des lueurs rouges (`ROUGE_LUEUR` ; ni le rouge ni le blanc ne sont des couleurs de robot) au fond des évents, sur les lentilles, dans un compteur allumé ;
  - un émetteur sculpté, propre à chaque arme, qui fait la silhouette : tête carrée à ailerons (FX-1), dents en fourche (FX-2), coupe aux huit lentilles disposées comme la gerbe du jeu, `pelletPattern` (FX-3), anneau focaliseur hexagonal (FX-4) ;
  - marquage « PROTOTYPE », pictogramme laser et bande hachurée en noir et blanc cassé ; sur la céramique, un texte en `MarquageNoir` ;
  - le compteur de charge est à l'arrière de l'arme, face au joueur (`compteur_arriere`), là où il se lit en vue subjective (demandé par l'utilisateur le 2026-10-15) : le dos du boîtier du pistolet ; sur une arme d'épaule, un pan ou une rampe au dos de la carcasse, relevé vers l'œil, sous la ligne de mire et derrière la hausse (en visée, il reste bas dans le champ). Jamais sur un accessoire : la lunette se remplace. Exception, le FX-2 (2026-10-26) : sa lunette remplit l'écran en visée, et à la hanche son dos sort de l'écran sous l'oculaire ; son compteur est dans un boîtier sur le haut gauche de la carcasse, entre les embases, tourné vers l'œil à la hanche (`lacet`, mesuré dans Unity : 35 cm derrière l'oculaire, 18 cm au-dessus, 12 cm à gauche).
- **La série 30** (bible § 12) : peinture blanc cassé sur métal (`PeintureClaire`, ses éclats montrent l'acier), aplats anthracite et noirs (dessus noirs : rail, bande ventilée, pour ne pas envahir la vue subjective), un accent de laiton terni (`Laiton` : ailettes, bagues, chien, détente) ; aucune lueur. Les couleurs vives des références deviennent anthracite (le bleu est une couleur de robot).
- **Une référence d'arme existante (Apex, CoD) donne une grammaire, pas un plan** : en reprendre l'archétype et le langage (ferme de triangles, carter à pans, dissipateurs), jamais calquer un dessin ou un logo ; le jeu sera vendu. La photo d'une arme réelle (un M4, une carabine à levier), elle, se suit de près pour la silhouette et les proportions, la palette restant celle du jeu : l'utilisateur juge la silhouette (FA-07 refait sur une photo de M4, l'avant de la version précédente étant trop épais ; crosse du FP-30 refaite « type fusil de chasse, plus longue et plus fine », puis reprise sur le FAP-30).
- Chaque arme a son histoire de rafistolage, sans répéter celle d'une autre :
  - FA-07, caches des rails remplacés par du ruban adhésif, anneau de grenadière refait en fil de fer, goupille avant remplacée par un boulon ; FP-12, bouton de culasse remplacé par un écrou soudé, appui-joue rembourré au ruban ; FAP-03, collier sur le tube, crosse agrafée ; PA-05, chambre à air, semelle dépareillée ;
  - FA-30, crosse réglable bloquée par un collier de serrage, bec écorné ; FP-30, boucle du levier gainée de ruban toilé, plaque de couche taillée dans un pneu ; RV-30, plaquettes refaites dans une tôle à l'apprêt, dragonne en fil de fer ; FAP-30, une ailette perdue et une autre en acier brut, poignet de crosse fêlé et serré de ruban toilé ; toutes quatre, des bâtons de victoires au marqueur ;
  - FX-1, panneau de céramique remplacé par une tôle rivetée, nez du capot au ruban ; FX-2, poutre fêlée sous un collier de serrage, câble de dérivation bridé sur les bagues, tôle à l'apprêt rouge ; FX-3, corne cassée net, pompe fendue tenue au fil de fer, tôle soudée, cellule perdue dans la cartouchière ; FX-4, plaque de la tête remplacée par une tôle boulonnée, boîtier fêlé au ruban.

## Le kit (`Tools/Blender/kit_lowpoly.py`)

- `Piece(nom, materiau)` : un bmesh et son attribut de face `bord` ; `vers_objet(...)` en fait un objet lissé.
- Volumes : `prisme` (profil (av, h) extrudé en x), `boite`, `loft` + `sections_av` (sections (x, h) le long du canon ; `mats_cotes` : un matériau par côté de la section), `cylindre`, `tranches` (rayons variables), `balayage` (section 2D le long d'un chemin), `lisser_chemin`, `helice`.
- `chanfreiner(pc, largeur, faces)` : chanfreine les arêtes convexes et marque `bord = 1`, que lit l'usure.
- Rafistolage : `bande` (ruban, collier, cerclage autour d'une section, avec bout décollé), `soudure`, `rivet`, `vis_six_pans`.
- Décalques : `quad`, `disque` (en quartiers pour une mire), `texte`.
- `couper(objet, coupeur, scene, materiaux)` : différence booléenne exacte ; les faces de coupe prennent le matériau du coupeur.
- `boite_axes` (boîte orientée), `anneau` (couronne creuse le long du canon).
- Dans `commun_armes` : `Atelier` (une arme : collection, racine, `piece`, `objet`, `couper`, `reperes`, `accroches`, `texte`), `rail`, `hausse` (`cotes=6` : anneau hexagonal), `guidon`, `poignee_pistolet`, `pontet`, `detente`, `courbes_chargeur` avec `chargeur` ou `cellule`, `emetteur`, `compteur_arriere` (l'écran du compteur, face au joueur), `autocollant_laser`, `bande_hachuree`, `encoches` (gravées, ou au marqueur : `mat="MarquageNoir"`, `largeur`), `mire_crash` ; pour les armes laser, `section8` et `mats8`, `poignee_futuriste` (même prise que `poignee_pistolet`), `coeur`, `plaque_montage`, `lueurs_dessus`.
- `materiau(nom, base, …, dessous, bords, eclats, rayures, salete, suie)` : usure procédurale, visible dans EEVEE comme dans Cycles, sans UV. Les marques d'un long service, absentes par défaut : `couche` (un apprêt sous la peinture, qui borde les éclats), `retouches` (peinture refaite au pinceau, d'une autre teinte), `rouille` (brune et sombre : cloques, creux, coulures), `huile`, `poussiere`, `delavage`, `zones` (l'usure forcée là où les mains frottent, le métal poli). `commun_armes.VARIANTES_USURE` en fait cinq variantes, que chaque script prend par `--usure` (repeinte, rouillee, depareillee, polie, encrassee ; « depareillee » échange aussi la finition de pièces, `REMPLACEMENTS_USURE`). Sans variante, le rendu ne change pas au pixel près.
- Studio : `lumiere`, `camera`, `rendre`.

## Pièges rencontrés (Blender 5.2)

- `Material.use_nodes` est déprécié (retrait en 6.0) : un matériau neuf a déjà son `node_tree`.
- `node.inputs["Factor_Float"]` cherche par **nom** : sur un nœud Mix, plusieurs prises s'appellent « A ». Passer par l'identifiant (`_par_id` dans le kit).
- Une couleur RGBA dans une entrée vecteur (Vector Math) lève une erreur : trois composantes.
- `bmesh.ops.bevel` : `affect="EDGES"` et `material=-1`, sinon les chanfreins prennent le matériau 0.
- Le moteur s'appelle `BLENDER_EEVEE` (plus `BLENDER_EEVEE_NEXT`).
- Éclairage : 40 à 50 W par lumière de surface à 1,4 m suffisent pour un objet d'un mètre ; à 180 W, une peinture anthracite sort gris clair.
- Un métal nu en `metallic` 1 reflète le fond sombre et paraît noir : l'usure se lit mieux à `metallic` 0,6.
- Voronoï « distance au bord » dessine un réseau de fissures, pas des rayures : le kit tire une tranche fine orientée au hasard par cellule.
- Un écaillage à grande échelle mange les petites lettres : `echelle_eclats` vers 150 sur les marquages.
- Booléen exact : `use_self` (`auto=True`) dès que la pièce réunit des volumes qui se chevauchent ; sans, le résultat change d'un lancement à l'autre (vu sur le châssis du FP-12 : 290 ou 322 triangles). Même avec, **l'ordre des sommets** peut changer d'une construction à l'autre, pas la forme : comparer les formes (`construire_armes.py --empreinte`, deux fois), jamais l'ordre des sommets.
- Une fonction qui crée ses objets par `a.objet` les inscrit déjà dans `a.objets` : n'y ajoute pas ce qu'elle rend (`a.objets += ca.poignee_pistolet(a)` comptait la poignée deux fois).
- `ca.arguments(ARGS)` : une liste, même vide, veut dire « rien à rendre ni à enregistrer » (c'est ce que passe `construire_armes`) ; seul `None` (le script lancé par Blender) lit la ligne de commande. Sinon chaque arme relirait les options de `construire_armes`.
- Un décalque (`quad`, `disque`) se pose à 0,2 ou 0,3 mm de sa surface, comme `texte` ; sur un flanc qui s'évase, calculer la position de la surface aux deux bouts du décalque. Un décalque se donne toujours sa `normale=` (Unity ne dessine pas l'envers d'une face).
- Pas de vide entre la carcasse et le garde-main, ni de canon nu pour le combler : le garde-main rejoint la carcasse (une section de jonction à la largeur du châssis, puis le loft vers sa propre section) et le rail du dessus court d'un seul tenant de l'une à l'autre (retour de l'utilisateur sur le FP-12, 2026-10-15).
- `sys.dont_write_bytecode = True` avant d'importer le kit (pas de `__pycache__` dans le projet) ; `preferences.filepaths.save_version = 0` en arrière-plan (pas de `.blend1`).
- Windows refuse de supprimer un fichier encore ouvert : fermer avant `os.remove`.
- **`Image.save()` à un chemin relatif n'enregistre rien, sans erreur** (vécu le 2026-10-16 : les occlusions des accessoires n'étaient jamais parties, les FBX et les fiches si). Les exports rendent `--sortie` absolu, et `cuire_occlusion` vérifie que le fichier existe.
- Une émission rouge forte vire au rose saumon sous AgX : rouge saturé, force basse (1,3 pour une lueur, 3 pour une lentille).
- Une collection instanciée (la planche) se décale de la position de sa racine : régler `instance_offset` sur elle.
- Plusieurs armes dans une même session : ne retirer que la scène d'usine (« Scene »), jamais les autres scènes.
- Une nouvelle marque d'usure tire son hasard d'un second générateur (`graine + 7919`) : si elle puisait dans le premier, elle décalerait les tirages des usures existantes, et toutes les armes changeraient d'aspect. Contrôle : un rendu sans variante comparé au pixel près à celui d'avant.
- Corriger un script par un heredoc Bash casse sur les apostrophes du français (« fin de fichier inattendue ») : écrire le correctif dans un fichier Python du scratchpad (remplacements exacts, `assert s.count(a) == 1`, `ast.parse` avant d'écrire), puis le lancer avec `python -I`.
