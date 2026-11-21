using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using CatalogSearch.EnrichmentCli;

namespace CatalogSearch.SourceExportCli;

public sealed record ApprovedAcquisitionTarget(string OfficialUrl,long ExpectedGeneration,string ObservationSha256);
// Trusted launcher implementation only. No implementation or production CLI switch is supplied here.
// It must verify actor, fixed URL approval, observed cache generation and their identity binding.
public interface IAcquisitionHandoffPrerequisites : IExportExecutionPrerequisites
{
    ApprovedAcquisitionTarget VerifyApprovedTarget(Selector selector,string originalFingerprint);
}
public sealed record AcquisitionHandoffResult(string RequestSha256,int RequestBytes,string GenerationObservationSha256);
public static class AcquisitionHandoff
{
    static readonly JsonSerializerOptions Strict=new(){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=12};
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok){if(!ok)throw new InvalidDataException("HANDOFF_REJECTED");}
    static void Duplicate(JsonElement e)
    {
        if(e.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var p in e.EnumerateObject()){Need(names.Add(p.Name));Duplicate(p.Value);}}
        else if(e.ValueKind==JsonValueKind.Array)foreach(var child in e.EnumerateArray())Duplicate(child);
    }
    static T Read<T>(byte[] bytes,params string[] fields)
    {
        using var d=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=12});Duplicate(d.RootElement);
        Need(d.RootElement.ValueKind==JsonValueKind.Object&&d.RootElement.EnumerateObject().Select(p=>p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(fields));
        return JsonSerializer.Deserialize<T>(bytes,Strict)??throw new InvalidDataException("HANDOFF_REJECTED");
    }
    public static AcquisitionHandoffResult Prepare(string exportRoot,string outputRoot,string exportSha,string completionSha,
        string intentSha,string selectorPath,string selectorSha,string policyPath,string policySha,string operationId,IAcquisitionHandoffPrerequisites trusted)
    {
        trusted.VerifyProtectedExecution(exportRoot);trusted.VerifyProtectedExecution(outputRoot);
        ProtectedExportFiles.CheckRoot(exportRoot);ProtectedExportFiles.CheckRoot(outputRoot);
        var bytes=ProtectedExportFiles.ReadPinned(Path.Combine(exportRoot,"source-export.protected.json"),524288,exportSha);
        var completion=Read<ExportCustodyReceipt>(ProtectedExportFiles.ReadPinned(Path.Combine(exportRoot,"export.completion.safe.json"),4096,completionSha),
            "Status","SelectorSha256","ExportSha256","ExportBytes","SourceFingerprint","DatabaseReadAttempted","IntentSaved","CompletionSaved");
        var intentBytes=ProtectedExportFiles.ReadPinned(Path.Combine(exportRoot,"export.intent.safe.json"),4096,intentSha);
        using(var intent=JsonDocument.Parse(intentBytes))
        {
            Duplicate(intent.RootElement);var e=intent.RootElement;
            Need(e.ValueKind==JsonValueKind.Object&&e.EnumerateObject().Select(p=>p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(new[]{"schema","selectorSha256","automaticRetry"}));
            Need(e.GetProperty("schema").GetString()=="lips-db-source-export-intent/v1"&&e.GetProperty("selectorSha256").GetString()==selectorSha&&e.GetProperty("automaticRetry").ValueKind==JsonValueKind.False);
        }
        var value=Read<ExportValue>(bytes,"Selector","SupplierId","EvidenceId","Identity","ExtractedJson","OriginalFingerprint","ExtractedBytes","ExtractedSha256");
        var selector=Read<Selector>(ProtectedExportFiles.ReadPinned(selectorPath,4096,selectorSha),"ProductId","SourceId","SupplierCode","Sku");
        Need(value.Selector==selector);
        Need(value.Selector is not null&&value.Identity is not null);SourceExport.Validate(value.Selector!);
        Need(value.SupplierId!=Guid.Empty&&value.EvidenceId!=Guid.Empty&&value.Identity!.SupplierCode==value.Selector!.SupplierCode&&value.Identity.Sku==value.Selector.Sku);
        var raw=new UTF8Encoding(false,true).GetBytes(value.ExtractedJson);
        Need(raw.Length==value.ExtractedBytes&&AcquisitionInput.Sha(raw)==value.ExtractedSha256&&SourceFingerprint.Create(value.Selector.SourceId,value.ExtractedJson)==value.OriginalFingerprint);
        Need(completion.Status=="DB_SOURCE_EXPORTED"&&completion.DatabaseReadAttempted&&completion.IntentSaved&&completion.CompletionSaved&&
            completion.SelectorSha256==selectorSha&&completion.ExportSha256==exportSha&&completion.ExportBytes==bytes.Length&&completion.SourceFingerprint==value.OriginalFingerprint);
        var target=trusted.VerifyApprovedTarget(value.Selector,value.OriginalFingerprint);
        Need(target is not null&&target.ExpectedGeneration>=0&&target.ObservationSha256 is {Length:64}&&target.ObservationSha256.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f'));
        var request=new AcquisitionRequest("lips-importer-acquisition-request/v1",operationId,value.Identity,value.Selector.SourceId,value.ExtractedJson,value.OriginalFingerprint,target!.OfficialUrl,target.ExpectedGeneration);
        var requestBytes=JsonSerializer.SerializeToUtf8Bytes(request,AcquisitionInput.Json);
        var policyBytes=ProtectedExportFiles.ReadPinned(policyPath,AcquisitionInput.PolicyLimit,policySha);
        AcquisitionInput.Validate(requestBytes,policyBytes); // Same strict parser used by the existing validate-only entry.
        trusted.VerifyProtectedExecution(outputRoot);
        var sha=ProtectedExportFiles.CreateNew(Path.Combine(outputRoot,"acquisition-request.protected.json"),requestBytes,AcquisitionInput.RequestLimit);
        return new(sha,requestBytes.Length,target.ObservationSha256);
    }
}
