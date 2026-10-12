import sys, os, glob
import numpy as np
from scipy.io import wavfile
from scipy import signal
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
root, out = sys.argv[1], sys.argv[2]
files = sorted(glob.glob(f"{root}/**/*.wav", recursive=True))
print(f"{'fichier':42s} {'durée':>6s} {'crête':>6s} {'RMS':>6s} {'centr.':>7s} {'DC':>7s} {'clip':>4s}")
for f in files:
    sr, x = wavfile.read(f); x = x.astype(np.float64) / 32768
    pk = 20*np.log10(np.max(np.abs(x))+1e-9); rms = 20*np.log10(np.sqrt(np.mean(x**2))+1e-9)
    X = np.abs(np.fft.rfft(x)); fr = np.fft.rfftfreq(len(x), 1/sr); cen = np.sum(fr*X)/np.sum(X)
    clip = int(np.sum(np.abs(x) > 0.999))
    print(f"{os.path.relpath(f, root):42s} {len(x)/sr:6.2f} {pk:6.1f} {rms:6.1f} {cen:7.0f} {np.mean(x):7.4f} {clip:4d}")
# planches de spectrogrammes par dossier
for d in sorted({os.path.dirname(f) for f in files}):
    fs = [f for f in files if os.path.dirname(f) == d]
    cols = 4; rows = int(np.ceil(len(fs)/cols))
    fig, axs = plt.subplots(rows, cols, figsize=(cols*4, rows*2.6), squeeze=False)
    for ax in axs.flat: ax.axis("off")
    for ax, f in zip(axs.flat, fs):
        sr, x = wavfile.read(f); x = x.astype(np.float64)/32768
        nper = 512 if len(x) > 4096 else 256
        fr, tt, Z = signal.spectrogram(x, sr, nperseg=nper, noverlap=nper*3//4)
        ax.pcolormesh(tt, fr/1000, 10*np.log10(Z+1e-12), vmin=-120, vmax=-30, shading="auto", cmap="magma")
        ax.plot(np.arange(len(x))/sr, 12 + x*10, color="cyan", lw=0.4)  # forme d'onde superposée
        ax.set_ylim(0, 24); ax.set_title(os.path.basename(f)[:-4], fontsize=8); ax.axis("on"); ax.tick_params(labelsize=6)
    fig.tight_layout(); fig.savefig(f"{out}/spec_{os.path.basename(d)}.png", dpi=70); plt.close(fig)
