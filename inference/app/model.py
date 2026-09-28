from pathlib import Path
from typing import Protocol
import json
import math
import shutil
import tempfile
import zipfile
import hashlib
import numpy as np
from PIL import Image, ImageDraw
from skimage import measure, morphology

MODEL = 'mock-unmix-v1'
MAX_CUBE = 256 * 1024 * 1024


def safe_path(root: Path, key: str) -> Path:
    if not key or Path(key).is_absolute() or '..' in Path(key).parts or '\\' in key:
        raise ValueError('非法文件键')
    path = (root / key).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError('文件键超出存储目录')
    return path


def load_image(path: Path):
    # Read bounded NPY headers from ZIP before allocating/decompressing arrays.
    with zipfile.ZipFile(path) as archive:
        expected = {'cube.npy', 'wavelengths_nm.npy', 'pixel_size_um.npy'}
        if set(archive.namelist()) != expected or len(archive.infolist()) != 3:
            raise ValueError('NPZ 必须且只能包含 cube、wavelengths_nm、pixel_size_um')
        headers = {}
        for entry in archive.infolist():
            if entry.file_size > MAX_CUBE + 8192:
                raise ValueError('解压大小超过 256 MB')
            with archive.open(entry) as stream:
                version = np.lib.format.read_magic(stream)
                if version == (1, 0):
                    shape, _, dtype = np.lib.format.read_array_header_1_0(stream)
                elif version == (2, 0):
                    shape, _, dtype = np.lib.format.read_array_header_2_0(stream)
                else:
                    raise ValueError('不支持的 NPY 版本')
                if dtype.hasobject or math.prod(shape) * dtype.itemsize > MAX_CUBE:
                    raise ValueError('数组类型或大小不受支持')
                if stream.tell() + math.prod(shape)*dtype.itemsize != entry.file_size:
                    raise ValueError('数组长度与头信息不符')
                headers[entry.filename] = (shape, dtype)
        shape, dtype = headers['cube.npy']
        if len(shape) != 3 or not (1 <= shape[0] <= 2048 and 1 <= shape[1] <= 2048 and 3 <= shape[2] <= 64) or dtype != np.dtype('float32'):
            raise ValueError('cube 必须为 float32[H,W,B]，H/W≤2048，3≤B≤64')
        if headers['wavelengths_nm.npy'] != ((shape[2],), np.dtype('float32')):
            raise ValueError('波长必须为 float32[B]')
        if headers['pixel_size_um.npy'][0] != () or headers['pixel_size_um.npy'][1].kind not in 'fi':
            raise ValueError('像素尺寸必须是数值标量')
    with np.load(path, allow_pickle=False) as data:
        cube, waves, pixel = data['cube'], data['wavelengths_nm'], float(data['pixel_size_um'])
    if not np.isfinite(cube).all() or not np.isfinite(waves).all() or not (np.diff(waves) > 0).all():
        raise ValueError('强度必须有限，波长必须有限且严格递增')
    if not math.isfinite(pixel) or pixel <= 0:
        raise ValueError('像素尺寸必须为正数')
    return cube, waves, pixel


