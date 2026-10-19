using Xunit;
namespace OncoMosaic.Tests;

public class ReviewPolicyTests
{
    private static (AnalysisRun, Cell) SuccessfulResult()
    {
        var run = new AnalysisRun { ModelVersion = Ki67Quantification.Model, ThresholdsJson = Json.Write(new Thresholds(.3, .3, .3, .3)) };
        run.Start();
        run.Succeed(100);
        return (run, new Cell { RunId = run.Id, Ki67Value = .8, Ki67Quality = "ok" });
    }
    [Theory]
    [InlineData(null, "REVIEW_VERSION_REQUIRED")]
    [InlineData(1, "REVIEW_CONFLICT")]
    public void RequiresCurrentVersion(int? version, string code)
    {
        var (run, cell) = SuccessfulResult();
        Assert.Equal(code, Assert.Throws<OperationFailure>(() => ReviewPolicy.Validate(run, cell, "identity", "panck", "reason", version, 2)).Code);
    }
    [Fact]
    public void ReviewCannotManufactureUnmeasuredOrFailedQualitySignal()
    {
        var (run, cell) = SuccessfulResult();
        cell.Ki67Value = null;
        Assert.Equal("KI67_NOT_REVIEWABLE", Assert.Throws<OperationFailure>(() => ReviewPolicy.Validate(run, cell, "ki67", "positive", "reason", 0, 0)).Code);
        cell.Ki67Value = .8;
        cell.Ki67Quality = "channel-qc-failed";
        Assert.Throws<OperationFailure>(() => ReviewPolicy.Validate(run, cell, "ki67", "positive", "reason", 0, 0));
    }
    [Fact]
    public void RequiresReasonAndCellOwnershipWithoutMutatingMeasurement()
    {
        var (run, cell) = SuccessfulResult();
        Assert.Equal("REASON_REQUIRED", Assert.Throws<OperationFailure>(() => ReviewPolicy.Validate(run, cell, "ki67", "negative", " ", 0, 0)).Code);
        ReviewPolicy.Validate(run, cell, "ki67", "negative", "reviewed", 0, 0);
        Assert.Equal(.8, cell.Ki67Value);
        cell.RunId = Guid.NewGuid();
        Assert.Equal("INVALID_REVIEW", Assert.Throws<OperationFailure>(() => ReviewPolicy.Validate(run, cell, "identity", "panck", "reason", 0, 0)).Code);
    }
}
