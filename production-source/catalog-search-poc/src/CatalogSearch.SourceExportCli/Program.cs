using System.Security.Cryptography;
using System.Text.Json;
using CatalogSearch.SourceExportCli;

// Execution/credential custody remains held. This entry only validates a pinned selector.
if(args.Length!=3 || args[0]!="--validate-only") { Console.WriteLine("EXECUTION_NOT_AVAILABLE_VALIDATE_ONLY");return 2; }
try
{
    using var f=new FileStream(args[1],FileMode.Open,FileAccess.Read,FileShare.Read);
    if(f.Length is <=0 or >4096) throw new InvalidDataException();
    var bytes=new byte[(int)f.Length];f.ReadExactly(bytes);
    if(!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)),args[2],StringComparison.Ordinal)) throw new InvalidDataException();
    using var doc=JsonDocument.Parse(bytes);
    if(doc.RootElement.ValueKind!=JsonValueKind.Object || doc.RootElement.EnumerateObject().Count()!=4 || doc.RootElement.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=4 ||
       !doc.RootElement.EnumerateObject().Select(x=>x.Name).ToHashSet().SetEquals(new[]{"ProductId","SourceId","SupplierCode","Sku"})) throw new InvalidDataException();
    SourceExport.Validate(JsonSerializer.Deserialize<Selector>(bytes) ?? throw new InvalidDataException());
    Console.WriteLine("SELECTOR_VALIDATED_DATABASE_NOT_CONNECTED");return 0;
}
catch { Console.WriteLine("INPUT_REJECTED_NO_DATABASE_CONNECTION");return 3; }
