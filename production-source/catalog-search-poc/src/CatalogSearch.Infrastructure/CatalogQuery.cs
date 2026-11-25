using CatalogSearch.Application;
using CatalogSearch.Domain;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using System.Text.Json;
namespace CatalogSearch.Infrastructure;
public sealed class CatalogQuery(CatalogSearchDbContext db, IImporterEnrichmentReader? enrichment = null, string? enrichmentVersion = null)
{
    public async Task<SemanticCatalogSearchResult> SearchSemanticAsync(SemanticSearchPlan plan, CancellationToken ct = default)
    {
        plan.RequireExecutable();
        if (plan.CountryAliases is null || plan.CountryAliases.Count > 3 ||
            !JapaneseWineDictionary.AreCompatibleCountryAliases(plan.CountryAliases) ||
            plan.CountryAliases.Distinct(StringComparer.Ordinal).Count() != plan.CountryAliases.Count ||
            (plan.CountryAliases.Count > 0 && !plan.CountryAliases.Contains(plan.Filter.Country ?? "", StringComparer.Ordinal)))
            throw new ArgumentException("Invalid reviewed country aliases.");
        // The wire contract remains capped at 20 canonical regions. Reviewed geography
        // may expand those into multiple exact source spellings before this internal query.
        if (plan.Regions.Count > 128 || plan.Regions.Any(string.IsNullOrWhiteSpace) ||
            (plan.Taste is not null && !WineTasteEvidence.Supports(plan.Taste)) ||
            (plan.Grape is not null && (!GrapeRegistry.TryGetAliases(plan.Grape.Name, out _) || plan.Grape.Mode is not ("contains" or "predominant"))))
            throw new ArgumentException("Invalid semantic predicate.");
        if (plan.Jan is { } jan && (jan.Length is not (8 or 12 or 13 or 14) || !jan.All(char.IsAsciiDigit)))
            throw new ArgumentException("Invalid JAN/GTIN predicate.");
        var supplier = await ResolveSupplierAsync(plan.SupplierCode, ct);
        var counters = new SemanticCounters();
        var rows = await SearchPlanAsync(new(plan.Filter with { Text = null, Region = null }, false, [], SupplierId: supplier),
            plan.Keywords, ct, plan, counters);
        return new(rows, counters.Unavailable, counters.Conflicts, true);
    }

