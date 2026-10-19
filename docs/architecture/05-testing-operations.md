# 05：测试、发布与运行

## 1. 测试层次与各自的证明范围

| 层次 | 入口 | 能证明什么 | 不能证明什么 |
|---|---|---|---|
| 领域与应用单测 | `dotnet test backend/OncoMosaic.slnx` | 状态机、阈值、复核规则、统计、依赖方向 | MySQL 锁行为与容器连通性 |
| MySQL 集成测试 | 设置 `ONCOMOSAIC_TEST_DATABASE` 后运行同一命令 | 并发重试、复核冲突、发布回滚、迁移与恢复 | 多实例调度正确性 |
| Python 测试 | `python -m pytest inference/tests -q` | 既有模拟算法与文件契约 | 医学性能 |
| 前端测试与构建 | `npm test`、`npm run build` | UI 组件、几何逻辑、类型与打包 | 真实后端链路 |
| API 故障验证 | `python scripts/verify_e2e.py --faults` | 真实分析、故障重试、数据库重启、导出 | 所有崩溃时间点 |
| 浏览器与导出 | Playwright 和 `verify_ki67_exports.py` | 交互与实际下载内容的一致性 | 负载容量、安全认证 |

MySQL 测试没有配置连接时会明确显示 skipped，不算数据库验证通过。它们在指定服务器上创建随机名称数据库并在结束时删除，因此连接必须指向隔离测试服务器，账号需要建库和删库权限。测试不会重置应用数据库。

## 2. 本地完整验证

仓库根目录：

```bash
dotnet restore backend/OncoMosaic.slnx --locked-mode
dotnet test backend/OncoMosaic.slnx --no-restore
python -m pytest inference/tests -q
docker compose -f compose.yaml -f compose.test.yaml up --build -d --wait
```

数据库集成测试需要通过环境变量提供 `ONCOMOSAIC_TEST_DATABASE`，包含 `Server=127.0.0.1;Port=13316` 和测试管理员凭据，然后运行 `dotnet test backend/OncoMosaic.slnx`。密码应从本机测试环境取得，不写入代码或提交配置。`compose.test.yaml` 仅为测试开放本地数据库端口。

前端目录执行：

```bash
npm ci
npm test
npm run build
npx playwright install chromium
PW_CHANNEL=chromium npm run test:e2e
```

根目录执行 API 验证及下载检查：

```bash
python scripts/verify_e2e.py --faults
python scripts/verify_comparison.py
python scripts/verify_ki67_exports.py
```

最后一个脚本依赖浏览器测试下载的文件。带 `--faults` 的脚本会停止、启动和重启测试项目中的服务，不能指向正在使用的业务环境。可以设置 `COMPOSE_PROJECT_NAME` 使用独立测试卷；脚本继承该环境变量。

## 3. 普通启动与数据库升级

演示环境仍然使用：

```bash
docker compose up --build -d --wait
```

Compose 显式设置 `Database__AutoMigrate=true` 和 `Demo__Seed=true`，因此演示流程保持方便。直接运行 API 时，这两个开关默认为 false，且必须提供 `ConnectionStrings__Database`。其他配置：`PYTHON_URL` 指向内部推理服务，`STORE_ROOT` 指向 API 与 Python 一致可见的文件根目录，`DEMO_FILE` 只用于演示导入。

独立迁移命令：

```bash
dotnet run --project backend/OncoMosaic.Api -- --migrate
```

执行前设置数据库连接环境变量。这个命令只迁移后退出，不启动 worker，不导入演示数据。实际发布时应先备份，在测试副本验证迁移，再使用迁移账号执行；运行账号原则上不需要 DDL 权限。本轮移动了迁移所在程序集并更新 CLR 名称，没有删除历史迁移或重建业务表。

新增迁移使用 Infrastructure 作为项目，Api 作为启动项目：

```bash
dotnet ef migrations add MeaningfulName --project backend/OncoMosaic.Infrastructure --startup-project backend/OncoMosaic.Api
```

需要预先安装与 EF 主版本匹配的 dotnet-ef 工具，并按设计时工厂要求提供数据库连接。迁移生成后必须审查 SQL 和 ModelSnapshot，再验证已有数据库升级与全新数据库初始化。

## 4. 排查顺序

请求返回错误：先记录响应的 code 和 traceId，在 API 日志中定位请求；业务冲突不是基础设施故障，不要通过无限重试掩盖。

任务失败：查询 status、attempt、error；检查 API worker 日志的 RunId，再检查 inference 日志、文件卷是否一致、manifest 与输入是否匹配。只有 Failed 且未达上限可重试；Succeeded 不允许重试，要更改方案应创建新任务。

任务长期 Running：确认宿主是否仍工作、Python 是否仍执行或被阻塞。单实例重启会恢复残留任务；不要在旧 worker 仍运行时启动第二个实例做“恢复”。

产物缺失：数据库索引不能重新生成已确认结果。先恢复对应卷或核对备份，不要删除数据库记录来掩盖引用断裂。

健康检查 `/api/health` 验证数据库可连接，不表示推理队列延迟正常或所有文件完整。未来上线需要区分 liveness、readiness，并观测队列年龄、失败率、推理时长和存储容量。

## 5. 当前部署限制

- 只支持一个 API/worker 实例；API 与 Python 依赖共享文件卷。
- 无账号、认证、授权和可信操作者审计；默认 Web 仅绑定本机回环地址。
- 没有队列容量限制、历史数据分页或孤立文件自动回收；大规模数据需额外设计。
- 没有备份恢复自动化和容量基准；`docker compose down` 保留卷，但这不是备份。
- 仍使用原有 ImageSharp 3.1.12，包审计存在已知告警；升级涉及新的许可与发布要求，需要明确选择合规升级或替代实现。本轮只更新了存在告警的两个加密传递依赖。
- 编译警告按错误处理，但 NU1902/NU1903 仍显示为告警，以便保留现有演示构建。真实发布流水线应完成漏洞处置后收紧该例外，不能把构建成功视为漏洞审核通过。

ImageSharp 的具体告警可通过 `dotnet list backend/OncoMosaic.slnx package --vulnerable --include-transitive` 复核。相关维护方记录包括 [ImageSharp TIFF 编码问题](https://github.com/SixLabors/ImageSharp/security/advisories/GHSA-jjfr-hcj7-qf5w)。依赖处置必须结合实际用途与许可证，不应只为了得到绿色日志而关闭审计。
