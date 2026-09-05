using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A model provider whose chat client never returns on its own — used to
/// exercise <see cref="ChildAgentInvoker"/>'s two-layer wait limit (144.1)
/// through a REAL <c>BackgroundAgentsProvider</c> tool-call flow, not a
/// hand-built invoker.
/// </summary>
/// <remarks>
/// Two modes: <c>respectsCancellation: true</c> genuinely stops when its
/// incoming token is canceled (the cooperative layer 1 case); <c>false</c>
/// ignores the token entirely and only resolves when <see cref="Release"/>
/// is called (the hard-cutoff layer 2 case) — a deterministic
/// <see cref="TaskCompletionSource"/> gate, not a wall-clock <c>Task.Delay</c>.
/// </remarks>
internal sealed class HangingModelProvider(string name, bool respectsCancellation) : IModelProvider
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "hanging-1" }];

    /// <summary>Completes once the chat client has been called at least once.</summary>
    public Task Started => _started.Task;

    /// <summary>Lets a call that ignores cancellation finally resolve, with the given text.</summary>
    public void Release(string text) => _release.TrySetResult(text);

    public IChatClient CreateChatClient(ModelBinding binding) => new HangingChatClient(_started, _release, respectsCancellation);

    private sealed class HangingChatClient(
        TaskCompletionSource started,
        TaskCompletionSource<string> release,
        bool respectsCancellation) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();

            var text = respectsCancellation
                ? await WaitForCancellationAsync(cancellationToken).ConfigureAwait(false)
                : await release.Task.ConfigureAwait(false);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            started.TrySetResult();

            var text = respectsCancellation
                ? await WaitForCancellationAsync(cancellationToken).ConfigureAwait(false)
                : await release.Task.ConfigureAwait(false);

            yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        }

        private static async Task<string> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

            return "unreachable";
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The fake client has no resource to release.
        }
    }
}
