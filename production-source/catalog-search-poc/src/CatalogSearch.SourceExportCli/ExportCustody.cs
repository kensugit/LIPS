using System.Text.Json;
using CatalogSearch.Infrastructure;

namespace CatalogSearch.SourceExportCli;

// Trusted local launcher prerequisite, never supplied by request JSON or a success Boolean.
// Production implementation is intentionally absent until actor/ownership/credential custody is verified.
public interface IExportExecutionPrerequisites
{
    void VerifyProtectedExecution(string root);
}
public sealed record ExportCustodyReceipt(string Status,string SelectorSha256,string? ExportSha256,
    int ExportBytes,string? SourceFingerprint,bool DatabaseReadAttempted,bool IntentSaved,bool CompletionSaved);
public static class ExportCustody
{
    public static async Task<ExportCustodyReceipt> RunAsync(string root,string selectorPath,string selectorSha,
        IExportExecutionPrerequisites prerequisites,IReadOnlySourceSnapshot source,CancellationToken ct)
    {
        // Reject unknown ownership/actor before reading an input or creating anything.
        prerequisites.VerifyProtectedExecution(root);
        ProtectedExportFiles.CheckRoot(root);
        var bytes=ProtectedExportFiles.ReadPinned(selectorPath,4096,selectorSha);
        using var doc=JsonDocument.Parse(bytes);
        if(doc.RootElement.ValueKind!=JsonValueKind.Object || doc.RootElement.EnumerateObject().Count()!=4 ||
           !doc.RootElement.EnumerateObject().Select(x=>x.Name).ToHashSet(StringComparer.Ordinal).SetEquals(new[]{"ProductId","SourceId","SupplierCode","Sku"}))
            throw new InvalidDataException("SELECTOR_REJECTED");
        var selector=JsonSerializer.Deserialize<Selector>(bytes)??throw new InvalidDataException("SELECTOR_REJECTED");
        SourceExport.Validate(selector);
        var intent=Path.Combine(root,"export.intent.safe.json");
        var output=Path.Combine(root,"source-export.protected.json");
        var completion=Path.Combine(root,"export.completion.safe.json");
        foreach(var path in new[]{intent,output,completion})
            if(Path.Exists(path)||new FileInfo(path).LinkTarget is not null)throw new IOException("PRIOR_EXPORT_PRESERVED");
        ProtectedExportFiles.CreateNew(intent,JsonSerializer.SerializeToUtf8Bytes(new{schema="lips-db-source-export-intent/v1",selectorSha256=selectorSha,automaticRetry=false}),4096);
        // Any subsequent failure preserves intent/output; no completion is synthesized and no retry occurs.
        var value=await SourceExport.CaptureAsync(selector,source,ct);
        prerequisites.VerifyProtectedExecution(root);
        var body=JsonSerializer.SerializeToUtf8Bytes(value);
        var sha=ProtectedExportFiles.CreateNew(output,body,524288);
        var receipt=new ExportCustodyReceipt("DB_SOURCE_EXPORTED",selectorSha,sha,body.Length,value.OriginalFingerprint,true,true,true);
        ProtectedExportFiles.CreateNew(completion,JsonSerializer.SerializeToUtf8Bytes(receipt),4096);
        return receipt;
    }
}
