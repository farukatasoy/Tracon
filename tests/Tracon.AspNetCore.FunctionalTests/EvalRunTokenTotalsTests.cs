using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The tokens an eval run reports are the tokens its case runs recorded, whatever the attempt that counted them.
/// </summary>
/// <remarks>
/// The first attempt of an eval job adds up what each case's response reports, and a retry that finds some cases done reads
/// those cases' tokens from their run records. The two have to be the same number for the same run, or a retried eval run would
/// report a different total than one that was never interrupted. A case that calls a tool spends tokens on both turns, so the
/// turn with the tool call is part of what is compared.
/// </remarks>
public sealed class EvalRunTokenTotalsTests
{
    private const string AgentName = "totals-agent";
    private const string Suite = "totals-suite";

    [Fact]
    public async Task The_tokens_of_an_eval_run_are_those_its_case_runs_recorded_tool_turn_included()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                builder
                    .AddTool((Func<string, string>)Echo, name: "echo", description: "Echoes.")
                    .AddModelProvider(new FakeModelProvider("totals-provider")
                        .WithModel(new ModelDescriptor { Name = "totals-1" })
                        .CallsTool("echo", new { text = "hi" }, inputTokens: 40, outputTokens: 12)
                        .RespondsWith("done", inputTokens: 55, outputTokens: 4))
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Echo what the user says.",
                        Model = new ModelBinding { Provider = "totals-provider", Model = "totals-1" },
                        ToolNames = ["echo"],
                        Origin = AgentDefinitionOrigin.Code,
                    })
                    .UseScheduling(static options =>
                    {
                        options.RunWorker = true;
                        options.PollInterval = TimeSpan.FromMilliseconds(20);
                    });
            });

        using (var suite = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/evals/{Suite}",
                   new EvalSuiteSaveRequest
                   {
                       AgentName = AgentName,
                       Checks = JsonDocument.Parse("""[{"kind":"nonEmpty","minLength":1}]""").RootElement,
                   },
                   TestContext.Current.CancellationToken))
        {
            suite.EnsureSuccessStatusCode();
        }

        using (var cases = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/evals/{Suite}/cases",
                   new object[] { new { query = "say hi" } },
                   TestContext.Current.CancellationToken))
        {
            cases.EnsureSuccessStatusCode();
        }

        using var triggered = await host.Client.PostAsJsonAsync($"/tracon/api/evals/{Suite}/run", new { }, TestContext.Current.CancellationToken);
        triggered.EnsureSuccessStatusCode();

        var evalRunId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();

        var detail = await WaitUntil.ValueAsync(
            async () =>
            {
                using var response = await host.Client.GetAsync($"/tracon/api/evals/runs/{evalRunId}", TestContext.Current.CancellationToken);

                return await TraconTestHost.ReadJsonAsync(response);
            },
            static body => body.GetProperty("run").GetProperty("status").GetString() is "Completed" or "Failed",
            "the eval run to finish");

        var run = detail.GetProperty("run");
        var caseRunId = detail.GetProperty("results").EnumerateArray().ShouldHaveSingleItem().GetProperty("runId").GetGuid();
        var usage = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(caseRunId, TestContext.Current.CancellationToken))
            .ShouldNotBeNull().Usage.ShouldNotBeNull();

        usage.InputTokens.ShouldBe(40 + 55);
        usage.OutputTokens.ShouldBe(12 + 4);
        ((long?)run.GetProperty("inputTokens").GetInt64()).ShouldBe(usage.InputTokens, "what the first attempt adds up is what the run recorded");
        ((long?)run.GetProperty("outputTokens").GetInt64()).ShouldBe(usage.OutputTokens);
    }

    private static string Echo(string text) => text;
}
