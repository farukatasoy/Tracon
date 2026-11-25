using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The published JSON Schema of the voice conversation frames says what the server writes and the client may send.
/// </summary>
/// <remarks>
/// <para>
/// Two things are measured, because either alone would let the schema drift. The schema and the frame types agree on names:
/// every frame type and every field of the code is in the schema, and the schema holds nothing the code does not. And the
/// frames a real server writes, over a real socket, conform to it: the kind of every field, the required fields, the constants.
/// </para>
/// <para>
/// The schema is read from the file the site publishes (<c>docs-site/public/schemas</c>), not from a copy.
/// </para>
/// </remarks>
public sealed class VoiceFrameSchemaTests
{
    private const string SchemaPath = "docs-site/public/schemas/voice-conversation-frames.schema.json";
    private const string Agent = "voice-agent";
    private const string Provider = "frames-script";

    [Fact]
    public void The_schema_names_every_frame_and_every_field_of_the_protocol_and_nothing_else()
    {
        var defs = Defs();

        var clientFrames = Constants("Client");
        var serverFrames = Constants("Server");

        FrameTypes(defs, "ClientFrame").Order(StringComparer.Ordinal).ShouldBe(clientFrames.Order(StringComparer.Ordinal));
        FrameTypes(defs, "ServerFrame").Order(StringComparer.Ordinal).ShouldBe(serverFrames.Order(StringComparer.Ordinal));

        FieldsOf(defs, clientFrames).Order(StringComparer.Ordinal)
            .ShouldBe(VoiceFramesSnapshotTests.Fields(typeof(VoiceClientMessage)).Where(static field => !string.Equals(field, "type", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        FieldsOf(defs, serverFrames).Order(StringComparer.Ordinal)
            .ShouldBe(VoiceFramesSnapshotTests.Fields(typeof(VoiceServerMessage)).Where(static field => !string.Equals(field, "type", StringComparison.Ordinal)).Order(StringComparer.Ordinal));

        Enum(defs, "Start", "inputFormat").Order(StringComparer.Ordinal)
            .ShouldBe(new[] { VoiceAudioFormats.WebmOpus, VoiceAudioFormats.Pcm16 }.Order(StringComparer.Ordinal));
        Enum(defs, "Ended", "reason").ShouldBe([VoiceConversationProtocol.EndedByAgent, VoiceConversationProtocol.EndedByDuration]);
    }

    [Fact]
    public async Task Every_frame_the_server_writes_conforms_to_the_schema()
    {
        var defs = Defs();
        var voice = new StubVoiceProvider();

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(new FakeModelProvider(Provider).WithModel(new ModelDescriptor { Name = "frames-1" })
                    .CallsTool(EndConversationTool.ToolName, new { }).EchoesLastToolResult())
                .AddAgent(new AgentDefinition
                {
                    Name = Agent,
                    Description = "Talks.",
                    Instructions = "Reply briefly.",
                    Model = new ModelBinding { Provider = Provider, Model = "frames-1" },
                    ToolNames = [EndConversationTool.ToolName],
                    Origin = AgentDefinitionOrigin.Code,
                })
                .UseVoiceConversation(options => options.ResolveConnection = static (_, _) =>
                    ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings { Greeting = "Hello." })),
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(voice);
                services.AddSingleton<ISpeechSynthesizer>(voice);
            });

        var seen = new HashSet<string>(StringComparer.Ordinal);

        // A conversation with a greeting, one turn and an ending by the agent: ready, text, audio, done, transcript,
        // runStarted, ended.
        await using (var client = await ConnectAsync(host, "frames-1"))
        {
            await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

            var frames = new List<VoiceEvent>();

            await client.WaitForAsync("done", frames);
            await client.SendAudioAsync(new byte[3200]);
            await client.SendControlAsync(new { type = "commit" });
            await client.WaitForAsync("ended", frames);

            Conform(frames, defs, seen);
        }

        // A commit before any audio answers idle; a message nobody defined answers error.
        await using (var client = await ConnectAsync(host, "frames-2"))
        {
            await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

            var frames = new List<VoiceEvent>();

            await client.WaitForAsync("done", frames);
            await client.SendControlAsync(new { type = "commit" });
            await client.WaitForAsync("idle", frames);
            await client.SendControlAsync(new { type = "no-such-frame" });
            await client.WaitForAsync("error", frames);

            Conform(frames, defs, seen);
        }

        // Every frame type of the protocol was written by the server and checked: the scenarios above are not a sample.
        seen.Order(StringComparer.Ordinal).ShouldBe(Constants("Server").Order(StringComparer.Ordinal));
    }

