"""Synthesizes the promo soundtrack (royalty-free: everything is generated here).

    python music.py events.json out.wav [duration]

events.json is the cue list exported by promo.html (window.__events): strokes, commits, taps, scene changes...
Tempo is 100 BPM (a bar is 2.4 s) so the scene changes in the video land on bar lines.
"""
import json, sys, wave
import numpy as np

SR = 48000
BPM = 100
BEAT = 60 / BPM
BAR = 4 * BEAT
rng = np.random.default_rng(20261023)

events = json.load(open(sys.argv[1], encoding='utf-8'))
out_path = sys.argv[2]
DUR = float(sys.argv[3]) if len(sys.argv) > 3 else 51.0
N = int(DUR * SR)

L = np.zeros(N)
R = np.zeros(N)
dryL = np.zeros(N)   # parts that should not go through the reverb
dryR = np.zeros(N)
sideL = np.zeros(N)  # pad + bass bus, ducked by the kick
sideR = np.zeros(N)


def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12)


def add(buf_l, buf_r, sig, t0, pan=0.0, gain=1.0):
    i0 = int(round(t0 * SR))
    if i0 >= N:
        return
    if i0 < 0:
        sig = sig[-i0:]
        i0 = 0
    n = min(len(sig), N - i0)
    gl = gain * np.cos((pan + 1) * np.pi / 4) * np.sqrt(2)
    gr = gain * np.sin((pan + 1) * np.pi / 4) * np.sqrt(2)
    buf_l[i0:i0 + n] += sig[:n] * gl
    buf_r[i0:i0 + n] += sig[:n] * gr


def env_adsr(n, a, d, s, r, sustain_len):
    """Attack/decay/sustain/release envelope; times in seconds."""
    t = np.arange(n) / SR
    e = np.zeros(n)
    e = np.where(t < a, t / max(a, 1e-6), e)
    dec = (t >= a) & (t < a + d)
    e = np.where(dec, 1 - (1 - s) * (t - a) / max(d, 1e-6), e)
    e = np.where((t >= a + d) & (t < sustain_len), s, e)
    rel = t >= sustain_len
    e = np.where(rel, s * np.exp(-(t - sustain_len) / max(r, 1e-6) * 3), e)
    return e


def lowpass(x, cutoff):
    """FFT brick-ish low-pass with a soft knee (fine for short sounds)."""
    n = len(x)
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    X *= 1 / (1 + (f / cutoff) ** 4)
    return np.fft.irfft(X, n)


def bandpass(x, lo, hi):
    n = len(x)
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    X *= (1 / (1 + (f / hi) ** 4)) * (1 - 1 / (1 + (f / lo) ** 4))
    return np.fft.irfft(X, n)


# ------------------------------------------------------------------ harmony
#                 bars   voicing (MIDI)
PROGRESSION = [
    (0, 2, [50, 57, 61, 64, 66]),   # Dmaj9
    (2, 2, [47, 54, 57, 61, 62]),   # Bm9
    (4, 2, [43, 50, 54, 59, 61]),   # Gmaj7#11
    (6, 2, [45, 52, 54, 59, 62]),   # A6sus
    (8, 2, [50, 57, 61, 64, 66]),   # Dmaj9
    (10, 2, [47, 54, 57, 61, 62]),  # Bm9
    (12, 2, [43, 50, 54, 57, 59]),  # Gmaj9
    (14, 2, [45, 52, 57, 61, 64]),  # A
    (16, 2, [47, 54, 57, 61, 62]),  # Bm9
    (18, 2, [43, 50, 54, 57, 59]),  # Gmaj9
    (20, 2, [50, 57, 61, 64, 69]),  # Dmaj9 (final)
]

# ------------------------------------------------------------------ pad
def pad_note(f, length):
    n = int(length * SR)
    t = np.arange(n) / SR
    sig = np.zeros(n)
    for det in (-0.0023, 0.0, 0.0021):          # ~±4 cents chorus
        ff = f * (1 + det)
        for k in range(1, 9):
            amp = (1 / k) * np.exp(-0.35 * k)
            sig += amp * np.sin(2 * np.pi * ff * k * t + rng.uniform(0, 2 * np.pi))
    sig *= 1 + 0.08 * np.sin(2 * np.pi * 0.23 * t + rng.uniform(0, 6))
    return sig / 3


for bar0, bars, notes in PROGRESSION:
    t0 = bar0 * BAR
    length = bars * BAR
    final = bar0 == 20
    n = int((length + 2.5) * SR)
    e = env_adsr(n, 0.9 if bar0 else 2.2, 0.6, 0.85, 1.6, length + (1.5 if final else 0.15))
    # The pad carries the intro alone, so it is louder there; drums and bass take over from bar 4.
    pad_gain = 0.085 if bar0 < 4 else 0.055 if bar0 < 18 else 0.07
    for i, m in enumerate(notes):
        s = pad_note(midi(m), length + 2.5)[:n] * e
        add(sideL, sideR, s, t0 - 0.1, pan=(-0.35 + 0.7 * i / (len(notes) - 1)), gain=pad_gain)

