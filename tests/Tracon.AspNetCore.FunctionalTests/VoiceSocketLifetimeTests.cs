using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// How a voice conversation ends on a real socket: the close frame the client reads, the reason the record carries, and the
/// limits that fire.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 These run on Kestrel and not on the in-process test server. Cancelling a pending receive aborts a real WebSocket
/// (the connection is reset, no close frame goes out, and the request is reported as aborted); the test server's socket
/// does not. A close that is the server's own (the agent ended it, a limit fired) was a reset recorded as a shutdown on
/// every real host and no test saw it.
/// </para>
/// <para>
/// Time is the clock's: no case waits for a limit. Each waits until the timer it is about is armed and then moves the clock.
/// </para>
/// </remarks>
public sealed class VoiceSocketLifetimeTests
{
    [Theory]
    [InlineData("waits")]
    [InlineData("closes-output")]
    [InlineData("aborts")]
    public async Task A_conversation_the_agent_ends_is_closed_cleanly_and_recorded_as_the_agents(string clientBehavior)
    {
        await using var host = await KestrelVoiceHost.StartAsync(agentEnds: true);
        var (socket, client) = await host.ConnectAsync();

        await using var _ = client;

        await KestrelVoiceHost.SpeakAsync(client);

        var frames = new List<VoiceEvent>();
        var ended = await client.WaitForAsync("ended", frames);

        ended.Text("reason").ShouldBe("agent");

        switch (clientBehavior)
        {
            case "waits":
                // The client that waits reads a close frame, and not the end of a connection that was reset.
                (await client.ReceiveAsync()).ShouldBeNull("the server closes the socket after `ended`");
                client.CloseStatus.ShouldBe(WebSocketCloseStatus.NormalClosure);
                client.CloseDescription.ShouldBe("The conversation was ended by the agent.");

                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);
                break;

            case "closes-output":
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);
                break;

