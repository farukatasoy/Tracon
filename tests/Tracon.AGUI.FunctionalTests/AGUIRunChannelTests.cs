using System.Net;
using Tracon.AGUI.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// The run authorization handler is told that a run came from the AG-UI endpoint, so a host that opens one agent on several
/// surfaces can allow it on this one and refuse it on another.
/// </summary>
public sealed class AGUIRunChannelTests
{
    [Fact]
    public async Task The_handler_is_asked_with_the_AGUI_channel_and_can_refuse_this_surface_alone()
    {
        var handler = new ChannelHandler(RunChannel.Voice);

        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureServices: services => services.AddSingleton<IRunAuthorizationHandler>(handler));

        var refused = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-channel", "hi"));

        refused.Status.ShouldBe(HttpStatusCode.Forbidden, refused.Body);
        handler.Seen.ShouldHaveSingleItem().Channel.ShouldBe(RunChannel.AGUI);
    }

    [Fact]
    public async Task A_surface_the_handler_allows_runs()
    {
        var handler = new ChannelHandler(RunChannel.AGUI);

        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureServices: services => services.AddSingleton<IRunAuthorizationHandler>(handler));

        var run = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-channel-ok", "hi"));

        run.Status.ShouldBe(HttpStatusCode.OK, run.Body);
        handler.Seen.ShouldHaveSingleItem().Channel.ShouldBe(RunChannel.AGUI);
    }

    /// <summary>Allows a run only on one channel, and records what it was asked.</summary>
    private sealed class ChannelHandler(RunChannel allowed) : IRunAuthorizationHandler
    {
        private readonly Lock _gate = new();
        private readonly List<RunAuthorizationRequest> _seen = [];

        public IReadOnlyList<RunAuthorizationRequest> Seen
        {
            get
            {
                lock (_gate)
                {
                    return [.. _seen];
                }
            }
        }

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _seen.Add(request);
            }

            return ValueTask.FromResult(request.Channel == allowed
                ? RunAuthorizationResult.Allow()
                : RunAuthorizationResult.Deny("This agent does not run on this surface."));
        }

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Allow());
    }
}
