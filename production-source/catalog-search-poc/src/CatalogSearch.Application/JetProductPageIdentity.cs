using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CatalogSearch.Application;

// Site-specific candidate, not registered by default. Brand rules must be reviewed configuration.
// Only the productAbout section is evidence; recommendations and browsing history are excluded.
public sealed class JetProductPageIdentity
{
    private readonly string supplier;
    private readonly Dictionary<string, string> brands;
    public string Version { get; }
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(200);
    private static readonly Regex Tags = new("<(?<close>/)?(?<tag>[a-z][a-z0-9]*)\\b(?<attrs>(?:\"[^\"]*\"|'[^']*'|[^'\">])*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Budget);
    private static readonly Regex Attrs = new("\\G\\s+(?<name>[a-z_:][a-z0-9_:.-]*)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)')", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Budget);

    public JetProductPageIdentity(string reviewedSupplierCode, IReadOnlyDictionary<string, string> reviewedBrandNames)
    {
        if (!ImporterEvidenceValidator.ValidText(reviewedSupplierCode, 100) || reviewedBrandNames.Count is 0 or > 100)
            throw new ArgumentException("JET_RULES_INVALID");
        supplier = reviewedSupplierCode;
        brands = new(StringComparer.Ordinal);
        foreach (var (slug, producer) in reviewedBrandNames)
        {
            if (!Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]{0,99}$", RegexOptions.CultureInvariant, Budget) ||
                !ImporterEvidenceValidator.ValidText(producer, 200) || !brands.TryAdd(slug, producer))
                throw new ArgumentException("JET_RULES_INVALID");
        }
        var rules = JsonSerializer.SerializeToUtf8Bytes(new { supplier,
            brands = brands.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new { slug = x.Key, producer = x.Value }).ToArray() });
        Version = "jet-product-about/1+" + Convert.ToHexString(SHA256.HashData(rules)).ToLowerInvariant();
    }

    public bool TryExtract(ImporterProductIdentity identity, string finalUrl, ReadOnlyMemory<byte> bytes,
        out IReadOnlyList<ImporterGrapeEvidence> grapes)
    {
        grapes = Array.Empty<ImporterGrapeEvidence>();
        if (identity.SupplierCode != supplier || bytes.Length is 0 or > ImporterEvidenceValidator.MaxBodyBytes ||
            !Uri.TryCreate(finalUrl, UriKind.Absolute, out var url) || url.Scheme != "https" ||
            url.IdnHost != "www.jetlc.co.jp" || url.Port != 443 || url.UserInfo != "" || url.Query != "" || url.Fragment != "" ||
            !Regex.IsMatch(url.AbsolutePath, "^/wine/[0-9]{1,12}/$", RegexOptions.CultureInvariant, Budget)) return false;
        try
        {
            var originalHtml = new UTF8Encoding(false, true).GetString(bytes.Span);
            var html = originalHtml;
            html = Regex.Replace(html, "<!--.*?-->|<script\\b[^>]*>.*?</script\\s*>|<style\\b[^>]*>.*?</style\\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, Budget);
            // Templates may nest: do not attempt a partial regex extraction of their contents.
            if (Regex.IsMatch(html, "</?template\\b", RegexOptions.IgnoreCase, Budget)) return false;
            html = Regex.Replace(html, "<noscript\\b[^>]*>.*?</noscript\\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, Budget);
            if (Regex.IsMatch(html, "</?noscript\\b", RegexOptions.IgnoreCase, Budget)) return false;
            var section = Single(html, "div", "productAbout");
            if (section is null || section.Length > 65536) return false;
            var logo = Single(section, "p", "imgLogo");
            var table = Single(section, "table", "productTbl");
            if (logo is null || table is null || Tags.Matches(table).Any(m => m.Groups["tag"].Value.Equals("table", StringComparison.OrdinalIgnoreCase))) return false;
            var anchors = Tags.Matches(logo).Where(m => IsOpen(m, "a")).ToArray();
            var images = Tags.Matches(logo).Where(m => IsOpen(m, "img")).ToArray();
            if (anchors.Length != 1 || images.Length != 1) return false;
            var link = Attributes(anchors[0]); var image = Attributes(images[0]);
            if (!link.TryGetValue("href", out var href) || !image.TryGetValue("alt", out var product) || product != identity.Product) return false;
            if (!Uri.TryCreate(href, UriKind.Absolute, out var brandUrl) || brandUrl.Scheme != "https" || brandUrl.IdnHost != "www.jetlc.co.jp" ||
                brandUrl.Port != 443 || brandUrl.UserInfo != "" || brandUrl.Query != "" || brandUrl.Fragment != "") return false;
            var brand = Regex.Match(brandUrl.AbsolutePath, "^/wine/brand/([a-z0-9-]+)/$", RegexOptions.CultureInvariant, Budget);
            if (!brand.Success || !brands.TryGetValue(brand.Groups[1].Value, out var producer) || producer != identity.Producer) return false;
            var rows = Elements(table, "tr", null);
            if (rows.Count is 0 or > 64) return false;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var heads = Elements(row, "th", null); var cells = Elements(row, "td", null);
                if (heads.Count != 1 || cells.Count != 1 || heads[0].Contains('<')) return false;
                var key = WebUtility.HtmlDecode(heads[0]).Trim();
                if (!values.TryAdd(key, cells[0])) return false;
            }
            string? Plain(string key) => values.TryGetValue(key, out var value) && !value.Contains('<') ? WebUtility.HtmlDecode(value).Trim() : null;
            if (Plain("商品コード") != identity.Sku || Plain("VINTAGE") != identity.Vintage || Plain("色") != identity.Variant) return false;
            var volume = Plain("容量/入数");
            if (volume is null || !Regex.IsMatch(volume, "^" + identity.VolumeMl.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ml/[1-9][0-9]{0,3}$", RegexOptions.CultureInvariant, Budget)) return false;
            var excerpt = Plain("ブドウ品種");
            if (!ImporterEvidenceValidator.ValidText(excerpt, 1000)) return false;
            // Initial supported shape: one explicit grape and percentage. Lists/ranges remain candidates.
            var g = Regex.Match(excerpt!, "^(?<name>[^0-9%,;、/<>]{1,100}?)\\s+(?<percent>(?:100|[0-9]{1,2})(?:\\.[0-9]{1,6})?)%$", RegexOptions.CultureInvariant, Budget);
            if (!g.Success) return false;
            var name = g.Groups["name"].Value.Trim();
            if (!decimal.TryParse(g.Groups["percent"].Value, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var percent) || percent > 100 ||
                !ImporterEvidenceValidator.ValidText(name, 100) || !originalHtml.Contains(excerpt!, StringComparison.Ordinal)) return false;
            grapes = Array.AsReadOnly(new[] { new ImporterGrapeEvidence(name, excerpt!) });
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or DecoderFallbackException or RegexMatchTimeoutException or InvalidOperationException) { return false; }
    }

    public bool Verify(ImporterProductIdentity identity, IReadOnlyList<ImporterGrapeEvidence> claims,
        string finalUrl, ReadOnlyMemory<byte> body) => TryExtract(identity, finalUrl, body, out var actual) && actual.SequenceEqual(claims);

    private static bool IsOpen(Match m, string tag) => m.Groups["close"].Length == 0 && m.Groups["tag"].Value.Equals(tag, StringComparison.OrdinalIgnoreCase);
    private static Dictionary<string, string> Attributes(Match tag)
    {
        var s = tag.Groups["attrs"].Value; var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var at = 0;
        while (at < s.Length)
        {
            if (string.IsNullOrWhiteSpace(s[at..]) || s[at..].Trim() == "/") break;
            var m = Attrs.Match(s, at); if (!m.Success || m.Index != at || !result.TryAdd(m.Groups["name"].Value, WebUtility.HtmlDecode(m.Groups["value"].Value))) throw new ArgumentException("JET_ATTRIBUTE_UNSUPPORTED");
            at += m.Length;
        }
        return result;
    }
    private static string? Single(string html, string tag, string cls)
    { var values = Elements(html, tag, cls); return values.Count == 1 ? values[0] : null; }
    private static List<string> Elements(string html, string tag, string? cls)
    {
        var tags = Tags.Matches(html).Where(m => m.Groups["tag"].Value.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToArray();
        var results = new List<string>();
        for (var i = 0; i < tags.Length; i++)
        {
            var open = tags[i]; if (!IsOpen(open, tag)) continue;
            if (cls is not null)
            { var a = Attributes(open); if (!a.TryGetValue("class", out var c) || !c.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(cls)) continue; }
            var depth = 1; var j = i + 1;
            for (; j < tags.Length && depth != 0; j++) depth += IsOpen(tags[j], tag) ? 1 : -1;
            if (depth != 0) throw new ArgumentException("JET_UNBALANCED_SECTION");
            var close = tags[j - 1]; results.Add(html[(open.Index + open.Length)..close.Index]);
            // Do not skip nested targets: duplicate/nested product sections must be rejected.
        }
        return results;
    }
}
