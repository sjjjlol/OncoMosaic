namespace OncoMosaic.Application;

// One invocation processes at most one durable task. Hosting and polling belong outside this class.
public sealed class AnalysisProcessor(IAnalysisRepository runs, IWorkspaceRepository workspace, IAnalysisEngine engine)
{
    public async Task<bool> ProcessNext(CancellationToken ct)
    {
        var run = await runs.ClaimNext(ct);
        if (run == null) return false;
        try
        {
            var roi = await workspace.Roi(run.RoiId, ct);
            var image = await workspace.Image(roi.ImageId, ct);
            var output = await engine.Analyze(run, roi, image, ct);
            await runs.Complete(run.Id, run.Attempt, output, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            error.Data["RunId"] = run.Id;
            var code = error is OperationFailure failure ? failure.Code : error is OperationCanceledException ? "INFERENCE_TIMEOUT" : "INFERENCE_UNAVAILABLE";
            var message = error is OperationFailure ? error.Message : "光谱模拟服务不可用、超时或结果无效，请检查服务日志后重试。";
            await runs.Fail(run.Id, run.Attempt, code, message, ct);
            throw; // The host logs the original exception, including run scope.
        }
    }
}
