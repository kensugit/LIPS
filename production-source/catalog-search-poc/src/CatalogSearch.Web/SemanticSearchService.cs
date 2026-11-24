using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CatalogSearch.Web;

public sealed record SemanticCatalogSnapshot(string Version, SemanticSearchOptions Options)
{
    // The bridge registry requires unique names. Local geography can disambiguate exact country/name pairs.
    public IReadOnlyList<SemanticRegionOption> RegionPairs { get; init; } = Options.Regions;
}
public sealed record SemanticUiResult(IReadOnlyList<CatalogResult> Rows, bool Capped, string CatalogVersion,
    string Mode, string? FallbackReason, object Conditions, ExplicitSearchUi ExplicitUi,
    IReadOnlyList<string> UnparsedTerms, IReadOnlyList<string> ClarificationReasons,
    int EvidenceUnavailableCount, int EvidenceConflictCount, bool Complete);
public interface ISemanticSearchRead : IAsyncDisposable
{
    Task<SemanticCatalogSnapshot> SnapshotAsync(CancellationToken ct);
    Task<SemanticCatalogSearchResult> SearchAsync(SemanticSearchPlan plan, CancellationToken ct);
}
public interface ISemanticSearchStore
{
    Task<SemanticCatalogSnapshot> SnapshotAsync(CancellationToken ct);
    Task<ISemanticSearchRead> BeginReadAsync(CancellationToken ct);
}

public sealed class DatabaseSemanticSearchStore(CatalogSearchDbContext db, IConfiguration configuration) : ISemanticSearchStore
{
    public async Task<SemanticCatalogSnapshot> SnapshotAsync(CancellationToken ct)
    {
        await using var read = await BeginReadAsync(ct);
        return await read.SnapshotAsync(ct);
    }
    public async Task<ISemanticSearchRead> BeginReadAsync(CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
            var (reader, extractorVersion) = ImporterEnrichmentReaderConfiguration.Create(configuration);
            return new Read(db, tx, reader, extractorVersion);
        }
        catch { await tx.DisposeAsync(); throw; }
    }
    sealed class Read(CatalogSearchDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, IImporterEnrichmentReader? enrichment, string? extractorVersion) : ISemanticSearchRead
    {
        public async Task<SemanticCatalogSnapshot> SnapshotAsync(CancellationToken ct)
        {
            var snapshot = await new CatalogSearchSnapshotProvider(db).CaptureAsync(ct);
            var pairs = await db.SupplierProducts.AsNoTracking().Select(x => new { x.Country, x.Region }).Distinct().ToListAsync(ct);
            // Ambiguous names are not assigned to an invented country. Omit them from this v2 registry.
            var regions = pairs.Where(x => !string.IsNullOrWhiteSpace(x.Country) && !string.IsNullOrWhiteSpace(x.Region))
                .GroupBy(x => x.Region, StringComparer.Ordinal).Where(g => g.Select(x => x.Country).Distinct().Count() == 1 || g.All(x => x.Country is "France" or "フランス"))
                .Select(g => new SemanticRegionOption(g.Key, g.All(x => x.Country is "France" or "フランス") && g.Any(x => x.Country == "フランス") ? "フランス" : g.First().Country)).OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
            var o = snapshot.Options;
            var options = new SemanticSearchOptions(o.Countries, regions, o.ProductTypes, o.VolumesMl, o.Vintages, o.Suppliers, GrapeRegistry.Names);
            var identity = JsonSerializer.Serialize(new { schema = "semantic-snapshot/v1", sourceVersion = snapshot.Version,
                promptVersion = SemanticSearchContract.PromptVersion, options, geography = ReviewedRightBankScope.Definition,
                japaneseDictionary = JapaneseWineDictionary.Revision, searchVocabulary = CatalogSearchVocabulary.Revision,
                aliases = GrapeRegistry.Names.Select(name => new { name, aliases = GrapeRegistry.TryGetAliases(name, out var values) ? values : [] }).ToArray() }, SemanticSearchContract.JsonOptions);
            return new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))), options)
            {
                RegionPairs = pairs.Where(x => !string.IsNullOrWhiteSpace(x.Country) && !string.IsNullOrWhiteSpace(x.Region))
                    .Select(x => new SemanticRegionOption(x.Region, x.Country))
                    .OrderBy(r => r.Country, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal).ToArray()
            };
        }
        public Task<SemanticCatalogSearchResult> SearchAsync(SemanticSearchPlan plan, CancellationToken ct) => new CatalogQuery(db, enrichment, extractorVersion).SearchSemanticAsync(plan, ct);
        public ValueTask DisposeAsync() => tx.DisposeAsync();
    }
}

