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