            default:
                // The client drops the connection the moment it reads `ended`: the end reason was already decided.
                socket.Abort();
                break;
        }

        (await host.WaitForRecordAsync()).EndReason.ShouldBe(VoiceSessionEndReason.Agent);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("closes-output")]
    [InlineData("aborts")]
    public async Task A_conversation_the_client_closes_first_is_recorded_as_the_clients(string how)
    {
        await using var host = await KestrelVoiceHost.StartAsync();
        var (socket, client) = await host.ConnectAsync();

        await using var _ = client;

        switch (how)
        {
            case "stop":
                await client.SendControlAsync(new { type = "stop" });
                (await client.ReceiveAsync()).ShouldBeNull();
                client.CloseDescription.ShouldBe("The conversation closed.");
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);
                break;

            case "closes-output":
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);
                break;

            default:
                socket.Abort();
                break;
        }

        (await host.WaitForRecordAsync()).EndReason.ShouldBe(VoiceSessionEndReason.Client);
    }

    [Fact]
    public async Task An_idle_connection_is_closed_cleanly_and_recorded_as_idle()
    {
        var idle = TimeSpan.FromSeconds(437);

        await using var host = await KestrelVoiceHost.StartAsync(options =>
        {
            options.IdleTimeout = idle;
            options.MaxConnectionDuration = TimeSpan.FromHours(1);
        });

        var (socket, client) = await host.ConnectAsync();

        await using var _ = client;

        await host.WaitForTimerAsync(idle);
        host.Clock.Advance(idle);

        (await client.ReceiveAsync()).ShouldBeNull("the server closes an idle connection with a close frame");
        client.CloseStatus.ShouldBe(WebSocketCloseStatus.NormalClosure);
        client.CloseDescription.ShouldBe("The connection stayed idle.");

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);

        (await host.WaitForRecordAsync()).EndReason.ShouldBe(VoiceSessionEndReason.IdleTimeout);
    }

    [Fact]
    public async Task The_global_duration_limit_closes_cleanly_without_an_ended_frame()
    {
        var limit = TimeSpan.FromSeconds(613);

        await using var host = await KestrelVoiceHost.StartAsync(options =>
        {
            options.MaxConnectionDuration = limit;
            options.IdleTimeout = TimeSpan.FromHours(1);
        });

        var (socket, client) = await host.ConnectAsync();

        await using var _ = client;

        await host.WaitForTimerAsync(limit);
        host.Clock.Advance(limit);

        // A client that is told nothing but the close reconnects: that is what the limit has always meant.
        (await client.ReceiveAsync()).ShouldBeNull();
        client.CloseStatus.ShouldBe(WebSocketCloseStatus.NormalClosure);
        client.CloseDescription.ShouldBe("The connection reached the duration limit.");

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);

        (await host.WaitForRecordAsync()).EndReason.ShouldBe(VoiceSessionEndReason.DurationLimit);
    }

    [Fact]
    public async Task A_limit_the_host_set_ends_the_conversation_and_tells_the_client_not_to_come_back()
    {
        var limit = TimeSpan.FromSeconds(211);

        await using var host = await KestrelVoiceHost.StartAsync(
            options => options.IdleTimeout = TimeSpan.FromHours(1),
            resolve: (_, _) => ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings { MaxDuration = limit }));

        var (socket, client) = await host.ConnectAsync();

        await using var _ = client;

        await host.WaitForTimerAsync(limit);
        host.Clock.Advance(limit);

        var frames = new List<VoiceEvent>();
        var ended = await client.WaitForAsync("ended", frames);

        ended.Text("reason").ShouldBe("duration");
        VoiceFrameSchemaTests.ConformToSchema(frames);

        (await client.ReceiveAsync()).ShouldBeNull();
        client.CloseStatus.ShouldBe(WebSocketCloseStatus.NormalClosure);
        client.CloseDescription.ShouldBe("The conversation reached its time limit.");

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);

        (await host.WaitForRecordAsync()).EndReason.ShouldBe(VoiceSessionEndReason.DurationLimit);
    }

    [Fact]
    public async Task A_limit_reached_in_the_middle_of_a_turn_cuts_the_turn_and_then_says_the_conversation_is_over()
    {
        var limit = TimeSpan.FromSeconds(307);

        await using var host = await KestrelVoiceHost.StartAsync(
            options => options.IdleTimeout = TimeSpan.FromHours(1),
            resolve: (_, _) => ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings { MaxDuration = limit }));

        // The synthesis holds, so the turn is still in progress when the limit is reached.
        host.Voice.HoldSynthesis = true;

        var (socket, client) = await host.ConnectAsync();

        await using var _ = client;

        await KestrelVoiceHost.SpeakAsync(client);
        await client.WaitForAsync("audioStart");

        await host.WaitForTimerAsync(limit);
        host.Clock.Advance(limit);

        var frames = new List<VoiceEvent>();
        var done = await client.WaitForAsync("done", frames);

        done.Flag("cancelled").ShouldBe(true);

        // `done` closes the turn the limit cut off, and only then does the client learn the conversation is over.
        (await client.WaitForAsync("ended", frames)).Text("reason").ShouldBe("duration");
        VoiceFrameSchemaTests.ConformToSchema(frames);

        // One `ended` only, and it is the limit's.
        frames.Count(static frame => string.Equals(frame.Type, "ended", StringComparison.Ordinal)).ShouldBe(1);

        (await client.ReceiveAsync()).ShouldBeNull();
        client.CloseDescription.ShouldBe("The conversation reached its time limit.");

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);

        (await host.WaitForRecordAsync()).EndReason.ShouldBe(VoiceSessionEndReason.DurationLimit);
    }

    [Fact]
    public async Task A_host_that_stops_closes_the_open_conversation_and_records_the_shutdown()
    {
        await using var host = await KestrelVoiceHost.StartAsync();
        var (socket, client) = await host.ConnectAsync();

        await using var _ = client;

        var stopping = host.StopAsync();

        (await client.ReceiveAsync()).ShouldBeNull("the server closes the conversation when the host stops");
        client.CloseStatus.ShouldBe(WebSocketCloseStatus.NormalClosure);
        client.CloseDescription.ShouldBe("The server is shutting down.");

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);
        await stopping.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        (await host.WaitForRecordAsync()).EndReason.ShouldBe(VoiceSessionEndReason.ServerShutdown);
    }

    [Fact]
    public async Task A_client_that_never_answers_the_close_does_not_hold_the_connection()
    {
        await using var host = await KestrelVoiceHost.StartAsync(agentEnds: true);
        var (socket, client) = await host.ConnectAsync();

        await using var _ = client;

        await KestrelVoiceHost.SpeakAsync(client);
        await client.WaitForAsync("ended");

        // The client reads nothing more and sends nothing: a ClientWebSocket answers a close frame only while it receives.
        // The conversation was ended and holds a slot until the server stops waiting for that answer.
        (await host.WaitForRecordAsync()).EndReason.ShouldBe(VoiceSessionEndReason.Agent);

        // The wait for the answer is five seconds, and neither shorter nor longer: the slot is still held four seconds in and is
        // released at five.
        var bound = TimeSpan.FromSeconds(5);

        await host.WaitForTimerAsync(bound);

        host.Clock.Advance(TimeSpan.FromSeconds(4));
        host.Limiter.CountFor("default").ShouldBe(1, "the server still waits for the client's answer");

        host.Clock.Advance(TimeSpan.FromSeconds(1));

        await WaitUntil.TrueAsync(() => host.Limiter.CountFor("default") == 0, "the connection slot to be released");

        socket.Abort();
    }

    [Fact]
    public async Task A_finished_conversation_leaves_none_of_its_own_timers_armed()
    {
        var idle = TimeSpan.FromSeconds(437);
        var limit = TimeSpan.FromSeconds(3617);

        await using var host = await KestrelVoiceHost.StartAsync(
            options =>
            {
                options.IdleTimeout = idle;
                options.MaxConnectionDuration = limit;
            },
            agentEnds: true);

        var (socket, client) = await host.ConnectAsync();

        await using var _ = client;

        await host.WaitForTimerAsync(idle);
        await KestrelVoiceHost.SpeakAsync(client);
        await client.WaitForAsync("ended");
        (await client.ReceiveAsync()).ShouldBeNull();

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => host.Limiter.CountFor("default") == 0, "the connection slot to be released");

        // The waits of the connection (its idle limit and the time left of its duration limit) are timers of this clock: none of
        // them outlives the connection.
        host.Clock.Pending.ShouldNotContain(idle);
        host.Clock.Pending.ShouldNotContain(limit);
    }

    [Fact]
    public async Task A_conversation_the_host_resolver_refuses_is_closed_with_the_error_description()
    {
        await using var host = await KestrelVoiceHost.StartAsync(
            resolve: static (_, _) => throw new InvalidOperationException("a failure of the host's own code"));

        var (socket, client) = await host.OpenAsync();

        await using var _ = client;

        await client.SendControlAsync(new { type = "start", agent = KestrelVoiceHost.Agent, inputFormat = "pcm16" });

        var frames = new List<VoiceEvent>();

        (await client.WaitForAsync("error", frames)).Text("message").ShouldBe("The conversation could not be started.");

        (await client.ReceiveAsync()).ShouldBeNull();
        client.CloseStatus.ShouldBe(WebSocketCloseStatus.NormalClosure);
        client.CloseDescription.ShouldBe("The conversation closed with an error.");

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);

        (await host.WaitForRecordAsync()).EndReason.ShouldBe(VoiceSessionEndReason.Error);
    }

    [Fact]
    public async Task A_record_that_cannot_be_written_does_not_take_the_close_with_it()
    {
        // A database that is down throws its own exception type, not Tracon's. The record is observability: the client still
        // reads the close the server owes it, and the connection slot is released.
        await using var host = await KestrelVoiceHost.StartAsync(
            agentEnds: true,
            configureServices: static services => services.Replace(
                ServiceDescriptor.Singleton<IVoiceSessionStore>(new UnwritableStore())));

        var (socket, client) = await host.ConnectAsync();

        await using var _ = client;

        await KestrelVoiceHost.SpeakAsync(client);
        await client.WaitForAsync("ended");

        (await client.ReceiveAsync()).ShouldBeNull("the server closes the socket although it could not write the record");
        client.CloseStatus.ShouldBe(WebSocketCloseStatus.NormalClosure);
        client.CloseDescription.ShouldBe("The conversation was ended by the agent.");

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => host.Limiter.CountFor("default") == 0, "the connection slot to be released");
    }

    private sealed class UnwritableStore : IVoiceSessionStore
    {
        public ValueTask SaveAsync(VoiceSessionRecord record, CancellationToken cancellationToken = default)
            => throw new IOException("the database is down");

        public ValueTask<IReadOnlyList<VoiceSessionRecord>> QueryAsync(
            string tenantId, VoiceSessionQuery query, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceSessionRecord>>([]);
    }
}
