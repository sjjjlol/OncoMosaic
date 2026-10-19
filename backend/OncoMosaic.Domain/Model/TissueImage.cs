using System.Text.Json;
namespace OncoMosaic.Domain;

public class TissueImage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string FileKey { get; init; } = "";
    public string AssayKey { get; init; } = "";
    public string AssaySha256 { get; init; } = "";
    public string AcquisitionJson { get; init; } = "{}";
    public string PreviewKey { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public int BandCount { get; init; }
    public string WavelengthsJson { get; init; } = "[]";
    public double PixelSizeUm { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
