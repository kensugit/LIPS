using System.Globalization;
using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: <xlsx> [--persist --observed-at YYYY-MM-DD] [--jsonl path] | migrate | schema-check | search [--country value --region value --type value --price-min amount --price-max amount --volume-ml ml --vintage year-or-NV --available --text value]");
    return 2;
}
string? Option(string name) { var i = Array.IndexOf(args, name); if (i < 0) return null; if (i + 1 >= args.Length || args[i + 1].StartsWith("--")) throw new ArgumentException($"Missing value: {name}"); return args[i + 1]; }
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
try
{
    if (args[0] == "verify-date-proposal")
    {
        if(args.Length!=3)throw new ArgumentException("verify-date-proposal request.json response.json");
        if(new FileInfo(args[1]).Length>50000 || new FileInfo(args[2]).Length>50000)throw new ArgumentException("Proposal file exceeds limit.");
        var request=JsonSerializer.Deserialize<DateProposalRequest>(await File.ReadAllTextAsync(args[1]),new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new ArgumentException("Missing request.");
        var response=IntakeDateProposal.ReadResponse(request,await File.ReadAllTextAsync(args[2]));
        Console.WriteLine(JsonSerializer.Serialize(new{valid=true,requiresHumanConfirmation=true,response.RequestId,response.Outcome,candidates=response.Candidates.Count}));
        return 0;
    }
    var needsDb = args[0] is "migrate" or "schema-check" or "search" || args.Contains("--persist");
    var connection = Environment.GetEnvironmentVariable("CATALOG_CONNECTION");
    if (needsDb && string.IsNullOrWhiteSpace(connection)) throw new ArgumentException("Set CATALOG_CONNECTION for the independent catalog database.");
    await using var db = needsDb ? new CatalogSearchDbContext(new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(connection).Options) : null;
    if (args[0] == "schema-check")
    {
        var expected = db!.Database.GetMigrations().ToArray();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var pending = expected.Except(applied).Count();
        var unknown = applied.Except(expected).Count();
        Console.WriteLine(JsonSerializer.Serialize(new { ready = pending == 0 && unknown == 0, pending, unknown }));
        return pending == 0 && unknown == 0 ? 0 : 3;
    }
    if (args[0] == "migrate") { await db!.Database.MigrateAsync(); Console.WriteLine("Catalog migrations applied."); return 0; }
    if (args[0] == "search")
    {
        var max = Option("--price-max");
        var min = Option("--price-min");
        var volume = Option("--volume-ml");
        var rows = await new CatalogQuery(db!).SearchAsync(new CatalogFilter(Option("--country"), Option("--region"), Option("--type"),
            max is null ? null : decimal.Parse(max, CultureInfo.InvariantCulture), args.Contains("--available"), Option("--text"), 2000, min is null ? null : decimal.Parse(min, CultureInfo.InvariantCulture),
            volume is null ? null : int.Parse(volume, CultureInfo.InvariantCulture), Option("--vintage")));
        Console.WriteLine(JsonSerializer.Serialize(rows, jsonOptions)); return 0;
    }
    var parser = new WineToStyleInventoryParser();
    if (args.Contains("--persist"))
    {
        var date = Option("--observed-at") ?? throw new ArgumentException("--observed-at YYYY-MM-DD is required for persistence.");
        var observed = new DateTimeOffset(DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), TimeSpan.Zero);
        var result = await new ImportService(new FileWorkbookSource(), parser, new PostgresInventoryStore(db!)).ImportAsync(args[0], observed);
        Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions)); return 0;
    }
    var parsed = await parser.ParseAsync(args[0], CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(new { products = parsed.Count,
        inventory = parsed.GroupBy(x => x.Inventory.Status).ToDictionary(x => x.Key.ToString(), x => x.Count()),
        numericInventory = parsed.Count(x => x.Inventory.Quantity.HasValue), openPrice = parsed.Count(x => x.ReferenceRetailPrice is null),
        multipleJan = parsed.Count(x => x.JanCodes.Count > 1), missingJan = parsed.Count(x => x.JanCodes.Count == 0) }, jsonOptions));
    var output = Option("--jsonl");
    if (output is not null)
    {
        if (Path.GetFullPath(output).Equals(Path.GetFullPath(args[0]), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output cannot overwrite source.");
        await using var writer = new StreamWriter(new FileStream(output, FileMode.CreateNew));
        foreach (var row in parsed) await writer.WriteLineAsync(JsonSerializer.Serialize(row));
    }
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException)
{
    Console.Error.WriteLine($"Import rejected: {ex.GetType().Name}. Check file layout and command arguments."); return 2;
}
catch (Exception)
{
    Console.Error.WriteLine("Catalog operation failed; transaction was not committed. Check database availability and migrations."); return 1;
}
