# 07｜C# 处理 DICOM、HL7 v2、FHIR 的基础协议 API

更新：2026-10-07。这里直接使用协议库读写数据。已有的 Dictionary、Split、JsonDocument 示例保留为[概念辅助](06-CSharp代码串讲.md)，本章作为 API 学习入口。

## 1. 先分清三个层次

**医学标准**规定数据含义和交换规则；**.NET 协议库**把文件/消息/JSON 转成可操作对象，或封装网络交互；**业务代码**决定怎样匹配患者、处理状态和失败。

C# 是语言，本身没有内置 DICOM/HL7/FHIR 类型。.NET 项目可以通过 NuGet 引入专门库。你不必自己实现二进制文件解析，也不必用字符串下标访问真实 HL7 消息。

| 数据 | 本例采用的库/包 | 最先学的 API |
|---|---|---|
| DICOM | fo-dicom 5.2.6 | DicomFile.Open、DicomTag、GetSingleValue、GetSequence/TryGetSequence、DicomPixelData、Save |
| HL7 v2 | nHapi 3.2.4 | PipeParser.Parse、类型化消息/段/字段、PipeParser.Encode |
| FHIR R4 | Firely .NET SDK，Hl7.Fhir.R4 6.6.0 | Patient、FhirJsonDeserializer、FhirJsonSerializer、FhirClient.ReadAsync/SearchAsync |

这些是可用的第三方库示例，不是已知的 MOSAIQ 依赖清单。版本固定在随附项目中；FHIR 的 R4 是数据标准版本，SDK 6.6.0 是库版本，两者不是同一个编号。

## 2. 运行方式：有依赖包的独立控制台项目

[项目文件](CSharp协议API示例/MedicalApis.csproj)已经引用三个包，首次运行会由 dotnet 恢复 NuGet 依赖。

以下命令在 OncoMosaic 仓库根目录执行。

```bash
dotnet run --project 'docs/Elekta/医疗信息化标准与临床系统/CSharp协议API示例/MedicalApis.csproj'
```

本机使用 .NET SDK 10.0.401。主程序自动生成 2×2 像素的合成 DICOM 测试文件，用真实 API 保存、打开和读取；HL7 与 FHIR 使用虚构数据。合成文件放在项目构建输出目录，程序重新运行会重新生成测试文件。

已编译运行的部分：文件/Tag/像素/引用片段、HL7 解析与编码、FHIR 资源反序列化与序列化。服务器发送、读取和搜索示例仅编译验证，需要另有相应服务才能执行。

## 3. DICOM：打开文件、读 Tag、读取像素

### 3.1 初始化与命名空间

fo-dicom 5 控制台示例先初始化一次，完整项目已经包含：

```csharp
using FellowOakDicom;
using FellowOakDicom.Imaging;

new DicomSetupBuilder()
    .RegisterServices(s => s.AddFellowOakDicom())
    .Build();
```

初始化是让库的服务可用，先照着写即可。下面的 filePath 指向主程序自动生成的文件，也可以在独立读取程序中指向自己的授权测试文件。

### 3.2 读患者号、检查 UID、图像尺寸与第一帧

```csharp
var file = DicomFile.Open(filePath);
var dataset = file.Dataset;

string patientId = dataset.GetSingleValueOrDefault(DicomTag.PatientID, "未提供");
string studyUid = dataset.GetSingleValue<string>(DicomTag.StudyInstanceUID);
Console.WriteLine($"患者号：{patientId}");
Console.WriteLine($"检查 UID：{studyUid}");

var pixelData = DicomPixelData.Create(dataset);
Console.WriteLine($"图像尺寸：{pixelData.Width} × {pixelData.Height}");
Console.WriteLine($"帧数：{pixelData.NumberOfFrames}");
Console.WriteLine($"第一帧字节数：{pixelData.GetFrame(0).Data.Length}");
```

**本例输出：**患者号 P0001，检查 UID 2.25.1001，尺寸 2×2，1 帧，第一帧 4 字节。

