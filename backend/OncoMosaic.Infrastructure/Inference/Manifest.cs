using System.Text.Json;
namespace OncoMosaic.Infrastructure;

public record Manifest(Guid RunId, string ModelVersion, Rectangle Roi, int Width, int Height, Marker[] Markers, string MaskKey, string OverlayKey, string TissueKey, string QcKey, int ValidTissuePx, string CellsKey, int CellCount, string? Ki67QuantitativeKey = null, JsonElement Identity = default);
