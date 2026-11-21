using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using CatalogSearch.Application;

namespace CatalogSearch.Infrastructure;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record EnrichmentIndexEntry(string ContentSha256, string VerificationSha256);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record EnrichmentIndex(string Schema, long Generation, string? PreviousIndexSha256,
    SortedDictionary<string, EnrichmentIndexEntry> Entries);

internal static class EnrichmentFiles
{
    internal static FileStream CreatePrivateFile(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }
    internal static void CreatePrivateDirectory(string path)
    {
        // Existing owner/mode are not changed. Unknown/reparse paths are rejected, not repaired.
        if (Path.Exists(path) || new DirectoryInfo(path).LinkTarget is not null) { DirectorySafe(path); return; }
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        DirectorySafe(path);
    }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenDirectory(string path, int flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int fd);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);
    internal static void SyncDirectory(string path)
    {
        // Linux deployment only: directory durability is mandatory. No fallback on failure.
        // Windows guarantees here are limited to file Flush(true) and atomic same-directory rename.
        if (!OperatingSystem.IsLinux()) return;
        const int flags = 0x10000 | 0x20000 | 0x80000; // O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC
        var fd = OpenDirectory(path, flags);
        if (fd < 0) throw new IOException("DIRECTORY_SYNC_OPEN_FAILED");
        var synced = false;
        try { if (Fsync(fd) != 0) throw new IOException("DIRECTORY_SYNC_FAILED"); synced = true; }
        finally { var closed = Close(fd); if (synced && closed != 0) throw new IOException("DIRECTORY_SYNC_CLOSE_FAILED"); }
    }
    internal const int RecordLimit = 65536, IndexLimit = 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 12
    };
    internal static readonly HashSet<string> States = new(StringComparer.Ordinal)
        { "candidate", "verified", "conflict", "stale", "unavailable", "withdrawn" };
    internal static void Need(bool ok, string code) { if (!ok) throw new InvalidDataException(code); }
    internal static bool Hash(string? s) => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static bool Text(string? s, int max) => s is not null && s.Length is > 0 && s.Length <= max &&
        s == s.Trim() && !s.Any(char.IsControl);
    internal static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    internal static string Key(ImporterProductIdentity identity) => Digest(Bytes(identity));
    internal static void DirectorySafe(string path)
    {
        var info = new DirectoryInfo(path);
        Need(info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0 && info.LinkTarget is null, "DIRECTORY_UNSAFE");
    }
    internal static void RootSafe(string root)
    {
        var current = new DirectoryInfo(Path.GetFullPath(root));
        while (current is not null) { DirectorySafe(current.FullName); current = current.Parent; }
    }
    internal static byte[] Read(string path, int limit)
    {
        var info = new FileInfo(path);
        Need(info.Exists && (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0 &&
            info.LinkTarget is null && info.Length is > 0 && info.Length <= limit, "FILE_UNSAFE_OR_SIZE");
        var length = info.Length; var modified = info.LastWriteTimeUtc;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        Need(stream.Length == length, "FILE_CHANGED");
        var bytes = new byte[checked((int)length)]; stream.ReadExactly(bytes);
        Need(stream.ReadByte() == -1 && stream.Length == length, "FILE_CHANGED");
        info.Refresh(); Need(info.Exists && info.Length == length && info.LastWriteTimeUtc == modified &&
            (info.Attributes & FileAttributes.ReparsePoint) == 0, "FILE_CHANGED");
        return bytes;
    }
    internal static T Parse<T>(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
        Duplicates(doc.RootElement);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("JSON_NULL");
    }
    private static void Duplicates(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in e.EnumerateObject()) { Need(seen.Add(p.Name), "JSON_DUPLICATE"); Duplicates(p.Value); }
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Duplicates(x);
    }
    internal static string RecordPath(string root, string hash)
    { Need(Hash(hash), "RECORD_HASH_INVALID"); return Path.Combine(root, "records", hash + ".json"); }
    internal static byte[] Record(string root, string hash)
    { var bytes = Read(RecordPath(root, hash), RecordLimit); Need(Digest(bytes) == hash, "RECORD_HASH_MISMATCH"); return bytes; }
    internal static EnrichmentIndex LoadIndex(string root, out byte[]? bytes)
    {
        var path = Path.Combine(root, "index.json");
        if (!File.Exists(path))
        { bytes = null; return new("lips-importer-enrichment-index/v1", 0, null, new(StringComparer.Ordinal)); }
        bytes = Read(path, IndexLimit); var index = Parse<EnrichmentIndex>(bytes);
        Need(index.Schema == "lips-importer-enrichment-index/v1" && index.Generation > 0 &&
            (index.Generation == 1 ? index.PreviousIndexSha256 is null : Hash(index.PreviousIndexSha256)) &&
            index.Entries is not null && index.Entries.Count <= 5000, "INDEX_INVALID");
        foreach (var (key, entry) in index.Entries!)
            Need(Hash(key) && entry is not null && Hash(entry.ContentSha256) && Hash(entry.VerificationSha256), "INDEX_ENTRY_INVALID");
        var history = Path.Combine(root, "history", index.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Digest(bytes) + ".json");
        Need(Read(history, IndexLimit).AsSpan().SequenceEqual(bytes), "INDEX_HISTORY_MISMATCH");
        if (index.Generation > 1)
        {
            var previous = Read(Path.Combine(root, "history", (index.Generation - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + index.PreviousIndexSha256 + ".json"), IndexLimit);
            Need(Digest(previous) == index.PreviousIndexSha256 && Parse<EnrichmentIndex>(previous).Generation == index.Generation - 1, "INDEX_HISTORY_CHAIN_MISMATCH");
        }
        return index;
    }
    internal static void ContentValid(ImporterEnrichmentContent c, DateTimeOffset now)
    {
        Need(c.Schema == "lips-importer-enrichment-content/v1" && c.Identity is not null &&
            Text(c.Identity.SupplierCode, 100) && Text(c.Identity.Sku, 100) && Text(c.Identity.Producer, 200) &&
            Text(c.Identity.Product, 200) && Text(c.Identity.Vintage, 32) && Text(c.Identity.Variant, 100) &&
            c.Identity.VolumeMl is > 0 and <= 100000 && Hash(c.OriginalFingerprint) && Text(c.OriginalSourceId, 200) &&
            Hash(c.BodySha256) && c.FetchedAtUtc != default && c.FetchedAtUtc.Offset == TimeSpan.Zero && c.FetchedAtUtc <= now,
            "CONTENT_INVALID");
        foreach (var url in new[] { c.RequestedUrl, c.FinalUrl })
            Need(Text(url, 2048) && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" &&
                u.Port == 443 && u.UserInfo == "" && u.Query == "" && u.Fragment == "" && !u.IsLoopback &&
                u.HostNameType == UriHostNameType.Dns, "CONTENT_URL_INVALID");
        Need(c.ETag is null || Text(c.ETag, 300), "ETAG_INVALID");
        Need(c.LastModified is null || Text(c.LastModified, 100), "LAST_MODIFIED_INVALID");
        Need(c.Extraction is not null && Text(c.Extraction.AdapterVersion, 100) && Text(c.Extraction.ExtractorVersion, 100), "EXTRACTION_INVALID");
        var x = c.Extraction!;
        Need(x.Method is "deterministic" or "llm", "EXTRACTION_METHOD_INVALID");
        Need(x.Method == "llm" ? Text(x.Model, 200) && Text(x.PromptVersion, 100) && Hash(x.InputSha256) :
            x.Model is null && x.PromptVersion is null && x.InputSha256 is null, "EXTRACTION_PROVENANCE_INVALID");
        Need(c.Grapes is not null && c.Grapes.Count <= 20 && c.Grapes.All(g => g is not null && Text(g.Grape, 100) && Text(g.SourceExcerpt, 1000)), "GRAPES_INVALID");
    }
    internal static void EventValid(ImporterEnrichmentVerification e, string contentHash, ImporterEnrichmentContent c, DateTimeOffset now)
    {
        Need(e.Schema == "lips-importer-enrichment-verification/v1" && e.ContentRecordSha256 == contentHash &&
            States.Contains(e.Status) && e.VerifiedAtUtc != default && e.VerifiedAtUtc.Offset == TimeSpan.Zero &&
            e.VerifiedAtUtc >= c.FetchedAtUtc && e.VerifiedAtUtc <= now && e.ExpiresAtUtc.Offset == TimeSpan.Zero &&
            e.ExpiresAtUtc > e.VerifiedAtUtc && e.ExpiresAtUtc <= e.VerifiedAtUtc.AddDays(30) &&
            e.ConfirmationKind is "200" or "304" or "404" or "manual" or "unavailable", "VERIFICATION_INVALID");
        Need(e.ConfirmationKind != "404" || e.Status == "withdrawn", "WITHDRAWAL_REQUIRED");
        // Actual conditional-fetch/304 provenance is not implemented. Do not renew from old proof.
        Need(e.Status != "verified" || (e.ConfirmationKind == "200" &&
            e.VerifiedAtUtc - c.FetchedAtUtc <= TimeSpan.FromSeconds(60)), "VERIFIED_REQUIRES_FRESH_200");
    }
    internal static bool Incomplete(string root) => File.Exists(Path.Combine(root, ".writer.lock")) ||
        Directory.EnumerateFiles(root, ".index-*.tmp", SearchOption.TopDirectoryOnly).Any();
}

