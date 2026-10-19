using Microsoft.EntityFrameworkCore;
namespace OncoMosaic.Infrastructure;

public sealed class WorkspaceRepository(AppDb db) : IWorkspaceRepository
{
    public Task<List<Project>> Projects(CancellationToken ct) => db.Projects.AsNoTracking().OrderBy(p => p.CreatedAt).ToListAsync(ct);
    public Task<bool> ProjectExists(Guid id, CancellationToken ct) => db.Projects.AnyAsync(p => p.Id == id, ct);
    public async Task AddProject(Project project, CancellationToken ct) { db.Projects.Add(project); await db.SaveChangesAsync(ct); }
    public Task<List<TissueImage>> Images(Guid projectId, CancellationToken ct) => db.Images.AsNoTracking().Where(i => i.ProjectId == projectId && i.AssayKey != "").OrderBy(i => i.CreatedAt).ToListAsync(ct);
    public async Task<TissueImage> Image(Guid id, CancellationToken ct) => await db.Images.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id && i.AssayKey != "", ct) ?? throw Missing("图像不存在");
    public Task<bool> ImageExists(Guid id, CancellationToken ct) => db.Images.AnyAsync(i => i.Id == id, ct);
    public async Task AddImage(TissueImage image, CancellationToken ct) { db.Images.Add(image); await db.SaveChangesAsync(ct); }
    public Task<List<Roi>> Rois(Guid imageId, CancellationToken ct) => db.Rois.AsNoTracking().Where(r => r.ImageId == imageId).ToListAsync(ct);
    public async Task<Roi> Roi(Guid id, CancellationToken ct) => await db.Rois.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw Missing("ROI 不存在");
    public async Task AddRoi(Roi roi, CancellationToken ct) { db.Rois.Add(roi); await db.SaveChangesAsync(ct); }
    private static OperationFailure Missing(string message) => new(FailureKind.NotFound, "NOT_FOUND", message);
}
