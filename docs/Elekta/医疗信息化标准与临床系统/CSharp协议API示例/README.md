# C# 医学协议 API 示例

使用真实 NuGet 协议库：fo-dicom 5.2.6、nHapi 3.2.4、Firely Hl7.Fhir.R4 6.6.0。解释见 [协议 API 实战](../07-CSharp医学协议API实战.md)。

运行（要求 .NET 10 SDK，首次恢复包需要网络）：

以下命令在 OncoMosaic 仓库根目录执行。

```bash
dotnet run --project 'docs/Elekta/医疗信息化标准与临床系统/CSharp协议API示例/MedicalApis.csproj'
```

[Program.cs](Program.cs) 自动生成合成 DICOM 小图，运行文件读取/Tag/像素、修改保存、引用片段、HL7 解析编码、FHIR 资源解析序列化。测试文件写在构建输出目录，重复运行会重新生成。全部使用虚构数据。

[ServerExamples.cs](ServerExamples.cs) 提供 C-STORE 与 FHIR 读取/搜索方法，仅编译验证，默认不运行；需要测试服务器和相应能力。引用序列方法已通过主程序的合成片段调用验证。

验证：2026-10-07，本机 .NET SDK 10.0.401；编译 0 警告、0 错误，七组本地调用执行通过。未对外部 DICOM/FHIR 服务联调，未做临床一致性认证。
