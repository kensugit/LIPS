using System.Security.Cryptography;
using System.Text.Json;
using CatalogSearch.Application;

namespace CatalogSearch.Infrastructure;

public static class LaVineeDuplicatePolicy
{
    public static void Apply(byte[] bytes,List<SupplierInventoryRow> rows,List<ImportRowHold> holds)
    {
        var hash=Convert.ToHexString(SHA256.HashData(bytes));
        foreach(var group in rows.GroupBy(r=>r.SupplierProductCode).Where(g=>g.Count()>1).ToArray())
        {
            var candidates=group.ToArray();
            // A lower quantity does not resolve another unanswered or conflicting field.
            if(holds.Where(h=>h.Sku==group.Key).Any(h=>h.Reason!="同じ商品コードが複数行にあります。別VT等の商品同一性は未確定です。"))continue;
            if(candidates.Any(r=>r.Inventory.Quantity is null or <0)||
               candidates.Select(CommercialIdentity).Distinct(StringComparer.Ordinal).Count()!=1)continue;
            var sameVintage=candidates.Select(r=>r.VintageRaw).Distinct(StringComparer.Ordinal).Count()==1;
            var approvedPair=hash=="3DF70F0F9DD75275AC4CF6ADAD509485A091EAFF87A880806D1EB9D0B76B5F03"&&group.Key=="105004143"&&
                candidates.Length==2&&candidates.All(r=>r.SourceSheet=="202610LaVineeリスト")&&
                candidates.Any(r=>r.SourceRow==938&&r.VintageRaw=="N.V")&&candidates.Any(r=>r.SourceRow==1000&&r.VintageRaw=="S.A.");
            if(!sameVintage&&!approvedPair)continue;
            var minimum=candidates.Min(r=>r.Inventory.Quantity);
            if(candidates.Count(r=>r.Inventory.Quantity==minimum)!=1)continue;
            var chosen=candidates.Single(r=>r.Inventory.Quantity==minimum);
            var adopted=chosen with {DuplicateMinimumSelected=true,
                Occurrences=candidates.SelectMany(r=>r.Occurrences).ToArray(),
                MappingEvidence=candidates.SelectMany(r=>r.MappingEvidence).ToArray()};
            rows.RemoveAll(r=>r.SupplierProductCode==group.Key);rows.Add(adopted);
            holds.RemoveAll(h=>h.Sku==group.Key);
        }
    }
    private static string CommercialIdentity(SupplierInventoryRow r)=>JsonSerializer.Serialize(new
    {
        r.ProductNameJa,r.ProductNameEn,r.ProducerNameJa,r.ProducerNameEn,r.VolumeMl,r.Vintage,
        r.Country,r.Region,r.SourceRegionRaw,r.SourceProductCategoryRaw,r.ProductType,
        r.ReferenceRetailPrice,r.PriceRaw,r.TaxIncluded,r.PriceComponents
    });
}
