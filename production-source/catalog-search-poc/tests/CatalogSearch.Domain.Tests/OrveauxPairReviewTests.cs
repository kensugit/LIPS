using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class OrveauxPairReviewTests
{
    [Theory]
    [InlineData("Unknown")][InlineData("Running")][InlineData("Completed")][InlineData("Created")]
    public void UnsafeStatesCannotBypassReviewGate(string state)
    {
        var r = new IntakeReview { State = state, Hash = OrveauxAocPairPolicy.KnownOriginalHash(true), Version = 4 };
        Assert.False(OrveauxPairReviewService.Eligible(r, true));
        Assert.Throws<IntakeConflictException>(() => OrveauxPairReviewService.Validate(r, new(r.Id, 4, r.Hash), true));
    }
    [Fact]
    public void VersionHashRoleAndUnknownResultAreStrict()
    {
        var r = new IntakeReview { State = "Held", Hash = OrveauxAocPairPolicy.KnownOriginalHash(true), Version = 4 };
        OrveauxPairReviewService.Validate(r, new(r.Id, 4, r.Hash), true);
        Assert.Throws<IntakeConflictException>(() => OrveauxPairReviewService.Validate(r, new(r.Id, 3, r.Hash), true));
        Assert.Throws<IntakeConflictException>(() => OrveauxPairReviewService.Validate(r, new(r.Id, 4, new string('0',64)), true));
        Assert.Throws<IntakeConflictException>(() => OrveauxPairReviewService.Validate(r, new(r.Id, 4, r.Hash), false));
        r.Result = new("x", "Held", "unknown", StopQueue: true);
        Assert.False(OrveauxPairReviewService.Eligible(r, true));
        Assert.Throws<ArgumentException>(() => OrveauxAocPairPolicy.KnownCandidate([1], [2]));
    }
    private sealed class NoBusinessStore : IInventoryStore
    {
        public Task<ImportResult> SaveAsync(SourceInput input,string hash,IReadOnlyList<SupplierInventoryRow> rows,CancellationToken ct) => throw new Exception("Unsupported source must not reach the business store.");
    }
    [OrveauxExactPairImportTests.ImportFact]
    public async Task UnsupportedFolderOriginalsRemainHeldAndFeedFixedPairWithReviewCas()
    {
        var connection = Environment.GetEnvironmentVariable("LIPS_AOC_IMPORT_CONNECTION")!;
        Assert.Equal("lips_aoc_import_20261013", new Npgsql.NpgsqlConnectionStringBuilder(connection).Database);
        var store = new IntakeReviewStore(connection);
        var bytes = new[] { "LIPS_AOC_EXCEL", "LIPS_AOC_PDF", "LIPS_AOC_ANSWERS" }.Select(k => File.ReadAllBytes(Environment.GetEnvironmentVariable(k)!)).ToArray();
        var received = new List<IntakeReview>();
        for (var i = 0; i < 2; i++)
        {
            var path = i == 0 ? "parent/オルヴォー/202610新着＆現在庫.xlsx" : "parent/オルヴォー/orveaux-public-202610.pdf";
            var r = await store.Receive(path, bytes[i], "2026-09-30", default);
            if (r.State == "Created") r = await store.Run(r,r.Version,r.Hash,true,
                (content,date,ct) => new FolderImportService(new NoBusinessStore()).ImportAsync(path,content,date,ct),default);
            Assert.Equal("Held",r.State); Assert.Contains(r.Result!.Status,new[]{"Held","Unsupported"}); Assert.False(r.Result.StopQueue);
            Assert.Equal(bytes[i],await store.Content(r,default)); received.Add(r);
        }
        var selection = new ExactPairReviewSelection(new(received[0].Id,received[0].Version,received[0].Hash),new(received[1].Id,received[1].Version,received[1].Hash));
        var found = await OrveauxPairReviewService.Discover(connection,default);
        Assert.Contains(found,r=>r.Id==received[0].Id); Assert.Contains(found,r=>r.Id==received[1].Id);
        var prepared = await OrveauxPairReviewService.Prepare(store,selection,default);
        Assert.Equal("ResolvedEvidenceOnly",OrveauxAocPairPolicy.Resolve(prepared.Candidate,prepared.Excel,prepared.Pdf,bytes[2]).Status);
        CatalogSearchDbContext Db()=>new(new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(connection).Options);
        await using(var db=Db())
        {
            var receipt=await new OrveauxExactPairImportStore(db).Import(Guid.NewGuid(),prepared.Candidate,prepared.Excel,prepared.Pdf,bytes[2],
                new(2026,9,30,0,0,0,TimeSpan.Zero),default,selection);
            Assert.Equal(0,receipt.AddedProducts);
        }
        await using(var db=Db())
            await Assert.ThrowsAsync<IntakeConflictException>(()=>new OrveauxExactPairImportStore(db).Import(Guid.NewGuid(),prepared.Candidate,prepared.Excel,prepared.Pdf,bytes[2],
                new(2026,9,30,0,0,0,TimeSpan.Zero),default,selection with {Excel=selection.Excel with {Version=1}}));
        foreach(var r in received) {var after=await store.Get(r.Id,default);Assert.Equal(r.Version,after!.Version);Assert.Equal("Held",after.State);}
    }
}
