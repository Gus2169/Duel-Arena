"""Banque de sons de Duel Arena, entièrement synthétisée (aucun échantillon tiers).

Direction : prototypes de soldats robotisés, châssis métal peint / plastique industriel /
caoutchouc, complexe militaire souterrain en béton (bible § 5, 10 et 13). Ton sérieux, jamais
cartoon. Tous les robots font les mêmes bruits (GDD § 8).

Usage : python3 -I make_sounds.py <dossier SoundsBank>
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sfxlib import (SR, Gen, add_at, bp, crush, env_ar, env_exp, fade, glide, hp, loop_crossfade, lp, metal,
                    modal, n_of, normalize, peak, reverb, rms_normalize, room_ir, sat, saw,
                    sine, square, sweep_lp, trim_silence, tv, write)

OUT = sys.argv[1]
MADE = []


def save(folder, name, x, peak_db=-1.0, rms=None, loop=False, trim_db=-55, lowpass=None):
    d = os.path.join(OUT, folder)
    os.makedirs(d, exist_ok=True)
    # Une boucle arrive ici déjà filtrée et fondue sur elle-même : un filtre appliqué après
    # coup repartirait d'un état nul au début du fichier, et la couture claquerait.
    if not loop:
        x = hp(x, 25)                    # pas de composante continue
        if lowpass:
            x = lp(x, lowpass, 4)
        x = fade(trim_silence(x, trim_db, pad=0.03), 0.0005, 0.03)
    x = rms_normalize(x, rms) if rms is not None else normalize(x, peak_db)
    p = os.path.join(d, name + ".wav")
    write(p, x)
    MADE.append(p)


# Salle de référence : grande salle de test en béton, sous terre.
HALL = room_ir(Gen(7), rt_low=1.7, rt_mid=1.2, rt_high=0.55, length=1.8)
SMALL = room_ir(Gen(8), rt_low=0.5, rt_mid=0.4, rt_high=0.2, length=0.6,
                early=(0.004, 0.009, 0.013), predelay=0.002)


# =================================================================== robot : déplacements
def footstep(g, kind):
    """Pied métallique à semelle caoutchouc sur béton : choc sourd, frottement granuleux, petit
    tintement du châssis et sifflement bref de l'actionneur de la jambe."""
    n = n_of(0.32)
    y = np.zeros(n)
    hard = {"walk": 1.0, "run": 1.5, "crouch": 0.55}[kind]

    def impact(t0, amp):
        m = n_of(0.12)
        f = glide(g.uniform(105, 125), g.uniform(52, 62), m)
        thump = sine(f) * env_exp(m, 0.022 * (1.2 if kind == "run" else 1.0))
        scuff = bp(g.white(m), 250, 2600) * env_exp(m, 0.010)
        grit = bp(g.white(m), 3000, 7000) * env_exp(m, 0.004) * 0.15
        clink = metal(g, m, g.uniform(380, 460), count=6, spread=(1.0, 8.0), decay=0.06) * 0.22
        hit = thump * 1.0 + scuff * 0.55 * hard + grit * hard + clink * hard
        add_at(y, hit * amp, t0)

    if kind == "run":
        impact(0.0, 1.0)
        add_at(y, bp(g.white(n_of(0.06)), 400, 3000) * env_exp(n_of(0.06), 0.02) * 0.18, 0.03)  # glissé
    else:
        impact(0.0, 1.0)                                  # talon
        impact(g.uniform(0.028, 0.042), 0.55)            # pointe
    # actionneur : très discret, mais c'est lui qui dit « robot »
    m = n_of(0.07)
    whine = sine(glide(g.uniform(1700, 1900), g.uniform(2200, 2500), m)) * env_ar(m, 0.01, 0.05) * 0.04
    add_at(y, whine, g.uniform(0.0, 0.02))
    if kind == "crouch":
        y = lp(y, 2200)
    y = sat(y * 1.2, 1.5)
    return reverb(y, SMALL, wet=0.08)


