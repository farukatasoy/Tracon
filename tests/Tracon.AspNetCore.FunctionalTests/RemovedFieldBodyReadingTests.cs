using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The removed-field refusal has to see the same body the binder sees. The binder converts the body from the
/// <c>charset</c> the request declares and reads it with the application's own JSON settings; a check that reads
/// UTF-8 with the framework defaults does not see a removed field in a body the binder accepts, and the field is
/// dropped without a word, which is exactly what the refusal exists to prevent.
/// </summary>
/// <remarks>
/// One surface per kind of path the filter walks: a top-level name (MCP server save), a nested name (agent
/// create) and a name inside every element of a list body (eval cases). Every case first sends the same body
/// WITHOUT the removed field and expects it to be saved, so a refusal cannot come from the binder not
/// understanding the body.
/// </remarks>
public sealed class RemovedFieldBodyReadingTests
{
    private const string RemovedFieldTitle = "Removed field";
    private const string InvalidBodyTitle = "Invalid request body";

    private static readonly string[] Surfaces = ["mcp-server", "agent", "eval-cases"];
    private static readonly string[] Charsets = ["utf-16", "utf-16BE", "utf-32", "iso-8859-1"];
    private static readonly string[] Syntaxes = ["trailing-comma", "comments"];

    public static TheoryData<string, string> SurfaceAndCharset => Cross(Surfaces, Charsets);

    public static TheoryData<string, string> SurfaceAndSyntax => Cross(Surfaces, Syntaxes);

    /// <param name="surface">The endpoint under test.</param>
    /// <param name="charset">The charset the body is sent in.</param>
    [Theory]
    [MemberData(nameof(SurfaceAndCharset))]
    public async Task A_removed_field_is_refused_in_a_body_sent_in_another_charset(string surface, string charset)
    {
        var target = Describe(surface);
        await using var host = await StartAsync(target, configureServices: null);

        using var accepted = await SendAsync(host, target, target.Clean, charset);

        accepted.IsSuccessStatusCode.ShouldBeTrue(await accepted.Content.ReadAsStringAsync());

        using var refused = await SendAsync(host, target, target.Legacy, charset);

        await ShouldBeRemovedFieldAsync(refused);
        (await target.LegacyWasStoredAsync(host)).ShouldBeFalse();
    }

    /// <param name="surface">The endpoint under test.</param>
    /// <param name="syntax">The JSON syntax the application's settings allow and the framework default does not.</param>
    [Theory]
    [MemberData(nameof(SurfaceAndSyntax))]
    public async Task A_removed_field_is_refused_in_a_body_the_configured_json_options_accept(string surface, string syntax)
    {
        var target = Describe(surface);
        await using var host = await StartAsync(target, AllowLenientSyntax);

        using var accepted = await SendAsync(host, target, target.Clean, charset: null, syntax);

        accepted.IsSuccessStatusCode.ShouldBeTrue(await accepted.Content.ReadAsStringAsync());

        using var refused = await SendAsync(host, target, target.Legacy, charset: null, syntax);

        await ShouldBeRemovedFieldAsync(refused);
        (await target.LegacyWasStoredAsync(host)).ShouldBeFalse();
    }

    /// <summary>
    /// The refusal must not become more lenient than the binder in the other direction either: a body the
    /// application's settings do not accept is the binder's to refuse, whatever it carries.
    /// </summary>
    /// <param name="surface">The endpoint under test.</param>
    /// <param name="syntax">The JSON syntax the framework default does not accept.</param>
    [Theory]
    [MemberData(nameof(SurfaceAndSyntax))]
    public async Task A_body_the_default_options_do_not_accept_gets_the_binders_answer(string surface, string syntax)
    {
        var target = Describe(surface);
        await using var host = await StartAsync(target, configureServices: null);

        using var response = await SendAsync(host, target, target.Legacy, charset: null, syntax);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("title").GetString().ShouldBe(InvalidBodyTitle);
        (await target.LegacyWasStoredAsync(host)).ShouldBeFalse();
    }

    /// <summary>
    /// A charset no encoding of the runtime matches cannot be read by anyone. The framework's own reader throws
    /// for it, which no handler caught: the caller got an empty <c>500</c> for a header it controls. It is a
    /// <c>415</c> that says what to send, on a body with or without a removed field.
    /// </summary>
    /// <param name="surface">The endpoint under test.</param>
    [Theory]
    [InlineData("mcp-server")]
    [InlineData("agent")]
    [InlineData("eval-cases")]
    public async Task A_body_in_a_charset_the_runtime_does_not_know_is_refused_with_415(string surface)
    {
        var target = Describe(surface);
        await using var host = await StartAsync(target, configureServices: null);

        foreach (var json in new[] { target.Clean, target.Legacy })
        {
            using var response = await SendAsync(host, target, json, "not-a-charset");

            response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType, json);

            var problem = await TraconTestHost.ReadJsonAsync(response);

            problem.GetProperty("title").GetString().ShouldBe("Unsupported media type");
            problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("UTF-8", Case.Sensitive);
        }

