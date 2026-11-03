using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run must not make a <see cref="Meter"/> of its own: a meter stays in the runtime's process-wide list until it is
/// disposed, and so does everything it holds, and a meter disposed after its run splits the series the OpenTelemetry SDK
/// exports into one series per run.
/// </summary>
/// <remarks>
/// <para>
/// Found by a soak that spread a steady load over thousands of tenants: the host's live managed heap rose by about
/// seven kilobytes a request with the tenant count fixed, and a heap dump held thousands of
/// <c>System.Diagnostics.Metrics.Meter</c> and <c>ActivitySource</c> instances, each with its four histograms. Counting
/// the instruments that are published, rather than the memory they take, is the same fact without a heap dump:
/// a process that creates its meters once publishes a fixed set, and one that creates a meter per run publishes a set
/// that grows with the runs. Publications are what stays flat only when the meters are shared; a count of live
/// instruments would also pass for a meter disposed after every run, which is the fix that breaks the exported series.
/// </para>
/// <para>
/// The listener cannot be bound to one <see cref="Meter"/> instance: the meter belongs to the chat client the agent
/// framework builds, which takes no <see cref="IMeterFactory"/>. Other tests in the process build agents too, so the
/// assertion leaves room for them: a leak publishes one set per run, 200 runs publish 200 sets, and the room is a quarter
/// of that.
/// </para>
/// </remarks>
public sealed class MeterChurnTests
{
    private const int WarmUpRuns = 5;
    private const int MeasuredRuns = 200;

    /// <summary>One of the four instruments a chat client's telemetry creates per instance; any one of them counts the instances.</summary>
    private const string PerClientInstrument = "gen_ai.client.token.usage";

    [Fact]
    public async Task Runs_after_the_first_few_do_not_make_a_new_telemetry_meter_for_the_same_agent_and_tenants()
    {
        var provider = new FakeModelProvider("probe").WithModel(new ModelDescriptor { Name = "probe-1" }).RespondsWith("ok");

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(Definition());
            builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            });
        });

        var announced = 0;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (string.Equals(instrument.Name, PerClientInstrument, StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref announced);
                }
            },
        };
        listener.Start();

        for (var run = 0; run < WarmUpRuns; run++)
        {
            await RunAsync(host, tenant: "tenant-a");
        }

        var before = Volatile.Read(ref announced);

        for (var run = 0; run < MeasuredRuns; run++)
        {
            await RunAsync(host, tenant: run % 2 == 0 ? "tenant-a" : "tenant-b");
        }

        var created = Volatile.Read(ref announced) - before;

        created.ShouldBeLessThan(
            MeasuredRuns / 4,
            $"{MeasuredRuns} further runs of one agent for two tenants announced {created} new '{PerClientInstrument}' instruments, "
            + "which means a telemetry Meter is created per run");
    }

    private static AgentDefinition Definition() => new()
    {
        Name = "meter-churn",
        Model = new ModelBinding { Provider = "probe", Model = "probe-1" },
        Instructions = "Reply briefly.",
        Origin = AgentDefinitionOrigin.Code,
    };

    private static async Task RunAsync(TraconTestHost host, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/agents/meter-churn/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.TryAddWithoutValidation("X-Tracon-Tenant", tenant);

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.ShouldBeTrue(body);
        body.ShouldNotContain("event: error", Case.Sensitive);
    }
}
