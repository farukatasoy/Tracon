using System.Net;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The <c>sessionId</c> filter of the run list and of the speech session list follows the rules of the ids it is compared
/// with: no more than 200 characters, and no whitespace at either edge.
/// </summary>
/// <remarks>
/// <para>
/// SQL Server ignores trailing spaces in a comparison of any collation, so a filter <c>abc </c> found the session
/// <c>abc</c> there and nothing on the other stores. The door refuses the edge, as it does for every other entry that
/// carries a session id, so the stores answer one question the same way.
/// </para>
/// </remarks>
public sealed class SessionIdFilterTests
{
    [Theory]
    [InlineData("/tracon/api/runs")]
    [InlineData("/tracon/api/voice/sessions")]
    public async Task A_filter_that_starts_or_ends_with_whitespace_is_a_400(string path)
    {
        await using var host = await TraconTestHost.StartAsync();

        foreach (var value in new[] { "abc%20", "%20abc", "abc%09", "abc%0A" })
        {
            using var response = await host.Client.GetAsync(new Uri($"{path}?sessionId={value}", UriKind.Relative), TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"{path} with sessionId={value}");
            (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!.ShouldContain("whitespace", Case.Sensitive);
        }
    }

    [Theory]
    [InlineData("/tracon/api/runs")]
    [InlineData("/tracon/api/voice/sessions")]
    public async Task A_filter_over_200_characters_is_a_400(string path)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri($"{path}?sessionId={new string('a', 201)}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!.ShouldContain("200", Case.Sensitive);
    }

    [Theory]
    [InlineData("/tracon/api/runs", "abc")]
    [InlineData("/tracon/api/runs", "my%20session")]
    [InlineData("/tracon/api/runs", "")]
    [InlineData("/tracon/api/voice/sessions", "abc")]
    [InlineData("/tracon/api/voice/sessions", "")]
    public async Task A_valid_or_empty_filter_is_answered(string path, string value)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri($"{path}?sessionId={value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_filter_of_exactly_200_characters_is_answered()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs?sessionId={new string('a', 200)}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
