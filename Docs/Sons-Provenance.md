# Provenance des sons du jeu

Généré par `Tools/Sons/preparer.py` (2026-10-13) : chaque fichier de `Assets/_ProjectArt/Audio/SoundsBank/`
vient de la banque « Bank Sounds » de l'utilisateur (dossier Téléchargements), converti en mono, rogné,
fondu en fin si besoin, et ramené à une crête commune par famille (-1 dBFS). Les choix sont dans
`Tools/Sons/selection.py`.

## Licences (relevé du 2026-10-13)

| Pack | Utilisé pour | Licence trouvée dans la banque |
|---|---|---|
| Gamemaster Audio, *Pro Sound Collection* v1.3 | armes, impacts et sifflements des balles, pas, franchir et retomber, épave, extinction du robot, parasites, décompte, reconstruction, ambiance de la salle, interface, tribune (coups, blocs, objets) | Aucun fichier de licence. Le guide du pack dit que le crédit n'est **pas obligatoire** (« appréciée »). Bibliothèque commerciale libre de droits : garder la preuve d'achat. |
| GameBurp, *2000 Game Sound FX* | servomoteurs du robot, impacts métalliques, touche et mort confirmées, départ et manche gagnée, impacts et détente à vide du raygun, étourdissement | Aucun fichier de licence. Bibliothèque commerciale libre de droits : garder la preuve d'achat. |
| *Mini Moves Pack 1* | ramper (frottements sur surface rugueuse) | Aucun fichier de licence dans son dossier. Le seul contrat de la banque est celui d'Epic Stock Media (pack *Anime Character Vocalizations*) : libre de droits, sans attribution, **un seul utilisateur**, fichiers bruts jamais transmis à un tiers. À vérifier selon d'où vient ce pack. |

Packs non retenus, faute de licence connue : *Inventory SFX Bundle*, *Medieval Fantasy SFX Pack*,
*Western SFX Pack*, *Space SFX Pack* (MP3). Ce qui vaut pour tous : les sons voyagent dans le jeu, jamais
en fichiers bruts. Le dépôt GitHub est privé ; s'il devenait public ou partagé, les `.wav` de
`SoundsBank/` y seraient des fichiers bruts.

## Musique (`Assets/_ProjectArt/Audio/Musique/`, téléchargée le 2026-10-13)

Morceaux de Pixabay, copiés tels quels (MP3 stéréo 44,1 kHz). **Licence Pixabay** (Content License) :
usage gratuit, commercial compris, sans attribution obligatoire ; interdit de les revendre ou de les
distribuer seuls (dans un jeu, ils voyagent avec lui : permis). Choisis parmi les morceaux **faits par des
humains** (filtre « authentic » : rien à déclarer à Steam sur ce point) et **sans « Content ID Registered »**
(sinon, une vidéo ou un stream du jeu peut recevoir une réclamation). Ces deux mentions sont celles du
2026-10-13 : un auteur peut inscrire son morceau à Content ID plus tard ; revérifier les pages avant la sortie.

| Fichier | Morceau, auteur | Page |
|---|---|---|
| `Menu_Welc0me_IndustrielSombre.mp3` | *190501 Dark Industrial Ambient Mechanical Electronica*, welc0mei0 | https://pixabay.com/music/electronic-190501-dark-industrial-ambient-mechanical-electronica-155502/ |
| `Preparation_Welc0me_MystereIndustriel.mp3` | *240910 Dark Mystery Industrial Gothic*, welc0mei0 | https://pixabay.com/music/beats-240910-dark-mystery-industrial-gothic-game-anime-video-239360/ |
| `Victoire_MondaMusic_PhonkDrift.mp3` | *Phonk Drift*, mondamusic | https://pixabay.com/music/phonk-phonk-phonk-drift-529550/ |
| `Victoire_Kulakovka_PhonkReel.mp3` | *Phonk Reel*, kulakovka | https://pixabay.com/music/phonk-phonk-reel-614877/ |

