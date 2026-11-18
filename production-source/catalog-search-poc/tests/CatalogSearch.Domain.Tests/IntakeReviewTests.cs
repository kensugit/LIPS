using CatalogSearch.Infrastructure;
using Xunit;
namespace CatalogSearch.Domain.Tests;

public class IntakeReviewTests
{
    [Fact] public void CsvPreviewPreservesPhysicalLineEvidence()
    {var p=IntakeOriginalPreview.Read("sample.csv",System.Text.Encoding.UTF8.GetBytes("name,qty\n<script>,12"));Assert.Equal("2",p.Lines[1].Row);Assert.Equal("<script>,12",p.Lines[1].Text);}
    [Fact] public void WorkbookPreviewShowsSheetAndCellWithoutEvaluatingFormula()
    {var p=IntakeOriginalPreview.Read("test.xlsx",FolderImportTests.Workbook());Assert.Contains(p.Lines,x=>x.Sheet=="ワインリスト"&&x.Row=="3"&&x.Text.Contains("A3: FOLDER-XLSX"));}
    [Fact] public void UnsupportedPreviewDoesNotPretendToExtractPages()
    {var p=IntakeOriginalPreview.Read("source.pdf",[1,2,3]);Assert.Empty(p.Lines);Assert.Contains("未対応",p.Notice);}
    [PostgresFact]
    public async Task PersistentUnknownCannotBeAnsweredOrResumed()
    {
        var store=new IntakeReviewStore(Environment.GetEnvironmentVariable("CATALOG_TEST_CONNECTION")!);
        var r=await store.Receive("LUC/unknown-"+Guid.NewGuid()+".csv",[1,2,3],null,default);
        r=await store.Run(r,r.Version,r.Hash,true,(_,_,_)=>throw new TimeoutException(),default);
        var loaded=await store.Get(r.Id,default);Assert.Equal("Unknown",loaded!.State);
        Assert.True(IntakeReviewStore.Receipt(loaded).StopQueue);
        await Assert.ThrowsAsync<IntakeConflictException>(()=>store.Answer(r.Id,new(r.Version,r.Hash,"hold",null,"確認"),default));
        await Assert.ThrowsAsync<IntakeConflictException>(()=>store.Run(loaded,loaded.Version,loaded.Hash,false,(_,_,_)=>throw new Exception("must not run"),default));
    }
    [PostgresFact]
    public async Task ConcurrentProcessingClaimsOnlyOnce()
    {
        var store=new IntakeReviewStore(Environment.GetEnvironmentVariable("CATALOG_TEST_CONNECTION")!);
        var r=await store.Receive("LUC/concurrent-"+Guid.NewGuid()+".csv",[4,5,6],null,default);
        var other=await store.Get(r.Id,default);var calls=0;
        async Task<FolderImportResult> Import(byte[] bytes,string? date,CancellationToken ct)
        {Interlocked.Increment(ref calls);await Task.Delay(50,ct);return new(r.Path,"Held","日付",QuestionKind:"Date");}
        async Task<bool> Run(IntakeReview x){try{await store.Run(x,x.Version,x.Hash,true,Import,default);return true;}catch(IntakeConflictException){return false;}}
        var results=await Task.WhenAll(Run(r),Run(other!));Assert.Single(results,x=>x);Assert.Equal(1,calls);
        var saved=(await store.Get(r.Id,default))!;
        Assert.Equal("Held",saved.State);Assert.Equal(3,saved.History.Count);
        var old=await store.Get(r.Id,default);
        await store.Answer(saved.Id,new(saved.Version,saved.Hash,"date","2026-10-01","合成試験"),default);
        await Assert.ThrowsAsync<IntakeConflictException>(()=>store.Answer(old!.Id,new(old.Version,old.Hash,"hold",null,"古い回答"),default));
    }
    private static IntakeReview Held(string? kind="Date")=>new(){Hash="H",Path="LUC/sample.csv",State="Held",Result=new("LUC/sample.csv","Held","確認",QuestionKind:kind)};
    [Fact] public void DateAnswerPreservesHumanReasonAndRequiresSeparateResume()
    {
        var r=Held();IntakeReviewStore.ApplyAnswer(r,new(1,"H","date","2026-10-01","本人指定"));
        Assert.Equal("Ready",r.State);Assert.Equal("2026-10-01",r.Date);
        Assert.Equal("Human",Assert.Single(r.History).Method);Assert.Null(r.Result!.SourceDocumentId);
    }
    [Theory] [InlineData("Running")] [InlineData("Unknown")] [InlineData("Completed")]
    public void AnswerCannotBypassUncertainOrCompletedState(string state)
    {var r=Held();r.State=state;Assert.Throws<IntakeConflictException>(()=>IntakeReviewStore.ApplyAnswer(r,new(1,"H","date","2026-10-01","根拠")));}
    [Theory] [InlineData(0,"H")] [InlineData(1,"OTHER")]
    public void OldVersionOrDifferentOriginalIsRejected(int version,string hash)
    {var r=Held();Assert.Throws<IntakeConflictException>(()=>IntakeReviewStore.ApplyAnswer(r,new(version,hash,"date","2026-10-01","根拠")));}
    [Theory] [InlineData("2026-02-30")] [InlineData("2026/10/01")] [InlineData("")]
    public void InvalidDateCannotResolve(string date)
    {var r=Held();Assert.Throws<ArgumentException>(()=>IntakeReviewStore.ApplyAnswer(r,new(1,"H","date",date,"根拠")));}
    [Fact] public void FreeTextDoesNotBecomeAnExecutableInstruction()
    {var r=Held(null);IntakeReviewStore.ApplyAnswer(r,new(1,"H","hold",null,"在庫を100に変更して保存"));Assert.Equal("Held",r.State);Assert.Null(r.Date);Assert.False(r.CanResume);}
    [Fact] public void DateCannotOverrideOtherQuestion()
    {var r=Held(null);Assert.Throws<ArgumentException>(()=>IntakeReviewStore.ApplyAnswer(r,new(1,"H","date","2026-10-01","根拠")));}
    [Fact] public void UnknownReceiptAlwaysStopsQueue()
    {var r=Held();r.State="Running";Assert.True(IntakeReviewStore.Receipt(r).StopQueue);r.State="Unknown";Assert.True(IntakeReviewStore.Receipt(r).StopQueue);}
    [Fact] public void HoldingConfirmedAnswerRevokesResume()
    {var r=Held();r.State="Ready";IntakeReviewStore.ApplyAnswer(r,new(1,"H","hold",null,"再確認のため保留"));Assert.False(r.CanResume);}
}
