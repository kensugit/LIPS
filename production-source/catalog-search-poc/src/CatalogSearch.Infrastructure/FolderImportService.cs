using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CatalogSearch.Application;

namespace CatalogSearch.Infrastructure;

public sealed record FolderImportResult(string Path, string Status, string Message, string? SupplierCode = null,
    string? ObservedAt = null, int Products = 0, bool AlreadyImported = false, Guid? SourceDocumentId = null, bool StopQueue = false,
    string? QuestionKind = null, Guid? ReviewId = null, IReadOnlyList<ImportRowHold>? Holds = null,
    IReadOnlyList<IntakeMappingQuestion>? Questions = null)
{
    public int AddedProducts { get; init; }
    public string? Hash { get; init; }
    public string? FailureCode { get; init; }
    public string? Remedy { get; init; }
}
public sealed class FolderImportHeldException(string message) : Exception(message);

// Pure parsing and policy; persistence is behind the existing store adapter.
public sealed class FolderImportService(IInventoryStore store, IReadOnlyList<IntakeReusableRule>? reusableRules = null,
    IFolderPdfReader? pdfReader = null, IFolderDuplicateLookup? duplicateLookup = null, IFolderPdfReader? xlsReader = null)
{
    public const int MaxFileBytes = 20_000_000;
    private static readonly Dictionary<string, (string Code, string Name)> Suppliers = new(StringComparer.Ordinal)
    {
        ["いろはわいん"] = ("IROHA", "いろはわいん"), ["IROHA"] = ("IROHA", "いろはわいん"),
        ["オルヴォー"] = ("ORVEAUX", "オルヴォー"), ["ORVEAUX"] = ("ORVEAUX", "オルヴォー"),
        ["ENEOS"] = ("ENEOS", "ENEOSマテリアルトレーディング株式会社"),
        ["ファインズ"] = ("FINES", "ファインズ"), ["FINES"] = ("FINES", "ファインズ"),
        ["日本リカー"] = ("NIPPON_LIQUOR", "日本リカー"), ["NIPPON_LIQUOR"] = ("NIPPON_LIQUOR", "日本リカー"),
        ["テラヴェール"] = ("TERRAVERT", "テラヴェール"), ["TERRAVERT"] = ("TERRAVERT", "テラヴェール"),
        ["豊通"] = ("TOYOTSU", "豊通"), ["TOYOTSU"] = ("TOYOTSU", "豊通"),
        ["フィネス"] = ("FINESSE", "フィネス"), ["FINESSE"] = ("FINESSE", "フィネス"),
        ["ワインエクスペリエンス"] = ("WINE_EXPERIENCE", "ワインエクスペリエンス"),
        ["WINE_EXPERIENCE"] = ("WINE_EXPERIENCE", "ワインエクスペリエンス"),
        ["アルカン"] = ("ARCAN", "アルカン"), ["ARCAN"] = ("ARCAN", "アルカン"),
        ["大榮産業"] = ("DAIEI", "大榮産業"), ["DAIEI"] = ("DAIEI", "大榮産業"),
        ["ディオニー"] = ("DIONY", "ディオニー"), ["DIONY"] = ("DIONY", "ディオニー"),
        ["ローヤルオブジャパン"] = ("ROYAL_OF_JAPAN", "ローヤルオブジャパン"), ["ROYAL"] = ("ROYAL_OF_JAPAN", "ローヤルオブジャパン"),
        ["ENEOSマテリアルズトレーディング"] = ("ENEOS", "ENEOSマテリアルトレーディング株式会社"),
        ["ENEOSマテリアルトレーディング株式会社"] = ("ENEOS", "ENEOSマテリアルトレーディング株式会社"),
        ["ルカヴォー"] = ("LE_CAVEAU", "ルカヴォー"), ["LE_CAVEAU"] = ("LE_CAVEAU", "ルカヴォー"),
        ["ヌーヴェルセレクション"] = ("NOUVELLES_SELECTIONS", "ヌーヴェル・セレクション"),
        ["LUC"] = ("LUC", "LUC"), ["WINE_TO_STYLE"] = ("WINE_TO_STYLE", "Wine to Style"),
        ["LA_VINEE"] = ("LA_VINEE", "ラ・ヴィネ"), ["LAVINEE"] = ("LA_VINEE", "ラ・ヴィネ"),
        ["ラヴィネ"] = ("LA_VINEE", "ラ・ヴィネ"), ["ラ・ヴィネ"] = ("LA_VINEE", "ラ・ヴィネ"),
        ["WINETOSTYLE"] = ("WINE_TO_STYLE", "Wine to Style"), ["ワイントゥスタイル"] = ("WINE_TO_STYLE", "Wine to Style"),
        ["KINOSHITA"] = ("KINOSHITA", "木下インターナショナル"), ["木下インターナショナル"] = ("KINOSHITA", "木下インターナショナル"),
        ["TOA"] = ("TOA", "東亜商事"), ["東亜商事"] = ("TOA", "東亜商事"), ["AMZ"] = ("AMZ", "AMZ"),
        ["エイ・エム・ズィー"] = ("AMZ", "AMZ")
    };
    private static string Normalize(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC).ToUpperInvariant(), @"\s+", "");

    public async Task<FolderImportResult> ImportAsync(string relativePath, byte[] bytes, string? explicitDate, CancellationToken ct, IReadOnlyList<string>? selectedSkus = null,
        IReadOnlyList<IntakeMappingResolution>? mappingAnswers = null, bool allowPartial = false)
    {
        string? code = null, dateText = null;
        IReadOnlyList<IntakeMappingQuestion>? currentQuestions = null;
        IReadOnlyList<ImportRowHold>? pendingHolds = null;
        var unresolvedMapping = false;
        FolderImportResult Result(string status, string message) => new(relativePath, status, message, code, dateText,
            QuestionKind: allowPartial && currentQuestions is not null ? (unresolvedMapping ? "Mapping" : "Rows") : null,
            Questions: allowPartial ? currentQuestions : null, Holds: allowPartial ? pendingHolds : null);
        try
        {
            ct.ThrowIfCancellationRequested();
            // This is display metadata, never a local/server filesystem path.
            if (relativePath.Length > 500 || relativePath.Contains('\\') || relativePath.Contains(':') || relativePath.Any(char.IsControl))
                return Result("Failed", "相対パスが不正です。");
            var parts = relativePath.Split('/');
            if (parts.Length < 2 || parts.Length > 16 || parts.Any(x => string.IsNullOrWhiteSpace(x) || x is "." or ".."))
                return Result("Failed", "取引先フォルダーを含む相対パスを指定してください。");
            var name = parts[^1];
            if (name.StartsWith("~$", StringComparison.Ordinal)) return Result("Held", "編集中の一時ファイルです。取り込みません。");
            if (bytes.Length is 0 or > MaxFileBytes) return Result("Failed", "空ファイル、または20MBを超えています。");
            var ext = Path.GetExtension(name).ToLowerInvariant();
            var matches = parts[..^1].Select(Normalize).Where(Suppliers.ContainsKey).Select(x => Suppliers[x]).Distinct().ToArray();
            if (matches.Length == 1) code = matches[0].Code;
            if (matches.Length != 1) return Result("Held", "取引先フォルダーを一意に判定できません。対応する取引先名を確認してください。");
            var supplier = matches[0]; code = supplier.Code;
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            // Runs only after uploading and hashing the bytes; before date/layout parsing.
            // A partial or unknown save must never be skipped as a completed duplicate.
            if (duplicateLookup is not null && await duplicateLookup.FindCompleted(code, hash, ct) is { } duplicate)
                return new(relativePath, "Success", "SHA-256一致：登録済みの同一原本です。重複のためスキップしました。",
                    code, duplicate.ObservedAt, duplicate.Products, true, duplicate.SourceDocumentId) { Hash = hash };
            if (ext is not (".xlsx" or ".csv" or ".pdf") && !(ext == ".xls" && code == "IROHA")) return Result("Unsupported", "このファイル形式には未対応です。") with
                { FailureCode = "UnsupportedFileType", Remedy = "旧Excel（.xls）はいろはわいんの対応帳票のみ取り込めます。その他は対応PDF・Excelまたは標準CSVを用意してください。" };
            FolderPdfResult? pdf = null;
            if (ext == ".xls" && code == "IROHA")
            {
                if (xlsReader is null) return Result("Unsupported", "旧Excel読取り環境が未設定です。");
                pdf = await xlsReader.ReadAsync(code, bytes, ct);
                if (!pdf.Ok) return Result("Unsupported", pdf.Message ?? "Excelを読み取れませんでした。") with
                    { Hash = hash, FailureCode = pdf.Code, Remedy = pdf.Remedy };
                if (string.IsNullOrWhiteSpace(pdf.ObservedAt)) return Result("Held", "Excel内の資料日付を確認できません。登録していません。");
                if (!string.IsNullOrWhiteSpace(explicitDate) && explicitDate != pdf.ObservedAt)
                    return Result("Held", "指定日付とExcel内の資料日付が一致しません。登録していません。");
                explicitDate = pdf.ObservedAt;
            }
            if (ext == ".pdf")
            {
                if (pdfReader is null) return Result("Unsupported", "PDF読取り環境が未設定です。") with
                    { Hash = hash, FailureCode = "PdfReaderUnavailable", Remedy = "管理担当者がPDF読取り環境を設定してください。" };
                pdf = await pdfReader.ReadAsync(code, bytes, ct);
                if (!pdf.Ok) return Result("Unsupported", pdf.Message ?? "PDFを読み取れませんでした。") with
                    { Hash = hash, FailureCode = pdf.Code, Remedy = pdf.Remedy };
            }
            if (allowPartial && (code != "LA_VINEE" || ext != ".xlsx"))
                return Result("Held", "回答済み行の部分保存はラヴィネの対応Excelだけに限ります。");
            var workbookBytes=bytes;
            if(code=="ORVEAUX" && ext==".xlsx") {
                ValidateArchive(bytes);
                var sourceDate=OrveauxStockParser.DocumentDate(bytes);
                if(!string.IsNullOrWhiteSpace(explicitDate)&&explicitDate!=sourceDate)return Result("Held","指定日付と資料内更新日が一致しません。");
                explicitDate=sourceDate;
                pendingHolds=OrveauxStockParser.NewArrivalHolds(bytes);
            }
            if(code=="ENEOS" && ext==".xlsx") {
                ValidateArchive(bytes);
                var sourceDate=EneosInventoryParser.DocumentDate(bytes);
                if(sourceDate is not null) {
                    if(!string.IsNullOrWhiteSpace(explicitDate)&&explicitDate!=sourceDate)return Result("Held","指定日付と原典内の日付が一致しません。");
                    explicitDate=sourceDate;
                }
            }
            if(code=="NOUVELLES_SELECTIONS" && ext==".xlsx") {
                ValidateArchive(bytes);
                workbookBytes=NouvellesInventoryParser.ReadProjection(bytes);
                var sourceDate=NouvellesInventoryParser.DocumentDate(workbookBytes);
                if(!string.IsNullOrWhiteSpace(explicitDate) && explicitDate!=sourceDate)return Result("Held","指定日付と資料内更新日が一致しません。");
                explicitDate=sourceDate;
            }
            if (!TryDate(name, explicitDate, out var date)) return Result("Held", "資料日付が未指定・不正・複数、またはファイル名と不一致です。原典と基準日を確認してください。") with { QuestionKind = "Date" };
            if(code == "LE_CAVEAU" && ext == ".xlsx" && date != new DateTime(2026,9,29)) return Result("Held", "ルカヴォーの対応資料日付と一致しません。");
            dateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            IWorkbookContentParser parser = new StandardCsvParser(); var version = "StandardCsv/1"; var parseBytes = workbookBytes;
            if (ext == ".xlsx")
            {
                parseBytes = code == "WINE_TO_STYLE" ? WtsStaticWorkbook.Project(bytes) : workbookBytes;
                ValidateArchive(parseBytes);
                var selected = WorkbookParserRegistry.Detect(parseBytes);
                if (selected.SupplierCode != code) return Result("Held", "フォルダーの取引先とExcelの内容が一致しません。");
                parser = selected.Parser; version = selected.Version + (ReferenceEquals(parseBytes, bytes) ? "" : "/StaticValues/1");
                if (code is "LUC" or "LA_VINEE")
                {
                    var questions = WorkbookMappingReview.Inspect(parseBytes);
                    if (allowPartial) currentQuestions = questions;
                    var resolutions = new List<IntakeMappingResolution>();
                    var unresolved = new List<IntakeMappingQuestion>();
                    foreach (var q in questions)
                    {
                        var answered = mappingAnswers?.SingleOrDefault(a => a.Question.Id == q.Id && a.Question.Scope == q.Scope && a.Question.RawValue == q.RawValue);
                        if (answered is not null && q.Choices.Contains(answered.Meaning)) resolutions.Add(answered);
                        else
                        {
                            var preview = IntakeReusableRules.Preview(new(q.Scope, q.Kind, q.SourceKey), reusableRules ?? []);
                            if (preview.Status == "Candidate" && preview.Rule is { } rule && q.Choices.Contains(rule.Meaning))
                                resolutions.Add(new(q, rule.Meaning, $"再利用ルール {rule.Id} v{rule.Version}; 回答 {rule.AnswerId} v{rule.AnswerVersion}; 原典SHA256 {rule.SourceHash}; {rule.Evidence}"));
                            else unresolved.Add(q);
                        }
                    }
                    unresolvedMapping = unresolved.Count > 0;
                    if (unresolvedMapping && !allowPartial) return Result("Held", "容量単位・地域表記の確認が必要です。原典の対象行と質問を確認してください。") with { QuestionKind = "Mapping", Questions = unresolved };
                    parser = code == "LUC" ? new LucInventoryParser(resolutions) : new LaVineeInventoryParser(resolutions);
                    if (resolutions.Count > 0) version += code == "LUC" ? "/HumanVolumeUnit/1" : "/HumanMapping/1";
                }
            }
            IReadOnlyList<SupplierInventoryRow>? partialRows = null;
            // Keep unresolved commercial terms visible at their original rows.
            // Parsing candidates is not permission to store incomplete price semantics.
            if (parser is LaVineeInventoryParser laVinee)
            {
                var checkedRows = laVinee.ParseWithHolds(parseBytes, ct);
                if (allowPartial)
                {
                    pendingHolds = checkedRows.Holds;
                    var heldSkus = pendingHolds.Select(h => h.Sku).ToHashSet(StringComparer.Ordinal);
                    partialRows = checkedRows.Rows.Where(row => !heldSkus.Contains(row.SupplierProductCode)).ToArray();
                    if (partialRows.Count == 0)
                        return Result("Held", "回答済みの正常な商品行がありません。保留行は保存していません。");
                }
                else if (checkedRows.Holds.Count > 0)
                    return Result("Held", "回答済みの容量・地域は保持しています。価格単位・商品識別などの未確定事項を確認してください。登録していません。")
                        with { QuestionKind = "Rows", Holds = checkedRows.Holds };
            }
            var rows = pdf?.Rows ?? partialRows ?? parser.Parse(parseBytes, ct);
            if (pdf is not null) version = pdf.Version ?? "FolderPdf/1";
            if (rows.Count is 0 or > 10000 || rows.Any(x => string.IsNullOrWhiteSpace(x.SupplierProductCode) || string.IsNullOrWhiteSpace(x.ProductNameJa)) || (code != "ENEOS" && rows.Select(x => x.SupplierProductCode).Distinct().Count() != rows.Count))
                return Result("Held", "商品が0件・上限超過、必須項目不足、またはSKU重複です。登録していません。");
            var saved = await store.SaveAsync(new SourceInput(name, bytes, new DateTimeOffset(date, TimeSpan.Zero), code, supplier.Name, version){SelectedSkus=selectedSkus, PendingHolds=pendingHolds},
                Convert.ToHexString(SHA256.HashData(bytes)), rows, ct);
            if (saved.Holds is { Count: > 0 }) return new(relativePath, saved.Products > 0 ? "Partial" : "Held",
                code == "ORVEAUX" ? "在庫シートの保存済み合計" + saved.Products + "件（今回更新" + saved.AddedProducts + "件）。既存商品との対応未確定行と新着案内は確認対象として保持しています。資料全体の取込は未完了です。" :
                saved.Products > 0 ? "一部保存・保留あり。未保存の商品は確認が必要です。" : "商品行の確認が必要です。",
                code, dateText, saved.Products, false, saved.SourceDocumentId, QuestionKind:code == "ORVEAUX" ? "OrveauxNewArrivals" : allowPartial && unresolvedMapping ? "Mapping" : "Rows", Holds:saved.Holds, Questions:allowPartial ? currentQuestions : null) { AddedProducts = saved.AddedProducts };
            return new(relativePath, "Success", saved.AlreadyImported ? "取込済み。同じ内容のため重複登録しませんでした。" : "取込が完了しました。",
                code, dateText, saved.Products, saved.AlreadyImported, saved.SourceDocumentId, Questions:allowPartial ? currentQuestions : null) { AddedProducts = saved.AddedProducts };
        }
        catch (UnsupportedWorkbookException) { return Result("Unsupported", "対応するExcelレイアウトがありません。"); }
        catch (AmbiguousWorkbookException) { return Result("Held", "Excelの取引先候補が複数あります。登録していません。"); }
        catch (FolderImportHeldException ex) { return Result("Held", ex.Message); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or FormatException or OverflowException or Microsoft.VisualBasic.FileIO.MalformedLineException or ClosedXML.Excel.Exceptions.ClosedXMLException)
        { return Result("Held", "ファイル形式・必須列・値を確認してください。登録していません。"); }
        // Storage/transport failures can include a lost commit response: never claim rollback or auto-retry.
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or TimeoutException or Microsoft.EntityFrameworkCore.DbUpdateException)
        { return Result("Held", "保存結果を確認できません。取込履歴を確認してください。自動再試行はしません。") with { StopQueue = true }; }
    }

    public static bool TryDate(string name, string? explicitDate, out DateTime date)
    {
        date = default;
        // Only width variants of decimal digits are dates; NFKC also turns circled labels into digits.
        var dateName = Regex.Replace(name, "[０-９]", m => ((char)('0' + m.Value[0] - '０')).ToString());
        var tokens = Regex.Matches(dateName, @"(?<!\d)(?<year>20\d{2})(?:[-._年](?<month>\d{1,2})[-._月](?<day>\d{1,2})日?|(?<month>\d{2})(?<day>\d{2}))(?!\d)");
        var dates = new List<DateTime>();
        foreach (Match m in tokens)
        {
            if (!DateTime.TryParseExact($"{m.Groups["year"].Value}-{int.Parse(m.Groups["month"].Value):00}-{int.Parse(m.Groups["day"].Value):00}", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
            dates.Add(parsed);
        }
        if (dates.Distinct().Count() > 1) return false;
        if (!string.IsNullOrWhiteSpace(explicitDate))
        {
            if (!DateTime.TryParseExact(explicitDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return false;
            return dates.Count == 0 || dates[0] == date;
        }
        if (dates.Count == 0) return false;
        date = dates[0]; return true;
    }

    private static void ValidateArchive(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
        if (zip.Entries.Count > 2000 || zip.Entries.Sum(x => x.Length) > 100_000_000 || zip.Entries.Any(x => x.FullName.StartsWith("xl/externalLinks/", StringComparison.Ordinal)))
            throw new InvalidDataException("Workbook exceeds limits or has external links.");
    }
}
