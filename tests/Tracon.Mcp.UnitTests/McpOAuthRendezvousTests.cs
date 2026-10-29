using System.Text;
using Microsoft.Extensions.Logging;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// How the instance that receives the provider's redirect hands the authorization code to the
/// instance that started the flow, through the distributed cache.
/// </summary>
/// <remarks>
/// 🚨 The redirect endpoint is anonymous and writes to a shared cache for it. These tests hold the
/// bound on that write: a value is accepted only when it is shaped like a <c>state</c> the SDK
/// generated, is small, and an instance announced the flow.
/// </remarks>
public sealed class McpOAuthRendezvousTests
{
    private const string State = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    [Fact]
    public async Task A_callback_envelope_is_taken_once()
    {
        var rendezvous = Rendezvous(new RecordingDistributedCache());

        await rendezvous.DepositCallbackAsync(State, "the-code", "https://issuer.example.com", CancellationToken.None);

        var first = (await rendezvous.TakeCallbackAsync(State, CancellationToken.None)).ShouldNotBeNull();
        first.Code.ShouldBe("the-code");
        first.Iss.ShouldBe("https://issuer.example.com");

        // Single use: a second reader, or the same one again, gets nothing.
        (await rendezvous.TakeCallbackAsync(State, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_redirect_without_a_code_is_taken_as_declined()
    {
        var rendezvous = Rendezvous(new RecordingDistributedCache());

        await rendezvous.DepositCallbackAsync(State, code: null, iss: null, CancellationToken.None);

        var envelope = (await rendezvous.TakeCallbackAsync(State, CancellationToken.None)).ShouldNotBeNull();

        McpOAuthRendezvous.ToAuthorizationResult(State, envelope).ShouldBeNull("the owner ends the flow instead of waiting for ten minutes.");
    }

    [Fact]
    public async Task The_code_and_the_result_are_stored_encrypted_and_under_digests()
    {
        var cache = new RecordingDistributedCache();
        var rendezvous = Rendezvous(cache);

        await rendezvous.AnnounceAsync(State, CancellationToken.None);
        await rendezvous.DepositCallbackAsync(State, "the-code-8c1d", "https://issuer.example.com", CancellationToken.None);
        await rendezvous.PublishOutcomeAsync(State, new McpOutcomeEnvelope(Success: false, "prod", "The provider rejected the code."));

        cache.Keys.Count.ShouldBe(3);

        foreach (var key in cache.Keys)
        {
            // The state is a bearer value: it is in no key, only its digest is.
            key.ShouldNotContain(State, Case.Sensitive);
        }

        var all = string.Concat(cache.Keys.Select(key => McpOAuthTestKit.AsText(cache.Peek(key)!)));

        all.ShouldNotContain("the-code-8c1d", Case.Sensitive);
        all.ShouldNotContain("issuer.example.com", Case.Sensitive);
        all.ShouldNotContain("rejected the code", Case.Sensitive);
    }

    [Fact]
    public async Task Every_entry_of_a_flow_ends_by_itself()
    {
        var cache = new RecordingDistributedCache();
        var rendezvous = Rendezvous(cache);

        await rendezvous.AnnounceAsync(State, CancellationToken.None);
        await rendezvous.DepositCallbackAsync(State, "code", iss: null, CancellationToken.None);
        await rendezvous.PublishOutcomeAsync(State, new McpOutcomeEnvelope(Success: true, "prod", Error: null));

        // The cache is shared and the endpoint is anonymous; nothing may live on.
        cache.OptionsOf(McpOAuthCacheKeys.Flow(State)).AbsoluteExpirationRelativeToNow.ShouldBe(TimeSpan.FromMinutes(10));
        cache.OptionsOf(McpOAuthCacheKeys.Callback(State)).AbsoluteExpirationRelativeToNow.ShouldBe(TimeSpan.FromMinutes(10));
        cache.OptionsOf(McpOAuthCacheKeys.Outcome(State)).AbsoluteExpirationRelativeToNow.ShouldBe(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task An_entry_that_is_not_an_encrypted_callback_is_discarded_and_the_flow_keeps_waiting()
    {
        var cache = new RecordingDistributedCache();
        var logs = new RecordingLoggerFactory();
        var rendezvous = new McpOAuthRendezvous(McpOAuthTestKit.Backend(cache), logs.CreateLogger("test"));

        // Somebody with access to the cache plants a plaintext code for a state they know.
        cache.Plant(McpOAuthCacheKeys.Callback(State), Encoding.UTF8.GetBytes("""{"code":"planted"}"""));

        (await rendezvous.TakeCallbackAsync(State, CancellationToken.None)).ShouldBeNull();
        cache.Peek(McpOAuthCacheKeys.Callback(State)).ShouldBeNull("the planted entry is gone.");
        logs.Count(LogLevel.Warning, "could not be read").ShouldBe(1);
    }

    [Fact]
    public async Task A_flow_is_announced_until_it_is_forgotten()
    {
        var rendezvous = Rendezvous(new RecordingDistributedCache());

        (await rendezvous.IsAnnouncedAsync(State, CancellationToken.None)).ShouldBeFalse();

        await rendezvous.AnnounceAsync(State, CancellationToken.None);
        (await rendezvous.IsAnnouncedAsync(State, CancellationToken.None)).ShouldBeTrue();

        await rendezvous.ForgetAsync(State);
        (await rendezvous.IsAnnouncedAsync(State, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_owner_that_waits_takes_the_code_that_arrives_later()
    {
        var rendezvous = Rendezvous(new RecordingDistributedCache());

        var waiting = rendezvous.AwaitCallbackAsync(State, CancellationToken.None);
        waiting.IsCompleted.ShouldBeFalse();

        await rendezvous.DepositCallbackAsync(State, "late-code", iss: null, CancellationToken.None);

        (await waiting).Code.ShouldBe("late-code");
    }

    [Fact]
    public async Task The_wait_for_a_code_ends_with_cancellation()
    {
        var rendezvous = Rendezvous(new RecordingDistributedCache());
        using var cancellation = new CancellationTokenSource();

        var waiting = rendezvous.AwaitCallbackAsync(State, cancellation.Token);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await waiting);
    }

    [Fact]
    public async Task A_cache_that_fails_for_a_moment_does_not_end_the_wait()
    {
        var cache = new RecordingDistributedCache { Failing = true };
        var rendezvous = Rendezvous(cache);

        var waiting = rendezvous.AwaitCallbackAsync(State, CancellationToken.None);

        // The administrator is in the middle of an authorization: the owner keeps looking.
        cache.Failing = false;
        await rendezvous.DepositCallbackAsync(State, "code-after-the-outage", iss: null, CancellationToken.None);

        (await waiting).Code.ShouldBe("code-after-the-outage");
    }

    [Fact]
    public async Task The_wait_for_a_result_ends_when_the_owner_leaves_none()
    {
        var timing = McpOAuthTestKit.FastTiming with { OutcomeWait = TimeSpan.FromMilliseconds(80) };
        var rendezvous = new McpOAuthRendezvous(
            McpOAuthTestKit.Backend(new RecordingDistributedCache(), timing: timing),
            new RecordingLoggerFactory().CreateLogger("test"));

        (await rendezvous.AwaitOutcomeAsync(State, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task The_result_reaches_the_instance_that_waits_for_it()
    {
        var rendezvous = Rendezvous(new RecordingDistributedCache());

        var waiting = rendezvous.AwaitOutcomeAsync(State, CancellationToken.None);
        await rendezvous.PublishOutcomeAsync(State, new McpOutcomeEnvelope(Success: true, "prod", Error: null));

        var outcome = (await waiting).ShouldNotBeNull();
        outcome.Success.ShouldBeTrue();
        outcome.ServerName.ShouldBe("prod");
    }

    [Theory]
    [InlineData("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk", true)]
    [InlineData("0123456789abcdef", true)]
    [InlineData("0123456789abcde", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("dBjftJeZ4CVP mB92K27uhbUJU1p1r_wW1gFWFOEjXk", false)]
    [InlineData("dBjftJeZ4CVP:mB92K27uhbUJU1p1r_wW1gFWFOEjXk", false)]
    [InlineData("<script>alert(1)</script>-padding-padding", false)]
    [InlineData("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXé", false)]
    public void Only_a_value_shaped_like_an_sdk_state_is_accepted(string? state, bool expected)
        => McpOAuthRendezvous.IsAcceptableState(state).ShouldBe(expected);

    [Fact]
    public void A_state_has_a_ceiling_in_length()
    {
        McpOAuthRendezvous.IsAcceptableState(new string('a', 128)).ShouldBeTrue();
        McpOAuthRendezvous.IsAcceptableState(new string('a', 129)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(4096, 2048, true)]
    [InlineData(4097, 0, false)]
    [InlineData(10, 2049, false)]
    public void The_values_of_a_redirect_have_ceilings(int codeLength, int issuerLength, bool expected)
        => McpOAuthRendezvous.AreAcceptableValues(new string('c', codeLength), issuerLength == 0 ? null : new string('i', issuerLength)).ShouldBe(expected);

    private static McpOAuthRendezvous Rendezvous(RecordingDistributedCache cache)
        => new(McpOAuthTestKit.Backend(cache), new RecordingLoggerFactory().CreateLogger("test"));
}
