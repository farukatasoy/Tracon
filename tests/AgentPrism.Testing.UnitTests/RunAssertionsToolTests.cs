using AgentPrism.Testing;

namespace AgentPrism.Testing.UnitTests;

public sealed class RunAssertionsToolTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void ShouldHaveCalledTool_en_az_bir_cagrida_gecer()
    {
        var assertions = Create([Invocation("get_order_status"), Invocation("get_order_status")]);

        Should.NotThrow(() => assertions.ShouldHaveCalledTool("get_order_status"));
    }

    [Fact]
    public void ShouldHaveCalledTool_hic_cagrilmadiysa_duser()
    {
        var assertions = Create([]);

        var exception = Should.Throw<AgentPrismAssertionException>(
            () => assertions.ShouldHaveCalledTool("get_order_status"));

        exception.Message.ShouldContain("get_order_status", Case.Sensitive);
    }

    [Fact]
    public void ShouldHaveCalledTool_times_dogru_sayidaysa_gecer()
    {
        var assertions = Create([Invocation("get_order_status"), Invocation("get_order_status")]);

        Should.NotThrow(() => assertions.ShouldHaveCalledTool("get_order_status", times: 2));
    }

    [Fact]
    public void ShouldHaveCalledTool_times_yanlis_sayidaysa_beklenen_ve_bulunani_yazarak_duser()
    {
        var assertions = Create([Invocation("get_order_status")]);

        var exception = Should.Throw<AgentPrismAssertionException>(
            () => assertions.ShouldHaveCalledTool("get_order_status", times: 2));

        exception.Message.ShouldContain("2", Case.Sensitive);
        exception.Message.ShouldContain("1", Case.Sensitive);
    }

    [Fact]
    public void ShouldNotHaveCalledTool_cagrilmadiysa_gecer()
    {
        var assertions = Create([]);

        Should.NotThrow(() => assertions.ShouldNotHaveCalledTool("delete_account"));
    }

    [Fact]
    public void ShouldNotHaveCalledTool_cagrildiysa_duser()
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