def crawl(g):
    """Plaques de blindage qui raclent le béton : bruit granuleux modulé, quelques cliquetis."""
    n = n_of(0.5)
    t = tv(n)
    grains = np.zeros(n)
    for _ in range(int(g.uniform(10, 16))):
        c, w = g.uniform(0.04, 0.42), g.uniform(0.015, 0.05)
        grains += g.uniform(0.4, 1.0) * np.exp(-0.5 * ((t - c) / w) ** 2)
    scrape = bp(g.white(n), 700, 4200) * grains
    rumble = lp(g.brown(n), 260) * grains * 0.5
    ticks = np.zeros(n)
    for _ in range(int(g.uniform(2, 5))):
        m = n_of(0.05)
        add_at(ticks, metal(g, m, g.uniform(1400, 2400), count=4, decay=0.015), g.uniform(0.02, 0.45), 0.25)
    y = scrape * 0.6 + rumble + ticks
    return reverb(lp(y, 5000), SMALL, wet=0.06)


def servo(g, f0, f1, dur, strain=0.0, gear=14.0):
    """Moteur d'actionneur : fondamentale qui monte ou descend, harmoniques, grain des engrenages."""
    n = n_of(dur)
    f = glide(f0, f1, n, "ease") * (1 + 0.004 * np.sin(2 * np.pi * 9 * tv(n)))
    tone = sine(f) + 0.45 * sine(2 * f) + 0.22 * sine(3 * f) + (0.25 + strain) * sine(f * gear / 4)
    gear_noise = bp(g.white(n), 1500, 6000) * (0.5 + 0.5 * np.sign(np.sin(phase(f * gear / 8))))
    env = env_ar(n, min(0.04, dur * 0.25), min(0.08, dur * 0.4))
    y = (tone * 0.6 + gear_noise * (0.18 + strain * 0.3)) * env
    if strain > 0:
        y *= 1 + strain * 0.5 * np.sin(2 * np.pi * 23 * tv(n))
    return bp(y, 250, 7000)


def phase(f):
    return 2 * np.pi * np.cumsum(f) / SR


def clank(g, heavy=1.0, base=None):
    m = n_of(0.35)
    y = metal(g, m, base or g.uniform(260, 340), count=8, spread=(1.0, 7.0), decay=0.09 * heavy) * 0.8
    y += sine(glide(95, 55, m)) * env_exp(m, 0.03) * heavy
    y += bp(g.white(m), 300, 3000) * env_exp(m, 0.008) * 0.6
    return y


def hiss(g, dur, lo=2500, hi=9000):
    n = n_of(dur)
    return bp(g.white(n), lo, hi) * env_ar(n, 0.01, dur * 0.8) * np.exp(-tv(n) / (dur * 0.5))


