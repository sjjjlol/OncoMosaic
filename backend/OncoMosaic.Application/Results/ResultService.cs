namespace OncoMosaic.Application;

public sealed class ResultService(IResultRepository repository, IResultExporter exporter)
{
    public Task<ResultSnapshot> Snapshot(Guid id, int? reviewVersion, CancellationToken ct) => repository.InSnapshot(async () =>
    {
        var data = await repository.Load(id, ct);
        if (data.Run.Status != "Succeeded")
            throw new OperationFailure(FailureKind.Conflict, "NOT_READY", "分析尚未成功，无法读取结果");
        var version = reviewVersion ?? data.Reviews.Select(r => r.Version).DefaultIfEmpty(0).Max();
        if (version < 0 || (version > 0 && !data.Reviews.Any(r => r.Version == version)))
            throw new OperationFailure(FailureKind.NotFound, "VERSION_NOT_FOUND", "复核版本不存在");
        var changes = data.Reviews.Where(r => r.Version <= version).OrderBy(r => r.Version).ToList();
        var effective = new Dictionary<Guid, string>();
        var ki67Effective = new Dictionary<Guid, string>();
        foreach (var change in changes)
            (change.Dimension == "ki67" ? ki67Effective : effective)[change.CellId] = change.NewLabel;
        var thresholds = Json.Read<Thresholds>(data.Run.ThresholdsJson);
        var views = Quantification.Views(data.Cells, thresholds, effective, ki67Effective);
        return new ResultSnapshot(data.Run, data.Roi, data.Image, views,
            Quantification.Calculate(views, data.Roi, data.Image.PixelSizeUm, data.Run.ValidTissuePx, thresholds, version), changes.Cast<object>().ToArray());
    }, ct);

    public async Task<byte[]> Export(Guid id, int? version, CancellationToken ct, int exportSchemaVersion = 1)
    {
        if (exportSchemaVersion is not (1 or 2))
            throw new OperationFailure(FailureKind.Invalid, "INVALID_EXPORT_VERSION", "导出版本只支持 1 或 2");
        return await exporter.Export(await Snapshot(id, version, ct), exportSchemaVersion, ct);
    }
}
