# OncoMosaic

多标记组织图像单细胞定量分析工程原型。提供可实际运行的「导入 → 圈选 ROI → Python 模拟分析 → 测量 → 复核 → 导出」完整流程。

**合成演示数据 / 模拟模型 / 非诊断用途。** 核检测与核周近似测量不等于全细胞分割；panCK、CD8 的组合不代表已经确定的生物学身份。只有标为「肿瘤候选区域」的 ROI 才显示「候选肿瘤相关细胞」。

![浏览器实际操作截图](docs/browser-workflow.png)

## 项目文档

完整的 [项目学习与讲解手册](docs/PROJECT_GUIDE.md) 包含：架构与数据模型、医学概念、带实际截图的核心功能、实现流程图、技术取舍，以及面试介绍与追问。

## 一键运行

需要 Docker Engine / Docker Desktop 和 Compose v2。无需在宿主机安装 .NET、Python、Node 或 MySQL。

```bash
git clone git@github.com:sjjjlol/OncoMosaic.git
cd OncoMosaic
docker compose up --build -d
```

打开 **http://localhost:8088**。首次构建会下载镜像和依赖，启动时等待 MySQL 与 Python 健康检查，然后自动执行 EF Core 迁移并幂等导入演示图像。

```bash
docker compose ps
docker compose logs -f api inference
# 停止服务，保留数据库和文件结果
docker compose down
# 重新启动，已有图像、ROI、任务、细胞和复核仍在
docker compose up -d
```

持久卷为 `oncomosaic_database`、`oncomosaic_artifacts`。`docker compose down -v` 会删除这些数据，只在明确要清空演示环境时使用。

默认仅将网页绑定到本机 `127.0.0.1:8088`；MySQL、API、Python 没有对宿主机开放端口。可在 `.env` 中设置 `WEB_PORT`、`MYSQL_PASSWORD`、`MYSQL_ROOT_PASSWORD`。默认数据库密码仅用于这个本地演示，改变已有数据卷的密码需同时变更数据库账户。

## 演示操作

1. 选择左侧自带的「合成组织 · sample-hsi」。也可创建项目，导入 `data/sample-hsi.npz` 或另一份符合格式的 NPZ。
2. 在顶部选择「矩形 ROI」，拖动圈选；右侧填写区域名称和标签，点击「保存 ROI」。滚轮缩放、平移和适应窗口只改变显示变换。
3. 设置 panCK、CD8 阈值，点击「开始分析」。底部显示排队、运行、完成或失败；实际任务由 .NET 后台服务调用 Python，再把细胞写入 MySQL。
4. 开关合成预览和 DAPI/panCK/CD8 通道、调透明度、切换轮廓/中心点；点击核对象查看原图坐标、强度、类别和边缘标志。
5. 修正类别或排除对象，填写原因，提交复核。每次生成新的版本；底部可切回 v0 自动结果或任意历史复核版本。
6. 「导出当前版本」下载 ZIP：`cells.csv`、`roi-summary.csv`、`overlay.png`、`method.json`。PNG 的类别颜色与指定复核版本一致，被排除对象显示灰色叉号。
7. 改阈值后重新点击分析，生成新的 run；旧任务和旧统计可通过「分析记录」回看。

核周 panCK/CD8 测量区域为核加 3 px 膨胀区域，排除相邻核。接触 ROI 裁剪边缘的核带 `crop-edge` 标记，可人工排除。比例分母为未排除的核数；密度分母为整个 ROI 面积。最近邻从每个 CD8 阳性核中心到最近 panCK 阳性核中心，双阳性对象可匹配自身（距离 0）；任一类缺失时返回 `null`，界面显示「无可计算对象」。

## 架构与目录

```text
React / Vite → nginx /api 反向代理 → ASP.NET Core .NET 10
                                       ├── MySQL 8.4 / EF Core 10
                                       ├── 持久文件卷
                                       └── BackgroundService → Python FastAPI
                                                                   └── 同一文件卷
```

- `frontend/`：React 19 + TypeScript，SVG 原图坐标查看器，自有样式与图标组合。
- `backend/OncoMosaic.Api/`：API、数据库迁移、持久任务、统计、复核与导出。
- `backend/OncoMosaic.Tests/`：坐标、状态迁移、阈值、物理单位、复核隔离测试。
- `inference/`：FastAPI 内部接口、可替换的 `SpectralAdapter` 协议与 `MockSpectralAdapter`。
- `scripts/generate_demo_hsi.py`：固定种子生成合成数据；`data/sample-hsi.npz` 已随仓库提供。
- `scripts/verify_e2e.py`：对运行中的四服务环境执行整链路与故障恢复验证。
- `docs/`：浏览器截图、验收数据和实现说明。
- `SPEC.md`：当前实施规格，已按用户要求把 PostgreSQL 改为 MySQL。

