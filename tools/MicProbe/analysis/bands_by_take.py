"""Per-take analysis of a MicProbe run: per-channel raw levels, band levels, and how much the
processed takes of one position differ from each other."""
import sys, glob, os
import numpy as np

d = sys.argv[1]
bands = [(150, 400), (400, 1000), (1000, 2500), (2500, 7000)]


def load(path):
    b = open(path, 'rb').read()
    ch = int.from_bytes(b[22:24], 'little')
    data = np.frombuffer(b[44:], dtype='<f4')
    return data.reshape(-1, ch).T


def db(x):
    return 20 * np.log10(np.sqrt(np.mean(np.square(x, dtype=np.float64))) + 1e-15)


def band_db(x, sr=48000):
    spec = np.abs(np.fft.rfft(x * np.hanning(len(x)))) ** 2
    f = np.fft.rfftfreq(len(x), 1 / sr)
    return [10 * np.log10(spec[(f >= lo) & (f < hi)].sum() + 1e-20) for lo, hi in bands]


positions = ['front', 'left', 'right', 'back']
settings = ['off', 'cardioid', 'stereo', 'omni']
print('Raw per-channel level (dBFS) and ch0/ch1 correlation; ch2/ch3 identical?')
for p in positions:
    for s in settings:
        f = os.path.join(d, f'{p}-{s}-raw.wav')
        if not os.path.exists(f):
            continue
        r = load(f)
        lv = ' '.join(f'{db(c):6.1f}' for c in r)
        same = np.allclose(r[2], r[3]) if len(r) >= 4 else None
        print(f'  {p:6} {s:9} {lv}   ch2==ch3 {same}')

print('\nProcessed band levels (dB, relative) per take:  ' + '  '.join(f'{lo}-{hi}' for lo, hi in bands))
proc = {}
for p in positions:
    for s in settings:
        f = os.path.join(d, f'{p}-{s}.wav')
        if not os.path.exists(f):
            continue
        x = load(f)[0]
        proc[(p, s)] = x
        print(f'  {p:6} {s:9} total {db(x):6.1f}  bands ' + ' '.join(f'{v:7.1f}' for v in band_db(x)))

print('\nRaw ch0 band levels per take:')
for p in positions:
    for s in settings:
        f = os.path.join(d, f'{p}-{s}-raw.wav')
        if not os.path.exists(f):
            continue
        x = load(f)[0]
        print(f'  {p:6} {s:9} total {db(x):6.1f}  bands ' + ' '.join(f'{v:7.1f}' for v in band_db(x)))

# Are processed takes of one position the "same signal"? Compare the 1-second-folded envelopes
# (the test signal repeats every second), aligned by circular cross-correlation.
print('\nProcessed: folded 1 s envelope similarity to the "off" take of the same position (1.0 = same shape)')
sr = 48000
for p in positions:
    if (p, 'off') not in proc:
        continue

    def fold(x):
        n = (len(x) // sr) * sr
        env = np.sqrt(np.mean(np.square(x[:n].reshape(-1, 480)), axis=1))  # 10 ms
        return env.reshape(-1, 100).mean(axis=0)

    ref = fold(proc[(p, 'off')])
    line = []
    for s in settings:
        if (p, s) not in proc:
            continue
        e = fold(proc[(p, s)])
        best = max(np.corrcoef(np.roll(e, k), ref)[0, 1] for k in range(100))
        line.append(f'{s} {best:.2f}')
    print(f'  {p:6} ' + '   '.join(line))
