namespace OncoMosaic.Application;

public sealed class ReviewService(IAnalysisRepository runs, IResultRepository results)
{
    public Task<List<ReviewEntry>> List(Guid id, CancellationToken ct) => results.Reviews(id, ct);
    public Task<int> Add(Guid id, ReviewInput input, CancellationToken ct) => runs.WithLockedRun(id, async run =>
    {
        var currentVersion = (await results.Reviews(id, ct)).Select(r => r.Version).DefaultIfEmpty(0).Max();
        var cell = await results.Cell(id, input.CellId, ct);
        ReviewPolicy.Validate(run, cell, input.Dimension, input.NewLabel, input.Reason, input.BaseReviewVersion, currentVersion);
        var revision = new ReviewRevision { RunId = id, Version = currentVersion + 1 };
        var change = new ReviewChange { RevisionId = revision.Id, CellId = input.CellId, Dimension = input.Dimension, NewLabel = input.NewLabel, Reason = input.Reason ?? "" };
        await results.AddReview(revision, change, ct);
        return revision.Version;
    }, ct);
}
