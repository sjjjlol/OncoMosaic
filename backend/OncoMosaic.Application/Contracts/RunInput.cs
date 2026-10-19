using System.Text.Json;
namespace OncoMosaic.Application;

public record RunInput(Guid ImageId, Guid RoiId, string ModelVersion, Thresholds Thresholds);
