using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace CatalogSearch.Infrastructure;
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<CatalogSearchDbContext>
{
    public CatalogSearchDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<CatalogSearchDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("CATALOG_CONNECTION") ?? "Host=127.0.0.1;Database=catalog_search;Username=catalog").Options);
}
