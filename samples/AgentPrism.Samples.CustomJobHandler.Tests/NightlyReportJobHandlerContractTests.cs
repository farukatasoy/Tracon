using System.Reflection;
using AgentPrism.Testing.Contracts;
using AgentPrism.Testing.Contracts.Scheduling;

namespace AgentPrism.Samples.CustomJobHandler.Tests;

/// <summary>Runs the published job handler contract against the sample handler.</summary>
public sealed class NightlyReportJobHandlerContractTests : JobHandlerContract
{
    protected override string HandlerKey => NightlyReportJobHandlerRegistrationExtensions.HandlerKey;

    protected override ValueTask<IJobHandler> CreateHandlerAsync()
        => ValueTask.FromResult<IJobHandler>(new NightlyReportJobHandler());

    protected override JobItemRecord CreateItem(int sequence, JobItemStatus status)
        => new() { Id = Guid.NewGuid(), JobId = JobId, Seq = sequence, Input = $"customer-{sequence}", Status = status };
}

/// <summary>Prevents a new job handler contract from silently bypassing this sample.</summary>
public sealed class JobHandlerContractCoverageTests
{
    [Fact]
    public void Every_job_handler_contract_has_a_derived_test()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(), ContractCoverage.SchedulingContracts).ShouldBeEmpty();
}
