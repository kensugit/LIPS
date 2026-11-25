using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CatalogSearch.Application;

namespace CatalogSearch.Infrastructure;

public sealed record SupplierSearchName(Guid Id, string Name, string Code);

public sealed record NaturalSearchPlan(CatalogFilter Filter, bool Champagne, string[] Conditions, bool MinExclusive = false, bool MaxExclusive = false, bool Sparkling = false, Guid? SupplierId = null);

/// <summary>Converts supported Japanese search phrases into deterministic SQL filters.</summary>
public static class NaturalSearch
{
    private const string Number = @"(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)(?:\.[0-9]+)?";
    private const string PriceNumber = "(?:" + Number + "万(?:" + Number + "千)?(?:" + Number + ")?|" + Number + "千(?:" + Number + ")?|" + Number + @"|[零〇一二三四五六七八九十百千万]+)";
    private const string PriceLabel = @"(?:(?:希望小売価格|小売価格|小売り|参考上代価格|参考上代|上代価格|上代|価格|予算|税抜)(?:は|が|:)?\s*)?";
    private static string Replace(string input, string pattern, MatchEvaluator evaluator) =>
        Regex.Replace(input, pattern, evaluator, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static NaturalSearchPlan Parse(CatalogFilter input, IReadOnlyCollection<SupplierSearchName>? suppliers = null)
    {
        if (input.Text?.Length > 200) throw new ArgumentException("検索条件は200文字以内で入力してください。");
        var text = (input.Text ?? "").Normalize(NormalizationForm.FormKC);
        var original = text;
        var filter = input;
        var conditions = new List<string>();
        var champagne = false;
        var sparkling = false;
        var minExclusive = false;
        var maxExclusive = false;
        var regionTerms = new List<string>();
        var parsed = false;
        const string marker = "\u001f";
        if (text.Contains(marker)) throw new ArgumentException("検索文に制御文字は使用できません。");
        Guid? supplierId = null;
        // Registered names/codes plus an explicit handling phrase: never infer a
        // supplier from a producer/product keyword or from partial name matches.
        var aliases = (suppliers ?? []).SelectMany(s => new[] { s.Name, s.Code }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new { Alias = x.Normalize(NormalizationForm.FormKC), Supplier = s }))
            .GroupBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Key.Length);
        foreach (var group in aliases)
        {
            var name = Regex.Escape(group.Key);
            const string boundary = @"(?=$|[\s\u001f、,。]|の|で|を|と|かつ|のみ|だけ)";
            var handling = @"(?:(?:取り扱い|取扱い|取扱)" + boundary + @"|取り扱う|扱う)";
            var pattern = @"(?:^|[\s\u001f、,]|の|で|と)(?:" + name + @"\s*(?:が|で|の)?\s*" + handling +
                @"|(?:取扱い先|取り扱い先|取扱先|取引先|仕入先)(?:は|が|:)?\s*" + name + boundary + @")";
            text = Replace(text, pattern, _ =>
            {
                var matches = group.Select(x => x.Supplier).DistinctBy(x => x.Id).ToArray();
                if (matches.Length != 1) throw new ArgumentException("取扱先名が複数の取引先に一致します。取引先コードで指定してください。");
                var supplier = matches[0];
                if (supplierId is { } previous && previous != supplier.Id)
                    throw new ArgumentException("取扱先は1社ずつ指定してください。");
                supplierId = supplier.Id;
                conditions.Add($"取扱先 {supplier.Name}");
                parsed = true;
                return marker;
            });
        }
        void Lower(decimal value, bool exclusive = false)
        {
            if (filter.PriceMin is null || value > filter.PriceMin) { filter = filter with { PriceMin = value }; minExclusive = exclusive; }
            else if (value == filter.PriceMin) minExclusive |= exclusive;
        }
        void Upper(decimal value, bool exclusive = false)
        {
            if (filter.PriceMax is null || value < filter.PriceMax) { filter = filter with { PriceMax = value }; maxExclusive = exclusive; }
            else if (value == filter.PriceMax) maxExclusive |= exclusive;
        }

