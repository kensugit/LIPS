using CatalogSearch.Application;
using Npgsql;
using System.Text.Json;

namespace CatalogSearch.Infrastructure;

public sealed record ExactPairReviewReference(Guid Id, int Version, string Hash);
public sealed record ExactPairReviewSelection(ExactPairReviewReference Excel, ExactPairReviewReference Pdf);
public static class OrveauxPairReviewService
{
    public static async Task<IReadOnlyList<IntakeReview>> Discover(string connectionString, CancellationToken ct)
    {
        await using var c = new NpgsqlConnection(connectionString); await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT "Body"::text FROM "IntakeReviews"
            WHERE "Hash" IN (@excel,@pdf) AND "Body"->>'state' IN ('Held','Ready')
            AND COALESCE("Body"->'result'->>'stopQueue','false') <> 'true'
            ORDER BY "UpdatedAt" DESC
            """, c);
        cmd.Parameters.AddWithValue("excel", OrveauxAocPairPolicy.KnownOriginalHash(true).ToUpperInvariant());
        cmd.Parameters.AddWithValue("pdf", OrveauxAocPairPolicy.KnownOriginalHash(false).ToUpperInvariant());
        await using var reader = await cmd.ExecuteReaderAsync(ct); var rows = new List<IntakeReview>();
        while (await reader.ReadAsync(ct)) rows.Add(JsonSerializer.Deserialize<IntakeReview>(reader.GetString(0), new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        return rows;
    }
    public static bool Eligible(IntakeReview r, bool excel) => r.State is "Held" or "Ready"
        && r.Result?.StopQueue != true && OrveauxAocPairPolicy.IsKnownOriginalHash(r.Hash, excel);
    public static void Validate(IntakeReview r, ExactPairReviewReference expected, bool excel)
    {
        if (r.Id != expected.Id || r.Version != expected.Version || !string.Equals(r.Hash, expected.Hash, StringComparison.OrdinalIgnoreCase)
            || !Eligible(r, excel)) throw new IntakeConflictException();
    }
    public static async Task<(OrveauxPairCandidate Candidate, byte[] Excel, byte[] Pdf)> Prepare(
        IntakeReviewStore store, ExactPairReviewSelection selection, CancellationToken ct)
    {
        if (selection.Excel.Id == selection.Pdf.Id) throw new IntakeConflictException();
        var excel = await store.Get(selection.Excel.Id, ct) ?? throw new KeyNotFoundException();
        var pdf = await store.Get(selection.Pdf.Id, ct) ?? throw new KeyNotFoundException();
        Validate(excel, selection.Excel, true); Validate(pdf, selection.Pdf, false);
        var excelBytes = await store.Content(excel, ct); var pdfBytes = await store.Content(pdf, ct);
        return (OrveauxAocPairPolicy.KnownCandidate(excelBytes, pdfBytes), excelBytes, pdfBytes);
    }
}
