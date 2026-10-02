import json
import sys
import shutil
from pathlib import Path
from uuid import uuid4
import numpy as np
import pytest
from fastapi.testclient import TestClient
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from app import main, model

@pytest.fixture
def ki67_store(tmp_path):
    base = Path(__file__).resolve().parents[2] / 'data'
    for suffix in ('.ome.tiff', '.assay.json'):
        shutil.copyfile(base / ('sample-ki67' + suffix), tmp_path / ('source' + suffix))
    return tmp_path


def analyze(root, **kwargs):
    return model.SpectralMifSimulationAdapter(root).analyze('source.ome.tiff','source.assay.json',dict(x=0,y=0,width=256,height=256),str(uuid4()),kwargs.get('version',model.KI67_MODEL))


def test_five_markers_one_http_call_and_nuclear_measurements(ki67_store, monkeypatch):
    monkeypatch.setattr(main,'adapter',model.SpectralMifSimulationAdapter(ki67_store))
    request=dict(imageKey='source.ome.tiff',assayKey='source.assay.json',roi=dict(x=0,y=0,width=256,height=256),runId=str(uuid4()),modelVersion=model.KI67_MODEL)
    with TestClient(main.app) as client:
        response=client.post('/v1/analyze',json=request)
        assert response.status_code==200
        manifest=response.json()
        assert len(manifest['markers'])==5 and manifest['markers'][-1]['name']=='Ki67'
        assert client.post('/v1/analyze',json=request).json()==manifest
    values=np.load(ki67_store/manifest['ki67QuantitativeKey'])
    mask=np.load(ki67_store/manifest['maskKey'])
    cells=json.loads((ki67_store/manifest['cellsKey']).read_text())
    for c in cells:
        assert c['ki67Value']==pytest.approx(float(values[mask==c['localIndex']].mean()))
        assert c['ki67ValidPixelCount']==c['areaPx']
    assert any(c['ki67Value']>=.35 and c['ki67Quality']=='ok' for c in cells)
    assert any(c['ki67Value']<.35 and c['ki67Quality']=='ok' for c in cells)
    assert any(c['ki67Value']>=.35 and c['cd3Value']>=.35 and c['ki67Quality']=='ok' for c in cells)
    assert any(c['ki67Value']>=.35 and c['panckValue']>=.35 and c['ki67Quality']=='ok' for c in cells)
    assert any(c['ki67Value']<.35 and c['panckValue']>=.35 and c['ki67Quality']=='ok' for c in cells)


def test_ki67_is_measured_inside_nucleus_not_halo(ki67_store, monkeypatch):
    maps=np.zeros((256,256,6),dtype=np.float32);maps[:,:,-1]=.18
    maps[30:36,30:36,0]=.8
    maps[27:39,27:39,4]=.9
    maps[30:36,30:36,4]=.05
    monkeypatch.setattr(model,'unmix',lambda *_:(maps,np.zeros((256,256),dtype=np.float32)))
    result=analyze(ki67_store)
    cells=json.loads((ki67_store/result['cellsKey']).read_text())
    assert len(cells)==1
    assert cells[0]['ki67Value']==pytest.approx(.05)


def test_channel_qc_failure_does_not_destroy_other_markers(ki67_store, monkeypatch):
    original=model.unmix
    def bad(*args):
        maps,_=original(*args)
        return maps,np.full(maps.shape[:2],.2,dtype=np.float32)
    monkeypatch.setattr(model,'unmix',bad)
    result=analyze(ki67_store)
    cells=json.loads((ki67_store/result['cellsKey']).read_text())
    assert len(cells)>20
    assert all(c['ki67Quality'] in ('channel-qc-failed','crop-edge') for c in cells)
    assert any(c['panckValue']>.35 and c['qualityFlag']=='ok' for c in cells)


@pytest.mark.parametrize('mutation',['missing','degenerate','wrong-schema'])
def test_invalid_reference_panel_rejected(ki67_store,mutation):
    path=ki67_store/'source.assay.json'; data=json.loads(path.read_text())
    if mutation=='missing': data['referenceSpectra'].pop()
    elif mutation=='degenerate': data['referenceSpectra'][4]=data['referenceSpectra'][0]
    else: data['schema']='spectral-mif-research-v1'
    path.write_text(json.dumps(data))
    with pytest.raises(ValueError): analyze(ki67_store)


def test_old_model_rejects_five_marker_input(ki67_store):
    with pytest.raises(ValueError,match='v2'): analyze(ki67_store,version=model.MODEL)


def test_v2_four_marker_input_is_not_measured(tmp_path):
    base=Path(__file__).resolve().parents[2]/'data'
    for suffix in ('.ome.tiff','.assay.json'):
        shutil.copyfile(base/('sample-spectral-mif'+suffix),tmp_path/('source'+suffix))
    result=analyze(tmp_path)
    assert 'ki67QuantitativeKey' not in result
    cells=json.loads((tmp_path/result['cellsKey']).read_text())
    assert all(c['ki67Value'] is None and c['ki67Quality']=='not-measured' for c in cells)


def test_saturated_nuclear_signal_is_not_automatically_evaluable(ki67_store, monkeypatch):
    original = model.load_image
    def saturated(*args):
        cube,waves,pixel,reference,background,metadata=original(*args)
        cube[30:36,30:36,0]=1
        return cube,waves,pixel,reference,background,metadata
    maps=np.zeros((256,256,6),dtype=np.float32);maps[:,:,-1]=.18
    maps[30:36,30:36,0]=.8;maps[30:36,30:36,4]=.7
    monkeypatch.setattr(model,'load_image',saturated)
    monkeypatch.setattr(model,'unmix',lambda *_:(maps,np.zeros((256,256),dtype=np.float32)))
    result=analyze(ki67_store)
    cell=json.loads((ki67_store/result['cellsKey']).read_text())[0]
    assert cell['qualityFlag']=='ok' and cell['ki67Quality']=='saturated-signal'
