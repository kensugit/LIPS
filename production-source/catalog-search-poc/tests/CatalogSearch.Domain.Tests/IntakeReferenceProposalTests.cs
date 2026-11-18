using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using System.Text.Json;
using Xunit;

namespace CatalogSearch.Domain.Tests;
public class IntakeReferenceProposalTests
{
    [Fact] public void AnswerRetainsTypedReferenceWithoutMutatingOriginal()
    {
        var r=new IntakeReview { Hash="H",Path="LUC/test.xlsx",State="Held",Result=new("LUC/test.xlsx","Held","date",QuestionKind:"Date") };
        var reference=new IntakeSourceReference("xlsx","原典",95);
        IntakeReviewStore.ApplyAnswer(r,new(1,"H","date","2026-10-01","本人確認",reference));
        Assert.Equal(reference,Assert.Single(r.History).SourceReference);Assert.Equal("H",r.Hash);Assert.Equal("Ready",r.State);
    }
    [Theory] [InlineData(0)] [InlineData(1048577)]
    public void InvalidExcelRowRejected(int row)=>Assert.Throws<ArgumentException>(()=>new IntakeSourceReference("xlsx","原典",row).Validate());
    [Fact] public void PdfBoundsValidated()
    {
        new IntakeSourceReference("pdf",Page:1,X:1,Y:2,Right:30,Bottom:40,Width:100,Height:100).Validate();
        Assert.Throws<ArgumentException>(()=>new IntakeSourceReference("pdf",Page:1,X:1,Y:2,Right:130,Bottom:40,Width:100,Height:100).Validate());
    }
    private static DateProposalRequest Request()=>new(IntakeDateProposal.Schema,Guid.NewGuid(),new string('A',64),3,IntakeDateProposal.Prompt,[new("e1","Sheet1!A1","在庫日2026年10月1日")]);
    private static DateProposalResponse Response(DateProposalRequest r)=>new(r.Schema,r.RequestId,r.SourceHash,r.ReviewVersion,r.PromptVersion,"offline-fixture","synthetic","Candidates",[new("2026-10-01","原典の在庫日",["e1"])]);
    private static string Serialize(object r)=>JsonSerializer.Serialize(r,new JsonSerializerOptions(JsonSerializerDefaults.Web));
    [Fact] public void ProposalIsValidatedButDoesNotBecomeAnswer()
    {var r=Request();Assert.Single(IntakeDateProposal.ReadResponse(r,Serialize(Response(r))).Candidates);}
    [Fact] public void UnknownIsValidAndDoesNotInventDate()
    {var r=Request();Assert.Empty(IntakeDateProposal.ReadResponse(r,Serialize(Response(r) with {Outcome="Unknown",Candidates=[]})).Candidates);}
    [Fact] public void WrongOriginalVersionAndEvidenceRejected()
    {
        var r=Request();var p=Response(r);
        Assert.Throws<ArgumentException>(()=>IntakeDateProposal.ReadResponse(r,Serialize(p with {SourceHash=new string('B',64)})));
        Assert.Throws<ArgumentException>(()=>IntakeDateProposal.ReadResponse(r,Serialize(p with {ReviewVersion=2})));
        Assert.Throws<ArgumentException>(()=>IntakeDateProposal.ReadResponse(r,Serialize(p with {Candidates=[new("2026-10-01","理由",["invented"])]})));
    }
    [Fact] public void InvalidDatesAndUnknownExecutableFieldsRejected()
    {
        var r=Request();var p=Response(r);
        Assert.Throws<ArgumentException>(()=>IntakeDateProposal.ReadResponse(r,Serialize(p with {Candidates=[new("2026-02-30","理由",["e1"])]})));
        Assert.Throws<JsonException>(()=>IntakeDateProposal.ReadResponse(r,Serialize(p).TrimEnd('}')+",\"execute\":\"import\"}"));
    }
    [Fact] public void RequestSizeIsBounded()
    {var r=Request();Assert.Throws<ArgumentException>(()=>IntakeDateProposal.ValidateRequest(r with{Excerpts=[new("e1","Sheet1!A1",new string('x',1001))]}));}
}
