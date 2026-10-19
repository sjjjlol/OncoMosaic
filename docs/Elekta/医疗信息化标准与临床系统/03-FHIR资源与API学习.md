# 03｜FHIR：用熟悉的 JSON 和 API 学医疗信息

这一章适合写过 REST API 的开发者。沿用张明、A 医院和患者号 P0001，示例采用 FHIR R4，地址与数据均为教学虚构。

## 一、如果每家公司都自己设计医疗 JSON，会怎样

假设两个系统表达同一位患者：

```json
{
  "patientNo": "P0001",
  "patientName": "张明"
}
```

```json
{
  "person_code": "P0001",
  "full_name": "张明"
}
```

人能看懂它们很像，程序仍需要专门映射。而且问题不只是字段命名：编号属于哪家医院？姓名只有一个还是可能有多个？预约状态怎么表达？检查结果是什么类型和单位？

当接入很多系统时，每一对都重新协商一套模型，会增加理解和维护成本。

**FHIR 提供了一套标准化的医疗信息模型和交换机制。**你可以从“大家共同约定的医疗数据模型”开始理解，再学习接口。**FHIR 是 HL7 标准家族的一部分**，不是与 HL7 完全无关的另一套体系。

## 二、“资源”是什么意思：把医疗信息拆成不同对象

FHIR 把很多医疗信息按 **Resource（资源）**组织。资源可以先理解为“**有明确含义的一类数据对象**”。

如果你熟悉 Java，可以把 Patient 类比成标准化的患者模型，Appointment 类比成标准化的预约模型。这帮助理解，但并不表示一个资源一定对应一张数据库表。

先学四个资源即可：

| 资源 | 中文理解 | 张明的故事中对应什么 |
|---|---|---|
| Patient | 患者基本及管理信息 | 张明是谁、有什么业务编号 |
| Appointment | 预约安排 | 张明被安排在某个时间接受某项服务 |
| Encounter | 就诊上下文 | 张明某次门诊或其他就诊过程 |
| Observation | 一项观察或测量结果 | 某次记录的体温等信息 |

这里的体温仅用于理解信息表达，与张明的具体治疗方案无关。

**关键点：患者不是预约，预约不是实际就诊或治疗完成，结果也不是患者基本资料。**资源拆开，是为了表达不同事实，并让它们通过明确关系联系起来。

## 三、逐行读一个 Patient JSON

先看这个小例子：

```json
{
  "resourceType": "Patient",
  "id": "patient-001",
  "identifier": [
    {
      "system": "https://hospital-a.example/identifiers/mrn",
      "value": "P0001"
    }
  ],
  "name": [
    {
      "family": "张",
      "given": ["明"]
    }
  ]
}
```

### 1. resourceType：告诉对方“这是什么”

`"resourceType": "Patient"` 表示这份数据按 Patient 资源解释。如果是 Appointment，字段和含义就不同。

你可以类比消息中的类型字段：收到一段 JSON，必须先知道它是哪一种对象。

### 2. id：这个服务里资源的编号

`"id": "patient-001"` 用来标识服务器上的这份 Patient 资源。通过它可以形成资源地址，例如 `Patient/patient-001`。

注意，这只是特定服务器上这个类型资源的逻辑身份。另一台服务器也可以有自己的 patient-001，不能仅凭相同 id 就认定同一个患者。

### 3. identifier：患者在业务中的编号

A 医院认识张明的业务编号是 P0001，放在 `identifier.value`。`system` 表达这个编号属于哪个标识体系。

可以这样读：**“A 医院的病历号体系里，编号是 P0001。”**system 虽然长得像网址，主要是标识域，不要求它提供一个可以打开查询的网页。

`mrn` 常用来表示 Medical Record Number，即病历号。这里的教学 URL 只是帮助你看到“编号需要所属体系”这件事。

### 4. name：姓名也按标准数据结构表达

