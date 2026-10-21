using Assay.Workflow.Domain;
using Xunit;

namespace Assay.Workflow.Tests;

public sealed class ModuleTests
{
    private static ContractSnapshot Baseline => new([new("Status", "System.String", true)], ["success", "timeout"]);

    [Fact]
    public void ContractIdentityAndOptionalAdditionsAreCompatible()
    {
        Assert.Empty(ContractAssay.Compare(Baseline, Baseline));
        Assert.Empty(ContractAssay.Compare(Baseline, Baseline with { Members = [.. Baseline.Members, new("Optional", "System.String", false)] }));
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("type")]
    [InlineData("required")]
    [InlineData("new-required")]
    [InlineData("outcome")]
    public void ContractRegressionsRemainVisible(string change)
    {
        var candidate = change switch
        {
            "removed" => Baseline with { Members = [] },
            "type" => Baseline with { Members = [new("Status", "System.Int32", true)] },
            "required" => Baseline with { Members = [new("Status", "System.String", false)] },
            "new-required" => Baseline with { Members = [.. Baseline.Members, new("New", "System.String", true)] },
            _ => Baseline with { Outcomes = ["success"] }
        };
        Assert.NotEmpty(ContractAssay.Compare(Baseline, candidate));
    }

    [Fact]
    public void DuplicateContractsCannotMasqueradeAsCompatibility() => Assert.NotEmpty(ContractAssay.Compare(Baseline, Baseline with { Members = [Baseline.Members[0], Baseline.Members[0]] }));

    [Fact]
    public void RiskRankingExplainsUnknownCoverageAndUsesStableTies()
    {
        var ranking = CodeRiskAssay.Rank([new("z", false, false, 0, 1, 0), new("critical", true, true, 30, 12, 1), new("a", false, false, 0, 1, 0)]);
        Assert.Equal("critical", ranking[0].Id);
        Assert.Contains("surviving-mutants", ranking[0].Reasons);
        Assert.Equal("a", ranking[1].Id);
        Assert.Contains("coverage-unknown", ranking[1].Reasons);
    }

    [Fact]
    public void TraceChecksIndependentDeclaredInvariants()
    {
        OperationTrace[] correct = [new("op", 1, 1, 0, TraceKind.Attempt), new("op", 2, 1, 1, TraceKind.Success)];
        Assert.Empty(ResilienceAssay.Check([.. correct], new(3, 100, false, true)));
        Assert.NotEmpty(ResilienceAssay.Check([], new(3, 100, false, true)));
        Assert.NotEmpty(ResilienceAssay.Check([new("op", 2, 1, 101, TraceKind.Attempt)], new(3, 100, false, true)));
        Assert.NotEmpty(ResilienceAssay.Check([new("op", 1, 1, 0, TraceKind.PermanentFailure), new("op", 2, 2, 1, TraceKind.Attempt)], new(3, 100, false, true)));
        Assert.NotEmpty(ResilienceAssay.Check([new("op", 1, 1, 0, TraceKind.Cancelled), new("op", 2, 2, 1, TraceKind.Attempt)], new(3, 100, false, true)));
        Assert.NotEmpty(ResilienceAssay.Check([new("op", 1, 1, 0, TraceKind.SideEffect), new("op", 2, 1, 1, TraceKind.SideEffect)], new(3, 100, false, true)));
        Assert.NotEmpty(ResilienceAssay.Check([new("op", 1, 1, 0, TraceKind.Fallback)], new(3, 100, false, true)));
    }
}
