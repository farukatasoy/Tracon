using Microsoft.Agents.AI.Workflows;
using NSubstitute;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// <see cref="TraconWorkflowContextExtensions.GetRunParameters"/> outside and inside a run scope.
/// </summary>
public sealed class TraconWorkflowContextExtensionsTests
{
    [Fact]
    public void Outside_a_run_it_refuses_instead_of_returning_an_empty_set()
    {
        // An empty dictionary would hide a wiring mistake: a test that forgot to
        // open a run would read "no values" and pass.
        TraconRunContext.SetCurrent(null);

        var context = Substitute.For<IWorkflowContext>();

        Should.Throw<InvalidOperationException>(() => context.GetRunParameters())
            .Message.ShouldContain("outside a Tracon workflow run", Case.Sensitive);
    }

    [Fact]
    public void Inside_a_run_it_returns_the_scopes_values_and_an_empty_set_when_there_are_none()
    {
        var context = Substitute.For<IWorkflowContext>();

        try
        {
            TraconRunContext.SetCurrent(new AgentRunScope { RunId = Guid.NewGuid(), RootRunId = Guid.NewGuid() });

            context.GetRunParameters().Count.ShouldBe(0);

            TraconRunContext.SetCurrent(new AgentRunScope
            {
                RunId = Guid.NewGuid(),
                RootRunId = Guid.NewGuid(),
                ParameterValues = new Dictionary<string, string>(StringComparer.Ordinal) { ["voice"] = "alloy" },
            });

            context.GetRunParameters()["voice"].ShouldBe("alloy");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    [Fact]
    public void A_null_context_is_rejected()
        => Should.Throw<ArgumentNullException>(() => TraconWorkflowContextExtensions.GetRunParameters(null!));
}
