using System.Net;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The check at the A2A and MCP surfaces. The file tools of a session workspace are not in the tool
/// registry (the framework adds them when it builds the agent), so the check reads the definition's workspace settings,
/// as it reads the skill settings.
/// </summary>
/// <remarks>
/// The tools that change a file always ask for approval, and an external caller is not a person who can answer. Only a
/// workspace that cannot change a file, and that asks for no approval to read one, can be exposed.
/// </remarks>
public sealed class SessionWorkspaceExposureTests
{
    public static TheoryData<bool, bool, bool> RefusedSettings => new()
    {
        { true, false, false },
        { true, false, true },
        { true, true, false },
    };

    [Theory]
    [MemberData(nameof(RefusedSettings))]
    public async Task A_workspace_that_can_change_a_file_or_asks_to_read_one_cannot_be_exposed_via_A2A(bool enabled, bool readOnly, bool disableReadApproval)
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddAgent(TestData.Definition() with { Workspace = new WorkspaceSettings { Enabled = enabled, ReadOnly = readOnly, DisableReadApproval = disableReadApproval } })
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapTraconA2A());

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => host.Client.GetAsync(
            new Uri("/tracon/a2a/kod-agent/.well-known/agent-card.json", UriKind.Relative)));

        exception.Message.ShouldContain("kod-agent", Case.Sensitive);
        exception.Message.ShouldContain("session workspace", Case.Sensitive);
        exception.Message.ShouldContain("Workspace.ReadOnly", Case.Sensitive);
        exception.Message.ShouldContain("Workspace.DisableReadApproval", Case.Sensitive);
    }

    [Theory]
    [MemberData(nameof(RefusedSettings))]
    public async Task A_workspace_that_can_change_a_file_or_asks_to_read_one_cannot_be_exposed_via_MCP(bool enabled, bool readOnly, bool disableReadApproval)
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddAgent(TestData.Definition() with { Workspace = new WorkspaceSettings { Enabled = enabled, ReadOnly = readOnly, DisableReadApproval = disableReadApproval } })
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapTraconMcpServer());

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => McpTestClient.SendAsync(host.Client, "/tracon/mcp", "tools/list"));

        exception.Message.ShouldContain("kod-agent", Case.Sensitive);
        exception.Message.ShouldContain("session workspace", Case.Sensitive);
    }

    [Fact]
    public async Task A_read_only_workspace_that_asks_for_no_approval_is_exposed_via_A2A()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddAgent(TestData.Definition() with { Workspace = new WorkspaceSettings { Enabled = true, ReadOnly = true, DisableReadApproval = true } })
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapTraconA2A());

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/a2a/kod-agent/.well-known/agent-card.json", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_read_only_workspace_that_asks_for_no_approval_is_exposed_via_MCP()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddAgent(TestData.Definition() with { Workspace = new WorkspaceSettings { Enabled = true, ReadOnly = true, DisableReadApproval = true } })
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapTraconMcpServer());

        var (response, body) = await McpTestClient.SendAsync(host.Client, "/tracon/mcp", "tools/list");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body?.ToString());
    }

    [Fact]
    public async Task A_workspace_that_is_off_does_not_stop_the_host_from_exposing_the_agent()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddAgent(TestData.Definition() with { Workspace = new WorkspaceSettings { Enabled = false } })
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapTraconA2A());

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/a2a/kod-agent/.well-known/agent-card.json", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_writable_workspace_on_an_agent_that_is_not_on_the_allow_list_does_not_stop_the_host()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddAgent(TestData.Definition())
                .AddAgent(TestData.Definition("drafter") with { Workspace = new WorkspaceSettings { Enabled = true } })
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapTraconA2A());

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/a2a/kod-agent/.well-known/agent-card.json", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
