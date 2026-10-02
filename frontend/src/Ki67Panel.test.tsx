// @vitest-environment jsdom
import {render,screen,fireEvent,cleanup} from '@testing-library/react';
import {afterEach,expect,it,vi} from 'vitest';
import {Ki67Panel} from './Ki67Panel';
import {selectedObjects} from './exploration';
import type {Summary,Cell} from './types';
afterEach(cleanup);
it('shows evaluable denominator and selects exactly that population/state',()=>{
  const summary={thresholds:{panck:.35,cd3:.35,cd8:.35,ki67:.35},ki67:{panck:{positiveCount:30,negativeCount:60,indeterminateCount:10,notMeasuredCount:0,evaluableCount:90,targetCount:100,excludedCount:2,identityUnclassifiedCount:1,fraction:1/3,coverage:.9,status:'ok',positiveDensity:6000,reason:null}}} satisfies Pick<Summary,'thresholds'|'ki67'>;
  const select=vi.fn();render(<Ki67Panel summary={summary} onSelection={select}/>);
  expect(screen.getByTestId('当前区域-ki67-fraction').textContent).toBe('33.33%');
  fireEvent.click(screen.getByRole('button',{name:'可评价 90'}));
  const cells=[{cellId:'a',qualityFlag:'ok',effectiveLabels:'panck',ki67:{effectiveState:'positive'}},{cellId:'b',qualityFlag:'ok',effectiveLabels:'panck',ki67:{effectiveState:'indeterminate'}},{cellId:'c',qualityFlag:'ok',effectiveLabels:'cd3',ki67:{effectiveState:'positive'}},{cellId:'d',qualityFlag:'ok',effectiveLabels:'excluded',ki67:{effectiveState:'positive'}}] as Cell[];
  expect(selectedObjects(cells,[],select.mock.calls[0][0]).map(c=>c.cellId)).toEqual(['a']);
});
it('shows not measured without inventing a zero percentage',()=>{
  render(<Ki67Panel summary={{thresholds:{panck:.35,cd3:.35,cd8:.35},ki67:{panck:{status:'not-measured',fraction:null,positiveCount:0,negativeCount:0,indeterminateCount:0,notMeasuredCount:1,evaluableCount:0,targetCount:1,excludedCount:0,identityUnclassifiedCount:0,coverage:0,positiveDensity:null,reason:'未检测'}}}} onSelection={()=>{}}/>);
  expect(screen.getByTestId('当前区域-ki67-fraction').textContent).toBe('未检测');
});
