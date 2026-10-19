namespace OncoMosaic.Domain;

// Semantic categories; only the HTTP adapter assigns status codes.
public enum FailureKind { Invalid, NotFound, Conflict, TooLarge, Rejected, Dependency, Unavailable, Timeout, MissingArtifact }

public sealed class OperationFailure(FailureKind kind, string code, string message) : Exception(message)
{
    public FailureKind Kind { get; } = kind;
    public string Code { get; } = code;
}
