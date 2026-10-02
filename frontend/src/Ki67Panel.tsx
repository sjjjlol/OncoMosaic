import { useState } from 'react';
import { populations, type Summary } from './types';
import type { ObjectSelection } from './exploration';

export function Ki67Panel({summary,onSelection,prefix='当前区域'}:{summary:Pick<Summary,'thresholds'|'ki67'>; onSelection:(s:ObjectSelection)=>void; prefix?:string}) {
  const [population,setPopulation]=useState('panck');
  const q=summary.ki67?.[population];
  const pct=(v:number|null|undefined)=>v==null?'—':`${(v*100).toFixed(2)}%`;
  const select=(state?:string)=>onSelection({label:'all',bin:null,ki67Population:population,ki67State:state});
  return <section className="ki67-panel" aria-label={`${prefix} Ki-67 统计`}>
    <header><h3>Ki-67 核内阳性比例 <small>所选 ROI 总体 · 模拟</small></h3><select aria-label={`${prefix} Ki-67 对象群`} value={population} onChange={e=>{setPopulation(e.target.value);onSelection({label:'all',bin:null});}}>{Object.entries(populations).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select></header>
    <div className="ki67-counts"><strong data-testid={`${prefix}-ki67-fraction`}>{q?.status==='not-measured'?'未检测':pct(q?.fraction)}</strong>
      <button onClick={()=>select('positive')}>阳性 {q?.positiveCount??0}</button>
      <button onClick={()=>select('negative')}>阴性 {q?.negativeCount??0}</button>
      <button onClick={()=>select('evaluable')}>可评价 {q?.evaluableCount??0}</button>
      <button onClick={()=>select('indeterminate')}>无法判定 {q?.indeterminateCount??0}</button>
      <button onClick={()=>select('not-measured')}>未检测 {q?.notMeasuredCount??0}</button>
      <button onClick={()=>select()}>目标群 {q?.targetCount??0}</button>
    </div>
    <p>覆盖率 {pct(q?.coverage)} · ROI 整体排除/无效核 {q?.excludedCount??0} · 身份无法判定 {q?.identityUnclassifiedCount??0} · 核内演示阈值 {summary.thresholds.ki67??'未设置'}<br/>分母为本群阳性＋阴性对象；点击计数回图核查。{q?.reason && <b> {q.reason}</b>}</p>
  </section>;
}
