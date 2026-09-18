using System.Net;
using System.Net.Http.Json;
using System.Text;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An enum-typed field refused the body with the raw System.Text.Json message:
/// the CLR type name and a byte offset, and neither the value that was refused
/// nor the values that would have worked.
/// </summary>
/// <remarks>
/// The fix sits on the request-body options, so one registration covers every
/// enum field of every contract. These tests therefore pick fields on different
/// endpoints deliberately — a single-field fix would pass the first and fail
/// the rest.
/// </remarks>
public sealed class EnumRejectionMessageTests
{
    [Fact]
    public async Task An_unknown_compaction_strategy_names_the_value_and_the_valid_ones()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            new StringContent(
                """
                {"name":"c2","model":{"provider":"echo","model":"echo-1"},
                 "compaction":{"strategy":"BoyleBirSeyYok","triggerTokens":1000}}
                """,
                Encoding.UTF8,
                "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain("'BoyleBirSeyYok' is not a valid value.");
        detail.ShouldContain("SlidingWindow");
        detail.ShouldContain("Pipeline");

        // The path is the one part of the stock message worth keeping; the CLR
        // type name and the byte offset are not.
        detail.ShouldContain("$.compaction.strategy");
        detail.ShouldNotContain("BytePositionInLine");
    }

    /// <summary>
    /// A second contract, a second endpoint, a nullable enum. The registration
    /// is per-options, not per-type, so this must pass without its own change.
    /// </summary>
    [Fact]
    public async Task An_unknown_response_format_kind_names_the_value_and_the_valid_ones()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            new StringContent(
                """
                {"name":"c3","model":{"provider":"echo","model":"echo-1",
                 "responseFormat":{"kind":"Yaml"}}}
                """,
                Encoding.UTF8,
                "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain("'Yaml' is not a valid value.");
        detail.ShouldContain("JsonSchema");
    }

    /// <summary>A valid value must still be accepted, and unchanged.</summary>
    [Fact]
    public async Task A_valid_enum_value_is_still_accepted()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            new
            {
                name = "c4",
                model = new { provider = "echo", model = "echo-1" },
                compaction = new { strategy = "SlidingWindow", triggerTokens = 1000 },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
