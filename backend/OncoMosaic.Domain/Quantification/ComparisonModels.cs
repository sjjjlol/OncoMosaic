namespace OncoMosaic.Domain;

public record ResultSelection(Guid RunId, int? ReviewVersion);
public record ComparisonInput(ResultSelection A, ResultSelection B);
public record Exploration(Guid RunId, string ModelVersion, string AlgorithmVersion, Roi Roi, Summary Summary, List<CellView> Cells, List<NearestNeighbor> NearestNeighbors, object[] Reviews);
public record ComparisonResult(Guid ImageId, string ImageSha256, string AssaySha256, double PixelSizeUm, Exploration A, Exploration B, bool SameScheme, string[] Warnings, Ki67Comparison? Ki67 = null);
