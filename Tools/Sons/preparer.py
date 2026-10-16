# Prépare les sons choisis (selection.py) pour Unity : mono, silences rognés, fondu de fin quand le
# fichier coupe net, niveau harmonisé par famille (crête à -1 dBFS), WAV 16 bits. Réécrit le tableau
# de Docs/Sons-Provenance.md (l'en-tête et les licences, écrits à la main, sont gardés).
#
# Usage : python -I preparer.py <banque> <Assets/_ProjectArt/Audio/SoundsBank> <selection.py> <Docs/Sons-Provenance.md> [mesures.json]
# Dépendances : numpy, soundfile (pip install numpy soundfile), dans un venv hors du projet.
import importlib.util
import json
import os
import shutil
import sys

import numpy as np
import soundfile as sf

bank, out_root, sel_path, prov_path = sys.argv[1:5]
measures_path = sys.argv[5] if len(sys.argv) > 5 else None
spec = importlib.util.spec_from_file_location("selection", sel_path)
selection = importlib.util.module_from_spec(spec)
spec.loader.exec_module(selection)

TABLE_HEADER = "| Fichier | Source | Traitement |"


def db(x):
    return 20 * np.log10(max(float(x), 1e-12))


def to_mono(d):
    if d.shape[1] == 1:
        return d[:, 0], "mono"
    m = d.mean(axis=1)
    rms_m = np.sqrt(np.mean(m ** 2))
    rms_ch = [np.sqrt(np.mean(d[:, c] ** 2)) for c in range(d.shape[1])]
    if rms_m < 0.6 * np.mean(rms_ch):  # opposition de phase : on garde le canal le plus fort
        c = int(np.argmax(rms_ch))
        return d[:, c], f"canal {c}"
    return m, "moyenne"


def process(x, sr, keep_start, max_len, fade_ms):
    peak = np.max(np.abs(x))
    notes = []
    start = 0
    if not keep_start:
        above = np.where(np.abs(x) > peak * 10 ** (-50 / 20))[0]
        start = max(0, int(above[0]) - int(0.002 * sr)) if len(above) else 0
        if start > 0:
            notes.append(f"début rogné {start / sr * 1000:.0f} ms")
    x = x[start:]
    if max_len:
        n = int(max_len * sr)
        if len(x) > n:
            x = x[:n]
            notes.append(f"coupé à {max_len:.2f} s")
    # Fin : dernier échantillon à -60 dB de la crête, plus 10 ms.
    above = np.where(np.abs(x) > peak * 10 ** (-60 / 20))[0]
    if len(above):
        end = min(len(x), int(above[-1]) + int(0.010 * sr))
        if end < len(x) - int(0.005 * sr):
            notes.append(f"fin rognée {(len(x) - end) / sr * 1000:.0f} ms")
        x = x[:end]
    # Fondu de fin : long si le son coupe net (les 10 dernières ms à moins de 40 dB de la crête).
    tail = x[-int(0.010 * sr):]
    abrupt = np.sqrt(np.mean(tail ** 2)) > peak * 10 ** (-40 / 20)
    f_ms = fade_ms if fade_ms else (40 if abrupt else 5)
    nf = min(len(x), int(f_ms / 1000 * sr))
    if nf > 1:
        x = x.copy()
        x[-nf:] *= np.cos(np.linspace(0, np.pi / 2, nf)) ** 2
        if abrupt or fade_ms:
            notes.append(f"fondu {f_ms} ms")
    return x, notes


def active_rms_db(x, sr):
    win = max(1, int(0.010 * sr))
    k = len(x) // win
    if k == 0:
        return db(np.sqrt(np.mean(x ** 2)))
    env = np.sqrt(np.mean(x[: k * win].reshape(k, win) ** 2, axis=1))
    act = env > env.max() * 10 ** (-20 / 20)
    return db(np.sqrt(np.mean(env[act] ** 2)))


missing = [src for fam in selection.FAMILIES for _, src in fam["items"] if not os.path.exists(os.path.join(bank, src))]
if missing:
    print("SOURCES INTROUVABLES :")
    for m in missing:
        print("  ", m)
    sys.exit(1)

measures = {}
rows = [TABLE_HEADER, "|---|---|---|"]
for fam in selection.FAMILIES:
    if fam.get("raw"):
        # Une boucle se copie telle quelle : rognée ou fondue, elle ne bouclerait plus sans couture.
        os.makedirs(os.path.join(out_root, fam["dest"]), exist_ok=True)
        for name, src in fam["items"]:
            ext = os.path.splitext(src)[1].lower()
            rel = f"{fam['dest']}/{name}{ext}"
            shutil.copyfile(os.path.join(bank, src), os.path.join(out_root, rel))
            rows.append(f"| `{rel}` | `{src}` | copie telle quelle (boucle) |")
            print(f"{rel:45} copie telle quelle")
        continue
    processed = []
    for name, src in fam["items"]:
        d, sr = sf.read(os.path.join(bank, src), dtype="float64", always_2d=True)
        x, how = to_mono(d)
        x, notes = process(x, sr, fam.get("keep_start", False), fam.get("max_len"), fam.get("fade_ms"))
        processed.append((name, src, x, sr, [how] + notes))
    peak = max(np.max(np.abs(p[2])) for p in processed)
    gain = 10 ** (-1 / 20) / peak
    os.makedirs(os.path.join(out_root, fam["dest"]), exist_ok=True)
    for name, src, x, sr, notes in processed:
        y = x * gain
        rel = f"{fam['dest']}/{name}.wav"
        sf.write(os.path.join(out_root, rel), y, sr, subtype="PCM_16")
        measures[rel] = dict(source=src, sr=sr, duree=round(len(y) / sr, 3), crete_db=round(db(np.max(np.abs(y))), 1),
                             rms_actif_db=round(active_rms_db(y, sr), 1), gain_db=round(db(gain), 1))
        rows.append(f"| `{rel}` | `{src}` | {', '.join(notes)}, gain {db(gain):+.1f} dB |")
        print(f"{rel:45} {len(y) / sr:5.2f} s  crête {db(np.max(np.abs(y))):5.1f}  rms {active_rms_db(y, sr):6.1f}  {'; '.join(notes)}")

# Le tableau remplace celui du document ; ce qui le précède (en-tête, licences) est gardé.
head = "# Provenance des sons du jeu\n\n"
if os.path.exists(prov_path):
    text = open(prov_path, encoding="utf-8").read()
    cut = text.find(TABLE_HEADER)
    head = text[:cut] if cut >= 0 else text.rstrip("\n") + "\n\n"
open(prov_path, "w", encoding="utf-8").write(head + "\n".join(rows) + "\n")
if measures_path:
    json.dump(measures, open(measures_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(f"\n{len(measures)} fichiers écrits")
