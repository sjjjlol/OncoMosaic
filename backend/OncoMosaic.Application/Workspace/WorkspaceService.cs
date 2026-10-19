namespace OncoMosaic.Application;

public sealed class WorkspaceService(IWorkspaceRepository repository, IArtifactFiles files)
{
    public Task<List<Project>> Projects(CancellationToken ct) => repository.Projects(ct);
    public async Task<Project> CreateProject(ProjectInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120)
            throw new OperationFailure(FailureKind.Invalid, "INVALID_NAME", "项目名称必填且最多 120 字");
        var project = new Project { Name = input.Name.Trim() };
        await repository.AddProject(project, ct);
        return project;
    }
    public Task<List<TissueImage>> Images(Guid projectId, CancellationToken ct) => repository.Images(projectId, ct);
    public Task<TissueImage> Image(Guid id, CancellationToken ct) => repository.Image(id, ct);
    public async Task<string> Preview(Guid id, CancellationToken ct) => files.Existing((await repository.Image(id, ct)).PreviewKey);
    public Task<List<Roi>> Rois(Guid imageId, CancellationToken ct) => repository.Rois(imageId, ct);
    public async Task<Roi> CreateRoi(Guid imageId, RoiInput input, CancellationToken ct)
    {
        var image = await repository.Image(imageId, ct);
        if (!new Rectangle(input.X, input.Y, input.Width, input.Height).Valid(image.Width, image.Height))
            throw new OperationFailure(FailureKind.Invalid, "INVALID_ROI", "ROI 必须在图像范围内且宽高大于零");
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120 || input.RegionTag is not ("tumor-candidate" or "other"))
            throw new OperationFailure(FailureKind.Invalid, "INVALID_ROI", "区域名称或标签无效");
        var roi = new Roi { ImageId = imageId, Name = input.Name.Trim(), X = input.X, Y = input.Y, Width = input.Width, Height = input.Height, RegionTag = input.RegionTag };
        await repository.AddRoi(roi, ct);
        return roi;
    }
}
