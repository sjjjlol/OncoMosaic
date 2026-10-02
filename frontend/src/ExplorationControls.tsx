import { colors, labels, type Exploration, type Cell } from './types';
import { allObjects, distanceBins, selectedObjects, type ObjectSelection } from './exploration';

type Props = {result: Exploration; prefix: string; selection: ObjectSelection; onSelection: (s: ObjectSelection) => void; showNeighbors: boolean; onShowNeighbors: (value: boolean) => void; onCell?: (cell: Cell) => void; commonMax?: number};
export function ExplorationControls(p: Props) {
  const bins=distanceBins(p.result.nearestNeighbors,p.commonMax);
  const objects=selectedObjects(p.result.cells,p.result.nearestNeighbors,p.selection);
  const maxCount=Math.max(1,...bins.map(b=>b.count));
  return <section className="exploration-controls" aria-label={`${p.prefix}结果探索`}>
    <div className="exploration-toolbar">
      <label>对象筛选 <select aria-label={`${p.prefix}对象筛选`} value={p.selection.label} onChange={e=>p.onSelection({label:e.target.value,bin:null})}>
        <option value="all">全部对象</option><option value="valid">可分类对象</option>
        {Object.entries(labels).map(([value,label])=><option key={value} value={value}>{label}</option>)}
        <option value="changed">类别已改变</option><option value="quality">有质量标志</option>
      </select></label>
      <label><input aria-label={`${p.prefix}显示最近邻连线`} type="checkbox" checked={p.showNeighbors} onChange={e=>p.onShowNeighbors(e.target.checked)}/>最近邻连线</label>
      <span data-testid={`${p.prefix}-selected-count`}>选中 {objects.length} / {p.result.cells.length}</span>
      <button className="text-button" onClick={()=>p.onSelection(allObjects)}>清除筛选</button>
    </div>
    <div className="histogram-heading"><span>CD3⁺CD8⁺ → panCK 距离分布</span><small>{p.result.nearestNeighbors.length} 个可配对起点 · µm</small></div>
    {p.result.nearestNeighbors.length ? <div className="histogram" role="group" aria-label={`${p.prefix}距离分布`}>
      {bins.map((bin,i)=><button key={i} aria-label={`${p.prefix}距离 ${bin.min}–${bin.max} µm，${bin.count} 个对象`} aria-pressed={p.selection.bin?.min===bin.min&&p.selection.bin?.max===bin.max} disabled={!bin.count}
        onClick={()=>{p.onSelection({label:'cd3-cd8',bin});p.onShowNeighbors(true);}}>
        <span className="histogram-count">{bin.count}</span><span className="histogram-track"><i style={{height:`${Math.max(0,bin.count/maxCount*100)}%`}}/></span><small>{bin.min}–{bin.max}</small>
      </button>)}
    </div> : <p className="empty-distance">没有可配对对象，距离不可计算。</p>}
    {p.selection.ki67Population && <p className="selection-note">Ki-67 对象群筛选：{p.selection.ki67Population} · {p.selection.ki67State||'全部状态'}（不改变统计分母）</p>}
    {p.selection.bin && <p className="selection-note">筛选距离 {p.selection.bin.min}–{p.selection.bin.max} µm 的起点对象；点击柱形查看对应连线。最后区间含上界。</p>}
    {p.onCell && <div className="comparison-object-list" aria-label={`${p.prefix}对象列表`}>
      {objects.slice(0,100).map(c=><button key={c.cellId} onClick={()=>p.onCell!(c)}><i style={{background:colors[c.effectiveLabels]}}/>#{c.localIndex} · {labels[c.effectiveLabels]}</button>)}
      {objects.length>100&&<span>显示前 100 个对象；图上高亮全部匹配对象。</span>}
      {!objects.length&&<span>没有匹配对象。</span>}
    </div>}
  </section>;
}
