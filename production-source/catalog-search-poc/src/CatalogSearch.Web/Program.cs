using System.Net;
using CatalogSearch.Web;
using System.Globalization;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
// This milestone is a local demo. Remote hosting requires authentication in a later slice.
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:55440");
builder.Services.AddOpenApi();
builder.WebHost.ConfigureKestrel(options=>options.Limits.MaxRequestBodySize=21_000_000);
builder.Services.AddHostedService<InboxWatcher>();
builder.Services.AddDbContext<CatalogSearchDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("CatalogSearch")));
builder.Services.AddSingleton<IEmbeddingProvider, DisabledEmbeddingProvider>();
builder.Services.AddHttpClient<IFreeTextInterpreter, LoopbackFreeTextInterpreter>(client => client.Timeout=Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(()=>new SocketsHttpHandler { AllowAutoRedirect=false,UseProxy=false,UseCookies=false,ConnectTimeout=TimeSpan.FromSeconds(5) });
builder.Services.AddScoped<IFreeTextSearchStore,DatabaseFreeTextSearchStore>();
builder.Services.AddScoped<FreeTextSearchService>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    var ip = context.Connection.RemoteIpAddress;
    if (ip is null || !IPAddress.IsLoopback(ip) || context.Request.Host.Host is not ("127.0.0.1" or "localhost"))
    { context.Response.StatusCode = 403; return; }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; frame-ancestors 'none'; base-uri 'none'";
    context.Response.Headers.CacheControl = "no-store";
    if (context.Request.Path != "/health" && context.Request.Path != "/architecture" && !app.Configuration.GetValue<bool>("CatalogDemo:Enabled"))
    { context.Response.StatusCode = 404; return; }
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        var origin=context.Request.Headers.Origin.ToString();
        if(context.Request.Headers["X-Catalog-Action"]!="local-demo" || origin!=$"http://{context.Request.Host}")
        {context.Response.StatusCode=403;return;}
    }
    try { await next(context); }
    catch (Exception ex) when (ex is Npgsql.NpgsqlException or TimeoutException or InvalidOperationException { InnerException: Npgsql.NpgsqlException })
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { error = "商品データに接続できません。確認用DBを起動して、もう一度検索してください。" });
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/architecture", () => Results.Ok(new { boundaries = new[] { "SOURCE", "EXTRACTION", "CANONICAL", "SEARCH" }, identities = new[] { "Product", "SupplierProduct", "InternalProductRecord" }, internalSources = new[] { "SHUDORAKU" } }));
app.MapGet("/api/options", async (HttpRequest request, CatalogSearchDbContext db, CancellationToken ct) => Results.Ok(new
{
    total = await db.SupplierProducts.CountAsync(ct),
    countries = await db.SupplierProducts.Select(x => x.Country).Distinct().OrderBy(x => x).ToListAsync(ct),
    regions = await db.SupplierProducts.Select(x => x.Region).Distinct().OrderBy(x => x).ToListAsync(ct),
    volumes = await db.SupplierProducts.Select(x => x.VolumeMl).Distinct().OrderBy(x => x).ToListAsync(ct),
    vintages = request.Query["vintageNotation"] == "canonical"
        ? VintageNotation.Options(await db.SupplierProducts.Select(x => x.VintageRaw).Distinct().ToListAsync(ct))
        : (await db.SupplierProducts.Select(x => x.VintageRaw).Distinct().OrderByDescending(x => x).ToListAsync(ct)).ToArray(),
    types = await db.SupplierProducts.Select(x => x.ProductType).Distinct().OrderBy(x => x).ToListAsync(ct)
}));
app.MapGet("/api/search", async (HttpRequest request, CatalogSearchDbContext db, CancellationToken ct) =>
{
    string? Value(string key) => request.Query[key].ToString() is { Length: > 0 } value ? value : null;
    decimal? max = null;
    decimal? min = null;
    int? volume = null;
    if (Value("priceMax") is { } price)
    {
        if (!decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            return Results.BadRequest(new { error = "上限価格には0以上の数値を入力してください。" });
        max = parsed;
    }
    if (Value("priceMin") is { } lower)
    {
        if (!decimal.TryParse(lower, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            return Results.BadRequest(new { error = "下限価格には0以上の数値を入力してください。" });
        min = parsed;
    }
    if (min > max) return Results.BadRequest(new { error = "価格の下限は上限以下にしてください。" });
    if (Value("volumeMl") is { } volumeText)
    {
        if (!int.TryParse(volumeText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            return Results.BadRequest(new { error = "容量には正の整数（ml）を指定してください。" });
        volume = parsed;
    }
    if (request.Query.Any(x => x.Value.ToString().Length > 200)) return Results.BadRequest(new { error = "検索条件は200文字以内で入力してください。" });
    var filter = new CatalogFilter(Value("country"), Value("region"), Value("type"), max,
        Value("available") == "true", Value("text"), 2000, min, volume, Value("vintage"));
    try
    {
        var interpretation = await new CatalogQuery(db).InterpretAsync(filter, ct);
        var rows = await new CatalogQuery(db).SearchAsync(filter, ct);
        return Results.Ok(new { rows, capped = rows.Count == 2000, conditions = interpretation.Conditions });
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/evidence/{sourceId:guid}/{sku}", async (Guid sourceId, string sku, CatalogSearchDbContext db, CancellationToken ct) =>
{
    var row = await (from e in db.SourceEvidence.AsNoTracking()
        join p in db.SupplierProducts on e.SupplierProductId equals p.Id
        join s in db.SourceDocuments on e.SourceDocumentId equals s.Id
        where e.SourceDocumentId == sourceId && p.SupplierProductCode == sku
        select new { p.SupplierProductCode, s.FileName, s.ObservedAt, e.SheetName, e.RowNumber, e.RawCellsJson, e.ExtractedRowJson }).SingleOrDefaultAsync(ct);
    return row is null ? Results.NotFound(new { error = "取込元の情報が見つかりません。" }) : Results.Ok(row);
});
app.MapFreeTextSearchEndpoints();
app.MapWorkflowEndpoints();
app.MapFolderImportEndpoints();
app.MapDateProposalEndpoints();
app.Run();
public partial class Program;
