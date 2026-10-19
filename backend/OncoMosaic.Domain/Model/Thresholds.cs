using System.Text.Json;
namespace OncoMosaic.Domain;

public record Thresholds([property: System.Text.Json.Serialization.JsonRequired] double Panck,
                         [property: System.Text.Json.Serialization.JsonRequired] double Cd3,
                         [property: System.Text.Json.Serialization.JsonRequired] double Cd8, double? Ki67 = null)
{
    public bool Valid() => double.IsFinite(Panck) && double.IsFinite(Cd3) && double.IsFinite(Cd8) && Panck is >= 0 and <= 1 && Cd3 is >= 0 and <= 1 && Cd8 is >= 0 and <= 1 && (Ki67 == null || (double.IsFinite(Ki67.Value) && Ki67 is >= 0 and <= 1));
}
