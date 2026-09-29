using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OncoMosaic;
using Xunit;

namespace OncoMosaic.Tests;

public class MigrationTests
{
    [Fact]
    public void CleanDatabaseMigrationIncludesSpectralAnalysisColumns()
    {
        var options = new DbContextOptionsBuilder<AppDb>()
            .UseMySQL("Server=localhost;Database=oncomosaic;User=test;Password=test")
            .Options;
        using var db = new AppDb(options);
        // Generate the actual upgrade SQL without connecting to a database.
        var sql = db.GetService<IMigrator>().GenerateScript();
        foreach (var column in new[] { "AcquisitionJson", "AssayKey", "AssaySha256", "Cd3Value", "ValidTissuePx" })
            Assert.Contains($"`{column}`", sql);
    }
}
