"""Generate the bundled beep tones as 16-bit PCM mono WAV files.

Run:  python tools/gen_sounds.py
Writes the same set into every app's Assets/Sounds folder (see OUT_DIRS below).
"""
import math
import os
import struct
import wave

SAMPLE_RATE = 44100

OUT_DIRS = [
    os.path.join("IntervalTimerOverlay", "Assets", "Sounds"),
    os.path.join("IntervalTimerWidget", "Assets", "Sounds"),
]


def _envelope(i, n, attack=0.01, release=0.05):
    """Simple attack/release ramp (in seconds) to avoid clicks."""
    t = i / SAMPLE_RATE
    total = n / SAMPLE_RATE
    a = min(attack, total / 2)
    r = min(release, total / 2)
    if t < a:
        return t / a
    if t > total - r:
        return max(0.0, (total - t) / r)
    return 1.0


def tone(freq, seconds, volume=0.6):
    n = int(SAMPLE_RATE * seconds)
    out = []
    for i in range(n):
        s = math.sin(2 * math.pi * freq * i / SAMPLE_RATE)
        out.append(s * volume * _envelope(i, n))
    return out


def silence(seconds):
    return [0.0] * int(SAMPLE_RATE * seconds)


def sweep(f0, f1, seconds, volume=0.6):
    n = int(SAMPLE_RATE * seconds)
    out = []
    phase = 0.0
    for i in range(n):
        frac = i / n
        f = f0 + (f1 - f0) * frac
        phase += 2 * math.pi * f / SAMPLE_RATE
        out.append(math.sin(phase) * volume * _envelope(i, n))
    return out


def partials(fundamental, seconds, weights, volume=0.5):
    n = int(SAMPLE_RATE * seconds)
    out = []
    for i in range(n):
        s = 0.0
        for k, w in enumerate(weights, start=1):
            s += w * math.sin(2 * math.pi * fundamental * k * i / SAMPLE_RATE)
        # exponential decay for a struck/rung feel
        decay = math.exp(-3.0 * i / n)
        out.append(s * volume * decay * _envelope(i, n, attack=0.002, release=0.02))
    return out


SOUNDS = {
    "beep.wav": lambda: tone(880, 0.16),
    "doublebeep.wav": lambda: tone(988, 0.09) + silence(0.06) + tone(988, 0.09),
    "chime.wav": lambda: partials(660, 0.9, [1.0, 0.5, 0.25, 0.12]),
    "bell.wav": lambda: partials(440, 1.4, [1.0, 0.6, 0.4, 0.25, 0.2, 0.15]),
    "alarm.wav": lambda: (sweep(700, 1400, 0.18) + silence(0.05)) * 3,
}


def write_wav(path, samples):
    with wave.open(path, "w") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SAMPLE_RATE)
        frames = bytearray()
        for s in samples:
            v = max(-1.0, min(1.0, s))
            frames += struct.pack("<h", int(v * 32767))
        w.writeframes(bytes(frames))


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    for rel in OUT_DIRS:
        out_dir = os.path.join(root, rel)
        os.makedirs(out_dir, exist_ok=True)
        for name, make in SOUNDS.items():
            path = os.path.join(out_dir, name)
            write_wav(path, make())
            print("wrote", path)


if __name__ == "__main__":
    main()
