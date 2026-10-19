using System.Net.Http.Json;
namespace OncoMosaic.Infrastructure;

public class InferenceClient(HttpClient client)
{
    public async Task<T> Post<T>(string route, object input, CancellationToken token)
    {
        using var response = await client.PostAsJsonAsync(route, input, Json.Options, token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(token);
            throw new OperationFailure(FailureKind.Rejected, "INFERENCE_REJECTED", "Python 校验或分析失败：" + body[..Math.Min(body.Length, 1200)]);
        }
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options, token)) ?? throw new OperationFailure(FailureKind.Dependency, "INVALID_MANIFEST", "Python 返回了空响应");
    }
}