        text = Replace(text, @"(?:容量(?:は|が|:)?\s*)?(?<![\d.,-])(" + Number + @")\s*(ml|ミリリットル|cl|l|リットル)(?![a-z])(?:のみ|だけ)?", m =>
        {
            var value = Amount(m.Groups[1].Value) * (m.Groups[2].Value.ToLowerInvariant() switch { "l" or "リットル" => 1000, "cl" => 10, _ => 1 });
            if (value <= 0 || value > int.MaxValue || value != decimal.Truncate(value))
                throw new ArgumentException("容量は1ml以上の整数で指定してください。");
            if (filter.VolumeMl is { } existing && existing != value)
                throw new ArgumentException("自由文と容量条件が異なります。容量を1つに揃えてください。");
            filter = filter with { VolumeMl = (int)value };
            parsed = true;
            return marker;
        });
        // Parse the complete range before consuming a single-ended '円から' condition.
        text = Replace(text, PriceLabel + @"(?<![\d.,-])(?<lower>" + PriceNumber + @")\s*(?:円\s*)?(?:から|[~〜～-])\s*(?<upper>" + PriceNumber + @")\s*円\s*(?<suffix>まで|以内|以下|以上|未満|超え?)?", m =>
        {
            if (m.Groups["suffix"].Value is "以上" or "超" or "超え")
                throw new ArgumentException("価格帯は「○円から○円」または「○円から○円まで」で指定してください。");
            var lower = Amount(m.Groups["lower"].Value);
            var upper = Amount(m.Groups["upper"].Value);
            if (lower > upper) throw new ArgumentException("価格帯の下限は上限以下にしてください。");
            Lower(lower);
            Upper(upper, m.Groups["suffix"].Value == "未満");
            parsed = true;
            return marker;
        });
        text = Replace(text, PriceLabel + @"(?<![\d.,-])(" + PriceNumber + @")\s*円\s*(以下|以内|まで|以上|から|未満|超え?|より安い|より高い)(?![a-z])", m =>
        {
            var amount = Amount(m.Groups[1].Value);
            var comparison = m.Groups[2].Value;
            if (comparison is "以上" or "から" or "超" or "超え" or "より高い") Lower(amount, comparison is not ("以上" or "から"));
            else Upper(amount, comparison is "未満" or "より安い");
            parsed = true;
            return marker;
        });
        text = Replace(text, @"予算(?:は|が|:)?\s*(" + PriceNumber + @")\s*円", m =>
        {
            Upper(Amount(m.Groups[1].Value));
            parsed = true;
            return marker;
        });
        text = Replace(text, "(?:" + CatalogSearchVocabulary.InStockPattern + @")(?!無|不明|りません|なし)(?:のみ|だけ)?", _ =>
        {
            filter = filter with { AvailableOnly = true };
            parsed = true;
            return marker;
        });

        text = Replace(text, ProductTypeVocabulary.QueryPattern, m =>
        {
            var a=ProductTypeVocabulary.QueryTerms(m.Groups["typeTerms"].Value);
            if(a is null)throw new ArgumentException("type_attribute_clarification_required");
            if(a.Category=="ワイン"&&a.Color is null&&a.Effervescence is null&&a.Sweetness is null&&m.Value.StartsWith("の",StringComparison.Ordinal)&&m.Index>0&&!char.IsWhiteSpace(text[m.Index-1])&&text[m.Index-1]!='\u001f')return m.Value;
            var explicitType=input.TypePredicate??ProductTypeVocabulary.Resolve(input);
            if(a.Conflicts.Any(axis => axis switch {"category"=>explicitType.Category is null,"color"=>explicitType.Color is null,"effervescence"=>explicitType.Effervescence is null,"sweetness"=>explicitType.Sweetness is null,_=>true}))throw new ArgumentException("conflicting_type_attributes");
            foreach(var axis in new (string? Before,string? After,string? Explicit)[]{(filter.Category,a.Category,explicitType.Category),(filter.Color,a.Color,explicitType.Color),(filter.Effervescence,a.Effervescence,explicitType.Effervescence),(filter.Sweetness,a.Sweetness,explicitType.Sweetness)})
                if(axis.Before is not null&&axis.After is not null&&axis.Before!=axis.After&&axis.Explicit is null)throw new ArgumentException("conflicting_type_attributes");
            var lower=filter with {ProductType=a.Category=="ワイン"?a.Color:filter.ProductType,Category=a.Category??filter.Category,Color=a.Color??filter.Color,Effervescence=a.Effervescence??filter.Effervescence,Sweetness=a.Sweetness??filter.Sweetness,TypePredicate=null};
            filter=ProductTypeVocabulary.MergeUi(input,lower) with {ProductType=input.ProductType??lower.ProductType};
            sparkling=filter.Effervescence=="スパークリング";parsed=true;return marker;
        });

