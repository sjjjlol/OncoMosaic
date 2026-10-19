# 从需求到交付：OncoMosaic 的 .NET 工程实践

这套文档按设计和开发的先后顺序组织。目标是让你能解释“为什么这样设计”，并能在另一个项目里重复这个思考过程，而不仅是记住四个目录名称。代码是本次已实现的单实例模块化单体；它使用生产项目常见的边界、事务和验证方法，但不意味着已经具备面向公网、临床或多实例运行的上线条件。

如果希望直接结合代码理解四层分工和完整任务链，先读 [源码导读：四层架构与分析任务流转](08-code-walkthrough.md)，再按下面的顺序深入各专题。

## 阅读与实践顺序

| 顺序 | 文档 | 读完应能回答的问题 |
|---|---|---|
| 1 | [目标、业务与约束](01-requirements.md) | 在画架构图前，必须明确什么？ |
| 2 | [分层与类的职责](02-architecture.md) | 一段代码应该放哪，什么时候需要接口？ |
| 3 | [按业务切片开发](03-development.md) | 如何从一个用例写到可运行功能？ |
| 4 | [任务、事务与一致性](04-consistency.md) | 超时、崩溃、重复点击和复核冲突怎么处理？ |
| 5 | [测试与交付](05-testing-operations.md) | 如何证明改动正确，如何启动、迁移和排障？ |
| 6 | [扩展练习与设计取舍](06-evolution.md) | 下一项需求如何落地，什么时候值得增加复杂度？ |
| 7 | [本次验证记录](07-verification.md) | 实际运行了哪些检查，还存在哪些限制？ |
| 8 | [源码导读与任务全链路](08-code-walkthrough.md) | 真实类和方法如何协作完成一次分析？ |

术语从根目录 [CONTEXT.md](../../CONTEXT.md) 开始。关键取舍见 [模块化单体决策](../adr/0001-modular-monolith.md) 与 [发布和复核决策](../adr/0002-publication-and-review.md)。旧学习手册中的路径与架构草图可能描述重构前结构或未来方案；本目录与 `backend/` 实现用于说明这次重构的实际状态，原有学习笔记保留。

## 最短代码阅读路径

先读 `Domain/Model/AnalysisRun.cs` 的状态转换，再看 `Application/Analysis/AnalysisService.cs` 如何建单、`AnalysisProcessor.cs` 如何执行。然后读 `Infrastructure/Persistence/AnalysisRepository.cs` 的事务实现，最后回到 `Api/Endpoints/AnalysisEndpoints.cs` 与 `Api/Program.cs`，理解 HTTP 接口和依赖装配。这里的目录均相对于 `backend/OncoMosaic.*`。

第二条路径是复核：`ReviewPolicy → ReviewService → WithLockedRun → ResultService.Snapshot`。这条路径展示业务规则、用例编排、并发控制和历史查询如何协作。

## 本轮范围

.NET 后端完成职责拆分与可靠性改造。React 和 Python 的现有核心流程、数值语义、v1/v2 导出格式继续作为回归基线，不进行全面重写。没有新增身份认证、微服务、消息中间件或多实例调度。API 契约虽然允许修改，本轮仅在有明确收益的地方改变行为，例如三次尝试上限、启动迁移与演示导入显式开启、错误响应增加 traceId。

## 直接跳转到关键实现

| 学习主题 | 代码入口 |
|---|---|
| 状态与合法转换 | [AnalysisRun](../../backend/OncoMosaic.Domain/Model/AnalysisRun.cs) |
| 分析方案规则 | [AnalysisScheme](../../backend/OncoMosaic.Domain/Model/AnalysisScheme.cs) |
| 复核不变量 | [ReviewPolicy](../../backend/OncoMosaic.Domain/Reviews/ReviewPolicy.cs) |
| 用例编排 | [AnalysisProcessor](../../backend/OncoMosaic.Application/Analysis/AnalysisProcessor.cs) |
| 复核事务中的业务步骤 | [ReviewService](../../backend/OncoMosaic.Application/Reviews/ReviewService.cs) |
| 重建历史快照 | [ResultService](../../backend/OncoMosaic.Application/Results/ResultService.cs) |
| 原子领取与发布 | [AnalysisRepository](../../backend/OncoMosaic.Infrastructure/Persistence/AnalysisRepository.cs) |
| 外部结果契约 | [ManifestValidator](../../backend/OncoMosaic.Infrastructure/Inference/ManifestValidator.cs) |
| 宿主生命周期 | [AnalysisWorker](../../backend/OncoMosaic.Api/Hosting/AnalysisWorker.cs) |
| 数据库并发验证 | [PersistenceIntegrationTests](../../backend/OncoMosaic.Tests/PersistenceIntegrationTests.cs) |
