using System.Text.Json;
namespace OncoMosaic.Infrastructure;

public record InspectResult(int Width, int Height, int BandCount, double[] Wavelengths, double PixelSizeUm, string PreviewKey, JsonElement Acquisition);
