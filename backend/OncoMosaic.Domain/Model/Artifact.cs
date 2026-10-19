using System.Text.Json;
namespace OncoMosaic.Domain;

public class Artifact
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid RunId { get; init; }
    public string Kind { get; init; } = "";
    public string FileKey { get; init; } = "";
    public string Sha256 { get; init; } = "";
}
