using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
namespace OncoMosaic.Infrastructure;

public class FileStore(IConfiguration configuration) : IArtifactFiles
{
    public string Root { get; } = Path.GetFullPath(configuration["STORE_ROOT"] ?? "/store");
    public string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Path.IsPathRooted(key) || key.Contains('\\') || key.Split('/').Contains("..")) throw new OperationFailure(FailureKind.Invalid, "INVALID_KEY", "非法文件键");
        var path = Path.GetFullPath(Path.Combine(Root, key));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new OperationFailure(FailureKind.Invalid, "INVALID_KEY", "非法文件键");
        return path;
    }
    public string Existing(string key)
    {
        var path = Resolve(key);
        if (!File.Exists(path)) throw new OperationFailure(FailureKind.MissingArtifact, "MISSING_ARTIFACT", "结果文件缺失，请检查持久存储卷");
        return path;
    }
    public static string Hash(string path) { using var f = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }
}
