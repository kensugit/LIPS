using System.Security.Cryptography;
using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace CatalogSearch.Infrastructure;

public sealed record ExactPairImportReceipt(Guid OperationId, Guid InternalIdentityId, Guid SupplierProductId,
    Guid ExcelSourceId, Guid PdfSourceId, bool AlreadyImported, int AddedProducts);

public sealed class OrveauxExactPairImportStore(CatalogSearchDbContext db)
{
    public const string ParserVersion = "OrveauxExactPair/1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Task<ExactPairImportReceipt?> Get(CancellationToken ct = default) => Read(null, ct);
    public Task<ExactPairImportReceipt?> GetOperation(Guid operationId, CancellationToken ct = default) => Read(operationId, ct);
    private async Task<ExactPairImportReceipt?> Read(Guid? operation, CancellationToken ct)
    {
        await using var c = new NpgsqlConnection(db.Database.GetConnectionString()); await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(operation.HasValue
            ? "SELECT \"Body\"::text FROM \"ExactSourcePairImportOperations\" WHERE \"OperationId\"=@key"
            : "SELECT \"Body\"::text FROM \"ExactSourcePairImports\" WHERE \"PairKey\"=@key", c);
        if (operation.HasValue) cmd.Parameters.AddWithValue("key", operation.Value); else cmd.Parameters.AddWithValue("key", OrveauxExactPairStore.PairKey);
        return await cmd.ExecuteScalarAsync(ct) is string body ? JsonSerializer.Deserialize<ExactPairImportReceipt>(body, Json) : null;
    }