def make_robot():
    for i in range(4):
        save("Robot", f"Robot_Step_Walk_{i + 1:02d}", footstep(Gen(100 + i), "walk"), trim_db=-48, lowpass=7000)
        save("Robot", f"Robot_Step_Run_{i + 1:02d}", footstep(Gen(200 + i), "run"), trim_db=-48, lowpass=8000)
    for i in range(3):
        save("Robot", f"Robot_Step_Crouch_{i + 1:02d}", footstep(Gen(300 + i), "crouch"), trim_db=-48)
        save("Robot", f"Robot_Crawl_{i + 1:02d}", crawl(Gen(400 + i)), trim_db=-48)

    for i in range(2):
        g = Gen(500 + i)
        y = servo(g, g.uniform(850, 950), g.uniform(1400, 1600), 0.2)
        save("Robot", f"Robot_Lean_Start_{i + 1:02d}", reverb(y, SMALL, 0.05), peak_db=-3)
        g = Gen(510 + i)
        y = np.zeros(n_of(0.26))
        add_at(y, servo(g, g.uniform(1350, 1500), g.uniform(780, 860), 0.18), 0)
        add_at(y, metal(g, n_of(0.08), g.uniform(2200, 2800), count=4, decay=0.012), 0.17, 0.35)
        save("Robot", f"Robot_Lean_End_{i + 1:02d}", reverb(y, SMALL, 0.05), peak_db=-3)

    g = Gen(520)
    y = np.zeros(n_of(0.55))
    add_at(y, servo(g, 1150, 600, 0.3), 0)
    add_at(y, hiss(g, 0.25), 0.05, 0.25)
    add_at(y, clank(g, 0.8), 0.28, 0.9)
    save("Robot", "Robot_Crouch_Down", reverb(y, SMALL, 0.08))

    g = Gen(521)
    y = np.zeros(n_of(0.5))
    add_at(y, servo(g, 620, 1250, 0.3, strain=0.15), 0)
    add_at(y, hiss(g, 0.18, 3000, 10000), 0.02, 0.35)
    add_at(y, metal(g, n_of(0.1), 2600, count=4, decay=0.015), 0.29, 0.4)
    save("Robot", "Robot_Crouch_Up", reverb(y, SMALL, 0.08))

    g = Gen(522)
    y = np.zeros(n_of(1.2))
    add_at(y, servo(g, 1100, 700, 0.35), 0)
    add_at(y, servo(g, 900, 480, 0.4, strain=0.1), 0.3)
    add_at(y, clank(g, 1.3, 230), 0.62, 1.0)
    add_at(y, clank(g, 0.7, 340), 0.78, 0.55)
    add_at(y, crawl(g)[: n_of(0.25)] * 0.4, 0.66)
    add_at(y, hiss(g, 0.3), 0.08, 0.2)
    save("Robot", "Robot_Prone_Down", reverb(y, SMALL, 0.1))

    g = Gen(523)
    y = np.zeros(n_of(1.1))
    add_at(y, servo(g, 480, 950, 0.45, strain=0.3), 0)
    add_at(y, servo(g, 760, 1300, 0.35, strain=0.15), 0.4)
    add_at(y, hiss(g, 0.35, 2500, 9000), 0.1, 0.3)
    add_at(y, metal(g, n_of(0.1), 2400, count=4, decay=0.02), 0.78, 0.45)
    save("Robot", "Robot_Prone_Up", reverb(y, SMALL, 0.08))


# =================================================================== arme automatique
def smg_shot(g):
    """Pistolet-mitrailleur compact (MP5 ou équivalent robotique) dans une salle de béton :
    claquement, corps, coup de poing grave, culasse, puis la salle qui répond."""
    n = n_of(0.25)
    crack = hp(g.white(n), 1800) * env_exp(n, 0.0035)
    crack[:n_of(0.0012)] += g.r.uniform(-1, 1, n_of(0.0012)) * 2.5
    body = sat(bp(g.white(n), 350, 4200) * env_exp(n, 0.017) * 1.6, 2.5)
    body = peak(body, g.uniform(800, 1100), 1.2, 5)
    punch = sine(glide(g.uniform(150, 170), g.uniform(52, 58), n)) * env_exp(n, 0.034)
    bolt = np.zeros(n)
    add_at(bolt, metal(g, n_of(0.05), g.uniform(1900, 2300), count=5, spread=(1.0, 3.2), decay=0.012), g.uniform(0.012, 0.018))
    add_at(bolt, metal(g, n_of(0.04), g.uniform(2600, 3000), count=4, spread=(1.0, 2.6), decay=0.008), g.uniform(0.042, 0.05), 0.5)
    dry = crack * 0.9 + body * 0.8 + punch * 1.1 + bolt * 0.22
    dry = sat(dry * 1.3, 1.8)
    return trim_silence(reverb(dry, HALL, wet=0.32, damp=5500), -55)


def make_weapon():
    for i in range(4):
        save("Weapon", f"SMG_Shot_{i + 1:02d}", smg_shot(Gen(600 + i)))
    g = Gen(610)
    n = n_of(0.18)
    y = hp(g.white(n), 3000) * env_exp(n, 0.0015) * 0.8
    add_at(y, metal(g, n_of(0.12), 2900, count=5, spread=(1.0, 2.4), decay=0.02), 0.002, 0.8)
    add_at(y, metal(g, n_of(0.1), 1900, count=4, decay=0.015), 0.045, 0.4)
    save("Weapon", "SMG_DryFire", reverb(y, SMALL, 0.1), peak_db=-4)