public interface ISemanticInterpreter
{
    bool Enabled { get; }
    Task<SemanticSearchInterpretation> InterpretAsync(SemanticSearchRequest request, CancellationToken ct);
}
public sealed class LoopbackSemanticInterpreter(HttpClient client, IConfiguration configuration) : ISemanticInterpreter
{
    // Separate opt-in, never silently change the deployed v1 bridge's contract.
    public bool Enabled => bool.TryParse(configuration["FreeTextSearch:SemanticV2Enabled"], out var enabled) && enabled &&
        bool.TryParse(configuration["FreeTextSearch:Enabled"], out var model) && model;
    public async Task<SemanticSearchInterpretation> InterpretAsync(SemanticSearchRequest request, CancellationToken ct)
    {
        if (!Enabled) throw new BridgeFailure("model_disabled");
        var endpoint = LoopbackFreeTextInterpreter.ValidateEndpoint(configuration["FreeTextSearch:SemanticEndpoint"]);
        var seconds = 10;
        if (configuration["FreeTextSearch:TimeoutSeconds"] is { } text && !int.TryParse(text, out seconds) || seconds is < 1 or > 30)
            throw new BridgeFailure("bridge_configuration_invalid");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(JsonSerializer.Serialize(request, SemanticSearchContract.JsonOptions), Encoding.UTF8, "application/json") };
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new BridgeFailure("bridge_http_failure");
            if (response.Content.Headers.ContentType?.MediaType != "application/json") throw new BridgeFailure("bridge_invalid_response");
            if (response.Content.Headers.ContentLength > SemanticSearchContract.MaxBodyBytes) throw new BridgeFailure("bridge_response_too_large");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var body = new MemoryStream(); var buffer = new byte[4096];
            while (true) { var n = await stream.ReadAsync(buffer, timeout.Token); if (n == 0) break;
                if (body.Length + n > SemanticSearchContract.MaxBodyBytes) throw new BridgeFailure("bridge_response_too_large"); body.Write(buffer, 0, n); }
            return SemanticSearchContract.ParseResponse(request, new UTF8Encoding(false, true).GetString(body.ToArray()));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new BridgeFailure("bridge_timeout"); }
        catch (Exception e) when (e is HttpRequestException or IOException) { throw new BridgeFailure("bridge_unavailable"); }
        catch (Exception e) when (e is ArgumentException or JsonException or DecoderFallbackException) { throw new BridgeFailure("bridge_invalid_response"); }
    }
}

