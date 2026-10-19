using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using NHapi.Base.Parser;
using NHapi.Model.V251.Message;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

// fo-dicom 5 的控制台初始化。
new DicomSetupBuilder().RegisterServices(s => s.AddFellowOakDicom()).Build();

// 自动生成本地合成文件，使首次运行不依赖外部医学影像。
string filePath = Path.Combine(AppContext.BaseDirectory, "learning-image.dcm");
CreateSyntheticDicom(filePath);

Console.WriteLine("=== DICOM：打开文件、读取 Tag 与像素 ===");
// demo:dicom-read:start
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
// demo:dicom-read:end

Console.WriteLine("=== DICOM：修改测试字段、保存并重新读取 ===");
// demo:dicom-write:start
string copyPath = Path.Combine(AppContext.BaseDirectory, "learning-copy.dcm");
dataset.AddOrUpdate(DicomTag.PatientName, "TEST^LEARNER");
file.Save(copyPath);
var savedFile = DicomFile.Open(copyPath);
Console.WriteLine($"副本姓名：{savedFile.Dataset.GetSingleValue<string>(DicomTag.PatientName)}");
// demo:dicom-write:end

Console.WriteLine("=== DICOM：读取合成引用序列 ===");
// 仅构造 RTDOSE 中一类引用字段的片段，不是完整 RTDOSE 文件。
var referencedPlan = new DicomDataset
{
    { DicomTag.ReferencedSOPClassUID, DicomUID.RTPlanStorage },
    { DicomTag.ReferencedSOPInstanceUID, "2.25.2001" }
};
var doseFragment = new DicomDataset
{
    new DicomSequence(DicomTag.ReferencedRTPlanSequence, referencedPlan)
};
ServerExamples.PrintDoseReferences(doseFragment);

Console.WriteLine("=== HL7 v2：解析为对象、读取字段 ===");
// demo:hl7-read:start
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
// demo:hl7-read:end

Console.WriteLine("=== HL7 v2：修改对象、编码为消息并重新解析 ===");
// demo:hl7-write:start
message.PID.GetPatientName(0).GivenName.Value = "XIAOMING";
string encoded = parser.Encode(message);
var roundTrip = (ADT_A01)parser.Parse(encoded);
Console.WriteLine($"重新解析后的名：{roundTrip.PID.GetPatientName(0).GivenName.Value}");
// demo:hl7-write:end

Console.WriteLine("=== FHIR：反序列化为 Patient、读取属性 ===");
// demo:fhir-read:start
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
// demo:fhir-read:end

Console.WriteLine("=== FHIR：构造 Patient、序列化再解析 ===");
// demo:fhir-write:start
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
// demo:fhir-write:end

// 这个小图只是让 DICOM API 有真实文件可以读取，不是患者影像。
static void CreateSyntheticDicom(string path)
{
    var dataset = new DicomDataset(DicomTransferSyntax.ExplicitVRLittleEndian)
    {
        { DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage },
        { DicomTag.SOPInstanceUID, "2.25.1003" },
        { DicomTag.StudyInstanceUID, "2.25.1001" },
        { DicomTag.SeriesInstanceUID, "2.25.1002" },
        { DicomTag.PatientID, "P0001" },
        { DicomTag.PatientName, "ZHANG^MING" },
        { DicomTag.Modality, "OT" },
        { DicomTag.Rows, (ushort)2 },
        { DicomTag.Columns, (ushort)2 },
        { DicomTag.BitsAllocated, (ushort)8 }
    };
    var pixels = DicomPixelData.Create(dataset, true);
    pixels.SamplesPerPixel = 1;
    pixels.BitsStored = 8;
    pixels.HighBit = 7;
    pixels.PixelRepresentation = PixelRepresentation.Unsigned;
    pixels.PhotometricInterpretation = PhotometricInterpretation.Monochrome2;
    pixels.AddFrame(new MemoryByteBuffer(new byte[] { 0, 64, 128, 255 }));
    new DicomFile(dataset).Save(path);
}
