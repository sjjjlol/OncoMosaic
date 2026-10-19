namespace OncoMosaic.Application;

public sealed class AnalysisService(IAnalysisRepository runs, IWorkspaceRepository workspace, IResultRepository results, IArtifactFiles files)
{
    public Task<List<AnalysisRun>> Runs(Guid imageId, CancellationToken ct) => runs.Runs(imageId, ct);
    public Task<AnalysisRun> Run(Guid id, CancellationToken ct) => runs.Run(id, ct);
    public async Task<AnalysisRun> Create(RunInput input, CancellationToken ct)
    {
        var roi = await workspace.Roi(input.RoiId, ct);
        var image = await workspace.Image(roi.ImageId, ct);
        var run = AnalysisScheme.Create(image, roi, input.ImageId, input.ModelVersion, input.Thresholds);
        await runs.Add(run, ct);
        return run;
    }
    public Task<AnalysisRun> Retry(Guid id, CancellationToken ct) => runs.WithLockedRun(id, run =>
    {
        run.Retry();
        return Task.FromResult(run);
    }, ct);
    public async Task<string> Artifact(Guid id, string kind, CancellationToken ct)
    {
        if ((await runs.Run(id, ct)).Status != "Succeeded")
            throw new OperationFailure(FailureKind.Conflict, "NOT_READY", "任务尚未成功");
        return files.Existing((await results.Artifact(id, kind, ct)).FileKey);
    }
}
