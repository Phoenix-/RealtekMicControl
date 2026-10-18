"""Second-pass analysis for a --configs MicProbe run: is the processed stream really mono, and do
the modes change its spectrum (relative to the raw front pair of the same take)?"""
import sys, os
import numpy as np

d = sys.argv[1]
bands = [(150, 400), (400, 1000), (1000, 2500), (2500, 7000)]


def load(path):
    b = open(path, 'rb').read()
    ch = int.from_bytes(b[22:24], 'little')
    return np.frombuffer(b[44:], dtype='<f4').reshape(-1, ch).T


def band_db(x, sr=48000):
    spec = np.abs(np.fft.rfft(x * np.hanning(len(x)))) ** 2
    f = np.fft.rfftfreq(len(x), 1 / sr)
    return np.array([10 * np.log10(spec[(f >= lo) & (f < hi)].sum() + 1e-20) for lo, hi in bands])


print('position config        setting    L==R   L/R corr   processed-minus-raw per band (dB): ' + '  '.join(f'{lo}-{hi}' for lo, hi in bands))
for p in ['back', 'front']:
    for c in ['default', 'comms', 'comms-reopen']:
        for s in ['off', 'cardioid', 'stereo', 'omni']:
            f = os.path.join(d, f'{p}-{c}-{s}.wav')
            if not os.path.exists(f):
                continue
            x = load(f)
            r = load(os.path.join(d, f'{p}-{c}-{s}-raw.wav'))
            if len(x) >= 2:
                same = str(bool(np.array_equal(x[0], x[1])))
                corr = np.corrcoef(x[0], x[1])[0, 1] if x[0].std() > 0 and x[1].std() > 0 else float('nan')
            else:
                same, corr = '1ch', float('nan')
            diff = band_db(x[0]) - (band_db(r[0]) + band_db(r[1])) / 2
            print(f'{p:8} {c:14} {s:9} {str(same):6} {corr:8.3f}   ' + ' '.join(f'{v:9.1f}' for v in diff))
