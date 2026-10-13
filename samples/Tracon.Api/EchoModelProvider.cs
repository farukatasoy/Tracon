using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Tracon;

namespace Tracon.Api;

/// <summary>
/// Sample model provider that makes no network calls.
/// </summary>
/// <remarks>
/// <para>
/// It lets the sample run without an API key and demonstrates that the
/// <see cref="IModelProvider"/> extension point works end to end.
/// </para>
/// <para>
/// Calling <c>builder.AddTracon().UseOpenAI(apiKey)</c> replaces this provider;
/// agent definitions only change the <see cref="ModelBinding.Provider"/> value,
/// nothing else.
/// </para>
/// </remarks>
internal sealed class EchoModelProvider : IModelProvider
{
    public string Name => "echo";

    public IReadOnlyList<ModelDescriptor> Models { get; } =
    [
        new ModelDescriptor
        {
            Name = "echo-1",
            DisplayName = "Echo (local, no network)",
            ContextWindowTokens = 8_192,
            MaxOutputTokens = 1_024,
            SupportsTools = false,
        },

        // Shows the instructions the agent was given, so a run that binds
        // parameter values can be seen to carry them (the podcast demo agents).
        new ModelDescriptor
        {
            Name = EchoInstructionsModel,
            DisplayName = "Echo with instructions (local, no network)",
            ContextWindowTokens = 8_192,
            MaxOutputTokens = 1_024,
            SupportsTools = false,
        },

        // Answers as the reviewer of DraftReviewCriterion would: a failing
        // verdict on the first round, a passing one after. A loop that runs on it
        // takes exactly two iterations, with no network and no key.
        new ModelDescriptor
        {
            Name = EchoReviewerModel,
            DisplayName = "Echo draft reviewer (local, no network)",
            ContextWindowTokens = 8_192,
            MaxOutputTokens = 1_024,
            SupportsTools = false,
        },
    ];

    /// <summary>The model whose reply also names the instructions it received.</summary>
    public const string EchoInstructionsModel = "echo-instructions-1";

    /// <summary>The model that answers with a review verdict: fail on round 1, pass after.</summary>
    public const string EchoReviewerModel = "echo-reviewer-1";

    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new EchoChatClient(binding.Model);
    }

    private sealed class EchoChatClient : IChatClient
    {
        private readonly string _modelId;

        public EchoChatClient(string modelId) => _modelId = modelId;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var reply = BuildReply(messages, options);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
            {
                ModelId = _modelId,
                Usage = new UsageDetails
                {
                    InputTokenCount = reply.Length / 4,
                    OutputTokenCount = reply.Length / 4,
                    TotalTokenCount = reply.Length / 2,
                },
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var word in BuildReply(messages, options).Split(' '))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate(ChatRole.Assistant, word + ' ') { ModelId = _modelId };
                await Task.Delay(30, cancellationToken).ConfigureAwait(false);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The local client has no resources to release.
        }

        private string BuildReply(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            var lastUserMessage = messages.LastOrDefault(static message => message.Role == ChatRole.User);
            var text = lastUserMessage?.Text ?? "(empty request)";

            if (string.Equals(_modelId, EchoReviewerModel, StringComparison.Ordinal))
            {
                return text.Contains("Round 1.", StringComparison.Ordinal)
                    ? """{"decision":"fail","findings":[{"issue":"The draft has no heading.","fix":"Start the draft with a '# ' heading."}]}"""
                    : """{"decision":"pass","findings":[]}""";
            }

            return string.Equals(_modelId, EchoInstructionsModel, StringComparison.Ordinal)
                ? $"Echo [instructions: {options?.Instructions ?? "(none)"}]: {text}"
                : $"Echo: {text}";
        }
    }
}