MySQL 使用官方 `MySql.EntityFrameworkCore` 10.0.1，初始迁移在仓库中。应用依赖固定在 `package-lock.json`、`packages.lock.json` 和 Python `requirements.txt` 中；Docker 基础镜像也固定版本。

## 输入与算法契约

只接收 `.npz`，最大 50 MB；必须且只能包含：

| 字段 | 要求 |
|---|---|
| `cube` | `float32[H,W,B]`，`1≤H,W≤2048`，`3≤B≤64`，所有值有限，解压数组≤256 MB |
| `wavelengths_nm` | `float32[B]`，有限且严格递增 |
| `pixel_size_um` | 有限正数标量 |

Python 在加载数组前读取 ZIP 目录与 NPY 头，拒绝超大数组、对象类型、重复/额外字段和错误形状。API 先写隔离临时目录，计算 SHA-256，真实调用 `/v1/inspect`，校验成功后原子发布图像目录；失败不留下数据库图像。

`mock-unmix-v1` 按 `floor(bandIndex × 3 / B)` 固定分组三个标记，组内等权平均后裁剪到 `[0,1]`。16 波段时三组是 `[0:6]`、`[6:11]`、`[11:16]`。DAPI ≥0.32，8 连通核检测，面积 5–400 px²。阈值分类由 .NET 计算，不从显示 PNG 反读强度。

输出包括三张 ROI 标记 PNG、整数实例 `nuclei-mask.npy`、透明核轮廓 PNG、`cells.json`。结果先写临时目录，再原子发布；同一 runId 与输入返回相同结果，不同输入复用 runId 被拒绝。API 校验尺寸、坐标、强度、文件键、掩膜编号和面积后，在一个事务内替换该 run 的结果。数据库唯一约束防止同一 `runId + localIndex` 重复。

单实例后台任务部署：`Queued → Running → Succeeded / Failed`。重试保留 runId，增加 attempt；服务重启时把中断的 Running 任务恢复为 Queued。复核在事务中锁定任务行，串行生成版本号，保留自动类别与原始测量。

## 测试

在项目根目录运行；本地测试需要 Python 3.12+、.NET 10 SDK、Node 22.15+。

```bash
python3 -m venv .venv
.venv/bin/pip install -r inference/requirements.txt
.venv/bin/python -m pytest inference/tests -q

dotnet test backend/OncoMosaic.Tests

cd frontend
npm ci
npm test
npm run build
cd ..

# 对已启动的 Compose 环境执行 API 集成验证
python3 scripts/verify_e2e.py

# 包含关闭 Python、失败重试、重启 MySQL/API 后持久化验证
python3 scripts/verify_e2e.py --faults

# 真正操作浏览器；默认使用已安装的 Google Chrome
cd frontend
npm run test:e2e
# 无 Chrome 时安装 Chromium，然后使用 PW_CHANNEL=chromium
npx playwright install chromium
PW_CHANNEL=chromium npm run test:e2e
```

整链路测试会增加明确命名的测试 ROI、任务和图像。`--faults` 会短暂停止此项目的容器，结束时恢复服务。脚本会更新 `docs/api-verification.json`；浏览器测试会更新 `docs/browser-workflow.png` 和 `docs/browser-1366x768.png`。这些是测试实际生成的证据。首次交付实测结果见 [验收记录](docs/VERIFICATION.md)。

重新生成数据：

```bash
.venv/bin/python scripts/generate_demo_hsi.py --output data/sample-hsi.npz
```

生成或更新迁移：

```bash
dotnet tool restore
dotnet ef migrations add YourMigration --project backend/OncoMosaic.Api
```

## 已实现与后续范围

已实现 F01–F07：项目创建、上传校验、图像预览、矩形 ROI、通道显示、持久分析任务、失败重试、单细胞测量、阈值版本、区域统计、逐细胞复核和历史版本 ZIP 导出。

暂不支持多边形 ROI、真实模型、真实病理性能验证、ENVI/OME-TIFF、DICOM、全切片瓦片、多用户权限、临床诊断。该部署只有一个 API/后台 worker 实例；扩展到多个副本前需引入分布式任务领取与租约。无患者来源的样例不能替代真实数据和人工参考标注验证。

## 参考

工作流参考 [QuPath 多重分析教程](https://qupath.readthedocs.io/en/latest/docs/tutorials/multiplex_analysis.html)，未复制其代码或资源。工程接口参考 [ASP.NET Core 托管服务](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)、[MySQL EF Core 文档](https://dev.mysql.com/doc/connector-net/en/connector-net-entityframework-core.html)、[FastAPI](https://fastapi.tiangolo.com/tutorial/first-steps/) 和 [scikit-image regionprops](https://scikit-image.org/docs/stable/api/skimage.measure.html)。各依赖的许可证由各自项目提供；没有对第三方依赖重新授权。
