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
