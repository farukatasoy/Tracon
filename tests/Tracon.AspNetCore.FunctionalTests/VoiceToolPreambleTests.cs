using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What the model says before it calls a tool is heard before the tool runs, not after it ends.
/// </summary>
/// <remarks>
/// <para>
/// A prompt that tells the model to say "one moment" before a lookup is only worth anything if the words reach the caller
/// while the lookup runs. The speech splitter holds back the last sentence until a character follows it, because it cannot tell
/// the end of a sentence from "3." until then. A tool call in the middle of an answer left that sentence waiting for a
/// character that arrives after the tool has finished.
/// </para>
/// <para>
/// 🚨 The case holds the tool open and reads what the synthesizer was asked to say <em>while it is held</em>. A case that read
/// the spoken text after the turn ended would pass for a sentence that was heard late.
/// </para>
/// </remarks>
public sealed class VoiceToolPreambleTests
{
    private const string Agent = "voice-agent";
    private const string Provider = "preamble";
    private const string LookupTool = "lookup_order";
    private const string Preamble = "One moment while I check";

    [Fact]
    public async Task The_words_before_a_tool_call_are_spoken_while_the_tool_runs_and_the_answer_after_it_is_a_sentence_of_its_own()
    {
        var voice = new StubVoiceProvider();
        var toolStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTool = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(new PreambleProvider(Provider, Preamble))
                .AddTool(
                    (Func<Task<string>>)(async () =>
                    {
                        toolStarted.TrySetResult();
                        await releaseTool.Task.ConfigureAwait(false);

                        return "shipped";
                    }),
                    name: LookupTool,
                    description: "Looks an order up.")
                .AddAgent(new AgentDefinition
                {
                    Name = Agent,
                    Description = "Talks to the customer.",
                    Instructions = "Say one moment before you look anything up.",
                    Model = new ModelBinding { Provider = Provider, Model = "m1" },
                    ToolNames = [LookupTool],
                    Origin = AgentDefinitionOrigin.Code,
                })
                .UseVoiceConversation(static options => options.OutputMediaType = "audio/mpeg"),
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(voice);
                services.AddSingleton<ISpeechSynthesizer>(voice);
            });

        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        var socket = await socketClient.ConnectAsync(
            new Uri("http://localhost/tracon/api/voice/sessions/session-1/stream"),
            TestContext.Current.CancellationToken);

        await using var client = new VoiceConversationClient(socket);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");
        await client.SendAudioAsync(new byte[3200]);
        await client.SendControlAsync(new { type = "commit" });

        await toolStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // The tool is held open. What was asked of the synthesizer so far is what the caller hears during the wait, and the audio
        // frame has to be on the caller's socket before the tool is released: a case that read the frames after the turn would
        // pass for a sentence that was heard late.
        await WaitUntil.TrueAsync(() => voice.Spoken.Contains(Preamble, StringComparer.Ordinal), "the words before the tool to be spoken");

        VoiceEvent heard;

        do
        {
            heard = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException("The connection closed before any audio arrived.");
        }
        while (!heard.IsAudio);

        releaseTool.Task.IsCompleted.ShouldBeFalse("the audio reached the caller while the tool was still held");

        releaseTool.TrySetResult();
        await client.WaitForAsync("done");

        voice.Spoken.ShouldBe([Preamble, "Done."], "the sentence after the tool is its own, not joined to the one before it");
    }

    /// <summary>A model that says a sentence with no full stop, calls a tool, and answers once it has the result.</summary>
    private sealed class PreambleProvider(string name, string preamble) : IModelProvider
    {
        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "m1" }];

        public IChatClient CreateChatClient(ModelBinding binding) => new Client(preamble);

        private sealed class Client(string preamble) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => throw new NotSupportedException("The voice conversation streams.");

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                var hasToolResult = messages.Any(static message => message.Contents.Any(static content => content is FunctionResultContent));

                if (hasToolResult)
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, "Done.");

                    yield break;
                }

                yield return new ChatResponseUpdate(ChatRole.Assistant, preamble);
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-1", LookupTool, new Dictionary<string, object?>(StringComparer.Ordinal))]);

                await Task.CompletedTask.ConfigureAwait(false);
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
            }
        }
    }
}
