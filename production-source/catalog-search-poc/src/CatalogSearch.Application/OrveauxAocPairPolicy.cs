using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CatalogSearch.Domain.Tests")]

namespace CatalogSearch.Application;

public sealed record OrveauxPairSource(string Hash, string Format, string Location, string LatinName,
    string JapaneseName, int Vintage, int? VolumeMl, int Stock);
public sealed record OrveauxPairCandidate(string Supplier, OrveauxPairSource Excel, OrveauxPairSource Pdf,
    decimal Wholesale, decimal ReferenceRetail, decimal TaxInclusiveRetail, decimal ConditionalPrice,
    string ConditionalRaw);
public sealed record OrveauxPairPrice(string Role, decimal Amount, bool TaxIncluded, string TaxBasis,
    string Unit, string Location, string Raw);
public sealed record OrveauxPairResolution(string Status, string Reason, OrveauxPairCandidate? Pair,
    string? AnswerBinding, int? AnswerVersion, IReadOnlyList<OrveauxPairPrice> Prices)
{
    // Evidence resolution is deliberately not a product identity or an import authorization.
    public bool BusinessImportAllowed => false;
}

/// <summary>One received human decision, not a general AOC/name normalizer. No I/O or database writes.</summary>
public static class OrveauxAocPairPolicy
{
    internal sealed record Manifest(string ExcelHash, string PdfHash, string AnswerHash);
    private static readonly Manifest Production = new(
        "eae86237e63cfef92a22aef292b714831341d9a1525a00657d8126c2f4706947",
        "4461f609796057ec6f03388e573e6446fe6618d1937f81558f99dc9bc9ff6828",
        "595df47e6a9f9ae84ba7db7203e894c131b438d654010b38f457ea28a33c3ac3");
    internal const string ItemId = "source-17a7690c75c1a61f";
    internal const string Binding = "17a7690c75c1a61f6f8ead0e17c62c0a761fdf5493da64aee7f431e40d47782b";
    internal const string AnswerText = "「AOC」が省略されているが同一商品";
    public static bool IsKnownOriginalHash(string hash, bool excel) => string.Equals(hash,
        excel ? Production.ExcelHash : Production.PdfHash, StringComparison.OrdinalIgnoreCase);
    public static string KnownOriginalHash(bool excel) => excel ? Production.ExcelHash : Production.PdfHash;

    // A fixed, previously verified extraction for these exact bytes only; never a generic parser.
    public static OrveauxPairCandidate KnownCandidate(ReadOnlySpan<byte> excel, ReadOnlySpan<byte> pdf)
    {
        if (!IsKnownOriginalHash(Convert.ToHexString(SHA256.HashData(excel)), true)
            || !IsKnownOriginalHash(Convert.ToHexString(SHA256.HashData(pdf)), false))
            throw new ArgumentException("Fixed original bytes do not match.");
        return new("ORVEAUX",
            new(Production.ExcelHash, "xlsx", "新着 /66", "Château Cesseras Cuvée Olric AOC Minervois", "シャトー・セスラ　キュヴェ・オルリック　ミネルヴォワ", 2024, null, 60),
            new(Production.PdfHash, "pdf", "15/Château Cesseras Cuvée Orlic Minervois/2024/750/60本", "Château Cesseras Cuvée Orlic Minervois", "シャトー・セスラス　キュヴェ・オルリック　ミネルヴォワ", 2024, 750, 60),
            1980, 3300, 3630, 1880, @"12本価格\1,880");
    }

    public static OrveauxPairResolution Resolve(OrveauxPairCandidate candidate,
        ReadOnlySpan<byte> excelOriginal, ReadOnlySpan<byte> pdfOriginal, ReadOnlySpan<byte> answerExport) =>
        ResolveVerified(candidate, excelOriginal, pdfOriginal, answerExport, Production);

    // Only tests can substitute synthetic byte hashes. Public callers cannot supply a trusted manifest.
    internal static OrveauxPairResolution ResolveVerified(OrveauxPairCandidate c,
        ReadOnlySpan<byte> excel, ReadOnlySpan<byte> pdf, ReadOnlySpan<byte> answers, Manifest manifest)
    {
        static OrveauxPairResolution Hold(string reason) => new("Held", reason, null, null, null, []);
        static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        static bool SameHash(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        if (excel.IsEmpty || pdf.IsEmpty || answers.IsEmpty || !SameHash(Hash(excel), manifest.ExcelHash)
            || !SameHash(Hash(pdf), manifest.PdfHash) || !SameHash(Hash(answers), manifest.AnswerHash))
            return Hold("OriginalOrAnswerHashMismatch");
        try
        {
            using var json = JsonDocument.Parse(answers.ToArray());
            var root = json.RootElement;
            var item = root.GetProperty("items").GetProperty(ItemId);
            if (root.GetProperty("schema").GetString() != "lips-source-review-answers/v1"
                || item.GetProperty("binding").GetString() != Binding || item.GetProperty("choice").GetString() != "回答"
                || item.GetProperty("version").GetInt32() != 1 || item.GetProperty("text").GetString() != AnswerText
                || item.GetProperty("history").ValueKind != JsonValueKind.Array || item.GetProperty("history").GetArrayLength() != 0)
                return Hold("ReceivedAnswerMismatch");
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return Hold("ReceivedAnswerMismatch");
        }
        if (c is null || c.Excel is null || c.Pdf is null || c.Supplier != "ORVEAUX"
            || !SameHash(c.Excel.Hash, manifest.ExcelHash) || !SameHash(c.Pdf.Hash, manifest.PdfHash))
            return Hold("PairScopeMismatch");
        var expectedExcel = new OrveauxPairSource(c.Excel.Hash, "xlsx", "新着 /66",
            "Château Cesseras Cuvée Olric AOC Minervois", "シャトー・セスラ　キュヴェ・オルリック　ミネルヴォワ", 2024, null, 60);
        var expectedPdf = new OrveauxPairSource(c.Pdf.Hash, "pdf", "15/Château Cesseras Cuvée Orlic Minervois/2024/750/60本",
            "Château Cesseras Cuvée Orlic Minervois", "シャトー・セスラス　キュヴェ・オルリック　ミネルヴォワ", 2024, 750, 60);
        if (c.Excel != expectedExcel || c.Pdf != expectedPdf) return Hold("ExactSourcePairMismatch");
        if (c.Wholesale != 1980 || c.ReferenceRetail != 3300 || c.TaxInclusiveRetail != 3630
            || c.ConditionalPrice != 1880 || c.ConditionalRaw != @"12本価格\1,880") return Hold("PriceEvidenceMismatch");
        return new("ResolvedEvidenceOnly", "HumanConfirmedExactPair;ProductIdentityAndConditionalUnitUnresolved", c, Binding, 1,
            Array.AsReadOnly(new[] {
                new OrveauxPairPrice("Wholesale", c.Wholesale, false, "Excel E54 納入価格（税別）", "Unknown", "新着 /E66", "1980"),
                new OrveauxPairPrice("ReferenceRetail", c.ReferenceRetail, false, "税込明記なし:本人既定規則", "Unknown", "新着 /F66;PDF15/2024", "3300"),
                new OrveauxPairPrice("TaxInclusiveRetail", c.TaxInclusiveRetail, true, "税込明記", "Unknown", "新着 /F65;PDF15/2024", @"税込\3,630"),
                new OrveauxPairPrice("ConditionalPrice", c.ConditionalPrice, false, "Excel E54 税別列;税込明記なし", "Unknown", "新着 /E65", c.ConditionalRaw)
            }));
    }
}
