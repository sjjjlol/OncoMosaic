"""Generate a reproducible *synthetic* spectral mIF field and separate reference truth."""
from pathlib import Path
import argparse
import hashlib
import json
import numpy as np
import tifffile

MARKERS = ('DAPI', 'panCK', 'CD3', 'CD8', 'autofluorescence')


def spectra(waves, ki67=False):
    centers = (460, 540, 600, 670, 510)
    widths = (25, 30, 28, 26, 90)
    if ki67:
        centers = (460, 540, 600, 670, 710, 510)
        widths = (25, 30, 28, 26, 14, 90)
    matrix = np.stack([np.exp(-.5 * ((waves - c) / s) ** 2) for c, s in zip(centers, widths)])
    return (matrix / matrix.max(axis=1, keepdims=True)).astype(np.float32)


def generate(path: Path, ki67=False):
    markers = (*MARKERS[:-1], "Ki67", MARKERS[-1]) if ki67 else MARKERS
    ki_rng = np.random.default_rng(20261002)
    rng = np.random.default_rng(20260929)
    height = width = 256
    yy, xx = np.mgrid[:height, :width]
    waves = np.linspace(420, 720, 24, dtype=np.float32)
    reference = spectra(waves, ki67)
    if ki67:
        # Lower synthetic detector gain for the larger panel; coefficients retain their scale.
        reference *= .65
    tissue = ((xx - 128) / 113) ** 2 + ((yy - 128) / 104) ** 2 < 1
    necrosis = ((xx - 172) / 20) ** 2 + ((yy - 142) / 15) ** 2 < 1
    artifact = (xx > 89) & (xx < 99) & (yy > 64) & (yy < 103)
    valid = tissue & ~necrosis & ~artifact
    maps = np.zeros((height, width, len(markers)), dtype=np.float32)
    maps[:, :, -1] = np.where(valid, .18, .005)
    truth_cells = []
    nucleus_labels = np.zeros((height, width), dtype=np.uint16)
    for cy in range(18, 245, 19):
        for cx in range(18, 245, 19):
            if not valid[cy, cx]:
                continue
            px, py = cx + rng.uniform(-2, 2), cy + rng.uniform(-2, 2)
            epithelial = px < 137
            cd8_t = (not epithelial) and rng.random() < .65
            nucleus = np.exp(-((xx-px)**2 + (yy-py)**2) / (2*2.8**2))
            halo = np.exp(-((xx-px)**2 + (yy-py)**2) / (2*4.7**2))
            maps[:, :, 0] += nucleus * .85
            maps[:, :, 1] += halo * (.78 if epithelial else .025)
            maps[:, :, 2] += halo * (.77 if cd8_t else .025)
            maps[:, :, 3] += halo * (.72 if cd8_t else .025)
            positive = bool(ki_rng.random() < (.55 if epithelial else .3))
            if ki67:
                maps[:, :, 4] += nucleus * (.85 if positive else .04)
            nucleus_labels[((xx-px)**2 + (yy-py)**2 < 4.0**2) & valid] = len(truth_cells) + 1
            truth_cells.append({'x': round(float(px), 2), 'y': round(float(py), 2), 'syntheticClass': 'epithelial' if epithelial else 'cd3-cd8' if cd8_t else 'other'})
            if ki67:
                truth_cells[-1].update(ki67Positive=positive, ki67Amplitude=.85 if positive else .04)
    maps[:, :, :-1] *= valid[:, :, None]
    background = np.full(len(waves), .012, dtype=np.float32)
    cube = np.einsum('hwc,cb->hwb', maps, reference) + background
    cube += rng.normal(0, .004, cube.shape).astype(np.float32)
    cube[artifact] = 1.0
    cube = np.clip(cube, 0, 1).astype(np.float32)
    metadata = {
        'schema': 'spectral-mif-research-v1', 'source': 'synthetic-no-patient-data',
        'specimenId': 'SYN-001', 'slideId': 'SYN-001-FOV-01', 'fieldId': 'FOV-01',
        'assayId': 'dapi-panck-cd3-cd8-v1', 'scannerId': 'synthetic-scanner-01',
        'stainBatchId': 'synthetic-batch-01', 'calibrationId': 'synthetic-single-stain-v1',
        'markers': list(MARKERS), 'excitationNm': [405, 488, 561, 640],
        'signalUnit': 'scanner-counts-16bit', 'detectorMaxCount': 65535,
        'referenceProvenance': 'synthetic-single-stain-controls',
        'fovOriginPx': [0, 0], 'note': 'No patient or clinical validation data.'
    }
    if ki67:
        metadata.update(schema='spectral-mif-research-v2', assayId='dapi-panck-cd3-cd8-ki67-v2',
                        calibrationId='synthetic-single-stain-ki67-v2', markers=list(markers),
                        panelVersion='2', ki67Reagent='synthetic-no-antibody', measurementUnit='simulated-relative-intensity')
    path.parent.mkdir(parents=True, exist_ok=True)
    controls_dir = path.parent / ('controls-ki67' if ki67 else 'controls')
    controls_dir.mkdir(exist_ok=True)
    controls = {}
    for index, marker in enumerate(markers):
        control_path = controls_dir / f'{marker}-single-stain.ome.tiff'
        signal = (np.broadcast_to((background + .55 * reference[index])[:, None, None], (24, 32, 32)) * 65535).astype(np.uint16).copy()
        tifffile.imwrite(control_path, signal, ome=True,
                         metadata={'axes': 'CYX', 'PhysicalSizeX': .5, 'PhysicalSizeY': .5,
                                   'PhysicalSizeXUnit': 'µm', 'PhysicalSizeYUnit': 'µm'})
        controls[marker] = {'file': control_path.name, 'sha256': hashlib.sha256(control_path.read_bytes()).hexdigest()}
    unstained_path = controls_dir / 'unstained.ome.tiff'
    tifffile.imwrite(unstained_path, (np.broadcast_to(background[:, None, None], (24, 32, 32)) * 65535).astype(np.uint16).copy(),
                     ome=True, metadata={'axes': 'CYX', 'PhysicalSizeX': .5, 'PhysicalSizeY': .5,
                                         'PhysicalSizeXUnit': 'µm', 'PhysicalSizeYUnit': 'µm'})
    controls['unstained'] = {'file': unstained_path.name, 'sha256': hashlib.sha256(unstained_path.read_bytes()).hexdigest()}
    tifffile.imwrite(path, (np.moveaxis(cube, -1, 0) * 65535).astype(np.uint16), ome=True,
                     metadata={'axes': 'CYX', 'PhysicalSizeX': .5, 'PhysicalSizeY': .5,
                               'PhysicalSizeXUnit': 'µm', 'PhysicalSizeYUnit': 'µm',
                               'Channel': {'Name': [f'{float(w):.1f} nm' for w in waves]}})
    assay = {**metadata, 'wavelengthsNm': waves.tolist(), 'pixelSizeUm': .5,
             'referenceSpectra': reference.tolist(), 'backgroundSpectrum': background.tolist(),
             'syntheticControlFiles': controls,
             'imageSha256': hashlib.sha256(path.read_bytes()).hexdigest()}
    assay_path = path.with_name(path.name.replace('.ome.tiff', '.assay.json'))
    assay_path.write_text(json.dumps(assay, ensure_ascii=False, indent=2))
    truth_image = path.with_name(path.name.replace('.ome.tiff', '.truth.ome.tiff'))
    regions = np.where(valid, np.where(xx < 137, 1, 2), 0).astype(np.uint16)
    tifffile.imwrite(truth_image, np.stack((valid.astype(np.uint16), regions, nucleus_labels)),
                     ome=True, metadata={'axes': 'CYX', 'Channel': {'Name': ['valid-tissue', 'synthetic-region', 'nucleus-instance']}})
    truth_path = path.with_name(path.name.replace('.ome.tiff', '.truth.json'))
    truth_path.write_text(json.dumps({'truthImage': truth_image.name,
                                      'regionCodes': {'0': 'excluded', '1': 'synthetic-epithelial-side', '2': 'synthetic-stroma-side'},
                                      'cells': truth_cells}, indent=2))
    return path, assay_path, truth_image, truth_path

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=Path('data/sample-spectral-mif.ome.tiff'))
    parser.add_argument('--ki67', action='store_true', help='Generate the five-marker v2 panel')
    args = parser.parse_args()
    generate(args.output, args.ki67)
