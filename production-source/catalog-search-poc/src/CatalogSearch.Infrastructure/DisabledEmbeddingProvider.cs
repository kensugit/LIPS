using CatalogSearch.Application;

namespace CatalogSearch.Infrastructure;

public sealed class DisabledEmbeddingProvider : IEmbeddingProvider
{
    public string Provider => "Disabled";
    public string Model => "None";
    public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Embedding is disabled. SQL/FTS search remains available.");
}
