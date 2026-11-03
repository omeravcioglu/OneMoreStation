"""One More Station - trailer theme ("Next Stop").

An original composition, synthesized from scratch in numpy: no samples, no loops, nothing licensed.
C major, 120 BPM (one beat = 15 frames at 30 fps, one bar = 2 s), so every section starts on a frame.

  python music.py [out.wav]        -> the mastered theme (float32 WAV, 48 kHz stereo) + music_cues.json

Instruments: marimba plucks, glockenspiel, a whistled lead, warm pads, bass, synthesized drums,
risers and impacts, and the two-tone station chime that opens and closes the trailer.
"""
import json
import os
import sys

import numpy as np
import scipy.io.wavfile as wavfile
import scipy.signal as ss

SR = 48000
BPM = 120
BEAT = 60.0 / BPM
BAR = 4 * BEAT
LENGTH = 73.0
N = int(LENGTH * SR)
rng = np.random.default_rng(20261030)

HERE = os.path.dirname(os.path.abspath(__file__))


def bt(bar, beat=0.0):
    """Time in seconds of a beat in a bar (bars count from 1)."""
    return (bar - 1) * BAR + beat * BEAT


def hz(m):
    return 440.0 * 2.0 ** ((m - 69) / 12.0)


# ---------------------------------------------------------------------------------------- tracks

class Track:
    def __init__(self, name, reverb=0.0):
        self.name = name
        self.buf = np.zeros((N, 2), np.float32)
        self.reverb = reverb

    def add(self, t, sig, pan=0.0, gain=1.0):
        """Adds a mono (n,) or stereo (n, 2) signal at time t, constant-power panned."""
        i = int(round(t * SR))
        if i >= N:
            return
        if sig.ndim == 1:
            p = (pan + 1) * np.pi / 4
            sig = np.stack([sig * np.cos(p), sig * np.sin(p)], axis=1) * np.sqrt(2)
        j0 = max(0, i)
        k0 = j0 - i
        n = min(len(sig) - k0, N - j0)
        if n > 0:
            self.buf[j0:j0 + n] += (sig[k0:k0 + n] * gain).astype(np.float32)


T = {}
for name, rv in [("kick", 0.02), ("snare", 0.18), ("hats", 0.08), ("perc", 0.15), ("cym", 0.12),
                 ("bass", 0.0), ("pad", 0.30), ("marimba", 0.24), ("bells", 0.38), ("lead", 0.26),
                 ("fx", 0.30), ("crowd", 0.15)]:
    T[name] = Track(name, rv)


def tt(n):
    return np.arange(n) / SR


def sos(kind, f, order=2):
    if kind == "bp":
        return ss.butter(order, [f[0] / (SR / 2), f[1] / (SR / 2)], btype="band", output="sos")
    return ss.butter(order, f / (SR / 2), btype=kind, output="sos")


def noise(n):
    return rng.standard_normal(n).astype(np.float64)


def fade(sig, a=0.003, r=0.01):
    n = len(sig)
    na, nr = max(1, int(a * SR)), max(1, int(r * SR))
    env = np.ones(n)
    env[:na] = np.linspace(0, 1, na)
    env[-nr:] *= np.linspace(1, 0, nr)
    return sig * env


# ---------------------------------------------------------------------------------------- tuned instruments

def marimba(m, dur, vel=1.0, ring=1.0):
    """Woody mallet pluck: fundamental + two-octave partial + a little click."""
    f = hz(m)
    decay = 1.25 * (440.0 / f) ** 0.35 * ring
    n = int((min(dur, decay * 3) + 0.25) * SR)
    t = tt(n)
    s = np.sin(2 * np.pi * f * t) * np.exp(-t / decay)
    s += 0.22 * np.sin(2 * np.pi * f * 4.0 * t) * np.exp(-t / (decay * 0.16))
    s += 0.05 * np.sin(2 * np.pi * f * 9.8 * t) * np.exp(-t / 0.03)
    click = ss.sosfilt(sos("highpass", 2500), noise(n)) * np.exp(-t / 0.002) * 0.05
    s = s + click
    if dur < decay * 3:  # damped when the next note comes
        k = t > dur
        s[k] *= np.exp(-(t[k] - dur) / 0.06)
    return fade(s, 0.002, 0.03) * vel


def bell(m, vel=1.0, decay=2.0):
    """Glockenspiel: inharmonic bar partials."""
    f = hz(m)
    n = int((decay * 2.2) * SR)
    t = tt(n)
    s = np.zeros(n)
    for r, a, d in [(1.0, 1.0, 1.0), (2.76, 0.35, 0.42), (5.40, 0.16, 0.2), (8.93, 0.07, 0.09)]:
        if f * r < SR / 2 - 500:
            s += a * np.sin(2 * np.pi * f * r * t) * np.exp(-t / (decay * d))
    return fade(s, 0.001, 0.05) * vel


def chime(m, vel=1.0):
    """The station chime: a rounder, longer bell with a soft octave below."""
    f = hz(m)
    n = int(3.2 * SR)
    t = tt(n)
    s = np.sin(2 * np.pi * f * t) * np.exp(-t / 1.4)
    s += 0.3 * np.sin(2 * np.pi * f * 0.5 * t) * np.exp(-t / 1.1)
    s += 0.18 * np.sin(2 * np.pi * f * 2.0 * t) * np.exp(-t / 0.35)
    s += 0.08 * np.sin(2 * np.pi * f * 3.01 * t) * np.exp(-t / 0.18)
    return fade(s, 0.002, 0.1) * vel


_tables = {}


