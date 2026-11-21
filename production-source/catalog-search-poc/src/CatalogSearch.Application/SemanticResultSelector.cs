namespace CatalogSearch.Application;

public static class SemanticResultSelector
{
    public static async Task<IReadOnlyList<T>> SelectAsync<T>(IAsyncEnumerable<T> candidates,
        Func<T, bool> matches, int limit, CancellationToken ct = default)
    {
        if (limit is < 1 or > 2000) throw new ArgumentOutOfRangeException(nameof(limit));
        var selected = new List<T>();
        await foreach (var item in candidates.WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (!matches(item)) continue;
            selected.Add(item);
            if (selected.Count == limit) break;
        }
        return selected;
    }
}
