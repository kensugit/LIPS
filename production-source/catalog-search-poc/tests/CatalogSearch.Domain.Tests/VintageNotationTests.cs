using System.Text;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CatalogSearch.Domain.Tests;
[Collection("SearchReadModel")]
public class VintageNotationTests
{
    [Theory]
    [InlineData("2021", "2021")]
    [InlineData("[2021]", "2021")]
    [InlineData("(2021)", "2021")]
    [InlineData(" （２０２１） ", "2021")]
    [InlineData("[ (2021) ]", "2021")]
    [InlineData("NV", "NV")]
    [InlineData("N.V", "NV")]
    [InlineData("N.V.", "NV")]
    [InlineData(" n.v. ", "NV")]
    [InlineData("（Ｎ．Ｖ．）", "NV")]
    [InlineData("MV", "NV")]
    [InlineData(" m.v. ", "NV")]
    [InlineData("（Ｍ．Ｖ．）", "NV")]
    [InlineData("[1982-19]", "[1982-19]")]
    [InlineData("202", "202")]
    [InlineData("0", "0")]
    [InlineData("-", "-")]
    [InlineData("30ANS", "30ANS")]
    [InlineData("95+96+00", "95+96+00")]
    [InlineData("(2021]", "(2021]")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizesOnlyKnownNotations(string? raw, string expected)
        => Assert.Equal(expected, VintageNotation.Normalize(raw));

    [Fact] public void OptionsAndMatchingUseTheSameEquivalence()
    {
        string[] raw = ["2021", "[2021]", "(2021)", "N.V.", "NV", "N.V", "2020", "MV", "", "-"];
        var options = VintageNotation.Options(raw);
        Assert.Equal(1, options.Count(x => x == "2021"));
        Assert.Equal(1, options.Count(x => x == "NV"));
        Assert.DoesNotContain("", options);
        Assert.DoesNotContain("MV", options);
        Assert.Equal(VintageNotation.MatchingRawValues(raw, "NV"), VintageNotation.MatchingRawValues(raw, "MV"));
        Assert.Equal(new[] { "2021", "[2021]", "(2021)" }, VintageNotation.MatchingRawValues(raw, "(2021)"));
        Assert.Equal(new[] { "N.V.", "NV", "N.V", "MV" }, VintageNotation.MatchingRawValues(raw, "NV"));
    }

    [PostgresFact] public async Task EquivalentYearsAndNvMatchBeforeLimitAndKeepEvidence()
    {
        var options = new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("CATALOG_TEST_CONNECTION")).Options;
        await using var db = new CatalogSearchDbContext(options);
        await db.Database.MigrateAsync();
        var tag = "VINTAGE" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        string[] raw = ["2021", "[2021]", "(2021)", "NV", "N.V", "N.V.", "2020", "MV", "", "-"];
        var csv = "SKU,ProductNameJa,VolumeMl,Price,Inventory,Vintage\n" + string.Join("\n", raw.Select((v, i) => $"{tag}{i},{tag},750,3000,12,{v}"));
        await new ContentImportService(db).ImportAsync("vintages.csv", Encoding.UTF8.GetBytes(csv), DateTimeOffset.UtcNow, tag, tag, default);
        var query = new CatalogQuery(db);
        foreach (var requested in new[] { "2021", "[2021]", "(2021)" })
        {
            var rows = await query.SearchAsync(new(Text: tag, VintageRaw: requested));
            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => { Assert.Equal("2021", r.VintageLabel); Assert.NotEqual(Guid.Empty, r.SupplierProductId); });
            Assert.Contains(rows, r => r.VintageRaw == "[2021]");
        }
        foreach (var requested in new[] { "NV", "N.V.", "N.V", "MV", "M.V." })
        {
            var rows = await query.SearchAsync(new(Text: tag, VintageRaw: requested));
            Assert.Equal(4, rows.Count);
            Assert.Contains(rows, r => r.VintageRaw == "MV");
            Assert.All(rows, r => Assert.Equal("NV", r.VintageLabel));
        }
        Assert.Single(await query.SearchAsync(new(Text: tag, VintageRaw: "2021", Limit: 1)));
        Assert.Empty(await query.SearchAsync(new(Text: tag, VintageRaw: "2021", PriceMax: 2000)));
        Assert.Equal(4, (await query.SearchAsync(new(Text: tag, VintageRaw: "MV"))).Count);
        Assert.Empty(await query.SearchAsync(new(Text: tag, VintageRaw: "2099")));
        Assert.Equal(10, await db.SupplierProducts.CountAsync(x => x.SupplierProductName == tag));
    }
}
