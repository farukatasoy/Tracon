using System.Reflection;
using Microsoft.Extensions.Options;
using Tracon.Testing.Contracts;
using Tracon.Testing.Contracts.Scripts;

namespace Tracon.Core.UnitTests.Contracts;

/// <summary>
/// Runs the published <see cref="SkillScriptExecutorContract"/> against the
/// built-in executor, <see cref="SkillScriptProcessRunner"/>.
/// </summary>
/// <remarks>
/// The built-in executor starts a real <c>/bin/sh</c> process, so every scenario
/// is skipped on a machine without one.
/// </remarks>
public sealed class SkillScriptProcessRunnerContractTests : SkillScriptExecutorContract
{
    protected override string? SkipReason => File.Exists("/bin/sh") ? null : "/bin/sh not found.";

    protected override ValueTask<ISkillScriptExecutor> CreateExecutorAsync()
    {
        var options = new TraconOptions();
        options.Skills.Scripts.Enabled = true;
        options.Skills.Scripts.PlatformIsolationAcknowledged = true;
        options.Skills.Scripts.Interpreters["sh"] = "/bin/sh";

        return ValueTask.FromResult<ISkillScriptExecutor>(new SkillScriptProcessRunner(Options.Create(options)));
    }
}

/// <summary>Prevents a new script executor contract from silently going untested.</summary>
public sealed class SkillScriptExecutorContractCoverageTests
{
    [Fact]
    public void Every_script_executor_contract_has_a_derived_test()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(), ContractCoverage.ScriptContracts).ShouldBeEmpty();
}
