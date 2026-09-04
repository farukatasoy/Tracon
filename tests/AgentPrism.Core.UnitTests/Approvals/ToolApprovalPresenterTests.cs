using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Approvals;

/// <summary>
/// <see cref="ToolApprovalPresenterRunner"/>: the fail-open contract on
/// <see cref="IToolApprovalPresenter"/> — not registered, resolves, throws, and times
/// out all publish the approval request either way (docs/142, section 142.2).
/// </summary>
public sealed class ToolApprovalPresenterTests
{
    [Fact]
    public async Task Unregistered_presenter_resolves_nothing_and_is_never_called()
    {
        var spy = new SpyPresenter(_ => throw new InvalidOperationException("must not be called"));
        var runner = new ToolApprovalPresenterRunner(
            NullToolApprovalPresenter.Instance,
            OptionsMonitor(TimeSpan.FromSeconds(2)),
            NullLogger<ToolApprovalPresenterRunner>.Instance);

        var result = await runner.ResolveAllAsync([Request("req-1", "cancel_order")], "tenant-a", "agent-a", CancellationToken.None);

        result.ShouldBeEmpty();
        spy.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Resolving_presenter_returns_its_presentation()
    {
        var presentation = new ToolApprovalPresentation { EntityType = "order", EntityId = "42", EntityName = "Order #42" };
        var presenter = new SpyPresenter(_ => new ValueTask<ToolApprovalPresentation?>(presentation));
        var runner = new ToolApprovalPresenterRunner(
            presenter,
            OptionsMonitor(TimeSpan.FromSeconds(2)),
            NullLogger<ToolApprovalPresenterRunner>.Instance);

        var result = await runner.ResolveAllAsync([Request("req-1", "cancel_order")], "tenant-a", "agent-a", CancellationToken.None);

        result["req-1"].ShouldBe(presentation);
    }

    [Fact]
    public async Task Throwing_presenter_does_not_block_the_approval_request()
    {
        var presenter = new SpyPresenter(_ => throw new InvalidOperationException("boom"));
        var runner = new ToolApprovalPresenterRunner(
            presenter,
            OptionsMonitor(TimeSpan.FromSeconds(2)),
            NullLogger<ToolApprovalPresenterRunner>.Instance);

        // 🚨 The runner itself must not throw: a presentation failure is never
        // allowed to suppress the approval request it decorates.
        var result = await runner.ResolveAllAsync([Request("req-1", "cancel_order")], "tenant-a", "agent-a", CancellationToken.None);

        result["req-1"].ShouldBeNull();
    }

    [Fact]
    public async Task Presenter_past_the_configured_timeout_resolves_null_without_blocking()
    {
        var presenter = new SpyPresenter(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);

            return null;
        });

        var runner = new ToolApprovalPresenterRunner(
            presenter,
            OptionsMonitor(TimeSpan.FromMilliseconds(20)),
            NullLogger<ToolApprovalPresenterRunner>.Instance);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await runner.ResolveAllAsync([Request("req-1", "cancel_order")], "tenant-a", "agent-a", CancellationToken.None);
        watch.Stop();

        result["req-1"].ShouldBeNull();
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5), "the configured 20ms timeout, not the presenter's infinite delay, must bound the call.");
    }

    [Fact]
    public async Task Callers_own_cancellation_propagates_instead_of_being_swallowed()
    {
        var presenter = new SpyPresenter(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);

            return null;
        });

        var runner = new ToolApprovalPresenterRunner(
            presenter,
            OptionsMonitor(TimeSpan.FromSeconds(30)),
            NullLogger<ToolApprovalPresenterRunner>.Instance);

        using var callerCancellation = new CancellationTokenSource();

        var resolveTask = runner.ResolveAllAsync([Request("req-1", "cancel_order")], "tenant-a", "agent-a", callerCancellation.Token).AsTask();

        await callerCancellation.CancelAsync();

        // The caller's own cancellation is real cancellation of the whole run, not a
        // presentation failure — it must propagate, not resolve to an empty result.
        await Should.ThrowAsync<OperationCanceledException>(() => resolveTask);
    }

    [Fact]
    public async Task Presenter_receives_the_tenant_and_arguments_of_the_call()
    {
        ToolApprovalContext? seen = null;
        var presenter = new SpyPresenter(_ => new ValueTask<ToolApprovalPresentation?>((ToolApprovalPresentation?)null));

        presenter.OnPresent = context => seen = context;

        var runner = new ToolApprovalPresenterRunner(
            presenter,
            OptionsMonitor(TimeSpan.FromSeconds(2)),
            NullLogger<ToolApprovalPresenterRunner>.Instance);

        await runner.ResolveAllAsync(
            [Request("req-1", "cancel_order", new Dictionary<string, object?>(StringComparer.Ordinal) { ["orderId"] = "42" })],
            "tenant-a",
            "agent-a",
            CancellationToken.None);

        seen.ShouldNotBeNull();
        seen!.TenantId.ShouldBe("tenant-a");
        seen.ToolName.ShouldBe("cancel_order");
        seen.AgentName.ShouldBe("agent-a");
        seen.GetString("orderId").ShouldBe("42");
    }

    private static ToolApprovalRequestContent Request(string requestId, string toolName, Dictionary<string, object?>? arguments = null)
        => new(requestId, new FunctionCallContent(callId: $"call-{requestId}", name: toolName, arguments: arguments ?? new Dictionary<string, object?>(StringComparer.Ordinal)));

    private static IOptionsMonitor<AgentPrismOptions> OptionsMonitor(TimeSpan approvalPresentationTimeout)
    {
        var services = new ServiceCollection();
        services.AddOptions<AgentPrismOptions>().Configure(o => o.Tools.ApprovalPresentationTimeout = approvalPresentationTimeout);

        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<AgentPrismOptions>>();
    }

    private sealed class SpyPresenter(Func<CancellationToken, ValueTask<ToolApprovalPresentation?>> callback) : IToolApprovalPresenter
    {
        public int CallCount { get; private set; }

        public Action<ToolApprovalContext>? OnPresent { get; set; }

        public ValueTask<ToolApprovalPresentation?> PresentAsync(ToolApprovalContext context, CancellationToken cancellationToken = default)
        {
            CallCount++;
            OnPresent?.Invoke(context);

            return callback(cancellationToken);
        }
    }
}
