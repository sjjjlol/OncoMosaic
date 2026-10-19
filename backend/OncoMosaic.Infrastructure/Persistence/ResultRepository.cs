using System.Data;
using Microsoft.EntityFrameworkCore;
namespace OncoMosaic.Infrastructure;

public sealed class ResultRepository(AppDb db) : IResultRepository
{
    public async Task<ResultData> Load(Guid id, CancellationToken ct)
    {
        var run = await db.AnalysisRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new OperationFailure(FailureKind.NotFound, "NOT_FOUND", "任务不存在");
        var roi = await db.Rois.AsNoTracking().SingleAsync(r => r.Id == run.RoiId, ct);
        var image = await db.Images.AsNoTracking().SingleAsync(i => i.Id == roi.ImageId, ct);
        var cells = await db.Cells.AsNoTracking().Where(c => c.RunId == id).OrderBy(c => c.LocalIndex).ToListAsync(ct);
        return new(run, roi, image, cells, await Reviews(id, ct));
    }
    public Task<List<ReviewEntry>> Reviews(Guid id, CancellationToken ct) =>
        (from c in db.ReviewChanges.AsNoTracking() join r in db.ReviewRevisions.AsNoTracking() on c.RevisionId equals r.Id where r.RunId == id orderby r.Version select new ReviewEntry(c.CellId, c.Dimension, c.NewLabel, c.Reason, r.Version, r.CreatedAt)).ToListAsync(ct);
    public Task<Cell?> Cell(Guid runId, Guid cellId, CancellationToken ct) => db.Cells.AsNoTracking().SingleOrDefaultAsync(c => c.Id == cellId && c.RunId == runId, ct);
    public Task AddReview(ReviewRevision revision, ReviewChange change, CancellationToken ct)
    {
        // Saved by the surrounding WithLockedRun transaction.
        db.ReviewRevisions.Add(revision);
        db.ReviewChanges.Add(change);
        return Task.CompletedTask;
    }
    public async Task<Artifact> Artifact(Guid runId, string kind, CancellationToken ct) => await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(a => a.RunId == runId && a.Kind == kind, ct)
        ?? throw new OperationFailure(FailureKind.NotFound, "NOT_FOUND", "结果文件不存在");
    public async Task<T> InSnapshot<T>(Func<Task<T>> read, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction != null) return await read();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var result = await read();
        await transaction.CommitAsync(ct);
        return result;
    }
}
