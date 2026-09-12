using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// Unit-level coverage of <see cref="ScopedAIFunction"/>: the innermost
/// wrapper <c>AddScopedTool</c> installs so a call resolves a REAL,
/// call-scoped <see cref="IServiceProvider"/> from
/// <see cref="AIFunctionArguments.Services"/> instead of MAF's empty one
/// (docs/127, 127.3).
/// </summary>
public sealed class ScopedToolTests
{
    private static IServiceScopeFactory ScopeFactory(Action<ServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<Marker>();
        configure?.Invoke(services);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task The_call_sees_a_real_provider_instead_of_MAFs_empty_one()
    {
        Marker? resolved = null;
        var inner = AIFunctionFactory.Create(
            (AIFunctionArguments arguments) =>
            {
                resolved = arguments.Services!.GetService<Marker>();
                return "ok";
            },
            "tool");
        var wrapped = new ScopedAIFunction(inner, ScopeFactory());

        await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        resolved.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_scope_closes_when_the_call_completes()
    {
        var factory = ScopeFactory(services => services.AddScoped<DisposeTracker>());
        DisposeTracker? tracker = null;

        var inner = AIFunctionFactory.Create(
            (AIFunctionArguments arguments) =>
            {
                tracker = arguments.Services!.GetRequiredService<DisposeTracker>();
                return "ok";
            },
            "tool");
        var wrapped = new ScopedAIFunction(inner, factory);

        await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        tracker.ShouldNotBeNull();
        tracker!.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task The_scope_still_closes_when_the_call_throws()
    {
        var factory = ScopeFactory(services => services.AddScoped<DisposeTracker>());
        DisposeTracker? tracker = null;

        var inner = AIFunctionFactory.Create(
            (AIFunctionArguments arguments) =>
            {
                tracker = arguments.Services!.GetRequiredService<DisposeTracker>();
                throw new InvalidOperationException("boom");
            },
            "tool");
        var wrapped = new ScopedAIFunction(inner, factory);

        // Whether the thrown exception propagates out of InvokeAsync or is
        // converted into a result by the AIFunction base is not this test's
        // concern - either way, the scope opened for this call must close.
        try
        {
            await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));
        }
        catch (InvalidOperationException)
        {
            // Expected: the body's own exception, propagated or not.
        }

        tracker.ShouldNotBeNull();
        tracker!.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task The_scope_closes_when_the_call_is_canceled()
    {
        var factory = ScopeFactory(services => services.AddScoped<DisposeTracker>());
        DisposeTracker? tracker = null;

        var inner = AIFunctionFactory.Create(
            async (AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            {
                tracker = arguments.Services!.GetRequiredService<DisposeTracker>();
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return "unreachable";
            },
            "tool");
        var wrapped = new ScopedAIFunction(inner, factory);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(20));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), cts.Token));

        tracker.ShouldNotBeNull();
        tracker!.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Two_concurrent_calls_never_share_a_scope()
    {
        var factory = ScopeFactory();
        var gate = new SemaphoreSlim(0, 2);
        var seenA = new TaskCompletionSource<Marker>();
        var seenB = new TaskCompletionSource<Marker>();
        var callIndex = 0;

        var inner = AIFunctionFactory.Create(
            async (AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            {
                var marker = arguments.Services!.GetRequiredService<Marker>();
                var isFirst = Interlocked.Increment(ref callIndex) == 1;

                (isFirst ? seenA : seenB).SetResult(marker);

                await gate.WaitAsync(cancellationToken);
                return "ok";
            },
            "tool");
        var wrapped = new ScopedAIFunction(inner, factory);

        var callA = wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)).AsTask();
        var callB = wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)).AsTask();

        var markerA = await seenA.Task;
        var markerB = await seenB.Task;

        gate.Release(2);
        await Task.WhenAll(callA, callB);

        markerA.ShouldNotBeSameAs(markerB);
    }

    private sealed class Marker;

    private sealed class DisposeTracker : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
