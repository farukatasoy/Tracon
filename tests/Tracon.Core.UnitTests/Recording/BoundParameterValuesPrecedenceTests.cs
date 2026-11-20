using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// The two carriers of the parameter values a run records: the run option an internal caller (queue, batch,
/// replay, workflow step) fills, and the marker the catalog puts on an agent it compiled for resolve-time values.
/// </summary>
/// <remarks>
/// A run records ONE set. When both carriers exist the run option wins, because an internal caller decided its set
/// before it ran the agent; the marker is for the caller that has no option to fill. A wrong order would write a
/// claim the run never followed, and a replay would repeat it.
/// </remarks>
public sealed class BoundParameterValuesPrecedenceTests
{
    private static readonly IReadOnlyDictionary<string, string> FromMarker =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "marker" };

    private static readonly IReadOnlyDictionary<string, string> FromOption =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "option" };

    [Fact]
    public async Task The_run_option_wins_when_the_agent_also_carries_a_marker()
    {
        var (agent, inputs, runs) = Build(marker: FromMarker);

        await agent.RunAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            session: null,
            new TraconRunOptions { ParameterValues = FromOption },
            TestContext.Current.CancellationToken);

        (await RecordedAsync(inputs, runs)).ShouldBe(FromOption);
    }

    [Fact]
    public async Task The_marker_is_recorded_when_the_run_carries_no_option()
    {
        var (agent, inputs, runs) = Build(marker: FromMarker);

        await agent.RunAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        (await RecordedAsync(inputs, runs)).ShouldBe(FromMarker);
    }

    [Fact]
    public async Task A_run_with_neither_records_no_values()
    {
        var (agent, inputs, runs) = Build(marker: null);

        await agent.RunAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        (await RecordedAsync(inputs, runs)).ShouldBeNull();
    }

    [Fact]
    public async Task A_marker_with_an_empty_set_records_an_empty_set_not_nothing()
    {
        // NULL is "no record", {} is "recorded and empty". A resolve with an empty set binds and records the
        // defaults, and an agent with no declared default still records the empty set it was bound with.
        var (agent, inputs, runs) = Build(marker: new Dictionary<string, string>(StringComparer.Ordinal));

        await agent.RunAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        (await RecordedAsync(inputs, runs)).ShouldNotBeNull().ShouldBeEmpty();
    }

    private static (AIAgent Agent, InMemoryRunInputStore Inputs, InMemoryRunStore Runs) Build(
        IReadOnlyDictionary<string, string>? marker)
    {
        var tenant = new FixedTenantContext("test");
        var runs = new InMemoryRunStore(tenantContext: tenant);
        var inputs = new InMemoryRunInputStore();
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(new FakeChatClient())),
            TestData.Registry());

        AIAgent inner = compiler.Compile(TestData.Definition());

        if (marker is not null)
        {
            inner = new BoundParameterValues(inner, marker);
        }

        var recording = TestRunRecording.Agent(
            inner,
            runs,
            tenant,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            runInputStore: inputs);

        return (recording, inputs, runs);
    }

    private static async Task<IReadOnlyDictionary<string, string>?> RecordedAsync(InMemoryRunInputStore inputs, InMemoryRunStore runs)
    {
        var run = (await runs.QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        var record = await inputs.GetAsync("test", run.Id, TestContext.Current.CancellationToken);

        return record.ShouldNotBeNull().Parameters;
    }
}
