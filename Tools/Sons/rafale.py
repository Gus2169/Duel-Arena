# Simule hors ligne une rafale du fusil d'assaut telle que WeaponAudio la jouera : premier coup,
# coups au hasard, queue relancée à chaque coup avec un fondu de la précédente. Sert à vérifier à
# l'œil (spectrogramme) qu'il n'y a ni trou ni empilement. Usage : rafale.py <dossier FusilAssaut> <sortie.wav> <coups> <cadence/s> <fondu s>
import os
import sys

import numpy as np
import soundfile as sf

folder, out, n, rate, fade = sys.argv[1], sys.argv[2], int(sys.argv[3]), float(sys.argv[4]), float(sys.argv[5])
first, sr = sf.read(os.path.join(folder, "Tir_Premier.wav"))
shots = [sf.read(os.path.join(folder, f"Tir_{i:02d}.wav"))[0] for i in range(1, 9)]
tail = sf.read(os.path.join(folder, "Tir_Queue.wav"))[0]
interval = 1.0 / rate
length = int((n * interval + len(tail) / sr + 0.2) * sr)
mix = np.zeros(length)
rng = np.random.default_rng(1)
prev_tail_start = None
for k in range(n):
    t0 = int(k * interval * sr)
    shot = first if k == 0 else shots[rng.integers(0, 8)]
    mix[t0:t0 + len(shot)] += shot
    # La queue précédente s'éteint en `fade` secondes à partir de ce coup.
    if prev_tail_start is not None:
        nf = int(fade * sr)
        seg_start = t0 - prev_tail_start
        env = np.ones(len(tail))
        env[seg_start:seg_start + nf] = np.linspace(1, 0, min(nf, len(tail) - seg_start))
        env[seg_start + nf:] = 0
        mix[prev_tail_start:prev_tail_start + len(tail)] -= tail * (1 - env)
    mix[t0:t0 + len(tail)] += tail
    prev_tail_start = t0
peak = np.max(np.abs(mix))
sf.write(out, mix / peak * 0.89, sr, subtype="PCM_16")
print("ok", out, f"crête avant normalisation {20 * np.log10(peak):.1f} dB")