# =================================================================== électricité (taser)
def arc_crack(g, strength=1.0):
    """Un claquement d'arc : front très raide, spectre centré sur 2-6 kHz, grésillement après."""
    n = n_of(0.03)
    burst = g.white(n) * env_exp(n, 0.0012 * strength + 0.0004)
    y = bp(burst, 1500, 9000) * 1.4 + bp(burst, 180, 600) * 0.5
    y += bp(g.white(n), 4500, 11000) * env_exp(n, 0.006) * 0.25 * strength
    y = peak(y, g.uniform(2800, 4200), 2.0, 6)
    return y * strength


def arc_buzz(g, n, base=110.0, level=1.0):
    """Le bourdon continu entre deux claquements : onde riche, filtrée, au volume tremblant."""
    t = tv(n)
    f = base * (1 + 0.01 * np.sin(2 * np.pi * 3.7 * t))
    tone = square(f, n, 0.3) * 0.6 + saw(f * 2.02, n) * 0.4
    jitter = lp(np.abs(g.white(n)), 60) * 3
    y = bp(tone, 300, 3200) * (0.4 + jitter)
    return y * level


def taser_pulses(g, n, rate, strength=1.0, jitter_ms=1.5, circular=False, start=0.0):
    y = np.zeros(n)
    period = 1.0 / rate
    k = 0
    while True:
        t0 = start + k * period + g.uniform(-jitter_ms, jitter_ms) / 1000
        if t0 >= n / SR:
            break
        c = arc_crack(g, strength * g.uniform(0.75, 1.0))
        if circular:
            idx = (n_of(max(0.0, t0)) + np.arange(len(c))) % n
            np.add.at(y, idx, c)
        else:
            add_at(y, c, max(0.0, t0))
        k += 1
    return y


def make_taser():
    # Boucle de 2,000 s, 36 claquements à exactement 18 par seconde : ElectricArc cale ses
    # impulsions visuelles sur floor(temps x 18).
    # 2,0 s est un multiple exact de la période (1/18 s) : les claquements de la zone fondue
    # tombent pile sur ceux du début, la boucle reste à 18 impulsions par seconde sans couture.
    g = Gen(700)
    xf = n_of(0.25)
    n = n_of(2.0) + xf
    pulses = taser_pulses(g, n, 18.0, 1.0, jitter_ms=1.0)
    buzz = arc_buzz(g, n, 112.0, 0.22)
    whine = sine(7200 + 40 * np.sin(2 * np.pi * 0.5 * tv(n)), None) * 0.012
    y = lp(hp(pulses + buzz + whine, 25), 11000, 4)
    y = loop_crossfade(y, xf)
    assert len(y) == n_of(2.0)
    save("Taser", "Taser_Idle_Loop", y, rms=-20, loop=True)

    g = Gen(701)
    n = n_of(0.7)
    y = np.zeros(n)
    add_at(y, metal(g, n_of(0.05), 3100, count=4, spread=(1.0, 2.0), decay=0.008), 0.0, 0.7)  # interrupteur
    m = n_of(0.45)
    charge = sine(glide(1200, 7400, m)) * np.linspace(0.05, 1, m) ** 1.5 * 0.18
    charge += sine(glide(2400, 14800, m)) * np.linspace(0.0, 1, m) ** 2 * 0.05
    add_at(y, charge, 0.03)
    for k, t0 in enumerate([0.47, 0.53, 0.575, 0.61, 0.64]):
        add_at(y, arc_crack(g, 0.6 + 0.1 * k), t0)
    add_at(y, arc_buzz(g, n_of(0.22), 112, 0.3) * env_ar(n_of(0.22), 0.02, 0.15), 0.47)
    save("Taser", "Taser_Draw", reverb(y, SMALL, 0.12))

    for i in range(3):
        g = Gen(710 + i)
        n = n_of(0.5)
        env = env_ar(n, 0.005, 0.25)
        y = taser_pulses(g, n_of(0.38), g.uniform(34, 44), 1.0)
        y = np.concatenate([y, np.zeros(n - len(y))]) * env
        y += arc_buzz(g, n, g.uniform(100, 125), 0.45) * env
        y += hp(g.white(n), 5000) * env * 0.08
        save("Taser", f"Taser_Zap_{i + 1:02d}", reverb(y, HALL, 0.12, damp=6000), lowpass=12000)

    for i in range(2):
        g = Gen(720 + i)
        n = n_of(1.0)
        y = np.zeros(n)
        # coup d'entrée : gros claquement + choc sourd sur le châssis
        big = arc_crack(g, 1.6)
        add_at(y, big * 1.4, 0)
        add_at(y, sine(glide(110, 45, n_of(0.15))) * env_exp(n_of(0.15), 0.05), 0, 1.0)
        # salve rapide pendant que la décharge traverse le robot
        m = n_of(0.5)
        burst_env = env_ar(m, 0.01, 0.2)
        salve = taser_pulses(g, m, g.uniform(48, 56), 1.2) * burst_env
        add_at(y, salve, 0.02)
        add_at(y, arc_buzz(g, m, 96, 0.7) * burst_env, 0.02)
        # le châssis qui résonne sous les impulsions
        ring = metal(g, n_of(0.8), g.uniform(170, 210), count=7, spread=(1.0, 7.5), decay=0.25) * 0.35
        add_at(y, ring, 0.0)
        # électronique qui grille
        fry = crush(bp(g.white(m), 600, 5000), bits=4, down=6) * burst_env * 0.25
        add_at(y, fry, 0.05)
        # dernier claquement et grésillement qui retombe
        add_at(y, arc_crack(g, 1.1), 0.55)
        add_at(y, hp(g.white(n_of(0.35)), 4000) * env_exp(n_of(0.35), 0.08) * 0.15, 0.56)
        y = sat(y, 1.6)
        save("Taser", f"Taser_Hit_{i + 1:02d}", reverb(y, HALL, 0.22, damp=6000), lowpass=13000)


