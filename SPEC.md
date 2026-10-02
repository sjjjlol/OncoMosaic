# OncoMosaic 当前实施规格：光谱 mIF 科研流程模拟

## 目标和边界

端到端演示“OME-TIFF 与 assay 伴随文件导入 → ROI → Python 光谱分析 → 单核对象测量 → 质控 → 区域统计 → 人工复核 → 双 ROI 比较与对象探索 → 导出”。仓库没有患者数据，也没有训练出的医学模型。所有页面、报告均标明合成演示与非诊断用途。本规格描述**当前已实现行为**，不等同真实分析性能或临床产品要求。

## Ki-67 首期实现

已实现五标记局部 ROI 模拟测量、按对象群统计、独立状态复核、历史回放、对象联动、双 ROI 比较及 v2 导出。实际面板、请求、字段、错误码、迁移和限制见 [Ki-67 实施说明](docs/Ki67实施说明.md)，总体目标见 [功能设计](docs/Ki67功能设计.md)。以下原有数据/接口条目描述 **v1 四标记兼容契约**；五标记扩展以实施说明为准。热点和真实医学性能验证尚未实现。

## 数据契约

- 主图像：单视野 `.ome.tiff`，`CYX`、`uint16` 探测器计数或已校准 `float32`、8–64 波段、H/W≤2048、解码像素≤256 MB，上传≤50 MB；OME-XML 内有 `PhysicalSizeX/Y`。
- 伴随 `.assay.json`：≤100 KB，`schema=spectral-mif-research-v1`，`source` 为 `synthetic-no-patient-data` 或 `deidentified-research`；记录样本、切片、视野、assay、扫描仪、染色批次、校准 ID、波长、参考光谱、背景光谱、像素尺寸、强度尺度、图像 SHA-256。
- 固定面板顺序：`DAPI, panCK, CD3, CD8, autofluorescence`。参考光谱为 `[5,B]`，背景为 `[B]`。上传时检查尺寸、有限值、波长递增、参考谱秩与条件数、图像 SHA、像素尺寸一致性。
- `scripts/generate_demo_hsi.py` 固定种子生成 256×256×24 合成 OME-TIFF、assay、`controls/` 单染/未染 OME-TIFF 和独立 `.truth.ome.tiff`（有效组织、区域、核实例）及 `.truth.json`（中心与类别）。真值不进入推理接口。
- 当前是局部视野工作流。尚未实现整切片瓦片、任意扫描仪转换、完整 OME 生态互操作、真实病理性能验证和多用户权限。

## Python 内部接口

`POST /v1/inspect` 输入 `{imageKey, assayKey}`，输出图像宽高、波段、波长、像素尺寸、预览文件键和采集元数据。`POST /v1/analyze` 再输入 `runId`、原图像素坐标中的矩形 ROI、`modelVersion=spectral-mif-sim-v1`。

分析结果清单包含四张标记图键、有效组织图键、核实例掩膜键、核叠加图键、`cells.json` 键、`qc.json` 键、有效组织像素数、细胞数、模型版本和与图像/assay/ROI 绑定的幂等身份。原始结果先写临时目录，再原子发布。同一 `runId` 与输入可重试；不同输入复用同一 ID 被拒绝。传输 JSON 只传文件键和小型元数据，图像/掩膜仍在受控文件卷。

`cells.json` 每行有局部编号、**原图**中心坐标、核面积、核轮廓、DAPI 核内均值、panCK/CD3/CD8 核周近似均值和质量标志。`qc.json` 有有效/排除像素数、饱和像素数、平均光谱残差、参考来源和方法局限。显示 PNG 为 8 位可视化产物，不作为测量源。

模拟分析器依据伴随参考光谱拟合多波段强度，并估计组织有效区与核对象。这是可运行的确定性规则，不应报告临床准确率。四标记面板没有通用膜标记，故不声称完整细胞分割。真实模型必须在同一输入输出语义下独立训练、核验和版本化。

## 后端与统计

API 导入时以暂存目录接收两个文件，由 Python inspect 校验，成功后原子移入图像目录并记录两个 SHA-256。持久任务由单个 .NET 后台 worker 领取，调用 Python；校验结果键、尺寸、掩膜、组织像素数、细胞坐标与强度后，在数据库事务中发布。失败可重试，不产生重复对象。

阳性阈值由 .NET 对保存的测量值计算。自动类别：panCK⁺ 为上皮候选，CD3⁺CD8⁺ 为 T 细胞候选，CD3⁺ 单阳性、阴性、无法判定；panCK 与 CD3/CD8 冲突、CD8 单阳性及触及 ROI 边缘的核归入无法判定。用户修正类别不覆盖原始测量或自动类别，复核版本可回看。

