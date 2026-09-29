import json
import sys
from pathlib import Path
from uuid import uuid4
import numpy as np
import pytest
import tifffile
from PIL import Image
from fastapi.testclient import TestClient
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from app.model import SpectralMifSimulationAdapter, load_image, safe_path, unmix, MODEL
from app import main

@pytest.fixture
def store(tmp_path):
    base = Path(__file__).resolve().parents[2] / 'data'
    for suffix in ('.ome.tiff', '.assay.json'):
        (tmp_path / ('source' + suffix)).write_bytes((base / ('sample-spectral-mif' + suffix)).read_bytes())
    return tmp_path


def test_ome_and_companion_metadata_are_required(store):
    cube, waves, pixel, spectra, background, meta = load_image(store/'source.ome.tiff', store/'source.assay.json')
    assert cube.shape == (256, 256, 24)
    assert len(waves) == 24 and pixel == .5
    assert meta['assayId'] == 'dapi-panck-cd3-cd8-v1'
    assert meta['referenceProvenance'] == 'synthetic-single-stain-controls'
    assert 'validTissue' not in meta and 'cells' not in meta
    maps, residual = unmix(cube, spectra, background)
    assert maps.shape == (256, 256, 5) and np.isfinite(residual).all()
    assert np.max(maps[:, :, 2]) > .5 and np.max(maps[:, :, 3]) > .5
    truth = tifffile.imread(Path(__file__).resolve().parents[2] / 'data/sample-spectral-mif.truth.ome.tiff')
    assert truth.shape == (3, 256, 256) and truth[2].max() > 20


def test_artifacts_qc_and_retry_are_deterministic(store):
    adapter = SpectralMifSimulationAdapter(store)
    metadata = adapter.inspect('source.ome.tiff', 'source.assay.json')
    assert metadata['bandCount'] == 24
    roi = dict(x=20, y=30, width=180, height=160)
    run = str(uuid4())
    manifest = adapter.analyze('source.ome.tiff', 'source.assay.json', roi, run, MODEL)
    cells = json.loads((store/manifest['cellsKey']).read_text())
    qc = json.loads((store/manifest['qcKey']).read_text())
    assert len(cells) > 20 and all(20 <= c['x'] < 200 and 30 <= c['y'] < 190 for c in cells)
    assert all('cd3Value' in c for c in cells)
    assert 0 < manifest['validTissuePx'] < roi['width']*roi['height']
    assert qc['validTissuePx'] == manifest['validTissuePx'] and qc['saturatedPx'] > 0
    assert adapter.analyze('source.ome.tiff', 'source.assay.json', roi, run, MODEL) == manifest
    assert [m['name'] for m in manifest['markers']] == ['DAPI','panCK','CD3','CD8']
    assert all(Image.open(store/m['displayKey']).size == (180,160) for m in manifest['markers'])
    with pytest.raises(ValueError, match='不同输入'):
        adapter.analyze('source.ome.tiff', 'source.assay.json', dict(x=0,y=0,width=20,height=20),run,MODEL)


def test_mismatched_or_corrupt_inputs_are_rejected(store):
    assay = store/'source.assay.json'
    meta = json.loads(assay.read_text())
    meta['imageSha256'] = '0'*64
    assay.write_text(json.dumps(meta))
    with pytest.raises(ValueError, match='校验值'):
        load_image(store/'source.ome.tiff', assay)
    (store/'corrupt.ome.tiff').write_bytes(b'not an image')
    with pytest.raises(Exception):
        load_image(store/'corrupt.ome.tiff', assay)


def test_http_contract(store, monkeypatch):
    monkeypatch.setattr(main,'adapter',SpectralMifSimulationAdapter(store))
    request={'imageKey':'source.ome.tiff','assayKey':'source.assay.json'}
    with TestClient(main.app,raise_server_exceptions=False) as client:
        assert client.get('/v1/health').status_code == 200
        assert client.post('/v1/inspect',json=request).status_code == 200
        assert client.post('/v1/inspect',json={**request,'imageKey':'../source.ome.tiff'}).status_code == 422
        result=client.post('/v1/analyze',json={**request,'runId':str(uuid4()),'roi':dict(x=20,y=30,width=180,height=160),'modelVersion':MODEL})
        assert result.status_code==200 and result.json()['cellCount']>20

@pytest.mark.parametrize('key',['../x','/tmp/x','a/../../x','a\\b'])
def test_file_traversal(store,key):
    with pytest.raises(ValueError): safe_path(store,key)
