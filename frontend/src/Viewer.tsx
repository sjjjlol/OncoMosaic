import { useEffect, useRef, useState } from 'react';
import { Minus, Plus, Maximize } from 'lucide-react';
import { imagePoint, rectangle } from './geometry';
import { colors, type TissueImage, type Rect, type Roi, type Cell } from './types';

type Props = { image: TissueImage; rois: Roi[]; selectedRoi?: Roi; runId?: string; cells: Cell[]; selectedCell?: string; mode: 'pan'|'roi'; display: 'contour'|'points'|'off'; channels: Record<string, boolean>; opacity: number; draft?: Rect; onDraft: (r: Rect) => void; onCell: (c: Cell) => void; onRoi: (r: Roi) => void };
export function Viewer(p: Props) {
  const svg = useRef<SVGSVGElement>(null);
  const [view, setView] = useState({x: 70, y: 35, scale: 1.7});
  const [bounds, setBounds] = useState({width: 700, height: 500});
  const [drag, setDrag] = useState<{x: number; y: number; px: number; py: number}|null>(null);
  useEffect(() => { const observer = new ResizeObserver(([entry]) => setBounds({width: entry.contentRect.width, height: entry.contentRect.height})); if (svg.current) observer.observe(svg.current); return () => observer.disconnect(); }, []);
  const fit = () => { const scale = Math.min((bounds.width-110)/p.image.width, (bounds.height-85)/p.image.height); setView({scale, x: (bounds.width-p.image.width*scale)/2, y: (bounds.height-p.image.height*scale)/2}); };
  useEffect(fit, [p.image.id, bounds.width, bounds.height]);
  const point = (e: {clientX: number; clientY: number}) => { const r = svg.current!.getBoundingClientRect(); return {x: e.clientX-r.left, y: e.clientY-r.top}; };
  const zoom = (factor: number, center = {x: bounds.width/2, y: bounds.height/2}) => setView(v => { const scale = Math.max(.15, Math.min(20, v.scale*factor)); const pt = imagePoint(center, v); return {scale, x: center.x-pt.x*scale, y: center.y-pt.y*scale}; });
  return <div className="viewer-wrap">
    <div className="viewer-label"><span className="live-dot"/> MULTIPLEX VIEWER <span>原图坐标 · px</span></div>
    <svg ref={svg} data-testid="viewer" className={'viewer '+p.mode} onWheel={e => zoom(e.deltaY < 0 ? 1.1 : 1/1.1, point(e))}
      onPointerDown={e => { if (e.button !== 0) return; const pos = point(e); const original = imagePoint(pos, view); setDrag({x: original.x, y: original.y, px: pos.x, py: pos.y}); e.currentTarget.setPointerCapture(e.pointerId); }}
      onPointerMove={e => { if (!drag) return; const pos = point(e); if (p.mode === 'pan') { setView(v => ({...v, x: v.x+pos.x-drag.px, y: v.y+pos.y-drag.py})); setDrag({...drag, px: pos.x, py: pos.y}); } else p.onDraft(rectangle(drag, imagePoint(pos, view), p.image.width, p.image.height)); }}
      onPointerUp={e => {setDrag(null); e.currentTarget.releasePointerCapture(e.pointerId);}} onPointerCancel={() => setDrag(null)}>
      <defs><filter id="tint-DAPI"><feColorMatrix type="matrix" values="0.35 0 0 0 0  0 0.48 0 0 0  0 0 1 0 0  0 0 0 1 0"/></filter><filter id="tint-panCK"><feColorMatrix type="matrix" values="1 0 0 0 0  0 0.55 0 0 0  0 0 0.2 0 0  0 0 0 1 0"/></filter><filter id="tint-CD8"><feColorMatrix type="matrix" values="0.2 0 0 0 0  0 1 0 0 0  0 0 0.6 0 0  0 0 0 1 0"/></filter><filter id="tint-CD3"><feColorMatrix type="matrix" values="0.8 0 0 0 0  0 0.2 0 0 0  0 0 0.8 0 0  0 0 0 1 0"/></filter></defs>
      <g transform={`translate(${view.x} ${view.y}) scale(${view.scale})`}>
        <rect width={p.image.width} height={p.image.height} fill="#080d17"/>
        {p.channels.composite && <image href={p.image.previewUrl} width={p.image.width} height={p.image.height}/>}
        {p.runId && p.selectedRoi && ['DAPI','panCK','CD3','CD8'].filter(c => p.channels[c]).map(c => <image key={c} filter={`url(#tint-${c})`} style={{mixBlendMode: 'screen'}} opacity={p.opacity} href={`/api/analysis-runs/${p.runId}/artifacts/${c}`} x={p.selectedRoi!.x} y={p.selectedRoi!.y} width={p.selectedRoi!.width} height={p.selectedRoi!.height}/>)}
        {p.rois.map(r => <g key={r.id} onPointerDown={e => { if(p.mode === 'pan') { e.stopPropagation(); p.onRoi(r); } }}><rect {...{x:r.x,y:r.y,width:r.width,height:r.height}} fill="none" stroke={r.id === p.selectedRoi?.id ? '#f0ddb0' : '#82999c'} strokeWidth={1/view.scale} strokeDasharray={r.id === p.selectedRoi?.id ? undefined : `${4/view.scale}`}/><text x={r.x+3/view.scale} y={r.y-6/view.scale} fill="#f0ddb0" fontSize={11/view.scale}>{r.name}</text></g>)}
        {p.draft && <rect {...p.draft} fill="#f0ddb011" stroke="#fff1bf" strokeWidth={1.5/view.scale} strokeDasharray={`${4/view.scale}`}/>}
        {p.display !== 'off' && p.cells.map(c => <g key={c.cellId} data-testid={`cell-${c.localIndex}`} role="button" aria-label={`细胞 ${c.localIndex}`} tabIndex={0} onKeyDown={e => {if (e.key === 'Enter') p.onCell(c);}} onPointerDown={e => e.stopPropagation()} onClick={() => p.onCell(c)} style={{cursor:'pointer'}}>
          {p.display === 'contour' && <polygon points={c.contour.map(v => v.join(',')).join(' ')} fill={c.cellId === p.selectedCell ? '#ffffff44' : 'transparent'} stroke={c.cellId === p.selectedCell ? '#fff' : colors[c.effectiveLabels]} strokeWidth={c.cellId === p.selectedCell ? 2/view.scale : .8/view.scale}/>}
          <circle cx={c.x} cy={c.y} r={p.display === 'points' ? 2.5/view.scale : 3/view.scale} fill={p.display === 'points' ? colors[c.effectiveLabels] : 'transparent'} stroke={c.cellId === p.selectedCell ? '#fff' : 'none'} strokeWidth={1.5/view.scale}/>
        </g>)}
      </g>
    </svg>
    <div className="viewer-hint">{p.mode === 'roi' ? '拖动绘制矩形，右侧保存区域' : '拖动平移 · 滚轮缩放 · 点击细胞查看测量'}</div>
    <div className="zoom-controls"><button aria-label="缩小" onClick={() => zoom(1/1.25)}><Minus size={14}/></button><span>{Math.round(view.scale*100)}%</span><button aria-label="放大" onClick={() => zoom(1.25)}><Plus size={14}/></button><button aria-label="适应窗口" onClick={fit}><Maximize size={14}/></button></div>
    <div className="scale-bar"><div style={{width: 50/p.image.pixelSizeUm*view.scale}}/>50 µm</div>
  </div>;
}
