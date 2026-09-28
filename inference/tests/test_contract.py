import json
import sys
import zipfile
from pathlib import Path
from uuid import uuid4
import numpy as np
import pytest
from PIL import Image
from fastapi.testclient import TestClient
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from app.model import MockSpectralAdapter, load_image, safe_path, unmix
from app import main

@pytest.fixture
def store(tmp_path):
    source = Path(__file__).resolve().parents[2] / 'data/sample-hsi.npz'
    (tmp_path/'source.npz').write_bytes(source.read_bytes())
    return tmp_path


def test_deterministic_artifacts_and_coordinates(store):
    adapter = MockSpectralAdapter(store)
    metadata = adapter.inspect('source.npz')
    assert (metadata['width'], metadata['height'], metadata['bandCount']) == (256, 256, 16)
    roi = dict(x=20, y=30, width=180, height=160)
    run = str(uuid4())
    manifest = adapter.analyze('source.npz', roi, run, 'mock-unmix-v1')
    cells = json.loads((store/manifest['cellsKey']).read_text())
    assert len(cells) > 20
    assert all(20 <= c['x'] < 200 and 30 <= c['y'] < 190 for c in cells)
    assert any(c['qualityFlag'] == 'crop-edge' for c in cells)
    assert adapter.analyze('source.npz', roi, run, 'mock-unmix-v1') == manifest
    second = adapter.analyze('source.npz', roi, str(uuid4()), 'mock-unmix-v1')
    assert (store/manifest['cellsKey']).read_bytes() == (store/second['cellsKey']).read_bytes()
    assert np.load(store/manifest['maskKey']).shape == (160, 180)
    assert [m['name'] for m in manifest['markers']] == ['DAPI','panCK','CD8']
    assert all(Image.open(store/m['displayKey']).size == (180,160) for m in manifest['markers'])
    with pytest.raises(ValueError, match='不同输入'):
        adapter.analyze('source.npz', dict(x=0,y=0,width=20,height=20),run,'mock-unmix-v1')

@pytest.mark.parametrize('roi', [dict(x=-1,y=0,width=4,height=5),dict(x=250,y=0,width=10,height=5),dict(x=0,y=0,width=0,height=5)])
def test_bad_roi(store, roi):
    with pytest.raises(ValueError, match='ROI'):
        MockSpectralAdapter(store).analyze('source.npz',roi,str(uuid4()),'mock-unmix-v1')

@pytest.mark.parametrize('problem', ['dtype','nan','waves','pixel','bands'])
def test_bad_arrays(store, problem):
    cube=np.zeros((10,10,3),dtype=np.float32)
    waves=np.array([400,500,600],dtype=np.float32)
    pixel=1.
    if problem=='dtype': cube=cube.astype(np.float64)
    if problem=='nan': cube[0,0,0]=np.nan
    if problem=='waves': waves[1]=waves[0]
    if problem=='pixel': pixel=0
    if problem=='bands': cube=np.zeros((10,10,2),dtype=np.float32)
    np.savez(store/'bad.npz',cube=cube,wavelengths_nm=waves,pixel_size_um=pixel)
    with pytest.raises(ValueError): load_image(store/'bad.npz')


def test_shape_bomb_rejected_before_allocation(store):
    import io
    buf=io.BytesIO()
    np.lib.format.write_array_header_1_0(buf,dict(descr='<f4',fortran_order=False,shape=(2048,2048,64)))
    with zipfile.ZipFile(store/'bomb.npz','w') as archive:
        archive.writestr('cube.npy',buf.getvalue())
        archive.writestr('wavelengths_nm.npy',b'')
        archive.writestr('pixel_size_um.npy',b'')
    with pytest.raises(ValueError): load_image(store/'bomb.npz')


def test_contract_errors_and_valid_http(store, monkeypatch):
    monkeypatch.setattr(main,'adapter',MockSpectralAdapter(store))
    with TestClient(main.app,raise_server_exceptions=False) as client:
        assert client.get('/v1/health').status_code == 200
        assert client.post('/v1/inspect',json={'imageKey':'source.npz'}).status_code == 200
        assert client.post('/v1/inspect',json={'imageKey':'../source.npz'}).status_code == 422
        r=client.post('/v1/analyze',json={'imageKey':'source.npz','runId':str(uuid4()),'roi':dict(x=20,y=30,width=180,height=160),'modelVersion':'mock-unmix-v1'})
        assert r.status_code==200 and r.json()['cellCount']>20

@pytest.mark.parametrize('bands',[3,4,5,16,64])
def test_all_supported_bands_have_three_finite_channels(bands):
    result=unmix(np.ones((2,2,bands),dtype=np.float32))
    assert result.shape==(2,2,3) and np.isfinite(result).all()

@pytest.mark.parametrize('key',['../x','/tmp/x','a/../../x','a\\b'])
def test_file_traversal(store,key):
    with pytest.raises(ValueError): safe_path(store,key)

def test_corrupt_archive_is_a_readable_validation_error(store,monkeypatch):
    (store/'bad.npz').write_bytes(b'not a ZIP archive')
    monkeypatch.setattr(main,'adapter',MockSpectralAdapter(store))
    with TestClient(main.app,raise_server_exceptions=False) as client:
        response=client.post('/v1/inspect',json={'imageKey':'bad.npz'})
        assert response.status_code==422
        assert response.json()['code']=='INVALID_IMAGE'

def test_concurrent_retries_publish_once(store):
    from concurrent.futures import ThreadPoolExecutor
    adapter=MockSpectralAdapter(store)
    run=str(uuid4())
    def execute(_):return adapter.analyze('source.npz',dict(x=0,y=0,width=256,height=256),run,'mock-unmix-v1')
    with ThreadPoolExecutor(2) as executor:
        first,second=list(executor.map(execute,range(2)))
    assert first==second
    assert len(list((store/'runs').iterdir()))==1