`family` 表示姓，`given` 表示名。外层是数组，是因为资源模型允许多组姓名信息，例如不同用途的姓名。

现在不用背所有姓名字段。先理解：FHIR 给出的不仅是一个自由字符串，而是有语义的数据类型和结构。

### 5. 最容易混淆的一组区别

```text
Patient/patient-001  → 服务中这份资源的地址
A 医院 + P0001      → 本例中的业务病历号身份
张明                → 人能读的姓名
```

这三种信息分别服务于资源寻址、业务身份、展示，不能互相替代。



### C# 协议 API｜用 Firely SDK 转为 Patient 对象

依赖 Hl7.Fhir.R4 6.6.0，使用 Hl7.Fhir.Model 和 Hl7.Fhir.Serialization。

```csharp
string json = """
{
  "resourceType": "Patient",
  "id": "patient-001",
  "identifier": [
    { "system": "https://hospital-a.example/identifiers/mrn", "value": "P0001" }
  ],
  "name": [{ "family": "张", "given": ["明"] }]
}
""";

Patient patient = FhirJsonDeserializer.DEFAULT.Deserialize<Patient>(json);
Console.WriteLine($"资源 ID：{patient.Id}");
Console.WriteLine($"业务病历号：{patient.Identifier[0].Value}");
Console.WriteLine($"姓名：{patient.Name[0].Family}{patient.Name[0].Given.First()}");
```

Deserialize<Patient> 直接得到标准资源模型，Id、Identifier、Name 是对象属性。输出为 patient-001、P0001、张明。这里的数组第一项仅适合已知样本，真实输入按业务规则选择。

构造资源、序列化，以及 FhirClient 服务器读取/搜索示例见[协议 API 实战](07-CSharp医学协议API实战.md)。


## 四、资源怎样关联：类似主记录和关联记录

假设我们记录张明的一次体温测量。先不写完整资源，只看 Observation 中的这个片段：

```json
{
  "subject": {
    "reference": "Patient/patient-001"
  }
}
```

`subject` 是结果涉及的对象；`reference` 表示引用。这个片段的意思是：“这项结果对应 Patient/patient-001。”

它是讲解引用的局部片段，缺少完整 Observation 所需的其他内容，不是可直接提交的完整资源。

和数据库外键类比很好理解：结果里应有指向患者的关系，而不是每次复制姓名，然后靠姓名回头匹配。实际 FHIR 引用还有其他形式，基础学习先理解关系明确即可。

## 五、API 怎么用：一条读取与一条搜索

### 1. 已知道资源 ID，就按地址读取

```http
GET /fhir/Patient/patient-001
```

你请求的是“这个 FHIR 服务里的 Patient/patient-001”。如果允许访问且资源存在，服务器返回相应资源。

这与普通 REST 的 GET 很接近。真正新增的部分，是返回资源的标准含义和交互规则。

### 2. 只知道病历号，就按业务标识搜索

概念上，你要问的是：“找出 identifier 属于 A 医院病历号体系、值是 P0001 的 Patient。”实际接口使用 FHIR 的标准搜索参数，并按要求编码请求。

搜索结果可能是零条、一条或多条。FHIR 通常用 **Bundle** 组织搜索结果，你可以先把这种搜索 Bundle 理解为“一包结果”。

```text
零条：当前查询条件下没找到，需要查明原因
一条：再核对是否是预期患者
多条：可能存在数据或查询问题，不能随便拿第一条
```

这里不会把复杂的 URL 编码当必背项；需要写接口时再看进阶示例。

## 六、为什么“返回 JSON”不等于“符合 FHIR”

如果一个接口返回 `{"name":"张明"}`，它是合法 JSON，但没按 Patient 资源结构表达，也没有说明业务标识体系等。

可以类比：一个 HTTP 接口返回 200，并不说明返回体符合双方的订单契约。**FHIR 也有自己的模型、数据类型和规则。**普通 JSON 是语法，FHIR 还涉及语义和约束。

