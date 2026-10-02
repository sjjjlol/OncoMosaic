import {test,expect} from '@playwright/test';
import path from 'node:path';
import fs from 'node:fs';

test('two ROI snapshots → linked distances → review versions → pinned comparison export',async({page,request})=>{
  test.setTimeout(120000);
  const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));
  const projects=await (await request.get('/api/projects')).json();
  const project=projects[0];
  const imported=await request.post(`/api/projects/${project.id}/images`,{multipart:{
    file:{name:'comparison.ome.tiff',mimeType:'application/octet-stream',buffer:fs.readFileSync(path.resolve('../data/sample-spectral-mif.ome.tiff'))},
    assay:{name:'comparison.assay.json',mimeType:'application/json',buffer:fs.readFileSync(path.resolve('../data/sample-spectral-mif.assay.json'))},
    name:`区域比较验收 ${Date.now()}`,
  }});
  expect(imported.status()).toBe(201);const image=await imported.json();
  const rois=[];
  for(const [name,y] of [['上部观察区域 A',0],['下部观察区域 B',128]] as const){
    const response=await request.post(`/api/images/${image.id}/rois`,{data:{name,x:0,y,width:256,height:128,regionTag:'other'}});
    expect(response.status()).toBe(201);rois.push(await response.json());
  }
  const runs=[];
  for(const roi of rois){
    const response=await request.post('/api/analysis-runs',{data:{imageId:image.id,roiId:roi.id,modelVersion:'spectral-mif-sim-v1',thresholds:{panck:.35,cd3:.35,cd8:.35}}});
    expect(response.status()).toBe(202);const run=await response.json();
    await expect.poll(async()=> (await (await request.get(`/api/analysis-runs/${run.runId}`)).json()).status,{timeout:45000}).toBe('Succeeded');runs.push(run);
  }
  await page.addInitScript(id=>localStorage.setItem('imageId',id),image.id);
  await page.goto('/');await expect(page.getByTestId('total-count')).not.toContainText('—');
  await page.getByRole('button',{name:'比较区域',exact:true}).click();
  const dialog=page.getByRole('dialog',{name:'多区域比较'});
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('区域 A分析记录').selectOption(runs[0].runId);
  await dialog.getByLabel('区域 B分析记录').selectOption(runs[1].runId);
  await dialog.getByRole('button',{name:'比较所选区域'}).click();
  await expect(dialog.getByTestId('comparison-scheme')).toContainText('分析方案一致');
  const input={a:{runId:runs[0].runId,reviewVersion:0},b:{runId:runs[1].runId,reviewVersion:0}};
  const result=await (await request.post(`/api/images/${image.id}/comparisons`,{data:input})).json();
  for(const [side,detail] of [['区域 A',result.a],['区域 B',result.b]] as const){
    const viewer=dialog.getByTestId(`${side}-viewer`);
    expect(detail.nearestNeighbors.length).toBeGreaterThan(0);
    await expect(viewer.locator('.neighbor-link')).toHaveCount(detail.nearestNeighbors.length);
    const button=dialog.getByRole('group',{name:`${side}距离分布`}).locator('button:not(:disabled)').first();
    await button.click();const selected=Number((await button.getAttribute('aria-label'))!.match(/，(\d+) 个/)![1]);
    await expect(dialog.getByTestId(`${side}-selected-count`)).toHaveText(`选中 ${selected} / ${detail.cells.length}`);
    await expect(viewer.locator('.neighbor-link')).toHaveCount(selected);
    await expect(viewer.locator('[data-highlighted="true"]')).toHaveCount(selected);
    await dialog.getByLabel(`${side}显示最近邻连线`).uncheck();await expect(viewer.locator('.neighbor-link')).toHaveCount(0);
    await dialog.getByLabel(`${side}显示最近邻连线`).check();
    await dialog.getByLabel(`${side}对象筛选`).selectOption('panck');
    await expect(dialog.getByTestId(`${side}-selected-count`)).toHaveText(`选中 ${detail.summary.counts.panck} / ${detail.cells.length}`);
  }
  // A later review does not silently replace the already loaded comparison.
  const target=result.a.nearestNeighbors[0].targetCellId;
  expect((await request.post(`/api/analysis-runs/${runs[0].runId}/reviews`,{data:{cellId:target,newLabel:'excluded',reason:'比较快照验收'}})).status()).toBe(201);
  await expect(dialog.getByTestId('comparison-scheme')).toContainText('A 复核 v0 / B 复核 v0');
  const exportRequest=page.waitForRequest(r=>new URL(r.url()).pathname.endsWith('/comparisons/export'));
  const downloaded=page.waitForEvent('download');
  await dialog.getByRole('button',{name:'导出比较快照'}).click();
  expect((await exportRequest).postDataJSON()).toEqual(input);
  const download=await downloaded;expect(download.suggestedFilename()).toContain('-v0-v0.zip');
  await download.saveAs(path.resolve('../.local/browser-comparison-export.zip'));
  await dialog.getByRole('button',{name:'比较所选区域'}).click();
  await expect(dialog.getByTestId('comparison-scheme')).toContainText('A 复核 v1 / B 复核 v0');
  await expect(dialog.getByLabel('区域 A复核版本').locator('option[value="1"]')).toHaveCount(1);
  await dialog.getByLabel('区域 A复核版本').selectOption('0');
  await dialog.getByRole('button',{name:'比较所选区域'}).click();
  await expect(dialog.getByTestId('comparison-scheme')).toContainText('A 复核 v0 / B 复核 v0');
  await dialog.getByRole('button',{name:'区域 A 平均最近邻距离',exact:true}).click();
  await page.screenshot({path:path.resolve('../docs/roi-comparison.png'),fullPage:true});
  await dialog.locator('.comparison-scroll').evaluate(element=>element.scrollTo(0,element.scrollHeight));
  await page.screenshot({path:path.resolve('../docs/roi-comparison-exploration.png'),fullPage:true});
  await page.keyboard.press('Escape');await expect(dialog).toHaveCount(0);
  await expect(page.getByRole('button',{name:'比较区域',exact:true})).toBeFocused();
  await page.getByRole('button',{name:'比较区域',exact:true}).click();
  await dialog.getByLabel('区域 A分析记录').selectOption(runs[0].runId);
  await dialog.getByLabel('区域 B分析记录').selectOption(runs[0].runId);
  await expect(dialog.getByRole('button',{name:'比较所选区域'})).toBeDisabled();
  await page.keyboard.press('Escape');
  // Single-region exploration shares the same result contract and preserves its headline counts.
  const total=await page.getByTestId('total-count').textContent();
  await page.getByRole('button',{name:'查看最近邻配对',exact:true}).click();
  await expect(page.getByTestId('viewer').locator('.neighbor-link')).toHaveCount(result.b.nearestNeighbors.length);
  await expect(page.getByTestId('total-count')).toHaveText(total!);
  await page.screenshot({path:path.resolve('../docs/result-exploration.png'),fullPage:true});
  const high=await (await request.post('/api/analysis-runs',{data:{imageId:image.id,roiId:rois[1].id,modelVersion:'spectral-mif-sim-v1',thresholds:{panck:1,cd3:1,cd8:1}}})).json();
  await expect.poll(async()=> (await (await request.get(`/api/analysis-runs/${high.runId}`)).json()).status,{timeout:45000}).toBe('Succeeded');
  await page.reload();await expect(page.getByTestId('total-count')).not.toContainText('—');
  await page.getByRole('button',{name:'比较区域',exact:true}).click();
  await dialog.getByLabel('区域 A分析记录').selectOption(runs[0].runId);
  await dialog.getByLabel('区域 B分析记录').selectOption(high.runId);
  await dialog.getByRole('button',{name:'比较所选区域'}).click();
  await expect(dialog.getByTestId('comparison-scheme')).toContainText('分析方案不一致');
  await expect(dialog.getByRole('button',{name:'区域 B 平均最近邻距离',exact:true})).toHaveText('不可计算');
  await expect(dialog.getByLabel('区域 B结果探索').getByText('没有可配对对象，距离不可计算。')).toBeVisible();
  await expect(dialog.getByTestId('区域 B-viewer').locator('.neighbor-link')).toHaveCount(0);
  expect(errors).toEqual([]);
});
