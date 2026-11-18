using System.Security.Cryptography;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;
namespace CatalogSearch.Domain.Tests;
public class PartialMappingStateTests
{
    private static IntakeReview Review()
    {
        var bytes=LaVineeMappingTests.Workbook();var questions=LaVineeMappingReview.Inspect(bytes);
        return new(){Hash=Convert.ToHexString(SHA256.HashData(bytes)),Version=3,State="Partial",
            Result=new("ラヴィネ/synthetic.xlsx","Partial","合成",SupplierCode:"LA_VINEE",Products:1,QuestionKind:"Mapping",Questions:questions)};
    }
    [Fact] public void PartialAnswerRetainsAcceptedCountAndPartialState()
    {
        var r=Review();var q=r.Result!.Questions![0];
        IntakeReviewStore.ApplyAnswer(r,new(r.Version,r.Hash,"mapping",null,"合成回答",MappingAnswers:[new(q.Id,q.Choices[0])]));
        Assert.Equal("Partial",r.State);Assert.Equal(1,r.Result.Products);Assert.True(r.CanResumeResolved);Assert.False(r.CanResume);
        Assert.Single(r.History);Assert.Single(r.MappingResolutions);
    }
    [Theory][InlineData("Unknown")][InlineData("Running")][InlineData("Completed")][InlineData("Created")]
    public void UnsafeStatesNeverAllowPartialResume(string state)
    {
        var r=Review();var q=r.Result!.Questions![0];r.MappingResolutions.Add(new(q,q.Choices[0],"合成回答"));r.State=state;
        Assert.False(r.CanResumeResolved);
    }
    [Fact] public void OnlySupportedSupplierCanRequestResolvedRows()
    {
        var r=Review();Assert.True(r.CanResumeResolved); // Service may resolve rows from existing reusable rules.
        var q=r.Result!.Questions![0];r.MappingResolutions.Add(new(q,q.Choices[0],"合成回答"));
        r.Result=r.Result with {SupplierCode="LUC"};Assert.False(r.CanResumeResolved);
    }
    [Fact] public void HoldDoesNotHidePreviouslyAcceptedRows()
    {
        var r=Review();IntakeReviewStore.ApplyAnswer(r,new(r.Version,r.Hash,"hold",null,"合成保留"));
        Assert.Equal("Partial",r.State);Assert.Equal(1,r.Result!.Products);
    }
}
