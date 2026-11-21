using System.Text.Json.Nodes;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.MixedCallRig;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What the model reads of a client-side tool's result string: as the caller wrote it on the request that carries it, and
/// as JSON text on the requests after it, which replay the history the session keeps.
/// </summary>
/// <remarks>
/// The guide for client-side tools says so in one sentence. The session keeps a result as JSON, in memory as well as in a
/// database, and the provider's adapter writes a string that comes back from that history as a JSON string, quotes
/// included. A caller that wants the model to read the same characters every time sends text it can read either way.
/// </remarks>
public sealed class ClientToolResultTextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_result_string_reaches_the_model_as_written_and_then_as_json_text_when_the_history_is_replayed(bool inDatabase)
    {
        using var database = new TempSqliteDatabase("result-text");
        await using var server = await StrictToolCallServer.StartAsync();
        server.ThenCalls(ClientCallOnly).ThenAnswers("Opened.").ThenAnswers("Anything else?");

        await using var rig = await MixedCallRig.StartAsync(
            new MixedCallRigOptions { SqliteConnectionString = inDatabase ? database.ConnectionString : null },
            server);

        await rig.RunAsync(new AgentRunRequest { SessionId = SessionId, Message = "open the page" }, streaming: false);
        await rig.RunAsync(
            new AgentRunRequest { SessionId = SessionId, ToolResults = [new ClientToolResult { CallId = ClientCallId, Result = "SEL-1" }] },
            streaming: false);
        await rig.RunAsync(new AgentRunRequest { SessionId = SessionId, Message = "thanks" }, streaming: false);

        ToolMessageOf(server.Bodies[1]).ShouldBe("SEL-1", "the request that carries the result");
        ToolMessageOf(server.Bodies[2]).ShouldBe("\"SEL-1\"", "the next request, which replays the history");
    }

    private static string ToolMessageOf(JsonObject body)
        => body["messages"]!.AsArray()
            .Select(static node => node!.AsObject())
            .Single(static message => string.Equals(message["role"]?.GetValue<string>(), "tool", StringComparison.Ordinal))["content"]!
            .GetValue<string>();
}
