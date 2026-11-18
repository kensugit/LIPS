using CatalogSearch.Application;
using CatalogSearch.Domain;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
namespace CatalogSearch.Infrastructure;
public sealed class CatalogQuery(CatalogSearchDbContext db)
{
    public async Task<NaturalSearchPlan> InterpretAsync(CatalogFilter filter, CancellationToken ct = default)
    {
        var suppliers = await db.Suppliers.AsNoTracking().Select(x => new SupplierSearchName(x.Id, x.Name, x.Code)).ToListAsync(ct);
        return NaturalSearch.Parse(filter, suppliers);
    }

    public async Task<IReadOnlyList<CatalogResult>> SearchAsync(CatalogFilter filter, CancellationToken ct = default)
    {
        var natural = await InterpretAsync(filter, ct);
        return await SearchPlanAsync(natural, null, ct);
    }

    public async Task<IReadOnlyList<CatalogResult>> SearchStructuredAsync(StructuredSearchPlan plan, CancellationToken ct = default)
    {
        var supplierId = await ResolveSupplierAsync(plan.SupplierCode, ct);
        // Model residuals are literal terms. They must never pass through NaturalSearch.
        return await SearchPlanAsync(new(plan.Filter with { Text = null }, false, [], SupplierId: supplierId), plan.Keywords, ct);
    }

    private async Task<Guid?> ResolveSupplierAsync(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var ids = await db.Suppliers.AsNoTracking().Where(s => s.Code == code).Select(s => s.Id).Take(2).ToListAsync(ct);
        if (ids.Count != 1) throw new ArgumentException("Supplier code must identify exactly one supplier.");
        return ids[0];
    }

    public async Task<IReadOnlyList<CatalogResult>> SearchRulesWithUiAsync(CatalogFilter ui, string? supplier, string text, CancellationToken ct = default)
    {
        var parsed = await InterpretAsync(new CatalogFilter(Text: text, Limit: ui.Limit), ct);
        var merged = MergeRulesWithUi(parsed, ui);
        if (!string.IsNullOrWhiteSpace(supplier)) merged = merged with { SupplierId = await ResolveSupplierAsync(supplier, ct) };
        return await SearchPlanAsync(merged, null, ct);
    }

    public static NaturalSearchPlan MergeRulesWithUi(NaturalSearchPlan parsed, CatalogFilter ui)
    {
        static string? Prefer(string? explicitValue, string? interpreted) => string.IsNullOrWhiteSpace(explicitValue) ? interpreted : explicitValue;
        var f = parsed.Filter;
        return parsed with
        {
            Filter = ui with { Country = Prefer(ui.Country, f.Country), Region = Prefer(ui.Region, f.Region), ProductType = Prefer(ui.ProductType, f.ProductType), VolumeMl = ui.VolumeMl ?? f.VolumeMl, VintageRaw = Prefer(ui.VintageRaw, f.VintageRaw), PriceMin = ui.PriceMin ?? f.PriceMin, PriceMax = ui.PriceMax ?? f.PriceMax, AvailableOnly = ui.AvailableOnly || f.AvailableOnly, Text = f.Text },
            MinExclusive = ui.PriceMin is null && parsed.MinExclusive,
            MaxExclusive = ui.PriceMax is null && parsed.MaxExclusive,
            Champagne = string.IsNullOrWhiteSpace(ui.Region) && string.IsNullOrWhiteSpace(ui.ProductType) && parsed.Champagne,
            Sparkling = string.IsNullOrWhiteSpace(ui.ProductType) && parsed.Sparkling
        };
    }

