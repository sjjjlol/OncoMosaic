using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace OncoMosaic;

public record ResultSelection(Guid RunId, int? ReviewVersion);
public record ComparisonInput(ResultSelection A, ResultSelection B);
public record Exploration(Guid RunId, string ModelVersion, string AlgorithmVersion, Roi Roi, Summary Summary, List<CellView> Cells, List<NearestNeighbor> NearestNeighbors, object[] Reviews);
public record ComparisonResult(Guid ImageId, string ImageSha256, string AssaySha256, double PixelSizeUm, Exploration A, Exploration B, bool SameScheme, string[] Warnings, Ki67Comparison? Ki67 = null);

public static class Comparison
{
    public static Exploration Explore(ResultSnapshot s) => new(s.Run.Id, s.Run.ModelVersion, s.Run.AlgorithmVersion, s.Roi, s.Summary, s.Cells, Quantification.Neighbors(s.Cells, s.Roi, s.Image.PixelSizeUm), s.Reviews);

    public static ComparisonResult Build(Guid imageId, ResultSnapshot a, ResultSnapshot b)
    {
        if (a.Image.Id != imageId || b.Image.Id != imageId)
            throw new ApiError(400, "COMPARISON_IMAGE_MISMATCH", "请选择当前图像中的两次分析结果");
        if (a.Roi.Id == b.Roi.Id)
            throw new ApiError(400, "COMPARISON_SAME_ROI", "请选择两个不同的 ROI；同一区域的参数比较不属于区域比较");
        var warnings = new List<string>();
        var sameScheme = a.Run.ModelVersion == b.Run.ModelVersion && a.Run.AlgorithmVersion == b.Run.AlgorithmVersion && a.Summary.Thresholds == b.Summary.Thresholds;
        if (!sameScheme) warnings.Add("两侧模型、统计版本或阳性阈值不一致。建议用同一方案重新分析，不能把数值差异直接归因于区域差异。");
        if (a.Roi.X < b.Roi.X+b.Roi.Width && b.Roi.X < a.Roi.X+a.Roi.Width && a.Roi.Y < b.Roi.Y+b.Roi.Height && b.Roi.Y < a.Roi.Y+a.Roi.Height)
            warnings.Add("两个 ROI 有重叠，可能包含相同组织和对象；不能合并计数或作为独立样本。");
        warnings.Add("距离仅在各自 ROI 内配对，未做边缘校正；本比较描述局部组成与空间关系，不推断细胞功能或疗效。");
        return new(imageId, a.Image.Sha256, a.Image.AssaySha256, a.Image.PixelSizeUm, Explore(a), Explore(b), sameScheme, warnings.ToArray(), Ki67Quantification.Compare(a,b));

    }

