namespace OncoMosaic.Domain;

public static class AnalysisScheme
{
    public const string LegacyModel = "spectral-mif-sim-v1";
    public static bool HasKi67(TissueImage image) => Json.Read<System.Text.Json.JsonElement>(image.AcquisitionJson)
        .TryGetProperty("markers", out var markers) && markers.EnumerateArray().Any(m => m.GetString() == "Ki67");

    public static AnalysisRun Create(TissueImage image, Roi roi, Guid requestedImageId, string model, Thresholds? thresholds)
    {
        if (roi.ImageId != image.Id || image.Id != requestedImageId || model is not (LegacyModel or Ki67Quantification.Model) || thresholds == null || !thresholds.Valid())
            throw new OperationFailure(FailureKind.Invalid, "INVALID_ANALYSIS", "图像、模型版本或阈值无效；阈值必须在 0–1 内");
        var ki67 = HasKi67(image);
        if ((ki67 && model != Ki67Quantification.Model) || ki67 != (thresholds.Ki67 != null))
            throw new OperationFailure(FailureKind.Invalid, "INVALID_KI67_SCHEME", "Ki-67 面板须使用 v2 模拟模型并提供 Ki-67 阈值；未检测面板不可提交该阈值");
        return new AnalysisRun { RoiId = roi.Id, ModelVersion = model, AlgorithmVersion = model == Ki67Quantification.Model ? Ki67Quantification.Algorithm : "quantification-v2", ThresholdsJson = Json.Write(thresholds) };
    }
}
