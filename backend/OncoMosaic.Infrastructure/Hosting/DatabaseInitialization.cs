using Microsoft.EntityFrameworkCore;
namespace OncoMosaic.Infrastructure;

public sealed class DatabaseInitialization(AppDb db, ImportService imports, FileStore store)
{
    public Task Migrate(CancellationToken ct) => db.Database.MigrateAsync(ct);
    public async Task SeedDemo(string fixture, CancellationToken ct)
    {
        var projectId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            db.Projects.Add(new Project { Id = projectId, Name = "多标记组织 · 演示项目" });
            await db.SaveChangesAsync(ct);
        }
        var assay = fixture.Replace(".ome.tiff", ".assay.json");
        if (!File.Exists(fixture) || !File.Exists(assay)) return;
        var imageId = Guid.Parse(FileStore.Hash(assay)[..32]);
        if (await db.Images.AnyAsync(i => i.Id == imageId, ct)) return;
        var orphan = Path.GetDirectoryName(store.Resolve($"images/{imageId}/source.ome.tiff"))!;
        if (Directory.Exists(orphan)) Directory.Delete(orphan, true);
        await using var stream = File.OpenRead(fixture);
        await using var assayStream = File.OpenRead(assay);
        await imports.Import(projectId, "Ki-67 五标记 · 合成演示", "24 波段 OME-TIFF；含 Ki-67 核内模拟信号与单染对照；无患者来源。", stream, assayStream, ct, imageId);
    }
}
