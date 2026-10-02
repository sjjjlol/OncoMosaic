import { useEffect, useRef, useState } from 'react';
import { ArrowDownToLine, X } from 'lucide-react';
import { api, post } from './api';
import { Ki67Panel } from './Ki67Panel';
import { Viewer } from './Viewer';
import { ExplorationControls } from './ExplorationControls';
import { allObjects, downloadResponse, selectedObjects, selectedPairs, type ObjectSelection } from './exploration';
import { labels, type Cell, type ComparisonResult, type Exploration, type Review, type Roi, type Run, type TissueImage } from './types';

type Props = {image: TissueImage; rois: Roi[]; runs: Run[]; preferredRunId: string; onClose: () => void};
type Metric = {name: string; value: (r: Exploration) => string; filter?: string; links?: boolean};
const number=(value:number|null,digits=2)=>value===null?'不可计算':value.toFixed(digits);
const metrics:Metric[]=[
  {name:'总对象 / 可分类',value:r=>`${r.summary.counts.total} / ${r.summary.counts.valid}`,filter:'valid'},
  {name:'panCK⁺ 候选数量',value:r=>`${r.summary.counts.panck} 个`,filter:'panck'},
  {name:'CD3 单阳性候选数量',value:r=>`${r.summary.counts.cd3Only} 个`,filter:'cd3'},
  {name:'CD3⁺CD8⁺ 候选数量',value:r=>`${r.summary.counts.cd3Cd8} 个`,filter:'cd3-cd8'},
  {name:'CD3⁺CD8⁺ 比例',value:r=>r.summary.cd3Cd8Fraction===null?'不可计算':`${(r.summary.cd3Cd8Fraction*100).toFixed(1)}%`,filter:'cd3-cd8'},
  {name:'CD3⁺CD8⁺ 密度',value:r=>`${number(r.summary.cd3Cd8Density,1)} 个/mm²`,filter:'cd3-cd8'},
  {name:'有效组织面积',value:r=>`${number(r.summary.areaMm2,6)} mm²`},
  {name:'平均最近邻距离',value:r=>r.summary.meanNearestDistanceUm===null?'不可计算':`${number(r.summary.meanNearestDistanceUm)} µm`,filter:'cd3-cd8',links:true},
];

