// MEAI001: Microsoft.Extensions.AI marks its image and speech clients "experimental" (10.11.0).
#pragma warning disable MEAI001

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Drives every Microsoft.Extensions.AI ring and the Microsoft Agent Framework workflow telemetry with
/// <c>EnableSensitiveData = true</c> and a sentinel in every content field, from inside a run.
/// </summary>
/// <remarks>
/// <para>
/// The rings are built here, by the test, with the same type and source name the host uses. The point is the
/// filter between the span and the store: a ring written by the library the consumer turned sensitive data on
/// for (the environment variable the library reads) hands the store whatever keys it likes, and the list of keys
/// the filter drops can only be proven complete against the real rings. A key a library upgrade adds shows up here
/// as a sentinel in the stored span.
/// </para>
/// <para>
/// Called from a tool, so every span is a descendant of the run's root span and the collector gives it to the run.
/// </para>
/// </remarks>
internal static class SensitiveContentProbe
{
    public const string SystemInstructions = "SYSTEM-INSTRUCTIONS-SENTINEL";
    public const string UserMessage = "USER-MESSAGE-SENTINEL";
    public const string ToolArguments = "TOOL-ARGUMENTS-SENTINEL";
    public const string ToolResult = "TOOL-RESULT-SENTINEL";
    public const string Answer = "MODEL-ANSWER-SENTINEL";
    public const string EmbeddingInput = "EMBEDDING-INPUT-SENTINEL";
    public const string ImagePrompt = "IMAGE-PROMPT-SENTINEL";
    public const string SpeechText = "SPEECH-TEXT-SENTINEL";
    public const string Transcript = "TRANSCRIPT-SENTINEL";
    public const string ExecutorInput = "EXECUTOR-INPUT-SENTINEL";
    public const string ExecutorOutput = "EXECUTOR-OUTPUT-SENTINEL";

    /// <summary>Every sentinel the probe writes.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        SystemInstructions,
        UserMessage,
        ToolArguments,
        ToolResult,
        Answer,
        EmbeddingInput,
        ImagePrompt,
        SpeechText,
        Transcript,
        ExecutorInput,
        ExecutorOutput,
    ];

    /// <summary>Runs every ring once.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when every ring has produced its spans.</returns>
    public static async Task EmitAsync(CancellationToken cancellationToken)
    {
        await EmitChatAsync(cancellationToken);
        await EmitEmbeddingAsync(cancellationToken);
        await EmitImageAsync(cancellationToken);
        await EmitSpeechAsync(cancellationToken);
        await EmitWorkflowAsync(cancellationToken);
    }

    private static async Task EmitChatAsync(CancellationToken cancellationToken)
    {
        using var telemetry = new OpenTelemetryChatClient(new ScriptedChat(), NullLogger.Instance, TraconDiagnostics.ActivitySourceName)
        {
            EnableSensitiveData = true,
        };

        // The same order the host builds: the tool loop above the telemetry ring.
        using var chat = new FunctionInvokingChatClient(telemetry);

        var echo = AIFunctionFactory.Create(
            static (string text) => ToolResult,
            "echo_probe",
            "Echoes a fixed value.");

        _ = await chat.GetResponseAsync(
            [new ChatMessage(ChatRole.User, UserMessage)],
            new ChatOptions { Instructions = SystemInstructions, Tools = [echo] },
            cancellationToken);
    }

    private static async Task EmitEmbeddingAsync(CancellationToken cancellationToken)
    {
        using var embeddings = new EmbeddingGeneratorBuilder<string, Embedding<float>>(new ConstantEmbeddings())
            .UseOpenTelemetry(NullLoggerFactory.Instance, TraconDiagnostics.ActivitySourceName, static ring => ring.EnableSensitiveData = true)
            .Build();

        _ = await embeddings.GenerateAsync([EmbeddingInput], cancellationToken: cancellationToken);
    }

    private static async Task EmitImageAsync(CancellationToken cancellationToken)
    {
        using var images = new ImageGeneratorBuilder(new ConstantImages())
            .UseOpenTelemetry(NullLoggerFactory.Instance, TraconDiagnostics.ActivitySourceName, static ring => ring.EnableSensitiveData = true)
            .Build();

        _ = await images.GenerateAsync(new ImageGenerationRequest(ImagePrompt), cancellationToken: cancellationToken);
    }

    private static async Task EmitSpeechAsync(CancellationToken cancellationToken)
    {
        using var synthesizer = new TextToSpeechClientBuilder(new ConstantSynthesizer())
            .UseOpenTelemetry(NullLoggerFactory.Instance, TraconDiagnostics.ActivitySourceName, static ring => ring.EnableSensitiveData = true)
            .Build();

        _ = await synthesizer.GetAudioAsync(SpeechText, cancellationToken: cancellationToken);

        using var transcriber = new SpeechToTextClientBuilder(new ConstantTranscriber())
            .UseOpenTelemetry(NullLoggerFactory.Instance, TraconDiagnostics.ActivitySourceName, static ring => ring.EnableSensitiveData = true)
            .Build();

        using var audio = new MemoryStream([1, 2, 3]);

        _ = await transcriber.GetTextAsync(audio, cancellationToken: cancellationToken);
    }

    private static async Task EmitWorkflowAsync(CancellationToken cancellationToken)
    {
        var step = ExecutorBindingExtensions.BindAsExecutor<string, string>(static _ => ExecutorOutput, id: "probe-step");

        // The source is the Tracon one, the way the host's own definition workflows hand it over.
        using var source = new System.Diagnostics.ActivitySource(TraconDiagnostics.ActivitySourceName);

        var workflow = new WorkflowBuilder(step)
            .WithOutputFrom(step)
            .WithOpenTelemetry(static options => options.EnableSensitiveData = true, source)
            .Build();

        await using var run = await InProcessExecution.RunAsync(workflow, ExecutorInput, cancellationToken: cancellationToken);
    }

    private sealed class ScriptedChat : IChatClient
    {
        private int _calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var message = Interlocked.Increment(ref _calls) == 1
                ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "echo_probe", new Dictionary<string, object?>(StringComparer.Ordinal) { ["text"] = ToolArguments })])
                : new ChatMessage(ChatRole.Assistant, Answer);

            return Task.FromResult(new ChatResponse(message));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The probe uses the non-streaming path.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ConstantEmbeddings : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                values.Select(static _ => new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f }))));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ConstantImages : IImageGenerator
    {
        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageGenerationResponse([new DataContent(new byte[] { 1, 2, 3 }, "image/png")]));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ConstantSynthesizer : ITextToSpeechClient
    {
        public Task<TextToSpeechResponse> GetAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new TextToSpeechResponse([new DataContent(new byte[] { 1, 2, 3 }, "audio/mpeg")]));

        public IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The probe uses the non-streaming path.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ConstantTranscriber : ISpeechToTextClient
    {
        public Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SpeechToTextResponse(Transcript));

        public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The probe uses the non-streaming path.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
