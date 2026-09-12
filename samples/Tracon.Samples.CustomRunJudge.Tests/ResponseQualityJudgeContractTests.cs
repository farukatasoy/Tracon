using System.Reflection;
using Tracon.Testing.Contracts;
using Tracon.Testing.Contracts.Judges;

namespace Tracon.Samples.CustomRunJudge.Tests;

/// <summary>Runs the published judge contract against the sample judge.</summary>
public sealed class ResponseQualityJudgeContractTests : RunJudgeContract
{
    protected override ValueTask<IRunJudge> CreateJudgeAsync()
        => ValueTask.FromResult<IRunJudge>(new ResponseQualityJudge());
}

/// <summary>Prevents a new judge contract from silently bypassing this sample.</summary>
public sealed class JudgeContractCoverageTests
{
    [Fact]
    public void Every_judge_contract_has_a_derived_test()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(), ContractCoverage.JudgeContracts).ShouldBeEmpty();
}