# ------------------------------------------------------------------ sub bass (from the demo onwards)
for bar0, bars, notes in PROGRESSION:
    if bar0 < 4 or bar0 >= 20:
        continue
    root = notes[0] - 12 if notes[0] > 45 else notes[0]
    for b in range(bars):
        t0 = (bar0 + b) * BAR
        n = int(BAR * SR)
        t = np.arange(n) / SR
        f = midi(root)
        s = np.sin(2 * np.pi * f * t) + 0.25 * np.sin(2 * np.pi * 2 * f * t)
        e = np.minimum(1, t / 0.02) * (0.65 + 0.35 * np.exp(-t / 0.5)) * np.minimum(1, (BAR - t) / 0.05)
        add(sideL, sideR, s * e, t0, gain=0.16)

# ------------------------------------------------------------------ bell arpeggio
def bell(f, length=0.9, bright=2.2):
    n = int(length * SR)
    t = np.arange(n) / SR
    idx = bright * np.exp(-t / 0.18)
    s = np.sin(2 * np.pi * f * t + idx * np.sin(2 * np.pi * 2.0 * f * t))
    s += 0.25 * np.sin(2 * np.pi * 3.0 * f * t) * np.exp(-t / 0.12)
    return s * np.exp(-t / 0.42) * np.minimum(1, t / 0.003)


ARP = [0, 2, 3, 4, 3, 2, 1, 2]
for bar0, bars, notes in PROGRESSION:
    for b in range(bars):
        bar = bar0 + b
        if bar < 2 or bar >= 21:
            continue
        sparse = bar < 4 or bar >= 18
        steps = range(0, 8, 2) if sparse else range(8)
        for k in steps:
            note = notes[ARP[k]] + 12
            if k in (3, 4) and not sparse:
                note += 12
            t0 = bar * BAR + k * BEAT / 2
            vel = 0.75 + 0.25 * (k % 2 == 0)
            add(L, R, bell(midi(note)), t0, pan=(-0.45 if k % 2 else 0.45), gain=(0.06 if bar < 4 else 0.045) * vel)

# ------------------------------------------------------------------ drums (demo → details)
kick_env = np.zeros(N)
DRUM_FROM, DRUM_TO = 4 * BAR, 18 * BAR


def kick():
    n = int(0.45 * SR)
    t = np.arange(n) / SR
    f = 46 + 90 * np.exp(-t / 0.045)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-t / 0.16) * np.minimum(1, t / 0.002)


def hat():
    n = int(0.08 * SR)
    t = np.arange(n) / SR
    x = np.diff(rng.standard_normal(n + 1))
    x = bandpass(x, 6000, 14000)
    return x * np.exp(-t / 0.018)


def snap():
    n = int(0.25 * SR)
    t = np.arange(n) / SR
    x = bandpass(rng.standard_normal(n), 900, 5000) * np.exp(-t / 0.05)
    x += 0.4 * np.sin(2 * np.pi * 190 * t) * np.exp(-t / 0.04)
    return x


bar = 4
while bar * BAR < DRUM_TO:
    for beat in range(4):
        t0 = bar * BAR + beat * BEAT
        if beat in (0, 2):
            add(dryL, dryR, kick(), t0, gain=0.30 if bar == 4 and beat == 0 else 0.27)
            i0 = int(t0 * SR)
            n = min(int(0.4 * SR), N - i0)
            tt = np.arange(n) / SR
            kick_env[i0:i0 + n] = np.maximum(kick_env[i0:i0 + n], np.exp(-tt / 0.14))
        if beat in (1, 3) and bar >= 9:
            add(L, R, snap(), t0, pan=0.05, gain=0.07)
        add(dryL, dryR, hat(), t0 + BEAT / 2, pan=0.25, gain=0.05)
        if bar >= 9:
            add(dryL, dryR, hat(), t0 + BEAT * 0.75, pan=-0.25, gain=0.02)
    bar += 1

# sidechain: duck pad + bass under the kick
duck = 1 - 0.38 * kick_env
L += sideL * duck
R += sideR * duck

# ------------------------------------------------------------------ sound effects from the video's cue list
def whoosh(length=0.9):
    """Noise swelling into the cut, brightening as it rises."""
    n = int(length * SR)
    t = np.arange(n) / SR
    x = rng.standard_normal(n)
    parts = []
    seg = n // 12
    for i in range(12):
        lo = 300 + 3500 * (i / 11) ** 2
        parts.append(bandpass(x[i * seg:(i + 1) * seg + 400], lo * 0.5, lo * 2.5)[:seg])
    y = np.concatenate(parts)
    tt = np.arange(len(y)) / SR
    shape = (tt / length) ** 2.2 * np.exp(-((tt - length * 0.86) / 0.09) ** 2 * 0.0) * np.minimum(1, (length - tt) / 0.05)
    return y * shape