def saw_table(m, cutoff):
    """One band-limited, low-passed saw cycle (4096 samples) for a pitch."""
    key = (m, int(cutoff))
    if key not in _tables:
        f = hz(m)
        ph = np.arange(4096) / 4096.0
        w = np.zeros(4096)
        k = 1
        while k * f < min(SR / 2 - 1000, 12000) and k <= 60:
            w += (1.0 / k) / (1.0 + (k * f / cutoff) ** 2) * np.sin(2 * np.pi * k * ph)
            k += 1
        _tables[key] = (w / np.max(np.abs(w))).astype(np.float64)
    return _tables[key]


def pad_chord(notes, dur, cutoff=1500.0, attack=0.45, release=0.9, vel=1.0):
    """Warm pad: two detuned band-limited saws per note, spread left/right."""
    n = int((dur + release) * SR)
    t = tt(n)
    out = np.zeros((n, 2))
    for i, m in enumerate(notes):
        tab = saw_table(m, cutoff)
        for side, cents in ((0, -7.0), (1, 7.0)):
            f = hz(m) * 2 ** (cents / 1200.0)
            ph = (f * t + rng.random()) % 1.0
            out[:, side] += np.interp(ph * 4096, np.arange(4097), np.append(tab, tab[0]))
    env = np.ones(n)
    na = int(attack * SR)
    env[:na] = np.linspace(0, 1, na) ** 1.5
    nd = int(dur * SR)
    env[nd:] = np.linspace(1, 0, n - nd) ** 2
    out *= env[:, None] / max(1, len(notes))
    return out * vel


def bass_note(m, dur, vel=1.0, kind="pluck"):
    f = hz(m)
    n = int((dur + 0.08) * SR)
    t = tt(n)
    ph = 2 * np.pi * f * t
    if kind == "bassoon":   # staccato reedy bass for the City Hall bit
        s = np.sin(ph) + 0.45 * np.sin(3 * ph) + 0.2 * np.sin(5 * ph) + 0.08 * np.sin(7 * ph)
        env = np.exp(-t / 0.16)
    else:
        s = np.sin(ph) + 0.35 * np.sin(2 * ph) + 0.12 * np.sin(3 * ph)
        env = 0.65 + 0.35 * np.exp(-t / 0.12)
    s = np.tanh(1.6 * s) / np.tanh(1.6)
    k = t > dur
    env[k] *= np.exp(-(t[k] - dur) / 0.02)
    return fade(s * env, 0.004, 0.02) * vel


# ---------------------------------------------------------------------------------------- the whistled lead

def lead_phrase(notes, t0, vibrato=1.0):
    """notes: [(start_beat_abs_seconds, dur_seconds, midi)], legato with short glides."""
    end = max(s + d for s, d, m in notes) + 0.4
    n = int((end - t0) * SR)
    t = tt(n) + t0
    f = np.zeros(n)
    amp = np.zeros(n)
    vib_env = np.zeros(n)
    for s, d, m in notes:
        i0, i1 = int((s - t0) * SR), int((s + d - t0) * SR)
        f[i0:i1] = hz(m)
        a = np.ones(i1 - i0)
        na = min(len(a), int(0.025 * SR))
        a[:na] = np.linspace(0.2, 1, na)
        nr = min(len(a), int(0.06 * SR))
        a[-nr:] *= np.linspace(1, 0.35, nr)
        amp[i0:i1] = np.maximum(amp[i0:i1], a)
        local = np.clip((np.arange(i1 - i0) / SR - 0.18) / 0.25, 0, 1)
        vib_env[i0:i1] = local
    # fill gaps with the previous pitch, and glide over 35 ms at every change
    last = f[np.nonzero(f)[0][0]]
    for i in range(n):
        if f[i] == 0:
            f[i] = last
        else:
            last = f[i]
    g = int(0.035 * SR)
    f = np.convolve(np.pad(f, (g, g), mode="edge"), np.ones(g) / g, mode="same")[g:-g]
    vib = 1 + vibrato * 0.0105 * vib_env * np.sin(2 * np.pi * 5.6 * t)
    ph = np.cumsum(2 * np.pi * f * vib / SR)
    sm = int(0.012 * SR)
    amp = np.convolve(amp, np.ones(sm) / sm, mode="same")
    s = np.sin(ph) + 0.10 * np.sin(2 * ph) + 0.025 * np.sin(3 * ph)
    breath = ss.sosfilt(sos("bp", (1800, 5200)), noise(n)) * 0.035
    return (s + breath) * amp


# ---------------------------------------------------------------------------------------- drums and effects

def kick(vel=1.0):
    n = int(0.45 * SR)
    t = tt(n)
    f = 46 + 120 * np.exp(-t / 0.028)
    s = np.sin(np.cumsum(2 * np.pi * f / SR)) * np.exp(-t / 0.26)
    s += ss.sosfilt(sos("highpass", 1800), noise(n)) * np.exp(-t / 0.0025) * 0.25
    return fade(np.tanh(1.4 * s), 0.0005, 0.02) * vel


def clap(vel=1.0):
    n = int(0.35 * SR)
    t = tt(n)
    nz = ss.sosfilt(sos("bp", (900, 2600)), noise(n))
    env = np.zeros(n)
    for k, d in enumerate((0.0, 0.009, 0.019)):
        i = int(d * SR)
        env[i:] += np.exp(-(t[i:] - d) / (0.006 if k < 2 else 0.11))
    return fade(nz * env * 0.55, 0.0005, 0.02) * vel


def snare(vel=1.0):
    n = int(0.25 * SR)
    t = tt(n)
    s = ss.sosfilt(sos("bp", (1500, 7000)), noise(n)) * np.exp(-t / 0.09) * 0.6
    s += np.sin(2 * np.pi * 185 * t) * np.exp(-t / 0.04) * 0.5
    return fade(s, 0.0005, 0.02) * vel


