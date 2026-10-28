using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// Verifies <see cref="McpTenantTools.Create"/>: an MCP-sourced tool defaults
/// to <see cref="ToolEffect.External"/> (its definition lives on a remote
/// server and can change, so the most cautious class is the honest default),
/// and every server-side tool is wrapped the same way <c>ToolRegistry</c>
/// wraps a code-defined one (Phase 69).
/// </summary>
public sealed class McpTenantToolsTests
{
    [Fact]
    public void An_mcp_tool_with_no_declared_effect_defaults_to_External()
    {
        var registration = new TraconToolRegistration(
            AIFunctionFactory.Create(() => "ok", "remote_tool"))
        {
            Source = "github-mcp",
        };

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
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
            AIFunctionFactory.Create(() => "ok", "remote_tool"))
        {
            Source = "github-mcp",
        };

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
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
            AIFunctionFactory.Create(() => "ok", "remote_write_tool"))
        {
            Source = "github-mcp",
            Effect = ToolEffect.Write,
        };

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
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
            AIFunctionFactory.Create(() => { ran = true; return "should never run"; }, "remote_tool"))
        {
            Source = "github-mcp",
        };

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new DenyingHandler("Not authorized for remote tools."),
            McpToolPolicyTestGates.Open(),
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
            McpShapedTool("remote_report", new string('a', 10_000)))
        {
            Source = "github-mcp",
            MaxOutputBytes = 100,
        };

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
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
            McpShapedTool("remote_report", new string('a', 10_000)))
        {
            Source = "github-mcp",
        };

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
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
            AIFunctionFactory.Create(() => new string('a', 10_000), "remote_report"))
        {
            Source = "github-mcp",
        };

        var tools = McpTenantTools.Create(
            [registration],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
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
    /// The source a standing approval rule is judged against belongs to the
    /// registration that WON the name, not to a skipped duplicate.
    /// </summary>
    [Fact]
    public void The_source_fingerprint_follows_the_registration_that_won_the_name()
    {
        var winner = new TraconToolRegistration(AIFunctionFactory.Create(() => "a", "shared_lookup")) { Source = "server-a" };
        var skipped = new TraconToolRegistration(AIFunctionFactory.Create(() => "b", "shared_lookup")) { Source = "server-b" };
        var sources = new Dictionary<TraconToolRegistration, string>(ReferenceEqualityComparer.Instance)
        {
            [winner] = ToolSourceFingerprint.ForEndpoint(new Uri("https://a.example/mcp")),
            [skipped] = ToolSourceFingerprint.ForEndpoint(new Uri("https://b.example/mcp")),
        };

        var tools = McpTenantTools.Create(
            [winner, skipped],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
            NoOpToolArgumentsValidator.Instance,
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance,
            sourceFingerprints: sources);

        tools.GetSourceFingerprint("shared_lookup").ShouldBe(sources[winner]);
        tools.GetSourceFingerprint("unknown_tool").ShouldBeNull();
    }

    /// <summary>
    /// A tool named like one of the tenant's servers but missing from the snapshot
    /// (the server's refresh failed) has an UNRESOLVED source, not none: a compiled
    /// agent may still hold the tool, and "none" would let every rule without a
    /// source approve it.
    /// </summary>
    [Fact]
    public void A_tool_of_a_server_whose_tools_are_not_discovered_has_an_unresolved_source()
    {
        var tools = McpTenantTools.Create(
            [],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
            NoOpToolArgumentsValidator.Instance,
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance,
            serverNames: ["crm"]);

        tools.GetSourceFingerprint("crm_lookup").ShouldBe(ToolSourceFingerprint.Unresolved);
        tools.GetSourceFingerprint("cancel_order").ShouldBeNull("a name no server produces is not remote");
    }

    /// <summary>
    /// The binding a compiled-agent cache key carries (F-305): the connection
    /// behind the registration that WON the name, "absent" for a name the snapshot
    /// does not serve (also a server whose refresh failed), and "unbound" for a
    /// served tool whose connection is not known.
    /// </summary>
    [Fact]
    public void The_binding_follows_the_connection_of_the_registration_that_won_the_name()
    {
        var winner = new TraconToolRegistration(AIFunctionFactory.Create(() => "a", "shared_lookup")) { Source = "server-a" };
        var skipped = new TraconToolRegistration(AIFunctionFactory.Create(() => "b", "shared_lookup")) { Source = "server-b" };
        var unbound = new TraconToolRegistration(AIFunctionFactory.Create(() => "c", "other_lookup")) { Source = "server-c" };
        var generations = new Dictionary<TraconToolRegistration, long>(ReferenceEqualityComparer.Instance)
        {
            [winner] = 41,
            [skipped] = 42,
        };

        var tools = McpTenantTools.Create(
            [winner, skipped, unbound],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
            NoOpToolArgumentsValidator.Instance,
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance,
            serverNames: ["crm"],
            connectionGenerations: generations);

        tools.GetBindingFingerprint("shared_lookup").ShouldStartWith("41:");
        tools.GetBindingFingerprint("other_lookup").ShouldBe(McpTenantTools.UnboundBinding);
        tools.GetBindingFingerprint("crm_lookup").ShouldBe(McpTenantTools.AbsentBinding, "the server is registered, its tools are not discovered");
        McpTenantTools.Empty.GetBindingFingerprint("shared_lookup").ShouldBe(McpTenantTools.AbsentBinding);
    }

    /// <summary>
    /// F-308: a server can change a tool's description or schema while the connection
    /// stays the same. The compiled agent embeds the tool object with the old
    /// definition, so the binding carries a hash of the definition too.
    /// </summary>
    [Fact]
    public void The_binding_changes_when_the_tool_definition_changes_on_the_same_connection()
    {
        static string BindingOf(string description)
        {
            var registration = new TraconToolRegistration(
                AIFunctionFactory.Create(() => "a", new AIFunctionFactoryOptions { Name = "crm_lookup", Description = description }))
            {
                Source = "crm",
            };

            return McpTenantTools.Create(
                    [registration],
                    NullLogger.Instance,
                    new AllowAllToolAuthorizationHandler(),
                    McpToolPolicyTestGates.Open(),
                    NoOpToolArgumentsValidator.Instance,
                    TimeSpan.FromSeconds(30),
                    attribution: null,
                    NullLogger<AuthorizingAIFunction>.Instance,
                    NullLogger<TimeoutAIFunction>.Instance,
                    NullLogger<ValidatingAIFunction>.Instance,
                    connectionGenerations: new Dictionary<TraconToolRegistration, long>(ReferenceEqualityComparer.Instance) { [registration] = 7 })
                .GetBindingFingerprint("crm_lookup");
        }

        BindingOf("Looks a customer up.").ShouldBe(BindingOf("Looks a customer up."));
        BindingOf("Looks a customer up.").ShouldNotBe(BindingOf("Looks a customer up by email."), StringComparer.Ordinal);
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
