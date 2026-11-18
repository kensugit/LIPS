using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatalogSearch.Application;

// Offline exchange contract. A proposal is never an IntakeAnswer or an import instruction.
public sealed record DateProposalExcerpt(string Id, string Location, string Text);
public sealed record DateProposalRequest(string Schema, Guid RequestId, string SourceHash, int ReviewVersion,
    string PromptVersion, IReadOnlyList<DateProposalExcerpt> Excerpts);
public sealed record DateProposalCandidate(string Date, string Reason, IReadOnlyList<string> EvidenceIds);
public sealed record DateProposalResponse(string Schema, Guid RequestId, string SourceHash, int ReviewVersion,
    string PromptVersion, string Provider, string Model, string Outcome, IReadOnlyList<DateProposalCandidate> Candidates);

public static class IntakeDateProposal
{
    public const string Schema = "lips-date-proposal/v1";
    public const string Prompt = "date-evidence/1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static void ValidateRequest(DateProposalRequest r)
    {
        if (r.Schema != Schema || r.RequestId == Guid.Empty || r.ReviewVersion < 1 || r.PromptVersion != Prompt ||
            r.SourceHash is null || r.SourceHash.Length != 64 || !r.SourceHash.All(Uri.IsHexDigit) ||
            r.Excerpts is null || r.Excerpts.Count is < 1 or > 16 ||
            r.Excerpts.Any(e => e is null || string.IsNullOrWhiteSpace(e.Id) || e.Id.Length > 64 ||
                string.IsNullOrWhiteSpace(e.Location) || e.Location.Length > 200 ||
                string.IsNullOrWhiteSpace(e.Text) || e.Text.Length > 1000) ||
            r.Excerpts.Select(e => e.Id).Distinct().Count() != r.Excerpts.Count || r.Excerpts.Sum(e => e.Text.Length) > 8000)
            throw new ArgumentException("Invalid bounded LIPS date-proposal request.");
    }

    public static DateProposalResponse ReadResponse(DateProposalRequest request, string payload)
    {
        ValidateRequest(request);
        if (payload.Length > 16000) throw new ArgumentException("Proposal response exceeds limit.");
        var response = JsonSerializer.Deserialize<DateProposalResponse>(payload, Json)
            ?? throw new ArgumentException("Missing proposal response.");
        if (response.Schema != Schema || response.RequestId != request.RequestId || response.SourceHash != request.SourceHash ||
            response.ReviewVersion != request.ReviewVersion || response.PromptVersion != request.PromptVersion ||
            string.IsNullOrWhiteSpace(response.Provider) || response.Provider.Length > 100 ||
            string.IsNullOrWhiteSpace(response.Model) || response.Model.Length > 200 ||
            response.Outcome is not ("Candidates" or "Unknown") || response.Candidates is null || response.Candidates.Count > 3 ||
            (response.Outcome == "Unknown") != (response.Candidates.Count == 0))
            throw new ArgumentException("Proposal identity, version or result contract mismatch.");
        var ids = request.Excerpts.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var c in response.Candidates)
            if (c is null || !DateOnly.TryParseExact(c.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                string.IsNullOrWhiteSpace(c.Reason) || c.Reason.Length > 1000 || c.EvidenceIds is null ||
                c.EvidenceIds.Count is < 1 or > 16 || c.EvidenceIds.Any(id => !ids.Contains(id)) ||
                c.EvidenceIds.Distinct().Count() != c.EvidenceIds.Count)
                throw new ArgumentException("Proposal date or source evidence is invalid.");
        if (response.Candidates.Select(c => c.Date).Distinct().Count() != response.Candidates.Count)
            throw new ArgumentException("Duplicate proposal dates.");
        return response;
    }
}
