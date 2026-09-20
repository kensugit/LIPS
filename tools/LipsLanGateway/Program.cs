using System.Net;
using LipsLanGateway;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var policy = new LanPolicy(builder.Configuration["PublicOrigin"] ?? "http://192.168.1.5:55441",
    builder.Configuration["AllowedNetworks"] ?? "192.168.1.0/24;10.10.10.0/24;192.168.100.0/24");
var port = new Uri(policy.PublicOrigin).Port;
var upstream = builder.Configuration["Upstream"] ?? "http://127.0.0.1:55440/";
var upstreamUri = new Uri(upstream);
if (upstreamUri.Scheme != "http" || upstreamUri.Host != "127.0.0.1" || upstreamUri.AbsolutePath != "/" ||
    upstreamUri.Query != "" || upstreamUri.UserInfo != "" || upstreamUri.Fragment != "" || upstreamUri.Port == port)
    throw new ArgumentException("Upstream must be a different IPv4 loopback HTTP port.");
builder.WebHost.ConfigureKestrel(o =>
{
    o.Listen(IPAddress.Any, port);
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = 21_000_000;
});
builder.Services.AddHttpForwarder();
var app = builder.Build();
using var client = new HttpMessageInvoker(new SocketsHttpHandler
{
    UseProxy = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None,
    UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(10)
});
var transform = new LocalCatalogTransform(upstreamUri.GetLeftPart(UriPartial.Authority));
var config = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(5) };
app.Run(async context =>
{
    // Do not trust X-Forwarded-For: only the actual socket peer grants LAN access.
    if (!policy.AllowsPeer(context.Connection.RemoteIpAddress) || !policy.AllowsRequest(context.Request))
    { context.Response.StatusCode = 403; return; }
    if (context.Request.Path.StartsWithSegments("/api/fixtures"))
    { context.Response.StatusCode = 404; return; }
    context.Response.Headers["X-LIPS-Gateway"] = "lan-v1";
    var forwarder = context.RequestServices.GetRequiredService<IHttpForwarder>();
    var error = await forwarder.SendAsync(context, upstream, client, config, transform);
    if (error != ForwarderError.None)
        app.Logger.LogWarning("Catalog forwarding failed: {Error}", error);
});
app.Run();

internal sealed class LocalCatalogTransform(string origin) : HttpTransformer
{
    public override async ValueTask TransformRequestAsync(HttpContext context, HttpRequestMessage request,
        string destinationPrefix, CancellationToken cancellationToken)
    {
        await base.TransformRequestAsync(context, request, destinationPrefix, cancellationToken);
        request.Headers.Host = null;
        foreach (var name in request.Headers.Select(x => x.Key).Where(x => x.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)).ToArray())
            request.Headers.Remove(name);
        request.Headers.Remove("Forwarded");
        // The external origin has already been checked by LanPolicy. Adapt to the unchanged local app.
        request.Headers.Remove("Origin");
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.Remove("Referer");
    }
}
