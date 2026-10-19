namespace OncoMosaic.Api;

public sealed class AnalysisWorker(IServiceScopeFactory scopes, ILogger<AnalysisWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var recovered = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (!recovered)
                {
                    await scope.ServiceProvider.GetRequiredService<IAnalysisRepository>().RecoverInterrupted(ct);
                    recovered = true;
                    logger.LogInformation("Interrupted task recovery completed; single worker deployment required");
                }
                var processor = scope.ServiceProvider.GetRequiredService<AnalysisProcessor>();
                if (!await processor.ProcessNext(ct)) await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                recovered = false;
                logger.LogError(error, "Analysis worker cycle failed for {RunId}; persisted task state determines recovery", error.Data["RunId"]);
                try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            }
        }
    }
}
