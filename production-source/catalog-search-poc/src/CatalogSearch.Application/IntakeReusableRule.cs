namespace CatalogSearch.Application;

// Pure scope matching: performs no I/O. IntakeReview publication and supported import adapters call this policy; persistence lives in Infrastructure.
// Records contain only immutable scalar values; an amendment is a new version.
public sealed record IntakeRuleScope(string SupplierId, string DocumentKind, string ColumnsFingerprint,
    string? ProductIdentity = null, string? Vintage = null);
public sealed record IntakeReusableRule(Guid Id, int Version, IntakeRuleScope Scope, string Kind,
    string SourceKey, string Meaning, Guid AnswerId, int AnswerVersion, string SourceHash,
    string Evidence, bool Revoked = false)
{
    // Separate from original human answer evidence. Omit null for old receipt hash compatibility.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CancellationReason { get; init; }
}
public sealed record IntakeRuleQuery(IntakeRuleScope Scope, string Kind, string SourceKey);
public sealed record IntakeRulePreview(string Status, string Reason, IntakeReusableRule? Rule = null);

public static class IntakeReusableRules
{
    private static bool Text(string? s, int max = 200) => !string.IsNullOrWhiteSpace(s) && s.Length <= max && !s.Any(char.IsControl);
    private static bool Hash(string? s) => s is { Length: 64 } && s.All(Uri.IsHexDigit);
    private static bool Kind(string s) => s is "VolumeUnit" or "ColumnMeaning" or "RegionAbbreviation" or "ProductAlias";
    private static bool Scope(IntakeRuleScope? s) => s is not null && Text(s.SupplierId) && Text(s.DocumentKind) && Hash(s.ColumnsFingerprint)
        && (s.ProductIdentity is null || Text(s.ProductIdentity)) && (s.Vintage is null || Text(s.Vintage, 20));

    public static void Validate(IntakeReusableRule r)
    {
        if (r is null || r.Id == Guid.Empty || r.Version < 1 || !Scope(r.Scope) || !Kind(r.Kind) ||
            !Text(r.SourceKey) || !Text(r.Meaning) || r.AnswerId == Guid.Empty || r.AnswerVersion < 1 ||
            !Hash(r.SourceHash) || !Text(r.Evidence, 2000)) throw new ArgumentException("Invalid reusable rule or missing human answer evidence.");
        if (r.CancellationReason is not null && (!r.Revoked || !Text(r.CancellationReason, 2000)))
            throw new ArgumentException("Invalid cancellation reason.");
        if (r.Kind == "VolumeUnit" && r.Meaning is not ("mL" or "cL" or "L"))
            throw new ArgumentException("Only a volume unit, never a volume value, can be reused.");
        if (r.Kind == "ColumnMeaning" && r.Meaning is not ("ReferenceRetail" or "PurchasePrice" or "PerBottle" or "PerCase" or "OrderQuantity" or "StockQuantity" or "Capacity" or "Region"))
            throw new ArgumentException("Unsupported column meaning; actual date, stock and price values cannot be reused.");
        if (r.Kind == "ProductAlias" && (!Text(r.Scope.ProductIdentity) || !Text(r.Scope.Vintage, 20)))
            throw new ArgumentException("A product alias requires exact product identity and vintage (including explicit NV).");
    }

    // Caller must persist with its own CAS. This method performs no writes.
    public static IntakeReusableRule Amend(IntakeReusableRule current, int expectedVersion, string meaning,
        Guid answerId, int answerVersion, string sourceHash, string evidence, bool revoke = false)
    {
        Validate(current);
        if (current.Version != expectedVersion) throw new InvalidOperationException("Stale rule version.");
        var next = current with { Version = checked(current.Version + 1), Meaning = meaning, AnswerId = answerId,
            AnswerVersion = answerVersion, SourceHash = sourceHash, Evidence = evidence, Revoked = revoke };
        Validate(next); return next;
    }

    public static IntakeRulePreview Preview(IntakeRuleQuery query, IEnumerable<IntakeReusableRule> history)
    {
        if (query is null || !Scope(query.Scope) || !Kind(query.Kind) || !Text(query.SourceKey))
            return new("Held", "Unsupported query or missing exact scope.");
        var all = history?.Take(1001).ToArray();
        if (all is null || all.Length > 1000) return new("Held", "Invalid or oversized rule history.");
        try { foreach (var r in all) Validate(r); }
        catch (ArgumentException) { return new("Held", "Rule evidence or definition is invalid."); }
        var latest = new List<IntakeReusableRule>();
        foreach (var versions in all.GroupBy(r => r.Id))
        {
            if (versions.GroupBy(r => r.Version).Any(g => g.Distinct().Count() != 1) ||
                versions.Select(r => (r.Scope, r.Kind, r.SourceKey)).Distinct().Count() != 1)
                return new("Held", "Conflicting versions or changed rule scope.");
            latest.Add(versions.MaxBy(r => r.Version)!);
        }
        var matches = latest.Where(r => r.Scope == query.Scope && r.Kind == query.Kind && r.SourceKey == query.SourceKey).ToArray();
        if (matches.Length == 0) return new("Held", "No exact supplier, format, column and product scope match; confirmation required.");
        if (matches.Length != 1) return new("Held", "Multiple matching rules require confirmation.");
        if (matches[0].Revoked) return new("Held", "Rule revoked; no fallback to an older version.", matches[0]);
        return new("Candidate", "Preview only; explicit answer and separate import operation remain required.", matches[0]);
    }
}
