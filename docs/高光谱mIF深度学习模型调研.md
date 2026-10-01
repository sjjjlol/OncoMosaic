# 拟接入的高光谱肿瘤细胞分割模型与 Python 服务

> 本文描述项目**预期接入**的算法及接口。当前仓库仍运行 `spectral-mif-sim-v1` 规则模拟器；深度学习模型的网络、权重、训练标签和实测性能尚未交付。下面是目标输出设计，不是当前功能。

## 如何介绍这个模型

可以这样表述：

> 平台拟接入高光谱肿瘤组织细胞分析模型。它以多波段免疫荧光图像为输入，结合光谱信号和细胞形态，定位并分割图像中的细胞对象；对每个对象给出肿瘤细胞候选判断，以及 panCK、CD3、CD8 等标记的阳性/阴性相关分数。Python 服务把模型预测与实际测得的标记强度、坐标和质控信息整理成逐对象结果。平台再统计不同候选细胞的数量、阳性比例、密度和空间关系，并允许研究者回到图像复核。

这里有**两个不同的“阳性”概念**：`panCK⁺`、`CD3⁺`、`CD8⁺`表示相应标记满足阳性判定；“肿瘤细胞候选”表示模型对细胞身份的判断。panCK⁺上皮细胞并不必然是恶性细胞。若希望模型识别肿瘤细胞，训练数据必须包含病理确认的细胞或区域标签，并在目标癌种上验证。当前面板缺少通用细胞膜标记，因此完整细胞轮廓也不能仅凭现有合成数据保证。[SITC mIF 图像分析建议](https://pmc.ncbi.nlm.nih.gov/articles/PMC11749220/)；[Mesmer 细胞分割研究](https://pubmed.ncbi.nlm.nih.gov/34795433/)。

## Python 服务接收什么

浏览器先把两个文件上传给 .NET；.NET 将文件放入共享存储，再调用 Python。Python HTTP 接口收到的是**文件键和参数**，不是把整张图的像素写进 JSON。

Python 有两个接口：`/v1/inspect` 接收 `imageKey`、`assayKey`，返回尺寸、波段、波长、像素尺寸和预览图文件键；`/v1/analyze` 在此基础上接收 ROI、任务 ID 和模型版本，执行分割并返回结果清单。

| 输入 | 含义 |
|---|---|
| `imageKey` | 单视野 `.ome.tiff` 的存储键。图像轴为 `CYX`（波段、高、宽）；仓库样例为 24×256×256。 |
| `assayKey` | `.assay.json` 的存储键，包含波长列表、像素尺寸、染色面板、参考光谱、背景及图像校验值。 |
| `roi` | 要分析的矩形区域，使用原图像素坐标 `{x, y, width, height}`。仅分析阶段需要。 |
| `runId`、`modelVersion` | 任务身份和所使用的算法版本，便于重试与追溯。仅分析阶段需要。 |

例如分析请求（文件键只是示意）：

```json
{
  "imageKey": "images/example/source.ome.tiff",
  "assayKey": "images/example/source.assay.json",
  "runId": "0ee703d4-3f82-4f6e-84f7-9a24a690d197",
  "roi": {"x": 20, "y": 30, "width": 180, "height": 160},
  "modelVersion": "future-spectral-segmentation-v1"
}
```

Python 读取文件、校验元数据、裁取 ROI，并把波段数据整理成模型使用的 `[高, 宽, 波段]` 张量；以上例即 `[160, 180, 24]`。实际归一化、波段选择及模型结构应由交付模型时一并确定。现有服务只接受 `spectral-mif-sim-v1`；上述未来版本号**尚不可调用**。[当前输入校验](../inference/app/model.py) · [当前分析接口](../inference/app/main.py)。

## 模型与 Python 分别输出什么

| 层次 | 预期输出 | 用途 |
|---|---|---|
| **深度学习模型** | 对象实例掩膜：每个核锚定对象有独立 ID 和边界；每对象的肿瘤细胞候选分数、panCK/CD3/CD8 阳性分数，以及不确定/需复核标志。若模型确实训练了组织分区，可另输出有效组织掩膜。 | 回答“对象在哪里、可能是什么、哪些标记可能为阳性”。具体输出头取决于实际交付模型。 |
| **Python 后处理** | 参考光谱分离得到的 DAPI、panCK、CD3、CD8 连续信号图；每对象原图坐标、面积、轮廓、各标记测得强度，并关联模型分数、质量标志。 | 保留可复查的强度数值与模型判断依据。分割模型是否自己生成标记图，应以实际模型为准。 |
| **Python HTTP 响应** | 小型 `manifest`：结果文件键、ROI/尺寸、有效组织像素数、对象数和模型版本。 | .NET 从共享存储读取大图和对象表，校验、保存并供前端查询。 |

一条**目标版**逐对象记录可这样理解（分数和数值仅为格式示意）：

```json
{
  "objectId": 42,
  "centerPx": [120.4, 86.2],
  "maskId": 42,
  "markerIntensity": {"panCK": 0.72, "CD3": 0.04, "CD8": 0.03},
  "markerPositiveScore": {"panCK": 0.94, "CD3": 0.03, "CD8": 0.02},
  "tumorCellCandidateScore": 0.81,
  "candidateLabel": "tumor-cell-candidate",
  "qualityFlag": "review-required"
}
```

这里的 `markerIntensity` 是测量值，`markerPositiveScore` 和 `tumorCellCandidateScore` 是模型预测值；只有经过校准验证，分数才能被解释为概率。最终阳性/阴性标签还必须记录采用的阈值或规则。

`/v1/analyze` 的响应可概括为：

```json
{
  "runId": "0ee703d4-3f82-4f6e-84f7-9a24a690d197",
  "modelVersion": "future-spectral-segmentation-v1",
  "roi": {"x": 20, "y": 30, "width": 180, "height": 160},
  "width": 180,
  "height": 160,
  "markers": [{"name": "DAPI", "displayKey": "runs/.../markers/DAPI.png"}],
  "tissueKey": "runs/.../valid-tissue.png",
  "maskKey": "runs/.../nuclei-mask.npy",
  "overlayKey": "runs/.../nuclei-overlay.png",
  "cellsKey": "runs/.../cells.json",
  "modelPredictionsKey": "runs/.../model-predictions.json",
  "qcKey": "runs/.../qc.json",
  "validTissuePx": 18000,
  "cellCount": 120
}
```

这里的路径和数量只是**结构示意**；实际响应还需要列出全部四张标记图和输入身份信息。`modelPredictionsKey` 是目标版新增字段，当前 .NET 尚未接收和保存它。

为了接入**当前** .NET 服务，Python 仍须写出四张标记显示 PNG、`valid-tissue.png`、`nuclei-mask.npy`、`nuclei-overlay.png`、`cells.json` 和 `qc.json`。当前 `cells.json` 只保存坐标、核面积/轮廓、四项强度和质量标志，没有肿瘤候选分数或标记阳性分数；这些属于需要新增的结果字段。显示 PNG 不能用于反推定量强度。[当前结果生成](../inference/app/model.py) · [.NET 结果校验](../backend/OncoMosaic.Api/AnalysisWorker.cs)。

**平台现状与目标的衔接：** 当前 .NET 根据测得强度和用户阈值判定 panCK⁺、CD3⁺CD8⁺等候选表型，前端展示轮廓、强度、统计并支持复核。目标版若要展示模型的“肿瘤细胞候选”和阳性分数，需要扩展 Python 结果、.NET 保存与分类规则、前端对象详情和导出；不能只替换 Python 中的模型权重。模型分数与阈值规则不一致时，应同时保留两种依据并标记待复核。
