using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace OncoMosaic.Infrastructure;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TissueImage> Images => Set<TissueImage>();
    public DbSet<Roi> Rois => Set<Roi>();
    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();
    public DbSet<Cell> Cells => Set<Cell>();
    public DbSet<ReviewRevision> ReviewRevisions => Set<ReviewRevision>();
    public DbSet<ReviewChange> ReviewChanges => Set<ReviewChange>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<TissueImage>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        m.Entity<Roi>().HasOne<TissueImage>().WithMany().HasForeignKey(x => x.ImageId);
        m.Entity<AnalysisRun>().HasOne<Roi>().WithMany().HasForeignKey(x => x.RoiId);
        m.Entity<Cell>().HasOne<AnalysisRun>().WithMany().HasForeignKey(x => x.RunId);
        m.Entity<Cell>().HasIndex(x => new { x.RunId, x.LocalIndex }).IsUnique();
        m.Entity<ReviewRevision>().HasOne<AnalysisRun>().WithMany().HasForeignKey(x => x.RunId);
        m.Entity<ReviewRevision>().HasIndex(x => new { x.RunId, x.Version }).IsUnique();
        m.Entity<ReviewChange>().HasOne<ReviewRevision>().WithMany().HasForeignKey(x => x.RevisionId);
        m.Entity<ReviewChange>().HasOne<Cell>().WithMany().HasForeignKey(x => x.CellId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Artifact>().HasOne<AnalysisRun>().WithMany().HasForeignKey(x => x.RunId);
        m.Entity<Artifact>().Property(x => x.Kind).HasMaxLength(32);
        m.Entity<Artifact>().HasIndex(x => new { x.RunId, x.Kind }).IsUnique();
    }
}
