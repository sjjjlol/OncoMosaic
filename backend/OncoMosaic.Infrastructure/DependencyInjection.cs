using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace OncoMosaic.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("请配置 ConnectionStrings:Database");
        services.AddDbContext<AppDb>(options => options.UseMySQL(connection));
        services.AddSingleton<FileStore>();
        services.AddSingleton<IArtifactFiles>(provider => provider.GetRequiredService<FileStore>());
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IAnalysisRepository, AnalysisRepository>();
        services.AddScoped<IResultRepository, ResultRepository>();
        services.AddScoped<IImageImporter, ImageImporter>();
        services.AddScoped<IAnalysisEngine, AnalysisEngine>();
        services.AddScoped<IResultExporter, ResultExporter>();
        services.AddScoped<DatabaseInitialization>();
        services.AddHttpClient<InferenceClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["PYTHON_URL"] ?? "http://localhost:8000/");
            client.Timeout = TimeSpan.FromSeconds(90);
        });
        return services;
    }
}
