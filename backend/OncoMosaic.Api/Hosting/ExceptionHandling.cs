using Microsoft.EntityFrameworkCore;
namespace OncoMosaic.Api;

public static class ExceptionHandling
{
    public static void UseOperationFailures(this WebApplication app) => app.Use(async (context, next) =>
    {
        try { await next(); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error)
        {
            var (status, code, message) = error switch
            {
                OperationFailure failure => (Status(failure.Kind), failure.Code, failure.Message),
                BadHttpRequestException => (400, "INVALID_REQUEST", "请求格式不正确或超过大小限制"),
                HttpRequestException => (503, "INFERENCE_UNAVAILABLE", "Python 服务不可用，请稍后重试"),
                OperationCanceledException => (504, "INFERENCE_TIMEOUT", "处理超时，请稍后重试"),
                DbUpdateException => (409, "CONFLICT", "记录冲突，请刷新后重试"),
                _ => (500, "INTERNAL_ERROR", "服务器处理失败，请查看 API 日志")
            };
            app.Logger.LogWarning(error, "Request {TraceId} failed: {Code}", context.TraceIdentifier, code);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = status;
                await context.Response.WriteAsJsonAsync(new { code, message, traceId = context.TraceIdentifier });
            }
        }
    });

    public static int Status(FailureKind kind) => kind switch
    {
        FailureKind.Invalid => 400,
        FailureKind.NotFound => 404,
        FailureKind.Conflict => 409,
        FailureKind.TooLarge => 413,
        FailureKind.Rejected => 422,
        FailureKind.MissingArtifact => 424,
        FailureKind.Dependency => 502,
        FailureKind.Unavailable => 503,
        FailureKind.Timeout => 504,
        _ => 500
    };
}
