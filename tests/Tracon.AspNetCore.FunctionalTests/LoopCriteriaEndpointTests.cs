using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 201 (F-302), failure mode 8: <c>GET /api/loop-criteria</c> lists the
/// kinds and the judge model without calling a registered factory, without
/// returning the judge binding's provider settings, and only to a Reader with the
/// <c>AgentsRead</c> scope.
/// </summary>
// MAAI001: the loop API is marked "evaluation purposes only" — see K-020.
#pragma warning disable MAAI001
public sealed class LoopCriteriaEndpointTests
{
    private static readonly Uri LoopCriteria = new("/tracon/api/loop-criteria", UriKind.Relative);

    [Fact]
    public async Task Lists_built_in_and_code_kinds_and_the_judge_model_without_calling_a_factory()
    {
        var calls = 0;

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddLoopEvaluator(
                "reviewed",
                _ =>
                {
                    Interlocked.Increment(ref calls);
                    return new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));
                },
                "The reviewer agent approves the draft.")
            .AddLoopEvaluator(
                "hasCitations",
                new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop())))
            .AddModelRunJudge(static options => options.Model = new ModelBinding
            {
                Provider = "echo",
                Model = "judge-1",
                ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["apiKeyName"] = JsonSerializer.SerializeToElement("Judge:Key:Name"),
                },
            }));

        using var response = await host.Client.GetAsync(LoopCriteria);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;

        json.GetProperty("kinds").EnumerateArray()
            .Select(static kind => (kind.GetProperty("kind").GetString(), kind.GetProperty("builtIn").GetBoolean()))
            .ShouldBe(
            [
                ("completionMarker", true),
                ("todoCompletion", true),
                ("aiJudge", true),
                ("backgroundTaskCompletion", true),
                ("agentReview", true),
                ("hasCitations", false),
                ("reviewed", false),
            ]);

        var reviewed = json.GetProperty("kinds").EnumerateArray()
            .Single(static kind => string.Equals(kind.GetProperty("kind").GetString(), "reviewed", StringComparison.Ordinal));
        reviewed.GetProperty("description").GetString().ShouldBe("The reviewer agent approves the draft.");

        // The built-in agent review says what it costs: a child run of the loop's run on every iteration.
        var agentReview = json.GetProperty("kinds").EnumerateArray()
            .Single(static kind => string.Equals(kind.GetProperty("kind").GetString(), "agentReview", StringComparison.Ordinal));
        agentReview.GetProperty("description").GetString().ShouldNotBeNull().ShouldContain("child run", Case.Sensitive);

        var judge = json.GetProperty("judgeModel");
        judge.GetProperty("provider").GetString().ShouldBe("echo");
        judge.GetProperty("model").GetString().ShouldBe("judge-1");

        // Names only: the binding's settings stay on the server.
        body.ShouldNotContain("providerSettings", Case.Insensitive);
        body.ShouldNotContain("Judge:Key:Name");

        calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_judge_model_is_null_when_the_host_bound_none()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(LoopCriteria);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("judgeModel").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("kinds").GetArrayLength().ShouldBe(5);
    }

    [Fact]
    public async Task A_caller_who_is_not_a_Reader_is_refused()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(LoopCriteria);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("AgentsRead", HttpStatusCode.OK)]
    [InlineData("RunsRead", HttpStatusCode.Forbidden)]
    public async Task An_api_key_needs_the_AgentsRead_scope(string scope, HttpStatusCode expected)
    {
        await using var host = await TraconTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "loop-reader", scope);

        using var request = new HttpRequestMessage(HttpMethod.Get, LoopCriteria);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(expected);
    }
}
#pragma warning restore MAAI001
