"""Petite boîte à outils de synthèse sonore (numpy/scipy) pour Duel Arena.
Tous les sons sont mono, 48 kHz, 16 bits."""
import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 48000


class Gen:
    def __init__(self, seed):
        self.r = np.random.default_rng(seed)

    # ------------------------------------------------------------ sources
    def white(self, n):
        return self.r.standard_normal(n)

    def pink(self, n):
        x = self.r.standard_normal(n)
        X = np.fft.rfft(x)
        f = np.fft.rfftfreq(n, 1 / SR)
        f[0] = f[1]
        X /= np.sqrt(f)
        y = np.fft.irfft(X, n)
        return y / (np.std(y) + 1e-12)

    def brown(self, n):
        y = np.cumsum(self.r.standard_normal(n))
        y = hp(y, 20)
        return y / (np.std(y) + 1e-12)

    def uniform(self, a, b):
        return float(self.r.uniform(a, b))

    def choice(self, seq):
        return seq[int(self.r.integers(0, len(seq)))]


def n_of(d):
    return int(round(d * SR))


def tv(n):
    return np.arange(n) / SR


# ------------------------------------------------------------ filtres
def _sos(kind, f, order=2):
    nyq = SR / 2
    if kind in ("low", "high"):
        return signal.butter(order, min(f / nyq, 0.999), btype=kind, output="sos")
    lo, hi = f
    return signal.butter(order, [max(lo / nyq, 1e-4), min(hi / nyq, 0.999)], btype="band", output="sos")


def lp(x, fc, order=2):
    return signal.sosfilt(_sos("low", fc, order), x)


def hp(x, fc, order=2):
    return signal.sosfilt(_sos("high", fc, order), x)


def bp(x, lo, hi, order=2):
    return signal.sosfilt(_sos("band", (lo, hi), order), x)


def peak(x, f0, q, gain_db):
    A = 10 ** (gain_db / 40)
    w0 = 2 * np.pi * f0 / SR
    alpha = np.sin(w0) / (2 * q)
    b = [1 + alpha * A, -2 * np.cos(w0), 1 - alpha * A]
    a = [1 + alpha / A, -2 * np.cos(w0), 1 - alpha / A]
    return signal.lfilter(b, a, x)


def sweep_lp(x, f_start, f_end, block=256):
    """Passe-bas dont la coupure glisse (exponentiellement) au fil du son."""
    out = np.zeros_like(x)
    zi = None
    nb = int(np.ceil(len(x) / block))
    for i in range(nb):
        k = i / max(1, nb - 1)
        fc = f_start * (f_end / f_start) ** k
        sos = _sos("low", fc, 2)
        seg = x[i * block:(i + 1) * block]
        if zi is None:
            zi = signal.sosfilt_zi(sos) * 0
        y, zi = signal.sosfilt(sos, seg, zi=zi)
        out[i * block:i * block + len(seg)] = y
    return out


# ------------------------------------------------------------ enveloppes
def env_exp(n, tau, attack=0.0005):
    t = tv(n)
    e = np.exp(-t / max(tau, 1e-5))
    na = max(1, n_of(attack))
    e[:na] *= np.linspace(0, 1, na)
    return e


def env_ar(n, attack, release):
    """Montée puis descente linéaires (en fraction de la durée)."""
    e = np.ones(n)
    na, nr = max(1, n_of(attack)), max(1, n_of(release))
    e[:na] = np.linspace(0, 1, na)
    e[-nr:] *= np.linspace(1, 0, nr)
    return e


def fade(x, fin=0.002, fout=0.01):
    x = x.copy()
    a, b = n_of(fin), n_of(fout)
    if a > 0:
        x[:a] *= np.linspace(0, 1, a)
    if b > 0:
        x[-b:] *= np.linspace(1, 0, b) ** 2
    return x


# ------------------------------------------------------------ oscillateurs
def phase_of(freq):
    return 2 * np.pi * np.cumsum(freq) / SR


def sine(freq, n=None, ph=0.0):
    if np.isscalar(freq):
        return np.sin(2 * np.pi * freq * tv(n) + ph)
    return np.sin(phase_of(freq) + ph)


def glide(f0, f1, n, curve="exp"):
    k = np.linspace(0, 1, n)
    if curve == "exp":
        return f0 * (f1 / f0) ** k
    if curve == "ease":
        k = 0.5 - 0.5 * np.cos(np.pi * k)
    return f0 + (f1 - f0) * k


def saw(freq, n=None):
    ph = (np.cumsum(np.full(n, freq) if np.isscalar(freq) else freq) / SR) % 1.0
    return 2 * ph - 1


def square(freq, n=None, duty=0.5):
    ph = (np.cumsum(np.full(n, freq) if np.isscalar(freq) else freq) / SR) % 1.0
    return np.where(ph < duty, 1.0, -1.0)


