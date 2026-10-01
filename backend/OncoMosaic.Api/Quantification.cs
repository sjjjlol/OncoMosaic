namespace OncoMosaic;

public record CellView(Guid CellId, int LocalIndex, double X, double Y, int AreaPx, object Intensities, string AutoLabels, string EffectiveLabels, string QualityFlag, double[][] Contour);
public record Counts(int Total, int Valid, int Excluded, int Unclassified, int Panck, int Cd3Cd8, int Cd3Only, int Negative);
public record Summary(Counts Counts, object Denominators, double AreaMm2, object Units, Thresholds Thresholds, int ReviewVersion, double? PanckFraction, double? Cd3Cd8Fraction, double PanckDensity, double Cd3Cd8Density, double? MeanNearestDistanceUm, double[] NearestDistancesUm, string RegionTag, string Notice);
public record NearestNeighbor(Guid SourceCellId, int SourceIndex, double SourceX, double SourceY, Guid TargetCellId, int TargetIndex, double TargetX, double TargetY, double DistanceUm);
public static class Quantification
{
    public const string Notice = "合成光谱 mIF 数据 / 确定性模拟分析 / 无医学性能验证 / 非诊断用途";
    public static readonly string[] Labels = ["negative", "panck", "cd3-cd8", "cd3", "unclassified", "excluded"];
    public static string Auto(Cell c, Thresholds t)
    {
        if (c.QualityFlag != "ok") return "unclassified";
        var epithelial = c.PanckValue >= t.Panck;
        var cd3 = c.Cd3Value >= t.Cd3;
        var cd8 = c.Cd8Value >= t.Cd8;
        if ((epithelial && (cd3 || cd8)) || (cd8 && !cd3)) return "unclassified";
        if (epithelial) return "panck";
        if (cd3 && cd8) return "cd3-cd8";
        if (cd3) return "cd3";
        return "negative";
    }
    public static List<CellView> Views(IEnumerable<Cell> cells, Thresholds thresholds, IReadOnlyDictionary<Guid, string> changes) => cells.Select(c =>
        new CellView(c.Id, c.LocalIndex, c.X, c.Y, c.AreaPx, new { dapi = c.DapiValue, panck = c.PanckValue, cd3 = c.Cd3Value, cd8 = c.Cd8Value }, Auto(c, thresholds), changes.GetValueOrDefault(c.Id, Auto(c, thresholds)), c.QualityFlag, Json.Read<double[][]>(c.ContourJson))).ToList();
    public static List<NearestNeighbor> Neighbors(List<CellView> cells, Roi roi, double pixelSize)
    {
        var inside = cells.Where(c => roi.Rect().Contains(c.X, c.Y)).ToList();
        var targets = inside.Where(c => c.EffectiveLabels == "panck").OrderBy(c => c.LocalIndex).ThenBy(c => c.CellId).ToList();
        var pairs = new List<NearestNeighbor>();
        foreach (var source in inside.Where(c => c.EffectiveLabels == "cd3-cd8"))
        {
            var nearest = targets.Where(t => t.CellId != source.CellId)
                .Select(t => new { Cell = t, Distance = Math.Sqrt(Math.Pow(source.X-t.X, 2)+Math.Pow(source.Y-t.Y, 2)) * pixelSize })
                .OrderBy(t => t.Distance).FirstOrDefault();
            if (nearest is not null)
                pairs.Add(new(source.CellId, source.LocalIndex, source.X, source.Y, nearest.Cell.CellId, nearest.Cell.LocalIndex, nearest.Cell.X, nearest.Cell.Y, nearest.Distance));
        }
        return pairs;
    }
    public static Summary Calculate(List<CellView> cells, Roi roi, double pixelSize, int validTissuePx, Thresholds thresholds, int version)
    {
        var inside = cells.Where(c => roi.Rect().Contains(c.X, c.Y)).ToList();
        var valid = inside.Where(c => c.EffectiveLabels is not ("excluded" or "unclassified")).ToList();
        var panck = valid.Where(c => c.EffectiveLabels == "panck").ToList();
        var cd3cd8 = valid.Where(c => c.EffectiveLabels == "cd3-cd8").ToList();
        var distances = Neighbors(cells, roi, pixelSize).Select(p => p.DistanceUm).ToArray();
        var area = validTissuePx * Math.Pow(pixelSize / 1000, 2);
        if (area <= 0) throw new InvalidOperationException("有效组织面积必须大于零");
        var counts = new Counts(inside.Count, valid.Count, inside.Count(c => c.EffectiveLabels == "excluded"), inside.Count(c => c.EffectiveLabels == "unclassified"), panck.Count, cd3cd8.Count, valid.Count(c => c.EffectiveLabels == "cd3"), valid.Count(c => c.EffectiveLabels == "negative"));
        return new(counts, new { positiveFraction = valid.Count, densityAreaMm2 = area, distanceSource = cd3cd8.Count, distanceTarget = panck.Count, excludedAreaPx = roi.Width * roi.Height - validTissuePx }, area, new { area = "mm²", density = "objects/mm²", distance = "µm" }, thresholds, version, valid.Count == 0 ? null : (double)panck.Count / valid.Count, valid.Count == 0 ? null : (double)cd3cd8.Count / valid.Count, panck.Count / area, cd3cd8.Count / area, distances.Length == 0 ? null : distances.Average(), distances, roi.RegionTag, Notice);
    }
}