def hat(open_=False, vel=1.0):
    d = 0.20 if open_ else 0.032
    n = int((d * 4) * SR)
    t = tt(n)
    s = ss.sosfilt(sos("highpass", 7200), noise(n)) * np.exp(-t / d)
    return fade(s * 0.35, 0.0005, 0.01) * vel


def shaker(vel=1.0):
    n = int(0.12 * SR)
    t = tt(n)
    env = np.minimum(t / 0.008, 1.0) * np.exp(-np.maximum(t - 0.008, 0) / 0.035)
    return ss.sosfilt(sos("bp", (5000, 11000)), noise(n)) * env * 0.22 * vel


def woodblock(high=True, vel=1.0):
    f = 1250 if high else 870
    n = int(0.12 * SR)
    t = tt(n)
    s = np.sin(2 * np.pi * f * t) * np.exp(-t / 0.028) + 0.4 * np.sin(2 * np.pi * f * 2.72 * t) * np.exp(-t / 0.012)
    return fade(s * 0.4, 0.0005, 0.01) * vel


def tom(f0, f1, vel=1.0):
    n = int(0.5 * SR)
    t = tt(n)
    f = f1 + (f0 - f1) * np.exp(-t / 0.08)
    s = np.sin(np.cumsum(2 * np.pi * f / SR)) * np.exp(-t / 0.22)
    return fade(s * 0.8, 0.001, 0.02) * vel


def crash(vel=1.0, length=2.4):
    n = int(length * SR)
    t = tt(n)
    out = np.zeros((n, 2))
    for c in range(2):
        nz = ss.sosfilt(sos("highpass", 3200), noise(n))
        metal = sum(np.sin(2 * np.pi * f * t + rng.random() * 6) for f in rng.uniform(3500, 9500, 9)) * 0.05
        out[:, c] = (nz * 0.5 + metal) * np.exp(-t / 0.75)
    out *= np.minimum(t / 0.002, 1)[:, None]
    return out * 0.45 * vel


def reverse_crash(length=1.5, vel=1.0):
    c = crash(1.0, length + 0.2)[: int(length * SR)]
    return c[::-1] * vel * 0.8


def impact(vel=1.0):
    n = int(2.2 * SR)
    t = tt(n)
    f = 34 + 40 * np.exp(-t / 0.12)
    s = np.sin(np.cumsum(2 * np.pi * f / SR)) * np.exp(-t / 0.7)
    s += ss.sosfilt(sos("lowpass", 900), noise(n)) * np.exp(-t / 0.09) * 0.6
    return fade(np.tanh(1.2 * s), 0.001, 0.2) * vel


def riser(length, vel=1.0):
    n = int(length * SR)
    x = np.linspace(0, 1, n)
    nz = noise(n)
    out = np.zeros(n)
    steps = 24
    for k in range(steps):   # band-pass sweep, cross-faded in slices
        a, b = int(k * n / steps), int((k + 1) * n / steps)
        fc = 350 * (9000 / 350) ** (k / (steps - 1))
        seg = ss.sosfilt(sos("bp", (fc * 0.7, min(fc * 1.4, SR / 2 - 100))), nz[max(0, a - 2000):b])[-(b - a):]
        out[a:b] = seg
    ph = np.cumsum(2 * np.pi * (220 * 4 ** x) / SR)
    out = out * 0.6 + np.sin(ph) * 0.08
    return out * (x ** 2.2) * vel


def crowd(length, vel=1.0):
    n = int(length * SR)
    t = tt(n)
    base = ss.sosfilt(sos("bp", (280, 2600)), noise(n))
    mod = ss.sosfilt(sos("lowpass", 3), noise(n))
    mod = 0.65 + 0.35 * mod / (np.max(np.abs(mod)) + 1e-9)
    swell = np.minimum(t / 0.4, 1) * np.minimum((length - t) / 1.2, 1)
    cheer = ss.sosfilt(sos("bp", (900, 3800)), noise(n)) * np.clip(np.sin(2 * np.pi * 0.5 * t), 0, 1) ** 3 * 0.6
    return (base + cheer) * mod * swell * 0.35 * vel


# ---------------------------------------------------------------------------------------- the arrangement

CH = {  # chord voicings for the pad (MIDI)
    "C": [60, 64, 67, 72], "Cmaj7": [60, 64, 67, 71], "Cadd9": [60, 62, 64, 67, 72],
    "G": [55, 59, 62, 67], "G6": [55, 59, 62, 64], "G7": [55, 59, 65, 67], "Gsus": [55, 60, 62, 67],
    "Am": [57, 60, 64, 69], "F": [53, 57, 60, 65], "Fmaj7": [53, 57, 60, 64], "Dm": [50, 57, 62, 65],
    "Dm7": [50, 57, 60, 65], "Em": [52, 59, 64, 67],
}
ROOT = {"C": 36, "Cmaj7": 36, "Cadd9": 36, "G": 43, "G6": 43, "G7": 43, "Gsus": 43, "Am": 45, "F": 41,
        "Fmaj7": 41, "Dm": 38, "Dm7": 38, "Em": 40}

