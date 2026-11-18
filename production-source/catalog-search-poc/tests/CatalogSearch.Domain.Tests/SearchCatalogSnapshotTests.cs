using CatalogSearch.Infrastructure;
using Xunit;
namespace CatalogSearch.Domain.Tests;
public sealed class SearchCatalogSnapshotTests
{
    private static string Digest(string[][] data)
    {
        using var d = new SearchCatalogDigest();
        for (var i = 0; i < 4; i++) { d.BeginTable(CatalogSearchSnapshotProvider.DependencyTables[i]); foreach (var row in data[i]) d.AppendRecord(row); d.EndTable(); }
        return d.Finish();
    }
    [Fact] public void AllFourDependenciesAffectVersion()
    {
        var original = new[] { new[] {"a"}, new[] {"b"}, new[] {"c"}, new[] {"d"} };
        var baseline = Digest(original);
        for (var i=0;i<4;i++) { var changed=original.Select(x=>x.ToArray()).ToArray(); changed[i][0]+="changed"; Assert.NotEqual(baseline,Digest(changed)); }
        Assert.StartsWith("search-catalog/v1:",baseline); Assert.Equal(baseline,Digest(original));
    }
    [Fact] public void FramingSeparatesRecordsAndTables()
    {
        Assert.NotEqual(Digest([ ["ab","c"],[],[],[] ]),Digest([ ["a","bc"],[],[],[] ]));
        Assert.NotEqual(Digest([ ["x"],[],[],[] ]),Digest([ [],["x"],[],[] ]));
        Assert.NotEqual(Digest([ ["a","b"],[],[],[] ]),Digest([ ["b","a"],[],[],[] ]));
        using var d=new SearchCatalogDigest(); Assert.Throws<InvalidOperationException>(()=>d.BeginTable("Suppliers")); Assert.Throws<InvalidOperationException>(()=>d.Finish());
    }
    [Fact] public void CanonicalOptionsAreOrderIndependentAndPreserveCodes()
    {
        var a=CatalogSearchSnapshotProvider.CanonicalOptions(["US","FR","FR",""],["B","A"],["red"],[750,375,750],["ＮＶ","(2020)"],[new("AMZ","Supplier")]);
        Assert.Equal(new[]{"FR","US"},a.Countries);Assert.Equal(new[]{375,750},a.VolumesMl);Assert.Equal(new[]{"2020","NV"},a.Vintages);Assert.Equal(new[]{"AMZ"},a.Suppliers);
    }
    [Fact] public void UnknownCapacityDoesNotBreakFreeTextOptionsOrInventAChoice()
    {
        var mixed=CatalogSearchSnapshotProvider.CanonicalOptions([],[],[],[0,750,0,375],[],[]);
        Assert.Equal(new[]{375,750},mixed.VolumesMl);
        var unknownOnly=CatalogSearchSnapshotProvider.CanonicalOptions([],[],[],[0,0],[],[]);
        Assert.Empty(unknownOnly.VolumesMl);
    }
    [Fact] public void BoundsAndAmbiguousSuppliersAreRejected()
    {
        void Build(IEnumerable<string> countries,IEnumerable<int> volumes,IEnumerable<SearchSupplierOption> suppliers)=>CatalogSearchSnapshotProvider.CanonicalOptions(countries,[],[],volumes,[],suppliers);
        Assert.Throws<InvalidOperationException>(()=>Build(Enumerable.Range(0,251).Select(x=>x.ToString()),[],[]));
        Assert.Throws<InvalidOperationException>(()=>Build([new string('x',201)],[],[]));
        Assert.Throws<InvalidOperationException>(()=>Build([],[-1],[]));
        Assert.Throws<InvalidOperationException>(()=>Build([],[100001],[]));
        Assert.Throws<InvalidOperationException>(()=>Build([],[],[new("A","One"),new("A","Two")]));
    }
}