- `DicomFile.Open` 真正解析磁盘上的 DICOM 文件，返回文件对象。
- `file.Dataset` 是其中的数据集，你可以从这里访问标准字段。
- `DicomTag.PatientID` 是库提供的标准 Tag 常量，对应 Patient ID；无需自己记 `(0010,0020)`。
- `GetSingleValueOrDefault` 读取单值 Tag，缺失时使用默认值；默认值只是显示处理，不代表业务校验通过。
- `GetSingleValue<string>` 读取所需单值；缺失或不适合该读取方式时会报错。
- `DicomPixelData.Create` 访问像素数据结构；Width/Height 读尺寸，GetFrame(0) 取第一帧。

本例是未压缩的单帧 8 位小图，4 字节就是 4 个像素。真实压缩或多帧图像要结合传输语法和所用解码/渲染库；不能把任意 GetFrame 返回值都当可直接显示的灰度数组。RTPLAN/RTSTRUCT 等对象也未必有图像像素。

**对应概念：**从文件里读出来的对象具有患者和检查上下文；读取像素与读取身份信息是不同 API。

### 3.3 修改测试字段并另存副本

```csharp
string copyPath = Path.Combine(AppContext.BaseDirectory, "learning-copy.dcm");
dataset.AddOrUpdate(DicomTag.PatientName, "TEST^LEARNER");
file.Save(copyPath);
var savedFile = DicomFile.Open(copyPath);
Console.WriteLine($"副本姓名：{savedFile.Dataset.GetSingleValue<string>(DicomTag.PatientName)}");
```

`AddOrUpdate` 修改或添加指定字段；`Save` 把数据写成 DICOM 文件。本例重新打开副本，验证姓名确实变成 TEST^LEARNER。它演示测试数据读写，不是完整匿名化流程。

### 3.4 放疗对象的引用序列怎样读

对于含有相应字段的 RTDOSE 对象，可以这样读取计划引用。此处 dataset 是该对象的数据集：

```csharp
if (dataset.TryGetSequence(DicomTag.ReferencedRTPlanSequence, out var plans))
{
    foreach (var item in plans.Items)
    {
        string planUid = item.GetSingleValue<string>(DicomTag.ReferencedSOPInstanceUID);
        Console.WriteLine($"引用的计划 UID：{planUid}");
    }
}
```

Sequence 可以理解为“包含多项子数据集的字段”。TryGetSequence 先检查存在，再逐项读取。ReferencedSOPInstanceUID 告诉你引用的是哪份计划对象。

主程序用库构造了一项**合成引用片段**，调用相同方法并输出 2.25.2001；这个片段不是完整 RTDOSE 文件。真实文件中是否要求此引用以及有多少项，要按对象字段和标准条件判断。

### 3.5 向 DICOM 服务发送文件：C-STORE

下面需要 `using FellowOakDicom.Network;` 和 `using FellowOakDicom.Network.Client;`，并传入测试服务的 host、port、calledAe 和 filePath：

```csharp
var client = DicomClientFactory.Create(host, port, false, "LEARN_SCU", calledAe);
var request = new DicomCStoreRequest(filePath);
request.OnResponseReceived = (_, response) => Console.WriteLine(response.Status);
await client.AddRequestAsync(request);
await client.SendAsync();
```

`DicomClientFactory.Create` 创建客户端；LEARN_SCU 是本例发送端 AE Title；calledAe 是目标 AE Title。第三个参数 false 表示本例未开启 TLS。`DicomCStoreRequest` 创建存储请求，AddRequestAsync 加入发送队列，SendAsync 发出，回调读取服务端的 DICOM 状态。

这段在 [ServerExamples.cs](CSharp协议API示例/ServerExamples.cs) 中已编译，但默认不执行。发送完成还要看响应状态；存储成功也不能自动证明计划已经获准使用。

## 4. HL7 v2：Parse 转成对象，Encode 转回消息

### 4.1 解析并读取字段

命名空间：`NHapi.Base.Parser` 和 `NHapi.Model.V251.Message`。示例使用 v2.5.1 的 ADT^A08 患者信息更新事件。