# bar -> chord(s) as [(beat, chord)]
PROG = {
    1: [(0, "Fmaj7")], 2: [(0, "G6")], 3: [(0, "Cadd9")], 4: [(0, "Cmaj7")], 5: [(0, "Fmaj7")],
    6: [(0, "C")], 7: [(0, "G")], 8: [(0, "Am")], 9: [(0, "F")], 10: [(0, "Dm7")], 11: [(0, "Gsus"), (2, "G7")],
    12: [(0, "F")], 13: [(0, "G")], 14: [(0, "Em")], 15: [(0, "Am")], 16: [(0, "F"), (2, "G")],
    17: [(0, "Am")], 18: [(0, "Dm")], 19: [(0, "G")], 20: [(0, "C")],
    21: [(0, "Fmaj7")], 22: [(0, "G6")],
    23: [(0, "F")], 24: [(0, "G")], 25: [(0, "Em")], 26: [(0, "Am")],
    27: [(0, "F")], 28: [(0, "G")], 29: [(0, "Gsus"), (2, "G")],
    30: [(0, "C")], 31: [(0, "Fmaj7")], 32: [(0, "G6")], 33: [(0, "Cadd9")], 34: [(0, "Cadd9")],
}


def chord_at(bar, beat):
    ch = PROG[bar][0][1]
    for b, c in PROG[bar]:
        if beat >= b:
            ch = c
    return ch


# --- pads
for bar, items in PROG.items():
    for k, (beat, ch) in enumerate(items):
        nb = items[k + 1][0] if k + 1 < len(items) else 4
        dur = (nb - beat) * BEAT
        last = bar >= 33
        cutoff = 1100 if bar <= 5 or bar >= 30 else 1700 if bar in (21, 22, 17, 18, 19, 20) else 2200
        vel = 0.55 if bar <= 2 else 0.7 if bar <= 5 else 0.5 if bar in (17, 18, 19, 20) else 0.8
        if bar == 34:
            continue
        T["pad"].add(bt(bar, beat), pad_chord(CH[ch], dur + (BAR if last else 0.0), cutoff, 0.35 if bar > 2 else 0.9,
                                              2.6 if last else 0.6, vel))

# --- the station chime ("ding-dong") at the very start and on the end card
for t0 in (0.0, bt(33)):
    T["bells"].add(t0, chime(88, 0.9), pan=-0.15)       # E6
    T["bells"].add(t0 + 0.42, chime(84, 0.9), pan=0.15)  # C6

# --- cold open (bars 1-2): the hook motif on marimba, opening up; riser into the title
HOOK_ARP = {  # bar chord -> (beat, dur, midi) the "da-da DA" figure
    "C": [(0, .5, 72), (.5, .5, 76), (1, 1, 79), (2, .5, 76), (2.5, .5, 79), (3, 1, 84)],
    "G": [(0, .5, 71), (.5, .5, 74), (1, 1, 79), (2, .5, 74), (2.5, .5, 79), (3, 1, 83)],
    "Am": [(0, .5, 69), (.5, .5, 72), (1, 1, 76), (2, .5, 72), (2.5, .5, 76), (3, 1, 81)],
    "F": [(0, .5, 69), (.5, .5, 72), (1, 1, 77), (2, .5, 72), (2.5, .5, 77), (3, 1, 81)],
    "Fmaj7": [(0, .5, 69), (.5, .5, 72), (1, 1, 76), (2, .5, 72), (2.5, .5, 76), (3, 1, 81)],
    "G6": [(0, .5, 71), (.5, .5, 74), (1, 1, 76), (2, .5, 74), (2.5, .5, 79), (3, 1, 83)],
    "Cmaj7": [(0, .5, 72), (.5, .5, 76), (1, 1, 79), (2, .5, 76), (2.5, .5, 79), (3, 1, 83)],
    "Dm7": [(0, .5, 69), (.5, .5, 72), (1, 1, 77), (2, .5, 74), (2.5, .5, 77), (3, 1, 81)],
}


def play_arp(bar, chord, vel, pan=0.1, octave=0, ring=1.0):
    for b, d, m in HOOK_ARP[chord]:
        T["marimba"].add(bt(bar, b), marimba(m + octave, d * BEAT, vel * (1.0 if b in (1, 3) else 0.8), ring), pan=pan)


play_arp(1, "Fmaj7", 0.45)
play_arp(2, "G6", 0.6)
T["fx"].add(bt(2, 1), riser(BAR * 0.75, 0.8))
T["cym"].add(bt(3) - 1.5, reverse_crash(1.5, 0.7))

# --- title hit (bar 3)
T["fx"].add(bt(3), impact(1.0))
T["cym"].add(bt(3), crash(1.0))
for k, m in enumerate((79, 84, 88, 91)):    # G5 C6 E6 G6, the logo flourish
    T["bells"].add(bt(3) + k * 0.075, bell(m, 0.75, 2.4), pan=-0.3 + k * 0.2)
for m in (60, 64, 67, 72):
    T["marimba"].add(bt(3), marimba(m, 1.5, 0.55), pan=0.0)

# --- intro (bars 4-5): rising-falling 8th arps on the marimba
INTRO = {"Cmaj7": [72, 76, 79, 83, 79, 76, 72, 76], "Fmaj7": [69, 72, 76, 77, 76, 72, 69, 72]}
for bar, ch in ((4, "Cmaj7"), (5, "Fmaj7")):
    for k, m in enumerate(INTRO[ch]):
        T["marimba"].add(bt(bar, k * 0.5), marimba(m, 0.5 * BEAT, 0.55 if k % 2 == 0 else 0.42), pan=0.2 if k % 2 else -0.2)
for k in range(16):
    T["perc"].add(bt(5, k * 0.25), shaker(0.5 if k % 4 == 0 else 0.3), pan=0.35)

