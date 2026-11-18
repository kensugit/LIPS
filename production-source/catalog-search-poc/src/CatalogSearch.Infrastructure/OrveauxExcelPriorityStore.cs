using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using Microsoft.EntityFrameworkCore;

namespace CatalogSearch.Infrastructure;

// Explicit user decision on 2026-10-17, limited to the exact workbook. Existing IDs
// and previous source/price/inventory evidence are retained. No fuzzy identity links.
public sealed class OrveauxExcelPriorityStore(CatalogSearchDbContext db) : IInventoryStore
{
    public const string Version = "OrveauxStock/2/ExcelPriority/1";
    public static string Normalize(string? s) => Regex.Replace((s ?? "").Normalize(NormalizationForm.FormKC), @"\s+", " ").Trim().ToUpperInvariant();
    public static bool ExactIdentity(SupplierInventoryRow row, SupplierProduct p) =>
        Normalize(row.ProductNameEn) == Normalize(p.ProductNameEn) && Normalize(row.ProducerNameEn) == Normalize(p.ProducerNameEn) &&
        Normalize(row.VintageRaw) == Normalize(p.VintageRaw) && row.VolumeMl == p.VolumeMl &&
        Normalize(row.ProductType) == Normalize(p.ProductType);
    public static bool ApprovedTinto(SupplierInventoryRow row, SupplierProduct p) =>
        row.ProductNameEn == "Tinto Velasco Vino de la Tierra de Castilla" && row.ProducerNameEn == "Finca la Escatada" &&
        row.VintageRaw == "2023" && row.VolumeMl == 750 && row.Inventory.Quantity == 126 &&
        p.SupplierProductCode == "ORV-F76228FE2775FF3F4E8C5F46" && p.ProductNameEn == row.ProductNameEn &&
        p.ProducerNameEn == "Ocho y Medio" && p.VintageRaw == "2023" && p.VolumeMl == 750 && p.ProductType == row.ProductType;