    public async Task<ExactPairImportReceipt> Import(Guid operationId, OrveauxPairCandidate candidate, byte[] excel,
        byte[] pdf, byte[] answers, DateTimeOffset observedAt, CancellationToken ct = default, ExactPairReviewSelection? reviews = null)
    {
        if (operationId == Guid.Empty || observedAt != new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero))
            throw new ExactPairConflictException("Explicit Excel G1 reference date 2026-09-30 UTC is required.");
        var evidence = OrveauxAocPairPolicy.Resolve(candidate, excel, pdf, answers);
        if (evidence.Status != "ResolvedEvidenceOnly") throw new ExactPairConflictException(evidence.Reason);
        var identity = await new OrveauxExactPairStore(db.Database.GetConnectionString()!).Save(operationId, 0, candidate, excel, pdf, answers, ct);
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { candidate, observedAt }, Json)));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190218)", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"ORVEAUX"},0))", ct);
        var c = (NpgsqlConnection)db.Database.GetDbConnection(); var nt = (NpgsqlTransaction)tx.GetDbTransaction();
        if (reviews is not null)
        {
            if (reviews.Excel.Id == reviews.Pdf.Id) throw new IntakeConflictException();
            foreach (var pair in new[] { (Reference: reviews.Excel, Excel: true), (Reference: reviews.Pdf, Excel: false) }.OrderBy(x => x.Reference.Id))
            {
                await using var guard = new NpgsqlCommand("SELECT \"Body\"::text FROM \"IntakeReviews\" WHERE \"Id\"=@id FOR UPDATE", c, nt);
                guard.Parameters.AddWithValue("id", pair.Reference.Id);
                var body = await guard.ExecuteScalarAsync(ct) as string ?? throw new IntakeConflictException();
                OrveauxPairReviewService.Validate(JsonSerializer.Deserialize<IntakeReview>(body, Json)!, pair.Reference, pair.Excel);
            }
        }
        async Task<ExactPairImportReceipt?> Existing(string table, string column, object key)
        {
            await using var cmd = new NpgsqlCommand($"SELECT \"RequestHash\",\"Body\"::text FROM \"{table}\" WHERE \"{column}\"=@key", c, nt);
            cmd.Parameters.AddWithValue("key", key); await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            if (reader.GetString(0) != hash) throw new ExactPairConflictException("Existing import request differs.");
            return JsonSerializer.Deserialize<ExactPairImportReceipt>(reader.GetString(1), Json)!;
        }
        var previous = await Existing("ExactSourcePairImportOperations", "OperationId", operationId);
        if (previous is not null) { await tx.CommitAsync(ct); return previous with { AlreadyImported = true, AddedProducts = 0 }; }
        var existingPair = await Existing("ExactSourcePairImports", "PairKey", OrveauxExactPairStore.PairKey);
        ExactPairImportReceipt receipt;
        if (existingPair is not null)
            receipt = existingPair with { OperationId = operationId, AlreadyImported = true, AddedProducts = 0 };
        else
        {
            var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Code == "ORVEAUX", ct);
            if (supplier is null) { supplier = new() { Code = "ORVEAUX", Name = "オルヴォー" }; db.Suppliers.Add(supplier); }
            var hashes = new[] { candidate.Excel.Hash.ToUpperInvariant(), candidate.Pdf.Hash.ToUpperInvariant() };
            if (await db.SourceDocuments.AnyAsync(s => s.SupplierId == supplier.Id && (hashes.Contains(s.FileHash.ToUpper()) || s.ObservedAt >= observedAt), ct))
                throw new ExactPairConflictException("An existing source requires explicit reconciliation; no automatic replacement.");
            var internalId = identity.Link.ReservedProductId;
            var product = new SupplierProduct { SupplierId = supplier.Id, SupplierProductCode = "internal:" + internalId.ToString("D"),
                CodeKind = "InternalIdentity", OriginalSupplierProductCode = null, ProductId = null,
                SupplierProductName = candidate.Excel.JapaneseName + " / " + candidate.Pdf.JapaneseName,
                ProductNameEn = candidate.Excel.LatinName + " / " + candidate.Pdf.LatinName,
                Vintage = 2024, VintageRaw = "2024", VolumeMl = 750, CurrentPrice = null, TaxIncluded = false,
                CurrentInventoryQuantity = 60, CurrentInventoryStatus = InventoryStatus.InStock, CurrentObservedAt = observedAt };
            db.SupplierProducts.Add(product);
            var sources = new List<SourceDocument>();
            for (var i = 0; i < 2; i++)
            {
                var src = i == 0 ? candidate.Excel : candidate.Pdf;
                var source = new SourceDocument { SupplierId = supplier.Id, FileName = i == 0 ? "202610新着＆現在庫.xlsx" : "orveaux-public-202610.pdf",
                    FileHash = hashes[i], Content = i == 0 ? excel : pdf, ObservedAt = observedAt, ImportedAt = DateTimeOffset.UtcNow };
                sources.Add(source); db.SourceDocuments.Add(source);
                db.ExtractionJobs.Add(new() { SourceDocumentId = source.Id, ParserVersion = ParserVersion, RowCount = 1, Status = "Partial" });
                var raw = JsonSerializer.Serialize(new { source = src, observedAtBasis = "Excel G1; PDF date not independently asserted",
                    originalSupplierProductCode = (string?)null, internalIdentityId = internalId, evidence.AnswerBinding, evidence.AnswerVersion });
                var prices = evidence.Prices.Where(p => i == 0 || p.Location.Contains("PDF15", StringComparison.Ordinal)).Select(p => new SourcePriceComponent(p.Role, p.Amount, p.TaxIncluded, p.Unit, p.Raw)).ToArray();
                var row = new SupplierInventoryRow(product.SupplierProductCode, "", "", src.JapaneseName, src.LatinName, 2024, "2024", 750, 0,
                    null, "", false, new(60, InventoryStatus.InStock, i == 0 ? "60" : "60本"), "", "", "", [], "", null, "", "", "", "", "単位未確定の価格は採用せず原典別に保持", "", "", i == 0 ? 66 : 15, i == 0 ? "新着 " : "PDF")
                { RawCellsJson = raw, PriceComponents = prices, Description = JsonSerializer.Serialize(new { src.Location, InternalIdentityId = internalId, SourceVolumeMl = src.VolumeMl, ResolvedVolumeSource = "PDF15/2024", Pair = evidence }) };
                db.SourceEvidence.Add(new() { SourceDocumentId = source.Id, SupplierProductId = product.Id, SheetName = row.SourceSheet, RowNumber = row.SourceRow,
                    RawCellsJson = raw, ExtractedRowJson = JsonSerializer.Serialize(row) });
                var snapshot = new InventorySnapshot { SourceDocumentId = source.Id, ObservedAt = observedAt }; db.InventorySnapshots.Add(snapshot);
                db.InventoryItems.Add(new() { SupplierProductId = product.Id, InventorySnapshotId = snapshot.Id, Quantity = 60, Status = InventoryStatus.InStock, RawValue = row.Inventory.RawValue });
                db.PriceHistories.Add(new() { SupplierProductId = product.Id, SourceDocumentId = source.Id, Amount = null, RawValue = "", TaxIncluded = false, ObservedAt = observedAt });
            }
            product.CurrentSourceDocumentId = sources[0].Id;
            db.ProductSearchDocuments.Add(new() { EntityType = SearchEntityType.SupplierProduct, SourceRecordId = product.Id,
                SearchText = product.SupplierProductName + " " + product.ProductNameEn + " 2024 750",
                StructuredDataJson = JsonSerializer.Serialize(new { product.CodeKind, product.OriginalSupplierProductCode, InternalIdentityId = internalId }) });
            db.ReviewTasks.Add(new() { SupplierProductId = product.Id, Reason = "Exact pair identity confirmed; canonical product remains unassigned" });
            db.OperationLogs.Add(new() { Action = ParserVersion, SourceDocumentId = sources[0].Id, Status = "ExactPairOnly", RowCount = 1 });
            await db.SaveChangesAsync(ct);
            receipt = new(operationId, internalId, product.Id, sources[0].Id, sources[1].Id, false, 1);
            await using var insert = new NpgsqlCommand("INSERT INTO \"ExactSourcePairImports\" (\"PairKey\",\"SupplierProductId\",\"RequestHash\",\"Body\") VALUES (@key,@product,@hash,@body)", c, nt);
            insert.Parameters.AddWithValue("key", OrveauxExactPairStore.PairKey); insert.Parameters.AddWithValue("product", product.Id);
            insert.Parameters.AddWithValue("hash", hash); insert.Parameters.AddWithValue("body", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(receipt, Json)); await insert.ExecuteNonQueryAsync(ct);
        }
        await using (var cmd = new NpgsqlCommand("INSERT INTO \"ExactSourcePairImportOperations\" (\"OperationId\",\"PairKey\",\"RequestHash\",\"Body\") VALUES (@id,@key,@hash,@body)", c, nt))
        {
            cmd.Parameters.AddWithValue("id", operationId); cmd.Parameters.AddWithValue("key", OrveauxExactPairStore.PairKey);
            cmd.Parameters.AddWithValue("hash", hash); cmd.Parameters.AddWithValue("body", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(receipt, Json)); await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct); return receipt;
    }
}
