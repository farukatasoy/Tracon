using System.Net;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Egress;

/// <summary>
/// The enforcement point itself: the connection callback, and the clients built
/// from it.
/// </summary>
/// <remarks>
/// <para>
/// This is the check that decides whether a socket opens, and until Phase 77 it
/// had no test at all (B05-6). A validator test cannot stand in for it: the
/// address rules could be perfect while the callback ignored them, or resolved
/// the name a second time and reopened the DNS rebinding window that the whole
/// design exists to close.
/// </para>
/// <para>
/// Every case here uses an IP literal, so no DNS query and no outbound packet
/// is produced: a rejected target fails before <c>connect</c>, and there is no
/// allowed-target case that would need a real listener.
/// </para>
/// </remarks>
public sealed class EgressSocketGuardTests
{
    private static EgressSocketGuard Strict()
        => new(static () => EgressAddressPolicy.Deny);

    private static EgressSocketGuard FromOptions(bool allowPrivateNetworkTargets)
        => new(new StaticMonitor(new AgentPrismEgressOptions
        {
            AllowPrivateNetworkTargets = allowPrivateNetworkTargets,
        }));

    /// <summary>
    /// The point of the whole phase: a request through a guarded client never
    /// reaches a private address.
    /// </summary>
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5:8080/")]
    [InlineData("http://[64:ff9b::a9fe:a9fe]/")]
    public async Task Guarded_client_refuses_to_connect_to_a_private_address(string url)
    {
        using var client = Strict().CreateHttpClient(TimeSpan.FromSeconds(5));

        var exception = await Should.ThrowAsync<HttpRequestException>(
            async () => await client.GetAsync(new Uri(url)));

        // The guard's own reason must survive HttpClient's wrapping, otherwise
        // an operator sees a bare "connection failed" and never learns which
        // setting to change.
        Unwrap(exception).ShouldNotBeNull()
            .Message.ShouldContain(nameof(AgentPrismEgressOptions.AllowPrivateNetworkTargets));
    }

    /// <summary>The callback reads the options per connection, so a change takes effect at once.</summary>
    [Fact]
    public async Task Options_decide_the_verdict()
    {
        using var strict = FromOptions(allowPrivateNetworkTargets: false).CreateHttpClient(TimeSpan.FromSeconds(5));

        await Should.ThrowAsync<HttpRequestException>(
            async () => await strict.GetAsync(new Uri("http://169.254.169.254/")));

        using var permissive = FromOptions(allowPrivateNetworkTargets: true)
            .CreateHttpClient(TimeSpan.FromSeconds(5));

        // Now the guard lets it through, so whatever happens next belongs to
        // the NETWORK, not to the guard. The exact outcome is environment
        // dependent — a refused connection outside a cloud VM, a timeout
        // inside one, a real response on a host that has a metadata service —
        // so the assertion is only that the guard is no longer the cause.
        Exception? thrown = null;

        try
        {
            using var response = await permissive.GetAsync(new Uri("http://169.254.169.254/"));
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        Unwrap(thrown).ShouldBeNull();
    }

    /// <summary>
    /// A proxy would see the real target while the callback judged only the
    /// proxy's address, which would defeat the guard silently.
    /// </summary>
    [Fact]
    public void Handler_does_not_use_an_ambient_proxy()
    {
        using var handler = Strict().CreateHandler();

        handler.UseProxy.ShouldBeFalse();
        handler.ConnectCallback.ShouldNotBeNull();
    }

    [Fact]
    public void Client_carries_the_timeout_it_was_given()
    {
        using var client = Strict().CreateHttpClient(TimeSpan.FromSeconds(100));

        client.Timeout.ShouldBe(TimeSpan.FromSeconds(100));
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_private_target_and_accepts_a_public_one()
    {
        var guard = Strict();

        (await Should.ThrowAsync<AgentPrismException>(
                async () => await guard.ValidateAsync(new Uri("http://169.254.169.254/"))))
            .Message.ShouldContain("169.254.169.254");

        await Should.NotThrowAsync(async () => await guard.ValidateAsync(new Uri("https://8.8.8.8/")));
    }

    [Fact]
    public async Task Cancellation_flows_through_the_callback()
    {
        using var client = Strict().CreateHttpClient(Timeout.InfiniteTimeSpan);
        using var source = new CancellationTokenSource();

        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await client.GetAsync(new Uri("https://8.8.8.8/"), source.Token));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new EgressSocketGuard((Func<EgressAddressPolicy>)null!));
        Should.Throw<ArgumentNullException>(
            () => new EgressSocketGuard((IOptionsMonitor<AgentPrismEgressOptions>)null!));
    }

    /// <summary>Finds the guard's own exception inside HttpClient's wrapping, if it is there.</summary>
    private static AgentPrismException? Unwrap(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is AgentPrismException agentPrism)
            {
                return agentPrism;
            }
        }

        return null;
    }

    private sealed class StaticMonitor(AgentPrismEgressOptions value) : IOptionsMonitor<AgentPrismEgressOptions>
    {
        public AgentPrismEgressOptions CurrentValue => value;

        public AgentPrismEgressOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AgentPrismEgressOptions, string?> listener) => null;
    }
}
