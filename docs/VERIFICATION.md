# 本地验收记录

验证日期：2026-09-28。环境：macOS ARM64、Docker Desktop；服务运行于 Linux 容器。数据库为 MySQL 8.4.8。所有图像均为项目生成的合成数据，无患者来源。

## 结果

| 验证 | 实际结果 |
|---|---|
| `docker compose up --build -d --wait --wait-timeout 180` | 四个服务启动成功；MySQL、Python、API 健康检查通过 |
| Python 契约测试 | 22 通过，覆盖确定性、坐标、坏文件、波长、大小/形状、路径与并发重试 |
| .NET 测试 | 5 通过，覆盖 ROI、状态迁移、物理单位、阈值、空集合和复核隔离 |
| 前端单元测试 | 3 通过，覆盖缩放/平移后的 ROI 坐标、边界裁剪、细胞选择和显示/测量隔离 |
| 前端生产构建 | TypeScript 检查与 Vite 构建通过 |
| 前端依赖检查 | `npm audit`：0 vulnerabilities |
| API 整链路与故障恢复 | `python3 scripts/verify_e2e.py --faults` 通过 |
| Chrome 实际交互 | Playwright 完整流程 1 通过，无页面 JavaScript 异常 |
| 1366×768 布局 | 查看器、指标和侧栏不相互遮挡，长侧栏可滚动 |
| 页面交付 | 已在 Codex 内置浏览器打开本地工作空间并确认持久复核结果 |

## API 实测

选择固定样例、ROI `(20,30,180,160)`、阈值 panCK/CD8 均为 `0.35`：

- 总核数 69；panCK 阳性 51；CD8 阳性 20；双阳性 15。
- ROI 面积 `0.0072 mm²`，使用原图坐标和 `0.5 µm/px` 换算。
- v1 排除首个核后，有效核数 68，panCK 阳性 50；v0 仍保持原始的 69/51。
- 对同一细胞再次修正生成 v2，v1 仍可独立导出。
- 每个版本的 CSV 行数、有效核数、JSON 汇总均与该版本 API 一致；复核前后的 PNG 内容不同。
- 把两个阈值都改成 1 后，新 run 的阳性数均为 0，最近邻为 `null`；旧 run 保留。
- 关闭 Python：任务变为 Failed，记录错误，结果接口拒绝读取。
- 恢复 Python 并重试同一 runId：attempt=2，69 个核，无重复 localIndex。
- 重启 MySQL 与 API：图像数不变，复核历史和历史导出保持一致；演示图像未重复导入。
- 上传坏 ZIP：422 且没有图像记录；另行上传正确 NPZ：201，元数据及图像列表持久化成功。

机器可读结果见 [api-verification.json](api-verification.json)。Python 容器日志确认真实的 `POST /v1/inspect` 和 `POST /v1/analyze` 均有 200 响应。前端未内置任何细胞结果数组。

## 浏览器实测

测试通过页面导入第二份样例，在缩放后绘制并保存 `170×138 px` 的 ROI。该 ROI 与 API 测试 ROI 不同，因此得到 61 个核；复核排除 1 个后有效核数 60。检查了通道开关不改变统计、点击对象详情、v0/v1 切换、下载 ZIP 与刷新恢复。

![1366×900 实际浏览器截图](browser-workflow.png)

![1366×768 实际浏览器截图](browser-1366x768.png)

## 验收范围

这是工程闭环验收，不是医学性能验证。未进行真实患者数据验证、真实模型准确率评估或多实例并发部署验收。支持的输入是 SPEC 规定的 NPZ，不包含真实扫描仪格式适配器。

复现命令、测试环境要求及未来扩展见 [README](../README.md)。
