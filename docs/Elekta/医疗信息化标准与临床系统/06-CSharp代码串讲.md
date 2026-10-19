# 06｜概念辅助：七段 C# 数据结构小例子

> 医学协议 API 调用请优先看[07｜C# 医学协议 API 实战](07-CSharp医学协议API实战.md)。本篇保留基础数据结构模拟，帮助理解业务区别。

更新：2026-10-07。配合前面三章的小白故事阅读。代码全部使用虚构数据、本地内存与 .NET 自带功能，不需要数据库、真实影像或外部服务。

## 先认识五个 C# 写法

| 写法 | 可以怎样理解 |
|---|---|
| `var` | 由编译器推断变量类型，仍是静态类型 |
| `Dictionary` | 类似 Java 的 Map，按键找值 |
| `HashSet` | 类似 Java 的 Set，保存不重复的项 |
| `$"姓名：{name}"` | 把变量插入字符串 |
| `Console.WriteLine` | 类似 System.out.println |

每个片段都按“代码 → 输出 → 解读 → 改一处观察”组织。先读输出，再解释它体现的业务区别。文档中的片段可分别放在控制台项目里运行；需要 System、System.Collections.Generic，JSON 例子另需 System.Text.Json。本套可运行项目已经包含这些 using。

## 一次运行全部示例

代码目录是 [CSharp示例](CSharp示例/README.md)，主文件是 [Program.cs](CSharp示例/Program.cs)。项目使用本机已有 .NET 10 SDK，不依赖第三方包。

在终端运行：

以下命令在 OncoMosaic 仓库根目录执行。

```bash
dotnet run --project 'docs/Elekta/医疗信息化标准与临床系统/CSharp示例/MedicalConcepts.csproj'
```

程序按顺序打印七组结果。每段外层的 `{ }` 只是隔开变量作用域，方便把不同示例放在同一文件；你无需先学 Web 框架、异步或 ORM。

## 示例 01｜患者身份：相同编号不一定是同一个人

```csharp
// 键包含医院和患者号；值是展示姓名。
var patients = new Dictionary<(string Hospital, string PatientId), string>();
patients[("HOSP_A", "P0001")] = "张明";
patients[("HOSP_B", "P0001")] = "李华";

Console.WriteLine(patients[("HOSP_A", "P0001")]);
Console.WriteLine(patients[("HOSP_B", "P0001")]);
Console.WriteLine($"患者记录数：{patients.Count}");
```

**预期输出：**

```text
张明
李华
患者记录数：2
```

**为什么这样写：**

`Dictionary` 类似 Java 的 `Map`。`(string Hospital, string PatientId)` 是一个二元组，表示把两个值一起当作键。这里 `(HOSP_A, P0001)` 和 `(HOSP_B, P0001)` 是两个不同的键，所以两位患者不会互相覆盖。

只用 `Dictionary<string, string>` 并把 P0001 当键时，第二次赋值会覆盖第一条。这说明**标识的范围是业务信息的一部分**。真实业务的域可能是签发机构或其他标识体系，不一定等于发送医院；本例用医院便于理解。

**改一处观察：**把第二条记录的 HOSP_B 改成 HOSP_A，看看记录数与第一条查询结果为什么变化。

## 示例 02｜DICOM 元数据：图像之外，还要知道属于谁、哪次检查

```csharp
// 手工模拟一份对象的元数据，尚未读取真正的 .dcm 文件。
var metadata = new Dictionary<string, string>
{
    ["PatientID"] = "P0001",
    ["StudyInstanceUID"] = "2.25.1001",
    ["SeriesInstanceUID"] = "2.25.1002",
    ["SOPInstanceUID"] = "2.25.1003",
    ["Modality"] = "CT"
};

Console.WriteLine($"患者号：{metadata["PatientID"]}");
Console.WriteLine($"检查 UID：{metadata["StudyInstanceUID"]}");
Console.WriteLine($"序列 UID：{metadata["SeriesInstanceUID"]}");
Console.WriteLine($"对象 UID：{metadata["SOPInstanceUID"]}");
Console.WriteLine($"设备类型：{metadata["Modality"]}");
```

**预期输出：**

```text
患者号：P0001
检查 UID：2.25.1001
序列 UID：2.25.1002
对象 UID：2.25.1003
设备类型：CT
```

**为什么这样写：**

这个字典只是把已经讲过的字段变成 C# 数据。Study、Series、SOP Instance 分别是检查、序列、具体对象的标识；它们不是患者号。`Modality` 在这里表示成像设备/方式，本例 CT。

`["PatientID"] = "P0001"` 是字典初始化写法。`$"患者号：{...}"` 是字符串插值，类似 Java 中拼接变量形成输出。

**本例想让你看到影像数据的上下文，未实现 DICOM 解析或像素显示。**实际文件里还有标准 Tag、数据类型、像素和其他信息，读取时使用适合项目的 DICOM 库。

**改一处观察：**只改变 SOPInstanceUID，保持检查和序列 UID 相同：它表示同一次检查、同一序列下的另一个对象，不是换了一个患者。

## 示例 03｜DICOM 关联：圈线只能按其明确引用找到对应资料

```csharp
// 只模拟某份轮廓数据里的一条影像实例引用。
string referencedImageUid = "2.25.1003";
var receivedImages = new HashSet<string>();

Console.WriteLine($"引用的影像已到达：{receivedImages.Contains(referencedImageUid)}");

// 随后收到这份影像。
receivedImages.Add("2.25.1003");
Console.WriteLine($"引用的影像已到达：{receivedImages.Contains(referencedImageUid)}");
```

**预期输出：**

```text
引用的影像已到达：False
引用的影像已到达：True
```

**为什么这样写：**

`HashSet` 类似 Java 的 `Set`，这里保存已收到的影像实例 UID。`Contains` 问的是“明确引用的那份影像是否存在”。第一次还没有资料，所以 False；补齐后是 True。

代码没有按姓名查影像，也没有拿“最后收到的图”当正确图。它体现的是**对象关系要明确**。真实 RTSTRUCT 可以有多项引用，不只有本例一个字符串。

True 只代表这条教学引用的目标存在，不能证明空间一致、所有资料齐全或计划已批准。

**改一处观察：**把 Add 中的 UID 改成 2.25.9999，输出仍是 False：收到另一份影像不能补齐这条引用。

## 示例 04｜HL7 v2：从固定样本读出消息号、事件和患者号

```csharp
// @ 字符串中的反斜杠按原样保留；\r 是真实的段结束字符。
string message = @"MSH|^~\&|HIS|HOSP_A|OIS|ONC_A|20261007090000+0800||ADT^A08^ADT_A01|MSG-100|T|2.5.1"
    + "\r" + "PID|1||P0001^^^HOSP_A^MR||ZHANG^MING";

string[] segments = message.Split('\r');
string[] msh = segments[0].Split('|');
string[] pid = segments[1].Split('|');
string[] patientIdentifier = pid[3].Split('^');

Console.WriteLine($"发送机构：{msh[3]}");
Console.WriteLine($"事件：{msh[8]}");
Console.WriteLine($"消息号：{msh[9]}");
Console.WriteLine($"患者号：{patientIdentifier[0]}");
Console.WriteLine($"患者号签发机构：{patientIdentifier[3]}");
```

**预期输出：**

```text
发送机构：HOSP_A
事件：ADT^A08^ADT_A01
消息号：MSG-100
患者号：P0001
患者号签发机构：HOSP_A
```

**为什么这样写：**

这是从**固定、已知样本**取字段的阅读练习，只保留 MSH 与 PID，不是一份完整临床消息。`Split` 和 Java 分割字符串的用途相似，C# 这里按单个字符分割；连续分隔符形成的空字段会保留。

MSH 特殊：分隔符本身是 MSH-1，所以本例数组 `msh[9]` 对应 MSH-10。PID 是普通段，本例 `pid[3]` 对应 PID-3；然后再按 `^` 拆开组件。

这段没有做任意消息的结构检查、转义、重复字段或字符集处理，输入变化也可能使索引不适用。它用于帮助你读懂样例；实际接口采用完整解析库和约束校验。

**关键区别：MSG-100 是这条消息的编号，P0001 是患者业务编号。**发送机构与患者号签发机构是不同概念，只是在这个样本里恰好都为 HOSP_A。

**改一处观察：**把消息号改成 MSG-101，患者号不变：你得到的是涉及同一患者的一条新消息，不是一个新患者。

## 示例 05｜HL7 重发：同一条通知收到两次，只模拟处理一次

```csharp
var processedMessages = new HashSet<(string Source, string MessageId)>();

void Receive(string source, string messageId)
{
    // Add：首次加入返回 true，已存在返回 false。
    if (!processedMessages.Add((source, messageId)))
    {
        Console.WriteLine($"重复，跳过：{source}/{messageId}");
        return;
    }

    // 这里用输出代替实际的患者资料更新。
    Console.WriteLine($"处理：{source}/{messageId}");
}

Receive("HOSP_A/HIS", "MSG-100");
Receive("HOSP_A/HIS", "MSG-100"); // 模拟确认丢失后的重发。
Receive("HOSP_B/HIS", "MSG-100"); // 另一来源，不当成同一条。
```

**预期输出：**

```text
处理：HOSP_A/HIS/MSG-100
重复，跳过：HOSP_A/HIS/MSG-100
处理：HOSP_B/HIS/MSG-100
```

**为什么这样写：**

这里的 `void Receive(...)` 是局部函数，类似你在 Java 中写一个处理方法。第一条首次出现，Add 返回 true，执行处理；第二条键相同，Add 返回 false，提前 return；第三条来源不同，仍然处理。

这只在**同一进程、顺序调用、永不失败的输出模拟**里解释重复识别，没有实现真实患者更新、ACK 或可靠幂等。程序重启后集合会丢失，HashSet 也不是这里的并发控制方案。

而且本例先标记再输出：如果真实业务写入失败，不能将这条消息永远当已处理。生产设计需要持久状态、事务、冲突检查和失败恢复；不是把这段内存集合直接放进接口。

**改一处观察：**把第二次调用改成 MSG-101，会再次处理。这说明消息去重不能单独证明“同一业务意图只产生一次效果”。

## 示例 06｜FHIR：读取 Patient JSON，区分资源地址与业务病历号

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

using var document = JsonDocument.Parse(json);
var patient = document.RootElement;
var identifier = patient.GetProperty("identifier")[0];
var name = patient.GetProperty("name")[0];

Console.WriteLine($"类型：{patient.GetProperty("resourceType").GetString()}");
Console.WriteLine($"资源地址：Patient/{patient.GetProperty("id").GetString()}");
Console.WriteLine($"编号体系：{identifier.GetProperty("system").GetString()}");
Console.WriteLine($"业务病历号：{identifier.GetProperty("value").GetString()}");
Console.WriteLine($"姓名：{name.GetProperty("family").GetString()}{name.GetProperty("given")[0].GetString()}");
```

**预期输出：**

```text
类型：Patient
资源地址：Patient/patient-001
编号体系：https://hospital-a.example/identifiers/mrn
业务病历号：P0001
姓名：张明
```

**为什么这样写：**

`JsonDocument` 是 .NET 自带的 JSON 读取工具，类似用 Java JSON 库读取树结构；需要 `using System.Text.Json;`。`RootElement` 是最外层 JSON，`GetProperty` 按字段名读取，`[0]` 取数组第一项。

`using var` 在结束当前作用域时释放 JsonDocument，思路类似 Java 的 try-with-resources。三重双引号是多行原始字符串，方便直接放 JSON。

**看输出：资源地址是 Patient/patient-001，业务病历号是域中的 P0001，姓名是张明。**更换资源 id 不等于更换病历号。

这里仅读取本地固定 JSON，没有访问 FHIR 服务器，也没有做标准或 Profile 校验。实际输入可能缺字段、有多个标识或姓名，不能无条件取第一项当正确值。

**改一处观察：**把 id 改为 patient-002，identifier.value 保持 P0001，观察资源地址变了，但业务编号未变。

## 示例 07｜临床工作流：资料接收、审核、预约、执行分别记录

```csharp
// 只是教学状态快照，不定义真实系统的审批规则。
bool planReceived = true;
bool planApproved = false;
bool appointmentBooked = true;
bool treatmentCompleted = false;

Console.WriteLine($"计划已接收：{planReceived}");
Console.WriteLine($"计划已批准：{planApproved}");
Console.WriteLine($"预约已安排：{appointmentBooked}");
Console.WriteLine($"治疗已完成：{treatmentCompleted}");
```

**预期输出：**

```text
计划已接收：True
计划已批准：False
预约已安排：True
治疗已完成：False
```

**为什么这样写：**

四个 bool 表达四种不同事实。计划接收不自动改变批准状态，预约存在也不自动改变治疗完成状态。

本例用 bool 让概念直观；真正系统可能用状态机、记录表及角色权限表达，并验证合法状态变化。不要据此认为任何状态组合都合法。

这也解释了 API 返回成功必须说明“哪一步成功”：接收接口的成功不能代替临床审批或实际执行。

**改一处观察：**只把 planApproved 改为 true，观察 treatmentCompleted 仍然是 False。

## 用代码复述面试重点

- 患者身份：字典的键为什么包含域和编号？
- DICOM：为什么保留检查、序列和实例标识？为什么按明确引用找资料？
- HL7 v2：消息号为什么与患者号不同？重试为什么需要识别重复？
- FHIR：为什么资源地址与业务编号分别读取？
- 工作流：为什么接收、审核、预约、执行不能合成一个 success？

这些例子帮助理解概念，未实现临床接口。完成运行后可说“我用 C# 做过几个本地学习示例”，不要把它描述为 DICOM/HL7/FHIR 生产集成。

标准背景见[官方来源](05-官方来源与事实边界.md)。JSON API 用法依据微软 [System.Text.Json 文档](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-dom)。