def unmix(cube):
    # Fixed equal weights per spectral group; for 16 bands: [0:6], [6:11], [11:16].
    indices = np.minimum(np.arange(cube.shape[2]) * 3 // cube.shape[2], 2)
    return np.stack([np.clip(cube[:, :, indices == i].mean(axis=2), 0, 1) for i in range(3)], axis=2)


def rgb(maps):
    return (np.clip(np.stack([maps[:, :, 1], maps[:, :, 2], maps[:, :, 0]], axis=2), 0, 1)*255).astype('uint8')


class SpectralAdapter(Protocol):
    def inspect(self, image_key: str) -> dict: ...
    def analyze(self, image_key: str, roi: dict, run_id: str, model_version: str) -> dict: ...


class MockSpectralAdapter:
    def __init__(self, root: Path):
        self.root = root

    def inspect(self, image_key):
        path = safe_path(self.root, image_key)
        cube, waves, pixel = load_image(path)
        preview = path.parent / 'preview.png'
        Image.fromarray(rgb(unmix(cube))).save(preview)
        return dict(width=cube.shape[1], height=cube.shape[0], bandCount=cube.shape[2], wavelengths=waves.tolist(), pixelSizeUm=pixel, previewKey=str(preview.relative_to(self.root)))

    def analyze(self, image_key, roi, run_id, model_version):
        if model_version != MODEL:
            raise ValueError('不支持的模型版本')
        source = safe_path(self.root, image_key)
        cube, _, _ = load_image(source)
        x, y, w, h = (roi[k] for k in ('x', 'y', 'width', 'height'))
        if min(x, y) < 0 or min(w, h) <= 0 or x+w > cube.shape[1] or y+h > cube.shape[0]:
            raise ValueError('ROI 越界或为空')
        out = safe_path(self.root, f'runs/{run_id}')
        fingerprint = hashlib.sha256(source.read_bytes()).hexdigest()
        identity = dict(imageSha256=fingerprint, roi=roi, modelVersion=model_version)
        if (out/'manifest.json').exists():
            cached = json.loads((out/'manifest.json').read_text())
            if cached['identity'] != identity:
                raise ValueError('runId 已被不同输入使用')
            return cached
        maps = unmix(cube[y:y+h, x:x+w])
        labels = measure.label(maps[:, :, 0] >= .32)
        mask = np.zeros((h, w), dtype=np.int32)
        cells = []
        for region in measure.regionprops(labels):
            if not 5 <= region.area <= 400:
                continue
            index = len(cells)+1
            nucleus = labels == region.label
            mask[nucleus] = index
            zone = morphology.binary_dilation(nucleus, morphology.disk(3)) & ((labels == 0) | nucleus)
            cy, cx = region.centroid
            contour = max(measure.find_contours(np.pad(nucleus, 1), .5), key=len)
            coords = [[round(float(px-1+x), 3), round(float(py-1+y), 3)] for py, px in contour]
            edge = region.bbox[0] == 0 or region.bbox[1] == 0 or region.bbox[2] == h or region.bbox[3] == w
            cells.append(dict(localIndex=index, x=float(cx+x), y=float(cy+y), areaPx=int(region.area), dapiValue=float(maps[:, :, 0][nucleus].mean()), panckValue=float(maps[:, :, 1][zone].mean()), cd8Value=float(maps[:, :, 2][zone].mean()), qualityFlag='crop-edge' if edge else 'ok', contour=coords))
        out.parent.mkdir(parents=True, exist_ok=True)
        temp = Path(tempfile.mkdtemp(prefix=f'.{run_id}-', dir=out.parent))
        try:
            (temp/'markers').mkdir()
            markers = []
            for i, name in enumerate(['DAPI', 'panCK', 'CD8']):
                Image.fromarray((maps[:, :, i]*255).astype('uint8')).save(temp/f'markers/{name}.png')
                markers.append(dict(name=name, displayKey=f'runs/{run_id}/markers/{name}.png'))
            np.save(temp/'nuclei-mask.npy', mask)
            overlay = Image.new('RGBA', (w, h))
            draw = ImageDraw.Draw(overlay)
            for cell in cells:
                draw.line([(p[0]-x, p[1]-y) for p in cell['contour']], fill=(255, 225, 140, 230), width=1)
            overlay.save(temp/'nuclei-overlay.png')
            (temp/'cells.json').write_text(json.dumps(cells, allow_nan=False))
            result = dict(runId=run_id, modelVersion=MODEL, roi=roi, width=w, height=h, markers=markers, maskKey=f'runs/{run_id}/nuclei-mask.npy', overlayKey=f'runs/{run_id}/nuclei-overlay.png', cellsKey=f'runs/{run_id}/cells.json', cellCount=len(cells), identity=identity)
            (temp/'manifest.json').write_text(json.dumps(result))
            temp.rename(out)
            return result
        finally:
            if temp.exists():
                shutil.rmtree(temp)