    private static void Conform(IEnumerable<VoiceEvent> frames, JsonElement defs, HashSet<string> seen)
    {
        foreach (var frame in frames.Where(static frame => frame.Type is not null))
        {
            var type = frame.Type!;
            var def = defs.GetProperty(char.ToUpperInvariant(type[0]) + type[1..]);
            var json = frame.Json!.Value;

            seen.Add(type);

            var properties = def.GetProperty("properties");

            foreach (var required in def.GetProperty("required").EnumerateArray())
            {
                json.TryGetProperty(required.GetString()!, out _).ShouldBeTrue($"A '{type}' frame lacks the required field '{required}'.");
            }

            foreach (var field in json.EnumerateObject())
            {
                properties.TryGetProperty(field.Name, out var declared)
                    .ShouldBeTrue($"A '{type}' frame carries '{field.Name}', which the schema does not declare.");

                if (declared.TryGetProperty("const", out var constant))
                {
                    field.Value.GetString().ShouldBe(constant.GetString(), $"'{type}'.{field.Name}");
                }
                else if (declared.TryGetProperty("enum", out var allowed))
                {
                    allowed.EnumerateArray().Select(static value => value.GetString()).ShouldContain(field.Value.GetString(), StringComparer.Ordinal, $"'{type}'.{field.Name}");
                }
                else
                {
                    var expected = declared.GetProperty("type").GetString();

                    var kind = field.Value.ValueKind;
                    var matches = expected switch
                    {
                        "string" => kind == JsonValueKind.String,
                        "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
                        "integer" => kind == JsonValueKind.Number && field.Value.TryGetInt64(out _),
                        _ => false,
                    };

                    matches.ShouldBeTrue($"A '{type}' frame writes '{field.Name}' as {kind}; the schema says {expected}.");
                }
            }
        }
    }

    private static JsonElement Defs()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot.Path, SchemaPath));

        return JsonDocument.Parse(text).RootElement.GetProperty("$defs").Clone();
    }

    private static IReadOnlyList<string> FrameTypes(JsonElement defs, string union)
        => [.. defs.GetProperty(union).GetProperty("oneOf").EnumerateArray()
            .Select(static reference => reference.GetProperty("$ref").GetString()!["#/$defs/".Length..])
            .Select(name => defs.GetProperty(name).GetProperty("properties").GetProperty("type").GetProperty("const").GetString()!)];

    private static IReadOnlyList<string> FieldsOf(JsonElement defs, IReadOnlyList<string> types)
        => [.. types
            .SelectMany(type => defs.GetProperty(char.ToUpperInvariant(type[0]) + type[1..]).GetProperty("properties").EnumerateObject())
            .Select(static property => property.Name)
            .Where(static name => !string.Equals(name, "type", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)];

    private static IReadOnlyList<string> Enum(JsonElement defs, string def, string field)
        => [.. defs.GetProperty(def).GetProperty("properties").GetProperty(field).GetProperty("enum").EnumerateArray().Select(static value => value.GetString()!)];

    /// <summary>The frame names of one direction, from the protocol's own constants.</summary>
    private static IReadOnlyList<string> Constants(string direction)
        => [.. VoiceFramesSnapshotTests.Names()
            .Where(name => name.StartsWith($"{direction.ToLowerInvariant()}-frame ", StringComparison.Ordinal))
            .Select(static name => name[(name.IndexOf(' ', StringComparison.Ordinal) + 1)..])];

    private static async Task<VoiceConversationClient> ConnectAsync(TraconTestHost host, string sessionId)
    {
        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        var socket = await socketClient.ConnectAsync(
            new Uri($"http://localhost/tracon/api/voice/sessions/{sessionId}/stream"),
            TestContext.Current.CancellationToken);

        return new VoiceConversationClient(socket);
    }
}
