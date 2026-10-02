"""Verify the actual downloads produced by frontend/e2e/ki67.spec.ts (stdlib only)."""
import csv
import io
import json
import zipfile
from pathlib import Path

root = Path(__file__).resolve().parents[1] / '.local'

def read_json(archive, name):
    return json.loads(archive.read(name).decode('utf-8-sig'))

def rows(archive, name):
    return list(csv.DictReader(io.StringIO(archive.read(name).decode('utf-8-sig'))))

def check_counts(cells, summaries, snapshot):
    for population, q in snapshot['ki67'].items():
        def belongs(c):
            return population == 'all' or (c['identity'] in ('cd3', 'cd3-cd8') if population == 'cd3' else c['identity'] == population)
        group = [c for c in cells if c['excluded'] == 'False' and belongs(c)]
        counts = {state: sum(c['effective_state'] == state for c in group) for state in ('positive','negative','indeterminate','not-measured')}
        p, n = counts['positive'], counts['negative']
        assert p == q['positiveCount'] and n == q['negativeCount']
        assert counts['indeterminate'] == q['indeterminateCount'] and counts['not-measured'] == q['notMeasuredCount']
        assert len(group) == q['targetCount'] and p+n == q['evaluableCount']
        if p+n: assert abs(p/(p+n)-q['fraction']) < 1e-10
        else: assert q['fraction'] is None
        table = next(r for r in summaries if r['population'] == population)
        assert int(table['positive']) == p and int(table['evaluable']) == p+n
        assert int(table['review_version']) == snapshot['reviewVersion']

with zipfile.ZipFile(root/'ki67-export.zip') as archive:
    method = read_json(archive,'method.json')
    assert method['exportSchemaVersion'] == 2 and method['reviewVersion'] == 1
    check_counts(rows(archive,'ki67-cells.csv'),rows(archive,'ki67-summary.csv'),method['summary'])
    assert {'Ki67.png','Ki67-quantitative.npy','mask.npy'} <= set(archive.namelist())
    assert any(c['auto_state'] == 'positive' and c['effective_state'] == 'negative' for c in rows(archive,'ki67-cells.csv'))
with zipfile.ZipFile(root/'ki67-legacy-export.zip') as archive:
    assert set(archive.namelist()) == {'cells.csv','roi-summary.csv','overlay.png','method.json','qc.json'}
    assert 'ki67' not in archive.read('cells.csv').decode('utf-8-sig').splitlines()[0]
    assert read_json(archive,'method.json')['reviewVersion'] == 0
with zipfile.ZipFile(root/'ki67-comparison-export.zip') as archive:
    method = read_json(archive,'method.json')
    assert method['exportSchemaVersion'] == 2 and method['ki67']['comparable']
    for side in ('A','B'):
        check_counts(rows(archive,f'ki67-cells-{side}.csv'),rows(archive,f'ki67-summary-{side}.csv'),method[side.lower()]['summary'])
print('Ki-67 downloaded exports verified: all populations, state history, pinned versions, quantitative files, legacy CSV compatibility')
