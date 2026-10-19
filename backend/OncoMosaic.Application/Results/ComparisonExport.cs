using System.Globalization;
using System.IO.Compression;
using System.Text;
namespace OncoMosaic.Application;

public static class ComparisonExport
{
    public static byte[] Export(ComparisonResult comparison, int exportSchemaVersion = 1)
    {
        if (exportSchemaVersion is not (1 or 2)) throw new OperationFailure(FailureKind.Invalid, "INVALID_EXPORT_VERSION", "导出版本只支持 1 或 2");
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
            Text("method.json", Json.Write(new
            {
                schema = exportSchemaVersion == 2 ? "roi-comparison-v2" : "roi-comparison-v1",
                exportSchemaVersion,
                ki67 = exportSchemaVersion == 2 ? comparison.Ki67 : null,
                ki67MethodA = exportSchemaVersion == 2 ? Ki67Quantification.Method(comparison.A.Summary.Thresholds) : null,
                ki67MethodB = exportSchemaVersion == 2 ? Ki67Quantification.Method(comparison.B.Summary.Thresholds) : null,
                notice = Quantification.Notice,
                comparison.ImageId,
                comparison.ImageSha256,
                comparison.AssaySha256,
                comparison.PixelSizeUm,
                comparison.SameScheme,
                comparison.Warnings,
                generatedAt = DateTime.UtcNow,
                a = new { comparison.A.RunId, comparison.A.Roi, comparison.A.ModelVersion, comparison.A.AlgorithmVersion, comparison.A.Summary, comparison.A.Reviews },
                b = new { comparison.B.RunId, comparison.B.Roi, comparison.B.ModelVersion, comparison.B.AlgorithmVersion, comparison.B.Summary, comparison.B.Reviews },
                definitions = new { coordinates = "original image pixels", measurements = "DAPI nuclear mean; panCK/CD3/CD8 nucleus plus 3px dilation excluding neighboring nuclei; approximate, not whole-cell segmentation", fraction = "all classifiable non-excluded objects", density = "objects per valid tissue mm2", distance = "CD3+CD8+ to nearest distinct panCK+ nuclear center within each ROI; no edge correction", ties = "lowest localIndex then cellId" }
            }));
        }
        return memory.ToArray();
    }
}
