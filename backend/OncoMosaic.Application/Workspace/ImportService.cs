namespace OncoMosaic.Application;

public sealed class ImportService(IWorkspaceRepository repository, IImageImporter importer)
{
    public const long MaxBytes = 50 * 1024 * 1024;
    public async Task<TissueImage> Import(Guid projectId, string name, string description, Stream input, Stream assay, CancellationToken ct, Guid? fixedId = null)
    {
        if (!await repository.ProjectExists(projectId, ct))
            throw new OperationFailure(FailureKind.NotFound, "NOT_FOUND", "项目不存在");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || description.Length > 2000)
            throw new OperationFailure(FailureKind.Invalid, "INVALID_NAME", "图像名称必填且最多 160 字，说明最多 2000 字");
        var image = await importer.Stage(fixedId ?? Guid.NewGuid(), projectId, name.Trim(), description, input, assay, ct);
        try { await repository.AddImage(image, ct); }
        catch
        {
            // A disconnected commit can have succeeded. Only compensate when absence is confirmed.
            // If the database is still unavailable, retain the files for later reconciliation.
            try
            {
                using var verification = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                if (!await repository.ImageExists(image.Id, verification.Token)) importer.Remove(image);
            }
            catch { /* Preserve the original persistence error and possibly committed files. */ }
            throw;
        }
        return image;
    }
}
