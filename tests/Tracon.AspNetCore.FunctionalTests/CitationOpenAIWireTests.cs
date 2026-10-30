using System.Text.Json;
using System.Text.Json.Nodes;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.OutputTruncationTests;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The citations of an OpenAI Responses answer reach the run record through the real SDK
/// adapter against a local Responses API, on the buffered and the streamed path alike.
/// </summary>
/// <remarks>
/// <para>
/// The plan left "how many times and in which content does the SDK give a citation in a stream" to be measured.
/// This is the recorded measurement for OpenAI (<c>Microsoft.Extensions.AI.OpenAI</c> 10.11.0): the API sends each
/// annotation as it is added and again whole when it closes the content part and the item.
/// </para>
/// <para>
/// The answer is the provider's own event shapes, parsed by the real SDK, so a citation type the adapter maps
/// differently after an upgrade fails here and not in a user's run.
/// </para>
/// </remarks>
public sealed class CitationOpenAIWireTests
{
    private const string Provider = "citation-openai-responses";

    private static readonly string[] Titles = ["Release notes", "Schedule"];

    [Fact]
    public async Task A_buffered_answer_records_each_annotation_of_its_text_as_one_citation()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ResponsesAnnotations = Annotations();

        await using var host = await StartAsync(server);

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var citations = await CitationsAsync(host);

        citations.Count.ShouldBe(3);
        citations.Select(static e => Field(e, "title")).Take(2).ShouldBe(Titles);
        Field(citations[0], "url").ShouldBe("https://example.com");
        Field(citations[2], "fileId").ShouldBe("file-42");
        citations.ShouldAllBe(static e => !e.Payload!.Contains("SECRET", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_streamed_answer_records_each_annotation_as_one_citation()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ResponsesAnnotations = Annotations();
        server.StreamResponses = true;

        await using var host = await StartAsync(server);

        await PostStreamingAsync(host);

        var citations = await CitationsAsync(host);

        citations.Count.ShouldBe(3, "one event for each annotation of the answer");
        citations.Select(static e => Field(e, "title")).Take(2).ShouldBe(Titles);
    }

    [Fact]
    public async Task The_buffered_and_the_streamed_answer_record_equal_events()
    {
        await using var buffered = await FakeOpenAiCompatibleServer.StartAsync();
        buffered.ResponsesAnnotations = Annotations();
        await using var bufferedHost = await StartAsync(buffered);
        using var response = await PostBufferedAsync(bufferedHost);
        response.EnsureSuccessStatusCode();

        await using var streamed = await FakeOpenAiCompatibleServer.StartAsync();
        streamed.ResponsesAnnotations = Annotations();
        streamed.StreamResponses = true;
        await using var streamedHost = await StartAsync(streamed);
        await PostStreamingAsync(streamedHost);

        var fromBuffered = await CitationsAsync(bufferedHost);
        var fromStream = await CitationsAsync(streamedHost);

        fromStream.Select(static e => (e.Text, e.Payload)).ShouldBe(fromBuffered.Select(static e => (e.Text, e.Payload)));
    }

    private static JsonArray Annotations()
        => JsonNode.Parse(
            """
            [
              {"type":"url_citation","start_index":0,"end_index":5,"url":"https://example.com/notes?sig=SECRET","title":"Release notes"},
              {"type":"url_citation","start_index":6,"end_index":10,"url":"https://example.org/schedule","title":"Schedule"},
              {"type":"file_citation","index":11,"file_id":"file-42","filename":"plan.pdf"}
            ]
            """)!.AsArray();

    private static async Task<List<RunEvent>> CitationsAsync(TraconTestHost host)
    {
        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);

        return [.. (await EventsAsync(host, run.Id))
            .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.Citation, StringComparison.Ordinal))];
    }

    private static string? Field(RunEvent e, string name)
    {
        using var payload = JsonDocument.Parse(e.Payload!);

        return payload.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    private static Task<TraconTestHost> StartAsync(FakeOpenAiCompatibleServer server)
        => TraconTestHost.StartAsync(builder => builder
            .UseOpenAICompatible("citation-openai", options =>
            {
                options.Endpoint = server.BaseAddress;
                options.EnableResponsesSurface = true;
            })
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Reply briefly.",
                Model = new ModelBinding { Provider = Provider, Model = "fake-local-model" },
                Origin = AgentDefinitionOrigin.Code,
            }));
}
