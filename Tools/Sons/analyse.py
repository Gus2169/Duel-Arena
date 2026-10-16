# Catalogue et mesure chaque son de la banque : durée, niveau, attaque, longueur utile,
# nombre de transitoires, timbre (centroïde, bandes, planéité). Les fichiers en double
# (OGG de GameBurp, versions 44,1 et 96 kHz des packs Epic Stock Media) sont écartés.
import csv
import math
import os
import sys
from multiprocessing import Pool

import numpy as np
import soundfile as sf

ROOT = sys.argv[1]
OUT = sys.argv[2]

SKIP_PARTS = (
    "Collection (OGG)",
    "44_1kHz-24bit",
    "44-1kHz-24bit",
    "96kHz-24bit",
)


def wanted(path):
    if not path.lower().endswith((".wav", ".mp3", ".ogg")):
        return False
    return not any(p in path for p in SKIP_PARTS)


def db(x):
    return 20.0 * math.log10(max(x, 1e-9))


def analyse(path):
    rel = os.path.relpath(path, ROOT)
    parts = rel.split(os.sep)
    row = {"path": rel, "pack": parts[0], "category": "/".join(parts[1:-1]), "name": parts[-1]}
    try:
        data, sr = sf.read(path, dtype="float32", always_2d=True)
    except Exception as e:  # fichier illisible : on le note et on continue
        row["error"] = str(e)[:80]
        return row
    ch = data.shape[1]
    mono = data.mean(axis=1)
    n = len(mono)
    row.update(sr=sr, ch=ch, dur=round(n / sr, 3))
    if n < 16:
        row["error"] = "vide"
        return row
    peak = float(np.max(np.abs(data)))
    row["peak_db"] = round(db(peak), 1)

    # Enveloppe RMS 10 ms, pas de 5 ms.
    win = max(1, int(0.010 * sr))
    hop = max(1, int(0.005 * sr))
    sq = mono.astype(np.float64) ** 2
    cs = np.concatenate([[0.0], np.cumsum(sq)])
    starts = np.arange(0, max(1, n - win), hop)
    env = np.sqrt((cs[starts + win] - cs[starts]) / win)
    env_db = 20 * np.log10(np.maximum(env, 1e-9))
    ep = float(env_db.max())
    t = starts / sr
    row["env_peak_db"] = round(ep, 1)
    above30 = np.where(env_db > ep - 30)[0]
    above40 = np.where(env_db > ep - 40)[0]
    above20 = np.where(env_db > ep - 20)[0]
    lead = float(t[above30[0]]) if len(above30) else 0.0
    end40 = float(t[above40[-1]] + 0.010) if len(above40) else row["dur"]
    end20 = float(t[above20[-1]] + 0.010) if len(above20) else row["dur"]
    ipk = int(np.argmax(env_db))
    row["lead"] = round(lead, 3)
    row["len40"] = round(end40 - lead, 3)
    row["len20"] = round(end20 - lead, 3)
    row["t_peak"] = round(float(t[ipk]) - lead, 3)
    act = env_db > ep - 30
    row["rms_act_db"] = round(db(math.sqrt(float(np.mean(env[act] ** 2)))) if act.any() else -120.0, 1)

    # Transitoires : maxima locaux qui montent d'au moins 9 dB en 60 ms, à 24 dB du maximum au plus.
    onsets = []
    look = max(1, int(0.060 / 0.005))
    for i in range(1, len(env_db) - 1):
        if env_db[i] < ep - 24:
            continue
        if env_db[i] >= env_db[i - 1] and env_db[i] >= env_db[i + 1]:
            lo = env_db[max(0, i - look):i].min() if i > 0 else env_db[i]
            if env_db[i] - lo >= 9:
                if not onsets or t[i] - onsets[-1] > 0.07:
                    onsets.append(float(t[i]))
    row["onsets"] = len(onsets)
    row["onset_times"] = " ".join(f"{o - lead:.2f}" for o in onsets[:12])

    # Timbre sur la partie active (10 s au plus).
    a0 = int(lead * sr)
    a1 = min(n, int(end40 * sr), a0 + int(10 * sr))
    seg = mono[a0:a1]
    nfft = 2048
    if len(seg) < nfft:
        seg = np.pad(seg, (0, nfft - len(seg)))
    frames = np.lib.stride_tricks.sliding_window_view(seg, nfft)[:: nfft // 2]
    frames = frames * np.hanning(nfft)
    spec = np.abs(np.fft.rfft(frames, axis=1)) ** 2
    freqs = np.fft.rfftfreq(nfft, 1 / sr)
    energy = spec.sum(axis=1)
    keep = energy > energy.max() * 1e-3
    spec = spec[keep] if keep.any() else spec
    tot = spec.sum()
    p = spec.sum(axis=0)
    row["centroid"] = int(float((p * freqs).sum() / max(p.sum(), 1e-12)))
    bands = [(0, 120), (120, 500), (500, 2000), (2000, 6000), (6000, sr / 2 + 1)]
    for (lo, hi), name in zip(bands, ["b_sub", "b_low", "b_mid", "b_himid", "b_air"]):
        m = (freqs >= lo) & (freqs < hi)
        row[name] = round(float(p[m].sum() / max(tot, 1e-12)), 3)
    # Planéité spectrale médiane : ~1 = bruit, ~0 = son tonal (bip, laser).
    gm = np.exp(np.mean(np.log(spec + 1e-20), axis=1))
    am = np.mean(spec, axis=1) + 1e-20
    row["flatness"] = round(float(np.median(gm / am)), 3)
    # Fréquence dominante (pic du spectre moyen) : utile pour les sons tonaux.
    row["f_peak"] = int(freqs[int(np.argmax(p[1:])) + 1])
    return row


def main():
    files = []
    for dp, dn, fn in os.walk(ROOT):
        for f in fn:
            full = os.path.join(dp, f)
            if wanted(full):
                files.append(full)
    files.sort()
    print(f"{len(files)} fichiers à mesurer", flush=True)
    fields = ["path", "pack", "category", "name", "sr", "ch", "dur", "peak_db", "env_peak_db", "rms_act_db",
              "lead", "len20", "len40", "t_peak", "onsets", "onset_times", "centroid", "f_peak", "flatness",
              "b_sub", "b_low", "b_mid", "b_himid", "b_air", "error"]
    with Pool(max(1, os.cpu_count() - 1)) as pool, open(OUT, "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=fields)
        w.writeheader()
        for i, row in enumerate(pool.imap(analyse, files, chunksize=16)):
            w.writerow(row)
            if i % 1000 == 0:
                print(i, flush=True)
    print("fini", flush=True)


if __name__ == "__main__":
    main()
