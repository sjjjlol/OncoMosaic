# OncoMosaic

多重免疫荧光（mIF）光谱图像的**科研流程模拟平台**。它导入 OME-TIFF 局部视野和染色/校准伴随文件，完成 ROI 圈选、参考光谱拟合、有效组织与核对象检测、四/五标记测量、规则分类、质控、人工复核、双 ROI 对比、对象联动探索和版本化导出。

**仓库数据全部由程序合成，没有患者来源。`spectral-mif-sim-v1/v2` 是确定性模拟分析器，不是训练好的医学模型；结果没有经过医学性能验证，不用于诊断。** 核周测量区不等于完整细胞边界；panCK⁺ 和 CD3⁺CD8⁺ 只表示候选表型。

![当前浏览器工作流](docs/browser-workflow.png)

## 从零理解这个项目

没有医学背景，可以先读 [平台零基础入门](docs/平台入门：写给没有医学背景的读者.md)，从一个研究场景了解为什么做这个平台、需要哪些医学常识，以及理想功能怎样帮助研究者。

建议先读 [医学知识与理想算法功能](docs/guide/02-医学概念与算法边界.md)：从平台需要理解的概念讲起，说明理想分析系统所需的数据、应实现的功能、输入输出和验收目标。对接浏览器 API 或 Python 服务时，读 [关键接口设计与流程](docs/关键接口设计与流程.md) 和 [输入输出与算法流程](docs/guide/04-实现原理与流程图.md)；完整阅读路线见 [学习手册](docs/PROJECT_GUIDE.md)。

准备项目答辩或面试，可阅读 [理想平台介绍与 40 个追问](docs/guide/06-面试话术与追问.md)，并结合 [平台架构与数据模型](docs/guide/01-项目架构与数据模型.md) 理解各模块的分工。面试材料按目标方案组织，实际成果以交付记录为准。

## Ki-67 功能

平台已加入 Ki-67 核内模拟测量，在上皮与 T 细胞候选身份之外记录独立的增殖标记状态，支持按对象群计算阳性比例、回图复核及固定版本比较。完整方案见 [Ki-67 功能设计](docs/Ki67功能设计.md)，包括统计分母、质控、热点分析、接口扩展和分阶段验收。当前已实现首期 ROI 统计、独立复核、对象联动与 v2 导出；热点等后续目标尚未实现。操作和实际 API 见 [Ki-67 实施说明](docs/Ki67实施说明.md)。

## 运行

需要 Docker Engine 与 Compose v2：

```bash
docker compose up --build -d
```

