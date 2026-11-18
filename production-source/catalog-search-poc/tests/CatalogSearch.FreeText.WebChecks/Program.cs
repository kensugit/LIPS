using CatalogSearch.Web;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
var checks=0;
void Need(bool value){if(!value)throw new Exception("Check failed");checks++;}
foreach(var (query,enabled,reason) in new[]{("wine",false,"model_disabled"),("",true,"query_empty")}){
 var store=new Store();var bridge=new Interpreter(store){Enabled=enabled};
 var r=await new FreeTextSearchService(store,bridge).SearchAsync(new(query),default);
 Need(r.Mode=="rules"&&r.FallbackReason==reason&&bridge.Calls==0&&store.Disposed);
}
{
 var store=new Store();var bridge=new Interpreter(store){Failure="bridge_timeout"};
 var r=await new FreeTextSearchService(store,bridge).SearchAsync(new("wine",new(Country:"FR")),default);
 Need(r.Mode=="rules"&&r.FallbackReason=="bridge_timeout"&&store.Ui?.Country=="FR");
}
{
 var store=new Store(){Changed=true};var bridge=new Interpreter(store);
 var r=await new FreeTextSearchService(store,bridge).SearchAsync(new("wine"),default);
 Need(r.Mode=="rules"&&r.FallbackReason=="catalog_changed"&&store.Plan==null&&r.CatalogVersion=="v2");
}
{
 var store=new Store();var bridge=new Interpreter(store);
 var r=await new FreeTextSearchService(store,bridge).SearchAsync(new("wine",new(Country:"FR",Supplier:"UI")),default);
 Need(r.Mode=="model"&&r.FallbackReason==null&&store.Plan?.Filter.Country=="FR"&&store.Plan.SupplierCode=="UI"&&store.Disposed);
}
foreach(var url in new[]{"http://example.com/api/free-text-search/interpret","http://127.0.0.1/other","http://localhost/api/free-text-search/interpret","http://127.0.0.1/api/free-text-search/interpret?q=x","https://127.0.0.1/api/free-text-search/interpret"}){
 try{LoopbackFreeTextInterpreter.ValidateEndpoint(url);throw new Exception("Unsafe URL accepted");}catch(BridgeFailure){checks++;}
}
Need(LoopbackFreeTextInterpreter.ValidateEndpoint("http://127.0.0.1:1234/api/free-text-search/interpret").IsLoopback);
foreach(var (status,body,reason) in new[]{(HttpStatusCode.Redirect,"","bridge_http_failure"),(HttpStatusCode.OK,"{}","bridge_invalid_response"),(HttpStatusCode.OK,new string('x',100001),"bridge_response_too_large")}){
 var handler=new Handler(status,body);using var client=new HttpClient(handler);
 var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"FreeTextSearch:Enabled","true"},{"FreeTextSearch:Endpoint","http://127.0.0.1:1234/api/free-text-search/interpret"}}).Build();
 var bridge=new LoopbackFreeTextInterpreter(client,cfg);
 try{await bridge.InterpretAsync(FreeTextSearchContract.CreateRequest(Guid.NewGuid(),"v1","wine",Store.Options),default);throw new Exception("Expected rejection");}catch(BridgeFailure e){Need(e.Reason==reason&&handler.Calls==1);}
}
foreach(var ui in new[]{new ExplicitSearchUi(Country:"OUTSIDE"),new ExplicitSearchUi(AvailableOnly:false),new ExplicitSearchUi(PriceMin:20,PriceMax:10)}){
 try{await new FreeTextSearchService(new Store(),new Interpreter(new Store()){Enabled=false}).SearchAsync(new("",ui),default);throw new Exception("Invalid UI accepted");}catch(ArgumentException){checks++;}
}
{
 var store=new Store();var bridge=new Interpreter(store){WrongVersion=true};
 var r=await new FreeTextSearchService(store,bridge).SearchAsync(new("wine"),default);
 Need(r.Mode=="rules"&&r.FallbackReason=="bridge_request_invalid"&&store.Plan==null);
}
foreach(var (kind,expected) in new[]{("io","bridge_unavailable"),("timeout","bridge_timeout"),("type","bridge_invalid_response")}){
 using var client=new HttpClient(new FailureHandler(kind));
 var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"FreeTextSearch:Enabled","true"},{"FreeTextSearch:Endpoint","http://127.0.0.1:1234/api/free-text-search/interpret"}}).Build();
 try{await new LoopbackFreeTextInterpreter(client,cfg).InterpretAsync(FreeTextSearchContract.CreateRequest(Guid.NewGuid(),"v1","wine",Store.Options),default);throw new Exception("Expected failure");}catch(BridgeFailure ex){Need(ex.Reason==expected);}
}
{
 var store=new Store();var logger=new CaptureLogger();
 await new FreeTextSearchService(store,new Interpreter(store),logger).SearchAsync(new("private synthetic query"),default);
 Need(logger.Messages.Count==1 && !logger.Messages[0].Contains("private synthetic query") && logger.Messages[0].Contains("InputHash") && logger.Messages[0].Contains("PromptVersion") && logger.Messages[0].Contains("Provider") && logger.Messages[0].Contains("Model"));
 var other=new Store();logger.Messages.Clear();
 await new FreeTextSearchService(other,new Interpreter(other){Enabled=false},logger).SearchAsync(new("private synthetic query"),default);
 Need(logger.Messages.Count==0);
}
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{status="passed",checks,realDatabase=false,realModel=false}));
sealed class CaptureLogger:ILogger<FreeTextSearchService>{
 public List<string> Messages {get;}=[];
 public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
 public bool IsEnabled(LogLevel level)=>true;
 public void Log<TState>(LogLevel level,EventId id,TState state,Exception? exception,Func<TState,Exception?,string> formatter)=>Messages.Add(formatter(state,exception));
}
sealed class Handler(HttpStatusCode status,string body):HttpMessageHandler{
 public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Calls++;return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(body,Encoding.UTF8,"application/json")});}
}
sealed class Interpreter(Store store):IFreeTextInterpreter{
 public bool Enabled{get;init;}=true;public int Calls;public string? Failure;public bool WrongVersion;
 public Task<FreeTextSearchInterpretation> InterpretAsync(FreeTextSearchRequest r,CancellationToken ct){
  if(store.Open)throw new Exception("Transaction held during model call");Calls++;
  if(Failure!=null)throw new BridgeFailure(Failure);
  return Task.FromResult(new FreeTextSearchInterpretation(r.Schema,r.RequestId,WrongVersion?"wrong":r.CatalogVersion,r.PromptVersion,"mock","mock",new([],"MODEL","IT",null,null,null,null,null,null,null),[]));
 }
}
sealed class Store:IFreeTextSearchStore,IFreeTextSearchRead{
 public static FreeTextSearchOptions Options=new(["FR","IT"],[],[],[],[],["UI","MODEL"]);
 public bool Open,Disposed,Changed;public ExplicitSearchUi? Ui;public StructuredSearchPlan? Plan;
 public Task<SearchCatalogSnapshot> SnapshotAsync(CancellationToken ct)=>Task.FromResult(new SearchCatalogSnapshot(Open&&Changed?"v2":"v1",Options,[]));
 public Task<IFreeTextSearchRead> BeginReadAsync(CancellationToken ct){Open=true;return Task.FromResult<IFreeTextSearchRead>(this);}
 public Task<SearchExecution> RulesAsync(ExplicitSearchUi ui,string query,CancellationToken ct){if(!Open)throw new Exception();Ui=ui;return Task.FromResult(new SearchExecution([],new{}));}
 public Task<SearchExecution> StructuredAsync(StructuredSearchPlan plan,CancellationToken ct){if(!Open)throw new Exception();Plan=plan;return Task.FromResult(new SearchExecution([],new{}));}
 public ValueTask DisposeAsync(){Open=false;Disposed=true;return ValueTask.CompletedTask;}
}

sealed class FailureHandler(string kind):HttpMessageHandler{
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
  if(kind=="io")throw new IOException();if(kind=="timeout")throw new TaskCanceledException();
  return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{}",Encoding.UTF8,"text/html")});
 }
}
