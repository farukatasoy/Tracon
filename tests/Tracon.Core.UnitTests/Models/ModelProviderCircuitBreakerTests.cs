using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// Verifies the circuit breaker state machine: <c>Closed</c> &#8594; <c>Open</c>
/// (when the threshold is exceeded) &#8594; <c>HalfOpen</c> (once the break
/// duration elapses, a single attempt) &#8594; <c>Closed</c> or <c>Open</c> again.
/// </summary>
public sealed class ModelProviderCircuitBreakerTests
{
    private const string Provider = "test-provider";

    /// <summary>
    /// 🚨 A tenant that brings its own credential (BYOK) must not be able to open
    /// the circuit for anyone else. Keyed by provider name alone, five failed
    /// runs with one tenant's invalid key made every other tenant's run throw
    /// <see cref="TraconProviderUnavailableException"/> for the whole break
    /// duration - a denial of service one tenant could trigger on all the rest.
    /// </summary>
    [Fact]
    public void One_tenants_byok_failures_do_not_open_the_circuit_for_another()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 3));

        breaker.RecordFailure(Provider, "tenant-a");
        breaker.RecordFailure(Provider, "tenant-a");
        breaker.RecordFailure(Provider, "tenant-a");

        Should.Throw<TraconProviderUnavailableException>(
            () => breaker.EnsureRequestAllowed(Provider, "tenant-a"));

        Should.NotThrow(
            () => breaker.EnsureRequestAllowed(Provider, "tenant-b"),
            "another tenant's credential is not the one that failed.");

        // The shared credential's circuit is a third, independent one.
        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));
    }

    /// <summary>
    /// The other half of the contract: the setup-time global credential is really
    /// shared, so its failures must still back every caller off. Splitting the
    /// circuit per tenant everywhere would have removed the protection.
    /// </summary>
    [Fact]
    public void Shared_credential_failures_still_stop_every_caller()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 3));

        breaker.RecordFailure(Provider);
        breaker.RecordFailure(Provider);
        breaker.RecordFailure(Provider);

        Should.Throw<TraconProviderUnavailableException>(
            () => breaker.EnsureRequestAllowed(Provider));

        breaker.IsOpen(Provider, out _).ShouldBeTrue();
    }

    [Fact]
    public void Provider_name_still_matches_case_insensitively()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 1));

        breaker.RecordFailure("Test-Provider", "tenant-a");

        // The dictionary used to be built with OrdinalIgnoreCase; the typed key
        // must not quietly reintroduce case sensitivity.
        Should.Throw<TraconProviderUnavailableException>(
            () => breaker.EnsureRequestAllowed("test-provider", "tenant-a"));
    }

    [Fact]
    public void Request_is_allowed_while_closed()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor());

        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Circuit_opens_and_the_request_is_rejected_when_the_threshold_is_exceeded()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 3));

        breaker.RecordFailure(Provider);
        breaker.RecordFailure(Provider);
        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));

        breaker.RecordFailure(Provider); // 3rd consecutive failure: threshold exceeded
        var exception = Should.Throw<TraconProviderUnavailableException>(
            () => breaker.EnsureRequestAllowed(Provider));

        exception.ProviderName.ShouldBe(Provider);
    }

    [Fact]
    public void Successful_call_resets_the_consecutive_failure_counter()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 2));

        breaker.RecordFailure(Provider);
        breaker.RecordSuccess(Provider);
        breaker.RecordFailure(Provider);

        // Since the success reset the counter, this second failure must NOT open the circuit (threshold=2).
        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void No_retry_happens_before_the_break_duration_elapses()
    {
        var time = new ManualTimeProvider();
        var breaker = new ModelProviderCircuitBreaker(
            Monitor(o =>
            {
                o.FailureThreshold = 1;
                o.BreakDuration = TimeSpan.FromSeconds(30);
            }),
            time);

        breaker.RecordFailure(Provider);
        Should.Throw<TraconProviderUnavailableException>(() => breaker.EnsureRequestAllowed(Provider));

        time.Advance(TimeSpan.FromSeconds(10));
        Should.Throw<TraconProviderUnavailableException>(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void A_single_attempt_passes_once_the_break_duration_elapses()
    {
        var time = new ManualTimeProvider();
        var breaker = new ModelProviderCircuitBreaker(
            Monitor(o =>
            {
                o.FailureThreshold = 1;
                o.BreakDuration = TimeSpan.FromSeconds(30);
            }),
            time);

        breaker.RecordFailure(Provider);
        time.Advance(TimeSpan.FromSeconds(31));

        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Circuit_reopens_immediately_when_the_half_open_attempt_fails()
    {
        var time = new ManualTimeProvider();
        var breaker = new ModelProviderCircuitBreaker(
            Monitor(o =>
            {
                o.FailureThreshold = 1;
                o.BreakDuration = TimeSpan.FromSeconds(30);
            }),
            time);

        breaker.RecordFailure(Provider);
        time.Advance(TimeSpan.FromSeconds(31));
        breaker.EnsureRequestAllowed(Provider); // the half-open attempt starts
        breaker.RecordFailure(Provider); // the attempt fails

        Should.Throw<TraconProviderUnavailableException>(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Circuit_closes_when_the_half_open_attempt_succeeds()
    {
        var time = new ManualTimeProvider();
        var breaker = new ModelProviderCircuitBreaker(
            Monitor(o =>
            {
                o.FailureThreshold = 1;
                o.BreakDuration = TimeSpan.FromSeconds(30);
            }),
            time);

        breaker.RecordFailure(Provider);
        time.Advance(TimeSpan.FromSeconds(31));
        breaker.EnsureRequestAllowed(Provider); // the half-open attempt starts
        breaker.RecordSuccess(Provider);

        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Circuit_never_opens_once_disabled()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o =>
        {
            o.Enabled = false;
            o.FailureThreshold = 1;
        }));

        breaker.RecordFailure(Provider);
        breaker.RecordFailure(Provider);
        breaker.RecordFailure(Provider);

        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Health_endpoint_reports_not_tripped_before_the_threshold_is_exceeded()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 2));

        breaker.RecordFailure(Provider);

        breaker.IsOpen(Provider, out var retryAfter).ShouldBeFalse();
        retryAfter.ShouldBeNull();
    }

    [Fact]
    public void IsOpen_reports_the_remaining_time_and_does_not_change_state_while_the_circuit_is_open()
    {
        var time = new ManualTimeProvider();
        var breaker = new ModelProviderCircuitBreaker(
            Monitor(o =>
            {
                o.FailureThreshold = 1;
                o.BreakDuration = TimeSpan.FromSeconds(30);
            }),
            time);

        breaker.RecordFailure(Provider);

        breaker.IsOpen(Provider, out var retryAfter).ShouldBeTrue();
        retryAfter.ShouldNotBeNull();
        retryAfter!.Value.ShouldBeInRange(TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(30));

        // IsOpen must not change state: an immediately following request must still be rejected.
        Should.Throw<TraconProviderUnavailableException>(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Different_providers_state_is_independent()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 1));

        breaker.RecordFailure("provider-a");

        Should.Throw<TraconProviderUnavailableException>(() => breaker.EnsureRequestAllowed("provider-a"));
        Should.NotThrow(() => breaker.EnsureRequestAllowed("provider-b"));
    }

    [Fact]
    public async Task Wrap_checks_the_circuit_before_reaching_the_provider()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 1));
        breaker.RecordFailure(Provider);

        var inner = new FakeChatClient();
        using var wrapped = breaker.Wrap(Provider, inner);

        await Should.ThrowAsync<TraconProviderUnavailableException>(
            () => wrapped.GetResponseAsync([]));

        inner.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Wrap_counts_a_failed_call_as_a_failure()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 1));
        var inner = new ThrowingChatClient();
        using var wrapped = breaker.Wrap(Provider, inner);

        await Should.ThrowAsync<InvalidOperationException>(() => wrapped.GetResponseAsync([]));

        breaker.IsOpen(Provider, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Wrap_leaves_the_circuit_closed_after_a_successful_call()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor());
        var inner = new FakeChatClient();
        using var wrapped = breaker.Wrap(Provider, inner);

        await wrapped.GetResponseAsync([]);

        breaker.IsOpen(Provider, out _).ShouldBeFalse();
    }

    private static IOptionsMonitor<TraconOptions> Monitor(Action<TraconCircuitBreakerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.Configure<TraconOptions>(options => configure?.Invoke(options.CircuitBreaker));

        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<TraconOptions>>();
    }

    private sealed class ThrowingChatClient : Microsoft.Extensions.AI.IChatClient
    {
        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("provider error");

        public async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new InvalidOperationException("provider error");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