# --- verse (bars 6-10): the hook figure on marimba, drums come in
for bar, ch in ((6, "C"), (7, "G"), (8, "Am"), (9, "F"), (10, "Dm7")):
    play_arp(bar, ch, 0.62, pan=0.15)
    root = ROOT[ch]
    for b in range(4):
        T["bass"].add(bt(bar, b), bass_note(root, BEAT * 0.9, 0.75))
    T["bass"].add(bt(bar, 3.5), bass_note(root + 12, BEAT * 0.45, 0.55))
    kicks = (0, 2) if bar <= 7 else (0, 1, 2, 3)
    for b in kicks:
        T["kick"].add(bt(bar, b), kick(0.85))
    if bar >= 8:
        for b in (1, 3):
            T["snare"].add(bt(bar, b), clap(0.8))
    for k in range(8):
        T["hats"].add(bt(bar, k * 0.5 + 0.5 * (k % 2 == 0)), hat(False, 0.5 if bar >= 8 else 0.35), pan=-0.3)
    for k in range(16):
        T["perc"].add(bt(bar, k * 0.25), shaker(0.55 if k % 4 == 0 else 0.32), pan=0.35)
# the first line opens (bar 7 downbeat): a bright little sparkle
for k, m in enumerate((84, 88, 91, 96)):
    T["bells"].add(bt(7) + k * 0.05, bell(m, 0.45, 1.2), pan=0.4)
T["cym"].add(bt(6), crash(0.5))

# --- build (bar 11): snare roll, riser, the drums drop out for the last eighth
for b in range(2):
    T["kick"].add(bt(11, b), kick(0.85))
roll = [(2 + k * 0.25) for k in range(4)] + [(3 + k * 0.125) for k in range(6)]
for k, b in enumerate(roll):
    T["snare"].add(bt(11, b), snare(0.35 + 0.55 * k / len(roll)))
T["fx"].add(bt(10, 2), riser(BAR * 1.0 + BAR * 0.5, 0.9))
T["cym"].add(bt(12) - 1.6, reverse_crash(1.6, 0.8))
for b in (0, 1, 2, 2.5, 3):
    T["bass"].add(bt(11, b), bass_note(43, BEAT * 0.45, 0.6 + 0.1 * b))

# --- chorus (bars 12-16) and final chorus (bars 23-26)
LEAD_A = [  # (bar offset, beat, dur beats, midi): the whistled hook over F | G | Em | Am
    (0, 0, .5, 69), (0, .5, .5, 72), (0, 1, 1, 77), (0, 2, .5, 76), (0, 2.5, .5, 77), (0, 3, 1, 79),
    (1, 0, .5, 71), (1, .5, .5, 74), (1, 1, 1.5, 79), (1, 2.5, .5, 77), (1, 3, 1, 76),
    (2, 0, .5, 67), (2, .5, .5, 71), (2, 1, 1, 76), (2, 2, .5, 74), (2, 2.5, .5, 76), (2, 3, 1, 79),
    (3, 0, 2.5, 81), (3, 2.5, .5, 79), (3, 3, .5, 76), (3, 3.5, .5, 79),
]
LEAD_TAIL = [(4, 0, 1, 77), (4, 1, 1, 76), (4, 2, 1, 74), (4, 3, 1, 71)]   # F E | D B, down into the City Hall bit


def chorus_drums(bar, big=False):
    for b in range(4):
        T["kick"].add(bt(bar, b), kick(0.95))
    for b in (1, 3):
        T["snare"].add(bt(bar, b), clap(0.95))
        if big:
            T["snare"].add(bt(bar, b), snare(0.35))
    for k in range(8):
        T["hats"].add(bt(bar, k * 0.5), hat(k == 7, 0.62 if k % 2 else 0.42), pan=-0.3)
    for k in range(16):
        T["perc"].add(bt(bar, k * 0.25), shaker(0.6 if k % 4 == 2 else 0.33), pan=0.38)


def chorus_bass(bar, ch, beat0=0, beats=4):
    root = ROOT[ch]
    for b, d, o, v in ((0, 1, 0, 0.9), (1.5, .5, 0, 0.7), (2, 1, 0, 0.85), (3, .5, 7, 0.7), (3.5, .5, 12, 0.75)):
        if beat0 <= b < beat0 + beats:
            T["bass"].add(bt(bar, b), bass_note(root + o, d * BEAT * 0.92, v))


def chorus(first_bar, big):
    t0 = bt(first_bar)
    notes = [(bt(first_bar + bo, b), d * BEAT, m) for bo, b, d, m in LEAD_A]
    T["lead"].add(t0, lead_phrase(notes, t0), pan=0.0, gain=1.0)
    if big:   # an octave below underneath, and a harmony line a third down on bells
        T["lead"].add(t0, lead_phrase([(s, d, m - 12) for s, d, m in notes], t0, 0.6), pan=0.0, gain=0.42)
    for k, ch in enumerate(("F", "G", "Em", "Am")):
        bar = first_bar + k
        chorus_drums(bar, big)
        chorus_bass(bar, ch)
        for j in range(8):   # running 8th-note marimba chord tones
            tones = [m for m in CH[ch] if m >= 60] + [m + 12 for m in CH[ch] if m >= 60]
            m = tones[(j * 2 + k) % len(tones)]
            T["marimba"].add(bt(bar, j * 0.5), marimba(m + 12, 0.45 * BEAT, 0.33 if j % 2 else 0.42), pan=0.3 if j % 2 else -0.3)
    T["cym"].add(t0, crash(1.0 if big else 0.85))
    T["fx"].add(t0, impact(0.75 if big else 0.6))


chorus(12, False)
# bar 16: F | G with the lead walking down, a tom fill into the City Hall section
t16 = bt(16)
T["lead"].add(t16, lead_phrase([(bt(16, b), d * BEAT, m) for _, b, d, m in LEAD_TAIL], t16), gain=0.95)
chorus_drums(16)
chorus_bass(16, "F", 0, 2)
chorus_bass(16, "G", 2, 2)
for k, (b, f0, f1) in enumerate(((3, 260, 170), (3.25, 230, 150), (3.5, 190, 120), (3.75, 160, 100))):
    T["perc"].add(bt(16, b), tom(f0, f1, 0.8))
