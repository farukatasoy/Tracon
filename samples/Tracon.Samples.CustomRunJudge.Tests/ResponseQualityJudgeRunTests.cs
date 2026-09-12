using System.Net;
using Tracon.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Samples.CustomRunJudge.Tests;

/// <summary>Crosses HTTP run, evaluation, and score-persistence boundaries.</summary>
public sealed class ResponseQualityJudgeRunTests
{
    private const string AgentName = "quality-probe";

    [Fact]
    public async Task A_real_run_can_be_judged_and_its_score_is_persisted()
    {
        var model = new FakeModelProvider("judge-sample")
            .RespondsWith("This is a sufficiently detailed response from the custom judge sample.");

        await using var host = await TraconTestHost.StartAsync(
            options =>
            {
                options.ModelProvider = model;
                options.ConfigureTracon = static builder => builder
                    .AddResponseQualityJudge()
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Return a detailed answer.",
                        Model = new ModelBinding { Provider = "judge-sample", Model = "fake-model" },
                    });
            },
            TestContext.Current.CancellationToken);

        var run = await host.RunAsync(
            AgentName,
            "Explain the result.",
            TestContext.Current.CancellationToken);

        using var response = await host.Client.PostAsync(
            $"/tracon/api/runs/{run.Record.Id}/judge",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var scores = await host.Services.GetRequiredService<IRunScoreStore>().ListAsync(
            "default",
            run.Record.Id,
            TestContext.Current.CancellationToken);
        var score = scores.ShouldHaveSingleItem();

        score.TenantId.ShouldBe("default");
        score.RunId.ShouldBe(run.Record.Id);
        score.Value.ShouldBe(65);
        score.Comment.ShouldBe("Deterministic quality rule.");
        score.Source.ShouldBe("judge:response-quality");
        score.Author.ShouldBe("judge:response-quality");
    }
}
