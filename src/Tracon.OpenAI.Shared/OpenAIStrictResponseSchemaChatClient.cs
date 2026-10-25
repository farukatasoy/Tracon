using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Asks OpenAI to enforce the response schema while the model generates (<c>strict: true</c>), and leaves
/// the tools out of it.
/// </summary>
/// <remarks>
/// <para>
/// The library reads one flag, <c>ChatOptions.AdditionalProperties["strict"]</c>, for the response format
/// <strong>and</strong> for every tool: <c>tool.AdditionalProperties</c> first, the options second. Writing
/// it on the options alone would turn every tool strict too, and the library would then rewrite each tool's
/// parameter schema. So each function declaration that did not choose for itself gets a wrapper whose
/// <c>strict</c> is <see langword="false"/>.
/// </para>
/// <para>
/// The decorator sits <strong>inside</strong> <c>UseFunctionInvocation()</c>. The function loop resolves a
/// tool call by the declarations it was given, not by what reaches the wire, so a wrapper changes only the
/// declaration the provider receives.
/// </para>
/// <para>
/// The caller's <see cref="ChatOptions"/> instance is <strong>never mutated</strong>: a compiled agent shares
/// one instance across all runs, and the fallback chain hands the same instance to the next provider, whose
/// request must not carry this provider's flag.
/// </para>
/// <para>
/// A request without a response schema passes through unchanged. A JSON-object format (no schema) has no
/// strict mode to ask for.
/// </para>
/// </remarks>
internal sealed class OpenAIStrictResponseSchemaChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    private const string StrictKey = "strict";

    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetResponseAsync(messages, Strict(options), cancellationToken);

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetStreamingResponseAsync(messages, Strict(options), cancellationToken);

    private static ChatOptions? Strict(ChatOptions? options)
    {
        if (options?.ResponseFormat is not ChatResponseFormatJson { Schema: not null })
        {
            return options;
        }

        var copy = options.Clone();

        copy.AdditionalProperties ??= [];
        copy.AdditionalProperties[StrictKey] = true;

        if (copy.Tools is { Count: > 0 } tools)
        {
            var declared = new List<AITool>(tools.Count);

            foreach (var tool in tools)
            {
                declared.Add(tool is AIFunctionDeclaration function && !function.AdditionalProperties.ContainsKey(StrictKey)
                    ? new NotStrictFunctionDeclaration(function)
                    : tool);
            }

            copy.Tools = declared;
        }

        return copy;
    }

    /// <summary>A function declaration the provider receives with <c>strict</c> set to <see langword="false"/>.</summary>
    /// <remarks>
    /// Declaration only: it cannot be invoked, and nothing invokes it, because the function loop above this
    /// decorator holds the invocable function. <see cref="GetService"/> reaches the original for any lookup
    /// that wants the function behind the declaration.
    /// </remarks>
    private sealed class NotStrictFunctionDeclaration : AIFunctionDeclaration
    {
        private readonly AIFunctionDeclaration _inner;
        private readonly Dictionary<string, object?> _additionalProperties;

        public NotStrictFunctionDeclaration(AIFunctionDeclaration inner)
        {
            _inner = inner;
            _additionalProperties = new Dictionary<string, object?>(inner.AdditionalProperties, StringComparer.Ordinal)
            {
                [StrictKey] = false,
            };
        }

        public override string Name => _inner.Name;

        public override string Description => _inner.Description;

        public override JsonElement JsonSchema => _inner.JsonSchema;

        public override JsonElement? ReturnJsonSchema => _inner.ReturnJsonSchema;

        public override IReadOnlyDictionary<string, object?> AdditionalProperties => _additionalProperties;

        public override object? GetService(Type serviceType, object? serviceKey = null)
        {
            ArgumentNullException.ThrowIfNull(serviceType);

            return serviceKey is null && serviceType.IsInstanceOfType(this)
                ? this
                : _inner.GetService(serviceType, serviceKey);
        }

        public override string ToString() => _inner.ToString();
    }
}
