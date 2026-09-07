using System.Net;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism.AspNetCore.FunctionalTests.Evals;

/// <summary>Tests for <c>GET /api/evaluation/scores/summary</c>.</summary>
public sealed class RunScoreSummaryEndpointTests
{
    private const string SummaryPath = "/agentprism/api/evaluation/scores/summary";

    [Fact]
    public async Task An_empty_store_returns_empty_lists_not_an_error()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri(SummaryPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("byName").GetArrayLength().ShouldBe(0);
        body.GetProperty("series").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Written_scores_are_summarized_by_name()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var scores = host.Services.GetRequiredService<IRunScoreStore>();

        await scores.UpsertAsync(Score(AgentPrismId.NewId()) with { Author = "alice", Value = 1 });
        await scores.UpsertAsync(Score(AgentPrismId.NewId()) with { Author = "bob", Value = 0 });

        using var response = await host.Client.GetAsync(new Uri(SummaryPath, UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        var byName = body.GetProperty("byName");
        byName.GetArrayLength().ShouldBe(1);
        byName[0].GetProperty("key").GetString().ShouldBe("helpfulness");
        byName[0].GetProperty("kind").GetString().ShouldBe("Binary");
        byName[0].GetProperty("count").GetInt64().ShouldBe(2);
        byName[0].GetProperty("average").GetDouble().ShouldBe(0.5);
    }

    [Fact]
    public async Task From_after_to_returns_400()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var from = DateTimeOffset.UtcNow;
        var to = from.AddHours(-1);

        using var response = await host.Client.GetAsync(new Uri(
            $"{SummaryPath}?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}",
            UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task MaxRows_out_of_range_returns_400(int maxRows)
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri($"{SummaryPath}?maxRows={maxRows}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Categorical_scores_report_categories_not_an_average()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var scores = host.Services.GetRequiredService<IRunScoreStore>();

        await scores.UpsertAsync(Score(AgentPrismId.NewId()) with
        {
            Name = "severity",
            Kind = RunScoreKind.Categorical,
            Value = null,
            TextValue = "minor",
            Author = "alice",
        });
        await scores.UpsertAsync(Score(AgentPrismId.NewId()) with
        {
            Name = "severity",
            Kind = RunScoreKind.Categorical,
            Value = null,
            TextValue = "minor",
            Author = "bob",
        });

        using var response = await host.Client.GetAsync(new Uri(SummaryPath, UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        var byName = body.GetProperty("byName")[0];
        byName.GetProperty("kind").GetString().ShouldBe("Categorical");
        byName.GetProperty("average").ValueKind.ShouldBe(JsonValueKind.Null);
        byName.GetProperty("categories").GetProperty("minor").GetInt64().ShouldBe(2);
    }

    [Fact]
    public async Task Stars_and_numeric_scores_of_the_same_name_stay_separate()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var scores = host.Services.GetRequiredService<IRunScoreStore>();

        await scores.UpsertAsync(Score(AgentPrismId.NewId()) with
        {
            Name = "quality",
            Kind = RunScoreKind.Stars,
            Value = 5,
            Author = "alice",
        });
        await scores.UpsertAsync(Score(AgentPrismId.NewId()) with
        {
            Name = "quality",
            Kind = RunScoreKind.Numeric,
            Value = 80,
            Author = "bob",
        });

        using var response = await host.Client.GetAsync(new Uri(SummaryPath, UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        var byName = body.GetProperty("byName");
        byName.GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Bucket_adds_a_trend_series()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var scores = host.Services.GetRequiredService<IRunScoreStore>();

        // Within the endpoint's own default 90-day window for a bucketed
        // query with no explicit 'from' -- a fixed past date would silently
        // fall outside it as the test suite ages.
        var now = DateTimeOffset.UtcNow;
        await scores.UpsertAsync(Score(AgentPrismId.NewId()) with { CreatedAt = now, Author = "alice" });
        await scores.UpsertAsync(Score(AgentPrismId.NewId()) with { CreatedAt = now.AddDays(-2), Author = "bob" });

        using var response = await host.Client.GetAsync(new Uri($"{SummaryPath}?bucket=Day", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        // A sparse series: only the two days with a score, never every day
        // between them.
        body.GetProperty("series").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task A_bucketed_query_with_no_from_defaults_to_the_last_90_days()
    {
        // A trend series has no natural upper bound the way a breakdown does
        // (MaxRows caps the groups INSIDE one bucket, not the number of
        // buckets) -- an unbounded 'from' would let a long-lived tenant's
        // series grow forever. The default applies to the WHOLE query
        // (breakdowns included), not only the series: a caller that wants a
        // bucketed trend AND an unbounded breakdown passes its own 'from'.
        await using var host = await AgentPrismTestHost.StartAsync();
        var scores = host.Services.GetRequiredService<IRunScoreStore>();

        var now = DateTimeOffset.UtcNow;
        await scores.UpsertAsync(Score(AgentPrismId.NewId()) with { CreatedAt = now.AddDays(-1), Author = "alice" });
        await scores.UpsertAsync(Score(AgentPrismId.NewId()) with { CreatedAt = now.AddDays(-120), Author = "bob" });

        using var response = await host.Client.GetAsync(new Uri($"{SummaryPath}?bucket=Day", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        // The 120-day-old score falls outside the default window: its bucket
        // does not appear in the series, and it is excluded from byName too.
        body.GetProperty("series").GetArrayLength().ShouldBe(1);
        body.GetProperty("byName")[0].GetProperty("count").GetInt64().ShouldBe(1);
    }

    [Fact]
    public async Task An_explicit_from_overrides_the_bucketed_default_and_is_not_itself_bounded()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var scores = host.Services.GetRequiredService<IRunScoreStore>();

        var now = DateTimeOffset.UtcNow;
        await scores.UpsertAsync(Score(AgentPrismId.NewId()) with { CreatedAt = now.AddDays(-1), Author = "alice" });
        await scores.UpsertAsync(Score(AgentPrismId.NewId()) with { CreatedAt = now.AddDays(-120), Author = "bob" });

        var explicitFrom = Uri.EscapeDataString(now.AddDays(-365).ToString("O"));
        using var response = await host.Client.GetAsync(
            new Uri($"{SummaryPath}?bucket=Day&from={explicitFrom}", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        // A caller's own 'from' is used as given, however far back -- the
        // 90-day default only fills in when 'from' is absent entirely.
        body.GetProperty("series").GetArrayLength().ShouldBe(2);
        body.GetProperty("byName")[0].GetProperty("count").GetInt64().ShouldBe(2);
    }

    [Fact]
    public async Task Live_online_summary_still_resets_while_this_endpoint_does_not()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var scores = host.Services.GetRequiredService<IRunScoreStore>();

        await scores.UpsertAsync(Score(AgentPrismId.NewId()) with { Author = "alice" });

        // The live indicator only ever grows from RecordScoreAsync (the
        // online sampler path); a direct UpsertAsync -- what this test, and a
        // human reviewer, use -- never touches it. This is not new behavior,
        // just a regression guard for it: this endpoint's data source (the
        // persisted rows) is intentionally separate from the online window.
        using var online = await host.Client.GetAsync(new Uri("/agentprism/api/evaluation/online", UriKind.Relative));
        (await AgentPrismTestHost.ReadJsonAsync(online)).GetProperty("sampleCount").GetInt64().ShouldBe(0);

        using var summary = await host.Client.GetAsync(new Uri(SummaryPath, UriKind.Relative));
        (await AgentPrismTestHost.ReadJsonAsync(summary)).GetProperty("byName").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task ByAgent_resolves_the_scored_runs_agent_through_the_run_store()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var runs = host.Services.GetRequiredService<IRunStore>();
        var scores = host.Services.GetRequiredService<IRunScoreStore>();

        var runId = AgentPrismId.NewId();
        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "billing-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });
        await scores.UpsertAsync(Score(runId) with { Author = "alice" });

        using var response = await host.Client.GetAsync(new Uri(SummaryPath, UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        var byAgent = body.GetProperty("byAgent");
        byAgent.GetArrayLength().ShouldBe(1);
        byAgent[0].GetProperty("key").GetString().ShouldBe("billing-agent");
    }

    [Fact]
    public async Task A_failing_store_is_not_softened_into_an_empty_200()
    {
        // Never soften a real failure into an empty summary: that would read
        // as "no scores exist" instead of "the query failed". AgentPrism
        // installs no exception-handling middleware of its own (the
        // consuming app's job), so on the test host the store's exception
        // propagates all the way out rather than turning into a response --
        // exactly the proof that nothing along the way caught and swallowed it.
        await using var host = await AgentPrismTestHost.StartAsync(configureServices: static services =>
            services.Replace(ServiceDescriptor.Singleton<IRunScoreStore>(new ThrowingRunScoreStore())));

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await host.Client.GetAsync(new Uri(SummaryPath, UriKind.Relative)));
    }

    private static RunScore Score(Guid runId)
        => new()
        {
            TenantId = "default",
            RunId = runId,
            Name = "helpfulness",
            Kind = RunScoreKind.Binary,
            Value = 1,
            Source = "human",
            Author = "operator@example",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private sealed class ThrowingRunScoreStore : IRunScoreStore
    {
        public ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunScore>> ListAsync(string tenantId, Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<bool> DeleteAsync(string tenantId, Guid scoreId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<RunScoreSummary> SummarizeAsync(RunScoreQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The score store is unavailable.");
    }
}
