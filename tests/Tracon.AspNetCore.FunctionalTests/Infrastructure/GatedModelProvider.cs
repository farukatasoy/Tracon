using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A model provider whose chat client answers only after the test opens a gate,
/// and answers with a fixed usage: the way a background child agent is made to
/// finish after the run that started it.
/// </summary>
/// <remarks>
/// The gate is a <see cref="TaskCompletionSource"/>, not a wall-clock delay: the
/// test decides when the child ends, so "the root closed first" holds by
/// construction and not by a race. The call ignores cancellation on purpose; the
/// child it serves is cut off only by what the test asks of it.
/// </remarks>
internal sealed class GatedModelProvider : IModelProvider
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _inputTokens;
    private readonly int _outputTokens;

    /// <summary>Creates the provider.</summary>
    /// <param name="name">The provider name.</param>
    /// <param name="model">The one model it serves, priced like the researcher of the tree tests.</param>
    /// <param name="inputTokens">The input tokens each answer reports.</param>
    /// <param name="outputTokens">The output tokens each answer reports.</param>
    public GatedModelProvider(string name, string model, int inputTokens = 4, int outputTokens = 6)
    {
        Name = name;
        _inputTokens = inputTokens;
        _outputTokens = outputTokens;
        Models =
        [
            new ModelDescriptor
            {
                Name = model,
                InputCostPerMillionTokens = 3_000m,
                OutputCostPerMillionTokens = 5_000m,
            },
        ];
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <summary>Completes once a chat client has been called at least once.</summary>
    public Task Started => _started.Task;

    /// <summary>Lets every waiting and every later call answer.</summary>
    public void Open() => _gate.TrySetResult();

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding) => new GatedChatClient(this);

    private sealed class GatedChatClient(GatedModelProvider provider) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            provider._started.TrySetResult();

            await provider._gate.Task.ConfigureAwait(false);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Sub task done"))
            {
                Usage = Usage(),
            };
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            provider._started.TrySetResult();

            await provider._gate.Task.ConfigureAwait(false);

            yield return new ChatResponseUpdate(ChatRole.Assistant, "Sub task done");
            yield return new ChatResponseUpdate { Contents = [new UsageContent(Usage())] };
        }

        private UsageDetails Usage()
            => new()
            {
                InputTokenCount = provider._inputTokens,
                OutputTokenCount = provider._outputTokens,
                TotalTokenCount = provider._inputTokens + provider._outputTokens,
            };

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The fake client has no resource to release.
        }
    }
}
