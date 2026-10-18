#!/usr/bin/env python3
"""Synthesises AVAJetSetWilly's sound effects and tunes into ../src/AVAJetSetWilly/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices. The title plays
the opening of Beethoven's Moonlight Sonata (1801); the tune about the house is a new composition. Willy's one line
is spoken by the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAJetSetWilly", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
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


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Willy.
    write("Jump.wav", cat(tone(0.16, 420, 900, "square", attack=0.002, release=0.3, gain=0.5), tone(0.16, 900, 420, "square", release=0.6, gain=0.5)), 0.12)
    write("Land.wav", tone(0.04, 180, 90, "square", attack=0.001, release=1.2, gain=0.5), 0.1)
    write("Item.wav", cat(*[tone(0.04, hz(n), kind="square", release=0.4) for n in ["G6", "B6", "D7"]]), 0.2)
    write("AllTaken.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6", "E6", "G6", "C7"]], tone(0.3, hz("C7"), kind="pulse", vibrato=0.02, release=0.5)), 0.28)
    write("Room.wav", tone(0.03, 600, 900, "square", attack=0.001, release=0.8, gain=0.4), 0.08)
    write("Crumble.wav", tone(0.25, 900, 200, "noise", attack=0.002, release=1.0, gain=0.6), 0.2)
    write("Die.wav", cat(*[tone(0.035, 1600 - i * 60, kind="square", release=0.2, gain=0.6) for i in range(24)]), 0.32)
    write("Tick.wav", tone(0.025, 1800, 1200, "square", attack=0.001, release=1.0, gain=0.5), 0.12)
    write("Stomp.wav", mix(tone(0.5, 90, 35, "sine", attack=0.002, release=0.9), tone(0.35, 700, 120, "noise", attack=0.001, release=1.0, gain=0.7)), 0.5)
    # The hall clock striking the hour.
    write("Chime.wav", cat(*[tone(0.45, hz(n), kind="sine", attack=0.002, release=0.9, vibrato=0.003, vib_rate=5) for n in ["E5", "C5", "D5", "G4"]]), 0.22)
    # "Sleep!" - when everything is put away.
    words = "S L IY1 P !"
    write("SayBed.wav", mix(say(words, rate=RATE, pitch=130, speed=1.0, size=0.95), [s * 0.3 for s in say(words, rate=RATE, pitch=195, speed=1.0, size=0.95, seed=11)]), 0.5)

    # About the house (an original composition): a jaunty walk round and round.
    e = 0.5
    lead = [("G4", e), ("B4", e), ("D5", e), ("B4", e), ("C5", 1), ("A4", 1), ("B4", e), ("D5", e), ("G5", e), ("F#5", e), ("E5", 1), ("D5", 1),
            ("C5", e), ("E5", e), ("A5", e), ("G5", e), ("F#5", e), ("D5", e), ("E5", e), ("F#5", e), ("G5", 1), ("D5", e), ("B4", e), ("G4", 2)]
    bass = [("G2", e), ("D3", e)] * 2 + [("A2", e), ("D3", e)] * 2 + [("G2", e), ("D3", e)] * 2 + [("C3", e), ("G2", e)] * 2 + \
           [("A2", e), ("E3", e)] * 2 + [("D3", e), ("A2", e)] * 2 + [("G2", e), ("D3", e)] * 2 + [("G2", e), ("D3", e), ("G2", 1)]
    write("Music.wav", tune(150, lead * 2, bass * 2, kind="pulse"), 0.26)
    # The Moonlight Sonata (Beethoven, 1801): the opening bars, the triplets over the octaves in the bass.
    t = 1 / 3
    def trip(a, b, c, n=4):
        return [(a, t), (b, t), (c, t)] * n
    arps = trip("G#3", "C#4", "E4") + trip("G#3", "C#4", "E4") + trip("A3", "C#4", "E4", 2) + trip("A3", "D4", "F#4", 2) + \
           [("G#3", t), ("C4", t), ("F#4", t), ("G#3", t), ("C#4", t), ("E4", t), ("G#3", t), ("C#4", t), ("D#4", t), ("F#3", t), ("C4", t), ("D#4", t)] + \
           trip("E3", "G#3", "C#4") + trip("G#3", "C#4", "E4")
    lows = [("C#3", 4), ("B2", 4), ("A2", 2), ("F#2", 2), ("G#2", 4), ("C#3", 4), ("C#3", 4)]
    write("TitleTune.wav", cat(tune(56, arps, lows, kind="tri"), silence(1.2)), 0.36)
    write("Victory.wav", tune(150, [("G5", 0.5), ("B5", 0.5), ("D6", 0.5), ("G6", 1.5), ("F#6", 0.5), ("G6", 0.5), ("A6", 0.5), ("B6", 3)],
                              [("G3", 1), ("D3", 1), ("E3", 1), ("C3", 1), ("D3", 1), ("G3", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(70, [("A4", 1), ("F4", 1), ("D4", 1), ("A3", 1), ("D4", 3)], [("D3", 2), ("A#2", 2), ("D2", 3)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
