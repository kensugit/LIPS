using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CatalogSearch.Application;

public sealed record GrapeEvidenceDecision(bool Included, string Reason, decimal? ExplicitPercent = null);

/// <summary>Pure candidate: evaluates only the source row's Grapes field. No region/name inference.</summary>
public static class GrapeEvidence
{
    private const int MaxJsonCharacters = 262144;
    private const int MaxGrapeCharacters = 4096;
    private static readonly string[] DefaultAliases = ["Sangiovese", "サンジョベーゼ"];
    private const string Amount = @"(?<percent>(?:0|[1-9][0-9]{0,2})(?:\.[0-9]{1,6})?)\s*%";
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex Uncertain = new(
        @"(?:[~〜～]|[0-9]\s*[-–−]\s*[0-9]|\b(?:not|without|except|non)\b|ない|無し|なし|不使用|以外|除く|未満|以下|以上|程度|約)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(50));
    private static readonly Regex PercentTokens = new(@"(?<token>[+\-]?[0-9][0-9.,eE+\-]*)\s*%", Options, RegexTimeout);
    private static readonly Regex ExactPercentNumber = new(@"^(?:0|[1-9][0-9]{0,2})(?:\.[0-9]{1,6})?$", Options, RegexTimeout);

    public static GrapeEvidenceDecision FromExtractedRowJson(string? json) => FromExtractedRowJson(json, DefaultAliases);

    /// <param name="trustedAliases">Only a reviewed registry entry, never model-generated aliases.</param>
    public static GrapeEvidenceDecision FromExtractedRowJson(string? json, IReadOnlyList<string> trustedAliases)
    {
        if (!TryAliases(trustedAliases, out _)) return Reject("TRUSTED_ALIASES_INVALID");
        if (string.IsNullOrEmpty(json) || json.Length > MaxJsonCharacters) return Reject("SOURCE_JSON_MISSING_OR_BOUND");
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Reject("SOURCE_JSON_NOT_OBJECT");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) return Reject("SOURCE_JSON_DUPLICATE_PROPERTY");
            if (!doc.RootElement.TryGetProperty("Grapes", out var value) || value.ValueKind != JsonValueKind.String)
                return Reject("GRAPES_MISSING_OR_NOT_STRING");
            return FromGrapes(value.GetString(), trustedAliases);
        }
        catch (JsonException) { return Reject("SOURCE_JSON_INVALID"); }
    }

    public static GrapeEvidenceDecision FromGrapes(string? source) => FromGrapes(source, DefaultAliases);

    /// <param name="trustedAliases">Only a reviewed registry entry, never model-generated aliases.</param>
    public static GrapeEvidenceDecision FromGrapes(string? source, IReadOnlyList<string> trustedAliases)
    {
        if (!TryAliases(trustedAliases, out var aliases)) return Reject("TRUSTED_ALIASES_INVALID");
        if (string.IsNullOrWhiteSpace(source) || source.Length > MaxGrapeCharacters) return Reject("GRAPES_MISSING_OR_BOUND");
        string normalized;
        try { normalized = Normalize(source); }
        catch (ArgumentException) { return Reject("GRAPES_INVALID_UNICODE"); }
        // Separate original explicit composition clauses, never extract a favorable substring.
        var clauses = normalized.Split([',', '、', ';', '/', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var mentions = clauses.Where(x => aliases.Any(alias => x.Contains(alias, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (mentions.Length == 0) return Reject("TARGET_GRAPE_NOT_STATED");
        if (mentions.Length != 1) return Reject("TARGET_GRAPE_MULTIPLE_CLAUSES");
        var clause = mentions[0];
        try
        {
            if (Uncertain.IsMatch(normalized)) return Reject("NEGATED_RANGE_OR_QUALIFIED_SOURCE");
            decimal total = 0;
            var tokens = PercentTokens.Matches(normalized);
            if (tokens.Count != normalized.Count(c => c == '%')) return Reject("INVALID_PERCENT_TOKEN");
            foreach (Match item in tokens)
            {
                var token = item.Groups["token"].Value;
                var previous = item.Index - 1;
                while (previous >= 0 && char.IsWhiteSpace(normalized[previous])) previous--;
                if (previous >= 0 && (char.IsDigit(normalized[previous]) || "+-−–.,/".Contains(normalized[previous])))
                    return Reject("INVALID_PERCENT_TOKEN");
                if (!ExactPercentNumber.IsMatch(token) || !decimal.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var share) || share > 100)
                    return Reject("INVALID_PERCENT_TOKEN");
                total += share;
            }
            if (total > 100) return Reject("COMPOSITION_TOTAL_OVER_100");
            if (clauses.Any(x => x != clause && x.Contains("主体", StringComparison.Ordinal)))
                return Reject("CONFLICTING_MAIN_GRAPE_STATEMENTS");
            var name = "(?:" + string.Join("|", aliases.Select(Regex.Escape)) + ")";
            var percentage = new Regex(@"^(?:" + name + @"\s*(?:" + Amount + @"|\(\s*" + Amount + @"\s*\))|" + Amount + @"\s*" + name + @")$", Options, RegexTimeout).Match(clause);
            if (percentage.Success)
            {
                if (!decimal.TryParse(percentage.Groups["percent"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount > 100)
                    return Reject("PERCENT_OUT_OF_RANGE");
                return amount > 50
                    ? new(true, "EXPLICIT_PERCENT_OVER_50", amount)
                    : new(false, "EXPLICIT_PERCENT_NOT_OVER_50", amount);
            }
            var main = new Regex(@"^(?:" + name + @"\s*(?:が\s*)?主体|主体\s*[:：]?\s*" + name + @")$", Options, RegexTimeout);
            if (main.IsMatch(clause)) return total >= 50
                ? Reject("MAIN_STATEMENT_CONTRADICTS_OTHER_PERCENTAGES")
                : new(true, "EXPLICIT_MAIN_GRAPE");
            return Reject("UNPROVEN_OR_UNSUPPORTED_TARGET_CLAUSE");
        }
        catch (RegexMatchTimeoutException) { return Reject("GRAPE_PARSE_TIMEOUT"); }
        catch (ArgumentException) { return Reject("TRUSTED_ALIASES_INVALID"); }
        catch (NotSupportedException) { return Reject("TRUSTED_ALIASES_INVALID"); }
    }

    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC).Trim().Replace("サンジョヴェーゼ", "サンジョベーゼ", StringComparison.Ordinal);

    private static bool TryAliases(IReadOnlyList<string>? values, out string[] aliases)
    {
        aliases = [];
        if (values is null || values.Count is < 1 or > 20) return false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || value.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)) return false;
            string normalized;
            try { normalized = Normalize(value); } catch (ArgumentException) { return false; }
            if (normalized.Length is < 1 or > 100 || normalized.IndexOfAny([',', '、', ';', '/', '%']) >= 0 || normalized.Contains("主体", StringComparison.Ordinal) || !seen.Add(normalized)) return false;
        }
        aliases = seen.ToArray();
        return true;
    }

    private static GrapeEvidenceDecision Reject(string reason) => new(false, reason);
}
