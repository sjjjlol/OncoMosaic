namespace OncoMosaic.Domain;

public static class Comparison
{
    public static Exploration Explore(ResultSnapshot s) => new(s.Run.Id, s.Run.ModelVersion, s.Run.AlgorithmVersion, s.Roi, s.Summary, s.Cells, Quantification.Neighbors(s.Cells, s.Roi, s.Image.PixelSizeUm), s.Reviews);

    public static ComparisonResult Build(Guid imageId, ResultSnapshot a, ResultSnapshot b)
    {
        if (a.Image.Id != imageId || b.Image.Id != imageId)
            throw new OperationFailure(FailureKind.Invalid, "COMPARISON_IMAGE_MISMATCH", "请选择当前图像中的两次分析结果");
        if (a.Roi.Id == b.Roi.Id)
            throw new OperationFailure(FailureKind.Invalid, "COMPARISON_SAME_ROI", "请选择两个不同的 ROI；同一区域的参数比较不属于区域比较");
        var warnings = new List<string>();
        var sameScheme = a.Run.ModelVersion == b.Run.ModelVersion && a.Run.AlgorithmVersion == b.Run.AlgorithmVersion && a.Summary.Thresholds == b.Summary.Thresholds;
        if (!sameScheme) warnings.Add("两侧模型、统计版本或阳性阈值不一致。建议用同一方案重新分析，不能把数值差异直接归因于区域差异。");
        if (a.Roi.X < b.Roi.X + b.Roi.Width && b.Roi.X < a.Roi.X + a.Roi.Width && a.Roi.Y < b.Roi.Y + b.Roi.Height && b.Roi.Y < a.Roi.Y + a.Roi.Height)
            warnings.Add("两个 ROI 有重叠，可能包含相同组织和对象；不能合并计数或作为独立样本。");
        warnings.Add("距离仅在各自 ROI 内配对，未做边缘校正；本比较描述局部组成与空间关系，不推断细胞功能或疗效。");
        return new(imageId, a.Image.Sha256, a.Image.AssaySha256, a.Image.PixelSizeUm, Explore(a), Explore(b), sameScheme, warnings.ToArray(), Ki67Quantification.Compare(a, b));

    }

}
