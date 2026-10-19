using Xunit;
namespace OncoMosaic.Tests;

public class AnalysisLifecycleTests
{
    [Fact]
    public void SuccessfulResultsCannotBeRetriedFailedOrRecovered()
    {
        var run = new AnalysisRun();
        run.Start();
        run.Succeed(120);
        Assert.Throws<OperationFailure>(run.Retry);
        Assert.Throws<OperationFailure>(() => run.Fail("LATE", "Late failure"));
        Assert.Throws<OperationFailure>(run.RecoverInterrupted);
        Assert.Equal("Succeeded", run.Status);
        Assert.Equal(120, run.ValidTissuePx);
    }

    [Fact]
    public void RecoveryConsumesTheSameBoundedAttemptBudgetAsManualRetry()
    {
        var run = new AnalysisRun();
        var id = run.Id;
        for (var attempt = 1; attempt <= AnalysisRun.MaximumAttempts; attempt++)
        {
            run.Start();
            Assert.Equal(attempt, run.Attempt);
            run.RecoverInterrupted();
            Assert.Equal(id, run.Id);
        }
        Assert.Equal("Failed", run.Status);
        Assert.Equal("WORKER_INTERRUPTED", run.ErrorCode);
        Assert.Equal("RETRY_LIMIT", Assert.Throws<OperationFailure>(run.Retry).Code);
        Assert.NotNull(run.FinishedAt);
    }

    [Fact]
    public void RetryClearsFailureButPreservesSchemeAndIdentity()
    {
        var run = new AnalysisRun { ThresholdsJson = Json.Write(new Thresholds(.2, .3, .4)) };
        var scheme = run.ThresholdsJson;
        run.Start();
        run.Fail("TIMEOUT", "Timed out");
        run.Retry();
        Assert.Equal("Queued", run.Status);
        Assert.Null(run.StartedAt);
        Assert.Null(run.FinishedAt);
        Assert.Null(run.ErrorCode);
        Assert.Equal(1, run.Attempt);
        Assert.Equal(scheme, run.ThresholdsJson);
    }

    [Fact]
    public void CompletionRequiresRunningStateAndValidTissue()
    {
        var run = new AnalysisRun();
        Assert.Throws<OperationFailure>(() => run.Succeed(1));
        run.Start();
        Assert.Throws<ArgumentOutOfRangeException>(() => run.Succeed(0));
        Assert.Equal("Running", run.Status);
    }

    [Fact]
    public void MissingKi67ThresholdOrWrongImageCannotCreateRun()
    {
        var image = new TissueImage { AcquisitionJson = "{\"markers\":[\"Ki67\"]}" };
        var roi = new Roi { ImageId = image.Id };
        Assert.Equal("INVALID_KI67_SCHEME", Assert.Throws<OperationFailure>(() => AnalysisScheme.Create(image, roi, image.Id, Ki67Quantification.Model, new(.3, .3, .3))).Code);
        Assert.Equal("INVALID_ANALYSIS", Assert.Throws<OperationFailure>(() => AnalysisScheme.Create(image, roi, Guid.NewGuid(), Ki67Quantification.Model, new(.3, .3, .3, .3))).Code);
        Assert.Equal(Ki67Quantification.Algorithm, AnalysisScheme.Create(image, roi, image.Id, Ki67Quantification.Model, new(.3, .3, .3, .3)).AlgorithmVersion);
    }
}
