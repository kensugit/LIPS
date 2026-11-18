using Microsoft.EntityFrameworkCore;

namespace CatalogSearch.Infrastructure;

public sealed record FolderDuplicate(Guid SourceDocumentId, int Products, string ObservedAt);
public interface IFolderDuplicateLookup
{
    Task<FolderDuplicate?> FindCompleted(string supplier, string hash, CancellationToken ct);
}

public sealed class FolderDuplicateLookup(CatalogSearchDbContext db) : IFolderDuplicateLookup
{
    public async Task<FolderDuplicate?> FindCompleted(string supplier, string hash, CancellationToken ct)
    {
        var source = await (from d in db.SourceDocuments.AsNoTracking()
                            join s in db.Suppliers.AsNoTracking() on d.SupplierId equals s.Id
                            where s.Code == supplier && d.FileHash == hash
                            select d).SingleOrDefaultAsync(ct);
        if (source is null) return null;
        if (await db.ExtractionJobs.AnyAsync(j => j.SourceDocumentId == source.Id &&
            j.ParserVersion == OrveauxExactPairImportStore.ParserVersion, ct)) return null;
        // Specialized partial import stores own completeness; do not bypass them.
        if (supplier is "LA_VINEE" or "ENEOS" or "ORVEAUX") return null;
        var count = await db.SourceEvidence.CountAsync(e => e.SourceDocumentId == source.Id, ct);
        var completed = await db.ExtractionJobs.AnyAsync(j => j.SourceDocumentId == source.Id &&
            j.Status == "Completed" && j.RowCount == count, ct);
        return count == 0 || !completed ? null : new(source.Id, count, source.ObservedAt.ToString("yyyy-MM-dd"));
    }
}
