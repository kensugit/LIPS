using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using Microsoft.EntityFrameworkCore;
namespace CatalogSearch.Infrastructure;

public sealed class PostgresInventoryStore(CatalogSearchDbContext db, bool folderIntake = false) : IInventoryStore
{
    public async Task<ImportResult> SaveAsync(SourceInput input, string hash, IReadOnlyList<SupplierInventoryRow> rows, CancellationToken ct)
    {
        if (folderIntake && input.SupplierCode == "ORVEAUX" && input.ParserVersion == OrveauxExcelPriorityStore.Version)
            return await new OrveauxExcelPriorityStore(db).SaveAsync(input, hash, rows, ct);
        if (folderIntake && input.SupplierCode == "ENEOS")
            return await new EneosInventoryStore(db).SaveAsync(input, hash, rows, ct);
        if (folderIntake && input.SupplierCode == "LA_VINEE")
            return await new LaVineeInventoryStore(db).SaveAsync(input, hash, rows, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190218)",ct);
        // One supplier's snapshots must be ordered even across processes.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({input.SupplierCode}, 0))", ct);
        var supplier = await db.Suppliers.SingleOrDefaultAsync(x => x.Code == input.SupplierCode, ct);
        if (supplier is null)
        {
            supplier = new Supplier { Code = input.SupplierCode, Name = input.SupplierName };
            db.Suppliers.Add(supplier);
        }
        var existing = await db.SourceDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.SupplierId == supplier.Id && x.FileHash == hash, ct);
        if (existing is not null)
        {
            if (input.SupplierCode == "ORVEAUX")
            {
                if (existing.ObservedAt != input.ObservedAt || !existing.Content.SequenceEqual(input.Content))
                    throw new FolderImportHeldException("保存済みオルヴォー原本の日付または内容が一致しません。");
                if (!await db.ExtractionJobs.AnyAsync(j => j.SourceDocumentId == existing.Id && j.ParserVersion == input.ParserVersion, ct))
                    throw new FolderImportHeldException("このオルヴォー原本は別の取込方式で保存済みです。既存商品の対応・数量差を照合してから再取込してください。原本全体を完了扱いにはしません。");
                var accepted = await db.SourceEvidence.CountAsync(x => x.SourceDocumentId == existing.Id, ct);
                await tx.CommitAsync(ct);
                return new(existing.Id, accepted, false, input.PendingHolds);
            }
            if (await db.ExtractionJobs.AnyAsync(j => j.SourceDocumentId == existing.Id && j.ParserVersion == OrveauxExactPairImportStore.ParserVersion, ct))
                throw new FolderImportHeldException("この原典は限定ペアのみ登録済みです。原典全体を取込済みとして扱えません。");
            var count = await db.SourceEvidence.CountAsync(x => x.SourceDocumentId == existing.Id, ct);
            db.OperationLogs.Add(new OperationLog { SourceDocumentId = existing.Id, Status = "AlreadyImported", RowCount = count, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(existing.Id, count, true);
        }
        // The same supplier lock covers duplicate, date conflict and commit across processes.
        if (folderIntake && await db.SourceDocuments.AnyAsync(x => x.SupplierId == supplier.Id && x.ObservedAt >= input.ObservedAt, ct))
            throw new FolderImportHeldException("同日別内容または新しい資料が登録済みです。資料日付と版を確認してください。");
        var source = new SourceDocument { SupplierId = supplier.Id, FileName = input.FileName, FileHash = hash,
            Content = input.Content, ObservedAt = input.ObservedAt, ImportedAt = DateTimeOffset.UtcNow };
        db.SourceDocuments.Add(source);
        db.ExtractionJobs.Add(new ExtractionJob { SourceDocumentId = source.Id, RowCount = rows.Count, ParserVersion = input.ParserVersion,
            Status = input.SupplierCode == "ORVEAUX" && input.PendingHolds is { Count: > 0 } ? "Partial" : "Completed" });
        var snapshot = new InventorySnapshot { SourceDocumentId = source.Id, ObservedAt = input.ObservedAt };
        db.InventorySnapshots.Add(snapshot);
        var products = await db.SupplierProducts.Where(x => x.SupplierId == supplier.Id).ToDictionaryAsync(x => x.SupplierProductCode, ct);
        var documents = await db.ProductSearchDocuments.Where(x => x.EntityType == SearchEntityType.SupplierProduct)
            .ToDictionaryAsync(x => x.SourceRecordId, ct);
        foreach (var row in rows)
        {
            if (!products.TryGetValue(row.SupplierProductCode, out var product))
            {
                product = new SupplierProduct { SupplierId = supplier.Id, SupplierProductCode = row.SupplierProductCode, SupplierProductName = row.ProductNameJa };
                db.SupplierProducts.Add(product);
                products.Add(row.SupplierProductCode, product);
                db.ReviewTasks.Add(new ReviewTask { SupplierProductId = product.Id });
            }
            db.PriceHistories.Add(new PriceHistory { SupplierProductId = product.Id, SourceDocumentId = source.Id,
                Amount = row.ReferenceRetailPrice, RawValue = row.PriceRaw, TaxIncluded = row.TaxIncluded, ObservedAt = input.ObservedAt });
            db.InventoryItems.Add(new InventoryItem { SupplierProductId = product.Id, InventorySnapshotId = snapshot.Id,
                Quantity = row.Inventory.Quantity, Status = row.Inventory.Status, RawValue = row.Inventory.RawValue });
            db.SourceEvidence.Add(new SourceEvidence { SourceDocumentId = source.Id, SupplierProductId = product.Id,
                SheetName = row.SourceSheet, RowNumber = row.SourceRow, RawCellsJson = row.RawCellsJson,
                ExtractedRowJson = JsonSerializer.Serialize(row) });
            if (product.CurrentObservedAt is not null && product.CurrentObservedAt > input.ObservedAt) continue;
            product.SupplierProductName = row.ProductNameJa;
            product.ProductNameEn = row.ProductNameEn;
            product.ProducerNameJa = row.ProducerNameJa;
            product.ProducerNameEn = row.ProducerNameEn;
            product.Country = row.Country;
            product.Region = row.Region;
            product.ProductType = row.ProductType;
            product.Vintage = row.Vintage;
            product.VintageRaw = row.VintageRaw;
            product.VolumeMl = row.VolumeMl;
            product.CurrentPrice = row.ReferenceRetailPrice;
            product.TaxIncluded = row.TaxIncluded;
            product.CurrentInventoryQuantity = row.Inventory.Quantity;
            product.CurrentInventoryStatus = row.Inventory.Status;
            product.CurrentObservedAt = input.ObservedAt;
            product.CurrentSourceDocumentId = source.Id;
            if (!documents.TryGetValue(product.Id, out var doc))
            {
                doc = new ProductSearchDocument { EntityType = SearchEntityType.SupplierProduct, SourceRecordId = product.Id, SearchText = "" };
                db.ProductSearchDocuments.Add(doc);
                documents.Add(product.Id, doc);
            }
            doc.SearchText = string.Join(" ", row.ProductNameJa, row.ProductNameEn, row.ProducerNameJa,
                row.ProducerNameEn, row.VintageRaw, row.ProductType, row.Country, row.Region,
                row.Grapes, row.AppellationJa, row.AppellationEn, row.OrganicCategory, row.Description, row.OriginalSupplierProductCode);
            doc.StructuredDataJson = JsonSerializer.Serialize(new { row.SupplierProductCode, row.JanCodes });
            doc.UpdatedAt = DateTimeOffset.UtcNow;
        }
        db.OperationLogs.Add(new OperationLog { SourceDocumentId = source.Id,
            Status = input.SupplierCode == "ORVEAUX" && input.PendingHolds is { Count: > 0 } ? "Partial" : "Completed",
            RowCount = rows.Count, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(source.Id, rows.Count, false, input.SupplierCode == "ORVEAUX" ? input.PendingHolds : null)
            { AddedProducts = input.SupplierCode == "ORVEAUX" ? rows.Count : 0 };
    }
}
