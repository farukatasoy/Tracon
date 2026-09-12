using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests.Runs;

/// <summary>
/// The named, reshaped score over HTTP (phase 152).
/// </summary>
/// <remarks>
/// These cases cross the HTTP boundary on purpose: the name default, the
/// categorical/numeric split and the decimal round trip all live in the
/// endpoint's binding and validation, which a store unit test never reaches.
/// <c>RunFeedbackEndpointTests</c> keeps the pre-152 cases; this class holds
/// what naming and reshaping added.
/// </remarks>
public sealed class RunScoreNameTests
{
    [Fact]
    public async Task Two_names_from_the_SAME_author_are_two_rows()
    {
        // Requires an authenticated actor: with no identity every call opens a
        // new row anyway and the name would prove nothing.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services));
        var runId = await SeedRunAsync(host);

        await PostAsync(host, runId, new { name = "helpfulness", kind = "Stars", value = 4 });
        await PostAsync(host, runId, new { name = "accuracy", kind = "Binary", value = 1 });

        var scores = await ListAsync(host, runId);

        scores.GetArrayLength().ShouldBe(2);
        Named(scores, "helpfulness").GetProperty("value").GetDouble().ShouldBe(4);
        Named(scores, "accuracy").GetProperty("value").GetDouble().ShouldBe(1);
    }

    [Fact]
    public async Task Writing_the_same_name_twice_updates_only_that_row()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services));
        var runId = await SeedRunAsync(host);

        await PostAsync(host, runId, new { name = "helpfulness", kind = "Stars", value = 4 });
        await PostAsync(host, runId, new { name = "accuracy", kind = "Binary", value = 1 });
        await PostAsync(host, runId, new { name = "helpfulness", kind = "Stars", value = 5 });

        var scores = await ListAsync(host, runId);

        scores.GetArrayLength().ShouldBe(2);
        Named(scores, "helpfulness").GetProperty("value").GetDouble().ShouldBe(5);
        Named(scores, "accuracy").GetProperty("value").GetDouble().ShouldBe(1);
    }

    [Fact]
    public async Task A_body_with_no_name_keeps_the_pre_152_behavior()
    {
        // The compatibility promise: an existing preview consumer's call is
        // unchanged, and its row is named 'overall'.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services));
        var runId = await SeedRunAsync(host);

        await PostAsync(host, runId, new { kind = "Binary", value = 1 });
        await PostAsync(host, runId, new { kind = "Binary", value = 0 });

        var scores = await ListAsync(host, runId);

        scores.GetArrayLength().ShouldBe(1);
        scores[0].GetProperty("name").GetString().ShouldBe("overall");
        scores[0].GetProperty("value").GetDouble().ShouldBe(0);
    }

    [Fact]
    public async Task A_decimal_value_is_not_rounded()
    {
        await using var host = await TraconTestHost.StartAsync();
        var runId = await SeedRunAsync(host);

        await PostAsync(host, runId, new { name = "similarity", kind = "Numeric", value = 0.87 });

        var scores = await ListAsync(host, runId);

        scores[0].GetProperty("value").GetDouble().ShouldBe(0.87);
    }

    [Fact]
    public async Task A_categorical_score_is_stored_and_returned_as_text()
    {
        await using var host = await TraconTestHost.StartAsync();
        var runId = await SeedRunAsync(host);

        await PostAsync(host, runId, new { name = "severity", kind = "Categorical", textValue = "minor" });

        var scores = await ListAsync(host, runId);

        scores[0].GetProperty("kind").GetString().ShouldBe("Categorical");
        scores[0].GetProperty("textValue").GetString().ShouldBe("minor");
        scores[0].GetProperty("value").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("naïve")]
    [InlineData("judge:quality")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task An_illegal_name_returns_400(string name)
    {
        await using var host = await TraconTestHost.StartAsync();
        var runId = await SeedRunAsync(host);

        using var response = await host.Client.PostAsJsonAsync(
            FeedbackUri(runId),
            new { name, kind = "Binary", value = 1 });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_categorical_score_with_no_text_value_returns_400()
        => (await PostRawAsync(new { name = "severity", kind = "Categorical" }))
            .ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task A_categorical_score_carrying_a_numeric_value_returns_400()
        => (await PostRawAsync(new { name = "severity", kind = "Categorical", textValue = "minor", value = 1 }))
            .ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task A_non_categorical_score_carrying_a_text_value_returns_400()
        => (await PostRawAsync(new { name = "severity", kind = "Binary", value = 1, textValue = "minor" }))
            .ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task A_non_categorical_score_with_no_value_returns_400()
        => (await PostRawAsync(new { name = "helpfulness", kind = "Binary" }))
            .ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task A_text_value_longer_than_the_limit_returns_400()
        => (await PostRawAsync(new
        {
            name = "severity",
            kind = "Categorical",
            textValue = new string('a', RunScoreRules.MaxTextValueLength + 1),
        })).ShouldBe(HttpStatusCode.BadRequest);

    private static async Task<HttpStatusCode> PostRawAsync(object body)
    {
        await using var host = await TraconTestHost.StartAsync();
        var runId = await SeedRunAsync(host);

        using var response = await host.Client.PostAsJsonAsync(FeedbackUri(runId), body);

        return response.StatusCode;
    }

    private static async Task PostAsync(TraconTestHost host, Guid runId, object body)
    {
        using var response = await host.Client.PostAsJsonAsync(FeedbackUri(runId), body);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<System.Text.Json.JsonElement> ListAsync(TraconTestHost host, Guid runId)
    {
        using var response = await host.Client.GetAsync(FeedbackUri(runId));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await TraconTestHost.ReadJsonAsync(response);
    }

    private static System.Text.Json.JsonElement Named(System.Text.Json.JsonElement scores, string name)
        => scores.EnumerateArray()
            .Single(score => string.Equals(score.GetProperty("name").GetString(), name, StringComparison.Ordinal));

    private static async Task<Guid> SeedRunAsync(TraconTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        return runId;
    }

    private static Uri FeedbackUri(Guid runId)
        => new($"/tracon/api/runs/{runId}/feedback", UriKind.Relative);
}
