using Tracon.Workflows.UnitTests.Fakes;
using Microsoft.Agents.AI.Workflows;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// BL-039 (phase 122): a duplicate code-defined workflow name used to throw a raw
/// <see cref="ArgumentException"/> from <see cref="Dictionary{TKey,TValue}.ToDictionary"/> - the
/// same defect <see cref="WorkflowFunctionRegistry"/> already avoided by rejecting a duplicate
/// with a normalized <see cref="TraconException"/>.
/// </summary>
public sealed class WorkflowCatalogTests
{
    [Fact]
    public void Duplicate_code_workflow_names_throw_an_TraconException()
    {
        var host = new WorkflowTestHost("alpha");

        var first = new CodeWorkflowRegistration("shared", "first", static _ => new WorkflowBuilder(null!).Build());
        var second = new CodeWorkflowRegistration("shared", "second", static _ => new WorkflowBuilder(null!).Build());

        var exception = Should.Throw<TraconException>(() => host.CreateRunner(codeWorkflows: [first, second]));

        exception.Message.ShouldContain("shared");
    }

    [Fact]
    public async Task Distinct_code_workflow_names_are_all_listed()
    {
        var host = new WorkflowTestHost("alpha");

        var first = new CodeWorkflowRegistration("first", "first workflow", static _ => new WorkflowBuilder(null!).Build());
        var second = new CodeWorkflowRegistration("second", "second workflow", static _ => new WorkflowBuilder(null!).Build());

        var runner = host.CreateRunner(codeWorkflows: [first, second]);

        var names = (await runner.ListAsync()).Select(static descriptor => descriptor.Name);

        names.ShouldBe(["first", "second"]);
    }
}
