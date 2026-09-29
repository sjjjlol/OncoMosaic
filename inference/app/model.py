"""Deterministic spectral mIF simulation adapter; never a clinical model."""
from pathlib import Path
from typing import Protocol
import hashlib
import json
import math
import shutil
import tempfile
import tifffile
from xml.etree import ElementTree
from threading import Lock
import numpy as np
from PIL import Image, ImageDraw
from skimage import measure, morphology

MODEL = 'spectral-mif-sim-v1'
MARKERS = ('DAPI', 'panCK', 'CD3', 'CD8', 'autofluorescence')
MAX_CUBE = 256 * 1024 * 1024


def safe_path(root: Path, key: str) -> Path:
    if not key or Path(key).is_absolute() or '..' in Path(key).parts or '\\' in key:
        raise ValueError('非法文件键')
    path = (root / key).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError('文件键超出存储目录')
    return path


def load_image(path: Path, assay_path: Path):
    if not path.name.endswith('.ome.tiff') or not assay_path.name.endswith('.assay.json'):
        raise ValueError('需要 OME-TIFF 图像和 assay.json 伴随文件')
    if assay_path.stat().st_size > 100_000:
        raise ValueError('assay.json 过大')
    metadata = json.loads(assay_path.read_text())
    required = ('schema', 'source', 'specimenId', 'slideId', 'fieldId', 'assayId', 'scannerId',
                'stainBatchId', 'calibrationId', 'markers', 'referenceProvenance', 'signalUnit',
                'fovOriginPx', 'wavelengthsNm', 'pixelSizeUm', 'referenceSpectra',
                'backgroundSpectrum', 'imageSha256')
    if not isinstance(metadata, dict) or any(k not in metadata for k in required):
        raise ValueError('染色/采集/对照元数据缺失')
    if metadata['schema'] != 'spectral-mif-research-v1' or metadata['source'] not in ('synthetic-no-patient-data', 'deidentified-research') or metadata['markers'] != list(MARKERS) or metadata['fovOriginPx'] != [0, 0]:
        raise ValueError('染色/采集元数据不兼容')
    if metadata['imageSha256'] != hashlib.sha256(path.read_bytes()).hexdigest():
        raise ValueError('图像与伴随元数据校验值不匹配')
    with tifffile.TiffFile(path) as tif:
        if not tif.is_ome or not tif.ome_metadata or len(tif.series) != 1:
            raise ValueError('需要单视野 OME-TIFF')
        series = tif.series[0]
        if series.axes != 'CYX' or len(series.shape) != 3:
            raise ValueError('OME-TIFF 必须为 CYX 波段、高度、宽度')
        bands, height, width = series.shape
        if not (8 <= bands <= 64 and 1 <= height <= 2048 and 1 <= width <= 2048) or series.dtype not in (np.dtype('float32'), np.dtype('uint16')) or bands * height * width * series.dtype.itemsize > MAX_CUBE:
            raise ValueError('OME-TIFF 只支持 uint16/float32[8..64,H,W] 且解码不超过 256 MB')
        root = ElementTree.fromstring(tif.ome_metadata)
        pixels = next((node for node in root.iter() if node.tag.endswith('Pixels')), None)
        if pixels is None or not all(k in pixels.attrib for k in ('PhysicalSizeX', 'PhysicalSizeY')):
            raise ValueError('OME-TIFF 缺少像素物理尺寸')
        physical_x = float(pixels.attrib['PhysicalSizeX']); physical_y = float(pixels.attrib['PhysicalSizeY'])
        raw = series.asarray()
        if series.dtype == np.dtype('uint16'):
            if metadata['signalUnit'] != 'scanner-counts-16bit' or metadata.get('detectorMaxCount') != 65535:
                raise ValueError('uint16 图像须提供 16 位探测器强度尺度')
            cube = np.moveaxis(raw, 0, -1).astype(np.float32) / 65535
        else:
            if metadata['signalUnit'] not in ('calibrated-relative-intensity', 'simulated-relative-intensity') or (metadata['signalUnit'] == 'simulated-relative-intensity' and metadata['source'] != 'synthetic-no-patient-data'):
                raise ValueError('float32 图像须明确校准后的相对强度单位')
            cube = np.moveaxis(raw, 0, -1)
    waves = np.asarray(metadata['wavelengthsNm'], dtype=np.float32)
    reference = np.asarray(metadata['referenceSpectra'], dtype=np.float32)
    background = np.asarray(metadata['backgroundSpectrum'], dtype=np.float32)
    pixel = float(metadata['pixelSizeUm'])
    if waves.shape != (bands,) or reference.shape != (5, bands) or background.shape != (bands,):
        raise ValueError('波长或参考光谱维度与图像不一致')
    if not np.isfinite(cube).all() or not np.isfinite(waves).all() or not np.isfinite(reference).all() or not np.isfinite(background).all():
        raise ValueError('图像或参考光谱含非有限值')
    if np.min(cube) < 0 or np.max(cube) > 1 or np.min(background) < 0 or np.max(background) > 1:
        raise ValueError('校准后的强度必须位于 0–1')
    if not (np.diff(waves) > 0).all() or not math.isfinite(pixel) or pixel <= 0 or abs(pixel-physical_x) > 1e-5 or abs(pixel-physical_y) > 1e-5:
        raise ValueError('波长或像素物理尺寸不一致')
    if np.any(reference < 0) or np.any(background < 0) or np.linalg.matrix_rank(reference) < 5 or np.linalg.cond(reference) > 1e4:
        raise ValueError('参考光谱无效或无法稳定区分五个信号')
    return cube, waves, pixel, reference, background, metadata


