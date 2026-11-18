using CatalogSearch.Domain;
using Microsoft.EntityFrameworkCore;
namespace CatalogSearch.Infrastructure;
public sealed partial class CatalogSearchDbContext
{
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<ExtractionJob> ExtractionJobs => Set<ExtractionJob>();
    public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();
    public DbSet<InventorySnapshot> InventorySnapshots => Set<InventorySnapshot>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<SourceEvidence> SourceEvidence => Set<SourceEvidence>();
    public DbSet<ReviewTask> ReviewTasks => Set<ReviewTask>();
    public DbSet<OperationLog> OperationLogs => Set<OperationLog>();
    public DbSet<InternalSnapshot> InternalSnapshots => Set<InternalSnapshot>();
    public DbSet<ReviewAction> ReviewActions => Set<ReviewAction>();
    public DbSet<WatchFileState> WatchFileStates => Set<WatchFileState>();
    private static void ConfigureIngestion(ModelBuilder m)
    {
        m.Entity<InternalSnapshot>().HasOne<InternalProductRecord>().WithMany().HasForeignKey(x => x.InternalProductRecordId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<InternalSnapshot>().HasIndex(x => new { x.InternalProductRecordId, x.Hash }).IsUnique();
        m.Entity<InternalSnapshot>().Property(x => x.PayloadJson).HasColumnType("jsonb");
        m.Entity<WatchFileState>().HasIndex(x => new { x.FileKey, x.FileHash }).IsUnique();
        m.Entity<ReviewAction>().HasIndex(x => new { x.EntityType, x.RecordId });
        m.Entity<Supplier>().HasIndex(x => x.Code).IsUnique();
        m.Entity<SourceDocument>().HasIndex(x => new { x.SupplierId, x.FileHash }).IsUnique();
        m.Entity<SourceDocument>().HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<SupplierProduct>().HasIndex(x => new { x.SupplierId, x.SupplierProductCode }).IsUnique();
        m.Entity<SupplierProduct>().Property(x => x.CodeKind).HasDefaultValue("SupplierSku");
        m.Entity<SupplierProduct>().HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<SupplierProduct>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<SupplierProduct>().HasOne<SourceDocument>().WithMany().HasForeignKey(x => x.CurrentSourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ExtractionJob>().HasOne<SourceDocument>().WithMany().HasForeignKey(x => x.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ExtractionJob>().HasIndex(x => x.SourceDocumentId).IsUnique();
        m.Entity<PriceHistory>().HasOne<SourceDocument>().WithMany().HasForeignKey(x => x.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<PriceHistory>().HasOne<SupplierProduct>().WithMany().HasForeignKey(x => x.SupplierProductId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<PriceHistory>().HasIndex(x => new { x.SourceDocumentId, x.SupplierProductId }).IsUnique();
        m.Entity<InventorySnapshot>().HasOne<SourceDocument>().WithMany().HasForeignKey(x => x.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<InventorySnapshot>().HasIndex(x => x.SourceDocumentId).IsUnique();
        m.Entity<InventoryItem>().HasOne<InventorySnapshot>().WithMany().HasForeignKey(x => x.InventorySnapshotId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<InventoryItem>().HasOne<SupplierProduct>().WithMany().HasForeignKey(x => x.SupplierProductId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<InventoryItem>().HasIndex(x => new { x.InventorySnapshotId, x.SupplierProductId }).IsUnique();
        m.Entity<SourceEvidence>().HasOne<SourceDocument>().WithMany().HasForeignKey(x => x.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<SourceEvidence>().HasOne<SupplierProduct>().WithMany().HasForeignKey(x => x.SupplierProductId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<SourceEvidence>().HasIndex(x => new { x.SourceDocumentId, x.SupplierProductId }).IsUnique();
        m.Entity<SourceEvidence>().Property(x => x.RawCellsJson).HasColumnType("jsonb");
        m.Entity<SourceEvidence>().Property(x => x.ExtractedRowJson).HasColumnType("jsonb");
        m.Entity<ReviewTask>().HasOne<SupplierProduct>().WithMany().HasForeignKey(x => x.SupplierProductId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ReviewTask>().HasIndex(x => x.SupplierProductId).IsUnique();
        m.Entity<OperationLog>().HasOne<SourceDocument>().WithMany().HasForeignKey(x => x.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<InternalProductRecord>().HasOne<SourceSystem>().WithMany().HasForeignKey(x => x.SourceSystemId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<InternalProductLink>().HasOne<InternalProductRecord>().WithOne().HasForeignKey<InternalProductLink>(x => x.InternalProductRecordId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<InternalProductLink>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ProductSearchDocument>().Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
            .HasComputedColumnSql("to_tsvector('simple', coalesce(\"SearchText\", ''))", stored: true);
        m.Entity<ProductSearchDocument>().HasIndex("SearchVector").HasMethod("GIN");
    }
}
