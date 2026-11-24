using System.Net;
using AGUI.Abstractions;
using Tracon.AGUI.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// An application can expose the AG-UI endpoint under more than one pattern, and one pattern cannot be mapped twice.
/// </summary>
/// <remarks>
/// <para>
/// Every mapping used to carry the same endpoint name. ASP.NET Core refuses two endpoints of one name, and it refuses at the
/// first request that needs the name, so the second mapping broke every endpoint of the application and the host started
/// without a word. The name now follows the pattern, and a pattern that is mapped a second time fails when it is mapped.
/// </para>
/// </remarks>
public sealed class AGUIMultipleMappingTests
{
    private const string SecondPattern = "/chat/agui";

    private static readonly DateTimeOffset FrozenNow = new(2030, 6, 1, 12, 0, 30, TimeSpan.Zero);

    [Fact]
    public async Task A_second_pattern_answers_a_run_and_leaves_the_first_one_working()
    {
        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            afterMap: static app => app.MapTraconAGUI(SecondPattern));

        var first = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-first", "hello"));
        var second = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-second", "hello"), pattern: SecondPattern);

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);
        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        first.Finished.Outcome.ShouldBeOfType<RunFinishedSuccessOutcome>();
        second.Finished.Outcome.ShouldBeOfType<RunFinishedSuccessOutcome>();

        // The management API shares the endpoint table with both mappings; it must not be what breaks.
        using var agents = await host.Client.GetAsync(new Uri("/tracon/api/agents", UriKind.Relative), TestContext.Current.CancellationToken);

        agents.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Three_patterns_whose_names_would_read_alike_are_all_mapped()
    {
        // Two patterns can reduce to one name once the characters a name cannot hold are dropped: both must still map.
        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            afterMap: static app =>
            {
                app.MapTraconAGUI("/a/b");
                app.MapTraconAGUI("/a_b");
            });

        (await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-1", "hi"), pattern: "/a/b")).Status.ShouldBe(HttpStatusCode.OK);
        (await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-2", "hi"), pattern: "/a_b")).Status.ShouldBe(HttpStatusCode.OK);
        (await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-3", "hi"))).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(AGUITestHost.Pattern)]
    [InlineData("/TRACON/agui/")]
    public async Task The_same_pattern_mapped_twice_fails_when_it_is_mapped_and_names_the_pattern(string again)
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() => AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            afterMap: app => app.MapTraconAGUI(again)));

        exception.Message.ShouldContain("/tracon/agui");
        exception.Message.ShouldContain("already");
    }

    [Theory]
    [InlineData(RateLimitScope.Instance)]
    [InlineData(RateLimitScope.Deployment)]
    public async Task Two_patterns_spend_one_window_of_the_AG_UI_group(RateLimitScope scope)
    {
        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(new ManualClock(FrozenNow));
                services.Configure<TraconRateLimitOptions>(options =>
                {
                    options.Enabled = true;
                    options.PermitLimit = 2;
                    options.Window = TimeSpan.FromHours(1);
                    options.Scope = scope;
                });
            },
            afterMap: static app => app.MapTraconAGUI(SecondPattern));

        // Two permits for the group, not two for each pattern: a second mapping would otherwise double the budget.
        (await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-1", "hi"))).Status.ShouldBe(HttpStatusCode.OK);
        (await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-2", "hi"), pattern: SecondPattern)).Status.ShouldBe(HttpStatusCode.OK);

        var rejected = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-3", "hi"), pattern: SecondPattern);

        rejected.Status.ShouldBe(HttpStatusCode.TooManyRequests, rejected.Body);
        (await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-4", "hi"))).Status.ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