def modal(freqs, decays, amps, n, g=None, phases=None):
    t = tv(n)
    y = np.zeros(n)
    for i, (f, d, a) in enumerate(zip(freqs, decays, amps)):
        if f >= SR / 2 * 0.95:
            continue
        ph = phases[i] if phases is not None else (g.uniform(0, 2 * np.pi) if g else 0.0)
        y += a * np.exp(-t / d) * np.sin(2 * np.pi * f * t + ph)
    return y


def metal(g, n, base, count=6, spread=(1.0, 4.5), decay=0.08, decay_tilt=0.6, bright=0.7):
    """Partiels inharmoniques de pièce métallique : base x rapports tirés au hasard."""
    ratios = np.sort(np.concatenate([[1.0], g.r.uniform(spread[0] * 1.3, spread[1], count - 1)]))
    freqs = base * ratios * g.r.uniform(0.97, 1.03, count)
    decays = decay * (1.0 / ratios) ** decay_tilt * g.r.uniform(0.7, 1.3, count)
    amps = (1.0 / ratios) ** (1 - bright) * g.r.uniform(0.5, 1.0, count)
    return modal(freqs, decays, amps, n, g)


# ------------------------------------------------------------ non-linéarités
def sat(x, drive=2.0):
    return np.tanh(drive * x) / np.tanh(drive)


def crush(x, bits=6, down=4):
    y = np.repeat(x[::down], down)[: len(x)]
    q = 2 ** (bits - 1)
    return np.round(y * q) / q


# ------------------------------------------------------------ espace
def room_ir(g, rt_low=1.5, rt_mid=1.1, rt_high=0.5, length=1.6, early=(0.011, 0.019, 0.027, 0.041, 0.058, 0.077), predelay=0.006):
    """Réponse impulsionnelle synthétique d'une grande salle de béton souterraine : premières
    réflexions franches (murs nus), puis queue dont les aigus meurent plus vite."""
    n = n_of(length)
    t = tv(n)
    noise = g.white(n)
    lo = lp(noise, 400) * np.exp(-6.91 * t / rt_low)
    mi = bp(noise, 400, 3000) * np.exp(-6.91 * t / rt_mid)
    hi = hp(noise, 3000) * np.exp(-6.91 * t / rt_high)
    tail = lo + 0.8 * mi + 0.45 * hi
    onset = np.clip(t / 0.04, 0, 1)
    ir = tail * onset * 0.35
    for i, d in enumerate(early):
        k = n_of(d + predelay)
        if k < n:
            ir[k] += (0.7 ** i) * g.uniform(0.6, 1.0) * (1 if g.r.random() < 0.5 else -1)
    ir[0] = 1.0
    return ir / np.max(np.abs(ir))


def reverb(x, ir, wet=0.2, damp=None):
    """Ajoute la queue de salle à x (le son direct reste à son niveau). damp : passe-bas (Hz)
    appliqué à la seule réverbération, comme l'air et le béton qui mangent les aigus."""
    ir2 = ir.copy()
    ir2[0] = 0.0
    out = signal.fftconvolve(x, ir2) * wet
    if damp:
        out = lp(out, damp)
    out[: len(x)] += x
    return out


def add_at(dst, src, t0, gain=1.0):
    k = n_of(t0)
    if k >= len(dst):
        return dst
    m = min(len(src), len(dst) - k)
    dst[k:k + m] += src[:m] * gain
    return dst


def normalize(x, peak_db=-1.0):
    p = np.max(np.abs(x)) + 1e-12
    return x * (10 ** (peak_db / 20) / p)


def rms_normalize(x, rms_db=-20.0, ceiling_db=-1.0):
    r = np.sqrt(np.mean(x ** 2)) + 1e-12
    y = x * (10 ** (rms_db / 20) / r)
    p = np.max(np.abs(y))
    c = 10 ** (ceiling_db / 20)
    return y * (c / p) if p > c else y


def trim_silence(x, thresh_db=-60, pad=0.01):
    a = np.abs(x)
    th = np.max(a) * 10 ** (thresh_db / 20)
    idx = np.where(a > th)[0]
    if len(idx) == 0:
        return x
    end = min(len(x), idx[-1] + n_of(pad))
    return x[:end]


def write(path, x):
    x = np.clip(x, -1, 1)
    wavfile.write(path, SR, (x * 32767).astype(np.int16))


def loop_crossfade(y, xf):
    """Rend y bouclable : ses xf derniers échantillons sont fondus sur ses premiers, et retirés."""
    n = len(y)
    head, tail = y[:xf].copy(), y[n - xf:].copy()
    w = np.linspace(0, 1, xf)
    out = y[: n - xf].copy()
    out[:xf] = head * w + tail * (1 - w)
    return out
