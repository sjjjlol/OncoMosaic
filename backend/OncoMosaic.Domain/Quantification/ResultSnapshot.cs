namespace OncoMosaic.Domain;

public record ResultSnapshot(AnalysisRun Run, Roi Roi, TissueImage Image, List<CellView> Cells, Summary Summary, object[] Reviews);
