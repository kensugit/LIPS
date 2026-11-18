using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public class IntakeRepeatReceiptTests
{
    [Fact]
    public void CompletedAtReceiveShowsDuplicateWithoutMutatingSavedResultOrHistory()
    {
        var sourceId=Guid.NewGuid();
        var review=new IntakeReview {State="Completed",Version=7,Hash=new('A',64),Path="LUC/synthetic.xlsx",
            Result=new("LUC/synthetic.xlsx","Success","取込が完了しました。","LUC","2026-10-12",2,SourceDocumentId:sourceId)};
        review.History.Add(new(DateTimeOffset.UtcNow,"Rule","Completed","synthetic",null,7));
        var original=review.Result;var history=review.History.ToArray();
        var receipt=IntakeReviewStore.Receipt(review,completedAtReceive:true);
        Assert.True(receipt.AlreadyImported);Assert.Contains("取込済み",receipt.Message);
        Assert.Equal(sourceId,receipt.SourceDocumentId);Assert.Equal(2,receipt.Products);Assert.Equal(review.Id,receipt.ReviewId);
        Assert.Same(original,review.Result);Assert.False(review.Result.AlreadyImported);
        Assert.Equal(7,review.Version);Assert.Equal(history,review.History);
    }
    [Fact]
    public void FirstRunCompletingIsNotReportedAsDuplicate()
    {
        var review=new IntakeReview {State="Completed",Result=new("LUC/synthetic.xlsx","Success","初回保存",Products:2)};
        Assert.False(IntakeReviewStore.Receipt(review,completedAtReceive:false).AlreadyImported);
        Assert.Equal("初回保存",IntakeReviewStore.Receipt(review).Message);
    }
    [Theory]
    [InlineData("Held")][InlineData("Partial")][InlineData("Unknown")][InlineData("Running")][InlineData("Ready")]
    public void UncompletedStatesNeverBecomeDuplicate(string state)
    {
        var review=new IntakeReview {State=state,Result=new("LUC/synthetic.xlsx",state=="Partial"?"Partial":"Held","要確認",Products:state=="Partial"?1:0)};
        var receipt=IntakeReviewStore.Receipt(review,completedAtReceive:true);
        Assert.False(receipt.AlreadyImported);Assert.DoesNotContain("取込済み",receipt.Message);
        Assert.Equal(state is "Unknown" or "Running",receipt.StopQueue);
    }
}
