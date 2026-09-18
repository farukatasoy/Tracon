using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// Verifies <see cref="McpTenantTools.Create"/>: an MCP-sourced tool defaults
/// to <see cref="ToolEffect.External"/> (its definition lives on a remote
/// server and can change, so the most cautious class is the honest default),
/// and every server-side tool is wrapped the same way <c>ToolRegistry</c>
/// wraps a code-defined one (docs/arsiv/fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md).
/// </summary>
public sealed class McpTenantToolsTests
{
    [Fact]
    public void An_mcp_tool_with_no_declared_effect_defaults_to_External()
    {
        var registration = new TraconToolRegistration(
            AIFunctionFactory.Create(() => "ok", "remote_tool"),
            source: "github-mcp");

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        tools.Descriptors.ShouldHaveSingleItem().Effect.ShouldBe(ToolEffect.External);
    }

    [Fact]
    public void An_mcp_tool_is_never_marked_RunsOnClient()
    {
        // Replay (phase 112) rejects a definition that carries a
        // RunsOnClient=true tool; a real MCP-discovered tool is always an
        // invocable AIFunction (McpClientTool : AIFunction — the "is not
        // AIFunction" branch below only ever fires for a misconfigured
        // direct TraconToolRegistration), so it must NEVER be
        // misclassified as client-side and wrongly block a replay.
        var registration = new TraconToolRegistration(
            AIFunctionFactory.Create(() => "ok", "remote_tool"),
            source: "github-mcp");

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        tools.Descriptors.ShouldHaveSingleItem().RunsOnClient.ShouldBeFalse();
    }

    [Fact]
    public void An_mcp_tool_with_an_explicitly_declared_effect_keeps_it()
    {
        var registration = new TraconToolRegistration(
            AIFunctionFactory.Create(() => "ok", "remote_write_tool"),
            source: "github-mcp",
            effect: ToolEffect.Write);

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        tools.Descriptors.ShouldHaveSingleItem().Effect.ShouldBe(ToolEffect.Write);
    }

    [Fact]
    public async Task A_denied_mcp_tool_call_returns_the_reason_and_does_not_run()
    {
        var ran = false;

        var registration = new TraconToolRegistration(
            AIFunctionFactory.Create(() => { ran = true; return "should never run"; }, "remote_tool"),
            source: "github-mcp");

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new DenyingHandler("Not authorized for remote tools."),
            NoOpToolArgumentsValidator.Instance,
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

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
        //
        // 🚨 The stand-in returns an AIContent, not a string: that IS the
        // shape McpClientTool answers with, and a string-returning fake hid
        // HATA-S1-026 for a whole phase — the chain was installed, the budget
        // was configured, and 8 KB still reached the model past a 200-byte
        // limit. A fake that cannot produce the real defect proves nothing.
        var registration = new TraconToolRegistration(
            McpShapedTool("remote_report", new string('a', 10_000)),
            source: "github-mcp",
            maxOutputBytes: 100);

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        tools.TryGet("remote_report", out var tool).ShouldBeTrue();

        var result = await ((AIFunction)tool!).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        Encoding.UTF8.GetByteCount((string)result!).ShouldBeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task The_installation_default_output_limit_applies_to_mcp_tools_too()
    {
        var registration = new TraconToolRegistration(
            McpShapedTool("remote_report", new string('a', 10_000)),
            source: "github-mcp");

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance,
            defaultMaxOutputBytes: 100);

        tools.TryGet("remote_report", out var tool).ShouldBeTrue();

        var result = await ((AIFunction)tool!).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        Encoding.UTF8.GetByteCount((string)result!).ShouldBeLessThanOrEqualTo(100);
    }

    [Fact]
    public void No_output_limit_anywhere_means_no_truncating_layer_is_installed_for_mcp_tools()
    {
        var registration = new TraconToolRegistration(
            AIFunctionFactory.Create(() => new string('a', 10_000), "remote_report"),
            source: "github-mcp");

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        tools.TryGet("remote_report", out var tool).ShouldBeTrue();

        ((AITool)tool!).GetService<TruncatingAIFunction>().ShouldBeOfType<TruncatingAIFunction>();
    }

    /// <summary>
    /// A tool answering the way <c>McpClientTool</c> does — one
    /// <see cref="AIContent"/> block for a single-block result — instead of a
    /// bare <see langword="string"/>.
    /// </summary>
    private static ContentResultFunction McpShapedTool(string name, string text)
        => new(name, new TextContent(text));

    private sealed class ContentResultFunction(string name, AIContent result) : AIFunction
    {
        private static readonly System.Text.Json.JsonElement EmptySchema =
            System.Text.Json.JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;

        public override string Name { get; } = name;

        public override string Description => string.Empty;

        public override System.Text.Json.JsonElement JsonSchema => EmptySchema;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken)
            => new(result);
    }

    private sealed class DenyingHandler(string reason) : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
            ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ToolAuthorizationResult.Deny(reason));
    }
}
