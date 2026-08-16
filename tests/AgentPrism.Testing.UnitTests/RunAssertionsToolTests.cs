using AgentPrism.Testing;

namespace AgentPrism.Testing.UnitTests;

public sealed class RunAssertionsToolTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void ShouldHaveCalledTool_passes_when_called_at_least_once()
    {
        var assertions = Create([Invocation("get_order_status"), Invocation("get_order_status")]);

        Should.NotThrow(() => assertions.ShouldHaveCalledTool("get_order_status"));
    }

    [Fact]
    public void ShouldHaveCalledTool_fails_when_never_called()
    {
        var assertions = Create([]);

        var exception = Should.Throw<AgentPrismAssertionException>(
            () => assertions.ShouldHaveCalledTool("get_order_status"));

        exception.Message.ShouldContain("get_order_status", Case.Sensitive);
    }

    [Fact]
    public void ShouldHaveCalledTool_passes_when_times_matches()
    {
        var assertions = Create([Invocation("get_order_status"), Invocation("get_order_status")]);

        Should.NotThrow(() => assertions.ShouldHaveCalledTool("get_order_status", times: 2));
    }

    [Fact]
    public void ShouldHaveCalledTool_fails_with_the_expected_and_actual_count_when_times_does_not_match()
    {
        var assertions = Create([Invocation("get_order_status")]);

        var exception = Should.Throw<AgentPrismAssertionException>(
            () => assertions.ShouldHaveCalledTool("get_order_status", times: 2));

        exception.Message.ShouldContain("2", Case.Sensitive);
        exception.Message.ShouldContain("1", Case.Sensitive);
    }

    [Fact]
    public void ShouldNotHaveCalledTool_passes_when_never_called()
    {
        var assertions = Create([]);

        Should.NotThrow(() => assertions.ShouldNotHaveCalledTool("delete_account"));
    }

    [Fact]
    public void ShouldNotHaveCalledTool_fails_when_called()
    {
        var assertions = Create([Invocation("delete_account")]);

        Should.Throw<AgentPrismAssertionException>(() => assertions.ShouldNotHaveCalledTool("delete_account"));
    }

    private static ToolInvocationRecord Invocation(string toolName)
        => new() { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), ToolName = toolName, CreatedAt = Now };

    private static RunAssertions Create(IReadOnlyList<ToolInvocationRecord> invocations)
    {
        var record = new RunRecord
        {
            Id = Guid.NewGuid(),
            AgentName = "test-agent",
            Status = RunStatus.Completed,
            StartedAt = Now,
        };

        return new RunAssertions(record, [], invocations);
    }
}
