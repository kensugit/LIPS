using System.Security.Cryptography;
using System.Text.Json;
using CatalogSearch.Application;

if(args.Length!=2||args[0]!="--observed-pages-dir")throw new ArgumentException("Use --observed-pages-dir <fixed local observation directory>; no HTTP is performed.");
var adapter=new ToyotsuProductPageIdentity("TOYOTSU",new Dictionary<string,string>{{"クーリー デュテイユ","クーリー デュテイユ"}});
var results=new List<object>();
foreach(var (sku,product,year,pin)in new[]{
    ("FE4113","シノン レ グラヴィエール","2023","5459cb0a107d6dd0379a66013f3660f0ecfa6553d36ff083560dcbe448c2e868"),
    ("FE4218","シノン クロ ド レコー","2018","043f07b483f2bf4b13df18e4bfe1b31eb6f1e30b5bc05475a50f52033dbf6c8f")})
{
    var bytes=File.ReadAllBytes(Path.Combine(Path.GetFullPath(args[1]),sku+".html"));
    if(Convert.ToHexStringLower(SHA256.HashData(bytes))!=pin)throw new InvalidDataException("OBSERVED_PAGE_PIN_CHANGED "+sku);
    var id=new ImporterProductIdentity("TOYOTSU",sku,"クーリー デュテイユ",product,year,750,"赤");
    var accepted=adapter.TryExtract(id,"https://toyotsu-wine.com/shop/g/g"+sku+"/",bytes,out var evidence);
    if(!accepted||evidence.Count!=1||!adapter.Verify(id,evidence,"https://toyotsu-wine.com/shop/g/g"+sku+"/",bytes)||
        !SemanticGrapeMatcher.Match(evidence[0].SourceExcerpt,new("カベルネ・フラン","predominant")).Included)throw new InvalidDataException("OBSERVED_PAGE_EXTRACTION_FAILED "+sku);
    results.Add(new{sku,bytes=bytes.Length,sha256=pin,accepted,identityType="configured TOYOTSU; catalogue facts; not production sourceFP",evidence});
}
Console.WriteLine(JsonSerializer.Serialize(new{status="passed",actualObservedPages=results,networkCalls=0,productionCacheWrites=0}));
