using System.Data;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace CatalogSearch.Web;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExplicitSearchUi(string? Country=null,string? Region=null,string? ProductType=null,int? VolumeMl=null,string? Vintage=null,decimal? PriceMin=null,decimal? PriceMax=null,bool? AvailableOnly=null,string? Supplier=null,int Limit=2000)
{
 public CatalogFilter Filter => new(Country,Region,ProductType,PriceMax,AvailableOnly==true,null,Limit,PriceMin,VolumeMl,Vintage);
 public void Validate()
 {
  if(new[]{Country,Region,ProductType,Vintage,Supplier}.Any(x=>x is not null&&(string.IsNullOrWhiteSpace(x)||x.Length>200)) || Limit is <1 or >2000 || PriceMin<0 || PriceMax<0 || PriceMin>PriceMax || VolumeMl<=0 || AvailableOnly==false)throw new ArgumentException("invalid_explicit_ui");
 }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FreeTextUiRequest(string Query,ExplicitSearchUi? ExplicitUi=null);
public sealed record SearchExecution(IReadOnlyList<CatalogResult> Rows,object Conditions);
public sealed record FreeTextUiResult(IReadOnlyList<CatalogResult> Rows,bool Capped,string CatalogVersion,string Mode,string? FallbackReason,object Conditions,ExplicitSearchUi ExplicitUi);
public interface IFreeTextSearchRead : IAsyncDisposable
{
 Task<SearchCatalogSnapshot> SnapshotAsync(CancellationToken ct);
 Task<SearchExecution> RulesAsync(ExplicitSearchUi ui,string query,CancellationToken ct);
 Task<SearchExecution> StructuredAsync(StructuredSearchPlan plan,CancellationToken ct);
}
public interface IFreeTextSearchStore
{
 Task<SearchCatalogSnapshot> SnapshotAsync(CancellationToken ct);
 Task<IFreeTextSearchRead> BeginReadAsync(CancellationToken ct);
}
public sealed class DatabaseFreeTextSearchStore(CatalogSearchDbContext db) : IFreeTextSearchStore
{
 public Task<SearchCatalogSnapshot> SnapshotAsync(CancellationToken ct)=>new CatalogSearchSnapshotProvider(db).CaptureAsync(ct);
 public async Task<IFreeTextSearchRead> BeginReadAsync(CancellationToken ct)
 {
  var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
  try {await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY",ct);return new Read(db,tx);}catch{await tx.DisposeAsync();throw;}
 }
 sealed class Read(CatalogSearchDbContext db,Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx):IFreeTextSearchRead
 {
  public Task<SearchCatalogSnapshot> SnapshotAsync(CancellationToken ct)=>new CatalogSearchSnapshotProvider(db).CaptureAsync(ct);
  public async Task<SearchExecution> RulesAsync(ExplicitSearchUi ui,string query,CancellationToken ct)
  {
   var catalog=new CatalogQuery(db);var parsed=await catalog.InterpretAsync(new CatalogFilter(Text:query,Limit:ui.Limit),ct);
   var merged=CatalogQuery.MergeRulesWithUi(parsed,ui.Filter);
   return new(await catalog.SearchRulesWithUiAsync(ui.Filter,ui.Supplier,query,ct),new {filter=merged.Filter,supplier=ui.Supplier,ruleSupplierId=ui.Supplier is null?merged.SupplierId:null,champagne=merged.Champagne,sparkling=merged.Sparkling,explicitUi=ui});
  }
  public async Task<SearchExecution> StructuredAsync(StructuredSearchPlan plan,CancellationToken ct)=>new(await new CatalogQuery(db).SearchStructuredAsync(plan,ct),new {filter=plan.Filter,supplier=plan.SupplierCode,keywords=plan.Keywords});
  public ValueTask DisposeAsync()=>tx.DisposeAsync();
 }
}
public sealed class BridgeFailure(string reason):Exception(reason) {public string Reason {get;}=reason;}
public interface IFreeTextInterpreter {bool Enabled{get;} Task<FreeTextSearchInterpretation> InterpretAsync(FreeTextSearchRequest request,CancellationToken ct);}
public sealed class LoopbackFreeTextInterpreter(HttpClient client,IConfiguration configuration):IFreeTextInterpreter
{
 public bool Enabled=>bool.TryParse(configuration["FreeTextSearch:Enabled"],out var enabled)&&enabled;
 public static Uri ValidateEndpoint(string? text)
 {
  if(!Uri.TryCreate(text,UriKind.Absolute,out var uri)||uri.Scheme!="http"||!IPAddress.TryParse(uri.Host.Trim('[',']'),out var ip)||!IPAddress.IsLoopback(ip)||uri.AbsolutePath!="/api/free-text-search/interpret"||uri.Query.Length!=0||uri.Fragment.Length!=0||uri.UserInfo.Length!=0)throw new BridgeFailure("bridge_configuration_invalid");
  return uri;
 }
 public async Task<FreeTextSearchInterpretation> InterpretAsync(FreeTextSearchRequest request,CancellationToken ct)
 {
  if(!Enabled)throw new BridgeFailure("model_disabled");
  var endpoint=ValidateEndpoint(configuration["FreeTextSearch:Endpoint"]);
  var setting=configuration["FreeTextSearch:TimeoutSeconds"];
  var seconds=10;
  if(setting is not null&&!int.TryParse(setting,out seconds))throw new BridgeFailure("bridge_configuration_invalid");
  if(seconds is <1 or >30)throw new BridgeFailure("bridge_configuration_invalid");
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
  try
  {
   using var message=new HttpRequestMessage(HttpMethod.Post,endpoint){Content=new StringContent(JsonSerializer.Serialize(request,FreeTextSearchContract.JsonOptions),Encoding.UTF8,"application/json")};
   using var response=await client.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
   if(!response.IsSuccessStatusCode)throw new BridgeFailure("bridge_http_failure");
   if(response.Content.Headers.ContentType?.MediaType!="application/json")throw new BridgeFailure("bridge_invalid_response");
   if(response.Content.Headers.ContentLength>FreeTextSearchContract.MaxBodyBytes)throw new BridgeFailure("bridge_response_too_large");
   await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var body=new MemoryStream();var buffer=new byte[4096];
   while(true){var count=await stream.ReadAsync(buffer,timeout.Token);if(count==0)break;if(body.Length+count>FreeTextSearchContract.MaxBodyBytes)throw new BridgeFailure("bridge_response_too_large");body.Write(buffer,0,count);}
   return FreeTextSearchContract.ParseResponse(request,new UTF8Encoding(false,true).GetString(body.ToArray()));
  }
  catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new BridgeFailure("bridge_timeout");}
  catch(HttpRequestException){throw new BridgeFailure("bridge_unavailable");}
  catch(IOException){throw new BridgeFailure("bridge_unavailable");}
  catch(Exception e)when(e is ArgumentException or JsonException or DecoderFallbackException){throw new BridgeFailure("bridge_invalid_response");}
 }
}
public sealed class FreeTextSearchService(IFreeTextSearchStore store,IFreeTextInterpreter interpreter,ILogger<FreeTextSearchService>? logger=null)
{
 static void ValidateOptions(ExplicitSearchUi ui,FreeTextSearchOptions o)
 {
  if((ui.Country is not null&&!o.Countries.Contains(ui.Country))||(ui.Region is not null&&!o.Regions.Contains(ui.Region))||(ui.ProductType is not null&&!o.ProductTypes.Contains(ui.ProductType))||(ui.Vintage is not null&&!o.Vintages.Contains(ui.Vintage))||(ui.Supplier is not null&&!o.Suppliers.Contains(ui.Supplier))||(ui.VolumeMl is not null&&!o.VolumesMl.Contains(ui.VolumeMl.Value)))throw new ArgumentException("explicit_ui_outside_catalog");
 }
 public async Task<FreeTextUiResult> SearchAsync(FreeTextUiRequest input,CancellationToken ct)
 {
  if(input.Query is null||input.Query.Length>200)throw new ArgumentException("invalid_query");
  var ui=input.ExplicitUi??new();ui.Validate();
  var initial=await store.SnapshotAsync(ct);FreeTextSearchInterpretation? interpretation=null;string? reason=null;
  if(string.IsNullOrWhiteSpace(input.Query))reason="query_empty";
  else if(!interpreter.Enabled)reason="model_disabled";
  else
  {
   try{var request=FreeTextSearchContract.CreateRequest(Guid.NewGuid(),initial.Version,input.Query,initial.Options);
    var response=await interpreter.InterpretAsync(request,ct);
    interpretation=FreeTextSearchContract.ParseResponse(request,JsonSerializer.Serialize(response,FreeTextSearchContract.JsonOptions));}
   catch(BridgeFailure ex){reason=ex.Reason;}
   catch(ArgumentException){reason="bridge_request_invalid";}
   catch(JsonException){reason="bridge_invalid_response";}
  }
  await using var read=await store.BeginReadAsync(ct);var current=await read.SnapshotAsync(ct);
  ValidateOptions(ui,current.Options);
  if(initial.Version!=current.Version){reason="catalog_changed";interpretation=null;}
  SearchExecution result;var mode="rules";
  if(interpretation is not null)
  {
   StructuredSearchPlan? plan=null;try{plan=FreeTextSearchContract.Merge(ui.Filter,ui.Supplier,interpretation);}catch(ArgumentException){reason="merged_conditions_invalid";}
   if(plan is not null){result=await read.StructuredAsync(plan,ct);mode="model";}else result=await read.RulesAsync(ui,input.Query,ct);
  }
  else result=await read.RulesAsync(ui,input.Query,ct);
  if(mode=="model" && interpretation is not null)
   logger?.LogInformation("Accepted free-text interpretation metadata {Metadata}",JsonSerializer.Serialize(new {
    interpretation.RequestId,interpretation.Provider,interpretation.Model,interpretation.PromptVersion,
    interpretation.CatalogVersion,InputHashKind="UTF8-query/v1",
    InputHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Query))).ToLowerInvariant()
   }));
  return new(result.Rows,result.Rows.Count==ui.Limit,current.Version,mode,reason,result.Conditions,ui);
 }
}