    public static byte[] Export(ComparisonResult comparison, int exportSchemaVersion = 1)
    {
        if (exportSchemaVersion is not (1 or 2)) throw new ApiError(400, "INVALID_EXPORT_VERSION", "导出版本只支持 1 或 2");
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            string Row(params object?[] values) => string.Join(',', values.Select(v => "\"" + (v is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : v?.ToString() ?? "").Replace("\"", "\"\"") + "\"")) + "\n";
            void Text(string name, string value) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(true)); writer.Write(value); }
            var summary = new StringBuilder("side,roi_name,roi_id,run_id,review_version,total,valid,excluded,unclassified,panck,cd3_only,cd3_cd8,negative,area_mm2,panck_fraction,cd3_cd8_fraction,panck_density,cd3_cd8_density,mean_nearest_distance_um,pair_count\n");
            var neighbors = new StringBuilder("side,run_id,review_version,source_cell_id,source_index,target_cell_id,target_index,distance_um,source_x_px,source_y_px,target_x_px,target_y_px\n");
            foreach (var (name, side) in new[] { ("A", comparison.A), ("B", comparison.B) })
            {
                var q = side.Summary;
                summary.Append(Row(name, side.Roi.Name, side.Roi.Id, side.RunId, q.ReviewVersion, q.Counts.Total, q.Counts.Valid, q.Counts.Excluded, q.Counts.Unclassified, q.Counts.Panck, q.Counts.Cd3Only, q.Counts.Cd3Cd8, q.Counts.Negative, q.AreaMm2, q.PanckFraction, q.Cd3Cd8Fraction, q.PanckDensity, q.Cd3Cd8Density, q.MeanNearestDistanceUm, side.NearestNeighbors.Count));
                var cells = new StringBuilder("cell_id,local_index,x_px,y_px,nuclear_area_px,dapi,panck,cd3,cd8,auto_label,effective_label,quality_flag,review_version\n");
                foreach (var c in side.Cells)
                {
                    var values = Json.Read<Dictionary<string, double>>(Json.Write(c.Intensities));
                    cells.Append(Row(c.CellId, c.LocalIndex, c.X, c.Y, c.AreaPx, values["dapi"], values["panck"], values["cd3"], values["cd8"], c.AutoLabels, c.EffectiveLabels, c.QualityFlag, q.ReviewVersion));
                }
                Text($"cells-{name}.csv", cells.ToString());
                if (exportSchemaVersion == 2)
                {
                    Text($"ki67-cells-{name}.csv", Ki67Quantification.CellCsv(side.Cells, q.ReviewVersion));
                    Text($"ki67-summary-{name}.csv", Ki67Quantification.SummaryCsv(q));
                }
                foreach (var p in side.NearestNeighbors)
                    neighbors.Append(Row(name, side.RunId, q.ReviewVersion, p.SourceCellId, p.SourceIndex, p.TargetCellId, p.TargetIndex, p.DistanceUm, p.SourceX, p.SourceY, p.TargetX, p.TargetY));
            }
            Text("comparison-summary.csv", summary.ToString());
            Text("nearest-neighbors.csv", neighbors.ToString());
            Text("method.json", Json.Write(new { schema = exportSchemaVersion == 2 ? "roi-comparison-v2" : "roi-comparison-v1", exportSchemaVersion, ki67 = exportSchemaVersion == 2 ? comparison.Ki67 : null, ki67MethodA = exportSchemaVersion == 2 ? Ki67Quantification.Method(comparison.A.Summary.Thresholds) : null, ki67MethodB = exportSchemaVersion == 2 ? Ki67Quantification.Method(comparison.B.Summary.Thresholds) : null, notice = Quantification.Notice, comparison.ImageId, comparison.ImageSha256, comparison.AssaySha256, comparison.PixelSizeUm, comparison.SameScheme, comparison.Warnings, generatedAt = DateTime.UtcNow,
                a = new { comparison.A.RunId, comparison.A.Roi, comparison.A.ModelVersion, comparison.A.AlgorithmVersion, comparison.A.Summary, comparison.A.Reviews },
                b = new { comparison.B.RunId, comparison.B.Roi, comparison.B.ModelVersion, comparison.B.AlgorithmVersion, comparison.B.Summary, comparison.B.Reviews },
                definitions = new { coordinates = "original image pixels", measurements = "DAPI nuclear mean; panCK/CD3/CD8 nucleus plus 3px dilation excluding neighboring nuclei; approximate, not whole-cell segmentation", fraction = "all classifiable non-excluded objects", density = "objects per valid tissue mm2", distance = "CD3+CD8+ to nearest distinct panCK+ nuclear center within each ROI; no edge correction", ties = "lowest localIndex then cellId" } }));
        }
        return memory.ToArray();
    }
}

public class ComparisonService(AppDb db, ResultService results)
{
    public async Task<ComparisonResult> Create(Guid imageId, ComparisonInput input, CancellationToken ct)
    {
        if (input.A is null || input.B is null)
            throw new ApiError(400, "INVALID_COMPARISON", "需要区域 A 和 B 的分析任务与复核版本");
        // Both latest versions resolve within one database snapshot; exports later use explicit versions.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var a = await results.Snapshot(input.A.RunId, input.A.ReviewVersion, ct);
        var b = await results.Snapshot(input.B.RunId, input.B.ReviewVersion, ct);
        var comparison = Comparison.Build(imageId, a, b);
        await transaction.CommitAsync(ct);
        return comparison;
    }
}
