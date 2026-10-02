import type { Cell, NearestNeighbor } from './types';

export type DistanceBin = {min: number; max: number; includeMax: boolean; count: number};
export type ObjectSelection = {label: string; bin: DistanceBin|null; ki67Population?: string; ki67State?: string};
export const allObjects: ObjectSelection = {label: 'all', bin: null};

export function inBin(distance: number, bin: DistanceBin) {
  return distance >= bin.min && (distance < bin.max || (bin.includeMax && distance === bin.max));
}

export function distanceBins(pairs: NearestNeighbor[], commonMax?: number): DistanceBin[] {
  const max = commonMax ?? Math.max(0, ...pairs.map(p => p.distanceUm));
  const step = Math.max(1, Math.ceil(max / 6));
  return Array.from({length: 6}, (_, index) => {
    const bin = {min: index*step, max: (index+1)*step, includeMax: index === 5, count: 0};
    return {...bin, count: pairs.filter(p => inBin(p.distanceUm, bin)).length};
  });
}

export function selectedObjects(cells: Cell[], pairs: NearestNeighbor[], selection: ObjectSelection) {
  const sources = selection.bin ? new Set(pairs.filter(p => inBin(p.distanceUm, selection.bin!)).map(p => p.sourceCellId)) : null;
  return cells.filter(c => {
    const matches = selection.label === 'all' || (selection.label === 'valid' ? !['excluded', 'unclassified'].includes(c.effectiveLabels) : selection.label === 'changed' ? c.autoLabels !== c.effectiveLabels : selection.label === 'quality' ? c.qualityFlag !== 'ok' : c.effectiveLabels === selection.label);
    const population = selection.ki67Population;
    const k = c.ki67?.effectiveState ?? 'not-measured';
    const eligible = c.qualityFlag === 'ok' && c.effectiveLabels !== 'excluded';
    const populationMatches = !population || (eligible && (population === 'all' || (population === 'cd3' ? ['cd3','cd3-cd8'].includes(c.effectiveLabels) : c.effectiveLabels === population)));
    const stateMatches = !selection.ki67State || (selection.ki67State === 'evaluable' ? ['positive','negative'].includes(k) : k === selection.ki67State);
    return matches && populationMatches && stateMatches && (!sources || sources.has(c.cellId));
  });
}

export function selectedPairs(pairs: NearestNeighbor[], objects: Cell[]) {
  const ids = new Set(objects.map(c => c.cellId));
  return pairs.filter(p => ids.has(p.sourceCellId) || ids.has(p.targetCellId));
}

export async function downloadResponse(response: Response, name: string) {
  if (!response.ok) throw new Error((await response.json().catch(() => ({message:'导出失败'}))).message);
  const url = URL.createObjectURL(await response.blob());
  const a = document.createElement('a'); a.href=url; a.download=name; a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