```csharp
string text = @"MSH|^~\&|HIS|HOSP_A|OIS|ONC_A|20261007090000+0800||ADT^A08^ADT_A01|MSG-100|T|2.5.1"
    + "\rEVN|A08|20261007090000+0800"
    + "\rPID|1||P0001^^^HOSP_A^MR||ZHANG^MING"
    + "\rPV1|1|O\r";

var parser = new PipeParser();
var message = (ADT_A01)parser.Parse(text);
var identifier = message.PID.GetPatientIdentifierList(0);
Console.WriteLine($"消息号：{message.MSH.MessageControlID.Value}");
Console.WriteLine($"患者号：{identifier.IDNumber.Value}");
Console.WriteLine($"患者号签发机构：{identifier.AssigningAuthority.NamespaceID.Value}");
```

**本例输出：**消息号 MSG-100、患者号 P0001、患者号签发机构 HOSP_A。

- `PipeParser.Parse` 把分隔符消息转成 nHapi 的消息对象。
- `ADT_A01` 是这里的结构类型；A08 更新事件复用这个结构，不意味着本例变成 A01 入院事件。
- `message.PID` 访问患者信息段，`message.MSH` 访问消息头。
- `GetPatientIdentifierList(0)` 取本例第一个患者标识，它包含编号及签发机构等组件。
- `.Value` 读取字段或组件的实际值。

你使用 `MessageControlID` 和 `IDNumber` 等属性，而不是在业务代码中记 `msh[9]`、`pid[3]`。真实输入仍需确认类型、版本、重复项和接口约束，不能不检查就强制转换所有消息。

### 4.2 修改并重新编码

```csharp
message.PID.GetPatientName(0).GivenName.Value = "XIAOMING";
string encoded = parser.Encode(message);
var roundTrip = (ADT_A01)parser.Parse(encoded);
Console.WriteLine($"重新解析后的名：{roundTrip.PID.GetPatientName(0).GivenName.Value}");
```

`GivenName.Value` 修改这份虚构患者姓名的“名”组件。`Encode` 根据消息模型生成协议文本。再次 Parse 后读出 XIAOMING，验证修改经过编码后保留了。

库负责消息模型及编码，应用仍负责患者匹配、处理状态、重试和去重。Parse 成功不代表满足医院的全部实施约束，也不会自动处理 MLLP 连接和可靠业务提交。

## 5. FHIR：JSON 变成 Patient 对象，再操作属性

### 5.1 使用 Firely 的反序列化 API

命名空间：`Hl7.Fhir.Model` 和 `Hl7.Fhir.Serialization`。下面使用 SDK 6 的 FhirJsonDeserializer。

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

**本例输出：**资源 ID patient-001、业务病历号 P0001、姓名张明。

`Deserialize<Patient>` 将 FHIR JSON 转成 SDK 的 Patient 对象。之后可以写 `patient.Id`、`patient.Identifier`、`patient.Name`，不必反复调用 GetProperty。

`Identifier[0]` 只是因为本例明确只有一个标识；真实数据可能有多个，需要按 system/use 等规则找到目标项。Patient 类型解析也不代替指定 Profile、术语与业务验证。

### 5.2 用标准类型创建资源，再序列化

```csharp
var newPatient = new Patient
{
    Id = "patient-002",
    Active = true,
    Identifier = new List<Identifier>
    {
        new Identifier("https://hospital-a.example/identifiers/mrn", "P0002")
    },
    Name = new List<HumanName>
    {
        new HumanName { Family = "李", Given = new[] { "华" } }
    }
};
string newJson = new FhirJsonSerializer().SerializeToString(newPatient);
Patient parsedPatient = FhirJsonDeserializer.DEFAULT.Deserialize<Patient>(newJson);
Console.WriteLine($"新资源：{parsedPatient.TypeName}/{parsedPatient.Id}");
Console.WriteLine($"新病历号：{parsedPatient.Identifier[0].Value}");
```

`Identifier` 和 `HumanName` 也是 SDK 提供的医疗数据类型；`FhirJsonSerializer.SerializeToString` 将对象转成 FHIR JSON。主程序重新解析得到 Patient/patient-002 和病历号 P0002。

这里创建的是本地对象和 JSON，没有提交服务器。下一节的客户端用于服务器交互。

