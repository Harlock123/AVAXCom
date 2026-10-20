#!/usr/bin/env python3
"""Synthesises AVAXCom's sound effects, voices and music into ../src/AVAXCom/Sounds as 16-bit mono WAV.

Nothing is sampled from any game or recording: every effect is synthesised here from square, saw, sine and noise
voices. The title plays the opening of Frederic Chopin's Funeral March (Marche funebre, the third movement of the
Piano Sonata No. 2 in B-flat minor, Op. 35) on a synthesised low piano and organ in a dark hall; the battlescape
ambience, the geoscape theme, the victory fanfare and the lament for a lost campaign are new compositions. VIGIL's
radio operator is spoken by the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAXCom", "Sounds")
RNG = random.Random(1994)


def write(name, samples, volume=0.7, fade=0.003, fade_in=0.0):
    """Normalises to the given peak and writes; a few milliseconds' fade at the ends so nothing clicks."""
    samples = list(samples)
    n = len(samples)
    k = min(n // 2, int(RATE * fade))
    for i in range(k):
        samples[n - 1 - i] *= i / k
    k = min(n // 2, int(RATE * fade_in))
    for i in range(k):
        samples[i] *= i / k
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def lowpass(samples, k):
    """A one-pole filter: k near 0 is dark, near 1 bright."""
    out, y = [], 0.0
    for s in samples:
        y += k * (s - y)
        out.append(y)
    return out


def highpass(samples, k):
    """What the one-pole lowpass takes away: the hiss and sizzle."""
    return [s - d for s, d in zip(samples, lowpass(samples, k))]


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


def gain(samples, g):
    return [s * g for s in samples]


def ring(samples, freq, depth=1.0):
    """Ring modulation: the sound multiplied by a sine, for buzzy, unearthly sidebands."""
    w = 2 * math.pi * freq / RATE
    return [s * (1 - depth + depth * math.sin(w * i)) for i, s in enumerate(samples)]


def echo(samples, delays=((0.07, 0.35), (0.13, 0.22), (0.21, 0.12))):
    """A hall's echoes: copies of the sound a little later and quieter."""
    tail = int(RATE * max(d for d, _ in delays))
    out = samples + [0.0] * tail
    for d, g in delays:
        k = int(RATE * d)
        for i, s in enumerate(samples):
            out[i + k] += s * g
    return out


HALL = ((0.09, 0.32), (0.17, 0.22), (0.29, 0.15), (0.43, 0.09), (0.61, 0.05))


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def place(buffer, at, samples):
    """Adds a sound into a longer buffer at a time (seconds)."""
    k = int(at * RATE)
    if k + len(samples) > len(buffer):
        buffer.extend([0.0] * (k + len(samples) - len(buffer)))
    for i, s in enumerate(samples):
        buffer[k + i] += s


def fold(buffer, seconds):
    """Wraps everything past the loop's length back onto its start, so the end runs seamlessly into the beginning."""
    n = int(seconds * RATE)
    out = [0.0] * n
    for i, s in enumerate(buffer):
        out[i % n] += s
    return out


# ───────────────────────────── instruments ─────────────────────────────

def organ(seconds, note, gain=0.5):
    """A pipe organ: a saw and the octaves above and below, swelling in and dying away slowly."""
    f = hz(note)
    n = int(RATE * seconds)
    out = []
    p1 = p2 = p3 = 0.0
    for i in range(n):
        t = i / RATE
        p1 += f / RATE
        p2 += f * 2 / RATE
        p3 += f / 2 / RATE
        s = 0.45 * voice("saw", p1) + 0.25 * voice("square", p2) * 0.6 + 0.35 * voice("tri", p3)
        env = min(1.0, t / 0.025) * min(1.0, (seconds - t) / 0.06 + 0.0)
        out.append(s * max(0.0, env) * gain)
    return out


def brass(seconds, note, gain=0.5):
    """A brassy voice: a saw that brightens as it swells."""
    f = hz(note)
    n = int(RATE * seconds)
    out, phase, y = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        phase += f * (1 + 0.004 * math.sin(2 * math.pi * 5.5 * t)) / RATE
        s = voice("saw", phase)
        k = 0.08 + 0.5 * min(1.0, t / 0.08)
        y += k * (s - y)
        env = min(1.0, t / 0.03) * min(1.0, max(0.0, (seconds - t) / 0.08))
        out.append(y * env * gain)
    return out


_PIANO = {}


def piano(seconds, note, gain=0.5):
    """A dark piano: a few slightly stretched partials, each dying faster than the one below, after a soft hammer."""
    key = (seconds, note, gain)
    if key in _PIANO:
        return _PIANO[key]
    f = hz(note)
    n = int(RATE * (seconds + 0.12))
    partials = []
    for h in range(1, 7):
        fh = f * h * math.sqrt(1 + 0.0004 * h * h)
        if fh > RATE / 2.5:
            break
        partials.append((2 * math.pi * fh / RATE, (1.0 / h ** 1.4) * (0.5 if h == 2 else 1.0), 0.7 + 0.55 * h + f / 600))
    out = []
    for i in range(n):
        t = i / RATE
        s = 0.0
        for w, a, d in partials:
            s += a * math.exp(-d * t) * math.sin(w * i)
        env = min(1.0, t / 0.006) * min(1.0, max(0.0, (seconds + 0.12 - t) / 0.12))
        out.append(s * env * gain)
    _PIANO[key] = out
    return out


def bell(seconds, note, gain=0.5, f=None):
    """A bell or chime: inharmonic sine partials, the high ones fading first."""
    f = f or hz(note)
    n = int(RATE * seconds)
    parts = [(1.0, 1.0, 2.5), (2.0, 0.5, 3.5), (2.76, 0.35, 5.0), (5.4, 0.2, 8.0), (8.93, 0.1, 12.0)]
    out = []
    for i in range(n):
        t = i / RATE
        s = sum(a * math.exp(-d * t / seconds) * math.sin(2 * math.pi * f * m * t) for m, a, d in parts)
        out.append(s * min(1.0, t / 0.002) * gain)
    return out


def pad(seconds, freqs, gain=0.3, attack=2.0, release=3.0, bright=0.04, sweep=0.03, sweep_rate=0.1, detune=0.004):
    """A synth pad: pairs of slightly detuned saws through a slowly breathing filter, swelling in and out."""
    n = int(RATE * seconds)
    phases = [[0.0, 0.33] for _ in freqs]
    out, y1, y2 = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        s = 0.0
        for j, f in enumerate(freqs):
            ph = phases[j]
            ph[0] += f * (1 - detune) / RATE
            ph[1] += f * (1 + detune) / RATE
            s += (ph[0] % 1.0) + (ph[1] % 1.0) - 1.0
        k = max(0.005, bright + sweep * math.sin(2 * math.pi * sweep_rate * t))
        y1 += k * (s - y1)
        y2 += k * (y1 - y2)
        a = min(1.0, t / attack)
        r = min(1.0, max(0.0, (seconds - t) / release))
        env = (math.sin(a * math.pi / 2) * math.sin(r * math.pi / 2)) ** 2
        out.append(y2 * env * gain / len(freqs))
    return out


def kick():
    return mix(tone(0.16, 120, 40, "sine", attack=0.001, release=1.5, gain=1.0), tone(0.02, 3000, 1000, "noise", attack=0.001, release=1, gain=0.3))


def boom(seconds=0.9):
    return lowpass(mix(tone(seconds, 300, 40, "noise", attack=0.002, release=1.4), tone(seconds * 0.7, 70, 30, "sine", attack=0.002, release=1.2, gain=0.8)), 0.25)


def clang(seconds, f, gain=0.5):
    """Struck metal: a short bell of high inharmonic partials."""
    return bell(seconds, None, gain, f=f)


# ───────────────────────────── music ─────────────────────────────

BBM = ["Bb1", "Bb2", "Db3", "F3"]
GB = ["Gb1", "Bb2", "Db3", "Gb3"]
DB = ["Db2", "Ab2", "Db3", "F3"]
AB7 = ["Ab1", "C3", "Eb3", "Gb3"]
FMAJ = ["F1", "C3", "Eb3", "A3"]


def funeral_march():
    """The opening of Chopin's Marche funebre, slowly (a crotchet a second): two bars of the left hand rocking between
    B-flat minor and G-flat major on every beat; the theme (B-flat repeated in its dotted funeral rhythm, then
    D-flat, C, B-flat, A, B-flat), twice; the answering phrase lifted into D-flat major, climbing to G-flat and
    falling back through F7 to B-flat; and the theme once more. The bass is doubled by a soft organ pedal, and the
    hall's echoes wrap round so the end runs straight back into the rocking introduction."""
    beat = 1.0
    bars = [[BBM, GB, BBM, GB]] * 2                # the introduction
    bars += [[BBM, GB, BBM, GB]] * 4               # the theme, twice
    bars += [[DB, GB, DB, GB], [DB, GB, AB7, DB],  # the answer in D-flat
             [GB, GB, BBM, BBM], [FMAJ, FMAJ, FMAJ, BBM]]
    bars += [[BBM, GB, BBM, GB]] * 2               # the theme again
    theme = [("Bb4", 1), ("Bb4", 0.75), ("Bb4", 0.25), ("Bb4", 2),
             ("Db5", 0.75), ("C5", 0.25), ("C5", 0.75), ("Bb4", 0.25), ("Bb4", 0.75), ("A4", 0.25), ("Bb4", 1)]
    answer = [("Db5", 1), ("Db5", 0.75), ("Db5", 0.25), ("Db5", 2),
              ("F5", 0.75), ("Eb5", 0.25), ("Eb5", 0.75), ("Db5", 0.25), ("Db5", 0.75), ("C5", 0.25), ("Db5", 1),
              ("Gb5", 2), ("F5", 0.75), ("Eb5", 0.25), ("Db5", 1),
              ("C5", 0.75), ("Db5", 0.25), ("Eb5", 0.75), ("C5", 0.25), ("A4", 1), ("Bb4", 1)]
    melody = [("-", 8)] + theme * 2 + answer + theme
    buf = []
    for b, bar in enumerate(bars):
        for k, chord in enumerate(bar):
            at = (b * 4 + k) * beat
            for i, n in enumerate(chord):
                place(buf, at + 0.012 * i, piano(beat * 1.15, n, 0.30 if i == 0 else 0.17))
            if k % 2 == 0:
                place(buf, at, organ(beat * 2.0, chord[0], 0.07))
    t = 0.0
    for n, d in melody:
        if n != "-":
            place(buf, t, piano(d * beat * 1.08, n, 0.42))
            place(buf, t + 0.004, piano(d * beat * 1.08, n[:-1] + str(int(n[-1]) - 1), 0.16))
            place(buf, t, organ(d * beat * 0.98, n, 0.035))
        t += d * beat
    length = len(bars) * 4 * beat
    return fold(lowpass(echo(buf, HALL), 0.45), length)


def ambient():
    """The battlescape: a low drone that never quite settles, distant metal ringing in the dark, and now and then a
    slow heartbeat. Every sound is wrapped round the loop's length, so it runs on forever without a seam."""
    length = 36.0
    buf = []
    segments = [["A1", "E2"], ["A1", "E2", "F2"], ["A1", "Eb2", "Bb2"]]
    for i, notes in enumerate(segments):
        place(buf, i * 12.0, pad(16.0, [hz(n) for n in notes], 0.5, attack=4.0, release=4.0, bright=0.025, sweep=0.02, sweep_rate=1 / 8.0, detune=0.006))
        # A whisper of air under it, swelling with it.
        place(buf, i * 12.0 + 2.0, lowpass(tone(12.0, 180, 120, "noise", attack=4.0, release=0.6, gain=0.05), 0.06))
    # A sub-bass throb that comes and goes.
    place(buf, 0.0, tone(36.0, 41.2, kind="sine", attack=6.0, release=0.8, vibrato=0.01, vib_rate=0.25, gain=0.25))
    place(buf, 18.0, tone(36.0, 41.2, kind="sine", attack=6.0, release=0.8, vibrato=0.01, vib_rate=0.25, gain=0.25))
    # Distant metallic pings.
    rng = random.Random(51)
    t = 1.5
    while t < length:
        place(buf, t, echo(gain(clang(1.8, rng.choice([622.0, 740.0, 880.0, 1046.0, 1245.0, 1397.0]), 0.06), 1.0), HALL))
        t += rng.uniform(2.5, 5.5)
    # Heartbeats: lub-dub, slowing.
    for start in (7.0, 25.0):
        for k in range(5):
            at = start + k * (0.95 + 0.05 * k)
            place(buf, at, lowpass(tone(0.18, 70, 38, "sine", attack=0.004, release=1.4, gain=0.55), 0.5))
            place(buf, at + 0.26, lowpass(tone(0.15, 60, 34, "sine", attack=0.004, release=1.4, gain=0.35), 0.5))
    return fold(buf, length)


def geoscape():
    """The command centre: slow minor chords on a pad (C minor, A-flat, F minor, G), and a quiet computer arpeggio
    that ticks over them, dropping out and coming back. 48 seconds, wrapped round so it loops seamlessly."""
    length = 48.0
    step = 0.25
    chords = [(["C2", "C3", "Eb3", "G3"], ["C5", "G4", "Eb5", "G4", "D5", "G4", "Eb5", "Bb4"]),
              (["Ab1", "Ab2", "C3", "Eb3"], ["C5", "Ab4", "Eb5", "Ab4", "C5", "G4", "Eb5", "Ab4"]),
              (["F1", "F2", "Ab2", "C3"], ["F4", "Ab4", "C5", "Ab4", "F5", "C5", "Ab4", "C5"]),
              (["G1", "G2", "B2", "D3"], ["G4", "B4", "D5", "B4", "F5", "D5", "B4", "Ab4"])]
    buf = []
    for i, (notes, arp) in enumerate(chords):
        at = i * 12.0
        place(buf, at, pad(15.0, [hz(n) for n in notes], 0.55, attack=3.0, release=4.0, bright=0.03, sweep=0.025, sweep_rate=1 / 12.0))
        # The arpeggio plays in some bars and rests in others; it always rests in the first.
        for s in range(48):
            bar = s // 16
            if (i, bar) in ((0, 0), (2, 2)) or (s % 16) in (7, 15) or (bar == 1 and s % 4 == 3):
                continue
            n = arp[s % 8]
            blip = mix(tone(0.16, hz(n), kind="pulse", attack=0.003, release=2.0, gain=0.06),
                       tone(0.2, hz(n) * 2, kind="sine", attack=0.002, release=2.5, gain=0.04))
            place(buf, at + s * step, lowpass(echo(blip, ((0.375, 0.35), (0.75, 0.15))), 0.35))
        # A low pulse at each chord change, like a slow console tick.
        place(buf, at, lowpass(tone(0.5, 80, 50, "sine", attack=0.01, release=1.5, gain=0.2), 0.5))
    return fold(buf, length)


def victory():
    """A brass fanfare in B-flat: a triplet call, a climb up the chord, and a held high F over the full band."""
    beat = 0.5
    buf = []
    for i in range(3):
        place(buf, i * beat / 3, brass(beat / 3 * 0.9, "F4", 0.5))
    seq = [("Bb4", 1), ("F4", 0.5), ("Bb4", 0.5), ("D5", 1), ("C5", 0.5), ("D5", 0.5), ("F5", 3.5)]
    t = beat
    for n, b in seq:
        place(buf, t, brass(b * beat * 0.95, n, 0.5))
        t += b * beat
    held = beat * 5
    for n in ["Bb2", "F3", "Bb3", "D4"]:
        place(buf, held, brass(beat * 3.5, n, 0.25))
    for n, at in (("Bb2", beat), ("F3", beat * 2), ("Bb2", beat * 3), ("F2", beat * 4)):
        place(buf, at, brass(beat * 0.9, n, 0.25))
    place(buf, beat, kick())
    place(buf, held, kick())
    place(buf, held, gain(lowpass(tone(1.5, 6000, 3000, "noise", attack=0.002, release=2.5), 0.6), 0.15))
    return echo(buf)


def lament():
    """A slow organ lament in B-flat minor, settling on a bare low B-flat."""
    beat = 0.75
    buf = []
    t = 0.0
    for n, b in [("F4", 1), ("Gb4", 1), ("F4", 0.5), ("Eb4", 0.5), ("Db4", 1), ("C4", 1), ("Bb3", 3)]:
        place(buf, t, organ(b * beat * 0.98, n, 0.45))
        t += b * beat
    for n, at, b in [("Bb2", 0, 2), ("Gb2", 2, 2), ("F2", 4, 2), ("Bb1", 6, 2)]:
        place(buf, at * beat, organ(b * beat * 0.98, n, 0.35))
    place(buf, 6 * beat, organ(2 * beat * 0.98, "F2", 0.2))
    return echo(buf, HALL)


# ───────────────────────────── voices ─────────────────────────────

LINES = {
    "SayUfoDetected": "Y UW1 EH1 F OW1 , D IH T EH1 K T IH D",
    "SayHostile": "HH AA1 S T AY L , S AY1 T IH D !",
    "SayMissionComplete": "M IH1 SH AH N , K AH M P L IY1 T",
}


def radio(samples):
    """A voice over the field radio: a little band-limited, with a click before and a hiss after."""
    dark = lowpass(samples, 0.55)
    bright = [s - d for s, d in zip(samples, lowpass(samples, 0.08))]
    out = [0.6 * a + 0.4 * b for a, b in zip(dark, bright)]
    return cat(tone(0.012, 2500, kind="square", attack=0.001, release=1, gain=0.25), out, tone(0.05, 6000, kind="noise", attack=0.001, release=1, gain=0.06))


# ───────────────────────────── effects ─────────────────────────────

def interface():
    write("Click.wav", tone(0.03, 1400, 900, "square", attack=0.001, release=1.2, gain=0.6), 0.25)
    write("Refused.wav", cat(tone(0.09, 160, kind="square", release=0.4), silence(0.03), tone(0.12, 110, kind="square", release=0.6)), 0.25)
    # The till: a drawer's clunk, coins spilling, and the bell.
    buf = mix(lowpass(tone(0.08, 900, 200, "noise", attack=0.001, release=1.3), 0.3), tone(0.07, 140, 70, "sine", attack=0.001, release=1.3, gain=0.6))
    rng = random.Random(7)
    for k in range(6):
        place(buf, 0.05 + k * 0.035 + rng.uniform(0, 0.02), clang(0.12, rng.uniform(2800, 4200), 0.18))
    place(buf, 0.12, bell(0.6, "E6", 0.35))
    place(buf, 0.12, bell(0.6, "C7", 0.18))
    write("Cash.wav", buf, 0.35)
    # Production complete: a heavy thunk, then a two-note chime.
    buf = mix(tone(0.3, 140, 50, "sine", attack=0.002, release=1.5), lowpass(tone(0.25, 900, 300, "noise", attack=0.001, release=1.2, gain=0.7), 0.25))
    place(buf, 0.25, bell(0.6, "E5", 0.35))
    place(buf, 0.45, bell(0.8, "A5", 0.35))
    write("Build.wav", buf, 0.45)
    # Research complete: a bright rising chime.
    buf = []
    for i, n in enumerate(["G5", "C6", "E6", "G6"]):
        place(buf, i * 0.12, bell(1.0 if i < 3 else 1.4, n, 0.3))
    write("ResearchDone.wav", echo(buf), 0.4)
    # The klaxon: three rising whoops.
    whoop = lowpass(mix(tone(0.45, 300, 560, "saw", attack=0.02, release=0.25), tone(0.45, 302, 566, "square", attack=0.02, release=0.25, gain=0.5)), 0.35)
    write("Alarm.wav", cat(whoop, silence(0.05), whoop, silence(0.05), whoop, silence(0.05)), 0.4)


def geoscape_effects():
    # Radar: two sonar pings sweeping down, then the urgent two-tone alert.
    buf = []
    for at in (0.0, 0.35):
        place(buf, at, echo(tone(0.35, 1500, 1350, "sine", attack=0.002, release=2.5, gain=0.5), ((0.08, 0.3), (0.16, 0.15))))
        place(buf, at, highpass(tone(0.25, 3000, 8000, "noise", attack=0.1, release=1.5, gain=0.08), 0.3))
    for k in range(6):
        place(buf, 0.75 + k * 0.12, tone(0.09, 1760 if k % 2 == 0 else 1320, kind="square", attack=0.002, release=0.3, gain=0.25))
    write("UfoDetected.wav", buf, 0.35)
    # The interceptor scrambles: a turbine whine rising and the roar of the jets.
    write("Launch.wav", mix(tone(2.0, 500, 2600, "sine", attack=0.3, release=0.5, vibrato=0.004, vib_rate=40, gain=0.25),
                            tone(2.0, 250, 1300, "saw", attack=0.3, release=0.5, gain=0.06),
                            lowpass(tone(2.0, 400, 3000, "noise", attack=0.6, release=0.6), 0.25),
                            lowpass(tone(2.0, 70, 110, "saw", attack=0.5, release=0.8, gain=0.4), 0.2)), 0.5)
    shot = mix(lowpass(tone(0.06, 5000, 1200, "noise", attack=0.0005, release=1.6), 0.55), tone(0.05, 180, 70, "sine", attack=0.001, release=1.3, gain=0.6))
    write("AirCannon.wav", cat(*[cat(shot, silence(0.01)) for _ in range(6)]), 0.4)
    write("AirMissile.wav", mix(tone(0.05, 300, 100, "square", attack=0.001, release=1, gain=0.4),
                                lowpass(tone(0.9, 800, 4000, "noise", attack=0.08, release=1.0), 0.35),
                                tone(0.9, 900, 400, "saw", attack=0.08, release=1.2, gain=0.05)), 0.4)
    write("AirLaser.wav", mix(tone(0.28, 2600, 300, "saw", attack=0.001, release=1.2, gain=0.5), tone(0.28, 1300, 200, "sine", attack=0.001, release=1, gain=0.5)), 0.35)
    bolt = mix(ring(tone(0.65, 240, 70, "saw", attack=0.005, release=0.9, vibrato=0.05, vib_rate=31), 37, 0.8),
               lowpass(tone(0.5, 2500, 600, "noise", attack=0.002, release=1.4, gain=0.5), 0.4))
    write("AirStorm.wav", lowpass(bolt, 0.5), 0.45)
    write("AirHit.wav", mix(clang(0.5, 410, 0.4), clang(0.4, 637, 0.3), boom(0.7)), 0.5)
    # A UFO going down: a falling, failing whine, the crunch of impact and the blast.
    buf = mix(tone(1.6, 1800, 180, "sine", attack=0.02, release=0.3, vibrato=0.03, vib_rate=9, gain=0.35),
              tone(1.6, 1200, 120, "saw", attack=0.02, release=0.4, vibrato=0.05, vib_rate=13, gain=0.12))
    place(buf, 1.5, lowpass(tone(0.3, 2000, 300, "noise", attack=0.001, release=1.2, gain=0.8), 0.4))
    place(buf, 1.55, lowpass(mix(tone(1.0, 200, 25, "noise", attack=0.003, release=1.2), tone(0.9, 60, 24, "sine", attack=0.003, release=1.0, gain=0.9)), 0.2))
    write("UfoCrash.wav", buf, 0.6)
    # The transport lands: engines spooling down, a clunk, and the ramp's hydraulic hiss.
    buf = mix(tone(1.8, 1400, 250, "sine", attack=0.02, release=0.6, gain=0.15),
              lowpass(tone(1.8, 3000, 400, "noise", attack=0.02, release=0.9), 0.25),
              lowpass(tone(1.8, 110, 45, "saw", attack=0.02, release=0.9, gain=0.5), 0.15))
    place(buf, 1.45, mix(tone(0.2, 120, 50, "sine", attack=0.001, release=1.4, gain=0.6), lowpass(tone(0.12, 1500, 300, "noise", attack=0.001, release=1.3, gain=0.4), 0.3)))
    place(buf, 1.6, highpass(tone(0.9, 7000, 5000, "noise", attack=0.08, release=0.7, gain=0.35), 0.35))
    write("TransportLand.wav", buf, 0.5)


def battlescape_effects():
    write("Door.wav", mix(highpass(lowpass(tone(0.45, 6000, 2000, "noise", attack=0.12, release=1.2, gain=0.6), 0.4), 0.05),
                          tone(0.4, 300, 700, "sine", attack=0.05, release=1, gain=0.15),
                          cat(silence(0.38), tone(0.08, 110, 60, "sine", attack=0.001, release=1.4, gain=0.4))), 0.35)
    write("Step.wav", mix(lowpass(tone(0.05, 2000, 600, "noise", attack=0.002, release=1.5), 0.3), tone(0.05, 95, 60, "sine", attack=0.002, release=1.5, gain=0.5)), 0.2)
    write("Rifle.wav", mix(lowpass(tone(0.18, 7000, 900, "noise", attack=0.0005, release=2.0), 0.7),
                           tone(0.1, 180, 55, "sine", attack=0.001, release=1.4, gain=0.6)), 0.5)
    write("Cannon.wav", mix(tone(0.45, 95, 34, "sine", attack=0.001, release=1.3), lowpass(tone(0.4, 2000, 200, "noise", attack=0.001, release=1.5, gain=0.9), 0.3)), 0.6)
    write("Laser.wav", mix(tone(0.2, 1900, 380, "square", attack=0.001, release=1.0, gain=0.3), tone(0.2, 2400, 500, "sine", attack=0.001, release=1, gain=0.5)), 0.35)
    # Alien plasma: a throaty growl ring-modulated into a buzz, with sparks crackling through it.
    growl = ring(tone(0.75, 170, 55, "saw", attack=0.003, release=0.9, vibrato=0.06, vib_rate=23), 47, 0.85)
    crackle = [0.0] * len(growl)
    rng = random.Random(3)
    for _ in range(40):
        k = rng.randrange(0, len(crackle) - 200)
        for j in range(rng.randrange(20, 120)):
            crackle[k + j] += rng.uniform(-1, 1) * 0.6 * (1 - k / len(crackle))
    write("Storm.wav", mix(lowpass(growl, 0.5), crackle, tone(0.08, 3000, 800, "saw", attack=0.001, release=1, gain=0.3)), 0.5)
    # A grenade tossed: the whoosh of the arm, a clunk and a little bounce.
    buf = lowpass(tone(0.3, 1500, 3000, "noise", attack=0.12, release=1.2, gain=0.5), 0.3)
    place(buf, 0.45, mix(tone(0.08, 320, 200, "sine", attack=0.001, release=1.4, gain=0.6), clang(0.08, 1300, 0.12), lowpass(tone(0.06, 1500, 400, "noise", attack=0.001, release=1.4, gain=0.4), 0.4)))
    place(buf, 0.62, gain(mix(tone(0.06, 300, 220, "sine", attack=0.001, release=1.4, gain=0.6), clang(0.06, 1300, 0.12)), 0.5))
    write("Throw.wav", buf, 0.35)
    write("Explosion.wav", boom(1.0), 0.6)
    buf = lowpass(mix(tone(2.0, 200, 25, "noise", attack=0.004, release=1.2), tone(1.5, 55, 22, "sine", attack=0.004, release=1.0, gain=0.9),
                      cat(silence(0.15), tone(1.0, 3000, 400, "noise", attack=0.01, release=2, gain=0.3))), 0.2)
    rng = random.Random(11)
    for _ in range(18):
        at = rng.uniform(0.35, 1.8)
        place(buf, at, gain(mix(lowpass(tone(0.04, 3000, 1000, "noise", attack=0.001, release=1.5), 0.5), clang(0.05, rng.uniform(900, 2400), 0.2)), 0.25 * (1.9 - at)))
    write("BigExplosion.wav", buf, 0.75)
    # An alien's death: two inharmonic screeching voices warbling against each other as they fall.
    scream = mix(tone(1.0, 900, 240, "saw", attack=0.01, release=0.8, vibrato=0.08, vib_rate=13, gain=0.4),
                 tone(1.0, 1370, 310, "saw", attack=0.01, release=0.8, vibrato=0.1, vib_rate=17, gain=0.3),
                 tone(1.0, 2300, 700, "sine", attack=0.01, release=1.2, vibrato=0.06, vib_rate=7, gain=0.3))
    write("AlienDeath.wav", lowpass(ring(scream, 61, 0.6), 0.5), 0.45)
    # A soldier's death cry, and a grunt when hit.
    cry = say("AA1 AA AA AH", rate=RATE, pitch=150, speed=0.55, size=1.0, bend=lambda x: 1.25 - 0.6 * x)
    write("SoldierDeath.wav", lowpass(cry, 0.6), 0.5)
    grunt = say("UH1 HH", rate=RATE, pitch=120, speed=1.3, size=1.05, bend=lambda x: 1.2 - 0.35 * x)
    write("SoldierHurt.wav", lowpass(grunt, 0.6), 0.45)
    write("ReactionFire.wav", cat(tone(0.045, 2200, kind="square", attack=0.001, release=0.5, gain=0.4), silence(0.015),
                                  tone(0.06, 2900, kind="square", attack=0.001, release=1.0, gain=0.4)), 0.25)
    # Psionics: close tones beating slowly, a fifth above, and a whispering shimmer.
    n = int(2.0 * RATE)
    hum = []
    for i in range(n):
        t = i / RATE
        s = (math.sin(2 * math.pi * 220 * t) + math.sin(2 * math.pi * 223.5 * t) + 0.6 * math.sin(2 * math.pi * 329 * t)
             + 0.6 * math.sin(2 * math.pi * 333.2 * t) + 0.3 * math.sin(2 * math.pi * 110.7 * t))
        env = math.sin(math.pi * min(1.0, t / 2.0)) ** 1.2
        hum.append(s * env * 0.2)
    whisper = highpass(lowpass(tone(2.0, 9000, 6000, "noise", attack=0.6, release=0.8, gain=0.5), 0.6), 0.2)
    whisper = ring(whisper, 5.5, 0.7)
    write("Psionic.wav", echo(mix(hum, whisper), HALL), 0.45)
    write("Melee.wav", mix(highpass(tone(0.13, 9000, 2500, "noise", attack=0.01, release=1.3, gain=0.6), 0.3),
                           cat(silence(0.08), mix(tone(0.15, 120, 45, "sine", attack=0.001, release=1.3), lowpass(tone(0.1, 1200, 300, "noise", attack=0.001, release=1.4, gain=0.6), 0.3)))), 0.5)
    # Hidden movement: a low boom and a dark, swelling tritone with a high dissonant tremble.
    buf = mix(lowpass(tone(1.5, 65.4, kind="saw", attack=0.25, release=1.2, gain=0.5), 0.06),
              lowpass(tone(1.5, 92.5, kind="saw", attack=0.25, release=1.2, gain=0.4), 0.06),
              lowpass(tone(0.6, 70, 30, "sine", attack=0.002, release=1.2, gain=0.8), 0.5),
              ring(tone(1.4, 1108, kind="sine", attack=0.4, release=1.0, gain=0.07), 9, 1.0),
              ring(tone(1.4, 1174, kind="sine", attack=0.4, release=1.0, gain=0.07), 7, 1.0))
    write("HiddenMovement.wav", echo(buf, HALL), 0.5)
    write("TurnStart.wav", cat(highpass(tone(0.03, 5000, kind="noise", attack=0.001, release=1, gain=0.3), 0.3),
                               tone(0.07, 1200, kind="sine", attack=0.002, release=0.3, gain=0.5), silence(0.02),
                               tone(0.1, 1600, kind="sine", attack=0.002, release=0.8, gain=0.5)), 0.3)


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    interface()
    geoscape_effects()
    battlescape_effects()
    # VIGIL's radio operator.
    for name, words in LINES.items():
        write(name + ".wav", radio(say(words, rate=RATE, pitch=104, speed=0.95, size=1.0)), 0.5)
    # The music. The loops fade a few milliseconds at each end; they are already wrapped, so nothing clicks.
    write("TitleTune.wav", funeral_march(), 0.4, fade=0.004, fade_in=0.004)
    write("Ambient.wav", ambient(), 0.35, fade=0.004, fade_in=0.004)
    write("GeoMusic.wav", geoscape(), 0.3, fade=0.004, fade_in=0.004)
    write("Victory.wav", victory(), 0.4)
    write("GameOver.wav", lament(), 0.42)


if __name__ == "__main__":
    main()
