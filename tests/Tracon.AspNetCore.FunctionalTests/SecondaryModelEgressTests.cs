using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The tenant's egress policy reaches every model an agent calls, across the
/// real HTTP and tenant boundary: a definition whose compaction summarizer or
/// <c>aiJudge</c> loop judge names a forbidden provider fails validation.
/// </summary>
/// <remarks>
/// 🚨 Regression (2026-10-11): both clients were built with the synchronous,
/// tenant-blind registry member. Validation accepted such a definition, and
/// every run sent the tenant's conversation to the forbidden provider. The
/// forbidden provider is registered on purpose: an unregistered one fails for
/// a different reason, and the test would pass without the policy ever running.
/// </remarks>
public sealed class SecondaryModelEgressTests
{
    private const string ForbiddenProvider = "forbidden";

    private static readonly Uri Validate = new("/tracon/api/agents/validate", UriKind.Relative);

    [Fact]
    public async Task A_summarization_model_on_a_forbidden_provider_fails_validation()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, TestData.Request() with
        {
            Compaction = new CompactionSettings
            {
                Strategy = CompactionStrategyKind.Summarization,
                TriggerMessages = 20,
                SummarizationModel = new ModelBinding { Provider = ForbiddenProvider, Model = "summarizer" },
            },
        });

        await ShouldBeRefusedByTheEgressPolicyAsync(response);
    }

    [Fact]
    public async Task An_aiJudge_whose_judge_model_is_on_a_forbidden_provider_fails_validation()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, TestData.Request() with
        {
            Harness = new HarnessSettings
            {
                Loop = new LoopSettings
                {
                    Criteria = [new LoopCriterion { Kind = "aiJudge", JudgeCriteria = ["Is the answer complete?"] }],
                },
            },
        });

        await ShouldBeRefusedByTheEgressPolicyAsync(response);
    }

    private static async Task<TraconTestHost> StartAsync()
    {
        var host = await TraconTestHost.StartAsync(configureTracon: static builder => builder
            .AddModelProvider(new FakeModelProvider(ForbiddenProvider))
            .AddModelRunJudge(static options => options.Model = new ModelBinding { Provider = ForbiddenProvider, Model = "judge" }));

        // The request carries no tenant header, so it runs as the default tenant.
        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/tenants/default/egress", UriKind.Relative),
            new { allowedProviders = new[] { TestData.Model().Provider } });

        saved.EnsureSuccessStatusCode();

        return host;
    }

    private static async Task ShouldBeRefusedByTheEgressPolicyAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("valid").GetBoolean().ShouldBeFalse();

        var messages = json.GetProperty("messages").EnumerateArray().ToArray();

        messages.ShouldContain(message =>
            string.Equals(message.GetProperty("code").GetString(), "compilation_error", StringComparison.Ordinal) &&
            (message.GetProperty("message").GetString() ?? string.Empty)
                .Contains($"not allowed to call model provider '{ForbiddenProvider}'", StringComparison.Ordinal));
    }
}
