namespace OncoMosaic.Api;

public static class WorkspaceEndpoints
{
    public static void MapWorkspace(this WebApplication app)
    {
        app.MapGet("/api/projects", (WorkspaceService workspace, CancellationToken ct) => workspace.Projects(ct));
        app.MapPost("/api/projects", async (ProjectInput input, WorkspaceService workspace, CancellationToken ct) =>
        {
            var project = await workspace.CreateProject(input, ct);
            return Results.Created($"/api/projects/{project.Id}", project);
        });
        app.MapGet("/api/projects/{id:guid}/images", async (Guid id, WorkspaceService workspace, CancellationToken ct) =>
            (await workspace.Images(id, ct)).Select(Representations.Image));
        app.MapPost("/api/projects/{id:guid}/images", ImportImage).DisableAntiforgery();
        app.MapGet("/api/images/{id:guid}", async (Guid id, WorkspaceService workspace, CancellationToken ct) => Representations.Image(await workspace.Image(id, ct)));
        app.MapGet("/api/images/{id:guid}/preview", async (Guid id, WorkspaceService workspace, CancellationToken ct) => Results.File(await workspace.Preview(id, ct), "image/png"));
        app.MapGet("/api/images/{id:guid}/rois", (Guid id, WorkspaceService workspace, CancellationToken ct) => workspace.Rois(id, ct));
        app.MapPost("/api/images/{id:guid}/rois", async (Guid id, RoiInput input, WorkspaceService workspace, CancellationToken ct) => Results.Created($"/api/images/{id}/rois", await workspace.CreateRoi(id, input, ct)));
    }

    private static async Task<IResult> ImportImage(Guid id, HttpRequest request, ImportService imports, CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        var assay = form.Files.GetFile("assay");
        if (file == null || !file.FileName.EndsWith(".ome.tiff", StringComparison.OrdinalIgnoreCase) || assay == null || !assay.FileName.EndsWith(".assay.json", StringComparison.OrdinalIgnoreCase))
            throw new OperationFailure(FailureKind.Invalid, "INVALID_FILE", "请选择 .ome.tiff 图像及 .assay.json 伴随文件");
        if (file.Length > ImportService.MaxBytes || assay.Length > 100_000)
            throw new OperationFailure(FailureKind.TooLarge, "FILE_TOO_LARGE", "图像不得超过 50 MB，伴随元数据不得超过 100 KB");
        await using var stream = file.OpenReadStream();
        await using var assayStream = assay.OpenReadStream();
        var image = await imports.Import(id, form["name"].ToString() is { Length: > 0 } name ? name : file.FileName[..^9], form["description"].ToString(), stream, assayStream, ct);
        return Results.Created($"/api/images/{image.Id}", Representations.Image(image));
    }
}
