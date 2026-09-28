"""Live API integration checks. --faults also stops/restarts project containers."""
import argparse
import csv
import io
import json
import subprocess
import time
import urllib.request
import urllib.error
import uuid
import zipfile
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--url',default='http://localhost:8088')
parser.add_argument('--faults',action='store_true')
args=parser.parse_args()

def call(path,body=None,method=None,expected=200):
    data=None if body is None else json.dumps(body).encode()
    req=urllib.request.Request(args.url+'/api'+path,data=data,method=method,headers={'Content-Type':'application/json'})
    try:
        with urllib.request.urlopen(req,timeout=120) as response:
            assert response.status==expected,(path,response.status)
            return json.load(response)
    except urllib.error.HTTPError as error:
        details=json.load(error)
        assert error.code==expected,(path,error.code,details)
        return details

def wait(run_id,status):
    for _ in range(110):
        run=call('/analysis-runs/'+run_id)
        if run['status']==status:return run
        if run['status']=='Failed' and status!='Failed':raise AssertionError(run)
        time.sleep(1)
    raise AssertionError(f'Timed out waiting for {status}')

def export(run_id,version):
    with urllib.request.urlopen(args.url+f'/api/analysis-runs/{run_id}/export?reviewVersion={version}') as response:
        data=response.read()
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        assert set(archive.namelist())=={'cells.csv','roi-summary.csv','overlay.png','method.json'}
        method=json.loads(archive.read('method.json').decode('utf-8-sig'))
        cells=list(csv.DictReader(io.StringIO(archive.read('cells.csv').decode('utf-8-sig'))))
        summary=list(csv.DictReader(io.StringIO(archive.read('roi-summary.csv').decode('utf-8-sig'))))[0]
        assert len(cells)==method['summary']['counts']['total']==int(summary['total'])
        assert method['reviewVersion']==version==int(summary['review_version'])
        assert sum(c['effective_label']!='excluded' for c in cells)==method['summary']['counts']['valid']
        assert method['summary']==call(f'/analysis-runs/{run_id}/summary?reviewVersion={version}')
        return method,archive.read('overlay.png')

# Upload isolation: rejected input leaves no analyzable image, a valid import persists.
import_project=call('/projects',dict(name='导入契约验收 '+str(uuid.uuid4())[:8]),expected=201)
def upload_bytes(content,name,expected):
    boundary='OncoMosaicBoundary'+uuid.uuid4().hex
    body=(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\nContent-Type: application/octet-stream\r\n\r\n'.encode()+content+f'\r\n--{boundary}--\r\n'.encode())
    req=urllib.request.Request(args.url+f"/api/projects/{import_project['id']}/images",data=body,headers={'Content-Type':'multipart/form-data; boundary='+boundary})
    try:
        with urllib.request.urlopen(req,timeout=120) as response:
            assert response.status==expected
            return json.load(response)
    except urllib.error.HTTPError as error:
        assert error.code==expected,(error.code,error.read())
upload_bytes(b'invalid zip','bad.npz',422)
assert call(f"/projects/{import_project['id']}/images")==[]
imported=upload_bytes(Path('data/sample-hsi.npz').read_bytes(),'imported.npz',201)
assert imported['width']==256 and imported['bandCount']==16
assert len(call(f"/projects/{import_project['id']}/images"))==1
project=call('/projects')[0]
images=call(f"/projects/{project['id']}/images")
image=next(i for i in images if i['id']=='a1000000-0000-0000-0000-000000000002')
roi=call(f"/images/{image['id']}/rois",dict(name='API 验收 ROI',x=20,y=30,width=180,height=160,regionTag='tumor-candidate'),expected=201)
call(f"/images/{image['id']}/rois",dict(name='bad',x=250,y=0,width=10,height=1,regionTag='other'),expected=400)
config=dict(imageId=image['id'],roiId=roi['id'],modelVersion='mock-unmix-v1',thresholds=dict(panck=.35,cd8=.35))
run=call('/analysis-runs',config,expected=202)
rid=run['runId']; wait(rid,'Succeeded')
cells=call(f'/analysis-runs/{rid}/cells')
assert len(cells)>20 and len({c['cellId'] for c in cells})==len(cells)
assert all(20<=c['x']<200 and 30<=c['y']<190 for c in cells)
assert all(c['autoLabels']==c['effectiveLabels'] for c in cells)
assert call(f'/analysis-runs/{rid}/summary')['areaMm2']==.0072
call(f'/analysis-runs/{rid}/retry',method='POST',expected=409)
call(f'/analysis-runs/{rid}/reviews',dict(cellId=str(uuid.uuid4()),newLabel='excluded'),expected=409)
auto,original_png=export(rid,0)
for index,label in enumerate(['excluded','cd8'],1):
    revision=call(f'/analysis-runs/{rid}/reviews',dict(cellId=cells[0]['cellId'],newLabel=label,reason='Integration verification'),expected=201)
    assert revision['reviewVersion']==index
reviewed,reviewed_png=export(rid,1)
assert reviewed['summary']['counts']['excluded']==1
assert original_png!=reviewed_png
latest,_=export(rid,2)
assert latest['summary']['counts']['excluded']==0
assert export(rid,0)[0]['summary']==auto['summary']
original_cells=call(f'/analysis-runs/{rid}/cells?reviewVersion=0')
assert original_cells==cells
call(f'/analysis-runs/{rid}/summary?reviewVersion=99',expected=404)
high=call('/analysis-runs',{**config,'thresholds':dict(panck=1,cd8=1)},expected=202)
wait(high['runId'],'Succeeded')
high_summary=call(f"/analysis-runs/{high['runId']}/summary")
assert high_summary['counts']['panck']==high_summary['counts']['cd8']==0
assert high_summary['meanNearestDistanceUm'] is None

fault_result=None
if args.faults:
    subprocess.run(['docker','compose','stop','inference'],check=True)
    try:
        failed=call('/analysis-runs',config,expected=202)
        failure=wait(failed['runId'],'Failed')
        assert failure['error'] is not None and failure['attempt']==1
        call(f"/analysis-runs/{failed['runId']}/cells",expected=409)
    finally:
        subprocess.run(['docker','compose','start','inference'],check=True)
    time.sleep(5)
    call(f"/analysis-runs/{failed['runId']}/retry",method='POST',expected=202)
    recovered=wait(failed['runId'],'Succeeded')
    assert recovered['attempt']==2
    retry_cells=call(f"/analysis-runs/{failed['runId']}/cells")
    assert len(retry_cells)==len(cells)==len({c['localIndex'] for c in retry_cells})
    fault_result={'runId':failed['runId'],'attempt':recovered['attempt'],'cellCount':len(retry_cells)}
    subprocess.run(['docker','compose','restart','db','api'],check=True)
    for _ in range(60):
        try:
            if call('/health')['status']=='ok': break
        except Exception: pass
        time.sleep(2)
    else: raise AssertionError('Services did not recover after restart')
    assert call(f'/analysis-runs/{rid}/summary?reviewVersion=1')==reviewed['summary']
    assert len(call(f"/projects/{project['id']}/images"))==len(images)
    assert export(rid,0)[0]['summary']==auto['summary']

report={'passed':True,'runId':rid,'cellCount':len(cells),'originalCounts':auto['summary']['counts'],'reviewedCounts':reviewed['summary']['counts'],'retry':fault_result,'checks':['ROI bounds','live Python call','original coordinates','physical units','review ordering','auto-result immutability','historical CSV/PNG/JSON exports','threshold version','missing nearest-neighbor null']}
Path('docs/api-verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(report,ensure_ascii=False,indent=2))