def unmix(cube, reference, background):
    """Fit the supplied synthetic single-stain/reference spectra, not band indices."""
    corrected = np.maximum(cube - background, 0)
    coefficients = np.einsum('hwb,bc->hwc', corrected, np.linalg.pinv(reference.astype(np.float64)).astype(np.float32))
    maps = np.clip(coefficients, 0, 1).astype(np.float32)
    residual = np.sqrt(np.mean((corrected - np.einsum('hwc,cb->hwb', maps, reference)) ** 2, axis=2))
    return maps, residual


def tissue_masks(maps, cube):
    # Synthetic fixture: tissue has broad autofluorescence; saturation is an artifact.
    saturated = (cube >= .995).mean(axis=2) > .7
    tissue = (maps[:, :, 4] >= .065) & ~saturated
    tissue = morphology.remove_small_objects(tissue, min_size=24)
    return tissue.astype(bool), saturated


def rgb(maps):
    return (np.clip(np.stack([maps[:, :, 1] + maps[:, :, 3] * .2,
                              maps[:, :, 2] + maps[:, :, 3] * .6,
                              maps[:, :, 0] + maps[:, :, 3] * .2], axis=2), 0, 1) * 255).astype('uint8')


class SpectralAdapter(Protocol):
    def inspect(self, image_key: str, assay_key: str) -> dict: ...
    def analyze(self, image_key: str, assay_key: str, roi: dict, run_id: str, model_version: str) -> dict: ...


