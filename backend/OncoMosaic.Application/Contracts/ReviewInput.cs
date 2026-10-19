using System.Text.Json;
namespace OncoMosaic.Application;

public record ReviewInput(Guid CellId, string NewLabel, string? Reason, string Dimension = "identity", int? BaseReviewVersion = null);