    private sealed class SemanticCounters
    {
        public int Unavailable; public int Conflicts;
        public Dictionary<Guid, ImporterSearchEvidence> Sources { get; } = new();
    }
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
            Filter = ProductTypeVocabulary.MergeUi(ui,ui with { Country = Prefer(ui.Country, f.Country), Region = Prefer(ui.Region, f.Region), ProductType = Prefer(ui.ProductType, f.ProductType), VolumeMl = ui.VolumeMl ?? f.VolumeMl, VintageRaw = Prefer(ui.VintageRaw, f.VintageRaw), PriceMin = ui.PriceMin ?? f.PriceMin, PriceMax = ui.PriceMax ?? f.PriceMax, AvailableOnly = ui.AvailableOnly || f.AvailableOnly, Text = f.Text, Category=f.Category, Color=f.Color, Effervescence=f.Effervescence, Sweetness=f.Sweetness, TypePredicate=f.TypePredicate },sparkling:parsed.Sparkling||parsed.Champagne),
            MinExclusive = ui.PriceMin is null && parsed.MinExclusive,
            MaxExclusive = ui.PriceMax is null && parsed.MaxExclusive,
            Champagne = string.IsNullOrWhiteSpace(ui.Region) && string.IsNullOrWhiteSpace(ui.ProductType) && parsed.Champagne,
            Sparkling = (ui.TypePredicate??ProductTypeVocabulary.Resolve(ui)).Effervescence is null && parsed.Sparkling
        };
    }

    private async Task<IReadOnlyList<CatalogResult>> SearchPlanAsync(NaturalSearchPlan natural, IReadOnlyList<string>? literalTerms, CancellationToken ct,
        SemanticSearchPlan? semantic = null, SemanticCounters? counters = null)
    {
        var filter = natural.Filter;
        if (filter.Limit is < 1 or > 2000 || filter.PriceMax < 0 || filter.PriceMin < 0 || filter.PriceMin > filter.PriceMax || filter.VolumeMl <= 0) throw new ArgumentException("Invalid search limits.");
        var query = db.SupplierProducts.AsNoTracking();
        if (natural.SupplierId is { } supplierId) query = query.Where(x => x.SupplierId == supplierId);
        if (!string.IsNullOrWhiteSpace(filter.Country))
        {
            var rawCountries = await db.SupplierProducts.AsNoTracking().Select(x => x.Country).Distinct().ToListAsync(ct);
            var matchingCountries = CatalogFacetVocabulary.MatchingCountries(rawCountries, filter.Country);
            query = query.Where(x => matchingCountries.Contains(x.Country));
        }
        else if (semantic?.CountryAliases.Count > 0) query = query.Where(x => semantic.CountryAliases.Contains(x.Country));
        var requestedRegions = semantic?.Regions.Count > 0 ? semantic.Regions :
            !string.IsNullOrWhiteSpace(filter.Region) ? new[] { filter.Region } : [];
        if (requestedRegions.Count > 0)
        {
            var rawRegions = await db.SupplierProducts.AsNoTracking().Select(x => new { x.Country, x.Region }).Distinct().ToListAsync(ct);
            var matchingRegions = CatalogFacetVocabulary.MatchingRegions(rawRegions.Select(x => (x.Country, x.Region)), filter.Country, requestedRegions);
            query = query.Where(x => matchingRegions.Contains(x.Region));
        }
        var typePredicate = filter.TypePredicate ?? ProductTypeVocabulary.Resolve(filter);
        if(typePredicate.Effervescence is null&&(natural.Sparkling||natural.Champagne||semantic?.Sparkling==true))typePredicate=typePredicate with {Effervescence="スパークリング"};
        var sweetness=typePredicate.Sweetness??WineTasteVocabulary.ResolveSweetness(semantic?.Taste??"");
        var flavor=semantic?.Taste is { } requestedTaste&&WineTasteVocabulary.ResolveSweetness(requestedTaste) is null?requestedTaste:null;
        if(typePredicate.Category is not null||typePredicate.Color is not null||typePredicate.Effervescence is not null||typePredicate.ExactRaw is not null)
        {
            var rawTypes=await db.SupplierProducts.AsNoTracking().Select(x=>x.ProductType).Distinct().ToListAsync(ct);
            var matchingTypes=rawTypes.Where(raw=>ProductTypeVocabulary.Matches(raw,typePredicate,includeSweetness:false)).ToArray();
            query=query.Where(x=>matchingTypes.Contains(x.ProductType));
        }
        if(natural.Champagne)query=query.Where(x=>x.Region=="シャンパーニュ"||x.Region=="シャンパーニュ地方"||x.Region=="Champagne");
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
        if (semantic?.Jan is { } jan)
        {
            var payload = JsonSerializer.Serialize(new { JanCodes = new[] { jan } });
            query = query.Where(x => db.ProductSearchDocuments.Any(d => d.EntityType == SearchEntityType.SupplierProduct &&
                d.SourceRecordId == x.Id && EF.Functions.JsonContains(d.StructuredDataJson, payload)));
        }
        var terms = literalTerms ?? SearchTerms.Parse(filter.Text);
        if (terms.Count > 20 || terms.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 200)) throw new ArgumentException("Invalid literal search terms.");
        foreach (var term in terms)
        {
            var pattern = SearchTerms.Pattern(term);
            var useFts = !term.Any(c => c is '%' or '_' or '\\');
            query = query.Where(x => EF.Functions.ILike(x.SupplierProductCode, pattern, "\\") || db.ProductSearchDocuments.Any(d => d.EntityType == SearchEntityType.SupplierProduct && d.SourceRecordId == x.Id &&
                ((useFts && EF.Property<NpgsqlTsVector>(d, "SearchVector").Matches(EF.Functions.PlainToTsQuery("simple", term))) || EF.Functions.ILike(d.SearchText, pattern, "\\"))));
        }
        var matches = (from p in query
            join supplier in db.Suppliers.AsNoTracking() on p.SupplierId equals supplier.Id
            join e in db.SourceEvidence.AsNoTracking() on new { ProductId = p.Id, SourceId = p.CurrentSourceDocumentId }
                equals new { ProductId = e.SupplierProductId, SourceId = (Guid?)e.SourceDocumentId }
            orderby p.SupplierProductCode, p.Id, e.Id
            select new { supplier.Code, e.RawCellsJson, e.ExtractedRowJson, Result = new CatalogResult(p.SupplierProductCode, p.SupplierProductName, p.ProductNameEn,
                p.ProducerNameJa, p.ProducerNameEn, p.Vintage, p.VintageRaw, p.VolumeMl,
                p.CurrentPrice, p.TaxIncluded, p.CurrentInventoryStatus, p.CurrentInventoryQuantity,
                e.SheetName, e.RowNumber, e.SourceDocumentId, p.Country, p.Region, p.ProductType) { SupplierName = supplier.Name, SupplierProductId = p.Id, CodeKind = p.CodeKind, OriginalSupplierProductCode = p.OriginalSupplierProductCode } })
            ;
        // Evaluate evidence before the result limit. A matching row after the first N candidates must remain eligible.
        var selected = await SemanticResultSelector.SelectAsync(
            (semantic?.Grape is null && sweetness is null && flavor is null ? matches.Take(filter.Limit) : matches).AsAsyncEnumerable(),
            x => (sweetness is null || WineTasteEvidence.Matches(x.ExtractedRowJson, sweetness, x.Result.ProductType)) &&
                (flavor is null || WineTasteEvidence.Matches(x.ExtractedRowJson, flavor, x.Result.ProductType)) &&
                (semantic?.Grape is not { } grape || AcceptGrape(x.Code, x.Result, x.ExtractedRowJson, grape, counters!)), filter.Limit, ct);
        return selected.Select(x => x.Result with { AbundantInventory = HasAbundantInventory(x.ExtractedRowJson), SpecialPurchasePrice = SpecialPurchasePrice.Read(x.Code, x.RawCellsJson), DuplicateMinimumSelected = x.Code == "LA_VINEE" && HasDuplicateSelection(x.ExtractedRowJson),
            ImporterEvidence = counters is not null && counters.Sources.TryGetValue(x.Result.SupplierProductId, out var source) ? source : null }).ToArray();
    }

    private bool AcceptGrape(string supplierCode, CatalogResult product, string extractedJson, SemanticGrapePredicate grape, SemanticCounters counts)
    {
        var original = SemanticGrapeMatcher.ReadOriginal(extractedJson);
        if (string.IsNullOrWhiteSpace(original) && supplierCode == "FINESSE")
            original = SemanticGrapeMatcher.ReadFinesseDescriptionClaim(extractedJson, product.ProductNameJa);
        var decision = SemanticGrapeMatcher.Match(original, grape);
        // Enrichment lookup is bound to current source bytes and exact product identity, not just name.
        if (enrichment is not null)
        {
            if (string.IsNullOrWhiteSpace(enrichmentVersion)) throw new ArgumentException("Enrichment configuration version is required.");
            var identity = new ImporterProductIdentity(supplierCode, product.Sku, product.ProducerNameJa,
                product.ProductNameJa, product.VintageRaw, product.VolumeMl, product.ProductType);
            var saved = enrichment.ReadVerifiedForProduct(identity,
                SourceFingerprint.Create(product.SourceDocumentId, extractedJson), enrichmentVersion, DateTimeOffset.UtcNow);
            if (saved.Status == "conflict")
            {
                if(saved.Content is not null&&ImporterEnrichmentAcquisition.HasExclusiveCompositionConflict(
                    string.IsNullOrWhiteSpace(original)?[]:[original],saved.Content.Grapes))
                { counts.Conflicts++; return false; }
                GrapeRegistry.TryGetAliases(grape.Name, out var conflictAliases);
                if (saved.Content is null || saved.Content.Grapes.Any(g => conflictAliases.Contains(
                    g.Grape.Replace("サンジョヴェーゼ", "サンジョベーゼ", StringComparison.Ordinal), StringComparer.OrdinalIgnoreCase)))
                { counts.Conflicts++; return false; }
            }
            if (saved.Status == "verified" && saved.Content is { } content)
            {
                GrapeRegistry.TryGetAliases(grape.Name, out var aliases);
                var relevant = content.Grapes.Where(g => aliases.Contains(g.Grape.Replace("サンジョヴェーゼ", "サンジョベーゼ", StringComparison.Ordinal), StringComparer.OrdinalIgnoreCase)).ToArray();
                if (relevant.Length == 1)
                {
                    var supplied = SemanticGrapeMatcher.Match(relevant[0].SourceExcerpt, grape);
                    if (decision.ExplicitPercent is { } a && supplied.ExplicitPercent is { } b && a != b)
                    { counts.Conflicts++; return false; }
                    var originalMajority = SemanticGrapeMatcher.Match(original, grape with { Mode = "predominant" });
                    var suppliedMajority = SemanticGrapeMatcher.Match(relevant[0].SourceExcerpt, grape with { Mode = "predominant" });
                    if ((originalMajority.Included && suppliedMajority.ExplicitPercent <= 50) ||
                        (suppliedMajority.Included && originalMajority.ExplicitPercent <= 50))
                    { counts.Conflicts++; return false; }
                    if (supplied.Included)
                    {
                        counts.Sources[product.SupplierProductId] = new(content.FinalUrl, content.FetchedAtUtc,
                            grape.Name, relevant[0].SourceExcerpt, saved.ContentRecordSha256!);
                        return true;
                    }
                }
            }
        }
        if (decision.Included) return true;
        // An explicit zero/minority is a known non-match; unknown evidence is separately visible.
        if (decision.ExplicitPercent is null) counts.Unavailable++;
        return false;
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
