using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 68 over HTTP: where run attribution may come from, where it may not,
/// and what happens when it breaks a limit.
/// </summary>
/// <remarks>
/// These live at the FUNCTIONAL level on purpose. The claim under test is that a
/// value cannot cross the HTTP boundary into the cost record — a unit test of the
/// gate would prove the gate works, not that it is wired in front of the run.
/// </remarks>
public sealed class RunAttributionEndpointTests
{
    private static readonly Uri Run = new("/tracon/api/agents/kod-agent/run", UriKind.Relative);
    private static readonly Uri Runs = new("/tracon/api/runs", UriKind.Relative);

    [Fact]
    public async Task A_userId_in_the_request_body_is_ignored()
    {
        // 🚨 The spoofing case. If the body could set the user, any client could
        // write spend against another user's name and forge the cost record. The
        // body is not a source of attribution at all — the field is simply not
        // bound, and the recorded run carries what the server resolved.
        await using var host = await StartAsync(new StubAttribution("real-user", labels: null));

        using var response = await host.Client.PostAsJsonAsync(
            Run,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["message"] = "hello",
                ["userId"] = "attacker",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var record = await SingleRunAsync(host);
        record.UserId.ShouldBe("real-user");
        string.Equals(record.UserId, "attacker", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task A_userId_in_the_body_cannot_invent_attribution_where_there_is_none()
    {
        // The same claim from the other side: with no IRunAttributionContext
        // registered, a body field must not become the recorded user either.
        await using var host = await StartAsync(attribution: null);

        using var response = await host.Client.PostAsJsonAsync(
            Run,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["message"] = "hello",
                ["userId"] = "attacker",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var record = await SingleRunAsync(host);
        record.UserId.ShouldBeNull();
    }

    [Fact]
    public async Task Nothing_changes_when_no_attribution_context_is_registered()
    {
        // The default an application gets for free: the run works and the columns
        // stay NULL. This is the "no surprises" guarantee.
        await using var host = await StartAsync(attribution: null);

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var record = await SingleRunAsync(host);
        record.Status.ShouldBe(RunStatus.Completed);
        record.UserId.ShouldBeNull();
        record.Labels.ShouldBeNull();
    }

    [Fact]
    public async Task A_resolved_user_and_labels_reach_the_run_record()
    {
        await using var host = await StartAsync(new StubAttribution(
            "ada",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" }));

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var record = await SingleRunAsync(host);
        record.UserId.ShouldBe("ada");
        record.Labels.ShouldNotBeNull();
        record.Labels!["team"].ShouldBe("payments");
    }

    [Fact]
    public async Task Too_many_labels_return_400_and_the_run_never_starts()
    {
        // 🚨 Rejected, not trimmed. A trimmed label set still reads as a complete
        // measurement to whoever queries the report later.
        var nine = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < RunLabels.MaxCount + 1; index++)
        {
            nine[$"key-{index}"] = "value";
        }

        await using var host = await StartAsync(new StubAttribution("ada", nine));

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain(RunLabels.MaxCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // The run must not exist at all: a rejected request costs nothing.
        var runs = host.Services.GetRequiredService<IRunStore>();
        (await runs.QueryRunsAsync(new RunQuery())).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_oversized_label_value_returns_400()
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["note"] = new('x', RunLabels.MaxValueLength + 1),
        };

        await using var host = await StartAsync(new StubAttribution(null, labels));

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("note");
    }

    [Fact]
    public async Task The_run_list_filters_by_user_and_by_label()
    {
        await using var host = await StartAsync(new StubAttribution(
            "ada",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" }));

        using (var first = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await CountAsync(host, "?userId=ada")).ShouldBe(1);
        (await CountAsync(host, "?userId=grace")).ShouldBe(0);
        (await CountAsync(host, "?label=team:payments")).ShouldBe(1);
        (await CountAsync(host, "?label=team:billing")).ShouldBe(0);

        // A bare key matches any value of that key.
        (await CountAsync(host, "?label=team")).ShouldBe(1);
        (await CountAsync(host, "?label=env")).ShouldBe(0);
    }

    [Fact]
    public async Task The_statistics_endpoint_breaks_down_by_user()
    {
        await using var host = await StartAsync(new StubAttribution(
            "ada",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" }));

        using (var first = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var stats = await host.Client.GetFromJsonAsync<RunStatistics>(
            new Uri("/tracon/api/stats", UriKind.Relative));

        stats.ShouldNotBeNull();
        stats.ByUser.ShouldHaveSingleItem().UserId.ShouldBe("ada");

        var label = stats.ByLabel.ShouldHaveSingleItem();
        label.Key.ShouldBe("team");
        label.Value.ShouldBe("payments");
    }

    private static async Task<int> CountAsync(TraconTestHost host, string query)
    {
        var records = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri(Runs.OriginalString + query, UriKind.Relative));

        return records!.Count;
    }

    private static async Task<RunRecord> SingleRunAsync(TraconTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        return (await runs.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
    }

    private static Task<TraconTestHost> StartAsync(IRunAttributionContext? attribution)
        => TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services =>
            {
                if (attribution is not null)
                {
                    // Registered BEFORE Tracon's own TryAdd, the way a consumer
                    // binding its identity pipeline would (K4: the consumer wins).
                    services.Replace(ServiceDescriptor.Singleton(attribution));
                }
            });

    private sealed class StubAttribution(string? userId, IReadOnlyDictionary<string, string>? labels)
        : IRunAttributionContext
    {
        public string? UserId => userId;

        public IReadOnlyDictionary<string, string>? Labels => labels;
    }
}