    private async Task<IReadOnlyList<CatalogResult>> SearchPlanAsync(NaturalSearchPlan natural, IReadOnlyList<string>? literalTerms, CancellationToken ct)
    {
        var filter = natural.Filter;
        if (filter.Limit is < 1 or > 2000 || filter.PriceMax < 0 || filter.PriceMin < 0 || filter.PriceMin > filter.PriceMax || filter.VolumeMl <= 0) throw new ArgumentException("Invalid search limits.");
        var query = db.SupplierProducts.AsNoTracking();
        if (natural.SupplierId is { } supplierId) query = query.Where(x => x.SupplierId == supplierId);
        if (!string.IsNullOrWhiteSpace(filter.Country)) query = query.Where(x => x.Country == filter.Country);
        if (!string.IsNullOrWhiteSpace(filter.Region)) query = query.Where(x => x.Region == filter.Region);
        if (!string.IsNullOrWhiteSpace(filter.ProductType))
            query = natural.Sparkling || natural.Champagne
                ? query.Where(x => x.ProductType.Contains(filter.ProductType))
                : query.Where(x => x.ProductType == filter.ProductType);
        if (natural.Sparkling) query = query.Where(x => x.ProductType.Contains("泡") || x.ProductType == "スパークリング");
        if (natural.Champagne) query = query.Where(x => (x.Region == "シャンパーニュ" || x.Region == "シャンパーニュ地方" || x.Region == "Champagne") && (x.ProductType.Contains("泡") || x.ProductType == "スパークリング"));
        if (filter.VolumeMl is not null) query = query.Where(x => x.VolumeMl == filter.VolumeMl);
        if (!string.IsNullOrWhiteSpace(filter.VintageRaw))
        {
            var rawVintages = await db.SupplierProducts.AsNoTracking().Select(x => x.VintageRaw).Distinct().ToListAsync(ct);
            var matchingVintages = VintageNotation.MatchingRawValues(rawVintages, filter.VintageRaw);
            query = query.Where(x => matchingVintages.Contains(x.VintageRaw));
        }
        if (filter.PriceMin is not null) query = query.Where(x => !x.TaxIncluded && x.CurrentPrice != null && x.CurrentPrice >= filter.PriceMin);
        // The milestone asks for tax-exclusive reference retail prices; do not mix tax bases.
        if (filter.PriceMax is not null) query = query.Where(x => !x.TaxIncluded && x.CurrentPrice != null && x.CurrentPrice <= filter.PriceMax);
        if (natural.MinExclusive) query = query.Where(x => x.CurrentPrice > filter.PriceMin);
        if (natural.MaxExclusive) query = query.Where(x => x.CurrentPrice < filter.PriceMax);
        if (filter.AvailableOnly) query = query.Where(x => x.CurrentInventoryStatus == InventoryStatus.InStock || x.CurrentInventoryStatus == InventoryStatus.LowStock);
        if (filter.Text?.Length > 200) throw new ArgumentException("Search text is too long.");
        var terms = literalTerms ?? SearchTerms.Parse(filter.Text);
        if (terms.Count > 20 || terms.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 200)) throw new ArgumentException("Invalid literal search terms.");
        foreach (var term in terms)
        {
            var pattern = SearchTerms.Pattern(term);
            var useFts = !term.Any(c => c is '%' or '_' or '\\');
            query = query.Where(x => EF.Functions.ILike(x.SupplierProductCode, pattern, "\\") || db.ProductSearchDocuments.Any(d => d.EntityType == SearchEntityType.SupplierProduct && d.SourceRecordId == x.Id &&
                ((useFts && EF.Property<NpgsqlTsVector>(d, "SearchVector").Matches(EF.Functions.PlainToTsQuery("simple", term))) || EF.Functions.ILike(d.SearchText, pattern, "\\"))));
        }
        var matches = await (from p in query
            join supplier in db.Suppliers.AsNoTracking() on p.SupplierId equals supplier.Id
            join e in db.SourceEvidence.AsNoTracking() on new { ProductId = p.Id, SourceId = p.CurrentSourceDocumentId }
                equals new { ProductId = e.SupplierProductId, SourceId = (Guid?)e.SourceDocumentId }
            orderby p.SupplierProductCode
            select new { supplier.Code, e.RawCellsJson, e.ExtractedRowJson, Result = new CatalogResult(p.SupplierProductCode, p.SupplierProductName, p.ProductNameEn,
                p.ProducerNameJa, p.ProducerNameEn, p.Vintage, p.VintageRaw, p.VolumeMl,
                p.CurrentPrice, p.TaxIncluded, p.CurrentInventoryStatus, p.CurrentInventoryQuantity,
                e.SheetName, e.RowNumber, e.SourceDocumentId, p.Country, p.Region, p.ProductType) { SupplierName = supplier.Name, SupplierProductId = p.Id, CodeKind = p.CodeKind, OriginalSupplierProductCode = p.OriginalSupplierProductCode } })
            .Take(filter.Limit).ToListAsync(ct);
        return matches.Select(x => x.Result with { AbundantInventory = HasAbundantInventory(x.ExtractedRowJson), SpecialPurchasePrice = SpecialPurchasePrice.Read(x.Code, x.RawCellsJson), DuplicateMinimumSelected = x.Code == "LA_VINEE" && HasDuplicateSelection(x.ExtractedRowJson) }).ToList();
    }
    private static bool HasAbundantInventory(string json)
    {
        try { using var doc=System.Text.Json.JsonDocument.Parse(json);return doc.RootElement.TryGetProperty("Inventory",out var stock)
            && stock.TryGetProperty("RawValue",out var raw) && raw.ValueKind==System.Text.Json.JsonValueKind.String && InventoryValue.IsAbundant(raw.GetString()); }
        catch(System.Text.Json.JsonException) { return false; }
    }
    private static bool HasDuplicateSelection(string json)
    {
        try { using var doc=System.Text.Json.JsonDocument.Parse(json);return doc.RootElement.TryGetProperty("DuplicateMinimumSelected",out var value)&&value.ValueKind==System.Text.Json.JsonValueKind.True; }
        catch(System.Text.Json.JsonException) { return false; }
    }
}
