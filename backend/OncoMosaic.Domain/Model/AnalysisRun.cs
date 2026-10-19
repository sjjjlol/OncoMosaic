using System.Text.Json;
namespace OncoMosaic.Domain;

public class AnalysisRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid RoiId { get; init; }
    public string Status { get; private set; } = "Queued";
    public int Attempt { get; private set; }
    public string ModelVersion { get; init; } = "spectral-mif-sim-v1";
    public string AlgorithmVersion { get; init; } = "quantification-v2";
    public string ThresholdsJson { get; init; } = Json.Write(new Thresholds(.35, .35, .35));
    public int ValidTissuePx { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }
    public const int MaximumAttempts = 3;

    public void Start()
    {
        Require(Status == "Queued", "只有排队任务可运行");
        Require(Attempt < MaximumAttempts, "任务已达到最大尝试次数");
        Status = "Running";
        Attempt++;
        StartedAt = DateTime.UtcNow;
        FinishedAt = null;
        ErrorCode = ErrorMessage = null;
    }

    public void Succeed(int validTissuePx)
    {
        Require(Status == "Running", "只有运行中的任务可完成");
        if (validTissuePx <= 0) throw new ArgumentOutOfRangeException(nameof(validTissuePx));
        ValidTissuePx = validTissuePx;
        Status = "Succeeded";
        FinishedAt = DateTime.UtcNow;
        ErrorCode = ErrorMessage = null;
    }

    public void Fail(string code, string message)
    {
        Require(Status == "Running", "只有运行中的任务可失败");
        Status = "Failed";
        FinishedAt = DateTime.UtcNow;
        ErrorCode = code;
        ErrorMessage = message;
    }

    public void Retry()
    {
        Require(Status == "Failed", "只有失败任务可以重试");
        if (Attempt >= MaximumAttempts)
            throw new OperationFailure(FailureKind.Conflict, "RETRY_LIMIT", "已达到三次尝试上限；请排除故障后新建分析任务");
        Status = "Queued";
        ErrorCode = ErrorMessage = null;
        StartedAt = FinishedAt = null;
    }

    public void RecoverInterrupted()
    {
        Require(Status == "Running", "只有中断的运行任务可恢复");
        Fail("WORKER_INTERRUPTED", "工作进程中断，任务未完成");
        if (Attempt < MaximumAttempts) Retry();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new OperationFailure(FailureKind.Conflict, "INVALID_STATE", message);
    }
}