# =================================================================== impacts de balles
def impact_concrete(g, ricochet):
    n = n_of(0.6)
    y = np.zeros(n)
    m = n_of(0.08)
    crack = bp(g.white(m), 900, 7000) * env_exp(m, 0.0028) * 1.2
    thud = lp(g.white(m), 700) * env_exp(m, 0.018) * 1.0
    thud += sine(glide(180, 90, m)) * env_exp(m, 0.02) * 0.5
    add_at(y, crack + thud, 0)
    # éclats de béton qui retombent
    for _ in range(int(g.uniform(8, 15))):
        t0 = g.uniform(0.01, 0.22) ** 1.3
        k = n_of(0.006)
        grain = hp(g.white(k), 2200) * env_exp(k, 0.0015)
        add_at(y, grain, t0, g.uniform(0.08, 0.3) * (1 - t0 * 2))
    add_at(y, bp(g.white(n_of(0.25)), 1500, 6000) * env_exp(n_of(0.25), 0.06) * 0.08, 0.01)
    if ricochet:
        m = n_of(0.3)
        f = glide(g.uniform(3400, 4200), g.uniform(1600, 2000), m) * (1 + 0.02 * np.sin(2 * np.pi * 38 * tv(m)))
        add_at(y, sine(f) * env_ar(m, 0.01, 0.25) * 0.12, 0.015)
    return reverb(y, HALL, 0.14, damp=5000)


def impact_chassis(g):
    n = n_of(0.55)
    y = np.zeros(n)
    m = n_of(0.45)
    ping = metal(g, m, g.uniform(900, 1300), count=8, spread=(1.0, 6.5), decay=g.uniform(0.09, 0.16), bright=0.85)
    thunk = metal(g, m, g.uniform(220, 300), count=4, spread=(1.0, 3.0), decay=0.05) * 0.8
    crack = bp(g.white(n_of(0.02)), 1500, 9000) * env_exp(n_of(0.02), 0.002) * 1.3
    add_at(y, ping * 0.7 + thunk, 0)
    add_at(y, crack, 0)
    # court-circuit : deux ou trois grésillements juste après
    for _ in range(int(g.uniform(1, 4))):
        add_at(y, arc_crack(g, 0.35), g.uniform(0.02, 0.09))
    return reverb(sat(y, 1.4), HALL, 0.12, damp=5000)


