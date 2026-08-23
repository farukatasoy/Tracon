using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Mcp.UnitTests;

/// <summary>
/// Verifies <see cref="McpTenantTools.Create"/>: an MCP-sourced tool defaults
/// to <see cref="ToolEffect.External"/> (its definition lives on a remote
/// server and can change, so the most cautious class is the honest default),
/// and every server-side tool is wrapped the same way <c>ToolRegistry</c>
/// wraps a code-defined one (docs/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md).
/// </summary>
public sealed class McpTenantToolsTests
{
    [Fact]
    public void An_mcp_tool_with_no_declared_effect_defaults_to_External()
    {
        var registration = new AgentPrismToolRegistration(
            AIFunctionFactory.Create(() => "ok", "remote_tool"),
            source: "github-mcp");

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance);

        tools.Descriptors.ShouldHaveSingleItem().Effect.ShouldBe(ToolEffect.External);
    }

    [Fact]
    public void An_mcp_tool_with_an_explicitly_declared_effect_keeps_it()
    {
        var registration = new AgentPrismToolRegistration(
            AIFunctionFactory.Create(() => "ok", "remote_write_tool"),
            source: "github-mcp",
            effect: ToolEffect.Write);

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance);

        tools.Descriptors.ShouldHaveSingleItem().Effect.ShouldBe(ToolEffect.Write);
    }

    [Fact]
    public async Task A_denied_mcp_tool_call_returns_the_reason_and_does_not_run()
    {
        var ran = false;

        var registration = new AgentPrismToolRegistration(
            AIFunctionFactory.Create(() => { ran = true; return "should never run"; }, "remote_tool"),
            source: "github-mcp");

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new DenyingHandler("Not authorized for remote tools."),
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance);

        tools.TryGet("remote_tool", out var tool).ShouldBeTrue();

        var result = await ((AIFunction)tool!).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result?.ToString().ShouldBe("Not authorized for remote tools.");
        ran.ShouldBeFalse();
    }

    [Fact]
    public async Task An_mcp_tools_output_is_truncated_the_same_way_a_code_defined_tools_is()
    {
        // 🚨 MCP tools go through a SECOND, separate wrapping chain
        // (McpTenantTools.Create) — this is the contract that both chains
        // must carry the same rings, proven directly rather than assumed.
        var registration = new AgentPrismToolRegistration(
            AIFunctionFactory.Create(() => new string('a', 10_000), "remote_report"),
            source: "github-mcp",
            maxOutputBytes: 100);

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance);

        tools.TryGet("remote_report", out var tool).ShouldBeTrue();

        var result = await ((AIFunction)tool!).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        Encoding.UTF8.GetByteCount((string)result!).ShouldBeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task The_installation_default_output_limit_applies_to_mcp_tools_too()
    {
        var registration = new AgentPrismToolRegistration(
            AIFunctionFactory.Create(() => new string('a', 10_000), "remote_report"),
            source: "github-mcp");

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            defaultMaxOutputBytes: 100);

        tools.TryGet("remote_report", out var tool).ShouldBeTrue();

        var result = await ((AIFunction)tool!).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        Encoding.UTF8.GetByteCount((string)result!).ShouldBeLessThanOrEqualTo(100);
    }

    [Fact]
    public void No_output_limit_anywhere_means_no_truncating_layer_is_installed_for_mcp_tools()
    {
        var registration = new AgentPrismToolRegistration(
            AIFunctionFactory.Create(() => new string('a', 10_000), "remote_report"),
            source: "github-mcp");

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance);

        tools.TryGet("remote_report", out var tool).ShouldBeTrue();

        ((AITool)tool!).GetService<TruncatingAIFunction>().ShouldBeNull();
    }

    private sealed class DenyingHandler(string reason) : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
            ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ToolAuthorizationResult.Deny(reason));
    }
}
