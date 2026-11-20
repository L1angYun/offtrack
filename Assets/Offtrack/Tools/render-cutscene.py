import math
import random
import struct
import wave
from pathlib import Path


RATE = 24000
ROOT = Path(__file__).resolve().parents[1] / "Audio" / "Cutscene"
TAU = math.tau


def render(name, length, texture, loop=False, room_tail=False):
    noise = random.Random(731 + sum(map(ord, name)))
    low = 0
    count = round(RATE * length)
    overlap = round(.18 * RATE) if loop else 0
    signal = []
    samples = bytearray()
    for index in range(count + overlap):
        time = index / RATE
        white = noise.uniform(-1, 1)
        low = low * .97 + white * .03
        value = texture(time, white, low)
        edge = 1 if loop else min(1, time * 180, (length - time - 1 / RATE) * 180)
        signal.append(value * max(0, edge))
    for index in range(overlap):
        blend = .5 - .5 * math.cos(math.pi * index / overlap)
        signal[index] = signal[count + index] * (1 - blend) + signal[index] * blend
    delays = [(round(RATE * delay), gain) for delay, gain in
              ((.031, .30), (.073, -.24), (.113, .21), (.151, .17), (.233, -.14),
               (.31, .10), (.43, .07), (.59, .046), (.74, -.027), (.87, .016))]
    for index in range(count + (delays[-1][0] if room_tail and not loop else 0)):
        value = signal[index] if index < count else 0
        if room_tail:
            for delay, gain in delays:
                previous = (index - delay) % count if loop else index - delay
                if 0 <= previous < count:
                    value += signal[previous] * gain
        samples.extend(struct.pack("<h", round(30000 * max(-1, min(1, value)))))
    with wave.open(str(ROOT / (name + ".wav")), "wb") as target:
        target.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        target.writeframes(samples)


def tone(time, pitch):
    return math.sin(time * TAU * pitch)


def piano(time, white, low):
    notes = [(0, 293.66), (.62, 261.63), (1.3, 220), (2.05, 196), (2.78, 174.61)]
    for start, pitch in reversed(notes):
        age = time - start
        if age >= 0:
            return min(1, age * 130) * math.exp(-3 * age) * (.47 * tone(age, pitch) + .12 * tone(age, 2.01 * pitch))
    return 0


def main():
    ROOT.mkdir(parents=True, exist_ok=True)
    render("RoomA", 4, lambda t, w, l: .16 * l + .025 * tone(t, 50), loop=True)
    render("ClassAir", 3, lambda t, w, l: (.36 + .16 * tone(t, 1)) * l, loop=True)
    render("Chalk", 2, lambda t, w, l: (.08 + .08 * tone(t, 7)) * w * tone(t, 780), loop=True)
    render("Phone", .27, lambda t, w, l: .38 * tone(t, 650 if t < .1 else 980) * math.exp(-6 * t))
    render("RoomC", 4, lambda t, w, l: .52 * l + .05 * tone(t, 50), loop=True)
    render("Tick", .11, lambda t, w, l: (.43 * w + .29 * tone(t, 1200)) * math.exp(-57 * t))
    render("ClockNoise", 2, lambda t, w, l: .52 * math.tanh(16 * l * tone(t, 112)), loop=True)
    render("Whoosh", .26, lambda t, w, l: .49 * l * math.sin(math.pi * t / .26) ** 2)
    render("Stop", .30, lambda t, w, l: (.77 * tone(t, 68 - 120 * t) + .23 * w) * (1 - t / .30) ** 3)
    render("RoomB", 4, lambda t, w, l: .20 * l + .014 * tone(t, 40), loop=True, room_tail=True)
    render("Step", .51, lambda t, w, l: .37 * l * math.exp(-18 * t), room_tail=True)
    render("Paper", .66, lambda t, w, l: (.20 * w + .16 * l) * (1 - t / .66) ** 2 * (.7 + .3 * tone(t, 19)), room_tail=True)
    render("Piano", 4.2, piano)
    render("PhoneB", .22, lambda t, w, l: .17 * tone(t, 611) * math.exp(-9 * t))
    render("Bell", .74, lambda t, w, l: (.21 * tone(t, 771) + .08 * tone(t, 1282)) * math.exp(-4 * t))
    render("PaperA", .54, lambda t, w, l: .29 * w * (1 - t / .54) ** 2 * (.55 + .45 * tone(t, 21)))
    render("Chair", .52, lambda t, w, l: (.43 * l + .16 * tone(t, 80 + 28 * t)) * math.sin(math.pi * t / .52))
    render("Pack", .59, lambda t, w, l: (.41 * l + .10 * w) * math.sin(math.pi * t / .59) ** 2)
    render("Zip", .32, lambda t, w, l: (.15 + .1 * tone(t, 51)) * w * math.sin(math.pi * t / .32))
    render("Talk", 1.31, lambda t, w, l: l * (.32 + .25 * tone(t, 5.3)) * (.4 + .34 * tone(t, 123) + .18 * tone(t, 262)))


if __name__ == "__main__":
    main()