    private static string SearchText(SupplierInventoryRow row) => string.Join(" ", row.ProductNameJa, row.ProductNameEn,
        row.ProducerNameJa, row.ProducerNameEn, row.VintageRaw, row.ProductType, row.Country, row.Region, row.Grapes,
        row.AppellationJa, row.AppellationEn, row.OrganicCategory, row.Description, row.SupplierProductCode,
        string.Join(" ", row.JanCodes ?? []));
    private async Task PreserveSearchTerms(Supplier supplier, SupplierProduct product, SupplierInventoryRow row, Guid currentSource, CancellationToken ct)
    {
        var document = await db.ProductSearchDocuments.SingleOrDefaultAsync(d => d.EntityType == SearchEntityType.SupplierProduct && d.SourceRecordId == product.Id, ct);
        if (document is null) { document = new() { EntityType = SearchEntityType.SupplierProduct, SourceRecordId = product.Id, SearchText = "" }; db.ProductSearchDocuments.Add(document); }
        // Preserve old index terms, and regenerate missing prior terms from immutable evidence.
        // This also repairs only the accepted products from the earlier priority run.
        var prior = await db.SourceEvidence.Where(e => e.SupplierProductId == product.Id && e.SourceDocumentId != currentSource)
            .Select(e => e.ExtractedRowJson).ToListAsync(ct);
        var fragments = new List<string> { supplier.Name, supplier.Code, SearchText(row) };
        foreach (var json in prior)
        {
            var historical = JsonSerializer.Deserialize<SupplierInventoryRow>(json);
            if (historical is not null) fragments.Add(SearchText(historical));
        }
        var text = document.SearchText;
        foreach (var fragment in fragments.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.Ordinal))
            if (!text.Contains(fragment, StringComparison.Ordinal)) text = string.Join(" ", text, fragment).Trim();
        if (text != document.SearchText) { document.SearchText = text; document.UpdatedAt = DateTimeOffset.UtcNow; }
    }

    public async Task<ImportResult> SaveAsync(SourceInput input, string hash, IReadOnlyList<SupplierInventoryRow> rows, CancellationToken ct)
    {
        if (input.SupplierCode != "ORVEAUX" || input.ParserVersion != Version ||
            !string.Equals(hash, OrveauxStockParser.ApprovedOriginalHash, StringComparison.OrdinalIgnoreCase) ||
            Convert.ToHexString(SHA256.HashData(input.Content)) != OrveauxStockParser.ApprovedOriginalHash ||
            input.ObservedAt != new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero))
            throw new FolderImportHeldException("Excel優先の本人指定は今回のオルヴォー原本・日付に限ります。別原本は確認が必要です。");
        hash = hash.ToUpperInvariant();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190218)", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({input.SupplierCode},0))", ct);
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Code == "ORVEAUX", ct);
        if (supplier is null) throw new FolderImportHeldException("既存オルヴォー商品の照合が必要です。");
        // Source-only lot-condition attachments are not accepted stock snapshots.
        // Each matched product's current date is checked below; no newer product is overwritten.
        var source = await db.SourceDocuments.SingleOrDefaultAsync(s => s.SupplierId == supplier.Id && s.FileHash == hash, ct);
        if (source is not null && (!source.Content.SequenceEqual(input.Content) || source.ObservedAt != input.ObservedAt))
            throw new FolderImportHeldException("保存済み原本の内容または日付が一致しません。");
        if (source is null)
        {
            source = new() { SupplierId = supplier.Id, FileName = input.FileName, FileHash = hash, Content = input.Content, ObservedAt = input.ObservedAt, ImportedAt = DateTimeOffset.UtcNow };
            db.SourceDocuments.Add(source);
        }
        var job = await db.ExtractionJobs.SingleOrDefaultAsync(j => j.SourceDocumentId == source.Id, ct);
        if (job is not null && job.ParserVersion != Version)
            throw new FolderImportHeldException("この原本は別方式で保存済みです。既存の取込根拠を変更せず確認してください。");
        if (job is null) { job = new() { SourceDocumentId = source.Id, ParserVersion = Version }; db.ExtractionJobs.Add(job); }
        var snapshot = await db.InventorySnapshots.SingleOrDefaultAsync(s => s.SourceDocumentId == source.Id, ct);
        if (snapshot is null) { snapshot = new() { SourceDocumentId = source.Id, ObservedAt = input.ObservedAt }; db.InventorySnapshots.Add(snapshot); }
        var products = await db.SupplierProducts.Where(p => p.SupplierId == supplier.Id).ToListAsync(ct);
        var accepted = (await db.SourceEvidence.Where(e => e.SourceDocumentId == source.Id).Select(e => e.SupplierProductId).ToListAsync(ct)).ToHashSet();
        var holds = new List<ImportRowHold>(input.PendingHolds ?? []);
        var assigned = new HashSet<Guid>(); var updated = 0;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var matches = products.Where(p => ExactIdentity(row, p) || ApprovedTinto(row, p)).ToArray();
            // A previously accepted Tinto now carries the Excel producer label.
            var product = matches.Length == 1 ? matches[0] : null;
            var reason = product is null ? "既存商品の生産者・商品名・年・容量・タイプとの一意な対応が未確定です。推測で統合・新規登録していません。" :
                !assigned.Add(product.Id) ? "複数のExcel商品が同じ既存商品に対応するため保留します。" :
                product.CurrentObservedAt > input.ObservedAt ? "より新しい商品情報があるため保留します。" : null;
            if (reason is not null) { holds.Add(new(row.SupplierProductCode, reason, row.Occurrences)); continue; }
            if (accepted.Contains(product!.Id)) { await PreserveSearchTerms(supplier, product, row, source.Id, ct); continue; }
            var existing = new { product.Id, product.SupplierProductCode, product.ProducerNameEn, product.ProductNameEn, product.VintageRaw, product.VolumeMl,
                product.CurrentSourceDocumentId, product.CurrentPrice, product.CurrentInventoryQuantity, product.CurrentInventoryStatus, product.CurrentObservedAt };
            var savedRow = row with { SupplierProductCode = product.SupplierProductCode,
                RawCellsJson = JsonSerializer.Serialize(new { OriginalExcel = row.RawCellsJson, PreviousProduct = existing,
                    Decision = "2026-10-17本人指定：今回Excel優先。既存商品との確定対応のみ更新。Tinto定番品126本を採用。", Match = ApprovedTinto(row, product) ? "ExplicitTintoPair" : "ExactProducerNameVintageVolumeType" }) };
            db.PriceHistories.Add(new() { SupplierProductId = product.Id, SourceDocumentId = source.Id, Amount = row.ReferenceRetailPrice, RawValue = row.PriceRaw, TaxIncluded = row.TaxIncluded, ObservedAt = input.ObservedAt });
            db.InventoryItems.Add(new() { SupplierProductId = product.Id, InventorySnapshotId = snapshot.Id, Quantity = row.Inventory.Quantity, Status = row.Inventory.Status, RawValue = row.Inventory.RawValue });
            db.SourceEvidence.Add(new() { SupplierProductId = product.Id, SourceDocumentId = source.Id, SheetName = row.SourceSheet, RowNumber = row.SourceRow, RawCellsJson = savedRow.RawCellsJson, ExtractedRowJson = JsonSerializer.Serialize(savedRow) });
            product.SupplierProductName = row.ProductNameJa; product.ProductNameEn = row.ProductNameEn;
            product.ProducerNameJa = row.ProducerNameJa; product.ProducerNameEn = row.ProducerNameEn;
            product.CurrentPrice = row.ReferenceRetailPrice; product.TaxIncluded = row.TaxIncluded;
            product.CurrentInventoryQuantity = row.Inventory.Quantity; product.CurrentInventoryStatus = row.Inventory.Status;
            product.CurrentSourceDocumentId = source.Id; product.CurrentObservedAt = input.ObservedAt;
            await PreserveSearchTerms(supplier, product, savedRow, source.Id, ct);
            accepted.Add(product.Id); updated++;
        }
        job.RowCount = accepted.Count; job.Status = holds.Count > 0 ? "Partial" : "Completed";
        db.OperationLogs.Add(new() { SourceDocumentId = source.Id, Status = job.Status, Action = "OrveauxExcelPriority", RowCount = updated, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(source.Id, accepted.Count, false, holds) { AddedProducts = updated };
    }
}
