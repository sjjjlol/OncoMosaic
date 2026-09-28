using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
namespace OncoMosaic;

public record ResultSnapshot(AnalysisRun Run, Roi Roi, TissueImage Image, List<CellView> Cells, Summary Summary, object[] Reviews);
public class ResultService(AppDb db, FileStore store)
{
    public async Task<ResultSnapshot> Snapshot(Guid id, int? reviewVersion, CancellationToken ct)
    {
        var run = await db.AnalysisRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new ApiError(404, "NOT_FOUND", "任务不存在");
        if (run.Status != "Succeeded") throw new ApiError(409, "NOT_READY", "分析尚未成功，无法读取结果");
        var roi = await db.Rois.AsNoTracking().SingleAsync(r => r.Id == run.RoiId, ct);
        var image = await db.Images.AsNoTracking().SingleAsync(i => i.Id == roi.ImageId, ct);
        var revisions = await db.ReviewRevisions.AsNoTracking().Where(r => r.RunId == id).OrderBy(r => r.Version).ToListAsync(ct);
        var version = reviewVersion ?? revisions.LastOrDefault()?.Version ?? 0;
        if (version < 0 || (version > 0 && !revisions.Any(r => r.Version == version))) throw new ApiError(404, "VERSION_NOT_FOUND", "复核版本不存在");
        var changes = await (from c in db.ReviewChanges.AsNoTracking() join r in db.ReviewRevisions.AsNoTracking() on c.RevisionId equals r.Id where r.RunId == id && r.Version <= version orderby r.Version select new { c.CellId, c.NewLabel, c.Reason, r.Version, r.CreatedAt }).ToListAsync(ct);
        var effective = new Dictionary<Guid, string>();
        foreach (var c in changes) effective[c.CellId] = c.NewLabel;
        var cells = await db.Cells.AsNoTracking().Where(c => c.RunId == id).OrderBy(c => c.LocalIndex).ToListAsync(ct);
        var thresholds = Json.Read<Thresholds>(run.ThresholdsJson);
        var views = Quantification.Views(cells, thresholds, effective);
        return new(run, roi, image, views, Quantification.Calculate(views, roi, image.PixelSizeUm, thresholds, version), changes.Cast<object>().ToArray());
    }
    public async Task<byte[]> Export(Guid id, int? version, CancellationToken ct)
    {
        var s = await Snapshot(id, version, ct);
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            void Text(string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(true)); writer.Write(content); }
            string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
            var csv = new StringBuilder("cellId,localIndex,x_px,y_px,area_px,dapi_nuclear,panck_perinuclear,cd8_perinuclear,auto_label,effective_label,quality_flag,review_version\n");
            foreach (var c in s.Cells)
            {
                var values = Json.Read<Dictionary<string, double>>(Json.Write(c.Intensities));
                csv.AppendLine(string.Join(',', c.CellId, c.LocalIndex, N(c.X), N(c.Y), c.AreaPx, N(values["dapi"]), N(values["panck"]), N(values["cd8"]), c.AutoLabels, c.EffectiveLabels, c.QualityFlag, s.Summary.ReviewVersion));
            }
            Text("cells.csv", csv.ToString());
            var q = s.Summary;
            Text("roi-summary.csv", "run_id,review_version,total,valid,excluded,panck,cd8,double_positive,panck_only,cd8_only,negative,area_mm2,panck_fraction,cd8_fraction,panck_density,cd8_density,mean_nearest_distance_um\n" + string.Join(',', id, q.ReviewVersion, q.Counts.Total, q.Counts.Valid, q.Counts.Excluded, q.Counts.Panck, q.Counts.Cd8, q.Counts.DoublePositive, q.Counts.PanckOnly, q.Counts.Cd8Only, q.Counts.Negative, N(q.AreaMm2), q.PanckFraction is double pf ? N(pf) : "", q.Cd8Fraction is double cf ? N(cf) : "", N(q.PanckDensity), N(q.Cd8Density), q.MeanNearestDistanceUm is double d ? N(d) : "") + "\n");
            Text("method.json", Json.Write(new { notice = Quantification.Notice, runId = id, imageSha256 = s.Image.Sha256, roi = s.Roi, s.Image.PixelSizeUm, wavelengthsNm = Json.Read<double[]>(s.Image.WavelengthsJson), s.Run.ModelVersion, s.Run.AlgorithmVersion, q.Thresholds, q.ReviewVersion, generatedAt = DateTime.UtcNow, measurements = new { dapi = "nuclear mean", panckAndCd8 = "nucleus plus 3px dilation excluding neighboring nuclei; approximate, not cell segmentation", detection = "DAPI >= 0.32, 8-connected components, area 5..400px", unmix = "fixed equal-weight spectral groups, clip to [0,1]", display = "linear 0..1 to 8-bit; never used for statistics", distance = "CD8-positive to nearest panCK-positive; self included for double-positive objects" }, summary = q, reviews = s.Reviews, overlayLegend = new { panck = "orange", cd8 = "green", doublePositive = "pink", negative = "blue", excluded = "gray cross" } }));
            using var overlay = Image.Load<Rgba32>(store.Existing(s.Image.PreviewKey));
            static Rgba32 Color(string label) => label switch { "panck" => new(255, 183, 77), "cd8" => new(66, 225, 167), "double-positive" => new(242, 132, 228), "excluded" => new(150, 150, 150), _ => new(116, 172, 255) };
            void Dot(int x, int y, Rgba32 color) { if (x >= 0 && y >= 0 && x < overlay.Width && y < overlay.Height) overlay[x, y] = color; }
            foreach (var c in s.Cells)
            {
                var color = Color(c.EffectiveLabels);
                foreach (var p in c.Contour) Dot((int)Math.Round(p[0]), (int)Math.Round(p[1]), color);
                if (c.EffectiveLabels == "excluded") for (int i = -3; i <= 3; i++) { Dot((int)c.X+i, (int)c.Y+i, color); Dot((int)c.X+i, (int)c.Y-i, color); }
            }
            for (int x = s.Roi.X; x < s.Roi.X+s.Roi.Width; x++) { Dot(x, s.Roi.Y, new(255, 255, 255)); Dot(x, s.Roi.Y+s.Roi.Height-1, new(255, 255, 255)); }
            for (int y = s.Roi.Y; y < s.Roi.Y+s.Roi.Height; y++) { Dot(s.Roi.X, y, new(255, 255, 255)); Dot(s.Roi.X+s.Roi.Width-1, y, new(255, 255, 255)); }
            await using var entry = zip.CreateEntry("overlay.png").Open(); await overlay.SaveAsPngAsync(entry, ct);
        }
        return memory.ToArray();
    }
}
