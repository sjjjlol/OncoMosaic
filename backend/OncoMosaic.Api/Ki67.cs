using System.Globalization;
using System.Text;
namespace OncoMosaic;

public record Ki67View(double? Value, string Unit, string Compartment, int ValidPixelCount, string Quality, string AutoState, string EffectiveState);
public record Ki67Summary(int PositiveCount, int NegativeCount, int IndeterminateCount, int NotMeasuredCount, int EvaluableCount, int TargetCount, int ExcludedCount, int IdentityUnclassifiedCount, double? Fraction, double? Coverage, double? PositiveDensity, string Status, string? Reason);
public record Ki67Comparison(bool Comparable, string[] Reasons, Dictionary<string, double?> DifferencePercentagePoints);

public static class Ki67Quantification
{
    public const string Model = "spectral-mif-sim-v2";
    public const string Algorithm = "quantification-v3";
    public static readonly string[] States = ["positive", "negative", "indeterminate"];
    public static readonly string[] Populations = ["panck", "cd3", "cd3-cd8", "all"];
    // These are simulation review gates, not medical scoring standards.
    public const int MinimumEvaluable = 10;
    public const double MinimumCoverage = .8;
    public static object Method(Thresholds thresholds) => new { measurementVersion = "ki67-nuclear-mean-v1", feature = "mean", compartment = "nucleus", unit = "simulated-relative-intensity", threshold = thresholds.Ki67, comparison = ">=", thresholdSource = "user-selected-simulation", scoringScope = "roi-total", minimumEvaluable = MinimumEvaluable, minimumCoverage = MinimumCoverage, fraction = "positive / (positive + negative); not-measured and indeterminate excluded; coverage reported", reviewActor = "local-user (no authentication)" };
    public static Ki67View View(Cell cell, Thresholds thresholds, string? reviewed)
    {
        var state = cell.Ki67Value == null ? "not-measured" : cell.QualityFlag != "ok" || cell.Ki67Quality != "ok" || thresholds.Ki67 == null ? "indeterminate" : cell.Ki67Value >= thresholds.Ki67 ? "positive" : "negative";
        return new(cell.Ki67Value, "simulated-relative-intensity", "nucleus", cell.Ki67ValidPixelCount, cell.Ki67Quality, state,
            state == "not-measured" || cell.QualityFlag != "ok" || cell.Ki67Quality != "ok" ? state : reviewed ?? state);
    }
    public static bool InPopulation(CellView c, string population) => population switch
    {
        "all" => true,
        "cd3" => c.EffectiveLabels is "cd3" or "cd3-cd8",
        _ => c.EffectiveLabels == population
    };
    public static Dictionary<string, Ki67Summary> Summarize(List<CellView> cells, double area)
    {
        var result = new Dictionary<string, Ki67Summary>();
        foreach (var population in Populations)
        {
            var group = cells.Where(c => c.EffectiveLabels != "excluded" && c.QualityFlag == "ok" && InPopulation(c, population)).ToList();
            int Count(string state) => group.Count(c => (c.Ki67?.EffectiveState ?? "not-measured") == state);
            var p = Count("positive"); var n = Count("negative"); var u = Count("indeterminate"); var m = Count("not-measured"); var e = p+n; var t = group.Count;
            var status = t == 0 ? "empty" : m == t ? "not-measured" : e == 0 ? "unavailable" : e < MinimumEvaluable || (double)e/t < MinimumCoverage ? "exploratory" : "ok";
            var reason = status switch { "empty" => "目标对象群为空", "not-measured" => "未检测 Ki-67", "unavailable" => "没有可评价的 Ki-67 对象，请查看质量原因", "exploratory" => "可评价对象少于 10 或覆盖率低于 80%；仅供模拟探索", _ => null };
            result[population] = new(p,n,u,m,e,t,cells.Count(c => c.EffectiveLabels == "excluded" || c.QualityFlag != "ok"), cells.Count(c => c.EffectiveLabels == "unclassified" && c.QualityFlag == "ok"), e == 0 ? null : (double)p/e, t == 0 ? null : (double)e/t, e == 0 || area <= 0 ? null : p/area, status, reason);
        }
        return result;
    }
    public static Ki67Comparison Compare(ResultSnapshot a, ResultSnapshot b)
    {
        var reasons = new List<string>();
        if (a.Run.ModelVersion != Model || b.Run.ModelVersion != Model || a.Summary.Thresholds.Ki67 == null || b.Summary.Thresholds.Ki67 == null) reasons.Add("缺少 Ki-67 分析或通道");
        if (a.Run.ModelVersion != b.Run.ModelVersion || a.Run.AlgorithmVersion != b.Run.AlgorithmVersion || a.Summary.Thresholds != b.Summary.Thresholds) reasons.Add("模型、统计或阈值方案不同");
        if (a.Image.AssaySha256 != b.Image.AssaySha256) reasons.Add("面板或校准资料不同");
        var differences = Populations.ToDictionary(p => p, p => reasons.Count == 0 && a.Summary.Ki67?[p].Fraction is double av && b.Summary.Ki67?[p].Fraction is double bv ? (double?)((av-bv)*100) : null);
        return new(reasons.Count == 0, reasons.ToArray(), differences);
    }
    public static string CellCsv(IEnumerable<CellView> cells, int version)
    {
        var csv = new StringBuilder("cell_id,local_index,ki67_nuclear_mean,unit,compartment,valid_pixels,auto_state,effective_state,quality,identity,excluded,review_version\n");
        foreach (var c in cells)
        {
            var k = c.Ki67;
            csv.AppendLine(string.Join(',', c.CellId,c.LocalIndex,k?.Value?.ToString("R",CultureInfo.InvariantCulture) ?? "",k?.Unit,k?.Compartment,k?.ValidPixelCount,k?.AutoState ?? "not-measured",k?.EffectiveState ?? "not-measured",k?.Quality ?? "not-measured",c.EffectiveLabels,c.EffectiveLabels == "excluded" || c.QualityFlag != "ok",version));
        }
        return csv.ToString();
    }
    public static string SummaryCsv(Summary summary)
    {
        var csv = new StringBuilder("population,positive,negative,indeterminate,not_measured,evaluable,target,excluded,identity_unclassified,fraction,coverage,positive_density,status,reason,review_version\n");
        foreach (var (population,q) in summary.Ki67 ?? [])
            csv.AppendLine(string.Join(',',new object?[]{population,q.PositiveCount,q.NegativeCount,q.IndeterminateCount,q.NotMeasuredCount,q.EvaluableCount,q.TargetCount,q.ExcludedCount,q.IdentityUnclassifiedCount,q.Fraction,q.Coverage,q.PositiveDensity,q.Status,q.Reason,summary.ReviewVersion}.Select(v => v is IFormattable f ? f.ToString(null,CultureInfo.InvariantCulture) : v?.ToString() ?? "")));
        return csv.ToString();
    }
}