def shimmer():
    s = np.zeros(int(1.6 * SR))
    for i, m in enumerate([81, 86, 90, 93]):
        b = bell(midi(m), 1.2, 1.4)
        i0 = int(i * 0.06 * SR)
        s[i0:i0 + len(b)] += b[:len(s) - i0]
    return s


def pen(length):
    n = int((length + 0.08) * SR)
    t = np.arange(n) / SR
    x = bandpass(rng.standard_normal(n), 1800, 7000)
    e = np.minimum(1, t / 0.03) * np.minimum(1, (n / SR - t) / 0.06) * (0.7 + 0.3 * np.sin(2 * np.pi * 11 * t))
    return x * e


def tink():
    s = bell(midi(88), 0.6, 1.2)
    n = int(0.01 * SR)
    s[:n] += rng.standard_normal(n) * np.linspace(0.6, 0, n)
    return s


def click():
    n = int(0.05 * SR)
    t = np.arange(n) / SR
    return (np.sin(2 * np.pi * 1700 * t) * np.exp(-t / 0.008) + 0.5 * rng.standard_normal(n) * np.exp(-t / 0.003))


def pop():
    n = int(0.12 * SR)
    t = np.arange(n) / SR
    f = 520 + 420 * (t / 0.12)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.035) * np.minimum(1, t / 0.002)


def draw_swell(length):
    n = int((length + 0.4) * SR)
    t = np.arange(n) / SR
    x = bandpass(rng.standard_normal(n), 2000, 9000)
    e = np.sin(np.pi * np.clip(t / (length + 0.4), 0, 1)) ** 1.5
    return x * e


for ev in events:
    t, kind = ev['t'], ev['type']
    if kind == 'whoosh':
        w = whoosh(0.9)
        add(L, R, w, t + 0.35 - 0.9 * 0.86, gain=0.11)
    elif kind == 'title':
        add(L, R, shimmer(), t, gain=0.05)
    elif kind == 'soft':
        add(L, R, bell(midi(74), 1.4, 0.8), t, pan=-0.2, gain=0.035)
    elif kind == 'stroke':
        add(L, R, pen(ev.get('dur', 0.3)), t, pan=0.2, gain=0.022)
    elif kind == 'commit':
        add(L, R, tink(), t, pan=-0.1, gain=0.07)
    elif kind == 'tap':
        add(dryL, dryR, click(), t, gain=0.09)
    elif kind == 'pop':
        add(L, R, pop(), t, pan=0.1, gain=0.05)
    elif kind == 'draw':
        add(L, R, draw_swell(ev.get('dur', 1.0)), t, gain=0.03)

# ------------------------------------------------------------------ reverb (convolution with decaying noise)
def reverb(x, seconds=2.8, seed=0):
    r = np.random.default_rng(seed)
    n = int(seconds * SR)
    t = np.arange(n) / SR
    ir = r.standard_normal(n) * np.exp(-t / (seconds / 6.9)) * np.minimum(1, t / 0.01)
    ir = lowpass(ir, 6500)
    ir /= np.sqrt(np.sum(ir ** 2))
    size = 1 << int(np.ceil(np.log2(len(x) + n)))
    y = np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(ir, size), size)[:len(x)]
    return y


wetL = reverb(L, seed=1)
wetR = reverb(R, seed=2)
outL = L * 0.85 + wetL * 0.42 + dryL + reverb(dryL, 1.2, 3) * 0.08
outR = R * 0.85 + wetR * 0.42 + dryR + reverb(dryR, 1.2, 4) * 0.08

# ------------------------------------------------------------------ master
t = np.arange(N) / SR
fade = np.minimum(1, t / 0.4) * np.clip((DUR - t) / 1.6, 0, 1) ** 1.5
outL *= fade
outR *= fade
stereo = np.stack([outL, outR], axis=1)
rms = np.sqrt(np.mean(stereo[int(10 * SR):int(40 * SR)] ** 2))
stereo *= 0.19 / max(rms, 1e-9)                     # about -14.5 dBFS RMS in the busy middle (YouTube aims at -14 LUFS)
stereo = np.tanh(stereo * 1.25) / 1.25              # gentle saturation / peak control
peak = np.max(np.abs(stereo))
if peak > 0.89:
    stereo *= 0.89 / peak                           # -1 dBFS ceiling
with wave.open(out_path, 'wb') as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes((np.clip(stereo, -1, 1) * 32767).astype('<i2').tobytes())
print(f"wrote {out_path}: {DUR:.1f}s, peak {20 * np.log10(np.max(np.abs(stereo))):.1f} dBFS, "
      f"rms {20 * np.log10(np.sqrt(np.mean(stereo ** 2))):.1f} dBFS, {len(events)} cues")
