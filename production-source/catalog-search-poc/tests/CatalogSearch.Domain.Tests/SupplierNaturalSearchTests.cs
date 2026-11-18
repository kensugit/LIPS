using System.Text;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CatalogSearch.Domain.Tests;
[Collection("SearchReadModel")]
public class SupplierNaturalSearchTests
{
    static readonly SupplierSearchName Fines = new(Guid.NewGuid(), "ファインズ", "FINES");
    [Theory]
    [InlineData("ピエールの白でファインズ取り扱い")]
    [InlineData("ファインズ取扱いのピエールの白")]
    [InlineData("ファインズ取扱のピエールの白")]
    [InlineData("ファインズが取り扱うピエールの白")]
    [InlineData("ファインズの取り扱いでピエールの白")]
    [InlineData("取扱い先はファインズ、ピエールの白")]
    [InlineData("取扱先:ファインズ ピエール 白")]
    [InlineData("仕入先はファインズ、ピエールの白")]
    [InlineData("FINES取り扱いのピエールの白")]
    [InlineData("ｆｉｎｅｓ取り扱いのピエールの白")]
    public void HandlingPhrasesBecomeSupplierCondition(string text)
    {
        var plan = NaturalSearch.Parse(new(Text:text), [Fines]);
        Assert.Equal(Fines.Id, plan.SupplierId);
        Assert.Equal("白", plan.Filter.ProductType);
        Assert.Equal("ピエール", plan.Filter.Text);
        Assert.Contains("取扱先 ファインズ", plan.Conditions);
    }
    [Theory]
    [InlineData("ファインズ")]
    [InlineData("ドメーヌファインズ取り扱い")]
    [InlineData("ファインズ取り扱い物語")]
    [InlineData("未登録社取り扱い")]
    public void PlainNamesAndUnknownPhrasesRemainKeywords(string text)
    {
        var plan = NaturalSearch.Parse(new(Text:text), [Fines]);
        Assert.Null(plan.SupplierId);Assert.Equal(text,plan.Filter.Text);
    }
    [Fact] public void AmbiguityAndMultipleSuppliersFailExplicitly()
    {
        var other=new SupplierSearchName(Guid.NewGuid(),"日本リカー","NIPPON");
        Assert.Throws<ArgumentException>(()=>NaturalSearch.Parse(new(Text:"ファインズ取り扱い 日本リカー取り扱い"),[Fines,other]));
        Assert.Throws<ArgumentException>(()=>NaturalSearch.Parse(new(Text:"ファインズ取り扱い"),[Fines,new(Guid.NewGuid(),"ファインズ","OTHER")]));
        Assert.Throws<ArgumentException>(()=>NaturalSearch.Parse(new(Text:"ファインズ取り扱いの白以外"),[Fines]));
    }
    [PostgresFact] public async Task SupplierFilterDoesNotMatchNamesInsideAnotherSuppliersProducts()
    {
        var options=new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("CATALOG_TEST_CONNECTION")).Options;
        await using var db=new CatalogSearchDbContext(options);await db.Database.MigrateAsync();
        var tag="SUPPLIER"+Guid.NewGuid().ToString("N").ToUpperInvariant();
        var header="SKU,ProductNameJa,VolumeMl,Price,Inventory,Vintage,Type,ProducerNameJa\n";
        var first=Encoding.UTF8.GetBytes(header+$"{tag}A,{tag} ピエール,750,3000,12,[2021],白,ピエール\n{tag}B,{tag} ピエール,750,5000,12,2021,白,ピエール");
        var second=Encoding.UTF8.GetBytes(header+$"{tag}C,{tag} ピエール,750,3000,12,2021,白,{tag}取扱社");
        await new ContentImportService(db).ImportAsync("supplier-a.csv",first,DateTimeOffset.UtcNow,tag+"A",tag+"取扱社",default);
        await new ContentImportService(db).ImportAsync("supplier-b.csv",second,DateTimeOffset.UtcNow,tag+"B",tag+"別社",default);
        var query=new CatalogQuery(db);
        var rows=await query.SearchAsync(new(Text:$"{tag} ピエールの白で{tag}取扱社取り扱い", VintageRaw:"2021", PriceMax:4000));
        var row=Assert.Single(rows);Assert.Equal(tag+"A",row.Sku);Assert.Equal("2021",row.VintageLabel);
        var plan=await query.InterpretAsync(new(Text:$"{tag}取扱社取り扱い"));
        Assert.Contains($"取扱先 {tag}取扱社",plan.Conditions);
        Assert.Equal(3,(await query.SearchAsync(new(Text:tag,VintageRaw:"2021"))).Count);
    }
}
