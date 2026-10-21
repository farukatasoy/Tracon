using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon.Samples.MafFeatures;

/// <summary>
/// Registers an agent extension that puts a chat client ring into the model pipeline of an agent
/// that is defined as data.
/// </summary>
public static class ModelCallLogExtensionRegistration
{
    /// <summary>The name a definition writes in <c>ExtensionNames</c>.</summary>
    public const string ExtensionName = "model-call-log";

    /// <summary>Adds the extension and the log it writes to the Tracon chain.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The same chain.</returns>
    /// <remarks>
    /// The ring sits inside the tool-call loop, so it sees every model call of a turn, including
    /// the call that takes a tool result to the model. The content guard, the run budget, and the
    /// <c>chat</c> span stay below it; the circuit breaker and the fallback chain stay above it.
    /// </remarks>
    public static ITraconBuilder AddModelCallLog(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<ModelCallLog>();

        return builder.AddAgentExtension(
            ExtensionName,
            context =>
            {
                var log = context.Services.GetRequiredService<ModelCallLog>();
                var agent = context.Definition.Name;

                context.UseChatClient(ChatClientSlot.ModelCall, inner => new ModelCallLogChatClient(inner, log, agent));
            },
            description: "Logs the role of the last message of every model call.");
    }

    // Derive a ring from DelegatingChatClient: it passes the calls and the service lookups that
    // it does not handle to the client below it.
    private sealed class ModelCallLogChatClient(IChatClient inner, ModelCallLog log, string agent) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var request = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
            Record(request);

            return base.GetResponseAsync(request, options, cancellationToken);
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var request = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
            Record(request);

            await foreach (var update in base.GetStreamingResponseAsync(request, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
        }

        private void Record(IReadOnlyList<ChatMessage> request)
        {
            if (request.Count > 0)
            {
                log.Record(agent, request[^1].Role.Value);
            }
        }
    }
}
