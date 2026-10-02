# 当前本地验收记录

日期：2026-09-29。macOS 本地、Docker Compose 中的 MySQL/API/Python/网页服务。验收数据全部为合成图像，无患者来源。

| 检查 | 结果 |
|---|---|
| `docker compose up --build -d` | 四服务构建与健康检查通过；既有卷未清空，新数据库迁移和新版样例导入成功 |
| `.venv/bin/python -m pytest inference/tests -q` | 8 通过：OME-TIFF＋assay、光谱结果、质控、畸形输入、HTTP 契约、幂等与路径约束 |
| `dotnet test backend/OncoMosaic.Tests` | 5 通过：ROI、任务、有效面积、不同对象距离、无法判定与阈值 |
| `cd frontend && npm test && npm run build` | 3 个测试通过，TypeScript 与 Vite 生产构建通过 |
| `python3 scripts/verify_e2e.py --faults` | 上传校验、分析、复核、历史导出、故障重试、数据库/API 重启后持久化均通过 |
| `cd frontend && npm run test:e2e` | Playwright 真实浏览器完整操作 1 通过，无页面 JavaScript 异常 |

API 验收使用默认合成 OME-TIFF，ROI `(20,30,180,160)`，panCK/CD3/CD8 阈值均为 `0.35`。结果：总核对象 76、可分类 64、无法判定 12；panCK⁺ 候选 43、CD3⁺CD8⁺ 候选 18。v1 排除一个对象后可分类 63，v0 仍可导出。故障测试中 Python 停止后任务失败，恢复并重试同一 runId 后 `attempt=2`，对象未重复。有效组织面积小于完整矩形的 `0.0072 mm²`，由有效组织像素数换算；精确值见 [机器可读报告](api-verification.json)。

[浏览器截图](browser-workflow.png)和 [1366×768 截图](browser-1366x768.png)由本轮 Playwright 测试生成。它们验证交互，不验证医学正确性。没有真实染色数据、病理参考标注或临床性能测试；对真实研究图像的准确性仍未知。

## 2026-09-29：GitHub Actions 缺失提交文件修复

