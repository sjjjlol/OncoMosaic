using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace OncoMosaic;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)!;
}
public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class TissueImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string FileKey { get; set; } = "";
    public string AssayKey { get; set; } = "";
    public string AssaySha256 { get; set; } = "";
    public string AcquisitionJson { get; set; } = "{}";
    public string PreviewKey { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int BandCount { get; set; }
    public string WavelengthsJson { get; set; } = "[]";
    public double PixelSizeUm { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public record Rectangle(int X, int Y, int Width, int Height)
{
    public bool Contains(double x, double y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
    public bool Valid(int width, int height) => X >= 0 && Y >= 0 && Width > 0 && Height > 0 && (long)X + Width <= width && (long)Y + Height <= height;
}
public class Roi
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ImageId { get; set; }
    public string Name { get; set; } = "ROI";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Version { get; set; } = 1;
    public string RegionTag { get; set; } = "tumor-candidate";
    public Rectangle Rect() => new(X, Y, Width, Height);
}
public record Thresholds([property: System.Text.Json.Serialization.JsonRequired] double Panck,
                         [property: System.Text.Json.Serialization.JsonRequired] double Cd3,
                         [property: System.Text.Json.Serialization.JsonRequired] double Cd8)
{
    public bool Valid() => double.IsFinite(Panck) && double.IsFinite(Cd3) && double.IsFinite(Cd8) && Panck is >= 0 and <= 1 && Cd3 is >= 0 and <= 1 && Cd8 is >= 0 and <= 1;
}
public class AnalysisRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoiId { get; set; }
    public string Status { get; set; } = "Queued";
    public int Attempt { get; set; }
    public string ModelVersion { get; set; } = "spectral-mif-sim-v1";
    public string AlgorithmVersion { get; set; } = "quantification-v2";
    public string ThresholdsJson { get; set; } = Json.Write(new Thresholds(.35, .35, .35));
    public int ValidTissuePx { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public void Start()
    {
        if (Status != "Queued") throw new InvalidOperationException("只有排队任务可运行");
        Status = "Running"; Attempt++; StartedAt = DateTime.UtcNow; FinishedAt = null;
        ErrorCode = ErrorMessage = null;
    }
    public void Retry()
    {
        if (Status != "Failed") throw new ApiError(409, "INVALID_STATE", "只有失败任务可以重试");
        Status = "Queued"; ErrorCode = ErrorMessage = null; StartedAt = FinishedAt = null;
    }
}
public class Cell
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public int LocalIndex { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public int AreaPx { get; set; }
    public double DapiValue { get; set; }
    public double PanckValue { get; set; }
    public double Cd3Value { get; set; }
    public double Cd8Value { get; set; }
    public string QualityFlag { get; set; } = "ok";
    public string ContourJson { get; set; } = "[]";
}
public class ReviewRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class ReviewChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RevisionId { get; set; }
    public Guid CellId { get; set; }
    public string NewLabel { get; set; } = "negative";
    public string Reason { get; set; } = "";
}
public class Artifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public string Kind { get; set; } = "";
    public string FileKey { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TissueImage> Images => Set<TissueImage>();
    public DbSet<Roi> Rois => Set<Roi>();
    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();
    public DbSet<Cell> Cells => Set<Cell>();
    public DbSet<ReviewRevision> ReviewRevisions => Set<ReviewRevision>();
    public DbSet<ReviewChange> ReviewChanges => Set<ReviewChange>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<TissueImage>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        m.Entity<Roi>().HasOne<TissueImage>().WithMany().HasForeignKey(x => x.ImageId);
        m.Entity<AnalysisRun>().HasOne<Roi>().WithMany().HasForeignKey(x => x.RoiId);
        m.Entity<Cell>().HasOne<AnalysisRun>().WithMany().HasForeignKey(x => x.RunId);
        m.Entity<Cell>().HasIndex(x => new { x.RunId, x.LocalIndex }).IsUnique();
        m.Entity<ReviewRevision>().HasOne<AnalysisRun>().WithMany().HasForeignKey(x => x.RunId);
        m.Entity<ReviewRevision>().HasIndex(x => new { x.RunId, x.Version }).IsUnique();
        m.Entity<ReviewChange>().HasOne<ReviewRevision>().WithMany().HasForeignKey(x => x.RevisionId);
        m.Entity<ReviewChange>().HasOne<Cell>().WithMany().HasForeignKey(x => x.CellId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Artifact>().HasOne<AnalysisRun>().WithMany().HasForeignKey(x => x.RunId);
        m.Entity<Artifact>().Property(x => x.Kind).HasMaxLength(32);
        m.Entity<Artifact>().HasIndex(x => new { x.RunId, x.Kind }).IsUnique();
    }
}
public class ApiError(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public record InspectResult(int Width, int Height, int BandCount, double[] Wavelengths, double PixelSizeUm, string PreviewKey, JsonElement Acquisition);
public record Marker(string Name, string DisplayKey);
public record Manifest(Guid RunId, string ModelVersion, Rectangle Roi, int Width, int Height, Marker[] Markers, string MaskKey, string OverlayKey, string TissueKey, string QcKey, int ValidTissuePx, string CellsKey, int CellCount);
public record MeasuredCell(int LocalIndex, double X, double Y, int AreaPx, double DapiValue, double PanckValue, double Cd3Value, double Cd8Value, string QualityFlag, double[][] Contour);
public record RunInput(Guid ImageId, Guid RoiId, string ModelVersion, Thresholds Thresholds);
public record RoiInput(string Name, int X, int Y, int Width, int Height, string RegionTag);
public record ReviewInput(Guid CellId, string NewLabel, string? Reason);
public record ProjectInput(string Name);
