namespace OncoMosaic.Infrastructure;

public sealed class AnalysisEngine(InferenceClient client, FileStore store) : IAnalysisEngine
{
    public async Task<AnalysisOutput> Analyze(AnalysisRun run, Roi roi, TissueImage image, CancellationToken ct)
    {
        var manifest = await client.Post<Manifest>("v1/analyze", new { runId = run.Id, imageKey = image.FileKey, assayKey = image.AssayKey, roi = roi.Rect(), modelVersion = run.ModelVersion }, ct);
        var (cells, artifacts) = ManifestValidator.Validate(run, roi, manifest, store, image);
        return new(manifest.ValidTissuePx, cells, artifacts);
    }
}
