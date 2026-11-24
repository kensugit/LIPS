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
 await service.SearchAsync(new("イタリアの赤"), default);
 Need(store.LastPlan is {Filter:{Country:"イタリア",ProductType:"赤"},Grape:null});
 Need(interpreter.Calls==0);
 Need(NaturalSearch.Parse(new(Text:"SKU-A2020年")).Filter.VintageRaw is null);
 Need(NaturalSearch.Parse(new(Text:"2020年代")).Filter.VintageRaw is null);
}
{
 var options = new SemanticSearchOptions(["フランス","イタリア"],
     [new("サンテミリオン","フランス"),new("ポムロール","フランス"),new("フロンサック","フランス")],
     ["白","赤"],[],[],[],GrapeRegistry.Names);
 var local = new VocabularyStore(new("synonym-fixture",options));
 var unused = new UnusedSemanticInterpreter();
 var service = new SemanticSearchService(local,unused);
 foreach(var alias in new[]{"ピノ・グリ","ピノグリ","Pinot Gris","ピノ・グリージョ","ピノグリージョ","Pinot Grigio"}) {
  await service.SearchAsync(new(alias+"を含む白ワイン"),default);
  Need(local.LastPlan is {Filter:{ProductType:"白"},Grape:{Name:"ピノ・グリ",Mode:"contains"}});
 }
 foreach(var query in new[]{"ピノグリもしくはピノグリージョを含む白ワイン",
     "ピノグリージョまたはピノグリを含む白ワイン", "Pinot Gris or Pinot Grigio を含む白ワイン",
     "ピノ・グリあるいはピノ・グリージョを含む白ワイン"}) {
  var result=await service.SearchAsync(new(query),default);
  Need(result.Mode=="rules" && local.LastPlan is {Filter:{ProductType:"白"},Grape:{Name:"ピノ・グリ",Mode:"contains"},Keywords.Count:0});
  Need(SemanticGrapeMatcher.Match("Pinot Grigio 20%",local.LastPlan!.Grape!).Included);
  Need(!SemanticGrapeMatcher.Match(null,local.LastPlan.Grape!).Included);
 }
 await service.SearchAsync(new("ピノグリもしくはピノグリージョ主体の白ワイン"),default);
 Need(local.LastPlan is {Grape:{Name:"ピノ・グリ",Mode:"predominant"}});
 Need(!SemanticGrapeMatcher.Match("Pinot Grigio 50%",local.LastPlan!.Grape!).Included);
 Need(SemanticGrapeMatcher.Match("Pinot Grigio 51%",local.LastPlan.Grape!).Included);
 await service.SearchAsync(new("イタリアでピノグリもしくはピノグリージョを含む白ワインで上代3000円以内で在庫あり"),default);
 Need(local.LastPlan is {Filter:{Country:"イタリア",ProductType:"白",PriceMax:3000,AvailableOnly:true},Grape:{Mode:"contains"}});
 await service.SearchAsync(new("ピノグリもしくはピノグリージョを含む白ワイン",new(ProductType:"赤",PriceMax:2000)),default);
 Need(local.LastPlan is {Filter:{ProductType:"赤",PriceMax:2000},Grape:{Name:"ピノ・グリ",Mode:"contains"}});
 foreach(var query in new[]{"ピノグリまたはシャルドネを含む白ワイン","ピノグリとシャルドネを含む白ワイン",
     "ピノグリかつシャルドネを含む白ワイン","ピノグリを含む白ワインまたは在庫あり",
     "フランスまたはイタリアのピノグリを含む白ワイン","ピノグリを含む白ワインで3000円以下または在庫あり",
     "ピノグリージョを含まない白ワイン","ピノグリ以外の白ワイン","ピノグリ不使用の白ワイン",
     "ピノグリもしくはピノグリージョまたはピノグリを含む白ワイン"}) {
  var before=local.SearchCalls;var result=await service.SearchAsync(new(query),default);
  Need(result.Mode=="needsClarification" && local.SearchCalls==before);
 }
 Need(unused.Calls==0);
 foreach(var (query,known) in new[]{("ボルドー右岸のピノグリージョでおすすめの白ワイン",true),
     ("ボルドー右岸で珍しい品種の白ワイン",false),("珍しい品種の白ワイン",false)}) {
  var capture=new GrapeScopeInterpreter();var scoped=new VocabularyStore(new("scoped-synonym",options));
  var result=await new SemanticSearchService(scoped,capture).SearchAsync(new(query),default);
  Need(capture.Request is not null && capture.Request.Options.Grapes.SequenceEqual(known?new[]{"ピノ・グリ"}:Array.Empty<string>()));
  Need(known?result.Mode=="model" && scoped.LastPlan!.Regions.Count==3:result.Mode=="needsClarification" && scoped.SearchCalls==0);
 }
}
{
 var options=new SemanticSearchOptions(["フランス"],[],["白","赤"],[],[],[],GrapeRegistry.Names);
 var local=new VocabularyStore(new("half-sweet-fixture",options));var unused=new UnusedSemanticInterpreter();
 foreach(var alias in new[]{"半甘口","やや甘口"}) {
  var result=await new SemanticSearchService(local,unused).SearchAsync(new(alias+"の白ワイン"),default);
  Need(result.Mode=="rules" && local.LastPlan is {Taste:"やや甘口",Filter:{ProductType:"白"}});
  Need(WineTasteEvidence.Matches("{\"Sweetness\":\"半甘口\"}",local.LastPlan!.Taste!));
  Need(!WineTasteEvidence.Matches("{\"Sweetness\":\"甘口\"}",local.LastPlan.Taste!));
 }
 Need(JapaneseWineDictionary.Interpret("甘い香りの白ワイン",options) is null && unused.Calls==0);
 var legacy=Path.Combine(Path.GetTempPath(),"lips-legacy-config");var extra=Path.Combine(Path.GetTempPath(),"lips-extra-config");
 var values=new Dictionary<string,string?>{{"ImporterEnrichment:Root",legacy},{"ImporterEnrichment:ExtractorVersion","old-v1"},
  {"ImporterEnrichment:SupplierRoutes:TOYOTSU:Root",extra},{"ImporterEnrichment:SupplierRoutes:TOYOTSU:ExtractorVersion","toyotsu-v1"}};
 var config=new ConfigurationBuilder().AddInMemoryCollection(values).Build();var route=ImporterEnrichmentReaderConfiguration.Create(config);
 Need(route.Reader is RoutedImporterEnrichmentReader && route.LegacyVersion=="old-v1");
 values["ImporterEnrichment:SupplierRoutes:TOYOTSU:Root"]=legacy;
 try{ImporterEnrichmentReaderConfiguration.Create(new ConfigurationBuilder().AddInMemoryCollection(values).Build());throw new InvalidOperationException("same root accepted");}catch(ArgumentException){checks++;}
}
{
 // Moving reviewed synonym data into one shared JSON must not change right-bank membership, grade or order.
 using var expected=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures","rightbank-scope-baseline.json")));
 var e=expected.RootElement;
 var baseline=new {Version=e.GetProperty("Version").GetString(),Sources=e.GetProperty("Sources").EnumerateArray().Select(x=>x.GetString()).ToArray(),
  Members=e.GetProperty("Members").EnumerateArray().Select(g=>g.EnumerateArray().Select(x=>x.GetString()).ToArray()).ToArray(),
  Outside=e.GetProperty("Outside").EnumerateArray().Select(x=>x.GetString()).ToArray()};
 Need(System.Text.Json.JsonSerializer.Serialize(ReviewedRightBankScope.Definition)==System.Text.Json.JsonSerializer.Serialize(baseline));
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
 public int SearchCalls {get;private set;}
 public Task<SemanticCatalogSnapshot> SnapshotAsync(CancellationToken ct) => Task.FromResult(snapshot);
 public Task<ISemanticSearchRead> BeginReadAsync(CancellationToken ct) => Task.FromResult<ISemanticSearchRead>(this);
 public Task<SemanticCatalogSearchResult> SearchAsync(SemanticSearchPlan plan,CancellationToken ct)
 {
  SearchCalls++;LastPlan=plan;
  return Task.FromResult(new SemanticCatalogSearchResult([],0,0,true));
 }
 public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
sealed class GrapeScopeInterpreter:ISemanticInterpreter
{
 public bool Enabled=>true;
 public SemanticSearchRequest? Request {get;private set;}
 public Task<SemanticSearchInterpretation> InterpretAsync(SemanticSearchRequest request,CancellationToken ct) {
  Request=request;
  return Task.FromResult(new SemanticSearchInterpretation(SemanticSearchContract.Schema,request.RequestId,request.CatalogVersion,
   request.PromptVersion,"synthetic","stub",new([],null,"フランス",request.Options.Regions.Select(r=>r.Name).ToArray(),"白",null,null,null,null,null,new("ピノ・グリ","contains")),[]));
 }
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
