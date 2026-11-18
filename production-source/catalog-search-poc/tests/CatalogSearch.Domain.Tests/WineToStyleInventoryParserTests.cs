using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using ClosedXML.Excel;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class WineToStyleInventoryParserTests
{
    [Fact]
    public async Task ParsesSupplierSpecificValuesAndEvidenceLocation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wine-to-style-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("ワインリスト");
                var headers = new[]
                {
                    "SKU", "生産者名／ブランド名", "Producer / Brand", "商品名", "Wine", "VTG", "容量", "入数",
                    "希望小売", "在庫", "タイプ", "国", "産地", "JAN", "品種（英）", "ｱﾙｺｰﾙ度数",
                    "原産地呼称JP", "原産地呼称EN", "有機区分 カテゴリー\n （カタログ・TS表記）",
                    "有機区分\n 認証団体", "メモ\n （価格関係）", "ｽｸﾘｭｰ", "旧"
                };
                for (var i = 0; i < headers.Length; i++) sheet.Cell(2, i + 1).Value = headers[i];
                var values = new object[]
                {
                    "TEST-NV", "生産者", "Producer", "商品", "Wine", "NV", 750, 6, "オープン", "少量",
                    "泡・白", "フランス", "シャンパーニュ", "1234567890123(瓶)\n1234567890124(箱)",
                    "Pinot Noir 60%", 12.5, "シャンパーニュ", "Champagne", "Biologique", "認証", "注記", "", "WIS"
                };
                for (var i = 0; i < values.Length; i++) sheet.Cell(3, i + 1).Value = Convert.ToString(values[i], System.Globalization.CultureInfo.InvariantCulture);
                workbook.SaveAs(path);
            }

            var rows = await new WineToStyleInventoryParser().ParseAsync(path, CancellationToken.None);

            var row = Assert.Single(rows);
            Assert.Null(row.Vintage);
            Assert.Equal("NV", row.VintageRaw);
            Assert.Null(row.ReferenceRetailPrice);
            Assert.False(row.TaxIncluded);
            Assert.Equal(InventoryStatus.LowStock, row.Inventory.Status);
            Assert.Equal(2, row.JanCodes.Count);
            Assert.Equal(3, row.SourceRow);
            Assert.Equal("ワインリスト", row.SourceSheet);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
