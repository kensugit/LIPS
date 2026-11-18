using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;
using Npgsql;

namespace CatalogSearch.Domain.Tests;
public sealed class RuleStoreFactAttribute : FactAttribute
{
    public RuleStoreFactAttribute()
    { if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIPS_RULE_TEST_CONNECTION")))Skip="Set LIPS_RULE_TEST_CONNECTION to the isolated rule test database after explicitly applying rule SQL."; }
}
public class IntakeReusableRuleStoreTests
{
    [RuleStoreFact] public async Task ExplicitRevokePreservesEvidenceAndIsIdempotentAfterLaterVersions()
    {
        var store=Store();var rule=Rule();await store.Save(new(Guid.NewGuid(),0,rule));var operation=Guid.NewGuid();
        var receipts=await Task.WhenAll(store.Revoke(rule.Id,operation,1,"合成取消理由"),store.Revoke(rule.Id,operation,1,"合成取消理由"));
        Assert.Equal(receipts[0],receipts[1]);var revoked=receipts[0].Rule;
        Assert.True(revoked.Revoked);Assert.Equal("合成取消理由",revoked.CancellationReason);
        Assert.Equal(rule with {Version=2,Revoked=true,CancellationReason="合成取消理由"},revoked);
        Assert.Equal("Held",IntakeReusableRules.Preview(new(rule.Scope,rule.Kind,rule.SourceKey),await store.List()).Status);
        Assert.Equal(new[]{rule,revoked},await store.History(rule.Id));
        await Assert.ThrowsAsync<IntakeRuleConflictException>(()=>store.Revoke(rule.Id,operation,1,"異なる理由"));
        await Assert.ThrowsAsync<IntakeRuleConflictException>(()=>store.Revoke(rule.Id,Guid.NewGuid(),1,"古い版"));
        await Assert.ThrowsAsync<IntakeRuleConflictException>(()=>store.Revoke(rule.Id,Guid.NewGuid(),2,"重複取消"));
        var next=rule with {Version=3};await store.Save(new(Guid.NewGuid(),2,next));
        Assert.Equal(receipts[0],await store.Revoke(rule.Id,operation,1,"合成取消理由"));
        Assert.Equal(next,await store.Get(rule.Id));Assert.Equal(3,(await store.History(rule.Id)).Count);
    }
    [RuleStoreFact] public async Task RevokeInvalidRequestsAndStaleVersionDoNotWrite()
    {
        var store=Store();var rule=Rule();await store.Save(new(Guid.NewGuid(),0,rule));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.Revoke(rule.Id,Guid.Empty,1,"reason"));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.Revoke(rule.Id,Guid.NewGuid(),1,""));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.Revoke(rule.Id,Guid.NewGuid(),1,"line\nbreak"));
        await Assert.ThrowsAsync<IntakeRuleConflictException>(()=>store.Revoke(rule.Id,Guid.NewGuid(),2,"stale"));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>store.Revoke(Guid.NewGuid(),Guid.NewGuid(),1,"missing"));
        Assert.Single(await store.History(rule.Id));
    }
    [Fact] public void NullCancellationReasonDoesNotChangeLegacyRequestJson()
    {
        var rule=Rule();var json=System.Text.Json.JsonSerializer.Serialize(new IntakeRuleSaveRequest(Guid.NewGuid(),0,rule),new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.DoesNotContain("cancellationReason",json);
        Assert.Throws<ArgumentException>(()=>IntakeReusableRules.Validate(rule with {CancellationReason="not revoked"}));
    }
    [RuleStoreFact] public async Task PublishHoldsReviewLockUntilRuleCommit()
    {
        var connection=Environment.GetEnvironmentVariable("LIPS_RULE_TEST_CONNECTION")!;
        var review=await new IntakeReviewStore(connection).Receive("SYNTHETIC/publish-"+Guid.NewGuid()+".xlsx",[4,5,6],null,default);
        var rule=Rule() with { AnswerId=review.Id, SourceHash=review.Hash };
        var publishName="publish-"+Guid.NewGuid(); var updateName="update-"+Guid.NewGuid();
        string Named(string name)=>new NpgsqlConnectionStringBuilder(connection){ApplicationName=name}.ConnectionString;
        await using var blocker=new NpgsqlConnection(connection);await blocker.OpenAsync();
        await using var tx=await blocker.BeginTransactionAsync();
        // Uncommitted unique key blocks only the final INSERT, after the review guard.
        await using(var insert=new NpgsqlCommand("INSERT INTO \"IntakeReusableRuleVersions\" (\"RuleId\",\"Version\",\"OperationId\",\"RequestHash\",\"ExpectedVersion\",\"Body\") VALUES (@id,1,@op,@hash,0,'{}')",blocker,tx))
        {insert.Parameters.AddWithValue("id",rule.Id);insert.Parameters.AddWithValue("op",Guid.NewGuid());insert.Parameters.AddWithValue("hash",new string('A',64));await insert.ExecuteNonQueryAsync();}
        async Task WaitLock(string name)
        {
            await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
            for(var n=0;n<100;n++)
            {
                await using var cmd=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name=@app AND wait_event_type='Lock')",observer);
                cmd.Parameters.AddWithValue("app",name);if((bool)(await cmd.ExecuteScalarAsync())!)return;
                await Task.Delay(20);
            }
            Assert.Fail("Expected PostgreSQL lock wait: "+name);
        }
        var store=new IntakeReusableRuleStore(Named(publishName));
        var request=new IntakeRuleSaveRequest(Guid.NewGuid(),0,rule);
        var save=store.Save(request,default,new(review.Id,1,review.Hash));
        await WaitLock(publishName);
        await using var updater=new NpgsqlConnection(Named(updateName));await updater.OpenAsync();
        await using var update=new NpgsqlCommand("UPDATE \"IntakeReviews\" SET \"Version\"=2, \"Body\"=jsonb_set(\"Body\",'{version}','2') WHERE \"Id\"=@id",updater);
        update.Parameters.AddWithValue("id",review.Id);var updateTask=update.ExecuteNonQueryAsync();
        await WaitLock(updateName);
        await tx.RollbackAsync();
        var receipt=await save;Assert.Equal(1,await updateTask);
        Assert.Equal(receipt,await store.GetOperation(request.OperationId));
        Assert.Equal(receipt,await store.Save(request,default,new(review.Id,1,review.Hash)));
        Assert.Single(await store.History(rule.Id));
    }
    [RuleStoreFact] public async Task ReviewUpdateWinningRaceRejectsStalePublishAfterRowLockWait()
    {
        var connection = Environment.GetEnvironmentVariable("LIPS_RULE_TEST_CONNECTION")!;
        var reviewStore = new IntakeReviewStore(connection);
        var review = await reviewStore.Receive("SYNTHETIC/guard-"+Guid.NewGuid()+".xlsx", [1,2,3], null, default);
        var rule = Rule() with { AnswerId=review.Id, SourceHash=review.Hash };
        var appName="rule-guard-"+Guid.NewGuid();
        var cs = new NpgsqlConnectionStringBuilder(connection) { ApplicationName=appName }.ConnectionString;
        await using var owner=new NpgsqlConnection(connection); await owner.OpenAsync();
        await using var tx=await owner.BeginTransactionAsync();
        await using (var update=new NpgsqlCommand("UPDATE \"IntakeReviews\" SET \"Version\"=2, \"Body\"=jsonb_set(jsonb_set(\"Body\",'{version}','2'),'{state}','\"Running\"') WHERE \"Id\"=@id",owner,tx))
        { update.Parameters.AddWithValue("id",review.Id); await update.ExecuteNonQueryAsync(); }
        var store=new IntakeReusableRuleStore(cs);
        var operation=Guid.NewGuid();
        var save=store.Save(new(operation,0,rule),default,new(review.Id,1,review.Hash));
        // Observe PostgreSQL's lock wait, not elapsed time, before releasing the updater.
        await using var observer=new NpgsqlConnection(connection); await observer.OpenAsync();
        var waiting=false;
        for(var n=0;n<100&&!waiting;n++)
        {
            await using var check=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name=@app AND wait_event_type='Lock')",observer);
            check.Parameters.AddWithValue("app",appName);waiting=(bool)(await check.ExecuteScalarAsync())!;
            if(!waiting)await Task.Delay(20);
        }
        Assert.True(waiting,"Publish must wait on the source review row lock.");
        await tx.CommitAsync();
        await Assert.ThrowsAsync<IntakeRuleConflictException>(()=>save);
        Assert.Null(await store.GetOperation(operation)); Assert.Null(await store.Get(rule.Id));
    }
    private static IntakeReusableRuleStore Store()=>new(Environment.GetEnvironmentVariable("LIPS_RULE_TEST_CONNECTION")!);
    private static IntakeReusableRule Rule()=>new(Guid.NewGuid(),1,new("SYNTHETIC","Inventory",Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N")),"VolumeUnit","H","L",Guid.NewGuid(),1,new('B',64),"Synthetic Sheet1 H1 human answer");
    [RuleStoreFact] public async Task ConcurrentNewIdentitiesForOneScopeHaveOnlyOneWinner()
    {
        var store=Store();var rule=Rule();
        var requests=Enumerable.Range(0,4).Select(_=>new IntakeRuleSaveRequest(Guid.NewGuid(),0,rule with {Id=Guid.NewGuid()})).ToArray();
        var start=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<IntakeRuleReceipt?> Save(IntakeRuleSaveRequest request)
        {await start.Task;try{return await store.Save(request);}catch(IntakeRuleConflictException){return null;}}
        var tasks=requests.Select(Save).ToArray();start.SetResult(true);
        var result=await Task.WhenAll(tasks);var winner=Assert.Single(result,x=>x is not null)!;
        Assert.Single(await store.List(),r=>r.Scope==rule.Scope);
        foreach(var request in requests)
            if(request.OperationId==winner.OperationId)Assert.Equal(winner,await store.Save(request));
            else Assert.Null(await store.GetOperation(request.OperationId));
        var amendment=IntakeReusableRules.Amend(winner.Rule,1,"mL",Guid.NewGuid(),2,rule.SourceHash,"Synthetic correction");
        await store.Save(new(Guid.NewGuid(),1,amendment));
        Assert.Equal(2,(await store.History(winner.Rule.Id)).Count);
    }
    [RuleStoreFact] public async Task RevokedLatestScopeRejectsNewIdentityButPreservesHistory()
    {
        var store=Store();var rule=Rule();await store.Save(new(Guid.NewGuid(),0,rule));
        var revoked=IntakeReusableRules.Amend(rule,1,"L",Guid.NewGuid(),2,rule.SourceHash,"Synthetic revoked",true);
        await store.Save(new(Guid.NewGuid(),1,revoked));
        await Assert.ThrowsAsync<IntakeRuleConflictException>(()=>store.Save(new(Guid.NewGuid(),0,rule with {Id=Guid.NewGuid()})));
        Assert.Equal(revoked,await store.Get(rule.Id));Assert.Equal(new[]{rule,revoked},await store.History(rule.Id));
    }
    [RuleStoreFact] public async Task DifferentExactScopeOrColumnCanCreateSeparateIdentity()
    {
        var store=Store();var rule=Rule();await store.Save(new(Guid.NewGuid(),0,rule));
        var variants=new[]{rule with {Scope=rule.Scope with {SupplierId="SYNTHETIC-OTHER"}},
            rule with {Scope=rule.Scope with {DocumentKind="Other"}},rule with {SourceKey="OtherColumn"},
            rule with {Scope=rule.Scope with {ProductIdentity="SYNTHETIC-P",Vintage="NV"}}};
        foreach(var variant in variants){var next=variant with {Id=Guid.NewGuid()};await store.Save(new(Guid.NewGuid(),0,next));Assert.Equal(next,await store.Get(next.Id));}
    }
    [RuleStoreFact] public async Task SaveReloadEditCancelRetainsImmutableHistoryAndOperationReceipt()
    {
        var store=Store();var r=Rule();var request=new IntakeRuleSaveRequest(Guid.NewGuid(),0,r);
        var receipt=await store.Save(request);Assert.Equal(r,await store.Get(r.Id));
        Assert.Equal(receipt,await store.GetOperation(request.OperationId));
        Assert.Equal(receipt,await store.Save(request));Assert.Single(await store.History(r.Id));
        await Assert.ThrowsAsync<IntakeRuleConflictException>(()=>store.Save(request with {Rule=r with {Meaning="mL"}}));
        var edit=IntakeReusableRules.Amend(r,1,"mL",Guid.NewGuid(),2,r.SourceHash,"Synthetic correction");
        await store.Save(new(Guid.NewGuid(),1,edit));
        var cancel=IntakeReusableRules.Amend(edit,2,"mL",Guid.NewGuid(),3,r.SourceHash,"Synthetic withdrawal",true);
        await store.Save(new(Guid.NewGuid(),2,cancel));
        Assert.Equal(new[]{r,edit,cancel},await store.History(r.Id));
        Assert.Contains(cancel,await store.List());Assert.Equal(cancel,await store.Get(r.Id));
        Assert.Equal("Held",IntakeReusableRules.Preview(new(r.Scope,r.Kind,r.SourceKey),await store.History(r.Id)).Status);
        // Simulate a caller that lost its response: exact retry remains the original receipt after later edits.
        Assert.Equal(receipt,await store.Save(request));Assert.Equal(3,(await store.History(r.Id)).Count);
    }
    [RuleStoreFact] public async Task ConcurrentCasHasOneWinnerAndDuplicateOperationIsIdempotent()
    {
        var store=Store();var r=Rule();var initial=new IntakeRuleSaveRequest(Guid.NewGuid(),0,r);
        var duplicates=await Task.WhenAll(store.Save(initial),store.Save(initial));Assert.Equal(duplicates[0],duplicates[1]);
        async Task<bool> Write(string unit)
        {
            try {await store.Save(new(Guid.NewGuid(),1,IntakeReusableRules.Amend(r,1,unit,Guid.NewGuid(),2,r.SourceHash,"Synthetic race")));return true;}
            catch(IntakeRuleConflictException){return false;}
        }
        Assert.Single(await Task.WhenAll(Write("mL"),Write("cL")),x=>x);
        Assert.Equal(2,(await store.History(r.Id)).Count);
    }
    [RuleStoreFact] public async Task InvalidEvidenceAndScopeChangesCannotWrite()
    {
        var store=Store();var r=Rule();await store.Save(new(Guid.NewGuid(),0,r));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.Save(new(Guid.NewGuid(),1,r with {Version=2,Evidence=""})));
        await Assert.ThrowsAsync<IntakeRuleConflictException>(()=>store.Save(new(Guid.NewGuid(),1,r with {Version=2,Scope=r.Scope with {SupplierId="OTHER"}})));
        Assert.Single(await store.History(r.Id));Assert.Null(await store.GetOperation(Guid.NewGuid()));
    }
}
