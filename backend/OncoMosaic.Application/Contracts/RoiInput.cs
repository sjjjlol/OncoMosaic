using System.Text.Json;
namespace OncoMosaic.Application;

public record RoiInput(string Name, int X, int Y, int Width, int Height, string RegionTag);
