"""Live comparison contract checks; uses synthetic input already imported in the first project."""
import argparse
import csv
import io
import json
import math
import time
import urllib.error
import urllib.request
import uuid
import zipfile
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--url',default='http://localhost:8088')
args=parser.parse_args()

def call(path,body=None,expected=200,raw=False):
    req=urllib.request.Request(args.url+'/api'+path,data=None if body is None else json.dumps(body).encode(),headers={'Content-Type':'application/json'})
    try:
        with urllib.request.urlopen(req,timeout=120) as response:
            assert response.status==expected,(path,response.status)
            return response.read() if raw else json.load(response)
    except urllib.error.HTTPError as error:
        details=json.load(error)
        assert error.code==expected,(path,error.code,details)
        return details

def run(image,roi,threshold=.35):
    thresholds=dict(panck=threshold,cd3=threshold,cd8=threshold)
    has_ki67=image.get('capabilities',{}).get('ki67',False)
    if has_ki67: thresholds['ki67']=threshold
    model='spectral-mif-sim-v2' if has_ki67 else 'spectral-mif-sim-v1'
    result=call('/analysis-runs',dict(imageId=image['id'],roiId=roi['id'],modelVersion=model,thresholds=thresholds),202)
    for _ in range(60):
        state=call('/analysis-runs/'+result['runId'])
        if state['status']=='Succeeded':return result['runId']
        assert state['status']!='Failed',state
        time.sleep(1)
    raise AssertionError('analysis timeout')

def verify_pairs(side,pixel):
    cells={c['cellId']:c for c in side['cells']}
    targets=[c for c in cells.values() if c['effectiveLabels']=='panck']
    sources=[c for c in cells.values() if c['effectiveLabels']=='cd3-cd8']
    pairs=side['nearestNeighbors']
    assert len(pairs)==(len(sources) if targets else 0)
    for pair in pairs:
        source=cells[pair['sourceCellId']];target=cells[pair['targetCellId']]
        assert source['effectiveLabels']=='cd3-cd8' and target['effectiveLabels']=='panck'
        assert source['cellId']!=target['cellId']
        distance=math.hypot(source['x']-target['x'],source['y']-target['y'])*pixel
        assert math.isclose(pair['distanceUm'],distance,abs_tol=1e-9)
        assert math.isclose(distance,min(math.hypot(source['x']-t['x'],source['y']-t['y'])*pixel for t in targets),abs_tol=1e-9)
        assert [pair['sourceX'],pair['sourceY'],pair['targetX'],pair['targetY']]==[source['x'],source['y'],target['x'],target['y']]
    mean=side['summary']['meanNearestDistanceUm']
    assert math.isclose(mean,sum(p['distanceUm'] for p in pairs)/len(pairs),abs_tol=1e-9) if pairs else mean is None

project=call('/projects')[0]
image=call(f"/projects/{project['id']}/images")[0]
suffix=uuid.uuid4().hex[:6]
rois=[call(f"/images/{image['id']}/rois",dict(name=f'比较 {side},{suffix}',x=0,y=y,width=256,height=128,regionTag='other'),201) for side,y in [('A',0),('B',128)]]
a,b=[run(image,r) for r in rois]
path=f"/images/{image['id']}/comparisons"
input=dict(a=dict(runId=a,reviewVersion=0),b=dict(runId=b,reviewVersion=0))
original=call(path,input)
assert original['sameScheme']
assert original['ki67']['comparable']==image.get('capabilities',{}).get('ki67',False)
for side in (original['a'],original['b']):verify_pairs(side,image['pixelSizeUm'])
assert call(f'/analysis-runs/{a}/exploration?reviewVersion=0')==original['a']
assert original['a']['nearestNeighbors']
target=original['a']['nearestNeighbors'][0]['targetCellId']
call(f'/analysis-runs/{a}/reviews',dict(cellId=target,newLabel='excluded',reason='最近邻版本一致性验收',baseReviewVersion=original['a']['summary']['reviewVersion']),201)
latest=call(path,dict(a=dict(runId=a),b=dict(runId=b)))
assert latest['a']['summary']['reviewVersion']==1
assert all(p['targetCellId']!=target for p in latest['a']['nearestNeighbors'])
verify_pairs(latest['a'],image['pixelSizeUm'])
assert call(path,input)==original
archive=zipfile.ZipFile(io.BytesIO(call(path+'/export',input,raw=True)))
assert set(archive.namelist())=={'cells-A.csv','cells-B.csv','comparison-summary.csv','nearest-neighbors.csv','method.json'}
method=json.loads(archive.read('method.json').decode('utf-8-sig'))
assert method['a']['summary']==original['a']['summary'] and method['b']['summary']==original['b']['summary']
rows=list(csv.DictReader(io.StringIO(archive.read('comparison-summary.csv').decode('utf-8-sig'))))
assert [r['roi_name'] for r in rows]==[r['name'] for r in rois]
assert all(r['review_version']=='0' for r in rows)
pairs=list(csv.DictReader(io.StringIO(archive.read('nearest-neighbors.csv').decode('utf-8-sig'))))
assert len(pairs)==len(original['a']['nearestNeighbors'])+len(original['b']['nearestNeighbors'])
for name,side in [('A',original['a']),('B',original['b'])]:
    cells=list(csv.DictReader(io.StringIO(archive.read(f'cells-{name}.csv').decode('utf-8-sig'))))
    assert len(cells)==side['summary']['counts']['total']
    assert sum(c['effective_label']=='panck' for c in cells)==side['summary']['counts']['panck']
empty=run(image,rois[1],1)
mismatch=call(path,dict(a=dict(runId=a,reviewVersion=0),b=dict(runId=empty,reviewVersion=0)))
assert not mismatch['sameScheme'] and any('不一致' in w for w in mismatch['warnings'])
assert mismatch['b']['nearestNeighbors']==[] and mismatch['b']['summary']['meanNearestDistanceUm'] is None
call(path,dict(a=dict(runId=a),b=dict(runId=a)),400)
call(f'/images/{uuid.uuid4()}/comparisons',input,400)
call(path,dict(a=dict(runId=a,reviewVersion=99),b=dict(runId=b)),404)
call(path,dict(a=None,b=dict(runId=b)),400)
report=dict(passed=True,modelVersion=original['a']['modelVersion'],ki67Capable=image.get('capabilities',{}).get('ki67',False),runIds=[a,b],reviewVersions=[0,0],pairCounts=[len(original['a']['nearestNeighbors']),len(original['b']['nearestNeighbors'])],checks=['nearest pair identities and physical coordinates','per-ROI summary means','review exclusion recomputes targets','historical snapshots unchanged','latest resolved to explicit versions','pinned CSV and JSON comparison export','quoted ROI names','scheme mismatch warning','missing pair null','same ROI / wrong image / nonexistent version / missing selection rejected'])
Path('docs/comparison-verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(report,ensure_ascii=False,indent=2))
