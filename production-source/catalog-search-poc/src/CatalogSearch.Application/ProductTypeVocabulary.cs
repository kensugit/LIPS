using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CatalogSearch.Application;

public sealed record ProductTypeAttributes(string Schema, string? Category, string? Color, string? Effervescence,
    string? Sweetness, IReadOnlyList<string> UnknownTokens, IReadOnlyList<string> Conflicts);
public sealed record ProductTypePredicate(string? Category, string? Color, string? Effervescence, string? Sweetness, string? ExactRaw);
public sealed record TypeAttributeOption(string Value, string Label);
public sealed record TypeAttributeOptions(string Schema, IReadOnlyList<TypeAttributeOption> Categories,
    IReadOnlyList<TypeAttributeOption> Colors, IReadOnlyList<TypeAttributeOption> Effervescences, IReadOnlyList<TypeAttributeOption> Sweetnesses);

/// <summary>Reviewed supplier type-column vocabulary only. Product names are never evidence.</summary>
public static class ProductTypeVocabulary
{
    public const string Schema = "lips-product-type-attributes/v1";
    private sealed record Data(string Schema, Dictionary<string,string[]> Categories, Dictionary<string,string[]> Colors,
        Dictionary<string,string[]> Effervescences, Dictionary<string,string[]> Sweetnesses, string[] SweetAbbreviations, Dictionary<string,string[]> ReviewedUnknownQualifiers);
    private sealed record Token(string Text, string Axis, string Value);
    private static readonly Data Vocabulary;
    private static readonly Token[] Tokens;
    public static string Revision { get; }
    static ProductTypeVocabulary()
    {
        using var stream=typeof(ProductTypeVocabulary).Assembly.GetManifestResourceStream("product-type-attributes.ja.json") ?? throw new InvalidDataException("Type vocabulary missing.");
        using var memory=new MemoryStream();stream.CopyTo(memory);var bytes=memory.ToArray();
        Revision=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        Vocabulary=JsonSerializer.Deserialize<Data>(bytes) ?? throw new InvalidDataException("Type vocabulary invalid.");
        if(Vocabulary.Schema!=Schema)throw new InvalidDataException("Type vocabulary version invalid.");
        Tokens=new[]{("category",Vocabulary.Categories),("color",Vocabulary.Colors),("effervescence",Vocabulary.Effervescences),("sweetness",Vocabulary.Sweetnesses)}
            .SelectMany(axis=>axis.Item2.SelectMany(group=>group.Value.Select(alias=>new Token(Key(alias),axis.Item1,group.Key))))
            .OrderByDescending(t=>t.Text.Length).ToArray();
    }
    public static string Key(string? raw)=>Regex.Replace((raw??"").Normalize(NormalizationForm.FormKC).Trim(),@"[\s・/、,:：()\-]+", "").ToLowerInvariant();
    public static string OpaqueKey(string? raw)=>(raw??"").Normalize(NormalizationForm.FormKC).Trim();
    public static ProductTypeAttributes Classify(string? raw)
    {
        var key=Key(raw);var claims=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
        foreach(var axis in new[]{"category","color","effervescence","sweetness"})claims[axis]=new(StringComparer.Ordinal);
        var unknown=new List<string>();var rest=new StringBuilder();
        for(var i=0;i<key.Length;){
            var token=Tokens.FirstOrDefault(t=>key.AsSpan(i).StartsWith(t.Text,StringComparison.Ordinal));
            if(token is not null){if(rest.Length>0){unknown.Add(rest.ToString());rest.Clear();}claims[token.Axis].Add(token.Value);i+=token.Text.Length;continue;}
            if(key[i]=='甘'&&Vocabulary.SweetAbbreviations.Select(Key).Contains(key,StringComparer.Ordinal)){claims["sweetness"].Add("甘口");i++;continue;}
            rest.Append(key[i++]);
        }
        if(rest.Length>0)unknown.Add(rest.ToString());
        if(unknown.Count>0)
        {
            var allowed=unknown.All(t=>Vocabulary.ReviewedUnknownQualifiers.ContainsKey(t))
                ?Vocabulary.ReviewedUnknownQualifiers[unknown[0]].Where(axis=>unknown.All(t=>Vocabulary.ReviewedUnknownQualifiers[t].Contains(axis))).ToArray():[];
            foreach(var axis in claims.Keys)if(!allowed.Contains(axis,StringComparer.Ordinal))claims[axis].Clear();
        }
        if(unknown.Count==0&&claims["category"].Count==0&&(claims["color"].Count>0||claims["effervescence"].Count>0))claims["category"].Add("ワイン");
        var conflicts=claims.Where(p=>p.Value.Count>1).Select(p=>p.Key).ToList();
        // Unparsed negative/qualified labels cannot turn an isolated token into affirmative evidence.
        if(unknown.Any(t=>new[]{"非","ない","以外","不明"}.Any(t.Contains)))foreach(var pair in claims.Where(p=>p.Value.Count>0))if(!conflicts.Contains(pair.Key))conflicts.Add(pair.Key);
        string? Value(string axis)=>conflicts.Contains(axis)||claims[axis].Count!=1?null:claims[axis].Single();
        return new(Schema,Value("category"),Value("color"),Value("effervescence"),Value("sweetness"),unknown,conflicts);
    }
    public static void ValidateValues(string? category,string? color,string? effervescence,string? sweetness)
    {
        if(category is not null&&!Vocabulary.Categories.ContainsKey(category)||color is not null&&!Vocabulary.Colors.ContainsKey(color)||
           effervescence is not null&&!Vocabulary.Effervescences.ContainsKey(effervescence)||sweetness is not null&&!Vocabulary.Sweetnesses.ContainsKey(sweetness))throw new ArgumentException("invalid_type_attributes");
    }
    public static ProductTypePredicate Resolve(CatalogFilter filter,bool strict=true)
    {
        ValidateValues(filter.Category,filter.Color,filter.Effervescence,filter.Sweetness);
        var a=Classify(filter.ProductType);
        string? Choose(string axis,string? direct,string? legacy){
            if(strict&&direct is not null&&(a.Conflicts.Contains(axis)||legacy is not null&&direct!=legacy))throw new ArgumentException("conflicting_type_attributes");
            return direct??legacy;
        }
        var exact=!string.IsNullOrWhiteSpace(filter.ProductType)&&(a.UnknownTokens.Count>0||a.Conflicts.Count>0||a.Category is null&&a.Color is null&&a.Effervescence is null&&a.Sweetness is null)?OpaqueKey(filter.ProductType):null;
        return new(Choose("category",filter.Category,a.Category),Choose("color",filter.Color,a.Color),Choose("effervescence",filter.Effervescence,a.Effervescence),Choose("sweetness",filter.Sweetness,a.Sweetness),exact);
    }
    public static CatalogFilter MergeUi(CatalogFilter ui,CatalogFilter lower,string? taste=null,bool sparkling=false)
    {
        var high=ui.TypePredicate??Resolve(ui);var low=Resolve(lower,false);
        var sweetness=WineTasteVocabulary.ResolveSweetness(taste??"");
        var predicate=new ProductTypePredicate(high.Category??low.Category,high.Color??low.Color,high.Effervescence??low.Effervescence??(sparkling?"スパークリング":null),
            high.Sweetness??low.Sweetness??sweetness,high.ExactRaw??low.ExactRaw);
        return lower with {ProductType=ui.ProductType??lower.ProductType,Category=predicate.Category,Color=predicate.Color,Effervescence=predicate.Effervescence,Sweetness=predicate.Sweetness,TypePredicate=predicate};
    }
    public static bool Matches(string raw,ProductTypePredicate predicate,bool includeSweetness=true)
    {
        var a=Classify(raw);
        return (predicate.ExactRaw is null||OpaqueKey(raw)==predicate.ExactRaw)&&
            (predicate.Category is null||predicate.Category==a.Category)&&(predicate.Color is null||predicate.Color==a.Color)&&
            (predicate.Effervescence is null||predicate.Effervescence==a.Effervescence)&&(!includeSweetness||predicate.Sweetness is null||predicate.Sweetness==a.Sweetness);
    }
    public static string QueryPattern => @"(?:^|[\s\u001f、,]|の|で|と)(?:タイプ(?:は|:)?\s*)?(?<typeTerms>"+QueryTerm+@"(?:(?:\s*(?:の|・|/)?\s*)"+QueryTerm+@")*)(?=の|で|を|と|[\s\u001f、,。]|$)";
    private static string QueryTerm => @"(?:(?:白|泡)[\s・/]*甘(?!口|味)|"+string.Join("|",Tokens.Select(t=>t.Text).Distinct(StringComparer.Ordinal).OrderByDescending(t=>t.Length).Select(Regex.Escape))+")";
    public static ProductTypeAttributes? QueryTerms(string text)
    {
        var a=Classify(text.Normalize(NormalizationForm.FormKC).Replace("の","",StringComparison.Ordinal));
        return a.UnknownTokens.Count==0&&(a.Category is not null||a.Color is not null||a.Effervescence is not null||a.Sweetness is not null||a.Conflicts.Count>0)?a:null;
    }
    public static string GroupKey(string raw){var a=Classify(raw);return a.UnknownTokens.Count>0||a.Conflicts.Count>0?"raw:"+OpaqueKey(raw):string.Join("|",a.Category,a.Color,a.Effervescence,a.Sweetness);}
    public static string Label(string raw){var a=Classify(raw);if(a.UnknownTokens.Count>0||a.Conflicts.Count>0)return raw.Normalize(NormalizationForm.FormKC).Trim();var values=new[]{a.Category=="ワイン"?null:a.Category,a.Color,a.Effervescence,a.Sweetness}.Where(x=>x is not null);var label=string.Join("・",values);return label.Length==0?a.Category??raw:label;}
    public static TypeAttributeOptions Options(IEnumerable<string> raw){var values=raw.Select(Classify).ToArray();TypeAttributeOption[] Axis(Func<ProductTypeAttributes,string?> get)=>values.Select(get).Where(v=>v is not null).Select(v=>v!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(v=>new TypeAttributeOption(v,v)).ToArray();return new(Schema,Axis(x=>x.Category),Axis(x=>x.Color),Axis(x=>x.Effervescence),Axis(x=>x.Sweetness));}
}
