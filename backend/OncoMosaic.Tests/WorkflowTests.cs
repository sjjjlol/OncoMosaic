using Xunit;
namespace OncoMosaic.Tests;

public sealed class WorkflowTests
{
    [Fact]
    public async Task EngineTimeoutPersistsFailureWhileHostCancellationLeavesRecoverableRun()
    {
        var workspace = new WorkspaceStub();
        var timedOut = new RunStub();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new AnalysisProcessor(timedOut, workspace, new CancelledEngine()).ProcessNext(default));
        Assert.Equal("Failed", timedOut.TaskRun.Status);
        Assert.Equal("INFERENCE_TIMEOUT", timedOut.TaskRun.ErrorCode);

        using var shutdown = new CancellationTokenSource();
        var interrupted = new RunStub();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new AnalysisProcessor(interrupted, workspace, new CancelledEngine(shutdown)).ProcessNext(shutdown.Token));
        Assert.Equal("Running", interrupted.TaskRun.Status);
        Assert.Null(interrupted.TaskRun.ErrorCode);
    }

    [Fact]
    public async Task EmptyQueueDoesNotCallInference()
    {
        var runs = new RunStub { Empty = true };
        Assert.False(await new AnalysisProcessor(runs, new WorkspaceStub(), new CancelledEngine()).ProcessNext(default));
        Assert.Equal("Queued", runs.TaskRun.Status);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task ImportCompensatesOnlyAfterConfirmingNoCommittedRecord(bool committed, bool cannotVerify, bool shouldRemove)
    {
        var workspace = new WorkspaceStub { CommitThenDisconnect = committed, CannotVerify = cannotVerify };
        var importer = new ImporterStub();
        var service = new ImportService(workspace, importer);
        await Assert.ThrowsAsync<IOException>(() => service.Import(Guid.NewGuid(), "image", "", Stream.Null, Stream.Null, default));
        Assert.Equal(shouldRemove, importer.Removed);
    }

    private sealed class CancelledEngine(CancellationTokenSource? shutdown = null) : IAnalysisEngine
    {
        public Task<AnalysisOutput> Analyze(AnalysisRun run, Roi roi, TissueImage image, CancellationToken ct)
        {
            shutdown?.Cancel();
            throw new OperationCanceledException();
        }
    }
    private sealed class ImporterStub : IImageImporter
    {
        public bool Removed { get; private set; }
        public Task<TissueImage> Stage(Guid id, Guid projectId, string name, string description, Stream image, Stream assay, CancellationToken ct) => Task.FromResult(new TissueImage { Id = id, ProjectId = projectId });
        public void Remove(TissueImage image) => Removed = true;
    }
    private sealed class RunStub : IAnalysisRepository
    {
        public AnalysisRun TaskRun { get; } = new();
        public bool Empty { get; init; }
        public Task<AnalysisRun?> ClaimNext(CancellationToken ct)
        {
            if (Empty) return Task.FromResult<AnalysisRun?>(null);
            TaskRun.Start();
            return Task.FromResult<AnalysisRun?>(TaskRun);
        }
        public Task Fail(Guid id, int attempt, string code, string message, CancellationToken ct) { TaskRun.Fail(code, message); return Task.CompletedTask; }
        public Task<List<AnalysisRun>> Runs(Guid imageId, CancellationToken ct) => throw new NotSupportedException();
        public Task<AnalysisRun> Run(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task Add(AnalysisRun run, CancellationToken ct) => throw new NotSupportedException();
        public Task<T> WithLockedRun<T>(Guid id, Func<AnalysisRun, Task<T>> action, CancellationToken ct) => throw new NotSupportedException();
        public Task RecoverInterrupted(CancellationToken ct) => throw new NotSupportedException();
        public Task Complete(Guid id, int attempt, AnalysisOutput output, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class WorkspaceStub : IWorkspaceRepository
    {
        public bool CommitThenDisconnect { get; init; }
        public bool CannotVerify { get; init; }
        public Task<TissueImage> Image(Guid id, CancellationToken ct) => Task.FromResult(new TissueImage { Id = id });
        public Task<Roi> Roi(Guid id, CancellationToken ct) => Task.FromResult(new Roi { Id = id });
        public Task<bool> ProjectExists(Guid id, CancellationToken ct) => Task.FromResult(true);
        public Task AddImage(TissueImage image, CancellationToken ct) => throw new IOException("Commit response lost");
        public Task<bool> ImageExists(Guid id, CancellationToken ct) => CannotVerify ? throw new IOException("Database unavailable") : Task.FromResult(CommitThenDisconnect);
        public Task<List<Project>> Projects(CancellationToken ct) => throw new NotSupportedException();
        public Task AddProject(Project project, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<TissueImage>> Images(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<Roi>> Rois(Guid imageId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddRoi(Roi roi, CancellationToken ct) => throw new NotSupportedException();
    }
}
