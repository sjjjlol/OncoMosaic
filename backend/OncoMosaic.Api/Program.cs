using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using OncoMosaic.Api;

if (args.Contains("--healthcheck"))
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var response = await client.GetAsync("http://127.0.0.1:8080/api/health");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch { Environment.ExitCode = 1; }
    return;
}

var migrateOnly = args.Contains("--migrate");
var builder = WebApplication.CreateBuilder(args.Where(a => a != "--migrate").ToArray());
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = ImportService.MaxBytes + 1024 * 1024);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = ImportService.MaxBytes + 1024 * 1024);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<AnalysisService>();
builder.Services.AddScoped<AnalysisProcessor>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<ResultService>();
builder.Services.AddScoped<ComparisonService>();
builder.Services.AddHostedService<AnalysisWorker>();
var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var initialization = scope.ServiceProvider.GetRequiredService<DatabaseInitialization>();
    if (migrateOnly || app.Configuration.GetValue<bool>("Database:AutoMigrate"))
        await initialization.Migrate(CancellationToken.None);
    if (migrateOnly) return;
    if (app.Configuration.GetValue<bool>("Demo:Seed"))
        await initialization.SeedDemo(app.Configuration["DEMO_FILE"] ?? "/demo/sample-ki67.ome.tiff", CancellationToken.None);
}

app.UseOperationFailures();
app.MapGet("/api/health", async (AppDb db, CancellationToken ct) => await db.Database.CanConnectAsync(ct)
    ? Results.Ok(new { status = "ok" })
    : Results.Json(new { code = "DATABASE_UNAVAILABLE", message = "数据库不可用" }, statusCode: 503));
app.MapWorkspace();
app.MapAnalysis();
app.MapResults();
app.Run();

public partial class Program { }
