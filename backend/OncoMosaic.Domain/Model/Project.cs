using System.Text.Json;
namespace OncoMosaic.Domain;

public class Project
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
