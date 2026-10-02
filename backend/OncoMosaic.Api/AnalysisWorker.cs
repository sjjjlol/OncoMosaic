using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
namespace OncoMosaic;

public class AnalysisWorker(IServiceScopeFactory scopes, ILogger<AnalysisWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // The deployment runs one worker. Interrupted calls are safe to repeat with the same runId.
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await db.AnalysisRuns.Where(r => r.Status == "Running" && (r.ModelVersion == "spectral-mif-sim-v1" || r.ModelVersion == Ki67Quantification.Model)).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "Queued"), ct);
        }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                var run = await db.AnalysisRuns.Where(r => r.Status == "Queued" && (r.ModelVersion == "spectral-mif-sim-v1" || r.ModelVersion == Ki67Quantification.Model)).OrderBy(r => r.CreatedAt).FirstOrDefaultAsync(ct);
                if (run == null) { await Task.Delay(1000, ct); continue; }
                run.Start(); await db.SaveChangesAsync(ct);
                try
                {
                    var roi = await db.Rois.SingleAsync(r => r.Id == run.RoiId, ct);
                    var image = await db.Images.SingleAsync(i => i.Id == roi.ImageId, ct);
                    var client = scope.ServiceProvider.GetRequiredService<InferenceClient>();
                    var store = scope.ServiceProvider.GetRequiredService<FileStore>();
                    var manifest = await client.Post<Manifest>("v1/analyze", new { runId = run.Id, imageKey = image.FileKey, assayKey = image.AssayKey, roi = roi.Rect(), modelVersion = run.ModelVersion }, ct);
                    var (cells, artifacts) = Validate(run, roi, manifest, store, image);
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    await db.Cells.Where(c => c.RunId == run.Id).ExecuteDeleteAsync(ct);
                    await db.Artifacts.Where(a => a.RunId == run.Id).ExecuteDeleteAsync(ct);
                    db.Cells.AddRange(cells); db.Artifacts.AddRange(artifacts);
                    run.ValidTissuePx = manifest.ValidTissuePx;
                    run.Status = "Succeeded"; run.FinishedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
                    logger.LogInformation("Analysis {RunId} succeeded with {Count} cells", run.Id, cells.Count);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    logger.LogError(error, "Analysis {RunId} failed", run.Id);
                    db.ChangeTracker.Clear();
                    var failed = await db.AnalysisRuns.SingleAsync(r => r.Id == run.Id, ct);
                    failed.Status = "Failed"; failed.FinishedAt = DateTime.UtcNow;
                    failed.ErrorCode = error is ApiError ae ? ae.Code : "INFERENCE_UNAVAILABLE";
                    failed.ErrorMessage = error is ApiError ? error.Message : "光谱模拟服务不可用或结果无效。请检查 Python 服务与日志，恢复后重试。";
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Worker cycle failed"); await Task.Delay(2000, ct); }
        }
    }
    public static (List<Cell>, List<Artifact>) Validate(AnalysisRun run, Roi roi, Manifest manifest, FileStore store, TissueImage? image = null)
    {
        void Require(bool ok) { if (!ok) throw new ApiError(502, "INVALID_MANIFEST", "光谱分析结果不符合坐标或文件契约"); }
        Require(manifest.RunId == run.Id && manifest.ModelVersion == run.ModelVersion && manifest.Roi == roi.Rect() && manifest.Width == roi.Width && manifest.Height == roi.Height);
        Require(manifest.ValidTissuePx > 0 && manifest.ValidTissuePx <= roi.Width * roi.Height);
        var hasKi67 = run.ModelVersion == Ki67Quantification.Model && Json.Read<Thresholds>(run.ThresholdsJson).Ki67 != null;
        if (run.ModelVersion == Ki67Quantification.Model && image != null)
        {
            Require(manifest.Identity.ValueKind == System.Text.Json.JsonValueKind.Object);
            Require(manifest.Identity.GetProperty("imageSha256").GetString() == image.Sha256 && manifest.Identity.GetProperty("assaySha256").GetString() == image.AssaySha256);
            Require(Json.Read<Rectangle>(manifest.Identity.GetProperty("roi").GetRawText()) == roi.Rect() && manifest.Identity.GetProperty("modelVersion").GetString() == run.ModelVersion);
        }
        Require(manifest.Markers.Length == (hasKi67 ? 5 : 4) && manifest.Markers.Select(m => m.Name).Order().SequenceEqual((hasKi67 ? new[] { "CD3", "CD8", "DAPI", "panCK", "Ki67" } : new[] { "CD3", "CD8", "DAPI", "panCK" }).Order()));
        var keys = manifest.Markers.Select(m => (m.Name, m.DisplayKey)).Concat(new[] { ("mask", manifest.MaskKey), ("overlay", manifest.OverlayKey), ("tissue", manifest.TissueKey), ("qc", manifest.QcKey), ("cells", manifest.CellsKey) });
        Require(hasKi67 == (manifest.Ki67QuantitativeKey != null));
        if (hasKi67) keys = keys.Append(("Ki67-quantitative", manifest.Ki67QuantitativeKey!));
        var artifacts = new List<Artifact>();
        foreach (var (kind, key) in keys)
        {
            Require(key.StartsWith($"runs/{run.Id}/", StringComparison.Ordinal));
            var path = store.Existing(key);
            if (kind is "DAPI" or "panCK" or "CD3" or "CD8" or "Ki67" or "overlay" or "tissue")
            {
                var info = Image.Identify(path); Require(info.Width == roi.Width && info.Height == roi.Height);
            }
            artifacts.Add(new Artifact { RunId = run.Id, Kind = kind, FileKey = key, Sha256 = FileStore.Hash(path) });
        }
        using (var tissue = Image.Load<L8>(store.Existing(manifest.TissueKey)))
        {
            var count = 0;
            for (var y = 0; y < tissue.Height; y++) for (var x = 0; x < tissue.Width; x++) if (tissue[x, y].PackedValue > 0) count++;
            Require(count == manifest.ValidTissuePx);
        }
        string? ki67ChannelStatus = null;
        using (var qc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(store.Existing(manifest.QcKey))))
        {
            Require(qc.RootElement.GetProperty("validTissuePx").GetInt32() == manifest.ValidTissuePx);
            Require(double.IsFinite(qc.RootElement.GetProperty("meanSpectralResidual").GetDouble()));
            if (run.ModelVersion == Ki67Quantification.Model)
            {
                var k = qc.RootElement.GetProperty("ki67");
                ki67ChannelStatus = k.GetProperty("status").GetString();
                Require(hasKi67 ? ki67ChannelStatus is "ok" or "channel-qc-failed" : ki67ChannelStatus == "not-measured");
                Require(k.GetProperty("measurementVersion").GetString() == "ki67-nuclear-mean-v1" && k.GetProperty("compartment").GetString() == "nucleus" && k.GetProperty("signalUnit").GetString() == "simulated-relative-intensity");
            }
        }
        var measured = Json.Read<List<MeasuredCell>>(File.ReadAllText(store.Existing(manifest.CellsKey)));
        Require(measured.Count == manifest.CellCount && measured.Select(c => c.LocalIndex).Distinct().Count() == measured.Count);
        MaskContract.Validate(store.Existing(manifest.MaskKey), roi, measured);
        if (hasKi67) Ki67MapContract.Validate(store.Existing(manifest.Ki67QuantitativeKey!), store.Existing(manifest.MaskKey), roi, measured);
        var cells = new List<Cell>();
        foreach (var c in measured)
        {
            Require(c.LocalIndex > 0 && c.AreaPx > 0 && roi.Rect().Contains(c.X, c.Y) && c.QualityFlag is "ok" or "crop-edge");
            Require(new[] { c.DapiValue, c.PanckValue, c.Cd3Value, c.Cd8Value }.All(v => double.IsFinite(v) && v is >= 0 and <= 1));
            Require(hasKi67 ? c.Ki67Value is double value && double.IsFinite(value) && value is >= 0 and <= 1 && c.Ki67ValidPixelCount == c.AreaPx && c.Ki67Quality is "ok" or "crop-edge" or "channel-qc-failed" or "spectral-residual" or "saturated-signal" : c.Ki67Value == null && c.Ki67ValidPixelCount == 0 && c.Ki67Quality == "not-measured");
            if (hasKi67 && ki67ChannelStatus == "channel-qc-failed") Require(c.Ki67Quality is "channel-qc-failed" or "crop-edge");
            Require(c.Contour.Length >= 3 && c.Contour.All(p => p.Length == 2 && double.IsFinite(p[0]) && double.IsFinite(p[1]) && p[0] >= roi.X-.5 && p[1] >= roi.Y-.5 && p[0] <= roi.X+roi.Width && p[1] <= roi.Y+roi.Height));
            cells.Add(new Cell { RunId = run.Id, LocalIndex = c.LocalIndex, X = c.X, Y = c.Y, AreaPx = c.AreaPx, DapiValue = c.DapiValue, PanckValue = c.PanckValue, Cd3Value = c.Cd3Value, Cd8Value = c.Cd8Value, Ki67Value = c.Ki67Value, Ki67Quality = c.Ki67Quality, Ki67ValidPixelCount = c.Ki67ValidPixelCount, QualityFlag = c.QualityFlag, ContourJson = Json.Write(c.Contour) });
        }
        return (cells, artifacts);
    }
}
