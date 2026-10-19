using System.Text.Json;
namespace OncoMosaic.Domain;

public class Roi
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ImageId { get; init; }
    public string Name { get; init; } = "ROI";
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Version { get; init; } = 1;
    public string RegionTag { get; init; } = "tumor-candidate";
    public Rectangle Rect() => new(X, Y, Width, Height);
}
