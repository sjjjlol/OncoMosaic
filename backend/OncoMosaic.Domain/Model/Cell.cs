using System.Text.Json;
namespace OncoMosaic.Domain;

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
    public double? Ki67Value { get; set; }
    public string Ki67Quality { get; set; } = "not-measured";
    public int Ki67ValidPixelCount { get; set; }
    public string QualityFlag { get; set; } = "ok";
    public string ContourJson { get; set; } = "[]";
}
