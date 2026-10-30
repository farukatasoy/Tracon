using Microsoft.Agents.AI.Workflows;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// BL-039: a duplicate code-defined workflow name used to throw a raw
/// <see cref="ArgumentException"/> from <see cref="Dictionary{TKey,TValue}.ToDictionary"/> - the
/// same defect <see cref="WorkflowFunctionRegistry"/> already avoided by rejecting a duplicate
/// with a normalized <see cref="TraconException"/>.
/// </summary>
public sealed class WorkflowCatalogTests
{
    /// <summary>
    /// <c>GET /api/workflows/functions</c> matches before
    /// <c>/api/workflows/{name}</c>, so a code workflow under that name could not be read back.
    /// </summary>
    [Theory]
    [InlineData("functions")]
    [InlineData("FUNCTIONS")]
    public void A_code_workflow_cannot_take_the_name_the_function_list_uses(string name)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var builder = services.AddTracon().UseWorkflows();

        Should.Throw<ArgumentException>(() => builder.AddWorkflow(name, static _ => new WorkflowBuilder(null!).Build()))
            .Message.ShouldContain("reserved");
    }

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