### 再认识两个词：先看生活化解释

**Profile（约束规范）：**基础 Patient 模型可以允许某个字段选填，但一个具体实施规范可能要求业务编号必须有。你可以理解为“通用模板之上，这个场景要求怎样填写”。

**CapabilityStatement（能力声明）：**告诉客户端某个服务支持什么。你可以理解为“接口的能力说明书”，例如提供哪些资源、能否写入、支持什么搜索。

两者的区别是：Profile 讲“数据应满足什么要求”，能力声明讲“这个服务提供什么能力”。使用同一个标准的两台服务器，也未必支持一样的资源和操作。

第一次学会这两个问题就够：**具体数据规则是什么？对方实际支持什么？**不用背完整英文对象结构。

## 七、用更新冲突学一个软件关键点

两名工作人员都打开了张明的同一份记录。甲先修改保存，乙仍在旧页面上修改并保存。如果不做冲突检查，乙可能把甲刚修改的内容覆盖掉。

这是你熟悉的并发更新问题，医疗资源也可能遇到。FHIR 接口可以在服务器支持的情况下，利用资源版本和条件更新检查，避免旧版本静默覆盖新版本。

入门回答可以说：“我会核对接口的版本控制能力，更新时检查是否基于旧版本；发生冲突后重新读取并让用户处理差异。”精确的 ETag、If-Match、412 状态码放在进阶版。

## 八、FHIR 与 HL7 v2 的区别，用两句话理解

继续张明故事：

- **HL7 v2 的例子：HIS 发消息，“张明的资料刚更新了，这是这次通知。”**
- **FHIR REST 的例子：一个应用请求 Patient 资源，“请给我这位患者的当前资料。”**

这个对照帮助理解常见用法，但不是严格限制。FHIR 也支持消息等方式，HL7 v2 也有查询机制。面试回答不说“一个只能推、一个只能拉”。

## 九、面试可以这样讲

> 我把 **FHIR** 理解为用标准化**医疗资源**来**组织和交换信息**。例如**患者可以表达成 Patient，预约表达成 Appointment**，不同资源用引用关联。我有 REST 和 JSON 的开发基础，所以会从资源模型、业务编号和资源 ID 的区别入手。实际对接还要确认服务支持的资源、搜索和写入能力，以及具体约束，不能仅因为返回 JSON 就认定符合 FHIR。

## 十、三道小练习与答案

**题 1：Patient/patient-001 与医院的 P0001 有什么不同？**

答案：前者是此服务中资源的地址，后者是医院业务编号，后者还要结合编号体系。

**题 2：预约资源存在，就意味着患者已经完成治疗吗？**

答案：不意味着。预约表达安排，实际执行需要相应记录和工作流。

**题 3：某服务器宣称使用 FHIR，是否能直接假定支持所有资源和写操作？**

答案：不能。先确认它的能力声明、实施规范和授权范围。

## 十一、进阶与来源

首次跳过 transaction/batch、OperationOutcome、扩展、条件创建、搜索分页和 HTTP 版本头。需要时查[原详细版](进阶参考/03-FHIR资源与API学习.md)。

基础定义：[FHIR R4 概览](https://hl7.org/fhir/R4/overview.html)。Patient 示例依据：[Patient](https://hl7.org/fhir/R4/patient.html)及[资源身份](https://hl7.org/fhir/R4/resource.html)。引用：[Reference](https://hl7.org/fhir/R4/references.html)。搜索：[Search](https://hl7.org/fhir/R4/search.html)。约束与能力：[Profile](https://hl7.org/fhir/R4/profiling.html)、[CapabilityStatement](https://hl7.org/fhir/R4/capabilitystatement.html)。预约与就诊区别：[Appointment](https://hl7.org/fhir/R4/appointment.html)、[Encounter](https://hl7.org/fhir/R4/encounter.html)。
