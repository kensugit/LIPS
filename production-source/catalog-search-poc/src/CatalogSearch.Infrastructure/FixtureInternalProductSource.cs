using System.Runtime.CompilerServices;
using System.Text.Json;
using CatalogSearch.Application;
namespace CatalogSearch.Infrastructure;
public sealed class FixtureInternalProductSource : IInternalProductSource
{
    public string SourceSystemCode => "SHUDORAKU_FIXTURE";
    public async IAsyncEnumerable<InternalProductSourceRow> ReadChangedAsync(DateTimeOffset? since, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        foreach (var item in new[] {
            new { Key="DEMO-INTERNAL-001", Name="確認用サンプル ピノ・ノワール", Vintage="2023", Active=true },
            new { Key="DEMO-INTERNAL-002", Name="確認用サンプル ピノ・ノワール", Vintage="2023", Active=true },
            new { Key="DEMO-INTERNAL-003", Name="確認用サンプル 未紐付け白ワイン", Vintage="NV", Active=true },
            new { Key="DEMO-INTERNAL-004", Name="確認用サンプル 終売ワイン", Vintage="2020", Active=false } })
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(item.Key, item.Name, "カクニンヨウサンプル", null, "DEMO_SUPPLIER", 750, "WINE", "匿名サンプル。実際の酒道楽マスターではありません。", item.Active,
                new DateTimeOffset(2026,9,17,0,0,0,TimeSpan.Zero), JsonSerializer.Serialize(new { item.Key, item.Name, item.Vintage, item.Active, Country="フランス", Region="ブルゴーニュ", Type=item.Vintage=="NV"?"白":"赤" }));
        }
    }
}
