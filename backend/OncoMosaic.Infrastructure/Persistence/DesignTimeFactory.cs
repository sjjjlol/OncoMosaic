using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace OncoMosaic.Infrastructure;

public class DesignTimeFactory : IDesignTimeDbContextFactory<AppDb>
{
    public AppDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AppDb>().UseMySQL(Environment.GetEnvironmentVariable("ConnectionStrings__Database") ?? throw new InvalidOperationException("请配置 ConnectionStrings__Database")).Options);
}
