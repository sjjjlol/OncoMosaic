using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
namespace OncoMosaic;

public class FileStore(IConfiguration configuration)
{
    public string Root { get; } = Path.GetFullPath(configuration["STORE_ROOT"] ?? "/store");
    public string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Path.IsPathRooted(key) || key.Contains('\\') || key.Split('/').Contains("..")) throw new ApiError(400, "INVALID_KEY", "非法文件键");
        var path = Path.GetFullPath(Path.Combine(Root, key));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ApiError(400, "INVALID_KEY", "非法文件键");
        return path;
    }
    public string Existing(string key)
    {
        var path = Resolve(key);
        if (!File.Exists(path)) throw new ApiError(424, "MISSING_ARTIFACT", "结果文件缺失，请检查持久存储卷");
        return path;
    }
    public static string Hash(string path) { using var f = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }
}
public class InferenceClient(HttpClient client)
{
    public async Task<T> Post<T>(string route, object input, CancellationToken token)
    {
        using var response = await client.PostAsJsonAsync(route, input, Json.Options, token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(token);
            throw new ApiError(422, "INFERENCE_REJECTED", "Python 校验或分析失败：" + body[..Math.Min(body.Length, 1200)]);
        }
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options, token)) ?? throw new ApiError(502, "INVALID_MANIFEST", "Python 返回了空响应");
    }
}
public class ImportService(AppDb db, FileStore store, InferenceClient python)
{
    public const long MaxBytes = 50 * 1024 * 1024;
    public async Task<TissueImage> Import(Guid projectId, string name, string description, Stream input, Stream assayInput, CancellationToken ct, Guid? fixedId = null)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new ApiError(404, "NOT_FOUND", "项目不存在");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || description.Length > 2000) throw new ApiError(400, "INVALID_NAME", "图像名称必填且最多 160 字，说明最多 2000 字");
        var id = fixedId ?? Guid.NewGuid();
        var tempKey = $"staging/{Guid.NewGuid()}/source.ome.tiff";
        var assayKey = tempKey.Replace("source.ome.tiff", "source.assay.json");
        var temp = store.Resolve(tempKey);
        var tempAssay = store.Resolve(assayKey);
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        string? destination = null;
        try
        {
            await using (var file = File.Create(temp))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > MaxBytes) throw new ApiError(413, "FILE_TOO_LARGE", "文件不得超过 50 MB");
                    await file.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            await using (var file = File.Create(tempAssay))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await assayInput.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > 100_000) throw new ApiError(413, "FILE_TOO_LARGE", "伴随元数据不得超过 100 KB");
                    await file.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            var info = await python.Post<InspectResult>("v1/inspect", new { imageKey = tempKey, assayKey }, ct);
            if (info.Width is < 1 or > 2048 || info.Height is < 1 or > 2048 || info.BandCount is < 8 or > 64 || !double.IsFinite(info.PixelSizeUm) || info.PixelSizeUm <= 0 || info.PreviewKey != tempKey.Replace("source.ome.tiff", "preview.png") || info.Wavelengths.Length != info.BandCount) throw new ApiError(502, "INVALID_METADATA", "Python 返回无效图像元数据");
            store.Existing(info.PreviewKey);
            var image = new TissueImage { Id = id, ProjectId = projectId, Name = name.Trim(), Description = description, FileKey = $"images/{id}/source.ome.tiff", AssayKey = $"images/{id}/source.assay.json", AssaySha256 = FileStore.Hash(tempAssay), AcquisitionJson = info.Acquisition.GetRawText(), PreviewKey = $"images/{id}/preview.png", Sha256 = FileStore.Hash(temp), Width = info.Width, Height = info.Height, BandCount = info.BandCount, PixelSizeUm = info.PixelSizeUm, WavelengthsJson = Json.Write(info.Wavelengths) };
            destination = Path.GetDirectoryName(store.Resolve(image.FileKey))!;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(Path.GetDirectoryName(temp)!, destination);
            db.Images.Add(image); await db.SaveChangesAsync(ct);
            return image;
        }
        catch { if (destination != null && Directory.Exists(destination)) Directory.Delete(destination, true); throw; }
        finally { var dir = Path.GetDirectoryName(temp)!; if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
