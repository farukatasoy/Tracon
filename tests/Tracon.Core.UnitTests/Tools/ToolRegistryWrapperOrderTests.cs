using System.Text;
using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// Verifies the composition order <c>ToolRegistry</c> installs — Authorizing
/// (outermost) then Timeout then ApprovalRequired (innermost) then the real
/// function — docs/arsiv/fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md, section 69.1.
/// </summary>
public sealed class ToolRegistryWrapperOrderTests
{
    [Fact]
    public async Task A_denied_call_never_reaches_the_approval_wrapper_or_the_real_body()
    {
        var ran = false;

        var registration = new TraconToolRegistration(
            AIFunctionFactory.Create(() => { ran = true; return "should never run"; }, "cancel_order"),
            requiresApproval: true,
            effect: ToolEffect.Destructive);

        var registry = new ToolRegistry(
            [registration],
            new DenyingHandler("This account cannot cancel orders."),
            NoOpToolArgumentsValidator.Instance,
            TestData.DefaultOptionsMonitor(),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        registry.TryGet("cancel_order", out var tool).ShouldBeTrue();

        // Authorization is outermost: a denied call is answered directly, it
        // never reaches ApprovalRequiredAIFunction (which would otherwise
        // produce a pending-approval signal) or the real body.
        var result = await ((AIFunction)tool!).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result?.ToString().ShouldBe("This account cannot cancel orders.");
        ran.ShouldBeFalse();
    }

    [Fact]
    public void The_approval_wrapper_stays_discoverable_through_the_outer_layers()
    {
        // 🚨 MEASURED, not assumed: ApprovalRequiredAIFunction.InvokeCoreAsync
        // does NOT itself defer — calling it directly runs the real body.
        // Microsoft Agent Framework's function-invoking client short-circuits
        // BEFORE ever calling InvokeAsync, by locating the ApprovalRequiredAIFunction
        // through the AITool.GetService(Type) pipeline (the same pattern
        // IChatClient.GetService uses for its own middleware chain). If
        // Authorizing/TimeoutAIFunction did not forward GetService to their
        // inner function (DelegatingAIFunction's default implementation
        // does), that lookup would fail and approval would silently stop
        // working for every tool wrapped by this registry.
        var registration = new TraconToolRegistration(
            AIFunctionFactory.Create(() => "result", "dangerous_tool"),
            requiresApproval: true);

        var registry = new ToolRegistry(
            [registration],
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            TestData.DefaultOptionsMonitor(),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        registry.TryGet("dangerous_tool", out var tool).ShouldBeTrue();

        ((AITool)tool!).GetService<ApprovalRequiredAIFunction>().ShouldBeOfType<ApprovalRequiredAIFunction>();
    }

    [Fact]
    public void No_output_limit_still_installs_the_canonical_result_boundary()
    {
        // Unlimited remains the default for size, but canonicalization and
        // fail-closed unsupported-result handling are unconditional.
        var registration = new TraconToolRegistration(
            AIFunctionFactory.Create(() => new string('a', 10_000), "big_report"));

        var registry = new ToolRegistry(
            [registration],
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            TestData.DefaultOptionsMonitor(),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        registry.TryGet("big_report", out var tool).ShouldBeTrue();

        ((AITool)tool!).GetService<TruncatingAIFunction>().ShouldBeOfType<TruncatingAIFunction>();
    }

    [Fact]
    public async Task A_registration_level_output_limit_truncates_the_real_result()
    {
        var registration = new TraconToolRegistration(
            AIFunctionFactory.Create(() => new string('a', 10_000), "big_report"),
            maxOutputBytes: 100);

        var registry = new ToolRegistry(
            [registration],
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            TestData.DefaultOptionsMonitor(),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        registry.TryGet("big_report", out var tool).ShouldBeTrue();

        ((AITool)tool!).GetService<TruncatingAIFunction>().ShouldBeOfType<TruncatingAIFunction>();

        var result = await ((AIFunction)tool!).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        Encoding.UTF8.GetByteCount((string)result!).ShouldBeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task The_installation_default_output_limit_applies_when_the_registration_sets_none()
    {
        var registration = new TraconToolRegistration(
            AIFunctionFactory.Create(() => new string('a', 10_000), "big_report"));

        var services = new ServiceCollection();
        services.AddOptions<TraconOptions>().Configure(options => options.Tools.DefaultMaxOutputBytes = 100);

        var registry = new ToolRegistry(
            [registration],
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<TraconOptions>>(),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        registry.TryGet("big_report", out var tool).ShouldBeTrue();

        var result = await ((AIFunction)tool!).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        Encoding.UTF8.GetByteCount((string)result!).ShouldBeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task A_registration_level_output_limit_overrides_the_installation_default()
    {
        // Same precedence rule as Timeout: the tool's own registration wins.
        var registration = new TraconToolRegistration(
            AIFunctionFactory.Create(() => new string('a', 10_000), "big_report"),
            maxOutputBytes: 5_000);

        var services = new ServiceCollection();
        services.AddOptions<TraconOptions>().Configure(options => options.Tools.DefaultMaxOutputBytes = 50);

        var registry = new ToolRegistry(
            [registration],
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<TraconOptions>>(),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        registry.TryGet("big_report", out var tool).ShouldBeTrue();

        var result = await ((AIFunction)tool!).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        // The 5,000-byte registration limit wins over the 50-byte default: the
        // result is still well over 50 bytes.
        Encoding.UTF8.GetByteCount((string)result!).ShouldBeGreaterThan(50);
    }

    [Fact]
    public void The_approval_wrapper_stays_discoverable_when_truncation_is_also_configured()
    {
        // Regression guard: inserting TruncatingAIFunction into the chain
        // must not break the existing GetService discoverability the
        // function-invoking client relies on to find the approval layer.
        var registration = new TraconToolRegistration(
            AIFunctionFactory.Create(() => "result", "dangerous_tool"),
            requiresApproval: true,
            maxOutputBytes: 1024);

        var registry = new ToolRegistry(
            [registration],
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            TestData.DefaultOptionsMonitor(),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        registry.TryGet("dangerous_tool", out var tool).ShouldBeTrue();

        ((AITool)tool!).GetService<ApprovalRequiredAIFunction>().ShouldBeOfType<ApprovalRequiredAIFunction>();
    }

    private sealed class DenyingHandler(string reason) : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
            ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ToolAuthorizationResult.Deny(reason));
    }
}