        text = Replace(text, @"(?<![a-z0-9])(?:" + CatalogSearchVocabulary.SparklingPattern +
            @")(?![a-z0-9])|(?:^|[\s\u001f、,]|の|で|と)泡(?=の|で|を|と|[\s\u001f、,。]|$)", _ =>
        {
            sparkling = true;
            parsed = true;
            return marker;
        });
        text = Replace(text, @"シャンパーニュ(?:地方)?|シャンパン|(?<![a-z])champagne(?![a-z])", _ =>
        {
            champagne = true;
            parsed = true;
            return marker;
        });

        text = Replace(text, @"(?:^|[\s\u001f、,]|の|で|と)(?:年号(?:は|:)?\s*)?(?:" +
            CatalogSearchVocabulary.NonVintagePattern + @")(?=の|で|ワイン|[\s\u001f、,。]|$)", _ =>
        {
            if (filter.VintageRaw is { } vintage && VintageNotation.Normalize(vintage) != "NV")
                throw new ArgumentException("自由文と年号条件が異なります。年号を1つに揃えてください。");
            filter = filter with { VintageRaw = "NV" };
            parsed = true;
            return marker;
        });

        text = Replace(text, @"(?<![a-z0-9])(?<year>(?:19|20)[0-9]{2})年(?=$|[\s\u001f、,。]|の|で|と|のみ|だけ)", m =>
        {
            var year = m.Groups["year"].Value;
            if (filter.VintageRaw is { } previous && VintageNotation.Normalize(previous) != year)
                throw new ArgumentException("自由文と年号条件が異なります。年号を1つに揃えてください。");
            filter = filter with { VintageRaw = year };
            parsed = true;
            return marker;
        });

        text = Replace(text, @"(^|[\s\u001f、,]|の|で|と)(?:国(?:は|:)?\s*)?(フランス|イタリア|スペイン|ドイツ|日本|アメリカ|チリ|アルゼンチン|オーストラリア|ニュージーランド|南アフリカ|ポルトガル|オーストリア|ギリシャ)(?:産)?(?=の|で|[\s\u001f、,。]|$)", m =>
        {
            var country = m.Groups[2].Value;
            if (!string.IsNullOrEmpty(filter.Country) && filter.Country != country)
                throw new ArgumentException("自由文と国の条件が異なります。国を1つに揃えてください。");
            filter = filter with { Country = country };
            parsed = true;
            return marker;
        });
        text = Replace(text, @"(?:^|[\s\u001f、,]|の|で|と)(?:産地(?:は|:)?\s*)?(ブルゴーニュ|ボルドー|トスカーナ|ピエモンテ|ロワール|アルザス|ローヌ|シチリア)(?:地方|産)?(?=の|で|[\s\u001f、,。]|$)", m =>
        {
            regionTerms.Add(m.Groups[1].Value);
            parsed = true;
            return marker;
        });
        text = Replace(text, @"(?:タイプ(?:は|:)?\s*)?(?<![a-z0-9])(?<alias>" + CatalogSearchVocabulary.ProductTypePattern +
            @")(?![a-z0-9])|(?:^|[\s\u001f、,]|の|で|と)(?:タイプ(?:は|:)?\s*)?(?<type>赤|白|ロゼ|オレンジ)(?=の|で|を|と|[\s\u001f、,。]|$)", m =>
        {
            var type = m.Groups["alias"].Success ? CatalogSearchVocabulary.ProductType(m.Groups["alias"].Value) : m.Groups["type"].Value;
            if (!string.IsNullOrEmpty(filter.ProductType) && filter.ProductType != type)
                throw new ArgumentException("自由文とタイプの条件が異なります。タイプを1つに揃えてください。");
            filter = filter with { ProductType = type };
            parsed = true;
            return marker;
        });

