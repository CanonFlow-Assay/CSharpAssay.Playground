namespace Playground.DecisionAssist.Domain;

public enum VerificationVerdict { Pass, Fail, Inconclusive, ToolFailure }
public enum NextAction { InspectAssertion, InspectReachability, ReviewContract, HumanReview }
public enum Observation { Unknown, Present, Absent }
public enum FeedbackTone { Concise, Coaching, Firm }

public readonly union ExperimentState(BaselineFailure, InvalidMutation, IncompleteTestRun, KilledMutation, SurvivingMutation);
public readonly record struct BaselineFailure;
public readonly record struct InvalidMutation;
public readonly record struct IncompleteTestRun;
public readonly record struct KilledMutation;
public readonly record struct SurvivingMutation;

public sealed record MutationEvidence(
    string SourceSha256,
    ExperimentState State,
    Observation Covered,
    Observation HasBoundaryAssertion,
    VerificationVerdict VerificationVerdict);

public readonly union Advice(Recommendation, ReviewRequired, AdviceUnavailable);
public sealed record Recommendation(NextAction Action, string Basis);
public sealed record ReviewRequired(string Reason);
public sealed record AdviceUnavailable(string Reason);

public static class Assistance
{
    public static ExperimentState Observe(bool baselinePassed, bool mutantCompiled, bool testsCompleted, bool survived) =>
        !baselinePassed ? new BaselineFailure() :
        !mutantCompiled ? new InvalidMutation() :
        !testsCompleted ? new IncompleteTestRun() :
        survived ? new SurvivingMutation() : new KilledMutation();

    public static bool Eligible(MutationEvidence evidence) => evidence.State is SurvivingMutation;

    public static Advice Baseline(MutationEvidence evidence) => evidence.State switch
    {
        BaselineFailure => new ReviewRequired("The original baseline did not pass."),
        InvalidMutation => new ReviewRequired("The mutation did not compile."),
        IncompleteTestRun => new ReviewRequired("The selected tests did not complete."),
        KilledMutation => new ReviewRequired("No surviving mutant requires triage."),
        SurvivingMutation => Prioritize(evidence)
    };

    private static Advice Prioritize(MutationEvidence evidence) => evidence switch
    {
        { Covered: Observation.Absent } => new Recommendation(NextAction.InspectReachability, "observed-no-coverage"),
        { HasBoundaryAssertion: Observation.Absent } => new Recommendation(NextAction.InspectAssertion, "declared-boundary-test-gap"),
        _ => new Recommendation(NextAction.ReviewContract, "equivalence-and-intent-need-review")
    };

    public static Advice Suggest(MutationEvidence evidence, NextAction action) =>
        Eligible(evidence)
            ? new Recommendation(action, "unqualified-provider-suggestion")
            : Baseline(evidence);

    public static string ActionText(NextAction action) => action switch
    {
        NextAction.InspectAssertion => "Inspect the zero-boundary assertion.",
        NextAction.InspectReachability => "Check whether the selected tests exercise the mutated path.",
        NextAction.ReviewContract => "Review the expected behavior and possible equivalent behavior.",
        NextAction.HumanReview => "Ask a reviewer for missing context.",
        _ => "Ask a reviewer to validate the unknown action."
    };

    public static string Explain(Advice advice, FeedbackTone tone) => advice switch
    {
        Recommendation suggestion => tone switch
        {
            FeedbackTone.Coaching => ActionText(suggestion.Action) + " Compare the original and mutated behavior before changing tests.",
            FeedbackTone.Firm => "Attention: a mutant survived the selected tests. " + ActionText(suggestion.Action) + " Keep the survivor visible until evidence resolves it.",
            _ => ActionText(suggestion.Action)
        },
        ReviewRequired review => "Review required: " + review.Reason,
        AdviceUnavailable unavailable => "Optional advice unavailable: " + unavailable.Reason
    };
}
