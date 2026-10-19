namespace OncoMosaic.Infrastructure;

public sealed class ImageImporter(FileStore store, InferenceClient python) : IImageImporter
{
    public const long MaxBytes = 50 * 1024 * 1024;
    public async Task<TissueImage> Stage(Guid id, Guid projectId, string name, string description, Stream input, Stream assayInput, CancellationToken ct)
    {
        var tempKey = $"staging/{Guid.NewGuid()}/source.ome.tiff";
        var assayKey = tempKey.Replace("source.ome.tiff", "source.assay.json");
        var temp = store.Resolve(tempKey);
        var tempAssay = store.Resolve(assayKey);
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        string? destination = null;
        var published = false;
        try
        {
            await using (var file = File.Create(temp))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > MaxBytes) throw new OperationFailure(FailureKind.TooLarge, "FILE_TOO_LARGE", "文件不得超过 50 MB");
                    await file.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            await using (var file = File.Create(tempAssay))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await assayInput.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > 100_000) throw new OperationFailure(FailureKind.TooLarge, "FILE_TOO_LARGE", "伴随元数据不得超过 100 KB");
                    await file.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            var info = await python.Post<InspectResult>("v1/inspect", new { imageKey = tempKey, assayKey }, ct);
            if (info.Width is < 1 or > 2048 || info.Height is < 1 or > 2048 || info.BandCount is < 8 or > 64 || !double.IsFinite(info.PixelSizeUm) || info.PixelSizeUm <= 0 || info.PreviewKey != tempKey.Replace("source.ome.tiff", "preview.png") || info.Wavelengths.Length != info.BandCount) throw new OperationFailure(FailureKind.Dependency, "INVALID_METADATA", "Python 返回无效图像元数据");
            store.Existing(info.PreviewKey);
            var image = new TissueImage { Id = id, ProjectId = projectId, Name = name.Trim(), Description = description, FileKey = $"images/{id}/source.ome.tiff", AssayKey = $"images/{id}/source.assay.json", AssaySha256 = FileStore.Hash(tempAssay), AcquisitionJson = info.Acquisition.GetRawText(), PreviewKey = $"images/{id}/preview.png", Sha256 = FileStore.Hash(temp), Width = info.Width, Height = info.Height, BandCount = info.BandCount, PixelSizeUm = info.PixelSizeUm, WavelengthsJson = Json.Write(info.Wavelengths) };
            destination = Path.GetDirectoryName(store.Resolve(image.FileKey))!;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(Path.GetDirectoryName(temp)!, destination);
            published = true;

            return image;
        }
        catch { if (published && destination != null && Directory.Exists(destination)) Directory.Delete(destination, true); throw; }
        finally { var dir = Path.GetDirectoryName(temp)!; if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    public void Remove(TissueImage image)
    {
        var directory = Path.GetDirectoryName(store.Resolve(image.FileKey))!;
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
