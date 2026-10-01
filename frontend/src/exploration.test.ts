import {describe,it,expect} from 'vitest';
import {distanceBins,inBin,selectedObjects,selectedPairs} from './exploration';
import type {Cell,NearestNeighbor} from './types';
const cell=(id:string,label:string,auto=label):Cell=>({cellId:id,localIndex:1,x:10,y:10,areaPx:10,intensities:{dapi:.8,panck:.2,cd3:.4,cd8:.5},autoLabels:auto,effectiveLabels:label,qualityFlag:'ok',contour:[]});
const pair=(id:string,distance:number):NearestNeighbor=>({sourceCellId:id,targetCellId:'target',sourceIndex:1,targetIndex:2,sourceX:10,sourceY:10,targetX:20,targetY:20,distanceUm:distance});
describe('linked result exploration',()=>{
  it('uses shared distance bins and includes each boundary only once',()=>{
    const a=distanceBins([pair('a',10),pair('b',60)],60),b=distanceBins([pair('c',0)],60);
    expect(a.map(x=>[x.min,x.max])).toEqual(b.map(x=>[x.min,x.max]));
    expect(a.map(x=>x.count)).toEqual([0,1,0,0,0,1]);
    expect(inBin(10,a[0])).toBe(false);expect(inBin(60,a[5])).toBe(true);
    expect(distanceBins([]).every(x=>x.count===0)).toBe(true);
  });
  it('links a distance bin to reviewed source objects and keeps their target pairs',()=>{
    const cells=[cell('a','cd3-cd8'),cell('b','excluded','panck'),cell('target','panck'),cell('c','unclassified')];
    const pairs=[pair('a',10)];const before=JSON.stringify(cells);
    const objects=selectedObjects(cells,pairs,{label:'cd3-cd8',bin:distanceBins(pairs)[5]});
    expect(objects.map(c=>c.cellId)).toEqual(['a']);
    expect(selectedPairs(pairs,objects)).toEqual(pairs);
    expect(selectedPairs(pairs,[cells[2]])).toEqual(pairs);
    expect(selectedObjects(cells,pairs,{label:'valid',bin:null}).map(c=>c.cellId)).toEqual(['a','target']);
    expect(selectedObjects(cells,pairs,{label:'changed',bin:null})).toEqual([cells[1]]);
    expect(JSON.stringify(cells)).toBe(before);
  });
});
