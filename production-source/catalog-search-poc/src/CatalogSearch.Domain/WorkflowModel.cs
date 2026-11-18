namespace CatalogSearch.Domain;
public sealed class InternalSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InternalProductRecordId { get; set; }
    public required string Hash { get; set; }
    public required string PayloadJson { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
}
public sealed class ReviewAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string EntityType { get; set; }
    public Guid RecordId { get; set; }
    public Guid? BeforeProductId { get; set; }
    public Guid? AfterProductId { get; set; }
    public required string Action { get; set; }
    public string Actor { get; set; } = "Local demo operator";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class WatchFileState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string FileKey { get; set; }
    public required string FileHash { get; set; }
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
}