def make_impacts():
    for i in range(4):
        save("Impacts", f"Impact_Concrete_{i + 1:02d}", impact_concrete(Gen(800 + i), ricochet=(i == 2)))
        save("Impacts", f"Impact_Chassis_{i + 1:02d}", impact_chassis(Gen(810 + i)))


# =================================================================== retours de combat (2D)
def make_feedback():
    g = Gen(900)
    n = n_of(0.12)
    y = modal([3150, 4620, 6900], [0.022, 0.016, 0.01], [1.0, 0.6, 0.3], n, g)
    y += hp(g.white(n), 4000) * env_exp(n, 0.001) * 0.6
    save("Feedback", "Hit_Body", y, peak_db=-3)

    g = Gen(901)
    n = n_of(0.4)
    y = modal([2380, 5150, 7340, 9800], [0.16, 0.11, 0.07, 0.04], [1.0, 0.55, 0.35, 0.15], n, g)
    y += modal([3150], [0.02], [0.7], n, g)
    y += hp(g.white(n), 5000) * env_exp(n, 0.0012) * 0.7
    y += sine(glide(300, 160, n)) * env_exp(n, 0.02) * 0.3
    save("Feedback", "Hit_Head", y, peak_db=-2)

    g = Gen(902)
    n = n_of(0.42)
    y = np.zeros(n)
    for t0, f in [(0.0, 1180), (0.09, 790)]:
        m = n_of(0.08)
        beep = lp(square(f, m, 0.5), 3500) * env_ar(m, 0.003, 0.03)
        add_at(y, beep * 0.5, t0)
    add_at(y, crush(bp(g.white(n_of(0.3)), 800, 6000), 4, 8) * env_exp(n_of(0.3), 0.08) * 0.15, 0.02)
    add_at(y, modal([2380, 5150], [0.12, 0.08], [0.6, 0.3], n_of(0.3), g), 0.0, 0.5)
    save("Feedback", "Kill_Confirm", y, peak_db=-2)

    for i in range(3):
        g = Gen(910 + i)
        n = n_of(0.34)
        y = np.zeros(n)
        grain = crush(bp(g.white(n_of(0.018)), 400, 5000), bits=3, down=int(g.uniform(5, 9)))
        t0 = 0.0
        for k in range(int(g.uniform(4, 7))):
            add_at(y, grain * g.uniform(0.5, 1.0), t0)
            t0 += g.uniform(0.016, 0.03)
        add_at(y, crush(g.white(n_of(0.12)) * env_exp(n_of(0.12), 0.05), 4, 4) * 0.25, t0)
        add_at(y, sine(glide(120, 60, n_of(0.12))) * env_exp(n_of(0.12), 0.03) * 0.8, 0)
        add_at(y, arc_crack(g, 0.4), 0.01)
        save("Feedback", f"Damage_Glitch_{i + 1:02d}", y, peak_db=-2, lowpass=8000)

    # Mort : le châssis cède, l'électricité claque, le robot s'éteint et ses pièces retombent.
    g = Gen(920)
    n = n_of(2.4)
    y = np.zeros(n)
    add_at(y, arc_crack(g, 1.7) * 1.3, 0)
    add_at(y, clank(g, 2.0, 150), 0.0, 1.0)
    add_at(y, metal(g, n_of(1.0), 120, count=8, spread=(1.0, 9.0), decay=0.35) * 0.4, 0.0)
    m = n_of(1.5)
    f = glide(880, 55, m) * (1 + 0.03 * np.sin(2 * np.pi * glide(6, 14, m) * tv(m)))
    down = (sine(f) + 0.4 * sine(2 * f) + 0.2 * saw(f * 3, m)) * env_ar(m, 0.02, 0.6) * 0.3
    add_at(y, bp(down, 50, 5000), 0.05)
    for _ in range(9):
        add_at(y, arc_crack(g, g.uniform(0.3, 0.8)), g.uniform(0.05, 0.9))
    t0 = 0.35
    gap = 0.22
    for _ in range(int(g.uniform(9, 13))):
        add_at(y, metal(g, n_of(0.15), g.uniform(900, 2600), count=5, decay=0.04) * g.uniform(0.15, 0.45), t0)
        t0 += gap * g.uniform(0.6, 1.2)
        gap *= 0.8
    add_at(y, metal(g, n_of(0.08), 3000, count=3, spread=(1.0, 2.0), decay=0.01), 1.7, 0.35)  # dernier relais
    save("Feedback", "Death_Shutdown", reverb(sat(y, 1.3), HALL, 0.25, damp=5000), lowpass=14000)


