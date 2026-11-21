using System.Text;
using CatalogSearch.Application;
using Xunit;
namespace CatalogSearch.Domain.Tests;
public sealed class ImporterYourCellarPolicyTests
{
    static IReadOnlyDictionary<string,IReadOnlyDictionary<string,YourCellarProductPageIdentity.ProducerRule>> Rules(string display="表示生産者") =>
        new Dictionary<string,IReadOnlyDictionary<string,YourCellarProductPageIdentity.ProducerRule>> {
            ["SYNTHETIC"]=new Dictionary<string,YourCellarProductPageIdentity.ProducerRule>{["原本生産者"]=new(display,"表示生産者（合成地域）")}};
    static readonly ImporterProductIdentity Identity=new("SYNTHETIC","SKU-A","原本生産者","合成商品","2023",750,"赤");
    const string Url="https://www.yourcellar.jp/view/item/000000001957?category_page_id=ct12";
    [Fact] public void ExplicitRegistrySelectsOneAdapterAndBindsClaimsAndVersion()
    {
        var policy=new ImporterPageAdapterPolicy(reviewedYourCellarSuppliers:Rules());
        var selected=policy.Select("SYNTHETIC");var body=Encoding.UTF8.GetBytes(YourCellarProductPageIdentityTests.Page());
        Assert.StartsWith("yourcellar-product/4+",selected.Version);
        Assert.True(selected.TryExtract(Identity,Url,body,out var grapes));Assert.True(selected.Verify(Identity,grapes,Url,body));
        Assert.False(selected.Verify(Identity,[new("メルロー","メルロー 100%")],Url,body));
        Assert.False(selected.TryExtract(Identity,Url.Replace("yourcellar.jp","evil.example"),body,out _));
        Assert.False(selected.TryExtract(Identity with{SupplierCode="OTHER"},Url,body,out _));
        Assert.Equal("official-product-jsonld/1",policy.Select("OTHER").Version);
        var host=new ImporterEvidenceHostPolicy([new("SYNTHETIC","www.yourcellar.jp")]);
        Assert.InRange(policy.BoundVersion(host).Length,1,100);
        Assert.NotEqual(policy.BoundVersion(host),new ImporterPageAdapterPolicy(reviewedYourCellarSuppliers:Rules("別表示")).BoundVersion(host));
        Assert.Equal(policy.BoundVersion(host),new ImporterPageAdapterPolicy(reviewedYourCellarSuppliers:Rules()).BoundVersion(host));
    }
    [Fact] public void SameSupplierCollisionIsRejectedAndJsonFallbackIsNotAttempted()
    {
        var jet=new Dictionary<string,IReadOnlyDictionary<string,string>>{["SYNTHETIC"]=new Dictionary<string,string>{{"brand","producer"}}};
        Assert.Throws<ArgumentException>(()=>new ImporterPageAdapterPolicy(jet,Rules()));
        var selected=new ImporterPageAdapterPolicy(reviewedYourCellarSuppliers:Rules()).Select("SYNTHETIC");
        var wrongProduct=YourCellarProductPageIdentityTests.Page().Replace("SKU-A","SKU-B");
        var body=Encoding.UTF8.GetBytes(wrongProduct+"<script type='application/ld+json'>{\"@context\":\"https://schema.org\",\"@type\":\"Product\"}</script>");
        Assert.False(selected.TryExtract(Identity,Url,body,out _));
        Assert.Equal("official-product-jsonld/1",new ImporterPageAdapterPolicy().Version);
        Assert.Equal(new ImporterPageAdapterPolicy(jet).Version,new ImporterPageAdapterPolicy(jet,new Dictionary<string,IReadOnlyDictionary<string,YourCellarProductPageIdentity.ProducerRule>>()).Version);
    }
}