`Manche/Ambiance_Salle.wav` (bourdonnement d'une salle, Gamemaster) est copiée telle quelle, en stéréo :
c'est une boucle, rognée ou fondue elle ne bouclerait plus sans couture.

| Fichier | Source | Traitement |
|---|---|---|
| `Armes/FusilAssaut/Tir_Premier.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_shot_00_first_01.wav` | moyenne, fondu 4 ms, gain -1.0 dB |
| `Armes/FusilAssaut/Tir_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_shot_01.wav` | moyenne, fondu 4 ms, gain -1.0 dB |
| `Armes/FusilAssaut/Tir_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_shot_02.wav` | moyenne, fondu 4 ms, gain -1.0 dB |
| `Armes/FusilAssaut/Tir_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_shot_03.wav` | moyenne, fondu 4 ms, gain -1.0 dB |
| `Armes/FusilAssaut/Tir_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_shot_04.wav` | moyenne, fondu 4 ms, gain -1.0 dB |
| `Armes/FusilAssaut/Tir_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_shot_05.wav` | moyenne, fondu 4 ms, gain -1.0 dB |
| `Armes/FusilAssaut/Tir_06.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_shot_06.wav` | moyenne, fondu 4 ms, gain -1.0 dB |
| `Armes/FusilAssaut/Tir_07.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_shot_07.wav` | moyenne, fondu 4 ms, gain -1.0 dB |
| `Armes/FusilAssaut/Tir_08.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_shot_08.wav` | moyenne, fondu 4 ms, gain -1.0 dB |
| `Armes/FusilAssaut/Tir_Queue.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_shot_00_tail_only_01.wav` | moyenne, fin rognée 86 ms, fondu 4 ms, gain -1.0 dB |
| `Armes/FusilAssaut/Rech_Sortie.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_magazine_unload_01.wav` | moyenne, gain -1.0 dB |
| `Armes/FusilAssaut/Rech_Insertion.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_magazine_load_01.wav` | moyenne, gain -1.0 dB |
| `Armes/FusilAssaut/Rech_Armement.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_cock_02.wav` | moyenne, gain -1.0 dB |
| `Armes/Sniper/Tir_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_sniper_shot_01.wav` | moyenne, fin rognée 619 ms, gain -1.0 dB |
| `Armes/Sniper/Tir_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_sniper_shot_02.wav` | moyenne, fin rognée 655 ms, gain -1.0 dB |
| `Armes/Sniper/Tir_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_sniper_shot_03.wav` | moyenne, fin rognée 620 ms, gain -1.0 dB |
| `Armes/Sniper/Tir_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_sniper_shot_04.wav` | moyenne, fin rognée 653 ms, gain -1.0 dB |
| `Armes/Sniper/Culasse_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_sniper_cock_01.wav` | moyenne, fin rognée 63 ms, gain -1.0 dB |
| `Armes/Sniper/Culasse_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_sniper_cock_03.wav` | moyenne, fin rognée 44 ms, gain -1.0 dB |
| `Armes/Sniper/Culasse_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_sniper_cock_04.wav` | moyenne, gain -1.0 dB |
| `Armes/Sniper/Rech_Sortie.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_magazine_unload_01.wav` | moyenne, fin rognée 26 ms, gain -1.0 dB |
| `Armes/Sniper/Rech_Insertion.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_magazine_load_02.wav` | moyenne, gain -1.0 dB |
| `Armes/Sniper/Rech_Culasse.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_sniper_cock_02.wav` | moyenne, gain -1.0 dB |
| `Armes/Pompe/Tir_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_shot_01.wav` | moyenne, fin rognée 567 ms, gain -1.0 dB |
| `Armes/Pompe/Tir_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_shot_02.wav` | moyenne, fin rognée 437 ms, gain -1.0 dB |
| `Armes/Pompe/Tir_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_shot_03.wav` | moyenne, fin rognée 528 ms, gain -1.0 dB |
| `Armes/Pompe/Tir_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_shot_04.wav` | moyenne, fin rognée 54 ms, gain -1.0 dB |
| `Armes/Pompe/Pompe_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_cock_02.wav` | moyenne, fin rognée 62 ms, gain -1.0 dB |
| `Armes/Pompe/Pompe_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_cock_03.wav` | moyenne, fin rognée 215 ms, gain -1.0 dB |
| `Armes/Pompe/Pompe_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_cock_04.wav` | moyenne, fin rognée 85 ms, gain -1.0 dB |
| `Armes/Pompe/Pompe_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_cock_05.wav` | moyenne, fin rognée 147 ms, gain -1.0 dB |
| `Armes/Pompe/Cartouche_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_load_bullet_01.wav` | moyenne, fin rognée 59 ms, gain -0.7 dB |
| `Armes/Pompe/Cartouche_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_load_bullet_02.wav` | moyenne, fin rognée 145 ms, gain -0.7 dB |
| `Armes/Pompe/Cartouche_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_load_bullet_03.wav` | moyenne, fin rognée 57 ms, gain -0.7 dB |
| `Armes/Pompe/Cartouche_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_load_bullet_04.wav` | moyenne, fin rognée 77 ms, gain -0.7 dB |
| `Armes/Pompe/Cartouche_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_load_bullet_05.wav` | moyenne, gain -0.7 dB |
| `Armes/Pompe/Cartouche_06.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_load_bullet_06.wav` | moyenne, fin rognée 92 ms, gain -0.7 dB |
| `Armes/Pompe/Cartouche_07.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_load_bullet_07.wav` | moyenne, fin rognée 66 ms, gain -0.7 dB |
| `Armes/Pompe/Cartouche_08.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_load_bullet_08.wav` | moyenne, fin rognée 89 ms, gain -0.7 dB |
| `Armes/Pompe/Rech_Fin.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_cock_01.wav` | moyenne, fin rognée 129 ms, gain -1.0 dB |
| `Armes/Raygun/Tir_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Sci-Fi Weapons/sci-fi_weapon_plasma_pistol_01.wav` | moyenne, fin rognée 57 ms, gain -1.0 dB |
| `Armes/Raygun/Tir_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Sci-Fi Weapons/sci-fi_weapon_plasma_pistol_03.wav` | moyenne, fin rognée 191 ms, gain -1.0 dB |
| `Armes/Raygun/Tir_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Sci-Fi Weapons/sci-fi_weapon_plasma_pistol_04.wav` | moyenne, fin rognée 42 ms, gain -1.0 dB |
| `Armes/Raygun/Tir_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Sci-Fi Weapons/sci-fi_weapon_plasma_pistol_06.wav` | moyenne, début rogné 0 ms, fin rognée 124 ms, gain -1.0 dB |
| `Armes/Raygun/Rech.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Sci-Fi Weapons/sci-fi_weapon_reload_01.wav` | moyenne, fin rognée 21 ms, gain -1.0 dB |
| `Armes/Taser/Elan_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_med_fast_01.wav` | moyenne, début rogné 0 ms, gain +3.9 dB |
| `Armes/Taser/Elan_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_med_fast_02.wav` | moyenne, gain +3.9 dB |
| `Armes/Taser/Elan_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_med_fast_03.wav` | moyenne, gain +3.9 dB |
| `Armes/Taser/Elan_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_med_fast_04.wav` | moyenne, gain +3.9 dB |
| `Armes/Taser/Decharge_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Taser/taser_stun_gun_zap_electricity_01.wav` | moyenne, début rogné 2 ms, fin rognée 26 ms, gain -0.4 dB |
| `Armes/Taser/Decharge_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Taser/taser_stun_gun_zap_electricity_02.wav` | moyenne, début rogné 1 ms, fin rognée 12 ms, gain -0.4 dB |
| `Armes/Taser/Decharge_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Taser/taser_stun_gun_zap_electricity_03.wav` | moyenne, gain -0.4 dB |
| `Armes/Taser/Decharge_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Taser/taser_stun_gun_zap_electricity_04.wav` | moyenne, gain -0.4 dB |
| `Robot/Pas_Marche_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_01.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_02.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_03.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_04.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_05.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_06.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_06.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_07.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_07.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_08.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_08.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_09.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_09.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_10.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_10.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_11.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_11.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_12.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_12.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_13.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_13.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_14.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_14.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_15.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_15.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Marche_16.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_walk_16.wav` | moyenne, gain +0.3 dB |
| `Robot/Pas_Course_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_run_01.wav` | moyenne, gain +0.5 dB |
| `Robot/Pas_Course_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_run_02.wav` | moyenne, gain +0.5 dB |
| `Robot/Pas_Course_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_run_03.wav` | moyenne, gain +0.5 dB |
| `Robot/Pas_Course_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_run_04.wav` | moyenne, gain +0.5 dB |
| `Robot/Pas_Course_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_run_05.wav` | moyenne, gain +0.5 dB |
| `Robot/Pas_Course_06.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_run_06.wav` | moyenne, gain +0.5 dB |
| `Robot/Pas_Course_07.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_run_07.wav` | moyenne, gain +0.5 dB |
| `Robot/Pas_Course_08.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_run_08.wav` | moyenne, gain +0.5 dB |
| `Robot/Pas_Course_09.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_run_09.wav` | moyenne, gain +0.5 dB |
| `Robot/Pas_Course_10.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_run_10.wav` | moyenne, gain +0.5 dB |
| `Robot/Pas_Course_11.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_run_11.wav` | moyenne, gain +0.5 dB |
| `Robot/Pas_Course_12.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_low_run_12.wav` | moyenne, gain +0.5 dB |
| `Robot/Pas_Accroupi_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_high_walk_01.wav` | moyenne, gain +0.4 dB |
| `Robot/Pas_Accroupi_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_high_walk_02.wav` | moyenne, fin rognée 11 ms, gain +0.4 dB |
| `Robot/Pas_Accroupi_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_high_walk_03.wav` | moyenne, fin rognée 40 ms, gain +0.4 dB |
| `Robot/Pas_Accroupi_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_high_walk_04.wav` | moyenne, fin rognée 42 ms, gain +0.4 dB |
| `Robot/Pas_Accroupi_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_high_walk_05.wav` | moyenne, gain +0.4 dB |
| `Robot/Pas_Accroupi_06.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_high_walk_06.wav` | moyenne, fin rognée 67 ms, gain +0.4 dB |
| `Robot/Pas_Accroupi_07.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_high_walk_07.wav` | moyenne, gain +0.4 dB |
| `Robot/Pas_Accroupi_08.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_high_walk_08.wav` | moyenne, fin rognée 24 ms, gain +0.4 dB |
| `Robot/Pas_Accroupi_09.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_high_walk_09.wav` | moyenne, fin rognée 54 ms, gain +0.4 dB |
| `Robot/Pas_Accroupi_10.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_high_walk_10.wav` | moyenne, fin rognée 14 ms, gain +0.4 dB |
| `Robot/Ramper_01.wav` | `Mini_Moves_Pack1/WAVE/35_Foley_Rough-Surface/Rough_Surface_Foley_MM_P1-01.wav` | moyenne, fin rognée 64 ms, gain +14.0 dB |
| `Robot/Ramper_02.wav` | `Mini_Moves_Pack1/WAVE/35_Foley_Rough-Surface/Rough_Surface_Foley_MM_P1-02.wav` | moyenne, fin rognée 69 ms, gain +14.0 dB |
| `Robot/Ramper_03.wav` | `Mini_Moves_Pack1/WAVE/35_Foley_Rough-Surface/Rough_Surface_Foley_MM_P1-03.wav` | moyenne, fin rognée 22 ms, gain +14.0 dB |
| `Robot/Ramper_04.wav` | `Mini_Moves_Pack1/WAVE/35_Foley_Rough-Surface/Rough_Surface_Foley_MM_P1-05.wav` | moyenne, fin rognée 54 ms, gain +14.0 dB |
| `Robot/Ramper_05.wav` | `Mini_Moves_Pack1/WAVE/35_Foley_Rough-Surface/Rough_Surface_Foley_MM_P1-07.wav` | moyenne, fin rognée 28 ms, gain +14.0 dB |
| `Robot/Ramper_06.wav` | `Mini_Moves_Pack1/WAVE/35_Foley_Rough-Surface/Rough_Surface_Foley_MM_P1-08.wav` | moyenne, fin rognée 58 ms, gain +14.0 dB |
| `Robot/Ramper_07.wav` | `Mini_Moves_Pack1/WAVE/35_Foley_Rough-Surface/Rough_Surface_Foley_MM_P1-10.wav` | moyenne, fin rognée 58 ms, gain +14.0 dB |
| `Robot/Ramper_08.wav` | `Mini_Moves_Pack1/WAVE/35_Foley_Rough-Surface/Rough_Surface_Foley_MM_P1-12.wav` | moyenne, fin rognée 70 ms, gain +14.0 dB |
| `Robot/Ramper_09.wav` | `Mini_Moves_Pack1/WAVE/35_Foley_Rough-Surface/Rough_Surface_Foley_MM_P1-14.wav` | moyenne, fin rognée 50 ms, gain +14.0 dB |
| `Robot/Ramper_10.wav` | `Mini_Moves_Pack1/WAVE/35_Foley_Rough-Surface/Rough_Surface_Foley_MM_P1-16.wav` | moyenne, fin rognée 42 ms, gain +14.0 dB |
| `Robot/Ramper_11.wav` | `Mini_Moves_Pack1/WAVE/35_Foley_Rough-Surface/Rough_Surface_Foley_MM_P1-18.wav` | moyenne, fin rognée 47 ms, gain +14.0 dB |
| `Robot/Ramper_12.wav` | `Mini_Moves_Pack1/WAVE/35_Foley_Rough-Surface/Rough_Surface_Foley_MM_P1-20.wav` | moyenne, fin rognée 66 ms, gain +14.0 dB |
| `Robot/Lean_01.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Rev Small 01.wav` | moyenne, coupé à 0.32 s, fondu 90 ms, gain +0.8 dB |
| `Robot/Lean_02.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Rev Small 05.wav` | moyenne, coupé à 0.32 s, fondu 90 ms, gain +0.8 dB |
| `Robot/Lean_03.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Rev Small 09.wav` | moyenne, coupé à 0.32 s, fondu 90 ms, gain +0.8 dB |
| `Robot/Accroupir_01.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Movement Stomp Small 01.wav` | moyenne, gain +0.5 dB |
| `Robot/Accroupir_02.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Movement Stomp Small 02.wav` | moyenne, fondu 40 ms, gain +0.5 dB |
| `Robot/Relever_01.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Rev Small 07.wav` | moyenne, fondu 40 ms, gain +0.5 dB |
| `Robot/Relever_02.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Rev Small 08.wav` | moyenne, fondu 40 ms, gain +0.5 dB |
| `Robot/Relever_03.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Rev Small 10.wav` | moyenne, gain +0.5 dB |
| `Robot/Allonger_01.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Movement Stomp 01.wav` | moyenne, gain +0.5 dB |
| `Robot/Allonger_02.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Movement Stomp 11.wav` | moyenne, gain +0.5 dB |
| `Robot/Lever_01.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Movement Stomp 05.wav` | moyenne, gain +0.5 dB |
| `Robot/Lever_02.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Movement Stomp 09.wav` | moyenne, gain +0.5 dB |
| `Tribune/Poing_Elan_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_med_01.wav` | canal 1, début rogné 1 ms, fin rognée 7 ms, gain +0.0 dB |
| `Tribune/Poing_Elan_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_med_02.wav` | moyenne, gain +0.0 dB |
| `Tribune/Poing_Elan_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_med_03.wav` | moyenne, gain +0.0 dB |
| `Tribune/Poing_Elan_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_med_04.wav` | canal 1, gain +0.0 dB |
| `Tribune/Poing_Elan_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_med_05.wav` | moyenne, gain +0.0 dB |
| `Tribune/Poing_Impact_01.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/IMPACTS - HITS - (50)/IMPACT Metal Hit Short 01.wav` | moyenne, gain +0.7 dB |
| `Tribune/Poing_Impact_02.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/IMPACTS - HITS - (50)/IMPACT Metal Hit Short 02.wav` | moyenne, gain +0.7 dB |
| `Tribune/Poing_Impact_03.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/IMPACTS - HITS - (50)/IMPACT Metal Hit Short 03.wav` | moyenne, gain +0.7 dB |
| `Tribune/Poing_Impact_04.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/IMPACTS - HITS - (50)/IMPACT Metal Hit Short 04.wav` | moyenne, gain +0.7 dB |
| `Tribune/Poing_Impact_05.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/IMPACTS - HITS - (50)/IMPACT Metal Hit Short 05.wav` | moyenne, gain +0.7 dB |
| `Tribune/Etourdi_01.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/ELECTRIC - ZAPS - SHOCKS - (21)/ELECTRIC Power Down 01.wav` | moyenne, gain +0.6 dB |
| `Tribune/Etourdi_02.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/ELECTRIC - ZAPS - SHOCKS - (21)/ELECTRIC Power Down 02.wav` | moyenne, gain +0.6 dB |
| `Tribune/Etourdi_03.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/ELECTRIC - ZAPS - SHOCKS - (21)/ELECTRIC Power Down 03.wav` | moyenne, fondu 40 ms, gain +0.6 dB |
| `Tribune/Bloc_Coup_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Impacts_Smashable/rock_impact_small_hit_01.wav` | moyenne, gain -0.9 dB |
| `Tribune/Bloc_Coup_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Impacts_Smashable/rock_impact_small_hit_02.wav` | moyenne, fin rognée 41 ms, gain -0.9 dB |
| `Tribune/Bloc_Coup_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Impacts_Smashable/rock_impact_small_hit_03.wav` | moyenne, gain -0.9 dB |
| `Tribune/Bloc_Casse_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Impacts_Smashable/rock_smashable_hit_impact_large_01.wav` | moyenne, fin rognée 28 ms, gain -0.8 dB |
| `Tribune/Bloc_Casse_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Impacts_Smashable/rock_smashable_hit_impact_large_02.wav` | moyenne, fin rognée 278 ms, gain -0.8 dB |
| `Tribune/Bloc_Casse_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Impacts_Smashable/rock_smashable_hit_impact_large_03.wav` | moyenne, fin rognée 124 ms, gain -0.8 dB |
| `Tribune/Ramasser_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Foley/foley_object_grab_pickup_01.wav` | moyenne, fin rognée 41 ms, gain -0.0 dB |
| `Tribune/Ramasser_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Foley/foley_object_grab_pickup_02.wav` | moyenne, fin rognée 12 ms, gain -0.0 dB |
| `Tribune/Ramasser_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Foley/foley_object_grab_pickup_03.wav` | moyenne, fin rognée 12 ms, gain -0.0 dB |
| `Tribune/Ramasser_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Foley/foley_object_grab_pickup_04.wav` | moyenne, fin rognée 37 ms, gain -0.0 dB |
| `Tribune/Ramasser_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Foley/foley_object_grab_pickup_05.wav` | moyenne, gain -0.0 dB |
| `Tribune/Lancer_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_high_big_01.wav` | moyenne, début rogné 1 ms, gain +0.2 dB |
| `Tribune/Lancer_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_high_big_02.wav` | moyenne, gain +0.2 dB |
| `Tribune/Lancer_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_high_big_03.wav` | moyenne, gain +0.2 dB |
| `Tribune/Lancer_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_high_big_04.wav` | moyenne, gain +0.2 dB |
| `Tribune/Lancer_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Whooshes/whoosh_swish_high_big_05.wav` | moyenne, gain +0.2 dB |
| `Tribune/Caillou_Impact_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Impacts_Smashable/rock_smashable_hit_impact_01.wav` | moyenne, fin rognée 51 ms, gain -0.5 dB |
| `Tribune/Caillou_Impact_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Impacts_Smashable/rock_smashable_hit_impact_02.wav` | moyenne, fin rognée 67 ms, gain -0.5 dB |
| `Tribune/Caillou_Impact_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Impacts_Smashable/rock_smashable_hit_impact_03.wav` | moyenne, fin rognée 204 ms, gain -0.5 dB |
| `Tribune/Objet_Chute_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_lid_movement_impact_01.wav` | moyenne, fin rognée 35 ms, gain -0.8 dB |
| `Tribune/Objet_Chute_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_lid_movement_impact_02.wav` | moyenne, début rogné 4 ms, fin rognée 34 ms, gain -0.8 dB |
| `Tribune/Objet_Chute_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_lid_movement_impact_03.wav` | moyenne, fin rognée 82 ms, gain -0.8 dB |
| `Tribune/Objet_Chute_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_lid_movement_impact_04.wav` | moyenne, fin rognée 485 ms, gain -0.8 dB |
| `Tribune/Objet_Chute_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_lid_movement_impact_05.wav` | moyenne, fin rognée 187 ms, gain -0.8 dB |
| `Tribune/Objet_Chute_06.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_lid_movement_impact_06.wav` | moyenne, fin rognée 176 ms, gain -0.8 dB |
| `Tribune/Objet_Chute_07.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_lid_movement_impact_07.wav` | moyenne, fin rognée 160 ms, gain -0.8 dB |
| `Tribune/Objet_Chute_08.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_lid_movement_impact_08.wav` | moyenne, fin rognée 6 ms, gain -0.8 dB |
| `Tribune/Arme_Prise_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_grab_pickup_01.wav` | moyenne, gain -1.0 dB |
| `Tribune/Arme_Prise_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_pickup_01.wav` | moyenne, gain -1.0 dB |
| `Tribune/Arme_Prise_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_pickup_02.wav` | moyenne, fin rognée 11 ms, gain -1.0 dB |
| `Armes/Commun/Impact_Decor_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_concrete_brick_01.wav` | moyenne, fin rognée 27 ms, gain -1.0 dB |
| `Armes/Commun/Impact_Decor_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_concrete_brick_02.wav` | moyenne, début rogné 3 ms, fin rognée 17 ms, gain -1.0 dB |
| `Armes/Commun/Impact_Decor_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_concrete_brick_03.wav` | moyenne, début rogné 1 ms, fin rognée 18 ms, gain -1.0 dB |
| `Armes/Commun/Impact_Decor_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_concrete_brick_04.wav` | moyenne, gain -1.0 dB |
| `Armes/Commun/Impact_Decor_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_concrete_brick_05.wav` | moyenne, début rogné 0 ms, fin rognée 16 ms, gain -1.0 dB |
| `Armes/Commun/Impact_Decor_06.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_concrete_brick_06.wav` | moyenne, début rogné 0 ms, gain -1.0 dB |
| `Armes/Commun/Impact_Robot_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_metal_light_01.wav` | moyenne, gain -1.0 dB |
| `Armes/Commun/Impact_Robot_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_metal_light_02.wav` | moyenne, fin rognée 37 ms, gain -1.0 dB |
| `Armes/Commun/Impact_Robot_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_metal_light_03.wav` | moyenne, fin rognée 10 ms, gain -1.0 dB |
| `Armes/Commun/Impact_Robot_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_metal_light_04.wav` | moyenne, fin rognée 115 ms, gain -1.0 dB |
| `Armes/Commun/Impact_Robot_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_metal_light_05.wav` | moyenne, gain -1.0 dB |
| `Armes/Commun/Impact_Robot_Lourd_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_metal_heavy_01.wav` | moyenne, fin rognée 28 ms, gain -1.0 dB |
| `Armes/Commun/Impact_Robot_Lourd_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_metal_heavy_02.wav` | moyenne, fin rognée 29 ms, gain -1.0 dB |
| `Armes/Commun/Impact_Robot_Lourd_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_metal_heavy_03.wav` | moyenne, fin rognée 159 ms, gain -1.0 dB |
| `Armes/Commun/Impact_Robot_Lourd_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_impact_metal_heavy_04.wav` | moyenne, gain -1.0 dB |
| `Armes/Commun/Sifflement_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_flyby_fast_01.wav` | moyenne, gain +1.6 dB |
| `Armes/Commun/Sifflement_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_flyby_fast_02.wav` | moyenne, début rogné 12 ms, gain +1.6 dB |
| `Armes/Commun/Sifflement_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_flyby_fast_03.wav` | moyenne, début rogné 2 ms, gain +1.6 dB |
| `Armes/Commun/Sifflement_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_flyby_fast_04.wav` | moyenne, début rogné 67 ms, gain +1.6 dB |
| `Armes/Commun/Sifflement_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_flyby_fast_05.wav` | moyenne, début rogné 37 ms, fin rognée 15 ms, gain +1.6 dB |
| `Armes/Commun/Sifflement_06.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Bullets/bullet_flyby_fast_06.wav` | moyenne, début rogné 16 ms, fin rognée 20 ms, gain +1.6 dB |
| `Armes/Commun/Sortie_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_pistol_general_handling_05.wav` | moyenne, gain -1.0 dB |
| `Armes/Commun/Sortie_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_pistol_general_handling_06.wav` | moyenne, gain -1.0 dB |
| `Armes/Commun/Sortie_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_pistol_general_handling_07.wav` | moyenne, début rogné 6 ms, fin rognée 18 ms, gain -1.0 dB |
| `Armes/Commun/Sortie_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_pistol_general_handling_08.wav` | moyenne, gain -1.0 dB |
| `Armes/FusilAssaut/Vide_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_dry_fire_01.wav` | moyenne, fin rognée 25 ms, gain -1.0 dB |
| `Armes/FusilAssaut/Vide_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_dry_fire_02.wav` | moyenne, gain -1.0 dB |
| `Armes/FusilAssaut/Vide_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_dry_fire_03.wav` | moyenne, gain -1.0 dB |
| `Armes/Sniper/Vide_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_sniper_dry_fire_01.wav` | moyenne, fin rognée 21 ms, gain -1.0 dB |
| `Armes/Sniper/Vide_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_rifle_sniper_dry_fire_02.wav` | moyenne, fin rognée 31 ms, gain -1.0 dB |
| `Armes/Pompe/Vide_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_dry_fire_01.wav` | moyenne, fin rognée 44 ms, gain -1.0 dB |
| `Armes/Pompe/Vide_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_shotgun_dry_fire_02.wav` | moyenne, fin rognée 77 ms, gain -1.0 dB |
| `Armes/Raygun/Vide_01.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/WEAPONS - BULLET SHELLS - CLICKS - (66)/WEAPON CLICK Electric Mechanism 02.wav` | moyenne, gain +0.9 dB |
| `Armes/Raygun/Impact_01.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/ELECTRIC - ZAPS - SHOCKS - (21)/ELECTRIC Shock Zap Short 02.wav` | moyenne, fondu 40 ms, gain +0.5 dB |
| `Armes/Raygun/Impact_02.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/ELECTRIC - ZAPS - SHOCKS - (21)/ELECTRIC Shock Zap Short 04.wav` | moyenne, fondu 40 ms, gain +0.5 dB |
| `Armes/Raygun/Impact_03.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/ELECTRIC - ZAPS - SHOCKS - (21)/ELECTRIC Shock Zap Short 07.wav` | moyenne, gain +0.5 dB |
| `Retours/Touche_01.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/IMPACTS - HITS - (50)/IMPACT Hit Short Clink 01.wav` | moyenne, gain +0.6 dB |
| `Retours/Touche_02.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/IMPACTS - HITS - (50)/IMPACT Hit Short Clink 02.wav` | moyenne, gain +0.6 dB |
| `Retours/Tete_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_small_impact_01.wav` | moyenne, fin rognée 9 ms, gain +0.7 dB |
| `Retours/Kill_01.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/TECH INTERFACE - COMPUTERS - (121)/TECH INTERFACE Industrial Hit Short 01.wav` | moyenne, gain +0.5 dB |
| `Retours/Kill_02.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/TECH INTERFACE - COMPUTERS - (121)/TECH INTERFACE Industrial Hit Short 02.wav` | moyenne, fondu 40 ms, gain +0.5 dB |
| `Retours/Parasites_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Electricity_Hums/radio_tv_electronic_static_14.wav` | moyenne, gain +1.2 dB |
| `Retours/Parasites_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Electricity_Hums/radio_tv_electronic_static_15.wav` | moyenne, gain +1.2 dB |
| `Retours/Parasites_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Electricity_Hums/radio_tv_electronic_static_16.wav` | moyenne, fondu 40 ms, gain +1.2 dB |
| `Retours/Parasites_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Electricity_Hums/radio_tv_electronic_static_17.wav` | moyenne, gain +1.2 dB |
| `Retours/Parasites_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Electricity_Hums/radio_tv_electronic_static_18.wav` | moyenne, fondu 40 ms, gain +1.2 dB |
| `Robot/Mort_Extinction_01.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Power Down 01.wav` | moyenne, gain +1.1 dB |
| `Robot/Mort_Extinction_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Sci-Fi/sci-fi_power_down_01.wav` | moyenne, fondu 40 ms, gain +1.1 dB |
| `Robot/Mort_CourtCircuit_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Sci-Fi/sci-fi_electric_pulse_power_down_01.wav` | moyenne, gain -0.3 dB |
| `Robot/Epave_Choc_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_tin_impacts_hit_hard_01.wav` | moyenne, fin rognée 8 ms, gain +0.0 dB |
| `Robot/Epave_Choc_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_tin_impacts_hit_hard_02.wav` | moyenne, fin rognée 9 ms, gain +0.0 dB |
| `Robot/Epave_Choc_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_tin_impacts_hit_hard_03.wav` | moyenne, fin rognée 53 ms, gain +0.0 dB |
| `Robot/Epave_Choc_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_tin_impacts_hit_hard_04.wav` | moyenne, fin rognée 55 ms, gain +0.0 dB |
| `Robot/Epave_Choc_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_tin_impacts_hit_hard_05.wav` | moyenne, gain +0.0 dB |
| `Robot/Epave_Choc_06.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Metal/metal_tin_impacts_hit_hard_06.wav` | moyenne, fin rognée 8 ms, gain +0.0 dB |
| `Robot/Franchir_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Foley/foley_soldier_gear_equipment_rattle_movement_med_01.wav` | moyenne, début rogné 3 ms, gain +0.2 dB |
| `Robot/Franchir_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Foley/foley_soldier_gear_equipment_rattle_movement_med_02.wav` | moyenne, début rogné 12 ms, gain +0.2 dB |
| `Robot/Franchir_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Foley/foley_soldier_gear_equipment_rattle_movement_med_03.wav` | moyenne, début rogné 6 ms, gain +0.2 dB |
| `Robot/Franchir_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Foley/foley_soldier_gear_equipment_rattle_movement_med_04.wav` | moyenne, gain +0.2 dB |
| `Robot/Atterrir_01.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_land_01.wav` | moyenne, gain +0.5 dB |
| `Robot/Atterrir_02.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_land_02.wav` | moyenne, fin rognée 6 ms, gain +0.5 dB |
| `Robot/Atterrir_03.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_land_03.wav` | moyenne, gain +0.5 dB |
| `Robot/Atterrir_04.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_land_04.wav` | moyenne, gain +0.5 dB |
| `Robot/Atterrir_05.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_land_05.wav` | moyenne, début rogné 0 ms, gain +0.5 dB |
| `Robot/Atterrir_06.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Footsteps/footstep_metal_land_06.wav` | moyenne, gain +0.5 dB |
| `Manche/Decompte.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Alarms_Beeps_Siren/beep_01.wav` | moyenne, gain +2.1 dB |
| `Manche/Depart.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/ALARMS - BUZZERS - RINGERS - (44)/ALARM Alert Buzzer Short 01.wav` | moyenne, fondu 40 ms, gain +0.7 dB |
| `Manche/Fin.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Cinematic Sounds/cinematic_deep_boom_impact_01.wav` | moyenne, fin rognée 114 ms, gain +0.2 dB |
| `Manche/Gagnee.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/TECH CHARGERS - (45)/TECH CHARGER Power Up Short 01.wav` | moyenne, gain +2.0 dB |
| `Manche/Reconstruction_Cliquet.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Switches_Buttons_Gears_Levers/gear_drill_windup_05.wav` | moyenne, fin rognée 67 ms, gain +0.5 dB |
| `Manche/Reconstruction_Perceuse.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Switches_Buttons_Gears_Levers/gear_drill_turn_02.wav` | moyenne, gain +0.5 dB |
| `Manche/Reconstruction_Reveil.wav` | `GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)/MECHS - MACHINES - SERVOS - (103)/MECH Servo Motor Power Up Long 02.wav` | moyenne, gain +0.5 dB |
| `Manche/Ambiance_Salle.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Backgrounds/background_room_interior_hum_loop_01.wav` | copie telle quelle (boucle) |
| `Interface/Survol.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/User_Interface_Menu/ui_button_simple_click_01.wav` | moyenne, fin rognée 15 ms, gain +0.4 dB |
| `Interface/Clic.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/User_Interface_Menu/ui_menu_button_click_05.wav` | moyenne, fin rognée 17 ms, gain +0.1 dB |
| `Interface/Valider.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/User_Interface_Menu/ui_menu_button_confirm_02.wav` | moyenne, gain +3.1 dB |
| `Interface/Retour.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/User_Interface_Menu/ui_menu_button_cancel_01.wav` | moyenne, gain +8.6 dB |
| `Interface/Choix.wav` | `Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k/Guns_Weapons/Guns/gun_semi_auto_rifle_safety_switch_01.wav` | moyenne, gain -0.5 dB |
