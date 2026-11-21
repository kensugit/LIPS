using System.Text.Json.Serialization;

namespace CatalogSearch.Application;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImporterExtractionProvenance(string Method, string AdapterVersion,
    string ExtractorVersion, string? Model, string? PromptVersion, string? InputSha256);

// Neither imported prices/inventory nor complete page bodies are storage fields.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImporterEnrichmentContent(string Schema, ImporterProductIdentity Identity,
    string OriginalSourceId, string OriginalFingerprint, string RequestedUrl, string FinalUrl,
    DateTimeOffset FetchedAtUtc, string BodySha256, string? ETag, string? LastModified,
    ImporterExtractionProvenance Extraction, IReadOnlyList<ImporterGrapeEvidence> Grapes);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImporterEnrichmentVerification(string Schema, string ContentRecordSha256,
    string Status, DateTimeOffset VerifiedAtUtc, DateTimeOffset ExpiresAtUtc, string ConfirmationKind);

public sealed record ImporterEnrichmentLookup(ImporterProductIdentity Identity, string OriginalFingerprint,
    string? ExpectedFinalUrl, string ExtractorVersion, DateTimeOffset NowUtc);

public sealed record ImporterEnrichmentReadResult(string Status, long? Generation,
    string? ContentRecordSha256, ImporterEnrichmentContent? Content);

// Web receives only this interface; it never obtains a writer or an acquisition callback.
public interface IImporterEnrichmentReader
{
    ImporterEnrichmentReadResult ReadVerified(ImporterEnrichmentLookup lookup);
    ImporterEnrichmentReadResult ReadVerified(ImporterProductIdentity identity, string originalFingerprint,
        string extractorVersion, DateTimeOffset nowUtc);
    ImporterEnrichmentReadResult ReadVerifiedForProduct(ImporterProductIdentity identity, string originalFingerprint,
        string extractorVersion, DateTimeOffset nowUtc);
}

public sealed record ImporterEnrichmentCommitResult(long Generation, string ContentRecordSha256,
    string VerificationRecordSha256, bool ContentAlreadyPresent);

public sealed record ImporterEnrichmentStorageLimits(int MaximumFiles, long MaximumTotalBytes);
