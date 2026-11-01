using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A session id the client chooses does not start or end with whitespace. SQL Server ignores trailing spaces in a key,
/// even under a binary collation, so <c>abc</c> and <c>abc </c> are one session there and two on the other stores. The
/// door refuses the edge so that every store answers the same.
/// </summary>
public sealed class SessionIdWhitespaceTests
{
    private static readonly Uri RunUri = new("/tracon/api/agents/kod-agent/run", UriKind.Relative);
    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);

    [Theory]
    [InlineData(" abc")]
    [InlineData("abc ")]
    [InlineData("abc\t")]
    [InlineData("abc\n")]
    [InlineData(" ")]
    public async Task A_run_with_a_session_id_that_starts_or_ends_with_whitespace_is_a_400(string sessionId)
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            RunUri,
            new AgentRunRequest { Message = "hello", SessionId = sessionId },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!.ShouldContain("whitespace", Case.Sensitive);
    }

    [Fact]
    public async Task Whitespace_inside_the_session_id_is_not_at_its_edge()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            RunUri,
            new AgentRunRequest { Message = "hello", SessionId = "my session" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A branch opens a session under an id the client chooses, so it is a fourth way to create a session and takes the same
    /// rule. Refused before anything is read, so the answer does not depend on the store (a branch is 501 without a database).
    /// </summary>
    [Theory]
    [InlineData("abc ")]
    [InlineData(" abc")]
    [InlineData("abc\t")]
    public async Task A_branch_whose_new_session_id_starts_or_ends_with_whitespace_is_a_400(string newSessionId)
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/sessions/source/branch", UriKind.Relative),
            new SessionBranchRequest { NewSessionId = newSessionId },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!.ShouldContain("whitespace", Case.Sensitive);
    }

    [Fact]
    public async Task A_branch_whose_new_session_id_is_over_the_column_is_a_400()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/sessions/source/branch", UriKind.Relative),
            new SessionBranchRequest { NewSessionId = new string('b', 201) },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!.ShouldContain("200", Case.Sensitive);
    }

    [Theory]
    [InlineData("conversation", "abc ")]
    [InlineData("conversation", " abc")]
    [InlineData("previous_response_id", "abc ")]
    [InlineData("previous_response_id", " abc")]
    public async Task A_conversation_or_response_id_that_starts_or_ends_with_whitespace_is_a_400_on_the_openai_compatible_endpoint(string field, string value)
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model"] = "kod-agent",
            ["input"] = "hello",
            [field] = value,
        };

        using var response = await host.Client.PostAsJsonAsync(Responses, body, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("whitespace", Case.Sensitive);
    }

    [Fact]
    public async Task Whitespace_inside_the_conversation_id_is_served_on_the_openai_compatible_endpoint()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model"] = "kod-agent",
            ["input"] = "hello",
            ["conversation"] = "my conversation",
        };

        using var response = await host.Client.PostAsJsonAsync(Responses, body, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
