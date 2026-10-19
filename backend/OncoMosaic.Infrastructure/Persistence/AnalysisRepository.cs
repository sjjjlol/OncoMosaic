using Microsoft.EntityFrameworkCore;
namespace OncoMosaic.Infrastructure;

public sealed class AnalysisRepository(AppDb db) : IAnalysisRepository
{
    public Task<List<AnalysisRun>> Runs(Guid imageId, CancellationToken ct) =>
        (from run in db.AnalysisRuns.AsNoTracking() join roi in db.Rois on run.RoiId equals roi.Id where roi.ImageId == imageId orderby run.CreatedAt descending select run).ToListAsync(ct);
    public async Task<AnalysisRun> Run(Guid id, CancellationToken ct) => await db.AnalysisRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new OperationFailure(FailureKind.NotFound, "NOT_FOUND", "任务不存在");
    public async Task Add(AnalysisRun run, CancellationToken ct) { db.AnalysisRuns.Add(run); await db.SaveChangesAsync(ct); }

    public async Task<T> WithLockedRun<T>(Guid id, Func<AnalysisRun, Task<T>> action, CancellationToken ct)
    {
        // A fresh entity is required: a tracked instance could predate this row lock.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var run = await db.AnalysisRuns.FromSqlInterpolated($"SELECT * FROM `AnalysisRuns` WHERE `Id` = {id} FOR UPDATE").SingleOrDefaultAsync(ct)
                ?? throw new OperationFailure(FailureKind.NotFound, "NOT_FOUND", "任务不存在");
            var result = await action(run);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        finally { db.ChangeTracker.Clear(); }
    }

    public async Task<AnalysisRun?> ClaimNext(CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var run = await db.AnalysisRuns.FromSqlInterpolated($"SELECT * FROM `AnalysisRuns` WHERE `Status` = 'Queued' AND `ModelVersion` IN ({AnalysisScheme.LegacyModel}, {Ki67Quantification.Model}) ORDER BY `CreatedAt` LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(ct);
        var next = run.FirstOrDefault();
        if (next != null) { next.Start(); await db.SaveChangesAsync(ct); }
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return next;
    }

    public async Task RecoverInterrupted(CancellationToken ct)
    {
        var ids = await db.AnalysisRuns.AsNoTracking().Where(r => r.Status == "Running").Select(r => r.Id).ToListAsync(ct);
        foreach (var id in ids)
            await WithLockedRun(id, run => { if (run.Status == "Running") run.RecoverInterrupted(); return Task.FromResult(true); }, ct);
    }

    public Task Complete(Guid id, int attempt, AnalysisOutput output, CancellationToken ct) => WithLockedRun(id, run =>
    {
        // Never replace an already published result or its reviewed cell identifiers.
        if (run.Status != "Running" || run.Attempt != attempt) return Task.FromResult(false);
        run.Succeed(output.ValidTissuePx);
        db.Cells.AddRange(output.Cells);
        db.Artifacts.AddRange(output.Artifacts);
        return Task.FromResult(true);
    }, ct);

    public Task Fail(Guid id, int attempt, string code, string message, CancellationToken ct) => WithLockedRun(id, run =>
    {
        if (run.Status == "Running" && run.Attempt == attempt) run.Fail(code, message);
        return Task.FromResult(true);
    }, ct);
}
