using System.Security.Cryptography;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace CatalogSearch.Domain.Tests;
public sealed class QuestionRefreshDbFactAttribute:FactAttribute
{
    public QuestionRefreshDbFactAttribute(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LIPS_REFRESH_TEST_CONNECTION")))Skip="Dedicated synthetic question refresh database required.";}
}
public class QuestionRefreshTests
{
    [QuestionRefreshDbFact] public async Task DedicatedDatabasePersistsRefreshAndNeverTouchesProducts()
    {
        var connection=Environment.GetEnvironmentVariable("LIPS_REFRESH_TEST_CONNECTION")!;
        var builder=new NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("lips_question_refresh_20261012",builder.Database);Assert.Equal("127.0.0.1",builder.Host);Assert.Equal(55993,builder.Port);
        await using var c=new NpgsqlConnection(connection);await c.OpenAsync();
        async Task<long> Products(){await using var cmd=new NpgsqlCommand("SELECT count(*) FROM \"SupplierProducts\"",c);return (long)(await cmd.ExecuteScalarAsync())!;}
        var count=await Products();Assert.Equal(0,count);
        var(old,bytes)=Old();var store=new IntakeReviewStore(connection);
        var received=await store.Receive("ラヴィネ/refresh-synthetic-"+Guid.NewGuid()+".xlsx",bytes,null,default);
        old.Id=received.Id;old.Path=received.Path;old.Result=old.Result! with {Path=old.Path,ReviewId=old.Id};
        await using(var seed=new NpgsqlCommand("UPDATE \"IntakeReviews\" SET \"Version\"=4,\"Body\"=@body WHERE \"Id\"=@id AND \"Version\"=1",c))
        {seed.Parameters.AddWithValue("id",old.Id);seed.Parameters.AddWithValue("body",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(old,new JsonSerializerOptions(JsonSerializerDefaults.Web)));Assert.Equal(1,await seed.ExecuteNonQueryAsync());}
        var operation=Guid.NewGuid();var updated=await store.RefreshQuestions(old.Id,4,old.Hash,operation,default);
        var saved=(await store.Get(old.Id,default))!;Assert.Equal(5,saved.Version);Assert.Equal("Held",saved.State);
        Assert.Equal(6,saved.Result!.Questions!.Count);Assert.Equal(3,saved.MappingResolutions.Count);Assert.Equal(operation,saved.History[^1].OperationId);
        Assert.Equal(bytes,await store.Content(saved,default));Assert.Equal(count,await Products());
        await Assert.ThrowsAsync<IntakeConflictException>(()=>store.RefreshQuestions(old.Id,4,old.Hash,Guid.NewGuid(),default));
        var unchanged=await store.RefreshQuestions(old.Id,5,old.Hash,Guid.NewGuid(),default);Assert.Equal(5,unchanged.Version);Assert.Equal(2,unchanged.History.Count);
        var proof=Environment.GetEnvironmentVariable("LIPS_REFRESH_TEST_PROOF");
        if(!string.IsNullOrEmpty(proof))await File.WriteAllTextAsync(proof,JsonSerializer.Serialize(new{reviewId=old.Id,operationId=operation,version=5,state=saved.State,questions=6,answers=3,unknownRegions=1,productsBefore=count,productsAfter=await Products(),sourceHash=old.Hash,originalUnchanged=true,staleRejected=true,noOpVersion=unchanged.Version}));
    }
    private static (IntakeReview Review,byte[] Bytes) Old()
    {
        var bytes=LaVineeMappingTests.Workbook(region:"XX");
        var old=LaVineeMappingReview.Inspect(bytes).Where(q=>q.Kind!="ColumnMeaning").ToArray();
        var answers=old.Where(q=>q.Choices.Count>0).Select(q=>new IntakeMappingResolution(q,q.Choices[0],"合成保存回答")).ToList();
        var r=new IntakeReview{Version=4,Hash=Convert.ToHexString(SHA256.HashData(bytes)),State="Held",Path="ラヴィネ/synthetic.xlsx",
            Result=new("ラヴィネ/synthetic.xlsx","Held","old",QuestionKind:"Mapping",Questions:old),MappingResolutions=answers};
        r.History.Add(new(DateTimeOffset.UtcNow,"Human","mapping","合成保存回答",null,4,MappingResolutions:answers.ToArray()));
        return(r,bytes);
    }
    [Fact] public void OldHeldQuestionsRefreshAddsPriceAndPreservesUnknownHumanAnswersAndOriginal()
    {
        var(r,b)=Old();var before=b.ToArray();var human=r.History[0];var operation=Guid.NewGuid();
        Assert.True(IntakeReviewStore.ApplyQuestionRefresh(r,4,r.Hash,operation,b));
        Assert.Equal(6,r.Result!.Questions!.Count);Assert.Equal(3,r.MappingResolutions.Count);Assert.Equal("Held",r.State);
        Assert.Contains(r.Result.Questions,q=>q.SourceKey=="C/XX"&&q.Choices.Count==0);
        Assert.Equal(2,r.Result.Questions.Count(q=>q.Kind=="ColumnMeaning"));Assert.Same(human,r.History[0]);Assert.Equal(before,b);
        var e=r.History[1];Assert.Equal(operation,e.OperationId);Assert.Equal(5,e.Version);Assert.Equal("RefreshQuestions",e.Action);
    }
    [Fact] public void ChangedQuestionRemovesActiveAnswerButRetainsHistorySnapshot()
    {
        var(r,b)=Old();var answer=r.MappingResolutions[0];r.MappingResolutions[0]=answer with {Question=answer.Question with {Prompt="旧文言"}};
        IntakeReviewStore.ApplyQuestionRefresh(r,4,r.Hash,Guid.NewGuid(),b);
        Assert.DoesNotContain(r.MappingResolutions,a=>a.Question.Id==answer.Question.Id);
        Assert.Contains(r.History[^1].MappingResolutions!,a=>a.Question.Prompt=="旧文言");Assert.Equal(2,r.History.Count);
    }
    [Theory][InlineData("Unknown")][InlineData("Running")][InlineData("Completed")][InlineData("Partial")]
    public void UnsafeStatesRejected(string state)
    {
        var(r,b)=Old();r.State=state;Assert.Throws<IntakeConflictException>(()=>IntakeReviewStore.ApplyQuestionRefresh(r,4,r.Hash,Guid.NewGuid(),b));Assert.Single(r.History);
    }
    [Fact] public void StaleVersionAndHashRejected()
    {
        var(r,b)=Old();Assert.Throws<IntakeConflictException>(()=>IntakeReviewStore.ApplyQuestionRefresh(r,3,r.Hash,Guid.NewGuid(),b));
        Assert.Throws<IntakeConflictException>(()=>IntakeReviewStore.ApplyQuestionRefresh(r,4,"wrong",Guid.NewGuid(),b));
        Assert.Throws<InvalidDataException>(()=>IntakeReviewStore.ApplyQuestionRefresh(r,4,r.Hash,Guid.NewGuid(),[1,2]));Assert.Single(r.History);
    }
    [Fact] public void RepeatedUnchangedRefreshDoesNotAppendHistoryOrChangeVersion()
    {
        var(r,b)=Old();IntakeReviewStore.ApplyQuestionRefresh(r,4,r.Hash,Guid.NewGuid(),b);r.Version=5;
        Assert.False(IntakeReviewStore.ApplyQuestionRefresh(r,5,r.Hash,Guid.NewGuid(),b));Assert.Equal(2,r.History.Count);Assert.Equal(5,r.Version);
    }
    [Fact] public void ReadyOldReviewBecomesHeldForNewUnansweredPrice()
    {
        var(r,b)=Old();r.State="Ready";IntakeReviewStore.ApplyQuestionRefresh(r,4,r.Hash,Guid.NewGuid(),b);Assert.Equal("Held",r.State);
    }
    [Fact] public void NonMappingAndRepeatedOperationRejected()
    {
        var(r,b)=Old();var operation=Guid.NewGuid();IntakeReviewStore.ApplyQuestionRefresh(r,4,r.Hash,operation,b);
        Assert.Throws<ArgumentException>(()=>IntakeReviewStore.ApplyQuestionRefresh(r,4,r.Hash,operation,b));
        r.Result=r.Result! with {QuestionKind="Rows"};Assert.Throws<IntakeConflictException>(()=>IntakeReviewStore.ApplyQuestionRefresh(r,4,r.Hash,Guid.NewGuid(),b));
    }
}
