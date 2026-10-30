using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// The ElevenLabs operations and fields <c>Tracon.Voice</c> depends on, derived
/// from the CODE and compared with the declaration in
/// <c>Fixtures/elevenlabs-operations.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Tracon.Voice</c> talks to ElevenLabs with a raw <see cref="HttpClient"/>;
/// there is no official .NET SDK, so nothing compiles against the provider's
/// contract. The declaration is the written dependency. This test proves the
/// declaration equals what the code does, in both directions: a path or field the
/// code sends and the file does not declare fails, and so does a declared one the
/// code no longer uses. <c>scripts/elevenlabs-kayma.py</c> then checks the
/// declaration against ElevenLabs' published OpenAPI document (needs the network,
/// so it does not run here).
/// </para>
/// <para>
/// Nothing on the code side is a hand-written list: operations come from driving
/// the real client against a recording handler, JSON fields from the
/// source-generated type metadata, and a source scan fails when the client
/// contains a path this test never drove.
/// </para>
/// </remarks>
public sealed class ElevenLabsOperationDriftTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly IReadOnlyDictionary<string, (Type? Request, Type? Response)> JsonRoots =
        new Dictionary<string, (Type?, Type?)>(StringComparer.Ordinal)
        {
            ["POST /v1/text-to-speech/{voice_id}"] = (typeof(ElevenLabsSynthesisRequest), null),
            ["POST /v1/text-to-speech/{voice_id}/stream"] = (typeof(ElevenLabsSynthesisRequest), null),
            ["POST /v1/text-to-speech/{voice_id}/with-timestamps"] = (typeof(ElevenLabsSynthesisRequest), typeof(ElevenLabsAudioWithTimestampsResponse)),
            ["POST /v1/text-to-dialogue"] = (typeof(ElevenLabsDialogueRequest), null),
            ["POST /v1/text-to-dialogue/with-timestamps"] = (typeof(ElevenLabsDialogueRequest), typeof(ElevenLabsDialogueWithTimestampsResponse)),
            ["POST /v1/speech-to-text"] = (null, typeof(ElevenLabsTranscriptionResponse)),
            ["GET /v2/voices"] = (null, typeof(ElevenLabsVoicesResponse)),
        };

    [Fact]
    public async Task The_operations_and_query_parameters_the_client_sends_are_the_declared_ones()
    {
        var observed = await DriveEveryOperationAsync();

        var declared = Declaration()
            .Select(operation => $"{operation.Method} {operation.Path} ? {string.Join(',', operation.Query.Order(StringComparer.Ordinal))}")
            .Order(StringComparer.Ordinal)
            .ToList();

        observed.Select(entry => $"{entry.Key} ? {string.Join(',', entry.Value.Query.Order(StringComparer.Ordinal))}")
            .Order(StringComparer.Ordinal)
            .ToList()
            .ShouldBe(declared);
    }

    [Fact]
    public void Every_path_in_the_client_source_is_one_this_test_drives()
    {
        // A new endpoint added to the client but never driven here would slip
        // through the test above. The literals in the source close that gap.
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Tracon.Voice", "Internal", "ElevenLabsSpeechClient.cs"));

        var literals = Regex.Matches(source, "\\$?\"(?<path>v\\d/[A-Za-z0-9_\\-/{}]+)", RegexOptions.None, RegexTimeout)
            .Select(match => "/" + match.Groups["path"].Value.Replace("{escapedVoiceId}", "{voice_id}", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var driven = JsonRoots.Keys.Select(key => key[(key.IndexOf(' ', StringComparison.Ordinal) + 1)..]).ToHashSet(StringComparer.Ordinal);

        literals.Order(StringComparer.Ordinal).ShouldBe(driven.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_JSON_and_form_fields_the_client_uses_are_the_declared_ones()
    {
        var observed = await DriveEveryOperationAsync();

        foreach (var operation in Declaration())
        {
            var key = $"{operation.Method} {operation.Path}";
            var (requestRoot, responseRoot) = JsonRoots[key];

            var request = requestRoot is not null
                ? FieldPaths(requestRoot)
                : observed[key].FormFields.Order(StringComparer.Ordinal).ToList();
            var response = responseRoot is not null ? FieldPaths(responseRoot) : [];

            request.ShouldBe(operation.Request.Order(StringComparer.Ordinal).ToList(), $"{key}: request fields");
            response.ShouldBe(operation.Response.Order(StringComparer.Ordinal).ToList(), $"{key}: response fields");
        }
    }

    [Fact]
    public void The_JSON_types_of_the_fields_the_client_uses_are_the_declared_ones()
    {
        // A field whose JSON type changes (a number the provider turns into a string)
        // keeps its name, so the name-only declaration could not see it. A multipart form
        // carries every value as text on the wire, so it declares no types.
        foreach (var operation in Declaration())
        {
            var key = $"{operation.Method} {operation.Path}";
            var (requestRoot, responseRoot) = JsonRoots[key];

            (requestRoot is not null ? FieldTypes(requestRoot) : [])
                .ShouldBe(operation.RequestTypes, $"{key}: request field types");
            (responseRoot is not null ? FieldTypes(responseRoot) : [])
                .ShouldBe(operation.ResponseTypes, $"{key}: response field types");
        }
    }

    [Fact]
    public void Every_declared_field_has_a_declared_type_unless_it_is_a_form_field()
    {
        foreach (var operation in Declaration())
        {
            var key = $"{operation.Method} {operation.Path}";
            var (requestRoot, responseRoot) = JsonRoots[key];

            if (requestRoot is not null)
            {
                operation.RequestTypes.Keys.Order(StringComparer.Ordinal).ShouldBe(operation.Request.Order(StringComparer.Ordinal), $"{key}: request");
            }

            if (responseRoot is not null)
            {
                operation.ResponseTypes.Keys.Order(StringComparer.Ordinal).ShouldBe(operation.Response.Order(StringComparer.Ordinal), $"{key}: response");
            }
        }
    }

    /// <summary>The dotted field paths of a JSON type, from the source-generated metadata.</summary>
    /// <remarks>
    /// <c>voice_settings</c> and <c>voice_settings.stability</c> are both paths; an array
    /// element is written <c>voices[]</c>. A property whose type has no generated
    /// metadata (a string, a number, a dictionary, a <see cref="JsonElement"/>) is a leaf.
    /// </remarks>
    private static List<string> FieldPaths(Type root)
        => [.. Fields(root).Select(static field => field.Path).Order(StringComparer.Ordinal)];

    /// <summary>The JSON type (<c>string</c>, <c>integer</c>, <c>number</c>, <c>boolean</c>, <c>array</c>, <c>object</c>) of each field path.</summary>
    private static SortedDictionary<string, string> FieldTypes(Type root)
        => new(Fields(root).ToDictionary(static field => field.Path, static field => field.JsonType, StringComparer.Ordinal), StringComparer.Ordinal);

    private static List<(string Path, string JsonType)> Fields(Type root)
    {
        var fields = new List<(string, string)>();
        Walk(root, prefix: string.Empty, depth: 0);
        return fields;

        void Walk(Type type, string prefix, int depth)
        {
            depth.ShouldBeLessThan(6, "the DTO graph should not recurse");

            var info = ElevenLabsJsonContext.Default.GetTypeInfo(type);

            if (info is null)
            {
                return;
            }

            foreach (var property in info.Properties)
            {
                var isArray = ElementType(property.PropertyType) is not null;
                var name = prefix + property.Name + (isArray ? "[]" : string.Empty);
                fields.Add((name, JsonTypeOf(property.PropertyType)));
                Walk(ElementType(property.PropertyType) ?? property.PropertyType, name + ".", depth + 1);
            }
        }

        static Type? ElementType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            return type.IsGenericType
                && type.GetGenericTypeDefinition() is var definition
                && (definition == typeof(List<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IList<>))
                    ? type.GetGenericArguments()[0]
                    : null;
        }

        static string JsonTypeOf(Type type)
        {
            var value = Nullable.GetUnderlyingType(type) ?? type;

            return value == typeof(string) ? "string"
                : value == typeof(bool) ? "boolean"
                : value == typeof(float) || value == typeof(double) || value == typeof(decimal) ? "number"
                : value.IsPrimitive ? "integer"
                : ElementType(value) is not null ? "array"
                : "object";
        }
    }

    private static async Task<Dictionary<string, Observed>> DriveEveryOperationAsync()
    {
        // Two voice pages, so the pagination query parameter is sent too.
        var voicePages = 0;
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith("/with-timestamps", StringComparison.Ordinal) =>
                StubHttpMessageHandler.Json("""{"audio_base64":"SUQz","alignment":{"characters":["a"],"character_start_times_seconds":[0],"character_end_times_seconds":[1]},"voice_segments":[]}"""),
            "/v2/voices" => StubHttpMessageHandler.Json(
                ++voicePages == 1
                    ? """{"voices":[],"has_more":true,"next_page_token":"page-2"}"""
                    : """{"voices":[],"has_more":false}"""),
            "/v1/speech-to-text" => StubHttpMessageHandler.Json("""{"text":"hello"}"""),
            _ => StubHttpMessageHandler.Binary([0x49, 0x44, 0x33]),
        });

        using var client = new ElevenLabsSpeechClient(
            new VoiceOptions { ApiKey = "FAKE-KEY", DefaultVoiceId = "voice-1" },
            new HttpClient(handler));
        var token = TestContext.Current.CancellationToken;

        _ = await client.SynthesizeAsync(new SpeechRequest { Text = "hi" }, token);
        _ = await client.SynthesizeAsync(new SpeechRequest { Text = "hi", IncludeTimestamps = true }, token);

        await foreach (var _ in client.SynthesizeStreamingAsync(new SpeechRequest { Text = "hi" }, token))
        {
        }

        _ = await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), token);
        _ = await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(includeTimestamps: true), token);
        _ = await client.ListVoicesAsync(token);

        using var audio = new MemoryStream([0x49, 0x44, 0x33]);
        _ = await client.TranscribeAsync(audio, "audio/mpeg", new SpeechTranscriptionOptions { LanguageCode = "en" }, token);

        var observed = new Dictionary<string, Observed>(StringComparer.Ordinal);

        foreach (var request in handler.Requests)
        {
            var path = Regex.Replace(
                request.Uri.AbsolutePath, "^/v1/text-to-speech/[^/]+", "/v1/text-to-speech/{voice_id}", RegexOptions.None, RegexTimeout);
            var key = $"{request.Method.Method} {path}";
            var query = request.Uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=')[0])
                .ToHashSet(StringComparer.Ordinal);
            var form = string.Equals(request.ContentType, "multipart/form-data", StringComparison.Ordinal) && request.Body is not null
                ? Regex.Matches(request.Body, "form-data; name=\"?(?<field>[A-Za-z_]+)\"?", RegexOptions.None, RegexTimeout)
                    .Select(m => m.Groups["field"].Value)
                    .ToHashSet(StringComparer.Ordinal)
                : [];

            if (observed.TryGetValue(key, out var seen))
            {
                seen.Query.UnionWith(query);
                seen.FormFields.UnionWith(form);
            }
            else
            {
                observed[key] = new Observed(query, form);
            }
        }

        observed.Keys.Order(StringComparer.Ordinal).ShouldBe(JsonRoots.Keys.Order(StringComparer.Ordinal));
        return observed;
    }

    private static List<DeclaredOperation> Declaration()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "elevenlabs-operations.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.GetProperty("operations").EnumerateArray()
            .Select(element => new DeclaredOperation(
                element.GetProperty("method").GetString()!,
                element.GetProperty("path").GetString()!,
                Strings(element.GetProperty("query")),
                Strings(element.GetProperty("request")),
                Strings(element.GetProperty("response")),
                Types(element.GetProperty("types").GetProperty("request")),
                Types(element.GetProperty("types").GetProperty("response"))))
            .ToList();

        static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToList();

        static SortedDictionary<string, string> Types(JsonElement map)
            => new(map.EnumerateObject().ToDictionary(static item => item.Name, static item => item.Value.GetString()!, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}'.");
    }

    private sealed record Observed(HashSet<string> Query, HashSet<string> FormFields);

    private sealed record DeclaredOperation(
        string Method,
        string Path,
        List<string> Query,
        List<string> Request,
        List<string> Response,
        SortedDictionary<string, string> RequestTypes,
        SortedDictionary<string, string> ResponseTypes);
}