# the growth card (bar 13 downbeat): a glockenspiel sparkle
for k, m in enumerate((79, 84, 88, 91, 96)):
    T["bells"].add(bt(13) + k * 0.045, bell(m, 0.5, 1.4), pan=-0.4 + k * 0.2)

# --- City Hall (bars 17-20): a bouncy, slightly officious oom-pah with tick-tock woodblocks
HALL = {
    17: ("Am", [(0, .25, 76), (.5, .25, 76), (1, .25, 72), (1.5, .25, 76), (2, .5, 81), (3, .25, 79), (3.5, .25, 76)]),
    18: ("Dm", [(0, .25, 77), (.5, .25, 77), (1, .25, 74), (1.5, .25, 77), (2, .5, 81), (3, .25, 79), (3.5, .25, 77)]),
    19: ("G", [(0, .5, 79), (1, .5, 79), (2, .25, 83), (2.5, .25, 81), (3, .5, 79)]),
    20: ("C", [(0, .5, 76), (1, .5, 79), (2, 1, 84), (3, .25, 83), (3.5, .25, 79)]),
}
for bar, (ch, mel) in HALL.items():
    for b, d, m in mel:
        T["marimba"].add(bt(bar, b), marimba(m, d * BEAT, 0.62, ring=0.5), pan=0.1)
    root = ROOT[ch]
    fifth = root + 7
    for b, m in ((0, root), (1, fifth), (2, root), (3, fifth)):
        T["bass"].add(bt(bar, b), bass_note(m, BEAT * 0.4, 0.8, kind="bassoon"))
    if bar <= 18:
        for b in (0, 2):
            T["kick"].add(bt(bar, b), kick(0.8))
        for k in range(8):
            T["perc"].add(bt(bar, k * 0.5), woodblock(k % 2 == 0, 0.55), pan=0.45 if k % 2 else -0.45)
        for b in (1, 3):
            T["snare"].add(bt(bar, b), clap(0.55))
    else:   # matchday: stomp, stomp, clap
        for b in (0, 0.5, 2, 2.5):
            T["kick"].add(bt(bar, b), kick(1.0))
            T["perc"].add(bt(bar, b), tom(120, 70, 0.55))
        for b in (1, 3):
            T["snare"].add(bt(bar, b), clap(1.0))
            T["snare"].add(bt(bar, b) + 0.012, clap(0.6))
        for k in range(16):
            T["hats"].add(bt(bar, k * 0.25), hat(False, 0.38 if k % 2 else 0.5), pan=-0.3)
T["crowd"].add(bt(19), crowd(BAR * 2 + 1.0, 1.0))
T["cym"].add(bt(19), crash(0.8))

# --- breakdown (bars 21-22): drums out, bells and pad, then a roll and a riser
for bar, mel in ((21, [(0, 1, 81), (1, 1, 79), (2, 2, 76)]), (22, [(0, 1, 74), (1, 1, 79), (2, 2, 83)])):
    for b, d, m in mel:
        T["bells"].add(bt(bar, b), bell(m, 0.55, 2.2), pan=-0.2 if b < 2 else 0.2)
    T["bass"].add(bt(bar), bass_note(ROOT[PROG[bar][0][1]] + 12, BAR * 0.95, 0.32))
roll = [2 + k * 0.25 for k in range(4)] + [3 + k * 0.125 for k in range(8)]
for k, b in enumerate(roll):
    T["snare"].add(bt(22, b), snare(0.3 + 0.6 * k / len(roll)))
T["fx"].add(bt(21, 2), riser(BAR * 1.5, 0.9))
T["cym"].add(bt(23) - 1.6, reverse_crash(1.6, 0.85))

# --- final chorus (bars 23-26): everything, a big doubled lead
chorus(23, True)

# --- the zoom out (bars 27-29): one long lift that resolves on C at bar 30
lift = [(bt(27, 0), 2 * BEAT, 81), (bt(27, 2), 2 * BEAT, 84), (bt(28, 0), 4 * BEAT, 86),
        (bt(29, 0), 1.5 * BEAT, 84), (bt(29, 1.5), 0.5 * BEAT, 83), (bt(29, 2), 2 * BEAT, 86)]
T["lead"].add(bt(27), lead_phrase(lift, bt(27), 1.2), gain=0.95)
T["lead"].add(bt(27), lead_phrase([(s, d, m - 12) for s, d, m in lift], bt(27), 0.7), gain=0.4)
for bar, ch in ((27, "F"), (28, "G"), (29, "G")):
    for k in range(8 if bar < 29 else 16):
        step = 0.5 if bar < 29 else 0.25
        T["kick"].add(bt(bar, k * step), kick(0.7 + 0.25 * k / (8 if bar < 29 else 16)))
    for b in range(8):
        T["bass"].add(bt(bar, b * 0.5), bass_note(ROOT[ch] + (12 if b % 2 else 0), BEAT * 0.42, 0.75))
    for k in range(16):   # rising 16th marimba runs
        tones = sorted(set(m + 12 for m in CH[ch]) | set(m + 24 for m in CH[ch]))
        T["marimba"].add(bt(bar, k * 0.25), marimba(tones[k % len(tones)], 0.22 * BEAT, 0.3 + 0.2 * k / 16), pan=0.3 if k % 2 else -0.3)
    for k in range(8):
        T["hats"].add(bt(bar, k * 0.5), hat(False, 0.5), pan=-0.3)
