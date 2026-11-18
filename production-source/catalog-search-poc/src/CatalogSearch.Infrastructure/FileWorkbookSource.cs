using CatalogSearch.Application;
namespace CatalogSearch.Infrastructure;
public sealed class FileWorkbookSource : IWorkbookSource
{
    public async Task<SourceInput> ReadAsync(string path, DateTimeOffset observedAt, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        return new(Path.GetFileName(path), bytes, observedAt);
    }
}
