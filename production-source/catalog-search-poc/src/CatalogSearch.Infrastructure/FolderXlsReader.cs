using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CatalogSearch.Application;
namespace CatalogSearch.Infrastructure;
public sealed class FolderXlsReader(string? python, string script) : IFolderPdfReader
{
    private static FolderPdfResult Unavailable() => new(false, "XlsReaderUnavailable",
        "サーバーの旧Excel読取り環境が未設定です。", "管理担当者がPdfImport:PythonまたはOriginalRender:Pythonと同梱xlrdを設定してください。");
    public async Task<FolderPdfResult> ReadAsync(string supplier, byte[] bytes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(python) || !File.Exists(python) || !File.Exists(script)) return Unavailable();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add(script); start.ArgumentList.Add(supplier);
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) return Unavailable();
            var output = ReadBounded(process.StandardOutput, 32_000_000, deadline.Token);
            var errors = ReadBounded(process.StandardError, 65536, deadline.Token);
            await process.StandardInput.BaseStream.WriteAsync(bytes, deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            var json = await output; await errors;
            if (process.ExitCode != 0) return Unavailable();
            var result = JsonSerializer.Deserialize<FolderPdfResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result is null) return Unavailable();
            if (result.Ok && (result.Rows is null || result.Rows.Count is 0 or > 10000 ||
                result.Hash != Convert.ToHexString(SHA256.HashData(bytes))))
                return new(false, "XlsExtractionFailed", "旧Excel読取り結果と原本の照合に失敗しました。", "原本を保持して管理担当者へ確認してください。自動再試行はしません。");
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return new(false, "XlsReadTimeout", "旧Excel読取りが制限時間を超えました。", "ページ数・ファイルサイズを確認し、資料を分けるかExcel版を使用してください。"); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or JsonException or InvalidDataException)
        { return Unavailable(); }
        finally { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    }
    private static async Task<string> ReadBounded(StreamReader reader, int limit, CancellationToken ct)
    {
        var result = new StringBuilder(); var buffer = new char[8192]; int n;
        while ((n = await reader.ReadAsync(buffer, ct)) > 0)
        { if (result.Length + n > limit) throw new InvalidDataException("旧Excel output limit"); result.Append(buffer, 0, n); }
        return result.ToString();
    }
}
