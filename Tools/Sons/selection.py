# Sélection des sons de la banque pour Duel Arena (2026-10-13).
# Chaque famille : dossier de destination (relatif à SoundsBank), liste (nom de sortie, source),
# options de traitement. Les sources sont relatives à la racine « Bank Sounds ».
GM = "Gamemaster Audio - Pro Sound Collection v1.3 - 16bit 48k"
GUNS = GM + "/Guns_Weapons/Guns"
BULLETS = GM + "/Guns_Weapons/Bullets"
GB = "GameBurp - 2000 Game Sound FX/GameBurp - 2000 Game Sound FX Collection (WAV)"
MECH = GB + "/MECHS - MACHINES - SERVOS - (103)"
MM = "Mini_Moves_Pack1/WAVE"
UI = GM + "/User_Interface_Menu"
# Options d'une famille : keep_start (début gardé), max_len (coupe, s), fade_ms (fondu de fin),
# raw (copie telle quelle : une boucle ne doit être ni rognée ni fondue).


def seq(pattern, numbers, out_pattern):
    return [(out_pattern.format(i + 1), pattern.format(n)) for i, n in enumerate(numbers)]


FAMILIES = [
    # ---------------------------------------------------------------- Fusil d'assaut
    # Tir automatique façon Gamemaster : premier coup, coups secs, queue jouée avec chaque coup.
    # Le calage dans le temps compte (la queue démarre avec le coup) : rien n'est rogné au début,
    # et la queue reste dans la même famille que les coups pour garder son niveau relatif (-22 dB).
    # Un coup sec finit volontairement net (-30 dB) : la queue prend le relais. Fondu de 4 ms seulement.
    dict(dest="Armes/FusilAssaut", keep_start=True, fade_ms=4, items=
         [("Tir_Premier", GUNS + "/gun_semi_auto_rifle_shot_00_first_01.wav")]
         + seq(GUNS + "/gun_semi_auto_rifle_shot_{:02d}.wav", range(1, 9), "Tir_{:02d}")
         + [("Tir_Queue", GUNS + "/gun_semi_auto_rifle_shot_00_tail_only_01.wav")]),
    dict(dest="Armes/FusilAssaut", items=[
        ("Rech_Sortie", GUNS + "/gun_semi_auto_rifle_magazine_unload_01.wav"),
        ("Rech_Insertion", GUNS + "/gun_semi_auto_rifle_magazine_load_01.wav"),
        ("Rech_Armement", GUNS + "/gun_semi_auto_rifle_cock_02.wav"),
    ]),
    # ---------------------------------------------------------------- Sniper
    dict(dest="Armes/Sniper", items=seq(GUNS + "/gun_rifle_sniper_shot_{:02d}.wav", [1, 2, 3, 4], "Tir_{:02d}")),
    dict(dest="Armes/Sniper", items=seq(GUNS + "/gun_rifle_sniper_cock_{:02d}.wav", [1, 3, 4], "Culasse_{:02d}")),
    dict(dest="Armes/Sniper", items=[
        ("Rech_Sortie", GUNS + "/gun_rifle_magazine_unload_01.wav"),
        ("Rech_Insertion", GUNS + "/gun_rifle_magazine_load_02.wav"),
        ("Rech_Culasse", GUNS + "/gun_rifle_sniper_cock_02.wav"),
    ]),
    # ---------------------------------------------------------------- Fusil à pompe
    dict(dest="Armes/Pompe", items=seq(GUNS + "/gun_shotgun_shot_{:02d}.wav", [1, 2, 3, 4], "Tir_{:02d}")),
    dict(dest="Armes/Pompe", items=seq(GUNS + "/gun_shotgun_cock_{:02d}.wav", [2, 3, 4, 5], "Pompe_{:02d}")),
    dict(dest="Armes/Pompe", items=seq(GUNS + "/gun_shotgun_load_bullet_{:02d}.wav", range(1, 9), "Cartouche_{:02d}")),
    dict(dest="Armes/Pompe", items=[("Rech_Fin", GUNS + "/gun_shotgun_cock_01.wav")]),
    # ---------------------------------------------------------------- Raygun
    dict(dest="Armes/Raygun", items=seq(GM + "/Sci-Fi Weapons/sci-fi_weapon_plasma_pistol_{:02d}.wav", [1, 3, 4, 6], "Tir_{:02d}")),
    dict(dest="Armes/Raygun", items=[("Rech", GM + "/Sci-Fi Weapons/sci-fi_weapon_reload_01.wav")]),
    # ---------------------------------------------------------------- Taser
    dict(dest="Armes/Taser", items=seq(GM + "/Whooshes/whoosh_swish_med_fast_{:02d}.wav", [1, 2, 3, 4], "Elan_{:02d}")),
    dict(dest="Armes/Taser", items=seq(GM + "/Guns_Weapons/Taser/taser_stun_gun_zap_electricity_{:02d}.wav", [1, 2, 3, 4], "Decharge_{:02d}")),
    # ---------------------------------------------------------------- Robot : pas et postures
    dict(dest="Robot", items=seq(GM + "/Footsteps/footstep_metal_low_walk_{:02d}.wav", range(1, 17), "Pas_Marche_{:02d}")),
    dict(dest="Robot", items=seq(GM + "/Footsteps/footstep_metal_low_run_{:02d}.wav", range(1, 13), "Pas_Course_{:02d}")),
    dict(dest="Robot", items=seq(GM + "/Footsteps/footstep_metal_high_walk_{:02d}.wav", range(1, 11), "Pas_Accroupi_{:02d}")),
    dict(dest="Robot", items=seq(MM + "/35_Foley_Rough-Surface/Rough_Surface_Foley_MM_P1-{:02d}.wav",
                                 [1, 2, 3, 5, 7, 8, 10, 12, 14, 16, 18, 20], "Ramper_{:02d}")),
    # Le lean est rapide (0,06 s) et fréquent : un servo court et sec, coupé à 0,32 s.
    dict(dest="Robot", max_len=0.32, fade_ms=90, items=seq(MECH + "/MECH Servo Motor Rev Small {:02d}.wav", [1, 5, 9], "Lean_{:02d}")),
    dict(dest="Robot", items=seq(MECH + "/MECH Servo Motor Movement Stomp Small {:02d}.wav", [1, 2], "Accroupir_{:02d}")),
    dict(dest="Robot", items=seq(MECH + "/MECH Servo Motor Rev Small {:02d}.wav", [7, 8, 10], "Relever_{:02d}")),
    dict(dest="Robot", items=seq(MECH + "/MECH Servo Motor Movement Stomp {:02d}.wav", [1, 11], "Allonger_{:02d}")),
    dict(dest="Robot", items=seq(MECH + "/MECH Servo Motor Movement Stomp {:02d}.wav", [5, 9], "Lever_{:02d}")),
    # ---------------------------------------------------------------- Tribune
    dict(dest="Tribune", items=seq(GM + "/Whooshes/whoosh_swish_med_{:02d}.wav", range(1, 6), "Poing_Elan_{:02d}")),
    dict(dest="Tribune", items=seq(GB + "/IMPACTS - HITS - (50)/IMPACT Metal Hit Short {:02d}.wav", range(1, 6), "Poing_Impact_{:02d}")),
    dict(dest="Tribune", items=seq(GB + "/ELECTRIC - ZAPS - SHOCKS - (21)/ELECTRIC Power Down {:02d}.wav", [1, 2, 3], "Etourdi_{:02d}")),
    dict(dest="Tribune", items=seq(GM + "/Impacts_Smashable/rock_impact_small_hit_{:02d}.wav", [1, 2, 3], "Bloc_Coup_{:02d}")),
    dict(dest="Tribune", items=seq(GM + "/Impacts_Smashable/rock_smashable_hit_impact_large_{:02d}.wav", [1, 2, 3], "Bloc_Casse_{:02d}")),
    dict(dest="Tribune", items=seq(GM + "/Foley/foley_object_grab_pickup_{:02d}.wav", range(1, 6), "Ramasser_{:02d}")),
    dict(dest="Tribune", items=seq(GM + "/Whooshes/whoosh_swish_high_big_{:02d}.wav", range(1, 6), "Lancer_{:02d}")),
    dict(dest="Tribune", items=seq(GM + "/Impacts_Smashable/rock_smashable_hit_impact_{:02d}.wav", [1, 2, 3], "Caillou_Impact_{:02d}")),
    dict(dest="Tribune", items=seq(GM + "/Metal/metal_lid_movement_impact_{:02d}.wav", [1, 2, 3, 4, 5, 6, 7, 8], "Objet_Chute_{:02d}")),
    dict(dest="Tribune", items=[
        ("Arme_Prise_01", GUNS + "/gun_rifle_grab_pickup_01.wav"),
        ("Arme_Prise_02", GUNS + "/gun_shotgun_pickup_01.wav"),
        ("Arme_Prise_03", GUNS + "/gun_shotgun_pickup_02.wav"),
    ]),

    # ================================================================ Les sons qui manquaient (2026-10-13)
    # ---------------------------------------------------------------- Où arrive le tir, l'arme qu'on sort
    dict(dest="Armes/Commun", items=seq(BULLETS + "/bullet_impact_concrete_brick_{:02d}.wav", range(1, 7), "Impact_Decor_{:02d}")),
    dict(dest="Armes/Commun", items=seq(BULLETS + "/bullet_impact_metal_light_{:02d}.wav", range(1, 6), "Impact_Robot_{:02d}")),
    dict(dest="Armes/Commun", items=seq(BULLETS + "/bullet_impact_metal_heavy_{:02d}.wav", range(1, 5), "Impact_Robot_Lourd_{:02d}")),
    dict(dest="Armes/Commun", items=seq(BULLETS + "/bullet_flyby_fast_{:02d}.wav", range(1, 7), "Sifflement_{:02d}")),
    dict(dest="Armes/Commun", items=seq(GUNS + "/gun_pistol_general_handling_{:02d}.wav", [5, 6, 7, 8], "Sortie_{:02d}")),
    dict(dest="Armes/FusilAssaut", items=seq(GUNS + "/gun_rifle_dry_fire_{:02d}.wav", [1, 2, 3], "Vide_{:02d}")),
    dict(dest="Armes/Sniper", items=seq(GUNS + "/gun_rifle_sniper_dry_fire_{:02d}.wav", [1, 2], "Vide_{:02d}")),
    dict(dest="Armes/Pompe", items=seq(GUNS + "/gun_shotgun_dry_fire_{:02d}.wav", [1, 2], "Vide_{:02d}")),
    dict(dest="Armes/Raygun", items=[("Vide_01", GB + "/WEAPONS - BULLET SHELLS - CLICKS - (66)/WEAPON CLICK Electric Mechanism 02.wav")]),
    dict(dest="Armes/Raygun", items=seq(GB + "/ELECTRIC - ZAPS - SHOCKS - (21)/ELECTRIC Shock Zap Short {:02d}.wav", [2, 4, 7], "Impact_{:02d}")),
    # ---------------------------------------------------------------- Retours de touche et de dégâts
    dict(dest="Retours", items=seq(GB + "/IMPACTS - HITS - (50)/IMPACT Hit Short Clink {:02d}.wav", [1, 2], "Touche_{:02d}")),
    dict(dest="Retours", items=[("Tete_01", GM + "/Metal/metal_small_impact_01.wav")]),
    dict(dest="Retours", items=seq(GB + "/TECH INTERFACE - COMPUTERS - (121)/TECH INTERFACE Industrial Hit Short {:02d}.wav", [1, 2], "Kill_{:02d}")),
    dict(dest="Retours", items=seq(GM + "/Electricity_Hums/radio_tv_electronic_static_{:02d}.wav", range(14, 19), "Parasites_{:02d}")),
    # ---------------------------------------------------------------- La mort, franchir, retomber
    dict(dest="Robot", items=[
        ("Mort_Extinction_01", MECH + "/MECH Servo Motor Power Down 01.wav"),
        ("Mort_Extinction_02", GM + "/Sci-Fi/sci-fi_power_down_01.wav"),
    ]),
    dict(dest="Robot", items=[("Mort_CourtCircuit_01", GM + "/Sci-Fi/sci-fi_electric_pulse_power_down_01.wav")]),
    dict(dest="Robot", items=seq(GM + "/Metal/metal_tin_impacts_hit_hard_{:02d}.wav", range(1, 7), "Epave_Choc_{:02d}")),
    dict(dest="Robot", items=seq(GM + "/Foley/foley_soldier_gear_equipment_rattle_movement_med_{:02d}.wav", range(1, 5), "Franchir_{:02d}")),
    dict(dest="Robot", items=seq(GM + "/Footsteps/footstep_metal_land_{:02d}.wav", range(1, 7), "Atterrir_{:02d}")),
    # ---------------------------------------------------------------- Le déroulé de la manche
    dict(dest="Manche", items=[("Decompte", GM + "/Alarms_Beeps_Siren/beep_01.wav")]),
    dict(dest="Manche", items=[("Depart", GB + "/ALARMS - BUZZERS - RINGERS - (44)/ALARM Alert Buzzer Short 01.wav")]),
    dict(dest="Manche", items=[("Fin", GM + "/Cinematic Sounds/cinematic_deep_boom_impact_01.wav")]),
    dict(dest="Manche", items=[("Gagnee", GB + "/TECH CHARGERS - (45)/TECH CHARGER Power Up Short 01.wav")]),
    dict(dest="Manche", items=[
        ("Reconstruction_Cliquet", GM + "/Switches_Buttons_Gears_Levers/gear_drill_windup_05.wav"),
        ("Reconstruction_Perceuse", GM + "/Switches_Buttons_Gears_Levers/gear_drill_turn_02.wav"),
        ("Reconstruction_Reveil", MECH + "/MECH Servo Motor Power Up Long 02.wav"),
    ]),
    dict(dest="Manche", raw=True, items=[("Ambiance_Salle", GM + "/Backgrounds/background_room_interior_hum_loop_01.wav")]),
    # ---------------------------------------------------------------- Interface
    dict(dest="Interface", items=[("Survol", UI + "/ui_button_simple_click_01.wav")]),
    dict(dest="Interface", items=[("Clic", UI + "/ui_menu_button_click_05.wav")]),
    dict(dest="Interface", items=[("Valider", UI + "/ui_menu_button_confirm_02.wav")]),
    dict(dest="Interface", items=[("Retour", UI + "/ui_menu_button_cancel_01.wav")]),
    dict(dest="Interface", items=[("Choix", GUNS + "/gun_semi_auto_rifle_safety_switch_01.wav")]),
]