- 可分类对象：核中心在 ROI 内，最终标签不为 `excluded` 或 `unclassified`。
- 面积：有效组织像素数 × `(pixelSizeUm / 1000)²`，单位 mm²。
- 阳性比例：该类可分类对象数 / 全部可分类对象数；分母为空时 `null`。
- 密度：该类对象数 / 有效组织面积，单位 objects/mm²。
- 距离：每个 `cd3-cd8` 候选核中心到最近的**不同** `panck` 候选核中心；没有配对时 `null`，单位 µm。

导出包：`cells.csv`、`roi-summary.csv`、`overlay.png`、`method.json`、`qc.json`。方法记录保留图像和 assay 校验值、元数据、ROI、模型/统计版本、阈值、复核版本与计算口径。

## 多区域比较与结果探索

- 同一图像中选择两个不同 ROI 的成功任务，可分别选择原始 v0、具体复核版本或载入时最新版本。同 ROI 或不同图像的任务返回 400；未成功任务返回 409；不存在任务或版本返回 404。
- 比较以一次 MySQL 可重复读事务解析两侧版本。返回值含确定的 `reviewVersion`，前端导出显式提交这两个版本；后续复核不会改变已载入快照。比较本身不新建分析任务、不重跑 Python，不保存独立的比较实体。
- 并排展示总数/可分类数、panCK/CD3 单阳性/CD3⁺CD8⁺ 候选数、CD3⁺CD8⁺ 比例与密度、有效组织面积及平均最近邻距离。类别筛选、距离区间和通道开关只改变显示，不改变汇总分母和原始测量。
- 最近邻逐起点返回来源/目标对象 ID、任务内编号、原图核中心坐标及距离。仅按所选版本的有效标签在本 ROI 内配对；等距时优先目标 `localIndex`，再按对象 ID。没有目标时返回空配对数组、汇总均值 `null`，不补零。
- 距离分布为六个区间，步长 `max(1, ceil(最大距离/6)) µm`；左闭右开，最后含上界。两侧使用共同最大距离确定区间，每个起点计一次；柱高按各侧最大计数缩放，柱顶显示实际计数，不代表概率或显著性。
- 点击指标/类别或柱形高亮对应对象，其他对象变淡；开启连线时配对目标保持可见。点击对象定位其核中心并查看强度、有效标签及最近目标。对象列表单侧最多显示 100 个，主界面最多 300 个；图上仍高亮全部匹配对象，导出保留全部对象。
- `sameScheme` 检查模型版本、统计算法版本与三项阈值是否相同。不一致仍允许描述性对照，但显示提醒；ROI 重叠也提醒不可合并计数或视作独立样本。比较不做边缘校正、不跨 ROI 配对、不推断细胞功能或疗效。
- 比较 ZIP：`cells-A.csv`、`cells-B.csv`、`comparison-summary.csv`、`nearest-neighbors.csv`、`method.json`。保留两侧对象原始/有效类别、测量、复核版本、配对、输入哈希、ROI、阈值、模型/统计版本、复核修改及口径。不可计算值在 CSV 为空、JSON 为 `null`。当前显示筛选不缩减快照导出，也不改变单 ROI 原有 ZIP 格式。
- 当前仅支持同图像双 ROI 对照；批量调度、跨样本汇总、研究组统计和比较结果收藏未实现。Python 与四标记面板沿用原有模拟实现。

### 新增 HTTP 接口

| 方法与路径 | 请求/返回 |
|---|---|
| `GET /api/analysis-runs/{id}/exploration?reviewVersion=0` | 一次解析版本，返回 `runId/modelVersion/algorithmVersion/roi/summary/cells/nearestNeighbors/reviews`；省略版本时解析最新 |
| `POST /api/images/{id}/comparisons` | 输入见下例，返回 `imageId/imageSha256/assaySha256/pixelSizeUm/a/b/sameScheme/warnings`，A/B 各为 exploration |
| `POST /api/images/{id}/comparisons/export` | 相同输入，返回 ZIP；建议使用已载入的具体版本 |

```json
{
  "a": { "runId": "任务 A 的 UUID", "reviewVersion": 0 },
  "b": { "runId": "任务 B 的 UUID", "reviewVersion": null }
}
```

`reviewVersion: null` 或省略表示请求处理时的最新版本。它不是永久跟随最新的指针；载入后的界面明确展示已解析版本。

## 当前验证与后续工作

已覆盖 OME/assay 契约、畸形输入、光谱结果、质控、幂等重试、物理单位、无法判定/不同对象距离、浏览器导入和复核、API 整链路导出。`docs/VERIFICATION.md` 记录当前运行结果。

接入真实科研数据时，需要真实染色及单染/空白对照、设备与批次校准、病理人员标注的组织和细胞参考集，以及按病例和批次独立的分步骤验证。当前算法对真实图像的组织/细胞识别性能未知；临床用途需另行验证预定用途。[SITC 分析建议](https://jitc.bmj.com/content/13/1/e008875) · [STORMI](https://jitc.bmj.com/content/13/12/e012280) · [OME 数据模型](https://docs.openmicroscopy.org/ome-model/5.5.7/)
