using System.Net;

namespace LipsLanGateway;

public sealed class LanPolicy
{
    public string PublicOrigin { get; }
    public string PublicAuthority { get; }
    private readonly System.Net.IPNetwork[] networks;

    public LanPolicy(string origin, string cidr)
    {
        var uri = new Uri(origin, UriKind.Absolute);
        networks = cidr.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(System.Net.IPNetwork.Parse).ToArray();
        if (networks.Length == 0 || networks.Any(n => n.PrefixLength < 8)) throw new ArgumentException("Explicit network ranges required.");
        if (uri.Scheme != "http" || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "" ||
            !IPAddress.TryParse(uri.Host, out var address) ||
            (!networks.Any(n => n.Contains(address)) && !IPAddress.IsLoopback(address)))
            throw new ArgumentException("Public origin must be an HTTP IP address on the allowed network.");
        PublicOrigin = uri.GetLeftPart(UriPartial.Authority);
        PublicAuthority = uri.Authority;
    }

    public bool AllowsPeer(IPAddress? ip)
    {
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return IPAddress.IsLoopback(ip) || networks.Any(n => n.Contains(ip));
    }

    public bool AllowsRequest(HttpRequest request)
    {
        if (!string.Equals(request.Host.Value, PublicAuthority, StringComparison.OrdinalIgnoreCase)) return false;
        if (request.Headers["Sec-Fetch-Site"] == "cross-site") return false;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) return true;
        return HttpMethods.IsPost(request.Method) && request.Headers.Origin.Count == 1 &&
            request.Headers.Origin[0] == PublicOrigin && request.Headers["X-Catalog-Action"] == "local-demo";
    }
}
