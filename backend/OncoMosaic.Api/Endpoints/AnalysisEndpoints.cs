namespace OncoMosaic.Api;

public static class AnalysisEndpoints
{
    public static void MapAnalysis(this WebApplication app)
    {
        app.MapGet("/api/images/{id:guid}/analysis-runs", async (Guid id, AnalysisService analysis, CancellationToken ct) => (await analysis.Runs(id, ct)).Select(Representations.Run));
        app.MapPost("/api/analysis-runs", async (RunInput input, AnalysisService analysis, CancellationToken ct) =>
        {
            var run = await analysis.Create(input, ct);
            return Results.Accepted($"/api/analysis-runs/{run.Id}", Representations.Run(run));
        });
        app.MapGet("/api/analysis-runs/{id:guid}", async (Guid id, AnalysisService analysis, CancellationToken ct) => Representations.Run(await analysis.Run(id, ct)));
        app.MapPost("/api/analysis-runs/{id:guid}/retry", async (Guid id, AnalysisService analysis, CancellationToken ct) => Results.Accepted($"/api/analysis-runs/{id}", Representations.Run(await analysis.Retry(id, ct))));
        app.MapGet("/api/analysis-runs/{id:guid}/artifacts/{kind}", async (Guid id, string kind, AnalysisService analysis, CancellationToken ct) => Results.File(await analysis.Artifact(id, kind, ct), kind is "mask" or "Ki67-quantitative" ? "application/octet-stream" : kind is "cells" or "qc" ? "application/json" : "image/png"));
        app.MapGet("/api/analysis-runs/{id:guid}/reviews", (Guid id, ReviewService reviews, CancellationToken ct) => reviews.List(id, ct));
        app.MapPost("/api/analysis-runs/{id:guid}/reviews", async (Guid id, ReviewInput input, ReviewService reviews, CancellationToken ct) => Results.Created($"/api/analysis-runs/{id}/reviews", new { reviewVersion = await reviews.Add(id, input, ct) }));
    }
}