打开 [http://localhost:8088](http://localhost:8088)。首次启动会迁移 MySQL 并导入 `data/sample-ki67.ome.tiff` 与对应 `.assay.json`。数据库和分析文件保存在 Docker 卷中；`docker compose down` 不删除它们。

API Dockerfile 在 Apple Silicon 虚拟化环境关闭 .NET Arm64 SVE 指令路径，以避开 SDK 构建时可能出现的 `Illegal instruction`；本地重建已验证。背景见 [.NET runtime 问题记录](https://github.com/dotnet/runtime/issues/122608)。

如果机器上留有旧版 NPZ 演示数据，新版页面只列出带新版 assay 文件的图像；**不会自动删除旧数据库记录或卷**。需要全新演示环境时，可先备份，再由使用者明确执行 `docker compose down -v`；这会删除本项目的数据库和结果卷。

## 输入

页面导入时提交两个文件：

1. `*.ome.tiff`：单视野 OME-TIFF，`uint16` 探测器计数或已校准 `float32`、`CYX`（波段×高×宽），8–64 波段，尺寸不超过 2048×2048，解码后不超过 256 MB，上传不超过 50 MB；OME-XML 必须带 X/Y 像素物理尺寸。
2. `*.assay.json`：不超过 100 KB，包含样本/切片/视野 ID、assay/扫描仪/染色批次/校准 ID、各波段波长、DAPI/panCK/CD3/CD8、可选 Ki67 及自发荧光参考光谱、背景光谱、像素尺寸和图像 SHA-256。导入时校验它与图像匹配。

样例是 uint16、256×256、24 波段、0.5 µm/px 的合成光谱 mIF 图像。`data/controls/` 和 `data/controls-ki67/` 提供模拟单染和未染对照 OME-TIFF；`data/sample-spectral-mif.truth.ome.tiff` 与 `.truth.json` 是**独立的生成真值**，不作为分析输入。assay 文件中的参考光谱与模拟对照由同一生成参数构造。当前支持四标记 v1 与五标记 v2 面板和局部视野，不是通用扫描仪适配器。

重新生成五标记演示数据（原四标记样例保留）：

```bash
.venv/bin/python scripts/generate_demo_hsi.py --ki67 --output data/sample-ki67.ome.tiff
```

## 分析与输出

1. 选择默认图像或上传 OME-TIFF 和 assay 文件，圈选矩形 ROI。
2. 设置 panCK、CD3、CD8 和已检测 Ki-67 的 0–1 演示阈值，启动持久分析任务。
3. Python 根据 assay 参考光谱拟合面板的四/五种标记及自发荧光；从自发荧光和饱和像素估计有效组织，从 DAPI 检测核。panCK/CD3/CD8 在核加 3 px 的近似区域测量，排除相邻核。Ki-67 在核内测量，保存浮点定量图。
4. 输出四/五张标记 PNG、有效组织 PNG、核实例 NPY、`cells.json`、`qc.json` 和叠加图。原始强度用于统计；显示 PNG 不用于反推数值。
5. .NET 保存逐对象测量和有效组织像素数，并依据阈值生成 `panck`、`cd3-cd8`、`cd3`、`negative` 或 `unclassified`；裁剪边缘和冲突信号归入无法判定。人工可复核或排除，历史版本可回看。
6. 密度分母是 **ROI 内有效组织面积**，阳性比例分母是可分类且未排除的对象数。空间距离从 CD3⁺CD8⁺ 候选对象到最近的**不同** panCK⁺ 候选对象；没有可计算配对时为 `null`。
7. v1 导出 ZIP 包含 `cells.csv`、`roi-summary.csv`、`overlay.png`、`method.json`、`qc.json`；方法记录保存图像与 assay 校验值、版本、阈值、面积和口径。

质量标志 `ok` 只表示未触发当前规则，不意味着医学质量合格。有效组织和表型均为模拟规则结果。真实科研使用仍需真实染色/扫描数据、参考标注、批次质控和独立验证；临床用途另需针对预定用途验证。[SITC 图像分析建议](https://jitc.bmj.com/content/13/1/e008875) · [STORMI 报告标准](https://jitc.bmj.com/content/13/12/e012280)

Ki-67 独立于身份类别，按选定对象群计算 P/(P+N)，无法判定和未检测单列，显示覆盖率；零分母为空。页面使用 v2 导出，新增 Ki-67 对象/汇总 CSV、方法与可用定量产物。

## 多区域比较与结果探索

在同一图像中完成两个不同 ROI 的分析后，点击结果区的 **比较区域**，选择两侧分析记录与复核版本，再点击 **比较所选区域**。可并排查看数量、比例、有效组织密度与最近邻距离，点击指标或距离柱形回到对应对象及配对连线。单区域结果也支持相同的筛选与连线。

比较载入后固定两侧版本，导出使用该快照的版本；后续复核后重新载入才能看到变化。阈值或算法版本不同、区域重叠时会提示解释限制。导出 ZIP 包含两侧对象表、比较汇总、逐起点最近邻表及方法记录。详细操作与读图案例见 [第 03 章](docs/guide/03-核心功能与截图演示.md#7-多区域比较与对象联动探索)。

当前每次比较同一图像的两个 ROI；尚未实现跨病例汇总、组间检验或批量任务。Python 保持确定性模拟分析，新增面板为 DAPI、panCK、CD3、CD8、Ki-67；四标记输入和历史结果继续兼容。

## 工程结构

- `frontend/`：React 查看器、导入、阈值、对象探索、双 ROI 对比、复核和导出。
- `backend/OncoMosaic.Api/`：ASP.NET Core API、MySQL、持久任务、结果校验与统计。
- `inference/`：FastAPI 的 OME-TIFF 输入校验和确定性光谱 mIF 模拟分析。
- `scripts/generate_demo_hsi.py`：图像、伴随文件、对照和独立真值生成器。
- `docs/`：架构、医学边界和验收记录。

## 验证

```bash
.venv/bin/python -m pytest inference/tests -q
dotnet test backend/OncoMosaic.Tests
cd frontend && npm test && npm run build && npm run test:e2e
cd ..
python3 scripts/verify_e2e.py
python3 scripts/verify_comparison.py
```

浏览器测试会更新 `docs/browser-workflow.png`；API 集成脚本会更新 `docs/api-verification.json`；区域比较脚本会更新 `docs/comparison-verification.json`，浏览器比较测试生成 `docs/roi-comparison.png` 与 `docs/result-exploration.png`。这些是工程行为证据，**不是医学准确率证据**。完整输入、输出、限制见 [SPEC.md](SPEC.md)，学习材料见 [项目手册](docs/PROJECT_GUIDE.md)。
