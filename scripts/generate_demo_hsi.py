"""Reproducible synthetic fixture; no patient data or medical model."""
from pathlib import Path
import argparse
import numpy as np


def generate(path: Path):
    rng = np.random.default_rng(20260928)
    y, x = np.mgrid[:256, :256]
    maps = np.zeros((256, 256, 3), dtype=np.float32)
    for cy in range(16, 250, 21):
        for cx in range(16, 250, 21):
            px, py = cx + rng.uniform(-4, 4), cy + rng.uniform(-4, 4)
            radius = rng.uniform(2.3, 3.6)
            r2 = (x-px)**2 + (y-py)**2
            maps[:, :, 0] += np.exp(-r2/(2*radius**2)) * rng.uniform(.75, 1)
            maps[:, :, 1] += np.exp(-r2/(2*(radius+2)**2)) * (rng.uniform(.65, 1) if cx < 160 else .08)
            maps[:, :, 2] += np.exp(-r2/(2*(radius+2)**2)) * (rng.uniform(.65, 1) if rng.random() < .4 else .06)
    cube = np.empty((256, 256, 16), dtype=np.float32)
    for b in range(16):
        cube[:, :, b] = np.clip(maps[:, :, min(b*3//16, 2)] + rng.uniform(0, .015, (256, 256)), 0, 1)
    path.parent.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(path, cube=cube, wavelengths_nm=np.linspace(420, 720, 16, dtype=np.float32), pixel_size_um=np.float32(.5))

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=Path('data/sample-hsi.npz'))
    generate(parser.parse_args().output)
