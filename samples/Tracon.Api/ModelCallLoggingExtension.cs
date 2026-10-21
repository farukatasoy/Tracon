using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Tracon;

namespace Tracon.Api;

/// <summary>
/// An agent extension with a chat client ring: it writes one log line for every model call of
/// the agents that select it (Phase 232, F-594).
/// </summary>
/// <remarks>
/// <para>
/// The ring sits in <see cref="ChatClientSlot.ModelCall"/>, inside the tool-call loop. A turn
/// that calls a tool writes two lines: one for the question and one for the call that takes the
/// tool result to the model. The content guard, the run budget and the <c>chat</c> span stay
/// below the ring; the circuit breaker, the fallback chain and error normalization stay above it.
/// </para>
/// <para>
/// The log line carries the role and the number of messages, never their text: a model call
/// carries the tenant's conversation.
/// </para>
/// </remarks>
internal sealed partial class ModelCallLoggingExtension : IAgentExtension
{
    /// <summary>The name a definition writes in <c>ExtensionNames</c>.</summary>
    public const string ExtensionName = "model-call-logging";

    /// <inheritdoc />
    public string Name => ExtensionName;

    /// <inheritdoc />
    public string? Description => "Writes one log line for every model call, inside the tool-call loop.";

    /// <inheritdoc />
    public void Configure(AgentExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var logger = context.Services.GetRequiredService<ILoggerFactory>().CreateLogger<ModelCallLoggingExtension>();
        var agent = context.Definition.Name;

        context.UseChatClient(ChatClientSlot.ModelCall, inner => new ModelCallLoggingChatClient(inner, logger, agent));
    }

    private sealed partial class ModelCallLoggingChatClient(IChatClient inner, ILogger logger, string agent) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var request = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
            Log(request);

            return base.GetResponseAsync(request, options, cancellationToken);
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var request = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
            Log(request);

            await foreach (var update in base.GetStreamingResponseAsync(request, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
        }

        private void Log(IReadOnlyList<ChatMessage> request)
            => ModelCall(logger, agent, request.Count == 0 ? "none" : request[^1].Role.Value, request.Count);

        [LoggerMessage(Level = LogLevel.Information, Message = "Model call of agent {Agent}: last message role {Role}, {MessageCount} messages.")]
        private static partial void ModelCall(ILogger logger, string agent, string role, int messageCount);
    }
}
