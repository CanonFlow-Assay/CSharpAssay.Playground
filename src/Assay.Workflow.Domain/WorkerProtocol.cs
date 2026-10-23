using System.Collections.Immutable;

namespace Assay.Workflow.Domain;

public sealed record WorkerScenario(string Id, string Variant, bool Passed, ImmutableArray<OperationTrace> Trace);
public sealed record WorkerCheck(string Id, bool Passed);
public sealed record WorkerExecution(string Schema, string ExecutionStatus,
    ImmutableArray<WorkerScenario> Scenarios, ImmutableArray<WorkerCheck> InfrastructureChecks,
    ContractSnapshot ObservedContract)
{
    public const string ProtocolSchema = "assay-worker-execution/0.1";
}