// No writes, acquisition, HTTP, DB or repair. Corrupt/unknown state excludes enrichment.
public sealed class FileImporterEnrichmentReader(string root) : IImporterEnrichmentReader
{
    public ImporterEnrichmentReadResult ReadVerifiedForProduct(ImporterProductIdentity identity, string originalFingerprint,
        string extractorVersion, DateTimeOffset nowUtc) => ReadVerified(identity, originalFingerprint, extractorVersion, nowUtc);
    public ImporterEnrichmentReadResult ReadVerified(ImporterProductIdentity identity, string originalFingerprint,
        string extractorVersion, DateTimeOffset nowUtc) => ReadVerified(new(identity, originalFingerprint, null, extractorVersion, nowUtc));
    public ImporterEnrichmentReadResult ReadVerified(ImporterEnrichmentLookup lookup)
    {
        try
        {
            if (!Directory.Exists(root)) return new("NOT_FOUND", null, null, null);
            EnrichmentFiles.RootSafe(root); EnrichmentFiles.DirectorySafe(Path.Combine(root, "records")); EnrichmentFiles.DirectorySafe(Path.Combine(root, "history"));
            if (EnrichmentFiles.Incomplete(root)) return new("WRITER_STATE_UNKNOWN", null, null, null);
            EnrichmentFiles.Need(lookup.NowUtc != default && lookup.NowUtc.Offset == TimeSpan.Zero, "NOW_INVALID");
            var index = EnrichmentFiles.LoadIndex(root, out var before);
            if (!index.Entries.TryGetValue(EnrichmentFiles.Key(lookup.Identity), out var entry)) return new("NOT_FOUND", index.Generation, null, null);
            var content = EnrichmentFiles.Parse<ImporterEnrichmentContent>(EnrichmentFiles.Record(root, entry.ContentSha256));
            var verification = EnrichmentFiles.Parse<ImporterEnrichmentVerification>(EnrichmentFiles.Record(root, entry.VerificationSha256));
            EnrichmentFiles.ContentValid(content, lookup.NowUtc); EnrichmentFiles.EventValid(verification, entry.ContentSha256, content, lookup.NowUtc);
            if (content.Identity != lookup.Identity || content.OriginalFingerprint != lookup.OriginalFingerprint ||
                content.Extraction.ExtractorVersion != lookup.ExtractorVersion ||
                (lookup.ExpectedFinalUrl is not null && content.FinalUrl != lookup.ExpectedFinalUrl)) return new("BINDING_CHANGED", index.Generation, null, null);
            if (EnrichmentFiles.Incomplete(root) || before is null ||
                !EnrichmentFiles.Read(Path.Combine(root, "index.json"), EnrichmentFiles.IndexLimit).AsSpan().SequenceEqual(before))
                return new("SNAPSHOT_CHANGED", null, null, null);
            // Conflict excerpts identify only affected grape fields; they are NOT adopted facts.
            if (verification.Status == "conflict") return new("conflict", index.Generation, entry.ContentSha256, content);
            if (verification.Status != "verified") return new(verification.Status, index.Generation, null, null);
            if (verification.ExpiresAtUtc <= lookup.NowUtc) return new("EXPIRED", index.Generation, null, null);
            return new("verified", index.Generation, entry.ContentSha256, content);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { return new("CORRUPT_OR_UNAVAILABLE", null, null, null); }
    }
}

// Intended only for the offline acquisition actor, never registered as a Web service.
public sealed class FileImporterEnrichmentWriter(string root, ImporterEnrichmentStorageLimits limits)
{
    public ImporterEnrichmentCommitResult Commit(ImporterEnrichmentContent content, string status,
        DateTimeOffset verifiedAtUtc, DateTimeOffset expiresAtUtc, string confirmationKind,
        long expectedGeneration, DateTimeOffset nowUtc, ValidatedImporterProductEvidence? proof)
    {
        EnrichmentFiles.Need(limits.MaximumFiles is > 0 and <= 100000 && limits.MaximumTotalBytes > 0 &&
            nowUtc != default && nowUtc.Offset == TimeSpan.Zero && expectedGeneration >= 0, "WRITE_POLICY_INVALID");
        EnrichmentFiles.ContentValid(content, nowUtc);
        var contentBytes = EnrichmentFiles.Bytes(content); var contentSha = EnrichmentFiles.Digest(contentBytes);
        var verification = new ImporterEnrichmentVerification("lips-importer-enrichment-verification/v1", contentSha,
            status, verifiedAtUtc, expiresAtUtc, confirmationKind);
        EnrichmentFiles.EventValid(verification, contentSha, content, nowUtc);
        if (status is "verified" or "conflict")
            EnrichmentFiles.Need(proof is not null && proof.Identity == content.Identity &&
                proof.OriginalFingerprint == content.OriginalFingerprint && proof.RequestedUrl == content.RequestedUrl &&
                proof.FinalUrl == content.FinalUrl && proof.ContentSha256 == content.BodySha256 && proof.FetchedAtUtc == content.FetchedAtUtc &&
                proof.Grapes.SequenceEqual(content.Grapes), "VALIDATED_PROOF_REQUIRED");
        EnrichmentFiles.Need(contentBytes.Length <= EnrichmentFiles.RecordLimit, "RECORD_SIZE");
        // Root must already exist under a reviewed storage configuration; never create production paths implicitly.
        EnrichmentFiles.RootSafe(root);
        if (EnrichmentFiles.Incomplete(root)) throw new IOException("WRITER_STATE_UNKNOWN");
        var lockPath = Path.Combine(root, ".writer.lock");
        using var writerLock = EnrichmentFiles.CreatePrivateFile(lockPath);
        writerLock.Flush(flushToDisk: true);
        var completed = false;
        try
        {
            EnrichmentFiles.SyncDirectory(root);
            foreach (var dir in new[] { "records", "history" })
            { var path = Path.Combine(root, dir); EnrichmentFiles.CreatePrivateDirectory(path); EnrichmentFiles.SyncDirectory(path); }
            EnrichmentFiles.SyncDirectory(root);
            var index = EnrichmentFiles.LoadIndex(root, out var oldBytes);
            EnrichmentFiles.Need(index.Generation == expectedGeneration, "INDEX_GENERATION_CONFLICT");
            var existingContent = File.Exists(EnrichmentFiles.RecordPath(root, contentSha));
            if (confirmationKind == "304")
                EnrichmentFiles.Need(existingContent && index.Entries.TryGetValue(EnrichmentFiles.Key(content.Identity), out var previous) &&
                    previous.ContentSha256 == contentSha, "304_REQUIRES_UNCHANGED_CURRENT_CONTENT");
            SaveRecord(contentSha, contentBytes);
            var eventBytes = EnrichmentFiles.Bytes(verification); var eventSha = EnrichmentFiles.Digest(eventBytes);
            SaveRecord(eventSha, eventBytes);
            var latest = EnrichmentFiles.LoadIndex(root, out var latestBytes);
            EnrichmentFiles.Need(latest.Generation == expectedGeneration &&
                (oldBytes is null ? latestBytes is null : latestBytes is not null && oldBytes.AsSpan().SequenceEqual(latestBytes)), "INDEX_GENERATION_CONFLICT");
            var entries = new SortedDictionary<string, EnrichmentIndexEntry>(index.Entries, StringComparer.Ordinal)
            { [EnrichmentFiles.Key(content.Identity)] = new(contentSha, eventSha) };
            EnrichmentFiles.Need(entries.Count <= 5000, "INDEX_CAPACITY");
            var next = new EnrichmentIndex("lips-importer-enrichment-index/v1", checked(index.Generation + 1),
                oldBytes is null ? null : EnrichmentFiles.Digest(oldBytes), entries);
            var nextBytes = EnrichmentFiles.Bytes(next); EnrichmentFiles.Need(nextBytes.Length <= EnrichmentFiles.IndexLimit, "INDEX_SIZE");
            var history = Path.Combine(root, "history", next.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + EnrichmentFiles.Digest(nextBytes) + ".json");
            SaveNew(history, nextBytes);
            var temp = Path.Combine(root, ".index-" + Guid.NewGuid().ToString("N") + ".tmp");
            SaveNew(temp, nextBytes);
            // Same-directory replacement; Linux fsync ordering is mandatory, Windows has the stated narrower guarantee.
            File.Move(temp, Path.Combine(root, "index.json"), overwrite: true);
            EnrichmentFiles.SyncDirectory(root);
            EnrichmentFiles.Need(EnrichmentFiles.Read(Path.Combine(root, "index.json"), EnrichmentFiles.IndexLimit).AsSpan().SequenceEqual(nextBytes), "INDEX_READBACK_FAILED");
            EnrichmentFiles.Record(root, contentSha); EnrichmentFiles.Record(root, eventSha);
            completed = true;
            return new(next.Generation, contentSha, eventSha, existingContent);
        }
        finally
        {
            writerLock.Dispose();
            // Any failed operation retains the lock marker and partials for inspection, never replay/repair.
            if (completed)
            {
                File.Delete(lockPath);
                try { EnrichmentFiles.SyncDirectory(root); }
                catch
                {
                    // Do not report success after an uncertain durable unlock. Preserve a marker if
                    // no subsequent writer already owns it; never overwrite another actor's marker.
                    if (!File.Exists(lockPath))
                    {
                        using var marker = EnrichmentFiles.CreatePrivateFile(lockPath);
                        marker.Flush(flushToDisk: true);
                    }
                    throw;
                }
            }
        }
    }
    private void SaveRecord(string hash, byte[] bytes)
    {
        var path = EnrichmentFiles.RecordPath(root, hash);
        if (File.Exists(path)) { EnrichmentFiles.Need(EnrichmentFiles.Record(root, hash).AsSpan().SequenceEqual(bytes), "EXISTING_RECORD_CHANGED"); return; }
        SaveNew(path, bytes);
    }
    private void SaveNew(string path, byte[] bytes)
    {
        foreach (var dir in new[] { root, Path.Combine(root, "records"), Path.Combine(root, "history") }) EnrichmentFiles.DirectorySafe(dir);
        var files = new[] { root, Path.Combine(root, "records"), Path.Combine(root, "history") }
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.TopDirectoryOnly)).Select(p => new FileInfo(p)).ToArray();
        EnrichmentFiles.Need(files.Length < limits.MaximumFiles && files.All(f => (f.Attributes & FileAttributes.ReparsePoint) == 0) &&
            files.Sum(f => f.Length) + bytes.Length <= limits.MaximumTotalBytes, "STORAGE_CAPACITY");
        using (var stream = EnrichmentFiles.CreatePrivateFile(path))
        { stream.Write(bytes); stream.Flush(flushToDisk: true); }
        EnrichmentFiles.SyncDirectory(Path.GetDirectoryName(path)!);
        EnrichmentFiles.Need(EnrichmentFiles.Read(path, EnrichmentFiles.IndexLimit).AsSpan().SequenceEqual(bytes), "WRITE_READBACK_FAILED");
    }
}
