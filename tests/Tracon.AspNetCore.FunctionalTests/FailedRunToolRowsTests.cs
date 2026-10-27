using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run that does not stream keeps the rows and the events of the tool calls it made, whether it ends or fails (F-689).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The recorder read a tool call from the messages of the model's answer, and a run that does not stream
/// (a queued run, a continuation, a workflow agent step, the <c>/v1/*</c> endpoints) has those messages only when it
/// succeeds. A run that ended in an exception wrote no row and no event for the calls it had made, and a run that
/// succeeded wrote a row with no duration. The operator who needs the trail most, when the run failed, had none.
/// </para>
/// <para>
/// The calls are now written at the outermost point of the tool chain, in a ledger the run opens, and read once when
/// the run closes. Every scenario here runs through the real host and the real tool-call loop; a tool body is called
/// by Microsoft Agent Framework, never by the test.
/// </para>
/// </remarks>
public sealed class FailedRunToolRowsTests
{
    private const string Provider = "rows-provider";
    private const string Model = "rows-model";
    private const string Agent = "rows-agent";
    private const string First = "spend_first";
    private const string Second = "spend_second";

    [Fact]
    public async Task A_queued_run_that_ends_on_a_budget_refusal_lists_the_call_that_ran_and_the_call_that_was_refused()
    {
        var clock = new SteppedClock();
        var tools = new ToolBodies(clock);

        await using var host = await StartAsync(
            Script(),
            tools.Speaks(First, amount: 5m, takes: TimeSpan.FromMilliseconds(250)),
            tools.Counts(Second),
            options => options.AgentGraph.MaxTotalCost = 1m,
            clock);

        var runId = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(runId, "Failed")).ShouldBe("Failed");

        var rows = await ToolRowsAsync(host, runId);

        rows.Count.ShouldBe(2, "a tool call is one row, however the run ended");

        var first = rows.Single(static row => string.Equals(row.ToolName, First, StringComparison.Ordinal));
        var second = rows.Single(static row => string.Equals(row.ToolName, Second, StringComparison.Ordinal));

        first.Succeeded.ShouldBeTrue();
        first.Duration.ShouldBe(TimeSpan.FromMilliseconds(250));
        second.Succeeded.ShouldBeFalse("a refused call was recorded as a success");
        second.Error.ShouldNotBeNull().ShouldContain("The tool call did not start.", Case.Sensitive);
        tools.Count(Second).ShouldBe(0, "the refused tool's body was called");
    }

    // --- Hosts ---

    private static FakeModelProvider Script()
        => new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .ForModel(Model, cfg => cfg
                .CallsTools((First, null), (Second, null))
                .EchoesLastToolResult("Done: ", inputTokens: 4, outputTokens: 6));

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        Func<ITraconBuilder, ITraconBuilder> first,
        Func<ITraconBuilder, ITraconBuilder> second,
        Action<TraconOptions> configureOptions,
        TimeProvider? clock = null,
        bool allowConcurrentToolCalls = false)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                second(first(builder))
                    .AddAgent(new AgentDefinition
                    {
                        Name = Agent,
                        Description = "Spends.",
                        Instructions = "Spend.",
                        Model = new ModelBinding
                        {
                            Provider = Provider,
                            Model = Model,
                            ToolLoop = new ToolLoopSettings { AllowConcurrentToolCalls = allowConcurrentToolCalls },
                        },
                        ToolNames = [First, Second],
                        Origin = AgentDefinitionOrigin.Code,
                    });
            },
            configureServices: services =>
            {
                services.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
                services.Configure<TraconOptions>(options =>
                {
                    options.Pricing.Currency = "USD";
                    configureOptions(options);
                });

                if (clock is not null)
                {
                    services.AddSingleton(clock);
                }
            });

    // --- Reading ---

    private static async Task<Guid> QueueAsync(TraconTestHost host, string session = "rows-session")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "go", SessionId = session }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    private static async Task<List<ToolInvocationRecord>> ToolRowsAsync(TraconTestHost host, Guid runId)
        => await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
               new Uri($"/tracon/api/runs/{runId}/tools", UriKind.Relative))
           ?? [];

    // --- Doubles ---

    /// <summary>
    /// A clock whose monotonic timestamp moves only when a tool body moves it, so a duration measured with it is exact.
    /// Its wall clock follows the real one (plus what was advanced) and its timers are the system's, because the job
    /// worker leases a job by the time the queue stamped it with and keeps polling by timer.
    /// </summary>
    private sealed class SteppedClock : TimeProvider
    {
        private long _advanced;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _advanced);

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + TimeSpan.FromTicks(Interlocked.Read(ref _advanced));

        public void Advance(TimeSpan amount) => Interlocked.Add(ref _advanced, amount.Ticks);
    }

    /// <summary>The tool bodies of one test, and how often each was called.</summary>
    private sealed class ToolBodies(SteppedClock? clock = null)
    {
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

        public int Count(string tool)
        {
            lock (_counts)
            {
                return _counts.GetValueOrDefault(tool);
            }
        }

        /// <summary>A tool that takes <paramref name="takes"/> of the test clock and reports an amount, as a speech tool does.</summary>
        public Func<ITraconBuilder, ITraconBuilder> Speaks(string tool, decimal amount, TimeSpan takes = default)
            => Run(tool, () =>
            {
                clock?.Advance(takes);
                TraconToolUsage.Report(new ToolCallUsage
                {
                    Unit = ToolUsageUnits.Characters,
                    Quantity = 100m,
                    Cost = amount,
                    Currency = "USD",
                }).ShouldBeTrue();
            });

        /// <summary>A tool that does nothing but count its calls.</summary>
        public Func<ITraconBuilder, ITraconBuilder> Counts(string tool)
            => Run(tool, static () => { });

        /// <summary>A tool that counts its calls and then runs <paramref name="body"/>.</summary>
        public Func<ITraconBuilder, ITraconBuilder> Run(string tool, Action body)
            => builder => builder.AddTool(
                (Func<Task<string>>)(() => Task.Run(() =>
                {
                    lock (_counts)
                    {
                        _counts[tool] = _counts.GetValueOrDefault(tool) + 1;
                    }

                    body();

                    return "spoken";
                })),
                name: tool);
    }
}