        (await target.LegacyWasStoredAsync(host)).ShouldBeFalse();
    }

    private static void AllowLenientSyntax(IServiceCollection services)
        => services.ConfigureHttpJsonOptions(static options =>
        {
            options.SerializerOptions.AllowTrailingCommas = true;
            options.SerializerOptions.ReadCommentHandling = JsonCommentHandling.Skip;
        });

    private static async Task ShouldBeRemovedFieldAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("title").GetString().ShouldBe(RemovedFieldTitle);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        TraconTestHost host,
        Surface target,
        string json,
        string? charset,
        string? syntax = null)
    {
        var text = Shape(json, syntax, charset);
        var encoding = ResolveEncoding(charset);

        using var request = new HttpRequestMessage(target.Method, new Uri(target.Path, UriKind.Relative))
        {
            Content = new ByteArrayContent(encoding.GetBytes(text)),
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = charset };

        return await host.Client.SendAsync(request);
    }

    private static string Shape(string json, string? syntax, string? charset)
    {
        // A character outside ASCII in an ignored member: the bytes of a Latin-1 body are not valid UTF-8.
        if (string.Equals(charset, "iso-8859-1", StringComparison.Ordinal))
        {
            var start = json.IndexOf('{', StringComparison.Ordinal);
            json = json.Insert(start + 1, "\"note\":\"caf" + char.ConvertFromUtf32(0xE9) + "\",");
        }

        return syntax switch
        {
            "trailing-comma" => json[..^1] + "," + json[^1],
            "comments" => "/* sent by an older client */ " + json + " // end of body",
            _ => json,
        };
    }

    private static Encoding ResolveEncoding(string? charset)
        => charset switch
        {
            "utf-16" => new UnicodeEncoding(bigEndian: false, byteOrderMark: false),
            "utf-16BE" => new UnicodeEncoding(bigEndian: true, byteOrderMark: false),
            "utf-32" => new UTF32Encoding(bigEndian: false, byteOrderMark: false),
            "iso-8859-1" => Encoding.Latin1,
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

    private static TheoryData<string, string> Cross(string[] first, string[] second)
    {
        var data = new TheoryData<string, string>();

        foreach (var left in first)
        {
            foreach (var right in second)
            {
                data.Add(left, right);
            }
        }

        return data;
    }

    private static async Task<TraconTestHost> StartAsync(Surface target, Action<IServiceCollection>? configureServices)
    {
        var host = await TraconTestHost.StartAsync(target.ConfigureTracon, configureServices: configureServices);

        if (target.PrepareAsync is { } prepare)
        {
            await prepare(host);
        }

        return host;
    }

    private static Surface Describe(string surface)
        => surface switch
        {
            // A top-level name.
            "mcp-server" => new Surface
            {
                Method = HttpMethod.Put,
                Path = "/tracon/api/mcp-servers/m1",
                Clean = """{"endpoint":"https://mcp.example.com/mcp"}""",
                Legacy = """{"endpoint":"https://legacy.example.com/mcp","authorizationConfigurationKey":"Tracon:McpSecrets:Legacy"}""",
                LegacyWasStoredAsync = static async host =>
                {
                    var stored = await host.Services.GetRequiredService<IMcpServerStore>().GetAsync("default", "m1");

                    return stored is not null && string.Equals(stored.Endpoint.Host, "legacy.example.com", StringComparison.Ordinal);
                },
            },

            // A nested name.
            "agent" => new Surface
            {
                Method = HttpMethod.Post,
                Path = "/tracon/api/agents",
                Clean = """{"name":"clean-agent","instructions":"x","model":{"provider":"legacy-model","model":"legacy-1"}}""",
                Legacy = """{"name":"legacy-agent","instructions":"x","model":{"provider":"legacy-model","model":"legacy-1","allowConcurrentToolCalls":true}}""",
                ConfigureTracon = static builder => builder
                    .AddModelProvider(new FakeModelProvider("legacy-model").WithModel(new ModelDescriptor { Name = "legacy-1" })),
                LegacyWasStoredAsync = static async host =>
                {
                    using var response = await host.Client.GetAsync(new Uri("/tracon/api/agents/legacy-agent", UriKind.Relative));

                    return response.StatusCode != HttpStatusCode.NotFound;
                },
            },

            // A name inside every element of a list body.
            "eval-cases" => new Surface
            {
                Method = HttpMethod.Put,
                Path = "/tracon/api/evals/legacy-suite/cases",
                Clean = """[{"query":"clean"}]""",
                Legacy = """[{"query":"legacy","expectedTools":["get_order_status"]}]""",
                PrepareAsync = static async host =>
                {
                    using var saved = await host.Client.PutAsync(
                        new Uri("/tracon/api/evals/legacy-suite", UriKind.Relative),
                        new StringContent(
                            """{"agentName":"legacy-agent","checks":[{"kind":"toolCallArgsMatch"}]}""",
                            Encoding.UTF8,
                            "application/json"));

                    saved.EnsureSuccessStatusCode();
                },
                LegacyWasStoredAsync = static async host =>
                {
                    var cases = await TraconTestHost.ReadJsonAsync(
                        await host.Client.GetAsync(new Uri("/tracon/api/evals/legacy-suite/cases", UriKind.Relative)));

                    return cases.EnumerateArray().Any(static item =>
                        string.Equals(item.GetProperty("query").GetString(), "legacy", StringComparison.Ordinal));
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, "Unknown surface."),
        };

    private sealed class Surface
    {
        public required HttpMethod Method { get; init; }

        public required string Path { get; init; }

        public required string Clean { get; init; }

        public required string Legacy { get; init; }

        public Action<ITraconBuilder>? ConfigureTracon { get; init; }

        public Func<TraconTestHost, Task>? PrepareAsync { get; init; }

        public required Func<TraconTestHost, Task<bool>> LegacyWasStoredAsync { get; init; }
    }
}
