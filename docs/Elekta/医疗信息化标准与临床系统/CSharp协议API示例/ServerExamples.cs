using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;

// 需要对应的测试服务器；主程序不自动执行这些方法。
public static class ServerExamples
{
    public static async System.Threading.Tasks.Task SendDicomAsync(
        string filePath, string host, int port, string calledAe)
    {
        // demo:dicom-send:start
        var client = DicomClientFactory.Create(host, port, false, "LEARN_SCU", calledAe);
        var request = new DicomCStoreRequest(filePath);
        request.OnResponseReceived = (_, response) => Console.WriteLine(response.Status);
        await client.AddRequestAsync(request);
        await client.SendAsync();
        // demo:dicom-send:end
    }

    public static async System.Threading.Tasks.Task ReadAndSearchFhirAsync(string baseUrl)
    {
        // demo:fhir-client:start
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
        // demo:fhir-client:end
    }

    public static void PrintDoseReferences(DicomDataset dataset)
    {
        // demo:dicom-sequence:start
        if (dataset.TryGetSequence(DicomTag.ReferencedRTPlanSequence, out var plans))
        {
            foreach (var item in plans.Items)
            {
                string planUid = item.GetSingleValue<string>(DicomTag.ReferencedSOPInstanceUID);
                Console.WriteLine($"引用的计划 UID：{planUid}");
            }
        }
        // demo:dicom-sequence:end
    }
}
