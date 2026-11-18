using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public class IntakeRulePublicationTests
{
    [Fact] public void RegionPublicationRequiresAllRowsOfExactAbbreviationNotAnotherAbbreviation()
    {
        var scope=new IntakeRuleScope("LA_VINEE","LaVinee/1",new('B',64));
        IntakeMappingQuestion Region(string id,int row,string raw) => new(id,scope,"RegionAbbreviation","C/"+raw,
            "地域を確認","原典",row,"SYNTH-"+row,raw,["アルザス","ブルゴーニュ"]);
        var q=new[]{Region("al1",11,"AL"),Region("al2",12,"AL"),Region("bg1",13,"BG")};
        var r=new IntakeReview{State="Held",Version=1,Result=new("LA_VINEE/test.xlsx","Held","地域確認",QuestionKind:"Mapping",Questions:q)};
        void Answer(string id,string value) { IntakeReviewStore.ApplyAnswer(r,new(r.Version,r.Hash,"mapping",null,"合成原典の対応確認",MappingAnswers:[new(id,value)]));r.Version++; }
        Answer("al1","アルザス");
        Assert.Throws<ArgumentException>(()=>IntakeRulePublication.Validate(r,q,"al1",2));
        Answer("al2","アルザス");
        Assert.Equal("アルザス",IntakeRulePublication.Validate(r,q,"al1",2).Resolution.Meaning);
        Answer("bg1","ブルゴーニュ");
        Assert.Equal("ブルゴーニュ",IntakeRulePublication.Validate(r,q,"bg1",4).Resolution.Meaning);
        Assert.Equal("アルザス",IntakeRulePublication.Validate(r,q,"al1",2).Resolution.Meaning);
    }
    private static IntakeMappingQuestion Q(string id,int row) => new(id,new("LUC","LUC/1",new('A',64)),"VolumeUnit","容量","列全体を確認","在庫リスト",row,"SYNTH-"+row,"750",["mL","cL","L"]);
    private static (IntakeReview Review,IntakeMappingQuestion[] Questions) Fixture(bool complete=true)
    {
        var questions=new[]{Q("q1",6),Q("q2",7)};
        var review=new IntakeReview{State="Held",Version=1,Result=new("LUC/test.xlsx","Held","単位確認",QuestionKind:"Mapping",Questions:questions)};
        void Answer(IntakeMappingQuestion question)
        {
            IntakeReviewStore.ApplyAnswer(review,new(review.Version,review.Hash,"mapping",null,"合成確認",MappingAnswers:[new(question.Id,"mL")]));
            review.Version++;
        }
        Answer(questions[0]);if(complete)Answer(questions[1]);
        return(review,questions);
    }
    [Fact] public void PartialOneOfTwoIsRejected()
    {var(r,q)=Fixture(false);Assert.Throws<ArgumentException>(()=>IntakeRulePublication.Validate(r,q,"q1",2));}
    [Fact] public void AllRowsCanBeSavedInSeparateAnswerVersions()
    {var(r,q)=Fixture();var result=IntakeRulePublication.Validate(r,q,"q1",2);Assert.Equal("mL",result.Resolution.Meaning);Assert.Equal(2,result.Answer.Version);}
    [Fact] public void CompletedUsesOriginalSetWhenResultNoLongerHasQuestions()
    {
        var(r,q)=Fixture();r.State="Completed";r.Result=new(r.Path,"Success","取込済み");
        Assert.Equal("q1",IntakeRulePublication.Validate(r,q,"q1",2).Resolution.Question.Id);
        r.MappingResolutions.RemoveAt(1);Assert.Throws<ArgumentException>(()=>IntakeRulePublication.Validate(r,q,"q1",2));
    }
    [Fact] public void CompletedCannotHideMissingOriginalQuestionInBothHistoryAndCurrentAnswers()
    {
        var(r,q)=Fixture(false);r.State="Completed";r.Result=new(r.Path,"Success","synthetic");
        Assert.Throws<ArgumentException>(()=>IntakeRulePublication.Validate(r,q,"q1",2));
    }
    [Theory][InlineData("Unknown")][InlineData("Running")][InlineData("Created")]
    public void UnconfirmedStatesReject(string state)
    {var(r,q)=Fixture();r.State=state;Assert.Throws<ArgumentException>(()=>IntakeRulePublication.Validate(r,q,"q1",2));}
    [Fact] public void ConflictingLatestSavedRowMeaningsReject()
    {
        var(r,q)=Fixture();var changed=r.MappingResolutions[1] with {Meaning="L"};
        r.MappingResolutions[1]=changed;r.History[1]=r.History[1] with {MappingResolutions=[changed]};
        Assert.Throws<ArgumentException>(()=>IntakeRulePublication.Validate(r,q,"q1",2));
    }
    [Fact] public void StaleSelectedAnswerRejectsAfterCorrection()
    {
        var(r,q)=Fixture();IntakeReviewStore.ApplyAnswer(r,new(r.Version,r.Hash,"mapping",null,"再確認",MappingAnswers:[new("q1","mL")]));r.Version++;
        Assert.Throws<ArgumentException>(()=>IntakeRulePublication.Validate(r,q,"q1",2));
        Assert.Equal(4,IntakeRulePublication.Validate(r,q,"q1",4).Answer.Version);
    }
    [Fact] public void UnsavedOrAlteredCurrentAnswerRejects()
    {var(r,q)=Fixture();r.MappingResolutions[1]=r.MappingResolutions[1] with {Evidence="未保存編集"};Assert.Throws<ArgumentException>(()=>IntakeRulePublication.Validate(r,q,"q1",2));}
    [Fact] public void OriginalScopeAndRowIdentityMustMatchSavedAnswers()
    {var(r,q)=Fixture();q[1]=q[1] with {Sku="OTHER"};Assert.Throws<ArgumentException>(()=>IntakeRulePublication.Validate(r,q,"q1",2));}
    [Fact] public void UnrelatedScopeDoesNotRequireAnswers()
    {var(r,q)=Fixture();var other=Q("other",8) with {Scope=q[0].Scope with {SupplierId="OTHER"}};Assert.NotNull(IntakeRulePublication.Validate(r,[..q,other],"q1",2).Answer);}
}