        if (parsed)
        {
            if (Regex.IsMatch(original, @"税込|仕入(?:れ)?価格|卸価格|以外|除く|ではない|ではありません|でない|じゃない|または|もしくは|あるいは"))
                throw new ArgumentException("税込・仕入価格や除外・選択条件には未対応です。税抜希望小売価格と、すべて満たす条件で検索してください。");
            // Only remove particles at extracted-condition boundaries, never inside product names.
            var parts = text.Split(marker).Select(part =>
            {
                var value = part.Trim(' ', '、', '。', ',');
                value = Regex.Replace(value, @"\s*(?:を|で)?(?:探して(?:ください)?|検索して(?:ください)?|検索|探す|お願いします)$", "");
                if (value is "ワイン" or "商品" or "もの") return "";
                value = Regex.Replace(value, @"^(?:(?:の|で|かつ|と|を|のみ|だけ)\s*)+", "");
                value = Regex.Replace(value, @"\s*(?:(?:の|で|かつ|と|を|のみ|だけ)\s*)+$", "");
                if (value is "ワイン" or "商品" or "もの") return "";
                return value.Trim();
            }).Where(part => part.Length > 0);
            filter = filter with { Text = string.Join(" ", regionTerms.Distinct().Concat(parts)) };
        }
        // Plain keyword/SKU searches retain their original spelling and wildcard escaping.
        if (filter.PriceMin > filter.PriceMax || (filter.PriceMin is not null && filter.PriceMin == filter.PriceMax && (minExclusive || maxExclusive))) throw new ArgumentException("価格条件が矛盾しています。下限と上限を確認してください。");
        if(filter.Category is { } category)conditions.Add($"分類 {category}");
        if(filter.Color is { } color)conditions.Add($"色 {color}");
        if(filter.Effervescence is { } effervescence)conditions.Add($"発泡性 {effervescence}");
        if(filter.Sweetness is { } sweetness)conditions.Add($"甘辛度 {sweetness}");
        if (filter.VolumeMl is { } volume) conditions.Add($"容量 {volume}ml");
        if (filter.PriceMin is { } min) conditions.Add($"税抜希望小売 {min.ToString("#,0.##", CultureInfo.InvariantCulture)}円{(minExclusive ? "超" : "以上")}");
        if (filter.PriceMax is { } max) conditions.Add($"税抜希望小売 {max.ToString("#,0.##", CultureInfo.InvariantCulture)}円{(maxExclusive ? "未満" : "以下")}");
        if (champagne) conditions.Add("産地 シャンパーニュ（発泡）");
        if (sparkling) conditions.Add("タイプ 発泡");
        if (!string.IsNullOrWhiteSpace(filter.Region)) conditions.Add($"産地 {filter.Region}");
        if (!string.IsNullOrWhiteSpace(filter.Country)) conditions.Add($"国 {filter.Country}");
        if (!string.IsNullOrWhiteSpace(filter.ProductType)) conditions.Add($"タイプ {filter.ProductType}");
        if (!string.IsNullOrWhiteSpace(filter.VintageRaw)) conditions.Add($"年 {filter.VintageRaw}");
        if (filter.AvailableOnly) conditions.Add("在庫あり・少量のみ");
        if (!string.IsNullOrWhiteSpace(filter.Text)) conditions.Add($"キーワード {filter.Text}");
        return new(filter, champagne, conditions.Distinct().ToArray(), minExclusive, maxExclusive, sparkling, supplierId);
    }

    private static decimal Amount(string text)
    {
        text = text.Replace(",", "");
        foreach (var (unit, multiplier) in new[] { ('万', 10000m), ('千', 1000m), ('百', 100m), ('十', 10m) })
        {
            var at = text.IndexOf(unit);
            if (at < 0) continue;
            if (text.LastIndexOf(unit) != at) throw new ArgumentException("金額の表記を確認してください。");
            var total = (at == 0 ? 1 : Amount(text[..at])) * multiplier + (at == text.Length - 1 ? 0 : Amount(text[(at + 1)..]));
            if (total > 1_000_000_000) throw new ArgumentException("金額は10億円以下で指定してください。");
            return total;
        }
        const string kanji = "〇一二三四五六七八九";
        text = string.Concat(text.Select(c => c == '零' ? '0' : kanji.IndexOf(c) is var digit && digit >= 0 ? (char)('0' + digit) : c));
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value > 1_000_000_000)
            throw new ArgumentException("数値が大きすぎます。10億以下で指定してください。");
        return value;
    }
}
