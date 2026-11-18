using System.Text.Json;
using CatalogSearch.Application;

namespace CatalogSearch.Web;

public static class DateProposalEndpoints
{
    private static DateProposalRequest Fixture() => new(IntakeDateProposal.Schema,
        Guid.Parse("92aa25d9-60fb-4886-8b54-330ebd3e7a31"), new string('A',64), 3,
        IntakeDateProposal.Prompt, [new("e1", "合成原典!A1", "在庫日2026年10月1日")]);

    public static void MapDateProposalEndpoints(this WebApplication app)
    {
        // Explicit local verification gate; no model calls and no persistence.
        if (!app.Configuration.GetValue<bool>("DateProposalReview:Enabled")) return;
        app.MapGet("/api/date-proposal-review/request", () => Results.Ok(Fixture()));
        app.MapGet("/api/date-proposal-review/rule-example/{scenario}", (string scenario) =>
        {
            var scope=new IntakeRuleScope("SYNTHETIC_SUPPLIER", "synthetic-xlsx-v1", new string('B',64));
            var rule=new IntakeReusableRule(Guid.Parse("11aa25d9-60fb-4886-8b54-330ebd3e7a31"),1,scope,
                "VolumeUnit","容量列","L",Guid.Parse("22aa25d9-60fb-4886-8b54-330ebd3e7a31"),1,new string('A',64),"合成原典!H1 単位Lを確認");
            var query=new IntakeRuleQuery(scope,"VolumeUnit","容量列");
            var history=new List<IntakeReusableRule>{rule};
            switch(scenario)
            {
                case "same": break;
                case "supplier": query=query with {Scope=scope with {SupplierId="OTHER_SUPPLIER"}};break;
                case "layout": query=query with {Scope=scope with {ColumnsFingerprint=new string('C',64)}};break;
                case "revoked": history.Add(IntakeReusableRules.Amend(rule,1,"L",rule.AnswerId,2,rule.SourceHash,"合成取消確認",true));break;
                case "conflict": history.Add(rule with {Id=Guid.Parse("33aa25d9-60fb-4886-8b54-330ebd3e7a31"),Meaning="mL"});break;
                default:return Results.BadRequest();
            }
            var preview=IntakeReusableRules.Preview(query,history);
            return Results.Ok(new {preview,query,history,persisted=false,synthetic=true});
        });
        app.MapPost("/api/date-proposal-review/validate", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            var buffer = new char[16001];
            var length = await reader.ReadBlockAsync(buffer.AsMemory(), request.HttpContext.RequestAborted);
            if (length > 16000) return Results.BadRequest(new { error="候補応答が上限を超えています。" });
            try
            {
                var source=Fixture();
                var response=IntakeDateProposal.ReadResponse(source,new string(buffer,0,length));
                return Results.Ok(new { response, source.Excerpts, requiresHumanConfirmation=true, persisted=false });
            }
            catch (Exception e) when (e is ArgumentException or JsonException)
            { return Results.BadRequest(new { error="要求・原典識別子・版・日付・根拠の形式が一致しません。候補は表示しません。" }); }
        });
    }
}
