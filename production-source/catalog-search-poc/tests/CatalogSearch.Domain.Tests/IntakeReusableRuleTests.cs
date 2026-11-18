using CatalogSearch.Application;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public class IntakeReusableRuleTests
{
    private static IntakeReusableRule Rule() => new(Guid.NewGuid(), 1, new("S1", "Inventory", new('A',64)),
        "VolumeUnit", "H", "L", Guid.NewGuid(), 1, new('B',64), "Sheet1!H1; explicit human confirmation");
    private static IntakeRuleQuery Query(IntakeReusableRule r) => new(r.Scope,r.Kind,r.SourceKey);

    [Fact] public void SameFormatNextDocumentReturnsTraceableCandidateOnly()
    {
        var r=Rule(); var p=IntakeReusableRules.Preview(Query(r),[r]);
        Assert.Equal("Candidate",p.Status); Assert.Equal(r,p.Rule); Assert.Equal(r.AnswerId,p.Rule!.AnswerId);
    }
    [Theory] [InlineData("supplier")] [InlineData("document")] [InlineData("columns")]
    public void ChangedScopeIsHeld(string change)
    {
        var r=Rule(); var scope=change switch {"supplier"=>r.Scope with {SupplierId="S2"},"document"=>r.Scope with {DocumentKind="Reservation"},_=>r.Scope with {ColumnsFingerprint=new('C',64)}};
        Assert.Equal("Held",IntakeReusableRules.Preview(Query(r) with {Scope=scope},[r]).Status);
    }
    [Theory] [InlineData("Date")] [InlineData("StockQuantity")] [InlineData("Price")] [InlineData("Unknown")]
    public void ActualValueKindsAreRejected(string kind) => Assert.Throws<ArgumentException>(()=>IntakeReusableRules.Validate(Rule() with {Kind=kind,Meaning="123"}));
    [Theory] [InlineData("VolumeUnit")] [InlineData("ColumnMeaning")]
    public void NumericActualValueIsRejected(string kind) => Assert.Throws<ArgumentException>(()=>IntakeReusableRules.Validate(Rule() with {Kind=kind,Meaning="750"}));
    [Fact] public void AliasRequiresExactIdentityAndVintage()
    {
        var r=Rule() with {Kind="ProductAlias",SourceKey="Source name",Meaning="Confirmed name"};
        Assert.Throws<ArgumentException>(()=>IntakeReusableRules.Validate(r));
        r=r with {Scope=r.Scope with {ProductIdentity="SKU1",Vintage="NV"}};
        Assert.Equal("Candidate",IntakeReusableRules.Preview(Query(r),[r]).Status);
        Assert.Equal("Held",IntakeReusableRules.Preview(Query(r) with {Scope=r.Scope with {Vintage="2025"}},[r]).Status);
        Assert.Equal("Held",IntakeReusableRules.Preview(Query(r) with {Scope=r.Scope with {ProductIdentity="SKU2"}},[r]).Status);
    }
    [Fact] public void AmendmentAndRevocationPreserveOldVersionAndPreventFallback()
    {
        var r=Rule(); var next=IntakeReusableRules.Amend(r,1,"mL",Guid.NewGuid(),2,r.SourceHash,"Corrected human evidence");
        Assert.Equal("L",r.Meaning); Assert.Equal(1,r.Version);
        Assert.Equal(next,IntakeReusableRules.Preview(Query(r),[r,next]).Rule);
        var revoked=IntakeReusableRules.Amend(next,2,"mL",Guid.NewGuid(),3,r.SourceHash,"Human withdrawal",true);
        Assert.Equal("Held",IntakeReusableRules.Preview(Query(r),[r,next,revoked]).Status);
        Assert.Throws<InvalidOperationException>(()=>IntakeReusableRules.Amend(next,1,"L",r.AnswerId,2,r.SourceHash,"Stale"));
    }
    [Fact] public void DuplicateReceiptIsIdempotentButConflictingVersionsAndRulesHold()
    {
        var r=Rule(); Assert.Equal("Candidate",IntakeReusableRules.Preview(Query(r),[r,r]).Status);
        Assert.Equal("Held",IntakeReusableRules.Preview(Query(r),[r,r with {Meaning="mL"}]).Status);
        Assert.Equal("Held",IntakeReusableRules.Preview(Query(r),[r,r with {Id=Guid.NewGuid()}]).Status);
    }
    [Fact] public void MissingEvidenceAndChangedVersionScopeHold()
    {
        var r=Rule(); Assert.Equal("Held",IntakeReusableRules.Preview(Query(r),[r with {Evidence=""}]).Status);
        Assert.Equal("Held",IntakeReusableRules.Preview(Query(r),[r,r with {Version=2,Scope=r.Scope with {SupplierId="S2"}}]).Status);
        Assert.Equal("Held",IntakeReusableRules.Preview(Query(r),[r with {AnswerId=Guid.Empty}]).Status);
    }
}
