using CatalogSearch.Infrastructure;
using Xunit;
namespace CatalogSearch.Domain.Tests;

public class SpecialPurchasePriceTests
{
    private const string Evidence = """{"SpecialPurchasePrice":{"Version":1,"SupplierCode":"FINES","Currency":"JPY","TaxIncluded":false,"Amount":20992.5}}""";
    [Fact]
    public void PurchaseIsTypedAndPreservesFractionalYen()
    {
        Assert.Equal(20992.5m, SpecialPurchasePrice.Read("FINES", Evidence));
        Assert.Null(SpecialPurchasePrice.Read("OTHER", Evidence));
        Assert.Null(SpecialPurchasePrice.Read("FINES", """{"特価":5000}"""));
        Assert.Null(SpecialPurchasePrice.Read("FINES", "{}"));
    }
    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"SpecialPurchasePrice\":null}")]
    [InlineData("{\"SpecialPurchasePrice\":{\"Version\":\"1\"}}")]
    public void MalformedEvidenceDoesNotBreakSearch(string raw) => Assert.Null(SpecialPurchasePrice.Read("FINES", raw));
    [Fact]
    public void InvalidAmountsVersionsAndTaxDoNotBecomePrices()
    {
        Assert.Null(SpecialPurchasePrice.Read("FINES", Evidence.Replace("20992.5", "-1")));
        Assert.Null(SpecialPurchasePrice.Read("FINES", Evidence.Replace("false", "true")));
        Assert.Null(SpecialPurchasePrice.Read("FINES", Evidence.Replace("JPY", "USD")));
        Assert.Null(SpecialPurchasePrice.Read("FINES", Evidence.Replace("\"Version\":1", "\"Version\":2")));
    }
}
