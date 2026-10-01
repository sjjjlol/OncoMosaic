using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using OncoMosaic;

if (args.Contains("--healthcheck"))
{
    try { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) }; var response = await client.GetAsync("http://127.0.0.1:8080/api/health"); Environment.Exit(response.IsSuccessStatusCode ? 0 : 1); }
    catch { Environment.Exit(1); }
    return;
}
var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = ImportService.MaxBytes + 1024 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = ImportService.MaxBytes + 1024 * 1024);
builder.Services.AddDbContext<AppDb>(o => o.UseMySQL(builder.Configuration.GetConnectionString("Database") ?? "Server=localhost;Database=oncomosaic;User=oncomosaic;Password=local-demo-only;AllowPublicKeyRetrieval=True"));
builder.Services.AddSingleton<FileStore>();
builder.Services.AddHttpClient<InferenceClient>(c => { c.BaseAddress = new Uri(builder.Configuration["PYTHON_URL"] ?? "http://localhost:8000/"); c.Timeout = TimeSpan.FromSeconds(90); });
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<ResultService>();
builder.Services.AddScoped<ComparisonService>();
builder.Services.AddHostedService<AnalysisWorker>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (Exception e)
    {
        var (status, code, message) = e switch
        {
            ApiError a => (a.Status, a.Code, a.Message),
            BadHttpRequestException => (400, "INVALID_REQUEST", "请求格式不正确或超过大小限制"),
            HttpRequestException => (503, "INFERENCE_UNAVAILABLE", "Python 服务不可用，请稍后重试"),
            TaskCanceledException => (504, "INFERENCE_TIMEOUT", "处理超时，请稍后重试"),
            DbUpdateException => (409, "CONFLICT", "记录冲突，请刷新后重试"),
            _ => (500, "INTERNAL_ERROR", "服务器处理失败，请查看 API 日志")
        };
        app.Logger.LogWarning(e, "Request failed: {Code}", code);
        if (!context.Response.HasStarted) { context.Response.StatusCode = status; await context.Response.WriteAsJsonAsync(new { code, message }); }
    }
});
app.MapGet("/api/health", async (AppDb db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.Json(new { code = "DATABASE_UNAVAILABLE", message = "数据库不可用" }, statusCode: 503));
app.MapGet("/api/projects", (AppDb db) => db.Projects.OrderBy(p => p.CreatedAt).ToListAsync());
app.MapPost("/api/projects", async (ProjectInput input, AppDb db) =>
{
    if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120) throw new ApiError(400, "INVALID_NAME", "项目名称必填且最多 120 字");
    var p = new Project { Name = input.Name.Trim() }; db.Projects.Add(p); await db.SaveChangesAsync(); return Results.Created($"/api/projects/{p.Id}", p);
});
static object ImageDto(TissueImage i) => new { i.Id, i.ProjectId, i.Name, i.Description, i.Width, i.Height, i.BandCount, i.PixelSizeUm, wavelengths = Json.Read<double[]>(i.WavelengthsJson), acquisition = Json.Read<object>(i.AcquisitionJson), i.Sha256, i.AssaySha256, i.CreatedAt, previewUrl = $"/api/images/{i.Id}/preview", notice = Quantification.Notice };
app.MapGet("/api/projects/{id:guid}/images", async (Guid id, AppDb db) => (await db.Images.Where(i => i.ProjectId == id && i.AssayKey != "").OrderBy(i => i.CreatedAt).ToListAsync()).Select(ImageDto));
app.MapPost("/api/projects/{id:guid}/images", async (Guid id, HttpRequest request, ImportService imports, CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file"); var assay = form.Files.GetFile("assay");
    if (file == null || !file.FileName.EndsWith(".ome.tiff", StringComparison.OrdinalIgnoreCase) || assay == null || !assay.FileName.EndsWith(".assay.json", StringComparison.OrdinalIgnoreCase)) throw new ApiError(400, "INVALID_FILE", "请选择 .ome.tiff 图像及 .assay.json 伴随文件");
    if (file.Length > ImportService.MaxBytes || assay.Length > 100_000) throw new ApiError(413, "FILE_TOO_LARGE", "图像不得超过 50 MB，伴随元数据不得超过 100 KB");
    await using var stream = file.OpenReadStream(); await using var assayStream = assay.OpenReadStream();
    var image = await imports.Import(id, form["name"].ToString() is { Length: > 0 } name ? name : file.FileName[..^9], form["description"].ToString(), stream, assayStream, ct);
    return Results.Created($"/api/images/{image.Id}", ImageDto(image));
}).DisableAntiforgery();
app.MapGet("/api/images/{id:guid}", async (Guid id, AppDb db) => ImageDto(await db.Images.SingleOrDefaultAsync(i => i.Id == id && i.AssayKey != "") ?? throw new ApiError(404, "NOT_FOUND", "新版图像不存在")));
app.MapGet("/api/images/{id:guid}/preview", async (Guid id, AppDb db, FileStore store) =>
{
    var image = await db.Images.FindAsync(id) ?? throw new ApiError(404, "NOT_FOUND", "图像不存在"); return Results.File(store.Existing(image.PreviewKey), "image/png");
});
app.MapGet("/api/images/{id:guid}/rois", (Guid id, AppDb db) => db.Rois.Where(r => r.ImageId == id).ToListAsync());
app.MapPost("/api/images/{id:guid}/rois", async (Guid id, RoiInput input, AppDb db) =>
{
    var image = await db.Images.FindAsync(id) ?? throw new ApiError(404, "NOT_FOUND", "图像不存在");
    if (!new Rectangle(input.X, input.Y, input.Width, input.Height).Valid(image.Width, image.Height)) throw new ApiError(400, "INVALID_ROI", "ROI 必须在图像范围内且宽高大于零");
    if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120 || input.RegionTag is not ("tumor-candidate" or "other")) throw new ApiError(400, "INVALID_ROI", "区域名称或标签无效");
    var roi = new Roi { ImageId = id, Name = input.Name.Trim(), X = input.X, Y = input.Y, Width = input.Width, Height = input.Height, RegionTag = input.RegionTag };
    db.Rois.Add(roi); await db.SaveChangesAsync(); return Results.Created($"/api/images/{id}/rois", roi);
});
static object RunDto(AnalysisRun r) => new { runId = r.Id, r.RoiId, r.Status, r.Attempt, r.CreatedAt, r.StartedAt, r.FinishedAt, r.ModelVersion, r.AlgorithmVersion, thresholds = Json.Read<Thresholds>(r.ThresholdsJson), error = r.ErrorCode == null ? null : new { code = r.ErrorCode, message = r.ErrorMessage } };
app.MapGet("/api/images/{id:guid}/analysis-runs", async (Guid id, AppDb db) => (await (from run in db.AnalysisRuns join roi in db.Rois on run.RoiId equals roi.Id where roi.ImageId == id orderby run.CreatedAt descending select run).ToListAsync()).Select(RunDto));
app.MapPost("/api/analysis-runs", async (RunInput input, AppDb db) =>
{
    var roi = await db.Rois.FindAsync(input.RoiId) ?? throw new ApiError(404, "NOT_FOUND", "ROI 不存在");
    if (roi.ImageId != input.ImageId || input.ModelVersion != "spectral-mif-sim-v1" || input.Thresholds == null || !input.Thresholds.Valid()) throw new ApiError(400, "INVALID_ANALYSIS", "图像、模型版本或阈值无效；阈值必须在 0–1 内");
    var run = new AnalysisRun { RoiId = roi.Id, ThresholdsJson = Json.Write(input.Thresholds) };
    db.AnalysisRuns.Add(run); await db.SaveChangesAsync(); return Results.Accepted($"/api/analysis-runs/{run.Id}", RunDto(run));
});
app.MapGet("/api/analysis-runs/{id:guid}", async (Guid id, AppDb db) => RunDto(await db.AnalysisRuns.FindAsync(id) ?? throw new ApiError(404, "NOT_FOUND", "任务不存在")));
app.MapPost("/api/analysis-runs/{id:guid}/retry", async (Guid id, AppDb db, CancellationToken ct) =>
{
    // Conditional update prevents a double-click from requeuing a running task.
    var changed = await db.AnalysisRuns.Where(r => r.Id == id && r.Status == "Failed").ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "Queued").SetProperty(r => r.ErrorCode, (string?)null).SetProperty(r => r.ErrorMessage, (string?)null).SetProperty(r => r.StartedAt, (DateTime?)null).SetProperty(r => r.FinishedAt, (DateTime?)null), ct);
    if (changed == 0) throw new ApiError(409, "INVALID_STATE", "只有失败任务可以重试");
    return Results.Accepted($"/api/analysis-runs/{id}", RunDto(await db.AnalysisRuns.SingleAsync(r => r.Id == id, ct)));
});
app.MapGet("/api/analysis-runs/{id:guid}/artifacts/{kind}", async (Guid id, string kind, AppDb db, FileStore store) =>
{
    if (!await db.AnalysisRuns.AnyAsync(r => r.Id == id && r.Status == "Succeeded")) throw new ApiError(409, "NOT_READY", "任务尚未成功");
    var a = await db.Artifacts.SingleOrDefaultAsync(a => a.RunId == id && a.Kind == kind) ?? throw new ApiError(404, "NOT_FOUND", "结果文件不存在");
    return Results.File(store.Existing(a.FileKey), kind == "mask" ? "application/octet-stream" : kind is "cells" or "qc" ? "application/json" : "image/png");
});
app.MapGet("/api/analysis-runs/{id:guid}/cells", async (Guid id, int? reviewVersion, double? x, double? y, double? width, double? height, ResultService results, CancellationToken ct) =>
{
    if ((x != null || y != null || width != null || height != null) && !(x is double xx && y is double yy && width is > 0 && height is > 0 && new[] { xx, yy, width.Value, height.Value }.All(double.IsFinite))) throw new ApiError(400, "INVALID_VIEWPORT", "视野参数必须完整且宽高为正数");
    var s = await results.Snapshot(id, reviewVersion, ct);
    return x == null ? s.Cells : s.Cells.Where(c => c.X >= x && c.Y >= y && c.X < x+width && c.Y < y+height).ToList();
});
app.MapGet("/api/analysis-runs/{id:guid}/summary", async (Guid id, int? reviewVersion, ResultService results, CancellationToken ct) => (await results.Snapshot(id, reviewVersion, ct)).Summary);
app.MapGet("/api/analysis-runs/{id:guid}/exploration", async (Guid id, int? reviewVersion, ResultService results, CancellationToken ct) => Comparison.Explore(await results.Snapshot(id, reviewVersion, ct)));
app.MapPost("/api/images/{id:guid}/comparisons", async (Guid id, ComparisonInput input, ComparisonService comparisons, CancellationToken ct) => await comparisons.Create(id, input, ct));
app.MapPost("/api/images/{id:guid}/comparisons/export", async (Guid id, ComparisonInput input, ComparisonService comparisons, CancellationToken ct) => Results.File(Comparison.Export(await comparisons.Create(id, input, ct)), "application/zip", "oncomosaic-comparison.zip"));
app.MapGet("/api/analysis-runs/{id:guid}/reviews", async (Guid id, AppDb db) => await (from change in db.ReviewChanges join rev in db.ReviewRevisions on change.RevisionId equals rev.Id where rev.RunId == id orderby rev.Version select new { rev.Version, rev.CreatedAt, change.CellId, change.NewLabel, change.Reason }).ToListAsync());
app.MapPost("/api/analysis-runs/{id:guid}/reviews", async (Guid id, ReviewInput input, AppDb db, CancellationToken ct) =>
{
    if (!Quantification.Labels.Contains(input.NewLabel) || (input.Reason?.Length ?? 0) > 2000) throw new ApiError(400, "INVALID_REVIEW", "复核类别或原因无效");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var run = await db.AnalysisRuns.FromSqlInterpolated($"SELECT * FROM `AnalysisRuns` WHERE `Id` = {id} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw new ApiError(404, "NOT_FOUND", "任务不存在");
    if (run.Status != "Succeeded" || !await db.Cells.AnyAsync(c => c.Id == input.CellId && c.RunId == id, ct)) throw new ApiError(409, "INVALID_REVIEW", "只能复核此成功任务中的细胞");
    var version = (await db.ReviewRevisions.Where(r => r.RunId == id).MaxAsync(r => (int?)r.Version, ct) ?? 0) + 1;
    var revision = new ReviewRevision { RunId = id, Version = version };
    db.ReviewRevisions.Add(revision); db.ReviewChanges.Add(new ReviewChange { RevisionId = revision.Id, CellId = input.CellId, NewLabel = input.NewLabel, Reason = input.Reason ?? "" });
    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Created($"/api/analysis-runs/{id}/reviews", new { reviewVersion = version });
});
app.MapGet("/api/analysis-runs/{id:guid}/export", async (Guid id, int? reviewVersion, ResultService results, CancellationToken ct) => Results.File(await results.Export(id, reviewVersion, ct), "application/zip", $"oncomosaic-{id}.zip"));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Database.MigrateAsync();
    var projectId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    var imageId = Guid.Parse("a1000000-0000-0000-0000-000000000003");
    if (!await db.Projects.AnyAsync(p => p.Id == projectId)) { db.Projects.Add(new Project { Id = projectId, Name = "多标记组织 · 演示项目" }); await db.SaveChangesAsync(); }
    var fixture = builder.Configuration["DEMO_FILE"] ?? "/demo/sample-spectral-mif.ome.tiff";
    var fixtureAssay = fixture.Replace(".ome.tiff", ".assay.json");
    if (File.Exists(fixture) && File.Exists(fixtureAssay) && !await db.Images.AnyAsync(i => i.Id == imageId))
    {
        var store = scope.ServiceProvider.GetRequiredService<FileStore>();
        var orphan = Path.GetDirectoryName(store.Resolve($"images/{imageId}/source.ome.tiff"))!;
        if (Directory.Exists(orphan)) Directory.Delete(orphan, true);
        await using var stream = File.OpenRead(fixture); await using var assayStream = File.OpenRead(fixtureAssay);
        await scope.ServiceProvider.GetRequiredService<ImportService>().Import(projectId, "合成光谱 mIF · sample", "24 波段 OME-TIFF 与模拟单染对照；无患者来源。", stream, assayStream, CancellationToken.None, imageId);
    }
}
app.Run();
public partial class Program { }