for k, b in enumerate([2 + j * 0.25 for j in range(8)]):
    T["perc"].add(bt(29, b), tom(280 - k * 20, 180 - k * 12, 0.85))
T["fx"].add(bt(27), riser(BAR * 3, 1.0))
T["cym"].add(bt(30) - 2.0, reverse_crash(2.0, 0.9))

# --- resolution and outro (bars 30-32): C, half-time, the hook motif gently, night falls
T["fx"].add(bt(30), impact(1.0))
T["cym"].add(bt(30), crash(1.0, 3.2))
T["lead"].add(bt(30), lead_phrase([(bt(30), 2.2 * BEAT, 88)], bt(30), 1.0), gain=0.8)
for bar, ch in ((30, "C"), (31, "Fmaj7"), (32, "G6")):
    play_arp(bar, ch if ch in HOOK_ARP else "C", 0.5, pan=0.15, ring=1.3)
    T["bass"].add(bt(bar), bass_note(ROOT[ch], BAR * 0.9, 0.6))
    T["kick"].add(bt(bar, 0), kick(0.75))
    T["snare"].add(bt(bar, 2), clap(0.6))
    for k in range(8):
        T["perc"].add(bt(bar, k * 0.5 + 0.25), shaker(0.3), pan=0.35)

# --- end card (bars 33-35): chime (added above), the logo flourish once more, a last low C
for k, m in enumerate((79, 84, 88, 91)):
    T["bells"].add(bt(33, 1) + k * 0.25, bell(m, 0.55, 2.6), pan=-0.3 + k * 0.2)
T["bass"].add(bt(33), bass_note(36, BAR * 1.5, 0.5))
T["marimba"].add(bt(34), marimba(72, 2.0, 0.45, ring=1.6))
T["marimba"].add(bt(34), marimba(79, 2.0, 0.35, ring=1.6), pan=0.2)

# ---------------------------------------------------------------------------------------- mix

def kick_duck(depth=0.35, release=0.13):
    """Side-chain envelope from the kick track (choruses pump a little)."""
    env = np.abs(T["kick"].buf[:, 0]).astype(np.float64)
    env = ss.sosfilt(sos("lowpass", 30), env)
    env = env / (np.max(env) + 1e-9)
    return 1.0 - depth * np.clip(env * 2.2, 0, 1)


def reverb_ir(seconds=2.3):
    n = int(seconds * SR)
    t = tt(n)
    ir = np.zeros((n, 2))
    for c in range(2):
        nz = noise(n)
        bright = nz * np.exp(-t / 0.42)
        dark = ss.sosfilt(sos("lowpass", 2400), nz) * np.exp(-t / 0.75)
        ir[:, c] = 0.55 * bright + 0.45 * dark
        for d, a in ((0.011, 0.5), (0.019, 0.35), (0.027, 0.28), (0.041, 0.2)):   # early reflections
            ir[int((d + 0.003 * c) * SR), c] += a
    ir[: int(0.006 * SR)] *= np.linspace(0, 1, int(0.006 * SR))[:, None]
    return ir / np.sqrt(np.sum(ir ** 2, axis=0, keepdims=True))


GAIN = {"kick": 0.72, "snare": 3.0, "hats": 1.25, "perc": 1.1, "cym": 0.55, "bass": 0.62, "pad": 0.38,
        "marimba": 0.55, "bells": 0.42, "lead": 0.44, "fx": 0.5, "crowd": 1.0}

# The arrangement's fader ride (dB at each time, interpolated): quiet open, big choruses, a dip for the breakdown.
RIDE = [(0.0, -7), (3.6, -7), (4.0, 0), (5.7, 0), (6.0, -6.5), (9.8, -6.5), (10.0, -3), (19.8, -3), (21.9, -0.5),
        (22.0, 0), (31.8, 0), (32.0, -3), (35.8, -3), (36.0, -1), (39.8, -1), (40.0, -7.5), (42.0, -7.5),
        (43.9, -1), (44.0, 1), (51.8, 1), (52.0, 1), (57.9, 2), (58.0, 1), (59.5, -6), (63.8, -6), (64.0, -5),
        (LENGTH, -5)]

duck = kick_duck()
dry = np.zeros((N, 2))
send = np.zeros((N, 2))
stem_rms = {}
for name, tr in T.items():
    x = tr.buf.astype(np.float64) * GAIN[name]
    if name in ("pad", "bass", "marimba"):
        x *= duck[:, None] if name != "marimba" else (0.5 + 0.5 * duck)[:, None]
    if name == "bass":
        x = ss.sosfilt(sos("lowpass", 2200), x, axis=0)
    if name in ("hats", "perc", "cym"):
        x = ss.sosfilt(sos("highpass", 300), x, axis=0)
    stem_rms[name] = float(np.sqrt(np.mean(x ** 2)) + 1e-12)
    dry += x
    send += x * tr.reverb

ir = reverb_ir()
wet = np.stack([ss.fftconvolve(send[:, c], ir[:, c])[:N] for c in range(2)], axis=1) * 0.55
mix = dry + wet
ride_db = np.interp(np.arange(N) / SR, [p[0] for p in RIDE], [p[1] for p in RIDE])
mix *= (10 ** (ride_db / 20))[:, None]
mix = ss.sosfilt(sos("highpass", 28), mix, axis=0)


# ---------------------------------------------------------------------------------------- master

def k_weight(x):
    b1, a1 = [1.53512485958697, -2.69169618940638, 1.19839281085285], [1.0, -1.69065929318241, 0.73248077421585]
    b2, a2 = [1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621]
    return ss.lfilter(b2, a2, ss.lfilter(b1, a1, x, axis=0), axis=0)


