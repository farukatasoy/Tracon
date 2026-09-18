using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// Proves <see cref="ToolWrapperChain"/> is the single composition point both
/// <c>ToolRegistry</c> (code-defined tools) and <c>McpTenantTools</c>
/// (MCP-sourced tools) build on — the two call sites differ only in the
/// <see cref="ToolDescriptor.Effect"/> they pass in, never in the wrapper
/// layers or their order (docs/127, 127.1).
/// </summary>
public sealed class ToolWrapperChainTests
{
    private static ToolDescriptor Descriptor(string name, ToolEffect effect = ToolEffect.Read, bool requiresApproval = false)
        => new()
        {
            Name = name,
            Effect = effect,
            RequiresApproval = requiresApproval,
        };

    private static AIFunctionDeclaration Compose(
        TraconToolRegistration registration,
        ToolDescriptor descriptor,
        IToolAuthorizationHandler? authorizationHandler = null,
        IToolArgumentsValidator? validator = null)
        => ToolWrapperChain.Compose(
            registration,
            descriptor,
            authorizationHandler ?? new AllowAllToolAuthorizationHandler(),
            validator ?? NoOpToolArgumentsValidator.Instance,
            defaultTimeout: TimeSpan.FromSeconds(30),
            defaultMaxOutputBytes: null,
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

    [Fact]
    public void A_code_defined_registration_and_an_mcp_style_registration_produce_the_same_wrapper_layers()
    {
        var codeRegistration = new TraconToolRegistration(TestData.Tool("code_tool"));
        var mcpRegistration = new TraconToolRegistration(TestData.Tool("mcp_tool"), source: "github-mcp");

        // ToolRegistry passes the registration's own effect straight through;
        // McpTenantTools promotes Read -> External before building the
        // descriptor. That promotion is the ONE allowed difference (127.1) -
        // everything else about the composed chain must be identical.
        var codeFunction = Compose(codeRegistration, Descriptor("code_tool", ToolEffect.Read));
        var mcpFunction = Compose(mcpRegistration, Descriptor("mcp_tool", ToolEffect.External));

        var codeChain = ((AITool)codeFunction).GetType();
        var mcpChain = ((AITool)mcpFunction).GetType();

        // ExplainedFailureAIFunction is the outermost layer since HATA-S1-024:
        // it covers every layer beneath it, so a rejected argument, a denial and
        // a timeout all reach the model with the sentence Tracon wrote.
        codeChain.ShouldBe(typeof(ExplainedFailureAIFunction));
        mcpChain.ShouldBe(typeof(ExplainedFailureAIFunction));

        ((AITool)codeFunction).GetService<AuthorizingAIFunction>().ShouldBeOfType<AuthorizingAIFunction>();
        ((AITool)mcpFunction).GetService<AuthorizingAIFunction>().ShouldBeOfType<AuthorizingAIFunction>();

        ((AITool)codeFunction).GetService<TimeoutAIFunction>().ShouldBeOfType<TimeoutAIFunction>();
        ((AITool)mcpFunction).GetService<TimeoutAIFunction>().ShouldBeOfType<TimeoutAIFunction>();

        ((AITool)codeFunction).GetService<TruncatingAIFunction>().ShouldBeOfType<TruncatingAIFunction>();
        ((AITool)mcpFunction).GetService<TruncatingAIFunction>().ShouldBeOfType<TruncatingAIFunction>();
    }

    [Fact]
    public void RequiresApproval_installs_the_approval_wrapper_for_both_call_shapes()
    {
        var codeRegistration = new TraconToolRegistration(TestData.Tool("code_tool"), requiresApproval: true);
        var mcpRegistration = new TraconToolRegistration(TestData.Tool("mcp_tool"), requiresApproval: true, source: "github-mcp");

        var codeFunction = Compose(codeRegistration, Descriptor("code_tool", requiresApproval: true));
        var mcpFunction = Compose(mcpRegistration, Descriptor("mcp_tool", ToolEffect.External, requiresApproval: true));

        ((AITool)codeFunction).GetService<ApprovalRequiredAIFunction>().ShouldBeOfType<ApprovalRequiredAIFunction>();
        ((AITool)mcpFunction).GetService<ApprovalRequiredAIFunction>().ShouldBeOfType<ApprovalRequiredAIFunction>();
    }

    [Fact]
    public async Task No_registered_validator_means_no_validating_layer_is_installed()
    {
        var registration = new TraconToolRegistration(TestData.Tool("plain_tool"));

        var function = Compose(registration, Descriptor("plain_tool"), validator: NoOpToolArgumentsValidator.Instance);

        ((AITool)function).GetService<ValidatingAIFunction>().ShouldBeNull();

        // The tool still runs normally - the ring is entirely absent, not just inert.
        var result = await ((AIFunction)function).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));
        result.ShouldNotBeNull();
    }

    [Fact]
    public void A_real_validator_installs_the_validating_layer_between_timeout_and_authorizing()
    {
        var registration = new TraconToolRegistration(TestData.Tool("validated_tool"));

        var function = Compose(registration, Descriptor("validated_tool"), validator: new AllowingValidator());

        ((AITool)function).GetType().ShouldBe(typeof(ExplainedFailureAIFunction));
        ((AITool)function).GetService<AuthorizingAIFunction>().ShouldBeOfType<AuthorizingAIFunction>();
        ((AITool)function).GetService<ValidatingAIFunction>().ShouldBeOfType<ValidatingAIFunction>();
    }

    [Fact]
    public void A_client_side_declaration_passes_through_unwrapped()
    {
        var schema = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"type":"object","properties":{}}""");
        var declaration = AIFunctionFactory.CreateDeclaration("client_tool", "desc", schema, returnJsonSchema: null);
        var registration = new TraconToolRegistration(declaration);

        var function = Compose(registration, Descriptor("client_tool"));

        function.ShouldBeSameAs(declaration);
    }

    [Fact]
    public void A_client_side_declaration_requiring_approval_throws()
    {
        var schema = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"type":"object","properties":{}}""");
        var declaration = AIFunctionFactory.CreateDeclaration("client_tool", "desc", schema, returnJsonSchema: null);
        var registration = new TraconToolRegistration(declaration, requiresApproval: true);

        var exception = Should.Throw<TraconException>(
            () => Compose(registration, Descriptor("client_tool", requiresApproval: true)));

        exception.Message.ShouldContain("client_tool");
        exception.Message.ShouldContain("client");
    }

    private sealed class AllowingValidator : IToolArgumentsValidator
    {
        public ValueTask<ToolArgumentsValidationResult> ValidateAsync(
            ToolDescriptor tool, AIFunctionArguments arguments, CancellationToken cancellationToken = default)
            => new(ToolArgumentsValidationResult.Valid);
    }
}
