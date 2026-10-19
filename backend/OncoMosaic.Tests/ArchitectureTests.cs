using Xunit;
namespace OncoMosaic.Tests;

public class ArchitectureTests
{
    [Fact]
    public void BusinessAssembliesCannotDependOnHostingOrPersistence()
    {
        var domain = typeof(AnalysisRun).Assembly;
        var application = typeof(AnalysisService).Assembly;
        Assert.NotEqual(domain, application);
        foreach (var assembly in new[] { domain, application })
        {
            var references = assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();
            Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore") || name.StartsWith("Microsoft.AspNetCore") || name.StartsWith("OncoMosaic.Infrastructure") || name.StartsWith("SixLabors"));
        }
        Assert.DoesNotContain(domain.GetReferencedAssemblies(), name => name.Name == application.GetName().Name);
    }
}
