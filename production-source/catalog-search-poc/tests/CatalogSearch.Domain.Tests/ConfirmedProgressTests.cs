using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;
public class ConfirmedProgressTests
{
    private static FolderImportResult Previous(string supplier="LA_VINEE")=>new("supplier/source.xlsx","Partial","saved",SupplierCode:supplier,Products:1,SourceDocumentId:Guid.NewGuid());
    [Theory][InlineData("Mapping")][InlineData("Date")][InlineData("Rows")]
    public void ValidationWithoutBusinessWritePreservesPriorAcceptance(string kind)
    {
        var before=Previous();var next=new FolderImportResult(before.Path,"Held","unresolved",QuestionKind:kind);
        var result=IntakeReviewStore.PreserveConfirmedProgress(before,next);
        Assert.Equal("Partial",result.Status);Assert.Equal(1,result.Products);Assert.Equal(before.SourceDocumentId,result.SourceDocumentId);
        Assert.Equal(kind,result.QuestionKind);Assert.Equal(0,result.AddedProducts);Assert.False(result.AlreadyImported);
    }
    [Fact] public void UnknownNeverClaimsAdditionalProductsAndReceiptKeepsConfirmedCount()
    {
        var before=Previous();var unknown=before with {StopQueue=true,Products=4,AddedProducts=3,Status="Success"};
        var result=IntakeReviewStore.PreserveConfirmedProgress(before,unknown);
        Assert.True(result.StopQueue);Assert.Equal("Held",result.Status);Assert.Equal(1,result.Products);Assert.Equal(0,result.AddedProducts);
        var receipt=IntakeReviewStore.Receipt(new(){State="Unknown",Path=before.Path,Result=result});
        Assert.True(receipt.StopQueue);Assert.Equal(1,receipt.Products);Assert.Equal(before.SourceDocumentId,receipt.SourceDocumentId);
    }
    [Fact] public void DifferentSourceIsNotMergedOrDeclaredSuccessful()
    {
        var before=Previous();var result=IntakeReviewStore.PreserveConfirmedProgress(before,before with {SourceDocumentId=Guid.NewGuid(),Products=2,Status="Success"});
        Assert.True(result.StopQueue);Assert.Equal(before.SourceDocumentId,result.SourceDocumentId);Assert.Equal(1,result.Products);
    }
    [Theory][InlineData("LA_VINEE")][InlineData("ENEOS")]
    public void ConfirmedSameSourceCanAdvanceAndComplete(string supplier)
    {
        var before=Previous(supplier);var next=before with {Status="Success",Products=2,AddedProducts=1};
        Assert.Same(next,IntakeReviewStore.PreserveConfirmedProgress(before,next));
    }
    [Fact] public void UnconfirmedInitialAttemptDoesNotInventSavedCount()
    {
        var next=new FolderImportResult("file","Held","failed",StopQueue:true);
        Assert.Same(next,IntakeReviewStore.PreserveConfirmedProgress(null,next));
        Assert.Same(next,IntakeReviewStore.PreserveConfirmedProgress(Previous() with {SourceDocumentId=null},next));
    }
    [Fact] public void RepeatedCompletedReceiptDoesNotClaimPreviousAddition()
    {
        var result=Previous() with {Status="Success",Products=2,AddedProducts=1};
        var review=new IntakeReview{State="Completed",Path=result.Path,Result=result};
        var repeated=IntakeReviewStore.Receipt(review,true);
        Assert.True(repeated.AlreadyImported);Assert.Equal(0,repeated.AddedProducts);Assert.Equal(2,repeated.Products);
        Assert.Equal(1,review.Result.AddedProducts);Assert.Equal(1,IntakeReviewStore.Receipt(review).AddedProducts);
    }
}
