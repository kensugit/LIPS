using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace CatalogSearch.Application;

public interface IImporterProductPageAdapter
{
    string Version { get; }
    bool TryExtract(ImporterProductIdentity identity, string finalUrl, ReadOnlyMemory<byte> body, out IReadOnlyList<ImporterGrapeEvidence> grapes);
    bool Verify(ImporterProductIdentity identity, IReadOnlyList<ImporterGrapeEvidence> grapes, string finalUrl, ReadOnlyMemory<byte> body);
}

/// <summary>Trusted deployment configuration only. Supplier mappings never come from model output or fetched HTML.</summary>
public sealed class ImporterPageAdapterPolicy
{
    readonly Dictionary<string, IImporterProductPageAdapter> overrides = new(StringComparer.Ordinal);
    readonly IImporterProductPageAdapter defaultAdapter = new JsonLdAdapter();
    public string Version { get; }
    public ImporterPageAdapterPolicy(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? reviewedJetSuppliers = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, YourCellarProductPageIdentity.ProducerRule>>? reviewedYourCellarSuppliers = null)
    {
        if ((reviewedJetSuppliers?.Count ?? 0) + (reviewedYourCellarSuppliers?.Count ?? 0) > 100) throw new ArgumentException("ADAPTER_POLICY_INVALID");
        foreach (var pair in reviewedJetSuppliers ?? new Dictionary<string, IReadOnlyDictionary<string, string>>())
        {
            if (!ImporterEvidenceValidator.ValidText(pair.Key,100) || pair.Value is null ||
                !overrides.TryAdd(pair.Key, new JetAdapter(pair.Key, pair.Value))) throw new ArgumentException("ADAPTER_POLICY_INVALID");
        }
        foreach (var pair in reviewedYourCellarSuppliers ?? new Dictionary<string, IReadOnlyDictionary<string, YourCellarProductPageIdentity.ProducerRule>>())
        {
            if (!ImporterEvidenceValidator.ValidText(pair.Key,100) || pair.Value is null ||
                !overrides.TryAdd(pair.Key, new YourCellarAdapter(pair.Key, pair.Value))) throw new ArgumentException("ADAPTER_POLICY_INVALID");
        }
        // Preserve existing JSON-LD-only policy identity. Mixed policies invalidate all earlier cache generations by version.
        Version = overrides.Count == 0 ? defaultAdapter.Version : "official-product-adapters/1+" +
            Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
                defaultVersion = defaultAdapter.Version,
                suppliers = overrides.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new { supplier = x.Key, version = x.Value.Version }).ToArray()
            })));
    }
    public IImporterProductPageAdapter Select(string supplier) => overrides.TryGetValue(supplier, out var adapter) ? adapter : defaultAdapter;
    public string BoundVersion(ImporterEvidenceHostPolicy hosts) => overrides.Count == 0 ? Version + "+" + hosts.Version :
        "official-product-adapters/1+" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "\n" + hosts.Version)));
    sealed class JsonLdAdapter : IImporterProductPageAdapter
    {
        public string Version => "official-product-jsonld/1";
        public bool TryExtract(ImporterProductIdentity i,string url,ReadOnlyMemory<byte> body,out IReadOnlyList<ImporterGrapeEvidence> grapes) => OfficialProductPageIdentity.TryExtract(i,body,out grapes);
        public bool Verify(ImporterProductIdentity i,IReadOnlyList<ImporterGrapeEvidence> grapes,string url,ReadOnlyMemory<byte> body) => OfficialProductPageIdentity.Verify(i,grapes,body);
    }
    sealed class JetAdapter(string supplier,IReadOnlyDictionary<string,string> brands) : IImporterProductPageAdapter
    {
        readonly JetProductPageIdentity inner = new(supplier,brands);
        public string Version => inner.Version;
        public bool TryExtract(ImporterProductIdentity i,string url,ReadOnlyMemory<byte> body,out IReadOnlyList<ImporterGrapeEvidence> grapes) => inner.TryExtract(i,url,body,out grapes);
        public bool Verify(ImporterProductIdentity i,IReadOnlyList<ImporterGrapeEvidence> grapes,string url,ReadOnlyMemory<byte> body) => inner.Verify(i,grapes,url,body);
    }
    sealed class YourCellarAdapter(string supplier,IReadOnlyDictionary<string,YourCellarProductPageIdentity.ProducerRule> producers) : IImporterProductPageAdapter
    {
        readonly YourCellarProductPageIdentity inner = new(supplier,producers);
        public string Version => inner.Version;
        public bool TryExtract(ImporterProductIdentity i,string url,ReadOnlyMemory<byte> body,out IReadOnlyList<ImporterGrapeEvidence> grapes) => inner.TryExtract(i,url,body,out grapes);
        public bool Verify(ImporterProductIdentity i,IReadOnlyList<ImporterGrapeEvidence> grapes,string url,ReadOnlyMemory<byte> body) => inner.Verify(i,grapes,url,body);
    }
}
