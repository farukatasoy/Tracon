using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A function's settings type can change between deployments; a definition saved against the
/// old shape must fail NAMED at the start of the run, not run the function with a silent default.
/// </summary>
/// <remarks>
/// The save endpoint checks the settings against the type that is registered NOW. These tests
/// write the stored definition straight to the store, which is what an earlier deployment's
/// save left behind - the endpoint would refuse the same body today.
/// </remarks>
public sealed class WorkflowFunctionSettingsDriftTests
{
    private const string Legacy = """{"voice":"alloy","legacyField":true}""";

    [Fact]
    public async Task A_definition_saved_against_an_older_settings_type_fails_the_run_named_before_any_node_runs()
    {
        // 4 and 17: the compile repeats the read. It must fail the run - not hang it, not default.
        await using var host = await StartAsync();

        await SeedAsync(host, "narrated", Legacy);

        var run = await RunAsync(host, "narrated").WaitAsync(Patience);

        run.Record.Status.ShouldBe(RunStatus.Failed);

        var message = run.Record.Error.ShouldNotBeNull().Message;

        message.ShouldContain("Workflow 'narrated' cannot run", Case.Sensitive);
        message.ShouldContain("Function node 'tts' has invalid settings", Case.Sensitive);
        message.ShouldContain("'legacyField'", Case.Sensitive);
        message.ShouldContain("may have changed", Case.Sensitive);

        // Nothing ran before the refusal: the first node is the writer agent, and it opened no run row.
        var runs = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });

        runs.Count(static candidate => candidate.Kind == RunKind.Agent).ShouldBe(0);
        run.Frames.ShouldContain(static frame => frame.Data.Contains("\"type\":\"RunFailed\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_graph_of_such_a_definition_is_a_400_that_says_so_not_a_server_error()
    {
        await using var host = await StartAsync();

        await SeedAsync(host, "narrated", Legacy);

        using var response = await host.Client.GetAsync("/tracon/api/workflows/narrated/graph");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemOf(response)).GetProperty("detail").GetString()!.ShouldContain("'legacyField'", Case.Sensitive);
    }

    [Fact]
    public async Task Saving_the_definition_again_with_settings_that_fit_makes_it_run()
    {
        await using var host = await StartAsync();

        await SeedAsync(host, "narrated", Legacy);

        (await RunAsync(host, "narrated")).Record.Status.ShouldBe(RunStatus.Failed);

        await SaveOkAsync(host, "narrated", Chain("""{"voice":"alloy"}"""));

        var run = await RunAsync(host, "narrated");

        run.Record.Status.ShouldBe(RunStatus.Completed, run.Record.Error?.Message);
        run.Output.ShouldBe("voice=alloy;lufs=-16;speed=Normal");
    }

    [Fact]
    public async Task A_function_that_lost_its_settings_type_fails_the_run_instead_of_ignoring_the_settings()
    {
        // The registration below declares NO settings, but the stored node still carries some.
        await using var host = await Tracon.AspNetCore.FunctionalTests.Infrastructure.TraconTestHost.StartAsync(builder => builder
            .AddAgent(Tracon.AspNetCore.FunctionalTests.Infrastructure.TestData.Definition(Writer))
            .UseWorkflows()
            .AddWorkflowFunction<List<Microsoft.Extensions.AI.ChatMessage>, List<Microsoft.Extensions.AI.ChatMessage>>(
                Tts,
                static _ => static (messages, _, _) => new ValueTask<List<Microsoft.Extensions.AI.ChatMessage>>(messages)));

        await SeedAsync(host, "narrated", """{"voice":"alloy"}""");

        var run = await RunAsync(host, "narrated").WaitAsync(Patience);

        run.Record.Status.ShouldBe(RunStatus.Failed);
        run.Record.Error.ShouldNotBeNull().Message.ShouldContain("declares none", Case.Sensitive);
    }

    private static async Task SeedAsync(
        Tracon.AspNetCore.FunctionalTests.Infrastructure.TraconTestHost host,
        string name,
        string settings)
    {
        var tenant = host.Services.GetRequiredService<IOptions<TraconOptions>>().Value.DefaultTenantId;

        await host.Services.GetRequiredService<IWorkflowDefinitionStore>().SaveAsync(
            tenant,
            new WorkflowDefinition
            {
                Name = name,
                Kind = WorkflowKind.Sequential,
                Nodes =
                [
                    new WorkflowNodeReference { Name = Writer, Kind = WorkflowNodeKind.Agent },
                    new WorkflowNodeReference { Name = Tts, Kind = WorkflowNodeKind.Function, Settings = Json(settings) },
                ],
            });
    }
}
