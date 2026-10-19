using System.Text.Json;
namespace OncoMosaic.Domain;

public class ReviewRevision
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid RunId { get; init; }
    public int Version { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
