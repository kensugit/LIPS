using System.Text.Json;
using CatalogSearch.Application;
namespace CatalogSearch.Web;
public static class FreeTextSearchEndpoints
{
 public static void MapFreeTextSearchEndpoints(this WebApplication app)
 {
  app.MapGet("/api/free-text-search/options",async(IFreeTextSearchStore store,IFreeTextInterpreter interpreter,ISemanticInterpreter semantic,IConfiguration config,CancellationToken ct)=>
  {
   var snapshot=await store.SnapshotAsync(ct);
   var v2=bool.TryParse(config["FreeTextSearch:SemanticV2Enabled"],out var enabled)&&enabled;
   return Results.Ok(new {catalogVersion=snapshot.Version,options=snapshot.Options,suppliers=snapshot.Suppliers,modelEnabled=v2?semantic.Enabled:interpreter.Enabled,semanticV2Enabled=v2});
  });
  app.MapPost("/api/free-text-search/search",async(HttpRequest request,FreeTextSearchService service,SemanticSearchService semantic,IConfiguration config,CancellationToken ct)=>
  {
   try
   {
    if(request.ContentType?.Split(';')[0].Trim()!="application/json")return Results.BadRequest(new {error="json_required"});
    if(request.ContentLength>16384)return Results.BadRequest(new {error="request_too_large"});
    using var bytes=new MemoryStream();var buffer=new byte[4096];
    while(true){var count=await request.Body.ReadAsync(buffer,ct);if(count==0)break;if(bytes.Length+count>16384)return Results.BadRequest(new {error="request_too_large"});bytes.Write(buffer,0,count);}
    using var doc=JsonDocument.Parse(bytes.ToArray(),new JsonDocumentOptions {MaxDepth=8});CheckDuplicates(doc.RootElement);
    var input=doc.RootElement.Deserialize<FreeTextUiRequest>(FreeTextSearchContract.JsonOptions)??throw new ArgumentException("request_required");
    if(!string.IsNullOrWhiteSpace(input.Query)&&bool.TryParse(config["FreeTextSearch:SemanticV2Enabled"],out var v2)&&v2)
     return Results.Ok(await semantic.SearchAsync(input,ct));
    return Results.Ok(await service.SearchAsync(input,ct));
   }
   catch(Exception ex)when(ex is ArgumentException or JsonException){return Results.BadRequest(new {error="invalid_search_request"});}
  });
 }
 static void CheckDuplicates(JsonElement value)
 {
  if(value.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var p in value.EnumerateObject()){if(!names.Add(p.Name))throw new ArgumentException("duplicate_member");CheckDuplicates(p.Value);}}
  else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())CheckDuplicates(item);
 }
}
