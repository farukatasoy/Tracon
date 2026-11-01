using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A session identifier the client chooses is at most 200 characters, on every surface that stores a session.
/// </summary>
/// <remarks>
/// The <c>sessions</c> table of SQL Server holds <c>nvarchar(200)</c> and refuses a longer value with a truncation
/// error, so the same request answered <c>500</c> there and <c>200</c> on the other stores (measured on SQL Server:
/// <c>SqlException</c>, "String or binary data would be truncated"). The AG-UI endpoint already refused it.
/// </remarks>
public sealed class SessionIdLengthTests
{
    private static readonly Uri Run = new("/tracon/api/agents/kod-agent/run", UriKind.Relative);
    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);

    [Fact]
    public async Task A_run_with_a_session_id_over_the_limit_is_refused()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Run,
            new AgentRunRequest { Message = "hello", SessionId = new string('s', 201) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_run_with_a_session_id_at_the_limit_is_served()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Run,
            new AgentRunRequest { Message = "hello", SessionId = new string('s', 200) });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("conversation")]
    [InlineData("previous_response_id")]
    public async Task A_responses_call_with_an_identifier_over_the_limit_is_refused(string field)
    {
        await using var host = await StartAsync();

        var body = new Dictionary<string, object>(StringComparer.Ordinal) { ["model"] = "kod-agent", ["input"] = "hello", [field] = new string('c', 201) };

        using var response = await host.Client.PostAsJsonAsync(Responses, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_responses_call_with_a_conversation_at_the_limit_is_served()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new Dictionary<string, object>(StringComparer.Ordinal) { ["model"] = "kod-agent", ["input"] = "hello", ["conversation"] = new string('c', 200) });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));
}
