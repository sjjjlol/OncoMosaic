import { describe, it, expect } from 'vitest';
import { imagePoint, rectangle } from './geometry';
describe('ROI coordinates',()=>{
  it('preserves original coordinates after zoom and pan',()=>{
    for(const scale of [.5,1,2,5]){
      const transform={x:123,y:-41,scale};
      const a=imagePoint({x:20*scale+123,y:30*scale-41},transform);
      const b=imagePoint({x:200*scale+123,y:190*scale-41},transform);
      expect(rectangle(a,b,256,256)).toEqual({x:20,y:30,width:180,height:160});
    }
  });
  it('clamps reverse drags to the image bounds',()=>expect(rectangle({x:270,y:300},{x:-5,y:-4},256,256)).toEqual({x:0,y:0,width:256,height:256}));
});
