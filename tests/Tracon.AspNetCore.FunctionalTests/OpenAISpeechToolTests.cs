using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// OpenAI speech as the unnamed default engine of the voice tools.
/// </summary>
/// <remarks>
/// <para>
/// F-579, open question 1 (answer A). <c>speak</c> and <c>transcribe</c> hand the voice options to the default
/// engine: an ElevenLabs-style <c>OutputFormat</c> (<c>mp3_44100_128</c> by default) and the
/// <c>SynthesisModelId</c> / <c>TranscriptionModelId</c> the host configured for ElevenLabs. An
/// engine bound to one OpenAI model must neither send the format name to the service (which
/// refuses it) nor run on its model while the request names another.
/// </para>
/// <para>
/// The tools are taken from the registrations <c>UseVoice()</c> wrote and invoked with an argument
/// set that carries no service provider, inside a run scope, as Microsoft Agent Framework calls them.
/// </para>
/// </remarks>
#pragma warning disable MEAI001
public sealed class OpenAISpeechToolTests
{
    [Fact]
    public async Task The_speak_tool_serves_the_default_openai_synthesizer_and_the_format_name_becomes_a_container()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);

        var result = await InvokeAsync(host, "speak", Args("text", "hello there"));

        result.ShouldBeOfType<string>().ShouldStartWith("Audio produced.");

        var body = server.SpeechBodies.ShouldHaveSingleItem();
        body["model"]!.GetValue<string>().ShouldBe("tts-test");
        body["response_format"]!.GetValue<string>().ShouldBe("mp3");
    }

    [Fact]
    public async Task The_speak_tool_with_an_elevenlabs_model_id_fails_naming_both_models_and_calls_nothing()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server, voice: static options => options.SynthesisModelId = "eleven_multilingual_v2");

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await InvokeAsync(host, "speak", Args("text", "hello there")));

        exception.Message.ShouldContain("'tts-test'");
        exception.Message.ShouldContain("'eleven_multilingual_v2'");
        server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task The_speak_tool_with_the_openai_model_id_runs()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server, voice: static options => options.SynthesisModelId = "tts-test");

        (await InvokeAsync(host, "speak", Args("text", "hello there"))).ShouldBeOfType<string>().ShouldStartWith("Audio produced.");
        server.SpeechBodies.ShouldHaveSingleItem()["model"]!.GetValue<string>().ShouldBe("tts-test");
    }

    [Fact]
    public async Task The_transcribe_tool_serves_the_default_openai_transcriber()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);

        var descriptor = await host.Services.GetRequiredService<IAttachmentStore>().SaveAsync(
            new AttachmentContent
            {
                TenantId = "default",
                FileName = "question.mp3",
                MediaType = "audio/mpeg",
                Data = ScriptedOpenAIAudioServer.Mp3,
            },
            TestContext.Current.CancellationToken);

        var result = await InvokeAsync(host, "transcribe", Args("attachmentId", descriptor.Id.ToString()));

        result.ShouldBe("where is my order");
        server.Transcriptions.ShouldHaveSingleItem().Model.ShouldBe("stt-test");
    }

    [Fact]
    public async Task The_transcribe_tool_with_an_elevenlabs_model_id_fails_naming_both_models()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server, voice: static options => options.TranscriptionModelId = "scribe_v2");

        var descriptor = await host.Services.GetRequiredService<IAttachmentStore>().SaveAsync(
            new AttachmentContent
            {
                TenantId = "default",
                FileName = "question.mp3",
                MediaType = "audio/mpeg",
                Data = ScriptedOpenAIAudioServer.Mp3,
            },
            TestContext.Current.CancellationToken);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await InvokeAsync(host, "transcribe", Args("attachmentId", descriptor.Id.ToString())));

        exception.Message.ShouldContain("'stt-test'");
        exception.Message.ShouldContain("'scribe_v2'");
        server.Requests.ShouldBe(0);
    }

    /// <summary>
    /// OpenAI speech first, then the voice tools: <c>UseAsDefault</c> registers with <c>TryAdd</c>, so the
    /// order decides which engine is the default.
    /// </summary>
    private static Task<TraconTestHost> StartAsync(ScriptedOpenAIAudioServer server, Action<VoiceOptions>? voice = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseOpenAI(options =>
                {
                    options.ApiKey = "scripted-server-key";
                    options.Endpoint = server.BaseAddress;
                })
                .UseOpenAISpeech(options =>
                {
                    options.SynthesisModel = "tts-test";
                    options.TranscriptionModel = "stt-test";
                    options.UseAsDefault = true;
                })
                .UseVoice(options => voice?.Invoke(options)));

    private static Dictionary<string, object?> Args(string name, object? value)
        => new(StringComparer.Ordinal) { [name] = value };

    private static async Task<object?> InvokeAsync(TraconTestHost host, string toolName, Dictionary<string, object?> arguments)
    {
        var tool = (AIFunction)host.Services.GetServices<TraconToolRegistration>()
            .Single(registration => string.Equals(registration.Function.Name, toolName, StringComparison.Ordinal))
            .Function;

        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            TenantId = "default",
            SessionId = "session-1",
            AgentName = "voice-agent",
        });

        try
        {
            // No Services on the arguments: the tool must work from what it took at setup.
            return await tool.InvokeAsync(new AIFunctionArguments(arguments, StringComparer.Ordinal), TestContext.Current.CancellationToken);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }
}
