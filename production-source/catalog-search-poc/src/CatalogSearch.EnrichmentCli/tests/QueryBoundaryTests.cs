using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class QueryBoundaryTests
{
    // No provider is configured: any attempted DB access would fail for the wrong reason.
    [Theory][InlineData("regions129")][InlineData("italy")][InlineData("country-not-contained")]
    public async Task InvalidInternalExpansionRejectedBeforeDatabase(string boundary)
    {
        await using var db = new CatalogSearchDbContext(new DbContextOptionsBuilder<CatalogSearchDbContext>().Options);
        var plan = new SemanticSearchPlan(new(Country:"フランス"), null, [], ["R1"], null, [], false);
        plan = boundary switch {
            "regions129" => plan with { Regions = Enumerable.Range(0,129).Select(x=>"R"+x).ToArray() },
            "italy" => plan with { CountryAliases = ["フランス","イタリア"] },
            _ => plan with { CountryAliases = ["France"] }
        };
        await Assert.ThrowsAsync<ArgumentException>(() => new CatalogQuery(db).SearchSemanticAsync(plan));
    }
}
