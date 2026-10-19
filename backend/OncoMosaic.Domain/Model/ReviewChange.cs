using System.Text.Json;
namespace OncoMosaic.Domain;

public class ReviewChange
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid RevisionId { get; init; }
    public Guid CellId { get; init; }
    public string Dimension { get; init; } = "identity";
    public string NewLabel { get; init; } = "negative";
    public string Reason { get; init; } = "";
}
