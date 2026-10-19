namespace OncoMosaic.Application;

public sealed class ComparisonService(IResultRepository repository, ResultService results)
{
    public Task<ComparisonResult> Create(Guid imageId, ComparisonInput input, CancellationToken ct)
    {
        if (input.A == null || input.B == null)
            throw new OperationFailure(FailureKind.Invalid, "INVALID_COMPARISON", "需要区域 A 和 B 的分析任务与复核版本");
        return repository.InSnapshot(async () => Comparison.Build(imageId,
            await results.Snapshot(input.A.RunId, input.A.ReviewVersion, ct),
            await results.Snapshot(input.B.RunId, input.B.ReviewVersion, ct)), ct);
    }
}
