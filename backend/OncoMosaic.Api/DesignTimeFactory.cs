using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace OncoMosaic;
public class DesignTimeFactory : IDesignTimeDbContextFactory<AppDb>
{
    public AppDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AppDb>().UseMySQL("Server=localhost;Database=oncomosaic;User=oncomosaic;Password=local-demo-only").Options);
}
