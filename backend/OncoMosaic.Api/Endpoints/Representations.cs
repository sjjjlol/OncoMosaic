namespace OncoMosaic.Api;

internal static class Representations
{
    public static object Image(TissueImage image) => new
    {
        image.Id,
        image.ProjectId,
        capabilities = new { ki67 = AnalysisScheme.HasKi67(image) },
        image.Name,
        image.Description,
        image.Width,
        image.Height,
        image.BandCount,
        image.PixelSizeUm,
        wavelengths = Json.Read<double[]>(image.WavelengthsJson),
        acquisition = Json.Read<object>(image.AcquisitionJson),
        image.Sha256,
        image.AssaySha256,
        image.CreatedAt,
        previewUrl = $"/api/images/{image.Id}/preview",
        notice = Quantification.Notice
    };
    public static object Run(AnalysisRun run) => new
    {
        runId = run.Id,
        run.RoiId,
        run.Status,
        run.Attempt,
        run.CreatedAt,
        run.StartedAt,
        run.FinishedAt,
        run.ModelVersion,
        run.AlgorithmVersion,
        thresholds = Json.Read<Thresholds>(run.ThresholdsJson),
        error = run.ErrorCode == null ? null : new { code = run.ErrorCode, message = run.ErrorMessage }
    };
}