### 5.3 通过 FhirClient 读取与搜索

命名空间：`Hl7.Fhir.Rest`。baseUrl 是测试 FHIR 服务基址，例如你自己部署的 R4 服务地址。

```csharp
using var client = new FhirClient(baseUrl, new FhirClientSettings
{
    PreferredFormat = ResourceFormat.Json,
    VerifyFhirVersion = true
});

Patient? patient = await client.ReadAsync<Patient>("Patient/patient-001");
Console.WriteLine($"读取的资源 ID：{patient?.Id}");

Bundle? results = await client.SearchAsync<Patient>(new[]
{
    "identifier=https://hospital-a.example/identifiers/mrn|P0001"
});
Console.WriteLine($"本页条目数：{results?.Entry.Count}");
```

- `FhirClient` 封装 FHIR HTTP 交互及资源转换。
- `ReadAsync<Patient>` 相当于读取指定资源地址。
- `SearchAsync<Patient>` 按业务标识搜索，返回 Bundle。
- 搜索条件中的 `system|value` 表达本例的标识体系与编号；SDK 负责请求的适当编码。
- `VerifyFhirVersion = true` 要求核对服务的版本信息；示例 SDK 使用 R4，目标也应与之兼容。

这段只编译验证，没有请求外部服务器。本例同时需要指定资源存在及搜索能力可用。Entry.Count 是本页条目数，不是自动得到全部患者命中数；实际客户端要处理分页、条目类型、权限与错误。CreateAsync、UpdateAsync 也用于资源写入，入门先掌握读取和搜索。

## 6. 面试最值得记住的 API 对照

| 想做什么 | C# 调用 | 对应业务理解 |
|---|---|---|
| 读一份影像文件 | DicomFile.Open | 从文件得到标准数据集 |
| 读患者号和检查身份 | GetSingleValue + DicomTag | 影像数据有明确上下文 |
| 读对象引用 | TryGetSequence + Items | 关联目标不是靠文件名猜 |
| 解析患者更新消息 | PipeParser.Parse | 原始消息变成类型化对象 |
| 输出 HL7 消息 | PipeParser.Encode | 对象按协议规则重新表达 |
| 读 FHIR JSON | Deserialize<Patient> | JSON 变成标准医疗资源对象 |
| 查询资源服务 | FhirClient.ReadAsync/SearchAsync | 地址读取与业务标识搜索不同 |

可口述：“我会用专门库处理协议格式，比如 fo-dicom 读取文件和 Tag，nHapi 解析 HL7 v2 消息，Firely SDK 操作 Patient 和 FHIR 接口。库负责格式与通信能力；患者身份、业务状态和故障恢复仍由应用按要求实现。”

## 7. 官方项目与 API 来源

- [fo-dicom 官方项目](https://github.com/fo-dicom/fo-dicom)：依赖包、初始化与网络 API 示例。
- [DicomFile](https://fo-dicom.github.io/stable/v5/api/FellowOakDicom.DicomFile.html)、[DicomDataset](https://fo-dicom.github.io/stable/v5/api/FellowOakDicom.DicomDataset.html)、[DicomPixelData](https://fo-dicom.github.io/stable/v5/api/FellowOakDicom.Imaging.DicomPixelData.html)：文件、Tag、Sequence 与像素 API。
- [nHapi 官方项目](https://github.com/nHapiNET/nHapi)、[解析](https://github.com/nHapiNET/nHapi/wiki/Decoding-a-message)、[编码](https://github.com/nHapiNET/nHapi/wiki/Encoding-a-message)：模型与消息处理。
- [Firely 反序列化](https://docs.fire.ly/projects/Firely-NET-SDK/en/latest/parsing/deserialization.html)、[资源 CRUD](https://docs.fire.ly/projects/Firely-NET-SDK/en/latest/client/crud.html)、[搜索](https://docs.fire.ly/projects/Firely-NET-SDK/en/latest/client/search.html)：资源转换和服务器交互。

版本来自本次 NuGet 包索引及实际恢复结果，未据此声称它们是招聘团队采用的版本。
