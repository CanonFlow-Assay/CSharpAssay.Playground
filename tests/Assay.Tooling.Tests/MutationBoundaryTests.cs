namespace Assay.Tooling.Tests;

public sealed class MutationBoundaryTests
{
    private static JsonNode Suite => Json.Create(("tests", 2), ("policyCalls", 2));

    private static readonly string[] expectedArray = new[] { "CS1525" };
    private static readonly string[] expected = new[] { "CS1525" };

    [Fact]
    public void ZeroTestsCannotMasqueradeAsSelectedSuite()
    {
        _ = Assert.Throws<InvalidDataException>(() => DecisionExperiments.ProbeObservation(new(ProcessState.Passed, 0, /*lang=json,strict*/ "{\"tests\":0,\"failures\":0,\"policyCalls\":0}"), Suite));
    }

    [Fact]
    public void DisagreeingExitIsNotSurvivor()
    {
        _ = Assert.Throws<InvalidDataException>(() => DecisionExperiments.ProbeObservation(new(ProcessState.Passed, 0, /*lang=json,strict*/ "{\"tests\":2,\"failures\":1,\"policyCalls\":2}"), Suite));
    }

    [Fact]
    public void BooleanCountsRejected()
    {
        _ = Assert.Throws<InvalidDataException>(() => DecisionExperiments.ProbeObservation(new(ProcessState.Passed, 0, /*lang=json,strict*/ "{\"tests\":true,\"failures\":0,\"policyCalls\":1}"), Suite));
    }

    [Fact]
    public void ProbeCrashNotKilledMutation()
    {
        _ = Assert.Throws<InvalidDataException>(() => DecisionExperiments.ProbeObservation(new(ProcessState.Failed, 137, ""), Suite));
    }

    [Fact]
    public void InfrastructureFailureNotInvalidMutation()
    {
        foreach (var result in new[] { new Execution(ProcessState.Failed, 1, "error NU1301: unavailable"), new Execution(ProcessState.Failed, 137, ""), new Execution(ProcessState.Failed, 1, "Build failed without diagnostics") })
        {
            _ = Assert.Throws<InvalidDataException>(() => DecisionExperiments.Compilation(result));
        }
    }
    [Fact] public void CompletedCompilerErrorIsInvalid() { var result = DecisionExperiments.Compilation(new(ProcessState.Failed, 1, "error CS1525: invalid expression")); Assert.Equal("invalid", result.S("status")); Assert.Equal(expected, result.Strings("diagnostics")); }
    [Fact] public void UnrelatedCompilerErrorsRemainVisible() { var result = DecisionExperiments.Compilation(new(ProcessState.Failed, 1, "error CS9999: unrelated")); Assert.NotEqual(expectedArray, result.Strings("diagnostics")); }
    [Fact]
    public void TimeoutCannotBecomeCompilerInvalid()
    {
        _ = Assert.Throws<InvalidDataException>(() => DecisionExperiments.Compilation(new(ProcessState.TimedOut, 137, "error CS1525: partial")));
    }
}