# =================================================================== manche et ambiance
def make_round_and_ambience():
    g = Gen(1000)
    n = n_of(0.16)
    y = lp(square(880, n, 0.5), 3000) * env_ar(n, 0.003, 0.05)
    save("Round", "Round_Countdown_Beep", reverb(y, HALL, 0.15), peak_db=-4)

    g = Gen(1001)
    n = n_of(1.0)
    y = np.zeros(n)
    m = n_of(0.24)
    add_at(y, lp(square(1320, m, 0.5), 4000) * env_ar(m, 0.003, 0.06) * 0.6, 0)
    m = n_of(0.5)
    buzzer = bp(saw(220, m) + saw(223.5, m) + 0.5 * square(110, m), 150, 3000) * env_ar(m, 0.01, 0.1)
    add_at(y, buzzer * 0.5, 0.26)
    save("Round", "Round_Start", reverb(y, HALL, 0.25), peak_db=-3)

    g = Gen(1002)
    n = n_of(1.2)
    y = bp(saw(140, n) + saw(141.7, n) + 0.6 * square(70, n), 80, 2500) * env_ar(n, 0.01, 0.5)
    save("Round", "Round_End", reverb(y, HALL, 0.3), peak_db=-3)

    # Reconstruction des châssis pendant le fondu au noir (GDD § 5) : perceuses, servos, cliquets.
    g = Gen(1003)
    n = n_of(5.0)
    y = np.zeros(n)
    t = 0.1
    while t < 4.4:
        d = g.uniform(0.35, 0.9)
        m = n_of(d)
        f = g.uniform(180, 260) * (1 + 0.08 * np.sin(2 * np.pi * g.uniform(3, 7) * tv(m)))
        drill = bp(saw(f, m) + 0.5 * saw(f * 2.01, m), 200, 4000) * env_ar(m, 0.06, 0.12)
        drill += bp(g.white(m), 2000, 7000) * env_ar(m, 0.06, 0.12) * 0.3
        add_at(y, drill * 0.4, t)
        t += d + g.uniform(0.1, 0.4)
        if g.r.random() < 0.6:
            add_at(y, servo(g, g.uniform(500, 900), g.uniform(900, 1500), 0.3), t, 0.5)
        if g.r.random() < 0.5:
            for k in range(int(g.uniform(3, 7))):
                add_at(y, metal(g, n_of(0.04), 2600, count=3, decay=0.006) * 0.4, t + k * 0.055)
        if g.r.random() < 0.4:
            add_at(y, clank(g, 0.6), t + 0.2, 0.5)
    save("Round", "Reconstruction_Bed", reverb(y, HALL, 0.35), rms=-22)

    # Ambiance du complexe : ronflement électrique et ventilation, tout dans le grave pour ne
    # jamais masquer les pas (qui vivent au-dessus de 200 Hz).
    g = Gen(1004)
    n = n_of(12.0)
    t = tv(n)
    hum = sum(a * np.sin(2 * np.pi * f * t) for f, a in [(50, 1.0), (100, 0.5), (150, 0.25), (200, 0.12)])
    hum *= 0.9 + 0.1 * np.sin(2 * np.pi * (1 / 6.0) * t)
    air = lp(g.brown(n), 380) * (0.75 + 0.25 * np.sin(2 * np.pi * (1 / 4.0) * t + 1.0))
    y = lp(hp(hum * 0.25 + air * 0.6, 25), 600)
    y = loop_crossfade(y, n_of(1.0))
    save("Ambience", "Ambience_Underground_Loop", y, rms=-26, loop=True)


if __name__ == "__main__":
    make_robot()
    make_weapon()
    make_taser()
    make_impacts()
    make_feedback()
    make_round_and_ambience()
    for p in MADE:
        print(os.path.relpath(p, OUT))
