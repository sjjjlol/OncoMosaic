namespace OncoMosaic.Application;

public record AnalysisOutput(int ValidTissuePx, List<Cell> Cells, List<Artifact> Artifacts);
public interface IAnalysisEngine
{
    Task<AnalysisOutput> Analyze(AnalysisRun run, Roi roi, TissueImage image, CancellationToken ct);
}

public interface IImageImporter
{
    // Returns validated, published image metadata. Caller persists it, or compensates on failure.
    Task<TissueImage> Stage(Guid id, Guid projectId, string name, string description, Stream image, Stream assay, CancellationToken ct);
    void Remove(TissueImage image);
}

public interface IArtifactFiles
{
    string Existing(string key);
}

public interface IResultExporter
{
    Task<byte[]> Export(ResultSnapshot snapshot, int schemaVersion, CancellationToken ct);
}
