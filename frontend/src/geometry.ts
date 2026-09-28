import type { Rect } from './types';
export function imagePoint(point: {x: number; y: number}, transform: {x: number; y: number; scale: number}) {
  return {x: (point.x-transform.x)/transform.scale, y: (point.y-transform.y)/transform.scale};
}
export function rectangle(a: {x: number; y: number}, b: {x: number; y: number}, width: number, height: number): Rect {
  const x = Math.max(0, Math.min(width, Math.floor(Math.min(a.x, b.x))));
  const y = Math.max(0, Math.min(height, Math.floor(Math.min(a.y, b.y))));
  return {x, y, width: Math.max(0, Math.min(width, Math.ceil(Math.max(a.x, b.x)))-x), height: Math.max(0, Math.min(height, Math.ceil(Math.max(a.y, b.y)))-y)};
}
