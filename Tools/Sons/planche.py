# Planche de spectrogrammes : une ligne par son (enveloppe en dB + spectrogramme), pour comparer
# des candidats à l'œil. Usage : planche.py sortie.png titre fichier1 [fichier2 ...]
# Un argument "@liste.txt" lit les chemins dans un fichier (un par ligne).
import os
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
import soundfile as sf

out = sys.argv[1]
title = sys.argv[2]
files = []
for a in sys.argv[3:]:
    if a.startswith("@"):
        files += [l.strip() for l in open(a[1:], encoding="utf-8") if l.strip()]
    else:
        files.append(a)

n = len(files)
fig, axes = plt.subplots(n, 2, figsize=(15, 1.55 * n + 0.6), gridspec_kw={"width_ratios": [1, 2.2]})
if n == 1:
    axes = np.array([axes])
fig.suptitle(title, fontsize=11)
tmax = 0.0
datas = []
for f in files:
    d, sr = sf.read(f, dtype="float32", always_2d=True)
    m = d.mean(axis=1)
    datas.append((m, sr))
    tmax = max(tmax, len(m) / sr)
tmax = min(tmax, 4.0)
for i, (f, (m, sr)) in enumerate(zip(files, datas)):
    name = os.path.basename(f)
    win = max(1, int(0.005 * sr))
    k = len(m) // win
    env = np.sqrt(np.mean(m[: k * win].reshape(k, win) ** 2, axis=1)) if k > 0 else np.array([0.0])
    env_db = 20 * np.log10(np.maximum(env, 1e-6))
    t = np.arange(len(env_db)) * win / sr
    ax = axes[i, 0]
    ax.plot(t, env_db, lw=0.8, color="#d06020")
    ax.set_ylim(-70, 0)
    ax.set_xlim(0, tmax)
    ax.grid(alpha=0.3)
    ax.set_title(f"{i + 1}. {name[:60]}", fontsize=8, loc="left")
    ax.tick_params(labelsize=7)
    ax2 = axes[i, 1]
    seg = m[: int(tmax * sr)]
    if len(seg) < 512:
        seg = np.pad(seg, (0, 512 - len(seg)))
    ax2.specgram(seg, NFFT=1024, Fs=sr, noverlap=768, cmap="magma", vmin=-120, vmax=-20)
    ax2.set_ylim(0, min(16000, sr / 2))
    ax2.set_xlim(0, tmax)
    ax2.tick_params(labelsize=7)
plt.tight_layout()
plt.savefig(out, dpi=72)
print("ok", out)
