using System;
using System.Collections.Generic;
using System.Text.Json;

Console.WriteLine("=== 01 患者身份：相同编号不一定是同一个人 ===");
{
    // 键包含医院和患者号；值是展示姓名。
    var patients = new Dictionary<(string Hospital, string PatientId), string>();
    patients[("HOSP_A", "P0001")] = "张明";
    patients[("HOSP_B", "P0001")] = "李华";

    Console.WriteLine(patients[("HOSP_A", "P0001")]);
    Console.WriteLine(patients[("HOSP_B", "P0001")]);
    Console.WriteLine($"患者记录数：{patients.Count}");
}
Console.WriteLine();

Console.WriteLine("=== 02 DICOM 元数据：图像之外，还要知道属于谁、哪次检查 ===");
{
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
}
Console.WriteLine();

Console.WriteLine("=== 03 DICOM 关联：圈线只能按其明确引用找到对应资料 ===");
{
    // 只模拟某份轮廓数据里的一条影像实例引用。
    string referencedImageUid = "2.25.1003";
    var receivedImages = new HashSet<string>();

    Console.WriteLine($"引用的影像已到达：{receivedImages.Contains(referencedImageUid)}");

    // 随后收到这份影像。
    receivedImages.Add("2.25.1003");
    Console.WriteLine($"引用的影像已到达：{receivedImages.Contains(referencedImageUid)}");
}
Console.WriteLine();

Console.WriteLine("=== 04 HL7 v2：从固定样本读出消息号、事件和患者号 ===");
{
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
}
Console.WriteLine();

Console.WriteLine("=== 05 HL7 重发：同一条通知收到两次，只模拟处理一次 ===");
{
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
}
Console.WriteLine();

Console.WriteLine("=== 06 FHIR：读取 Patient JSON，区分资源地址与业务病历号 ===");
{
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
}
Console.WriteLine();

Console.WriteLine("=== 07 临床工作流：资料接收、审核、预约、执行分别记录 ===");
{
    // 只是教学状态快照，不定义真实系统的审批规则。
    bool planReceived = true;
    bool planApproved = false;
    bool appointmentBooked = true;
    bool treatmentCompleted = false;

    Console.WriteLine($"计划已接收：{planReceived}");
    Console.WriteLine($"计划已批准：{planApproved}");
    Console.WriteLine($"预约已安排：{appointmentBooked}");
    Console.WriteLine($"治疗已完成：{treatmentCompleted}");
}
Console.WriteLine();
