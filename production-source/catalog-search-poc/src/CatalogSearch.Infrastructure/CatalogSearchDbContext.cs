using CatalogSearch.Domain;
using Microsoft.EntityFrameworkCore;

namespace CatalogSearch.Infrastructure;

public sealed partial class CatalogSearchDbContext(DbContextOptions<CatalogSearchDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<SupplierProduct> SupplierProducts => Set<SupplierProduct>();
    public DbSet<SourceSystem> SourceSystems => Set<SourceSystem>();
    public DbSet<InternalProductRecord> InternalProductRecords => Set<InternalProductRecord>();
    public DbSet<InternalProductLink> InternalProductLinks => Set<InternalProductLink>();
    public DbSet<ProductSearchDocument> ProductSearchDocuments => Set<ProductSearchDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureIngestion(modelBuilder);
        modelBuilder.Entity<Product>().HasKey(x => x.Id);
        modelBuilder.Entity<SupplierProduct>().HasKey(x => x.Id);
        modelBuilder.Entity<SourceSystem>().HasKey(x => x.Id);
        modelBuilder.Entity<SourceSystem>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<InternalProductRecord>().HasKey(x => x.Id);
        modelBuilder.Entity<InternalProductRecord>().HasIndex(x => new { x.SourceSystemId, x.SourceKey }).IsUnique();
        modelBuilder.Entity<InternalProductLink>().HasKey(x => x.InternalProductRecordId);
        modelBuilder.Entity<ProductSearchDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<ProductSearchDocument>().HasIndex(x => new { x.EntityType, x.SourceRecordId }).IsUnique();
        modelBuilder.Entity<Product>().Property(x => x.AttributesJson).HasColumnType("jsonb");
        modelBuilder.Entity<InternalProductRecord>().Property(x => x.SourcePayloadJson).HasColumnType("jsonb");
        modelBuilder.Entity<InternalProductLink>().Property(x => x.EvidenceJson).HasColumnType("jsonb");
        modelBuilder.Entity<ProductSearchDocument>().Property(x => x.StructuredDataJson).HasColumnType("jsonb");
    }
}
