using System.Text.Json;
using System.Text.Json.Nodes;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.OutputTruncationTests;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-773 (phase 267): the citations of an Anthropic answer reach the run record through the real SDK adapter
/// against a local Messages API, on the buffered and the streamed path alike.
/// </summary>
/// <remarks>
/// The plan left "how many times and in which content does the SDK give a citation in a stream" to be
/// measured. This is the recorded measurement for Anthropic (SDK 13.3.0): a <c>citations_delta</c> carries one
/// citation, the buffered block carries the list, and both reach the record as the same events.
/// </remarks>
public sealed class CitationAnthropicWireTests
{
    private const string Notes = """
        {"type":"web_search_result_location","url":"https://example.com/notes?sig=SECRET","title":"Release notes",
         "encrypted_index":"opaque-1","cited_text":"Ships on Friday."}
        """;

    private const string Schedule = """
        {"type":"web_search_result_location","url":"https://example.org/schedule","title":"Schedule",
         "encrypted_index":"opaque-2","cited_text":"Friday at noon."}
        """;

    [Fact]
    public async Task A_buffered_answer_records_each_citation_of_its_text_block()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(BufferedMessage());

        await using var host = await StartAsync(anthropic);

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var citations = await CitationsAsync(host);

        citations.Select(static e => Title(e)).ShouldBe(["Release notes", "Schedule"]);
        citations.ShouldAllBe(static e => e.Payload!.Contains("SECRET", StringComparison.Ordinal) == false);
    }

    [Fact]
    public async Task A_streamed_answer_records_the_same_citations_once_each()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Stream(StreamedMessage());

        await using var host = await StartAsync(anthropic);

        await PostStreamingAsync(host);

        var citations = await CitationsAsync(host);

        citations.Select(static e => Title(e)).ShouldBe(["Release notes", "Schedule"]);
    }

    [Fact]
    public async Task The_buffered_and_the_streamed_answer_record_equal_events()
    {
        await using var buffered = await FakeAnthropicServer.StartAsync();
        buffered.Respond(BufferedMessage());
        await using var bufferedHost = await StartAsync(buffered);
        using var response = await PostBufferedAsync(bufferedHost);
        response.EnsureSuccessStatusCode();

        await using var streamed = await FakeAnthropicServer.StartAsync();
        streamed.Stream(StreamedMessage());
        await using var streamedHost = await StartAsync(streamed);
        await PostStreamingAsync(streamedHost);

        var fromBuffered = await CitationsAsync(bufferedHost);
        var fromStream = await CitationsAsync(streamedHost);

        fromStream.Select(static e => (e.Text, e.Payload)).ShouldBe(fromBuffered.Select(static e => (e.Text, e.Payload)));
    }

    private static async Task<List<RunEvent>> CitationsAsync(TraconTestHost host)
    {
        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);

        return [.. (await EventsAsync(host, run.Id))
            .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.Citation, StringComparison.Ordinal))];
    }

    private static string? Title(RunEvent e)
    {
        using var payload = JsonDocument.Parse(e.Payload!);

        return payload.RootElement.GetProperty("title").GetString();
    }

    private static string BufferedMessage()
        => new JsonObject
        {
            ["id"] = "msg_cited",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = "claude-test",
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = "The release ships on Friday at noon.",
                ["citations"] = new JsonArray(JsonNode.Parse(Notes), JsonNode.Parse(Schedule)),
            }),
            ["stop_reason"] = "end_turn",
            ["stop_sequence"] = null,
            ["usage"] = new JsonObject { ["input_tokens"] = 40, ["output_tokens"] = 12 },
        }.ToJsonString();

    private static string StreamedMessage()
        => string.Concat(
            Frame("message_start", new JsonObject
            {
                ["type"] = "message_start",
                ["message"] = new JsonObject
                {
                    ["id"] = "msg_cited",
                    ["type"] = "message",
                    ["role"] = "assistant",
                    ["model"] = "claude-test",
                    ["content"] = new JsonArray(),
                    ["stop_reason"] = null,
                    ["stop_sequence"] = null,
                    ["usage"] = new JsonObject { ["input_tokens"] = 40, ["output_tokens"] = 1 },
                },
            }),
            Frame("content_block_start", new JsonObject
            {
                ["type"] = "content_block_start",
                ["index"] = 0,
                ["content_block"] = new JsonObject { ["type"] = "text", ["text"] = string.Empty },
            }),
            Frame("content_block_delta", Delta(new JsonObject { ["type"] = "text_delta", ["text"] = "The release ships on Friday" })),
            Frame("content_block_delta", Delta(new JsonObject { ["type"] = "citations_delta", ["citation"] = JsonNode.Parse(Notes) })),
            Frame("content_block_delta", Delta(new JsonObject { ["type"] = "text_delta", ["text"] = " at noon." })),
            Frame("content_block_delta", Delta(new JsonObject { ["type"] = "citations_delta", ["citation"] = JsonNode.Parse(Schedule) })),
            Frame("content_block_stop", new JsonObject { ["type"] = "content_block_stop", ["index"] = 0 }),
            Frame("message_delta", new JsonObject
            {
                ["type"] = "message_delta",
                ["delta"] = new JsonObject { ["stop_reason"] = "end_turn", ["stop_sequence"] = null },
                ["usage"] = new JsonObject { ["output_tokens"] = 12 },
            }),
            Frame("message_stop", new JsonObject { ["type"] = "message_stop" }));

    private static JsonObject Delta(JsonObject delta)
        => new() { ["type"] = "content_block_delta", ["index"] = 0, ["delta"] = delta };

    private static string Frame(string name, JsonObject data) => $"event: {name}\ndata: {data.ToJsonString()}\n\n";

    private static Task<TraconTestHost> StartAsync(FakeAnthropicServer anthropic)
        => TraconTestHost.StartAsync(configureTracon: builder => builder
            .UseAnthropic("test-anthropic-citations", options =>
            {
                options.Endpoint = anthropic.BaseAddress;
                options.MaxRetries = 0;
                options.Models.Add(new ModelDescriptor { Name = "claude-test" });
            })
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Reply briefly.",
                Model = new ModelBinding
                {
                    Provider = AnthropicProviderNames.Anthropic,
                    Model = "claude-test",
                    MaxOutputTokens = 1_000,
                },
                Origin = AgentDefinitionOrigin.Code,
            }));
}