/// <summary>Detect omissions of explicit supported intents. It does not infer geography or source facts.</summary>
public static class SemanticIntentCoverage
{
    public static SemanticSearchPlan? TryRulesFallback(string query, ExplicitSearchUi ui, SemanticCatalogSnapshot snapshot)
    {
        try
        {
            var rules = NaturalSearch.Parse(new CatalogFilter(Text: query));
            // A residual may be a grape, an OR region, or another unsupported meaning.
            // Never turn it into a keyword or invoke the interpreter again.
            if (!string.IsNullOrWhiteSpace(rules.Filter.Text) || rules.Conditions.Length == 0 || rules.Champagne ||
                rules.MinExclusive || rules.MaxExclusive || rules.Filter.Region is not null || rules.SupplierId is not null) return null;
            static int? Price(decimal? value) => value is null ? null : value >= 0 && value <= 1000000000 && decimal.Truncate(value.Value) == value
                ? (int)value.Value : throw new ArgumentException("rules_price_unrepresentable");
            var f = rules.Filter;
            var request = SemanticSearchContract.CreateRequest(Guid.NewGuid(), snapshot.Version, query, snapshot.Options);
            var response = new SemanticSearchInterpretation(request.Schema, request.RequestId, request.CatalogVersion, request.PromptVersion,
                "rules", "existing-natural-search", new([], null, f.Country, [], f.ProductType, f.VolumeMl, f.VintageRaw,
                    Price(f.PriceMin), Price(f.PriceMax), f.AvailableOnly, null), []);
            var plan = SemanticSearchContract.Merge(request, ui.Filter, ui.Supplier, response) with { Sparkling = rules.Sparkling && ui.ProductType is null };
            return Check(query, ui, plan).Count == 0 ? plan : null;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or RegexMatchTimeoutException) { return null; }
    }
    public static IReadOnlyList<string> Check(string query, ExplicitSearchUi ui, SemanticSearchPlan plan, SemanticSearchOptions? options = null)
    {
        var errors = new List<string>(); var text = query.Normalize(NormalizationForm.FormKC);
        bool Has(string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var f = plan.Filter;
        if (ui.ProductType is null)
        {
            if (Has("赤(?:ワイン)?") && f.ProductType is not ("赤" or "赤ワイン")) errors.Add("explicit_red_not_adopted");
            if (Has("白(?:ワイン)?") && f.ProductType is not ("白" or "白ワイン")) errors.Add("explicit_white_not_adopted");
        }
        if (ui.AvailableOnly is null && Has("在庫(?:が)?(?:有る|ある|有り|あり)") && !f.AvailableOnly) errors.Add("explicit_stock_not_adopted");
        if (ui.Country is null && Has("イタリア|\\bItaly\\b") && f.Country is not ("イタリア" or "ｲﾀﾘｱ" or "Italy")) errors.Add("explicit_country_not_adopted");
        if (ui.Country is null && Has(@"南アフリカ|\bSouth Africa\b") && f.Country is not ("南アフリカ" or "South Africa")) errors.Add("explicit_country_not_adopted");
        if (Has("サンジョ[ヴベ]ェ?ーゼ|\\bSangiovese\\b") && plan.Grape?.Name != "サンジョベーゼ") errors.Add("explicit_grape_not_adopted");
        if (Has(@"シュナン[・ ]?ブラン|\bChenin Blanc\b") && plan.Grape?.Name != "シュナン・ブラン") errors.Add("explicit_grape_not_adopted");
        if (Has("主体|過半|50\\s*%\\s*(?:超|より)") && plan.Grape?.Mode != "predominant") errors.Add("explicit_predominance_not_adopted");
        if (Has("左岸") || (Has("右岸") && options is null)) errors.Add("geography_scope_unverified");
        if (options is not null) errors.AddRange(ReviewedRightBankScope.Check(query, ui, plan, options));
        var structuredTerms = plan.Regions.Concat(new[] { f.Country, f.ProductType, plan.Grape?.Name }.Where(x => x is not null).Select(x => x!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (plan.Grape is { } grape && GrapeRegistry.TryGetAliases(grape.Name, out var aliases)) structuredTerms.UnionWith(aliases);
        if (plan.Keywords.Any(structuredTerms.Contains)) errors.Add("structured_keyword_duplicate");
        try
        {
            var rules = NaturalSearch.Parse(new CatalogFilter(Text: text));
            if (ui.PriceMin is null && rules.Filter.PriceMin is { } min && (f.PriceMin != min || rules.MinExclusive)) errors.Add("explicit_price_min_not_adopted");
            if (ui.PriceMax is null && rules.Filter.PriceMax is { } max && (f.PriceMax != max || rules.MaxExclusive)) errors.Add("explicit_price_max_not_adopted");
            if (ui.VolumeMl is null && rules.Filter.VolumeMl is { } volume && f.VolumeMl != volume) errors.Add("explicit_volume_not_adopted");
            if (ui.Vintage is null && rules.Filter.VintageRaw is { } vintage && f.VintageRaw != vintage) errors.Add("explicit_vintage_not_adopted");
            if (Has("円|価格|上代|予算") && rules.Filter.PriceMin is null && rules.Filter.PriceMax is null && ui.PriceMin is null && ui.PriceMax is null)
                errors.Add("price_scope_unverified");
        }
        catch (ArgumentException) { errors.Add("explicit_conditions_unverified"); }
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }
}

public sealed class SemanticSearchService(ISemanticSearchStore store, ISemanticInterpreter interpreter)
{
    // Only reviewed exact source spellings, drawn from this snapshot. Never fuzzy-country expansion.
    public static IReadOnlyList<string> ItalyAliases(string? adoptedCountry, SemanticSearchOptions options)
    {
        if (adoptedCountry is not ("イタリア" or "ｲﾀﾘｱ")) return [];
        if (!options.Countries.Contains(adoptedCountry, StringComparer.Ordinal)) throw new ArgumentException("country_not_in_snapshot");
        return options.Countries.Where(c => c is "イタリア" or "ｲﾀﾘｱ")
            .Where(c => c.Normalize(NormalizationForm.FormKC) == "イタリア")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
    public async Task<SemanticUiResult> SearchAsync(FreeTextUiRequest input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Query) || input.Query.Length > 200) throw new ArgumentException("invalid_semantic_query");
        var ui = input.ExplicitUi ?? new(); ui.Validate();
        var initial = await store.SnapshotAsync(ct);
        var interpretedVersion = initial.Version;
        var interpretationOptions = initial.Options;
        ReviewedRightBankScope.Binding? geographyBinding = null;
        SemanticUiResult Clarify(string reason, SemanticSearchPlan? p = null, IReadOnlyList<string>? reasons = null) =>
            new([], false, initial.Version, "needsClarification", reason,
                new { filter = p?.Filter ?? ui.Filter, supplier = p?.SupplierCode ?? ui.Supplier, regions = p?.Regions ?? [], grape = p?.Grape, keywords = p?.Keywords ?? [] },
                ui, p?.UnparsedTerms ?? [], reasons ?? [reason], 0, 0, false);
        async Task<SemanticUiResult> Execute(SemanticSearchPlan accepted, string? fallbackReason = null, JapaneseWineDictionary.Match? dictionary = null)
        {
            accepted.RequireExecutable();
            await using var read = await store.BeginReadAsync(ct);
            var current = await read.SnapshotAsync(ct);
            if (current.Version != initial.Version) return Clarify("catalog_changed");
            if (JsonSerializer.Serialize(current.Options, SemanticSearchContract.JsonOptions) != JsonSerializer.Serialize(initial.Options, SemanticSearchContract.JsonOptions)) return Clarify("catalog_changed");
            var executionPlan = geographyBinding is null ? accepted with { CountryAliases = dictionary?.Countries ?? (accepted.CountryAliases.Count > 0 ? accepted.CountryAliases : ItalyAliases(accepted.Filter.Country, current.Options)) } : accepted with {
                CountryAliases = geographyBinding.RawCountries ?? [],
                Regions = accepted.Regions.SelectMany(r => geographyBinding.RawAliases![r]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
            var result = await read.SearchAsync(executionPlan, ct);
            return new(result.Items, result.Items.Count == ui.Limit, fallbackReason is null ? interpretedVersion : current.Version, fallbackReason is null ? "model" : "rules", fallbackReason,
                new { filter = accepted.Filter, supplier = accepted.SupplierCode, regions = dictionary?.PlaceLabel is { } place ? new[] { place } : accepted.Regions, grape = accepted.Grape, taste = accepted.Taste, sparkling = accepted.Sparkling, jan = accepted.Jan, keywords = accepted.Keywords,
                    geographyUnmappedRegionCount = geographyBinding?.UnmappedRegionCount,
                    geographyCoverage = dictionary?.PlaceLabel is not null ? "辞書で照合できた登録産地のみを対象としています。" :
                        ReviewedRightBankScope.Requested(input.Query) ? (ui.Region is not null ? "画面で明示した産地を優先しています。" : ReviewedRightBankScope.CoverageNotice) : null },
                ui, [], [], result.EvidenceUnavailableCount, result.EvidenceConflictCount, result.Complete);
        }
        async Task<SemanticUiResult> Fallback(string reason)
        {
            var rules = SemanticIntentCoverage.TryRulesFallback(input.Query, ui, initial);
            return rules is null ? Clarify(reason) : await Execute(rules, reason);
        }
        if (!ReviewedRightBankScope.Requested(input.Query) && !input.Query.Contains("左岸", StringComparison.Ordinal) &&
            JapaneseWineDictionary.Interpret(input.Query, initial.Options) is { } dictionary)
        {
            if (ui.Region is not null)
                dictionary = dictionary with { PlaceLabel = null, Countries = [], Regions = [ui.Region] };
            if (ui.Country is not null)
                dictionary = dictionary with {
                    Countries = [],
                    PlaceLabel = dictionary.Countries.Contains(ui.Country, StringComparer.Ordinal) ? dictionary.PlaceLabel : null,
                    Regions = ui.Region is not null || dictionary.Countries.Contains(ui.Country, StringComparer.Ordinal) ? dictionary.Regions : []
                };
            if (dictionary.PlaceLabel is not null && dictionary.Regions.Length is 0 or > 128) return Clarify("dictionary_region_not_bound");
            var filter = ui.Filter with { Country = ui.Country ?? dictionary.Countries.FirstOrDefault(), ProductType = ui.ProductType ?? dictionary.ProductType };
            var dictionaryPlan = new SemanticSearchPlan(filter, ui.Supplier, [], dictionary.Regions, dictionary.Grape, [], false) {
                Taste = dictionary.Taste, Sparkling = dictionary.Sparkling && ui.ProductType is null
            };
            return await Execute(dictionaryPlan, "dictionary_match", dictionary);
        }
        var geographyOptions = initial.Options with { Regions = initial.RegionPairs };
        if (ReviewedRightBankScope.Requested(input.Query) && string.IsNullOrWhiteSpace(ui.Region))
        {
            var resolved = ReviewedRightBankScope.Bind(input.Query, geographyOptions, ui.Country);
            if (resolved.Errors.Count != 0) return Clarify("semantic_conditions_unresolved", reasons: resolved.Errors);
            geographyBinding = resolved;
        }
        if (ComposedSearchRules.Interpret(input.Query, ui, initial, geographyBinding) is { } composed)
            return await Execute(composed, "dictionary_match");

        if (ComposedSearchRules.RequiresClarification(input.Query)) return Clarify("unsupported_condition_logic");

        if (JapaneseWineDictionary.MentionsTaste(input.Query)) return Clarify("dictionary_taste_not_bound");
        var mentionedGrapes = GrapeRegistry.MentionedNames(input.Query.Normalize(NormalizationForm.FormKC));
        if (mentionedGrapes.Length == 0 && SemanticIntentCoverage.TryRulesFallback(input.Query, ui, initial) is { } exactRules)
            return await Execute(exactRules, "dictionary_match");
        if (mentionedGrapes.Length > 1) return Clarify("dictionary_multiple_grapes");
        interpretationOptions = interpretationOptions with { Grapes = mentionedGrapes };
        SemanticSearchRequest request; SemanticSearchInterpretation response; SemanticSearchPlan plan;
        if (geographyBinding is { } binding)
        {
            interpretationOptions = initial.Options with {
                Countries = binding.Regions.Select(r => r.Country).Distinct(StringComparer.Ordinal).ToArray(),
                Regions = binding.Regions, Grapes = mentionedGrapes };
            interpretedVersion = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
                schema = "semantic-scoped-request/v1", sourceVersion = initial.Version,
                scope = ReviewedRightBankScope.Definition, binding, options = interpretationOptions }, SemanticSearchContract.JsonOptions)));
        }
        try
        {
            request = SemanticSearchContract.CreateRequest(Guid.NewGuid(), interpretedVersion, input.Query, interpretationOptions);
            response = await interpreter.InterpretAsync(request, ct);
            plan = SemanticSearchContract.Merge(request, ui.Filter, ui.Supplier, response);
        }
        catch (BridgeFailure ex) { return await Fallback(ex.Reason); }
        catch (Exception ex) when (ex is ArgumentException or JsonException) { return await Fallback("semantic_interpretation_invalid"); }
        var coverage = SemanticIntentCoverage.Check(input.Query, ui, plan, geographyOptions);
        if (mentionedGrapes.Length == 1 && plan.Grape?.Name != mentionedGrapes[0])
            return Clarify("semantic_conditions_unresolved", plan, coverage.Append("explicit_grape_not_adopted").Distinct().ToArray());
        if (plan.NeedsClarification || coverage.Count != 0) return Clarify("semantic_conditions_unresolved", plan, coverage.Concat(plan.NeedsClarification ? new[] { "unparsed_terms" } : []).ToArray());
        return await Execute(plan);
    }
}
