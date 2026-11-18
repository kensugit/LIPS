using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace CatalogSearch.Web;
public sealed class InboxWatcher(IServiceScopeFactory scopes,IConfiguration config):BackgroundService
{
    private readonly Dictionary<string,(long Size,DateTime Time)> seen=new();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try { if(config.GetValue<bool>("CatalogDemo:Enabled")&&config.GetValue<bool>("CatalogDemo:WatchEnabled"))await ScanAsync(stoppingToken); }
            catch(Exception ex) when(ex is not OperationCanceledException){ /* DB unavailable: next tick retries without logging source contents. */ }
            try{await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
    private async Task ScanAsync(CancellationToken ct)
    {
        var root=config["CatalogDemo:Inbox"];
        if(string.IsNullOrWhiteSpace(root)||!Directory.Exists(root))return;
        foreach(var dir in Directory.EnumerateDirectories(root))
        {
            var supplier=Path.GetFileName(dir);
            if(!Regex.IsMatch(supplier,"^[A-Z0-9_]{1,50}$"))continue;
            foreach(var file in Directory.EnumerateFiles(dir).Where(x=>Path.GetExtension(x).ToLowerInvariant() is ".csv" or ".xlsx"))
            {
                ct.ThrowIfCancellationRequested();var info=new FileInfo(file);
                if(info.Length is 0 or >20_000_000)continue;
                var signature=(info.Length,info.LastWriteTimeUtc);
                if(!seen.TryGetValue(file,out var prior)||prior!=signature){seen[file]=signature;continue;}
                byte[] bytes;
                try{await using var read=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);using var memory=new MemoryStream();await read.CopyToAsync(memory,ct);bytes=memory.ToArray();}catch(IOException){continue;}
                var hash=Convert.ToHexString(SHA256.HashData(bytes));var key=supplier+"/"+Path.GetFileName(file);
                using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<CatalogSearchDbContext>();
                var state=await db.WatchFileStates.SingleOrDefaultAsync(x=>x.FileKey==key&&x.FileHash==hash,ct);
                if(state is null){state=new WatchFileState{FileKey=key,FileHash=hash};db.WatchFileStates.Add(state);await db.SaveChangesAsync(ct);}
                if(state.Status=="Completed"||state.Attempts>=3||state.NextAttemptAt>DateTimeOffset.UtcNow)continue;
                state.Attempts++;
                try
                {
                    var date=Regex.Match(Path.GetFileName(file),@"\d{4}-\d{2}-\d{2}").Value;
                    if(!DateTime.TryParseExact(date,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var observed))throw new InvalidDataException("Date required.");
                    if(Path.GetExtension(file).Equals(".xlsx",StringComparison.OrdinalIgnoreCase)&&supplier!="WINE_TO_STYLE")throw new InvalidDataException("Excel supplier mismatch.");
                    // Separate context: import rollback cannot discard watcher retry state.
                    using var importScope=scopes.CreateScope();var importDb=importScope.ServiceProvider.GetRequiredService<CatalogSearchDbContext>();
                    await new ContentImportService(importDb).ImportAsync(Path.GetFileName(file),bytes,new DateTimeOffset(observed,TimeSpan.Zero),supplier,supplier,ct);
                    state.Status="Completed";
                }
                catch(Exception ex)when(ex is not OperationCanceledException)
                {state.Status="Failed";db.OperationLogs.Add(new OperationLog{Action="FolderImport",Status="Failed",Target=state.Id.ToString(),CreatedAt=DateTimeOffset.UtcNow,Details="形式・列・日付・DB接続を確認してください。"});}
                state.NextAttemptAt=DateTimeOffset.UtcNow.AddSeconds(5*state.Attempts);await db.SaveChangesAsync(ct);
            }
        }
    }
}
