using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Pins the SSE event names the server writes to <c>docs/sozlesme/sse-olaylari.txt</c> (Phase 238, section 238.2).
/// </summary>
/// <remarks>
/// <para>
/// A client switches on the <c>event:</c> name, so removing or renaming one is a breaking change in 1.x; a new name
/// is not. Two streams carry two vocabularies, and the snapshot keeps them apart by a prefix:
/// <c>run-events</c> is <c>GET /api/runs/{id}/events</c>, whose names come from <c>RunEndpoints.EventName</c>, and
/// <c>agent-stream</c> is the stream a direct run opens (<c>run</c>, <c>update</c>, <c>done</c>, <c>error</c>, …).
/// </para>
/// <para>
/// The first group is read the way a client reads it. A run holds one event of every <c>RunEventType</c> and the
/// stream is fetched over HTTP, so the mapping is not read from source and no internal type is exposed to the test.
/// The second group has no store to seed: each name is a literal in a <c>WriteEventAsync</c> call, so the names are
/// read from those calls. A stream added elsewhere with a name that is not a literal at the call site would not be
/// seen; <see cref="The_scan_finds_the_direct_stream_names_it_is_known_to_find"/> keeps the scan from going blind.
/// </para>
/// </remarks>
public sealed class SseEventNameSnapshotTests
{
    private static readonly string[] DirectStreamSources =
    [
        "src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs",
        "src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs",
        "src/Tracon.AspNetCore/Streaming/RunNoticeMirror.cs",
    ];

    // The second argument of WriteEventAsync(id, eventName, data, token) when it is a string literal.
    private static readonly Regex EventNameArgument = new(
        """WriteEventAsync\(\s*[^,()]+(?:\([^)]*\))?\s*,\s*"(?<name>[^"]+)"\s*,""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    // `internal const string FrameName = "custom";` - the notice mirror passes the constant, not a literal.
    private static readonly Regex FrameNameConstant = new(
        """const\s+string\s+FrameName\s*=\s*"(?<name>[^"]+)"\s*;""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public async Task The_snapshot_matches_the_event_names_the_server_writes()
    {
        var names = (await ReadRunEventNamesAsync()).Select(static name => $"run-events {name}")
            .Concat(ReadDirectStreamNames().Select(static name => $"agent-stream {name}"))
            .ToList();

        ContractSnapshot.Verify(
            "docs/sozlesme/sse-olaylari.txt",
            "tests/Tracon.AspNetCore.FunctionalTests/SseEventNameSnapshotTests.cs",
            "The SSE event names the server writes (run-events: the recorded run stream, agent-stream: a direct run's stream)",
            names);
    }

    [Fact]
    public async Task Every_run_event_type_reaches_the_wire_under_a_name_of_its_own()
    {
        var names = await ReadRunEventNamesAsync();

        names.Count.ShouldBe(Enum.GetValues<RunEventType>().Length);
        names.ShouldNotContain(static name => string.Equals(name, "unknown", StringComparison.Ordinal), "A RunEventType fell through the name table to the catch-all.");
    }

    [Fact]
    public void The_scan_finds_the_direct_stream_names_it_is_known_to_find()
    {
        // A scan that finds nothing would keep the snapshot test green by leaving the group out.
        ReadDirectStreamNames().ShouldBe(["approvals", "custom", "done", "error", "event", "inputs", "loop.iteration-completed", "run", "update"]);
    }

    [Theory]
    [InlineData("await writer.WriteEventAsync(sequence++, \"update\", payload, token);", "update")]
    [InlineData("await writer.WriteEventAsync(\n    sequence,\n    \"done\",\n    Json(x),\n    token);", "done")]
    [InlineData("=> writer.WriteEventAsync(\n    sequence,\n    \"error\",\n    JsonSerializer.Serialize(new F(nameof(X), m), JsonOptions),\n    CancellationToken.None);", "error")]
    public void The_scan_reads_the_event_name_argument(string call, string expected)
    {
        EventNameArgument.Match(call).Groups["name"].Value.ShouldBe(expected);
    }

    private static async Task<List<string>> ReadRunEventNamesAsync()
    {
        await using var host = await TraconTestHost.StartAsync();

        var store = host.Services.GetRequiredService<IRunStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;
        var runId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await store.StartRunAsync(
            new RunStartInfo { RunId = runId, AgentName = "echo-agent", StartedAt = now, TenantId = tenant },
            TestContext.Current.CancellationToken);

        long sequence = 0;

        foreach (var type in Enum.GetValues<RunEventType>())
        {
            await store.AppendEventAsync(
                new RunEvent
                {
                    RunId = runId,
                    Sequence = sequence++,
                    Type = type,
                    Timestamp = now,
                    CustomType = type == RunEventType.Custom ? "tracon.test" : null,
                    TenantId = tenant,
                },
                TestContext.Current.CancellationToken);
        }

        await store.CompleteRunAsync(
            new RunCompletion { RunId = runId, Status = RunStatus.Completed, CompletedAt = now, EventCount = sequence },
            TestContext.Current.CancellationToken);

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return [.. body
            .Split('\n')
            .Where(static line => line.StartsWith("event: ", StringComparison.Ordinal))
            .Select(static line => line["event: ".Length..].Trim())];
    }

    private static List<string> ReadDirectStreamNames()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var source in DirectStreamSources)
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot.Path, source));

            foreach (Match match in EventNameArgument.Matches(text))
            {
                names.Add(match.Groups["name"].Value);
            }

            foreach (Match match in FrameNameConstant.Matches(text))
            {
                names.Add(match.Groups["name"].Value);
            }
        }

        return [.. names];
    }
}
