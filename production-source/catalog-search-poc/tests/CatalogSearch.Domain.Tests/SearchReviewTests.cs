using System.Text;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CatalogSearch.Domain.Tests;
public class SearchReviewTests
{
    [Fact] public void TermsHandleUnicodeSpacesAndLiteralWildcards()
    {
        Assert.Equal(new[]{"赤","Pinot"},SearchTerms.Parse(" 赤　 Pinot  pinot\t"));
        Assert.Equal(@"%a\%\_\\%",SearchTerms.Pattern(@"a%_\"));
    }
    [Theory]
    [InlineData("生産者A","Producer A","生産者A","producer a",true)]
    [InlineData("生産者A","","生産者B","",false)]
    [InlineData("生産者A","Producer A","生産者A","Producer B",false)]
    [InlineData("","","生産者A","Producer A",true)]
    public void ProducerComparisonIsConservative(string ja,string en,string otherJa,string otherEn,bool expected)
        =>Assert.Equal(expected,CandidatePolicy.Matches("ワイン",750,"NV",ja,en,"ワイン",750,"NV",otherJa,otherEn));
    [Fact] public void MissingIdentityDoesNotGenerateCandidates()
    {
        Assert.False(CandidatePolicy.Matches("ワイン",750,"","","","ワイン",750,"","",""));
        Assert.False(CandidatePolicy.Matches("ワイン",null,"NV","","","ワイン",750,"NV","",""));
    }
    [PostgresFact] public async Task SearchAndStaleReviewUseCurrentPersistedData()
    {
        var options=new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("CATALOG_TEST_CONNECTION")).Options;
        var code="KEYWORD_"+Guid.NewGuid().ToString("N").ToUpperInvariant();
        const string header="SKU,ProductNameJa,VolumeMl,Price,Inventory,Vintage,ProducerNameJa,ProductNameEn\n";
        var csv=Encoding.UTF8.GetBytes(header+"ONE,検索赤 特別,750,4000,12,NV,生産者A,Pinot Noir\nTWO,検索赤 特別,750,5000,12,NV,生産者A,Pinot Noir\nTHREE,検索赤 特別,750,6000,12,NV,生産者B,Pinot Noir\n");
        await using var db=new CatalogSearchDbContext(options);await db.Database.MigrateAsync();
        var import=new ContentImportService(db);var now=DateTimeOffset.UtcNow;
        var first=await import.ImportAsync("a.csv",csv,now,code,code,default);
        var query=new CatalogQuery(db);
        Assert.NotEmpty(await query.SearchAsync(new(Text:"特別　pINot",PriceMin:4000,PriceMax:4500)));
        Assert.Empty(await query.SearchAsync(new(Text:"特別 存在しない単語")));
        Assert.Empty(await query.SearchAsync(new(Text:"検索赤%")));
        await Assert.ThrowsAsync<ArgumentException>(()=>query.SearchAsync(new(Text:new string('a',201))));
        var supplier=await db.Suppliers.SingleAsync(x=>x.Code==code);
        var rows=await db.SupplierProducts.Where(x=>x.SupplierId==supplier.Id).OrderBy(x=>x.SupplierProductCode).ToArrayAsync();
        var one=rows.Single(x=>x.SupplierProductCode=="ONE");var two=rows.Single(x=>x.SupplierProductCode=="TWO");var three=rows.Single(x=>x.SupplierProductCode=="THREE");
        await using(var guard=new CatalogSearchDbContext(options))
            await Assert.ThrowsAsync<ReviewConflictException>(()=>new ReviewService(guard).ApplyAsync(new("supplier",one.Id,three.Id,null,"link",first.SourceDocumentId,first.SourceDocumentId.ToString()),default));
        await import.ImportAsync("a.csv",Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(csv).Replace("4000","4100")),now.AddDays(1),code,code,default);
        await using(var guard=new CatalogSearchDbContext(options))
            await Assert.ThrowsAsync<ReviewConflictException>(()=>new ReviewService(guard).ApplyAsync(new("supplier",one.Id,two.Id,null,"link",first.SourceDocumentId,first.SourceDocumentId.ToString()),default));
        var current=await db.SupplierProducts.AsNoTracking().SingleAsync(x=>x.Id==one.Id);
        await using(var guard=new CatalogSearchDbContext(options))
            await Assert.ThrowsAsync<ReviewConflictException>(()=>new ReviewService(guard).ApplyAsync(new("supplier",one.Id,two.Id,null,"link",first.SourceDocumentId,current.CurrentSourceDocumentId.ToString()),default));
        await using(var guard=new CatalogSearchDbContext(options))
            Assert.NotNull(await new ReviewService(guard).ApplyAsync(new("supplier",one.Id,two.Id,null,"link",current.CurrentSourceDocumentId,current.CurrentSourceDocumentId.ToString()),default));
    }
}
