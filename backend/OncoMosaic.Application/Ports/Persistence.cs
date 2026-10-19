namespace OncoMosaic.Application;

public interface IWorkspaceRepository
{
    Task<List<Project>> Projects(CancellationToken ct);
    Task<bool> ProjectExists(Guid id, CancellationToken ct);
    Task AddProject(Project project, CancellationToken ct);
    Task<List<TissueImage>> Images(Guid projectId, CancellationToken ct);
    Task<TissueImage> Image(Guid id, CancellationToken ct);
    Task AddImage(TissueImage image, CancellationToken ct);
    Task<bool> ImageExists(Guid id, CancellationToken ct);
    Task<List<Roi>> Rois(Guid imageId, CancellationToken ct);
    Task<Roi> Roi(Guid id, CancellationToken ct);
    Task AddRoi(Roi roi, CancellationToken ct);
}

public interface IAnalysisRepository
{
    Task<List<AnalysisRun>> Runs(Guid imageId, CancellationToken ct);
    Task<AnalysisRun> Run(Guid id, CancellationToken ct);
    Task Add(AnalysisRun run, CancellationToken ct);
    // Serializes changes to a run, including review version allocation, in one transaction.
    Task<T> WithLockedRun<T>(Guid id, Func<AnalysisRun, Task<T>> action, CancellationToken ct);
    Task<AnalysisRun?> ClaimNext(CancellationToken ct);
    Task RecoverInterrupted(CancellationToken ct);
    Task Complete(Guid id, int attempt, AnalysisOutput output, CancellationToken ct);
    Task Fail(Guid id, int attempt, string code, string message, CancellationToken ct);
}

public record ReviewEntry(Guid CellId, string Dimension, string NewLabel, string Reason, int Version, DateTime CreatedAt);
public record ResultData(AnalysisRun Run, Roi Roi, TissueImage Image, List<Cell> Cells, List<ReviewEntry> Reviews);

public interface IResultRepository
{
    Task<ResultData> Load(Guid id, CancellationToken ct);
    Task<List<ReviewEntry>> Reviews(Guid id, CancellationToken ct);
    Task<Cell?> Cell(Guid runId, Guid cellId, CancellationToken ct);
    Task AddReview(ReviewRevision revision, ReviewChange change, CancellationToken ct);
    Task<Artifact> Artifact(Guid runId, string kind, CancellationToken ct);
    // Multiple reads resolve against a repeatable database snapshot.
    Task<T> InSnapshot<T>(Func<Task<T>> read, CancellationToken ct);
}
