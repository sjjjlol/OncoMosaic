using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png.Chunks;
namespace OncoMosaic.Infrastructure;

public class ResultExporter(AppDb db, FileStore store) : IResultExporter
{
    public async Task<byte[]> Export(ResultSnapshot s, int exportSchemaVersion, CancellationToken ct)
    {
        if (exportSchemaVersion is not (1 or 2)) throw new OperationFailure(FailureKind.Invalid, "INVALID_EXPORT_VERSION", "导出版本只支持 1 或 2");
        var id = s.Run.Id;
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            void Text(string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(true)); writer.Write(content); }
            string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
            var csv = new StringBuilder("cellId,localIndex,x_px,y_px,area_px,dapi_nuclear,panck_near_nucleus,cd3_near_nucleus,cd8_near_nucleus,auto_label,effective_label,quality_flag,review_version,notice\n");
            foreach (var c in s.Cells)
            {
                var values = Json.Read<Dictionary<string, double>>(Json.Write(c.Intensities));
                csv.AppendLine(string.Join(',', c.CellId, c.LocalIndex, N(c.X), N(c.Y), c.AreaPx, N(values["dapi"]), N(values["panck"]), N(values["cd3"]), N(values["cd8"]), c.AutoLabels, c.EffectiveLabels, c.QualityFlag, s.Summary.ReviewVersion, Quantification.Notice));
            }
            Text("cells.csv", csv.ToString());
            if (exportSchemaVersion == 2)
            {
                Text("ki67-cells.csv", Ki67Quantification.CellCsv(s.Cells, s.Summary.ReviewVersion));
                Text("ki67-summary.csv", Ki67Quantification.SummaryCsv(s.Summary));
                foreach (var kind in new[] { "Ki67", "Ki67-quantitative", "mask" })
                {
                    var artifact = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(a => a.RunId == id && a.Kind == kind, ct);
                    if (artifact == null) continue;
                    await using var source = File.OpenRead(store.Existing(artifact.FileKey));
                    await using var destination = zip.CreateEntry(kind + (kind == "Ki67" ? ".png" : ".npy")).Open();
                    await source.CopyToAsync(destination, ct);
                }
            }
            var q = s.Summary;
            Text("roi-summary.csv", "run_id,review_version,total,valid,excluded,unclassified,panck,cd3_cd8,cd3_only,negative,valid_tissue_area_mm2,panck_fraction,cd3_cd8_fraction,panck_density,cd3_cd8_density,mean_nearest_distance_um,notice\n" + string.Join(',', id, q.ReviewVersion, q.Counts.Total, q.Counts.Valid, q.Counts.Excluded, q.Counts.Unclassified, q.Counts.Panck, q.Counts.Cd3Cd8, q.Counts.Cd3Only, q.Counts.Negative, N(q.AreaMm2), q.PanckFraction is double pf ? N(pf) : "", q.Cd3Cd8Fraction is double cf ? N(cf) : "", N(q.PanckDensity), N(q.Cd3Cd8Density), q.MeanNearestDistanceUm is double d ? N(d) : "", Quantification.Notice) + "\n");
            var qc = Json.Read<object>(File.ReadAllText(store.Existing((await db.Artifacts.AsNoTracking().SingleAsync(a => a.RunId == id && a.Kind == "qc", ct)).FileKey)));
            Text("qc.json", Json.Write(qc));
            Text("method.json", Json.Write(new { exportSchemaVersion, ki67Method = exportSchemaVersion == 2 ? Ki67Quantification.Method(q.Thresholds) : null, notice = Quantification.Notice, runId = id, imageSha256 = s.Image.Sha256, assaySha256 = s.Image.AssaySha256, acquisition = Json.Read<object>(s.Image.AcquisitionJson), roi = s.Roi, s.Image.PixelSizeUm, wavelengthsNm = Json.Read<double[]>(s.Image.WavelengthsJson), s.Run.ModelVersion, s.Run.AlgorithmVersion, q.Thresholds, q.ReviewVersion, validTissuePx = s.Run.ValidTissuePx, generatedAt = DateTime.UtcNow, measurements = new { dapi = "nuclear mean", markers = "nucleus plus 3px dilation excluding neighboring nuclei; approximate, not whole-cell segmentation", unmix = "fit supplied simulated single-stain spectra plus autofluorescence", tissue = "autofluorescence threshold and saturation exclusion; valid tissue denominator", distance = "CD3+CD8+ candidate to nearest distinct panCK+ object" }, summary = q, reviews = s.Reviews }));
            using var overlay = Image.Load<Rgba32>(store.Existing(s.Image.PreviewKey));
            overlay.Metadata.GetPngMetadata().TextData.Add(new PngTextData("Description", "Synthetic spectral mIF / Simulated analysis / NOT FOR DIAGNOSIS", "", ""));
            overlay.Metadata.GetPngMetadata().TextData.Add(new PngTextData("Analysis", $"runId={id}; reviewVersion={q.ReviewVersion}", "", ""));
            static Rgba32 Color(string label) => label switch { "panck" => new(255, 183, 77), "cd3-cd8" => new(66, 225, 167), "cd3" => new(242, 132, 228), "unclassified" => new(200, 160, 100), "excluded" => new(150, 150, 150), _ => new(116, 172, 255) };
            void Dot(int x, int y, Rgba32 color) { if (x >= 0 && y >= 0 && x < overlay.Width && y < overlay.Height) overlay[x, y] = color; }
            foreach (var c in s.Cells)
            {
                var color = Color(c.EffectiveLabels);
                foreach (var p in c.Contour) Dot((int)Math.Round(p[0]), (int)Math.Round(p[1]), color);
                if (c.EffectiveLabels == "excluded") for (int i = -3; i <= 3; i++) { Dot((int)c.X + i, (int)c.Y + i, color); Dot((int)c.X + i, (int)c.Y - i, color); }
            }
            for (int x = s.Roi.X; x < s.Roi.X + s.Roi.Width; x++) { Dot(x, s.Roi.Y, new(255, 255, 255)); Dot(x, s.Roi.Y + s.Roi.Height - 1, new(255, 255, 255)); }
            for (int y = s.Roi.Y; y < s.Roi.Y + s.Roi.Height; y++) { Dot(s.Roi.X, y, new(255, 255, 255)); Dot(s.Roi.X + s.Roi.Width - 1, y, new(255, 255, 255)); }
            await using var entry = zip.CreateEntry("overlay.png").Open(); await overlay.SaveAsPngAsync(entry, ct);
        }
        return memory.ToArray();
    }
}
