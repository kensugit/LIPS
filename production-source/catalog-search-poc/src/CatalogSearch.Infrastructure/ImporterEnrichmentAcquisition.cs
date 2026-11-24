using CatalogSearch.Application;

namespace CatalogSearch.Infrastructure;

public sealed record ImporterAcquisitionResult(string Status, ImporterEnrichmentCommitResult? Saved);

// Invoked only by the acquisition actor with a reviewed host policy and original row snapshot.
// Never registered on the public Web route. Failed fetch/validation does not trigger a retry.
public sealed class ImporterEnrichmentAcquisition(IOfficialImporterPageFetcher fetcher,
    FileImporterEnrichmentWriter writer, ImporterEvidenceHostPolicy hosts, ImporterPageAdapterPolicy? adapters = null)
{
    public async Task<ImporterAcquisitionResult> AcquireAsync(ImporterOriginalProduct original,
        Guid sourceDocumentId, string originalExtractedJson, string officialUrl, long expectedGeneration,
        CancellationToken ct = default)
    {
        if (sourceDocumentId == Guid.Empty || SourceFingerprint.Create(sourceDocumentId, originalExtractedJson) != original.Fingerprint ||
            !SourceIdentityBinding.Verify(original.Identity, originalExtractedJson))
            throw new ArgumentException("ACQUISITION_ORIGINAL_BINDING");
        var originalGrapes = SemanticGrapeMatcher.ReadOriginal(originalExtractedJson);
        original = original with { KnownGrapes = string.IsNullOrWhiteSpace(originalGrapes) ? [] : [originalGrapes] };
        var page = await fetcher.FetchAsync(original.Identity.SupplierCode, officialUrl, hosts, ct);
        var policy = adapters ?? new ImporterPageAdapterPolicy();
        var adapter = policy.Select(original.Identity.SupplierCode);
        if (!adapter.TryExtract(original.Identity, page.FinalUrl, page.Body, out var grapes))
            return new("PAGE_REQUIRES_REVIEW_NOT_SAVED", null);
        var evidence = new ImporterProductEvidence(ImporterEvidenceValidator.Schema, page.RequestedUrl, page.FinalUrl,
            page.FetchedAtUtc, page.ContentSha256, original.Fingerprint, original.Identity, "EXACT", false, grapes);
        var now = DateTimeOffset.UtcNow;
        // Bind identity/excerpts first; preserve substantive conflicts as non-adopted records.
        var proof = ImporterEvidenceValidator.Validate(evidence, original, hosts, page.Body, now,
            (_, _) => false, (identity, claims, bytes) => adapter.Verify(identity, claims, page.FinalUrl, bytes));
        var conflict = HasConflict(original.KnownGrapes, grapes);
        var content = new ImporterEnrichmentContent("lips-importer-enrichment-content/v1", original.Identity,
            sourceDocumentId.ToString("D"), original.Fingerprint, page.RequestedUrl, page.FinalUrl,
            page.FetchedAtUtc, page.ContentSha256, null, null,
            new("deterministic", adapter.Version, policy.BoundVersion(hosts), null, null, null), grapes);
        return new(conflict ? "CONFLICT_SAVED_NOT_ADOPTED" : "VERIFIED_SAVED",
            writer.Commit(content, conflict ? "conflict" : "verified", now, now.AddDays(30), "200", expectedGeneration, now, proof));
    }

    public static bool HasConflict(IReadOnlyList<string> original, IReadOnlyList<ImporterGrapeEvidence> supplied)
    {
        if(HasExclusiveCompositionConflict(original,supplied))return true;
        foreach (var name in GrapeRegistry.Names)
        {
            GrapeRegistry.TryGetAliases(name, out var aliases);
            var before = GrapeEvidence.FromGrapes(string.Join(",", original), aliases);
            var afterRows = supplied.Where(x => aliases.Contains(x.Grape.Replace("サンジョヴェーゼ", "サンジョベーゼ", StringComparison.Ordinal), StringComparer.OrdinalIgnoreCase)).ToArray();
            if (afterRows.Length > 1) return true;
            if (afterRows.Length == 0) continue;
            var after = GrapeEvidence.FromGrapes(afterRows[0].SourceExcerpt, aliases);
            if (before.ExplicitPercent is { } a && after.ExplicitPercent is { } b && a != b) return true;
            if (before.Included && after.ExplicitPercent <= 50 || after.Included && before.ExplicitPercent <= 50) return true;
        }
        return false;
    }
    public static bool HasExclusiveCompositionConflict(IReadOnlyList<string> original,IReadOnlyList<ImporterGrapeEvidence> supplied)
    {
        // A plain list is not complete composition. Only explicit100% excludes another explicit100%.
        var before=new HashSet<string>(StringComparer.Ordinal);
        foreach(var name in GrapeRegistry.Names)
        {
            GrapeRegistry.TryGetAliases(name,out var aliases);
            if(GrapeEvidence.FromGrapes(string.Join(",",original),aliases).ExplicitPercent==100)before.Add(name);
        }
        foreach(var claim in supplied)
        {
            var canonical=GrapeRegistry.Resolve(claim.Grape);
            if(canonical is null||!GrapeRegistry.TryGetAliases(canonical,out var aliases))continue;
            if(GrapeEvidence.FromGrapes(claim.SourceExcerpt,aliases).ExplicitPercent==100&&before.Any(name=>name!=canonical))return true;
        }
        return false;
    }
}
