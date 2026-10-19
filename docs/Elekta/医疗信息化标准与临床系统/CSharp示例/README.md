# C# 医疗概念学习示例

入口说明和每段解读见 [代码串讲](../06-CSharp代码串讲.md)。代码使用虚构数据与 .NET 自带功能，不访问网络或真实临床系统。

要求 .NET 10 SDK。运行：

以下命令在 OncoMosaic 仓库根目录执行。

```bash
dotnet run --project 'docs/Elekta/医疗信息化标准与临床系统/CSharp示例/MedicalConcepts.csproj'
```

[Program.cs](Program.cs) 按顺序运行患者身份、DICOM 元数据、影像引用、HL7 固定样本阅读、消息重复识别、FHIR JSON、工作流状态七个示例。[预期输出](预期输出.txt) 可以与运行结果比较。

不依赖第三方包。bin/obj 是构建产物，已列入该目录的 .gitignore。

验证：2026-10-07 使用本机 .NET SDK 10.0.401 编译运行通过，七组输出与预期一致。