失败运行：[36586410056](https://github.com/sjjjlol/OncoMosaic/actions/runs/36586410056)，提交 `eebf220`。Python 测试出现 8 个准备阶段错误，均为缺少 `data/sample-spectral-mif.ome.tiff`。本地样例存在，但未被 Git 跟踪，因此 Actions 的干净检出没有这些文件。

同一轮检查还发现新版 EF Core 迁移的主文件与 Designer 文件未纳入 Git；已提交的模型快照不能代替可执行迁移，旧数据库也会掩盖这个问题。

修复内容：

- 将合成主图、assay、对照、真值及两份新版迁移文件加入版本控制。
- 新增 `MigrationTests.CleanDatabaseMigrationIncludesSpectralAnalysisColumns`，生成实际建库 SQL 并检查新版字段，不连接数据库。该测试在缺少迁移的干净副本中失败，补齐迁移后通过。
- 使用仅由 Git 暂存区导出的副本验证，避免读取未跟踪的本地文件。Python 8 项、.NET 6 项、前端 3 项及生产构建通过；独立 Compose 项目使用新建数据库成功启动并完成迁移。

补充验证：独立环境的 API 整链路与 `--faults` 检查通过，包括停止 Python 后失败、重试、数据库/API 重启及历史导出。浏览器流程在本机 Chrome 上复查通过（1 项）。首次浏览器运行期间，本机 ARM64 Docker 中 API 曾以退出码 132（非法指令）重启并返回 502；复跑成功不代表该本地运行时问题已解决。Playwright 自带 Chromium 本机未安装，此次使用已安装 Chrome，不能代替 GitHub 的 Ubuntu/Chromium 运行结果。

### 提交前怎样避免同类问题

`git commit -am` 不会自动加入新文件。提交前运行 `git status --short`，检查样例、迁移等必要文件是否仍为 `??`，并用 `git diff --cached --stat` 检查实际提交范围。IDE 个人配置不需要因此一起加入。

复现 CI 时使用干净检出和独立数据库；直接在含未跟踪文件、已迁移数据库的开发目录运行，不能证明 GitHub 上的检出也完整。新增数据文件必须保持图像与 assay 哈希匹配，不应通过跳过测试或修改校验值来绕过失败。

本节记录本地修复验证，不表示原失败运行已经变绿；修复提交推送后，GitHub Actions 才会产生新的远端结果。

## 2026-10-01：双 ROI 比较与结果探索

本轮新增同图像双 ROI 对照、指标/类别/距离区间到对象的高亮、最近邻连线与目标定位、固定版本比较 ZIP。Python 服务继续使用 `spectral-mif-sim-v1`，面板为 DAPI、panCK、CD3、CD8；没有新增真实医学模型。

| 检查 | 本轮结果 |
|---|---|
| `dotnet test backend/OncoMosaic.Tests` | 9 通过；新增配对身份与等距选择、复核排除重算、跨图像/同 ROI 拒绝、方案/重叠提醒及 ZIP 版本/转义/空值检查 |
| `cd frontend && npm test && npm run build` | 6 通过，TypeScript/Vite 构建通过；新增共同分箱、边界仅计一次、有效标签筛选与连线坐标/目标可见性 |
| `docker compose up --build -d api web` | 本地服务重建成功，数据库卷保留，没有新增迁移 |
| `python3 scripts/verify_comparison.py` | 通过；实际 HTTP 验证配对最小距离、原图坐标、物理单位、复核后目标更新、旧快照不变、固定版本 CSV/JSON、方案不一致、空配对和非法请求 |
| `python3 scripts/verify_e2e.py` | 恢复后复跑通过；旧单 ROI 五文件 ZIP、复核与历史导出保持兼容。本轮未重跑 `--faults` 故障注入 |
| `cd frontend && npm run test:e2e` | 本机 Chrome 2 项通过；原有流程与新增双 ROI 对比、筛选/柱形/连线、最新与历史版本、固定导出、同 ROI 禁用、空距离与方案提醒均通过，页面无 JavaScript 异常 |
| 本地文档链接与 `git diff --check` | 通过 |

合成样例上部 `(0,0,256,128)` 与下部 `(0,128,256,128)`、三项阈值均为 `0.35`：可配对起点分别为 15、13。每个起点到自身 ROI 内最近 panCK 候选核中心配对；不跨 ROI 寻找目标，也没有边缘校正。[比较机器报告](comparison-verification.json)记录本轮任务 ID 和检查项；[单 ROI 机器报告](api-verification.json)已由本轮非故障复验更新。

实际页面截图：[比较汇总](roi-comparison.png)、[比较中的分布与配对](roi-comparison-exploration.png)、[单区域探索](result-exploration.png)。检查了表格、两个查看器、距离柱形、筛选和滚动区域；这些证据说明工程流程，不证明医学准确性。

### 本机运行时的残留问题

首次复验旧单 ROI 导出时，API 容器退出码 `132`，Docker 自动重启，客户端收到一次 `502`；Docker 事件确认 `exitCode=132`、`restartCount=1`。此现象与前一节记录的本地 ARM64 运行时异常一致，但本轮没有查明底层原因，不能声称已经修复。自动恢复后的旧 API 整链路和两项浏览器验收均通过。本轮不将复跑通过解释为长期运行稳定性已验证。

新增比较 API 检查已加入 GitHub Actions 工作流；这里记录的是本地结果，远端 CI 尚未在本轮运行。当前功能限同图像双 ROI 描述性对照；跨病例汇总、显著性检验、批量调度、几何复核和真实数据验证仍未实现。

## 2026-10-02：Ki-67 首期模拟功能

新增五标记 v2 面板，仍使用一次 `/v1/analyze` 调用。已验证核内测量、独立状态、按群分母与覆盖率、历史版本、双 ROI 比较及 v2 导出。旧四标记数据、v1 任务与旧 ZIP/CSV 格式继续兼容。

- Python：17 项通过，新增核内而非核周测量、五标记 HTTP/幂等、参考谱校验、通道质控、饱和核、旧面板未检测。
- .NET：14 项通过，新增 30/90 分母算例、状态复核与排除、群体并集、空值/零值、比较口径、导出、定量图与核内测量一致性及迁移字段检查。
- 前端：8 项通过，TypeScript/Vite 构建通过；Ki-67 比例展示和分母对象筛选通过。
- Playwright：四标记操作、双 ROI 对照和 Ki-67 工作流共 3 项；覆盖独立状态复核、原身份不变、历史回放、版本冲突、通道显示、分子/分母定位、比较和下载。
- `scripts/verify_ki67_exports.py`：读取实际浏览器下载，重算四个群体并与 JSON/CSV 汇总核对，检查历史状态、固定版本、定量产物和 v1 文件集合/列兼容；已加入 CI。
- 原有 `scripts/verify_e2e.py` 与 `scripts/verify_comparison.py` 通过。既有 MySQL 卷升级成功，历史记录保留。本轮未重跑停止服务的 `--faults` 场景。

[Ki-67 截图](ki67-workflow.png) · [机器报告](ki67-verification.json) · [实施说明](Ki67实施说明.md)。完整视野模拟结果为 101 个核，其中上皮候选 60 个；Ki-67 自动阳性 31 个，复核一个阳性为阴性后为 30/60=50%，覆盖率 100%。这些是合成数据的工程结果，不是医学准确率。

浏览器端到端测试发现初版五标记合成数据存在大量探测器饱和。已降低模拟探测器增益并同步参考谱，保持核内系数尺度，保留显式饱和伪影；新增有效上皮/T 细胞阳性及阴性断言。没有通过放宽质控来让样例通过。

本机 Apple Silicon Docker 构建仍偶发退出码 132（已有 .NET 非法指令问题），重试后构建通过；本机直接运行 .NET 测试正常。运行时问题与医学有效性均不由本轮功能验收解决。
