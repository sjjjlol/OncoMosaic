> 进阶参考：2026-10-02 原详细版。首次学习请先阅读[小白速学路线](../00-阅读指南与学习路线.md)。

# FHIR：资源模型、API 与互操作约束

本章固定使用 **FHIR R4 4.0.1** 教学。并非声称它是最新版本或 MOSAIQ 的实际接口版本。FHIR 全称 Fast Healthcare Interoperability Resources。

## 1. 从普通 REST 经验走到 FHIR

普通 API 常由团队自行定义 DTO；FHIR 提供通用资源模型、数据类型、引用和约束机制。返回一个带患者信息的 JSON，不足以证明符合 FHIR。FHIR 也不要求所有场景都用 REST。[R4 概览](https://hl7.org/fhir/R4/overview.html)

| 学习资源 | 主要理解 |
|---|---|
| Patient | 患者人口学与管理信息 |
| Encounter | 就诊上下文；不是预约的同义词 |
| Appointment | 预约安排；预约成功不等于治疗已执行 |
| Observation | 一项观察/测量结果 |
| DiagnosticReport | 报告上下文及其相关结果 |
| ServiceRequest | 服务请求/医嘱相关意图 |
| ImagingStudy | 影像检查信息及系列/实例相关描述，不能等同于全部影像像素 |
| DocumentReference | 文档引用及元数据 |

这些是入门资源用途，实际工作流可组合多个资源。诊断资源关系见[诊断模块](https://hl7.org/fhir/R4/diagnostics-module.html)；Patient 定义见[Patient](https://hl7.org/fhir/R4/patient.html)。预约与就诊可分别查[Appointment](https://hl7.org/fhir/R4/appointment.html)、[Encounter](https://hl7.org/fhir/R4/encounter.html)。

## 2. 一个 Patient 资源，三个身份概念

以下是虚构的 R4 最小学习示例，未声称符合某个医院 Profile。

```json
{
  "resourceType": "Patient",
  "id": "patient-001",
  "meta": { "versionId": "7" },
  "identifier": [
    {
      "system": "https://hospital-a.example/identifiers/mrn",
      "value": "P0001"
    }
  ],
  "active": true,
  "name": [{ "family": "TEST", "given": ["PATIENT"] }],
  "gender": "unknown",
  "birthDate": "1980-01-01"
}
```

| 字段 | 意义 | 不要这样理解 |
|---|---|---|
| id | 此服务器上此资源类型的逻辑 ID | 不是跨服务器通用的患者身份 |
| identifier.system + value | 业务标识及其命名空间 | system 是标识域 URI，不要求它是可查询的网页 |
| meta.versionId | 服务器维护的资源版本标识 | 不保证是数字，也不能当作跨系统可比较的业务序号 |

所以 `/Patient/patient-001` 是资源地址；业务病历号是域＋P0001。两台服务器恰好都有 Patient/patient-001，不代表同一个患者。[资源身份](https://hl7.org/fhir/R4/resource.html)、[Patient](https://hl7.org/fhir/R4/patient.html)、[Identifier 数据类型](https://hl7.org/fhir/R4/datatypes.html#Identifier)

例如 Observation 的 `subject.reference` 可指向 `Patient/patient-001`，表示与另一个资源的关系，而不是把整份患者记录复制进结果。引用的目标、权限与是否存在需要验证。[资源引用](https://hl7.org/fhir/R4/references.html)

## 3. REST 请求：读懂即可，不盲目请求公网

下面是本地/测试环境请求形态，不是可直接访问的 Elekta 地址。是否支持每种操作由服务器能力决定。

```http
GET /fhir/metadata
Accept: application/fhir+json

GET /fhir/Patient/patient-001
Accept: application/fhir+json

GET /fhir/Patient?identifier=https%3A%2F%2Fhospital-a.example%2Fidentifiers%2Fmrn%7CP0001
Accept: application/fhir+json
```

`/metadata` 返回能力声明。按逻辑 ID 读取与按业务标识搜索不同；搜索通常返回 `Bundle`，不是直接返回 Patient。搜索零条不一定是患者不存在，还需考虑权限、索引和查询支持；多条命中不能随意选第一条。分页应按返回的 `next` 链接处理，不能只读第一页。[搜索](https://hl7.org/fhir/R4/search.html)

写操作需要结合权限、约束和服务器能力。R4 的 POST 创建通常由服务器分配逻辑 ID；对支持版本感知更新的服务器，可以从响应 ETag 获取版本并用 `If-Match: W/"7"` 进行更新，版本冲突通常返回 412。不要把 PUT 理解为只修改传入的几个字段。[HTTP 交互](https://hl7.org/fhir/R4/http.html)

## 4. 为什么模型相同，还是可能不互通

| 概念 | 解决什么问题 | 接口前应确认 |
|---|---|---|
| Profile / StructureDefinition | 在基础资源上规定更具体约束 | 必填项、基数、扩展、术语绑定、规则 |
| Implementation Guide（IG） | 面向某个应用场景组织实施要求 | 适用地区、版本、工作流与依赖包 |
| CapabilityStatement | 声明实现提供的交互能力 | 支持的资源、操作、搜索和 Profile 等 |
| Coding / CodeableConcept | 表达带代码系统的概念 | system、code、display 和对应值集 |
| Extension | 用标准扩展机制表达额外信息 | URL、定义、类型及使用规则；不能任意塞字段 |

定义见[R4 一致性模块](https://hl7.org/fhir/R4/conformance-module.html)、[Profile](https://hl7.org/fhir/R4/profiling.html)、[CapabilityStatement](https://hl7.org/fhir/R4/capabilitystatement.html)、[扩展](https://hl7.org/fhir/R4/extensibility.html)。

`meta.profile` 声明资源声称符合某 Profile，不是“已验证通过”的证据。JSON 语法通过、基础模型通过、Profile/术语通过、引用有效、业务允许是不同检查层次。实施中需要分别产生结果。

## 5. Bundle、错误与事务

| 类型/对象 | 核心区别 |
|---|---|
| searchset Bundle | 搜索结果集合，关注分页和匹配项 |
| transaction Bundle | 一组操作作为整体成功或失败，按标准执行原子处理 |
| batch Bundle | 每个 entry 独立处理，可能部分成功 |
| OperationOutcome | 以结构化 issue 表达错误/警告等信息；仍要看 HTTP 状态和上下文 |

**典型追问：**“FHIR batch 返回 200，是不是全部成功？”答：检查每个 entry 的响应状态和错误详情，外层成功不能代替逐项成功。一个 transaction 的原子性也不能自动覆盖服务器之外的消息发送、设备动作或其他远程系统。[Bundle](https://hl7.org/fhir/R4/bundle.html)、[HTTP](https://hl7.org/fhir/R4/http.html)、[OperationOutcome](https://hl7.org/fhir/R4/operationoutcome.html)

## 6. 超时、幂等与安全

如果 POST 创建成功但响应丢失，直接再 POST 可能重复创建。应先确认服务器是否支持条件创建、业务标识查询及其他约定机制；不能假定任意 FHIR 服务器接受自定义 Idempotency-Key。条件创建基于约定搜索条件，也需要处理多条匹配和并发场景。[HTTP 条件交互](https://hl7.org/fhir/R4/http.html)

FHIR 不是一套开箱即用的安全实现。生产接口仍需身份认证、授权、传输保护、审计与数据最小化；OAuth/SMART 等机制是否采用需按项目确认。资源搜索、引用读取和日志也不能绕过权限。[FHIR 安全](https://hl7.org/fhir/R4/security.html)

学习设计建议：日志优先记录关联 ID、资源类型、受控内部标识、状态和错误分类；原始敏感载荷单独受控保存。不要把患者内容直接打印到普通应用日志或提交到仓库。

## 7. Java 开发者怎样迁移已有知识

HTTP 客户端、JSON、异常处理、事务与乐观锁是已有基础。新增的学习点是 Resource/Reference、Profile/IG、术语与能力协商。实现时优先使用成熟 FHIR 库并核对版本，避免用普通 DTO 自己拼出“看起来像 FHIR”的接口。

**自测：**id 与 identifier 有什么不同？为什么资源通过校验还不能保证临床业务正确？什么时候选 transaction，什么时候选 batch？如何防止旧版本更新覆盖新版本？
