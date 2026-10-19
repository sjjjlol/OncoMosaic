using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit;
namespace OncoMosaic.Tests;

public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ONCOMOSAIC_TEST_DATABASE")))
            Skip = "Set ONCOMOSAIC_TEST_DATABASE to an isolated MySQL server; each test creates and deletes its own database.";
    }
}

public sealed class PersistenceIntegrationTests : IAsyncLifetime
{
    private string connection = "";
    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ONCOMOSAIC_TEST_DATABASE");
        if (string.IsNullOrEmpty(configured)) return;
        var builder = new MySqlConnectionStringBuilder(configured) { Database = "oncomosaic_test_" + Guid.NewGuid().ToString("N") };
        connection = builder.ConnectionString;
        await using var db = Open();
        await db.Database.MigrateAsync();
    }
    public async Task DisposeAsync()
    {
        if (connection == "") return;
        await using var db = Open();
        await db.Database.EnsureDeletedAsync();
    }
    private AppDb Open() => new(new DbContextOptionsBuilder<AppDb>().UseMySQL(connection).Options);
    private async Task<AnalysisRun> Seed(bool succeeded = false)
    {
        await using var db = Open();
        var project = new Project { Name = "Integration" };
        var image = new TissueImage { ProjectId = project.Id, AssayKey = "test.assay.json" };
        var roi = new Roi { ImageId = image.Id, Width = 100, Height = 100 };
        var run = new AnalysisRun { RoiId = roi.Id };
        run.Start();
        if (succeeded) run.Succeed(100); else run.Fail("TEST", "Test failure");
        db.AddRange(project, image, roi, run);
        await db.SaveChangesAsync();
        return run;
    }

    [MySqlFact]
    public async Task ConcurrentRetryOnlyQueuesOnce()
    {
        var run = await Seed();
        async Task<bool> Retry()
        {
            await using var db = Open();
            try
            {
                await new AnalysisRepository(db).WithLockedRun(run.Id, r => { r.Retry(); return Task.FromResult(true); }, default);
                return true;
            }
            catch (OperationFailure failure) when (failure.Code == "INVALID_STATE") { return false; }
        }
        var results = await Task.WhenAll(Retry(), Retry());
        Assert.Single(results, r => r);
        await using var check = Open();
        var saved = await check.AnalysisRuns.SingleAsync();
        Assert.Equal("Queued", saved.Status);
        Assert.Equal(1, saved.Attempt);
    }

    [MySqlFact]
    public async Task ClaimAndCompletionAreAtomicAndPublishedCellsCannotBeReplaced()
    {
        var seeded = await Seed();
        await using var db = Open();
        var repository = new AnalysisRepository(db);
        await repository.WithLockedRun(seeded.Id, r => { r.Retry(); return Task.FromResult(true); }, default);
        var run = Assert.IsType<AnalysisRun>(await repository.ClaimNext(default));
        Assert.Null(await repository.ClaimNext(default));
        var cell = new Cell { RunId = run.Id, LocalIndex = 1 };
        await repository.Complete(run.Id, run.Attempt - 1, new(100, [cell], []), default);
        Assert.Empty(await db.Cells.ToListAsync());
        // An invalid FK must roll back both inserted cells and Succeeded status.
        await Assert.ThrowsAsync<DbUpdateException>(() => repository.Complete(run.Id, run.Attempt,
            new(100, [cell], [new Artifact { RunId = Guid.NewGuid(), Kind = "bad" }]), default));
        Assert.Equal("Running", (await repository.Run(run.Id, default)).Status);
        Assert.Empty(await db.Cells.AsNoTracking().ToListAsync());
        await repository.Complete(run.Id, run.Attempt, new(100, [cell], []), default);
        await repository.Complete(run.Id, run.Attempt, new(200, [new Cell { RunId = run.Id, LocalIndex = 1 }], []), default);
        await repository.Fail(run.Id, run.Attempt, "LATE", "late error", default);
        Assert.Equal(cell.Id, (await db.Cells.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(100, (await repository.Run(run.Id, default)).ValidTissuePx);
        Assert.Equal("Succeeded", (await repository.Run(run.Id, default)).Status);
    }

    [MySqlFact]
    public async Task ConcurrentReviewsRejectStaleVersionWithoutChangingMeasurements()
    {
        var run = await Seed(true);
        var cell = new Cell { RunId = run.Id, LocalIndex = 1, PanckValue = .8 };
        await using (var db = Open()) { db.Cells.Add(cell); await db.SaveChangesAsync(); }
        async Task<bool> Review(string label)
        {
            await using var db = Open();
            var service = new ReviewService(new AnalysisRepository(db), new ResultRepository(db));
            try { await service.Add(run.Id, new(cell.Id, label, "checked", BaseReviewVersion: 0), default); return true; }
            catch (OperationFailure failure) when (failure.Code == "REVIEW_CONFLICT") { return false; }
        }
        var results = await Task.WhenAll(Review("excluded"), Review("negative"));
        Assert.Single(results, r => r);
        await using var check = Open();
        Assert.Single(await check.ReviewRevisions.ToListAsync());
        Assert.Equal(.8, (await check.Cells.SingleAsync()).PanckValue);
    }

    [MySqlFact]
    public async Task MigrationsMatchModelAndInterruptedRunsRecoverWithinBudget()
    {
        var seeded = await Seed();
        await using var db = Open();
        Assert.False(db.Database.HasPendingModelChanges());
        var repository = new AnalysisRepository(db);
        await repository.WithLockedRun(seeded.Id, r => { r.Retry(); r.Start(); return Task.FromResult(true); }, default);
        await repository.RecoverInterrupted(default);
        Assert.Equal("Queued", (await repository.Run(seeded.Id, default)).Status);
        await repository.ClaimNext(default);
        await repository.RecoverInterrupted(default);
        Assert.Equal("Failed", (await repository.Run(seeded.Id, default)).Status);
        Assert.Equal(3, (await repository.Run(seeded.Id, default)).Attempt);
    }
}