def lufs(x):
    y = k_weight(x)
    blk, hop = int(0.4 * SR), int(0.1 * SR)
    ms = np.array([np.sum(np.mean(y[i:i + blk] ** 2, axis=0)) for i in range(0, len(y) - blk, hop)])
    l = -0.691 + 10 * np.log10(ms + 1e-12)
    g = ms[l > -70]
    rel = -0.691 + 10 * np.log10(np.mean(g)) - 10
    g2 = ms[(l > -70) & (l > rel)]
    return -0.691 + 10 * np.log10(np.mean(g2)), l


def compress(x, thresh_db=-16.0, ratio=2.2, att=0.01, rel=0.18):
    lvl = np.sqrt(ss.sosfilt(sos("lowpass", 8), np.mean(x ** 2, axis=1)).clip(1e-12))
    db = 20 * np.log10(lvl)
    over = np.maximum(db - thresh_db, 0)
    gr = over * (1 - 1 / ratio)
    # attack/release smoothing of the gain reduction
    g = np.zeros_like(gr)
    a_c, r_c = np.exp(-1 / (att * SR)), np.exp(-1 / (rel * SR))
    prev = 0.0
    for i in range(0, len(gr), 64):   # block-wise for speed
        target = gr[i]
        c = a_c ** 64 if target > prev else r_c ** 64
        prev = target + (prev - target) * c
        g[i:i + 64] = prev
    return x * (10 ** (-g / 20))[:, None]


def limit(x, ceiling_db=-1.2, look=0.004, rel=0.08):
    ceil = 10 ** (ceiling_db / 20)
    peak = np.max(np.abs(x), axis=1)
    w = int(look * SR)
    peak = ss.maximum_filter1d(peak, size=2 * w + 1) if hasattr(ss, "maximum_filter1d") else \
        np.array([np.max(peak[max(0, i - w):i + w + 1]) for i in range(0, len(peak))])
    need = np.minimum(1.0, ceil / np.maximum(peak, 1e-9))
    g = np.ones_like(need)
    r_c = np.exp(-1 / (rel * SR))
    cur = 1.0
    for i in range(0, len(need), 32):
        tgt = np.min(need[i:i + 32])
        cur = tgt if tgt < cur else tgt + (cur - tgt) * (r_c ** 32)
        g[i:i + 32] = cur
    return x * g[:, None]


try:
    from scipy.ndimage import maximum_filter1d as _mf

    def limit(x, ceiling_db=-1.2, look=0.004, rel=0.08):   # noqa: F811 (faster version)
        ceil = 10 ** (ceiling_db / 20)
        peak = _mf(np.max(np.abs(x), axis=1), size=2 * int(look * SR) + 1)
        need = np.minimum(1.0, ceil / np.maximum(peak, 1e-9))
        g = np.ones_like(need)
        r_c = np.exp(-1 / (rel * SR)) ** 32
        cur = 1.0
        for i in range(0, len(need), 32):
            tgt = np.min(need[i:i + 32])
            cur = tgt if tgt < cur else tgt + (cur - tgt) * r_c
            g[i:i + 32] = cur
        return x * g[:, None]
except ImportError:
    pass

mix = compress(mix)
target = -14.0
L, _ = lufs(mix)
mix *= 10 ** ((target - L) / 20)
for _ in range(3):   # limiting lowers loudness a little; nudge back up
    out = limit(mix)
    L2, _ = lufs(out)
    if abs(L2 - target) < 0.2:
        break
    mix *= 10 ** ((target - L2) / 20)
out = limit(mix)
# the very end: fade the last second of the reverb tail
fn = int(1.2 * SR)
out[-fn:] *= np.linspace(1, 0, fn)[:, None] ** 2

# ---------------------------------------------------------------------------------------- report and write

L_final, short = lufs(out)
true_peak = 20 * np.log10(np.max(np.abs(ss.resample_poly(out, 4, 1, axis=0))) + 1e-12)
dest = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "next_stop_theme.wav")
wavfile.write(dest, SR, out.astype(np.float32))
cues = {
    "bpm": BPM, "bar_seconds": BAR, "length": LENGTH,
    "hits": {"title": bt(3), "line_built": bt(7), "chorus": bt(12), "card": bt(13), "city_hall": bt(17),
             "matchday": bt(19), "breakdown": bt(21), "final_chorus": bt(23), "zoom": bt(27), "resolve": bt(30),
             "end_card": bt(33)},
}
with open(os.path.join(HERE, "music_cues.json"), "w") as f:
    json.dump(cues, f, indent=1)
sections = [("cold", 1), ("title", 3), ("intro", 4), ("verse", 6), ("build", 11), ("chorus", 12), ("hall", 17),
            ("matchday", 19), ("break", 21), ("final", 23), ("zoom", 27), ("outro", 30), ("end", 33)]
print(f"wrote {dest}: {LENGTH:.1f} s, integrated {L_final:.1f} LUFS, true peak {true_peak:.1f} dBTP")
print("stem RMS (dBFS): " + ", ".join(f"{k} {20 * np.log10(v):.1f}" for k, v in stem_rms.items()))
for k, (name, bar) in enumerate(sections):
    end_bar = sections[k + 1][1] if k + 1 < len(sections) else 36
    i0, i1 = int((bt(bar)) / 0.1), int(bt(end_bar) / 0.1)
    seg = short[i0:i1]
    print(f"  {name:9s} {bt(bar):5.1f}-{bt(end_bar):5.1f} s  momentary loudness {np.percentile(seg, 50):6.1f} LUFS (max {np.max(seg):6.1f})")
