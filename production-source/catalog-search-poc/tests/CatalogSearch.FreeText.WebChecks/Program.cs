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
{
 const string query = "南アフリカで在庫が有る白ワインでシュナンブラン主体";
 var plan = new SemanticSearchPlan(new(Country:"南アフリカ",ProductType:"白",AvailableOnly:true),null,[],[],new("シュナン・ブラン","predominant"),[],false);
 Need(SemanticIntentCoverage.Check(query,new(),plan).Count==0);
 Need(SemanticIntentCoverage.Check(query,new(),plan with {Grape=new("シャルドネ","predominant")}).Contains("explicit_grape_not_adopted"));
 Need(SemanticIntentCoverage.Check(query,new(),plan with {Grape=new("シュナン・ブラン","contains")}).Contains("explicit_predominance_not_adopted"));
 Need(SemanticIntentCoverage.Check(query,new(),plan with {Filter=plan.Filter with {Country="フランス"}}).Contains("explicit_country_not_adopted"));
}
{
 var options = new SemanticSearchOptions(["フランス","南アフリカ","イタリア","アメリカ","ドイツ","Deutschland"],
     [new("ロワール","フランス"),new("Chinon","フランス"),new("Elgin","南アフリカ"),new("エルギン","アメリカ"),new("Toscana","イタリア"),new("カンパーニア","イタリア")],
     ["赤","白"],[],[],[],GrapeRegistry.Names);
 var loire = JapaneseWineDictionary.Interpret("ロワールのカベルネフラン主体の赤ワイン",options);
 Need(loire is {PlaceLabel:"フランスのロワール地方",ProductType:"赤",Grape:{Name:"カベルネ・フラン",Mode:"predominant"}} &&
     loire.Regions.SequenceEqual(["ロワール","Chinon"]));
 Need(JapaneseWineDictionary.Interpret("南アフリカのエルギン",options)?.Regions.SequenceEqual(["Elgin"])==true);
 Need(JapaneseWineDictionary.Interpret("エルギンの白ワイン",options)?.ProductType=="白");
 Need(JapaneseWineDictionary.Interpret("トスカーナ",options)?.Regions.SequenceEqual(["Toscana"])==true);
 Need(JapaneseWineDictionary.Interpret("ロワールのカベルネフラン主体の赤ワインで3000円以下",options) is null);
 Need(JapaneseWineDictionary.Interpret("南アフリカのロワール",options) is null);
 Need(JapaneseWineDictionary.Interpret("エルギン",options with {Regions=[]})?.Regions.Length==0);
 var sweet = JapaneseWineDictionary.Interpret("ドイツの甘口白ワイン",options);
 Need(sweet is {Taste:"甘口",ProductType:"白"} && sweet.Countries.SequenceEqual(["ドイツ","Deutschland"]));
 Need(JapaneseWineDictionary.Interpret("柑橘系の風味の白ワイン",options)?.Taste=="柑橘系");
 Need(JapaneseWineDictionary.Interpret("ドイツの甘口白ワイン",options with {Countries=["フランス"]}) is null);
 Need(JapaneseWineDictionary.Interpret("カンパーニアの赤ワイン",options) is {PlaceLabel:"イタリアのカンパーニア",ProductType:"赤"});
 Need(GrapeRegistry.MentionedNames("ロワールの Cabernet Franc を使用").SequenceEqual(["カベルネ・フラン"]));
 Need(GrapeRegistry.Resolve("Tempranillo") is not null && GrapeRegistry.Names.Count>250);
 Need(JapaneseWineDictionary.Revision.Length==64);
}
{
 var options = new SemanticSearchOptions(["フランス"], [], ["赤","白","白泡"], [750], ["NV","2022"], [], GrapeRegistry.Names);
 var snapshot = new SemanticCatalogSnapshot("test-vocabulary", options);
 const string sparklingQuery = "在庫が有るノンヴィンテージの発泡性ワイン";
 var plan = SemanticIntentCoverage.TryRulesFallback(sparklingQuery,new(),snapshot);
 Need(plan is {Sparkling:true,Filter:{AvailableOnly:true,VintageRaw:"NV"}});
 var explicitUi = SemanticIntentCoverage.TryRulesFallback(sparklingQuery,new(ProductType:"白",Vintage:"2022"),snapshot);
 Need(explicitUi is {Sparkling:false,Filter:{ProductType:"白",VintageRaw:"2022"}});
 Need(SemanticIntentCoverage.TryRulesFallback("シャンパン",new(),snapshot) is null);
 Need(SemanticIntentCoverage.TryRulesFallback("発泡性ワインまたは白ワイン",new(),snapshot) is null);
 Need(SemanticIntentCoverage.TryRulesFallback("1500mlのホワイトワイン",new(),snapshot) is null);
 var local = new VocabularyStore(snapshot);
 var interpreter = new UnusedSemanticInterpreter();
 var result = await new SemanticSearchService(local,interpreter).SearchAsync(new(sparklingQuery),default);
 Need(result is {Mode:"rules",FallbackReason:"dictionary_match"} && interpreter.Calls==0);
 Need(local.LastPlan is {Sparkling:true,Filter:{AvailableOnly:true,VintageRaw:"NV"}});
 var sweetSparkling = await new SemanticSearchService(local,interpreter).SearchAsync(new("辛口のスパークリングワイン"),default);
 Need(sweetSparkling.FallbackReason=="dictionary_match" && local.LastPlan is {Sparkling:true,Taste:"辛口"} && interpreter.Calls==0);
}
{
 var plan = NaturalSearch.Parse(new CatalogFilter(Text:"フランスの在庫が有る赤ワインで上代1万円以内"));
 Need(plan.Filter is {Country:"フランス", ProductType:"赤", PriceMax:10000, AvailableOnly:true} &&
      string.IsNullOrWhiteSpace(plan.Filter.Text));
 var rightBank = NaturalSearch.Parse(new CatalogFilter(Text:"ボルドー右岸で上代10000円以内で在庫が有る赤ワイン"));
 Need(rightBank.Filter is {PriceMax:10000, AvailableOnly:true, ProductType:"赤"} &&
      rightBank.Filter.Text?.Contains("右岸", StringComparison.Ordinal)==true &&
      !rightBank.Filter.Text.Contains("上代", StringComparison.Ordinal));
}
{
 var options = new SemanticSearchOptions(["チリ", "フランス", "イタリア"],
     [new("サンテミリオン","フランス"), new("フロンサック","フランス"), new("シャンパーニュ","フランス")],
     ["白","赤","白泡"], [750,1500], ["2020","2021"], [], GrapeRegistry.Names);
 var store = new VocabularyStore(new("composition-fixture", options) {
     RegionPairs = [..options.Regions,new("ポムロール","フランス"),new("ポムロール","チリ")] });
 var interpreter = new UnusedSemanticInterpreter();
 var service = new SemanticSearchService(store, interpreter);
 await service.SearchAsync(new("ボルドー右岸で上代10000円以内で在庫が有る赤ワイン"),default);
 Need(store.LastPlan is { Filter: { Country: "フランス", ProductType: "赤", AvailableOnly: true, PriceMax: 10000 } }
     && store.LastPlan.Regions.Contains("ポムロール") && store.LastPlan.Regions.Count == 3);
 await service.SearchAsync(new("イタリアでサンジョヴェーゼ主体の在庫あり、3,000円以下"), default);
 Need(store.LastPlan is { Filter: {Country:"イタリア", AvailableOnly:true, PriceMax:3000}, Grape:{Name:"サンジョベーゼ",Mode:"predominant"} });
 await service.SearchAsync(new("ＪＡＮ ００００００００００００５、2020年750ml"), default);
 Need(store.LastPlan is { Filter: {VintageRaw:"2020",VolumeMl:750}, Jan:"0000000000005" });
 await service.SearchAsync(new("フランスの赤を3,000円以下で（画面では白を指定）", new(ProductType:"白")), default);
 Need(store.LastPlan is {Filter:{Country:"フランス",ProductType:"白",PriceMax:3000}});
 Need(interpreter.Calls==0);
 Need(NaturalSearch.Parse(new(Text:"SKU-A2020年")).Filter.VintageRaw is null);
 Need(NaturalSearch.Parse(new(Text:"2020年代")).Filter.VintageRaw is null);
}
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{status="passed",checks,realDatabase=false,realModel=false}));
sealed class CaptureLogger:ILogger<FreeTextSearchService>{
 public List<string> Messages {get;}=[];
 public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
 public bool IsEnabled(LogLevel level)=>true;
 public void Log<TState>(LogLevel level,EventId id,TState state,Exception? exception,Func<TState,Exception?,string> formatter)=>Messages.Add(formatter(state,exception));
}
sealed class VocabularyStore(SemanticCatalogSnapshot snapshot):ISemanticSearchStore, ISemanticSearchRead
{
 public SemanticSearchPlan? LastPlan {get;private set;}
 public Task<SemanticCatalogSnapshot> SnapshotAsync(CancellationToken ct) => Task.FromResult(snapshot);
 public Task<ISemanticSearchRead> BeginReadAsync(CancellationToken ct) => Task.FromResult<ISemanticSearchRead>(this);
 public Task<SemanticCatalogSearchResult> SearchAsync(SemanticSearchPlan plan,CancellationToken ct)
 {
  LastPlan=plan;
  return Task.FromResult(new SemanticCatalogSearchResult([],0,0,true));
 }
 public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
sealed class UnusedSemanticInterpreter:ISemanticInterpreter
{
 public bool Enabled => true;
 public int Calls {get;private set;}
 public Task<SemanticSearchInterpretation> InterpretAsync(SemanticSearchRequest request,CancellationToken ct)
 {
  Calls++;
  throw new InvalidOperationException("Dictionary query should not call the interpreter");
 }
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
