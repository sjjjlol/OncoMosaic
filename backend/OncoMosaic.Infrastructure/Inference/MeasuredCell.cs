using System.Text.Json;
namespace OncoMosaic.Infrastructure;

public record MeasuredCell(int LocalIndex, double X, double Y, int AreaPx, double DapiValue, double PanckValue, double Cd3Value, double Cd8Value, string QualityFlag, double[][] Contour, double? Ki67Value = null, string Ki67Quality = "not-measured", int Ki67ValidPixelCount = 0);
