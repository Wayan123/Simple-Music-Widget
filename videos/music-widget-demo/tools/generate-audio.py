"""Original instrumental demo, seed 19042026. No samples or external services."""
from pathlib import Path
import argparse
import wave
import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument("output", type=Path)
args = parser.parse_args()
sr, seconds, bpm = 48000, 36, 112
beat = 60 / bpm
rng = np.random.default_rng(19042026)
left = np.zeros(sr * seconds, dtype=np.float64)
right = np.zeros_like(left)

def add(start, duration, frequency, gain, pan=0.0, bass=False):
    offset = int(start * sr)
    size = min(int(duration * sr), len(left) - offset)
    if size <= 0:
        return
    t = np.arange(size) / sr
    envelope = (1 - np.exp(-t * 170)) * np.exp(-t * (3.1 if bass else 4.2))
    voice = np.sin(2 * np.pi * frequency * t)
    voice += 0.22 * np.sin(2 * np.pi * frequency * 2 * t)
    voice += 0.055 * np.sin(2 * np.pi * frequency * 3 * t)
    voice *= envelope * gain
    left[offset:offset + size] += voice * np.sqrt((1 - pan) / 2)
    right[offset:offset + size] += voice * np.sqrt((1 + pan) / 2)

chords = [(57, 60, 64), (53, 57, 60), (60, 64, 67), (55, 59, 62)]
roots = [45, 41, 48, 43]
for bar in range(17):
    base = bar * beat * 4
    chord = chords[bar % 4]
    for note in chord:
        add(base, beat * 3.9, 440 * 2 ** ((note - 69) / 12), 0.10, pan=(note % 3 - 1) * 0.28)
    for tick in (0, 1.5, 2, 3.5):
        add(base + tick * beat, beat * 0.9, 440 * 2 ** ((roots[bar % 4] - 69) / 12), 0.32, bass=True)
    pattern = [0, 2, 1, 2, 0, 1, 2, 1]
    for tick, degree in enumerate(pattern):
        if (bar + tick) % 7 != 0:
            add(base + tick * beat / 2, beat * 0.8, 440 * 2 ** ((chord[degree] + 12 - 69) / 12), 0.10, pan=(-0.35 if tick % 2 else 0.35))
    for tick in range(4):
        offset = int((base + tick * beat) * sr)
        n = min(int(0.24 * sr), len(left) - offset)
        if n <= 0:
            continue
        t = np.arange(n) / sr
        kick = np.sin(2 * np.pi * (48 * t + 0.9 * (1 - np.exp(-t * 40)))) * np.exp(-t * 22) * 0.22
        left[offset:offset+n] += kick
        right[offset:offset+n] += kick
    for tick in range(8):
        offset = int((base + tick * beat / 2) * sr)
        n = min(int(0.07 * sr), len(left) - offset)
        if n <= 0:
            continue
        t = np.arange(n) / sr
        noise = rng.normal(0, 1, n)
        hat = np.diff(noise, prepend=noise[0]) * np.exp(-t * 95) * (0.022 if tick % 2 else 0.035)
        left[offset:offset+n] += hat
        right[offset:offset+n] += hat * 0.85
stereo = np.stack([left, right], axis=1)
stereo /= max(np.max(np.abs(stereo)) / 0.73, 1.0)
fade = np.minimum(np.arange(len(left)) / (sr * 0.03), 1)
fade *= np.minimum((len(left) - np.arange(len(left))) / (sr * 1.2), 1)
stereo *= fade[:, None]
args.output.parent.mkdir(parents=True, exist_ok=True)
with wave.open(str(args.output), "wb") as out:
    out.setnchannels(2)
    out.setsampwidth(2)
    out.setframerate(sr)
    out.writeframes((stereo * 32767).astype("<i2").tobytes())
print(f"Original instrumental: {args.output.name}, {seconds}s, {sr} Hz stereo, seed 19042026")
