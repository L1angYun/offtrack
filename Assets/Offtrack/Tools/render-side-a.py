"""Render the side-A music track from the composer's four FL Studio loops.

Usage: python render-side-a.py 1.wav 2.wav 3.wav 4.wav [output.wav]

Arrangement (approved 2026-11-13): 1 -> 2 -> 3 -> 4, one loop each. Every source carries an
FL Studio `smpl` loop of exactly 360000 samples (4 bars at 128 BPM, 48 kHz = 7.5 s). Each loop
starts at the previous loop's end; tails beyond the loop (reverb, delay) ring into the next loop,
as in the DAW, and the final loop's tail is kept in full as the record's run-out after 30 s.
The game's chart and B-table parameters (128 BPM, 16 beats per revolution, 30 s) assume this.
"""
import sys
import wave

import numpy as np

LOOP = 360000
RATE = 48000


def read(path):
    with wave.open(path, 'rb') as w:
        assert w.getframerate() == RATE, path + ': expected 48 kHz'
        assert w.getnchannels() == 2, path + ': expected stereo'
        width = w.getsampwidth()
        raw = w.readframes(w.getnframes())
    if width == 3:
        b = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3)
        v = (b[:, 0].astype(np.int32) | (b[:, 1].astype(np.int32) << 8) | (b[:, 2].astype(np.int32) << 16))
        v = np.where(v & 0x800000, v - 0x1000000, v)
        x = v.astype(np.float64) / 2**23
    elif width == 2:
        x = np.frombuffer(raw, dtype='<i2').astype(np.float64) / 2**15
    else:
        raise SystemExit(path + ': unsupported sample width %d' % width)
    return x.reshape(-1, 2)


def main():
    if len(sys.argv) < 5:
        raise SystemExit(__doc__)
    clips = [read(p) for p in sys.argv[1:5]]
    out_path = sys.argv[5] if len(sys.argv) > 5 else 'side-a.wav'
    total = LOOP * (len(clips) - 1) + len(clips[-1])
    out = np.zeros((total, 2))
    for i, clip in enumerate(clips):
        start = i * LOOP
        end = min(total, start + len(clip))
        out[start:end] += clip[:end - start]
    peak = np.abs(out).max()
    if peak > 0.98:
        out *= 0.98 / peak
    pcm = np.clip(np.round(out * 32767), -32768, 32767).astype('<i2')
    with wave.open(out_path, 'wb') as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())
    print('%s: %.3f s, peak %.2f dBFS before limiting' % (out_path, total / RATE, 20 * np.log10(peak)))


if __name__ == '__main__':
    main()