function ComparisonSide({name,image,result,selection,onSelection,showNeighbors,onShowNeighbors,commonMax}:{name:string;image:TissueImage;result:Exploration;selection:ObjectSelection;onSelection:(s:ObjectSelection)=>void;showNeighbors:boolean;onShowNeighbors:(v:boolean)=>void;commonMax:number}) {
  const [cellId,setCellId]=useState('');
  const [channels,setChannels]=useState<Record<string,boolean>>({composite:true,DAPI:false,panCK:false,CD3:false,CD8:false,Ki67:false});
  const objects=selectedObjects(result.cells,result.nearestNeighbors,selection);
  const pairs=selectedPairs(result.nearestNeighbors,objects);
  const cell=result.cells.find(c=>c.cellId===cellId);
  const neighbor=result.nearestNeighbors.find(n=>n.sourceCellId===cellId);
  const onCell=(c:Cell)=>setCellId(c.cellId);
  return <section className="comparison-side" aria-label={`${name}结果`}>
    <div className="comparison-side-heading"><h3>{name} · {result.roi.name}</h3><span>复核 v{result.summary.reviewVersion}</span></div>
    <div className="comparison-channels">{['composite','DAPI','panCK','CD3','CD8',...(image.capabilities?.ki67?['Ki67']:[])].map(c=><label key={c}><input type="checkbox" checked={channels[c]} onChange={e=>setChannels({...channels,[c]:e.target.checked})}/>{c==='composite'?'合成预览':c}</label>)}</div>
    <div className="comparison-viewer"><Viewer image={image} rois={[result.roi]} selectedRoi={result.roi} runId={result.runId} cells={result.cells} selectedCell={cellId}
      mode="pan" display="contour" channels={channels} opacity={.8} onDraft={()=>{}} onRoi={()=>{}} onCell={onCell} focusRoi testId={`${name}-viewer`}
      highlightedCellIds={objects.map(c=>c.cellId)} nearestNeighbors={pairs} showNeighbors={showNeighbors}/></div>
    <Ki67Panel summary={result.summary} prefix={name} onSelection={onSelection}/>
    <ExplorationControls result={result} prefix={name} selection={selection} onSelection={onSelection} showNeighbors={showNeighbors} onShowNeighbors={onShowNeighbors} commonMax={commonMax} onCell={onCell}/>
    {cell&&<div className="comparison-cell" data-testid={`${name}-cell-detail`}>
      <strong>对象 #{cell.localIndex} · {labels[cell.effectiveLabels]}</strong>
      <span>panCK {cell.intensities.panck.toFixed(3)} · CD3 {cell.intensities.cd3.toFixed(3)} · CD8 {cell.intensities.cd8.toFixed(3)} · Ki-67 {cell.ki67?.value?.toFixed(3)??'未检测'}</span>
      {neighbor?<span>最近上皮候选 <button className="text-button" onClick={()=>setCellId(neighbor.targetCellId)}>#{neighbor.targetIndex}</button> · {neighbor.distanceUm.toFixed(2)} µm</span>:<span>{cell.effectiveLabels==='cd3-cd8'?'本 ROI 无可配对上皮候选对象。':'最近邻以 CD3⁺CD8⁺ 候选为起点。'}</span>}
    </div>}
    <p className="comparison-side-note">排除 {result.summary.counts.excluded} · 无法判定 {result.summary.counts.unclassified} · 面板内阴性 {result.summary.counts.negative}<br/>阈值 panCK / CD3 / CD8 / Ki-67：{Object.values(result.summary.thresholds).join(' / ')} · 任务 {result.runId.slice(0,8)}</p>
  </section>;
}

export function RegionComparison(p:Props) {
  const candidates=p.runs.filter(r=>r.status==='Succeeded');
  const first=candidates.find(r=>r.runId===p.preferredRunId)||candidates[0];
  const second=candidates.find(r=>r.roiId!==first?.roiId);
  const [a,setA]=useState(first?.runId||''), [b,setB]=useState(second?.runId||'');
  const [va,setVa]=useState('latest'), [vb,setVb]=useState('latest');
  const [versions,setVersions]=useState<Record<string,number[]>>({});
  const [result,setResult]=useState<ComparisonResult|null>(null), [busy,setBusy]=useState(false), [error,setError]=useState('');
  const [selectionA,setSelectionA]=useState<ObjectSelection>(allObjects), [selectionB,setSelectionB]=useState<ObjectSelection>(allObjects);
  const [linksA,setLinksA]=useState(true), [linksB,setLinksB]=useState(true);
  const generation=useRef(0);
  const dialog=useRef<HTMLDivElement>(null);
  useEffect(()=>{
    let active=true;
    Promise.all([...new Set([a,b].filter(Boolean))].map(async id=>[id,await api<Review[]>(`/analysis-runs/${id}/reviews`)] as const)).then(items=>{
      if(active)setVersions(Object.fromEntries(items.map(([id,reviews])=>[id,[...new Set(reviews.map(r=>r.version))]])));
    }).catch(e=>{if(active)setError(e.message);});
    return()=>{active=false;};
  },[a,b]);
  useEffect(()=>{++generation.current;setResult(null);setError('');setBusy(false);setSelectionA(allObjects);setSelectionB(allObjects);},[a,b,va,vb]);
  useEffect(()=>{
    const previous=document.activeElement as HTMLElement|null;
    dialog.current?.querySelector<HTMLButtonElement>('button[aria-label="关闭区域比较"]')?.focus();
    const close=(event:KeyboardEvent)=>{
      if(event.key==='Escape')p.onClose();
      if(event.key==='Tab'){
        const elements=Array.from(dialog.current?.querySelectorAll<HTMLElement>('button:not(:disabled), select:not(:disabled), input:not(:disabled)')||[]);
        const first=elements[0],last=elements.at(-1);
        if(event.shiftKey&&document.activeElement===first){event.preventDefault();last?.focus();}
        else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first?.focus();}
      }
    };
    document.addEventListener('keydown',close);
    return()=>{++generation.current;document.removeEventListener('keydown',close);previous?.focus();};
  },[p.onClose]);
  const sameRoi=p.runs.find(r=>r.runId===a)?.roiId===p.runs.find(r=>r.runId===b)?.roiId;
  const input={a:{runId:a,reviewVersion:va==='latest'?null:Number(va)},b:{runId:b,reviewVersion:vb==='latest'?null:Number(vb)}};
  const load=async()=>{
    const ticket=++generation.current;setBusy(true);setError('');setResult(null);
    try {
      const next=await post<ComparisonResult>(`/images/${p.image.id}/comparisons`,input);
      if(ticket===generation.current){
        setResult(next);setSelectionA(allObjects);setSelectionB(allObjects);
        setVersions(previous=>({...previous,...Object.fromEntries([next.a,next.b].map(side=>[side.runId,[...new Set([...(previous[side.runId]||[]),...side.reviews.map(r=>r.version)])].sort((x,y)=>x-y)]))}));
      }
    }
    catch(e){if(ticket===generation.current)setError((e as Error).message);}
    finally{if(ticket===generation.current)setBusy(false);}
  };
  const download=async()=>{
    if(!result)return;
    const ticket=++generation.current;setBusy(true);setError('');
    try {
      const pinned={a:{runId:result.a.runId,reviewVersion:result.a.summary.reviewVersion},b:{runId:result.b.runId,reviewVersion:result.b.summary.reviewVersion}};
      const response=await fetch(`/api/images/${p.image.id}/comparisons/export?exportSchemaVersion=2`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(pinned)});
      if(ticket===generation.current)await downloadResponse(response,`oncomosaic-comparison-v${pinned.a.reviewVersion}-v${pinned.b.reviewVersion}.zip`);
    } catch(e){if(ticket===generation.current)setError((e as Error).message);}
    finally{if(ticket===generation.current)setBusy(false);}
  };
  const commonMax=result?Math.max(0,...result.a.nearestNeighbors.map(n=>n.distanceUm),...result.b.nearestNeighbors.map(n=>n.distanceUm)):0;
  return <div className="comparison-backdrop"><div ref={dialog} role="dialog" aria-modal="true" aria-labelledby="comparison-title" className="comparison-dialog">
    <header className="comparison-header"><div><h2 id="comparison-title">多区域比较</h2><p>同一图像 · 组成、密度与空间关系 · 合成数据 / 模拟分析</p></div><div><button className="secondary" onClick={download} disabled={!result||busy}><ArrowDownToLine size={14}/>导出比较快照</button><button aria-label="关闭区域比较" onClick={p.onClose}><X size={20}/></button></div></header>
    <div className="comparison-scroll">
      <div className="comparison-selectors">{[{name:'区域 A',id:a,version:va,setId:setA,setVersion:setVa},{name:'区域 B',id:b,version:vb,setId:setB,setVersion:setVb}].map(side=><section key={side.name}>
        <label>{side.name} 分析记录<select aria-label={`${side.name}分析记录`} value={side.id} onChange={e=>{side.setId(e.target.value);side.setVersion('latest');}}>
          {!side.id&&<option value="">请选择已完成任务</option>}{candidates.map(r=><option value={r.runId} key={r.runId}>{p.rois.find(roi=>roi.id===r.roiId)?.name} · {r.runId.slice(0,8)} · {new Date(r.createdAt).toLocaleString()}</option>)}
        </select></label>
        <label>复核版本<select aria-label={`${side.name}复核版本`} value={side.version} onChange={e=>side.setVersion(e.target.value)}><option value="latest">载入时的最新版本</option><option value="0">原始自动结果 · v0</option>{(versions[side.id]||[]).map(v=><option value={v} key={v}>复核版本 v{v}</option>)}</select></label>
      </section>)}</div>
      <div className="comparison-actions"><button className="primary" onClick={load} disabled={!a||!b||sameRoi||busy}>{busy?'处理中…':'比较所选区域'}</button><span>{sameRoi?'请选择两个不同的 ROI。':'建议选择相同阈值与算法版本的结果。'}</span></div>
      {error&&<p role="alert" className="comparison-error">{error}</p>}
      {!result&&!error&&<p className="comparison-empty">选择两个已完成分析的不同区域，再载入比较。最新版本在载入时固定，后续复核不会改变当前快照。</p>}
      {result&&<>
        <p className={`comparison-scheme ${result.sameScheme?'':'mismatch'}`} data-testid="comparison-scheme">{result.sameScheme?'分析方案一致':'分析方案不一致'} · A 复核 v{result.a.summary.reviewVersion} / B 复核 v{result.b.summary.reviewVersion}</p>
        <p className="comparison-definitions">Ki-67：{result.ki67?.comparable ? `上皮候选比例 A−B ${result.ki67.differencePercentagePoints.panck==null?'不可计算':result.ki67.differencePercentagePoints.panck.toFixed(2)+' 个百分点'}（局部描述性比较）` : result.ki67?.reasons.join('；')||'不可比较'}。各对象群的分母与覆盖率见下方。</p>
        <table className="comparison-table"><caption>点击数量、比例或密度高亮对应对象；点击距离显示配对连线。</caption><thead><tr><th>指标</th><th>区域 A · {result.a.roi.name}</th><th>区域 B · {result.b.roi.name}</th></tr></thead><tbody>
          {metrics.map(metric=><tr key={metric.name}><th>{metric.name}</th>{[{name:'区域 A',side:result.a,setSelection:setSelectionA,setLinks:setLinksA},{name:'区域 B',side:result.b,setSelection:setSelectionB,setLinks:setLinksB}].map(({name,side,setSelection,setLinks})=><td key={name}>{metric.filter?<button aria-label={`${name} ${metric.name}`} onClick={()=>{setSelection({label:metric.filter!,bin:null});if(metric.links)setLinks(true);}}>{metric.value(side)}</button>:metric.value(side)}</td>)}</tr>)}
        </tbody></table>
        <p className="comparison-definitions">比例分母：可分类且未排除对象。密度分母：各 ROI 有效组织面积。距离：CD3⁺CD8⁺ → 最近的不同 panCK⁺ 对象的核中心距离。两侧分布图使用相同区间，每个起点计一次。</p>
        <div className="comparison-sides"><ComparisonSide key={`a-${result.a.runId}-${result.a.summary.reviewVersion}`} name="区域 A" image={p.image} result={result.a} selection={selectionA} onSelection={setSelectionA} showNeighbors={linksA} onShowNeighbors={setLinksA} commonMax={commonMax}/><ComparisonSide key={`b-${result.b.runId}-${result.b.summary.reviewVersion}`} name="区域 B" image={p.image} result={result.b} selection={selectionB} onSelection={setSelectionB} showNeighbors={linksB} onShowNeighbors={setLinksB} commonMax={commonMax}/></div>
        <ul className="comparison-notes">{result.warnings.map(w=><li key={w}>{w}</li>)}</ul>
      </>}
    </div>
  </div></div>;
}
