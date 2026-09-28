namespace OncoMosaic;

public record CellView(Guid CellId, int LocalIndex, double X, double Y, int AreaPx, object Intensities, string AutoLabels, string EffectiveLabels, string QualityFlag, double[][] Contour);
public record Counts(int Total, int Valid, int Excluded, int Panck, int Cd8, int DoublePositive, int PanckOnly, int Cd8Only, int Negative);
public record Summary(Counts Counts, object Denominators, double AreaMm2, object Units, Thresholds Thresholds, int ReviewVersion, double? PanckFraction, double? Cd8Fraction, double PanckDensity, double Cd8Density, double? MeanNearestDistanceUm, double[] NearestDistancesUm, string RegionTag, string Notice);
public static class Quantification
{
    public const string Notice = "合成演示数据 / 模拟模型 / 非诊断用途";
    public static readonly string[] Labels = ["negative", "panck", "cd8", "double-positive", "excluded"];
    public static string Auto(Cell c, Thresholds t) => (c.PanckValue >= t.Panck, c.Cd8Value >= t.Cd8) switch
    {
        (true, true) => "double-positive", (true, false) => "panck", (false, true) => "cd8", _ => "negative"
    };
    public static List<CellView> Views(IEnumerable<Cell> cells, Thresholds thresholds, IReadOnlyDictionary<Guid, string> changes) => cells.Select(c =>
        new CellView(c.Id, c.LocalIndex, c.X, c.Y, c.AreaPx, new { dapi = c.DapiValue, panck = c.PanckValue, cd8 = c.Cd8Value }, Auto(c, thresholds), changes.GetValueOrDefault(c.Id, Auto(c, thresholds)), c.QualityFlag, Json.Read<double[][]>(c.ContourJson))).ToList();
    public static Summary Calculate(List<CellView> cells, Roi roi, double pixelSize, Thresholds thresholds, int version)
    {
        var inside = cells.Where(c => roi.Rect().Contains(c.X, c.Y)).ToList();
        var valid = inside.Where(c => c.EffectiveLabels != "excluded").ToList();
        var panck = valid.Where(c => c.EffectiveLabels is "panck" or "double-positive").ToList();
        var cd8 = valid.Where(c => c.EffectiveLabels is "cd8" or "double-positive").ToList();
        var distances = panck.Count == 0 ? [] : cd8.Select(c => panck.Min(p => Math.Sqrt(Math.Pow(c.X-p.X, 2)+Math.Pow(c.Y-p.Y, 2))) * pixelSize).ToArray();
        var area = (double)roi.Width * roi.Height * Math.Pow(pixelSize / 1000, 2);
        var counts = new Counts(inside.Count, valid.Count, inside.Count-valid.Count, panck.Count, cd8.Count, valid.Count(c => c.EffectiveLabels == "double-positive"), valid.Count(c => c.EffectiveLabels == "panck"), valid.Count(c => c.EffectiveLabels == "cd8"), valid.Count(c => c.EffectiveLabels == "negative"));
        return new(counts, new { positiveFraction = valid.Count, densityAreaMm2 = area, distanceSource = cd8.Count, distanceTarget = panck.Count }, area, new { area = "mm²", density = "cells/mm²", distance = "µm" }, thresholds, version, valid.Count == 0 ? null : (double)panck.Count / valid.Count, valid.Count == 0 ? null : (double)cd8.Count / valid.Count, panck.Count / area, cd8.Count / area, distances.Length == 0 ? null : distances.Average(), distances, roi.RegionTag, Notice);
    }
}
