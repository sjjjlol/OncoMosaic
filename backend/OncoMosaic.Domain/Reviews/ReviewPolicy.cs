namespace OncoMosaic.Domain;

public static class ReviewPolicy
{
    public static void Validate(AnalysisRun run, Cell? cell, string dimension, string label, string? reason, int? baseVersion, int currentVersion)
    {
        if (dimension is not ("identity" or "ki67") || !(dimension == "ki67" ? Ki67Quantification.States.Contains(label) : Quantification.Labels.Contains(label)) || (reason?.Length ?? 0) > 2000)
            throw new OperationFailure(FailureKind.Invalid, "INVALID_REVIEW", "复核类别或原因无效");
        if (run.Status != "Succeeded" || cell == null || cell.RunId != run.Id)
            throw new OperationFailure(FailureKind.Conflict, "INVALID_REVIEW", "只能复核此成功任务中的细胞");
        if ((run.ModelVersion == Ki67Quantification.Model || dimension == "ki67") && baseVersion == null)
            throw new OperationFailure(FailureKind.Invalid, "REVIEW_VERSION_REQUIRED", "请提交当前复核版本");
        if (baseVersion != null && baseVersion != currentVersion)
            throw new OperationFailure(FailureKind.Conflict, "REVIEW_CONFLICT", "复核版本已变化，请载入最新结果后重试");
        if (dimension != "ki67") return;
        if (cell.Ki67Value == null || cell.Ki67Quality != "ok" || cell.QualityFlag != "ok" || Json.Read<Thresholds>(run.ThresholdsJson).Ki67 == null)
            throw new OperationFailure(FailureKind.Conflict, "KI67_NOT_REVIEWABLE", "未检测或质控不通过，不能通过复核伪造有效信号");
        if (string.IsNullOrWhiteSpace(reason))
            throw new OperationFailure(FailureKind.Invalid, "REASON_REQUIRED", "Ki-67 状态修订需要原因");
    }
}
