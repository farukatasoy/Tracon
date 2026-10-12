using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The shared setup of the function-node settings tests (F-295): a host with a
/// text-to-speech-shaped function, the HTTP calls that save and run a chain, and the
/// two settings types the tests use.
/// </summary>
/// <remarks>
/// The function renders the settings it received into the message it passes on, so a
/// run's output shows what the function was actually given - not what the test hoped.
/// </remarks>
internal static class WorkflowSettingsFixtures
{
    public const string Writer = "writer";

    public const string Tts = "tts";

    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>A host with the <c>tts</c> function; <paramref name="configure"/> adds anything else.</summary>
    public static Task<TraconTestHost> StartAsync(Action<ITraconBuilder>? configure = null, bool tenancy = false)
        => TraconTestHost.StartAsync(builder =>
        {
            builder
                .AddAgent(TestData.Definition(Writer))
                .UseWorkflows()
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, TtsSettings>(
                    Tts,
                    static _ => static (_, settings, _, _) => Say(settings.Describe()),
                    static options => options.Description = "Text to speech.");

            if (tenancy)
            {
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
            }

            configure?.Invoke(builder);
        });

    public static ValueTask<List<ChatMessage>> Say(string text)
        => new([new ChatMessage(ChatRole.User, text)]);

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>
    /// A node as a request body carries it. <paramref name="settings"/> is raw JSON, so a test can
    /// send what no typed model would let it write (an array, a misspelled member).
    /// </summary>
    public static object Node(string name, string kind, string? settings = null)
        => settings is null
            ? new { name, kind }
            : new { name, kind, settings = Json(settings) };

    public static object[] Chain(string? settings, string function = Tts)
        => [Node(Writer, "Agent"), Node(function, "Function", settings)];

    public static Task<HttpResponseMessage> SaveAsync(
        TraconTestHost host,
        string name,
        object[] nodes,
        string? tenant = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/tracon/api/workflows/{name}")
        {
            Content = JsonContent.Create(new { kind = "Sequential", nodes }),
        };

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        return host.Client.SendAsync(request);
    }

    /// <summary>Saves a chain and fails the test unless the save is accepted.</summary>
    public static async Task SaveOkAsync(TraconTestHost host, string name, object[] nodes, string? tenant = null)
    {
        using var response = await SaveAsync(host, name, nodes, tenant);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public static async Task<WorkflowRun> RunAsync(TraconTestHost host, string name, string message = "hello", string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/workflows/{name}/run")
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = message }),
        };

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await ReadRunAsync(host, response);
    }

    public static async Task<WorkflowRun> ReadRunAsync(TraconTestHost host, HttpResponseMessage response)
    {
        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var runId = JsonDocument.Parse(frames.First(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal)).Data)
            .RootElement.GetProperty("runId").GetGuid();

        string? output = null;

        foreach (var frame in frames.Where(static frame => string.Equals(frame.Event, "event", StringComparison.Ordinal)))
        {
            using var document = JsonDocument.Parse(frame.Data);

            if (string.Equals(document.RootElement.GetProperty("type").GetString(), "WorkflowOutput", StringComparison.Ordinal))
            {
                output = document.RootElement.GetProperty("text").GetString();
            }
        }

        var record = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId))!;

        return new WorkflowRun(record, output, frames);
    }

    public static async Task<JsonElement> ProblemOf(HttpResponseMessage response)
        => await TraconTestHost.ReadJsonAsync(response);

    internal sealed record WorkflowRun(RunRecord Record, string? Output, IReadOnlyList<SseFrame> Frames);

    internal enum SpeechSpeed
    {
        Slow,
        Normal,
        Fast,
    }

    /// <summary>The settings of the <c>tts</c> function: one required member, the rest with defaults.</summary>
    internal sealed record TtsSettings
    {
        public required string Voice { get; init; }

        public double LoudnessLufs { get; init; } = -16;

        public SpeechSpeed Speed { get; init; } = SpeechSpeed.Normal;

        public string Describe()
            => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"voice={Voice};lufs={LoudnessLufs};speed={Speed}");
    }

    /// <summary>A second settings type, with nothing required: an empty object is a valid one.</summary>
    internal sealed record MixSettings
    {
        public string Preset { get; init; } = "flat";
    }
}