class SpectralMifSimulationAdapter:
    def __init__(self, root: Path):
        self.root = root.resolve()
        self._analysis_lock = Lock()

    def inspect(self, image_key, assay_key):
        path = safe_path(self.root, image_key)
        cube, waves, pixel, reference, background, metadata = load_image(path, safe_path(self.root, assay_key))
        maps, _ = unmix(cube, reference, background)
        preview = path.parent / 'preview.png'
        Image.fromarray(rgb(maps)).save(preview)
        return dict(width=cube.shape[1], height=cube.shape[0], bandCount=cube.shape[2],
                    wavelengths=waves.tolist(), pixelSizeUm=pixel,
                    previewKey=str(preview.relative_to(self.root)), acquisition=metadata)

    def analyze(self, image_key, assay_key, roi, run_id, model_version):
        with self._analysis_lock:
            return self._analyze(image_key, assay_key, roi, run_id, model_version)

    def _analyze(self, image_key, assay_key, roi, run_id, model_version):
        if model_version != MODEL:
            raise ValueError('不支持的模型版本')
        source = safe_path(self.root, image_key)
        assay_source = safe_path(self.root, assay_key)
        cube, _, _, reference, background, metadata = load_image(source, assay_source)
        x, y, w, h = (roi[k] for k in ('x', 'y', 'width', 'height'))
        if min(x, y) < 0 or min(w, h) <= 0 or x+w > cube.shape[1] or y+h > cube.shape[0]:
            raise ValueError('ROI 越界或为空')
        out = safe_path(self.root, f'runs/{run_id}')
        identity = dict(imageSha256=hashlib.sha256(source.read_bytes()).hexdigest(), roi=roi,
                        modelVersion=model_version, assaySha256=hashlib.sha256(assay_source.read_bytes()).hexdigest(), assayId=metadata['assayId'], calibrationId=metadata['calibrationId'])
        if (out/'manifest.json').exists():
            cached = json.loads((out/'manifest.json').read_text())
            if cached['identity'] != identity:
                raise ValueError('runId 已被不同输入使用')
            return cached
        crop = cube[y:y+h, x:x+w]
        maps, residual = unmix(crop, reference, background)
        valid_tissue, saturation = tissue_masks(maps, crop)
        labels = measure.label((maps[:, :, 0] >= .30) & valid_tissue)
        mask = np.zeros((h, w), dtype=np.int32)
        cells = []
        for region in measure.regionprops(labels):
            if not 5 <= region.area <= 400:
                continue
            index = len(cells)+1
            nucleus = labels == region.label
            mask[nucleus] = index
            zone = morphology.binary_dilation(nucleus, morphology.disk(3)) & ((labels == 0) | nucleus) & valid_tissue
            cy, cx = region.centroid
            contour = max(measure.find_contours(np.pad(nucleus, 1), .5), key=len)
            coords = [[round(float(px-1+x), 3), round(float(py-1+y), 3)] for py, px in contour]
            edge = region.bbox[0] == 0 or region.bbox[1] == 0 or region.bbox[2] == h or region.bbox[3] == w
            values = [float(maps[:, :, i][zone].mean()) for i in (1, 2, 3)]
            cells.append(dict(localIndex=index, x=float(cx+x), y=float(cy+y), areaPx=int(region.area),
                              dapiValue=float(maps[:, :, 0][nucleus].mean()), panckValue=values[0],
                              cd3Value=values[1], cd8Value=values[2],
                              qualityFlag='crop-edge' if edge else 'ok', contour=coords))
        qc = dict(validTissuePx=int(valid_tissue.sum()), excludedPx=int(valid_tissue.size-valid_tissue.sum()),
                  saturatedPx=int(saturation.sum()), meanSpectralResidual=float(residual.mean()),
                  signalUnit=metadata['signalUnit'], boundaryMethod='nucleus-plus-3px; approximate, not whole-cell',
                  referenceProvenance=metadata['referenceProvenance'],
                  limitations=['synthetic data', 'deterministic simulation', 'no medical performance validation'])
        if qc['validTissuePx'] == 0:
            raise ValueError('ROI 中没有可分析组织')
        out.parent.mkdir(parents=True, exist_ok=True)
        temp = Path(tempfile.mkdtemp(prefix=f'.{run_id}-', dir=out.parent))
        try:
            (temp/'markers').mkdir()
            markers = []
            for i, name in enumerate(MARKERS[:4]):
                Image.fromarray((maps[:, :, i]*255).astype('uint8')).save(temp/f'markers/{name}.png')
                markers.append(dict(name=name, displayKey=f'runs/{run_id}/markers/{name}.png'))
            np.save(temp/'nuclei-mask.npy', mask)
            Image.fromarray((valid_tissue*255).astype('uint8')).save(temp/'valid-tissue.png')
            overlay = Image.new('RGBA', (w, h))
            draw = ImageDraw.Draw(overlay)
            for cell in cells:
                draw.line([(p[0]-x, p[1]-y) for p in cell['contour']], fill=(255, 225, 140, 230), width=1)
            overlay.save(temp/'nuclei-overlay.png')
            (temp/'cells.json').write_text(json.dumps(cells, allow_nan=False))
            (temp/'qc.json').write_text(json.dumps(qc, allow_nan=False))
            result = dict(runId=run_id, modelVersion=MODEL, roi=roi, width=w, height=h, markers=markers,
                          maskKey=f'runs/{run_id}/nuclei-mask.npy', overlayKey=f'runs/{run_id}/nuclei-overlay.png',
                          tissueKey=f'runs/{run_id}/valid-tissue.png', qcKey=f'runs/{run_id}/qc.json',
                          validTissuePx=qc['validTissuePx'], cellsKey=f'runs/{run_id}/cells.json',
                          cellCount=len(cells), identity=identity)
            (temp/'manifest.json').write_text(json.dumps(result))
            temp.rename(out)
            return result
        finally:
            if temp.exists():
                shutil.rmtree(temp)
