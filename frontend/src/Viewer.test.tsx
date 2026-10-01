// @vitest-environment jsdom
import React from 'react';
import { it, expect, vi } from 'vitest';
import { render, fireEvent } from '@testing-library/react';
import { Viewer } from './Viewer';
import type { Cell, TissueImage } from './types';
vi.stubGlobal('ResizeObserver', class {observe(){} disconnect(){}});
it('selects the exact cell and channel visibility cannot mutate measurements',()=>{
  const image:TissueImage={id:'i',name:'demo',description:'',width:256,height:256,bandCount:16,pixelSizeUm:.5,wavelengths:[400,700],previewUrl:'/preview',acquisition:{assayId:'test',scannerId:'test',stainBatchId:'test',calibrationId:'test',source:'synthetic-no-patient-data'}};
  const cell:Cell={cellId:'c1',localIndex:1,x:30,y:40,areaPx:12,intensities:{dapi:.7,panck:.8,cd3:.1,cd8:.1},autoLabels:'panck',effectiveLabels:'panck',qualityFlag:'ok',contour:[[28,38],[32,38],[32,42]]};
  const onCell=vi.fn();const props={image,rois:[],cells:[cell],mode:'pan' as const,display:'points' as const,channels:{composite:true,DAPI:false},opacity:1,onDraft:vi.fn(),onCell,onRoi:vi.fn()};
  const original=JSON.stringify(cell);const ui=render(<Viewer {...props}/>);
  fireEvent.click(ui.getByRole('button',{name:'细胞 1'}));expect(onCell).toHaveBeenCalledWith(cell);
  ui.rerender(<Viewer {...props} channels={{composite:false,DAPI:true}}/>);
  expect(JSON.stringify(cell)).toBe(original);expect(ui.getByRole('button',{name:'细胞 1'})).toBeTruthy();
});
it('draws backend pair coordinates and keeps the target visible when only the source is selected',()=>{
  const image:TissueImage={id:'pair-image',name:'demo',description:'',width:100,height:100,bandCount:16,pixelSizeUm:2,wavelengths:[400,700],previewUrl:'/preview',acquisition:{assayId:'test',scannerId:'test',stainBatchId:'test',calibrationId:'test',source:'synthetic-no-patient-data'}};
  const source:Cell={cellId:'source',localIndex:11,x:10,y:10,areaPx:12,intensities:{dapi:.7,panck:.1,cd3:.8,cd8:.8},autoLabels:'cd3-cd8',effectiveLabels:'cd3-cd8',qualityFlag:'ok',contour:[]};
  const target:Cell={...source,cellId:'target',localIndex:12,x:13,y:14,autoLabels:'panck',effectiveLabels:'panck'};
  const other:Cell={...target,cellId:'other',localIndex:13,x:50,y:50};
  const pair={sourceCellId:'source',targetCellId:'target',sourceIndex:11,targetIndex:12,sourceX:10,sourceY:10,targetX:13,targetY:14,distanceUm:10};
  const props={image,rois:[],cells:[source,target,other],mode:'pan' as const,display:'points' as const,channels:{composite:true},opacity:1,onDraft:vi.fn(),onCell:vi.fn(),onRoi:vi.fn(),highlightedCellIds:['source'],nearestNeighbors:[pair],showNeighbors:true};
  const ui=render(<Viewer {...props}/>);const line=ui.getByTestId('neighbor-11').querySelector('line')!;
  expect([line.getAttribute('x1'),line.getAttribute('y1'),line.getAttribute('x2'),line.getAttribute('y2')]).toEqual(['10','10','13','14']);
  expect(ui.getByTestId('cell-11').getAttribute('data-highlighted')).toBe('true');
  expect(ui.getByTestId('cell-12').getAttribute('opacity')).toBe('1');
  expect(ui.getByTestId('cell-13').getAttribute('opacity')).toBe('0.16');
  ui.rerender(<Viewer {...props} showNeighbors={false}/>);
  expect(ui.queryByTestId('neighbor-11')).toBeNull();expect(ui.getByTestId('cell-12').getAttribute('opacity')).toBe('0.16');
});
