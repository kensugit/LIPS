using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;

public sealed class RoutedImporterEnrichmentReaderTests
{
    sealed class Reader(string status):IImporterEnrichmentReader
    {
        public List<ImporterEnrichmentLookup> Calls {get;}=[];
        public ImporterEnrichmentReadResult ReadVerified(ImporterEnrichmentLookup lookup){Calls.Add(lookup);return new(status,null,null,null);}
        public ImporterEnrichmentReadResult ReadVerified(ImporterProductIdentity id,string fp,string version,DateTimeOffset now)=>ReadVerified(new(id,fp,null,version,now));
        public ImporterEnrichmentReadResult ReadVerifiedForProduct(ImporterProductIdentity id,string fp,string version,DateTimeOffset now)=>ReadVerified(id,fp,version,now);
    }
    static ImporterEnrichmentLookup Lookup(string supplier)=>new(new(supplier,"SKU","P","Wine","2023",750,"赤"),new('a',64),"https://example.com/p","legacy-version",DateTimeOffset.UtcNow);
    [Fact]public void LegacySupplierAndItsVersionRemainUnchanged()
    {
        var old=new Reader("verified");var selected=new Reader("verified");var mixed=new RoutedImporterEnrichmentReader(old,new Dictionary<string,RoutedImporterEnrichmentReader.Route>{{"TOYOTSU",new(selected,"toyotsu-version")}});
        var input=Lookup("NL");Assert.Equal("verified",mixed.ReadVerified(input).Status);Assert.Equal(input,Assert.Single(old.Calls));Assert.Empty(selected.Calls);
    }
    [Theory][InlineData("NOT_FOUND")][InlineData("BINDING_CHANGED")][InlineData("EXPIRED")]
    public void OldNonAdoptableVersionCannotBecomeDuplicateVerified(string oldStatus)
    {
        var old=new Reader(oldStatus);var selected=new Reader("verified");var mixed=new RoutedImporterEnrichmentReader(old,new Dictionary<string,RoutedImporterEnrichmentReader.Route>{{"TOYOTSU",new(selected,"toyotsu-version")}});
        var input=Lookup("TOYOTSU");Assert.Equal("verified",mixed.ReadVerified(input).Status);Assert.Equal(input with{ExtractorVersion="toyotsu-version"},Assert.Single(selected.Calls));Assert.Equal(input,Assert.Single(old.Calls));
    }
    [Fact]public void MissingRouteDoesNotAdoptLegacyVerifiedRecord()
    {
        var old=new Reader("verified");var selected=new Reader("NOT_FOUND");var mixed=new RoutedImporterEnrichmentReader(old,new Dictionary<string,RoutedImporterEnrichmentReader.Route>{{"TOYOTSU",new(selected,"v")}});
        Assert.Equal("NOT_FOUND",mixed.ReadVerified(Lookup("TOYOTSU")).Status);Assert.Single(old.Calls);Assert.Single(selected.Calls);
    }
    [Fact]public void TwoVerifiedRootsAreAnExcludedConflict()
    {
        var mixed=new RoutedImporterEnrichmentReader(new Reader("verified"),new Dictionary<string,RoutedImporterEnrichmentReader.Route>{{"TOYOTSU",new(new Reader("verified"),"v")}});
        var result=mixed.ReadVerified(Lookup("TOYOTSU"));Assert.Equal("conflict",result.Status);Assert.Null(result.Content);
    }
    [Theory][InlineData("NOT_FOUND")][InlineData("EXPIRED")][InlineData("BINDING_CHANGED")][InlineData("verified")]
    public void SourceBoundLegacyConflictCannotBeClearedByNewRoute(string selectedStatus)
    {
        var mixed=new RoutedImporterEnrichmentReader(new Reader("conflict"),new Dictionary<string,RoutedImporterEnrichmentReader.Route>{{"TOYOTSU",new(new Reader(selectedStatus),"v")}});
        Assert.Equal("conflict",mixed.ReadVerified(Lookup("TOYOTSU")).Status);
    }
    [Theory][InlineData("CORRUPT_OR_UNAVAILABLE")][InlineData("WRITER_STATE_UNKNOWN")][InlineData("SNAPSHOT_CHANGED")]
    public void UnmeasuredLegacyStateCannotBeDeclaredNoConflict(string status)
    {
        var mixed=new RoutedImporterEnrichmentReader(new Reader(status),new Dictionary<string,RoutedImporterEnrichmentReader.Route>{{"TOYOTSU",new(new Reader("verified"),"v")}});
        Assert.Equal("ROUTE_STATE_UNKNOWN",mixed.ReadVerified(Lookup("TOYOTSU")).Status);
    }
    [Theory][InlineData("Pinot Noir 100%",true)][InlineData("Pinot Noir",false)][InlineData("Cabernet Franc 100%",false)]
    public void CompleteOriginalCompositionCannotBeOverriddenByAnotherHundredPercent(string original,bool conflict)=>
        Assert.Equal(conflict,ImporterEnrichmentAcquisition.HasConflict([original],[new("カベルネ・フラン","カベルネ・フラン100%")]));
}
