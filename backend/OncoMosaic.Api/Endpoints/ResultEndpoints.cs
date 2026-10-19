namespace OncoMosaic.Api;

public static class ResultEndpoints
{
    public static void MapResults(this WebApplication app)
    {
        app.MapGet("/api/analysis-runs/{id:guid}/cells", async (Guid id, int? reviewVersion, double? x, double? y, double? width, double? height, ResultService results, CancellationToken ct) =>
        {
            if ((x != null || y != null || width != null || height != null) && !(x is double xx && y is double yy && width is > 0 && height is > 0 && new[] { xx, yy, width.Value, height.Value }.All(double.IsFinite))) throw new OperationFailure(FailureKind.Invalid, "INVALID_VIEWPORT", "视野参数必须完整且宽高为正数");
            var s = await results.Snapshot(id, reviewVersion, ct);
            return x == null ? s.Cells : s.Cells.Where(c => c.X >= x && c.Y >= y && c.X < x + width && c.Y < y + height).ToList();
        });
        app.MapGet("/api/analysis-runs/{id:guid}/summary", async (Guid id, int? reviewVersion, ResultService results, CancellationToken ct) => (await results.Snapshot(id, reviewVersion, ct)).Summary);
        app.MapGet("/api/analysis-runs/{id:guid}/exploration", async (Guid id, int? reviewVersion, ResultService results, CancellationToken ct) => Comparison.Explore(await results.Snapshot(id, reviewVersion, ct)));
        app.MapPost("/api/images/{id:guid}/comparisons", async (Guid id, ComparisonInput input, ComparisonService comparisons, CancellationToken ct) => await comparisons.Create(id, input, ct));
        app.MapPost("/api/images/{id:guid}/comparisons/export", async (Guid id, ComparisonInput input, int? exportSchemaVersion, ComparisonService comparisons, CancellationToken ct) => Results.File(ComparisonExport.Export(await comparisons.Create(id, input, ct), exportSchemaVersion ?? 1), "application/zip", "oncomosaic-comparison.zip"));
        app.MapGet("/api/analysis-runs/{id:guid}/export", async (Guid id, int? reviewVersion, int? exportSchemaVersion, ResultService results, CancellationToken ct) => Results.File(await results.Export(id, reviewVersion, ct, exportSchemaVersion ?? 1), "application/zip", $"oncomosaic-{id}.zip"));
    }
}
