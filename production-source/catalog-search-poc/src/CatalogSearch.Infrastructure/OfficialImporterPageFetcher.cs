using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using CatalogSearch.Application;

namespace CatalogSearch.Infrastructure;

// Ephemeral body; never pass this record to the persistent evidence store.
public sealed record OfficialImporterPage(string RequestedUrl, string FinalUrl,
    DateTimeOffset FetchedAtUtc, string ContentSha256, ReadOnlyMemory<byte> Body);

public interface IOfficialImporterPageFetcher
{
    Task<OfficialImporterPage> FetchAsync(string supplier, string url,
        ImporterEvidenceHostPolicy policy, CancellationToken cancellationToken = default);
}

public sealed class OfficialImporterPageFetcher : IOfficialImporterPageFetcher
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    private readonly Func<HttpMessageHandler>? testHandler;
    public OfficialImporterPageFetcher() { }
    internal OfficialImporterPageFetcher(Func<HttpMessageHandler> syntheticHandler) => testHandler = syntheticHandler;

    // Conservative global-unicast policy. Unsupported/special-purpose networks are rejected.
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (b.Length == 4)
            return b[0] is > 0 and < 224 && b[0] != 10 && b[0] != 127 &&
                !(b[0] == 100 && b[1] is >= 64 and <= 127) &&
                !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31) &&
                !(b[0] == 192 && (b[1] == 0 || b[1] == 168 || b[1] == 88 && b[2] == 99)) &&
                !(b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100)) &&
                !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        // Only 2000::/3; exclude IETF protocol assignments, documentation and 6to4.
        return b.Length == 16 && address.ScopeId == 0 && (b[0] & 0xe0) == 0x20 &&
            !(b[0] == 0x20 && b[1] == 0x01 && (b[2] < 2 || b[2] == 0x0d && b[3] == 0xb8)) &&
            !(b[0] == 0x20 && b[1] == 0x02) && !(b[0] == 0x3f && b[1] == 0xff);
    }

    public async Task<OfficialImporterPage> FetchAsync(string supplier, string url,
        ImporterEvidenceHostPolicy policy, CancellationToken cancellationToken = default)
    {
        if (!policy.Allows(supplier, url)) throw new InvalidOperationException("IMPORTER_URL_NOT_ALLOWED");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        var gate = Gates.GetOrAdd(supplier, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            var current = new Uri(url);
            for (var redirects = 0; ; redirects++)
            {
                if (!policy.Allows(supplier, current.AbsoluteUri)) throw new InvalidOperationException("REDIRECT_NOT_ALLOWED");
                // A new handler per hop prevents connection pooling across validated hosts.
                var connectAttempts = 0;
                using var handler = testHandler?.Invoke() ?? new SocketsHttpHandler
                {
                    AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
                    AutomaticDecompression = DecompressionMethods.None, MaxResponseHeadersLength = 32,
                    ConnectCallback = async (context, ct) =>
                    {
                        if (Interlocked.Increment(ref connectAttempts) != 1)
                            throw new InvalidOperationException("CONNECTION_RETRY_FORBIDDEN");
                        if (context.DnsEndPoint.Host != current.IdnHost || context.DnsEndPoint.Port != 443)
                            throw new InvalidOperationException("CONNECTION_HOST_MISMATCH");
                        var addresses = await Dns.GetHostAddressesAsync(current.IdnHost, ct);
                        if (addresses.Length is 0 or > 32 || addresses.Any(x => !IsPublicAddress(x)))
                            throw new InvalidOperationException("NONPUBLIC_DNS_ADDRESS");
                        var address = addresses[0]; // No automatic fallback/retry.
                        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        try
                        {
                            await socket.ConnectAsync(new IPEndPoint(address, 443), ct);
                            if (socket.RemoteEndPoint is not IPEndPoint remote || !remote.Address.Equals(address) || remote.Port != 443)
                                throw new InvalidOperationException("REMOTE_ADDRESS_MISMATCH");
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch { socket.Dispose(); throw; }
                    }
                };
                // TLS remains handled by HttpClient for the original DNS hostname; no certificate override.
                using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                using var request = new HttpRequestMessage(HttpMethod.Get, current)
                {
                    Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact
                };
                request.Headers.Accept.ParseAdd("text/html,application/ld+json");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (redirects >= 3 || response.Headers.Location is null)
                        throw new InvalidOperationException("REDIRECT_LIMIT_OR_MISSING");
                    current = new Uri(current, response.Headers.Location);
                    continue;
                }
                if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("PAGE_NOT_200");
                if (response.Content.Headers.ContentEncoding.Count != 0 ||
                    response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/ld+json") ||
                    response.Content.Headers.ContentLength > ImporterEvidenceValidator.MaxBodyBytes)
                    throw new InvalidOperationException("PAGE_FORMAT_OR_SIZE");
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                var bytes = await ReadBoundedAsync(stream, token);
                return new(url, current.AbsoluteUri, DateTimeOffset.UtcNow,
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes);
            }
        }
        finally { gate.Release(); }
    }

    public static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var block = new byte[16384];
        while (true)
        {
            var count = await stream.ReadAsync(block.AsMemory(), token);
            if (count == 0) break;
            if (buffer.Length + count > ImporterEvidenceValidator.MaxBodyBytes)
                throw new InvalidOperationException("PAGE_TOO_LARGE");
            buffer.Write(block, 0, count);
        }
        if (buffer.Length == 0) throw new InvalidOperationException("PAGE_EMPTY");
        return buffer.ToArray();
    }
}
